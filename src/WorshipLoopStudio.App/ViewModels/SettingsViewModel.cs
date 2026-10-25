using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using WorshipLoopStudio.App.Infrastructure;
using WorshipLoopStudio.App.Services;
using WorshipLoopStudio.Audio.Devices;
using WorshipLoopStudio.Core;
using WorshipLoopStudio.Core.Samples;

namespace WorshipLoopStudio.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private OptionItem<OutputDriver> _selectedDriver;
    private AudioDeviceInfo? _selectedDevice;
    private int _selectedLatency;
    private string _audioStatus = string.Empty;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        Drivers = new[]
        {
            new OptionItem<OutputDriver>(OutputDriver.WasapiShared, "WASAPI shared (recommended)"),
            new OptionItem<OutputDriver>(OutputDriver.WasapiExclusive, "WASAPI exclusive (lowest latency, takes over the device)"),
            new OptionItem<OutputDriver>(OutputDriver.WaveOut, "MME / WaveOut (maximum compatibility)"),
        };
        var driver = AppServices.ParseDriver(_s.Settings.OutputDriver);
        _selectedDriver = Drivers.First(d => d.Value == driver);
        _selectedLatency = LatencyOptions.Contains(_s.Settings.LatencyMs) ? _s.Settings.LatencyMs : 40;

        ApplyAudioCommand = new RelayCommand(ApplyAudio);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        ChangeDataRootCommand = new RelayCommand(ChangeDataRoot);
        ResetDataRootCommand = new RelayCommand(ResetDataRoot, () => _s.Settings.DataRoot is not null);
        OpenDataFolderCommand = new RelayCommand(() => Dialogs.OpenInExplorer(_s.Paths.Root));
        OpenLogsCommand = new RelayCommand(() => Dialogs.OpenInExplorer(_s.Paths.Logs));
        RegenerateSamplesCommand = new AsyncRelayCommand(RegenerateSamplesAsync);
        RefreshDevices();
    }

    public IReadOnlyList<OptionItem<OutputDriver>> Drivers { get; }
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public IReadOnlyList<int> LatencyOptions { get; } = new[] { 10, 20, 30, 40, 60, 80, 100, 150, 200 };

    public ICommand ApplyAudioCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ChangeDataRootCommand { get; }
    public ICommand ResetDataRootCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand RegenerateSamplesCommand { get; }

    public OptionItem<OutputDriver> SelectedDriver
    {
        get => _selectedDriver;
        set
        {
            if (value is not null && SetProperty(ref _selectedDriver, value))
            {
                RefreshDevices();
            }
        }
    }

    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public int SelectedLatency
    {
        get => _selectedLatency;
        set => SetProperty(ref _selectedLatency, value);
    }

    public string AudioStatus
    {
        get => _audioStatus;
        set => SetProperty(ref _audioStatus, value);
    }

    public string DataRoot => _s.Paths.Root;

    public string ConfiguredDataRoot => _s.Settings.DataRoot ?? $"Default ({AppPaths.DefaultRoot})";

    public string DiskFreeText => DiskSpaceGuard.GetAvailableBytes(_s.Paths.Root) is long free
        ? $"{DiskSpaceGuard.FormatBytes(free)} free on this drive"
        : "Free space unknown";

    public string VersionText => "Version " + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev").Split('+')[0];

    public void RefreshStatus()
    {
        AudioStatus = _s.Output.IsRunning
            ? $"Running: {_s.Output.DeviceName}, buffer {_s.Output.LatencyMs} ms"
            : "Audio output is not running.";
        OnPropertyChanged(nameof(DiskFreeText));
    }

    private void RefreshDevices()
    {
        var wanted = SelectedDevice?.Id ?? _s.Settings.OutputDeviceId ?? AudioDeviceInfo.DefaultId;
        OutputDevices.Clear();
        foreach (var device in _s.Output.GetDevices(SelectedDriver.Value))
        {
            OutputDevices.Add(device);
        }

        SelectedDevice = OutputDevices.FirstOrDefault(d => d.Id == wanted) ?? OutputDevices.FirstOrDefault();
    }

    private void ApplyAudio()
    {
        _s.Settings.OutputDriver = SelectedDriver.Value.ToString();
        _s.Settings.OutputDeviceId = string.IsNullOrEmpty(SelectedDevice?.Id) ? null : SelectedDevice!.Id;
        _s.Settings.LatencyMs = SelectedLatency;
        try
        {
            _s.Output.Start(SelectedDriver.Value, _s.Settings.OutputDeviceId, SelectedLatency);
            _s.SaveSettings();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            AudioStatus = ex.Message;
            ErrorReporter.Report(ex.Message + " Falling back to the default device.", null);
            var warning = _s.StartAudioOutput();
            if (!string.IsNullOrEmpty(warning))
            {
                AudioStatus = warning;
            }
        }
    }

    private void ChangeDataRoot()
    {
        var folder = Dialogs.PickFolder("Choose where Worship Loop Studio stores projects, samples and stems", _s.Paths.Root);
        if (folder is null)
        {
            return;
        }

        try
        {
            var target = Path.Combine(folder, AppPaths.AppFolderName);
            var paths = new AppPaths(target);
            paths.EnsureCreated();
            var probe = Path.Combine(paths.Root, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            _s.Settings.DataRoot = paths.Root;
            _s.SaveSettings();
            OnPropertyChanged(nameof(ConfiguredDataRoot));
            Dialogs.Info("Storage folder", $"New storage folder:\n{paths.Root}\n\nRestart Worship Loop Studio to use it. Existing files are not moved automatically; copy them yourself if needed.");
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("That folder cannot be used (no write permission?)", ex);
        }
    }

    private void ResetDataRoot()
    {
        _s.Settings.DataRoot = null;
        _s.SaveSettings();
        OnPropertyChanged(nameof(ConfiguredDataRoot));
        Dialogs.Info("Storage folder", $"The default folder will be used after a restart:\n{AppPaths.DefaultRoot}");
    }

    private async Task RegenerateSamplesAsync()
    {
        var count = await Task.Run(() => StarterLibrary.EnsureGenerated(_s.Paths.StarterSamples, overwrite: true));
        foreach (var s in StarterLibrary.All)
        {
            _s.Samples.Invalidate(Path.Combine(_s.Paths.StarterSamples, StarterLibrary.FileNameFor(s.Id)));
        }

        Dialogs.Info("Starter library", $"{count} starter samples were rebuilt.");
    }
}
