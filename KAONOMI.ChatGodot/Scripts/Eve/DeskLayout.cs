using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The windows kept from one run to the next, in a text file of their own (<c>~/kaonomi/chatgodot-windows.txt</c>),
/// written when the chat closes and read when it starts:
/// <list type="bullet">
/// <item>the window of the chat itself: its screen, position, size and state (normal, maximised, full screen), so that
/// the desktop has the same size again;</item>
/// <item>every tool window open, in front or at the back, with its place and size in the desktop, in the order they were
/// opened.</item>
/// </list>
/// Only the windows of the tools (opened by a command such as <c>/journal</c>) come back: a window of one object (an
/// image analysis, an entity, a note…) belongs to the moment. The file can be read and changed by hand:
/// <code>
/// main screen=0 x=120 y=80 width=2400 height=1300 mode=windowed
/// window /journal x=310 y=60 width=920 height=780 front
/// window /search x=4 y=4 width=700 height=500 back
/// window /work x=40 y=40 width=1000 height=700 front maximized
/// </code>
/// </summary>
public static class DeskLayout
{
    /// <summary>A tool window kept: its command, its place and size in the desktop, whether it was in front.</summary>
    public sealed record Entry(string Command, float X, float Y, float Width, float Height, bool Front, bool Maximized = false);

    /// <summary>The window of the chat: its screen, position and size (pixels of the screen), its state.</summary>
    public sealed record MainWindow(int Screen, int X, int Y, int Width, int Height, DisplayServer.WindowMode Mode);

