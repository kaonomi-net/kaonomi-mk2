using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The vision applications of KAONOMI.VisionOpenCV (Python and OpenCV), run by the windows of the chat:
/// <c>/vision:move</c> runs <c>vision_move.py --stream</c>, the motion detection; <c>/vision</c> runs
/// <c>vision_face.py --stream --objects</c>, the face and facial expression recognition and the object detection
/// with YOLOv8 (the detector of <c>vision_yolo.py</c>). They run in a venv of their own,
/// <c>~/kaonomi/vision/venv</c>, built the first time with the Python of the machine and the requirements.txt of the
/// project (opencv-python, numpy), and built again when that file changes.
/// </summary>
public static class VisionApps
{
    /// <summary>One install at a time: two windows opened together wait for the same venv.</summary>
    private static readonly System.Threading.SemaphoreSlim Installing = new(1, 1);

    /// <summary>The folder of the Python applications, beside the other projects.</summary>
    public static string ProjectDirectory => Path.Combine(ProcessWindowViewModel.FindProjects(), "KAONOMI.VisionOpenCV");

    public static string VenvDirectory => Path.Combine(ChatSettings.KaonomiHome, "vision", "venv");

    /// <summary>
    /// A video the vision windows read instead of the camera (the option --vision-source of the chat): a test without
    /// the camera. What /vision sees in it is neither recorded nor sent to the search engine.
    /// </summary>
    public static string? TestSource { get; set; }

    private static bool Windows => OperatingSystem.IsWindows();

    private static string VenvPython => Path.Combine(VenvDirectory, Windows ? "Scripts" : "bin", Windows ? "python.exe" : "python");

    /// <summary>
    /// The command of the chat whose window runs <c>vision_&lt;name&gt;.py</c>: <c>/vision</c> for <c>vision_face.py</c> (the
    /// faces and the objects), <c>/vision:move</c> for <c>vision_move.py</c>.
    /// </summary>
    public static string CommandOf(string name) => name == "face" ? "/vision" : $"/vision:{name}";

    /// <summary>What was installed in the venv for the application <paramref name="name"/>: the requirements it asked then.</summary>
    private static string InstalledMarker(string name) => Path.Combine(VenvDirectory, $"requirements.{name}.installed.txt");

