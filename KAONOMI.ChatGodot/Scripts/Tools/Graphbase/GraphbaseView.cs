using System;
using System.Linq;
using System.Text.Json;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Dashboards;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Graphbase;

/// <summary>
/// <c>/graphbase</c>: the graph of KAONOMI.graphbase in 3D (<see cref="Graphbase3D"/>: the nodes as spheres), or in 2D
/// (▦ 2D: on a plane, seen from the front; kept from one opening to the next). Above
/// it, the search: a text (found in the ids, labels and properties) or texts joined by AND, OR and ( ), a label, a depth (the neighbours shown around the
/// nodes found), SEARCH, ALL (the whole graph again), FIT; ⌘ GRAPHQL opens the GraphQL extension under the graph: what
/// to see in your words, written as a query by the model of the chat (✨ TO GRAPHQL), an editor with the colours of
/// GraphQL (RUN or ⌘⏎), its variables, the answer; the nodes and edges of the answer are drawn. Beside the graph, the node selected: its labels, id, properties and edges (a click goes to the other end),
/// ✚ NEIGHBOURS, CENTER; with nothing selected, the kinds of the nodes and the types of the edges drawn, with their
/// colours (a click on a kind searches it). With two nodes or more chosen by Shift + click, the shortest paths between
/// them: how many, their length, and the first ones node by node with the edge taken (a click turns the camera to the node).
/// </summary>
[ToolView(typeof(GraphbaseWindowViewModel), "Graphbase", "🕸", 1560, 940)]
public sealed class GraphbaseView : IToolView
{
    public Control Build(object viewModel, Shell shell) => new GraphbasePage((GraphbaseWindowViewModel)viewModel);
}

public sealed partial class GraphbasePage : VBoxContainer
{
    private readonly GraphbaseWindowViewModel _vm = null!;
    private readonly Graphbase3D _graph = null!;
    private readonly OptionButton _labels = new() { TooltipText = "Only the nodes of this label", CustomMinimumSize = new Vector2(170, 0) };
    private readonly VBoxContainer _details = new();

    // The panel of the paths (on the left): a search among the nodes drawn, the nodes chosen, SEARCH.
    private readonly LineEdit _pathFind = new() { PlaceholderText = "Find a node drawn…", ClearButtonEnabled = true };
    private readonly VBoxContainer _pathChosen = new();
    private readonly VBoxContainer _pathNodes = new();
    private readonly Label _pathChosenCaption = E.Caption("CHOSEN", E.Muted, 11);
    private readonly Label _pathNodesCaption = E.Caption("NODES DRAWN", E.Muted, 11);
    private readonly Button _pathSearch = E.Button("🔍 SEARCH", primary: true, tooltip: "The shortest paths between the nodes chosen (2 or more): what is off the paths 90 % transparent");

    /// <summary>The nodes listed in the panel of the paths at most (type to narrow the list).</summary>
    private const int PathNodesListed = 80;
    private readonly Control _graphQl = null!;
    private CodeEdit _editor = null!;

    // Godot builds its scripts with this constructor.
    public GraphbasePage()
    {
    }

    public GraphbasePage(GraphbaseWindowViewModel vm)
    {
        _vm = vm;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);

        // The kinds unticked the last time stay so; each change is kept.
        vm.SetHiddenKinds(Preferences.GraphHiddenKinds);
        _graph = new Graphbase3D(vm, Preferences.GraphFlat);
        _graph.NodeDoubleClicked += id => _ = vm.ExpandAsync(id);
        _graphQl = BuildGraphQl();

