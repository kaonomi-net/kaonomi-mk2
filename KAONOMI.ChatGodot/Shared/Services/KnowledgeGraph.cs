using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The ontology and the relations of KAONOMI (on the server) in KAONOMI.graphbase, as a graph:
/// <list type="bullet">
/// <item>a node per entity of the ontology, id <c>onto:&lt;id&gt;</c>, labels <c>Ontology</c>, its entity type
/// (<c>PERSON</c>, <c>ORGANISATION</c>, <c>DOMAIN</c>…) and <c>Class</c> for a class of the ontology; its properties
/// <c>name</c>, <c>entityType</c>, <c>summary</c>, <c>description</c>, <c>symbol</c>, <c>synonyms</c>, <c>tags</c>,
/// <c>links</c>, <c>startDate</c>, <c>endDate</c>, <c>confidentiality</c>, <c>emotions</c>, <c>ontologyId</c>,
/// <c>updated</c>;</item>
/// <item>an edge <c>CHILD_OF</c> from an entity to its parent (<c>onto:parent:&lt;id&gt;</c>), and an edge
/// <c>RELATED_TO</c> to each of its related entities, found by their name or a synonym
/// (<c>onto:related:&lt;id&gt;:&lt;other&gt;</c>);</item>
/// <item>an edge per relation of <c>/relation</c> (its last version), from its source to each of its destinations, its
/// nature as the type (<c>est membre de</c> → <c>EST_MEMBRE_DE</c>), id <c>rel:&lt;id&gt;</c>; properties
/// <c>nature</c>, <c>title</c>, <c>description</c>, <c>probability</c>, <c>dateFrom</c>, <c>dateTo</c>,
/// <c>version</c>, the information of its two ends, <c>relationId</c>. An end that is no entity of the ontology is a
/// node <c>RelationEnd</c> (its window, its object).</item>
/// </list>
/// Every node and edge of the ontology has <c>source: "ontology"</c>, of the relations <c>source: "relation"</c>. A whole
/// synchronisation makes the graph what the server holds (what was removed is removed, the rest written as it is); a
/// change made in the chat is written at once (<see cref="WriteOntologyAsync"/>, <see cref="WriteRelationAsync"/>).
/// </summary>
public static class KnowledgeGraph
{
    public const string OntologyLabel = "Ontology";
    public const string ClassLabel = "Class";
    public const string EndLabel = "RelationEnd";
    public const string OntologySource = "ontology";
    public const string RelationSource = "relation";

    private const int NodeBatch = 1000;
    private const int EdgeBatch = 2000;
    private const int Page = 10000;

    /// <summary>What a synchronisation did.</summary>
    public sealed record SyncResult(int Entities, int Relations, int Edges, int Created, int Updated, int Removed, int Unresolved)
    {
        public string Summary => $"{Entities} entities, {Relations} relations ({Edges} edges): {Created} created, {Updated} changed, {Removed} removed"
                                 + (Unresolved > 0 ? $"; {Unresolved} related entities not found by their name" : "");

        public bool Changed => Created + Updated + Removed > 0;
    }

    public static string NodeId(string ontologyId) => "onto:" + ontologyId;

    // ── The mapping ──

    private static object Node(OntologyEntry entry)
    {
        var labels = new List<string> { OntologyLabel };
        if (!string.IsNullOrWhiteSpace(entry.EntityType))
            labels.Add(entry.EntityType.Trim());
        if (entry.IsOntologyClass)
            labels.Add(ClassLabel);
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name.Trim(),
            ["ontologyId"] = entry.Id,
            ["source"] = OntologySource
        };
        Put(properties, "entityType", entry.EntityType);
        Put(properties, "summary", entry.Summary);
        Put(properties, "description", entry.Description);
        Put(properties, "symbol", entry.Symbol);
        List(properties, "synonyms", entry.Synonyms);
        List(properties, "tags", entry.Tags);
        List(properties, "links", entry.RelatedLinks);
        if (entry.StartDate is { } start)
            properties["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (entry.EndDate is { } end)
            properties["endDate"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Put(properties, "confidentiality", entry.Confidentiality);
        if (entry.IsOntologyClass)
            properties["isClass"] = true;
        var emotions = new Dictionary<string, int>
        {
            ["angry"] = entry.AngryLevel ?? 0, ["sad"] = entry.SadLevel ?? 0, ["happy"] = entry.HappyLevel ?? 0,
            ["fear"] = entry.FearLevel ?? 0, ["surprise"] = entry.SurpriseLevel ?? 0, ["disgust"] = entry.DisgustLevel ?? 0
        }.Where(e => e.Value != 0).ToDictionary(e => e.Key, e => e.Value);
        if (emotions.Count > 0)
            properties["emotions"] = emotions;
        if (entry.Date is { } date)
            properties["updated"] = date.ToString("O", CultureInfo.InvariantCulture);
        return new { id = NodeId(entry.Id!), labels, properties };
    }

    /// <summary>The edges of an entity to its parent and to its related entities (<paramref name="resolve"/>: the ids of a name).</summary>
    private static IEnumerable<(string Id, object Edge)> StructureEdges(OntologyEntry entry, Func<string, bool> exists, Func<string, IReadOnlyList<string>> resolve, Action unresolved)
    {
        var id = entry.Id!;
        if (entry.NodeParentId is { Length: > 0 } parent && parent != id && exists(parent))
            yield return ($"onto:parent:{id}", new
            {
                id = $"onto:parent:{id}", type = "CHILD_OF", from = NodeId(id), to = NodeId(parent),
                properties = new Dictionary<string, object?> { ["kind"] = "child of", ["source"] = OntologySource }
            });
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in entry.RelatedEntities ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var targets = resolve(name).Where(t => t != id).Take(3).ToList();
            if (targets.Count == 0)
                unresolved();
            foreach (var target in targets.Where(seen.Add))
            {
                var edgeId = $"onto:related:{id}:{target}";
                yield return (edgeId, new
                {
                    id = edgeId, type = "RELATED_TO", from = NodeId(id), to = NodeId(target),
                    properties = new Dictionary<string, object?> { ["kind"] = "related to", ["name"] = name.Trim(), ["source"] = OntologySource }
                });
            }
        }
    }

