using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The chat, on the right of the screen, bound to the view model of the Avalonia chat (<see cref="MainViewModel"/>): the
/// 3D animation of the preset (its mouth follows the voice), the clock and the state of the server, the model, preset
/// and history, the system prompt, the conversation (Markdown answers revealed as they come, thumbs, copy, code, stats),
/// the input with the push-to-talk microphone and its language, Send / Stop, and a menu for the exports (JSON, Word),
/// the saved prompts and the prompt collections.
/// </summary>
public partial class ChatPanel : PanelContainer
{
    private readonly MainViewModel _vm;
    private Control _avatarStage = null!;
    /// <summary>The block of the animation (hidden while a full chat shows it, <see cref="FullChatWindow.OpenCount"/>).</summary>
    private Control _avatarBlock = null!;
    private IAvatar? _avatar;
    private SceneAnimation? _shownAnimation;
    private Label _clock = null!, _date = null!, _server = null!, _status = null!, _avatarState = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _messages = null!;
    private TextEdit _input = null!;
    private Button _mic = null!;
    private Label _ticker = null!;
    private double _tickerOffset;
    private bool _tickerHasText;
    private float _tickerWidth = -1;
    private bool _stickToBottom = true;

    /// <summary>The width of the chat (open).</summary>
    private const float FullWidth = 460;

    /// <summary>The share of the chat still seen when it is slid to the right.</summary>
    private const float TuckedShare = 0.1f;

    /// <summary>The frame of the content: it keeps its whole width while the chat narrows, what is past it is cut.</summary>
    private Control _slide = null!;
    private Control _content = null!;
    private bool _tucked;
    private Tween? _tuckMotion;

    public ChatPanel(MainViewModel viewModel)
    {
        _vm = viewModel;
        CustomMinimumSize = new Vector2(FullWidth, 0);
        // A card floating over the desktop (Eve.Floating): rounded corners, a hairline edge and a soft shadow under it.
        AddThemeStyleboxOverride("panel", Eve.FloatingBox(Eve.Panel));
    }

    public override void _Ready()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        // The content in a frame that cuts it: slid to the right, the chat narrows and its left edge only stays in view.
        // It cuts along the rounded corners too: the header and the bar of the input end round like the chat.
        _slide = Eve.RoundedClip();
        AddChild(_slide);
        _slide.AddChild(column);
        _content = column;
        _slide.Resized += FitContent;
        column.AddChild(BuildHeader());
        column.AddChild(BuildAvatar());
        column.AddChild(BuildTicker());
        column.AddChild(BuildOptions());
        column.AddChild(BuildSystemPrompt());
        column.AddChild(BuildConversation());
        column.AddChild(BuildInput());
        column.AddChild(BuildStatus());

