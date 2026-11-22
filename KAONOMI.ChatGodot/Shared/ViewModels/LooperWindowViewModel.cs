using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>A note or a pad hit of a loop: where it begins in the loop (in beats), how long it is held (notes).</summary>
public sealed class LoopEvent
{
    public double Beat { get; set; }

    /// <summary>True for a drum pad (<see cref="Value"/> its number), false for a note (<see cref="Value"/> its MIDI number).</summary>
    public bool Pad { get; set; }

    public int Value { get; set; }

    public double Velocity { get; set; } = 0.8;

    /// <summary>How long the note is held, in beats.</summary>
    public double Length { get; set; }

    /// <summary>The pass of the loop during which it was recorded (not played again in that pass: it was heard).</summary>
    [JsonIgnore]
    public int Pass { get; set; } = -1;
}

/// <summary>A track of the looper: its loop (what was recorded in it), its instrument, its volume.</summary>
public sealed partial class LoopTrack : ObservableObject
{
    [ObservableProperty]
    private string _name = "Loop";

    [ObservableProperty]
    private string _instrument = "Piano";

    /// <summary>0 to 100.</summary>
    [ObservableProperty]
    private int _volume = 80;

    [ObservableProperty]
    private bool _muted;

    /// <summary>An audio file played at the start of every pass of the loop (cut at its end), or "".</summary>
    [ObservableProperty]
    private string _audio = "";

    /// <summary>The audio file before the last clearing (UNDO).</summary>
    [JsonIgnore]
    public string? BeforeAudio { get; set; }

    public List<LoopEvent> Events { get; set; } = [];

    /// <summary>The kit of the pads: a note plays the pad of its place (C Kick, C♯ Snare, D Hat… from the bottom row).</summary>
    public const string DrumKit = "Drum kit";

    /// <summary>The sounds a track can take: the instruments of the piano, the kit of the pads, each sound of the pads.</summary>
    public static readonly string[] Sounds = [.. PianoWindowViewModel.Instruments, DrumKit, .. PianoWindowViewModel.PadNames];

    /// <summary>The pad of a single drum sound chosen for the track (every note and hit plays it), or -1.</summary>
    [JsonIgnore]
    public int SinglePad => Array.IndexOf(PianoWindowViewModel.PadNames, Instrument);

    /// <summary>True when the track plays the instruments of the piano (not drums).</summary>
    [JsonIgnore]
    public bool Melodic => PianoWindowViewModel.Instruments.Contains(Instrument);

    /// <summary>The pad of a note in the kit: from C, the bottom row (Kick, Snare, Hat…) then the top one (Clap, Rim…).</summary>
    public static int KitPad(int note)
    {
        var k = ((note - 36) % 16 + 16) % 16;
        return k < 8 ? 8 + k : k - 8;
    }

    /// <summary>The loop as it was before the last recording (UNDO).</summary>
    [JsonIgnore]
    public List<LoopEvent>? Before { get; set; }
}

/// <summary>
/// The looper (<c>/looper</c>): loops recorded one over the other while they play, from what the piano plays (the
/// keys, the pads, a MIDI keyboard through <c>/piano</c>). Its tempo, the beats of a bar, the bars of a loop; the
/// metronome, a bar counted before recording, the notes put on the grid (quantize); eight tracks, each with its
/// sound (an instrument of the piano, the kit of the pads or one of their sounds), its volume, muted or not. Kept in <c>~/kaonomi/looper/session.json</c>.
/// </summary>
public sealed partial class LooperWindowViewModel : ViewModelBase
{
    public const int TrackCount = 8;
    public static readonly int[] BarChoices = [1, 2, 3, 4, 6, 8, 12, 16];
    public static readonly string[] Quantizes = ["Off", "1/4", "1/8", "1/16"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "looper");

    public static string FilePath => Path.Combine(Folder, "session.json");

    /// <summary>Where the audio files added to the loops are copied.</summary>
    public static string AudioFolder => Path.Combine(Folder, "audio");

    /// <summary>Where the exports are offered.</summary>
    public static string ExportFolder => Path.Combine(Folder, "exports");

    /// <summary>How many passes of the loops an export holds.</summary>
    [ObservableProperty]
    private int _exportPasses = 4;

    /// <summary>What the looper says (an export done, an audio file that could not be read…).</summary>
    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isError;

