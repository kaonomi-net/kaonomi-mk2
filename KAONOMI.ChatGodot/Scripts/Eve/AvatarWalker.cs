using System;
using Godot;
using KAONOMI.Chat.Models;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The 3D animation out of its frame: it gets a body and walks over the desktop. Its head is the animation itself
/// (the face in relief, which turns the way it goes). Its body is the one drawn for it (<see cref="BodyPuppet"/>:
/// the picture of its whole body, made from its face, cut along its joints into a paper puppet whose pieces turn in
/// depth): it really walks, its legs step with the ground it covers towards where it goes, its arms swing, it bobs,
/// and it turns a little the way it goes. An animation that has no picture of its body (JARVIS, or a face added
/// since) gets a small robot body in the colours of the theme instead, which turns fully: in profile to cross the
/// desktop, its back to us to go up the screen, facing us to come down. An animation whose picture is already its whole
/// body gets none and goes as it is, larger than a head, turning round to face the way it goes: KINGYO (the fish) swims
/// to and fro across the whole width of the screen, rising and sinking as it goes (caught by its body only, so that the
/// windows it passes over still take the clicks); BENI (the small robot) hops along on its own legs. Any other goes, each time, just beside the window in use (on its left when there is
/// room, else on its right, above or under it; in its corner when the window fills the desktop), follows it when it
/// is moved or when another window becomes the one in use, and turns towards it once arrived. With no window, it
/// waits in the corner of the desktop next to the chat. A double-click on it, or on its empty frame, sends it back
/// in its frame by a transporter: it glitters away where it stands and appears in its frame the same way.
/// </summary>
public partial class AvatarWalker : Control
{
    /// <summary>Its width and height, the side of its head, and its distance to the window it stands beside.</summary>
    private const float Wide = 250, High = 400, Head = 170, Gap = 10;

    /// <summary>Where the body is drawn under the head (it starts behind the chin), and the pixels walked for a full stride (two steps).</summary>
    private const float BodyTop = 112, Stride = 140;

    /// <summary>
    /// How an animation whose picture is already its whole body goes out of the frame (no body is added to it): it swims
    /// (a fish) or hops on its own legs; which way its picture looks (1: to the left of the screen, -1: to the right);
    /// the side of its picture (a square) and where its top is in the walker.
    /// </summary>
    private sealed record WholeBody(bool Swims, float Looks, float Side, float Top);

    /// <summary>The way <paramref name="view"/> goes when its picture is its whole body; null when it gets a body.</summary>
    private static WholeBody? WholeBodyOf(Control view) => view is not FaceAvatar face ? null : face.Animation switch
    {
        SceneAnimation.Kingyo => new WholeBody(Swims: true, Looks: 1, Side: Wide, Top: 120),
        // The robot standing on the bottom of the walker (its feet at the level of the bottom of the window).
        SceneAnimation.Beni => new WholeBody(Swims: false, Looks: -1, Side: 340, Top: High - 340),
        _ => null
    };

    /// <summary>How fast a swimmer crosses the screen on its tour (across, in the units of the interface per second).</summary>
    private const float RoamSpeed = 110;

    private WholeBody? _whole;
    private float _roam = -1;       // where a swimmer is on its tour (see Roam); -1: not started
    private float _flip = 1;        // its picture as it is (1) or mirrored (-1), through 0 as it turns round
    private float _side = 1;        // the way it turns to (1 or -1)
    private float _facing;          // where it faces now (radians, 0 towards us, positive to the right of the screen)

    private readonly WindowDeck? _deck;
    private Vector2 _place;         // its top left corner, in the coordinates of its parent
    private Vector2 _velocity;
    private double _time;
    private bool _returning;

