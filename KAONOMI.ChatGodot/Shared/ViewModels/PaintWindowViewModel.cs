using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The drawing program (<c>/paint</c>): a picture drawn with the usual tools (pencil, brush, spray, eraser, line,
/// rectangle, ellipse, fill, colour picker, text, selection), two colours (left and right click), undo, zoom, opened
/// from and saved to PNG/JPG/WEBP. The picture being drawn is kept in <c>~/kaonomi/paint/current.png</c> (drawn on
/// again when <c>/paint</c> opens), the pictures saved in <c>~/kaonomi/paint</c> by default; a picture saved again
/// under the same name has its older version kept aside (<c>history/</c> next to it), never erased.
/// </summary>
public sealed partial class PaintWindowViewModel : ViewModelBase
{
    /// <summary>The tools, in the order of the toolbar: (name, icon, key, tooltip).</summary>
    public static readonly (string Name, string Icon, string Key, string Tip)[] Tools =
    [
        ("Select", "⬚", "S", "Selection: drag a rectangle, then drag it to move it; Delete clears it, ⌘C/⌘X/⌘V copy, cut, paste; Enter or a click outside sets it down"),
        ("Pencil", "✏", "P", "Pencil: hard pixels, no smoothing"),
        ("Brush", "🖌", "B", "Brush: a round soft brush (its size, its hardness, its opacity)"),
        ("Spray", "💨", "A", "Spray: paint sprayed in dots"),
        ("Eraser", "⌫", "E", "Eraser: makes the picture transparent where it passes (the right colour instead with the right button)"),
        ("Line", "╱", "L", "Line (Shift: at 45°)"),
        ("Rectangle", "▭", "R", "Rectangle (Shift: a square); outline, filled or both"),
        ("Ellipse", "◯", "O", "Ellipse (Shift: a circle); outline, filled or both"),
        ("Fill", "🪣", "G", "Fill: the area of the same colour (the tolerance set) filled"),
        ("Picker", "💧", "I", "Colour picker: the colour clicked becomes the left colour (right click: the right colour)"),
        ("Text", "T", "T", "Text: click where it goes, type it, Enter"),
    ];

    public static readonly string[] ShapeModes = ["Outline", "Filled", "Both"];

    /// <summary>The usual palette (RGB hex).</summary>
    public static readonly string[] Palette =
    [
        "000000", "7f7f7f", "880015", "ed1c24", "ff7f27", "fff200", "22b14c", "00a2e8", "3f48cc", "a349a4", "8b4513", "ff69b4", "40e0d0", "1e3a5f",
        "ffffff", "c3c3c3", "b97a57", "ffaec9", "ffc90e", "efe4b0", "b5e61d", "99d9ea", "7092be", "c8bfe7", "deb887", "ffe4e1", "e0ffff", "708090",
    ];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "paint");

    public static string CurrentPicture => Path.Combine(Folder, "current.png");

    private static string SettingsPath => Path.Combine(Folder, "settings.json");

    [ObservableProperty]
    private string _tool = "Brush";

    /// <summary>The size of the brush, pencil, eraser, spray and shape outlines, in pixels.</summary>
    [ObservableProperty]
    private int _size = 8;

    /// <summary>The opacity of what is painted, 1 to 100.</summary>
    [ObservableProperty]
    private int _opacity = 100;

    /// <summary>The hardness of the brush and the eraser, 0 (soft) to 100 (hard).</summary>
    [ObservableProperty]
    private int _hardness = 70;

    /// <summary>How far a colour may be from the one clicked and still be filled, 0 to 100.</summary>
    [ObservableProperty]
    private int _tolerance = 15;

    [ObservableProperty]
    private string _shapeMode = "Outline";

    /// <summary>The size of the text, in pixels.</summary>
    [ObservableProperty]
    private int _fontSize = 32;

    /// <summary>The left colour (RRGGBBAA).</summary>
    [ObservableProperty]
    private string _primary = "000000ff";

    /// <summary>The right colour (RRGGBBAA).</summary>
    [ObservableProperty]
    private string _secondary = "ffffffff";

    /// <summary>The file the picture was opened from or saved to ("" for a new picture).</summary>
    [ObservableProperty]
    private string _filePath = "";

    /// <summary>The size of the picture, shown.</summary>
    [ObservableProperty]
    private string _pictureSize = "";

    /// <summary>Where the mouse is on the picture, and its colour, shown.</summary>
    [ObservableProperty]
    private string _cursor = "";

    [ObservableProperty]
    private string _zoomText = "100 %";

    /// <summary>True when the picture has changed since it was opened or saved.</summary>
    [ObservableProperty]
    private bool _modified;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isError;

    private sealed class Settings
    {
        public string Tool { get; set; } = "Brush";
        public int Size { get; set; } = 8;
        public int Opacity { get; set; } = 100;
        public int Hardness { get; set; } = 70;
        public int Tolerance { get; set; } = 15;
        public string ShapeMode { get; set; } = "Outline";
        public int FontSize { get; set; } = 32;
        public string Primary { get; set; } = "000000ff";
        public string Secondary { get; set; } = "ffffffff";
        public string FilePath { get; set; } = "";
    }

    public PaintWindowViewModel()
    {
        try
        {
            if (File.Exists(SettingsPath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) is { } s)
            {
                _tool = Array.Exists(Tools, t => t.Name == s.Tool) ? s.Tool : "Brush";
                _size = Math.Clamp(s.Size, 1, 200);
                _opacity = Math.Clamp(s.Opacity, 1, 100);
                _hardness = Math.Clamp(s.Hardness, 0, 100);
                _tolerance = Math.Clamp(s.Tolerance, 0, 100);
                _shapeMode = Array.IndexOf(ShapeModes, s.ShapeMode) >= 0 ? s.ShapeMode : "Outline";
                _fontSize = Math.Clamp(s.FontSize, 6, 400);
                _primary = s.Primary;
                _secondary = s.Secondary;
                _filePath = s.FilePath;
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Paint", "The settings could not be read: " + ex.Message);
        }
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Tool) or nameof(Size) or nameof(Opacity) or nameof(Hardness) or nameof(Tolerance) or nameof(ShapeMode) or nameof(FontSize) or nameof(Primary) or nameof(Secondary) or nameof(FilePath))
                SaveSettings();
        };
    }

    public void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var s = new Settings { Tool = Tool, Size = Size, Opacity = Opacity, Hardness = Hardness, Tolerance = Tolerance, ShapeMode = ShapeMode, FontSize = FontSize, Primary = Primary, Secondary = Secondary, FilePath = FilePath };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Json));
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Paint", "The settings could not be saved: " + ex.Message);
        }
    }

    /// <summary>A file about to be written over is copied aside first (<c>history/NAME-date.ext</c> next to it).</summary>
    public static void KeepOlder(string path)
    {
        if (!File.Exists(path))
            return;
        var history = Path.Combine(Path.GetDirectoryName(path) ?? Folder, "history");
        Directory.CreateDirectory(history);
        File.Copy(path, Path.Combine(history, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}"), true);
    }

    public void Say(string message, bool error = false)
    {
        Status = message;
        IsError = error;
    }
}
