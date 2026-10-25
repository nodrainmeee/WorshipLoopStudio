namespace WorshipLoopStudio.Core.Samples;

public static class WavWriter
{
    /// <summary>Writes a 16-bit PCM mono WAV file (through a temporary file).</summary>
    public static void WriteMono16(string path, float[] samples, int sampleRate)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            var dataBytes = samples.Length * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataBytes);
            foreach (var s in samples)
            {
                var v = Math.Clamp(Math.Round(s * 32767.0), -32768, 32767);
                writer.Write((short)v);
            }
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temp, path);
    }
}
