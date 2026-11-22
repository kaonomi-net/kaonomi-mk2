using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>
/// The kinds of fact sheets of the architecture repository of <c>/archi</c> (after SAP LeanIX): the usual ones of
/// enterprise architecture (business capabilities, processes, applications, interfaces, data objects, IT components,
/// tech categories, providers, organizations, user groups, projects) and the ones of AI (models, agents, multi-agent
/// systems, MCP servers, tools, REST APIs, prompts, datasets, vector stores, knowledge bases, evaluations, guardrails).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArchiType
{
    BusinessCapability,
    Process,
    Application,
    Interface,
    DataObject,
    ITComponent,
    TechCategory,
    Provider,
    Organization,
    UserGroup,
    Project,
    AIModel,
    Agent,
    MultiAgentSystem,
    McpServer,
    Tool,
    RestApi,
    Prompt,
    Dataset,
    VectorStore,
    KnowledgeBase,
    Evaluation,
    Guardrail
}

/// <summary>The phases of the life of a fact sheet, in their order.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArchiPhase
{
    Plan,
    PhaseIn,
    Active,
    PhaseOut,
    EndOfLife
}

/// <summary>A field of a kind of fact sheet: its key, its caption and, for a choice, its values (empty: free text).</summary>
public sealed record ArchiField(string Key, string Caption, string[] Choices, string Hint = "")
{
    public bool IsChoice => Choices.Length > 0;
}

/// <summary>What is known of a kind of fact sheet: its name, its icon, its layer, its colour (#RRGGBB) and its fields.</summary>
public sealed record ArchiTypeInfo(ArchiType Type, string Label, string Plural, string Icon, string Layer, string Color, ArchiField[] Fields)
{
    public bool IsAi => Layer == ArchiCatalog.AiLayer;
}

/// <summary>The kinds of fact sheets, their fields, the scales and the kinds of relations of <c>/archi</c>.</summary>
public static class ArchiCatalog
{
    public const string AiLayer = "AI";

    public static readonly string[] Layers = ["Business", "Application", "Technology", "Transformation", AiLayer];

    /// <summary>The business criticality (as in LeanIX).</summary>
    public static readonly string[] Criticality = ["", "Administrative service", "Business operational", "Business critical", "Mission critical"];

    /// <summary>The functional fit and the technical fit (as in LeanIX).</summary>
    public static readonly string[] Fit = ["", "Unreasonable", "Insufficient", "Appropriate", "Perfect"];

    /// <summary>The risk of an AI system under the EU AI Act.</summary>
    public static readonly string[] AiRisk = ["", "Minimal", "Limited", "High", "Unacceptable"];

    /// <summary>The kinds of relations a fact sheet can have with another one.</summary>
    public static readonly string[] RelationKinds =
    [
        "supports", "realizes", "uses", "runs on", "calls", "provides", "consumes", "reads", "writes", "exposes",
        "orchestrates", "delegates to", "is part of", "depends on", "trained on", "fine-tuned from", "evaluated by",
        "indexes", "embeds with", "guarded by", "prompted by", "hosted by", "owned by", "used by", "replaces", "implements"
    ];

    private static ArchiField F(string key, string caption, string hint = "") => new(key, caption, [], hint);
    private static ArchiField C(string key, string caption, params string[] choices) => new(key, caption, ["", .. choices]);

    private static readonly string[] Hosting = ["Local", "On-premises", "Private cloud", "Public cloud", "Edge", "SaaS"];
    private static readonly string[] YesNo = ["Yes", "No"];

