using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A picture tied to a paragraph: its file, its address, its caption and what the image model said of it.</summary>
public sealed record DisseminationPicture(string Path, string Url, string Alt, string Description)
{
    public bool Exists => Path.Length > 0 && File.Exists(Path);
}

/// <summary>An entity of an analysed page: its kind and the emotions the page expresses about it.</summary>
public sealed record DisseminationEntity(string Name, string Type, IReadOnlyList<string> FoldedNames, string Leading, double Valence, double Intensity,
    IReadOnlyDictionary<string, double> Emotions, string Summary, bool HasEmotion);

/// <summary>A relation of the graph of an analysed page.</summary>
public sealed record DisseminationRelation(string Source, string Type, string Target, string Description, string Evidence, int Confidence);

/// <summary>An analysed page: what the results show of it.</summary>
public sealed class DisseminationPage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Website { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public DateOnly Day { get; init; }
    public bool IsMainPage { get; init; }
    public string RenderingPath { get; init; } = string.Empty;
    public IReadOnlyList<DisseminationEntity> Entities { get; init; } = [];
    public IReadOnlyList<DisseminationRelation> Relations { get; init; } = [];
    public IReadOnlyList<DisseminationPicture> Pictures { get; init; } = [];

    public bool HasRendering => RenderingPath.Length > 0 && File.Exists(RenderingPath);
}

/// <summary>
/// A result of <c>/dissimination</c>: a title or a paragraph of an analysed page, with the entities it names and the
/// pictures tied to it.
/// </summary>
public sealed class DisseminationHit
{
    public DisseminationPage Page { get; init; } = new();

    /// <summary>The title of the section the paragraph is in (itself for a title), or empty.</summary>
    public string SectionTitle { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <summary>True for a title of the page, false for a paragraph.</summary>
    public bool IsTitle { get; init; }

    /// <summary>The whole section: its title and its paragraphs (what READ shows).</summary>
    public IReadOnlyList<string> Section { get; init; } = [];

    /// <summary>The text folded (lower case, no accents), character for character.</summary>
    public string Folded { get; init; } = string.Empty;

    /// <summary>The entities of the page the paragraph names.</summary>
    public IReadOnlyList<DisseminationEntity> Entities { get; init; } = [];

    public IReadOnlyList<DisseminationPicture> Pictures { get; init; } = [];

    /// <summary>
    /// True when the paragraph names <paramref name="foldedTerm"/>: written in it as whole words, or a name of an
    /// entity it names (a paragraph that writes « Macron » answers « Emmanuel Macron » when the analysis knows both).
    /// </summary>
    public bool Names(string foldedTerm) =>
        DisseminationQuery.Places(Folded, foldedTerm).Any() || Entities.Any(e => e.FoldedNames.Contains(foldedTerm));
}

/// <summary>A search kept in the window: a series of the chart, with its colour and its results.</summary>
public sealed class DisseminationSearch
{
    public string Text { get; init; } = string.Empty;
    public DisseminationQuery Query { get; init; } = DisseminationQuery.Parse(string.Empty).Query!;

    /// <summary>The rank of its colour among the colours of the series.</summary>
    public int Color { get; init; }

    /// <summary>The paragraphs that answer it, within the plan, the dates and the filters.</summary>
    public List<DisseminationHit> Hits { get; set; } = [];

    public string Label => Text.Length > 0 ? Text : "All entities";
}

/// <summary>A count for a bar of a panel: a website, an entity (with its kind).</summary>
public sealed record DisseminationCount(string Name, int Count, string Kind = "");

/// <summary>A relation among the results: how many pages state it.</summary>
public sealed record DisseminationRelationCount(string Source, string Type, string Target, int Pages, string Evidence);

/// <summary>
/// <c>/dissimination</c>: how the entities spread in what the web scraper collected and analysed (the indices
/// <c>pageanalysis</c> and <c>page</c> of the search engine). The window chooses a collection plan and a period, and
/// keeps one or more searches: names of entities joined by AND, OR, NOT and parentheses (<see cref="DisseminationQuery"/>).
/// A result is a title or a paragraph of an analysed page. For the searches it gives the number of results per day
/// (the bars of the chart), and for the search selected its results with the pictures tied to each paragraph, the
/// websites it comes from, the entities named with it, the emotions expressed about them and the relations stated
/// between them. A click in a chart filters the results (a day, a website, an entity).
/// </summary>
public partial class DisseminationWindowViewModel : ViewModelBase
{
    public const int MostSearches = 6;
    private const int PageSize = 30;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The seven emotions of the analyses, in the order of the charts.</summary>
    public static readonly string[] EmotionNames = ["happy", "surprised", "neutral", "sad", "fearful", "angry", "disgust"];

