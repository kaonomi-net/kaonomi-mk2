using System;
using System.Collections.Generic;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The bar of commands on the left, on the whole height, in the manner of the Neocom of EVE: at the top the K, under
/// it the eyes of the dock of the NeXT stations (<see cref="NextEyes"/>), which follow the pointer, then ›_ unfolds to
/// the right a text area with Send (a command or a prompt, run as if typed in the chat), then one icon per tool, in
/// colour (a black edge of 5 px around it under the pointer, a cyan line on its left while its window is open), /config
/// at the bottom. A click runs the command.
/// </summary>
public partial class Neocom : PanelContainer
{
    /// <summary>The tools of the bar, in its order: the command, its icon and its name (the same as the default chat).</summary>
    public static readonly (string Command, string Icon, string Name)[] Tools =
    [
        ("/work", "🧮", "Work (tape, calculator, graph)"),
        ("/pictures", "🖼", "Picture library"),
        ("/fooocus", "🎨", "Fooocus images"),
        ("/relation", "🕸", "Relations"),
        ("/ontology", "🌳", "Ontology"),
        ("/idea", "💡", "Ideas"),
        ("/journal", "📓", "Journal"),
        ("/wiki", "📚", "Wiki"),
        ("/obs", "🗒", "Notes (after Obsidian): a vault of Markdown notes, links, backlinks, graph"),
        ("/interview", "🎙", "Interviews"),
        ("/cv", "📇", "CVs"),
        ("/bpmnai", "🔀", "AIMN (from BPMN) diagrams"),
        ("/infra", "🏗", "Infrastructure diagrams"),
        ("/archi", "🏛", "Architecture repository (EA and AI, after LeanIX)"),
        ("/graphbase", "🕸", "The graph of graphbase in 3D (search, GraphQL)"),
        ("/dissimination", "📡", "Dissemination: the entities in the pages collected and analysed"),
        ("/todo", "✅", "To do today"),
        ("/support", "🛟", "Request support (a task of the kanban, project Support)"),
        ("/mindmap", "🧠", "Mind maps"),
        ("/write", "✍", "Ghost writer"),
        ("/youtube", "📺", "YouTube videos"),
        ("/music", "🎵", "Music player"),
        ("/piano", "🎹", "Piano: play by clicking the keys"),
        ("/looper", "🔁", "Looper: loops recorded over each other"),
        ("/synth", "🎛", "Synth: FM synthesis with six operators"),
        ("/sampler", "🎤", "Sampler: play a sound recorded with the microphone"),
        ("/paint", "🎨", "Paint: a drawing program"),
        ("/recorder", "🎙", "Audio recorder (VU meter, spectrum, formants, annotations)"),
        ("/completion", "☁", "Completions and word cloud"),
        ("/project", "🎯", "Projects (name, description, goals, tags)"),
        ("/kanban", "📋", "Kanban"),
        ("/intel", "🕵", "Intel"),
        ("/collectionplan", "🕷", "Collection plans (web scraper)"),
        ("/game:solitaire", "✚", "Game 1 · Morpion solitaire"),
        ("/fractal", "❄", "Fractal generator"),
        ("/game:chess", "♞", "Chess against the LLM"),
        ("/game:go", "⚫", "Go against the LLM"),
        ("/immune", "🧬", "Immune system: AI images and Office code"),
        ("/orleans", "🛰", "Orleans monitoring (web)"),
        ("/rest", "🔌", "REST API of the server (Swagger)"),
        ("/doc", "📖", "Documentation of Kaonomi (web)"),
        ("/prompts", "📝", "Prompts of the tools (versions)"),
        ("/emotion", "🎭", "What if emotion (system prompt)"),
        ("/models", "🧭", "Default models of each use (gateway)"),
        ("/video", "🎞", "Videos: player, selection, AI analyses"),
        ("/vision:move", "👁", "Vision: motion detection (OpenCV)"),
        ("/vision", "👁", "Vision: faces, expressions and objects (OpenCV, YOLOv8)"),
        ("/alarm", "⏰", "Alarm clock"),
        ("/calendar", "📅", "Calendar"),
        ("/message", "✉", "Messages: internal mail between the users"),
        ("/iq", "🧩", "IQ tests"),
        ("/pedago:test", "🧒", "Pedago: name the objects of a picture"),
        ("/activity", "🗒", "Activity journal (each SAVE)"),
        ("/folders", "📁", "Folders of windows"),
        ("/cache", "🗃", "Cache builder (prompts and completions)"),
        ("/search", "🔎", "Search every index (fuzzy)"),
        ("/json", "🌐", "HTTP JSON client"),
        ("/cmd", "⌨", "Console (cmd.exe)"),
        ("/symbol", "🔣", "Symbolic patterns"),
        ("/remote", "🎛", "Remote (voice and avatar)"),
        ("/system", "🖥", "System activity"),
        ("/log", "📜", "Logs dashboard"),
        ("/performance", "⏱", "Performance dashboard"),
        ("/token", "🪙", "Tokens dashboard")
    ];

