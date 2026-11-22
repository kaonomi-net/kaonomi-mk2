using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A take of a sentence: what was to be read, what Whisper heard, how alike (0 to 1), how long, when.</summary>
public sealed record VoiceTake(string Id, string Text, string Heard, double Score, double Seconds, DateTime Date);

/// <summary>
/// MY VOICE (/recorder): the data to make a voice from one's own. The sentences of <c>sentences.txt</c> are shown one
/// after the other; ● RECORD listens, the recording stops by itself after a silence of a second once something was
/// said (or on ■ STOP), and the take is kept as <c>wavs/0001.wav</c> (mono, 22 050 Hz, 16 bits: what Piper trains on),
/// its sentence in <c>metadata.csv</c> (<c>0001|the sentence</c>, the format LJSpeech of the training of Piper).
/// Whisper listens to each take and says how alike what it heard is to the sentence: under
/// <see cref="GoodScore"/>, the take is said doubtful (it is kept; REDO records it again). A take recorded again has
/// the former one kept aside in <c>rejected/</c>, never erased. Everything stays on this machine, in
/// <c>~/kaonomi/audio/myvoice/&lt;name&gt;</c>.
/// </summary>
public sealed partial class VoiceDatasetViewModel : ViewModelBase, IDisposable, IStaysOpen
{
    /// <summary>
    /// MY VOICE never closes for want of use: it is worked with the space bar (which the desktop does not count as a
    /// use of the window), and closed, it would close the recorder under it. It closes when the user closes it.
    /// </summary>
    public bool StaysOpen => !_disposed;

    /// <summary>The rate of the takes: the one of the medium voices of Piper.</summary>
    public const int Rate = 22050;

    /// <summary>A take whose words are at least this alike to its sentence is good.</summary>
    public const double GoodScore = 0.85;

    /// <summary>The longest take, in seconds: the recording stops there.</summary>
    private const double MaxSeconds = 25;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Root => Path.Combine(ChatSettings.KaonomiHome, "audio", "myvoice");

    private static string LastNameFile => Path.Combine(Root, "last.txt");

    private readonly RecorderWindowViewModel _recorder;
    private readonly SpeechRecognitionService _whisper;
    private readonly string _language;
    private readonly object _lock = new();
    private readonly Dictionary<string, VoiceTake> _takes = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _sentences = [];
    private TakeDetector? _detector;
    private int _recordingRate;
    private bool _disposed;
    // While a take records: when it started, when the microphone last sent something, the watch of both.
    private long _recordStarted, _lastSamples, _lastSound;
    // The loudest sample of the take so far (its level, logged and shown when nothing is heard).
    private float _loudest;
    private Timer? _watch;

    public VoiceDatasetViewModel(RecorderWindowViewModel recorder, string speechModel, string language)
    {
        _recorder = recorder;
        _whisper = new SpeechRecognitionService(speechModel);
        _language = string.IsNullOrWhiteSpace(language) ? "FR" : language.ToUpperInvariant();
        string? last = null;
        try
        {
            last = File.Exists(LastNameFile) ? File.ReadAllText(LastNameFile).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The default name.
        }
        _voiceName = string.IsNullOrWhiteSpace(last) ? "ma-voix" : last;
        Load();
        _ = _whisper.WarmUpAsync(WhisperLanguage);
    }

    private string WhisperLanguage => _language.ToLowerInvariant();

    /// <summary>The name of the voice: the folder of its data (letters, digits and dashes).</summary>
    [ObservableProperty]
    private string _voiceName;