    /// <summary>
    /// What starts the application <paramref name="name"/> (<c>vision_&lt;name&gt;.py</c>) with the Python of the venv,
    /// its input, output and errors redirected; the venv is built first when it is missing. Null when it cannot be
    /// started: <paramref name="report"/> says why (text, is it an error), and what is being installed meanwhile.
    /// </summary>
    public static async Task<ProcessStartInfo?> PrepareAsync(string name, Action<string, bool> report, params string[] arguments)
    {
        var command = CommandOf(name);
        try
        {
            var script = Path.Combine(ProjectDirectory, $"vision_{name}.py");
            if (!File.Exists(script))
            {
                report($"{script} was not found", true);
                return null;
            }
            await Installing.WaitAsync();
            try
            {
                if (!await EnsureVenvAsync(command, name, report))
                    return null;
            }
            finally
            {
                Installing.Release();
            }
            var info = new ProcessStartInfo(VenvPython)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ProjectDirectory
            };
            info.ArgumentList.Add(script);
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
            return info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            report($"{command} could not be prepared: {ex.Message}", true);
            ActivityLog.Error("Vision", $"{command} could not be prepared", ex);
            return null;
        }
    }

    /// <summary>A camera of this machine: the index OpenCV opens it with, and its name.</summary>
    public sealed record Camera(int Index, string Name);

    /// <summary>The camera chosen in the vision windows, kept from one opening to the next (the same for all of them).</summary>
    private static string CameraFile => Path.Combine(ChatSettings.KaonomiHome, "vision", "camera.txt");

    /// <summary>The index of the camera chosen (0, the first one, when none was).</summary>
    public static int ChosenCamera
    {
        get
        {
            try
            {
                return File.Exists(CameraFile) && int.TryParse(File.ReadAllText(CameraFile).Trim(), out var index) && index >= 0 ? index : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CameraFile)!);
                File.WriteAllText(CameraFile, value.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not kept: the first camera at the next opening.
            }
        }
    }

    /// <summary>
    /// True when this machine has a camera (a video given with --vision-source counts as one): on macOS asked to the
    /// system (system_profiler, no camera opened), elsewhere through <c>vision_cameras.py</c>. False when none is found
    /// or they cannot be listed.
    /// </summary>
    public static async Task<bool> HasCameraAsync()
    {
        if (!string.IsNullOrEmpty(TestSource))
            return true;
        try
        {
            string output;
            if (OperatingSystem.IsMacOS())
            {
                var info = new ProcessStartInfo("system_profiler", "SPCameraDataType -json")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                };
                using var process = Process.Start(info) ?? throw new InvalidOperationException("system_profiler could not be started");
                var read = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(timeout.Token);
                using var listed = System.Text.Json.JsonDocument.Parse((await read).Trim() is { Length: > 0 } text ? text : "{}");
                return listed.RootElement.TryGetProperty("SPCameraDataType", out var cameras)
                       && cameras.ValueKind == System.Text.Json.JsonValueKind.Array && cameras.GetArrayLength() > 0;
            }
            if (await PrepareAsync("cameras", (_, _) => { }) is not { } script)
                return false;
            using (var process = Process.Start(script) ?? throw new InvalidOperationException("Python could not be started"))
            {
                process.StandardInput.Close();
                var read = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                output = (await read).Trim();
            }
            using var found = System.Text.Json.JsonDocument.Parse(output.Length > 0 ? output : "[]");
            return found.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && found.RootElement.GetArrayLength() > 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            ActivityLog.Error("Vision", "The cameras could not be listed at the start", ex);
            return false;
        }
    }

    /// <summary>
    /// The cameras of this machine (<c>vision_cameras.py</c>: their names from the system on macOS, without opening
    /// them); the first one alone, unnamed, when they cannot be listed.
    /// </summary>
    public static async Task<IReadOnlyList<Camera>> ListCamerasAsync(Action<string, bool> report)
    {
        var cameras = new List<Camera>();
        try
        {
            if (await PrepareAsync("cameras", report) is { } info)
            {
                using var process = Process.Start(info) ?? throw new InvalidOperationException("Python could not be started");
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                using var listed = System.Text.Json.JsonDocument.Parse((await output).Trim() is { Length: > 0 } text ? text : "[]");
                foreach (var camera in listed.RootElement.EnumerateArray())
                    cameras.Add(new Camera(camera.GetProperty("index").GetInt32(), camera.GetProperty("name").GetString() ?? "Camera"));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or KeyNotFoundException)
        {
            ActivityLog.Error("Vision", "The cameras could not be listed", ex);
        }
        if (cameras.Count == 0)
            cameras.Add(new Camera(0, "Camera 1"));
        return cameras;
    }

    /// <summary>
    /// The venv with what the application asks: requirements.txt (every application) and requirements-&lt;name&gt;.txt
    /// (its own, e.g. Ultralytics for yolo). Built, or completed when one of these files changed.
    /// </summary>
    private static async Task<bool> EnsureVenvAsync(string command, string name, Action<string, bool> report)
    {
        var files = new[] { Path.Combine(ProjectDirectory, "requirements.txt"), Path.Combine(ProjectDirectory, $"requirements-{name}.txt") }.Where(File.Exists).ToArray();
        var wanted = string.Join("\n", await Task.WhenAll(files.Select(f => File.ReadAllTextAsync(f))));
        var marker = InstalledMarker(name);
        if (File.Exists(VenvPython) && File.Exists(marker) && await File.ReadAllTextAsync(marker) == wanted)
            return true;

        if (!File.Exists(VenvPython))
        {
            var python = await FindPythonAsync();
            if (python is null)
            {
                report($"{command}: Python 3.9 or newer was not found (python.org, or brew install python; see /setup)", true);
                return false;
            }
            report($"{command}: building the Python venv of the vision applications ({VenvDirectory}), once…", false);
            Directory.CreateDirectory(Path.GetDirectoryName(VenvDirectory)!);
            var (venvCode, venvOutput) = await RunAsync(python, ["-m", "venv", VenvDirectory]);
            if (venvCode != 0 || !File.Exists(VenvPython))
            {
                report($"{command}: python -m venv failed ({venvCode}): {LastLine(venvOutput)}", true);
                return false;
            }
        }
        report($"{command}: installing what it needs in the venv (pip install: {string.Join(", ", files.Select(Path.GetFileName))}), once, a few minutes…", false);
        var (code, output) = files.Length > 0
            ? await RunAsync(VenvPython, ["-m", "pip", "install", "--disable-pip-version-check", .. files.SelectMany(f => new[] { "-r", f })])
            : await RunAsync(VenvPython, ["-m", "pip", "install", "--disable-pip-version-check", "opencv-python", "numpy"]);
        if (code != 0)
        {
            report($"{command}: pip install failed ({code}): {LastLine(output)}", true);
            ActivityLog.Error("Vision", $"pip install failed ({code}): {output}");
            return false;
        }
        await File.WriteAllTextAsync(marker, wanted);
        return true;
    }

    private static async Task<string?> FindPythonAsync()
    {
        string[] candidates = Windows
            ? ["python", "python3", "py"]
            : ["/Library/Frameworks/Python.framework/Versions/Current/bin/python3", "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "python3", "python"];
        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate) && !File.Exists(candidate))
                continue;
            try
            {
                var (code, output) = await RunAsync(candidate, ["--version"]);
                var text = output.Trim();
                if (code == 0 && text.StartsWith("Python ", StringComparison.Ordinal) && Version.TryParse(text[7..].Split(' ')[0], out var version) && version >= new Version(3, 9))
                    return candidate;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Not on this machine.
            }
        }
        return null;
    }

    private static async Task<(int Code, string Output)> RunAsync(string file, string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        // The tools of the machine even when the chat was started from the Finder with a short PATH.
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        info.Environment["PATH"] = string.Join(Path.PathSeparator, new[] { path, "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin" }.Where(p => p.Length > 0));
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} could not be started");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await errors);
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no message";
}
