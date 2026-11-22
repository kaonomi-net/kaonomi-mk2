using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Dissemination;

/// <summary>The colours of the charts of /dissimination: the series of the searches, the kinds of entities, the emotions.</summary>
internal static class DisseminationColors
{
    /// <summary>The colour of the series of rank <paramref name="index"/>.</summary>
    public static Color Series(int index) => (index % DisseminationWindowViewModel.MostSearches) switch
    {
        0 => Eve.Eve.Cyan,
        1 => Eve.Eve.Amber,
        2 => Eve.Eve.Green,
        3 => new Color(0.66f, 0.47f, 0.93f),
        4 => Eve.Eve.Red,
        _ => new Color(0.2f, 0.72f, 0.68f)
    };

    public static Color Kind(string type) => type switch
    {
        "PERSON" => Eve.Eve.Cyan,
        "ORGANISATION" => Eve.Eve.Amber,
        "EVENT" => Eve.Eve.Green,
        _ => Eve.Eve.Muted
    };

    public static Color Emotion(string name) => name switch
    {
        "happy" => new Color(0.36f, 0.75f, 0.38f),
        "surprised" => new Color(0.95f, 0.66f, 0.24f),
        "neutral" => new Color(0.6f, 0.63f, 0.68f),
        "sad" => new Color(0.33f, 0.55f, 0.88f),
        "fearful" => new Color(0.62f, 0.45f, 0.86f),
        "angry" => new Color(0.88f, 0.31f, 0.29f),
        _ => new Color(0.55f, 0.6f, 0.25f)
    };

    /// <summary>From red (-1) through grey (0) to green (1).</summary>
    public static Color Valence(double valence)
    {
        var grey = new Color(0.6f, 0.63f, 0.68f);
        var t = (float)Math.Clamp(Math.Abs(valence), 0, 1);
        return grey.Lerp(valence < 0 ? new Color(0.88f, 0.31f, 0.29f) : new Color(0.36f, 0.75f, 0.38f), t);
    }
}

/// <summary>
/// The results per day: for each day of the period, one bar per search (its colour), as high as its number of
/// results; a dot above the bars of the search selected tells the mean valence about the entities named that day
/// (red: negative, green: positive). The day under the pointer is lit and told in a tooltip; a click on a bar selects
/// its search and filters the results on that day (a click on the day selected clears the filter).
/// </summary>
internal sealed partial class TimelineChart : Control
{
    private const float Left = 46, Right = 12, Top = 16, Bottom = 26;

    private readonly DisseminationWindowViewModel _vm;
    private int _hoverDay = -1, _hoverSearch = -1;

    public TimelineChart(DisseminationWindowViewModel vm)
    {
        _vm = vm;
        CustomMinimumSize = new Vector2(300, 250);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () =>
        {
            _hoverDay = _hoverSearch = -1;
            QueueRedraw();
        };
    }

    private Rect2 Plot => new(Left, Top, Math.Max(10, Size.X - Left - Right), Math.Max(10, Size.Y - Top - Bottom));

    private int Max => Math.Max(1, _vm.Series.Count == 0 ? 1 : _vm.Series.Max(s => s.Length == 0 ? 0 : s.Max()));