    partial void OnVoiceNameChanged(string value)
    {
        var name = new string(value.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (name != value)
        {
            VoiceName = name.Length == 0 ? "ma-voix" : name;
            return;
        }
        if (IsRecording)
            return;
        Load();
    }

    public string Folder => Path.Combine(Root, VoiceName);

    private string WavsFolder => Path.Combine(Folder, "wavs");

    private string SentencesFile => Path.Combine(Folder, "sentences.txt");

    private string MetadataFile => Path.Combine(Folder, "metadata.csv");

    private string TakesFile => Path.Combine(Folder, "takes.json");

    /// <summary>The sentence shown, by its number (from 0).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Sentence), nameof(SentenceNumber), nameof(Take))]
    private int _index;

    /// <summary>The sentence to read.</summary>
    public string Sentence => Index >= 0 && Index < _sentences.Count ? _sentences[Index] : "All the sentences are recorded.";

    public string SentenceNumber => $"Sentence {Math.Min(Index + 1, _sentences.Count)} / {_sentences.Count}";

    /// <summary>The take of the sentence shown, when it has one.</summary>
    public VoiceTake? Take
    {
        get
        {
            lock (_lock)
                return _takes.GetValueOrDefault(IdOf(Index));
        }
    }

    [ObservableProperty]
    private bool _isRecording;

    /// <summary>How long the take records so far, shown while it does.</summary>
    [ObservableProperty]
    private string _recordingTime = string.Empty;

    [ObservableProperty]
    private bool _isChecking;

    /// <summary>How many sentences are recorded, how long of voice, how many doubtful.</summary>
    [ObservableProperty]
    private string _progress = string.Empty;

    /// <summary>What happens (listening, checking, kept, doubtful…).</summary>
    [ObservableProperty]
    private string _state = string.Empty;

    /// <summary>The state is a warning (a doubtful take, an error).</summary>
    [ObservableProperty]
    private bool _isWarning;

    private static string IdOf(int index) => (index + 1).ToString("D4");

    /// <summary>The voice of <see cref="VoiceName"/>: its sentences (written the first time), its takes, the first sentence without one.</summary>
    private void Load()
    {
        try
        {
            Directory.CreateDirectory(WavsFolder);
            if (!File.Exists(SentencesFile))
                File.WriteAllLines(SentencesFile, VoiceSentences.For(_language));
            _sentences = [.. File.ReadAllLines(SentencesFile).Select(l => l.Trim()).Where(l => l.Length > 0)];
            File.WriteAllText(LastNameFile, VoiceName);
            lock (_lock)
            {
                _takes.Clear();
                if (File.Exists(TakesFile) && JsonSerializer.Deserialize<List<VoiceTake>>(File.ReadAllText(TakesFile)) is { } takes)
                    foreach (var take in takes.Where(t => File.Exists(Path.Combine(WavsFolder, t.Id + ".wav"))))
                        _takes[take.Id] = take;
            }
            // The scores of the takes computed again from what Whisper heard (the way they are compared may have
            // changed: the numbers in digits are now said in words first); takes.json written again when one changed.
            var changed = false;
            lock (_lock)
                foreach (var take in _takes.Values.Where(t => t.Heard.Length > 0).ToList())
                {
                    var score = Math.Round(Likeness(take.Text, take.Heard), 3);
                    if (Math.Abs(score - take.Score) > 0.0005)
                    {
                        _takes[take.Id] = take with { Score = score };
                        changed = true;
                    }
                }
            if (changed)
                Save();
            WriteReadme();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Say($"The voice {VoiceName} could not be read: {ex.Message}", true);
        }
        var first = Enumerable.Range(0, _sentences.Count).FirstOrDefault(i => TakeOf(i) is null, _sentences.Count);
        Index = Math.Min(first, Math.Max(0, _sentences.Count - 1));
        OnPropertyChanged(nameof(Sentence));
        OnPropertyChanged(nameof(SentenceNumber));
        OnPropertyChanged(nameof(Take));
        UpdateProgress();
        if (State.Length == 0)
            Say($"Read the sentence aloud after ● RECORD (or the space bar): the recording stops by itself after a second of silence. The data: {Folder}");
    }

    private VoiceTake? TakeOf(int index)
    {
        lock (_lock)
            return _takes.GetValueOrDefault(IdOf(index));
    }

    private void UpdateProgress()
    {
        List<VoiceTake> takes;
        lock (_lock)
            takes = [.. _takes.Values];
        var seconds = takes.Sum(t => t.Seconds);
        var doubtful = takes.Count(t => t.Score < GoodScore);
        Progress = $"{takes.Count} / {_sentences.Count} recorded · {TimeSpan.FromSeconds(seconds):mm\\:ss} of voice"
                   + (doubtful > 0 ? $" · {doubtful} doubtful" : string.Empty)
                   + " · 30 to 60 minutes make a good voice";
    }

    private void Say(string text, bool warning = false)
    {
        State = text;
        IsWarning = warning;
    }

    // ── Moving through the sentences ──

    public void Next()
    {
        if (IsRecording)
            return;
        if (Index < _sentences.Count - 1)
            Index++;
        else if (_sentences.Count > 0 && Enumerable.Range(0, _sentences.Count).All(i => TakeOf(i) is not null))
            SayAllRecorded();
        else
            Say("This is the last sentence: NEXT MISSING goes to the ones not recorded yet.");
    }

    public void Previous()
    {
        if (!IsRecording && Index > 0)
            Index--;
    }

    /// <summary>The next sentence without a take (after the one shown, then from the start); all recorded: said so.</summary>
    public void NextMissing()
    {
        if (IsRecording)
            return;
        if (!MoveTo(i => TakeOf(i) is null))
            SayAllRecorded();
    }

    /// <summary>The next sentence whose take is doubtful (after the one shown, then from the start), to record it again.</summary>
    public void NextDoubtful()
    {
        if (IsRecording)
            return;
        if (MoveTo(i => TakeOf(i) is { } take && take.Score < GoodScore))
            Say("A doubtful take: ● REDO to record it again (PLAY to listen to it first), or NEXT DOUBTFUL to leave it");
        else
            Say("No doubtful take left.");
    }

    /// <summary>Goes to the next sentence (after the one shown, then from the start) that <paramref name="wanted"/>; false when none.</summary>
    private bool MoveTo(Func<int, bool> wanted)
    {
        for (var step = 1; step <= _sentences.Count; step++)
        {
            var i = (Index + step) % _sentences.Count;
            if (wanted(i))
            {
                Index = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>Every sentence has its take: what to do next.</summary>
    private void SayAllRecorded()
    {
        List<VoiceTake> takes;
        lock (_lock)
            takes = [.. _takes.Values];
        var minutes = takes.Sum(t => t.Seconds) / 60;
        var doubtful = takes.Count(t => t.Score < GoodScore);
        Say($"All the {_sentences.Count} sentences are recorded ({minutes:0.0} min of voice). "
            + (doubtful > 0 ? $"NEXT DOUBTFUL goes through the {doubtful} doubtful takes; " : string.Empty)
            + (minutes < 30 ? "+ SENTENCES adds new ones to read: 30 to 60 minutes make a good voice." : "That is enough to train a voice."));
    }

    /// <summary>The sentences not in the list yet, by batches (the second list first, longer sentences).</summary>
    public int MoreSentencesLeft => VoiceSentences.More(_language).Count(s => !_sentences.Contains(s));

    /// <summary>
    /// + SENTENCES: the next sentences of the built-in lists not read yet (<paramref name="count"/> at most) added at
    /// the end of sentences.txt, and the first of them shown. Nothing recorded is changed.
    /// </summary>
    public void AddSentences(int count = 100)
    {
        if (IsRecording)
            return;
        var known = new HashSet<string>(_sentences, StringComparer.Ordinal);
        var added = VoiceSentences.More(_language).Where(known.Add).Take(count).ToList();
        if (added.Count == 0)
        {
            Say("No other sentence in the lists: write yours in sentences.txt (one a line), then reopen MY VOICE", true);
            return;
        }
        try
        {
            File.AppendAllLines(SentencesFile, added);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"The sentences could not be added: {ex.Message}", true);
            return;
        }
        var first = _sentences.Count;
        _sentences = [.. _sentences, .. added];
        Index = first;
        OnPropertyChanged(nameof(Sentence));
        OnPropertyChanged(nameof(SentenceNumber));
        OnPropertyChanged(nameof(Take));
        OnPropertyChanged(nameof(MoreSentencesLeft));
        UpdateProgress();
        Say($"{added.Count} sentences added (sentence {first + 1} to {_sentences.Count}): ● RECORD");
    }

    // ── Recording ──

    /// <summary>Starts listening for the take of the sentence shown (the former take is kept aside when the new one is kept).</summary>
    public void Record()
    {
        if (IsRecording || IsChecking || Index >= _sentences.Count || _disposed)
            return;
        var recorder = _recorder.Recorder;
        if (!recorder.IsListening)
        {
            Say("The microphone is not open: choose one above", true);
            return;
        }
        lock (_lock)
        {
            _recordingRate = recorder.Rate;
            _detector = new TakeDetector(_recordingRate);
        }
        _recordStarted = _lastSamples = _lastSound = Environment.TickCount64;
        _loudest = 0;
        recorder.SamplesHeard += OnSamples;
        IsRecording = true;
        RecordingTime = "● 0.0 s";
        Say("● Speak now: read the sentence…");
        ActivityLog.Info("MyVoice", $"{VoiceName} {IdOf(Index)}: recording ({recorder.SourceName}, {_recordingRate} Hz)");
        // The watch: the time shown, and a microphone that stops sending sound noticed (the take would wait for ever).
        _watch?.Dispose();
        _watch = new Timer(_ => Watch(), null, 250, 250);
    }

    /// <summary>Four times a second while a take records: its time; no sound from the microphone for 1.5 s: stopped, the microphone reopened.</summary>
    private void Watch()
    {
        if (!IsRecording)
            return;
        var now = Environment.TickCount64;
        RecordingTime = $"● {(now - _recordStarted) / 1000.0:0.0} s";
        var noData = now - Interlocked.Read(ref _lastSamples) >= 1500;
        // Samples that are all zero (or nearly: under -100 dB) for 1.5 s: the microphone sends nothing either.
        var digitalSilence = now - Interlocked.Read(ref _lastSound) >= 1500;
        if (!noData && !digitalSilence)
            return;
        ActivityLog.Info("MyVoice", $"{VoiceName} {IdOf(Index)}: {(noData ? "no sound from the microphone" : "only silence (zeros) from the microphone")} for 1.5 s (loudest {Db(_loudest)}), the take is stopped and the microphone reopened");
        lock (_lock)
            _detector = null;
        EndRecording();
        try
        {
            if (_recorder.Recorder.Restart())
            {
                Say("The microphone stopped sending sound: it is reopened. ● RECORD again", true);
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or PortAudioSharp.PortAudioException or DllNotFoundException)
        {
            ActivityLog.Error("MyVoice", "The microphone could not be reopened", ex);
        }
        Say("The microphone stopped sending sound and could not be reopened: choose it again in the list above", true);
    }

    /// <summary>The take no longer listened to: the microphone let go, the watch stopped.</summary>
    private void EndRecording()
    {
        _recorder.Recorder.SamplesHeard -= OnSamples;
        _watch?.Dispose();
        _watch = null;
        IsRecording = false;
        RecordingTime = string.Empty;
    }

    /// <summary>
    /// ■ STOP: the take ends now and what was recorded is kept, whatever the detection heard (the user said
    /// something): from the speech heard to now, or all of it. Whisper says next whether it is the sentence.
    /// </summary>
    public void Stop()
    {
        if (!IsRecording)
            return;
        float[]? take;
        int rate;
        lock (_lock)
        {
            take = _detector?.Take(whole: true);
            rate = _recordingRate;
        }
        Finish(take, rate, "■ STOP");
    }

    /// <summary>The samples of the microphone (on its thread): the end of what is said ends the take.</summary>
    private void OnSamples(ReadOnlySpan<float> samples)
    {
        float[]? done;
        string why;
        var tick = Environment.TickCount64;
        Interlocked.Exchange(ref _lastSamples, tick);
        var loudest = 0f;
        foreach (var sample in samples)
            loudest = Math.Max(loudest, Math.Abs(sample));
        if (loudest > 1e-5f)
            Interlocked.Exchange(ref _lastSound, tick);
        if (loudest > _loudest)
            _loudest = loudest;
        int rate;
        lock (_lock)
        {
            if (_detector is not { } detector)
                return;
            rate = _recordingRate;
            if (detector.Add(samples))
            {
                done = detector.Take(whole: false);
                why = "a second of silence";
            }
            else if (detector.Length > MaxSeconds * rate)
            {
                done = detector.Take(whole: true);
                why = $"{MaxSeconds:0} s reached";
                ActivityLog.Info("MyVoice", $"{VoiceName}: the take reached {MaxSeconds:0} s without a second of silence (loudest {Db(_loudest)}, noise {Db(detector.Noise)}, speech heard: {detector.HeardSpeech}): stopped");
            }
            else
                return;
        }
        Task.Run(() => Finish(done, rate, why));
    }

    private void Finish(float[]? take, int rate, string why)
    {
        TakeDetector? detector;
        lock (_lock)
        {
            detector = _detector;
            if (detector is null)
                return;
            _detector = null;
        }
        EndRecording();
        if (take is null)
        {
            ActivityLog.Info("MyVoice", $"{VoiceName} {IdOf(Index)}: nothing was heard ({why}; loudest {Db(_loudest)}, noise {Db(detector.Noise)})");
            Say(_loudest < 0.01f
                ? $"Nothing was heard: the microphone is very quiet (loudest {Db(_loudest)}). Raise its level (+3 dB) or speak closer to it, then ● RECORD"
                : "Nothing was heard: ● RECORD again and read the sentence", true);
            return;
        }
        _ = KeepAsync(Index, new SpeechSegment(take, rate, 0, take.Length / (double)rate));
    }

    /// <summary>The take written (the former one kept aside), checked by Whisper, noted in metadata.csv and takes.json.</summary>
    private async Task KeepAsync(int index, SpeechSegment segment)
    {
        var id = IdOf(index);
        var text = _sentences[index];
        IsChecking = true;
        Say("Checking the take with Whisper…");
        try
        {
            var path = Path.Combine(WavsFolder, id + ".wav");
            if (File.Exists(path))
            {
                var rejected = Path.Combine(Folder, "rejected");
                Directory.CreateDirectory(rejected);
                File.Move(path, Path.Combine(rejected, $"{id}-{DateTime.Now:yyyyMMdd-HHmmss}.wav"));
            }
            // The hum of the room taken away, every take as loud (what the training of the voice wants).
            var samples = TakeDetector.Clean(SpeechRecognitionService.Resample(segment.Samples, segment.Rate, Rate), Rate);
            await Task.Run(() => WriteWav(path, samples));
            var seconds = samples.Length / (double)Rate;

            var heard = string.Empty;
            try
            {
                heard = (await _whisper.TranscribeAsync(Wav.ForWhisper(segment), WhisperLanguage, log: false)).Trim();
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or OperationCanceledException)
            {
                ActivityLog.Info("MyVoice", "Whisper could not check the take: " + ex.Message);
            }
            var score = heard.Length == 0 ? 0 : Likeness(text, heard);
            var take = new VoiceTake(id, text, heard, Math.Round(score, 3), Math.Round(seconds, 2), DateTime.Now);
            lock (_lock)
                _takes[id] = take;
            Save();
            UpdateProgress();
            OnPropertyChanged(nameof(Take));
            ActivityLog.Info("MyVoice", $"{VoiceName} {id}: {seconds:0.0} s, {score * 100:0} % alike");
            if (score >= GoodScore)
            {
                Say($"✓ Kept ({seconds:0.0} s, {score * 100:0} % of the words heard by Whisper): next sentence");
                if (Index == index)
                {
                    if (!MoveTo(i => TakeOf(i) is null))
                        SayAllRecorded();
                }
            }
            else
                Say(heard.Length == 0
                    ? $"Kept ({seconds:0.0} s), but Whisper could not check it: REDO if it was not well read"
                    : $"Doubtful: Whisper heard « {heard} » ({score * 100:0} % alike). REDO to record it again, or NEXT to keep it", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"The take could not be kept: {ex.Message}", true);
            ActivityLog.Error("MyVoice", "The take could not be kept", ex);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static string Db(float level) => level <= 1e-6f ? "-120 dB" : $"{20 * Math.Log10(level):0} dB";

    private static void WriteWav(string path, float[] samples)
    {
        using var file = File.Create(path);
        Wav.WriteHeader(file, Rate, 0);
        file.Position = 44;
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue));
        file.Write(bytes);
        Wav.WriteHeader(file, Rate, samples.Length);
    }

    /// <summary>metadata.csv (the takes in the order of their sentences: <c>0001|the sentence</c>) and takes.json.</summary>
    private void Save()
    {
        List<VoiceTake> takes;
        lock (_lock)
            takes = [.. _takes.Values.OrderBy(t => t.Id, StringComparer.Ordinal)];
        File.WriteAllLines(MetadataFile, takes.Select(t => $"{t.Id}|{t.Text.Replace('|', ' ')}"), new UTF8Encoding(false));
        File.WriteAllText(TakesFile, JsonSerializer.Serialize(takes, Json));
    }

    /// <summary>
    /// How alike two sentences are, by their words (case, accents and punctuation aside; the numbers Whisper writes in
    /// digits, « 10h », « 189€ », « 1er », said in words first): 1 the same, 0 nothing alike.
    /// </summary>
    public static double Likeness(string expected, string heard)
    {
        expected = NumbersInWords(expected);
        heard = NumbersInWords(heard);
        static string[] Words(string text)
        {
            var folded = new StringBuilder();
            foreach (var c in text.Normalize(NormalizationForm.FormD))
                if (char.IsLetterOrDigit(c))
                    folded.Append(char.ToLowerInvariant(c));
                else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    folded.Append(' ');
            return folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        var a = Words(expected);
        var b = Words(heard);
        if (a.Length == 0)
            return 0;
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
        return Math.Max(0, 1 - previous[b.Length] / (double)Math.Max(a.Length, b.Length));
    }

    /// <summary>
    /// The numbers written in digits said in French words, as they are read: « 15h30 » quinze heures trente, « 10h »
    /// dix heures, « 189€ » cent quatre-vingt-neuf euros, « 200 g » deux cents grammes, « 1er » premier, « 2026 » deux
    /// mille vingt-six.
    /// </summary>
    public static string NumbersInWords(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)\s*h\s*(\d{1,2})\b", m => $"{French(m.Groups[1].Value)} heures {French(m.Groups[2].Value)}");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)\s*h\b", m => $"{French(m.Groups[1].Value)} heures");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b1\s*(er|ère)\b", m => m.Groups[1].Value == "er" ? "premier" : "première");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)\s*€", m => $"{French(m.Groups[1].Value)} euros");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)\s*%", m => $"{French(m.Groups[1].Value)} pour cent");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)\s*g\b", m => $"{French(m.Groups[1].Value)} grammes");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+)\s*km\b", m => $"{French(m.Groups[1].Value)} kilomètres");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d)[\s\u00A0\u202F.](?=\d{3}\b)", "$1");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\d+", m => French(m.Value));
    }

    private static string French(string digits) => long.TryParse(digits, out var n) && n < 1_000_000_000 ? French(n) : digits;

    private static readonly string[] Units = ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix", "onze", "douze",
        "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"];

    private static readonly string[] Tens = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];

    /// <summary>A number in French words (the old spelling: vingt et un, quatre-vingts, deux cents, mille).</summary>
    private static string French(long n)
    {
        if (n < 20)
            return Units[n];
        if (n < 100)
        {
            var ten = n / 10;
            var unit = n % 10;
            if (ten is 7 or 9)
                return Tens[ten] + (unit == 1 && ten == 7 ? " et onze" : "-" + Units[10 + unit]);
            if (unit == 0)
                return ten == 8 ? "quatre-vingts" : Tens[ten];
            return Tens[ten] + (unit == 1 && ten != 8 ? " et un" : "-" + Units[unit]);
        }
        if (n < 1000)
        {
            var hundreds = n / 100;
            var rest = n % 100;
            var head = hundreds == 1 ? "cent" : Units[hundreds] + (rest == 0 ? " cents" : " cent");
            return rest == 0 ? head : head + " " + French(rest);
        }
        if (n < 1_000_000)
        {
            var thousands = n / 1000;
            var rest = n % 1000;
            var head = thousands == 1 ? "mille" : French(thousands) + " mille";
            return rest == 0 ? head : head + " " + French(rest);
        }
        var millions = n / 1_000_000;
        var left = n % 1_000_000;
        var start = millions == 1 ? "un million" : French(millions) + " millions";
        return left == 0 ? start : start + " " + French(left);
    }

    /// <summary>README.txt of the voice: what the folder holds, and how a Piper voice is trained on it.</summary>
    private void WriteReadme()
    {
        var readme = Path.Combine(Folder, "README.txt");
        if (File.Exists(readme))
            return;
        File.WriteAllText(readme, $"""
            The data to make the voice « {VoiceName} » (MY VOICE of /recorder in the Kaonomi chat).

            wavs/0001.wav …   the takes: mono, {Rate} Hz, 16 bits, one sentence each
            metadata.csv      0001|the sentence read   (the format LJSpeech, read by the training of Piper)
            takes.json        each take: the sentence, what Whisper heard, how alike (score, 0 to 1), its length, its date
            sentences.txt     the sentences shown, one a line (edit it to read others)
            rejected/         the takes recorded again: the former ones, kept

            Train a Piper voice on it: fine-tune a French voice of the same quality ("medium", {Rate} Hz) with
            piper1-gpl (its training: preprocessing of this folder, then a fine-tuning from the checkpoint of the voice),
            then export the .onnx and its .json into ~/kaonomi/piper/voices. The takes whose score is low can be
            left out first (takes.json). 30 to 60 minutes of voice give a good result.
            """);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _recorder.Recorder.SamplesHeard -= OnSamples;
        _watch?.Dispose();
        _whisper.Dispose();
    }
}
