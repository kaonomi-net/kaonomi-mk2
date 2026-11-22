using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The repository of <c>/archi</c> in KAONOMI.graphbase, as a graph:
/// <list type="bullet">
/// <item>a node per fact sheet, id <c>archi:&lt;id&gt;</c>, labels <c>Archi</c> and its kind (<c>Application</c>,
/// <c>AIModel</c>, <c>Agent</c>…), its properties: <c>name</c>, <c>fullName</c> (with its parents), <c>type</c>,
/// <c>layer</c>, <c>description</c>, <c>owner</c>, <c>tags</c>, <c>phase</c> (today), <c>lifecycle</c>,
/// <c>criticality</c>, <c>functionalFit</c>, <c>technicalFit</c>, <c>aiRisk</c>, <c>completeness</c>, <c>updated</c>,
/// <c>archiId</c>, and the fields of its kind (<c>provider</c>, <c>hosting</c>, <c>autonomy</c>…);</item>
/// <item>an edge per relation, id <c>archi:&lt;id&gt;</c>, its kind as the type (<c>runs on</c> → <c>RUNS_ON</c>), its
/// properties <c>kind</c>, <c>description</c>, <c>archiId</c>;</item>
/// <item>an edge <c>CHILD_OF</c> from a fact sheet to its parent, id <c>archi:parent:&lt;id&gt;</c>.</item>
/// </list>
/// Every node and edge written here has <c>source: "archi"</c>. A synchronisation makes the graph what the repository
/// is: the fact sheets and relations removed from <c>/archi</c> are removed from the graph, the others written as they
/// are (<c>importGraph</c> with <c>replace</c>); the rest of the graph is not touched.
/// </summary>
public static class ArchiGraph
{
    public const string Label = "Archi";
    public const string Source = "archi";
    public const string ParentType = "CHILD_OF";

    /// <summary>What the repository gives to the graph, read at one moment.</summary>
    public sealed record Payload(List<object> Nodes, List<object> Edges, HashSet<string> NodeIds, HashSet<string> EdgeIds);

    /// <summary>What a synchronisation did.</summary>
    public sealed record SyncResult(int Nodes, int Edges, int NodesCreated, int NodesUpdated, int EdgesCreated, int EdgesUpdated, int NodesDeleted, int EdgesDeleted)
    {
        public bool Changed => NodesCreated + NodesUpdated + EdgesCreated + EdgesUpdated + NodesDeleted + EdgesDeleted > 0;

        public string Summary => $"{Nodes} fact sheets and {Edges} relations"
                                 + (Changed
                                     ? $" ({NodesCreated} created, {NodesUpdated} changed, {NodesDeleted} removed; relations {EdgesCreated} created, {EdgesUpdated} changed, {EdgesDeleted} removed)"
                                     : ", already up to date");
    }

    public static string NodeId(string sheetId) => "archi:" + sheetId;

