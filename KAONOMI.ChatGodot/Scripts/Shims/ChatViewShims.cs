using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;

// The few types of the views of the Avalonia chat that its view models use: given here without their Avalonia
// controls, so that the view models are shared as they are (linked sources) with this Godot version.
namespace KAONOMI.Chat.Views
{
    /// <summary>A function drawn by the graph of /work.</summary>
    public sealed record GraphFunction(string Label, Func<double, double> Evaluate, Color Color);

    /// <summary>A slice of the pie of the languages of /completion.</summary>
    public sealed record PieSlice(string Label, int Value, IBrush Brush)
    {
        public double Share { get; init; }
    }

    /// <summary>What a window says about itself at one end of a link (links are not drawn in this version).</summary>
    public sealed record LinkEnd(string WindowName, string? ObjectId, string Context);

    /// <summary>A YouTube player as an end of a link: never linking in this version (no red string).</summary>
    public sealed class PlayerAnchor
    {
        public bool Linking => false;
    }

    /// <summary>The players reported by the YouTube page: nothing is kept (no red string in this version).</summary>
    public static class LinkAnchors
    {
        public static PlayerAnchor Report(string id, string videoId, string? title, PixelRect rect) => new();
        public static PlayerAnchor? Player(string id) => null;
    }

    /// <summary>The red string of the links: removed from this version.</summary>
    public static class LinkString
    {
        public static bool IsActive => false;
        public static void Start(PlayerAnchor anchor)
        {
        }
    }

    /// <summary>The image lines of the Markdown of the journal and of the tape of /work (the parts without controls).</summary>
    public static class JournalMarkdown
    {
        public static readonly Regex ImageLine = new(
            @"^[ \t]*!\[(?<alt>[^\]|]*)(\|(?<width>\d+))?\]\((?<url><[^>\n]+>|[^)\s]+)\)[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public const int DefaultImageWidth = 480;

        public static string LocalPath(string url)
        {
            var path = url.Trim().TrimStart('<').TrimEnd('>');
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var uri))
                return uri.LocalPath;
            return path;
        }

        public static string ImageMarkdown(string path, int width)
        {
            var alt = System.IO.Path.GetFileNameWithoutExtension(path).Replace("]", string.Empty).Replace("|", string.Empty);
            var link = path.Contains(' ') || path.Contains(')') ? $"<{path}>" : path;
            return $"![{alt}|{width}]({link})";
        }

        public static string SetImageWidth(string markdown, int index, int width)
        {
            var count = 0;
            return ImageLine.Replace(markdown, match =>
            {
                if (count++ != index)
                    return match.Value;
                var indent = match.Value[..(match.Value.Length - match.Value.TrimStart().Length)];
                return $"{indent}![{match.Groups["alt"].Value}|{width}]({match.Groups["url"].Value})";
            });
        }
    }
}

namespace KAONOMI.Chat.Themes
{
    /// <summary>The themes of the chat: the names of the former ones stay known (config.yml), only Kaonomi, Matrix, Kandinsky, MarieKilleuse, Ubuntu, Ghost, MacOS, IronMan, StarCitizen, CounterStrike, Defense, Minecraft, Mars, Couture, Silicon, Robotics, Aquarium, KaonomiDark (shown Kaonomi-Dark, once KaonomiNet) and KaonomiWhite (shown Kaonomi-White) are drawn.</summary>
    public enum AppThemeKind
    {
        Default, Kaonomi, Black, Cyberpunk, Matrix, Apple, Windows, Linux, Glass, Swift, Mari, Jarvis, Kandinsky, Pepper, Hal9000, Steam, Eve,
        Ghost,
        Analyst,
        Minimal,
        Defense,
        Police,
        Trading,
        MarieKilleuse,
        Ubuntu,
        MacOS,
        IronMan,
        StarCitizen,
        CounterStrike,
        Minecraft,
        Mars,
        Couture,
        Silicon,
        Robotics,
        KaonomiDark,
        KaonomiWhite,
        Aquarium
    }

