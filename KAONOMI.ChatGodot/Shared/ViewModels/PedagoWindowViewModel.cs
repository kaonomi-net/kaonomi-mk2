using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// <c>/pedago:test</c>: a picture (a kitchen) whose objects are known (<see cref="PedagoScene"/>: each outlined once,
/// by hand). A click on an object names it at once, without a model: its name is written over the picture and said
/// by the voice of the chat; then the model of the chat says, for a child, what it is called and what it is used for
/// (<c>pedago.explain</c> of /prompts), written in the chat and read aloud. What a model said of an object is kept:
/// clicked again, it is said again without asking. The successes are counted: a click on an object is one, a click
/// beside every object is a miss; the objects found (each once) are counted out of those of the picture.
/// </summary>
public sealed partial class PedagoWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel? _chat;
    private readonly OllamaModelService? _ollama;
    private readonly string _model;
    private readonly string _language;
    private readonly Dictionary<string, string> _explanations = [];
    private readonly HashSet<string> _found = [];
    private CancellationTokenSource? _asking;

    /// <summary>For the designer.</summary>
    public PedagoWindowViewModel() : this(null, null, string.Empty, "FR")
    {
    }

    public PedagoWindowViewModel(MainViewModel? chat, OllamaModelService? ollama, string model, string language, string scene = "kitchen", string? folder = null)
    {
        (_chat, _ollama, _model, _language) = (chat, ollama, model, language);
        (Scene, var error) = PedagoScene.Load(scene, folder);
        if (error is not null)
        {
            SetStatus(error, true);
            ActivityLog.Error("Pedago", error);
        }
        else
        {
            SetStatus(Hint, false);
        }
    }

    /// <summary>The picture and its objects; null when the scene could not be read (<see cref="Status"/> says why).</summary>
    public PedagoScene? Scene { get; }

    public string Title => Scene?.Title(_language) ?? string.Empty;

    /// <summary>How many objects the picture holds.</summary>
    public int Total => Scene?.Objects.Count ?? 0;

    private bool French => _language.Equals("FR", StringComparison.OrdinalIgnoreCase);

    /// <summary>What to do, in the language of the chat.</summary>
    public string Hint => French ? "Clique sur un objet de l'image pour savoir comment il s'appelle et à quoi il sert."
        : "Click on an object of the picture to know what it is called and what it is used for.";

    /// <summary>The object clicked last; null after a click beside every object.</summary>
    [ObservableProperty]
    private PedagoObject? _selected;

    /// <summary>The name of the object clicked, known at once (no model): « une passoire ».</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>What the model said of the object clicked (empty while it is asked).</summary>
    [ObservableProperty]
    private string _explanation = string.Empty;

    /// <summary>True while the model is asked what the object is used for.</summary>
    [ObservableProperty]
    private bool _isExplaining;

    /// <summary>The clicks that fell on an object.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Score))]
    private int _successes;

    /// <summary>The clicks beside every object.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Score))]
    private int _misses;

    /// <summary>The objects found so far, each counted once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Score))]
    private int _foundCount;

    /// <summary>Every outline is drawn over the picture (to check a scene, or to help).</summary>
    [ObservableProperty]
    private bool _showAll;

    /// <summary>Where the last click fell, as shares of the width and of the height of the picture.</summary>
    [ObservableProperty]
    private (double X, double Y)? _lastClick;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    /// <summary>The score as shown: the successes, the objects found out of those of the picture, the misses.</summary>
    public string Score => French
        ? $"✔ {Successes} réussite{(Successes > 1 ? "s" : "")}  ·  {FoundCount}/{Total} objets trouvés  ·  ✘ {Misses} à côté"
        : $"✔ {Successes} success{(Successes == 1 ? "" : "es")}  ·  {FoundCount}/{Total} objects found  ·  ✘ {Misses} beside";

    /// <summary>True once <paramref name="thing"/> was found.</summary>
    public bool WasFound(PedagoObject thing) => _found.Contains(thing.Id);

    private void SetStatus(string text, bool error)
    {
        Status = text;
        IsError = error;
    }

    /// <summary>
    /// A click on the picture, at (<paramref name="x"/>, <paramref name="y"/>) as shares of its width and height: the
    /// object there is named at once, counted, then explained by the model; beside every object, a miss.
    /// </summary>
    public void Click(double x, double y)
    {
        if (Scene is not { } scene)
            return;
        LastClick = (x, y);
        var thing = scene.At(x * scene.Width, y * scene.Height);
        if (thing is null)
        {
            _asking?.Cancel();
            Misses++;
            IsExplaining = false;
            Selected = null;
            Name = string.Empty;
            Explanation = string.Empty;
            SetStatus(French ? "Ici, il n'y a pas d'objet que je connais. Essaie ailleurs !" : "There is no object I know here. Try somewhere else!", false);
            return;
        }
        Successes++;
        if (_found.Add(thing.Id))
            FoundCount = _found.Count;
        // The name at once: it is known from the picture, no model is asked.
        Explanation = string.Empty;
        Name = thing.Name(_language);
        Selected = thing;
        SetStatus(FoundCount == Total
            ? (French ? "Bravo, tu as trouvé tous les objets !" : "Well done, you found every object!")
            : string.Empty, false);
        ActivityLog.Info("Pedago", $"{Title}: {thing.Id} ({Name}) clicked; {Successes} successes, {FoundCount}/{Total} found, {Misses} beside");
        _ = ExplainAsync(thing, Name);
    }

    /// <summary>The name with its first letter in upper case, as said or written alone.</summary>
    public static string Sentence(string name) => name.Length == 0 ? name : char.ToUpper(name[0]) + name[1..];

    private static string LanguageName(string code) => code.ToUpperInvariant() switch
    {
        "FR" => "French", "EN" => "English", "DE" => "German", "NL" => "Dutch", "ES" => "Spanish", "IT" => "Italian", "JA" => "Japanese",
        _ => code
    };

    /// <summary>Says the name at once, then what the object is used for: what was said of it before, or what the model answers.</summary>
    private async Task ExplainAsync(PedagoObject thing, string name)
    {
        _asking?.Cancel();
        var asking = _asking = new CancellationTokenSource();
        try
        {
            if (_explanations.TryGetValue(thing.Id, out var known))
            {
                IsExplaining = false;
                Explanation = known;
                if (_chat is not null)
                    await _chat.SayAsync(known);
                return;
            }
            // The name, said by the voice of the chat while the model is asked.
            _chat?.Say(Sentence(name) + ".");
            if (_ollama is null || string.IsNullOrWhiteSpace(_model))
            {
                SetStatus(French ? "Aucun modèle n'est choisi dans le chat : seul le nom est donné." : "No model is chosen in the chat: only the name is given.", true);
                return;
            }
            IsExplaining = true;
            var system = Prompts.Get("pedago.explain", ("language", LanguageName(_language)), ("scene", Title));
            var text = Spoken(await _ollama.ChatAsync(_model, system, name, asking.Token));
            if (asking.IsCancellationRequested)
                return;
            _explanations[thing.Id] = text;
            IsExplaining = false;
            if (ReferenceEquals(Selected, thing))
                Explanation = text;
            if (_chat is not null)
                await _chat.SayAsync(text);
        }
        catch (OperationCanceledException)
        {
            // Another object was clicked meanwhile.
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            SetStatus((French ? "Le modèle n'a pas répondu : " : "The model did not answer: ") + ex.Message, true);
            ActivityLog.Error("Pedago", $"{thing.Id}: the model did not answer", ex);
        }
        finally
        {
            if (ReferenceEquals(_asking, asking))
            {
                _asking = null;
                IsExplaining = false;
            }
            asking.Dispose();
        }
    }

    /// <summary>The answer of the model as it is said: one paragraph, without the marks of Markdown.</summary>
    private static string Spoken(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text.Replace("**", string.Empty).Replace("*", string.Empty).Replace("#", string.Empty), @"\s+", " ").Trim();

    /// <summary>Starts again: the counts back to zero, nothing selected (what the model said of the objects is kept).</summary>
    [RelayCommand]
    private void Reset()
    {
        _asking?.Cancel();
        _found.Clear();
        Successes = Misses = FoundCount = 0;
        IsExplaining = false;
        Selected = null;
        Name = string.Empty;
        Explanation = string.Empty;
        LastClick = null;
        SetStatus(Hint, false);
    }

    public void Dispose() => _asking?.Cancel();
}