        var search = Ui.Line(vm, nameof(GraphbaseWindowViewModel.Search), "Search: a name, a word, an id… AND, OR, ( )");
        search.CustomMinimumSize = new Vector2(340, 0);
        search.TooltipText = """
            A text found in the ids, labels and properties (case ignored), or texts joined by AND and OR, grouped by ( ):
              kaonomi AND (agent OR mcp)
            AND binds first. Words side by side are one text, as written: Parti démocrate.
            A text in quotes may hold AND, OR or parentheses: "rock and roll".
            """;
        search.TextSubmitted += text => _ = vm.SearchAsync();
        FillLabels();
        Action labelsChanged = () => Bind.OnMain(FillLabels);
        vm.LabelsChanged += labelsChanged;
        _labels.ItemSelected += index => vm.Label = index <= 0 ? "" : _labels.GetItemText((int)index);
        var depth = new SpinBox { MinValue = 0, MaxValue = 4, Value = vm.Depth, CustomMinimumSize = new Vector2(84, 0), TooltipText = "The neighbours shown around the nodes found: 0 none, 1 the nodes linked to them, 2 theirs too…" };
        depth.ValueChanged += value => vm.Depth = (int)value;
        var graphQlButton = E.Button("⌘ GRAPHQL", tooltip: "The GraphQL extension: any query or mutation of graphbase, its answer drawn");
        graphQlButton.ToggleMode = true;
        graphQlButton.Toggled += on =>
        {
            _graphQl.Visible = on;
            if (on)
                _editor.GrabFocus();
        };

        var pathsButton = E.Button("⛓ PATHS", tooltip: "The panel of the paths, on the left: choose 2 nodes or more, SEARCH shows the shortest paths between them. Again: the panel hidden");
        pathsButton.ToggleMode = true;

        var flatButton = E.Button("▦ 2D", tooltip: "The graph in 2D: on a plane, seen from the front, without perspective; a drag moves it. Again: back to 3D");
        flatButton.ToggleMode = true;
        flatButton.SetPressedNoSignal(_graph.Flat);
        flatButton.Toggled += on =>
        {
            _graph.Flat = on;
            Preferences.GraphFlat = on;
            RefreshDetails();
        };

        // The bar wraps on a second line when the window is narrow (else it would push the panels out of the window).
        AddChild(Ui.Toolbar(Flow(
            E.Caption("Graphbase", E.TextBright, 14),
            search,
            _labels,
            Ui.Row(4, E.Caption("Depth"), depth),
            Ui.Action("🔍 SEARCH", () => _ = vm.SearchAsync(), primary: true, tooltip: "The nodes holding the text, or the texts joined by AND, OR and ( ) (and of the label), with their neighbours to the depth"),
            Ui.Action("⟳ ALL", ShowAll, tooltip: "The whole graph again (its most linked nodes when it is large)"),
            Ui.Action("◎ FIT", _graph.FitAll, tooltip: "See the whole graph drawn again (turning, in 3D)"),
            flatButton,
            pathsButton,
            Ui.Action("⇄ SYNC", () => _ = vm.SyncAsync(), tooltip: "Write to graphbase now the repository of /archi, the ontology and the relations of the server (done by themselves as they change)"),
            graphQlButton)));

        var side = new PanelContainer { CustomMinimumSize = new Vector2(340, 0) };
        side.AddThemeStyleboxOverride("panel", E.Box(E.Field, E.Edge, 8));
        side.AddChild(Ui.Scroll(Ui.Margin(_details, 10)));
        var left = Ui.SplitV(_graph, _graphQl, 470);
        var paths = BuildPathsPanel();
        pathsButton.SetPressedNoSignal(true);
        pathsButton.Toggled += on => paths.Visible = on;
        AddChild(Ui.SplitH(paths, Ui.SplitH(left, side, 860), 290));
        AddChild(Ui.StatusBar(vm, nameof(GraphbaseWindowViewModel.Status), nameof(GraphbaseWindowViewModel.IsError), nameof(GraphbaseWindowViewModel.IsBusy)));

