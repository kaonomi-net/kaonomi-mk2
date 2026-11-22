using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// LIVE CHAT: the voice input without the button. While it is on, the microphone is listened to and Whisper writes in
/// the message box what is said, as it is said (again every <see cref="LivePartialSeconds"/> seconds); after
/// <see cref="LivePause"/> of silence, the text is sent, as SEND would. The microphone is not heard while an answer is
/// read aloud (and while it is prepared, and while the answer comes): the voice of the assistant is never taken for
/// the user's, nor a prompt sent over an answer; it is heard again <see cref="LiveGraceSeconds"/> after.
/// <para>
/// A shout stops the voice: while an answer is read aloud the microphone is still listened to, for its level only
/// (<see cref="ShoutDetector"/>: a level that rises at once far over the usual one, the voice of the assistant in the
/// loudspeakers included, for a fifth of a second), and a shout does what Esc does (<see cref="Interrupt"/>). The
/// shout itself is not written nor sent: the microphone is heard again once it has ended.
/// </para>
/// <para>
/// A room that is never silent (a fan, music, a bus): the silence that ends a sentence may never come. So what is
/// written in the box is also sent once it has not changed for <see cref="LivePause"/> while the voice has stopped
/// (the last second is clearly quieter than the loudest one, <see cref="VoiceStopped"/>): the text ends the sentence
/// when the sound cannot.
/// </para>
/// <para>
/// Near or away: while /vision runs with a place chosen (SET LOCATION), what is said in front of the computer
/// (the picture framed in red at some moment of the sentence: the face and the voice reach the measure of the place)
/// is sent as it is; what is said away from it (a small face, or none, or a quiet voice) is sent only when it starts
/// with « dis &lt;name&gt; » (the name of the 3D animation or of the preset: « Dis Kaonomi, est-ce que tu sais qui est
/// Jérôme ? »), without these words. A silence may follow « dis &lt;name&gt; »: said alone, it waits
/// <see cref="WakeWaitSeconds"/> seconds for the question. Without /vision (or without a place), everything is sent.
/// </para>
/// </summary>
public partial class MainViewModel
{
    /// <summary>The silence after which what was said is sent.</summary>
    private static readonly TimeSpan LivePause = TimeSpan.FromSeconds(2);

    /// <summary>What is being said is written in the box again this often, in seconds.</summary>
    private const double LivePartialSeconds = 1.5;

    /// <summary>The most of what is being said that is written in the box meanwhile, in seconds (its end; the whole is recognised once it is said).</summary>
    private const int LivePartialMostSeconds = 28;

    /// <summary>The microphone is heard again this long after the voice of the assistant stops (its echo in the room).</summary>
    private const double LiveGraceSeconds = 0.6;

    /// <summary>After « dis &lt;name&gt; » said alone, the question is awaited this long, in seconds.</summary>
    private const double WakeWaitSeconds = 8;

    /// <summary>The words that call the assistant, by language: « dis Kaonomi », « hey Kaonomi »…</summary>
    private static readonly string[] WakeWords = ["dis", "dit", "di", "hey", "hé", "he", "say", "sag", "ok"];

    /// <summary>The description of a picture of a place (SET LOCATION) by the vision model, in the language of the chat.</summary>
    private async Task<(string Text, string Model)> DescribeLocationAsync(byte[] picture, CancellationToken token)
    {
        var model = string.IsNullOrWhiteSpace(_settings.VisionModel) ? ChatSettings.DefaultVisionModel : _settings.VisionModel;
        var french = string.Equals(_settings.Language, "FR", StringComparison.OrdinalIgnoreCase);
        var system = french
            ? "Tu décris des photos prises par la caméra d'un ordinateur, de façon précise et factuelle, en français."
            : "You describe pictures taken by the camera of a computer, precisely and factually, in English.";
        var prompt = french
            ? "Décris cette image : le lieu et la pièce, les objets et les meubles visibles, les écrans et ce qu'ils montrent, les textes lisibles, les marques, les personnes (sans les identifier), la lumière et l'ambiance. Nomme précisément ce que tu reconnais."
            : "Describe this picture: the place and the room, the objects and furniture seen, the screens and what they show, the texts that can be read, the brands, the people (without identifying them), the light and the mood. Name precisely what you recognise.";
        return (await _ollama.DescribeImageAsync(model, system, prompt, picture, token), model);
    }

