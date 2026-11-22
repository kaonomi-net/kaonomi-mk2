using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A folder of <c>/message</c> in the list on the left: its name with what it holds (« 📥 Inbox (2) »: two not read).</summary>
public sealed partial class MailFolderViewModel(MailFolder folder) : ObservableObject
{
    public MailFolder Folder { get; } = folder;

    [ObservableProperty]
    private string _label = folder.ToString();
}

/// <summary>
/// A user that may receive the message being written: its login, its name as shown (first and last names, else the
/// login), its level, and what its profile says (its e-mail, its hashtags, who it is), which the search reads.
/// </summary>
public sealed record MailUser(string Login, string Name, string Role, string Email, string Hashtags, string Profile, bool IsMe);

/// <summary>Who a text of receivers names: the logins of the users found, and what names no user (or more than one).</summary>
public sealed record MailReceivers(IReadOnlyList<string> Logins, IReadOnlyList<string> Unknown);

/// <summary>A message in the list of a folder: who, its subject, its date, what it holds, read or not.</summary>
public sealed partial class MailRowViewModel : ObservableObject
{
    public MailRowViewModel(MailMessage message, string who, bool unread, int notes = 0)
    {
        Message = message;
        Who = who;
        Notes = notes;
        _isUnread = unread;
    }

    /// <summary>How many notes the user logged in wrote on the message (those of an encrypted message are not counted: they are in its cipher).</summary>
    public int Notes { get; }

    public MailMessage Message { get; }

    /// <summary>The sender (in the inbox, the trash) or « To: … » (sent, drafts).</summary>
    public string Who { get; }

    public string Subject => Message.Subject.Length > 0 ? Message.Subject : "(no subject)";

    public string DateLabel => Message.Date.LocalDateTime.Date == DateTime.Today
        ? Message.Date.LocalDateTime.ToString("HH:mm")
        : Message.Date.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

    /// <summary>What the message holds: 🔒 encrypted, 🖼 its pictures, ✎ the notes of the user on it.</summary>
    public string Marks => string.Join("  ", new[] { Message.Encrypted ? "🔒" : null, Message.Pictured > 0 ? $"🖼 {Message.Pictured}" : null, Notes > 0 ? $"✎ {Notes}" : null }.Where(m => m is not null));

    /// <summary>The start of the text (« encrypted » when AES-256 hides it).</summary>
    public string Preview => Message.Encrypted
        ? "Encrypted (AES-256): its password opens it"
        : MailMessage.WithoutMarks(Message.Text).ReplaceLineEndings(" ").Trim() is { Length: > 140 } text ? text[..140] + "…" : MailMessage.WithoutMarks(Message.Text).ReplaceLineEndings(" ").Trim();

    [ObservableProperty]
    private bool _isUnread;
}

/// <summary>What the model of the chat is asked about the message selected.</summary>
public enum MailInsight
{
    /// <summary>ANALYSE: the conversation with its sender.</summary>
    Conversation,
    /// <summary>I'M ASPERGER: the message itself, explained.</summary>
    Explained
}

/// <summary>
/// <c>/message</c> (or <c>/msg</c>): the internal mail of the application, a small Outlook without e-mail. The users
/// are those of the application (<see cref="UserStore"/>); the messages are files of the machine
/// (<see cref="MailStore"/>). On the left the folders of the user logged in (Inbox with what is not read, Sent,
/// Drafts, Trash), then the messages of the folder (searched by <see cref="Term"/>), then the message selected, or the
/// one being written.
/// <para>
/// A message: receivers typed in <see cref="ComposeTo"/> (logins or names separated by commas, completed while they
/// are typed by <see cref="SuggestTo"/>, or picked in the search of the users, <see cref="SearchUsers"/>), a subject, a text, pictures inserted in the text (kept in base 64 in
/// the message, <c>[[image:id]]</c> where each stands). Encrypted when asked: the text and the pictures under AES-256
/// (GCM, the key from a password of 12 characters at least, shared with the receivers by another way); the subject,
/// the sender, the receivers and the date stay readable. A message encrypted is opened with its password, each time.
/// </para>
/// <para>
/// The entities of each message sent: when SEND is pressed, the NER of the Kaonomi server (<c>POST /ner</c>) reads the
/// subject and the text (not what is quoted of the message answered) and what it finds is kept with the message: the
/// persons, the organisations, the places, the dates, the e-mails, the amounts…, each with its type and how many times
/// it is named, under its name in the ontology when it is known there. They are shown with the message and searched
/// with it. Those of an encrypted message are encrypted with it (they would tell what it is about). The message is
/// sent without them when the server does not answer within <see cref="NerTimeout"/>.
/// </para>
/// <para>
/// ANALYSE: the last messages exchanged with the sender of the message selected (with its receiver when the message
/// is one of mine; <see cref="AnalysisMessages"/> at most, both ways, the oldest first) are given to the model of the
/// chat (<c>message.analyze</c> of /prompts), which tells the subjects, how the conversation moves on the substance,
/// its emotional side and what to remember. An encrypted message is not read (only its envelope is told), even one
/// that is open. The analysis is shown in place of the message; it is not kept.
/// </para>
/// <para>
/// I'M ASPERGER: the message selected, one that was received, is given to the model of the chat
/// (<c>message.asperger</c> of /prompts), which explains it to a reader with an Asperger profile: what it says
/// literally, what is implied, the emotions of its sender with the words that show them, what is expected, how to
/// answer. The earlier messages exchanged with the sender (<see cref="ExplainContextMessages"/> at most, none that is
/// encrypted) are given as the context. An encrypted message is explained once its password opened it (its text is
/// then given to the model). Shown in place of the message, as the analysis; not kept.
/// </para>
/// <para>
/// ASPERGER TO MOLDU, the other way round, in the form of a message being written: its author having an Asperger
/// profile, the model of the chat (<c>message.moldu</c> of /prompts) rewrites what was written for a neurotypical
/// reader (the same facts and requests, with the greeting, the intention, the courtesy and the closing such a reader
/// expects) and tells what it changed; MY TEXT puts back what was written. Nothing is sent by it.
/// </para>
/// <para>
/// ANNOTATE: a note of the user on a part of the message shown: on the text selected with the mouse, on a picture
/// that was clicked, or on both (<see cref="BeginAnnotation"/>). The notes are kept with the message
/// (<see cref="MailAnnotation"/>; inside its cipher when it is encrypted, since they quote it) and are their author's
/// alone: shown under the message, their text marked in it, searched with it. → IDEA: the message selected becomes a
/// new unsaved idea of /idea (<see cref="IdeaOfSelected"/>): its subject, its text, its entities as tags, where it
/// comes from and the notes written on it. → TASK: the same in the form of a new task of /kanban
/// (<see cref="TaskOfSelected"/>), created there by CREATE.
/// </para>
/// </summary>
public sealed partial class MessageWindowViewModel : ViewModelBase
{
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private string? _draftId;
    private string? _replyTo;
    /// <summary>The password that opened the message shown (kept while it is shown: its answer is encrypted with it).</summary>
    private string _shownPassword = string.Empty;
    private bool _refreshing;
    private long _stamp;

    private readonly Func<string, CancellationToken, Task<IReadOnlyList<NerEntityHit>>>? _ner;
    private readonly Func<string, string, CancellationToken, Task<string>>? _ask;
    private readonly string _language;
    private CancellationTokenSource? _analysing;

    /// <summary>The most messages of a conversation given to the model (the last ones).</summary>
    public const int AnalysisMessages = 20;

    /// <summary>The most of the text of each message given to the model, in characters.</summary>
    public const int AnalysisCharsPerMessage = 2500;

    /// <summary>The most earlier messages given to the model as the context of the message explained.</summary>
    public const int ExplainContextMessages = 4;

    /// <summary>The most of the text of the message explained given to the model, in characters.</summary>
    public const int ExplainChars = 8000;

    /// <summary>The most of the text of each message of the context, in characters.</summary>
    public const int ExplainContextChars = 800;

    /// <summary>How long the NER of the server is waited for when a message is sent.</summary>
    public TimeSpan NerTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public MessageWindowViewModel() : this(UserSession.Current?.Login ?? UserStore.DefaultLogin)
    {
    }

    /// <summary>
    /// The mail of <paramref name="me"/>; <paramref name="ner"/> finds the entities of a text (the NER of the server);
    /// without it the messages are sent without entities. <paramref name="ask"/> asks the model of the chat (a system
    /// prompt, the question; its answer), for ANALYSE, in <paramref name="language"/> (FR, EN…).
    /// </summary>
    public MessageWindowViewModel(string me, Func<string, CancellationToken, Task<IReadOnlyList<NerEntityHit>>>? ner = null,
        Func<string, string, CancellationToken, Task<string>>? ask = null, string language = "FR")
    {
        Me = me;
        _ner = ner;
        _ask = ask;
        _language = language;
        foreach (var folder in Enum.GetValues<MailFolder>())
            Folders.Add(new MailFolderViewModel(folder));
        _selectedFolder = Folders[0];
        Refresh();
    }

    /// <summary>The login of the user whose mail this is.</summary>
    public string Me { get; }

