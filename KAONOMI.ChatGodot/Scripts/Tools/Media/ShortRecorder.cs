using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Media;

/// <summary>
/// A Short of <c>/remote</c> (SHORT checked): the 3D animation of the chat filmed in 9:16, 1080 × 1920 at 30 frames a
/// second, with its voice, written as an MP4 (H.264, AAC) in <c>~/kaonomi/video/shorts</c>. The animation is drawn
/// again for it in a viewport of its own, on a dark background: the animation of the preset in use (a <c>CHANGE:</c>
/// of a script changes it there too), its mouth following the same voice, a picture of <c>IMAGE:</c> shown over it as
/// in the chat. Its frames go to ffmpeg as they are drawn, one repeated when the chat draws fewer than 30 a second, so
/// that the picture keeps the time of the voice. The voice: each sentence heard (its effects applied) is copied when it
/// starts, and the sentences are laid at their moments on one track at the end, joined to the picture. Only what is
/// said is kept (and a picture of IMAGE: while it shows): the silences before the first sentence, after the last one and
/// between two sequences (the wait between two paragraphs, a change of preset, a voice loading) are cut, at the same
/// frames in the picture and in the voice.
/// </summary>
public sealed partial class ShortRecorder : Node, IShortRecording
{
    public const int Width = 1080, Height = 1920, Fps = 30;

    /// <summary>A face is seen from a little further than in the chat (2.55), so that it fits the narrow frame.</summary>
    private const float FaceDistance = 3.0f;

    /// <summary>The rate of the sound track of the Short: 1600 samples a frame.</summary>
    private const int VoiceRate = 48000, SamplesPerFrame = VoiceRate / Fps;

    /// <summary>The silence kept before a sequence and after it, in seconds, so that a cut never takes a sound.</summary>
    private const double LeadIn = 0.12, LeadOut = 0.25;

    /// <summary>
    /// A silence longer than this between two sentences, in seconds, is cut (a change of preset, a voice loading, the
    /// wait between two paragraphs); the pause between two sentences of a paragraph is shorter and stays.
    /// </summary>
    private const double LongestPause = 0.6;

    /// <summary>Where the Shorts are written: <c>~/kaonomi/video/shorts</c>.</summary>
    public static string Folder => Path.Combine(VideoMedia.Home, "shorts");

    private static string Ffmpeg => Path.Combine(VideoMedia.Home, "bin", "ffmpeg");

    private readonly MainViewModel _chat;
    private readonly string _work = Path.Combine(Path.GetTempPath(), "kaonomi-short", Guid.NewGuid().ToString("N"));
    private readonly List<(string File, long At)> _sentences = [];
    private readonly List<double> _images = [];
    private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private SubViewport _viewport = null!;
    private Control _stage = null!;
    private Control? _avatarView;
    private IAvatar? _avatar;
    private SceneAnimation? _animation;
    private Process? _ffmpeg;
    private Task<string>? _ffmpegErrors;
    private Task? _writer;
    private volatile bool _speaking;
    private volatile bool _stopped;
    private long _start;
    private long _written;
    private int _warmup = 3;
    private Task<string?>? _result;

    private ShortRecorder(MainViewModel chat) => _chat = chat;

