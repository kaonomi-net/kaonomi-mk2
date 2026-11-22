using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Data;
using KAONOMI.ChatGodot.Tools.Writing;

namespace KAONOMI.ChatGodot.Tools.Daily;

/// <summary>
/// /message (or /msg): the internal mail between the users of the application, a small Outlook without e-mail. Top:
/// NEW MESSAGE (⌘/Ctrl N), REPLY, REPLY ALL, FORWARD, DELETE (to the trash; from the trash, for good), RESTORE (in the
/// trash), NOT READ (in the inbox), ANALYSE (the last messages exchanged with the sender of the message selected, read
/// by the model of the chat: the subjects, the evolution on the substance, the emotional side; shown in place of the
/// message, with COPY, AGAIN, CLOSE), I'M ASPERGER (the message selected, one that was received, explained by the model of
/// the chat to a reader with an Asperger profile: what it says literally, what is implied, the emotions of its sender,
/// what is expected, how to answer; shown the same way), ANNOTATE (a note of yours on the text selected with the mouse
/// in the message, on a picture that was clicked, or on both: kept with the message, seen by you only, listed under it,
/// its text marked in the message), → IDEA (the message as a new unsaved idea in /idea: its subject, its text, its
/// entities as tags, your notes), → TASK (the same in the form of a new task of /kanban, created there by CREATE),
/// ↻ (F5) and the search; the right click of the text of a message brings the tools of the right click of /work
/// (<see cref="MessageTools"/>: correct the spelling, rewrite, evaluate on a message being written; evaluate and
/// translate on one that is read); BACK TO MY MESSAGE while a message being written waits
/// (another one is looked at: nothing of it is lost). Left: the folders of the user logged in (Inbox with what is
/// not read, Sent, Drafts, Trash). Middle: the messages of the folder (who, date, subject, 🔒 encrypted, 🖼 pictures,
/// the start of the text; a dot before what is not read). Right: the message selected (its pictures where they stand
/// in the text, each saved with 💾; under it the entities the NER of the server found when it was sent, by type; an
/// encrypted one asks for its password) or the one being written: the receivers typed in To (logins or names
/// separated by commas, completed while they are typed by the users that match in a list under the field; 🔍 opens the
/// search of the users, where they are ticked), the subject, the text, INSERT IMAGE (PNG, JPEG, WebP, kept in base 64
/// in the message, a mark
/// where the cursor is), ASPERGER TO MOLDU (the text rewritten by the model of the chat for a neurotypical reader, its
/// author having an Asperger profile; beside it what was changed, and MY TEXT to put back what was written), ENCRYPT
/// (AES-256) with its password twice, SEND (⌘/Ctrl Enter), SAVE DRAFT, DISCARD.
/// </summary>
[ToolView(typeof(MessageWindowViewModel), "Messages", "✉", 1280, 800)]
public sealed class MessageView : IToolView
{
    private static readonly string[] PictureFilters = ["*.png, *.jpg, *.jpeg, *.webp ; Pictures (PNG, JPEG, WebP)"];

    /// <summary>The widest a picture is shown in a message.</summary>
    private const float PictureWidth = 640;

    /// <summary>What is selected in the message shown, for ANNOTATE: its texts (one of them holds the selection) and the picture clicked.</summary>
    private sealed class Picked
    {
        public List<RichTextLabel> Texts { get; } = [];
        public string? PictureId { get; set; }
        public event Action? PictureChanged;

        /// <summary>The text the mouse selected in the message (empty: none).</summary>
        public string Text => Texts.Where(t => GodotObject.IsInstanceValid(t)).Select(t => t.GetSelectedText()).FirstOrDefault(t => t.Trim().Length > 0) ?? string.Empty;

        public void Pick(string? pictureId)
        {
            PictureId = pictureId;
            PictureChanged?.Invoke();
        }
    }

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (MessageWindowViewModel)viewModel;
        var picked = new Picked();