    /// <summary>The node of an end of a relation: its entity of the ontology, else a node of its own (written in <paramref name="ends"/>).</summary>
    private static string EndNode(RelationEndEntry end, Func<string, bool> isEntity, Dictionary<string, object> ends)
    {
        if (end.ObjectId is { Length: > 0 } objectId && isEntity(objectId))
            return NodeId(objectId);
        var window = string.IsNullOrWhiteSpace(end.WindowName) ? "Window" : end.WindowName.Trim();
        var key = end.ObjectId is { Length: > 0 } o ? o : (end.Information ?? end.Title ?? "").Trim();
        var id = $"relend:{window}:{(key.Length > 120 ? key[..120] : key)}";
        if (!ends.ContainsKey(id))
            ends[id] = new
            {
                id,
                labels = new[] { EndLabel, ArchiGraph.EdgeType(window) },
                properties = new Dictionary<string, object?>
                {
                    ["name"] = new[] { end.Information, end.Title, end.ObjectId, window }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim(),
                    ["window"] = window, ["objectId"] = end.ObjectId, ["source"] = RelationSource
                }
            };
        return id;
    }

    /// <summary>The edges of a relation (its last version): from its source to each destination.</summary>
    private static IEnumerable<(string Id, object Edge)> RelationEdges(RelationEntry relation, Func<string, bool> isEntity, Dictionary<string, object> ends)
    {
        if (relation.IsDeleted || string.IsNullOrWhiteSpace(relation.Id) || relation.Versions.Count == 0)
            yield break;
        var version = relation.Current;
        var from = EndNode(version.Source, isEntity, ends);
        for (var i = 0; i < version.Destinations.Count; i++)
        {
            var destination = version.Destinations[i];
            var to = EndNode(destination, isEntity, ends);
            var id = i == 0 ? $"rel:{relation.Id}" : $"rel:{relation.Id}:{i}";
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["relationId"] = relation.Id, ["probability"] = version.Probability, ["version"] = version.Version, ["source"] = RelationSource
            };
            Put(properties, "nature", version.Nature);
            if (!string.Equals(version.Title?.Trim(), version.Nature?.Trim(), StringComparison.OrdinalIgnoreCase))
                Put(properties, "title", version.Title);
            Put(properties, "description", version.Description);
            if (version.DateFrom is { } dateFrom)
                properties["dateFrom"] = dateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (version.DateTo is { } dateTo)
                properties["dateTo"] = dateTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Put(properties, "fromInformation", version.Source.Information);
            Put(properties, "toInformation", destination.Information);
            if ((version.Date ?? relation.Date) is { } date)
                properties["date"] = date.ToString("O", CultureInfo.InvariantCulture);
            Put(properties, "userName", version.UserName ?? relation.UserName);
            yield return (id, new { id, type = ArchiGraph.EdgeType(version.Nature ?? version.Title), from, to, properties });
        }
    }

    private static void Put(Dictionary<string, object?> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            properties[key] = value.Trim();
    }

    private static void List(Dictionary<string, object?> properties, string key, IEnumerable<string>? values)
    {
        var list = (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList();
        if (list.Count > 0)
            properties[key] = list;
    }

    // ── The whole synchronisation ──

    /// <summary>
    /// Makes the graph what the server holds: the entities and relations given written as they are, the nodes and edges
    /// of the ontology and of the relations no longer in them removed (not when the lists are cut: <paramref name="complete"/>).
    /// </summary>
    public static async Task<SyncResult> SyncAsync(GraphbaseClient client, IReadOnlyList<OntologyEntry> ontology, IReadOnlyList<RelationEntry> relations, bool complete, CancellationToken ct)
    {
        var entities = ontology.Where(e => !e.IsDeleted && !string.IsNullOrWhiteSpace(e.Id)).GroupBy(e => e.Id!).Select(g => g.First()).ToList();
        var ids = entities.Select(e => e.Id!).ToHashSet(StringComparer.Ordinal);
        // The ids of a name (or a synonym), case ignored.
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entities)
            foreach (var name in new[] { entry.Name }.Concat(entry.Synonyms ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byName.TryGetValue(name, out var list))
                    byName[name] = list = [];
                list.Add(entry.Id!);
            }
        IReadOnlyList<string> Resolve(string name) => byName.TryGetValue(name.Trim(), out var found) ? found : [];

        var nodes = entities.Select(Node).ToList();
        var edges = new Dictionary<string, object>(StringComparer.Ordinal);
        var unresolved = 0;
        foreach (var entry in entities)
            foreach (var (id, edge) in StructureEdges(entry, ids.Contains, Resolve, () => unresolved++))
                edges.TryAdd(id, edge);
        var ends = new Dictionary<string, object>(StringComparer.Ordinal);
        var activeRelations = relations.Where(r => !r.IsDeleted).ToList();
        foreach (var relation in activeRelations)
            foreach (var (id, edge) in RelationEdges(relation, ids.Contains, ends))
                edges.TryAdd(id, edge);
        nodes.AddRange(ends.Values);
        var nodeIds = ids.Select(NodeId).Concat(ends.Keys).ToHashSet(StringComparer.Ordinal);

        var removed = 0;
        if (complete)
        {
            var heldNodes = (await AllIdsAsync(client, $"nodes(label: \"{OntologyLabel}\"", ct)).Concat(await AllIdsAsync(client, $"nodes(label: \"{EndLabel}\"", ct));
            var heldEdges = (await AllIdsAsync(client, $"edges(where: [{{property: \"source\", eq: \"{OntologySource}\"}}]", ct))
                .Concat(await AllIdsAsync(client, $"edges(where: [{{property: \"source\", eq: \"{RelationSource}\"}}]", ct));
            var staleEdges = heldEdges.Where(id => !edges.ContainsKey(id)).Distinct().ToList();
            var staleNodes = heldNodes.Where(id => !nodeIds.Contains(id)).Distinct().ToList();
            foreach (var chunk in staleEdges.Chunk(5000))
                removed += (await client.RunAsync("mutation($ids: [ID!]!) { deleteEdges(ids: $ids) }", new { ids = chunk }, ct)).GetProperty("deleteEdges").GetInt32();
            foreach (var chunk in staleNodes.Chunk(2000))
                removed += (await client.RunAsync("mutation($ids: [ID!]!) { deleteNodes(ids: $ids) }", new { ids = chunk }, ct)).GetProperty("deleteNodes").GetInt32();
        }

        int created = 0, updated = 0;
        async Task Import(object[] nodesPart, object[] edgesPart)
        {
            var written = (await client.RunAsync("""
                mutation($nodes: [NodeInput!], $edges: [EdgeInput!]) {
                  importGraph(nodes: $nodes, edges: $edges, mergeEdges: false, replace: true) { nodesCreated nodesUpdated edgesCreated edgesUpdated }
                }
                """, new { nodes = nodesPart, edges = edgesPart }, ct)).GetProperty("importGraph");
            created += written.GetProperty("nodesCreated").GetInt32() + written.GetProperty("edgesCreated").GetInt32();
            updated += written.GetProperty("nodesUpdated").GetInt32() + written.GetProperty("edgesUpdated").GetInt32();
        }
        foreach (var chunk in nodes.Chunk(NodeBatch))
            await Import(chunk, []);
        foreach (var chunk in edges.Values.Chunk(EdgeBatch))
            await Import([], chunk);
        return new SyncResult(entities.Count, activeRelations.Count, edges.Count, created, updated, removed, unresolved);
    }

    /// <summary>Every id a list of graphbase gives (<paramref name="field"/>: its name and its first arguments), page by page.</summary>
    private static async Task<List<string>> AllIdsAsync(GraphbaseClient client, string field, CancellationToken ct)
    {
        var ids = new List<string>();
        for (var skip = 0; ; skip += Page)
        {
            var data = await client.RunAsync($"query($skip: Int!) {{ list: {field}, first: {Page}, skip: $skip) {{ id }} }}", new { skip }, ct);
            var page = data.GetProperty("list").EnumerateArray().Select(n => n.GetProperty("id").GetString()).OfType<string>().ToList();
            ids.AddRange(page);
            if (page.Count < Page)
                return ids;
        }
    }

    // ── A change made in the chat ──

    /// <summary>An entity created or changed: its node and its edges to its parent and its related entities (an entity deleted: its node removed).</summary>
    public static async Task WriteOntologyAsync(GraphbaseClient client, OntologyEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
            return;
        var node = NodeId(entry.Id);
        if (entry.IsDeleted)
        {
            await client.RunAsync("mutation($id: ID!) { deleteNode(id: $id) }", new { id = node }, ct);
            return;
        }
        var names = (entry.RelatedEntities ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList();
        var parent = entry.NodeParentId is { Length: > 0 } p ? NodeId(p) : "";
        var known = await client.RunAsync($$"""
            query($names: [JSON!], $parent: ID!, $node: ID!) {
              named: nodes(label: "{{OntologyLabel}}", where: [{property: "name", in: $names}], first: 1000) { id properties }
              parent: node(id: $parent) { id }
              held: edges(from: $node, where: [{property: "source", eq: "{{OntologySource}}"}], first: 10000) { id }
            }
            """, new { names, parent = parent.Length > 0 ? parent : "-", node }, ct);
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var named in known.GetProperty("named").EnumerateArray())
            if (named.GetProperty("properties").TryGetProperty("name", out var n) && n.GetString() is { } name
                && named.GetProperty("properties").TryGetProperty("ontologyId", out var o) && o.GetString() is { } ontologyId)
            {
                if (!byName.TryGetValue(name, out var list))
                    byName[name] = list = [];
                list.Add(ontologyId);
            }
        var parentExists = known.GetProperty("parent").ValueKind == JsonValueKind.Object;
        var edges = StructureEdges(entry, id => id == entry.NodeParentId && parentExists, name => byName.TryGetValue(name.Trim(), out var ids) ? ids : [], () => { }).ToList();
        var stale = known.GetProperty("held").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).Except(edges.Select(e => e.Id)).ToList();
        await client.RunAsync("""
            mutation($stale: [ID!]!, $nodes: [NodeInput!], $edges: [EdgeInput!]) {
              deleteEdges(ids: $stale)
              importGraph(nodes: $nodes, edges: $edges, mergeEdges: false, replace: true) { nodesCreated }
            }
            """, new { stale, nodes = new[] { Node(entry) }, edges = edges.Select(e => e.Edge).ToArray() }, ct);
    }

    /// <summary>A relation saved: its edges (the ones it no longer has removed); a relation deleted: its edges removed.</summary>
    public static async Task WriteRelationAsync(GraphbaseClient client, RelationEntry relation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(relation.Id))
            return;
        if (relation.IsDeleted)
        {
            await RemoveRelationAsync(client, relation.Id, ct);
            return;
        }
        var version = relation.Current;
        var objects = new[] { version.Source }.Concat(version.Destinations).Select(e => e.ObjectId).OfType<string>().Where(o => o.Length > 0).Distinct().Select(NodeId).ToList();
        var known = await client.RunAsync("""
            query($ids: [ID!], $relation: JSON) {
              entities: nodes(ids: $ids, first: 1000) { id }
              held: edges(where: [{property: "relationId", eq: $relation}], first: 1000) { id }
            }
            """, new { ids = objects, relation = relation.Id }, ct);
        var entities = known.GetProperty("entities").EnumerateArray().Select(n => n.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var ends = new Dictionary<string, object>(StringComparer.Ordinal);
        var edges = RelationEdges(relation, objectId => entities.Contains(NodeId(objectId)), ends).ToList();
        var stale = known.GetProperty("held").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).Except(edges.Select(e => e.Id)).ToList();
        await client.RunAsync("""
            mutation($stale: [ID!]!, $nodes: [NodeInput!], $edges: [EdgeInput!]) {
              deleteEdges(ids: $stale)
              importGraph(nodes: $nodes, edges: $edges, mergeEdges: false, replace: true) { edgesCreated }
            }
            """, new { stale, nodes = ends.Values.ToArray(), edges = edges.Select(e => e.Edge).ToArray() }, ct);
    }

    /// <summary>A relation deleted: its edges removed.</summary>
    public static async Task RemoveRelationAsync(GraphbaseClient client, string relationId, CancellationToken ct)
    {
        var held = await client.RunAsync("query($relation: JSON) { edges(where: [{property: \"relationId\", eq: $relation}], first: 1000) { id } }", new { relation = relationId }, ct);
        var ids = held.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
        if (ids.Count > 0)
            await client.RunAsync("mutation($ids: [ID!]!) { deleteEdges(ids: $ids) }", new { ids }, ct);
    }
}
