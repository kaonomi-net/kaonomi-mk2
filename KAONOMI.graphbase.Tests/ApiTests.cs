using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using KAONOMI.graphbase.Configuration;

namespace KAONOMI.graphbase.Tests;

/// <summary>A server started in the process of the tests, on a free port (HTTP), its data in a temporary folder.</summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "kaonomi-graphbase-api-" + Guid.NewGuid().ToString("N")[..8]);
    private GraphbaseHost? _host;

    public HttpClient Client { get; } = new();
    public string GraphQLUrl => _host!.GraphQLUrl;

    public async Task InitializeAsync()
    {
        var settings = GraphSettings.Load(null, ["http.port=" + FreePort(), "path.data=" + _data, "https.enabled=false", "network.host=127.0.0.1"]);
        _host = await GraphbaseHost.StartAsync(settings) ?? throw new InvalidOperationException("The server did not start");
    }

    /// <summary>Runs a GraphQL request; its <c>data</c> and <c>errors</c>.</summary>
    public async Task<JsonObject> Run(string query, object? variables = null)
    {
        using var response = await Client.PostAsJsonAsync(GraphQLUrl, new { query, variables });
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
        if (Directory.Exists(_data))
            Directory.Delete(_data, recursive: true);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed class ApiTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static string? Code(JsonObject result) => result["errors"]?[0]?["extensions"]?["code"]?.GetValue<string>();

    [Fact]
    public async Task Nodes_edges_and_their_neighbours()
    {
        var made = await server.Run("""
            mutation {
              a: createNode(id: "api-ada", labels: ["Person"], properties: {name: "Ada", born: 1815}) { id }
              b: createNode(id: "api-charles", labels: ["Person"], properties: {name: "Charles", born: 1791}) { id }
              e: createEdge(type: "KNOWS", from: "api-ada", to: "api-charles", properties: {since: 1833}) { id from { id } to { id } }
            }
            """);
        Assert.Null(made["errors"]);
        Assert.Equal("api-charles", made["data"]!["e"]!["to"]!["id"]!.GetValue<string>());

        var read = await server.Run("""
            {
              node(id: "api-ada") { labels property(name: "born") degree neighbors(direction: OUT) { id } }
              nodes(label: "Person", where: [{property: "born", lt: 1800}]) { id }
              matched: nodeCount(match: "(api-ada OR api-charles) and person")
            }
            """);
        Assert.Equal(2, read["data"]!["matched"]!.GetValue<int>());
        Assert.Null(read["errors"]);
        Assert.Equal(1815, read["data"]!["node"]!["property"]!.GetValue<int>());
        Assert.Equal(1, read["data"]!["node"]!["degree"]!.GetValue<int>());
        Assert.Equal("api-charles", read["data"]!["node"]!["neighbors"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("api-charles", Assert.Single(read["data"]!["nodes"]!.AsArray())!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Import_with_variables_and_shortest_path()
    {
        var imported = await server.Run("""
            mutation($nodes: [NodeInput!], $edges: [EdgeInput!]) {
              importGraph(nodes: $nodes, edges: $edges) { nodesCreated edgesCreated }
            }
            """, new
        {
            nodes = new[] { new { id = "p1" }, new { id = "p2" }, new { id = "p3" } },
            edges = new[] { new { type = "R", from = "p1", to = "p2" }, new { type = "R", from = "p2", to = "p3" } }
        });
        Assert.Null(imported["errors"]);
        Assert.Equal(3, imported["data"]!["importGraph"]!["nodesCreated"]!.GetValue<int>());

        var path = await server.Run("""{ shortestPath(from: "p1", to: "p3", direction: OUT) { length nodes { id } } }""");
        Assert.Equal(2, path["data"]!["shortestPath"]!["length"]!.GetValue<int>());
    }

    [Fact]
    public async Task Refusals_carry_their_code()
    {
        Assert.Equal("NOT_FOUND", Code(await server.Run("""mutation { createEdge(type: "X", from: "nobody", to: "none") { id } }""")));
        await server.Run("""mutation { createNode(id: "api-dup") { id } }""");
        Assert.Equal("ALREADY_EXISTS", Code(await server.Run("""mutation { createNode(id: "api-dup") { id } }""")));
        Assert.Equal("INVALID_ARGUMENT", Code(await server.Run("""{ nodes(where: [{property: "name", matches: "("}]) { id } }""")));
        Assert.Equal("INVALID_ARGUMENT", Code(await server.Run("""mutation { deleteNodes }""")));
        Assert.Equal("INVALID_ARGUMENT", Code(await server.Run("""{ nodes(match: "(ada OR charles") { id } }""")));
    }

    [Fact]
    public async Task Too_deep_a_query_is_refused()
    {
        var deep = "{ node(id: \"api-ada\") { " + string.Concat(Enumerable.Repeat("neighbors { ", 25)) + "id" + new string('}', 25) + " } }";
        var result = await server.Run(deep);
        Assert.NotNull(result["errors"]);
        Assert.Null(result["data"]);
    }

    [Fact]
    public async Task The_root_says_what_the_server_is()
    {
        var root = await server.Client.GetFromJsonAsync<JsonElement>(server.GraphQLUrl.Replace("/graphql", "/"));
        Assert.Equal("KAONOMI.graphbase", root.GetProperty("name").GetString());
    }
}
