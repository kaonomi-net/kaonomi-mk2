using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>What a video is (kaonomi-media probe): its length, its size, whether macOS can read it.</summary>
public sealed class VideoProbe
{
    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("fps")]
    public double Fps { get; set; }

    [JsonPropertyName("hasVideo")]
    public bool HasVideo { get; set; }

    [JsonPropertyName("hasAudio")]
    public bool HasAudio { get; set; }

    [JsonPropertyName("playable")]
    public bool Playable { get; set; }

    /// <summary>What reads it: <c>avfoundation</c> (macOS) or <c>ffmpeg</c> (webm, mkv, avi…, with ffmpeg next to kaonomi-media).</summary>
    [JsonPropertyName("backend")]
    public string Backend { get; set; } = string.Empty;
}

/// <summary>A video of the library of /video, as the cache keeps it (~/kaonomi/video/library.json).</summary>
public sealed class VideoEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; }

    /// <summary>What kaonomi-media said of it; null until it was looked at.</summary>
    [JsonPropertyName("probe")]
    public VideoProbe? Probe { get; set; }

    /// <summary>The thumbnail made for it (empty: none yet, or none possible).</summary>
    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = string.Empty;
}

/// <summary>
/// A version of a note of /video in the search engine (index <c>videonote</c>, see KAONOMI.Core VideoNote): an analysis
/// of a segment, of a frame or of the sound, a picture kept, or an annotation. Written once; a change is a new version.
/// </summary>
public sealed class VideoNoteDocument
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

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("noteId")]
    public string NoteId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("videoKey")]
    public string VideoKey { get; set; } = string.Empty;

    [JsonPropertyName("videoPath")]
    public string VideoPath { get; set; } = string.Empty;

    [JsonPropertyName("videoName")]
    public string VideoName { get; set; } = string.Empty;

    [JsonPropertyName("videoDuration")]
    public double VideoDuration { get; set; }

    [JsonPropertyName("start")]
    public double Start { get; set; }

    [JsonPropertyName("end")]
    public double End { get; set; }

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("transcript")]
    public string Transcript { get; set; } = string.Empty;

    [JsonPropertyName("speakers")]
    public List<string> Speakers { get; set; } = [];

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("promptVersion")]
    public int PromptVersion { get; set; }

    [JsonPropertyName("picture")]
    public string Picture { get; set; } = string.Empty;

    [JsonPropertyName("pictureFile")]
    public string PictureFile { get; set; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>The project the note is attached to (the project chosen in the player), or null.</summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    /// <summary>The id of the version <paramref name="version"/> of the note <paramref name="noteId"/>.</summary>
    public static string IdOf(string noteId, int version) => $"videonote-{noteId}-v{version}";

    /// <summary>A copy of this version, as the next one (its id, its date and its operation to set).</summary>
    public VideoNoteDocument Next(string operation, int version) => new()
    {
        NoteId = NoteId, Version = version, Id = IdOf(NoteId, version), Kind = Kind, Operation = operation, VideoKey = VideoKey,
        VideoPath = VideoPath, VideoName = VideoName, VideoDuration = VideoDuration, Start = Start, End = End, Question = Question,
        Text = Text, Transcript = Transcript, Speakers = [.. Speakers], Model = Model, PromptVersion = PromptVersion, Picture = Picture,
        PictureFile = PictureFile, DurationMs = DurationMs, Title = Title, Comment = Comment, IsDeleted = false, Date = DateTime.UtcNow, ProjectId = ProjectId
    };
}
