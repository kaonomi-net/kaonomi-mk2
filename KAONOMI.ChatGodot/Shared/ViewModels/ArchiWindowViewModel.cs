using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The architecture repository (<c>/archi</c>), after SAP LeanIX, without its security nor its access rights: fact
/// sheets of enterprise architecture and of AI (<see cref="ArchiType"/>), each with its owner, its tags, its parent,
/// its lifecycle (plan, phase in, active, phase out, end of life, by date), its business criticality, its functional
/// and technical fit, its risk under the EU AI Act for the AI ones, the fields of its kind, and its relations to the
/// others. Every change is saved at once in <c>~/kaonomi/archi/workspace.json</c>; the file as it was when the window
/// opened is kept first in <c>~/kaonomi/archi/history</c> (nothing is lost). The first time, an example describing
/// Kaonomi itself is written. Exports: JSON (the whole repository) and CSV (the inventory); import: a JSON of the same
/// shape, which replaces the repository (the one before goes to the history). While KAONOMI.graphbase runs
/// (<c>graphbaseUrl</c>), the repository is written to it as a graph (<see cref="ArchiGraph"/>) when the window opens
/// and after each change (2 s after the last one), and at once with <see cref="SyncGraphbaseAsync"/>.
/// </summary>
public sealed partial class ArchiWindowViewModel : ViewModelBase, ISingleWindow
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The JSON of the file of the repository (for <see cref="ArchiSync"/>).</summary>
    internal static JsonSerializerOptions FileJson => Json;
    private static readonly TimeSpan GraphbaseQuiet = TimeSpan.FromSeconds(2);
    /// <summary>One synchronisation with graphbase at a time (two windows, or a change during one).</summary>
    private static readonly SemaphoreSlim GraphbaseGate = new(1, 1);
    private readonly GraphbaseClient _graphbase = new(ChatSettings.Load().GraphbaseUrl);
    private CancellationTokenSource? _graphbaseWait;
    private bool _historyKept;

    public ArchiWindowViewModel()
    {
        Workspace = Load();
        // Fact sheets changed on another machine (the store is the search engine): the repository is read again.
        LocalSync.Applied += kind =>
        {
            if (kind != "archiitem")
                return;
            var selected = SelectedId;
            Workspace = Load();
            SelectedId = Workspace.Sheets.Any(s => s.Id == selected) ? selected : null;
            Changed?.Invoke();
        };
        _ = WriteToGraphbaseAsync(manual: false, TimeSpan.Zero);
    }

    /// <summary>Where the repository is kept: <c>~/kaonomi/archi</c>.</summary>
    public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "archi");

    public static string FilePath => Path.Combine(Folder, "workspace.json");

    public static string HistoryFolder => Path.Combine(Folder, "history");

    public ArchiWorkspace Workspace { get; private set; }

    /// <summary>The repository as it is on disk (read only: for /support), empty when there is none yet.</summary>
    public static ArchiWorkspace ReadWorkspace()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<ArchiWorkspace>(File.ReadAllText(FilePath), Json) ?? new ArchiWorkspace() : new ArchiWorkspace();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ActivityLog.Error("Archi", $"Cannot read {FilePath}: {ex.Message}");
            return new ArchiWorkspace();
        }
    }

    public IReadOnlyList<ArchiSheet> Sheets => Workspace.Sheets;

    public IReadOnlyList<ArchiRelation> Relations => Workspace.Relations;

    /// <summary>The fact sheet selected (its id), shown in the editor and at the centre of the graph.</summary>
    [ObservableProperty]
    private string? _selectedId;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isError;

    /// <summary>Raised when fact sheets or relations were added, removed or changed (the lists are drawn again).</summary>
    public event Action? Changed;

    public ArchiSheet? Selected => SelectedId is null ? null : Find(SelectedId);

    public ArchiSheet? Find(string id) => Workspace.Sheets.FirstOrDefault(s => s.Id == id);

    public void SetStatus(string text, bool error = false)
    {
        Status = text;
        IsError = error;
    }

    // ── Queries ──

    /// <summary>The fact sheets of <paramref name="types"/> (all when empty) whose name, description, owner, tags or fields hold <paramref name="search"/>, in the phase <paramref name="phase"/> today when given.</summary>
    public List<ArchiSheet> Filter(IReadOnlyCollection<ArchiType> types, string search, ArchiPhase? phase = null)
    {
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Workspace.Sheets
            .Where(s => types.Count == 0 || types.Contains(s.Type))
            .Where(s => phase is null || s.PhaseOn(DateTime.Today) == phase)
            .Where(s => words.All(w => Haystack(s).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Type).ThenBy(s => FullName(s), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Haystack(ArchiSheet s) =>
        string.Join(' ', [s.Name, s.Description, s.Owner, s.Tags, ArchiCatalog.Info(s.Type).Label, .. s.Fields.Values]);

    /// <summary>The name with the names of its parents before it: "Customer service / Support / Chat support".</summary>
    public string FullName(ArchiSheet sheet)
    {
        var names = new List<string> { sheet.Name };
        var seen = new HashSet<string> { sheet.Id };
        var parent = sheet.ParentId is null ? null : Find(sheet.ParentId);
        while (parent is not null && seen.Add(parent.Id))
        {
            names.Insert(0, parent.Name);
            parent = parent.ParentId is null ? null : Find(parent.ParentId);
        }
        return string.Join(" / ", names);
    }

    public IEnumerable<ArchiSheet> Children(ArchiSheet sheet) => Workspace.Sheets.Where(s => s.ParentId == sheet.Id).OrderBy(s => s.Name);

    /// <summary>The relations from the fact sheet.</summary>
    public IEnumerable<ArchiRelation> Outgoing(string id) => Workspace.Relations.Where(r => r.From == id);

    /// <summary>The relations to the fact sheet.</summary>
    public IEnumerable<ArchiRelation> Incoming(string id) => Workspace.Relations.Where(r => r.To == id);

    /// <summary>The fact sheets related to <paramref name="id"/>, either way.</summary>
    public IEnumerable<ArchiSheet> Neighbours(string id) =>
        Workspace.Relations.Where(r => r.From == id || r.To == id)
            .Select(r => Find(r.From == id ? r.To : r.From)).OfType<ArchiSheet>().Distinct();

    public bool Related(string a, string b) => Workspace.Relations.Any(r => (r.From == a && r.To == b) || (r.From == b && r.To == a));

    /// <summary>How complete the fact sheet is (0 to 100): its description, owner, lifecycle, scores, fields and relations.</summary>
    public int Completeness(ArchiSheet s)
    {
        var info = ArchiCatalog.Info(s.Type);
        var checks = new List<bool>
        {
            s.Description.Length > 0, s.Owner.Length > 0, s.Lifecycle.Count > 0, s.Criticality.Length > 0,
            s.FunctionalFit.Length > 0, s.TechnicalFit.Length > 0, Neighbours(s.Id).Any()
        };
        if (info.IsAi)
            checks.Add(s.AiRisk.Length > 0);
        checks.AddRange(info.Fields.Select(f => s.Field(f.Key).Length > 0));
        return (int)Math.Round(100.0 * checks.Count(c => c) / checks.Count);
    }

    // ── Changes (each one saved at once) ──

    public ArchiSheet Add(ArchiType type, string? name = null)
    {
        var info = ArchiCatalog.Info(type);
        var sheet = new ArchiSheet { Type = type, Name = name ?? UniqueName($"New {info.Label.ToLowerInvariant()}") };
        sheet.Lifecycle[ArchiPhase.Plan] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Workspace.Sheets.Add(sheet);
        SelectedId = sheet.Id;
        Save($"{info.Label} \"{sheet.Name}\" added");
        Changed?.Invoke();
        return sheet;
    }

    public ArchiSheet Duplicate(ArchiSheet sheet)
    {
        var copy = sheet.Copy();
        copy.Name = UniqueName(sheet.Name + " (copy)");
        Workspace.Sheets.Add(copy);
        foreach (var relation in Outgoing(sheet.Id).ToList())
            Workspace.Relations.Add(new ArchiRelation { From = copy.Id, To = relation.To, Kind = relation.Kind, Description = relation.Description });
        SelectedId = copy.Id;
        Save($"\"{sheet.Name}\" duplicated");
        Changed?.Invoke();
        return copy;
    }

    public void Delete(ArchiSheet sheet)
    {
        Workspace.Sheets.Remove(sheet);
        Workspace.Relations.RemoveAll(r => r.From == sheet.Id || r.To == sheet.Id);
        foreach (var child in Workspace.Sheets.Where(s => s.ParentId == sheet.Id))
            child.ParentId = sheet.ParentId;
        if (SelectedId == sheet.Id)
            SelectedId = null;
        Save($"\"{sheet.Name}\" deleted (with its relations)");
        Changed?.Invoke();
    }

    /// <summary>A field of the fact sheet changed (from the editor): saved; <paramref name="redraw"/> draws the lists again.</summary>
    public void Touch(ArchiSheet sheet, bool redraw = false)
    {
        sheet.Updated = DateTime.Now;
        Save(null);
        if (redraw)
            Changed?.Invoke();
    }

    public ArchiRelation Relate(string from, string to, string kind)
    {
        var relation = new ArchiRelation { From = from, To = to, Kind = kind };
        Workspace.Relations.Add(relation);
        Save($"Relation added: {Find(from)?.Name} {kind} {Find(to)?.Name}");
        Changed?.Invoke();
        return relation;
    }

    public void Unrelate(ArchiRelation relation)
    {
        Workspace.Relations.Remove(relation);
        Save($"Relation removed: {Find(relation.From)?.Name} {relation.Kind} {Find(relation.To)?.Name}");
        Changed?.Invoke();
    }

    private string UniqueName(string name)
    {
        var candidate = name;
        for (var i = 2; Workspace.Sheets.Any(s => s.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = $"{name} {i}";
        return candidate;
    }

    // ── The file ──

    private ArchiWorkspace Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var workspace = JsonSerializer.Deserialize<ArchiWorkspace>(File.ReadAllText(FilePath), Json) ?? new ArchiWorkspace();
                SetStatus($"{workspace.Sheets.Count} fact sheets and {workspace.Relations.Count} relations, from {FilePath}");
                return workspace;
            }
            var example = ArchiExample.Kaonomi();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(example, Json));
            SetStatus($"An example (the architecture of Kaonomi itself) was written in {FilePath}: change it, or start your own");
            return example;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus($"The repository could not be read ({ex.Message}): an empty one is used", true);
            ActivityLog.Error("Archi", $"Cannot read {FilePath}: {ex.Message}");
            return new ArchiWorkspace();
        }
    }

    /// <summary>Writes the repository; the first time in this window, the file as it was goes to the history first.</summary>
    private void Save(string? done)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (!_historyKept && File.Exists(FilePath))
            {
                Directory.CreateDirectory(HistoryFolder);
                File.Copy(FilePath, Path.Combine(HistoryFolder, $"workspace-{DateTime.Now:yyyyMMdd-HHmmss}.json"), overwrite: false);
            }
            _historyKept = true;
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Workspace, Json));
            LocalSync.Touch("archiitem");
            if (done is not null)
                SetStatus(done);
            _ = WriteToGraphbaseAsync(manual: false, GraphbaseQuiet);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Not saved: {ex.Message}", true);
            ActivityLog.Error("Archi", $"Cannot write {FilePath}: {ex.Message}");
        }
    }

    // ── KAONOMI.graphbase ──

    /// <summary>Writes the repository to KAONOMI.graphbase now (⇄ GRAPHBASE), and says what it did (also returned).</summary>
    public Task<string?> SyncGraphbaseAsync() => WriteToGraphbaseAsync(manual: true, TimeSpan.Zero);

    /// <summary>
    /// Makes the graph of graphbase what the repository is, after <paramref name="wait"/> without other change (a change
    /// meanwhile starts the wait again). Not running: nothing is done, said only when asked (<paramref name="manual"/>).
    /// Started and continued on the thread of the window: the repository is read there.
    /// </summary>
    private async Task<string?> WriteToGraphbaseAsync(bool manual, TimeSpan wait)
    {
        _graphbaseWait?.Cancel();
        var cancel = _graphbaseWait = new CancellationTokenSource();
        try
        {
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancel.Token);
            if (!await _graphbase.RunningAsync(cancel.Token))
            {
                if (manual)
                    SetStatus($"KAONOMI.graphbase does not answer at {_graphbase.Url}: start it from /process", true);
                return manual ? Status : null;
            }
            await GraphbaseGate.WaitAsync(cancel.Token);
            try
            {
                var payload = ArchiGraph.Build(Workspace, FullName, Completeness);
                var result = await ArchiGraph.SyncAsync(_graphbase, payload, cancel.Token);
                if (result.Changed || manual)
                    ActivityLog.Info("Archi", $"Written to graphbase ({_graphbase.Url}): {result.Summary}");
                if (manual)
                    SetStatus($"graphbase: {result.Summary}");
                return result.Summary;
            }
            finally
            {
                GraphbaseGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A change came: the next write takes it.
            return null;
        }
        catch (Exception ex) when (ex is GraphbaseException or HttpRequestException or JsonException or IOException)
        {
            ActivityLog.Error("Archi", $"Not written to graphbase ({_graphbase.Url}): {ex.Message}");
            if (manual)
                SetStatus($"Not written to graphbase: {ex.Message}", true);
            return $"Not written to graphbase: {ex.Message}";
        }
    }

    public string ExportJson() => JsonSerializer.Serialize(Workspace, Json);

    /// <summary>The inventory as CSV (one line per fact sheet; separated by semicolons, as Excel reads them in French).</summary>
    public string ExportCsv()
    {
        static string Cell(string? text) => "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";
        var fieldKeys = ArchiCatalog.Types.SelectMany(t => t.Fields.Select(f => f.Key)).Distinct().ToList();
        var csv = new StringBuilder();
        string[] header = ["Id", "Type", "Name", "Full name", "Description", "Owner", "Tags", "Phase today", .. Enum.GetNames<ArchiPhase>(),
            "Business criticality", "Functional fit", "Technical fit", "AI risk", "Completeness", "Relations", .. fieldKeys];
        csv.AppendLine(string.Join(';', header.Select(Cell)));
        foreach (var s in Workspace.Sheets.OrderBy(s => s.Type).ThenBy(s => s.Name))
        {
            var relations = string.Join(" | ", Outgoing(s.Id).Select(r => $"{r.Kind} {Find(r.To)?.Name}"));
            string[] line = [s.Id, ArchiCatalog.Info(s.Type).Label, s.Name, FullName(s), s.Description, s.Owner, s.Tags, s.PhaseOn(DateTime.Today)?.ToString() ?? "",
                .. Enum.GetValues<ArchiPhase>().Select(p => s.Lifecycle.GetValueOrDefault(p, "")),
                s.Criticality, s.FunctionalFit, s.TechnicalFit, s.AiRisk, Completeness(s).ToString(CultureInfo.InvariantCulture), relations,
                .. fieldKeys.Select(s.Field)];
            csv.AppendLine(string.Join(';', line.Select(Cell)));
        }
        return csv.ToString();
    }

    /// <summary>Replaces the repository by the JSON of <paramref name="path"/> (the one before is kept in the history).</summary>
    public bool Import(string path)
    {
        try
        {
            var workspace = JsonSerializer.Deserialize<ArchiWorkspace>(File.ReadAllText(path), Json);
            if (workspace is null || workspace.Sheets.Count == 0)
            {
                SetStatus($"{Path.GetFileName(path)} holds no fact sheet", true);
                return false;
            }
            Directory.CreateDirectory(HistoryFolder);
            if (File.Exists(FilePath))
                File.Copy(FilePath, Path.Combine(HistoryFolder, $"workspace-{DateTime.Now:yyyyMMdd-HHmmss}-before-import.json"), overwrite: false);
            _historyKept = true;
            Workspace = workspace;
            SelectedId = null;
            Save($"{workspace.Sheets.Count} fact sheets and {workspace.Relations.Count} relations imported from {Path.GetFileName(path)} (the repository before is in the history)");
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus($"{Path.GetFileName(path)} could not be imported: {ex.Message}", true);
            return false;
        }
    }
}

