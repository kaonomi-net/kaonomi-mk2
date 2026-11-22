using HotChocolate.Types;
using KAONOMI.graphbase.Storage;

namespace KAONOMI.graphbase.GraphQL;

public sealed class NodeType : ObjectType<GraphNode>
{
    protected override void Configure(IObjectTypeDescriptor<GraphNode> descriptor)
    {
        descriptor.Name("Node").Description("A node of the graph: what it is (its labels) and what is known of it (its properties).");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(n => n.Id).Type<NonNullType<IdType>>();
        descriptor.Field(n => n.Labels).Description("What the node is: Person, Organization, Place…");
        descriptor.Field(n => n.Properties).Type<NonNullType<JsonType>>().Description("Its properties, a JSON object.");
        descriptor.Field(n => n.Created);
        descriptor.Field(n => n.Updated);
    }
}

public sealed class EdgeType : ObjectType<GraphEdge>
{
    protected override void Configure(IObjectTypeDescriptor<GraphEdge> descriptor)
    {
        descriptor.Name("Edge").Description("A relation of a type from a node to another, with its properties.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(e => e.Id).Type<NonNullType<IdType>>();
        descriptor.Field(e => e.Type).Description("What the relation is: KNOWS, WORKS_FOR, LOCATED_IN…");
        descriptor.Field(e => e.From).Name("fromId").Type<NonNullType<IdType>>().Description("The id of the node it starts from.");
        descriptor.Field(e => e.To).Name("toId").Type<NonNullType<IdType>>().Description("The id of the node it goes to.");
        descriptor.Field(e => e.Properties).Type<NonNullType<JsonType>>().Description("Its properties, a JSON object.");
        descriptor.Field(e => e.Created);
        descriptor.Field(e => e.Updated);
    }
}

public sealed class PathType : ObjectType<GraphPath>
{
    protected override void Configure(IObjectTypeDescriptor<GraphPath> descriptor)
    {
        descriptor.Name("Path").Description("A path: its nodes from the first to the last, the edges between them.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(p => p.Nodes);
        descriptor.Field(p => p.Edges);
        descriptor.Field(p => p.Length).Description("The number of edges.");
    }
}

public sealed class SubgraphType : ObjectType<Subgraph>
{
    protected override void Configure(IObjectTypeDescriptor<Subgraph> descriptor)
    {
        descriptor.Name("Subgraph").Description("Nodes and the edges between them.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(s => s.Nodes);
        descriptor.Field(s => s.Edges);
        descriptor.Field(s => s.Truncated).Description("True when the limit stopped the search: there are more nodes around.");
    }
}

public sealed class NameCountType : ObjectType<NameCount>
{
    protected override void Configure(IObjectTypeDescriptor<NameCount> descriptor)
    {
        descriptor.Name("NameCount").Description("A label or a type of edge, and how many use it.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Name);
        descriptor.Field(c => c.Count);
    }
}

public sealed class ImportResultType : ObjectType<ImportCounts>
{
    protected override void Configure(IObjectTypeDescriptor<ImportCounts> descriptor)
    {
        descriptor.Name("ImportResult").Description("What an import did.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.NodesCreated);
        descriptor.Field(c => c.NodesUpdated);
        descriptor.Field(c => c.EdgesCreated);
        descriptor.Field(c => c.EdgesUpdated);
        descriptor.Field(c => c.Unchanged).Description("Nodes and edges given that were already as given.");
    }
}

public sealed class GraphChangeType : ObjectType<GraphChange>
{
    protected override void Configure(IObjectTypeDescriptor<GraphChange> descriptor)
    {
        descriptor.Name("GraphChange").Description("A change of the graph.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Kind);
        descriptor.Field(c => c.Node).Description("The node created, changed or deleted (its last state when deleted).");
        descriptor.Field(c => c.Edge).Description("The edge created, changed or deleted (its last state when deleted).");
        descriptor.Field(c => c.At);
    }
}

/// <summary>What the server is and holds (<c>stats</c>).</summary>
public sealed record GraphStats(string Version, DateTimeOffset Started, int Nodes, int Edges, IReadOnlyList<NameCount> Labels, IReadOnlyList<NameCount> EdgeTypes);

public sealed class StatsType : ObjectType<GraphStats>
{
    protected override void Configure(IObjectTypeDescriptor<GraphStats> descriptor)
    {
        descriptor.Name("Stats").Description("What the server is and holds.");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(s => s.Version);
        descriptor.Field(s => s.Started);
        descriptor.Field(s => s.Nodes);
        descriptor.Field(s => s.Edges);
        descriptor.Field(s => s.Labels);
        descriptor.Field(s => s.EdgeTypes);
    }
}
