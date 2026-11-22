using KAONOMI.graphbase.Configuration;

namespace KAONOMI.graphbase;

/// <summary>The log of the server, on the console and in <c>path.logs/graphbase.log</c> when it is set.</summary>
public sealed class GraphLog
{
    private readonly string? _file;
    private readonly object _lock = new();

    private GraphLog(string? file) => _file = file;

    public static GraphLog For(GraphSettings settings)
    {
        string? file = null;
        if (settings.LogsPath is { } folder)
        {
            Directory.CreateDirectory(folder);
            file = Path.Combine(folder, "graphbase.log");
        }
        return new GraphLog(file);
    }

    public void Info(string component, string message) => Write("INFO ", component, message);
    public void Warn(string component, string message) => Write("WARN ", component, message);
    public void Error(string component, string message) => Write("ERROR", component, message);

    private void Write(string level, string component, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss,fff}][{level}][{component}] {message}";
        lock (_lock)
        {
            (level == "ERROR" ? Console.Error : Console.Out).WriteLine(line);
            if (_file is not null)
                File.AppendAllText(_file, line + Environment.NewLine);
        }
    }
}
