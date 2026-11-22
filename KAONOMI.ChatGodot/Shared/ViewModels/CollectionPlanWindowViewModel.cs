using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A page a site refused to the web scraper agent, with the plan it belongs to and what the site answered.</summary>
public sealed record RefusedPage(string PlanId, string PlanTitle, string Url, string Why);

/// <summary>
/// Search, creation, cloning, edition and deletion of the collection plans of the web scraper (<c>/collectionplan</c>,
/// <c>/ncollectionplan</c> in its own window). The search looks in every text field; an empty search lists the newest
/// plans (shown when the window opens).
/// </summary>
public partial class CollectionPlanWindowViewModel : ViewModelBase, INoteContextProvider, IProjectDocument
{
    /// <summary>The project of the plan selected (shown and chosen by its name in the header of the window).</summary>
    public string? ProjectId
    {
        get => SelectedResult?.ProjectId;
        set
        {
            if (SelectedResult is { } selected && selected.ProjectId != value)
            {
                selected.ProjectId = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasProjectDocument => SelectedResult is not null;

    partial void OnSelectedResultChanged(CollectionPlanEntryViewModel? value)
    {
        OnPropertyChanged(nameof(ProjectId));
        OnPropertyChanged(nameof(HasProjectDocument));
    }

    private const int PageSize = 50;

    private readonly KaonomiAssistantService _assistant;
    private readonly OllamaModelService? _ollama;
    private readonly string _visionModel;

    /// <summary>Design-time constructor using default server URL from settings.</summary>
    public CollectionPlanWindowViewModel() : this(new KaonomiAssistantService(ChatSettings.Load().ServerUrl), load: false)
    {
    }

    /// <summary>
    /// Uses the given assistant client for the collection plan API calls; lists the newest plans.
    /// <paramref name="ollama"/> and <paramref name="visionModel"/> are for the image analysis of a picture opened from
    /// 🗂 RESULTS.
    /// </summary>
    public CollectionPlanWindowViewModel(KaonomiAssistantService assistant, bool load = true, OllamaModelService? ollama = null,
        string visionModel = ChatSettings.DefaultVisionModel)
    {
        _assistant = assistant;
        _ollama = ollama;
        _visionModel = visionModel;
        RefusedPages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRefusedPages));
        if (load)
            _ = SearchAsync(CancellationToken.None);
    }

    /// <summary>Search hits and locally created rows bound to the results list.</summary>
    public ObservableCollection<CollectionPlanEntryViewModel> Results { get; } = [];

    /// <summary>True when at least one result row is present.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>Asks the user to confirm (message, confirm label); set by the view.</summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (message, label) => Confirmations.Ask(message, label);

    [ObservableProperty]
    private string _term = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand), nameof(DeleteCommand), nameof(RunCommand), nameof(AnalyzeCommand))]
    private CollectionPlanEntryViewModel? _selectedResult;

    /// <summary>True while the agent started by RUN collects the pages of the plan.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(AnalyzeCommand), nameof(CancelRunCommand))]
    private bool _isRunning;

    /// <summary>What RUN did: the steps of the agent, one line each (the pages collected, left out, failed; what was saved).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunLog))]
    private string _runLog = string.Empty;

    public bool HasRunLog => RunLog.Length > 0;

    // ── RUN: the agent KAONOMI.Agent.WebScraper.Collect on the selected plan ──

    /// <summary>The project (and the process) of the agent that collects the pages of a plan.</summary>
    public const string AgentProject = "KAONOMI.Agent.WebScraper.Collect";

    /// <summary>The project (and the process) of the agent that analyses the pages collected.</summary>
    public const string AnalyzeProject = "KAONOMI.Agent.WebScraper.Analyze";

    /// <summary>Where the whole output of the agent that collects goes (the window shows its steps only).</summary>
    public static string AgentLogFile => LogFileOf(AgentProject);

    /// <summary>The log of an agent of the web scraper: WebScraper.Collect.log, WebScraper.Analyze.log.</summary>
    public static string LogFileOf(string project) =>
        Path.Combine(ProcessWindowViewModel.LogsDirectory, (project.StartsWith("KAONOMI.Agent.", StringComparison.Ordinal) ? project["KAONOMI.Agent.".Length..] : project) + ".log");

    /// <summary>The agent started by RUN, or null.</summary>
    private Process? _agent;

    private bool CanRun() => SelectedResult is not null && !IsRunning;

    /// <summary>A line the agent writes on its console: « [19:23:15] ▸ … », its marker x for an error, ! for a warning.</summary>
    private static readonly Regex AgentLine = new(@"^\[\d\d:\d\d:\d\d\]\s*(?<mark>\S)\s+(?<text>.+)$", RegexOptions.Compiled);

    /// <summary>The colors of the console (ANSI escapes), taken off the lines of the agent.</summary>
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]|\x1B\][^\u0007]*\u0007", RegexOptions.Compiled);

    /// <summary>
    /// RUN: starts the agent KAONOMI.Agent.WebScraper.Collect on the selected plan (<c>--plan &lt;id&gt;</c>: one run,
    /// then it stops), as the plan is saved on the server, active or not. The agent collects each URL of its websites
    /// by its mode and saves each page in the search engine (index <c>page</c>): its content by titles and paragraphs,
    /// its entities (name and type, from the NER of the server), every URL it links to; a page ticked Main page is
    /// named by its title and the day (yyyyMMdd) and overwritten when collected again the same day. The picture of
    /// each page and its pictures go to Pictures/kaonomi/scraper/&lt;plan&gt;/&lt;website&gt;/&lt;yyyyMMdd&gt;. The
    /// steps of the agent are shown in the log of the window; its whole output goes to <see cref="AgentLogFile"/>.
    /// When pages were saved, the user is asked whether to analyse them now: on yes, the agent
    /// KAONOMI.Agent.WebScraper.Analyze is started on the plan (<c>--plan &lt;id&gt; --max &lt;pages saved&gt;</c>),
    /// its steps following in the same log (its output in WebScraper.Analyze.log).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var entry = SelectedResult!;
        IsError = false;
        if (entry.IsNew)
        {
            Fail("Save the plan first: the agent reads it as it is saved on the server");
            return;
        }
        if (entry.IsDirty && !await ConfirmAsync($"\"{entry.Title}\" has unsaved changes: the agent reads the plan as it is saved on the server. Run anyway?", "Run"))
            return;
        RunLog = string.Empty;
        RefusedPages.Clear();
        var planId = entry.Id!;
        var title = entry.Title;
        var summary = await RunAgentAsync(AgentProject, planId, title, $"▶ {AgentProject} on \"{title}\"", "--plan", planId);

        // The collection done: the analysis of what it saved is offered.
        var saved = summary is not null && SavedPages.Match(summary) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
        if (saved == 0)
            return;
        if (!await ConfirmAsync($"{saved} page(s) collected for \"{title}\". Launch the analysis of these pages now? "
                                + "The agent KAONOMI.Agent.WebScraper.Analyze settles their persons, organisations and events, puts them in the ontology "
                                + "(entities are created and completed there), builds their relations, analyses the emotions, the writing and the pictures. "
                                + "It takes from half a minute to several minutes a page.", "Analyse"))
        {
            Status = $"Done: {summary} · the analysis was not launched";
            return;
        }
        await RunAgentAsync(AnalyzeProject, planId, title, $"▶ {AnalyzeProject} on the pages of \"{title}\"", "--plan", planId, "--max", saved.ToString());
    }

    /// <summary>
    /// ANALYZE: starts the agent KAONOMI.Agent.WebScraper.Analyze on the selected plan (<c>--plan &lt;id&gt;</c>: one
    /// run, then it stops), without collecting first: the pages collected for the plan that are not analysed yet (or
    /// whose content changed since), a few per run (<c>maxPagesPerRun</c> of the agent). Its steps are shown in the
    /// log of the window; CANCEL stops it (the analyses saved stay).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task AnalyzeAsync()
    {
        var entry = SelectedResult!;
        IsError = false;
        if (entry.IsNew)
        {
            Fail("Save the plan and RUN it first: the analysis reads the pages collected for it");
            return;
        }
        RunLog = string.Empty;
        await RunAgentAsync(AnalyzeProject, entry.Id!, entry.Title, $"▶ {AnalyzeProject} on the pages of \"{entry.Title}\"", "--plan", entry.Id!);
    }

    /// <summary>How many pages the agent that collects saved, in the summary of its run: « Saved 3 page(s), … ».</summary>
    private static readonly Regex SavedPages = new(@"^Saved (\d+) page", RegexOptions.Compiled);

    /// <summary>
    /// Starts the agent with <paramref name="arguments"/> and shows its steps in the log until it ends; the summary of
    /// its run ("Saved …"), or null when it gave none. The pages a site refused are added to <see cref="RefusedPages"/>.
    /// </summary>
    private async Task<string?> RunAgentAsync(string project, string planId, string planTitle, string startLine, params string[] arguments)
    {
        var projects = ProcessWindowViewModel.FindProjects();
        if (!Directory.Exists(Path.Combine(projects, project)))
        {
            Fail($"The agent {project} was not found in {projects}");
            return null;
        }
        if (Process.GetProcessesByName(project).Length > 0)
        {
            Fail($"The agent {project} is already running (started from /process or a terminal): stop it first");
            return null;
        }
        var info = ProcessWindowViewModel.Project(projects, project, true, arguments);
        if (info is null)
        {
            Fail("dotnet was not found: install the .NET 8 SDK");
            return null;
        }
        // The agent writes its console in UTF-8 (the markers ▸ ✓ ·).
        info.StandardOutputEncoding = System.Text.Encoding.UTF8;
        info.StandardErrorEncoding = System.Text.Encoding.UTF8;

        var lines = new ConcurrentQueue<(bool Error, string Text)>();
        var errors = new List<string>();
        StreamWriter? log = null;
        var logPath = LogFileOf(project);
        string? summary = null;
        try
        {
            Directory.CreateDirectory(ProcessWindowViewModel.LogsDirectory);
            log = new StreamWriter(logPath, append: true) { AutoFlush = true };
            log.WriteLine($"──── {DateTime.Now:yyyy-MM-dd HH:mm:ss} · started by the collection plan window: {string.Join(' ', arguments)} ({planTitle}) ────");
            var writer = log;
            void Received(string? data)
            {
                if (data is null)
                    return;
                lock (writer)
                    writer.WriteLine(data);
                var clean = Ansi.Replace(data, string.Empty).Trim();
                if (AgentLine.Match(clean) is { Success: true } m)
                    lines.Enqueue((m.Groups["mark"].Value == "x", m.Groups["text"].Value.Trim()));
            }

            _agent = new Process { StartInfo = info, EnableRaisingEvents = true };
            _agent.OutputDataReceived += (_, e) => Received(e.Data);
            _agent.ErrorDataReceived += (_, e) => Received(e.Data);
            if (!_agent.Start())
                throw new InvalidOperationException("the process did not start");
            _agent.BeginOutputReadLine();
            _agent.BeginErrorReadLine();
            IsRunning = true;
            Status = $"Agent started on \"{planTitle}\" (building it the first time takes a moment)…";
            Log($"{startLine} (output in {logPath})");
            ActivityLog.Info("CollectionPlan", $"Agent {project} started on {planId} ({planTitle})");

            // The steps of the agent shown as they come; the window stays responsive.
            while (!_agent.HasExited)
            {
                await Task.Delay(400);
                summary = Drain(lines) ?? summary;
            }
            await _agent.WaitForExitAsync();
            summary = Drain(lines) ?? summary;

            var code = _agent.ExitCode;
            var nothingSaved = summary is null || summary.StartsWith("Saved 0 page", StringComparison.Ordinal) || summary.StartsWith("Analysed 0 page", StringComparison.Ordinal);
            IsError = !_cancelled && ((code != 0 && summary is null) || (errors.Count > 0 && nothingSaved));
            Status = _cancelled
                ? "Run cancelled (what the agent saved stays)"
                : (summary is not null
                      ? $"Done: {summary}{(project == AgentProject ? " (🗂 RESULTS shows them)" : " (index pageanalysis)")}"
                      : code == 0 ? "The agent stopped without a summary" : $"The agent ended with code {code} (see {logPath})")
                  + (errors.Count > 0 ? $" · {errors.Count} error(s): {errors[0]}" : string.Empty)
                  + (RefusedPages.Count > 0 ? $" · {RefusedPages.Count} page(s) refused by their site: see under the log" : string.Empty);
            Log((IsError ? "✕ " : "■ ") + Status);
            ActivityLog.Info("CollectionPlan", $"Agent {project} on {planId}: {Status}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Fail($"The agent could not be started: {ex.Message}");
        }
        finally
        {
            log?.Dispose();
            _agent?.Dispose();
            _agent = null;
            _cancelled = false;
            IsRunning = false;
        }
        return summary;

        // The new lines of the agent in the log (its errors kept); the summary of its run when it came ("Run done: Saved …").
        string? Drain(ConcurrentQueue<(bool Error, string Text)> queue)
        {
            string? done = null;
            while (queue.TryDequeue(out var line))
            {
                if (line.Text.StartsWith("Run done:", StringComparison.Ordinal))
                    done = line.Text["Run done:".Length..].Trim();
                if (line.Error)
                    errors.Add(line.Text);
                if (RefusedLine.Match(line.Text) is { Success: true } refused && RefusedPages.All(p => p.Url != refused.Groups["url"].Value))
                    RefusedPages.Add(new RefusedPage(planId, planTitle, refused.Groups["url"].Value, refused.Groups["why"].Value));
                Log((line.Error ? "✕ " : "· ") + line.Text);
                Status = line.Text;
            }
            return done;
        }
    }

    // ── The pages a site refused: opened and copied by the user, then kept (PASTE PAGE) ──

    /// <summary>What the agent says of a page its site refused: « Refused by the site (HTTP 403, …): &lt;url&gt; is not collected ».</summary>
    private static readonly Regex RefusedLine = new(@"^Refused by the site \((?<why>[^)]*)\): (?<url>\S+) is not collected$", RegexOptions.Compiled);

    /// <summary>
    /// The pages of the last RUN that their site refused to the agent (a protection against robots). The agent does not
    /// go round it; the user may open such a page in a browser (OPEN), copy it there, and PASTE PAGE keeps what was
    /// copied as the page collected.
    /// </summary>
    public ObservableCollection<RefusedPage> RefusedPages { get; } = [];

    public bool HasRefusedPages => RefusedPages.Count > 0;

    /// <summary>The plain text of the clipboard (set by the view), used when the clipboard holds no HTML.</summary>
    public Func<string?>? ClipboardText { get; set; }

    /// <summary>Reads the HTML of the clipboard (replaced in the tests).</summary>
    public Func<CancellationToken, Task<string?>> ReadClipboardHtmlAsync { get; set; } = ClipboardPage.ReadHtmlAsync;

    /// <summary>OPEN: the refused page in the browser of the user, as any reader opens it.</summary>
    [RelayCommand]
    private void OpenRefused(RefusedPage? page)
    {
        if (page is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(page.Url) { UseShellExecute = true });
            Status = $"{page.Url} opened in your browser: select the whole page (⌘A / Ctrl+A), copy it (⌘C / Ctrl+C), then PASTE PAGE";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Fail($"The page could not be opened: {ex.Message}");
        }
    }

    /// <summary>
    /// PASTE PAGE: keeps what the user copied in the browser (the HTML of the clipboard; its plain text when it holds
    /// no HTML) as the page of <paramref name="page"/>: the agent is started with <c>--import</c> on a file holding
    /// it, cuts it by titles and paragraphs, finds its entities, keeps its links, and saves it as a page it would have
    /// collected (a main page: named by its website and the day). Nothing is asked to the site: no rendering, and its
    /// pictures are listed but not downloaded.
    /// </summary>
    [RelayCommand]
    private async Task PasteRefusedAsync(RefusedPage? page)
    {
        if (page is null || IsRunning)
            return;
        IsError = false;
        var content = await ReadClipboardHtmlAsync(CancellationToken.None);
        var html = content is not null;
        content ??= ClipboardText?.Invoke();
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length < 200)
        {
            Fail("The clipboard holds no page: OPEN it, select the whole page in the browser (⌘A / Ctrl+A), copy it (⌘C / Ctrl+C), then PASTE PAGE");
            return;
        }
        var file = Path.Combine(Path.GetTempPath(), $"kaonomi-page-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(file, content);
            var summary = await RunAgentAsync(AgentProject, page.PlanId, page.PlanTitle,
                $"▶ Page copied by hand ({content.Length:N0} characters{(html ? string.Empty : ", plain text: no titles, no links")}) for {page.Url}",
                "--plan", page.PlanId, "--import", file, "--url", page.Url);
            if (summary is not null && summary.StartsWith("Saved 1 page", StringComparison.Ordinal))
                RefusedPages.Remove(page);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail($"The page copied could not be written for the agent: {ex.Message}");
        }
        finally
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>True once CANCEL stopped the agent of the run under way.</summary>
    private bool _cancelled;

    /// <summary>CANCEL: stops the agent started by RUN (the pages it saved stay).</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void CancelRun()
    {
        try
        {
            if (_agent is { HasExited: false } agent)
            {
                _cancelled = true;
                agent.Kill(entireProcessTree: true);
                Status = "Stopping the agent…";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Fail($"The agent could not be stopped: {ex.Message}");
        }
    }

    // ── RESULTS: what the agent saved for the selected plan ──

    /// <summary>
    /// The window of what the agent collected for <paramref name="entry"/> (its pages, their rendering, pictures, text
    /// and entities, read in the index <c>page</c> of the search engine); its pictures open in the image analysis at
    /// their real size.
    /// </summary>
    public WebScrapeResultsWindowViewModel CreateResults(CollectionPlanEntryViewModel entry) =>
        new(ChatSettings.Load().SearchEngineUrl, entry.Id ?? string.Empty, entry.Title,
            path => new ImageAnalysisViewModel(path, _assistant, _ollama, _visionModel));

    private void Log(string line) => RunLog = RunLog.Length == 0 ? line : RunLog + "\n" + line;

    private void Fail(string message)
    {
        IsError = true;
        Status = message;
        Log("✕ " + message);
    }

    [ObservableProperty]
    private string _status = "Loading the latest collection plans…";

    [ObservableProperty]
    private bool _isError;

    /// <summary>Inserts a new unsaved collection plan at the top of the list.</summary>
    [RelayCommand]
    private void New()
    {
        var entry = CollectionPlanEntryViewModel.CreateNew(_assistant);
        Results.Insert(0, entry);
        SelectedResult = entry;
        OnPropertyChanged(nameof(HasResults));
        ActivityLog.Info("CollectionPlan", "New collection plan started (unsaved)");
    }

    private bool CanClone() => SelectedResult is not null;

    /// <summary>Duplicates the selected collection plan as a new unsaved one.</summary>
    [RelayCommand(CanExecute = nameof(CanClone))]
    private void Clone()
    {
        var source = SelectedResult!;
        var clone = source.Clone();
        Results.Insert(Results.IndexOf(source) + 1, clone);
        SelectedResult = clone;
        clone.Status = "Unsaved clone";
        OnPropertyChanged(nameof(HasResults));
        ActivityLog.Info("CollectionPlan", $"Collection plan cloned from {source.Id ?? "(new)"} (unsaved)");
    }

    private bool CanDelete() => SelectedResult is not null;

    /// <summary>
    /// Deletes the selected collection plan after a confirmation: on the server it is only marked deleted
    /// (<c>isDeleted</c>), and it leaves the list; a new plan not saved yet is just removed from the list.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var entry = SelectedResult!;
        if (!await ConfirmAsync($"Delete the collection plan \"{entry.Title}\"?{(entry.IsNew ? "" : " It is only marked deleted on the server.")}", "Delete"))
            return;

        if (!entry.IsNew)
        {
            try
            {
                var result = await _assistant.DeleteCollectionPlanAsync(entry.Id!, cancellationToken);
                if (!result.IsSuccess)
                {
                    IsError = true;
                    Status = result.Message ?? "The Kaonomi server rejected the deletion.";
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                IsError = true;
                Status = $"Collection plan not deleted: {ex.Message}";
                ActivityLog.Error("CollectionPlan", $"Collection plan {entry.Id} not deleted", ex);
                return;
            }
        }

        var index = Results.IndexOf(entry);
        Results.Remove(entry);
        SelectedResult = Results.Count == 0 ? null : Results[Math.Min(index, Results.Count - 1)];
        OnPropertyChanged(nameof(HasResults));
        IsError = false;
        Status = $"Collection plan \"{entry.Title}\" deleted";
        ActivityLog.Info("CollectionPlan", $"Collection plan deleted (isDeleted = true): {entry.Id ?? "(unsaved)"} ({entry.Title})");
    }

    /// <summary>
    /// Searches the collection plans matching <see cref="Term"/> in all their text fields; an empty term (or <c>*</c>)
    /// lists the newest plans.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var term = Term.Trim();
        var isLast = term.Length == 0 || term == "*";
        IsError = false;
        Status = isLast ? "Loading the latest collection plans…" : $"Searching for \"{term}\"…";

        try
        {
            var entries = isLast
                ? await _assistant.GetLastCollectionPlansAsync(PageSize, cancellationToken)
                : await _assistant.SearchCollectionPlansAsync(term, PageSize, cancellationToken);

            Results.Clear();
            foreach (var entry in entries)
                Results.Add(new CollectionPlanEntryViewModel(entry, _assistant));

            SelectedResult = Results.FirstOrDefault();
            Status = isLast
                ? Results.Count == 0 ? "No collection plan yet: New creates one" : $"{Results.Count} latest collection plan(s)"
                : Results.Count == 0 ? $"No results for \"{term}\"" : $"{Results.Count} result(s) for \"{term}\"";
            ActivityLog.Info("CollectionPlan", isLast ? $"Latest collection plans: {Results.Count}" : $"Search \"{term}\": {Results.Count} result(s)");
        }
        catch (OperationCanceledException)
        {
            Status = "Search cancelled";
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            IsError = true;
            Status = $"Kaonomi server unreachable at {_assistant.BaseAddress} ({ex.Message})";
            ActivityLog.Error("CollectionPlan", $"Search \"{term}\" failed: server unreachable", ex);
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = ex.Message;
            ActivityLog.Error("CollectionPlan", $"Search \"{term}\" failed", ex);
        }
        finally
        {
            OnPropertyChanged(nameof(HasResults));
        }
    }

    /// <inheritdoc />
    public string NoteWindowName => "Collection plan";

    /// <inheritdoc />
    public object? GetNoteContext() => new
    {
        searchTerm = Term,
        resultCount = Results.Count,
        selected = SelectedResult?.ToEntry()
    };
}
