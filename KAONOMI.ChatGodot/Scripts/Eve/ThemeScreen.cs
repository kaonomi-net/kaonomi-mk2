using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The theme chosen in this Godot chat, kept in <c>preferences.json</c> of its data folder (the Avalonia chat keeps its
/// own theme in config.yml: the two apps do not change each other's look).
/// </summary>
public static class Preferences
{
    private sealed record Data(string? Theme, string? Wallpaper = null, bool SetupDone = false, string[]? RecentWindows = null, string? AudioInput = null, string? AudioOutput = null, string? GraphQlModel = null, bool GraphFlat = false, string[]? GraphHiddenKinds = null);

    private static string FilePath => ProjectSettings.GlobalizePath("user://preferences.json");

    /// <summary>The name of the theme chosen last (null: the default, EVE).</summary>
    public static string? Theme
    {
        get => Load().Theme;
        set => Save(Load() with { Theme = value });
    }

    /// <summary>The picture behind the windows of the desktop (a copy in the data folder; null: none).</summary>
    public static string? Wallpaper
    {
        get => Load().Wallpaper;
        set => Save(Load() with { Wallpaper = value });
    }

    /// <summary>True once DONE was pressed on the screen of the dependencies (shown at the first start until then).</summary>
    public static bool SetupDone
    {
        get => Load().SetupDone;
        set => Save(Load() with { SetupDone = value });
    }

    /// <summary>The microphone chosen with /audiosettings (null: the default one of the system).</summary>
    public static string? AudioInput
    {
        get => Load().AudioInput;
        set => Save(Load() with { AudioInput = value });
    }

    /// <summary>The output chosen with /audiosettings (null: the default one of the system).</summary>
    public static string? AudioOutput
    {
        get => Load().AudioOutput;
        set => Save(Load() with { AudioOutput = value });
    }

    /// <summary>The model chosen last to write the GraphQL queries of /graphbase (✨ TO GRAPHQL; null: the one of the chat).</summary>
    public static string? GraphQlModel
    {
        get => Load().GraphQlModel;
        set => Save(Load() with { GraphQlModel = value });
    }

    /// <summary>The graph of /graphbase was left in its 2D mode (▦ 2D): it opens in it.</summary>
    public static bool GraphFlat
    {
        get => Load().GraphFlat;
        set => Save(Load() with { GraphFlat = value });
    }

    /// <summary>The kinds of nodes unticked in the legend of /graphbase (their spheres 90 % transparent): kept from one opening to the next.</summary>
    public static IReadOnlyList<string> GraphHiddenKinds
    {
        get => Load().GraphHiddenKinds ?? [];
        set => Save(Load() with { GraphHiddenKinds = value.Distinct(StringComparer.Ordinal).ToArray() });
    }

    /// <summary>The commands of the tool windows opened last (/intel, /mindmap…), the newest first (10 at most).</summary>
    public static IReadOnlyList<string> RecentWindows =>
        // A former name (/vision:face, now /vision) is shown and opened under its name of today.
        (Load().RecentWindows ?? []).Select(KAONOMI.Chat.ViewModels.MainViewModel.CurrentCommand).Distinct().ToList();

    /// <summary>The tool window of <paramref name="command"/> was opened: it goes first in <see cref="RecentWindows"/>.</summary>
    public static void AddRecentWindow(string command)
    {
        // A chat started for a test or a screenshot (--no-restore) leaves the windows of the user as they were.
        if (OS.GetCmdlineUserArgs().Contains("--no-restore"))
            return;
        var data = Load();
        var recent = new List<string> { command };
        recent.AddRange((data.RecentWindows ?? []).Select(KAONOMI.Chat.ViewModels.MainViewModel.CurrentCommand)
            .Where(c => c != command).Distinct().Take(9));
        Save(data with { RecentWindows = [.. recent] });
    }

    /// <summary>The data folder of the chat (where the copy of the wallpaper is kept).</summary>
    public static string Folder => Path.GetDirectoryName(FilePath)!;

    private static Data Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(null) : new Data(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Data(null);
        }
    }

    private static void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning("The preferences were not saved: " + ex.Message);
        }
    }
}

/// <summary>
/// The screen of the themes (🎨 at the bottom of the bar of commands): three cards side by side, one per theme, with a
/// small preview of its colours (its page, a panel with a title, a text and a button, its accent), its name and
/// description, and Apply (the interface is built again in that theme at once; the windows open come back as they were).
/// A search keeps the themes holding its words (Enter applies the first one); ‹ › or the arrow keys ← → move along the themes, one card at a time, horizontally only. Its window has a fixed size.
/// </summary>
public static partial class ThemeScreen
{
    /// <summary>The key of its window.</summary>
    public const string Key = "themes";

