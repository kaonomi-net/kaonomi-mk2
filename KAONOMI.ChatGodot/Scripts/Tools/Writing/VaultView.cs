using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Writing;

/// <summary>
/// /obs: the notes, after Obsidian. Header: the vault (📂 VAULT opens another folder, an Obsidian vault too), ✚ NOTE,
/// 📁+ FOLDER, 📅 DAILY, ✎ RENAME (the links follow), 🗑 DELETE (to the .trash of the vault), the mode EDIT / SPLIT /
/// PREVIEW and 🕸 GRAPH. Left: the search (words, or #tag), the tree of the folders and notes, the tags of the vault.
/// Middle: the editor of the note (wiki links, code and headings coloured) and its preview (its [[links]] and #tags can
/// be clicked: a link to a note that does not exist yet creates it), or the graph of the notes and their links. Right:
/// the backlinks of the note (with their line), its links, its outline (a click goes to the heading) and its tags.
/// A note is saved by itself a second after a change.
/// </summary>
[ToolView(typeof(VaultWindowViewModel), "Notes", "🗒", 1560, 920)]
public sealed class VaultView : IToolView
{
    public Control Build(object viewModel, Shell shell) => new VaultPage((VaultWindowViewModel)viewModel);
}

public sealed partial class VaultPage : VBoxContainer
{
    private readonly VaultWindowViewModel _vm = null!;
    private readonly Tree _tree = new();
    private readonly VBoxContainer _results = new();
    private readonly HFlowContainer _tags = new();
    private readonly LineEdit _search = new() { PlaceholderText = "🔎 Search the notes (words, #tag, or project:Name)", ClearButtonEnabled = true };
    private readonly CodeEdit _editor = new();
    private readonly RichTextLabel _preview = Ui.Markdown();
    private readonly Label _title = E.Text_("", E.TextBright, 18);
    private readonly VBoxContainer _side = new();
    private readonly VaultGraph _graph = null!;
    private readonly Control _editorPane = null!, _previewPane = null!;
    private readonly HBoxContainer _middle = new();
    private string _mode = "split";
    private bool _showGraph, _filling, _previewStale;

    // Godot builds its scripts with this constructor.
    public VaultPage()
    {
    }

    public VaultPage(VaultWindowViewModel vm)
    {
        _vm = vm;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
        _graph = new VaultGraph(vm) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        _graph.NoteClicked += note =>
        {
            _vm.Open(note);
            _showGraph = false;
            Layout();
        };

        // ── Header ──
        var vault = E.Text_("", E.Muted, 11);
        vault.ClipText = true;
        vault.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vault.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Bind.When(vault, vm, () => vault.Text = vm.Vault, nameof(vm.Vault));
        var mode = new OptionButton { TooltipText = "Edit, edit beside the preview, or the preview alone" };
        foreach (var m in new[] { "✎ EDIT", "◧ SPLIT", "👁 PREVIEW" })
            mode.AddItem(m);
        mode.Selected = 1;
        mode.ItemSelected += i =>
        {
            _mode = i switch { 0 => "edit", 2 => "preview", _ => "split" };
            _showGraph = false;
            Layout();
        };
        var delete = Ui.Action("🗑 DELETE", () => _ = DeleteAsync(), tooltip: "Move the note to the trash of the vault (.trash): nothing is erased");
        delete.AddThemeColorOverride("font_color", E.Red);
        AddChild(Ui.Toolbar(Ui.Row(8, [
            E.Caption("Notes", E.TextBright, 14),
            vault,
            Ui.Action("📂 VAULT", () => _ = OpenVaultAsync(), tooltip: "Open another folder as the vault (an Obsidian vault too)"),
            Ui.Action("✚ NOTE", () => _ = NewNoteAsync(), primary: true, tooltip: "A new note (\"Folder/Name\" puts it in a folder)"),
            Ui.Action("📁+ FOLDER", () => _ = NewFolderAsync(), tooltip: "A new folder of the vault"),
            Ui.Action("📅 DAILY", _vm.Daily, tooltip: "The note of today, in the folder Daily"),
            Ui.Action("✎ RENAME", () => _ = RenameAsync(), tooltip: "Rename the note: the links to it in the other notes follow"),
            delete,
            mode,
            Ui.Action("🕸 GRAPH", () => { _showGraph = !_showGraph; Layout(); }, tooltip: "The notes and their links (a click opens a note)")])));

        // ── Left: search, tree, tags ──
        _search.TextChanged += _ => FillLeft();
        _tree.HideRoot = true;
        _tree.SizeFlagsVertical = SizeFlags.ExpandFill;
        _tree.ItemSelected += () =>
        {
            if (_filling || _tree.GetSelected()?.GetMetadata(0).AsString() is not { } meta || !meta.StartsWith("note:", StringComparison.Ordinal))
                return;
            if (_vm.Notes.FirstOrDefault(n => n.Path == meta[5..]) is { } note)
            {
                _vm.Open(note);
                _showGraph = false;
                Layout();
            }
        };
        _results.AddThemeConstantOverride("separation", 4);
        var resultsScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        _results.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        resultsScroll.AddChild(_results);
        _tags.AddThemeConstantOverride("h_separation", 4);
        _tags.AddThemeConstantOverride("v_separation", 4);
        var tagsScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 130), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tags.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tagsScroll.AddChild(_tags);
        var left = Ui.Column(6, _search, _tree, resultsScroll, E.Caption("Tags", E.Muted, 11), tagsScroll);
        left.CustomMinimumSize = new Vector2(270, 0);

