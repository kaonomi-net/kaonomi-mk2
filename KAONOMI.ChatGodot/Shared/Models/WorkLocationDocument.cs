using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>
/// A place where the user works, measured by SET LOCATION in /vision, as its document in the search engine of
/// the server (<c>WorkLocation</c>, index <c>worklocation</c>): the size of the box of the face the camera saw and the
/// level of the voice the microphone heard while the user sat in the usual working position and spoke. Its id comes
/// from the name (<see cref="IdOf"/>): measured again, the place is written over.
/// </summary>
public sealed class WorkLocationDocument
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

    /// <summary>The name of the place, as typed.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The area of the box of the face, as a share of the frame (0 to 1): its mean over the measure.</summary>
    [JsonPropertyName("faceArea")]
    public double FaceArea { get; set; }

    [JsonPropertyName("faceWidth")]
    public int FaceWidth { get; set; }

    [JsonPropertyName("faceHeight")]
    public int FaceHeight { get; set; }

    [JsonPropertyName("frameWidth")]
    public int FrameWidth { get; set; }

    [JsonPropertyName("frameHeight")]
    public int FrameHeight { get; set; }

    [JsonPropertyName("faceFrames")]
    public int FaceFrames { get; set; }

    /// <summary>The level of the voice: the root mean square of the samples heard (0 to 1), and in dB.</summary>
    [JsonPropertyName("voiceLevel")]
    public double VoiceLevel { get; set; }

    [JsonPropertyName("voiceDecibels")]
    public double VoiceDecibels { get; set; }

    [JsonPropertyName("seconds")]
    public double Seconds { get; set; }

    [JsonPropertyName("camera")]
    public string Camera { get; set; } = string.Empty;

    [JsonPropertyName("microphone")]
    public string Microphone { get; set; } = string.Empty;

    /// <summary>What the vision model saw in a picture of the camera taken at the end of the measure, and the model.</summary>
    [JsonPropertyName("analysis")]
    public string Analysis { get; set; } = string.Empty;

    [JsonPropertyName("analysisModel")]
    public string AnalysisModel { get; set; } = string.Empty;

    /// <summary>The entities the NER found in <see cref="Analysis"/>.</summary>
    [JsonPropertyName("entities")]
    public List<string> Entities { get; set; } = [];

    /// <summary>The picture analysed, on this machine.</summary>
    [JsonPropertyName("picture")]
    public string Picture { get; set; } = string.Empty;

    /// <summary>The id of the place of this name: <c>worklocation-</c> and the name in lower case, letters and digits only.</summary>
    public static string IdOf(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return "worklocation-" + (slug.Length > 0 ? slug : "place");
    }
}
