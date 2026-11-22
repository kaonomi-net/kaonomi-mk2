using System;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The browser of the chat: the links open in a window of the desktop like the others (its header, full size, sent to
/// the back on a card, moved, resized, closed), 1200×1024 when the desktop has the room, with the bar of the other tools
/// at the top (back, forward, reload, the address, the browser of the system) and the page under it.
/// One window for every link: a new link replaces the page and brings the window to the front. The page is drawn by
/// WebKit on macOS, by WebView2 (Edge) on Windows (<see cref="EdgeView"/>); elsewhere, and for the files, the browser of
/// the system.
/// </summary>
public static class WebWindow
{
    private const string Key = "web";

    /// <summary>Opens <paramref name="url"/> in the browser of the chat.</summary>
    public static void Open(string url)
    {
        if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()) || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || Shell.Current is not { } shell)
        {
            OS.ShellOpen(url);
            return;
        }
        if (shell.Deck.Find(Key) is { Content: WebPage page } open)
        {
            page.Load(url);
            shell.Deck.BringToFront(open);
            return;
        }
        page = new WebPage(url);
        page.Window = shell.OpenControl(Key, "Web", "🌐", page, new Vector2(1200, 1024));
    }
}

/// <summary>
/// The page of the browser of the chat: a web view of WebKit (the engine of Safari) laid over the native view of the
/// chat, exactly on this control while its window is flat in front and nothing covers it; hidden otherwise (the window
/// at the back, faded, flying, under another window, the chat locked), when this control shows the title of the page instead.
/// </summary>
public partial class WebPage : Control
{
    // The agent of Safari: the sites give their page for Safari (some refuse an unknown WebKit).
    private const string UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendBool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern bool GetBool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern Rect GetRectRegisters(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static extern void GetRectStret(out Rect rect, IntPtr receiver, IntPtr selector);

    /// <summary>
    /// An NSRect (four doubles, 32 bytes) returned by a selector: in registers on Apple Silicon, through a hidden
    /// pointer (objc_msgSend_stret) on Intel, where the plain objc_msgSend would crash.
    /// </summary>
    private static Rect GetRect(IntPtr receiver, IntPtr selector)
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
        {
            GetRectStret(out var rect, receiver, selector);
            return rect;
        }
        return GetRectRegisters(receiver, selector);
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendRect(IntPtr receiver, IntPtr selector, Rect rect);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr InitWebView(IntPtr receiver, IntPtr selector, Rect frame, IntPtr configuration);

    /// <summary>An NSRect (four doubles).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public double X, Y, Width, Height;
    }

    private static IntPtr Sel(string name) => sel_registerName(name);

    private static IntPtr NsString(string text) => SendString(objc_getClass("NSString"), Sel("stringWithUTF8String:"), text);

    private string _url;
    private IntPtr _view, _host;
    /// <summary>The web view on Windows (null elsewhere, or when WebView2 cannot start).</summary>
    private EdgeView? _edge;
    private bool _shown;
    private Rect _frame;
    private Label _caption = null!;
    private Control _surface = null!;
    private LineEdit _address = null!;
    private Button _back = null!;
    private Button _forward = null!;
    private double _clock;

    public WebPage(string url)
    {
        _url = url;
        SizeFlagsHorizontal = SizeFlagsVertical = SizeFlags.ExpandFill;
        ClipContents = true;
    }

    /// <summary>The window of the desktop showing the page (its title is the one of the page).</summary>
    public DeckWindow? Window { get; set; }

