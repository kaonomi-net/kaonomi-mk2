using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Data;
using KAONOMI.ChatGodot.Tools.Work;

namespace KAONOMI.ChatGodot.Tools.Daily;

/// <summary>
/// The tools of the right click of /work, in /message, asked to the model of the chat as there (the waiting popup,
/// then a window with the result). On the text of a message being written, added to the menu of the text areas:
/// ✏️ Correct the spelling and ✨ Rewrite for readability (the selection; the result can be changed, then replaces the
/// selection or goes below it), 🧐 Evaluate (the selection, or the whole text). On the text of a message that is read,
/// a menu of its own: Copy, Select all, ✎ Annotate (a note on the message), 🧐 Evaluate and 🌐 Translate into a language
/// (the selection, or the whole text; nothing is replaced: the message is not changed).
/// </summary>
internal static class MessageTools
{
    /// <summary>The languages of 🌐 Translate on a message that is read: those of /work, and French.</summary>
    private static readonly (string Language, string Label)[] Languages =
        [("French", "Français 🇫🇷"), .. TapeEditor.TranslationLanguages];

    /// <summary>
    /// The tools of /work for the text of the message being written (<paramref name="text"/>: its selection, or the
    /// whole text when <paramref name="isSelection"/> is false), first in its right-click menu.
    /// </summary>
    public static IEnumerable<Overlay.MenuItem?> ForWriting(MessageWindowViewModel vm, Shell shell, Control host, string text, bool isSelection,
        Func<string, bool> replace, Func<string, bool> insert)
    {
        var selected = isSelection && text.Trim().Length > 0;
        var can = vm.CanUseWorkTools;
        yield return new Overlay.MenuItem(selected ? "✏️ Correct the spelling" : "✏️ Correct the spelling (select text first)", selected && can,
            () => _ = RewriteAsync(vm, shell, host, text, TapeEditor.RewriteSpelling, replace, insert));
        yield return new Overlay.MenuItem(selected ? "✨ Rewrite for readability" : "✨ Rewrite for readability (select text first)", selected && can,
            () => _ = RewriteAsync(vm, shell, host, text, TapeEditor.RewriteReadability, replace, insert));
        yield return new Overlay.MenuItem(selected ? "🧐 Evaluate the selection" : "🧐 Evaluate the text", text.Trim().Length > 0 && can,
            () => _ = EvaluateAsync(vm, shell, host, text));
        yield return null;
    }

    /// <summary>
    /// The right-click menu of the text of a message that is read (<paramref name="label"/>), at <paramref name="at"/>:
    /// Copy, Select all, ✎ Annotate (<paramref name="annotate"/>), 🧐 Evaluate, 🌐 Translate into…
    /// </summary>
    public static void ShowReadingMenu(MessageWindowViewModel vm, Shell shell, RichTextLabel label, Vector2 at, Action annotate, bool canAnnotate)
    {
        if (label.GetViewport() is not { } viewport)
            return;
        var selection = label.GetSelectedText();
        var selected = selection.Trim().Length > 0;
        var text = selected ? selection : label.GetParsedText();
        var hasText = text.Trim().Length > 0;
        var can = vm.CanUseWorkTools;
        var overlay = new Overlay();
        viewport.AddChild(overlay);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shortcut = OS.GetName() == "macOS" ? "⌘" : "Ctrl+";
        var items = new List<Overlay.MenuItem?>
        {
            new(selected ? "🧐 Evaluate the selection" : "🧐 Evaluate the text", hasText && can, () => _ = EvaluateAsync(vm, shell, label, text)),
            new(selected ? "🌐 Translate the selection into" : "🌐 Translate the text into", false, null)
        };
        foreach (var (language, name) in Languages)
            items.Add(new Overlay.MenuItem(name, hasText && can, () => _ = TranslateAsync(vm, shell, label, text, language), Indent: 1));
        items.Add(null);
        items.Add(new Overlay.MenuItem(selected ? "✎ Annotate the selection…" : "✎ Annotate (select text or click a picture first)", canAnnotate || selected, annotate));
        items.Add(new Overlay.MenuItem("Copy", selected, () =>
        {
            DisplayServer.ClipboardSet(selection);
            vm.Status = "Copied";
        }, shortcut + "C"));
        items.Add(new Overlay.MenuItem("Select all", hasText, label.SelectAll, shortcut + "A"));
        overlay.Menu(items, at, closed: () => overlay.QueueFree());
    }

