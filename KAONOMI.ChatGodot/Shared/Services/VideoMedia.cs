using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The videos of /video through kaonomi-media (KAONOMI.Media, Swift and AVFoundation: the decoders of macOS): what a
/// video is, its thumbnail, a picture at a time, the strip of the timeline, the sheet of a segment, the sound of a
/// segment. The tool is built by the chat with swiftc the first time (and again when its source changed), in
/// ~/kaonomi/video/bin.
/// </summary>
public static class VideoMedia
{
    /// <summary>The file names of videos the library looks for.</summary>
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".qt", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".3gp", ".ts", ".mts", ".m2ts", ".ogv"
    };

    public static string Home => Path.Combine(ChatSettings.KaonomiHome, "video");

    public static string ThumbnailsDirectory => Path.Combine(Home, "thumbs");

    public static string StripsDirectory => Path.Combine(Home, "strips");

    /// <summary>Where the pictures kept from the videos are saved.</summary>
    public static string PicturesDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) is { Length: > 0 } pictures ? pictures : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures"), "kaonomi", "video");

    private static string ToolPath => Path.Combine(Home, "bin", "kaonomi-media");

    /// <summary>
    /// ffmpeg and ffprobe next to kaonomi-media (~/kaonomi/video/bin): what macOS cannot read (webm, mkv, avi, wmv,
    /// flv…) goes through them.
    /// </summary>
    public static bool HasFfmpeg => File.Exists(Path.Combine(Home, "bin", "ffmpeg")) && File.Exists(Path.Combine(Home, "bin", "ffprobe"));

    private static string SourcePath => Path.Combine(ProcessWindowViewModel.FindProjects(), "KAONOMI.Media", "kaonomi-media.swift");

    private static readonly SemaphoreSlim BuildGate = new(1, 1);

    /// <summary>
    /// The tool, built when missing or older than its source; null with the reason when it cannot be (no swiftc: the
    /// command line tools of Xcode are needed).
    /// </summary>
    public static async Task<(string? Tool, string? Error)> ToolAsync(Action<string>? report = null)
    {
        await BuildGate.WaitAsync();
        try
        {
            var source = SourcePath;
            var fresh = File.Exists(ToolPath) && (!File.Exists(source) || File.GetLastWriteTimeUtc(ToolPath) >= File.GetLastWriteTimeUtc(source));
            if (fresh)
                return (ToolPath, null);
            if (!File.Exists(source))
                return (null, $"the source of kaonomi-media is missing: {source}");
            Directory.CreateDirectory(Path.GetDirectoryName(ToolPath)!);
            report?.Invoke("Building kaonomi-media (Swift, AVFoundation)… once, about 10 s");
            var (code, _, error) = await RunAsync("/usr/bin/swiftc", ["-O", "-swift-version", "5", "-suppress-warnings", source, "-o", ToolPath], TimeSpan.FromMinutes(3));
            if (code != 0)
                return (null, "kaonomi-media could not be built (swiftc: the command line tools of Xcode): " + error.Trim());
            ActivityLog.Info("Video", "kaonomi-media built in " + ToolPath);
            return (ToolPath, null);
        }
        finally
        {
            BuildGate.Release();
        }
    }

    /// <summary>Runs a program; its exit code, what it wrote and its errors.</summary>
    public static async Task<(int Code, string Output, string Error)> RunAsync(string program, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{Path.GetFileName(program)} took more than {timeout.TotalSeconds:0} s");
        }
        return (process.ExitCode, await output, await error);
    }

    /// <summary>Runs a command of kaonomi-media; its JSON answer. Throws with its message when it fails.</summary>
    private static async Task<JsonElement> MediaAsync(TimeSpan timeout, CancellationToken cancellationToken, params string[] arguments)
    {
        var (tool, problem) = await ToolAsync();
        if (tool is null)
            throw new InvalidOperationException(problem);
        var (code, output, error) = await RunAsync(tool, arguments, timeout, cancellationToken);
        if (code != 0)
            throw new InvalidOperationException(error.Trim().Length > 0 ? error.Trim() : $"kaonomi-media {arguments[0]} failed");
        using var document = JsonDocument.Parse(output.Trim().Length > 0 ? output : "{}");
        return document.RootElement.Clone();
    }

    private static string Number(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What the video is.</summary>
    public static async Task<VideoProbe> ProbeAsync(string path, CancellationToken cancellationToken = default) =>
        (await MediaAsync(TimeSpan.FromSeconds(30), cancellationToken, "probe", path)).Deserialize<VideoProbe>() ?? new VideoProbe();

    /// <summary>What the video is, and its thumbnail written to <paramref name="thumbnail"/> (at 10 % of it).</summary>
    public static async Task<VideoProbe> CardAsync(string path, string thumbnail, int width = 360, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        return (await MediaAsync(TimeSpan.FromSeconds(40), cancellationToken, "card", path, thumbnail, width.ToString())).Deserialize<VideoProbe>() ?? new VideoProbe();
    }

    /// <summary>The picture at <paramref name="seconds"/> as a JPEG (<paramref name="width"/> 0: full size; exact: that very frame).</summary>
    public static async Task<byte[]> FrameAsync(string path, double seconds, int width, bool exact = true, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(Path.GetTempPath(), $"kaonomi-frame-{Guid.NewGuid():N}.jpg");
        try
        {
            await MediaAsync(TimeSpan.FromSeconds(40), cancellationToken, "thumb", path, Number(seconds), file, width.ToString(), exact ? "exact" : "near");
            return await File.ReadAllBytesAsync(file, cancellationToken);
        }
        finally
        {
            TryDelete(file);
        }
    }

    /// <summary>The strip of pictures of the timeline (made once per video, kept in ~/kaonomi/video/strips).</summary>
    public static async Task<string> StripAsync(string path, string key, int count = 48, int height = 72, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(StripsDirectory, $"{key}-{count}x{height}.jpg");
        if (File.Exists(file))
            return file;
        Directory.CreateDirectory(StripsDirectory);
        await MediaAsync(TimeSpan.FromMinutes(2), cancellationToken, "strip", path, count.ToString(), file, height.ToString());
        return file;
    }

    /// <summary>The pictures of a segment on one sheet, in order and numbered with their time (a JPEG).</summary>
    public static async Task<byte[]> SheetAsync(string path, double start, double end, int count, int cellWidth, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(Path.GetTempPath(), $"kaonomi-sheet-{Guid.NewGuid():N}.jpg");
        try
        {
            await MediaAsync(TimeSpan.FromMinutes(2), cancellationToken, "sheet", path, Number(start), Number(end), count.ToString(), file, cellWidth.ToString());
            return await File.ReadAllBytesAsync(file, cancellationToken);
        }
        finally
        {
            TryDelete(file);
        }
    }

    /// <summary>The sound of a segment, 16 kHz mono (what the speech recognition and the voices take).</summary>
    public static async Task<float[]> AudioAsync(string path, double start, double end, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(Path.GetTempPath(), $"kaonomi-sound-{Guid.NewGuid():N}.wav");
        try
        {
            await MediaAsync(TimeSpan.FromMinutes(3), cancellationToken, "audio", path, Number(start), Number(end), file);
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
            const int header = 44;
            var samples = new float[Math.Max(0, (bytes.Length - header) / 2)];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, header + i * 2) / 32768f;
            return samples;
        }
        finally
        {
            TryDelete(file);
        }
    }

    /// <summary>The program and its arguments that stream the video from <paramref name="start"/> (for the player).</summary>
    public static async Task<ProcessStartInfo> PlayStartAsync(string path, double start, int width)
    {
        var (tool, problem) = await ToolAsync();
        if (tool is null)
            throw new InvalidOperationException(problem);
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "play", path, Number(start), width.ToString() })
            info.ArgumentList.Add(argument);
        return info;
    }

    /// <summary>The key of a video in the search engine: its path and its size (the same file moved is another one).</summary>
    public static string KeyOf(string path, long size) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path) + "|" + size)))[..20].ToLowerInvariant();

    /// <summary>A time as 01:23.4 (or 1:02:03.4).</summary>
    public static string Clock(double seconds)
    {
        var total = Math.Max(0, seconds);
        var span = TimeSpan.FromSeconds(total);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds / 100}" : $"{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds / 100}";
    }

    /// <summary>A length as "12 s", "3 min 05", "1 h 02".</summary>
    public static string Length(double seconds) =>
        seconds < 60 ? $"{seconds:0.#} s" : seconds < 3600 ? $"{(int)seconds / 60} min {(int)seconds % 60:00}" : $"{(int)seconds / 3600} h {(int)seconds % 3600 / 60:00}";

    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB" : $"{bytes / 1024.0:0} KB";

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
