using System.Diagnostics;
using KAONOMI.Agent.WebScraper.Collect.Scraping;
using KAONOMI.Core;
using KAONOMI.Core.Helpers;
using KAONOMI.Core.Models;
using MARI.Framework.Core;
using MARI.Framework.Core.Abstractions;
using MARI.Framework.Core.Abstractions.Attributes;
using Microsoft.Extensions.Logging;

namespace KAONOMI.Agent.WebScraper.Collect.Agents
{
    /// <summary>Collection stage: the URLs to collect (of a collection plan, of the active ones, or of the command line).</summary>
    [VGrainCategory("Collection")]
    public interface iWebScraperCollectCollecVAgent : IVGrain
    {
        /// <summary>Input: a <see cref="CollectRequest"/> (JSON); output: the <see cref="CollectJob"/> list (JSON).</summary>
        Task<string> DoProcess(string input, IExecutionContext context);
    }

    /// <summary>Analysis stage: collects the pages (content, entities, URLs, pictures, rendering) and saves each one as soon as it is collected.</summary>
    [VGrainCategory("Analysis")]
    public interface iWebScraperCollectAnalysisVAgent : IVGrain
    {
        /// <summary>Input: the <see cref="CollectJob"/> list (JSON); output: a <see cref="CollectSummary"/> (JSON).</summary>
        Task<string> DoProcess(string input, IExecutionContext context);
    }

    /// <summary>Actions stage: reports what the run saved in the search engine (index <c>page</c>).</summary>
    [VGrainCategory("Actions")]
    public interface iWebScraperCollectActionsVAgent : IVGrain
    {
        /// <summary>Input: a <see cref="CollectSummary"/> (JSON); output: the summary of what was saved, in a sentence.</summary>
        Task<string> DoProcess(string input, IExecutionContext context);
    }

    /// <summary>What the three agents share: the configuration, the session and the measure of their time.</summary>
    internal static class AgentRun
    {
        public static CollectConfig LoadConfig() =>
            HelperConfigManager.GetFromFile<CollectConfig>("config.json")
            ?? throw new InvalidOperationException("config.json could not be loaded.");

        public static async Task<string> MeasureAsync(string operation, Func<CollectConfig, SessionManager, Task<string>> work)
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

        public static void Log(SessionManager session, CollectConfig config, string message, string level = "INFO")
        {
            session.LogSave(message, config.appName, level);
            if (level == "ERROR")
                HelperConsoleTheme.Error(message);
            else
                HelperConsoleTheme.Info(message);
        }
    }

    /// <summary>
    /// The URLs to collect: those of the command line; or every URL of the websites of the collection plan asked
    /// (<c>--plan</c>, active or not: all of them are collected); or those of the active collection plans (not deleted,
    /// <c>isActive</c>). Each one with its mode, its keywords and whether it is a main page.
    /// </summary>
    internal sealed class WebScraperCollectCollecVAgent : VGrainBase<iWebScraperCollectCollecVAgent>, iWebScraperCollectCollecVAgent
    {
        public WebScraperCollectCollecVAgent(ILogger<iWebScraperCollectCollecVAgent> logger) : base(logger)
        {
        }

        public Task<string> DoProcess(string input, IExecutionContext context) =>
            AgentRun.MeasureAsync("WebScraperCollectCollecVAgent.DoProcess", (config, session) =>
            {
                var request = SequenceJson.Read<CollectRequest>(input);
                var jobs = new List<CollectJob>();
                if (request.importFile.Length > 0)
                {
                    // A page copied by the user (--import --url [--plan]): its URL as the plan has it (website, mode, main page).
                    var address = request.urls.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))?.Trim() ?? string.Empty;
                    if (!Uri.TryCreate(address, UriKind.Absolute, out var pasted))
                    {
                        AgentRun.Log(session, config, "--import needs the URL of the page: --url <url>", "ERROR");
                        return Task.FromResult(SequenceJson.Write(jobs));
                    }
                    var job = new CollectJob { url = address, mode = request.mode, isMainPage = request.isMainPage, keywords = request.keywords, collectionPlanName = "command line", always = true, importFile = request.importFile };
                    var plan = request.planId.Length > 0 ? session.CollectionPlanGet(request.planId) : null;
                    if (plan is not null)
                    {
                        job.collectionPlanId = plan.id;
                        job.collectionPlanName = plan.name;
                        foreach (var website in plan.websites ?? new List<Website>())
                        {
                            var process = (website.urlProcesses ?? new List<UrlProcess>()).FirstOrDefault(p =>
                                Uri.TryCreate((p.url ?? string.Empty).Trim(), UriKind.Absolute, out var u)
                                && string.Equals(PageNames.Normalize(u), PageNames.Normalize(pasted), StringComparison.OrdinalIgnoreCase));
                            if (process is null)
                                continue;
                            job.websiteName = website.name;
                            job.mode = string.IsNullOrWhiteSpace(process.mode) ? "fixed" : process.mode;
                            job.isMainPage = process.isMainPage;
                            job.keywords = process.keywords ?? new List<string>();
                            break;
                        }
                    }
                    jobs.Add(job);
                    AgentRun.Log(session, config, $"Page copied by hand to import: {address}{(job.isMainPage ? " (main page)" : string.Empty)}");
                    return Task.FromResult(SequenceJson.Write(jobs));
                }
                if (request.urls.Count > 0)
                {
                    jobs.AddRange(request.urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => new CollectJob
                    {
                        url = u.Trim(),
                        mode = request.mode,
                        isMainPage = request.isMainPage,
                        keywords = request.keywords,
                        collectionPlanName = "command line",
                        always = true
                    }));
                    AgentRun.Log(session, config, $"{jobs.Count} URL(s) from the command line");
                    return Task.FromResult(SequenceJson.Write(jobs));
                }

