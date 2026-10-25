namespace WorshipLoopStudio.Core.Samples;

public sealed record StarterSample(string Id, string Name, string Category, Func<float[]> Generate);

/// <summary>The built-in instrument library. Files are synthesized on first launch (and by the build pipeline).</summary>
public static class StarterLibrary
{
    public const string AfricanCategory = "African Percussion";
    public const string DrumsCategory = "Drum Kit";
    public const string CymbalsCategory = "Cymbals & Hi-Hats";
    public const string ShakersCategory = "Shakers";
    public const string GongsCategory = "Gongs";

    public static IReadOnlyList<StarterSample> All { get; } = new List<StarterSample>
    {
        new("gangan_open", "Gangan (talking drum) - open", AfricanCategory, () => PercussionSynth.Gangan(205, 205, 1)),
        new("gangan_up", "Gangan - pitch up", AfricanCategory, () => PercussionSynth.Gangan(165, 265, 0.07, seed: 15)),
        new("gangan_down", "Gangan - pitch down", AfricanCategory, () => PercussionSynth.Gangan(270, 165, 0.08, seed: 16)),
        new("gangan_low", "Gangan - low", AfricanCategory, () => PercussionSynth.Gangan(140, 140, 1, 1.0, 1.2, 17)),
        new("gangan_high", "Gangan - high short", AfricanCategory, () => PercussionSynth.Gangan(300, 300, 1, 0.6, 0.6, 18)),
        new("djembe_bass", "Djembe - bass", AfricanCategory, PercussionSynth.DjembeBass),
        new("djembe_tone", "Djembe - tone", AfricanCategory, PercussionSynth.DjembeTone),
        new("djembe_slap", "Djembe - slap", AfricanCategory, PercussionSynth.DjembeSlap),
        new("shekere", "Shekere", AfricanCategory, () => PercussionSynth.Shekere()),
        new("agogo_high", "Agogo - high", AfricanCategory, () => PercussionSynth.Agogo(930, 19)),
        new("agogo_low", "Agogo - low", AfricanCategory, () => PercussionSynth.Agogo(640, 20)),
        new("african_bell_high", "African bell - high", AfricanCategory, () => PercussionSynth.AfricanBell(1240, 21)),
        new("african_bell_low", "African bell - low", AfricanCategory, () => PercussionSynth.AfricanBell(830, 22)),

        new("kick", "Kick", DrumsCategory, () => PercussionSynth.Kick()),
        new("kick_deep", "Kick - deep", DrumsCategory, () => PercussionSynth.Kick(120, 40, 0.65, 1.0, 23)),
        new("snare", "Snare", DrumsCategory, () => PercussionSynth.Snare()),
        new("rim", "Rimshot", DrumsCategory, () => PercussionSynth.Rim()),
        new("clap", "Clap", DrumsCategory, () => PercussionSynth.Clap()),
        new("tom_high", "Tom - high", DrumsCategory, () => PercussionSynth.Tom(280, 210, 24)),
        new("tom_mid", "Tom - mid", DrumsCategory, () => PercussionSynth.Tom(200, 150, 25)),
        new("tom_low", "Tom - low", DrumsCategory, () => PercussionSynth.Tom(140, 98, 26)),

        new("hihat_closed", "Hi-hat - closed", CymbalsCategory, PercussionSynth.HiHatClosed),
        new("hihat_open", "Hi-hat - open", CymbalsCategory, PercussionSynth.HiHatOpen),
        new("crash", "Crash cymbal", CymbalsCategory, PercussionSynth.Crash),
        new("ride", "Ride cymbal", CymbalsCategory, PercussionSynth.Ride),

        new("shaker", "Shaker", ShakersCategory, () => PercussionSynth.Shaker()),
        new("shaker_long", "Shaker - long", ShakersCategory, () => PercussionSynth.Shaker(0.4, 0.1, 27)),

        new("gong_large", "Gong - large", GongsCategory, () => PercussionSynth.Gong(82, 1.0, 7.0, 28)),
        new("gong_small", "Gong - small", GongsCategory, () => PercussionSynth.Gong(165, 0.55, 4.0, 29)),
    };

    public static StarterSample? Find(string id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string FileNameFor(string id) => id + ".wav";

    /// <summary>Writes every missing starter sample to the folder. Returns the number of files written.</summary>
    public static int EnsureGenerated(string folder, bool overwrite = false)
    {
        Directory.CreateDirectory(folder);
        var written = 0;
        foreach (var sample in All)
        {
            var path = Path.Combine(folder, FileNameFor(sample.Id));
            if (!overwrite && File.Exists(path) && new FileInfo(path).Length > 44)
            {
                continue;
            }

            WavWriter.WriteMono16(path, sample.Generate(), PercussionSynth.SampleRate);
            written++;
        }

        return written;
    }

    /// <summary>Copies pre-built samples shipped next to the executable, then synthesizes whatever is still missing.</summary>
    public static int Install(string folder, string? shippedFolder)
    {
        Directory.CreateDirectory(folder);
        if (!string.IsNullOrEmpty(shippedFolder) && Directory.Exists(shippedFolder))
        {
            foreach (var sample in All)
            {
                var source = Path.Combine(shippedFolder, FileNameFor(sample.Id));
                var target = Path.Combine(folder, FileNameFor(sample.Id));
                if (File.Exists(source) && !File.Exists(target))
                {
                    File.Copy(source, target);
                }
            }
        }

        return EnsureGenerated(folder);
    }
}
