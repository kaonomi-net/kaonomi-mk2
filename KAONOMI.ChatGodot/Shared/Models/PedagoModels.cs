using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>
/// An object of a scene of <c>/pedago</c>: its names by language, with their article (« une passoire », "a colander"),
/// and its outlines in the picture, in pixels (one polygon, or several: a ladle is a handle and a bowl).
/// </summary>
public sealed class PedagoObject
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("names")] public Dictionary<string, string> Names { get; set; } = [];
    [JsonPropertyName("shapes")] public List<List<double[]>> Shapes { get; set; } = [];

    /// <summary>The name in <paramref name="language"/> (FR, EN, DE…), else in French, else the first one written.</summary>
    public string Name(string language) =>
        Names.FirstOrDefault(n => n.Key.Equals(language, StringComparison.OrdinalIgnoreCase)).Value
        ?? Names.FirstOrDefault(n => n.Key.Equals("FR", StringComparison.OrdinalIgnoreCase)).Value
        ?? Names.Values.FirstOrDefault() ?? Id;

    /// <summary>The area of the smallest outline of the object holding the point (pixels of the picture); null when none does.</summary>
    public double? AreaAt(double x, double y)
    {
        double? smallest = null;
        foreach (var shape in Shapes)
            if (Contains(shape, x, y) && Area(shape) is var area && (smallest is null || area < smallest))
                smallest = area;
        return smallest;
    }

    private static bool Contains(List<double[]> shape, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = shape.Count - 1; i < shape.Count; j = i++)
        {
            var (xi, yi, xj, yj) = (shape[i][0], shape[i][1], shape[j][0], shape[j][1]);
            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static double Area(List<double[]> shape)
    {
        double twice = 0;
        for (int i = 0, j = shape.Count - 1; i < shape.Count; j = i++)
            twice += shape[j][0] * shape[i][1] - shape[i][0] * shape[j][1];
        return Math.Abs(twice) / 2;
    }
}

/// <summary>
/// A scene of <c>/pedago</c>: a picture and the objects known in it, each outlined by hand once, so that a click names
/// its object at once, without a model. Kept as <c>Assets/pedago/&lt;id&gt;.json</c> beside its picture, next to the
/// assembly of the chat.
/// </summary>
public sealed class PedagoScene
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("titles")] public Dictionary<string, string> Titles { get; set; } = [];
    [JsonPropertyName("image")] public string Image { get; set; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("objects")] public List<PedagoObject> Objects { get; set; } = [];

    /// <summary>The file of the picture.</summary>
    [JsonIgnore] public string ImagePath { get; private set; } = string.Empty;

    /// <summary>The folder of the scenes shipped with the chat.</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "pedago");

    public string Title(string language) =>
        Titles.FirstOrDefault(t => t.Key.Equals(language, StringComparison.OrdinalIgnoreCase)).Value
        ?? Titles.FirstOrDefault(t => t.Key.Equals("FR", StringComparison.OrdinalIgnoreCase)).Value
        ?? Titles.Values.FirstOrDefault() ?? Id;

    /// <summary>
    /// The object at a point of the picture (pixels): among the objects whose outline holds it, the one with the
    /// smallest outline (the spatula in the pan rather than the pan, the pan rather than the cooker); null: none.
    /// </summary>
    public PedagoObject? At(double x, double y)
    {
        PedagoObject? found = null;
        var smallest = double.MaxValue;
        foreach (var thing in Objects)
            if (thing.AreaAt(x, y) is { } area && area < smallest)
                (found, smallest) = (thing, area);
        return found;
    }

    /// <summary>The scene <paramref name="id"/> of <paramref name="folder"/> (<see cref="Folder"/> by default); null with the reason when it cannot be read.</summary>
    public static (PedagoScene? Scene, string? Error) Load(string id, string? folder = null)
    {
        var file = Path.Combine(folder ?? Folder, id + ".json");
        try
        {
            var scene = JsonSerializer.Deserialize<PedagoScene>(File.ReadAllText(file));
            if (scene is null || scene.Width <= 0 || scene.Height <= 0 || scene.Objects.Count == 0)
                return (null, $"{file} describes no scene (width, height, objects)");
            scene.Objects.RemoveAll(o => o.Shapes.Count == 0 || o.Shapes.Any(s => s.Count < 3 || s.Any(p => p.Length < 2)));
            scene.ImagePath = Path.Combine(Path.GetDirectoryName(file)!, scene.Image);
            return File.Exists(scene.ImagePath) ? (scene, null) : (null, $"the picture of the scene is missing: {scene.ImagePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, $"{file}: {ex.Message}");
        }
    }
}
