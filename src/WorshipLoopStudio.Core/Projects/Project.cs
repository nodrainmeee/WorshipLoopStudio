using WorshipLoopStudio.Core.Samples;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.Core.Projects;

public enum TrackKind
{
    /// <summary>A one-shot sample triggered by the step sequencer.</summary>
    Pattern,

    /// <summary>An audio clip (recording or imported loop) restarted at the start of every loop cycle.</summary>
    AudioLoop,
}

public sealed class StepEvent
{
    public int Index { get; set; }
    public float Velocity { get; set; } = 1f;
}

public sealed class TrackModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Track";
    public TrackKind Kind { get; set; } = TrackKind.Pattern;

    /// <summary>"starter:&lt;id&gt;" for built-in sounds, otherwise an absolute or project-relative file path.</summary>
    public string SampleRef { get; set; } = string.Empty;

    public double Volume { get; set; } = 0.8;
    public double Pan { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public List<StepEvent> Steps { get; set; } = new();
}

public sealed class MetronomeSettings
{
    public bool Enabled { get; set; } = true;
    public double Volume { get; set; } = 0.6;
    public int CountInBars { get; set; } = 1;
}

public sealed class Project
{
    public const int CurrentFormatVersion = 1;
    public const int MaxTracks = 32;
    public static readonly int[] AllowedBars = { 1, 2, 4, 8 };

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get; set; } = "Untitled";
    public double Bpm { get; set; } = TempoMath.DefaultBpm;
    public int TimeSignatureNumerator { get; set; } = 4;
    public int TimeSignatureDenominator { get; set; } = 4;
    public Quantization Quantization { get; set; } = Quantization.Sixteenth;
    public int Bars { get; set; } = 2;
    public MetronomeSettings Metronome { get; set; } = new();
    public double MasterVolume { get; set; } = 0.9;
    public List<TrackModel> Tracks { get; set; } = new();
    public List<string> RecordingReferences { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public TimeSignature GetTimeSignature()
    {
        var ts = new TimeSignature(TimeSignatureNumerator, TimeSignatureDenominator);
        return ts.IsValid ? ts : TimeSignature.FourFour;
    }

    public void SetTimeSignature(TimeSignature signature)
    {
        TimeSignatureNumerator = signature.Numerator;
        TimeSignatureDenominator = signature.Denominator;
    }

    public PatternGrid GetGrid() => new(GetTimeSignature().Numerator, this.Quantization.StepsPerBeat(), Bars);

    /// <summary>Brings every value into a safe range. Called after loading a file.</summary>
    public void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Untitled" : Name.Trim();
        Bpm = TempoMath.ClampBpm(Bpm);
        var ts = GetTimeSignature();
        SetTimeSignature(ts);
        if (!Enum.IsDefined(this.Quantization))
        {
            Quantization = Quantization.Sixteenth;
        }

        if (!AllowedBars.Contains(Bars))
        {
            Bars = AllowedBars.OrderBy(b => Math.Abs(b - Bars)).First();
        }

        Metronome ??= new MetronomeSettings();
        Metronome.Volume = Math.Clamp(Metronome.Volume, 0, 1);
        Metronome.CountInBars = Math.Clamp(Metronome.CountInBars, 0, 4);
        MasterVolume = Math.Clamp(MasterVolume, 0, 1.5);
        Tracks ??= new List<TrackModel>();
        RecordingReferences ??= new List<string>();
        if (Tracks.Count > MaxTracks)
        {
            Tracks = Tracks.Take(MaxTracks).ToList();
        }

        var total = GetGrid().TotalSteps;
        foreach (var track in Tracks)
        {
            track.Id = string.IsNullOrWhiteSpace(track.Id) ? Guid.NewGuid().ToString("N") : track.Id;
            track.Name = string.IsNullOrWhiteSpace(track.Name) ? "Track" : track.Name;
            track.SampleRef ??= string.Empty;
            track.Volume = Math.Clamp(track.Volume, 0, 1.5);
            track.Pan = Math.Clamp(track.Pan, -1, 1);
            track.Steps = (track.Steps ?? new List<StepEvent>())
                .Where(s => s.Index >= 0 && s.Index < total)
                .GroupBy(s => s.Index)
                .Select(g => new StepEvent { Index = g.Key, Velocity = Math.Clamp(g.Max(s => s.Velocity), 0.05f, 1f) })
                .OrderBy(s => s.Index)
                .ToList();
        }
    }

    /// <summary>A ready-to-play worship groove built from the starter library.</summary>
    public static Project CreateDefault(string name)
    {
        var project = new Project { Name = name, Bpm = 80, Bars = 2, Quantization = Quantization.Sixteenth };
        var stepsPerBar = 16;

        TrackModel Track(string trackName, string sampleId, double volume, double pan, params int[] stepsInBar)
        {
            var track = new TrackModel
            {
                Name = trackName,
                SampleRef = SampleRefs.Starter(sampleId),
                Volume = volume,
                Pan = pan,
            };
            for (var bar = 0; bar < project.Bars; bar++)
            {
                foreach (var s in stepsInBar)
                {
                    track.Steps.Add(new StepEvent { Index = bar * stepsPerBar + s, Velocity = 1f });
                }
            }

            return track;
        }

        project.Tracks.Add(Track("Kick", "kick", 0.9, 0, 0, 10));
        project.Tracks.Add(Track("Clap", "clap", 0.6, 0, 4, 12));
        project.Tracks.Add(Track("Gangan", "gangan_open", 0.75, -0.25, 3, 6, 11, 14));
        project.Tracks.Add(Track("Shaker", "shaker", 0.45, 0.3, 0, 2, 4, 6, 8, 10, 12, 14));
        project.Tracks.Add(Track("Djembe", "djembe_bass", 0.7, 0.15, 7, 15));
        return project;
    }
}
