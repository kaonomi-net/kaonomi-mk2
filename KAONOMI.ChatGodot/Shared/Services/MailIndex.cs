using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>
/// Keeps the messages of <c>/message</c> in the search engine of the server (index <c>mail</c>, <c>PUT /mail</c>), so
/// that <c>/search</c> finds them: a message is written there when it is sent (or changed), marked deleted there when
/// its file goes (every user removed it), and the whole mail is compared with the server when the chat starts, then
/// every 10 minutes and when <c>/message</c> opens: the messages missing there (the ones of before, the ones sent while
/// the server did not run) are written. Only what the receivers read: never the drafts nor the notes of the users;
/// an encrypted message gives its envelope only (sender, receivers, subject, date). One write at a time; nothing is
/// done while the server does not answer (told in the log once, done at the next comparison).
/// </summary>
public static partial class MailIndex
{
    private static readonly TimeSpan AutoEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FirstAfter = TimeSpan.FromSeconds(30);
    /// <summary>The most messages asked to the server at once (the limit of the search engine).</summary>
    private const int MaxItems = 10000;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    /// <summary>What the server holds of each message (its signature), as far as this chat knows.</summary>
    private static readonly ConcurrentDictionary<string, string> Indexed = new(StringComparer.Ordinal);
    private static KaonomiAssistantService? _server;
    private static DateTime _lastWhole = DateTime.MinValue;
    private static bool _listening;
    private static bool _toldOffline;

    [GeneratedRegex(@"\[\[image:[^\]]*\]\]")]
    private static partial Regex PictureMarker();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Spaces();

