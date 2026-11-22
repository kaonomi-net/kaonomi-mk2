using System;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The sampler (<c>/sampler</c>): the keyboard of <c>/piano</c> playing a sound recorded with the microphone (or a
/// sound file). A recording opens a small editor: the part kept (dragged on the waveform), its gain (or NORMALIZE),
/// heard before it is VALIDATED; then every key plays it, higher or lower than its root note. The recordings are kept
/// (<c>~/kaonomi/sampler/recordings</c>), the sounds validated too (<c>~/kaonomi/sampler/samples</c>, never written
/// over), the sound played and how it was cut in <c>~/kaonomi/sampler/current.json</c>.
/// </summary>
public sealed partial class SamplerWindowViewModel : ViewModelBase
{
    public static readonly string[] Modes = ["One-shot", "Hold"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "sampler");

    public static string RecordingFolder => Path.Combine(Folder, "recordings");

    public static string SampleFolder => Path.Combine(Folder, "samples");

    private static string CurrentPath => Path.Combine(Folder, "current.json");

    /// <summary>The state of the keyboard (octave, sustain, MIDI, pitch bend, volume).</summary>
    public PianoWindowViewModel Keys { get; } = new();

    /// <summary>The name of the sound (its file once validated).</summary>
    [ObservableProperty]
    private string _name = "SAMPLE";

    /// <summary>The note that plays the sound as it was recorded (60: C4).</summary>
    [ObservableProperty]
    private int _root = 60;

    /// <summary>One-shot: the sound plays to its end whatever the key; Hold: it stops (fades) when the key is released.</summary>
    [ObservableProperty]
    private string _mode = "Hold";

    /// <summary>How long it fades once the key is released (Hold), 0 to 100.</summary>
    [ObservableProperty]
    private int _release = 30;

    [ObservableProperty]
    private bool _recording;

    /// <summary>True while the editor is open (a recording to cut).</summary>
    [ObservableProperty]
    private bool _editing;

    /// <summary>The part kept, in parts of the recording (0 to 1).</summary>
    [ObservableProperty]
    private double _start;

    [ObservableProperty]
    private double _end = 1;

    /// <summary>The gain of the part kept, in dB (-24 to 24).</summary>
    [ObservableProperty]
    private double _gain;

    /// <summary>The recording being edited (its file).</summary>
    [ObservableProperty]
    private string _source = "";

    /// <summary>The sound played (its file), "" when there is none yet.</summary>
    [ObservableProperty]
    private string _sample = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isError;

    public static string NoteName(int note) =>
        new[] { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" }[((note % 12) + 12) % 12] + (note / 12 - 1);

    private sealed class Current
    {
        public string Name { get; set; } = "SAMPLE";
        public string Sample { get; set; } = "";
        public string Source { get; set; } = "";
        public double Start { get; set; }
        public double End { get; set; } = 1;
        public double Gain { get; set; }
        public int Root { get; set; } = 60;
        public string Mode { get; set; } = "Hold";
        public int Release { get; set; } = 30;
    }

    public SamplerWindowViewModel()
    {
        try
        {
            if (File.Exists(CurrentPath) && JsonSerializer.Deserialize<Current>(File.ReadAllText(CurrentPath)) is { } current)
            {
                _name = current.Name;
                _sample = File.Exists(current.Sample) ? current.Sample : "";
                _source = current.Source;
                _start = current.Start;
                _end = current.End;
                _gain = current.Gain;
                _root = Math.Clamp(current.Root, 0, 127);
                _mode = Array.IndexOf(Modes, current.Mode) >= 0 ? current.Mode : "Hold";
                _release = Math.Clamp(current.Release, 0, 100);
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Sampler", "The current sound could not be read: " + ex.Message);
        }
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Root) or nameof(Mode) or nameof(Release) or nameof(Name))
                SaveCurrent();
        };
    }

    public void SaveCurrent()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var current = new Current { Name = Name, Sample = Sample, Source = Source, Start = Start, End = End, Gain = Gain, Root = Root, Mode = Mode, Release = Release };
            File.WriteAllText(CurrentPath, JsonSerializer.Serialize(current, Json));
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Sampler", "The current sound could not be saved: " + ex.Message);
        }
    }

    public void Say(string message, bool error = false)
    {
        Status = message;
        IsError = error;
    }
}
