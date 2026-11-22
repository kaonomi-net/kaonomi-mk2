using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Video;

/// <summary>
/// /video: the videos of the home folder as cards: the thumbnail, the length, the notes kept on it, the name, the
/// folder, the size and the date (a video macOS cannot read is dimmed). Filtered as you type (name, folder, mp4…),
/// sorted (most recent, name, largest, longest, with notes). A double click (or Enter) opens the video in its
/// window; ⟳ SCAN walks the home folder again.
/// </summary>
[ToolView(typeof(VideoLibraryWindowViewModel), "Videos", "🎞", 1320, 880)]
public sealed partial class VideoLibraryView : IToolView
{
    private const int CardWidth = 268, ThumbHeight = 151;

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (VideoLibraryWindowViewModel)viewModel;
        vm.OpenRequested += player =>
        {
            player.Saved += vm.NotesChanged;
            Bind.OnMain(() => shell.Open(player, "VideoPlayer#" + player.Key, "🎬 " + player.Name));
        };

        var filter = Ui.Line(vm, nameof(vm.Filter), "Filter: a name, a folder, mp4, mov…");
        filter.CustomMinimumSize = new Vector2(320, 0);
        filter.ClearButtonEnabled = true;
        var order = new OptionButton { TooltipText = "The order of the videos" };
        foreach (var name in VideoLibraryWindowViewModel.Orders)
            order.AddItem(name);
        order.Selected = vm.Order;
        order.ItemSelected += index => vm.Order = (int)index;
        var count = Ui.Label(vm, nameof(vm.CountLabel), Eve.Eve.Muted, 12);
        // The projects: every video, the ones of no project, or the ones of a project.
        var projects = new OptionButton { TooltipText = "The videos of a project (the project of a video is chosen in its player)", CustomMinimumSize = new Vector2(200, 0), ClipText = true };
        var projectIds = new List<string?>();
        void FillProjects()
        {
            var chosen = vm.ProjectFilter;
            projects.Clear();
            projectIds.Clear();
            projectIds.Add(null);
            projects.AddItem("🎯 Every project");
            projectIds.Add(string.Empty);
            projects.AddItem("🎯 No project");
            foreach (var project in KAONOMI.Chat.Services.ProjectCatalog.Projects)
            {
                projectIds.Add(project.Id);
                projects.AddItem("🎯 " + project.Name);
            }
            projects.Selected = Math.Max(0, projectIds.IndexOf(chosen));
        }
        FillProjects();
        projects.ItemSelected += index => vm.ProjectFilter = index >= 0 && index < projectIds.Count ? projectIds[(int)index] : null;
        void OnCatalog() => Bind.OnMain(() => { if (GodotObject.IsInstanceValid(projects)) FillProjects(); });
        KAONOMI.Chat.Services.ProjectCatalog.Changed += OnCatalog;
        projects.TreeExiting += () => KAONOMI.Chat.Services.ProjectCatalog.Changed -= OnCatalog;
        var toolbar = Ui.Toolbar(Ui.Row(10,
            Eve.Eve.Text_("🔎", Eve.Eve.Muted, 14), filter, order, projects,
            Ui.Check("WITH NOTES", vm, nameof(vm.OnlyWithNotes), "Only the videos that have notes in the search engine"),
            Ui.Spring(), count,
            Ui.Command("⟳ SCAN", vm.ScanCommand, tooltip: "Look for the videos of the home folder again")));

        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 14);
        flow.AddThemeConstantOverride("v_separation", 14);
        var empty = Ui.Placeholder("No video yet. ⟳ SCAN looks for them in your home folder (macOS may ask once to let the chat into Desktop, Documents and Downloads).");
        var scroll = Ui.Scroll(Ui.Margin(Ui.Column(0, flow, empty), 16, 14, 16, 16));

        var cards = new Dictionary<VideoItemViewModel, Card>();
        Card? selected = null;
        void Select(Card card)
        {
            selected?.SetSelected(false);
            selected = card;
            card.SetSelected(true);
        }
        void Lay()
        {
            foreach (var child in flow.GetChildren())
                flow.RemoveChild(child);
            foreach (var item in vm.Shown)
            {
                if (!cards.TryGetValue(item, out var card))
                {
                    card = new Card(item);
                    card.Clicked += () => Select(card);
                    card.Opened += () => vm.Open(item);
                    cards[item] = card;
                }
                flow.AddChild(card);
            }
            empty.Visible = vm.Shown.Count == 0;
        }
        vm.ShownChanged += () => Bind.OnMain(Lay);
        Lay();

        var page = new KeyedColumn(() => { if (selected is not null) vm.Open(selected.Item); });
        page.AddThemeConstantOverride("separation", 0);
        page.AddChild(toolbar);
        page.AddChild(Ui.FillHeight(scroll));
        page.AddChild(Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError), nameof(vm.IsBusy)));
        Bind.OnGone(page, vm.Dispose);
        return page;
    }

    /// <summary>The page: Enter opens the video selected.</summary>
    private sealed partial class KeyedColumn(Action open) : VBoxContainer
    {
        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter })
            {
                open();
                AcceptEvent();
            }
        }
    }

    /// <summary>The card of a video.</summary>
    private sealed partial class Card : PanelContainer
    {
        private static readonly SemaphoreSlim Decoding = new(4);

        private readonly TextureRect _thumb;
        private readonly Label _duration;
        private readonly PanelContainer _notes;
        private readonly Label _notesText;
        private readonly Label _unreadable;
        private readonly Label _info;
        private readonly Label _project;
        private bool _selected;
        private bool _hovered;
        private string _shownThumb = string.Empty;

        public VideoItemViewModel Item { get; }

        public event Action? Clicked;

        public event Action? Opened;

        public Card(VideoItemViewModel item)
        {
            Item = item;
            CustomMinimumSize = new Vector2(CardWidth, 0);
            MouseDefaultCursorShape = CursorShape.PointingHand;
            FocusMode = FocusModeEnum.Click;
            TooltipText = item.Path;
            Restyle();

            var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", 4);

            var picture = new Control { CustomMinimumSize = new Vector2(CardWidth - 2, ThumbHeight), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
            picture.AddChild(Full(new ColorRect { Color = new Color(0.02f, 0.025f, 0.035f), MouseFilter = MouseFilterEnum.Ignore }));
            _thumb = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore };
            picture.AddChild(Full(_thumb));
            var glyph = Eve.Eve.Text_("🎞", Eve.Eve.Muted with { A = 0.35f }, 34);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            picture.AddChild(Full(glyph));
            _unreadable = Eve.Eve.Text_("not readable", Eve.Eve.Amber, 11);
            _unreadable.HorizontalAlignment = HorizontalAlignment.Center;
            _unreadable.VerticalAlignment = VerticalAlignment.Bottom;
            picture.AddChild(Full(_unreadable, 0, 0, 0, -26));

            _duration = Eve.Eve.Text_("…", Colors.White, 11);
            _duration.AddThemeFontOverride("font", Eve.Eve.Mono);
            var durationBox = Badge(_duration, new Color(0, 0, 0, 0.72f));
            picture.AddChild(durationBox);
            durationBox.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize, 6);

            _notesText = Eve.Eve.Text_("", new Color(0.02f, 0.05f, 0.08f), 11);
            _notes = Badge(_notesText, Eve.Eve.Cyan);
            picture.AddChild(_notes);
            _notes.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.KeepSize, 6);

            var format = Badge(Eve.Eve.Text_(item.Extension, Eve.Eve.TextBright, 10), new Color(0, 0, 0, 0.6f));
            picture.AddChild(format);
            format.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, 6);
            column.AddChild(picture);

            var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", 1);
            text.AddChild(Line(item.Name, Eve.Eve.TextBright, 13));
            text.AddChild(Line(item.Folder, Eve.Eve.Muted, 11));
            _info = Line(item.Info, Eve.Eve.Muted with { A = 0.8f }, 10);
            text.AddChild(_info);
            _project = Line(string.Empty, Eve.Eve.Cyan, 11);
            text.AddChild(_project);
            var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            foreach (var (side, value) in new[] { ("margin_left", 9), ("margin_right", 9), ("margin_top", 2), ("margin_bottom", 8) })
                margin.AddThemeConstantOverride(side, value);
            margin.AddChild(text);
            column.AddChild(margin);
            AddChild(column);

            Bind.When(this, item, Update);
        }

        private static T Full<T>(T control, float left = 0, float top = 0, float right = 0, float bottom = 0) where T : Control
        {
            control.SetAnchorsPreset(LayoutPreset.FullRect);
            control.OffsetLeft = left;
            control.OffsetTop = top;
            control.OffsetRight = right;
            control.OffsetBottom = bottom;
            return control;
        }

        private static PanelContainer Badge(Label label, Color back)
        {
            var box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            var style = Eve.Eve.Box(back, back, 0, 0);
            style.ContentMarginLeft = style.ContentMarginRight = 6;
            style.ContentMarginTop = style.ContentMarginBottom = 1;
            style.SetCornerRadiusAll(3);
            box.AddThemeStyleboxOverride("panel", style);
            label.MouseFilter = MouseFilterEnum.Ignore;
            box.AddChild(label);
            return box;
        }

        private static Label Line(string value, Color color, int size)
        {
            var label = Eve.Eve.Text_(value, color, size);
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.MouseFilter = MouseFilterEnum.Ignore;
            return label;
        }

        private void Update()
        {
            _duration.Text = Item.DurationLabel;
            _notes.Visible = Item.Notes > 0;
            _notesText.Text = $"✎ {Item.Notes}";
            _unreadable.Visible = !Item.Playable;
            _info.Text = Item.Info;
            _project.Text = Item.ProjectName.Length > 0 ? "🎯 " + Item.ProjectName : string.Empty;
            _project.Visible = Item.ProjectName.Length > 0;
            Modulate = Item.Playable ? Colors.White : new Color(1, 1, 1, 0.55f);
            if (Item.Thumbnail != _shownThumb && Item.Thumbnail.Length > 0)
            {
                _shownThumb = Item.Thumbnail;
                _ = LoadAsync(Item.Thumbnail);
            }
        }

        /// <summary>Decodes the thumbnail away from the frames of the chat, then shows it.</summary>
        private async Task LoadAsync(string path)
        {
            await Decoding.WaitAsync();
            try
            {
                var image = await Task.Run(() => System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null);
                if (image is null || image.IsEmpty())
                    return;
                Bind.OnMain(() =>
                {
                    if (IsInstanceValid(this))
                        _thumb.Texture = ImageTexture.CreateFromImage(image);
                });
            }
            finally
            {
                Decoding.Release();
            }
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            Restyle();
        }

        private void Restyle()
        {
            var edge = _selected ? Eve.Eve.Cyan : _hovered ? Eve.Eve.EdgeBright : Eve.Eve.Edge with { A = 0.5f };
            var back = _selected ? Eve.Eve.Cyan with { A = 0.08f }
                : Eve.Eve.IsLight ? (_hovered ? new Color(0.985f, 0.99f, 1f) : Eve.Eve.Field)
                : _hovered ? Eve.Eve.Raised(0.05f) : Eve.Eve.Raised(0.025f);
            var style = Eve.Eve.Box(back, edge, 0, _selected ? 2 : 1);
            style.SetCornerRadiusAll(6);
            style.ShadowColor = new Color(0, 0, 0, _hovered ? 0.45f : 0.25f);
            style.ShadowSize = _hovered ? 10 : 4;
            AddThemeStyleboxOverride("panel", style);
        }

        public override void _Notification(int what)
        {
            if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit)
            {
                _hovered = what == NotificationMouseEnter;
                Restyle();
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
            {
                GrabFocus();
                Clicked?.Invoke();
                if (button.DoubleClick)
                    Opened?.Invoke();
                AcceptEvent();
            }
        }
    }
}