    public static readonly IReadOnlyList<ArchiTypeInfo> Types =
    [
        new(ArchiType.BusinessCapability, "Business capability", "Business capabilities", "🏛", "Business", "#7E57C2",
            [C("maturity", "Maturity", "Initial", "Repeatable", "Defined", "Managed", "Optimized"), C("strategic", "Strategic importance", "Low", "Medium", "High")]),
        new(ArchiType.Process, "Process", "Processes", "🔁", "Business", "#9575CD",
            [F("trigger", "Trigger"), F("outcome", "Outcome"), C("automation", "Automation", "Manual", "Partly automated", "Automated", "Autonomous (AI)")]),
        new(ArchiType.Application, "Application", "Applications", "🧩", "Application", "#1E88E5",
            [C("kind", "Kind", "Custom built", "Commercial (COTS)", "SaaS", "Open source"), C("hosting", "Hosting", Hosting), F("users", "Users", "how many"), F("cost", "Cost per year", "EUR"), F("url", "URL")]),
        new(ArchiType.Interface, "Interface", "Interfaces", "🔌", "Application", "#29B6F6",
            [C("protocol", "Protocol", "REST", "GraphQL", "gRPC", "SOAP", "Message queue", "File", "Database link", "Event stream"), C("frequency", "Frequency", "Real time", "Hourly", "Daily", "Weekly", "On demand"), C("direction", "Direction", "Outbound", "Inbound", "Both ways")]),
        new(ArchiType.DataObject, "Data object", "Data objects", "🗄", "Application", "#26A69A",
            [C("classification", "Classification", "Public", "Internal", "Confidential", "Secret"), C("personal", "Personal data", YesNo), F("volume", "Volume")]),
        new(ArchiType.ITComponent, "IT component", "IT components", "⚙", "Technology", "#8D6E63",
            [C("category", "Category", "Software", "Hardware", "Service", "Platform", "Database", "Runtime"), F("version", "Version"), F("vendor", "Vendor"), C("hosting", "Hosting", Hosting)]),
        new(ArchiType.TechCategory, "Tech category", "Tech categories", "🗂", "Technology", "#A1887F", [F("standard", "Standard technologies")]),
        new(ArchiType.Provider, "Provider", "Providers", "🏢", "Technology", "#6D4C41",
            [F("website", "Website"), F("contract", "Contract"), C("strategic", "Relation", "Strategic", "Preferred", "Tolerated", "To be replaced")]),
        new(ArchiType.Organization, "Organization", "Organizations", "👥", "Business", "#5C6BC0", [C("kind", "Kind", "Company", "Business unit", "Department", "Team", "Partner")]),
        new(ArchiType.UserGroup, "User group", "User groups", "🧑‍🤝‍🧑", "Business", "#7986CB", [F("size", "Size", "how many people")]),
        new(ArchiType.Project, "Project", "Projects", "🚀", "Transformation", "#EF6C00",
            [C("status", "Status", "Idea", "Planned", "Running", "On hold", "Done", "Cancelled"), F("budget", "Budget", "EUR"), F("sponsor", "Sponsor")]),

        new(ArchiType.AIModel, "AI model", "AI models", "🧠", AiLayer, "#E91E63",
            [F("provider", "Provider", "Mistral, Meta, OpenAI, Anthropic…"), F("family", "Family", "llama, mistral, qwen…"), F("version", "Version"),
             F("parameters", "Parameters", "7B, 24B…"), F("context", "Context window", "tokens"),
             C("modality", "Modality", "Text", "Vision", "Audio", "Multimodal", "Embedding", "Image generation", "Speech to text", "Text to speech"),
             C("hosting", "Hosting", Hosting), C("serving", "Served by", "Ollama", "LM Studio", "vLLM", "llama.cpp", "Cloud API", "LLM gateway", "Other"),
             C("license", "License", "Open weights", "Open source", "Commercial", "Proprietary API"), F("cost", "Cost per million tokens", "EUR")]),
        new(ArchiType.Agent, "Agent", "Agents", "🤖", AiLayer, "#AB47BC",
            [C("framework", "Framework", "Semantic Kernel", "Microsoft Agent Framework", "LangGraph", "LangChain", "AutoGen", "CrewAI", "OpenAI Agents SDK", "Custom"),
             C("autonomy", "Autonomy", "Assistant (answers)", "Copilot (suggests)", "Agent (acts, confirmed)", "Autonomous (acts alone)"),
             C("human", "Human in the loop", YesNo), C("memory", "Memory", "None", "Conversation", "Long term", "Vector memory", "Knowledge graph"),
             F("goal", "Goal"), F("trigger", "Trigger", "a message, a schedule, an event…")]),
        new(ArchiType.MultiAgentSystem, "Multi-agent system", "Multi-agent systems", "🕸", AiLayer, "#8E24AA",
            [C("pattern", "Orchestration", "Sequential", "Supervisor / hierarchical", "Group chat", "Handoff", "Swarm", "Graph / workflow", "Magentic"),
             C("protocol", "Protocol between agents", "In process", "A2A", "MCP", "REST", "gRPC", "Message bus", "Orleans grains"),
             F("termination", "Termination", "when it stops")]),
        new(ArchiType.McpServer, "MCP server", "MCP servers", "🛰", AiLayer, "#00ACC1",
            [C("transport", "Transport", "stdio", "SSE", "Streamable HTTP"), F("url", "URL or command"), F("tools", "Tools exposed")]),
        new(ArchiType.Tool, "Tool", "Tools", "🛠", AiLayer, "#00897B",
            [C("kind", "Kind", "Function", "MCP tool", "REST call", "Code interpreter", "Web search", "Database query", "Browser"), C("effects", "Side effects", "Read only", "Writes", "Irreversible")]),
        new(ArchiType.RestApi, "REST API", "REST APIs", "🌐", AiLayer, "#43A047",
            [F("url", "Base URL"), F("openapi", "OpenAPI / Swagger URL"), C("style", "Style", "REST", "GraphQL", "gRPC", "WebSocket", "OpenAI compatible"), F("version", "Version"), F("endpoints", "Endpoints", "GET /search, POST /chat…")]),
        new(ArchiType.Prompt, "Prompt", "Prompts", "📝", AiLayer, "#FDD835",
            [F("key", "Key"), F("version", "Version"), F("purpose", "Purpose")]),
        new(ArchiType.Dataset, "Dataset", "Datasets", "📚", AiLayer, "#FB8C00",
            [F("source", "Source"), F("size", "Size"), C("personal", "Personal data", YesNo), C("usage", "Used for", "Training", "Fine-tuning", "Evaluation", "Retrieval (RAG)"), F("refresh", "Refresh")]),
        new(ArchiType.VectorStore, "Vector store", "Vector stores", "🧮", AiLayer, "#F4511E",
            [C("engine", "Engine", "Elasticsearch", "OpenSearch", "Qdrant", "pgvector", "Chroma", "Milvus", "Weaviate", "Pinecone", "FAISS"), F("dimensions", "Dimensions"), F("documents", "Documents")]),
        new(ArchiType.KnowledgeBase, "Knowledge base (RAG)", "Knowledge bases", "📖", AiLayer, "#FF7043",
            [F("sources", "Sources"), F("chunking", "Chunking"), C("retrieval", "Retrieval", "Vector", "Keyword", "Hybrid", "Graph (GraphRAG)")]),
        new(ArchiType.Evaluation, "Evaluation", "Evaluations", "📏", AiLayer, "#C0CA33",
            [F("metric", "Metric", "accuracy, faithfulness…"), F("score", "Score"), F("date", "Date", "yyyy-mm-dd"), F("benchmark", "Benchmark or test set")]),
        new(ArchiType.Guardrail, "Guardrail", "Guardrails", "🛡", AiLayer, "#78909C",
            [C("kind", "Kind", "Input filter", "Output filter", "Personal data masking", "Prompt injection detection", "Human approval", "Rate limit"), F("rule", "Rule")])
    ];

