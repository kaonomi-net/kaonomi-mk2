using KAONOMI.graphbase;
using KAONOMI.graphbase.Configuration;

// KAONOMI.graphbase: a graph database (nodes with labels and properties, typed edges between them) served by a GraphQL API.
//   KAONOMI.graphbase [--config graphbase.yml] [--port 4000] [--data folder] [--http] [--set key=value]…

string? configFile = null;
var overrides = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--config" or "-c" when i + 1 < args.Length:
            configFile = args[++i];
            break;
        case "--set" or "-E" when i + 1 < args.Length:
            overrides.Add(args[++i]);
            break;
        case "--port" when i + 1 < args.Length:
            overrides.Add("http.port=" + args[++i]);
            break;
        case "--data" when i + 1 < args.Length:
            overrides.Add("path.data=" + args[++i]);
            break;
        case "--http":
            // Kept for the scripts that pass it: plain HTTP is the only mode now.
            break;
        case "-h" or "--help" or "help":
            Console.WriteLine("""
                KAONOMI.graphbase: a graph database with a GraphQL API.

                  KAONOMI.graphbase [--config graphbase.yml] [--port 4000] [--data ~/kaonomi/graphbase] [--http]
                                    [--set key=value]...
                      Serves the GraphQL API (http://localhost:4000/graphql by default; the IDE in a browser).
                      --set: any key of config/graphbase.yml.
                """);
            return 0;
        default:
            Console.Error.WriteLine($"unknown argument [{args[i]}] (--help)");
            return 1;
    }
}
// The file of the project folder (dotnet run from the project or from the solution folder), else the copy of the build.
configFile ??= new[] { "config/graphbase.yml", "graphbase.yml", "KAONOMI.graphbase/config/graphbase.yml",
        Path.Combine(AppContext.BaseDirectory, "config", "graphbase.yml") }
    .FirstOrDefault(File.Exists);

GraphSettings settings;
try
{
    settings = GraphSettings.Load(configFile, overrides);
}
catch (Exception ex) when (ex is ArgumentException or YamlDotNet.Core.YamlException or IOException)
{
    Console.Error.WriteLine($"configuration [{configFile}]: {ex.Message}");
    return 1;
}

var host = await GraphbaseHost.StartAsync(settings);
if (host is null)
    return 1;
await host.WaitForShutdownAsync();
await host.DisposeAsync();
return 0;
