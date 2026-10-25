using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.Tests;

public class TempoMathTests
{
    [Theory]
    [InlineData(120, 44100, 22050)]
    [InlineData(60, 44100, 44100)]
    [InlineData(90, 48000, 32000)]
    public void FramesPerBeat_IsExact(double bpm, int rate, double expected)
    {
        Assert.Equal(expected, TempoMath.FramesPerBeat(bpm, rate), 6);
    }

    [Fact]
    public void FramesPerStep_DividesBeat()
    {
        Assert.Equal(5512.5, TempoMath.FramesPerStep(120, 44100, 4), 6);
    }

    [Fact]
    public void StepPositions_DoNotDrift_OverLongPerformances()
    {
        var fps = TempoMath.FramesPerStep(87, 44100, 4);
        const int steps = 100_000;
        var expected = steps * fps;
        Assert.InRange(TempoMath.StepStartFrame(steps, fps), expected - 1, expected + 1);
    }

    [Fact]
    public void LoopLength_MatchesBarDuration()
    {
        var fps = TempoMath.FramesPerStep(120, 44100, 4);
        var total = TempoMath.TotalSteps(TimeSignature.FourFour, 4, 2);
        Assert.Equal(32, total);
        Assert.Equal(176400, TempoMath.LoopLengthFrames(total, fps));
        Assert.Equal(4.0, TempoMath.LoopDurationSeconds(120, TimeSignature.FourFour, 2), 9);
    }

    [Theory]
    [InlineData(10, 30)]
    [InlineData(1000, 300)]
    [InlineData(127.456, 127.46)]
    [InlineData(double.NaN, TempoMath.DefaultBpm)]
    public void ClampBpm_KeepsSafeRange(double input, double expected)
    {
        Assert.Equal(expected, TempoMath.ClampBpm(input), 6);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(1, 100, 100)]
    [InlineData(100, 100, 100)]
    [InlineData(101, 100, 200)]
    public void NextBoundary_RoundsUp(long position, long boundary, long expected)
    {
        Assert.Equal(expected, TempoMath.NextBoundary(position, boundary));
    }

    [Fact]
    public void TapTempo_AveragesIntervals()
    {
        var taps = new[] { 0.0, 0.5, 1.0, 1.5, 2.0 };
        Assert.Equal(120, TempoMath.TapTempo(taps)!.Value, 3);
        Assert.Null(TempoMath.TapTempo(new[] { 1.0 }));
    }

    [Theory]
    [InlineData("4/4", 4, 4)]
    [InlineData("6/8", 6, 8)]
    [InlineData(" 7 / 8 ", 7, 8)]
    public void TimeSignature_Parses(string text, int n, int d)
    {
        Assert.Equal(new TimeSignature(n, d), TimeSignature.Parse(text));
    }

    [Theory]
    [InlineData("4/3")]
    [InlineData("0/4")]
    [InlineData("abc")]
    public void TimeSignature_RejectsInvalid(string text)
    {
        Assert.False(TimeSignature.TryParse(text, out _));
    }

    [Fact]
    public void Quantization_StepsPerBeat()
    {
        Assert.Equal(4, Quantization.Sixteenth.StepsPerBeat());
        Assert.Equal(3, Quantization.EighthTriplet.StepsPerBeat());
        Assert.Equal(6, Quantization.SixteenthTriplet.StepsPerBeat());
    }
}