    public override void _Ready()
    {
        // The bar of the other tools at the top: back, forward, reload, the address (Enter opens it), the browser of
        // the system; the page takes the rest.
        var column = new VBoxContainer();
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);
        _back = Eve.Button("◀", tooltip: "Back");
        _back.Pressed += () => Navigate("goBack");
        _forward = Eve.Button("▶", tooltip: "Forward");
        _forward.Pressed += () => Navigate("goForward");
        var reload = Eve.Button("⟳", tooltip: "Reload the page");
        reload.Pressed += () => Navigate("reload");
        _address = new LineEdit { PlaceholderText = "Address · Enter opens it", Text = _url };
        _address.TextSubmitted += text =>
        {
            var address = text.Trim();
            if (address.Length > 0)
                Load(address.Contains("://") ? address : "https://" + address);
        };
        var external = Eve.Button("⧉", tooltip: "Open the page in the browser of the system");
        external.Pressed += () => OS.ShellOpen(_url);
        // 📷: what the page shows saved in PNG (Windows: WebView2 draws it), the file shown after the button.
        var capture = Eve.Button("📷", tooltip: $"Save what the page shows as a PNG picture, in {CaptureDirectory}");
        capture.Visible = OperatingSystem.IsWindows();
        _captured = new LinkButton { Visible = false, Underline = LinkButton.UnderlineMode.OnHover, TooltipText = "Show the picture in its folder" };
        _captured.AddThemeFontSizeOverride("font_size", 11);
        _captured.AddThemeColorOverride("font_color", Eve.Green);
        _captured.Pressed += () =>
        {
            if (_capturedPath is not null)
                OS.ShellShowInFileManager(_capturedPath);
        };
        capture.Pressed += () => _ = CaptureAsync();
        column.AddChild(Ui.Toolbar(Ui.Row(6, _back, _forward, reload, Ui.Fill(_address), capture, _captured, external)));
        _surface = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        column.AddChild(_surface);
        var back = new ColorRect { Color = Eve.Space, MouseFilter = MouseFilterEnum.Ignore };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _surface.AddChild(back);
        _caption = Eve.Text_("🌐", Eve.Muted, 14, wrap: true);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _caption.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _surface.AddChild(_caption);
        if (OperatingSystem.IsWindows())
        {
            var window = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
            _edge = EdgeView.Create(window, _url, _ => Bind.OnMain(() =>
            {
                // No WebView2: the browser of the system, as before.
                _edge = null;
                if (IsInstanceValid(this))
                    _caption.Text = "🌐 The page is open in the browser of the system: " + _url;
                OS.ShellOpen(_url);
            }));
            Load(_url);
            return;
        }
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
            var window = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
            _host = Send(window, Sel("contentView"));
            var configuration = Send(Send(objc_getClass("WKWebViewConfiguration"), Sel("alloc")), Sel("init"));
            _view = InitWebView(Send(objc_getClass("WKWebView"), Sel("alloc")), Sel("initWithFrame:configuration:"), new Rect { Width = 100, Height = 100 }, configuration);
            Send(configuration, Sel("release"));
            Send(_view, Sel("setCustomUserAgent:"), NsString(UserAgent));
            SendBool(_view, Sel("setHidden:"), true);
            Send(_host, Sel("addSubview:"), _view);
            Load(_url);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            ActivityLog.Error("Web", "The browser of the chat cannot be opened", ex);
            _view = IntPtr.Zero;
            OS.ShellOpen(_url);
        }
    }

    /// <summary>Where the captures of the pages go: the Pictures folder of the system, kaonomi/web.</summary>
    public static string CaptureDirectory => System.IO.Path.Combine(ChatSettings.SystemPicturesDirectory, "kaonomi", "web");

    private LinkButton _captured = null!;
    private string? _capturedPath;

    /// <summary>
    /// 📷: what the page shows now saved in <see cref="CaptureDirectory"/> as a PNG named after its title and the
    /// time; the name of the file shows after the button (a click shows it in its folder).
    /// </summary>
    private async System.Threading.Tasks.Task CaptureAsync()
    {
        if (_edge is not { IsReady: true } edge)
        {
            ShowCaptured(null, "The page is not ready yet");
            return;
        }
        try
        {
            System.IO.Directory.CreateDirectory(CaptureDirectory);
            var name = WikiPageEntry.Slug(edge.Title is { Length: > 0 } title ? title : new Uri(_url).Host);
            var path = System.IO.Path.Combine(CaptureDirectory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await edge.CaptureAsync(path);
            ShowCaptured(path, "✓ " + System.IO.Path.GetFileName(path));
            ActivityLog.Info("Web", $"Page captured: {path} ({_url})");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or UriFormatException or COMException)
        {
            ShowCaptured(null, "Not captured: " + ex.Message);
            ActivityLog.Error("Web", "The page could not be captured", ex);
        }
    }

    private void ShowCaptured(string? path, string text)
    {
        if (!IsInstanceValid(this))
            return;
        _capturedPath = path;
        _captured.Text = text;
        _captured.AddThemeColorOverride("font_color", path is null ? Eve.Red : Eve.Green);
        _captured.Disabled = path is null;
        _captured.Visible = true;
    }

    /// <summary>Shows <paramref name="url"/>.</summary>
    public void Load(string url)
    {
        _url = url;
        _caption.Text = "🌐 " + url;
        if (_address is not null && !_address.HasFocus())
            _address.Text = url;
        if (_edge is not null)
        {
            _edge.Load(url);
            ActivityLog.Info("Web", $"Link opened in the browser of the chat: {url}");
            return;
        }
        if (_view == IntPtr.Zero)
            return;
        var address = Send(objc_getClass("NSURL"), Sel("URLWithString:"), NsString(url));
        if (address == IntPtr.Zero)
        {
            OS.ShellOpen(url);
            return;
        }
        ActivityLog.Info("Web", $"Link opened in the browser of the chat: {url}");
        if (LocalPage.IsLocalHttp(url))
        {
            _ = LoadLocalAsync(url);
            return;
        }
        Send(_view, Sel("loadRequest:"), Send(objc_getClass("NSURLRequest"), Sel("requestWithURL:"), address));
    }

    /// <summary>
    /// A page of this machine in http (the dashboard of Orleans): its HTML read here, what it loads from other sites
    /// asked in https (<see cref="LocalPage"/>), then shown with its own address as the base (its scripts and their
    /// calls go to this machine as before). Read as it is when that fails.
    /// </summary>
    private async System.Threading.Tasks.Task LoadLocalAsync(string url)
    {
        var html = await LocalPage.ReadAsync(url);
        Bind.OnMain(() =>
        {
            // Closed meanwhile, or another page asked since.
            if (!IsInstanceValid(this) || _view == IntPtr.Zero || _url != url)
                return;
            var address = Send(objc_getClass("NSURL"), Sel("URLWithString:"), NsString(url));
            if (html is null)
                Send(_view, Sel("loadRequest:"), Send(objc_getClass("NSURLRequest"), Sel("requestWithURL:"), address));
            else
                Send(_view, Sel("loadHTMLString:baseURL:"), NsString(html), address);
        });
    }

    public override void _Process(double delta)
    {
        if (_edge is not null)
        {
            ProcessEdge(delta);
            return;
        }
        if (_view == IntPtr.Zero)
            return;
        Place();
        // The title of the page in the header of the window, once a second.
        _clock += delta;
        if (_clock < 1)
            return;
        _clock = 0;
        var title = Send(_view, Sel("title"));
        var text = title == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(title, Sel("UTF8String")));
        if (!string.IsNullOrWhiteSpace(text) && Window is { } window && window.Title != text)
        {
            window.Title = text;
            _caption.Text = "🌐 " + text;
        }
        // The address of the page (it changes with the links followed) and the state of back / forward.
        var current = Send(_view, Sel("URL"));
        var absolute = current == IntPtr.Zero ? IntPtr.Zero : Send(current, Sel("absoluteString"));
        var location = absolute == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(absolute, Sel("UTF8String")));
        if (!string.IsNullOrWhiteSpace(location) && !_address.HasFocus())
        {
            _url = location;
            _address.Text = location;
        }
        _back.Disabled = !GetBool(_view, Sel("canGoBack"));
        _forward.Disabled = !GetBool(_view, Sel("canGoForward"));
    }

    /// <summary>The web view of Windows on this control; the title, the address and back / forward of its page once a second.</summary>
    private void ProcessEdge(double delta)
    {
        var edge = _edge!;
        var visible = CanShow(out var window);
        if (visible)
        {
            // From the viewport of the window to the desktop, then in proportion to the pixels of the window of the chat.
            var visibleRect = GetTree().Root.GetVisibleRect();
            var canvas = new Rect2(window!.Container.GetGlobalRect().Position + _surface.GetGlobalRect().Position - visibleRect.Position, _surface.Size);
            var ratio = (Vector2)DisplayServer.WindowGetSize() / visibleRect.Size;
            edge.Place((int)Math.Round(canvas.Position.X * ratio.X), (int)Math.Round(canvas.Position.Y * ratio.Y),
                (int)Math.Round(canvas.Size.X * ratio.X), (int)Math.Round(canvas.Size.Y * ratio.Y));
        }
        if (visible != _shown)
        {
            _shown = visible;
            edge.Show(visible);
        }
        _clock += delta;
        if (_clock < 1 || !edge.IsReady)
            return;
        _clock = 0;
        if (edge.Title is { Length: > 0 } title && Window is { } shownIn && shownIn.Title != title)
        {
            shownIn.Title = title;
            _caption.Text = "🌐 " + title;
        }
        if (edge.Url is { Length: > 0 } location && !_address.HasFocus())
        {
            _url = location;
            _address.Text = location;
        }
        _back.Disabled = !edge.CanGoBack;
        _forward.Disabled = !edge.CanGoForward;
    }

    /// <summary>True when the page can be seen: its window flat in front, nothing over it, the chat not locked.</summary>
    private bool CanShow(out DeckWindow? window)
    {
        window = Window;
        return IsVisibleInTree() && window is { IsFront: true, IsFaded: false, Motion: null } && window.Container.Visible
               && !KioskLock.IsOn && _surface.Size.X > 8 && _surface.Size.Y > 8 && !Covered(window);
    }

    /// <summary>goBack, goForward or reload on the web view.</summary>
    private void Navigate(string selector)
    {
        if (_edge is { } edge)
        {
            switch (selector)
            {
                case "goBack": edge.Back(); break;
                case "goForward": edge.Forward(); break;
                default: edge.Reload(); break;
            }
            return;
        }
        if (_view != IntPtr.Zero)
            Send(_view, Sel(selector));
    }

    /// <summary>The web view on this control, or hidden when the page cannot be seen.</summary>
    private void Place()
    {
        var visible = CanShow(out var window);
        if (visible)
        {
            // From the viewport of the window to the desktop, then in proportion to the native view of the chat (its
            // points: the scale of the interface is not the one of the screen once the window is on another screen).
            var visibleRect = GetTree().Root.GetVisibleRect();
            var canvas = new Rect2(Window!.Container.GetGlobalRect().Position + _surface.GetGlobalRect().Position - visibleRect.Position, _surface.Size);
            var bounds = GetRect(_host, Sel("bounds"));
            var ratio = new Vector2((float)bounds.Width, (float)bounds.Height) / visibleRect.Size;
            var top = canvas.Position.Y * ratio.Y;
            var frame = new Rect
            {
                X = Math.Round(canvas.Position.X * ratio.X),
                Y = Math.Round(GetBool(_host, Sel("isFlipped")) ? top : bounds.Height - top - canvas.Size.Y * ratio.Y),
                Width = Math.Round(canvas.Size.X * ratio.X),
                Height = Math.Round(canvas.Size.Y * ratio.Y)
            };
            if (!frame.Equals(_frame))
            {
                _frame = frame;
                SendRect(_view, Sel("setFrame:"), frame);
            }
        }
        if (visible == _shown)
            return;
        _shown = visible;
        SendBool(_view, Sel("setHidden:"), !visible);
        // Hidden while the keyboard was on the page: the keyboard back to the chat.
        if (!visible)
            Send(Send(_host, Sel("window")), Sel("makeFirstResponder:"), _host);
    }

    /// <summary>True when another window in front is above <paramref name="window"/> over part of it.</summary>
    private static bool Covered(DeckWindow window)
    {
        var index = window.Container.GetIndex();
        var rect = window.Container.GetGlobalRect();
        return Shell.Current.Deck.Windows.Any(other => other != window && other.IsFront && other.Container.Visible
            && other.Container.GetIndex() > index && other.Container.GetGlobalRect().Intersects(rect));
    }

    public override void _Notification(int what)
    {
        // Freed with its window (not when the window goes to the back: it leaves the tree then, and comes back).
        if (what == NotificationPredelete && _edge is { } edge)
        {
            _edge = null;
            edge.Close();
            ActivityLog.Info("Web", "Browser of the chat closed");
            return;
        }
        if (what != NotificationPredelete || _view == IntPtr.Zero)
            return;
        // The window closed: the page goes (a video stops).
        Send(_view, Sel("removeFromSuperview"));
        Send(_view, Sel("release"));
        _view = IntPtr.Zero;
        ActivityLog.Info("Web", "Browser of the chat closed");
    }
}

