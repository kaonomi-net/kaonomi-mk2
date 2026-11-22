using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Graphbase;

/// <summary>
/// The graph of <c>/graphbase</c> in 3D: a sphere per node, in the colour of its kind (the colour of /archi for a fact
/// sheet), the more edges it has the larger, its name and its kind above it; a line per edge with an arrowhead at the
/// node it goes to, in the colour of its type, its type written at its middle when the graph is small (or for the node
/// selected). The nodes are laid out by forces (they push each other away, the edges pull their ends together) and settle
/// in a few seconds, without shaking (<see cref="Step"/>); the graph turns slowly until it is touched. Drag to turn around it, right drag (or Shift + drag)
/// to move it, the wheel or a pinch to come closer; a click selects a node (its details beside), a double-click adds its
/// neighbours. The node selected and its edges are lit, the nodes found by the search glow. Shift + click on two nodes
/// (or more: a Shift + click on one of them takes it out) shows the shortest paths between them
/// (<see cref="GraphbaseWindowViewModel.TogglePathEnd"/>): their nodes and edges lit, every other node, its name and
/// the other edges <see cref="Faded"/> (90 % transparent); a click without Shift leaves the paths. The panel of the
/// paths of the window does the same for the nodes chosen there (SEARCH). The nodes of a kind
/// unticked in the legend (<see cref="GraphbaseWindowViewModel.HiddenKinds"/>) are <see cref="KindHidden"/> (90 %
/// transparent), their names and edges too, and the pointer goes through them; the node selected stays as it is.
/// <see cref="Flat"/> is the 2D mode: the same graph laid out on a plane and seen from the front, without perspective.
/// </summary>
public partial class Graphbase3D : SubViewportContainer
{
    private const float Spacing = 6f;          // the length the edges tend to
    private const int EdgeTitlesUpTo = 30;     // more edges: only those of the node selected are titled
    private const int NamesUpTo = 40;          // more nodes: the names of the most linked and of the nodes lit only
    private const int KindsUpTo = 20;          // more nodes: the kind is not written under the name
    private const float Faded = 0.1f;          // what is left of a node out of the paths shown: 90 % transparent
    private const float KindHidden = 0.1f;     // what is left of a node of a kind unticked in the legend: 90 % transparent

    /// <summary>
    /// The opacity to draw for a share of a node left visible (<see cref="KindHidden"/>, <see cref="Faded"/>). The 3D
    /// scene blends in linear light: on a dark background, 10 % of a bright colour comes out about a third as bright as
    /// the colour (the conversion to the screen lifts the dark values), far from 90 % transparent. The share is turned
    /// into linear light there, so that what is seen is about a tenth; on a light background 10 % already looks very faint.
    /// </summary>
    private static float Seen(float share) => Eve.Eve.IsLight ? share : MathF.Pow(share, 2.2f);

    private static float HiddenAlpha => Seen(KindHidden);
    private const float NamePixel = 0.00034f;  // the size of the names (of a pixel of their font, at one unit of the camera)
    private const float TitlePixel = 0.0003f;  // the same for the types written on the edges

