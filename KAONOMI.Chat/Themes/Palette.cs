using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using KAONOMI.Chat.Services;

namespace KAONOMI.UI.Themes;

/// <summary>
/// A theme of the desk, as in the Godot chat (<c>Scripts/Eve/Eve.cs</c>): its colours, its fonts, the shader of its
/// animated background (the <c>.gdshader</c> of the Godot chat, translated to GLSL by <see cref="GodotShader"/>) and the
/// picture under it when it has one.
/// </summary>
public sealed record Palette(
    string Name,
    string Description,
    bool IsLight,
    Color Space, Color Panel, Color PanelSolid, Color Header, Color Field,
    Color Edge, Color EdgeBright, Color Text, Color TextBright, Color Muted,
    Color Accent, Color Warm, Color Red, Color Green,
    string[] BodyFonts, string[] DisplayFonts, int DisplayWeight,
    string Background,
    string? Picture = null,
    string? Stamp = null,
    int StampWidth = 384)
{
    /// <summary>The file of the shader of the background (Shaders next to the program).</summary>
    public string ShaderPath => Path.Combine(AppContext.BaseDirectory, "Shaders", Background);

    /// <summary>The file of the picture under the shader (Assets next to the program), null without one.</summary>
    public string? PicturePath => Picture is null ? null : Path.Combine(AppContext.BaseDirectory, "Assets", Picture);

    /// <summary>The file of the picture the shader draws itself (its <c>stamp</c> uniform), Assets next to the program; null without one.</summary>
    public string? StampPath => Stamp is null ? null : Path.Combine(AppContext.BaseDirectory, "Assets", Stamp);

    /// <summary>A colour of the Godot chat: its channels from 0 to 1.</summary>
    private static Color C(float r, float g, float b, float a = 1) =>
        Color.FromArgb((byte)Math.Round(a * 255), (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));

    /// <summary>A colour of the Godot chat written #RRGGBB or #RRGGBBAA (the alpha last, as in Godot).</summary>
    private static Color H(string html)
    {
        var hex = html.TrimStart('#');
        var alpha = hex.Length == 8 ? Convert.ToByte(hex[6..], 16) : (byte)255;
        return Color.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    public static readonly Palette Kaonomi = new(
        "Kaonomi", "Light · white paper, turquoise and gold, in the style of www.kaonomi.net", true,
        Space: H("#F3F8FC"),
        Panel: H("#FFFFFFFA"),
        PanelSolid: H("#FFFFFF"),
        Header: H("#F4F6F8"),
        Field: H("#FFFFFF"),
        Edge: H("#CBD3DA"),
        EdgeBright: H("#7D8B96"),
        Text: H("#212529"),
        TextBright: H("#0B0D0F"),
        Muted: H("#6C757D"),
        Accent: H("#1A8FA8"),
        Warm: H("#B18D1D"),
        Red: H("#C0392B"),
        Green: H("#2E8B57"),
        BodyFonts: ["Rubik", "Inter", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Rubik", "Inter", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "kaonomi_paper.gdshader");

    public static readonly Palette Matrix = new(
        "Matrix", "Dark · the digital rain, phosphor green on black and the monospace of a terminal, in the style of the film", false,
        Space: C(0.0f, 0.016f, 0.004f),
        Panel: C(0.0f, 0.04f, 0.012f, 0.92f),
        PanelSolid: C(0.004f, 0.05f, 0.016f, 0.97f),
        Header: C(0.01f, 0.09f, 0.03f, 0.95f),
        Field: C(0.0f, 0.0f, 0.0f, 0.45f),
        Edge: C(0.0f, 0.55f, 0.18f, 0.5f),
        EdgeBright: C(0.2f, 0.85f, 0.35f, 0.85f),
        Text: C(0.72f, 0.95f, 0.75f),
        TextBright: C(0.85f, 1.0f, 0.88f),
        Muted: C(0.40f, 0.70f, 0.45f),
        Accent: C(0.0f, 1.0f, 0.25f),
        Warm: C(0.75f, 0.95f, 0.2f),
        Red: C(0.95f, 0.3f, 0.25f),
        Green: C(0.2f, 0.9f, 0.35f),
        BodyFonts: ["Menlo", "SF Mono", "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New"],
        DisplayFonts: ["Menlo", "SF Mono", "Consolas", "Cascadia Mono", "Courier New"],
        DisplayWeight: 700,
        Background: "matrix_rain.gdshader");

    /// <summary>
    /// Kandinsky: his geometric abstractions of the Bauhaus years (Composition VIII). A warm cream paper, panels of a
    /// lighter cream, an ink-black text, the blue of his circles for what is active, his yellow for what calls, his red
    /// for alarms, the geometric capitals of the Bauhaus (Futura) for the headers; behind the desk the composition
    /// drifts slowly: circles in their halos, black lines, a yellow triangle, a checkerboard, arcs.
    /// </summary>
    public static readonly Palette Kandinsky = new(
        "Kandinsky", "Light · cream paper, circles in their halos, black lines and primary colours, after the abstractions of Kandinsky", true,
        Space: H("#F3EDE0"),
        Panel: H("#FBF8F1"),
        PanelSolid: H("#FCFAF5"),
        Header: H("#FBF8F100"),
        Field: H("#FFFFFF"),
        Edge: H("#DDD3C1"),
        EdgeBright: H("#A89C87"),
        Text: H("#2A2622"),
        TextBright: H("#141210"),
        Muted: H("#7B7266"),
        Accent: H("#2451A8"),
        Warm: H("#E0A118"),
        Red: H("#D2372A"),
        Green: H("#3E8E5A"),
        BodyFonts: ["Avenir Next", "Futura", "Century Gothic", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Futura", "Century Gothic", "Avenir Next", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "kandinsky_composition.gdshader");

    /// <summary>
    /// MarieKilleuse: half kawaii, half gore, like the robot doll with a knife of the avatar. Pastel pink panels, a
    /// plum text, the hot pink of her heart for what is active, the mint of her headphones for what calls, the red of
    /// blood for alarms, rounded capitals; behind the desk a pink sheet of polka dots, floating hearts, stars and
    /// sparkles, cut by a stitched seam from a dark crimson half, with blood running from the top and splattered.
    /// </summary>
    public static readonly Palette MarieKilleuse = new(
        "MarieKilleuse", "Light · half kawaii, half gore: pastel pink, hearts and stars, then blood running down, after the doll with a knife", true,
        Space: H("#FFE6F0"),
        Panel: H("#FFF3F7"),
        PanelSolid: H("#FFF6F9"),
        Header: H("#FFF3F700"),
        Field: H("#FFFFFF"),
        Edge: H("#F1BFD2"),
        EdgeBright: H("#D98AA8"),
        Text: H("#3A1F2B"),
        TextBright: H("#22111A"),
        Muted: H("#946A7C"),
        Accent: H("#E2357B"),
        Warm: H("#1FA39A"),
        Red: H("#B0001C"),
        Green: H("#36A874"),
        BodyFonts: ["Avenir Next", "Nunito", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Arial Rounded MT Bold", "Varela Round", "Nunito", "Avenir Next", "Arial"],
        DisplayWeight: 700,
        Background: "killeuse_doll.gdshader",
        Stamp: "mariekilleuse-face.png");

    /// <summary>
    /// Ubuntu: the desktop of Ubuntu (its Yaru look, dark). Charcoal grey panels, a light grey text, the orange of
    /// Ubuntu for what is active, a warm yellow for what calls, its red for errors, the Ubuntu font when it is
    /// installed; behind the desk the aubergine of its wallpapers turning to orange, with translucent ribbons in slow
    /// waves and soft rising lights.
    /// </summary>
    public static readonly Palette Ubuntu = new(
        "Ubuntu", "Dark · the desktop of Ubuntu: charcoal panels and Ubuntu orange on its aubergine wallpaper with slow ribbons", false,
        Space: H("#2C001E"),
        Panel: H("#2B2B2BEB"),
        PanelSolid: H("#303030F8"),
        Header: H("#242424F2"),
        Field: H("#1D1D1DD9"),
        Edge: H("#4C4C4CC0"),
        EdgeBright: H("#7D7D7D"),
        Text: H("#E3E3E3"),
        TextBright: H("#FFFFFF"),
        Muted: H("#A3A3A3"),
        Accent: H("#E95420"),
        Warm: H("#F4A100"),
        Red: H("#E0313B"),
        Green: H("#3EB34F"),
        BodyFonts: ["Ubuntu", "Ubuntu Sans", "Cantarell", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Ubuntu", "Ubuntu Sans", "Cantarell", "Helvetica Neue", "Arial"],
        DisplayWeight: 500,
        Background: "ubuntu_waves.gdshader");

    /// <summary>
    /// Ghost: after Ghost in the Shell, on the picture of a cyborg woman on a rooftop over a megacity at night (Assets/
    /// ghost-rooftop.jpg). Panels of a dark teal glass, a pale cyan text, the teal of its neons for what is active, their
    /// pink for what calls, condensed capitals for the headers; behind the desk the picture itself, alive: a slow
    /// travelling, the neons breathing, mist drifting, thin rain.
    /// </summary>
    public static readonly Palette Ghost = new(
        "Ghost", "Dark · a cyborg on a rooftop over the megacity at night, neons breathing in the mist and the rain, after Ghost in the Shell", false,
        Space: C(0.02f, 0.05f, 0.06f),
        Panel: C(0.025f, 0.06f, 0.075f, 0.84f),
        PanelSolid: C(0.03f, 0.07f, 0.085f, 0.97f),
        Header: C(0.05f, 0.11f, 0.13f, 0.93f),
        Field: C(0.0f, 0.02f, 0.03f, 0.55f),
        Edge: C(0.2f, 0.55f, 0.55f, 0.5f),
        EdgeBright: C(0.45f, 0.9f, 0.85f, 0.85f),
        Text: C(0.8f, 0.9f, 0.92f),
        TextBright: C(0.94f, 0.99f, 1.0f),
        Muted: C(0.48f, 0.63f, 0.66f),
        Accent: C(0.18f, 0.9f, 0.78f),
        Warm: C(1.0f, 0.32f, 0.58f),
        Red: C(1.0f, 0.25f, 0.32f),
        Green: C(0.25f, 0.88f, 0.55f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["Avenir Next Condensed", "Bahnschrift", "Eurostile", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "ghost_rooftop.gdshader",
        Stamp: "ghost-rooftop.jpg",
        StampWidth: 4096);

    /// <summary>
    /// MacOS: after the desktop of macOS. Panels of a light frosted glass, a near-black text, the system blue for what
    /// is active, its orange for what calls, its red and green for the states, the San Francisco fonts when they are
    /// installed; behind the desk the colourful hills of its recent wallpapers, swaying very slowly.
    /// </summary>
    public static readonly Palette MacOS = new(
        "MacOS", "Light · the desktop of macOS: frosted glass panels, system blue, on the colourful hills of its wallpapers", true,
        Space: H("#E9ECF2"),
        Panel: H("#F6F6F8F2"),
        PanelSolid: H("#F8F8FA"),
        Header: H("#F6F6F800"),
        Field: H("#FFFFFF"),
        Edge: H("#D5D5DA"),
        EdgeBright: H("#A1A1A8"),
        Text: H("#1D1D1F"),
        TextBright: H("#000000"),
        Muted: H("#6E6E73"),
        Accent: H("#007AFF"),
        Warm: H("#FF9500"),
        Red: H("#FF3B30"),
        Green: H("#34C759"),
        BodyFonts: ["SF Pro Text", "SF Pro", "Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["SF Pro Display", "SF Pro", "Helvetica Neue", "Inter", "Arial"],
        DisplayWeight: 600,
        Background: "macos_hills.gdshader");

    /// <summary>
    /// IronMan: after the armour of Iron Man and the head-up display of its helmet. Panels of a dark gunmetal with a red
    /// cast, a warm white text, the blue-white of the arc reactor for what is active, the gold of the armour for what
    /// calls, its red for alarms, wide technical capitals for the headers; behind the desk the red armour plates with
    /// their gold seams, the arc reactor glowing and the rings of the HUD turning slowly.
    /// </summary>
    public static readonly Palette IronMan = new(
        "IronMan", "Dark · red armour plates with gold seams, the arc reactor glowing and the rings of a HUD, after Iron Man", false,
        Space: C(0.045f, 0.015f, 0.02f),
        Panel: C(0.065f, 0.04f, 0.042f, 0.86f),
        PanelSolid: C(0.075f, 0.045f, 0.048f, 0.97f),
        Header: C(0.14f, 0.05f, 0.055f, 0.95f),
        Field: C(0.02f, 0.01f, 0.012f, 0.55f),
        Edge: C(0.7f, 0.5f, 0.2f, 0.45f),
        EdgeBright: C(0.95f, 0.72f, 0.3f, 0.85f),
        Text: C(0.93f, 0.9f, 0.86f),
        TextBright: C(1.0f, 0.98f, 0.95f),
        Muted: C(0.66f, 0.58f, 0.54f),
        Accent: C(0.42f, 0.88f, 1.0f),
        Warm: C(0.96f, 0.72f, 0.24f),
        Red: C(0.9f, 0.14f, 0.17f),
        Green: C(0.3f, 0.82f, 0.55f),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Eurostile", "Bank Gothic", "Avenir Next Condensed", "Bahnschrift", "Arial"],
        DisplayWeight: 700,
        Background: "ironman_hud.gdshader");

    /// <summary>
    /// StarCitizen: after the space game Star Citizen and its mobiGlas. Panels of a dark blue glass, a pale cyan text,
    /// the cyan of the mobiGlas for what is active, amber for what calls, wide condensed capitals for the headers; behind
    /// the desk deep space with a nebula and stars drifting, a star with its flare, a gas giant turning slowly, a ship
    /// crossing now and then, the brackets and the scanner of a HUD.
    /// </summary>
    public static readonly Palette StarCitizen = new(
        "StarCitizen", "Dark · deep space, a gas giant and a ship passing by, in the cyan holograms of a mobiGlas, after the game Star Citizen", false,
        Space: C(0.008f, 0.016f, 0.04f),
        Panel: C(0.02f, 0.05f, 0.08f, 0.84f),
        PanelSolid: C(0.025f, 0.06f, 0.095f, 0.97f),
        Header: C(0.04f, 0.1f, 0.15f, 0.93f),
        Field: C(0.0f, 0.02f, 0.04f, 0.55f),
        Edge: C(0.2f, 0.6f, 0.7f, 0.45f),
        EdgeBright: C(0.45f, 0.88f, 0.95f, 0.85f),
        Text: C(0.82f, 0.92f, 0.96f),
        TextBright: C(0.95f, 0.99f, 1.0f),
        Muted: C(0.5f, 0.64f, 0.72f),
        Accent: C(0.31f, 0.82f, 0.91f),
        Warm: C(0.95f, 0.63f, 0.19f),
        Red: C(0.95f, 0.28f, 0.25f),
        Green: C(0.3f, 0.86f, 0.58f),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Avenir Next Condensed", "Eurostile", "Bahnschrift", "Helvetica Neue", "Arial"],
        DisplayWeight: 600,
        Background: "starcitizen_space.gdshader");

    /// <summary>
    /// CounterStrike: after the menus and the radar of Counter-Strike 2. Panels of a dark slate, a light grey text, the
    /// orange of the game for what is active, its gold for what calls, a red for alarms, bold condensed capitals for the
    /// headers; behind the desk the picture of a fight in a desert town (Assets/cs-match.jpg), alive: a slow travelling,
    /// smoke drifting, embers rising, dust floating, the diode of the bomb pulsing.
    /// </summary>
    public static readonly Palette CounterStrike = new(
        "CounterStrike", "Dark · a fight in a desert town between the sites A and B, smoke drifting and embers rising, after Counter-Strike 2", false,
        Space: C(0.055f, 0.065f, 0.08f),
        Panel: C(0.075f, 0.085f, 0.1f, 0.88f),
        PanelSolid: C(0.085f, 0.095f, 0.11f, 0.97f),
        Header: C(0.12f, 0.13f, 0.15f, 0.95f),
        Field: C(0.03f, 0.035f, 0.045f, 0.6f),
        Edge: C(0.35f, 0.37f, 0.4f, 0.55f),
        EdgeBright: C(0.62f, 0.6f, 0.55f, 0.85f),
        Text: C(0.88f, 0.88f, 0.86f),
        TextBright: C(1.0f, 1.0f, 0.98f),
        Muted: C(0.6f, 0.61f, 0.62f),
        Accent: C(0.95f, 0.55f, 0.15f),
        Warm: C(0.98f, 0.78f, 0.3f),
        Red: C(0.92f, 0.25f, 0.22f),
        Green: C(0.4f, 0.85f, 0.4f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["Avenir Next Condensed", "DIN Condensed", "Bahnschrift Condensed", "Arial Narrow", "Arial"],
        DisplayWeight: 800,
        Background: "cs2_match.gdshader",
        Stamp: "cs-match.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Defense: a military theme, on the picture of soldiers of the French army with a tank, an armoured vehicle and
    /// helicopters on a field of dust (Assets/defense-field.jpg). Panels of a dark olive, a sand text, a tactical green
    /// for what is active, amber for what calls, condensed DIN capitals for the headers; behind the desk the picture
    /// itself, alive: a slow travelling, dust drifting, motes floating, the headlights glowing.
    /// </summary>
    public static readonly Palette Defense = new(
        "Defense", "Dark · a military theme: soldiers, a tank and helicopters on a field of dust, olive and sand with a tactical green", false,
        Space: C(0.07f, 0.075f, 0.055f),
        Panel: C(0.09f, 0.095f, 0.07f, 0.86f),
        PanelSolid: C(0.1f, 0.105f, 0.078f, 0.97f),
        Header: C(0.15f, 0.155f, 0.11f, 0.95f),
        Field: C(0.04f, 0.045f, 0.03f, 0.6f),
        Edge: C(0.5f, 0.47f, 0.33f, 0.5f),
        EdgeBright: C(0.75f, 0.7f, 0.5f, 0.85f),
        Text: C(0.9f, 0.87f, 0.76f),
        TextBright: C(0.97f, 0.95f, 0.86f),
        Muted: C(0.66f, 0.63f, 0.52f),
        Accent: C(0.58f, 0.76f, 0.36f),
        Warm: C(0.95f, 0.7f, 0.25f),
        Red: C(0.86f, 0.3f, 0.25f),
        Green: C(0.5f, 0.75f, 0.4f),
        BodyFonts: ["DIN Alternate", "Bahnschrift", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["DIN Condensed", "Bahnschrift Condensed", "Avenir Next Condensed", "Arial Narrow", "Arial"],
        DisplayWeight: 700,
        Background: "defense_field.gdshader",
        Stamp: "defense-field.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Minecraft: after the game Minecraft, on the picture of a valley of blocks at sunset (Assets/minecraft-valley.jpg).
    /// Dark translucent panels like the menus of the game, a white text, the green of the grass for what is active, the
    /// yellow of its splash texts for what calls, a pixel font for the headers when one is installed; behind the desk the
    /// picture itself, alive: a slow travelling, clouds of blocks, the sun breathing, the river shimmering, square motes.
    /// </summary>
    public static readonly Palette Minecraft = new(
        "Minecraft", "Dark · a valley of blocks at sunset, clouds of blocks and a shimmering river, grass green and splash yellow, after Minecraft", false,
        Space: C(0.08f, 0.1f, 0.07f),
        Panel: C(0.1f, 0.1f, 0.1f, 0.84f),
        PanelSolid: C(0.12f, 0.12f, 0.12f, 0.97f),
        Header: C(0.18f, 0.18f, 0.18f, 0.95f),
        Field: C(0.0f, 0.0f, 0.0f, 0.55f),
        Edge: C(0.4f, 0.4f, 0.4f, 0.6f),
        EdgeBright: C(0.75f, 0.75f, 0.75f, 0.9f),
        Text: C(0.92f, 0.92f, 0.92f),
        TextBright: C(1.0f, 1.0f, 1.0f),
        Muted: C(0.63f, 0.63f, 0.63f),
        Accent: C(0.36f, 0.73f, 0.25f),
        Warm: C(0.99f, 0.86f, 0.02f),
        Red: C(0.9f, 0.25f, 0.2f),
        Green: C(0.45f, 0.8f, 0.3f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["Minecraft", "Minecraftia", "Press Start 2P", "Silkscreen", "Menlo", "Courier New"],
        DisplayWeight: 400,
        Background: "minecraft_valley.gdshader",
        Stamp: "minecraft-valley.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Mars: after the rover Curiosity of NASA (Mars Science Laboratory), on the picture of the rover in a crater at
    /// sunset (Assets/mars-curiosity.jpg). Panels of a dark rust, a light sand text, the peach orange of its title for
    /// what is active, the butterscotch of the sun for what calls, the red of NASA for alarms, condensed capitals for the
    /// headers; behind the desk the picture itself, alive: a slow travelling, red dust blowing, the sun breathing, stars.
    /// </summary>
    public static readonly Palette Mars = new(
        "Mars", "Dark · the rover Curiosity of NASA in a crater of Mars at sunset, red dust blowing, rust and peach orange", false,
        Space: C(0.09f, 0.045f, 0.035f),
        Panel: C(0.11f, 0.06f, 0.045f, 0.86f),
        PanelSolid: C(0.12f, 0.066f, 0.05f, 0.97f),
        Header: C(0.18f, 0.09f, 0.06f, 0.95f),
        Field: C(0.05f, 0.025f, 0.018f, 0.6f),
        Edge: C(0.6f, 0.35f, 0.22f, 0.5f),
        EdgeBright: C(0.9f, 0.6f, 0.4f, 0.85f),
        Text: C(0.95f, 0.88f, 0.8f),
        TextBright: C(1.0f, 0.96f, 0.9f),
        Muted: C(0.72f, 0.6f, 0.52f),
        Accent: C(0.95f, 0.6f, 0.35f),
        Warm: C(1.0f, 0.82f, 0.5f),
        Red: C(0.99f, 0.24f, 0.13f),
        Green: C(0.5f, 0.8f, 0.5f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["DIN Condensed", "Avenir Next Condensed", "Bahnschrift Condensed", "Futura", "Arial Narrow", "Arial"],
        DisplayWeight: 700,
        Background: "mars_curiosity.gdshader",
        Stamp: "mars-curiosity.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Couture: after the haute couture of Paris, on the sketches of a collection on the table of an atelier
    /// (Assets/couture-sketches.jpg). Panels of a cream paper, an ink black text, the navy of velvet for what is active,
    /// the gold of the embroideries for what calls, burgundy for alarms, Didot capitals for the headers; behind the desk
    /// the picture itself, alive: a slow travelling, a soft light passing, flecks of gold drifting.
    /// </summary>
    public static readonly Palette Couture = new(
        "Couture", "Light · the sketches of a Paris haute couture collection: cream paper, ink, navy velvet and gold embroidery", true,
        Space: H("#F2EADB"),
        Panel: H("#FBF7EFF4"),
        PanelSolid: H("#FCF9F2"),
        Header: H("#FBF7EF00"),
        Field: H("#FFFDF8"),
        Edge: H("#DCCFB8"),
        EdgeBright: H("#A8977A"),
        Text: H("#2B2622"),
        TextBright: H("#111014"),
        Muted: H("#857866"),
        Accent: H("#1F2B52"),
        Warm: H("#B8913A"),
        Red: H("#8E1F2F"),
        Green: H("#4E7A57"),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Didot", "Bodoni 72", "Bodoni MT", "Playfair Display", "Georgia", "Times New Roman"],
        DisplayWeight: 400,
        Background: "couture_atelier.gdshader",
        Stamp: "couture-sketches.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Silicon: after microelectronics, on the picture of the die of a processor on its circuit board
    /// (Assets/silicon-die.jpg). Panels of a dark teal like the solder mask, a pale silver text, the cyan of a signal for
    /// what is active, the gold of the die for what calls, technical capitals for the headers; behind the desk the
    /// picture itself, alive: a slow travelling, pulses of signal along the traces, an iridescent sheen over the die.
    /// </summary>
    public static readonly Palette Silicon = new(
        "Silicon", "Dark · the die of a processor on its circuit board, pulses of signal along the traces, teal, copper and gold", false,
        Space: C(0.02f, 0.07f, 0.09f),
        Panel: C(0.025f, 0.075f, 0.09f, 0.86f),
        PanelSolid: C(0.03f, 0.085f, 0.1f, 0.97f),
        Header: C(0.05f, 0.13f, 0.15f, 0.95f),
        Field: C(0.0f, 0.03f, 0.04f, 0.55f),
        Edge: C(0.3f, 0.5f, 0.52f, 0.5f),
        EdgeBright: C(0.75f, 0.68f, 0.5f, 0.85f),
        Text: C(0.84f, 0.89f, 0.9f),
        TextBright: C(0.96f, 0.98f, 0.98f),
        Muted: C(0.52f, 0.62f, 0.64f),
        Accent: C(0.25f, 0.78f, 0.75f),
        Warm: C(0.88f, 0.66f, 0.35f),
        Red: C(0.9f, 0.33f, 0.29f),
        Green: C(0.28f, 0.76f, 0.48f),
        BodyFonts: ["Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["DIN Alternate", "Eurostile", "Bahnschrift", "Menlo", "Arial"],
        DisplayWeight: 600,
        Background: "silicon_die.gdshader",
        Stamp: "silicon-die.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Robotics: after a robotics lab, on the picture of a humanoid robot in a workshop with drones, tools and monitors
    /// (Assets/robotics-lab.jpg). Panels of a light aluminium grey, a graphite text, the blue of the cables and benches
    /// for what is active, the orange of the tools for what calls, condensed technical capitals for the headers; behind
    /// the desk the picture itself, alive: a slow travelling, shafts of daylight, dust, the robot's light pulsing.
    /// </summary>
    public static readonly Palette Robotics = new(
        "Robotics", "Light · a humanoid robot in a robotics lab, drones and tools, daylight from the windows, aluminium, blue and orange", true,
        Space: H("#E4E6E8"),
        Panel: H("#F4F5F6F2"),
        PanelSolid: H("#F6F7F8"),
        Header: H("#F4F5F600"),
        Field: H("#FFFFFF"),
        Edge: H("#C9CED3"),
        EdgeBright: H("#8E969E"),
        Text: H("#24292E"),
        TextBright: H("#0E1114"),
        Muted: H("#6A737C"),
        Accent: H("#1F6FD1"),
        Warm: H("#F08A24"),
        Red: H("#D93A3A"),
        Green: H("#2E9E5B"),
        BodyFonts: ["Avenir Next", "Helvetica Neue", "Inter", "Segoe UI", "Arial"],
        DisplayFonts: ["DIN Condensed", "Avenir Next Condensed", "Bahnschrift Condensed", "Arial Narrow", "Arial"],
        DisplayWeight: 700,
        Background: "robotics_lab.gdshader",
        Stamp: "robotics-lab.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Kaonomi-Dark (once KaonomiNet): after www.kaonomi.net, on the picture of the site (Assets/kaonomi-net.jpg): the hooded robot of
    /// Kaonomi with its golden eye among its circuits and networks of agents. Panels of a night blue, a pale text, the
    /// gold of ".net" for what is active, the cyan of the circuits for what calls, the Rubik font of the site; behind the
    /// desk the picture shown whole, alive: the eye breathing, pulses along the circuits, the nodes twinkling.
    /// </summary>
    public static readonly Palette KaonomiDark = new(
        "Kaonomi-Dark", "Dark · the picture of www.kaonomi.net: the hooded robot with its golden eye among circuits and networks of agents, night blue and gold", false,
        Space: C(0.02f, 0.05f, 0.1f),
        Panel: C(0.03f, 0.065f, 0.12f, 0.86f),
        PanelSolid: C(0.035f, 0.075f, 0.135f, 0.97f),
        Header: C(0.05f, 0.1f, 0.18f, 0.95f),
        Field: C(0.0f, 0.02f, 0.05f, 0.55f),
        Edge: C(0.3f, 0.45f, 0.62f, 0.5f),
        EdgeBright: C(0.85f, 0.68f, 0.36f, 0.85f),
        Text: C(0.86f, 0.9f, 0.95f),
        TextBright: C(0.97f, 0.98f, 1.0f),
        Muted: C(0.55f, 0.63f, 0.73f),
        Accent: C(0.88f, 0.69f, 0.29f),
        Warm: C(0.31f, 0.76f, 0.97f),
        Red: C(0.92f, 0.32f, 0.3f),
        Green: C(0.33f, 0.8f, 0.55f),
        BodyFonts: ["Rubik", "Inter", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Rubik", "Inter", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "kaonomi_net.gdshader",
        Stamp: "kaonomi-net.jpg",
        StampWidth: 4096);

    /// <summary>
    /// Kaonomi-White: Kaonomi-Dark in light, on the same picture of www.kaonomi.net (Assets/kaonomi-net.jpg), alive the
    /// same way (the eye breathing, pulses along the circuits, the nodes twinkling): panels of a bluish white, almost
    /// opaque, a night-blue text, the gold of ".net" (darker, to be read on white) for what is active, the cyan of the
    /// circuits for what calls, the Rubik font of the site.
    /// </summary>
    public static readonly Palette KaonomiWhite = new(
        "Kaonomi-White", "Light · Kaonomi-Dark in white on the same picture of www.kaonomi.net, alive: bluish white panels, night-blue text, gold and cyan", true,
        Space: H("#E8EDF4"),
        Panel: H("#F7F9FCF2"),
        PanelSolid: H("#F8FAFC"),
        Header: H("#F7F9FC00"),
        Field: H("#FFFFFF"),
        Edge: H("#C8D2DE"),
        EdgeBright: H("#C49A45"),
        Text: H("#16202E"),
        TextBright: H("#0A0F18"),
        Muted: H("#5E6B7C"),
        Accent: H("#B5841C"),
        Warm: H("#1C8FD0"),
        Red: H("#D23C3A"),
        Green: H("#2E9E5B"),
        BodyFonts: ["Rubik", "Inter", "Helvetica Neue", "Segoe UI", "Arial"],
        DisplayFonts: ["Rubik", "Inter", "Helvetica Neue", "Arial"],
        DisplayWeight: 700,
        Background: "kaonomi_net.gdshader",
        Stamp: "kaonomi-net.jpg",
        StampWidth: 4096);
}

/// <summary>
/// The themes of the desk and the one in use. A theme is applied at once, without building the interface again: the
/// brushes of <c>Styles/Kaonomi.axaml</c>, which every view uses (<c>Ui.Accent</c>, <c>Ui.Panel</c>…), are recoloured in
/// place, the fonts follow the window, the Fluent controls go light or dark, and the background shader is replaced. The
/// choice is kept in <c>~/kaonomi/ui-theme.txt</c> (the Godot chat keeps its own).
/// </summary>
public static class DeskTheme
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "kaonomi", "ui-theme.txt");

    /// <summary>The themes, in the order of the Godot chat.</summary>
    public static IReadOnlyList<Palette> All { get; } =
        [Palette.Kaonomi, Palette.Matrix, Palette.Kandinsky, Palette.MarieKilleuse, Palette.Ubuntu, Palette.Ghost, Palette.MacOS, Palette.IronMan, Palette.StarCitizen, Palette.CounterStrike, Palette.Defense, Palette.Minecraft, Palette.Mars, Palette.Couture, Palette.Silicon, Palette.Robotics, Palette.KaonomiDark, Palette.KaonomiWhite];

    /// <summary>The theme in use.</summary>
    public static Palette Current { get; private set; } = Palette.Kaonomi;

    /// <summary>Raised once a theme is applied.</summary>
    public static event Action<Palette>? Changed;

    /// <summary>The theme named <paramref name="name"/> (Kaonomi for none and for the names of the themes removed).</summary>
    public static Palette Find(string? name)
    {
        var wanted = name?.Trim() ?? string.Empty;
        if (FormerNames.TryGetValue(wanted, out var now))
            wanted = now;
        return All.FirstOrDefault(p => string.Equals(Plain(p.Name), Plain(wanted), StringComparison.OrdinalIgnoreCase)) ?? Palette.Kaonomi;
    }

    /// <summary>The names the themes had before (kept in ui-theme.txt): their names now.</summary>
    private static readonly Dictionary<string, string> FormerNames = new(StringComparer.OrdinalIgnoreCase) { ["KaonomiNet"] = "Kaonomi-Dark" };

    /// <summary>A name of theme without its hyphens: kaonomidark is Kaonomi-Dark.</summary>
    private static string Plain(string name) => name.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);

    /// <summary>The theme chosen last (Kaonomi the first time).</summary>
    public static Palette Saved()
    {
        try
        {
            return File.Exists(FilePath) ? Find(File.ReadAllText(FilePath)) : Palette.Kaonomi;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Palette.Kaonomi;
        }
    }

    /// <summary>Applies <paramref name="palette"/> to the whole interface; <paramref name="save"/>: kept for the next start.</summary>
    public static void Apply(Palette palette, bool save = true)
    {
        if (Application.Current is not { } app)
            return;
        Current = palette;
        foreach (var (key, color) in new[]
                 {
                     ("Space", palette.Space), ("Panel", palette.Panel), ("Header", palette.Header), ("Field", palette.Field),
                     ("Edge", palette.Edge), ("Text", palette.Text), ("TextBright", palette.TextBright), ("Muted", palette.Muted),
                     ("Accent", palette.Accent), ("Warm", palette.Warm), ("Red", palette.Red), ("Green", palette.Green)
                 })
        {
            // The brush every view holds is recoloured: what is shown changes at once.
            if (app.TryGetResource(key, null, out var value) && value is SolidColorBrush brush)
                brush.Color = color;
            else
                app.Resources[key] = new SolidColorBrush(color);
        }
        app.Resources["PanelSolid"] = new SolidColorBrush(palette.PanelSolid);
        app.Resources["EdgeBright"] = new SolidColorBrush(palette.EdgeBright);
        // The accent of the Fluent controls (selections, check boxes, sliders, focus).
        app.Resources["SystemAccentColor"] = palette.Accent;
        app.Resources["SystemAccentColorDark1"] = Shade(palette.Accent, -0.15);
        app.Resources["SystemAccentColorDark2"] = Shade(palette.Accent, -0.3);
        app.Resources["SystemAccentColorDark3"] = Shade(palette.Accent, -0.45);
        app.Resources["SystemAccentColorLight1"] = Shade(palette.Accent, 0.15);
        app.Resources["SystemAccentColorLight2"] = Shade(palette.Accent, 0.3);
        app.Resources["SystemAccentColorLight3"] = Shade(palette.Accent, 0.45);
        // On the accent: dark text on a light accent, white on a dark one.
        app.Resources["OnAccent"] = new SolidColorBrush(Luminance(palette.Accent) > 0.5 ? Color.FromRgb(5, 8, 11) : Colors.White);
        app.Resources["BodyFont"] = new FontFamily(string.Join(", ", palette.BodyFonts));
        app.Resources["DisplayFont"] = new FontFamily(string.Join(", ", palette.DisplayFonts));
        app.Resources["DisplayWeight"] = (FontWeight)palette.DisplayWeight;
        app.RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        if (save)
            Save(palette);
        Changed?.Invoke(palette);
    }

    private static void Save(Palette palette)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, palette.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Theme", $"The theme was not saved: {ex.Message}");
        }
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// <summary><paramref name="color"/> lighter (amount above 0) or darker (below 0).</summary>
    private static Color Shade(Color color, double amount)
    {
        byte Channel(byte v) => (byte)Math.Clamp(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
        return Color.FromRgb(Channel(color.R), Channel(color.G), Channel(color.B));
    }
}
