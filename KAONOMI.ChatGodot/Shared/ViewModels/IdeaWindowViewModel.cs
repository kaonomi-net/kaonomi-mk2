using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// List, search, creation and edition of ideas (<c>/idea</c>, <c>/nidea</c> in its own window). ✨ NEW IDEAS asks the
/// model of the chat for 10 new ideas from the idea selected (prompt <c>idea.generate</c>, editable in /prompts): they
/// are added under it, unsaved, for the user to keep (SAVE, or 💾 SAVE NEW for all of them) or to delete. ⬇ EXPORT
/// ALL writes every idea of the server in a JSON file, ⬆ IMPORT ALL takes them back (by their id: no duplicates);
/// ⬇ EXPORT IDEA and ⬆ IMPORT IDEA do the same with one idea.
/// </summary>
public partial class IdeaWindowViewModel : ViewModelBase, INoteContextProvider, IProjectDocument
{
    /// <summary>The project of the idea selected (shown and chosen by its name in the header of the window).</summary>
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

    partial void OnSelectedResultChanged(IdeaEntryViewModel? value)
    {
        OnPropertyChanged(nameof(ProjectId));
        OnPropertyChanged(nameof(HasProjectDocument));
    }

    private const int PageSize = 100;

    private readonly KaonomiAssistantService _assistant;
    private readonly OllamaModelService? _llm;
    private readonly Func<string?>? _model;
    private readonly string _language;

    /// <summary>Design-time constructor using default server URL from settings.</summary>
    public IdeaWindowViewModel() : this(new KaonomiAssistantService(ChatSettings.Load().ServerUrl))
    {
    }

    /// <summary>
    /// Uses the given assistant client for the idea API calls; with <paramref name="llm"/> and <paramref name="model"/>
    /// (the model of the chat), ✨ NEW IDEAS writes new ideas, in the language of the idea (<paramref name="language"/>
    /// when it has no text).
    /// </summary>
    public IdeaWindowViewModel(KaonomiAssistantService assistant, OllamaModelService? llm = null, Func<string?>? model = null, string language = "English")
    {
        _assistant = assistant;
        _llm = llm;
        _model = model;
        _language = language;
    }

    /// <summary>Ideas of the last search (or the newest ones) and locally created rows.</summary>
    public ObservableCollection<IdeaEntryViewModel> Results { get; } = [];

    /// <summary>Categories of the listed ideas, offered by the category box.</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>True when at least one idea is listed.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>Asks the user to confirm (message, confirm label); set by the view.</summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (message, label) => Confirmations.Ask(message, label);

