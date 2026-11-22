using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The tools whose store is the search engine, each with its copy on this machine (<see cref="LocalSync"/>): how each
/// reads its items, writes those that came from the server, and says when it changed.
/// </summary>
public static class SyncAdapters
{
    /// <summary>The JSON of the items as they travel (the same on every machine: their hash compares them).</summary>
    internal static readonly JsonSerializerOptions ItemJson = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Every tool of the chat registered with <see cref="LocalSync"/>.</summary>
    public static void RegisterAll()
    {
        LocalSync.Register(new CalendarSync());
        LocalSync.Register(new ArchiSync());
        LocalSync.Register(new TodoSync());
        LocalSync.Register(new SupportSync());
        LocalSync.Register(new VaultSync());
        LocalSync.Register(new EntityAnalysisSync());
    }

    internal static DateTime Modified(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    internal static string Lines(params string?[] parts) =>
        string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>A file written beside, then put in place: a stop in the middle never leaves half a file.</summary>
    internal static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".sync.tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The list of <typeparamref name="T"/> of a JSON file: empty when there is none, null when it cannot be read.</summary>
    internal static List<T>? ReadList<T>(string path, JsonSerializerOptions options)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), options) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ActivityLog.Error("Sync", $"{path} cannot be read", ex);
            return null;
        }
    }
}

/// <summary>/calendar: its events (the reminders rung or snoozed stay the business of each machine).</summary>
public sealed class CalendarSync : ISyncAdapter
{
    public string Kind => "calendarevent";
    public string Label => "Calendar";

    public CalendarSync() => CalendarStore.Shared.Changed += () => LocalSync.Touch(Kind);

    private static string FilePath => CalendarStore.Shared.FilePath;

    public bool LocalExists() => File.Exists(FilePath);

    public DateTime LocalModified() => SyncAdapters.Modified(FilePath);

    public IReadOnlyList<SyncItem>? ReadLocal()
    {
        // A file there that cannot be read: nothing decided (the store would show it empty).
        if (File.Exists(FilePath) && SyncAdapters.ReadList<CalendarEvent>(FilePath, CalendarStore.FileJson) is null)
            return null;
        return CalendarStore.Shared.Events.Select(e =>
        {
            var copy = CalendarStore.Copy(e);
            copy.LastReminded = null;
            copy.SnoozedUntil = null;
            var when = e.AllDay ? $"{e.Start:yyyy-MM-dd} (all day)" : $"{e.Start:yyyy-MM-dd HH:mm}, {e.Duration:hh\\:mm}";
            return new SyncItem(e.Id.ToString(), "event", e.Title,
                SyncAdapters.Lines(e.Title, when, e.Repeat != CalendarRepeat.None ? $"Repeat: {e.Repeat}" : null, e.Notes),
                JsonSerializer.Serialize(copy, SyncAdapters.ItemJson));
        }).ToList();
    }

