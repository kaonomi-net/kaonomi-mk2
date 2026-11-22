using System;
using Godot;

namespace KAONOMI.ChatGodot.Tools.Video;

/// <summary>
/// Where the video is shown: its picture fitted in the space (black around it); while the first picture comes,
/// "loading". A click plays or pauses. In the capture mode (✂ CAPTURE, C), a rectangle drawn on the picture is the part
/// to capture (Esc cancels).
/// </summary>
public sealed partial class VideoSurface : Control
{
    private readonly VideoEngine _engine;
    private bool _capturing;
    private Vector2? _from;
    private Vector2 _to;

    /// <summary>A part of the picture drawn in the capture mode: x, y, width and height in fractions of the picture.</summary>
    public event Action<(double X, double Y, double Width, double Height)>? Captured;

    /// <summary>True while a part of the picture is to be drawn.</summary>
    public bool Capturing
    {
        get => _capturing;
        set
        {
            _capturing = value;
            _from = null;
            MouseDefaultCursorShape = value ? CursorShape.Cross : CursorShape.PointingHand;
            QueueRedraw();
        }
    }

    /// <summary>Where the picture is drawn in the surface.</summary>
    private Rect2? Shown()
    {
        if (_engine.Texture is not { } texture)
            return null;
        var scale = Math.Min(Size.X / texture.GetWidth(), Size.Y / texture.GetHeight());
        var size = new Vector2(texture.GetWidth(), texture.GetHeight()) * scale;
        return new Rect2(Size / 2 - size / 2, size);
    }

    public VideoSurface(VideoEngine engine)
    {
        _engine = engine;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(480, 270);
        FocusMode = FocusModeEnum.Click;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ClipContents = true;
        engine.FrameShown += QueueRedraw;
        TreeExiting += () => engine.FrameShown -= QueueRedraw;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.015f, 0.017f, 0.022f));
        var center = Size / 2;
        if (_engine.Texture is { } texture && Shown() is { } shown)
            DrawTextureRect(texture, shown, false);
        if (_engine.Loading)
        {
            DrawString(Eve.Eve.Body, center + new Vector2(-60, 6), "loading…", HorizontalAlignment.Center, 120, 14, Eve.Eve.Muted);
            return;
        }
        if (!_capturing)
            return;
        // The capture: the hint, then the rectangle drawn (the rest dimmed, its size in pixels of the video).
        var hint = "✂  Draw the part of the picture to capture  ·  Esc cancels";
        var width = Eve.Eve.Body.GetStringSize(hint, HorizontalAlignment.Left, -1, 13).X + 24;
        DrawRect(new Rect2(center.X - width / 2, 10, width, 26), new Color(0, 0, 0, 0.7f));
        DrawString(Eve.Eve.Body, new Vector2(center.X - width / 2 + 12, 28), hint, HorizontalAlignment.Left, -1, 13, Eve.Eve.Cyan);
        if (_from is not { } from || Shown() is not { } area)
            return;
        var box = new Rect2(from, Vector2.Zero).Expand(_to).Intersection(area);
        var dim = new Color(0, 0, 0, 0.5f);
        DrawRect(new Rect2(area.Position, new Vector2(area.Size.X, box.Position.Y - area.Position.Y)), dim);
        DrawRect(new Rect2(area.Position.X, box.End.Y, area.Size.X, area.End.Y - box.End.Y), dim);
        DrawRect(new Rect2(area.Position.X, box.Position.Y, box.Position.X - area.Position.X, box.Size.Y), dim);
        DrawRect(new Rect2(box.End.X, box.Position.Y, area.End.X - box.End.X, box.Size.Y), dim);
        DrawRect(box, Eve.Eve.Cyan, false, 2);
        var label = $"{box.Size.X / area.Size.X * 100:0}% × {box.Size.Y / area.Size.Y * 100:0}%";
        DrawString(Eve.Eve.Mono, box.Position + new Vector2(4, -6), label, HorizontalAlignment.Left, -1, 11, Eve.Eve.Cyan);
    }

    /// <summary>The part drawn, in fractions of the picture.</summary>
    private (double X, double Y, double Width, double Height)? Part()
    {
        if (_from is not { } from || Shown() is not { } area)
            return null;
        var box = new Rect2(from, Vector2.Zero).Expand(_to).Intersection(area);
        if (box.Size.X < 6 || box.Size.Y < 6)
            return null;
        return ((box.Position.X - area.Position.X) / area.Size.X, (box.Position.Y - area.Position.Y) / area.Size.Y, box.Size.X / area.Size.X, box.Size.Y / area.Size.Y);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_capturing)
        {
            switch (@event)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                    _from = press.Position;
                    _to = press.Position;
                    break;
                case InputEventMouseMotion motion when _from is not null:
                    _to = motion.Position;
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release when _from is not null:
                    _to = release.Position;
                    var part = Part();
                    Capturing = false;
                    if (part is { } drawn)
                        Captured?.Invoke(drawn);
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                    Capturing = false;
                    break;
                default:
                    return;
            }
            QueueRedraw();
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            GrabFocus();
            _engine.Toggle();
            QueueRedraw();
            AcceptEvent();
        }
    }
}