    public static readonly (string Command, string Icon, string Name) Settings = ("/config", "⚙", "Edit config.yml");

    /// <summary>The screen of the themes (EVE, Kaonomi…), at the bottom of the bar.</summary>
    public static readonly (string Command, string Icon, string Name) Themes = ("themes", "🎨", "Themes: choose the look of the chat");

    /// <summary>Every window of the desktop to the back (cards in 3D at the bottom).</summary>
    public static readonly (string Command, string Icon, string Name) AllBack = ("all-back", "⏬", "Send every window to the back");

    /// <summary>Every window of the desktop back to the front.</summary>
    public static readonly (string Command, string Icon, string Name) AllFront = ("all-front", "⏫", "Bring every window to the front");

    /// <summary>The icon of the tree of the windows: a folder and, under it on a branch, two folders.</summary>
    private const string TreeIcon = """
        <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">
        <path d="M5.3 7.2v8.3h4.6M5.3 11.3h4.6" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        <path d="M2 2.7c0-.4.3-.7.7-.7h2.6l1.1 1.2h3.4c.4 0 .7.3.7.7v2.8c0 .4-.3.7-.7.7H2.7c-.4 0-.7-.3-.7-.7z" fill="#fff"/>
        <path d="M10.6 9.4c0-.4.3-.7.7-.7h2l.9 1h3.4c.4 0 .7.3.7.7v2c0 .4-.3.7-.7.7h-6.3c-.4 0-.7-.3-.7-.7z" fill="#fff"/>
        <path d="M10.6 13.6c0-.4.3-.7.7-.7h2l.9 1h3.4c.4 0 .7.3.7.7v2c0 .4-.3.7-.7.7h-6.3c-.4 0-.7-.3-.7-.7z" fill="#fff"/>
        </svg>
        """;

    /// <summary>The windows of the desktop now (their key, title and icon), shown first in the tree of the windows.</summary>
    public Func<IReadOnlyList<(string Key, string Title, string Icon)>>? OpenWindows { get; set; }

    /// <summary>Raised with the key of a window open now, clicked in the tree of the windows (it comes to the front).</summary>
    public event Action<string>? WindowRequested;

    private readonly Dictionary<string, Button> _buttons = [];
    private WindowTree _windowTree = null!;
    private Button _treeToggle = null!;
    private PopupPanel _commandPopup = null!;
    private TextEdit _commandInput = null!;
    private Button _commandToggle = null!;

    /// <summary>Raised with the command of the icon clicked, or with what was typed in the text area.</summary>
    public event Action<string>? CommandRequested;

