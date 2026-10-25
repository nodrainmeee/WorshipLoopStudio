using System.Globalization;
using WorshipLoopStudio.Core;

namespace WorshipLoopStudio.Separation;

public sealed record PythonCommand(string FileName, IReadOnlyList<string> PrefixArguments, string Description)
{
    public IEnumerable<string> With(params string[] arguments) => PrefixArguments.Concat(arguments);
}

public sealed record DemucsStatus(bool PythonFound, string? PythonDescription, bool DemucsInstalled, string? DemucsVersion,
    string? TorchVersion, bool CudaAvailable, string Message)
{
    public bool IsReady => PythonFound && DemucsInstalled;
}

public sealed record SeparationRequest(string InputWavPath, string SongName, string ModelName, string Device);

public sealed record SeparatedStem(string Stem, string DisplayName, string Path);

public sealed record SeparationResult(string OutputFolder, IReadOnlyList<SeparatedStem> Stems);

public sealed class SeparationFailedException : Exception
{
    public SeparationFailedException(string message, IReadOnlyList<string> log, Exception? inner = null) : base(message, inner)
    {
        Log = log;
    }

    public IReadOnlyList<string> Log { get; }
}

/// <summary>
/// Runs Meta's open-source Demucs source-separation model in a private Python environment.
/// Nothing is simulated: when Demucs or the model is missing, the operation fails with an explanation.
/// </summary>
public sealed class DemucsService
{
    // Versions known to work together on Windows (torch 2.5 still saves WAV through soundfile).
    public const string TorchVersion = "2.5.1";
    public const string DemucsVersion = "4.0.1";

    private readonly AppPaths _paths;

    public DemucsService(AppPaths paths)
    {
        _paths = paths;
    }

    public string EnvironmentFolder => Path.Combine(_paths.Models, "demucs-env");
    public string ManagedPython => Path.Combine(EnvironmentFolder, "Scripts", "python.exe");
    public string TorchHome => Path.Combine(_paths.Models, "torch");

    /// <summary>The Python used for separation: a custom path if set, else the managed environment.</summary>
    public PythonCommand? ResolveRuntimePython(string? customPythonPath)
    {
        if (!string.IsNullOrWhiteSpace(customPythonPath))
        {
            return File.Exists(customPythonPath) ? new PythonCommand(customPythonPath, Array.Empty<string>(), customPythonPath) : null;
        }

        return File.Exists(ManagedPython) ? new PythonCommand(ManagedPython, Array.Empty<string>(), "Built-in environment") : null;
    }

