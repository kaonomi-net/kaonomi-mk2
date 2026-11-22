using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A note of a video as it is now (its last version), for the list of the player.</summary>
public sealed class VideoNoteViewModel(VideoNoteDocument document, int versions)
{
    public VideoNoteDocument Document { get; } = document;

    public int Versions { get; } = versions;

    public string Kind => Document.Kind;

    public bool IsAnalysis => Kind is "segment" or "frame" or "sound";

    public bool IsPoint => Kind is "frame" or "picture" || Document.End - Document.Start < 0.05;

    public string Icon => IconOf(Kind);

    public string KindLabel => Kind switch
    {
        "segment" => "SEGMENT", "frame" => "FRAME", "sound" => "SOUND", "picture" => "PICTURE", _ => "NOTE"
    };

    public string Range => IsPoint ? VideoMedia.Clock(Document.Start) : $"{VideoMedia.Clock(Document.Start)} → {VideoMedia.Clock(Document.End)}";

    public string Footer
    {
        get
        {
            var parts = new List<string>();
            if (Document.Model.Length > 0)
                parts.Add(Document.Model);
            parts.Add(Document.Date.ToLocalTime().ToString("dd MMM HH:mm"));
            if (Document.DurationMs > 0)
                parts.Add($"{Document.DurationMs / 1000.0:0.0} s");
            if (Document.Operation is "edit" or "rerun" or "restore")
                parts.Add(Document.Operation switch { "edit" => "edited", "rerun" => "run again", _ => "restored" });
            if (!string.IsNullOrWhiteSpace(Document.ProjectId))
                parts.Add("🎯 " + ProjectCatalog.NameOf(Document.ProjectId));
            return string.Join(" · ", parts);
        }
    }

    public static string IconOf(string kind) => kind switch
    {
        "segment" => "🎬", "frame" => "🖼", "sound" => "🎧", "picture" => "📸", _ => "✎"
    };
}

/// <summary>
/// The player of a video of /video: the video and its sound, the position, a selection (a part of the video, from IN
/// to OUT), and what is done on it, each kept in the search engine as a versioned note (index <c>videonote</c>):
/// 🎬 ANALYSE SEGMENT (a sheet of pictures across the selection and what is said in it, read by the video model),
/// 🖼 ANALYSE FRAME (the picture shown, read by the image model), 🎧 ANALYSE SOUND (what is said in the selection,
/// who speaks among the voices enrolled in /recorder, the loudness and the silences, read by the audio model),
/// ✎ ANNOTATE (a text of the user on the selection or the position) and 📸 KEEP FRAME (the picture saved in
/// ~/Pictures/kaonomi/video). A question typed asks the analyses something precise. Each note can be edited, run
/// again, restored from a former version or deleted: each of these writes a new version, nothing is written over
/// nor erased. The models are the ones of /models (video, image, audio).
/// </summary>
public sealed partial class VideoPlayerWindowViewModel : ObservableObject, IDisposable, IProjectDocument
{
    private readonly ChatSettings? _settings;
    private readonly KaonomiAssistantService? _assistant;
    private readonly OllamaModelService? _ollama;
    private readonly CancellationTokenSource _closing = new();
    private SpeechRecognitionService? _speech;
    private List<VideoNoteDocument> _versions = [];

    public VideoPlayerWindowViewModel() : this(new VideoEntry(), null, null, null)
    {
    }

    public VideoPlayerWindowViewModel(VideoEntry entry, ChatSettings? settings, KaonomiAssistantService? assistant, OllamaModelService? ollama)
    {
        Entry = entry;
        _settings = settings;
        _assistant = assistant;
        _ollama = ollama;
        Key = VideoMedia.KeyOf(entry.Path, entry.Size);
        Probe = entry.Probe ?? new VideoProbe();
        if (settings is not null && entry.Path.Length > 0)
            _ = PrepareAsync();
    }

    public VideoEntry Entry { get; }

    public string Path => Entry.Path;

    public string Name => System.IO.Path.GetFileName(Entry.Path);

