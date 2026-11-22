using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace KAONOMI.graphbase.Configuration;

/// <summary>A user allowed to call the server when the security is on (basic authentication).</summary>
public sealed record GraphUser(string Name, string Password);

/// <summary>
/// The settings of the server, read from <c>config/graphbase.yml</c>: the keys can be written nested
/// (<c>http: { port: 4000 }</c>) or dotted (<c>http.port: 4000</c>), and <c>--set key=value</c> on the command line
/// overrides them.
/// </summary>
public sealed class GraphSettings
{
    public const string Version = "1.0.0";

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>The users of <c>security.users</c> (name and password).</summary>
    public List<GraphUser> Users { get; } = new();

    /// <summary>The file the settings were read from, or null (defaults only).</summary>
    public string? File { get; private set; }

    public string Host => Get("network.host", "localhost");
    public int Port => GetInt("http.port", 4000);
    /// <summary>Plain HTTP only: graphbase has no HTTPS any more (an https.* key left in a config is ignored).</summary>
    public bool HttpsEnabled => false;
    /// <summary>By default <c>~/kaonomi/graphbase</c>, created on the first start.</summary>
    public string DataPath => ResolvePath(Get("path.data", "~/kaonomi/graphbase"));
    public string? LogsPath => _values.TryGetValue("path.logs", out var logs) && logs.Length > 0 ? ResolvePath(logs) : null;
    public string GraphQLPath => "/" + Get("graphql.path", "/graphql").Trim('/');
    public bool Ide => GetBool("graphql.ide", true);
    public int MaxDepth => Math.Max(2, GetInt("graphql.max_depth", 20));
    public int MaxResults => Math.Max(1, GetInt("graphql.max_results", 10000));
    public int MaxHops => Math.Clamp(GetInt("graphql.max_hops", 10), 1, 50);
    public bool SecurityEnabled => GetBool("security.enabled", false);
    public bool LogRequests => GetBool("log.requests", false);

    /// <summary>The scheme the server is reached with (<c>https</c> unless <c>https.enabled: false</c>).</summary>
    public string Scheme => HttpsEnabled ? "https" : "http";

    /// <summary>The relative paths (data, logs, certificate) are relative to the folder of the settings file.</summary>
    public string BaseFolder => File is null ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetFullPath(File))!;

    /// <summary>A path of the settings: <c>~</c> is the home folder of the user, a relative path is relative to <see cref="BaseFolder"/>.</summary>
    private string ResolvePath(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : string.Empty);
        return Path.GetFullPath(path, BaseFolder);
    }

    /// <summary>Reads <paramref name="file"/> (when it exists), then applies the <c>key=value</c> overrides.</summary>
    public static GraphSettings Load(string? file, IEnumerable<string> overrides)
    {
        var settings = new GraphSettings();
        if (file is not null && System.IO.File.Exists(file))
        {
            settings.File = file;
            var yaml = new YamlStream();
            using (var reader = new StreamReader(file))
                yaml.Load(reader);
            if (yaml.Documents.Count > 0 && yaml.Documents[0].RootNode is YamlMappingNode root)
                settings.Flatten(root, string.Empty);
        }
        foreach (var item in overrides)
        {
            var equal = item.IndexOf('=');
            if (equal <= 0)
                throw new ArgumentException($"--set expects key=value, not [{item}]");
            settings._values[item[..equal].Trim()] = item[(equal + 1)..].Trim();
        }
        return settings;
    }

    private void Flatten(YamlMappingNode node, string prefix)
    {
        foreach (var (keyNode, valueNode) in node.Children)
        {
            var key = prefix + ((YamlScalarNode)keyNode).Value;
            switch (valueNode)
            {
                case YamlMappingNode map:
                    Flatten(map, key + ".");
                    break;
                case YamlSequenceNode list when key == "security.users":
                    foreach (var user in list.Children.OfType<YamlMappingNode>())
                    {
                        string? Field(string name) => user.Children.TryGetValue(new YamlScalarNode(name), out var v) ? ((YamlScalarNode)v).Value : null;
                        var name = Field("username") ?? Field("name");
                        if (!string.IsNullOrEmpty(name))
                            Users.Add(new GraphUser(name, Field("password") ?? string.Empty));
                    }
                    break;
                case YamlSequenceNode list:
                    _values[key] = string.Join(",", list.Children.OfType<YamlScalarNode>().Select(s => s.Value));
                    break;
                case YamlScalarNode scalar:
                    _values[key] = scalar.Value ?? string.Empty;
                    break;
            }
        }
    }

    public string Get(string key, string fallback) => _values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    public bool GetBool(string key, bool fallback) =>
        _values.TryGetValue(key, out var value) && value.Length > 0 ? value.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1" : fallback;

    public int GetInt(string key, int fallback) =>
        _values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
}
