using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KAONOMI.Chat.Services;

/// <summary>What KAONOMI.graphbase refused, with its code (<c>NOT_FOUND</c>, <c>INVALID_ARGUMENT</c>…) when it gave one.</summary>
public sealed class GraphbaseException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

/// <summary>
/// The client of the GraphQL API of KAONOMI.graphbase (<c>graphbaseUrl</c> in config.yml,
/// http://localhost:4000/graphql), in plain HTTP.
/// </summary>
public sealed class GraphbaseClient(string url)
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new(StringComparer.Ordinal);

    public string Url { get; } = url;

    private HttpClient Http => Clients.GetOrAdd(Url, address =>
        new HttpClient(HttpHandlers.Create(Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)) { Timeout = TimeSpan.FromSeconds(60) });

    /// <summary>True when something listens on the port of <see cref="Url"/>.</summary>
    public async Task<bool> RunningAsync(CancellationToken cancellationToken) =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && await EngineShutdown.PortOpenAsync(uri.Host, uri.Port, cancellationToken).ConfigureAwait(false);

    /// <summary>The GraphQL schema of graphbase as SDL (<c>GET …/graphql?sdl</c>).</summary>
    public async Task<string> SchemaAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(Url + "?sdl", cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || !text.Contains("type Query", StringComparison.Ordinal))
            throw new GraphbaseException($"{Url}?sdl answered HTTP {(int)response.StatusCode}, not the schema");
        return text;
    }

    /// <summary>
    /// Sends a request as it is: the whole answer (<c>data</c> and <c>errors</c>) and its HTTP status, for the GraphQL
    /// editor of <c>/graphbase</c>; a <see cref="GraphbaseException"/> only when the answer is not JSON.
    /// </summary>
    public async Task<(int Status, JsonElement Body)> SendAsync(string query, object? variables, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsJsonAsync(Url, new { query, variables }, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(text);
            return ((int)response.StatusCode, document.RootElement.Clone());
        }
        catch (JsonException)
        {
            throw new GraphbaseException($"{Url} answered HTTP {(int)response.StatusCode}, not GraphQL: {Start(text)}");
        }
    }

    /// <summary>Runs a query or a mutation; its <c>data</c>, or a <see cref="GraphbaseException"/> with the first error.</summary>
    public async Task<JsonElement> RunAsync(string query, object? variables, CancellationToken cancellationToken)
    {
        var (status, body) = await SendAsync(query, variables, cancellationToken).ConfigureAwait(false);
        var text = body.GetRawText();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            var message = first.TryGetProperty("message", out var m) ? m.GetString() ?? "error" : "error";
            var code = first.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object
                       && extensions.TryGetProperty("code", out var c) ? c.GetString() : null;
            throw new GraphbaseException(message, code);
        }
        if (status is < 200 or > 299 || body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new GraphbaseException($"{Url} answered HTTP {status} without data: {Start(text)}");
        return data.Clone();
    }

    private static string Start(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
