using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>Someone whose voice is enrolled: the name, its samples, how long they are in all, a line and the detail of each sample.</summary>
public sealed record EnrolledVoice(string Name, int Samples, double Seconds, string Details, string SampleList);

/// <summary>
/// Who speaks, in /recorder (<see cref="SpeakerRecognition"/>: sherpa-onnx and the speaker model ERes2Net of
/// 3D-Speaker, open source, everything on this machine).
/// <para>
/// Enrolment, always through a popup, never a field of the window: when a recording ends, its voice is compared with
/// the voices enrolled; recognised, it is said; not recognised, a popup asks the name of who speaks (the voice of
/// the recording is then kept under it; Cancel ignores it). ENROL SELECTION does the same for the part selected on
/// the track of the recording selected (the whole of it when none is): where one person speaks alone. Several
/// samples per name make the identification steadier. 👥 VOICES lists who is enrolled (<see cref="EnrolledVoices"/>);
/// 🗑 on a line deletes the samples of that name, once confirmed.
/// </para>
/// <para>
/// Detection: LIVE says, about once a second, whose voice the microphone hears (the last 3 seconds), recording or
/// not; 🗣 WHO SPEAKS goes through the recording selected (or its selection) and notes, as annotations, each moment
/// someone starts to speak.
/// </para>
/// <para>
/// The voices enrolled are also kept in the search engine of the server (one <see cref="VoiceDocument"/> per sample:
/// the name and the embedding, not the sound), and both are kept alike: at the start what is missing on one side is
/// copied from the other; a voice enrolled is written at once; a name forgotten is erased there too, for good.
/// </para>
/// </summary>
public partial class RecorderWindowViewModel
{
    /// <summary>The seconds of microphone a live identification listens to, and the most of a recording an enrolment reads.</summary>
    private const double LiveSeconds = 3, EnrolMaxSeconds = 60;

    /// <summary>
    /// Under this level (root mean square, about -64 dB) nobody speaks. Low on purpose: a quiet microphone (a
    /// Bluetooth headset) gives speech around 0.002, and its silence around 0.0002.
    /// </summary>
    private const double SilenceLevel = 0.0006;

    /// <summary>A level in decibels, as the VU meter shows it.</summary>
    private static string Decibels(double level) => level <= 1e-6 ? "-120 dB" : $"{20 * Math.Log10(level):0} dB";

    private const string SpeakerMark = "🗣 ";

    private const int MaxVoices = 2000;

    private readonly SpeakerRecognition _speakers = new();
    private HashSet<string>? _voicesStored;
    private bool _speakersStarted;
    private int _speakerBusy;
    private long _speakerAt;

    /// <summary>Who the microphone hears now.</summary>
    [ObservableProperty]
    private string _speakerLabel = "…";

    /// <summary>The voice heard now is the one of someone enrolled.</summary>
    [ObservableProperty]
    private bool _speakerKnown;

    /// <summary>The names enrolled, with their samples.</summary>
    [ObservableProperty]
    private string _speakersLabel = "loading the speaker model…";

    /// <summary>The smallest similarity (cosine, in per cent) to say a voice is someone's.</summary>
    [ObservableProperty]
    private int _speakerThreshold = 50;

    /// <summary>The live identification of the voice heard by the microphone.</summary>
    [ObservableProperty]
    private bool _identifyLive = true;

    /// <summary>What the search engine holds of the voices: how many, or why not.</summary>
    [ObservableProperty]
    private string _voiceServerLabel = string.Empty;

    /// <summary>A recording is being gone through to find who speaks.</summary>
    [ObservableProperty]
    private bool _isFindingSpeakers;

    partial void OnIdentifyLiveChanged(bool value)
    {
        if (!value)
            (SpeakerLabel, SpeakerKnown) = ("live identification off", false);
    }

    /// <summary>The language of the chat (FR, EN…): kept with each voice enrolled here, as the language spoken in it.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The Whisper model of the chat (MY VOICE checks each take with it).</summary>
    public string SpeechModel { get; set; } = string.Empty;

    /// <summary>The voices enrolled, one line per name: what the window of the voices lists.</summary>
    public System.Collections.ObjectModel.ObservableCollection<EnrolledVoice> EnrolledVoices { get; } = [];

