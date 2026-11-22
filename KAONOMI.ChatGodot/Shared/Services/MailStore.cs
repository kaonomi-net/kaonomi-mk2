using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>The folders of <c>/message</c>, each seen by the user logged in.</summary>
public enum MailFolder
{
    Inbox,
    Sent,
    Drafts,
    Trash
}

/// <summary>
/// The messages of <c>/message</c>, the internal mail between the users of the application: one JSON file each, in
/// the folder <c>messages</c> beside the file of the users (<c>~/kaonomi/messages</c>; with <c>--users-file=</c>,
/// beside that file: a test has its own mail as it has its own users). There is no e-mail behind it: the files are the
/// messages, for the account that runs the chat only (as the file of the users); <see cref="MailIndex"/> writes what
/// the receivers read of the messages sent to the search engine of the server, for the searches.
/// </summary>
public static class MailStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>The largest picture a message takes, in bytes (its file).</summary>
    public const long MaxPictureBytes = 8 * 1024 * 1024;

    /// <summary>The kinds of pictures a message takes, by extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> PictureKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".webp"] = "image/webp"
    };

    /// <summary>Raised when a message is written (sent, saved as a draft, read, trashed…), out of the lock of the files.</summary>
    public static event Action<MailMessage>? Written;

    /// <summary>Raised when the file of a message is deleted (everyone removed it).</summary>
    public static event Action<string>? Deleted;

    /// <summary>The folder of the messages.</summary>
    public static string Folder => Path.Combine(Path.GetDirectoryName(UserStore.FilePath)!, "messages");

    private static string FileOf(string id) => Path.Combine(Folder, Path.GetFileName(id) + ".json");

    /// <summary>A new identifier: the date and a random part, so that the files sort by date.</summary>
    public static string NewId() => $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Every message that can be read, the latest first (a file that cannot be read is skipped, and told in the log).</summary>
    public static List<MailMessage> All()
    {
        var messages = new List<MailMessage>();
        lock (Gate)
        {
            if (!Directory.Exists(Folder))
                return messages;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<MailMessage>(File.ReadAllText(file), Json) is { Id.Length: > 0 } message)
                        messages.Add(message);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    ActivityLog.Error("Message", $"The message {Path.GetFileName(file)} cannot be read", ex);
                }
            }
        }
        return [.. messages.OrderByDescending(m => m.Date)];
    }

    /// <summary>
    /// What changes when a message is written, changed or deleted (how many files, the date of the last one written):
    /// compared from time to time, it tells that the mail is to be read again.
    /// </summary>
    public static long Stamp()
    {
        lock (Gate)
        {
            try
            {
                if (!Directory.Exists(Folder))
                    return 0;
                long stamp = 0;
                foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("*.json"))
                    stamp = unchecked(stamp * 31 + file.LastWriteTimeUtc.Ticks + file.Length);
                return stamp;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }

    /// <summary>The message <paramref name="id"/>, read again from its file (null: gone).</summary>
    public static MailMessage? Get(string id)
    {
        lock (Gate)
        {
            try
            {
                return File.Exists(FileOf(id)) ? JsonSerializer.Deserialize<MailMessage>(File.ReadAllText(FileOf(id)), Json) : null;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Writes <paramref name="message"/> (a new one gets its identifier).</summary>
    public static void Save(MailMessage message)
    {
        lock (Gate)
        {
            if (message.Id.Length == 0)
                message.Id = NewId();
            Directory.CreateDirectory(Folder);
            // Written beside, then put in place: a stop in the middle never leaves half a file.
            var temporary = FileOf(message.Id) + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(message, Json));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, FileOf(message.Id), overwrite: true);
        }
        Written?.Invoke(message);
    }

    /// <summary>Deletes the file of the message <paramref name="id"/>.</summary>
    public static void Delete(string id)
    {
        lock (Gate)
        {
            if (!File.Exists(FileOf(id)))
                return;
            File.Delete(FileOf(id));
        }
        Deleted?.Invoke(id);
    }

    /// <summary>
    /// Changes the message <paramref name="id"/> as it is in its file now (another user may have read or deleted it
    /// meanwhile) and writes it back; returns it, or null when it is gone. A message everyone removed is deleted.
    /// </summary>
    public static MailMessage? Update(string id, Action<MailMessage> change)
    {
        lock (Gate)
        {
            if (Get(id) is not { } message)
                return null;
            change(message);
            // A draft is its author's alone; a user that is gone has nothing to remove.
            var known = UserStore.All().Select(u => u.Login).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var concerned = message.Draft ? [message.From] : message.Everyone().Where(known.Contains).ToList();
            if (concerned.All(message.IsRemovedBy))
            {
                Delete(id);
                return null;
            }
            Save(message);
            return message;
        }
    }

    /// <summary>True when <paramref name="message"/> is in <paramref name="folder"/> for <paramref name="login"/>.</summary>
    public static bool IsIn(MailMessage message, MailFolder folder, string login)
    {
        if (message.IsRemovedBy(login))
            return false;
        var mine = message.Draft ? message.IsFrom(login) : message.IsFrom(login) || message.IsTo(login);
        if (!mine)
            return false;
        if (message.IsTrashedBy(login))
            return folder == MailFolder.Trash;
        return folder switch
        {
            MailFolder.Inbox => !message.Draft && message.IsTo(login),
            MailFolder.Sent => !message.Draft && message.IsFrom(login),
            MailFolder.Drafts => message.Draft,
            _ => false
        };
    }

    /// <summary>How many messages of its inbox <paramref name="login"/> has not read.</summary>
    public static int Unread(string login) => All().Count(m => IsIn(m, MailFolder.Inbox, login) && !m.IsReadBy(login));

    /// <summary>
    /// The picture of <paramref name="file"/> for a message (its bytes in base 64); null with the reason when it is not
    /// a PNG, JPEG or WebP picture, is too large or cannot be read.
    /// </summary>
    public static (MailPicture? Picture, string? Error) ReadPicture(string file)
    {
        if (!PictureKinds.TryGetValue(Path.GetExtension(file), out var mime))
            return (null, $"{Path.GetFileName(file)}: only PNG, JPEG and WebP pictures can be inserted");
        try
        {
            var info = new FileInfo(file);
            if (info.Length > MaxPictureBytes)
                return (null, $"{info.Name} is too large ({info.Length / 1048576.0:0.0} MB; {MaxPictureBytes / 1048576} MB at most)");
            if (info.Length == 0)
                return (null, $"{info.Name} is empty");
            return (new MailPicture
            {
                Id = Guid.NewGuid().ToString("N")[..6],
                Name = info.Name,
                Mime = mime,
                Data = Convert.ToBase64String(File.ReadAllBytes(file))
            }, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"{Path.GetFileName(file)}: {ex.Message}");
        }
    }

    /// <summary>The content of a message as AES-256 hides it (<see cref="JournalCrypto"/>: slow on purpose, run it off the UI thread).</summary>
    public static string Encrypt(MailContent content, string password) => JournalCrypto.Encrypt(JsonSerializer.Serialize(content), password);

    /// <summary>
    /// The content of an encrypted message; throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// when the password is wrong (or the message was changed).
    /// </summary>
    public static MailContent Decrypt(string cipher, string password) =>
        JsonSerializer.Deserialize<MailContent>(JournalCrypto.Decrypt(cipher, password), Json) ?? new MailContent();
}
