using System;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot;

/// <summary>
/// The Kaonomi chat in Godot, in the style of EVE Online, with every function of the default chat (its view models and
/// services are shared as they are). On the left the bar of commands (one icon per tool, a command box at its top), on
/// the right the chat with its 3D animation; between them the desktop: every tool opens there as a window, flat in front
/// while it is in use, at the back in 3D in a perspective otherwise (a click on a card brings it back). There are no red
/// strings between the windows in this version. /logout takes all of it away and shows the login screen again
/// (<see cref="Logout"/>).
/// Command line (after <c>--</c>): <c>--prompt="…"</c> sends a first prompt, <c>--command=/ontology</c> runs a command,
/// <c>--avatar=HAL</c> picks the 3D animation, <c>--back-after=2</c> sends the window in front to the back after 2 s, <c>--voice</c> starts in the voice mode, <c>--theme=kaonomi</c> starts in that theme, <c>--themes</c> opens the screen of the themes, <c>--no-intro</c> starts without the intro, <c>--no-services</c> starts without checking the services (nor starting them, nor opening /process), <c>--windowed</c> starts in a window where it was at the last close (by default: full screen on the smallest screen; <c>--largest-screen</c>: on the largest one).
/// </summary>
public partial class Main : Control
{
    private MainViewModel _vm = null!;
    private Shell _shell = null!;
    private WindowDeck _deck = null!;
    private Neocom _neocom = null!;
    private ChatSettings _settings = null!;
    private ChatPanel _chat = null!;
    private ShaderMaterial _space = null!;

    /// <summary>The desk as the background of the theme was told it last (left and right, in parts of the width).</summary>
    private Vector2 _desk;

    /// <summary>The wallpaper, over the background of the theme and under the whole interface (the chat included).</summary>
    private TextureRect _wallpaper = null!;

    /// <summary>The background of the theme (its picture and its animated shader): not drawn while the wallpaper covers it.</summary>
    private readonly System.Collections.Generic.List<CanvasItem> _themeBackground = [];

    /// <summary>The last voice level given to the shader of the background (set only when it changes).</summary>
    private float _energy = -1;
    private Control _ui = null!;
    private double _serverCheck, _tickerRefresh = 60, _layoutSave = 20;

    public override void _Ready()
    {
        DisplayServer.WindowSetTitle("KAONOMI");
        var window = GetWindow();
        // The chat starts on the smallest screen, in full screen (--largest-screen: the largest one; --windowed: where it was at the last close, as it
        // was). It goes to that screen first: the scale of the interface is the one of the screen it is on.
        // --window-size=1920x1080: a window of that size (a test of another screen: 16:9, 4K…).
        Vector2I? windowSize = Argument("--window-size=") is { } sized && sized.Split('x') is [var w, var h]
                               && int.TryParse(w, out var width) && int.TryParse(h, out var height) ? new Vector2I(width, height) : null;
        var windowed = OS.GetCmdlineUserArgs().Contains("--windowed") || windowSize is not null;
        if (!windowed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetCurrentScreen(DeskLayout.StartScreen());
        }
        // Retina / high-DPI screens: the interface at the scale of the densest screen connected. On macOS Godot counts
        // every screen in the pixels of the densest one (a 5120×1440 monitor beside a Retina laptop is 10240×2880
        // for it), so the scale of the screen the window is on (1 for that monitor) would draw everything half as large.
        var scale = Math.Max(1f, Math.Max(DisplayServer.ScreenGetMaxScale(), DisplayServer.ScreenGetScale(window.CurrentScreen)));
        KAONOMI.Chat.Services.ActivityLog.Info("App", $"Interface scale {scale} (screen {window.CurrentScreen + 1}: scale {DisplayServer.ScreenGetScale(window.CurrentScreen)}, the densest: {DisplayServer.ScreenGetMaxScale()})");
        window.ContentScaleFactor = scale;
        window.MinSize = (Vector2I)(new Vector2(1200, 760) * scale);
        if (scale > 1)
        {
            window.Size = (Vector2I)(new Vector2(1600, 960) * scale);
            window.MoveToCenter();
        }
        GetTree().AutoAcceptQuit = false;
        // Every view model asks with a dialog before a change it cannot undo, even one shown without a window of its own.
        Confirmations.Ask = KAONOMI.ChatGodot.Tools.Writing.WritingUi.Confirm;
        // The bank of the 1000 questions of /iq, shipped with the chat.
        IqWindowViewModel.BankPath = ProjectSettings.GlobalizePath("res://Data/iq-questions.json");
        // The web pages of the tools (Show on map…) open in the browser of the chat.
        PictureLibrary.WebOpener = url => Bind.OnMain(() => WebWindow.Open(url));
        // A right click on a text selected anywhere offers to annotate it.
        Tools.Data.Annotations.HookTextMenus(GetTree());
        // The URLs shown anywhere open in the browser.
        Eve.Links.Hook(GetTree());
        // Each SAVE of a window goes to the journal of the activities (index useractivities).
        ActivityJournal.Hook(GetTree());
        // Full screen on the smallest screen (--largest-screen: the largest one; --windowed: the window of the chat where it was at the last close: screen,
        // position, size, maximised…), once the scene is set up: switching to full screen during _Ready makes Godot
        // add a child to the busy root ("Parent node is busy").
        if (windowSize is { } fixedSize)
            Callable.From(() =>
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(fixedSize);
            }).CallDeferred();
        else if (windowed)
            Callable.From(() => DeskLayout.RestoreMainWindow(window)).CallDeferred();
        else
            Callable.From(DeskLayout.FullScreenOnStartScreen).CallDeferred();

