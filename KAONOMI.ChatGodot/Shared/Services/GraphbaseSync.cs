using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>
/// Keeps KAONOMI.graphbase fed with the ontology and the relations of the server (<see cref="KnowledgeGraph"/>): a change
/// made in the chat (an entity created or changed, a relation saved or deleted, through
/// <see cref="KaonomiAssistantService"/>) is written at once; the whole of them when <c>/ontology</c> or
/// <c>/relation</c> opens (once every 10 minutes at most) and with ⇄ SYNC of <c>/graphbase</c>. Nothing is done while
/// graphbase does not run; one write at a time.
/// </summary>
public static class GraphbaseSync
{
    private static readonly TimeSpan AutoEvery = TimeSpan.FromMinutes(10);
    /// <summary>The most entities and relations asked to the server: a list this long may be cut (nothing is removed then).</summary>
    private const int MaxItems = 10000;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static GraphbaseClient? _client;
    private static KaonomiAssistantService? _server;
    private static DateTime _lastWhole = DateTime.MinValue;
    private static bool _listening;

    /// <summary>Starts following the changes (once), with the graphbase of <paramref name="graphbaseUrl"/> and the server of <paramref name="server"/>.</summary>
    public static void Start(string graphbaseUrl, KaonomiAssistantService server)
    {
        _client = new GraphbaseClient(graphbaseUrl);
        _server = server;
        if (_listening)
            return;
        _listening = true;
        KaonomiAssistantService.OntologySaved += entry => _ = RunAsync($"entity {entry.Name}", (client, ct) => KnowledgeGraph.WriteOntologyAsync(client, entry, ct));
        KaonomiAssistantService.RelationSaved += relation => _ = RunAsync($"relation {relation.Id}", (client, ct) => KnowledgeGraph.WriteRelationAsync(client, relation, ct));
        KaonomiAssistantService.RelationDeleted += id => _ = RunAsync($"relation {id} removed", (client, ct) => KnowledgeGraph.RemoveRelationAsync(client, id, ct));
    }

    /// <summary>
    /// The whole ontology and every relation of the server written to graphbase: what it did, or why not (null: not
    /// done, the last one is too recent, unless <paramref name="manual"/>).
    /// </summary>
    public static async Task<string?> SyncKnowledgeAsync(bool manual, CancellationToken ct = default)
    {
        if (_client is not { } client || _server is not { } server)
            return manual ? "The synchronisation with graphbase is not started" : null;
        if (!manual && DateTime.UtcNow - _lastWhole < AutoEvery)
            return null;
        try
        {
            if (!await client.RunningAsync(ct))
                return manual ? $"KAONOMI.graphbase does not answer at {client.Url}: start it from /process" : null;
            _lastWhole = DateTime.UtcNow;
            var ontology = await server.GetLastOntologiesAsync("*", MaxItems, ct);
            var relations = await server.SearchRelationsAsync("*", MaxItems, ct);
            var complete = ontology.Count < MaxItems && relations.Count < MaxItems;
            await Gate.WaitAsync(ct);
            try
            {
                var result = await KnowledgeGraph.SyncAsync(client, ontology, relations, complete, ct);
                var summary = result.Summary + (complete ? "" : $" (the server gave {MaxItems} items or more: nothing was removed)");
                if (manual || result.Changed)
                    ActivityLog.Info("Graphbase", $"Ontology and relations written to graphbase ({client.Url}): {summary}");
                return summary;
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is GraphbaseException or HttpRequestException or JsonException or IOException or OperationCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            ActivityLog.Error("Graphbase", $"Ontology and relations not written to graphbase ({client.Url}): {ex.Message}");
            return $"Not written to graphbase: {ex.Message}";
        }
    }

    /// <summary>A change written to graphbase when it runs (nothing said when it does not).</summary>
    private static async Task RunAsync(string what, Func<GraphbaseClient, CancellationToken, Task> write)
    {
        if (_client is not { } client)
            return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (!await client.RunningAsync(timeout.Token))
                return;
            await Gate.WaitAsync(timeout.Token);
            try
            {
                await write(client, timeout.Token);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (Exception ex) when (ex is GraphbaseException or HttpRequestException or JsonException or IOException or OperationCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            ActivityLog.Error("Graphbase", $"{what} not written to graphbase ({client.Url}): {ex.Message}");
        }
    }
}