    /// <summary>The cards shown at once.</summary>
    public const int Shown = 3;

    /// <summary>The width and height of a card, the space between two.</summary>
    public const float CardWidth = 300, CardHeight = 318, Gap = 14;

    /// <summary>The size of the window of the screen (fixed: it cannot be resized).</summary>
    public static Vector2 WindowSize => new(Shown * CardWidth + (Shown - 1) * Gap + 2 * 44 + 2 * 10 + 130, CardHeight + 200);

    /// <summary>Builds the screen; <paramref name="apply"/> applies the theme chosen.</summary>
    public static Control Build(Action<Palette> apply) => new Carousel(apply);

    /// <summary>
    /// Moves the themes of <paramref name="screen"/> (built by <see cref="Build"/>) by <paramref name="step"/> cards:
    /// the arrow keys ← →, handled by the chat while the window of the themes is the active one. False when it is not
    /// such a screen.
    /// </summary>
    public static bool Step(Control screen, int step)
    {
        if (screen is not Carousel carousel || carousel.IsTyping)
            return false;
        carousel.Move(step);
        return true;
    }

    /// <summary>The cards in a strip seen through a window of three, moved by ‹ › or ← →.</summary>
    private sealed partial class Carousel : VBoxContainer
    {
        private readonly HBoxContainer _strip = new() { MouseFilter = MouseFilterEnum.Pass };
        private readonly Button _previous = Eve.Button("‹", tooltip: "The themes before (←)");
        private readonly Button _next = Eve.Button("›", tooltip: "The themes after (→)");
        private readonly Label _where = Eve.Text_("", Eve.Muted, 12);
        private readonly LineEdit _search = new() { PlaceholderText = "🔎 Search a theme: its name, light or dark, a colour, a word of its description…", ClearButtonEnabled = true };
        private readonly Action<Palette> _apply = null!;
        private List<Palette> _palettes = [];
        private int _first;
        private Tween? _slide;

        // Godot builds its scripts with this constructor.
        public Carousel()
        {
        }

        public Carousel(Action<Palette> apply)
        {
            _apply = apply;
            AddThemeConstantOverride("separation", 12);
            AddChild(Eve.Caption("Choose the look of the chat", Eve.TextBright, 13));
            AddChild(Eve.Text_("The theme is kept for the next start. The windows open stay open.", Eve.Muted, 12, wrap: true));
            // The search: the themes whose name or description hold every word typed.
            _search.TextChanged += _ => Filter();
            _search.TextSubmitted += _ =>
            {
                // Enter: the first theme found is applied.
                if (_palettes.Count > 0 && _palettes[_first] != Eve.Current)
                    _apply(_palettes[_first]);
            };
            AddChild(_search);

            _strip.AddThemeConstantOverride("separation", (int)Gap);
            // The strip is seen through a window three cards wide: what is past its sides is cut.
            var view = new Control
            {
                ClipContents = true,
                CustomMinimumSize = new Vector2(Shown * CardWidth + (Shown - 1) * Gap, CardHeight),
                MouseFilter = MouseFilterEnum.Pass
            };
            view.AddChild(_strip);
            foreach (var arrow in new[] { _previous, _next })
            {
                arrow.CustomMinimumSize = new Vector2(44, 0);
                arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                arrow.FocusMode = FocusModeEnum.None;
                arrow.AddThemeFontSizeOverride("font_size", 26);
            }
            _previous.Pressed += () => Move(-1);
            _next.Pressed += () => Move(1);
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(_previous);
            row.AddChild(view);
            row.AddChild(_next);
            AddChild(row);
            _where.HorizontalAlignment = HorizontalAlignment.Center;
            AddChild(_where);

            // The theme in use is shown at the start, in the middle when it can be.
            Fill(Eve.Palettes.ToList());
            var current = Math.Max(0, _palettes.IndexOf(Eve.Current));
            _first = Math.Clamp(current - 1, 0, Last);
            _strip.Position = new Vector2(-_first * (CardWidth + Gap), 0);
            Refresh();
        }

        /// <summary>True while the search has the keyboard: the arrows move its cursor, not the themes.</summary>
        public bool IsTyping => _search.HasFocus();

        private int Last => Math.Max(0, _palettes.Count - Shown);

