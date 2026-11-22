using System;
using System.Linq;
using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Video;

/// <summary>
/// The timeline of the player: the marks of the notes (a bar for a part of the video, a diamond for a time, in the
/// colour of their kind), the ruler, the strip of pictures of the video, the selection (bright, its two handles to
/// drag, the rest dimmed) and the playhead. A click goes to that time; a drag selects a part of the video; a drag on a
/// handle moves it; a right click clears the selection; a double click on a mark goes to its note. Over the strip, the
/// picture of that time grows above the pointer.
/// </summary>
public sealed partial class VideoTimeline : Control
{
    private const float Side = 14, LaneTop = 4, LaneHeight = 14, RulerTop = 22, RulerHeight = 16, TrackTop = 40;

    private enum Drag { None, Pending, Creating, Start, End }

    private readonly VideoPlayerWindowViewModel _vm;
    private readonly TextureRect _preview;
    private readonly Label _previewTime;
    private readonly PanelContainer _previewPanel;
    private Texture2D? _strip;
    private Drag _drag;
    private float _pressX;
    private double _pressTime;
    private float? _hover;

    public VideoTimeline(VideoPlayerWindowViewModel vm)
    {
        _vm = vm;
        CustomMinimumSize = new Vector2(0, 122);
        FocusMode = FocusModeEnum.Click;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = string.Empty;
        _preview = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, CustomMinimumSize = new Vector2(208, 117) };
        _previewTime = Eve.Eve.Text_("", Eve.Eve.TextBright, 11);
        _previewTime.AddThemeFontOverride("font", Eve.Eve.Mono);
        _previewTime.HorizontalAlignment = HorizontalAlignment.Center;
        _previewPanel = new PanelContainer { TopLevel = true, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _previewPanel.AddThemeStyleboxOverride("panel", Eve.Eve.Box(new Color(0, 0, 0, 0.85f), Eve.Eve.Cyan with { A = 0.8f }, 3));
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        column.AddChild(_preview);
        column.AddChild(_previewTime);
        _previewPanel.AddChild(column);
        AddChild(_previewPanel);
        Bind.When(this, vm, LoadStrip, nameof(vm.StripPath));
        Bind.When(this, vm, QueueRedraw, nameof(vm.SelectionStart), nameof(vm.SelectionEnd), nameof(vm.Probe));
        vm.NotesChanged += QueueRedraw;
        TreeExiting += () => vm.NotesChanged -= QueueRedraw;
    }

    private void LoadStrip()
    {
        if (_vm.StripPath.Length == 0 || !System.IO.File.Exists(_vm.StripPath))
            return;
        var image = Image.LoadFromFile(_vm.StripPath);
        if (image is null || image.IsEmpty())
            return;
        _strip = ImageTexture.CreateFromImage(image);
        QueueRedraw();
    }

    private Rect2 Track => new(Side, TrackTop, Math.Max(1, Size.X - 2 * Side), Math.Max(20, Size.Y - TrackTop - 6));

    private float X(double seconds) => Track.Position.X + (float)(Math.Clamp(seconds / Math.Max(0.001, _vm.Duration), 0, 1)) * Track.Size.X;

    private double TimeAt(float x) => Math.Clamp((x - Track.Position.X) / Track.Size.X, 0, 1) * _vm.Duration;

    private static Color ColorOf(string kind) => kind switch
    {
        "segment" => Eve.Eve.Cyan,
        "frame" => Eve.Eve.Amber,
        "sound" => new Color(0.66f, 0.5f, 1f),
        "picture" => Eve.Eve.Green,
        _ => new Color(1f, 0.47f, 0.72f)
    };

    private VideoNoteViewModel? NoteAt(float x) =>
        _vm.AllNotes.FirstOrDefault(n => n.IsPoint ? Math.Abs(X(n.Document.Start) - x) <= 6 : x >= X(n.Document.Start) - 2 && x <= X(n.Document.End) + 2);