        // ── Middle: the editor, the preview, the graph ──
        var highlighter = new CodeHighlighter
        {
            NumberColor = E.Text, SymbolColor = E.Text, FunctionColor = E.Text, MemberVariableColor = E.Text
        };
        highlighter.AddColorRegion("[[", "]]", E.Cyan);
        highlighter.AddColorRegion("```", "```", E.Amber);
        highlighter.AddColorRegion("`", "`", E.Amber, true);
        highlighter.AddColorRegion("# ", "", E.TextBright, true);
        highlighter.AddColorRegion("> ", "", E.Muted, true);
        _editor.SyntaxHighlighter = highlighter;
        _editor.WrapMode = TextEdit.LineWrappingMode.Boundary;
        _editor.GuttersDrawLineNumbers = false;
        _editor.SizeFlagsVertical = SizeFlags.ExpandFill;
        _editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _editor.AddThemeFontSizeOverride("font_size", 15);
        _editor.AddThemeConstantOverride("line_spacing", 6);
        _editor.PlaceholderText = "Write in Markdown: # headings, **bold**, - lists, [[links]], #tags…";
        _editor.TextChanged += () =>
        {
            if (_filling)
                return;
            _vm.Text = _editor.Text;
            _previewStale = true;
        };
        _editorPane = _editor;
        // The links of the preview are the ones of the vault (not the links of the chat: obs: and obstag: are ours).
        _preview.SetMeta("links_hooked", true);
        // A click on a link opens it (a selection would take the click first): the text is selected in the editor.
        _preview.SelectionEnabled = false;
        _preview.MetaClicked += meta => OnLink(meta.AsString());
        _preview.AddThemeFontSizeOverride("normal_font_size", 15);
        var previewScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var previewMargin = Ui.Margin(_preview, 10);
        previewMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        previewScroll.AddChild(previewMargin);
        var previewPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        previewPanel.AddThemeStyleboxOverride("panel", E.Box(E.Field, E.Edge with { A = 0.4f }, 4));
        previewPanel.AddChild(previewScroll);
        _previewPane = previewPanel;
        _middle.AddThemeConstantOverride("separation", 8);
        _middle.SizeFlagsVertical = SizeFlags.ExpandFill;
        _middle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _middle.AddChild(_editorPane);
        _middle.AddChild(_previewPane);
        _middle.AddChild(_graph);
        _title.ClipText = true;
        var center = Ui.Column(6, _title, _middle);
        center.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        center.SizeFlagsVertical = SizeFlags.ExpandFill;

