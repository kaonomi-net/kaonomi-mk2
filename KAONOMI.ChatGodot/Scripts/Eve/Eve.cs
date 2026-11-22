using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The look of the chat, in the style of the interface of EVE Online: deep space behind dark translucent panels with
/// thin grey edges, small spaced capitals for the headers, a cold cyan for what is active and selected, amber for what
/// needs attention, flat buttons with a hairline edge. The fonts are the ones of the system (no file to ship).
/// </summary>
public static class Eve
{
    // ── The theme ──

    /// <summary>The themes of the chat: Kaonomi (light, in the style of www.kaonomi.net, the default) and Matrix (the green of the film).</summary>
    public static IReadOnlyList<Palette> Palettes { get; } = [Palette.Kaonomi, Palette.Matrix, Palette.Kandinsky, Palette.MarieKilleuse, Palette.Ubuntu, Palette.Ghost, Palette.MacOS, Palette.IronMan, Palette.StarCitizen, Palette.CounterStrike, Palette.Defense, Palette.Minecraft, Palette.Mars, Palette.Couture, Palette.Silicon, Palette.Robotics, Palette.Aquarium, Palette.KaonomiDark, Palette.KaonomiWhite];

    /// <summary>The theme in use.</summary>
    public static Palette Current { get; private set; } = Palette.Kaonomi;

    /// <summary>Makes <paramref name="palette"/> the theme in use (the interface is then built again by the shell).</summary>
    public static void Use(Palette palette)
    {
        Current = palette;
        _shared = null;
        _body = null;
        _display = null;
    }

    /// <summary>The theme named <paramref name="name"/> (Kaonomi when there is none).</summary>
    public static Palette Find(string? name)
    {
        var wanted = name?.Trim() ?? string.Empty;
        if (FormerNames.TryGetValue(wanted, out var now))
            wanted = now;
        return Palettes.FirstOrDefault(p => string.Equals(Plain(p.Name), Plain(wanted), StringComparison.OrdinalIgnoreCase)) ?? Palette.Kaonomi;
    }

    /// <summary>The names the themes had before (kept in the preferences, typed after /theme:): their names now.</summary>
    private static readonly Dictionary<string, string> FormerNames = new(StringComparer.OrdinalIgnoreCase) { ["KaonomiNet"] = "Kaonomi-Dark" };

