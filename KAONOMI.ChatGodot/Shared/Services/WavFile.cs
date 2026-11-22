using System;
using System.IO;
using System.Text;

namespace KAONOMI.Chat.Services;

/// <summary>
/// WAV files read and written without a library: PCM 8, 16, 24 or 32 bits or 32-bit float, mono or stereo (more
/// channels: the first two), any rate (brought to 44.1 kHz); written in 16-bit stereo.
/// </summary>
public static class WavFile
{
    public const int Rate = 44100;

    /// <summary>The samples of a WAV file, left and right, at 44.1 kHz (-1 to 1).</summary>
    public static (float[] Left, float[] Right) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            throw new InvalidDataException("Not a WAV file (no RIFF header)");
        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            throw new InvalidDataException("Not a WAV file (no WAVE header)");
        int format = 0, channels = 0, rate = 0, bits = 0;
        byte[]? data = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            var next = reader.BaseStream.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                // WAVE_FORMAT_EXTENSIBLE: the true format in the sub-format.
                if (format == 0xFFFE && size >= 26)
                {
                    reader.ReadInt16();
                    reader.ReadInt16();
                    reader.ReadInt32();
                    format = reader.ReadInt16();
                }
            }
            else if (id == "data")
                data = reader.ReadBytes((int)Math.Min(size, reader.BaseStream.Length - reader.BaseStream.Position));
            if (data is not null && format != 0)
                break;
            reader.BaseStream.Position = Math.Min(next, reader.BaseStream.Length);
        }
        if (data is null || channels <= 0 || rate <= 0)
            throw new InvalidDataException("A WAV file without its format or its sound");
        if (format != 1 && format != 3)
            throw new InvalidDataException($"A compressed WAV file (format {format}): only PCM and float are read");

        var bytes = bits / 8;
        var frames = data.Length / (bytes * channels);
        var left = new float[frames];
        var right = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var at = f * bytes * channels;
            left[f] = SampleAt(data, at, bits, format);
            right[f] = channels > 1 ? SampleAt(data, at + bytes, bits, format) : left[f];
        }
        return rate == Rate ? (left, right) : (Resample(left, rate), Resample(right, rate));
    }

    private static float SampleAt(byte[] data, int at, int bits, int format) => (format, bits) switch
    {
        (3, 32) => BitConverter.ToSingle(data, at),
        (3, 64) => (float)BitConverter.ToDouble(data, at),
        (_, 8) => (data[at] - 128) / 128f,
        (_, 16) => BitConverter.ToInt16(data, at) / 32768f,
        (_, 24) => ((data[at] | data[at + 1] << 8 | (sbyte)data[at + 2] << 16)) / 8388608f,
        (_, 32) => BitConverter.ToInt32(data, at) / 2147483648f,
        _ => throw new InvalidDataException($"{bits}-bit samples are not read")
    };

    private static float[] Resample(float[] samples, int rate)
    {
        var length = (int)((long)samples.Length * Rate / rate);
        var result = new float[length];
        var step = rate / (double)Rate;
        for (var i = 0; i < length; i++)
        {
            var x = i * step;
            var a = (int)x;
            var b = Math.Min(a + 1, samples.Length - 1);
            result[i] = (float)(samples[a] + (samples[b] - samples[a]) * (x - a));
        }
        return result;
    }

    /// <summary>Writes a 16-bit stereo WAV file at 44.1 kHz.</summary>
    public static void Write(string path, float[] left, float[] right)
    {
        var frames = Math.Min(left.Length, right.Length);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + frames * 4);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(Rate);
        writer.Write(Rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(frames * 4);
        for (var i = 0; i < frames; i++)
        {
            writer.Write((short)Math.Round(Math.Clamp(left[i], -1f, 1f) * 32767));
            writer.Write((short)Math.Round(Math.Clamp(right[i], -1f, 1f) * 32767));
        }
    }
}