        // ── Right: backlinks, links, outline, tags ──
        _side.AddThemeConstantOverride("separation", 6);
        _side.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var sideScroll = new ScrollContainer { CustomMinimumSize = new Vector2(290, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        sideScroll.AddChild(_side);

        var body = Ui.Row(10, left, center, sideScroll);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(body);
        AddChild(Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));

        // A note is saved a second after a change; the preview follows the text.
        var timer = new Timer { WaitTime = 1.0, Autostart = true };
        timer.Timeout += () =>
        {
            if (_vm.IsDirty)
            {
                _vm.SaveIfDirty();
                FillSide();
            }
        };
        AddChild(timer);
        var previewTimer = new Timer { WaitTime = 0.35, Autostart = true };
        previewTimer.Timeout += () =>
        {
            if (!_previewStale)
                return;
            _previewStale = false;
            if (_previewPane.Visible)
                Ui.SetMarkdown(_preview, _vm.PreviewMarkdown());
        };
        AddChild(previewTimer);

        _vm.Changed += OnVaultChanged;
        _vm.Opened += OnOpened;
        // The names of the projects arrive from the server: the title and the tree follow.
        KAONOMI.Chat.Services.ProjectCatalog.Changed += OnProjects;
        Bind.OnGone(this, () =>
        {
            KAONOMI.Chat.Services.ProjectCatalog.Changed -= OnProjects;
            _vm.Changed -= OnVaultChanged;
            _vm.Opened -= OnOpened;
            _vm.SaveIfDirty();
        });
        FillLeft();
        OnOpened();
        Layout();
    }

    private void OnVaultChanged() => Bind.OnMain(() =>
    {
        if (!IsInstanceValid(this))
            return;
        FillLeft();
        if (_graph.Visible)
            _graph.Reset();
    });

    private void OnProjects() => Bind.OnMain(() =>
    {
        if (!IsInstanceValid(this))
            return;
        UpdateTitle();
        FillLeft();
    });

    private void UpdateTitle() =>
        _title.Text = _vm.Current is { } note
            ? (note.Folder.Length > 0 ? $"{note.Folder.Replace('\\', '/')} / " : "") + note.Name + (_vm.ProjectOf(note) is { Length: > 0 } project ? $"   🎯 {project}" : "")
            : "No note open";

    private void OnOpened() => Bind.OnMain(() =>
    {
        if (!IsInstanceValid(this))
            return;
        _filling = true;
        _editor.Text = _vm.Text;
        _editor.Editable = _vm.Current is not null;
        _filling = false;
        UpdateTitle();
        Ui.SetMarkdown(_preview, _vm.PreviewMarkdown());
        SelectInTree();
        FillSide();
    });

    /// <summary>Shows the editor, the preview or both (or the graph).</summary>
    private void Layout()
    {
        _graph.Visible = _showGraph;
        _editorPane.Visible = !_showGraph && _mode != "preview";
        _previewPane.Visible = !_showGraph && _mode != "edit";
        if (_previewPane.Visible)
            Ui.SetMarkdown(_preview, _vm.PreviewMarkdown());
        if (_showGraph)
            _graph.Reset();
    }

    private void OnLink(string url)
    {
        if (url.StartsWith("obs:", StringComparison.Ordinal))
            _vm.OpenLink(Uri.UnescapeDataString(url[4..]));
        else if (url.StartsWith("obstag:", StringComparison.Ordinal))
            _search.Text = "#" + Uri.UnescapeDataString(url[7..]);
        else
            Links.Open(url);
    }

    // ── Left ──

    private void FillLeft()
    {
        var searching = _search.Text.Trim().Length > 0;
        _tree.Visible = !searching;
        if (_results.GetParent() is ScrollContainer scroll)
            scroll.Visible = searching;
        if (searching)
            FillResults();
        else
            FillTree();
        FillTags();
    }

