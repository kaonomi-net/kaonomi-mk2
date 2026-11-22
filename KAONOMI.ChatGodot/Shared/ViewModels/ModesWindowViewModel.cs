using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A use of /models: its model, and the one the gateway uses for it now.</summary>
public sealed partial class ModeViewModel(string key, string icon, string title, string description) : ObservableObject
{
    public string Key { get; } = key;

    public string Icon { get; } = icon;

    public string Title { get; } = title;

    public string Description { get; } = description;

    /// <summary>The default model of this use.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GatewayState))]
    private string _model = string.Empty;

    /// <summary>What the gateway does for this use now (read from it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GatewayState))]
    private string _gatewayModel = string.Empty;

    /// <summary>Whether the gateway has the model (installed in the server behind it).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GatewayState))]
    private bool _installed = true;

    public string GatewayState =>
        GatewayModel.Length == 0 ? "gateway: not read"
        : !Installed && Model.Length > 0 ? $"gateway: {GatewayModel} · {Model} is NOT installed"
        : string.Equals(GatewayModel, Model, StringComparison.OrdinalIgnoreCase) ? $"gateway: {GatewayModel} ✓"
        : $"gateway: {GatewayModel} ≠ (ALIGN to use {(Model.Length > 0 ? Model : "?")})";
}

/// <summary>
/// /models: the default model of each use: the basic discussions, the advanced questions (reasoning, calculations,
/// code: the model the chat starts with), the analysis of sound, of pictures (the vision model) and of video. SAVE writes
/// them in config.yml (<c>modes</c>) and the chat uses them at once. ALIGN THE GATEWAY makes the LLM gateway route
/// the same way: its default model is the one of the discussions; its rules of code, calculations, numbers and long
/// sentences, and its escalation model (the 👎), the advanced model; its rule of pictures the image model; a rule of
/// sound and one of video (AUDIO, VIDEO in the request) the audio and video models. Each line says what the gateway
/// does now.
/// </summary>
public sealed partial class ModesWindowViewModel : ObservableObject
{
    // The rules of the gateway the advanced model takes (its routing rules of reasoning).
    private static readonly string[] AdvancedRules = ["code", "math", "numbers", "complex"];

    private readonly ChatSettings? _settings;
    private readonly Action<ModeModels>? _applied;
    private readonly LlmGatewayClient? _gateway;

    public ModesWindowViewModel() : this(null, null)
    {
    }

