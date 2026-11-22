using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A node of KAONOMI.graphbase as <c>/graphbase</c> shows it.</summary>
public sealed class GraphbaseNode
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Labels { get; init; }
    /// <summary>Its properties (a JSON object).</summary>
    public required JsonElement Properties { get; init; }
    /// <summary>What it is called: its property name (or title, label, fullName), else its id.</summary>
    public required string Name { get; init; }

    private static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new Dictionary<string, string>());

    /// <summary>
    /// What it is: its first label but the ones of where it comes from (a fact sheet of /archi is <c>Archi</c> and its
    /// kind, an entity of the ontology <c>Ontology</c> and its entity type).
    /// </summary>
    public string Kind => Labels.FirstOrDefault(l => l is not (ArchiGraph.Label or KnowledgeGraph.OntologyLabel or KnowledgeGraph.ClassLabel or KnowledgeGraph.EndLabel))
                          ?? Labels.FirstOrDefault() ?? "";

    internal static GraphbaseNode Read(JsonElement node)
    {
        var id = node.GetProperty("id").GetString() ?? "";
        var properties = node.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p.Clone() : EmptyObject;
        var labels = node.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Array
            ? l.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];
        var name = new[] { "name", "title", "label", "fullName" }
            .Select(key => properties.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? id;
        return new GraphbaseNode { Id = id, Labels = labels, Properties = properties, Name = name };
    }
}

/// <summary>An edge of KAONOMI.graphbase as <c>/graphbase</c> shows it.</summary>
public sealed class GraphbaseEdge
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required JsonElement Properties { get; init; }

    public string Other(string nodeId) => From == nodeId ? To : From;

    internal static GraphbaseEdge Read(JsonElement edge) => new()
    {
        Id = edge.GetProperty("id").GetString() ?? "",
        Type = edge.GetProperty("type").GetString() ?? "",
        From = edge.GetProperty("fromId").GetString() ?? "",
        To = edge.GetProperty("toId").GetString() ?? "",
        Properties = edge.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p.Clone() : JsonSerializer.SerializeToElement(new Dictionary<string, string>())
    };
}

/// <summary>
/// The shortest paths between two nodes chosen with Shift + click in <c>/graphbase</c>: <see cref="Length"/> edges each
/// (-1: no path), <see cref="Count"/> of them, the first ones as their nodes from <see cref="From"/> to <see cref="To"/>.
/// </summary>
public sealed record GraphbasePaths(string From, string To, int Length, long Count, IReadOnlyList<IReadOnlyList<string>> Listed);

/// <summary>
/// <c>/graphbase</c>: the graph of KAONOMI.graphbase (<c>graphbaseUrl</c> in config.yml) in 3D. At the opening, the whole
/// graph when it holds <see cref="MaxNodes"/> nodes at most, else its 150 most linked nodes (its hubs). The search (a text found in the
/// ids, labels and properties, a label, a depth) shows the nodes found and their neighbours to that depth; a
/// double-click on a node adds its neighbours (<see cref="ExpandAsync"/>). The GraphQL extension runs any query or
/// mutation (not for a viewer) and draws the nodes and edges of its answer, with the edges between them.
/// Shift + click on two nodes (or more) shows the shortest paths between them (<see cref="TogglePathEnd"/>).
/// </summary>
public sealed partial class GraphbaseWindowViewModel : ViewModelBase, ISingleWindow
{
    /// <summary>The most nodes drawn at once.</summary>
    public const int MaxNodes = 600;

    private const string NodeFields = "id labels properties";
    private const string EdgeFields = "id type fromId toId properties";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public const string DefaultQuery = """
        # Any query or mutation of KAONOMI.graphbase (RUN, or ⌘⏎): the nodes and edges
        # of its answer are drawn, with the edges between them. The schema: the IDE of
        # graphbase in a browser, or { __schema { types { name } } }.
        {
          nodes(label: "Agent") {
            id
            name: property(name: "name")
            neighbors(direction: OUT) { id }
          }
        }
        """;

    private readonly GraphbaseClient _client;
    private readonly OllamaModelService? _models;
    private readonly Func<string?> _model;
    private CancellationTokenSource? _running;

    /// <param name="models">The models that write a query from a text (✨ TO GRAPHQL); null: none.</param>
    /// <param name="model">The model to use (the one selected in the chat).</param>
    public GraphbaseWindowViewModel(string url, OllamaModelService? models = null, Func<string?>? model = null)
    {
        _client = new GraphbaseClient(url);
        _models = models;
        _model = model ?? (() => null);
    }

    /// <summary>True when a model can write a query from a text.</summary>
    public bool CanAsk => _models is not null;

    /// <summary>The model selected in the chat.</summary>
    public string? ChatModel => _model();

    /// <summary>The model that writes the queries (chosen in the GraphQL extension); null: the one of the chat.</summary>
    [ObservableProperty]
    private string? _queryModel;