    /// <summary>The theme in use; this version draws one look only, the choice is kept in config.yml.</summary>
    public static class AppTheme
    {
        public static IReadOnlyList<(AppThemeKind Kind, string Description)> All { get; } =
        [
            (AppThemeKind.Kaonomi, "Light · white paper, turquoise and gold, in the style of www.kaonomi.net"),
            (AppThemeKind.Matrix, "Dark · the digital rain, phosphor green on black and the monospace of a terminal, in the style of the film"),
            (AppThemeKind.Kandinsky, "Light · cream paper, circles in their halos, black lines and primary colours, after the abstractions of Kandinsky"),
            (AppThemeKind.MarieKilleuse, "Light · half kawaii, half gore: pastel pink, hearts and stars, then blood running down, after the doll with a knife"),
            (AppThemeKind.Ubuntu, "Dark · the desktop of Ubuntu: charcoal panels and Ubuntu orange on its aubergine wallpaper with slow ribbons"),
            (AppThemeKind.Ghost, "Dark · a cyborg on a rooftop over the megacity at night, neons breathing in the mist and the rain, after Ghost in the Shell"),
            (AppThemeKind.MacOS, "Light · the desktop of macOS: frosted glass panels, system blue, on the colourful hills of its wallpapers"),
            (AppThemeKind.IronMan, "Dark · red armour plates with gold seams, the arc reactor glowing and the rings of a HUD, after Iron Man"),
            (AppThemeKind.StarCitizen, "Dark · deep space, a gas giant and a ship passing by, in the cyan holograms of a mobiGlas, after the game Star Citizen"),
            (AppThemeKind.CounterStrike, "Dark · a fight in a desert town between the sites A and B, smoke drifting and embers rising, after Counter-Strike 2"),
            (AppThemeKind.Defense, "Dark · a military theme: soldiers, a tank and helicopters on a field of dust, olive and sand with a tactical green"),
            (AppThemeKind.Minecraft, "Dark · a valley of blocks at sunset, clouds of blocks and a shimmering river, grass green and splash yellow, after Minecraft"),
            (AppThemeKind.Mars, "Dark · the rover Curiosity of NASA in a crater of Mars at sunset, red dust blowing, rust and peach orange"),
            (AppThemeKind.Couture, "Light · the sketches of a Paris haute couture collection: cream paper, ink, navy velvet and gold embroidery"),
            (AppThemeKind.Silicon, "Dark · the die of a processor on its circuit board, pulses of signal along the traces, teal, copper and gold"),
            (AppThemeKind.Robotics, "Light · a humanoid robot in a robotics lab, drones and tools, daylight from the windows, aluminium, blue and orange"),
            (AppThemeKind.Aquarium, "Dark · a planted aquarium: plants swaying, caustics dancing on the moss and the stones, bubbles rising, a school of neon tetras"),
            (AppThemeKind.KaonomiDark, "Dark · the picture of www.kaonomi.net: the hooded robot with its golden eye among circuits and networks of agents, night blue and gold"),
            (AppThemeKind.KaonomiWhite, "Light · Kaonomi-Dark in white on the same picture of www.kaonomi.net, alive: bluish white panels, night-blue text, gold and cyan")
        ];

        public static AppThemeKind Current { get; private set; } = AppThemeKind.Kaonomi;

        /// <summary>The theme shown (set by the shell when it applies one).</summary>
        public static void SetCurrent(AppThemeKind kind) => Current = kind;

        /// <summary>Raised when a theme is applied.</summary>
        public static event Action<AppThemeKind>? Applied;

        public static string ToName(AppThemeKind kind) => kind switch
        {
            AppThemeKind.Default => "white",
            AppThemeKind.KaonomiDark => "kaonomi-dark",
            AppThemeKind.KaonomiWhite => "kaonomi-white",
            _ => kind.ToString().ToLowerInvariant()
        };

        public static bool TryParse(string? name, out AppThemeKind kind)
        {
            kind = AppThemeKind.Default;
            var trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed) || char.IsDigit(trimmed[0]))
                return false;
            // Hyphens aside (kaonomi-dark is KaonomiDark), and the former name of a theme (kaonominet) stands for it.
            trimmed = trimmed.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            if (string.Equals(trimmed, "KaonomiNet", StringComparison.OrdinalIgnoreCase))
                trimmed = nameof(AppThemeKind.KaonomiDark);
            // Only the themes drawn (Kaonomi, Matrix, Kandinsky, MarieKilleuse, Ubuntu, Ghost, MacOS, IronMan, StarCitizen, CounterStrike, Defense, Minecraft, Mars, Couture, Silicon, Robotics, Aquarium, KaonomiDark, KaonomiWhite): the other names are no longer a theme.
            if (!Enum.TryParse(trimmed, ignoreCase: true, out AppThemeKind found) || !Enum.IsDefined(found))
                return false;
            foreach (var theme in All)
                if (theme.Kind == found)
                {
                    kind = found;
                    return true;
                }
            return false;
        }

        public static void Apply(AppThemeKind kind)
        {
            Current = kind;
            Applied?.Invoke(kind);
        }
    }
}
