using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The tree of the windows (the icon of a tree of folders under ›_ in the bar of the tools): every window of the chat,
/// grouped by what it is for. The windows open now come first, then documents, projects, knowledge, diagrams, media,
/// vision, the assistant, the AI models, games and the system. A click opens a window (or brings back the one open),
/// a click on a category folds it or unfolds it (kept while the chat runs), the filter keeps what matches its name or
/// its command and Enter opens the first one.
/// </summary>
public partial class WindowTree : PopupPanel
{
    /// <summary>A window: the command that opens it, its icon and its name.</summary>
    public sealed record Entry(string Command, string Icon, string Name);

    /// <summary>A category of windows: its name, its icon and the commands of its windows, in their order.</summary>
    public sealed record Category(string Name, string Icon, string[] Commands);

    /// <summary>The windows that have no icon in the bar, opened by their command only.</summary>
    private static readonly Entry[] Extra =
    [
        new("/fullchat", "💬", "Full chat in a window (3D animation, microphone)"),
        new("/preset", "🗣", "Presets of the avatar (voice, animation, system prompt)"),
        new("/gateway", "🚦", "LLM gateway (semantic cache, routing)"),
        new("/process", "🔧", "Processes of KAONOMI (start, stop, restart)"),
        new("/setup", "🧰", "Setup: the dependencies of the chat"),
        new("/user", "🪪", "My profile (names, contact, hashtags, photo)"),
        new("/users", "👥", "Users and their levels (administrators only)"),
        new("/password", "🔑", "Change your password"),
        new("/audiosettings", "🔊", "Audio devices (output, microphone)")
    ];

    /// <summary>The categories, in their order (a window of the bar in none of them goes into "Other").</summary>
    public static readonly Category[] Categories =
    [
        new("Documents & writing", "✍", ["/work", "/write", "/journal", "/wiki", "/obs", "/idea", "/cv"]),
        new("Projects & planning", "🎯", ["/todo", "/project", "/kanban", "/support", "/calendar", "/message", "/alarm", "/folders", "/activity"]),
        new("Knowledge & intelligence", "🧠", ["/relation", "/ontology", "/intel", "/collectionplan", "/dissimination", "/interview", "/search", "/symbol", "/graphbase"]),
        new("Diagrams & maps", "🔀", ["/mindmap", "/bpmnai", "/infra", "/archi"]),
        new("Images, sound & video", "🎞", ["/pictures", "/paint", "/fooocus", "/fractal", "/video", "/youtube", "/music", "/piano", "/synth", "/sampler", "/looper", "/recorder"]),
        new("Vision (camera)", "👁", ["/vision", "/vision:move"]),
        new("Assistant & avatar", "🎭", ["/fullchat", "/preset", "/emotion", "/remote"]),
        new("AI models & prompts", "🧭", ["/models", "/gateway", "/prompts", "/completion", "/cache", "/iq", "/immune"]),
        new("Games", "♞", ["/game:solitaire", "/game:chess", "/game:go", "/pedago:test"]),
        new("System & tools", "🖥", ["/system", "/process", "/log", "/performance", "/token", "/orleans", "/rest", "/doc", "/cmd", "/json", "/setup", "/audiosettings", "/config", "/user", "/users", "/password"])
    ];

    private const string OpenNow = "Open now";
    private const string Other = "Other";

    /// <summary>The categories unfolded (the windows open now at first), kept from one opening to the next.</summary>
    private static readonly HashSet<string> Unfolded = [OpenNow];

    /// <summary>Every window by its command: the ones of the bar, then the others.</summary>
    private static readonly Dictionary<string, Entry> Entries = Neocom.Tools.Append(Neocom.Settings)
        .Select(t => new Entry(t.Command, t.Icon, t.Name))
        .Concat(Extra)
        .GroupBy(e => e.Command)
        .ToDictionary(g => g.Key, g => g.First());

    /// <summary>Raised with the command of the window clicked (it opens, or the one open comes back).</summary>
    public event Action<string>? CommandRequested;

    /// <summary>Raised with the key of a window open now that was clicked (it comes to the front).</summary>
    public event Action<string>? WindowRequested;

    private readonly Func<IReadOnlyList<(string Key, string Title, string Icon)>> _openWindows;
    private readonly Tree _tree;
    private readonly LineEdit _filter;
    private readonly Label _count;

