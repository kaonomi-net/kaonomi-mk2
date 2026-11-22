using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The windows of the users of the chat (<see cref="UserStore"/>). <c>/users</c>, for the administrators only: every
/// user in a list with its level (changed in its drop-down list), RESET PASSWORD and DELETE, and at the bottom a line
/// to ADD one (login, password, level); the user logged in neither deletes itself nor changes its own level, and the
/// last administrator stays one. <c>/password</c>, for everybody: the current password, the new one typed twice.
/// </summary>
public static class UsersScreen
{
    public const string Key = "users";
    public const string Icon = "👥";
    public const string PasswordKey = "password";
    public const string PasswordIcon = "🔑";

    private static readonly UserRole[] Roles = [UserRole.Administrator, UserRole.User, UserRole.Viewer];

    private static string What(UserRole role) => role switch
    {
        UserRole.Administrator => "everything, with the users",
        UserRole.User => "everything but the users",
        _ => "looks: no SAVE / DELETE / PUBLISH, no system command"
    };

    private static OptionButton RolePicker(UserRole selected)
    {
        var picker = new OptionButton { TooltipText = string.Join("\n", Array.ConvertAll(Roles, r => $"{UserSession.Name(r)}: {What(r)}")) };
        foreach (var role in Roles)
            picker.AddItem(UserSession.Name(role).ToUpperInvariant());
        picker.Selected = Array.IndexOf(Roles, selected);
        return picker;
    }

