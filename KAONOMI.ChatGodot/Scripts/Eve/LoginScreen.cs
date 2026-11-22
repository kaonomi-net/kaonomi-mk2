using System;
using Godot;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The screen of the start: a login and a password (<see cref="UserStore"/>) asked over the whole window before anything
/// of the chat is built (no window, no microphone, no command). Enter in the login goes to the password, Enter in the
/// password logs in. After a wrong password the screen waits before the next try: 1 s, then 2, 4… up to 30 s.
/// The same screen, with BACK, is asked to leave the voice mode (<see cref="VoiceMode.AskToLeave"/>).
/// At its top, a header after the profile of Jérôme Fortias: a band of the photo of a talk, his head in a ring over it.
/// Under the fields, the banner of the YouTube channel Le Lab IA: its round logo, its name, @lelabia, a link to it and
/// one to the LinkedIn page of Jérôme Fortias. At its right (the login of the start, not the one of the voice mode), a
/// notice in English: KAONOMI is a prototype, used at the risk of the user alone, without any liability of its
/// publisher; then the MIT license.
/// </summary>
public partial class LoginScreen : Control
{
    private readonly Action<UserAccount>? _signedIn;
    private readonly Action? _back;
    private readonly LineEdit _login = new() { PlaceholderText = "Login", CustomMinimumSize = new Vector2(300, 0) };
    private readonly LineEdit _password = new() { PlaceholderText = "Password", Secret = true, CustomMinimumSize = new Vector2(300, 0) };
    private readonly Label _message = Eve.Text_(string.Empty, Eve.Red, 12, wrap: true);
    private readonly Button _enter = Eve.Button("LOG IN", primary: true);
    private int _failures;

    /// <summary>The YouTube channel of the banner.</summary>
    public const string ChannelUrl = "https://www.youtube.com/@lelabia";

    /// <summary>The LinkedIn page of Jérôme Fortias, beside the channel.</summary>
    public const string LinkedInUrl = "https://www.linkedin.com/in/jerome-fortias/";

    /// <summary>The blue of LinkedIn.</summary>
    private static readonly Color LinkedInBlue = Color.FromHtml("#3D8FE0");

    /// <summary>The coral of the charter of Le Lab IA.</summary>
    private static readonly Color Coral = Color.FromHtml("#D9665E");

    // Godot builds its scripts with this constructor.
    public LoginScreen()
    {
    }

