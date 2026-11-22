using System;
using Godot;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// <c>/user</c>: the profile of the user logged in, filled by itself — first name, last name, e-mail, phone, profile (who
/// it is, in its own words), hashtags (shown as tags as they are typed) and a photo. UPLOAD PHOTO takes a picture (PNG,
/// JPEG, WebP…), cut to a square in its middle and kept at 512 px in <c>~/kaonomi/users/photos</c> at once; REMOVE PHOTO
/// takes it away; SAVE PROFILE keeps the rest (<see cref="UserStore.SaveProfile"/>). Every level fills its own profile,
/// a viewer too. An administrator opens the profile of any user from <c>/users</c> (a double-click on its line): the
/// same window, for that user (<see cref="Open"/>).
/// </summary>
public static class ProfileScreen
{
    public const string Key = "user";
    public const string Icon = "🪪";
    private const int PhotoSize = 512;

    /// <summary>Raised with the login of a profile saved (its photo too): <c>/users</c> shows it again.</summary>
    public static event Action<string>? Saved;

    /// <summary>
    /// Opens the profile of <paramref name="login"/> (null: the user logged in) in a window of the desktop, or brings
    /// back the one open. The profile of another user is for the administrators only.
    /// </summary>
    public static void Open(Shell shell, string? login = null)
    {
        var self = login is null || string.Equals(login, UserSession.Current?.Login, StringComparison.OrdinalIgnoreCase);
        if (!self && !UserSession.IsAdministrator)
        {
            shell.Chat.Status = "The profile of another user is for the administrators only";
            return;
        }
        var key = self ? Key : $"{Key}:{login!.ToLowerInvariant()}";
        if (!shell.TryBringBack(key))
            shell.OpenControl(key, self ? "My profile" : $"Profile of {login}", Icon, Build(self ? null : login), new Vector2(820, 600));
    }

