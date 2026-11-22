using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// <c>/entity:&lt;name&gt;</c>: what the pages collected by the web scraper (the index <c>page</c>) say of an entity,
/// day by day. The name is looked for whatever its accents, its case and a typing mistake (« melanchon » finds
/// « Mélenchon »): among the names of the entity recogniser and the words of the texts; each way it is written can be
/// left out. For each day collected, the titles and paragraphs that name it, their sources, how they are written
/// (sentences in the conditional, adverbs, counted without a model), and the persons, places and events found near it;
/// then the model of the advanced conversations reads the passages of each day (prompt <c>entity.day</c>): the emotions
/// they express about the entity, the persons, places and events linked to it, a summary, the tone of each source and
/// what the writing says; and at the end the whole period (prompt <c>entity.overview</c>). The analysis of a day is
/// kept (~/kaonomi/entity-analyses): opened again, a day whose passages did not change is not read again.
/// </summary>
public partial class EntityAnalysisWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>A way the entity is written in the pages: a name of the entity recogniser, or a spelling of the texts.</summary>
    public sealed class Alias
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Its type for the entity recogniser (PERSON, ORGANISATION…), empty for a spelling found in the texts.</summary>
        public string Type { get; init; } = string.Empty;

        /// <summary>"entity recogniser" or "text".</summary>
        public string Origin { get; init; } = string.Empty;

        /// <summary>The name folded (lower case, no accents): what is looked for in the texts.</summary>
        public string Folded { get; init; } = string.Empty;

        /// <summary>How many titles and paragraphs of the pages write it.</summary>
        public int Mentions { get; set; }

        /// <summary>Its passages are analysed (unticked: left out).</summary>
        public bool Chosen { get; set; }
    }

    /// <summary>A title or a paragraph of a collected page that names the entity (or that speaks of it, in the same section).</summary>
    public sealed record Passage(DateOnly Date, string Website, string PageId, string PageName, string Url, string RenderingPath, string SectionTitle,
        string Text, bool IsTitle, string Folded, string Language)
    {
        /// <summary>
        /// False for a paragraph that does not write the name but follows one that does, or belongs to a section whose
        /// title names the entity: it speaks of the entity without its name (« il aurait… »).
        /// </summary>
        public bool Names { get; init; } = true;

        public bool HasRendering => RenderingPath.Length > 0 && File.Exists(RenderingPath);
    }

    /// <summary>A person, a place or an event linked to the entity: its name, how it is linked (said by the model), how many passages of the day name it.</summary>
    public sealed record Link(string Name, string Role, int Mentions);

    /// <summary>A source of a day: how many pages and passages, how the passages are written, and what the model says of its tone.</summary>
    public sealed record Source(string Website, int Pages, int Passages, WritingCounts? Writing)
    {
        public string Tone { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public enum DayState { Waiting, Reading, Done, Failed }

    /// <summary>
    /// A day collected: the passages that name the entity, their sources, how they are written (counted), the persons,
    /// places and events found near the entity, and what the model says of the day (emotions, links, summary, sources,
    /// writing).
    /// </summary>
    public sealed class Day
    {
        public DateOnly Date { get; init; }
        public List<Passage> Passages { get; init; } = [];
        public int Pages { get; init; }
        public List<Source> Sources { get; init; } = [];

        /// <summary>How all the passages of the day are written.</summary>
        public WritingCounts? Writing { get; init; }

        /// <summary>The adverbs of the passages, the most used first.</summary>
        public List<(string Adverb, int Count)> Adverbs { get; init; } = [];

        /// <summary>The sentences of the passages in the conditional.</summary>
        public List<string> Conditionals { get; init; } = [];

        /// <summary>The persons, places and events the entity recogniser found in the passages (name and how many passages).</summary>
        public List<Link> FoundPersons { get; init; } = [];
        public List<Link> FoundPlaces { get; init; } = [];
        public List<Link> FoundEvents { get; init; } = [];

        /// <summary>What the day is: its passages (an analysis kept for the same hash is used again).</summary>
        public string Hash { get; init; } = string.Empty;

        // ── What the model says ──
        public DayState State { get; set; } = DayState.Waiting;
        public string Error { get; set; } = string.Empty;

        /// <summary>The analysis comes from an earlier opening (the passages of the day did not change).</summary>
        public bool FromCache { get; set; }

        /// <summary>How many passages the model read (the longest days are read in part, every source kept).</summary>
        public int PassagesRead { get; set; }

        /// <summary>The share of each emotion (<see cref="DisseminationWindowViewModel.EmotionNames"/>), 0 to 100.</summary>
        public double[] Emotions { get; set; } = new double[DisseminationWindowViewModel.EmotionNames.Length];
        public string Leading { get; set; } = string.Empty;
        public double Valence { get; set; }
        public double Intensity { get; set; }
        public string EmotionSummary { get; set; } = string.Empty;
        public List<Link> Persons { get; set; } = [];
        public List<Link> Places { get; set; } = [];
        public List<Link> Events { get; set; } = [];
        public string Summary { get; set; } = string.Empty;
        public string WritingNotes { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;

        public bool Analysed => State == DayState.Done;
    }

    /// <summary>The most characters of passages given to the model for a day (the longest days are read in part).</summary>
    private const int DayChars = 7000;
    private const int PassageChars = 900;
    private const int PagesRead = 2000;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(4);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly HashSet<string> PersonTypes = new(StringComparer.OrdinalIgnoreCase) { "PERSON", "PER", "PERS" };
    private static readonly HashSet<string> PlaceTypes = new(StringComparer.OrdinalIgnoreCase) { "LOC", "LOCATION", "CITY", "COUNTRY", "GPE", "REGION", "PLACE" };
    private static readonly HashSet<string> EventTypes = new(StringComparer.OrdinalIgnoreCase) { "EVENT" };

    private readonly string _searchEngineUrl;
    private readonly OllamaModelService? _llm;
    private readonly Func<string?> _model;
    private readonly string _language;
    private readonly Func<string, ImageAnalysisViewModel>? _createAnalysis;
    private CancellationTokenSource? _run;
    private List<PageData> _pages = [];
    private Cache _cache = new();

    /// <summary>Design-time constructor.</summary>
    public EntityAnalysisWindowViewModel() : this("entity", ChatSettings.Load().SearchEngineUrl, null, () => null, "English", null, load: false)
    {
    }

    public EntityAnalysisWindowViewModel(string query, string searchEngineUrl, OllamaModelService? llm, Func<string?> model, string language,
        Func<string, ImageAnalysisViewModel>? createAnalysis, bool load = true)
    {
        Query = query.Trim();
        _searchEngineUrl = searchEngineUrl.TrimEnd('/');
        _llm = llm;
        _model = model;
        _language = language;
        _createAnalysis = createAnalysis;
        if (load)
            _ = RefreshAsync();
    }

    /// <summary>The name typed after /entity:.</summary>
    public string Query { get; }

    /// <summary>The key of the window: one window per entity.</summary>
    public string Key => "/entity:" + DisseminationQuery.Fold(Query);

    /// <summary>The name shown: the way the entity is written the most often, else the name typed.</summary>
    public string DisplayName =>
        Aliases.Where(a => a.Chosen).OrderByDescending(a => a.Origin == "entity recogniser").ThenByDescending(a => a.Name.Split(' ').Length).ThenByDescending(a => a.Mentions)
            .Select(a => a.Name).FirstOrDefault() ?? Query;

    public string Title => $"Entity · {DisplayName}";

    /// <summary>The ways the entity is written, the most written first.</summary>
    public List<Alias> Aliases { get; private set; } = [];

    /// <summary>When nothing names the entity: the names of the pages closest to the name typed.</summary>
    public List<string> Suggestions { get; private set; } = [];

    /// <summary>The days collected that name the entity, the newest first.</summary>
    public List<Day> Days { get; private set; } = [];

    /// <summary>The model that analyses (the model of the advanced conversations).</summary>
    public string Model => _model() ?? string.Empty;

    public int PageCount => Days.Sum(d => d.Pages);
    public int PassageCount => Days.Sum(d => d.Passages.Count);
    public List<string> Websites => Days.SelectMany(d => d.Sources.Select(s => s.Website)).Distinct().OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList();

    // ── The whole period, said by the model ──
    public string Overview { get; private set; } = string.Empty;
    public string OverviewSources { get; private set; } = string.Empty;
    public string OverviewWriting { get; private set; } = string.Empty;
    public string OverviewError { get; private set; } = string.Empty;
    public bool OverviewReading { get; private set; }

    [ObservableProperty]
    private string _status = "Reading the pages collected…";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(AnalyseAgainCommand), nameof(CancelCommand))]
    private bool _isRunning;

    /// <summary>What the window shows changed as a whole (the pages read, a way of writing ticked).</summary>
    public event Action? Changed;

    /// <summary>A day was analysed (or is being read, or failed).</summary>
    public event Action<Day>? DayChanged;

    /// <summary>The summary of the whole period changed.</summary>
    public event Action? OverviewChanged;

    private bool CanStart() => !IsRunning;

    /// <summary>REFRESH: the pages read again; the days not analysed yet (new, or whose passages changed) are analysed.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task RefreshAsync() => RunAsync(load: true, force: false);

    /// <summary>ANALYSE AGAIN: every day read again by the model, whatever was kept.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task AnalyseAgainAsync() => RunAsync(load: false, force: true);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _run?.Cancel();

    /// <summary>Ticks or unticks a way of writing the entity: the days are computed again, those that changed analysed.</summary>
    public void ToggleAlias(Alias alias)
    {
        alias.Chosen = !alias.Chosen;
        if (IsRunning)
            _run?.Cancel();
        BuildDays();
        Changed?.Invoke();
        _ = RunAsync(load: false, force: false);
    }

    private async Task RunAsync(bool load, bool force)
    {
        if (IsRunning)
            return;
        _run?.Dispose();
        var cancel = _run = new CancellationTokenSource();
        IsRunning = true;
        IsError = false;
        try
        {
            if (load)
            {
                Status = "Reading the pages collected…";
                _pages = await LoadPagesAsync(cancel.Token);
                _cache = LoadCache();
                FindAliases();
                BuildDays();
                Changed?.Invoke();
            }
            if (Days.Count == 0)
            {
                Status = _pages.Count == 0
                    ? "No page collected yet: 🕷 RUN in /collectionplan"
                    : Suggestions.Count > 0
                        ? $"No page of the {_pages.Count} collected names « {Query} »: maybe one of the names below"
                        : $"No page of the {_pages.Count} collected names « {Query} »";
                return;
            }
            await AnalyseAsync(force, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Status = "Analysis stopped: the days read so far are kept";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            IsError = true;
            Status = $"The pages could not be read ({_searchEngineUrl}): {ex.Message}";
            ActivityLog.Error("Entity", $"Pages of « {Query} » not read", ex);
        }
        finally
        {
            if (ReferenceEquals(_run, cancel))
                _run = null;
            cancel.Dispose();
            IsRunning = false;
        }
    }

    // ── The pages ──

    private sealed record PageEntityData(string Name, string Type);

    private sealed record PageData(string Id, string Name, DateOnly Day, string Website, string Url, string RenderingPath, string Language,
        List<PageEntityData> Entities, List<(string Title, List<string> Paragraphs)> Sections);

    private async Task<List<PageData>> LoadPagesAsync(CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, object>
        {
            ["size"] = PagesRead,
            ["_source"] = new[] { "id", "name", "pageTitle", "day", "scrapedAt", "websiteName", "url", "finalUrl", "renderingPath", "pageLanguage", "entities", "sections", "isDeleted" },
            ["sort"] = new object[] { new Dictionary<string, object> { ["scrapedAt"] = new Dictionary<string, string> { ["order"] = "desc" } } },
            ["query"] = new Dictionary<string, object> { ["match_all"] = new Dictionary<string, object>() }
        };
        using var content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync($"{_searchEngineUrl}/page/_search", content, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var pages = new List<PageData>();
        if (!document.RootElement.TryGetProperty("hits", out var outer) || !outer.TryGetProperty("hits", out var hits))
            return pages;
        foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("_source", out var source) || Bool(source, "isDeleted"))
                continue;
            var day = DateOnly.TryParseExact(Text(source, "day"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : source.TryGetProperty("scrapedAt", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var date) ? DateOnly.FromDateTime(date.ToLocalTime()) : default;
            if (day == default)
                continue;
            var url = Text(source, "finalUrl") is { Length: > 0 } final ? final : Text(source, "url");
            var sections = Items(source, "sections").Select(s => (Text(s, "title"), Texts(s, "paragraphs"))).ToList();
            pages.Add(new PageData(
                hit.TryGetProperty("_id", out var id) ? id.GetString() ?? Text(source, "id") : Text(source, "id"),
                Text(source, "name") is { Length: > 0 } name ? name : Text(source, "pageTitle"),
                day,
                Text(source, "websiteName") is { Length: > 0 } site ? site : Host(url),
                url,
                Text(source, "renderingPath"),
                Text(source, "pageLanguage"),
                Items(source, "entities").Select(e => new PageEntityData(Text(e, "name"), Text(e, "type"))).Where(e => e.Name.Length > 1).ToList(),
                sections));
        }
        return pages;
    }

    // ── The ways the entity is written ──

    private static readonly char[] WordSeparators = [' ', '-', '\'', '’', '.', ',', '(', ')', '/', '"', '«', '»'];

    private static List<string> Words(string folded) =>
        folded.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).ToList();

    /// <summary>
    /// The ways the entity is written: the names of the entity recogniser that hold the words typed (or that the words
    /// typed hold), whatever the accents, the case and a typing mistake; and, for a single word typed, the words of the
    /// texts close to it. A longer name that holds the word typed is ticked for a person (a surname is written alone),
    /// left unticked otherwise (« France » does not make « Banque de France »).
    /// </summary>
    private void FindAliases()
    {
        var previous = Aliases.ToDictionary(a => a.Folded, a => a.Chosen, StringComparer.Ordinal);
        var query = Words(DisseminationQuery.Fold(Query));
        var aliases = new Dictionary<string, Alias>(StringComparer.Ordinal);
        if (query.Count == 0)
        {
            Aliases = [];
            return;
        }

        // The names of the entity recogniser, each with its type met most often.
        var names = _pages.SelectMany(p => p.Entities).GroupBy(e => DisseminationQuery.Fold(e.Name).Trim())
            .Where(g => g.Key.Length >= 2)
            .Select(g => (Folded: g.Key, Name: g.GroupBy(e => e.Name.Trim()).OrderByDescending(n => n.Count()).First().Key,
                Type: g.GroupBy(e => e.Type).OrderByDescending(t => t.Count()).First().Key))
            .ToList();
        foreach (var (folded, name, type) in names)
        {
            var match = Match(query, Words(folded));
            if (match == NameMatch.None)
                continue;
            aliases[folded] = new Alias
            {
                Name = name,
                Type = type,
                Origin = "entity recogniser",
                Folded = folded,
                Chosen = previous.TryGetValue(folded, out var chosen) ? chosen : match != NameMatch.QueryInName || PersonTypes.Contains(type)
            };
        }

        // A single word typed: the words of the texts close to it (the right spelling of a name typed wrong).
        if (query.Count == 1)
        {
            var spellings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (var block in _pages.SelectMany(p => p.Sections.SelectMany(s => s.Paragraphs.Prepend(s.Title))))
                foreach (System.Text.RegularExpressions.Match word in System.Text.RegularExpressions.Regex.Matches(block, @"\p{L}[\p{L}\-’']*"))
                {
                    var folded = DisseminationQuery.Fold(word.Value).Trim('-', '\'', '’');
                    if (Math.Abs(folded.Length - query[0].Length) > 2 || !Close(folded, query[0]))
                        continue;
                    var forms = spellings.TryGetValue(folded, out var known) ? known : spellings[folded] = new Dictionary<string, int>(StringComparer.Ordinal);
                    var form = word.Value.Trim('-', '\'', '’');
                    forms[form] = forms.GetValueOrDefault(form) + 1;
                }
            foreach (var (folded, forms) in spellings)
            {
                // Already written by a name ticked of the entity recogniser: no need of a second way.
                if (aliases.ContainsKey(folded) || aliases.Values.Any(a => a.Chosen && Words(a.Folded).Contains(folded)))
                    continue;
                aliases[folded] = new Alias
                {
                    Name = forms.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal).First().Key,
                    Origin = "text",
                    Folded = folded,
                    Chosen = !previous.TryGetValue(folded, out var chosen) || chosen
                };
            }
        }

        Aliases = aliases.Values.ToList();
        Suggestions = Aliases.Count > 0
            ? []
            : names.Select(n => (n.Name, Distance: Words(n.Folded).Select(w => query.Min(q => Distance(q, w))).DefaultIfEmpty(99).Min()))
                .Where(n => n.Distance <= 3).OrderBy(n => n.Distance).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Select(n => n.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    }

    private enum NameMatch { None, Same, QueryInName, NameInQuery }

    /// <summary>How the words typed and the words of a name match: the same, the name holds them, or they hold the name.</summary>
    private static NameMatch Match(List<string> query, List<string> name)
    {
        if (name.Count == 0)
            return NameMatch.None;
        bool Holds(List<string> longer, List<string> shorter) => shorter.All(s => longer.Any(l => Close(l, s)));
        if (query.Count == name.Count && Holds(name, query))
            return NameMatch.Same;
        if (name.Count > query.Count && Holds(name, query))
            return NameMatch.QueryInName;
        if (query.Count > name.Count && Holds(query, name))
            return NameMatch.NameInQuery;
        return NameMatch.None;
    }

    /// <summary>Two words that are the same but for a typing mistake: one letter for 4 to 7 letters, two from 8.</summary>
    public static bool Close(string a, string b)
    {
        if (a == b)
            return true;
        var length = Math.Min(a.Length, b.Length);
        var allowed = length <= 3 ? 0 : length <= 7 ? 1 : 2;
        return allowed > 0 && Math.Abs(a.Length - b.Length) <= allowed && Distance(a, b) <= allowed;
    }

    /// <summary>The edits from one word to another (a letter added, taken, changed, or two swapped).</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[a.Length, b.Length];
    }

    // ── The days ──

    /// <summary>The passages of the ways of writing ticked, day by day, with their sources, writing and neighbours.</summary>
    private void BuildDays()
    {
        var chosen = Aliases.Where(a => a.Chosen).Select(a => a.Folded).ToList();
        foreach (var alias in Aliases)
            alias.Mentions = 0;
        var byDay = new Dictionary<DateOnly, List<(PageData Page, Passage Passage)>>();
        // The passages: the titles and paragraphs that write a name ticked; and those that speak of the entity without
        // its name: every paragraph of a section whose title names it (an article about it), and the paragraph right
        // after one that names it (« il aurait… »).
        foreach (var page in _pages)
            foreach (var (title, paragraphs) in page.Sections)
            {
                var blocks = (title.Length > 0 ? new[] { (Text: title, IsTitle: true) } : []).Concat(paragraphs.Select(p => (Text: p, IsTitle: false)))
                    .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                    .Select(b => (b.Text, b.IsTitle, Folded: DisseminationQuery.Fold(b.Text)))
                    .ToList();
                var titleNames = title.Length > 0 && chosen.Any(c => DisseminationQuery.Places(DisseminationQuery.Fold(title), c).Any());
                var previousNamed = false;
                foreach (var (text, isTitle, folded) in blocks)
                {
                    foreach (var alias in Aliases.Where(a => DisseminationQuery.Places(folded, a.Folded).Any()))
                        alias.Mentions++;
                    var names = chosen.Any(c => DisseminationQuery.Places(folded, c).Any());
                    var speaks = names || titleNames || (previousNamed && !isTitle);
                    previousNamed = names;
                    if (!speaks)
                        continue;
                    var passage = new Passage(page.Day, page.Website, page.Id, page.Name, page.Url, page.RenderingPath, title, text, isTitle, folded, page.Language) { Names = names };
                    (byDay.TryGetValue(page.Day, out var list) ? list : byDay[page.Day] = []).Add((page, passage));
                }
            }
        Aliases = Aliases.OrderByDescending(a => a.Chosen).ThenByDescending(a => a.Mentions).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var days = new List<Day>();
        foreach (var (day, found) in byDay.OrderByDescending(d => d.Key))
        {
            // The same paragraph in two pages of the day (a front page and its article) is one passage.
            var passages = found.GroupBy(f => f.Passage.Folded).Select(g => g.First()).ToList();
            var texts = passages.Select(p => p.Passage.Text).ToList();
            var language = Common(passages.Select(p => p.Passage.Language));
            var sources = passages.GroupBy(p => p.Passage.Website)
                .Select(g => new Source(g.Key, g.Select(p => p.Page.Id).Distinct().Count(), g.Count(),
                    WritingCounter.Count(g.Select(p => p.Passage.Text), Common(g.Select(p => p.Passage.Language)))))
                .OrderByDescending(s => s.Passages).ThenBy(s => s.Website, StringComparer.OrdinalIgnoreCase).ToList();

            // The persons, places and events of the entity recogniser written in the passages, the entity left out.
            var near = passages.SelectMany(p => p.Page.Entities).GroupBy(e => DisseminationQuery.Fold(e.Name).Trim())
                .Where(g => g.Key.Length >= 2 && !chosen.Contains(g.Key) && !Aliases.Any(a => a.Folded == g.Key))
                .Select(g => (Name: g.GroupBy(e => e.Name).OrderByDescending(n => n.Count()).First().Key, Type: g.GroupBy(e => e.Type).OrderByDescending(t => t.Count()).First().Key,
                    Count: passages.Count(p => DisseminationQuery.Places(p.Passage.Folded, g.Key).Any())))
                .Where(e => e.Count > 0)
                .OrderByDescending(e => e.Count).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<Link> Of(HashSet<string> types) => near.Where(e => types.Contains(e.Type)).Take(12).Select(e => new Link(e.Name, string.Empty, e.Count)).ToList();

            // Its passages make the day: a way of writing unticked that leaves them as they were does not make it read again.
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                day.ToString("yyyyMMdd") + "|" + string.Join("\n", passages.Select(p => p.Passage.Website + ":" + p.Passage.Folded).OrderBy(t => t, StringComparer.Ordinal)))))[..32];
            var entityDay = new Day
            {
                Date = day,
                Passages = passages.Select(p => p.Passage).ToList(),
                Pages = passages.Select(p => p.Page.Id).Distinct().Count(),
                Sources = sources,
                Writing = WritingCounter.Count(texts, language),
                Adverbs = WritingCounter.Adverbs(texts, language),
                Conditionals = WritingCounter.ConditionalSentences(texts, language),
                FoundPersons = Of(PersonTypes),
                FoundPlaces = Of(PlaceTypes),
                FoundEvents = Of(EventTypes),
                Hash = hash
            };
            if (_cache.Days.TryGetValue(hash, out var kept))
                Apply(entityDay, kept, fromCache: true);
            days.Add(entityDay);
        }
        Days = days;
        var overviewHash = OverviewHash();
        if (_cache.OverviewHash == overviewHash && _cache.Overview is { } overview)
        {
            Overview = overview.Overview;
            OverviewSources = overview.Sources;
            OverviewWriting = overview.Writing;
        }
        else
            Overview = OverviewSources = OverviewWriting = string.Empty;
        OverviewError = string.Empty;
    }

    private string OverviewHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", Days.Select(d => d.Hash)))))[..32];

    private static string Common(IEnumerable<string> values) =>
        values.Where(v => v.Length > 0).GroupBy(v => v.ToLowerInvariant()).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? string.Empty;

    // ── What the model says ──

    /// <summary>
    /// The shape of the answer for a day, added to the prompt (whatever /prompts made of it): the model is asked for a JSON
    /// object in its answer rather than held to a schema, which the reasoning models (gpt-oss through LM Studio) fill
    /// with broken texts.
    /// </summary>
    private const string DayShape =
        "Answer with one JSON object only, nothing before or after it, of this shape: {\"emotions\": {\"angry\": 0, \"disgust\": 0, \"fearful\": 0, \"happy\": 0, "
        + "\"neutral\": 100, \"sad\": 0, \"surprised\": 0}, \"leading\": \"neutral\", \"intensity\": 0.2, \"valence\": 0.0, \"emotionSummary\": \"...\", "
        + "\"persons\": [{\"name\": \"...\", \"role\": \"...\"}], \"places\": [{\"name\": \"...\", \"role\": \"...\"}], \"events\": [{\"name\": \"...\", \"role\": \"...\"}], "
        + "\"summary\": \"...\", \"sources\": [{\"source\": \"...\", \"tone\": \"...\", \"notes\": \"...\"}], \"writing\": \"...\"}";

    private const string OverviewShape =
        "Answer with one JSON object only, nothing before or after it, of this shape: {\"overview\": \"...\", \"sources\": \"...\", \"writing\": \"...\"}";

    /// <summary>The JSON object the model writes in its answer (see <see cref="OllamaModelService.ChatForJsonAsync"/>).</summary>
    private Task<JsonDocument> AskJsonAsync(string model, string system, string user, CancellationToken cancellationToken) =>
        _llm!.ChatForJsonAsync(model, system, user, cancellationToken);

    /// <summary>The days not analysed yet (all of them with <paramref name="force"/>), the newest first, then the whole period.</summary>
    private async Task AnalyseAsync(bool force, CancellationToken cancellationToken)
    {
        var model = Model;
        if (_llm is null || string.IsNullOrWhiteSpace(model))
        {
            IsError = true;
            Status = "No model to analyse: choose the model of the advanced conversations in /models";
            return;
        }
        var todo = Days.Where(d => force || !d.Analysed).ToList();
        for (var i = 0; i < todo.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var day = todo[i];
            day.State = DayState.Reading;
            day.Error = string.Empty;
            DayChanged?.Invoke(day);
            Status = $"{model} reads {day.Date:dd/MM/yyyy}: {day.Passages.Count} passage(s) of {day.Sources.Count} source(s) · day {i + 1} of {todo.Count}…";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(CallTimeout);
                using var answer = await AskJsonAsync(model, Prompts.Get("entity.day", ("entity", DisplayName), ("language", _language)) + "\n\n" + DayShape, DayPrompt(day), timeout.Token);
                Read(day, answer.RootElement);
                day.Model = model;
                day.State = DayState.Done;
                day.FromCache = false;
                _cache.Days[day.Hash] = Keep(day);
                SaveCache();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                day.State = DayState.Waiting;
                DayChanged?.Invoke(day);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
            {
                day.State = DayState.Failed;
                day.Error = ex is OperationCanceledException ? $"no answer of {model} within {CallTimeout.TotalMinutes:0} minutes" : ex.Message;
                ActivityLog.Error("Entity", $"Day {day.Date:yyyy-MM-dd} of « {DisplayName} » not analysed", ex);
            }
            DayChanged?.Invoke(day);
        }

        var failed = Days.Count(d => d.State == DayState.Failed);
        if (Days.Any(d => d.Analysed) && (force || Overview.Length == 0))
            await OverviewAsync(model, cancellationToken);
        IsError = failed > 0;
        Status = $"{Days.Count(d => d.Analysed)} of {Days.Count} day(s) analysed by {model}"
                 + (failed > 0 ? $" · {failed} failed (ANALYSE AGAIN reads them again)" : string.Empty)
                 + $" · {PassageCount} passage(s) in {PageCount} page(s) of {Websites.Count} source(s)";
    }

    /// <summary>What the model is given for a day: the sources, the writing counted, the names found near the entity, the passages.</summary>
    private string DayPrompt(Day day)
    {
        var text = new StringBuilder();
        text.AppendLine($"Entity: {DisplayName}" + (Aliases.Count(a => a.Chosen) > 1 ? $" (also written: {string.Join(", ", Aliases.Where(a => a.Chosen && a.Name != DisplayName).Select(a => a.Name))})" : string.Empty));
        text.AppendLine($"Day: {day.Date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}");
        text.AppendLine();
        text.AppendLine("Sources and their writing, measured by a program on the passages about the entity:");
        foreach (var source in day.Sources)
        {
            var w = source.Writing;
            text.AppendLine(w is null
                ? $"- {source.Website}: {source.Passages} passage(s) in {source.Pages} page(s)"
                : $"- {source.Website}: {source.Passages} passage(s) in {source.Pages} page(s), {w.SentenceCount} sentence(s), {w.ConditionalSentenceCount} in the conditional, "
                  + $"{w.AdverbCount} adverb(s), {w.AverageSentenceLength:0.#} words per sentence, vocabulary {w.VocabularyComplexity:0}/100");
        }
        if (day.Adverbs.Count > 0)
            text.AppendLine($"Adverbs used the most: {string.Join(", ", day.Adverbs.Take(10).Select(a => $"{a.Adverb} ({a.Count})"))}");
        text.AppendLine();
        void Names(string what, List<Link> names)
        {
            if (names.Count > 0)
                text.AppendLine($"{what} found near the entity by the entity recogniser: {string.Join(", ", names.Select(n => $"{n.Name} ({n.Mentions})"))}");
        }
        Names("Persons", day.FoundPersons);
        Names("Places", day.FoundPlaces);
        Names("Events", day.FoundEvents);

        // The passages, every source kept: one of each in turn, until the length allowed.
        var queues = day.Passages.GroupBy(p => p.Website).OrderByDescending(g => g.Count()).Select(g => new Queue<Passage>(g.OrderByDescending(p => p.Names))).ToList();
        var chosen = new List<Passage>();
        var length = 0;
        while (queues.Any(q => q.Count > 0) && length < DayChars)
            foreach (var queue in queues.Where(q => q.Count > 0))
            {
                var passage = queue.Dequeue();
                chosen.Add(passage);
                length += Math.Min(PassageChars, passage.Text.Length) + 40;
                if (length >= DayChars)
                    break;
            }
        day.PassagesRead = chosen.Count;
        text.AppendLine();
        text.AppendLine($"Passages ({chosen.Count} of {day.Passages.Count}):");
        foreach (var passage in chosen)
        {
            var body = passage.Text.Length > PassageChars ? passage.Text[..PassageChars] + "…" : passage.Text;
            var section = !passage.IsTitle && passage.SectionTitle.Length > 0 ? $" ({passage.SectionTitle})" : string.Empty;
            text.AppendLine($"[{passage.Website}]{section} {(passage.IsTitle ? "TITLE: " : string.Empty)}{body}");
        }
        return text.ToString();
    }

    private static void Read(Day day, JsonElement answer)
    {
        var shares = DisseminationWindowViewModel.EmotionNames.Select(n => answer.TryGetProperty("emotions", out var e) ? Math.Max(0, Number(e, n)) : 0).ToArray();
        var sum = shares.Sum();
        if (sum <= 0)
        {
            shares[Array.IndexOf(DisseminationWindowViewModel.EmotionNames, "neutral")] = 100;
            sum = 100;
        }
        day.Emotions = shares.Select(s => Math.Round(s * 100 / sum, 1)).ToArray();
        var leading = Text(answer, "leading").ToLowerInvariant();
        day.Leading = DisseminationWindowViewModel.EmotionNames.Contains(leading)
            ? leading
            : DisseminationWindowViewModel.EmotionNames[Array.IndexOf(day.Emotions, day.Emotions.Max())];
        day.Valence = Math.Round(Math.Clamp(Number(answer, "valence"), -1, 1), 2);
        day.Intensity = Math.Round(Math.Clamp(Number(answer, "intensity"), 0, 1), 2);
        day.EmotionSummary = Text(answer, "emotionSummary");
        List<Link> Links(string name, List<Link> found) => Items(answer, name)
            .Select(i => (Name: Text(i, "name"), Role: Text(i, "role")))
            .Where(i => i.Name.Length > 1)
            .GroupBy(i => DisseminationQuery.Fold(i.Name)).Select(g => g.First())
            .Select(i => new Link(i.Name, i.Role, found.FirstOrDefault(f => DisseminationQuery.Fold(f.Name) == DisseminationQuery.Fold(i.Name))?.Mentions
                                                       ?? day.Passages.Count(p => DisseminationQuery.Places(p.Folded, DisseminationQuery.Fold(i.Name)).Any())))
            .ToList();
        day.Persons = Links("persons", day.FoundPersons);
        day.Places = Links("places", day.FoundPlaces);
        day.Events = Links("events", day.FoundEvents);
        day.Summary = Text(answer, "summary");
        day.WritingNotes = Text(answer, "writing");
        foreach (var note in Items(answer, "sources"))
        {
            var name = DisseminationQuery.Fold(Text(note, "source"));
            if (day.Sources.FirstOrDefault(s => DisseminationQuery.Fold(s.Website) == name || name.Contains(DisseminationQuery.Fold(s.Website), StringComparison.Ordinal)) is { } source)
            {
                source.Tone = Text(note, "tone");
                source.Notes = Text(note, "notes");
            }
        }
    }

    /// <summary>The whole period: the evolution, the sources and the writing, from what the model said of each day.</summary>
    private async Task OverviewAsync(string model, CancellationToken cancellationToken)
    {
        OverviewReading = true;
        OverviewError = string.Empty;
        OverviewChanged?.Invoke();
        Status = $"{model} writes the summary of the {Days.Count} day(s)…";
        try
        {
            var text = new StringBuilder();
            text.AppendLine($"Entity: {DisplayName}");
            text.AppendLine($"Period: {Days.Min(d => d.Date):yyyy-MM-dd} to {Days.Max(d => d.Date):yyyy-MM-dd}, {Days.Count} day(s) collected, {PassageCount} passage(s) in {PageCount} page(s)");
            text.AppendLine();
            text.AppendLine("Day by day (newest first):");
            foreach (var day in Days)
            {
                text.Append($"- {day.Date:yyyy-MM-dd}: {day.Passages.Count} passage(s); sources: {string.Join(", ", day.Sources.Select(s => $"{s.Website} {s.Passages}"))}");
                if (day.Analysed)
                    text.Append($"; leading emotion {day.Leading}, valence {day.Valence:+0.00;-0.00;0.00}; persons: {string.Join(", ", day.Persons.Take(6).Select(p => p.Name))}; "
                                + $"places: {string.Join(", ", day.Places.Take(6).Select(p => p.Name))}; events: {string.Join(", ", day.Events.Take(6).Select(p => p.Name))}; summary: {day.Summary}");
                text.AppendLine();
            }
            text.AppendLine();
            text.AppendLine("Writing per source over the period, measured by a program:");
            foreach (var source in SourceTotals())
                text.AppendLine($"- {source.Website}: {source.Passages} passage(s), {source.Sentences} sentence(s), {source.Conditional} in the conditional ({source.ConditionalShare:0.#} %), "
                                + $"{source.Adverbs} adverb(s) ({source.AdverbsPer100Words:0.#} per 100 words), {source.AverageSentenceLength:0.#} words per sentence");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CallTimeout);
            using var answer = await AskJsonAsync(model, Prompts.Get("entity.overview", ("entity", DisplayName), ("language", _language)) + "\n\n" + OverviewShape, text.ToString(), timeout.Token);
            Overview = Text(answer.RootElement, "overview");
            OverviewSources = Text(answer.RootElement, "sources");
            OverviewWriting = Text(answer.RootElement, "writing");
            _cache.OverviewHash = OverviewHash();
            _cache.Overview = new CachedOverview { Overview = Overview, Sources = OverviewSources, Writing = OverviewWriting };
            SaveCache();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            OverviewError = ex is OperationCanceledException ? $"no answer of {model} within {CallTimeout.TotalMinutes:0} minutes" : ex.Message;
        }
        finally
        {
            OverviewReading = false;
            OverviewChanged?.Invoke();
        }
    }

    /// <summary>The writing of each source over the whole period.</summary>
    public sealed record SourceTotal(string Website, int Days, int Pages, int Passages, int Sentences, int Words, int Conditional, int Adverbs)
    {
        public double ConditionalShare => Sentences > 0 ? Conditional * 100.0 / Sentences : 0;
        public double AdverbsPer100Words => Words > 0 ? Adverbs * 100.0 / Words : 0;
        public double AverageSentenceLength => Sentences > 0 ? Words / (double)Sentences : 0;
    }

    public List<SourceTotal> SourceTotals() => Days.SelectMany(d => d.Sources.Select(s => (d.Date, Source: s)))
        .GroupBy(s => s.Source.Website)
        .Select(g => new SourceTotal(g.Key, g.Select(s => s.Date).Distinct().Count(), g.Sum(s => s.Source.Pages), g.Sum(s => s.Source.Passages),
            g.Sum(s => s.Source.Writing?.SentenceCount ?? 0), g.Sum(s => s.Source.Writing?.WordCount ?? 0),
            g.Sum(s => s.Source.Writing?.ConditionalSentenceCount ?? 0), g.Sum(s => s.Source.Writing?.AdverbCount ?? 0)))
        .OrderByDescending(s => s.Passages).ThenBy(s => s.Website, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The persons, places or events linked to the entity over the whole period: by how many passages name them.</summary>
    public List<Link> TopLinks(Func<Day, List<Link>> analysed, Func<Day, List<Link>> found) =>
        Days.SelectMany(d => (d.Analysed && analysed(d).Count > 0 ? analysed(d) : found(d)).Select(l => (Day: d, Link: l)))
            .GroupBy(l => DisseminationQuery.Fold(l.Link.Name))
            .Select(g => new Link(g.GroupBy(l => l.Link.Name).OrderByDescending(n => n.Count()).First().Key,
                string.Join(" · ", g.Select(l => l.Link.Role).Where(r => r.Length > 0).Distinct().Take(3)),
                g.Sum(l => Math.Max(1, l.Link.Mentions))))
            .OrderByDescending(l => l.Mentions).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Take(15).ToList();

    // ── The report ──

    /// <summary>The whole analysis in Markdown (⧉ COPY REPORT).</summary>
    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"# {DisplayName}");
        text.AppendLine();
        text.AppendLine($"{PassageCount} passages in {PageCount} pages of {Websites.Count} sources, {Days.Count} days collected"
                        + (Days.Count > 0 ? $", from {Days.Min(d => d.Date):yyyy-MM-dd} to {Days.Max(d => d.Date):yyyy-MM-dd}" : string.Empty) + $" · analysed by {Model}.");
        text.AppendLine();
        if (Overview.Length > 0)
        {
            text.AppendLine("## Overview").AppendLine().AppendLine(Overview).AppendLine();
            text.AppendLine("### Sources").AppendLine().AppendLine(OverviewSources).AppendLine();
            text.AppendLine("### Writing").AppendLine().AppendLine(OverviewWriting).AppendLine();
        }
        text.AppendLine("| Source | Days | Passages | Sentences | Conditional | Adverbs |").AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var s in SourceTotals())
            text.AppendLine($"| {s.Website} | {s.Days} | {s.Passages} | {s.Sentences} | {s.Conditional} ({s.ConditionalShare:0.#} %) | {s.Adverbs} ({s.AdverbsPer100Words:0.#}/100 words) |");
        foreach (var day in Days)
        {
            text.AppendLine().AppendLine($"## {day.Date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}").AppendLine();
            text.AppendLine($"{day.Passages.Count} passages in {day.Pages} pages: {string.Join(", ", day.Sources.Select(s => $"{s.Website} ({s.Passages})"))}.");
            if (day.Analysed)
            {
                text.AppendLine().AppendLine($"**Emotions**: {day.Leading}, valence {day.Valence:+0.00;-0.00;0.00}, intensity {day.Intensity:0.00} · "
                                             + string.Join(", ", DisseminationWindowViewModel.EmotionNames.Select((n, i) => (n, day.Emotions[i])).Where(e => e.Item2 >= 1).Select(e => $"{e.n} {e.Item2:0} %"))
                                             + $". {day.EmotionSummary}");
                text.AppendLine().AppendLine(day.Summary);
                void Links(string what, List<Link> links)
                {
                    if (links.Count > 0)
                        text.AppendLine().AppendLine($"**{what}**: " + string.Join("; ", links.Select(l => l.Role.Length > 0 ? $"{l.Name} ({l.Role})" : l.Name)));
                }
                Links("Persons", day.Persons);
                Links("Places", day.Places);
                Links("Events", day.Events);
            }
            if (day.Writing is { } w)
                text.AppendLine().AppendLine($"**Writing**: {w.SentenceCount} sentences, {w.ConditionalSentenceCount} in the conditional, {w.AdverbCount} adverbs, {w.AverageSentenceLength:0.#} words per sentence."
                                             + (day.WritingNotes.Length > 0 ? " " + day.WritingNotes : string.Empty));
            foreach (var source in day.Sources.Where(s => s.Tone.Length > 0 || s.Notes.Length > 0))
                text.AppendLine($"- {source.Website}: {source.Tone}. {source.Notes}");
        }
        return text.ToString();
    }

    /// <summary>The analysis of the picture at <paramref name="path"/> (the capture of a page), at its real size; null when the file is not here.</summary>
    public ImageAnalysisViewModel? OpenImage(string path)
    {
        if (_createAnalysis is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = $"The picture is not on this machine: {path}";
            return null;
        }
        var analysis = _createAnalysis(path);
        analysis.OpenAtActualSize = true;
        return analysis;
    }

    // ── What is kept from one opening to the next ──

    private sealed class CachedDay
    {
        public double[] Emotions { get; set; } = [];
        public string Leading { get; set; } = string.Empty;
        public double Valence { get; set; }
        public double Intensity { get; set; }
        public string EmotionSummary { get; set; } = string.Empty;
        public List<Link> Persons { get; set; } = [];
        public List<Link> Places { get; set; } = [];
        public List<Link> Events { get; set; } = [];
        public string Summary { get; set; } = string.Empty;
        public string WritingNotes { get; set; } = string.Empty;
        public Dictionary<string, string[]> Sources { get; set; } = [];
        public int PassagesRead { get; set; }
        public string Model { get; set; } = string.Empty;
    }

    private sealed class CachedOverview
    {
        public string Overview { get; set; } = string.Empty;
        public string Sources { get; set; } = string.Empty;
        public string Writing { get; set; } = string.Empty;
    }

    private sealed class Cache
    {
        public Dictionary<string, CachedDay> Days { get; set; } = [];
        public string OverviewHash { get; set; } = string.Empty;
        public CachedOverview? Overview { get; set; }
    }

    private static CachedDay Keep(Day day) => new()
    {
        Emotions = day.Emotions,
        Leading = day.Leading,
        Valence = day.Valence,
        Intensity = day.Intensity,
        EmotionSummary = day.EmotionSummary,
        Persons = day.Persons,
        Places = day.Places,
        Events = day.Events,
        Summary = day.Summary,
        WritingNotes = day.WritingNotes,
        Sources = day.Sources.ToDictionary(s => s.Website, s => new[] { s.Tone, s.Notes }),
        PassagesRead = day.PassagesRead,
        Model = day.Model
    };

    private static void Apply(Day day, CachedDay kept, bool fromCache)
    {
        day.Emotions = kept.Emotions.Length == DisseminationWindowViewModel.EmotionNames.Length ? kept.Emotions : day.Emotions;
        day.Leading = kept.Leading;
        day.Valence = kept.Valence;
        day.Intensity = kept.Intensity;
        day.EmotionSummary = kept.EmotionSummary;
        day.Persons = kept.Persons;
        day.Places = kept.Places;
        day.Events = kept.Events;
        day.Summary = kept.Summary;
        day.WritingNotes = kept.WritingNotes;
        foreach (var source in day.Sources)
            if (kept.Sources.TryGetValue(source.Website, out var note) && note.Length == 2)
                (source.Tone, source.Notes) = (note[0], note[1]);
        day.PassagesRead = kept.PassagesRead;
        day.Model = kept.Model;
        day.State = DayState.Done;
        day.FromCache = fromCache;
    }

    /// <summary>The file of the analyses of this entity: ~/kaonomi/entity-analyses/&lt;name&gt;.json.</summary>
    private string CacheFile
    {
        get
        {
            var name = new string(DisseminationQuery.Fold(Query).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            return Path.Combine(ChatSettings.KaonomiHome, "entity-analyses", (name.Length > 0 ? name : "entity") + ".json");
        }
    }

    private Cache LoadCache()
    {
        try
        {
            return File.Exists(CacheFile) ? JsonSerializer.Deserialize<Cache>(File.ReadAllText(CacheFile)) ?? new Cache() : new Cache();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Cache();
        }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            // Only the days of now: an analysis of passages that changed since is not kept.
            var current = Days.Select(d => d.Hash).ToHashSet(StringComparer.Ordinal);
            _cache.Days = _cache.Days.Where(d => current.Contains(d.Key)).ToDictionary(d => d.Key, d => d.Value);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            LocalSync.Touch("entityanalysis");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Entity", $"The analyses of « {Query} » could not be kept", ex);
        }
    }

    public void Dispose()
    {
        _run?.Cancel();
        GC.SuppressFinalize(this);
    }

    // ── JSON ──

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", string.Empty) : "(unknown)";

    private static string Text(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Clean(value.GetString()) : string.Empty;

    /// <summary>
    /// A text without the characters the fonts of the chat do not draw (shown as boxes): the non-breaking hyphens a
    /// model writes (« Jean‑Luc ») become hyphens, the narrow no-break spaces of the French typography no-break spaces.
    /// </summary>
    private static string Clean(string? text) =>
        (text ?? string.Empty).Replace('\u2011', '-').Replace('\u2010', '-').Replace('\u202F', '\u00A0').Replace('\u2009', ' ').Trim();

    private static double Number(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static bool Bool(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static List<string> Texts(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? string.Empty).Where(v => v.Length > 0).ToList()
            : [];

    private static List<JsonElement> Items(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object).ToList()
            : [];
}
