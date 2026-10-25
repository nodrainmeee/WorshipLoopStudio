using NAudio.Wave;
using WorshipLoopStudio.Audio.Engine;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.Tests;

public class LoopEngineTests
{
    private const int Rate = EngineFormat.SampleRate;

    private static CachedSample Impulse()
    {
        var data = new float[20];
        data[0] = 1f;
        data[1] = 1f;
        return new CachedSample("impulse", data);
    }

    private static EngineArrangement Arrangement(double bpm, int bars, int[] steps, int countIn = 0, bool metronome = false,
        bool mute = false, int beatsPerBar = 4, int stepsPerBeat = 4, string id = "t1")
    {
        var total = beatsPerBar * stepsPerBeat * bars;
        var velocities = new float[total];
        foreach (var s in steps)
        {
            velocities[s] = 1f;
        }

        return new EngineArrangement
        {
            Bpm = bpm,
            BeatsPerBar = beatsPerBar,
            StepsPerBeat = stepsPerBeat,
            Bars = bars,
            CountInBars = countIn,
            MetronomeEnabled = metronome,
            MetronomeVolume = 1f,
            MasterVolume = 1f,
            Tracks = new[] { new EngineTrack(id, Impulse(), velocities, false) { Volume = 1f, Pan = 0f, Mute = mute } },
        };
    }

    /// <summary>Renders with deliberately odd buffer sizes, like a real sound card callback.</summary>
    private static float[] Render(LoopEngine engine, long frames)
    {
        var output = new float[frames * 2];
        var sizes = new[] { 441, 512, 333, 1024, 97 };
        long written = 0;
        var i = 0;
        var buffer = new float[2048];
        while (written < frames)
        {
            var n = (int)Math.Min(sizes[i++ % sizes.Length], frames - written);
            engine.Read(buffer, 0, n * 2);
            Array.Copy(buffer, 0, output, written * 2, n * 2);
            written += n;
        }

        return output;
    }

    private static List<long> Onsets(float[] stereo, float threshold = 0.95f)
    {
        var list = new List<long>();
        for (long f = 0; f < stereo.Length / 2; f++)
        {
            if (stereo[f * 2] >= threshold)
            {
                list.Add(f);
            }
        }

        return list;
    }

    [Theory]
    [InlineData(120)]
    [InlineData(87)]
    [InlineData(143.5)]
    public void Steps_AreSampleAccurate_AndLoopSeamlessly(double bpm)
    {
        var engine = new LoopEngine();
        engine.SetArrangement(Arrangement(bpm, 1, new[] { 0, 4, 8, 12, 15 }));
        engine.Start();

        var fps = TempoMath.FramesPerStep(bpm, Rate, 4);
        var loop = TempoMath.LoopLengthFrames(16, fps);
        var output = Render(engine, loop * 3);

        var expected = new List<long>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            foreach (var s in new[] { 0, 4, 8, 12, 15 })
            {
                expected.Add(cycle * loop + TempoMath.StepStartFrame(s, fps));
            }
        }

        Assert.Equal(expected, Onsets(output));
    }

    [Fact]
    public void Playback_ContinuesUntilStopped()
    {
        var engine = new LoopEngine();
        engine.SetArrangement(Arrangement(120, 1, new[] { 0 }));
        engine.Start();
        var loop = TempoMath.LoopLengthFrames(16, TempoMath.FramesPerStep(120, Rate, 4));
        var output = Render(engine, loop * 20);
        Assert.Equal(20, Onsets(output).Count);
        Assert.Equal(EngineTransport.Playing, engine.Transport);
    }

    [Fact]
    public void CountIn_DelaysTheLoopByWholeBars()
    {
        var engine = new LoopEngine();
        engine.SetArrangement(Arrangement(120, 1, new[] { 0 }, countIn: 1, metronome: true));
        engine.Start();
        var output = Render(engine, 22050 * 6);

        // Four count-in clicks (each below 0.95) and then the first step at exactly one bar.
        var onsets = Onsets(output);
        Assert.Equal(88200, onsets.First());
        Assert.True(output.Take(88200 * 2).Max() > 0.3f, "count-in clicks should be audible");
    }

    [Fact]
    public void StopAtEndOfBar_DoesNotTriggerTheNextBar()
    {
        var engine = new LoopEngine();
        engine.SetArrangement(Arrangement(120, 2, new[] { 0, 16 }));
        engine.Start();
        var first = Render(engine, 1000);
        engine.Stop(StopMode.EndOfBar);
        var rest = Render(engine, 88200 * 3);

        Assert.Equal(new long[] { 0 }, Onsets(first));
        Assert.Empty(Onsets(rest));
        Assert.Equal(EngineTransport.Stopped, engine.Transport);
    }

    [Fact]
    public void StopAtEndOfLoop_PlaysTheWholeLoop()
    {
        var engine = new LoopEngine();
        engine.SetArrangement(Arrangement(120, 2, new[] { 0, 16 }));
        engine.Start();
        var first = Render(engine, 1000);
        engine.Stop(StopMode.EndOfLoop);
        var rest = Render(engine, 88200 * 4);

        Assert.Equal(new long[] { 88200 - 1000 }, Onsets(rest));
        Assert.Single(Onsets(first));
        Assert.Equal(EngineTransport.Stopped, engine.Transport);
    }

    [Fact]
    public void MutedTrack_IsSilent()
    {
        var engine = new LoopEngine();
        engine.SetArrangement(Arrangement(120, 1, new[] { 0, 4, 8, 12 }, mute: true));
        engine.Start();
        var output = Render(engine, 88200);
        Assert.Equal(0f, output.Max(x => Math.Abs(x)));
    }

    [Fact]
    public void TempoChange_WhilePlaying_DoesNotRetriggerSteps()
    {
        var engine = new LoopEngine();
        var steps = Enumerable.Range(0, 16).ToArray();
        engine.SetArrangement(Arrangement(120, 1, steps));
        engine.Start();
        var a = Render(engine, 10_000);
        engine.SetArrangement(Arrangement(100, 1, steps));
        var b = Render(engine, 200_000);

        var all = Onsets(a).Concat(Onsets(b).Select(o => o + 10_000)).ToList();
        var minGap = all.Zip(all.Skip(1), (x, y) => y - x).Min();
        Assert.True(minGap > 5000, $"steps were retriggered (gap {minGap})");
        Assert.Equal(EngineTransport.Playing, engine.Transport);
    }

    [Fact]
    public void Offline_Render_WritesExactLength()
    {
        var dir = ProjectTests.TempDir();
        var path = Path.Combine(dir, "mix.wav");
        var arrangement = Arrangement(120, 1, new[] { 0, 8 }).ForExport();
        var duration = AudioExporter.RenderToWav(arrangement, 4, 1.0, path);

        Assert.Equal(TimeSpan.FromSeconds((88200 * 4 + 44100) / 44100.0), duration);
        using var reader = new WaveFileReader(path);
        Assert.Equal(44100, reader.WaveFormat.SampleRate);
        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal((88200L * 4 + 44100) * 4, reader.Length);
    }

    [Fact]
    public void ExportOfSingleTrack_IgnoresMuteAndSolo()
    {
        var arrangement = Arrangement(120, 1, new[] { 0 }, mute: true);
        var export = arrangement.ForExport("t1");
        Assert.Single(export.Tracks);
        Assert.False(export.Tracks[0].Mute);
        Assert.False(export.MetronomeEnabled);
        Assert.Equal(0, export.CountInBars);
    }
}