        // Unsubscribed when the panel is freed (a new theme builds the interface again).
        Action<string> pulse = word => Bind.OnMain(() => _avatar?.Pulse(word.Length));
        Action<bool> speakingChanged = speaking => Bind.OnMain(() =>
        {
            if (!IsInstanceValid(this))
                return;
            if (_avatar is not null)
                _avatar.Speaking = speaking;
            UpdateAvatarState();
        });
        // FULLSCREEN:ON / OFF of a script of /remote: the voice mode (the chat in full screen) or back (without a login
        // when the script put it on).
        Action<bool> fullScreen = on => Bind.OnMain(() =>
        {
            if (!IsInstanceValid(this))
                return;
            if (on)
                EnterVoiceMode(byCode: true);
            else
                LeaveVoiceMode();
        });
        _vm.WordStreamed += pulse;
        _vm.SpeakingChanged += speakingChanged;
        // IMAGE:<file> of a script of /remote: the picture over the animation, for 20 s.
        Action<string> image = path => Bind.OnMain(() =>
        {
            if (!IsInstanceValid(this) || _avatarView is not { } view || !IsInstanceValid(view))
                return;
            // The chat knows while a picture is shown (a script waits for it before leaving the full screen).
            _imagesShown++;
            _vm.IsShowingImage = true;
            void Ended()
            {
                _imagesShown = Math.Max(0, _imagesShown - 1);
                _vm.IsShowingImage = _imagesShown > 0;
            }
            if (!ImageShow.Show(view, path, Ended))
            {
                Ended();
                _vm.Status = $"The picture {Path.GetFileName(path)} cannot be read.";
            }
        });
        // The YouTube videos sharing entities with the answer: a card beside the animation, for 30 s.
        Action<System.Collections.Generic.IReadOnlyList<KAONOMI.Chat.Models.RelatedVideo>> related = videos => Bind.OnMain(() =>
        {
            if (IsInstanceValid(this))
                RelatedVideosCard.Show(videos, () =>
                    _avatarView is { } view && IsInstanceValid(view) && view.IsVisibleInTree() && view.Size.X > 4 ? view.GetGlobalRect()
                    : IsInstanceValid(_avatarStage) && _avatarStage.IsVisibleInTree() ? _avatarStage.GetGlobalRect() : null);
        });
        _vm.RelatedVideosFound += related;
        // --related-demo (tests): the card with the last YouTube videos of the server, as if they shared entities.
        if (OS.GetCmdlineUserArgs().Contains("--related-demo"))
            GetTree().CreateTimer(5).Timeout += async () =>
            {
                try
                {
                    var last = await KAONOMI.Chat.Services.KaonomiAssistantService.Shared!.GetLastYoutubeVideosAsync(3);
                    related(last.Where(v => v.VideoId is not null).Select(v => new KAONOMI.Chat.Models.RelatedVideo(v, [.. (v.Entities is { Count: > 0 } e ? e : ["(demo)"]).Take(3)])).ToList());
                }
                catch (Exception ex)
                {
                    KAONOMI.Chat.Services.ActivityLog.Info("Chat", "--related-demo: " + ex.Message);
                }
            };
        _vm.FullScreenRequested += fullScreen;
        _vm.ImageShowRequested += image;
        Bind.OnGone(this, () =>
        {
            _vm.WordStreamed -= pulse;
            _vm.SpeakingChanged -= speakingChanged;
            _vm.FullScreenRequested -= fullScreen;
            _vm.ImageShowRequested -= image;
            _vm.RelatedVideosFound -= related;
        });
        Bind.When(this, _vm, () =>
        {
            if (_avatar is not null)
                _avatar.Thinking = _vm.IsAwaitingAnswer;
            UpdateAvatarState();
        }, nameof(MainViewModel.IsAwaitingAnswer), nameof(MainViewModel.IsListening), nameof(MainViewModel.IsTranscribing));
        Bind.When(this, _vm, ShowPresetAnimation, nameof(MainViewModel.SelectedPreset), nameof(MainViewModel.Animation));
        // A full chat open: no animation here (also for a chat built again by a new theme while it is open).
        Action fullChat = () => Bind.OnMain(FullChatChanged);
        FullChatWindow.OpenChanged += fullChat;
        Bind.OnGone(this, () => FullChatWindow.OpenChanged -= fullChat);
        FullChatChanged();
    }

    /// <summary>
    /// The person in front of the camera (/vision running, a place chosen). Close enough: the 3D animation turns
    /// towards their face, and comes closer in the chat (not in the voice mode, in full screen). Away: it turns towards
    /// what moves (the rectangle MOVEMENT). Nothing moving, or while it walks: it looks ahead again. The picture of
    /// the camera turns the same way as the face in its picture (a face on the right of the picture: to the right).
    /// </summary>
    private void FollowPerson()
    {
        if (_avatar is null)
            return;
        static Vector2 Look((double X, double Y) at) => new((float)(at.X - 0.5) * 2, (float)(0.5 - at.Y) * 2);
        if (IsWandering || _vm.Presence is not { WatchesPlace: true } presence)
            _avatar.Follow(0, null);
        // Close enough: it follows the face, and comes closer in the chat (not in full screen, where it is big already).
        else if (presence is { PersonNear: true, PersonAt: { } face })
            _avatar.Follow(InVoiceMode ? 0 : 1, Look(face));
        // Away (where « dis <name> » is needed): it follows what moves in the room.
        else if (presence.MovementAt is { } moving)
            _avatar.Follow(0, Look(moving));
        else
            _avatar.Follow(0, null);
    }

    /// <summary>The second shown by the clock (the clock, the date and the next alarm are written once a second).</summary>
    private long _shownSecond = -1;

    public override void _Process(double delta)
    {
        FollowPerson();
        var now = DateTime.Now;
        var second = now.Ticks / TimeSpan.TicksPerSecond;
        if (second != _shownSecond)
        {
            _shownSecond = second;
            _clock.Text = now.ToString("HH:mm:ss");
            var date = now.ToString("ddd d MMM yyyy").ToUpperInvariant();
            // The next alarm of /alarm within a day, beside the date.
            if (KAONOMI.Chat.Services.AlarmClock.Shared.Upcoming(now).FirstOrDefault() is { Alarm: not null } next && next.Ring - now < TimeSpan.FromDays(1))
                date += $"  ·  ⏰ {next.Ring:HH:mm}";
            if (_date.Text != date)
                _date.Text = date;
        }
        // The ticker scrolls from right to left.
        // Its text and its width are read when it changes, not at each frame (each read crosses into the engine).
        if (_tickerHasText && _ticker.Visible)
        {
            _tickerOffset -= delta * 60;
            if (_tickerWidth < 0)
                _tickerWidth = _ticker.GetMinimumSize().X;
            if (_tickerOffset < -_tickerWidth)
                _tickerOffset = _ticker.GetParentControl().Size.X;
            _ticker.Position = new Vector2((float)_tickerOffset, 3);
        }
    }

    // ── Header: title, clock, server ──

    /// <summary>The content at the whole width of the chat (even while it is narrowed), at the height of the frame.</summary>
    private void FitContent()
    {
        _content.Position = Vector2.Zero;
        _content.Size = new Vector2(Math.Max(_slide.Size.X, FullWidth - 1), _slide.Size.Y);
    }

    /// <summary>True while the chat is slid to the right (only its left edge in view).</summary>
    public bool IsTucked => _tucked;

    /// <summary>
    /// A double-click on the bar at the top of the chat: it slides to the right of the screen, 90 % of its width hidden
    /// (the desktop takes the room), or comes back.
    /// </summary>
    public void ToggleTucked() => SetTucked(!_tucked, animate: true);

    private void SetTucked(bool tucked, bool animate)
    {
        _tucked = tucked;
        _tuckMotion?.Kill();
        var width = tucked ? FullWidth * TuckedShare : FullWidth;
        if (!animate)
        {
            CustomMinimumSize = new Vector2(width, 0);
            return;
        }
        _tuckMotion = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _tuckMotion.TweenProperty(this, "custom_minimum_size:x", width, 0.35);
    }

    private Control BuildHeader()
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = "Double-click: slide the chat to the right of the screen (again: back)" };
        // A double-click on the bar slides the chat aside (the clock keeps its own: the alarm clock).
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left })
            {
                panel.AcceptEvent();
                ToggleTucked();
            }
        };
        var box = Eve.Box(Eve.Header, Eve.Edge with { A = 0f }, 0, 0);
        box.BorderWidthBottom = 1;
        box.BorderColor = Eve.Edge with { A = 0.6f };
        box.ContentMarginLeft = box.ContentMarginRight = 12;
        box.ContentMarginTop = box.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer();
        panel.AddChild(row);
        var titles = new VBoxContainer();
        titles.AddThemeConstantOverride("separation", 0);
        titles.AddChild(Eve.Caption("Kaonomi", Eve.TextBright, 15));
        _server = Eve.Caption("Server · checking", Eve.Muted, 10);
        titles.AddChild(_server);
        titles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(titles);
        var time = new VBoxContainer();
        time.AddThemeConstantOverride("separation", 0);
        _clock = Eve.Text_("--:--:--", Eve.TextBright, 18);
        _clock.AddThemeFontOverride("font", Eve.Mono);
        _clock.HorizontalAlignment = HorizontalAlignment.Right;
        _date = Eve.Caption("", Eve.Muted, 10);
        _date.HorizontalAlignment = HorizontalAlignment.Right;
        time.AddChild(_clock);
        time.AddChild(_date);
        // A double-click on the clock opens the alarm clock.
        time.MouseFilter = MouseFilterEnum.Stop;
        time.MouseDefaultCursorShape = CursorShape.PointingHand;
        time.TooltipText = "Double-click: the alarm clock (/alarm)";
        time.GuiInput += e =>
        {
            if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left })
            {
                time.AcceptEvent();
                _vm.OpenTool("/alarm", false);
            }
        };
        row.AddChild(time);
        Bind.When(_server, _vm, () =>
        {
            var ok = _vm.IsServerReachable;
            _server.Text = ok ? "● SERVER ONLINE" : "● SERVER OFFLINE";
            _server.AddThemeColorOverride("font_color", ok ? Eve.Green : Eve.Red);
        }, nameof(MainViewModel.IsServerReachable));
        return panel;
    }

    // ── The 3D animation ──

    private Control BuildAvatar()
    {
        var stage = new Control
        {
            CustomMinimumSize = new Vector2(0, 250), ClipContents = true, MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Double-click: voice mode (the animation alone on the screen, a microphone)\nDrag it to the left and release: it leaves its frame and walks beside the window in use (double-click it, or its empty frame, to beam it back)"
        };
        stage.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
                return;
            if (click.DoubleClick)
            {
                _dragFrom = null;
                // Out of its frame: called back; in it: the voice mode.
                if (IsWandering)
                    ComeHome(animate: true);
                else
                    EnterVoiceMode();
            }
            else if (click.Pressed)
                _dragFrom = click.Position;
            else if (_dragFrom is { } from)
            {
                _dragFrom = null;
                // Dragged to the left, more than up or down, and released: it leaves its frame.
                var moved = click.Position - from;
                if (moved.X <= -WanderDrag && Math.Abs(moved.Y) < -moved.X)
                    Wander();
            }
        };
        _avatarAway = Eve.Caption("OUT ON THE DESKTOP  ·  DOUBLE-CLICK: BEAM BACK", Eve.Muted, 10);
        _avatarAway.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _avatarAway.GrowHorizontal = GrowDirection.Both;
        _avatarAway.MouseFilter = MouseFilterEnum.Ignore;
        _avatarAway.Visible = false;
        stage.AddChild(_avatarAway);
        _avatarStage = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _avatarStage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        stage.AddChild(_avatarStage);
        _avatarState = Eve.Caption("Standby", Eve.Muted, 10);
        _avatarState.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _avatarState.Position = new Vector2(12, 226);
        stage.AddChild(_avatarState);
        _avatarBlock = stage;
        return stage;
    }

    // ── The animation out of its frame ──

    /// <summary>How far to the left the animation is dragged (in pixels) to leave its frame.</summary>
    private const float WanderDrag = 50;

    /// <summary>Where the button was pressed on the animation (null: not pressed).</summary>
    private Vector2? _dragFrom;

    /// <summary>The animation out of its frame, walking beside the window in use (null: in its frame).</summary>
    private AvatarWalker? _walker;

    private Label _avatarAway = null!;

    private bool IsWandering => _walker is { } walker && IsInstanceValid(walker) && !walker.IsQueuedForDeletion();

    /// <summary>The animation leaves its frame and walks over the desktop, beside the window in use.</summary>
    private void Wander()
    {
        if (IsWandering || InVoiceMode || FullChatShown || _avatarStage.GetChildCount() == 0 || Shell.Current.Root is not Control root)
            return;
        var view = (Control)_avatarStage.GetChild(0);
        var walker = new AvatarWalker(Shell.Current.Deck, view, view.GetGlobalRect());
        walker.HomeRequested += () => ComeHome(animate: true);
        root.AddChild(walker);
        view.Reparent(walker, false);
        // Its head, over the body it gets.
        walker.Seat(view);
        _walker = walker;
        _avatarAway.Visible = true;
        ActivityLog.Info("Chat", "The 3D animation left its frame: it walks to the window in use");
    }

    /// <summary>
    /// The animation goes back in its frame: at once, or by a transporter (it glitters away where it stands on the
    /// desktop, then appears in its frame the same way).
    /// </summary>
    private void ComeHome(bool animate)
    {
        if (_walker is not { } walker || !IsInstanceValid(walker))
        {
            _walker = null;
            _avatarAway.Visible = false;
            return;
        }
        void Arrive(bool beamed)
        {
            if (!IsInstanceValid(this))
                return;
            if (IsInstanceValid(walker))
            {
                if (walker.View is { } view && IsInstanceValid(view) && !view.IsQueuedForDeletion())
                {
                    if (view is FaceAvatar face)
                        face.Yaw = 0;
                    // A swimmer comes back as its picture is (not turned nor mirrored).
                    view.Scale = Vector2.One;
                    view.Rotation = 0;
                    view.PivotOffset = Vector2.Zero;
                    view.Reparent(_avatarStage, false);
                    view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                    view.Modulate = Colors.White;
                    // It appears in its frame as it left the desktop.
                    if (beamed && _avatarBlock.IsVisibleInTree())
                        TransporterBeam.In(_avatarBlock, view);
                }
                walker.QueueFree();
            }
            if (ReferenceEquals(_walker, walker))
                _walker = null;
            _avatarAway.Visible = false;
        }
        if (animate && _avatarStage.IsVisibleInTree())
            walker.BeamOut(() => Arrive(beamed: true));
        else
            Arrive(beamed: false);
    }

    /// <summary>The voice mode is shown now (it holds the animation of the chat).</summary>
    private bool InVoiceMode => _voiceMode is { } voice && IsInstanceValid(voice) && !voice.IsQueuedForDeletion();

    /// <summary>A full chat open (/fullchat) shows the animation: the chat has none then (one 3D animation drawn, not two); it comes back when the last full chat closes.</summary>
    private bool FullChatShown => FullChatWindow.OpenCount > 0 && !InVoiceMode;

    private void FullChatChanged()
    {
        if (!IsInstanceValid(this))
            return;
        _avatarBlock.Visible = !FullChatShown;
        if (FullChatShown)
        {
            // Out of its frame: it leaves the desktop too (the full chat shows the animation).
            if (_walker is { } walker && IsInstanceValid(walker))
                walker.QueueFree();
            _walker = null;
            _avatarAway.Visible = false;
            foreach (var child in _avatarStage.GetChildren())
                child.QueueFree();
            _avatar = null;
            _avatarView = null;
            _shownAnimation = null;
        }
        else if (_shownAnimation is null)
            ShowPresetAnimation();
    }

    /// <summary>The pictures of IMAGE: being shown (until the animation is back at its place).</summary>
    private int _imagesShown;

    /// <summary>The view of the 3D animation (in the chat, or in the voice mode while it is shown).</summary>
    private Control? _avatarView;

    /// <summary>The voice mode shown now (null: the usual chat).</summary>
    private VoiceMode? _voiceMode;

    /// <summary>
    /// The voice mode: the animation on the whole screen with a microphone; it comes back here when it ends.
    /// <paramref name="byCode"/>: put on by code (an alarm, a script of /remote), which may then take it back without a
    /// login; else (a double-click, --voice) leaving it asks for one.
    /// </summary>
    public void EnterVoiceMode(bool byCode = false)
    {
        // The voice mode takes the whole chat: back to its width first.
        if (_tucked)
            SetTucked(false, animate: false);
        if (_voiceMode is { } shown && GodotObject.IsInstanceValid(shown) && !shown.IsQueuedForDeletion())
            return;
        // Out of its frame: back in it at once, the voice mode takes it from there.
        ComeHome(animate: false);
        if (_avatarStage.GetChildCount() == 0 || Shell.Current.Root is not Control root)
            return;
        var view = (Control)_avatarStage.GetChild(0);
        VoiceMode? mode = null;
        // Back in the chat: the animation shown at the end (a preset changed meanwhile has replaced the first one).
        mode = new VoiceMode(_vm, view, shown =>
        {
            if (GodotObject.IsInstanceValid(shown))
            {
                shown.Reparent(_avatarStage, false);
                shown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            }
            if (ReferenceEquals(_voiceMode, mode))
                _voiceMode = null;
            // A full chat opened meanwhile: it shows the animation, not the chat.
            FullChatChanged();
        }) { OpenedByCode = byCode };
        _voiceMode = mode;
        root.AddChild(mode);
    }

    /// <summary>
    /// Code asks to leave the voice mode (the end of an alarm, FULLSCREEN:OFF of a script of /remote): left at once when
    /// code put it on, else the login is asked, as for a double-click or Esc.
    /// </summary>
    public void LeaveVoiceMode()
    {
        if (_voiceMode is not { } shown || !GodotObject.IsInstanceValid(shown) || shown.IsQueuedForDeletion())
        {
            _voiceMode = null;
            return;
        }
        if (shown.OpenedByCode)
        {
            shown.Leave();
            _voiceMode = null;
        }
        else
            shown.AskToLeave();
    }

    private void ShowPresetAnimation()
    {
        var animation = _vm.SelectedPreset?.Animation ?? _vm.Animation;
        if (animation == SceneAnimation.None)
            animation = SceneAnimation.Kaonomi;
        // The full chat shows it: made again here when it closes.
        if (FullChatShown || _shownAnimation == animation)
            return;
        _shownAnimation = animation;
        // In the voice mode the animation is shown there: the new one replaces it there.
        var inVoiceMode = _voiceMode is { } voice && IsInstanceValid(voice) && !voice.IsQueuedForDeletion();
        if (!inVoiceMode)
            foreach (var child in _avatarStage.GetChildren())
                child.QueueFree();
        Control view;
        if (animation == SceneAnimation.Jarvis)
        {
            var jarvis = new JarvisAvatar();
            view = jarvis;
            _avatar = jarvis;
        }
        else
        {
            FaceAvatar face;
            try
            {
                face = new FaceAvatar(animation);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
            {
                face = new FaceAvatar(SceneAnimation.Kaonomi);
            }
            view = face;
            _avatar = face;
        }
        view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (inVoiceMode)
            _voiceMode!.SwapAvatar(view);
        else if (IsWandering)
            _walker!.Swap(view);   // out of its frame: the new animation walks in its place
        else
            _avatarStage.AddChild(view);
        _avatarView = view;
        // A picture of IMAGE: shown stays in front of the new animation.
        Callable.From(() => ImageShow.Retarget(view)).CallDeferred();
        _avatar.VoiceLevel = () => _vm.SpeechLevel;
        _avatar.Thinking = _vm.IsAwaitingAnswer;
        UpdateAvatarState();
    }

    private void UpdateAvatarState()
    {
        var (text, color) = _vm.IsListening ? ("● Listening", Eve.Red)
            : _vm.IsTranscribing ? ("Recognising the speech", Eve.Amber)
            : _vm.IsAwaitingAnswer ? ("Processing", Eve.Amber)
            : _vm.IsSpeakingOrPreparing ? ("Speaking", Eve.Cyan)
            : ("Standby", Eve.Muted);
        _avatarState.Text = $"{(_vm.SelectedPreset?.Name ?? "Kaonomi").ToUpperInvariant()}  ·  {text.ToUpperInvariant()}";
        _avatarState.AddThemeColorOverride("font_color", color);
    }

    // ── Ticker ──

    private Control BuildTicker()
    {
        var band = new Control { CustomMinimumSize = new Vector2(0, 22), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        var back = new ColorRect { Color = Eve.Sunken(0.4f), MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        band.AddChild(back);
        _ticker = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _ticker.AddThemeFontSizeOverride("font_size", 12);
        _ticker.AddThemeColorOverride("font_color", Eve.Text);
        band.AddChild(_ticker);
        Bind.Visible(band, _vm, nameof(MainViewModel.HasTickerItems));
        void Fill()
        {
            _ticker.Text = string.Join("      ", _vm.TickerItems.Select(t => $"{(t.HasArrow ? t.Arrow + " " : string.Empty)}{t.Text}"));
            // Measured again at the next frame it scrolls (see _Process).
            _tickerHasText = _ticker.Text.Length > 0;
            _tickerWidth = -1;
        }
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) => Bind.OnMain(() =>
        {
            if (IsInstanceValid(_ticker))
                Fill();
        });
        _vm.TickerItems.CollectionChanged += changed;
        Bind.OnGone(band, () => _vm.TickerItems.CollectionChanged -= changed);
        Fill();
        return band;
    }

    // ── Model, preset, history ──

    private Control BuildOptions()
    {
        var margin = Margins(12, 8, 12, 6);
        var column = new VBoxContainer();
        margin.AddChild(column);

        var actions = new HBoxContainer();
        var toggle = Eve.Button("⚙", tooltip: "Show or hide the model, preset and history");
        Bind.Check(toggle, _vm, nameof(MainViewModel.IsChatOptionsVisible));
        actions.AddChild(toggle);
        // The actions as icons, each named in its tooltip.
        var system = Eve.Button("📜", tooltip: "System prompt · show or hide the system prompt");
        Bind.Check(system, _vm, nameof(MainViewModel.IsSystemPromptVisible));
        actions.AddChild(system);
        var newChat = Eve.Button("💬", tooltip: "New chat · start a new conversation");
        Bind.Command(newChat, _vm.NewConversationCommand);
        actions.AddChild(newChat);
        // The prompts: the one being typed saved as JSON, one loaded from a JSON file, a whole collection run.
        var savePrompt = Eve.Button("💾", tooltip: $"Save prompt · save the prompt being typed as a JSON file ({PromptCollection.Pattern}: {{\"prompt\": …}})");
        savePrompt.Pressed += () => _ = SavePromptAsync();
        actions.AddChild(savePrompt);
        var loadPrompt = Eve.Button("📂", tooltip: $"Load prompt · load a prompt from a JSON file ({PromptCollection.Pattern}, or a text file) into the prompt box");
        loadPrompt.Pressed += () => _ = LoadPromptAsync();
        actions.AddChild(loadPrompt);
        var loadCollection = Eve.Button("📚", tooltip: "Load collection · read a folder of prompts (*.prompt.json; a folder ending in -FR, -EN… gives the language, the name before it the collection), count them, ask, then run them in the chat one after the other; each prompt and its completion are saved as JSON in the <model>-<collection> folder of the collection");
        loadCollection.Pressed += () => _ = PromptCollectionRunner.RunAsync(_vm);
        actions.AddChild(loadCollection);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actions.AddChild(spacer);
        var note = Eve.Button("📝", tooltip: "Note · add a note about the chat (sent to the server with the conversation data)");
        note.Pressed += () => Tools.Data.NoteView.Open(Shell.Current, "Kaonomi Chat", _vm);
        actions.AddChild(note);
        actions.AddChild(BuildMenu());
        column.AddChild(actions);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 4);
        Bind.Visible(grid, _vm, nameof(MainViewModel.IsChatOptionsVisible));
        grid.AddChild(Eve.Caption("Model"));
        var models = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        Bind.Options(models, _vm.Models, _vm, nameof(MainViewModel.SelectedModel));
        grid.AddChild(models);
        grid.AddChild(Eve.Caption("Preset"));
        var presets = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        // The presets read again when the store changes (a preset saved or deleted in /preset or /remote).
        Bind.Options(presets, Bind.Live(() => _vm.Presets), _vm, nameof(MainViewModel.SelectedPreset), p => p.Name, itemsProperty: nameof(MainViewModel.Presets));
        grid.AddChild(presets);
        grid.AddChild(Eve.Caption("History"));
        var history = new SpinBox { MinValue = 0, MaxValue = 20, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Bind.Number(history, _vm, nameof(MainViewModel.HistoryExchanges));
        // Used only straight to LM Studio (Direct): through the server the prompt goes alone (the server keeps the context).
        Bind.When(history, _vm, () =>
        {
            history.Editable = _vm.IsDirect;
            history.Modulate = new Color(1, 1, 1, _vm.IsDirect ? 1f : 0.45f);
            history.TooltipText = _vm.IsDirect
                ? "Previous exchanges written into each prompt"
                : "Used only with Direct checked: through the server the prompt goes alone";
        }, nameof(MainViewModel.IsDirect));
        grid.AddChild(history);
        // Direct: the prompts go straight to LM Studio (ollamaUrl of config.yml), without the Kaonomi server.
        grid.AddChild(Eve.Caption("Direct"));
        grid.AddChild(Ui.Check($"Straight to {_vm.LlmProviderName}, without the server", _vm, nameof(MainViewModel.IsDirect),
            $"Checked: the prompts go straight to {_vm.LlmProviderName} ({_vm.OllamaUrl}) with the system prompt and the history, " +
            "without the Kaonomi server (no agents, no context kept, nothing saved there). Unchecked: through the server."));
        column.AddChild(grid);
        return margin;
    }

    private MenuButton BuildMenu()
    {
        var menu = new MenuButton { Text = "⋯", TooltipText = "Export the conversation, saved prompts, prompt collection", Flat = false };
        var popup = menu.GetPopup();
        popup.AddItem("Export the conversation as JSON…", 0);
        popup.AddItem("Export the conversation as Word…", 1);
        popup.AddSeparator();
        popup.AddItem("Save the prompt being typed…", 2);
        popup.AddItem("Load a saved prompt…", 3);
        popup.AddSeparator();
        popup.AddItem("Run a prompt collection…", 4);
        popup.IdPressed += id =>
        {
            switch (id)
            {
                case 0: _ = ExportAsync("json", "JSON", (stream, export) => ChatExportService.WriteJsonAsync(stream, export)); break;
                case 1: _ = ExportAsync("docx", "Word document", (stream, export) => ChatExportService.WriteWordAsync(stream, export)); break;
                case 2: _ = SavePromptAsync(); break;
                case 3: _ = LoadPromptAsync(); break;
                case 4: _ = PromptCollectionRunner.RunAsync(_vm); break;
            }
        };
        return menu;
    }

    // ── System prompt ──

    private Control BuildSystemPrompt()
    {
        var margin = Margins(12, 0, 12, 6);
        var edit = new TextEdit
        {
            PlaceholderText = "Instructions given to the model before the conversation (optional)",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 90)
        };
        Bind.Edit(edit, _vm, nameof(MainViewModel.SystemPrompt));
        edit.TooltipText = "The system prompt of the chat, shared by every preset: it is added after the system prompt of the preset in use";
        // What the preset in use adds (its own system prompt, set in /preset).
        var preset = Ui.Label(_vm, nameof(MainViewModel.PresetPromptNote), Eve.Muted, 11, wrap: true);
        margin.AddChild(Ui.Column(4, edit, preset));
        Bind.Visible(margin, _vm, nameof(MainViewModel.IsSystemPromptVisible));
        return margin;
    }

    // ── Conversation ──

    private Control BuildConversation()
    {
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var margin = Margins(10, 6, 10, 6);
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(margin);
        _messages = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _messages.AddThemeConstantOverride("separation", 10);
        margin.AddChild(_messages);
        var empty = Eve.Text_("Type a message, hold 🎤 to speak, or a / command (/help).", Eve.Muted, 13, wrap: true);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        Bind.Items(_messages, _vm.Messages, message => new MessageView(message, _vm), empty);
        _messages.AddChild(empty);
        // Follows the end of the conversation while it is at the end.
        var bar = _scroll.GetVScrollBar();
        bar.ValueChanged += _ => _stickToBottom = bar.Value >= bar.MaxValue - bar.Page - 40;
        bar.Changed += () =>
        {
            if (_stickToBottom)
                _scroll.ScrollVertical = (int)bar.MaxValue;
        };
        return _scroll;
    }

    // ── Input ──

    private Control BuildInput()
    {
        var panel = new PanelContainer();
        var box = Eve.Box(Eve.Header with { A = 0.7f }, Eve.Edge with { A = 0f }, 10, 0);
        box.BorderWidthTop = 1;
        box.BorderColor = Eve.Edge with { A = 0.6f };
        panel.AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer();
        panel.AddChild(column);

        _input = new TextEdit
        {
            PlaceholderText = "Message · Enter: send · Shift+Enter: new line · ↑ ↓: previous prompts · /: the commands",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 70),
            ScrollFitContentHeight = false
        };
        Bind.Edit(_input, _vm, nameof(MainViewModel.Input));
        _input.GuiInput += OnInputKey;
        column.AddChild(_input);
        // The commands that match what is typed after a slash, in a list over the box.
        _completion = new CommandCompletion(_input, ChooseCommand);
        AddChild(_completion);

        var row = new HBoxContainer();
        var language = new OptionButton { TooltipText = "Language of the voice input (and of the voice reading the answers)", CustomMinimumSize = new Vector2(64, 0) };
        Bind.Options(language, _vm.VoiceLanguages, _vm, nameof(MainViewModel.VoiceLanguage), l => l.ToUpperInvariant());
        row.AddChild(language);
        _mic = Eve.Button("🎤", tooltip: "Hold to speak, release: the speech is sent");
        _mic.CustomMinimumSize = new Vector2(48, 34);
        _mic.ButtonDown += () => _vm.StartListening();
        _mic.ButtonUp += () => _ = _vm.StopListeningAsync();
        Bind.When(_mic, _vm, () =>
        {
            var box2 = _vm.IsListening ? Eve.Box(Eve.Red with { A = 0.55f }, Eve.Red, 6)
                : _vm.IsTranscribing ? Eve.Box(Eve.Amber with { A = 0.4f }, Eve.Amber, 6)
                : null;
            if (box2 is null)
                _mic.RemoveThemeStyleboxOverride("normal");
            else
                _mic.AddThemeStyleboxOverride("normal", box2);
            if (box2 is not null)
                _mic.AddThemeStyleboxOverride("pressed", box2);
            else
                _mic.RemoveThemeStyleboxOverride("pressed");
        }, nameof(MainViewModel.IsListening), nameof(MainViewModel.IsTranscribing));
        row.AddChild(_mic);
        // The live chat: listened to without the button, sent after 2 seconds of silence.
        row.AddChild(Ui.Check("LIVE CHAT", _vm, nameof(MainViewModel.LiveChat),
            "Live chat: the microphone is listened to and what you say is written in the box as you speak; after 2 seconds of silence it is sent (as SEND). Not heard while the assistant answers and reads its answer aloud"));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var stop = Eve.Button("■ STOP", tooltip: "Stop the request (and the voice)");
        stop.Pressed += () =>
        {
            Bind.Run(_vm.SendCancelCommand);
            _vm.StopSpeaking();
        };
        Bind.When(stop, _vm.SendCommand, () => stop.Disabled = !_vm.SendCommand.IsRunning, nameof(_vm.SendCommand.IsRunning));
        row.AddChild(stop);
        var send = Eve.Button("SEND ▶", primary: true, tooltip: "Send (Enter)");
        send.CustomMinimumSize = new Vector2(100, 34);
        Bind.Command(send, _vm.SendCommand);
        row.AddChild(send);
        column.AddChild(row);
        return panel;
    }

    private CommandCompletion _completion = null!;

    /// <summary>A command chosen in the list of the completion: written in the box, and run when asked.</summary>
    private void ChooseCommand(string command, bool run)
    {
        _vm.Input = command;
        _input.Text = command;
        _input.SetCaretLine(0);
        _input.SetCaretColumn(command.Length);
        _completion.Close();
        _input.GrabFocus();
        if (run && !_vm.SkipStreaming())
            Bind.Run(_vm.SendCommand);
    }

    private void OnInputKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key)
            return;
        // The list of the commands is open: the keys go to it first.
        if (_completion.IsOpen)
        {
            switch (key.Keycode)
            {
                case Key.Up:
                    _input.AcceptEvent();
                    _completion.Move(-1);
                    return;
                case Key.Down:
                    _input.AcceptEvent();
                    _completion.Move(1);
                    return;
                case Key.Tab:
                    _input.AcceptEvent();
                    ChooseCommand(_completion.Chosen!, run: false);
                    return;
                case Key.Enter or Key.KpEnter when !key.ShiftPressed && !_completion.IsExact:
                    _input.AcceptEvent();
                    ChooseCommand(_completion.Chosen!, run: true);
                    return;
                case Key.Escape:
                    _input.AcceptEvent();
                    _completion.Close();
                    return;
            }
        }
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter when !key.ShiftPressed:
                _input.AcceptEvent();
                _completion.Close();
                if (_vm.SkipStreaming())
                    return;
                Bind.Run(_vm.SendCommand);
                break;
            case Key.Up when _input.GetCaretLine() == 0 && _vm.RecallPreviousInput():
                _input.AcceptEvent();
                _input.CallDeferred(TextEdit.MethodName.SetCaretLine, _input.GetLineCount() - 1);
                break;
            case Key.Down when _input.GetCaretLine() == _input.GetLineCount() - 1 && _vm.RecallNextInput():
                _input.AcceptEvent();
                break;
            // The voice stopped and the answer shown whole: the live chat listens again at once.
            case Key.Escape when _vm.CanInterrupt:
                _input.AcceptEvent();
                _vm.Interrupt();
                break;
        }
    }

    /// <summary>Puts the cursor in the input.</summary>
    public void FocusInput()
    {
        if (IsInstanceValid(_input) && _input.IsInsideTree())
            _input.GrabFocus();
    }

    // ── Status ──

    private Control BuildStatus()
    {
        var margin = Margins(12, 4, 12, 6);
        _status = Eve.Text_("", Eve.Muted, 11);
        _status.ClipText = true;
        Bind.Text(_status, _vm, nameof(MainViewModel.Status));
        margin.AddChild(_status);
        return margin;
    }

    // ── Exports and prompts ──

    private async Task ExportAsync(string extension, string typeName, Func<Stream, ChatExport, Task> write)
    {
        if (_vm.HasNoMessages)
        {
            _vm.Status = "Nothing to export: the conversation is empty";
            return;
        }
        var path = await Dialogs.SaveFile($"Export the conversation ({typeName})", $"kaonomi-chat-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}", [$"*.{extension} ; {typeName}"]);
        if (path is null)
            return;
        try
        {
            var export = _vm.BuildExport();
            await using (var stream = File.Create(path))
                await write(stream, export);
            _vm.Status = $"Exported to {Path.GetFileName(path)}";
            ActivityLog.Info("Export", $"Conversation exported as {typeName} to {path} ({export.Messages.Count} message(s))");
        }
        catch (Exception ex)
        {
            _vm.Status = "Export failed: " + ex.Message;
            ActivityLog.Error("Export", $"{typeName} export to {path} failed", ex);
        }
    }

    private async Task SavePromptAsync()
    {
        var text = _vm.Input.Trim();
        if (text.Length == 0)
        {
            _vm.Status = "Type a prompt first, then save it";
            return;
        }
        Directory.CreateDirectory(_vm.PromptDirectory);
        var name = string.Concat(text.Split('\n')[0].Take(40).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        var path = await Dialogs.SaveFile("Save the prompt", (name.Length == 0 ? $"prompt-{DateTime.Now:yyyyMMdd-HHmmss}" : name) + PromptCollection.Extension,
            [$"{PromptCollection.Pattern} ; Prompt"], _vm.PromptDirectory);
        if (path is null)
            return;
        var target = PromptCollection.WithExtension(path);
        await File.WriteAllTextAsync(target, PromptCollection.Serialize(text), new System.Text.UTF8Encoding(false));
        _vm.Status = $"Prompt saved: {Path.GetFileName(target)}";
    }

    private async Task LoadPromptAsync()
    {
        var path = await Dialogs.OpenFile("Load a prompt", [$"{PromptCollection.Pattern} ; Prompt", "*.txt, *.md ; Text"], _vm.PromptDirectory);
        if (path is null)
            return;
        var text = await File.ReadAllTextAsync(path);
        _vm.Input = PromptCollection.Parse(text) ?? text;
        _vm.Status = $"Prompt loaded: {Path.GetFileName(path)}";
        _input.GrabFocus();
    }

    private static MarginContainer Margins(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }
}