    /// <summary>
    /// Starts following the mail (once) with the server of <paramref name="server"/>: each message written or deleted,
    /// and the whole mail compared after 30 s, then every 10 minutes (a minute after a failure).
    /// </summary>
    public static void Start(KaonomiAssistantService server)
    {
        _server = server;
        if (_listening)
            return;
        _listening = true;
        MailStore.Written += OnWritten;
        MailStore.Deleted += OnDeleted;
        _ = Task.Run(async () =>
        {
            await Task.Delay(FirstAfter);
            while (true)
            {
                // Every 10 minutes (SyncAsync says when), a minute after a comparison that failed.
                await SyncAsync(manual: false);
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        });
    }

    /// <summary>The message as the search engine keeps it: what its receivers read, its pictures and its notes left out.</summary>
    public static MailDocument ToDocument(MailMessage message, IReadOnlyDictionary<string, string>? names = null)
    {
        names ??= UserStore.All().GroupBy(u => u.Login, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        string NameOf(string login) => names.TryGetValue(login, out var name) && !string.IsNullOrWhiteSpace(name) ? name : login;
        var subject = message.Subject.Trim();
        return new MailDocument
        {
            Id = message.Id,
            Date = message.Date,
            AppName = "KAONOMI.Chat",
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            Title = subject.Length > 0 ? subject : "(no subject)",
            From = message.From,
            FromName = NameOf(message.From),
            To = Clean(message.To),
            ToNames = Clean(message.To.Select(NameOf)),
            Subject = subject,
            Encrypted = message.Encrypted,
            Text = message.Encrypted ? string.Empty : Spaces().Replace(PictureMarker().Replace(message.Text, " "), " ").Replace("\r\n", "\n").Trim(),
            PictureCount = message.Encrypted ? message.PictureCount : message.Pictures.Count,
            PictureNames = message.Encrypted ? [] : Clean(message.Pictures.Select(p => p.Name)),
            Entities = message.Encrypted ? [] : Clean(message.Entities.Select(e => string.IsNullOrWhiteSpace(e.Type) ? e.Name : $"{e.Name} ({e.Type})")),
            ReplyTo = string.IsNullOrWhiteSpace(message.ReplyTo) ? null : message.ReplyTo
        };
    }

    /// <summary>What a message holds for the search engine: two equal signatures, nothing to write.</summary>
    public static string Signature(MailDocument d) => string.Join('\u001f',
        d.From, d.FromName, string.Join('\u001e', d.To), string.Join('\u001e', d.ToNames), d.Subject, d.Text.Replace("\r\n", "\n").Trim(),
        d.Encrypted, d.PictureCount, string.Join('\u001e', d.PictureNames), string.Join('\u001e', d.Entities), d.ReplyTo ?? string.Empty,
        d.Date.ToUnixTimeSeconds());

    private static List<string> Clean(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList();

    private static void OnWritten(MailMessage message)
    {
        if (message.Draft || _server is null)
            return;
        // Read now: the message may change after (the next read of its file).
        var document = ToDocument(message);
        var signature = Signature(document);
        if (Indexed.TryGetValue(document.Id, out var known) && known == signature)
            return;
        _ = Task.Run(() => WriteAsync(document, signature));
    }

    private static void OnDeleted(string id)
    {
        if (_server is null)
            return;
        _ = Task.Run(async () =>
        {
            await Gate.WaitAsync();
            try
            {
                await _server.DeleteMailAsync(id);
                Indexed.TryRemove(id, out _);
                ActivityLog.Info("Message", $"Message {id} removed by everyone: marked deleted in the search engine");
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                Offline(ex);
            }
            finally
            {
                Gate.Release();
            }
        });
    }

    private static async Task WriteAsync(MailDocument document, string signature)
    {
        await Gate.WaitAsync();
        try
        {
            if (Indexed.TryGetValue(document.Id, out var known) && known == signature)
                return;
            var result = await _server!.SaveMailAsync(document);
            if (result.IsSuccess)
            {
                Indexed[document.Id] = signature;
                _toldOffline = false;
                ActivityLog.Info("Message", $"Message « {document.Title} » written to the search engine");
            }
            else
                ActivityLog.Error("Message", $"Message {document.Id} not written to the search engine: {result.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            Offline(ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void Offline(Exception ex)
    {
        if (_toldOffline)
            return;
        _toldOffline = true;
        ActivityLog.Info("Message", $"The search engine does not take the messages now ({ex.Message}): they are written at the next comparison");
    }

    /// <summary>
    /// Compares the mail of this machine with the search engine: the messages missing there or changed are written, the
    /// ones of this machine whose file is gone are marked deleted (those of another machine are left alone). What it
    /// did, or why not (null: not done, the last one is too recent, unless <paramref name="manual"/>).
    /// </summary>
    public static async Task<string?> SyncAsync(bool manual, CancellationToken ct = default)
    {
        if (_server is not { } server)
            return manual ? "The mail is not followed" : null;
        if (!manual && DateTime.UtcNow - _lastWhole < AutoEvery - TimeSpan.FromSeconds(5))
            return null;
        await Gate.WaitAsync(ct);
        try
        {
            _lastWhole = DateTime.UtcNow;
            var onServer = (await server.GetLastMailsAsync(MaxItems, ct)).Where(d => d.IsDeleted != true && d.Id.Length > 0)
                .GroupBy(d => d.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var names = UserStore.All().GroupBy(u => u.Login, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);
            var local = MailStore.All().Where(m => !m.Draft).ToList();
            int written = 0, failed = 0, removed = 0;
            foreach (var message in local)
            {
                ct.ThrowIfCancellationRequested();
                var document = ToDocument(message, names);
                var signature = Signature(document);
                if (onServer.TryGetValue(document.Id, out var there) && Signature(there) == signature)
                {
                    Indexed[document.Id] = signature;
                    continue;
                }
                var result = await server.SaveMailAsync(document, ct);
                if (result.IsSuccess)
                {
                    Indexed[document.Id] = signature;
                    written++;
                }
                else
                {
                    failed++;
                    ActivityLog.Error("Message", $"Message {document.Id} not written to the search engine: {result.Message}");
                }
            }
            // The messages of this machine gone from it (removed by everyone while the chat did not run). Not when the
            // server gave as many as asked: the list may be cut.
            if (onServer.Count < MaxItems)
            {
                var files = local.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var gone in onServer.Values.Where(d => !files.Contains(d.Id)
                             && string.Equals(d.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                             && MailStore.Get(d.Id) is null))
                {
                    ct.ThrowIfCancellationRequested();
                    if ((await server.DeleteMailAsync(gone.Id, ct)).IsSuccess)
                    {
                        Indexed.TryRemove(gone.Id, out _);
                        removed++;
                    }
                }
            }
            _toldOffline = false;
            var summary = $"{local.Count} message(s) of this machine: {written} written to the search engine, {removed} marked deleted"
                          + (failed > 0 ? $", {failed} refused (see the log)" : string.Empty)
                          + (local.Count - written - failed > 0 ? $", {local.Count - written - failed} already there" : string.Empty);
            if (manual || written + removed + failed > 0)
                ActivityLog.Info("Message", "Mail compared with the search engine: " + summary);
            return summary;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            // Tried again at the next comparison (not 10 minutes later when it failed: in a minute).
            _lastWhole = DateTime.UtcNow - AutoEvery + TimeSpan.FromMinutes(1);
            Offline(ex);
            return manual ? $"The mail was not compared with the search engine: {ex.Message}" : null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