    public override void _Draw()
    {
        var size = Size;
        var track = Track;
        DrawRect(new Rect2(Vector2.Zero, size), Eve.Eve.Sunken(0.4f));
        DrawRect(new Rect2(Vector2.Zero, size), Eve.Eve.Edge with { A = 0.35f }, false, 1);
        var duration = Math.Max(0.001, _vm.Duration);

        // The marks of the notes.
        foreach (var note in _vm.AllNotes.OrderBy(n => n.IsPoint))
        {
            var color = ColorOf(note.Kind);
            if (note.IsPoint)
            {
                var x = X(note.Document.Start);
                var y = LaneTop + LaneHeight / 2;
                DrawColoredPolygon([new Vector2(x, y - 6), new Vector2(x + 5, y), new Vector2(x, y + 6), new Vector2(x - 5, y)], color);
                DrawLine(new Vector2(x, y + 6), new Vector2(x, track.Position.Y), color with { A = 0.35f }, 1);
            }
            else
            {
                var from = X(note.Document.Start);
                var to = Math.Max(from + 3, X(note.Document.End));
                DrawRect(new Rect2(from, LaneTop + 3, to - from, LaneHeight - 6), color with { A = 0.85f });
            }
        }

        // The ruler: about one label every 90 pixels.
        var steps = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
        var step = steps.FirstOrDefault(s => s / duration * track.Size.X >= 90, 3600);
        for (var t = 0.0; t <= duration + 0.001; t += step)
        {
            var x = X(t);
            DrawLine(new Vector2(x, RulerTop + RulerHeight - 6), new Vector2(x, RulerTop + RulerHeight), Eve.Eve.Muted with { A = 0.7f }, 1);
            DrawString(Eve.Eve.Mono, new Vector2(x + 3, RulerTop + 10), Short(t), HorizontalAlignment.Left, -1, 10, Eve.Eve.Muted);
            for (var minor = 1; minor < 5; minor++)
            {
                var mx = X(t + step * minor / 5);
                if (mx < track.End.X)
                    DrawLine(new Vector2(mx, RulerTop + RulerHeight - 3), new Vector2(mx, RulerTop + RulerHeight), Eve.Eve.Muted with { A = 0.35f }, 1);
            }
        }

        // The strip of pictures.
        if (_strip is not null)
            DrawTextureRect(_strip, track, false, new Color(1, 1, 1, 0.92f));
        else
        {
            DrawRect(track, Eve.Eve.Raised(0.03f));
            DrawString(Eve.Eve.Body, new Vector2(track.Position.X + 10, track.Position.Y + track.Size.Y / 2 + 4), "preparing the strip of pictures…", HorizontalAlignment.Left, -1, 11, Eve.Eve.Muted);
        }
        DrawRect(track, Eve.Eve.Edge with { A = 0.5f }, false, 1);

        // The selection: the rest dimmed, its edges and handles bright.
        if (_vm.SelectionStart is { } start && _vm.SelectionEnd is { } end)
        {
            var from = X(start);
            var to = X(end);
            var dim = new Color(0, 0, 0, 0.55f);
            DrawRect(new Rect2(track.Position.X, track.Position.Y, from - track.Position.X, track.Size.Y), dim);
            DrawRect(new Rect2(to, track.Position.Y, track.End.X - to, track.Size.Y), dim);
            DrawRect(new Rect2(from, track.Position.Y, to - from, track.Size.Y), Eve.Eve.Cyan with { A = 0.08f });
            DrawRect(new Rect2(from, track.Position.Y - 1, to - from, track.Size.Y + 2), Eve.Eve.Cyan, false, 2);
            foreach (var x in new[] { from, to })
            {
                var handle = new Rect2(x - 4, track.Position.Y - 4, 8, track.Size.Y + 8);
                DrawRect(handle, Eve.Eve.Cyan);
                for (var i = -1; i <= 1; i++)
                    DrawLine(new Vector2(x - 1.5f, handle.Position.Y + handle.Size.Y / 2 + i * 4), new Vector2(x + 1.5f, handle.Position.Y + handle.Size.Y / 2 + i * 4), new Color(0, 0, 0, 0.6f), 1);
            }
            var label = VideoMedia.Length(end - start);
            var width = Eve.Eve.Mono.GetStringSize(label, HorizontalAlignment.Left, -1, 10).X + 10;
            var center = Math.Clamp((from + to) / 2 - width / 2, track.Position.X, track.End.X - width);
            DrawRect(new Rect2(center, RulerTop, width, 14), Eve.Eve.Cyan);
            DrawString(Eve.Eve.Mono, new Vector2(center + 5, RulerTop + 11), label, HorizontalAlignment.Left, -1, 10, new Color(0.02f, 0.05f, 0.08f));
        }

        // A start or an end alone (I or O): its flag.
        if (_vm.SelectionStart is null ^ _vm.SelectionEnd is null)
        {
            var alone = (_vm.SelectionStart ?? _vm.SelectionEnd)!.Value;
            var isStart = _vm.SelectionStart is not null;
            var x = X(alone);
            DrawLine(new Vector2(x, RulerTop), new Vector2(x, track.End.Y + 3), Eve.Eve.Cyan, 2);
            var flag = isStart ? "IN" : "OUT";
            var width = Eve.Eve.Mono.GetStringSize(flag, HorizontalAlignment.Left, -1, 10).X + 10;
            var left = isStart ? x : x - width;
            DrawRect(new Rect2(left, RulerTop, width, 14), Eve.Eve.Cyan);
            DrawString(Eve.Eve.Mono, new Vector2(left + 5, RulerTop + 11), flag, HorizontalAlignment.Left, -1, 10, new Color(0.02f, 0.05f, 0.08f));
        }

        // The pointer.
        if (_hover is { } hover && _drag is Drag.None or Drag.Pending)
            DrawLine(new Vector2(hover, RulerTop), new Vector2(hover, track.End.Y), Eve.Eve.TextBright with { A = 0.35f }, 1);

        // The playhead.
        var head = X(_vm.Position);
        DrawLine(new Vector2(head, RulerTop + 4), new Vector2(head, track.End.Y + 3), Colors.White, 2);
        DrawColoredPolygon([new Vector2(head - 6, RulerTop), new Vector2(head + 6, RulerTop), new Vector2(head, RulerTop + 8)], Colors.White);
    }