                // One plan asked (--plan): collected now, active or not.
                var asked = request.planId.Length > 0;
                List<CollectionPlan> plans;
                if (asked)
                {
                    var plan = session.CollectionPlanGet(request.planId);
                    if (plan is null || plan.isDeleted)
                    {
                        AgentRun.Log(session, config, $"Collection plan {request.planId} not found (or deleted)", "ERROR");
                        return Task.FromResult(SequenceJson.Write(jobs));
                    }
                    plans = new List<CollectionPlan> { plan };
                }
                else
                {
                    plans = session.CollectionPlansLast(500).Where(p => p.isActive && !p.isDeleted).ToList();
                }
                foreach (var plan in plans)
                    foreach (var website in plan.websites ?? new List<Website>())
                        foreach (var process in website.urlProcesses ?? new List<UrlProcess>())
                        {
                            if (string.IsNullOrWhiteSpace(process.url))
                                continue;
                            jobs.Add(new CollectJob
                            {
                                url = process.url.Trim(),
                                mode = string.IsNullOrWhiteSpace(process.mode) ? "fixed" : process.mode,
                                isMainPage = process.isMainPage,
                                keywords = process.keywords ?? new List<string>(),
                                collectionPlanId = plan.id,
                                collectionPlanName = plan.name,
                                websiteName = website.name,
                                always = asked
                            });
                        }
                var main = jobs.Count(j => j.isMainPage);
                AgentRun.Log(session, config, asked
                    ? $"Collection plan \"{plans[0].name}\": {jobs.Count} URL(s) to collect, {main} main page(s)"
                    : $"{plans.Count} active collection plan(s): {jobs.Count} URL(s) to collect, {main} main page(s)");
                return Task.FromResult(SequenceJson.Write(jobs));
            });
    }

    /// <summary>
    /// Collects the URLs with the shared browser (<see cref="PageCollector"/>), within the time budget of a run
    /// (<see cref="CollectConfig.runBudgetMinutes"/>): the URLs not reached are left for the next run. Each page is
    /// saved in the search engine (index <c>page</c>) as soon as it is collected, under its id: a main page collected
    /// again the same day, or any other page collected again, overwrites its document; a run stopped before its end
    /// keeps the pages saved so far.
    /// </summary>
    internal sealed class WebScraperCollectAnalysisVAgent : VGrainBase<iWebScraperCollectAnalysisVAgent>, iWebScraperCollectAnalysisVAgent
    {
        public WebScraperCollectAnalysisVAgent(ILogger<iWebScraperCollectAnalysisVAgent> logger) : base(logger)
        {
        }

        public Task<string> DoProcess(string input, IExecutionContext context) =>
            AgentRun.MeasureAsync("WebScraperCollectAnalysisVAgent.DoProcess", async (config, session) =>
            {
                var jobs = SequenceJson.Read<List<CollectJob>>(input);
                var result = new CollectSummary();
                if (jobs.Count == 0)
                    return SequenceJson.Write(result);

                void Save(Page page, List<DetectedEntity> detected)
                {
                    var saved = session.PageSave(page);
                    if (!saved.Success || saved.ReturnedId == "-1")
                    {
                        result.failed++;
                        AgentRun.Log(session, config, $"Page {page.url} not saved: {saved.Message}", "ERROR");
                        return;
                    }
                    result.pages++;
                    result.entities += page.entities.Count;
                    result.urls += page.urls.Count;
                    result.pictures += page.images.Count(i => i.path.Length > 0);
                    result.renderings += page.renderingPath.Length > 0 ? 1 : 0;
                    result.names.Add(page.name);
                    // Every entity the NER found in the page, in the index detectedentity.
                    if (detected.Count > 0)
                    {
                        var kept = session.DetectedEntitiesOfPageSave(page.id, detected);
                        if (int.TryParse(kept.ReturnedId, out var count))
                            result.detected += count;
                        if (!kept.Success || kept.Message?.Contains("failed", StringComparison.Ordinal) == true)
                            AgentRun.Log(session, config, $"Entities of {page.url} not all saved in detectedentity: {kept.Message}", "ERROR");
                    }
                }

                // The pages copied by hand (--import): kept as they were copied, no browser, nothing asked to the site.
                foreach (var import in jobs.Where(j => j.importFile.Length > 0))
                {
                    try
                    {
                        using var ner = new NerClient(config.ServerUrl);
                        var content = await File.ReadAllTextAsync(import.importFile);
                        var detected = new List<DetectedEntity>();
                        var page = await PageImporter.ImportAsync(config, import, content, ner, detected, message => AgentRun.Log(session, config, message), CancellationToken.None);
                        if (page is not null)
                            Save(page, detected);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AgentRun.Log(session, config, $"The page copied could not be read ({import.importFile}): {ex.Message}", "ERROR");
                    }
                }
                jobs = jobs.Where(j => j.importFile.Length == 0).ToList();
                if (jobs.Count == 0)
                    return SequenceJson.Write(result);

                PageCollector collector;
                try
                {
                    collector = await PageCollector.GetAsync(config);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Microsoft.Playwright.PlaywrightException)
                {
                    AgentRun.Log(session, config, ex.Message.Split('\n')[0], "ERROR");
                    return SequenceJson.Write(result);
                }
                AgentRun.Log(session, config, $"Browser: {collector.Browser} · renderings and pictures in {config.ResolvedOutputFolder}");

                using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(Math.Max(1, config.runBudgetMinutes)));
                var watch = Stopwatch.StartNew();
                var done = 0;
                var collected = new HashSet<string>(StringComparer.Ordinal);
                foreach (var job in jobs)
                {
                    if (budget.IsCancellationRequested)
                        break;
                    try
                    {
                        AgentRun.Log(session, config, $"Collecting {job.url} ({job.mode}{(job.isMainPage ? ", main page" : "")}{(job.websiteName.Length > 0 ? ", " + job.websiteName : "")})");
                        await collector.CollectAsync(job, session, collected, Save, message => AgentRun.Log(session, config, message), budget.Token);
                        done++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AgentRun.Log(session, config, $"Collection of {job.url} failed: {ex.Message.Split('\n')[0]}", "ERROR");
                    }
                }
                AgentRun.Log(session, config, $"{result.pages + result.failed} page(s) collected from {done}/{jobs.Count} URL(s) in {watch.Elapsed.TotalSeconds:0} s"
                                              + (done < jobs.Count ? $" (time budget of {config.runBudgetMinutes} min reached, the rest next run)" : string.Empty));
                return SequenceJson.Write(result);
            });
    }

    /// <summary>Reports what the run saved: how many pages, entities, URLs, pictures and renderings.</summary>
    internal sealed class WebScraperCollectActionsVAgent : VGrainBase<iWebScraperCollectActionsVAgent>, iWebScraperCollectActionsVAgent
    {
        public WebScraperCollectActionsVAgent(ILogger<iWebScraperCollectActionsVAgent> logger) : base(logger)
        {
        }

        public Task<string> DoProcess(string input, IExecutionContext context) =>
            AgentRun.MeasureAsync("WebScraperCollectActionsVAgent.DoProcess", (config, session) =>
            {
                var result = SequenceJson.Read<CollectSummary>(input);
                var summary = $"Saved {result.pages} page(s), {result.entities} entit(ies) ({result.detected} found by the NER in detectedentity), {result.urls} URL(s), {result.pictures} picture(s), {result.renderings} rendering(s)"
                              + (result.failed > 0 ? $", {result.failed} page(s) not saved" : string.Empty);
                if (result.pages + result.failed > 0)
                    session.LogSave(summary + ": " + string.Join(" · ", result.names), config.appName, "INFO");
                return Task.FromResult(summary);
            });
    }
}