/// <summary>A message of the conversation: its author and time, its text (Markdown for the answers), its actions.</summary>
public partial class MessageView : PanelContainer
{
    public MessageView(ChatMessageViewModel message, MainViewModel chat)
    {
        var color = message.IsUser ? Eve.Cyan : Eve.Amber;
        var box = Eve.Box(message.IsUser ? Eve.Cyan with { A = 0.07f } : Eve.Sunken(0.3f), Eve.Edge with { A = 0f }, 10, 0);
        box.BorderWidthLeft = 2;
        box.BorderColor = color with { A = 0.8f };
        AddThemeStyleboxOverride("panel", box);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        AddChild(column);

        var header = new HBoxContainer();
        var author = Eve.Caption($"{message.Author}  ·  {message.Timestamp:HH:mm}", color, 10);
        author.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(author);
        var like = Small("👍", "Like this answer (evaluation)");
        var dislike = Small("👎", "Dislike this answer (evaluation)");
        like.Pressed += () => Evaluate(message, chat, false);
        dislike.Pressed += () => Evaluate(message, chat, true);
        Bind.When(like, message, () =>
        {
            like.Visible = dislike.Visible = message.CanEvaluate;
            like.Modulate = message.IsLiked ? Eve.Green : Colors.White;
            dislike.Modulate = message.IsDisliked ? Eve.Red : Colors.White;
        }, nameof(ChatMessageViewModel.CanEvaluate), nameof(ChatMessageViewModel.IsLiked), nameof(ChatMessageViewModel.IsDisliked));
        header.AddChild(like);
        header.AddChild(dislike);
        var copy = Small("⧉", "Copy to the clipboard");
        copy.Pressed += () =>
        {
            Dialogs.Copy(message.Content);
            copy.Text = "✓";
            GetTree().CreateTimer(1.2).Timeout += () => copy.Text = "⧉";
        };
        Bind.Visible(copy, message, nameof(ChatMessageViewModel.IsPending), invert: true);
        header.AddChild(copy);
        column.AddChild(header);

        var body = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeFontSizeOverride("normal_font_size", 13);
        body.AddThemeFontSizeOverride("mono_font_size", 12);
        body.AddThemeFontSizeOverride("bold_font_size", 13);
        // The text shown as plain text while the answer is revealed (the words appended, nothing parsed again), and
        // the version of the last formatting asked (the Markdown of a long answer is converted off the main thread).
        var shownPlain = (string?)null;
        var formatVersion = 0;
        string Format(string text) => message.IsError ? $"[color=#{Eve.Red.ToHtml(false)}]{Links.Bbcode(text)}[/color]"
            : message.IsUser ? Links.Bbcode(text)
            : MarkdownBbcode.Convert(LatexText.Convert(text));
        Bind.When(body, message, () =>
        {
            var text = message.Content;
            if (message.IsRevealing && !message.IsError && !message.IsUser)
            {
                // Revealed word by word: the new words are appended as they are (the Markdown comes at the end), the
                // label laying out the new piece only.
                if (shownPlain is not null && text.StartsWith(shownPlain, StringComparison.Ordinal))
                    body.AppendText(MarkdownBbcode.Escape(text[shownPlain.Length..]));
                else
                    body.Text = MarkdownBbcode.Escape(text);
                shownPlain = text;
                return;
            }
            shownPlain = null;
            var version = ++formatVersion;
            // A short text is formatted at once; a long one on a thread of the pool, the newest asked winning.
            if (text.Length < 3000)
            {
                body.Text = Format(text);
                return;
            }
            _ = System.Threading.Tasks.Task.Run(() => Format(text)).ContinueWith(done => Bind.OnMain(() =>
            {
                if (version == formatVersion && IsInstanceValid(body) && done.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                    body.Text = done.Result;
            }));
        }, nameof(ChatMessageViewModel.Content), nameof(ChatMessageViewModel.IsError), nameof(ChatMessageViewModel.IsRevealing));
        column.AddChild(body);

        var pending = new ProgressBar { Indeterminate = true, CustomMinimumSize = new Vector2(0, 3), ShowPercentage = false };
        Bind.Visible(pending, message, nameof(ChatMessageViewModel.IsPending));
        column.AddChild(pending);

        var code = Eve.Button("", tooltip: "Open the code of the answer");
        code.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        Bind.Text(code, message, nameof(ChatMessageViewModel.ShowCodeLabel));
        Bind.Visible(code, message, nameof(ChatMessageViewModel.HasCode));
        code.Pressed += () => Bind.Run(chat.ShowCodeCommand, message);
        column.AddChild(code);

        var stats = Eve.Text_("", Eve.Muted, 10);
        Bind.Text(stats, message, nameof(ChatMessageViewModel.Stats));
        Bind.Visible(stats, message, nameof(ChatMessageViewModel.HasStats));
        column.AddChild(stats);
    }

    private static Button Small(string text, string tooltip)
    {
        var button = new Button { Text = text, Flat = true, TooltipText = tooltip, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
        button.AddThemeFontSizeOverride("font_size", 11);
        return button;
    }

    private static void Evaluate(ChatMessageViewModel message, MainViewModel chat, bool negative)
    {
        var evaluation = chat.CreateEvaluation(message, negative);
        // A thumb down on an answer of the default model: out of the cache of the gateway, and asked again to the
        // escalation model (while the popup asks what was wrong).
        if (negative)
            _ = chat.RetryDislikedAsync(message);
        if (Shell.Current.Open(evaluation) is { } window)
            window.Closed += () =>
            {
                if (evaluation.Succeeded)
                    message.Feedback = !negative;
            };
    }
}