        // The toolbar.
        var search = Ui.Line(vm, nameof(vm.Term), "Search the folder (subject, name, text)");
        search.CustomMinimumSize = new Vector2(240, 0);
        var restore = Ui.Command("⤺ RESTORE", vm.RestoreCommand, tooltip: "Put the message back in its folder");
        var unread = Ui.Command("● NOT READ", vm.MarkUnreadCommand, tooltip: "Mark the message as not read");
        var delete = Ui.Command("🗑 DELETE", vm.DeleteCommand, tooltip: "To the trash; from the trash: removed for good (for you)");
        Bind.When(restore, vm, () =>
        {
            restore.Visible = vm.SelectedFolder.Folder == MailFolder.Trash;
            unread.Visible = vm.SelectedFolder.Folder == MailFolder.Inbox;
            delete.Text = vm.SelectedFolder.Folder == MailFolder.Trash ? "🗑 DELETE FOR GOOD" : "🗑 DELETE";
        }, nameof(vm.SelectedFolder));
        var resume = Ui.Command("✎ BACK TO MY MESSAGE", vm.ResumeCommand, primary: true, tooltip: "The message you were writing, as you left it");
        Bind.When(resume, vm, () => resume.Visible = vm.HasUnsent && !vm.IsComposing, nameof(vm.HasUnsent), nameof(vm.IsComposing));
        var bar = WritingUi.Flow(6, 6,
            Ui.Command("✉ NEW MESSAGE", vm.NewCommand, primary: true, tooltip: "Write a new message (Ctrl+N / ⌘N)"),
            resume,
            Ui.Command("↩ REPLY", vm.ReplyCommand, tooltip: "Answer the sender"),
            Ui.Command("↩↩ REPLY ALL", vm.ReplyAllCommand, tooltip: "Answer the sender and the other receivers"),
            Ui.Command("→ FORWARD", vm.ForwardCommand, tooltip: "Send the message, with its pictures, to others"),
            Ui.Command("🔎 ANALYSE", vm.AnalyseCommand, tooltip: "The last messages exchanged with the sender of the message selected, analysed by the model of the chat: the subjects, how the conversation moves on the substance, its emotional side (an encrypted message is not read)"),
            Ui.Command("💡 I'M ASPERGER", vm.ExplainCommand, tooltip: "The message selected, one you received, explained by the model of the chat to a reader with an Asperger profile: what it says literally, what is implied (irony, polite formulas, figures of speech), the emotions of its sender with the words that show them, what is expected of you, how to answer (an encrypted message: once its password opened it)"),
            Ui.Action("✎ ANNOTATE", () => vm.BeginAnnotation(picked.Text, picked.PictureId),
                tooltip: "A note of yours on a part of the message: select some of its text with the mouse, or click one of its pictures (or both), then this button. The note is kept with the message and seen by you only"),
            Ui.Action("→ IDEA", async () =>
            {
                if (vm.IdeaOfSelected() is not { } idea)
                    return;
                if (await Data.SearchOpener.OpenToolAsync<IdeaWindowViewModel>(shell, "/idea") is not { } ideas)
                {
                    vm.Status = "/idea could not be opened";
                    return;
                }
                ideas.NewIdeaFrom(idea.Title, idea.Description, idea.Comment, idea.Tags);
            }, tooltip: "Opens /idea with a new idea filled from the message selected: its subject as the title, its text as the description, its entities as tags, where it comes from and your notes on it as the comment. Nothing is saved before SAVE there"),
            Ui.Action("→ TASK", async () =>
            {
                if (vm.TaskOfSelected() is not { } task)
                    return;
                if (await Data.SearchOpener.OpenToolAsync<KanbanWindowViewModel>(shell, "/kanban") is not { } kanban)
                {
                    vm.Status = "/kanban could not be opened";
                    return;
                }
                kanban.NewTaskFromMessage(task.Title, task.Description, task.Comment, task.Tags);
            }, tooltip: "Opens /kanban with the form of a new task filled from the message selected: its subject as the name, its text as the description, its entities as tags, where it comes from and your notes on it as the comment; status backlog. The task is created by CREATE there"),
            delete, restore, unread,
            Ui.Command("↻", vm.RefreshCommand, tooltip: "Read the messages again (F5)"),
            Ui.Fill(search));

        // The folders.
        var me = Eve.Eve.Caption(vm.NameOf(vm.Me).ToUpperInvariant(), Eve.Eve.TextBright, 12);
        me.ClipText = true;
        var folders = Ui.Column(3, Ui.Margin(me, 4, 4, 4, 6));
        foreach (var folder in vm.Folders)
        {
            var label = Ui.Label(folder, nameof(folder.Label), Eve.Eve.Text, 13);
            folders.AddChild(Choice(vm, nameof(vm.SelectedFolder), () => ReferenceEquals(vm.SelectedFolder, folder), () => vm.SelectedFolder = folder, label, 34));
        }
        folders.CustomMinimumSize = new Vector2(150, 0);

        // The messages of the folder.
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        var empty = Ui.Placeholder("No message");
        Bind.Items(list, vm.Rows, row => Row(vm, row), empty);
        var middle = Ui.Column(0, Ui.FillHeight(Ui.Scroll(list)), empty);
        middle.CustomMinimumSize = new Vector2(260, 0);

        // The message selected, or the one being written.
        TextEdit? body = null;
        var pane = Ui.Swap(vm, nameof(vm.Pane), _ =>
        {
            body = null;
            picked = new Picked();
            return vm.IsComposing ? Composer(vm, shell, out body) : vm.ShowsAnalysis ? Analysis(vm) : Reader(vm, shell, picked);
        });

