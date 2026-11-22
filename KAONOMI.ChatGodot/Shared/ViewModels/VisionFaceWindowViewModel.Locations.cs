using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The places where the user works (SET LOCATION): the user sits in front of the camera in the usual working
/// position and speaks for <see cref="MeasureSeconds"/> seconds; the mean area of the box of the face (a share of the
/// frame) and the level of the voice are kept under the name of the place, in the search engine (index
/// <c>worklocation</c>) and in <c>~/kaonomi/vision/locations.json</c>. Then, with that place chosen, a frame where
/// the largest face is at least that big while the voice of the last second is at least that loud means the user
/// speaks to the assistant (<see cref="Talking"/>: the picture is framed in red).
/// </summary>
public sealed partial class VisionFaceWindowViewModel
{
    /// <summary>How long a place is measured, in seconds.</summary>
    public const int MeasureSeconds = 5;

    /// <summary>The voice compared with the place: the level of the last second heard.</summary>
    private const double LiveVoiceSeconds = 1;

    private static readonly JsonSerializerOptions LocationsJson = new() { WriteIndented = true };

    private static string LocationsFile => Path.Combine(ChatSettings.KaonomiHome, "vision", "locations.json");

    private static string ChosenLocationFile => Path.Combine(ChatSettings.KaonomiHome, "vision", "location.txt");

    /// <summary>The pictures of the places analysed: <c>~/kaonomi/vision/locations/&lt;id&gt;.jpg</c>.</summary>
    public static string LocationPicturesDirectory => Path.Combine(ChatSettings.KaonomiHome, "vision", "locations");

    /// <summary>
    /// What a vision model sees in a picture (a JPEG): its text and the model that wrote it. Set by the chat (the
    /// model of the pictures of config.yml, <c>modes.image</c>); null: the places are not analysed.
    /// </summary>
    public Func<byte[], CancellationToken, Task<(string Text, string Model)>>? DescribePicture { get; set; }

    /// <summary>The entities of a text (the NER of the server); set by the chat, null: none are looked for.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<string>>>? ExtractEntities { get; set; }

    private readonly object _measureLock = new();
    private bool _measuring;
    private double _measuredArea;
    private long _measuredWidth, _measuredHeight;
    private int _measuredFrames, _frameWidth, _frameHeight;

    private volatile bool _talking;
    private List<WorkLocationDocument>? _locations;

    /// <summary>The places measured, by their name.</summary>
    public IReadOnlyList<WorkLocationDocument> Locations => _locations ??= ReadLocations();

    /// <summary>The place the face and the voice are compared with ("" for none): kept for the next time.</summary>
    [ObservableProperty]
    private string _location = ReadChosenLocation();

    /// <summary>The places changed (one measured, or read from the search engine).</summary>
    public event Action? LocationsChanged;

    /// <summary>
    /// The window says where the user is: it runs and a place measured is chosen. Otherwise <see cref="Talking"/> says
    /// nothing (it is false).
    /// </summary>
    public bool WatchesPlace => IsRunning && Place is not null;

    /// <summary>
    /// The place chosen, among the places measured (null: none). Looked for once, then kept until the choice or the
    /// places change: it is asked at each frame of the camera, and at each frame of the screen by the 3D animation.
    /// </summary>
    private WorkLocationDocument? Place
    {
        get
        {
            if (!_placeKnown)
            {
                var name = Location;
                _place = name.Length == 0 ? null : Locations.FirstOrDefault(l => l.Name == name);
                _placeKnown = true;
            }
            return _place;
        }
    }

    private volatile WorkLocationDocument? _place;
    private volatile bool _placeKnown;

    /// <summary>
    /// The user is close enough: the place chosen is watched and the largest face of the last frame is at least as big
    /// as measured there (the voice aside). The 3D animation then comes closer and follows the face.
    /// </summary>
    public bool PersonNear => _near;

    /// <summary>The centre of the largest face of the last frame, from 0 to 1 across the picture (x to the right, y down); null when none.</summary>
    public (double X, double Y)? PersonAt => _personAt;

    /// <summary>The centre of the rectangle of what moves in the last frame (MOVEMENT), from 0 to 1 across the picture; null when nothing moves.</summary>
    public (double X, double Y)? MovementAt => _movementBox is { } m ? (m.Item1, m.Item2) : null;

    private volatile Tuple<double, double>? _movementBox;
    private volatile bool _near;
    private volatile Tuple<double, double>? _personAtBox;
    private (double X, double Y)? _personAt => _personAtBox is { } p ? (p.Item1, p.Item2) : null;

    /// <summary>The user speaks to the assistant now: the face and the voice reach the measure of the place chosen.</summary>
    public bool Talking => _talking;

