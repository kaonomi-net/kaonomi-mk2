using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Video;

/// <summary>
/// The player of a video of /video. On the left, the video (a click plays or pauses), the transport (⏮ ◀ 5 s ⏯ 5 s ▶,
/// the time, IN and OUT, the selection, the volume), the timeline (the strip of pictures, the selection to drag, the
/// marks of the notes) and the panel of the analyses (a question, 🎬 SEGMENT, 🖼 FRAME, 🎧 SOUND, ✎ ANNOTATE,
/// 📸 KEEP FRAME). On the right, the notes kept in the search engine, newest first, filtered by kind: each one with
/// its time (a click goes there and selects its part), its picture, its result, what is said, who speaks, and ✎ EDIT,
/// ⟲ RUN AGAIN, 🕘 HISTORY (its versions, each one can be taken again) and 🗑. ✂ CAPTURE (C): a rectangle drawn on
/// the picture is captured at full size and opened in the image editor. Keys: Space play / pause, ← → 5 s (Shift 1 s),
/// I the start of the selection, O its end, C capture, Esc cancels or clears, Home the start.
/// </summary>
[ToolView(typeof(VideoPlayerWindowViewModel), "Video", "🎬", 1600, 1000, PerObject = true)]
public sealed partial class VideoPlayerView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (VideoPlayerWindowViewModel)viewModel;
        var engine = new VideoEngine { Width = 1600 };
        vm.AskAsync = (title, text, initial) => Dialogs.Ask(title, text, initial, multiline: true);
        vm.ConfirmAsync = (title, text) => Dialogs.Confirm(title, text, "Delete");
        vm.SeekRequested += at => Bind.OnMain(() => engine.Seek(at));
        engine.Ended += () => vm.Tell("End of the video · Space plays it again from where it is");
        engine.Failed += message => Bind.OnMain(() => vm.Tell("The video cannot be played: " + message, true));

        var surface = new VideoSurface(engine);
        vm.Crop = Crop;
        // The pictures open in the image editor of /pictures (the same window, made the same way).
        vm.ImageEditorRequested += file => Bind.OnMain(() =>
            KAONOMI.ChatGodot.Tools.Images.ImageKit.OpenAnalysis(shell, vm.CreateEditor, file, () => OS.ShellOpen(file)));
        surface.Captured += part => _ = vm.CaptureAsync(engine.Position, part);
        void StartCapture()
        {
            engine.Pause();
            surface.Capturing = true;
            surface.GrabFocus();
            vm.Tell("✂ Draw on the picture the part to capture (Esc, or a right click, cancels)");
        }
        var timeline = new VideoTimeline(vm);

        // ── The header ──
        var title = Eve.Eve.Text_(vm.Name, Eve.Eve.TextBright, 15);
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var header = Ui.Toolbar(Ui.Row(10,
            Eve.Eve.Text_("🎬", null, 16), title,
            Ui.Label(vm, nameof(vm.InfoLabel), Eve.Eve.Muted, 12),
            Flat(Ui.Action("📁 FINDER", () => OS.ShellShowInFileManager(vm.Path), tooltip: "Show the video in the Finder")),
            Flat(Ui.Command("⟳", vm.ReloadCommand, tooltip: "Read the notes of the video again from the search engine"))));

        // ── The transport ──
        var play = Flat(Ui.Action("▶", engine.Toggle, primary: true, tooltip: "Play / pause (Space)"));
        play.CustomMinimumSize = new Vector2(54, 0);
        var time = Ui.Label(vm, nameof(vm.TimeLabel), Eve.Eve.TextBright, 14);
        time.AddThemeFontOverride("font", Eve.Eve.Mono);
        var selection = Ui.Label(vm, nameof(vm.SelectionLabel), Eve.Eve.Muted, 12);
        selection.AddThemeFontOverride("font", Eve.Eve.Mono);
        Bind.When(selection, vm, () => selection.AddThemeColorOverride("font_color", vm.HasSelection ? Eve.Eve.Cyan : Eve.Eve.Muted), nameof(vm.HasSelection));
        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.9, CustomMinimumSize = new Vector2(110, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = "Volume" };
        volume.ValueChanged += value => engine.Volume = (float)value;
        var transport = Ui.Row(6,
            Flat(Ui.Action("⏮", () => vm.Seek(0), tooltip: "The start (Home)")),
            Flat(Ui.Action("◀ 5 s", () => vm.Seek(vm.Position - 5), tooltip: "5 seconds back (←; Shift: 1 s)")),
            play,
            Flat(Ui.Action("5 s ▶", () => vm.Seek(vm.Position + 5), tooltip: "5 seconds on (→; Shift: 1 s)")),
            Spacer(8), time, Ui.Spring(),
            Flat(Ui.Command("⇤ IN", vm.SetInCommand, tooltip: "The selection starts here (I)")),
            Flat(Ui.Command("OUT ⇥", vm.SetOutCommand, tooltip: "The selection ends here (O)")),
            selection,
            Flat(Ui.Command("✕", vm.ClearSelectionCommand, tooltip: "Clear the selection (Esc, or a right click on the timeline)")),
            Spacer(8), Eve.Eve.Text_("🔊", Eve.Eve.Muted, 13), volume);

        // ── The analyses ──
        var question = Ui.Line(vm, nameof(vm.Question), "What to look for (optional): who comes in? what is written on the board? what is the tone?");
        question.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        question.ClearButtonEnabled = true;
        var working = Ui.Label(vm, nameof(vm.Working), Eve.Eve.Amber, 12);
        Bind.Visible(working, vm, nameof(vm.IsWorking));
        var actions = Ui.Row(8,
            Flat(Ui.Command("🎬 ANALYSE SEGMENT", vm.AnalyseSegmentCommand, primary: true,
                tooltip: "What happens in the selection: a sheet of its pictures and what is said in it, read by the video model of /models")),
            Flat(Ui.Command("🖼 ANALYSE FRAME", vm.AnalyseFrameCommand, tooltip: "What the picture shown now holds, read by the image model of /models")),
            Flat(Ui.Command("🎧 ANALYSE SOUND", vm.AnalyseSoundCommand,
                tooltip: "The sound of the selection (10 s around the position without one): what is said, who speaks (the voices of /recorder), the loudness and the silences, read by the audio model of /models")),
            Spacer(6),
            Flat(Ui.Command("✎ ANNOTATE", vm.AnnotateCommand, tooltip: "Write a note on the selection, or on the position")),
            Flat(Ui.Command("📸 KEEP FRAME", vm.KeepFrameCommand, tooltip: "Save the picture shown in ~/Pictures/kaonomi/video, at full size, and note it")),
            Flat(Ui.Action("✂ CAPTURE", StartCapture, tooltip: "Draw a rectangle on the picture: that part of the frame, at full size, is saved, noted and opened in the image editor (C)")));
        var analysis = Panel(Ui.Column(8, Ui.Row(8, Eve.Eve.Caption("ASK", Eve.Eve.Cyan), question), actions, working), Eve.Eve.Raised(0.03f), 10);

        var left = Ui.Column(8, Ui.FillHeight(surface), transport, timeline, analysis);

        // ── The notes ──
        var notesList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        notesList.AddThemeConstantOverride("separation", 10);
        var none = Ui.Placeholder("No note yet. Select a part of the video on the timeline, then analyse it, or annotate it. Everything done is kept here and in the search engine, with its versions.");
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        foreach (var (key, label, tip) in new[] { ("all", "ALL", "Every note"), ("segment", "🎬", "The analyses of segments"), ("frame", "🖼", "The analyses of frames"), ("sound", "🎧", "The analyses of the sound"), ("picture", "📸", "The pictures kept"), ("annotation", "✎", "The annotations") })
        {
            var button = new Button { Text = label, ToggleMode = true, ButtonGroup = group, ButtonPressed = vm.Filter == key, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(key == "all" ? 46 : 36, 0) };
            button.Pressed += () => vm.Filter = key;
            filters.AddChild(button);
        }
        void LayNotes()
        {
            foreach (var child in notesList.GetChildren())
                child.QueueFree();
            foreach (var note in vm.Notes)
                notesList.AddChild(NoteCard(vm, note));
            none.Visible = vm.Notes.Count == 0;
        }
        vm.NotesChanged += () => Bind.OnMain(LayNotes);
        LayNotes();
        var notesHeader = Ui.Row(8, Ui.Label(vm, nameof(vm.NotesLabel), Eve.Eve.TextBright, 13), Ui.Spring(), filters);
        var right = Panel(Ui.Column(8, notesHeader, Eve.Eve.Separator(), Ui.FillHeight(Ui.Scroll(Ui.Column(10, notesList, none)))), Eve.Eve.Sunken(0.18f), 10);
        right.CustomMinimumSize = new Vector2(440, 0);

        var split = new HSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        split.AddChild(Ui.Margin(left, 10, 10, 4, 10));
        split.AddChild(Ui.Margin(right, 4, 10, 10, 10));

        var page = new PlayerPage(vm, engine, surface, timeline, play, StartCapture);
        page.AddThemeConstantOverride("separation", 0);
        page.AddChild(engine);
        page.AddChild(header);
        page.AddChild(split);
        page.AddChild(Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError)));
        page.Ready += () => engine.Open(vm.Path, 0, false);
        Bind.OnGone(page, vm.Dispose);
        return page;
    }

    private static T Flat<T>(T button) where T : Control
    {
        button.FocusMode = Control.FocusModeEnum.None;
        return button;
    }

    private static Control Spacer(int width) => new() { CustomMinimumSize = new Vector2(width, 0), MouseFilter = Control.MouseFilterEnum.Ignore };

    private static PanelContainer Panel(Control content, Color back, int margin)
    {
        var panel = new PanelContainer();
        var style = Eve.Eve.Box(back, Eve.Eve.Edge with { A = 0.4f }, margin);
        style.SetCornerRadiusAll(6);
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(content);
        return panel;
    }

    private static Color ColorOf(string kind) => kind switch
    {
        "segment" => Eve.Eve.Cyan,
        "frame" => Eve.Eve.Amber,
        "sound" => new Color(0.66f, 0.5f, 1f),
        "picture" => Eve.Eve.Green,
        _ => new Color(1f, 0.47f, 0.72f)
    };

    // ── A note ──

    private const int Shortened = 700;

    private static Control NoteCard(VideoPlayerWindowViewModel vm, VideoNoteViewModel note)
    {
        var color = ColorOf(note.Kind);
        var card = new PanelContainer();
        var style = Eve.Eve.Box(Eve.Eve.Raised(0.035f), Eve.Eve.Edge with { A = 0.45f }, 10);
        style.BorderWidthLeft = 3;
        style.BorderColor = Eve.Eve.Edge with { A = 0.45f };
        style.SetCornerRadiusAll(6);
        card.AddThemeStyleboxOverride("panel", style);
        var stripe = Eve.Eve.Box(Eve.Eve.IsLight ? Eve.Eve.Field : Eve.Eve.Raised(0.035f), color, 10, 0);
        stripe.BorderWidthLeft = 3;
        stripe.SetCornerRadiusAll(6);
        stripe.ContentMarginLeft = 12;
        card.AddThemeStyleboxOverride("panel", stripe);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);

        var range = Ui.Action(note.Range, () => vm.GoTo(note), tooltip: note.IsPoint ? "Go to this time" : "Go there and select this part of the video");
        range.Flat = true;
        range.FocusMode = Control.FocusModeEnum.None;
        range.AddThemeFontOverride("font", Eve.Eve.Mono);
        range.AddThemeColorOverride("font_color", Eve.Eve.Cyan);
        var top = Ui.Row(6, Eve.Eve.Caption($"{note.Icon}  {note.KindLabel}", color, 11), range, Ui.Spring());
        if (note.Versions > 1)
            top.AddChild(Eve.Eve.Text_($"v{note.Document.Version} · {note.Versions} versions", Eve.Eve.Muted, 10));
        column.AddChild(top);

        var heading = Eve.Eve.Text_(note.Document.Title, Eve.Eve.TextBright, 13, wrap: true);
        column.AddChild(heading);

        if (note.Document.Question.Length > 0)
            column.AddChild(Eve.Eve.Text_("❓ " + note.Document.Question, Eve.Eve.Amber, 12, wrap: true));

        if (note.Document.Picture.Length > 0)
        {
            var picture = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(0, note.Kind == "segment" ? 150 : 170), MouseDefaultCursorShape = Control.CursorShape.PointingHand,
                TooltipText = "Double-click: open it in the image editor (as in /pictures)"
            };
            _ = ShowPicture(picture, note.Document.Picture);
            picture.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true })
                    _ = vm.EditPictureAsync(note);
            };
            column.AddChild(picture);
        }

        var text = note.Document.Text;
        var body = Ui.Markdown(text.Length > Shortened ? text[..Shortened] + "…" : text);
        column.AddChild(body);
        if (text.Length > Shortened)
        {
            var whole = false;
            var more = Ui.Action("▾ MORE", () => { }, tooltip: "The whole text");
            more.Flat = true;
            more.FocusMode = Control.FocusModeEnum.None;
            more.Pressed += () =>
            {
                whole = !whole;
                Ui.SetMarkdown(body, whole ? text : text[..Shortened] + "…");
                more.Text = whole ? "▴ LESS" : "▾ MORE";
            };
            column.AddChild(more);
        }

        if (note.Document.Transcript.Length > 0 || note.Document.Speakers.Count > 0)
        {
            var said = Ui.Markdown((note.Document.Speakers.Count > 0 ? "**Who speaks**  \n" + string.Join("  \n", note.Document.Speakers) + "\n\n" : string.Empty)
                + (note.Document.Transcript.Length > 0 ? "**What is said**  \n" + note.Document.Transcript : string.Empty));
            var panel = Panel(said, Eve.Eve.Sunken(0.25f), 8);
            panel.Visible = false;
            var toggle = Ui.Action("💬 WHAT IS SAID", () => panel.Visible = !panel.Visible, tooltip: "The speech recognition of the part, and who speaks");
            toggle.Flat = true;
            toggle.FocusMode = Control.FocusModeEnum.None;
            column.AddChild(toggle);
            column.AddChild(panel);
        }
        if (note.Kind == "sound" && note.Document.Comment.Length > 0)
            column.AddChild(Eve.Eve.Text_("📊 " + note.Document.Comment, Eve.Eve.Muted, 11, wrap: true));

        column.AddChild(Eve.Eve.Text_(note.Footer, Eve.Eve.Muted with { A = 0.85f }, 10));

        var buttons = Ui.Row(4);
        buttons.AddChild(Small("✎ EDIT", () => _ = vm.EditAsync(note), "Edit the text: a new version"));
        if (note.IsAnalysis)
            buttons.AddChild(Small("⟲ RUN AGAIN", () => _ = vm.RerunAsync(note), "Run the analysis again on the same part, with the same question: a new version"));
        if (note.Document.Picture.Length > 0)
            buttons.AddChild(Small("🖌 EDIT PICTURE", () => _ = vm.EditPictureAsync(note), "Open the picture in the image editor (as in /pictures; a double click on it does the same)"));
        if (note.Document.PictureFile.Length > 0)
            buttons.AddChild(Small("📁", () => OS.ShellShowInFileManager(note.Document.PictureFile), "Show the picture in the Finder"));
        buttons.AddChild(Small($"🕘 HISTORY ({note.Versions})", () => ShowHistory(vm, note), "Every version of this note; any of them can be taken again"));
        buttons.AddChild(Ui.Spring());
        buttons.AddChild(Small("🗑", () => _ = vm.DeleteAsync(note), "Delete the note (its versions are kept in the search engine)"));
        column.AddChild(buttons);
        card.AddChild(column);
        return card;
    }

    private static Button Small(string text, Action action, string tooltip)
    {
        var button = Ui.Action(text, action, tooltip: tooltip);
        button.Flat = true;
        button.FocusMode = Control.FocusModeEnum.None;
        button.AddThemeFontSizeOverride("font_size", 11);
        button.AddThemeColorOverride("font_color", Eve.Eve.Muted);
        button.AddThemeColorOverride("font_hover_color", Eve.Eve.Cyan);
        return button;
    }

    private static async Task ShowPicture(TextureRect target, string base64)
    {
        var image = await Task.Run(() =>
        {
            try
            {
                var picture = new Image();
                return picture.LoadJpgFromBuffer(Convert.FromBase64String(base64)) == Error.Ok ? picture : null;
            }
            catch (FormatException)
            {
                return null;
            }
        });
        if (image is not null && GodotObject.IsInstanceValid(target))
            target.Texture = ImageTexture.CreateFromImage(image);
    }

    /// <summary>The versions of a note, newest first; RESTORE takes one again (a new version, a copy of it).</summary>
    private static void ShowHistory(VideoPlayerWindowViewModel vm, VideoNoteViewModel note)
    {
        var versions = vm.VersionsOf(note);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 10);
        foreach (var version in versions)
        {
            var latest = version.Version == versions[0].Version;
            var what = version.Operation switch
            {
                "create" => "created", "edit" => "edited", "rerun" => "run again", "restore" => "restored", "delete" => "deleted",
                "project" => "project " + (string.IsNullOrWhiteSpace(version.ProjectId) ? "removed" : ProjectCatalog.NameOf(version.ProjectId)), var other => other
            };
            var head = Ui.Row(8,
                Eve.Eve.Text_($"v{version.Version}", latest ? Eve.Eve.Cyan : Eve.Eve.TextBright, 13),
                Eve.Eve.Text_($"{what} · {version.Date.ToLocalTime():dd MMM yyyy HH:mm}" + (version.Model.Length > 0 ? $" · {version.Model}" : string.Empty), Eve.Eve.Muted, 11),
                Ui.Spring());
            if (!latest && !version.IsDeleted)
            {
                var restore = Small("↺ RESTORE", () => _ = vm.RestoreAsync(version), "Make this version the note again (a new version, the others kept)");
                head.AddChild(restore);
            }
            else if (latest)
                head.AddChild(Eve.Eve.Text_("current", Eve.Eve.Cyan, 10));
            var text = version.IsDeleted ? "*(deleted)*" : version.Text.Length > 500 ? version.Text[..500] + "…" : version.Text;
            list.AddChild(Panel(Ui.Column(4, head, Ui.Markdown(text)), latest ? Eve.Eve.Cyan with { A = 0.06f } : Eve.Eve.Raised(0.03f), 8));
        }
        var scroll = Ui.Scroll(Ui.Margin(list, 4));
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Dialogs.Show($"History · {note.Document.Title}", scroll, new Vector2I(760, 640));
    }

    /// <summary>
    /// Crops a picture to a part given in fractions of its size, at most <paramref name="maxWidth"/> wide (0: as it
    /// is), as PNG or JPEG.
    /// </summary>
    private static byte[]? Crop(byte[] picture, (double X, double Y, double Width, double Height) part, int maxWidth, string format)
    {
        var image = new Image();
        if (image.LoadJpgFromBuffer(picture) != Error.Ok && image.LoadPngFromBuffer(picture) != Error.Ok)
            return null;
        var region = new Rect2I(
            (int)Math.Round(part.X * image.GetWidth()), (int)Math.Round(part.Y * image.GetHeight()),
            Math.Max(1, (int)Math.Round(part.Width * image.GetWidth())), Math.Max(1, (int)Math.Round(part.Height * image.GetHeight())));
        region = region.Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
        if (region.Size.X < 1 || region.Size.Y < 1)
            return null;
        var cropped = image.GetRegion(region);
        if (maxWidth > 0 && cropped.GetWidth() > maxWidth)
            cropped.Resize(maxWidth, Math.Max(1, cropped.GetHeight() * maxWidth / cropped.GetWidth()), Image.Interpolation.Lanczos);
        return format == "png" ? cropped.SavePngToBuffer() : cropped.SaveJpgToBuffer(0.88f);
    }

    /// <summary>
    /// The page of the player: it follows the engine every frame, and takes the keys of the player while its window is
    /// the one in front (unless a field is being typed in): Space, ← →, Home, I, O, C, Esc.
    /// </summary>
    private sealed partial class PlayerPage(VideoPlayerWindowViewModel vm, VideoEngine engine, VideoSurface surface, VideoTimeline timeline, Button play, Action capture) : VBoxContainer
    {
        private bool _wasPlaying;

        public override void _Process(double delta)
        {
            if (Math.Abs(vm.Position - engine.Position) > 0.001)
                vm.Position = engine.Position;
            if (engine.Playing != _wasPlaying)
            {
                _wasPlaying = engine.Playing;
                vm.IsPlaying = engine.Playing;
                play.Text = engine.Playing ? "⏸" : "▶";
                surface.QueueRedraw();
            }
            if (engine.Playing || engine.Loading)
            {
                timeline.QueueRedraw();
                surface.QueueRedraw();
            }
        }

        public override void _UnhandledKeyInput(InputEvent @event)
        {
            if (@event is not InputEventKey { Pressed: true } key || key.CtrlPressed || key.MetaPressed || key.AltPressed)
                return;
            // Only the player in front; never while a field is being typed in.
            if (!ReferenceEquals(Shell.Current.Deck.Front?.ViewModel, vm) || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
                return;
            var step = key.ShiftPressed ? 1 : 5;
            switch (key.Keycode)
            {
                case Key.Space: engine.Toggle(); break;
                case Key.Left: vm.Seek(vm.Position - step); break;
                case Key.Right: vm.Seek(vm.Position + step); break;
                case Key.Home: vm.Seek(0); break;
                case Key.I: vm.SetInCommand.Execute(null); break;
                case Key.O: vm.SetOutCommand.Execute(null); break;
                case Key.C: capture(); break;
                case Key.Escape:
                    if (surface.Capturing)
                    {
                        surface.Capturing = false;
                        vm.Tell("Capture cancelled");
                    }
                    else
                        vm.ClearSelectionCommand.Execute(null);
                    break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }
    }
}
