using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// The analysis of the pictures of a page by the image model: what each one shows, its kind, the text written in
    /// it, and which entities of the page it is about. Nobody is recognised by their face: an entity is tied to a
    /// picture by its caption (the alt of the picture) or by what is written in the picture. Only the pictures
    /// downloaded with the page are analysed (JPEG, PNG, WebP), the largest files first, not the small ones (icons).
    /// </summary>
    public static class ImageAnalyzer
    {
        private static readonly string[] Readable = { ".jpg", ".jpeg", ".png", ".webp" };

        private static string System(string language) =>
            "You describe a picture of a press web page. Say what it shows in two or three sentences, its kind "
            + "(photo, drawing, chart, logo, map, screenshot, other) and copy the text written in it, if any. "
            + "You are given its caption and the entities the page names: list the ones the picture is about, only when the caption "
            + "or a text written in the picture names them. Never tell who a person is from their face. "
            + $"Write the description in {language}. Answer with a JSON object only: "
            + "{\"description\":\"...\",\"kind\":\"photo\",\"text\":\"...\",\"entities\":[\"exact names of the list\"]}";

        public static async Task<List<AnalysedImage>> AnalyseAsync(Llm llm, string model, AnalyzeConfig config, Page page, List<AnalysedEntity> entities,
            List<string> warnings, Action<string> log, CancellationToken cancellationToken)
        {
            var results = new List<AnalysedImage>();
            var downloaded = (page.images ?? new List<PageImage>()).Where(i => !string.IsNullOrWhiteSpace(i.path)).ToList();
            var candidates = downloaded
                .Where(i => Readable.Contains(Path.GetExtension(i.path).ToLowerInvariant()) && File.Exists(i.path))
                .Select(i => (Image: i, Bytes: new FileInfo(i.path).Length))
                .Where(i => i.Bytes >= config.minImageBytes && i.Bytes <= config.maxImageBytes)
                .OrderByDescending(i => i.Bytes)
                .Take(Math.Max(0, config.maxImagesPerPage))
                .Select(i => i.Image)
                .ToList();
            if (downloaded.Count == 0)
            {
                if ((page.images?.Count ?? 0) > 0)
                    warnings.Add("Pictures: none was downloaded with the page (a page imported by hand has its pictures listed only)");
                return results;
            }
            if (candidates.Count < downloaded.Count)
                warnings.Add($"Pictures: {candidates.Count} of {downloaded.Count} analysed (JPEG, PNG or WebP of {config.minImageBytes / 1000} kB to {config.maxImageBytes / 1_000_000} MB, the {config.maxImagesPerPage} largest)");

            var language = EntityAnalyzer.LanguageName(config.language);
            var names = string.Join(", ", entities.Select(e => e.name));
            foreach (var image in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    warnings.Add($"Pictures: the time of the stage ended, {candidates.Count - results.Count} not analysed");
                    break;
                }
                var result = new AnalysedImage { url = image.url, path = image.path, alt = image.alt ?? string.Empty };
                var user = $"Caption of the picture: {(result.alt.Length > 0 ? result.alt : "(none)")}\nEntities of the page: {(names.Length > 0 ? names : "(none)")}";
                using var answer = await llm.AskJsonAsync(model, System(language), user, cancellationToken, image.path);
                if (answer is null)
                    result.error = llm.LastError;
                else
                {
                    result.description = Json.Text(answer.RootElement, "description");
                    result.kind = Json.Text(answer.RootElement, "kind").ToLowerInvariant();
                    result.text = Json.Text(answer.RootElement, "text");
                    // Only entities of the page, and only when the caption or the text of the picture names them.
                    var said = TextTools.Fold(result.alt + " " + result.text);
                    var told = Json.Texts(answer.RootElement, "entities").Select(TextTools.Fold).ToHashSet();
                    result.entities = entities
                        .Where(e => TextTools.Names(said, TextTools.FoldedNames(e)) && (told.Count == 0 || TextTools.FoldedNames(e).Any(told.Contains) || TextTools.Names(TextTools.Fold(result.alt), TextTools.FoldedNames(e))))
                        .Select(e => e.name)
                        .ToList();
                }
                results.Add(result);
            }
            log($"Pictures: {results.Count(r => r.error.Length == 0)} analysed{(results.Any(r => r.error.Length > 0) ? $", {results.Count(r => r.error.Length > 0)} failed" : string.Empty)}");
            return results;
        }
    }
}