    /// <summary>Finds a system Python 3.10-3.12 to create the managed environment.</summary>
    public async Task<PythonCommand?> FindSystemPythonAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<PythonCommand>
        {
            new("py", new[] { "-3.11" }, "Python 3.11 (py launcher)"),
            new("py", new[] { "-3.12" }, "Python 3.12 (py launcher)"),
            new("py", new[] { "-3.10" }, "Python 3.10 (py launcher)"),
            new("python", Array.Empty<string>(), "python on PATH"),
            new("python3", Array.Empty<string>(), "python3 on PATH"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var result = await ProcessRunner.RunAsync(candidate.FileName,
                    candidate.With("-c", "import sys; print('PYVER', sys.version_info[0], sys.version_info[1])"),
                    null, null, cancellationToken).ConfigureAwait(false);
                var line = result.Lines.FirstOrDefault(l => l.StartsWith("PYVER", StringComparison.Ordinal));
                if (result.ExitCode != 0 || line is null)
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var major = int.Parse(parts[1], CultureInfo.InvariantCulture);
                var minor = int.Parse(parts[2], CultureInfo.InvariantCulture);
                if (major == 3 && minor is >= 10 and <= 12)
                {
                    return candidate with { Description = $"Python {major}.{minor} ({candidate.FileName})" };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    public async Task<DemucsStatus> CheckAsync(string? customPythonPath, CancellationToken cancellationToken)
    {
        var python = ResolveRuntimePython(customPythonPath);
        if (python is null)
        {
            var message = string.IsNullOrWhiteSpace(customPythonPath)
                ? "Demucs is not installed yet. Install Python 3.11 from python.org, then click 'Install Demucs'."
                : $"The Python executable '{customPythonPath}' was not found.";
            return new DemucsStatus(false, null, false, null, null, false, message);
        }

        try
        {
            var result = await ProcessRunner.RunAsync(python.FileName,
                python.With("-c", "import torch, demucs; print('WLS_OK', demucs.__version__, torch.__version__, torch.cuda.is_available())"),
                TorchEnvironment(), null, cancellationToken).ConfigureAwait(false);
            var ok = result.Lines.FirstOrDefault(l => l.StartsWith("WLS_OK", StringComparison.Ordinal));
            if (result.ExitCode != 0 || ok is null)
            {
                return new DemucsStatus(true, python.Description, false, null, null, false, DemucsOutputParser.ExplainFailure(result.Lines));
            }

            var parts = ok.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cuda = parts.Length > 3 && parts[3].Equals("True", StringComparison.OrdinalIgnoreCase);
            var device = cuda ? "NVIDIA GPU available" : "CPU only (separation of a 4-minute song takes a few minutes)";
            return new DemucsStatus(true, python.Description, true, parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2), cuda,
                $"Demucs {parts.ElementAtOrDefault(1)} ready. {device}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DemucsStatus(false, python.Description, false, null, null, false, ex.Message);
        }
    }

    /// <summary>Creates the private environment and installs PyTorch + Demucs (internet connection required, ~1-3 GB).</summary>
    public async Task InstallAsync(bool useNvidiaGpu, Action<string> log, CancellationToken cancellationToken)
    {
        DiskSpaceGuard.EnsureAvailable(_paths.Models, 6L * 1024 * 1024 * 1024);
        var systemPython = await FindSystemPythonAsync(cancellationToken).ConfigureAwait(false)
                           ?? throw new SeparationFailedException(
                               "Python 3.10, 3.11 or 3.12 was not found. Install Python 3.11 from https://www.python.org/downloads/windows/ (tick 'Add python.exe to PATH'), then try again.",
                               Array.Empty<string>());
        log($"Using {systemPython.Description}");
        Directory.CreateDirectory(_paths.Models);

        if (!File.Exists(ManagedPython))
        {
            log("Creating the private Python environment...");
            await RunStep(systemPython.FileName, systemPython.With("-m", "venv", EnvironmentFolder), log, cancellationToken).ConfigureAwait(false);
        }

        var python = ManagedPython;
        log("Upgrading pip...");
        await RunStep(python, new[] { "-m", "pip", "install", "--upgrade", "pip" }, log, cancellationToken).ConfigureAwait(false);

        var index = useNvidiaGpu ? "https://download.pytorch.org/whl/cu121" : "https://download.pytorch.org/whl/cpu";
        log($"Installing PyTorch {TorchVersion} ({(useNvidiaGpu ? "NVIDIA CUDA" : "CPU")}). This is a large download...");
        await RunStep(python, new[] { "-m", "pip", "install", $"torch=={TorchVersion}", $"torchaudio=={TorchVersion}", "--index-url", index },
            log, cancellationToken).ConfigureAwait(false);

        log($"Installing Demucs {DemucsVersion}...");
        await RunStep(python, new[] { "-m", "pip", "install", $"demucs=={DemucsVersion}", "soundfile" }, log, cancellationToken).ConfigureAwait(false);
        log("Installation finished. Click 'Download model' to fetch the separation model now (optional, otherwise it downloads on first use).");
    }

    public async Task DownloadModelAsync(string? customPythonPath, string modelName, Action<string> log, CancellationToken cancellationToken)
    {
        var python = ResolveRuntimePython(customPythonPath)
                     ?? throw new SeparationFailedException("Demucs is not installed. Click 'Install Demucs' first.", Array.Empty<string>());
        DiskSpaceGuard.EnsureAvailable(_paths.Models, 1024L * 1024 * 1024);
        log($"Downloading model '{modelName}' into {TorchHome}...");
        var result = await ProcessRunner.RunAsync(python.FileName,
            python.With("-c", $"from demucs.pretrained import get_model; get_model('{SanitizeModel(modelName)}'); print('WLS_MODEL_OK')"),
            TorchEnvironment(), log, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !result.Lines.Any(l => l.Contains("WLS_MODEL_OK", StringComparison.Ordinal)))
        {
            throw new SeparationFailedException(DemucsOutputParser.ExplainFailure(result.Lines), result.Lines);
        }

        log("Model downloaded.");
    }

    public async Task<SeparationResult> SeparateAsync(SeparationRequest request, string? customPythonPath,
        IProgress<double> progress, Action<string> log, CancellationToken cancellationToken)
    {
        var python = ResolveRuntimePython(customPythonPath)
                     ?? throw new SeparationFailedException("Demucs is not installed. Click 'Install Demucs' first.", Array.Empty<string>());
        if (!File.Exists(request.InputWavPath))
        {
            throw new SeparationFailedException("The prepared input file is missing.", Array.Empty<string>());
        }

        var model = DemucsModels.Find(request.ModelName);
        var inputSize = new FileInfo(request.InputWavPath).Length;
        DiskSpaceGuard.EnsureAvailable(_paths.SeparatedStems, inputSize * (model.Stems.Count + 2) + 500L * 1024 * 1024);

        var work = Path.Combine(_paths.Temp, "separate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var args = new List<string> { "-m", "demucs", "-n", model.Name, "-o", work };
            if (request.Device is "cpu" or "cuda")
            {
                args.Add("-d");
                args.Add(request.Device);
            }

            args.Add(request.InputWavPath);
            log($"Running Demucs model '{model.Name}'...");
            var result = await ProcessRunner.RunAsync(python.FileName, python.With(args.ToArray()), TorchEnvironment(), line =>
            {
                if (DemucsOutputParser.TryParseProgress(line, out var pct))
                {
                    progress.Report(pct);
                }
                else
                {
                    log(line);
                }
            }, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new SeparationFailedException(DemucsOutputParser.ExplainFailure(result.Lines), result.Lines);
            }

            var produced = Directory.EnumerateFiles(work, "*.wav", SearchOption.AllDirectories).ToList();
            if (produced.Count == 0)
            {
                throw new SeparationFailedException("Demucs finished but produced no stems.", result.Lines);
            }

            var safeSong = Core.Projects.ProjectStore.SanitizeName(request.SongName);
            var output = Path.Combine(_paths.SeparatedStems, $"{safeSong}_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(output);
            var stems = new List<SeparatedStem>();
            foreach (var file in produced)
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                var target = Path.Combine(output, stem + ".wav");
                File.Move(file, target, true);
                stems.Add(new SeparatedStem(stem, DemucsModels.StemDisplayName(stem), target));
            }

            var order = model.Stems.ToList();
            stems = stems.OrderBy(s => order.IndexOf(s.Stem) < 0 ? 99 : order.IndexOf(s.Stem)).ToList();
            progress.Report(100);
            log($"Done. {stems.Count} stems saved to {output}");
            return new SeparationResult(output, stems);
        }
        finally
        {
            TryDelete(work);
        }
    }

    /// <summary>Reopens a folder of previously separated stems.</summary>
    public static SeparationResult LoadExisting(string folder)
    {
        var stems = Directory.EnumerateFiles(folder, "*.wav", SearchOption.TopDirectoryOnly)
            .Select(f => new SeparatedStem(Path.GetFileNameWithoutExtension(f), DemucsModels.StemDisplayName(Path.GetFileNameWithoutExtension(f)), f))
            .ToList();
        return new SeparationResult(folder, stems);
    }

    private Dictionary<string, string> TorchEnvironment() => new()
    {
        ["TORCH_HOME"] = TorchHome,
    };

    private async Task RunStep(string fileName, IEnumerable<string> args, Action<string> log, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(fileName, args, TorchEnvironment(), log, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new SeparationFailedException($"Installation step failed (exit code {result.ExitCode}). Check your internet connection and the log.", result.Lines);
        }
    }

    private static string SanitizeModel(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception)
        {
            // Temp leftovers are harmless.
        }
    }
}