    /// <summary>The models the provider of the chat offers (empty when it does not answer).</summary>
    public async Task<IReadOnlyList<string>> ModelsAsync()
    {
        if (_models is null)
            return [];
        try
        {
            return await _models.ListModelsAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
        {
            return [];
        }
    }

    /// <summary>What the user asks in their words, written as a GraphQL query by the model (✨ TO GRAPHQL).</summary>
    [ObservableProperty]
    private string _ask = "";

    public string Url => _client.Url;

    /// <summary>The nodes drawn, by id.</summary>
    public Dictionary<string, GraphbaseNode> Nodes { get; } = new(StringComparer.Ordinal);

    /// <summary>The edges drawn (between nodes drawn), by id.</summary>
    public Dictionary<string, GraphbaseEdge> Edges { get; } = new(StringComparer.Ordinal);

    /// <summary>The nodes found by the search or in the answer of a query (lit up).</summary>
    public HashSet<string> Found { get; } = new(StringComparer.Ordinal);

    /// <summary>The labels of the graph, the most used first (the list of the search).</summary>
    public List<string> AllLabels { get; } = [];

    public int TotalNodes { get; private set; }
    public int TotalEdges { get; private set; }

    /// <summary>The graph drawn changed: true when replaced (laid out again), false when nodes or edges were added.</summary>
    public event Action<bool>? GraphChanged;

    public event Action? LabelsChanged;

    [ObservableProperty]
    private string _search = "";

    /// <summary>The label searched ("": any).</summary>
    [ObservableProperty]
    private string _label = "";

    /// <summary>How far from the nodes found their neighbours are shown (0: the nodes found only).</summary>
    [ObservableProperty]
    private int _depth = 1;

    [ObservableProperty]
    private string? _selectedId;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The text of the GraphQL extension.</summary>
    [ObservableProperty]
    private string _query = DefaultQuery;

    /// <summary>The variables of the query (a JSON object), or nothing.</summary>
    [ObservableProperty]
    private string _variables = "";

    /// <summary>The answer of the last query, indented.</summary>
    [ObservableProperty]
    private string _result = "";

    public GraphbaseNode? Selected => SelectedId is { } id && Nodes.TryGetValue(id, out var node) ? node : null;

    partial void OnSelectedIdChanged(string? value)
    {
        OnPropertyChanged(nameof(Selected));
        // A node selected another way (an edge of its details, the search, its neighbours added) leaves the paths.
        if (!_choosingPath && value is not null && PathEnds.Count > 0 && !(PathEnds.Count == 1 && PathEnds[0] == value))
            ClearPath();
    }

    public IEnumerable<GraphbaseEdge> EdgesOf(string id) => Edges.Values.Where(e => e.From == id || e.To == id);

    public int Degree(string id) => Edges.Values.Count(e => e.From == id || e.To == id);

    public string NameOf(string id) => Nodes.TryGetValue(id, out var node) ? node.Name : id;

    public void SetStatus(string text, bool error = false)
    {
        Status = text;
        IsError = error;
    }

    // ── The paths (Shift + click) ──

    /// <summary>The most paths listed beside the graph for two nodes (all of them are lit in the graph).</summary>
    public const int PathsListed = 12;

    /// <summary>The longest path searched in graphbase when the graph drawn has none (its graphql.max_hops, 10, at most).</summary>
    private const int PathMaxEdges = 10;

    /// <summary>The nodes chosen with Shift + click, in the order chosen: with two or more, the paths between them are shown.</summary>
    public List<string> PathEnds { get; } = [];

    /// <summary>The nodes chosen and the nodes of the shortest paths between them.</summary>
    public HashSet<string> PathNodes { get; } = new(StringComparer.Ordinal);

    /// <summary>The edges (their ids) of the shortest paths between the nodes chosen.</summary>
    public HashSet<string> PathEdges { get; } = new(StringComparer.Ordinal);

    /// <summary>The shortest paths of each pair of the nodes chosen.</summary>
    public List<GraphbasePaths> Paths { get; } = [];

    /// <summary>True when a path is shown: the nodes out of the paths are faded.</summary>
    public bool PathShown => PathEdges.Count > 0;

    /// <summary>The nodes chosen or the paths between them changed.</summary>
    public event Action? PathChanged;

    // ── The kinds shown (the boxes of the legend) ──

    /// <summary>
    /// The kinds of nodes unticked in the legend: their spheres, names and edges are 90 % transparent, and a click goes
    /// through them. Kept by kind, whatever the graph drawn (a kind unticked stays so for the next search).
    /// </summary>
    public HashSet<string> HiddenKinds { get; } = new(StringComparer.Ordinal);

    /// <summary>The kinds shown or not changed.</summary>
    public event Action? KindsChanged;

    /// <summary>True when the node <paramref name="id"/> is of a kind unticked.</summary>
    public bool IsHidden(string id) => HiddenKinds.Count > 0 && Nodes.TryGetValue(id, out var node) && HiddenKinds.Contains(node.Kind);

    /// <summary>Shows the nodes of <paramref name="kind"/> (<paramref name="shown"/>) or makes them 90 % transparent.</summary>
    public void ShowKind(string kind, bool shown)
    {
        if (shown ? HiddenKinds.Remove(kind) : HiddenKinds.Add(kind))
            KindsChanged?.Invoke();
    }

    /// <summary>Every kind of the graph drawn shown (<paramref name="shown"/>), or every one of them but <paramref name="except"/> transparent.</summary>
    public void ShowAllKinds(bool shown, string? except = null)
    {
        var kinds = Nodes.Values.Select(n => n.Kind).Distinct(StringComparer.Ordinal).ToList();
        var changed = false;
        foreach (var kind in kinds)
            changed |= shown || kind == except ? HiddenKinds.Remove(kind) : HiddenKinds.Add(kind);
        if (changed)
            KindsChanged?.Invoke();
    }

    /// <summary>The kinds unticked, set again (from the preferences).</summary>
    public void SetHiddenKinds(IEnumerable<string> kinds)
    {
        HiddenKinds.Clear();
        HiddenKinds.UnionWith(kinds);
        KindsChanged?.Invoke();
    }

    private bool _choosingPath;

    /// <summary>
    /// Shift + click on a node: it joins the nodes chosen (or leaves them when it is one). The node selected before, by
    /// a click without Shift, is the first of them. With two nodes or more, the shortest paths between each pair of
    /// them are shown: the fewest edges, followed in both directions, in the graph drawn; several paths of the same
    /// length are all shown. Two nodes with no path in the graph drawn: the shortest one of graphbase is searched, and
    /// its nodes added to the graph drawn.
    /// </summary>
    public void TogglePathEnd(string id)
    {
        if (!Nodes.ContainsKey(id))
            return;
        if (PathEnds.Count == 0 && SelectedId is { } first && first != id && Nodes.ContainsKey(first))
            PathEnds.Add(first);
        if (!PathEnds.Remove(id))
            PathEnds.Add(id);
        // The panel of the paths shows the nodes chosen this way too.
        PathChoice.Clear();
        PathChoice.AddRange(PathEnds);
        PathChoiceChanged?.Invoke();
        _choosingPath = true;
        try
        {
            // One node: its details beside, as selected. Two or more: the paths beside.
            SelectedId = PathEnds.Count == 1 ? PathEnds[0] : null;
        }
        finally
        {
            _choosingPath = false;
        }
        UpdatePaths();
        if (Paths.Any(p => p.Length < 0))
        {
            SayPaths(": searching graphbase…", searching: true);
            _ = FindPathsInGraphbaseAsync();
        }
        else
            SayPaths();
    }

    // ── The panel of the paths: nodes chosen among those drawn, then SEARCH ──

    /// <summary>
    /// The nodes chosen in the panel of the paths (or by Shift + click), in the order chosen: SEARCH shows the shortest
    /// paths between them. Kept when the paths are left, so that they can be searched again.
    /// </summary>
    public List<string> PathChoice { get; } = [];

    /// <summary>The nodes chosen in the panel of the paths changed.</summary>
    public event Action? PathChoiceChanged;

    /// <summary>Adds the node <paramref name="id"/> to the nodes chosen (<paramref name="chosen"/>), or takes it out.</summary>
    public void ChoosePathNode(string id, bool chosen)
    {
        if (chosen ? PathChoice.Contains(id) || !Nodes.ContainsKey(id) : !PathChoice.Remove(id))
            return;
        if (chosen)
            PathChoice.Add(id);
        PathChoiceChanged?.Invoke();
    }

    /// <summary>CLEAR: no node chosen, the paths left.</summary>
    public void ClearPathChoice()
    {
        PathChoice.Clear();
        PathChoiceChanged?.Invoke();
        ClearPath();
    }

    /// <summary>
    /// SEARCH of the panel of the paths: the shortest paths between every pair of the nodes chosen (2 or more), as a
    /// Shift + click on each of them does; the nodes and edges out of the paths are faded. A pair with no path in the
    /// graph drawn is searched in graphbase.
    /// </summary>
    public void SearchPaths()
    {
        PathChoice.RemoveAll(id => !Nodes.ContainsKey(id));
        if (PathChoice.Count < 2)
        {
            PathChoiceChanged?.Invoke();
            SetStatus("Choose 2 nodes or more among those drawn, then SEARCH", true);
            return;
        }
        PathEnds.Clear();
        PathEnds.AddRange(PathChoice);
        _choosingPath = true;
        try
        {
            SelectedId = null;
        }
        finally
        {
            _choosingPath = false;
        }
        UpdatePaths();
        if (Paths.Any(p => p.Length < 0))
        {
            SayPaths(": searching graphbase…", searching: true);
            _ = FindPathsInGraphbaseAsync();
        }
        else
            SayPaths();
    }

    /// <summary>A click without Shift: this node selected (null: none), the paths left.</summary>
    public void Select(string? id)
    {
        ClearPath();
        SelectedId = id;
    }

    /// <summary>No node chosen any more: the graph as before.</summary>
    public void ClearPath()
    {
        if (PathEnds.Count == 0)
            return;
        var shown = PathEnds.Count >= 2;
        PathEnds.Clear();
        UpdatePaths();
        if (shown)
            SetStatus($"The paths are left · {Nodes.Count} nodes, {Edges.Count} edges drawn");
    }

    /// <summary>The edges drawn between two nodes, in either direction.</summary>
    public IEnumerable<GraphbaseEdge> EdgesBetween(string a, string b) =>
        Edges.Values.Where(e => (e.From == a && e.To == b) || (e.From == b && e.To == a));

    /// <summary>
    /// The paths between the nodes chosen, in the graph drawn (after a Shift + click, and when the graph drawn changes):
    /// a node is on a shortest path from a to b when its distances to a and to b add up to the distance from a to b.
    /// </summary>
    private void UpdatePaths()
    {
        PathEnds.RemoveAll(id => !Nodes.ContainsKey(id));
        PathNodes.Clear();
        PathEdges.Clear();
        Paths.Clear();
        PathNodes.UnionWith(PathEnds);
        if (PathEnds.Count >= 2)
        {
            var neighbours = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var edge in Edges.Values.Where(e => e.From != e.To))
            {
                (neighbours.TryGetValue(edge.From, out var from) ? from : neighbours[edge.From] = []).Add(edge.To);
                (neighbours.TryGetValue(edge.To, out var to) ? to : neighbours[edge.To] = []).Add(edge.From);
            }
            // Once each, by name: the paths are listed in the same order from one time to the next.
            foreach (var id in neighbours.Keys.ToList())
                neighbours[id] = neighbours[id].Distinct(StringComparer.Ordinal).OrderBy(NameOf, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal).ToList();
            var distances = PathEnds.ToDictionary(id => id, id => Distances(neighbours, id), StringComparer.Ordinal);
            for (var i = 0; i < PathEnds.Count; i++)
            for (var j = i + 1; j < PathEnds.Count; j++)
            {
                string a = PathEnds[i], b = PathEnds[j];
                Dictionary<string, int> fromA = distances[a], fromB = distances[b];
                if (!fromA.TryGetValue(b, out var length))
                {
                    Paths.Add(new GraphbasePaths(a, b, -1, 0, []));
                    continue;
                }
                bool On(string node, int at) => fromA.TryGetValue(node, out var x) && x == at && fromB.TryGetValue(node, out var y) && x + y == length;
                PathNodes.UnionWith(fromA.Keys.Where(node => fromB.TryGetValue(node, out var y) && fromA[node] + y == length));
                foreach (var edge in Edges.Values)
                    if (edge.From != edge.To
                        && ((fromA.TryGetValue(edge.From, out var f) && On(edge.From, f) && On(edge.To, f + 1))
                            || (fromA.TryGetValue(edge.To, out var t) && On(edge.To, t) && On(edge.From, t + 1))))
                        PathEdges.Add(edge.Id);
                // How many: the paths to a node are those to the nodes before it, added.
                var counts = new Dictionary<string, long>(StringComparer.Ordinal) { [a] = 1 };
                foreach (var node in fromA.Where(d => On(d.Key, d.Value)).OrderBy(d => d.Value).Select(d => d.Key))
                    if (neighbours.TryGetValue(node, out var next))
                        foreach (var other in next.Where(o => On(o, fromA[node] + 1)))
                            counts[other] = Math.Min(long.MaxValue / 2, counts.GetValueOrDefault(other) + counts.GetValueOrDefault(node));
                var listed = new List<IReadOnlyList<string>>();
                Walk(new List<string> { a });
                Paths.Add(new GraphbasePaths(a, b, length, counts.GetValueOrDefault(b), listed));

                // The paths listed: from a, to each next node one edge further from a and one nearer to b.
                void Walk(List<string> path)
                {
                    if (path[^1] == b)
                    {
                        listed.Add(path.ToArray());
                        return;
                    }
                    if (!neighbours.TryGetValue(path[^1], out var next))
                        return;
                    foreach (var other in next)
                    {
                        if (listed.Count >= PathsListed)
                            return;
                        if (!On(other, path.Count))
                            continue;
                        path.Add(other);
                        Walk(path);
                        path.RemoveAt(path.Count - 1);
                    }
                }
            }
        }
        PathChanged?.Invoke();
    }

