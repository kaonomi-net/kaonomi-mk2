using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PortAudioSharp;

namespace KAONOMI.Chat.Services;

/// <summary>What a recording gave: its file, its rate, its length, and what went wrong while writing it (null: nothing).</summary>
public sealed record AudioRecording(string Path, int Rate, double Seconds, string? Error);

/// <summary>
/// The microphone listened to and, on demand, recorded (PortAudio; the microphone chosen with <c>/audiosettings</c>).
/// <see cref="Listen"/> opens it: from then on <see cref="Analyzer"/> receives what it hears (for the meters), whether
/// it is recorded or not. <see cref="StartRecording"/> also writes it into a WAV file (mono, 16 bits, at the rate of
/// the microphone), as it comes: a long recording is never held in memory, and the header of the file is brought up to
/// date every few seconds, so that a recording cut short is still read.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private readonly object _lock = new();
    private PortAudioSharp.Stream? _stream;
    private PortAudioSharp.Stream.Callback? _callback;
    private float[] _buffer = [];
    private BlockingCollection<float[]>? _queue;
    private Thread? _writer;
    private string? _path;
    private long _captured;
    private long _written;
    private volatile string? _error;

    /// <summary>Samples heard, as they come (on the thread of the microphone: copy them, do not keep the span).</summary>
    public delegate void SamplesHandler(ReadOnlySpan<float> samples);

    /// <summary>Each block of samples heard, as it comes (the live chat cuts them at the silences).</summary>
    public event SamplesHandler? SamplesHeard;

    /// <summary>What the microphone hears, analysed for the meters.</summary>
    public AudioAnalyzer Analyzer { get; } = new();

    /// <summary>The rate of the microphone, in Hz (0 before <see cref="Listen"/>).</summary>
    public int Rate { get; private set; }

    /// <summary>The name of the microphone listened to.</summary>
    public string SourceName { get; private set; } = string.Empty;

    public bool IsListening
    {
        get
        {
            lock (_lock)
                return _stream is not null || _test is not null;
        }
    }

    /// <summary>
    /// A WAV file listened to instead of the microphone, again and again at its own pace (the option --audio-source
    /// of the chat): a test without the microphone.
    /// </summary>
    public static string? TestSource { get; set; }

    private Thread? _test;
    private volatile bool _testStop;

    /// <summary>Plays <see cref="TestSource"/> into the analysis as the microphone would, in a loop.</summary>
    private void ListenToTestFile(string path)
    {
        var samples = WavFile.Read(path).Left;
        if (samples.Length == 0)
            throw new InvalidOperationException($"The test sound {path} is empty");
        Rate = WavFile.Rate;
        SourceName = "test: " + System.IO.Path.GetFileName(path);
        Analyzer.Rate = Rate;
        Analyzer.Clear();
        lock (_heardLock)
        {
            _heard = new float[Rate * HeardSeconds];
            _heardHead = 0;
            _heardTotal = 0;
        }
        _testStop = false;
        _test = new Thread(() =>
        {
            const int chunk = 1024;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long fed = 0;
            var position = 0;
            var buffer = new float[chunk];
            while (!_testStop)
            {
                // At the pace of the sound: no further than the time gone by.
                if (fed > clock.Elapsed.TotalSeconds * Rate)
                {
                    Thread.Sleep(5);
                    continue;
                }
                for (var i = 0; i < chunk; i++)
                {
                    buffer[i] = samples[position];
                    position = (position + 1) % samples.Length;
                }
                Feed(buffer.AsSpan());
                fed += chunk;
            }
        }) { IsBackground = true, Name = "Test sound" };
        _test.Start();
        ActivityLog.Info("Recorder", $"Listening to the test sound {path} instead of the microphone");
    }

    public bool IsRecording
    {
        get
        {
            lock (_lock)
                return _queue is not null;
        }
    }

    /// <summary>The last seconds heard are kept, recorded or not: who speaks is found in them.</summary>
    private const int HeardSeconds = 12;

    private readonly object _heardLock = new();
    private float[] _heard = new float[1];
    private int _heardHead;
    private long _heardTotal;

    /// <summary>
    /// The last <paramref name="seconds"/> heard by the microphone (at most <see cref="HeardSeconds"/>), the oldest
    /// sample first; fewer when it has not listened that long, none when it does not listen.
    /// </summary>
    public float[] Heard(double seconds)
    {
        if (!IsListening)
            return [];
        lock (_heardLock)
        {
            var count = (int)Math.Min(Math.Min((long)(seconds * Rate), _heardTotal), _heard.Length);
            if (count <= 0)
                return [];
            var samples = new float[count];
            var start = ((_heardHead - count) % _heard.Length + _heard.Length) % _heard.Length;
            var first = Math.Min(count, _heard.Length - start);
            Array.Copy(_heard, start, samples, 0, first);
            Array.Copy(_heard, 0, samples, first, count - first);
            return samples;
        }
    }

    /// <summary>
    /// The level of the last <paramref name="seconds"/> heard (the root mean square of the samples, 0 to 1) and how
    /// long of them was heard (<paramref name="heardSeconds"/>: fewer when it has not listened that long, 0 when it
    /// does not listen). Read in place: nothing is copied nor allocated (asked at each frame of the camera).
    /// </summary>
    public double Level(double seconds, out double heardSeconds)
    {
        heardSeconds = 0;
        if (!IsListening || Rate <= 0)
            return 0;
        lock (_heardLock)
        {
            var count = (int)Math.Min(Math.Min((long)(seconds * Rate), _heardTotal), _heard.Length);
            if (count <= 0)
                return 0;
            heardSeconds = count / (double)Rate;
            var start = ((_heardHead - count) % _heard.Length + _heard.Length) % _heard.Length;
            var first = Math.Min(count, _heard.Length - start);
            double sum = 0;
            foreach (var sample in _heard.AsSpan(start, first))
                sum += (double)sample * sample;
            foreach (var sample in _heard.AsSpan(0, count - first))
                sum += (double)sample * sample;
            return Math.Sqrt(sum / count);
        }
    }

    /// <summary>How long was recorded since <see cref="StartRecording"/>.</summary>
    public TimeSpan Elapsed => Rate > 0 ? TimeSpan.FromSeconds(Interlocked.Read(ref _captured) / (double)Rate) : TimeSpan.Zero;

    /// <summary>Opens the microphone (nothing when it already is); throws when there is none or it cannot be opened.</summary>
    public void Listen()
    {
        lock (_lock)
        {
            if (_stream is not null || _test is not null)
                return;
            if (TestSource is { } test)
            {
                ListenToTestFile(test);
                return;
            }
            AudioDevices.EnsureInitialized();
            var device = AudioDevices.InputDevice();
            if (device == PortAudio.NoDevice)
                throw new InvalidOperationException("No microphone found");
            var info = PortAudio.GetDeviceInfo(device);
            Rate = (int)Math.Round(info.defaultSampleRate);
            SourceName = info.name;
            Analyzer.Rate = Rate;
            Analyzer.Clear();
            lock (_heardLock)
            {
                _heard = new float[Rate * HeardSeconds];
                _heardHead = 0;
                _heardTotal = 0;
            }
            var parameters = new StreamParameters
            {
                device = device,
                channelCount = 1,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = info.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };
            // Kept in a field: the native side calls it as long as the stream runs.
            _callback = OnAudio;
            _stream = AudioDevices.OpenStream(parameters, null, Rate, _callback);
            ActivityLog.Info("Recorder", $"Listening to {info.name} at {Rate} Hz");
        }
    }

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero || frameCount == 0)
            return StreamCallbackResult.Continue;
        var count = (int)frameCount;
        if (_buffer.Length < count)
            _buffer = new float[count];
        Marshal.Copy(input, _buffer, 0, count);
        Feed(_buffer.AsSpan(0, count));
        return StreamCallbackResult.Continue;
    }

    /// <summary>What is heard: analysed, kept for who speaks, and recorded while a recording runs.</summary>
    private void Feed(Span<float> heard)
    {
        var count = heard.Length;
        // The gain of the microphone (AudioDevices.InputGainDb), on what everything hears and records.
        var gain = AudioDevices.InputGain;
        if (gain != 1f)
            for (var i = 0; i < heard.Length; i++)
                heard[i] = Math.Clamp(heard[i] * gain, -1f, 1f);
        Analyzer.Push(heard);
        SamplesHeard?.Invoke(heard);
        lock (_heardLock)
        {
            // Copied by blocks (this runs in the callback of the microphone): the end of the ring, then its start.
            var ring = _heard;
            var source = heard.Length > ring.Length ? heard[^ring.Length..] : heard;
            var first = Math.Min(source.Length, ring.Length - _heardHead);
            source[..first].CopyTo(ring.AsSpan(_heardHead));
            source[first..].CopyTo(ring);
            _heardHead = (_heardHead + source.Length) % ring.Length;
            _heardTotal += count;
        }
        // Recorded: a copy goes to the thread of the file (the buffer is reused by the next callback).
        if (_queue is { } queue)
        {
            Interlocked.Add(ref _captured, count);
            try
            {
                queue.Add(heard.ToArray());
            }
            catch (InvalidOperationException)
            {
                // Stopped meanwhile.
            }
        }
    }

    /// <summary>
    /// Listens to the microphone chosen now (<see cref="AudioDevices.Input"/>): the one listened to is closed and the
    /// new one opened. Nothing while a recording runs (it would be cut). False when it could not be opened.
    /// </summary>
    public bool Restart()
    {
        if (IsRecording || TestSource is not null)
            return IsListening;
        PortAudioSharp.Stream? stream;
        lock (_lock)
        {
            stream = _stream;
            _stream = null;
        }
        AudioDevices.CloseStream(stream);
        Listen();
        return IsListening;
    }

    /// <summary>Starts recording what is heard into the new file <paramref name="path"/> (the microphone is opened when it is not).</summary>
    public void StartRecording(string path)
    {
        Listen();
        lock (_lock)
        {
            if (_queue is not null)
                throw new InvalidOperationException("Already recording");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            try
            {
                Wav.WriteHeader(file, Rate, 0);
            }
            catch
            {
                file.Dispose();
                throw;
            }
            _path = path;
            _error = null;
            Interlocked.Exchange(ref _captured, 0);
            Interlocked.Exchange(ref _written, 0);
            var queue = new BlockingCollection<float[]>();
            _writer = new Thread(() => WriteLoop(queue, file)) { IsBackground = true, Name = "Audio recorder" };
            _writer.Start();
            _queue = queue;
            ActivityLog.Info("Recorder", $"Recording {SourceName} into {path}");
        }
    }

    /// <summary>The thread of the file: each buffer written as 16-bit samples.</summary>
    private void WriteLoop(BlockingCollection<float[]> queue, FileStream file)
    {
        var bytes = Array.Empty<byte>();
        long sinceHeader = 0;
        using (file)
        {
            foreach (var chunk in queue.GetConsumingEnumerable())
            {
                if (_error is not null)
                    continue;
                try
                {
                    if (bytes.Length < chunk.Length * 2)
                        bytes = new byte[chunk.Length * 2];
                    for (var i = 0; i < chunk.Length; i++)
                    {
                        var value = (short)Math.Round(Math.Clamp(chunk[i], -1f, 1f) * 32767f);
                        bytes[2 * i] = (byte)value;
                        bytes[2 * i + 1] = (byte)(value >> 8);
                    }
                    file.Write(bytes, 0, chunk.Length * 2);
                    Interlocked.Add(ref _written, chunk.Length);
                    sinceHeader += chunk.Length;
                    if (sinceHeader >= Rate * 5L)
                    {
                        Wav.WriteHeader(file, Rate, Interlocked.Read(ref _written));
                        file.Flush();
                        sinceHeader = 0;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The disk is full or gone: what is written stays, the rest is lost.
                    _error = ex.Message;
                    ActivityLog.Error("Recorder", "Recording: " + ex.Message);
                }
            }
            try
            {
                Wav.WriteHeader(file, Rate, Interlocked.Read(ref _written));
                file.Flush(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _error ??= ex.Message;
            }
        }
    }

    /// <summary>Stops recording and closes the file (the microphone stays listened to); null when nothing was recorded.</summary>
    public AudioRecording? StopRecording()
    {
        BlockingCollection<float[]>? queue;
        Thread? writer;
        lock (_lock)
        {
            queue = _queue;
            writer = _writer;
            _queue = null;
            _writer = null;
        }
        if (queue is null)
            return null;
        queue.CompleteAdding();
        writer?.Join();
        queue.Dispose();
        var written = Interlocked.Read(ref _written);
        ActivityLog.Info("Recorder", $"Recorded {written / (double)Rate:0.0} s into {_path}");
        return new AudioRecording(_path!, Rate, written / (double)Rate, _error);
    }

    /// <summary>Stops the recording running (its file is closed and kept) and closes the microphone.</summary>
    public void Dispose()
    {
        StopRecording();
        PortAudioSharp.Stream? stream;
        Thread? test;
        lock (_lock)
        {
            stream = _stream;
            _stream = null;
            test = _test;
            _test = null;
        }
        if (test is not null)
        {
            _testStop = true;
            test.Join();
        }
        AudioDevices.CloseStream(stream);
    }
}
