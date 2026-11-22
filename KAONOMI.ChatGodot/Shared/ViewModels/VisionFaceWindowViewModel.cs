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

/// <summary>
/// /vision: the face recognition, the facial expression recognition and the
/// object detection (YOLOv8, <see cref="Objects"/>) of KAONOMI.VisionOpenCV, on the same frames of one camera.
/// <para>
/// The faces: the face recognition and the facial expression recognition of KAONOMI.VisionOpenCV (Python and
/// OpenCV, with three open source models of the OpenCV Zoo: YuNet, SFace, MobileFaceNet) in a window of the desktop
/// (<see cref="VisionStreamViewModel"/>). Everything runs on this machine. A click on the box of a face asks its name
/// (<see cref="Hold"/>, then <see cref="EnrolHeld"/>: that face is kept under the name, in
/// <c>~/kaonomi/vision/faces/&lt;name&gt;</c>; several pictures per name make the recognition steadier) or forgets the
/// name it is recognised as (<see cref="ForgetAsync"/>: the pictures of the name are deleted, once confirmed). The
/// window also asks the smallest similarity to recognise a face, the expressions, the landmarks, the movement (the
/// centre of each face, the arrow of its movement, its yellow trail fading in 3 seconds), the pause, a picture saved. The models are in <c>~/kaonomi/models/face</c> (downloaded when missing).
/// </para>
/// <para>
/// The faces enrolled are also kept in the search engine of the server (one <see cref="FaceDocument"/> per picture:
/// the name, the picture, what the recognition model says of it), and both are kept alike. Before the script starts,
/// the pictures of the search engine missing in the folder are written there (the faces enrolled on another machine,
/// or a folder lost, come back); when it starts, the pictures of the folder missing in the search engine are written
/// there; a face enrolled is written at once; a name forgotten is erased from the search engine too, for good.
/// Without the server, the folder alone is used, and the search engine catches up at the next start.
/// </para>
/// <para>
/// What is seen of the faces is recorded there too (RECORD): the script follows each face and sums it up every
/// second (who, the share of each emotion in per cent, the area of its rectangle and its movement: min, max, mean,
/// standard deviation); it sends episodes (<c>faceepisode</c>: a period where the state of a face stays the same,
/// written again every second while it lasts) and, at each change, the observation of that second
/// (<c>faceobservation</c>), which this window writes as they come.
/// </para>
/// <para>
/// The objects: YOLOv8 on the same frames (nano to extra large, in
/// <c>~/kaonomi/models/yolo</c>, downloaded when missing), the smallest confidence, the persons only or every object,
/// the centre, trail and direction of each person, the alerts (two persons overlapping, an object of a class), the
/// detections as JSON lines in <c>~/kaonomi/vision/yolo</c>, and every minute everything detected written in the
/// search engine (RECORD OBJECTS, index <c>visionobjects</c>).
/// </para>
/// </summary>
public sealed partial class VisionFaceWindowViewModel(KaonomiAssistantService? assistant = null) : VisionStreamViewModel("face"), ISingleWindow
{
    /// <summary>
    /// The voice, under the picture: what the microphone hears (its spectrum, its pitch, its formants) and whose voice
    /// it is, as in /recorder (the same voices enrolled). Null: the window has no voice part. The view opens the
    /// microphone once it is shown and closes it with the window.
    /// </summary>
    public RecorderWindowViewModel? Voice { get; set; }

    /// <summary>A face of the last frame: who it is (or "unknown"), how sure, and its box in the pixels of the frame.</summary>
    public sealed record SeenFace(string Name, bool Known, double Score, int X, int Y, int Width, int Height)
    {
        public bool Holds(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    }

    /// <summary>The most faces read from the search engine.</summary>
    private const int MaxFaces = 2000;

    /// <summary>The ids of the documents the search engine holds, as last read or written; null when it was not reached.</summary>
    private HashSet<string>? _stored;

    /// <summary>What the search engine holds of the faces: how many, or why not.</summary>
    [ObservableProperty]
    private string _serverLabel = string.Empty;

    /// <summary>
    /// What is seen of the faces is recorded in the search engine: who, the share of each emotion, the area of the
    /// face and its movement, as episodes (a period where the state of a face stays the same, written again every
    /// second) and observations (the second where something changed).
    /// </summary>
    [ObservableProperty]
    private bool _record = true;