    /// <summary>
    /// Starts a Short of the chat of <paramref name="shell"/>; null when it cannot, the reason given to
    /// <paramref name="error"/> (no ffmpeg in <c>~/kaonomi/video/bin</c>, a folder that cannot be written).
    /// </summary>
    public static ShortRecorder? Start(Shell shell, Action<string> error)
    {
        if (!File.Exists(Ffmpeg))
        {
            error($"ffmpeg is missing in {Path.GetDirectoryName(Ffmpeg)} (the video tools of the chat install it there)");
            return null;
        }
        var recorder = new ShortRecorder(shell.Chat) { Name = nameof(ShortRecorder) };
        try
        {
            Directory.CreateDirectory(recorder._work);
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error(ex.Message);
            recorder.Free();
            return null;
        }
        shell.Root.AddChild(recorder);
        ActivityLog.Info("Short", "Filming a Short: the 3D animation in 9:16 and the voice");
        return recorder;
    }

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Width, Height),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            GuiDisableInput = true,
            Msaa2D = Viewport.Msaa.Msaa4X
        };
        AddChild(_viewport);
        _stage = new Control { Size = new Vector2(Width, Height), MouseFilter = Control.MouseFilterEnum.Ignore };
        _viewport.AddChild(_stage);
        _stage.AddChild(Background());
        FollowAnimation();
        SpeechService.SentencePlaying += OnSentence;
        _chat.SpeakingChanged += OnSpeaking;
        _chat.ImageShowRequested += OnImage;
        StartEncoder();
    }

    public override void _Process(double delta)
    {
        if (_stopped)
            return;
        FollowAnimation();
        if (_avatar is not null)
            _avatar.Speaking = _speaking;
        // The first frames: the viewport draws its first picture before the clock starts.
        if (_warmup > 0)
        {
            if (--_warmup == 0)
                _start = Stopwatch.GetTimestamp();
            return;
        }
        // The frames due by now, at 30 a second: none when the chat draws faster, the last one repeated when slower.
        var elapsed = (Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency;
        var due = (long)(elapsed * Fps) + 1;
        if (due <= _written)
            return;
        var image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty())
            return;
        if (image.GetFormat() != Image.Format.Rgb8)
            image.Convert(Image.Format.Rgb8);
        var data = image.GetData();
        while (_written < due && _frames.Writer.TryWrite(data))
            _written++;
    }

    public override void _ExitTree()
    {
        // Freed without being stopped (the chat closing): nothing is written.
        if (_stopped)
            return;
        Detach();
        try
        {
            if (_ffmpeg is { HasExited: false } ffmpeg)
                ffmpeg.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already ended.
        }
    }

    /// <summary>Ends the filming and writes the MP4 (from any thread); its path, or null (the log says why).</summary>
    public Task<string?> StopAsync()
    {
        if (_result is { } running)
            return running;
        Detach();
        Bind.OnMain(() =>
        {
            if (IsInstanceValid(this))
                QueueFree();
        });
        return _result = Task.Run(FinishAsync);
    }

    /// <summary>No more frames nor sentences: the events of the chat are left.</summary>
    private void Detach()
    {
        _stopped = true;
        SpeechService.SentencePlaying -= OnSentence;
        _chat.SpeakingChanged -= OnSpeaking;
        _chat.ImageShowRequested -= OnImage;
        _frames.Writer.TryComplete();
    }

    private static Control Background()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.06f, 0.08f, 0.14f));
        gradient.SetColor(1, new Color(0.0f, 0.0f, 0.02f));
        var back = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, Width = 4, Height = 256, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1) },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return back;
    }

    /// <summary>The animation of the preset in use, as the chat shows it (made again when a preset changes it).</summary>
    private void FollowAnimation()
    {
        var animation = _chat.SelectedPreset?.Animation ?? _chat.Animation;
        if (animation == SceneAnimation.None)
            animation = SceneAnimation.Kaonomi;
        if (animation == _animation)
            return;
        _animation = animation;
        _avatarView?.QueueFree();
        Control view;
        if (animation == SceneAnimation.Jarvis)
        {
            var jarvis = new JarvisAvatar();
            view = jarvis;
            _avatar = jarvis;
        }
        else
        {
            FaceAvatar face;
            try
            {
                face = new FaceAvatar(animation);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                face = new FaceAvatar(SceneAnimation.Kaonomi);
            }
            face.Distance = FaceDistance;
            view = face;
            _avatar = face;
        }
        view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _stage.AddChild(view);
        _avatarView = view;
        _avatar.VoiceLevel = () => _chat.SpeechLevel;
        _avatar.Speaking = _speaking;
    }

    private void OnSpeaking(bool speaking) => _speaking = speaking;

    /// <summary>
    /// A picture of IMAGE: shown over the animation of the chat: over the one of the Short too, and kept in it even
    /// while nothing is said (its moment noted).
    /// </summary>
    private void OnImage(string path) => Bind.OnMain(() =>
    {
        if (!_stopped && IsInstanceValid(this) && _avatarView is { } view && IsInstanceValid(view) && ImageShow.Show(view, path))
            lock (_images)
                _images.Add((Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency);
    });

    /// <summary>A sentence starts to be heard: copied at once (its file is deleted once the reading ends), with its moment.</summary>
    private void OnSentence(string file, long at)
    {
        if (_stopped)
            return;
        lock (_sentences)
        {
            var copy = Path.Combine(_work, $"voice-{_sentences.Count:0000}.wav");
            try
            {
                File.Copy(file, copy, overwrite: true);
                _sentences.Add((copy, at));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActivityLog.Error("Short", $"A sentence of the voice could not be kept: {ex.Message}");
            }
        }
    }

    /// <summary>ffmpeg reads the frames on its input and writes the picture alone (H.264); a task feeds it.</summary>
    private void StartEncoder()
    {
        var start = new ProcessStartInfo(Ffmpeg) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[]
                 {
                     "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", $"{Width}x{Height}", "-r", $"{Fps}", "-i", "-",
                     "-an", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", Path.Combine(_work, "picture.mp4")
                 })
            start.ArgumentList.Add(argument);
        try
        {
            _ffmpeg = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ActivityLog.Error("Short", "ffmpeg could not start", ex);
        }
        if (_ffmpeg is not { } ffmpeg)
        {
            Detach();
            return;
        }
        _ffmpegErrors = ffmpeg.StandardError.ReadToEndAsync();
        _writer = Task.Run(async () =>
        {
            var input = ffmpeg.StandardInput.BaseStream;
            try
            {
                await foreach (var frame in _frames.Reader.ReadAllAsync())
                    await input.WriteAsync(frame);
                await input.FlushAsync();
            }
            catch (IOException ex)
            {
                // ffmpeg ended before its input (its error says why): the frames still coming are let go.
                ActivityLog.Error("Short", "ffmpeg stopped reading the frames", ex);
                await foreach (var _ in _frames.Reader.ReadAllAsync())
                {
                }
            }
            finally
            {
                try
                {
                    input.Close();
                }
                catch (IOException)
                {
                    // Already closed by ffmpeg.
                }
            }
        });
    }

    /// <summary>The picture finished, the voice laid on its track, both joined into the MP4 of the folder of the Shorts.</summary>
    private async Task<string?> FinishAsync()
    {
        try
        {
            if (_writer is not null)
                await _writer;
            if (_ffmpeg is not { } ffmpeg)
                return null;
            await ffmpeg.WaitForExitAsync();
            var errors = _ffmpegErrors is null ? string.Empty : (await _ffmpegErrors).Trim();
            var picture = Path.Combine(_work, "picture.mp4");
            var seconds = _written / (double)Fps;
            if (ffmpeg.ExitCode != 0 || !File.Exists(picture))
            {
                ActivityLog.Error("Short", $"ffmpeg could not write the picture (exit code {ffmpeg.ExitCode}): {errors}");
                return null;
            }
            if (seconds < 0.5)
            {
                ActivityLog.Error("Short", "Nothing was filmed: the Short ended at once");
                return null;
            }
            var output = Path.Combine(Folder, $"short-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
            List<(string File, long At)> sentences;
            lock (_sentences)
                sentences = [.. _sentences];
            if (sentences.Count == 0)
            {
                // No voice was heard: the picture alone.
                File.Move(picture, output, overwrite: true);
                ActivityLog.Info("Short", $"Short written without voice (none was heard): {output}, {seconds:0.0} s");
                return output;
            }
            var voices = ReadVoices(sentences);
            var track = Mix(voices, _written);
            // Only what is said (and the pictures of IMAGE: shown): the silences before, between and after the sequences
            // are cut, at the same frames in the picture and in the voice.
            List<(double Start, double End)> spans = [.. voices.Select(v => (v.At + v.From - LeadIn, v.At + v.To + LeadOut))];
            lock (_images)
                spans.AddRange(_images.Select(at => (at, at + ImageShow.Shown)));
            var kept = Kept(spans, _written);
            if (kept.Count == 0)
                kept.Add((0, _written - 1));
            var voice = Path.Combine(_work, "voice.wav");
            WriteWav(voice, Cut(track, kept), VoiceRate);
            var frames = kept.Sum(range => range.Last - range.First + 1);
            var select = $"select='{string.Join("+", kept.Select(range => $"between(n,{range.First},{range.Last})"))}',setpts=N/({Fps}*TB)";
            if (await RunFfmpegAsync("-y", "-loglevel", "error", "-i", picture, "-i", voice, "-map", "0:v", "-map", "1:a", "-vf", select,
                    "-r", $"{Fps}", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", output) is { } failure)
            {
                ActivityLog.Error("Short", $"ffmpeg could not join the voice to the picture: {failure}");
                return null;
            }
            ActivityLog.Info("Short", $"Short written: {output}, {frames / (double)Fps:0.0} s kept of {seconds:0.0} s filmed "
                                      + $"({kept.Count} sequence(s), the silences around them cut), {sentences.Count} sentence(s) of voice");
            return output;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ActivityLog.Error("Short", "The Short could not be written", ex);
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(_work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in the temp folder.
            }
        }
    }

    /// <summary>Under this level (-40 dB), a sample of a sentence is silence (Piper starts and ends each one with some).</summary>
    private const float Audible = 0.01f;

    /// <summary>
    /// The sentences read from their files: their samples, their rate, their moment after the first frame, and where they
    /// are heard in them (the silence Piper puts before and after the words left out), in seconds.
    /// </summary>
    private List<(float[] Samples, int Rate, double At, double From, double To)> ReadVoices(List<(string File, long At)> sentences)
    {
        var voices = new List<(float[] Samples, int Rate, double At, double From, double To)>();
        foreach (var (file, at) in sentences)
        {
            if (!TryReadWav(file, out var samples, out var rate) || samples.Length < 2)
            {
                ActivityLog.Error("Short", $"A sentence of the voice could not be read: {Path.GetFileName(file)}");
                continue;
            }
            var first = Array.FindIndex(samples, sample => Math.Abs(sample) > Audible);
            var last = Array.FindLastIndex(samples, sample => Math.Abs(sample) > Audible);
            // Nothing above the level (a sentence whispered by an effect): all of it counts.
            if (first < 0)
                (first, last) = (0, samples.Length - 1);
            voices.Add((samples, rate, (at - _start) / (double)Stopwatch.Frequency, first / (double)rate, (last + 1) / (double)rate));
        }
        return voices;
    }

    /// <summary>The sentences laid at their moments on one track as long as the frames filmed (48 kHz, mono).</summary>
    private static float[] Mix(List<(float[] Samples, int Rate, double At, double From, double To)> voices, long frames)
    {
        var track = new float[frames * SamplesPerFrame];
        foreach (var (samples, rate, offset, _, _) in voices)
        {
            var first = (long)Math.Round(Math.Max(0, offset) * VoiceRate);
            // A sentence begun before the first frame: its beginning is left out.
            var skip = offset < 0 ? -offset * rate : 0;
            var step = rate / (double)VoiceRate;
            for (long i = 0; first + i < track.Length; i++)
            {
                var position = skip + i * step;
                var index = (int)position;
                if (index + 1 >= samples.Length)
                    break;
                var fraction = (float)(position - index);
                track[first + i] += samples[index] + (samples[index + 1] - samples[index]) * fraction;
            }
        }
        return track;
    }

    /// <summary>
    /// The frames kept, as ranges (first, last): the moments of <paramref name="spans"/> (in seconds), those less than
    /// <see cref="LongestPause"/> apart taken as one sequence (the pause between two sentences of a paragraph stays);
    /// the longer silences between them, and before the first and after the last, are left out.
    /// </summary>
    private static List<(long First, long Last)> Kept(List<(double Start, double End)> spans, long frames)
    {
        var kept = new List<(long First, long Last)>();
        foreach (var (start, end) in spans.OrderBy(span => span.Start))
        {
            var first = Math.Max(0, (long)Math.Floor(start * Fps));
            var last = Math.Min(frames - 1, (long)Math.Ceiling(end * Fps) - 1);
            if (last < first)
                continue;
            if (kept.Count > 0 && first - kept[^1].Last - 1 <= LongestPause * Fps)
                kept[^1] = (kept[^1].First, Math.Max(kept[^1].Last, last));
            else
                kept.Add((first, last));
        }
        return kept;
    }

    /// <summary>The track of the frames kept only, end to end (the same cuts as the picture, to the sample).</summary>
    private static float[] Cut(float[] track, List<(long First, long Last)> kept)
    {
        var cut = new float[kept.Sum(range => (range.Last - range.First + 1) * SamplesPerFrame)];
        long at = 0;
        foreach (var (first, last) in kept)
        {
            var count = (last - first + 1) * SamplesPerFrame;
            var available = Math.Min(count, track.Length - first * SamplesPerFrame);
            if (available > 0)
                Array.Copy(track, first * SamplesPerFrame, cut, at, available);
            at += count;
        }
        return cut;
    }

    /// <summary>The samples of a 16-bit PCM WAV file (its channels averaged) and its rate; false when it is not one.</summary>
    private static bool TryReadWav(string path, out float[] samples, out int rate)
    {
        samples = [];
        rate = 0;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
            return false;
        int format = 0, channels = 0, bits = 0;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, position, 4);
            var size = BitConverter.ToInt32(bytes, position + 4);
            var body = position + 8;
            if (id == "fmt " && body + 16 <= bytes.Length)
            {
                format = BitConverter.ToUInt16(bytes, body);
                channels = BitConverter.ToInt16(bytes, body + 2);
                rate = BitConverter.ToInt32(bytes, body + 4);
                bits = BitConverter.ToInt16(bytes, body + 14);
            }
            else if (id == "data")
            {
                // PCM, or the extensible format holding PCM.
                if (format is not (1 or 0xFFFE) || bits != 16 || channels < 1 || rate <= 0)
                    return false;
                var length = (size < 0 || size > bytes.Length - body ? bytes.Length - body : size) / 2 / channels;
                samples = new float[length];
                for (var i = 0; i < length; i++)
                {
                    var sum = 0;
                    for (var channel = 0; channel < channels; channel++)
                        sum += BitConverter.ToInt16(bytes, body + (i * channels + channel) * 2);
                    samples[i] = sum / (32768f * channels);
                }
                return true;
            }
            if (size < 0)
                return false;
            position = body + size + (size & 1);
        }
        return false;
    }

    private static void WriteWav(string path, float[] track, int rate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var data = track.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + data);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data);
        foreach (var sample in track)
            writer.Write((short)Math.Clamp(sample * 32767f, -32768f, 32767f));
    }

    /// <summary>Runs ffmpeg with <paramref name="arguments"/>: null when it succeeded, else what it said.</summary>
    private static async Task<string?> RunFfmpegAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(Ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start");
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? null : errors.Trim().Length > 0 ? errors.Trim() : $"exit code {process.ExitCode}";
    }
}
