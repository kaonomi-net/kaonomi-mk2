using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Writing;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Media;

/// <summary>
/// /looper: loops recorded one over the other while they play. Its tempo (or TAP), the beats of a bar, the bars of a
/// loop, the grid the notes are put on, the metronome, a bar counted before recording. Eight tracks: the armed one (●)
/// records what /piano plays (its keys, its pads, the MIDI keyboard, the arpeggiator) while the others play on; each
/// with its sound (an instrument of the piano, the kit of the pads: a note the pad of its place, or a single sound of
/// the pads: Kick, Clap… every note and hit playing it), an audio file (🎵: a WAV, or any sound file through ffmpeg,
/// played at the start of every pass and cut at its end), its volume, MUTE, UNDO (the last recording taken back), CLEAR.
/// The loops exported in WAV or MP3 (through ffmpeg), so many passes of them. ▶ plays the loops, ● records
/// (counting a bar first when the looper is stopped), ■ stops.
/// </summary>
[ToolView(typeof(LooperWindowViewModel), "Looper", "🔁", 1280, 740)]
public sealed class LooperView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (LooperWindowViewModel)viewModel;
        var engine = new LooperEngine(vm);

        var play = Ui.Action("▶ PLAY", () => engine.TogglePlay(), primary: true, tooltip: "Play the loops (again: stop)");
        var record = Ui.Action("● REC", () => engine.ToggleRecord(), tooltip: "Record in the armed loop while it plays (from a stopped looper: a bar counted first)");
        var stop = Ui.Action("■ STOP", () => engine.Stop(), tooltip: "Stop");
        Bind.When(play, vm, () => play.Text = vm.Playing ? "❚❚ PLAYING" : "▶ PLAY", nameof(vm.Playing));
        Bind.When(record, vm, () =>
        {
            record.Text = vm.Recording ? "● RECORDING" : "● REC";
            record.AddThemeColorOverride("font_color", vm.Recording ? E.Red : E.Text);
        }, nameof(vm.Recording));

        var tempo = Bind.Number(new SpinBox { MinValue = 30, MaxValue = 300, Step = 1, Suffix = "BPM", CustomMinimumSize = new Vector2(110, 0), TooltipText = "The tempo" }, vm, nameof(vm.Tempo));
        var tap = Ui.Action("TAP", engine.Tap, tooltip: "Tap the tempo (four times or more)");
        var beats = Bind.Number(new SpinBox { MinValue = 1, MaxValue = 16, Step = 1, CustomMinimumSize = new Vector2(70, 0), TooltipText = "The beats of a bar (4 for 4/4, 3 for 3/4…)" }, vm, nameof(vm.BeatsPerBar));
        var bars = Bind.Number(new SpinBox { MinValue = 1, MaxValue = 64, Step = 1, CustomMinimumSize = new Vector2(70, 0), TooltipText = "The bars of a loop" }, vm, nameof(vm.Bars));
        var quantize = WritingUi.Choice(vm, nameof(vm.Quantize), LooperWindowViewModel.Quantizes);
        quantize.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        quantize.TooltipText = "The grid the notes recorded are put on";
        var position = E.Text_("", E.TextBright, 15);
        position.CustomMinimumSize = new Vector2(170, 0);
        position.HorizontalAlignment = HorizontalAlignment.Right;
        Bind.When(position, vm, () => position.Text = vm.Position, nameof(vm.Position));

        var header = Ui.Toolbar(Ui.Column(8,
            Ui.Row(8, play, record, stop, Ui.Spring(), position,
                Ui.Action("🎹 PIANO", () => vm.OpenPiano?.Invoke(), tooltip: "Open the piano: what it plays is recorded in the armed loop")),
            Ui.Row(10,
                E.Text_("Tempo", E.Muted, 12), tempo, tap,
                E.Text_("Beats / bar", E.Muted, 12), beats,
                E.Text_("Bars / loop", E.Muted, 12), bars,
                E.Text_("Quantize", E.Muted, 12), quantize,
                Ui.Check("Metronome", vm, nameof(vm.Metronome), "The click of the beats while it plays"),
                Ui.Check("Count-in", vm, nameof(vm.CountIn), "A bar counted before recording, when ● starts the looper"),
                Ui.Spring(),
                E.Text_("Export", E.Muted, 12),
                Bind.Number(new SpinBox { MinValue = 1, MaxValue = 64, Step = 1, Prefix = "×", CustomMinimumSize = new Vector2(70, 0), TooltipText = "How many passes of the loops the export holds" }, vm, nameof(vm.ExportPasses)),
                Ui.Action("⬇ WAV", () => _ = engine.Export("wav"), tooltip: "Export the loops in a WAV file (16-bit, 44.1 kHz)"),
                Ui.Action("⬇ MP3", () => _ = engine.Export("mp3"), tooltip: "Export the loops in an MP3 file (through ffmpeg)"),
                Ui.Action("📁", () =>
                {
                    if (File.Exists(vm.LastExport))
                        OS.ShellShowInFileManager(vm.LastExport);
                    else
                    {
                        Directory.CreateDirectory(LooperWindowViewModel.ExportFolder);
                        OS.ShellShowInFileManager(LooperWindowViewModel.ExportFolder);
                    }
                }, tooltip: "Show the last export in the Finder"))));

        var ruler = new LoopRuler(vm, engine) { CustomMinimumSize = new Vector2(0, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var tracks = Ui.Column(6);
        tracks.AddChild(Ui.Row(8, new Control { CustomMinimumSize = new Vector2(TrackWidth, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin }, ruler));
        for (var i = 0; i < vm.Tracks.Count; i++)
            tracks.AddChild(TrackRow(vm, engine, i));

        var hint = E.Text_("Arm a loop (●), then play on /piano: its keys, its pads or your MIDI keyboard. The loop records over itself at every pass; UNDO takes the last recording back.", E.Muted, 12);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var status = Ui.Label(vm, nameof(vm.Status), E.Muted, 12);
        Bind.When(status, vm, () => status.AddThemeColorOverride("font_color", vm.IsError ? E.Red : E.Green), nameof(vm.IsError));
        var page = Ui.Column(10, header, tracks, hint, status);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        page.AddChild(engine);
        // --looper-export=/path/x.wav (tests): the loops exported there without asking, two seconds after opening.
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--looper-export=", StringComparison.Ordinal)) is { } test)
        {
            var target = test["--looper-export=".Length..].Trim('"');
            engine.TreeEntered += () => engine.GetTree().CreateTimer(2).Timeout += () => _ = engine.Export(Path.GetExtension(target).TrimStart('.').ToLowerInvariant(), target);
        }
        return page;
    }

    private const int TrackWidth = 500;

    private static Control TrackRow(LooperWindowViewModel vm, LooperEngine engine, int index)
    {
        var track = vm.Tracks[index];
        var arm = new Button { Text = "●", ToggleMode = true, CustomMinimumSize = new Vector2(34, 0), TooltipText = "Arm this loop: ● records in it" };
        Bind.When(arm, vm, () =>
        {
            arm.SetPressedNoSignal(vm.Armed == index);
            arm.AddThemeColorOverride("font_color", vm.Armed == index ? E.Red : E.Muted);
            arm.AddThemeColorOverride("font_pressed_color", E.Red);
        }, nameof(vm.Armed));
        arm.Pressed += () => engine.Arm(index);
        var name = Bind.Edit(new LineEdit { CustomMinimumSize = new Vector2(80, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "The name of the loop" }, track, nameof(track.Name));
        var instrument = WritingUi.Choice(track, nameof(track.Instrument), LoopTrack.Sounds);
        instrument.TooltipText = "The sound of the loop: an instrument of the piano, the kit of the pads (a note plays the pad of its place: C Kick, C♯ Snare, D Hat…) or one sound of the pads (every note and hit plays it)";
        instrument.CustomMinimumSize = new Vector2(110, 0);
        instrument.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var volume = Bind.Range(new HSlider { MinValue = 0, MaxValue = 100, Step = 1, CustomMinimumSize = new Vector2(60, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "The volume of the loop" }, track, nameof(track.Volume));
        var mute = Bind.Check(new Button { Text = "M", CustomMinimumSize = new Vector2(30, 0), TooltipText = "Mute the loop" }, track, nameof(track.Muted));
        var undo = Ui.Action("↶", () => vm.Undo(track), tooltip: "UNDO: the loop as it was before its last recording or clearing");
        var clear = Ui.Action("✕", () => engine.Clear(track), tooltip: "CLEAR the loop (its notes and its audio file)");
        var audio = Ui.Action("🎵", async () =>
        {
            if (await Dialogs.OpenFile("Add an audio file to the loop", ["*.wav ; WAV", "*.mp3, *.m4a, *.aif, *.aiff, *.flac, *.ogg ; Other audio (through ffmpeg)"]) is { } path)
                await engine.ImportAudio(track, path);
        }, tooltip: "Add an audio file to the loop (a WAV, or another sound file through ffmpeg): played at the start of every pass, cut at its end");
        var controls = Ui.Row(4, arm, name, instrument, volume, mute, audio, undo, clear);
        controls.CustomMinimumSize = new Vector2(TrackWidth, 0);
        controls.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var lane = new LoopLane(vm, engine, index) { CustomMinimumSize = new Vector2(0, 56), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        return Ui.Row(8, controls, lane);
    }
}

/// <summary>
/// The heart of the looper: the clock (in beats), the metronome, the loops played (each track its own synth, so its
/// own instrument), what the piano plays recorded in the armed loop.
/// </summary>
public sealed partial class LooperEngine : Node
{
    private readonly LooperWindowViewModel _vm = null!;
    private readonly List<PianoSynth> _synths = [];
    private readonly List<PianoWindowViewModel> _sounds = [];
    private PianoSynth _click = null!;
    private readonly List<(int Track, int Note, double At)> _offs = [];
    private readonly Dictionary<int, (double At, double Velocity)> _held = [];
    private readonly List<double> _taps = [];
    private readonly Dictionary<string, AudioClip> _audio = [];

    /// <summary>An audio file of a loop: its samples at 44.1 kHz, the peaks of its waveform.</summary>
    public sealed record AudioClip(float[] Left, float[] Right, float[] Peaks);

    private static string Ffmpeg => Path.Combine(VideoMedia.Home, "bin", "ffmpeg");
    private double _abs;
    private bool _recordAtZero;

    /// <summary>Where the looper is, in beats from its start (below 0 while it counts a bar).</summary>
    public double Beat => _abs;

    // Godot builds its scripts with this constructor.
    public LooperEngine()
    {
    }

    public LooperEngine(LooperWindowViewModel vm)
    {
        _vm = vm;
        foreach (var track in vm.Tracks)
        {
            var sound = new PianoWindowViewModel { Instrument = track.Melodic ? track.Instrument : "Piano", Volume = track.Volume, Drums = 90 };
            track.PropertyChanged += (_, _) =>
            {
                if (track.Melodic)
                    sound.Instrument = track.Instrument;
                sound.Volume = track.Volume;
            };
            var synth = new PianoSynth(sound);
            track.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(track.Audio))
                {
                    synth.StopClips();
                    LoadAudio(track.Audio);
                }
                if (e.PropertyName == nameof(track.Muted) && track.Muted)
                    synth.StopClips();
            };
            LoadAudio(track.Audio);
            _sounds.Add(sound);
            _synths.Add(synth);
            AddChild(synth);
        }
        _click = new PianoSynth(new PianoWindowViewModel { Volume = 70, Drums = 100 });
        AddChild(_click);
        PianoSynth.Played += OnPlayed;
        TreeExiting += () =>
        {
            PianoSynth.Played -= OnPlayed;
            Stop();
        };
        _vm.Position = Describe();
    }

    public void TogglePlay()
    {
        if (_vm.Playing)
            Stop();
        else
            Start(false);
    }

    public void ToggleRecord()
    {
        if (!_vm.Playing)
        {
            Start(true);
            return;
        }
        if (_vm.Recording)
            EndRecording();
        else if (_abs >= 0)
            BeginRecording();
        else
            _recordAtZero = !_recordAtZero;
    }

    private void Start(bool record)
    {
        // A hair before the first beat, so that it clicks.
        _abs = (record && _vm.CountIn ? -_vm.BeatsPerBar : 0) - 1e-6;
        _recordAtZero = record;
        _offs.Clear();
        _vm.Playing = true;
        if (record && !_vm.CountIn)
        {
            _abs = 0;
            BeginRecording();
            _abs = -1e-6;
        }
    }

    public void Stop()
    {
        if (_vm.Recording)
            EndRecording();
        _recordAtZero = false;
        _vm.Playing = false;
        foreach (var synth in _synths)
            synth.AllOff();
        _offs.Clear();
        _abs = 0;
        _vm.Position = Describe();
    }

    public void Arm(int track)
    {
        if (_vm.Recording)
        {
            EndRecording();
            _vm.Armed = track;
            BeginRecording();
        }
        else
            _vm.Armed = track;
    }

    public void Clear(LoopTrack track)
    {
        var index = _vm.Tracks.IndexOf(track);
        if (index >= 0)
            _synths[index].AllOff();
        _offs.RemoveAll(o => o.Track == index);
        _vm.Clear(track);
    }

    /// <summary>TAP: the tempo of the last taps (two seconds apart at most).</summary>
    public void Tap()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (_taps.Count > 0 && now - _taps[^1] > 2)
            _taps.Clear();
        _taps.Add(now);
        if (_taps.Count > 8)
            _taps.RemoveAt(0);
        if (_taps.Count >= 2)
            _vm.Tempo = Math.Clamp((int)Math.Round(60 / ((_taps[^1] - _taps[0]) / (_taps.Count - 1))), 30, 300);
    }

    private LoopTrack Armed => _vm.Tracks[Math.Clamp(_vm.Armed, 0, _vm.Tracks.Count - 1)];

    private void BeginRecording()
    {
        _recordAtZero = false;
        _held.Clear();
        Armed.Before = [.. Armed.Events];
        _vm.Recording = true;
    }

    private void EndRecording()
    {
        // The notes still held end now.
        foreach (var note in _held.Keys.ToList())
            Finish(note);
        _vm.Recording = false;
        _vm.Save();
    }

    private double Snap(double beat) => _vm.Grid > 0 ? Math.Round(beat / _vm.Grid) * _vm.Grid : beat;

    /// <summary>Where <paramref name="abs"/> falls in the loop once put on the grid, and the pass it belongs to.</summary>
    private (double Beat, int Pass) InLoop(double abs)
    {
        var length = _vm.LoopBeats;
        var pass = (int)Math.Floor(abs / length);
        var beat = Snap(abs - pass * length);
        if (beat >= length - 1e-9)
        {
            beat -= length;
            pass++;
        }
        return (Math.Max(0, beat), pass);
    }

    private void OnPlayed(PianoSynth.PianoPlayed played)
    {
        if (!_vm.Recording || _abs < 0)
            return;
        var track = Armed;
        // A track empty takes the sound played: the instrument of the piano, or the kit for the pads.
        if (track.Events.Count == 0 && _held.Count == 0 && played.On)
            track.Instrument = played.Pad ? LoopTrack.DrumKit : played.Instrument;
        if (played.Pad)
        {
            var (beat, pass) = InLoop(_abs);
            track.Events.Add(new LoopEvent { Beat = beat, Pad = true, Value = played.Value, Velocity = played.Velocity, Pass = pass });
        }
        else if (played.On)
        {
            if (_held.ContainsKey(played.Value))
                Finish(played.Value);
            _held[played.Value] = (_abs, played.Velocity);
        }
        else if (_held.ContainsKey(played.Value))
            Finish(played.Value);
    }

    private void Finish(int note)
    {
        if (!_held.Remove(note, out var held))
            return;
        var (beat, pass) = InLoop(held.At);
        var length = Math.Min(_abs - held.At, _vm.LoopBeats);
        if (_vm.Grid > 0)
            length = Math.Max(_vm.Grid, Math.Round(length / _vm.Grid) * _vm.Grid);
        Armed.Events.Add(new LoopEvent { Beat = beat, Value = note, Velocity = held.Velocity, Length = Math.Max(0.05, length), Pass = pass });
    }

    public override void _Process(double delta)
    {
        if (!_vm.Playing)
            return;
        var before = _abs;
        _abs += delta * _vm.Tempo / 60.0;

        // The metronome (always while counting the bar before recording).
        for (var b = Math.Floor(before) + 1; b <= _abs; b++)
            if (_vm.Metronome || b < 0)
                _click.Hit(Mod(b, _vm.BeatsPerBar) == 0 ? PianoSynth.MetronomeAccent : PianoSynth.Metronome, 1);

        if (_abs < 0)
        {
            _vm.Position = $"count-in  {(int)Math.Floor(_abs) + _vm.BeatsPerBar + 1} / {_vm.BeatsPerBar}";
            return;
        }
        if (_recordAtZero)
            BeginRecording();

        var length = _vm.LoopBeats;
        var from = Math.Max(before, 0);
        for (var pass = (int)Math.Floor(from / length); pass <= (int)Math.Floor(_abs / length); pass++)
        {
            var a = Math.Max(from, pass * length) - pass * length;
            var b = Math.Min(_abs, (pass + 1) * length) - pass * length;
            if (b <= a)
                continue;
            for (var t = 0; t < _vm.Tracks.Count; t++)
            {
                var track = _vm.Tracks[t];
                if (track.Muted)
                    continue;
                // The audio file of the loop, at the start of every pass.
                if (a == 0 && AudioOf(t) is { } clip)
                    _synths[t].PlayClip(clip.Left, clip.Right, LoopFrames);
                var single = track.SinglePad;
                var kit = track.Instrument == LoopTrack.DrumKit;
                foreach (var e in track.Events.Where(e => e.Beat >= a && e.Beat < b && e.Pass != pass).ToList())
                {
                    // A drum sound chosen: every note and hit plays it; the kit: a note plays the pad of its place.
                    if (single >= 0)
                        _synths[t].Hit(single, e.Velocity);
                    else if (e.Pad)
                        _synths[t].Hit(e.Value, e.Velocity);
                    else if (kit)
                        _synths[t].Hit(LoopTrack.KitPad(e.Value), e.Velocity);
                    else
                    {
                        _synths[t].NoteOn(e.Value, e.Velocity);
                        _offs.Add((t, e.Value, pass * length + e.Beat + e.Length));
                    }
                }
            }
        }
        foreach (var off in _offs.Where(o => o.At <= _abs).ToList())
        {
            _synths[off.Track].NoteOff(off.Note);
            _offs.Remove(off);
        }
        _vm.Position = Describe();
    }

    private static double Mod(double x, double m) => x - m * Math.Floor(x / m);

    /// <summary>The length of a loop in frames (44.1 kHz) at the tempo.</summary>
    private int LoopFrames => (int)Math.Round(_vm.LoopBeats * 60.0 / _vm.Tempo * WavFile.Rate);

    /// <summary>The audio file of a track, read (null when it has none or it cannot be read).</summary>
    public AudioClip? AudioOf(int track) =>
        track >= 0 && track < _vm.Tracks.Count && _audio.TryGetValue(_vm.Tracks[track].Audio, out var clip) ? clip : null;

    /// <summary>A length of the loop compared with an audio file: how much of the loop it covers (more than 1: cut).</summary>
    public double Cover(AudioClip clip) => clip.Left.Length / (double)Math.Max(1, LoopFrames);

    private void LoadAudio(string path)
    {
        if (string.IsNullOrEmpty(path) || _audio.ContainsKey(path))
            return;
        try
        {
            var (left, right) = WavFile.Read(path);
            const int buckets = 512;
            var peaks = new float[buckets];
            for (var i = 0; i < left.Length; i++)
            {
                var b = (int)((long)i * buckets / Math.Max(1, left.Length));
                peaks[b] = Math.Max(peaks[b], Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
            }
            _audio[path] = new AudioClip(left, right, peaks);
        }
        catch (Exception ex)
        {
            Say($"{Path.GetFileName(path)} could not be read: {ex.Message}", true);
        }
    }

    private void Say(string message, bool error = false)
    {
        _vm.Status = message;
        _vm.IsError = error;
    }

    /// <summary>🎵: an audio file copied into the looper (other formats than WAV turned into WAV by ffmpeg), given to the track.</summary>
    public async Task ImportAudio(LoopTrack track, string source)
    {
        try
        {
            Directory.CreateDirectory(LooperWindowViewModel.AudioFolder);
            var name = Path.GetFileNameWithoutExtension(source);
            var target = Path.Combine(LooperWindowViewModel.AudioFolder, name + ".wav");
            for (var n = 2; File.Exists(target); n++)
                target = Path.Combine(LooperWindowViewModel.AudioFolder, $"{name}-{n}.wav");
            var wav = Path.GetExtension(source).Equals(".wav", StringComparison.OrdinalIgnoreCase);
            if (wav)
                File.Copy(source, target);
            if (!wav || !Readable(target))
            {
                if (!File.Exists(Ffmpeg))
                {
                    File.Delete(target);
                    Say($"{Path.GetFileName(source)}: only WAV files are read without ffmpeg (~/kaonomi/video/bin/ffmpeg)", true);
                    return;
                }
                Say($"Converting {Path.GetFileName(source)}…");
                var (code, _, error) = await VideoMedia.RunAsync(Ffmpeg, ["-y", "-i", source, "-ar", "44100", "-ac", "2", "-c:a", "pcm_s16le", target], TimeSpan.FromMinutes(3));
                if (code != 0 || !File.Exists(target))
                {
                    Say($"{Path.GetFileName(source)} could not be converted: {error.Trim().Split('\n').LastOrDefault()}", true);
                    return;
                }
            }
            LoadAudio(target);
            if (!_audio.TryGetValue(target, out var clip))
                return;
            track.BeforeAudio = track.Audio;
            track.Before = [.. track.Events];
            track.Audio = target;
            if (track.Name.StartsWith("Loop ", StringComparison.Ordinal))
                track.Name = name;
            var seconds = clip.Left.Length / (double)WavFile.Rate;
            var loop = LoopFrames / (double)WavFile.Rate;
            Say($"{name}: {seconds:0.0} s in a loop of {loop:0.0} s" + (seconds > loop + 0.05 ? " (cut at the end of the loop)" : ""));
            _vm.Save();
        }
        catch (Exception ex)
        {
            Say($"{Path.GetFileName(source)} could not be added: {ex.Message}", true);
        }
    }

    private static bool Readable(string path)
    {
        try
        {
            WavFile.Read(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The loops rendered (so many passes, and the tail of the last notes): left and right at 44.1 kHz.</summary>
    private async Task<(float[] Left, float[] Right)> Render(int passes)
    {
        var rate = WavFile.Rate;
        var perBeat = 60.0 / _vm.Tempo * rate;
        var length = _vm.LoopBeats;
        var loopFrames = LoopFrames;
        var synths = new List<PianoSynth>();
        var actions = new List<(long At, int Order, Action Do)>();
        for (var t = 0; t < _vm.Tracks.Count; t++)
        {
            var track = _vm.Tracks[t];
            var synth = new PianoSynth(new PianoWindowViewModel { Instrument = track.Melodic ? track.Instrument : "Piano", Volume = track.Volume, Drums = 90 });
            synths.Add(synth);
            if (track.Muted)
                continue;
            var single = track.SinglePad;
            var kit = track.Instrument == LoopTrack.DrumKit;
            var clip = AudioOf(t);
            var events = track.Events.Where(e => e.Beat < length).ToList();
            for (var pass = 0; pass < passes; pass++)
            {
                long start = (long)pass * loopFrames;
                if (clip is not null)
                    actions.Add((start, 0, () => synth.PlayClip(clip.Left, clip.Right, loopFrames)));
                foreach (var e in events)
                {
                    var at = start + (long)Math.Round(e.Beat * perBeat);
                    if (single >= 0 || e.Pad || kit)
                    {
                        var pad = single >= 0 ? single : e.Pad ? e.Value : LoopTrack.KitPad(e.Value);
                        actions.Add((at, 1, () => synth.Hit(pad, e.Velocity)));
                    }
                    else
                    {
                        actions.Add((at, 1, () => synth.NoteOn(e.Value, e.Velocity)));
                        actions.Add((at + (long)Math.Round(e.Length * perBeat), 0, () => synth.NoteOff(e.Value)));
                    }
                }
            }
        }
        actions.Sort((x, y) => x.At != y.At ? x.At.CompareTo(y.At) : x.Order.CompareTo(y.Order));
        var total = (long)passes * loopFrames + rate * 3;
        var left = new float[total];
        var right = new float[total];
        await Task.Run(() =>
        {
            const int block = 64;
            var next = 0;
            for (long at = 0; at < total; at += block)
            {
                var n = (int)Math.Min(block, total - at);
                while (next < actions.Count && actions[next].At < at + n)
                    actions[next++].Do();
                foreach (var synth in synths)
                {
                    var mixed = synth.Mix(n);
                    for (var i = 0; i < n; i++)
                    {
                        left[at + i] += mixed[i].X;
                        right[at + i] += mixed[i].Y;
                    }
                }
            }
        });
        foreach (var synth in synths)
            synth.QueueFree();
        // The tail cut where the last sounds have faded; the whole brought under 1.
        var end = total;
        while (end > (long)passes * loopFrames && Math.Abs(left[end - 1]) < 1e-4 && Math.Abs(right[end - 1]) < 1e-4)
            end--;
        var peak = 0f;
        for (var i = 0; i < end; i++)
            peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        var scale = peak > 0.98f ? 0.98f / peak : 1f;
        var outLeft = new float[end];
        var outRight = new float[end];
        for (var i = 0; i < end; i++)
        {
            outLeft[i] = left[i] * scale;
            outRight[i] = right[i] * scale;
        }
        return (outLeft, outRight);
    }

    /// <summary>⬇ WAV / ⬇ MP3: the loops rendered and written where the user says (MP3 through ffmpeg).</summary>
    public async Task Export(string format, string? to = null)
    {
        if (_vm.Tracks.All(t => t.Muted || (t.Events.Count == 0 && AudioOf(_vm.Tracks.IndexOf(t)) is null)))
        {
            Say("Nothing to export: the loops are empty (or muted)", true);
            return;
        }
        if (format == "mp3" && !File.Exists(Ffmpeg))
        {
            Say("The MP3 export needs ffmpeg (~/kaonomi/video/bin/ffmpeg): export in WAV", true);
            return;
        }
        Directory.CreateDirectory(LooperWindowViewModel.ExportFolder);
        var path = to ?? await Dialogs.SaveFile("Export the loops", $"looper-{DateTime.Now:yyyyMMdd-HHmm}.{format}", [$"*.{format} ; {format.ToUpperInvariant()}"], LooperWindowViewModel.ExportFolder);
        if (path is null)
            return;
        if (!path.EndsWith("." + format, StringComparison.OrdinalIgnoreCase))
            path += "." + format;
        try
        {
            Say($"Rendering {_vm.ExportPasses} passes…");
            var (left, right) = await Render(Math.Max(1, _vm.ExportPasses));
            if (format == "wav")
                await Task.Run(() => WavFile.Write(path, left, right));
            else
            {
                var temporary = Path.Combine(Path.GetTempPath(), $"looper-{Guid.NewGuid():N}.wav");
                await Task.Run(() => WavFile.Write(temporary, left, right));
                Say("Encoding the MP3…");
                var (code, _, error) = await VideoMedia.RunAsync(Ffmpeg, ["-y", "-i", temporary, "-codec:a", "libmp3lame", "-q:a", "2", path], TimeSpan.FromMinutes(5));
                File.Delete(temporary);
                if (code != 0)
                {
                    Say("The MP3 could not be encoded: " + error.Trim().Split('\n').LastOrDefault(), true);
                    return;
                }
            }
            _vm.LastExport = path;
            Say($"Exported: {path} ({left.Length / (double)WavFile.Rate:0.0} s)");
            ActivityLog.Info("Looper", "Exported " + path);
        }
        catch (Exception ex)
        {
            Say("The export failed: " + ex.Message, true);
        }
    }

    /// <summary>"bar 2 / 4 · beat 3".</summary>
    private string Describe()
    {
        var inLoop = Mod(Math.Max(_abs, 0), _vm.LoopBeats);
        var bar = (int)(inLoop / _vm.BeatsPerBar);
        var beat = (int)(inLoop - bar * _vm.BeatsPerBar);
        return $"bar {bar + 1} / {_vm.Bars} · beat {beat + 1}";
    }

    /// <summary>Where the playhead is in the loop (0 to 1), or -1 when stopped or counting.</summary>
    public double Playhead => _vm.Playing && _abs >= 0 ? Mod(_abs, _vm.LoopBeats) / _vm.LoopBeats : -1;
}

/// <summary>The bars and beats of the loop above the lanes, the playhead running over them.</summary>
public sealed partial class LoopRuler : Control
{
    private readonly LooperWindowViewModel _vm = null!;
    private readonly LooperEngine _engine = null!;

    // Godot builds its scripts with this constructor.
    public LoopRuler()
    {
    }

    public LoopRuler(LooperWindowViewModel vm, LooperEngine engine)
    {
        _vm = vm;
        _engine = engine;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var beats = (int)_vm.LoopBeats;
        for (var b = 0; b <= beats; b++)
        {
            var x = Size.X * b / beats;
            var bar = b % _vm.BeatsPerBar == 0;
            DrawLine(new Vector2(x, bar ? 4 : 14), new Vector2(x, Size.Y), bar ? E.Text : E.Edge, bar ? 1.5f : 1);
            if (bar && b < beats)
                DrawString(E.Body, new Vector2(x + 4, 14), (b / _vm.BeatsPerBar + 1).ToString(), HorizontalAlignment.Left, -1, 11, E.Muted);
        }
        if (_engine.Playhead >= 0)
        {
            var x = (float)(Size.X * _engine.Playhead);
            DrawColoredPolygon([new Vector2(x - 6, 0), new Vector2(x + 6, 0), new Vector2(x, 10)], _vm.Recording ? E.Red : E.Cyan);
        }
    }
}

/// <summary>A loop drawn: its notes (by their pitch) and its pad hits, the bars, the playhead.</summary>
public sealed partial class LoopLane : Control
{
    private readonly LooperWindowViewModel _vm = null!;
    private readonly LooperEngine _engine = null!;
    private readonly int _index;

    // Godot builds its scripts with this constructor.
    public LoopLane()
    {
    }

    public LoopLane(LooperWindowViewModel vm, LooperEngine engine, int index)
    {
        _vm = vm;
        _engine = engine;
        _index = index;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "The loop: its notes and its pad hits (a click arms it)";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _engine.Arm(_index);
            AcceptEvent();
        }
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var track = _vm.Tracks[_index];
        var armed = _vm.Armed == _index;
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, E.Field);
        var length = _vm.LoopBeats;
        var beats = (int)length;
        for (var b = 1; b < beats; b++)
        {
            var x = Size.X * b / beats;
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), b % _vm.BeatsPerBar == 0 ? E.Edge : E.Edge with { A = E.Edge.A * 0.35f }, 1);
        }

        // The audio file: its waveform over the part of the loop it covers.
        if (_engine.AudioOf(_index) is { } clip)
        {
            var cover = _engine.Cover(clip);
            var wave = (track.Muted ? E.Muted : E.Amber) with { A = 0.45f };
            for (var b = 0; b < clip.Peaks.Length; b++)
            {
                var x = (float)(Size.X * cover * b / clip.Peaks.Length);
                if (x > Size.X)
                    break;
                var h = clip.Peaks[b] * (Size.Y - 6) / 2;
                DrawLine(new Vector2(x, Size.Y / 2 - h), new Vector2(x, Size.Y / 2 + h), wave, Math.Max(1, (float)(Size.X * cover / clip.Peaks.Length)));
            }
        }

        var events = track.Events.ToList();
        var notes = events.Where(e => !e.Pad).ToList();
        var padRow = events.Any(e => e.Pad) ? 14f : 0f;
        var colour = track.Muted ? E.Muted : armed ? E.Red.Lerp(E.Amber, 0.3f) : E.Cyan;
        if (notes.Count > 0)
        {
            var low = notes.Min(e => e.Value) - 1;
            var high = notes.Max(e => e.Value) + 1;
            var h = Size.Y - padRow - 6;
            var step = h / Math.Max(1, high - low);
            foreach (var e in notes.Where(e => e.Beat < length))
            {
                var x = (float)(Size.X * e.Beat / length);
                var w = (float)Math.Max(2, Size.X * e.Length / length);
                var y = 3 + (high - e.Value) * step - step / 2;
                var bar = new Rect2(x, y - Math.Max(2, step * 0.4f), w, Math.Max(3, step * 0.8f));
                // Drums: the colour of the pad played.
                if (!track.Muted && !track.Melodic)
                    colour = LaunchkeyBody.PadColour(track.SinglePad >= 0 ? track.SinglePad : LoopTrack.KitPad(e.Value));
                DrawRect(bar, colour with { A = 0.45f + 0.5f * (float)e.Velocity });
                // A note going over the end of the loop goes on at its start.
                if (bar.End.X > Size.X)
                    DrawRect(new Rect2(0, bar.Position.Y, bar.End.X - Size.X, bar.Size.Y), colour with { A = 0.45f + 0.5f * (float)e.Velocity });
            }
        }
        foreach (var e in events.Where(e => e.Pad && e.Beat < length))
        {
            var x = (float)(Size.X * e.Beat / length);
            DrawRect(new Rect2(x - 3, Size.Y - padRow + 2, 6, 9), LaunchkeyBody.PadColour(track.SinglePad >= 0 ? track.SinglePad : e.Value));
        }
        if (events.Count == 0 && _engine.AudioOf(_index) is null)
            DrawString(E.Body, new Vector2(10, Size.Y / 2 + 5), armed ? "armed: ● records here what /piano plays" : "empty", HorizontalAlignment.Left, -1, 12, E.Muted);

        if (_engine.Playhead >= 0)
        {
            var x = (float)(Size.X * _engine.Playhead);
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), armed && _vm.Recording ? E.Red : E.TextBright, 1.5f);
        }
        DrawRect(rect, armed ? (_vm.Recording ? E.Red : E.Red with { A = 0.5f }) : E.Edge, false, armed ? 2 : 1);
    }
}