    /// <summary>A name of theme without its hyphens: kaonomidark (the AppThemeKind of the shared code) is Kaonomi-Dark.</summary>
    private static string Plain(string name) => name.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);

    // ── Colors (of the theme in use) ──
    public static Color Space => Current.Space;
    public static Color Panel => Current.Panel;
    public static Color PanelSolid => Current.PanelSolid;
    public static Color Header => Current.Header;
    public static Color Field => Current.Field;
    public static Color Edge => Current.Edge;
    public static Color EdgeBright => Current.EdgeBright;
    public static Color Text => Current.Text;
    public static Color TextBright => Current.TextBright;
    public static Color Muted => Current.Muted;
    /// <summary>The accent: what is active and selected (cyan in EVE, the turquoise of kaonomi.net).</summary>
    public static Color Cyan => Current.Accent;
    /// <summary>What needs attention (amber in EVE, the gold of the logo of kaonomi.net).</summary>
    public static Color Amber => Current.Warm;
    public static Color Red => Current.Red;
    public static Color Green => Current.Green;
    /// <summary>True for a light theme (dark text on light panels).</summary>
    public static bool IsLight => Current.IsLight;

    /// <summary>
    /// The strongest text, for what is hovered, pressed or selected: white in a dark theme, the darkest text in a light
    /// one (white there would vanish on the light panels).
    /// </summary>
    public static Color Ink => IsLight ? TextBright : Colors.White;

    /// <summary>A sunken shade (a field, a bar, a list) of strength <paramref name="alpha"/>: black glass in a dark theme, a faint grey in a light one.</summary>
    public static Color Sunken(float alpha) => IsLight ? new Color(0.11f, 0.15f, 0.2f, alpha * 0.22f) : new Color(0, 0, 0, alpha);

    /// <summary>A raised shade (a button, a hovered row) of strength <paramref name="alpha"/>: white glass in a dark theme, a faint grey in a light one.</summary>
    public static Color Raised(float alpha) => IsLight ? new Color(0, 0, 0, alpha * 0.8f) : new Color(1, 1, 1, alpha);

    // ── Fonts ──

    /// <summary>The emoji of the icons and of the texts (the colour font of the system).</summary>
    public static readonly SystemFont Emoji = new()
    {
        FontNames = ["Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji", "Segoe UI Symbol"]
    };

    private static SystemFont? _body;
    private static FontVariation? _display;

    /// <summary>The font of the whole chat, whatever the theme: Arial (the fonts of the theme only when it is missing).</summary>
    public const string DefaultFont = "Arial";

    /// <summary><see cref="DefaultFont"/> first, then <paramref name="names"/> (the fonts of the theme) in case it is missing.</summary>
    public static string[] WithDefault(string[] names) => [DefaultFont, .. names.Where(n => !string.Equals(n, DefaultFont, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The text: Arial (else the font of the theme), of the system (the emoji from <see cref="Emoji"/>).</summary>
    public static SystemFont Body => _body ??= new SystemFont
    {
        FontNames = WithDefault(Current.BodyFonts),
        FontWeight = 400,
        Antialiasing = TextServer.FontAntialiasing.Gray,
        Hinting = TextServer.Hinting.Light,
        Fallbacks = [Emoji]
    };

    /// <summary>The headers: Arial (else the display font of the theme), semi-bold, spaced.</summary>
    public static FontVariation Display => _display ??= new FontVariation
    {
        BaseFont = new SystemFont { FontNames = WithDefault(Current.DisplayFonts), FontWeight = Current.DisplayWeight, Fallbacks = [Emoji] },
        SpacingGlyph = IsLight ? 1 : 2
    };

    /// <summary>Monospace font of the code and of the numbers.</summary>
    public static readonly SystemFont Mono = new()
    {
        FontNames = ["SF Mono", "Menlo", "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "monospace"],
        Antialiasing = TextServer.FontAntialiasing.Lcd
    };

    // ── Boxes ──

    /// <summary>The rounding of the corners of the boxes: soft in the light themes (glass and white cards), slight in the dark ones.</summary>
    public static int Rounding => IsLight ? 7 : 3;

    /// <summary>The height of the title bar of a dialog.</summary>
    private const int DialogTitleHeight = 30;

    /// <summary>
    /// A flat box of <paramref name="back"/> with a hairline edge of <paramref name="edge"/>, its corners rounded by
    /// <paramref name="radius"/> (-1: <see cref="Rounding"/>; 0: square).
    /// </summary>
    public static StyleBoxFlat Box(Color back, Color? edge = null, int margin = 8, int border = 1, int radius = -1)
    {
        var box = new StyleBoxFlat { BgColor = back, BorderColor = edge ?? Edge, AntiAliasing = false };
        box.SetBorderWidthAll(edge is null && border == 1 ? 1 : border);
        box.SetContentMarginAll(margin);
        var corner = radius < 0 ? Rounding : radius;
        if (corner > 0)
        {
            box.SetCornerRadiusAll(corner);
            box.AntiAliasing = true;
            box.AntiAliasingSize = 0.6f;
        }
        return box;
    }

    /// <summary>A white card with a hairline edge (the light themes), the panel of a group of things.</summary>
    public static StyleBoxFlat Card(int margin = 10) =>
        IsLight ? Box(Field, Edge with { A = 0.85f }, margin, 1, 10) : Box(Sunken(0.18f), Edge with { A = 0.3f }, margin);

    /// <summary>An icon drawn from SVG, sharp at every scale of the interface.</summary>
    public static Texture2D Icon(string svg) => DpiTexture.CreateFromString(svg, 1f, 1f, null);

    private static string Hex(Color color) => "#" + color.ToHtml(false);

    /// <summary>The cross that closes a dialog, in <paramref name="color"/>.</summary>
    private static string CloseCross(Color color) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M4 4L12 12M12 4L4 12' stroke='{Hex(color)}' stroke-width='1.6' stroke-linecap='round'/></svg>";

    /// <summary>The panel of a window: dark glass, a hairline edge, a faint light along its top.</summary>
    public static StyleBoxFlat WindowBox(bool active)
    {
        var box = Box(Panel, active ? EdgeBright with { A = 0.7f } : Edge, 0);
        box.BorderWidthTop = 1;
        box.ShadowColor = new Color(0, 0, 0, IsLight ? 0.18f : 0.55f);
        box.ShadowSize = 18;
        return box;
    }

    // ── The bars that float over the desktop (the chat on the right, the bar of the tools on the left) ──

    /// <summary>The radius of the corners of a floating bar.</summary>
    public static int FloatCorner => IsLight ? 14 : 8;

    /// <summary>The room left around a floating bar (its shadow is drawn there); half of it on the side of the desktop.</summary>
    public const int FloatMargin = 14;

    /// <summary>The panel of a floating bar: rounded corners, a hairline edge, a soft shadow under it.</summary>
    public static StyleBoxFlat FloatingBox(Color back)
    {
        var box = Box(back, Edge with { A = 0.6f }, 0, 1, FloatCorner);
        box.ShadowColor = new Color(0, 0, 0, IsLight ? 0.16f : 0.5f);
        box.ShadowSize = 22;
        box.ShadowOffset = new Vector2(0, 8);
        return box;
    }

    /// <summary>
    /// <paramref name="bar"/> in the frame that makes it float: room on every side for its shadow (half of it on the
    /// side of the desktop, <paramref name="desktopOnLeft"/> or on its right), the background seen around it.
    /// </summary>
    public static MarginContainer Floating(Control bar, bool desktopOnLeft)
    {
        var frame = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddThemeConstantOverride("margin_left", desktopOnLeft ? FloatMargin / 2 : FloatMargin);
        frame.AddThemeConstantOverride("margin_top", FloatMargin);
        frame.AddThemeConstantOverride("margin_right", desktopOnLeft ? FloatMargin : FloatMargin / 2);
        frame.AddThemeConstantOverride("margin_bottom", FloatMargin);
        frame.AddChild(bar);
        return frame;
    }

    /// <summary>
    /// A frame that cuts what it holds along the rounded corners of a floating bar (an opaque rounded panel used as a
    /// mask only, not drawn): the header and the bars inside end round like the bar.
    /// </summary>
    public static Panel RoundedClip()
    {
        var mask = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true, AntiAliasingSize = 0.6f };
        mask.SetCornerRadiusAll(Math.Max(FloatCorner - 1, 0));
        var clip = new Panel { ClipContents = true, ClipChildren = CanvasItem.ClipChildrenMode.Only, MouseFilter = Control.MouseFilterEnum.Ignore };
        clip.AddThemeStyleboxOverride("panel", mask);
        return clip;
    }

    /// <summary>A label of the header of a section: small spaced capitals, muted.</summary>
    public static Label Caption(string text, Color? color = null, int size = 11)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeFontOverride("font", Display);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Muted);
        return label;
    }

    /// <summary>A plain label.</summary>
    public static Label Text_(string text, Color? color = null, int size = 13, bool wrap = false)
    {
        var label = new Label { Text = text };
        if (wrap)
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>A flat button; <paramref name="primary"/> gives it the cyan of the main action.</summary>
    public static Button Button(string text, bool primary = false, string? tooltip = null)
    {
        var button = new Button { Text = text, TooltipText = tooltip ?? string.Empty, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        if (primary)
            StylePrimary(button);
        return button;
    }

    /// <summary>The padding of a button: comfortable, as the buttons of a toolbar of a desktop.</summary>
    private static StyleBoxFlat ButtonBox(Color back, Color edge)
    {
        var box = Box(back, edge, 6);
        box.ContentMarginLeft = box.ContentMarginRight = IsLight ? 10 : 9;
        box.ContentMarginTop = box.ContentMarginBottom = 5;
        return box;
    }

    /// <summary>The accent (blue in the light themes) on <paramref name="button"/>: what is on now (PAUSE while it runs, the tool in use).</summary>
    public static void StyleAccent(BaseButton button)
    {
        var text = IsLight ? Cyan.Darkened(0.15f) : TextBright;
        button.AddThemeStyleboxOverride("normal", ButtonBox(Cyan with { A = IsLight ? 0.13f : 0.2f }, Cyan with { A = 0.65f }));
        button.AddThemeStyleboxOverride("hover", ButtonBox(Cyan with { A = IsLight ? 0.22f : 0.3f }, Cyan));
        button.AddThemeStyleboxOverride("pressed", ButtonBox(Cyan with { A = IsLight ? 0.3f : 0.42f }, Cyan));
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_focus_color", text);
        button.AddThemeColorOverride("font_hover_color", text);
        button.AddThemeColorOverride("font_pressed_color", text);
        button.AddThemeColorOverride("font_hover_pressed_color", text);
    }

    /// <summary>The main action on <paramref name="button"/>: green, as the action that adds or keeps.</summary>
    public static void StylePrimary(BaseButton button)
    {
        var text = IsLight ? Green.Darkened(0.35f) : TextBright;
        button.AddThemeStyleboxOverride("normal", ButtonBox(Green with { A = IsLight ? 0.16f : 0.2f }, Green with { A = 0.7f }));
        button.AddThemeStyleboxOverride("hover", ButtonBox(Green with { A = IsLight ? 0.26f : 0.32f }, Green));
        button.AddThemeStyleboxOverride("pressed", ButtonBox(Green with { A = IsLight ? 0.36f : 0.45f }, Green));
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_focus_color", text);
        button.AddThemeColorOverride("font_hover_color", text);
        button.AddThemeColorOverride("font_pressed_color", text);
        button.AddThemeColorOverride("font_hover_pressed_color", text);
    }

    /// <summary>A thin horizontal line between sections.</summary>
    public static Control Separator()
    {
        var line = new ColorRect { Color = Edge with { A = 0.35f }, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore };
        return line;
    }

    private static Theme? _shared;

    /// <summary>The theme of the chat, built once: the window and every window of the desktop (their viewports do not inherit it).</summary>
    public static Theme Shared => _shared ??= Build();

    /// <summary>
    /// The theme of every control: dark fields with hairline edges, flat grey buttons lit in cyan under the pointer,
    /// thin scroll bars, cyan selections, small text.
    /// </summary>
    public static Theme Build()
    {
        var theme = new Theme { DefaultFont = Body, DefaultFontSize = 13 };
        theme.SetColor("font_color", "Label", Text);
        theme.SetFont("mono_font", "RichTextLabel", Mono);
        theme.SetFont("bold_font", "RichTextLabel", new SystemFont { FontNames = Body.FontNames, FontWeight = 700 });
        theme.SetColor("default_color", "RichTextLabel", Text);
        theme.SetColor("selection_color", "RichTextLabel", Cyan with { A = 0.3f });

        // Buttons.
        // A light card with a hairline edge, darker under the pointer; pressed (a toggle that is on: PAUSE, a tab) blue.
        theme.SetStylebox("normal", "Button", ButtonBox(IsLight ? Field : Raised(0.04f), IsLight ? Edge : Edge));
        theme.SetStylebox("hover", "Button", ButtonBox(IsLight ? new Color(0.93f, 0.95f, 0.97f) : Cyan with { A = 0.12f }, IsLight ? EdgeBright with { A = 0.7f } : Cyan with { A = 0.8f }));
        theme.SetStylebox("pressed", "Button", ButtonBox(Cyan with { A = IsLight ? 0.14f : 0.28f }, Cyan with { A = IsLight ? 0.7f : 1f }));
        theme.SetStylebox("hover_pressed", "Button", ButtonBox(Cyan with { A = IsLight ? 0.2f : 0.34f }, Cyan));
        theme.SetStylebox("disabled", "Button", ButtonBox(IsLight ? Field with { A = 0.55f } : Raised(0.015f), Edge with { A = IsLight ? 0.5f : 0.25f }));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", IsLight ? Text.Lightened(0.18f) : Text);
        // The button that has the focus (the OK of a dialog when it opens): without this, the light grey of Godot.
        theme.SetColor("font_focus_color", "Button", IsLight ? Text.Lightened(0.18f) : Text);
        theme.SetColor("font_hover_color", "Button", TextBright);
        theme.SetColor("font_pressed_color", "Button", IsLight ? Cyan.Darkened(0.15f) : Ink);
        theme.SetColor("font_hover_pressed_color", "Button", IsLight ? Cyan.Darkened(0.25f) : Ink);
        theme.SetColor("font_disabled_color", "Button", Muted with { A = 0.5f });
        theme.SetColor("icon_normal_color", "Button", Text);
        theme.SetColor("icon_hover_color", "Button", Cyan);

        // Fields.
        var field = Box(Field, Edge with { A = IsLight ? 0.9f : 0.45f }, 6);
        var focus = Box(Field, Cyan with { A = 0.8f }, 6);
        if (IsLight)
            focus.SetBorderWidthAll(2);
        foreach (var type in new[] { "LineEdit", "TextEdit", "CodeEdit" })
        {
            theme.SetStylebox("normal", type, field);
            theme.SetStylebox("focus", type, focus);
            theme.SetStylebox("read_only", type, field);
            theme.SetColor("font_color", type, TextBright);
            theme.SetColor("font_placeholder_color", type, Muted with { A = 0.8f });
            theme.SetColor("caret_color", type, Cyan);
            theme.SetColor("selection_color", type, Cyan with { A = 0.3f });
            theme.SetColor("font_readonly_color", type, Text);
            theme.SetColor("background_color", type, new Color(0, 0, 0, 0));
        }

        // Drop-downs and menus.
        foreach (var state in new[] { "normal", "focus" })
            theme.SetStylebox(state, "OptionButton", Box(Field, Edge with { A = 0.5f }, 6));
        theme.SetStylebox("hover", "OptionButton", Box(Cyan with { A = 0.1f }, Cyan with { A = 0.8f }, 6));
        theme.SetStylebox("pressed", "OptionButton", Box(Cyan with { A = 0.2f }, Cyan, 6));
        theme.SetColor("font_color", "OptionButton", TextBright);
        theme.SetColor("font_focus_color", "OptionButton", TextBright);
        theme.SetColor("font_hover_color", "OptionButton", Ink);
        theme.SetColor("font_pressed_color", "OptionButton", Ink);
        theme.SetColor("font_hover_pressed_color", "OptionButton", Ink);
        theme.SetStylebox("panel", "PopupMenu", Box(PanelSolid, EdgeBright with { A = 0.6f }, 4));
        theme.SetStylebox("hover", "PopupMenu", Box(Cyan with { A = 0.22f }, Cyan with { A = 0f }, 2, 0));
        theme.SetColor("font_color", "PopupMenu", Text);
        theme.SetColor("font_hover_color", "PopupMenu", Ink);
        theme.SetColor("font_separator_color", "PopupMenu", Muted);
        theme.SetStylebox("panel", "PopupPanel", Box(PanelSolid, EdgeBright with { A = 0.6f }, 6));

        // Lists and trees.
        var list = IsLight ? Box(Field, Edge with { A = 0.85f }, 4, 1, 8) : Box(Field, Edge with { A = 0.35f }, 4);
        var selected = Box(Cyan with { A = 0.2f }, Cyan with { A = 0f }, 2, 0);
        selected.BorderWidthLeft = 2;
        selected.BorderColor = Cyan;
        foreach (var type in new[] { "ItemList", "Tree" })
        {
            theme.SetStylebox("panel", type, list);
            theme.SetStylebox("focus", type, new StyleBoxEmpty());
            theme.SetStylebox("selected", type, selected);
            theme.SetStylebox("selected_focus", type, selected);
            theme.SetStylebox("cursor", type, new StyleBoxEmpty());
            theme.SetStylebox("cursor_unfocused", type, new StyleBoxEmpty());
            theme.SetStylebox("hovered", type, Box(Raised(0.04f), new Color(0, 0, 0, 0), 2, 0));
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_selected_color", type, Ink);
            theme.SetColor("font_hovered_color", type, TextBright);
            // A selected item under the pointer: without these, Godot's own white text and box (invisible in a light theme).
            theme.SetColor("font_hovered_selected_color", type, Ink);
            theme.SetStylebox("hovered_selected", type, selected);
            theme.SetStylebox("hovered_selected_focus", type, selected);
            theme.SetColor("guide_color", type, Edge with { A = 0.25f });
        }
        theme.SetColor("relationship_line_color", "Tree", Edge with { A = 0.4f });
        theme.SetStylebox("title_button_normal", "Tree", Box(Header, Edge with { A = 0.4f }, 4));

        // Tabs.
        var tab = Box(Raised(0.03f), Edge with { A = 0.3f }, 6);
        tab.ContentMarginLeft = tab.ContentMarginRight = 12;
        var tabSelected = Box(Cyan with { A = 0.14f }, Cyan with { A = 0f }, 6, 0);
        tabSelected.BorderWidthBottom = 2;
        tabSelected.BorderColor = Cyan;
        tabSelected.ContentMarginLeft = tabSelected.ContentMarginRight = 12;
        theme.SetStylebox("tab_unselected", "TabContainer", tab);
        theme.SetStylebox("tab_hovered", "TabContainer", Box(Cyan with { A = 0.08f }, Edge with { A = 0.3f }, 6));
        theme.SetStylebox("tab_selected", "TabContainer", tabSelected);
        theme.SetStylebox("panel", "TabContainer", Box(Sunken(0.15f), Edge with { A = 0.25f }, 8));
        theme.SetColor("font_selected_color", "TabContainer", Ink);
        theme.SetColor("font_unselected_color", "TabContainer", Muted);
        theme.SetColor("font_hovered_color", "TabContainer", TextBright);
        theme.SetFont("font", "TabContainer", Display);
        theme.SetFontSize("font_size", "TabContainer", 11);

        // Check boxes, sliders, progress.
        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetColor("font_hover_color", "CheckBox", TextBright);
        theme.SetColor("font_pressed_color", "CheckBox", TextBright);
        theme.SetColor("font_hover_pressed_color", "CheckBox", TextBright);
        theme.SetColor("font_focus_color", "CheckBox", Text);
        theme.SetColor("font_color", "CheckButton", Text);
        theme.SetColor("font_pressed_color", "CheckButton", TextBright);
        theme.SetColor("font_hover_color", "CheckButton", TextBright);
        theme.SetColor("font_hover_pressed_color", "CheckButton", TextBright);
        theme.SetColor("font_focus_color", "CheckButton", Text);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" })
        {
            theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
            theme.SetStylebox(state, "CheckButton", new StyleBoxEmpty());
        }
        // A check: a green tick when on, a rounded square when off.
        var tick = Icon($"<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'><path d='M3.5 9.5l3.6 3.6 7.4-8' fill='none' stroke='{Hex(Green)}' stroke-width='2.4' stroke-linecap='round' stroke-linejoin='round'/></svg>");
        var box = Icon($"<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'><rect x='2.5' y='2.5' width='13' height='13' rx='3.5' fill='{Hex(Field)}' stroke='{Hex(EdgeBright)}' stroke-width='1.3'/></svg>");
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            theme.SetIcon("checked", type, tick);
            theme.SetIcon("checked_disabled", type, tick);
            theme.SetIcon("unchecked", type, box);
            theme.SetIcon("unchecked_disabled", type, box);
            theme.SetConstant("h_separation", type, 6);
        }
        // A slider: a thin rounded track, filled in the accent, and a round knob.
        var track = new StyleBoxFlat { BgColor = Edge with { A = IsLight ? 0.9f : 0.35f }, ContentMarginTop = 2, ContentMarginBottom = 2 };
        track.SetCornerRadiusAll(2);
        var filled = new StyleBoxFlat { BgColor = Cyan with { A = 0.9f }, ContentMarginTop = 2, ContentMarginBottom = 2 };
        filled.SetCornerRadiusAll(2);
        theme.SetStylebox("slider", "HSlider", track);
        theme.SetStylebox("grabber_area", "HSlider", filled);
        theme.SetStylebox("grabber_area_highlight", "HSlider", filled);
        var knob = Icon($"<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'><circle cx='9' cy='9.6' r='7' fill='#000' opacity='0.12'/><circle cx='9' cy='9' r='6.6' fill='#ffffff' stroke='{Hex(EdgeBright)}' stroke-width='1'/></svg>");
        var knobLit = Icon($"<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'><circle cx='9' cy='9.6' r='7' fill='#000' opacity='0.14'/><circle cx='9' cy='9' r='6.6' fill='#ffffff' stroke='{Hex(Cyan)}' stroke-width='1.4'/></svg>");
        theme.SetIcon("grabber", "HSlider", knob);
        theme.SetIcon("grabber_highlight", "HSlider", knobLit);
        theme.SetIcon("grabber_disabled", "HSlider", knob);
        theme.SetStylebox("background", "ProgressBar", Box(Field, Edge with { A = 0.35f }, 0));
        theme.SetStylebox("fill", "ProgressBar", new StyleBoxFlat { BgColor = Cyan with { A = 0.75f } });
        theme.SetColor("font_color", "ProgressBar", TextBright);
        theme.SetFontSize("font_size", "ProgressBar", 10);

        // Scroll bars: a thin grey grabber, cyan when used.
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            var grabber = new StyleBoxFlat { BgColor = Edge with { A = 0.6f } };
            grabber.SetCornerRadiusAll(1);
            theme.SetStylebox("grabber", type, grabber);
            theme.SetStylebox("grabber_highlight", type, new StyleBoxFlat { BgColor = EdgeBright });
            theme.SetStylebox("grabber_pressed", type, new StyleBoxFlat { BgColor = Cyan });
            var scroll = new StyleBoxFlat { BgColor = Sunken(0.2f) };
            if (type == "VScrollBar")
                scroll.ContentMarginLeft = scroll.ContentMarginRight = 2;
            else
                scroll.ContentMarginTop = scroll.ContentMarginBottom = 2;
            theme.SetStylebox("scroll", type, scroll);
        }

        // Tooltips, panels, dialogs, split bars.
        theme.SetStylebox("panel", "TooltipPanel", Box(PanelSolid, EdgeBright with { A = 0.6f }, 6));
        theme.SetColor("font_color", "TooltipLabel", Text);
        theme.SetFontSize("font_size", "TooltipLabel", 12);
        theme.SetStylebox("panel", "PanelContainer", Card(8));
        theme.SetStylebox("panel", "Panel", Box(Sunken(0.18f), Edge with { A = 0.3f }, 0));
        // The dialogs: a thin frame of the panel around their content, as the windows (EveFrame.Inset).
        // Square: the content of a dialog is drawn on an opaque ground, its rounded corners showed it (dark).
        theme.SetStylebox("panel", "AcceptDialog", Box(PanelSolid, EdgeBright with { A = 0.6f }, 5, radius: 0));
        // Their title bar is drawn above their content: the frame goes up over it (else its title is written over
        // what is behind, a picture of the camera for instance).
        var dialogFrame = Box(PanelSolid, EdgeBright with { A = 0.6f }, 0);
        dialogFrame.ExpandMarginTop = DialogTitleHeight;
        theme.SetStylebox("embedded_border", "Window", dialogFrame);
        theme.SetStylebox("embedded_unfocused_border", "Window", dialogFrame);
        theme.SetConstant("title_height", "Window", DialogTitleHeight);
        // Their close cross in the colour of the texts (the one of Godot is white: unseen on a light panel).
        theme.SetIcon("close", "Window", Icon(CloseCross(Muted)));
        theme.SetIcon("close_pressed", "Window", Icon(CloseCross(TextBright)));
        theme.SetColor("title_color", "Window", TextBright);
        theme.SetFont("title_font", "Window", Display);
        theme.SetConstant("separation", "HSplitContainer", 6);
        theme.SetConstant("separation", "VSplitContainer", 6);
        theme.SetConstant("separation", "VBoxContainer", 6);
        theme.SetConstant("separation", "HBoxContainer", 6);
        return theme;
    }
}

