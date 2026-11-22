using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KAONOMI.Chat.Models;

/// <summary>
/// A picture of a message of <c>/message</c>: its file name, its kind (image/png, image/jpeg, image/webp) and its
/// bytes in base 64. <see cref="Id"/> is what the text of the message names where the picture stands
/// (<c>[[image:3f9a]]</c>).
/// </summary>
public sealed class MailPicture
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("mime")] public string Mime { get; set; } = "image/png";
    [JsonPropertyName("data")] public string Data { get; set; } = string.Empty;

    /// <summary>What stands for the picture in the text of a message.</summary>
    [JsonIgnore] public string Marker => $"[[image:{Id}]]";

    /// <summary>The size of the picture, in bytes (three bytes for four characters of base 64).</summary>
    [JsonIgnore] public long Size => Data.Length * 3L / 4;
}

/// <summary>
/// An entity the NER of the server found in a message when it was sent: its name (the one of the ontology when it is
/// known there, <see cref="Known"/>), its type (PERSON, ORGANISATION, CITY, DATE, EMAIL, MONEY…) and how many times
/// the message names it.
/// </summary>
public sealed class MailEntity
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
    [JsonPropertyName("known")] public bool Known { get; set; }
}

/// <summary>
/// A note a user wrote on a part of a message: on some text of it (<see cref="Quote"/>, what was selected), on one of
/// its pictures (<see cref="PictureId"/>), or on both. It is its author's alone (<see cref="By"/>): the other users
/// of the message do not see it.
/// </summary>
public sealed class MailAnnotation
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("by")] public string By { get; set; } = string.Empty;
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    /// <summary>The text of the message the note is about, as it was selected (empty: a picture only).</summary>
    [JsonPropertyName("quote")] public string Quote { get; set; } = string.Empty;
    /// <summary>The picture of the message the note is about (null: some text only).</summary>
    [JsonPropertyName("pictureId")] public string? PictureId { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;
}

/// <summary>
/// What a message says: its text (with the places of its pictures), its pictures, and the entities found in its
/// subject and its text when it was sent. What AES-256 hides of an encrypted message (its entities would tell what it
/// is about).
/// </summary>
public sealed class MailContent
{
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("pictures")] public List<MailPicture> Pictures { get; set; } = [];
    [JsonPropertyName("entities")] public List<MailEntity> Entities { get; set; } = [];
    /// <summary>The notes the users wrote on parts of the message (those of an encrypted message are encrypted with it: they quote it).</summary>
    [JsonPropertyName("annotations")] public List<MailAnnotation> Annotations { get; set; } = [];
}

/// <summary>A part of the text of a message, in its order: some text, or a picture (then <see cref="Picture"/> is not null).</summary>
public sealed record MailPart(string Text, MailPicture? Picture);

