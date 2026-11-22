using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Tools.Video;

/// <summary>
/// Plays a video in the chat: kaonomi-media decodes it with AVFoundation (pictures in JPEG, sound in Float32 stereo),
/// this node shows the pictures and plays the sound. The clock is the sound actually played (the pictures follow it);
/// without sound, the time of the chat. Seeking starts the stream again at that time; paused, the stream waits (its
/// pipe full), nothing is decoded for nothing.
/// </summary>
public sealed partial class VideoEngine : Node
{
    private const int Rate = 44100;
    private const int MaxFrames = 36;

    private readonly ConcurrentQueue<(double Pts, Image Image)> _frames = new();
    private readonly ConcurrentQueue<Vector2[]> _sound = new();
    private AudioStreamPlayer _player = null!;
    private AudioStreamGeneratorPlayback? _playback;
    private ImageTexture? _texture;
    private Process? _process;
    private CancellationTokenSource? _reading;
    private int _generation;
    private long _soundQueued;
    private long _pushed;
    private int _capacity;
    private double _start;
    private double _silentClock;
    private bool _firstShown;
    private volatile bool _endOfStream;
    private volatile bool _hasSound;
    private Vector2[]? _pending;

    /// <summary>The video played.</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>The width of the pictures asked to the decoder.</summary>
    public int Width { get; set; } = 1280;

    /// <summary>The picture shown now (null before the first one).</summary>
    public Texture2D? Texture => _texture;

    /// <summary>The time shown, in seconds.</summary>
    public double Position { get; private set; }

    public bool Playing { get; private set; }

    /// <summary>True until the first picture after an opening or a seek is shown.</summary>
    public bool Loading => !_firstShown;

    /// <summary>A new picture is shown.</summary>
    public event Action? FrameShown;

    /// <summary>The end of the video was reached.</summary>
    public event Action? Ended;

    /// <summary>The stream could not start (its message).</summary>
    public event Action<string>? Failed;

    public float Volume
    {
        get => Mathf.DbToLinear(_player.VolumeDb);
        set => _player.VolumeDb = value <= 0.001f ? -80 : Mathf.LinearToDb(value);
    }

