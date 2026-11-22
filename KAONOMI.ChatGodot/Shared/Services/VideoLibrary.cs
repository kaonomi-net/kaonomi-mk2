using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The videos of the home folder for /video: every file with the name of a video, the folders of the system, of the
/// applications and of the code left out (Library, hidden folders, node_modules, bin/obj, the virtual environments,
/// the libraries of Photos, the profiles of the browsers). What was found is kept in ~/kaonomi/video/library.json
/// (with what each video is and its thumbnail), so that the list comes at once the next time.
/// </summary>
public static class VideoLibrary
{
    private static string CachePath => Path.Combine(VideoMedia.Home, "library.json");

    /// <summary>A video smaller than this is left out (the little clips of the applications).</summary>
    public const long MinSize = 150 * 1024;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Library", "Applications", "node_modules", "bin", "obj", "venv", ".venv", "site-packages", "__pycache__", "build", "dist", "target",
        "Pods", "DerivedData", "Extensions", "Cache", "Caches", "Code Cache", "GPUCache", "youtube-player", "thumbs", "strips"
    };

    private static readonly string[] SkippedEndings = [".photoslibrary", ".app", ".bundle", ".framework", ".xcodeproj", ".xcassets", ".fcpbundle", ".imovielibrary", ".tvlibrary"];

    private static bool Skipped(string folder)
    {
        var name = Path.GetFileName(folder);
        return name.StartsWith('.') || SkippedFolders.Contains(name) || SkippedEndings.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The videos found the last time.</summary>
    public static List<VideoEntry> Cached()
    {
        try
        {
            return File.Exists(CachePath) ? JsonSerializer.Deserialize<List<VideoEntry>>(File.ReadAllText(CachePath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Keeps what is known of the videos for the next time.</summary>
    public static void Save(IEnumerable<VideoEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(VideoMedia.Home);
            var temporary = CachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries.ToList()));
            File.Move(temporary, CachePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Video", "The library could not be kept", ex);
        }
    }

    /// <summary>
    /// Every video of the home folder (a walk of its folders, the ones above left out): its path, its size, its date.
    /// <paramref name="progress"/> is told the folders walked now and then.
    /// </summary>
    public static List<VideoEntry> Scan(Action<int, int>? progress = null, CancellationToken cancellationToken = default)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var found = new List<VideoEntry>();
        var folders = new Stack<string>();
        folders.Push(home);
        var walked = 0;
        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = folders.Pop();
            walked++;
            if (walked % 200 == 0)
                progress?.Invoke(walked, found.Count);
            try
            {
                foreach (var child in Directory.EnumerateDirectories(folder))
                {
                    try
                    {
                        // A link to a folder is not walked: it may lead out of the home, or in a circle.
                        if (!Skipped(child) && !new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
                            folders.Push(child);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!VideoMedia.Extensions.Contains(Path.GetExtension(file)) || Path.GetFileName(file).StartsWith('.'))
                        continue;
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length >= MinSize)
                            found.Add(new VideoEntry { Path = info.FullName, Size = info.Length, Modified = info.LastWriteTimeUtc });
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A folder the system keeps closed (Desktop, Documents… until allowed): left out.
            }
        }
        progress?.Invoke(walked, found.Count);
        return found;
    }
}
