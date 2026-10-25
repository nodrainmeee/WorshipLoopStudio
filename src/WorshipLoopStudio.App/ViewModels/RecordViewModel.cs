using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using NAudio.Wave;
using WorshipLoopStudio.App.Infrastructure;
using WorshipLoopStudio.App.Services;
using WorshipLoopStudio.Audio.Devices;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Audio.Recording;
using WorshipLoopStudio.Core.Editing;
using WorshipLoopStudio.Core.Samples;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.App.ViewModels;

public enum RecordDurationMode
{
    UntilStopped,
    Seconds,
    Bars,
}

public sealed record RecordingItem(string Path, string Name, DateTime Created)
{
    public string Subtitle => Created.ToString("ddd dd MMM HH:mm");
}

/// <summary>Microphone / interface recording, plus the clip editor (trim, loop points, save as sample).</summary>
public sealed class RecordViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly PerformViewModel _perform;
    private AudioDeviceInfo? _selectedInput;
    private bool _isInputOpen;
    private string _inputStatus = "Input closed. Choose a device and click 'Open input'.";
    private double _inputGainDb;
    private double _inputLevel;
    private bool _isClipping;
    private int _clipHold;
    private bool _monitorEnabled;
    private ISampleProvider? _monitorInMixer;
    private OptionItem<RecordDurationMode> _durationMode;
    private double _durationSeconds = 8;
    private int _durationBars = 4;
    private bool _startPlaybackWithRecording = true;
    private bool _isRecording;
    private string _elapsedText = "00:00.0";
    private RecordingItem? _selectedRecording;
    private CachedSample? _editorSample;
    private float[] _peaks = Array.Empty<float>();
    private double _duration;
    private double _trimStart;
    private double _trimEnd;
    private bool _isEditorBusy;

    public RecordViewModel(AppServices services, PerformViewModel perform)
    {
        _s = services;
        _perform = perform;
        DurationModes = new[]
        {
            new OptionItem<RecordDurationMode>(RecordDurationMode.UntilStopped, "Until I press Stop"),
            new OptionItem<RecordDurationMode>(RecordDurationMode.Seconds, "Fixed length (seconds)"),
            new OptionItem<RecordDurationMode>(RecordDurationMode.Bars, "Bars at current tempo"),
        };
        _durationMode = DurationModes[0];

        OpenInputCommand = new RelayCommand(OpenInput);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        StartRecordingCommand = new RelayCommand(StartRecording, () => !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        OpenPrivacySettingsCommand = new RelayCommand(() => Dialogs.OpenUri("ms-settings:privacy-microphone"));
        OpenSoundSettingsCommand = new RelayCommand(() => Dialogs.OpenUri("ms-settings:sound"));
        ImportAudioCommand = new AsyncRelayCommand(ImportAudioAsync);
        DeleteRecordingCommand = new RelayCommand(DeleteRecording, () => SelectedRecording is not null);
        PreviewSelectionCommand = new RelayCommand(PreviewSelection, () => _editorSample is not null);
        StopPreviewCommand = new RelayCommand(() => _s.Engine.StopPreview());
        SnapToBarsCommand = new RelayCommand(SnapToBars, () => _editorSample is not null);
        ResetTrimCommand = new RelayCommand(() => { TrimStart = 0; TrimEnd = Duration; }, () => _editorSample is not null);
        SaveAsSampleCommand = new AsyncRelayCommand(SaveAsSampleAsync, () => _editorSample is not null && !IsEditorBusy);
        AddToProjectCommand = new AsyncRelayCommand(AddToProjectAsync, () => _editorSample is not null && !IsEditorBusy);
        ShowInFolderCommand = new RelayCommand(() => Dialogs.OpenInExplorer(SelectedRecording?.Path ?? _s.Paths.Recordings));

        _s.Recorder.RecordingCompleted += (_, e) => Application.Current?.Dispatcher.InvokeAsync(() => OnRecordingCompleted(e));
        _s.Recorder.InputFailed += (_, message) => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsRecording = false;
            IsInputOpen = false;
            InputStatus = message;
            ErrorReporter.Report(message, null);
        });
    }

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<RecordingItem> Recordings { get; } = new();
    public IReadOnlyList<OptionItem<RecordDurationMode>> DurationModes { get; }
    public IReadOnlyList<int> BarChoices { get; } = new[] { 1, 2, 4, 8, 16 };

    public ICommand OpenInputCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand OpenPrivacySettingsCommand { get; }
    public ICommand OpenSoundSettingsCommand { get; }
    public ICommand ImportAudioCommand { get; }
    public ICommand DeleteRecordingCommand { get; }
    public ICommand PreviewSelectionCommand { get; }
    public ICommand StopPreviewCommand { get; }
    public ICommand SnapToBarsCommand { get; }
    public ICommand ResetTrimCommand { get; }
    public ICommand SaveAsSampleCommand { get; }
    public ICommand AddToProjectCommand { get; }
    public ICommand ShowInFolderCommand { get; }

    public AudioDeviceInfo? SelectedInput
    {
        get => _selectedInput;
        set
        {
            if (SetProperty(ref _selectedInput, value) && value is not null)
            {
                _s.Settings.InputDeviceId = value.Id;
                if (IsInputOpen)
                {
                    OpenInput();
                }
            }
        }
    }

    public bool IsInputOpen
    {
        get => _isInputOpen;
        private set => SetProperty(ref _isInputOpen, value);
    }

    public string InputStatus
    {
        get => _inputStatus;
        private set => SetProperty(ref _inputStatus, value);
    }

    public double InputGainDb
    {
        get => _inputGainDb;
        set
        {
            if (SetProperty(ref _inputGainDb, Math.Clamp(value, -24, 24)))
            {
                _s.Recorder.InputGain = (float)Math.Pow(10, _inputGainDb / 20);
                OnPropertyChanged(nameof(InputGainText));
            }
        }
    }

    public string InputGainText => $"{InputGainDb:+0.0;-0.0;0.0} dB";

    public double InputLevel
    {
        get => _inputLevel;
        private set => SetProperty(ref _inputLevel, value);
    }

    public bool IsClipping
    {
        get => _isClipping;
        private set => SetProperty(ref _isClipping, value);
    }

    public bool MonitorEnabled
    {
        get => _monitorEnabled;
        set
        {
            if (SetProperty(ref _monitorEnabled, value))
            {
                ApplyMonitoring();
            }
        }
    }

    public OptionItem<RecordDurationMode> SelectedDurationMode
    {
        get => _durationMode;
        set
        {
            if (value is not null && SetProperty(ref _durationMode, value))
            {
                OnPropertyChanged(nameof(IsSecondsMode));
                OnPropertyChanged(nameof(IsBarsMode));
            }
        }
    }

    public bool IsSecondsMode => SelectedDurationMode.Value == RecordDurationMode.Seconds;
    public bool IsBarsMode => SelectedDurationMode.Value == RecordDurationMode.Bars;

    public double DurationSeconds
    {
        get => _durationSeconds;
        set => SetProperty(ref _durationSeconds, Math.Clamp(value, 1, 1800));
    }

    public int DurationBars
    {
        get => _durationBars;
        set => SetProperty(ref _durationBars, Math.Clamp(value, 1, 64));
    }

    public bool StartPlaybackWithRecording
    {
        get => _startPlaybackWithRecording;
        set => SetProperty(ref _startPlaybackWithRecording, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set => SetProperty(ref _isRecording, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public RecordingItem? SelectedRecording
    {
        get => _selectedRecording;
        set
        {
            if (SetProperty(ref _selectedRecording, value))
            {
                _ = LoadEditorAsync(value);
            }
        }
    }

    public float[] Peaks
    {
        get => _peaks;
        private set => SetProperty(ref _peaks, value);
    }

    public double Duration
    {
        get => _duration;
        private set => SetProperty(ref _duration, value);
    }

    public double TrimStart
    {
        get => _trimStart;
        set
        {
            var region = LoopRegion.Clamp(value, Math.Max(value, _trimEnd), Duration);
            if (SetProperty(ref _trimStart, region.StartSeconds))
            {
                if (_trimEnd < region.EndSeconds)
                {
                    _trimEnd = region.EndSeconds;
                    OnPropertyChanged(nameof(TrimEnd));
                }

                OnTrimChanged();
            }
        }
    }

    public double TrimEnd
    {
        get => _trimEnd;
        set
        {
            var region = LoopRegion.Clamp(Math.Min(_trimStart, value), value, Duration);
            if (SetProperty(ref _trimEnd, region.EndSeconds))
            {
                if (_trimStart > region.StartSeconds)
                {
                    _trimStart = region.StartSeconds;
                    OnPropertyChanged(nameof(TrimStart));
                }

                OnTrimChanged();
            }
        }
    }

    public double SelectionStartFraction => Duration > 0 ? TrimStart / Duration : 0;
    public double SelectionEndFraction => Duration > 0 ? TrimEnd / Duration : 1;

    public string TrimText
    {
        get
        {
            var length = TrimEnd - TrimStart;
            var bar = TempoMath.BarDurationSeconds(_perform.Bpm, _perform.SelectedTimeSignature);
            return $"Start {TrimStart:0.000} s   End {TrimEnd:0.000} s   Length {length:0.000} s ({length / bar:0.00} bars at {_perform.Bpm:0.#} BPM)";
        }
    }

    public bool HasEditorClip => _editorSample is not null;

    public bool IsEditorBusy
    {
        get => _isEditorBusy;
        private set => SetProperty(ref _isEditorBusy, value);
    }

    // ---------------------------------------------------------------- devices

    public void Initialize()
    {
        RefreshDevices();
        RefreshRecordings();
    }

    private void RefreshDevices()
    {
        var current = SelectedInput?.Id ?? _s.Settings.InputDeviceId;
        InputDevices.Clear();
        foreach (var d in RecordingService.GetInputDevices())
        {
            InputDevices.Add(d);
        }

        _selectedInput = InputDevices.FirstOrDefault(d => d.Id == current) ?? InputDevices.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedInput));
    }

    public void OpenInput()
    {
        RemoveMonitorFromMixer();
        try
        {
            _s.Recorder.OpenInput(SelectedInput?.Id);
            IsInputOpen = true;
            var f = _s.Recorder.CaptureFormat;
            InputStatus = $"Listening to {_s.Recorder.InputName}" + (f is null ? string.Empty : $" ({f.SampleRate} Hz, {f.Channels} ch)");
            ApplyMonitoring();
        }
        catch (Exception ex)
        {
            IsInputOpen = false;
            InputStatus = ex.Message;
            _s.Log.Warn("Input open failed", ex);
            ErrorReporter.Report(ex.Message, null);
        }
    }

    private void ApplyMonitoring()
    {
        _s.Recorder.MonitorEnabled = MonitorEnabled;
        RemoveMonitorFromMixer();
        if (MonitorEnabled && _s.Recorder.MonitorProvider is { } provider)
        {
            _s.Output.Mixer.AddMixerInput(provider);
            _monitorInMixer = provider;
        }
    }

    private void RemoveMonitorFromMixer()
    {
        if (_monitorInMixer is not null)
        {
            _s.Output.Mixer.RemoveMixerInput(_monitorInMixer);
            _monitorInMixer = null;
        }
    }

    // ---------------------------------------------------------------- recording

    private void StartRecording()
    {
        if (!IsInputOpen)
        {
            OpenInput();
            if (!IsInputOpen)
            {
                return;
            }
        }

        TimeSpan? max = SelectedDurationMode.Value switch
        {
            RecordDurationMode.Seconds => TimeSpan.FromSeconds(DurationSeconds),
            RecordDurationMode.Bars => TimeSpan.FromSeconds(TempoMath.BarDurationSeconds(_perform.Bpm, _perform.SelectedTimeSignature) * DurationBars),
            _ => null,
        };

        var path = Path.Combine(_s.Paths.Recordings, $"Recording {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav");
        try
        {
            _s.Recorder.StartRecording(path, max);
            IsRecording = true;
            InputStatus = max is null ? "Recording... press Stop when finished." : $"Recording {max.Value.TotalSeconds:0.0} s...";
            if (StartPlaybackWithRecording && _perform.IsStopped)
            {
                _perform.Play();
            }
        }
        catch (Exception ex)
        {
            IsRecording = false;
            ErrorReporter.Report("Recording could not start", ex);
        }
    }

    private void StopRecording()
    {
        _s.Recorder.StopRecording();
    }

    private void OnRecordingCompleted(RecordingCompletedEventArgs e)
    {
        IsRecording = false;
        if (e.Error is not null)
        {
            InputStatus = $"Recording stopped with an error: {e.Error.Message}";
            ErrorReporter.Report("Recording failed", e.Error);
        }
        else
        {
            InputStatus = $"Saved {Path.GetFileName(e.Path)} ({e.Duration.TotalSeconds:0.0} s). Trim it below, then save it as a sample or add it to the project.";
        }

        _perform.Project.RecordingReferences.Add(e.Path);
        RefreshRecordings();
        SelectedRecording = Recordings.FirstOrDefault(r => string.Equals(r.Path, e.Path, StringComparison.OrdinalIgnoreCase));
        _perform.RefreshLibrary();
    }

    public void RefreshRecordings()
    {
        var selected = SelectedRecording?.Path;
        Recordings.Clear();
        if (Directory.Exists(_s.Paths.Recordings))
        {
            foreach (var file in Directory.EnumerateFiles(_s.Paths.Recordings).Where(SampleLibrary.IsSupportedAudioFile)
                         .OrderByDescending(f => File.GetCreationTime(f)))
            {
                Recordings.Add(new RecordingItem(file, Path.GetFileNameWithoutExtension(file), File.GetCreationTime(file)));
            }
        }

        _selectedRecording = Recordings.FirstOrDefault(r => r.Path == selected);
        OnPropertyChanged(nameof(SelectedRecording));
    }

    public void Tick()
    {
        var peak = _s.Recorder.ReadAndResetPeak();
        InputLevel = Math.Max(peak, InputLevel * 0.85);
        if (peak >= 0.99f)
        {
            _clipHold = 45;
        }

        if (_clipHold > 0)
        {
            _clipHold--;
        }

        IsClipping = _clipHold > 0;
        if (IsRecording)
        {
            ElapsedText = _s.Recorder.Elapsed.ToString(@"mm\:ss\.f");
        }
    }

    // ---------------------------------------------------------------- import & editor

    private async Task ImportAudioAsync()
    {
        var file = Dialogs.OpenFile("Import audio to edit", SampleLibrary.FileDialogFilter);
        if (file is null)
        {
            return;
        }

        await ImportForEditingAsync(file);
    }

    /// <summary>Copies an audio file into the Recordings folder and opens it in the clip editor.</summary>
    public async Task ImportForEditingAsync(string file)
    {
        await Task.Run(() => SampleLoader.Load(file, TimeSpan.FromMinutes(20)));
        var target = PerformViewModel.UniqueFile(_s.Paths.Recordings, Path.GetFileName(file));
        await Task.Run(() => File.Copy(file, target));
        RefreshRecordings();
        SelectedRecording = Recordings.FirstOrDefault(r => r.Path == target);
    }

    private void DeleteRecording()
    {
        var item = SelectedRecording;
        if (item is null || !Dialogs.Confirm("Delete recording", $"Delete '{item.Name}' permanently?"))
        {
            return;
        }

        _s.Engine.StopPreview();
        _editorSample = null;
        _s.Samples.Invalidate(item.Path);
        File.Delete(item.Path);
        RefreshRecordings();
        _ = LoadEditorAsync(null);
        _perform.RefreshLibrary();
    }

    private async Task LoadEditorAsync(RecordingItem? item)
    {
        _editorSample = null;
        Peaks = Array.Empty<float>();
        Duration = 0;
        _trimStart = 0;
        _trimEnd = 0;
        OnTrimChanged();
        OnPropertyChanged(nameof(HasEditorClip));
        if (item is null)
        {
            return;
        }

        IsEditorBusy = true;
        try
        {
            var sample = await Task.Run(() => SampleLoader.Load(item.Path, TimeSpan.FromMinutes(20)));
            if (SelectedRecording != item)
            {
                return;
            }

            var peaks = await Task.Run(() => WaveformPeaks.Compute(sample.Data, EngineFormat.Channels, 1600));
            _editorSample = sample;
            Peaks = peaks;
            Duration = sample.DurationSeconds;
            _trimStart = 0;
            _trimEnd = Duration;
            OnTrimChanged();
            OnPropertyChanged(nameof(HasEditorClip));
        }
        catch (Exception ex)
        {
            ErrorReporter.Report($"'{item.Name}' could not be opened", ex);
        }
        finally
        {
            IsEditorBusy = false;
        }
    }

    private void OnTrimChanged()
    {
        OnPropertyChanged(nameof(TrimStart));
        OnPropertyChanged(nameof(TrimEnd));
        OnPropertyChanged(nameof(SelectionStartFraction));
        OnPropertyChanged(nameof(SelectionEndFraction));
        OnPropertyChanged(nameof(TrimText));
    }

    private CachedSample? TrimmedClip()
    {
        if (_editorSample is null)
        {
            return null;
        }

        var region = LoopRegion.Clamp(TrimStart, TrimEnd, Duration);
        var data = AudioEdit.Trim(_editorSample.Data, EngineFormat.Channels, EngineFormat.SampleRate, region);
        return new CachedSample(_editorSample.Path, data);
    }

    private void PreviewSelection()
    {
        if (TrimmedClip() is { } clip)
        {
            _s.Engine.Preview(clip, 0.9f);
        }
    }

    /// <summary>Rounds the selection length to whole bars at the project tempo, so the clip loops in time.</summary>
    private void SnapToBars()
    {
        var bar = TempoMath.BarDurationSeconds(_perform.Bpm, _perform.SelectedTimeSignature);
        var length = TrimEnd - TrimStart;
        var bars = Math.Max(1, Math.Round(length / bar));
        var end = TrimStart + bars * bar;
        while (end > Duration && bars > 1)
        {
            bars--;
            end = TrimStart + bars * bar;
        }

        TrimEnd = Math.Min(end, Duration);
    }

    private async Task<string?> SaveTrimmedAsync(string folder, string title)
    {
        if (_editorSample is null)
        {
            return null;
        }

        var name = Dialogs.AskText(title, "Name:", SelectedRecording?.Name ?? "My loop");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var safe = Core.Projects.ProjectStore.SanitizeName(name);
        var path = PerformViewModel.UniqueFile(folder, safe + ".wav");
        var sample = _editorSample;
        var region = LoopRegion.Clamp(TrimStart, TrimEnd, Duration);
        IsEditorBusy = true;
        try
        {
            await Task.Run(() => AudioExporter.SaveTrimmed(sample, region, path));
        }
        finally
        {
            IsEditorBusy = false;
        }

        return path;
    }

    private async Task SaveAsSampleAsync()
    {
        var path = await SaveTrimmedAsync(_s.Paths.UserSamples, "Save as sample");
        if (path is not null)
        {
            _perform.RefreshLibrary();
            InputStatus = $"Saved sample '{Path.GetFileNameWithoutExtension(path)}' (category: My Samples).";
        }
    }

    private async Task AddToProjectAsync()
    {
        var path = await SaveTrimmedAsync(_s.Paths.Loops, "Add to project as loop");
        if (path is not null)
        {
            _perform.AddAudioLoopTrack(path, Path.GetFileNameWithoutExtension(path));
            InputStatus = $"Added '{Path.GetFileNameWithoutExtension(path)}' to the project as an audio loop track.";
        }
    }
}