/// <summary>The example written the first time: the architecture of Kaonomi itself, with its models, agents and APIs.</summary>
public static class ArchiExample
{
    public static ArchiWorkspace Kaonomi()
    {
        var w = new ArchiWorkspace { Name = "Kaonomi" };
        var year = DateTime.Today.Year;
        ArchiSheet S(ArchiType type, string name, string description, string phase = "Active", string criticality = "", string? parent = null, params (string Key, string Value)[] fields)
        {
            var sheet = new ArchiSheet { Type = type, Name = name, Description = description, Owner = "Kaonomi team", ParentId = parent, Criticality = criticality };
            sheet.Lifecycle[ArchiPhase.Plan] = $"{year - 2}-01-15";
            sheet.Lifecycle[ArchiPhase.PhaseIn] = $"{year - 1}-03-01";
            if (phase is "Active" or "PhaseOut")
                sheet.Lifecycle[ArchiPhase.Active] = $"{year - 1}-09-01";
            if (phase == "PhaseOut")
                sheet.Lifecycle[ArchiPhase.PhaseOut] = $"{year}-06-01";
            if (phase == "Plan")
            {
                sheet.Lifecycle.Clear();
                sheet.Lifecycle[ArchiPhase.Plan] = $"{year}-01-10";
                sheet.Lifecycle[ArchiPhase.PhaseIn] = $"{year + 1}-02-01";
                sheet.Lifecycle[ArchiPhase.Active] = $"{year + 1}-06-01";
            }
            foreach (var (key, value) in fields)
                sheet.Fields[key] = value;
            w.Sheets.Add(sheet);
            return sheet;
        }
        void R(ArchiSheet from, string kind, ArchiSheet to) => w.Relations.Add(new ArchiRelation { From = from.Id, To = to.Id, Kind = kind });

        // Business
        var intel = S(ArchiType.BusinessCapability, "Intelligence", "Collect, analyse and share information", criticality: "Business critical");
        var collect = S(ArchiType.BusinessCapability, "Collection", "Gather information from the web, documents and sensors", parent: intel.Id);
        var analysis = S(ArchiType.BusinessCapability, "Analysis", "Understand and link what was collected", parent: intel.Id);
        var assist = S(ArchiType.BusinessCapability, "Assistance", "Help people by conversation and voice", criticality: "Business operational");
        var conversation = S(ArchiType.BusinessCapability, "Conversation", "Answer questions, by text and voice", parent: assist.Id);
        var perception = S(ArchiType.BusinessCapability, "Perception", "See and hear: faces, objects, speech", parent: assist.Id);
        var knowledge = S(ArchiType.BusinessCapability, "Knowledge management", "Keep and search documents, notes and relations");
        var team = S(ArchiType.Organization, "Kaonomi team", "The people building Kaonomi", fields: [("kind", "Team")]);
        var analysts = S(ArchiType.UserGroup, "Analysts", "The users of the intelligence tools", fields: [("size", "10")]);

        // Applications and technology
        var chat = S(ArchiType.Application, "KAONOMI Chat (Godot)", "The desktop of Kaonomi: the chat, the tools, the 3D avatar", criticality: "Business critical", fields: [("kind", "Custom built"), ("hosting", "Local")]);
        var server = S(ArchiType.Application, "KAONOMI Server", "The server of Kaonomi: its REST API, its agents, its grains", criticality: "Mission critical", fields: [("kind", "Custom built"), ("hosting", "Local"), ("url", "http://localhost:49165")]);
        var searchEngine = S(ArchiType.Application, "KAONOMI SearchEngine", "The search engine of the documents, notes, intels and their vectors", criticality: "Business critical", fields: [("kind", "Custom built")]);
        var vision = S(ArchiType.Application, "KAONOMI Vision", "Faces, objects and movements from the camera (OpenCV, YOLO)", fields: [("kind", "Custom built")]);
        var avalonia = S(ArchiType.Application, "KAONOMI Chat (Avalonia)", "The first desktop of Kaonomi, replaced by the Godot chat", phase: "PhaseOut", fields: [("kind", "Custom built")]);
        var godot = S(ArchiType.ITComponent, "Godot 4 (.NET)", "The engine of the desktop", fields: [("category", "Runtime"), ("version", "4.7")]);
        var dotnet = S(ArchiType.ITComponent, ".NET 8", "The runtime of the server and of the chat", fields: [("category", "Runtime"), ("version", "8")]);
        var orleans = S(ArchiType.ITComponent, "Microsoft Orleans", "The virtual actors (grains) of the server", fields: [("category", "Platform"), ("version", "9")]);
        var mongo = S(ArchiType.ITComponent, "MongoDB", "The database of the server", fields: [("category", "Database")]);
        var ollamaRuntime = S(ArchiType.ITComponent, "Ollama", "Runs the local models", fields: [("category", "Platform"), ("hosting", "Local")]);
        var lmstudio = S(ArchiType.ITComponent, "LM Studio", "Runs local models behind an OpenAI compatible API", fields: [("category", "Platform"), ("hosting", "Local")]);
        var frameworks = S(ArchiType.TechCategory, "AI runtimes", "Where the models run");
        var mistralAi = S(ArchiType.Provider, "Mistral AI", "Models with open weights", fields: [("website", "https://mistral.ai")]);
        var liquid = S(ArchiType.Provider, "Liquid AI", "Small multimodal models", fields: [("website", "https://www.liquid.ai")]);
        var conversations = S(ArchiType.DataObject, "Conversations", "The exchanges with the chat", fields: [("classification", "Confidential"), ("personal", "Yes")]);
        var documents = S(ArchiType.DataObject, "Documents", "Documents, web pages and notes collected", fields: [("classification", "Internal")]);
        var serverApi = S(ArchiType.Interface, "Chat → Server", "The chat calls the REST API of the server", fields: [("protocol", "REST"), ("frequency", "Real time")]);

        // AI
        var lfm = S(ArchiType.AIModel, "lfm3-omni-2b", "The small multimodal model of the chat (text, audio, vision)", criticality: "Business critical",
            fields: [("provider", "Liquid AI"), ("family", "LFM"), ("parameters", "2B"), ("modality", "Multimodal"), ("hosting", "Local"), ("serving", "LM Studio"), ("license", "Open weights")]);
        var mistral = S(ArchiType.AIModel, "mistral-small3.2", "The vision model reading the pictures of the pages and the scans", criticality: "Business operational",
            fields: [("provider", "Mistral AI"), ("family", "mistral"), ("parameters", "24B"), ("context", "128000"), ("modality", "Vision"), ("hosting", "Local"), ("serving", "Ollama"), ("license", "Open weights")]);
        var embed = S(ArchiType.AIModel, "nomic-embed-text", "The embeddings of the documents", fields: [("modality", "Embedding"), ("hosting", "Local"), ("serving", "Ollama"), ("license", "Open weights")]);
        var whisper = S(ArchiType.AIModel, "whisper (ggml-small)", "Speech to text of the dictation and the recorder", fields: [("modality", "Speech to text"), ("hosting", "Local"), ("serving", "Other"), ("license", "Open source")]);
        var piper = S(ArchiType.AIModel, "Piper voices", "Text to speech of the avatar", fields: [("modality", "Text to speech"), ("hosting", "Local"), ("license", "Open source")]);
        var yolo = S(ArchiType.AIModel, "YOLO", "Detection of the objects of the camera", fields: [("modality", "Vision"), ("hosting", "Local"), ("license", "Open source")]);
        var future = S(ArchiType.AIModel, "Next reasoning model", "A larger model for the agents, under study", phase: "Plan", fields: [("modality", "Text"), ("hosting", "Local")]);
        var assistant = S(ArchiType.Agent, "Kaonomi assistant", "Answers in the chat, with its persona and its voice", criticality: "Business critical",
            fields: [("framework", "Custom"), ("autonomy", "Assistant (answers)"), ("human", "Yes"), ("memory", "Conversation"), ("goal", "Answer the user")]);
        var analyst = S(ArchiType.Agent, "Collection analyst", "Reads the pages of a collection plan and writes the analysis",
            fields: [("framework", "Custom"), ("autonomy", "Agent (acts, confirmed)"), ("human", "Yes"), ("memory", "None"), ("trigger", "RUN of a collection plan")]);
        var reader = S(ArchiType.Agent, "Page reader", "Reads the pictures of a web page, screen by screen",
            fields: [("framework", "Custom"), ("autonomy", "Autonomous (acts alone)"), ("human", "No"), ("memory", "None")]);
        var dreamer = S(ArchiType.Agent, "Dreamer", "Links the knowledge at night: relations, clusters, ideas",
            fields: [("framework", "Custom"), ("autonomy", "Autonomous (acts alone)"), ("human", "No"), ("memory", "Knowledge graph"), ("trigger", "Schedule")]);
        var scraping = S(ArchiType.MultiAgentSystem, "Web scraping pipeline", "The scraper, the page reader and the analyst, one after the other",
            fields: [("pattern", "Sequential"), ("protocol", "In process"), ("termination", "When every page is read")]);
        var grains = S(ArchiType.MultiAgentSystem, "Server agents", "The agents of the server, as Orleans grains", fields: [("pattern", "Supervisor / hierarchical"), ("protocol", "Orleans grains")]);
        var rest = S(ArchiType.RestApi, "Kaonomi REST API", "The API of the server", criticality: "Mission critical",
            fields: [("url", "http://localhost:49165"), ("openapi", "http://localhost:49165/swagger/index.html"), ("style", "REST"), ("endpoints", "/search, /collectionplan, /prompttemplate…")]);
        var openai = S(ArchiType.RestApi, "LM Studio API", "The OpenAI compatible API of LM Studio", fields: [("url", "http://localhost:1234/v1"), ("style", "OpenAI compatible")]);
        var ollamaApi = S(ArchiType.RestApi, "Ollama API", "The API of Ollama", fields: [("url", "http://localhost:11434"), ("style", "REST")]);
        var mcp = S(ArchiType.McpServer, "Kaonomi MCP", "The tools of Kaonomi offered to other assistants", phase: "Plan", fields: [("transport", "Streamable HTTP"), ("tools", "search, intel, mindmap")]);
        var searchTool = S(ArchiType.Tool, "Search", "Searches the indices of the search engine", fields: [("kind", "REST call"), ("effects", "Read only")]);
        var prompts = S(ArchiType.Prompt, "Collection analysis", "The prompt of the collection analyst", fields: [("key", "collection.analysis"), ("version", "3")]);
        var vectors = S(ArchiType.VectorStore, "Search engine vectors", "The vectors of the documents in the search engine", fields: [("engine", "Elasticsearch")]);
        var rag = S(ArchiType.KnowledgeBase, "Kaonomi knowledge", "Documents, notes and intels, searched by the chat", fields: [("retrieval", "Hybrid")]);
        var corpus = S(ArchiType.Dataset, "Collected pages", "The pages read by the collection plans", fields: [("source", "Web"), ("personal", "No"), ("usage", "Retrieval (RAG)")]);
        var faithfulness = S(ArchiType.Evaluation, "Answer faithfulness", "Are the answers backed by the sources?", fields: [("metric", "faithfulness"), ("score", "0.82")]);
        var guard = S(ArchiType.Guardrail, "Personal data masking", "Masks the personal data before a model reads it", phase: "Plan", fields: [("kind", "Personal data masking")]);
        assistant.AiRisk = "Limited";
        analyst.AiRisk = "Limited";
        reader.AiRisk = "Minimal";
        dreamer.AiRisk = "Minimal";
        scraping.AiRisk = "Limited";
        lfm.AiRisk = "Limited";
        mistral.AiRisk = "Minimal";
        yolo.AiRisk = "Limited";
        embed.AiRisk = "Minimal";
        whisper.AiRisk = "Limited";
        piper.AiRisk = "Limited";
        future.AiRisk = "Limited";
        grains.AiRisk = "Minimal";
        var faces = S(ArchiType.AIModel, "Face recognition (OpenCV)", "Recognises the faces enrolled in /vision", fields: [("modality", "Vision"), ("hosting", "Local"), ("license", "Open source")]);
        faces.AiRisk = "High";

        R(chat, "supports", conversation); R(chat, "supports", perception); R(server, "supports", knowledge); R(searchEngine, "supports", knowledge);
        R(vision, "supports", perception); R(server, "supports", collect); R(server, "supports", analysis); R(avalonia, "supports", conversation);
        R(chat, "runs on", godot); R(chat, "runs on", dotnet); R(server, "runs on", dotnet); R(server, "runs on", orleans); R(server, "runs on", mongo);
        R(ollamaRuntime, "is part of", frameworks); R(lmstudio, "is part of", frameworks);
        R(chat, "provides", serverApi); R(serverApi, "consumes", conversations); R(server, "writes", documents); R(chat, "writes", conversations);
        R(server, "exposes", rest); R(chat, "calls", rest); R(chat, "uses", assistant); R(chat, "uses", whisper); R(chat, "uses", piper); R(vision, "uses", yolo); R(vision, "uses", faces);
        R(assistant, "uses", lfm); R(assistant, "calls", openai); R(assistant, "reads", rag); R(assistant, "uses", searchTool); R(assistant, "evaluated by", faithfulness); R(assistant, "guarded by", guard);
        R(analyst, "uses", lfm); R(analyst, "prompted by", prompts); R(reader, "uses", mistral); R(reader, "calls", ollamaApi);
        R(scraping, "orchestrates", reader); R(scraping, "orchestrates", analyst); R(grains, "orchestrates", dreamer); R(dreamer, "uses", lfm);
        R(lfm, "hosted by", lmstudio); R(mistral, "hosted by", ollamaRuntime); R(embed, "hosted by", ollamaRuntime); R(mistral, "is part of", mistralAi); R(lfm, "is part of", liquid);
        R(rag, "indexes", vectors); R(vectors, "embeds with", embed); R(rag, "reads", corpus); R(rag, "reads", documents);
        R(mcp, "exposes", searchTool); R(searchTool, "calls", rest); R(future, "evaluated by", faithfulness);
        R(chat, "owned by", team); R(server, "owned by", team); R(chat, "used by", analysts);
        var project = S(ArchiType.Project, "Agents on the server", "Move the agents of the chat to the server, behind MCP", phase: "Plan", fields: [("status", "Planned"), ("sponsor", "Kaonomi team")]);
        R(project, "implements", mcp); R(project, "implements", future);
        return w;
    }
}