    /// <summary>What was recorded since the window opened, or why it is not.</summary>
    [ObservableProperty]
    private string _recordLabel = string.Empty;

    private readonly HashSet<string> _episodesWritten = [];
    private int _changesWritten;
    private Task _writes = Task.CompletedTask;

    partial void OnRecordChanged(bool value)
    {
        Send($"record {(value ? 1 : 0)}");
        if (!value)
            RecordLabel = "not recorded";
    }

    /// <summary>The folder of the faces enrolled: one folder per name.</summary>
    public static string FacesDirectory => Path.Combine(ChatSettings.KaonomiHome, "vision", "faces");

    public static string ModelsDirectory => Path.Combine(ChatSettings.KaonomiHome, "models", "face");

    private volatile IReadOnlyList<SeenFace> _people = [];
    private volatile IReadOnlyList<string> _names = [];

    /// <summary>The faces of the last frame received, the largest first.</summary>
    public IReadOnlyList<SeenFace> People => _people;

    /// <summary>How long the user stays known once their face is lost (a head turned away, a few frames missed).</summary>
    public static readonly TimeSpan UserMemory = TimeSpan.FromSeconds(30);

    private sealed record Recognition(string Name, DateTime At);

    private volatile Recognition? _user;

    /// <summary>
    /// Who the user is: the person enrolled recognised in front of the camera (the largest face of the ones recognised:
    /// the nearest), still them for <see cref="UserMemory"/> after their face is lost; null when nobody enrolled was
    /// seen lately, or when the window does not run. The chat writes it in its system prompt.
    /// </summary>
    public string? RecognisedUser => IsRunning && _user is { } user && DateTime.UtcNow - user.At <= UserMemory ? user.Name : null;

    /// <summary>The names enrolled, as the last frame says them.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>The smallest similarity (cosine, in per cent) to recognise a face: 36 is the one of SFace.</summary>
    [ObservableProperty]
    private int _threshold = 36;

    [ObservableProperty]
    private bool _expressions = true;

    /// <summary>The 5 landmarks of each face (eyes, nose, corners of the mouth).</summary>
    [ObservableProperty]
    private bool _landmarks;

    /// <summary>
    /// The movement of each face: the centre of gravity of its box, an arrow from it in the direction it moves, a
    /// yellow trail of where it went, fading out in 3 seconds.
    /// </summary>
    [ObservableProperty]
    private bool _motion = true;

    /// <summary>
    /// MOVEMENT: the rectangle around what moves in the picture (the motion detection of /vision:move, just its
    /// rectangle). Away from the computer, the 3D animation follows it.
    /// </summary>
    [ObservableProperty]
    private bool _movement = true;

    /// <summary>Asks the user to confirm in a popup (title, question); set by the view. Without a view, nothing is confirmed.</summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (_, _) => Task.FromResult(false);

    // ── The objects (YOLOv8) ──

    public static readonly string[] Models = ["YOLOv8 nano (fast)", "YOLOv8 medium (more accurate)", "YOLOv8 large", "YOLOv8 extra large (the most accurate, the slowest)"];

    private static readonly string[] ModelSizes = ["n", "m", "l", "x"];

    /// <summary>The folder of the objects detected written as JSON lines.</summary>
    public static string LogDirectory => Path.Combine(ChatSettings.KaonomiHome, "vision", "yolo");

    /// <summary>The folder of the YOLO models (yolov8n.pt…).</summary>
    public static string YoloModelsDirectory => Path.Combine(ChatSettings.KaonomiHome, "models", "yolo");

    /// <summary>The objects are detected too (YOLOv8 on the same frames); off, only the faces.</summary>
    [ObservableProperty]
    private bool _objects = true;

    /// <summary>0: nano; 1: medium; 2: large; 3: extra large.</summary>
    [ObservableProperty]
    private int _model;

    /// <summary>The smallest confidence of an object kept, in per cent.</summary>
    [ObservableProperty]
    private int _confidence = 50;

    [ObservableProperty]
    private bool _personsOnly;

