using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.PromptTools;

/// <summary>
/// /prompts: the prompts the tools of the chat send to the models. On the left, a search and the list of the prompts
/// (its title, its key, the version in use, "changed" when it is no longer the default). On the right, the one
/// selected: what it is for and its placeholders; its text to edit; SAVE (a new version, used at once), a word on
/// what changed, DEFAULT (the default of config.yml in the editor), ✨ ANALYSE (the model of the chat reviews it),
/// SAVE AS JSON and LOAD JSON; under it, on the left its versions, the newest first (a click puts one in the editor:
/// saving it makes it the version in use again), on the right the analysis of the model. Nothing is erased: every
/// change is one more version in the search engine.
/// </summary>
[ToolView(typeof(PromptsWindowViewModel), "Prompts", "📝", 1300, 860)]
public sealed class PromptsView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (PromptsWindowViewModel)viewModel;
        vm.SaveFileAsync = (title, name) => Dialogs.SaveFile(title, name, ["*.json ; JSON"]);
        vm.OpenFileAsync = title => Dialogs.OpenFile(title, ["*.json ; JSON"]);

        // The list follows what is typed: no button to search.
        var search = Ui.Line(vm, nameof(vm.Term), "Search the prompts: a tool, a word of the text…");
        search.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var toolbar = Ui.Toolbar(Ui.Row(6, search, Ui.Command("⟳ RELOAD", vm.ReloadCommand, tooltip: "Read the versions again from the search engine")));

        // The prompts.
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        var empty = Ui.Placeholder("No prompt matches");
        Bind.Items(list, vm.Items, item => PromptRow(vm, item), empty);
        var left = Ui.Column(0, Ui.FillHeight(Ui.Scroll(list)), empty);
        left.CustomMinimumSize = new Vector2(330, 0);

        // The prompt selected.
        var heading = Ui.Label(vm, nameof(vm.Heading), Eve.Eve.TextBright, 17);
        var about = Ui.Label(vm, nameof(vm.About), Eve.Eve.Muted, 12, wrap: true);
        var text = Ui.Lines(vm, nameof(vm.Text), 260, "The prompt", mono: true);
        text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        text.WrapMode = TextEdit.LineWrappingMode.Boundary;
        Bind.Enabled(text, vm, nameof(vm.HasSelection));
        var edited = Ui.Label(vm, nameof(vm.EditedLabel), Eve.Eve.Muted, 11);
        Bind.When(edited, vm, () => edited.AddThemeColorOverride("font_color", vm.IsDirty ? Eve.Eve.Amber : Eve.Eve.Muted), nameof(vm.IsDirty));

        var comment = Ui.Line(vm, nameof(vm.Comment), "What changed in this version (optional)");
        comment.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var analyse = Ui.Command("✨ ANALYSE", vm.AnalyseCommand, tooltip: "Ask the model of the chat to review the text in the editor: what it asks, its weaknesses, a better wording");
        Bind.When(analyse, vm, () => analyse.Text = vm.IsAnalysing ? "… ANALYSING" : "✨ ANALYSE", nameof(vm.IsAnalysing));
        var actions = Ui.Row(8,
            Ui.Command("SAVE", vm.SaveCommand, primary: true, tooltip: "Keep the text as a new version: every tool uses it at once; the former versions stay"),
            comment,
            Ui.Command("DEFAULT", vm.UseDefaultCommand, tooltip: "Put the default of config.yml in the editor (SAVE makes it the version in use)"),
            analyse,
            Ui.Command("SAVE AS JSON", vm.ExportCommand, tooltip: "Write the prompt, the text in the editor and all its versions in a JSON file"),
            Ui.Command("LOAD JSON", vm.ImportCommand, tooltip: "Take the text of a JSON file (a file saved here, or any JSON with a \"text\") as a new version of this prompt"));

        // Its versions, and the analysis of the model.
        var versions = new VBoxContainer();
        versions.AddThemeConstantOverride("separation", 2);
        var noVersion = Ui.Placeholder("No version yet: the default is in use");
        Bind.Items(versions, vm.Versions, version => VersionRow(vm, version), noVersion);
        var history = Ui.Column(4, Eve.Eve.Caption("Versions (the newest first · a click puts one in the editor)"), Ui.FillHeight(Ui.Scroll(versions)), noVersion);
        history.CustomMinimumSize = new Vector2(360, 0);
        var analysis = Ui.Markdown(vm, nameof(vm.Analysis));
        analysis.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var review = Ui.Column(4, Eve.Eve.Caption("Analysis of the model"), Ui.FillHeight(Ui.Scroll(analysis)));
        var bottom = Ui.SplitH(history, review, 420);
        bottom.CustomMinimumSize = new Vector2(0, 240);

        var right = Ui.Column(6, heading, about, Ui.FillHeight(text), edited, actions, Ui.FillHeight(bottom));
        var page = Ui.Column(6, toolbar, Ui.FillHeight(Ui.SplitH(left, Ui.Margin(right, 8, 0, 8, 0), 350)), Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));
        return page;
    }

    /// <summary>A prompt of the list: its title, then its key, its version and whether it was changed; a click selects it.</summary>
    private static Control PromptRow(PromptsWindowViewModel vm, PromptItemViewModel item)
    {
        var button = new Button { ToggleMode = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44), Alignment = HorizontalAlignment.Left, TooltipText = item.Definition.Description };
        button.AddThemeStyleboxOverride("normal", Eve.Eve.Box(Eve.Eve.Sunken(0.15f), Eve.Eve.Edge with { A = 0.15f }, 6));
        var name = Eve.Eve.Text_(item.Title, Eve.Eve.TextBright, 13);
        name.ClipText = true;
        var details = Ui.Label(item, nameof(item.Details), Eve.Eve.Muted, 10);
        Bind.When(details, item, () => details.AddThemeColorOverride("font_color", item.IsChanged ? Eve.Eve.Amber : Eve.Eve.Muted), nameof(item.IsChanged));
        var content = Ui.Column(1, name, details);
        content.MouseFilter = name.MouseFilter = details.MouseFilter = Control.MouseFilterEnum.Ignore;
        var margin = Ui.Margin(content, 8, 4, 8, 4);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(margin);
        button.Pressed += () => vm.Selected = item;
        Bind.When(button, vm, () => button.SetPressedNoSignal(ReferenceEquals(vm.Selected, item)), nameof(vm.Selected));
        return button;
    }

    /// <summary>A version: its number, when and by whom, where it comes from, what changed; a click puts its text in the editor.</summary>
    private static Control VersionRow(PromptsWindowViewModel vm, PromptVersion version)
    {
        var what = version.Comment.Length > 0 ? version.Comment : version.Source;
        var row = Ui.Command($"v{version.Version}  ·  {version.Date.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {version.Source}", vm.ShowVersionCommand,
            tooltip: $"Version {version.Version}, by {version.UserName} on {version.MachineName}\n{what}\n\n{(version.Text.Length > 600 ? version.Text[..600] + "…" : version.Text)}", parameter: () => version);
        row.Alignment = HorizontalAlignment.Left;
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var comment = Eve.Eve.Text_(what, Eve.Eve.Muted, 10);
        comment.ClipText = true;
        return Ui.Column(0, row, Ui.Margin(comment, 10, 0, 4, 2));
    }
}