    public Neocom()
    {
        CustomMinimumSize = new Vector2(52, 0);
        // A bar floating over the desktop, like the chat (Eve.Floating): rounded corners, a hairline edge, a shadow.
        AddThemeStyleboxOverride("panel", Eve.FloatingBox(Eve.IsLight ? Eve.Panel : new Color(0.02f, 0.025f, 0.03f, 0.92f)));

        // What it holds is cut along its rounded corners (the mark of an open tool, the edge under the pointer).
        var clip = Eve.RoundedClip();
        AddChild(clip);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        clip.AddChild(column);

        // The emblem and the command box.
        var emblem = Eve.Caption("K", Eve.Cyan, 20);
        emblem.HorizontalAlignment = HorizontalAlignment.Center;
        emblem.TooltipText = "KAONOMI · double-click: full screen (again: back)";
        emblem.MouseFilter = MouseFilterEnum.Stop;
        emblem.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true, Pressed: true })
            {
                ToggleFullScreen();
                emblem.AcceptEvent();
            }
        };
        column.AddChild(Spacer(6));
        column.AddChild(emblem);
        column.AddChild(Spacer(4));
        column.AddChild(new NextEyes());
        column.AddChild(Spacer(4));
        _commandToggle = new Button
        {
            Text = "›_",
            ToggleMode = true,
            CustomMinimumSize = new Vector2(40, 36),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            TooltipText = "Type a command (or a prompt) and run it",
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        _commandToggle.AddThemeFontOverride("font", Eve.Mono);
        _commandToggle.AddThemeFontSizeOverride("font_size", 15);
        _commandToggle.Toggled += on =>
        {
            if (on)
                OpenCommandBox();
            else
                _commandPopup.Hide();
        };
        column.AddChild(_commandToggle);
        column.AddChild(Spacer(4));
        // Under ›_: the tree of the windows, grouped by category (WindowTree).
        _treeToggle = new Button
        {
            ToggleMode = true,
            Icon = Eve.Icon(TreeIcon),
            IconAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(40, 36),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            TooltipText = "The windows, by category (a tree): click one to open it",
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        var pressedIcon = Eve.IsLight ? Eve.Cyan.Darkened(0.15f) : Eve.Ink;
        _treeToggle.AddThemeColorOverride("icon_normal_color", Eve.Text);
        _treeToggle.AddThemeColorOverride("icon_focus_color", Eve.Text);
        _treeToggle.AddThemeColorOverride("icon_hover_color", Eve.Cyan);
        _treeToggle.AddThemeColorOverride("icon_pressed_color", pressedIcon);
        _treeToggle.AddThemeColorOverride("icon_hover_pressed_color", pressedIcon);
        _treeToggle.Toggled += on =>
        {
            if (on)
                OpenWindowTree();
            else
                _windowTree.Hide();
        };
        column.AddChild(_treeToggle);
        column.AddChild(Spacer(4));
        var line = Eve.Separator();
        column.AddChild(line);
        column.AddChild(Spacer(4));

        var tools = new VBoxContainer();
        tools.AddThemeConstantOverride("separation", 2);
        foreach (var tool in Tools)
            tools.AddChild(IconButton(tool));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever };
        scroll.AddChild(tools);
        column.AddChild(scroll);
        column.AddChild(Eve.Separator());
        column.AddChild(IconButton(AllBack));
        column.AddChild(IconButton(AllFront));
        column.AddChild(IconButton(Themes));
        column.AddChild(IconButton(Settings));
        column.AddChild(Spacer(8));

        BuildCommandBox();
        BuildWindowTree();
    }

    // ── The tree of the windows ──

    private void BuildWindowTree()
    {
        _windowTree = new WindowTree(() => OpenWindows?.Invoke() ?? []);
        _windowTree.CommandRequested += command => CommandRequested?.Invoke(command);
        _windowTree.WindowRequested += key => WindowRequested?.Invoke(key);
        _windowTree.PopupHide += () => _treeToggle.SetPressedNoSignal(false);
        AddChild(_windowTree);
    }

    /// <summary>Opens the tree of the windows, its filter set to <paramref name="filter"/> (--window-tree=vision).</summary>
    public void ShowWindowTree(string filter = "")
    {
        _treeToggle.ButtonPressed = true;
        _windowTree.Filter(filter);
    }

    private void OpenWindowTree()
    {
        // Unfolds to the right of the bar, its top at the height of the icon.
        _commandPopup.Hide();
        var at = _treeToggle.GetScreenPosition() + new Vector2(_treeToggle.Size.X + 10, -6);
        _windowTree.Open(at, GetViewport().GetVisibleRect().Size.Y);
    }

    private static Control Spacer(int height) => new Control { CustomMinimumSize = new Vector2(0, height), MouseFilter = MouseFilterEnum.Ignore };

    private Control IconButton((string Command, string Icon, string Name) tool)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(44, 40),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            Flat = true,
            FocusMode = FocusModeEnum.None,
            TooltipText = $"{tool.Name}  ·  {tool.Command}",
            MouseDefaultCursorShape = CursorShape.PointingHand
        };
        // Under the pointer: a black edge of 5 px around the icon (the icon itself stays in colour).
        var hover = Eve.Box(new Color(0, 0, 0, 0), Colors.Black, 0, 5);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        var icon = new Label { Text = tool.Icon, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.AddThemeFontSizeOverride("font_size", 20);
        button.AddChild(icon);
        _buttons[tool.Command] = button;
        button.Pressed += () => CommandRequested?.Invoke(tool.Command);
        return button;
    }

    /// <summary>The mark of a tool whose window is open: a cyan line on the left of its icon.</summary>
    private static StyleBox OpenBox()
    {
        var box = Eve.Box(Eve.Cyan with { A = 0.1f }, Eve.Cyan with { A = 0f }, 0, 0);
        box.BorderWidthLeft = 2;
        box.BorderColor = Eve.Cyan;
        return box;
    }

    /// <summary>The state of the window before the full screen of the K, given back by a second double-click.</summary>
    private DisplayServer.WindowMode _beforeFullScreen = DisplayServer.WindowMode.Windowed;

    /// <summary>Double-click on the K: the chat in full screen; again: back to how it was (windowed or maximised).</summary>
    private void ToggleFullScreen()
    {
        var mode = DisplayServer.WindowGetMode();
        if (mode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen)
            DisplayServer.WindowSetMode(_beforeFullScreen is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen
                ? DisplayServer.WindowMode.Windowed : _beforeFullScreen);
        else
        {
            _beforeFullScreen = mode;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
    }

    /// <summary>Marks the icon of <paramref name="command"/> (a cyan line on its left) while its window is open.</summary>
    public void SetOpen(string command, bool open)
    {
        if (_buttons.TryGetValue(command, out var button))
            button.AddThemeStyleboxOverride("normal", open ? OpenBox() : new StyleBoxEmpty());
    }

    // ── The command box ──

    private void BuildCommandBox()
    {
        _commandPopup = new PopupPanel { Transparent = false };
        _commandPopup.AddThemeStyleboxOverride("panel", Eve.Box(Eve.PanelSolid, Eve.Cyan with { A = 0.7f }, 8));
        var row = new HBoxContainer();
        _commandInput = new TextEdit
        {
            CustomMinimumSize = new Vector2(460, 40),
            PlaceholderText = "A command (/work, /relation…) or a prompt · Enter: send · Shift+Enter: new line · Esc: close",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            ScrollFitContentHeight = true
        };
        _commandInput.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter, ShiftPressed: false })
            {
                _commandInput.AcceptEvent();
                Send();
            }
            else if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                _commandInput.AcceptEvent();
                _commandPopup.Hide();
            }
        };
        row.AddChild(_commandInput);
        var send = Eve.Button("SEND", primary: true);
        send.CustomMinimumSize = new Vector2(76, 40);
        send.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        send.Pressed += Send;
        row.AddChild(send);
        _commandPopup.AddChild(row);
        _commandPopup.PopupHide += () => _commandToggle.SetPressedNoSignal(false);
        AddChild(_commandPopup);
    }

    private void OpenCommandBox()
    {
        // Unfolds to the right of the bar, next to ›_, growing from it.
        var at = _commandToggle.GetScreenPosition() + new Vector2(_commandToggle.Size.X + 10, -6);
        _commandPopup.Popup(new Rect2I((Vector2I)at, new Vector2I(60, 56)));
        var tween = _commandPopup.CreateTween();
        tween.TweenProperty(_commandPopup, "size", new Vector2I(560, 56), 0.16).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _commandInput.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Send()
    {
        var text = _commandInput.Text.Trim();
        if (text.Length == 0)
            return;
        _commandInput.Text = string.Empty;
        _commandPopup.Hide();
        CommandRequested?.Invoke(text);
    }
}