    private readonly GraphbaseWindowViewModel _vm = null!;
    private readonly SubViewport _viewport = new() { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
    private readonly Camera3D _camera = new() { Fov = 50, Far = 6000 };
    private readonly Node3D _scene = new();
    private readonly SphereMesh _sphere = new() { Radius = 1, Height = 2, RadialSegments = 32, Rings = 16 };
    private readonly Dictionary<string, Ball> _balls = new(StringComparer.Ordinal);
    private readonly List<(GraphbaseEdge Edge, Label3D Title)> _titles = [];
    private readonly ImmediateMesh _lineMesh = new();
    /// <summary>The nodes whose name is always written (all of them on a small graph, else the most linked).</summary>
    private readonly HashSet<string> _named = new(StringComparer.Ordinal);

    private float _yaw = 0.6f, _pitch = 0.35f, _distance = 60;
    private Vector3 _target;
    private bool _orbiting, _panning, _moved;
    /// <summary>The camera follows the graph as it settles, and turns, until the user moves it.</summary>
    private bool _autoFit = true, _spin = true;
    private bool _flat;
    private float _temperature;
    private int _steps;
    private string? _hovered;
    private ulong _lastPress;
    private string? _lastPressed;
    /// <summary>Shift was down when the button was pressed: the click chooses a node of the paths.</summary>
    private bool _shiftPress;

    /// <summary>A double-click on a node (its id).</summary>
    public event Action<string>? NodeDoubleClicked;

    private sealed class Ball
    {
        public required MeshInstance3D Mesh { get; init; }
        public required Label3D Label { get; init; }
        public required StandardMaterial3D Material { get; init; }
        public Vector3 Position;
        public Vector3 Push;
        /// <summary>The direction of its last move, and how fast it may go (1: the temperature; less after it turned back).</summary>
        public Vector3 Heading;
        public float Speed = 1;
        public float Size = 1;
        public Color Color;
    }

    // Godot builds its scripts with this constructor.
    public Graphbase3D()
    {
    }

    /// <param name="flat">Opened in the 2D mode (<see cref="Flat"/>).</param>
    public Graphbase3D(GraphbaseWindowViewModel vm, bool flat = false)
    {
        _vm = vm;
        _flat = flat;
        if (flat)
        {
            // Seen from the front, still.
            _yaw = _pitch = 0;
            _spin = false;
            _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        }
        Stretch = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(320, 240);
        AddChild(_viewport);
        _viewport.AddChild(_scene);
        _scene.AddChild(_camera);
        _camera.Current = true;

        var light = Eve.Eve.IsLight;
        var background = light ? new Color(0.97f, 0.98f, 0.99f) : new Color(0.012f, 0.018f, 0.028f);
        _scene.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = background,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = light ? new Color(0.75f, 0.78f, 0.82f) : new Color(0.3f, 0.36f, 0.44f),
                AmbientLightEnergy = 1f,
                GlowEnabled = !light,
                GlowIntensity = 0.7f,
                GlowBloom = 0.1f,
                FogEnabled = true,
                FogLightColor = background,
                FogDensity = light ? 0.0015f : 0.004f
            }
        });
        _scene.AddChild(new DirectionalLight3D { LightEnergy = 1.15f, RotationDegrees = new Vector3(-50, 35, 0) });
        _scene.AddChild(new DirectionalLight3D { LightEnergy = 0.4f, RotationDegrees = new Vector3(30, -140, 0), LightColor = Eve.Eve.Cyan });
        _scene.AddChild(new MeshInstance3D
        {
            Mesh = _lineMesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha
            }
        });

        Action<bool> changed = replaced => Bind.OnMain(() => Rebuild(replaced));
        vm.GraphChanged += changed;
        Bind.OnGone(this, () => vm.GraphChanged -= changed);
        Bind.When(this, vm, UpdateLook, nameof(GraphbaseWindowViewModel.SelectedId));
        Action pathChanged = () => Bind.OnMain(UpdateLook);
        vm.PathChanged += pathChanged;
        vm.KindsChanged += pathChanged;
        Bind.OnGone(this, () =>
        {
            vm.PathChanged -= pathChanged;
            vm.KindsChanged -= pathChanged;
        });
        PlaceCamera();
        Rebuild(true);
    }

    // ── 2D or 3D ──

    /// <summary>
    /// The 2D mode: the nodes laid out on a plane seen from the front, without perspective (no node behind another,
    /// nothing nearer or further), still; a drag moves the graph instead of turning it. Turned on, the graph is
    /// flattened as it is seen, then laid out again on its plane; turned off, the nodes leave the plane and the graph
    /// turns again.
    /// </summary>
    public bool Flat
    {
        get => _flat;
        set
        {
            if (_flat == value)
                return;
            _flat = value;
            if (value)
            {
                // What is seen now, flattened: across and up as the camera sees them.
                var back = new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw));
                var right = Vector3.Up.Cross(back).Normalized();
                var up = back.Cross(right).Normalized();
                foreach (var ball in _balls.Values)
                {
                    var d = ball.Position - _target;
                    ball.Position = new Vector3(d.Dot(right), d.Dot(up), 0);
                }
                _target = Vector3.Zero;
                _yaw = _pitch = 0;
            }
            else
                // Out of the plane, each by a depth of its own: the forces spread them in the three directions again.
                foreach (var (id, ball) in _balls)
                    ball.Position = ball.Position with { Z = ((Hash(id) >> 16 & 0xFF) / 255f - 0.5f) * Spacing * 2 };
            _camera.Projection = value ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
            foreach (var ball in _balls.Values)
            {
                ball.Label.PixelSize = NamePixel * TextScale;
                ball.Speed = 1;
                ball.Heading = Vector3.Zero;
            }
            foreach (var (_, title) in _titles)
                title.PixelSize = TitlePixel * TextScale;
            _temperature = Spacing * 0.9f;
            _steps = 320;
            _autoFit = true;
            _spin = !value;
            Place();
        }
    }

    /// <summary>The tangent of half the angle the camera sees, up and down.</summary>
    private float HalfView => MathF.Tan(Mathf.DegToRad(_camera.Fov / 2));

    /// <summary>
    /// What the texts of a fixed size are scaled by: without perspective Godot sizes them from the height seen and not
    /// from their distance, <see cref="HalfView"/> times as large for the same picture (seen in a screenshot: half the size).
    /// </summary>
    private float TextScale => _flat ? 1 / HalfView : 1;

    // ── The colours ──

    /// <summary>The colour of a kind: the one of /archi for a kind of fact sheet, else one made from its name.</summary>
    public static Color ColorOf(string kind)
    {
        if (Enum.TryParse<ArchiType>(kind, out var archi) && Enum.IsDefined(archi))
            return Color.FromHtml(ArchiCatalog.Info(archi).Color);
        return kind.Length == 0 ? new Color(0.6f, 0.66f, 0.72f) : Color.FromHsv(Hash(kind) % 360 / 360f, 0.55f, Eve.Eve.IsLight ? 0.75f : 0.92f);
    }

    private static Color EdgeColor(string type) => Color.FromHsv(Hash(type) % 360 / 360f, 0.35f, Eve.Eve.IsLight ? 0.45f : 0.85f);

    /// <summary>A hash of a text that is the same from one run to the next (FNV-1a).</summary>
    private static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
            hash = (hash ^ c) * 16777619u;
        return hash;
    }

    // ── Building ──

    /// <summary>
    /// The spheres of the nodes drawn: the new ones placed near a neighbour already there (or anywhere), the gone ones
    /// removed; then laid out again (from scratch when <paramref name="replaced"/>).
    /// </summary>
    private void Rebuild(bool replaced)
    {
        if (!IsInstanceValid(this))
            return;
        foreach (var id in _balls.Keys.Where(id => !_vm.Nodes.ContainsKey(id)).ToList())
        {
            _balls[id].Mesh.QueueFree();
            _balls[id].Label.QueueFree();
            _balls.Remove(id);
        }
        var spread = MathF.Max(6, MathF.Cbrt(_vm.Nodes.Count) * Spacing);
        foreach (var node in _vm.Nodes.Values)
        {
            var color = ColorOf(node.Kind);
            if (!_balls.TryGetValue(node.Id, out var ball))
            {
                var material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.32f, Metallic = 0.15f, RimEnabled = true, Rim = 0.35f, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0 };
                var mesh = new MeshInstance3D { Mesh = _sphere, MaterialOverride = material };
                var label = new Label3D
                {
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    // The same size on the screen however far the node is (readable on a large graph).
                    FixedSize = true,
                    FontSize = 48,
                    PixelSize = NamePixel * TextScale,
                    OutlineSize = 12,
                    Modulate = Eve.Eve.IsLight ? new Color(0.08f, 0.1f, 0.13f) : new Color(0.93f, 0.96f, 1f),
                    OutlineModulate = Eve.Eve.IsLight ? new Color(1, 1, 1, 0.85f) : new Color(0, 0, 0, 0.8f),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                ball = new Ball { Mesh = mesh, Label = label, Material = material, Position = Start(node.Id, spread) };
                _scene.AddChild(mesh);
                _scene.AddChild(label);
                _balls[node.Id] = ball;
            }
            ball.Color = color;
            ball.Material.AlbedoColor = color;
            ball.Material.Emission = color;
            var name = node.Name.Length > 32 ? node.Name[..31] + "…" : node.Name;
            ball.Label.Text = node.Kind.Length > 0 && _vm.Nodes.Count <= KindsUpTo ? $"{name}\n{node.Kind}" : name;
        }
        var degrees = _balls.Keys.ToDictionary(id => id, _vm.Degree, StringComparer.Ordinal);
        foreach (var (id, ball) in _balls)
            ball.Size = MathF.Min(2.4f, 0.8f + 0.32f * MathF.Log2(1 + degrees[id]));
        _named.Clear();
        _named.UnionWith(_balls.Count <= NamesUpTo ? _balls.Keys : degrees.OrderByDescending(d => d.Value).ThenBy(d => d.Key, StringComparer.Ordinal).Take(20).Select(d => d.Key));

        foreach (var (_, title) in _titles)
            title.QueueFree();
        _titles.Clear();
        if (_vm.Edges.Count <= 1500)
            foreach (var edge in _vm.Edges.Values.Where(e => e.From != e.To))
            {
                var title = new Label3D
                {
                    Text = edge.Type, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, FontSize = 36, PixelSize = TitlePixel * TextScale, OutlineSize = 10,
                    Modulate = EdgeColor(edge.Type).Lightened(Eve.Eve.IsLight ? 0 : 0.2f),
                    OutlineModulate = Eve.Eve.IsLight ? new Color(1, 1, 1, 0.8f) : new Color(0, 0, 0, 0.75f)
                };
                _scene.AddChild(title);
                _titles.Add((edge, title));
            }

        _temperature = replaced ? Spacing * 1.6f : Spacing * 0.6f;
        _steps = replaced ? 420 : 220;
        foreach (var ball in _balls.Values)
        {
            ball.Speed = 1;
            ball.Heading = Vector3.Zero;
        }
        if (replaced)
            _autoFit = true;
        UpdateLook();
        Place();
    }

    /// <summary>Where a new node starts: near a neighbour already drawn, else at a place given by its id.</summary>
    private Vector3 Start(string id, float spread)
    {
        var seed = Hash(id);
        var jitter = new Vector3((seed & 0xFF) / 255f - 0.5f, ((seed >> 8) & 0xFF) / 255f - 0.5f, _flat ? 0 : ((seed >> 16) & 0xFF) / 255f - 0.5f);
        var neighbour = _vm.EdgesOf(id).Select(e => e.Other(id)).FirstOrDefault(other => other != id && _balls.ContainsKey(other));
        return neighbour is not null ? _balls[neighbour].Position + jitter * Spacing : jitter * spread * 2;
    }

    // ── The layout ──

    /// <summary>
    /// One step of the forces (Fruchterman-Reingold in 3D, with a pull to the centre). The forces are far stronger than
    /// the temperature: every node would move by the whole temperature at each step and, near its place, turn back at
    /// the next one, the graph shaking until it has cooled. A node that turns back slows down a lot, and speeds up again
    /// little by little while it goes on the same way.
    /// </summary>
    private void Step()
    {
        var balls = _balls.Values.ToArray();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = _balls.Keys.ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            index[ids[i]] = i;
            balls[i].Push = Vector3.Zero;
        }
        const float k2 = Spacing * Spacing;
        for (var i = 0; i < balls.Length; i++)
        for (var j = i + 1; j < balls.Length; j++)
        {
            var d = balls[i].Position - balls[j].Position;
            var length = MathF.Max(d.Length(), 0.05f);
            var force = d / length * (k2 / length);
            balls[i].Push += force;
            balls[j].Push -= force;
        }
        foreach (var edge in _vm.Edges.Values)
        {
            if (edge.From == edge.To || !index.TryGetValue(edge.From, out var a) || !index.TryGetValue(edge.To, out var b))
                continue;
            var d = balls[a].Position - balls[b].Position;
            var length = MathF.Max(d.Length(), 0.05f);
            var force = d / length * (length * length / Spacing);
            balls[a].Push -= force;
            balls[b].Push += force;
        }
        foreach (var ball in balls)
        {
            ball.Push -= ball.Position * 0.08f;
            // 2D: the nodes stay on their plane.
            if (_flat)
            {
                ball.Push = ball.Push with { Z = 0 };
                ball.Position = ball.Position with { Z = 0 };
            }
            var length = ball.Push.Length();
            if (length <= 0.0001f)
                continue;
            var heading = ball.Push / length;
            ball.Speed = heading.Dot(ball.Heading) < 0 ? MathF.Max(0.02f, ball.Speed * 0.25f) : MathF.Min(1, ball.Speed * 1.05f);
            ball.Heading = heading;
            ball.Position += heading * MathF.Min(length, _temperature * ball.Speed);
        }
        _temperature = MathF.Max(0.02f, _temperature * 0.985f);
    }

    public override void _Process(double delta)
    {
        if (_vm is null || _balls.Count == 0)
            return;
        if (_steps > 0)
        {
            var perFrame = _balls.Count > 300 ? 1 : _balls.Count > 120 ? 2 : 4;
            for (var i = 0; i < perFrame && _steps > 0; i++, _steps--)
                Step();
            Place();
        }
        if (_autoFit)
            Fit((float)delta);
        if (_spin && !_flat && !_orbiting && !_panning)
            _yaw += (float)delta * 0.07f;
        PlaceCamera();
    }

    /// <summary>
    /// The camera brought, little by little, to see the whole graph as it is seen now: the middle of what it covers on
    /// the screen at the centre, and the distance at which every sphere (and the name above it) is inside the picture,
    /// across and up and down.
    /// </summary>
    private void Fit(float delta)
    {
        var back = new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw));
        var right = Vector3.Up.Cross(back).Normalized();
        var up = back.Cross(right).Normalized();
        var centre = _balls.Values.Aggregate(Vector3.Zero, (sum, b) => sum + b.Position) / _balls.Count;
        // The middle of what the graph covers across and up and down.
        float left = float.MaxValue, rightMost = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
        foreach (var ball in _balls.Values)
        {
            var d = ball.Position - centre;
            left = MathF.Min(left, d.Dot(right) - ball.Size);
            rightMost = MathF.Max(rightMost, d.Dot(right) + ball.Size);
            bottom = MathF.Min(bottom, d.Dot(up) - ball.Size);
            top = MathF.Max(top, d.Dot(up) + ball.Size + 1.5f);
        }
        centre += right * (left + rightMost) / 2 + up * (bottom + top) / 2;
        var tanV = HalfView;
        var tanH = tanV * MathF.Max(0.3f, Size.X / MathF.Max(1, Size.Y));
        var distance = 8f;
        foreach (var ball in _balls.Values)
        {
            var d = ball.Position - centre;
            var nearer = d.Dot(back);
            distance = MathF.Max(distance, nearer + (MathF.Abs(d.Dot(right)) + ball.Size + 1) / tanH);
            distance = MathF.Max(distance, nearer + (MathF.Abs(d.Dot(up)) + ball.Size + 2) / tanV);
        }
        var t = 1 - MathF.Exp(-delta * 4);
        _target = _target.Lerp(centre, t);
        _distance = Mathf.Lerp(_distance, Math.Clamp(distance * 1.06f, 8, 5000), t);
    }

    private void PlaceCamera()
    {
        _pitch = Math.Clamp(_pitch, -1.45f, 1.45f);
        var offset = new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw)) * _distance;
        // 2D: no perspective, the camera sees the height it would see at this distance.
        if (_flat)
            _camera.Size = 2 * _distance * HalfView;
        if (_camera.IsInsideTree())
            _camera.LookAtFromPosition(_target + offset, _target, Vector3.Up);
        else
            _camera.Position = _target + offset;
    }

    /// <summary>The camera turned to a node, close to it.</summary>
    public void Focus(string id)
    {
        if (!_balls.TryGetValue(id, out var ball))
            return;
        _autoFit = _spin = false;
        _target = ball.Position;
        _distance = Math.Clamp(ball.Size * 14, 10, 60);
    }

    /// <summary>Fits the whole graph again (and lets it turn).</summary>
    public void FitAll()
    {
        _autoFit = true;
        _spin = !_flat;
    }

    // ── Drawing ──

    /// <summary>The spheres, their names, the lines and the titles of the edges at their places.</summary>
    private void Place()
    {
        foreach (var ball in _balls.Values)
        {
            ball.Mesh.Position = ball.Position;
            ball.Label.Position = ball.Position + new Vector3(0, ball.Size + 0.35f, 0);
        }
        var selected = _vm.SelectedId;
        var hovered = _hovered;
        // The paths between the nodes chosen (Shift + click): their edges lit, the others faded.
        var paths = _vm.PathShown;
        _lineMesh.ClearSurfaces();
        var begun = false;
        foreach (var edge in _vm.Edges.Values)
        {
            if (!_balls.TryGetValue(edge.From, out var from) || !_balls.TryGetValue(edge.To, out var to))
                continue;
            if (!begun)
            {
                _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
                begun = true;
            }
            var lit = paths
                ? _vm.PathEdges.Contains(edge.Id)
                : selected is not null && (edge.From == selected || edge.To == selected) || hovered is not null && (edge.From == hovered || edge.To == hovered);
            var color = lit ? Eve.Eve.Amber : EdgeColor(edge.Type) with { A = paths ? 0.6f * Seen(Faded) : selected is null ? 0.6f : 0.22f };
            // An end of a kind unticked in the legend (not the node selected): the edge 90 % transparent too.
            if ((edge.From != selected && _vm.IsHidden(edge.From)) || (edge.To != selected && _vm.IsHidden(edge.To)))
                color = color with { A = color.A * HiddenAlpha };
            if (edge.From == edge.To)
            {
                // A loop: a small triangle above the sphere.
                var top = from.Position + new Vector3(0, from.Size, 0);
                var a = top + new Vector3(-from.Size, from.Size * 1.2f, 0);
                var b = top + new Vector3(from.Size, from.Size * 1.2f, 0);
                Line(top, a, color);
                Line(a, b, color);
                Line(b, top, color);
                continue;
            }
            var direction = (to.Position - from.Position).Normalized();
            var start = from.Position + direction * from.Size;
            var end = to.Position - direction * to.Size;
            Line(start, end, color);
            var side = direction.Cross(MathF.Abs(direction.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
            var up = side.Cross(direction).Normalized();
            var back = end - direction * 0.9f;
            foreach (var v in new[] { side, -side, up, -up })
                Line(end, back + v * 0.35f, color);
        }
        if (begun)
            _lineMesh.SurfaceEnd();
        var titled = _vm.Edges.Count <= EdgeTitlesUpTo;
        foreach (var (edge, title) in _titles)
        {
            var mine = paths ? _vm.PathEdges.Contains(edge.Id) : selected is not null && (edge.From == selected || edge.To == selected);
            var hiddenEnd = (edge.From != selected && _vm.IsHidden(edge.From)) || (edge.To != selected && _vm.IsHidden(edge.To));
            title.Visible = ((titled && selected is null && !paths) || mine) && !hiddenEnd;
            if (title.Visible && _balls.TryGetValue(edge.From, out var from) && _balls.TryGetValue(edge.To, out var to))
                title.Position = (from.Position + to.Position) / 2 + new Vector3(0, 0.45f, 0);
        }
    }

    private void Line(Vector3 a, Vector3 b, Color color)
    {
        _lineMesh.SurfaceSetColor(color);
        _lineMesh.SurfaceAddVertex(a);
        _lineMesh.SurfaceSetColor(color);
        _lineMesh.SurfaceAddVertex(b);
    }

    /// <summary>
    /// The sizes and the light of the spheres: the node selected the brightest, the nodes found glowing. With paths
    /// shown (Shift + click on two nodes), the nodes chosen are the brightest, the nodes of the paths glow, and every
    /// other node is <see cref="Faded"/>, with its name.
    /// </summary>
    private void UpdateLook()
    {
        if (_vm is null || !IsInstanceValid(this))
            return;
        var selected = _vm.SelectedId;
        var paths = _vm.PathShown;
        var near = selected is null ? [] : _vm.EdgesOf(selected).Select(e => e.Other(selected)).ToHashSet(StringComparer.Ordinal);
        var ink = Eve.Eve.IsLight ? new Color(0.08f, 0.1f, 0.13f) : new Color(0.93f, 0.96f, 1f);
        var outline = Eve.Eve.IsLight ? new Color(1, 1, 1, 0.85f) : new Color(0, 0, 0, 0.8f);
        foreach (var (id, ball) in _balls)
        {
            // A node chosen for the paths (one alone is the node selected too).
            var isEnd = _vm.PathEnds.Contains(id);
            var isSelected = id == selected || isEnd;
            var onPath = paths && _vm.PathNodes.Contains(id);
            // Its kind unticked in the legend: 90 % transparent (the node selected or chosen stays as it is).
            var hidden = !isSelected && _vm.IsHidden(id);
            var faded = hidden || (paths && !onPath);
            var see = hidden ? HiddenAlpha : Seen(Faded);
            var dimmed = !paths && selected is not null && !isSelected && !near.Contains(id);
            ball.Mesh.Scale = Vector3.One * ball.Size * (isSelected ? 1.18f : id == _hovered ? 1.08f : 1);
            ball.Material.EmissionEnergyMultiplier = faded ? 0 : isSelected ? 1.3f : onPath || _vm.Found.Contains(id) ? 0.55f : id == _hovered ? 0.35f : 0;
            var color = !dimmed ? ball.Color : Eve.Eve.IsLight ? ball.Color.Lightened(0.5f) : ball.Color.Darkened(0.5f);
            // Out of the paths, or of a kind unticked: the sphere lets see through it (and no rim: it would stay bright on its edge).
            ball.Material.Transparency = faded ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
            ball.Material.RimEnabled = !faded;
            ball.Material.AlbedoColor = color with { A = faded ? see : 1 };
            var alpha = faded ? see : dimmed ? 0.35f : 1;
            ball.Label.Modulate = ink with { A = alpha };
            ball.Label.OutlineModulate = outline with { A = outline.A * (faded ? see : 1) };
            ball.Label.Visible = _named.Contains(id) || isSelected || onPath || id == _hovered || near.Contains(id) || _vm.Found.Contains(id);
        }
        Place();
    }

    // ── The pointer ──

    /// <summary>The node whose sphere is under <paramref name="point"/>, the nearest to the camera first.</summary>
    private string? NodeAt(Vector2 point)
    {
        string? best = null;
        var bestDepth = float.MaxValue;
        foreach (var (id, ball) in _balls)
        {
            // A node of a kind unticked in the legend lets the pointer through (the node selected excepted).
            if (_camera.IsPositionBehind(ball.Position) || (id != _vm.SelectedId && _vm.IsHidden(id)))
                continue;
            var screen = _camera.UnprojectPosition(ball.Position);
            var rim = _camera.UnprojectPosition(ball.Position + _camera.GlobalBasis.X * ball.Size);
            var radius = MathF.Max(8, screen.DistanceTo(rim));
            var depth = _camera.GlobalPosition.DistanceTo(ball.Position);
            if (screen.DistanceTo(point) <= radius && depth < bestDepth)
            {
                best = id;
                bestDepth = depth;
            }
        }
        return best;
    }

    private void ZoomBy(float factor)
    {
        _autoFit = false;
        _distance = Math.Clamp(_distance / factor, 4, 5000);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
            {
                var factor = wheel.Factor > 0 ? wheel.Factor : 1;
                ZoomBy(wheel.ButtonIndex == MouseButton.WheelUp ? Mathf.Pow(1.12f, factor) : 1 / Mathf.Pow(1.12f, factor));
                AcceptEvent();
                break;
            }
            case InputEventMagnifyGesture magnify:
                ZoomBy(magnify.Factor);
                AcceptEvent();
                break;
            case InputEventPanGesture pan:
                ZoomBy(Mathf.Pow(1.06f, -pan.Delta.Y));
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle } button:
                if (button.Pressed)
                {
                    _moved = false;
                    _spin = false;
                    _shiftPress = button.ShiftPressed;
                    // 2D: there is nothing to turn around, every drag moves the graph.
                    _panning = _flat || button.ButtonIndex != MouseButton.Left || button.ShiftPressed;
                    _orbiting = !_panning;
                    if (button.ButtonIndex == MouseButton.Left)
                    {
                        // The double-click of the system, or two presses on the same node within 400 ms.
                        var now = Time.GetTicksMsec();
                        var at = NodeAt(button.Position);
                        // Not with Shift: two Shift + clicks on a node choose it for the paths, then take it out.
                        if (at is not null && !_shiftPress && (button.DoubleClick || (at == _lastPressed && now - _lastPress < 400)))
                        {
                            _lastPressed = null;
                            _orbiting = false;
                            NodeDoubleClicked?.Invoke(at);
                        }
                        else
                        {
                            _lastPressed = at;
                            _lastPress = now;
                        }
                    }
                }
                else
                {
                    if (!_moved && button.ButtonIndex == MouseButton.Left)
                    {
                        // Shift + click (Shift + drag moves the graph): a node of the paths; beside the nodes, nothing.
                        var at = NodeAt(button.Position);
                        if (!_shiftPress)
                            _vm.Select(at);
                        else if (at is not null)
                            _vm.TogglePathEnd(at);
                    }
                    _orbiting = _panning = false;
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                if (_orbiting || _panning)
                {
                    _moved |= motion.Relative.Length() > 1;
                    if (_moved)
                        _autoFit = false;
                    if (_orbiting)
                    {
                        _yaw -= motion.Relative.X * 0.008f;
                        _pitch += motion.Relative.Y * 0.008f;
                    }
                    else
                        // 2D: the graph follows the pointer exactly (what a point of the screen is on its plane).
                        _target += (-_camera.GlobalBasis.X * motion.Relative.X + _camera.GlobalBasis.Y * motion.Relative.Y)
                                   * (_flat ? 2 * _distance * HalfView / MathF.Max(1, Size.Y) : _distance * 0.0016f);
                    AcceptEvent();
                    break;
                }
                var hovered = NodeAt(motion.Position);
                if (hovered != _hovered)
                {
                    _hovered = hovered;
                    UpdateLook();
                    MouseDefaultCursorShape = hovered is null ? CursorShape.Arrow : CursorShape.PointingHand;
                    TooltipText = hovered is null || !_vm.Nodes.TryGetValue(hovered, out var node)
                        ? string.Empty
                        : $"{node.Name}{(node.Kind.Length > 0 ? $" · {node.Kind}" : "")} · {_vm.Degree(hovered)} edge(s) · click: its details · double-click: its neighbours · Shift + click: the paths to the other nodes chosen";
                }
                break;
        }
    }
}
