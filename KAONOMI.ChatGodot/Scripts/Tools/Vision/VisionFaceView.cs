using System;
using System.Linq;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Vision;

/// <summary>
/// /vision, one window: the faces (below) and the objects of YOLOv8 on the same frames of the
/// camera. The second line of settings is the one of the objects: OBJECTS (on or off), the model (nano to extra
/// large), the smallest confidence, PERSONS ONLY, CENTRES, DIRECTIONS, OVERLAP, the class that raises an alert, JSON
/// LOG (📁 its folder), RECORD OBJECTS (once a minute, everything detected goes to the search engine). The boxes of the
/// objects are drawn under the faces; the line under the picture tells both.
/// <para>
/// The faces: the face recognition and the facial expression recognition of KAONOMI.VisionOpenCV (Python and
/// OpenCV, open source models of the OpenCV Zoo, everything on this machine) in a window of the desktop. At the top,
/// the camera, ADD RECO (a face from a photo), FORGET ▾ (the names enrolled: the pictures of the one chosen are
/// deleted, asked first), 📁 (the folder of the faces), ⏸ PAUSE, 📷 PICTURE, 📁 FOLDER, and START when it stopped.
/// Under it: the smallest similarity to recognise a face, EXPRESSIONS, LANDMARKS, MOTION (the centre of each face, the
/// arrow of its movement, its yellow trail fading in 3 seconds), RECORD (what is seen of each face,
/// every second, goes to the search engine). In the middle, the frames: each face in its box, its name (green) or
/// "unknown" (yellow) over it, its expression under it. A click in the box of a face opens a popup to name it (that
/// face is kept under the name; again, from other angles, for a steadier recognition) or to forget the name it is
/// recognised as. Under the frames: the faces seen, the names enrolled (and their pictures), the frames per second.
/// The faces enrolled are kept in the search engine of the server too (said at the right of the settings).
/// The script starts once the window is shown and stops with it (the camera is released).
/// </para>
/// </summary>
[ToolView(typeof(VisionFaceWindowViewModel), "Vision · faces, objects & voice", "👁", 1400, 1120)]
public sealed partial class VisionFaceView : IToolView
{
    private static readonly string[] PhotoFilters = ["*.jpg, *.jpeg, *.png, *.webp, *.bmp ; Photos"];

