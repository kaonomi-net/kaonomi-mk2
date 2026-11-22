using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using KAONOMI.graphbase.Storage;

namespace KAONOMI.graphbase.GraphQL;

public sealed class Mutation
{
    [GraphQLDescription("Makes a node: its id (none: a new one), its labels, its properties (a JSON object).")]
    public GraphNode CreateNode(
        [Service] GraphStore store,
        [GraphQLType(typeof(IdType))] string? id = null,
        IReadOnlyList<string>? labels = null,
        [GraphQLType(typeof(JsonType))] JsonElement? properties = null) =>
        store.CreateNode(id, labels, properties);

    [GraphQLDescription("""
        Changes a node: its properties merged (a null value removes its property), or in place of the others when
        replaceProperties; removeProperties taken away; its labels replaced (labels), added (addLabels), removed
        (removeLabels). A node already as asked is not written again.
        """)]
    public GraphNode UpdateNode(
        [Service] GraphStore store,
        [GraphQLType(typeof(NonNullType<IdType>))] string id,
        [GraphQLType(typeof(JsonType))] JsonElement? properties = null,
        bool replaceProperties = false,
        IReadOnlyList<string>? removeProperties = null,
        IReadOnlyList<string>? labels = null,
        IReadOnlyList<string>? addLabels = null,
        IReadOnlyList<string>? removeLabels = null) =>
        store.UpdateNode(id, new NodeChange
        {
            Properties = properties, ReplaceProperties = replaceProperties, RemoveProperties = removeProperties,
            Labels = labels, AddLabels = addLabels, RemoveLabels = removeLabels
        });

    [GraphQLDescription("""
        The node of label whose property key is value, completed with labels and properties; made when there is none
        (as MERGE of Cypher): writing the same entity twice gives one node.
        """)]
    public GraphNode MergeNode(
        [Service] GraphStore store,
        string label,
        string key,
        [GraphQLType(typeof(NonNullType<JsonType>))] JsonElement value,
        IReadOnlyList<string>? labels = null,
        [GraphQLType(typeof(JsonType))] JsonElement? properties = null) =>
        store.MergeNode(label, key, value, labels, properties);

    [GraphQLDescription("Deletes a node, and its edges when detach (else it must have none). False: there was no such node.")]
    public bool DeleteNode([Service] GraphStore store, [GraphQLType(typeof(NonNullType<IdType>))] string id, bool detach = true) =>
        store.DeleteNode(id, detach);

    [GraphQLDescription("Deletes every node found (as nodes finds them, not a page) and their edges; how many. One criterion at least.")]
    public int DeleteNodes(
        [Service] GraphStore store,
        [GraphQLType(typeof(ListType<NonNullType<IdType>>))] IReadOnlyList<string>? ids = null,
        string? label = null,
        IReadOnlyList<PropertyFilterInput>? where = null,
        string? search = null,
        [GraphQLDescription(Query.MatchDescription)] string? match = null) =>
        store.DeleteNodes(new NodeQuery { Ids = ids, Label = label, Where = PropertyFilterInput.Conditions(where), Search = search, Match = match });

    [GraphQLDescription("Makes an edge of a type from a node to another, with its properties.")]
    public GraphEdge CreateEdge(
        [Service] GraphStore store,
        string type,
        [GraphQLType(typeof(NonNullType<IdType>))] string from,
        [GraphQLType(typeof(NonNullType<IdType>))] string to,
        [GraphQLType(typeof(IdType))] string? id = null,
        [GraphQLType(typeof(JsonType))] JsonElement? properties = null) =>
        store.CreateEdge(id, type, from, to, properties);

    [GraphQLDescription("The edge of this type from a node to another, its properties completed; made when there is none.")]
    public GraphEdge MergeEdge(
        [Service] GraphStore store,
        string type,
        [GraphQLType(typeof(NonNullType<IdType>))] string from,
        [GraphQLType(typeof(NonNullType<IdType>))] string to,
        [GraphQLType(typeof(JsonType))] JsonElement? properties = null) =>
        store.MergeEdge(type, from, to, properties);

    [GraphQLDescription("Changes the properties of an edge (merged, or replaced when replaceProperties), removeProperties taken away.")]
    public GraphEdge UpdateEdge(
        [Service] GraphStore store,
        [GraphQLType(typeof(NonNullType<IdType>))] string id,
        [GraphQLType(typeof(JsonType))] JsonElement? properties = null,
        bool replaceProperties = false,
        IReadOnlyList<string>? removeProperties = null) =>
        store.UpdateEdge(id, properties, replaceProperties, removeProperties);

    [GraphQLDescription("Deletes an edge. False: there was no such edge.")]
    public bool DeleteEdge([Service] GraphStore store, [GraphQLType(typeof(NonNullType<IdType>))] string id) =>
        store.DeleteEdge(id);

    [GraphQLDescription("Deletes the edges of these ids; how many there were.")]
    public int DeleteEdges([Service] GraphStore store, [GraphQLType(typeof(NonNullType<ListType<NonNullType<IdType>>>))] IReadOnlyList<string> ids) =>
        store.DeleteEdges(ids);

    [GraphQLDescription("""
        Writes many nodes, then many edges, all or nothing: a node or an edge whose id exists is completed, else made;
        an edge without id completes the edge of the same type between the same nodes when mergeEdges. The edges can
        go to the nodes of the same import. replace (the import is the source of what it gives): a node or an edge
        that exists becomes the one given, its labels and properties those given only (an edge of other ends or type
        is made again).
        """)]
    public ImportCounts ImportGraph(
        [Service] GraphStore store,
        IReadOnlyList<NodeInput>? nodes = null,
        IReadOnlyList<EdgeInput>? edges = null,
        bool mergeEdges = true,
        bool replace = false) =>
        store.Import((nodes ?? []).Select(n => n.ToWrite()).ToList(), (edges ?? []).Select(e => e.ToWrite()).ToList(), mergeEdges, replace);
}

public sealed class Subscription
{
    [Subscribe]
    [Topic(GraphChange.Topic)]
    [GraphQLDescription("Every change of the graph, as it is written: a node or an edge created, changed, deleted.")]
    public GraphChange GraphChanged([EventMessage] GraphChange change) => change;
}