    private readonly string _searchEngineUrl;
    private readonly Func<string, ImageAnalysisViewModel>? _createAnalysis;
    private List<DisseminationHit> _all = [];
    private List<DisseminationPage> _pages = [];
    private bool _loading;

    /// <summary>Design-time constructor.</summary>
    public DisseminationWindowViewModel() : this(ChatSettings.Load().SearchEngineUrl, null, load: false)
    {
    }

    public DisseminationWindowViewModel(string searchEngineUrl, Func<string, ImageAnalysisViewModel>? createAnalysis, bool load = true)
    {
        _searchEngineUrl = searchEngineUrl.TrimEnd('/');
        _createAnalysis = createAnalysis;
        Searches.Add(new DisseminationSearch());
        Selected = Searches[0];
        if (load)
            _ = RefreshAsync();
    }

    /// <summary>Raised when what the window shows changed (data read, a search, a filter): the view draws it again.</summary>
    public event Action? Changed;

    // ── The selection ──

    /// <summary>The collection plans that have analysed pages: (id, name); the first one is every plan.</summary>
    public List<(string Id, string Name)> Plans { get; private set; } = [(string.Empty, "All collection plans")];

    [ObservableProperty]
    private string _planId = string.Empty;

    /// <summary>The first day of the period, yyyy-MM-dd.</summary>
    [ObservableProperty]
    private string _dateFrom = string.Empty;

    /// <summary>The last day of the period, yyyy-MM-dd.</summary>
    [ObservableProperty]
    private string _dateTo = string.Empty;

    /// <summary>The search being typed.</summary>
    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private string _status = "Reading the analyses…";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isRunning;

    /// <summary>The searches kept: the series of the chart.</summary>
    public List<DisseminationSearch> Searches { get; } = [];

    /// <summary>The search whose results are shown.</summary>
    public DisseminationSearch Selected { get; private set; } = null!;

    /// <summary>The filters set by a click in a chart, or null.</summary>
    public DateOnly? DayFilter { get; private set; }

    public string? WebsiteFilter { get; private set; }

    public string? EntityFilter { get; private set; }

    // ── What is shown ──

    /// <summary>The days of the period, in order.</summary>
    public List<DateOnly> Days { get; private set; } = [];

    /// <summary>For each search, its number of results on each day of <see cref="Days"/>.</summary>
    public List<int[]> Series { get; private set; } = [];

    /// <summary>The mean valence of the entities named in the results of the search selected, per day (NaN: none).</summary>
    public double[] Valence { get; private set; } = [];

    /// <summary>The results of the search selected, after the filters, the newest first.</summary>
    public List<DisseminationHit> Results { get; private set; } = [];

    /// <summary>How many of <see cref="Results"/> are shown (SHOW MORE shows more).</summary>
    public int Shown { get; private set; } = PageSize;

    public List<DisseminationCount> Websites { get; private set; } = [];

    public List<DisseminationCount> TopEntities { get; private set; } = [];

    /// <summary>The share of each emotion (<see cref="EmotionNames"/>), 0 to 100, about the entities named in the results.</summary>
    public double[] Emotions { get; private set; } = new double[EmotionNames.Length];

    /// <summary>The mean valence about the entities named in the results (-1 to 1), or NaN.</summary>
    public double MeanValence { get; private set; } = double.NaN;