    // The body: the paper puppet of the animation when it has one, else the small robot made of simple shapes.
    private BodyPuppet? _puppet;
    private Camera3D? _camera;
    private Control? _bodyView;
    private Node3D? _body, _hipLeft, _hipRight, _kneeLeft, _kneeRight, _shoulderLeft, _shoulderRight, _elbowLeft, _elbowRight, _chest;
    private float _phase;           // where it is in its stride, in radians
    private float _walk;            // 0: standing, 1: walking
    private float _yaw;             // where its body faces: 0 towards us, +90° to the right of the screen

    // Godot builds its scripts with this constructor.
    public AvatarWalker()
    {
    }

    /// <summary>The walker of <paramref name="view"/>, which leaves the frame at <paramref name="from"/> (in the coordinates of the screen).</summary>
    public AvatarWalker(WindowDeck deck, Control view, Rect2 from)
    {
        _deck = deck;
        View = view;
        _whole = WholeBodyOf(view);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Double-click: back in its frame";
        Size = new Vector2(Wide, High);
        Position = _place = StartFor(from, _whole);
        ZIndex = 50;
    }

    /// <summary>The animation it carries: its head.</summary>
    public Control? View { get; private set; }

    /// <summary>Where it is told to go instead of beside the window in use (the top left corner of its place, in the coordinates of its parent); null: beside the window.</summary>
    public Func<Vector2>? Destination { get; set; }

    /// <summary>Raised by a double-click on it: it is asked to go back in its frame.</summary>
    public event Action? HomeRequested;

    /// <summary>Its place when its head (or its whole picture) is in the middle of the frame <paramref name="frame"/>.</summary>
    private static Vector2 StartFor(Rect2 frame, WholeBody? whole) =>
        frame.GetCenter() - (whole is null ? new Vector2(Wide / 2, Head / 2) : new Vector2(Wide / 2, whole.Top + whole.Side / 2));

    public override void _Ready()
    {
        BuildBody();
        // The body appears as it leaves the frame.
        if (_bodyView is { } body)
        {
            body.Modulate = new Color(1, 1, 1, 0);
            CreateTween().TweenProperty(body, "modulate:a", 1f, 0.4);
        }
    }

    /// <summary>Puts the animation at its place: the head, over the body.</summary>
    public void Seat(Control view)
    {
        view.SetAnchorsPreset(LayoutPreset.TopLeft);
        if (_whole is { } whole)
        {
            // Its whole picture, mirrored about its middle and turned (Pose): a swimmer about its middle, a robot about
            // its feet.
            view.Position = new Vector2((Wide - whole.Side) / 2, whole.Top);
            view.Size = new Vector2(whole.Side, whole.Side);
            view.PivotOffset = new Vector2(whole.Side / 2, whole.Swims ? whole.Side / 2 : whole.Side);
            view.Scale = Vector2.One;
            view.Rotation = 0;
        }
        // On the puppet, the head is put where the face is in its body, at each frame (Pose).
        else if (_puppet is null)
        {
            view.Position = new Vector2((Wide - Head) / 2, 0);
            view.Size = new Vector2(Head, Head);
        }
        // In front of the body: the chin hides the neck.
        if (view.GetParent() == this)
            MoveChild(view, GetChildCount() - 1);
    }

    /// <summary>Replaces the animation it carries (the preset changed while it is out).</summary>
    public void Swap(Control view)
    {
        if (View is { } old && IsInstanceValid(old))
            old.QueueFree();
        View = view;
        AddChild(view);
        _whole = WholeBodyOf(view);
        _flip = _side = 1;
        _roam = -1;
        // The body of the new animation.
        var shown = _bodyView?.Modulate ?? Colors.White;
        if (_bodyView is { } former && IsInstanceValid(former))
            former.Free();
        _bodyView = null;
        _puppet = null;
        BuildBody();
        if (_bodyView is { } body)
            body.Modulate = shown;
        Seat(view);
    }

    /// <summary>
    /// Leaves the desktop as by a transporter: it stops where it is, glitters and fades away (<see cref="TransporterBeam"/>),
    /// then <paramref name="gone"/> (who called puts the animation back in its frame).
    /// </summary>
    public void BeamOut(Action gone)
    {
        if (_returning)
            return;
        _returning = true;
        _walk = 0;
        TransporterBeam.Out(this, gone);
    }