    /// <summary>/vision, the last one opened: it says whether the user is in front of the computer.</summary>
    private VisionFaceWindowViewModel? _visionFace;

    /// <summary>/vision while it runs: where the user is (close enough or not, the face in the picture); null otherwise.</summary>
    public VisionFaceWindowViewModel? Presence => _visionFace is { IsRunning: true } vision ? vision : null;

    /// <summary>
    /// The live chat: listened to, written, sent after 2 seconds of silence. Off at the start of the Godot chat (its
    /// check box to tick; the option --live-chat puts it on from the start).
    /// </summary>
    [ObservableProperty]
    private bool _liveChat;

    private AudioRecorder? _liveMic;
    private CancellationTokenSource? _liveStop;

    private bool _liveFollowsInput;

    partial void OnLiveChatChanged(bool value)
    {
        // Another microphone chosen (/recorder, /audiosettings): the live chat listens to it.
        if (!_liveFollowsInput)
        {
            _liveFollowsInput = true;
            AudioDevices.InputChanged += () =>
            {
                if (LiveChat)
                    _ = RunLiveChatAsync();
            };
        }
        if (value)
            _ = RunLiveChatAsync();
        else
            StopLiveChat();
    }

    private void StopLiveChat()
    {
        _liveStop?.Cancel();
        _liveStop = null;
        var mic = _liveMic;
        _liveMic = null;
        if (mic is not null)
            _ = Task.Run(mic.Dispose);
    }

    /// <summary>The microphone is not heard: the assistant speaks, prepares what it says, or answers.</summary>
    private bool LiveMuted => IsSpeakingOrPreparing || SendCommand.IsRunning || IsListening || IsTranscribing;

    private async Task RunLiveChatAsync()
    {
        StopLiveChat();
        _voiceInput ??= new SpeechRecognitionService(_settings.SpeechModel);
        if (!_voiceInput.HasModel)
        {
            Status = $"Live chat: Whisper model not found ({_voiceInput.ModelPath}); set speech.model in config.yml";
            LiveChat = false;
            return;
        }
        var speech = _voiceInput;
        var stop = new CancellationTokenSource();
        _liveStop = stop;
        var mic = new AudioRecorder();
        _liveMic = mic;
        var heard = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions { SingleReader = true });
        mic.SamplesHeard += samples => heard.Writer.TryWrite(samples.ToArray());
        try
        {
            await Task.Run(mic.Listen).WaitAsync(TimeSpan.FromSeconds(6));
        }
        catch (Exception ex)
        {
            Status = ex is TimeoutException
                ? "Live chat: the microphone does not open; allow it for the application that launched the chat (System Settings › Privacy & Security › Microphone)"
                : "Live chat: the microphone could not be opened: " + ex.Message;
            ActivityLog.Error("Speech", "Live chat: " + Status);
            if (_liveStop == stop)
            {
                StopLiveChat();
                LiveChat = false;
            }
            return;
        }
        ActivityLog.Info("Speech", $"Live chat on: {mic.SourceName} ({mic.Rate} Hz), sent after {LivePause.TotalSeconds:0} s of silence");
        var listening = $"🎙 Live chat ({VoiceLanguage.ToUpperInvariant()}): speak, it is sent after {LivePause.TotalSeconds:0} s of silence";
        Status = listening;
        _ = speech.WarmUpAsync(VoiceLanguage);

        var segmenter = new SilenceSegmenter(mic.Rate, LivePause);
        var clock = Stopwatch.StartNew();
        var heardAgainAt = 0.0;
        var muted = false;
        var lastPartial = 0.0;
        var partialRunning = false;
        var said = 0;
        // What is being said as last written in the box, since when (the time its first transcription was asked), and
        // that text once it is to be sent: unchanged for LivePause, the voice stopped.
        var steady = string.Empty;
        var steadySince = 0.0;
        string? steadyDue = null;
        // Near: the picture was framed in red during what is being said. Woken: « dis <name> » was said alone, at that time.
        var near = false;
        var wokenAt = double.NegativeInfinity;
        // A shout over the voice of the assistant stops it. While the voice was read: the loudest the microphone heard
        // (told in the log when it listens again: what a shout would have had to reach).
        var shouts = new ShoutDetector(mic.Rate);
        var afterShout = false;
        var readAloud = false;
        var loudest = 0.0;
        static string Decibels(double level) => level <= 1e-6 ? "-120 dB" : $"{20 * Math.Log10(level):0} dB";

