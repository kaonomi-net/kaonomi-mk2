using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using EveStyle = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Dashboards;

/// <summary>
/// /performance: the performance measurements of the agents (index performance), as /log shows the logs. Header: status
/// (busy, error dot), the period (last hour, 24 hours, 7 days, last 10,000), auto-refresh (15 s), REFRESH and SAVE (the
/// content of the window as JSON). Key figures: measures, operations, average, P95 (green, amber from 1 s, red from 5 s),
/// the slowest, the total time. The durations over time (average and max per slot), the operations (count, average,
/// P95, max, a bar of their share of the time; a click filters on one, a second click shows them all) and the slowest
/// measures. The table (date, duration coloured when slow, application, operation, comment, details; double-click shows
/// the whole measure) with the search, application and operation filters and Clear filters.
/// </summary>
[ToolView(typeof(PerformanceDashboardViewModel), "Performance dashboard", "⏱", 1400, 900)]
public sealed class PerformanceDashboardView : IToolView
{
    private static readonly Color Slow = Color.FromHtml("#F28C38");

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (PerformanceDashboardViewModel)viewModel;

        // Header.
        var status = Ui.Label(vm, nameof(vm.Status), EveStyle.Text, 12);
        status.ClipText = true;
        status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Bind.When(status, vm, () => status.AddThemeColorOverride("font_color", vm.IsError ? EveStyle.Red : EveStyle.Text), nameof(vm.IsError));
        var scopes = Bind.Options(new OptionButton { CustomMinimumSize = new Vector2(190, 0), TooltipText = "The measures read (GET /performance/since, /performance/last)" }, vm.Scopes, vm, nameof(vm.Scope), s => s.Label);
        var header = Ui.Toolbar(Ui.Row(8,
            DashUi.Dot(EveStyle.Red, vm, nameof(vm.IsError)),
            DashUi.Busy(vm.RefreshCommand),
            Ui.Fill(status),
            EveStyle.Caption("Period"),
            scopes,
            Ui.Check("Auto-refresh (15 s)", vm, nameof(vm.AutoRefresh)),
            Ui.Command("REFRESH", vm.RefreshCommand, primary: true),
            Ui.Action("SAVE", () => Save(vm), tooltip: "Saves the content of the window (figures, operations and the measures of the table) as JSON")));

        // Key figures.
        var figures = Ui.Grid(6, 8, 8);
        figures.AddChild(Ui.Fill(DashUi.Kpi(vm, "Measures", nameof(vm.CountText), nameof(vm.CountDetails))));
        figures.AddChild(Ui.Fill(DashUi.Kpi(vm, "Operations", nameof(vm.OperationsText), nameof(vm.OperationsDetails))));
        figures.AddChild(Ui.Fill(DashUi.Kpi(vm, "Average", nameof(vm.AverageText), nameof(vm.AverageDetails))));
        figures.AddChild(Ui.Fill(DashUi.Kpi(vm, "P95", nameof(vm.P95Text), nameof(vm.P95Details), colorProperty: nameof(vm.P95Brush))));
        figures.AddChild(Ui.Fill(DashUi.Kpi(vm, "Slowest", nameof(vm.SlowestText), nameof(vm.SlowestDetails))));
        figures.AddChild(Ui.Fill(DashUi.Kpi(vm, "Total time", nameof(vm.TotalText), nameof(vm.TotalDetails))));

        // Durations over time.
        var chart = UsageChart.Of(vm.AverageDurations, DashUi.Blue, PerformanceDashboardViewModel.Buckets, vm.MaxDurations, Slow, vm, nameof(vm.DurationMax));
        chart.SeriesName = "Average";
        chart.SecondaryName = "Max";
        chart.Format = v => PerformanceDashboardViewModel.Ms(v);
        chart.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var durations = DashUi.Card(Ui.Column(4,
                Ui.FillHeight(Ui.Row(6, Ui.Fill(chart), Top(Ui.Label(vm, nameof(vm.DurationMaxLabel), EveStyle.Muted, 10)))),
                Ui.Row(6, Ui.Fill(Ui.Label(vm, nameof(vm.DurationStart), EveStyle.Muted, 11)), Ui.Label(vm, nameof(vm.DurationEnd), EveStyle.Muted, 11))),
            vm, nameof(vm.DurationTitle), EveStyle.Text_("■ average", DashUi.Blue, 11), EveStyle.Text_("■ max", Slow, 11));
        durations.SizeFlagsStretchRatio = 2;