    /// <summary>The file of the windows.</summary>
    public static string FilePath => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "kaonomi", "chatgodot-windows.txt");

    /// <summary>The file of the first versions (JSON in the data folder of Godot), read once when there is no text file yet.</summary>
    private static string OldFilePath => ProjectSettings.GlobalizePath("user://desktop.json");

    /// <summary>Writes the window of the chat and the tool windows of <paramref name="deck"/>.</summary>
    /// <summary>True with --no-restore: the desk of the last close is neither opened nor written (a chat started for a test or a screenshot).</summary>
    public static bool Disabled => OS.GetCmdlineUserArgs().Contains("--no-restore");

    public static void Save(Window window, WindowDeck deck)
    {
        if (Disabled)
            return;
        var text = new StringBuilder();
        text.AppendLine("# KAONOMI ChatGodot: the windows open when the chat closed, opened again at the next start.");
        text.AppendLine("# main: the window of the chat (screen, position and size in pixels of the screen, windowed / maximized / fullscreen).");
        text.AppendLine("# window: a tool (its command), its place and size in the desktop, front (in use) or back (a card in 3D).");
        var mode = DisplayServer.WindowGetMode();
        var position = DisplayServer.WindowGetPosition();
        var size = DisplayServer.WindowGetSize();
        text.AppendLine(Invariant($"main screen={window.CurrentScreen} x={position.X} y={position.Y} width={size.X} height={size.Y} mode={ModeName(mode)}"));
        foreach (var w in deck.Windows.Where(w => w.Key.StartsWith('/')))
        {
            // A window in full size keeps the place it will go back to with ❐.
            var rect = w.Restore ?? deck.RectOf(w);
            text.AppendLine(Invariant($"window {w.Key} x={Math.Round(rect.Position.X)} y={Math.Round(rect.Position.Y)} width={Math.Round(rect.Size.X)} height={Math.Round(rect.Size.Y)} {(w.IsFront ? "front" : "back")}{(w.IsMaximized ? " maximized" : "")}"));
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, text.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning("The windows were not saved: " + ex.Message);
        }
    }

    /// <summary>The window of the chat and the tool windows saved at the last close (none when there is no file).</summary>
    public static (MainWindow? Main, IReadOnlyList<Entry> Windows) Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Parse(File.ReadAllLines(FilePath));
            if (File.Exists(OldFilePath))
                return (null, JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(OldFilePath)) ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            GD.PushWarning("The windows saved could not be read: " + ex.Message);
        }
        return (null, []);
    }

    /// <summary>Reads the lines of the file (a line that cannot be read is skipped).</summary>
    private static (MainWindow?, IReadOnlyList<Entry>) Parse(IEnumerable<string> lines)
    {
        MainWindow? main = null;
        var windows = new List<Entry>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = words.Skip(1).Select(w => w.Split('=', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].ToLowerInvariant(), p => p[1]);
            float Number(string key) => values.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : float.NaN;
            if (words[0] == "main")
            {
                var (x, y, width, height) = (Number("x"), Number("y"), Number("width"), Number("height"));
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(width) || float.IsNaN(height))
                    continue;
                var screen = float.IsNaN(Number("screen")) ? 0 : (int)Number("screen");
                main = new MainWindow(screen, (int)x, (int)y, (int)width, (int)height, ParseMode(values.GetValueOrDefault("mode")));
            }
            else if (words[0] == "window" && words.Length > 1 && words[1].StartsWith('/'))
            {
                var (x, y, width, height) = (Number("x"), Number("y"), Number("width"), Number("height"));
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(width) || float.IsNaN(height))
                    continue;
                windows.Add(new Entry(words[1], x, y, width, height, !words.Contains("back"), words.Contains("maximized")));
            }
        }
        return (main, windows);
    }

    /// <summary>
    /// The largest screen connected: the one whose surface is the largest in real size (its pixels divided by its
    /// density: a wide monitor is larger than the denser screen of a laptop, which may hold more pixels); by the
    /// number of pixels when the densities are not known.
    /// </summary>
    public static int LargestScreen() => ScreenBySize(largest: true);

    /// <summary>The smallest screen (by its physical size, as <see cref="LargestScreen"/> measures it): the one the chat starts on.</summary>
    public static int SmallestScreen() => ScreenBySize(largest: false);

    /// <summary>The largest (or smallest) screen, by its physical area: its pixels over its density when every screen gives one.</summary>
    private static int ScreenBySize(bool largest)
    {
        var count = DisplayServer.GetScreenCount();
        var best = DisplayServer.WindowGetCurrentScreen();
        double bestArea = largest ? -1 : double.MaxValue;
        // The densities are trusted when every screen gives one.
        var known = true;
        for (var screen = 0; screen < count; screen++)
            known &= DisplayServer.ScreenGetDpi(screen) > 0;
        for (var screen = 0; screen < count; screen++)
        {
            Vector2 size = DisplayServer.ScreenGetSize(screen);
            var dpi = known ? DisplayServer.ScreenGetDpi(screen) : 1;
            var area = (double)size.X / dpi * (size.Y / dpi);
            if (largest ? area > bestArea : area < bestArea)
                (best, bestArea) = (screen, area);
        }
        return Math.Clamp(best, 0, Math.Max(0, count - 1));
    }

    /// <summary>
    /// The screen the chat starts on, in full screen: the smallest one (--largest-screen: the largest one, as before).
    /// </summary>
    public static int StartScreen() => OS.GetCmdlineUserArgs().Contains("--largest-screen") ? LargestScreen() : SmallestScreen();

    /// <summary>Puts the window of the chat on its start screen (<see cref="StartScreen"/>), in full screen.</summary>
    public static void FullScreenOnStartScreen()
    {
        var screen = StartScreen();
        if (DisplayServer.WindowGetCurrentScreen() != screen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetCurrentScreen(screen);
        }
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        KAONOMI.Chat.Services.ActivityLog.Info("App", $"Full screen on the {(screen == LargestScreen() && DisplayServer.GetScreenCount() > 1 ? "largest" : "smallest")} screen ({screen + 1} of {DisplayServer.GetScreenCount()}: {DisplayServer.ScreenGetSize(screen).X}×{DisplayServer.ScreenGetSize(screen).Y}, {DisplayServer.ScreenGetDpi(screen)} dpi)");
    }

    /// <summary>
    /// Puts the window of the chat back at its screen, position, size and state, when that screen is still there and
    /// the place is on it.
    /// </summary>
    public static void RestoreMainWindow(Window window)
    {
        if (Load().Main is not { } main)
            return;
        if (main.Screen < 0 || main.Screen >= DisplayServer.GetScreenCount())
            return;
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(main.Screen), DisplayServer.ScreenGetSize(main.Screen));
        var rect = new Rect2I(main.X, main.Y, main.Width, main.Height);
        if (!screen.Intersects(rect) || main.Width < 200 || main.Height < 200)
            return;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetCurrentScreen(main.Screen);
        DisplayServer.WindowSetSize(new Vector2I(main.Width, main.Height));
        DisplayServer.WindowSetPosition(new Vector2I(main.X, main.Y));
        if (main.Mode is DisplayServer.WindowMode.Maximized or DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen)
            DisplayServer.WindowSetMode(main.Mode);
    }

    /// <summary>
    /// Opens again the tool windows saved, in their order: <paramref name="open"/> runs the command of each one; the
    /// window is then put back at its place and, when it was at the back, sent there.
    /// </summary>
    public static void Restore(WindowDeck deck, Action<string> open)
    {
        if (Disabled)
            return;
        foreach (var entry in Load().Windows)
        {
            if (deck.Find(entry.Command) is null)
                open(entry.Command);
            if (deck.Find(entry.Command) is not { } window)
                continue;
            deck.MoveTo(window, new Rect2(entry.X, entry.Y, entry.Width, entry.Height));
            if (entry.Maximized)
                deck.Maximize(window);
            if (!entry.Front)
                deck.SendToBack(window);
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string ModeName(DisplayServer.WindowMode mode) => mode switch
    {
        DisplayServer.WindowMode.Maximized => "maximized",
        DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen => "fullscreen",
        _ => "windowed"
    };

    private static DisplayServer.WindowMode ParseMode(string? name) => name?.ToLowerInvariant() switch
    {
        "maximized" => DisplayServer.WindowMode.Maximized,
        "fullscreen" => DisplayServer.WindowMode.Fullscreen,
        _ => DisplayServer.WindowMode.Windowed
    };
}
