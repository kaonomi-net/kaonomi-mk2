using Godot;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.ChatGodot.Tools.Vision;

/// <summary>
/// The frames of a vision application (<see cref="VisionStreamViewModel"/>): at each frame drawn by Godot, the last
/// one received (if any) is decoded and shown, kept in its proportions, and what was found in it is written in the
/// label given (red when it is an alert). With <see cref="CanClick"/>, a click on the picture says where, in its
/// pixels (<see cref="PictureClicked"/>): the faces of /vision, named by a click on their box.
/// </summary>
public sealed partial class VisionScreen : TextureRect
{
    private readonly VisionStreamViewModel? _vm;
    private readonly Label? _found;
    private ImageTexture? _texture;

    // Godot builds its scripts with this constructor.
    public VisionScreen()
    {
    }

    /// <summary>Whether this point of the picture (in its pixels) can be clicked: the hand shows over it. Null: nothing can.</summary>
    public System.Func<Vector2, bool>? CanClick { get; set; }

    /// <summary>
    /// When it says true, the picture is framed in red (/vision: the user speaks to the assistant). Asked at each
    /// frame of the screen. Null: never.
    /// </summary>
    public System.Func<bool>? Framed { get; set; }

    private bool _framed;

    /// <summary>A point of the picture that can be clicked was clicked: where, in the pixels of the picture.</summary>
    public event System.Action<Vector2>? PictureClicked;

    public VisionScreen(VisionStreamViewModel vm, Label found)
    {
        _vm = vm;
        _found = found;
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(320, 240);
    }

    /// <summary>
    /// The choice of the camera of a vision window: the cameras of this machine by their name; choosing one runs
    /// the detection again on it, and it is kept for the other vision windows.
    /// </summary>
    public static Control CameraPicker(VisionStreamViewModel vm)
    {
        var cameras = new OptionButton { TooltipText = "The camera the detection reads: choosing another one starts it again on that camera (kept for the other vision windows)", CustomMinimumSize = new Vector2(190, 0), ClipText = true };
        void Fill()
        {
            cameras.Clear();
            foreach (var camera in vm.Cameras)
            {
                cameras.AddItem(camera.Name);
                cameras.SetItemMetadata(cameras.ItemCount - 1, camera.Index);
                if (camera.Index == vm.Camera)
                    cameras.Selected = cameras.ItemCount - 1;
            }
            cameras.Disabled = vm.Cameras.Count < 2;
        }
        Fill();
        Eve.Bind.When(cameras, vm, Fill, nameof(vm.Cameras), nameof(vm.Camera));
        cameras.ItemSelected += index => vm.Camera = cameras.GetItemMetadata((int)index).AsInt32();
        return Eve.Ui.Field("Camera", cameras);
    }

    /// <summary>The label of what was found, in the font of the numbers.</summary>
    public static Label FoundLabel()
    {
        var found = Eve.Eve.Text_("", Eve.Eve.Muted, 14);
        found.AddThemeFontOverride("font", Eve.Eve.Mono);
        found.ClipText = true;
        return found;
    }

    /// <summary>The red frame around the picture shown (kept in its proportions, centred).</summary>
    public override void _Draw()
    {
        if (!_framed || Texture is null)
            return;
        var picture = Texture.GetSize();
        var scale = Mathf.Min(Size.X / picture.X, Size.Y / picture.Y);
        if (scale <= 0)
            return;
        var shown = new Rect2((Size - picture * scale) / 2, picture * scale);
        DrawRect(shown.Grow(-3), Eve.Eve.Red, false, 6);
    }

    /// <summary>Where a point of this control falls in the picture shown (kept in its proportions, centred), in its pixels; null outside it.</summary>
    public Vector2? ToPicture(Vector2 local)
    {
        if (Texture is null)
            return null;
        var picture = Texture.GetSize();
        var scale = Mathf.Min(Size.X / picture.X, Size.Y / picture.Y);
        if (scale <= 0)
            return null;
        var point = (local - (Size - picture * scale) / 2) / scale;
        return point.X < 0 || point.Y < 0 || point.X > picture.X || point.Y > picture.Y ? null : point;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (CanClick is null)
            return;
        if (@event is InputEventMouseMotion motion)
            MouseDefaultCursorShape = ToPicture(motion.Position) is { } over && CanClick(over) ? CursorShape.PointingHand : CursorShape.Arrow;
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click
                 && ToPicture(click.Position) is { } point && CanClick(point))
        {
            AcceptEvent();
            PictureClicked?.Invoke(point);
        }
    }

    public override void _Process(double delta)
    {
        if (Framed is not null && Framed() != _framed)
        {
            _framed = !_framed;
            QueueRedraw();
        }
        if (_vm is null || !IsVisibleInTree())
            return;
        // The frame decoded meanwhile: shown.
        if (System.Threading.Interlocked.Exchange(ref _decoded, null) is { } ready)
        {
            var image = ready.Image;
            if (_texture is null || _texture.GetWidth() != image.GetWidth() || _texture.GetHeight() != image.GetHeight())
                Texture = _texture = ImageTexture.CreateFromImage(image);
            else
                _texture.Update(image);
            if (_found is not null)
            {
                _found.Text = ready.Found;
                _found.AddThemeColorOverride("font_color", ready.Alert ? Eve.Eve.Red : Eve.Eve.Muted);
            }
        }
        // The next frame is decoded off this thread (the JPEG of a frame of 1280 × 720 takes 2 to 3 ms of it), one at a
        // time: it is shown at the next frame of the screen. A frame that comes meanwhile replaces the one waiting.
        if (System.Threading.Interlocked.CompareExchange(ref _decoding, 1, 0) != 0)
            return;
        if (_vm.TakeFrame() is not { } frame)
        {
            System.Threading.Volatile.Write(ref _decoding, 0);
            return;
        }
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var image = new Image();
                var read = image.LoadJpgFromBuffer(frame.Picture) == Error.Ok;
                frame.Release();
                if (read)
                    System.Threading.Volatile.Write(ref _decoded, new Decoded(image, frame.Found, frame.Alert));
            }
            finally
            {
                System.Threading.Volatile.Write(ref _decoding, 0);
            }
        });
    }

    /// <summary>A frame decoded off the thread of the screen, waiting to be shown.</summary>
    private sealed record Decoded(Image Image, string Found, bool Alert);

    private Decoded? _decoded;
    private int _decoding;
}