    private static string Model(MessageWindowViewModel vm) => vm.Writing?.CurrentModelName is { Length: > 0 } name ? name : "LLM";

    /// <summary>🧐 Evaluate: the model criticizes the text and suggests improvements; the result opens in a window of its own.</summary>
    private static async Task EvaluateAsync(MessageWindowViewModel vm, Shell shell, Control host, string text)
    {
        var model = Model(vm);
        vm.Status = $"🧐 Evaluating with {model}…";
        var (markdown, error, cancelled) = await TextMenu.BusyAsync(host, "Evaluation", $"{model} evaluates the text…", ct => vm.EvaluateTextAsync(text, ct));
        Told(vm, "Evaluation", error, cancelled);
        if (!cancelled)
            // A table the model writes is hard to read in the window: shown as a list.
            WorkWindows.Evaluation(shell, text, model, markdown is null ? null : MessageWindowViewModel.TablesAsLists(markdown), error,
                subtitle: $"Criticism of the message (clarity, tone, structure, what is missing), then ideas to improve it, without rewriting it · {model}");
    }

    /// <summary>🌐 Translate on a message that is read: the translation opens in a window of its own (Copy); the message is not changed.</summary>
    private static async Task TranslateAsync(MessageWindowViewModel vm, Shell shell, Control host, string text, string language)
    {
        var model = Model(vm);
        vm.Status = $"🌐 Translating into {language} with {model}…";
        var (translation, error, cancelled) = await TextMenu.BusyAsync(host, "Translation", $"{model} translates the text into {language}…",
            ct => vm.Writing!.TranslateAsync(text, language, ct));
        Told(vm, "Translation", error, cancelled);
        if (!cancelled)
            // Shown as it is written: each line of the translation is a line of the window.
            WorkWindows.Evaluation(shell, text, model, translation?.ReplaceLineEndings("\n").Replace("\n", "  \n"), error,
                heading: $"🌐 Translation into {language}", subtitle: $"The message is not changed · {model}");
    }

    /// <summary>
    /// ✏️ Correct the spelling (the language identified first) / ✨ Rewrite for readability; the result opens in a window
    /// where it can be changed, then replace the selection or go below it.
    /// </summary>
    private static async Task RewriteAsync(MessageWindowViewModel vm, Shell shell, Control host, string text, string mode,
        Func<string, bool> replace, Func<string, bool> insert)
    {
        var spelling = mode == TapeEditor.RewriteSpelling;
        var model = Model(vm);
        string? language = null;
        vm.Status = spelling ? $"✏️ Correcting the spelling with {model}…" : $"✨ Rewriting for readability with {model}…";
        var (result, error, cancelled) = await TextMenu.BusyAsync(host, spelling ? "Spelling correction" : "Rewriting",
            spelling ? $"{model} identifies the language of the text, then corrects its spelling…" : $"{model} rewrites the text for readability…", async ct =>
            {
                if (spelling)
                    language = await vm.Writing!.DetectLanguageAsync(text, ct);
                return await vm.Writing!.RewriteAsync(text, mode, ct, language);
            });
        Told(vm, spelling ? "Spelling correction" : "Rewriting", error, cancelled);
        if (cancelled)
            return;
        WorkWindows.Translation(shell, text, string.Empty, model, result, error, replace, insert,
            spelling ? $"✏️ Spelling correction{(language is null ? string.Empty : " · " + language)}" : $"✨ Rewriting · « {WritingAssistant.ReadabilityPrompt} »",
            spelling ? "Spelling correction" : "Rewriting");
    }

    private static void Told(MessageWindowViewModel vm, string what, string? error, bool cancelled)
    {
        vm.IsError = error is not null;
        vm.Status = cancelled ? what + " cancelled" : error is not null ? $"{what} failed: {error}" : string.Empty;
        if (error is not null)
            ActivityLog.Error("Message", $"{what} of a text of /message failed: {error}");
    }
}
