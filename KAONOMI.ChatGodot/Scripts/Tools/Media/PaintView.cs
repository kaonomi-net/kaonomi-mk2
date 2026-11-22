using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Writing;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Media;

/// <summary>
/// /paint: a drawing program. On the left the tools (selection, pencil, brush, spray, eraser, line, rectangle,
/// ellipse, fill, colour picker, text; each with its key), above the picture their settings (size, opacity, hardness,
/// tolerance, outline/filled, text size) and the file, undo, picture and zoom actions, below it the two colours (the
/// left button paints with the first, the right button with the second) and the palette. The wheel (or a pinch)
/// zooms, the middle button (or Space, or two fingers) moves the picture. ⌘Z/⇧⌘Z undo and redo, ⌘S saves, ⌘O opens,
/// ⌘N a new picture, ⌘C/⌘X/⌘V/⌘A the selection, [ ] the size, X swaps the colours.
/// </summary>
[ToolView(typeof(PaintWindowViewModel), "Paint", "🎨", 1400, 900)]
public sealed class PaintView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (PaintWindowViewModel)viewModel;
        var canvas = new PaintCanvas(vm) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };

        // ── The file, undo, the picture, the zoom ──
        var top = Ui.Row(6,
            Ui.Action("🗋 New", canvas.AskNew, tooltip: "A new picture (⌘N): its size, filled with the right colour (transparent when the right colour is)"),
            Ui.Action("📂 Open", canvas.AskOpen, tooltip: "Open a picture (⌘O): PNG, JPG, WEBP, BMP, TGA, SVG"),
            Ui.Action("💾 Save", () => canvas.Save(false), primary: true, tooltip: "Save (⌘S) to its file (the older version kept aside in history/), else asks where"),
            Ui.Action("Save as…", () => canvas.Save(true), tooltip: "Save under another name (⇧⌘S): PNG, JPG or WEBP"),
            Sep(),
            Ui.Action("↶", canvas.Undo, tooltip: "Undo (⌘Z)"),
            Ui.Action("↷", canvas.Redo, tooltip: "Redo (⇧⌘Z)"),
            Sep(),
            Ui.Action("✂ Cut", canvas.Cut, tooltip: "Cut the selection (⌘X)"),
            Ui.Action("⧉ Copy", canvas.Copy, tooltip: "Copy the selection (⌘C)"),
            Ui.Action("📋 Paste", canvas.Paste, tooltip: "Paste (⌘V): what was copied, else a picture copied in another program"),
            Ui.Action("⌗ Crop", canvas.Crop, tooltip: "Keep only the selection"),
            Sep(),
            Ui.Action("⇋", () => canvas.Transform("Flip", i => i.FlipX()), tooltip: "Flip left–right"),
            Ui.Action("⇵", () => canvas.Transform("Flip", i => i.FlipY()), tooltip: "Flip upside down"),
            Ui.Action("⟲", () => canvas.Transform("Rotate", i => i.Rotate90(ClockDirection.Counterclockwise)), tooltip: "Rotate a quarter turn left"),
            Ui.Action("⟳", () => canvas.Transform("Rotate", i => i.Rotate90(ClockDirection.Clockwise)), tooltip: "Rotate a quarter turn right"),
            Ui.Action("⤢ Resize", canvas.AskResize, tooltip: "Resize the picture (stretched): a size or a percentage"),
            Ui.Action("▣ Canvas", canvas.AskCanvas, tooltip: "Change the size of the canvas (the picture not stretched; the new part in the right colour)"),
            Ui.Spring(),
            Ui.Action("−", () => canvas.ZoomBy(1 / 1.25f), tooltip: "Zoom out (the wheel)"),
            Ui.Label(vm, nameof(vm.ZoomText), E.TextBright, 13),
            Ui.Action("+", () => canvas.ZoomBy(1.25f), tooltip: "Zoom in (the wheel)"),
            Ui.Action("Fit", canvas.Fit, tooltip: "The whole picture in the window"),
            Ui.Action("1:1", () => canvas.SetZoom(1), tooltip: "100 %"));

        // ── The settings of the tools ──
        var size = Slider(1, 200, vm, nameof(vm.Size), "The size of the brush, the pencil, the eraser, the spray and the outlines ([ and ])");
        var sizeText = Ui.Label(vm, nameof(vm.Size), E.TextBright, 12, format: v => $"{v} px");
        sizeText.CustomMinimumSize = new Vector2(46, 0);
        var opacity = Slider(1, 100, vm, nameof(vm.Opacity), "The opacity of what is painted");
        var hardness = Slider(0, 100, vm, nameof(vm.Hardness), "The hardness of the brush and the eraser (0: soft edges)");
        var tolerance = Slider(0, 100, vm, nameof(vm.Tolerance), "Fill: how far a colour may be from the one clicked and still be filled");
        var shape = WritingUi.Choice(vm, nameof(vm.ShapeMode), PaintWindowViewModel.ShapeModes);
        shape.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        shape.CustomMinimumSize = new Vector2(110, 0);
        shape.TooltipText = "Rectangle and ellipse: the outline (left colour), filled, or both (filled with the right colour)";
        var font = Bind.Number(new SpinBox { MinValue = 6, MaxValue = 400, Step = 1, Suffix = "px", CustomMinimumSize = new Vector2(90, 0), TooltipText = "The size of the text" }, vm, nameof(vm.FontSize));
        var options = Ui.Row(8,
            Caption("SIZE"), size, sizeText,
            Caption("OPACITY"), opacity,
            Caption("HARDNESS"), hardness,
            Caption("TOLERANCE"), tolerance,
            Caption("SHAPES"), shape,
            Caption("TEXT"), font,
            Ui.Spring(),
            Ui.Label(vm, nameof(vm.Cursor), E.Muted, 12));

        // ── The tools ──
        var tools = new VBoxContainer();
        tools.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        foreach (var (name, icon, key, tip) in PaintWindowViewModel.Tools)
        {
            var button = new Button
            {
                Text = $"{icon}  {name}",
                ToggleMode = true,
                ButtonGroup = group,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(126, 34),
                TooltipText = $"{tip} ({key})",
                FocusMode = Control.FocusModeEnum.None,
            };
            var tool = name;
            button.Pressed += () => vm.Tool = tool;
            Bind.When(button, vm, () => button.SetPressedNoSignal(vm.Tool == tool), nameof(vm.Tool));
            tools.AddChild(button);
        }

        // ── The colours ──
        var colours = Ui.Row(8, Caption("LEFT"), ColourButton(vm, nameof(vm.Primary)), Caption("RIGHT"), ColourButton(vm, nameof(vm.Secondary)),
            Ui.Action("⇄", canvas.SwapColours, tooltip: "Swap the two colours (X)"));
        var palette = new GridContainer { Columns = PaintWindowViewModel.Palette.Length / 2 };
        palette.AddThemeConstantOverride("h_separation", 3);
        palette.AddThemeConstantOverride("v_separation", 3);
        foreach (var hex in PaintWindowViewModel.Palette)
            palette.AddChild(new PaintSwatch(vm, Color.FromHtml(hex)));
        var transparent = new PaintSwatch(vm, new Color(1, 1, 1, 0)) { TooltipText = "Transparent (left click: the left colour, right click: the right colour)" };
        var bottom = Ui.Row(16, colours, palette, transparent, Ui.Spring(), Ui.Label(vm, nameof(vm.PictureSize), E.Muted, 12));

        var frame = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = E.Sunken(0.5f), BorderColor = E.Edge, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        frame.AddChild(canvas);
        var middle = Ui.Row(8, tools, frame);
        middle.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var root = Ui.Column(8, Ui.Toolbar(top), options, middle, bottom, Ui.StatusBar(vm, nameof(vm.Status)));
        return Ui.Margin(root, 10);
    }

    private static Control Sep() => new VSeparator { CustomMinimumSize = new Vector2(6, 0) };

    private static Label Caption(string text) => E.Text_(text, E.Muted, 11);

    private static HSlider Slider(int min, int max, PaintWindowViewModel vm, string name, string tip) =>
        Bind.Range(new HSlider { MinValue = min, MaxValue = max, Step = 1, CustomMinimumSize = new Vector2(100, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = tip }, vm, name);

    private static ColorPickerButton ColourButton(PaintWindowViewModel vm, string name)
    {
        var button = new ColorPickerButton { CustomMinimumSize = new Vector2(46, 30), EditAlpha = true, TooltipText = name == nameof(vm.Primary) ? "The left colour (the left button paints with it)" : "The right colour (the right button paints with it; a new picture and what is cleared take it)" };
        button.ColorChanged += c => Bind.Set(vm, name, c.ToHtml(true));
        Bind.When(button, vm, () => button.Color = Color.FromHtml(Bind.Get<string>(vm, name) ?? "000000ff"), name);
        return button;
    }
}

