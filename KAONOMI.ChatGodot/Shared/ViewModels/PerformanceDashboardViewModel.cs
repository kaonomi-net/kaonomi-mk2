using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A measurement of the table of /performance.</summary>
public sealed class PerformanceRowViewModel(PerformanceEntry entry)
{
    public PerformanceEntry Entry { get; } = entry;
    public string Date { get; } = entry.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    public string App { get; } = entry.AppName ?? string.Empty;
    public string Operation { get; } = entry.OperationName ?? string.Empty;
    public int Milliseconds { get; } = entry.DurationMilliseconds;
    public string Duration { get; } = PerformanceDashboardViewModel.Ms(entry.DurationMilliseconds);
    public string Comment { get; } = entry.Comment ?? string.Empty;
    public string Details { get; } = string.Join(" · ", (entry.Metadatas ?? []).Where(m => m.Key.Length > 0).Select(m => $"{m.Key}={m.Value}"));

    /// <summary>Slow (a second or more), very slow (5 s or more).</summary>
    public bool IsSlow => Milliseconds >= 1000;
    public bool IsVerySlow => Milliseconds >= 5000;
}

/// <summary>An operation of the "By operation" panel: how many, average, P95 and max, a bar of its share of the total time.</summary>
public sealed class OperationStatViewModel(string operation, int count, double average, int p95, int max, double share, double totalShare)
{
    public string Operation { get; } = operation;
    public int Count { get; } = count;
    public double Average { get; } = average;
    public string Details { get; } = $"{count:N0}× · avg {PerformanceDashboardViewModel.Ms(average)} · p95 {PerformanceDashboardViewModel.Ms(p95)} · max {PerformanceDashboardViewModel.Ms(max)}";
    public string TimeText { get; } = $"{totalShare:0.#} % of the time";

    /// <summary>Width of the bar, 0–100 (the operation that took the most time = 100).</summary>
    public double Share { get; } = share;
    public bool IsSlow => average >= 1000;
}

/// <summary>
/// <c>/performance</c> (and <c>/perf</c>) window: dashboard of the performance measurements of the agents (index
/// <c>performance</c> of the server, <c>GET /performance/since</c>; last 24 hours, 7 days or 10,000 measurements on
/// demand), as <c>/log</c> shows the logs: key figures (measurements, operations, average, P95, the slowest, the total
/// time), the durations over time (average and max per slot), the operations (a click filters on one), the slowest
/// measurements and their table. The filters (text, application, operation) apply to the whole dashboard; it can
/// refresh itself every 15 seconds.
/// </summary>
public partial class PerformanceDashboardViewModel : ViewModelBase, INoteContextProvider, IDisposable
{
    /// <summary>Points of the graph.</summary>
    public const int Buckets = 60;

    private const string AllApps = "All applications";
    private const string AllOperations = "All operations";

    private readonly KaonomiAssistantService _assistant;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private List<PerformanceRowViewModel> _allRows = [];

    /// <summary>Design-time constructor.</summary>
    public PerformanceDashboardViewModel() : this(new KaonomiAssistantService(ChatSettings.Load().ServerUrl))
    {
    }

