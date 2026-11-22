using System;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The shimmer of a transporter, as in the old space series: for a moment, columns of bright sparks glitter over
/// what leaves or arrives, thick in the middle of the effect and gone at its end. It only draws the sparks: what is
/// under it is faded out or in by who uses it (<see cref="Out"/> and <see cref="In"/> do both). It frees itself
/// when it is over.
/// </summary>
public partial class TransporterBeam : Control
{
    /// <summary>How long a transport takes, each way, in seconds.</summary>
    public const double Seconds = 0.9;

    private readonly Random _random = new();
    private readonly double _duration;
    private double _time;

    // Godot builds its scripts with this constructor.
    public TransporterBeam() : this(Seconds)
    {
    }

    public TransporterBeam(double duration)
    {
        _duration = duration;
        MouseFilter = MouseFilterEnum.Ignore;
        // The sparks add their light to what is under them.
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        ZIndex = 60;
    }

    /// <summary>Dematerialises <paramref name="what"/>: the sparks over it while it fades out, then <paramref name="gone"/>.</summary>
    public static void Out(Control what, Action gone)
    {
        var beam = new TransporterBeam();
        what.AddChild(beam);
        beam.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var tween = what.CreateTween();
        // It holds a moment under the sparks, then fades.
        tween.TweenInterval(Seconds * 0.25);
        tween.TweenProperty(what, "modulate:a", 0f, Seconds * 0.75).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(gone));
    }

    /// <summary>Materialises <paramref name="what"/> (made transparent first) in <paramref name="where"/>: the sparks over that place while it fades in.</summary>
    public static void In(Control where, Control what)
    {
        what.Modulate = what.Modulate with { A = 0 };
        var beam = new TransporterBeam();
        where.AddChild(beam);
        beam.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var tween = what.CreateTween();
        tween.TweenInterval(Seconds * 0.2);
        tween.TweenProperty(what, "modulate:a", 1f, Seconds * 0.8).SetTrans(Tween.TransitionType.Sine);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_time >= _duration)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Thick in the middle of the effect, nothing at its start and its end.
        var strength = MathF.Sin((float)(_time / _duration) * MathF.PI);
        var size = Size;
        if (size.X < 2 || size.Y < 2)
            return;
        // The sparks stand in a column in the middle of the place, as a figure would.
        var count = (int)(size.X * size.Y / 260 * strength);
        var half = size.X * 0.32f;
        for (var i = 0; i < count; i++)
        {
            // Twice the sum of two: more sparks towards the middle.
            var x = size.X / 2 + ((float)_random.NextDouble() + (float)_random.NextDouble() - 1) * half;
            var y = (float)_random.NextDouble() * size.Y;
            var length = 3 + (float)_random.NextDouble() * 22;
            var glow = (float)_random.NextDouble();
            var color = glow > 0.75f ? new Color(1f, 1f, 1f) : glow > 0.35f ? new Color(1f, 0.86f, 0.45f) : new Color(0.55f, 0.85f, 1f);
            DrawRect(new Rect2(x, y, 1.6f, length), color with { A = strength * (0.25f + 0.6f * glow) });
        }
    }
}
