using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A video of the library of /video: what it is, its thumbnail, its notes in the search engine.</summary>
public sealed partial class VideoItemViewModel(VideoEntry entry) : ObservableObject
{
    private static readonly string HomeFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public VideoEntry Entry { get; } = entry;

    public string Path => Entry.Path;

    public string Name { get; } = System.IO.Path.GetFileNameWithoutExtension(entry.Path);

    public string Extension { get; } = System.IO.Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant();

    /// <summary>The folder, from the home folder (~/Movies/Holidays).</summary>
    public string Folder { get; } = Relative(System.IO.Path.GetDirectoryName(entry.Path) ?? string.Empty);

    public string Key { get; } = VideoMedia.KeyOf(entry.Path, entry.Size);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationLabel), nameof(Info))]
    private VideoProbe? _probe = entry.Probe;

    [ObservableProperty]
    private string _thumbnail = entry.Thumbnail;

    /// <summary>The notes of the video in the search engine (the notes as they are now, the deleted ones aside).</summary>
    [ObservableProperty]
    private int _notes;

    /// <summary>The project of the video (the one of its newest note), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    private string? _projectId;

    public string ProjectName => string.IsNullOrWhiteSpace(ProjectId) ? string.Empty : ProjectCatalog.NameOf(ProjectId);

    public bool Playable => Probe is not { } probe || probe.Playable;

    public string DurationLabel => Probe is { } probe ? probe.Playable ? Clockish(probe.Duration) : "not readable" : "…";

    public string Info => $"{VideoMedia.Size(Entry.Size)} · {Entry.Modified.ToLocalTime():dd MMM yyyy}"
        + (Probe is { Width: > 0 } probe ? $" · {probe.Width}×{probe.Height}" : string.Empty);

    private static string Clockish(double seconds) =>
        seconds >= 3600 ? $"{(int)seconds / 3600}:{(int)seconds % 3600 / 60:00}:{(int)seconds % 60:00}" : $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    private static string Relative(string folder) =>
        folder.StartsWith(HomeFolder, StringComparison.Ordinal) ? "~" + folder[HomeFolder.Length..] : folder;
}

