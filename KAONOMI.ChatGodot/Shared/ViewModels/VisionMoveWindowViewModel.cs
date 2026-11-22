using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// /vision:move: the motion detection of KAONOMI.VisionOpenCV (Python and OpenCV, no neural network) in a window of
/// the desktop (<see cref="VisionStreamViewModel"/>). The window asks: the view (the mosaic of the four views, or one
/// of them), the smallest zone kept, the threshold of the background subtraction, the trail of the gravity centre,
/// the pause, the background learnt again, a picture saved in <c>~/Pictures/kaonomi/vision</c>.
/// </summary>
public sealed partial class VisionMoveWindowViewModel() : VisionStreamViewModel("move")
{
    public static readonly string[] Views = ["Mosaic (the 4 views)", "Camera + zones", "Foreground mask", "Contours + trail", "Motion highlight"];

    /// <summary>0: the mosaic of the four views; 1 to 4: one of them.</summary>
    [ObservableProperty]
    private int _view;

    /// <summary>The smallest moving zone kept, in pixels.</summary>
    [ObservableProperty]
    private int _minArea = 800;

    /// <summary>The threshold of the background subtraction: the lower, the more sensitive.</summary>
    [ObservableProperty]
    private int _threshold = 25;

    [ObservableProperty]
    private bool _trail = true;

    partial void OnViewChanged(int value) => Send($"view {value}");

    partial void OnMinAreaChanged(int value) => Send($"min_area {value}");

    partial void OnThresholdChanged(int value) => Send($"threshold {value}");

    partial void OnTrailChanged(bool value) => Send($"trail {(value ? 1 : 0)}");

    protected override IEnumerable<string> StartArguments() => ["--min-area", MinArea.ToString(), "--sensitivity", Threshold.ToString()];

    protected override void SendState()
    {
        Send($"view {View}");
        Send($"trail {(Trail ? 1 : 0)}");
    }

    protected override void RunningChanged() => ResetBackgroundCommand.NotifyCanExecuteChanged();

    protected override (string Found, bool Alert) Describe(JsonElement frame)
    {
        var state = frame.TryGetProperty("state", out var s) ? s.GetString() : null;
        var what = state switch { "motion" => "MOTION", "learning" => "LEARNING THE BACKGROUND", _ => "still" };
        var zones = frame.TryGetProperty("zones", out var z) ? z.GetInt32() : 0;
        var ratio = frame.TryGetProperty("ratio", out var r) ? r.GetDouble() : 0;
        var fps = frame.TryGetProperty("fps", out var f) ? f.GetDouble() : 0;
        var center = frame.TryGetProperty("center", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() == 2
            ? $"  ·  centre {c[0].GetInt32()},{c[1].GetInt32()}" : string.Empty;
        return ($"{what}  ·  {zones} zone(s)  ·  {ratio * 100:0.0} % of the frame{center}  ·  {fps:0.0} fps", state == "motion");
    }

    /// <summary>Forgets the background and learns it again: the camera moved, the light changed.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void ResetBackground() => Send("reset");
}