    public ModesWindowViewModel(ChatSettings? settings, Action<ModeModels>? applied)
    {
        _settings = settings;
        _applied = applied;
        if (settings is not null)
            _gateway = new LlmGatewayClient(settings.LlmGatewayUrl);
        var modes = settings?.Modes ?? new ModeModels();
        Modes.Add(new ModeViewModel("chat", "💬", "Basic discussions", "The questions of every day, the conversations: the default model of the gateway")
        {
            Model = Or(modes.Chat, modes.Advanced.Length == 0 ? settings?.Model : null)
        });
        Modes.Add(new ModeViewModel("advanced", "🧠", "Advanced questions", "The model the chat starts with; reasoning, calculations, numbers, code, long questions: the gateway sends them there; also the 👎 (escalation)") { Model = modes.Advanced });
        Modes.Add(new ModeViewModel("audio", "🎧", "Sound analysis", "A request with a sound (an audio part)") { Model = modes.Audio });
        Modes.Add(new ModeViewModel("image", "🖼", "Image analysis", "A request with a picture; the vision model of the tools (analysis of pictures, the work)") { Model = Or(modes.Image, settings?.VisionModel) });
        Modes.Add(new ModeViewModel("video", "🎞", "Video analysis", "A request with a video (a video part)") { Model = modes.Video });
        foreach (var mode in Modes)
            mode.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModeViewModel.Model))
                    UpdateInstalled();
            };
        _ = ReloadAsync();
    }

    private static string Or(string value, string? fallback) => value.Length > 0 ? value : fallback?.Trim() ?? string.Empty;

    public ObservableCollection<ModeViewModel> Modes { get; } = [];

    /// <summary>The models the server behind the gateway has.</summary>
    public ObservableCollection<string> Installed { get; } = [];

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    private void Report(string status, bool isError)
    {
        Status = status;
        IsError = isError;
    }

    private ModeModels Current() => new()
    {
        Chat = Model("chat"), Advanced = Model("advanced"), Audio = Model("audio"), Image = Model("image"), Video = Model("video")
    };

    private string Model(string key) => Modes.First(m => m.Key == key).Model.Trim();

    private void UpdateInstalled()
    {
        foreach (var mode in Modes)
            mode.Installed = Installed.Count == 0 || mode.Model.Trim().Length == 0 || Installed.Contains(mode.Model.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads the models installed and what the gateway does for each use.</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_gateway is null)
        {
            Report("No settings: the gateway is not known", true);
            return;
        }
        IsBusy = true;
        try
        {
            var models = await _gateway.ModelsAsync();
            Installed.Clear();
            foreach (var model in models.Where(m => !m.Contains("embed", StringComparison.OrdinalIgnoreCase)))
                Installed.Add(model);
            var settings = await _gateway.SettingsAsync();
            var rules = await _gateway.RulesAsync();
            string Rule(string name) => rules.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && r.Enabled)?.Model ?? "no rule";
            var routing = settings?.Routing;
            Set("chat", routing?.DefaultModel ?? "?");
            var advanced = AdvancedRules.Select(Rule).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Set("advanced", (advanced.Count == 1 ? advanced[0] : string.Join(" / ", advanced)) + (routing is null ? "" : $" (👎: {(routing.EscalationModel.Length > 0 ? routing.EscalationModel : "none")})"));
            Set("audio", Rule("audio"));
            Set("image", Rule("images"));
            Set("video", Rule("video"));
            UpdateInstalled();
            Report($"{Installed.Count} model(s) installed · routing {(routing?.Enabled == true ? "on" : "OFF")}" + (Modes.Any(m => !m.Installed) ? " · a model chosen is not installed" : string.Empty), Modes.Any(m => !m.Installed));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Report($"The gateway ({_settings?.LlmGatewayUrl}) could not be read: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Set(string key, string gateway) => Modes.First(m => m.Key == key).GatewayModel = gateway;

    /// <summary>Writes the models in config.yml; the chat uses them at once.</summary>
    [RelayCommand]
    private void Save()
    {
        if (_settings is null)
            return;
        var modes = Current();
        _settings.Modes = modes;
        ChatSettings.SaveModes(modes);
        _applied?.Invoke(modes);
        Report("Saved in config.yml (modes) · the chat uses them now; ALIGN THE GATEWAY to route the same way", false);
        ActivityLog.Info("Modes", $"chat {modes.Chat}, advanced {modes.Advanced}, audio {modes.Audio}, image {modes.Image}, video {modes.Video}");
    }

    /// <summary>
    /// Makes the gateway route as the modes say: the default model, the escalation model, the rules of reasoning,
    /// of pictures, of sound and of video (made when missing). A use without a model leaves the gateway as it is.
    /// </summary>
    [RelayCommand]
    private async Task AlignAsync()
    {
        if (_gateway is null)
            return;
        var modes = Current();
        IsBusy = true;
        try
        {
            var changes = new List<string>();
            var settings = await _gateway.SettingsAsync() ?? throw new InvalidOperationException("the gateway gave no settings");
            var routing = settings.Routing;
            if (modes.Chat.Length > 0 && routing.DefaultModel != modes.Chat)
            {
                changes.Add($"default model {routing.DefaultModel} → {modes.Chat}");
                routing.DefaultModel = modes.Chat;
            }
            if (modes.Advanced.Length > 0 && routing.EscalationModel != modes.Advanced)
            {
                changes.Add($"escalation {(routing.EscalationModel.Length > 0 ? routing.EscalationModel : "none")} → {modes.Advanced}");
                routing.EscalationModel = modes.Advanced;
            }
            var rules = await _gateway.RulesAsync();
            void Route(string name, string model, Func<GatewayRule> create)
            {
                if (model.Length == 0)
                    return;
                var rule = rules.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (rule is null)
                {
                    rule = create();
                    rule.Model = model;
                    rules.Add(rule);
                    changes.Add($"rule {name} added ({model})");
                    return;
                }
                if (rule.Model != model || !rule.Enabled)
                {
                    changes.Add($"rule {name} {rule.Model}{(rule.Enabled ? "" : " (off)")} → {model}");
                    rule.Model = model;
                    rule.Enabled = true;
                }
            }
            foreach (var name in AdvancedRules)
                if (rules.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    Route(name, modes.Advanced, () => new GatewayRule());
            Route("images", modes.Image, () => new GatewayRule { Name = "images", Description = "A picture in the request: the model that sees (/models).", Any = ["IMAGE"], Priority = 100 });
            Route("audio", modes.Audio, () => new GatewayRule { Name = "audio", Description = "A sound in the request: the model that hears (/models).", Any = ["AUDIO"], Priority = 100 });
            Route("video", modes.Video, () => new GatewayRule { Name = "video", Description = "A video in the request: the model of video (/models).", Any = ["VIDEO"], Priority = 100 });
            if (changes.Count == 0)
            {
                Report("The gateway already routes as the modes say", false);
                return;
            }
            await _gateway.SaveRulesAsync(rules);
            await _gateway.SaveSettingsAsync(settings.Cache, routing);
            ActivityLog.Info("Modes", "Gateway aligned: " + string.Join("; ", changes));
            await ReloadAsync();
            Report("Gateway aligned: " + string.Join(" · ", changes), false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Report($"The gateway was not aligned: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
