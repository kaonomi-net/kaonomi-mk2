using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// A frame of a vision application: its picture (JPEG), what was found in it as a line, and whether that is an alert.
/// The picture is in a buffer of the pool (a frame of 1280 × 720 is over 100 KB, 25 times a second: one allocated for
/// each would be a large object each time): <see cref="Release"/> gives it back once the picture is decoded, or the
/// frame dropped.
/// </summary>
public sealed class VisionFrame(byte[] buffer, int length, string found, bool alert)
{
    private byte[]? _buffer = buffer;

    /// <summary>The JPEG (empty once released).</summary>
    public ReadOnlySpan<byte> Picture => _buffer is { } held ? held.AsSpan(0, length) : default;

    public string Found { get; } = found;

    public bool Alert { get; } = alert;

    /// <summary>The buffer of the picture goes back to the pool (once: asked again, nothing).</summary>
    public void Release()
    {
        if (Interlocked.Exchange(ref _buffer, null) is { } held)
            System.Buffers.ArrayPool<byte>.Shared.Return(held);
    }
}

/// <summary>
/// A window of the desktop showing a vision application of KAONOMI.VisionOpenCV (Python and OpenCV):
/// <c>vision_&lt;app&gt;.py --stream</c> reads the camera and sends each frame drawn (a JPEG picture and what was found,
/// as JSON) on its output, read here; it takes on its input what the window asks, one command per line. The view
/// takes the last frame when it draws (<see cref="TakeFrame"/>): a frame not shown in time is dropped, never queued.
/// The window closed, the script is stopped and the camera released.
/// </summary>
public abstract partial class VisionStreamViewModel(string app) : ObservableObject, IDisposable, IStaysOpen
{
    private readonly object _gate = new();
    private Process? _process;
    private VisionFrame? _frame;
    private bool _disposed;
    private int _run;

    /// <summary>The command of the chat: /vision:move, /vision.</summary>
    protected string Command { get; } = VisionApps.CommandOf(app);

    public static string PicturesDirectory => Path.Combine(ChatSettings.SystemPicturesDirectory, "kaonomi", "vision");

    [ObservableProperty]
    private string _status = "Starting…";

    [ObservableProperty]
    private bool _isError;

    /// <summary>The script runs (or is being started): false once it stopped, START runs it again.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(SnapshotCommand), nameof(PauseCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    private bool _isPaused;

    public string PauseLabel => IsPaused ? "▶ RESUME" : "⏸ PAUSE";

    partial void OnIsPausedChanged(bool value) => Send($"pause {(value ? 1 : 0)}");

    /// <summary>The cameras of this machine (listed when the window opens).</summary>
    public IReadOnlyList<VisionApps.Camera> Cameras { get; private set; } = [];

    /// <summary>
    /// The index of the camera the script reads: the one chosen in a vision window, kept for all of them. Changing
    /// it runs the script again on that camera.
    /// </summary>
    [ObservableProperty]
    private int _camera = VisionApps.ChosenCamera;

    partial void OnCameraChanged(int value)
    {
        VisionApps.ChosenCamera = value;
        if (IsRunning)
            _ = RestartAsync();
    }

