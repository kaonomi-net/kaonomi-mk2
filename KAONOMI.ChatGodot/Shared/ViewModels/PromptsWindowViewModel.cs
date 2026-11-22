using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A prompt in the list of /prompts: what it is, and the version in use.</summary>
public sealed partial class PromptItemViewModel(PromptDefinition definition) : ObservableObject
{
    public PromptDefinition Definition { get; } = definition;

    public string Key => Definition.Key;

    public string Title => Definition.Title;

    /// <summary>The version in use (0: the default, the search engine has none), and whether its text is no longer the default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private int _version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private bool _isChanged;

    public string Details => $"{Key}  ·  {(Version > 0 ? $"v{Version}" : "default")}{(IsChanged ? "  ·  changed" : string.Empty)}";

    public void Refresh()
    {
        Version = Prompts.CurrentVersion(Key);
        IsChanged = Prompts.Template(Key) != Prompts.Default(Key);
    }
}

/// <summary>A prompt as a JSON file: what /prompts saves, and what it takes back (only <c>text</c> is needed then).</summary>
public sealed class PromptFile
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("placeholders")]
    public List<string> Placeholders { get; set; } = [];

    /// <summary>The version of <see cref="Text"/> (0: the default).</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("default")]
    public string Default { get; set; } = string.Empty;

    /// <summary>Every version kept, the oldest first.</summary>
    [JsonPropertyName("versions")]
    public List<PromptVersion> Versions { get; set; } = [];
}