/// <summary>
/// A page of this machine in http, made readable by WebKit in the chat. macOS (App Transport Security) lets the web
/// view of the chat load http from this machine only: a stylesheet or a script asked from another site in http is
/// refused, or never ends when a redirection leads there, and the page stays empty. The dashboard of Orleans is such a
/// page: its <c>assets/bootstrap.css</c> redirects to <c>//maxcdn.bootstrapcdn.com/…</c>, http from a page in http.
/// Here its HTML is read, and each <c>href</c> or <c>src</c> that leads to another site in http (at once or through a
/// redirection of this machine) is written in https.
/// </summary>
public static class LocalPage
{
    private static readonly System.Net.Http.HttpClient Http = new(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly System.Text.RegularExpressions.Regex Reference =
        new("(?<attribute>\\b(?:href|src)\\s*=\\s*)(?<quote>[\"'])(?<value>[^\"'<>]+)\\k<quote>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsLocal(Uri uri) => uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for an http address of this machine (localhost, 127.0.0.1, ::1).</summary>
    public static bool IsLocalHttp(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && IsLocal(uri);

    /// <summary>
    /// The HTML of <paramref name="url"/>, what it loads from other sites in http written in https; null when it is
    /// not an HTML page that answers (it is then loaded as it is).
    /// </summary>
    public static async System.Threading.Tasks.Task<string?> ReadAsync(string url)
    {
        try
        {
            var page = new Uri(url);
            using var response = await Http.GetAsync(page);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "text/html")
                return null;
            var html = await response.Content.ReadAsStringAsync();
            var changed = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var value in Reference.Matches(html).Select(m => m.Groups["value"].Value).Distinct())
                if (await SecureAsync(page, value) is { } secure)
                    changed[value] = secure;
            if (changed.Count == 0)
                return html;
            ActivityLog.Info("Web", $"{url}: {changed.Count} resource(s) of other sites asked in https ({string.Join(", ", changed.Values.Select(v => new Uri(v).Host).Distinct())})");
            return Reference.Replace(html, m => changed.TryGetValue(m.Groups["value"].Value, out var secure)
                ? m.Groups["attribute"].Value + m.Groups["quote"].Value + secure + m.Groups["quote"].Value
                : m.Value);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            ActivityLog.Error("Web", $"{url} could not be read; loaded as it is", ex);
            return null;
        }
    }

    /// <summary>The https address of a reference that leads to another site in http; null when it is fine as it is.</summary>
    private static async System.Threading.Tasks.Task<string?> SecureAsync(Uri page, string value)
    {
        if (value.StartsWith('#') || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(page, value, out var target) || target.Scheme != Uri.UriSchemeHttp)
            return null;
        if (!IsLocal(target))
            return Https(target);
        // On this machine: where it redirects (the dashboard of Orleans sends its stylesheets to the sites of the libraries).
        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, target);
        using var response = await Http.SendAsync(request);
        return (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location
               && new Uri(target, location) is { Scheme: "http" } next && !IsLocal(next)
            ? Https(next)
            : null;
    }

    private static string Https(Uri uri) => new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = uri.IsDefaultPort ? -1 : uri.Port }.Uri.AbsoluteUri;
}