    /// <summary>Stops the script and runs it again (another camera was chosen).</summary>
    private async Task RestartAsync()
    {
        // What the script still running says is no longer listened to.
        Interlocked.Increment(ref _run);
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        if (process is not null)
        {
            try
            {
                lock (process)
                {
                    process.StandardInput.WriteLine("quit");
                    process.StandardInput.Close();
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or ObjectDisposedException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception kill) when (kill is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }
        }
        Interlocked.Exchange(ref _frame, null);
        IsRunning = false;
        if (!_disposed)
            await Start();
    }

    partial void OnIsRunningChanged(bool value) => RunningChanged();

    /// <summary>The script started or stopped: the commands of the window that need it running follow.</summary>
    protected virtual void RunningChanged()
    {
    }

    /// <summary>The arguments of the script, after --stream and --pictures: what the window shows now.</summary>
    protected abstract IEnumerable<string> StartArguments();

    /// <summary>What the window shows now, said again to the script just started (what is not an argument).</summary>
    protected virtual void SendState()
    {
    }

    /// <summary>What is done before the script starts (e.g. bringing its data from the server); nothing by default.</summary>
    protected virtual Task BeforeStartAsync() => Task.CompletedTask;

    /// <summary>A message of the script (its JSON), after its text was shown in the status: what else it holds.</summary>
    protected virtual void OnMessage(JsonElement message)
    {
    }

    /// <summary>What was found in a frame (its JSON), as the line shown under the picture, and whether it is an alert.</summary>
    protected abstract (string Found, bool Alert) Describe(JsonElement frame);

    /// <summary>The last frame received and not shown yet; null when there is none.</summary>
    public VisionFrame? TakeFrame() => Interlocked.Exchange(ref _frame, null);

    private bool CanStart() => !IsRunning;

    /// <summary>A camera that watches is not idle: the desktop keeps its window open while the script runs.</summary>
    public bool StaysOpen => IsRunning;

    /// <summary>Starts the script (the view does it once shown; START does it again after it stopped).</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task Start()
    {
        if (IsRunning || _disposed)
            return;
        IsRunning = true;
        var run = Interlocked.Increment(ref _run);
        Report("Starting…", false);
        await BeforeStartAsync();
        if (Cameras.Count == 0)
        {
            Cameras = await VisionApps.ListCamerasAsync(Report);
            // The camera chosen is no longer there: the first one.
            if (Cameras.All(c => c.Index != Camera))
                Camera = Cameras[0].Index;
            OnPropertyChanged(nameof(Cameras));
        }
        var info = await VisionApps.PrepareAsync(app, Report, ["--stream", "--source", VisionApps.TestSource ?? Camera.ToString(), "--pictures", PicturesDirectory, .. StartArguments()]);
        if (info is null || _disposed || run != _run)
        {
            IsRunning = false;
            return;
        }
        try
        {
            var process = Process.Start(info) ?? throw new InvalidOperationException("Python could not be started");
            lock (_gate)
                _process = process;
            // What OpenCV and Python write on the errors is read, or the script would block on a full pipe.
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            Report("Opening the camera…", false);
            ActivityLog.Info("Vision", $"{Command} started");
            Send($"pause {(IsPaused ? 1 : 0)}");
            SendState();
            _ = Task.Run(() => ReadAsync(process, run));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            IsRunning = false;
            Report($"{Command} could not start: {ex.Message}", true);
            ActivityLog.Error("Vision", $"{Command} could not start", ex);
        }
    }

    /// <summary>Reads the messages of the script until it stops: a frame (kept for the view) or a message (shown in the status).</summary>
    private async Task ReadAsync(Process process, int run)
    {
        var output = process.StandardOutput.BaseStream;
        var header = new byte[4];
        var first = true;
        var failed = false;
        try
        {
            while (true)
            {
                if (!await ReadExactlyAsync(output, header))
                    break;
                var text = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
                if (!await ReadExactlyAsync(output, text) || !await ReadExactlyAsync(output, header))
                    break;
                // The picture, in a buffer of the pool (given back by the frame, or here when it is not one).
                var length = BinaryPrimitives.ReadInt32BigEndian(header);
                var picture = length > 0 ? System.Buffers.ArrayPool<byte>.Shared.Rent(length) : [];
                var kept = false;
                try
                {
                if (!await ReadExactlyAsync(output, picture.AsMemory(0, length)))
                    break;
                using var meta = JsonDocument.Parse(text);
                var root = meta.RootElement;
                // A picture asked by CaptureAsync: given to it, not shown.
                if (root.TryGetProperty("capture", out _))
                {
                    Interlocked.Exchange(ref _capture, null)?.TrySetResult(picture.AsSpan(0, length).ToArray());
                    continue;
                }
                if (root.TryGetProperty("message", out var message))
                {
                    var isError = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True;
                    failed |= isError;
                    Report(message.GetString() ?? string.Empty, isError);
                    if (isError)
                        ActivityLog.Error("Vision", $"{Command}: {message.GetString()}");
                    OnMessage(root.Clone());
                    continue;
                }
                if (length == 0)
                {
                    // Neither a text for the status nor a frame: something the script tells the window (what it
                    // records of the faces, for instance).
                    OnMessage(root.Clone());
                    continue;
                }
                var (found, alert) = Describe(root);
                // The frame before, when the view did not take it, is dropped.
                kept = true;
                Interlocked.Exchange(ref _frame, new VisionFrame(picture, length, found, alert))?.Release();
                if (first)
                {
                    first = false;
                    Report("The camera is on", false);
                }
                }
                finally
                {
                    if (!kept && length > 0)
                        System.Buffers.ArrayPool<byte>.Shared.Return(picture);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or ObjectDisposedException or InvalidOperationException or OverflowException or KeyNotFoundException or FormatException)
        {
            // The script was stopped, or said something that is not a message: it is over.
        }
        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        lock (_gate)
            if (ReferenceEquals(_process, process))
                _process = null;
        process.Dispose();
        if (_disposed || run != _run)
            return;
        IsRunning = false;
        if (!failed)
            Report("Stopped · START runs it again", false);
        ActivityLog.Info("Vision", $"{Command} stopped");
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..]);
            if (count == 0)
                return false;
            read += count;
        }
        return true;
    }

    private TaskCompletionSource<byte[]?>? _capture;

    /// <summary>
    /// The next frame of the camera as it is (a JPEG, nothing drawn on it); null when the script does not run, does
    /// not know the command, or sends nothing within <paramref name="timeout"/>.
    /// </summary>
    public async Task<byte[]?> CaptureAsync(TimeSpan timeout)
    {
        if (!IsRunning)
            return null;
        var wanted = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _capture, wanted)?.TrySetResult(null);
        Send("capture");
        var done = await Task.WhenAny(wanted.Task, Task.Delay(timeout));
        Interlocked.CompareExchange(ref _capture, null, wanted);
        return done == wanted.Task ? await wanted.Task : null;
    }

    /// <summary>A command to the script, on its input; nothing when it does not run.</summary>
    protected void Send(string command)
    {
        Process? process;
        lock (_gate)
            process = _process;
        if (process is null)
            return;
        try
        {
            lock (process)
            {
                process.StandardInput.WriteLine(command);
                process.StandardInput.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // It just stopped.
        }
    }

    /// <summary>Saves the picture shown in <see cref="PicturesDirectory"/>.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Snapshot() => Send("snapshot");

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Pause() => IsPaused = !IsPaused;

    /// <summary>Opens the folder of the pictures saved in the file explorer of the system.</summary>
    [RelayCommand]
    private void OpenFolder() => OpenInExplorer(PicturesDirectory);

    protected void OpenInExplorer(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Report($"The folder could not be opened: {ex.Message}", true);
        }
    }

    /// <summary>Says <paramref name="status"/> in the status line of the window.</summary>
    public void Say(string status, bool isError = false) => Report(status, isError);

    protected void Report(string status, bool isError)
    {
        Status = status;
        IsError = isError;
    }

    /// <summary>The window closes: the script is stopped, the camera released.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
        Send("quit");
        Process? process;
        lock (_gate)
            process = _process;
        if (process is null)
            return;
        // "quit" (or its input closed) ends it at the next frame; killed when it does not.
        _ = Task.Run(async () =>
        {
            try
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or ObjectDisposedException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception kill) when (kill is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }
        });
    }
}