    public override void _Ready()
    {
        _player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.35f } };
        AddChild(_player);
    }

    /// <summary>Opens a video at <paramref name="at"/> seconds, playing or paused (the picture of that time shown).</summary>
    public void Open(string path, double at, bool play)
    {
        Path = path;
        Restart(at, play);
    }

    /// <summary>Goes to <paramref name="at"/> seconds, playing on when it played.</summary>
    public void Seek(double at) => Restart(Math.Max(0, at), Playing);

    public void Play()
    {
        if (Path.Length == 0)
            return;
        if (_endOfStream && _frames.IsEmpty && _sound.IsEmpty)
        {
            Restart(Position, true);
            return;
        }
        Playing = true;
        _player.StreamPaused = false;
    }

    public void Pause()
    {
        Playing = false;
        _player.StreamPaused = true;
    }

    public void Toggle()
    {
        if (Playing)
            Pause();
        else
            Play();
    }

    private void Restart(double at, bool play)
    {
        StopStream();
        while (_frames.TryDequeue(out _)) { }
        while (_sound.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _soundQueued, 0);
        _pending = null;
        _pushed = 0;
        _start = at;
        _silentClock = 0;
        Position = at;
        _firstShown = false;
        _endOfStream = false;
        _hasSound = false;
        _player.Stop();
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _capacity = _playback?.GetFramesAvailable() ?? 0;
        Playing = play;
        _player.StreamPaused = !play;
        var generation = ++_generation;
        _ = StartAsync(at, generation);
    }

    private async Task StartAsync(double at, int generation)
    {
        try
        {
            var start = await VideoMedia.PlayStartAsync(Path, at, Width);
            if (generation != _generation)
                return;
            var process = Process.Start(start) ?? throw new InvalidOperationException("kaonomi-media did not start");
            var reading = new CancellationTokenSource();
            _process = process;
            _reading = reading;
            _ = Task.Run(() => Read(process, reading.Token));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    /// <summary>Reads the stream of kaonomi-media (on a thread of its own): the pictures decoded, the sound in samples.</summary>
    private void Read(Process process, CancellationToken token)
    {
        var stream = process.StandardOutput.BaseStream;
        var header = new byte[13];
        try
        {
            while (!token.IsCancellationRequested)
            {
                stream.ReadExactly(header);
                var type = (char)header[0];
                var pts = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(1, 8)));
                var data = new byte[BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9, 4))];
                if (data.Length > 0)
                    stream.ReadExactly(data);
                switch (type)
                {
                    case 'I':
                        using (var info = JsonDocument.Parse(data))
                            _hasSound = info.RootElement.TryGetProperty("hasAudio", out var sound) && sound.GetBoolean();
                        break;
                    case 'V':
                        var image = new Image();
                        if (image.LoadJpgFromBuffer(data) != Error.Ok)
                            break;
                        // Ahead enough: wait for the clock (paused, it waits as long as the pause lasts).
                        while (_frames.Count >= MaxFrames && !token.IsCancellationRequested)
                            Thread.Sleep(4);
                        _frames.Enqueue((pts, image));
                        break;
                    case 'A':
                        var samples = new Vector2[data.Length / 8];
                        for (var i = 0; i < samples.Length; i++)
                            samples[i] = new Vector2(BitConverter.ToSingle(data, i * 8), BitConverter.ToSingle(data, i * 8 + 4));
                        while (Interlocked.Read(ref _soundQueued) > Rate * 2 && !token.IsCancellationRequested)
                            Thread.Sleep(4);
                        _sound.Enqueue(samples);
                        Interlocked.Add(ref _soundQueued, samples.Length);
                        break;
                    case 'E':
                        _endOfStream = true;
                        return;
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or InvalidOperationException or JsonException)
        {
            // The stream stopped: the window closed, or it seeks elsewhere.
            _endOfStream = !token.IsCancellationRequested;
        }
    }

    public override void _Process(double delta)
    {
        if (Path.Length == 0)
            return;
        // The sound: as much as the player takes.
        if (Playing && _hasSound && _playback is not null)
        {
            while (true)
            {
                if (_pending is null && !_sound.TryDequeue(out _pending))
                    break;
                if (!_playback.CanPushBuffer(_pending.Length))
                    break;
                _playback.PushBuffer(_pending);
                _pushed += _pending.Length;
                Interlocked.Add(ref _soundQueued, -_pending.Length);
                _pending = null;
            }
        }

        // The clock: the sound played (what was given to the player, minus what it still holds and its latency).
        double clock;
        if (_hasSound && _playback is not null)
        {
            var held = Math.Max(0, _capacity - _playback.GetFramesAvailable());
            var played = Math.Max(0, _pushed - held);
            clock = _start + played / (double)Rate - (played > 0 ? AudioServer.GetOutputLatency() : 0);
        }
        else
        {
            if (Playing && _firstShown)
                _silentClock += delta;
            clock = _start + _silentClock;
        }

        // The pictures due by now (the first one at once, even paused).
        Image? shown = null;
        while (_frames.TryPeek(out var frame) && (!_firstShown || frame.Pts <= clock + 0.012))
        {
            _frames.TryDequeue(out frame);
            shown = frame.Image;
            if (!_firstShown)
            {
                _firstShown = true;
                break;
            }
        }
        if (shown is not null)
        {
            if (_texture is null || _texture.GetWidth() != shown.GetWidth() || _texture.GetHeight() != shown.GetHeight())
                _texture = ImageTexture.CreateFromImage(shown);
            else
                _texture.Update(shown);
            FrameShown?.Invoke();
        }
        if (Playing && _firstShown)
            Position = Math.Max(_start, clock);

        if (Playing && _endOfStream && _frames.IsEmpty && _sound.IsEmpty && _pending is null
            && (!_hasSound || _playback is null || _playback.GetFramesAvailable() >= _capacity - 64))
        {
            Pause();
            Ended?.Invoke();
        }
    }

    private void StopStream()
    {
        _reading?.Cancel();
        _reading = null;
        if (_process is { } process)
        {
            _process = null;
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            process.Dispose();
        }
    }

    public override void _ExitTree() => StopStream();
}
