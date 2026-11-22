using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>
/// A sample of the voice of someone, enrolled in /recorder (who speaks): what the speaker model says of it (its
/// embedding), not the sound. It is a JSON file of <c>~/kaonomi/audio/speakers/&lt;name&gt;/</c> and, the same, a
/// document of the search engine of the server (<c>Voice</c>, index <c>voice</c>). Its id comes from the name and
/// the file (<see cref="IdOf"/>), so the same sample is always the same document.
/// </summary>
public sealed class VoiceDocument
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

    /// <summary>The name the voice was enrolled under.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The name of this file in the folder of the name.</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>How long the sample was, in seconds.</summary>
    [JsonPropertyName("seconds")]
    public double Seconds { get; set; }

    /// <summary>Where the sample came from: the microphone, or a recording.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>The language spoken in the sample (FR, EN…): the one of the chat when it was enrolled; empty when unknown.</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    /// <summary>The embedding of the voice, of length 1.</summary>
    [JsonPropertyName("feature")]
    public List<double> Feature { get; set; } = [];

    /// <summary>The speaker model the embedding comes from.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>The id of the document of this sample of this name: letters, digits, - and _ only.</summary>
    public static string IdOf(string name, string file)
    {
        var text = $"voice-{name}-{System.IO.Path.GetFileNameWithoutExtension(file)}";
        return string.Concat(System.Linq.Enumerable.Select(text, c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
    }
}