        Action<bool> graphChanged = _ => Bind.OnMain(RefreshDetails);
        vm.GraphChanged += graphChanged;
        Action pathChanged = () => Bind.OnMain(RefreshDetails);
        vm.PathChanged += pathChanged;
        Action kindsChanged = () =>
        {
            Preferences.GraphHiddenKinds = vm.HiddenKinds.ToList();
            Bind.OnMain(RefreshPathNodes);
        };
        vm.KindsChanged += kindsChanged;
        Action graphDrawn = () => Bind.OnMain(RefreshPathsPanel);
        Action<bool> graphRedrawn = _ => graphDrawn();
        vm.GraphChanged += graphRedrawn;
        vm.PathChoiceChanged += graphDrawn;
        Bind.OnGone(this, () =>
        {
            vm.GraphChanged -= graphChanged;
            vm.LabelsChanged -= labelsChanged;
            vm.PathChanged -= pathChanged;
            vm.KindsChanged -= kindsChanged;
            vm.GraphChanged -= graphRedrawn;
            vm.PathChoiceChanged -= graphDrawn;
        });
        Bind.When(this, vm, RefreshDetails, nameof(GraphbaseWindowViewModel.SelectedId));
        _ = vm.LoadAsync();
    }

    private void ShowAll()
    {
        _vm.Search = "";
        _vm.Label = "";
        _labels.Select(0);
        _ = _vm.SearchAsync();
    }

    private void FillLabels()
    {
        if (!IsInstanceValid(_labels))
            return;
        _labels.Clear();
        _labels.AddItem("Any label");
        foreach (var label in _vm.AllLabels)
            _labels.AddItem(label);
        var index = _vm.AllLabels.IndexOf(_vm.Label);
        _labels.Select(index < 0 ? 0 : index + 1);
    }

    // ── The GraphQL extension ──

    private Control BuildGraphQl()
    {
        _editor = CodeEditors.Editor();
        _editor.Text = _vm.Query;
        _editor.SyntaxHighlighter = GraphQlColours();
        _editor.TextChanged += () => _vm.Query = _editor.Text;
        // The query written by the model (✨ TO GRAPHQL) shown in the editor.
        Bind.When(this, _vm, () =>
        {
            if (_editor.Text != _vm.Query)
                _editor.Text = _vm.Query;
        }, nameof(GraphbaseWindowViewModel.Query));
        _editor.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter } key && (key.MetaPressed || key.CtrlPressed))
            {
                _editor.AcceptEvent();
                _ = _vm.RunQueryAsync();
            }
        };
        var variables = CodeEditors.Editor(lineNumbers: false);
        variables.CustomMinimumSize = new Vector2(0, 64);
        variables.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        variables.PlaceholderText = "{ \"label\": \"Agent\" }  (for query($label: String) { … })";
        variables.SyntaxHighlighter = CodeEditors.Json();
        variables.Text = _vm.Variables;
        variables.TextChanged += () => _vm.Variables = variables.Text;
        Bind.When(this, _vm, () =>
        {
            if (variables.Text != _vm.Variables)
                variables.Text = _vm.Variables;
        }, nameof(GraphbaseWindowViewModel.Variables));
        var answer = CodeEditors.Editor(editable: false);
        answer.SyntaxHighlighter = CodeEditors.Json();
        answer.PlaceholderText = "The answer of graphbase (data, errors)";
        Bind.When(this, _vm, () => answer.Text = _vm.Result, nameof(GraphbaseWindowViewModel.Result));

        var header = Ui.Row(8,
            E.Caption("GRAPHQL", E.TextBright, 12),
            E.Text_("Any query or mutation of graphbase: the nodes and edges of its answer are drawn · ⌘⏎ runs", E.Muted, 12),
            Ui.Spring(),
            Ui.Action("↺ EXAMPLE", () =>
            {
                _editor.Text = GraphbaseWindowViewModel.DefaultQuery;
                _vm.Query = _editor.Text;
            }, tooltip: "The example query again"),
            Ui.Action("▶ RUN", () => _ = _vm.RunQueryAsync(), primary: true, tooltip: "Run the query (⌘⏎); a viewer runs no mutation"));
        // ✨ TO GRAPHQL: what the user wants, in their words, written as a query by the model selected in the chat.
        var ask = Ui.Line(_vm, nameof(GraphbaseWindowViewModel.Ask), "In your words: \"the members of the Parti démocrate\", \"the agents and their models\"…");
        ask.TextSubmitted += text => _ = _vm.AskAsync();
        var askButton = Ui.Action("✨ TO GRAPHQL", () => _ = _vm.AskAsync(), primary: true,
            tooltip: "The model selected in the chat writes the GraphQL query of what you ask (from the schema of graphbase, its labels and types of edges), then it runs; a mutation waits for RUN. The prompt: graphbase.query in /prompts");
        askButton.Disabled = !_vm.CanAsk;
        // The model that writes the queries: the one chosen last here, else the one of the chat (a small model of
        // conversation writes them badly: a larger one, gpt-oss for instance, is better at it).
        var models = new OptionButton { TooltipText = "The model that writes the query (kept from one opening to the next)", CustomMinimumSize = new Vector2(220, 0) };
        models.ItemSelected += index =>
        {
            _vm.QueryModel = models.GetItemText((int)index);
            Preferences.GraphQlModel = _vm.QueryModel;
        };
        _ = FillModelsAsync(models);
        var asking = Ui.Row(8, Ui.Fill(ask), models, askButton);
        var query = Ui.Column(6, asking, Ui.Fill(_editor, vertical: true), Ui.Field("Variables (a JSON object)", variables));
        query.SizeFlagsVertical = SizeFlags.ExpandFill;
        var panel = Ui.Column(6, header, Ui.Fill(Ui.SplitH(query, answer, 600), vertical: true));
        panel.CustomMinimumSize = new Vector2(0, 260);
        panel.Visible = false;
        return panel;
    }

    private async System.Threading.Tasks.Task FillModelsAsync(OptionButton models)
    {
        var names = (await _vm.ModelsAsync()).ToList();
        if (!IsInstanceValid(models))
            return;
        var chat = _vm.ChatModel;
        if (chat is not null && !names.Contains(chat))
            names.Insert(0, chat);
        if (names.Count == 0)
        {
            models.AddItem("no model");
            models.Disabled = true;
            return;
        }
        foreach (var name in names)
            models.AddItem(name);
        var chosen = Preferences.GraphQlModel is { } kept && names.Contains(kept) ? kept : chat ?? names[0];
        models.Select(names.IndexOf(chosen));
        _vm.QueryModel = chosen;
    }

    /// <summary>The colours of GraphQL: its keywords, strings, comments, numbers and the fields with arguments.</summary>
    private static Godot.CodeHighlighter GraphQlColours()
    {
        var dark = !E.IsLight;
        var highlighter = new Godot.CodeHighlighter
        {
            NumberColor = Color.FromHtml(dark ? "#B5CEA8" : "#098658"),
            SymbolColor = E.Muted,
            FunctionColor = Color.FromHtml(dark ? "#DCDCAA" : "#795E26"),
            MemberVariableColor = E.Text
        };
        highlighter.AddColorRegion("\"", "\"", Color.FromHtml(dark ? "#CE9178" : "#A31515"), false);
        highlighter.AddColorRegion("#", "", Color.FromHtml(dark ? "#6A9955" : "#008000"), true);
        foreach (var keyword in new[] { "query", "mutation", "subscription", "fragment", "on", "true", "false", "null" })
            highlighter.AddKeywordColor(keyword, Color.FromHtml(dark ? "#569CD6" : "#0000FF"));
        return highlighter;
    }

    // ── Beside the graph ──

    private void RefreshDetails()
    {
        if (!IsInstanceValid(_details))
            return;
        foreach (var child in _details.GetChildren())
        {
            _details.RemoveChild(child);
            child.QueueFree();
        }
        _details.AddThemeConstantOverride("separation", 8);
        if (_vm.PathEnds.Count >= 2)
            ShowPaths();
        else if (_vm.Selected is { } node)
            ShowNode(node);
        else
            ShowLegend();
    }

    /// <summary>
    /// The paths between the nodes chosen with Shift + click: the nodes chosen, then for each pair of them how many
    /// shortest paths and their length, and the first paths node by node, each with the edge that leads to it.
    /// </summary>
    private void ShowPaths()
    {
        _details.AddChild(E.Caption("THE PATHS", E.TextBright, 12));
        _details.AddChild(E.Text_("The shortest paths between the nodes chosen (the fewest edges, in both directions), in the graph drawn. "
                                  + "Shift + click: another node, or one of them to take it out. A click without Shift leaves the paths.", E.Muted, 12, wrap: true));
        _details.AddChild(E.Caption($"NODES CHOSEN ({_vm.PathEnds.Count})", E.Muted, 11));
        foreach (var id in _vm.PathEnds)
            _details.AddChild(NodeRow(id, _vm.NameOf(id)));
        _details.AddChild(Ui.Row(6, Ui.Action("✕ LEAVE THE PATHS", _vm.ClearPath, tooltip: "Choose no node any more: the graph as before")));
        foreach (var paths in _vm.Paths)
        {
            _details.AddChild(new HSeparator());
            _details.AddChild(E.Text_($"{_vm.NameOf(paths.From)} ↔ {_vm.NameOf(paths.To)}", E.TextBright, 14, wrap: true));
            if (paths.Length < 0)
            {
                _details.AddChild(E.Text_("No path between them", E.Amber, 12, wrap: true));
                continue;
            }
            _details.AddChild(E.Text_($"{(paths.Count == 1 ? "1 shortest path" : $"{paths.Count} shortest paths")} of {paths.Length} edge(s)", E.Text, 13, wrap: true));
            for (var i = 0; i < paths.Listed.Count; i++)
            {
                var path = paths.Listed[i];
                if (paths.Count > 1)
                    _details.AddChild(E.Caption($"PATH {i + 1}", E.Muted, 11));
                for (var step = 0; step < path.Count; step++)
                {
                    if (step == 0)
                    {
                        _details.AddChild(NodeRow(path[0], _vm.NameOf(path[0])));
                        continue;
                    }
                    // The edge taken, as it is drawn: → from the node before, ← to it (↔ with edges both ways).
                    var edges = _vm.EdgesBetween(path[step - 1], path[step]).ToList();
                    var forth = edges.Any(e => e.To == path[step]);
                    var back = edges.Any(e => e.From == path[step]);
                    var types = string.Join(", ", edges.Select(e => e.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal).Take(3));
                    _details.AddChild(NodeRow(path[step], $"{(forth && back ? "↔" : forth ? "→" : "←")} {types}  {_vm.NameOf(path[step])}"));
                }
            }
            if (paths.Count > paths.Listed.Count)
                _details.AddChild(E.Text_($"… and {paths.Count - paths.Listed.Count} more (all of them are lit in the graph)", E.Muted, 12, wrap: true));
        }
    }

    /// <summary>A node of the paths, in the colour of its kind: a click turns the camera to it.</summary>
    private Button NodeRow(string id, string text)
    {
        var row = Row(Graphbase3D.ColorOf(_vm.Nodes.TryGetValue(id, out var node) ? node.Kind : ""), text, "Turn the camera to this node");
        row.Pressed += () => _graph.Focus(id);
        return row;
    }

    // ── The panel of the paths ──

    /// <summary>
    /// The panel of the paths, on the left of the graph: a search among the nodes drawn (those of a kind unticked in
    /// the legend left out), a box before each to choose it, the nodes chosen (✕ takes one out, a click turns the
    /// camera to it), SEARCH (2 nodes or more: the shortest paths between them, the rest 90 % transparent) and CLEAR.
    /// </summary>
    private Control BuildPathsPanel()
    {
        _pathFind.TooltipText = "A part of the name, of the kind or of the id of a node drawn (case and accents ignored)";
        _pathFind.TextChanged += _ => RefreshPathNodes();
        _pathFind.TextSubmitted += _ =>
        {
            // Enter chooses the first node found that is not chosen yet.
            var first = PathCandidates().FirstOrDefault(n => !_vm.PathChoice.Contains(n.Id));
            if (first is not null)
                _vm.ChoosePathNode(first.Id, true);
        };
        _pathSearch.Pressed += _vm.SearchPaths;
        _pathChosen.AddThemeConstantOverride("separation", 2);
        _pathNodes.AddThemeConstantOverride("separation", 0);
        var column = Ui.Column(8,
            E.Caption("SHORTEST PATHS", E.TextBright, 12),
            E.Text_("Choose 2 nodes or more among those drawn, then SEARCH: the shortest paths between them are lit, the rest is 90 % transparent.", E.Muted, 12, wrap: true),
            _pathFind,
            _pathChosenCaption,
            _pathChosen,
            Ui.Row(6, _pathSearch, Ui.Action("✕ CLEAR", _vm.ClearPathChoice, tooltip: "No node chosen, the paths left")),
            new HSeparator(),
            _pathNodesCaption,
            _pathNodes);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) };
        panel.AddThemeStyleboxOverride("panel", E.Box(E.Field, E.Edge, 8));
        panel.AddChild(Ui.Scroll(Ui.Margin(column, 10)));
        RefreshPathsPanel();
        return panel;
    }

    /// <summary>The nodes drawn that the text of the search finds, of the kinds shown, the names that start with it first.</summary>
    private System.Collections.Generic.List<GraphbaseNode> PathCandidates()
    {
        var text = DisseminationQuery.Fold(_pathFind.Text.Trim());
        return _vm.Nodes.Values
            .Where(n => !_vm.HiddenKinds.Contains(n.Kind))
            .Select(n => (Node: n, Name: DisseminationQuery.Fold(n.Name)))
            .Where(n => text.Length == 0 || n.Name.Contains(text, StringComparison.Ordinal) || DisseminationQuery.Fold(n.Node.Kind).Contains(text, StringComparison.Ordinal)
                        || DisseminationQuery.Fold(n.Node.Id).Contains(text, StringComparison.Ordinal))
            .OrderByDescending(n => text.Length > 0 && n.Name.StartsWith(text, StringComparison.Ordinal))
            .ThenBy(n => n.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(n => n.Node)
            .ToList();
    }

    private void RefreshPathsPanel()
    {
        if (!IsInstanceValid(_pathChosen))
            return;
        foreach (var child in _pathChosen.GetChildren())
        {
            _pathChosen.RemoveChild(child);
            child.QueueFree();
        }
        var chosen = _vm.PathChoice.Where(_vm.Nodes.ContainsKey).ToList();
        _pathChosenCaption.Text = $"CHOSEN ({chosen.Count})";
        if (chosen.Count == 0)
            _pathChosen.AddChild(E.Text_("None yet: tick nodes below (or Shift + click spheres)", E.Muted, 12, wrap: true));
        foreach (var id in chosen)
        {
            var node = _vm.Nodes[id];
            var row = Row(Graphbase3D.ColorOf(node.Kind), node.Name, $"{node.Name} · {node.Kind}\nClick: turn the camera to it");
            row.Pressed += () => _graph.Focus(id);
            var remove = new Button { Text = "✕", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Take it out of the nodes chosen" };
            remove.Pressed += () => _vm.ChoosePathNode(id, false);
            _pathChosen.AddChild(Ui.Row(0, row, remove));
        }
        _pathSearch.Disabled = chosen.Count < 2;
        RefreshPathNodes();
    }

    private void RefreshPathNodes()
    {
        if (!IsInstanceValid(_pathNodes))
            return;
        foreach (var child in _pathNodes.GetChildren())
        {
            _pathNodes.RemoveChild(child);
            child.QueueFree();
        }
        var found = PathCandidates();
        var shown = _vm.Nodes.Values.Count(n => !_vm.HiddenKinds.Contains(n.Kind));
        _pathNodesCaption.Text = _pathFind.Text.Trim().Length > 0 ? $"FOUND ({found.Count} of {shown})" : $"NODES DRAWN ({shown})";
        foreach (var node in found.Take(PathNodesListed))
        {
            var id = node.Id;
            var box = new CheckBox { ButtonPressed = _vm.PathChoice.Contains(id), FocusMode = FocusModeEnum.None, TooltipText = "Choose this node for the paths" };
            box.Toggled += on => _vm.ChoosePathNode(id, on);
            var row = Row(Graphbase3D.ColorOf(node.Kind), node.Kind.Length > 0 ? $"{node.Name}  · {node.Kind}" : node.Name, $"{node.Name} · {node.Kind}\nClick: turn the camera to it");
            row.Pressed += () => _graph.Focus(id);
            _pathNodes.AddChild(Ui.Row(0, box, row));
        }
        if (found.Count > PathNodesListed)
            _pathNodes.AddChild(E.Text_($"… and {found.Count - PathNodesListed} more: type to narrow the list", E.Muted, 12, wrap: true));
        if (found.Count == 0)
            _pathNodes.AddChild(E.Text_(_vm.Nodes.Count == 0 ? "No node drawn" : "No node drawn holds this text (or its kind is unticked in the legend)", E.Muted, 12, wrap: true));
    }

    private void ShowLegend()
    {
        _details.AddChild(E.Caption("THE GRAPH DRAWN", E.TextBright, 12));
        _details.AddChild(E.Text_($"{_vm.Nodes.Count} nodes, {_vm.Edges.Count} edges"
                                  + (_vm.TotalNodes > 0 ? $" · graphbase holds {_vm.TotalNodes} nodes, {_vm.TotalEdges} edges" : ""), E.Text, 13, wrap: true));
        _details.AddChild(E.Text_("A click on a sphere: its details. A double-click: its neighbours. Shift + click on two spheres: the paths between them. "
                                  + (_graph.Flat ? "Drag: move · wheel or pinch: zoom · ▦ 2D: back to 3D." : "Drag: turn · right drag or Shift: move · wheel or pinch: zoom · ▦ 2D: on a plane."), E.Muted, 12, wrap: true));
        if (_vm.Nodes.Count == 0)
            return;
        // The kinds: a box shows them or makes them 90 % transparent; the name searches the nodes of the label.
        var kinds = _vm.Nodes.Values.GroupBy(n => n.Kind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        var caption = E.Caption("KINDS", E.Muted, 11);
        void Told()
        {
            var hidden = kinds.Count(k => _vm.HiddenKinds.Contains(k.Key));
            caption.Text = hidden == 0 ? "KINDS" : $"KINDS · {kinds.Count - hidden} of {kinds.Count} shown";
        }
        Told();
        var all = Ui.Action("ALL", () =>
        {
            _vm.ShowAllKinds(true);
            RefreshDetails();
        }, tooltip: "Show every kind");
        var none = Ui.Action("NONE", () =>
        {
            _vm.ShowAllKinds(false);
            RefreshDetails();
        }, tooltip: "Make every kind 90 % transparent (then tick the ones to see)");
        foreach (var button in new[] { all, none })
            button.AddThemeFontSizeOverride("font_size", 11);
        // The buttons on their own line: the panel may be narrow.
        _details.AddChild(caption);
        _details.AddChild(Ui.Row(6, all, none));
        foreach (var kind in kinds)
        {
            var key = kind.Key;
            var name = key.Length > 0 ? key : "(no label)";
            var box = new CheckBox
            {
                ButtonPressed = !_vm.HiddenKinds.Contains(key),
                FocusMode = FocusModeEnum.None,
                TooltipText = $"Ticked: the nodes {name} are shown · unticked: 90 % transparent, their names and edges too (a click goes through them)\n"
                              + "Right click: this kind alone"
            };
            var button = Row(Graphbase3D.ColorOf(key), $"{name}  {kind.Count()}", key.Length > 0 ? $"Search the nodes of the label {name}" : "The nodes without label");
            button.Modulate = box.ButtonPressed ? Colors.White : new Color(1, 1, 1, 0.45f);
            box.Toggled += on =>
            {
                _vm.ShowKind(key, on);
                button.Modulate = on ? Colors.White : new Color(1, 1, 1, 0.45f);
                Told();
            };
            box.GuiInput += e =>
            {
                // Right click: this kind alone, the others transparent.
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                {
                    _vm.ShowAllKinds(false, except: key);
                    box.AcceptEvent();
                    RefreshDetails();
                }
            };
            if (key.Length > 0)
                button.Pressed += () =>
                {
                    _vm.Search = "";
                    _vm.Label = key;
                    FillLabels();
                    _ = _vm.SearchAsync();
                };
            _details.AddChild(Ui.Row(0, box, button));
        }
        if (_vm.Edges.Count == 0)
            return;
        _details.AddChild(E.Caption("TYPES OF EDGES", E.Muted, 11));
        foreach (var type in _vm.Edges.Values.GroupBy(e => e.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(40))
            _details.AddChild(E.Text_($"{type.Key}  {type.Count()}", E.Text, 12));
    }

    private void ShowNode(GraphbaseNode node)
    {
        _details.AddChild(E.Text_(node.Name, E.TextBright, 17, wrap: true));
        var labels = new HFlowContainer();
        labels.AddThemeConstantOverride("h_separation", 6);
        labels.AddThemeConstantOverride("v_separation", 4);
        foreach (var label in node.Labels)
            labels.AddChild(Ui.Badge(label, Graphbase3D.ColorOf(label)));
        _details.AddChild(labels);
        var id = new LineEdit { Text = node.Id, Editable = false, TooltipText = "Its id (select it to copy it)" };
        id.AddThemeFontOverride("font", E.Mono);
        id.AddThemeFontSizeOverride("font_size", 12);
        id.AddThemeColorOverride("font_uneditable_color", E.Text);
        _details.AddChild(id);
        _details.AddChild(Ui.Row(6,
            Ui.Action("✚ NEIGHBOURS", () => _ = _vm.ExpandAsync(node.Id), tooltip: "Add its neighbours to the graph drawn (a double-click on its sphere)"),
            Ui.Action("◎ CENTER", () => _graph.Focus(node.Id), tooltip: "Turn the camera to it"),
            Ui.Action("✕", () => _vm.SelectedId = null, tooltip: "Select nothing")));

        _details.AddChild(E.Caption("PROPERTIES", E.Muted, 11));
        var count = 0;
        foreach (var property in node.Properties.EnumerateObject())
        {
            if (++count > 80)
            {
                _details.AddChild(E.Text_("…", E.Muted, 12));
                break;
            }
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : JsonSerializer.Serialize(property.Value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            _details.AddChild(Ui.Field(property.Name, E.Text_(value.Length > 600 ? value[..600] + "…" : value, E.Text, 13, wrap: true)));
        }
        if (count == 0)
            _details.AddChild(E.Text_("None", E.Muted, 12));

        var edges = _vm.EdgesOf(node.Id).OrderBy(e => e.Type).ThenBy(e => _vm.NameOf(e.Other(node.Id))).ToList();
        _details.AddChild(E.Caption($"EDGES DRAWN ({edges.Count})", E.Muted, 11));
        foreach (var edge in edges.Take(200))
        {
            var other = edge.Other(node.Id);
            var arrow = edge.From == edge.To ? "↺" : edge.From == node.Id ? "→" : "←";
            var row = Row(Graphbase3D.ColorOf(_vm.Nodes.TryGetValue(other, out var o) ? o.Kind : ""), $"{arrow} {edge.Type}  {_vm.NameOf(other)}", "Go to this node");
            row.Pressed += () =>
            {
                _vm.SelectedId = other;
                _graph.Focus(other);
            };
            _details.AddChild(row);
        }
        if (edges.Count == 0)
            _details.AddChild(E.Text_("None drawn: ✚ NEIGHBOURS shows them", E.Muted, 12));
    }

    /// <summary>A flat button of the list beside the graph, a dot of <paramref name="color"/> before its text.</summary>
    private static HFlowContainer Flow(params Control[] children)
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var child in children)
            flow.AddChild(child);
        return flow;
    }

    private static Button Row(Color color, string text, string tooltip)
    {
        var button = new Button { Text = "●  " + text, Flat = true, Alignment = HorizontalAlignment.Left, TooltipText = tooltip, ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.AddThemeColorOverride("font_color", E.Text);
        button.AddThemeColorOverride("font_hover_color", E.TextBright);
        button.AddThemeFontSizeOverride("font_size", 13);
        // The disc keeps its colour (the theme tints the icons of the buttons).
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_hover_pressed_color" })
            button.AddThemeColorOverride(state, Colors.White);
        // The dot in the colour of the kind: a text of two colours is not possible, the icon is a disc.
        button.Icon = Disc(color);
        button.Text = text;
        return button;
    }

    private static Texture2D Disc(Color color)
    {
        const int size = 14;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var centre = (size - 1) / 2f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = new Vector2(x - centre, y - centre).Length();
            image.SetPixel(x, y, color with { A = Math.Clamp(centre + 0.5f - d, 0, 1) });
        }
        return ImageTexture.CreateFromImage(image);
    }
}