        // The clicks, keys and screenshot of the command line are set now: they test the login screen too.
        ScheduleAutomation();
        // --users-file=<file.json>: the users of a test, not those of ~/kaonomi/users.json.
        if (Argument("--users-file=") is { } usersFile)
            UserStore.FilePath = usersFile.Trim('"');
        // Nothing of the chat is built (no window, no microphone, no command run) before somebody is logged in.
        Eve.Eve.Use(Eve.Eve.Find(Argument("--theme=") ?? Preferences.Theme));
        Theme = Eve.Eve.Shared;
        // --user=<login> --password=<password>: logged in from the command line (a test); refused, the screen asks.
        string? refused = null;
        if (Argument("--user=") is { } login)
        {
            if (UserStore.Verify(login.Trim('"'), (Argument("--password=") ?? string.Empty).Trim('"')) is { } user)
            {
                UserSession.SignIn(user);
                CheckServices();
                return;
            }
            refused = "The login or the password of the command line is wrong.";
            ActivityLog.Info("Users", $"Login of the command line refused for {ActivityLog.Excerpt(login)}");
        }
        AskLogin(Argument("--user=")?.Trim('"'), refused);
    }

    /// <summary>
    /// The login screen over the whole window (at the start, and after /logout): once somebody is logged in, the
    /// services are checked and the chat is built for that user.
    /// </summary>
    private void AskLogin(string? login = null, string? message = null) =>
        AddChild(new LoginScreen(account =>
        {
            UserSession.SignIn(account);
            // Once the click that logged in is handled: the interface is built at the next frame.
            Callable.From(CheckServices).CallDeferred();
        }, login, message));

    /// <summary>The services checked at this start (null: --no-services); /process opens once the chat is there.</summary>
    private ServiceStartupViewModel? _services;

    /// <summary>
    /// Between the login and the chat: the search engine, the Mongo engine, the LLM gateway, the graph database and the
    /// server are checked, and started one after the other when they do not run, in a popup that tells where each is
    /// (<see cref="ServiceStartupScreen"/>); then the chat is built. --no-services builds the chat at once, and does not
    /// open /process (a chat started for a test).
    /// </summary>
    private void CheckServices()
    {
        if (OS.GetCmdlineUserArgs().Contains("--no-services"))
        {
            Start();
            return;
        }
        // A viewer does not run /process: it starts no service either.
        _services = new ServiceStartupViewModel(ChatSettings.Load(), UserSession.CanRun("/process", out _));
        // Once the click or the timer that closes the popup is handled: the interface is built at the next frame.
        AddChild(new ServiceStartupScreen(_services, () => Callable.From(Start).CallDeferred()));
    }

    /// <summary>
    /// The chat is there: /process opens (not for a viewer), and the services still starting (OPEN THE CHAT NOW) are
    /// told in the status bar once they are all looked at.
    /// </summary>
    private async void AfterServices()
    {
        if (_services is not { } services)
            return;
        _services = null;
        // The chat of this login: after /logout, what is still awaited here tells nothing to the next one.
        if (_vm is not { } vm)
        {
            services.Dispose();
            return;
        }
        if (UserSession.CanRun("/process", out _))
            vm.OpenTool("/process", detached: false);
        try
        {
            // Still starting behind the chat (OPEN THE CHAT NOW): waited for, not checked a second time.
            var late = !services.IsDone;
            await services.Completion;
            if (late || services.HasFailed)
                vm.Status = "Services · " + services.Status;
        }
        finally
        {
            services.Dispose();
        }
    }

    /// <summary>True once the chat was built (a login after /logout builds it again).</summary>
    private bool _started;

    /// <summary>
    /// The chat itself, once somebody is logged in: its view model, its interface, its windows, what the command line
    /// asks. After /logout it is built again for whoever logs in, as at the start but for what belongs to the start of
    /// the application: what the command line asks to do (--command, --prompt, --voice, --themes…) and the intro.
    /// </summary>
    private void Start()
    {
        var first = !_started;
        _started = true;
        AvaloniaBridge.Initialize();
        var settings = ChatSettings.Load();
        if (Argument("--avatar=") is { } avatar && Enum.TryParse<SceneAnimation>(avatar, true, out var animation))
            settings.Animation = animation;
        _settings = settings;
        _vm = new MainViewModel(settings);
        if (settings.KeepAwake)
            SleepGuard.Start();
        // The output and the microphone chosen with /audiosettings.
        AudioSettings.Restore();

        // In the theme chosen last (--theme=kaonomi on the command line for a test), set before the login.
        BuildUi();
        // A viewer looks: its buttons that save, delete or publish do nothing.
        AccessControl.Hook(GetTree());
        WireTools();
        // The alarms of /alarm ring even when its window is closed (built once: a new theme keeps it).
        AddChild(new AlarmRinger(_vm.Say, () => _vm.VoiceLanguage,
            enterFullScreen: () =>
            {
                // Put on by the alarm: taken back without a login at its end.
                _chat.EnterVoiceMode(byCode: true);
                // The windows small at the bottom: the 3D animation in view, not a window in the middle.
                GetTree().CreateTimer(0.4).Timeout += () =>
                {
                    if (_deck.ShowsOnStage && !_deck.IsShelved)
                        _deck.ToggleShelf();
                };
            },
            leaveFullScreen: () => _chat.LeaveVoiceMode()));
        // /theme:kaonomi or /theme:eve typed in the chat changes the theme too (asked once: it outlives a /logout).
        if (first)
            KAONOMI.Chat.Themes.AppTheme.Applied += kind => Bind.OnMain(() => ApplyTheme(Eve.Eve.Find(kind.ToString())));

        _vm.RefreshModelsCommand.Execute(null);
        _ = _vm.CheckServerAsync();
        _vm.RefreshTickerCommand.Execute(null);
        ActivityLog.Info("App", "Godot chat (EVE) started");
        if (UserSession.Current is { } logged)
            _vm.Status = $"Logged in as {logged.Login} ({UserSession.Name(logged.Role)})";
        // The windows open at the last close (in front or at the back) come back, once the desktop has its size.
        GetTree().CreateTimer(0.3).Timeout += RestoreDesktop;

        // --vision-source=<video>: the vision windows read this video instead of the camera (a test without the camera).
        if (Argument("--vision-source=") is { } video)
            KAONOMI.Chat.Services.VisionApps.TestSource = video.Trim('"');
        // --audio-source=<wav>: the microphone windows (/recorder, the voice of /vision) hear this file instead.
        if (Argument("--audio-source=") is { } sound)
            KAONOMI.Chat.Services.AudioRecorder.TestSource = sound.Trim('"');
        // The microphone chosen in /recorder is kept, as the one of /audiosettings.
        if (first)
            KAONOMI.Chat.Services.AudioDevices.InputChanged += () => Eve.Preferences.AudioInput = KAONOMI.Chat.Services.AudioDevices.Input;
        // LIVE CHAT off at the start (ticked in the chat to listen); --live-chat: on from the start (--no-live-chat, the
        // option of the tests, is still taken: it changes nothing now).
        if (OS.GetCmdlineUserArgs().Contains("--live-chat") && !OS.GetCmdlineUserArgs().Contains("--no-live-chat"))
            _vm.LiveChat = true;
        // --command=/search;/json runs those commands one after the other (once: not at a login after /logout).
        if (Argument("--command=") is { } commands)
        {
            if (first)
                foreach (var command in commands.Trim('"').Split(';', StringSplitOptions.RemoveEmptyEntries))
                    CallDeferred(MethodName.RunStartCommand, command.Trim());
        }
        // Else startCommands of config.yml (/vision…).
        else
        {
            foreach (var command in _settings.StartCommands)
                CallDeferred(MethodName.RunStartCommand, command);
            // A webcam on this machine: /vision opens (once the windows of the last close are back, unless it is among
            // them or in startCommands); --no-vision: not.
            if (first && !OS.GetCmdlineUserArgs().Contains("--no-vision")
                && !_settings.StartCommands.Any(c => c.StartsWith("/vision", StringComparison.OrdinalIgnoreCase)))
                OpenVisionWhenCamera();
        }
        // --back-after=2: sends the window in front to the back after so many seconds (to see the desktop in 3D).
        if (first && Argument("--back-after=") is { } back && double.TryParse(back, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            GetTree().CreateTimer(seconds).Timeout += () =>
            {
                if (IsInstanceValid(_deck))
                    _deck.SendFrontToBack();
            };
        // --open-url=https://…: opens that link in the browser of the chat.
        if (first && Argument("--open-url=") is { } url)
            GetTree().CreateTimer(1).Timeout += () => Eve.Links.Open(url.Trim('"'));
        if (first && Argument("--prompt=") is { } prompt)
            CallDeferred(MethodName.RunStartCommand, prompt.Trim('"'));
        _chat.CallDeferred(ChatPanel.MethodName.FocusInput);
        // --window-tree[=vision]: opens the tree of the windows (under ›_ in the bar), with that filter, once the windows
        // of the start are open (a window that opens takes the focus, which closes the tree).
        if (first && OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--window-tree", StringComparison.Ordinal)) is { } tree)
            GetTree().CreateTimer(6).Timeout += () =>
            {
                if (IsInstanceValid(_neocom))
                    _neocom.ShowWindowTree(tree.Contains('=') ? tree[(tree.IndexOf('=') + 1)..].Trim('"') : "");
            };
        // --themes: opens the screen of the themes.
        if (first && OS.GetCmdlineUserArgs().Contains("--themes"))
            GetTree().CreateTimer(1).Timeout += OpenThemes;
        // The first start (until DONE on that screen), or --setup: the dependencies of the chat checked, with their installation.
        if (!Preferences.SetupDone || first && OS.GetCmdlineUserArgs().Contains("--setup"))
            GetTree().CreateTimer(1).Timeout += OpenSetup;
        // --voice: starts in the voice mode (the animation alone, the microphone).
        if (first && OS.GetCmdlineUserArgs().Contains("--voice"))
            GetTree().CreateTimer(0.5).Timeout += () =>
            {
                if (IsInstanceValid(_chat))
                    _chat.EnterVoiceMode();
            };
        // The intro (the 3D animation that comes close and says "Jarvis is a douche") over everything; --no-intro skips it.
        // The first opening of the day (in this chat or the Avalonia one): the tasks of the days gone are marked deleted
        // (but those put off to today), then the popup to plan the tasks of the day, once the intro is over. --plan-day
        // shows the popup anyway (a test: the day is not marked, nothing is deleted); a chat started for a test or a
        // screenshot (--no-restore) does neither. A login after /logout comes straight to the chat, without the intro.
        var args = OS.GetCmdlineUserArgs();
        var firstToday = !args.Contains("--no-restore") && DayTodoStore.FirstTimeToday();
        if (firstToday)
            DayTodoStore.StartDay();
        var planDay = firstToday || first && args.Contains("--plan-day");
        if (first && !args.Contains("--no-intro"))
        {
            var intro = new StartIntro(settings);
            intro.Finished += _chat.FocusInput;
            // /process once the chat is in view, under the popup of the day.
            intro.Finished += AfterServices;
            if (planDay)
                intro.Finished += PlanTheDay;
            AddChild(intro);
        }
        else
        {
            // After the windows of the last close (0.3 s): /process in front of them.
            GetTree().CreateTimer(1).Timeout += AfterServices;
            if (planDay)
                GetTree().CreateTimer(1.5).Timeout += PlanTheDay;
        }
    }

    /// <summary>
    /// What the command line asks to do by itself, counted from the start of the application (so before the login as
    /// after it): clicks, a text typed, keys pressed, a screenshot.
    /// </summary>
    private void ScheduleAutomation()
    {
        // --click=640,400[;700,300…] [--click-after=10]: clicks of the mouse there (in the pixels of the window, as
        // --screenshot saves them), the first after so many seconds (6 by default), the next ones 0.7 s apart; a click
        // written 640,400@12.5 comes at that second instead (a click after some typing): what clicks open or do, checked
        // in a screenshot.
        if (Argument("--click=") is { } clicks)
        {
            var clickAfter = Argument("--click-after=") is { } wait && double.TryParse(wait, System.Globalization.CultureInfo.InvariantCulture, out var delay) ? delay : 6;
            var points = clicks.Trim('"').Split(';', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < points.Length; i++)
            {
                var when = clickAfter + i * 0.7;
                var point = points[i];
                // r:640,400 is a click of the right button (a context menu).
                var button = MouseButton.Left;
                if (point.StartsWith("r:", StringComparison.Ordinal))
                {
                    button = MouseButton.Right;
                    point = point[2..];
                }
                // s:640,400 is a click with Shift held (a node of the paths of /graphbase).
                var shift = point.StartsWith("s:", StringComparison.Ordinal);
                if (shift)
                    point = point[2..];
                if (point.IndexOf('@') is var sign and > 0)
                {
                    if (double.TryParse(point[(sign + 1)..], System.Globalization.CultureInfo.InvariantCulture, out var second))
                        when = second;
                    point = point[..sign];
                }
                if (point.Split(',') is not [var clickX, var clickY]
                    || !float.TryParse(clickX, System.Globalization.CultureInfo.InvariantCulture, out var atX)
                    || !float.TryParse(clickY, System.Globalization.CultureInfo.InvariantCulture, out var atY))
                    continue;
                var at = new Vector2(atX, atY);
                GetTree().CreateTimer(when).Timeout += () =>
                {
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ShiftPressed = shift });
                };
                GetTree().CreateTimer(when + 0.08).Timeout += () =>
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at, ShiftPressed = shift });
            }
        }
        // --drag=640,400>900,400@12[;…]: the mouse pressed at the first point at that second (6 by default), moved to
        // the second one and released there 0.4 s later: some text selected in a screenshot test (a selection is made, from
        // the point pressed, but it does not end exactly where the drag ends: enough to try what reads a selection).
        if (Argument("--drag=") is { } drags)
        {
            foreach (var drag in drags.Trim('"').Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var when = 6.0;
                var path = drag;
                if (path.IndexOf('@') is var sign and > 0)
                {
                    double.TryParse(path[(sign + 1)..], System.Globalization.CultureInfo.InvariantCulture, out when);
                    path = path[..sign];
                }
                Vector2? Point(string text) => text.Split(',') is [var px, var py]
                    && float.TryParse(px, System.Globalization.CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(py, System.Globalization.CultureInfo.InvariantCulture, out var y) ? new Vector2(x, y) : null;
                if (path.Split('>') is not [var first, var second] || Point(first) is not { } from || Point(second) is not { } to)
                    continue;
                GetTree().CreateTimer(when).Timeout += () =>
                {
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from, ButtonMask = MouseButtonMask.Left });
                };
                for (var step = 1; step <= 8; step++)
                {
                    var at = from.Lerp(to, step / 8f);
                    GetTree().CreateTimer(when + 0.04 * step).Timeout += () =>
                        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left, Relative = (to - from) / 8f });
                }
                GetTree().CreateTimer(when + 0.4).Timeout += () =>
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
            }
        }

        // --type="some text" [--type-after=7]: the text typed after so many seconds, a character every 40 ms (in the field
        // that has the keyboard, clicked first with --click); \n in it is the Enter key (a new line, a blank line).
        if (Argument("--type=") is { } typed)
        {
            var typeAfter = Argument("--type-after=") is { } wait && double.TryParse(wait, System.Globalization.CultureInfo.InvariantCulture, out var delay) ? delay : 7;
            var text = typed.Trim('"').Replace("\\n", "\n");
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                GetTree().CreateTimer(typeAfter + i * 0.04).Timeout += () =>
                {
                    if (c == '\n')
                    {
                        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
                        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
                        return;
                    }
                    Input.ParseInputEvent(new InputEventKey { Unicode = c, Pressed = true });
                    Input.ParseInputEvent(new InputEventKey { Unicode = c, Pressed = false });
                };
            }
        }
        // --key=Right[,Right…] [--key-after=7]: these keys pressed one after the other (a quarter of a second apart) after
        // so many seconds: what a key does, checked in a screenshot.
        if (Argument("--key=") is { } keys)
        {
            var keyAfter = Argument("--key-after=") is { } wait && double.TryParse(wait, System.Globalization.CultureInfo.InvariantCulture, out var delay) ? delay : 7;
            var names = keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < names.Length; i++)
            {
                if (!Enum.TryParse<Key>(names[i], true, out var key))
                    continue;
                // Pressed, then released a little later (both in the same frame, the press is lost).
                GetTree().CreateTimer(keyAfter + i * 0.25).Timeout += () =>
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                GetTree().CreateTimer(keyAfter + i * 0.25 + 0.08).Timeout += () =>
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
            }
        }
        // --screenshot=<file.png> [--screenshot-after=8] [--quit]: what the chat shows, saved after so many seconds
        // (the design checked without a screen capture of the system); --quit closes the chat once it is saved.
        if (Argument("--screenshot=") is { } shot)
        {
            var after = Argument("--screenshot-after=") is { } wait && double.TryParse(wait, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 8;
            GetTree().CreateTimer(after).Timeout += () =>
            {
                var image = GetViewport().GetTexture().GetImage();
                var saved = image.SavePng(shot.Trim('"'));
                ActivityLog.Info("App", $"Screenshot {shot}: {saved}");
                if (OS.GetCmdlineUserArgs().Contains("--quit"))
                    GetTree().Quit();
            };
        }
    }

    /// <summary>The popup of the morning: the tasks of the day to type (those of the days before are marked deleted).</summary>
    private void PlanTheDay()
    {
        if (_shell is null)
            return;
        Dialogs.Show("Plan your day", new Tools.Daily.TodoPage(new TodoWindowViewModel(planning: true)) { CustomMinimumSize = new Vector2(720, 460) }, new Vector2I(780, 560));
    }

    /// <summary>
    /// Runs a command or sends a prompt given on the command line; a prompt waits (20 s at most) for the server and the
    /// models, checked just after the start.
    /// </summary>
    private async void RunStartCommand(string text)
    {
        var vm = _vm;
        // Until /logout: the command of a start is not run in the chat of whoever logs in next.
        for (var i = 0; i < 40 && ReferenceEquals(vm, _vm); i++)
        {
            vm.Input = text;
            if (vm.SendCommand.CanExecute(null))
            {
                vm.RunCommand(text);
                return;
            }
            await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        }
    }

    /// <summary>The value of <paramref name="name"/> on the command line (after <c>--</c>), or null.</summary>
    private static string? Argument(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name, StringComparison.Ordinal))?[name.Length..];

    /// <summary>The whole interface, in the theme in use: background, bar of commands, desktop, chat.</summary>
    private void BuildUi()
    {
        Theme = Eve.Eve.Shared;
        _ui = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _ui.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_ui);
        BuildBackground();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ui.AddChild(row);
        _neocom = new Neocom();
        // The bar of the tools floats over the desktop too: room around it, rounded corners, a shadow under it.
        row.AddChild(Eve.Eve.Floating(_neocom, desktopOnLeft: false));
        _deck = new WindowDeck
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            WallpaperRect = _wallpaper,
            // The windows not used for windowIdleCloseSeconds of config.yml close by themselves.
            // --idle-close=20: another delay (a test of the windows that stay open).
            IdleCloseSeconds = Argument("--idle-close=") is { } idle && int.TryParse(idle, out var seconds) ? seconds : _settings.WindowIdleCloseSeconds
        };
        row.AddChild(_deck);
        _chat = new ChatPanel(_vm);
        // The chat floats over the desktop: room around it, rounded corners, a shadow under it.
        row.AddChild(Eve.Eve.Floating(_chat, desktopOnLeft: true));

        _shell = new Shell(_vm, _deck, this);
        _neocom.CommandRequested += command =>
        {
            if (command == Neocom.Themes.Command)
                OpenThemes();
            else if (command == Neocom.AllBack.Command)
                _deck.SendAllToBack();
            else if (command == Neocom.AllFront.Command)
                _deck.BringAllToFront();
            else
                _vm.RunCommand(command);
        };
        // The tree of the windows (under ›_ in the bar): the windows open now first; a click brings one to the front.
        _neocom.OpenWindows = () => _deck.Windows.Select(w => (w.Key, w.Title, w.Icon)).ToList();
        _neocom.WindowRequested += key => _shell.TryBringBack(key);
        _deck.FrontChanged += _ => RefreshNeocom();
        // The menu of the empty desktop: the picture library, /think, the tools opened last.
        _deck.CommandRequested += command => _vm.RunCommand(command);
        // ❐ in the header of a window: the same tool in a second window.
        _deck.CloneRequested += window =>
        {
            var clone = _shell.Clone(window, tool => _vm.OpenTool(tool, false));
            _vm.Status = clone is null ? $"{window.Title} cannot be cloned" : $"{window.Title} cloned";
        };
        // 📝 in the header of a window: a note about it, with its data.
        _deck.NoteRequested += window => Tools.Data.NoteView.Open(_shell, window.Title, window.ViewModel);
        // 📁+ in the header of a window: the window, its data and a picture of it, into a folder.
        _deck.FolderRequested += window => Tools.Data.AddToFolderView.Open(_shell, window.Title, window.ViewModel, window.Picture());
    }

    /// <summary>/think: the black screen of the last hour over everything (once at a time).</summary>
    private void OpenThink(string? branch)
    {
        if (GetChildren().OfType<ThinkScreen>().Any())
            return;
        var think = new ThinkScreen(_settings, _vm.Assistant, branch);
        think.Finished += _chat.FocusInput;
        AddChild(think);
    }

    /// <summary>The screen of the dependencies (the first start, --setup, /setup), in a window of the desktop.</summary>
    private void OpenSetup()
    {
        // Asked by a timer of the start: nothing after a /logout that came first.
        if (_shell is null)
            return;
        if (_shell.TryBringBack(SetupScreen.Key))
            return;
        Control content = null!;
        content = SetupScreen.Build(_settings, () => _shell.Close(content));
        _shell.OpenControl(SetupScreen.Key, "Setup", SetupScreen.Icon, content, new Vector2(900, 640));
    }

    /// <summary>/users: the users of the chat and their levels, in a window of the desktop (the administrators only).</summary>
    private void OpenUsers()
    {
        // Asked again here: the window never opens for somebody else, whatever asked for it.
        if (!UserSession.IsAdministrator)
        {
            _vm.Status = "/users is for the administrators only";
            return;
        }
        if (!_shell.TryBringBack(UsersScreen.Key))
            _shell.OpenControl(UsersScreen.Key, "Users", UsersScreen.Icon, UsersScreen.Build(), new Vector2(820, 560));
    }

    /// <summary>/user: the profile of the user logged in (names, contact, profile, hashtags, photo), in a window of the desktop.</summary>
    private void OpenProfile()
    {
        ProfileScreen.Open(_shell);
    }

    /// <summary>/password: the user logged in changes its own password, in a window of the desktop.</summary>
    private void OpenPassword()
    {
        if (_shell.TryBringBack(UsersScreen.PasswordKey))
            return;
        Control content = null!;
        content = UsersScreen.BuildPassword(() => _shell.Close(content));
        _shell.OpenControl(UsersScreen.PasswordKey, "Password", UsersScreen.PasswordIcon, content, new Vector2(460, 380)).Resizable = false;
    }

    /// <summary>The screen of the themes, in a window of the desktop.</summary>
    private void OpenThemes()
    {
        if (_shell is null)
            return;
        if (_shell.TryBringBack(ThemeScreen.Key))
            return;
        // Three themes side by side, a window of a fixed size.
        var window = _shell.OpenControl(ThemeScreen.Key, "Themes", Neocom.Themes.Icon, ThemeScreen.Build(ApplyTheme), ThemeScreen.WindowSize);
        window.Resizable = false;
        // The keyboard goes to the themes (← →, see _Input), not to the field that had it.
        GetViewport().GuiReleaseFocus();
    }

    /// <summary>
    /// Builds the interface again in <paramref name="palette"/> (kept for the next start): the windows open come back
    /// at their place, in front or at the back; the conversation stays.
    /// </summary>
    private async void ApplyTheme(Palette palette)
    {
        if (palette == Eve.Eve.Current)
            return;
        // The tool windows are opened again: what was typed in them and not saved would be lost.
        if (_deck.Windows.Any(w => w.Key != ThemeScreen.Key)
            && !await Dialogs.Confirm("Theme", $"The interface is built again in the {palette.Name} theme: the windows open come back at their place, but what is typed in them and not saved is lost."
                                               + (Wallpaper.Current is null ? string.Empty : " The wallpaper is removed: the theme shows its own background."), "Apply"))
            return;
        // The click that asked is still being handled by the interface: it is built again at the next frame.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(_ui) || palette == Eve.Eve.Current)
            return;
        Preferences.Theme = palette.Name;
        // A wallpaper set by hand would hide the background of the new theme: it goes with the old theme.
        if (Wallpaper.Current is not null)
            Wallpaper.Clear();
        var themesOpen = _deck.Find(ThemeScreen.Key) is not null;
        DeskLayout.Save(GetWindow(), _deck);
        _deck.CloseAll();
        _ui.QueueFree();
        RemoveChild(_ui);
        Eve.Eve.Use(palette);
        BuildUi();
        ActivityLog.Info("App", $"Theme of the Godot chat: {palette.Name}");
        GetTree().CreateTimer(0.2).Timeout += () =>
        {
            RestoreDesktop();
            if (themesOpen)
                OpenThemes();
        };
    }

    /// <summary>
    /// /vision opened at the start when this machine has a webcam (asked away from the main thread: system_profiler
    /// takes a moment), 2 s after the start so that the windows of the last close are back (it is not opened twice).
    /// </summary>
    private void OpenVisionWhenCamera()
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            var camera = await KAONOMI.Chat.Services.VisionApps.HasCameraAsync();
            ActivityLog.Info("Vision", camera ? "A webcam is there: /vision opens" : "No webcam: /vision is not opened at the start");
            if (!camera)
                return;
            await System.Threading.Tasks.Task.Delay(2000);
            Bind.OnMain(() =>
            {
                if (IsInstanceValid(this) && IsInstanceValid(_deck) && _deck.Find("/vision") is null)
                    RunStartCommand("/vision");
            });
        });
    }

    private void BuildBackground()
    {
        // The picture of the theme, when it has one (the shader of the theme is then an overlay: its rain, its lights).
        if (Eve.Eve.Current.PicturePath is { } path && System.IO.File.Exists(path))
        {
            var image = Image.LoadFromFile(path);
            if (image is not null && !image.IsEmpty())
            {
                var picture = new TextureRect
                {
                    Texture = ImageTexture.CreateFromImage(image),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                _ui.AddChild(picture);
                _themeBackground.Add(picture);
            }
            else
                GD.PushWarning($"The picture of the theme {path} could not be read");
        }
        _space = new ShaderMaterial { Shader = GD.Load<Shader>(Eve.Eve.Current.Background) };
        _desk = Vector2.Zero;
        // The picture the shader draws itself (MarieKilleuse in her hearts, the rooftop of Ghost), at the width of the
        // theme (small when it is drawn small), with its mipmaps (the shader reads it blurred too: the glow of Ghost).
        if (Eve.Eve.Current.StampPath is { } stampPath && System.IO.File.Exists(stampPath))
        {
            var stamp = Image.LoadFromFile(stampPath);
            if (stamp is not null && !stamp.IsEmpty())
            {
                var width = Math.Min(Eve.Eve.Current.StampWidth, stamp.GetWidth());
                if (width != stamp.GetWidth())
                    stamp.Resize(width, width * stamp.GetHeight() / Math.Max(1, stamp.GetWidth()), Image.Interpolation.Lanczos);
                stamp.GenerateMipmaps();
                _space.SetShaderParameter("stamp", ImageTexture.CreateFromImage(stamp));
            }
            else
                GD.PushWarning($"The picture of the theme {stampPath} could not be read");
        }
        var background = new ColorRect { Material = _space, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ui.AddChild(background);
        _themeBackground.Add(background);

        // The wallpaper on the whole surface of the application, under the bar of commands, the desktop and the chat
        // (the desktop puts the picture chosen in it, and hides it on the stage).
        _wallpaper = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _wallpaper.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ui.AddChild(_wallpaper);
    }

    /// <summary>Every request of the chat to open a tool opens its window on the desktop.</summary>
    private void WireTools()
    {
        // The key of the window: the tool's own, or the one of a clone being made (/wiki#2).
        void Open(object viewModel, string key) => Bind.OnMain(() => _shell.Open(viewModel, _shell.KeyFor(key)));
        _vm.CodeWindowRequested += vm => Bind.OnMain(() => _shell.Open(vm));
        _vm.SearchWindowRequested += (vm, _) => Open(vm, "/search");
        _vm.JsonClientWindowRequested += (vm, _) => Open(vm, "/json");
        _vm.SymbolicPatternWindowRequested += (vm, _) => Open(vm, "/symbol");
        _vm.CacheWindowRequested += (vm, _) => Open(vm, "/cache");
        _vm.OntologyWindowRequested += (vm, _) => Open(vm, "/ontology");
        _vm.JournalWindowRequested += (vm, _) => Open(vm, "/journal");
        _vm.CompletionWindowRequested += (vm, _) => Open(vm, "/completion");
        _vm.FooocusWindowRequested += (vm, _) => Open(vm, "/fooocus");
        _vm.YoutubeWindowRequested += (vm, _) => Open(vm, "/youtube");
        _vm.MusicWindowRequested += (vm, _) => Open(vm, "/music");
        _vm.RecorderWindowRequested += (vm, _) => Open(vm, "/recorder");
        _vm.ProjectWindowRequested += (vm, _) => Open(vm, "/project");
        _vm.PictureWindowRequested += (vm, _) => Open(vm, "/pictures");
        _vm.CalculatorWindowRequested += (vm, _) => Open(vm, "/work");
        _vm.IdeaWindowRequested += (vm, _) => Open(vm, "/idea");
        _vm.TokenDashboardWindowRequested += (vm, _) => Open(vm, "/token");
        _vm.ConfigWindowRequested += (vm, _) => Open(vm, "/config");
        _vm.IntelWindowRequested += (vm, _) => Open(vm, "/intel");
        _vm.CollectionPlanWindowRequested += (vm, _) => Open(vm, "/collectionplan");
        _vm.Game1WindowRequested += (vm, _) => Open(vm, "/game:solitaire");
        _vm.FractalWindowRequested += (vm, _) => Open(vm, "/fractal");
        _vm.ChessWindowRequested += (vm, _) => Open(vm, "/game:chess");
        _vm.GoWindowRequested += (vm, _) => Open(vm, "/game:go");
        _vm.ImmuneWindowRequested += (vm, _) => Open(vm, "/immune");
        _vm.PromptsWindowRequested += (vm, _) => Open(vm, "/prompts");
        _vm.EmotionWindowRequested += (vm, _) => Open(vm, "/emotion");
        _vm.ModesWindowRequested += (vm, _) => Open(vm, "/models");
        _vm.VideoWindowRequested += (vm, _) => Open(vm, "/video");
        _vm.VisionMoveWindowRequested += (vm, _) => Open(vm, "/vision:move");
        _vm.VisionFaceWindowRequested += (vm, _) => Open(vm, "/vision");
        _vm.AlarmWindowRequested += (vm, _) => Open(vm, "/alarm");
        _vm.CalendarWindowRequested += (vm, _) => Open(vm, "/calendar");
        _vm.IqWindowRequested += (vm, _) => Open(vm, "/iq");
        _vm.PedagoWindowRequested += (vm, _) => Open(vm, "/pedago:test");
        _vm.MessageWindowRequested += (vm, _) => Open(vm, "/message");
        _vm.ActivityWindowRequested += (vm, _) => Open(vm, ActivityJournal.Command);
        _vm.FoldersWindowRequested += (vm, _) => Open(vm, "/folders");
        _vm.KanbanWindowRequested += (vm, _) => Open(vm, "/kanban");
        _vm.RemoteWindowRequested += (vm, _) => Open(vm, "/remote");
        _vm.PresetWindowRequested += (vm, _) => Open(vm, "/preset");
        _vm.ProcessWindowRequested += (vm, _) => Open(vm, "/process");
        _vm.GatewayWindowRequested += (vm, _) => Open(vm, "/gateway");
        _vm.WikiWindowRequested += (vm, _) => Open(vm, "/wiki");
        _vm.InterviewWindowRequested += (vm, _) => Open(vm, "/interview");
        _vm.CvWindowRequested += (vm, _) => Open(vm, "/cv");
        _vm.BpmnWindowRequested += (vm, _) => Open(vm, "/bpmnai");
        _vm.ArchiWindowRequested += (vm, _) => Open(vm, "/archi");
        _vm.GraphbaseWindowRequested += (vm, _) => Open(vm, "/graphbase");
        // /entity:<name>: one window per entity.
        _vm.EntityAnalysisWindowRequested += (vm, _) => Bind.OnMain(() => _shell.Open(vm, _shell.KeyFor(vm.Key), $"Entity · {vm.Query}"));
        // /dissimination fills the desktop.
        _vm.DisseminationWindowRequested += (vm, _) => Bind.OnMain(() =>
        {
            if (_shell.Open(vm, _shell.KeyFor("/dissimination")) is { IsMaximized: false } window)
                _deck.Maximize(window);
        });
        _vm.SupportWindowRequested += (vm, _) => Open(vm, "/support");
        _vm.TodoWindowRequested += (vm, _) => Open(vm, "/todo");
        _vm.VaultWindowRequested += (vm, _) => Open(vm, "/obs");
        _vm.PianoWindowRequested += (vm, _) => Open(vm, "/piano");
        _vm.LooperWindowRequested += (vm, _) => Open(vm, "/looper");
        _vm.SynthWindowRequested += (vm, _) => Open(vm, "/synth");
        _vm.SamplerWindowRequested += (vm, _) => Open(vm, "/sampler");
        _vm.PaintWindowRequested += (vm, _) => Open(vm, "/paint");
        _vm.InfraWindowRequested += (vm, _) => Open(vm, "/infra");
        _vm.MindmapWindowRequested += (vm, _) => Open(vm, "/mindmap");
        _vm.GhostWriterWindowRequested += (vm, _) => Open(vm, "/write");
        _vm.ConsoleWindowRequested += (vm, _) => Open(vm, "/cmd");
        _vm.LogDashboardWindowRequested += (vm, _) => Open(vm, "/log");
        _vm.PerformanceDashboardWindowRequested += (vm, _) => Open(vm, "/performance");
        _vm.SystemWindowRequested += (vm, _) => Open(vm, "/system");
        _vm.RelationGraphRequested += (vm, _) => Open(vm, "/relation");
        // /fullchat: the chat as in its full screen (the 3D animation and the microphone), in a window of the desktop.
        _vm.FullChatRequested += detached => Bind.OnMain(() =>
        {
            if (_shell.Deck.Find(FullChatWindow.Key) is { } open)
            {
                if (detached)
                    DetachFullChat(open);
                else
                    _shell.Deck.BringToFront(open);
                return;
            }
            var window = OpenFullChat(new FullChatWindow(_vm));
            if (detached && window is not null)
                Callable.From(() => DetachFullChat(window)).CallDeferred();
        });
        _vm.SetupRequested += () => Bind.OnMain(OpenSetup);
        _vm.UsersRequested += () => Bind.OnMain(OpenUsers);
        _vm.ProfileRequested += () => Bind.OnMain(OpenProfile);
        _vm.PasswordRequested += () => Bind.OnMain(OpenPassword);
        _vm.LogoutRequested += () => Bind.OnMain(Logout);
        // /orleans: the dashboard of Orleans in the browser of the chat (a window of the desktop, 1200×1024).
        _vm.WebPageRequested += url => Bind.OnMain(() => WebWindow.Open(url));
        _vm.ThinkRequested += branch => Bind.OnMain(() => OpenThink(branch));
        _vm.MosaicRequested += () => Bind.OnMain(() =>
        {
            if (!_deck.Mosaic())
                _vm.Status = _deck.Windows.Count == 0 ? "/mosaic: no window open" : "/mosaic: not while the windows are on the stage (voice mode)";
        });
        _vm.WindowsRequested += () => Bind.OnMain(() =>
        {
            if (!_deck.ToggleCarousel())
                _vm.Status = _deck.Windows.Count == 0 ? "/windows: no window open" : "/windows: not while the windows are on the stage (voice mode)";
        });
        _vm.AudioSettingsRequested += () => Bind.OnMain(() => AudioSettings.Open(this));
        _vm.CloseAllRequested += () => Bind.OnMain(() => _ = _shell.Deck.RequestCloseAllAsync());
        _vm.ExitCountdown += remaining => Bind.OnMain(() => ShowCountdown(remaining));
        _vm.ExitRequested += () => Bind.OnMain(Quit);
    }

    /// <summary>The countdown of /bye shown over everything (null: cancelled, it goes away).</summary>
    private ExitCountdown? _countdown;

    private void ShowCountdown(int? remaining)
    {
        if (remaining is not { } seconds)
        {
            if (_countdown is not null && IsInstanceValid(_countdown))
                _countdown.QueueFree();
            _countdown = null;
            return;
        }
        if (_countdown is null || !IsInstanceValid(_countdown))
        {
            _countdown = new ExitCountdown(() => Bind.Run(_vm.SendCancelCommand));
            AddChild(_countdown);
        }
        _countdown.SetRemaining(seconds);
    }

    /// <summary>Lights the icons of the tools whose window is open.</summary>
    private void RefreshNeocom()
    {
        var open = _deck.Windows.Select(w => w.Key).ToHashSet();
        foreach (var (command, _, _) in Neocom.Tools.Append(Neocom.Settings).Append(Neocom.Themes))
            _neocom.SetOpen(command, open.Contains(command));
    }

    public override void _Process(double delta)
    {
        // Once Avalonia is started, what waits on the main thread goes on through its dispatcher: pumped without a chat
        // too (after /logout, the check of the services of the next login waits on it); nothing before it is started.
        AvaloniaBridge.Pump();
        // Restoring the main window in _Ready can draw a frame before the interface is built; none after /logout.
        if (_space is null)
            return;
        // The background of the theme is not drawn under a wallpaper (its full-screen shader cost every frame for nothing).
        var covered = IsInstanceValid(_wallpaper) && _wallpaper.Visible && _wallpaper.Texture is not null;
        foreach (var item in _themeBackground)
            if (IsInstanceValid(item) && item.Visible == covered)
                item.Visible = !covered;
        // Where the desk is (between the bar of commands and the chat): a background may lay its picture on it.
        var screen = GetViewport().GetVisibleRect().Size;
        if (screen.X > 0 && IsInstanceValid(_deck))
        {
            var rect = _deck.GetGlobalRect();
            var desk = new Vector2(rect.Position.X / screen.X, rect.End.X / screen.X);
            if (desk.DistanceTo(_desk) > 0.0005f)
            {
                _desk = desk;
                _space.SetShaderParameter("desk", desk);
            }
        }
        var energy = (float)Math.Round(Math.Clamp(_vm.SpeechLevel, 0, 1), 2);
        if (Math.Abs(energy - _energy) > 0.001f)
        {
            _energy = energy;
            _space.SetShaderParameter("energy", energy);
        }
        _serverCheck += delta;
        if (_serverCheck > 5)
        {
            _serverCheck = 0;
            _ = _vm.CheckServerAsync();
        }
        // The windows saved every 20 s too: a chat stopped without closing (a signal, a crash) keeps them.
        _layoutSave -= delta;
        // Not while the voice mode covers the screen: the window then is not the one of the usual desktop.
        if (_layoutSave < 0 && !_quitting && !KioskLock.IsOn)
        {
            _layoutSave = 20;
            DeskLayout.Save(GetWindow(), _deck);
        }
        _tickerRefresh -= delta;
        if (_tickerRefresh < 0)
        {
            _tickerRefresh = 60;
            _vm.RefreshTickerCommand.Execute(null);
        }
    }

    /// <summary>
    /// The messages of the MIDI keyboards (an external keyboard plugged in): Godot gives them to the main window only,
    /// they are passed on to the windows that listen (the piano).
    /// </summary>
    public static event Action<InputEventMidi>? MidiReceived;

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMidi midi)
        {
            MidiReceived?.Invoke(midi);
            return;
        }
        if (_deck is null)
            return;
        // A click outside the window in front takes the keyboard away from it, and the other way round: the chat and
        // the window in front never both receive the keys.
        if (e is InputEventMouseButton { Pressed: true } click)
        {
            if (_deck.FrontAt(click.GlobalPosition) is { } clicked)
            {
                // A click in a window in front makes it the active one (above the others) and gives it the keyboard.
                _deck.Activate(clicked);
                GetViewport().GuiReleaseFocus();
                foreach (var other in _deck.Windows.Where(w => w.IsFront && w != clicked))
                    other.Content.GetViewport()?.GuiReleaseFocus();
            }
            else
                foreach (var window in _deck.Windows.Where(w => w.IsFront))
                    window.Content.GetViewport()?.GuiReleaseFocus();
        }
        if (e is InputEventKey { Pressed: true } key)
        {
            if (key.Keycode == Key.Escape && PromptCollectionRunner.Stop())
                GetViewport().SetInputAsHandled();
            else if (key.Keycode == Key.Tab && key.CtrlPressed && !KioskLock.IsOn)
            {
                _deck.Cycle();
                GetViewport().SetInputAsHandled();
            }
            // ← → move along the themes while their window is the active one and no text field has the keyboard (the
            // chat, once clicked, keeps its arrows). Handled here, once per key: the windows of the desktop may receive
            // a key twice.
            else if (key.Keycode is Key.Left or Key.Right && _deck.Front is { Key: ThemeScreen.Key, IsFront: true } themes
                     && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit)
                     && ThemeScreen.Step(themes.Content, key.Keycode == Key.Left ? -1 : 1))
                GetViewport().SetInputAsHandled();
            else if (key.Keycode == Key.M && key.CtrlPressed && !key.Echo)
            {
                ShowPointerTarget();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>The layer of the target of Ctrl+M, above everything (the windows of the desktop live in layers of their own).</summary>
    private CanvasLayer? _targetLayer;
    private PointerTarget? _target;

    /// <summary>Ctrl+M: a target drawn around the pointer for two seconds (again while shown: the two seconds start over).</summary>
    private void ShowPointerTarget()
    {
        if (_target is not null && IsInstanceValid(_target))
        {
            _target.Restart();
            return;
        }
        if (_targetLayer is null || !IsInstanceValid(_targetLayer))
        {
            _targetLayer = new CanvasLayer { Name = "PointerTargetLayer", Layer = 120 };
            AddChild(_targetLayer);
        }
        _target = new PointerTarget();
        _targetLayer.AddChild(_target);
    }

    public override void _Notification(int what)
    {
        // The chat in full screen locked (the voice mode) does not close (Cmd+Q): the voice mode is left first.
        if (what == NotificationWMCloseRequest && !KioskLock.IsOn)
            Quit();
    }

    // The tools are opened directly (not as a typed command, which waits while an answer or /bye is running).
    // The windows saved come back, the clones too (/wiki#2 opens the tool /wiki under that key).
    private void RestoreDesktop()
    {
        if (_deck is null || !IsInstanceValid(_deck))
            return;
        DeskLayout.Restore(_deck, command => _shell.OpenToolAs(command, tool => _vm.OpenTool(tool, false)));
    }

    /// <summary>
    /// The full chat (/fullchat) on the desktop, with ⧉ in its header: the window moves into a window of the system
    /// (to put on another screen); closing that window brings it back here.
    /// </summary>
    private DeckWindow? OpenFullChat(Control content)
    {
        var window = _shell.OpenControl(FullChatWindow.Key, "Chat", "🎙", content, new Vector2(720, 640));
        if (!ReferenceEquals(window.Content, content))
            return null;
        window.Frame.AddHeaderButton("⧉", "Open in a window of its own (of the system: it goes on another screen); closing it brings the chat back here", () =>
            // Deferred: the tooltip of the button (an embedded pop-up) must be gone before the embedding is turned off.
            Callable.From(() => DetachFullChat(window)).CallDeferred());
        return window;
    }

    /// <summary>The full chat of <paramref name="window"/> moved into a window of the system (back here when it is closed).</summary>
    private void DetachFullChat(DeckWindow window)
    {
        if (!IsInstanceValid(window.Content) || _shell.Deck.Find(FullChatWindow.Key) is null)
            return;
        var size = (Vector2I)window.Rect.Size.Max(new Vector2(480, 420));
        _vm.Status = NativeWindows.Detach(_shell, window, "KAONOMI · Chat", size, back => OpenFullChat(back));
    }

    private bool _quitting;

    /// <summary>True while /logout asks about the changes not saved, then closes the chat (once at a time; no quit meanwhile).</summary>
    private bool _loggingOut;

    /// <summary>
    /// /logout: the user leaves, the chat stays open on its login screen. As at a quit, the changes not saved are asked
    /// about first, the windows open are kept for the next login, what speaks stops and the microphone is released;
    /// then everything built for that user goes (the interface, the windows, the conversation with its view model, the
    /// alarms) and the login screen is shown: <see cref="Start"/> builds the chat again for whoever logs in. Not in the
    /// voice mode: it is a lock screen, left with a login of its own.
    /// </summary>
    private async void Logout()
    {
        if (_quitting || _askingToQuit || _loggingOut || _vm is not { } vm || _deck is null)
            return;
        if (KioskLock.IsOn)
        {
            vm.Status = "/logout: not in the voice mode. Leave it first (Esc or a double-click on the animation, then your login)";
            return;
        }
        _loggingOut = true;
        try
        {
            if (_deck.UnsavedWindows() is { Count: > 0 } unsaved)
            {
                var leave = await Dialogs.Confirm("Log out",
                    $"{unsaved.Count} window(s) have changes that are not saved:\n{WindowDeck.Titles(unsaved)}\n\nLogging out loses them.",
                    "Log out without saving", "Cancel");
                if (!leave)
                {
                    vm.Status = "/logout cancelled: changes not saved";
                    return;
                }
                ActivityLog.Info("App", $"Logout without saving {unsaved.Count} window(s)");
            }
            // The command (or the click of the dialog) is still being handled: the interface goes at the next frame.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_quitting || KioskLock.IsOn || !ReferenceEquals(vm, _vm) || !IsInstanceValid(_deck))
                return;
            var who = UserSession.Current?.Login ?? "nobody";
            // As at a quit: the windows open now come back at the next login.
            DeskLayout.Save(GetWindow(), _deck);
            PromptCollectionRunner.Stop();
            // The chat moved to a window of the system comes back to the desktop, and closes with it.
            NativeWindows.CloseAll();
            vm.StopSpeaking();
            vm.SaveSystemPrompt();
            _deck.CloseAll();
            vm.DisposeSpeech();
            SleepGuard.Stop();
            // Everything built for this user: the interface, the dialogs, the alarms, a screen shown over them.
            foreach (var child in GetChildren())
            {
                RemoveChild(child);
                child.QueueFree();
            }
            _themeBackground.Clear();
            _services?.Dispose();
            _services = null;
            _countdown = null;
            _target = null;
            _targetLayer = null;
            _energy = -1;
            // Nothing of the chat is left: _Process, _Input and a quit see that there is no desktop.
            _vm = null!;
            _ui = null!;
            _shell = null!;
            _deck = null!;
            _neocom = null!;
            _chat = null!;
            _wallpaper = null!;
            _space = null!;
            UserSession.SignOut();
            ActivityLog.Info("App", $"Godot chat (EVE): {who} logged out, back to the login screen");
            AskLogin();
        }
        finally
        {
            _loggingOut = false;
        }
    }

    public override void _ExitTree()
    {
        // Leaving without the close of the window (Ctrl+C in the terminal, the end of the process): the windows are kept.
        if (!_quitting && !KioskLock.IsOn && IsInstanceValid(_deck))
            DeskLayout.Save(GetWindow(), _deck);
    }

    /// <summary>True while the question about the changes not saved is shown (a second close waits for it).</summary>
    private bool _askingToQuit;

    private async void Quit()
    {
        // Once: /bye then the close of the window must not save an empty desktop over the windows saved.
        if (_quitting || _askingToQuit || _loggingOut)
            return;
        // Windows with changes not saved (a writing, a page, a diagram…): the user says first whether to lose them.
        if (_deck is not null && IsInstanceValid(_deck) && _deck.UnsavedWindows() is { Count: > 0 } unsaved)
        {
            _askingToQuit = true;
            var quit = await Dialogs.Confirm("Quit KAONOMI",
                $"{unsaved.Count} window(s) have changes that are not saved:\n{WindowDeck.Titles(unsaved)}\n\nQuitting loses them.",
                "Quit without saving", "Cancel");
            _askingToQuit = false;
            if (!quit)
                return;
            ActivityLog.Info("App", $"Quit without saving {unsaved.Count} window(s)");
        }
        _quitting = true;
        // A start that failed before the desktop was built: nothing to save, the window just closes.
        if (_deck is null || _vm is null)
        {
            ActivityLog.Flush(TimeSpan.FromSeconds(2));
            GetTree().Quit();
            return;
        }
        // The windows open now (in front or at the back) come back at the next start.
        DeskLayout.Save(GetWindow(), _deck);
        _vm.StopSpeaking();
        _vm.SaveSystemPrompt();
        _deck.CloseAll();
        _vm.DisposeSpeech();
        SleepGuard.Stop();
        ActivityLog.Info("App", "Godot chat (EVE) closed");
        ActivityLog.Flush(TimeSpan.FromSeconds(2));
        GetTree().Quit();
    }
}
