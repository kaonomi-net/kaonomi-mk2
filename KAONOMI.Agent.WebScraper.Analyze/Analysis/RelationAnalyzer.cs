using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// The relations between the persons, organisations and events of a page, as the data of a graph. The relations
    /// that may hold between two kinds of entities are defined here (<see cref="Possible"/>); the model of the
    /// advanced conversations reads the text, in parts, and gives the relations it states between the entities named
    /// in each part, each with its type among the possible ones, a description, the sentence it comes from and how
    /// sure it is. The nodes of the graph are the entities (their id: the id of their entity of the ontology).
    /// </summary>
    public static class RelationAnalyzer
    {
        private const string P = EntityAnalyzer.Person, O = EntityAnalyzer.Organisation, E = EntityAnalyzer.Event;

        /// <summary>The relations possible, by the kinds of their two ends (source → target).</summary>
        public static readonly IReadOnlyList<PossibleRelation> Possible = new List<PossibleRelation>
        {
            R(P, P, "ALLY_OF", "supports, works closely with or is close to"),
            R(P, P, "OPPONENT_OF", "opposes, criticises or competes with"),
            R(P, P, "FAMILY_OF", "is a relative or the partner of"),
            R(P, P, "MET_WITH", "met, received or talked with"),
            R(P, P, "WORKS_WITH", "works with, in the same team or project"),
            R(P, P, "REPORTS_TO", "is under the authority of"),
            R(P, P, "SUCCEEDS", "took over the office or role of"),
            R(P, P, "ACCUSES", "accuses, sues or denounces"),
            R(P, O, "LEADS", "heads or chairs"),
            R(P, O, "MEMBER_OF", "belongs to or is elected for"),
            R(P, O, "WORKS_FOR", "is employed by or acts for"),
            R(P, O, "FOUNDED", "created or co-founded"),
            R(P, O, "REPRESENTS", "speaks or negotiates for"),
            R(P, O, "SUPPORTS", "backs or defends"),
            R(P, O, "OPPOSES", "criticises, fights or sues"),
            R(P, O, "LEFT", "resigned from or was dismissed by"),
            R(P, E, "PARTICIPATES_IN", "takes part in or attends"),
            R(P, E, "ORGANISES", "sets up or hosts"),
            R(P, E, "SPEAKS_AT", "speaks or testifies at"),
            R(P, E, "VICTIM_OF", "is hurt, killed or harmed by"),
            R(P, E, "ACCUSED_IN", "is suspected, charged or tried in"),
            R(P, E, "COMMENTS_ON", "reacts to or gives an opinion on"),
            R(O, O, "PARTNER_OF", "cooperates or has an agreement with"),
            R(O, O, "PART_OF", "is a body, a subsidiary or a member of"),
            R(O, O, "COMPETES_WITH", "is a rival of"),
            R(O, O, "OPPOSES", "criticises, fights or sues"),
            R(O, O, "FUNDS", "finances or invests in"),
            R(O, O, "ACQUIRES", "buys or takes control of"),
            R(O, O, "SUPPLIES", "sells to or signs a contract with"),
            R(O, O, "REGULATES", "controls, sanctions or supervises"),
            R(O, E, "ORGANISES", "sets up or hosts"),
            R(O, E, "PARTICIPATES_IN", "takes part in"),
            R(O, E, "SPONSORS", "finances or backs"),
            R(O, E, "TARGET_OF", "is aimed at or hit by"),
            R(O, E, "RESPONSIBLE_FOR", "caused or claims"),
            R(O, E, "COMMENTS_ON", "reacts to or gives an opinion on"),
            R(E, E, "PART_OF", "is a part or a session of"),
            R(E, E, "CAUSES", "leads to or triggers"),
            R(E, E, "FOLLOWS", "comes after or answers")
        };

        /// <summary>The relation kept when the model gives one that is not possible for the kinds of its ends.</summary>
        public const string Generic = "RELATED_TO";

        private static PossibleRelation R(string source, string target, string type, string meaning) =>
            new() { sourceType = source, targetType = target, type = type, meaning = meaning };

        private static string System(string language) =>
            "You are an analyst who builds a graph of relations from a web page. You are given a part of its text, the entities it names "
            + "(persons, organisations, events) and the relations possible between two kinds of entities. "
            + "Give every relation the text states between two of these entities: its source and its target by their exact names of the list, "
            + "its type among the possible ones for their kinds (in that direction: source to target), a short description, "
            + "the sentence of the text it comes from (evidence, copied as written) and how sure you are from 0 to 100. "
            + "Give only what the text says, not what you know otherwise; no relation between an entity and itself. "
            + $"Write the description in {language}. Answer with a JSON object only: "
            + "{\"relations\":[{\"source\":\"...\",\"target\":\"...\",\"type\":\"...\",\"description\":\"...\",\"evidence\":\"...\",\"confidence\":80}]}";

        /// <summary>The graph of the page: every entity as a node, the relations the model found as edges.</summary>
        public static async Task<RelationGraph> AnalyseAsync(Llm llm, string model, AnalyzeConfig config, Page page, IReadOnlyList<string> blocks,
            List<AnalysedEntity> entities, List<string> warnings, Action<string> log, CancellationToken cancellationToken)
        {
            var graph = new RelationGraph();
            var nodeOf = new Dictionary<AnalysedEntity, GraphNodeData>();
            foreach (var entity in entities)
            {
                var node = new GraphNodeData
                {
                    id = entity.ontologyId.Length > 0 ? entity.ontologyId : $"page:{page.id}:{TextTools.Fold(entity.type + " " + entity.name).Replace(' ', '-')}",
                    name = entity.name,
                    type = entity.type,
                    ontologyId = entity.ontologyId
                };
                nodeOf[entity] = node;
                graph.nodes.Add(node);
            }
            if (entities.Count < 2)
                return graph;

            var catalogue = string.Join("\n", Possible.GroupBy(p => (p.sourceType, p.targetType))
                .Select(g => $"{g.Key.sourceType} to {g.Key.targetType}: " + string.Join(", ", g.Select(p => $"{p.type} ({p.meaning})"))));
            var language = EntityAnalyzer.LanguageName(config.language);
            var parts = TextTools.Parts(blocks, Math.Max(1000, config.llmChunkChars), Math.Max(2000, config.maxTextChars), out _);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < parts.Count && !cancellationToken.IsCancellationRequested; i++)
            {
                var text = string.Join("\n", parts[i]);
                var folded = TextTools.Fold(text);
                var here = entities.Where(e => TextTools.Names(folded, TextTools.FoldedNames(e))).ToList();
                if (here.Count < 2)
                    continue;
                var user = $"Page: {page.name}\n\nEntities named in this part:\n" + string.Join("\n", here.Select(e => $"- {e.name} ({e.type})"))
                           + $"\n\nRelations possible:\n{catalogue}\n\nText (part {i + 1} of {parts.Count}):\n{text}";
                using var answer = await llm.AskJsonAsync(model, System(language), user, cancellationToken);
                if (answer is null)
                {
                    warnings.Add($"Relations, part {i + 1} of {parts.Count}: {llm.LastError}");
                    continue;
                }
                foreach (var item in Json.Items(answer.RootElement, "relations"))
                {
                    var source = Find(here, Json.Text(item, "source"));
                    var target = Find(here, Json.Text(item, "target"));
                    if (source is null || target is null || ReferenceEquals(source, target))
                        continue;
                    var type = Json.Text(item, "type").ToUpperInvariant().Replace(' ', '_');
                    // The type must be possible for these kinds; in the other direction, the relation is turned round.
                    if (!IsPossible(source.type, target.type, type))
                    {
                        if (IsPossible(target.type, source.type, type))
                            (source, target) = (target, source);
                        else
                            type = Generic;
                    }
                    var edge = new GraphEdgeData
                    {
                        sourceId = nodeOf[source].id,
                        targetId = nodeOf[target].id,
                        source = source.name,
                        target = target.name,
                        type = type,
                        description = Json.Text(item, "description"),
                        evidence = Json.Text(item, "evidence"),
                        confidence = (int)Math.Clamp(Math.Round(Json.Number(item, "confidence")), 0, 100),
                        sourcepageid = page.id
                    };
                    edge.id = $"{edge.sourceId}|{edge.type}|{edge.targetId}";
                    if (seen.Add(edge.id))
                        graph.edges.Add(edge);
                }
                log($"Relations: part {i + 1} of {parts.Count} read, {graph.edges.Count} so far");
            }
            if (cancellationToken.IsCancellationRequested)
                warnings.Add("Relations: the time of the stage ended before the whole text was read");
            return graph;
        }

        private static bool IsPossible(string sourceType, string targetType, string type) =>
            Possible.Any(p => p.sourceType == sourceType && p.targetType == targetType && p.type == type);

        private static AnalysedEntity? Find(List<AnalysedEntity> entities, string name)
        {
            var folded = TextTools.Fold(name);
            if (folded.Length < 2)
                return null;
            // The model may add the kind after the name, as in the list: « Airbus (ORGANISATION) ».
            var paren = folded.LastIndexOf(" (", StringComparison.Ordinal);
            var bare = paren > 0 ? folded[..paren] : folded;
            return entities.FirstOrDefault(e => TextTools.FoldedNames(e).Any(n => n == folded || n == bare));
        }
    }
}
