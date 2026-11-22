using HotChocolate;
using HotChocolate.Types;
using KAONOMI.graphbase.Configuration;
using KAONOMI.graphbase.Storage;

namespace KAONOMI.graphbase.GraphQL;

/// <summary>When the server started (<c>stats</c>).</summary>
public sealed record ServerInfo(DateTimeOffset Started);

public sealed class Query
{
    [GraphQLDescription("The node of this id, or null.")]
    public GraphNode? GetNode([Service] GraphStore store, [GraphQLType(typeof(NonNullType<IdType>))] string id) => store.GetNode(id);

    /// <summary>The description of the argument match (nodes, nodeCount, deleteNodes).</summary>
    internal const string MatchDescription = """
        Texts found as search finds them, joined by AND and OR (any case) and grouped by ( ): kaonomi AND (agent OR mcp).
        Words side by side are one text, as written; "a text in quotes" may hold AND, OR and parentheses. AND binds first.
        """;

    [GraphQLDescription("""
        Nodes: of these ids, of a label, holding conditions on their properties, containing a text (case ignored) in their
        id, labels or string properties (search), or texts joined by AND, OR and ( ) (match). Ordered by orderBy ("id",
        "created", "updated", "degree": the number of its edges, or the path of a property; by default by creation, or in
        the order of ids), then the page: skip, first.
        """)]
    public IReadOnlyList<GraphNode> GetNodes(
        [Service] GraphStore store, [Service] GraphSettings settings,
        [GraphQLType(typeof(ListType<NonNullType<IdType>>))] IReadOnlyList<string>? ids = null,
        string? label = null,
        IReadOnlyList<PropertyFilterInput>? where = null,
        string? search = null,
        [GraphQLDescription(MatchDescription)] string? match = null,
        string? orderBy = null,
        bool descending = false,
        int first = 100,
        int skip = 0) =>
        store.FindNodes(new NodeQuery
        {
            Ids = ids, Label = label, Where = PropertyFilterInput.Conditions(where), Search = search, Match = match,
            OrderBy = orderBy, Descending = descending, First = Math.Min(first, settings.MaxResults), Skip = skip
        });

    [GraphQLDescription("How many nodes the same arguments as nodes find (all of them, not a page).")]
    public int GetNodeCount(
        [Service] GraphStore store,
        string? label = null,
        IReadOnlyList<PropertyFilterInput>? where = null,
        string? search = null,
        [GraphQLDescription(MatchDescription)] string? match = null) =>
        store.CountNodes(new NodeQuery { Label = label, Where = PropertyFilterInput.Conditions(where), Search = search, Match = match });

    [GraphQLDescription("The edge of this id, or null.")]
    public GraphEdge? GetEdge([Service] GraphStore store, [GraphQLType(typeof(NonNullType<IdType>))] string id) => store.GetEdge(id);

    [GraphQLDescription("Edges: of these ids, of a type, from a node, to a node, holding conditions on their properties; ordered as nodes.")]
    public IReadOnlyList<GraphEdge> GetEdges(
        [Service] GraphStore store, [Service] GraphSettings settings,
        [GraphQLType(typeof(ListType<NonNullType<IdType>>))] IReadOnlyList<string>? ids = null,
        string? type = null,
        [GraphQLType(typeof(IdType))] string? from = null,
        [GraphQLType(typeof(IdType))] string? to = null,
        IReadOnlyList<PropertyFilterInput>? where = null,
        string? orderBy = null,
        bool descending = false,
        int first = 100,
        int skip = 0) =>
        store.FindEdges(new EdgeQuery
        {
            Ids = ids, Type = type, From = from, To = to, Where = PropertyFilterInput.Conditions(where),
            OrderBy = orderBy, Descending = descending, First = Math.Min(first, settings.MaxResults), Skip = skip
        });

    [GraphQLDescription("How many edges the same arguments as edges find.")]
    public int GetEdgeCount(
        [Service] GraphStore store,
        string? type = null,
        [GraphQLType(typeof(IdType))] string? from = null,
        [GraphQLType(typeof(IdType))] string? to = null,
        IReadOnlyList<PropertyFilterInput>? where = null) =>
        store.CountEdges(new EdgeQuery { Type = type, From = from, To = to, Where = PropertyFilterInput.Conditions(where) });

