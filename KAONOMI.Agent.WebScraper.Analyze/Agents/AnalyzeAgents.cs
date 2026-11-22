using System.Text.Json;
using KAONOMI.Agent.WebScraper.Analyze.Analysis;
using KAONOMI.Core;
using KAONOMI.Core.Helpers;
using KAONOMI.Core.Models;
using MARI.Framework.Core;
using MARI.Framework.Core.Abstractions;
using MARI.Framework.Core.Abstractions.Attributes;
using Microsoft.Extensions.Logging;

namespace KAONOMI.Agent.WebScraper.Analyze.Agents
{
    /// <summary>Collection stage: the persons, organisations and events of the page, and their entities of the ontology.</summary>
    [VGrainCategory("Collection")]
    public interface iWebScraperAnalyzeCollecVAgent : IVGrain
    {
        /// <summary>Input: the id of a collected page; output: the same id (the analysis started is saved).</summary>
        Task<string> DoProcess(string input, IExecutionContext context);
    }

    /// <summary>Analysis stage: the relations between the entities, their emotional analysis and how the text is written.</summary>
    [VGrainCategory("Analysis")]
    public interface iWebScraperAnalyzeAnalysisVAgent : IVGrain
    {
        /// <summary>Input: the id of the page; output: the same id (the analysis is saved again).</summary>
        Task<string> DoProcess(string input, IExecutionContext context);
    }

    /// <summary>Actions stage: the pictures of the page, then the analysis saved for good.</summary>
    [VGrainCategory("Actions")]
    public interface iWebScraperAnalyzeActionsVAgent : IVGrain
    {
        /// <summary>Input: the id of the page; output: what the analysis holds, in a sentence.</summary>
        Task<string> DoProcess(string input, IExecutionContext context);
    }

    /// <summary>What the three agents share: the configuration, the session, the measure of their time and the analysis in progress.</summary>
    internal static class AgentRun
    {
        public static AnalyzeConfig LoadConfig() =>
            HelperConfigManager.GetFromFile<AnalyzeConfig>("config.json")
            ?? throw new InvalidOperationException("config.json could not be loaded.");

        public static async Task<string> MeasureAsync(string operation, Func<AnalyzeConfig, SessionManager, Task<string>> work)
        {
            var config = LoadConfig();
            var session = new SessionManager(config);
            var perf = new Performance
            {
                date = DateTime.Now,
                comment = $"{operation} execution time.",
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                appName = config.appName,
                operationName = operation,
                metadatas = new List<MetaData>()
            };
            perf.startTime = perf.date;
            try
            {
                return await work(config, session);
            }
            finally
            {
                perf.endTime = DateTime.Now;
                perf.durationMilliseconds = (int)(perf.endTime - perf.startTime).TotalMilliseconds;
                session.PerformanceSave(perf);
            }
        }

        public static void Log(SessionManager session, AnalyzeConfig config, string message, string level = "INFO")
        {
            session.LogSave(message, config.appName, level);
            if (level == "ERROR")
                HelperConsoleTheme.Error(message);
            else
                HelperConsoleTheme.Info(message);
        }

        /// <summary>The time one stage has: what is not done when it ends is left out.</summary>
        public static TimeSpan Budget(AnalyzeConfig config) => TimeSpan.FromMinutes(Math.Clamp(config.stageBudgetMinutes, 0.5, 9));

        /// <summary>Saves the analysis in progress; false (and an error told) when the search engine refused it.</summary>
        public static bool Save(SessionManager session, AnalyzeConfig config, PageAnalysis analysis)
        {
            var saved = session.PageAnalysisSave(analysis);
            if (saved.Success && saved.ReturnedId != "-1")
                return true;
            Log(session, config, $"The analysis of \"{analysis.sourcePageName}\" was not saved: {saved.Message}", "ERROR");
            return false;
        }

        /// <summary>The page and its analysis in progress for a later stage; null (told) when one is missing.</summary>
        public static (Page Page, PageAnalysis Analysis)? Load(SessionManager session, AnalyzeConfig config, string pageId)
        {
            var page = session.PageGet(pageId);
            var analysis = page is null ? null : session.PageAnalysisGet(pageId);
            if (page is not null && analysis is not null)
                return (page, analysis);
            Log(session, config, page is null ? $"Page {pageId} not found" : $"The analysis of the page {pageId} was not started", "ERROR");
            return null;
        }
    }