    private void FillTree()
    {
        _filling = true;
        _tree.Clear();
        var root = _tree.CreateItem();
        var folders = new Dictionary<string, TreeItem> { [""] = root };
        TreeItem FolderItem(string folder)
        {
            if (folders.TryGetValue(folder, out var item))
                return item;
            var parent = FolderItem(System.IO.Path.GetDirectoryName(folder) ?? "");
            item = _tree.CreateItem(parent);
            item.SetText(0, "📁 " + System.IO.Path.GetFileName(folder));
            item.SetMetadata(0, "folder:" + folder);
            item.SetCustomColor(0, E.Muted);
            folders[folder] = item;
            return item;
        }
        foreach (var folder in _vm.Folders())
            FolderItem(folder);
        foreach (var note in _vm.Notes)
        {
            var item = _tree.CreateItem(FolderItem(note.Folder));
            item.SetText(0, "🗒 " + note.Name);
            if (_vm.ProjectOf(note) is { Length: > 0 } project)
                item.SetTooltipText(0, "🎯 " + project);
            item.SetMetadata(0, "note:" + note.Path);
        }
        _filling = false;
        SelectInTree();
    }

    private void SelectInTree()
    {
        if (_vm.Current is not { } current || _tree.GetRoot() is not { } root)
            return;
        _filling = true;
        foreach (var item in Items(root))
            if (item.GetMetadata(0).AsString() == "note:" + current.Path)
            {
                for (var parent = item.GetParent(); parent is not null; parent = parent.GetParent())
                    parent.Collapsed = false;
                item.Select(0);
                _tree.ScrollToItem(item);
                break;
            }
        _filling = false;
    }

    private static IEnumerable<TreeItem> Items(TreeItem item)
    {
        for (var child = item.GetFirstChild(); child is not null; child = child.GetNext())
        {
            yield return child;
            foreach (var below in Items(child))
                yield return below;
        }
    }

    private void FillResults()
    {
        foreach (var child in _results.GetChildren())
            child.QueueFree();
        var found = _vm.Search(_search.Text);
        _results.AddChild(E.Caption($"{found.Count} note(s)", E.Muted, 11));
        foreach (var (note, snippet) in found.Take(200))
        {
            var button = E.Button("🗒 " + note.Name, tooltip: snippet);
            button.Alignment = HorizontalAlignment.Left;
            button.ClipText = true;
            button.Flat = true;
            var target = note;
            button.Pressed += () => _vm.Open(target);
            _results.AddChild(button);
            if (snippet.Length > 0 && !snippet.StartsWith('#'))
            {
                var line = E.Text_(snippet, E.Muted, 11);
                line.ClipText = true;
                _results.AddChild(line);
            }
        }
    }

    private void FillTags()
    {
        foreach (var child in _tags.GetChildren())
            child.QueueFree();
        foreach (var (tag, count) in _vm.AllTags().Take(80))
        {
            var button = E.Button($"#{tag} {count}", tooltip: $"The notes tagged #{tag}");
            button.AddThemeFontSizeOverride("font_size", 11);
            var name = tag;
            button.Pressed += () => _search.Text = "#" + name;
            _tags.AddChild(button);
        }
    }

    // ── Right ──

