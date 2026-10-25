using System.Diagnostics;
using System.Text;

namespace WorshipLoopStudio.Separation;

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> Lines);

/// <summary>Runs an external process, streaming stdout and stderr line by line (\r and \n both end a line, for tqdm bars).</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment, Action<string>? onLine, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["PYTHONUNBUFFERED"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {fileName}.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new FileNotFoundException($"'{fileName}' was not found. Check the Python installation.", fileName, ex);
        }

        var lines = new List<string>();
        var gate = new object();

        void Collect(string line)
        {
            if (line.Length == 0)
            {
                return;
            }

            lock (gate)
            {
                lines.Add(line);
                if (lines.Count > 2000)
                {
                    lines.RemoveRange(0, 500);
                }
            }

            onLine?.Invoke(line);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Already exited.
            }
        });

        var stdout = PumpAsync(process.StandardOutput, Collect);
        var stderr = PumpAsync(process.StandardError, Collect);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        List<string> snapshot;
        lock (gate)
        {
            snapshot = new List<string>(lines);
        }

        return new ProcessResult(process.ExitCode, snapshot);
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        var buffer = new char[4096];
        var current = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\r' or '\n')
                {
                    if (current.Length > 0)
                    {
                        onLine(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0)
        {
            onLine(current.ToString());
        }
    }
}