/// <summary>A colour of the palette: the left click makes it the left colour, the right click the right colour.</summary>
public sealed partial class PaintSwatch : Control
{
    private readonly PaintWindowViewModel _vm;
    private readonly Color _colour;

    public PaintSwatch(PaintWindowViewModel vm, Color colour)
    {
        _vm = vm;
        _colour = colour;
        CustomMinimumSize = new Vector2(22, 22);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = colour.A < 1 ? "Transparent" : "#" + colour.ToHtml(false);
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        if (_colour.A < 1)
        {
            DrawRect(rect, Colors.White);
            DrawRect(new Rect2(0, 0, Size.X / 2, Size.Y / 2), new Color(0.75f, 0.75f, 0.75f));
            DrawRect(new Rect2(Size.X / 2, Size.Y / 2, Size.X / 2, Size.Y / 2), new Color(0.75f, 0.75f, 0.75f));
        }
        DrawRect(rect, _colour);
        DrawRect(rect, E.Edge, false, 1);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } b && b.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            if (b.ButtonIndex == MouseButton.Left)
                _vm.Primary = _colour.ToHtml(true);
            else
                _vm.Secondary = _colour.ToHtml(true);
            AcceptEvent();
        }
    }
}

/// <summary>
/// The picture and what draws on it. The picture is a buffer of RGBA bytes (a texture updated once a frame when it
/// changed). A stroke or a shape fills a coverage mask (0 to 1 per pixel, its largest value kept), the picture under
/// it composed again from what it was before the stroke: an opacity below 100 does not build up where the stroke
/// crosses itself, and a shape is drawn again from scratch at each move of the mouse. Undo keeps, for each change,
/// the part of the picture changed, before and after.
/// </summary>
public sealed partial class PaintCanvas : Control
{
    private const int MaxSide = 8000;
    private const int MaxUndo = 60;
    private const long MaxUndoBytes = 600L * 1024 * 1024;

    private readonly PaintWindowViewModel _vm;
    private int _w, _h;
    private byte[] _px = [];
    private Image? _image;
    private ImageTexture? _texture;
    private bool _dirty;
    private readonly ImageTexture _checker;

    private float _zoom = 1;
    private Vector2 _offset;
    private bool _fitted;

    // The stroke being drawn.
    private byte[]? _snap;
    private float[] _mask = [];
    private float[]? _mask2;
    private Rect2I _bounds;
    private Rect2I _shapeBounds;
    private Color _colour, _colour2;
    private bool _erase;
    private float _alpha;
    private MouseButton _button = MouseButton.None;
    private Vector2 _start, _last;
    private string _strokeName = "";

    // Moving the picture.
    private bool _panning;
    private Vector2 _mouse;

    // The selection; floating: lifted (or pasted) and moved over the picture until set down.
    private Rect2I? _selection;
    private byte[]? _float;
    private Vector2I _floatSize, _floatAt;
    private byte[]? _floatBase;
    private byte[]? _floatBefore;
    private bool _dragFloat;
    private Vector2 _dragFrom;
    private Vector2I _dragAt;
    private static (byte[] Px, int W, int H)? _clip;

    // The text being typed.
    private LineEdit? _textEdit;
    private Vector2I _textAt;
    private int _textRender;
    private SubViewport? _textPort;
    private PaintTextDrawer? _textDrawer;

    private readonly Random _random = new();
    private double _sprayTime;
    private double _saveIn = -1;

