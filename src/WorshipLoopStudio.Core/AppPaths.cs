namespace WorshipLoopStudio.Core;

/// <summary>
/// Folder layout of the application data directory (default: %LOCALAPPDATA%\WorshipLoopStudio).
/// No administrator rights are needed: everything lives in the user's profile unless the user picks another folder.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "WorshipLoopStudio";

    public AppPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A root folder is required.", nameof(root));
        }

        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    public string Projects => Path.Combine(Root, "Projects");
    public string Loops => Path.Combine(Root, "Loops");
    public string Recordings => Path.Combine(Root, "Recordings");
    public string Samples => Path.Combine(Root, "Samples");
    public string StarterSamples => Path.Combine(Samples, "Starter");
    public string ImportedSamples => Path.Combine(Samples, "Imported");
    public string UserSamples => Path.Combine(Samples, "User");
    public string SeparatedStems => Path.Combine(Root, "SeparatedStems");
    public string Models => Path.Combine(Root, "Models");
    public string Logs => Path.Combine(Root, "Logs");
    public string Exports => Path.Combine(Root, "Exports");
    public string Temp => Path.Combine(Root, "Temp");

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary>Settings always live in the default folder so a custom data folder can be found again.</summary>
    public static string SettingsFile => Path.Combine(DefaultRoot, "settings.json");

    public IReadOnlyList<string> AllFolders => new[]
    {
        Root, Projects, Loops, Recordings, Samples, StarterSamples, ImportedSamples, UserSamples,
        SeparatedStems, Models, Logs, Exports, Temp,
    };

    public void EnsureCreated()
    {
        foreach (var folder in AllFolders)
        {
            Directory.CreateDirectory(folder);
        }
    }
}