    /// <summary>A round number just above <paramref name="max"/>, with four steps under it.</summary>
    private static int Ceiling(int max)
    {
        if (max <= 4)
            return 4;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var step in new[] { 1, 2, 4, 5, 8, 10 })
            if (step * magnitude >= max)
                return (int)(step * magnitude);
        return max;
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var plot = Plot;
        var days = _vm.Days.Count;
        var top = Ceiling(Max);
        // The grid and its numbers.
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.End.Y - plot.Size.Y * i / 4f;
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Eve.Eve.Edge with { A = i == 0 ? 0.7f : 0.22f }, 1);
            DrawString(font, new Vector2(4, y + 4), (top * i / 4).ToString(), HorizontalAlignment.Right, Left - 10, 10, Eve.Eve.Muted);
        }
        if (days == 0 || _vm.Series.Count == 0 || _vm.Series.All(s => s.All(c => c == 0)))
        {
            DrawString(font, new Vector2(plot.Position.X, plot.Position.Y + plot.Size.Y / 2), "No result in this period", HorizontalAlignment.Center, plot.Size.X, 13, Eve.Eve.Muted);
            if (days == 0)
                return;
        }

        var slot = plot.Size.X / days;
        var selected = _vm.Searches.IndexOf(_vm.Selected);
        // The day filtered, then the day under the pointer.
        if (_vm.DayFilter is { } filtered && _vm.Days.IndexOf(filtered) is >= 0 and var at)
            DrawRect(new Rect2(plot.Position.X + at * slot, plot.Position.Y, slot, plot.Size.Y), DisseminationColors.Series(_vm.Selected.Color) with { A = 0.16f });
        if (_hoverDay >= 0 && _hoverDay < days)
            DrawRect(new Rect2(plot.Position.X + _hoverDay * slot, plot.Position.Y, slot, plot.Size.Y), Eve.Eve.Raised(0.07f));

        var series = _vm.Series.Count;
        var group = Math.Max(1f, slot * 0.82f);
        var bar = Math.Max(1f, group / series);
        for (var d = 0; d < days; d++)
        {
            var x = plot.Position.X + d * slot + (slot - group) / 2;
            for (var s = 0; s < series; s++)
            {
                var count = _vm.Series[s][d];
                if (count == 0)
                    continue;
                var height = Math.Max(2f, plot.Size.Y * count / top);
                var color = DisseminationColors.Series(_vm.Searches[s].Color);
                var lit = s == selected || (d == _hoverDay && s == _hoverSearch);
                DrawRect(new Rect2(x + s * bar, plot.End.Y - height, Math.Max(1f, bar - (bar > 4 ? 1 : 0)), height), color with { A = lit ? 1f : 0.55f });
                // The count above the bar, when there is room.
                if (bar >= 16 && days <= 45)
                    DrawString(font, new Vector2(x + s * bar, plot.End.Y - height - 3), count.ToString(), HorizontalAlignment.Center, bar, 10, Eve.Eve.Text);
            }
            // The valence of the day, for the search selected.
            if (d < _vm.Valence.Length && !double.IsNaN(_vm.Valence[d]) && selected >= 0 && _vm.Series[selected][d] > 0)
                DrawCircle(new Vector2(x + group / 2, plot.Position.Y - 6), Math.Clamp(slot * 0.18f, 2.5f, 5f), DisseminationColors.Valence(_vm.Valence[d]));
        }

        // The days under the axis: as many as fit.
        var every = Math.Max(1, (int)Math.Ceiling(days / Math.Max(1f, plot.Size.X / 62f)));
        for (var d = 0; d < days; d += every)
        {
            var label = _vm.Days[d].ToString(days > 60 ? "dd/MM" : "ddd dd/MM");
            DrawString(font, new Vector2(plot.Position.X + d * slot - 30 + slot / 2, plot.End.Y + 16), label, HorizontalAlignment.Center, 60, 10,
                _vm.DayFilter == _vm.Days[d] ? Eve.Eve.TextBright : Eve.Eve.Muted);
        }
    }

    /// <summary>The day and the search under <paramref name="position"/> (-1: none).</summary>
    private (int Day, int Search) At(Vector2 position)
    {
        var plot = Plot;
        var days = _vm.Days.Count;
        if (days == 0 || position.X < plot.Position.X || position.X >= plot.End.X)
            return (-1, -1);
        var slot = plot.Size.X / days;
        var day = Math.Clamp((int)((position.X - plot.Position.X) / slot), 0, days - 1);
        var series = _vm.Series.Count;
        var group = Math.Max(1f, slot * 0.82f);
        var inGroup = position.X - (plot.Position.X + day * slot + (slot - group) / 2);
        var search = series > 0 && inGroup >= 0 && inGroup < group ? Math.Clamp((int)(inGroup / (group / series)), 0, series - 1) : -1;
        return (day, search);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            var (day, search) = At(motion.Position);
            if (day != _hoverDay || search != _hoverSearch)
            {
                (_hoverDay, _hoverSearch) = (day, search);
                QueueRedraw();
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var (day, search) = At(click.Position);
            if (day < 0)
                return;
            // A click on a bar selects its search; elsewhere in the day, the search selected stays.
            var chosen = search >= 0 && search < _vm.Searches.Count && _vm.Series[search][day] > 0 ? _vm.Searches[search] : null;
            _vm.ToggleDay(_vm.Days[day], ReferenceEquals(chosen, _vm.Selected) ? null : chosen);
            AcceptEvent();
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var (day, _) = At(atPosition);
        if (day < 0 || day >= _vm.Days.Count)
            return string.Empty;
        var lines = new List<string> { _vm.Days[day].ToString("dddd d MMMM yyyy") };
        for (var s = 0; s < _vm.Searches.Count && s < _vm.Series.Count; s++)
            lines.Add($"{_vm.Searches[s].Label}: {_vm.Series[s][day]} result(s)");
        if (day < _vm.Valence.Length && !double.IsNaN(_vm.Valence[day]))
            lines.Add($"Valence about the entities named ({_vm.Selected.Label}): {_vm.Valence[day]:+0.00;-0.00;0.00}");
        lines.Add("Click: the results of this day");
        return string.Join("\n", lines);
    }
}