    private sealed record Step(string Name, Rect2I Rect, byte[] Before, byte[] After, Vector2I SizeBefore, Vector2I SizeAfter);

    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];

    public PaintCanvas(PaintWindowViewModel vm)
    {
        _vm = vm;
        ClipContents = true;
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.Cross;
        var checker = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                checker.SetPixel(x, y, (x < 8) ^ (y < 8) ? new Color(0.8f, 0.8f, 0.8f) : Colors.White);
        _checker = ImageTexture.CreateFromImage(checker);
        TextureRepeat = TextureRepeatEnum.Enabled;

        // The picture being drawn, else a new one.
        var loaded = false;
        if (File.Exists(PaintWindowViewModel.CurrentPicture))
            loaded = LoadInto(PaintWindowViewModel.CurrentPicture, false);
        if (!loaded)
            NewPicture(1280, 800, Colors.White, false);
        _vm.Modified = false;
        _vm.Say(loaded ? "The picture being drawn, as it was left" + (_vm.FilePath.Length > 0 ? $" ({Path.GetFileName(_vm.FilePath)})" : "") : "A new picture: draw with the left button (left colour) or the right button (right colour)");

        Resized += () =>
        {
            if (!_fitted && Size.X > 10 && Size.Y > 10)
            {
                _fitted = true;
                Fit();
            }
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.Tool))
            {
                SetDown();
                CommitText();
                QueueRedraw();
            }
            else if (e.PropertyName is nameof(_vm.Size))
                QueueRedraw();
        };
        Bind.OnGone(this, () =>
        {
            SetDown();
            if (_saveIn >= 0)
                SaveCurrent();
        });
    }

    public override void _Ready()
    {
        _textPort = new SubViewport { TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Size = new Vector2I(4, 4) };
        _textDrawer = new PaintTextDrawer();
        _textPort.AddChild(_textDrawer);
        AddChild(_textPort);

        // --paint-demo (tests): every tool drawn once.
        if (OS.GetCmdlineUserArgs().Contains("--paint-demo"))
            Callable.From(() => { _ = Demo(); }).CallDeferred();
    }

    // ── The picture ──

    private void NewPicture(int w, int h, Color fill, bool undoable)
    {
        (byte[] Px, Vector2I Size)? before = undoable ? Whole() : null;
        _w = w;
        _h = h;
        _px = new byte[w * h * 4];
        var (r, g, b, a) = Bytes(fill);
        for (var i = 0; i < _px.Length; i += 4)
        {
            _px[i] = r;
            _px[i + 1] = g;
            _px[i + 2] = b;
            _px[i + 3] = a;
        }
        AfterResize();
        if (before is { } old)
            Push(new Step("New", new Rect2I(0, 0, w, h), old.Px, (byte[])_px.Clone(), old.Size, new Vector2I(w, h)));
    }

    private void SetPixels(byte[] px, int w, int h)
    {
        _w = w;
        _h = h;
        _px = px;
        AfterResize();
    }

    private void AfterResize()
    {
        _mask = new float[_w * _h];
        _mask2 = null;
        _selection = null;
        _float = null;
        _image = Image.CreateFromData(_w, _h, false, Image.Format.Rgba8, _px);
        _texture = ImageTexture.CreateFromImage(_image);
        _vm.PictureSize = $"{_w} × {_h} px";
        QueueRedraw();
    }

    private (byte[] Px, Vector2I Size) Whole() => ((byte[])_px.Clone(), new Vector2I(_w, _h));

    private Image ToImage() => Image.CreateFromData(_w, _h, false, Image.Format.Rgba8, (byte[])_px.Clone());

    private bool LoadInto(string path, bool undoable)
    {
        try
        {
            var image = Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
                throw new IOException("not a picture");
            if (image.IsCompressed())
                image.Decompress();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() > MaxSide || image.GetHeight() > MaxSide)
                throw new IOException($"larger than {MaxSide} × {MaxSide}");
            var before = undoable ? Whole() : default;
            SetPixels(image.GetData(), image.GetWidth(), image.GetHeight());
            if (undoable)
                Push(new Step("Open", new Rect2I(0, 0, _w, _h), before.Px, (byte[])_px.Clone(), before.Size, new Vector2I(_w, _h)));
            return true;
        }
        catch (Exception ex)
        {
            _vm.Say($"{Path.GetFileName(path)} could not be opened: {ex.Message}", true);
            return false;
        }
    }

    public override void _Process(double delta)
    {
        if (_dirty && _image is not null && _texture is not null)
        {
            _dirty = false;
            _image.SetData(_w, _h, false, Image.Format.Rgba8, _px);
            _texture.Update(_image);
            QueueRedraw();
        }

        // The spray keeps spraying while the button is held.
        if (_snap is not null && _vm.Tool == "Spray" && _button != MouseButton.None)
        {
            _sprayTime += delta;
            while (_sprayTime > 0.03)
            {
                _sprayTime -= 0.03;
                Spray(_last);
            }
        }

        // The picture being drawn kept on the disk a little after it changed.
        if (_saveIn >= 0)
        {
            _saveIn -= delta;
            if (_saveIn < 0)
                SaveCurrent();
        }
    }

    private void SaveCurrent()
    {
        _saveIn = -1;
        try
        {
            Directory.CreateDirectory(PaintWindowViewModel.Folder);
            ToImage().SavePng(PaintWindowViewModel.CurrentPicture);
        }
        catch (Exception ex)
        {
            KAONOMI.Chat.Services.ActivityLog.Info("Paint", "The picture being drawn could not be kept: " + ex.Message);
        }
    }

    // ── Drawing it on the screen ──

    private Vector2 ToPicture(Vector2 local) => (local - _offset) / _zoom;

    private Vector2 ToScreen(Vector2 picture) => picture * _zoom + _offset;

    public override void _Draw()
    {
        if (_texture is null)
            return;
        var rect = new Rect2(_offset, new Vector2(_w, _h) * _zoom);
        DrawRect(rect.Grow(1), new Color(0, 0, 0, 0.5f), false, 1);
        TextureFilter = TextureFilterEnum.Linear;
        DrawTextureRect(_checker, rect, true);
        TextureFilter = _zoom >= 2 ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
        DrawTextureRect(_texture, rect, false);

        // The selection.
        Rect2I? shown = _float is not null ? new Rect2I(_floatAt, _floatSize) : _selection;
        if (shown is { } s)
        {
            var r = new Rect2(ToScreen(s.Position), (Vector2)s.Size * _zoom);
            DrawRect(r, Colors.White, false, 1);
            var corners = new[] { r.Position, r.Position + new Vector2(r.Size.X, 0), r.End, r.Position + new Vector2(0, r.Size.Y), r.Position };
            for (var i = 0; i < 4; i++)
                DrawDashedLine(corners[i], corners[i + 1], Colors.Black, 1, 5);
        }

        // The size of the brush around the mouse.
        if (_vm.Tool is "Brush" or "Pencil" or "Eraser" or "Spray" && !_panning && GetGlobalRect().HasPoint(GetGlobalMousePosition()))
        {
            var radius = Math.Max(_vm.Size * _zoom / 2, 2);
            DrawArc(_mouse, radius, 0, Mathf.Tau, 48, new Color(0, 0, 0, 0.6f), 1.5f);
            DrawArc(_mouse, radius + 1, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.6f), 1);
        }
    }

    public void SetZoom(float zoom, Vector2? around = null)
    {
        var at = around ?? Size / 2;
        var picture = ToPicture(at);
        _zoom = Math.Clamp(zoom, 0.05f, 32f);
        _offset = at - picture * _zoom;
        _vm.ZoomText = $"{_zoom * 100:0} %";
        QueueRedraw();
    }

    public void ZoomBy(float factor) => SetZoom(_zoom * factor);

    public void Fit()
    {
        if (Size.X < 10 || Size.Y < 10)
            return;
        _zoom = Math.Min(1, Math.Min((Size.X - 20) / _w, (Size.Y - 20) / _h));
        _offset = ((Size - new Vector2(_w, _h) * _zoom) / 2).Floor();
        _vm.ZoomText = $"{_zoom * 100:0} %";
        QueueRedraw();
    }

    // ── The mouse and the keys ──

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                SetZoom(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), wheel.Position);
                AcceptEvent();
                break;
            case InputEventMagnifyGesture magnify:
                SetZoom(_zoom * magnify.Factor, magnify.Position);
                AcceptEvent();
                break;
            case InputEventPanGesture pan:
                _offset -= pan.Delta * 12;
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton button:
                OnButton(button);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                OnMotion(motion);
                break;
            case InputEventKey { Pressed: true } key:
                if (OnKey(key))
                    AcceptEvent();
                break;
        }
    }

    private void OnButton(InputEventMouseButton b)
    {
        _mouse = b.Position;
        if (b.Pressed)
            GrabFocus();
        if (b.ButtonIndex == MouseButton.Middle || (b.ButtonIndex == MouseButton.Left && b.Pressed && Input.IsKeyPressed(Key.Space)))
        {
            _panning = b.Pressed;
            return;
        }
        if (_panning && !b.Pressed)
        {
            _panning = false;
            return;
        }
        if (b.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
            return;
        var at = ToPicture(b.Position);
        if (b.Pressed)
        {
            if (_button != MouseButton.None)
                return;
            _button = b.ButtonIndex;
            Press(at, b.ButtonIndex == MouseButton.Right, b.DoubleClick);
        }
        else if (b.ButtonIndex == _button)
        {
            Release(at);
            _button = MouseButton.None;
        }
    }

    private void OnMotion(InputEventMouseMotion m)
    {
        var previous = _mouse;
        _mouse = m.Position;
        if (_panning)
        {
            _offset += _mouse - previous;
            QueueRedraw();
            return;
        }
        var at = ToPicture(m.Position);
        var x = (int)Math.Floor(at.X);
        var y = (int)Math.Floor(at.Y);
        if (x >= 0 && y >= 0 && x < _w && y < _h)
        {
            var i = (y * _w + x) * 4;
            _vm.Cursor = $"{x}, {y}   #{_px[i]:x2}{_px[i + 1]:x2}{_px[i + 2]:x2}" + (_px[i + 3] < 255 ? $" α{_px[i + 3]}" : "");
        }
        else
            _vm.Cursor = "";
        if (_button != MouseButton.None)
            Drag(at, m.ShiftPressed);
        if (_vm.Tool is "Brush" or "Pencil" or "Eraser" or "Spray")
            QueueRedraw();
    }

    private bool OnKey(InputEventKey k)
    {
        var command = k.IsCommandOrControlPressed();
        switch (k.Keycode)
        {
            case Key.Z when command && k.ShiftPressed:
            case Key.Y when command:
                Redo();
                return true;
            case Key.Z when command:
                Undo();
                return true;
            case Key.S when command:
                Save(k.ShiftPressed);
                return true;
            case Key.O when command:
                AskOpen();
                return true;
            case Key.N when command:
                AskNew();
                return true;
            case Key.C when command:
                Copy();
                return true;
            case Key.X when command:
                Cut();
                return true;
            case Key.V when command:
                Paste();
                return true;
            case Key.A when command:
                SetDown();
                _vm.Tool = "Select";
                _selection = new Rect2I(0, 0, _w, _h);
                QueueRedraw();
                return true;
            case Key.Delete or Key.Backspace:
                DeleteSelection();
                return true;
            case Key.Escape:
                if (_float is not null)
                    SetDown();
                _selection = null;
                QueueRedraw();
                return true;
            case Key.Enter or Key.KpEnter:
                SetDown();
                return true;
            case Key.Bracketleft:
                _vm.Size = Math.Max(1, _vm.Size - (_vm.Size > 20 ? 5 : 1));
                return true;
            case Key.Bracketright:
                _vm.Size = Math.Min(200, _vm.Size + (_vm.Size >= 20 ? 5 : 1));
                return true;
            case Key.X when !command:
                SwapColours();
                return true;
            case Key.Plus or Key.Equal or Key.KpAdd:
                ZoomBy(1.25f);
                return true;
            case Key.Minus or Key.KpSubtract:
                ZoomBy(1 / 1.25f);
                return true;
            case Key.Key0 when command:
                Fit();
                return true;
        }
        if (!command && !k.AltPressed)
        {
            var letter = OS.GetKeycodeString(k.Keycode);
            foreach (var (name, _, key, _) in PaintWindowViewModel.Tools)
                if (string.Equals(letter, key, StringComparison.OrdinalIgnoreCase))
                {
                    _vm.Tool = name;
                    return true;
                }
        }
        return false;
    }

    public void SwapColours() => (_vm.Primary, _vm.Secondary) = (_vm.Secondary, _vm.Primary);

    // ── The tools ──

    private Color Left => Color.FromHtml(_vm.Primary);

    private Color Right => Color.FromHtml(_vm.Secondary);

    private void Press(Vector2 at, bool right, bool doubleClick)
    {
        _start = _last = at;
        var tool = _vm.Tool;
        switch (tool)
        {
            case "Picker":
                Pick(at, right);
                return;
            case "Text":
                if (_textEdit is not null)
                    CommitText();
                StartText(at);
                return;
            case "Select":
                PressSelect(at, doubleClick);
                return;
        }

        Begin(tool, right ? Right : Left, right ? Left : Right, tool == "Eraser" && !right);
        switch (tool)
        {
            case "Pencil":
            case "Brush":
            case "Eraser":
                Segment(at, at);
                break;
            case "Spray":
                _sprayTime = 0;
                Spray(at);
                break;
            case "Fill":
                Fill(at);
                End();
                break;
            case "Line" or "Rectangle" or "Ellipse":
                Shape(at, false);
                break;
        }
    }

    private void Drag(Vector2 at, bool shift)
    {
        switch (_vm.Tool)
        {
            case "Select":
                DragSelect(at);
                break;
            case "Picker":
                Pick(at, _button == MouseButton.Right);
                break;
            case "Pencil" or "Brush" or "Eraser" when _snap is not null:
                Segment(_last, at);
                break;
            case "Spray" when _snap is not null:
                Spray(at);
                break;
            case "Line" or "Rectangle" or "Ellipse" when _snap is not null:
                Shape(at, shift);
                break;
        }
        _last = at;
    }

    private void Release(Vector2 at)
    {
        if (_vm.Tool == "Select")
        {
            ReleaseSelect(at);
            return;
        }
        if (_snap is not null)
            End();
    }

    private void Pick(Vector2 at, bool right)
    {
        var x = (int)Math.Floor(at.X);
        var y = (int)Math.Floor(at.Y);
        if (x < 0 || y < 0 || x >= _w || y >= _h)
            return;
        var i = (y * _w + x) * 4;
        var hex = $"{_px[i]:x2}{_px[i + 1]:x2}{_px[i + 2]:x2}{_px[i + 3]:x2}";
        if (right)
            _vm.Secondary = hex;
        else
            _vm.Primary = hex;
    }

    // ── Strokes: a mask, the picture composed again under it ──

    private void Begin(string name, Color colour, Color colour2, bool erase)
    {
        SetDown();
        _strokeName = name;
        _snap = (byte[])_px.Clone();
        Array.Clear(_mask);
        _mask2 = null;
        _bounds = new Rect2I();
        _shapeBounds = new Rect2I();
        _colour = colour;
        _colour2 = colour2;
        _erase = erase;
        _alpha = _vm.Opacity / 100f;
    }

    private void End()
    {
        if (_snap is null)
            return;
        if (_bounds.HasArea())
        {
            var before = Region(_snap, _w, _bounds);
            var after = Region(_px, _w, _bounds);
            Push(new Step(_strokeName, _bounds, before, after, new Vector2I(_w, _h), new Vector2I(_w, _h)));
        }
        _snap = null;
        _mask2 = null;
    }

    private Rect2I Clip(Rect2I r) => r.Intersection(new Rect2I(0, 0, _w, _h));

    private static Rect2I Around(Vector2 a, Vector2 b, float margin)
    {
        var min = a.Min(b) - new Vector2(margin, margin);
        var max = a.Max(b) + new Vector2(margin, margin);
        var x0 = (int)Math.Floor(min.X);
        var y0 = (int)Math.Floor(min.Y);
        return new Rect2I(x0, y0, (int)Math.Ceiling(max.X) - x0 + 1, (int)Math.Ceiling(max.Y) - y0 + 1);
    }

    private void Grow(Rect2I r) => _bounds = _bounds.HasArea() ? _bounds.Merge(r) : r;

    /// <summary>The pixels of <paramref name="r"/> composed again: the picture as it was, the mask's colour over it.</summary>
    private void Compose(Rect2I r)
    {
        r = Clip(r);
        if (!r.HasArea() || _snap is null)
            return;
        float cr = _colour.R, cg = _colour.G, cb = _colour.B, ca = _colour.A;
        float fr = _colour2.R, fg = _colour2.G, fb = _colour2.B, fa = _colour2.A;
        for (var y = r.Position.Y; y < r.End.Y; y++)
        {
            for (var x = r.Position.X; x < r.End.X; x++)
            {
                var p = y * _w + x;
                var i = p * 4;
                // Not painted (most of the rectangle of a stroke): the picture as it was, copied.
                if (_mask[p] <= 0 && (_mask2 is null || _mask2[p] <= 0))
                {
                    _px[i] = _snap[i];
                    _px[i + 1] = _snap[i + 1];
                    _px[i + 2] = _snap[i + 2];
                    _px[i + 3] = _snap[i + 3];
                    continue;
                }
                float dr = _snap[i] / 255f, dg = _snap[i + 1] / 255f, db = _snap[i + 2] / 255f, da = _snap[i + 3] / 255f;
                if (_mask2 is not null && _mask2[p] > 0)
                    Over(ref dr, ref dg, ref db, ref da, fr, fg, fb, fa * _mask2[p] * _alpha);
                var m = _mask[p] * _alpha;
                if (m > 0)
                {
                    if (_erase)
                        da *= 1 - m;
                    else
                        Over(ref dr, ref dg, ref db, ref da, cr, cg, cb, ca * m);
                }
                _px[i] = (byte)Math.Clamp(dr * 255 + 0.5f, 0, 255);
                _px[i + 1] = (byte)Math.Clamp(dg * 255 + 0.5f, 0, 255);
                _px[i + 2] = (byte)Math.Clamp(db * 255 + 0.5f, 0, 255);
                _px[i + 3] = (byte)Math.Clamp(da * 255 + 0.5f, 0, 255);
            }
        }
        _dirty = true;
    }

    private static void Over(ref float dr, ref float dg, ref float db, ref float da, float sr, float sg, float sb, float sa)
    {
        var oa = sa + da * (1 - sa);
        if (oa <= 0.0001f)
        {
            dr = dg = db = da = 0;
            return;
        }
        dr = (sr * sa + dr * da * (1 - sa)) / oa;
        dg = (sg * sa + dg * da * (1 - sa)) / oa;
        db = (sb * sa + db * da * (1 - sa)) / oa;
        da = oa;
    }

    /// <summary>A piece of stroke from <paramref name="a"/> to <paramref name="b"/>: the pixels near the segment covered.</summary>
    private void Segment(Vector2 a, Vector2 b, float? width = null, float? hardness = null)
    {
        var pencil = _vm.Tool == "Pencil" && width is null;
        var radius = (width ?? _vm.Size) / 2f;
        var hard = hardness ?? (_vm.Hardness / 100f);
        if (pencil)
        {
            a = a.Floor() + new Vector2(0.5f, 0.5f);
            b = b.Floor() + new Vector2(0.5f, 0.5f);
            radius = Math.Max(radius, 0.5f);
        }
        var box = Clip(Around(a, b, radius + 2));
        if (!box.HasArea())
            return;
        var ab = b - a;
        var length2 = ab.LengthSquared();
        var ramp = Math.Max(radius * (1 - hard), 1f);
        for (var y = box.Position.Y; y < box.End.Y; y++)
        {
            for (var x = box.Position.X; x < box.End.X; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var t = length2 > 0 ? Math.Clamp((p - a).Dot(ab) / length2, 0, 1) : 0;
                var d = (p - (a + ab * t)).Length();
                var cover = pencil ? (d <= radius + 0.001f ? 1f : 0f) : Math.Clamp((radius + 0.5f - d) / ramp, 0, 1);
                var k = y * _w + x;
                if (cover > _mask[k])
                    _mask[k] = cover;
            }
        }
        Grow(box);
        Compose(box);
    }

    private void Spray(Vector2 at)
    {
        var radius = Math.Max(_vm.Size / 2f, 2);
        var dots = Math.Clamp((int)(radius * radius * 0.08f), 4, 400);
        for (var n = 0; n < dots; n++)
        {
            var angle = _random.NextDouble() * Math.Tau;
            var distance = Math.Sqrt(_random.NextDouble()) * radius;
            var x = (int)Math.Floor(at.X + Math.Cos(angle) * distance);
            var y = (int)Math.Floor(at.Y + Math.Sin(angle) * distance);
            if (x >= 0 && y >= 0 && x < _w && y < _h)
                _mask[y * _w + x] = 1;
        }
        var box = Clip(Around(at, at, radius + 1));
        if (box.HasArea())
        {
            Grow(box);
            Compose(box);
        }
    }

    /// <summary>A line, a rectangle or an ellipse from where the button was pressed to <paramref name="at"/>, drawn again from scratch.</summary>
    private void Shape(Vector2 at, bool shift)
    {
        // What the previous position drew, taken away.
        if (_shapeBounds.HasArea())
        {
            ClearMasks(_shapeBounds);
            Compose(_shapeBounds);
        }
        var tool = _vm.Tool;
        var width = (float)_vm.Size;
        if (tool == "Line")
        {
            var end = at;
            if (shift)
            {
                var v = at - _start;
                var angle = Mathf.Round(Mathf.Atan2(v.Y, v.X) / (Mathf.Pi / 4)) * (Mathf.Pi / 4);
                end = _start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * v.Length();
            }
            var before = _bounds;
            _bounds = new Rect2I();
            Segment(_start, end, width, 1);
            _shapeBounds = _bounds;
            _bounds = before.HasArea() ? before.Merge(_shapeBounds) : _shapeBounds;
            return;
        }

        var a = _start.Floor();
        var b = at.Floor();
        if (shift)
        {
            var side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            b = a + new Vector2(Math.Sign(b.X - a.X) * side, Math.Sign(b.Y - a.Y) * side);
        }
        var min = a.Min(b);
        var max = a.Max(b) + Vector2.One;
        var centre = (min + max) / 2;
        var half = (max - min) / 2;
        var outline = _vm.ShapeMode != "Filled";
        var filled = _vm.ShapeMode != "Outline";
        var both = outline && filled;
        if (both)
            _mask2 ??= new float[_w * _h];
        var box = Clip(Around(min, max, 2));
        if (!box.HasArea())
            return;
        var ellipse = tool == "Ellipse";
        for (var y = box.Position.Y; y < box.End.Y; y++)
        {
            for (var x = box.Position.X; x < box.End.X; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - centre;
                float sd;
                if (ellipse)
                {
                    var rx = Math.Max(half.X, 0.5f);
                    var ry = Math.Max(half.Y, 0.5f);
                    var f = Mathf.Sqrt(p.X * p.X / (rx * rx) + p.Y * p.Y / (ry * ry));
                    var g = Mathf.Sqrt(p.X * p.X / (rx * rx * rx * rx) + p.Y * p.Y / (ry * ry * ry * ry));
                    sd = g > 0 ? (f - 1) * f / g : -Math.Min(rx, ry);
                }
                else
                {
                    var q = p.Abs() - half;
                    sd = q.Max(Vector2.Zero).Length() + Math.Min(Math.Max(q.X, q.Y), 0);
                }
                var fill = Math.Clamp(0.5f - sd, 0, 1);
                var line = Math.Clamp(Math.Min(0.5f - sd, sd + width + 0.5f), 0, 1);
                var k = y * _w + x;
                if (both)
                {
                    _mask[k] = line;
                    _mask2![k] = fill;
                }
                else
                    _mask[k] = outline ? line : fill;
            }
        }
        _shapeBounds = box;
        Grow(box);
        Compose(box);
    }

    private void ClearMasks(Rect2I r)
    {
        r = Clip(r);
        for (var y = r.Position.Y; y < r.End.Y; y++)
        {
            Array.Clear(_mask, y * _w + r.Position.X, r.Size.X);
            if (_mask2 is not null)
                Array.Clear(_mask2, y * _w + r.Position.X, r.Size.X);
        }
    }

    /// <summary>The area of the colour clicked (within the tolerance), filled: one line of pixels at a time.</summary>
    private void Fill(Vector2 at)
    {
        var sx = (int)Math.Floor(at.X);
        var sy = (int)Math.Floor(at.Y);
        if (sx < 0 || sy < 0 || sx >= _w || sy >= _h)
            return;
        var s = (sy * _w + sx) * 4;
        int r0 = _px[s], g0 = _px[s + 1], b0 = _px[s + 2], a0 = _px[s + 3];
        var tolerance = (int)(_vm.Tolerance * 2.55f);
        var seen = new bool[_w * _h];
        bool Same(int p)
        {
            if (seen[p])
                return false;
            var i = p * 4;
            return Math.Abs(_px[i] - r0) <= tolerance && Math.Abs(_px[i + 1] - g0) <= tolerance && Math.Abs(_px[i + 2] - b0) <= tolerance && Math.Abs(_px[i + 3] - a0) <= tolerance;
        }
        int minX = sx, maxX = sx, minY = sy, maxY = sy;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var row = y * _w;
            if (!Same(row + x))
                continue;
            var left = x;
            while (left > 0 && Same(row + left - 1))
                left--;
            var right = x;
            while (right < _w - 1 && Same(row + right + 1))
                right++;
            for (var i = left; i <= right; i++)
            {
                seen[row + i] = true;
                _mask[row + i] = 1;
            }
            minX = Math.Min(minX, left);
            maxX = Math.Max(maxX, right);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            foreach (var ny in new[] { y - 1, y + 1 })
            {
                if (ny < 0 || ny >= _h)
                    continue;
                var nrow = ny * _w;
                var inside = false;
                for (var i = left; i <= right; i++)
                {
                    var same = Same(nrow + i);
                    if (same && !inside)
                        stack.Push((i, ny));
                    inside = same;
                }
            }
        }
        var box = new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
        Grow(box);
        Compose(box);
    }

    // ── The selection ──

    private void PressSelect(Vector2 at, bool doubleClick)
    {
        var p = new Vector2I((int)Math.Floor(at.X), (int)Math.Floor(at.Y));
        if (_float is not null && new Rect2I(_floatAt, _floatSize).HasPoint(p))
        {
            _dragFloat = true;
        }
        else if (_float is null && _selection is { } s && s.HasPoint(p) && !doubleClick)
        {
            Lift(s);
            _dragFloat = true;
        }
        else
        {
            SetDown();
            _selection = null;
            _dragFloat = false;
        }
        _dragFrom = at;
        _dragAt = _floatAt;
        QueueRedraw();
    }

    private void DragSelect(Vector2 at)
    {
        if (_dragFloat && _float is not null)
        {
            var moved = _dragAt + new Vector2I((int)Math.Round(at.X - _dragFrom.X), (int)Math.Round(at.Y - _dragFrom.Y));
            MoveFloat(moved);
            return;
        }
        var a = new Vector2I((int)Math.Floor(Math.Clamp(_start.X, 0, _w)), (int)Math.Floor(Math.Clamp(_start.Y, 0, _h)));
        var b = new Vector2I((int)Math.Floor(Math.Clamp(at.X, 0, _w)), (int)Math.Floor(Math.Clamp(at.Y, 0, _h)));
        var r = new Rect2I(a.Min(b), (a - b).Abs());
        _selection = r.HasArea() ? r : null;
        QueueRedraw();
    }

    private void ReleaseSelect(Vector2 at) => _dragFloat = false;

    /// <summary>The selection lifted off the picture (its place taken by the right colour), to be moved.</summary>
    private void Lift(Rect2I s)
    {
        _floatBefore = (byte[])_px.Clone();
        _float = Region(_px, _w, s);
        _floatSize = s.Size;
        _floatAt = s.Position;
        var (r, g, b, a) = Bytes(Right);
        for (var y = s.Position.Y; y < s.End.Y; y++)
            for (var x = s.Position.X; x < s.End.X; x++)
            {
                var i = (y * _w + x) * 4;
                _px[i] = r;
                _px[i + 1] = g;
                _px[i + 2] = b;
                _px[i + 3] = a;
            }
        _floatBase = (byte[])_px.Clone();
        _selection = null;
        MoveFloat(_floatAt);
    }

    /// <summary>A picture laid over the picture, floating (pasted), to be moved then set down.</summary>
    private void Float(byte[] px, int w, int h, Vector2I at)
    {
        SetDown();
        _vm.Tool = "Select";
        _floatBefore = (byte[])_px.Clone();
        _floatBase = (byte[])_px.Clone();
        _float = px;
        _floatSize = new Vector2I(w, h);
        _floatAt = at;
        _selection = null;
        MoveFloat(at);
    }

    private void MoveFloat(Vector2I at)
    {
        if (_float is null || _floatBase is null)
            return;
        // Where it was, as under it.
        var old = Clip(new Rect2I(_floatAt, _floatSize));
        for (var y = old.Position.Y; y < old.End.Y; y++)
            Buffer.BlockCopy(_floatBase, (y * _w + old.Position.X) * 4, _px, (y * _w + old.Position.X) * 4, old.Size.X * 4);
        _floatAt = at;
        var now = Clip(new Rect2I(at, _floatSize));
        for (var y = now.Position.Y; y < now.End.Y; y++)
        {
            for (var x = now.Position.X; x < now.End.X; x++)
            {
                var i = (y * _w + x) * 4;
                var f = ((y - at.Y) * _floatSize.X + (x - at.X)) * 4;
                float dr = _px[i] / 255f, dg = _px[i + 1] / 255f, db = _px[i + 2] / 255f, da = _px[i + 3] / 255f;
                Over(ref dr, ref dg, ref db, ref da, _float[f] / 255f, _float[f + 1] / 255f, _float[f + 2] / 255f, _float[f + 3] / 255f);
                _px[i] = (byte)(dr * 255 + 0.5f);
                _px[i + 1] = (byte)(dg * 255 + 0.5f);
                _px[i + 2] = (byte)(db * 255 + 0.5f);
                _px[i + 3] = (byte)(da * 255 + 0.5f);
            }
        }
        _dirty = true;
        QueueRedraw();
    }

    /// <summary>What floats set down on the picture (one step to undo), the selection kept where it is.</summary>
    private void SetDown()
    {
        if (_float is null || _floatBefore is null)
            return;
        var rect = new Rect2I(0, 0, _w, _h);
        Push(new Step("Move", rect, _floatBefore, (byte[])_px.Clone(), new Vector2I(_w, _h), new Vector2I(_w, _h)));
        var placed = Clip(new Rect2I(_floatAt, _floatSize));
        _float = null;
        _floatBase = null;
        _floatBefore = null;
        _selection = placed.HasArea() ? placed : null;
        QueueRedraw();
    }

    public void Copy()
    {
        if (_float is not null)
            _clip = ((byte[])_float.Clone(), _floatSize.X, _floatSize.Y);
        else if (_selection is { } s)
            _clip = (Region(_px, _w, s), s.Size.X, s.Size.Y);
        else
        {
            _vm.Say("Nothing selected: the Select tool (S) drags a rectangle (⌘A: all)");
            return;
        }
        _vm.Say($"Copied: {_clip.Value.W} × {_clip.Value.H} px");
    }

    public void Cut()
    {
        if (_float is null && _selection is null)
        {
            Copy();
            return;
        }
        Copy();
        DeleteSelection();
    }

    public void Paste()
    {
        GrabFocus();
        var view = ToPicture(Vector2.Zero);
        var at = new Vector2I(Math.Clamp((int)view.X + 10, 0, Math.Max(0, _w - 10)), Math.Clamp((int)view.Y + 10, 0, Math.Max(0, _h - 10)));
        if (_clip is { } clip)
        {
            Float((byte[])clip.Px.Clone(), clip.W, clip.H, at);
            _vm.Say("Pasted: drag it where it goes, then Enter (or a click outside) sets it down");
            return;
        }
        if (DisplayServer.ClipboardHasImage() && DisplayServer.ClipboardGetImage() is { } image && !image.IsEmpty())
        {
            if (image.IsCompressed())
                image.Decompress();
            image.Convert(Image.Format.Rgba8);
            Float(image.GetData(), image.GetWidth(), image.GetHeight(), at);
            _vm.Say("Picture pasted from the clipboard: drag it where it goes, then Enter sets it down");
            return;
        }
        _vm.Say("Nothing to paste");
    }

    private void DeleteSelection()
    {
        if (_float is not null && _floatBase is not null)
        {
            // What floated, dropped: the picture as under it.
            Buffer.BlockCopy(_floatBase, 0, _px, 0, _px.Length);
            _float = null;
            Push(new Step("Delete", new Rect2I(0, 0, _w, _h), _floatBefore!, (byte[])_px.Clone(), new Vector2I(_w, _h), new Vector2I(_w, _h)));
            _floatBase = null;
            _floatBefore = null;
            _dirty = true;
            QueueRedraw();
            return;
        }
        if (_selection is not { } s)
            return;
        var before = Region(_px, _w, s);
        var (r, g, b, a) = Bytes(Right);
        for (var y = s.Position.Y; y < s.End.Y; y++)
            for (var x = s.Position.X; x < s.End.X; x++)
            {
                var i = (y * _w + x) * 4;
                _px[i] = r;
                _px[i + 1] = g;
                _px[i + 2] = b;
                _px[i + 3] = a;
            }
        Push(new Step("Delete", s, before, Region(_px, _w, s), new Vector2I(_w, _h), new Vector2I(_w, _h)));
        _dirty = true;
    }

    public void Crop()
    {
        SetDown();
        if (_selection is not { } s)
        {
            _vm.Say("Select first the part to keep (the Select tool, S)");
            return;
        }
        Transform("Crop", _ => { }, s);
    }

    // ── The picture changed as a whole ──

    /// <summary>The whole picture changed by <paramref name="change"/> (flipped, turned, resized), one step to undo.</summary>
    public void Transform(string name, Action<Image> change, Rect2I? crop = null)
    {
        SetDown();
        CancelText();
        var before = Whole();
        var image = crop is { } c ? ToImage().GetRegion(c) : ToImage();
        if (crop is null)
            change(image);
        image.Convert(Image.Format.Rgba8);
        SetPixels(image.GetData(), image.GetWidth(), image.GetHeight());
        Push(new Step(name, new Rect2I(0, 0, _w, _h), before.Px, (byte[])_px.Clone(), before.Size, new Vector2I(_w, _h)));
        _vm.Say($"{name}: {_w} × {_h} px");
    }

    private static Vector2I? ParseSize(string? text, int w, int h)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim().ToLowerInvariant().Replace(" ", "");
        if (text.EndsWith('%') && double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent) && percent > 0)
            return new Vector2I(Math.Max(1, (int)Math.Round(w * percent / 100)), Math.Max(1, (int)Math.Round(h * percent / 100)));
        var parts = text.Split('x', '×', '*', ',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var nw) && int.TryParse(parts[1], out var nh) && nw > 0 && nh > 0 && nw <= MaxSide && nh <= MaxSide)
            return new Vector2I(nw, nh);
        if (parts.Length == 2 && int.TryParse(parts[0], out nw) && parts[1].Length == 0 && nw > 0)
            return new Vector2I(nw, Math.Max(1, (int)Math.Round((double)h * nw / w)));
        return null;
    }

    public async void AskNew()
    {
        if (!await Discard())
            return;
        var text = await Dialogs.Ask("New picture", "Its size in pixels (e.g. 1280 x 800). It is filled with the right colour (transparent if the right colour is).", $"{_w} x {_h}");
        if (ParseSize(text, _w, _h) is not { } size)
            return;
        SetDown();
        CancelText();
        NewPicture(size.X, size.Y, Right, true);
        _vm.FilePath = "";
        _vm.Modified = false;
        Fit();
        _vm.Say($"A new picture, {size.X} × {size.Y} px");
    }

    public async void AskResize()
    {
        var text = await Dialogs.Ask("Resize", "The new size, stretched: 1920 x 1080, 50 %, or a width alone (1024 x) to keep the proportions.", $"{_w} x {_h}");
        if (ParseSize(text, _w, _h) is not { } size)
            return;
        Transform("Resize", i => i.Resize(size.X, size.Y, Image.Interpolation.Lanczos));
    }

    public async void AskCanvas()
    {
        var text = await Dialogs.Ask("Canvas size", "The new size of the canvas (the picture kept at the top left, not stretched; what is added takes the right colour).", $"{_w} x {_h}");
        if (ParseSize(text, _w, _h) is not { } size)
            return;
        var fill = Right;
        Transform("Canvas", i =>
        {
            var copy = (Image)i.Duplicate();
            i.Resize(size.X, size.Y, Image.Interpolation.Nearest);
            i.Fill(fill);
            i.BlitRect(copy, new Rect2I(0, 0, copy.GetWidth(), copy.GetHeight()), Vector2I.Zero);
        });
    }

    private async Task<bool> Discard() =>
        !_vm.Modified || await Dialogs.Confirm("Paint", "The picture has changed since it was saved. Leave it anyway? (It can still be got back with ↶.)", "Leave it", "Cancel");

    public async void AskOpen()
    {
        if (!await Discard())
            return;
        var folder = _vm.FilePath.Length > 0 ? Path.GetDirectoryName(_vm.FilePath) : PaintWindowViewModel.Folder;
        Directory.CreateDirectory(PaintWindowViewModel.Folder);
        var path = await Dialogs.OpenFile("Open a picture", ["*.png, *.jpg, *.jpeg, *.webp, *.bmp, *.tga, *.svg ; Pictures"], folder);
        if (path is null)
            return;
        SetDown();
        CancelText();
        if (LoadInto(path, true))
        {
            _vm.FilePath = path;
            _vm.Modified = false;
            Fit();
            _vm.Say($"Opened: {path}");
        }
    }

    public async void Save(bool asking)
    {
        SetDown();
        CommitText();
        var path = _vm.FilePath;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (asking || path.Length == 0 || ext is not (".png" or ".jpg" or ".jpeg" or ".webp"))
        {
            Directory.CreateDirectory(PaintWindowViewModel.Folder);
            var folder = path.Length > 0 ? Path.GetDirectoryName(path) : PaintWindowViewModel.Folder;
            var name = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) + ".png" : $"drawing-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var chosen = await Dialogs.SaveFile("Save the picture", name, ["*.png ; PNG", "*.jpg, *.jpeg ; JPEG", "*.webp ; WEBP"], folder);
            if (chosen is null)
                return;
            path = Path.GetExtension(chosen).Length == 0 ? chosen + ".png" : chosen;
            ext = Path.GetExtension(path).ToLowerInvariant();
        }
        try
        {
            PaintWindowViewModel.KeepOlder(path);
            var image = ToImage();
            var error = ext switch
            {
                ".jpg" or ".jpeg" => image.SaveJpg(path, 0.92f),
                ".webp" => image.SaveWebp(path, false, 0.92f),
                _ => image.SavePng(path),
            };
            if (error != Error.Ok)
                throw new IOException(error.ToString());
            _vm.FilePath = path;
            _vm.Modified = false;
            _vm.Say("Saved: " + path + (ext is ".jpg" or ".jpeg" ? " (JPEG keeps no transparency)" : ""));
        }
        catch (Exception ex)
        {
            _vm.Say("The picture could not be saved: " + ex.Message, true);
        }
    }

    // ── Undo ──

    private void Push(Step step)
    {
        _undo.Add(step);
        _redo.Clear();
        var bytes = _undo.Sum(s => (long)s.Before.Length + s.After.Length);
        while (_undo.Count > MaxUndo || (bytes > MaxUndoBytes && _undo.Count > 1))
        {
            bytes -= _undo[0].Before.Length + _undo[0].After.Length;
            _undo.RemoveAt(0);
        }
        Changed();
    }

    private void Changed()
    {
        _vm.Modified = true;
        _saveIn = 1.5;
        _dirty = true;
    }

    public void Undo()
    {
        SetDown();
        CancelText();
        if (_undo.Count == 0)
            return;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        Apply(step.Rect, step.Before, step.SizeBefore, step.SizeAfter);
        _vm.Say($"↶ {step.Name}");
    }

    public void Redo()
    {
        SetDown();
        CancelText();
        if (_redo.Count == 0)
            return;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        Apply(step.Rect, step.After, step.SizeAfter, step.SizeBefore);
        _vm.Say($"↷ {step.Name}");
    }

    private void Apply(Rect2I rect, byte[] data, Vector2I size, Vector2I otherSize)
    {
        if (size != otherSize || size != new Vector2I(_w, _h))
            SetPixels((byte[])data.Clone(), size.X, size.Y);
        else
            for (var y = 0; y < rect.Size.Y; y++)
                Buffer.BlockCopy(data, y * rect.Size.X * 4, _px, ((rect.Position.Y + y) * _w + rect.Position.X) * 4, rect.Size.X * 4);
        _selection = null;
        Changed();
    }

    private static byte[] Region(byte[] px, int w, Rect2I r)
    {
        var data = new byte[r.Size.X * r.Size.Y * 4];
        for (var y = 0; y < r.Size.Y; y++)
            Buffer.BlockCopy(px, ((r.Position.Y + y) * w + r.Position.X) * 4, data, y * r.Size.X * 4, r.Size.X * 4);
        return data;
    }

    private static (byte R, byte G, byte B, byte A) Bytes(Color c) => ((byte)c.R8, (byte)c.G8, (byte)c.B8, (byte)c.A8);

    // ── Text: typed in a box, drawn on the picture as it is typed ──

    private void StartText(Vector2 at)
    {
        _textAt = new Vector2I((int)Math.Floor(at.X), (int)Math.Floor(at.Y));
        Begin("Text", Left, Right, false);
        _textEdit = new LineEdit { PlaceholderText = "Type the text, Enter", CustomMinimumSize = new Vector2(260, 0) };
        _textEdit.Position = (ToScreen(at) - new Vector2(0, 40)).Clamp(Vector2.Zero, Size - new Vector2(260, 34));
        _textEdit.TextChanged += _ => RenderText().ContinueWith(_ => { });
        _textEdit.TextSubmitted += _ => CommitText();
        _textEdit.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                CancelText();
                GrabFocus();
            }
        };
        AddChild(_textEdit);
        _textEdit.GrabFocus();
    }

    private async Task RenderText()
    {
        if (_textEdit is null || _textPort is null || _textDrawer is null)
            return;
        var ticket = ++_textRender;
        var text = _textEdit.Text;
        var mask = await RenderMask(text, _vm.FontSize);
        if (ticket != _textRender || _snap is null || _textEdit is null)
            return;
        if (_shapeBounds.HasArea())
        {
            ClearMasks(_shapeBounds);
            Compose(_shapeBounds);
            _shapeBounds = new Rect2I();
        }
        if (mask is not { } m)
            return;
        var box = Clip(new Rect2I(_textAt, new Vector2I(m.W, m.H)));
        for (var y = box.Position.Y; y < box.End.Y; y++)
            for (var x = box.Position.X; x < box.End.X; x++)
                _mask[y * _w + x] = m.Alpha[(y - _textAt.Y) * m.W + (x - _textAt.X)];
        _shapeBounds = box;
        Grow(box);
        Compose(box);
    }

    /// <summary>The text drawn in white on a small transparent viewport, its alpha read back as a mask.</summary>
    private async Task<(float[] Alpha, int W, int H)?> RenderMask(string text, int size)
    {
        if (string.IsNullOrEmpty(text) || _textPort is null || _textDrawer is null)
            return null;
        var font = PaintTextDrawer.Font;
        var measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var w = Math.Clamp((int)Math.Ceiling(measured.X) + 4, 1, 4096);
        var h = Math.Clamp((int)Math.Ceiling(measured.Y) + 4, 1, 4096);
        _textPort.Size = new Vector2I(w, h);
        _textDrawer.Size = new Vector2(w, h);
        _textDrawer.Text = text;
        _textDrawer.FontSize = size;
        _textDrawer.QueueRedraw();
        _textPort.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _textPort.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        var image = _textPort.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        var alpha = new float[w * h];
        for (var i = 0; i < alpha.Length && i * 4 + 3 < data.Length; i++)
            alpha[i] = data[i * 4 + 3] / 255f;
        return (alpha, w, h);
    }

    private void CommitText()
    {
        if (_textEdit is null)
            return;
        var edit = _textEdit;
        _textEdit = null;
        edit.QueueFree();
        _textRender++;
        End();
    }

    private void CancelText()
    {
        if (_textEdit is null)
            return;
        var edit = _textEdit;
        _textEdit = null;
        edit.QueueFree();
        _textRender++;
        if (_snap is not null)
        {
            Buffer.BlockCopy(_snap, 0, _px, 0, _px.Length);
            _snap = null;
            _dirty = true;
        }
    }

    // ── --paint-demo (tests) ──

    private async Task Demo()
    {
        NewPicture(900, 560, Colors.White, false);
        Fit();
        void Stroke(string tool, Color colour, params Vector2[] points)
        {
            _vm.Tool = tool;
            Begin(tool, colour, Colors.White, tool == "Eraser");
            Segment(points[0], points[0]);
            for (var i = 1; i < points.Length; i++)
                Segment(points[i - 1], points[i]);
            End();
        }
        _vm.Size = 24;
        _vm.Hardness = 20;
        _vm.Opacity = 60;
        Stroke("Brush", new Color(0.9f, 0.2f, 0.2f), new(60, 80), new(200, 60), new(320, 140), new(120, 160), new(60, 80));
        _vm.Opacity = 100;
        _vm.Size = 3;
        Stroke("Pencil", Colors.Black, new(40, 220), new(380, 260));
        _vm.ShapeMode = "Both";
        _vm.Size = 6;
        _vm.Tool = "Rectangle";
        Begin("Rectangle", new Color(0.1f, 0.3f, 0.8f), new Color(1f, 0.85f, 0.2f), false);
        _start = new Vector2(420, 40);
        Shape(new Vector2(600, 120), false);
        Shape(new Vector2(640, 200), false);
        End();
        _vm.ShapeMode = "Outline";
        _vm.Tool = "Ellipse";
        Begin("Ellipse", new Color(0.1f, 0.6f, 0.2f), Colors.White, false);
        _start = new Vector2(660, 40);
        Shape(new Vector2(860, 220), false);
        End();
        _vm.Tool = "Fill";
        _vm.Tolerance = 10;
        Begin("Fill", new Color(0.6f, 0.9f, 1f), Colors.White, false);
        Fill(new Vector2(760, 130));
        End();
        _vm.Tool = "Line";
        _vm.Size = 10;
        Begin("Line", new Color(0.5f, 0.2f, 0.6f), Colors.White, false);
        _start = new Vector2(40, 300);
        Shape(new Vector2(400, 520), true);
        End();
        _vm.Size = 40;
        _vm.Tool = "Spray";
        Begin("Spray", new Color(1f, 0.5f, 0f), Colors.White, false);
        for (var i = 0; i < 20; i++)
            Spray(new Vector2(480 + i * 8, 380));
        End();
        _vm.Size = 30;
        _vm.Hardness = 100;
        Stroke("Eraser", Colors.White, new(80, 100), new(300, 110));
        _vm.FontSize = 48;
        _vm.Tool = "Text";
        Begin("Text", Colors.Black, Colors.White, false);
        _textAt = new Vector2I(440, 460);
        var mask = await RenderMask("Kaonomi Paint ✓", 48);
        if (mask is { } m)
        {
            var box = Clip(new Rect2I(_textAt, new Vector2I(m.W, m.H)));
            for (var y = box.Position.Y; y < box.End.Y; y++)
                for (var x = box.Position.X; x < box.End.X; x++)
                    _mask[y * _w + x] = m.Alpha[(y - _textAt.Y) * m.W + (x - _textAt.X)];
            Grow(box);
            Compose(box);
        }
        End();
        _vm.Tool = "Select";
        _selection = new Rect2I(420, 40, 221, 161);
        Lift(_selection.Value);
        MoveFloat(new Vector2I(440, 260));
        SetDown();
        Undo();
        Redo();
        _vm.Say($"Demo drawn: {_undo.Count} steps to undo");
    }
}

/// <summary>What draws the text, in white, in the small viewport whose alpha becomes the mask of the text.</summary>
public sealed partial class PaintTextDrawer : Control
{
    public static readonly SystemFont Font = new()
    {
        FontNames = ["Helvetica Neue", "Arial", "Helvetica", "DejaVu Sans", "sans-serif"],
        Antialiasing = TextServer.FontAntialiasing.Gray,
        Fallbacks = [new SystemFont { FontNames = ["Apple Color Emoji", "Segoe UI Emoji", "Noto Color Emoji"] }],
    };

    public string Text { get; set; } = "";

    public int FontSize { get; set; } = 32;

    public override void _Draw() =>
        DrawString(Font, new Vector2(2, 2 + Font.GetAscent(FontSize)), Text, HorizontalAlignment.Left, -1, FontSize, Colors.White);
}
