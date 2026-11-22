using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KAONOMI.Core;
using KAONOMI.Core.Models;
using Microsoft.Playwright;

namespace KAONOMI.Agent.WebScraper.Collect.Scraping
{
    /// <summary>
    /// Collects the pages of the collection plans with a headless browser (Playwright: the installed Chrome or Edge,
    /// else its own browser). For each page, a <see cref="Page"/>: its content cut by titles and paragraphs, the
    /// entities the NER of the server finds in it, every URL it links to, and, in
    /// <c>&lt;root&gt;/&lt;collection plan&gt;/&lt;website&gt;/&lt;yyyyMMdd&gt;</c>, the picture of the whole page as
    /// the browser draws it and its pictures. One browser is shared by the run (started at the first page).
    /// </summary>
    public sealed class PageCollector : IAsyncDisposable
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static PageCollector? _shared;

        private readonly CollectConfig _config;
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly HttpClient _http;
        private readonly RobotsTxt _robots;
        private readonly NerClient _ner;

        private PageCollector(CollectConfig config, IPlaywright playwright, IBrowser browser)
        {
            _config = config;
            _playwright = playwright;
            _browser = browser;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(config.userAgent);
            _robots = new RobotsTxt(_http);
            _ner = new NerClient(config.ServerUrl);
        }

        /// <summary>The browser that was started: "chrome", "msedge" or "playwright".</summary>
        public string Browser { get; private set; } = string.Empty;

