using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Vision;

/// <summary>
/// The object detection of KAONOMI.VisionOpenCV (Python, YOLOv8 and OpenCV) alone, in a window of the desktop that no
/// command opens any more (it was /vision:yolo; /vision now shows the objects with the faces).
/// At the top, the model (nano or medium), ⏸ PAUSE, 📷 PICTURE (saved in ~/Pictures/kaonomi/vision), 📁 FOLDER, and
/// START when the detection stopped. Under it, what is detected and drawn: the smallest confidence kept, PERSONS
/// ONLY, CENTRES (of each person, with its coordinates), DIRECTIONS (its trail and the arrow of its move), OVERLAP
/// (the alert when two persons overlap), the class of objects that raises an alert, and JSON LOG (the detections as
/// JSON lines in ~/kaonomi/vision/yolo; 📁 opens it), and RECORD (once a minute, everything detected goes to the
/// search engine). In the middle, the frames as the script sends them; under them,
/// what is seen (the objects by class), the alerts, the model and where it runs, the frames per second. The script
/// starts once the window is shown and stops with it (the camera is released).
/// </summary>
[ToolView(typeof(VisionYoloWindowViewModel), "Vision · YOLO", "🎯", 1100, 900)]
public sealed partial class VisionYoloView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (VisionYoloWindowViewModel)viewModel;

        var models = new OptionButton { TooltipText = "Nano: fast. Medium, large, extra large: each more accurate and slower than the one before (loaded when chosen)", CustomMinimumSize = new Vector2(250, 0) };
        foreach (var model in VisionYoloWindowViewModel.Models)
            models.AddItem(model);
        models.Selected = vm.Model;
        models.ItemSelected += index => vm.Model = (int)index;
        Bind.When(models, vm, () => models.Selected = vm.Model, nameof(vm.Model));

        var pause = Ui.Command("⏸ PAUSE", vm.PauseCommand, tooltip: "Freeze the picture, or go on");
        pause.CustomMinimumSize = new Vector2(110, 0);
        Bind.When(pause, vm, () => pause.Text = vm.PauseLabel, nameof(vm.PauseLabel));
        var start = Bind.Visible(Ui.Command("▶ START", vm.StartCommand, primary: true, tooltip: "Run the detection again"), vm, nameof(vm.IsRunning), invert: true);
        var toolbar = Ui.Toolbar(Ui.Row(10,
            VisionScreen.CameraPicker(vm),
            Ui.Field("Model", models),
            pause,
            Ui.Command("📷 PICTURE", vm.SnapshotCommand, tooltip: "Save the picture shown in ~/Pictures/kaonomi/vision"),
            Ui.Command("📁 FOLDER", vm.OpenFolderCommand, tooltip: "Open the folder of the pictures saved"),
            start,
            Ui.Spring()));

        var name = Eve.Eve.Text_("Confidence", Eve.Eve.Muted, 11);
        var slider = Bind.Range(new HSlider
        {
            MinValue = 5, MaxValue = 95, Step = 5, TooltipText = "The smallest confidence of a detection kept: lower, more objects (and more mistakes)",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(160, 16)
        }, vm, nameof(vm.Confidence));
        var value = Eve.Eve.Text_($"{vm.Confidence} %", Eve.Eve.TextBright, 11);
        value.AddThemeFontOverride("font", Eve.Eve.Mono);
        value.CustomMinimumSize = new Vector2(44, 0);
        Bind.When(value, vm, () => value.Text = $"{vm.Confidence} %", nameof(vm.Confidence));

        var alert = Ui.Line(vm, nameof(vm.AlertClass), "a class: wine glass, cell phone, cup…");
        alert.CustomMinimumSize = new Vector2(170, 0);
        alert.TooltipText = "An alert is written across the picture when an object of this class is seen (a class of COCO: wine glass, cell phone, cup, bottle, dog…); empty: no alert";

        var settings = Ui.Row(14,
            Ui.Row(8, name, slider, value),
            Ui.Check("PERSONS ONLY", vm, nameof(vm.PersonsOnly), "Detect the persons only, not the other objects"),
            Ui.Check("CENTRES", vm, nameof(vm.Centers), "The centre of the box of each person, with its coordinates"),
            Ui.Check("DIRECTIONS", vm, nameof(vm.Directions), "Each person followed: the trail of its last positions and the arrow of its average direction"),
            Ui.Check("OVERLAP", vm, nameof(vm.Overlap), "An alert when the boxes of two persons overlap"),
            Ui.Row(6, Eve.Eve.Text_("Alert on", Eve.Eve.Muted, 11), alert),
            Ui.Check("JSON LOG", vm, nameof(vm.LogJson), "Write the detections as JSON lines (once a second) in ~/kaonomi/vision/yolo"),
            Ui.Command("📁", vm.OpenLogFolderCommand, tooltip: "Open the folder of the JSON lines"),
            Ui.Check("RECORD", vm, nameof(vm.Record), "Record in the search engine, once a minute, everything detected during that minute: each kind of object with the most of them at once, how often it was seen and how sure, the persons, the alerts"),
            Ui.Label(vm, nameof(vm.RecordLabel), Eve.Eve.Muted, 11));

        var found = VisionScreen.FoundLabel();
        var screen = new VisionScreen(vm, found);

        var page = Ui.Column(6, toolbar, Ui.Margin(Ui.Scroll(settings), 8, 0, 8, 0), Ui.FillHeight(screen), Ui.Margin(found, 8, 0, 8, 0), Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));
        // The model is loaded and the camera opened once the window is shown; both are released with it.
        page.Ready += () => _ = vm.Start();
        Bind.OnGone(page, vm.Dispose);
        return page;
    }
}
