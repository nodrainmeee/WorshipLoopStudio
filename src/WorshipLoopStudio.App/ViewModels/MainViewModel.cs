using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WorshipLoopStudio.App.Infrastructure;
using WorshipLoopStudio.App.Services;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Core.Projects;

namespace WorshipLoopStudio.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _deviceCheckTimer;
    private string _statusMessage = "Ready.";
    private string? _errorMessage;
    private bool _audioOk;
    private string _audioStatusText = "Audio: starting...";
    private int _selectedTabIndex;
    private bool _recordInitialized;
    private bool _isExporting;
    private double _exportProgress;
    private string? _lastExportPath;

    public MainViewModel(AppServices services)
    {
        _s = services;
        Perform = new PerformViewModel(services);
        Record = new RecordViewModel(services, Perform);
        Separate = new SeparateViewModel(services, Record);
        Settings = new SettingsViewModel(services);
        Perform.DirtyChanged += (_, _) => OnPropertyChanged(nameof(WindowTitle));
        Perform.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PerformViewModel.ProjectName) or "")
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        };
        Separate.RequestRecordTab += (_, _) => SelectedTabIndex = 1;

        NewProjectCommand = new RelayCommand(NewProject);
        OpenProjectCommand = new RelayCommand(OpenProject);
        SaveProjectCommand = new RelayCommand(() => SaveProject());
        SaveProjectAsCommand = new RelayCommand(() => SaveProjectAs());
        RenameProjectCommand = new RelayCommand(RenameProject);
        ExportMixCommand = new AsyncRelayCommand(() => ExportAsync(null), () => !IsExporting);
        ExportTrackCommand = new AsyncRelayCommand(() => ExportAsync(Perform.SelectedTrack), () => !IsExporting && Perform.SelectedTrack is not null);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = null);
        ShowLastExportCommand = new RelayCommand(() => Dialogs.OpenInExplorer(_lastExportPath ?? _s.Paths.Exports));

        ErrorReporter.Handler = ShowError;
        _s.Output.PlaybackFailed += (_, e) => Application.Current?.Dispatcher.InvokeAsync(() => OnAudioFailed(e.Message));
        _s.Output.DevicesChanged += (_, _) => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _deviceCheckTimer?.Stop();
            _deviceCheckTimer?.Start();
        });

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += (_, _) => Tick();
        _deviceCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _deviceCheckTimer.Tick += (_, _) =>
        {
            _deviceCheckTimer.Stop();
            CheckDevice();
        };
    }

    public PerformViewModel Perform { get; }
    public RecordViewModel Record { get; }
    public SeparateViewModel Separate { get; }
    public SettingsViewModel Settings { get; }

    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand SaveProjectAsCommand { get; }
    public ICommand RenameProjectCommand { get; }
    public ICommand ExportMixCommand { get; }
    public ICommand ExportTrackCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public ICommand ShowLastExportCommand { get; }

    public string WindowTitle => $"Worship Loop Studio - {Perform.ProjectName}{(Perform.IsDirty ? " *" : string.Empty)}";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool AudioOk
    {
        get => _audioOk;
        private set => SetProperty(ref _audioOk, value);
    }

    public string AudioStatusText
    {
        get => _audioStatusText;
        private set => SetProperty(ref _audioStatusText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                if (value == 1 && !_recordInitialized)
                {
                    _recordInitialized = true;
                    Record.OpenInput();
                }

                if (value == 3)
                {
                    Settings.RefreshStatus();
                }
            }
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set => SetProperty(ref _isExporting, value);
    }

    public double ExportProgress
    {
        get => _exportProgress;
        private set => SetProperty(ref _exportProgress, value);
    }

    public string DataFolder => _s.Paths.Root;

    // ---------------------------------------------------------------- startup

    public async Task InitializeAsync()
    {
        StatusMessage = "Preparing the starter instrument library...";
        try
        {
            var generated = await Task.Run(() => _s.InstallStarterSamples());
            if (generated > 0)
            {
                _s.Log.Info($"Generated {generated} starter samples.");
            }
        }
        catch (Exception ex)
        {
            ShowError("The starter samples could not be created", ex);
        }

        Perform.RefreshLibrary();
        Record.Initialize();

        var warning = _s.StartAudioOutput();
        UpdateAudioStatus();
        if (!string.IsNullOrEmpty(warning))
        {
            ShowError(warning, null);
        }

        foreach (var w in _s.StartupWarnings)
        {
            ShowError(w, null);
        }

        LoadStartupProject();
        _uiTimer.Start();
        StatusMessage = "Ready. Space = play/stop, 1-9 = trigger tracks, Ctrl+S = save.";
        _ = Separate.CheckAsync();
    }

    private void LoadStartupProject()
    {
        var last = _s.Settings.LastProjectPath;
        if (!string.IsNullOrEmpty(last) && File.Exists(last))
        {
            try
            {
                Perform.LoadProject(ProjectSerializer.Load(last), last);
                return;
            }
            catch (Exception ex)
            {
                ShowError($"The last project could not be opened ({Path.GetFileName(last)})", ex);
            }
        }

        var path = _s.Projects.UniquePathFor("My First Loop");
        var project = Project.CreateDefault(Path.GetFileNameWithoutExtension(path));
        try
        {
            ProjectSerializer.Save(project, path);
            _s.Settings.LastProjectPath = path;
        }
        catch (Exception ex)
        {
            ShowError("The starter project could not be saved", ex);
            path = null!;
        }

        Perform.LoadProject(project, path);
    }

    // ---------------------------------------------------------------- errors & audio status

    private void ShowError(string context, Exception? ex)
    {
        var message = ex is null ? context : $"{context}: {ex.Message}";
        ErrorMessage = message;
        StatusMessage = message;
        _s.Log.Error(context, ex);
    }

    private void UpdateAudioStatus()
    {
        AudioOk = _s.Output.IsRunning;
        AudioStatusText = AudioOk ? $"Audio: {_s.Output.DeviceName} ({_s.Output.LatencyMs} ms)" : "Audio: NOT RUNNING";
        Settings.RefreshStatus();
    }

    private void OnAudioFailed(string message)
    {
        AudioOk = false;
        AudioStatusText = "Audio: DEVICE LOST";
        ShowError(message + " Trying to recover...", null);
        _deviceCheckTimer.Stop();
        _deviceCheckTimer.Start();
    }

    private void CheckDevice()
    {
        if (_s.Output.IsRunning && _s.Output.IsDeviceStillPresent())
        {
            UpdateAudioStatus();
            return;
        }

        var warning = _s.StartAudioOutput();
        UpdateAudioStatus();
        if (_s.Output.IsRunning)
        {
            StatusMessage = string.IsNullOrEmpty(warning) ? $"Audio restored on {_s.Output.DeviceName}." : warning;
            if (string.IsNullOrEmpty(warning))
            {
                ErrorMessage = null;
            }
        }
        else
        {
            ShowError(warning, null);
        }
    }

    private void Tick()
    {
        try
        {
            Perform.Tick();
            if (SelectedTabIndex == 1 || Record.IsRecording)
            {
                Record.Tick();
            }

            if (SelectedTabIndex == 2)
            {
                Separate.Tick();
            }
        }
        catch (Exception ex)
        {
            _s.Log.Warn("UI tick failed", ex);
        }
    }

    // ---------------------------------------------------------------- projects

    public bool ConfirmDiscardChanges()
    {
        if (!Perform.IsDirty)
        {
            return true;
        }

        return Dialogs.AskSaveChanges(Perform.ProjectName) switch
        {
            MessageBoxResult.Yes => SaveProject(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void NewProject()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var name = Dialogs.AskText("New project", "Project name:", "Sunday Service");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var path = _s.Projects.UniquePathFor(name);
        var project = Project.CreateDefault(Path.GetFileNameWithoutExtension(path));
        ProjectSerializer.Save(project, path);
        Perform.LoadProject(project, path);
        RememberProject(path);
        StatusMessage = $"Created '{project.Name}' with a starter groove. Edit or remove the tracks as you like.";
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var path = Dialogs.OpenFile("Open project", $"Worship Loop Studio project (*{ProjectSerializer.Extension})|*{ProjectSerializer.Extension}", _s.Paths.Projects);
        if (path is null)
        {
            return;
        }

        try
        {
            var project = ProjectSerializer.Load(path);
            Perform.LoadProject(project, path);
            RememberProject(path);
            StatusMessage = $"Opened '{project.Name}'.";
        }
        catch (ProjectLoadException ex)
        {
            ShowError(ex.Message, null);
        }
    }

    public bool SaveProject()
    {
        if (Perform.ProjectPath is null)
        {
            return SaveProjectAs();
        }

        try
        {
            ProjectSerializer.Save(Perform.Project, Perform.ProjectPath);
            Perform.IsDirty = false;
            RememberProject(Perform.ProjectPath);
            StatusMessage = $"Saved {Path.GetFileName(Perform.ProjectPath)} at {DateTime.Now:HH:mm}.";
            return true;
        }
        catch (Exception ex)
        {
            ShowError("The project could not be saved", ex);
            return false;
        }
    }

    private bool SaveProjectAs()
    {
        var path = Dialogs.SaveFile("Save project as", $"Worship Loop Studio project (*{ProjectSerializer.Extension})|*{ProjectSerializer.Extension}",
            ProjectStore.SanitizeName(Perform.ProjectName) + ProjectSerializer.Extension, _s.Paths.Projects);
        if (path is null)
        {
            return false;
        }

        Perform.RenameProject(Path.GetFileNameWithoutExtension(path));
        Perform.ProjectPath = path;
        return SaveProject();
    }

    private void RenameProject()
    {
        var name = Dialogs.AskText("Rename project", "New name:", Perform.ProjectName);
        if (string.IsNullOrWhiteSpace(name) || name == Perform.ProjectName)
        {
            return;
        }

        try
        {
            if (Perform.ProjectPath is null)
            {
                Perform.RenameProject(name);
                Perform.IsDirty = true;
            }
            else
            {
                var newPath = _s.Projects.Rename(Perform.Project, Perform.ProjectPath, name);
                Perform.ProjectPath = newPath;
                Perform.RenameProject(name);
                Perform.IsDirty = false;
                RememberProject(newPath);
            }

            OnPropertyChanged(nameof(WindowTitle));
            StatusMessage = $"Project renamed to '{name}'.";
        }
        catch (Exception ex)
        {
            ShowError("The project could not be renamed", ex);
        }
    }

    private void RememberProject(string path)
    {
        _s.Settings.LastProjectPath = path;
        _s.SaveSettings();
    }

    // ---------------------------------------------------------------- export

    private async Task ExportAsync(TrackViewModel? onlyTrack)
    {
        var baseName = ProjectStore.SanitizeName(onlyTrack is null ? Perform.ProjectName : $"{Perform.ProjectName} - {onlyTrack.Name}");
        var target = Dialogs.SaveFile(onlyTrack is null ? "Export mix" : $"Export loop '{onlyTrack.Name}'",
            "WAV audio (*.wav)|*.wav|MP3 audio (*.mp3)|*.mp3", baseName + ".wav", _s.Paths.Exports);
        if (target is null)
        {
            return;
        }

        var missing = Perform.MissingSampleNames();
        var arrangement = Perform.BuildArrangement().ForExport(onlyTrack?.Model.Id);
        var loops = Perform.ExportLoops;
        var mp3 = string.Equals(Path.GetExtension(target), ".mp3", StringComparison.OrdinalIgnoreCase);
        IsExporting = true;
        ExportProgress = 0;
        StatusMessage = "Exporting...";
        var progress = new Progress<double>(p => ExportProgress = p * 100);
        try
        {
            var wavPath = mp3 ? Path.Combine(_s.Paths.Temp, $"export-{Guid.NewGuid():N}.wav") : target;
            var duration = await Task.Run(() => AudioExporter.RenderToWav(arrangement, loops, 3.0, wavPath, progress));
            if (mp3)
            {
                try
                {
                    await Task.Run(() => AudioExporter.EncodeMp3(wavPath, target));
                }
                finally
                {
                    File.Delete(wavPath);
                }
            }

            _lastExportPath = target;
            StatusMessage = $"Exported {Path.GetFileName(target)} ({duration.TotalSeconds:0.0} s, {loops} loop{(loops > 1 ? "s" : "")})."
                            + (missing.Count > 0 ? $" Warning: {missing.Count} track(s) had no sample and are silent." : string.Empty);
        }
        catch (Mp3UnavailableException ex)
        {
            ShowError(ex.Message, null);
        }
        catch (Exception ex)
        {
            ShowError("Export failed", ex);
        }
        finally
        {
            IsExporting = false;
            ExportProgress = 0;
        }
    }

    public void OnClosing(CancelEventArgs e)
    {
        if (Record.IsRecording)
        {
            if (!Dialogs.Confirm("Recording in progress", "A recording is in progress. Stop it and quit?"))
            {
                e.Cancel = true;
                return;
            }

            _s.Recorder.StopRecording();
        }

        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        _uiTimer.Stop();
        _s.SaveSettings();
    }
}
