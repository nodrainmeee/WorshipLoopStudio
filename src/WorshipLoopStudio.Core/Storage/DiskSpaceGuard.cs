namespace WorshipLoopStudio.Core;

public sealed class InsufficientDiskSpaceException : IOException
{
    public InsufficientDiskSpaceException(long requiredBytes, long availableBytes, string path)
        : base($"Not enough disk space on the drive of '{path}'. Needed {DiskSpaceGuard.FormatBytes(requiredBytes)}, available {DiskSpaceGuard.FormatBytes(availableBytes)}. Free some space or choose another storage folder in Settings.")
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    public long RequiredBytes { get; }
    public long AvailableBytes { get; }
}

public static class DiskSpaceGuard
{
    public static long? GetAvailableBytes(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception)
        {
            // Network shares and unusual mounts cannot always be queried; do not block the user.
            return null;
        }
    }

    public static void EnsureAvailable(string path, long requiredBytes)
    {
        if (GetAvailableBytes(path) is long available && available < requiredBytes)
        {
            throw new InsufficientDiskSpaceException(requiredBytes, available, path);
        }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
