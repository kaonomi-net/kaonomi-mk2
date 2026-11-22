using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Pedago;

/// <summary>
/// /pedago:test: the picture of a kitchen. A click on an object writes its name over it at once (the picture is known:
/// no model is asked for the name) and the chat says what it is used for (the model of the chat, for a child). Top:
/// the title of the scene, the score (successes, objects found, clicks beside), SHOW THE OUTLINES, RESET. Under the
/// picture: the name, what the model said, the status.
/// </summary>
[ToolView(typeof(PedagoWindowViewModel), "Pedago · name the objects", "🧒", 1320, 900)]
public sealed class PedagoView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (PedagoWindowViewModel)viewModel;

        var score = Eve.Eve.Text_(string.Empty, Eve.Eve.TextBright, 15);
        Bind.When(score, vm, () => score.Text = vm.Score, nameof(vm.Score));
        var bar = Ui.Row(14,
            Eve.Eve.Caption(vm.Title.ToUpperInvariant(), Eve.Eve.TextBright, 16),
            Ui.Fill(score),
            Ui.Check("Show the outlines", vm, nameof(vm.ShowAll), "Draw the outline of every object known in the picture"),
            Ui.Command("↺ RESET", vm.ResetCommand, false, "Start again: the counts back to zero"));

        var name = Eve.Eve.Text_(string.Empty, Eve.Eve.Cyan, 26);
        Bind.When(name, vm, () => name.Text = PedagoWindowViewModel.Sentence(vm.Name), nameof(vm.Name));
        var explanation = Eve.Eve.Text_(string.Empty, Eve.Eve.Text, 16, wrap: true);
        explanation.CustomMinimumSize = new Vector2(0, 66);
        Bind.When(explanation, vm, () => explanation.Text = vm.IsExplaining ? "…" : vm.Explanation, nameof(vm.Explanation), nameof(vm.IsExplaining));
        var status = Eve.Eve.Text_(string.Empty, Eve.Eve.Muted, 13, wrap: true);
        Bind.When(status, vm, () =>
        {
            status.Text = vm.Status;
            status.AddThemeColorOverride("font_color", vm.IsError ? Eve.Eve.Red : Eve.Eve.Muted);
        }, nameof(vm.Status), nameof(vm.IsError));

        var page = Ui.Column(8, bar, Ui.FillHeight(new PedagoCanvas(vm)), name, explanation, status);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return page;
    }
}

/// <summary>
/// The picture of the scene, as large as the window lets it be, and over it: the outline and the name of the object
/// clicked (a cross where a click fell beside every object), every outline when asked. A click gives the place to the
/// view model, as shares of the width and of the height of the picture.
/// </summary>
public sealed partial class PedagoCanvas : Control
{
    private static readonly Color Found = new(0.12f, 0.72f, 0.36f);
    private static readonly Color Missed = new(0.90f, 0.22f, 0.20f);

    private readonly PedagoWindowViewModel _vm;
    private readonly ImageTexture? _texture;

    public PedagoCanvas(PedagoWindowViewModel vm)
    {
        _vm = vm;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(480, 268);
        ClipContents = true;
        if (vm.Scene is { } scene && Image.LoadFromFile(scene.ImagePath) is { } image && !image.IsEmpty())
            _texture = ImageTexture.CreateFromImage(image);
        PropertyChangedEventHandler changed = (_, _) => Bind.OnMain(() =>
        {
            if (IsInstanceValid(this))
                QueueRedraw();
        });
        _vm.PropertyChanged += changed;
        TreeExiting += () => _vm.PropertyChanged -= changed;
        Resized += QueueRedraw;
    }

