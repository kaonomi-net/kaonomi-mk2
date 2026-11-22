using System.Text.Json;

namespace KAONOMI.graphbase.Storage;

/// <summary>
/// A node of the graph: its id, its labels (what it is: <c>Person</c>, <c>Organization</c>…) and its properties (a JSON
/// object). Never changed once made: a change makes a new <see cref="GraphNode"/>, so that it can be read without lock.
/// </summary>
public sealed class GraphNode
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> Labels { get; init; }
    /// <summary>Always a JSON object.</summary>
    public required JsonElement Properties { get; init; }
    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset Updated { get; init; }

    public bool HasLabel(string label) => Labels.Contains(label, StringComparer.Ordinal);
}

/// <summary>
/// An edge of the graph: a relation of a type (<c>KNOWS</c>, <c>WORKS_FOR</c>…) going from one node to another, with
/// its properties (a JSON object). Its ends never change. Never changed once made, as <see cref="GraphNode"/>.
/// </summary>
public sealed class GraphEdge
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    /// <summary>The id of the node it starts from.</summary>
    public required string From { get; init; }
    /// <summary>The id of the node it goes to.</summary>
    public required string To { get; init; }
    /// <summary>Always a JSON object.</summary>
    public required JsonElement Properties { get; init; }
    public required DateTimeOffset Created { get; init; }
    public required DateTimeOffset Updated { get; init; }

    /// <summary>The node at the other end of the edge from <paramref name="nodeId"/>.</summary>
    public string Other(string nodeId) => string.Equals(From, nodeId, StringComparison.Ordinal) ? To : From;
}

/// <summary>Which edges of a node: the ones going out of it, the ones coming in, or both.</summary>
public enum Direction
{
    Out,
    In,
    Both
}

public enum GraphChangeKind
{
    NodeCreated,
    NodeUpdated,
    NodeDeleted,
    EdgeCreated,
    EdgeUpdated,
    EdgeDeleted
}

/// <summary>A change of the graph, sent to the subscriptions (<c>graphChanged</c>).</summary>
public sealed class GraphChange
{
    public const string Topic = "graphChanged";

    public required GraphChangeKind Kind { get; init; }
    /// <summary>The node created, changed or deleted (its last state when deleted).</summary>
    public GraphNode? Node { get; init; }
    /// <summary>The edge created, changed or deleted (its last state when deleted).</summary>
    public GraphEdge? Edge { get; init; }
    public required DateTimeOffset At { get; init; }
}

/// <summary>What a request asks that the graph cannot do: its <see cref="Code"/> is the code of the GraphQL error.</summary>
public sealed class GraphException(string code, string message) : Exception(message)
{
    public const string NotFound = "NOT_FOUND";
    public const string Invalid = "INVALID_ARGUMENT";
    public const string Exists = "ALREADY_EXISTS";
    public const string HasEdges = "NODE_HAS_EDGES";

    public string Code { get; } = code;
}

/// <summary>Another server uses the same <c>path.data</c>.</summary>
public sealed class DataPathInUseException(string message) : Exception(message);