/// <summary>
/// A theme of the chat: its colours, its fonts, the shader of its background and whether it is light.
/// </summary>
public sealed record Palette(
    string Name,
    string Description,
    bool IsLight,
    Color Space, Color Panel, Color PanelSolid, Color Header, Color Field,
    Color Edge, Color EdgeBright, Color Text, Color TextBright, Color Muted,
    Color Accent, Color Warm, Color Red, Color Green,
    string[] BodyFonts, string[] DisplayFonts, int DisplayWeight,
    string Background,
    string? Picture = null,
    string? Stamp = null,
    int StampWidth = 384)
{
    /// <summary>The file of the picture behind the desk (Assets folder next to the chat), null without one.</summary>
    public string? PicturePath => Picture is null ? null : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", Picture);

    /// <summary>
    /// The file of the picture the shader of the background draws itself (its <c>stamp</c> uniform: the portraits of
    /// MarieKilleuse in her hearts), Assets folder next to the chat; null without one.
    /// </summary>
    public string? StampPath => Stamp is null ? null : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", Stamp);

    /// <summary>
    /// kaonomi.net: a white page with faint networks of hexagons, black titles in spaced capitals, the turquoise of its
    /// links and the gold of its logo, the Rubik font of the site when it is installed.
    /// </summary>
    public static readonly Palette Kaonomi = new(
        "Kaonomi", "Light · white paper, turquoise and gold, in the style of www.kaonomi.net", true,
        Space: Color.FromHtml("#EEF2F6"),
        Panel: Color.FromHtml("#F3F5F8"),
        PanelSolid: Color.FromHtml("#F6F7F9"),
        Header: Color.FromHtml("#F4F6F800"),
        Field: Color.FromHtml("#FFFFFF"),
        Edge: Color.FromHtml("#D3D9DF"),
        EdgeBright: Color.FromHtml("#9AA5B0"),
        Text: Color.FromHtml("#22272C"),
        TextBright: Color.FromHtml("#101317"),
        Muted: Color.FromHtml("#6B7580"),
        Accent: Color.FromHtml("#2F78E4"),
        Warm: Color.FromHtml("#E58A1F"),
        Red: Color.FromHtml("#D14336"),
        Green: Color.FromHtml("#3FA44E"),
        BodyFonts: ["Rubik", "Inter", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Rubik", "Inter", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/kaonomi_paper.gdshader");

    /// <summary>
    /// The Matrix: the black of the screen with a faint green cast, panels of dark glass, the phosphor green of the
    /// digital rain for what is active, a pale green for the text, the monospace fonts of a terminal; the rain of
    /// glyphs falls behind the desk.
    /// </summary>
    public static readonly Palette Matrix = new(
        "Matrix", "Dark · the digital rain, phosphor green on black and the monospace of a terminal, in the style of the film", false,
        Space: new(0.0f, 0.016f, 0.004f),
        Panel: new(0.0f, 0.04f, 0.012f, 0.92f),
        PanelSolid: new(0.004f, 0.05f, 0.016f, 0.97f),
        Header: new(0.01f, 0.09f, 0.03f, 0.95f),
        Field: new(0.0f, 0.0f, 0.0f, 0.45f),
        Edge: new(0.0f, 0.55f, 0.18f, 0.5f),
        EdgeBright: new(0.2f, 0.85f, 0.35f, 0.85f),
        Text: new(0.72f, 0.95f, 0.75f),
        TextBright: new(0.85f, 1.0f, 0.88f),
        Muted: new(0.40f, 0.70f, 0.45f),
        Accent: new(0.0f, 1.0f, 0.25f),
        Warm: new(0.75f, 0.95f, 0.2f),
        Red: new(0.95f, 0.3f, 0.25f),
        Green: new(0.2f, 0.9f, 0.35f),
        BodyFonts: ["Menlo", "SF Mono", "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New", "monospace"],
        DisplayFonts: ["Menlo", "SF Mono", "Consolas", "Cascadia Mono", "Courier New", "monospace"],
        DisplayWeight: 700,
        Background: "res://Shaders/matrix_rain.gdshader");

    /// <summary>
    /// Kandinsky: his geometric abstractions of the Bauhaus years (Composition VIII). A warm cream paper, panels of a
    /// lighter cream, an ink-black text, the blue of his circles for what is active, his yellow for what calls, his red
    /// for alarms, the geometric capitals of the Bauhaus (Futura) for the headers; behind the desk the composition
    /// drifts slowly: circles in their halos, black lines, a yellow triangle, a checkerboard, arcs.
    /// </summary>
    public static readonly Palette Kandinsky = new(
        "Kandinsky", "Light · cream paper, circles in their halos, black lines and primary colours, after the abstractions of Kandinsky", true,
        Space: Color.FromHtml("#F3EDE0"),
        Panel: Color.FromHtml("#FBF8F1"),
        PanelSolid: Color.FromHtml("#FCFAF5"),
        Header: Color.FromHtml("#FBF8F100"),
        Field: Color.FromHtml("#FFFFFF"),
        Edge: Color.FromHtml("#DDD3C1"),
        EdgeBright: Color.FromHtml("#A89C87"),
        Text: Color.FromHtml("#2A2622"),
        TextBright: Color.FromHtml("#141210"),
        Muted: Color.FromHtml("#7B7266"),
        Accent: Color.FromHtml("#2451A8"),
        Warm: Color.FromHtml("#E0A118"),
        Red: Color.FromHtml("#D2372A"),
        Green: Color.FromHtml("#3E8E5A"),
        BodyFonts: ["Avenir Next", "Futura", "Century Gothic", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Futura", "Century Gothic", "Avenir Next", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/kandinsky_composition.gdshader");

    /// <summary>
    /// MarieKilleuse: half kawaii, half gore, like the robot doll with a knife of the avatar. Pastel pink panels, a
    /// plum text, the hot pink of her heart for what is active, the mint of her headphones for what calls, the red of
    /// blood for alarms, rounded capitals; behind the desk a pink sheet of polka dots, floating hearts, stars and
    /// sparkles, cut by a stitched seam from a dark crimson half, with blood running from the top and splattered.
    /// </summary>
    public static readonly Palette MarieKilleuse = new(
        "MarieKilleuse", "Light · half kawaii, half gore: pastel pink, hearts and stars, then blood running down, after the doll with a knife", true,
        Space: Color.FromHtml("#FFE6F0"),
        Panel: Color.FromHtml("#FFF3F7"),
        PanelSolid: Color.FromHtml("#FFF6F9"),
        Header: Color.FromHtml("#FFF3F700"),
        Field: Color.FromHtml("#FFFFFF"),
        Edge: Color.FromHtml("#F1BFD2"),
        EdgeBright: Color.FromHtml("#D98AA8"),
        Text: Color.FromHtml("#3A1F2B"),
        TextBright: Color.FromHtml("#22111A"),
        Muted: Color.FromHtml("#946A7C"),
        Accent: Color.FromHtml("#E2357B"),
        Warm: Color.FromHtml("#1FA39A"),
        Red: Color.FromHtml("#B0001C"),
        Green: Color.FromHtml("#36A874"),
        BodyFonts: ["Avenir Next", "Nunito", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Arial Rounded MT Bold", "Varela Round", "Nunito", "Avenir Next", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/killeuse_doll.gdshader",
        Stamp: "mariekilleuse-face.png");

    /// <summary>
    /// Ubuntu: the desktop of Ubuntu (its Yaru look, dark). Charcoal grey panels, a light grey text, the orange of
    /// Ubuntu for what is active, a warm yellow for what calls, its red for errors, the Ubuntu font when it is
    /// installed; behind the desk the aubergine of its wallpapers turning to orange, with translucent ribbons in slow
    /// waves and soft rising lights.
    /// </summary>
    public static readonly Palette Ubuntu = new(
        "Ubuntu", "Dark · the desktop of Ubuntu: charcoal panels and Ubuntu orange on its aubergine wallpaper with slow ribbons", false,
        Space: Color.FromHtml("#2C001E"),
        Panel: Color.FromHtml("#2B2B2BEB"),
        PanelSolid: Color.FromHtml("#303030F8"),
        Header: Color.FromHtml("#242424F2"),
        Field: Color.FromHtml("#1D1D1DD9"),
        Edge: Color.FromHtml("#4C4C4CC0"),
        EdgeBright: Color.FromHtml("#7D7D7D"),
        Text: Color.FromHtml("#E3E3E3"),
        TextBright: Color.FromHtml("#FFFFFF"),
        Muted: Color.FromHtml("#A3A3A3"),
        Accent: Color.FromHtml("#E95420"),
        Warm: Color.FromHtml("#F4A100"),
        Red: Color.FromHtml("#E0313B"),
        Green: Color.FromHtml("#3EB34F"),
        BodyFonts: ["Ubuntu", "Ubuntu Sans", "Cantarell", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Ubuntu", "Ubuntu Sans", "Cantarell", "Helvetica Neue", "Arial"],
        DisplayWeight: 500,
        Background: "res://Shaders/ubuntu_waves.gdshader");

    /// <summary>
    /// Ghost: after Ghost in the Shell, on the picture of a cyborg woman on a rooftop over a megacity at night (Assets/
    /// ghost-rooftop.jpg). Panels of a dark teal glass, a pale cyan text, the teal of its neons for what is active, their
    /// pink for what calls, condensed capitals for the headers; behind the desk the picture itself, alive: a slow
    /// travelling, the neons breathing, mist drifting, thin rain.
    /// </summary>
    public static readonly Palette Ghost = new(
        "Ghost", "Dark · a cyborg on a rooftop over the megacity at night, neons breathing in the mist and the rain, after Ghost in the Shell", false,
        Space: new(0.02f, 0.05f, 0.06f),
        Panel: new(0.025f, 0.06f, 0.075f, 0.84f),
        PanelSolid: new(0.03f, 0.07f, 0.085f, 0.97f),
        Header: new(0.05f, 0.11f, 0.13f, 0.93f),
        Field: new(0.0f, 0.02f, 0.03f, 0.55f),
        Edge: new(0.2f, 0.55f, 0.55f, 0.5f),
        EdgeBright: new(0.45f, 0.9f, 0.85f, 0.85f),
        Text: new(0.8f, 0.9f, 0.92f),
        TextBright: new(0.94f, 0.99f, 1.0f),
        Muted: new(0.48f, 0.63f, 0.66f),
        Accent: new(0.18f, 0.9f, 0.78f),
        Warm: new(1.0f, 0.32f, 0.58f),
        Red: new(1.0f, 0.25f, 0.32f),
        Green: new(0.25f, 0.88f, 0.55f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Avenir Next Condensed", "Bahnschrift", "Eurostile", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/ghost_rooftop.gdshader",
        Stamp: "ghost-rooftop.jpg",
        StampWidth: 4096);

    /// <summary>
    /// MacOS: after the desktop of macOS. Panels of a light frosted glass, a near-black text, the system blue for what
    /// is active, its orange for what calls, its red and green for the states, the San Francisco fonts when they are
    /// installed; behind the desk the colourful hills of its recent wallpapers, swaying very slowly.
    /// </summary>
    public static readonly Palette MacOS = new(
        "MacOS", "Light · the desktop of macOS: frosted glass panels, system blue, on the colourful hills of its wallpapers", true,
        Space: Color.FromHtml("#E9ECF2"),
        Panel: Color.FromHtml("#F6F6F8F2"),
        PanelSolid: Color.FromHtml("#F8F8FA"),
        Header: Color.FromHtml("#F6F6F800"),
        Field: Color.FromHtml("#FFFFFF"),
        Edge: Color.FromHtml("#D5D5DA"),
        EdgeBright: Color.FromHtml("#A1A1A8"),
        Text: Color.FromHtml("#1D1D1F"),
        TextBright: Color.FromHtml("#000000"),
        Muted: Color.FromHtml("#6E6E73"),
        Accent: Color.FromHtml("#007AFF"),
        Warm: Color.FromHtml("#FF9500"),
        Red: Color.FromHtml("#FF3B30"),
        Green: Color.FromHtml("#34C759"),
        BodyFonts: ["SF Pro Text", "SF Pro", "Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["SF Pro Display", "SF Pro", "Helvetica Neue", "Inter", "Arial"],
        DisplayWeight: 600,
        Background: "res://Shaders/macos_hills.gdshader");

    /// <summary>
    /// IronMan: after the armour of Iron Man and the head-up display of its helmet. Panels of a dark gunmetal with a red
    /// cast, a warm white text, the blue-white of the arc reactor for what is active, the gold of the armour for what
    /// calls, its red for alarms, wide technical capitals for the headers; behind the desk the red armour plates with
    /// their gold seams, the arc reactor glowing and the rings of the HUD turning slowly.
    /// </summary>
    public static readonly Palette IronMan = new(
        "IronMan", "Dark · red armour plates with gold seams, the arc reactor glowing and the rings of a HUD, after Iron Man", false,
        Space: new(0.045f, 0.015f, 0.02f),
        Panel: new(0.065f, 0.04f, 0.042f, 0.86f),
        PanelSolid: new(0.075f, 0.045f, 0.048f, 0.97f),
        Header: new(0.14f, 0.05f, 0.055f, 0.95f),
        Field: new(0.02f, 0.01f, 0.012f, 0.55f),
        Edge: new(0.7f, 0.5f, 0.2f, 0.45f),
        EdgeBright: new(0.95f, 0.72f, 0.3f, 0.85f),
        Text: new(0.93f, 0.9f, 0.86f),
        TextBright: new(1.0f, 0.98f, 0.95f),
        Muted: new(0.66f, 0.58f, 0.54f),
        Accent: new(0.42f, 0.88f, 1.0f),
        Warm: new(0.96f, 0.72f, 0.24f),
        Red: new(0.9f, 0.14f, 0.17f),
        Green: new(0.3f, 0.82f, 0.55f),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Eurostile", "Bank Gothic", "Avenir Next Condensed", "Bahnschrift", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/ironman_hud.gdshader");

    /// <summary>
    /// StarCitizen: after the space game Star Citizen and its mobiGlas. Panels of a dark blue glass, a pale cyan text,
    /// the cyan of the mobiGlas for what is active, amber for what calls, wide condensed capitals for the headers; behind
    /// the desk deep space with a nebula and stars drifting, a star with its flare, a gas giant turning slowly, a ship
    /// crossing now and then, the brackets and the scanner of a HUD.
    /// </summary>
    public static readonly Palette StarCitizen = new(
        "StarCitizen", "Dark · deep space, a gas giant and a ship passing by, in the cyan holograms of a mobiGlas, after the game Star Citizen", false,
        Space: new(0.008f, 0.016f, 0.04f),
        Panel: new(0.02f, 0.05f, 0.08f, 0.84f),
        PanelSolid: new(0.025f, 0.06f, 0.095f, 0.97f),
        Header: new(0.04f, 0.1f, 0.15f, 0.93f),
        Field: new(0.0f, 0.02f, 0.04f, 0.55f),
        Edge: new(0.2f, 0.6f, 0.7f, 0.45f),
        EdgeBright: new(0.45f, 0.88f, 0.95f, 0.85f),
        Text: new(0.82f, 0.92f, 0.96f),
        TextBright: new(0.95f, 0.99f, 1.0f),
        Muted: new(0.5f, 0.64f, 0.72f),
        Accent: new(0.31f, 0.82f, 0.91f),
        Warm: new(0.95f, 0.63f, 0.19f),
        Red: new(0.95f, 0.28f, 0.25f),
        Green: new(0.3f, 0.86f, 0.58f),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Avenir Next Condensed", "Eurostile", "Bahnschrift", "Helvetica Neue", "Arial"],
        DisplayWeight: 600,
        Background: "res://Shaders/starcitizen_space.gdshader");

    /// <summary>
    /// CounterStrike: after the menus and the radar of Counter-Strike 2. Panels of a dark slate, a light grey text, the
    /// orange of the game for what is active, its gold for what calls, a red for alarms, bold condensed capitals for the
    /// headers; behind the desk the picture of a fight in a desert town (Assets/cs-match.jpg), alive: a slow travelling,
    /// smoke drifting, embers rising, dust floating, the diode of the bomb pulsing.
    /// </summary>
    public static readonly Palette CounterStrike = new(
        "CounterStrike", "Dark · a fight in a desert town between the sites A and B, smoke drifting and embers rising, after Counter-Strike 2", false,
        Space: new(0.055f, 0.065f, 0.08f),
        Panel: new(0.075f, 0.085f, 0.1f, 0.88f),
        PanelSolid: new(0.085f, 0.095f, 0.11f, 0.97f),
        Header: new(0.12f, 0.13f, 0.15f, 0.95f),
        Field: new(0.03f, 0.035f, 0.045f, 0.6f),
        Edge: new(0.35f, 0.37f, 0.4f, 0.55f),
        EdgeBright: new(0.62f, 0.6f, 0.55f, 0.85f),
        Text: new(0.88f, 0.88f, 0.86f),
        TextBright: new(1.0f, 1.0f, 0.98f),
        Muted: new(0.6f, 0.61f, 0.62f),
        Accent: new(0.95f, 0.55f, 0.15f),
        Warm: new(0.98f, 0.78f, 0.3f),
        Red: new(0.92f, 0.25f, 0.22f),
        Green: new(0.4f, 0.85f, 0.4f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Avenir Next Condensed", "DIN Condensed", "Bahnschrift Condensed", "Arial Narrow", "Arial"],
        DisplayWeight: 800,
        Background: "res://Shaders/cs2_match.gdshader",
        Stamp: "cs-match.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Defense: a military theme, on the picture of soldiers of the French army with a tank, an armoured vehicle and
    /// helicopters on a field of dust (Assets/defense-field.jpg). Panels of a dark olive, a sand text, a tactical green
    /// for what is active, amber for what calls, condensed DIN capitals for the headers; behind the desk the picture
    /// itself, alive: a slow travelling, dust drifting, motes floating, the headlights glowing.
    /// </summary>
    public static readonly Palette Defense = new(
        "Defense", "Dark · a military theme: soldiers, a tank and helicopters on a field of dust, olive and sand with a tactical green", false,
        Space: new(0.07f, 0.075f, 0.055f),
        Panel: new(0.09f, 0.095f, 0.07f, 0.86f),
        PanelSolid: new(0.1f, 0.105f, 0.078f, 0.97f),
        Header: new(0.15f, 0.155f, 0.11f, 0.95f),
        Field: new(0.04f, 0.045f, 0.03f, 0.6f),
        Edge: new(0.5f, 0.47f, 0.33f, 0.5f),
        EdgeBright: new(0.75f, 0.7f, 0.5f, 0.85f),
        Text: new(0.9f, 0.87f, 0.76f),
        TextBright: new(0.97f, 0.95f, 0.86f),
        Muted: new(0.66f, 0.63f, 0.52f),
        Accent: new(0.58f, 0.76f, 0.36f),
        Warm: new(0.95f, 0.7f, 0.25f),
        Red: new(0.86f, 0.3f, 0.25f),
        Green: new(0.5f, 0.75f, 0.4f),
        BodyFonts: ["DIN Alternate", "Bahnschrift", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["DIN Condensed", "Bahnschrift Condensed", "Avenir Next Condensed", "Arial Narrow", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/defense_field.gdshader",
        Stamp: "defense-field.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Minecraft: after the game Minecraft, on the picture of a valley of blocks at sunset (Assets/minecraft-valley.jpg).
    /// Dark translucent panels like the menus of the game, a white text, the green of the grass for what is active, the
    /// yellow of its splash texts for what calls, a pixel font for the headers when one is installed; behind the desk the
    /// picture itself, alive: a slow travelling, clouds of blocks, the sun breathing, the river shimmering, square motes.
    /// </summary>
    public static readonly Palette Minecraft = new(
        "Minecraft", "Dark · a valley of blocks at sunset, clouds of blocks and a shimmering river, grass green and splash yellow, after Minecraft", false,
        Space: new(0.08f, 0.1f, 0.07f),
        Panel: new(0.1f, 0.1f, 0.1f, 0.84f),
        PanelSolid: new(0.12f, 0.12f, 0.12f, 0.97f),
        Header: new(0.18f, 0.18f, 0.18f, 0.95f),
        Field: new(0.0f, 0.0f, 0.0f, 0.55f),
        Edge: new(0.4f, 0.4f, 0.4f, 0.6f),
        EdgeBright: new(0.75f, 0.75f, 0.75f, 0.9f),
        Text: new(0.92f, 0.92f, 0.92f),
        TextBright: new(1.0f, 1.0f, 1.0f),
        Muted: new(0.63f, 0.63f, 0.63f),
        Accent: new(0.36f, 0.73f, 0.25f),
        Warm: new(0.99f, 0.86f, 0.02f),
        Red: new(0.9f, 0.25f, 0.2f),
        Green: new(0.45f, 0.8f, 0.3f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Minecraft", "Minecraftia", "Press Start 2P", "Silkscreen", "Menlo", "Courier New"],
        DisplayWeight: 400,
        Background: "res://Shaders/minecraft_valley.gdshader",
        Stamp: "minecraft-valley.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Mars: after the rover Curiosity of NASA (Mars Science Laboratory), on the picture of the rover in a crater at
    /// sunset (Assets/mars-curiosity.jpg). Panels of a dark rust, a light sand text, the peach orange of its title for
    /// what is active, the butterscotch of the sun for what calls, the red of NASA for alarms, condensed capitals for the
    /// headers; behind the desk the picture itself, alive: a slow travelling, red dust blowing, the sun breathing, stars.
    /// </summary>
    public static readonly Palette Mars = new(
        "Mars", "Dark · the rover Curiosity of NASA in a crater of Mars at sunset, red dust blowing, rust and peach orange", false,
        Space: new(0.09f, 0.045f, 0.035f),
        Panel: new(0.11f, 0.06f, 0.045f, 0.86f),
        PanelSolid: new(0.12f, 0.066f, 0.05f, 0.97f),
        Header: new(0.18f, 0.09f, 0.06f, 0.95f),
        Field: new(0.05f, 0.025f, 0.018f, 0.6f),
        Edge: new(0.6f, 0.35f, 0.22f, 0.5f),
        EdgeBright: new(0.9f, 0.6f, 0.4f, 0.85f),
        Text: new(0.95f, 0.88f, 0.8f),
        TextBright: new(1.0f, 0.96f, 0.9f),
        Muted: new(0.72f, 0.6f, 0.52f),
        Accent: new(0.95f, 0.6f, 0.35f),
        Warm: new(1.0f, 0.82f, 0.5f),
        Red: new(0.99f, 0.24f, 0.13f),
        Green: new(0.5f, 0.8f, 0.5f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["DIN Condensed", "Avenir Next Condensed", "Bahnschrift Condensed", "Futura", "Arial Narrow", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/mars_curiosity.gdshader",
        Stamp: "mars-curiosity.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Couture: after the haute couture of Paris, on the sketches of a collection on the table of an atelier
    /// (Assets/couture-sketches.jpg). Panels of a cream paper, an ink black text, the navy of velvet for what is active,
    /// the gold of the embroideries for what calls, burgundy for alarms, Didot capitals for the headers; behind the desk
    /// the picture itself, alive: a slow travelling, a soft light passing, flecks of gold drifting.
    /// </summary>
    public static readonly Palette Couture = new(
        "Couture", "Light · the sketches of a Paris haute couture collection: cream paper, ink, navy velvet and gold embroidery", true,
        Space: Color.FromHtml("#F2EADB"),
        Panel: Color.FromHtml("#FBF7EFF4"),
        PanelSolid: Color.FromHtml("#FCF9F2"),
        Header: Color.FromHtml("#FBF7EF00"),
        Field: Color.FromHtml("#FFFDF8"),
        Edge: Color.FromHtml("#DCCFB8"),
        EdgeBright: Color.FromHtml("#A8977A"),
        Text: Color.FromHtml("#2B2622"),
        TextBright: Color.FromHtml("#111014"),
        Muted: Color.FromHtml("#857866"),
        Accent: Color.FromHtml("#1F2B52"),
        Warm: Color.FromHtml("#B8913A"),
        Red: Color.FromHtml("#8E1F2F"),
        Green: Color.FromHtml("#4E7A57"),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Didot", "Bodoni 72", "Bodoni MT", "Playfair Display", "Georgia", "Times New Roman"],
        DisplayWeight: 400,
        Background: "res://Shaders/couture_atelier.gdshader",
        Stamp: "couture-sketches.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Silicon: after microelectronics, on the picture of the die of a processor on its circuit board
    /// (Assets/silicon-die.jpg). Panels of a dark teal like the solder mask, a pale silver text, the cyan of a signal for
    /// what is active, the gold of the die for what calls, technical capitals for the headers; behind the desk the
    /// picture itself, alive: a slow travelling, pulses of signal along the traces, an iridescent sheen over the die.
    /// </summary>
    public static readonly Palette Silicon = new(
        "Silicon", "Dark · the die of a processor on its circuit board, pulses of signal along the traces, teal, copper and gold", false,
        Space: new(0.02f, 0.07f, 0.09f),
        Panel: new(0.025f, 0.075f, 0.09f, 0.86f),
        PanelSolid: new(0.03f, 0.085f, 0.1f, 0.97f),
        Header: new(0.05f, 0.13f, 0.15f, 0.95f),
        Field: new(0.0f, 0.03f, 0.04f, 0.55f),
        Edge: new(0.3f, 0.5f, 0.52f, 0.5f),
        EdgeBright: new(0.75f, 0.68f, 0.5f, 0.85f),
        Text: new(0.84f, 0.89f, 0.9f),
        TextBright: new(0.96f, 0.98f, 0.98f),
        Muted: new(0.52f, 0.62f, 0.64f),
        Accent: new(0.25f, 0.78f, 0.75f),
        Warm: new(0.88f, 0.66f, 0.35f),
        Red: new(0.9f, 0.33f, 0.29f),
        Green: new(0.28f, 0.76f, 0.48f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["DIN Alternate", "Eurostile", "Bahnschrift", "Menlo", "Arial"],
        DisplayWeight: 600,
        Background: "res://Shaders/silicon_die.gdshader",
        Stamp: "silicon-die.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Robotics: after a robotics lab, on the picture of a humanoid robot in a workshop with drones, tools and monitors
    /// (Assets/robotics-lab.jpg). Panels of a light aluminium grey, a graphite text, the blue of the cables and benches
    /// for what is active, the orange of the tools for what calls, condensed technical capitals for the headers; behind
    /// the desk the picture itself, alive: a slow travelling, shafts of daylight, dust, the robot's light pulsing.
    /// </summary>
    public static readonly Palette Robotics = new(
        "Robotics", "Light · a humanoid robot in a robotics lab, drones and tools, daylight from the windows, aluminium, blue and orange", true,
        Space: Color.FromHtml("#E4E6E8"),
        Panel: Color.FromHtml("#F4F5F6F2"),
        PanelSolid: Color.FromHtml("#F6F7F8"),
        Header: Color.FromHtml("#F4F5F600"),
        Field: Color.FromHtml("#FFFFFF"),
        Edge: Color.FromHtml("#C9CED3"),
        EdgeBright: Color.FromHtml("#8E969E"),
        Text: Color.FromHtml("#24292E"),
        TextBright: Color.FromHtml("#0E1114"),
        Muted: Color.FromHtml("#6A737C"),
        Accent: Color.FromHtml("#1F6FD1"),
        Warm: Color.FromHtml("#F08A24"),
        Red: Color.FromHtml("#D93A3A"),
        Green: Color.FromHtml("#2E9E5B"),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["DIN Condensed", "Avenir Next Condensed", "Bahnschrift Condensed", "Arial Narrow", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/robotics_lab.gdshader",
        Stamp: "robotics-lab.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Aquarium: after a planted aquarium, on the picture of a nature tank seen from the front (Assets/aquarium-tank.jpg):
    /// driftwood and stones in moss, a small ruined arch, stem plants and red Ludwigia on black gravel, light falling from
    /// the surface. Panels of a deep water teal, a pale aqua text, the turquoise of the light in the water for what is
    /// active, the coral of the red plants for what calls, rounded capitals like bubbles for the headers; behind the desk
    /// the picture itself, alive: the surface shimmering, the plants swaying, caustics, rays, bubbles and neon tetras.
    /// </summary>
    public static readonly Palette Aquarium = new(
        "Aquarium", "Dark · a planted aquarium: plants swaying, caustics dancing on the moss and the stones, bubbles rising, a school of neon tetras", false,
        Space: new(0.03f, 0.09f, 0.095f),
        Panel: new(0.035f, 0.12f, 0.125f, 0.86f),
        PanelSolid: new(0.04f, 0.13f, 0.135f, 0.97f),
        Header: new(0.05f, 0.18f, 0.18f, 0.95f),
        Field: new(0.015f, 0.05f, 0.055f, 0.6f),
        Edge: new(0.3f, 0.6f, 0.55f, 0.5f),
        EdgeBright: new(0.55f, 0.88f, 0.8f, 0.85f),
        Text: new(0.86f, 0.95f, 0.92f),
        TextBright: new(0.95f, 1.0f, 0.98f),
        Muted: new(0.56f, 0.7f, 0.66f),
        Accent: new(0.36f, 0.85f, 0.8f),
        Warm: new(0.96f, 0.52f, 0.4f),
        Red: new(0.95f, 0.28f, 0.28f),
        Green: new(0.55f, 0.85f, 0.35f),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Inter", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Arial Rounded MT Bold", "Avenir Next", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/aquarium_tank.gdshader",
        Stamp: "aquarium-tank.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Kaonomi-Dark (once KaonomiNet): after www.kaonomi.net, on the picture of the site (Assets/kaonomi-net.jpg): the hooded robot of
    /// Kaonomi with its golden eye among its circuits and networks of agents. Panels of a night blue, a pale text, the
    /// gold of ".net" for what is active, the cyan of the circuits for what calls, the Rubik font of the site; behind the
    /// desk the picture shown whole, alive: the eye breathing, pulses along the circuits, the nodes twinkling.
    /// </summary>
    public static readonly Palette KaonomiDark = new(
        "Kaonomi-Dark", "Dark · the picture of www.kaonomi.net: the hooded robot with its golden eye among circuits and networks of agents, night blue and gold", false,
        Space: new(0.02f, 0.05f, 0.1f),
        Panel: new(0.03f, 0.065f, 0.12f, 0.86f),
        PanelSolid: new(0.035f, 0.075f, 0.135f, 0.97f),
        Header: new(0.05f, 0.1f, 0.18f, 0.95f),
        Field: new(0.0f, 0.02f, 0.05f, 0.55f),
        Edge: new(0.3f, 0.45f, 0.62f, 0.5f),
        EdgeBright: new(0.85f, 0.68f, 0.36f, 0.85f),
        Text: new(0.86f, 0.9f, 0.95f),
        TextBright: new(0.97f, 0.98f, 1.0f),
        Muted: new(0.55f, 0.63f, 0.73f),
        Accent: new(0.88f, 0.69f, 0.29f),
        Warm: new(0.31f, 0.76f, 0.97f),
        Red: new(0.92f, 0.32f, 0.3f),
        Green: new(0.33f, 0.8f, 0.55f),
        BodyFonts: ["Rubik", "Inter", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Rubik", "Inter", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/kaonomi_net.gdshader",
        Stamp: "kaonomi-net.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Kaonomi-White: Kaonomi-Dark in light, on the same picture of www.kaonomi.net (Assets/kaonomi-net.jpg), alive the
    /// same way (the eye breathing, pulses along the circuits, the nodes twinkling): panels of a bluish white, almost
    /// opaque, a night-blue text, the gold of ".net" (darker, to be read on white) for what is active, the cyan of the
    /// circuits for what calls, the Rubik font of the site.
    /// </summary>
    public static readonly Palette KaonomiWhite = new(
        "Kaonomi-White", "Light · Kaonomi-Dark in white on the same picture of www.kaonomi.net, alive: bluish white panels, night-blue text, gold and cyan", true,
        Space: Color.FromHtml("#E8EDF4"),
        Panel: Color.FromHtml("#F7F9FCF2"),
        PanelSolid: Color.FromHtml("#F8FAFC"),
        Header: Color.FromHtml("#F7F9FC00"),
        Field: Color.FromHtml("#FFFFFF"),
        Edge: Color.FromHtml("#C8D2DE"),
        EdgeBright: Color.FromHtml("#C49A45"),
        Text: Color.FromHtml("#16202E"),
        TextBright: Color.FromHtml("#0A0F18"),
        Muted: Color.FromHtml("#5E6B7C"),
        Accent: Color.FromHtml("#B5841C"),
        Warm: Color.FromHtml("#1C8FD0"),
        Red: Color.FromHtml("#D23C3A"),
        Green: Color.FromHtml("#2E9E5B"),
        BodyFonts: ["Rubik", "Inter", "Helvetica Neue", "Segoe UI", "Arial", "sans-serif"],
        DisplayFonts: ["Rubik", "Inter", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "res://Shaders/kaonomi_net.gdshader",
        Stamp: "kaonomi-net.jpg",
        StampWidth: 4096);
}