    /// <summary>Where the picture is drawn: the largest rectangle of its shape in the control, at its top, centred across.</summary>
    private Rect2 Frame()
    {
        if (_vm.Scene is not { } scene)
            return new Rect2(Vector2.Zero, Size);
        var scale = Math.Min(Size.X / scene.Width, Size.Y / scene.Height);
        var size = new Vector2(scene.Width, scene.Height) * scale;
        return new Rect2(new Vector2((Size.X - size.X) / 2, 0), size);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press || _vm.Scene is null)
            return;
        var frame = Frame();
        AcceptEvent();
        if (!frame.HasPoint(press.Position) || frame.Size.X <= 0)
            return;
        var at = (press.Position - frame.Position) / frame.Size;
        _vm.Click(at.X, at.Y);
    }

    public override void _Draw()
    {
        var frame = Frame();
        if (_vm.Scene is not { } scene || _texture is null)
        {
            DrawString(Eve.Eve.Body, new Vector2(12, 28), _vm.Status, HorizontalAlignment.Left, Size.X - 24, 14, Eve.Eve.Red);
            return;
        }
        DrawTextureRect(_texture, frame, false);
        var scale = frame.Size.X / scene.Width;
        Vector2 Point(double[] p) => frame.Position + new Vector2((float)p[0], (float)p[1]) * scale;

        if (_vm.ShowAll)
            foreach (var thing in scene.Objects)
                Outline(thing, Point, _vm.WasFound(thing) ? Found : new Color(0.10f, 0.35f, 0.85f), 2, fill: false);

        if (_vm.Selected is { } selected)
        {
            Outline(selected, Point, Found, 4, fill: true);
            // The name over the object: above its outline, inside the picture.
            var points = selected.Shapes.SelectMany(s => s).Select(Point).ToList();
            var (left, right, top, bottom) = (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
            Tag(PedagoWindowViewModel.Sentence(_vm.Name), new Vector2((left + right) / 2, top), bottom, frame, Found);
        }
        else if (_vm.LastClick is { } click)
        {
            var at = frame.Position + new Vector2((float)click.X, (float)click.Y) * frame.Size;
            var arm = Math.Max(10, frame.Size.X * 0.012f);
            DrawLine(at - new Vector2(arm, arm), at + new Vector2(arm, arm), Missed, 5, true);
            DrawLine(at - new Vector2(arm, -arm), at + new Vector2(arm, -arm), Missed, 5, true);
        }
    }

    private void Outline(PedagoObject thing, Func<double[], Vector2> point, Color color, float width, bool fill)
    {
        foreach (var shape in thing.Shapes)
        {
            var points = shape.Select(point).ToArray();
            if (fill)
                DrawColoredPolygon(points, new Color(color, 0.22f));
            DrawPolyline([.. points, points[0]], color, width, true);
        }
    }

    /// <summary>A name on a plate, centred on <paramref name="anchor"/> and above it (under the object when there is no room above).</summary>
    private void Tag(string text, Vector2 anchor, float bottom, Rect2 frame, Color color)
    {
        if (text.Length == 0)
            return;
        var size = (int)Math.Clamp(frame.Size.X * 0.022f, 16, 34);
        var font = Eve.Eve.Body;
        var extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var pad = new Vector2(size * 0.5f, size * 0.28f);
        var plate = new Rect2(anchor.X - extent.X / 2 - pad.X, anchor.Y - extent.Y - 2 * pad.Y - 8, extent.X + 2 * pad.X, extent.Y + 2 * pad.Y);
        if (plate.Position.Y < frame.Position.Y + 4)
            plate.Position = new Vector2(plate.Position.X, Math.Min(bottom + 8, frame.End.Y - plate.Size.Y - 4));
        plate.Position = new Vector2(Math.Clamp(plate.Position.X, frame.Position.X + 4, Math.Max(frame.Position.X + 4, frame.End.X - plate.Size.X - 4)), plate.Position.Y);
        DrawRect(plate, new Color(1, 1, 1, 0.95f));
        DrawRect(plate, color, false, 3);
        DrawString(font, new Vector2(plate.Position.X + pad.X, plate.Position.Y + pad.Y + font.GetAscent(size)), text, HorizontalAlignment.Left, -1, size, new Color(0.08f, 0.10f, 0.12f));
    }
}
