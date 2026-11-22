using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The body of a 3D animation, made from its picture (Assets/&lt;name&gt;-body.png, drawn from the face by
/// Tools/avatar-body/generate_bodies.py, with its joints in &lt;name&gt;-body.json): the picture is cut into its parts
/// (the trunk with the head, and for each arm and leg its upper and lower part) along its joints, and each part is a
/// flat piece of the picture hung from its joint in a 3D scene, as a paper puppet. The pieces turn about their joints
/// in depth: a leg that steps forward really comes towards the camera, and the perspective shortens it. Each piece
/// keeps a round of the picture around its joints, so that no hole opens when it turns.
/// <para>
/// Units: the body is <see cref="High"/> high, its feet on y = 0, centred on x = 0, flat in the plane z = 0, facing
/// +z. The picture of the face of the animation (the bust at the top of the body) is taken out of the trunk: the
/// animation itself is shown there, over the body (<see cref="FaceCorners"/>), so its mouth still moves.
/// </para>
/// </summary>
public sealed class BodyPuppet
{
    /// <summary>The height of the body, in the units of the scene.</summary>
    public const float High = 1.9f;

    private const int Trunk = 0;

    /// <summary>The parts cut out of the picture, after the trunk: for each, its two joints and what it hangs from.</summary>
    private static readonly (string Name, string From, string To, int Parent, bool Leg)[] Limbs =
    [
        ("thigh_l", "hip_l", "knee_l", Trunk, true), ("thigh_r", "hip_r", "knee_r", Trunk, true),
        ("shin_l", "knee_l", "ankle_l", 1, true), ("shin_r", "knee_r", "ankle_r", 2, true),
        ("arm_l", "shoulder_l", "elbow_l", Trunk, false), ("arm_r", "shoulder_r", "elbow_r", Trunk, false),
        ("forearm_l", "elbow_l", "wrist_l", 5, false), ("forearm_r", "elbow_r", "wrist_r", 6, false),
    ];

    private readonly Node3D[] _pivots = new Node3D[Limbs.Length + 1];
    private readonly Vector3[] _faceCorners = new Vector3[2];

    private BodyPuppet(Node3D root) => Root = root;

    /// <summary>The body: added to the scene, turned and moved as a whole.</summary>
    public Node3D Root { get; }

    /// <summary>
    /// The top left and bottom right corners of the picture of the face in the body (in the units of <see cref="Root"/>):
    /// where the animation is shown.
    /// </summary>
    public IReadOnlyList<Vector3> FaceCorners => _faceCorners;

    /// <summary>The body of the animation whose face is the picture <paramref name="facePath"/> (…-face.png); null when it has none.</summary>
    public static BodyPuppet? Load(string facePath)
    {
        const string suffix = "-face.png";
        if (!facePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;
        var stem = facePath[..^suffix.Length];
        var picturePath = stem + "-body.png";
        var jointsPath = stem + "-body.json";
        if (!File.Exists(picturePath) || !File.Exists(jointsPath))
            return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jointsPath));
            var root = document.RootElement;
            var joints = new Dictionary<string, Vector2>();
            foreach (var joint in root.GetProperty("joints").EnumerateObject())
                joints[joint.Name] = new Vector2(joint.Value[0].GetSingle(), joint.Value[1].GetSingle());
            var face = root.GetProperty("face");
            var faceRect = new Rect2(face[0].GetSingle(), face[1].GetSingle(), face[2].GetSingle(), face[3].GetSingle());
            // The arms are cut out only when the picture says they hang clear of the trunk, with joints found right.
            if (!(root.TryGetProperty("arms", out var arms) && arms.ValueKind == JsonValueKind.True))
                foreach (var name in new[] { "shoulder", "elbow", "wrist" })
                {
                    joints.Remove(name + "_l");
                    joints.Remove(name + "_r");
                }
            var picture = Image.LoadFromFile(picturePath);
            if (picture is null || picture.IsEmpty())
                return null;
            picture.Convert(Image.Format.Rgba8);
            var facePicture = Image.LoadFromFile(facePath);
            facePicture?.Convert(Image.Format.Rgba8);
            return Build(picture, joints, faceRect, facePicture);
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            GD.PushWarning($"Body {picturePath} not read: {ex.Message}");
            return null;
        }
    }

    private static BodyPuppet Build(Image picture, Dictionary<string, Vector2> joints, Rect2 faceRect, Image? facePicture)
    {
        int width = picture.GetWidth(), height = picture.GetHeight();
        var pixels = picture.GetData();     // RGBA, row after row
        var owner = new byte[width * height];
        var middle = (joints["hip_l"].X + joints["hip_r"].X) / 2;
        var hips = Math.Max(8, Math.Abs(joints["hip_r"].X - joints["hip_l"].X));

        // How thick each part is: half the width of the body at its middle, where it stands apart; never more than
        // what a limb can be next to the hips.
        var radius = new float[Limbs.Length + 1];
        // A limb without its joints (the arms of a body that keeps them against its trunk) is not cut out.
        var cut = new bool[Limbs.Length + 1];
        for (var i = 0; i < Limbs.Length; i++)
        {
            var limb = Limbs[i];
            cut[i + 1] = joints.ContainsKey(limb.From) && joints.ContainsKey(limb.To);
            if (!cut[i + 1])
                continue;
            var at = (joints[limb.From] + joints[limb.To]) / 2;
            var measured = RunWidth(pixels, width, height, at) / 2 + 3;
            var most = limb.Leg ? hips * 0.75f : hips * 0.5f;
            var least = limb.Leg ? hips * 0.3f : hips * 0.2f;
            radius[i + 1] = Math.Clamp(measured, least, most);
        }

        // Which part each point of the picture belongs to: the lower parts first, then the upper ones, the trunk for the rest.
        int[] order = [3, 4, 1, 2, 7, 8, 5, 6];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] == 0)
                    continue;
                var point = new Vector2(x, y);
                foreach (var part in order)
                {
                    if (!cut[part])
                        continue;
                    var limb = Limbs[part - 1];
                    Vector2 from = joints[limb.From], to = joints[limb.To];
                    var left = limb.Name.EndsWith("_l", StringComparison.Ordinal);
                    // A leg is on its side of the body and under its hip; an arm is on the outside of its shoulder.
                    if (limb.Leg && (left ? x > middle : x < middle))
                        continue;
                    if (limb.Leg && y < joints[left ? "hip_l" : "hip_r"].Y - radius[part] * 0.2f)
                        continue;
                    if (!limb.Leg)
                    {
                        var shoulder = joints[left ? "shoulder_l" : "shoulder_r"];
                        if (left ? x > shoulder.X + radius[part] * 0.55f : x < shoulder.X - radius[part] * 0.55f)
                            continue;
                        if (y < shoulder.Y - radius[part])
                            continue;
                    }
                    // The lower part goes on to the end of the limb (the hand, the foot).
                    var end = limb.Parent != Trunk ? to + (to - from) * 0.9f : to;
                    if (Distance(point, from, end) <= radius[part])
                    {
                        owner[y * width + x] = (byte)part;
                        break;
                    }
                }
            }

        // The picture of the face, as it is in the body: taken out of the trunk (the animation is shown there).
        var faceAlpha = facePicture is null ? null : facePicture.GetData();
        int faceWidth = facePicture?.GetWidth() ?? 0, faceHeight = facePicture?.GetHeight() ?? 0;

        var scale = High / height;
        Vector3 Place(Vector2 p, float depth = 0) => new((p.X - middle) * scale, (height - p.Y) * scale, depth);

        var puppet = new BodyPuppet(new Node3D());
        puppet._pivots[Trunk] = puppet.Root;
        puppet._faceCorners[0] = Place(faceRect.Position, 0.012f);
        puppet._faceCorners[1] = Place(faceRect.End, 0.012f);

        for (var part = 0; part <= Limbs.Length; part++)
        {
            if (part != Trunk && !cut[part])
                continue;
            // The points of the part, and the rounds kept around its joints (of what it hangs from, and of what hangs from it).
            var rounds = new List<(Vector2 Centre, float Radius)>();
            if (part != Trunk)
            {
                rounds.Add((joints[Limbs[part - 1].From], radius[part]));
                rounds.Add((joints[Limbs[part - 1].To], radius[part] * 0.95f));
            }
            for (var child = 1; child <= Limbs.Length; child++)
                if (cut[child] && Limbs[child - 1].Parent == part)
                    rounds.Add((joints[Limbs[child - 1].From], radius[child] * 0.95f));

            int left = width, top = height, right = -1, bottom = -1;
            var keep = new bool[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    if (pixels[index * 4 + 3] == 0)
                        continue;
                    var mine = owner[index] == part;
                    if (!mine)
                    {
                        // A round around a joint: only what belongs to the part on the other side of that joint.
                        var other = owner[index];
                        var related = part == Trunk ? Limbs[other - 1].Parent == Trunk
                            : other == Limbs[part - 1].Parent || (other != Trunk && Limbs[other - 1].Parent == part);
                        if (!related)
                            continue;
                        foreach (var (spot, r) in rounds)
                            if (spot.DistanceSquaredTo(new Vector2(x, y)) <= r * r)
                            {
                                mine = true;
                                break;
                            }
                        if (!mine)
                            continue;
                    }
                    if (part == Trunk && faceAlpha is not null && faceRect.HasPoint(new Vector2(x, y)))
                    {
                        // Only well inside the face: a rim of the picture stays under the edge of the animation, which
                        // moves a little (it sways, its relief shifts it), so no gap opens between the two.
                        const int rim = 14;
                        bool Solid(int px, int py)
                        {
                            if (!faceRect.HasPoint(new Vector2(px, py)))
                                return false;
                            var fx = Math.Clamp((int)((px - faceRect.Position.X) / faceRect.Size.X * faceWidth), 0, faceWidth - 1);
                            var fy = Math.Clamp((int)((py - faceRect.Position.Y) / faceRect.Size.Y * faceHeight), 0, faceHeight - 1);
                            return faceAlpha[(fy * faceWidth + fx) * 4 + 3] > 215;
                        }
                        if (Solid(x, y) && Solid(x - rim, y) && Solid(x + rim, y) && Solid(x, y - rim) && Solid(x, y + rim))
                            continue;
                    }
                    keep[index] = true;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            if (right < left)
            {
                puppet._pivots[part] ??= new Node3D();
                continue;
            }

            int partWidth = right - left + 1, partHeight = bottom - top + 1;
            var data = new byte[partWidth * partHeight * 4];
            for (var y = top; y <= bottom; y++)
                for (var x = left; x <= right; x++)
                {
                    if (!keep[y * width + x])
                        continue;
                    Array.Copy(pixels, (y * width + x) * 4, data, ((y - top) * partWidth + (x - left)) * 4, 4);
                }
            var image = Image.CreateFromData(partWidth, partHeight, false, Image.Format.Rgba8, data);
            image.GenerateMipmaps();
            var material = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(image), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };

            // The pivot of the part: its first joint, hung from the pivot of what it hangs from.
            Node3D pivot;
            Vector2 origin;
            if (part == Trunk)
            {
                pivot = puppet.Root;
                origin = new Vector2(middle, height);
            }
            else
            {
                var limb = Limbs[part - 1];
                origin = joints[limb.From];
                var parentOrigin = limb.Parent == Trunk ? new Vector2(middle, height) : joints[Limbs[limb.Parent - 1].From];
                pivot = new Node3D { Position = new Vector3((origin.X - parentOrigin.X) * scale, (parentOrigin.Y - origin.Y) * scale, 0) };
                puppet._pivots[limb.Parent].AddChild(pivot);
                puppet._pivots[part] = pivot;
            }
            // The legs a little behind the trunk and the arms a little in front of it: no two pieces in the same plane.
            var depth = part == Trunk ? 0 : Limbs[part - 1].Leg ? -0.004f * (Limbs[part - 1].Parent == Trunk ? 1 : 2) : 0.004f * (Limbs[part - 1].Parent == Trunk ? 1 : 2);
            var centre = new Vector2(left + partWidth / 2f, top + partHeight / 2f);
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(partWidth * scale, partHeight * scale) },
                MaterialOverride = material,
                Position = new Vector3((centre.X - origin.X) * scale, (origin.Y - centre.Y) * scale, depth)
            });
        }
        return puppet;
    }

    /// <summary>The width of the body at this point: the run of the picture, on its row, that holds it.</summary>
    private static float RunWidth(byte[] pixels, int width, int height, Vector2 at)
    {
        int x = Math.Clamp((int)at.X, 0, width - 1), y = Math.Clamp((int)at.Y, 0, height - 1);
        if (pixels[(y * width + x) * 4 + 3] < 40)
            return 0;
        int left = x, right = x;
        while (left > 0 && pixels[(y * width + left - 1) * 4 + 3] >= 40)
            left--;
        while (right < width - 1 && pixels[(y * width + right + 1) * 4 + 3] >= 40)
            right++;
        return right - left + 1;
    }

    /// <summary>The distance of a point to the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static float Distance(Vector2 point, Vector2 a, Vector2 b)
    {
        var along = b - a;
        var length = along.LengthSquared();
        var t = length < 1e-6f ? 0 : Math.Clamp((point - a).Dot(along) / length, 0, 1);
        return point.DistanceTo(a + along * t);
    }

    /// <summary>
    /// The pose of a stride: <paramref name="phase"/> is where it is in it (radians), <paramref name="walk"/> how much
    /// it walks (0: standing, 1: walking), <paramref name="axis"/> the axis the limbs swing about, in the units of
    /// the body (across the way it goes), <paramref name="reach"/> how wide the stride is (1: the full stride, in
    /// depth; less when the limbs swing in the plane of the picture, where a full stride would split the legs too far),
    /// <paramref name="time"/> the time, for the breathing.
    /// </summary>
    public void Pose(float phase, float walk, Vector3 axis, float reach, float time)
    {
        var swing = MathF.Sin(phase) * reach;
        // A leg swings forward (a negative turn about the axis) and its knee bends as it comes back; the arms the other way.
        Turn(1, axis, -0.5f * swing * walk);
        Turn(2, axis, 0.5f * swing * walk);
        Turn(3, axis, (0.1f + 0.7f * MathF.Max(0, MathF.Sin(phase - 1.9f))) * walk * reach);
        Turn(4, axis, (0.1f + 0.7f * MathF.Max(0, MathF.Sin(phase - 1.9f + MathF.PI))) * walk * reach);
        Turn(5, axis, 0.4f * swing * walk);
        Turn(6, axis, -0.4f * swing * walk);
        Turn(7, axis, -0.3f * walk);
        Turn(8, axis, -0.3f * walk);
        // It bobs at each step; standing, it breathes.
        var breath = MathF.Sin(time * 1.6f) * 0.006f * (1 - walk);
        Root.Position = new Vector3(0, 0.03f * MathF.Abs(MathF.Cos(phase)) * walk + breath, 0);
    }

    private void Turn(int part, Vector3 axis, float angle)
    {
        if (_pivots[part] is { } pivot)
            pivot.Basis = new Basis(axis, angle);
    }
}
