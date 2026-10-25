namespace WorshipLoopStudio.Separation;

public sealed record DemucsModelInfo(string Name, string DisplayName, IReadOnlyList<string> Stems, string Notes)
{
    public override string ToString() => DisplayName;
}

public static class DemucsModels
{
    public static IReadOnlyList<DemucsModelInfo> All { get; } = new[]
    {
        new DemucsModelInfo("htdemucs_6s", "6 stems (vocals, drums, bass, guitar, piano, other)",
            new[] { "vocals", "drums", "bass", "guitar", "piano", "other" },
            "Hybrid Transformer Demucs, 6 sources. Piano separation is still experimental in the model itself."),
        new DemucsModelInfo("htdemucs", "4 stems (vocals, drums, bass, other) - fast",
            new[] { "vocals", "drums", "bass", "other" },
            "Default Demucs v4 model."),
        new DemucsModelInfo("htdemucs_ft", "4 stems - fine-tuned, best quality, about 4x slower",
            new[] { "vocals", "drums", "bass", "other" },
            "Fine-tuned version of htdemucs."),
    };

    public static DemucsModelInfo Find(string? name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static string StemDisplayName(string stem) => stem.ToLowerInvariant() switch
    {
        "vocals" => "Vocals",
        "drums" => "Drums & percussion",
        "bass" => "Bass",
        "guitar" => "Guitar",
        "piano" => "Piano & keys",
        "other" => "Other accompaniment",
        _ => stem,
    };
}
