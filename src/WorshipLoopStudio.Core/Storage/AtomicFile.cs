namespace WorshipLoopStudio.Core;

/// <summary>Writes files through a temporary file so a crash or power loss never leaves a half-written project.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        WriteAllBytes(path, bytes);
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new IOException($"Invalid path: {path}");
        Directory.CreateDirectory(dir);
        DiskSpaceGuard.EnsureAvailable(dir, bytes.LongLength + 1024 * 1024);

        var temp = full + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (File.Exists(full))
        {
            File.Replace(temp, full, null);
        }
        else
        {
            File.Move(temp, full);
        }
    }
}