    /// <summary>The centre of each person and its coordinates.</summary>
    [ObservableProperty]
    private bool _centers = true;

    /// <summary>The trail of each person and the arrow of its direction.</summary>
    [ObservableProperty]
    private bool _directions = true;

    /// <summary>The alert when the boxes of two persons overlap.</summary>
    [ObservableProperty]
    private bool _overlap = true;

    /// <summary>The class of objects that raises an alert when one is seen (empty: none).</summary>
    [ObservableProperty]
    private string _alertClass = "wine glass";

    /// <summary>The objects detected written as JSON lines.</summary>
    [ObservableProperty]
    private bool _logJson;

    /// <summary>Every minute, everything detected is written in the search engine (index <c>visionobjects</c>).</summary>
    [ObservableProperty]
    private bool _recordObjects = true;

    /// <summary>What was recorded of the objects since the window opened, or why it is not.</summary>
    [ObservableProperty]
    private string _recordObjectsLabel = "recorded every minute";

    private int _minutesRecorded;

    private string ModelSize => ModelSizes[Math.Clamp(Model, 0, ModelSizes.Length - 1)];

    partial void OnObjectsChanged(bool value) => Send($"objects {(value ? 1 : 0)}");

    partial void OnModelChanged(int value) => Send($"model {ModelSize}");

    partial void OnConfidenceChanged(int value) => Send($"conf {value}");

    partial void OnPersonsOnlyChanged(bool value) => Send($"persons {(value ? 1 : 0)}");

    partial void OnCentersChanged(bool value) => Send($"centers {(value ? 1 : 0)}");

    partial void OnDirectionsChanged(bool value) => Send($"vectors {(value ? 1 : 0)}");

    partial void OnOverlapChanged(bool value) => Send($"overlap {(value ? 1 : 0)}");

    partial void OnAlertClassChanged(string value) => Send($"alert {OneLine(value)}");

    partial void OnLogJsonChanged(bool value) => Send($"log {(value ? 1 : 0)}");

    partial void OnRecordObjectsChanged(bool value)
    {
        Send($"recordobjects {(value ? 1 : 0)}");
        RecordObjectsLabel = value ? "recorded every minute" : "not recorded";
    }

    /// <summary>Everything the script detected during the last minute: written in the search engine.</summary>
    private async Task WriteObjectsAsync(JsonElement document)
    {
        if (assistant is null)
        {
            RecordObjectsLabel = "not recorded: no server";
            return;
        }
        var what = document.TryGetProperty("title", out var title) ? title.GetString() : string.Empty;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await assistant.SaveVisionObjectsAsync(document, timeout.Token);
            if (!result.IsSuccess)
            {
                RecordObjectsLabel = $"not recorded ({result.Message})";
                ActivityLog.Error("Vision", $"{Command}: the objects of the minute were refused by the server: {result.Message}");
                return;
            }
            _minutesRecorded++;
            RecordObjectsLabel = $"recorded: {_minutesRecorded} minute(s) · {DateTime.Now:HH:mm:ss} · {what}";
            ActivityLog.Info("Vision", $"{Command}: recorded the objects of the minute ({what})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            RecordObjectsLabel = $"not recorded ({ex.Message})";
            ActivityLog.Error("Vision", $"{Command}: the objects of the minute were not recorded in the search engine", ex);
        }
    }

    /// <summary>Opens the folder of the JSON lines of the objects.</summary>
    [RelayCommand]
    private void OpenLogFolder() => OpenInExplorer(LogDirectory);

    partial void OnThresholdChanged(int value) => Send($"threshold {value}");

    partial void OnExpressionsChanged(bool value) => Send($"expressions {(value ? 1 : 0)}");

    partial void OnLandmarksChanged(bool value) => Send($"landmarks {(value ? 1 : 0)}");

    partial void OnMotionChanged(bool value) => Send($"motion {(value ? 1 : 0)}");

    partial void OnMovementChanged(bool value) => Send($"movement {(value ? 1 : 0)}");

