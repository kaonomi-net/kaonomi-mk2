using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The repository of <c>/archi</c> loaded into the ontology of the server (<c>/ontology</c>) and its relations
/// (<c>/relation</c>):
/// <list type="bullet">
/// <item>a class <c>Architecture</c> at the root, a class per layer under it (<c>Business layer</c>, <c>AI layer</c>…), a
/// class per kind of fact sheet under its layer (<c>Applications</c>, <c>AI models</c>…), and an entity per fact sheet
/// under its kind (or under the fact sheet above it); the entity type of a kind and of its fact sheets is the kind in
/// upper case (<c>APPLICATION</c>, <c>AI_MODEL</c>), the one of the root and the layers <c>ARCHITECTURE</c>;</item>
/// <item>a fact sheet gives its name, its description (the summary), a description written from all it holds (kind and
/// layer, owner, lifecycle, scores, fields, relations either way), its tags with <c>#ARCHI</c>, the names of the fact
/// sheets related to it (related entities), the start of its phase in (or active) and its end of life;</item>
/// <item>a relation gives a relation of <c>/relation</c> between the two entities (windows <c>Entity</c>), its kind as
/// the nature, certain (probability 100).</item>
/// </list>
/// The ids are fixed (<c>archi-root</c>, <c>archi-layer-ai</c>, <c>archi-kind-Agent</c>, <c>archi-sheet-&lt;id&gt;</c>,
/// <c>archi-rel-&lt;id&gt;</c>): loading again changes only what changed in <c>/archi</c>, restores what was deleted,
/// and deletes (softly: they can be undeleted) the entities and relations of fact sheets and relations removed from
/// <c>/archi</c>. What the user added to an entity in <c>/ontology</c> (synonyms, examples, emotions, confidentiality,
/// virtual parents, events, signals, links, project, note) is kept.
/// </summary>
public static class ArchiOntology
{
    public const string IdPrefix = "archi-";
    public const string RootId = "archi-root";
    public const string ArchitectureType = "ARCHITECTURE";
    public const string Tag = "#ARCHI";
    /// <summary>The window of an entity of the ontology at an end of a relation.</summary>
    public const string EntityWindow = "Entity";
    private const string AppName = "KAONOMI.Chat";
    private const string DefaultConfidentiality = "PUBLIC(PU)";
    /// <summary>The most entities and relations asked to the server: a list this long may be cut (nothing is deleted then).</summary>
    private const int MaxItems = 10000;

    public static string LayerId(string layer) => $"archi-layer-{layer.ToLowerInvariant()}";
    public static string KindId(ArchiType type) => $"archi-kind-{type}";
    public static string SheetId(string sheetId) => $"archi-sheet-{sheetId}";
    public static string RelationId(string relationId) => $"archi-rel-{relationId}";