    [ObservableProperty]
    private string _term = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(GenerateIdeasCommand), nameof(ExportIdeaCommand))]
    private IdeaEntryViewModel? _selectedResult;

    [ObservableProperty]
    private string _status = "Loading the latest ideas…";

    [ObservableProperty]
    private bool _isError;

    /// <summary>Inserts a new unsaved idea at the top of the list.</summary>
    [RelayCommand]
    private void New()
    {
        var entry = IdeaEntryViewModel.CreateNew(_assistant);
        entry.Saved += OnSaved;
        Results.Insert(0, entry);
        SelectedResult = entry;
        OnPropertyChanged(nameof(HasResults));
        ActivityLog.Info("Idea", "New idea started (unsaved)");
    }

    /// <summary>
    /// The ideas not saved yet that a search does not take away: the one filled from a message of /message, and those
    /// written by the model (✨ NEW IDEAS).
    /// </summary>
    private List<IdeaEntryViewModel> _pending = [];

    /// <summary>The idea filled from a message: a new one from a message replaces it while it is not saved.</summary>
    private IdeaEntryViewModel? _messageIdea;

    /// <summary>
    /// Inserts a new unsaved idea filled from elsewhere (a message of /message): its title, its description, a comment
    /// that says where it comes from, its tags (one per name). Nothing is saved before SAVE; it stays at the top of
    /// the list through the searches until it is saved or deleted.
    /// </summary>
    public void NewIdeaFrom(string title, string description, string comment, IEnumerable<string> tags)
    {
        var entry = IdeaEntryViewModel.CreateNew(_assistant);
        entry.IdeaTitle = title.Trim();
        entry.IdeaDescription = description.Trim();
        entry.Comment = comment.Trim();
        entry.Tags = string.Join('\n', MetaTags.Normalize(tags, MetaTags.Tag));
        entry.Saved += OnSaved;
        if (_messageIdea is { IsNew: true } before && Results.Remove(before))
        {
            before.Saved -= OnSaved;
            _pending.Remove(before);
        }
        _messageIdea = entry;
        _pending.Add(entry);
        Results.Insert(0, entry);
        SelectedResult = entry;
        OnPropertyChanged(nameof(HasResults));
        IsError = false;
        Status = $"New idea \"{entry.DisplayTitle}\": check it, then SAVE.";
        ActivityLog.Info("Idea", "New idea filled from a message (unsaved)");
    }

    /// <summary>
    /// Searches the ideas matching <see cref="Term"/> on title, description, category and tags; an empty term lists
    /// the newest ideas.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var term = Term.Trim();
        var isLast = term.Length == 0 || term == "*";
        IsError = false;
        Status = isLast ? "Loading the latest ideas…" : $"Searching for \"{term}\"…";

        try
        {
            var entries = isLast
                ? await _assistant.GetLastIdeasAsync(PageSize, cancellationToken)
                : await _assistant.SearchIdeasAsync(term, PageSize, cancellationToken);

            // The ideas not saved yet (filled from a message, written by the model) stay, at the top.
            var kept = _pending.Where(p => p.IsNew && Results.Contains(p)).ToList();
            var keptSelected = SelectedResult is { } current && kept.Contains(current) ? current : null;
            _pending = kept;
            foreach (var row in Results)
                row.Saved -= OnSaved;
            Results.Clear();
            foreach (var entry in entries)
            {
                var row = new IdeaEntryViewModel(entry, _assistant);
                row.Saved += OnSaved;
                Results.Add(row);
            }

            for (var i = 0; i < kept.Count; i++)
            {
                kept[i].Saved += OnSaved;
                Results.Insert(i, kept[i]);
            }
            RefreshCategories();
            SelectedResult = keptSelected ?? Results.FirstOrDefault(r => !kept.Contains(r)) ?? kept.FirstOrDefault();
            NotifyNew();
            Status = isLast
                ? $"{Results.Count} latest idea(s)"
                : Results.Count == 0 ? $"No results for \"{term}\"" : $"{Results.Count} result(s) for \"{term}\"";
            ActivityLog.Info("Idea", isLast ? $"Latest ideas: {Results.Count}" : $"Search \"{term}\": {Results.Count} result(s)");
        }
        catch (OperationCanceledException)
        {
            Status = "Search cancelled";
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            IsError = true;
            Status = $"Kaonomi server unreachable at {_assistant.BaseAddress} ({ex.Message})";
            ActivityLog.Error("Idea", "Idea search failed: server unreachable", ex);
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = ex.Message;
            ActivityLog.Error("Idea", "Idea search failed", ex);
        }
        finally
        {
            OnPropertyChanged(nameof(HasResults));
        }
    }

    private bool CanDelete() => SelectedResult is not null;

    /// <summary>Deletes the selected idea on the server after confirmation (an unsaved idea is only removed from the list).</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var idea = SelectedResult!;
        if (!await ConfirmAsync($"Delete the idea \"{idea.DisplayTitle}\"?\n\nIt is removed from the server and cannot be restored.", "Delete"))
            return;

        if (!idea.IsNew)
        {
            try
            {
                var result = await _assistant.DeleteIdeaAsync(idea.Id!, cancellationToken);
                if (!result.IsSuccess)
                {
                    IsError = true;
                    Status = result.Message ?? "The Kaonomi server rejected the deletion.";
                    ActivityLog.Error("Idea", $"Delete idea {idea.Id} rejected: {Status}");
                    return;
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null)
            {
                IsError = true;
                Status = $"Kaonomi server unreachable at {_assistant.BaseAddress} ({ex.Message})";
                ActivityLog.Error("Idea", "Idea not deleted: server unreachable", ex);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                IsError = true;
                Status = ex.Message;
                ActivityLog.Error("Idea", "Idea not deleted", ex);
                return;
            }
        }

        var index = Results.IndexOf(idea);
        _pending.Remove(idea);
        if (ReferenceEquals(_messageIdea, idea))
            _messageIdea = null;
        idea.Saved -= OnSaved;
        Results.Remove(idea);
        SelectedResult = Results.Count == 0 ? null : Results[Math.Min(index, Results.Count - 1)];
        RefreshCategories();
        OnPropertyChanged(nameof(HasResults));
        IsError = false;
        Status = $"Idea \"{idea.DisplayTitle}\" deleted";
        NotifyNew();
        ActivityLog.Info("Idea", $"Idea deleted: {idea.Id ?? "(new)"}");
    }

    private void OnSaved(IdeaEntryViewModel idea)
    {
        RefreshCategories();
        NotifyNew();
    }

    // ── ✨ NEW IDEAS: new ideas written by the model from the idea selected ──

    /// <summary>How many ideas ✨ NEW IDEAS asks for.</summary>
    public const int NewIdeasCount = 10;

    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromMinutes(4);

    /// <summary>The shape of the answer, added to the prompt whatever /prompts made of it.</summary>
    private const string IdeasShape =
        "Answer with one JSON object only, nothing before or after it, of this shape: "
        + "{\"ideas\": [{\"title\": \"...\", \"description\": \"...\", \"tags\": [\"...\"]}]}";

    /// <summary>True while the model writes the new ideas.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateIdeasCommand))]
    private bool _isGenerating;

    /// <summary>How many ideas of the list are not saved yet (written by the model, or filled from a message).</summary>
    public int NewCount => _pending.Count(p => p.IsNew && Results.Contains(p));

    public bool HasNew => NewCount > 0;

    public string SaveNewLabel => $"💾 SAVE NEW ({NewCount})";

    private void NotifyNew()
    {
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(HasNew));
        OnPropertyChanged(nameof(SaveNewLabel));
    }

    private bool CanGenerate() => SelectedResult is not null && !IsGenerating;

    /// <summary>
    /// ✨ NEW IDEAS: the model of the chat writes <see cref="NewIdeasCount"/> new ideas from the idea selected (as it is
    /// in the editor, saved or not): each with its title, description and tags, the category and the project of the idea,
    /// and a comment saying where it comes from. They are added under it, not saved: SAVE keeps one, 💾 SAVE NEW all of
    /// them, 🗑 takes one away; a search does not take them away.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateIdeasAsync(CancellationToken cancellationToken)
    {
        var source = SelectedResult!;
        if (string.IsNullOrWhiteSpace(source.IdeaTitle) && string.IsNullOrWhiteSpace(source.IdeaDescription))
        {
            IsError = true;
            Status = "Write the idea first (a title or a description): the new ideas are made from it";
            return;
        }
        var model = _model?.Invoke();
        if (_llm is null || string.IsNullOrWhiteSpace(model))
        {
            IsError = true;
            Status = "No model to write the ideas: choose one in the chat";
            return;
        }

        IsGenerating = true;
        IsError = false;
        Status = $"{model} imagines {NewIdeasCount} new ideas from « {source.DisplayTitle} »…";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GenerateTimeout);
            var tags = SplitLines(source.Tags);
            var idea = $"Title: {source.IdeaTitle.Trim()}\n"
                       + (source.Category.Trim().Length > 0 ? $"Category: {source.Category.Trim()}\n" : string.Empty)
                       + (tags.Count > 0 ? $"Tags: {string.Join(", ", tags)}\n" : string.Empty)
                       + $"Description:\n{source.IdeaDescription.Trim()}";
            using var answer = await _llm.ChatForJsonAsync(model,
                Prompts.Get("idea.generate", ("count", NewIdeasCount), ("language", _language)) + "\n\n" + IdeasShape, idea, timeout.Token);

            var written = new List<(string Title, string Description, List<string> Tags)>();
            if (answer.RootElement.TryGetProperty("ideas", out var ideas) && ideas.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in ideas.EnumerateArray().Where(i => i.ValueKind == System.Text.Json.JsonValueKind.Object))
                {
                    var title = Clean(Text(item, "title"));
                    if (title.Length == 0 || written.Any(w => string.Equals(w.Title, title, StringComparison.OrdinalIgnoreCase))
                        || string.Equals(title, source.IdeaTitle.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    var itemTags = item.TryGetProperty("tags", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? t.EnumerateArray().Where(v => v.ValueKind == System.Text.Json.JsonValueKind.String).Select(v => Clean(v.GetString())).Where(v => v.Length > 0).ToList()
                        : Clean(Text(item, "tags")).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    written.Add((title, Clean(Text(item, "description")), itemTags));
                }
            if (written.Count == 0)
            {
                IsError = true;
                Status = $"{model} gave no idea: try again";
                return;
            }

            // Under the idea, in the order the model gave them.
            var at = Results.IndexOf(source);
            var comment = $"Written by {model} from the idea « {source.DisplayTitle} »" + (source.Id is { } id ? $" ({id})" : string.Empty);
            foreach (var (title, description, itemTags) in written.Take(NewIdeasCount))
            {
                var entry = IdeaEntryViewModel.CreateNew(_assistant);
                entry.IdeaTitle = title;
                entry.IdeaDescription = description;
                entry.Category = source.Category.Trim();
                entry.Tags = string.Join('\n', MetaTags.Normalize(tags.Concat(itemTags), MetaTags.Tag));
                entry.Comment = comment;
                entry.ProjectId = source.ProjectId;
                entry.Status = $"Written by {model}: SAVE to keep it";
                entry.Saved += OnSaved;
                _pending.Add(entry);
                Results.Insert(++at, entry);
            }
            OnPropertyChanged(nameof(HasResults));
            NotifyNew();
            var count = Math.Min(written.Count, NewIdeasCount);
            Status = $"{count} new idea(s) from « {source.DisplayTitle} », under it, not saved: SAVE the ones to keep, or 💾 SAVE NEW for all of them";
            ActivityLog.Info("Idea", $"{count} new ideas written by {model} from {source.Id ?? "(new idea)"}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            IsError = true;
            Status = $"{model} gave no answer within {GenerateTimeout.TotalMinutes:0} minutes";
        }
        catch (OperationCanceledException)
        {
            Status = "New ideas cancelled";
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {
            IsError = true;
            Status = $"The new ideas could not be written: {ex.Message}";
            ActivityLog.Error("Idea", "New ideas not written", ex);
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>💾 SAVE NEW: saves every idea of the list not saved yet (written by the model, or filled from a message).</summary>
    [RelayCommand]
    private async Task SaveNewAsync(CancellationToken cancellationToken)
    {
        var drafts = _pending.Where(p => p.IsNew && Results.Contains(p)).ToList();
        if (drafts.Count == 0)
            return;
        IsError = false;
        var saved = 0;
        for (var i = 0; i < drafts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Status = $"Saving the new ideas: {i + 1} of {drafts.Count}…";
            if (drafts[i].SaveCommand.CanExecute(null))
                await drafts[i].SaveCommand.ExecuteAsync(null);
            if (!drafts[i].IsNew)
                saved++;
        }
        NotifyNew();
        IsError = saved < drafts.Count;
        Status = saved == drafts.Count
            ? $"{saved} new idea(s) saved"
            : $"{saved} of {drafts.Count} new idea(s) saved: the others tell why in their status";
    }

    private static List<string> SplitLines(string text) =>
        (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Text(System.Text.Json.JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>A text of the model without the characters the fonts of the chat do not draw (its non-breaking hyphens, narrow spaces).</summary>
    private static string Clean(string? text) =>
        (text ?? string.Empty).Replace('\u2011', '-').Replace('\u2010', '-').Replace('\u202F', '\u00A0').Replace('\u2009', ' ').Trim();

    // ── ⬇ EXPORT / ⬆ IMPORT: every idea of the server in a JSON file, and back ──

    /// <summary>The most ideas read from the server at once (the limit of the search engine).</summary>
    public const int ExportLimit = 10000;

    /// <summary>What a file of ideas says it is (<c>"kind"</c>).</summary>
    public const string ExportKind = "kaonomi-ideas";

    private static readonly System.Text.Json.JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Asks where to write a file (title, suggested name); set by the view. Null: given up.</summary>
    public Func<string, string, Task<string?>> SaveFileAsync { get; set; } = (_, _) => Task.FromResult<string?>(null);

    /// <summary>Asks for a file to read (title); set by the view. Null: given up.</summary>
    public Func<string, Task<string?>> OpenFileAsync { get; set; } = _ => Task.FromResult<string?>(null);

    /// <summary>The file of ideas: what it is, when and where it was written, the ideas as the server keeps them.</summary>
    public sealed class IdeaFile
    {
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; set; } = ExportKind;

        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [System.Text.Json.Serialization.JsonPropertyName("exported")]
        public DateTimeOffset Exported { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("machineName")]
        public string? MachineName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("count")]
        public int Count { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("ideas")]
        public List<IdeaEntry> Ideas { get; set; } = [];
    }

    /// <summary>
    /// ⬇ EXPORT ALL: every idea of the server (not only those listed), newest first, in a JSON file chosen by the user. The
    /// ideas not saved yet are not in it.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        var path = await SaveFileAsync("Export every idea as JSON", $"ideas-{DateTime.Now:yyyy-MM-dd}.json");
        if (string.IsNullOrWhiteSpace(path))
            return;
        IsError = false;
        Status = "Reading every idea of the server…";
        try
        {
            // The ideas deleted (marked, never erased on the server) are left out.
            var ideas = (await _assistant.GetLastIdeasAsync(ExportLimit, cancellationToken)).Where(i => i.IsDeleted != true).ToList();
            var file = new IdeaFile { Exported = DateTimeOffset.Now, MachineName = Environment.MachineName, Count = ideas.Count, Ideas = ideas };
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(file, ExportJson), cancellationToken);
            var unsaved = NewCount;
            Status = $"{ideas.Count} idea(s) exported in {path}"
                     + (ideas.Count >= ExportLimit ? $" (the first {ExportLimit}: the limit of the search engine)" : string.Empty)
                     + (unsaved > 0 ? $"; {unsaved} not saved yet, not in it" : string.Empty);
            ActivityLog.Info("Idea", $"{ideas.Count} idea(s) exported in {path}");
        }
        catch (OperationCanceledException)
        {
            Status = "Export cancelled";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            IsError = true;
            Status = ex is HttpRequestException { StatusCode: null }
                ? $"Kaonomi server unreachable at {_assistant.BaseAddress} ({ex.Message})"
                : $"The ideas were not exported: {ex.Message}";
            ActivityLog.Error("Idea", "Ideas not exported", ex);
        }
    }

    /// <summary>
    /// ⬆ IMPORT ALL: the ideas of a JSON file (one written by ⬇ EXPORT ALL, or a bare array of ideas) saved on the server, after
    /// a confirmation that says how many are new (an idea deleted since the export among them: it comes back), how many
    /// replace the idea of the same id (changed) and how many are already there as they are (left alone). The ids are
    /// kept: importing a file twice makes no duplicate.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (await AskIdeaFileAsync("Import ideas from a JSON file", cancellationToken) is { } file)
            await ImportIdeasAsync(file.Path, file.Ideas, single: false, cancellationToken);
    }

    /// <summary>
    /// ⬇ EXPORT IDEA: the idea selected, as it is in the editor (saved or not), in a JSON file of one idea (read by
    /// ⬆ IMPORT IDEA as by ⬆ IMPORT ALL).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task ExportIdeaAsync(CancellationToken cancellationToken)
    {
        if (SelectedResult is not { } selected)
            return;
        var path = await SaveFileAsync($"Export the idea « {selected.DisplayTitle} » as JSON", $"idea-{FileSlug(selected.IdeaTitle)}.json");
        if (string.IsNullOrWhiteSpace(path))
            return;
        IsError = false;
        try
        {
            var idea = selected.ToEntry();
            var file = new IdeaFile { Exported = DateTimeOffset.Now, MachineName = Environment.MachineName, Count = 1, Ideas = [idea] };
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(file, ExportJson), cancellationToken);
            Status = $"Idea « {selected.DisplayTitle} » exported in {path}"
                     + (selected.IsNew ? " (not saved yet: it gets an id when imported)" : selected.IsDirty ? " (with its changes not saved)" : string.Empty);
            ActivityLog.Info("Idea", $"Idea {selected.Id ?? "(new)"} exported in {path}");
        }
        catch (OperationCanceledException)
        {
            Status = "Export cancelled";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsError = true;
            Status = $"The idea was not exported: {ex.Message}";
        }
    }

    /// <summary>
    /// ⬆ IMPORT IDEA: the idea of a JSON file of one idea (written by ⬇ EXPORT IDEA, or an idea alone) saved on the
    /// server after a confirmation: a new one, or the one of the same id replaced when it changed (nothing when it is
    /// there as it is). It is selected in the list afterwards. A file of several ideas goes through ⬆ IMPORT ALL.
    /// </summary>
    [RelayCommand]
    private async Task ImportIdeaAsync(CancellationToken cancellationToken)
    {
        if (await AskIdeaFileAsync("Import one idea from a JSON file", cancellationToken) is not { } file)
            return;
        if (file.Ideas.Count > 1)
        {
            IsError = true;
            Status = $"{Path.GetFileName(file.Path)} holds {file.Ideas.Count} ideas: ⬆ IMPORT ALL takes them";
            return;
        }
        await ImportIdeasAsync(file.Path, file.Ideas, single: true, cancellationToken);
    }

    /// <summary>Asks for a file of ideas and reads it; null (the reason in the status) when given up, unreadable or empty.</summary>
    private async Task<(string Path, List<IdeaEntry> Ideas)?> AskIdeaFileAsync(string title, CancellationToken cancellationToken)
    {
        var path = await OpenFileAsync(title);
        if (string.IsNullOrWhiteSpace(path))
            return null;
        IsError = false;
        List<IdeaEntry> ideas;
        try
        {
            ideas = ReadIdeaFile(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            IsError = true;
            Status = $"The file was not read: {ex.Message}";
            return null;
        }
        if (ideas.Count == 0)
        {
            IsError = true;
            Status = $"No idea in {Path.GetFileName(path)}";
            return null;
        }
        return (path, ideas);
    }

    /// <summary>A name for a file from a title: letters, digits and '-', at most 40 characters.</summary>
    private static string FileSlug(string? title)
    {
        var chars = (title ?? string.Empty).Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? DateTime.Now.ToString("yyyy-MM-dd") : slug[..Math.Min(40, slug.Length)].TrimEnd('-');
    }

    /// <summary>
    /// Saves <paramref name="ideas"/> (read from <paramref name="path"/>) on the server after a confirmation: the new
    /// ones created, the ones of an id already there replaced when they changed, the others left alone. With
    /// <paramref name="single"/>, the confirmation names the idea and it is selected afterwards.
    /// </summary>
    private async Task ImportIdeasAsync(string path, List<IdeaEntry> ideas, bool single, CancellationToken cancellationToken)
    {
        try
        {
            Status = "Comparing with the ideas of the server…";
            var existing = (await _assistant.GetLastIdeasAsync(ExportLimit, cancellationToken))
                .Where(i => !string.IsNullOrEmpty(i.Id) && i.IsDeleted != true)
                .GroupBy(i => i.Id!)
                .ToDictionary(g => g.Key, g => g.First());
            var added = new List<IdeaEntry>();
            var replaced = new List<IdeaEntry>();
            var same = 0;
            foreach (var idea in ideas)
            {
                if (idea.Id is { Length: > 0 } id && existing.TryGetValue(id, out var current))
                {
                    if (SameIdea(idea, current))
                        same++;
                    else
                        replaced.Add(idea);
                }
                else
                    added.Add(idea);
            }
            if (added.Count + replaced.Count == 0)
            {
                if (single)
                    SelectedResult = Results.FirstOrDefault(r => r.Id == ideas[0].Id) ?? SelectedResult;
                Status = single
                    ? $"The idea « {ideas[0].IdeaTitle} » is already on the server as it is: nothing to import"
                    : $"The {ideas.Count} idea(s) of {Path.GetFileName(path)} are already on the server as they are: nothing to import";
                return;
            }
            var message = single
                ? added.Count == 1
                    ? $"Import the idea « {ideas[0].IdeaTitle} » from {Path.GetFileName(path)}?\n\nIt is new: it is saved on the server."
                    : $"Import the idea « {ideas[0].IdeaTitle} » from {Path.GetFileName(path)}?\n\nIt replaces the idea of the same id on the server, changed since: those changes are lost."
                : $"Import {ideas.Count} idea(s) from {Path.GetFileName(path)}?\n\n"
                  + $"• {added.Count} new\n"
                  + $"• {replaced.Count} replace the idea of the same id on the server (changed)\n"
                  + $"• {same} already there as they are (left alone)";
            if (replaced.Count > 0 && !single)
                message += "\n\nThe ideas replaced lose their changes made since the export.";
            if (!await ConfirmAsync(message, "Import"))
                return;

            var saved = 0;
            string? lastId = null;
            var failed = new List<string>();
            var toSave = added.Concat(replaced).ToList();
            for (var i = 0; i < toSave.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var idea = toSave[i];
                Status = $"Importing the ideas: {i + 1} of {toSave.Count}…";
                // The same title for every document (kept in step on save, like SAVE does).
                if (string.IsNullOrWhiteSpace(idea.Title))
                    idea.Title = idea.IdeaTitle;
                // An idea deleted since the export comes back.
                idea.IsDeleted = false;
                var result = idea.Id is { Length: > 0 } && existing.ContainsKey(idea.Id)
                    ? await _assistant.UpdateIdeaAsync(idea, cancellationToken)
                    : await _assistant.CreateIdeaAsync(idea, cancellationToken);
                if (result.IsSuccess)
                {
                    saved++;
                    lastId = result.ReturnedId;
                }
                else
                    failed.Add($"{idea.IdeaTitle ?? idea.Id}: {result.Message}");
            }
            ActivityLog.Info("Idea", $"{saved} idea(s) imported from {path} ({added.Count} new, {replaced.Count} replaced, {same} unchanged)");
            await SearchCommand.ExecuteAsync(null);
            if (single && lastId is not null && Results.FirstOrDefault(r => r.Id == lastId) is { } imported)
                SelectedResult = imported;
            IsError = failed.Count > 0;
            Status = single
                ? failed.Count == 0
                    ? $"Idea « {ideas[0].IdeaTitle} » {(added.Count == 1 ? "imported" : "replaced")} from {Path.GetFileName(path)}"
                    : $"The idea was not imported: {failed[0]}"
                : failed.Count == 0
                ? $"{saved} idea(s) imported from {Path.GetFileName(path)}: {added.Count} new, {replaced.Count} replaced, {same} left alone"
                : $"{saved} of {toSave.Count} idea(s) imported; not saved: {string.Join(" · ", failed.Take(3))}";
        }
        catch (OperationCanceledException)
        {
            Status = "Import cancelled";
        }
        catch (HttpRequestException ex)
        {
            IsError = true;
            Status = ex.StatusCode is null
                ? $"Kaonomi server unreachable at {_assistant.BaseAddress} ({ex.Message})"
                : $"The ideas were not imported: {ex.Message}";
            ActivityLog.Error("Idea", "Ideas not imported", ex);
        }
    }

    /// <summary>The ideas of a file: an object with "ideas" (written by ⬇ EXPORT ALL or ⬇ EXPORT IDEA), a bare array of ideas, or an idea alone.</summary>
    public static List<IdeaEntry> ReadIdeaFile(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        System.Text.Json.JsonElement array;
        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            array = root;
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("ideas", out var inner)
                 && inner.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            if (root.TryGetProperty("kind", out var kind) && kind.ValueKind == System.Text.Json.JsonValueKind.String && kind.GetString() != ExportKind)
                throw new System.Text.Json.JsonException($"this file holds « {kind.GetString()} », not ideas");
            array = inner;
        }
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object && (root.TryGetProperty("ideaTitle", out _) || root.TryGetProperty("ideaDescription", out _)))
        {
            // An idea alone.
            var one = System.Text.Json.JsonSerializer.Deserialize<IdeaEntry>(root);
            return one is not null && (!string.IsNullOrWhiteSpace(one.IdeaTitle) || !string.IsNullOrWhiteSpace(one.IdeaDescription)) ? [one] : [];
        }
        else
            throw new System.Text.Json.JsonException("neither a file of ideas (an object with \"ideas\"), an array of ideas nor an idea");
        return array.EnumerateArray()
            .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.Object)
            .Select(e => System.Text.Json.JsonSerializer.Deserialize<IdeaEntry>(e))
            .Where(i => i is not null && (!string.IsNullOrWhiteSpace(i.IdeaTitle) || !string.IsNullOrWhiteSpace(i.IdeaDescription)))
            .Select(i => i!)
            .ToList();
    }

    /// <summary>True when two ideas hold the same (what the user writes, and where they belong).</summary>
    private static bool SameIdea(IdeaEntry a, IdeaEntry b)
    {
        static string T(string? s) => (s ?? string.Empty).Replace("\r\n", "\n").Trim();
        static string L(List<string>? l) => string.Join('\n', (l ?? []).Select(x => x.Trim()).Order(StringComparer.Ordinal));
        return T(a.IdeaTitle) == T(b.IdeaTitle) && T(a.IdeaDescription) == T(b.IdeaDescription) && T(a.Category) == T(b.Category)
               && T(a.Comment) == T(b.Comment) && T(a.ProjectId) == T(b.ProjectId) && L(a.Tags) == L(b.Tags)
               && L(a.Events) == L(b.Events) && L(a.Signals) == L(b.Signals);
    }

    /// <summary>A /kanban window on the same assistant, for an idea sent there as a new task.</summary>
    public KanbanWindowViewModel CreateKanbanWindow() => new(_assistant);

    /// <summary>Rebuilds the category suggestions from the listed ideas.</summary>
    private void RefreshCategories()
    {
        var categories = Results.Select(r => r.Category.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Categories.Clear();
        foreach (var category in categories)
            Categories.Add(category);
    }

    /// <inheritdoc />
    public string NoteWindowName => "Ideas";

    /// <inheritdoc />
    public object? GetNoteContext() => new
    {
        searchTerm = Term,
        resultCount = Results.Count,
        selected = SelectedResult?.ToEntry()
    };
}