/// <summary>
/// A message of <c>/message</c>, the internal mail of the application: from a user to users (their logins), a subject,
/// a text with pictures, a date. Kept as one JSON file (<see cref="Services.MailStore"/>).
/// <para>
/// Encrypted (<see cref="Encrypted"/>): the text and the pictures are not in <see cref="Text"/> and
/// <see cref="Pictures"/> (both empty) but in <see cref="Cipher"/>, AES-256-GCM under a password the sender and the
/// receivers share (<see cref="Services.JournalCrypto"/>); the subject, the sender, the receivers and the date stay
/// readable, as the envelope of a letter.
/// </para>
/// <para>
/// Each user has its own view of the message: read or not (<see cref="ReadBy"/>), in its trash
/// (<see cref="TrashedBy"/>), removed from it (<see cref="RemovedBy"/>: the file goes once everyone removed it).
/// </para>
/// </summary>
public sealed class MailMessage
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
    [JsonPropertyName("to")] public List<string> To { get; set; } = [];
    [JsonPropertyName("subject")] public string Subject { get; set; } = string.Empty;
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    /// <summary>True while it is being written (seen by its author only, in Drafts).</summary>
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    /// <summary>The message it answers or forwards, when there is one.</summary>
    [JsonPropertyName("replyTo")] public string? ReplyTo { get; set; }
    [JsonPropertyName("encrypted")] public bool Encrypted { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("pictures")] public List<MailPicture> Pictures { get; set; } = [];
    /// <summary>The entities found by the NER when the message was sent (those of an encrypted message are in <see cref="Cipher"/>).</summary>
    [JsonPropertyName("entities")] public List<MailEntity> Entities { get; set; } = [];
    /// <summary>The notes the users wrote on parts of the message (those of an encrypted message are in <see cref="Cipher"/>).</summary>
    [JsonPropertyName("annotations")] public List<MailAnnotation> Annotations { get; set; } = [];
    /// <summary>The text, the pictures, the entities and the notes of an encrypted message (**CRYPTED**AES-256-GCM:v1:…).</summary>
    [JsonPropertyName("cipher")] public string Cipher { get; set; } = string.Empty;
    /// <summary>How many pictures an encrypted message holds (told on its envelope).</summary>
    [JsonPropertyName("pictureCount")] public int PictureCount { get; set; }
    [JsonPropertyName("readBy")] public List<string> ReadBy { get; set; } = [];
    [JsonPropertyName("trashedBy")] public List<string> TrashedBy { get; set; } = [];
    [JsonPropertyName("removedBy")] public List<string> RemovedBy { get; set; } = [];

    private static bool Holds(List<string> logins, string login) => logins.Any(l => l.Equals(login, StringComparison.OrdinalIgnoreCase));

    public bool IsFrom(string login) => From.Equals(login, StringComparison.OrdinalIgnoreCase);
    public bool IsTo(string login) => Holds(To, login);
    public bool IsReadBy(string login) => Holds(ReadBy, login);
    public bool IsTrashedBy(string login) => Holds(TrashedBy, login);
    public bool IsRemovedBy(string login) => Holds(RemovedBy, login);

    /// <summary>Everyone the message concerns: its author and its receivers, each once.</summary>
    public IEnumerable<string> Everyone() => To.Prepend(From).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The content of a message that is not encrypted.</summary>
    public MailContent Content() => new() { Text = Text, Pictures = Pictures, Entities = Entities, Annotations = Annotations };

    /// <summary>How many pictures the message holds, encrypted or not.</summary>
    [JsonIgnore] public int Pictured => Encrypted ? PictureCount : Pictures.Count;

    private static readonly Regex Marker = new(@"\[\[image:([0-9a-f]{4,16})\]\]", RegexOptions.Compiled);

    /// <summary>
    /// <paramref name="content"/> cut where its pictures stand: the text, a picture, the text that follows… A picture
    /// the text does not name comes at the end; a mark without its picture stays as text.
    /// </summary>
    public static List<MailPart> Parts(MailContent content)
    {
        var parts = new List<MailPart>();
        var shown = new HashSet<string>();
        var at = 0;
        foreach (Match match in Marker.Matches(content.Text))
        {
            if (content.Pictures.FirstOrDefault(p => p.Id == match.Groups[1].Value) is not { } picture)
                continue;
            if (match.Index > at && content.Text[at..match.Index].Trim().Length > 0)
                parts.Add(new MailPart(content.Text[at..match.Index].Trim('\n', '\r'), null));
            parts.Add(new MailPart(string.Empty, picture));
            shown.Add(picture.Id);
            at = match.Index + match.Length;
        }
        if (at < content.Text.Length && content.Text[at..].Trim().Length > 0)
            parts.Add(new MailPart(content.Text[at..].Trim('\n', '\r'), null));
        foreach (var picture in content.Pictures.Where(p => !shown.Contains(p.Id)))
            parts.Add(new MailPart(string.Empty, picture));
        return parts;
    }

    /// <summary>The text without the marks of its pictures (a preview, a quote).</summary>
    public static string WithoutMarks(string text) => Marker.Replace(text, string.Empty);

    /// <summary>
    /// What the NER reads of a message: its subject and what its author wrote, without the marks of the pictures nor
    /// what is quoted of the message answered or forwarded (the lines after « --- date, name: --- »).
    /// </summary>
    public static string ForEntities(string subject, string text)
    {
        var lines = WithoutMarks(text).ReplaceLineEndings("\n").Split('\n');
        var own = lines.TakeWhile(l => !(l.StartsWith("--- ", StringComparison.Ordinal) && l.TrimEnd().EndsWith("---", StringComparison.Ordinal)))
            .Where(l => !l.StartsWith('>'));
        return (subject.Trim() + "\n" + string.Join('\n', own)).Trim();
    }

    /// <summary>
    /// The entities of a message from what the NER found, each once (by its name and its type, whatever the case) with
    /// how many times it is named, under its name in the ontology when it is known there; by type, then by name.
    /// </summary>
    public static List<MailEntity> EntitiesOf(IEnumerable<Services.NerEntityHit> hits) =>
        [.. hits
            .Select(h => (Name: string.Join(' ', (h.OntologyName ?? h.Text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), Type: h.Type.Trim().ToUpperInvariant(), Known: h.OntologyName is not null))
            .Where(h => h.Name.Length > 1 && h.Type.Length > 0)
            .GroupBy(h => (h.Name.ToUpperInvariant(), h.Type))
            .Select(g => new MailEntity { Name = g.First().Name, Type = g.Key.Type, Count = g.Count(), Known = g.Any(h => h.Known) })
            .OrderBy(e => e.Type, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// A message of <c>/message</c> as the search engine keeps it (<c>Mail</c> on the server, index <c>mail</c>): what its
/// receivers read, for the searches. Written by <see cref="Services.MailIndex"/>; the message itself stays its file.
/// An encrypted message gives its envelope only (sender, receivers, subject, date).
/// </summary>
public sealed class MailDocument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("appName")] public string? AppName { get; set; }
    [JsonPropertyName("machineName")] public string? MachineName { get; set; }
    [JsonPropertyName("userName")] public string? UserName { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("from")] public string From { get; set; } = string.Empty;
    [JsonPropertyName("fromName")] public string FromName { get; set; } = string.Empty;
    [JsonPropertyName("to")] public List<string> To { get; set; } = [];
    [JsonPropertyName("toNames")] public List<string> ToNames { get; set; } = [];
    [JsonPropertyName("subject")] public string Subject { get; set; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("encrypted")] public bool Encrypted { get; set; }
    [JsonPropertyName("pictureCount")] public int PictureCount { get; set; }
    [JsonPropertyName("pictureNames")] public List<string> PictureNames { get; set; } = [];
    [JsonPropertyName("entities")] public List<string> Entities { get; set; } = [];
    [JsonPropertyName("replyTo")] public string? ReplyTo { get; set; }

    /// <summary>True when the server marked it deleted; left out of the body when null.</summary>
    [JsonPropertyName("isDeleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsDeleted { get; set; }
}

/// <summary>
/// An item of a tool of the chat whose store is the search engine (<c>SyncedRecord</c> on the server, one index per
/// tool: calendarevent, archiitem, daytodo, supportticket, vaultnote, entityanalysis), kept on the machine as a copy by
/// <see cref="Services.LocalSync"/>: what the searches read (title, text, type) and the item itself (its JSON in base 64).
/// </summary>
public sealed class SyncedRecordEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("appName")] public string? AppName { get; set; }
    [JsonPropertyName("machineName")] public string? MachineName { get; set; }
    [JsonPropertyName("userName")] public string? UserName { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("payload")] public string Payload { get; set; } = string.Empty;
    [JsonPropertyName("revision")] public string Revision { get; set; } = string.Empty;
    [JsonPropertyName("isDeleted")] public bool IsDeleted { get; set; }
}