    /// <summary>The entity type of a kind: <c>AIModel</c> → <c>AI_MODEL</c>, <c>McpServer</c> → <c>MCP_SERVER</c>.</summary>
    public static string EntityType(ArchiType type)
    {
        var name = type.ToString();
        var text = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            // A word starts at an upper case letter after a lower case one, or before a lower case one (AIModel: AI_Model).
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                text.Append('_');
            text.Append(char.ToUpperInvariant(name[i]));
        }
        return text.ToString();
    }

    /// <summary>What a load did.</summary>
    public sealed record Result(int Entities, int Created, int Updated, int Unchanged, int Deleted,
        int Relations, int RelationsCreated, int RelationsUpdated, int RelationsUnchanged, int RelationsDeleted,
        int Failed, string? FirstError, bool Complete)
    {
        public bool Changed => Created + Updated + Deleted + RelationsCreated + RelationsUpdated + RelationsDeleted > 0;

        public string Summary =>
            $"{Entities} entities ({Created} created, {Updated} changed, {Unchanged} unchanged, {Deleted} deleted) · "
            + $"{Relations} relations ({RelationsCreated} created, {RelationsUpdated} changed, {RelationsUnchanged} unchanged, {RelationsDeleted} deleted)"
            + (Failed > 0 ? $" · {Failed} refused by the server: {FirstError}" : "")
            + (Complete ? "" : $" · the server gave {MaxItems} items or more: nothing was deleted");
    }

    // ── What the repository gives ──

    /// <summary>
    /// The entities of the repository, a parent always before its children: the root, the layers and the kinds that
    /// have fact sheets, the fact sheets. Their confidentiality, dates of creation and authors are not set here.
    /// </summary>
    public static List<OntologyEntry> Entries(ArchiWorkspace workspace)
    {
        var sheets = workspace.Sheets.Where(s => !string.IsNullOrWhiteSpace(s.Id)).GroupBy(s => s.Id).Select(g => g.First()).ToList();
        var byId = sheets.ToDictionary(s => s.Id);
        var kinds = ArchiCatalog.Types.Where(t => sheets.Any(s => s.Type == t.Type)).ToList();
        var layers = ArchiCatalog.Layers.Where(l => kinds.Any(k => k.Layer == l)).ToList();

        var entries = new List<OntologyEntry>
        {
            new()
            {
                Id = RootId, Name = "Architecture", EntityType = ArchitectureType, IsOntologyClass = true, Symbol = "🏗",
                Summary = "The architecture repository of /archi: its layers, its kinds of fact sheets and its fact sheets.",
                Description = $"The repository \"{workspace.Name}\" of /archi (after SAP LeanIX): {sheets.Count} fact sheets of {kinds.Count} kinds "
                              + $"in {layers.Count} layers ({string.Join(", ", layers)}), and {workspace.Relations.Count} relations between them.",
                Tags = [Tag]
            }
        };
        foreach (var layer in layers)
        {
            var inLayer = kinds.Where(k => k.Layer == layer).ToList();
            entries.Add(new OntologyEntry
            {
                Id = LayerId(layer), NodeParentId = RootId, Name = $"{layer} layer", EntityType = ArchitectureType, IsOntologyClass = true,
                Summary = $"The {layer} layer of the architecture of /archi.",
                Description = $"The {layer} layer of the architecture of /archi: {string.Join(", ", inLayer.Select(k => k.Plural.ToLowerInvariant()))}.",
                Tags = [Tag]
            });
        }
        foreach (var kind in kinds)
        {
            var count = sheets.Count(s => s.Type == kind.Type);
            entries.Add(new OntologyEntry
            {
                Id = KindId(kind.Type), NodeParentId = LayerId(kind.Layer), Name = kind.Plural, EntityType = EntityType(kind.Type), IsOntologyClass = true,
                Symbol = kind.Icon, Title = kind.Label,
                Summary = $"The {kind.Plural.ToLowerInvariant()} of the architecture ({kind.Layer} layer of /archi).",
                Description = $"The {kind.Plural.ToLowerInvariant()} of the architecture of /archi ({kind.Layer} layer): {count} fact sheet(s)."
                              + (kind.Fields.Length > 0 ? $" Their fields: {string.Join(", ", kind.Fields.Select(f => f.Caption))}." : ""),
                Tags = [Tag]
            });
        }
        // The fact sheets, the ones above before the ones under them (a parent of another kind, unknown or in a loop: under its kind).
        string ParentOf(ArchiSheet sheet) =>
            sheet.ParentId is { } parent && byId.TryGetValue(parent, out var above) && above.Type == sheet.Type && !InLoop(sheet, byId)
                ? SheetId(parent)
                : KindId(sheet.Type);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var waiting = sheets.OrderBy(s => s.Type).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        while (waiting.Count > 0)
        {
            var ready = waiting.Where(s => ParentOf(s) is var p && (!p.StartsWith("archi-sheet-", StringComparison.Ordinal) || placed.Contains(p))).ToList();
            foreach (var sheet in ready)
            {
                entries.Add(Entry(sheet, ParentOf(sheet), workspace, byId));
                placed.Add(SheetId(sheet.Id));
            }
            if (ready.Count == 0)
            {
                // Never reached (a loop goes under its kind); the rest under their kinds rather than waiting forever.
                entries.AddRange(waiting.Select(s => Entry(s, KindId(s.Type), workspace, byId)));
                break;
            }
            waiting.RemoveAll(ready.Contains);
        }
        return entries;
    }

    /// <summary>True when the fact sheets above <paramref name="sheet"/> come back to it.</summary>
    private static bool InLoop(ArchiSheet sheet, Dictionary<string, ArchiSheet> byId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { sheet.Id };
        for (var parent = sheet.ParentId; parent is not null && byId.TryGetValue(parent, out var above); parent = above.ParentId)
            if (!seen.Add(above.Id))
                return true;
        return false;
    }

    private static OntologyEntry Entry(ArchiSheet sheet, string parentId, ArchiWorkspace workspace, Dictionary<string, ArchiSheet> byId)
    {
        var info = ArchiCatalog.Info(sheet.Type);
        var name = string.IsNullOrWhiteSpace(sheet.Name) ? $"{info.Label} {sheet.Id}" : sheet.Name.Trim();
        var outgoing = workspace.Relations.Where(r => r.From == sheet.Id && byId.ContainsKey(r.To) && r.To != sheet.Id).ToList();
        var incoming = workspace.Relations.Where(r => r.To == sheet.Id && byId.ContainsKey(r.From) && r.From != sheet.Id).ToList();
        string Label(ArchiSheet other) => ArchiCatalog.Info(other.Type).Label.ToLowerInvariant();

        var text = new List<string>();
        if (!string.IsNullOrWhiteSpace(sheet.Description))
            text.Add(sheet.Description.Trim());
        text.Add($"{info.Label} ({info.Layer} layer) of the architecture \"{workspace.Name}\" of /archi.");
        if (!string.IsNullOrWhiteSpace(sheet.Owner))
            text.Add($"Owner: {sheet.Owner.Trim()}.");
        var phases = Enum.GetValues<ArchiPhase>().Where(p => sheet.Lifecycle.TryGetValue(p, out var d) && !string.IsNullOrWhiteSpace(d))
            .Select(p => $"{PhaseName(p)} {sheet.Lifecycle[p].Trim()}").ToList();
        if (phases.Count > 0)
            text.Add($"Lifecycle: {string.Join(", ", phases)}.");
        var scores = new[]
            {
                ("Business criticality", sheet.Criticality), ("Functional fit", sheet.FunctionalFit), ("Technical fit", sheet.TechnicalFit),
                ("AI risk (EU AI Act)", sheet.AiRisk)
            }
            .Where(s => !string.IsNullOrWhiteSpace(s.Item2)).Select(s => $"{s.Item1}: {s.Item2.Trim()}.").ToList();
        if (scores.Count > 0)
            text.Add(string.Join(" ", scores));
        var fields = info.Fields.Where(f => !string.IsNullOrWhiteSpace(sheet.Field(f.Key))).Select(f => $"{f.Caption}: {sheet.Field(f.Key).Trim()}.").ToList();
        if (fields.Count > 0)
            text.Add(string.Join(" ", fields));
        var relations = outgoing.Select(r => $"{r.Kind} {byId[r.To].Name} ({Label(byId[r.To])})")
            .Concat(incoming.Select(r => $"{byId[r.From].Name} ({Label(byId[r.From])}) {r.Kind} it")).ToList();
        if (relations.Count > 0)
            text.Add($"Relations: {string.Join("; ", relations)}.");

        return new OntologyEntry
        {
            Id = SheetId(sheet.Id), NodeParentId = parentId, Name = name, EntityType = EntityType(sheet.Type), IsOntologyClass = false,
            Symbol = info.Icon, Title = info.Label,
            Summary = string.IsNullOrWhiteSpace(sheet.Description) ? $"{info.Label} of the architecture of /archi." : sheet.Description.Trim(),
            Description = string.Join("\n", text),
            Tags = MetaTags.Normalize([Tag, .. (sheet.Tags ?? "").Split(',', ';')], '#'),
            RelatedEntities = outgoing.Select(r => byId[r.To].Name.Trim()).Concat(incoming.Select(r => byId[r.From].Name.Trim()))
                .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StartDate = sheet.StartOf(ArchiPhase.PhaseIn) ?? sheet.StartOf(ArchiPhase.Active),
            EndDate = sheet.StartOf(ArchiPhase.EndOfLife)
        };
    }

    private static string PhaseName(ArchiPhase phase) => phase switch
    {
        ArchiPhase.PhaseIn => "phase in",
        ArchiPhase.PhaseOut => "phase out",
        ArchiPhase.EndOfLife => "end of life",
        _ => phase.ToString().ToLowerInvariant()
    };

    /// <summary>The relations of the repository between fact sheets that exist (a relation of a fact sheet to itself left out).</summary>
    public static List<RelationEntry> Relations(ArchiWorkspace workspace)
    {
        var byId = workspace.Sheets.Where(s => !string.IsNullOrWhiteSpace(s.Id)).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        return workspace.Relations
            .Where(r => !string.IsNullOrWhiteSpace(r.Id) && r.From != r.To && byId.ContainsKey(r.From) && byId.ContainsKey(r.To))
            .GroupBy(r => r.Id).Select(g => g.First())
            .Select(r =>
            {
                var (from, to) = (byId[r.From], byId[r.To]);
                var kind = string.IsNullOrWhiteSpace(r.Kind) ? "depends on" : r.Kind.Trim();
                return new RelationEntry
                {
                    Id = RelationId(r.Id), AppName = AppName,
                    Versions =
                    [
                        new RelationVersionEntry
                        {
                            Nature = kind, Title = $"{from.Name.Trim()} {kind} {to.Name.Trim()}",
                            Description = string.IsNullOrWhiteSpace(r.Description) ? $"From the architecture \"{workspace.Name}\" of /archi." : r.Description.Trim(),
                            Probability = 100,
                            Source = new RelationEndEntry { WindowName = EntityWindow, ObjectId = SheetId(from.Id), Title = from.Name.Trim(), Information = ArchiCatalog.Info(from.Type).Label },
                            Destinations = [new RelationEndEntry { WindowName = EntityWindow, ObjectId = SheetId(to.Id), Title = to.Name.Trim(), Information = ArchiCatalog.Info(to.Type).Label }]
                        }
                    ]
                };
            }).ToList();
    }

    // ── The load ──

    /// <summary>
    /// Loads the repository into the ontology and the relations of the server (see the class); what was done.
    /// <paramref name="progress"/> is told how far it is.
    /// </summary>
    public static async Task<Result> LoadAsync(ArchiWorkspace workspace, KaonomiAssistantService server, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var wanted = Entries(workspace);
        var existing = await server.GetLastOntologiesAsync("*", MaxItems, ct);
        var stored = existing.Where(e => !string.IsNullOrEmpty(e.Id)).GroupBy(e => e.Id!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var complete = existing.Count < MaxItems;
        int created = 0, updated = 0, unchanged = 0, deleted = 0, failed = 0;
        string? firstError = null;
        var now = DateTime.Now;

        void Refused(string what, ServiceReturn result)
        {
            failed++;
            firstError ??= $"{what}: {result.Message ?? "refused"}";
        }

        // The confidentiality of each entity written: a new one is never less confidential than its parent.
        var levels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in wanted)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Loading /archi into the ontology: {created + updated + unchanged + failed + 1}/{wanted.Count} entities…");
            if (stored.TryGetValue(entry.Id!, out var old))
            {
                var merged = Merge(old, entry);
                levels[entry.Id!] = merged.Confidentiality ?? DefaultConfidentiality;
                if (Same(old, merged))
                {
                    unchanged++;
                    continue;
                }
                merged.Date = now;
                var result = await server.UpdateOntologyAsync(merged, ct);
                if (result.IsSuccess)
                    updated++;
                else
                    Refused(entry.Name ?? entry.Id!, result);
                continue;
            }
            var parentLevel = entry.NodeParentId is { } parent && levels.TryGetValue(parent, out var level) ? level
                : entry.NodeParentId is { } p && stored.TryGetValue(p, out var storedParent) ? storedParent.Confidentiality : null;
            entry.Confidentiality = string.IsNullOrWhiteSpace(parentLevel) ? DefaultConfidentiality : parentLevel;
            levels[entry.Id!] = entry.Confidentiality;
            entry.IsDeleted = false;
            entry.Date = now;
            entry.DateCreated = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            entry.AppName = AppName;
            entry.MachineName = Environment.MachineName;
            entry.UserName = Environment.UserName;
            var made = await server.CreateOntologyAsync(entry, ct);
            if (made.IsSuccess)
                created++;
            else
                Refused(entry.Name ?? entry.Id!, made);
        }
        // The entities of fact sheets (and of kinds, layers) no longer in /archi: deleted, softly.
        if (complete)
        {
            var ids = wanted.Select(e => e.Id!).ToHashSet(StringComparer.Ordinal);
            foreach (var old in stored.Values.Where(e => e.Id!.StartsWith(IdPrefix, StringComparison.Ordinal) && !ids.Contains(e.Id!) && !e.IsDeleted))
            {
                ct.ThrowIfCancellationRequested();
                var gone = old.Copy();
                gone.IsDeleted = true;
                gone.Date = now;
                var result = await server.UpdateOntologyAsync(gone, ct);
                if (result.IsSuccess)
                    deleted++;
                else
                    Refused(old.Name ?? old.Id!, result);
            }
        }

        // The relations.
        var relations = Relations(workspace);
        var storedRelations = await server.SearchRelationsAsync("*", MaxItems, ct);
        var relationsComplete = storedRelations.Count < MaxItems;
        var current = storedRelations.Where(r => !string.IsNullOrEmpty(r.Id)).GroupBy(r => r.Id!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        int relationsCreated = 0, relationsUpdated = 0, relationsUnchanged = 0, relationsDeleted = 0;
        foreach (var relation in relations)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Loading /archi into the relations: {relationsCreated + relationsUpdated + relationsUnchanged + 1}/{relations.Count}…");
            var known = current.TryGetValue(relation.Id!, out var old) ? old : null;
            if (known is not null && !known.IsDeleted && Same(known.Current, relation.Current))
            {
                relationsUnchanged++;
                continue;
            }
            relation.Date = DateTimeOffset.Now;
            relation.MachineName = Environment.MachineName;
            relation.UserName = Environment.UserName;
            relation.Versions[^1].UserName = Environment.UserName;
            relation.Versions[^1].MachineName = Environment.MachineName;
            // A relation the server has (or had, deleted) gets a new version; a new one is made with its id.
            var result = await server.SaveRelationAsync(relation, ct);
            if (!result.IsSuccess)
                Refused(relation.Current.Title ?? relation.Id!, result);
            else if (known is null)
                relationsCreated++;
            else
                relationsUpdated++;
        }
        if (relationsComplete)
        {
            var ids = relations.Select(r => r.Id!).ToHashSet(StringComparer.Ordinal);
            foreach (var old in current.Values.Where(r => r.Id!.StartsWith(IdPrefix, StringComparison.Ordinal) && !ids.Contains(r.Id!) && !r.IsDeleted))
            {
                ct.ThrowIfCancellationRequested();
                var result = await server.DeleteRelationAsync(old.Id!, ct);
                if (result.IsSuccess)
                    relationsDeleted++;
                else
                    Refused(old.Current.Title ?? old.Id!, result);
            }
        }

        return new Result(wanted.Count, created, updated, unchanged, deleted, relations.Count, relationsCreated, relationsUpdated, relationsUnchanged,
            relationsDeleted, failed, firstError, complete && relationsComplete);
    }

    /// <summary>The stored entity with what <c>/archi</c> gives in place of its own fields; the rest (what the user added) kept.</summary>
    private static OntologyEntry Merge(OntologyEntry stored, OntologyEntry archi)
    {
        var merged = stored.Copy();
        merged.Name = archi.Name;
        merged.EntityType = archi.EntityType;
        merged.IsOntologyClass = archi.IsOntologyClass;
        merged.NodeParentId = archi.NodeParentId;
        merged.Summary = archi.Summary;
        merged.Description = archi.Description;
        merged.Symbol = archi.Symbol;
        merged.Title = archi.Title;
        merged.Tags = archi.Tags is null ? null : [.. archi.Tags];
        merged.RelatedEntities = archi.RelatedEntities is null ? null : [.. archi.RelatedEntities];
        merged.StartDate = archi.StartDate;
        merged.EndDate = archi.EndDate;
        merged.IsDeleted = false;
        if (string.IsNullOrWhiteSpace(merged.Confidentiality))
            merged.Confidentiality = DefaultConfidentiality;
        return merged;
    }

    /// <summary>True when the fields <c>/archi</c> gives are the same (an empty text or list the same as none).</summary>
    private static bool Same(OntologyEntry a, OntologyEntry b)
    {
        static string T(string? s) => (s ?? "").Trim();
        static bool L(List<string>? x, List<string>? y) => (x ?? []).SequenceEqual(y ?? []);
        return T(a.Name) == T(b.Name) && T(a.EntityType) == T(b.EntityType) && a.IsOntologyClass == b.IsOntologyClass
               && T(a.NodeParentId) == T(b.NodeParentId) && T(a.Summary) == T(b.Summary) && T(a.Description) == T(b.Description)
               && T(a.Symbol) == T(b.Symbol) && T(a.Title) == T(b.Title) && L(a.Tags, b.Tags) && L(a.RelatedEntities, b.RelatedEntities)
               && a.StartDate?.Date == b.StartDate?.Date && a.EndDate?.Date == b.EndDate?.Date && a.IsDeleted == b.IsDeleted
               && T(a.Confidentiality) == T(b.Confidentiality);
    }

    /// <summary>True when two versions of a relation say the same (nature, title, description, probability, ends).</summary>
    private static bool Same(RelationVersionEntry a, RelationVersionEntry b)
    {
        static string T(string? s) => (s ?? "").Trim();
        return T(a.Nature) == T(b.Nature) && T(a.Title) == T(b.Title) && T(a.Description) == T(b.Description) && a.Probability == b.Probability
               && T(a.Source.ObjectId) == T(b.Source.ObjectId) && T(a.Source.WindowName) == T(b.Source.WindowName)
               && a.Destinations.Select(d => T(d.ObjectId)).SequenceEqual(b.Destinations.Select(d => T(d.ObjectId)));
    }
}