        // A sentence was said (its text, its length in seconds, away from the computer or not): sent, unless it was
        // said from afar without the name of the assistant.
        void Said(string text, double seconds, bool away)
        {
            if (text.Length == 0)
                return;
            var woken = clock.Elapsed.TotalSeconds - wokenAt <= WakeWaitSeconds;
            if (AfterWakeWords(text) is { } rest)
            {
                // « Dis Kaonomi » alone: the question comes after a silence.
                if (rest.Length == 0)
                {
                    wokenAt = clock.Elapsed.TotalSeconds;
                    Input = string.Empty;
                    Status = $"🎙 {text} — yes? (the question is awaited {WakeWaitSeconds:0} s)";
                    ActivityLog.Info("Speech", "Live chat: called by its name, the question is awaited");
                    return;
                }
                text = rest;
            }
            else if (away && !woken)
            {
                Input = string.Empty;
                Status = "🎙 Heard away from the computer: not sent (from afar, say « dis <name> … »)";
                ActivityLog.Info("Speech", $"Live chat: {seconds:0.0} s heard away from the computer, not for the assistant");
                return;
            }
            wokenAt = double.NegativeInfinity;
            Input = text;
            Status = "🎙 " + text;
            if (SendCommand.CanExecute(null))
                SendCommand.Execute(null);
            else
                Status = "🎙 Live chat: not sent (no model, or the server cannot be reached): " + text;
        }

