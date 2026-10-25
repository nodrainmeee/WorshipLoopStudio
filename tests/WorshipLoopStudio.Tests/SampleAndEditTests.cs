using NAudio.Wave;
using WorshipLoopStudio.Core;
using WorshipLoopStudio.Core.Editing;
using WorshipLoopStudio.Core.Samples;
using WorshipLoopStudio.Separation;

namespace WorshipLoopStudio.Tests;

public class SampleAndEditTests
{
    public static IEnumerable<object[]> StarterIds() => StarterLibrary.All.Select(s => new object[] { s.Id });

    [Theory]
    [MemberData(nameof(StarterIds))]
    public void StarterSamples_AreAudibleNormalizedAndEndSilently(string id)
    {
        var data = StarterLibrary.Find(id)!.Generate();
        Assert.True(data.Length > 1000);
        Assert.DoesNotContain(data, x => float.IsNaN(x));
        var peak = data.Max(x => Math.Abs(x));
        Assert.InRange(peak, 0.85f, 0.9f);
        Assert.True(Math.Abs(data[^1]) < 0.001f);
    }

    [Fact]
    public void StarterLibrary_CoversRequiredInstruments()
    {
        var ids = StarterLibrary.All.Select(s => s.Id).ToList();
        foreach (var required in new[] { "gangan_open", "djembe_tone", "shekere", "agogo_high", "african_bell_high", "gong_large", "hihat_closed", "kick", "snare", "clap", "shaker" })
        {
            Assert.Contains(required, ids);
        }

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void StarterLibrary_WritesReadableWavFiles()
    {
        var dir = ProjectTests.TempDir();
        var written = StarterLibrary.EnsureGenerated(dir);
        Assert.Equal(StarterLibrary.All.Count, written);
        Assert.Equal(0, StarterLibrary.EnsureGenerated(dir));

        using var reader = new WaveFileReader(Path.Combine(dir, "kick.wav"));
        Assert.Equal(44100, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.True(reader.TotalTime > TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void SampleRefs_ResolveStarterAndRelativePaths()
    {
        var paths = new AppPaths(ProjectTests.TempDir());
        Assert.Equal(Path.Combine(paths.StarterSamples, "kick.wav"), SampleRefs.Resolve("starter:kick", paths, null));
        Assert.Equal(Path.GetFullPath(Path.Combine(@"C:\proj", "loops", "a.wav")), SampleRefs.Resolve(Path.Combine("loops", "a.wav"), paths, @"C:\proj"));
        Assert.Null(SampleRefs.Resolve("", paths, null));
        Assert.Equal("Kick", SampleRefs.DisplayName("starter:kick"));
    }

    [Fact]
    public void LoopRegion_ClampsToClip()
    {
        Assert.Equal(new LoopRegion(1, 2), LoopRegion.Clamp(2, 1, 5));
        Assert.Equal(new LoopRegion(0, 5), LoopRegion.Clamp(-3, 9, 5));
        var tiny = LoopRegion.Clamp(4.999, 4.999, 5);
        Assert.True(tiny.LengthSeconds >= LoopRegion.MinimumLengthSeconds - 1e-9);
        Assert.True(tiny.EndSeconds <= 5);
    }

    [Fact]
    public void Trim_CutsAndFadesWithoutClicks()
    {
        var data = Enumerable.Repeat(1f, 44100 * 2 * 2).ToArray();
        var trimmed = AudioEdit.Trim(data, 2, 44100, new LoopRegion(0.5, 1.5), 5);
        Assert.Equal(44100 * 2, trimmed.Length);
        Assert.Equal(0f, trimmed[0]);
        Assert.Equal(1f, trimmed[44100]);
        Assert.True(trimmed[^1] < 0.01f);
    }

    [Fact]
    public void WaveformPeaks_FindsMaxima()
    {
        var data = new float[200];
        data[10] = -0.8f;
        data[150] = 0.5f;
        var peaks = WaveformPeaks.Compute(data, 2, 4);
        Assert.Equal(4, peaks.Length);
        Assert.Equal(0.8f, peaks[0]);
        Assert.Equal(0.5f, peaks[3]);
    }

    [Fact]
    public void AppPaths_CreatesAllFolders()
    {
        var paths = new AppPaths(Path.Combine(ProjectTests.TempDir(), "WorshipLoopStudio"));
        paths.EnsureCreated();
        foreach (var folder in new[] { paths.Projects, paths.Loops, paths.Recordings, paths.Samples, paths.SeparatedStems, paths.Models, paths.Logs })
        {
            Assert.True(Directory.Exists(folder), folder);
        }
    }

    [Fact]
    public void DiskSpaceGuard_ThrowsWhenTooLittleSpace()
    {
        var dir = ProjectTests.TempDir();
        if (DiskSpaceGuard.GetAvailableBytes(dir) is null)
        {
            return;
        }

        Assert.Throws<InsufficientDiskSpaceException>(() => DiskSpaceGuard.EnsureAvailable(dir, long.MaxValue));
        DiskSpaceGuard.EnsureAvailable(dir, 1);
    }

    [Theory]
    [InlineData(" 42%|████      | 12.3/29.2 [00:10<00:14,  1.20seconds/s]", 42)]
    [InlineData("100%|██████████| 29.2/29.2 [00:24<00:00]", 100)]
    [InlineData("  7.5%|▋ | 1/14", 7.5)]
    public void DemucsProgress_IsParsed(string line, double expected)
    {
        Assert.True(DemucsOutputParser.TryParseProgress(line, out var pct));
        Assert.Equal(expected, pct, 3);
    }

    [Fact]
    public void DemucsProgress_IgnoresOtherLines()
    {
        Assert.False(DemucsOutputParser.TryParseProgress("Selected model is a bag of 1 models.", out _));
    }

    [Fact]
    public void DemucsFailures_AreExplained()
    {
        Assert.Contains("Install Demucs", DemucsOutputParser.ExplainFailure(new[] { "ModuleNotFoundError: No module named 'demucs'" }));
        Assert.Contains("CPU", DemucsOutputParser.ExplainFailure(new[] { "torch.cuda.OutOfMemoryError: CUDA out of memory" }));
    }

    [Fact]
    public void DemucsModels_SixStemModelHasAllRequestedStems()
    {
        var model = DemucsModels.Find("htdemucs_6s");
        Assert.Equal(new[] { "vocals", "drums", "bass", "guitar", "piano", "other" }, model.Stems);
        Assert.Equal("htdemucs_6s", DemucsModels.Find("unknown").Name);
    }
}