    public void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes) =>
        CalendarStore.Shared.ApplySync(
            upserts.Select(u => JsonSerializer.Deserialize<CalendarEvent>(u.Data, SyncAdapters.ItemJson)).OfType<CalendarEvent>().ToList(),
            deletes.Select(d => Guid.TryParse(d, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList());
}

/// <summary>/archi: its fact sheets, its relations and the name of the repository, each an item.</summary>
public sealed class ArchiSync : ISyncAdapter
{
    public string Kind => "archiitem";
    public string Label => "Archi";

    private const string SheetPrefix = "sheet:";
    private const string RelationPrefix = "relation:";
    private const string NameId = "workspace";

    public bool LocalExists() => File.Exists(ArchiWindowViewModel.FilePath);

    public DateTime LocalModified() => SyncAdapters.Modified(ArchiWindowViewModel.FilePath);

    private static ArchiWorkspace? Read()
    {
        try
        {
            return File.Exists(ArchiWindowViewModel.FilePath)
                ? JsonSerializer.Deserialize<ArchiWorkspace>(File.ReadAllText(ArchiWindowViewModel.FilePath), ArchiWindowViewModel.FileJson) ?? new ArchiWorkspace()
                : new ArchiWorkspace { Sheets = [], Relations = [] };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ActivityLog.Error("Sync", $"{ArchiWindowViewModel.FilePath} cannot be read", ex);
            return null;
        }
    }

    public IReadOnlyList<SyncItem>? ReadLocal()
    {
        if (Read() is not { } workspace)
            return null;
        if (!File.Exists(ArchiWindowViewModel.FilePath))
            return [];
        var names = workspace.Sheets.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Name);
        var items = new List<SyncItem> { new(NameId, "workspace", workspace.Name, workspace.Name, JsonSerializer.Serialize(workspace.Name, SyncAdapters.ItemJson)) };
        foreach (var sheet in workspace.Sheets)
            items.Add(new SyncItem(SheetPrefix + sheet.Id, "sheet:" + sheet.Type, sheet.Name,
                SyncAdapters.Lines(sheet.Name, sheet.Type.ToString(), sheet.Description, sheet.Owner.Length > 0 ? "Owner: " + sheet.Owner : null, sheet.Tags,
                    string.Join("\n", sheet.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => $"{f.Key}: {f.Value}"))),
                JsonSerializer.Serialize(sheet, SyncAdapters.ItemJson)));
        foreach (var relation in workspace.Relations)
            items.Add(new SyncItem(RelationPrefix + relation.Id, "relation",
                $"{names.GetValueOrDefault(relation.From, relation.From)} {relation.Kind} {names.GetValueOrDefault(relation.To, relation.To)}",
                SyncAdapters.Lines($"{names.GetValueOrDefault(relation.From, relation.From)} {relation.Kind} {names.GetValueOrDefault(relation.To, relation.To)}", relation.Description),
                JsonSerializer.Serialize(relation, SyncAdapters.ItemJson)));
        return items;
    }

    public void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes)
    {
        var workspace = Read() ?? throw new IOException($"{ArchiWindowViewModel.FilePath} cannot be read");
        foreach (var id in deletes)
        {
            if (id.StartsWith(SheetPrefix, StringComparison.Ordinal))
                workspace.Sheets.RemoveAll(s => s.Id == id[SheetPrefix.Length..]);
            else if (id.StartsWith(RelationPrefix, StringComparison.Ordinal))
                workspace.Relations.RemoveAll(r => r.Id == id[RelationPrefix.Length..]);
        }
        foreach (var item in upserts)
        {
            if (item.Id == NameId)
                workspace.Name = JsonSerializer.Deserialize<string>(item.Data, SyncAdapters.ItemJson) ?? workspace.Name;
            else if (item.Id.StartsWith(SheetPrefix, StringComparison.Ordinal) && JsonSerializer.Deserialize<ArchiSheet>(item.Data, SyncAdapters.ItemJson) is { } sheet)
            {
                var at = workspace.Sheets.FindIndex(s => s.Id == sheet.Id);
                if (at >= 0)
                    workspace.Sheets[at] = sheet;
                else
                    workspace.Sheets.Add(sheet);
            }
            else if (item.Id.StartsWith(RelationPrefix, StringComparison.Ordinal) && JsonSerializer.Deserialize<ArchiRelation>(item.Data, SyncAdapters.ItemJson) is { } relation)
            {
                var at = workspace.Relations.FindIndex(r => r.Id == relation.Id);
                if (at >= 0)
                    workspace.Relations[at] = relation;
                else
                    workspace.Relations.Add(relation);
            }
        }
        SyncAdapters.WriteFile(ArchiWindowViewModel.FilePath, JsonSerializer.Serialize(workspace, ArchiWindowViewModel.FileJson));
    }
}

/// <summary>/todo: the tasks of the day (those marked deleted too: a deletion is a change of the task).</summary>
public sealed class TodoSync : ISyncAdapter
{
    public string Kind => "daytodo";
    public string Label => "Todo";

    public TodoSync() => DayTodoStore.Changed += () => LocalSync.Touch(Kind);