    public static Control Build()
    {
        var list = Ui.Column(6);
        var status = Eve.Text_(string.Empty, Eve.Muted, 12, wrap: true);
        void Say(string? error, string done)
        {
            status.Text = error ?? done;
            status.AddThemeColorOverride("font_color", error is null ? Eve.Muted : Eve.Red);
            if (error is null)
                ActivityLog.Info("Users", $"{UserSession.Current?.Login}: {done}");
        }

        void Refresh()
        {
            foreach (var child in list.GetChildren())
                child.QueueFree();
            foreach (var user in UserStore.All())
            {
                var login = user.Login;
                var self = string.Equals(login, UserSession.Current?.Login, StringComparison.OrdinalIgnoreCase);
                var name = Eve.Text_(login, Eve.TextBright, 14);
                var who = string.Join(" · ", new[] { user.DisplayName == login ? "" : user.DisplayName, user.Email }.Where(s => s.Length > 0));
                var dates = Eve.Text_((who.Length > 0 ? who + " · " : "") + $"created {user.Created.LocalDateTime:yyyy-MM-dd} · " + (user.LastLogin is { } last ? $"last login {last.LocalDateTime:yyyy-MM-dd HH:mm}" : "never logged in"), Eve.Muted, 11);
                var texts = Ui.Fill(Ui.Column(2, self ? Ui.Row(8, name, Ui.Badge("YOU", Eve.Cyan)) : name, dates));
                var role = RolePicker(user.Role);
                role.Disabled = self;
                if (self)
                    role.TooltipText = "Your own level is changed by another administrator";
                role.ItemSelected += index =>
                {
                    var wanted = Roles[(int)index];
                    Say(UserStore.SetRole(login, wanted), $"{login} is now {UserSession.Name(wanted)}");
                    Refresh();
                };
                var reset = Ui.Action("RESET PASSWORD", async () =>
                {
                    if (await AskNewPassword(login) is { } password)
                        Say(UserStore.SetPassword(login, password), $"The password of {login} is changed");
                }, tooltip: "A new password for this user (yours is changed with /password)");
                var delete = Ui.Action("DELETE", async () =>
                {
                    if (!await Dialogs.Confirm("Delete a user", $"Delete the user {login}? It can no longer log in.", "Delete"))
                        return;
                    Say(UserStore.Delete(login), $"The user {login} is deleted");
                    Refresh();
                });
                delete.Disabled = self;
                if (self)
                    delete.TooltipText = "You cannot delete yourself";
                var buttons = Ui.Row(6, role, reset, delete);
                buttons.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                var panel = new PanelContainer { TooltipText = $"Double-click: the profile of {login}", MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                // A double-click on the line (not on its buttons): the profile of the user, as /user shows it.
                ulong lastPress = 0;
                panel.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
                        return;
                    // The double-click of the system, or two clicks within 400 ms (the clicks of a test have no double-click).
                    var now = Time.GetTicksMsec();
                    var twice = press.DoubleClick || now - lastPress < 400;
                    lastPress = twice ? 0 : now;
                    if (!twice)
                        return;
                    panel.AcceptEvent();
                    ProfileScreen.Open(Shell.Current, login);
                };
                panel.AddThemeStyleboxOverride("panel", Eve.Box(Eve.Field, Eve.Edge, 8));
                panel.AddChild(Ui.Row(8, texts, buttons));
                list.AddChild(panel);
            }
        }

        var newLogin = new LineEdit { PlaceholderText = "Login", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var newPassword = new LineEdit { PlaceholderText = "Password", Secret = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var newRole = RolePicker(UserRole.User);
        void Add()
        {
            var login = newLogin.Text.Trim();
            var role = Roles[newRole.Selected];
            var error = UserStore.Add(login, newPassword.Text, role);
            Say(error, $"The user {login} is added ({UserSession.Name(role)})");
            if (error is not null)
                return;
            newLogin.Text = newPassword.Text = string.Empty;
            Refresh();
        }
        newLogin.TextSubmitted += _ => newPassword.GrabFocus();
        newPassword.TextSubmitted += _ => Add();
        var add = Ui.Row(8, Ui.Field("New user", newLogin), Ui.Field("Password", newPassword), Ui.Column(3, Eve.Caption("Level"), newRole),
            Ui.Column(3, Eve.Caption(" "), Ui.Action("ADD", Add, primary: true)));

        var header = Ui.Column(4,
            Eve.Caption("The users of the chat", Eve.TextBright, 13),
            Eve.Text_("Administrator: everything, with this window. User: everything but the users. Viewer: looks, its SAVE / DELETE / PUBLISH buttons do nothing and the system commands (/config, /cmd, /process…) are refused. A level changed applies at the next login of that user. A double-click on a user opens its profile.", Eve.Muted, 12, wrap: true));
        Refresh();
        var page = Ui.Margin(Ui.Column(10, header, Ui.Fill(Ui.Scroll(list), vertical: true), add, status), 8);
        // A profile saved (its name, its e-mail, its photo) is shown at once in the list.
        void OnSaved(string _) => Bind.OnMain(() => { if (GodotObject.IsInstanceValid(list)) Refresh(); });
        ProfileScreen.Saved += OnSaved;
        Bind.OnGone(page, () => ProfileScreen.Saved -= OnSaved);
        return page;
    }

    /// <summary>Asks an administrator for the new password of <paramref name="login"/>, typed twice (null: cancelled).</summary>
    private static Task<string?> AskNewPassword(string login)
    {
        var done = new TaskCompletionSource<string?>();
        var dialog = new ConfirmationDialog { Title = "RESET PASSWORD", OkButtonText = "Change", MinSize = new Vector2I(420, 0), DialogHideOnOk = false };
        var first = new LineEdit { PlaceholderText = "New password", Secret = true, CustomMinimumSize = new Vector2(380, 0) };
        var second = new LineEdit { PlaceholderText = "The same again", Secret = true, CustomMinimumSize = new Vector2(380, 0) };
        var message = Eve.Text_(string.Empty, Eve.Red, 12);
        dialog.AddChild(Ui.Column(8, Eve.Text_($"A new password for {login}:", Eve.Text, 13), first, second, message));
        void Accept()
        {
            if (first.Text != second.Text)
                message.Text = "The two passwords are not the same";
            else if (first.Text.Length < UserStore.MinPasswordLength)
                message.Text = $"A password is {UserStore.MinPasswordLength} characters at least";
            else
            {
                done.TrySetResult(first.Text);
                dialog.QueueFree();
            }
        }
        first.TextSubmitted += _ => second.GrabFocus();
        second.TextSubmitted += _ => Accept();
        dialog.Confirmed += Accept;
        dialog.Canceled += () => { done.TrySetResult(null); dialog.QueueFree(); };
        Shell.Current.Root.AddChild(dialog);
        dialog.ResetSize();
        dialog.PopupCentered();
        first.GrabFocus();
        return done.Task;
    }

    /// <summary>The content of the window of <c>/password</c>; <paramref name="done"/> closes it once the password is changed.</summary>
    public static Control BuildPassword(Action done)
    {
        var login = UserSession.Current?.Login ?? string.Empty;
        var current = new LineEdit { PlaceholderText = "Current password", Secret = true };
        var first = new LineEdit { PlaceholderText = "New password", Secret = true };
        var second = new LineEdit { PlaceholderText = "The same again", Secret = true };
        var message = Eve.Text_(string.Empty, Eve.Red, 12, wrap: true);
        message.CustomMinimumSize = new Vector2(0, 34);
        void Change()
        {
            if (first.Text != second.Text)
            {
                message.Text = "The two new passwords are not the same";
                return;
            }
            if (UserStore.ChangePassword(login, current.Text, first.Text) is { } error)
            {
                message.Text = error;
                return;
            }
            ActivityLog.Info("Users", $"{login} changed its password");
            Shell.Current.Chat.Status = "Your password is changed";
            done();
        }
        current.TextSubmitted += _ => first.GrabFocus();
        first.TextSubmitted += _ => second.GrabFocus();
        second.TextSubmitted += _ => Change();
        var page = Ui.Column(10,
            Eve.Caption($"Password of {login}", Eve.TextBright, 13),
            Eve.Text_($"Logged in as {login} ({UserSession.Name(UserSession.Role)}). The new password is asked at your next login.", Eve.Muted, 12, wrap: true),
            Ui.Field("Current password", current), Ui.Field("New password", first), Ui.Field("New password, again", second),
            message, Ui.Row(8, Ui.Spring(), Ui.Action("CHANGE PASSWORD", Change, primary: true)));
        Callable.From(() => { if (GodotObject.IsInstanceValid(current)) current.GrabFocus(); }).CallDeferred();
        return Ui.Margin(page, 12);
    }
}