/// <summary>
/// A ranking drawn as bars: a name, its bar (as long as its count) and its count, one line each. The line under the
/// pointer is lit; a click chooses it (<see cref="Chosen"/>), a right click asks for more (<see cref="Asked"/>).
/// </summary>
internal sealed partial class RankBars : Control
{
    private const float Row = 22;

    private List<(string Name, int Count, Color Color)> _items = [];
    private string? _selected;
    private int _hover = -1;

    public RankBars()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () =>
        {
            _hover = -1;
            QueueRedraw();
        };
    }

    public event Action<string>? Chosen;

    public event Action<string>? Asked;

    /// <summary>What the tooltip of a line says under its name.</summary>
    public string Hint { get; set; } = string.Empty;

    public void Set(IEnumerable<(string Name, int Count, Color Color)> items, string? selected)
    {
        _items = items.ToList();
        _selected = selected;
        CustomMinimumSize = new Vector2(120, Math.Max(Row, _items.Count * Row));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        if (_items.Count == 0)
        {
            DrawString(font, new Vector2(0, 15), "None", HorizontalAlignment.Left, Size.X, 11, Eve.Eve.Muted);
            return;
        }
        var max = Math.Max(1, _items.Max(i => i.Count));
        for (var i = 0; i < _items.Count; i++)
        {
            var (name, count, color) = _items[i];
            var y = i * Row;
            var selected = name == _selected;
            if (i == _hover)
                DrawRect(new Rect2(0, y, Size.X, Row - 2), Eve.Eve.Raised(0.07f));
            DrawRect(new Rect2(0, y + 2, Math.Max(3, (Size.X - 44) * count / max), Row - 6), color with { A = selected ? 0.75f : 0.3f });
            if (selected)
                DrawRect(new Rect2(0, y + 2, 3, Row - 6), color);
            DrawString(font, new Vector2(8, y + 15), name, HorizontalAlignment.Left, Math.Max(20, Size.X - 56), 12, selected ? Eve.Eve.TextBright : Eve.Eve.Text);
            DrawString(font, new Vector2(Size.X - 42, y + 15), count.ToString(), HorizontalAlignment.Right, 40, 11, Eve.Eve.Muted);
        }
    }

    private int At(Vector2 position) => position.Y >= 0 && (int)(position.Y / Row) is var row && row < _items.Count ? row : -1;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            var row = At(motion.Position);
            if (row != _hover)
            {
                _hover = row;
                QueueRedraw();
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true } click && At(click.Position) is >= 0 and var at)
        {
            if (click.ButtonIndex == MouseButton.Left)
                Chosen?.Invoke(_items[at].Name);
            else if (click.ButtonIndex == MouseButton.Right)
                Asked?.Invoke(_items[at].Name);
            AcceptEvent();
        }
    }

    public override string _GetTooltip(Vector2 atPosition) =>
        At(atPosition) is >= 0 and var row ? $"{_items[row].Name}: {_items[row].Count} result(s)\n{Hint}" : string.Empty;
}

