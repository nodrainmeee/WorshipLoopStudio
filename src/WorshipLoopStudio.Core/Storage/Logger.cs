namespace WorshipLoopStudio.Core;

/// <summary>Very small file logger. Never call it from the real-time audio thread.</summary>
public sealed class Logger
{
    private readonly object _gate = new();

    public Logger(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public string CurrentFile => Path.Combine(Folder, $"app-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            if (ex is not null)
            {
                line += Environment.NewLine + ex;
            }

            lock (_gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // Logging must never crash the application.
        }
    }
}