    /// <param name="signedIn">Called once, with the user who logged in.</param>
    /// <param name="login">A login written beforehand (the one of the command line that was refused).</param>
    /// <param name="message">A message shown at first (why the command line was refused).</param>
    /// <param name="purpose">The line under KAONOMI ("Log in to open the chat" by default).</param>
    /// <param name="back">
    /// When given, BACK (and Esc) calls it instead of logging in, and what is behind the screen shows through it (the
    /// voice mode); the screen is then hidden, not freed, so the wait after wrong passwords goes on when it is shown again.
    /// </param>
    public LoginScreen(Action<UserAccount> signedIn, string? login = null, string? message = null, string? purpose = null, Action? back = null)
    {
        _signedIn = signedIn;
        _back = back;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = Eve.Space with { A = back is null ? 1 : 0.82f }, MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var title = Eve.Caption("KAONOMI", Eve.TextBright, 26);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var subtitle = Eve.Text_(purpose ?? "Log in to open the chat", Eve.Muted, 12);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _message.CustomMinimumSize = new Vector2(300, 34);
        _message.Text = message ?? string.Empty;
        _login.Text = login ?? string.Empty;
        _login.TextSubmitted += _ => _password.GrabFocus();
        _password.TextSubmitted += _ => Submit();
        _enter.Pressed += Submit;
        Control buttons = _enter;
        if (back is not null)
        {
            var backButton = Eve.Button("BACK");
            backButton.Pressed += Back;
            _enter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            backButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(backButton);
            row.AddChild(_enter);
            buttons = row;
        }
        var column = Ui.Column(12, ProfileHeader(), title, subtitle, new Control { CustomMinimumSize = new Vector2(0, 6) },
            Ui.Field("Login", _login), Ui.Field("Password", _password), _message, buttons,
            new Control { CustomMinimumSize = new Vector2(0, 4) }, new HSeparator(), ChannelBanner());
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Eve.Box(Eve.PanelSolid, Eve.Edge, 28, 1, Eve.FloatCorner));
        panel.AddChild(column);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (back is null)
        {
            var both = new HBoxContainer();
            both.AddThemeConstantOverride("separation", 24);
            both.AddChild(panel);
            both.AddChild(Notice());
            center.AddChild(both);
        }
        else
            center.AddChild(panel);
        AddChild(center);
    }

    /// <summary>
    /// The banner of the YouTube channel: its round logo, « Jerome Fortias - Le Lab IA », @lelabia, the link to the
    /// channel (the logo and the name open it too) and the one to the LinkedIn page of Jérôme Fortias.
    /// </summary>
    private static Control ChannelBanner()
    {
        var logo = new TextureRect
        {
            Texture = RoundLogo(),
            CustomMinimumSize = new Vector2(64, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = ChannelUrl
        };
        logo.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                OS.ShellOpen(ChannelUrl);
        };
        var name = new LinkButton { Text = "Jerome Fortias - Le Lab IA", Uri = ChannelUrl, Underline = LinkButton.UnderlineMode.Never, TooltipText = ChannelUrl };
        name.AddThemeFontSizeOverride("font_size", 16);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            name.AddThemeColorOverride(state, Eve.TextBright);
        var handle = Eve.Text_("@lelabia · YouTube", Eve.Muted, 12);
        var link = new LinkButton { Text = "▶  youtube.com/@lelabia", Uri = ChannelUrl, TooltipText = ChannelUrl };
        link.AddThemeFontSizeOverride("font_size", 12);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            link.AddThemeColorOverride(state, Coral);
        var linkedIn = new LinkButton { Text = "in  linkedin.com/in/jerome-fortias", Uri = LinkedInUrl, TooltipText = LinkedInUrl };
        linkedIn.AddThemeFontSizeOverride("font_size", 12);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            linkedIn.AddThemeColorOverride(state, LinkedInBlue);
        var text = Ui.Column(2, name, handle, link, linkedIn);
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(logo);
        row.AddChild(text);
        return row;
    }

    /// <summary>The logo of the channel cut round (null when its file is not there).</summary>
    private static ImageTexture? RoundLogo() => Round("lelabia-avatar.jpg", 128, 0, 0);

    /// <summary>
    /// The header of the card, after the profile of Jérôme Fortias: a band of the photo of a talk (cut to the width of
    /// the card), his head in a white ring over its lower left corner. Nothing when its pictures are not there.
    /// </summary>
    private static Control ProfileHeader()
    {
        const float band = 110, head = 104, overlap = 50;
        var header = new Control { CustomMinimumSize = new Vector2(300, band + head - overlap), MouseFilter = MouseFilterEnum.Ignore };
        if (Picture("jerome-cover.png") is { } cover)
        {
            var clip = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
            clip.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            clip.OffsetBottom = band;
            var photo = new TextureRect
            {
                Texture = cover,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore
            };
            photo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            clip.AddChild(photo);
            header.AddChild(clip);
        }
        if (Round("jerome-head.png", 208, 6, 8) is { } face)
        {
            var portrait = new TextureRect
            {
                Texture = face,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(14, band - overlap),
                Size = new Vector2(head, head),
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = LinkedInUrl
            };
            portrait.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    OS.ShellOpen(LinkedInUrl);
            };
            header.AddChild(portrait);
        }
        return header.GetChildCount() > 0 ? header : new Control();
    }

    private static string AssetPath(string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", file);

    /// <summary>A picture of the assets (null when its file is not there).</summary>
    private static ImageTexture? Picture(string file) =>
        System.IO.File.Exists(AssetPath(file)) && Image.LoadFromFile(AssetPath(file)) is { } image && !image.IsEmpty()
            ? ImageTexture.CreateFromImage(image)
            : null;

    /// <summary>
    /// A picture of the assets cut round at <paramref name="size"/> pixels: its edge first cut by <paramref name="trim"/>
    /// pixels (a border of the picture itself), then a white ring of <paramref name="ring"/> pixels around it.
    /// </summary>
    private static ImageTexture? Round(string file, int size, int trim, int ring)
    {
        if (!System.IO.File.Exists(AssetPath(file)) || Image.LoadFromFile(AssetPath(file)) is not { } image || image.IsEmpty())
            return null;
        image.Convert(Image.Format.Rgba8);
        if (trim > 0 && image.GetWidth() > 2 * trim && image.GetHeight() > 2 * trim)
            image = image.GetRegion(new Rect2I(trim, trim, image.GetWidth() - 2 * trim, image.GetHeight() - 2 * trim));
        image.Resize(size, size, Image.Interpolation.Lanczos);
        var radius = size / 2f;
        var inner = radius - ring;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = new Vector2(x + 0.5f - radius, y + 0.5f - radius).Length();
                var color = image.GetPixel(x, y);
                // The ring (white), between the picture and the outside, each edge soft by one pixel.
                if (ring > 0)
                    color = color.Lerp(Colors.White, Mathf.Clamp(distance - inner + 0.5f, 0f, 1f));
                image.SetPixel(x, y, color with { A = color.A * Mathf.Clamp(radius - distance, 0f, 1f) });
            }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The notice of the prototype, in English.</summary>
    public const string PrototypeNotice =
        "KAONOMI is an experimental prototype, provided for research, demonstration and evaluation purposes only. It is not a "
        + "finished product: it may contain errors, lose or corrupt data, give wrong, incomplete or misleading answers "
        + "(including those generated by AI models), and stop working at any time.\n\n"
        + "By logging in and using KAONOMI, you acknowledge that you use it entirely at your own risk and that you accept "
        + "100% of the risks of its use, including any loss of data, any damage to your equipment, and any decision you "
        + "take on the basis of what it shows or says.\n\n"
        + "The publisher and the authors give no warranty of any kind and accept no liability whatsoever, to the fullest "
        + "extent permitted by the applicable law, for any direct or indirect damage arising from the use of KAONOMI or "
        + "from the inability to use it.\n\n"
        + "Do not use KAONOMI for critical, medical, legal, financial or safety-related decisions.";

    /// <summary>The MIT license of KAONOMI.</summary>
    public const string License =
        "MIT License\n\n"
        + "Copyright (c) 2026 Jérôme Fortias\n\n"
        + "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated "
        + "documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation "
        + "the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and "
        + "to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n"
        + "The above copyright notice and this permission notice shall be included in all copies or substantial portions "
        + "of the Software.\n\n"
        + "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED "
        + "TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE "
        + "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF "
        + "CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER "
        + "DEALINGS IN THE SOFTWARE.";

    /// <summary>
    /// The panel at the right of the card: PROTOTYPE, USE AT YOUR OWN RISK, the notice, then the MIT license (scrolled
    /// when the panel is not tall enough: the panel is as tall as the card).
    /// </summary>
    private static Control Notice()
    {
        var title = Eve.Caption("PROTOTYPE", Eve.Amber, 20);
        var risk = Eve.Caption("USE AT YOUR OWN RISK", Eve.TextBright, 13);
        var notice = Eve.Text_(PrototypeNotice, Eve.Text, 12, wrap: true);
        notice.CustomMinimumSize = new Vector2(400, 0);
        var license = Eve.Text_(License, Eve.Muted, 10, wrap: true);
        license.CustomMinimumSize = new Vector2(384, 0);
        var scroll = Ui.Scroll(license);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = new Vector2(400, 120);
        var column = Ui.Column(10, title, risk, notice, new HSeparator(), scroll);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(456, 0), SizeFlagsVertical = SizeFlags.Fill };
        panel.AddThemeStyleboxOverride("panel", Eve.Box(Eve.PanelSolid, Eve.Edge, 28, 1, Eve.FloatCorner));
        panel.AddChild(column);
        return panel;
    }

    public override void _Ready() => FocusField();

    /// <summary>The keyboard in the login when it is empty, else in the password.</summary>
    private void FocusField() =>
        Callable.From(() =>
        {
            if (IsInstanceValid(this) && IsVisibleInTree())
                (_login.Text.Length == 0 ? _login : _password).GrabFocus();
        }).CallDeferred();

    /// <summary>Shows the screen again (hidden by BACK), the keyboard in it.</summary>
    public void Ask()
    {
        Visible = true;
        if (!_enter.Disabled)
            _message.Text = string.Empty;
        FocusField();
    }

    /// <summary>BACK: the screen hidden, what is behind it given back.</summary>
    private void Back()
    {
        if (_back is null || !Visible)
            return;
        _password.Text = string.Empty;
        Visible = false;
        GetViewport().GuiReleaseFocus();
        _back();
    }

    public override void _Input(InputEvent e)
    {
        // Esc is BACK, before the fields see it.
        if (_back is not null && IsVisibleInTree() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Back();
        }
    }

    private void Submit()
    {
        if (_enter.Disabled)
            return;
        var login = _login.Text.Trim();
        if (login.Length == 0 || _password.Text.Length == 0)
        {
            _message.Text = "Type your login and your password";
            return;
        }
        UserAccount? user;
        try
        {
            user = UserStore.Verify(login, _password.Text);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException)
        {
            _message.Text = $"The users could not be read ({UserStore.FilePath}): {ex.Message}";
            ActivityLog.Error("Users", $"The users could not be read ({UserStore.FilePath})", ex);
            return;
        }
        _password.Text = string.Empty;
        if (user is not null)
        {
            _signedIn?.Invoke(user);
            QueueFree();
            return;
        }
        _failures++;
        ActivityLog.Info("Users", $"Login refused for {ActivityLog.Excerpt(login)} ({_failures} in a row)");
        var wait = Math.Min(30, 1 << Math.Min(_failures - 1, 5));
        _message.Text = $"Wrong login or password. Try again in {wait} s.";
        _enter.Disabled = true;
        _login.Editable = _password.Editable = false;
        GetTree().CreateTimer(wait).Timeout += () =>
        {
            if (!IsInstanceValid(this))
                return;
            _enter.Disabled = false;
            _login.Editable = _password.Editable = true;
            _message.Text = "Wrong login or password.";
            // Hidden by BACK meanwhile: the keyboard stays where it is.
            if (IsVisibleInTree())
                _password.GrabFocus();
        };
    }
}