/// <summary>
/// The emotions about the entities named in the results: one bar cut in seven colours (the share of each emotion), the
/// shares under it, and the mean valence on a line from negative to positive.
/// </summary>
internal sealed partial class EmotionBar : Control
{
    private double[] _shares = [];
    private double _valence = double.NaN;

    public EmotionBar()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(160, 132);
    }

    public void Set(double[] shares, double valence)
    {
        _shares = shares;
        _valence = valence;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var names = DisseminationWindowViewModel.EmotionNames;
        if (_shares.Length != names.Length || _shares.Sum() <= 0)
        {
            DrawString(font, new Vector2(0, 15), "No emotional analysis for these results", HorizontalAlignment.Left, Size.X, 11, Eve.Eve.Muted);
            return;
        }
        float x = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var width = (float)(Size.X * _shares[i] / 100);
            if (width > 0)
                DrawRect(new Rect2(x, 0, width, 18), DisseminationColors.Emotion(names[i]));
            x += width;
        }
        // The shares, in two columns.
        for (var i = 0; i < names.Length; i++)
        {
            var column = i % 2;
            var position = new Vector2(column * Size.X / 2, 30 + (i / 2) * 17);
            DrawRect(new Rect2(position.X, position.Y - 1, 9, 9), DisseminationColors.Emotion(names[i]));
            DrawString(font, position + new Vector2(14, 8), $"{names[i]} {_shares[i]:0}%", HorizontalAlignment.Left, Size.X / 2 - 16, 11, _shares[i] >= 1 ? Eve.Eve.Text : Eve.Eve.Muted);
        }
        // The valence: negative on the left, positive on the right.
        if (double.IsNaN(_valence))
            return;
        const float y = 116;
        DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Eve.Eve.Edge with { A = 0.6f }, 2);
        DrawLine(new Vector2(Size.X / 2, y - 4), new Vector2(Size.X / 2, y + 4), Eve.Eve.Muted, 1);
        DrawCircle(new Vector2((float)(Size.X * (Math.Clamp(_valence, -1, 1) + 1) / 2), y), 6, DisseminationColors.Valence(_valence));
        DrawString(font, new Vector2(0, y - 8), "negative", HorizontalAlignment.Left, 80, 10, Eve.Eve.Muted);
        DrawString(font, new Vector2(Size.X - 80, y - 8), "positive", HorizontalAlignment.Right, 80, 10, Eve.Eve.Muted);
        DrawString(font, new Vector2(0, y - 8), $"valence {_valence:+0.00;-0.00;0.00}", HorizontalAlignment.Center, Size.X, 10, Eve.Eve.Text);
    }

    public override string _GetTooltip(Vector2 atPosition) =>
        "What the pages express about the entities named in the results (each mention counts once): the share of seven emotions and the mean valence, from the analyses";
}

/// <summary>Loads the thumbnails of the pictures off the main thread, two at a time; disposed with the window.</summary>
internal sealed class ThumbnailLoader : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(2);
    private readonly CancellationTokenSource _cancel = new();

    public void Load(string path, int size, Action<Texture2D> done)
    {
        var token = _cancel.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Gate.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                if (token.IsCancellationRequested || !File.Exists(path))
                    return;
                var image = new Image();
                if (image.Load(path) != Error.Ok || image.IsEmpty())
                    return;
                var scale = Math.Min(1.0, (double)size / Math.Max(image.GetWidth(), image.GetHeight()));
                if (scale < 1)
                    image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Bilinear);
                Bind.OnMain(() =>
                {
                    if (!token.IsCancellationRequested)
                        done(ImageTexture.CreateFromImage(image));
                });
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // No thumbnail: the picture stays empty.
            }
            finally
            {
                Gate.Release();
            }
        });
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }
}