    public List<DisseminationRelationCount> Relations { get; private set; } = [];

    public int PageCount { get; private set; }

    public int AnalysedPages => _pages.Count;

    partial void OnPlanIdChanged(string value)
    {
        if (_loading)
            return;
        ClearFilters(false);
        FitDates();
        Recompute();
    }

    // ── Reading the search engine ──

    private bool CanRefresh() => !IsRunning;

    /// <summary>REFRESH: the analyses and their pages read again.</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRunning = true;
        IsError = false;
        Status = "Reading the analyses…";
        try
        {
            var pages = await LoadAsync(CancellationToken.None);
            _pages = pages.Pages;
            _all = pages.Hits;
            _loading = true;
            Plans = [(string.Empty, "All collection plans"), .. _pages.Where(p => p.PlanId.Length > 0).GroupBy(p => p.PlanId)
                .Select(g => (g.Key, g.First().PlanName.Length > 0 ? g.First().PlanName : g.Key)).OrderBy(p => p.Item2, StringComparer.OrdinalIgnoreCase)];
            if (Plans.All(p => p.Id != PlanId))
                PlanId = string.Empty;
            _loading = false;
            if (!TryDay(DateFrom, out _) || !TryDay(DateTo, out _))
                FitDates();
            Recompute();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            IsError = true;
            Status = $"The search engine could not be read ({_searchEngineUrl}): {ex.Message}";
            ActivityLog.Error("Dissemination", "Analyses not read", ex);
        }
        finally
        {
            _loading = false;
            IsRunning = false;
        }
    }

    private async Task<(List<DisseminationPage> Pages, List<DisseminationHit> Hits)> LoadAsync(CancellationToken cancellationToken)
    {
        var pages = new List<DisseminationPage>();
        var hits = new List<DisseminationHit>();
        var query = new Dictionary<string, object>
        {
            ["size"] = 500,
            ["sort"] = new object[] { new Dictionary<string, object> { ["analysedAt"] = new Dictionary<string, string> { ["order"] = "desc" } } },
            ["query"] = new Dictionary<string, object> { ["match_all"] = new Dictionary<string, object>() }
        };
        using var content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync($"{_searchEngineUrl}/pageanalysis/_search", content, cancellationToken);
        // No page analysed yet: the index does not exist.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (pages, hits);
        response.EnsureSuccessStatusCode();
        using var analyses = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!analyses.RootElement.TryGetProperty("hits", out var outer) || !outer.TryGetProperty("hits", out var list))
            return (pages, hits);