    partial void OnLocationChanged(string value)
    {
        _placeKnown = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ChosenLocationFile)!);
            File.WriteAllText(ChosenLocationFile, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Info("Vision", "The place chosen could not be kept: " + ex.Message);
        }
        if (value.Length == 0)
            _talking = false;
    }

    private static string ReadChosenLocation()
    {
        try
        {
            return File.Exists(ChosenLocationFile) ? File.ReadAllText(ChosenLocationFile).Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static List<WorkLocationDocument> ReadLocations()
    {
        try
        {
            if (File.Exists(LocationsFile) && JsonSerializer.Deserialize<List<WorkLocationDocument>>(File.ReadAllText(LocationsFile)) is { } read)
                return [.. read.Where(l => l.Name.Length > 0).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ActivityLog.Info("Vision", "The places could not be read: " + ex.Message);
        }
        return [];
    }

    private void KeepLocations(IEnumerable<WorkLocationDocument> locations)
    {
        _locations = [.. locations.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)];
        _placeKnown = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LocationsFile)!);
            File.WriteAllText(LocationsFile, JsonSerializer.Serialize(_locations, LocationsJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Info("Vision", "The places could not be kept: " + ex.Message);
        }
        LocationsChanged?.Invoke();
    }

    /// <summary>The places of the search engine are brought here (the newest of each one kept).</summary>
    public async Task LoadLocationsAsync()
    {
        if (assistant is null)
            return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stored = await assistant.LastWorkLocationsAsync(500, timeout.Token);
            if (stored.Count == 0)
                return;
            var merged = Locations.Concat(stored).GroupBy(l => l.Id).Select(g => g.OrderByDescending(l => l.Date).First());
            KeepLocations(merged);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            ActivityLog.Info("Vision", "The places could not be read from the search engine: " + ex.Message);
        }
    }

    /// <summary>The measure of a place starts: the faces of the frames from now on are summed up.</summary>
    public void BeginMeasure()
    {
        lock (_measureLock)
        {
            _measuring = true;
            _measuredArea = 0;
            _measuredWidth = _measuredHeight = 0;
            _measuredFrames = 0;
        }
    }

    /// <summary>The measure of a place is given up: nothing is kept.</summary>
    public void CancelMeasure()
    {
        lock (_measureLock)
            _measuring = false;
    }

    /// <summary>
    /// The measure of the place <paramref name="name"/> ends: the mean box of the largest face of the frames since
    /// <see cref="BeginMeasure"/> and the level of the voice of the last <see cref="MeasureSeconds"/> seconds are kept
    /// (here, then in the search engine) and the place is chosen. Null when it is kept, else why not.
    /// </summary>
    public async Task<string?> EndMeasureAsync(string name)
    {
        name = name.Trim();
        double area;
        int frames, width, height, frameWidth, frameHeight;
        lock (_measureLock)
        {
            _measuring = false;
            frames = _measuredFrames;
            area = frames > 0 ? _measuredArea / frames : 0;
            width = frames > 0 ? (int)(_measuredWidth / frames) : 0;
            height = frames > 0 ? (int)(_measuredHeight / frames) : 0;
            (frameWidth, frameHeight) = (_frameWidth, _frameHeight);
        }
        if (frames == 0)
            return "No face was seen during the measure: sit in front of the camera, your face in the picture";
        if (Voice is not { } voice)
            return "This window has no microphone";
        var level = voice.HeardLevel(MeasureSeconds);
        if (RecorderWindowViewModel.IsSilence(level))
            return $"No voice was heard ({RecorderWindowViewModel.LevelText(level)}): speak during the {MeasureSeconds} seconds";

        var location = new WorkLocationDocument
        {
            Id = WorkLocationDocument.IdOf(name),
            Name = name,
            Title = name,
            FaceArea = area,
            FaceWidth = width,
            FaceHeight = height,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            FaceFrames = frames,
            VoiceLevel = level,
            VoiceDecibels = Math.Round(20 * Math.Log10(Math.Max(level, 1e-6)), 1),
            Seconds = MeasureSeconds,
            Camera = VisionApps.TestSource is { } test ? "test: " + Path.GetFileName(test) : Cameras.FirstOrDefault(c => c.Index == Camera)?.Name ?? Camera.ToString(),
            Microphone = voice.Recorder.SourceName,
        };
        // A picture of the place, taken now (the user still in the working position): analysed after.
        var picture = DescribePicture is null ? null : await CaptureAsync(TimeSpan.FromSeconds(3));
        KeepLocations(Locations.Where(l => l.Id != location.Id).Append(location));
        Location = name;
        var said = $"{name}: face {area * 100:0.0} % of the picture ({width} × {height} px), voice {location.VoiceDecibels:0} dB";
        ActivityLog.Info("Vision", $"Place measured: {said}");
        await StoreLocationAsync(location, said);
        if (picture is not null)
            _ = AnalyseLocationAsync(location, picture);
        else if (DescribePicture is not null)
            ActivityLog.Info("Vision", $"Place {name}: no picture of the camera came, the place is not analysed");
        return null;
    }

    /// <summary>The place written in the search engine (it is kept on this machine already); said in the status.</summary>
    private async Task StoreLocationAsync(WorkLocationDocument location, string said)
    {
        if (assistant is null)
        {
            Say($"{said} · kept on this machine (no server)");
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var saved = await assistant.SaveWorkLocationAsync(location, timeout.Token);
            Say(saved.Success ? $"{said} · kept in the search engine" : $"{said} · kept on this machine, the search engine refused it: {saved.Message}", !saved.Success);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Say($"{said} · kept on this machine, not in the search engine ({ex.Message})", true);
        }
    }

    /// <summary>
    /// The picture of the place: kept in <see cref="LocationPicturesDirectory"/>, described by the vision model, the
    /// entities of its description found by the NER; all three added to the place, kept here and in the search engine.
    /// </summary>
    private async Task AnalyseLocationAsync(WorkLocationDocument location, byte[] picture)
    {
        var name = location.Name;
        try
        {
            Directory.CreateDirectory(LocationPicturesDirectory);
            var file = Path.Combine(LocationPicturesDirectory, location.Id + ".jpg");
            await File.WriteAllBytesAsync(file, picture);
            location.Picture = file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Info("Vision", $"The picture of {name} could not be kept: {ex.Message}");
        }
        try
        {
            Say($"{name}: the picture of the place is analysed…");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var (text, model) = await DescribePicture!(picture, timeout.Token);
            location.Analysis = text.Trim();
            location.AnalysisModel = model;
            if (ExtractEntities is { } extract && location.Analysis.Length > 0)
            {
                try
                {
                    location.Entities = [.. await extract(location.Analysis, timeout.Token)];
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    ActivityLog.Info("Vision", $"The entities of the picture of {name} could not be found: {ex.Message}");
                }
            }
            ActivityLog.Info("Vision", $"Place {name} analysed by {model}: {location.Analysis.Length} characters, {location.Entities.Count} entit(ies)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Say($"{name}: the picture of the place could not be analysed ({ex.Message})", true);
            ActivityLog.Error("Vision", $"The picture of {name} could not be analysed", ex);
        }
        // Kept with what was found, unless the place was measured again meanwhile.
        if (Locations.FirstOrDefault(l => l.Id == location.Id) is { } kept && !ReferenceEquals(kept, location) && kept.Date > location.Date)
            return;
        KeepLocations(Locations.Where(l => l.Id != location.Id).Append(location));
        var entities = location.Entities.Count > 0 ? $" · entities: {string.Join(", ", location.Entities.Take(8))}{(location.Entities.Count > 8 ? "…" : "")}" : "";
        await StoreLocationAsync(location, $"{name}: picture analysed{entities}");
    }

    /// <summary>
    /// At each frame (the faces of it just read): summed up while a place is measured; compared with the place chosen
    /// otherwise. The text added to the line under the picture.
    /// </summary>
    private string LocationFrame(JsonElement frame)
    {
        var width = frame.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
        var height = frame.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
        // The largest face: the first one (the script gives the faces the largest first).
        var people = _people;
        var face = people.Count > 0 ? people[0] : null;
        var area = face is not null && width > 0 && height > 0 ? face.Width * (double)face.Height / (width * (double)height) : 0;
        _movementBox = width > 0 && height > 0 && frame.TryGetProperty("movement", out var moving) && moving.ValueKind == JsonValueKind.Array && moving.GetArrayLength() == 4
            ? Tuple.Create((moving[0].GetInt32() + moving[2].GetInt32() / 2.0) / width, (moving[1].GetInt32() + moving[3].GetInt32() / 2.0) / height)
            : null;
        _personAtBox = face is not null && width > 0 && height > 0
            ? Tuple.Create((face.X + face.Width / 2.0) / width, (face.Y + face.Height / 2.0) / height)
            : null;
        lock (_measureLock)
        {
            (_frameWidth, _frameHeight) = (width, height);
            if (_measuring)
            {
                if (face is not null)
                {
                    _measuredArea += area;
                    _measuredWidth += face.Width;
                    _measuredHeight += face.Height;
                    _measuredFrames++;
                }
                _talking = false;
                _near = false;
                return "  ·  measuring the place…";
            }
        }
        if (Place is not { } place || Voice is not { } voice)
        {
            _talking = false;
            _near = false;
            return string.Empty;
        }
        _near = face is not null && area >= place.FaceArea - 1e-9;
        var level = voice.HeardLevel(LiveVoiceSeconds);
        // (1e-9: a mean of equal areas may come out a hair over them.)
        _talking = face is not null && area >= place.FaceArea - 1e-9 && level >= place.VoiceLevel;
        return $"  ·  {place.Name}: face {area * 100:0.0}/{place.FaceArea * 100:0.0} %, voice {RecorderWindowViewModel.LevelText(level)}/{place.VoiceDecibels:0} dB";
    }
}