    // ── The body ──

    /// <summary>
    /// The body, in a 3D scene of its own drawn under the head: units of about a metre, the feet on y = 0, the neck at
    /// 1.33, facing +z (towards the camera) when it does not turn.
    /// </summary>
    private void BuildBody()
    {
        _body = _hipLeft = _hipRight = _kneeLeft = _kneeRight = _shoulderLeft = _shoulderRight = _elbowLeft = _elbowRight = _chest = null;
        _puppet = null;
        // Its picture is its whole body: nothing is added.
        if (_whole is not null)
            return;
        _puppet = View is FaceAvatar picture ? BodyPuppet.Load(picture.PicturePath) : null;
        if (_puppet is not null)
        {
            BuildPuppet(_puppet);
            return;
        }
        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(0, BodyTop), Size = new Vector2(Wide, High - BodyTop) };
        AddChild(container);
        MoveChild(container, 0);
        _bodyView = container;
        var scene = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
        container.AddChild(scene);
        scene.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f
            }
        });
        var light = new DirectionalLight3D { LightEnergy = 2.2f };
        scene.AddChild(light);
        light.RotationDegrees = new Vector3(-35, 30, 0);
        // The camera sees from under the feet to over the neck.
        const float middle = 0.71f, far = 3.4f;
        var camera = new Camera3D { Fov = 30, Near = 0.1f, Far = 20, Current = true, Position = new Vector3(0, middle, far) };
        scene.AddChild(camera);

        var shell = new StandardMaterial3D
        {
            AlbedoColor = Eve.IsLight ? new Color(0.78f, 0.82f, 0.88f) : new Color(0.16f, 0.2f, 0.26f), Metallic = 0.65f, Roughness = 0.35f,
            EmissionEnabled = true, Emission = Eve.Cyan, EmissionEnergyMultiplier = 0.05f
        };
        var glow = new StandardMaterial3D { AlbedoColor = Eve.Cyan, EmissionEnabled = true, Emission = Eve.Cyan, EmissionEnergyMultiplier = 1.6f, Roughness = 0.4f };

        _body = new Node3D();
        scene.AddChild(_body);
        _chest = new Node3D { Position = new Vector3(0, 0.74f, 0) };
        _body.AddChild(_chest);
        // The torso, wider at the shoulders, with a light on the chest; the neck; the hips.
        _chest.AddChild(Part(new BoxMesh { Size = new Vector3(0.58f, 0.34f, 0.3f) }, shell, new Vector3(0, 0.36f, 0)));
        _chest.AddChild(Part(new BoxMesh { Size = new Vector3(0.44f, 0.2f, 0.26f) }, shell, new Vector3(0, 0.1f, 0)));
        _chest.AddChild(Part(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.07f, Height = 0.12f }, glow, new Vector3(0, 0.58f, 0)));
        _chest.AddChild(Part(new SphereMesh { Radius = 0.05f, Height = 0.1f }, glow, new Vector3(0, 0.38f, 0.155f)));
        _body.AddChild(Part(new BoxMesh { Size = new Vector3(0.46f, 0.13f, 0.27f) }, shell, new Vector3(0, 0.7f, 0)));

        (_hipLeft, _kneeLeft) = Limb(_body, new Vector3(-0.13f, 0.68f, 0), 0.095f, 0.34f, 0.08f, 0.32f, shell, glow, foot: true);
        (_hipRight, _kneeRight) = Limb(_body, new Vector3(0.13f, 0.68f, 0), 0.095f, 0.34f, 0.08f, 0.32f, shell, glow, foot: true);
        (_shoulderLeft, _elbowLeft) = Limb(_chest, new Vector3(-0.37f, 0.46f, 0), 0.07f, 0.28f, 0.06f, 0.26f, shell, glow, foot: false);
        (_shoulderRight, _elbowRight) = Limb(_chest, new Vector3(0.37f, 0.46f, 0), 0.07f, 0.28f, 0.06f, 0.26f, shell, glow, foot: false);
    }

    /// <summary>The scene of the paper puppet: it fills the walker, the camera sees it from its feet to over its head.</summary>
    private void BuildPuppet(BodyPuppet puppet)
    {
        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore, Position = Vector2.Zero, Size = new Vector2(Wide, High) };
        AddChild(container);
        MoveChild(container, 0);
        _bodyView = container;
        var scene = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
        container.AddChild(scene);
        // A little more than the body, for its bobbing and the legs that come forward.
        const float seen = BodyPuppet.High * 1.12f, fov = 28;
        var far = seen / 2 / MathF.Tan(Mathf.DegToRad(fov / 2));
        _camera = new Camera3D { Fov = fov, Near = 0.1f, Far = 30, Current = true, Position = new Vector3(0, BodyPuppet.High * 0.5f, far) };
        scene.AddChild(_camera);
        _body = new Node3D();
        scene.AddChild(_body);
        _body.AddChild(puppet.Root);
    }

    /// <summary>The pose of the paper puppet, and its head put where its face is.</summary>
    private void PosePuppet(BodyPuppet puppet)
    {
        if (_body is null)
            return;
        // A flat puppet turns only a little the way it goes: seen from the side it would be a line.
        var turned = Math.Clamp(Mathf.Wrap(_yaw, -MathF.PI, MathF.PI), -0.5f, 0.5f);
        // Its limbs swing along the way it goes: across the desktop, towards us or away. The axis they turn about
        // is across that way, level; given in the units of the body, which is itself turned.
        var way = _velocity.Length() > 8 ? new Vector3(_velocity.X, 0, _velocity.Y).Normalized() : new Vector3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
        var axis = new Vector3(way.Z, 0, -way.X).Rotated(Vector3.Up, -turned);
        _body.Rotation = new Vector3(0, turned, 0);
        puppet.Pose(_phase, _walk, axis.Normalized(), Mathf.Lerp(1f, 0.6f, Math.Abs(way.X)), (float)_time);

        if (View is not { } view || !IsInstanceValid(view) || _camera is null || _bodyView is null)
            return;
        // The animation, over the picture of its face in the body: the corners of that picture, seen by the camera.
        var place = _body.Transform * puppet.Root.Transform;
        var topLeft = _camera.UnprojectPosition(place * puppet.FaceCorners[0]);
        var bottomRight = _camera.UnprojectPosition(place * puppet.FaceCorners[1]);
        var rect = new Rect2(topLeft, bottomRight - topLeft).Abs();
        // The animation shows its picture a little smaller than its frame (its camera stands back): the frame is that much larger.
        const float shown = 0.947f;
        var size = rect.Size / shown;
        view.Position = _bodyView.Position + rect.GetCenter() - size / 2;
        view.Size = size;
        if (view is FaceAvatar face)
            face.Yaw = turned;
    }

    private static MeshInstance3D Part(Mesh mesh, Material material, Vector3 position) =>
        new() { Mesh = mesh, MaterialOverride = material, Position = position };

    /// <summary>
    /// A limb of two parts hanging from <paramref name="at"/>: the pivot of its top (hip, shoulder) and the pivot of
    /// its joint (knee, elbow), each turned about x to swing it; a glowing ball at the joint, a foot at the end of a leg.
    /// </summary>
    private static (Node3D Top, Node3D Joint) Limb(Node3D parent, Vector3 at, float radius, float length, float lowerRadius, float lowerLength,
        Material shell, Material glow, bool foot)
    {
        var top = new Node3D { Position = at };
        parent.AddChild(top);
        top.AddChild(Part(new CapsuleMesh { Radius = radius, Height = length + radius * 2 }, shell, new Vector3(0, -length / 2, 0)));
        var joint = new Node3D { Position = new Vector3(0, -length, 0) };
        top.AddChild(joint);
        joint.AddChild(Part(new SphereMesh { Radius = radius * 1.08f, Height = radius * 2.16f }, glow, Vector3.Zero));
        joint.AddChild(Part(new CapsuleMesh { Radius = lowerRadius, Height = lowerLength + lowerRadius * 2 }, shell, new Vector3(0, -lowerLength / 2, 0)));
        if (foot)
            joint.AddChild(Part(new BoxMesh { Size = new Vector3(0.16f, 0.08f, 0.3f) }, shell, new Vector3(0, -lowerLength - 0.02f, 0.06f)));
        else
            joint.AddChild(Part(new SphereMesh { Radius = 0.075f, Height = 0.15f }, shell, new Vector3(0, -lowerLength - 0.03f, 0)));
        return (top, joint);
    }

    /// <summary>
    /// An animation that is its whole picture: it turns round to face the way it goes, then the window once arrived (its
    /// picture mirrored, through a thin edge as if it turned). A swimmer rises and sinks gently, its nose up as it rises
    /// and the way it goes up or down; a robot hops on its own legs at each step, waddling, leaning a little the way it
    /// goes, and breathes standing.
    /// </summary>
    private void PoseWhole(WholeBody whole, float delta)
    {
        if (View is not { } view || !IsInstanceValid(view))
            return;
        var across = MathF.Sin(_facing);
        if (MathF.Abs(across) > 0.12f)
            _side = across > 0 ? -whole.Looks : whole.Looks;
        _flip = Mathf.MoveToward(_flip, _side, delta * 4);
        view.Scale = new Vector2(_flip, 1);
        var x = (Wide - whole.Side) / 2;
        if (whole.Swims)
        {
            var swing = (float)_time * 1.7f;
            view.Position = new Vector2(x, whole.Top + MathF.Sin(swing) * (5 + 3 * _walk));
            // Turning clockwise lifts the nose of a picture looking to the left.
            var looksLeft = whole.Looks * MathF.Sign(_flip);
            view.Rotation = -looksLeft * (0.06f * MathF.Cos(swing) + Math.Clamp(_velocity.Y / 900f, -0.3f, 0.3f));
        }
        else
        {
            var hop = MathF.Abs(MathF.Sin(_phase)) * 14 * _walk;
            var breath = MathF.Sin((float)_time * 1.6f) * 1.5f * (1 - _walk);
            view.Position = new Vector2(x, whole.Top - hop + breath);
            view.Rotation = Math.Clamp(_velocity.X / 2500f, -0.08f, 0.08f) + 0.035f * MathF.Sin(_phase) * _walk;
        }
        if (view is FaceAvatar face)
            face.Yaw = 0;
    }

    /// <summary>The pose of the body: the stride (legs and arms), the bobbing, and where it faces.</summary>
    private void Pose(float delta)
    {
        if (_whole is { } whole)
        {
            PoseWhole(whole, delta);
            return;
        }
        if (_puppet is { } puppet)
        {
            PosePuppet(puppet);
            return;
        }
        if (_body is null)
            return;
        var swing = MathF.Sin(_phase);
        // A leg swings forward (a turn about x that is negative) and its knee bends as it comes back; the arms the other way.
        Turn(_hipLeft, -0.62f * swing * _walk);
        Turn(_hipRight, 0.62f * swing * _walk);
        Turn(_kneeLeft, (0.15f + 0.75f * MathF.Max(0, MathF.Sin(_phase - 1.9f))) * _walk);
        Turn(_kneeRight, (0.15f + 0.75f * MathF.Max(0, MathF.Sin(_phase - 1.9f + MathF.PI))) * _walk);
        Turn(_shoulderLeft, 0.5f * swing * _walk);
        Turn(_shoulderRight, -0.5f * swing * _walk);
        Turn(_elbowLeft, -0.25f - 0.35f * _walk);
        Turn(_elbowRight, -0.25f - 0.35f * _walk);
        // It bobs at each step and leans a little forward when it walks; standing, it breathes.
        var breath = MathF.Sin((float)_time * 1.6f) * 0.006f * (1 - _walk);
        _body.Position = new Vector3(0, 0.03f * MathF.Abs(MathF.Cos(_phase)) * _walk + breath, 0);
        _body.Rotation = new Vector3(0, _yaw, 0);
        if (_chest is not null)
            _chest.Rotation = new Vector3(0.1f * _walk, 0.12f * swing * _walk, 0);
        // The head turns as the body does, as far as a face in relief can: it has no profile and no back, so it
        // only looks to the side it goes.
        if (View is FaceAvatar face && IsInstanceValid(face))
            face.Yaw = Math.Clamp(Mathf.Wrap(_yaw, -MathF.PI, MathF.PI), -0.55f, 0.55f);
    }

    private static void Turn(Node3D? pivot, float angle)
    {
        if (pivot is not null)
            pivot.Rotation = new Vector3(angle, 0, 0);
    }

    // ── Where it goes ──

    /// <summary>The window it stands beside (in the coordinates of the screen), or null when there is none.</summary>
    private Rect2? WindowInUse(Rect2 desk)
    {
        if (_deck?.Front is not { } window)
            return null;
        var rect = _deck.RectOf(window);
        rect.Position += desk.Position;
        return rect;
    }

    /// <summary>
    /// Where a swimmer goes now, on its tour of the screen (in the coordinates of its parent): to and fro across its whole
    /// width (slowing down to turn at each side), rising and sinking as it goes along two waves of different lengths, so
    /// that it never goes the same way twice. <see cref="_roam"/> grows with the time: from 0 to 1 it crosses to the
    /// right, from 1 to 2 back to the left.
    /// </summary>
    private Vector2 Roam(WholeBody whole, float step)
    {
        var screen = GetViewportRect();
        var origin = GetParent() is Control parent ? parent.GlobalPosition : Vector2.Zero;
        var left = screen.Position.X + 8;
        var right = Math.Max(left + 1, screen.End.X - Wide - 8);
        // Its picture inside the screen, from its top to its bottom.
        var top = screen.Position.Y - whole.Top + 10;
        var bottom = Math.Max(top + 1, screen.End.Y - whole.Top - whole.Side - 10);
        if (_roam < 0)
        {
            // It starts on its way back to the left, from where it is across the screen.
            var share = Math.Clamp((_place.X + origin.X - left) / (right - left), 0, 1);
            _roam = 2 - MathF.Acos(1 - 2 * share) / MathF.PI;
        }
        // Wrapped at 200, where both waves and the crossings are back where they started.
        _roam = (_roam + step * RoamSpeed / (right - left)) % 200;
        var across = 0.5f - 0.5f * MathF.Cos(MathF.PI * _roam);
        var depth = 0.5f + 0.32f * MathF.Sin(MathF.Tau * 1.5f * _roam) + 0.15f * MathF.Sin(MathF.Tau * 0.37f * _roam + 1);
        return new Vector2(Mathf.Lerp(left, right, across), Mathf.Lerp(top, bottom, Math.Clamp(depth, 0, 1))) - origin;
    }

    /// <summary>
    /// Where it stands now (in the coordinates of its parent): beside the window in use, inside the desktop; a swimmer
    /// tours the whole screen instead.
    /// </summary>
    private Vector2 Target(float step)
    {
        if (Destination is { } told)
            return told();
        if (_whole is { Swims: true } swimmer)
            return Roam(swimmer, step);
        if (_deck is null || !IsInstanceValid(_deck))
            return _place;
        var desk = _deck.GetGlobalRect();
        var origin = GetParent() is Control parent ? parent.GlobalPosition : Vector2.Zero;
        Vector2 spot;
        if (WindowInUse(desk) is { } rect)
        {
            // Its feet on the level of the bottom of the window when it stands beside it.
            var y = rect.End.Y - High;
            if (rect.Position.X - Wide - Gap >= desk.Position.X)
                spot = new Vector2(rect.Position.X - Wide - Gap, y);                // on its left
            else if (rect.End.X + Gap + Wide <= desk.End.X)
                spot = new Vector2(rect.End.X + Gap, y);                            // on its right
            else if (rect.Position.Y - High - Gap >= desk.Position.Y)
                spot = new Vector2(rect.Position.X + 24, rect.Position.Y - High - Gap);   // above it
            else if (rect.End.Y + Gap + High <= desk.End.Y)
                spot = new Vector2(rect.Position.X + 24, rect.End.Y + Gap);         // under it
            else
                spot = new Vector2(rect.End.X - Wide - 16, rect.End.Y - High - 16); // the window fills the desktop: in its corner
        }
        else
            spot = new Vector2(desk.End.X - Wide - 16, desk.End.Y - High - 16);     // no window: the corner next to the chat
        spot.X = Math.Clamp(spot.X, desk.Position.X, Math.Max(desk.Position.X, desk.End.X - Wide));
        spot.Y = Math.Clamp(spot.Y, desk.Position.Y, Math.Max(desk.Position.Y, desk.End.Y - High));
        return spot - origin;
    }

    public override void _Process(double delta)
    {
        if (_returning)
        {
            // Being transported: it stands still.
            _time += delta;
            _phase = Mathf.LerpAngle(_phase, MathF.Round(_phase / MathF.PI) * MathF.PI, Math.Min(1, (float)delta * 6));
            Pose((float)delta);
            return;
        }
        _time += delta;
        var step = (float)delta;
        var target = Target(step);
        var before = _place;
        var distance = _place.DistanceTo(target);
        // A walking pace: quicker when it is far, slower as it arrives.
        var speed = Math.Clamp(distance * 2.2f, 90f, 520f);
        _place = _place.MoveToward(target, speed * step);
        var moved = _place - before;
        _velocity = _velocity.Lerp(moved / Math.Max(step, 1e-4f), 0.25f);
        Position = _place.Round();

        // The stride follows the ground covered: the feet do not slide.
        var walking = distance > 1.5f;
        _phase += moved.Length() / Stride * MathF.Tau;
        _walk = Mathf.MoveToward(_walk, walking ? 1 : 0, step * 5);
        if (!walking)
            // Standing: the legs come back together.
            _phase = Mathf.LerpAngle(_phase, MathF.Round(_phase / MathF.PI) * MathF.PI, Math.Min(1, step * 6));

        // It faces where it goes: +x of the screen is its right (in profile), down the screen is towards us, up is
        // away. Arrived, it turns towards the window (or towards us when there is none).
        float facing;
        if (walking && _velocity.Length() > 8)
            facing = MathF.Atan2(_velocity.X, _velocity.Y);
        else if (_deck is not null && IsInstanceValid(_deck) && WindowInUse(_deck.GetGlobalRect()) is { } rect)
            facing = Math.Clamp((rect.GetCenter().X - GetGlobalRect().GetCenter().X) / 400f, -0.75f, 0.75f);
        else
            facing = 0;
        _facing = facing;
        _yaw = Mathf.LerpAngle(_yaw, facing, Math.Min(1, step * 7));
        Pose(step);
    }

    /// <summary>
    /// A swimmer touring the screen is caught by its body only: the rest of its square lets the clicks through to the
    /// windows it passes over.
    /// </summary>
    public override bool _HasPoint(Vector2 point)
    {
        if (_whole is not { Swims: true } swimmer || View is not { } view || !IsInstanceValid(view))
            return new Rect2(Vector2.Zero, Size).HasPoint(point);
        var body = new Rect2(view.Position + new Vector2(0.06f, 0.28f) * swimmer.Side, new Vector2(0.88f, 0.42f) * swimmer.Side);
        return body.HasPoint(point);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left })
        {
            AcceptEvent();
            HomeRequested?.Invoke();
        }
    }
}