    public static ArchiTypeInfo Info(ArchiType type) => Types.First(t => t.Type == type);

    /// <summary>The kind of relation proposed first from a fact sheet of <paramref name="from"/> to one of <paramref name="to"/>.</summary>
    public static string SuggestedKind(ArchiType from, ArchiType to) => (from, to) switch
    {
        (ArchiType.Application, ArchiType.BusinessCapability) => "supports",
        (ArchiType.Process, ArchiType.BusinessCapability) => "realizes",
        (ArchiType.Application, ArchiType.ITComponent) => "runs on",
        (ArchiType.Application, ArchiType.Interface) => "provides",
        (ArchiType.Interface, ArchiType.DataObject) => "consumes",
        (ArchiType.Application, ArchiType.DataObject) => "writes",
        (ArchiType.ITComponent, ArchiType.Provider) => "hosted by",
        (ArchiType.AIModel, ArchiType.Provider) => "hosted by",
        (ArchiType.ITComponent, ArchiType.TechCategory) => "is part of",
        (ArchiType.Project, _) => "implements",
        (_, ArchiType.Organization) => "owned by",
        (_, ArchiType.UserGroup) => "used by",
        (ArchiType.Agent, ArchiType.AIModel) => "uses",
        (ArchiType.Agent, ArchiType.Tool) => "uses",
        (ArchiType.Agent, ArchiType.McpServer) => "uses",
        (ArchiType.Agent, ArchiType.RestApi) => "calls",
        (ArchiType.Agent, ArchiType.Prompt) => "prompted by",
        (ArchiType.Agent, ArchiType.KnowledgeBase) => "reads",
        (ArchiType.Agent, ArchiType.Guardrail) => "guarded by",
        (ArchiType.Agent, ArchiType.Agent) => "delegates to",
        (ArchiType.MultiAgentSystem, ArchiType.Agent) => "orchestrates",
        (ArchiType.MultiAgentSystem, ArchiType.AIModel) => "uses",
        (ArchiType.McpServer, ArchiType.Tool) => "exposes",
        (ArchiType.Tool, ArchiType.RestApi) => "calls",
        (ArchiType.Application, ArchiType.RestApi) => "exposes",
        (ArchiType.Application, ArchiType.Agent or ArchiType.MultiAgentSystem or ArchiType.AIModel) => "uses",
        (ArchiType.AIModel, ArchiType.Dataset) => "trained on",
        (ArchiType.AIModel, ArchiType.AIModel) => "fine-tuned from",
        (ArchiType.AIModel or ArchiType.Agent or ArchiType.MultiAgentSystem, ArchiType.Evaluation) => "evaluated by",
        (ArchiType.KnowledgeBase, ArchiType.VectorStore) => "indexes",
        (ArchiType.VectorStore, ArchiType.AIModel) => "embeds with",
        (ArchiType.KnowledgeBase, ArchiType.Dataset) => "reads",
        (ArchiType.AIModel or ArchiType.Agent, ArchiType.Guardrail) => "guarded by",
        _ => "depends on"
    };
}

