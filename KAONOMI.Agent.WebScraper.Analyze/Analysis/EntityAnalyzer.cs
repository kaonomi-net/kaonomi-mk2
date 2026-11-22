using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// The persons, organisations and events of a page, settled by the model of the advanced conversations: from the
    /// entities the NER found when the page was collected (and the text itself, read in parts), the model keeps the
    /// real ones, gives each its full name, its other spellings in the text, who or what it is and what the page says
    /// of it, and adds the events the page names.
    /// </summary>
    public static class EntityAnalyzer
    {
        public const string Person = "PERSON", Organisation = "ORGANISATION", Event = "EVENT";

        /// <summary>PERSON, ORGANISATION or EVENT for a type of the NER or of the ontology (PER, PERS, ORG…), else null.</summary>
        public static string? Kind(string? type) => (type ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PERSON" or "PER" or "PERS" or "PERSONNE" or "PEOPLE" => Person,
            "ORG" or "ORGANISATION" or "ORGANIZATION" or "ORGANISATIONS" or "COMPANY" or "INSTITUTION" => Organisation,
            "EVENT" or "EVENEMENT" or "ÉVÉNEMENT" or "EVENTS" => Event,
            _ => null
        };

        private static string System(string language) =>
            "You are an analyst who reads a web page and lists the persons, the organisations and the events it names. "
            + "You are given a part of the text of the page and the names an entity recogniser found in it (some are wrong, cut or duplicated). "
            + "Keep only real persons, organisations (companies, institutions, parties, administrations, associations, media) and events "
            + "(a summit, an election, a trial, an attack, a festival, a match, a strike…: something that happens at a time) that the text names. "
            + "Give each one once, under its fullest name written in the text; put its other spellings of the text in aliases (a surname alone, an acronym). "
            + "Add the events the text names even when they are not in the names given. Do not invent anything the text does not say. "
            + $"Write summary and facts in {language}. Answer with a JSON object only: "
            + "{\"entities\":[{\"name\":\"...\",\"type\":\"PERSON|ORGANISATION|EVENT\",\"aliases\":[\"...\"],"
            + "\"summary\":\"who or what it is, in one or two sentences, according to the text\","
            + "\"facts\":\"what the text says about it, in a few sentences\",\"date\":\"yyyy-MM-dd for an event when the text gives its date, else empty\"}]}";

        /// <summary>
        /// The entities of the page. When the model gives nothing (it does not answer), the entities of the NER of the
        /// three kinds are kept as they are, with a warning.
        /// </summary>
        public static async Task<List<AnalysedEntity>> AnalyseAsync(Llm llm, string model, AnalyzeConfig config, Page page, IReadOnlyList<string> blocks,
            List<string> warnings, Action<string> log, CancellationToken cancellationToken)
        {
            var language = LanguageName(config.language);
            var candidates = (page.entities ?? new List<PageEntity>())
                .Select(e => (e.name, Kind: Kind(e.type), e.count))
                .Where(e => e.Kind is not null && !string.IsNullOrWhiteSpace(e.name) && e.name.Trim().Length > 1)
                .OrderByDescending(e => e.count)
                .ToList();

            var parts = TextTools.Parts(blocks, Math.Max(1000, config.llmChunkChars), Math.Max(2000, config.maxTextChars), out var leftOut);
            if (leftOut > 0)
                warnings.Add($"The page is long: its last {leftOut} block(s) of text were not read by the model (maxTextChars = {config.maxTextChars})");

            var merged = new List<AnalysedEntity>();
            var answered = 0;
            for (var i = 0; i < parts.Count && !cancellationToken.IsCancellationRequested; i++)
            {
                var text = string.Join("\n", parts[i]);
                var folded = TextTools.Fold(text);
                var here = candidates.Where(c => TextTools.Names(folded, new[] { TextTools.Fold(c.name) })).Select(c => $"{c.name} ({c.Kind})").Distinct().Take(80).ToList();
                var user = $"Page: {page.name}\nAddress: {page.url}\n\nNames found by the entity recogniser in this part:\n"
                           + (here.Count > 0 ? string.Join("\n", here.Select(h => "- " + h)) : "(none)")
                           + $"\n\nText (part {i + 1} of {parts.Count}):\n{text}";
                using var answer = await llm.AskJsonAsync(model, System(language), user, cancellationToken);
                if (answer is null)
                {
                    warnings.Add($"Entities, part {i + 1} of {parts.Count}: {llm.LastError}");
                    continue;
                }
                answered++;
                foreach (var item in Json.Items(answer.RootElement, "entities"))
                {
                    var name = Json.Text(item, "name");
                    var kind = Kind(Json.Text(item, "type"));
                    if (name.Length < 2 || kind is null)
                        continue;
                    Merge(merged, new AnalysedEntity
                    {
                        name = name,
                        type = kind,
                        aliases = Json.Texts(item, "aliases").Where(a => a.Length > 1 && !Same(a, name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        summary = Json.Text(item, "summary"),
                        facts = Json.Text(item, "facts"),
                        date = Json.Text(item, "date")
                    });
                }
                log($"Entities: part {i + 1} of {parts.Count} read, {merged.Count} so far");
            }
            if (cancellationToken.IsCancellationRequested)
                warnings.Add("Entities: the time of the stage ended before the whole text was read");

            if (answered == 0)
            {
                // The model gave nothing: the names of the NER, as they are.
                warnings.Add("The model gave no entity: those of the entity recogniser are kept as they are, without summary");
                foreach (var candidate in candidates)
                    Merge(merged, new AnalysedEntity { name = candidate.name.Trim(), type = candidate.Kind! });
            }

            // Only what the page really names, the most named first; and only the spellings the page really writes
            // (a model adds names it knows: « Airbus Group », « UE »).
            var foldedBlocks = blocks.Select(TextTools.Fold).ToList();
            var wholeText = string.Join("\n", foldedBlocks);
            foreach (var entity in merged)
            {
                entity.aliases = entity.aliases.Where(a => TextTools.Names(wholeText, new[] { TextTools.Fold(a) })).ToList();
                entity.mentions = TextTools.Passages(blocks, foldedBlocks, entity).Count;
            }
            var kept = merged.Where(e => e.mentions > 0).OrderByDescending(e => e.mentions).ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase).ToList();
            var dropped = merged.Count - kept.Count;
            if (dropped > 0)
                warnings.Add($"{dropped} entit(ies) given by the model are not named in the text of the page: left out");
            if (kept.Count > config.maxEntitiesPerPage)
            {
                warnings.Add($"{kept.Count} entities: the {config.maxEntitiesPerPage} named the most often are analysed (maxEntitiesPerPage)");
                kept = kept.Take(config.maxEntitiesPerPage).ToList();
            }
            return kept;
        }

        /// <summary>Adds the entity, or completes the one of the same kind with the same name (or a name that is its alias).</summary>
        private static void Merge(List<AnalysedEntity> entities, AnalysedEntity found)
        {
            var names = TextTools.FoldedNames(found);
            var known = entities.FirstOrDefault(e => e.type == found.type && TextTools.FoldedNames(e).Intersect(names).Any());
            if (known is null)
            {
                entities.Add(found);
                return;
            }
            // The fullest name is the name; the other one becomes an alias.
            if (found.name.Length > known.name.Length)
            {
                known.aliases.Add(known.name);
                known.name = found.name;
            }
            else if (!Same(found.name, known.name))
                known.aliases.Add(found.name);
            known.aliases = known.aliases.Concat(found.aliases).Where(a => !Same(a, known.name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (found.summary.Length > known.summary.Length)
                known.summary = found.summary;
            if (found.facts.Length > 0 && !known.facts.Contains(found.facts, StringComparison.OrdinalIgnoreCase))
                known.facts = known.facts.Length == 0 ? found.facts : known.facts + " " + found.facts;
            if (known.date.Length == 0)
                known.date = found.date;
        }

        private static bool Same(string a, string b) => TextTools.Fold(a) == TextTools.Fold(b);

        /// <summary>The language the model writes in, from the language of the configuration (FR, EN…).</summary>
        public static string LanguageName(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "FR" => "French",
            "EN" => "English",
            "DE" => "German",
            "ES" => "Spanish",
            "IT" => "Italian",
            "NL" => "Dutch",
            "PT" => "Portuguese",
            _ => "the language of the text"
        };
    }
}