    private void FillSide()
    {
        foreach (var child in _side.GetChildren())
            child.QueueFree();
        if (_vm.Current is not { } note)
            return;
        var backlinks = _vm.Backlinks(note);
        _side.AddChild(E.Caption($"Backlinks ({backlinks.Count})", E.TextBright, 12));
        if (backlinks.Count == 0)
            _side.AddChild(E.Text_("No note links here yet.", E.Muted, 11));
        foreach (var (from, line) in backlinks)
        {
            var button = E.Button("🗒 " + from.Name);
            button.Alignment = HorizontalAlignment.Left;
            button.Flat = true;
            var target = from;
            button.Pressed += () => _vm.Open(target);
            _side.AddChild(button);
            var text = E.Text_(line, E.Muted, 11, wrap: true);
            text.CustomMinimumSize = new Vector2(260, 0);
            _side.AddChild(text);
        }

        var links = VaultWindowViewModel.LinksOf(_vm.Text);
        _side.AddChild(E.Separator());
        _side.AddChild(E.Caption($"Links ({links.Count})", E.TextBright, 12));
        foreach (var link in links)
        {
            var exists = _vm.Find(link) is not null;
            var button = E.Button((exists ? "→ " : "✚ ") + link, tooltip: exists ? "Open it" : "It does not exist yet: a click creates it");
            button.Alignment = HorizontalAlignment.Left;
            button.Flat = true;
            if (!exists)
                button.AddThemeColorOverride("font_color", E.Amber);
            var target = link;
            button.Pressed += () => _vm.OpenLink(target);
            _side.AddChild(button);
        }

        var outline = _vm.Outline();
        _side.AddChild(E.Separator());
        _side.AddChild(E.Caption("Outline", E.TextBright, 12));
        foreach (var (level, title, line) in outline)
        {
            var button = E.Button(new string(' ', (level - 1) * 3) + title);
            button.Alignment = HorizontalAlignment.Left;
            button.Flat = true;
            button.AddThemeFontSizeOverride("font_size", level <= 2 ? 13 : 12);
            var at = line;
            button.Pressed += () =>
            {
                if (_mode == "preview")
                    return;
                _editor.SetCaretLine(at);
                _editor.CenterViewportToCaret();
                _editor.GrabFocus();
            };
            _side.AddChild(button);
        }

        var tags = VaultWindowViewModel.TagsOf(_vm.Text);
        if (tags.Count > 0)
        {
            _side.AddChild(E.Separator());
            _side.AddChild(E.Caption("Tags", E.TextBright, 12));
            var flow = new HFlowContainer();
            foreach (var tag in tags)
            {
                var button = E.Button("#" + tag);
                button.AddThemeFontSizeOverride("font_size", 11);
                var name = tag;
                button.Pressed += () => _search.Text = "#" + name;
                flow.AddChild(button);
            }
            _side.AddChild(flow);
        }
    }

    // ── Actions ──

    private async Task OpenVaultAsync()
    {
        var folder = await Dialogs.OpenFolder("Open a vault (a folder of Markdown notes)", _vm.Vault);
        if (folder is not null)
            _vm.SetVault(folder);
    }

    private async Task NewNoteAsync()
    {
        var name = await Dialogs.Ask("New note", "Its name (\"Folder/Name\" puts it in a folder):");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var folder = _vm.Current?.Folder ?? "";
        _vm.Create(name, name.Contains('/') ? "" : folder);
        _showGraph = false;
        Layout();
        _editor.GrabFocus();
        _editor.SetCaretLine(_editor.GetLineCount() - 1);
    }

    private async Task NewFolderAsync()
    {
        var name = await Dialogs.Ask("New folder", "Its name (\"Parent/Child\" for a folder in another):");
        if (!string.IsNullOrWhiteSpace(name))
            _vm.CreateFolder(name);
    }

    private async Task RenameAsync()
    {
        if (_vm.Current is not { } note)
            return;
        var name = await Dialogs.Ask("Rename", "The new name of the note (the links to it in the other notes follow):", note.Name);
        if (!string.IsNullOrWhiteSpace(name))
            _vm.Rename(name);
    }

    private async Task DeleteAsync()
    {
        if (_vm.Current is not { } note)
            return;
        if (await Dialogs.Confirm("Delete", $"\"{note.Name}\" goes to the trash of the vault (.trash): nothing is erased, it can be taken back from there.", "Move to the trash"))
            _vm.Delete();
    }
}

/// <summary>The graph of the notes: each note a dot (larger with more links), its links as lines, the note open in the accent colour; a click opens a note.</summary>
public sealed partial class VaultGraph : Control
{
    private readonly VaultWindowViewModel _vm = null!;
    private readonly Dictionary<string, Vector2> _places = [];
    private List<(VaultWindowViewModel.VaultNote From, VaultWindowViewModel.VaultNote To)> _edges = [];
    private Dictionary<string, int> _degree = [];

    // Godot builds its scripts with this constructor.
    public VaultGraph()
    {
    }