    /// <param name="ofLogin">The user shown; null: the user logged in.</param>
    public static Control Build(string? ofLogin = null)
    {
        UserSession.Reload();
        var user = ofLogin is null ? UserSession.Current : UserStore.Get(ofLogin);
        if (user is null)
            return Ui.Placeholder(ofLogin is null ? "Nobody is logged in." : $"The user {ofLogin} does not exist.");
        var login = user.Login;
        var self = string.Equals(login, UserSession.Current?.Login, StringComparison.OrdinalIgnoreCase);
        // The user shown, read again after a change (the one logged in: its session follows).
        UserAccount? Shown()
        {
            if (self)
                UserSession.Reload();
            return self ? UserSession.Current : UserStore.Get(login);
        }
        void Changed()
        {
            _ = Shown();
            Saved?.Invoke(login);
        }

        TextEdit Area(string text, string placeholder) => new()
        {
            Text = text, PlaceholderText = placeholder, WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 110), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        LineEdit Line(string text, string placeholder) => new() { Text = text, PlaceholderText = placeholder, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        var first = Line(user.FirstName, "First name");
        var last = Line(user.LastName, "Last name");
        var email = Line(user.Email, "name@example.com");
        var phone = Line(user.Phone, "+33 6 12 34 56 78");
        var profile = Area(user.Profile, "Who you are: your job, what you work on, what you like…");
        var hashtags = Line(string.Join(" ", user.Hashtags), "#ia #cyber #geopolitique");
        var tags = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        tags.AddThemeConstantOverride("h_separation", 6);
        tags.AddThemeConstantOverride("v_separation", 4);
        void ShowTags()
        {
            foreach (var child in tags.GetChildren())
                child.QueueFree();
            foreach (var tag in UserStore.ParseHashtags(hashtags.Text))
                tags.AddChild(Ui.Badge(tag, Eve.Cyan));
        }
        hashtags.TextChanged += _ => ShowTags();
        ShowTags();

        var status = Eve.Text_(string.Empty, Eve.Muted, 12, wrap: true);
        void Say(string? error, string done)
        {
            status.Text = error ?? done;
            status.AddThemeColorOverride("font_color", error is null ? Eve.Green : Eve.Red);
        }

        // The photo: the one kept, else the initials of the user on a disc of the accent.
        var picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(200, 200)
        };
        var initials = Eve.Caption(Initials(user), Eve.Ink, 44);
        initials.HorizontalAlignment = HorizontalAlignment.Center;
        initials.VerticalAlignment = VerticalAlignment.Center;
        initials.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // A disc: the photo is drawn only inside it (ClipChildren), its edge above it.
        // The frame is only the shape of the disc (ClipChildren.Only: not drawn itself); the disc, the initials and the
        // photo are drawn inside it, at their full colours.
        var frame = new Panel { CustomMinimumSize = new Vector2(200, 200), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, ClipChildren = CanvasItem.ClipChildrenMode.Only };
        frame.AddThemeStyleboxOverride("panel", Eve.Box(Colors.White, Colors.White, 0, 0, 100));
        var disc = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        disc.AddThemeStyleboxOverride("panel", Eve.Box(Eve.Cyan with { A = 0.18f }, Eve.Edge, 0, 1, 100));
        disc.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(disc);
        frame.AddChild(initials);
        frame.AddChild(picture);
        Button removePhoto = null!;
        void ShowPhoto()
        {
            var path = Shown() is { } now ? UserStore.PhotoPath(now) : null;
            var image = path is null ? null : Image.LoadFromFile(path);
            picture.Texture = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
            initials.Visible = picture.Texture is null;
            removePhoto.Disabled = picture.Texture is null;
        }

        var upload = Ui.Action("📷 UPLOAD PHOTO", async () =>
        {
            var file = await Dialogs.OpenFile("Your photo", ["*.png, *.jpg, *.jpeg, *.webp, *.bmp, *.tga ; Pictures"]);
            if (file is null)
                return;
            var image = Image.LoadFromFile(file);
            if (image is null || image.IsEmpty())
            {
                Say($"{System.IO.Path.GetFileName(file)} could not be read as a picture", string.Empty);
                return;
            }
            // A square cut in the middle of the picture, 512 px at most.
            var side = Math.Min(image.GetWidth(), image.GetHeight());
            image = image.GetRegion(new Rect2I((image.GetWidth() - side) / 2, (image.GetHeight() - side) / 2, side, side));
            if (side > PhotoSize)
                image.Resize(PhotoSize, PhotoSize, Image.Interpolation.Lanczos);
            var target = UserStore.PhotoFileFor(login);
            if (image.SavePng(target) != Error.Ok)
            {
                Say($"The photo could not be written to {target}", string.Empty);
                return;
            }
            Say(UserStore.SetPhoto(login, target), "Photo saved");
            ActivityLog.Info("Users", self ? $"{login} changed its photo" : $"{UserSession.Current?.Login} changed the photo of {login}");
            Changed();
            ShowPhoto();
        }, tooltip: "A picture of you (PNG, JPEG, WebP…): cut to a square in its middle, kept at once");
        removePhoto = Ui.Action("REMOVE PHOTO", async () =>
        {
            if (!await Dialogs.Confirm("Photo", self ? "Remove your photo?" : $"Remove the photo of {login}?", "Remove"))
                return;
            Say(UserStore.SetPhoto(login, null), "Photo removed");
            Changed();
            ShowPhoto();
        });
        // Everybody fills its own profile: these buttons are not stopped for a viewer.
        upload.SetMeta(AccessControl.AllowedMeta, true);
        removePhoto.SetMeta(AccessControl.AllowedMeta, true);
        ShowPhoto();

        var save = Ui.Action("💾 SAVE PROFILE", () =>
        {
            var error = UserStore.SaveProfile(login, new UserProfile(first.Text, last.Text, email.Text, phone.Text, profile.Text, hashtags.Text));
            Say(error, "Profile saved");
            if (error is not null)
                return;
            ActivityLog.Info("Users", self ? $"{login} saved its profile" : $"{UserSession.Current?.Login} saved the profile of {login}");
            Changed();
            var saved = Shown();
            hashtags.Text = string.Join(" ", saved?.Hashtags ?? []);
            ShowTags();
            if (picture.Texture is null && saved is not null)
                initials.Text = Initials(saved);
        }, primary: true);
        save.SetMeta(AccessControl.AllowedMeta, true);

        var left = Ui.Column(8, frame, Ui.Fill(upload), Ui.Fill(removePhoto),
            Eve.Text_($"{login} · {UserSession.Name(user.Role)}", Eve.Muted, 12));
        left.CustomMinimumSize = new Vector2(210, 0);
        var names = Ui.Row(10, Ui.Field("First name", first), Ui.Field("Last name", last));
        var contact = Ui.Row(10, Ui.Field("E-mail", email), Ui.Field("Phone", phone));
        var right = Ui.Fill(Ui.Column(10, names, contact, Ui.Field("Profile", profile), Ui.Field("Hashtags", hashtags), tags));
        var bottom = Ui.Row(8, Ui.Fill(status), save);
        var page = Ui.Column(12,
            Eve.Caption(self ? "Your profile" : $"Profile of {login}", Eve.TextBright, 13),
            Ui.Row(18, left, right),
            Ui.Spring(),
            bottom);
        return Ui.Margin(Ui.Scroll(Ui.Margin(page, 4)), 8);
    }

    private static string Initials(UserAccount user)
    {
        var a = user.FirstName.Trim();
        var b = user.LastName.Trim();
        var text = a.Length > 0 || b.Length > 0 ? $"{(a.Length > 0 ? a[0] : ' ')}{(b.Length > 0 ? b[0] : ' ')}".Trim() : user.Login[..Math.Min(2, user.Login.Length)];
        return text.ToUpperInvariant();
    }
}