    protected override IEnumerable<string> StartArguments() =>
    [
        "--models", ModelsDirectory, "--faces", FacesDirectory,
        "--threshold", (Threshold / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        .. (Objects ? (string[])["--objects"] : []),
        "--model", ModelSize, "--yolo-models", YoloModelsDirectory, "--conf", (Confidence / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--alert-class", OneLine(AlertClass), "--log-directory", LogDirectory
    ];

    protected override void SendState()
    {
        Send($"expressions {(Expressions ? 1 : 0)}");
        Send($"landmarks {(Landmarks ? 1 : 0)}");
        Send($"motion {(Motion ? 1 : 0)}");
        Send($"movement {(Movement ? 1 : 0)}");
        Send($"record {(Record ? 1 : 0)}");
        Send($"persons {(PersonsOnly ? 1 : 0)}");
        Send($"centers {(Centers ? 1 : 0)}");
        Send($"vectors {(Directions ? 1 : 0)}");
        Send($"overlap {(Overlap ? 1 : 0)}");
        Send($"log {(LogJson ? 1 : 0)}");
        Send($"recordobjects {(RecordObjects ? 1 : 0)}");
    }

    protected override (string Found, bool Alert) Describe(JsonElement frame)
    {
        _people = frame.TryGetProperty("people", out var seen) && seen.ValueKind == JsonValueKind.Array
            ? [.. seen.EnumerateArray().Where(person => person.TryGetProperty("box", out var box) && box.GetArrayLength() == 4).Select(person =>
            {
                var box = person.GetProperty("box");
                return new SeenFace(person.GetProperty("name").GetString() ?? "unknown",
                    person.TryGetProperty("known", out var k) && k.ValueKind == JsonValueKind.True,
                    person.TryGetProperty("score", out var s) ? s.GetDouble() : 0,
                    box[0].GetInt32(), box[1].GetInt32(), box[2].GetInt32(), box[3].GetInt32());
            })]
            : [];
        // The user: the largest face recognised, the nearest to the camera.
        if (_people.Where(person => person.Known && person.Name != "unknown").MaxBy(person => person.Width * person.Height) is { } nearest)
            _user = new Recognition(nearest.Name, DateTime.UtcNow);
        WatchUnknownAtStart();
        _names = frame.TryGetProperty("known", out var enrolled) && enrolled.ValueKind == JsonValueKind.Object
            ? [.. enrolled.EnumerateObject().Select(person => person.Name).Order(StringComparer.CurrentCultureIgnoreCase)]
            : [];
        var people = frame.TryGetProperty("people", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Select(person =>
            {
                var name = person.GetProperty("name").GetString();
                var known = person.TryGetProperty("known", out var k) && k.ValueKind == JsonValueKind.True;
                var expression = person.TryGetProperty("expression", out var e) ? e.GetString() : null;
                return name + (known ? $" {person.GetProperty("score").GetDouble() * 100:0} %" : string.Empty)
                    + (string.IsNullOrEmpty(expression) ? string.Empty : $" ({expression})");
            }).ToList()
            : [];
        var known = frame.TryGetProperty("known", out var n) && n.ValueKind == JsonValueKind.Object
            ? string.Join(", ", n.EnumerateObject().Select(person => $"{person.Name} ({person.Value.GetInt32()})"))
            : string.Empty;
        var fps = frame.TryGetProperty("fps", out var f) ? f.GetDouble() : 0;
        var faces = people.Count == 0 ? "no face" : $"{people.Count} face(s): {string.Join(", ", people)}";

        // The objects, when they are detected.
        var objects = string.Empty;
        var alert = false;
        if (frame.TryGetProperty("objects_on", out var on) && on.ValueKind == JsonValueKind.True && frame.TryGetProperty("counts", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            var counts = string.Join(", ", c.EnumerateObject().OrderByDescending(o => o.Value.GetInt32()).Take(6).Select(o => $"{o.Value.GetInt32()} {o.Name}"));
            var overlap = frame.TryGetProperty("overlap", out var o) && o.ValueKind == JsonValueKind.True;
            var classSeen = frame.TryGetProperty("alert", out var a) && a.ValueKind == JsonValueKind.True;
            alert = overlap || classSeen;
            var model = frame.TryGetProperty("model", out var m) ? m.GetString() : string.Empty;
            var device = frame.TryGetProperty("device", out var d) ? d.GetString() : string.Empty;
            objects = $"  ·  objects: {(counts.Length > 0 ? counts : "nothing")}"
                + (overlap ? "  ·  OVERLAP" : string.Empty)
                + (classSeen ? $"  ·  {(frame.TryGetProperty("alert_class", out var k) ? k.GetString() : "alert")?.ToUpperInvariant()}" : string.Empty)
                + $" ({model}, {device})";
        }
        var place = LocationFrame(frame);
        // Who the chat names in its system prompt ("L'utilisateur est …").
        var user = RecognisedUser is { } who ? $"  ·  the user: {who}" : string.Empty;
        return ($"{(Talking ? "SPEAKING TO THE ASSISTANT  ·  " : string.Empty)}{faces}{objects}{user}  ·  enrolled: {(known.Length > 0 ? known : "nobody")}  ·  {fps:0.0} fps{place}", alert || Talking);
    }

    // ── A face nobody recognises at the start ──

    /// <summary>How long a face must be there, unrecognised, before <see cref="UnknownFaceAtStart"/>.</summary>
    public static readonly TimeSpan UnknownFaceDelay = TimeSpan.FromSeconds(4);

    /// <summary>How long after the start a face nobody recognises is still told (after: the start is over).</summary>
    public static readonly TimeSpan StartWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// At the start of /vision, a face is in front of the camera and nobody is recognised: there for
    /// <see cref="UnknownFaceDelay"/>, within <see cref="StartWindow"/> of the first frame, nobody recognised meanwhile.
    /// Once per start. Raised on the thread that made the window.
    /// </summary>
    public event Action? UnknownFaceAtStart;

    private DateTime? _firstFrame;
    private DateTime? _unknownSince;
    private bool _startTold;

    /// <summary>Follows the faces of each frame for <see cref="UnknownFaceAtStart"/>.</summary>
    private void WatchUnknownAtStart()
    {
        if (_startTold)
            return;
        var now = DateTime.UtcNow;
        _firstFrame ??= now;
        if (now - _firstFrame > StartWindow || _people.Any(person => person.Known && person.Name != "unknown"))
        {
            // Over: someone was recognised, or the start is past.
            _startTold = true;
            return;
        }
        if (_people.Count == 0)
        {
            _unknownSince = null;
            return;
        }
        _unknownSince ??= now;
        if (now - _unknownSince < UnknownFaceDelay)
            return;
        _startTold = true;
        ActivityLog.Info("Vision", $"{Command}: a face nobody recognises at the start");
        if (_ui is null)
            UnknownFaceAtStart?.Invoke();
        else
            _ui.Post(_ => UnknownFaceAtStart?.Invoke(), null);
    }

    // ── The search engine ──

    /// <summary>Before the script starts: the faces of the search engine missing in the folder are written there.</summary>
    protected override async Task BeforeStartAsync()
    {
        // A new start: a face nobody recognises is told again.
        _firstFrame = null;
        _unknownSince = null;
        _startTold = false;
        _ = LoadLocationsAsync();
        _stored = null;
        if (assistant is null)
        {
            ServerLabel = "search engine: no server";
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var faces = await assistant.LastFacesAsync(MaxFaces, timeout.Token);
            var written = 0;
            foreach (var face in faces)
            {
                // A name and a file as the script writes them, and nothing else: they become a path.
                if (face.Name.Length == 0 || face.File.Length == 0 || face.Picture.Length == 0
                    || face.Name != Path.GetFileName(face.Name) || face.File != Path.GetFileName(face.File)
                    || face.Name is "." or ".." || !face.File.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    continue;
                var path = Path.Combine(FacesDirectory, face.Name, face.File);
                if (File.Exists(path))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, Convert.FromBase64String(face.Picture));
                written++;
            }
            _stored = [.. faces.Select(f => f.Id)];
            ServerLabel = $"search engine: {faces.Count} face(s)" + (written > 0 ? $", {written} brought back to this machine" : string.Empty);
            if (written > 0)
                ActivityLog.Info("Vision", $"{Command}: {written} face(s) brought back from the search engine into {FacesDirectory}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            ServerLabel = $"search engine: not reached ({ex.Message}); the faces of this machine are used";
            ActivityLog.Error("Vision", $"{Command}: the faces of the search engine could not be read", ex);
        }
    }

    /// <summary>After this long without being seen, someone recognised again "comes in" once more (and is greeted again).</summary>
    public static readonly TimeSpan AbsenceBeforeArrival = TimeSpan.FromMinutes(10);

    // The window is made by the chat on its main thread: the arrivals are told there.
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly Dictionary<string, DateTime> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Someone enrolled is recognised in front of the camera: the first time, or after <see cref="AbsenceBeforeArrival"/>
    /// without being seen (the name enrolled). Raised on the thread that made the window.
    /// </summary>
    public event Action<string>? PersonArrived;

    /// <summary>Notes who an episode or an observation is about; tells when someone recognised comes in.</summary>
    private void Seen(JsonElement document)
    {
        if (!document.TryGetProperty("known", out var known) || known.ValueKind != JsonValueKind.True
            || !document.TryGetProperty("name", out var who) || who.GetString() is not { Length: > 0 } name || name == "unknown")
            return;
        var now = DateTime.UtcNow;
        bool arrived;
        lock (_lastSeen)
        {
            arrived = !_lastSeen.TryGetValue(name, out var last) || now - last >= AbsenceBeforeArrival;
            _lastSeen[name] = now;
        }
        if (!arrived)
            return;
        ActivityLog.Info("Vision", $"{Command}: {name} recognised");
        if (_ui is null)
            PersonArrived?.Invoke(name);
        else
            _ui.Post(_ => PersonArrived?.Invoke(name), null);
    }

    /// <summary>What the script says of the faces it keeps: the search engine follows.</summary>
    protected override void OnMessage(JsonElement message)
    {
        if (message.TryGetProperty("detected", out var detected) && detected.ValueKind == JsonValueKind.Object)
            _ = WriteObjectsAsync(detected);
        else if (message.TryGetProperty("episode", out var episode) && episode.ValueKind == JsonValueKind.Object)
        {
            Seen(episode);
            WriteRecord(episode: true, episode);
        }
        else if (message.TryGetProperty("observation", out var observation) && observation.ValueKind == JsonValueKind.Object)
        {
            Seen(observation);
            WriteRecord(episode: false, observation);
        }
        else if (message.TryGetProperty("inventory", out var inventory) && inventory.ValueKind == JsonValueKind.Array)
            _ = StoreAsync([.. inventory.EnumerateArray().Select(Read).Where(f => f is not null).Select(f => f!)], onlyMissing: true);
        else if (message.TryGetProperty("enrolled", out var enrolled) && Read(enrolled) is { } face)
            _ = StoreAsync([face], onlyMissing: false);
        else if (message.TryGetProperty("forgotten", out var forgotten) && forgotten.GetString() is { Length: > 0 } name)
            _ = EraseAsync(name);
    }

    /// <summary>
    /// Writes an episode or an observation in the search engine, one after the other in the order they came (an
    /// episode is written again every second: the last one must be the one kept).
    /// </summary>
    private void WriteRecord(bool episode, JsonElement document)
    {
        if (assistant is null)
        {
            RecordLabel = "not recorded: no server";
            return;
        }
        var id = document.TryGetProperty("id", out var given) ? given.GetString() ?? string.Empty : string.Empty;
        _writes = _writes.ContinueWith(async _ =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await assistant.SaveFaceRecordAsync(episode, document, timeout.Token);
                if (!result.IsSuccess)
                {
                    RecordLabel = $"not recorded ({result.Message})";
                    ActivityLog.Error("Vision", $"{Command}: {id} refused by the server: {result.Message}");
                    return;
                }
                if (episode)
                    _episodesWritten.Add(id);
                else
                {
                    _changesWritten++;
                    // The changes are few: each is said in the journal (who, what changed).
                    ActivityLog.Info("Vision", $"{Command}: recorded {(document.TryGetProperty("name", out var who) ? who.GetString() : "?")} "
                        + $"{(document.TryGetProperty("change", out var what) ? what.GetString() : "?")} "
                        + $"({(document.TryGetProperty("emotion", out var feels) ? feels.GetString() : "?")}, {(document.TryGetProperty("activity", out var moves) ? moves.GetString() : "?")})");
                }
                RecordLabel = $"recorded: {_episodesWritten.Count} episode(s), {_changesWritten} change(s) · {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                RecordLabel = $"not recorded ({ex.Message})";
                ActivityLog.Error("Vision", $"{Command}: {id} not recorded in the search engine", ex);
            }
        }, TaskScheduler.Default).Unwrap();
    }

    /// <summary>A face as the script describes it (name, file, feature); its picture is read from its file when it is stored.</summary>
    private static FaceDocument? Read(JsonElement face)
    {
        if (face.ValueKind != JsonValueKind.Object || !face.TryGetProperty("name", out var n) || !face.TryGetProperty("file", out var f)
            || n.GetString() is not { Length: > 0 } name || f.GetString() is not { Length: > 0 } file)
            return null;
        return new FaceDocument
        {
            Id = FaceDocument.IdOf(name, file), Name = name, File = file, Title = name,
            Feature = face.TryGetProperty("feature", out var numbers) && numbers.ValueKind == JsonValueKind.Array ? [.. numbers.EnumerateArray().Select(v => v.GetDouble())] : []
        };
    }

    /// <summary>Writes these faces in the search engine (only the ones it does not hold, or all of them), each with its picture.</summary>
    private async Task StoreAsync(List<FaceDocument> faces, bool onlyMissing)
    {
        if (assistant is null)
            return;
        // Not reached before the start: tried again now, and everything is written.
        var stored = _stored;
        var saved = 0;
        try
        {
            foreach (var face in faces)
            {
                if (onlyMissing && stored is not null && stored.Contains(face.Id))
                    continue;
                var path = Path.Combine(FacesDirectory, face.Name, face.File);
                if (!File.Exists(path))
                    continue;
                face.Picture = Convert.ToBase64String(await File.ReadAllBytesAsync(path));
                face.Date = File.GetLastWriteTimeUtc(path);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var result = await assistant.SaveFaceAsync(face, timeout.Token);
                if (!result.IsSuccess)
                {
                    ServerLabel = $"search engine: {face.Name} not saved ({result.Message})";
                    ActivityLog.Error("Vision", $"{Command}: face {face.Id} refused by the server: {result.Message}");
                    return;
                }
                stored?.Add(face.Id);
                saved++;
            }
            if (saved > 0 || !onlyMissing)
            {
                ServerLabel = $"search engine: {saved} face(s) saved {DateTime.Now:HH:mm:ss}" + (stored is not null ? $", {stored.Count} in all" : string.Empty);
                ActivityLog.Info("Vision", $"{Command}: {saved} face(s) saved in the search engine ({string.Join(", ", faces.Select(f => f.Name).Distinct())})");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        {
            ServerLabel = $"search engine: not saved ({ex.Message}); kept on this machine, written at the next start";
            ActivityLog.Error("Vision", $"{Command}: the faces could not be saved in the search engine", ex);
        }
    }

    /// <summary>Erases every face of this name from the search engine, for good.</summary>
    private async Task EraseAsync(string name)
    {
        if (assistant is null)
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // The ones the search engine lists, and the ones just written that it does not list yet (it lists a
            // document a second or two after it was saved).
            var prefix = FaceDocument.IdOf(name, string.Empty);
            var ids = (await assistant.LastFacesAsync(MaxFaces, timeout.Token)).Where(f => f.Name == name).Select(f => f.Id)
                .Concat(_stored?.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)) ?? [])
                .Distinct().ToList();
            foreach (var id in ids)
            {
                await assistant.DeleteFaceAsync(id, timeout.Token);
                _stored?.Remove(id);
            }
            ServerLabel = $"search engine: {name} erased ({ids.Count} face(s))";
            ActivityLog.Info("Vision", $"{Command}: {name} erased from the search engine ({ids.Count} face(s))");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            ServerLabel = $"search engine: {name} NOT erased ({ex.Message}): it would come back at the next start, FORGET it again";
            IsError = true;
            ActivityLog.Error("Vision", $"{Command}: {name} could not be erased from the search engine", ex);
        }
    }

    private static string OneLine(string name) => name.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>The face of the last frame at this point (in the pixels of the frame): the smallest box holding it; null when none does.</summary>
    public SeenFace? FaceAt(double x, double y) =>
        People.Where(face => face.Holds(x, y)).OrderBy(face => face.Width * face.Height).FirstOrDefault();

    /// <summary>
    /// A face was clicked: the script keeps it (aligned) until it is named, so that the face enrolled is this one even
    /// when it moves, or another one comes, while the name is typed.
    /// </summary>
    public void Hold(SeenFace face) => Send($"hold {face.X + face.Width / 2} {face.Y + face.Height / 2}");

    /// <summary>
    /// Keeps the face clicked (<see cref="Hold"/>) under <paramref name="name"/>: a new name, or one more picture of
    /// a name enrolled. False when the script does not run (the answer comes back in the status of the window).
    /// </summary>
    public bool EnrolHeld(string name)
    {
        name = OneLine(name);
        if (!IsRunning || name.Length == 0)
            return false;
        Send($"enrolheld {name}");
        ActivityLog.Info("Vision", $"{Command}: enrol {name} (the face clicked)");
        return true;
    }

    /// <summary>
    /// ADD RECO: enrols the largest face of a photo (a file) under <paramref name="name"/>, as a click on a face does
    /// with the camera: the picture is kept in the folder of the faces and in the search engine. False when the script
    /// does not run (the answer comes back in the status of the window).
    /// </summary>
    public bool EnrolPhoto(string name, string path)
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(name) || !File.Exists(path))
            return false;
        Send("enrolfile " + JsonSerializer.Serialize(new { name = name.Trim(), path }));
        ActivityLog.Info("Vision", $"{Command}: enrol {name.Trim()} from the photo {Path.GetFileName(path)}");
        return true;
    }

