using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>An item of a tool as <see cref="LocalSync"/> moves it: its id, what it is, what the searches read, the item itself (JSON).</summary>
public sealed record SyncItem(string Id, string Type, string Title, string Text, string Data);

/// <summary>
/// What <see cref="LocalSync"/> needs of a tool: its items as they are on this machine, and how to write the ones that
/// came from the search engine (changed or deleted on another machine). Called on the thread of the windows.
/// </summary>
public interface ISyncAdapter
{
    /// <summary>The index of the tool on the server (calendarevent, archiitem…).</summary>
    string Kind { get; }

    /// <summary>The name of the tool, for the log (Calendar, Archi…).</summary>
    string Label { get; }

    /// <summary>
    /// The items of this machine; null when they cannot be read now (a file that cannot be read: nothing is decided on
    /// it, above all not that everything was deleted).
    /// </summary>
    IReadOnlyList<SyncItem>? ReadLocal();

    /// <summary>
    /// False when this machine has no copy at all (its file or folder is not there: a new machine, a file deleted by
    /// hand): the items of the server are written here, none deleted there. An empty copy that is there is a copy whose
    /// items were all deleted.
    /// </summary>
    bool LocalExists();

    /// <summary>When the items of this machine were written last (UTC; <see cref="DateTime.MinValue"/>: never).</summary>
    DateTime LocalModified();

    /// <summary>Writes the items that came from the search engine (<paramref name="upserts"/>) and deletes <paramref name="deletes"/>.</summary>
    void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes);
}