    /// <summary>
    /// The writing tools of the right click of /work, asked to the model of the chat: correct the spelling, rewrite
    /// for readability, evaluate, translate (null: these tools are not offered).
    /// </summary>
    public WritingAssistant? Writing { get; init; }

    /// <summary>True when the tools of the right click of /work can be asked.</summary>
    public bool CanUseWorkTools => Writing?.CanAsk == true;

    /// <summary>The criticism of <paramref name="text"/>, a message or a part of one, and ideas to improve it, in Markdown.</summary>
    public Task<string> EvaluateTextAsync(string text, CancellationToken cancellationToken) =>
        (Writing ?? throw new InvalidOperationException("no model is chosen in the chat")).EvaluateAsync(text,
            "Tu es un relecteur exigeant et bienveillant de messages professionnels. Réponds en Markdown, dans la langue du texte évalué, avec des titres courts.",
            "Analyse ce message et critique-le (clarté, ton, structure, ce qui manque ou prête à confusion). Puis suggère des pistes d'amélioration, sans le réécrire.",
            cancellationToken);

    public ObservableCollection<MailFolderViewModel> Folders { get; } = [];
    public ObservableCollection<MailRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private MailFolderViewModel _selectedFolder;

    [ObservableProperty]
    private MailRowViewModel? _selectedRow;

    /// <summary>What is searched in the folder: in the subjects, the names, the texts that are not encrypted.</summary>
    [ObservableProperty]
    private string _term = string.Empty;

    /// <summary>The content of the message selected, once it can be read (at once, or once its password opened it); null otherwise.</summary>
    [ObservableProperty]
    private MailContent? _shown;

    /// <summary>The password typed to open the encrypted message selected.</summary>
    [ObservableProperty]
    private string _unlockPassword = string.Empty;

    /// <summary>True while a message is written (the right side is its form).</summary>
    [ObservableProperty]
    private bool _isComposing;

    /// <summary>
    /// True from the moment a message is started until it is sent, kept as a draft or discarded: while another message
    /// is looked at meanwhile, it waits (BACK TO MY MESSAGE), nothing of it is lost.
    /// </summary>
    [ObservableProperty]
    private bool _hasUnsent;

    /// <summary>True while the right side shows the analysis of a conversation (in place of the message selected).</summary>
    [ObservableProperty]
    private bool _showsAnalysis;

    /// <summary>What the right side shows then: the analysis of a conversation, or a message explained.</summary>
    [ObservableProperty]
    private MailInsight _insight;

    /// <summary>True while the model is asked for the analysis.</summary>
    [ObservableProperty]
    private bool _isAnalysing;

    /// <summary>What the model said of the conversation (Markdown); empty while it is asked.</summary>
    [ObservableProperty]
    private string _analysis = string.Empty;

    /// <summary>Whose conversation is analysed, how many messages, from when to when.</summary>
    [ObservableProperty]
    private string _analysisTitle = string.Empty;

    /// <summary>Replaced whenever the right side (the message selected, or the one written) is to be drawn again.</summary>
    [ObservableProperty]
    private object _pane = new();

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    /// <summary>True while a message is encrypted or decrypted (a moment: the key takes 600 000 rounds).</summary>
    [ObservableProperty]
    private bool _isBusy;

    // ── The message being written ──

    /// <summary>
    /// The receivers of the message being written, as typed: logins or names of users, separated by commas or
    /// semicolons (<see cref="ReceiversOf"/> says who they are).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToHint), nameof(ToIsUnknown))]
    private string _composeTo = string.Empty;

    /// <summary>The users of the application, who may receive a message (read when a message is started).</summary>
    public IReadOnlyList<MailUser> Users { get; private set; } = [];

    /// <summary>Under the field: the names of the receivers typed, or what names no user.</summary>
    public string ToHint
    {
        get
        {
            var receivers = ReceiversOf(ComposeTo);
            if (receivers.Unknown.Count > 0)
                return "No user named: " + string.Join(", ", receivers.Unknown);
            return receivers.Logins.Count == 0 ? string.Empty : $"{receivers.Logins.Count} receiver{(receivers.Logins.Count > 1 ? "s" : string.Empty)}: {NamesOf(receivers.Logins)}";
        }
    }

    /// <summary>True when something typed in <see cref="ComposeTo"/> names no user.</summary>
    public bool ToIsUnknown => ReceiversOf(ComposeTo).Unknown.Count > 0;

    /// <summary>The pictures inserted in the message being written.</summary>
    public ObservableCollection<MailPicture> ComposePictures { get; } = [];

    [ObservableProperty]
    private string _composeSubject = string.Empty;

    [ObservableProperty]
    private string _composeText = string.Empty;

    [ObservableProperty]
    private bool _composeEncrypt;

    [ObservableProperty]
    private string _composePassword = string.Empty;

    [ObservableProperty]
    private string _composePasswordAgain = string.Empty;

    /// <summary>True while the model rewrites the message being written (ASPERGER TO MOLDU).</summary>
    [ObservableProperty]
    private bool _isRewriting;

    /// <summary>True once the message being written was rewritten: MY TEXT puts back what was written.</summary>
    [ObservableProperty]
    private bool _canUndoRewrite;

    /// <summary>What the model changed when it rewrote the message being written, and why (a few lines).</summary>
    [ObservableProperty]
    private string _rewriteNotes = string.Empty;

    /// <summary>The message being written as it was before it was rewritten.</summary>
    private string _beforeRewrite = string.Empty;
    private CancellationTokenSource? _rewriting;

    /// <summary>True when the message selected is encrypted and not opened.</summary>
    public bool IsLocked => SelectedRow is { Message.Encrypted: true } && Shown is null;

    /// <summary>The name of a user as shown (its first and last names, else its login; « me » is told).</summary>
    public string NameOf(string login) =>
        (_names.TryGetValue(login, out var name) ? name : login) + (login.Equals(Me, StringComparison.OrdinalIgnoreCase) ? " (me)" : string.Empty);

    public string NamesOf(IEnumerable<string> logins) => string.Join(", ", logins.Select(NameOf));

    private void Say(string text, bool error = false)
    {
        Status = text;
        IsError = error;
    }

    partial void OnSelectedFolderChanged(MailFolderViewModel value)
    {
        if (!_refreshing)
            Refresh();
    }

    partial void OnTermChanged(string value) => Refresh();

    partial void OnSelectedRowChanged(MailRowViewModel? value)
    {
        if (_refreshing)
            return;
        Show(value);
    }

    /// <summary>The message selected is shown: read at once when it is not encrypted, marked as read.</summary>
    private void Show(MailRowViewModel? row)
    {
        LeaveAnalysis();
        LeaveAnnotation();
        UnlockPassword = string.Empty;
        _shownPassword = string.Empty;
        Shown = row is { Message.Encrypted: false } ? row.Message.Content() : null;
        if (row is { IsUnread: true })
        {
            row.IsUnread = false;
            MailStore.Update(row.Message.Id, m =>
            {
                if (!m.IsReadBy(Me))
                    m.ReadBy.Add(Me);
            });
            Count(MailStore.All());
            _stamp = MailStore.Stamp();
        }
        if (IsComposing && row is not null)
            Say("Your message waits: ✎ BACK TO MY MESSAGE");
        IsComposing = false;
        Pane = new object();
    }

