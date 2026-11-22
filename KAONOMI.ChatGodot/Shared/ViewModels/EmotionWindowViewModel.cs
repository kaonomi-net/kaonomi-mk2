using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>An emotion of /emotion: its name, and the text added to the system prompt while it leads.</summary>
public sealed partial class EmotionPromptViewModel(string name, string icon, string hint) : ObservableObject
{
    public string Name { get; } = name;

    public string Icon { get; } = icon;

    /// <summary>What the emotion means, in a few words.</summary>
    public string Hint { get; } = hint;

    /// <summary>The key of its prompt (<c>emotion.happy</c>…).</summary>
    public string Key => "emotion." + Name;

    /// <summary>The text being edited.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(State))]
    private string _text = string.Empty;

    /// <summary>The version in use (0: none yet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private int _version;

    /// <summary>The emotion engine found this emotion last: its text is the one applied now.</summary>
    [ObservableProperty]
    private bool _isLeading;

    public bool IsDirty => Text.Replace("\r\n", "\n").Trim() != Prompts.Template(Key).Trim();

    public string State => IsDirty ? "edited · not saved"
        : Version == 0 ? "no text: the system prompt is not changed"
        : Prompts.Template(Key).Trim().Length == 0 ? $"version {Version} · emptied: the system prompt is not changed"
        : $"version {Version} in use";

    public void Refresh(bool keepEdit)
    {
        Version = Prompts.CurrentVersion(Key);
        if (!keepEdit)
            Text = Prompts.Template(Key);
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(State));
    }
}

/// <summary>
/// /emotion: the "what if" of the emotions. Every 3 prompts of a conversation, the emotion engine of the server finds
/// the emotion that leads in these prompts (happy, sad, angry, fearful, disgust, surprised, neutral). For each of
/// them, this window holds a text: while that emotion leads in the conversation, the server adds its text to the
/// system prompt of the answers ("what if the user is angry: …"). An emotion whose text is empty changes nothing.
/// SAVE keeps each text changed as a new version (the prompts <c>emotion.&lt;name&gt;</c>, see /prompts for their
/// history): nothing is erased, and emptying a text is one more version. The window also says what the engine found
/// last.
/// </summary>
public sealed partial class EmotionWindowViewModel : ObservableObject
{
    private static readonly (string Name, string Icon, string Hint)[] Known =
    [
        ("happy", "😊", "joyful, pleased"), ("sad", "😢", "sad, discouraged"), ("angry", "😠", "angry, irritated"), ("fearful", "😨", "worried, afraid"),
        ("disgust", "🤢", "disgusted, put off"), ("surprised", "😮", "surprised"), ("neutral", "😐", "no feeling shows")
    ];

    private readonly KaonomiAssistantService? _assistant;

    public EmotionWindowViewModel() : this(null)
    {
    }

    public EmotionWindowViewModel(KaonomiAssistantService? assistant)
    {
        _assistant = assistant;
        foreach (var (name, icon, hint) in Known)
        {
            var emotion = new EmotionPromptViewModel(name, icon, hint);
            emotion.Refresh(keepEdit: false);
            emotion.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(EmotionPromptViewModel.Text))
                    SaveCommand.NotifyCanExecuteChanged();
            };
            Emotions.Add(emotion);
        }
        _ = ReloadAsync();
    }

    /// <summary>The seven emotions the engine tells apart.</summary>
    public ObservableCollection<EmotionPromptViewModel> Emotions { get; } = [];

    /// <summary>What the emotion engine found last, in any conversation.</summary>
    [ObservableProperty]
    private string _lastDetected = "The emotion engine has found nothing yet";

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    private void Report(string status, bool isError)
    {
        Status = status;
        IsError = isError;
    }

    /// <summary>Reads the texts again from the search engine, and what the engine found last.</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_assistant is null)
        {
            Report("No server: the texts cannot be read or saved", true);
            return;
        }
        var result = await Prompts.LoadAsync(_assistant);
        foreach (var emotion in Emotions)
            emotion.Refresh(keepEdit: emotion.IsDirty);
        await ShowLastAsync();
        SaveCommand.NotifyCanExecuteChanged();
        var missed = result.Contains("not reached", StringComparison.Ordinal);
        Report(missed ? char.ToUpperInvariant(result[0]) + result[1..]
            : $"{Emotions.Count(e => Prompts.Template(e.Key).Trim().Length > 0)} of {Emotions.Count} emotions have a text · it is added to the system prompt while that emotion leads", missed);
    }

    private async Task ShowLastAsync()
    {
        if (_assistant is null)
            return;
        try
        {
            var found = await _assistant.LastDetectedEmotionsAsync(1);
            foreach (var emotion in Emotions)
                emotion.IsLeading = false;
            if (found.Count == 0)
            {
                LastDetected = "The emotion engine has found nothing yet: it looks at the emotions every 3 prompts of a conversation";
                return;
            }
            var last = found[0];
            var name = last.TryGetProperty("emotion", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            var intensity = last.TryGetProperty("intensity", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetDouble() : 0;
            var when = last.TryGetProperty("date", out var d) && d.TryGetDateTime(out var date) ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : string.Empty;
            var why = last.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var applied = last.TryGetProperty("systemText", out var t) && (t.GetString() ?? string.Empty).Length > 0;
            LastDetected = $"Found last ({when}): {name}, {intensity:0}/10 · {(applied ? "its text was added to the system prompt" : "it had no text: the system prompt was not changed")}"
                + (why.Length > 0 ? $"\n{why}" : string.Empty);
            foreach (var emotion in Emotions)
                emotion.IsLeading = emotion.Name == name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LastDetected = $"What the emotion engine found could not be read: {ex.Message}";
        }
    }

    private bool CanSave() => Emotions.Any(e => e.IsDirty);

    /// <summary>Keeps each text changed as a new version: the server uses it from the next prompt.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_assistant is null)
        {
            Report("No server: the texts cannot be saved", true);
            return;
        }
        var saved = new List<string>();
        try
        {
            foreach (var emotion in Emotions.Where(e => e.IsDirty).ToList())
            {
                var text = emotion.Text.Replace("\r\n", "\n").Trim();
                var version = await Prompts.SaveAsync(_assistant, emotion.Key, text, "edit", text.Length == 0 ? "emptied in /emotion" : "edited in /emotion");
                emotion.Refresh(keepEdit: false);
                saved.Add($"{emotion.Name} v{version.Version}");
                ActivityLog.Info("Emotion", $"{emotion.Key}: version {version.Version} saved ({text.Length} chars)");
            }
            Report($"Saved: {string.Join(", ", saved)} · used from the next prompt", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Report($"{(saved.Count > 0 ? "Saved: " + string.Join(", ", saved) + " · then " : string.Empty)}a text was not saved: {ex.Message}", true);
        }
        SaveCommand.NotifyCanExecuteChanged();
    }
}
