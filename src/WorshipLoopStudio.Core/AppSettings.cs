using System.Text.Json;

namespace WorshipLoopStudio.Core;

public sealed class AppSettings
{
    public string? DataRoot { get; set; }
    public string OutputDriver { get; set; } = "WasapiShared";
    public string? OutputDeviceId { get; set; }
    public int LatencyMs { get; set; } = 40;
    public string? InputDeviceId { get; set; }
    public string? PythonPath { get; set; }
    public string DemucsModel { get; set; } = "htdemucs_6s";
    public string DemucsDevice { get; set; } = "auto";
    public string StopMode { get; set; } = "EndOfBar";
    public string? LastProjectPath { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file must never prevent the app from starting during a service.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string path)
    {
        var json = JsonSerializer.Serialize(settings, Options);
        AtomicFile.WriteAllText(path, json);
    }
}