    /// <summary>The last file exported (📁 shows it).</summary>
    [ObservableProperty]
    private string _lastExport = "";

    /// <summary>Beats per minute.</summary>
    [ObservableProperty]
    private int _tempo = 120;

    /// <summary>The beats of a bar (4 for 4/4, 3 for 3/4…).</summary>
    [ObservableProperty]
    private int _beatsPerBar = 4;

    /// <summary>The bars of a loop.</summary>
    [ObservableProperty]
    private int _bars = 2;

    [ObservableProperty]
    private bool _metronome = true;

    /// <summary>A bar of metronome counted before the recording begins (when REC starts the looper).</summary>
    [ObservableProperty]
    private bool _countIn = true;

    /// <summary>The grid the notes recorded are put on: Off, 1/4, 1/8, 1/16.</summary>
    [ObservableProperty]
    private string _quantize = "1/16";

    /// <summary>The track that records (0 to 7).</summary>
    [ObservableProperty]
    private int _armed;

    [ObservableProperty]
    private bool _playing;

    [ObservableProperty]
    private bool _recording;

    /// <summary>Where the looper is: "bar 1 · beat 1", or the count before recording.</summary>
    [ObservableProperty]
    private string _position = "";

    public ObservableCollection<LoopTrack> Tracks { get; } = [];

    /// <summary>Opens the piano (set by the chat).</summary>
    public Action? OpenPiano { get; init; }

    public LooperWindowViewModel()
    {
        Load();
        while (Tracks.Count < TrackCount)
            Tracks.Add(new LoopTrack { Name = $"Loop {Tracks.Count + 1}" });
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Tempo) or nameof(BeatsPerBar) or nameof(Bars) or nameof(Metronome) or nameof(CountIn) or nameof(Quantize))
                Save();
        };
        foreach (var track in Tracks)
            track.PropertyChanged += (_, _) => Save();
    }

    /// <summary>The length of a loop, in beats.</summary>
    public double LoopBeats => Math.Max(1, Bars) * Math.Max(1, BeatsPerBar);

    /// <summary>The grid of the quantize, in beats (0: none).</summary>
    public double Grid => Quantize switch
    {
        "1/4" => 1,
        "1/8" => 0.5,
        "1/16" => 0.25,
        _ => 0
    };

    public void Clear(LoopTrack track)
    {
        track.Before = [.. track.Events];
        track.BeforeAudio = track.Audio;
        track.Events.Clear();
        track.Audio = "";
        Save();
    }

    /// <summary>The track as it was before its last recording or clearing.</summary>
    public void Undo(LoopTrack track)
    {
        if (track.Before is null)
            return;
        (track.Events, track.Before) = (track.Before, null);
        if (track.BeforeAudio is { } audio)
            track.Audio = audio;
        track.BeforeAudio = null;
        Save();
    }

    private sealed class Session
    {
        public int Tempo { get; set; } = 120;
        public int BeatsPerBar { get; set; } = 4;
        public int Bars { get; set; } = 2;
        public bool Metronome { get; set; } = true;
        public bool CountIn { get; set; } = true;
        public string Quantize { get; set; } = "1/16";
        public List<LoopTrack> Tracks { get; set; } = [];
    }

    private bool _loading;

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(FilePath));
            if (session is null)
                return;
            _loading = true;
            Tempo = Math.Clamp(session.Tempo, 30, 300);
            BeatsPerBar = Math.Clamp(session.BeatsPerBar, 1, 16);
            Bars = Math.Clamp(session.Bars, 1, 64);
            Metronome = session.Metronome;
            CountIn = session.CountIn;
            Quantize = Quantizes.Contains(session.Quantize) ? session.Quantize : "1/16";
            foreach (var track in session.Tracks.Take(TrackCount))
                Tracks.Add(track);
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Looper", "The session could not be read: " + ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    public void Save()
    {
        if (_loading)
            return;
        try
        {
            Directory.CreateDirectory(Folder);
            var session = new Session
            {
                Tempo = Tempo, BeatsPerBar = BeatsPerBar, Bars = Bars, Metronome = Metronome, CountIn = CountIn, Quantize = Quantize,
                Tracks = [.. Tracks]
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(session, Json));
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Looper", "The session could not be saved: " + ex.Message);
        }
    }
}
