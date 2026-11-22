using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>
/// A face enrolled by /vision, as its document in the search engine of the server (<c>Face</c>, index
/// <c>face</c>): one per picture kept. Its id comes from the name and the file (<see cref="IdOf"/>), so the same
/// picture is always the same document.
/// </summary>
public sealed class FaceDocument
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

    /// <summary>The name the face was enrolled under.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The name of the picture in the folder of the name.</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>The picture: the face aligned (112 x 112), a JPEG in base 64.</summary>
    [JsonPropertyName("picture")]
    public string Picture { get; set; } = string.Empty;

    /// <summary>What the recognition model says of the face (128 numbers).</summary>
    [JsonPropertyName("feature")]
    public List<double> Feature { get; set; } = [];

    [JsonPropertyName("model")]
    public string Model { get; set; } = "face_recognition_sface_2021dec";

    /// <summary>The id of the document of this picture of this name: letters, digits, - and _ only.</summary>
    public static string IdOf(string name, string file)
    {
        var text = $"face-{name}-{System.IO.Path.GetFileNameWithoutExtension(file)}";
        return string.Concat(System.Linq.Enumerable.Select(text, c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
    }
}