/// <summary>
/// The store of /calendar, /archi, /todo, /support, /obs and /entity: is the search engine (one index per tool, the
/// <c>SyncedRecord</c> of the server); each machine keeps a copy of it, its files as before, to work without the
/// server. Each change made here is written there (a few seconds after it), and what another machine changed comes
/// here: the whole of each tool is compared 20 s after the chat starts, then every 2 minutes. Each item carries a
/// revision (a new one at each save); what this machine knows of each is kept in <c>~/kaonomi/sync/&lt;kind&gt;.json</c>
/// (its hash and its revision when they were the same here and there): changed here only, it is written there; changed
/// there only, it is written here; changed on both sides, the newest wins (told in the log). A deletion goes the same
/// way (the item stays on the server, marked deleted). A copy that cannot be read, or that is gone while items were
/// known, is never taken for a deletion of everything: the items of the server are written back here.
/// </summary>
public static class LocalSync
{
    private static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AfterChange = TimeSpan.FromSeconds(3);
    /// <summary>The most items of a tool asked to the server at once (the limit of the search engine).</summary>
    private const int MaxItems = 10000;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, ISyncAdapter> Adapters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CancellationTokenSource> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };
    private static KaonomiAssistantService? _server;
    private static SynchronizationContext? _ui;
    private static bool _started;
    private static bool _toldOffline;
    /// <summary>
    /// The revisions this chat wrote: one of them read back while it is not the last one known is an old state seen
    /// late by the search engine, not a change of another machine.
    /// </summary>
    private static readonly HashSet<string> Mine = new(StringComparer.Ordinal);

    /// <summary>Raised (on the thread of the windows) when items that came from the search engine were written here.</summary>
    public static event Action<string>? Applied;

    /// <summary>The folder of what this machine knows of each item.</summary>
    public static string StateFolder => Path.Combine(ChatSettings.KaonomiHome, "sync");

    /// <summary>The tools followed.</summary>
    public static IReadOnlyCollection<ISyncAdapter> Tools => Adapters.Values;

    /// <summary>Adds a tool (before or after <see cref="Start"/>).</summary>
    public static void Register(ISyncAdapter adapter) => Adapters[adapter.Kind] = adapter;

    /// <summary>
    /// Starts (once) with the server of <paramref name="server"/>; to be called on the thread of the windows (the items
    /// of the tools are read and written there). Every tool compared after 20 s, then every 2 minutes.
    /// </summary>
    public static void Start(KaonomiAssistantService server, bool loop = true)
    {
        _server = server;
        _ui ??= SynchronizationContext.Current;
        if (_started || !loop)
            return;
        _started = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(FirstAfter);
            while (true)
            {
                await SyncAllAsync();
                await Task.Delay(Every);
            }
        });
    }

    /// <summary>The items of <paramref name="kind"/> changed here: written to the search engine a few seconds later (once for several changes).</summary>
    public static void Touch(string kind)
    {
        if (_server is null || !Adapters.ContainsKey(kind))
            return;
        CancellationTokenSource next;
        lock (Pending)
        {
            if (Pending.TryGetValue(kind, out var before))
                before.Cancel();
            Pending[kind] = next = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AfterChange, next.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await SyncAsync(kind);
        });
    }

    /// <summary>Every tool compared with the search engine; what was done (empty: nothing).</summary>
    public static async Task<string> SyncAllAsync(CancellationToken ct = default)
    {
        var done = new List<string>();
        foreach (var kind in Adapters.Keys.ToList())
            if (await SyncAsync(kind, ct) is { Length: > 0 } summary)
                done.Add(summary);
        return string.Join("; ", done);
    }

    /// <summary>
    /// One tool compared with the search engine (see <see cref="LocalSync"/>); what was done ("": nothing to do, null:
    /// not done: the server does not answer, or the items of this machine cannot be read).
    /// </summary>
    public static async Task<string?> SyncAsync(string kind, CancellationToken ct = default)
    {
        if (_server is not { } server || !Adapters.TryGetValue(kind, out var adapter))
            return null;
        await Gate.WaitAsync(ct);
        try
        {
            var remote = (await server.GetSyncedAllAsync(kind, MaxItems, ct))
                .Where(r => r.Id.Length > 0)
                .GroupBy(r => r.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Date).First(), StringComparer.Ordinal);
            var read = await OnUiAsync(() => (adapter.ReadLocal(), adapter.LocalModified(), adapter.LocalExists()));
            if (read.Item1 is not { } localItems)
            {
                ActivityLog.Error("Sync", $"{adapter.Label}: the copy of this machine cannot be read: nothing done");
                return null;
            }
            var localStamp = read.Item2;
            var local = localItems.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var state = LoadState(kind);
            // No copy on this machine (a file deleted by hand, a new machine) while items were known: the items of the
            // server come back, nothing is deleted there.
            if (!read.Item3 && local.Count == 0 && state.Values.Any(s => s.Hash is not null))
            {
                ActivityLog.Info("Sync", $"{adapter.Label}: the copy of this machine is empty: the items of the search engine are written back");
                state.Clear();
            }

            int pushed = 0, pulledIn = 0, deletedHere = 0, deletedThere = 0, conflicts = 0;
            var upserts = new List<SyncItem>();
            var deletes = new List<string>();
            var pulledIds = new List<string>();
            foreach (var id in local.Keys.Concat(remote.Keys).Concat(state.Keys).Distinct(StringComparer.Ordinal).ToList())
            {
                ct.ThrowIfCancellationRequested();
                state.TryGetValue(id, out var known);
                local.TryGetValue(id, out var here);
                remote.TryGetValue(id, out var there);
                var hereHash = here is null ? null : Hash(here.Data);
                var thereItem = there is { IsDeleted: false } ? Decode(there) : null;
                var thereHash = thereItem is null ? null : Hash(thereItem.Data);

                // The same on both sides (or gone on both): only what is known changes.
                if (hereHash == thereHash)
                {
                    if (hereHash is null && there is null)
                        state.Remove(id);
                    else
                        state[id] = new KnownItem(hereHash, there?.Revision ?? known?.Revision ?? string.Empty);
                    continue;
                }
                var changedHere = known is null ? here is not null : hereHash != known.Hash;
                // Gone from the server while it was known there: written there again.
                var changedThere = known is null ? there is not null : there is not null && there.Revision != known.Revision;
                if (changedThere && known is not null && there is not null && Mine.Contains(there.Revision))
                {
                    // An older state written here, read late: nothing changed there.
                    continue;
                }
                if (known is not null && there is null && known.Hash is not null)
                    changedHere = true;

                var push = changedHere && !changedThere;
                var pull = changedThere && !changedHere;
                if (changedHere && changedThere)
                {
                    conflicts++;
                    pull = (there?.Date.UtcDateTime ?? DateTime.MinValue) > localStamp;
                    push = !pull;
                    ActivityLog.Info("Sync", $"{adapter.Label}: « {here?.Title ?? thereItem?.Title ?? id} » changed here and on {there?.MachineName ?? "the server"}: the {(pull ? "server's" : "one of this machine")}, the newest, is kept");
                }

                if (push)
                {
                    if (here is null && there is null)
                    {
                        state.Remove(id);
                        continue;
                    }
                    var revision = Guid.NewGuid().ToString("N");
                    var record = here is not null ? Encode(here, revision) : Tombstone(there!, revision);
                    var result = await server.SaveSyncedAsync(kind, record, ct);
                    if (!result.IsSuccess)
                    {
                        ActivityLog.Error("Sync", $"{adapter.Label}: « {record.Title} » not written to the search engine: {result.Message}");
                        continue;
                    }
                    state[id] = new KnownItem(hereHash, revision);
                    lock (Mine)
                        Mine.Add(revision);
                    if (here is null)
                        deletedThere++;
                    else
                        pushed++;
                    SaveState(kind, state);
                }
                else if (pull)
                {
                    if (thereItem is null)
                    {
                        if (here is not null)
                        {
                            deletes.Add(id);
                            deletedHere++;
                        }
                    }
                    else
                    {
                        upserts.Add(thereItem);
                        pulledIn++;
                    }
                    pulledIds.Add(id);
                    state[id] = new KnownItem(thereHash, there!.Revision);
                }
            }

            if (upserts.Count + deletes.Count > 0)
            {
                // Written here, then read again: the hash known is the one of the copy as this machine writes it.
                var after = await OnUiAsync(() =>
                {
                    adapter.WriteLocal(upserts, deletes);
                    Applied?.Invoke(kind);
                    return adapter.ReadLocal();
                });
                if (after is not null)
                {
                    var now = after.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    foreach (var id in pulledIds)
                        if (state.TryGetValue(id, out var k))
                            state[id] = k with { Hash = now.TryGetValue(id, out var item) ? Hash(item.Data) : null };
                }
            }
            SaveState(kind, state);
            _toldOffline = false;
            if (pushed + pulledIn + deletedHere + deletedThere == 0)
                return string.Empty;
            var summary = $"{adapter.Label}: {pushed} written to the search engine, {deletedThere} deleted there, {pulledIn} from it, {deletedHere} deleted here"
                          + (conflicts > 0 ? $", {conflicts} changed on both sides (the newest kept)" : string.Empty);
            ActivityLog.Info("Sync", summary);
            return summary;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (!_toldOffline)
            {
                _toldOffline = true;
                ActivityLog.Info("Sync", $"The search engine does not answer ({ex.Message}): the tools work on the copy of this machine, written there when it answers again");
            }
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    // ── The items as the server keeps them ──

    private static SyncedRecordEntry Encode(SyncItem item, string revision) => new()
    {
        Id = item.Id,
        Date = DateTimeOffset.UtcNow,
        AppName = "KAONOMI.Chat",
        MachineName = Environment.MachineName,
        UserName = Environment.UserName,
        Title = item.Title.Trim().Length > 0 ? item.Title.Trim() : item.Id,
        Type = item.Type,
        Text = item.Text,
        Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Data)),
        Revision = revision
    };

    private static SyncedRecordEntry Tombstone(SyncedRecordEntry there, string revision) => new()
    {
        Id = there.Id,
        Date = DateTimeOffset.UtcNow,
        AppName = "KAONOMI.Chat",
        MachineName = Environment.MachineName,
        UserName = Environment.UserName,
        Title = there.Title,
        Type = there.Type,
        Text = string.Empty,
        Payload = string.Empty,
        Revision = revision,
        IsDeleted = true
    };

    private static SyncItem? Decode(SyncedRecordEntry record)
    {
        try
        {
            return new SyncItem(record.Id, record.Type, record.Title, record.Text, Encoding.UTF8.GetString(Convert.FromBase64String(record.Payload)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The hash of an item (its JSON as this machine writes it).</summary>
    public static string Hash(string data) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.Replace("\r\n", "\n"))));

    // ── What this machine knows of each item ──

    /// <summary>An item as it was the same here and on the server: its hash (null: deleted) and its revision.</summary>
    public sealed record KnownItem(string? Hash, string Revision);

    private static string StateFile(string kind) => Path.Combine(StateFolder, kind + ".json");

    private static Dictionary<string, KnownItem> LoadState(string kind)
    {
        try
        {
            if (File.Exists(StateFile(kind)))
                return JsonSerializer.Deserialize<Dictionary<string, KnownItem>>(File.ReadAllText(StateFile(kind)))
                       is { } state ? new Dictionary<string, KnownItem>(state, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            ActivityLog.Error("Sync", $"{StateFile(kind)} cannot be read: everything is compared again", ex);
        }
        return new Dictionary<string, KnownItem>(StringComparer.Ordinal);
    }

    private static void SaveState(string kind, Dictionary<string, KnownItem> state)
    {
        Directory.CreateDirectory(StateFolder);
        var temporary = StateFile(kind) + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, StateJson));
        File.Move(temporary, StateFile(kind), overwrite: true);
    }

    /// <summary>Runs <paramref name="work"/> on the thread of the windows (inline when there is none: tests, start).</summary>
    private static Task<T> OnUiAsync<T>(Func<T> work)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            return Task.FromResult(work());
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(_ =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }, null);
        return done.Task;
    }
}
