using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Tools.Dissemination;
using Day = KAONOMI.Chat.ViewModels.EntityAnalysisWindowViewModel.Day;

namespace KAONOMI.ChatGodot.Tools.EntityAnalysis;

/// <summary>
/// The days of /entity, the oldest on the left: for each day a strip of its emotions (the share of each, in their
/// colours; grey while it is not analysed), a bar as high as the passages that name the entity, and the valence of the
/// day on a line from negative (low, red) to positive (high, green). The day under the pointer is told in a tooltip; a
/// click shows its card.
/// </summary>
internal sealed partial class EntityTimeline : Control
{
    private const float Left = 34, Right = 10, Top = 8, Strip = 14, Gap = 8, ValenceBand = 46, Bottom = 22;

    private readonly EntityAnalysisWindowViewModel _vm;
    private int _hover = -1;

    public EntityTimeline(EntityAnalysisWindowViewModel vm)
    {
        _vm = vm;
        CustomMinimumSize = new Vector2(300, 190);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () =>
        {
            _hover = -1;
            QueueRedraw();
        };
    }

    /// <summary>A day was clicked.</summary>
    public event Action<Day>? Chosen;

    private List<Day> Days => _vm.Days.OrderBy(d => d.Date).ToList();

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var days = Days;
        if (days.Count == 0)
        {
            DrawString(font, new Vector2(Left, Size.Y / 2), "No day collected names this entity", HorizontalAlignment.Left, Size.X - Left, 13, Eve.Eve.Muted);
            return;
        }
        var width = Size.X - Left - Right;
        var slot = width / days.Count;
        var column = Math.Clamp(slot * 0.7f, 3, 60);
        var barsTop = Top + Strip + Gap;
        var barsBottom = Size.Y - Bottom - ValenceBand - Gap;
        var valenceTop = barsBottom + Gap;
        var max = Math.Max(1, days.Max(d => d.Passages.Count));

        // The scale of the passages and the band of the valence.
        DrawString(font, new Vector2(2, barsTop + 9), max.ToString(), HorizontalAlignment.Right, Left - 8, 10, Eve.Eve.Muted);
        DrawString(font, new Vector2(2, barsBottom), "0", HorizontalAlignment.Right, Left - 8, 10, Eve.Eve.Muted);
        DrawLine(new Vector2(Left, barsBottom), new Vector2(Size.X - Right, barsBottom), Eve.Eve.Edge with { A = 0.6f }, 1);
        var middle = valenceTop + ValenceBand / 2;
        DrawLine(new Vector2(Left, middle), new Vector2(Size.X - Right, middle), Eve.Eve.Edge with { A = 0.35f }, 1);
        DrawString(font, new Vector2(2, valenceTop + 9), "+", HorizontalAlignment.Right, Left - 8, 10, DisseminationColors.Valence(1));
        DrawString(font, new Vector2(2, valenceTop + ValenceBand), "−", HorizontalAlignment.Right, Left - 8, 10, DisseminationColors.Valence(-1));

        var points = new List<Vector2>();
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var x = Left + i * slot + (slot - column) / 2;
            if (i == _hover)
                DrawRect(new Rect2(Left + i * slot, Top - 4, slot, Size.Y - Top - Bottom + 4), Eve.Eve.Raised(0.07f));

            // The strip of the emotions.
            if (day.Analysed)
            {
                float y = 0;
                for (var e = 0; e < DisseminationWindowViewModel.EmotionNames.Length; e++)
                {
                    var share = (float)(day.Emotions[e] / 100);
                    if (share <= 0)
                        continue;
                    DrawRect(new Rect2(x, Top + y * Strip, column, share * Strip), DisseminationColors.Emotion(DisseminationWindowViewModel.EmotionNames[e]));
                    y += share;
                }
            }
            else
                DrawRect(new Rect2(x, Top, column, Strip), day.State == EntityAnalysisWindowViewModel.DayState.Failed ? Eve.Eve.Red with { A = 0.5f } : Eve.Eve.Muted with { A = 0.25f });

            // The passages: the bar in the colour of the leading emotion.
            var height = Math.Max(2, (barsBottom - barsTop) * day.Passages.Count / max);
            var color = day.Analysed ? DisseminationColors.Emotion(day.Leading) : Eve.Eve.Cyan;
            DrawRect(new Rect2(x, barsBottom - height, column, height), color with { A = i == _hover ? 0.95f : 0.7f });
            if (column >= 18)
                DrawString(font, new Vector2(x, barsBottom - height - 3), day.Passages.Count.ToString(), HorizontalAlignment.Center, column, 10, Eve.Eve.Text);

            // The valence.
            if (day.Analysed)
                points.Add(new Vector2(x + column / 2, middle - (float)day.Valence * ValenceBand / 2));
        }
        if (points.Count > 1)
            DrawPolyline(points.ToArray(), Eve.Eve.Muted with { A = 0.7f }, 1.5f, true);
        foreach (var (point, day) in points.Zip(days.Where(d => d.Analysed)))
            DrawCircle(point, 4, DisseminationColors.Valence(day.Valence));

        // The days under the axis: as many as fit.
        var every = Math.Max(1, (int)Math.Ceiling(days.Count / Math.Max(1f, width / 58f)));
        for (var i = 0; i < days.Count; i += every)
            DrawString(font, new Vector2(Left + i * slot + slot / 2 - 30, Size.Y - 6), days[i].Date.ToString(days.Count > 14 ? "dd/MM" : "ddd dd/MM"),
                HorizontalAlignment.Center, 60, 10, i == _hover ? Eve.Eve.TextBright : Eve.Eve.Muted);
    }

    /// <summary>A text cut into lines of about <paramref name="width"/> characters, at the spaces (a tooltip does not wrap by itself).</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0)
            yield return line.ToString();
    }

    private int At(Vector2 position)
    {
        var days = _vm.Days.Count;
        if (days == 0 || position.X < Left || position.X >= Size.X - Right)
            return -1;
        return Math.Clamp((int)((position.X - Left) / ((Size.X - Left - Right) / days)), 0, days - 1);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && At(motion.Position) is var hover && hover != _hover)
        {
            _hover = hover;
            QueueRedraw();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && At(click.Position) is >= 0 and var at)
        {
            Chosen?.Invoke(Days[at]);
            AcceptEvent();
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (At(atPosition) is not (>= 0 and var at))
            return string.Empty;
        var day = Days[at];
        var lines = new List<string>
        {
            day.Date.ToString("dddd d MMMM yyyy"),
            $"{day.Passages.Count} passage(s) in {day.Pages} page(s): {string.Join(", ", day.Sources.Select(s => $"{s.Website} {s.Passages}"))}"
        };
        if (day.Analysed)
        {
            lines.Add($"{day.Leading} · valence {day.Valence:+0.00;-0.00;0.00} · intensity {day.Intensity:0.00}");
            if (day.Summary.Length > 0)
                lines.AddRange(Wrap(day.Summary.Length > 320 ? day.Summary[..320] + "…" : day.Summary, 80));
        }
        else
            lines.Add(day.State switch
            {
                EntityAnalysisWindowViewModel.DayState.Reading => "Being read by the model…",
                EntityAnalysisWindowViewModel.DayState.Failed => "Not analysed: " + day.Error,
                _ => "Not analysed yet"
            });
        lines.Add("Click: its card");
        return string.Join("\n", lines);
    }
}