    public bool LocalExists() => File.Exists(DayTodoStore.FilePath);

    public DateTime LocalModified() => SyncAdapters.Modified(DayTodoStore.FilePath);

    public IReadOnlyList<SyncItem>? ReadLocal() =>
        SyncAdapters.ReadList<DayTodo>(DayTodoStore.FilePath, DayTodoStore.FileJson)?.Select(t => new SyncItem(t.Id, t.IsDeleted ? "todo:deleted" : "todo:" + t.Status, t.Title,
            SyncAdapters.Lines(t.Title, $"{t.Day} · {t.Status}", t.Text), JsonSerializer.Serialize(t, SyncAdapters.ItemJson))).ToList();

    public void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes) =>
        DayTodoStore.Change(all =>
        {
            all.RemoveAll(t => deletes.Contains(t.Id));
            foreach (var todo in upserts.Select(u => JsonSerializer.Deserialize<DayTodo>(u.Data, SyncAdapters.ItemJson)).OfType<DayTodo>())
            {
                var at = all.FindIndex(t => t.Id == todo.Id);
                if (at >= 0)
                    all[at] = todo;
                else
                    all.Add(todo);
            }
        });
}

/// <summary>/support: the requests for support.</summary>
public sealed class SupportSync : ISyncAdapter
{
    public string Kind => "supportticket";
    public string Label => "Support";

    public bool LocalExists() => File.Exists(SupportWindowViewModel.FilePath);

    public DateTime LocalModified() => SyncAdapters.Modified(SupportWindowViewModel.FilePath);

    public IReadOnlyList<SyncItem>? ReadLocal() =>
        SyncAdapters.ReadList<SupportTicket>(SupportWindowViewModel.FilePath, SupportWindowViewModel.FileJson)?.Select(t => new SyncItem(t.Id, "ticket:" + t.Category, $"{t.Id} · {t.Title}",
            SyncAdapters.Lines(t.Title, $"{t.Category} · {t.Priority} · {t.Requester}", t.Description, string.Join(", ", t.Products)),
            JsonSerializer.Serialize(t, SyncAdapters.ItemJson))).ToList();

    public void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes)
    {
        var all = SyncAdapters.ReadList<SupportTicket>(SupportWindowViewModel.FilePath, SupportWindowViewModel.FileJson)
                  ?? throw new IOException($"{SupportWindowViewModel.FilePath} cannot be read");
        all.RemoveAll(t => deletes.Contains(t.Id));
        foreach (var ticket in upserts.Select(u => JsonSerializer.Deserialize<SupportTicket>(u.Data, SyncAdapters.ItemJson)).OfType<SupportTicket>())
        {
            var at = all.FindIndex(t => t.Id == ticket.Id);
            if (at >= 0)
                all[at] = ticket;
            else
                all.Add(ticket);
        }
        SyncAdapters.WriteFile(SupportWindowViewModel.FilePath, JsonSerializer.Serialize(all.OrderByDescending(t => t.Created).ToList(), SupportWindowViewModel.FileJson));
    }
}

/// <summary>
/// /obs: the Markdown notes of the vault of the chat (<c>~/kaonomi/vault</c>; another vault chosen in /obs stays a
/// folder of this machine), each note an item, its id its path in the vault; the hidden folders (.trash, .obsidian)
/// left out. A note deleted on another machine goes to the .trash of the vault here (nothing is erased).
/// </summary>
public sealed class VaultSync : ISyncAdapter
{
    public string Kind => "vaultnote";
    public string Label => "Obs";

    private static string Folder => VaultWindowViewModel.DefaultVault;

    public bool LocalExists() => Directory.Exists(Folder);

    public DateTime LocalModified()
    {
        if (!Directory.Exists(Folder))
            return DateTime.MinValue;
        var latest = Directory.GetLastWriteTimeUtc(Folder);
        foreach (var file in Directory.EnumerateFiles(Folder, "*.md", SearchOption.AllDirectories))
            latest = new[] { latest, File.GetLastWriteTimeUtc(file) }.Max();
        return latest;
    }

