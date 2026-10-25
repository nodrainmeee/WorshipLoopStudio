namespace WorshipLoopStudio.Core.Samples;

internal sealed class NoiseSource
{
    private uint _state;

    public NoiseSource(uint seed)
    {
        _state = seed == 0 ? 0x9E3779B9u : seed;
    }

    /// <summary>White noise in the range -1..1 (xorshift32, deterministic).</summary>
    public double Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state / (double)uint.MaxValue * 2.0 - 1.0;
    }

    public double NextUnit() => (Next() + 1.0) * 0.5;
}

/// <summary>RBJ biquad filter, transposed direct form II.</summary>
internal sealed class Biquad
{
    private readonly double _b0, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public static Biquad LowPass(int sampleRate, double frequency, double q)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad HighPass(int sampleRate, double frequency, double q)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad BandPass(int sampleRate, double frequency, double q)
    {
        var (cos, alpha) = Prepare(sampleRate, frequency, q);
        return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public double Process(double x)
    {
        var y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    private static (double Cos, double Alpha) Prepare(int sampleRate, double frequency, double q)
    {
        var f = Math.Clamp(frequency, 10, sampleRate * 0.45);
        var w0 = 2 * Math.PI * f / sampleRate;
        return (Math.Cos(w0), Math.Sin(w0) / (2 * q));
    }
}
