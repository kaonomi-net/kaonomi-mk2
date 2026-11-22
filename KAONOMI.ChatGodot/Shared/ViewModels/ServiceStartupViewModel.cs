using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>Where a service of the start is: not looked at yet, being checked, found running, being started, started, or not running.</summary>
public enum ServiceState
{
    Waiting,
    Checking,
    Running,
    Starting,
    Started,
    Failed
}

/// <summary>A service checked when the chat starts, with what is known of it.</summary>
public sealed partial class ServiceStep : ObservableObject
{
    public ServiceStep(ManagedProcess process) => Process = process;

    /// <summary>The process of /process it is.</summary>
    public ManagedProcess Process { get; }

    public string Name => Process.Name;

    /// <summary>The address it answers at (host:port), empty when it has no port.</summary>
    public string Address => Process.Port is { } port ? $"{port.Host}:{port.Port}" : string.Empty;

    [ObservableProperty]
    private ServiceState _state = ServiceState.Waiting;

    /// <summary>What is being done, or what was found, in a few words.</summary>
    [ObservableProperty]
    private string _detail = "waiting";
}

/// <summary>
/// The services the chat needs, checked when it starts (<see cref="Services"/>: the search engine, the Mongo engine, the
/// LLM gateway, the graph database, then the server, which the chat reads its projects, its presets and its voices
/// from), one after the other in that order: one that answers on its port is left as it is; one that does not is started as /process starts it (its output in <c>~/kaonomi/logs</c>), and the next one is
/// looked at only once it answers, or once it is known not to (it stopped just after its start, or did not answer
/// within <see cref="StartLimit"/>: the others are still checked, and started). A service whose process is found while
/// its port is closed is being started from elsewhere (a terminal): it is waited for, not started a second time.
/// A viewer starts nothing, and a service of another machine is not started from here: both are only told. The
/// server is not started without the two engines it needs (<see cref="Needs"/>), as START CORE of /process does not.
/// </summary>
public sealed partial class ServiceStartupViewModel : ViewModelBase, IDisposable
{
    /// <summary>The services checked, in the order they are started (their names in /process).</summary>
    public static readonly IReadOnlyList<string> Services = ["SearchEngine", "MongoEngine", "LLM Gateway", "graphbase", "Server"];

    /// <summary>The services a service is not started without: it would stop, or never answer.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Needs = new Dictionary<string, string[]>
    {
        ["Server"] = ["SearchEngine", "MongoEngine"]
    };

    private enum Outcome
    {
        Answers,
        Gone,
        TimedOut
    }

    private readonly ProcessWindowViewModel? _processes;
    private readonly Func<ManagedProcess, CancellationToken, Task<(bool Answers, bool Found)>> _probe;
    private readonly Func<ManagedProcess, string?> _launch;
    private readonly bool _mayStart;
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;

    /// <summary>The services of <paramref name="settings"/>; <paramref name="mayStart"/> false (a viewer) only checks them.</summary>
    public ServiceStartupViewModel(ChatSettings settings, bool mayStart)
    {
        _processes = new ProcessWindowViewModel(settings);
        _mayStart = mayStart;
        _probe = async (process, ct) =>
        {
            var (open, pids) = await ProcessWindowViewModel.ProbeAsync(process, ct);
            // The process launched from here counts while it lives (on Windows, dotnet run is not found by its name).
            return (open, pids.Count > 0 || IsAlive(process.Started));
        };
        _launch = _processes.Launch;
        foreach (var name in Services)
            if (_processes.Processes.FirstOrDefault(p => p.Name == name) is { } process)
                Steps.Add(new ServiceStep(process));
    }

    /// <summary>
    /// For the tests: <paramref name="services"/> checked with <paramref name="probe"/> (it answers on its port, its
    /// process is found) and started with <paramref name="launch"/> (null once launched, else why not).
    /// </summary>
    public ServiceStartupViewModel(IEnumerable<ManagedProcess> services,
        Func<ManagedProcess, CancellationToken, Task<(bool Answers, bool Found)>> probe, Func<ManagedProcess, string?> launch, bool mayStart = true)
    {
        _probe = probe;
        _launch = launch;
        _mayStart = mayStart;
        foreach (var process in services)
            Steps.Add(new ServiceStep(process));
    }