        /// <summary>The collector of the process, its browser started at the first call.</summary>
        public static async Task<PageCollector> GetAsync(CollectConfig config)
        {
            await Gate.WaitAsync();
            try
            {
                if (_shared is null)
                {
                    var playwright = await Playwright.CreateAsync();
                    // The installed browser asked, or Chrome then Edge; else the browser of Playwright
                    // (installed with "playwright install chromium").
                    var channels = string.IsNullOrWhiteSpace(config.browserChannel)
                        ? new string?[] { "chrome", "msedge", null }
                        : new string?[] { config.browserChannel.Trim(), null };
                    IBrowser? browser = null;
                    var started = string.Empty;
                    PlaywrightException? last = null;
                    foreach (var channel in channels)
                    {
                        try
                        {
                            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true, Channel = channel });
                            started = channel ?? "playwright";
                            break;
                        }
                        catch (PlaywrightException ex)
                        {
                            last = ex;
                        }
                    }
                    if (browser is null)
                    {
                        playwright.Dispose();
                        throw new InvalidOperationException("No browser to collect the pages: install Google Chrome or Microsoft Edge, or the browser of Playwright "
                                                            + $"(playwright install chromium). {last?.Message.Split('\n')[0]}");
                    }
                    _shared = new PageCollector(config, playwright, browser) { Browser = started };
                }
                return _shared;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Collects the URL of <paramref name="job"/> and, in explore or random mode, links of the same site. The pages
        /// reached by a link that were collected less than <see cref="CollectConfig.revisitHours"/> ago are left out;
        /// so is the URL of the job itself, unless it is a main page or the job says always. A page already collected
        /// in this run (its id in <paramref name="collected"/>: the same URL twice in a plan, or reached from two
        /// URLs) is not collected twice. Each page is given to <paramref name="onPage"/> as soon as it is collected
        /// (it is saved then: a run stopped before its end keeps the pages collected so far).
        /// </summary>
        public async Task<List<Page>> CollectAsync(CollectJob job, SessionManager session, HashSet<string> collected, Action<Page, List<DetectedEntity>> onPage,
            Action<string> log, CancellationToken cancellationToken)
        {
            var results = new List<Page>();
            if (!Uri.TryCreate(job.url, UriKind.Absolute, out var start) || (start.Scheme != Uri.UriSchemeHttp && start.Scheme != Uri.UriSchemeHttps))
            {
                log($"Not a web URL: {job.url}");
                return results;
            }

            var mode = (job.mode ?? "fixed").Trim().ToLowerInvariant();
            var limit = mode switch
            {
                "explore" => Math.Max(1, _config.maxPagesPerUrl),
                "random" => 1 + Math.Max(0, _config.randomPages),
                _ => 1
            };
            var queue = new List<(Uri Url, int Depth)> { (start, 0) };
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0 && results.Count < limit && !cancellationToken.IsCancellationRequested)
            {
                var (url, depth) = queue[0];
                queue.RemoveAt(0);
                if (!visited.Add(PageNames.Normalize(url)))
                    continue;

                // The main page is the URL of the plan itself, not the pages it links to.
                var isMainPage = job.isMainPage && depth == 0;
                var now = DateTime.Now;
                var day = PageNames.Day(now);
                var id = PageNames.Id(job.collectionPlanId, url, isMainPage, day);

                if (collected.Contains(id))
                {
                    log($"Already collected in this run: {url}");
                    continue;
                }

                // A main page changes with the news: always collected. The others, not twice in a short time.
                var mustCollect = isMainPage || (depth == 0 && job.always);
                Page? known = mustCollect ? null : SafeGet(session, id);
                if (known is not null && now - known.scrapedAt < TimeSpan.FromHours(_config.revisitHours))
                {
                    log($"Collected less than {_config.revisitHours:0.#} h ago: {url}");
                    // Its links are still followed (explore, random): they were kept with it.
                    Follow(known.urls);
                    continue;
                }
                if (_config.respectRobotsTxt && !await _robots.IsAllowedAsync(url, cancellationToken))
                {
                    log($"Disallowed by robots.txt: {url}");
                    continue;
                }

                if (results.Count > 0)
                    await Task.Delay(Math.Max(0, _config.delayBetweenPagesMs), cancellationToken);
                var detected = new List<DetectedEntity>();
                var page = await CollectPageAsync(job, url, depth, isMainPage, id, now, detected, log, cancellationToken);
                if (page is null)
                    continue;
                results.Add(page);
                collected.Add(id);
                onPage(page, detected);
                Follow(page.urls);

                // The links to follow: same site, not seen, those holding a keyword first (explore) or at random.
                void Follow(List<string> links)
                {
                    if (mode == "fixed" || (mode == "explore" && depth >= _config.maxDepth) || (mode == "random" && depth >= 1))
                        return;
                    var next = links
                        .Select(l => Uri.TryCreate(l, UriKind.Absolute, out var u) ? u : null)
                        .OfType<Uri>()
                        .Where(u => (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && SameSite(u, start) && !visited.Contains(PageNames.Normalize(u)) && !LooksLikeFile(u))
                        .DistinctBy(PageNames.Normalize)
                        .ToList();
                    if (mode == "random")
                        next = next.OrderBy(_ => Random.Shared.Next()).Take(_config.randomPages).ToList();
                    else
                        next = next.OrderByDescending(u => KeywordsIn(u.AbsoluteUri, job.keywords).Count).ToList();
                    queue.AddRange(next.Select(u => (u, depth + 1)));
                }
            }
            return results;
        }

        private static Page? SafeGet(SessionManager session, string id)
        {
            try
            {
                return session.PageGet(id);
            }
            catch
            {
                return null;
            }
        }