    public VaultGraph(VaultWindowViewModel vm)
    {
        _vm = vm;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public event Action<VaultWindowViewModel.VaultNote>? NoteClicked;

    /// <summary>Lays the notes out again (a few hundred steps of springs and repulsion), in a square of side 1.</summary>
    public void Reset()
    {
        _edges = _vm.Edges();
        _degree = _vm.Notes.ToDictionary(n => n.Path, n => _edges.Count(e => e.From.Path == n.Path || e.To.Path == n.Path));
        _places.Clear();
        var notes = _vm.Notes;
        var random = new Random(7);
        for (var i = 0; i < notes.Count; i++)
        {
            var angle = Mathf.Tau * i / Math.Max(1, notes.Count);
            _places[notes[i].Path] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (0.3f + 0.1f * (float)random.NextDouble());
        }
        var k = 0.6f / MathF.Sqrt(Math.Max(1, notes.Count));
        for (var step = 0; step < 220; step++)
        {
            var force = notes.ToDictionary(n => n.Path, _ => Vector2.Zero);
            for (var i = 0; i < notes.Count; i++)
                for (var j = i + 1; j < notes.Count; j++)
                {
                    var d = _places[notes[i].Path] - _places[notes[j].Path];
                    var distance = Math.Max(0.01f, d.Length());
                    var push = d / distance * (k * k / distance);
                    force[notes[i].Path] += push;
                    force[notes[j].Path] -= push;
                }
            foreach (var (from, to) in _edges)
            {
                var d = _places[from.Path] - _places[to.Path];
                var distance = Math.Max(0.01f, d.Length());
                var pull = d / distance * (distance * distance / k);
                force[from.Path] -= pull;
                force[to.Path] += pull;
            }
            var heat = 0.05f * (1 - step / 220f) + 0.002f;
            foreach (var note in notes)
            {
                var f = force[note.Path] - _places[note.Path] * 0.3f;
                _places[note.Path] += f.Normalized() * Math.Min(f.Length(), heat);
            }
        }
        QueueRedraw();
    }

    private Vector2 Screen(Vector2 p)
    {
        if (_places.Count == 0)
            return Size / 2;
        var min = new Vector2(_places.Values.Min(v => v.X), _places.Values.Min(v => v.Y));
        var max = new Vector2(_places.Values.Max(v => v.X), _places.Values.Max(v => v.Y));
        var span = (max - min).Max(new Vector2(0.001f, 0.001f));
        var scale = Math.Min((Size.X - 160) / span.X, (Size.Y - 80) / span.Y);
        return Size / 2 + (p - (min + max) / 2) * scale;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), E.Sunken(0.2f));
        var font = E.Body;
        foreach (var (from, to) in _edges)
        {
            var current = from.Path == _vm.Current?.Path || to.Path == _vm.Current?.Path;
            DrawLine(Screen(_places[from.Path]), Screen(_places[to.Path]), (current ? E.Cyan : E.Edge) with { A = current ? 0.8f : 0.45f }, current ? 2 : 1, true);
        }
        foreach (var note in _vm.Notes)
        {
            var at = Screen(_places.GetValueOrDefault(note.Path));
            var degree = _degree.GetValueOrDefault(note.Path);
            var radius = 4 + MathF.Sqrt(degree) * 3;
            var current = note.Path == _vm.Current?.Path;
            DrawCircle(at, radius, current ? E.Cyan : degree == 0 ? E.Muted : E.Text);
            if (_vm.Notes.Count <= 80 || degree > 1 || current)
                DrawString(font, at + new Vector2(radius + 3, 4), note.Name, HorizontalAlignment.Left, 180, 12, current ? E.Cyan : E.Muted);
        }
        if (_vm.Notes.Count == 0)
            DrawString(font, new Vector2(20, 30), "No note yet.", HorizontalAlignment.Left, -1, 13, E.Muted);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return;
        var best = _vm.Notes.Select(n => (Note: n, Distance: Screen(_places.GetValueOrDefault(n.Path)).DistanceTo(click.Position)))
            .OrderBy(n => n.Distance).FirstOrDefault();
        if (best.Note is not null && best.Distance < 16)
        {
            AcceptEvent();
            NoteClicked?.Invoke(best.Note);
        }
    }
}