        var page = Ui.Column(6, Ui.Toolbar(bar), Ui.SplitH(folders, Ui.SplitH(middle, pane, 360), 180),
            Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError), nameof(vm.IsBusy)));

        WritingUi.Shortcuts(page, key =>
        {
            if (WritingUi.IsCommand(key, Key.N))
                return WritingUi.Run(vm.NewCommand);
            if (key.Keycode == Key.F5)
                return WritingUi.Run(vm.RefreshCommand);
            if (vm.IsComposing && key.Keycode is Key.Enter or Key.KpEnter && key.IsCommandOrControlPressed())
                return WritingUi.Run(vm.SendCommand);
            if (vm.IsAnnotating && key.Keycode is Key.Enter or Key.KpEnter && key.IsCommandOrControlPressed())
                return WritingUi.Run(vm.SaveAnnotationCommand);
            return false;
        });

        // A message sent meanwhile from another chat of the machine: seen within a few seconds.
        var timer = new Timer { WaitTime = 10, Autostart = true };
        timer.Timeout += () => vm.RefreshIfChanged();
        page.AddChild(timer);
        return page;
    }

    /// <summary>A message of the list: a dot when it is not read, who, its date, its subject, what it holds, the start of its text.</summary>
    private static Control Row(MessageWindowViewModel vm, MailRowViewModel row)
    {
        var dot = Bind.Visible(Eve.Eve.Text_("●", Eve.Eve.Cyan, 10), row, nameof(row.IsUnread));
        var who = Eve.Eve.Text_(row.Who, Eve.Eve.TextBright, 13);
        who.ClipText = true;
        who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var subject = WritingUi.Clamp(Eve.Eve.Text_(row.Subject, Eve.Eve.Text, 13), 1);
        Bind.When(subject, row, () =>
        {
            who.AddThemeColorOverride("font_color", row.IsUnread ? Eve.Eve.Cyan : Eve.Eve.TextBright);
            subject.AddThemeColorOverride("font_color", row.IsUnread ? Eve.Eve.TextBright : Eve.Eve.Text);
        }, nameof(row.IsUnread));
        var content = Ui.Column(2,
            Ui.Row(6, dot, who, Eve.Eve.Text_(row.Marks, Eve.Eve.Amber, 11), Eve.Eve.Text_(row.DateLabel, Eve.Eve.Muted, 10)),
            subject,
            WritingUi.Clamp(Eve.Eve.Text_(row.Preview, Eve.Eve.Muted, 11), 1));
        return Choice(vm, nameof(vm.SelectedRow), () => ReferenceEquals(vm.SelectedRow, row), () => vm.SelectedRow = row, content, 62);
    }

    /// <summary>
    /// A row that is chosen by a click (a folder, a message) and stays shown as chosen when it is clicked again (the
    /// toggle of the row would show it as left).
    /// </summary>
    private static Button Choice(MessageWindowViewModel vm, string selected, Func<bool> isSelected, Action select, Control content, int minHeight)
    {
        Button? button = null;
        button = WritingUi.ResultRow(vm, selected, isSelected, () =>
        {
            select();
            button?.SetPressedNoSignal(isSelected());
        }, content, minHeight);
        return button;
    }

    // ── The analysis of a conversation ──

    /// <summary>
    /// What the model says, in place of the message selected. ANALYSE: whose conversation and how many messages, then
    /// its answer (the subjects, the evolution on the substance, the emotional side, what to remember). I'M ASPERGER:
    /// which message, then its explanation (what it says, what is implied, the emotions of its sender, what is
    /// expected, how to answer). COPY, AGAIN and CLOSE; a wait while the model is asked.
    /// </summary>
    private static Control Analysis(MessageWindowViewModel vm)
    {
        var explained = vm.Insight == MailInsight.Explained;
        var title = Eve.Eve.Text_((explained ? "💡 " : "🔎 ") + vm.AnalysisTitle, Eve.Eve.TextBright, 15, wrap: true);
        // The note takes the room the buttons leave, on as many lines as it needs (a long one would push them out of the pane).
        var actions = Ui.Row(6,
            Ui.Fill(Eve.Eve.Text_(explained
                ? "By the model of the chat · a reading, not a certainty · not kept"
                : "By the model of the chat, from the messages only · not kept", Eve.Eve.Muted, 11, wrap: true)));
        if (!vm.IsAnalysing)
        {
            actions.AddChild(Ui.Action("⧉ COPY", () =>
            {
                Dialogs.Copy(vm.AnalysisTitle + "\n\n" + vm.Analysis);
                vm.Status = explained ? "Explanation copied" : "Analysis copied";
            }, tooltip: explained ? "Copy the explanation (Markdown)" : "Copy the analysis (Markdown)"));
            actions.AddChild(Ui.Command("↻ AGAIN", vm.AgainCommand, tooltip: "Ask the model again"));
        }
        actions.AddChild(Ui.Command(vm.IsAnalysing ? "■ STOP" : "✕ CLOSE", vm.CloseAnalysisCommand, primary: !vm.IsAnalysing, tooltip: "Back to the message"));
        // Each button keeps its own height when the note beside it takes two lines.
        foreach (var child in actions.GetChildren().OfType<BaseButton>())
            child.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Control content;
        if (vm.IsAnalysing)
        {
            var wait = new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(220, 5), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            content = Ui.Margin(Ui.Column(10, Eve.Eve.Text_(explained ? "The model reads the message…" : "The model reads the messages…", Eve.Eve.Muted, 13), wait), 8, 18, 8, 8);
        }
        else
        {
            content = Ui.Scroll(Ui.Margin(Ui.Markdown(vm.Analysis), 8, 8, 12, 12));
        }
        return Ui.Column(6, Ui.Margin(Ui.Column(4, title, actions), 8, 4, 8, 0), Eve.Eve.Separator(), Ui.FillHeight(content));
    }

    // ── Reading ──

    private static Control Reader(MessageWindowViewModel vm, Shell shell, Picked picked)
    {
        if (vm.SelectedRow is not { } row)
            return Ui.Placeholder("Select a message, or write one: ✉ NEW MESSAGE");
        var message = row.Message;

        var subject = Eve.Eve.Text_(row.Subject, Eve.Eve.TextBright, 19, wrap: true);
        var envelope = Eve.Eve.Text_($"From {vm.NameOf(message.From)}  →  {(message.To.Count > 0 ? vm.NamesOf(message.To) : "nobody yet")}", Eve.Eve.Text, 12, wrap: true);
        var marks = Ui.Row(6, Eve.Eve.Text_(message.Date.LocalDateTime.ToString("dddd d MMMM yyyy, HH:mm"), Eve.Eve.Muted, 11));
        if (message.Draft)
            marks.AddChild(Ui.Badge("draft", Eve.Eve.Amber));
        if (message.Encrypted)
            marks.AddChild(Ui.Badge(vm.IsLocked ? "🔒 encrypted · AES-256" : "🔓 decrypted · AES-256", vm.IsLocked ? Eve.Eve.Amber : Eve.Eve.Green));
        if (message.Pictured > 0)
            marks.AddChild(Ui.Badge($"🖼 {message.Pictured}", Eve.Eve.Cyan));
        marks.AddChild(Ui.Spring());
        if (message.Encrypted && !vm.IsLocked)
            marks.AddChild(Ui.Command("🔒 LOCK", vm.LockCommand, tooltip: "Hide the message again"));
        if (message.Draft)
            marks.AddChild(Ui.Command("✎ EDIT", vm.EditDraftCommand, primary: true, tooltip: "Go on with this draft"));
        var header = Ui.Column(4, subject, envelope, marks);

        Control content;
        if (vm.IsLocked)
        {
            var password = Bind.Edit(new LineEdit { PlaceholderText = "Password of the message", Secret = true, CustomMinimumSize = new Vector2(280, 0) }, vm, nameof(vm.UnlockPassword));
            password.TextSubmitted += _ => Bind.Run(vm.UnlockCommand);
            var explain = Eve.Eve.Text_("This message is encrypted with AES-256 (GCM). Its text and its pictures are read with the password its sender chose, " +
                "given to you by another way. Nothing of it is kept decrypted: it is locked again when another message is selected.", Eve.Eve.Muted, 12, wrap: true);
            explain.CustomMinimumSize = new Vector2(320, 0);
            content = Ui.Margin(Ui.Column(10, Eve.Eve.Text_("🔒", Eve.Eve.Amber, 34), explain, Ui.Row(6, password, Ui.Command("UNLOCK", vm.UnlockCommand, primary: true))), 8, 18, 8, 8);
            // The keyboard goes to the password once the click that selected the message is over (the window takes the
            // keyboard at that click: asked at once, it would be taken back).
            password.Ready += () => password.GetTree().CreateTimer(0.15).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(password) && password.IsInsideTree())
                    password.GrabFocus();
            };
        }
        else
        {
            var notes = vm.MyAnnotations;
            var quotes = notes.Select(n => n.Quote).Where(q => q.Length > 0).Distinct().ToList();
            var parts = Ui.Column(10);
            if (vm.IsAnnotating)
                parts.AddChild(NoteForm(vm));
            foreach (var part in MailMessage.Parts(vm.Shown ?? new MailContent()))
                parts.AddChild(part.Picture is { } picture ? Picture(vm, picture, picked, notes.Count(n => n.PictureId == picture.Id)) : Text(vm, shell, part.Text, quotes, picked));
            if (parts.GetChildCount() == (vm.IsAnnotating ? 1 : 0))
                parts.AddChild(Eve.Eve.Text_("(no text)", Eve.Eve.Muted, 12));
            if (notes.Count > 0)
                parts.AddChild(Notes(vm, notes));
            if (vm.Shown is { Entities.Count: > 0 } shown)
                parts.AddChild(Entities(shown.Entities));
            content = Ui.Scroll(Ui.Margin(parts, 8, 8, 12, 12));
        }
        return Ui.Column(6, Ui.Margin(header, 8, 4, 8, 0), Eve.Eve.Separator(), Ui.FillHeight(content));
    }

    /// <summary>
    /// The entities the NER found in the message when it was sent, under its text: by type, each with its name (how
    /// many times when more than once), framed when the ontology knows it.
    /// </summary>
    private static Control Entities(List<MailEntity> entities)
    {
        var flow = WritingUi.Flow(6, 5);
        foreach (var entity in entities)
        {
            var color = entity.Type switch
            {
                "PERSON" => Eve.Eve.Cyan,
                "ORGANISATION" or "ORGANIZATION" or "ORG" or "COMPANY" => Eve.Eve.Amber,
                "DATE" or "TIME" or "MONEY" or "EMAIL" or "URL" or "PHONE" or "NUMBER" or "CODE" => Eve.Eve.Muted,
                _ => Eve.Eve.Green
            };
            var chip = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Stop,
                TooltipText = $"{entity.Type}: {entity.Name}" + (entity.Count > 1 ? $" · named {entity.Count} times" : string.Empty) + (entity.Known ? " · known in the ontology" : string.Empty)
            };
            chip.AddThemeStyleboxOverride("panel", Eve.Eve.Box(color with { A = 0.14f }, color with { A = entity.Known ? 0.9f : 0.35f }, 0, entity.Known ? 2 : 1, 9));
            chip.AddChild(Ui.Margin(Ui.Row(5,
                Eve.Eve.Caption(entity.Type, color, 9),
                Eve.Eve.Text_(entity.Name + (entity.Count > 1 ? $" ×{entity.Count}" : string.Empty), Eve.Eve.TextBright, 12)), 8, 2, 8, 2));
            flow.AddChild(chip);
        }
        return Ui.Column(4, Eve.Eve.Separator(), Eve.Eve.Caption($"ENTITIES FOUND WHEN IT WAS SENT (NER) · {entities.Count}", Eve.Eve.Muted, 10), flow);
    }

    /// <summary>
    /// A part of the text of a message, as it was written (its lines kept), that can be selected and copied; what the
    /// notes of the user are about (<paramref name="quotes"/>) is marked in it. Its selection stays when a button is
    /// clicked (ANNOTATE reads it).
    /// </summary>
    private static Control Text(MessageWindowViewModel vm, Shell shell, string text, List<string> quotes, Picked picked)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = Marked(text, quotes),
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            DeselectOnFocusLossEnabled = false,
            // Its right click shows the tools of /work on a message that is read (MessageTools), not the menu of Godot.
            ContextMenuEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Select some text with the mouse, then ✎ ANNOTATE, to write a note on it"
        };
        picked.Texts.Add(label);
        label.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press)
                return;
            MessageTools.ShowReadingMenu(vm, shell, label, press.GlobalPosition,
                () => vm.BeginAnnotation(label.GetSelectedText().Trim().Length > 0 ? label.GetSelectedText() : picked.Text, picked.PictureId), picked.PictureId is not null);
            label.AcceptEvent();
        };
        return label;
    }

    /// <summary>
    /// <paramref name="text"/> as BBCode that shows it as it is (its « [ » are not read as tags), with a coloured
    /// background wherever one of <paramref name="quotes"/> stands.
    /// </summary>
    private static string Marked(string text, List<string> quotes)
    {
        text = text.ReplaceLineEndings("\n");
        var marked = new bool[text.Length];
        foreach (var quote in quotes)
            for (var at = text.IndexOf(quote, StringComparison.Ordinal); at >= 0; at = text.IndexOf(quote, at + quote.Length, StringComparison.Ordinal))
                Array.Fill(marked, true, at, quote.Length);
        var color = (Eve.Eve.Amber with { A = 0.32f }).ToHtml(true);
        var code = new System.Text.StringBuilder(text.Length + 32);
        for (var i = 0; i < text.Length; i++)
        {
            if (marked[i] && (i == 0 || !marked[i - 1]))
                code.Append("[bgcolor=#").Append(color).Append(']');
            code.Append(text[i] == '[' ? "[lb]" : text[i].ToString());
            if (marked[i] && (i == text.Length - 1 || !marked[i + 1]))
                code.Append("[/bgcolor]");
        }
        return code.ToString();
    }

    /// <summary>The note being written on a part of the message: what it is about (the text selected, the picture clicked), the note, SAVE NOTE (⌘/Ctrl Enter) and CANCEL.</summary>
    private static Control NoteForm(MessageWindowViewModel vm)
    {
        var about = Ui.Column(3);
        if (vm.AnnotationQuote.Length > 0)
            about.AddChild(WritingUi.Clamp(Eve.Eve.Text_("« " + vm.AnnotationQuote.ReplaceLineEndings(" ") + " »", Eve.Eve.Text, 12, wrap: true), 3));
        if (vm.PictureName(vm.AnnotationPictureId) is { } name)
            about.AddChild(Eve.Eve.Text_("🖼 " + name, Eve.Eve.Text, 12));
        var note = Ui.Lines(vm, nameof(vm.AnnotationNote), 64, "Your note on this part of the message…");
        note.Ready += () => note.GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(note) && note.IsInsideTree())
                note.GrabFocus();
        };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Eve.Eve.Box(Eve.Eve.Amber with { A = 0.08f }, Eve.Eve.Amber with { A = 0.7f }, 5));
        panel.AddChild(Ui.Margin(Ui.Column(6,
            Eve.Eve.Caption("✎ NEW NOTE ON", Eve.Eve.Amber, 10),
            about,
            note,
            Ui.Row(6, Eve.Eve.Text_("Kept with the message · seen by you only", Eve.Eve.Muted, 11), Ui.Spring(),
                Ui.Command("CANCEL", vm.CancelAnnotationCommand),
                Ui.Command("SAVE NOTE", vm.SaveAnnotationCommand, primary: true, tooltip: "Keep the note with the message (Ctrl+Enter / ⌘Enter)"))), 8, 6, 8, 6));
        return panel;
    }

    /// <summary>The notes of the user on the message, under its text: what each is about (the text, the picture), the note, its date, ✕ to remove it.</summary>
    private static Control Notes(MessageWindowViewModel vm, List<MailAnnotation> notes)
    {
        var list = Ui.Column(6);
        foreach (var note in notes)
        {
            var lines = Ui.Column(2);
            if (note.Quote.Length > 0)
                lines.AddChild(WritingUi.Clamp(Eve.Eve.Text_("« " + note.Quote.ReplaceLineEndings(" ") + " »", Eve.Eve.Muted, 11, wrap: true), 2));
            if (note.PictureId is not null)
                lines.AddChild(Eve.Eve.Text_("🖼 " + (vm.PictureName(note.PictureId) ?? "a picture that is gone"), Eve.Eve.Muted, 11));
            lines.AddChild(Eve.Eve.Text_(note.Note, Eve.Eve.TextBright, 13, wrap: true));
            lines.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var remove = Ui.Action("✕", () => _ = vm.RemoveAnnotationAsync(note), tooltip: "Remove this note");
            remove.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            var date = Eve.Eve.Text_(note.Date.LocalDateTime.ToString("dd/MM/yyyy HH:mm"), Eve.Eve.Muted, 10);
            date.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", Eve.Eve.Box(Eve.Eve.Amber with { A = 0.08f }, Eve.Eve.Amber with { A = 0.45f }, 5));
            panel.AddChild(Ui.Margin(Ui.Row(8, lines, date, remove), 8, 4, 6, 4));
            list.AddChild(panel);
        }
        return Ui.Column(4, Eve.Eve.Separator(), Eve.Eve.Caption($"✎ YOUR NOTES · {notes.Count} · SEEN BY YOU ONLY", Eve.Eve.Muted, 10), list);
    }

    /// <summary>The texture of a picture kept in base 64; null when it cannot be read.</summary>
    private static ImageTexture? Texture(MailPicture picture)
    {
        try
        {
            var bytes = Convert.FromBase64String(picture.Data);
            var image = new Image();
            // The kind told first, then the others (a file named .png that is a JPEG).
            Func<byte[], Error>[] readers = picture.Mime switch
            {
                "image/jpeg" => [image.LoadJpgFromBuffer, image.LoadPngFromBuffer, image.LoadWebpFromBuffer],
                "image/webp" => [image.LoadWebpFromBuffer, image.LoadPngFromBuffer, image.LoadJpgFromBuffer],
                _ => [image.LoadPngFromBuffer, image.LoadJpgFromBuffer, image.LoadWebpFromBuffer]
            };
            return readers.Any(read => read(bytes) == Error.Ok) && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A picture of a message at its place in the text: shown <see cref="PictureWidth"/> wide at most, its name, its size, 💾.</summary>
    private static Control Picture(MessageWindowViewModel vm, MailPicture picture, Picked picked, int notes)
    {
        var caption = Eve.Eve.Text_($"🖼 {picture.Name} · {picture.Size / 1024.0:0} KB" + (notes > 0 ? $" · ✎ {notes}" : string.Empty), notes > 0 ? Eve.Eve.Amber : Eve.Eve.Muted, 11);
        if (Texture(picture) is not { } texture)
            return Ui.Row(6, caption, Eve.Eve.Text_("this picture cannot be shown", Eve.Eve.Red, 11));
        // As wide as the message lets it be, PictureWidth at most, never larger than it is: its height follows.
        var view = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"{picture.Name} · {texture.GetWidth()}×{texture.GetHeight()} · click to select it (again: not selected), then ✎ ANNOTATE, to write a note on it"
        };
        // Clicked: selected for ANNOTATE (a frame around it); a picture with notes keeps a thin frame.
        var frame = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        void Frame()
        {
            var selected = picked.PictureId == picture.Id;
            frame.AddThemeStyleboxOverride("panel", Eve.Eve.Box(Colors.Transparent,
                selected ? Eve.Eve.Cyan : notes > 0 ? Eve.Eve.Amber with { A = 0.6f } : Colors.Transparent, 4, selected ? 2 : 1, 3));
        }
        Frame();
        picked.PictureChanged += () =>
        {
            if (GodotObject.IsInstanceValid(frame))
                Frame();
        };
        view.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                return;
            picked.Pick(picked.PictureId == picture.Id ? null : picture.Id);
            vm.Status = picked.PictureId is null ? string.Empty : $"{picture.Name} selected: ✎ ANNOTATE writes a note on it";
            view.AcceptEvent();
        };
        frame.AddChild(view);
        void Fit()
        {
            var width = Math.Min(Math.Min(view.Size.X, PictureWidth), texture.GetWidth());
            var height = MathF.Round(width * texture.GetHeight() / texture.GetWidth());
            if (width > 0 && Math.Abs(view.CustomMinimumSize.Y - height) > 0.5f)
                view.CustomMinimumSize = new Vector2(0, height);
        }
        view.Resized += Fit;
        view.Ready += Fit;
        var save = Ui.Action("💾", async () =>
        {
            if (await Dialogs.SaveFile("Save the picture of the message", picture.Name, ["* ; All files"]) is not { } path)
                return;
            try
            {
                File.WriteAllBytes(path, Convert.FromBase64String(picture.Data));
                vm.Status = $"Saved: {path}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                vm.Status = "The picture could not be saved: " + ex.Message;
            }
        }, tooltip: "Save this picture as a file");
        return Ui.Column(3, frame, Ui.Row(6, caption, save));
    }

    // ── Writing ──

    private static Control Composer(MessageWindowViewModel vm, Shell shell, out TextEdit body)
    {
        // The receivers: logins or names typed, completed by the users that match; 🔍 searches the users.
        var to = Ui.Line(vm, nameof(vm.ComposeTo), "Logins or names of users, separated by commas");
        to.TooltipText = "The receivers: the login or the name of a user, then a comma for the next one. While you type, the users that match are listed under the field: ↑ ↓ choose, Tab or Enter writes the login";
        to.AddChild(new ReceiverCompletion(to, vm));
        var search = Ui.Action("🔍", () => PickReceivers(vm, shell, to), tooltip: "Search the users (login, name, level, e-mail, hashtags, profile) and tick the receivers");
        var named = Ui.Label(vm, nameof(vm.ToHint), Eve.Eve.Muted, 11, wrap: true);
        Bind.When(named, vm, () =>
        {
            named.Visible = vm.ToHint.Length > 0;
            named.AddThemeColorOverride("font_color", vm.ToIsUnknown ? Eve.Eve.Red : Eve.Eve.Muted);
        }, nameof(vm.ToHint));

        var subject = Ui.Line(vm, nameof(vm.ComposeSubject), "Subject");
        subject.AddThemeFontSizeOverride("font_size", 15);
        var text = Ui.Lines(vm, nameof(vm.ComposeText), 200, "Write your message…");
        text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body = text;
        // The tools of the right click of /work, first in the menu of the text.
        TextMenu.Extend(text, (target, isSelection, replace, insert) => MessageTools.ForWriting(vm, shell, text, target, isSelection, replace, insert));

        // The pictures inserted: a small view of each, its name, ✕.
        var pictures = WritingUi.Flow(8, 6);
        Bind.Items(pictures, vm.ComposePictures, picture => Thumbnail(vm, picture));
        var insert = Ui.Action("🖼 INSERT IMAGE…", async () =>
        {
            foreach (var file in await Dialogs.OpenFiles("Pictures to insert in the message", PictureFilters))
            {
                if (vm.AddPicture(file) is not { } mark || !GodotObject.IsInstanceValid(text))
                    continue;
                // The mark alone on its line, where the cursor is.
                var line = text.GetLine(text.GetCaretLine());
                text.InsertTextAtCaret((text.GetCaretColumn() > 0 && line.Length > 0 ? "\n" : string.Empty) + mark + "\n");
            }
            if (GodotObject.IsInstanceValid(text))
                text.GrabFocus();
        }, tooltip: "PNG, JPEG or WebP pictures, kept in base 64 in the message; a mark [[image:…]] stands where each is shown");

        // AES-256.
        var password = Bind.Edit(new LineEdit { PlaceholderText = $"Password ({JournalCrypto.MinPasswordLength} characters at least)", Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }, vm, nameof(vm.ComposePassword));
        var again = Bind.Edit(new LineEdit { PlaceholderText = "The same password again", Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }, vm, nameof(vm.ComposePasswordAgain));
        var passwords = Bind.Visible(Ui.Row(6, password, again), vm, nameof(vm.ComposeEncrypt));
        passwords.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var encrypt = Ui.Check("🔒 ENCRYPT (AES-256)", vm, nameof(vm.ComposeEncrypt),
            "The text and the pictures are encrypted with AES-256 (GCM) under this password; the subject, the sender, the receivers and the date stay readable. Give the password to the receivers by another way: without it the message cannot be read, by anyone.");

        // ASPERGER TO MOLDU: the text rewritten for a neurotypical reader; beside it, what was changed.
        var moldu = Ui.Command("🪄 ASPERGER TO MOLDU", vm.ToMolduCommand, tooltip: "You have an Asperger profile, your reader has not (a « moldu »): the model of the chat rewrites what you wrote so that a neurotypical reader takes it as you mean it: the same facts and requests, with the greeting, the intention, the courtesy and the closing such a reader expects. What is quoted of the message answered and the pictures stay as they are. Nothing is sent: read it, change it, ↩ MY TEXT puts back what you wrote");
        var stop = Ui.Command("■ STOP", vm.StopRewriteCommand, tooltip: "Stop the model: your message stays as you wrote it");
        var mine = Ui.Command("↩ MY TEXT", vm.UndoRewriteCommand, tooltip: "Put back the message as you wrote it before it was rewritten (what you typed since is lost)");
        var wait = new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(120, 5), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var notes = Ui.Label(vm, nameof(vm.RewriteNotes), Eve.Eve.Muted, 11, wrap: true);
        notes.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foreach (var button in new[] { moldu, stop, mine })
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        // What was changed: under the buttons, on the whole width, a few lines high (the rest by its scroll bar).
        var changes = Ui.Scroll(notes);
        changes.CustomMinimumSize = new Vector2(0, 66);
        changes.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        changes.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var rewrite = Ui.Column(4, Ui.Row(8, moldu, stop, mine, wait), changes);
        Bind.When(rewrite, vm, () =>
        {
            moldu.Visible = !vm.IsRewriting;
            stop.Visible = wait.Visible = vm.IsRewriting;
            mine.Visible = vm.CanUndoRewrite && !vm.IsRewriting;
            changes.Visible = vm.RewriteNotes.Length > 0 && !vm.IsRewriting;
            text.Editable = !vm.IsRewriting;
        }, nameof(vm.IsRewriting), nameof(vm.CanUndoRewrite), nameof(vm.RewriteNotes));

        var actions = Ui.Row(6, insert, Ui.Spring(),
            Ui.Command("DISCARD", vm.DiscardCommand, tooltip: "Leave this message"),
            Ui.Command("SAVE DRAFT", vm.SaveDraftCommand, tooltip: "Keep it in Drafts"),
            Ui.Command("SEND ➤", vm.SendCommand, primary: true, tooltip: "Send to the users named in To (Ctrl+Enter / ⌘Enter)"));

        var form = Ui.Column(8,
            Ui.Field("To", Ui.Column(2, Ui.Row(6, to, search), named)),
            Ui.Field("Subject", subject),
            Ui.FillHeight(Ui.Field("Message", text)),
            rewrite,
            pictures,
            Ui.Row(10, encrypt, passwords),
            actions);
        Callable.From(() =>
        {
            // A new message starts with its receivers, an answer with its text.
            var first = vm.ComposeTo.Trim().Length == 0 ? to : (Control)text;
            if (GodotObject.IsInstanceValid(first) && first.IsInsideTree())
                first.GrabFocus();
        }).CallDeferred();
        return Ui.Margin(form, 8, 4, 8, 8);
    }

    /// <summary>
    /// The search of the users, for the receivers of the message being written: a field (login, name, level, e-mail,
    /// hashtags, profile; each word typed is looked for), the users found, each with a box to tick (those already in To
    /// are ticked), ALL and NONE for those shown. USE writes the logins ticked in To; CANCEL leaves To as it is.
    /// </summary>
    private static void PickReceivers(MessageWindowViewModel vm, Shell shell, LineEdit to)
    {
        var chosen = new List<string>(vm.ReceiversOf(vm.ComposeTo).Logins);
        bool IsChosen(string login) => chosen.Contains(login, StringComparer.OrdinalIgnoreCase);
        void Set(string login, bool on)
        {
            chosen.RemoveAll(l => l.Equals(login, StringComparison.OrdinalIgnoreCase));
            if (on)
                chosen.Add(login);
        }

        var dialog = new ConfirmationDialog { Title = "RECEIVERS · SEARCH THE USERS", OkButtonText = "USE", MinSize = new Vector2I(660, 480) };
        var term = new LineEdit { PlaceholderText = "Search: a login, a name, a level, an e-mail, a #hashtag, a word of the profile…", ClearButtonEnabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var count = Eve.Eve.Text_(string.Empty, Eve.Eve.Muted, 11);
        count.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        IReadOnlyList<MailUser> shown = [];

        void Tell() => count.Text = $"{chosen.Count} receiver{(chosen.Count == 1 ? string.Empty : "s")} ticked · {shown.Count} of {vm.Users.Count} user{(vm.Users.Count == 1 ? string.Empty : "s")} shown";
        void Build()
        {
            foreach (var child in rows.GetChildren())
            {
                rows.RemoveChild(child);
                child.QueueFree();
            }
            shown = vm.SearchUsers(term.Text);
            foreach (var user in shown)
            {
                var tick = new CheckBox { Text = user.Name + (user.IsMe ? " (me)" : string.Empty), ButtonPressed = IsChosen(user.Login), CustomMinimumSize = new Vector2(220, 0), ClipText = true, TooltipText = user.Profile };
                tick.Toggled += on =>
                {
                    Set(user.Login, on);
                    Tell();
                };
                var login = Eve.Eve.Text_(user.Login, Eve.Eve.Text, 12);
                login.AddThemeFontOverride("font", Eve.Eve.Mono);
                login.CustomMinimumSize = new Vector2(120, 0);
                login.ClipText = true;
                var what = Eve.Eve.Text_(string.Join(" · ", new[] { user.Role, user.Email, user.Hashtags }.Where(part => part.Length > 0)), Eve.Eve.Muted, 11);
                what.ClipText = true;
                what.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                rows.AddChild(Ui.Row(10, tick, login, what));
            }
            if (shown.Count == 0)
                rows.AddChild(Eve.Eve.Text_("No user for this search", Eve.Eve.Muted, 12));
            Tell();
        }

        var all = Ui.Action("ALL", () =>
        {
            foreach (var user in shown)
                Set(user.Login, true);
            Build();
        }, tooltip: "Tick every user shown");
        var none = Ui.Action("NONE", () =>
        {
            foreach (var user in shown)
                Set(user.Login, false);
            Build();
        }, tooltip: "Untick every user shown");
        term.TextChanged += _ => Build();
        // Enter: the only user found is ticked, and the next search can be typed.
        term.TextSubmitted += _ =>
        {
            if (shown.Count != 1)
                return;
            Set(shown[0].Login, true);
            term.Clear();
            Build();
        };
        var list = Ui.Scroll(rows);
        list.CustomMinimumSize = new Vector2(620, 300);
        dialog.AddChild(Ui.Column(8, term, Ui.Row(8, count, all, none), list));

        void Close(bool use)
        {
            if (use)
                vm.SetReceivers(chosen);
            dialog.QueueFree();
            if (GodotObject.IsInstanceValid(to) && to.IsInsideTree())
            {
                to.GrabFocus();
                to.CaretColumn = to.Text.Length;
            }
        }
        dialog.Confirmed += () => Close(true);
        dialog.Canceled += () => Close(false);
        shell.Root.AddChild(dialog);
        Build();
        dialog.PopupCentered();
        term.GrabFocus();
    }

    /// <summary>A picture inserted in the message being written: a small view, its name and size, ✕ to take it out.</summary>
    private static Control Thumbnail(MessageWindowViewModel vm, MailPicture picture)
    {
        var view = new TextureRect
        {
            Texture = Texture(picture),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(72, 54),
            TooltipText = picture.Marker
        };
        var name = Eve.Eve.Text_(picture.Name, Eve.Eve.Text, 11);
        name.ClipText = true;
        name.CustomMinimumSize = new Vector2(120, 0);
        var remove = Ui.Action("✕", () => vm.RemovePicture(picture), tooltip: "Take this picture out of the message");
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Eve.Eve.Box(Eve.Eve.Sunken(0.15f), Eve.Eve.Edge with { A = 0.25f }, 5));
        panel.AddChild(Ui.Row(6, view, Ui.Column(2, name, Eve.Eve.Text_($"{picture.Size / 1024.0:0} KB", Eve.Eve.Muted, 10)), remove));
        return panel;
    }
}
