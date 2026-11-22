using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KAONOMI.Chat.Services;

/// <summary>
/// A prompt of the chat: its key (how the code asks for it), what it is for, the placeholders its text may hold
/// (<c>{{language}}</c>…) and its text when nobody changed it.
/// </summary>
public sealed record PromptDefinition(string Key, string Title, string Description, IReadOnlyList<string> Placeholders, string Default);

/// <summary>
/// A version of a prompt, as its document in the search engine of the server (<c>PromptTemplate</c>, index
/// <c>prompttemplate</c>): one document per version, never written over and never erased. The version in use is the
/// one with the largest number.
/// </summary>
public sealed class PromptVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "KAONOMI.Chat";

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = Environment.UserName;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = Environment.MachineName;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>The key of the prompt (<c>cv.summary</c>, <c>game.chess</c>…).</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The number of the version, from 1.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Where the version comes from: <c>config</c> (the default of config.yml), <c>edit</c>, <c>upload</c> (a JSON file), <c>restore</c> (a former version taken again), <c>default</c> (back to the default).</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>What changed, as its author says.</summary>
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    public static string IdOf(string key, int version) => $"prompt-{key}-v{version}";
}

/// <summary>
/// The prompts the chat sends to the models. The code asks for a prompt by its key (<see cref="Get"/>) and gets the
/// text in use, its placeholders replaced:
/// <list type="number">
/// <item>the last version kept in the search engine of the server, when it has one (edited in /prompts);</item>
/// <item>else the default of config.yml (<c>promptTemplates</c>);</item>
/// <item>else the text written in the code (<see cref="PromptDefaults"/>).</item>
/// </list>
/// A prompt the search engine does not know yet is written there as its version 1, from the default. Every change is
/// a new version: nothing is ever written over or erased.
/// </summary>
public static class Prompts
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PromptDefinition> Definitions = PromptDefaults.All.ToDictionary(d => d.Key, StringComparer.Ordinal);
    private static Dictionary<string, string> _config = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<PromptVersion>> Stored = new(StringComparer.Ordinal);

    /// <summary>Raised when the prompts in use changed (read from the search engine, or one saved).</summary>
    public static event Action? Changed;

    /// <summary>Every prompt, in the order of <see cref="PromptDefaults"/>.</summary>
    public static IReadOnlyList<PromptDefinition> All => PromptDefaults.All;

    /// <summary>The defaults of config.yml (<c>promptTemplates</c>: key, text); the empty ones are left out.</summary>
    public static void UseConfig(IReadOnlyDictionary<string, string>? templates)
    {
        lock (Gate)
            _config = (templates ?? new Dictionary<string, string>()).Where(t => !string.IsNullOrWhiteSpace(t.Value))
                .ToDictionary(t => t.Key, t => t.Value.Replace("\r\n", "\n").Trim('\n'), StringComparer.Ordinal);
    }

    /// <summary>The default of a prompt: the one of config.yml, else the one of the code; empty for an unknown key.</summary>
    public static string Default(string key)
    {
        lock (Gate)
            return _config.TryGetValue(key, out var text) ? text : Definitions.TryGetValue(key, out var definition) ? definition.Default : string.Empty;
    }

    /// <summary>The text in use of a prompt, its placeholders left as they are.</summary>
    public static string Template(string key)
    {
        lock (Gate)
            if (Stored.TryGetValue(key, out var versions) && versions.Count > 0)
                return versions[^1].Text;
        return Default(key);
    }

    /// <summary>The prompt in use, each <c>{{name}}</c> replaced by its value.</summary>
    public static string Get(string key, params (string Name, object? Value)[] values)
    {
        var text = Template(key);
        foreach (var (name, value) in values)
            text = text.Replace("{{" + name + "}}", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal);
        return text;
    }

    /// <summary>The versions of a prompt kept in the search engine, the oldest first (empty: only its default is known).</summary>
    public static IReadOnlyList<PromptVersion> Versions(string key)
    {
        lock (Gate)
            return Stored.TryGetValue(key, out var versions) ? [.. versions] : [];
    }

    /// <summary>The number of the version in use (0: the default, the search engine has none).</summary>
    public static int CurrentVersion(string key)
    {
        lock (Gate)
            return Stored.TryGetValue(key, out var versions) && versions.Count > 0 ? versions[^1].Version : 0;
    }

    /// <summary>
    /// Reads the versions of every prompt from the search engine; the prompts it does not know are written there as
    /// their version 1, from their default. Returns what happened, as a sentence; the defaults stay in use when the
    /// server does not answer.
    /// </summary>
    public static async Task<string> LoadAsync(KaonomiAssistantService assistant, CancellationToken cancellationToken = default)
    {
        try
        {
            var versions = await assistant.PromptVersionsAsync(cancellationToken);
            lock (Gate)
            {
                Stored.Clear();
                foreach (var group in versions.Where(v => v.Key.Length > 0 && v.Version > 0).GroupBy(v => v.Key, StringComparer.Ordinal))
                    Stored[group.Key] = [.. group.GroupBy(v => v.Version).Select(g => g.First()).OrderBy(v => v.Version)];
            }
            var seeded = 0;
            foreach (var definition in All)
            {
                // A prompt whose default is empty (the texts of /emotion) has nothing to write until it is given one.
                if (CurrentVersion(definition.Key) > 0 || Default(definition.Key).Length == 0)
                    continue;
                await SaveAsync(assistant, definition.Key, Default(definition.Key), "config", "the default, written when the search engine had no version", cancellationToken, notify: false);
                seeded++;
            }
            Changed?.Invoke();
            return seeded > 0 ? $"{All.Count} prompts, {seeded} written in the search engine from their default" : $"{All.Count} prompts read from the search engine";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return $"the search engine was not reached ({ex.Message}): the defaults of config.yml are used";
        }
    }

    /// <summary>
    /// Saves <paramref name="text"/> as a new version of the prompt (the next number) in the search engine, and uses
    /// it from now on. Throws when the server refuses or does not answer: nothing changes then.
    /// </summary>
    public static async Task<PromptVersion> SaveAsync(KaonomiAssistantService assistant, string key, string text, string source, string comment,
        CancellationToken cancellationToken = default, bool notify = true)
    {
        var title = Definitions.TryGetValue(key, out var definition) ? definition.Title : key;
        var version = new PromptVersion
        {
            Key = key, Version = CurrentVersion(key) + 1, Text = text.Replace("\r\n", "\n"), Source = source, Comment = comment.Trim(), Title = title, Date = DateTime.UtcNow
        };
        version.Id = PromptVersion.IdOf(key, version.Version);
        var result = await assistant.SavePromptVersionAsync(version, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Message);
        lock (Gate)
        {
            if (!Stored.TryGetValue(key, out var versions))
                Stored[key] = versions = [];
            versions.Add(version);
        }
        if (notify)
            Changed?.Invoke();
        return version;
    }
}