    [GraphQLDescription("""
        The shortest path from a node to another (the fewest edges), following the edges in direction, of these types
        (any when none), maxDepth edges at most (graphql.max_hops at most); null when there is none.
        """)]
    public GraphPath? GetShortestPath(
        [Service] GraphStore store, [Service] GraphSettings settings,
        [GraphQLType(typeof(NonNullType<IdType>))] string from,
        [GraphQLType(typeof(NonNullType<IdType>))] string to,
        Direction direction = Direction.Both,
        IReadOnlyList<string>? types = null,
        int maxDepth = 6) =>
        store.ShortestPath(from, to, direction, types, Math.Clamp(maxDepth, 1, settings.MaxHops));

    [GraphQLDescription("""
        The nodes within depth edges of the nodes of ids (following direction and these types), limit nodes at most, and
        every edge of these types between them: the neighbourhood to draw around nodes.
        """)]
    public Subgraph GetSubgraph(
        [Service] GraphStore store, [Service] GraphSettings settings,
        [GraphQLType(typeof(NonNullType<ListType<NonNullType<IdType>>>))] IReadOnlyList<string> ids,
        int depth = 1,
        Direction direction = Direction.Both,
        IReadOnlyList<string>? types = null,
        int limit = 1000) =>
        store.Neighborhood(ids, Math.Clamp(depth, 0, settings.MaxHops), direction, types, Math.Clamp(limit, 1, settings.MaxResults));

    [GraphQLDescription("The labels of the nodes, the most used first.")]
    public IReadOnlyList<NameCount> GetLabels([Service] GraphStore store) => store.Labels();

    [GraphQLDescription("The types of the edges, the most used first.")]
    public IReadOnlyList<NameCount> GetEdgeTypes([Service] GraphStore store) => store.EdgeTypes();

    [GraphQLDescription("What the server is and holds.")]
    public GraphStats GetStats([Service] GraphStore store, [Service] ServerInfo info) =>
        new(GraphSettings.Version, info.Started, store.NodeCount, store.EdgeCount, store.Labels(), store.EdgeTypes());
}

[ExtendObjectType("Node")]
public sealed class NodeFields
{
    [GraphQLDescription("The value of a property (its path: \"name\", \"address.city\"), or null.")]
    [GraphQLType(typeof(JsonType))]
    public System.Text.Json.JsonElement? GetProperty([Parent] GraphNode node, string name) =>
        JsonValues.TryGet(node.Properties, name, out var value) ? value : null;

    [GraphQLDescription("Its edges (oldest first): going out, coming in or both, of a type or of any.")]
    public IReadOnlyList<GraphEdge> GetEdges(
        [Parent] GraphNode node, [Service] GraphStore store, [Service] GraphSettings settings,
        Direction direction = Direction.Both, string? type = null, int first = 100) =>
        store.EdgesOf(node.Id, direction, type, Math.Min(first, settings.MaxResults));

    [GraphQLDescription("The nodes at the other end of its edges, each once: of a type of edge, of a label, or any.")]
    public IReadOnlyList<GraphNode> GetNeighbors(
        [Parent] GraphNode node, [Service] GraphStore store, [Service] GraphSettings settings,
        Direction direction = Direction.Both, string? type = null, string? label = null, int first = 100) =>
        store.NeighborsOf(node.Id, direction, type, label, Math.Min(first, settings.MaxResults));

    [GraphQLDescription("How many edges it has (in direction, of a type or of any).")]
    public int GetDegree([Parent] GraphNode node, [Service] GraphStore store, Direction direction = Direction.Both, string? type = null) =>
        store.Degree(node.Id, direction, type);
}

[ExtendObjectType("Edge")]
public sealed class EdgeFields
{
    [GraphQLDescription("The node it starts from.")]
    public GraphNode? GetFrom([Parent] GraphEdge edge, [Service] GraphStore store) => store.GetNode(edge.From);

    [GraphQLDescription("The node it goes to.")]
    public GraphNode? GetTo([Parent] GraphEdge edge, [Service] GraphStore store) => store.GetNode(edge.To);

    [GraphQLDescription("The value of a property (its path), or null.")]
    [GraphQLType(typeof(JsonType))]
    public System.Text.Json.JsonElement? GetProperty([Parent] GraphEdge edge, string name) =>
        JsonValues.TryGet(edge.Properties, name, out var value) ? value : null;
}