/// <summary>A fact sheet of the repository: one application, one model, one agent…</summary>
public sealed class ArchiSheet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public ArchiType Type { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Tags { get; set; } = "";

    /// <summary>The fact sheet above this one, of the same kind (a capability of level 1 above one of level 2…), or null.</summary>
    public string? ParentId { get; set; }

    /// <summary>When each phase starts (yyyy-mm-dd), by phase; a phase without a date is not planned.</summary>
    public Dictionary<ArchiPhase, string> Lifecycle { get; set; } = [];

    public string Criticality { get; set; } = "";
    public string FunctionalFit { get; set; } = "";
    public string TechnicalFit { get; set; } = "";

    /// <summary>The risk under the EU AI Act (the fact sheets of AI).</summary>
    public string AiRisk { get; set; } = "";

    /// <summary>The fields of its kind (<see cref="ArchiTypeInfo.Fields"/>), by key.</summary>
    public Dictionary<string, string> Fields { get; set; } = [];

    public DateTime Updated { get; set; } = DateTime.Now;

    public string Field(string key) => Fields.TryGetValue(key, out var value) ? value : "";

    /// <summary>The phase it is in today (the last one started), or null when none is dated or none has started.</summary>
    public ArchiPhase? PhaseOn(DateTime day)
    {
        ArchiPhase? current = null;
        foreach (var phase in Enum.GetValues<ArchiPhase>())
            if (StartOf(phase) is { } start && start <= day)
                current = phase;
        return current;
    }

    public DateTime? StartOf(ArchiPhase phase) =>
        Lifecycle.TryGetValue(phase, out var text) && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public ArchiSheet Copy() => new()
    {
        Type = Type, Name = Name, Description = Description, Owner = Owner, Tags = Tags, ParentId = ParentId,
        Lifecycle = new(Lifecycle), Criticality = Criticality, FunctionalFit = FunctionalFit, TechnicalFit = TechnicalFit,
        AiRisk = AiRisk, Fields = new(Fields)
    };
}

/// <summary>A relation from one fact sheet to another, of a kind (<see cref="ArchiCatalog.RelationKinds"/>).</summary>
public sealed class ArchiRelation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Kind { get; set; } = "depends on";
    public string Description { get; set; } = "";
}

/// <summary>The repository of <c>/archi</c>: its fact sheets and their relations (saved as JSON).</summary>
public sealed class ArchiWorkspace
{
    public string Name { get; set; } = "Architecture";
    public List<ArchiSheet> Sheets { get; set; } = [];
    public List<ArchiRelation> Relations { get; set; } = [];
}