    public string Key { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Duration), nameof(InfoLabel), nameof(TimeLabel))]
    [NotifyCanExecuteChangedFor(nameof(AnalyseSoundCommand))]
    private VideoProbe _probe;

    public double Duration => Probe.Duration;

    public string InfoLabel => $"{VideoMedia.Length(Duration)} · {Probe.Width}×{Probe.Height} · {Probe.Fps:0.#} fps · {VideoMedia.Size(Entry.Size)}"
        + (Probe.HasAudio ? string.Empty : " · no sound") + (Probe.Backend == "ffmpeg" ? " · read by ffmpeg" : string.Empty);

    /// <summary>The position of the player, in seconds (set by the player).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLabel))]
    private double _position;

    [ObservableProperty]
    private bool _isPlaying;

    public string TimeLabel => $"{VideoMedia.Clock(Position)} / {VideoMedia.Clock(Duration)}";

    /// <summary>Where the selection starts (null: none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionLabel))]
    [NotifyCanExecuteChangedFor(nameof(AnalyseSegmentCommand), nameof(ClearSelectionCommand))]
    private double? _selectionStart;

    /// <summary>Where the selection ends (null: none).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectionLabel))]
    [NotifyCanExecuteChangedFor(nameof(AnalyseSegmentCommand), nameof(ClearSelectionCommand))]
    private double? _selectionEnd;

    public bool HasSelection => SelectionStart is { } start && SelectionEnd is { } end && end - start >= 0.2;

    public string SelectionLabel => HasSelection
        ? $"{VideoMedia.Clock(SelectionStart!.Value)} → {VideoMedia.Clock(SelectionEnd!.Value)}  ({VideoMedia.Length(SelectionEnd.Value - SelectionStart.Value)})"
        : SelectionStart is { } start ? $"IN {VideoMedia.Clock(start)} · O sets the end"
        : SelectionEnd is { } end ? $"OUT {VideoMedia.Clock(end)} · I sets the start"
        : "no selection: drag on the timeline, or I (start) and O (end)";

    /// <summary>
    /// The project the video is attached to (chosen by its name in the header of the window), or null: every new note
    /// takes it; changing it attaches the notes of the video to the new one (a new version of each, the others kept).
    /// </summary>
    [ObservableProperty]
    private string? _projectId;

    /// <summary>The player always holds a video.</summary>
    public bool HasProjectDocument => true;

    // True while the project is read from the notes (not a choice of the user).
    private bool _readingProject;

    partial void OnProjectIdChanged(string? value)
    {
        if (!_readingProject)
            _ = AttachToProjectAsync(value);
    }

    /// <summary>The notes as they are now take the project chosen: a version of each that is not in it yet.</summary>
    private async Task AttachToProjectAsync(string? project)
    {
        var notes = AllNotes.Where(n => n.Document.ProjectId != project).ToList();
        var name = string.IsNullOrWhiteSpace(project) ? "no project" : $"the project {ProjectCatalog.NameOf(project)}";
        if (notes.Count == 0)
        {
            Report($"The video is attached to {name}: the notes written now go with it");
            return;
        }
        try
        {
            foreach (var note in notes)
            {
                var next = note.Document.Next("project", NextVersion(note.Document.NoteId));
                next.ProjectId = string.IsNullOrWhiteSpace(project) ? null : project;
                await SaveAsync(next);
            }
            Report($"✓ The video and its {notes.Count} note(s) are attached to {name} (a new version of each; the former ones kept)");
            ActivityLog.Info("Video", $"{Name}: {notes.Count} note(s) attached to {name}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Report("The notes could not all be attached to the project: " + ex.Message, true);
        }
    }

    /// <summary>What the analyses are asked to look for (empty: a general analysis).</summary>
    [ObservableProperty]
    private string _question = string.Empty;

    /// <summary>The strip of pictures of the timeline (empty until it is made).</summary>
    [ObservableProperty]
    private string _stripPath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    /// <summary>What is being done now (empty: nothing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyCanExecuteChangedFor(nameof(AnalyseSegmentCommand), nameof(AnalyseFrameCommand), nameof(AnalyseSoundCommand), nameof(AnnotateCommand), nameof(KeepFrameCommand))]
    private string _working = string.Empty;

    public bool IsWorking => Working.Length > 0;

    /// <summary>The kind of notes shown: all, segment, frame, sound, picture, annotation.</summary>
    [ObservableProperty]
    private string _filter = "all";

    partial void OnFilterChanged(string value) => ShowNotes();

    /// <summary>The notes as they are now (the deleted ones aside), newest first, the filter applied.</summary>
    public ObservableCollection<VideoNoteViewModel> Notes { get; } = [];

    /// <summary>Every note as it is now (the deleted ones aside), whatever the filter: the marks of the timeline.</summary>
    public IReadOnlyList<VideoNoteViewModel> AllNotes { get; private set; } = [];

    [ObservableProperty]
    private string _notesLabel = "NOTES";

    /// <summary>Raised when the notes changed (the timeline draws their marks again).</summary>
    public event Action? NotesChanged;

    /// <summary>Asks the player to go to a time.</summary>
    public event Action<double>? SeekRequested;

    /// <summary>Asks for a text (title, explanation, text at first; null when cancelled): set by the view.</summary>
    public Func<string, string, string, Task<string?>> AskAsync { get; set; } = (_, _, _) => Task.FromResult<string?>(null);

    /// <summary>Asks for a confirmation: set by the view.</summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (_, _) => Task.FromResult(false);

    private void Report(string status, bool isError = false)
    {
        Status = status;
        IsError = isError;
    }

    /// <summary>Says something in the status of the window (the player: a failure, the end of the video).</summary>
    public void Tell(string status, bool isError = false) => Report(status, isError);

    private string LanguageName => (_settings?.Language ?? "FR").ToUpperInvariant() switch
    {
        "FR" => "French", "EN" => "English", "DE" => "German", "ES" => "Spanish", "IT" => "Italian", "PT" => "Portuguese", "NL" => "Dutch", var other => other
    };

    // ── At the start ──

    private async Task PrepareAsync()
    {
        try
        {
            if (Probe.Duration <= 0)
                Probe = await VideoMedia.ProbeAsync(Path, _closing.Token);
            await ReloadAsync();
            StripPath = await VideoMedia.StripAsync(Path, Key, 60, 72, _closing.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            Report("The video could not be prepared: " + ex.Message, true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Reads the notes of the video in the search engine again.</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_assistant is null)
            return;
        try
        {
            _versions = [.. await _assistant.VideoNotesAsync(Key, _closing.Token)];
            ShowNotes();
            // The project of the video: the one of its newest note.
            _readingProject = true;
            try
            {
                ProjectId = AllNotes.OrderByDescending(n => n.Document.Date).FirstOrDefault()?.Document.ProjectId;
            }
            finally
            {
                _readingProject = false;
            }
            if (Status.Length == 0)
                Report(AllNotes.Count == 0
                    ? "Space: play · drag on the timeline to select a part · I / O: in and out · then analyse it, or annotate it"
                    : $"{AllNotes.Count} note(s) on this video");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Report("The notes could not be read from the search engine: " + ex.Message, true);
        }
    }

    private void ShowNotes()
    {
        var current = _versions.GroupBy(v => v.NoteId)
            .Select(g => (Last: g.OrderByDescending(v => v.Version).First(), Count: g.Count()))
            .Where(n => !n.Last.IsDeleted)
            .Select(n => new VideoNoteViewModel(n.Last, n.Count))
            .OrderByDescending(n => n.Document.Date)
            .ToList();
        AllNotes = current;
        Notes.Clear();
        foreach (var note in current.Where(n => Filter == "all" || n.Kind == Filter))
            Notes.Add(note);
        NotesLabel = $"NOTES · {current.Count}";
        NotesChanged?.Invoke();
    }

    /// <summary>The versions of a note, the newest first.</summary>
    public IReadOnlyList<VideoNoteDocument> VersionsOf(VideoNoteViewModel note) =>
        _versions.Where(v => v.NoteId == note.Document.NoteId).OrderByDescending(v => v.Version).ToList();

    // ── The selection ──

    /// <summary>Selects a part of the video (the ends put in order and kept in the video).</summary>
    public void Select(double start, double end)
    {
        var (from, to) = start <= end ? (start, end) : (end, start);
        SelectionStart = Math.Clamp(from, 0, Duration);
        SelectionEnd = Math.Clamp(to, 0, Duration);
    }

    /// <summary>I: the selection starts at the position (an end before it is dropped).</summary>
    [RelayCommand]
    private void SetIn()
    {
        var at = Math.Clamp(Position, 0, Duration);
        if (SelectionEnd is { } end && end <= at + 0.05)
            SelectionEnd = null;
        SelectionStart = at;
    }

    /// <summary>O: the selection ends at the position (a start after it is dropped).</summary>
    [RelayCommand]
    private void SetOut()
    {
        var at = Math.Clamp(Position, 0, Duration);
        if (SelectionStart is { } start && start >= at - 0.05)
            SelectionStart = null;
        SelectionEnd = at;
    }

    [RelayCommand(CanExecute = nameof(HasSelectionOrStart))]
    private void ClearSelection()
    {
        SelectionStart = null;
        SelectionEnd = null;
    }

    private bool HasSelectionOrStart() => SelectionStart is not null || SelectionEnd is not null;

    /// <summary>Goes to a note: its time, and its part of the video selected.</summary>
    public void GoTo(VideoNoteViewModel note)
    {
        if (!note.IsPoint)
            Select(note.Document.Start, note.Document.End);
        SeekRequested?.Invoke(note.Document.Start);
    }

    public void Seek(double seconds) => SeekRequested?.Invoke(Math.Clamp(seconds, 0, Duration));

    // ── The analyses ──

    private bool CanWork() => !IsWorking && _assistant is not null;

    private bool CanAnalyseSegment() => CanWork() && HasSelection;

    private bool CanAnalyseSound() => CanWork() && Probe.HasAudio;

    private string ModelFor(string kind)
    {
        var modes = _settings?.Modes ?? new ModeModels();
        var model = kind switch { "segment" => modes.Video, "frame" => modes.Image, "sound" => modes.Audio, _ => string.Empty };
        if (model.Length > 0)
            return model;
        return kind == "sound" ? _settings?.Model ?? string.Empty : _settings?.VisionModel ?? _settings?.Model ?? string.Empty;
    }

    private string QuestionPart() =>
        Question.Trim().Length > 0 ? $"\n\nThe question of the user (answer it first): {Question.Trim()}" : string.Empty;

    /// <summary>The selection, or, without one, 10 seconds around the position.</summary>
    private (double Start, double End) Range() =>
        HasSelection ? (SelectionStart!.Value, SelectionEnd!.Value) : (Math.Max(0, Position - 5), Math.Min(Duration, Position + 5));

    [RelayCommand(CanExecute = nameof(CanAnalyseSegment))]
    private Task AnalyseSegmentAsync() => RunAsync(NewNote("segment", Range(), Question.Trim()), "create");

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task AnalyseFrameAsync() => RunAsync(NewNote("frame", (Position, Position), Question.Trim()), "create");

    [RelayCommand(CanExecute = nameof(CanAnalyseSound))]
    private Task AnalyseSoundAsync() => RunAsync(NewNote("sound", Range(), Question.Trim()), "create");

    private VideoNoteDocument NewNote(string kind, (double Start, double End) range, string question)
    {
        var noteId = Guid.NewGuid().ToString("N")[..16];
        return new VideoNoteDocument
        {
            NoteId = noteId, Version = 1, Id = VideoNoteDocument.IdOf(noteId, 1), Kind = kind, VideoKey = Key, VideoPath = Path, VideoName = Name,
            VideoDuration = Duration, Start = Math.Round(range.Start, 2), End = Math.Round(range.End, 2), Question = question,
            ProjectId = string.IsNullOrWhiteSpace(ProjectId) ? null : ProjectId
        };
    }

    /// <summary>Runs the analysis of <paramref name="note"/> (a new one, or a version of a note run again) and keeps it.</summary>
    private async Task RunAsync(VideoNoteDocument note, string operation)
    {
        if (_ollama is null)
            return;
        var what = note.Kind switch { "segment" => "the segment", "frame" => "the frame", _ => "the sound" };
        Working = $"Analysing {what} {(note.Kind == "frame" ? VideoMedia.Clock(note.Start) : $"{VideoMedia.Clock(note.Start)} → {VideoMedia.Clock(note.End)}")}…";
        Report(Working);
        var clock = Stopwatch.StartNew();
        var token = _closing.Token;
        try
        {
            note.Operation = operation;
            note.Model = ModelFor(note.Kind);
            note.Title = note.Kind switch
            {
                "segment" => $"Segment {VideoMedia.Clock(note.Start)} → {VideoMedia.Clock(note.End)}",
                "frame" => $"Frame {VideoMedia.Clock(note.Start)}",
                _ => $"Sound {VideoMedia.Clock(note.Start)} → {VideoMedia.Clock(note.End)}"
            };
            var question = note.Question.Length > 0 ? $"\n\nThe question of the user (answer it first): {note.Question}" : string.Empty;
            switch (note.Kind)
            {
                case "frame":
                {
                    var picture = await VideoMedia.FrameAsync(Path, note.Start, 1280, true, token);
                    var (system, version) = PromptOf("video.frame");
                    note.PromptVersion = version;
                    note.Text = await _ollama.ChatWithImagesAsync(note.Model, system,
                        $"The picture at {VideoMedia.Clock(note.Start)} of the video « {Name} » ({VideoMedia.Length(Duration)}).{question}", [picture], token);
                    note.Picture = Convert.ToBase64String(await VideoMedia.FrameAsync(Path, note.Start, 640, true, token));
                    break;
                }
                case "segment":
                {
                    var length = note.End - note.Start;
                    var count = length <= 8 ? 4 : length <= 90 ? 6 : 9;
                    Working = $"Analysing the segment: the pictures…";
                    var sheet = await VideoMedia.SheetAsync(Path, note.Start, note.End, count, 400, token);
                    note.Transcript = await TranscriptAsync(note.Start, note.End, token);
                    Working = $"Analysing the segment: the model reads {count} pictures…";
                    var (system, version) = PromptOf("video.segment");
                    note.PromptVersion = version;
                    var said = note.Transcript.Length > 0 ? note.Transcript : Probe.HasAudio ? "(nothing heard, or the speech recognition is not installed)" : "(the video has no sound)";
                    note.Text = await _ollama.ChatWithImagesAsync(note.Model, system,
                        $"The segment {VideoMedia.Clock(note.Start)} → {VideoMedia.Clock(note.End)} ({VideoMedia.Length(length)}) of the video « {Name} ». "
                        + $"The picture is a sheet of {count} pictures in the order of the time, each numbered with its time.\n\nWhat is said in the segment:\n{said}{question}",
                        [sheet], token);
                    note.Picture = Convert.ToBase64String(sheet);
                    break;
                }
                default:
                {
                    Working = "Analysing the sound: what is said…";
                    var samples = await VideoMedia.AudioAsync(Path, note.Start, note.End, token);
                    note.Transcript = await TranscribeAsync(samples, note.Start, token);
                    Working = "Analysing the sound: who speaks…";
                    note.Speakers = await SpeakersAsync(samples, note.Start, token);
                    var measures = Measures(samples, note.Start);
                    Working = "Analysing the sound: the model reads it…";
                    var (system, version) = PromptOf("video.sound");
                    note.PromptVersion = version;
                    var text = new StringBuilder($"The sound of the segment {VideoMedia.Clock(note.Start)} → {VideoMedia.Clock(note.End)} ({VideoMedia.Length(note.End - note.Start)}) of the video « {Name} ».\n\n");
                    text.Append("What is said:\n").Append(note.Transcript.Length > 0 ? note.Transcript : "(nothing understood)").Append("\n\n");
                    text.Append("Who speaks:\n").Append(note.Speakers.Count > 0 ? string.Join("\n", note.Speakers) : "(no voice recognised: nobody enrolled in /recorder, or unknown voices)").Append("\n\n");
                    text.Append("Measures of the sound: ").Append(measures).Append(question);
                    note.Text = await _ollama.ChatAsync(note.Model, system, text.ToString(), token);
                    note.Comment = measures;
                    break;
                }
            }
            note.Text = LatexText.Convert(note.Text).Trim();
            note.DurationMs = clock.ElapsedMilliseconds;
            note.Date = DateTime.UtcNow;
            await SaveAsync(note);
            Report($"✓ {note.Title} analysed in {clock.Elapsed.TotalSeconds:0.0} s by {note.Model} · kept in the search engine (v{note.Version})");
            ActivityLog.Info("Video", $"{note.Title} of {Name}: {operation}, v{note.Version}, {clock.ElapsedMilliseconds} ms, {note.Model}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or TimeoutException or IOException or FileNotFoundException)
        {
            Report($"The analysis of {what} failed: {ex.Message}", true);
            ActivityLog.Error("Video", $"The analysis of {what} of {Name} failed", ex);
        }
        finally
        {
            Working = string.Empty;
        }
    }

    /// <summary>The prompt of /prompts for <paramref name="key"/>, in the language of the chat, and its version.</summary>
    private (string System, int Version) PromptOf(string key) => (Prompts.Get(key, ("language", LanguageName)), Prompts.CurrentVersion(key));

    /// <summary>What is said between <paramref name="start"/> and <paramref name="end"/> (empty without sound, without the model, or above 15 minutes).</summary>
    private async Task<string> TranscriptAsync(double start, double end, CancellationToken token)
    {
        if (!Probe.HasAudio || end - start > 900 || _settings is null || !File.Exists(_settings.SpeechModel))
            return string.Empty;
        Working = "Analysing the segment: what is said…";
        return await TranscribeAsync(await VideoMedia.AudioAsync(Path, start, end, token), start, token);
    }

    /// <summary>
    /// What is said in <paramref name="samples"/> (16 kHz mono, starting at <paramref name="start"/> s of the video),
    /// each piece with its time: "[00:12.0] …". The sound is cut in pieces of 20 s at most, at their most silent
    /// moment, and the silent pieces are left out: Whisper, given a whole segment, sometimes stopped after a pause.
    /// </summary>
    private async Task<string> TranscribeAsync(float[] samples, double start, CancellationToken token)
    {
        if (_settings is null || samples.Length < 16000 / 2)
            return string.Empty;
        if (!File.Exists(_settings.SpeechModel))
            throw new FileNotFoundException($"The model of the speech recognition is missing: {_settings.SpeechModel}");
        _speech ??= new SpeechRecognitionService(_settings.SpeechModel);
        var lines = new List<string>();
        foreach (var (from, to) in Pieces(samples))
        {
            token.ThrowIfCancellationRequested();
            var piece = samples[from..to];
            if (SpeakerRecognition.Level(piece) < 0.0006)
                continue;
            var text = (await _speech.TranscribeAsync(piece, _settings.Language.ToLowerInvariant(), token, log: false)).Trim();
            if (text.Length > 0)
                lines.Add($"[{VideoMedia.Clock(start + from / 16000.0)}] {text}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The pieces of the sound to transcribe: cut in the middle of each silence of 0.3 s or more (a sentence each, more
    /// or less), the pieces under 2 s joined to the one before, and a piece above 20 s cut again at its quietest 50 ms.
    /// </summary>
    private static List<(int From, int To)> Pieces(float[] samples)
    {
        const int rate = 16000, step = rate / 20, shortest = 2 * rate, longest = 20 * rate;
        const double silence = 0.008;
        // The silences: runs of 6 frames of 50 ms or more under the level of silence; a cut in the middle of each.
        var cuts = new List<int> { 0 };
        var run = 0;
        for (var at = 0; at + step <= samples.Length; at += step)
        {
            double energy = 0;
            for (var i = at; i < at + step; i++)
                energy += samples[i] * samples[i];
            if (Math.Sqrt(energy / step) < silence)
                run++;
            else
            {
                if (run >= 6 && at - run * step / 2 > cuts[^1] + rate / 2)
                    cuts.Add(at - run * step / 2);
                run = 0;
            }
        }
        cuts.Add(samples.Length);
        var pieces = new List<(int From, int To)>();
        for (var i = 0; i + 1 < cuts.Count; i++)
        {
            var (from, to) = (cuts[i], cuts[i + 1]);
            if (pieces.Count > 0 && (to - from < shortest || pieces[^1].To - pieces[^1].From < shortest) && to - pieces[^1].From <= longest)
                pieces[^1] = (pieces[^1].From, to);
            else
                pieces.Add((from, to));
        }
        // A piece still too long: cut again at its quietest moment between 8 and 20 s.
        var result = new List<(int, int)>();
        foreach (var (from, to) in pieces)
        {
            var at = from;
            while (to - at > longest)
            {
                var (cut, quietest) = (at + longest, double.MaxValue);
                for (var i = at + 8 * rate; i + step <= at + longest; i += step)
                {
                    double energy = 0;
                    for (var j = i; j < i + step; j++)
                        energy += samples[j] * samples[j];
                    if (energy < quietest)
                        (cut, quietest) = (i + step / 2, energy);
                }
                result.Add((at, cut));
                at = cut;
            }
            if (to - at > rate / 4)
                result.Add((at, to));
        }
        return result;
    }

    /// <summary>Who speaks, window of 3 s by window, among the voices enrolled in /recorder: "00:12.0 → 00:18.0  Jerome".</summary>
    private static async Task<List<string>> SpeakersAsync(float[] samples, double start, CancellationToken token)
    {
        if (!File.Exists(SpeakerRecognition.ModelPath))
            return [];
        var speakers = new SpeakerRecognition();
        await speakers.LoadAsync(_ => { }, token);
        if (speakers.Counts.Count == 0)
            return [];
        return await Task.Run(() =>
        {
            const int rate = 16000, window = 3 * rate;
            var turns = new List<(double From, double To, string Who)>();
            for (var at = 0; at + rate < samples.Length; at += window)
            {
                token.ThrowIfCancellationRequested();
                var part = samples.AsSpan(at, Math.Min(window, samples.Length - at));
                if (SpeakerRecognition.Level(part) < 0.0006 || speakers.Embed(part.ToArray(), rate) is not { } embedding)
                    continue;
                var who = speakers.Identify(embedding, 0.5).Name ?? "unknown voice";
                var (from, to) = (start + at / (double)rate, start + (at + part.Length) / (double)rate);
                if (turns.Count > 0 && turns[^1].Who == who && from - turns[^1].To < 0.5)
                    turns[^1] = (turns[^1].From, to, who);
                else
                    turns.Add((from, to, who));
            }
            return turns.Select(t => $"{VideoMedia.Clock(t.From)} → {VideoMedia.Clock(t.To)}  {t.Who}").ToList();
        }, token);
    }

    /// <summary>The loudness of the sound, its peak, its silences and its loudest moment.</summary>
    private static string Measures(float[] samples, double start)
    {
        if (samples.Length == 0)
            return "no sound";
        const int rate = 16000, step = rate / 20;
        double sum = 0, peak = 0, loudest = 0, loudestAt = 0;
        var silent = 0;
        var parts = 0;
        for (var at = 0; at < samples.Length; at += step)
        {
            double part = 0;
            var count = Math.Min(step, samples.Length - at);
            for (var i = at; i < at + count; i++)
            {
                var value = Math.Abs(samples[i]);
                part += value * value;
                peak = Math.Max(peak, value);
            }
            sum += part;
            var rms = Math.Sqrt(part / Math.Max(1, count));
            if (rms < 0.006)
                silent++;
            if (rms > loudest)
                (loudest, loudestAt) = (rms, start + at / (double)rate);
            parts++;
        }
        static string Db(double value) => value <= 0 ? "-∞ dB" : $"{20 * Math.Log10(value):0} dB";
        return $"loudness {Db(Math.Sqrt(sum / samples.Length))}, peak {Db(peak)}, silence {100.0 * silent / Math.Max(1, parts):0} % of the time, loudest at {VideoMedia.Clock(loudestAt)}";
    }

    // ── The annotations and the pictures kept ──

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task AnnotateAsync()
    {
        var range = HasSelection ? (SelectionStart!.Value, SelectionEnd!.Value) : (Position, Position);
        var where = HasSelection ? $"{VideoMedia.Clock(range.Item1)} → {VideoMedia.Clock(range.Item2)}" : VideoMedia.Clock(Position);
        var text = await AskAsync("Annotate", $"Your note on {where} of « {Name} » (Markdown). It is kept in the search engine, and every change is a new version.", string.Empty);
        if (string.IsNullOrWhiteSpace(text))
            return;
        var note = NewNote("annotation", range, string.Empty);
        note.Operation = "create";
        note.Text = text.Trim();
        note.Title = FirstLine(note.Text);
        await SaveAsync(note);
        Report($"✓ Note kept on {where} (v1)");
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task KeepFrameAsync()
    {
        Working = "Keeping the picture…";
        try
        {
            var at = Position;
            var full = await VideoMedia.FrameAsync(Path, at, 0, true, _closing.Token);
            Directory.CreateDirectory(VideoMedia.PicturesDirectory);
            var file = System.IO.Path.Combine(VideoMedia.PicturesDirectory,
                $"{System.IO.Path.GetFileNameWithoutExtension(Name)}-{VideoMedia.Clock(at).Replace(':', '.')}-{DateTime.Now:HHmmss}.jpg");
            await File.WriteAllBytesAsync(file, full, _closing.Token);
            var note = NewNote("picture", (at, at), string.Empty);
            note.Operation = "create";
            note.Title = $"Picture {VideoMedia.Clock(at)}";
            note.PictureFile = file;
            note.Text = $"Picture at {VideoMedia.Clock(at)} saved in `{file}`.";
            note.Picture = Convert.ToBase64String(await VideoMedia.FrameAsync(Path, at, 640, true, _closing.Token));
            await SaveAsync(note);
            Report($"✓ Picture saved: {file}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Report("The picture could not be kept: " + ex.Message, true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Working = string.Empty;
        }
    }

    // ── A part of the frame ──

    /// <summary>
    /// Crops a picture (JPEG or PNG bytes) to a part of it given in fractions of its width and height (x, y, width,
    /// height), made at most <c>maxWidth</c> wide (0: as it is), as PNG ("png") or JPEG ("jpg"): set by the view.
    /// </summary>
    public Func<byte[], (double X, double Y, double Width, double Height), int, string, byte[]?>? Crop { get; set; }

    /// <summary>A picture (its file) to open in the image editor: the window of /pictures.</summary>
    public event Action<string>? ImageEditorRequested;

    /// <summary>The image editor of a picture, as /pictures makes it (the vision model of the chat).</summary>
    public ImageAnalysisViewModel CreateEditor(string path) =>
        new(path, _assistant, _ollama, _settings?.VisionModel ?? ChatSettings.DefaultVisionModel);

    /// <summary>
    /// The file of the picture of a note: the one saved (a capture, a picture kept), or, for a picture kept only in the
    /// note (a frame or a segment analysed), the picture written once in ~/Pictures/kaonomi/video. Null without picture.
    /// </summary>
    public async Task<string?> PictureFileOf(VideoNoteViewModel note)
    {
        if (note.Document.PictureFile.Length > 0 && File.Exists(note.Document.PictureFile))
            return note.Document.PictureFile;
        if (note.Document.Picture.Length == 0)
            return null;
        try
        {
            Directory.CreateDirectory(VideoMedia.PicturesDirectory);
            var file = System.IO.Path.Combine(VideoMedia.PicturesDirectory,
                $"{System.IO.Path.GetFileNameWithoutExtension(Name)}-{VideoMedia.Clock(note.Document.Start).Replace(':', '.')}-{note.Kind}-{note.Document.NoteId[..8]}-v{note.Document.Version}.jpg");
            if (!File.Exists(file))
                await File.WriteAllBytesAsync(file, Convert.FromBase64String(note.Document.Picture));
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Report("The picture of the note could not be written: " + ex.Message, true);
            return null;
        }
    }

    /// <summary>Opens the picture of a note in the image editor of /pictures.</summary>
    public async Task EditPictureAsync(VideoNoteViewModel note)
    {
        if (await PictureFileOf(note) is { } file)
            ImageEditorRequested?.Invoke(file);
    }

    /// <summary>
    /// ✂ CAPTURE: the part <paramref name="part"/> (fractions of the picture) of the frame at <paramref name="at"/>
    /// seconds, taken from the full picture of the video: saved in ~/Pictures/kaonomi/video (PNG), kept as a note
    /// (a picture, versioned in the search engine) and opened in the image editor.
    /// </summary>
    public async Task CaptureAsync(double at, (double X, double Y, double Width, double Height) part)
    {
        if (Crop is null || IsWorking || part.Width <= 0.002 || part.Height <= 0.002)
            return;
        Working = "Capturing the part of the picture…";
        try
        {
            var full = await VideoMedia.FrameAsync(Path, at, 0, true, _closing.Token);
            var png = Crop(full, part, 0, "png") ?? throw new InvalidOperationException("the picture could not be cropped");
            Directory.CreateDirectory(VideoMedia.PicturesDirectory);
            var file = System.IO.Path.Combine(VideoMedia.PicturesDirectory,
                $"{System.IO.Path.GetFileNameWithoutExtension(Name)}-{VideoMedia.Clock(at).Replace(':', '.')}-capture-{DateTime.Now:HHmmss}.png");
            await File.WriteAllBytesAsync(file, png, _closing.Token);
            var width = (int)Math.Round(part.Width * Probe.Width);
            var height = (int)Math.Round(part.Height * Probe.Height);
            var note = NewNote("picture", (at, at), string.Empty);
            note.Operation = "create";
            note.Title = $"Capture {VideoMedia.Clock(at)} · {width}×{height}";
            note.PictureFile = file;
            note.Text = $"Part of the picture at {VideoMedia.Clock(at)} ({width}×{height}, from {part.X * 100:0}% {part.Y * 100:0}%) saved in `{file}` and opened in the image editor.";
            note.Picture = Convert.ToBase64String(Crop(full, part, 640, "jpg") ?? png);
            await SaveAsync(note);
            Report($"✓ Captured {width}×{height} at {VideoMedia.Clock(at)}: {file} · opened in the image editor");
            ImageEditorRequested?.Invoke(file);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Report("The part of the picture could not be captured: " + ex.Message, true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Working = string.Empty;
        }
    }

    private static string FirstLine(string text)
    {
        // The first line without its Markdown (titles, bold, italics, code, list marks).
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Note";
        var line = System.Text.RegularExpressions.Regex.Replace(first, @"(\*\*|__|`|~~)", string.Empty).Trim().TrimStart('#', ' ', '*', '-', '>', '_').Trim();
        if (line.Length == 0)
            line = "Note";
        return line.Length > 70 ? line[..70] + "…" : line;
    }

    // ── The versions ──

    private int NextVersion(string noteId) => _versions.Where(v => v.NoteId == noteId).Select(v => v.Version).DefaultIfEmpty(0).Max() + 1;

    /// <summary>Writes a version in the search engine and shows it.</summary>
    private async Task SaveAsync(VideoNoteDocument note)
    {
        if (_assistant is null)
            return;
        var result = await _assistant.SaveVideoNoteAsync(note, _closing.Token);
        if (!result.IsSuccess)
            throw new InvalidOperationException("the search engine refused the note: " + result.Message);
        _versions.Add(note);
        ShowNotes();
        Saved?.Invoke();
    }

    /// <summary>A version was kept (the library counts the notes again).</summary>
    public event Action? Saved;

    /// <summary>Edits the text of a note: a new version.</summary>
    public async Task EditAsync(VideoNoteViewModel note)
    {
        var text = await AskAsync($"Edit · {note.Document.Title}", "The text of the note (Markdown). Saving writes a new version; the former ones are kept.", note.Document.Text);
        if (text is null || text.Trim() == note.Document.Text.Trim())
            return;
        var next = note.Document.Next("edit", NextVersion(note.Document.NoteId));
        next.Text = text.Trim();
        if (next.Kind == "annotation")
            next.Title = FirstLine(next.Text);
        await Guarded(() => SaveAsync(next), $"✓ {next.Title}: v{next.Version} kept");
    }

    /// <summary>Runs the analysis of a note again (the same part of the video, the same question): a new version.</summary>
    public Task RerunAsync(VideoNoteViewModel note)
    {
        if (!note.IsAnalysis || IsWorking)
            return Task.CompletedTask;
        var next = note.Document.Next("rerun", NextVersion(note.Document.NoteId));
        next.Text = string.Empty;
        next.Transcript = string.Empty;
        next.Speakers = [];
        return RunAsync(next, "rerun");
    }

    /// <summary>Deletes a note (once confirmed): a version that hides it; its versions are kept.</summary>
    public async Task DeleteAsync(VideoNoteViewModel note)
    {
        if (!await ConfirmAsync("Delete a note", $"Delete « {note.Document.Title} »? It leaves the list; its {note.Versions} version(s) stay in the search engine (a version that deletes it is written)."))
            return;
        var next = note.Document.Next("delete", NextVersion(note.Document.NoteId));
        next.IsDeleted = true;
        await Guarded(() => SaveAsync(next), $"✓ {next.Title} deleted (v{next.Version}: its history is kept)");
    }

    /// <summary>Takes a former version of a note again: a new version, a copy of it.</summary>
    public async Task RestoreAsync(VideoNoteDocument version)
    {
        var next = version.Next("restore", NextVersion(version.NoteId));
        await Guarded(() => SaveAsync(next), $"✓ {next.Title}: the version {version.Version} is the note again (v{next.Version})");
    }

    private async Task Guarded(Func<Task> action, string done)
    {
        try
        {
            await action();
            Report(done);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Report("Not kept: " + ex.Message, true);
        }
    }

    public void Dispose()
    {
        _closing.Cancel();
        _closing.Dispose();
        _speech?.Dispose();
    }
}