    /// <param name="openWindows">The windows of the desktop now: their key, their title and their icon.</param>
    public WindowTree(Func<IReadOnlyList<(string Key, string Title, string Icon)>> openWindows)
    {
        _openWindows = openWindows;
        Transparent = true;
        TransparentBg = true;
        // A thin frame around the list: 6 px of the panel between its edge and the list.
        AddThemeStyleboxOverride("panel", Eve.Box(Eve.PanelSolid, Eve.EdgeBright with { A = 0.6f }, Frame, 1, Eve.FloatCorner));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Frame);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddChild(Eve.Caption("Windows", Eve.TextBright, 12));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _count = Eve.Caption("", Eve.Muted, 10);
        header.AddChild(_count);
        column.AddChild(Inset(header));

        // Made first: the keys of the filter move into it.
        _tree = new Tree
        {
            Columns = 2,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ScrollHorizontalEnabled = false
        };

        _filter = new LineEdit { PlaceholderText = "Filter: a name or a /command", ClearButtonEnabled = true };
        _filter.TextChanged += _ => Fill();
        _filter.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } key)
                return;
            if (key.Keycode is Key.Enter or Key.KpEnter)
            {
                _filter.AcceptEvent();
                Activate(_tree.GetSelected() ?? FirstWindow(), byMouse: false);
            }
            else if (key.Keycode == Key.Down)
            {
                _filter.AcceptEvent();
                _tree.GrabFocus();
                if (_tree.GetSelected() is null && FirstWindow() is { } first)
                    first.Select(0);
            }
            else if (key.Keycode == Key.Escape)
            {
                _filter.AcceptEvent();
                Hide();
            }
        };
        column.AddChild(_filter);

        _tree.SetColumnExpand(0, true);
        _tree.SetColumnExpand(1, false);
        _tree.SetColumnCustomMinimumWidth(1, 118);
        _tree.AddThemeConstantOverride("v_separation", 6);
        _tree.AddThemeConstantOverride("item_margin", 14);
        // A click: a window opens, a category folds or unfolds; Enter (or a double-click) opens the window selected.
        _tree.ItemMouseSelected += (_, button) =>
        {
            if (button == (long)MouseButton.Left)
                Activate(_tree.GetSelected(), byMouse: true);
        };
        _tree.ItemActivated += () => Activate(_tree.GetSelected(), byMouse: false);
        _tree.ItemCollapsed += item =>
        {
            // Folded or unfolded by hand (not by the filter, which unfolds everything it keeps): remembered.
            if (_filter.Text.Trim().Length > 0 || item.GetMetadata(0).AsString() is not { Length: > 0 } meta || !meta.StartsWith("category:", StringComparison.Ordinal))
                return;
            var name = meta["category:".Length..];
            if (item.Collapsed)
                Unfolded.Remove(name);
            else
                Unfolded.Add(name);
        };
        column.AddChild(_tree);

        column.AddChild(Inset(Eve.Caption("Click: open  ·  Enter: the first one  ·  Esc: close", Eve.Muted, 9)));
    }

    /// <summary>The room between the edge of the tree and its list (and between its parts).</summary>
    private const int Frame = 6;

    /// <summary>A line of text a little in from the edge (the title, the hint), the list and the filter at the edge.</summary>
    private static MarginContainer Inset(Control control)
    {
        var inset = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        inset.AddThemeConstantOverride("margin_left", 4);
        inset.AddThemeConstantOverride("margin_right", 4);
        inset.AddChild(control);
        return inset;
    }

    /// <summary>Opens the tree with its top left corner at <paramref name="at"/>, as high as the screen allows.</summary>
    public void Open(Vector2 at, float screenHeight)
    {
        _filter.Text = string.Empty;
        Fill();
        var height = (int)Math.Clamp(screenHeight - at.Y - 24, 320, 780);
        Popup(new Rect2I((Vector2I)at, new Vector2I(460, height)));
        _filter.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>The filter set to <paramref name="text"/>: the tree keeps what matches.</summary>
    public void Filter(string text)
    {
        _filter.Text = text;
        _filter.CaretColumn = text.Length;
        Fill();
    }

    /// <summary>The tree again, with the windows open now and the filter.</summary>
    private void Fill()
    {
        _tree.Clear();
        var root = _tree.CreateItem();
        var filter = _filter.Text.Trim();
        bool Matches(string name, string command) =>
            filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase) || command.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var open = _openWindows();
        var openKeys = open.Select(w => w.Key).ToHashSet();
        var shown = 0;

        // The windows open now: a click brings one to the front.
        var openShown = open.Where(w => Matches(w.Title, w.Key)).ToList();
        if (openShown.Count > 0)
        {
            var category = AddCategory(root, OpenNow, "●", openShown.Count, filter.Length > 0);
            category.SetCustomColor(0, Eve.Green);
            foreach (var window in openShown)
            {
                var item = AddWindow(category, window.Icon, window.Title, window.Key.StartsWith('/') ? window.Key : "", $"window:{window.Key}");
                item.SetTooltipText(0, $"{window.Title}  ·  click: to the front");
            }
            shown += openShown.Count;
        }

        // The windows by category, then the ones of the bar in no category.
        var listed = Categories.SelectMany(c => c.Commands).ToHashSet();
        var others = Neocom.Tools.Select(t => t.Command).Where(c => !listed.Contains(c)).ToArray();
        foreach (var (name, icon, commands) in Categories.Select(c => (c.Name, c.Icon, c.Commands)).Append((Other, "…", others)))
        {
            var entries = commands.Select(c => Entries.GetValueOrDefault(c)).OfType<Entry>().Where(e => Matches(e.Name, e.Command)).ToList();
            if (entries.Count == 0)
                continue;
            var category = AddCategory(root, name, icon, entries.Count, filter.Length > 0);
            foreach (var entry in entries)
            {
                var isOpen = openKeys.Contains(entry.Command);
                var item = AddWindow(category, entry.Icon, entry.Name, isOpen ? $"● {entry.Command}" : entry.Command, $"command:{entry.Command}");
                item.SetTooltipText(0, $"{entry.Name}  ·  {entry.Command}{(isOpen ? "  ·  open: click to bring it back" : "")}");
                if (isOpen)
                {
                    item.SetCustomColor(0, Eve.Cyan);
                    item.SetCustomColor(1, Eve.Cyan);
                }
            }
            shown += entries.Count;
        }

        _count.Text = filter.Length == 0
            ? $"{Entries.Count} windows · {open.Count} open"
            : $"{shown} found";
        // Filtering: the first window found is selected (Enter opens it).
        if (filter.Length > 0)
            FirstWindow()?.Select(0);
    }

    private TreeItem AddCategory(TreeItem root, string name, string icon, int count, bool filtering)
    {
        var item = _tree.CreateItem(root);
        item.SetText(0, $"{icon}   {name.ToUpperInvariant()}");
        item.SetCustomFont(0, Eve.Display);
        item.SetCustomFontSize(0, 11);
        item.SetCustomColor(0, Eve.TextBright);
        item.SetText(1, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        item.SetTextAlignment(1, HorizontalAlignment.Right);
        item.SetCustomColor(1, Eve.Muted);
        item.SetCustomFontSize(1, 10);
        item.SetMetadata(0, $"category:{name}");
        item.SetTooltipText(0, $"{name}: click to fold or unfold");
        item.Collapsed = !filtering && !Unfolded.Contains(name);
        return item;
    }

    private TreeItem AddWindow(TreeItem category, string icon, string name, string command, string meta)
    {
        var item = _tree.CreateItem(category);
        item.SetText(0, $"{icon}   {name}");
        item.SetTextOverrunBehavior(0, TextServer.OverrunBehavior.TrimEllipsis);
        item.SetText(1, command);
        item.SetTextAlignment(1, HorizontalAlignment.Right);
        item.SetTextOverrunBehavior(1, TextServer.OverrunBehavior.TrimEllipsis);
        item.SetCustomFont(1, Eve.Mono);
        item.SetCustomFontSize(1, 10);
        item.SetCustomColor(1, Eve.Muted);
        item.SetMetadata(0, meta);
        return item;
    }

    /// <summary>The first window of the tree (not a category), or null.</summary>
    private TreeItem? FirstWindow()
    {
        for (var category = _tree.GetRoot()?.GetFirstChild(); category is not null; category = category.GetNext())
            if (category.GetFirstChild() is { } first)
                return first;
        return null;
    }

    /// <summary>A window: it opens (or comes to the front) and the tree closes; a category clicked: it folds or unfolds.</summary>
    private void Activate(TreeItem? item, bool byMouse)
    {
        if (item?.GetMetadata(0).AsString() is not { Length: > 0 } meta)
            return;
        if (meta.StartsWith("category:", StringComparison.Ordinal))
        {
            if (byMouse)
            {
                item.Collapsed = !item.Collapsed;
                item.Deselect(0);
            }
            return;
        }
        Hide();
        if (meta.StartsWith("window:", StringComparison.Ordinal))
            WindowRequested?.Invoke(meta["window:".Length..]);
        else if (meta.StartsWith("command:", StringComparison.Ordinal))
            CommandRequested?.Invoke(meta["command:".Length..]);
    }
}
