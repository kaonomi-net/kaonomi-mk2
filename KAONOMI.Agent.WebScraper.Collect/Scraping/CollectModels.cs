using System.Text.Json;

namespace KAONOMI.Agent.WebScraper.Collect.Scraping
{
    /// <summary>
    /// What the sequence receives: URLs given on the command line, or a collection plan (its id), or nothing (the active
    /// collection plans).
    /// </summary>
    public sealed class CollectRequest
    {
        public List<string> urls { get; set; } = new();

        /// <summary>The id of one collection plan to collect now, active or not (--plan: RUN of the chat).</summary>
        public string planId { get; set; } = string.Empty;

        /// <summary>fixed, explore or random, for the URLs of the command line.</summary>
        public string mode { get; set; } = "fixed";

        /// <summary>The URLs of the command line are main pages (--main).</summary>
        public bool isMainPage { get; set; }

        public List<string> keywords { get; set; } = new();

        /// <summary>
        /// A file holding the content of one page as the user copied it in a browser (HTML, or plain text), to keep as
        /// the page of the first URL of <see cref="urls"/> (--import: PASTE PAGE of the chat, for a site that refuses
        /// the agent). Nothing is asked to the site.
        /// </summary>
        public string importFile { get; set; } = string.Empty;
    }

    /// <summary>A URL of a collection plan to collect (the first agent gives them to the second).</summary>
    public sealed class CollectJob
    {
        public string url { get; set; } = string.Empty;

        /// <summary>fixed: the page only; explore: the page and the links of the same site; random: the page and some of its links.</summary>
        public string mode { get; set; } = "fixed";

        /// <summary>The URL is a main page: named by its title and the day, one document per day.</summary>
        public bool isMainPage { get; set; }

        public List<string> keywords { get; set; } = new();

        public string collectionPlanId { get; set; } = string.Empty;

        public string collectionPlanName { get; set; } = string.Empty;

        public string websiteName { get; set; } = string.Empty;

        /// <summary>Collected even when it was collected less than <see cref="CollectConfig.revisitHours"/> ago.</summary>
        public bool always { get; set; }

        /// <summary>Not collected from the site: the page is what this file holds (copied by the user in a browser).</summary>
        public string importFile { get; set; } = string.Empty;
    }

    /// <summary>What a run collected and saved (the second agent gives it to the third, which reports it).</summary>
    public sealed class CollectSummary
    {
        /// <summary>The pages saved in the search engine (index <c>page</c>).</summary>
        public int pages { get; set; }

        /// <summary>The pages collected that the search engine did not save.</summary>
        public int failed { get; set; }

        public int entities { get; set; }

        /// <summary>The entities found by the NER saved in the index <c>detectedentity</c> (each place they are found).</summary>
        public int detected { get; set; }

        public int urls { get; set; }

        /// <summary>The pictures downloaded (the renderings are not counted).</summary>
        public int pictures { get; set; }

        public int renderings { get; set; }

        /// <summary>The names of the pages saved.</summary>
        public List<string> names { get; set; } = new();
    }

    /// <summary>The JSON passed from one agent of the sequence to the next.</summary>
    public static class SequenceJson
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

        public static T Read<T>(string? json) where T : new()
        {
            if (string.IsNullOrWhiteSpace(json))
                return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
            }
            catch (JsonException)
            {
                return new T();
            }
        }
    }
}
