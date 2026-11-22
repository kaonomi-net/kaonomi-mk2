using System.Net;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using HotChocolate.AspNetCore;
using HotChocolate.Subscriptions;
using KAONOMI.graphbase.Configuration;
using KAONOMI.graphbase.GraphQL;
using KAONOMI.graphbase.Storage;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using NodeType = KAONOMI.graphbase.GraphQL.NodeType;

namespace KAONOMI.graphbase;

/// <summary>The server running: its graph and its GraphQL API (also started by the tests, in their process).</summary>
public sealed class GraphbaseHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public GraphStore Store { get; }
    public GraphSettings Settings { get; }

    /// <summary>The address of the server (<c>http://localhost:4000</c>).</summary>
    public string Url { get; }

    /// <summary>The address of the GraphQL API (<c>http://localhost:4000/graphql</c>).</summary>
    public string GraphQLUrl => Url + Settings.GraphQLPath;

    private GraphbaseHost(WebApplication app, GraphStore store, GraphSettings settings, string url)
    {
        _app = app;
        Store = store;
        Settings = settings;
        Url = url;
    }

    /// <summary>Opens the graph of <c>path.data</c> and starts listening, or returns null (logged) when it cannot.</summary>
    public static async Task<GraphbaseHost?> StartAsync(GraphSettings settings)
    {
        var log = GraphLog.For(settings);
        log.Info("node", $"KAONOMI.graphbase {GraphSettings.Version}, config [{settings.File ?? "defaults"}], data [{settings.DataPath}]");

        // The port first: a server already running there keeps its graph to itself.
        if (PortInUse(settings))
        {
            log.Error("http", $"cannot listen on {settings.Host}:{settings.Port}: the port is used (a graphbase is running?)");
            return null;
        }
        GraphStore store;
        try
        {
            store = GraphStore.Open(settings.DataPath);
        }
        catch (Exception ex) when (ex is DataPathInUseException or SqliteException or IOException or UnauthorizedAccessException)
        {
            log.Error("store", ex.Message);
            return null;
        }
        log.Info("store", $"{store.NodeCount} node(s), {store.EdgeCount} edge(s) in [{Path.Combine(settings.DataPath, GraphStore.DatabaseName)}]");
        if (store.OrphanEdges > 0)
            log.Warn("store", $"{store.OrphanEdges} edge(s) left out: a node of their ends is missing");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = 64 * 1024 * 1024;
            kestrel.AddServerHeader = false;
            switch (settings.Host)
            {
                case "0.0.0.0" or "*":
                    kestrel.ListenAnyIP(settings.Port);
                    break;
                case "localhost":
                    kestrel.ListenLocalhost(settings.Port);
                    break;
                default:
                    kestrel.Listen(Address(settings.Host), settings.Port);
                    break;
            }
        });
        builder.Services.AddSingleton(store).AddSingleton(settings).AddSingleton(log).AddSingleton(new ServerInfo(DateTimeOffset.UtcNow));
        builder.Services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>()
            .AddSubscriptionType<Subscription>()
            .AddType<NodeType>()
            .AddType<EdgeType>()
            .AddType<PathType>()
            .AddType<SubgraphType>()
            .AddType<NameCountType>()
            .AddType<ImportResultType>()
            .AddType<GraphChangeType>()
            .AddType<StatsType>()
            .AddTypeExtension<NodeFields>()
            .AddTypeExtension<EdgeFields>()
            .AddInMemorySubscriptions()
            .AddErrorFilter(_ => new GraphErrorFilter(log))
            .AddMaxExecutionDepthRule(settings.MaxDepth)
            // The lists are cut by graphql.max_results and the depth by graphql.max_depth: no cost analysis on top.
            .ModifyCostOptions(options => options.EnforceCostLimits = false);

        var app = builder.Build();
        app.UseWebSockets();
        if (settings.LogRequests)
            app.Use(async (context, next) =>
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                await next(context);
                log.Info("http", $"{context.Request.Method} {context.Request.Path} {context.Response.StatusCode} {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}ms");
            });
        if (settings.SecurityEnabled)
        {
            if (settings.Users.Count == 0)
                log.Warn("security", "security.enabled without security.users: every request is refused");
            app.Use(async (context, next) =>
            {
                if (Authorized(context.Request.Headers.Authorization.ToString(), settings.Users))
                {
                    await next(context);
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"KAONOMI.graphbase\", charset=\"UTF-8\"";
                await context.Response.WriteAsJsonAsync(new { errors = new[] { new { message = "authentication required (security.users)", extensions = new { code = "UNAUTHENTICATED" } } } });
            });
        }
        app.MapGet("/", (GraphStore graph) => Results.Json(new
        {
            name = "KAONOMI.graphbase",
            version = GraphSettings.Version,
            graphql = settings.GraphQLPath,
            nodes = graph.NodeCount,
            edges = graph.EdgeCount
        }));
        // The IDE from the files of the package (ChilliCream.Nitro.App), not from the CDN of ChilliCream: nothing leaves the machine.
        app.MapGraphQL(settings.GraphQLPath).WithOptions(new GraphQLServerOptions { Tool = { Enable = settings.Ide, ServeMode = GraphQLToolServeMode.Embedded } });

        // The changes of the graph to the subscriptions, in their order, without holding the writes.
        var sender = app.Services.GetRequiredService<ITopicEventSender>();
        var changes = Channel.CreateUnbounded<GraphChange>(new UnboundedChannelOptions { SingleReader = true });
        store.Changed += batch =>
        {
            foreach (var change in batch)
                changes.Writer.TryWrite(change);
        };
        var forwarding = Task.Run(async () =>
        {
            await foreach (var change in changes.Reader.ReadAllAsync())
            {
                try
                {
                    await sender.SendAsync(GraphChange.Topic, change);
                }
                catch (Exception ex)
                {
                    log.Warn("subscriptions", $"{change.Kind} not sent: {ex.Message}");
                }
            }
        });
        app.Lifetime.ApplicationStopped.Register(() =>
        {
            log.Info("node", "stopping ...");
            changes.Writer.TryComplete();
            forwarding.Wait(TimeSpan.FromSeconds(2));
            store.Dispose();
            log.Info("node", "stopped");
        });

        try
        {
            await app.StartAsync();
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            log.Error("http", $"cannot listen on {settings.Host}:{settings.Port}: {ex.Message}");
            changes.Writer.TryComplete();
            store.Dispose();
            return null;
        }
        var host = settings.Host is "0.0.0.0" or "*" ? "localhost" : settings.Host.Contains(':') ? $"[{settings.Host}]" : settings.Host;
        var url = $"{settings.Scheme}://{host}:{settings.Port}";
        log.Info("http", $"GraphQL API on {url}{settings.GraphQLPath}{(settings.Ide ? " (open it in a browser: the IDE)" : string.Empty)}, security {(settings.SecurityEnabled ? "on" : "off")}");
        return new GraphbaseHost(app, store, settings, url);
    }

    /// <summary>True when the header gives a user of <c>security.users</c> and its password (basic authentication).</summary>
    private static bool Authorized(string header, List<GraphUser> users)
    {
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;
        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }
        var colon = decoded.IndexOf(':');
        if (colon < 0)
            return false;
        var name = Encoding.UTF8.GetBytes(decoded[..colon]);
        var password = Encoding.UTF8.GetBytes(decoded[(colon + 1)..]);
        var found = false;
        foreach (var user in users) // every user compared, in constant time
            found |= CryptographicOperations.FixedTimeEquals(name, Encoding.UTF8.GetBytes(user.Name))
                     & CryptographicOperations.FixedTimeEquals(password, Encoding.UTF8.GetBytes(user.Password));
        return found;
    }

    private static IPAddress Address(string host) =>
        IPAddress.TryParse(host, out var ip) ? ip : Dns.GetHostAddresses(host).First();

    private static bool PortInUse(GraphSettings settings)
    {
        var address = settings.Host switch
        {
            "0.0.0.0" or "*" => IPAddress.Any,
            "localhost" => IPAddress.Loopback,
            _ => Address(settings.Host)
        };
        try
        {
            var probe = new TcpListener(address, settings.Port);
            probe.Start();
            probe.Stop();
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