/// <summary>
/// /prompts: the prompts the tools of the chat send to the models (<see cref="Prompts"/>). On the left, every prompt
/// (a search narrows the list) with the version in use; on the right the one selected: what it is for, its
/// placeholders, its text to edit, and its versions. SAVE keeps the text as a new version (with a word on what
/// changed) and uses it at once, in every tool. A click on a former version puts its text in the editor: saving it
/// makes it the newest one again. DEFAULT puts back the default of config.yml. ANALYSE asks the model of the chat to
/// review the prompt (what it asks, its weaknesses, a better wording). SAVE AS JSON writes the prompt and all its
/// versions in a file; LOAD JSON takes the text of such a file (or of any JSON with a "text") as a new version.
/// Nothing is ever erased: every change is one more version in the search engine.
/// </summary>
public sealed partial class PromptsWindowViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly KaonomiAssistantService? _assistant;
    private readonly OllamaModelService? _ollama;
    private readonly Func<string?>? _model;
    private readonly string _language;
    private readonly List<PromptItemViewModel> _all;
    private CancellationTokenSource? _analysing;

    public PromptsWindowViewModel() : this(null, null, null, "EN")
    {
    }

    public PromptsWindowViewModel(KaonomiAssistantService? assistant, OllamaModelService? ollama, Func<string?>? model, string language)
    {
        _assistant = assistant;
        _ollama = ollama;
        _model = model;
        _language = language;
        _all = [.. Prompts.All.Select(d => new PromptItemViewModel(d))];
        foreach (var item in _all)
            item.Refresh();
        Filter();
        Selected = Items.FirstOrDefault();
        _ = ReloadAsync();
    }

    /// <summary>The prompts shown (the ones the search keeps).</summary>
    public ObservableCollection<PromptItemViewModel> Items { get; } = [];

    /// <summary>The versions of the prompt selected, the newest first.</summary>
    public ObservableCollection<PromptVersion> Versions { get; } = [];

    /// <summary>Asks where to save a JSON file (title, suggested name): its path, or null. Set by the view.</summary>
    public Func<string, string, Task<string?>> SaveFileAsync { get; set; } = (_, _) => Task.FromResult<string?>(null);

    /// <summary>Asks which JSON file to load (title): its path, or null. Set by the view.</summary>
    public Func<string, Task<string?>> OpenFileAsync { get; set; } = _ => Task.FromResult<string?>(null);

    [ObservableProperty]
    private string _term = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Heading), nameof(About))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(AnalyseCommand), nameof(ExportCommand), nameof(ImportCommand), nameof(UseDefaultCommand))]
    private PromptItemViewModel? _selected;

    /// <summary>The text being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(EditedLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _text = string.Empty;

    /// <summary>A word on what changed, kept with the version saved.</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>The review of the prompt by the model (Markdown).</summary>
    [ObservableProperty]
    private string _analysis = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyseCommand))]
    private bool _isAnalysing;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    /// <summary>Where the text in the editor comes from, when it is not typed: "edit", "restore", "default" or "upload".</summary>
    private string _source = "edit";

    public bool HasSelection => Selected is not null;

    public string Heading => Selected is { } item ? item.Title : "No prompt selected";

    /// <summary>What the prompt selected is for, and its placeholders.</summary>
    public string About => Selected is not { } item ? string.Empty
        : item.Definition.Description + (item.Definition.Placeholders.Count > 0
            ? "\nPlaceholders (replaced when the prompt is used; keep them as they are): " + string.Join(", ", item.Definition.Placeholders.Select(p => "{{" + p + "}}"))
            : "\nNo placeholder.");

    /// <summary>The text in the editor is not the one in use.</summary>
    public bool IsDirty => Selected is { } item && Text.Replace("\r\n", "\n") != Prompts.Template(item.Key);

    public string EditedLabel => Selected is not { } item ? string.Empty
        : IsDirty ? $"edited · not saved ({Text.Length} characters)" : $"{(item.Version > 0 ? $"version {item.Version}" : "the default")} in use ({Text.Length} characters)";

    partial void OnTermChanged(string value) => Filter();

    partial void OnSelectedChanged(PromptItemViewModel? value)
    {
        _analysing?.Cancel();
        Analysis = string.Empty;
        Comment = string.Empty;
        _source = "edit";
        Text = value is null ? string.Empty : Prompts.Template(value.Key);
        ShowVersions();
    }

    partial void OnTextChanged(string value)
    {
        // Typed again after a version, the default or a file was put in the editor: it is an edit of it.
        if (Selected is not null && !IsDirty)
            _source = "edit";
    }

    private void Filter()
    {
        var words = Term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = _all.Where(item => words.All(word => item.Key.Contains(word, StringComparison.OrdinalIgnoreCase)
            || item.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
            || item.Definition.Description.Contains(word, StringComparison.OrdinalIgnoreCase)
            || Prompts.Template(item.Key).Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
        Items.Clear();
        foreach (var item in kept)
            Items.Add(item);
        if (Selected is not null && !kept.Contains(Selected))
            Selected = kept.FirstOrDefault();
    }

    private void ShowVersions()
    {
        Versions.Clear();
        if (Selected is { } item)
            foreach (var version in Prompts.Versions(item.Key).Reverse())
                Versions.Add(version);
    }

    private void Report(string status, bool isError)
    {
        Status = status;
        IsError = isError;
    }

    /// <summary>Reads the versions again from the search engine (another machine may have saved some).</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_assistant is null)
        {
            Report("No server: the defaults of config.yml are used, and nothing can be saved", true);
            return;
        }
        var edited = IsDirty;
        var result = await Prompts.LoadAsync(_assistant);
        foreach (var item in _all)
            item.Refresh();
        ShowVersions();
        // What was being typed is kept; else the text in use is shown.
        if (!edited && Selected is { } selected)
            Text = Prompts.Template(selected.Key);
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(EditedLabel));
        Report(char.ToUpperInvariant(result[0]) + result[1..], result.Contains("not reached", StringComparison.Ordinal));
    }

    private bool CanSave() => Selected is not null && IsDirty && Text.Trim().Length > 0;

    /// <summary>Keeps the text as a new version of the prompt, used at once by every tool.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (Selected is not { } item)
            return;
        if (_assistant is null)
        {
            Report("No server: the version cannot be saved", true);
            return;
        }
        var missing = item.Definition.Placeholders.Where(p => !Text.Contains("{{" + p + "}}", StringComparison.Ordinal)).ToList();
        try
        {
            var version = await Prompts.SaveAsync(_assistant, item.Key, Text, _source, Comment);
            item.Refresh();
            ShowVersions();
            Comment = string.Empty;
            _source = "edit";
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(EditedLabel));
            SaveCommand.NotifyCanExecuteChanged();
            Report($"{item.Title}: version {version.Version} saved and in use" + (missing.Count > 0
                ? $" · note: it no longer holds {string.Join(", ", missing.Select(p => "{{" + p + "}}"))}, so the tool cannot give it that value" : string.Empty), false);
            ActivityLog.Info("Prompts", $"{item.Key}: version {version.Version} saved ({version.Source}, {version.Text.Length} chars)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Report($"The version was not saved: {ex.Message}", true);
            ActivityLog.Error("Prompts", $"{item.Key}: the version was not saved", ex);
        }
    }

    /// <summary>Puts the text of a former version in the editor: SAVE makes it the newest version again.</summary>
    [RelayCommand]
    private void ShowVersion(PromptVersion? version)
    {
        if (version is null || Selected is null)
            return;
        Text = version.Text;
        _source = "restore";
        Comment = $"back to version {version.Version}";
        Report(IsDirty ? $"Version {version.Version} ({version.Date.ToLocalTime():yyyy-MM-dd HH:mm}) is in the editor: SAVE makes it the version in use again"
            : $"Version {version.Version} is the one in use", false);
    }

    /// <summary>Puts the default of config.yml in the editor: SAVE makes it the newest version.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void UseDefault()
    {
        if (Selected is not { } item)
            return;
        Text = Prompts.Default(item.Key);
        _source = "default";
        Comment = "back to the default";
        Report(IsDirty ? "The default is in the editor: SAVE makes it the version in use" : "The text in use is the default", false);
    }

    private bool CanAnalyse() => Selected is not null && !IsAnalysing;

    /// <summary>Asks the model of the chat to review the text in the editor (ANALYSE again while it runs: stopped).</summary>
    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task Analyse()
    {
        if (Selected is not { } item)
            return;
        if (_ollama is null || _model?.Invoke() is not { Length: > 0 } model)
        {
            Report("No model is available to analyse the prompt", true);
            return;
        }
        _analysing?.Cancel();
        var cancel = _analysing = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        IsAnalysing = true;
        Analysis = string.Empty;
        Report($"Analysing the prompt with {model}…", false);
        try
        {
            var language = _language.ToUpperInvariant() switch { "FR" => "French", "DE" => "German", "ES" => "Spanish", "IT" => "Italian", "NL" => "Dutch", "JA" => "Japanese", _ => "English" };
            var placeholders = item.Definition.Placeholders.Count > 0 ? string.Join(", ", item.Definition.Placeholders.Select(p => "{{" + p + "}}")) : "none";
            var answer = await _ollama.ChatAsync(model, Prompts.Get("prompts.analyse", ("language", language)),
                $"What the prompt is for: {item.Title}. {item.Definition.Description}\nIts placeholders: {placeholders}\n\nThe prompt:\n\"\"\"\n{Text}\n\"\"\"", cancel.Token);
            if (cancel.IsCancellationRequested || !ReferenceEquals(Selected, item))
                return;
            Analysis = LatexText.Convert(answer).Trim();
            Report($"Analysis by {model}: read it, change the text, then SAVE", false);
            ActivityLog.Info("Prompts", $"{item.Key} analysed by {model} ({Analysis.Length} chars)");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            if (!cancel.IsCancellationRequested || ex is not OperationCanceledException)
                Report($"The prompt was not analysed: {ex.Message}", true);
        }
        finally
        {
            if (ReferenceEquals(_analysing, cancel))
                IsAnalysing = false;
        }
    }

    /// <summary>Writes the prompt selected, the text in the editor and all its versions in a JSON file.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Export()
    {
        if (Selected is not { } item)
            return;
        var path = await SaveFileAsync("Save the prompt as JSON", $"prompt-{item.Key}.json");
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            var file = new PromptFile
            {
                Key = item.Key, Title = item.Title, Description = item.Definition.Description, Placeholders = [.. item.Definition.Placeholders],
                Version = IsDirty ? 0 : item.Version, Text = Text.Replace("\r\n", "\n"), Default = Prompts.Default(item.Key), Versions = [.. Prompts.Versions(item.Key)]
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, Json));
            Report($"Saved in {path} ({file.Versions.Count} version(s))", false);
            ActivityLog.Info("Prompts", $"{item.Key} saved as JSON in {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"The file was not written: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Takes the text of a JSON file as a new version of the prompt selected: a file saved here, or any JSON object
    /// with a "text" (or "prompt") field. The file of another prompt is refused. Nothing is replaced: it is one more version.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Import()
    {
        if (Selected is not { } item)
            return;
        var path = await OpenFileAsync($"Load a JSON file for « {item.Title} »");
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("the file is not a JSON object");
            if (root.TryGetProperty("key", out var key) && key.GetString() is { Length: > 0 } named && named != item.Key)
            {
                Report($"This file is the prompt « {named} », not « {item.Key} »: select that prompt first", true);
                return;
            }
            var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()
                : root.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new JsonException("no \"text\" (or \"prompt\") field with the prompt");
            Text = text.Replace("\r\n", "\n");
            if (!IsDirty)
            {
                Report("The file holds the text already in use: nothing to save", false);
                return;
            }
            _source = "upload";
            Comment = $"loaded from {Path.GetFileName(path)}";
            await Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Report($"The file was not read: {ex.Message}", true);
        }
    }
}