        /// <summary>The themes of the search, from the first.</summary>
        private void Filter()
        {
            var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Fill(Eve.Palettes.Where(p => words.All(w => $"{p.Name} {p.Description} {(p.IsLight ? "light clair" : "dark sombre")}".Contains(w, StringComparison.OrdinalIgnoreCase))).ToList());
            _first = 0;
            _slide?.Kill();
            _strip.Position = Vector2.Zero;
            Refresh();
        }

        private void Fill(List<Palette> palettes)
        {
            _palettes = palettes;
            foreach (var child in _strip.GetChildren())
            {
                _strip.RemoveChild(child);
                child.QueueFree();
            }
            foreach (var palette in palettes)
                _strip.AddChild(Card(palette, _apply));
            if (palettes.Count == 0)
            {
                var none = Eve.Text_($"No theme holds \"{_search.Text.Trim()}\".", Eve.Muted, 13);
                none.CustomMinimumSize = new Vector2(Shown * CardWidth, CardHeight);
                none.HorizontalAlignment = HorizontalAlignment.Center;
                none.VerticalAlignment = VerticalAlignment.Center;
                _strip.AddChild(none);
            }
        }

        public void Move(int step)
        {
            var first = Math.Clamp(_first + step, 0, Last);
            if (first == _first)
                return;
            _first = first;
            _slide?.Kill();
            _slide = CreateTween();
            _slide.TweenProperty(_strip, "position:x", -_first * (CardWidth + Gap), 0.22)
                  .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            Refresh();
        }

        private void Refresh()
        {
            _previous.Disabled = _first == 0;
            _next.Disabled = _first >= Last;
            var count = _palettes.Count;
            var of = count == Eve.Palettes.Count ? $"{count} themes" : $"{count} found of {Eve.Palettes.Count} themes";
            _where.Text = count == 0 ? $"0 found of {Eve.Palettes.Count} themes"
                : $"{_first + 1}–{Math.Min(_first + Shown, count)} of {of}  ·  ‹ › or the arrow keys ← → to see the others";
        }
    }

    private static Control Card(Palette palette, Action<Palette> apply)
    {
        var inUse = palette == Eve.Current;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth, CardHeight) };
        card.AddThemeStyleboxOverride("panel", Eve.Box(Eve.Field, inUse ? Eve.Cyan : Eve.Edge, 10, inUse ? 2 : 1));
        var name = Eve.Caption(palette.Name, inUse ? Eve.Cyan : Eve.TextBright, 14);
        var description = Eve.Text_(palette.Description, Eve.Muted, 12, wrap: true);
        // Three lines at most: every card has the same height.
        description.CustomMinimumSize = new Vector2(CardWidth - 20, 0);
        description.MaxLinesVisible = 3;
        description.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        description.TooltipText = palette.Description;
        description.MouseFilter = Control.MouseFilterEnum.Pass;
        var button = inUse ? Eve.Button("IN USE") : Eve.Button("APPLY", primary: true);
        button.Disabled = inUse;
        button.Pressed += () => apply(palette);
        card.AddChild(Ui.Column(8, Preview(palette), name, description, button));
        return card;
    }

    /// <summary>A small picture of the theme drawn with its own colours.</summary>
    private static Control Preview(Palette palette)
    {
        var page = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth - 20, 130), MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddThemeStyleboxOverride("panel", Eve.Box(palette.Space, palette.Edge, 12));
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = Eve.Box(palette.PanelSolid, palette.Edge, 10);
        box.BorderWidthTop = 3;
        box.BorderColor = palette.Accent;
        panel.AddThemeStyleboxOverride("panel", box);
        Label Label(string text, Color color, int size)
        {
            var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeColorOverride("font_color", color);
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeFontOverride("font", new SystemFont { FontNames = Eve.WithDefault(palette.DisplayFonts), FontWeight = palette.DisplayWeight });
            return label;
        }
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        chip.AddThemeStyleboxOverride("panel", Eve.Box(palette.Accent with { A = 0.85f }, palette.Accent, 4));
        chip.AddChild(Label("SEND", palette.IsLight ? Colors.White : palette.TextBright, 10));
        var swatches = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var color in new[] { palette.Accent, palette.Warm, palette.Green, palette.Red, palette.Muted })
            swatches.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(22, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
        panel.AddChild(Ui.Column(4, Label("KAONOMI", palette.TextBright, 13), Label("The chat, the tools, the 3D desk", palette.Text, 10), chip, swatches));
        page.AddChild(panel);
        return page;
    }
}