    /// <summary>
    /// ADD RECO: a popup to enrol a face from a photo: the name, the photo (chosen on the disk, shown), ENROL. The
    /// largest face of the photo is kept under the name; the answer is in the status of the window.
    /// </summary>
    private static void AddReco(VisionFaceWindowViewModel vm)
    {
        string? path = null;
        var dialog = new ConfirmationDialog { Title = "ADD RECO · ENROL A FACE FROM A PHOTO", OkButtonText = "ENROL", MinSize = new Vector2I(560, 0) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        var name = new LineEdit { PlaceholderText = "Name of the person", CustomMinimumSize = new Vector2(520, 0) };
        var preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(520, 300), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        var file = Eve.Eve.Text_("No photo chosen", Eve.Eve.Muted, 12, wrap: true);
        file.CustomMinimumSize = new Vector2(520, 0);
        var ok = dialog.GetOkButton();
        void Refresh() => ok.Disabled = path is null || name.Text.Trim().Length == 0;
        var choose = Ui.Action("📂 CHOOSE A PHOTO…", async () =>
        {
            var chosen = await Dialogs.OpenFile("Photo of the face to enrol", PhotoFilters);
            if (chosen is null)
                return;
            var image = Image.LoadFromFile(chosen);
            if (image is null || image.IsEmpty())
            {
                file.Text = $"{System.IO.Path.GetFileName(chosen)} could not be read";
                path = null;
            }
            else
            {
                preview.Texture = ImageTexture.CreateFromImage(image);
                file.Text = $"{System.IO.Path.GetFileName(chosen)} · {image.GetWidth()}×{image.GetHeight()}: the largest face is kept";
                path = chosen;
            }
            Refresh();
        }, tooltip: "A photo where the face is seen from the front (jpg, png, webp, bmp)");
        name.TextChanged += _ => Refresh();
        column.AddChild(Ui.Field("Name", name));
        column.AddChild(choose);
        column.AddChild(preview);
        column.AddChild(file);
        dialog.AddChild(column);
        dialog.Confirmed += () =>
        {
            if (path is not null && !vm.EnrolPhoto(name.Text, path))
                _ = Dialogs.Message("Add reco", "The recognition is not running: START it, then add the photo again.");
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        Refresh();
        Shell.Current.Root.AddChild(dialog);
        dialog.ResetSize();
        dialog.PopupCentered();
        name.GrabFocus();
    }

    /// <summary>
    /// A face clicked in its box: a popup with its picture, the name it is recognised as (empty when unknown), ENROL
    /// (this face is kept under the name typed: one more picture of the name, or a new name) and, when it is
    /// recognised, FORGET (the pictures of its name are deleted, asked first). The script keeps the face clicked while
    /// the name is typed: the one enrolled is this one even if it moves.
    /// </summary>
    /// <para>
    /// An unknown face is enrolled with its voice, on that condition: the popup says the person will speak for
    /// <see cref="SpeakSeconds"/> seconds; OK shows a bar emptying from 5 to 0 with SPEAK, then the voice heard is
    /// enrolled under the name (as in /recorder) and the face with it. No voice heard: neither is enrolled.
    /// </para>
    private static void NameFace(VisionFaceWindowViewModel vm, VisionScreen screen, VisionFaceWindowViewModel.SeenFace face)
    {
        vm.Hold(face);
        var withVoice = !face.Known;
        var dialog = new ConfirmationDialog
        {
            Title = face.Known ? $"FACE · {face.Name.ToUpperInvariant()} · {face.Score * 100:0} %" : "FACE · UNKNOWN",
            OkButtonText = withVoice ? "OK" : "ENROL", MinSize = new Vector2I(520, 0)
        };

        // The face as it is seen now, cut from the frame shown (inside the line of its box).
        var picture = new TextureRect
        {
            CustomMinimumSize = new Vector2(128, 128), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        if (screen.Texture?.GetImage() is { } frame && !frame.IsEmpty())
        {
            var region = new Rect2I(face.X + 3, face.Y + 3, face.Width - 6, face.Height - 6).Intersection(new Rect2I(0, 0, frame.GetWidth(), frame.GetHeight()));
            if (region.HasArea())
                picture.Texture = ImageTexture.CreateFromImage(frame.GetRegion(region));
        }

        var name = new LineEdit { Text = face.Known ? face.Name : string.Empty, PlaceholderText = "Name of the person", SelectAllOnFocus = true };
        var help = Eve.Eve.Text_(face.Known
            ? $"Recognised as {face.Name}. ENROL adds this picture to {face.Name}: from other angles, the recognition is steadier. Another name enrols this face under that name."
            : $"Type the name of this person. To enrol this face, the person must speak for {SpeakSeconds} seconds: their voice is enrolled at the same time, under the same name. Click OK (Enter), then speak while the bar empties.", Eve.Eve.Muted, 12, wrap: true);
        help.CustomMinimumSize = new Vector2(340, 0);
        var column = Ui.Column(10, Ui.Field("Name", name), help);
        column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        dialog.AddChild(Ui.Row(14, picture, column));

        var ok = dialog.GetOkButton();
        void Refresh() => ok.Disabled = name.Text.Trim().Length == 0;
        name.TextChanged += _ => Refresh();
        dialog.RegisterTextEnter(name);
        if (face.Known)
        {
            dialog.AddButton($"FORGET {face.Name}", false, "forget");
            dialog.CustomAction += action =>
            {
                if (action != "forget")
                    return;
                dialog.Hide();
                dialog.QueueFree();
                _ = vm.ForgetAsync(face.Name);
            };
        }
        dialog.Confirmed += () =>
        {
            var typed = name.Text.Trim();
            dialog.QueueFree();
            if (typed.Length == 0)
                return;
            if (!vm.IsRunning)
                _ = Dialogs.Message("Enrol a face", "The recognition is not running: START it, then click the face again.");
            else if (!withVoice)
                vm.EnrolHeld(typed);
            else if (vm.Voice is not { } voice)
                _ = Dialogs.Message("Enrol a face", "The face is enrolled with the voice, and this window has no microphone.");
            else if (voice.CannotEnrolHeard() is { } why)
                _ = Dialogs.Message("Enrol a face", $"The face is enrolled with the voice, and the voice cannot be heard now: {why}.");
            else
                Speak(vm, voice, typed);
        };
        dialog.Canceled += dialog.QueueFree;
        Refresh();
        Shell.Current.Root.AddChild(dialog);
        dialog.ResetSize();
        dialog.PopupCentered();
        name.GrabFocus();
        // The name it is recognised as: Enter adds this picture to it, typing replaces it.
        name.SelectAll();
    }

    /// <summary>How long the person speaks for their voice to be enrolled with their face.</summary>
    private const int SpeakSeconds = 5;

    /// <summary>
    /// SPEAK: a bar emptying from <see cref="SpeakSeconds"/> seconds to 0 while the person speaks; then the voice heard
    /// is enrolled under <paramref name="name"/> and, when it is, the face clicked too. CANCEL enrols neither.
    /// </summary>
    private static void Speak(VisionFaceWindowViewModel vm, RecorderWindowViewModel voice, string name) =>
        Countdown($"ENROL · {name.ToUpperInvariant()}", $"{name}, speak until the bar is empty: your voice is enrolled with your face.", SpeakSeconds,
            "Enrolling the voice…", () => vm.Say($"{name} not enrolled: cancelled"), async () =>
            {
                var why = await voice.EnrolHeardAsync(name, SpeakSeconds, "/vision (spoken while the face was enrolled)");
                if (why is not null)
                {
                    vm.Say($"{name} not enrolled: {why}", true);
                    _ = Dialogs.Message("Enrol a face", $"Neither the face nor the voice of {name} is enrolled.\n\n{why}.\n\nClick the face again to try once more.");
                    return;
                }
                if (!vm.EnrolHeld(name))
                    _ = Dialogs.Message("Enrol a face", $"The voice of {name} is enrolled, but the recognition stopped meanwhile: START it, then click the face again.");
            });

    /// <summary>
    /// A popup with SPEAK and a bar emptying from <paramref name="seconds"/> to 0 (the time left written under it),
    /// and CANCEL. At 0, <paramref name="working"/> is said while <paramref name="done"/> runs, then it closes;
    /// CANCEL closes it and calls <paramref name="cancelled"/> instead.
    /// </summary>
    private static void Countdown(string title, string help, int seconds, string working, Action cancelled, Func<System.Threading.Tasks.Task> done)
    {
        var dialog = new AcceptDialog { Title = title, OkButtonText = "CANCEL", MinSize = new Vector2I(520, 0), Exclusive = true };
        dialog.GetOkButton().Visible = false;
        var cancel = dialog.AddCancelButton("CANCEL");
        var speak = Eve.Eve.Text_("SPEAK", Eve.Eve.Red, 44);
        speak.HorizontalAlignment = HorizontalAlignment.Center;
        var bar = new ProgressBar { MinValue = 0, MaxValue = seconds, Value = seconds, ShowPercentage = false, CustomMinimumSize = new Vector2(460, 22) };
        bar.AddThemeStyleboxOverride("fill", Eve.Eve.Box(Eve.Eve.Red, Eve.Eve.Red, 4));
        bar.AddThemeStyleboxOverride("background", Eve.Eve.Box(Eve.Eve.Sunken(0.4f), Eve.Eve.Edge with { A = 0.3f }, 4));
        var left = Eve.Eve.Text_($"{seconds:0.0} s", Eve.Eve.TextBright, 16);
        left.AddThemeFontOverride("font", Eve.Eve.Mono);
        left.HorizontalAlignment = HorizontalAlignment.Center;
        var text = Eve.Eve.Text_(help, Eve.Eve.Muted, 12, wrap: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.CustomMinimumSize = new Vector2(460, 0);
        dialog.AddChild(Ui.Margin(Ui.Column(10, speak, bar, left, text), 10, 10, 10, 6));

        var stopped = false;
        dialog.Canceled += () =>
        {
            stopped = true;
            dialog.QueueFree();
            cancelled();
        };
        var started = Time.GetTicksMsec();
        var tick = new Timer { WaitTime = 0.05, Autostart = true };
        dialog.AddChild(tick);
        tick.Timeout += async () =>
        {
            var remaining = Math.Max(0, seconds - (Time.GetTicksMsec() - started) / 1000.0);
            bar.Value = remaining;
            left.Text = $"{remaining:0.0} s";
            if (remaining > 0 || stopped)
                return;
            tick.Stop();
            speak.Text = "…";
            speak.AddThemeColorOverride("font_color", Eve.Eve.Muted);
            text.Text = working;
            cancel.Disabled = true;
            await done();
            if (GodotObject.IsInstanceValid(dialog))
                dialog.QueueFree();
        };
        Shell.Current.Root.AddChild(dialog);
        dialog.ResetSize();
        dialog.PopupCentered();
    }

    /// <summary>
    /// SET LOCATION: the name of the place is asked, then the user is asked to sit in the usual working position;
    /// OK measures for <see cref="VisionFaceWindowViewModel.MeasureSeconds"/> seconds (SPEAK and the bar) the box of
    /// the face and the level of the voice, kept under that name (on this machine and in the search engine) and chosen.
    /// </summary>
    private static async void SetLocation(VisionFaceWindowViewModel vm)
    {
        var name = (await Dialogs.Ask("Set location", "The name of this place (home office, desk at work…). Measured again, a place keeps its new values.", vm.Location))?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        // The popup of the name is closed before the next one opens (two exclusive popups at once are refused).
        await Shell.Current.Root.ToSignal(Shell.Current.Root.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!await Dialogs.Confirm($"Set location · {name}",
                $"Sit in front of the camera in your usual working position, as you are when you speak to the assistant.\n\n" +
                $"When you are ready, click OK, then speak for {VisionFaceWindowViewModel.MeasureSeconds} seconds as you usually do: the size of your face in the picture and the level of your voice are measured and kept for {name}."))
            return;
        if (!vm.IsRunning)
        {
            _ = Dialogs.Message("Set location", "The camera is not running: START it, then SET LOCATION again.");
            return;
        }
        if (vm.Voice is not { } voice || !voice.Recorder.IsListening)
        {
            _ = Dialogs.Message("Set location", $"The microphone is not open: the level of the voice cannot be measured. {vm.Voice?.Status}");
            return;
        }
        vm.BeginMeasure();
        Countdown($"SET LOCATION · {name.ToUpperInvariant()}", $"Stay in your working position and speak until the bar is empty.", VisionFaceWindowViewModel.MeasureSeconds,
            "Measuring…", () =>
            {
                vm.CancelMeasure();
                vm.Say($"{name}: not measured, cancelled");
            }, async () =>
            {
                if (await vm.EndMeasureAsync(name) is { } why)
                {
                    vm.Say($"{name} not measured: {why}", true);
                    _ = Dialogs.Message("Set location", $"{name} is not kept.\n\n{why}.");
                }
            });
    }

    /// <summary>The place the face and the voice are compared with: the places measured, or none.</summary>
    private static OptionButton LocationPicker(VisionFaceWindowViewModel vm)
    {
        var picker = new OptionButton
        {
            TooltipText = "The place where you work: when your face is at least as big and your voice at least as loud as measured there, the picture is framed in red (you speak to the assistant). SET LOCATION measures a place",
            CustomMinimumSize = new Vector2(170, 0),
        };
        void Fill()
        {
            picker.Clear();
            picker.AddItem("no place");
            var chosen = 0;
            foreach (var place in vm.Locations)
            {
                picker.AddItem($"📍 {place.Name}");
                picker.SetItemTooltip(picker.ItemCount - 1, $"face {place.FaceArea * 100:0.0} % of the picture ({place.FaceWidth} × {place.FaceHeight} px), voice {place.VoiceDecibels:0} dB · {place.Camera} · {place.Microphone} · {place.Date.ToLocalTime():yyyy-MM-dd HH:mm}");
                if (place.Name == vm.Location)
                    chosen = picker.ItemCount - 1;
            }
            picker.Select(chosen);
        }
        Fill();
        picker.ItemSelected += index => vm.Location = index == 0 ? string.Empty : vm.Locations[(int)index - 1].Name;
        Action changed = () => Bind.OnMain(() =>
        {
            if (GodotObject.IsInstanceValid(picker))
                Fill();
        });
        vm.LocationsChanged += changed;
        Bind.OnGone(picker, () => vm.LocationsChanged -= changed);
        Bind.When(picker, vm, Fill, nameof(vm.Location));
        return picker;
    }

    /// <summary>FORGET ▾: the names enrolled (as the last frame says them); the one chosen is forgotten, asked first.</summary>
    private static MenuButton ForgetMenu(VisionFaceWindowViewModel vm)
    {
        var forget = new MenuButton
        {
            Text = "FORGET ▾", Flat = false, MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "Delete the pictures kept under a name (asked first). For someone in front of the camera: click their face"
        };
        var menu = forget.GetPopup();
        forget.AboutToPopup += () =>
        {
            menu.Clear();
            foreach (var name in vm.Names)
                menu.AddItem(name);
            if (menu.ItemCount == 0)
            {
                menu.AddItem("Nobody enrolled");
                menu.SetItemDisabled(0, true);
            }
        };
        menu.IndexPressed += index => _ = vm.ForgetAsync(menu.GetItemText((int)index));
        return forget;
    }

    /// <summary>
    /// 👥 FACES: every name enrolled (the folders of the faces), each with its pictures, how many and since when, 📁 its
    /// folder and 🗑 FORGET (asked first: its pictures deleted on this machine and in the search engine).
    /// </summary>
    private static void ShowFaces(VisionFaceWindowViewModel vm)
    {
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var count = Eve.Eve.Text_("", Eve.Eve.Muted, 12);
        void Fill()
        {
            if (!GodotObject.IsInstanceValid(list))
                return;
            foreach (var child in list.GetChildren())
                child.QueueFree();
            var faces = VisionFaceWindowViewModel.EnrolledFaces();
            count.Text = faces.Count == 0 ? "Nobody enrolled yet: click the box of a face in the picture to name it."
                : $"{faces.Count} name(s) enrolled · {faces.Sum(f => f.Pictures.Count)} picture(s) · {VisionFaceWindowViewModel.FacesDirectory}";
            foreach (var face in faces)
                list.AddChild(FaceRow(vm, face, Fill));
        }
        var refresh = Ui.Action("⟳", Fill, tooltip: "Read the folder of the faces again");
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(list);
        var page = Ui.Column(10, Ui.Row(8, Eve.Eve.Caption("Faces enrolled", Eve.Eve.TextBright, 13), Ui.Spring(), refresh), count, scroll);
        page.CustomMinimumSize = new Vector2(640, 480);
        count.CustomMinimumSize = new Vector2(600, 0);
        count.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Fill();
        Dialogs.Show("Faces enrolled", page, new Vector2I(700, 600));
    }

    /// <summary>A name enrolled: its first pictures, its name, how many pictures and since when, 📁 and 🗑 FORGET.</summary>
    private static Control FaceRow(VisionFaceWindowViewModel vm, VisionFaceWindowViewModel.EnrolledFace face, Action refill)
    {
        var pictures = new HBoxContainer();
        pictures.AddThemeConstantOverride("separation", 4);
        foreach (var path in face.Pictures.Take(4))
        {
            var image = Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
                continue;
            var side = 72;
            var scale = (float)side / Math.Max(1, Math.Min(image.GetWidth(), image.GetHeight()));
            image.Resize(Math.Max(1, (int)(image.GetWidth() * scale)), Math.Max(1, (int)(image.GetHeight() * scale)), Image.Interpolation.Lanczos);
            pictures.AddChild(new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(image), CustomMinimumSize = new Vector2(side, side),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                TooltipText = System.IO.Path.GetFileName(path)
            });
        }
        if (face.Pictures.Count == 0)
            pictures.AddChild(Eve.Eve.Text_("no picture", Eve.Eve.Muted, 11));

        // The pictures are named after the day they were taken: 20261113-195937-….jpg.
        static string Day(string path) => System.IO.Path.GetFileName(path) is { Length: >= 8 } file
            && DateTime.TryParseExact(file[..8], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? day.ToString("yyyy-MM-dd") : "?";
        var since = face.Pictures.Count == 0 ? "" : $" · since {Day(face.Pictures[^1])}";
        var name = Eve.Eve.Text_(face.Name, Eve.Eve.TextBright, 15);
        var details = Eve.Eve.Text_($"{face.Pictures.Count} picture(s){since}", Eve.Eve.Muted, 11);
        var info = Ui.Column(2, name, details);
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        var folder = Ui.Action("📁", () => OS.ShellOpen(System.IO.Path.Combine(VisionFaceWindowViewModel.FacesDirectory, face.Name)), tooltip: "Open the folder of this face");
        var forget = Ui.Action("🗑 FORGET", async () =>
        {
            if (!await vm.ForgetAsync(face.Name))
                return;
            // The script deletes the folder a moment later: the list is read again then.
            await System.Threading.Tasks.Task.Delay(900);
            Bind.OnMain(refill);
        }, tooltip: "Delete the pictures of this face, on this machine and in the search engine (asked first)");
        forget.AddThemeColorOverride("font_color", Eve.Eve.Red);
        var row = Ui.Row(12, pictures, info, folder, forget);
        foreach (var button in new Control[] { folder, forget })
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Eve.Eve.Box(Eve.Eve.Raised(0.03f), Eve.Eve.Edge with { A = 0.5f }, 8, 1, 8));
        panel.AddChild(row);
        return panel;
    }

    /// <summary>The line of the objects: on or off, the model, the confidence, what is drawn, the alerts, the log, the record.</summary>
    private static Control ObjectSettings(VisionFaceWindowViewModel vm)
    {
        var models = new OptionButton { TooltipText = "Nano: fast. Medium, large, extra large: each more accurate and slower than the one before (loaded when chosen)", CustomMinimumSize = new Vector2(230, 0) };
        foreach (var model in VisionFaceWindowViewModel.Models)
            models.AddItem(model);
        models.Selected = vm.Model;
        models.ItemSelected += index => vm.Model = (int)index;
        Bind.When(models, vm, () => models.Selected = vm.Model, nameof(vm.Model));

        var slider = Bind.Range(new HSlider
        {
            MinValue = 5, MaxValue = 95, Step = 5, TooltipText = "The smallest confidence of an object kept: lower, more objects (and more mistakes)",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(130, 16)
        }, vm, nameof(vm.Confidence));
        var value = Eve.Eve.Text_($"{vm.Confidence} %", Eve.Eve.TextBright, 11);
        value.AddThemeFontOverride("font", Eve.Eve.Mono);
        value.CustomMinimumSize = new Vector2(44, 0);
        Bind.When(value, vm, () => value.Text = $"{vm.Confidence} %", nameof(vm.Confidence));

        var alert = Ui.Line(vm, nameof(vm.AlertClass), "a class: wine glass, cell phone, cup…");
        alert.CustomMinimumSize = new Vector2(150, 0);
        alert.TooltipText = "An alert is written across the picture when an object of this class is seen (a class of COCO: wine glass, cell phone, cup, bottle, dog…); empty: no alert";

        // Everything but the switch follows it: dimmed while the objects are off. The line wraps when the window is narrow.
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 14);
        flow.AddThemeConstantOverride("v_separation", 4);
        var details = new Control[]
        {
            models,
            Ui.Row(8, Eve.Eve.Text_("Confidence", Eve.Eve.Muted, 11), slider, value),
            Ui.Check("PERSONS ONLY", vm, nameof(vm.PersonsOnly), "Detect the persons only, not the other objects"),
            Ui.Check("CENTRES", vm, nameof(vm.Centers), "The centre of the box of each person, with its coordinates"),
            Ui.Check("DIRECTIONS", vm, nameof(vm.Directions), "Each person followed: the trail of its last positions and the arrow of its average direction"),
            Ui.Check("OVERLAP", vm, nameof(vm.Overlap), "An alert when the boxes of two persons overlap"),
            Ui.Row(6, Eve.Eve.Text_("Alert on", Eve.Eve.Muted, 11), alert),
            Ui.Check("JSON LOG", vm, nameof(vm.LogJson), "Write the objects detected as JSON lines (once a second) in ~/kaonomi/vision/yolo"),
            Ui.Command("📁", vm.OpenLogFolderCommand, tooltip: "Open the folder of the JSON lines"),
            Ui.Check("RECORD OBJECTS", vm, nameof(vm.RecordObjects), "Record in the search engine, once a minute, everything detected during that minute: each kind of object with the most of them at once, how often it was seen and how sure, the persons, the alerts"),
            Ui.Label(vm, nameof(vm.RecordObjectsLabel), Eve.Eve.Muted, 11),
        };
        var on = Ui.Check("DETECT", vm, nameof(vm.Objects), "Detect the objects too (YOLOv8) on the same frames: their boxes under the faces. Off: the faces alone, faster");
        flow.AddChild(on);
        foreach (var control in details)
        {
            control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            flow.AddChild(control);
        }
        Bind.When(flow, vm, () =>
        {
            foreach (var control in details)
                control.Modulate = vm.Objects ? Colors.White : new Color(1, 1, 1, 0.4f);
        }, nameof(vm.Objects));
        var caption = Eve.Eve.Caption("OBJECTS", Eve.Eve.Cyan, 11);
        caption.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        caption.CustomMinimumSize = new Vector2(0, 30);
        caption.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Row(8, caption, flow);
    }

    /// <summary>
    /// The voice, under the picture, as in /recorder: who speaks (LIVE, the smallest similarity, 👥 VOICES, 📁, the
    /// names enrolled), then what the microphone hears now: the VU meter, the spectrum with the envelope of the voice,
    /// the pitch (F0), the formants F1 to F4 and the vowel they make. The voices are enrolled in /recorder.
    /// </summary>
    private static Control Voice(RecorderWindowViewModel voice)
    {
        var source = Ui.Label(voice, nameof(voice.Status), Eve.Eve.Muted, 11);
        source.ClipText = true;
        source.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        source.HorizontalAlignment = HorizontalAlignment.Right;
        Bind.When(source, voice, () =>
        {
            source.TooltipText = voice.Status;
            source.AddThemeColorOverride("font_color", voice.IsError ? Eve.Eve.Red : Eve.Eve.Muted);
        }, nameof(voice.Status), nameof(voice.IsError));
        var who = Media.RecorderView.Speakers(voice, recordings: false);
        who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var scope = new Media.RecorderView.Scope(voice)
        {
            LiveOnly = true,
            CustomMinimumSize = new Vector2(0, 170),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "What the microphone hears now: the VU meter, the spectrum (the white line: the envelope of the voice), the pitch F0, the formants F1 to F4 and the vowel they make. To enrol a voice: /recorder",
        };
        return Ui.Column(4, Ui.Row(8, Eve.Eve.Caption("VOICE", Eve.Eve.Cyan, 11), who), scope);
    }

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (VisionFaceWindowViewModel)viewModel;
        vm.ConfirmAsync = (caption, question) => Dialogs.Confirm(caption, question, "Forget");

        var pause = Ui.Command("⏸ PAUSE", vm.PauseCommand, tooltip: "Freeze the picture, or go on");
        pause.CustomMinimumSize = new Vector2(110, 0);
        Eve.Eve.StyleAccent(pause);
        Bind.When(pause, vm, () => pause.Text = vm.PauseLabel, nameof(vm.PauseLabel));
        var start = Bind.Visible(Ui.Command("▶ START", vm.StartCommand, primary: true, tooltip: "Run the recognition again"), vm, nameof(vm.IsRunning), invert: true);
        var toolbar = Ui.Toolbar(Ui.Row(10,
            VisionScreen.CameraPicker(vm),
            Ui.Action("🖼 ADD RECO", () => AddReco(vm), primary: true, tooltip: "Enrol a new face from a photo: choose the photo and type the name. For a face in front of the camera: click it"),
            ForgetMenu(vm),
            Ui.Action("👥 FACES", () => ShowFaces(vm), tooltip: "Every face enrolled, with its pictures: delete the ones to forget (the camera on or not)"),
            Ui.Command("📁", vm.OpenFacesFolderCommand, tooltip: "Open the folder of the faces enrolled (one folder per name)"),
            pause,
            Ui.Command("📷 PICTURE", vm.SnapshotCommand, tooltip: "Save the picture shown in ~/Pictures/kaonomi/vision"),
            Ui.Command("📁 FOLDER", vm.OpenFolderCommand, tooltip: "Open the folder of the pictures saved"),
            Ui.Action("📍 SET LOCATION", () => SetLocation(vm), tooltip: "Measure the place where you work: sit in your usual working position and speak 5 seconds; the size of your face and the level of your voice are kept (in the search engine too). Then, at least that big and that loud, you speak to the assistant: the picture is framed in red"),
            LocationPicker(vm),
            start,
            Ui.Spring()));

        var caption = Eve.Eve.Text_("Similarity", Eve.Eve.Muted, 11);
        var slider = Bind.Range(new HSlider
        {
            MinValue = 10, MaxValue = 90, Step = 1, TooltipText = "The smallest similarity to recognise a face (36 %: the one of SFace). Higher: fewer mistakes, more faces left unknown",
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(180, 16)
        }, vm, nameof(vm.Threshold));
        var value = Eve.Eve.Text_($"{vm.Threshold} %", Eve.Eve.TextBright, 11);
        value.AddThemeFontOverride("font", Eve.Eve.Mono);
        value.CustomMinimumSize = new Vector2(44, 0);
        Bind.When(value, vm, () => value.Text = $"{vm.Threshold} %", nameof(vm.Threshold));
        var settings = Ui.Row(14,
            Ui.Row(8, caption, slider, value),
            Ui.Check("EXPRESSIONS", vm, nameof(vm.Expressions), "Under each face, what it expresses: angry, disgust, fearful, happy, neutral, sad, surprised"),
            Ui.Check("LANDMARKS", vm, nameof(vm.Landmarks), "The 5 landmarks of each face: the eyes, the nose, the corners of the mouth"),
            Ui.Check("MOTION", vm, nameof(vm.Motion), "The movement of each face: the centre of its box (white dot), an arrow in the direction it moves (where it goes in half a second; none while it is still), and a yellow trail of where it went, fading out in 3 seconds"),
            Ui.Check("MOVEMENT", vm, nameof(vm.Movement), "The rectangle around what moves in the picture (the motion detection of /vision:move, just its rectangle, in magenta). Away from the computer (a place chosen, the face small or none), the 3D animation turns towards it"),
            Ui.Check("RECORD", vm, nameof(vm.Record), "Record in the search engine what is seen of each face, summed up every second: who it is, the share of each emotion (%), the area of the face and its movement (min, max, mean, standard deviation). Written as episodes (a period where its state stays the same) and, at each change, the observation of that second"),
            Ui.Label(vm, nameof(vm.RecordLabel), Eve.Eve.Muted, 11));

        var found = VisionScreen.FoundLabel();
        var screen = new VisionScreen(vm, found)
        {
            // A click in the box of a face names it (or forgets its name).
            CanClick = point => vm.FaceAt(point.X, point.Y) is not null,
            // Framed in red: the face and the voice reach the measure of the place chosen.
            Framed = () => vm.Talking,
        };
        screen.PictureClicked += point =>
        {
            if (vm.FaceAt(point.X, point.Y) is { } face)
                NameFace(vm, screen, face);
        };

        // What the search engine holds of the faces, at the right of the settings.
        var server = Ui.Label(vm, nameof(vm.ServerLabel), Eve.Eve.Muted, 11);
        server.HorizontalAlignment = HorizontalAlignment.Right;
        server.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        server.ClipText = true;
        settings.AddChild(server);

        var page = Ui.Column(6, toolbar, Ui.Margin(Ui.Row(8, Eve.Eve.Caption("FACES", Eve.Eve.Cyan, 11), settings), 8, 0, 8, 0),
            Ui.Margin(ObjectSettings(vm), 8, 0, 8, 0), Ui.FillHeight(screen), Ui.Margin(found, 8, 0, 8, 0));
        if (vm.Voice is { } voice)
            page.AddChild(Ui.Margin(Voice(voice), 8, 0, 8, 0));
        page.AddChild(Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));
        // The models are loaded and the camera opened once the window is shown; both are released with it. The
        // microphone too, for the voice.
        page.Ready += () => _ = vm.Start();
        Bind.OnGone(page, vm.Dispose);
        if (vm.Voice is { } listened)
        {
            listened.ConfirmAsync = (caption, question) => Dialogs.Confirm(caption, question, "Delete");
            page.Ready += listened.Listen;
            Bind.OnGone(page, listened.Dispose);
        }
        return page;
    }
}
