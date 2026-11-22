using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HtmlAgilityPack;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Collect.Scraping
{
    /// <summary>
    /// Keeps a page the user copied by hand in a browser (PASTE PAGE of the chat): for a site that refuses the agent,
    /// the user opens the page as any reader, copies it, and the agent keeps what was copied as the <see cref="Page"/>
    /// of that URL, named and identified as a page it would have collected (a main page: its title and the day, one
    /// document per day). Nothing is asked to the site: the content is cut by titles and paragraphs, its entities are
    /// found by the NER of the server, its links are kept as URLs and its pictures listed by their address (not
    /// downloaded); there is no rendering.
    /// </summary>
    public static class PageImporter
    {
        /// <summary>Less than this many characters of text: not a page (an empty clipboard, a few words copied).</summary>
        public const int MinTextChars = 200;

        public static async Task<Page?> ImportAsync(CollectConfig config, CollectJob job, string content, NerClient ner, List<DetectedEntity> detected, Action<string> log,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            if (!Uri.TryCreate(job.url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                log($"Not a web URL: {job.url}");
                return null;
            }
            var html = LooksLikeHtml(content) ? content : TextAsHtml(content);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            var now = DateTime.Now;
            var day = PageNames.Day(now);
            var page = new Page
            {
                id = PageNames.Id(job.collectionPlanId, url, job.isMainPage, day),
                day = day,
                isMainPage = job.isMainPage,
                isImported = true,
                url = url.AbsoluteUri,
                finalUrl = url.AbsoluteUri,
                startUrl = job.url,
                mode = job.mode,
                keywords = job.keywords ?? new List<string>(),
                collectionPlanId = job.collectionPlanId,
                collectionPlanName = job.collectionPlanName,
                websiteName = job.websiteName,
                scrapedAt = now,
                date = now,
                appName = config.appName,
                machineName = Environment.MachineName,
                userName = Environment.UserName
            };

            page.sections = PageStructure.Split(html, config.minParagraphLength);
            page.paragraphCount = page.sections.Sum(s => s.paragraphs.Count);
            page.text = PageStructure.TextOf(page.sections);
            if (page.text.Length < MinTextChars)
            {
                log($"What was copied holds no page ({page.text.Length} characters of text): in the browser, select the whole page and copy it, then PASTE PAGE again");
                return null;
            }
            page.contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.text))).ToLowerInvariant();

            // What is copied has no <title>. A main page takes the name of its website (its first title is the headline
            // of the day); any other page its first main title.
            page.pageTitle = Title(document, page.sections, job.websiteName, job.isMainPage);
            page.name = PageNames.Name(page.pageTitle, url, job.isMainPage, day);
            page.title = page.name;
            page.keywordsFound = PageCollector.KeywordsIn(page.pageTitle + "\n" + page.text, page.keywords);

            page.urls = Attributes(document, "//a[@href]", "href")
                .Select(href => Absolute(url, href))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
            // Its pictures are listed, not downloaded: that would be the agent asking the site again.
            page.images = (document.DocumentNode.SelectNodes("//img") ?? Enumerable.Empty<HtmlNode>())
                .Select(img => (Address: Absolute(url, FirstOf(img, "src", "data-src", "data-lazy-src")), Alt: WebUtility.HtmlDecode(img.GetAttributeValue("alt", string.Empty)), Around: Around(img)))
                .Where(i => i.Address is not null)
                .DistinctBy(i => i.Address)
                .Select(i => new PageImage { url = i.Address!, alt = i.Alt.Trim(), context = i.Around, error = "not downloaded: the page was imported by hand" })
                .ToList();
            page.warnings.Add("Imported by hand from what the user copied in a browser: no rendering, its pictures are not downloaded");

            page.entities = await ner.EntitiesOfAsync(PageStructure.BlocksOf(page.sections), page.warnings, log, url.AbsoluteUri, cancellationToken, page, detected);
            page.durationMs = (int)watch.ElapsedMilliseconds;
            log($"{page.name}: imported by hand, {page.sections.Count} section(s), {page.paragraphCount} paragraph(s), {page.entities.Count} entit(ies), {page.urls.Count} URL(s) · {page.url}");
            return page;
        }

        private static bool LooksLikeHtml(string content) =>
            System.Text.RegularExpressions.Regex.IsMatch(content ?? string.Empty, @"<\s*(p|div|h[1-6]|a|span|article|section|body|html|li|br)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>A text copied without its HTML: a paragraph per line (the titles cannot be told from the paragraphs).</summary>
        private static string TextAsHtml(string text) =>
            string.Join("\n", (text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => "<p>" + WebUtility.HtmlEncode(l) + "</p>"));

        private static string Title(HtmlDocument document, List<PageSection> sections, string websiteName, bool isMainPage)
        {
            var title = WebUtility.HtmlDecode(document.DocumentNode.SelectSingleNode("//title")?.InnerText ?? string.Empty).Trim();
            if (title.Length > 0)
                return title;
            var site = (websiteName ?? string.Empty).Trim();
            var main = sections.FirstOrDefault(s => s.level == 1)?.title ?? string.Empty;
            return isMainPage ? (site.Length > 0 ? site : main) : (main.Length > 0 ? main : site);
        }

        /// <summary>The text around a picture: of its figure, article, list item or link, 300 characters at most.</summary>
        private static string Around(HtmlNode image)
        {
            var holder = image.Ancestors().FirstOrDefault(a => a.Name is "figure" or "article" or "li" or "a");
            if (holder is null)
                return string.Empty;
            var text = System.Text.RegularExpressions.Regex.Replace(WebUtility.HtmlDecode(holder.InnerText) ?? string.Empty, @"\s+", " ").Trim();
            return text.Length > 300 ? text[..300] : text;
        }

        private static IEnumerable<string> Attributes(HtmlDocument document, string path, string name) =>
            (document.DocumentNode.SelectNodes(path) ?? Enumerable.Empty<HtmlNode>()).Select(n => n.GetAttributeValue(name, string.Empty));

        private static string FirstOf(HtmlNode node, params string[] names) =>
            names.Select(n => node.GetAttributeValue(n, string.Empty)).FirstOrDefault(v => v.Length > 0 && !v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        /// <summary>The address as an absolute http(s) URL (a relative one is from the page), or null.</summary>
        private static string? Absolute(Uri page, string address)
        {
            address = WebUtility.HtmlDecode(address ?? string.Empty).Trim();
            return address.Length > 0 && Uri.TryCreate(page, address, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                ? absolute.AbsoluteUri
                : null;
        }
    }
}