    private static string Short(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : seconds < 60 && seconds % 1 != 0 ? $"{seconds:0.0}" : $"{span.Minutes}:{span.Seconds:00}";
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_vm.Duration <= 0)
            return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
            {
                GrabFocus();
                var x = press.Position.X;
                if (press.DoubleClick && press.Position.Y < RulerTop && NoteAt(x) is { } note)
                {
                    _vm.GoTo(note);
                    _drag = Drag.None;
                    break;
                }
                if (_vm.SelectionStart is { } start && Math.Abs(X(start) - x) <= 7)
                    _drag = Drag.Start;
                else if (_vm.SelectionEnd is { } end && Math.Abs(X(end) - x) <= 7)
                    _drag = Drag.End;
                else
                {
                    _drag = Drag.Pending;
                    _pressX = x;
                    _pressTime = TimeAt(x);
                }
                AcceptEvent();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release:
                if (_drag == Drag.Pending)
                    _vm.Seek(TimeAt(release.Position.X));
                _drag = Drag.None;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _vm.ClearSelectionCommand.Execute(null);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
            {
                var x = motion.Position.X;
                _hover = x;
                var time = TimeAt(x);
                switch (_drag)
                {
                    case Drag.Pending when Math.Abs(x - _pressX) > 4:
                        _drag = Drag.Creating;
                        _vm.Select(_pressTime, time);
                        break;
                    case Drag.Creating:
                        _vm.Select(_pressTime, time);
                        break;
                    case Drag.Start when _vm.SelectionEnd is { } end:
                        if (time > end)
                        {
                            _drag = Drag.End;
                            _vm.Select(end, time);
                        }
                        else
                            _vm.Select(time, end);
                        break;
                    case Drag.End when _vm.SelectionStart is { } start:
                        if (time < start)
                        {
                            _drag = Drag.Start;
                            _vm.Select(time, start);
                        }
                        else
                            _vm.Select(start, time);
                        break;
                }
                var nearHandle = _vm.SelectionStart is { } s && Math.Abs(X(s) - x) <= 7 || _vm.SelectionEnd is { } e && Math.Abs(X(e) - x) <= 7;
                MouseDefaultCursorShape = nearHandle || _drag is Drag.Start or Drag.End ? CursorShape.Hsize : CursorShape.PointingHand;
                ShowPreview(motion.Position);
                QueueRedraw();
                break;
            }
        }
    }

    /// <summary>The picture of the time under the pointer (its cell of the strip), above the timeline; the note there, if any.</summary>
    private void ShowPreview(Vector2 at)
    {
        var time = TimeAt(at.X);
        var note = at.Y < RulerTop ? NoteAt(at.X) : null;
        _previewTime.Text = note is null ? VideoMedia.Clock(time) : $"{note.Icon} {note.Document.Title}";
        if (_strip is { } strip && note is null)
        {
            const int cells = 60;
            var cell = strip.GetWidth() / (float)cells;
            var index = Math.Clamp((int)(time / Math.Max(0.001, _vm.Duration) * cells), 0, cells - 1);
            _preview.Texture = new AtlasTexture { Atlas = strip, Region = new Rect2(index * cell, 0, cell, strip.GetHeight()) };
            _preview.CustomMinimumSize = new Vector2(208, 208 * strip.GetHeight() / Math.Max(1, cell));
            _preview.Visible = true;
        }
        else
            _preview.Visible = false;
        _previewPanel.Visible = true;
        _previewPanel.ResetSize();
        var panel = _previewPanel.Size;
        var global = GetGlobalTransformWithCanvas() * new Vector2(at.X, 0);
        var viewport = GetViewportRect().Size;
        _previewPanel.Position = new Vector2(Math.Clamp(global.X - panel.X / 2, 4, viewport.X - panel.X - 4), Math.Max(4, global.Y - panel.Y - 8));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            _hover = null;
            _previewPanel.Visible = false;
            QueueRedraw();
        }
    }
}