    /// <summary>
    /// Reads the messages again (another user of the machine may have sent some): the folders with their counts, the
    /// messages of the folder selected, the one selected kept when it is still there.
    /// </summary>
    [RelayCommand]
    public void Refresh()
    {
        _names.Clear();
        foreach (var user in UserStore.All())
            _names[user.Login] = user.DisplayName;

        _stamp = MailStore.Stamp();
        var all = MailStore.All();
        Count(all);
        var folder = SelectedFolder.Folder;
        var words = Term.Trim();
        var kept = SelectedRow?.Message.Id;
        _refreshing = true;
        try
        {
            Rows.Clear();
            foreach (var message in all.Where(m => MailStore.IsIn(m, folder, Me)))
            {
                var who = folder is MailFolder.Sent or MailFolder.Drafts || (folder == MailFolder.Trash && message.IsFrom(Me) && !message.IsTo(Me))
                    ? "To: " + (message.To.Count > 0 ? NamesOf(message.To) : "nobody yet")
                    : NameOf(message.From);
                if (words.Length > 0 && !Matches(message, who, words))
                    continue;
                Rows.Add(new MailRowViewModel(message, who, folder == MailFolder.Inbox && !message.IsReadBy(Me), message.Annotations.Count(a => a.By.Equals(Me, StringComparison.OrdinalIgnoreCase))));
            }
            var again = kept is null ? null : Rows.FirstOrDefault(r => r.Message.Id == kept);
            SelectedRow = again;
            if (again is null && kept is not null && !IsComposing)
            {
                Shown = null;
                Pane = new object();
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Shows the message <paramref name="id"/> (a hit of /search): its folder for the user logged in selected, the
    /// search of the window emptied, the message selected. Null when it is shown; else why not (gone, or not one of the
    /// messages of this user).
    /// </summary>
    public string? Show(string id)
    {
        if (MailStore.Get(id) is not { } message)
            return "This message is not on this machine any more";
        var folder = new[] { MailFolder.Inbox, MailFolder.Sent, MailFolder.Trash, MailFolder.Drafts }
            .Cast<MailFolder?>().FirstOrDefault(f => MailStore.IsIn(message, f!.Value, Me));
        if (folder is null)
            return $"This message is not one of {Me}'s";
        _refreshing = true;
        try
        {
            Term = string.Empty;
            SelectedFolder = Folders.First(f => f.Folder == folder);
        }
        finally
        {
            _refreshing = false;
        }
        Refresh();
        SelectedRow = Rows.FirstOrDefault(r => r.Message.Id == id);
        return SelectedRow is null ? "The message could not be selected" : null;
    }

    /// <summary>
    /// Reads the messages again when the mail changed (a message came from another chat of the machine) and none is
    /// being written, encrypted or decrypted; true when it did.
    /// </summary>
    public bool RefreshIfChanged()
    {
        if (IsBusy || MailStore.Stamp() == _stamp)
            return false;
        Refresh();
        return true;
    }

    private bool Matches(MailMessage message, string who, string words) =>
        message.Subject.Contains(words, StringComparison.OrdinalIgnoreCase)
        || who.Contains(words, StringComparison.OrdinalIgnoreCase)
        || (!message.Encrypted && (message.Text.Contains(words, StringComparison.OrdinalIgnoreCase)
            || message.Entities.Any(e => e.Name.Contains(words, StringComparison.OrdinalIgnoreCase) || e.Type.Equals(words, StringComparison.OrdinalIgnoreCase))
            || message.Annotations.Any(a => a.By.Equals(Me, StringComparison.OrdinalIgnoreCase) && (a.Note.Contains(words, StringComparison.OrdinalIgnoreCase) || a.Quote.Contains(words, StringComparison.OrdinalIgnoreCase)))));

    private void Count(List<MailMessage> all)
    {
        foreach (var folder in Folders)
        {
            var mine = all.Where(m => MailStore.IsIn(m, folder.Folder, Me)).ToList();
            folder.Label = folder.Folder switch
            {
                MailFolder.Inbox => "📥  Inbox" + (mine.Count(m => !m.IsReadBy(Me)) is > 0 and var unread ? $"  ({unread})" : string.Empty),
                MailFolder.Sent => "📤  Sent",
                MailFolder.Drafts => "📝  Drafts" + (mine.Count > 0 ? $"  ({mine.Count})" : string.Empty),
                _ => "🗑  Trash" + (mine.Count > 0 ? $"  ({mine.Count})" : string.Empty)
            };
        }
    }

    // ── Reading ──

    /// <summary>Opens the encrypted message selected with <see cref="UnlockPassword"/>.</summary>
    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (SelectedRow is not { Message.Encrypted: true } row || IsBusy)
            return;
        var password = UnlockPassword;
        if (password.Length == 0)
        {
            Say("Type the password of the message", true);
            return;
        }
        IsBusy = true;
        Say("Decrypting…");
        try
        {
            var content = await Task.Run(() => MailStore.Decrypt(row.Message.Cipher, password));
            if (!ReferenceEquals(SelectedRow, row))
                return;
            _shownPassword = password;
            UnlockPassword = string.Empty;
            Shown = content;
            Pane = new object();
            Say("Decrypted (AES-256). It is locked again as soon as another message is selected.");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or System.Text.Json.JsonException)
        {
            Say(ex is CryptographicException ? "Wrong password (or the message was changed): it stays encrypted" : "The encrypted message is damaged: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Hides again the encrypted message that was opened.</summary>
    [RelayCommand]
    private void Lock()
    {
        if (SelectedRow is not { Message.Encrypted: true })
            return;
        _shownPassword = string.Empty;
        Shown = null;
        LeaveAnnotation();
        Pane = new object();
        Say("Locked again");
    }

    /// <summary>The message selected goes to the trash; from the trash, it is removed for good (for this user).</summary>
    [RelayCommand]
    private void Delete()
    {
        if (SelectedRow is not { } row)
            return;
        var purge = SelectedFolder.Folder == MailFolder.Trash;
        MailStore.Update(row.Message.Id, m =>
        {
            if (purge)
            {
                if (!m.IsRemovedBy(Me))
                    m.RemovedBy.Add(Me);
            }
            else if (!m.IsTrashedBy(Me))
            {
                m.TrashedBy.Add(Me);
            }
        });
        ActivityLog.Info("Message", $"{Me}: message {row.Message.Id} {(purge ? "removed from the trash" : "moved to the trash")}");
        SelectedRow = null;
        Refresh();
        Say(purge ? "Removed for good" : "Moved to the trash");
    }

    /// <summary>The message selected in the trash goes back where it was.</summary>
    [RelayCommand]
    private void Restore()
    {
        if (SelectedRow is not { } row || SelectedFolder.Folder != MailFolder.Trash)
            return;
        MailStore.Update(row.Message.Id, m => m.TrashedBy.RemoveAll(l => l.Equals(Me, StringComparison.OrdinalIgnoreCase)));
        SelectedRow = null;
        Refresh();
        Say("Put back in its folder");
    }

    /// <summary>The message selected in the inbox is not read again.</summary>
    [RelayCommand]
    private void MarkUnread()
    {
        if (SelectedRow is not { } row || SelectedFolder.Folder != MailFolder.Inbox)
            return;
        MailStore.Update(row.Message.Id, m => m.ReadBy.RemoveAll(l => l.Equals(Me, StringComparison.OrdinalIgnoreCase)));
        SelectedRow = null;
        Refresh();
        Say("Marked as not read");
    }

    // ── The analysis of a conversation ──

    /// <summary>The analysis asked is dropped, the right side is no longer it.</summary>
    private void LeaveAnalysis()
    {
        _analysing?.Cancel();
        _analysing = null;
        IsAnalysing = false;
        ShowsAnalysis = false;
    }

    /// <summary>
    /// Who <paramref name="message"/> is exchanged with: its sender, or its first receiver (other than me) when it is
    /// one of mine; me when I wrote it to myself only; null when it has no receiver yet.
    /// </summary>
    public string? CorrespondentOf(MailMessage message) =>
        !message.IsFrom(Me) ? message.From
        : message.To.FirstOrDefault(l => !l.Equals(Me, StringComparison.OrdinalIgnoreCase)) ?? (message.To.Count > 0 ? Me : null);

    /// <summary>
    /// The last messages exchanged between me and <paramref name="other"/> (sent by one, received by the other; no
    /// draft, none I removed or put in the trash), <see cref="AnalysisMessages"/> at most, the oldest first.
    /// </summary>
    public List<MailMessage> ExchangedWith(string other)
    {
        var exchanged = MailStore.All()
            .Where(m => !m.Draft && !m.IsRemovedBy(Me) && !m.IsTrashedBy(Me)
                        && ((m.IsFrom(Me) && m.IsTo(other)) || (m.IsFrom(other) && m.IsTo(Me))))
            .Take(AnalysisMessages)
            .ToList();
        exchanged.Reverse();
        return exchanged;
    }

    /// <summary>The messages as the model reads them: numbered, dated, who to whom, the subject, the entities, the text (an encrypted one: its envelope only).</summary>
    private string Transcript(List<MailMessage> messages, string other)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Conversation between {Plain(Me)} and {Plain(other)}: {messages.Count} message(s), from {messages[0].Date.LocalDateTime:yyyy-MM-dd HH:mm} to {messages[^1].Date.LocalDateTime:yyyy-MM-dd HH:mm}.");
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            text.AppendLine();
            text.AppendLine($"[{i + 1}] {message.Date.LocalDateTime:yyyy-MM-dd HH:mm} · {Plain(message.From)} → {string.Join(", ", message.To.Select(Plain))} · Subject: {(message.Subject.Length > 0 ? message.Subject : "(none)")}");
            if (message.Encrypted)
            {
                text.AppendLine("(encrypted message: its content was not read)");
                continue;
            }
            if (message.Entities.Count > 0)
                text.AppendLine("Entities: " + string.Join(", ", message.Entities.Select(e => $"{e.Type} {e.Name}")));
            if (message.Pictures.Count > 0)
                text.AppendLine($"({message.Pictures.Count} picture(s) in the message)");
            // What its author wrote, without what is quoted of the message answered (it is above, under its own number).
            var own = MailMessage.ForEntities(string.Empty, message.Text);
            text.AppendLine(own.Length > AnalysisCharsPerMessage ? own[..AnalysisCharsPerMessage] + " […]" : own.Length > 0 ? own : "(no text)");
        }
        return text.ToString().TrimEnd();
    }

    private static string LanguageName(string code) => code.ToUpperInvariant() switch
    {
        "FR" => "French", "EN" => "English", "DE" => "German", "NL" => "Dutch", "ES" => "Spanish", "IT" => "Italian", "JA" => "Japanese",
        _ => code
    };

    /// <summary>
    /// Analyses the last messages exchanged with the sender of the message selected: the subjects, how the
    /// conversation moves on the substance, its emotional side. Shown in place of the message; selecting a message,
    /// or CLOSE, leaves it (and stops the model when it is still asked).
    /// </summary>
    [RelayCommand]
    private async Task AnalyseAsync()
    {
        if (SelectedRow is not { Message: { Draft: false } message })
        {
            Say("Select a message: the conversation with its sender is analysed", true);
            return;
        }
        if (CorrespondentOf(message) is not { } other)
            return;
        if (_ask is null)
        {
            Say("No model is chosen in the chat: the conversation cannot be analysed", true);
            return;
        }
        var messages = ExchangedWith(other);
        if (messages.Count == 0)
        {
            Say("No message exchanged with " + NameOf(other), true);
            return;
        }
        var hidden = messages.Count(m => m.Encrypted);
        if (hidden == messages.Count)
        {
            Say($"The {messages.Count} message(s) exchanged with {NameOf(other)} are encrypted: nothing of them is read, there is nothing to analyse", true);
            return;
        }

        var title = $"Conversation with {NameOf(other)} · {messages.Count} message{(messages.Count > 1 ? "s" : "")} · {messages[0].Date.LocalDateTime:dd/MM/yyyy} → {messages[^1].Date.LocalDateTime:dd/MM/yyyy}"
                    + (hidden > 0 ? $" · {hidden} encrypted, not read" : string.Empty);
        var system = Prompts.Get("message.analyze", ("language", LanguageName(_language)), ("me", Plain(Me)), ("other", Plain(other)));
        await AskModelAsync(MailInsight.Conversation, title, system, Transcript(messages, other),
            $"Analysing the last {messages.Count} message(s) exchanged with {NameOf(other)}…",
            $"Conversation with {NameOf(other)} analysed · {messages.Count} message(s)" + (hidden > 0 ? $", {hidden} encrypted not read" : string.Empty) + " · not kept",
            $"conversation with {other} analysed ({messages.Count} message(s), {hidden} encrypted not read)");
    }

    /// <summary>The name of a user without « (me) », as the model reads it.</summary>
    private string Plain(string login) => NameOf(login).Replace(" (me)", string.Empty);

    /// <summary>
    /// Asks the model of the chat (<paramref name="system"/>, <paramref name="question"/>) and shows its answer in
    /// place of the message selected, under <paramref name="title"/>; a wait meanwhile. Selecting a message, CLOSE or
    /// another question leaves it (and stops the model when it is still asked).
    /// </summary>
    private async Task AskModelAsync(MailInsight insight, string title, string system, string question, string doing, string done, string logged)
    {
        _analysing?.Cancel();
        var asking = _analysing = new CancellationTokenSource();
        IsComposing = false;
        LeaveAnnotation();
        Insight = insight;
        Analysis = string.Empty;
        AnalysisTitle = title;
        ShowsAnalysis = true;
        IsAnalysing = true;
        Pane = new object();
        Say(doing);
        try
        {
            var answer = (await _ask!(system, question, asking.Token)).Trim();
            if (asking.IsCancellationRequested || !ReferenceEquals(_analysing, asking))
                return;
            Analysis = answer.Length > 0 ? TablesAsLists(answer) : "_The model gave no answer._";
            ActivityLog.Info("Message", $"{Me}: {logged}, {answer.Length} characters");
            Say(done);
        }
        catch (OperationCanceledException)
        {
            // Another message was selected, or CLOSE.
            return;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!ReferenceEquals(_analysing, asking))
                return;
            Analysis = "_The model did not answer: " + ex.Message + "_";
            Say("The model did not answer: " + ex.Message, true);
            ActivityLog.Error("Message", "The model of the chat did not answer about a message", ex);
        }
        finally
        {
            if (ReferenceEquals(_analysing, asking))
            {
                _analysing = null;
                IsAnalysing = false;
                Pane = new object();
            }
            asking.Dispose();
        }
    }

    private static readonly System.Text.RegularExpressions.Regex TableRule = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// <paramref name="markdown"/> with its tables written as lists (the prompts ask for none, a model writes one now
    /// and then, and a table is hard to read in the pane): each row a bullet, its first cell in bold, then the other
    /// cells, each after the heading of its column (« - **déçue** — Intensité: forte — Certitude: sûre »).
    /// </summary>
    public static string TablesAsLists(string markdown)
    {
        static string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];
        var lines = markdown.ReplaceLineEndings("\n").Replace("****", "**").Split('\n');
        var written = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith('|'))
            {
                written.Add(lines[i]);
                continue;
            }
            // The rows of the table: the lines that follow, as long as they start with « | ».
            var rows = new List<string>();
            for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                rows.Add(lines[i]);
            i--;
            var headings = rows.Count > 1 && TableRule.IsMatch(rows[1]) ? Cells(rows[0]) : [];
            foreach (var row in rows.Skip(headings.Length > 0 ? 2 : 0).Where(r => !TableRule.IsMatch(r)))
            {
                var cells = Cells(row);
                if (cells.All(c => c.Length == 0))
                    continue;
                var parts = new List<string> { cells[0].Length == 0 || cells[0].Contains('*') ? cells[0] : $"**{cells[0]}**" };
                for (var c = 1; c < cells.Length; c++)
                {
                    if (cells[c].Length == 0)
                        continue;
                    var heading = c < headings.Length ? headings[c].Trim('*', ' ').TrimEnd(':', ' ') : string.Empty;
                    parts.Add(heading.Length > 0 ? $"{heading}: {cells[c]}" : cells[c]);
                }
                written.Add("- " + string.Join(" — ", parts.Where(p => p.Length > 0)));
            }
        }
        return string.Join('\n', written);
    }

    /// <summary>
    /// I'M ASPERGER: explains the message selected, one that was received, to a reader with an Asperger profile: what
    /// it says literally, what is implied, the emotions of its sender, what is expected, how to answer. An encrypted
    /// message is explained once its password opened it. Shown in place of the message.
    /// </summary>
    [RelayCommand]
    private async Task ExplainAsync()
    {
        if (SelectedRow is not { Message: { Draft: false } message })
        {
            Say("Select a message you received: it is explained to you", true);
            return;
        }
        if (message.IsFrom(Me) && !message.IsTo(Me))
        {
            Say("This message is one you wrote: select one you received", true);
            return;
        }
        if (Shown is not { } content)
        {
            Say("Open the encrypted message with its password first: then it can be explained", true);
            return;
        }
        var text = MailMessage.WithoutMarks(content.Text).Trim();
        if (text.Length == 0 && message.Subject.Trim().Length == 0)
        {
            Say("This message has no text to explain", true);
            return;
        }
        if (_ask is null)
        {
            Say("No model is chosen in the chat: the message cannot be explained", true);
            return;
        }

        // What was exchanged with its sender before it: the context (nothing of an encrypted message).
        var before = ExchangedWith(message.From)
            .Where(m => m.Id != message.Id && m.Date < message.Date && !m.Encrypted)
            .TakeLast(ExplainContextMessages)
            .ToList();
        var question = new System.Text.StringBuilder();
        if (before.Count > 0)
        {
            question.AppendLine("Earlier messages of the conversation (context only, the oldest first):");
            foreach (var earlier in before)
            {
                var own = MailMessage.ForEntities(string.Empty, earlier.Text);
                question.AppendLine();
                question.AppendLine($"{earlier.Date.LocalDateTime:yyyy-MM-dd HH:mm} · {Plain(earlier.From)} → {string.Join(", ", earlier.To.Select(Plain))} · Subject: {(earlier.Subject.Length > 0 ? earlier.Subject : "(none)")}");
                question.AppendLine(own.Length > ExplainContextChars ? own[..ExplainContextChars] + " […]" : own.Length > 0 ? own : "(no text)");
            }
            question.AppendLine();
            question.AppendLine("=====");
            question.AppendLine();
        }
        question.AppendLine("The message to explain:");
        question.AppendLine($"{message.Date.LocalDateTime:yyyy-MM-dd HH:mm} · {Plain(message.From)} → {string.Join(", ", message.To.Select(Plain))} · Subject: {(message.Subject.Length > 0 ? message.Subject : "(none)")}");
        if (content.Pictures.Count > 0)
            question.AppendLine($"({content.Pictures.Count} picture(s) in the message, which you do not see)");
        question.AppendLine();
        question.Append(text.Length > ExplainChars ? text[..ExplainChars] + " […]" : text.Length > 0 ? text : "(no text: the subject is the whole message)");

        var title = $"« {(message.Subject.Length > 0 ? message.Subject : "(no subject)")} » from {NameOf(message.From)} · {message.Date.LocalDateTime:dd/MM/yyyy HH:mm}"
                    + (before.Count > 0 ? $" · read with the {before.Count} message{(before.Count > 1 ? "s" : "")} before it" : string.Empty)
                    + (message.Encrypted ? " · decrypted: its text was given to the model" : string.Empty)
                    + (content.Pictures.Count > 0 ? $" · {content.Pictures.Count} picture{(content.Pictures.Count > 1 ? "s" : "")} not seen" : string.Empty);
        var system = Prompts.Get("message.asperger", ("language", LanguageName(_language)), ("reader", Plain(Me)), ("sender", Plain(message.From)));
        await AskModelAsync(MailInsight.Explained, title, system, question.ToString(),
            $"Explaining the message of {NameOf(message.From)}…",
            $"Message of {NameOf(message.From)} explained · a reading by the model, not a certainty · not kept",
            $"message {message.Id} of {message.From} explained ({text.Length} characters{(message.Encrypted ? ", decrypted" : string.Empty)}, {before.Count} message(s) of context)");
    }

    /// <summary>Asks the model again what the right side shows: the analysis of the conversation, or the message explained.</summary>
    [RelayCommand]
    private Task AgainAsync() => !ShowsAnalysis ? Task.CompletedTask : Insight == MailInsight.Explained ? ExplainAsync() : AnalyseAsync();

    /// <summary>Back from the analysis to the message selected.</summary>
    [RelayCommand]
    private void CloseAnalysis()
    {
        if (!ShowsAnalysis)
            return;
        LeaveAnalysis();
        Pane = new object();
        Say(string.Empty);
    }

    // ── Notes on parts of a message ──

    /// <summary>The most of a text selected that a note keeps, in characters.</summary>
    public const int AnnotationQuoteChars = 2000;

    /// <summary>True while a note on a part of the message shown is being written.</summary>
    [ObservableProperty]
    private bool _isAnnotating;

    /// <summary>The note being written.</summary>
    [ObservableProperty]
    private string _annotationNote = string.Empty;

    /// <summary>The text of the message the note being written is about (empty: a picture only).</summary>
    public string AnnotationQuote { get; private set; } = string.Empty;

    /// <summary>The picture of the message the note being written is about (null: some text only).</summary>
    public string? AnnotationPictureId { get; private set; }

    /// <summary>The notes of the user logged in on the message shown, the oldest first (those of the others are theirs alone).</summary>
    public List<MailAnnotation> MyAnnotations =>
        Shown is null ? [] : [.. Shown.Annotations.Where(a => a.By.Equals(Me, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Date)];

    /// <summary>The name of the picture <paramref name="id"/> of the message shown (null: none, or gone).</summary>
    public string? PictureName(string? id) => id is null ? null : Shown?.Pictures.FirstOrDefault(p => p.Id == id)?.Name;

    private void LeaveAnnotation()
    {
        IsAnnotating = false;
        AnnotationNote = string.Empty;
        AnnotationQuote = string.Empty;
        AnnotationPictureId = null;
    }

    /// <summary>
    /// Starts a note on a part of the message shown: <paramref name="selectedText"/> (what the mouse selected in its
    /// text), the picture <paramref name="pictureId"/> (the one clicked), or both. False, and <see cref="Status"/> says
    /// why, when there is nothing to note: no message, one still encrypted, neither text nor picture.
    /// </summary>
    public bool BeginAnnotation(string selectedText, string? pictureId)
    {
        if (SelectedRow is not { Message.Draft: false })
        {
            Say("Select a message: its text or its pictures are annotated", true);
            return false;
        }
        if (Shown is not { } content)
        {
            Say("Open the encrypted message with its password first: then it can be annotated", true);
            return false;
        }
        var quote = selectedText.ReplaceLineEndings("\n").Trim();
        if (quote.Length > AnnotationQuoteChars)
            quote = quote[..AnnotationQuoteChars];
        if (pictureId is not null && content.Pictures.All(p => p.Id != pictureId))
            pictureId = null;
        if (quote.Length == 0 && pictureId is null)
        {
            Say("Select some text of the message with the mouse, or click one of its pictures (or both), then ✎ ANNOTATE", true);
            return false;
        }
        LeaveAnalysis();
        IsComposing = false;
        AnnotationQuote = quote;
        AnnotationPictureId = pictureId;
        AnnotationNote = string.Empty;
        IsAnnotating = true;
        Pane = new object();
        Say("Write your note on what you selected, then SAVE NOTE");
        return true;
    }

    /// <summary>Leaves the note being written.</summary>
    [RelayCommand]
    private void CancelAnnotation()
    {
        if (!IsAnnotating)
            return;
        LeaveAnnotation();
        Pane = new object();
        Say(string.Empty);
    }

    /// <summary>Keeps the note being written with the message (inside its cipher when the message is encrypted).</summary>
    [RelayCommand]
    private async Task SaveAnnotationAsync()
    {
        if (!IsAnnotating || IsBusy)
            return;
        var note = AnnotationNote.Trim();
        if (note.Length == 0)
        {
            Say("Write the note first", true);
            return;
        }
        var annotation = new MailAnnotation
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            By = Me,
            Date = DateTimeOffset.Now,
            Quote = AnnotationQuote,
            PictureId = AnnotationPictureId,
            Note = note
        };
        if (!await ChangeAnnotationsAsync(notes => notes.Add(annotation)))
            return;
        LeaveAnnotation();
        Pane = new object();
        Say("Note kept with the message · seen by you only" + (SelectedRow is { Message.Encrypted: true } ? " · encrypted with it (AES-256)" : string.Empty));
    }

    /// <summary>Removes <paramref name="annotation"/>, a note of the user logged in on the message shown.</summary>
    public async Task RemoveAnnotationAsync(MailAnnotation annotation)
    {
        if (IsBusy || !annotation.By.Equals(Me, StringComparison.OrdinalIgnoreCase))
            return;
        if (!await ChangeAnnotationsAsync(notes => notes.RemoveAll(a => a.Id == annotation.Id && a.By.Equals(Me, StringComparison.OrdinalIgnoreCase))))
            return;
        Pane = new object();
        Say("Note removed");
    }

    /// <summary>
    /// Changes the notes of the message shown in its file as it is now and shows it again. An encrypted message is
    /// decrypted with the password that opened it, changed and encrypted again (a moment). False when it could not
    /// be done (<see cref="Status"/> says why).
    /// </summary>
    private async Task<bool> ChangeAnnotationsAsync(Action<List<MailAnnotation>> change)
    {
        if (SelectedRow is not { } row || Shown is null)
            return false;
        var id = row.Message.Id;
        try
        {
            MailContent? shown;
            if (!row.Message.Encrypted)
            {
                shown = MailStore.Update(id, m => change(m.Annotations))?.Content();
            }
            else
            {
                var password = _shownPassword;
                IsBusy = true;
                Say("Encrypting your notes with the message (AES-256)…");
                try
                {
                    shown = await Task.Run(() =>
                    {
                        if (MailStore.Get(id) is not { Encrypted: true } now)
                            return null;
                        var content = MailStore.Decrypt(now.Cipher, password);
                        change(content.Annotations);
                        var cipher = MailStore.Encrypt(content, password);
                        // Written only when the message is still as it was read (another chat may have changed it meanwhile).
                        var saved = MailStore.Update(id, m =>
                        {
                            if (m.Cipher == now.Cipher)
                                m.Cipher = cipher;
                        });
                        return saved?.Cipher == cipher ? content : null;
                    });
                }
                finally
                {
                    IsBusy = false;
                }
            }
            if (shown is null)
            {
                Say("The message changed or is gone: the note was not kept, try again", true);
                Refresh();
                return false;
            }
            if (SelectedRow?.Message.Id != id)
                return true;
            Refresh();
            if (SelectedRow?.Message.Id == id)
                Shown = shown;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or System.Text.Json.JsonException or System.IO.IOException or UnauthorizedAccessException)
        {
            Say("The note could not be kept: " + ex.Message, true);
            ActivityLog.Error("Message", "A note on a message could not be kept", ex);
            return false;
        }
    }

    // ── To /idea, to /kanban ──

    /// <summary>What /idea or /kanban takes of a message to make a new idea or a new task of it.</summary>
    public sealed record MailIdea(string Title, string Description, string Comment, List<string> Tags);

    private static readonly System.Text.RegularExpressions.Regex ReplyPrefix = new(@"^\s*((RE|FW|FWD|TR)\s*:\s*)+", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly HashSet<string> NotTags = new(StringComparer.OrdinalIgnoreCase) { "DATE", "TIME", "MONEY", "EMAIL", "URL", "PHONE", "NUMBER", "CODE" };

    /// <summary>
    /// The message selected as a new idea for /idea: its subject (without RE:, FW:) as the title, what its author
    /// wrote as the description (the whole text when it only forwards), the names of its entities (persons,
    /// organisations, places…) as tags, and a comment that says where it comes from, with the notes the user wrote on
    /// it. Null, and <see cref="Status"/> says why, for no message, a draft or a message still encrypted.
    /// </summary>
    public MailIdea? IdeaOfSelected() => ForTool("/idea", "a new idea", "SAVE");

    /// <summary>
    /// The message selected as a new task for /kanban, made as <see cref="IdeaOfSelected"/> makes an idea: the subject
    /// as the name of the task, the text as its description, the entities as tags, where it comes from and the notes
    /// as its comment. Null, and <see cref="Status"/> says why, for no message, a draft or a message still encrypted.
    /// </summary>
    public MailIdea? TaskOfSelected() => ForTool("/kanban", "a new task", "CREATE");

    /// <summary>The message selected for <paramref name="tool"/>, which makes <paramref name="what"/> of it, kept there by <paramref name="kept"/>.</summary>
    private MailIdea? ForTool(string tool, string what, string kept)
    {
        if (SelectedRow is not { Message: { Draft: false } message })
        {
            Say($"Select a message: it is sent to {tool} as {what}", true);
            return null;
        }
        if (Shown is not { } content)
        {
            Say($"Open the encrypted message with its password first: then it can be sent to {tool}", true);
            return null;
        }
        var own = MailMessage.ForEntities(string.Empty, content.Text);
        var description = own.Length > 0 ? own : MailMessage.WithoutMarks(content.Text).Trim();
        var title = ReplyPrefix.Replace(message.Subject, string.Empty).Trim();
        if (title.Length == 0)
            title = description.Split('\n')[0].Trim() is { Length: > 80 } line ? line[..80] + "…" : description.Split('\n')[0].Trim();
        var comment = new System.Text.StringBuilder();
        comment.Append($"From the message of {Plain(message.From)} to {string.Join(", ", message.To.Select(Plain))}, {message.Date.LocalDateTime:dd/MM/yyyy HH:mm}");
        if (message.Subject.Length > 0)
            comment.Append($" (« {message.Subject} »)");
        if (content.Pictures.Count > 0)
            comment.Append($" · {content.Pictures.Count} picture(s) not copied");
        var notes = MyAnnotations;
        if (notes.Count > 0)
        {
            comment.Append("\nMy notes on it:");
            foreach (var note in notes)
            {
                var about = string.Join(" · ", new[] { note.Quote.Length > 0 ? $"« {note.Quote.ReplaceLineEndings(" ")} »" : null, PictureName(note.PictureId) is { } name ? $"picture {name}" : null }.Where(p => p is not null));
                comment.Append($"\n- {(about.Length > 0 ? about + ": " : string.Empty)}{note.Note.ReplaceLineEndings(" ")}");
            }
        }
        var tags = content.Entities.Where(e => !NotTags.Contains(e.Type)).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ActivityLog.Info("Message", $"{Me}: message {message.Id} sent to {tool} as {what} ({description.Length} characters, {tags.Count} tag(s), {notes.Count} note(s))");
        Say($"Sent to {tool} as {what}: check it there, then {kept}" + (message.Encrypted ? " · there its text is no longer encrypted" : string.Empty));
        return new MailIdea(title, description, comment.ToString(), tags);
    }

    // ── ASPERGER TO MOLDU: the message being written, rewritten for a neurotypical reader ──

    private static readonly System.Text.RegularExpressions.Regex RewriteRule = new(@"^\s*={3,}\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PictureMark = new(@"\[\[image:[0-9a-f]{4,16}\]\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// <paramref name="text"/> of a message being written, cut in two: what its author wrote, and what is quoted of
    /// the message answered or forwarded (from the line « --- date, name: --- » on; empty when there is none).
    /// </summary>
    public static (string Own, string Quoted) OwnAndQuoted(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("--- ", StringComparison.Ordinal) && l.TrimEnd().EndsWith("---", StringComparison.Ordinal));
        return at < 0 ? (string.Join('\n', lines), string.Empty) : (string.Join('\n', lines[..at]), string.Join('\n', lines[at..]));
    }

    /// <summary>
    /// What the model answered to <c>message.moldu</c>, cut at its line « ===== »: the message rewritten (without the
    /// fence of a code block a model puts around it now and then) and what was changed (empty when it told nothing).
    /// </summary>
    public static (string Message, string Changes) Rewritten(string answer)
    {
        var text = answer.ReplaceLineEndings("\n").Trim();
        var cut = RewriteRule.Match(text);
        var message = (cut.Success ? text[..cut.Index] : text).Trim();
        var changes = cut.Success ? text[(cut.Index + cut.Length)..].Trim() : string.Empty;
        if (message.StartsWith("```", StringComparison.Ordinal) && message.EndsWith("```", StringComparison.Ordinal) && message.Length > 6)
            message = message[(message.IndexOf('\n') + 1)..^3].Trim();
        return (message, changes);
    }

    /// <summary>
    /// <paramref name="message"/> without the name the model signed it with (<paramref name="author"/>, or its first
    /// name, alone on the last line) when <paramref name="own"/>, what the author wrote, was not signed so.
    /// </summary>
    public static string WithoutAddedSignature(string message, string own, string author)
    {
        static string Last(string text) => text.TrimEnd().Split('\n')[^1].Trim().TrimEnd('.', ',');
        bool IsName(string line) => line.Length > 0 && (line.Equals(author, StringComparison.OrdinalIgnoreCase) || line.Equals(author.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
        if (!IsName(Last(message)) || IsName(Last(own)) || !message.TrimEnd().Contains('\n'))
            return message;
        var trimmed = message.TrimEnd();
        return trimmed[..trimmed.LastIndexOf('\n')].TrimEnd();
    }

    /// <summary>
    /// ASPERGER TO MOLDU: the message being written is rewritten by the model of the chat (<c>message.moldu</c> of
    /// /prompts) for a neurotypical reader, its author having an Asperger profile: the same facts and requests, with
    /// the greeting, the intention, the courtesy and the closing such a reader expects. Only what the author wrote is
    /// rewritten: what is quoted of the message answered is given as the context and stays as it is, and the marks of
    /// the pictures are kept. The text of the form is replaced (unless it was changed meanwhile);
    /// <see cref="RewriteNotes"/> tells what was changed, and MY TEXT puts back what was written.
    /// </summary>
    [RelayCommand]
    private async Task ToMolduAsync()
    {
        if (!IsComposing || IsRewriting)
            return;
        var whole = ComposeText;
        var (own, quoted) = OwnAndQuoted(whole);
        if (own.Trim().Length == 0)
        {
            Say("Write your message first: then it is rewritten for a neurotypical reader", true);
            return;
        }
        if (_ask is null)
        {
            Say("No model is chosen in the chat: the message cannot be rewritten", true);
            return;
        }
        var to = ReceiversOf(ComposeTo).Logins.Select(Plain).ToList();
        var question = new System.Text.StringBuilder();
        question.AppendLine($"From: {Plain(Me)} · To: {(to.Count > 0 ? string.Join(", ", to) : "(not chosen yet)")} · Subject: {(ComposeSubject.Trim().Length > 0 ? ComposeSubject.Trim() : "(none)")}");
        if (quoted.Trim().Length > 0)
        {
            question.AppendLine();
            question.AppendLine("The message it answers (context only):");
            question.AppendLine(quoted.Length > AnalysisCharsPerMessage ? quoted[..AnalysisCharsPerMessage] + " […]" : quoted);
        }
        question.AppendLine();
        question.AppendLine("The message to rewrite:");
        question.Append(own.Trim().Length > ExplainChars ? own.Trim()[..ExplainChars] : own.Trim());

        _rewriting?.Cancel();
        var asking = _rewriting = new CancellationTokenSource();
        IsRewriting = true;
        Say("Rewriting your message for a neurotypical reader…" + (ComposeEncrypt ? " (its text is given to the model of the chat before it is encrypted)" : string.Empty));
        try
        {
            var system = Prompts.Get("message.moldu", ("language", LanguageName(_language)), ("sender", Plain(Me)));
            var (message, changes) = Rewritten(await _ask(system, question.ToString(), asking.Token));
            if (asking.IsCancellationRequested || !ReferenceEquals(_rewriting, asking))
                return;
            if (message.Length == 0)
            {
                Say("The model gave no answer: your message is as you wrote it", true);
                return;
            }
            if (!IsComposing && !HasUnsent || ComposeText != whole)
            {
                Say("Your message changed while the model was asked: it was not replaced", true);
                return;
            }
            message = WithoutAddedSignature(message, own, Plain(Me));
            // A picture the model dropped keeps its place at the end of what was written.
            foreach (var mark in PictureMark.Matches(own).Select(m => m.Value).Distinct().Where(m => !message.Contains(m, StringComparison.Ordinal)))
                message += "\n" + mark;
            _beforeRewrite = whole;
            ComposeText = quoted.Length > 0 ? message + "\n\n" + quoted : message;
            RewriteNotes = TablesAsLists(changes);
            CanUndoRewrite = true;
            ActivityLog.Info("Message", $"{Me}: the message being written was rewritten for a neurotypical reader ({own.Trim().Length} → {message.Length} characters)");
            Say("Rewritten for a neurotypical reader: read it before you send it · ↩ MY TEXT puts back what you wrote");
        }
        catch (OperationCanceledException)
        {
            // STOP, or the message was sent, kept or left meanwhile.
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (ReferenceEquals(_rewriting, asking))
                Say("The model did not answer: " + ex.Message + " · your message is as you wrote it", true);
            ActivityLog.Error("Message", "The model of the chat did not rewrite a message", ex);
        }
        finally
        {
            if (ReferenceEquals(_rewriting, asking))
            {
                _rewriting = null;
                IsRewriting = false;
            }
            asking.Dispose();
        }
    }

    /// <summary>Stops the model asked to rewrite the message: it stays as it was written.</summary>
    [RelayCommand]
    private void StopRewrite()
    {
        if (!IsRewriting)
            return;
        LeaveRewrite(false);
        Say("Stopped: your message is as you wrote it");
    }

    /// <summary>Puts back the message as it was written before ASPERGER TO MOLDU (what was typed since is lost).</summary>
    [RelayCommand]
    private void UndoRewrite()
    {
        if (!CanUndoRewrite)
            return;
        ComposeText = _beforeRewrite;
        LeaveRewrite(true);
        Say("Your text, as you wrote it");
    }

    /// <summary>The model asked to rewrite is dropped; <paramref name="forget"/>: so is what it changed before (another message is written).</summary>
    private void LeaveRewrite(bool forget)
    {
        _rewriting?.Cancel();
        _rewriting = null;
        IsRewriting = false;
        if (!forget)
            return;
        _beforeRewrite = string.Empty;
        RewriteNotes = string.Empty;
        CanUndoRewrite = false;
    }

    // ── The receivers ──

    private static readonly char[] ToSeparators = [',', ';', '\n'];

    /// <summary>A text as it is compared: without accents, in lower case, its spaces single.</summary>
    internal static string Fold(string text)
    {
        var folded = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                folded.Append(char.ToLowerInvariant(c));
        return string.Join(' ', folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Who <paramref name="text"/> names: its parts, separated by commas or semicolons, each the login of a user, its
    /// name (first and last names; accents and case do not count), or <c>Name &lt;login&gt;</c>. A part that names no
    /// user, or a name two users have, is unknown. Each user once, in the order typed.
    /// </summary>
    public MailReceivers ReceiversOf(string text)
    {
        var logins = new List<string>();
        var unknown = new List<string>();
        foreach (var part in text.Split(ToSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var typed = part;
            // Name <login>
            if (typed.EndsWith('>') && typed.LastIndexOf('<') is >= 0 and var open)
                typed = typed[(open + 1)..^1].Trim();
            if (typed.EndsWith("(me)", StringComparison.OrdinalIgnoreCase))
                typed = typed[..^4].Trim();
            if (typed.Length == 0)
                continue;
            var user = Users.FirstOrDefault(u => u.Login.Equals(typed, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                var folded = Fold(typed);
                var named = Users.Where(u => Fold(u.Name) == folded).ToList();
                user = named.Count == 1 ? named[0] : null;
            }
            if (user is null)
            {
                if (!unknown.Contains(part, StringComparer.OrdinalIgnoreCase))
                    unknown.Add(part);
            }
            else if (!logins.Contains(user.Login, StringComparer.OrdinalIgnoreCase))
                logins.Add(user.Login);
        }
        return new MailReceivers(logins, unknown);
    }

    /// <summary>
    /// The part of <paramref name="text"/> the cursor at <paramref name="caret"/> is in: from after the separator
    /// before it (and the spaces that follow) to the next separator.
    /// </summary>
    public static (int Start, int End) ToPartAt(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret == 0 ? 0 : text.LastIndexOfAny(ToSeparators, caret - 1) + 1;
        while (start < caret && char.IsWhiteSpace(text[start]))
            start++;
        var end = text.IndexOfAny(ToSeparators, caret);
        return (start, end < 0 ? text.Length : end);
    }

    /// <summary>
    /// The users that complete what is typed before the cursor in its part of <paramref name="text"/>, the best first:
    /// those whose login starts with it, whose name starts with it, one of whose names starts with it, then (from two
    /// letters typed) those whose login, name or e-mail holds it; not the receivers already named elsewhere in the
    /// text. None when nothing is typed.
    /// </summary>
    public IReadOnlyList<MailUser> SuggestTo(string text, int caret, int most = 8)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var (start, end) = ToPartAt(text, caret);
        var typed = Fold(text[start..caret]);
        if (typed.Length == 0)
            return [];
        var others = ReceiversOf(text[..start] + text[end..]).Logins;
        int Rank(MailUser user)
        {
            var login = Fold(user.Login);
            var name = Fold(user.Name);
            if (login.StartsWith(typed, StringComparison.Ordinal))
                return 0;
            if (name.StartsWith(typed, StringComparison.Ordinal))
                return 1;
            if (name.Split(' ').Any(word => word.StartsWith(typed, StringComparison.Ordinal)))
                return 2;
            // One letter is in nearly every name: what is held counts from two letters.
            return typed.Length >= 2 && (login.Contains(typed, StringComparison.Ordinal) || name.Contains(typed, StringComparison.Ordinal) || Fold(user.Email).Contains(typed, StringComparison.Ordinal)) ? 3 : -1;
        }
        return [.. Users.Where(u => !others.Contains(u.Login, StringComparer.OrdinalIgnoreCase))
            .Select(u => (User: u, Rank: Rank(u)))
            .Where(m => m.Rank >= 0)
            .OrderBy(m => m.Rank).ThenBy(m => m.User.Login, StringComparer.OrdinalIgnoreCase)
            .Take(most)
            .Select(m => m.User)];
    }

    /// <summary>
    /// <paramref name="text"/> with the part the cursor is in replaced by <paramref name="login"/> and a comma, ready
    /// for the next receiver; and where the cursor goes (after it).
    /// </summary>
    public static (string Text, int Caret) CompleteTo(string text, int caret, string login)
    {
        var (start, end) = ToPartAt(text, caret);
        var rest = text[end..].TrimStart(' ', ',', ';', '\n');
        var head = text[..start] + login + ", ";
        return (head + rest, head.Length);
    }

    /// <summary>
    /// The users <paramref name="term"/> finds, by login: each of its words is in the login, the name, the level, the
    /// e-mail, the hashtags or the profile (accents and case do not count). Every user when it is empty.
    /// </summary>
    public IReadOnlyList<MailUser> SearchUsers(string term)
    {
        var words = Fold(term).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. Users.Where(u =>
        {
            var all = Fold($"{u.Login} {u.Name} {u.Role} {u.Email} {u.Hashtags} {u.Profile}");
            return words.All(word => all.Contains(word, StringComparison.Ordinal));
        })];
    }

    /// <summary>
    /// The receivers become <paramref name="logins"/> (picked in the search of the users); what was typed and names no
    /// user stays after them.
    /// </summary>
    public void SetReceivers(IEnumerable<string> logins)
    {
        var unknown = ReceiversOf(ComposeTo).Unknown;
        ComposeTo = string.Join(", ", logins.Distinct(StringComparer.OrdinalIgnoreCase).Concat(unknown));
        Say(ToHint, ToIsUnknown);
    }

    // ── Writing ──

    private void Compose(IEnumerable<string> to, string subject, string text, IEnumerable<MailPicture> pictures, bool encrypt, string password, string? draftId, string? replyTo)
    {
        Users = [.. UserStore.All().Select(u => new MailUser(u.Login, u.DisplayName, u.Role.ToString(), u.Email, string.Join(' ', u.Hashtags), u.Profile,
            u.Login.Equals(Me, StringComparison.OrdinalIgnoreCase)))];
        ComposeTo = string.Join(", ", to.Distinct(StringComparer.OrdinalIgnoreCase));
        ComposePictures.Clear();
        foreach (var picture in pictures)
            ComposePictures.Add(picture);
        ComposeSubject = subject;
        ComposeText = text;
        ComposeEncrypt = encrypt;
        ComposePassword = ComposePasswordAgain = password;
        _draftId = draftId;
        _replyTo = replyTo;
        LeaveAnalysis();
        LeaveAnnotation();
        LeaveRewrite(true);
        HasUnsent = true;
        IsComposing = true;
        Pane = new object();
    }

    /// <summary>
    /// True when a message with something in it is being written: it is shown again, and what was asked (a new message,
    /// an answer) is not done over it.
    /// </summary>
    private bool KeepsUnsent()
    {
        if (!HasUnsent || (ComposeSubject.Trim().Length == 0 && ComposeText.Trim().Length == 0 && ComposePictures.Count == 0))
            return false;
        LeaveAnalysis();
        IsComposing = true;
        Pane = new object();
        Say("A message is being written: send it, keep it as a draft or discard it first", true);
        return true;
    }

    /// <summary>Back to the message being written, left to look at another one.</summary>
    [RelayCommand]
    private void Resume()
    {
        if (!HasUnsent || IsComposing)
            return;
        LeaveAnalysis();
        IsComposing = true;
        Pane = new object();
        Say("Your message, as you left it");
    }

    /// <summary>A new message, empty.</summary>
    [RelayCommand]
    private void New()
    {
        if (KeepsUnsent())
            return;
        Compose([], string.Empty, string.Empty, [], false, string.Empty, null, null);
        Say("New message: type its receivers (To), write it, SEND");
    }

    private static string Prefixed(string prefix, string subject) =>
        subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? subject : $"{prefix} {subject}".TrimEnd();

    /// <summary>The text of the message shown, quoted under an answer (nothing of an encrypted message that was not opened).</summary>
    private string Quoted(MailMessage message)
    {
        var text = Shown is null ? "[encrypted message, not opened]" : MailMessage.WithoutMarks(Shown.Text).Trim();
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(l => "> " + l);
        return $"\n\n--- {message.Date.LocalDateTime:dd/MM/yyyy HH:mm}, {NameOf(message.From)}: ---\n{string.Join('\n', lines)}";
    }

    /// <summary>An answer to the sender of the message selected (to its receivers when it is one of mine).</summary>
    [RelayCommand]
    private void Reply() => Answer(false);

    /// <summary>An answer to the sender and to the other receivers of the message selected.</summary>
    [RelayCommand]
    private void ReplyAll() => Answer(true);

    private void Answer(bool everyone)
    {
        if (SelectedRow is not { Message: { Draft: false } message } || KeepsUnsent())
            return;
        var to = everyone
            ? message.Everyone().Where(l => !l.Equals(Me, StringComparison.OrdinalIgnoreCase)).ToList()
            : message.IsFrom(Me) ? message.To.ToList() : [message.From];
        if (to.Count == 0)
            to = [Me];
        Compose(to, Prefixed("RE:", message.Subject), Quoted(message), [], message.Encrypted, _shownPassword, null, message.Id);
        Say(message.Encrypted ? "Answer: encrypted as the message is" + (_shownPassword.Length > 0 ? ", with the same password" : " (type its password)") : "Answer");
    }

    /// <summary>The message selected, with its pictures, to send to others.</summary>
    [RelayCommand]
    private void Forward()
    {
        if (SelectedRow is not { Message: { Draft: false } message } || KeepsUnsent())
            return;
        if (Shown is null)
        {
            Say("Open the encrypted message with its password before forwarding it", true);
            return;
        }
        var text = $"\n\n--- {message.Date.LocalDateTime:dd/MM/yyyy HH:mm}, {NameOf(message.From)} to {NamesOf(message.To)}: ---\n{Shown.Text}";
        Compose([], Prefixed("FW:", message.Subject), text, Shown.Pictures, message.Encrypted, _shownPassword, null, message.Id);
        Say("Forward: type its receivers (To)");
    }

    /// <summary>The draft selected, back in the form to go on with it (an encrypted one once its password opened it).</summary>
    [RelayCommand]
    private void EditDraft()
    {
        if (SelectedRow is not { Message: { Draft: true } message } || KeepsUnsent())
            return;
        if (Shown is null)
        {
            Say("Open the encrypted draft with its password to go on with it", true);
            return;
        }
        Compose(message.To, message.Subject, Shown.Text, Shown.Pictures, message.Encrypted, _shownPassword, message.Id, message.ReplyTo);
        Say("Draft opened");
    }

    /// <summary>
    /// Inserts the picture of <paramref name="file"/> in the message being written; returns what stands for it in the
    /// text (the view writes it where the cursor is), or null when it cannot be inserted (<see cref="Status"/> says why).
    /// </summary>
    public string? AddPicture(string file)
    {
        var (picture, error) = MailStore.ReadPicture(file);
        if (picture is null)
        {
            Say(error ?? "The picture cannot be inserted", true);
            return null;
        }
        ComposePictures.Add(picture);
        Say($"{picture.Name} inserted ({picture.Size / 1024.0:0} KB, kept in base 64 in the message)");
        return picture.Marker;
    }

    /// <summary>Takes <paramref name="picture"/> out of the message being written, and its mark out of the text.</summary>
    public void RemovePicture(MailPicture picture)
    {
        ComposePictures.Remove(picture);
        ComposeText = ComposeText.Replace(picture.Marker + "\n", string.Empty).Replace(picture.Marker, string.Empty);
        Say($"{picture.Name} removed");
    }

    /// <summary>The message being written, as it will be kept; null when it cannot be (<see cref="Status"/> says why).</summary>
    private async Task<MailMessage?> BuildAsync(bool draft)
    {
        var receivers = ReceiversOf(ComposeTo);
        if (!draft && receivers.Unknown.Count > 0)
        {
            Say($"No user named {string.Join(", ", receivers.Unknown)}: type a login or a name in To (the list under it completes it), or 🔍 to search the users", true);
            return null;
        }
        var to = receivers.Logins.ToList();
        if (!draft && to.Count == 0)
        {
            Say("Give at least one receiver: type a login or a name in To, or 🔍 to search the users", true);
            return null;
        }
        // A draft keeps what names nobody yet as it was typed: it is there again when the draft is opened.
        if (draft)
            to.AddRange(receivers.Unknown);
        if (!draft && ComposeSubject.Trim().Length == 0 && ComposeText.Trim().Length == 0 && ComposePictures.Count == 0)
        {
            Say("The message is empty: write a subject or a text", true);
            return null;
        }
        if (ComposeEncrypt)
        {
            if (ComposePassword.Length < JournalCrypto.MinPasswordLength)
            {
                Say($"The password of an encrypted message has {JournalCrypto.MinPasswordLength} characters at least", true);
                return null;
            }
            if (ComposePassword != ComposePasswordAgain)
            {
                Say("The two passwords are not the same", true);
                return null;
            }
        }
        var message = new MailMessage
        {
            Id = _draftId ?? string.Empty,
            From = Me,
            To = to,
            Subject = ComposeSubject.Trim(),
            Date = DateTimeOffset.Now,
            Draft = draft,
            ReplyTo = _replyTo,
            ReadBy = [Me]
        };
        var content = new MailContent { Text = ComposeText.TrimEnd(), Pictures = [.. ComposePictures] };
        if (!draft)
            content.Entities = await EntitiesAsync(message.Subject, content.Text);
        if (ComposeEncrypt)
        {
            var password = ComposePassword;
            IsBusy = true;
            Say("Encrypting (AES-256)…");
            try
            {
                message.Cipher = await Task.Run(() => MailStore.Encrypt(content, password));
            }
            finally
            {
                IsBusy = false;
            }
            message.Encrypted = true;
            message.PictureCount = content.Pictures.Count;
        }
        else
        {
            message.Text = content.Text;
            message.Pictures = content.Pictures;
            message.Entities = content.Entities;
        }
        return message;
    }

    /// <summary>What is told of the entities of the message just sent (how many, or why none).</summary>
    private string _entitiesNote = string.Empty;

    /// <summary>
    /// The entities of a message being sent, found by the NER of the server in its subject and its text; none when
    /// there is no NER, nothing to read, or no answer in time (<see cref="_entitiesNote"/> tells which): the message is
    /// sent anyway.
    /// </summary>
    private async Task<List<MailEntity>> EntitiesAsync(string subject, string text)
    {
        _entitiesNote = string.Empty;
        var read = MailMessage.ForEntities(subject, text);
        if (_ner is null || read.Length == 0)
            return [];
        IsBusy = true;
        Say("Finding the entities (NER)…");
        using var timeout = new CancellationTokenSource(NerTimeout);
        try
        {
            // The answer or the end of the wait, whichever comes first (a server that does not answer does not hold the message).
            var entities = MailMessage.EntitiesOf(await _ner(read, timeout.Token).WaitAsync(NerTimeout));
            _entitiesNote = entities.Count == 0 ? " · no entity found (NER)" : $" · {entities.Count} entit{(entities.Count == 1 ? "y" : "ies")} found (NER)";
            return entities;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or OperationCanceledException or TimeoutException or System.Text.Json.JsonException or InvalidOperationException)
        {
            var why = ex is OperationCanceledException or TimeoutException ? $"no answer in {NerTimeout.TotalSeconds:0} s" : ex.Message;
            _entitiesNote = " · sent without its entities (the NER of the server: " + why + ")";
            ActivityLog.Error("Message", "The entities of a message could not be found by the NER", ex);
            return [];
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sends the message being written to the users named in <see cref="ComposeTo"/>.</summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        if (!IsComposing || IsBusy)
            return;
        try
        {
            if (await BuildAsync(false) is not { } message)
                return;
            MailStore.Save(message);
            var note = _entitiesNote;
            ActivityLog.Info("Message", $"{Me} sent a message to {string.Join(", ", message.To)}: {(message.Encrypted ? "encrypted (AES-256)" : "not encrypted")}, {message.Pictured} picture(s){note}");
            Close();
            Say($"Sent to {NamesOf(message.To)}" + (message.Encrypted ? " · encrypted (AES-256): give them the password by another way" : string.Empty) + note,
                note.Contains("without its entities", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Say("The message could not be written: " + ex.Message, true);
            ActivityLog.Error("Message", "A message could not be written", ex);
        }
    }

    /// <summary>Keeps the message being written in Drafts, to go on with it later.</summary>
    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (!IsComposing || IsBusy)
            return;
        try
        {
            if (await BuildAsync(true) is not { } message)
                return;
            MailStore.Save(message);
            Close();
            Say("Kept in Drafts" + (message.Encrypted ? " · encrypted (AES-256)" : string.Empty));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Say("The draft could not be written: " + ex.Message, true);
            ActivityLog.Error("Message", "A draft could not be written", ex);
        }
    }

    /// <summary>Leaves the message being written (a draft opened stays as it was kept).</summary>
    [RelayCommand]
    private void Discard()
    {
        if (!IsComposing)
            return;
        Close();
        Say("Message left");
    }

    private void Close()
    {
        LeaveRewrite(true);
        HasUnsent = false;
        IsComposing = false;
        ComposePassword = ComposePasswordAgain = string.Empty;
        ComposePictures.Clear();
        ComposeText = ComposeSubject = string.Empty;
        _draftId = _replyTo = null;
        Refresh();
        Pane = new object();
    }
}
