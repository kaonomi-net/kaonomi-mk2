using System.Text.Json;
using KAONOMI.graphbase.Storage;

namespace KAONOMI.graphbase.Tests;

/// <summary>The graph itself (<see cref="GraphStore"/>), its data in a temporary folder.</summary>
public sealed class StoreTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "kaonomi-graphbase-tests-" + Guid.NewGuid().ToString("N")[..8]);
    private GraphStore _store;

    public StoreTests() => _store = GraphStore.Open(_data);

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_data))
            Directory.Delete(_data, recursive: true);
    }

    private static JsonElement Json(string json) => JsonValues.Parse(json);

    private void Reopen()
    {
        _store.Dispose();
        _store = GraphStore.Open(_data);
    }

    [Fact]
    public void What_is_written_is_there_after_a_restart()
    {
        _store.CreateNode("ada", ["Person"], Json("""{"name": "Ada", "born": 1815, "address": {"city": "London"}}"""));
        _store.CreateNode("charles", ["Person"], null);
        var edge = _store.CreateEdge(null, "KNOWS", "ada", "charles", Json("""{"since": 1833}"""));
        _store.UpdateNode("ada", new NodeChange { Properties = Json("""{"born": null, "died": 1852}"""), AddLabels = ["Mathematician"] });

        Reopen();

        var ada = _store.GetNode("ada")!;
        Assert.Equal(["Person", "Mathematician"], ada.Labels);
        Assert.Equal("London", ada.Properties.GetProperty("address").GetProperty("city").GetString());
        Assert.False(ada.Properties.TryGetProperty("born", out _));
        Assert.Equal(1852, ada.Properties.GetProperty("died").GetInt32());
        Assert.Equal(1833, _store.GetEdge(edge.Id)!.Properties.GetProperty("since").GetInt32());
        Assert.Equal(1, _store.Degree("charles", Direction.In, "KNOWS"));
    }

    [Fact]
    public void A_second_server_on_the_same_folder_is_refused()
    {
        Assert.Throws<DataPathInUseException>(() => GraphStore.Open(_data));
    }

    [Fact]
    public void An_import_is_all_or_nothing()
    {
        var error = Assert.Throws<GraphException>(() => _store.Import(
            [new NodeWrite("rome", ["Place"], null)],
            [new EdgeWrite(null, "IN", "rome", "nowhere", null)], mergeEdges: true));
        Assert.Equal(GraphException.NotFound, error.Code);
        Assert.StartsWith("edges[0]:", error.Message);
        Assert.Null(_store.GetNode("rome"));
        Reopen();
        Assert.Equal(0, _store.NodeCount);
    }

    [Fact]
    public void An_import_completes_what_exists_and_merges_the_edges()
    {
        _store.CreateNode("london", ["Place"], Json("""{"name": "London"}"""));
        var counts = _store.Import(
            [new NodeWrite("london", ["City"], Json("""{"country": "UK"}""")), new NodeWrite("ada", ["Person"], null)],
            [new EdgeWrite(null, "LIVED_IN", "ada", "london", Json("""{"from": 1815}""")), new EdgeWrite(null, "LIVED_IN", "ada", "london", Json("""{"to": 1852}"""))],
            mergeEdges: true);
        Assert.Equal(new ImportCounts(1, 1, 1, 1, 0), counts);
        var edge = Assert.Single(_store.EdgesOf("ada", Direction.Out, null, 10));
        Assert.Equal(1815, edge.Properties.GetProperty("from").GetInt32());
        Assert.Equal(1852, edge.Properties.GetProperty("to").GetInt32());
        Assert.Equal(["Place", "City"], _store.GetNode("london")!.Labels);
    }

    [Fact]
    public void An_import_that_replaces_makes_the_graph_what_it_gives()
    {
        _store.Import(
            [new NodeWrite("a", ["Archi", "Agent"], Json("""{"name": "A", "goal": "answer"}""")), new NodeWrite("b", ["Archi"], null), new NodeWrite("c", ["Archi"], null)],
            [new EdgeWrite("r1", "USES", "a", "b", Json("""{"kind": "uses", "note": "x"}"""))], mergeEdges: false);
        _store.UpdateNode("a", new NodeChange { AddLabels = ["Extra"] });

        var counts = _store.Import(
            [new NodeWrite("a", ["Archi", "Tool"], Json("""{"name": "A"}"""))],
            [new EdgeWrite("r1", "CALLS", "a", "c", Json("""{"kind": "calls"}"""))], mergeEdges: false, replace: true);

        Assert.Equal(new ImportCounts(0, 1, 0, 1, 0), counts);
        var a = _store.GetNode("a")!;
        Assert.Equal(["Archi", "Tool"], a.Labels);
        Assert.False(a.Properties.TryGetProperty("goal", out _));
        var edge = _store.GetEdge("r1")!;
        Assert.Equal(("CALLS", "a", "c"), (edge.Type, edge.From, edge.To));
        Assert.False(edge.Properties.TryGetProperty("note", out _));
        Assert.Equal(0, _store.Degree("b", Direction.Both, null));
        Assert.Equal(1, _store.DeleteEdges(["r1", "r1", "nothing"]));
        Reopen();
        Assert.Equal(0, _store.EdgeCount);
    }

    [Fact]
    public void Merge_node_writes_one_node_per_key()
    {
        var first = _store.MergeNode("Person", "name", Json("\"Alan Turing\""), null, Json("""{"born": 1912}"""));
        var second = _store.MergeNode("Person", "name", Json("\"Alan Turing\""), ["Scientist"], Json("""{"died": 1954}"""));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _store.NodeCount);
        Assert.Equal(["Person", "Scientist"], second.Labels);
        Assert.Equal(1912, second.Properties.GetProperty("born").GetInt32());
    }

    [Fact]
    public void A_change_that_changes_nothing_is_not_written()
    {
        var node = _store.CreateNode("ada", ["Person"], Json("""{"name": "Ada"}"""));
        var changes = 0;
        _store.Changed += batch => changes += batch.Count;
        var same = _store.UpdateNode("ada", new NodeChange { Properties = Json("""{"name": "Ada"}"""), AddLabels = ["Person"] });
        Assert.Same(node, same);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Deleting_a_node_deletes_its_edges_or_is_refused()
    {
        _store.CreateNode("a", null, null);
        _store.CreateNode("b", null, null);
        _store.CreateEdge("loop", "SELF", "a", "a", null);
        _store.CreateEdge("ab", "LINK", "a", "b", null);
        Assert.Equal(GraphException.HasEdges, Assert.Throws<GraphException>(() => _store.DeleteNode("a", detach: false)).Code);
        GraphChangeKind[]? kinds = null;
        _store.Changed += batch => kinds = batch.Select(c => c.Kind).ToArray();
        Assert.True(_store.DeleteNode("a", detach: true));
        Assert.Equal([GraphChangeKind.EdgeDeleted, GraphChangeKind.EdgeDeleted, GraphChangeKind.NodeDeleted], kinds);
        Assert.Equal(0, _store.EdgeCount);
        Assert.Equal(0, _store.Degree("b", Direction.Both, null));
        Assert.False(_store.DeleteNode("a", detach: true));
    }

    [Fact]
    public void Conditions_on_the_properties()
    {
        _store.CreateNode("ada", ["Person"], Json("""{"name": "Ada Lovelace", "born": 1815, "tags": ["math", "poetry"]}"""));
        _store.CreateNode("charles", ["Person"], Json("""{"name": "Charles Babbage", "born": 1791.0}"""));
        _store.CreateNode("paris", ["Place"], Json("""{"name": "Paris"}"""));

        IEnumerable<string> Ids(params PropertyCondition[] where) => _store.FindNodes(new NodeQuery { Where = where }).Select(n => n.Id);

        Assert.Equal(["charles"], Ids(new PropertyCondition { Property = "born", Eq = Json("1791") }));
        Assert.Equal(["ada"], Ids(new PropertyCondition { Property = "tags", Eq = Json("\"poetry\"") }));
        Assert.Equal(["charles", "paris"], Ids(new PropertyCondition { Property = "tags", Ne = Json("\"poetry\"") }));
        Assert.Equal(["ada"], Ids(new PropertyCondition { Property = "born", Gte = Json("1800"), Lt = Json("1900") }));
        Assert.Equal(["paris"], Ids(new PropertyCondition { Property = "born", Exists = false }));
        Assert.Equal(["ada", "charles"], Ids(new PropertyCondition { Property = "name", Matches = PropertyCondition.Pattern("^(ada|charles) ") }));
        Assert.Equal(["charles"], Ids(new PropertyCondition { Property = "name", Contains = "BABB" }));
        Assert.Equal(["charles", "ada"], _store.FindNodes(new NodeQuery { Label = "Person", OrderBy = "born" }).Select(n => n.Id));
        Assert.Equal(["paris"], _store.FindNodes(new NodeQuery { Search = "pari" }).Select(n => n.Id));
        _store.CreateEdge(null, "KNOWS", "ada", "charles", null);
        _store.CreateEdge(null, "VISITED", "ada", "paris", null);
        Assert.Equal(["ada", "charles", "paris"], _store.FindNodes(new NodeQuery { OrderBy = "degree", Descending = true }).Select(n => n.Id));
    }

    [Fact]
    public void Match_joins_texts_with_and_or_and_parentheses()
    {
        _store.CreateNode("ada", ["Person"], Json("""{"name": "Ada Lovelace", "field": "mathematics", "city": "London"}"""));
        _store.CreateNode("charles", ["Person"], Json("""{"name": "Charles Babbage", "field": "mathematics", "city": "London"}"""));
        _store.CreateNode("mary", ["Person"], Json("""{"name": "Mary Shelley", "field": "novels", "city": "London"}"""));
        _store.CreateNode("paris", ["Place"], Json("""{"name": "Paris", "motto": "rock and roll"}"""));

        IEnumerable<string> Ids(string match) => _store.FindNodes(new NodeQuery { Match = match }).Select(n => n.Id);

        Assert.Equal(["ada", "charles"], Ids("london AND mathematics"));
        Assert.Equal(["ada", "paris"], Ids("lovelace or PARIS"));
        Assert.Equal(["ada", "mary"], Ids("london and (lovelace OR shelley)"));
        Assert.Equal(["mary", "paris"], Ids("paris OR london AND novels"));
        Assert.Empty(Ids("(paris OR london) AND novels AND place"));
        // Words side by side are one text, as written; in quotes, AND is a word of the text.
        Assert.Equal(["ada"], Ids("Ada Lovelace"));
        Assert.Empty(Ids("Lovelace Ada"));
        Assert.Equal(["paris"], Ids("\"rock and roll\""));
        Assert.Empty(Ids("\"rock or roll\""));
        // A group next to a text is joined to it by AND.
        Assert.Equal(["charles"], Ids("(babbage OR shelley) mathematics"));
        Assert.Equal(1, _store.CountNodes(new NodeQuery { Match = "paris OR (london AND novels)", Label = "Place" }));
        Assert.Equal(["ada"], _store.FindNodes(new NodeQuery { Search = "london", Match = "lovelace OR paris" }).Select(n => n.Id));
        Assert.Equal(4, _store.FindNodes(new NodeQuery { Match = "  " }).Count);
        Assert.Equal(2, _store.DeleteNodes(new NodeQuery { Match = "babbage OR shelley" }));
    }

    [Theory]
    [InlineData("a OR b AND c", "(\"a\" OR (\"b\" AND \"c\"))")]
    [InlineData("(a OR b) AND c", "((\"a\" OR \"b\") AND \"c\")")]
    [InlineData("a or b or c and d", "(\"a\" OR \"b\" OR (\"c\" AND \"d\"))")]
    [InlineData("Parti  démocrate or \"x and (y)\"", "(\"Parti  démocrate\" OR \"x and (y)\")")]
    [InlineData("(a) b \"c\"", "(\"a\" AND \"b\" AND \"c\")")]
    [InlineData("((kaonomi))", "\"kaonomi\"")]
    [InlineData("android ORACLE", "\"android ORACLE\"")]
    public void A_match_reads_and_before_or(string match, string read) =>
        Assert.Equal(read, SearchExpression.Parse(match).ToString());

    [Theory]
    [InlineData("", "nothing to search")]
    [InlineData("a AND", "AND needs a text after it")]
    [InlineData("OR a", "OR needs a text before it")]
    [InlineData("a and or b", "and or: a text is missing between them")]
    [InlineData("a OR )", "OR needs a text after it")]
    [InlineData("(a OR b", "a ( is not closed")]
    [InlineData("a) OR b", "a ) closes no (")]
    [InlineData("a AND ()", "nothing between ( and )")]
    [InlineData("\"a OR b", "a \" is not closed")]
    [InlineData("a \" \"", "nothing between the quotes \"\"")]
    public void A_match_not_well_formed_is_refused(string match, string reason)
    {
        var error = Assert.Throws<GraphException>(() => SearchExpression.Parse(match));
        Assert.Equal(GraphException.Invalid, error.Code);
        Assert.Equal($"match: {reason}", error.Message);
    }

    [Fact]
    public void A_match_too_deep_is_refused()
    {
        var deep = new string('(', 100) + "a" + new string(')', 100);
        Assert.Contains("parentheses", Assert.Throws<GraphException>(() => SearchExpression.Parse(deep)).Message);
        Assert.Equal("\"a\"", SearchExpression.Parse(new string('(', SearchExpression.MaxDepth) + "a" + new string(')', SearchExpression.MaxDepth)).ToString());
    }

    [Fact]
    public void Shortest_path_and_neighborhood()
    {
        foreach (var id in new[] { "a", "b", "c", "d", "e" })
            _store.CreateNode(id, ["N"], null);
        _store.CreateEdge(null, "NEXT", "a", "b", null);
        _store.CreateEdge(null, "NEXT", "b", "c", null);
        _store.CreateEdge(null, "NEXT", "c", "d", null);
        _store.CreateEdge(null, "JUMP", "a", "d", null);
        _store.CreateEdge(null, "NEXT", "e", "d", null);

        Assert.Equal(1, _store.ShortestPath("a", "d", Direction.Out, null, 6)!.Length);
        var path = _store.ShortestPath("a", "d", Direction.Out, ["NEXT"], 6)!;
        Assert.Equal(["a", "b", "c", "d"], path.Nodes.Select(n => n.Id));
        Assert.Null(_store.ShortestPath("a", "d", Direction.Out, ["NEXT"], 2));
        Assert.Null(_store.ShortestPath("a", "e", Direction.Out, null, 6));
        Assert.Equal(["a", "d", "e"], _store.ShortestPath("a", "e", Direction.Both, null, 6)!.Nodes.Select(n => n.Id));

        var around = _store.Neighborhood(["d"], 1, Direction.Both, null, 100);
        Assert.Equal(["a", "c", "d", "e"], around.Nodes.Select(n => n.Id).Order());
        Assert.Equal(3, around.Edges.Count);
        Assert.False(around.Truncated);
        Assert.True(_store.Neighborhood(["d"], 2, Direction.Both, null, 2).Truncated);
    }

    [Fact]
    public async Task Writes_at_the_same_time_are_all_kept()
    {
        _store.CreateNode("hub", null, null);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                var node = _store.CreateNode($"n{t}-{i}", ["Leaf"], null);
                _store.CreateEdge(null, "OF", node.Id, "hub", null);
                _ = _store.FindNodes(new NodeQuery { Label = "Leaf", First = 10 });
            }
        })));
        Assert.Equal(400, _store.Degree("hub", Direction.In, "OF"));
        Reopen();
        Assert.Equal(401, _store.NodeCount);
        Assert.Equal(400, _store.EdgeCount);
    }
}