    /// <summary>How long a service started (from here or elsewhere) has to answer on its port.</summary>
    public TimeSpan StartLimit { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>The time between two looks at a service that starts.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A process not found before this long after its start is not said to have stopped yet.</summary>
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(3);

    public ObservableCollection<ServiceStep> Steps { get; } = [];

    /// <summary>What is being done, then what was done.</summary>
    [ObservableProperty]
    private string _status = "Checking the services of KAONOMI…";

    /// <summary>True once every service was looked at.</summary>
    [ObservableProperty]
    private bool _isDone;

    /// <summary>True when a service does not run at the end.</summary>
    public bool HasFailed => Steps.Any(s => s.State == ServiceState.Failed);

    /// <summary>True when a service was started, or waited for.</summary>
    public bool HasStarted => Steps.Any(s => s.State == ServiceState.Started);

    /// <summary>Where the output of a service started from here goes.</summary>
    public static string LogsDirectory => ProcessWindowViewModel.LogsDirectory;

    /// <summary>
    /// Checks the services one after the other, starting those that do not answer; the same task when asked again while
    /// it runs. Once done (<see cref="IsDone"/>), asking again checks them all again (RETRY).
    /// </summary>
    public Task RunAsync()
    {
        if (_run is { IsCompleted: false })
            return _run;
        return _run = RunOnceAsync(_stop.Token);
    }

    /// <summary>The run in progress, or the last one (done already when none was asked): awaited without starting another.</summary>
    public Task Completion => _run ?? Task.CompletedTask;

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IsDone = false;
        foreach (var step in Steps)
        {
            step.State = ServiceState.Waiting;
            step.Detail = "waiting";
        }
        try
        {
            foreach (var step in Steps)
                await EnsureAsync(step, ct);
        }
        catch (OperationCanceledException)
        {
            // The chat closes: what was started goes on.
            return;
        }
        var failed = Steps.Where(s => s.State == ServiceState.Failed).Select(s => s.Name).ToList();
        var started = Steps.Where(s => s.State == ServiceState.Started).Select(s => s.Name).ToList();
        Status = failed.Count > 0
            ? $"Not running: {string.Join(", ", failed)}" + (started.Count > 0 ? $" · started: {string.Join(", ", started)}" : string.Empty)
            : started.Count > 0
                ? $"Started: {string.Join(", ", started)} · the {Steps.Count} services are running"
                : $"The {Steps.Count} services are running";
        if (failed.Count > 0)
            ActivityLog.Error("Services", "Start of the chat · " + Status + " (" + string.Join("; ", Steps.Where(s => s.State == ServiceState.Failed).Select(s => $"{s.Name}: {s.Detail}")) + ")");
        else
            ActivityLog.Info("Services", "Start of the chat · " + Status);
        IsDone = true;
    }

