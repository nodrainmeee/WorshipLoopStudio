using WorshipLoopStudio.Audio.IO;

namespace WorshipLoopStudio.Audio.Engine;

public enum EngineTransport
{
    Stopped = 0,
    CountIn = 1,
    Playing = 2,
}

public enum StopMode
{
    /// <summary>Stop right away and fade every sound out quickly.</summary>
    Immediate,

    /// <summary>Stop right away but let ringing sounds (gongs, cymbals) finish naturally.</summary>
    Release,

    /// <summary>Stop at the start of the next bar.</summary>
    EndOfBar,

    /// <summary>Stop when the loop reaches its end.</summary>
    EndOfLoop,
}

/// <summary>Immutable snapshot of one track, handed from the UI thread to the audio thread.</summary>
public sealed class EngineTrack
{
    public EngineTrack(string id, CachedSample sample, float[] stepVelocities, bool isAudioLoop)
    {
        Id = id;
        Sample = sample;
        StepVelocities = stepVelocities;
        IsAudioLoop = isAudioLoop;
    }

    public string Id { get; }
    public CachedSample Sample { get; }

    /// <summary>One entry per step; 0 means the step is off.</summary>
    public float[] StepVelocities { get; }

    public bool IsAudioLoop { get; }
    public float Volume { get; init; } = 0.8f;
    public float Pan { get; init; }
    public bool Mute { get; init; }
    public bool Solo { get; init; }

    // Gain smoothing state, owned by the audio thread.
    internal float GainL;
    internal float GainR;
    internal float TargetL;
    internal float TargetR;
    internal bool GainInitialized;

    public EngineTrack CloneForExport(bool forceAudible)
    {
        return new EngineTrack(Id, Sample, StepVelocities, IsAudioLoop)
        {
            Volume = Volume,
            Pan = Pan,
            Mute = !forceAudible && Mute,
            Solo = !forceAudible && Solo,
        };
    }
}

/// <summary>Immutable snapshot of the whole arrangement. Replaced atomically whenever the user edits something.</summary>
public sealed class EngineArrangement
{
    public static readonly EngineArrangement Empty = new();

    public double Bpm { get; init; } = 120;
    public int BeatsPerBar { get; init; } = 4;
    public int StepsPerBeat { get; init; } = 4;
    public int Bars { get; init; } = 1;
    public EngineTrack[] Tracks { get; init; } = Array.Empty<EngineTrack>();
    public bool MetronomeEnabled { get; init; }
    public float MetronomeVolume { get; init; } = 0.6f;
    public int CountInBars { get; init; }
    public float MasterVolume { get; init; } = 0.9f;

    public int StepsPerBar => BeatsPerBar * StepsPerBeat;
    public int TotalSteps => StepsPerBar * Math.Max(1, Bars);

    /// <summary>Copy used for offline rendering: no metronome, no count-in, optionally a single track.</summary>
    public EngineArrangement ForExport(string? onlyTrackId = null)
    {
        var tracks = onlyTrackId is null
            ? Tracks.Select(t => t.CloneForExport(false)).ToArray()
            : Tracks.Where(t => t.Id == onlyTrackId).Select(t => t.CloneForExport(true)).ToArray();
        return new EngineArrangement
        {
            Bpm = Bpm,
            BeatsPerBar = BeatsPerBar,
            StepsPerBeat = StepsPerBeat,
            Bars = Bars,
            Tracks = tracks,
            MetronomeEnabled = false,
            CountInBars = 0,
            MetronomeVolume = 0,
            MasterVolume = MasterVolume,
        };
    }
}

internal enum EngineCommandKind
{
    Start,
    Stop,
    Preview,
    StopPreview,
    TriggerTrack,
    Panic,
}

internal sealed class EngineCommand
{
    public EngineCommandKind Kind { get; init; }
    public StopMode StopMode { get; init; }
    public CachedSample? Sample { get; init; }
    public string? TrackId { get; init; }
    public float Value { get; init; } = 1f;
}

internal sealed class Voice
{
    public bool Active;
    public float[] Data = Array.Empty<float>();
    public int Position;
    public int Frames;
    public float Velocity;
    public EngineTrack? Track;
    public float FixedGainL;
    public float FixedGainR;
    public int FadeRemaining = -1;
    public long Age;
    public bool IsPreview;
}