    /// <summary>How many edges from a node to each node it reaches (breadth first, the edges in both directions).</summary>
    private static Dictionary<string, int> Distances(Dictionary<string, List<string>> neighbours, string from)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [from] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!neighbours.TryGetValue(node, out var next))
                continue;
            foreach (var other in next)
                if (distances.TryAdd(other, distances[node] + 1))
                    queue.Enqueue(other);
        }
        return distances;
    }

    /// <summary>
    /// The status of the paths shown; <paramref name="noPath"/> is said after the pairs with no path (where it was
    /// searched, or that graphbase is being searched: <paramref name="searching"/>, no error yet).
    /// </summary>
    private void SayPaths(string noPath = "", string after = "", bool searching = false)
    {
        switch (PathEnds.Count)
        {
            case 0:
                SetStatus("No node chosen any more: the paths are left");
                return;
            case 1:
                SetStatus($"{NameOf(PathEnds[0])} chosen: Shift + click another node to see the paths between them");
                return;
        }
        var said = Paths.Take(4).Select(p => p.Length < 0
            ? $"no path between {NameOf(p.From)} and {NameOf(p.To)}{noPath}"
            : $"{NameOf(p.From)} ↔ {NameOf(p.To)}: {(p.Count == 1 ? "1 shortest path" : $"{p.Count} shortest paths")} of {p.Length} edge(s)");
        SetStatus(string.Join(" · ", said) + (Paths.Count > 4 ? $" · and {Paths.Count - 4} more pair(s)" : "")
                  + (PathShown && Nodes.Count > PathNodes.Count ? " · the other nodes are faded" : "") + after, !searching && Paths.All(p => p.Length < 0));
    }

    /// <summary>
    /// The nodes chosen with no path between them in the graph drawn: the shortest path of graphbase (its edges in both
    /// directions, <see cref="PathMaxEdges"/> at most), its nodes added to the graph drawn, the paths looked for again.
    /// </summary>
    private Task FindPathsInGraphbaseAsync() => Run(async ct =>
    {
        var found = new List<string>();
        foreach (var pair in Paths.Where(p => p.Length < 0).ToList())
        {
            var data = await _client.RunAsync("query($a: ID!, $b: ID!, $d: Int!) { shortestPath(from: $a, to: $b, maxDepth: $d) { nodes { id } } }",
                new { a = pair.From, b = pair.To, d = PathMaxEdges }, ct);
            if (data.GetProperty("shortestPath") is { ValueKind: JsonValueKind.Object } path)
                found.AddRange(Ids(path.GetProperty("nodes")));
        }
        var none = $", in the graph drawn nor in graphbase ({PathMaxEdges} edges at most)";
        if (found.Count == 0)
        {
            SayPaths(none);
            return;
        }
        var added = found.Where(id => !Nodes.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        if (Nodes.Count + added.Count > MaxNodes)
        {
            SetStatus($"The path of graphbase goes through {added.Count} node(s) not drawn, and {MaxNodes} nodes are drawn at most: search fewer nodes first", true);
            return;
        }
        // The nodes of the path with those drawn, and every edge between them (the edge of the path may not be drawn).
        var (nodes, edges, _) = await SubgraphAsync(Nodes.Keys.Concat(added).ToList(), 0, ct);
        Show(nodes, edges, [], replace: false);
        SayPaths(none, added.Count > 0 ? $" · {added.Count} node(s) of the path added from graphbase" : "");
    });

    // ── What is drawn ──

    /// <summary>The labels of the graph, then the whole graph (or its most linked nodes when it is large).</summary>
    public Task LoadAsync() => Run(async ct =>
    {
        var data = await _client.RunAsync("{ labels { name count } }", null, ct);
        AllLabels.Clear();
        AllLabels.AddRange(data.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString() ?? "").Where(l => l.Length > 0));
        LabelsChanged?.Invoke();
        await OverviewAsync(ct);
    });

    /// <summary>
    /// The search: the nodes holding the text, or the texts joined by AND, OR and ( ) (the argument <c>match</c> of
    /// graphbase: <c>kaonomi AND (agent OR mcp)</c>), and of the label, with their neighbours to the depth; nothing to
    /// search: the whole graph.
    /// </summary>
    public Task SearchAsync() => Run(async ct =>
    {
        var text = Search.Trim();
        var label = Label.Length > 0 ? Label : null;
        if (text.Length == 0 && label is null)
        {
            await OverviewAsync(ct);
            return;
        }
        var what = string.Join(", ", new[] { text.Length > 0 ? $"\"{text}\"" : null, label is null ? null : $"label {label}" }.OfType<string>());
        var found = await _client.RunAsync("query($s: String, $l: String) { nodes(match: $s, label: $l, first: 100) { id } nodeCount(match: $s, label: $l) }",
            new { s = text.Length > 0 ? text : null, l = label }, ct);
        var ids = Ids(found.GetProperty("nodes"));
        var count = found.GetProperty("nodeCount").GetInt32();
        if (ids.Count == 0)
        {
            SetStatus($"Nothing matches {what} (the graph drawn is left as it is)");
            return;
        }
        var (nodes, edges, truncated) = await SubgraphAsync(ids, Depth, ct);
        Show(nodes, edges, ids, replace: true);
        SelectedId = ids.Count == 1 ? ids[0] : null;
        SetStatus($"{count} node(s) match {what}{(count > ids.Count ? $" (the first {ids.Count} drawn)" : "")}; with their neighbours to {Depth} edge(s): "
                  + $"{Nodes.Count} nodes, {Edges.Count} edges{(truncated ? $" (cut at {MaxNodes} nodes)" : "")}");
    });

    /// <summary>
    /// ⇄ SYNC: writes to graphbase the repository of /archi, the ontology and the relations of the server, then draws
    /// the graph again and says what was done.
    /// </summary>
    public Task SyncAsync() => Run(async ct =>
    {
        SetStatus("Writing /archi, the ontology and the relations to graphbase…");
        var archi = await new ArchiWindowViewModel().SyncGraphbaseAsync();
        var knowledge = await GraphbaseSync.SyncKnowledgeAsync(manual: true, ct);
        await OverviewAsync(ct);
        SetStatus($"/archi: {archi ?? "not written"} · ontology and relations: {knowledge ?? "not written"}",
            (archi ?? "").StartsWith("Not", StringComparison.Ordinal) || (knowledge ?? "").StartsWith("Not", StringComparison.Ordinal));
    });

    /// <summary>Adds the neighbours of a node to the graph drawn (a double-click on its sphere).</summary>
    public Task ExpandAsync(string id) => Run(async ct =>
    {
        var before = Nodes.Count;
        var (around, _, _) = await SubgraphAsync([id], 1, ct);
        var ids = Nodes.Keys.Concat(Ids(around)).Distinct(StringComparer.Ordinal).Take(MaxNodes).ToList();
        // Every edge between the nodes drawn, the new ones with the others too.
        var (nodes, edges, _) = await SubgraphAsync(ids, 0, ct);
        Show(nodes, edges, [], replace: false);
        SelectedId = id;
        SetStatus($"{NameOf(id)}: {Nodes.Count - before} neighbour(s) added · {Nodes.Count} nodes, {Edges.Count} edges"
                  + (Nodes.Count >= MaxNodes ? $" (at most {MaxNodes} nodes are drawn)" : ""));
    });

    /// <summary>
    /// Runs the query of the GraphQL extension: its answer in <see cref="Result"/>, and the nodes it names (any object
    /// with an id, the ends of the edges) drawn with the edges between them. A viewer runs no mutation; no subscription
    /// here (it would wait for the changes).
    /// </summary>
    public Task RunQueryAsync() => Run(async ct => await RunQueryCoreAsync(ct));

    /// <summary>
    /// ✨ TO GRAPHQL: the model selected in the chat writes the query of <see cref="Ask"/> (from the schema of graphbase,
    /// its labels, its types of edges and the properties of its main labels; the prompt <c>graphbase.query</c> of
    /// /prompts), put in the editor and run. A query refused by graphbase goes back once to the model with its errors,
    /// to be corrected. A mutation is not run: it waits in the editor for RUN.
    /// </summary>
    public Task AskAsync() => Run(async ct =>
    {
        var text = Ask.Trim();
        if (text.Length == 0)
        {
            SetStatus("Write first what you want to see, in your words (\"the members of the Parti démocrate\")", true);
            return;
        }
        if (_models is null || (QueryModel ?? _model()) is not { Length: > 0 } model)
        {
            SetStatus("No model to write the query: choose one in the chat (MODEL)", true);
            return;
        }
        SetStatus($"✨ {model} writes the query of \"{text}\"…");
        var system = Prompts.Get("graphbase.query",
            ("schema", await _client.SchemaAsync(ct)), ("labels", await CountsAsync("labels", 60, ct)), ("edgeTypes", await CountsAsync("edgeTypes", 80, ct)),
            ("properties", await PropertiesAsync(ct)));
        var query = Clean(await _models.ChatAsync(model, system, text, ct));
        Variables = "";
        for (var attempt = 1; ; attempt++)
        {
            Query = $"# {text.ReplaceLineEndings(" ")}\n{query}";
            var code = Code(query);
            if (Regex.IsMatch(code, @"(^|[\s}])(mutation|subscription)\b"))
            {
                SetStatus($"✨ {model} wrote a mutation: check it in the editor, then RUN (it is not run by itself)");
                return;
            }
            var error = await RunQueryCoreAsync(ct);
            if (error is null)
            {
                SetStatus($"✨ {model}: {Status}", IsError);
                return;
            }
            if (attempt == 2)
            {
                SetStatus($"✨ The query of {model} is refused by graphbase, even corrected: {error} (change it in the editor, or ask otherwise)", true);
                return;
            }
            SetStatus($"✨ {model} corrects its query ({error})…");
            query = Clean(await _models.ChatAsync(model, system,
                $"{text}\n\nThe query you wrote:\n{query}\n\nKAONOMI.graphbase refused it:\n{Result}\n\nWrite the corrected query, and nothing else.", ct));
        }
    });

    /// <summary>The labels or the types of edges of the graph with their counts, the most used first.</summary>
    private async Task<string> CountsAsync(string field, int most, CancellationToken ct)
    {
        var data = await _client.RunAsync($"{{ list: {field} {{ name count }} }}", null, ct);
        var items = data.GetProperty("list").EnumerateArray().Take(most).Select(c => $"{c.GetProperty("name").GetString()} ({c.GetProperty("count").GetInt32()})").ToList();
        return items.Count == 0 ? "none" : string.Join(", ", items);
    }

    /// <summary>The names of the properties of a few nodes of each main label: "PERSON: name, entityType, startDate…".</summary>
    private async Task<string> PropertiesAsync(CancellationToken ct)
    {
        var labels = (await _client.RunAsync("{ labels { name } }", null, ct)).GetProperty("labels").EnumerateArray()
            .Select(l => l.GetProperty("name").GetString()).OfType<string>().Take(12).ToList();
        if (labels.Count == 0)
            return "none";
        var query = "{ " + string.Join(" ", labels.Select((l, i) => $"l{i}: nodes(label: {JsonSerializer.Serialize(l)}, first: 5) {{ properties }}")) + " }";
        var data = await _client.RunAsync(query, null, ct);
        return string.Join("; ", labels.Select((label, i) =>
        {
            var keys = data.GetProperty($"l{i}").EnumerateArray().SelectMany(n => n.GetProperty("properties").EnumerateObject().Select(p => p.Name)).Distinct().Take(30);
            return $"{label}: {string.Join(", ", keys)}";
        }));
    }

    /// <summary>The query in an answer of a model: without its Markdown fence, its reasoning, what it says before.</summary>
    private static string Clean(string answer)
    {
        var text = Regex.Replace(answer, "<think>[\\s\\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
        if (Regex.Match(text, "```(?:graphql|gql|json)?[ \\t]*\\r?\\n([\\s\\S]*?)```", RegexOptions.IgnoreCase) is { Success: true } fenced)
            text = fenced.Groups[1].Value;
        if (Regex.Match(text, @"(^|\n)\s*(query|mutation|subscription|fragment|\{)") is { Success: true } start)
            text = text[start.Index..];
        return text.Trim();
    }

    /// <summary>The query of the editor run (<see cref="RunQueryAsync"/>); the error graphbase gave (null: none).</summary>
    private async Task<string?> RunQueryCoreAsync(CancellationToken ct)
    {
        var code = Code(Query);
        if (Regex.IsMatch(code, @"(^|[\s}])subscription\b"))
        {
            SetStatus("A subscription is not run here (it waits for the changes): use the IDE of graphbase in a browser", true);
            return null;
        }
        if (Regex.IsMatch(code, @"(^|[\s}])mutation\b") && !UserSession.CanChange)
        {
            SetStatus("A viewer does not change the graph: no mutation", true);
            return null;
        }
        JsonElement? variables = null;
        if (!string.IsNullOrWhiteSpace(Variables))
        {
            try
            {
                variables = JsonSerializer.Deserialize<JsonElement>(Variables);
            }
            catch (JsonException ex)
            {
                SetStatus($"The variables are not JSON: {ex.Message}", true);
                return null;
            }
            if (variables.Value.ValueKind != JsonValueKind.Object)
            {
                SetStatus("The variables are a JSON object: { \"name\": value, … }", true);
                return null;
            }
        }
        var (status, body) = await _client.SendAsync(Query, variables, ct);
        Result = JsonSerializer.Serialize(body, Indented);
        var error = body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
            ? $"GraphQL: {(errors[0].TryGetProperty("message", out var m) ? m.GetString() : "error")}" + (errors.GetArrayLength() > 1 ? $" (and {errors.GetArrayLength() - 1} more)" : "")
            : status is < 200 or > 299 ? $"HTTP {status}" : null;
        var named = new List<string>();
        if (body.TryGetProperty("data", out var data))
            Collect(data, named);
        named = named.Distinct(StringComparer.Ordinal).ToList();
        if (named.Count == 0)
        {
            SetStatus(error ?? "Done: no node in the answer, nothing to draw (the graph drawn is left as it is)", error is not null);
            return error;
        }
        // The ids that are nodes (an object with an id may be an edge, or a node deleted by the mutation).
        var known = Ids((await _client.RunAsync("query($ids: [ID!]) { nodes(ids: $ids, first: 10000) { id } }", new { ids = named.Take(10000) }, ct)).GetProperty("nodes"));
        if (known.Count == 0)
        {
            SetStatus(error ?? "Done: the ids of the answer are no nodes of the graph, nothing to draw", error is not null);
            return error;
        }
        var drawn = known.Take(MaxNodes).ToList();
        var (nodes, edges, _) = await SubgraphAsync(drawn, 0, ct);
        Show(nodes, edges, drawn, replace: true);
        SelectedId = drawn.Count == 1 ? drawn[0] : null;
        SetStatus(error ?? $"Drawn from the answer: {Nodes.Count} nodes, {Edges.Count} edges between them"
                  + (known.Count > drawn.Count ? $" (the first {MaxNodes} of {known.Count})" : ""), error is not null);
        return error;
    }

    // ── Private ──

    private async Task OverviewAsync(CancellationToken ct)
    {
        var stats = (await _client.RunAsync("{ stats { nodes edges } }", null, ct)).GetProperty("stats");
        TotalNodes = stats.GetProperty("nodes").GetInt32();
        TotalEdges = stats.GetProperty("edges").GetInt32();
        if (TotalNodes <= MaxNodes)
        {
            var data = await _client.RunAsync($"query($n: Int!) {{ nodes(first: $n) {{ {NodeFields} }} edges(first: 10000) {{ {EdgeFields} }} }}", new { n = MaxNodes }, ct);
            Show(data.GetProperty("nodes"), data.GetProperty("edges"), [], replace: true);
            SetStatus(TotalNodes == 0 ? $"The graph of {Url} is empty: /archi fills it, or a mutation of the GraphQL extension" : $"The whole graph: {Nodes.Count} nodes, {Edges.Count} edges");
            return;
        }
        // A large graph: its hubs (the most linked nodes) and the edges between them.
        var hubs = Ids((await _client.RunAsync("{ nodes(first: 150, orderBy: \"degree\", descending: true) { id } }", null, ct)).GetProperty("nodes"));
        var (nodes, edges, _) = await SubgraphAsync(hubs, 0, ct);
        Show(nodes, edges, [], replace: true);
        SetStatus($"The {Nodes.Count} most linked of the {TotalNodes} nodes and the {Edges.Count} edges between them: search to see the others");
    }

    private async Task<(JsonElement Nodes, JsonElement Edges, bool Truncated)> SubgraphAsync(IReadOnlyList<string> ids, int depth, CancellationToken ct)
    {
        var data = await _client.RunAsync(
            $"query($ids: [ID!]!, $d: Int!, $limit: Int!) {{ subgraph(ids: $ids, depth: $d, limit: $limit) {{ nodes {{ {NodeFields} }} edges {{ {EdgeFields} }} truncated }} }}",
            new { ids, d = depth, limit = MaxNodes }, ct);
        var subgraph = data.GetProperty("subgraph");
        return (subgraph.GetProperty("nodes"), subgraph.GetProperty("edges"), subgraph.GetProperty("truncated").GetBoolean());
    }

    /// <summary>Draws these nodes and edges, in place of the graph drawn or with it.</summary>
    private void Show(JsonElement nodes, JsonElement edges, IEnumerable<string> found, bool replace)
    {
        if (replace)
        {
            Nodes.Clear();
            Edges.Clear();
            Found.Clear();
        }
        foreach (var node in nodes.EnumerateArray().Select(GraphbaseNode.Read))
            if (Nodes.Count < MaxNodes || Nodes.ContainsKey(node.Id))
                Nodes[node.Id] = node;
        foreach (var edge in edges.EnumerateArray().Select(GraphbaseEdge.Read))
            if (Nodes.ContainsKey(edge.From) && Nodes.ContainsKey(edge.To))
                Edges[edge.Id] = edge;
        Found.UnionWith(found.Where(Nodes.ContainsKey));
        if (SelectedId is { } selected && !Nodes.ContainsKey(selected))
            SelectedId = null;
        // The paths shown follow the graph drawn (an end gone, a shorter path with the new edges).
        if (PathEnds.Count > 0)
            UpdatePaths();
        GraphChanged?.Invoke(replace);
        OnPropertyChanged(nameof(Selected));
    }

    private static List<string> Ids(JsonElement nodes) =>
        nodes.EnumerateArray().Select(n => n.GetProperty("id").GetString()).OfType<string>().ToList();

    /// <summary>The ids named in an answer: of every object with an id, and the ends of the edges (not inside the properties).</summary>
    private static void Collect(JsonElement element, List<string> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && property.Name is "id" or "fromId" or "toId")
                        ids.Add(property.Value.GetString()!);
                    else if (property.Name != "properties")
                        Collect(property.Value, ids);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, ids);
                break;
        }
    }

    /// <summary>The query without its comments and strings (to find its kind of operation).</summary>
    private static string Code(string query) =>
        Regex.Replace(Regex.Replace(query, "\"\"\"[\\s\\S]*?\"\"\"|\"(\\\\.|[^\"\\\\])*\"", "\"\""), "#[^\\n]*", "");

    /// <summary>Runs one thing at a time (a new one stops the one before), busy meanwhile, its failure in the status.</summary>
    private async Task Run(Func<CancellationToken, Task> work)
    {
        _running?.Cancel();
        var cancel = _running = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            if (!await _client.RunningAsync(cancel.Token))
            {
                SetStatus($"KAONOMI.graphbase does not answer at {Url}: start it from /process", true);
                return;
            }
            await work(cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // Another search or query took its place.
        }
        catch (Exception ex) when (ex is GraphbaseException or HttpRequestException or JsonException or IOException or OperationCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            SetStatus(ex is OperationCanceledException ? $"{Url} did not answer in time" : ex.Message, true);
        }
        finally
        {
            if (ReferenceEquals(_running, cancel))
                IsBusy = false;
        }
    }
}
