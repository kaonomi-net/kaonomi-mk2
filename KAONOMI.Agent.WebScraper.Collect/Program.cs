namespace KAONOMI.Agent.WebScraper.Collect
{
    using KAONOMI.Agent.WebScraper.Collect.Agents;
    using KAONOMI.Agent.WebScraper.Collect.Scraping;
    using KAONOMI.Core;
    using KAONOMI.Core.Helpers;
    using KAONOMI.Core.Hosting;
    using MARI.Framework.Builders;
    using MARI.Framework.Configurations;
    using MARI.Framework.Core.Abstractions;
    using MARI.Framework.Node.Abstractions.Models;
    using MARI.Framework.Node.Extensions;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Console host of the agent that collects the pages of the collection plans, one-shot: started on demand (RUN of
    /// the collection plan window of the chat, /process, a terminal), it runs its sequence once (the URLs to collect →
    /// the pages collected → the pages saved, index <c>page</c>) and stops. It never runs in a loop.
    /// Command line: <c>--plan &lt;id&gt;</c> collects that collection plan now, active or not (what RUN does);
    /// <c>--url &lt;url&gt;</c> (repeatable) collects these URLs instead, in the mode of
    /// <c>--mode fixed|explore|random</c> (fixed by default), with the keywords of <c>--keywords "a,b"</c>, as main
    /// pages with <c>--main</c>; without them, the active collection plans. <c>--import &lt;file&gt; --url &lt;url&gt;</c>
    /// (with <c>--plan &lt;id&gt;</c>) keeps the content of the file, copied by the user in a browser, as the page of
    /// that URL, without asking anything to the site (PASTE PAGE of the chat, for a site that refuses the agent).
    /// </summary>
    internal class Program
    {
        private static SessionManager _session = null!;

        static async Task Main(string[] args)
        {
            var config = HelperConfigManager.GetFromFile<CollectConfig>("config.json")
                ?? throw new InvalidOperationException("config.json could not be loaded.");
            var appName = config.appName;
            var (request, hostArgs) = ParseArguments(args);

            HelperConsoleTheme.PlaceOnSmallestScreen(slot: 6, slotCount: 7);
            HelperConsoleTheme.Start(config.appName, config.appDescription, config.appDocumentationUrl, config.consoleTheme);

            _session = new SessionManager(config);
            _session.LogSave("Application started", appName, "Info");

            var sequence = Sequence.Build("webscrapercollect", fixUid: new Guid("7C3F5A18-2D64-4B9E-8F01-A5D2C9E47B60"), metadataBuilder: m =>
            {
                m.Description("Web scraper of the collection plans: collects the pages (titles and paragraphs, entities, URLs, pictures, rendering)")
                 .AddTags("webscraper", "collectionplan", "page", "ner");
            })
                .RequiredInput<string>()
                .Use<iWebScraperCollectCollecVAgent>().Configure("WEBSCRAPERCOLLECT-TABLE").Call((g, i, ctx) => g.DoProcess(i, ctx)).Return
                .Use<iWebScraperCollectAnalysisVAgent>().Configure("WEBSCRAPERCOLLECT-TABLE").Call((g, i, ctx) => g.DoProcess(i, ctx)).Return
                .Use<iWebScraperCollectActionsVAgent>().Configure("WEBSCRAPERCOLLECT-TABLE").Call((g, i, ctx) => g.DoProcess(i, ctx)).Return
                .Build();

            using var host = Host.CreateDefaultBuilder(hostArgs)
                .UseMARINode(b =>
                {
                    b.WizardConfig()
                     .UseMongoCluster(mongo => mongo
                         .ConnectionString("mongodb://localhost:27017")
                         .ManualConfig((options, _) => options.DatabaseName = "kaonomi"),
                         serviceId: KaonomiSiloBuilderExtensions.ServiceId,
                         clusterId: KaonomiSiloBuilderExtensions.ClusterId)
                     .AddEndpointOptions(new ClusterNodeEndPointOptions(loopback: true, siloPort: 11118, gatewayPort: 30007))
                     .ConfigureLogging(c => c.AddConsole())
                     .AddInMemoryDefinitionProvider(d => d.SetupSequences(sequence));
                    b.ManualyAdvancedConfig().UseKaonomiNode("Kaonomi.WebScraperCollect");
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
                var run = ExecuteSequenceAsync(handler, sequence.Uid, appName, SequenceJson.Write(request));
                // Ctrl+C stops the run (what was saved stays).
                await Task.WhenAny(run, Task.Delay(Timeout.Infinite, cts.Token));
                if (!run.IsCompleted)
                    HelperConsoleTheme.Info("Run stopped (Ctrl+C).");
            }
            finally
            {
                await PageCollector.CloseAsync();
                await host.StopAsync();
            }
        }

        /// <summary>The URLs, plan, mode and keywords of the command line, and the arguments left for the host.</summary>
        private static (CollectRequest Request, string[] HostArgs) ParseArguments(string[] args)
        {
            var request = new CollectRequest();
            var rest = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--url" when i + 1 < args.Length:
                        request.urls.Add(args[++i]);
                        break;
                    case "--plan" when i + 1 < args.Length:
                        request.planId = args[++i];
                        break;
                    case "--mode" when i + 1 < args.Length:
                        request.mode = args[++i];
                        break;
                    case "--main":
                        request.isMainPage = true;
                        break;
                    case "--import" when i + 1 < args.Length:
                        request.importFile = args[++i];
                        break;
                    case "--keywords" when i + 1 < args.Length:
                        request.keywords = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        break;
                    default:
                        rest.Add(args[i]);
                        break;
                }
            }
            return (request, rest.ToArray());
        }

        /// <summary>Runs the sequence once and logs success or failure.</summary>
        private static async Task ExecuteSequenceAsync(IMARIExecutionHandler handler, Guid sequenceUid, string appName, string input)
        {
            try
            {
                var result = await handler.Sequence<string>(sequenceUid).SetInput(input).RunAsync<string>();
                var summary = result.SafeGetResult();
                _session.LogSave($"Sequence executed: {summary}", appName, "INFO");
                HelperConsoleTheme.Info($"Run done: {summary}");
            }
            catch (Exception ex)
            {
                _session.LogSave($"Sequence executed with Error: {ex.Message}", appName, "ERROR");
                HelperConsoleTheme.Error($"Sequence error: {ex.Message}");
            }
        }
    }
}