        private async Task<Page?> CollectPageAsync(CollectJob job, Uri url, int depth, bool isMainPage, string id, DateTime now, List<DetectedEntity> detected, Action<string> log,
            CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var day = PageNames.Day(now);
            var page = new Page
            {
                id = id,
                day = day,
                isMainPage = isMainPage,
                url = url.AbsoluteUri,
                startUrl = job.url,
                depth = depth,
                mode = job.mode,
                keywords = job.keywords ?? new List<string>(),
                collectionPlanId = job.collectionPlanId,
                collectionPlanName = job.collectionPlanName,
                websiteName = job.websiteName,
                scrapedAt = now,
                date = now,
                appName = _config.appName,
                machineName = Environment.MachineName,
                userName = Environment.UserName
            };

            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = _config.userAgent,
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
                Locale = "fr-FR",
                IgnoreHTTPSErrors = true
            });
            var browserPage = await context.NewPageAsync();
            var timeout = Math.Max(5, _config.pageTimeoutSeconds) * 1000f;

            try
            {
                var response = await browserPage.GotoAsync(url.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = timeout });
                page.httpStatus = response?.Status ?? 0;
                if (response is not null && !response.Ok)
                {
                    log(response.Status is 401 or 403 or 429
                        ? $"Refused by the site (HTTP {response.Status}{Protection(response.Headers)}): {url} is not collected"
                        : $"HTTP {response.Status}: {url}");
                    if (response.Status >= 400)
                        return null;
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                log($"Page not loaded ({ex.Message.Split('\n')[0]}): {url}");
                return null;
            }

            try
            {
                await browserPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 });
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                // Pages that keep loading (ads, analytics): what is there is enough.
            }

            // Scrolls down the page so that the pictures loaded lazily are there, then back to the top.
            try
            {
                await browserPage.EvaluateAsync(@"async () => {
                    const step = window.innerHeight;
                    for (let y = 0, i = 0; y < document.body.scrollHeight && i < 40; y += step, i++) {
                        window.scrollTo(0, y);
                        await new Promise(r => setTimeout(r, 150));
                    }
                    window.scrollTo(0, 0);
                }");
                await browserPage.WaitForTimeoutAsync(500);
            }
            catch (PlaywrightException)
            {
            }

            page.finalUrl = browserPage.Url;
            page.pageTitle = (await SafeAsync(() => browserPage.TitleAsync(), string.Empty)).Trim();
            page.name = PageNames.Name(page.pageTitle, url, isMainPage, day);
            page.title = page.name;

            // Its files: <root>/<collection plan>/<website>/<yyyyMMdd>; those of an earlier collection of this page there are replaced.
            page.folder = PageNames.Folder(_config.ResolvedOutputFolder, job.collectionPlanName, job.websiteName, url, day);
            var prefix = PageNames.FilePrefix(page.pageTitle.Length > 0 ? page.pageTitle : url.Host, id);
            try
            {
                Directory.CreateDirectory(page.folder);
                PageNames.RemoveFiles(page.folder, id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                page.warnings.Add($"Folder {page.folder}: {ex.Message}");
            }

            // The rendering: the whole page as the browser draws it.
            var rendering = Path.Combine(page.folder, prefix + ".png");
            try
            {
                await browserPage.ScreenshotAsync(new PageScreenshotOptions { Path = rendering, FullPage = true, Timeout = timeout });
                page.renderingPath = rendering;
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or IOException or UnauthorizedAccessException)
            {
                page.warnings.Add("Rendering failed: " + ex.Message.Split('\n')[0]);
            }

            var html = await SafeAsync(() => browserPage.ContentAsync(), string.Empty);
            var facts = await ReadFactsAsync(browserPage);
            page.pageLanguage = facts.Language;
            page.urls = facts.Links.Distinct(StringComparer.Ordinal).ToList();

            // The content, by titles and paragraphs.
            page.sections = PageStructure.Split(html, _config.minParagraphLength);
            page.paragraphCount = page.sections.Sum(s => s.paragraphs.Count);
            page.text = PageStructure.TextOf(page.sections);
            page.contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.text))).ToLowerInvariant();
            page.keywordsFound = KeywordsIn(page.pageTitle + "\n" + page.text, page.keywords);

            // Its entities: name and type.
            page.entities = await _ner.EntitiesOfAsync(PageStructure.BlocksOf(page.sections), page.warnings, log, url.AbsoluteUri, cancellationToken, page, detected);

            // Its pictures, beside the rendering.
            page.images = await DownloadImagesAsync(context, facts.Images, page.finalUrl, page.folder, prefix, cancellationToken);

            page.durationMs = (int)watch.ElapsedMilliseconds;
            log($"{page.name}: {page.sections.Count} section(s), {page.paragraphCount} paragraph(s), {page.entities.Count} entit(ies), {page.urls.Count} URL(s), "
                + $"{page.images.Count(i => i.path.Length > 0)} picture(s){(page.renderingPath.Length > 0 ? ", rendering" : ", no rendering")} · {page.finalUrl}");
            return page;
        }

        /// <summary>
        /// What refuses the automated visits, when the answer of the site tells it: a site that protects itself against
        /// robots answers 403 to the agent. The agent does not go round such a protection: the page is left out.
        /// </summary>
        private static string Protection(IReadOnlyDictionary<string, string> headers)
        {
            bool Has(string name) => headers.Keys.Any(k => k.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            var name = Has("x-datadome") ? "DataDome"
                : Has("cf-mitigated") ? "Cloudflare"
                : Has("x-akamai") || Has("akamai-") ? "Akamai"
                : headers.TryGetValue("server", out var server) && server.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) ? "Cloudflare"
                : string.Empty;
            return name.Length > 0 ? $", protection against robots: {name}" : ", it does not accept automated visits";
        }

        private sealed record PageFacts(string Language, List<string> Links, List<(string Url, string Alt, string Context)> Images);

        /// <summary>
        /// The language, the links and the pictures of the page, as the browser sees them: the pictures shown (lazy ones
        /// and srcset resolved), those of the &lt;picture&gt; sources, of the backgrounds set in the style of an element,
        /// and the one the page gives for its sharing (og:image).
        /// </summary>
        private static async Task<PageFacts> ReadFactsAsync(IPage page)
        {
            try
            {
                var json = await page.EvaluateAsync<string>(@"() => {
                    const abs = u => { try { return new URL(u, location.href).href; } catch (e) { return ''; } };
                    const first = s => (s || '').split(',')[0].trim().split(/\s+/)[0];
                    const around = e => { const c = e.closest('figure, article, li, a'); return c ? (c.innerText || '').replace(/\s+/g, ' ').trim().slice(0, 300) : ''; };
                    const images = [];
                    for (const i of Array.from(document.images))
                        images.push({ src: i.currentSrc || i.src || abs(i.dataset.src || i.dataset.lazySrc || first(i.dataset.srcset) || ''), alt: i.alt || '', context: around(i) });
                    for (const s of Array.from(document.querySelectorAll('picture source[srcset]')))
                        images.push({ src: abs(first(s.srcset)), alt: '', context: around(s) });
                    for (const e of Array.from(document.querySelectorAll('[style*=""background""]'))) {
                        const m = /url\(\s*['""]?([^'"")]+)['""]?\s*\)/.exec(e.getAttribute('style') || '');
                        if (m) images.push({ src: abs(m[1]), alt: '', context: around(e) });
                    }
                    for (const m of Array.from(document.querySelectorAll('meta[property=""og:image""], meta[name=""twitter:image""]')))
                        images.push({ src: abs(m.content), alt: 'og:image', context: '' });
                    return JSON.stringify({
                        lang: document.documentElement.lang || '',
                        links: Array.from(document.querySelectorAll('a[href]')).map(a => a.href),
                        images: images
                    });
                }");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var links = root.GetProperty("links").EnumerateArray().Select(e => e.GetString() ?? string.Empty)
                    .Where(s => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)).ToList();
                var images = root.GetProperty("images").EnumerateArray()
                    .Select(e => (Url: e.GetProperty("src").GetString() ?? string.Empty, Alt: e.GetProperty("alt").GetString() ?? string.Empty,
                        Context: e.TryGetProperty("context", out var around) ? around.GetString() ?? string.Empty : string.Empty))
                    .Where(i => i.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return new PageFacts(root.GetProperty("lang").GetString() ?? string.Empty, links, images);
            }
            catch (Exception ex) when (ex is PlaywrightException or JsonException or KeyNotFoundException)
            {
                return new PageFacts(string.Empty, new(), new());
            }
        }

        /// <summary>
        /// Downloads the pictures (with the cookies of the page) into <paramref name="folder"/>, named
        /// <c>&lt;prefix&gt;-001-&lt;name&gt;.&lt;ext&gt;</c>; what is not a picture, or is smaller than
        /// <see cref="CollectConfig.minImageBytes"/>, is left out.
        /// </summary>
        private async Task<List<PageImage>> DownloadImagesAsync(IBrowserContext context, List<(string Url, string Alt, string Context)> found, string referer,
            string folder, string prefix, CancellationToken cancellationToken)
        {
            var images = new List<PageImage>();
            var kept = 0;
            foreach (var (url, alt, around) in found.DistinctBy(i => i.Url))
            {
                if (kept >= Math.Max(0, _config.maxImagesPerPage) || cancellationToken.IsCancellationRequested)
                    break;
                var image = new PageImage { url = url, alt = alt, context = around };
                try
                {
                    var response = await context.APIRequest.GetAsync(url, new APIRequestContextOptions
                    {
                        Headers = new Dictionary<string, string> { ["Referer"] = referer },
                        Timeout = 20000
                    });
                    if (!response.Ok)
                    {
                        image.error = $"HTTP {response.Status}";
                        images.Add(image);
                        continue;
                    }
                    response.Headers.TryGetValue("content-type", out var contentType);
                    image.contentType = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
                    if (!image.contentType.StartsWith("image/"))
                        continue;
                    var bytes = await response.BodyAsync();
                    image.bytes = bytes.LongLength;
                    if (bytes.Length < _config.minImageBytes)
                        continue;

                    var path = Path.Combine(folder, $"{prefix}-{kept + 1:D3}-{FileName(url)}{Extension(image.contentType, url)}");
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                    image.path = path;
                    kept++;
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException or IOException or UnauthorizedAccessException)
                {
                    image.error = ex.Message.Split('\n')[0];
                }
                images.Add(image);
            }
            return images;
        }

        private static string FileName(string url)
        {
            var name = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath)) : "image";
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => invalid.Contains(c) || c == ' ' || c == ':' ? '_' : c).ToArray()).Trim('_', '.');
            return name.Length == 0 ? "image" : name.Length > 60 ? name[..60] : name;
        }

        private static string Extension(string contentType, string url) => contentType switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/avif" => ".avif",
            "image/bmp" => ".bmp",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            _ => Uri.TryCreate(url, UriKind.Absolute, out var uri) && Path.GetExtension(uri.AbsolutePath) is { Length: > 1 and <= 5 } ext ? ext : ".img"
        };

        /// <summary>The keywords found in <paramref name="text"/> (case ignored).</summary>
        public static List<string> KeywordsIn(string text, IEnumerable<string>? keywords) =>
            (keywords ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k) && text.Contains(k.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static bool SameSite(Uri url, Uri start) =>
            string.Equals(url.Host.Replace("www.", ""), start.Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase);

        private static readonly string[] FileExtensions = { ".pdf", ".zip", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mp3", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".exe", ".dmg", ".svg", ".xml", ".rss" };

        private static bool LooksLikeFile(Uri url) =>
            FileExtensions.Contains(Path.GetExtension(url.AbsolutePath), StringComparer.OrdinalIgnoreCase);

        private static async Task<T> SafeAsync<T>(Func<Task<T>> action, T fallback)
        {
            try
            {
                return await action();
            }
            catch (PlaywrightException)
            {
                return fallback;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _browser.DisposeAsync();
            _playwright.Dispose();
            _http.Dispose();
            _ner.Dispose();
        }

        /// <summary>Closes the browser of the process, if one was started (the end of the run).</summary>
        public static async Task CloseAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (_shared is not null)
                    await _shared.DisposeAsync();
                _shared = null;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