    /// <summary>One service: left as it is when it answers, else started and waited for.</summary>
    private async Task EnsureAsync(ServiceStep step, CancellationToken ct)
    {
        var process = step.Process;
        Status = $"Checking {step.Name}…";
        Set(step, ServiceState.Checking, $"checking {step.Address}…");
        var (answers, found) = await _probe(process, ct);
        if (answers)
        {
            Set(step, ServiceState.Running, $"already running · {step.Address} answers");
            return;
        }
        var clock = Stopwatch.StartNew();
        if (found)
        {
            // Its process is there, its port is not open yet: started from elsewhere a moment ago. Not a second one.
            Status = $"{step.Name} is starting: waiting for {step.Address}…";
            Set(step, ServiceState.Starting, "starting from elsewhere…");
            switch (await WaitAsync(step, clock, "starting from elsewhere", ct))
            {
                case Outcome.Answers:
                    Set(step, ServiceState.Started, $"started from elsewhere · {step.Address} answers");
                    return;
                case Outcome.TimedOut:
                    Set(step, ServiceState.Failed, $"its process runs, but {step.Address} does not answer after {(int)StartLimit.TotalSeconds} s");
                    return;
            }
            // Its process went away without answering: it is started from here.
            clock.Restart();
        }
        if (!_mayStart)
        {
            Set(step, ServiceState.Failed, "not running (a viewer does not start the services: ask a user or an administrator)");
            return;
        }
        if (process.Port is { } port && !IsLocal(port.Host))
        {
            Set(step, ServiceState.Failed, $"{step.Address} does not answer, and it is not this machine: start it there");
            return;
        }
        if (Needs.TryGetValue(step.Name, out var needed)
            && Steps.Where(s => s.State == ServiceState.Failed && needed.Contains(s.Name)).Select(s => s.Name).ToList() is { Count: > 0 } missing)
        {
            Set(step, ServiceState.Failed, $"not started: it needs {string.Join(" and ", missing)}, which {(missing.Count == 1 ? "does" : "do")} not run");
            return;
        }
        Status = $"Starting {step.Name}…";
        Set(step, ServiceState.Starting, "starting…");
        if (_launch(process) is { } refused)
        {
            // The name is already shown beside it.
            Set(step, ServiceState.Failed, refused.StartsWith(step.Name + " ", StringComparison.Ordinal) ? refused[(step.Name.Length + 1)..] : refused);
            return;
        }
        switch (await WaitAsync(step, clock, "starting", ct))
        {
            case Outcome.Answers:
                Set(step, ServiceState.Started, $"started in {Math.Max(1, (int)Math.Round(clock.Elapsed.TotalSeconds))} s · {step.Address} answers");
                break;
            case Outcome.Gone:
                Set(step, ServiceState.Failed, $"stopped just after its start (see {Log(process)})");
                break;
            default:
                Set(step, ServiceState.Failed, $"started, but {step.Address} does not answer after {(int)StartLimit.TotalSeconds} s (see {Log(process)})");
                break;
        }
    }

    /// <summary>Looks at a service that starts until it answers, its process is gone, or <see cref="StartLimit"/> is over.</summary>
    private async Task<Outcome> WaitAsync(ServiceStep step, Stopwatch clock, string doing, CancellationToken ct)
    {
        var missing = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (answers, found) = await _probe(step.Process, ct);
            if (answers)
                return Outcome.Answers;
            // Gone: not found twice in a row (between dotnet run and the program it builds, a look can miss it).
            missing = found ? 0 : missing + 1;
            if (missing >= 2 && clock.Elapsed >= Grace)
                return Outcome.Gone;
            if (clock.Elapsed >= StartLimit)
                return Outcome.TimedOut;
            step.Detail = $"{doing}… {(int)clock.Elapsed.TotalSeconds} s";
            await Task.Delay(Poll, ct);
        }
    }

    /// <summary>The log of <paramref name="process"/> as it is told: <c>~/kaonomi/logs/…</c> when it is in the folder of the user.</summary>
    private static string Log(ManagedProcess process)
    {
        var file = ProcessWindowViewModel.LogFile(process);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 1 && file.StartsWith(home + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~" + file[home.Length..] : file;
    }

    private static void Set(ServiceStep step, ServiceState state, string detail)
    {
        step.Detail = detail;
        step.State = state;
    }

    private static bool IsAlive(Process? process)
    {
        try
        {
            return process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="host"/> is this machine (localhost, a loopback address, its name or one of its addresses).</summary>
    public static bool IsLocal(string host)
    {
        var name = host.Trim().Trim('[', ']');
        if (name.Length == 0 || name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(name, out var address))
            return false;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Any(a => a.Address.Equals(address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    /// <summary>Stops looking (the chat closes); the services started go on.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _processes?.Dispose();
    }
}