        // By operation.
        var operations = Ui.Column(2);
        Bind.Items(operations, vm.OperationStats, stat => OperationRow(vm, stat));
        var byOperation = DashUi.Card(Ui.FillHeight(Ui.Scroll(operations)), "By operation, the most time first (click to filter)");

        // The slowest.
        var slowest = Ui.Column(2);
        var none = Ui.Placeholder("No measure");
        Bind.Items(slowest, vm.Slowest, SlowRow, none);
        var slowCard = DashUi.Card(Ui.FillHeight(Ui.Column(0, Ui.FillHeight(Ui.Scroll(slowest)), none)), "The slowest measures");

        var middle = Ui.Row(8, Ui.Fill(durations, true), Ui.Fill(byOperation, true), Ui.Fill(slowCard, true));
        middle.CustomMinimumSize = new Vector2(0, 260);
        foreach (var card in new[] { durations, byOperation, slowCard })
            card.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        // Filters and table.
        var search = Ui.Line(vm, nameof(vm.Search), "Search in the operations, applications, comments and details…");
        var appFilter = Bind.Options(new OptionButton { CustomMinimumSize = new Vector2(260, 0), ClipText = true, FitToLongestItem = false }, vm.Apps, vm, nameof(vm.SelectedApp));
        var operationFilter = Bind.Options(new OptionButton { CustomMinimumSize = new Vector2(260, 0), ClipText = true, FitToLongestItem = false }, vm.Operations, vm, nameof(vm.SelectedOperation));
        var tableTitle = EveStyle.Caption("");
        Bind.Text(tableTitle, vm, nameof(vm.TableTitle), v => (v?.ToString() ?? string.Empty).ToUpperInvariant());
        tableTitle.CustomMinimumSize = new Vector2(170, 0);
        var table = new VirtualTable { EmptyText = "No measure", Tooltip = row => Describe((PerformanceRowViewModel)row) };
        table.Columns(
            new TableColumn("Date", 185, row => ((PerformanceRowViewModel)row).Date) { Mono = true },
            new TableColumn("Duration", 105, row => ((PerformanceRowViewModel)row).Duration)
            {
                Mono = true,
                Color = row => row is PerformanceRowViewModel { IsVerySlow: true } ? DashUi.ErrorRed.Lightened(0.2f) : row is PerformanceRowViewModel { IsSlow: true } ? Slow : null
            },
            new TableColumn("Application", 260, row => ((PerformanceRowViewModel)row).App),
            new TableColumn("Operation", 300, row => ((PerformanceRowViewModel)row).Operation),
            new TableColumn("Comment", 280, row => ((PerformanceRowViewModel)row).Comment),
            new TableColumn("Details", 0, row => ((PerformanceRowViewModel)row).Details) { Color = _ => EveStyle.Muted });
        table.CopyText = row => row is PerformanceRowViewModel p ? $"{p.Date}\t{p.Milliseconds}\t{p.App}\t{p.Operation}\t{p.Comment}\t{p.Details}" : string.Empty;
        table.Activated += row =>
        {
            if (row is PerformanceRowViewModel p)
                _ = Dialogs.Message($"{p.Operation} · {p.Duration}", Describe(p));
        };
        Bind.When(table, vm, () => table.SetRows(vm.Rows), nameof(vm.Rows));
        var measures = DashUi.Card(Ui.Column(8,
            Ui.Row(8, tableTitle, Ui.Fill(search), appFilter, operationFilter, Ui.Command("CLEAR FILTERS", vm.ClearFiltersCommand)),
            table));
        measures.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var page = Ui.Column(8, header, Ui.FillHeight(Ui.Margin(Ui.Column(8, figures, middle, Ui.FillHeight(measures)), 8, 0, 8, 8)));
        page.Ready += () => Bind.Run(vm.RefreshCommand);
        return page;
    }

    /// <summary>A measure in full: its operation, application, times, duration, comment and details.</summary>
    private static string Describe(PerformanceRowViewModel p)
    {
        var e = p.Entry;
        var details = (e.Metadatas ?? []).Where(m => m.Key.Length > 0).Select(m => $"  {m.Key}: {m.Value}");
        return $"{p.Operation}\n{p.App} · {e.UserName}@{e.MachineName}\n\n"
               + $"Start  {e.StartTime.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\nEnd    {e.EndTime.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\nDuration  {p.Duration} ({p.Milliseconds:N0} ms)"
               + (p.Comment.Length > 0 ? $"\n\n{p.Comment}" : string.Empty)
               + (details.Any() ? "\n\nDetails\n" + string.Join("\n", details) : string.Empty);
    }

    private static Label Top(Label label)
    {
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return label;
    }

    /// <summary>An operation: its name, its share of the time, its figures and a bar; a click filters on it.</summary>
    private static Control OperationRow(PerformanceDashboardViewModel vm, OperationStatViewModel stat)
    {
        var name = EveStyle.Text_(stat.Operation, stat.IsSlow ? Slow : EveStyle.Text, 11);
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var time = EveStyle.Text_(stat.TimeText, EveStyle.Muted, 10);
        var details = EveStyle.Text_(stat.Details, EveStyle.Muted, 10);
        details.ClipText = true;
        details.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return Row(Ui.Column(2, Ui.Row(6, name, time), details, DashUi.Share(stat.Share)), stat.Operation,
            () => Bind.Run(vm.FilterOperationCommand, stat),
            button => Bind.When(button, vm, () => button.SetPressedNoSignal(vm.SelectedOperation == stat.Operation), nameof(vm.SelectedOperation)));
    }

    /// <summary>One of the slowest measures: its duration, its operation, when; a click shows it in full.</summary>
    private static Control SlowRow(PerformanceRowViewModel p)
    {
        var duration = EveStyle.Text_(p.Duration, p.IsVerySlow ? DashUi.ErrorRed.Lightened(0.15f) : p.IsSlow ? Slow : EveStyle.Text, 12);
        duration.CustomMinimumSize = new Vector2(70, 0);
        var operation = EveStyle.Text_(p.Operation, EveStyle.Text, 11);
        operation.ClipText = true;
        operation.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var when = EveStyle.Text_($"{p.Entry.Date.ToLocalTime():dd/MM HH:mm:ss} · {p.App}", EveStyle.Muted, 10);
        when.ClipText = true;
        when.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return Row(Ui.Row(8, duration, Ui.Fill(Ui.Column(1, operation, when))), Describe(p),
            () => _ = Dialogs.Message($"{p.Operation} · {p.Duration}", Describe(p)));
    }

    /// <summary>A flat button row holding <paramref name="content"/>.</summary>
    private static Button Row(Control content, string tooltip, Action pressed, Action<Button>? bind = null)
    {
        var button = new Button { Flat = true, ToggleMode = bind is not null, TooltipText = tooltip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.AddThemeStyleboxOverride("pressed", EveStyle.Box(EveStyle.Cyan with { A = 0.16f }, EveStyle.Cyan with { A = 0.5f }, 4));
        button.AddThemeStyleboxOverride("hover", EveStyle.Box(EveStyle.Raised(0.05f), EveStyle.Edge with { A = 0 }, 4));
        button.AddThemeStyleboxOverride("normal", EveStyle.Box(new Color(0, 0, 0, 0), EveStyle.Edge with { A = 0 }, 4));
        var margin = Ui.Margin(content, 4, 3, 4, 3);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        Ignore(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(margin);
        margin.MinimumSizeChanged += () => button.CustomMinimumSize = new Vector2(0, margin.GetCombinedMinimumSize().Y);
        button.Ready += () => button.CustomMinimumSize = new Vector2(0, margin.GetCombinedMinimumSize().Y);
        button.Pressed += pressed;
        bind?.Invoke(button);
        return button;
    }

    private static void Ignore(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control control)
                control.MouseFilter = Control.MouseFilterEnum.Ignore;
            Ignore(child);
        }
    }

    /// <summary>SAVE: the content of the window to a JSON file chosen by the user.</summary>
    private static async void Save(PerformanceDashboardViewModel vm)
    {
        try
        {
            var path = await Dialogs.SaveFile("Save the performance dashboard", $"performance-dashboard-{DateTime.Now:yyyyMMdd-HHmmss}.json", ["*.json ; JSON"]);
            if (path is null)
                return;
            if (Path.GetExtension(path).Length == 0)
                path += ".json";
            await using (var stream = File.Create(path))
                await JsonSerializer.SerializeAsync(stream, vm.BuildExport(), new JsonSerializerOptions { WriteIndented = true });
            vm.Status = $"Saved to {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            vm.Status = $"Save failed: {ex.Message}";
        }
    }
}
