using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Collect
{
    /// <summary>
    /// config.json of the agent: the settings of every Kaonomi host (<see cref="Config"/>) and how the pages are
    /// collected.
    /// </summary>
    public sealed class CollectConfig : Config
    {
        /// <summary>
        /// A page collected less than this many hours ago is not collected again: the pages reached from a URL of a
        /// plan (explore, random) and, when the agent runs the active plans, the URLs that are not main pages. A main
        /// page, and every URL of the plan asked by <c>--plan</c>, is always collected.
        /// </summary>
        public double revisitHours { get; set; } = 24;

        /// <summary>Most pages collected from one URL of a collection plan (explore mode).</summary>
        public int maxPagesPerUrl { get; set; } = 20;

        /// <summary>How many links away from the start URL the explore mode goes.</summary>
        public int maxDepth { get; set; } = 2;

        /// <summary>How many links of the start page the random mode collects.</summary>
        public int randomPages { get; set; } = 5;

        /// <summary>Most pictures downloaded for one page.</summary>
        public int maxImagesPerPage { get; set; } = 300;

        /// <summary>Smaller pictures (tracking pixels, spacers) are not kept.</summary>
        public int minImageBytes { get; set; } = 1024;

        /// <summary>Shorter blocks of text are not paragraphs (titles excepted).</summary>
        public int minParagraphLength { get; set; } = 40;

        public int pageTimeoutSeconds { get; set; } = 45;

        /// <summary>
        /// Minutes the collection of one run may take (a grain call fails after 10 minutes): the URLs not reached are
        /// left for the next run.
        /// </summary>
        public double runBudgetMinutes { get; set; } = 8;

        /// <summary>Pause between two pages of the same run, to be polite with the sites.</summary>
        public int delayBetweenPagesMs { get; set; } = 1500;

        /// <summary>Leaves out the pages that the robots.txt of the site disallows.</summary>
        public bool respectRobotsTxt { get; set; } = true;

        /// <summary>
        /// The installed browser Playwright drives (chrome, msedge); empty: Chrome, else Edge, else the browser of
        /// Playwright.
        /// </summary>
        public string browserChannel { get; set; } = string.Empty;

        public string userAgent { get; set; } = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 KaonomiWebScraper/1.0";

        /// <summary>Root of the renderings and pictures; empty: Pictures/kaonomi/scraper.</summary>
        public string outputFolder { get; set; } = string.Empty;

        /// <summary>The root folder of the renderings and pictures.</summary>
        public string ResolvedOutputFolder => string.IsNullOrWhiteSpace(outputFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "kaonomi", "scraper")
            : outputFolder;

        /// <summary>The URL of the Kaonomi server (for <c>POST /ner</c>).</summary>
        public string ServerUrl => string.IsNullOrWhiteSpace(dialogServerUrl) ? "http://localhost:49165" : dialogServerUrl.TrimEnd('/');
    }
}
