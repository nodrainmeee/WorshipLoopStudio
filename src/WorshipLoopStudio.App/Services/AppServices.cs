using WorshipLoopStudio.Audio.Devices;
using WorshipLoopStudio.Audio.Engine;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Audio.Recording;
using WorshipLoopStudio.Audio.Stems;
using WorshipLoopStudio.Core;
using WorshipLoopStudio.Core.Projects;
using WorshipLoopStudio.Core.Samples;
using WorshipLoopStudio.Separation;

namespace WorshipLoopStudio.App.Services;

/// <summary>Composition root: creates and owns every long-lived service.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Settings = SettingsStore.Load(AppPaths.SettingsFile);
        var root = string.IsNullOrWhiteSpace(Settings.DataRoot) ? AppPaths.DefaultRoot : Settings.DataRoot!;
        try
        {
            Paths = new AppPaths(root);
            Paths.EnsureCreated();
        }
        catch (Exception)
        {
            // The custom folder is unavailable (unplugged drive...): fall back to the default location.
            Paths = new AppPaths(AppPaths.DefaultRoot);
            Paths.EnsureCreated();
            StartupWarnings.Add($"The storage folder '{root}' is not available. Using {Paths.Root} instead.");
        }

        Log = new Logger(Paths.Logs);
        Log.Info($"Worship Loop Studio starting. Data folder: {Paths.Root}");

        Engine = new LoopEngine();
        Stems = new StemPlayer();
        Output = new AudioOutputService();
        Output.Mixer.AddMixerInput(Engine);
        Output.Mixer.AddMixerInput(Stems);
        Samples = new SampleCache();
        Recorder = new RecordingService();
        Demucs = new DemucsService(Paths);
        Projects = new ProjectStore(Paths.Projects);
        Library = new SampleLibrary(Paths);
    }

    public AppSettings Settings { get; }
    public AppPaths Paths { get; }
    public Logger Log { get; }
    public LoopEngine Engine { get; }
    public StemPlayer Stems { get; }
    public AudioOutputService Output { get; }
    public SampleCache Samples { get; }
    public RecordingService Recorder { get; }
    public DemucsService Demucs { get; }
    public ProjectStore Projects { get; }
    public SampleLibrary Library { get; }
    public List<string> StartupWarnings { get; } = new();

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings, AppPaths.SettingsFile);
        }
        catch (Exception ex)
        {
            Log.Warn("Settings could not be saved", ex);
        }
    }

    /// <summary>Generates (or copies from the install folder) the starter instrument library.</summary>
    public int InstallStarterSamples()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "Samples", "Starter");
        return StarterLibrary.Install(Paths.StarterSamples, shipped);
    }

    public static OutputDriver ParseDriver(string? value) =>
        Enum.TryParse<OutputDriver>(value, true, out var driver) ? driver : OutputDriver.WasapiShared;

    /// <summary>Opens the configured output, falling back to safer options so the app is never silent without telling the user.</summary>
    public string StartAudioOutput()
    {
        var driver = ParseDriver(Settings.OutputDriver);
        try
        {
            Output.Start(driver, Settings.OutputDeviceId, Settings.LatencyMs);
            return string.Empty;
        }
        catch (Exception first)
        {
            Log.Warn("Configured audio output failed", first);
            try
            {
                Output.Start(OutputDriver.WasapiShared, null, Math.Max(40, Settings.LatencyMs));
                return $"{first.Message} Using the Windows default device instead.";
            }
            catch (Exception second)
            {
                Log.Warn("Default WASAPI output failed", second);
                try
                {
                    Output.Start(OutputDriver.WaveOut, null, 100);
                    return $"{first.Message} Using MME output instead.";
                }
                catch (Exception third)
                {
                    Log.Error("No audio output available", third);
                    return "No audio output device could be opened. Connect speakers or an audio interface, then open Settings and click 'Apply audio settings'.";
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Recorder.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Recorder shutdown", ex);
        }

        try
        {
            Output.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Output shutdown", ex);
        }

        SaveSettings();
        Log.Info("Shutdown complete.");
    }
}