        // The page of each analysis, a few at a time.
        var sources = list.EnumerateArray().Where(h => h.TryGetProperty("_source", out var s) && !Bool(s, "isDeleted") && Text(s, "sourcepageid").Length > 0)
            .Select(h => h.GetProperty("_source")).ToList();
        using var gate = new SemaphoreSlim(6);
        var loaded = await Task.WhenAll(sources.Select(async analysis =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                using var page = await Http.GetAsync($"{_searchEngineUrl}/page/_doc/{Uri.EscapeDataString(Text(analysis, "sourcepageid"))}", cancellationToken);
                if (!page.IsSuccessStatusCode)
                    return ((DisseminationPage Page, List<DisseminationHit> Hits)?)null;
                using var document = JsonDocument.Parse(await page.Content.ReadAsStringAsync(cancellationToken));
                return document.RootElement.TryGetProperty("_source", out var source) && !Bool(source, "isDeleted") ? ((DisseminationPage Page, List<DisseminationHit> Hits)?)Build(analysis, source) : null;
            }
            finally
            {
                gate.Release();
            }
        }));
        foreach (var item in loaded)
            if (item is { } built)
            {
                pages.Add(built.Page);
                hits.AddRange(built.Hits);
            }
        return (pages, hits);
    }

    /// <summary>The page and its titles and paragraphs, from its document and its analysis.</summary>
    private static (DisseminationPage Page, List<DisseminationHit> Hits) Build(JsonElement analysis, JsonElement source)
    {
        var entities = new List<DisseminationEntity>();
        foreach (var e in Items(analysis, "entities"))
        {
            var name = Text(e, "name");
            if (name.Length == 0)
                continue;
            var names = Texts(e, "aliases").Prepend(name).Select(DisseminationQuery.Fold).Where(n => n.Length >= 2).Distinct().ToList();
            var emotions = new Dictionary<string, double>();
            string leading = string.Empty, summary = string.Empty;
            double valence = 0, intensity = 0;
            var hasEmotion = e.TryGetProperty("emotion", out var emotion) && emotion.ValueKind == JsonValueKind.Object;
            if (hasEmotion)
            {
                leading = Text(emotion, "leading");
                summary = Text(emotion, "summary");
                valence = Number(emotion, "valence");
                intensity = Number(emotion, "intensity");
                if (emotion.TryGetProperty("emotions", out var shares) && shares.ValueKind == JsonValueKind.Object)
                    foreach (var emotionName in EmotionNames)
                        emotions[emotionName] = Number(shares, emotionName);
            }
            entities.Add(new DisseminationEntity(name, Text(e, "type"), names, leading, valence, intensity, emotions, summary, hasEmotion));
        }

        var relations = new List<DisseminationRelation>();
        if (analysis.TryGetProperty("graph", out var graph) && graph.ValueKind == JsonValueKind.Object)
            foreach (var edge in Items(graph, "edges"))
                relations.Add(new DisseminationRelation(Text(edge, "source"), Text(edge, "type"), Text(edge, "target"), Text(edge, "description"), Text(edge, "evidence"),
                    (int)Number(edge, "confidence")));

        // The pictures of the page, with what the image model said of them (by their file).
        var described = Items(analysis, "images").Where(i => Text(i, "path").Length > 0).GroupBy(i => Text(i, "path"))
            .ToDictionary(g => g.Key, g => (Description: Text(g.First(), "description"), Entities: Texts(g.First(), "entities").Select(DisseminationQuery.Fold).ToList()));
        var pictures = new List<(DisseminationPicture Picture, string Alt, string Context, List<string> Entities)>();
        foreach (var image in Items(source, "images"))
        {
            var path = Text(image, "path");
            if (path.Length == 0)
                continue;
            described.TryGetValue(path, out var told);
            pictures.Add((new DisseminationPicture(path, Text(image, "url"), Text(image, "alt"), told.Description ?? string.Empty),
                DisseminationQuery.Fold(Text(image, "alt")), DisseminationQuery.Fold(Text(image, "context")), told.Entities ?? []));
        }

        var day = DateOnly.TryParseExact(Text(source, "day"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : source.TryGetProperty("scrapedAt", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var date) ? DateOnly.FromDateTime(date.ToLocalTime()) : default;
        var finalUrl = Text(source, "finalUrl");
        var page = new DisseminationPage
        {
            Id = Text(analysis, "sourcepageid"),
            Name = Text(source, "name") is { Length: > 0 } pageName ? pageName : Text(source, "pageTitle"),
            Url = finalUrl.Length > 0 ? finalUrl : Text(source, "url"),
            Website = Text(source, "websiteName") is { Length: > 0 } site ? site : Host(Text(source, "url")),
            PlanId = Text(source, "collectionPlanId"),
            PlanName = Text(source, "collectionPlanName"),
            Day = day,
            IsMainPage = Bool(source, "isMainPage"),
            RenderingPath = Text(source, "renderingPath"),
            Entities = entities,
            Relations = relations,
            Pictures = pictures.Select(p => p.Picture).ToList()
        };

        var hits = new List<DisseminationHit>();
        foreach (var section in Items(source, "sections"))
        {
            var title = Text(section, "title");
            var paragraphs = Texts(section, "paragraphs");
            var whole = (title.Length > 0 ? new[] { title } : []).Concat(paragraphs).ToList();
            var foldedTitle = DisseminationQuery.Fold(title);
            for (var i = title.Length > 0 ? -1 : 0; i < paragraphs.Count; i++)
            {
                var text = i < 0 ? title : paragraphs[i];
                var folded = DisseminationQuery.Fold(text);
                var named = entities.Where(e => e.FoldedNames.Any(n => DisseminationQuery.Places(folded, n).Any())).ToList();
                hits.Add(new DisseminationHit
                {
                    Page = page,
                    SectionTitle = title,
                    Text = text,
                    IsTitle = i < 0,
                    Section = whole,
                    Folded = folded,
                    Entities = named,
                    Pictures = pictures.Where(p => Tied(p.Alt, p.Context, p.Entities, folded, foldedTitle, named)).Select(p => p.Picture).Take(8).ToList()
                });
            }
        }
        return (page, hits);
    }

    /// <summary>
    /// True when a picture goes with a paragraph: what surrounds the picture in the page (its context) holds the
    /// start of the paragraph or the title of its section; or its caption is in the paragraph or in that title (or the
    /// title in the caption); or the image model tied it to an entity the paragraph names.
    /// </summary>
    private static bool Tied(string alt, string context, List<string> pictureEntities, string folded, string foldedTitle, List<DisseminationEntity> named)
    {
        var start = folded.Length > 60 ? folded[..60] : folded;
        if (context.Length >= 20 && ((start.Length >= 20 && context.Contains(start, StringComparison.Ordinal)) || (foldedTitle.Length >= 12 && context.Contains(foldedTitle, StringComparison.Ordinal))))
            return true;
        if (alt.Length >= 12 && alt != "og:image" && (folded.Contains(alt, StringComparison.Ordinal) || foldedTitle.Contains(alt, StringComparison.Ordinal)
                                                     || (foldedTitle.Length >= 12 && alt.Contains(foldedTitle, StringComparison.Ordinal))))
            return true;
        return pictureEntities.Count > 0 && named.Any(e => e.FoldedNames.Any(pictureEntities.Contains));
    }

    // ── The searches ──

    /// <summary>ADD: keeps the search typed as a series of the chart and shows its results.</summary>
    [RelayCommand]
    private void AddSearch()
    {
        var text = QueryText.Trim();
        var (query, error) = DisseminationQuery.Parse(text);
        if (query is null)
        {
            IsError = true;
            Status = $"The search cannot be read: {error}. Example: Macron AND (Airbus OR \"von der Leyen\") AND NOT Lufthansa";
            return;
        }
        var written = query.ToString();
        if (Searches.FirstOrDefault(s => string.Equals(s.Text, written, StringComparison.OrdinalIgnoreCase)) is { } known)
        {
            Select(known);
            return;
        }
        // The search of everything gives way to the first search typed.
        if (written.Length > 0 && Searches.Count == 1 && Searches[0].Text.Length == 0)
            Searches.Clear();
        if (Searches.Count >= MostSearches)
        {
            IsError = true;
            Status = $"{MostSearches} searches at most: remove one (✕) first";
            return;
        }
        var color = Enumerable.Range(0, MostSearches).First(c => Searches.All(s => s.Color != c));
        var search = new DisseminationSearch { Text = written, Query = query, Color = color };
        Searches.Add(search);
        Selected = search;
        QueryText = string.Empty;
        ClearFilters(false);
        Recompute();
    }

    /// <summary>Adds a search from a click (an entity, a relation).</summary>
    public void Search(string text)
    {
        QueryText = text;
        AddSearch();
    }

    public void Select(DisseminationSearch search)
    {
        if (!Searches.Contains(search))
            return;
        Selected = search;
        Shown = PageSize;
        Recompute();
    }

    public void Remove(DisseminationSearch search)
    {
        Searches.Remove(search);
        if (Searches.Count == 0)
            Searches.Add(new DisseminationSearch());
        if (ReferenceEquals(Selected, search))
            Selected = Searches[0];
        Recompute();
    }

    /// <summary>A name written for a search: in quotation marks when it holds an operator, a parenthesis or several words.</summary>
    public static string Quoted(string name) => name.Contains(' ') || name.Any(c => c is '(' or ')') ? $"\"{name.Replace("\"", string.Empty)}\"" : name;

    // ── The period and the filters ──

    /// <summary>Applies the dates typed (yyyy-MM-dd); a date that cannot be read is told.</summary>
    public void ApplyDates()
    {
        if (!TryDay(DateFrom, out var from) || !TryDay(DateTo, out var to))
        {
            IsError = true;
            Status = "The dates are written yyyy-MM-dd (2026-11-21)";
            return;
        }
        if (from > to)
            (DateFrom, DateTo) = (Format(to), Format(from));
        ClearFilters(false);
        Recompute();
    }

    /// <summary>The last <paramref name="days"/> days up to the newest page; 0: the whole period of the pages of the plan.</summary>
    public void LastDays(int days)
    {
        FitDates();
        if (days > 0 && TryDay(DateTo, out var to))
            DateFrom = Format(to.AddDays(1 - days));
        ClearFilters(false);
        Recompute();
    }

    /// <summary>The period of the analysed pages of the plan (today when there is none).</summary>
    private void FitDates()
    {
        var days = _pages.Where(p => (PlanId.Length == 0 || p.PlanId == PlanId) && p.Day != default).Select(p => p.Day).ToList();
        var today = DateOnly.FromDateTime(DateTime.Today);
        DateFrom = Format(days.Count > 0 ? days.Min() : today);
        DateTo = Format(days.Count > 0 ? days.Max() : today);
    }

    public void ToggleDay(DateOnly day, DisseminationSearch? search = null)
    {
        if (search is not null && Searches.Contains(search))
            Selected = search;
        DayFilter = DayFilter == day && search is null ? null : day;
        Shown = PageSize;
        Recompute();
    }

    public void ToggleWebsite(string website)
    {
        WebsiteFilter = WebsiteFilter == website ? null : website;
        Shown = PageSize;
        Recompute();
    }

    public void ToggleEntity(string entity)
    {
        EntityFilter = EntityFilter == entity ? null : entity;
        Shown = PageSize;
        Recompute();
    }

    public void ClearFilters() => ClearFilters(true);

    private void ClearFilters(bool recompute)
    {
        DayFilter = null;
        WebsiteFilter = null;
        EntityFilter = null;
        Shown = PageSize;
        if (recompute)
            Recompute();
    }

    public void ShowMore()
    {
        Shown += PageSize;
        Changed?.Invoke();
    }

    // ── What is shown, computed ──

    private void Recompute()
    {
        if (Selected is null || !Searches.Contains(Selected))
            Selected = Searches[0];
        if (!TryDay(DateFrom, out var from) || !TryDay(DateTo, out var to))
            (from, to) = (DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
        if (from > to)
            (from, to) = (to, from);
        // A chart of a year at most: the bars of a longer period could not be told apart.
        if (to.DayNumber - from.DayNumber > 366)
            from = to.AddDays(-366);
        Days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

        var inScope = _all.Where(h => (PlanId.Length == 0 || h.Page.PlanId == PlanId) && h.Page.Day >= from && h.Page.Day <= to
                                      && (WebsiteFilter is null || h.Page.Website == WebsiteFilter)
                                      && (EntityFilter is null || h.Entities.Any(e => e.Name == EntityFilter))).ToList();
        Series = [];
        foreach (var search in Searches)
        {
            search.Hits = inScope.Where(search.Query.Matches).ToList();
            var counts = new int[Days.Count];
            foreach (var hit in search.Hits)
                counts[hit.Page.Day.DayNumber - from.DayNumber]++;
            Series.Add(counts);
        }

        // The valence per day, for the search selected.
        Valence = new double[Days.Count];
        var perDay = Selected.Hits.GroupBy(h => h.Page.Day.DayNumber - from.DayNumber).ToDictionary(g => g.Key, g => g.SelectMany(h => h.Entities).Where(e => e.HasEmotion).ToList());
        for (var i = 0; i < Days.Count; i++)
            Valence[i] = perDay.TryGetValue(i, out var named) && named.Count > 0 ? named.Average(e => e.Valence) : double.NaN;

        Results = Selected.Hits.Where(h => DayFilter is null || h.Page.Day == DayFilter)
            .OrderByDescending(h => h.Page.Day).ThenBy(h => h.Page.Website, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Page.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        PageCount = Results.Select(h => h.Page.Id).Distinct().Count();

        Websites = Results.GroupBy(h => h.Page.Website).Select(g => new DisseminationCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        TopEntities = Results.SelectMany(h => h.Entities.Select(e => (e.Name, e.Type)).Distinct()).GroupBy(e => e)
            .Select(g => new DisseminationCount(g.Key.Name, g.Count(), g.Key.Type))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(18).ToList();

        // The emotions about the entities named in the results: each mention counts once.
        var mentions = Results.SelectMany(h => h.Entities).Where(e => e.HasEmotion).ToList();
        Emotions = new double[EmotionNames.Length];
        MeanValence = mentions.Count > 0 ? mentions.Average(e => e.Valence) : double.NaN;
        if (mentions.Count > 0)
        {
            for (var i = 0; i < EmotionNames.Length; i++)
                Emotions[i] = mentions.Sum(e => e.Emotions.GetValueOrDefault(EmotionNames[i]));
            var sum = Emotions.Sum();
            if (sum > 0)
                for (var i = 0; i < Emotions.Length; i++)
                    Emotions[i] = Emotions[i] * 100 / sum;
        }

        // The relations stated by the pages of the results, between entities these results name.
        var names = Results.SelectMany(h => h.Entities.Select(e => e.Name)).ToHashSet(StringComparer.Ordinal);
        Relations = Results.Select(h => h.Page).Distinct().SelectMany(p => p.Relations.Select(r => (Page: p.Id, Relation: r)))
            .Where(r => names.Contains(r.Relation.Source) || names.Contains(r.Relation.Target))
            .GroupBy(r => (r.Relation.Source, r.Relation.Type, r.Relation.Target))
            .Select(g => new DisseminationRelationCount(g.Key.Source, g.Key.Type, g.Key.Target, g.Select(r => r.Page).Distinct().Count(), g.First().Relation.Evidence))
            .OrderByDescending(r => r.Pages).ThenBy(r => r.Source, StringComparer.OrdinalIgnoreCase).Take(14).ToList();

        IsError = false;
        var filters = new List<string>();
        if (DayFilter is { } day)
            filters.Add(day.ToString("yyyy-MM-dd"));
        if (WebsiteFilter is not null)
            filters.Add(WebsiteFilter);
        if (EntityFilter is not null)
            filters.Add(EntityFilter);
        Status = _pages.Count == 0
            ? "No page analysed yet: 🕷 RUN then 🧠 ANALYZE in /collectionplan"
            : $"{Results.Count} result(s) in {PageCount} page(s) for « {Selected.Label} »{(filters.Count > 0 ? " · filtered: " + string.Join(", ", filters) : string.Empty)} · {_pages.Count} page(s) analysed in all";
        Changed?.Invoke();
    }

    /// <summary>The pictures tied to the titles and paragraphs of the section of <paramref name="hit"/>, each once.</summary>
    public IReadOnlyList<DisseminationPicture> PicturesOfSection(DisseminationHit hit) =>
        _all.Where(h => ReferenceEquals(h.Page, hit.Page) && h.SectionTitle == hit.SectionTitle).SelectMany(h => h.Pictures).Distinct().ToList();

    /// <summary>
    /// The analysis of the picture at <paramref name="path"/> (a picture of a page, or the capture of a whole page),
    /// shown at its real size, or null when the file is not on this machine.
    /// </summary>
    public ImageAnalysisViewModel? OpenImage(string path)
    {
        if (_createAnalysis is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            IsError = true;
            Status = $"The picture is not on this machine: {path}";
            return null;
        }
        var analysis = _createAnalysis(path);
        analysis.OpenAtActualSize = true;
        return analysis;
    }

    private static bool TryDay(string text, out DateOnly day) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static string Format(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", string.Empty) : "(unknown)";

    private static string Text(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

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