    /// <summary>Uses <paramref name="assistant"/> to read <c>GET /performance/since</c> and <c>/performance/last</c>.</summary>
    public PerformanceDashboardViewModel(KaonomiAssistantService assistant)
    {
        _assistant = assistant;
        _scope = Scopes[0];
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            Apply();
        };
        _refreshTimer.Tick += (_, _) =>
        {
            if (!RefreshCommand.IsRunning)
                RefreshCommand.Execute(null);
        };
    }

    /// <summary>Periods offered: the last hour (default), 24 hours, 7 days, or the last 10,000 measurements.</summary>
    public IReadOnlyList<LogScope> Scopes { get; } =
    [
        new("Last hour", 60, null), new("Last 24 hours", 24 * 60, null), new("Last 7 days", 7 * 24 * 60, null), new("Last 10,000 measures", null, 10000)
    ];

    public ObservableCollection<string> Apps { get; } = [AllApps];
    public ObservableCollection<string> Operations { get; } = [AllOperations];

    [ObservableProperty] private LogScope _scope = new("Last hour", 60, null);
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _selectedApp = AllApps;
    [ObservableProperty] private string _selectedOperation = AllOperations;
    [ObservableProperty] private bool _autoRefresh;
    [ObservableProperty] private string _status = "Loading…";
    [ObservableProperty] private bool _isError;

    // Key figures.
    [ObservableProperty] private string _countText = "–";
    [ObservableProperty] private string _countDetails = string.Empty;
    [ObservableProperty] private string _operationsText = "–";
    [ObservableProperty] private string _operationsDetails = string.Empty;
    [ObservableProperty] private string _averageText = "–";
    [ObservableProperty] private string _averageDetails = string.Empty;
    [ObservableProperty] private string _p95Text = "–";
    [ObservableProperty] private string _p95Details = string.Empty;
    [ObservableProperty] private IBrush _p95Brush = Brushes.Gray;
    [ObservableProperty] private string _slowestText = "–";
    [ObservableProperty] private string _slowestDetails = string.Empty;
    [ObservableProperty] private string _totalText = "–";
    [ObservableProperty] private string _totalDetails = string.Empty;

    // Graph.
    [ObservableProperty] private double _durationMax = 1;
    [ObservableProperty] private string _durationMaxLabel = string.Empty;
    [ObservableProperty] private string _durationStart = string.Empty;
    [ObservableProperty] private string _durationEnd = string.Empty;
    [ObservableProperty] private string _durationTitle = "Durations";

    [ObservableProperty] private string _tableTitle = "Measures";

    /// <summary>Average duration (ms) per time slot.</summary>
    public ObservableCollection<double> AverageDurations { get; } = [];

    /// <summary>Longest duration (ms) per time slot.</summary>
    public ObservableCollection<double> MaxDurations { get; } = [];

    public ObservableCollection<OperationStatViewModel> OperationStats { get; } = [];

    /// <summary>The slowest measurements (filters applied).</summary>
    public ObservableCollection<PerformanceRowViewModel> Slowest { get; } = [];

    /// <summary>Rows of the table (the filtered measurements, newest first).</summary>
    [ObservableProperty] private IReadOnlyList<PerformanceRowViewModel> _rows = [];

    partial void OnSearchChanged(string value)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    partial void OnSelectedAppChanged(string value) => Apply();

    partial void OnSelectedOperationChanged(string value) => Apply();

    partial void OnScopeChanged(LogScope value) => RefreshCommand.Execute(null);

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value)
            _refreshTimer.Start();
        else
            _refreshTimer.Stop();
    }

    /// <summary>Reads the measurements of the period from the server, then rebuilds the dashboard.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsError = false;
        Status = Scope.Minutes is { } loading ? $"Loading the measures of the {Scope.Label.ToLowerInvariant()}…" : $"Loading the last {Scope.Size:N0} measures…";
        try
        {
            var scope = Scope;
            var (rows, apps, operations) = await Task.Run(async () =>
            {
                var loaded = scope.Minutes is { } minutes
                    ? await _assistant.GetPerformancesSinceAsync(minutes, cancellationToken)
                    : await _assistant.GetLastPerformancesAsync(scope.Size ?? 10000, cancellationToken);
                var all = loaded.OrderByDescending(p => p.Date).Select(p => new PerformanceRowViewModel(p)).ToList();
                return (all, Names(all.Select(r => r.App)), Names(all.Select(r => r.Operation)));
            }, cancellationToken);
            _allRows = rows;
            Refill(Apps, AllApps, apps, SelectedApp, v => SelectedApp = v);
            Refill(Operations, AllOperations, operations, SelectedOperation, v => SelectedOperation = v);
            await ApplyAsync();
            Status = $"{_allRows.Count:N0} measures loaded at {DateTime.Now:HH:mm:ss}" + (AutoRefresh ? " · auto-refresh every 15 s" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "Loading cancelled";
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            IsError = true;
            Status = $"Kaonomi server unreachable at {_assistant.BaseAddress} ({ex.Message})";
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = $"Measures not loaded: {ex.Message}";
            ActivityLog.Error("Performance", "Performance dashboard: GET /performance failed", ex);
        }
    }

    private static List<string> Names(IEnumerable<string> values) =>
        values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    private static void Refill(ObservableCollection<string> list, string all, List<string> names, string selected, Action<string> select)
    {
        list.Clear();
        list.Add(all);
        foreach (var name in names)
            list.Add(name);
        select(names.Contains(selected) ? selected : all);
    }

    /// <summary>Clears the text, application and operation filters.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        Search = string.Empty;
        SelectedApp = AllApps;
        SelectedOperation = AllOperations;
    }

    /// <summary>Filters on one operation (click in "By operation"); a second click shows them all again.</summary>
    [RelayCommand]
    private void FilterOperation(OperationStatViewModel? stat)
    {
        if (stat is not null)
            SelectedOperation = SelectedOperation == stat.Operation ? AllOperations : stat.Operation;
    }

    private int _applyVersion;

    private void Apply() => _ = ApplyAsync();

    /// <summary>Rebuilds every panel from the loaded measurements and the filters (computed away from the UI thread).</summary>
    private async Task ApplyAsync()
    {
        var version = ++_applyVersion;
        var (all, scope, term, app, operation) = (_allRows, Scope.Label, Search.Trim(), SelectedApp, SelectedOperation);
        Figures figures;
        try
        {
            figures = await Task.Run(() => Compute(all, scope, term, app, operation));
        }
        catch (Exception ex)
        {
            ActivityLog.Error("Performance", "Performance dashboard not rebuilt", ex);
            return;
        }
        if (version == _applyVersion)
            Publish(figures);
    }

    private sealed record Figures(
        List<PerformanceRowViewModel> Rows, string CountText, string CountDetails, string OperationsText, string OperationsDetails,
        string AverageText, string AverageDetails, string P95Text, string P95Details, string P95Color, string SlowestText,
        string SlowestDetails, string TotalText, string TotalDetails, double[] Averages, double[] Maxima, double Max, string MaxLabel,
        string Start, string End, string Title, List<OperationStatViewModel> Operations, List<PerformanceRowViewModel> Slowest, string TableTitle);

    private static Figures Compute(List<PerformanceRowViewModel> all, string scope, string term, string app, string operation)
    {
        var rows = all.Where(r =>
                (app == AllApps || string.Equals(r.App, app, StringComparison.OrdinalIgnoreCase))
                && (operation == AllOperations || string.Equals(r.Operation, operation, StringComparison.OrdinalIgnoreCase))
                && (term.Length == 0
                    || r.Operation.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.App.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.Comment.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || r.Details.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var durations = rows.Select(r => r.Milliseconds).OrderBy(d => d).ToArray();
        var total = durations.Sum(d => (long)d);
        var average = durations.Length == 0 ? 0 : durations.Average();
        var p95 = Percentile(durations, 0.95);
        var median = Percentile(durations, 0.5);
        var slowest = rows.OrderByDescending(r => r.Milliseconds).FirstOrDefault();

        var byOperation = rows.GroupBy(r => r.Operation.Length > 0 ? r.Operation : "(none)")
            .Select(g =>
            {
                var sorted = g.Select(r => r.Milliseconds).OrderBy(d => d).ToArray();
                return (Operation: g.Key, Count: sorted.Length, Average: sorted.Average(), P95: Percentile(sorted, 0.95), Max: sorted[^1], Total: sorted.Sum(d => (long)d));
            })
            .OrderByDescending(o => o.Total).ToList();
        var mostTime = byOperation.Count == 0 ? 1 : Math.Max(1, byOperation[0].Total);
        var operations = byOperation.Take(30)
            .Select(o => new OperationStatViewModel(o.Operation, o.Count, o.Average, o.P95, o.Max, o.Total * 100.0 / mostTime, total == 0 ? 0 : o.Total * 100.0 / total))
            .ToList();

        var (averages, maxima, max, maxLabel, start, end, title) = BuildDurations(rows);
        return new Figures(
            rows,
            rows.Count.ToString("N0"),
            rows.Count == all.Count ? scope.ToLowerInvariant() : $"of {all.Count:N0} loaded (filtered)",
            byOperation.Count.ToString("N0"),
            byOperation.Count == 0 ? "no measure" : $"{rows.Select(r => r.App).Distinct().Count():N0} application(s)",
            durations.Length == 0 ? "–" : Ms(average),
            durations.Length == 0 ? "no measure" : $"median {Ms(median)}",
            durations.Length == 0 ? "–" : Ms(p95),
            "95 % of the measures are faster",
            p95 >= 5000 ? "#E81123" : p95 >= 1000 ? "#F28C38" : "#3DBB4A",
            slowest is null ? "–" : slowest.Duration,
            slowest is null ? "no measure" : $"{slowest.Operation} · {slowest.Entry.Date.ToLocalTime():dd/MM HH:mm:ss}",
            Ms(total),
            "the time of every measure added",
            averages, maxima, max, maxLabel, start, end, title,
            operations,
            rows.OrderByDescending(r => r.Milliseconds).Take(10).ToList(),
            rows.Count == all.Count ? $"Measures ({rows.Count:N0})" : $"Measures ({rows.Count:N0} of {all.Count:N0})");
    }

    private void Publish(Figures f)
    {
        CountText = f.CountText;
        CountDetails = f.CountDetails;
        OperationsText = f.OperationsText;
        OperationsDetails = f.OperationsDetails;
        AverageText = f.AverageText;
        AverageDetails = f.AverageDetails;
        P95Text = f.P95Text;
        P95Details = f.P95Details;
        P95Brush = new SolidColorBrush(Color.Parse(f.P95Color));
        SlowestText = f.SlowestText;
        SlowestDetails = f.SlowestDetails;
        TotalText = f.TotalText;
        TotalDetails = f.TotalDetails;

        AverageDurations.Clear();
        foreach (var value in f.Averages)
            AverageDurations.Add(value);
        MaxDurations.Clear();
        foreach (var value in f.Maxima)
            MaxDurations.Add(value);
        DurationMax = f.Max;
        DurationMaxLabel = f.MaxLabel;
        DurationStart = f.Start;
        DurationEnd = f.End;
        DurationTitle = f.Title;

        OperationStats.Clear();
        foreach (var stat in f.Operations)
            OperationStats.Add(stat);
        Slowest.Clear();
        foreach (var row in f.Slowest)
            Slowest.Add(row);
        Rows = f.Rows;
        TableTitle = f.TableTitle;
    }

    /// <summary>Average and longest duration per slot over the time span of the measurements (oldest on the left).</summary>
    private static (double[] Averages, double[] Maxima, double Max, string MaxLabel, string Start, string End, string Title)
        BuildDurations(List<PerformanceRowViewModel> rows)
    {
        if (rows.Count == 0)
            return ([], [], 1, string.Empty, string.Empty, string.Empty, "Durations");
        var oldest = rows[^1].Entry.Date;
        var newest = rows[0].Entry.Date;
        var slot = TimeSpan.FromTicks(Math.Max((newest - oldest).Ticks / Buckets, TimeSpan.TicksPerSecond));
        var sums = new double[Buckets];
        var counts = new int[Buckets];
        var maxima = new double[Buckets];
        foreach (var row in rows)
        {
            var index = (int)Math.Min(Buckets - 1, (row.Entry.Date - oldest).Ticks / slot.Ticks);
            sums[index] += row.Milliseconds;
            counts[index]++;
            maxima[index] = Math.Max(maxima[index], row.Milliseconds);
        }
        var averages = sums.Select((s, i) => counts[i] == 0 ? 0 : s / counts[i]).ToArray();
        var max = Math.Max(1, maxima.Max());
        return (averages, maxima, max, $"max {Ms(max)}", oldest.ToLocalTime().ToString("dd/MM HH:mm:ss"),
            newest.ToLocalTime().ToString("dd/MM HH:mm:ss"), $"Durations · {Buckets} slots of {Span(slot)}");
    }

    /// <summary>The value under which <paramref name="share"/> of the sorted <paramref name="sorted"/> are (nearest rank).</summary>
    private static int Percentile(int[] sorted, double share) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Length) - 1, 0, sorted.Length - 1)];

    /// <summary>A duration in milliseconds as it reads: 850 ms, 1.24 s, 2.5 min.</summary>
    public static string Ms(double milliseconds) =>
        milliseconds >= 60000 ? (milliseconds / 60000).ToString("0.#", CultureInfo.InvariantCulture) + " min"
        : milliseconds >= 1000 ? (milliseconds / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " s"
        : milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";

    private static string Span(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.TotalDays:0.#} d"
        : span.TotalHours >= 1 ? $"{span.TotalHours:0.#} h"
        : span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#} min"
        : $"{span.TotalSeconds:0.#} s";

    /// <inheritdoc />
    public string NoteWindowName => "Performance dashboard";

    /// <inheritdoc />
    public object? GetNoteContext() => new
    {
        scope = Scope.Label,
        filters = new { search = Search, application = SelectedApp, operation = SelectedOperation },
        figures = new { measures = CountText, operations = OperationsText, average = AverageText, p95 = P95Text, slowest = SlowestText, slowestDetails = SlowestDetails, total = TotalText },
        byOperation = OperationStats.Select(o => new { o.Operation, o.Count, o.Details }).ToArray()
    };

    /// <summary>Content of the window for SAVE: period, filters, figures, graph, operations, slowest and the measurements of the table.</summary>
    public object BuildExport() => new
    {
        window = NoteWindowName,
        generatedAt = DateTimeOffset.Now,
        machineName = Environment.MachineName,
        status = Status,
        period = Scope.Label,
        filters = new { search = Search, application = SelectedApp, operation = SelectedOperation },
        figures = new
        {
            measures = new { value = CountText, details = CountDetails },
            operations = new { value = OperationsText, details = OperationsDetails },
            average = new { value = AverageText, details = AverageDetails },
            p95 = new { value = P95Text, details = P95Details },
            slowest = new { value = SlowestText, details = SlowestDetails },
            total = new { value = TotalText, details = TotalDetails }
        },
        durations = new { title = DurationTitle, start = DurationStart, end = DurationEnd, maximum = DurationMaxLabel, average = AverageDurations.ToArray(), max = MaxDurations.ToArray() },
        byOperation = OperationStats.Select(o => new { operation = o.Operation, count = o.Count, details = o.Details, time = o.TimeText }).ToArray(),
        measures = Rows.Select(r => new
        {
            id = r.Entry.Id,
            date = r.Entry.Date,
            application = r.App,
            operation = r.Operation,
            durationMilliseconds = r.Milliseconds,
            comment = r.Comment,
            details = r.Details
        })
    };

    /// <summary>Stops the auto-refresh.</summary>
    public void Dispose()
    {
        _refreshTimer.Stop();
        _searchDelay.Stop();
    }
}
