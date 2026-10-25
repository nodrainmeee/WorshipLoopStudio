namespace WorshipLoopStudio.Core.Projects;

public sealed record ProjectInfo(string Name, string Path, DateTime ModifiedLocal);

/// <summary>Each project lives in its own folder: Projects\&lt;Name&gt;\&lt;Name&gt;.wlsproj</summary>
public sealed class ProjectStore
{
    public ProjectStore(string projectsRoot)
    {
        ProjectsRoot = projectsRoot;
    }

    public string ProjectsRoot { get; }

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? string.Empty).Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    public string DefaultPathFor(string name)
    {
        var safe = SanitizeName(name);
        return Path.Combine(ProjectsRoot, safe, safe + ProjectSerializer.Extension);
    }

    public string UniquePathFor(string name)
    {
        var safe = SanitizeName(name);
        var path = DefaultPathFor(safe);
        var i = 2;
        while (File.Exists(path) || Directory.Exists(Path.GetDirectoryName(path)!) && Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Any())
        {
            path = DefaultPathFor($"{safe} ({i++})");
        }

        return path;
    }

    public IReadOnlyList<ProjectInfo> List()
    {
        if (!Directory.Exists(ProjectsRoot))
        {
            return Array.Empty<ProjectInfo>();
        }

        return Directory.EnumerateFiles(ProjectsRoot, "*" + ProjectSerializer.Extension, SearchOption.AllDirectories)
            .Select(p => new ProjectInfo(Path.GetFileNameWithoutExtension(p), p, File.GetLastWriteTime(p)))
            .OrderByDescending(p => p.ModifiedLocal)
            .ToList();
    }

    /// <summary>
    /// Renames a project. When the project sits in its own folder under the projects root, the folder is renamed too.
    /// Returns the new project file path.
    /// </summary>
    public string Rename(Project project, string currentPath, string newName)
    {
        var safe = SanitizeName(newName);
        var currentFull = Path.GetFullPath(currentPath);
        var currentDir = Path.GetDirectoryName(currentFull)!;
        var rootFull = Path.GetFullPath(ProjectsRoot).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(currentDir)?.TrimEnd(Path.DirectorySeparatorChar);
        var ownsFolder = string.Equals(parent, rootFull, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(Path.GetFileName(currentDir), Path.GetFileNameWithoutExtension(currentFull), StringComparison.OrdinalIgnoreCase);

        project.Name = newName.Trim();
        string newPath;
        if (ownsFolder)
        {
            var newDir = Path.Combine(rootFull, safe);
            if (!string.Equals(newDir, currentDir, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(newDir))
                {
                    throw new IOException($"A project folder named '{safe}' already exists.");
                }

                Directory.Move(currentDir, newDir);
            }

            var movedFile = Path.Combine(newDir, Path.GetFileName(currentFull));
            newPath = Path.Combine(newDir, safe + ProjectSerializer.Extension);
            if (!string.Equals(movedFile, newPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(movedFile, newPath);
            }
        }
        else
        {
            newPath = Path.Combine(currentDir, safe + ProjectSerializer.Extension);
            if (!string.Equals(newPath, currentFull, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newPath))
                {
                    throw new IOException($"A project file named '{safe}' already exists in this folder.");
                }

                File.Move(currentFull, newPath);
            }
        }

        ProjectSerializer.Save(project, newPath);
        return newPath;
    }
}