    /// <summary>
    /// Starts the analysis of a page: the model of the advanced conversations settles its persons, organisations and
    /// events (<see cref="EntityAnalyzer"/>), then each one is linked to its entity of the ontology, completed, or
    /// created there (<see cref="OntologyLinker"/>). The analysis is saved (index <c>pageanalysis</c>).
    /// </summary>
    internal sealed class WebScraperAnalyzeCollecVAgent : VGrainBase<iWebScraperAnalyzeCollecVAgent>, iWebScraperAnalyzeCollecVAgent
    {
        public WebScraperAnalyzeCollecVAgent(ILogger<iWebScraperAnalyzeCollecVAgent> logger) : base(logger)
        {
        }

        public Task<string> DoProcess(string input, IExecutionContext context) =>
            AgentRun.MeasureAsync("WebScraperAnalyzeCollecVAgent.DoProcess", async (config, session) =>
            {
                var pageId = (input ?? string.Empty).Trim();
                var page = session.PageGet(pageId);
                if (page is null)
                {
                    AgentRun.Log(session, config, $"Page {pageId} not found in the index page", "ERROR");
                    return string.Empty;
                }
                var (model, from) = config.TextModel();
                var now = DateTime.Now;
                var analysis = new PageAnalysis
                {
                    id = PageAnalysis.IdFor(page.id),
                    sourcepageid = page.id,
                    sourcePageName = page.name,
                    title = page.name,
                    sourceUrl = string.IsNullOrWhiteSpace(page.finalUrl) ? page.url : page.finalUrl,
                    sourceContentHash = page.contentHash,
                    collectionPlanId = page.collectionPlanId,
                    collectionPlanName = page.collectionPlanName,
                    websiteName = page.websiteName,
                    day = page.day,
                    model = model,
                    analysedAt = now,
                    date = now,
                    appName = config.appName,
                    machineName = Environment.MachineName,
                    userName = Environment.UserName
                };
                var blocks = TextTools.Blocks(page);
                AgentRun.Log(session, config, $"Analysing \"{page.name}\": {blocks.Count} block(s) of text, {page.entities?.Count ?? 0} entit(ies) found at the collection · model {model} ({from})");
                void Told(string message) => AgentRun.Log(session, config, message);

                using var llm = new Llm(config);
                using var budget = new CancellationTokenSource(AgentRun.Budget(config));
                analysis.entities = await EntityAnalyzer.AnalyseAsync(llm, model, config, page, blocks, analysis.warnings, Told, budget.Token);
                analysis.steps.Add("entities");
                Told($"Entities: {analysis.entities.Count(e => e.type == EntityAnalyzer.Person)} person(s), {analysis.entities.Count(e => e.type == EntityAnalyzer.Organisation)} organisation(s), "
                     + $"{analysis.entities.Count(e => e.type == EntityAnalyzer.Event)} event(s)");

                try
                {
                    new OntologyLinker(session, config).Apply(page, analysis.entities, blocks, analysis.warnings, Told);
                    analysis.steps.Add("ontology");
                }
                catch (Exception ex)
                {
                    analysis.warnings.Add($"Ontology not read: {ex.Message}");
                    AgentRun.Log(session, config, $"The ontology could not be read: {ex.Message}", "ERROR");
                }
                return AgentRun.Save(session, config, analysis) ? page.id : string.Empty;
            });
    }

    /// <summary>
    /// The relations between the entities as the data of a graph (<see cref="RelationAnalyzer"/>), the emotional
    /// analysis of each entity (<see cref="EmotionAnalyzer"/>) and how the page, and the passages about each entity,
    /// are written (<see cref="WritingAnalyzer"/>).
    /// </summary>
    internal sealed class WebScraperAnalyzeAnalysisVAgent : VGrainBase<iWebScraperAnalyzeAnalysisVAgent>, iWebScraperAnalyzeAnalysisVAgent
    {
        public WebScraperAnalyzeAnalysisVAgent(ILogger<iWebScraperAnalyzeAnalysisVAgent> logger) : base(logger)
        {
        }