    private static string? IdOf(string file)
    {
        var relative = Path.GetRelativePath(Folder, file);
        return relative.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')) ? null : relative.Replace('\\', '/');
    }

    private static string PathOf(string id)
    {
        var path = Path.GetFullPath(Path.Combine(Folder, id.Replace('/', Path.DirectorySeparatorChar)));
        // Never outside the vault (an id with ..).
        if (!path.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new IOException($"The note {id} is not a note of the vault");
        return path;
    }

    public IReadOnlyList<SyncItem>? ReadLocal()
    {
        if (!Directory.Exists(Folder))
            return [];
        try
        {
            var items = new List<SyncItem>();
            foreach (var file in Directory.EnumerateFiles(Folder, "*.md", SearchOption.AllDirectories))
            {
                if (IdOf(file) is not { } id)
                    continue;
                var text = File.ReadAllText(file);
                items.Add(new SyncItem(id, "note", Path.GetFileNameWithoutExtension(file), text, text));
            }
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Sync", $"The vault {Folder} cannot be read", ex);
            return null;
        }
    }

    public void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes)
    {
        foreach (var id in deletes)
        {
            var path = PathOf(id);
            if (!File.Exists(path))
                continue;
            var trash = Path.Combine(Folder, ".trash");
            Directory.CreateDirectory(trash);
            var target = Path.Combine(trash, Path.GetFileName(path));
            for (var i = 2; File.Exists(target); i++)
                target = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(path)} {i}.md");
            File.Move(path, target);
        }
        foreach (var item in upserts)
            SyncAdapters.WriteFile(PathOf(item.Id), item.Data);
    }
}

/// <summary>/entity: the analyses of each entity (one file each in <c>~/kaonomi/entity-analyses</c>), each an item.</summary>
public sealed class EntityAnalysisSync : ISyncAdapter
{
    public string Kind => "entityanalysis";
    public string Label => "Entity analyses";

    public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "entity-analyses");

    public bool LocalExists() => Directory.Exists(Folder);

    public DateTime LocalModified()
    {
        if (!Directory.Exists(Folder))
            return DateTime.MinValue;
        var latest = Directory.GetLastWriteTimeUtc(Folder);
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
            latest = new[] { latest, File.GetLastWriteTimeUtc(file) }.Max();
        return latest;
    }

    public IReadOnlyList<SyncItem>? ReadLocal()
    {
        if (!Directory.Exists(Folder))
            return [];
        try
        {
            var items = new List<SyncItem>();
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var json = File.ReadAllText(file);
                items.Add(new SyncItem(name, "analysis", name, Summaries(name, json), json));
            }
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Sync", $"{Folder} cannot be read", ex);
            return null;
        }
    }

    /// <summary>What the searches read of the analyses of an entity: its name, the overview, the summary of each day.</summary>
    private static string Summaries(string name, string json)
    {
        var text = new StringBuilder(name);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Overview", out var overview) && overview.ValueKind == JsonValueKind.Object
                && overview.TryGetProperty("Overview", out var o) && o.ValueKind == JsonValueKind.String)
                text.Append('\n').Append(o.GetString());
            if (root.TryGetProperty("Days", out var days) && days.ValueKind == JsonValueKind.Object)
                foreach (var day in days.EnumerateObject())
                    foreach (var field in new[] { "Summary", "EmotionSummary" })
                        if (day.Value.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s)
                            text.Append('\n').Append(s);
        }
        catch (JsonException)
        {
            // The text of the file is what is kept; only the searches lose its summaries.
        }
        return text.ToString();
    }

    public void WriteLocal(IReadOnlyList<SyncItem> upserts, IReadOnlyCollection<string> deletes)
    {
        foreach (var id in deletes)
        {
            var path = Path.Combine(Folder, Path.GetFileName(id) + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }
        foreach (var item in upserts)
            SyncAdapters.WriteFile(Path.Combine(Folder, Path.GetFileName(item.Id) + ".json"), item.Data);
    }
}
