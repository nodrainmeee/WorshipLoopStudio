using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using WorshipLoopStudio.App.Infrastructure;
using WorshipLoopStudio.App.Services;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Audio.Stems;
using WorshipLoopStudio.Core.Samples;
using WorshipLoopStudio.Separation;

namespace WorshipLoopStudio.App.ViewModels;

public sealed class StemViewModel : ObservableObject
{
    private readonly SeparateViewModel _owner;

    public StemViewModel(SeparateViewModel owner, StemTrack track, string displayName)
    {
        _owner = owner;
        Track = track;
        DisplayName = displayName;
        ExportWavCommand = new AsyncRelayCommand(() => _owner.ExportStemAsync(this, false));
        ExportMp3Command = new AsyncRelayCommand(() => _owner.ExportStemAsync(this, true));
        EditCommand = new AsyncRelayCommand(() => _owner.OpenInEditorAsync(this));
    }

    public StemTrack Track { get; }
    public string DisplayName { get; }
    public string FilePath => Track.Path;
    public ICommand ExportWavCommand { get; }
    public ICommand ExportMp3Command { get; }
    public ICommand EditCommand { get; }

    public double Volume
    {
        get => Track.Volume;
        set
        {
            Track.Volume = (float)Math.Clamp(value, 0, 2);
            OnPropertyChanged();
        }
    }

    public bool Mute
    {
        get => Track.Mute;
        set
        {
            Track.Mute = value;
            OnPropertyChanged();
        }
    }