        public Task<string> DoProcess(string input, IExecutionContext context) =>
            AgentRun.MeasureAsync("WebScraperAnalyzeAnalysisVAgent.DoProcess", async (config, session) =>
            {
                if (string.IsNullOrWhiteSpace(input) || AgentRun.Load(session, config, input.Trim()) is not var (page, analysis))
                    return string.Empty;
                void Told(string message) => AgentRun.Log(session, config, message);
                var blocks = TextTools.Blocks(page);
                var deadline = DateTime.Now + AgentRun.Budget(config);
                using var llm = new Llm(config);

                // How it is written: counted on the words, no model.
                analysis.writing = WritingAnalyzer.Profile(blocks, page.pageLanguage) ?? new WritingProfile();
                var foldedBlocks = blocks.Select(TextTools.Fold).ToList();
                foreach (var entity in analysis.entities)
                    entity.writing = WritingAnalyzer.Profile(TextTools.Passages(blocks, foldedBlocks, entity), page.pageLanguage);
                analysis.steps.Add("writing");
                Told($"Writing: {analysis.writing.sentenceCount} sentence(s) of {analysis.writing.averageSentenceLength:0.#} word(s) on average, vocabulary {analysis.writing.vocabularyComplexity:0}/100, "
                     + $"{analysis.writing.adverbCount} adverb(s), {analysis.writing.conditionalSentenceCount} sentence(s) in the conditional");

                // The relations: a little more than half of the time of the stage, the emotions the rest.
                analysis.possibleRelations = RelationAnalyzer.Possible.ToList();
                using (var forRelations = new CancellationTokenSource(AgentRun.Budget(config) * 0.55))
                    analysis.graph = await RelationAnalyzer.AnalyseAsync(llm, analysis.model, config, page, blocks, analysis.entities, analysis.warnings, Told, forRelations.Token);
                analysis.steps.Add("relations");
                Told($"Relations: {analysis.graph.nodes.Count} node(s), {analysis.graph.edges.Count} edge(s)");

                var left = deadline - DateTime.Now;
                using (var forEmotions = new CancellationTokenSource(left > TimeSpan.FromSeconds(5) ? left : TimeSpan.FromSeconds(5)))
                    await EmotionAnalyzer.AnalyseAsync(llm, analysis.model, config, blocks, analysis.entities, analysis.warnings, Told, forEmotions.Token);
                analysis.steps.Add("emotions");
                return AgentRun.Save(session, config, analysis) ? page.id : string.Empty;
            });
    }

    /// <summary>The pictures of the page analysed by the image model (<see cref="ImageAnalyzer"/>), then the analysis saved for good.</summary>
    internal sealed class WebScraperAnalyzeActionsVAgent : VGrainBase<iWebScraperAnalyzeActionsVAgent>, iWebScraperAnalyzeActionsVAgent
    {
        public WebScraperAnalyzeActionsVAgent(ILogger<iWebScraperAnalyzeActionsVAgent> logger) : base(logger)
        {
        }

        public Task<string> DoProcess(string input, IExecutionContext context) =>
            AgentRun.MeasureAsync("WebScraperAnalyzeActionsVAgent.DoProcess", async (config, session) =>
            {
                if (string.IsNullOrWhiteSpace(input) || AgentRun.Load(session, config, input.Trim()) is not var (page, analysis))
                    return string.Empty;
                void Told(string message) => AgentRun.Log(session, config, message);
                var (imageModel, from) = config.PictureModel();
                analysis.imageModel = imageModel;
                Told($"Pictures: {page.images?.Count(i => !string.IsNullOrWhiteSpace(i.path)) ?? 0} downloaded with the page · model {imageModel} ({from})");

                using var llm = new Llm(config);
                using var budget = new CancellationTokenSource(AgentRun.Budget(config));
                analysis.images = await ImageAnalyzer.AnalyseAsync(llm, imageModel, config, page, analysis.entities, analysis.warnings, Told, budget.Token);
                analysis.steps.Add("images");
                analysis.durationMs = (int)Math.Min(int.MaxValue, (DateTime.Now - analysis.analysedAt).TotalMilliseconds);
                if (!AgentRun.Save(session, config, analysis))
                    return string.Empty;

                var summary = JsonSerializer.Serialize(new
                {
                    page = page.name,
                    entities = analysis.entities.Count,
                    existing = analysis.entities.Count(e => e.ontologyStatus == "existing"),
                    created = analysis.entities.Count(e => e.ontologyStatus == "created"),
                    relations = analysis.graph.edges.Count,
                    emotions = analysis.entities.Count(e => e.emotion is not null),
                    images = analysis.images.Count(i => i.error.Length == 0),
                    warnings = analysis.warnings.Count
                });
                Told($"\"{page.name}\" analysed in {analysis.durationMs / 1000.0:0} s: {analysis.entities.Count} entit(ies) "
                     + $"({analysis.entities.Count(e => e.ontologyStatus == "existing")} in the ontology, {analysis.entities.Count(e => e.ontologyStatus == "created")} created), "
                     + $"{analysis.graph.edges.Count} relation(s), {analysis.entities.Count(e => e.emotion is not null)} emotional analys(es), "
                     + $"{analysis.images.Count(i => i.error.Length == 0)} picture(s){(analysis.warnings.Count > 0 ? $", {analysis.warnings.Count} warning(s)" : string.Empty)}");
                return summary;
            });
    }
}