/// <summary>
/// A small carousel of the pictures tied to a paragraph: one picture at a time, ◀ and ▶ to go to the previous and the
/// next one (the wheel of the mouse too), its rank, its caption; a click opens it at its real size.
/// </summary>
internal sealed partial class PictureCarousel : HBoxContainer
{
    private readonly IReadOnlyList<DisseminationPicture> _pictures;
    private readonly ThumbnailLoader _loader;
    private readonly Action<string> _open;
    private readonly TextureRect _picture;
    private readonly Button _frame;
    private readonly Label _caption;
    private readonly Dictionary<int, Texture2D> _textures = [];
    private int _index;

    public PictureCarousel(IReadOnlyList<DisseminationPicture> pictures, ThumbnailLoader loader, Action<string> open, Vector2? size = null)
    {
        _pictures = pictures;
        _loader = loader;
        _open = open;
        AddThemeConstantOverride("separation", 6);
        var frameSize = size ?? new Vector2(232, 132);

        _frame = new Button
        {
            CustomMinimumSize = frameSize,
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            ClipContents = true,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _frame.AddThemeStyleboxOverride("normal", Eve.Eve.Box(Eve.Eve.Sunken(0.25f), Eve.Eve.Edge with { A = 0.35f }, 2));
        _frame.AddThemeStyleboxOverride("hover", Eve.Eve.Box(Eve.Eve.Sunken(0.25f), Eve.Eve.Cyan, 2));
        _frame.AddThemeStyleboxOverride("pressed", Eve.Eve.Box(Eve.Eve.Sunken(0.35f), Eve.Eve.Cyan, 2));
        _picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _picture.OffsetLeft = _picture.OffsetTop = 2;
        _picture.OffsetRight = _picture.OffsetBottom = -2;
        _frame.AddChild(_picture);
        _frame.Pressed += () => _open(_pictures[_index].Path);
        _frame.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true } wheel && _pictures.Count > 1 && wheel.ButtonIndex is MouseButton.WheelLeft or MouseButton.WheelRight)
            {
                Show(_index + (wheel.ButtonIndex == MouseButton.WheelRight ? 1 : -1));
                _frame.AcceptEvent();
            }
        };

        _caption = Eve.Eve.Text_(string.Empty, Eve.Eve.Muted, 11, wrap: true);
        _caption.CustomMinimumSize = new Vector2(150, 0);
        _caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _caption.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        var previous = Arrow("◀", "The previous picture", () => Show(_index - 1));
        var next = Arrow("▶", "The next picture", () => Show(_index + 1));
        previous.Visible = next.Visible = pictures.Count > 1;
        AddChild(previous);
        AddChild(_frame);
        AddChild(next);
        AddChild(_caption);
        Show(0);
    }

    private static Button Arrow(string text, string tooltip, Action action)
    {
        var button = Eve.Eve.Button(text, false, tooltip);
        button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        button.FocusMode = FocusModeEnum.None;
        button.Pressed += action;
        return button;
    }

    private void Show(int index)
    {
        if (_pictures.Count == 0)
            return;
        _index = ((index % _pictures.Count) + _pictures.Count) % _pictures.Count;
        var picture = _pictures[_index];
        var lines = new List<string> { $"{_index + 1} / {_pictures.Count}" };
        if (picture.Alt.Length > 0 && picture.Alt != "og:image")
            lines.Add(picture.Alt);
        if (picture.Description.Length > 0)
            lines.Add(picture.Description);
        _caption.Text = string.Join("\n", lines);
        _frame.TooltipText = string.Join("\n", lines.Skip(1).Append(picture.Url.Length > 0 ? picture.Url : picture.Path).Append("Click: open it at its real size in the image analysis"));
        _picture.Texture = _textures.GetValueOrDefault(_index);
        if (_textures.ContainsKey(_index) || !picture.Exists)
        {
            if (!picture.Exists)
                _caption.Text += "\n(the file is not on this machine)";
            return;
        }
        var wanted = _index;
        _loader.Load(picture.Path, 480, texture =>
        {
            if (!IsInstanceValid(this))
                return;
            _textures[wanted] = texture;
            if (_index == wanted)
                _picture.Texture = texture;
        });
    }
}
