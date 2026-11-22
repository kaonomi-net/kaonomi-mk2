using System.Globalization;
using KAONOMI.Core;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// Puts the entities of a page in the ontology. An entity that is there (same kind, same name or one of its
    /// synonyms, whatever the case and the accents) gets the link of the page in its related links and what it lacks:
    /// the spellings of the page as synonyms, a summary and a description when it has none, a sentence of the page as
    /// an example, the date of an event. What is written there already is never replaced. An entity that is not there
    /// is created. With <c>updateOntology</c> false, nothing is written: each entity is only told existing or not.
    /// </summary>
    public sealed class OntologyLinker
    {
        private const int MostExamples = 10;

        private readonly SessionManager _session;
        private readonly AnalyzeConfig _config;
        private readonly Dictionary<string, Ontology> _byName = new(StringComparer.Ordinal);

        public OntologyLinker(SessionManager session, AnalyzeConfig config)
        {
            _session = session;
            _config = config;
            foreach (var entity in session.OntologiesAll(20000))
                Index(entity);
        }

        /// <summary>How many names (titles and synonyms) of persons, organisations and events the ontology has.</summary>
        public int Names => _byName.Count;

        private void Index(Ontology entity)
        {
            if (entity.isDeleted || entity.isOntologyClass || string.IsNullOrWhiteSpace(entity.name) || EntityAnalyzer.Kind(entity.EntityType) is not { } kind)
                return;
            foreach (var name in (entity.synonyms ?? new List<string>()).Prepend(entity.name!))
            {
                var key = Key(kind, name);
                if (key is not null)
                    _byName.TryAdd(key, entity);
            }
        }

        private static string? Key(string kind, string? name)
        {
            var folded = TextTools.Fold(name);
            return folded.Length < 2 ? null : kind + "|" + folded;
        }

        /// <summary>The entity of the ontology for <paramref name="entity"/>: by its name first, then by its aliases of several words.</summary>
        private Ontology? Find(AnalysedEntity entity)
        {
            if (Key(entity.type, entity.name) is { } key && _byName.TryGetValue(key, out var byName))
                return byName;
            // A single word (a surname alone) may be someone else: only the aliases of several words, and the acronyms of organisations.
            foreach (var alias in entity.aliases)
                if (IsSynonym(entity.type, alias) && Key(entity.type, alias) is { } aliasKey && _byName.TryGetValue(aliasKey, out var byAlias))
                    return byAlias;
            return null;
        }

        /// <summary>Links or creates each entity; sets its <c>ontologyId</c>, <c>ontologyStatus</c> and <c>ontologyChanges</c>.</summary>
        public void Apply(Page page, List<AnalysedEntity> entities, IReadOnlyList<string> blocks, List<string> warnings, Action<string> log)
        {
            var foldedBlocks = blocks.Select(TextTools.Fold).ToList();
            var link = string.IsNullOrWhiteSpace(page.finalUrl) ? page.url : page.finalUrl;
            int existing = 0, created = 0, changed = 0;
            foreach (var entity in entities)
            {
                var example = TextTools.Passages(blocks, foldedBlocks, entity).Select(p => WritingAnalyzer.Sentences(new[] { p })
                        .FirstOrDefault(s => TextTools.Names(TextTools.Fold(s), TextTools.FoldedNames(entity))) ?? string.Empty)
                    .FirstOrDefault(s => s.Length is > 20 and < 400) ?? string.Empty;
                var known = Find(entity);
                if (known is not null)
                {
                    existing++;
                    entity.ontologyId = known.id;
                    entity.ontologyStatus = "existing";
                    var changes = Complete(known, entity, link, example);
                    if (changes.Count == 0 || !_config.updateOntology)
                        continue;
                    if (Save(known, warnings))
                    {
                        entity.ontologyChanges = changes;
                        changed++;
                    }
                    continue;
                }
                if (!_config.updateOntology)
                    continue;
                var fresh = Create(entity, page, link, example);
                if (!Save(fresh, warnings))
                    continue;
                created++;
                entity.ontologyId = fresh.id;
                entity.ontologyStatus = "created";
                entity.ontologyChanges = new List<string> { "created" };
                Index(fresh);
            }
            log(_config.updateOntology
                ? $"Ontology: {existing} entit(ies) already there ({changed} completed), {created} created"
                : $"Ontology: {existing} entit(ies) already there, {entities.Count - existing} not (updateOntology is false: nothing written)");
        }

        /// <summary>Adds to <paramref name="known"/> what it lacks; what was added, in words.</summary>
        private static List<string> Complete(Ontology known, AnalysedEntity entity, string link, string example)
        {
            var changes = new List<string>();
            known.relatedLinks ??= new List<string>();
            if (link.Length > 0 && !known.relatedLinks.Contains(link, StringComparer.OrdinalIgnoreCase))
            {
                known.relatedLinks.Add(link);
                changes.Add("link of the page added");
            }
            known.synonyms ??= new List<string>();
            var names = known.synonyms.Prepend(known.name ?? string.Empty).Select(TextTools.Fold).ToHashSet();
            foreach (var alias in entity.aliases.Prepend(entity.name))
            {
                if (names.Contains(TextTools.Fold(alias)) || !IsSynonym(entity.type, alias))
                    continue;
                known.synonyms.Add(alias);
                names.Add(TextTools.Fold(alias));
                changes.Add($"synonym \"{alias}\" added");
            }
            if (string.IsNullOrWhiteSpace(known.summary) && entity.summary.Length > 0)
            {
                known.summary = entity.summary;
                changes.Add("summary written");
            }
            if (string.IsNullOrWhiteSpace(known.description) && entity.facts.Length > 0)
            {
                known.description = entity.facts;
                changes.Add("description written");
            }
            known.sentencesExemples ??= new List<string>();
            if (example.Length > 0 && known.sentencesExemples.Count < MostExamples && !known.sentencesExemples.Contains(example))
            {
                known.sentencesExemples.Add(example);
                changes.Add("example sentence added");
            }
            if (entity.type == EntityAnalyzer.Event && known.startDate is null && Date(entity.date) is { } date)
            {
                known.startDate = date;
                changes.Add($"date {date:yyyy-MM-dd} set");
            }
            return changes;
        }

        /// <summary>
        /// True when a spelling of the page may become a synonym in the ontology: a name of several words, or the
        /// acronym of an organisation (capital letters and digits). A word alone is not: a surname would name every
        /// person of that surname, « Commission » every commission, « Cannes » the city as well as its festival.
        /// </summary>
        public static bool IsSynonym(string type, string alias)
        {
            alias = (alias ?? string.Empty).Trim();
            if (alias.Length < 2)
                return false;
            if (alias.Contains(' '))
                return alias.Length >= 5;
            return type == EntityAnalyzer.Organisation && alias.Length is >= 2 and <= 8 && alias.Any(char.IsUpper) && alias.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '.' || c == '-');
        }

        private Ontology Create(AnalysedEntity entity, Page page, string link, string example)
        {
            var now = DateTime.Now;
            var created = new Ontology
            {
                id = Guid.NewGuid().ToString(),
                name = entity.name,
                title = entity.name,
                EntityType = entity.type,
                summary = entity.summary,
                description = entity.facts,
                synonyms = entity.aliases.Where(a => IsSynonym(entity.type, a)).ToList(),
                relatedLinks = link.Length > 0 ? new List<string> { link } : new List<string>(),
                relatedEntities = new List<string>(),
                nodevirtualeftchildrenId = new List<string>(),
                sentencesExemples = example.Length > 0 ? new List<string> { example } : new List<string>(),
                startDate = entity.type == EntityAnalyzer.Event ? Date(entity.date) : null,
                comment = $"Created by KAONOMI.Agent.WebScraper.Analyze from the page \"{page.name}\" ({link})",
                appName = _config.appName,
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                date = now
            };
            return created;
        }

        private bool Save(Ontology entity, List<string> warnings)
        {
            try
            {
                var saved = _session.OntologySave(entity);
                if (saved.ReturnedId is not null and not "-1" and not "")
                    return true;
                warnings.Add($"Ontology: \"{entity.name}\" not saved: {saved.Message}");
            }
            catch (Exception ex)
            {
                warnings.Add($"Ontology: \"{entity.name}\" not saved: {ex.Message}");
            }
            return false;
        }

        private static DateTime? Date(string text) =>
            DateTime.TryParseExact((text ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year is > 1000 and < 2200
                ? date
                : null;
    }
}
