using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace KAONOMI.graphbase.Storage;

/// <summary>
/// The graph: every node and edge in memory (with the edges of each node, the nodes of each label, the edges of each
/// type), kept in <c>graph.db</c> (SQLite, in <c>path.data</c>). A write is checked, then written to the database in
/// one transaction, and only then applied in memory: it is all done or not at all (an import too). The writes go one
/// at a time, the reads together; what a read returns never changes (<see cref="GraphNode"/>, <see cref="GraphEdge"/>).
/// </summary>
public sealed class GraphStore : IDisposable
{
    public const string DatabaseName = "graph.db";
    private const int MaxNameLength = 256;

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphEdge> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _out = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _in = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _byLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _byType = new(StringComparer.Ordinal);
    private readonly SqliteConnection _db;
    private readonly FileStream _dataLock;
    private bool _disposed;

    /// <summary>The folder of the graph.</summary>
    public string DataPath { get; }

    /// <summary>The edges left out at the opening because a node of their ends was missing.</summary>
    public int OrphanEdges { get; }

    /// <summary>The changes of each write, once done (outside the lock: a handler may read the graph).</summary>
    public event Action<IReadOnlyList<GraphChange>>? Changed;

    private GraphStore(string dataPath, FileStream dataLock)
    {
        DataPath = dataPath;
        _dataLock = dataLock;
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataPath, DatabaseName),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        _db.Open();
        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS nodes (
                id TEXT PRIMARY KEY, labels TEXT NOT NULL, properties TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS edges (
                id TEXT PRIMARY KEY, type TEXT NOT NULL, source TEXT NOT NULL, target TEXT NOT NULL,
                properties TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS edges_source ON edges(source);
            CREATE INDEX IF NOT EXISTS edges_target ON edges(target);
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT OR IGNORE INTO meta(key, value) VALUES ('schema', '1');
            """);

        using (var read = _db.CreateCommand())
        {
            read.CommandText = "SELECT id, labels, properties, created, updated FROM nodes";
            using var row = read.ExecuteReader();
            while (row.Read())
                AddNode(new GraphNode
                {
                    Id = row.GetString(0),
                    Labels = JsonSerializer.Deserialize<string[]>(row.GetString(1)) ?? [],
                    Properties = JsonValues.Parse(row.GetString(2)),
                    Created = Date(row.GetString(3)),
                    Updated = Date(row.GetString(4))
                });
        }
        using (var read = _db.CreateCommand())
        {
            read.CommandText = "SELECT id, type, source, target, properties, created, updated FROM edges";
            using var row = read.ExecuteReader();
            while (row.Read())
            {
                var edge = new GraphEdge
                {
                    Id = row.GetString(0),
                    Type = row.GetString(1),
                    From = row.GetString(2),
                    To = row.GetString(3),
                    Properties = JsonValues.Parse(row.GetString(4)),
                    Created = Date(row.GetString(5)),
                    Updated = Date(row.GetString(6))
                };
                if (_nodes.ContainsKey(edge.From) && _nodes.ContainsKey(edge.To))
                    AddEdge(edge);
                else
                    OrphanEdges++;
            }
        }
    }

    /// <summary>Opens (or makes) the graph of <paramref name="dataPath"/>; a second server on the same folder is refused.</summary>
    public static GraphStore Open(string dataPath)
    {
        Directory.CreateDirectory(dataPath);
        FileStream dataLock;
        try
        {
            dataLock = new FileStream(Path.Combine(dataPath, "graphbase.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new DataPathInUseException($"the graph of [{dataPath}] is used by another KAONOMI.graphbase");
        }
        try
        {
            return new GraphStore(dataPath, dataLock);
        }
        catch
        {
            dataLock.Dispose();
            throw;
        }
    }

    // ------------------------------------------------------------------ reads

    public int NodeCount => Read(() => _nodes.Count);
    public int EdgeCount => Read(() => _edges.Count);

    public GraphNode? GetNode(string id) => Read(() => _nodes.GetValueOrDefault(id));
    public GraphEdge? GetEdge(string id) => Read(() => _edges.GetValueOrDefault(id));

    public IReadOnlyList<GraphNode> FindNodes(NodeQuery query) => Read(() =>
    {
        var nodes = MatchNodes(query).ToList();
        // By degree: the most linked first when descending (the hubs of the graph).
        var sorted = string.Equals(query.OrderBy?.Trim(), "degree", StringComparison.Ordinal)
            ? (query.Descending ? nodes.OrderByDescending(DegreeOf) : nodes.OrderBy(DegreeOf)).ThenBy(n => n.Id, StringComparer.Ordinal)
            : Sort(nodes, query.OrderBy, query.Descending, query.Ids is not null, n => n.Id, n => n.Created, n => n.Updated, n => n.Properties);
        return sorted.Skip(Math.Max(0, query.Skip)).Take(Math.Max(0, query.First)).ToList();
    });

    /// <summary>How many edges a node has, either way (a loop twice).</summary>
    private int DegreeOf(GraphNode node) =>
        (_out.TryGetValue(node.Id, out var outgoing) ? outgoing.Count : 0) + (_in.TryGetValue(node.Id, out var incoming) ? incoming.Count : 0);

    public int CountNodes(NodeQuery query) => Read(() => MatchNodes(query).Count());

    public IReadOnlyList<GraphEdge> FindEdges(EdgeQuery query) => Read(() =>
        Sort(MatchEdges(query).ToList(), query.OrderBy, query.Descending, query.Ids is not null, e => e.Id, e => e.Created, e => e.Updated, e => e.Properties)
            .Skip(Math.Max(0, query.Skip)).Take(Math.Max(0, query.First)).ToList());

    public int CountEdges(EdgeQuery query) => Read(() => MatchEdges(query).Count());

    /// <summary>The edges of a node (oldest first), of a type or of any.</summary>
    public IReadOnlyList<GraphEdge> EdgesOf(string nodeId, Direction direction, string? type, int first) => Read(() =>
        Adjacent(nodeId, direction, type is null ? null : [type]).Take(Math.Max(0, first)).ToList());

    /// <summary>The nodes at the other end of the edges of a node, each once, with a label or any.</summary>
    public IReadOnlyList<GraphNode> NeighborsOf(string nodeId, Direction direction, string? type, string? label, int first) => Read(() =>
        Adjacent(nodeId, direction, type is null ? null : [type])
            .Select(e => e.Other(nodeId)).Distinct(StringComparer.Ordinal)
            .Select(id => _nodes[id]).Where(n => label is null || n.HasLabel(label))
            .Take(Math.Max(0, first)).ToList());

    public int Degree(string nodeId, Direction direction, string? type) => Read(() =>
        Adjacent(nodeId, direction, type is null ? null : [type]).Count());

    /// <summary>
    /// The shortest path from a node to another (the fewest edges, breadth first), following the edges in
    /// <paramref name="direction"/> of the types given (any when none), <paramref name="maxDepth"/> edges at most;
    /// null when there is none.
    /// </summary>
    public GraphPath? ShortestPath(string from, string to, Direction direction, IReadOnlyCollection<string>? types, int maxDepth) => Read(() =>
    {
        var start = _nodes.GetValueOrDefault(from) ?? throw NodeNotFound(from);
        if (!_nodes.ContainsKey(to))
            throw NodeNotFound(to);
        if (from == to)
            return new GraphPath([start], []);
        var reachedBy = new Dictionary<string, GraphEdge?>(StringComparer.Ordinal) { [from] = null };
        var frontier = new List<string> { from };
        for (var depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
                foreach (var edge in Adjacent(node, direction, types))
                {
                    var other = edge.Other(node);
                    if (!reachedBy.TryAdd(other, edge))
                        continue;
                    if (other == to)
                        return Walk(to, reachedBy);
                    next.Add(other);
                }
            frontier = next;
        }
        return null;
    });

    /// <summary>
    /// The nodes within <paramref name="depth"/> edges of <paramref name="ids"/> (following <paramref name="direction"/>
    /// and the types given), <paramref name="limit"/> nodes at most, and every edge (of these types) between them.
    /// </summary>
    public Subgraph Neighborhood(IReadOnlyList<string> ids, int depth, Direction direction, IReadOnlyCollection<string>? types, int limit) => Read(() =>
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var id in ids)
        {
            if (!_nodes.ContainsKey(id))
                throw NodeNotFound(id);
            if (seen.Add(id))
                order.Add(id);
        }
        var truncated = false;
        var frontier = order.ToList();
        for (var level = 1; level <= depth && frontier.Count > 0 && !truncated; level++)
        {
            var next = new List<string>();
            foreach (var node in frontier)
            {
                foreach (var edge in Adjacent(node, direction, types))
                {
                    var other = edge.Other(node);
                    if (seen.Contains(other))
                        continue;
                    if (seen.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }
                    seen.Add(other);
                    order.Add(other);
                    next.Add(other);
                }
                if (truncated)
                    break;
            }
            frontier = next;
        }
        var edges = order.SelectMany(id => Adjacent(id, Direction.Out, types)).Where(e => seen.Contains(e.To)).ToList();
        return new Subgraph(order.Select(id => _nodes[id]).ToList(), edges, truncated);
    });

    /// <summary>The labels of the nodes, the most used first.</summary>
    public IReadOnlyList<NameCount> Labels() => Read(() => Counts(_byLabel));

    /// <summary>The types of the edges, the most used first.</summary>
    public IReadOnlyList<NameCount> EdgeTypes() => Read(() => Counts(_byType));

    // ----------------------------------------------------------------- writes

    public GraphNode CreateNode(string? id, IReadOnlyList<string>? labels, JsonElement? properties) => Write(batch =>
    {
        var nodeId = id is null ? NewId() : CheckName(id, "id");
        if (batch.Node(nodeId) is not null)
            throw new GraphException(GraphException.Exists, $"the node {nodeId} already exists");
        var node = new GraphNode { Id = nodeId, Labels = CheckLabels(labels), Properties = JsonValues.Properties(properties), Created = batch.Now, Updated = batch.Now };
        batch.PutNode(node);
        return node;
    });

    public GraphNode UpdateNode(string id, NodeChange change) => Write(batch =>
    {
        var node = batch.Node(id) ?? throw NodeNotFound(id);
        return batch.PutNodeIfChanged(node, WithChange(node, change, batch.Now));
    });

    /// <summary>
    /// The node of <paramref name="label"/> whose property <paramref name="key"/> is <paramref name="value"/>, completed
    /// with <paramref name="labels"/> and <paramref name="properties"/>; made when there is none (as MERGE of Cypher).
    /// </summary>
    public GraphNode MergeNode(string label, string key, JsonElement value, IReadOnlyList<string>? labels, JsonElement? properties) => Write(batch =>
    {
        label = CheckName(label, "label");
        key = CheckName(key, "key");
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new GraphException(GraphException.Invalid, "mergeNode needs the value of its key");
        var all = new[] { label }.Concat(labels ?? []).ToList();
        var keyed = JsonValues.ToObject(JsonValues.Properties(properties));
        keyed[key] = System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText());
        var withKey = JsonValues.ToElement(keyed);

        var existing = _byLabel.TryGetValue(label, out var set)
            ? set.Select(n => _nodes[n]).OrderBy(n => n.Created).ThenBy(n => n.Id, StringComparer.Ordinal)
                .FirstOrDefault(n => n.Properties.TryGetProperty(key, out var v) && JsonValues.DeepEquals(v, value))
            : null;
        if (existing is not null)
            return batch.PutNodeIfChanged(existing, WithChange(existing, new NodeChange { AddLabels = all, Properties = withKey }, batch.Now));
        var node = new GraphNode { Id = NewId(), Labels = CheckLabels(all), Properties = withKey, Created = batch.Now, Updated = batch.Now };
        batch.PutNode(node);
        return node;
    });

    /// <summary>Deletes a node; its edges with it when <paramref name="detach"/>, else it must have none. False: there was no such node.</summary>
    public bool DeleteNode(string id, bool detach) => Write(batch =>
    {
        if (batch.Node(id) is not { } node)
            return false;
        Delete(batch, node, detach);
        return true;
    });

    /// <summary>Deletes the nodes of a query (all of them, whatever its page) and their edges; the number deleted.</summary>
    public int DeleteNodes(NodeQuery query) => Write(batch =>
    {
        if (query.Ids is null && query.Label is null && query.Where is not { Count: > 0 } && string.IsNullOrWhiteSpace(query.Search) && string.IsNullOrWhiteSpace(query.Match))
            throw new GraphException(GraphException.Invalid, "deleteNodes needs ids, a label, conditions, a search or a match (it never deletes the whole graph)");
        var nodes = MatchNodes(query).ToList();
        foreach (var node in nodes)
            Delete(batch, node, detach: true);
        return nodes.Count;
    });

    public GraphEdge CreateEdge(string? id, string type, string from, string to, JsonElement? properties) => Write(batch =>
    {
        var edgeId = id is null ? NewId() : CheckName(id, "id");
        if (batch.Edge(edgeId) is not null)
            throw new GraphException(GraphException.Exists, $"the edge {edgeId} already exists");
        var edge = NewEdge(batch, edgeId, type, from, to, properties);
        batch.PutEdge(edge);
        return edge;
    });

    /// <summary>The edge of <paramref name="type"/> from <paramref name="from"/> to <paramref name="to"/>, its properties completed; made when there is none.</summary>
    public GraphEdge MergeEdge(string type, string from, string to, JsonElement? properties) => Write(batch =>
    {
        type = CheckName(type, "type");
        if (batch.FindEdge(from, to, type) is { } existing)
            return batch.PutEdgeIfChanged(existing, existing.With(JsonValues.Merge(existing.Properties, properties, false, null), batch.Now));
        var edge = NewEdge(batch, NewId(), type, from, to, properties);
        batch.PutEdge(edge);
        return edge;
    });

    public GraphEdge UpdateEdge(string id, JsonElement? properties, bool replace, IReadOnlyList<string>? remove) => Write(batch =>
    {
        var edge = batch.Edge(id) ?? throw new GraphException(GraphException.NotFound, $"the edge {id} does not exist");
        return batch.PutEdgeIfChanged(edge, edge.With(JsonValues.Merge(edge.Properties, properties, replace, remove), batch.Now));
    });

    public bool DeleteEdge(string id) => Write(batch =>
    {
        if (batch.Edge(id) is not { } edge)
            return false;
        batch.DeleteEdge(edge);
        return true;
    });

    /// <summary>Deletes the edges of these ids; how many there were.</summary>
    public int DeleteEdges(IReadOnlyList<string> ids) => Write(batch =>
    {
        var deleted = 0;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
            if (batch.Edge(id) is { } edge)
            {
                batch.DeleteEdge(edge);
                deleted++;
            }
        return deleted;
    });

    /// <summary>
    /// Writes many nodes and edges at once, all or nothing. A node whose id exists is completed (labels added,
    /// properties merged), else made; an edge too, and an edge without id, when <paramref name="mergeEdges"/>, completes
    /// the edge of the same type between the same nodes. The edges can go to the nodes of the same import.
    /// With <paramref name="replace"/> (the import is the source of what it gives), a node or an edge that exists becomes
    /// the one given instead: its labels and its properties those given only, an edge of other ends or type made again.
    /// </summary>
    public ImportCounts Import(IReadOnlyList<NodeWrite> nodes, IReadOnlyList<EdgeWrite> edges, bool mergeEdges, bool replace = false) => Write(batch =>
    {
        int nodesCreated = 0, nodesUpdated = 0, edgesCreated = 0, edgesUpdated = 0, unchanged = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            var item = nodes[i];
            try
            {
                if (item.Id is { } id && batch.Node(CheckName(id, "id")) is { } existing)
                {
                    var change = replace
                        ? new NodeChange { Labels = item.Labels ?? [], Properties = item.Properties, ReplaceProperties = true }
                        : new NodeChange { AddLabels = item.Labels, Properties = item.Properties };
                    var changed = WithChange(existing, change, batch.Now);
                    if (ReferenceEquals(batch.PutNodeIfChanged(existing, changed), existing))
                        unchanged++;
                    else
                        nodesUpdated++;
                    continue;
                }
                batch.PutNode(new GraphNode
                {
                    Id = item.Id ?? NewId(), Labels = CheckLabels(item.Labels), Properties = JsonValues.Properties(item.Properties),
                    Created = batch.Now, Updated = batch.Now
                });
                nodesCreated++;
            }
            catch (GraphException ex)
            {
                throw new GraphException(ex.Code, $"nodes[{i}]: {ex.Message}");
            }
        }
        for (var i = 0; i < edges.Count; i++)
        {
            var item = edges[i];
            try
            {
                var type = CheckName(item.Type, "type");
                var existing = item.Id is { } id ? batch.Edge(CheckName(id, "id")) : mergeEdges ? batch.FindEdge(item.From, item.To, type) : null;
                if (existing is not null)
                {
                    if (existing.From != item.From || existing.To != item.To || existing.Type != type)
                    {
                        if (!replace)
                            throw new GraphException(GraphException.Exists, $"the edge {existing.Id} exists, of type {existing.Type} from {existing.From} to {existing.To}");
                        // Its ends or its type never change: the edge is made again, with the same id.
                        batch.DeleteEdge(existing);
                        batch.PutEdge(NewEdge(batch, existing.Id, type, item.From, item.To, item.Properties));
                        edgesUpdated++;
                        continue;
                    }
                    if (ReferenceEquals(batch.PutEdgeIfChanged(existing, existing.With(JsonValues.Merge(existing.Properties, item.Properties, replace, null), batch.Now)), existing))
                        unchanged++;
                    else
                        edgesUpdated++;
                    continue;
                }
                batch.PutEdge(NewEdge(batch, item.Id ?? NewId(), type, item.From, item.To, item.Properties));
                edgesCreated++;
            }
            catch (GraphException ex)
            {
                throw new GraphException(ex.Code, $"edges[{i}]: {ex.Message}");
            }
        }
        return new ImportCounts(nodesCreated, nodesUpdated, edgesCreated, edgesUpdated, unchanged);
    });

    // ---------------------------------------------------------------- private

    private T Read<T>(Func<T> read)
    {
        _lock.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return read();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private T Write<T>(Func<Batch, T> build)
    {
        T result;
        List<GraphChange> changes;
        _lock.EnterWriteLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var batch = new Batch(this);
            result = build(batch);
            if (batch.Ops.Count > 0)
            {
                Persist(batch.Ops);
                foreach (var op in batch.Ops)
                    Apply(op);
            }
            changes = batch.Ops.Select(op => new GraphChange { Kind = op.Kind, Node = op.Node, Edge = op.Edge, At = batch.Now }).ToList();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        if (changes.Count > 0)
            Changed?.Invoke(changes);
        return result;
    }

    private IEnumerable<GraphNode> MatchNodes(NodeQuery query)
    {
        IEnumerable<GraphNode> nodes = query.Ids is { } ids
            ? ids.Distinct(StringComparer.Ordinal).Select(id => _nodes.GetValueOrDefault(id)).OfType<GraphNode>()
            : query.Label is { } label
                ? _byLabel.TryGetValue(label, out var set) ? set.Select(id => _nodes[id]) : []
                : _nodes.Values;
        if (query.Ids is not null && query.Label is { } only)
            nodes = nodes.Where(n => n.HasLabel(only));
        if (query.Where is { Count: > 0 } where)
            nodes = nodes.Where(n => where.All(c => c.HoldsFor(n.Properties)));
        if (query.Search?.Trim() is { Length: > 0 } text)
            nodes = nodes.Where(n => Contains(n, text));
        if (query.Match?.Trim() is { Length: > 0 } match)
        {
            var expression = SearchExpression.Parse(match);
            nodes = nodes.Where(n => expression.Matches(t => Contains(n, t)));
        }
        return nodes;
    }

    /// <summary>True when a node holds the text (case ignored) in its id, a label or a string of its properties.</summary>
    private static bool Contains(GraphNode node, string text) =>
        node.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
        || node.Labels.Any(l => l.Contains(text, StringComparison.OrdinalIgnoreCase))
        || JsonValues.ContainsText(node.Properties, text);

    private IEnumerable<GraphEdge> MatchEdges(EdgeQuery query)
    {
        IEnumerable<GraphEdge> edges = query.Ids is { } ids
            ? ids.Distinct(StringComparer.Ordinal).Select(id => _edges.GetValueOrDefault(id)).OfType<GraphEdge>()
            : query.From is { } from ? Ids(_out, from).Select(id => _edges[id])
            : query.To is { } to ? Ids(_in, to).Select(id => _edges[id])
            : query.Type is { } type ? Ids(_byType, type).Select(id => _edges[id])
            : _edges.Values;
        if (query.Type is { } t)
            edges = edges.Where(e => e.Type == t);
        if (query.From is { } f)
            edges = edges.Where(e => e.From == f);
        if (query.To is { } o)
            edges = edges.Where(e => e.To == o);
        if (query.Where is { Count: > 0 } where)
            edges = edges.Where(e => where.All(c => c.HoldsFor(e.Properties)));
        return edges;
    }

    /// <summary>The edges of a node in a direction (a loop once), of the types given (any when none), oldest first.</summary>
    private IEnumerable<GraphEdge> Adjacent(string nodeId, Direction direction, IReadOnlyCollection<string>? types)
    {
        if (!_nodes.ContainsKey(nodeId))
            throw NodeNotFound(nodeId);
        var ids = direction switch
        {
            Direction.Out => Ids(_out, nodeId),
            Direction.In => Ids(_in, nodeId),
            _ => Ids(_out, nodeId).Concat(Ids(_in, nodeId)).Distinct(StringComparer.Ordinal)
        };
        return ids.Select(id => _edges[id])
            .Where(e => types is not { Count: > 0 } || types.Contains(e.Type))
            .OrderBy(e => e.Created).ThenBy(e => e.Id, StringComparer.Ordinal);
    }

    private static IEnumerable<string> Ids(Dictionary<string, HashSet<string>> index, string key) =>
        index.TryGetValue(key, out var set) ? set : [];

    private GraphPath Walk(string to, Dictionary<string, GraphEdge?> reachedBy)
    {
        var nodes = new List<GraphNode> { _nodes[to] };
        var edges = new List<GraphEdge>();
        var current = to;
        while (reachedBy[current] is { } edge)
        {
            edges.Add(edge);
            current = edge.Other(current);
            nodes.Add(_nodes[current]);
        }
        nodes.Reverse();
        edges.Reverse();
        return new GraphPath(nodes, edges);
    }

    /// <summary>
    /// Sorted by <paramref name="orderBy"/>: <c>id</c>, <c>created</c>, <c>updated</c>, or the path of a property (the
    /// items without it last, whatever the direction); by default by creation, or as given (<paramref name="keepOrder"/>).
    /// </summary>
    private static IEnumerable<T> Sort<T>(List<T> items, string? orderBy, bool descending, bool keepOrder,
        Func<T, string> id, Func<T, DateTimeOffset> created, Func<T, DateTimeOffset> updated, Func<T, JsonElement> properties)
    {
        if (string.IsNullOrWhiteSpace(orderBy) && keepOrder)
            return descending ? Enumerable.Reverse(items) : items;
        Comparison<T> compare = orderBy?.Trim() switch
        {
            null or "" or "created" => (a, b) => created(a).CompareTo(created(b)),
            "updated" => (a, b) => updated(a).CompareTo(updated(b)),
            "id" => (a, b) => string.CompareOrdinal(id(a), id(b)),
            var path => (a, b) =>
            {
                var hasA = JsonValues.TryGet(properties(a), path, out var x) && x.ValueKind != JsonValueKind.Null;
                var hasB = JsonValues.TryGet(properties(b), path, out var y) && y.ValueKind != JsonValueKind.Null;
                if (!hasA || !hasB)
                    return hasA == hasB ? 0 : hasA ? (descending ? 1 : -1) : (descending ? -1 : 1); // missing last
                return JsonValues.Compare(x, y) ?? Rank(x).CompareTo(Rank(y));
            }
        };
        var sorted = items.ToList();
        sorted.Sort((a, b) =>
        {
            var order = compare(a, b);
            if (order == 0)
                order = string.CompareOrdinal(id(a), id(b));
            return descending ? -order : order;
        });
        return sorted;
    }

    private static int Rank(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => 0,
        JsonValueKind.String => 1,
        JsonValueKind.True or JsonValueKind.False => 2,
        JsonValueKind.Array => 3,
        _ => 4
    };

    private static List<NameCount> Counts(Dictionary<string, HashSet<string>> index) =>
        index.Where(e => e.Value.Count > 0).Select(e => new NameCount(e.Key, e.Value.Count))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();

    private void Delete(Batch batch, GraphNode node, bool detach)
    {
        var edges = Ids(_out, node.Id).Concat(Ids(_in, node.Id)).Distinct(StringComparer.Ordinal)
            .Select(batch.Edge).OfType<GraphEdge>().ToList();
        if (edges.Count > 0 && !detach)
            throw new GraphException(GraphException.HasEdges, $"the node {node.Id} has {edges.Count} edge(s): delete them first, or detach: true");
        foreach (var edge in edges)
            batch.DeleteEdge(edge);
        batch.DeleteNode(node);
    }

    private static GraphEdge NewEdge(Batch batch, string id, string type, string from, string to, JsonElement? properties)
    {
        type = CheckName(type, "type");
        if (batch.Node(from) is null)
            throw new GraphException(GraphException.NotFound, $"the node {from} (from) does not exist");
        if (batch.Node(to) is null)
            throw new GraphException(GraphException.NotFound, $"the node {to} (to) does not exist");
        return new GraphEdge { Id = id, Type = type, From = from, To = to, Properties = JsonValues.Properties(properties), Created = batch.Now, Updated = batch.Now };
    }

    private static GraphNode WithChange(GraphNode node, NodeChange change, DateTimeOffset now)
    {
        var labels = (change.Labels is not null ? CheckLabels(change.Labels) : node.Labels)
            .Concat(CheckLabels(change.AddLabels))
            .Except(change.RemoveLabels ?? [], StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal).ToList();
        return new GraphNode
        {
            Id = node.Id, Labels = labels, Created = node.Created, Updated = now,
            Properties = JsonValues.Merge(node.Properties, change.Properties, change.ReplaceProperties, change.RemoveProperties)
        };
    }

    private static GraphException NodeNotFound(string id) => new(GraphException.NotFound, $"the node {id} does not exist");

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>An id, a label, a type, a key: not empty, no control character, 256 characters at most.</summary>
    private static string CheckName(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new GraphException(GraphException.Invalid, $"{what} cannot be empty");
        if (value.Length > MaxNameLength || value.Any(char.IsControl))
            throw new GraphException(GraphException.Invalid, $"{what} [{value[..Math.Min(40, value.Length)]}…] is not valid: {MaxNameLength} characters at most, no control character");
        return value;
    }

    private static IReadOnlyList<string> CheckLabels(IReadOnlyList<string>? labels) =>
        (labels ?? []).Select(l => CheckName(l?.Trim(), "label")).Distinct(StringComparer.Ordinal).ToList();

    private static DateTimeOffset Date(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private void Execute(string sql)
    {
        using var command = _db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void Persist(List<Op> ops)
    {
        using var transaction = _db.BeginTransaction();
        SqliteCommand? putNode = null, deleteNode = null, putEdge = null, deleteEdge = null;
        try
        {
            foreach (var op in ops)
            {
                switch (op.Kind)
                {
                    case GraphChangeKind.NodeCreated or GraphChangeKind.NodeUpdated:
                        putNode ??= Command(transaction, """
                            INSERT INTO nodes(id, labels, properties, created, updated) VALUES ($id, $labels, $properties, $created, $updated)
                            ON CONFLICT(id) DO UPDATE SET labels = excluded.labels, properties = excluded.properties, updated = excluded.updated
                            """, "$id", "$labels", "$properties", "$created", "$updated");
                        Run(putNode, op.Node!.Id, JsonSerializer.Serialize(op.Node.Labels), op.Node.Properties.GetRawText(), Text(op.Node.Created), Text(op.Node.Updated));
                        break;
                    case GraphChangeKind.NodeDeleted:
                        deleteNode ??= Command(transaction, "DELETE FROM nodes WHERE id = $id", "$id");
                        Run(deleteNode, op.Node!.Id);
                        break;
                    case GraphChangeKind.EdgeCreated or GraphChangeKind.EdgeUpdated:
                        putEdge ??= Command(transaction, """
                            INSERT INTO edges(id, type, source, target, properties, created, updated) VALUES ($id, $type, $source, $target, $properties, $created, $updated)
                            ON CONFLICT(id) DO UPDATE SET properties = excluded.properties, updated = excluded.updated
                            """, "$id", "$type", "$source", "$target", "$properties", "$created", "$updated");
                        var edge = op.Edge!;
                        Run(putEdge, edge.Id, edge.Type, edge.From, edge.To, edge.Properties.GetRawText(), Text(edge.Created), Text(edge.Updated));
                        break;
                    case GraphChangeKind.EdgeDeleted:
                        deleteEdge ??= Command(transaction, "DELETE FROM edges WHERE id = $id", "$id");
                        Run(deleteEdge, op.Edge!.Id);
                        break;
                }
            }
            transaction.Commit();
        }
        finally
        {
            putNode?.Dispose();
            deleteNode?.Dispose();
            putEdge?.Dispose();
            deleteEdge?.Dispose();
        }
    }

    private SqliteCommand Command(SqliteTransaction transaction, string sql, params string[] names)
    {
        var command = _db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in names)
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        command.Prepare();
        return command;
    }

    private static void Run(SqliteCommand command, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
            command.Parameters[i].Value = values[i];
        command.ExecuteNonQuery();
    }

    private static string Text(DateTimeOffset date) => date.ToString("O", CultureInfo.InvariantCulture);

    private void Apply(Op op)
    {
        switch (op.Kind)
        {
            case GraphChangeKind.NodeCreated or GraphChangeKind.NodeUpdated:
                if (_nodes.GetValueOrDefault(op.Node!.Id) is { } before)
                    foreach (var label in before.Labels)
                        Remove(_byLabel, label, before.Id);
                AddNode(op.Node);
                break;
            case GraphChangeKind.NodeDeleted:
                foreach (var label in op.Node!.Labels)
                    Remove(_byLabel, label, op.Node.Id);
                _nodes.Remove(op.Node.Id);
                _out.Remove(op.Node.Id);
                _in.Remove(op.Node.Id);
                break;
            case GraphChangeKind.EdgeCreated:
                AddEdge(op.Edge!);
                break;
            case GraphChangeKind.EdgeUpdated:
                _edges[op.Edge!.Id] = op.Edge;
                break;
            case GraphChangeKind.EdgeDeleted:
                _edges.Remove(op.Edge!.Id);
                Remove(_out, op.Edge.From, op.Edge.Id);
                Remove(_in, op.Edge.To, op.Edge.Id);
                Remove(_byType, op.Edge.Type, op.Edge.Id);
                break;
        }
    }

    private void AddNode(GraphNode node)
    {
        _nodes[node.Id] = node;
        foreach (var label in node.Labels)
            Add(_byLabel, label, node.Id);
    }

    private void AddEdge(GraphEdge edge)
    {
        _edges[edge.Id] = edge;
        Add(_out, edge.From, edge.Id);
        Add(_in, edge.To, edge.Id);
        Add(_byType, edge.Type, edge.Id);
    }

    private static void Add(Dictionary<string, HashSet<string>> index, string key, string id)
    {
        if (!index.TryGetValue(key, out var set))
            index[key] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(id);
    }

    private static void Remove(Dictionary<string, HashSet<string>> index, string key, string id)
    {
        if (index.TryGetValue(key, out var set) && set.Remove(id) && set.Count == 0)
            index.Remove(key);
    }

    public void Dispose()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            _db.Dispose();
            _dataLock.Dispose();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // ------------------------------------------------------------------ batch

    private sealed record Op(GraphChangeKind Kind, GraphNode? Node, GraphEdge? Edge);

    /// <summary>
    /// The changes of one write, before they are written: what it reads sees them (an import makes nodes, then edges
    /// to them). A staged null is a deletion.
    /// </summary>
    private sealed class Batch(GraphStore store)
    {
        private readonly Dictionary<string, GraphNode?> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphEdge?> _edges = new(StringComparer.Ordinal);
        private readonly List<string> _newEdges = new();

        public List<Op> Ops { get; } = new();
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

        public GraphNode? Node(string id) => _nodes.TryGetValue(id, out var staged) ? staged : store._nodes.GetValueOrDefault(id);
        public GraphEdge? Edge(string id) => _edges.TryGetValue(id, out var staged) ? staged : store._edges.GetValueOrDefault(id);

        public void PutNode(GraphNode node)
        {
            Ops.Add(new Op(Node(node.Id) is null ? GraphChangeKind.NodeCreated : GraphChangeKind.NodeUpdated, node, null));
            _nodes[node.Id] = node;
        }

        /// <summary>Writes <paramref name="after"/> when it differs from <paramref name="before"/>; the node as it is then.</summary>
        public GraphNode PutNodeIfChanged(GraphNode before, GraphNode after)
        {
            if (before.Labels.SequenceEqual(after.Labels, StringComparer.Ordinal) && JsonValues.DeepEquals(before.Properties, after.Properties))
                return before;
            PutNode(after);
            return after;
        }

        public void DeleteNode(GraphNode node)
        {
            Ops.Add(new Op(GraphChangeKind.NodeDeleted, node, null));
            _nodes[node.Id] = null;
        }

        public void PutEdge(GraphEdge edge)
        {
            var created = Edge(edge.Id) is null;
            Ops.Add(new Op(created ? GraphChangeKind.EdgeCreated : GraphChangeKind.EdgeUpdated, null, edge));
            if (created)
                _newEdges.Add(edge.Id);
            _edges[edge.Id] = edge;
        }

        public GraphEdge PutEdgeIfChanged(GraphEdge before, GraphEdge after)
        {
            if (JsonValues.DeepEquals(before.Properties, after.Properties))
                return before;
            PutEdge(after);
            return after;
        }

        public void DeleteEdge(GraphEdge edge)
        {
            Ops.Add(new Op(GraphChangeKind.EdgeDeleted, null, edge));
            _edges[edge.Id] = null;
        }

        /// <summary>The edge of <paramref name="type"/> from a node to another, made before or in this write.</summary>
        public GraphEdge? FindEdge(string from, string to, string type) =>
            Ids(store._out, from).Concat(_newEdges).Select(Edge).OfType<GraphEdge>()
                .Where(e => e.From == from && e.To == to && e.Type == type)
                .OrderBy(e => e.Created).ThenBy(e => e.Id, StringComparer.Ordinal)
                .FirstOrDefault();
    }
}

internal static class GraphEdgeChanges
{
    /// <summary>The same edge with other properties.</summary>
    public static GraphEdge With(this GraphEdge edge, JsonElement properties, DateTimeOffset now) => new()
    {
        Id = edge.Id, Type = edge.Type, From = edge.From, To = edge.To, Properties = properties, Created = edge.Created, Updated = now
    };
}
