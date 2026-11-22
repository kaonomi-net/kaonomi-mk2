using System.Text.Json;

namespace KAONOMI.graphbase.Storage;

/// <summary>Which nodes: by ids, by label, by conditions on their properties, by a text; in what order, which page.</summary>
public sealed record NodeQuery
{
    public IReadOnlyList<string>? Ids { get; init; }
    public string? Label { get; init; }
    public IReadOnlyList<PropertyCondition>? Where { get; init; }
    /// <summary>A text found (case ignored) in the id, a label or a string of the properties.</summary>
    public string? Search { get; init; }
    /// <summary>Texts found as <see cref="Search"/> finds them, joined by AND, OR and parentheses (<see cref="SearchExpression"/>).</summary>
    public string? Match { get; init; }
    /// <summary><c>id</c>, <c>created</c>, <c>updated</c>, <c>degree</c> (its number of edges) or the path of a property; null: by creation (the order of <see cref="Ids"/> when given).</summary>
    public string? OrderBy { get; init; }
    public bool Descending { get; init; }
    public int Skip { get; init; }
    public int First { get; init; } = 100;
}

/// <summary>Which edges: by ids, type, ends, conditions on their properties; in what order, which page.</summary>
public sealed record EdgeQuery
{
    public IReadOnlyList<string>? Ids { get; init; }
    public string? Type { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public IReadOnlyList<PropertyCondition>? Where { get; init; }
    public string? OrderBy { get; init; }
    public bool Descending { get; init; }
    public int Skip { get; init; }
    public int First { get; init; } = 100;
}

/// <summary>A change of a node: its properties (merged, or replaced), properties removed, its labels.</summary>
public sealed record NodeChange
{
    public JsonElement? Properties { get; init; }
    public bool ReplaceProperties { get; init; }
    public IReadOnlyList<string>? RemoveProperties { get; init; }
    /// <summary>All its labels, in place of the ones it has.</summary>
    public IReadOnlyList<string>? Labels { get; init; }
    public IReadOnlyList<string>? AddLabels { get; init; }
    public IReadOnlyList<string>? RemoveLabels { get; init; }
}

/// <summary>A node of an import: the node of that id is completed (labels added, properties merged), else made.</summary>
public sealed record NodeWrite(string? Id, IReadOnlyList<string>? Labels, JsonElement? Properties);

/// <summary>An edge of an import: the edge of that id is completed, else made (or, without id, the one of the same type between the same nodes).</summary>
public sealed record EdgeWrite(string? Id, string Type, string From, string To, JsonElement? Properties);

public sealed record ImportCounts(int NodesCreated, int NodesUpdated, int EdgesCreated, int EdgesUpdated, int Unchanged);

/// <summary>A path of the graph: its nodes from the first to the last, and the edges between them.</summary>
public sealed record GraphPath(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges)
{
    public int Length => Edges.Count;
}

/// <summary>Nodes around others and the edges between them; <see cref="Truncated"/> when the limit stopped it.</summary>
public sealed record Subgraph(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, bool Truncated);

public sealed record NameCount(string Name, int Count);
