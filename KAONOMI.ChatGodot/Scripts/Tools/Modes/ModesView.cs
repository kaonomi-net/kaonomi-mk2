using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Modes;

/// <summary>
/// /models: one line per use (basic discussions, advanced questions, sound, image, video): the model typed or picked
/// in the list of the models installed (▾), and what the gateway does for it now. SAVE writes config.yml; ALIGN THE
/// GATEWAY makes the gateway route the same way; ⟳ reads the gateway again.
/// </summary>
[ToolView(typeof(ModesWindowViewModel), "Modes · default models", "🧭", 980, 620)]
public sealed class ModesView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (ModesWindowViewModel)viewModel;
        var toolbar = Ui.Toolbar(Ui.Row(10,
            Ui.Command("SAVE", vm.SaveCommand, primary: true, tooltip: "Write the models in config.yml (modes): the chat uses them at once"),
            Ui.Command("⇄ ALIGN THE GATEWAY", vm.AlignCommand, tooltip: "Make the LLM gateway route as here: default model, escalation (👎), rules of reasoning, pictures, sound and video"),
            Ui.Command("⟳", vm.ReloadCommand, tooltip: "Read the models installed and the gateway again"),
            Ui.Spring()));

        var grid = Ui.Grid(4, 14, 14);
        foreach (var mode in vm.Modes)
        {
            var title = Eve.Eve.Text_($"{mode.Icon}  {mode.Title}", Eve.Eve.TextBright, 14);
            var description = Eve.Eve.Text_(mode.Description, Eve.Eve.Muted, 11, wrap: true);
            description.CustomMinimumSize = new Vector2(260, 0);
            var line = Ui.Line(mode, nameof(mode.Model), "model");
            line.CustomMinimumSize = new Vector2(230, 0);
            line.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            var pick = new MenuButton { Text = "▾", TooltipText = "The models installed in the server behind the gateway" };
            pick.AboutToPopup += () =>
            {
                var popup = pick.GetPopup();
                popup.Clear();
                foreach (var model in vm.Installed)
                    popup.AddItem(model);
            };
            pick.GetPopup().IndexPressed += index => mode.Model = pick.GetPopup().GetItemText((int)index);
            var state = Ui.Label(mode, nameof(mode.GatewayState), Eve.Eve.Muted, 11);
            Bind.When(state, mode, () => state.AddThemeColorOverride("font_color",
                !mode.Installed ? Eve.Eve.Amber : mode.GatewayState.EndsWith('✓') ? Eve.Eve.Cyan : Eve.Eve.Muted), nameof(mode.GatewayState));
            grid.AddChild(Ui.Column(2, title, description));
            grid.AddChild(line);
            grid.AddChild(pick);
            grid.AddChild(state);
        }

        var intro = Eve.Eve.Text_("The default model of each use. The chat takes the one of the discussions and the image one for its tools; the gateway routes "
            + "a request to the model of its use once aligned (a calculation or code to the advanced model, a picture to the image model…).", Eve.Eve.Muted, 12, wrap: true);
        return Ui.Column(8, toolbar, Ui.Margin(intro, 10, 0, 10, 0), Ui.FillHeight(Ui.Scroll(Ui.Margin(grid, 10, 4, 10, 10))),
            Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError), nameof(vm.IsBusy)));
    }
}
