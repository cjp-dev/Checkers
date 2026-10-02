using Checkers.App.Models;

namespace Checkers.App.Services;

/// <summary>
/// Synthesizes procedural game sounds as 16-bit mono PCM WAV bytes without requiring external audio files.
/// </summary>
public static class SoundWaves
{
    public const int SampleRate = 22_050;
    private static readonly Dictionary<SoundType, byte[]> Cache = [];

    public static byte[] Create(SoundType sound)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(sound, out byte[]? wav))
            {
                wav = ToWav(sound switch
                {
                    SoundType.Move => Sweep(420, 180, 0.08, 0.7),
                    SoundType.Capture => DoubleClack(0.12),
                    SoundType.King => Notes([523.25, 659.25, 783.99, 1046.50], 0.10, 0.7),
                    SoundType.Win => Notes([523.25, 659.25, 783.99, 1046.50, 1318.5], 0.12, 0.75),
                    SoundType.Loss => Notes([392.00, 311.13, 261.63], 0.18, 0.6),
                    _ => Sweep(400, 200, 0.08, 0.5)
                });
                Cache[sound] = wav;
            }

            return wav;
        }
    }

    private static short[] Sweep(double fromHz, double toHz, double seconds, double volume)
    {
        int count = (int)(SampleRate * seconds);
        var samples = new short[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / count;
            phase += 2 * Math.PI * (fromHz + (toHz - fromHz) * t) / SampleRate;
            samples[i] = (short)(short.MaxValue * volume * Math.Exp(-5 * t) * Math.Sin(phase));
        }
        return samples;
    }

    private static short[] DoubleClack(double seconds)
    {
        int count = (int)(SampleRate * seconds);
        var samples = new short[count];
        int half = count / 2;

        for (int i = 0; i < half; i++)
        {
            double t = (double)i / half;
            double phase = 2 * Math.PI * (500 - 300 * t) * i / SampleRate;
            samples[i] = (short)(short.MaxValue * 0.75 * Math.Exp(-8 * t) * Math.Sin(phase));
        }

        for (int i = half; i < count; i++)
        {
            double t = (double)(i - half) / half;
            double phase = 2 * Math.PI * (450 - 250 * t) * (i - half) / SampleRate;
            samples[i] = (short)(short.MaxValue * 0.85 * Math.Exp(-8 * t) * Math.Sin(phase));
        }

        return samples;
    }

    private static short[] Notes(double[] frequencies, double seconds, double volume)
    {
        int noteLength = (int)(SampleRate * seconds);
        var samples = new short[noteLength * frequencies.Length];

        for (int note = 0; note < frequencies.Length; note++)
        {
            for (int i = 0; i < noteLength; i++)
            {
                double envelope = Math.Min(1, Math.Min(i, noteLength - i) / (SampleRate * 0.01));
                double value = Math.Sin(2 * Math.PI * frequencies[note] * i / SampleRate);
                samples[note * noteLength + i] = (short)(short.MaxValue * volume * envelope * value);
            }
        }

        return samples;
    }

    private static byte[] ToWav(short[] samples)
    {
        int dataChunkSize = samples.Length * sizeof(short);
        int fileSize = 44 + dataChunkSize;
        var bytes = new byte[fileSize];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(fileSize - 8);
        writer.Write("WAVE"u8);

        // fmt chunk
        writer.Write("fmt "u8);
        writer.Write(16); // Subchunk1Size (16 for PCM)
        writer.Write((short)1); // AudioFormat (1 for PCM)
        writer.Write((short)1); // NumChannels (1 = Mono)
        writer.Write(SampleRate); // SampleRate
        writer.Write(SampleRate * sizeof(short)); // ByteRate
        writer.Write((short)sizeof(short)); // BlockAlign
        writer.Write((short)16); // BitsPerSample

        // data chunk
        writer.Write("data"u8);
        writer.Write(dataChunkSize);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        return bytes;
    }
}
