using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Vision;

/// <summary>
/// /vision:move: the motion detection of KAONOMI.VisionOpenCV (Python and OpenCV) in a window of the desktop. At the
/// top, the view (the mosaic of the four views: the camera with the moving zones, the foreground mask, the contours
/// and the trail of the gravity centre, the moving pixels alone; or one of them), ⏸ PAUSE, ⟲ BACKGROUND (learnt
/// again), 📷 PICTURE (saved in ~/Pictures/kaonomi/vision), 📁 FOLDER, and START when the detection stopped. Under it,
/// the sliders: the smallest zone kept and the threshold of the background subtraction, and TRAIL. In the middle, the
/// frames as the script sends them, kept in their proportions; under them, what was found: MOTION or still, the
/// zones, the part of the frame that moves, the gravity centre, the frames per second. The script starts once the
/// window is shown and stops with it (the camera is released).
/// </summary>
[ToolView(typeof(VisionMoveWindowViewModel), "Vision · motion", "👁", 1100, 900)]
public sealed partial class VisionMoveView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (VisionMoveWindowViewModel)viewModel;

        var views = new OptionButton { TooltipText = "The mosaic of the four views, or one of them alone", CustomMinimumSize = new Vector2(210, 0) };
        foreach (var name in VisionMoveWindowViewModel.Views)
            views.AddItem(name);
        views.Selected = vm.View;
        views.ItemSelected += index => vm.View = (int)index;
        Bind.When(views, vm, () => views.Selected = vm.View, nameof(vm.View));

        var pause = Ui.Command("⏸ PAUSE", vm.PauseCommand, tooltip: "Freeze the picture, or go on");
        pause.CustomMinimumSize = new Vector2(110, 0);
        Bind.When(pause, vm, () => pause.Text = vm.PauseLabel, nameof(vm.PauseLabel));
        var start = Bind.Visible(Ui.Command("▶ START", vm.StartCommand, primary: true, tooltip: "Run the motion detection again"), vm, nameof(vm.IsRunning), invert: true);
        var toolbar = Ui.Toolbar(Ui.Row(10,
            VisionScreen.CameraPicker(vm),
            Ui.Field("View", views),
            pause,
            Ui.Command("⟲ BACKGROUND", vm.ResetBackgroundCommand, tooltip: "Forget the background and learn it again: the camera moved, or the light changed"),
            Ui.Command("📷 PICTURE", vm.SnapshotCommand, tooltip: "Save the picture shown in ~/Pictures/kaonomi/vision"),
            Ui.Command("📁 FOLDER", vm.OpenFolderCommand, tooltip: "Open the folder of the pictures saved"),
            start,
            Ui.Spring()));

        var settings = Ui.Row(16,
            Slider(vm, "Min area", 50, 20000, 50, nameof(vm.MinArea), () => vm.MinArea, " px", "The smallest moving zone kept, in pixels: higher, the small moves are ignored"),
            Slider(vm, "Threshold", 1, 100, 1, nameof(vm.Threshold), () => vm.Threshold, "", "The threshold of the background subtraction: the lower, the more sensitive (and the more noise)"),
            Ui.Check("TRAIL", vm, nameof(vm.Trail), "Draw the path of the gravity centre of what moves"));

        var found = VisionScreen.FoundLabel();
        var screen = new VisionScreen(vm, found);

        var page = Ui.Column(6, toolbar, Ui.Margin(settings, 8, 0, 8, 0), Ui.FillHeight(screen), Ui.Margin(found, 8, 0, 8, 0), Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));
        // The camera opens once the window is shown, and is released with it.
        page.Ready += () => _ = vm.Start();
        Bind.OnGone(page, vm.Dispose);
        return page;
    }

    /// <summary>A setting as a line: its name, a slider, its value.</summary>
    private static Control Slider(VisionMoveWindowViewModel vm, string caption, double min, double max, double step, string property, System.Func<int> get, string unit, string tooltip)
    {
        var name = Eve.Eve.Text_(caption, Eve.Eve.Muted, 11);
        var slider = Bind.Range(new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(160, 16)
        }, vm, property);
        var value = Eve.Eve.Text_($"{get()}{unit}", Eve.Eve.TextBright, 11);
        value.AddThemeFontOverride("font", Eve.Eve.Mono);
        value.CustomMinimumSize = new Vector2(70, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Bind.When(value, vm, () => value.Text = $"{get()}{unit}", property);
        var row = Ui.Row(8, name, slider, value);
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return row;
    }
}
