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
/// The object detection of KAONOMI.VisionOpenCV (Python, YOLOv8 and OpenCV) alone, in a window of the desktop
/// (<see cref="VisionStreamViewModel"/>) that no command opens any more: it was /vision:yolo, and /vision now shows the
/// objects with the faces. The window asks: the model (nano: fast; medium: more accurate), the
/// smallest confidence kept, the persons only or every object, and what is drawn over the boxes: the centre of each
/// person, its trail and the arrow of its direction, the alert when two persons overlap, the alert when an object of
/// a class is seen; the detections written as JSON lines in <c>~/kaonomi/vision/yolo</c>; the pause; a picture saved
/// in <c>~/Pictures/kaonomi/vision</c>. The models are in <c>~/kaonomi/models/yolo</c> (downloaded when missing).
/// Every minute, the script sums up everything it detected and this window writes it in the search engine (RECORD).
/// </summary>
public sealed partial class VisionYoloWindowViewModel(KaonomiAssistantService? assistant = null) : VisionStreamViewModel("yolo")
{
    /// <summary>
    /// Everything detected is recorded in the search engine, one document a minute: each kind of object with how
    /// many of them at once, how often and how sure, the persons, the alerts (index <c>visionobjects</c>).
    /// </summary>
    [ObservableProperty]
    private bool _record = true;

    /// <summary>What was recorded since the window opened, or why it is not.</summary>
    [ObservableProperty]
    private string _recordLabel = "recorded every minute";

    private int _recorded;

    partial void OnRecordChanged(bool value)
    {
        Send($"record {(value ? 1 : 0)}");
        RecordLabel = value ? "recorded every minute" : "not recorded";
    }

    /// <summary>The script tells what it detected during the last minute: written in the search engine.</summary>
    protected override void OnMessage(JsonElement message)
    {
        if (message.TryGetProperty("detected", out var detected) && detected.ValueKind == JsonValueKind.Object)
            _ = WriteAsync(detected);
    }

    private async Task WriteAsync(JsonElement document)
    {
        if (assistant is null)
        {
            RecordLabel = "not recorded: no server";
            return;
        }
        var what = document.TryGetProperty("title", out var title) ? title.GetString() : string.Empty;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await assistant.SaveVisionObjectsAsync(document, timeout.Token);
            if (!result.IsSuccess)
            {
                RecordLabel = $"not recorded ({result.Message})";
                ActivityLog.Error("Vision", $"{Command}: the objects of the minute were refused by the server: {result.Message}");
                return;
            }
            _recorded++;
            RecordLabel = $"recorded: {_recorded} minute(s) · {DateTime.Now:HH:mm:ss} · {what}";
            ActivityLog.Info("Vision", $"{Command}: recorded the objects of the minute ({what})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            RecordLabel = $"not recorded ({ex.Message})";
            ActivityLog.Error("Vision", $"{Command}: the objects of the minute were not recorded in the search engine", ex);
        }
    }

    public static readonly string[] Models = ["YOLOv8 nano (fast)", "YOLOv8 medium (more accurate)", "YOLOv8 large", "YOLOv8 extra large (the most accurate, the slowest)"];

    private static readonly string[] ModelSizes = ["n", "m", "l", "x"];

    /// <summary>The folder of the detections written as JSON lines.</summary>
    public static string LogDirectory => Path.Combine(ChatSettings.KaonomiHome, "vision", "yolo");

    /// <summary>The folder of the models (yolov8n.pt, yolov8m.pt).</summary>
    public static string ModelsDirectory => Path.Combine(ChatSettings.KaonomiHome, "models", "yolo");

    /// <summary>0: nano; 1: medium; 2: large; 3: extra large.</summary>
    [ObservableProperty]
    private int _model;

    /// <summary>The smallest confidence of a detection kept, in per cent.</summary>
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

    /// <summary>The detections written as JSON lines.</summary>
    [ObservableProperty]
    private bool _logJson;

    private string ModelSize => ModelSizes[System.Math.Clamp(Model, 0, ModelSizes.Length - 1)];

    partial void OnModelChanged(int value) => Send($"model {ModelSize}");

    partial void OnConfidenceChanged(int value) => Send($"conf {value}");

    partial void OnPersonsOnlyChanged(bool value) => Send($"persons {(value ? 1 : 0)}");

    partial void OnCentersChanged(bool value) => Send($"centers {(value ? 1 : 0)}");

    partial void OnDirectionsChanged(bool value) => Send($"vectors {(value ? 1 : 0)}");

    partial void OnOverlapChanged(bool value) => Send($"overlap {(value ? 1 : 0)}");

    partial void OnAlertClassChanged(string value) => Send($"alert {OneLine(value)}");

    partial void OnLogJsonChanged(bool value) => Send($"log {(value ? 1 : 0)}");

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    protected override IEnumerable<string> StartArguments() =>
    [
        "--model", ModelSize, "--models", ModelsDirectory, "--conf", (Confidence / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--alert-class", OneLine(AlertClass), "--log-directory", LogDirectory
    ];

    protected override void SendState()
    {
        Send($"persons {(PersonsOnly ? 1 : 0)}");
        Send($"centers {(Centers ? 1 : 0)}");
        Send($"vectors {(Directions ? 1 : 0)}");
        Send($"overlap {(Overlap ? 1 : 0)}");
        Send($"log {(LogJson ? 1 : 0)}");
        Send($"record {(Record ? 1 : 0)}");
    }

    protected override (string Found, bool Alert) Describe(JsonElement frame)
    {
        var counts = frame.TryGetProperty("counts", out var c) && c.ValueKind == JsonValueKind.Object
            ? string.Join(", ", c.EnumerateObject().OrderByDescending(p => p.Value.GetInt32()).Take(6).Select(p => $"{p.Value.GetInt32()} {p.Name}"))
            : string.Empty;
        var overlap = frame.TryGetProperty("overlap", out var o) && o.ValueKind == JsonValueKind.True;
        var alert = frame.TryGetProperty("alert", out var a) && a.ValueKind == JsonValueKind.True;
        var alerts = (overlap ? "  ·  OVERLAP" : string.Empty)
            + (alert ? $"  ·  {(frame.TryGetProperty("alert_class", out var k) ? k.GetString() : "alert")?.ToUpperInvariant()}" : string.Empty);
        var model = frame.TryGetProperty("model", out var m) ? m.GetString() : string.Empty;
        var device = frame.TryGetProperty("device", out var d) ? d.GetString() : string.Empty;
        var fps = frame.TryGetProperty("fps", out var f) ? f.GetDouble() : 0;
        return ($"{(counts.Length > 0 ? counts : "nothing seen")}{alerts}  ·  {model} ({device})  ·  {fps:0.0} fps", overlap || alert);
    }

    /// <summary>Opens the folder of the JSON lines in the file explorer of the system.</summary>
    [RelayCommand]
    private void OpenLogFolder() => OpenInExplorer(LogDirectory);
}
