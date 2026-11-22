using System.Net.Http.Json;
using KAONOMI.Core.Helpers;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Collect.Scraping
{
    /// <summary>
    /// The NER of the Kaonomi server (<c>POST /ner</c>): the model, the rules and the names of the ontology. The
    /// server is reached in plain HTTP, on localhost.
    /// </summary>
    public sealed class NerClient : IDisposable
    {
        /// <summary>Most characters sent at once: the blocks of a page are grouped up to this length.</summary>
        public const int ChunkChars = 3000;

        private readonly HttpClient _http;
        private readonly string _serverUrl;

        public NerClient(string serverUrl)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
        }

        private sealed class NerAnswer
        {
            public List<NerEntity> entities { get; set; } = new();
            public List<string> warnings { get; set; } = new();
        }

        /// <summary>The entities of <paramref name="text"/>, with the warnings of the server.</summary>
        public async Task<(List<NerEntity> Entities, List<string> Warnings)> ExtractAsync(string text, CancellationToken cancellationToken)
        {
            using var response = await _http.PostAsJsonAsync(_serverUrl + "/ner", new { text, types = (string[]?)null }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<NerAnswer>(cancellationToken: cancellationToken) ?? new NerAnswer();
            return (answer.entities ?? new(), answer.warnings ?? new());
        }

        /// <summary>
        /// The entities of the blocks of text of a page (its titles and paragraphs), each once: name and type. Every
        /// entity found, where it was found, is added to <paramref name="detected"/> (for the index <c>detectedentity</c>).
        /// When the server does not answer, the entities found so far are kept and a warning is added to
        /// <paramref name="warnings"/>.
        /// </summary>
        public async Task<List<PageEntity>> EntitiesOfAsync(IEnumerable<string> blocks, List<string> warnings, Action<string> log, string what,
            CancellationToken cancellationToken, Page? page = null, List<DetectedEntity>? detected = null)
        {
            var found = new List<NerEntity>();
            var part = 0;
            foreach (var chunk in Chunks(blocks))
            {
                try
                {
                    var (entities, told) = await ExtractAsync(chunk, cancellationToken);
                    found.AddRange(entities);
                    if (page is not null && detected is not null)
                        detected.AddRange(entities.Where(e => !string.IsNullOrWhiteSpace(e.text))
                            .Select(e => DetectedEntity.FromPage(e, page.id, string.IsNullOrWhiteSpace(page.finalUrl) ? page.url : page.finalUrl, page.collectionPlanId, part, chunk)));
                    part++;
                    foreach (var warning in told.Where(w => !warnings.Contains("NER: " + w)))
                        warnings.Add("NER: " + warning);
                }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    warnings.Add($"NER unavailable at {_serverUrl}: {ex.Message}");
                    log($"NER unavailable at {_serverUrl} ({ex.Message}): {what} is saved without all its entities");
                    break;
                }
            }
            return Group(found);
        }

        /// <summary>
        /// The blocks of text grouped into texts of at most <see cref="ChunkChars"/> characters (a block alone when it
        /// is longer), one block per line: fewer calls than one per block.
        /// </summary>
        public static List<string> Chunks(IEnumerable<string> blocks)
        {
            var chunks = new List<string>();
            var current = new System.Text.StringBuilder();
            foreach (var block in blocks.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                if (current.Length > 0 && current.Length + 1 + block.Length > ChunkChars)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0)
                    current.Append('\n');
                current.Append(block);
            }
            if (current.Length > 0)
                chunks.Add(current.ToString());
            return chunks;
        }

        /// <summary>
        /// The entities of a page, each once: its name (the title of the ontology when it is known there, else the text
        /// as written) and its type, with how many times it was found; the most frequent first.
        /// </summary>
        public static List<PageEntity> Group(IEnumerable<NerEntity> found) =>
            found.Select(e => (Name: (string.IsNullOrWhiteSpace(e.ontologyName) ? e.text : e.ontologyName)!.Trim(), Type: (e.type ?? string.Empty).Trim()))
                .Where(e => e.Name.Length > 0)
                .GroupBy(e => (Name: e.Name.ToLowerInvariant(), e.Type))
                .Select(g => new PageEntity
                {
                    // The spelling met the most often (« Macron », not « MACRON »).
                    name = g.GroupBy(e => e.Name).OrderByDescending(n => n.Count()).First().Key,
                    type = g.Key.Type,
                    count = g.Count()
                })
                .OrderByDescending(e => e.count).ThenBy(e => e.type, StringComparer.Ordinal).ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        public void Dispose() => _http.Dispose();
    }
}
