using WorshipLoopStudio.Core.Samples;

// Generates the starter instrument library (original, procedurally synthesized WAV files).
// Usage: dotnet run --project tools/SampleGen -- <output folder>
var output = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "Samples", "Starter");
Directory.CreateDirectory(output);
var count = StarterLibrary.EnsureGenerated(output, overwrite: true);
Console.WriteLine($"Generated {count} starter samples in {Path.GetFullPath(output)}");
foreach (var sample in StarterLibrary.All)
{
    var file = Path.Combine(output, StarterLibrary.FileNameFor(sample.Id));
    Console.WriteLine($"  {sample.Category,-20} {sample.Name,-34} {new FileInfo(file).Length / 1024,6} KB");
}

return count == StarterLibrary.All.Count ? 0 : 1;