    /// <summary>A name enrolled, as the folder of the faces holds it: its pictures (the newest first).</summary>
    public sealed record EnrolledFace(string Name, IReadOnlyList<string> Pictures);

    /// <summary>The names enrolled on this machine (one folder each in <see cref="FacesDirectory"/>), by name; read from the folder, the camera on or not.</summary>
    public static IReadOnlyList<EnrolledFace> EnrolledFaces()
    {
        try
        {
            if (!Directory.Exists(FacesDirectory))
                return [];
            return Directory.GetDirectories(FacesDirectory)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Select(d => new EnrolledFace(Path.GetFileName(d), Directory.GetFiles(d)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !Path.GetFileName(f).StartsWith('.'))
                    .OrderByDescending(f => f, StringComparer.Ordinal).ToList()))
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Vision", "/vision: the faces enrolled could not be listed", ex);
            return [];
        }
    }

    /// <summary>
    /// Deletes the pictures kept under <paramref name="name"/>, once confirmed, on this machine and in the search engine.
    /// The camera on, the script forgets the name (its folder deleted) and the search engine follows; the camera off, the
    /// folder is deleted here and the faces of the name erased from the search engine (else they would come back at the
    /// next start). False when it was not confirmed or could not be done.
    /// </summary>
    public async Task<bool> ForgetAsync(string name)
    {
        name = OneLine(name);
        if (name.Length == 0
            || !await ConfirmAsync("Forget a face", $"Forget {name}? The pictures kept under this name are deleted, on this machine and in the search engine: this cannot be undone."))
            return false;
        var folder = Path.Combine(FacesDirectory, name);
        if (IsRunning && _names.Contains(name))
        {
            Send($"forget {name}");
            return true;
        }
        try
        {
            if (Directory.Exists(folder) && Path.GetFullPath(folder).StartsWith(Path.GetFullPath(FacesDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                Directory.Delete(folder, recursive: true);
            ActivityLog.Info("Vision", $"{Command}: {name} forgotten (its folder deleted, the camera off)");
            // The script running without this name (a folder without a picture): it reads the folder again.
            if (IsRunning)
                Send($"forget {name}");
            await EraseAsync(name);
            Report($"{name} forgotten", false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"{name} could not be forgotten: {ex.Message}", true);
            ActivityLog.Error("Vision", $"{Command}: {name} could not be forgotten", ex);
            return false;
        }
    }

    /// <summary>Opens the folder of the faces enrolled in the file explorer of the system.</summary>
    [RelayCommand]
    private void OpenFacesFolder() => OpenInExplorer(FacesDirectory);
}