    public bool Solo
    {
        get => Track.Solo;
        set
        {
            Track.Solo = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>The "Separate" workspace: real AI source separation with Demucs, stem preview, mixing and export.</summary>
public sealed class SeparateViewModel : ObservableObject
{
    private const int MaxLogChars = 40_000;
    private readonly AppServices _s;
    private readonly RecordViewModel _record;
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private string _statusText = "Click 'Check setup' to see whether Demucs is installed.";
    private bool _isReady;
    private bool _isBusy;
    private bool _useGpuInstall;
    private DemucsModelInfo _selectedModel;
    private OptionItem<string> _selectedDevice;
    private string? _inputFile;
    private double _progress;
    private string _progressText = string.Empty;
    private string? _outputFolder;
    private double _position;
    private bool _updatingPosition;
    private bool _isPlaying;
    private bool _hasError;

    public SeparateViewModel(AppServices services, RecordViewModel record)
    {
        _s = services;
        _record = record;
        Devices = new[]
        {
            new OptionItem<string>("auto", "Automatic (GPU if available)"),
            new OptionItem<string>("cpu", "CPU"),
            new OptionItem<string>("cuda", "NVIDIA GPU (CUDA)"),
        };
        _selectedModel = DemucsModels.Find(_s.Settings.DemucsModel);
        _selectedDevice = Devices.FirstOrDefault(d => d.Value == _s.Settings.DemucsDevice) ?? Devices[0];

        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy);
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync, () => !IsBusy && IsReady);
        BrowseInputCommand = new RelayCommand(BrowseInput, () => !IsBusy);
        SeparateCommand = new AsyncRelayCommand(SeparateAsync, () => !IsBusy && IsReady && !string.IsNullOrEmpty(InputFile));
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        BrowsePythonCommand = new RelayCommand(BrowsePython, () => !IsBusy);
        ClearPythonCommand = new RelayCommand(() => PythonPath = null, () => !IsBusy);
        OpenPythonDownloadCommand = new RelayCommand(() => Dialogs.OpenUri("https://www.python.org/downloads/windows/"));
        PlayCommand = new RelayCommand(() => _s.Stems.Play(), () => Stems.Count > 0);
        PauseCommand = new RelayCommand(() => _s.Stems.Pause(), () => Stems.Count > 0);
        StopCommand = new RelayCommand(() => _s.Stems.Stop(), () => Stems.Count > 0);
        ExportAllWavCommand = new AsyncRelayCommand(() => ExportAllAsync(false), () => Stems.Count > 0 && !IsBusy);
        ExportAllMp3Command = new AsyncRelayCommand(() => ExportAllAsync(true), () => Stems.Count > 0 && !IsBusy);
        OpenOutputFolderCommand = new RelayCommand(() => Dialogs.OpenInExplorer(OutputFolder ?? _s.Paths.SeparatedStems));
        OpenPreviousCommand = new AsyncRelayCommand(OpenPreviousAsync, () => !IsBusy);
        ClearLogCommand = new RelayCommand(() => { _log.Clear(); OnPropertyChanged(nameof(LogText)); });
    }

    public IReadOnlyList<DemucsModelInfo> Models => DemucsModels.All;
    public IReadOnlyList<OptionItem<string>> Devices { get; }
    public ObservableCollection<StemViewModel> Stems { get; } = new();

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand DownloadModelCommand { get; }
    public ICommand BrowseInputCommand { get; }
    public ICommand SeparateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowsePythonCommand { get; }
    public ICommand ClearPythonCommand { get; }
    public ICommand OpenPythonDownloadCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ExportAllWavCommand { get; }
    public ICommand ExportAllMp3Command { get; }
    public ICommand OpenOutputFolderCommand { get; }
    public ICommand OpenPreviousCommand { get; }
    public ICommand ClearLogCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool UseGpuInstall
    {
        get => _useGpuInstall;
        set => SetProperty(ref _useGpuInstall, value);
    }

    public string? PythonPath
    {
        get => _s.Settings.PythonPath;
        set
        {
            _s.Settings.PythonPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PythonDisplay));
            IsReady = false;
            StatusText = "Python setting changed. Click 'Check setup'.";
        }
    }

    public string PythonDisplay => PythonPath ?? $"Built-in environment ({_s.Demucs.EnvironmentFolder})";

    public DemucsModelInfo SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (value is not null && SetProperty(ref _selectedModel, value))
            {
                _s.Settings.DemucsModel = value.Name;
                OnPropertyChanged(nameof(ModelNotes));
            }
        }
    }

    public string ModelNotes => SelectedModel.Notes;

    public OptionItem<string> SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (value is not null && SetProperty(ref _selectedDevice, value))
            {
                _s.Settings.DemucsDevice = value.Value;
            }
        }
    }

    public string? InputFile
    {
        get => _inputFile;
        set
        {
            if (SetProperty(ref _inputFile, value))
            {
                OnPropertyChanged(nameof(InputFileName));
            }
        }
    }

    public string InputFileName => InputFile is null ? "No song selected" : Path.GetFileName(InputFile);

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string LogText => _log.ToString();

    public string? OutputFolder
    {
        get => _outputFolder;
        private set => SetProperty(ref _outputFolder, value);
    }

    public double Duration => _s.Stems.DurationSeconds;

    public double Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(PositionText));
                if (!_updatingPosition)
                {
                    _s.Stems.Seek(value);
                }
            }
        }
    }

    public string PositionText => $"{TimeSpan.FromSeconds(Position):m\\:ss} / {TimeSpan.FromSeconds(Duration):m\\:ss}";

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public void Tick()
    {
        if (Stems.Count == 0)
        {
            return;
        }

        _updatingPosition = true;
        Position = _s.Stems.PositionSeconds;
        _updatingPosition = false;
        IsPlaying = _s.Stems.IsPlaying;
    }

    private void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > MaxLogChars)
        {
            _log.Remove(0, _log.Length - MaxLogChars);
        }

        OnPropertyChanged(nameof(LogText));
    }

    private Action<string> UiLog() => line => Application.Current?.Dispatcher.InvokeAsync(() => AppendLog(line));

    private void SetStatus(string text, bool error)
    {
        StatusText = text;
        HasError = error;
    }

    public async Task CheckAsync()
    {
        IsBusy = true;
        try
        {
            SetStatus("Checking Python and Demucs...", false);
            var status = await _s.Demucs.CheckAsync(PythonPath, CancellationToken.None);
            IsReady = status.IsReady;
            SetStatus(status.Message, !status.IsReady);
            if (status.TorchVersion is not null)
            {
                AppendLog($"Python: {status.PythonDescription}, Demucs {status.DemucsVersion}, PyTorch {status.TorchVersion}, CUDA: {status.CudaAvailable}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync()
    {
        var message = "This downloads and installs PyTorch and Demucs (about 1 GB for CPU, 3 GB for NVIDIA GPU) into:\n"
                      + _s.Demucs.EnvironmentFolder
                      + "\n\nPython 3.10, 3.11 or 3.12 must already be installed. An internet connection is required. Continue?";
        if (!Dialogs.Confirm("Install Demucs", message))
        {
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("Installing Demucs... this can take 5 to 20 minutes.", false);
            await Task.Run(() => _s.Demucs.InstallAsync(UseGpuInstall, UiLog(), _cts.Token));
            PythonPath = null;
            IsBusy = false;
            await CheckAsync();
        }
        catch (SeparationFailedException ex)
        {
            SetStatus(ex.Message, true);
            AppendLog(ex.Message);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Installation cancelled.", true);
        }
        catch (Exception ex)
        {
            SetStatus($"Installation failed: {ex.Message}", true);
            _s.Log.Error("Demucs install failed", ex);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task DownloadModelAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            SetStatus($"Downloading the '{SelectedModel.Name}' model...", false);
            await Task.Run(() => _s.Demucs.DownloadModelAsync(PythonPath, SelectedModel.Name, UiLog(), _cts.Token));
            SetStatus("Model ready. Choose a song and click 'Separate'.", false);
        }
        catch (SeparationFailedException ex)
        {
            SetStatus(ex.Message, true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Download cancelled.", true);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BrowseInput()
    {
        var file = Dialogs.OpenFile("Choose a song to separate", SampleLibrary.FileDialogFilter);
        if (file is not null)
        {
            InputFile = file;
        }
    }

    private void BrowsePython()
    {
        var file = Dialogs.OpenFile("Choose python.exe (an environment where demucs is installed)", "python.exe|python.exe|Executables (*.exe)|*.exe");
        if (file is not null)
        {
            PythonPath = file;
        }
    }

    private async Task SeparateAsync()
    {
        var input = InputFile;
        if (input is null || !File.Exists(input))
        {
            SetStatus("The selected song file does not exist.", true);
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var tempWav = Path.Combine(_s.Paths.Temp, $"separate-input-{Guid.NewGuid():N}.wav");
        try
        {
            _s.Stems.Clear();
            Stems.Clear();
            Progress = 0;
            ProgressText = "Preparing audio...";
            SetStatus($"Separating '{Path.GetFileName(input)}' with {SelectedModel.Name}. Keep the app open.", false);
            await Task.Run(() => AudioExporter.ConvertToWav(input, tempWav, token), token);

            var progress = new Progress<double>(p =>
            {
                Progress = p;
                ProgressText = $"Separating... {p:0}%";
            });
            var request = new SeparationRequest(tempWav, Path.GetFileNameWithoutExtension(input), SelectedModel.Name, SelectedDevice.Value);
            var result = await Task.Run(() => _s.Demucs.SeparateAsync(request, PythonPath, progress, UiLog(), token), token);
            ProgressText = "Loading stems...";
            await LoadResultAsync(result);
            SetStatus($"Separation finished: {result.Stems.Count} stems. Preview them below, then export.", false);
            ProgressText = "Done";
        }
        catch (SeparationFailedException ex)
        {
            ProgressText = "Failed";
            SetStatus(ex.Message, true);
            foreach (var line in ex.Log.TakeLast(25))
            {
                AppendLog(line);
            }
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Cancelled";
            SetStatus("Separation cancelled.", true);
        }
        catch (Exception ex)
        {
            ProgressText = "Failed";
            SetStatus($"Separation failed: {ex.Message}", true);
            _s.Log.Error("Separation failed", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempWav))
                {
                    File.Delete(tempWav);
                }
            }
            catch (Exception)
            {
                // Leftover temp file is harmless.
            }

            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task LoadResultAsync(SeparationResult result)
    {
        var tracks = await Task.Run(() => result.Stems.Select(s => (Stem: s, Track: StemTrack.Load(s.DisplayName, s.Path))).ToList());
        _s.Stems.Load(tracks.Select(t => t.Track));
        Stems.Clear();
        foreach (var (stem, track) in tracks)
        {
            Stems.Add(new StemViewModel(this, track, stem.DisplayName));
        }

        OutputFolder = result.OutputFolder;
        Position = 0;
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(PositionText));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task OpenPreviousAsync()
    {
        var folder = Dialogs.PickFolder("Open a folder of separated stems", _s.Paths.SeparatedStems);
        if (folder is null)
        {
            return;
        }

        var result = DemucsService.LoadExisting(folder);
        if (result.Stems.Count == 0)
        {
            SetStatus("That folder contains no WAV stems.", true);
            return;
        }

        IsBusy = true;
        try
        {
            await LoadResultAsync(result);
            SetStatus($"Opened {result.Stems.Count} stems from {Path.GetFileName(folder)}.", false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportStemAsync(StemViewModel stem, bool mp3)
    {
        var ext = mp3 ? ".mp3" : ".wav";
        var filter = mp3 ? "MP3 audio (*.mp3)|*.mp3" : "WAV audio (*.wav)|*.wav";
        var target = Dialogs.SaveFile($"Export {stem.DisplayName}", filter, Path.GetFileNameWithoutExtension(stem.FilePath) + ext, _s.Paths.Exports);
        if (target is null)
        {
            return;
        }

        await ExportOneAsync(stem.FilePath, target, mp3);
        SetStatus($"Exported {Path.GetFileName(target)}.", false);
    }

    private async Task ExportAllAsync(bool mp3)
    {
        var folder = Dialogs.PickFolder("Export all stems to folder", _s.Paths.Exports);
        if (folder is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var stem in Stems.ToList())
            {
                var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(stem.FilePath) + (mp3 ? ".mp3" : ".wav"));
                await ExportOneAsync(stem.FilePath, target, mp3);
            }

            SetStatus($"Exported {Stems.Count} stems to {folder}.", false);
            Dialogs.OpenInExplorer(folder);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task ExportOneAsync(string source, string target, bool mp3)
    {
        if (mp3)
        {
            await Task.Run(() => AudioExporter.EncodeMp3(source, target));
        }
        else if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => File.Copy(source, target, true));
        }
    }

    public async Task OpenInEditorAsync(StemViewModel stem)
    {
        await _record.ImportForEditingAsync(stem.FilePath);
        SetStatus($"'{stem.DisplayName}' copied to Record > clip editor. Trim a loop there and add it to your project.", false);
        RequestRecordTab?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RequestRecordTab;
}
