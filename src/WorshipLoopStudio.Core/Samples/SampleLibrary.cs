namespace WorshipLoopStudio.Core.Samples;

public sealed record SampleEntry(string SampleRef, string Name, string Category, string FullPath, bool IsStarter)
{
    public override string ToString() => Name;
}

public static class SampleRefs
{
    public const string StarterPrefix = "starter:";

    public static string Starter(string id) => StarterPrefix + id;

    public static bool IsStarter(string? sampleRef) =>
        sampleRef is not null && sampleRef.StartsWith(StarterPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns a sample reference into a file path. Returns null for an empty reference.</summary>
    public static string? Resolve(string? sampleRef, AppPaths paths, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(sampleRef))
        {
            return null;
        }

        if (IsStarter(sampleRef))
        {
            var id = sampleRef[StarterPrefix.Length..];
            return Path.Combine(paths.StarterSamples, StarterLibrary.FileNameFor(id));
        }

        if (Path.IsPathRooted(sampleRef))
        {
            return sampleRef;
        }

        var baseDir = projectDirectory ?? paths.Root;
        return Path.GetFullPath(Path.Combine(baseDir, sampleRef));
    }

    public static string DisplayName(string? sampleRef)
    {
        if (string.IsNullOrWhiteSpace(sampleRef))
        {
            return "(no sample)";
        }

        if (IsStarter(sampleRef))
        {
            var id = sampleRef[StarterPrefix.Length..];
            return StarterLibrary.Find(id)?.Name ?? id;
        }

        return Path.GetFileNameWithoutExtension(sampleRef);
    }
}

/// <summary>Builds the sample browser contents from the starter library and the user's folders.</summary>
public sealed class SampleLibrary
{
    public const string AllCategory = "All";
    public const string ImportedCategory = "Imported";
    public const string UserCategory = "My Samples";
    public const string RecordingsCategory = "Recordings";
    public const string LoopsCategory = "Loops";
    public const string StemsCategory = "Separated Stems";

    private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".aiff", ".aif", ".flac", ".m4a", ".wma" };

    private readonly AppPaths _paths;

    public SampleLibrary(AppPaths paths)
    {
        _paths = paths;
    }

    public static bool IsSupportedAudioFile(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string FileDialogFilter =>
        "Audio files (*.wav;*.mp3;*.aiff;*.aif;*.flac;*.m4a;*.wma)|*.wav;*.mp3;*.aiff;*.aif;*.flac;*.m4a;*.wma|All files (*.*)|*.*";

    public IReadOnlyList<SampleEntry> Scan()
    {
        var list = new List<SampleEntry>();
        foreach (var s in StarterLibrary.All)
        {
            list.Add(new SampleEntry(SampleRefs.Starter(s.Id), s.Name, s.Category,
                Path.Combine(_paths.StarterSamples, StarterLibrary.FileNameFor(s.Id)), true));
        }

        AddFolder(list, _paths.UserSamples, UserCategory, SearchOption.AllDirectories);
        AddFolder(list, _paths.ImportedSamples, ImportedCategory, SearchOption.AllDirectories);
        AddFolder(list, _paths.Loops, LoopsCategory, SearchOption.AllDirectories);
        AddFolder(list, _paths.Recordings, RecordingsCategory, SearchOption.TopDirectoryOnly);
        AddFolder(list, _paths.SeparatedStems, StemsCategory, SearchOption.AllDirectories);
        return list;
    }

    public static IReadOnlyList<string> Categories(IEnumerable<SampleEntry> entries)
    {
        var categories = new List<string> { AllCategory };
        categories.AddRange(entries.Select(e => e.Category).Distinct());
        return categories;
    }

    private static void AddFolder(List<SampleEntry> list, string folder, string category, SearchOption option)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.*", option).Where(IsSupportedAudioFile).OrderBy(f => f).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var parent = Path.GetFileName(Path.GetDirectoryName(file));
            if (option == SearchOption.AllDirectories && !string.Equals(Path.GetDirectoryName(file), folder, StringComparison.OrdinalIgnoreCase))
            {
                name = $"{parent} / {name}";
            }

            list.Add(new SampleEntry(file, name, category, file, false));
        }
    }
}
