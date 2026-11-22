using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Emotions;

/// <summary>
/// /emotion: the "what if" of the emotions. At the top, what the emotion engine of the server found last (the
/// emotion that leads in the last 3 prompts of a conversation, and whether its text was applied). Under it, one box
/// per emotion (happy, sad, angry, fearful, disgust, surprised, neutral): the text added to the system prompt of the
/// answers while that emotion leads; empty, nothing is added. The one found last is marked. SAVE keeps each text
/// changed as a new version, used from the next prompt; their history is in /prompts (emotion.happy…).
/// </summary>
[ToolView(typeof(EmotionWindowViewModel), "What if emotion", "🎭", 1000, 900)]
public sealed class EmotionView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (EmotionWindowViewModel)viewModel;

        var last = Ui.Label(vm, nameof(vm.LastDetected), Eve.Eve.TextBright, 12, wrap: true);
        last.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var toolbar = Ui.Toolbar(Ui.Row(10, last,
            Ui.Command("⟳ RELOAD", vm.ReloadCommand, tooltip: "Read the texts again from the search engine, and what the emotion engine found last"),
            Ui.Command("SAVE", vm.SaveCommand, primary: true, tooltip: "Keep each text changed as a new version: the server uses it from the next prompt (nothing is erased; the history is in /prompts)")));

        var intro = Eve.Eve.Text_("Every 3 prompts of a conversation, the server finds the emotion that leads in these prompts. While an emotion leads, its text below is added to "
            + "the system prompt of the answers. Say how the assistant should then behave (for instance, for angry: \"The user is irritated: apologise briefly, go straight to the "
            + "solution, no humour.\"). An emotion left empty changes nothing.", Eve.Eve.Muted, 12, wrap: true);

        var boxes = new VBoxContainer();
        boxes.AddThemeConstantOverride("separation", 10);
        foreach (var emotion in vm.Emotions)
            boxes.AddChild(Box(emotion));

        var page = Ui.Column(8, toolbar, Ui.Margin(intro, 8, 0, 8, 0), Ui.FillHeight(Ui.Scroll(Ui.Margin(boxes, 8, 0, 14, 8))), Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));
        return page;
    }

    /// <summary>An emotion: its name, what it means, whether it leads now, its state, and its text.</summary>
    private static Control Box(EmotionPromptViewModel emotion)
    {
        var name = Eve.Eve.Text_($"{emotion.Icon}  {emotion.Name.ToUpperInvariant()}", Eve.Eve.TextBright, 15);
        var hint = Eve.Eve.Text_(emotion.Hint, Eve.Eve.Muted, 11);
        var leading = Bind.Visible(Ui.Badge("FOUND LAST", Eve.Eve.Cyan), emotion, nameof(emotion.IsLeading));
        var state = Ui.Label(emotion, nameof(emotion.State), Eve.Eve.Muted, 11);
        state.HorizontalAlignment = HorizontalAlignment.Right;
        state.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Bind.When(state, emotion, () => state.AddThemeColorOverride("font_color", emotion.IsDirty ? Eve.Eve.Amber : Eve.Eve.Muted), nameof(emotion.IsDirty));
        var text = Ui.Lines(emotion, nameof(emotion.Text), 74, $"What is added to the system prompt while the user is {emotion.Name} (empty: nothing)");
        text.WrapMode = TextEdit.LineWrappingMode.Boundary;
        return Ui.Column(4, Ui.Row(10, name, hint, leading, state), text);
    }
}