    /// <summary>
    /// The type of edge of a kind of relation: <c>runs on</c> → <c>RUNS_ON</c>, <c>a pour métier</c> →
    /// <c>A_POUR_METIER</c> (the accents taken away, the rest of the letters and digits kept).
    /// </summary>
    public static string EdgeType(string? kind)
    {
        var plain = new string((kind ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var type = Regex.Replace(plain.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        return type.Length > 0 ? (type.Length > 120 ? type[..120] : type) : "RELATED_TO";
    }

    /// <summary>The nodes and edges of the repository (to be called on the thread that changes it).</summary>
    public static Payload Build(ArchiWorkspace workspace, Func<ArchiSheet, string> fullName, Func<ArchiSheet, int> completeness)
    {
        var nodes = new List<object>();
        var edges = new List<object>();
        var sheets = workspace.Sheets.GroupBy(s => s.Id).Select(g => g.First()).ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var sheet in sheets.Values)
        {
            var info = ArchiCatalog.Info(sheet.Type);
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            // The fields of its kind first: the common properties below win over a field of the same name.
            foreach (var (key, value) in sheet.Fields)
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    properties[key] = value.Trim();
            properties["name"] = sheet.Name;
            properties["fullName"] = fullName(sheet);
            properties["type"] = info.Label;
            properties["layer"] = info.Layer;
            Put(properties, "description", sheet.Description);
            Put(properties, "owner", sheet.Owner);
            var tags = sheet.Tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tags.Length > 0)
                properties["tags"] = tags;
            if (sheet.PhaseOn(DateTime.Today) is { } phase)
                properties["phase"] = phase.ToString();
            if (sheet.Lifecycle.Count > 0)
                properties["lifecycle"] = sheet.Lifecycle.OrderBy(p => p.Key)
                    .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Key.ToString()), p => p.Value);
            Put(properties, "criticality", sheet.Criticality);
            Put(properties, "functionalFit", sheet.FunctionalFit);
            Put(properties, "technicalFit", sheet.TechnicalFit);
            Put(properties, "aiRisk", sheet.AiRisk);
            properties["completeness"] = completeness(sheet);
            properties["updated"] = sheet.Updated.ToString("O", CultureInfo.InvariantCulture);
            properties["archiId"] = sheet.Id;
            properties["source"] = Source;
            nodes.Add(new { id = NodeId(sheet.Id), labels = new[] { Label, sheet.Type.ToString() }, properties });
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in workspace.Relations)
        {
            if (!sheets.ContainsKey(relation.From) || !sheets.ContainsKey(relation.To) || !edgeIds.Add("archi:" + relation.Id))
                continue;
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = relation.Kind, ["archiId"] = relation.Id, ["source"] = Source };
            Put(properties, "description", relation.Description);
            edges.Add(new { id = "archi:" + relation.Id, type = EdgeType(relation.Kind), from = NodeId(relation.From), to = NodeId(relation.To), properties });
        }
        foreach (var sheet in sheets.Values)
        {
            if (sheet.ParentId is not { } parent || !sheets.ContainsKey(parent) || parent == sheet.Id)
                continue;
            var id = "archi:parent:" + sheet.Id;
            edgeIds.Add(id);
            edges.Add(new
            {
                id, type = ParentType, from = NodeId(sheet.Id), to = NodeId(parent),
                properties = new Dictionary<string, object?> { ["kind"] = "child of", ["source"] = Source }
            });
        }
        return new Payload(nodes, edges, sheets.Keys.Select(NodeId).ToHashSet(StringComparer.Ordinal), edgeIds);
    }

    private static void Put(Dictionary<string, object?> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            properties[key] = value.Trim();
    }

    /// <summary>
    /// Makes the graph what <paramref name="payload"/> gives: the nodes and edges of <c>/archi</c> no longer in it
    /// removed, the others written as they are; one request.
    /// </summary>
    public static async Task<SyncResult> SyncAsync(GraphbaseClient client, Payload payload, CancellationToken cancellationToken)
    {
        // What the graph holds of /archi now.
        var held = await client.RunAsync($$"""
            query($first: Int!) {
              nodes(label: "{{Label}}", first: $first) { id }
              edges(where: [{property: "source", eq: "{{Source}}"}], first: $first) { id }
            }
            """, new { first = 10000 }, cancellationToken).ConfigureAwait(false);
        var staleNodes = held.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetString()!).Where(id => !payload.NodeIds.Contains(id)).ToList();
        var staleEdges = held.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).Where(id => !payload.EdgeIds.Contains(id)).ToList();

        // The mutations of one request run one after the other: the removals, then the import.
        var done = await client.RunAsync("""
            mutation($staleEdges: [ID!]!, $staleNodes: [ID!]!, $nodes: [NodeInput!], $edges: [EdgeInput!]) {
              edgesDeleted: deleteEdges(ids: $staleEdges)
              nodesDeleted: deleteNodes(ids: $staleNodes)
              written: importGraph(nodes: $nodes, edges: $edges, mergeEdges: false, replace: true) {
                nodesCreated nodesUpdated edgesCreated edgesUpdated
              }
            }
            """, new { staleEdges, staleNodes, nodes = payload.Nodes, edges = payload.Edges }, cancellationToken).ConfigureAwait(false);
        var written = done.GetProperty("written");
        int Count(string name) => written.GetProperty(name).GetInt32();
        return new SyncResult(payload.Nodes.Count, payload.Edges.Count,
            Count("nodesCreated"), Count("nodesUpdated"), Count("edgesCreated"), Count("edgesUpdated"),
            done.GetProperty("nodesDeleted").GetInt32(), done.GetProperty("edgesDeleted").GetInt32());
    }
}