    /// <summary>The languages of the samples of someone, as a part of the line of the list (" · FR, EN"); empty when none is known.</summary>
    private static string Languages(IEnumerable<VoiceDocument> voices)
    {
        var languages = voices.Select(v => v.Language).Where(l => l.Length > 0).Distinct().OrderBy(l => l, StringComparer.Ordinal).ToList();
        return languages.Count > 0 ? " · " + string.Join(", ", languages) : string.Empty;
    }

    private void ShowSpeakers()
    {
        EnrolledVoices.Clear();
        foreach (var person in _speakers.All.GroupBy(v => v.Name).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var last = person.Max(v => v.Date).ToLocalTime();
            EnrolledVoices.Add(new EnrolledVoice(person.Key, person.Count(), person.Sum(v => v.Seconds),
                $"{person.Count()} sample(s) · {person.Sum(v => v.Seconds):0} s of voice{Languages(person)} · last enrolled {last:yyyy-MM-dd HH:mm}",
                string.Join("\n", person.OrderBy(v => v.Date).Select(v => $"{v.Date.ToLocalTime():yyyy-MM-dd HH:mm}  {v.Seconds:0.0} s  {(v.Language.Length > 0 ? v.Language + "  " : string.Empty)}{v.Source}"))));
        }
        var counts = _speakers.Counts;
        SpeakersLabel = counts.Count == 0
            ? "nobody enrolled yet: record someone speaking, the name is asked when the recording ends"
            : "enrolled: " + string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => $"{c.Key} ({c.Value})"));
    }

    /// <summary>
    /// Once the microphone listens: the voices of the search engine missing here are brought back, the speaker model
    /// is loaded (downloaded the first time), and the voices of this machine missing in the search engine are sent.
    /// </summary>
    private async Task StartSpeakersAsync()
    {
        if (_speakersStarted)
            return;
        _speakersStarted = true;
        try
        {
            await PullVoicesAsync();
            await _speakers.LoadAsync(text => SpeakersLabel = text);
            ShowSpeakers();
            ActivityLog.Info("Recorder", $"Who speaks: model {SpeakerRecognition.ModelName} loaded, {_speakers.Counts.Count} name(s) enrolled");
            await PushVoicesAsync(_speakers.All, onlyMissing: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException
                                       or DllNotFoundException or TypeInitializationException or EntryPointNotFoundException or BadImageFormatException)
        {
            _speakersStarted = false;
            SpeakersLabel = $"who speaks: not available ({ex.Message})";
            (SpeakerLabel, SpeakerKnown) = ("—", false);
            ActivityLog.Error("Recorder", "The speaker model could not be loaded", ex);
        }
    }

    // ── Detection ──

    /// <summary>Called ten times a second: about once a second, whose voice the microphone heard during the last seconds.</summary>
    private void RefreshSpeaker()
    {
        if (!IdentifyLive || !_speakers.IsReady || !Recorder.IsListening)
            return;
        var now = Environment.TickCount64;
        if (now - _speakerAt < 800 || Interlocked.CompareExchange(ref _speakerBusy, 1, 0) != 0)
            return;
        _speakerAt = now;
        // Silence, or nobody to recognise: said at once, without a copy of the seconds heard (most of the time).
        var level = Recorder.Level(LiveSeconds, out var heard);
        var quiet = heard < SpeakerRecognition.MinSeconds ? "listening…"
            : level < SilenceLevel ? $"silence ({Decibels(level)})"
            : _speakers.Counts.Count == 0 ? "a voice (nobody enrolled)"
            : null;
        if (quiet is not null)
        {
            (SpeakerLabel, SpeakerKnown) = (quiet, false);
            Interlocked.Exchange(ref _speakerBusy, 0);
            return;
        }
        var rate = Recorder.Rate;
        var samples = Recorder.Heard(LiveSeconds);
        var threshold = SpeakerThreshold / 100.0;
        _ = Task.Run(() =>
        {
            try
            {
                if (_speakers.Embed(samples, rate) is not { } embedding)
                    return;
                var match = _speakers.Identify(embedding, threshold);
                (SpeakerLabel, SpeakerKnown) = match.Name is { } name
                    ? ($"{name}  {match.Score * 100:0} %", true)
                    : ($"unknown voice (closest: {match.Closest} {match.Score * 100:0} %)", false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // The window closed meanwhile.
            }
            finally
            {
                Interlocked.Exchange(ref _speakerBusy, 0);
            }
        });
    }

    /// <summary>The samples of a part of a WAV file (the channels mixed into one), at most <paramref name="maxSeconds"/> of it.</summary>
    private static (float[] Samples, int Rate) ReadPart(string path, double start, double end, double maxSeconds)
    {
        using var reader = new WavReader(path);
        var rate = reader.Rate;
        var first = (long)(Math.Max(0, start) * rate);
        var count = (int)Math.Min(Math.Min((long)((end - start) * rate), reader.TotalFrames - first), (long)(maxSeconds * rate));
        if (count <= 0)
            return ([], rate);
        reader.Seek(first);
        var samples = new float[count];
        var read = 0;
        var chunk = new float[Math.Min(count, rate)];
        while (read < count)
        {
            var got = reader.Read(chunk);
            if (got <= 0)
                break;
            var take = Math.Min(got, count - read);
            Array.Copy(chunk, 0, samples, read, take);
            read += take;
        }
        return (read == count ? samples : samples[..read], rate);
    }

    /// <summary>
    /// 🗣 WHO SPEAKS: goes through the recording selected (its selection, or the whole of it) by windows of 3 seconds,
    /// one a second, and notes as annotations each moment someone starts to speak (the notes of a former search in
    /// that part are replaced).
    /// </summary>
    [RelayCommand]
    private async Task WhoSpeaks()
    {
        if (IsRecording || SelectedRecording is not { } item)
        {
            Report(IsRecording ? "Stop the recording before looking for who speaks in one" : "Select a recording", true);
            return;
        }
        if (!_speakers.IsReady || IsFindingSpeakers)
        {
            Report(IsFindingSpeakers ? "Already looking for who speaks…" : "The speaker model is not loaded yet", true);
            return;
        }
        if (_speakers.Counts.Count == 0)
        {
            Report("Nobody is enrolled: record someone speaking (the name is asked when the recording ends), or select a part of a recording, then ENROL SELECTION", true);
            return;
        }
        var (start, end) = Range();
        var threshold = SpeakerThreshold / 100.0;
        IsFindingSpeakers = true;
        Report($"Looking for who speaks in {item.Name} ({Clock(start)} – {Clock(end)})…", false);
        try
        {
            var turns = await Task.Run(() =>
            {
                // Each window: who (null: silence), and how sure.
                var windows = new List<(double Time, string? Who, double Score)>();
                using var reader = new WavReader(item.Path);
                var rate = reader.Rate;
                var size = (int)(LiveSeconds * rate);
                var buffer = new float[size];
                for (var time = start; time + SpeakerRecognition.MinSeconds <= end; time += 1)
                {
                    reader.Seek((long)(time * rate));
                    var got = 0;
                    while (got < size)
                    {
                        var chunk = new float[size - got];
                        var read = reader.Read(chunk);
                        if (read <= 0)
                            break;
                        Array.Copy(chunk, 0, buffer, got, read);
                        got += read;
                    }
                    var window = got == size ? buffer : buffer[..got];
                    if (got < SpeakerRecognition.MinSeconds * rate || SpeakerRecognition.Level(window) < SilenceLevel || _speakers.Embed(window, rate) is not { } embedding)
                    {
                        windows.Add((time, null, 0));
                        continue;
                    }
                    var match = _speakers.Identify(embedding, threshold);
                    windows.Add((time, match.Name ?? "unknown", match.Score));
                }
                // The turns: the consecutive windows of the same voice, a lone window between two of another dropped.
                for (var i = 1; i < windows.Count - 1; i++)
                    if (windows[i - 1].Who is not null && windows[i - 1].Who == windows[i + 1].Who && windows[i].Who != windows[i - 1].Who)
                        windows[i] = (windows[i].Time, windows[i - 1].Who, windows[i].Score);
                // A lone window of an unknown voice between two others is a change of voice, not someone.
                for (var i = 0; i < windows.Count; i++)
                    if (windows[i].Who == "unknown" && (i == 0 || windows[i - 1].Who != "unknown") && (i == windows.Count - 1 || windows[i + 1].Who != "unknown"))
                        windows[i] = (windows[i].Time, null, 0);
                var found = new List<(double Time, string Who, double Score, double Seconds)>();
                for (var i = 0; i < windows.Count; i++)
                {
                    var window = windows[i];
                    if (window.Who is null)
                        continue;
                    // A window is the voice that fills most of it: a turn that follows something else starts
                    // about its middle, not its start.
                    if (i > 0 && windows[i - 1].Who != window.Who)
                        window = (Math.Min(window.Time + LiveSeconds / 2, end), window.Who, window.Score);
                    if (found.Count > 0 && found[^1].Who == window.Who && window.Time - (found[^1].Time + found[^1].Seconds) <= 2)
                    {
                        var last = found[^1];
                        found[^1] = (last.Time, last.Who, Math.Max(last.Score, window.Score), Math.Max(1, window.Time + 1 - last.Time));
                    }
                    else
                        found.Add((window.Time, window.Who, window.Score, 1));
                }
                return found;
            });
            if (!ReferenceEquals(SelectedRecording, item))
                return;
            var info = item.Info;
            var replaced = info.Annotations.RemoveAll(a => a.Text.StartsWith(SpeakerMark, StringComparison.Ordinal) && a.Time >= start - 0.05 && a.Time <= end + 0.05);
            foreach (var turn in turns)
                info.Annotations.Add(new RecorderAnnotation
                {
                    Time = Math.Round(turn.Time, 1),
                    Text = turn.Who == "unknown" ? $"{SpeakerMark}unknown voice ({Clock(turn.Seconds)})" : $"{SpeakerMark}{turn.Who} ({turn.Score * 100:0} %, {Clock(turn.Seconds)})"
                });
            var who = turns.Where(t => t.Who != "unknown").GroupBy(t => t.Who).Select(g => $"{g.Key} {Clock(g.Sum(t => t.Seconds))}").ToList();
            var summary = turns.Count == 0 ? "nobody speaks" : $"{turns.Count} turn(s): {(who.Count > 0 ? string.Join(", ", who) : "only unknown voices")}";
            Log(info, "speakers", string.Empty, $"who speaks in {Clock(start)} – {Clock(end)}: {summary}" + (replaced > 0 ? $"; {replaced} former note(s) replaced" : string.Empty));
            SaveInfo(item.Path, info);
            item.Changed();
            ShowAnnotations(info);
            Report($"Who speaks: {summary} · noted as annotations", false);
            ActivityLog.Info("Recorder", $"Who speaks in {Path.GetFileName(item.Path)}: {summary}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Report($"Who speaks could not be found: {ex.Message}", true);
            ActivityLog.Error("Recorder", "Who speaks could not be found", ex);
        }
        finally
        {
            IsFindingSpeakers = false;
        }
    }

    // ── Enrolment ──

    /// <summary>
    /// Asks the name of who speaks in a popup (empty: the voice is one that is not known, no name is to be proposed);
    /// null when it is cancelled, or the name left empty.
    /// </summary>
    private async Task<string?> AskVoiceNameAsync(string caption, string question)
    {
        if (await AskAsync(caption, question, string.Empty, false) is not { } typed)
            return null;
        var name = SpeakerRecognition.SafeName(typed);
        return name.Length == 0 || name.Equals("unknown", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    private string EnrolledNames()
    {
        var counts = _speakers.Counts;
        return counts.Count == 0 ? "Nobody is enrolled yet." : "Enrolled: " + string.Join(", ", counts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) + ".";
    }

    /// <summary>
    /// A recording just ended: its voice is compared with the voices enrolled. Recognised: said in the status and
    /// kept in the manipulations of the recording. Not recognised: a popup asks the name of who speaks, and the
    /// voice of the recording is kept under it; Cancel ignores it (a part of the track can be enrolled later with
    /// ENROL SELECTION). Too short or silent: only said.
    /// </summary>
    private async Task RecogniseRecordingAsync(RecorderItemViewModel item)
    {
        if (!_speakers.IsReady)
            return;
        try
        {
            var seconds = item.Info.Seconds;
            var (samples, rate) = await Task.Run(() => ReadPart(item.Path, 0, seconds, EnrolMaxSeconds));
            var heard = samples.Length / (double)Math.Max(1, rate);
            var level = SpeakerRecognition.Level(samples);
            if (heard < SpeakerRecognition.MinSeconds || level < SilenceLevel)
            {
                var why = heard < SpeakerRecognition.MinSeconds ? $"too short ({heard:0.0} s; {SpeakerRecognition.MinSeconds:0.0} s of speech at least)" : $"silent (level {Decibels(level)} from {item.Info.Source}: the right microphone? muted?)";
                Report($"Recorded {Clock(seconds)} · who speaks: not looked for, the recording is {why}", false);
                ActivityLog.Info("Recorder", $"Who speaks in {Path.GetFileName(item.Path)}: not looked for, {why}");
                return;
            }
            var threshold = SpeakerThreshold / 100.0;
            var match = await Task.Run(() => _speakers.Embed(samples, rate) is { } embedding ? _speakers.Identify(embedding, threshold) : null);
            if (match is null)
                return;
            if (match.Name is { } known)
            {
                Log(item.Info, "speaker", string.Empty, $"voice recognised: {known} ({match.Score * 100:0} %)");
                SaveInfo(item.Path, item.Info);
                Report($"Recorded {Clock(seconds)} · voice recognised: {known} ({match.Score * 100:0} %)", false);
                ActivityLog.Info("Recorder", $"Voice of {Path.GetFileName(item.Path)} recognised: {known} ({match.Score * 100:0} %)");
                return;
            }
            var closest = match.Closest is { } near ? $" (the closest voice enrolled is {near}, {match.Score * 100:0} %)" : string.Empty;
            ActivityLog.Info("Recorder", $"Voice of {Path.GetFileName(item.Path)} not recognised{closest}: the name is asked");
            var name = await AskVoiceNameAsync("Who speaks?",
                $"The voice of this recording ({Clock(seconds)}) is not recognised{closest}.\n\nType the name of who speaks to enrol this voice: it will be recognised next time. {EnrolledNames()}\n\nCancel ignores it: several persons speak, or you prefer to select a part of the track and use ENROL SELECTION.");
            if (name is null)
            {
                Report($"Recorded {Clock(seconds)} · voice not recognised, not enrolled (select a part of the track, then ENROL SELECTION, to enrol someone)", false);
                return;
            }
            await EnrolAsync(name, samples, rate, $"{item.Info.Project}/{Path.GetFileName(item.Path)}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            ActivityLog.Error("Recorder", "The voice of the recording could not be compared with the voices enrolled", ex);
        }
    }

    /// <summary>
    /// ENROL SELECTION: keeps the voice of the part selected of the recording selected (the whole of it when none is;
    /// at most a minute) under the name asked in a popup.
    /// </summary>
    [RelayCommand]
    private async Task EnrolSelection()
    {
        if (!_speakers.IsReady)
        {
            Report($"The speaker model is not loaded yet ({SpeakersLabel})", true);
            return;
        }
        if (IsRecording || SelectedRecording is not { } item)
        {
            Report(IsRecording ? "Stop the recording first" : "Select a recording, and on its track the part where this person speaks alone", true);
            return;
        }
        var (start, end) = Range();
        try
        {
            var (samples, rate) = await Task.Run(() => ReadPart(item.Path, start, end, EnrolMaxSeconds));
            var heard = samples.Length / (double)Math.Max(1, rate);
            var level = SpeakerRecognition.Level(samples);
            if (heard < SpeakerRecognition.MinSeconds || level < SilenceLevel)
            {
                Report($"The part selected is too short or silent ({heard:0.0} s, level {Decibels(level)}): select a few seconds where this person speaks alone", true);
                ActivityLog.Info("Recorder", $"Enrol from a selection: refused, {heard:0.0} s at {Decibels(level)}");
                return;
            }
            var what = HasSelection ? $"the part selected ({Clock(start)} – {Clock(end)}, {heard:0.0} s)" : $"the whole recording ({heard:0.0} s)";
            var name = await AskVoiceNameAsync("Enrol a voice", $"Who speaks in {what} of {item.Name}?\n\nType the name: this voice is kept under it. {EnrolledNames()}");
            if (name is null)
                return;
            await EnrolAsync(name, samples, rate, $"{item.Info.Project}/{Path.GetFileName(item.Path)} {Clock(start)} – {Clock(end)}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Report($"The recording could not be read: {ex.Message}", true);
        }
    }

    /// <summary>
    /// The microphone can enrol a voice now: it listens and the speaker model is loaded. Otherwise, why not (null when
    /// it can).
    /// </summary>
    public string? CannotEnrolHeard()
    {
        if (!Recorder.IsListening)
            return IsError ? Status : "The microphone is not open";
        return _speakers.IsReady ? null : $"The speaker model is not loaded yet ({SpeakersLabel})";
    }

    /// <summary>
    /// The level of what the microphone heard during the last <paramref name="seconds"/>: the root mean square of the
    /// samples (0 to 1); 0 when it does not listen or has not heard that long yet.
    /// </summary>
    public double HeardLevel(double seconds)
    {
        var level = Recorder.Level(seconds, out var heard);
        return heard < seconds * 0.9 ? 0 : level;
    }

    /// <summary>A level under which nobody speaks (see <see cref="SilenceLevel"/>).</summary>
    public static bool IsSilence(double level) => level < SilenceLevel;

    /// <summary>A level as the VU meter shows it, in dB.</summary>
    public static string LevelText(double level) => Decibels(level);

    /// <summary>
    /// Enrols under <paramref name="name"/> the voice the microphone heard during the last <paramref name="seconds"/>
    /// (someone asked to speak, as in /vision where a face is enrolled with its voice). Null when it is enrolled,
    /// else why not: the microphone or the model not ready, too short, silent, too little speech.
    /// </summary>
    public async Task<string?> EnrolHeardAsync(string name, double seconds, string source)
    {
        name = name.Trim();
        if (CannotEnrolHeard() is { } why)
            return why;
        var rate = Recorder.Rate;
        var samples = Recorder.Heard(seconds);
        var heard = samples.Length / (double)Math.Max(1, rate);
        var level = SpeakerRecognition.Level(samples);
        if (heard < SpeakerRecognition.MinSeconds || level < SilenceLevel)
        {
            ActivityLog.Info("Recorder", $"Enrol from the microphone: refused, {heard:0.0} s at {Decibels(level)}");
            return $"No voice was heard ({heard:0.0} s, level {Decibels(level)}): the person must speak during the {seconds:0} seconds";
        }
        try
        {
            var voice = await Task.Run(() => _speakers.Enrol(name, samples, rate, source, Language));
            if (voice is null)
                return $"Too little speech in these {seconds:0} seconds to enrol a voice: speak all along";
            ShowSpeakers();
            Report($"{name} enrolled: {voice.Seconds:0.0} s of voice from {source} · {_speakers.Counts[name]} sample(s) of this voice", false);
            ActivityLog.Info("Recorder", $"Voice of {name} enrolled ({voice.Seconds:0.0} s, {source})");
            await PushVoicesAsync([voice], onlyMissing: false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ActivityLog.Error("Recorder", "The voice could not be enrolled", ex);
            return $"The voice could not be enrolled: {ex.Message}";
        }
    }

    private async Task EnrolAsync(string name, float[] samples, int rate, string source)
    {
        try
        {
            var voice = await Task.Run(() => _speakers.Enrol(name, samples, rate, source, Language));
            if (voice is null)
            {
                Report("Too little speech to enrol a voice: a few seconds are needed", true);
                return;
            }
            ShowSpeakers();
            Report($"{name} enrolled: {voice.Seconds:0.0} s of voice from {source} · {_speakers.Counts[name]} sample(s) of this voice", false);
            ActivityLog.Info("Recorder", $"Voice of {name} enrolled ({voice.Seconds:0.0} s, {source})");
            await PushVoicesAsync([voice], onlyMissing: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Report($"The voice could not be enrolled: {ex.Message}", true);
            ActivityLog.Error("Recorder", "The voice could not be enrolled", ex);
        }
    }

    /// <summary>🗑 in the window of the voices: deletes the samples of voice kept under this name, here and in the search engine, once confirmed.</summary>
    [RelayCommand]
    private async Task ForgetVoice(EnrolledVoice? voice)
    {
        if (voice is null)
            return;
        var name = voice.Name;
        if (!_speakers.Counts.ContainsKey(name))
        {
            ShowSpeakers();
            return;
        }
        if (!await ConfirmAsync("Forget a voice", $"Forget the voice of {name}? Its samples are deleted, on this machine and in the search engine: this cannot be undone."))
            return;
        try
        {
            var ids = _speakers.All.Where(v => v.Name == name).Select(v => v.Id).ToList();
            _speakers.Forget(name);
            ShowSpeakers();
            Report($"The voice of {name} is forgotten", false);
            ActivityLog.Info("Recorder", $"Voice of {name} forgotten");
            await EraseVoicesAsync(name, ids);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"The voice could not be forgotten: {ex.Message}", true);
        }
    }

    /// <summary>Opens the folder of the voices enrolled in the file explorer of the system.</summary>
    [RelayCommand]
    private void OpenSpeakersFolder()
    {
        try
        {
            Directory.CreateDirectory(SpeakerRecognition.SpeakersDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SpeakerRecognition.SpeakersDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Report($"The folder could not be opened: {ex.Message}", true);
        }
    }

    // ── The search engine ──

    /// <summary>The voices of the search engine missing in the folder of the voices are written there.</summary>
    private async Task PullVoicesAsync()
    {
        _voicesStored = null;
        if (_assistant is null)
        {
            VoiceServerLabel = "search engine: no server";
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var voices = await _assistant.LastVoicesAsync(MaxVoices, timeout.Token);
            var written = 0;
            foreach (var voice in voices)
            {
                // A name and a file as they are written here, and nothing else: they become a path.
                if (voice.Name.Length == 0 || voice.File.Length == 0 || voice.Feature.Count == 0
                    || voice.Name != Path.GetFileName(voice.Name) || voice.File != Path.GetFileName(voice.File)
                    || voice.Name is "." or ".." || voice.Name != SpeakerRecognition.SafeName(voice.Name) || !voice.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(Path.Combine(SpeakerRecognition.SpeakersDirectory, voice.Name, voice.File)))
                    continue;
                SpeakerRecognition.Write(voice);
                written++;
            }
            _voicesStored = [.. voices.Select(v => v.Id)];
            VoiceServerLabel = $"search engine: {voices.Count} voice sample(s)" + (written > 0 ? $", {written} brought back to this machine" : string.Empty);
            if (written > 0)
                ActivityLog.Info("Recorder", $"{written} voice sample(s) brought back from the search engine into {SpeakerRecognition.SpeakersDirectory}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            VoiceServerLabel = $"search engine: not reached ({ex.Message}); the voices of this machine are used";
            ActivityLog.Error("Recorder", "The voices of the search engine could not be read", ex);
        }
    }

    /// <summary>Writes these voices in the search engine (only the ones it does not hold, or all of them).</summary>
    private async Task PushVoicesAsync(IReadOnlyList<VoiceDocument> voices, bool onlyMissing)
    {
        if (_assistant is null)
            return;
        var stored = _voicesStored;
        var saved = 0;
        try
        {
            foreach (var voice in voices)
            {
                if (onlyMissing && stored is not null && stored.Contains(voice.Id))
                    continue;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = await _assistant.SaveVoiceAsync(voice, timeout.Token);
                if (!result.IsSuccess)
                {
                    VoiceServerLabel = $"search engine: {voice.Name} not saved ({result.Message})";
                    ActivityLog.Error("Recorder", $"Voice {voice.Id} refused by the server: {result.Message}");
                    return;
                }
                stored?.Add(voice.Id);
                saved++;
            }
            if (saved > 0 || !onlyMissing)
                VoiceServerLabel = $"search engine: {saved} voice sample(s) saved {DateTime.Now:HH:mm:ss}" + (stored is not null ? $", {stored.Count} in all" : string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            VoiceServerLabel = $"search engine: not saved ({ex.Message}); kept on this machine, written at the next start";
            ActivityLog.Error("Recorder", "The voices could not be saved in the search engine", ex);
        }
    }

    /// <summary>Erases every sample of this name from the search engine, for good.</summary>
    private async Task EraseVoicesAsync(string name, IReadOnlyList<string> known)
    {
        if (_assistant is null)
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // The ones the search engine lists, and the ones just written that it does not list yet.
            var ids = (await _assistant.LastVoicesAsync(MaxVoices, timeout.Token)).Where(v => v.Name == name).Select(v => v.Id).Concat(known).Distinct().ToList();
            foreach (var id in ids)
            {
                await _assistant.DeleteVoiceAsync(id, timeout.Token);
                _voicesStored?.Remove(id);
            }
            VoiceServerLabel = $"search engine: {name} erased ({ids.Count} sample(s))";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            VoiceServerLabel = $"search engine: {name} NOT erased ({ex.Message}): it would come back at the next start, FORGET it again";
            ActivityLog.Error("Recorder", $"The voice of {name} could not be erased from the search engine", ex);
        }
    }
}