        try
        {
            while (await heard.Reader.WaitToReadAsync(stop.Token))
            {
                while (heard.Reader.TryRead(out var block))
                {
                    // The level of every block is followed (what is usual in the room, the voice of the assistant as
                    // the microphone hears it); a shout while an answer is read aloud stops the voice, as Esc does.
                    var reading = _speech.IsActive;
                    var shout = shouts.Add(block, reading && _speech.Level > ShoutDetector.Audible);
                    if (reading)
                    {
                        readAloud = true;
                        loudest = Math.Max(loudest, shouts.Level);
                        if (shout is { } cry)
                        {
                            afterShout = true;
                            ActivityLog.Info("Speech", $"Live chat: a shout ({Decibels(cry.Level)} for {cry.Seconds:0.00} s, the usual {Decibels(cry.Usual)}) stops the voice");
                            Interrupt("a shout");
                            Status = "🎙 A shout: the voice stopped";
                        }
                    }

                    // The assistant speaks (or answers): nothing is heard, what was being said is dropped.
                    if (LiveMuted)
                    {
                        if (!muted)
                        {
                            muted = true;
                            segmenter = new SilenceSegmenter(mic.Rate, LivePause);
                            said++;
                            steady = string.Empty;
                            steadyDue = null;
                            Status = "🎙 Live chat: paused while the assistant answers and speaks";
                            ActivityLog.Info("Speech", $"Live chat paused ({(IsSpeakingOrPreparing ? "the assistant speaks" : SendCommand.IsRunning ? "the assistant answers" : "the 🎤 button")})");
                        }
                        heardAgainAt = clock.Elapsed.TotalSeconds + LiveGraceSeconds;
                        continue;
                    }
                    // The shout that stopped the voice is not a sentence: nothing is heard until it has ended.
                    if (afterShout && shouts.Loud)
                        heardAgainAt = clock.Elapsed.TotalSeconds + LiveGraceSeconds;
                    if (clock.Elapsed.TotalSeconds < heardAgainAt)
                        continue;
                    if (muted)
                    {
                        muted = false;
                        if (!afterShout)
                            Status = listening;
                        // What a shout had to reach, and what was heard: to know why the voice stopped, or did not.
                        ActivityLog.Info("Speech", "Live chat listens again" + (readAloud
                            ? $" (while the voice was read: {(shouts.Echo is { } echo ? "heard at " + Decibels(echo) : "not heard long enough to know its level")}, the loudest sound {Decibels(loudest)}, a shout from {Decibels(Math.Max(ShoutDetector.Floor, ShoutDetector.Ratio * (shouts.Echo ?? 0)))})"
                            : string.Empty));
                        readAloud = false;
                        loudest = 0;
                    }
                    afterShout = false;

                    if (_visionFace is { WatchesPlace: true, Talking: true })
                        near = true;
                    foreach (var segment in segmenter.Add(block))
                    {
                        said++;
                        steady = string.Empty;
                        steadyDue = null;
                        var away = _visionFace is { WatchesPlace: true } && !near;
                        near = false;
                        var text = (await speech.TranscribeAsync(Wav.ForWhisper(segment), VoiceLanguage, stop.Token, prompt: WakePrompt())).Trim();
                        Said(text, segment.Samples.Length / (double)segment.Rate, away);
                    }

                    // No silence came, but what is written in the box has not changed for LivePause and the voice
                    // has stopped: the sentence ends here, by its text (the room is not silent).
                    if (steadyDue is { } spoken)
                    {
                        steadyDue = null;
                        steady = string.Empty;
                        var seconds = segmenter.CurrentLength / (double)segmenter.Rate;
                        segmenter.Flush();
                        said++;
                        var away = _visionFace is { WatchesPlace: true } && !near;
                        near = false;
                        ActivityLog.Info("Speech", $"Live chat: {seconds:0.0} s ended by its text, unchanged for {LivePause.TotalSeconds:0} s (the room is not silent)");
                        Said(spoken, seconds, away);
                    }

                    if (segmenter.CurrentLength == 0)
                    {
                        near = false;
                        steady = string.Empty;
                    }

                    // What is being said, written in the box as it is said (away from the computer: only once called).
                    var now = clock.Elapsed.TotalSeconds;
                    var showPartial = !(_visionFace is { WatchesPlace: true }) || near || now - wokenAt <= WakeWaitSeconds;
                    // Away and not called: nothing is written before the end (Whisper does not run on what is said in the room).
                    // Asked again every LivePartialSeconds; sooner when the text stands still, to know it at LivePause.
                    var due = lastPartial + LivePartialSeconds;
                    if (steady.Length > 0)
                        due = Math.Min(due, Math.Max(lastPartial + 0.5, steadySince + LivePause.TotalSeconds));
                    if (!partialRunning && showPartial && now >= due && segmenter.CurrentLength > segmenter.Rate / 2)
                    {
                        var current = segmenter.CurrentTail(segmenter.Rate * LivePartialMostSeconds);
                        lastPartial = now;
                        partialRunning = true;
                        var turn = said;
                        var asked = now;
                        var stopped = VoiceStopped(current, segmenter.Rate);
                        _ = TranscribePartialAsync(speech, new SpeechSegment(current, segmenter.Rate, 0, current.Length / (double)segmenter.Rate), VoiceLanguage, WakePrompt(), stop.Token)
                            .ContinueWith(t =>
                            {
                                partialRunning = false;
                                // Still the same thing being said: written in the box (a sentence sent, or dropped, is not written back).
                                if (!t.IsCompletedSuccessfully || turn != said || LiveMuted)
                                    return;
                                if (t.Result.Length == 0)
                                {
                                    steady = string.Empty;
                                    return;
                                }
                                Input = t.Result;
                                Status = "🎙 … " + t.Result;
                                if (t.Result != steady)
                                {
                                    steady = t.Result;
                                    steadySince = asked;
                                }
                                else if (asked - steadySince >= LivePause.TotalSeconds && stopped && t.Result.Any(char.IsLetterOrDigit))
                                {
                                    // The same words for LivePause, and the voice has stopped: sent at the next block heard.
                                    steadyDue = t.Result;
                                }
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Turned off.
        }
        catch (Exception ex)
        {
            Status = "Live chat: " + ex.Message;
            ActivityLog.Error("Speech", "Live chat: " + ex.Message);
            if (_liveStop == stop)
            {
                StopLiveChat();
                LiveChat = false;
            }
            return;
        }
        ActivityLog.Info("Speech", "Live chat off");
        if (!LiveChat)
            Status = "🎙 Live chat off";
    }

    /// <summary>
    /// What Whisper is told was said before: the assistant called by its names, so that « Dis Kaonomi » is written so,
    /// and not « C'est un asomy ».
    /// </summary>
    private string WakePrompt()
    {
        static string Spoken(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
        var names = new[] { "Kaonomi", Spoken(Animation.ToString()), Spoken(SelectedPreset?.Name ?? string.Empty) }
            .Where(n => n.Length > 1 && !n.Equals("None", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(" ", names.Select(n => $"Dis {n}."));
    }

    /// <summary>The names the assistant answers to: its 3D animation, its preset, Kaonomi.</summary>
    private string[] WakeNames() =>
        [.. new[] { Animation.ToString(), SelectedPreset?.Name ?? string.Empty, "Kaonomi" }
            .Select(Fold).Where(n => n.Length > 1).Distinct()];

    /// <summary>
    /// What is said after « dis &lt;name&gt; » at the start of <paramref name="text"/> (« dis », « hey »… then the name of
    /// the assistant, as Whisper may write it: a letter or two apart, in one word or several); "" when nothing follows;
    /// null when the text does not start so.
    /// </summary>
    private string? AfterWakeWords(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(text, @"[\p{L}\p{N}]+");
        if (words.Count == 0)
            return null;
        var first = Fold(words[0].Value);
        // « Dis » apart (« Dis Kaonomi »), or written with the name in one word (« DicaNomi »).
        foreach (var wake in WakeWords.Where(w => first.StartsWith(w, StringComparison.Ordinal)))
        {
            foreach (var name in WakeNames())
            {
                var joined = first[wake.Length..];
                for (var i = 0; i < Math.Min(words.Count, 4); i++)
                {
                    if (i > 0)
                        joined += Fold(words[i].Value);
                    if (joined.Length > 0 && Distance(joined, name) <= (name.Length >= 6 ? 2 : 1))
                        return text[(words[i].Index + words[i].Length)..].TrimStart(' ', ',', '.', '!', '?', ';', ':', '-', '…').Trim();
                    if (joined.Length > name.Length + 2)
                        break;
                }
            }
        }
        return null;
    }

    /// <summary>Lower case, without accents nor what is not a letter or a digit.</summary>
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var kept = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (char.IsLetterOrDigit(c))
                kept.Append(char.ToLowerInvariant(c));
        return kept.ToString();
    }

    /// <summary>How many letters to change, add or remove to go from one word to the other.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>
    /// True when the voice has stopped in <paramref name="samples"/> (what is being said, at <paramref name="rate"/>):
    /// its last second is clearly quieter than its loudest second before (three quarters of its level at most). In a
    /// steady noise without a voice the two are alike: false, and nothing is sent on the words Whisper may hear in it.
    /// </summary>
    internal static bool VoiceStopped(ReadOnlySpan<float> samples, int rate)
    {
        // Energies by quarters of a second; a second is four of them.
        var step = Math.Max(1, rate / 4);
        var count = samples.Length / step;
        if (count < 8)
            return false;
        var tail = samples[^(count * step)..];
        var energies = new double[count];
        for (var i = 0; i < count; i++)
        {
            double sum = 0;
            foreach (var value in tail.Slice(i * step, step))
                sum += value * value;
            energies[i] = sum;
        }
        double Second(int from) => energies[from] + energies[from + 1] + energies[from + 2] + energies[from + 3];
        var last = Second(count - 4);
        double loudest = 0;
        for (var from = 0; from + 4 <= count - 4; from++)
            loudest = Math.Max(loudest, Second(from));
        // Levels (RMS) compare as the square roots of the energies: 0.75 of the level is 0.5625 of the energy.
        return loudest > 0 && last <= loudest * 0.75 * 0.75;
    }

    private static async Task<string> TranscribePartialAsync(SpeechRecognitionService speech, SpeechSegment segment, string language, string prompt, CancellationToken cancellationToken)
    {
        try
        {
            return (await speech.TranscribeAsync(Wav.ForWhisper(segment), language, cancellationToken, log: false, prompt: prompt)).Trim();
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
