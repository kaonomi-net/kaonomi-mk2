using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PortAudioSharp;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The microphone and the loudspeakers of the chat (PortAudio): the devices of the system, the ones chosen with
/// <c>/audiosettings</c> (<see cref="Input"/> and <see cref="Output"/>, null for the default of the system), and a WAV
/// player on the chosen output. Only the devices of the host API of the default device are listed (on Windows the same
/// device is also seen through MME, DirectSound, WASAPI and WDM-KS).
/// </summary>
public static class AudioDevices
{
    private static readonly object InitLock = new();
    private static bool _ready;

    /// <summary>Name of the microphone chosen (null: the default microphone of the system).</summary>
    public static string? Input { get; set; }

    /// <summary>Name of the output chosen for the voice (null: the default player of the system).</summary>
    public static string? Output { get; set; }

    /// <summary>
    /// The gain of the microphone, in dB (-20 to +30; 0: as it comes), applied to what every window hears (the
    /// recorder, the live chat, the voice of /vision): a quiet microphone made louder, a loud one quieter. Kept in
    /// <c>~/kaonomi/audio/input-gain.txt</c>.
    /// </summary>
    public static double InputGainDb
    {
        get => _inputGainDb ??= ReadGain();
        set
        {
            _inputGainDb = Math.Clamp(value, MinGainDb, MaxGainDb);
            InputGain = (float)Math.Pow(10, _inputGainDb.Value / 20);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GainFile)!);
                File.WriteAllText(GainFile, _inputGainDb.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActivityLog.Info("Audio", "The gain of the microphone could not be kept: " + ex.Message);
            }
        }
    }

    public const double MinGainDb = -20, MaxGainDb = 30;

    /// <summary>The gain of the microphone as a factor (1: as it comes), read in the callback of the microphone.</summary>
    public static float InputGain { get; private set; } = (float)Math.Pow(10, ReadGain() / 20);

    private static double? _inputGainDb;

    private static string GainFile => Path.Combine(Models.ChatSettings.KaonomiHome, "audio", "input-gain.txt");

    private static double ReadGain()
    {
        try
        {
            if (File.Exists(GainFile) && double.TryParse(File.ReadAllText(GainFile).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var db))
                return Math.Clamp(db, MinGainDb, MaxGainDb);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // As it comes.
        }
        return 0;
    }

    /// <summary>Raised when the microphone chosen changes (the windows that listen open the new one).</summary>
    public static event Action? InputChanged;

    /// <summary>Chooses the microphone <paramref name="name"/> (null: the default of the system) and tells the windows that listen.</summary>
    public static void ChooseInput(string? name)
    {
        if (string.Equals(Input, name, StringComparison.Ordinal))
            return;
        Input = name;
        InputChanged?.Invoke();
    }

    /// <summary>Starts PortAudio once for the whole application.</summary>
    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_ready)
                return;
            PortAudio.Initialize();
            _ready = true;
        }
    }

    /// <summary>
    /// PortAudio (CoreAudio under it on a Mac) is not made to open, start, stop or close streams from several threads at
    /// once: the live chat, /recorder, /audiosettings, the interviews and the voice open and close theirs from the thread
    /// pool and the main thread, and two of them crossing (a stream stopped while another opens) left CoreAudio locked
    /// for good, the chat frozen with it. Every stream goes through these, one at a time for the whole application. The
    /// callbacks never take this lock: a stop waiting for its callback cannot wait for it.
    /// </summary>
    private static readonly object StreamLock = new();

    /// <summary>Opens a stream (and starts it, unless <paramref name="start"/> is false); closed again when it does not start.</summary>
    public static PortAudioSharp.Stream OpenStream(StreamParameters? input, StreamParameters? output, double rate,
        PortAudioSharp.Stream.Callback callback, bool start = true)
    {
        EnsureInitialized();
        lock (StreamLock)
        {
            var stream = new PortAudioSharp.Stream(input, output, rate, 0, StreamFlags.ClipOff, callback, IntPtr.Zero);
            if (!start)
                return stream;
            try
            {
                stream.Start();
            }
            catch
            {
                stream.Dispose();
                throw;
            }
            return stream;
        }
    }

    /// <summary>Starts a stream opened with <see cref="OpenStream"/>.</summary>
    public static void StartStream(PortAudioSharp.Stream stream)
    {
        lock (StreamLock)
            stream.Start();
    }

    /// <summary>Stops a stream (it can start again).</summary>
    public static void StopStream(PortAudioSharp.Stream stream)
    {
        lock (StreamLock)
            stream.Stop();
    }

    /// <summary>Stops (unless <paramref name="stop"/> is false) and closes a stream; nothing for null.</summary>
    public static void CloseStream(PortAudioSharp.Stream? stream, bool stop = true)
    {
        if (stream is null)
            return;
        lock (StreamLock)
        {
            try
            {
                if (stop)
                    stream.Stop();
            }
            catch (PortAudioException)
            {
                // Already stopped.
            }
            finally
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>The names of the microphones.</summary>
    public static IReadOnlyList<string> Inputs() => List(input: true);

    /// <summary>The names of the outputs (loudspeakers, headphones…).</summary>
    public static IReadOnlyList<string> Outputs() => List(input: false);

    /// <summary>The microphone chosen, or the default one when it is not there any more (<see cref="PortAudio.NoDevice"/>: none).</summary>
    public static int InputDevice() => Find(Input, input: true);

    /// <summary>The microphone named <paramref name="name"/> (null or empty: the default one of the system), or the default one when it is not there.</summary>
    public static int InputDevice(string? name) => Find(name, input: true);

    /// <summary>The output chosen, or the default one when it is not there any more (<see cref="PortAudio.NoDevice"/>: none).</summary>
    public static int OutputDevice() => Find(Output, input: false);

    private static IReadOnlyList<string> List(bool input)
    {
        EnsureInitialized();
        var names = new List<string>();
        var fallback = input ? PortAudio.DefaultInputDevice : PortAudio.DefaultOutputDevice;
        if (fallback == PortAudio.NoDevice)
            return names;
        var hostApi = PortAudio.GetDeviceInfo(fallback).hostApi;
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.hostApi == hostApi && (input ? info.maxInputChannels : info.maxOutputChannels) > 0 && !names.Contains(info.name))
                names.Add(info.name);
        }
        return names;
    }

    private static int Find(string? name, bool input)
    {
        EnsureInitialized();
        var fallback = input ? PortAudio.DefaultInputDevice : PortAudio.DefaultOutputDevice;
        if (string.IsNullOrEmpty(name) || fallback == PortAudio.NoDevice)
            return fallback;
        var hostApi = PortAudio.GetDeviceInfo(fallback).hostApi;
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.hostApi == hostApi && info.name == name && (input ? info.maxInputChannels : info.maxOutputChannels) > 0)
                return i;
        }
        ActivityLog.Info("Audio", $"{(input ? "Microphone" : "Output")} \"{name}\" not found: the default one is used");
        return fallback;
    }

    /// <summary>
    /// Plays a 16-bit PCM WAV file on <see cref="Output"/> and waits for its end (stopped on cancellation). The sound is
    /// resampled to the rate of the device, so any device takes it.
    /// </summary>
    public static async Task PlayAsync(string file, CancellationToken cancellationToken)
    {
        var (samples, fileRate) = ReadMono(file);
        var device = OutputDevice();
        if (device == PortAudio.NoDevice)
            throw new InvalidOperationException("No audio output found");
        var info = PortAudio.GetDeviceInfo(device);
        var rate = (int)Math.Round(info.defaultSampleRate);
        var sound = Resample(samples, fileRate, rate);
        var channels = Math.Min(2, info.maxOutputChannels);
        var parameters = new StreamParameters
        {
            device = device,
            channelCount = channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowOutputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero
        };

        var position = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // One buffer for the whole sound (grown when the callback asks more frames): nothing is allocated on the audio thread.
        var buffer = Array.Empty<float>();
        PortAudioSharp.Stream.Callback callback = (IntPtr _, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo _, StreamCallbackFlags _, IntPtr _) =>
        {
            var length = (int)frameCount * channels;
            if (buffer.Length < length)
                buffer = new float[length];
            var frames = (int)Math.Min(frameCount, sound.Length - position);
            for (var i = 0; i < frames; i++)
                for (var c = 0; c < channels; c++)
                    buffer[i * channels + c] = sound[position + i];
            // The end of the sound: silence after its last frame.
            if (frames * channels < length)
                Array.Clear(buffer, frames * channels, length - frames * channels);
            position += frames;
            Marshal.Copy(buffer, 0, output, length);
            if (position < sound.Length)
                return StreamCallbackResult.Continue;
            done.TrySetResult();
            return StreamCallbackResult.Complete;
        };

        var stream = OpenStream(null, parameters, rate, callback);
        try
        {
            using (cancellationToken.Register(() => done.TrySetCanceled(cancellationToken)))
                await done.Task;
            // The last buffer is still in the device.
            await Task.Delay(TimeSpan.FromSeconds(info.defaultLowOutputLatency + 0.05), cancellationToken);
        }
        finally
        {
            CloseStream(stream);
            GC.KeepAlive(callback);
        }
    }

    /// <summary>The samples of a 16-bit PCM WAV file, mixed to mono (full scale 1), and its rate.</summary>
    private static (float[] Samples, int Rate) ReadMono(string file)
    {
        using var reader = new BinaryReader(File.OpenRead(file));
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Not a WAV file: " + file);
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Not a WAV file: " + file);
        int channels = 0, rate = 0, bits = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                if (format != 1 || bits != 16)
                    throw new InvalidDataException("Only 16-bit PCM WAV files are played: " + file);
                reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
            }
            else if (id == "data" && channels > 0)
            {
                var length = (int)Math.Min(size, reader.BaseStream.Length - reader.BaseStream.Position);
                var frames = length / 2 / channels;
                var samples = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    var sum = 0f;
                    for (var c = 0; c < channels; c++)
                        sum += reader.ReadInt16() / 32768f;
                    samples[i] = sum / channels;
                }
                return (samples, rate);
            }
            else
            {
                reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        throw new InvalidDataException("No sound in the WAV file: " + file);
    }

    /// <summary>Linear resampling from <paramref name="from"/> Hz to <paramref name="to"/> Hz.</summary>
    private static float[] Resample(float[] samples, int from, int to)
    {
        if (from == to || samples.Length == 0)
            return samples;
        var length = (int)((long)samples.Length * to / from);
        var result = new float[length];
        var step = (double)from / to;
        for (var i = 0; i < length; i++)
        {
            var x = i * step;
            var j = (int)x;
            var next = Math.Min(j + 1, samples.Length - 1);
            result[i] = (float)(samples[j] + (samples[next] - samples[j]) * (x - j));
        }
        return result;
    }
}