/// <summary>
/// /video: the videos of the home folder, as cards (thumbnail, length, name, folder, size, date, notes). The list of
/// the last time comes at once; the home folder is walked again in the background (SCAN), then each new video is
/// looked at (its length, its size, whether macOS reads it, its thumbnail). A double click opens the video in a window
/// of its own (the player: the selection, the analyses, the annotations). Filtered by name or folder, sorted by date,
/// name, size, length or notes.
/// </summary>
public sealed partial class VideoLibraryWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>The cards shown at most (the filter finds the others).</summary>
    public const int MaxShown = 600;

    private readonly ChatSettings? _settings;
    private readonly KaonomiAssistantService? _assistant;
    private readonly OllamaModelService? _ollama;
    private readonly CancellationTokenSource _closing = new();
    private List<VideoItemViewModel> _all = [];

    public VideoLibraryWindowViewModel() : this(null, null, null)
    {
    }

    public VideoLibraryWindowViewModel(ChatSettings? settings, KaonomiAssistantService? assistant, OllamaModelService? ollama)
    {
        _settings = settings;
        _assistant = assistant;
        _ollama = ollama;
        _all = VideoLibrary.Cached().Select(e => new VideoItemViewModel(e)).ToList();
        Refresh();
        if (settings is not null)
            _ = ScanAsync();
    }

    /// <summary>The cards shown: the filter and the order applied, <see cref="MaxShown"/> at most.</summary>
    public ObservableCollection<VideoItemViewModel> Shown { get; } = [];

    /// <summary>Raised when the list shown changed (the view lays the cards again).</summary>
    public event Action? ShownChanged;

    /// <summary>A video to open in its window.</summary>
    public event Action<VideoPlayerWindowViewModel>? OpenRequested;

    public static IReadOnlyList<string> Orders { get; } = ["Most recent", "Name", "Largest", "Longest", "With notes"];

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _onlyWithNotes;

    /// <summary>The project shown: null every video, "" the videos of no project, else the id of a project.</summary>
    [ObservableProperty]
    private string? _projectFilter;

    partial void OnProjectFilterChanged(string? value) => Refresh();

    [ObservableProperty]
    private string _countLabel = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool _isBusy;

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnOrderChanged(int value) => Refresh();

    partial void OnOnlyWithNotesChanged(bool value) => Refresh();

    private void Report(string status, bool isError = false)
    {
        Status = status;
        IsError = isError;
    }

    /// <summary>Applies the filter and the order.</summary>
    private void Refresh()
    {
        var words = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<VideoItemViewModel> items = _all.Where(i => words.All(w => i.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
            || i.Folder.Contains(w, StringComparison.OrdinalIgnoreCase) || i.Extension.Equals(w, StringComparison.OrdinalIgnoreCase)));
        if (OnlyWithNotes)
            items = items.Where(i => i.Notes > 0);
        if (ProjectFilter is { } project)
            items = items.Where(i => project.Length == 0 ? string.IsNullOrWhiteSpace(i.ProjectId) : i.ProjectId == project);
        items = Order switch
        {
            1 => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => items.OrderByDescending(i => i.Entry.Size),
            3 => items.OrderByDescending(i => i.Probe?.Duration ?? 0),
            4 => items.OrderByDescending(i => i.Notes).ThenByDescending(i => i.Entry.Modified),
            _ => items.OrderByDescending(i => i.Entry.Modified)
        };
        var list = items.ToList();
        Shown.Clear();
        foreach (var item in list.Take(MaxShown))
            Shown.Add(item);
        var hours = _all.Sum(i => i.Probe?.Duration ?? 0) / 3600;
        CountLabel = list.Count == _all.Count
            ? $"{_all.Count} video(s) · {hours:0.#} h"
            : $"{list.Count} of {_all.Count} video(s)" + (list.Count > MaxShown ? $" · the first {MaxShown} shown" : string.Empty);
        ShownChanged?.Invoke();
    }

    /// <summary>Walks the home folder again, then looks at the new videos (length, thumbnail) and counts the notes.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        var token = _closing.Token;
        try
        {
            Report("Looking for the videos of the home folder…");
            var found = await Task.Run(() => VideoLibrary.Scan((folders, videos) => Report($"Looking for the videos of the home folder… {folders} folders, {videos} video(s)"), token), token);
            var known = _all.ToDictionary(i => i.Path, StringComparer.Ordinal);
            var items = new List<VideoItemViewModel>();
            foreach (var entry in found)
            {
                // The same file (its size and its date): what is known of it is kept.
                if (known.TryGetValue(entry.Path, out var item) && item.Entry.Size == entry.Size && item.Entry.Modified == entry.Modified)
                    items.Add(item);
                else
                    items.Add(new VideoItemViewModel(entry));
            }
            _all = items;
            Refresh();
            VideoLibrary.Save(_all.Select(i => i.Entry));
            await CountNotesAsync();
            await LookAtNewAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Report("The videos could not be listed: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanScan() => !IsBusy;

    /// <summary>Each video not looked at yet: its length, its size, whether macOS reads it, its thumbnail (3 at once).</summary>
    private async Task LookAtNewAsync(CancellationToken token)
    {
        // Not looked at yet, or not readable the last time while ffmpeg is there now (webm, mkv…).
        var waiting = _all.Where(i => i.Probe is null || !i.Probe.Playable && VideoMedia.HasFfmpeg && i.Probe.Backend != "ffmpeg").ToList();
        if (waiting.Count == 0)
        {
            Report($"{_all.Count} video(s) · double-click a video to watch it, select a part of it and analyse it");
            return;
        }
        var (tool, problem) = await VideoMedia.ToolAsync(s => Report(s));
        if (tool is null)
        {
            Report(problem ?? "kaonomi-media is missing", true);
            return;
        }
        var done = 0;
        using var gate = new SemaphoreSlim(3);
        var tasks = waiting.Select(async item =>
        {
            await gate.WaitAsync(token);
            try
            {
                var thumbnail = System.IO.Path.Combine(VideoMedia.ThumbnailsDirectory, item.Key + ".jpg");
                var probe = await VideoMedia.CardAsync(item.Path, thumbnail, 360, token);
                item.Entry.Probe = probe;
                item.Entry.Thumbnail = File.Exists(thumbnail) ? thumbnail : string.Empty;
                item.Probe = probe;
                item.Thumbnail = item.Entry.Thumbnail;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
            {
                // Not readable (neither by macOS nor by ffmpeg): said on its card.
                item.Entry.Probe = new VideoProbe { Playable = false, Backend = VideoMedia.HasFfmpeg ? "ffmpeg" : string.Empty };
                item.Probe = item.Entry.Probe;
            }
            finally
            {
                gate.Release();
                var count = Interlocked.Increment(ref done);
                Report($"Looking at the videos… {count} of {waiting.Count}");
                if (count % 25 == 0)
                    VideoLibrary.Save(_all.Select(i => i.Entry));
            }
        });
        await Task.WhenAll(tasks);
        VideoLibrary.Save(_all.Select(i => i.Entry));
        Refresh();
        var unreadable = _all.Count(i => !i.Playable);
        Report($"{_all.Count} video(s)" + (unreadable > 0 ? VideoMedia.HasFfmpeg ? $" · {unreadable} not readable" : $" · {unreadable} not readable by macOS (mkv, webm, avi…: ffmpeg in ~/kaonomi/video/bin reads them)" : string.Empty)
            + " · double-click a video to watch it");
    }

    /// <summary>The notes of each video in the search engine (the last version of each note, the deleted ones aside).</summary>
    private async Task CountNotesAsync()
    {
        if (_assistant is null)
            return;
        try
        {
            var versions = await _assistant.LastVideoNotesAsync(10000);
            var current = versions.GroupBy(v => v.NoteId).Select(g => g.OrderByDescending(v => v.Version).First()).Where(v => !v.IsDeleted)
                .GroupBy(v => v.VideoKey).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var item in _all)
            {
                var notes = current.TryGetValue(item.Key, out var found) ? found : [];
                item.Notes = notes.Count;
                item.ProjectId = notes.OrderByDescending(n => n.Date).FirstOrDefault()?.ProjectId;
            }
            if (Order == 4 || OnlyWithNotes || ProjectFilter is not null)
                Refresh();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            ActivityLog.Error("Video", "The notes of the videos could not be counted", ex);
        }
    }

    /// <summary>Opens a video in its window (a video macOS cannot read is said so).</summary>
    public void Open(VideoItemViewModel item)
    {
        if (!item.Playable)
        {
            Report(VideoMedia.HasFfmpeg
                ? $"{item.Name}.{item.Extension.ToLowerInvariant()} cannot be read (neither by macOS nor by ffmpeg)"
                : $"{item.Name}.{item.Extension.ToLowerInvariant()} is not readable by macOS (AVFoundation reads mp4, mov, m4v…; ffmpeg in ~/kaonomi/video/bin reads the others)", true);
            return;
        }
        OpenRequested?.Invoke(new VideoPlayerWindowViewModel(item.Entry, _settings, _assistant, _ollama));
        ActivityLog.Info("Video", $"Opened {item.Path}");
    }

    /// <summary>The notes changed in a player: counted again.</summary>
    public void NotesChanged() => _ = CountNotesAsync();

    public void Dispose()
    {
        _closing.Cancel();
        _closing.Dispose();
    }
}
