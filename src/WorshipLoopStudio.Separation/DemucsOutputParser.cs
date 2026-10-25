using System.Globalization;
using System.Text.RegularExpressions;

namespace WorshipLoopStudio.Separation;

public static partial class DemucsOutputParser
{
    [GeneratedRegex(@"(\d{1,3}(?:\.\d+)?)%\|")]
    private static partial Regex PercentRegex();

    /// <summary>Extracts the percentage from a tqdm progress line such as " 42%|████      | 12.3/29.2 [00:10&lt;00:14]".</summary>
    public static bool TryParseProgress(string line, out double percent)
    {
        percent = 0;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var matches = PercentRegex().Matches(line);
        if (matches.Count == 0)
        {
            return false;
        }

        var value = matches[^1].Groups[1].Value;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            return false;
        }

        percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    /// <summary>Turns common Python / Demucs failures into advice a worship team member can act on.</summary>
    public static string ExplainFailure(IEnumerable<string> logLines)
    {
        var text = string.Join("\n", logLines);
        if (text.Contains("No module named demucs", StringComparison.OrdinalIgnoreCase)
            || text.Contains("No module named 'demucs'", StringComparison.OrdinalIgnoreCase))
        {
            return "Demucs is not installed in the selected Python environment. Click 'Install Demucs' first.";
        }

        if (text.Contains("No module named 'torch'", StringComparison.OrdinalIgnoreCase))
        {
            return "PyTorch is missing from the Python environment. Click 'Install Demucs' to install it.";
        }

        if (text.Contains("CUDA out of memory", StringComparison.OrdinalIgnoreCase) || text.Contains("OutOfMemoryError", StringComparison.OrdinalIgnoreCase))
        {
            return "The graphics card ran out of memory. Choose 'CPU' as the processing device and try again.";
        }

        if (text.Contains("Torch not compiled with CUDA", StringComparison.OrdinalIgnoreCase))
        {
            return "This PyTorch build has no NVIDIA GPU support. Choose 'CPU' or reinstall Demucs with the GPU option.";
        }

        if (text.Contains("urlopen error", StringComparison.OrdinalIgnoreCase) || text.Contains("HTTP Error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Failed to establish a new connection", StringComparison.OrdinalIgnoreCase))
        {
            return "The separation model could not be downloaded. Connect to the internet once and click 'Download model'.";
        }

        if (text.Contains("MemoryError", StringComparison.Ordinal) || text.Contains("not enough memory", StringComparison.OrdinalIgnoreCase))
        {
            return "The computer ran out of memory. Close other applications or separate a shorter song.";
        }

        return "Separation failed. See the log below for details.";
    }
}
