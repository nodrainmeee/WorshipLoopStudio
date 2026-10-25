using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorshipLoopStudio.Core.Projects;

public sealed class ProjectLoadException : Exception
{
    public ProjectLoadException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

public static class ProjectSerializer
{
    public const string Extension = ".wlsproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return JsonSerializer.Serialize(project, Options);
    }

    public static Project Deserialize(string json)
    {
        Project? project;
        try
        {
            project = JsonSerializer.Deserialize<Project>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ProjectLoadException("The project file is damaged or is not a Worship Loop Studio project.", ex);
        }

        if (project is null)
        {
            throw new ProjectLoadException("The project file is empty.");
        }

        if (project.FormatVersion > Project.CurrentFormatVersion)
        {
            throw new ProjectLoadException(
                $"This project was saved by a newer version of Worship Loop Studio (format {project.FormatVersion}). Please update the application.");
        }

        project.FormatVersion = Project.CurrentFormatVersion;
        project.Normalize();
        return project;
    }

    public static void Save(Project project, string path)
    {
        project.ModifiedUtc = DateTime.UtcNow;
        AtomicFile.WriteAllText(path, Serialize(project));
    }

    public static Project Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ProjectLoadException($"The project file '{path}' does not exist.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProjectLoadException($"The project file could not be read: {ex.Message}", ex);
        }

        return Deserialize(json);
    }
}
