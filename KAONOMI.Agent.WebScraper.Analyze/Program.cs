namespace KAONOMI.Agent.WebScraper.Analyze
{
    using System.Text.Json;
    using KAONOMI.Agent.WebScraper.Analyze.Agents;
    using KAONOMI.Core;
    using KAONOMI.Core.Helpers;
    using KAONOMI.Core.Hosting;
    using KAONOMI.Core.Models;
    using MARI.Framework.Builders;
    using MARI.Framework.Configurations;
    using MARI.Framework.Core.Abstractions;
    using MARI.Framework.Node.Abstractions.Models;
    using MARI.Framework.Node.Extensions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Console host of the agent that analyses the pages collected by KAONOMI.Agent.WebScraper.Collect, one-shot:
    /// started on demand (/process, a terminal), it analyses a few pages, one after the other, and stops. For each
    /// page its sequence runs once: the persons, organisations and events and the ontology → the relations, the
    /// emotions and the writing → the pictures; the analysis is saved in the index <c>pageanalysis</c> (one document
    /// per page, <c>sourcepageid</c>: the id of the page).
    /// Command line: <c>--page &lt;id&gt;</c> (repeatable) analyses these pages; <c>--plan &lt;id&gt;</c> the pages of
    /// that collection plan; without them, the newest pages. A page analysed already is left out unless its content
    /// changed since, or <c>--force</c>; <c>--max &lt;n&gt;</c>: how many pages at most (maxPagesPerRun by default).
    /// </summary>
    internal class Program
    {
        private static SessionManager _session = null!;

        private sealed record Arguments(List<string> Pages, string Plan, bool Force, int Max, string[] Host);

        static async Task Main(string[] args)
        {
            var config = HelperConfigManager.GetFromFile<AnalyzeConfig>("config.json")
                ?? throw new InvalidOperationException("config.json could not be loaded.");
            var appName = config.appName;
            var arguments = ParseArguments(args, config);

            HelperConsoleTheme.PlaceOnSmallestScreen(slot: 6, slotCount: 7);
            HelperConsoleTheme.Start(config.appName, config.appDescription, config.appDocumentationUrl, config.consoleTheme);

            _session = new SessionManager(config);
            _session.LogSave("Application started", appName, "Info");

            var sequence = Sequence.Build("webscraperanalyze", fixUid: new Guid("B2E64D09-7A1F-4C38-9D52-3F8A60C1E7D4"), metadataBuilder: m =>
            {
                m.Description("Web scraper of the collection plans: analyses a collected page (entities and ontology, relations, emotions, writing, pictures)")
                 .AddTags("webscraper", "collectionplan", "page", "ontology", "analysis");
            })
                .RequiredInput<string>()
                .Use<iWebScraperAnalyzeCollecVAgent>().Configure("WEBSCRAPERANALYZE-TABLE").Call((g, i, ctx) => g.DoProcess(i, ctx)).Return
                .Use<iWebScraperAnalyzeAnalysisVAgent>().Configure("WEBSCRAPERANALYZE-TABLE").Call((g, i, ctx) => g.DoProcess(i, ctx)).Return
                .Use<iWebScraperAnalyzeActionsVAgent>().Configure("WEBSCRAPERANALYZE-TABLE").Call((g, i, ctx) => g.DoProcess(i, ctx)).Return
                .Build();

            using var host = Host.CreateDefaultBuilder(arguments.Host)
                .UseMARINode(b =>
                {
                    b.WizardConfig()
                     .UseMongoCluster(mongo => mongo
                         .ConnectionString("mongodb://localhost:27017")
                         .ManualConfig((options, _) => options.DatabaseName = "kaonomi"),
                         serviceId: KaonomiSiloBuilderExtensions.ServiceId,
                         clusterId: KaonomiSiloBuilderExtensions.ClusterId)
                     .AddEndpointOptions(new ClusterNodeEndPointOptions(loopback: true, siloPort: 11119, gatewayPort: 30008))
                     .ConfigureLogging(c => c.AddConsole())
                     .AddInMemoryDefinitionProvider(d => d.SetupSequences(sequence));
                    b.ManualyAdvancedConfig().UseKaonomiNode("Kaonomi.WebScraperAnalyze");
                })
                .Build();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await host.StartAsync(cts.Token);
            HelperConsoleTheme.Info("One run, then stop.");

            try
            {
                var handler = host.Services.GetRequiredService<IMARIExecutionHandler>();
                var run = AnalysePagesAsync(handler, sequence.Uid, config, arguments, cts.Token);
                // Ctrl+C stops the run (the analyses saved stay).
                await Task.WhenAny(run, Task.Delay(Timeout.Infinite, cts.Token));
                if (!run.IsCompleted)
                    HelperConsoleTheme.Info("Run stopped (Ctrl+C).");
            }
            finally
            {
                await host.StopAsync();
            }
        }

        private static Arguments ParseArguments(string[] args, AnalyzeConfig config)
        {
            var pages = new List<string>();
            var plan = string.Empty;
            var force = false;
            var max = Math.Max(1, config.maxPagesPerRun);
            var rest = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--page" when i + 1 < args.Length:
                        pages.Add(args[++i]);
                        break;
                    case "--plan" when i + 1 < args.Length:
                        plan = args[++i];
                        break;
                    case "--force":
                        force = true;
                        break;
                    case "--max" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0:
                        max = n;
                        i++;
                        break;
                    default:
                        rest.Add(args[i]);
                        break;
                }
            }
            return new Arguments(pages, plan, force, max, rest.ToArray());
        }

        /// <summary>
        /// The pages to analyse: those asked; else the newest pages (of the plan asked), without those analysed
        /// already whose content did not change (unless --force), at most <c>Max</c>.
        /// </summary>
        private static List<(string Id, string Name)> PagesToAnalyse(Arguments arguments)
        {
            if (arguments.Pages.Count > 0)
            {
                var asked = new List<(string, string)>();
                foreach (var id in arguments.Pages.Distinct())
                {
                    var page = _session.PageGet(id);
                    if (page is null || page.isDeleted)
                        HelperConsoleTheme.Error($"Page {id} not found in the index page");
                    else
                        asked.Add((page.id, page.name));
                }
                return asked;
            }
            var pages = _session.PagesLast(300)
                .Where(p => arguments.Plan.Length == 0 || p.collectionPlanId == arguments.Plan)
                .OrderByDescending(p => p.scrapedAt)
                .ToList();
            var chosen = new List<(string, string)>();
            var analysed = 0;
            foreach (var page in pages)
            {
                if (!arguments.Force && _session.PageAnalysisGet(page.id) is { } known && known.sourceContentHash == page.contentHash && known.steps.Contains("images"))
                {
                    analysed++;
                    continue;
                }
                if (chosen.Count < arguments.Max)
                    chosen.Add((page.id, page.name));
            }
            HelperConsoleTheme.Info($"{pages.Count} collected page(s){(arguments.Plan.Length > 0 ? " of the plan " + arguments.Plan : string.Empty)}: {analysed} analysed already, "
                                    + $"{pages.Count - analysed} to analyse, {chosen.Count} in this run (at most {arguments.Max})");
            return chosen;
        }

        /// <summary>Runs the sequence once per page and tells what was done.</summary>
        private static async Task AnalysePagesAsync(IMARIExecutionHandler handler, Guid sequenceUid, AnalyzeConfig config, Arguments arguments, CancellationToken cancellationToken)
        {
            var appName = config.appName;
            List<(string Id, string Name)> pages;
            try
            {
                pages = PagesToAnalyse(arguments);
            }
            catch (Exception ex)
            {
                HelperConsoleTheme.Error($"The collected pages could not be read: {ex.Message}");
                HelperConsoleTheme.Info("Run done: Analysed 0 page(s)");
                return;
            }
            int done = 0, entities = 0, created = 0, relations = 0, images = 0;
            foreach (var (id, name) in pages)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                try
                {
                    var result = await handler.Sequence<string>(sequenceUid).SetInput(id).RunAsync<string>();
                    var summary = result.SafeGetResult();
                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        HelperConsoleTheme.Error($"\"{name}\" was not analysed (see above)");
                        continue;
                    }
                    using var told = JsonDocument.Parse(summary);
                    done++;
                    entities += told.RootElement.GetProperty("entities").GetInt32();
                    created += told.RootElement.GetProperty("created").GetInt32();
                    relations += told.RootElement.GetProperty("relations").GetInt32();
                    images += told.RootElement.GetProperty("images").GetInt32();
                    _session.LogSave($"Sequence executed: {summary}", appName, "INFO");
                }
                catch (Exception ex)
                {
                    _session.LogSave($"Sequence executed with Error on the page {id}: {ex.Message}", appName, "ERROR");
                    HelperConsoleTheme.Error($"Analysis of \"{name}\" failed: {ex.Message}");
                }
            }
            HelperConsoleTheme.Info($"Run done: Analysed {done} page(s) of {pages.Count}, {entities} entit(ies) ({created} created in the ontology), {relations} relation(s), {images} picture(s)");
        }
    }
}
