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
/// /sampler: the keyboard of /piano playing a sound recorded with the microphone. ● REC records (the level and the
/// time shown; one minute at most), ■ STOP opens the small editor: the waveform, the part kept dragged on it (its edges
/// moved, a double click: all; AUTO-TRIM: the silences around taken away), its gain (−24 to +24 dB, NORMALIZE: its peak
/// at −1 dB; what would clip in red), ▷ PREVIEW, ✓ VALIDATE (the part kept, with a short fade at both ends, saved and
/// played by the keys) or ✕ CANCEL. 📂 opens a sound file the same way (WAV, or MP3/M4A/AIFF/FLAC/OGG through ffmpeg),
/// ✎ EDIT opens the recording of the sound played again. The root note (the key that plays it as recorded), One-shot
/// (to its end) or Hold (it fades when the key is released, the release set), the volume; the pads of a MIDI keyboard
/// play it too, a semitone apart from the root.
/// </summary>
[ToolView(typeof(SamplerWindowViewModel), "Sampler", "🎤", 1300, 700)]
public sealed class SamplerView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (SamplerWindowViewModel)viewModel;
        var synth = new SamplerSynth(vm);
        var keyboard = new PianoKeyboard(vm.Keys, synth) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(600, 170) };
        var editor = new SamplerEditor(vm, synth);

        // The sound played, read again.
        if (File.Exists(vm.Sample))
        {
            try
            {
                var (left, right) = WavFile.Read(vm.Sample);
                synth.SetSample(left, right);
            }
            catch (Exception ex)
            {
                vm.Say($"{Path.GetFileName(vm.Sample)} could not be read: {ex.Message}", true);
            }
        }
        else
            vm.Say("● REC records a sound with the microphone (or 📂 opens a sound file); then the keys play it");

        var record = Ui.Action("● REC", editor.ToggleRecord, primary: true, tooltip: "Record a sound with the microphone (again: stop, and the editor opens)");
        Bind.When(record, vm, () =>
        {
            record.Text = vm.Recording ? "■ STOP" : "● REC";
            record.Modulate = vm.Recording ? new Color(1f, 0.5f, 0.5f) : Colors.White;
        }, nameof(vm.Recording));
        var level = new ProgressBar { MinValue = -60, MaxValue = 0, ShowPercentage = false, CustomMinimumSize = new Vector2(90, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "The level of the microphone while recording" };
        var time = E.Text_("", E.TextBright, 13);
        time.CustomMinimumSize = new Vector2(48, 0);
        var meter = new Timer { WaitTime = 0.05, Autostart = true };
        meter.Timeout += () =>
        {
            var (seconds, rms) = editor.Meter();
            level.Value = rms;
            time.Text = vm.Recording ? $"{seconds:0.0} s" : "";
        };

        var name = Bind.Edit(new LineEdit { CustomMinimumSize = new Vector2(150, 0), TooltipText = "The name of the sound (its file once validated)" }, vm, nameof(vm.Name));
        var root = Bind.Number(new SpinBox { MinValue = 24, MaxValue = 96, Step = 1, CustomMinimumSize = new Vector2(80, 0), TooltipText = "The root note: the key that plays the sound as it was recorded" }, vm, nameof(vm.Root));
        var rootName = E.Text_("", E.TextBright, 13);
        rootName.CustomMinimumSize = new Vector2(34, 0);
        Bind.When(rootName, vm, () => rootName.Text = SamplerWindowViewModel.NoteName(vm.Root), nameof(vm.Root));
        var mode = WritingUi.Choice(vm, nameof(vm.Mode), SamplerWindowViewModel.Modes);
        mode.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        mode.TooltipText = "One-shot: the sound plays to its end; Hold: it fades when the key is released";
        var release = Bind.Range(new HSlider { MinValue = 0, MaxValue = 100, Step = 1, CustomMinimumSize = new Vector2(90, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "The release: how long the sound fades once the key is released (Hold)" }, vm, nameof(vm.Release));
        var volume = Bind.Range(new HSlider { MinValue = 0, MaxValue = 100, Step = 1, CustomMinimumSize = new Vector2(90, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "The volume" }, vm.Keys, nameof(vm.Keys.Volume));
        var midi = E.Text_("", E.Muted, 12);
        Bind.When(midi, vm.Keys, () =>
        {
            midi.Text = vm.Keys.HasMidi ? "🎹 MIDI" : "no MIDI keyboard";
            midi.TooltipText = vm.Keys.Midi + (vm.Keys.HasMidi ? "\nIts pads play the sound too, a semitone apart from the root" : "");
            midi.AddThemeColorOverride("font_color", vm.Keys.HasMidi ? E.Green : E.Muted);
        }, nameof(vm.Keys.Midi), nameof(vm.Keys.HasMidi));

        var header = Ui.Toolbar(Ui.Row(8,
            E.Caption("SAMPLER", E.TextBright, 14), record, level, time,
            Ui.Action("📂", () => _ = editor.Open(), tooltip: "Open a sound file (WAV, or MP3, M4A, AIFF, FLAC, OGG through ffmpeg) in the editor"),
            Ui.Action("✎ EDIT", editor.EditAgain, tooltip: "Open the recording of the sound played again in the editor (another part, another gain)"),
            name,
            E.Text_("Root", E.Muted, 12), root, rootName,
            mode,
            E.Text_("Release", E.Muted, 12), release,
            E.Text_("Volume", E.Muted, 12), volume,
            Ui.Spring(), midi));

        var octave = E.Text_("", E.TextBright, 13);
        octave.HorizontalAlignment = HorizontalAlignment.Center;
        Bind.When(octave, vm.Keys, () => octave.Text = $"C{vm.Keys.Octave} – C{vm.Keys.Octave + 3}", nameof(vm.Keys.Octave));
        var side = Ui.Column(6,
            Ui.Row(4, Ui.Action("OCT −", () => vm.Keys.Octave = Math.Max(1, vm.Keys.Octave - 1), tooltip: "The keyboard an octave lower"),
                Ui.Action("OCT +", () => vm.Keys.Octave = Math.Min(6, vm.Keys.Octave + 1), tooltip: "The keyboard an octave higher")),
            octave,
            Ui.Check("HOLD", vm.Keys, nameof(vm.Keys.Sustain), "The sustain pedal (or hold the space bar)"));
        side.CustomMinimumSize = new Vector2(130, 0);
        var keys = Ui.Row(10, side, keyboard);
        keys.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var status = Ui.Label(vm, nameof(vm.Status), E.Muted, 12);
        Bind.When(status, vm, () => status.AddThemeColorOverride("font_color", vm.IsError ? E.Red : E.Green), nameof(vm.IsError));
        var played = Ui.Label(vm.Keys, nameof(vm.Keys.Played), E.Muted, 12);
        played.ClipText = true;
        played.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        // --sampler-open=/path/x.wav (tests): that file in the editor, as a recording would be.
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--sampler-open=", StringComparison.Ordinal)) is { } test)
            editor.Ready += () => editor.OpenPath(test["--sampler-open=".Length..].Trim('"'));

        var page = Ui.Column(8, header, editor, keys, Ui.Row(10, status, played));
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        page.AddChild(synth);
        page.AddChild(meter);
        return page;
    }
}

/// <summary>
/// The waveform of the sampler, and its small editor: the sound played shown (or nothing yet); while editing, the
/// recording with the part kept, its gain and the buttons that decide.
/// </summary>
public sealed partial class SamplerEditor : VBoxContainer
{
    private readonly SamplerWindowViewModel _vm = null!;
    private readonly SamplerSynth _synth = null!;
    private readonly SampleWave _wave = null!;
    private AudioRecorder? _recorder;
    private float[] _rawLeft = [];
    private float[] _rawRight = [];

    private static string Ffmpeg => Path.Combine(VideoMedia.Home, "bin", "ffmpeg");

    // Godot builds its scripts with this constructor.
    public SamplerEditor()
    {
    }

    public SamplerEditor(SamplerWindowViewModel vm, SamplerSynth synth)
    {
        _vm = vm;
        _synth = synth;
        AddThemeConstantOverride("separation", 6);
        _wave = new SampleWave(vm, synth, () => _vm.Editing ? (_rawLeft, _rawRight) : (_synth.Left, _synth.Right))
        {
            CustomMinimumSize = new Vector2(0, 190),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        AddChild(_wave);

        var gain = Bind.Range(new HSlider { MinValue = -24, MaxValue = 24, Step = 0.5, CustomMinimumSize = new Vector2(180, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "The gain of the part kept, in dB" }, vm, nameof(vm.Gain));
        var gainText = E.Text_("", E.TextBright, 13);
        gainText.CustomMinimumSize = new Vector2(70, 0);
        Bind.When(gainText, vm, () => gainText.Text = $"{vm.Gain:+0.0;-0.0;0.0} dB", nameof(vm.Gain));
        var part = E.Text_("", E.Muted, 12);
        Bind.When(part, vm, () =>
        {
            var seconds = _rawLeft.Length / (double)WavFile.Rate;
            part.Text = $"kept {vm.Start * seconds:0.00} s → {vm.End * seconds:0.00} s ({(vm.End - vm.Start) * seconds:0.00} s of {seconds:0.00} s)";
        }, nameof(vm.Start), nameof(vm.End), nameof(vm.Source), nameof(vm.Editing));
        var tools = Ui.Row(8,
            E.Caption("EDIT", E.TextBright, 12),
            part,
            Ui.Action("AUTO-TRIM", AutoTrim, tooltip: "Keep the sound without the silences before and after it"),
            Ui.Action("ALL", () => { _vm.Start = 0; _vm.End = 1; }, tooltip: "Keep the whole recording (also a double click on the waveform)"),
            E.Text_("Gain", E.Muted, 12), gain, gainText,
            Ui.Action("NORMALIZE", Normalize, tooltip: "The gain that brings the peak of the part kept to −1 dB"),
            Ui.Spring(),
            Ui.Action("▷ PREVIEW", () => _synth.Preview(Slice()), tooltip: "Hear the part kept, with its gain"),
            Ui.Action("✕ CANCEL", Cancel, tooltip: "Close the editor: the sound played stays as it was"),
            Ui.Action("✓ VALIDATE", Validate, primary: true, tooltip: "Keep this part with this gain: the keys play it"));
        Bind.Visible(tools, vm, nameof(vm.Editing));
        AddChild(tools);
        Bind.When(this, vm, _wave.QueueRedraw, nameof(vm.Editing), nameof(vm.Sample));
        TreeExiting += () =>
        {
            _recorder?.Dispose();
            _recorder = null;
            _vm.Recording = false;
        };
    }

    /// <summary>The time recorded and the level of the microphone (dB), for the meter.</summary>
    public (double Seconds, double Rms) Meter()
    {
        if (_recorder is not { IsRecording: true } recorder)
            return (0, -60);
        var frame = recorder.Analyzer.Analyze();
        var seconds = recorder.Elapsed.TotalSeconds;
        if (seconds >= 60)
            ToggleRecord();
        return (seconds, Math.Clamp(AudioFrame.Decibels(frame.Rms), -60, 0));
    }

    public void ToggleRecord()
    {
        if (_vm.Recording)
        {
            var recording = _recorder?.StopRecording();
            _recorder?.Dispose();
            _recorder = null;
            _vm.Recording = false;
            if (recording is null || recording.Seconds < 0.05)
            {
                _vm.Say("Nothing was recorded", true);
                return;
            }
            if (recording.Error is { } error)
                _vm.Say("The recording had a problem: " + error, true);
            if (OpenEditor(recording.Path, 0, 1, 0))
                AutoTrim();
            return;
        }
        try
        {
            Directory.CreateDirectory(SamplerWindowViewModel.RecordingFolder);
            var path = Path.Combine(SamplerWindowViewModel.RecordingFolder, $"rec-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            _recorder = new AudioRecorder();
            _recorder.Listen();
            _recorder.StartRecording(path);
            _vm.Recording = true;
            _vm.Say($"Recording with {_recorder.SourceName}… (■ STOP to end it)");
        }
        catch (Exception ex)
        {
            _recorder?.Dispose();
            _recorder = null;
            _vm.Say("The microphone could not be opened: " + ex.Message, true);
        }
    }

    /// <summary>📂: a sound file opened in the editor (other formats than WAV turned into WAV by ffmpeg, next to the recordings).</summary>
    public async Task Open()
    {
        if (await Dialogs.OpenFile("Open a sound to sample", ["*.wav ; WAV", "*.mp3, *.m4a, *.aif, *.aiff, *.flac, *.ogg ; Other audio (through ffmpeg)"]) is not { } source)
            return;
        var path = source;
        if (!Path.GetExtension(source).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(Ffmpeg))
            {
                _vm.Say("Only WAV files are read without ffmpeg (~/kaonomi/video/bin/ffmpeg)", true);
                return;
            }
            Directory.CreateDirectory(SamplerWindowViewModel.RecordingFolder);
            path = Path.Combine(SamplerWindowViewModel.RecordingFolder, Path.GetFileNameWithoutExtension(source) + $"-{DateTime.Now:HHmmss}.wav");
            _vm.Say($"Converting {Path.GetFileName(source)}…");
            var (code, _, error) = await VideoMedia.RunAsync(Ffmpeg, ["-y", "-i", source, "-ar", "44100", "-ac", "2", "-c:a", "pcm_s16le", path], TimeSpan.FromMinutes(3));
            if (code != 0 || !File.Exists(path))
            {
                _vm.Say($"{Path.GetFileName(source)} could not be converted: {error.Trim().Split('\n').LastOrDefault()}", true);
                return;
            }
        }
        _vm.Name = Path.GetFileNameWithoutExtension(source).ToUpperInvariant();
        if (OpenEditor(path, 0, 1, 0))
            AutoTrim();
    }

    /// <summary>A sound file (WAV) opened in the editor, the silences around it taken away (--sampler-open, tests).</summary>
    public void OpenPath(string path)
    {
        if (OpenEditor(path, 0, 1, 0))
            AutoTrim();
    }

    /// <summary>✎ EDIT: the recording of the sound played, as it was cut (or the sound itself when its recording is gone).</summary>
    public void EditAgain()
    {
        if (File.Exists(_vm.Source))
            OpenEditor(_vm.Source, _vm.Start, _vm.End, _vm.Gain);
        else if (File.Exists(_vm.Sample))
            OpenEditor(_vm.Sample, 0, 1, 0);
        else
            _vm.Say("No sound yet: ● REC records one", true);
    }

    private bool OpenEditor(string path, double start, double end, double gain)
    {
        try
        {
            (_rawLeft, _rawRight) = WavFile.Read(path);
            _vm.Source = path;
            _vm.Start = Math.Clamp(start, 0, 1);
            _vm.End = Math.Clamp(end, _vm.Start, 1);
            _vm.Gain = gain;
            _vm.Editing = true;
            _wave.Reset();
            _vm.Say($"{Path.GetFileName(path)}: drag on the waveform the part to keep, set its gain, ▷ PREVIEW, then ✓ VALIDATE");
            return true;
        }
        catch (Exception ex)
        {
            _vm.Say($"{Path.GetFileName(path)} could not be read: {ex.Message}", true);
            return false;
        }
    }

    private void AutoTrim()
    {
        var length = _rawLeft.Length;
        if (length == 0)
            return;
        var peak = 0f;
        for (var i = 0; i < length; i++)
            peak = Math.Max(peak, Math.Max(Math.Abs(_rawLeft[i]), Math.Abs(_rawRight[i])));
        var threshold = Math.Max(peak * 0.06f, 0.002f);
        var first = 0;
        while (first < length && Math.Abs(_rawLeft[first]) < threshold && Math.Abs(_rawRight[first]) < threshold)
            first++;
        var last = length - 1;
        while (last > first && Math.Abs(_rawLeft[last]) < threshold && Math.Abs(_rawRight[last]) < threshold)
            last--;
        if (first >= last)
            return;
        _vm.Start = Math.Max(0, first - WavFile.Rate * 0.01) / length;
        _vm.End = Math.Min(length, last + WavFile.Rate * 0.08) / length;
    }

    private (int From, int To) Part()
    {
        var length = _rawLeft.Length;
        var from = (int)Math.Clamp(_vm.Start * length, 0, length);
        var to = (int)Math.Clamp(_vm.End * length, from, length);
        return (from, to);
    }

    private void Normalize()
    {
        var (from, to) = Part();
        var peak = 0f;
        for (var i = from; i < to; i++)
            peak = Math.Max(peak, Math.Max(Math.Abs(_rawLeft[i]), Math.Abs(_rawRight[i])));
        if (peak <= 1e-5f)
            return;
        _vm.Gain = Math.Round(Math.Clamp(20 * Math.Log10(0.891 / peak), -24, 24) * 2) / 2;
    }

    /// <summary>The part kept, with its gain and a short fade at both ends (no click).</summary>
    private (float[] Left, float[] Right) Slice()
    {
        var (from, to) = Part();
        var count = to - from;
        var left = new float[count];
        var right = new float[count];
        var gain = (float)Math.Pow(10, _vm.Gain / 20);
        var fadeIn = Math.Min(count / 4, WavFile.Rate * 4 / 1000);
        var fadeOut = Math.Min(count / 4, WavFile.Rate * 15 / 1000);
        for (var i = 0; i < count; i++)
        {
            var fade = 1f;
            if (i < fadeIn)
                fade = i / (float)fadeIn;
            if (count - 1 - i < fadeOut)
                fade = Math.Min(fade, (count - 1 - i) / (float)fadeOut);
            left[i] = Math.Clamp(_rawLeft[from + i] * gain * fade, -1f, 1f);
            right[i] = Math.Clamp(_rawRight[from + i] * gain * fade, -1f, 1f);
        }
        return (left, right);
    }

    private void Cancel()
    {
        _synth.Preview(([], []));
        _vm.Editing = false;
        _vm.Say("The editor is closed: the sound played stays as it was");
    }

    /// <summary>✓ VALIDATE: the part kept saved (a new file, never one written over) and given to the keys.</summary>
    private void Validate()
    {
        var (left, right) = Slice();
        if (left.Length < WavFile.Rate / 100)
        {
            _vm.Say("The part kept is too short: drag a longer part on the waveform", true);
            return;
        }
        try
        {
            Directory.CreateDirectory(SamplerWindowViewModel.SampleFolder);
            var name = string.Concat(_vm.Name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            if (name.Length == 0)
                name = "SAMPLE";
            var path = Path.Combine(SamplerWindowViewModel.SampleFolder, name + ".wav");
            for (var n = 2; File.Exists(path); n++)
                path = Path.Combine(SamplerWindowViewModel.SampleFolder, $"{name}-{n}.wav");
            WavFile.Write(path, left, right);
            _synth.Preview(([], []));
            _synth.SetSample(left, right);
            _vm.Sample = path;
            _vm.Editing = false;
            _vm.SaveCurrent();
            _vm.Say($"{Path.GetFileName(path)}: {left.Length / (double)WavFile.Rate:0.00} s, played by the keys ({SamplerWindowViewModel.NoteName(_vm.Root)} as recorded)");
        }
        catch (Exception ex)
        {
            _vm.Say("The sound could not be saved: " + ex.Message, true);
        }
    }
}

/// <summary>
/// A waveform: the sound played (seen), or the recording being edited (the part kept between its two edges, dragged;
/// the rest shaded; its gain seen, what would clip in red; the preview running over it).
/// </summary>
public sealed partial class SampleWave : Control
{
    private readonly SamplerWindowViewModel _vm = null!;
    private readonly SamplerSynth _synth = null!;
    private readonly Func<(float[] Left, float[] Right)> _data = null!;
    private float[]? _peaksOf;
    private float[] _peaks = [];
    private int _drag; // 0 none, 1 start, 2 end, 3 a new part
    private double _anchor;

    // Godot builds its scripts with this constructor.
    public SampleWave()
    {
    }

    public SampleWave(SamplerWindowViewModel vm, SamplerSynth synth, Func<(float[] Left, float[] Right)> data)
    {
        _vm = vm;
        _synth = synth;
        _data = data;
        MouseFilter = MouseFilterEnum.Stop;
        Bind.When(this, vm, QueueRedraw, nameof(vm.Start), nameof(vm.End), nameof(vm.Gain));
    }

    public void Reset()
    {
        _peaksOf = null;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_synth.PreviewProgress >= 0 || _synth.Playing)
            QueueRedraw();
    }

    private float[] Peaks(float[] left, float[] right, int buckets)
    {
        if (ReferenceEquals(_peaksOf, left) && _peaks.Length == buckets)
            return _peaks;
        _peaksOf = left;
        _peaks = new float[Math.Max(1, buckets)];
        for (var i = 0; i < left.Length; i++)
        {
            var b = (int)((long)i * _peaks.Length / left.Length);
            _peaks[b] = Math.Max(_peaks[b], Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        }
        return _peaks;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.06f, 0.07f, 0.08f));
        var (left, right) = _data();
        var editing = _vm.Editing;
        if (left.Length == 0)
        {
            DrawString(E.Body, new Vector2(0, Size.Y / 2 + 6), editing ? "An empty recording" : "No sound yet: ● REC records one with the microphone, 📂 opens a file",
                HorizontalAlignment.Center, Size.X, 15, E.Muted);
            return;
        }
        var peaks = Peaks(left, right, (int)Size.X);
        var mid = Size.Y / 2;
        var gain = editing ? (float)Math.Pow(10, _vm.Gain / 20) : 1f;
        var startX = editing ? (float)(_vm.Start * Size.X) : 0;
        var endX = editing ? (float)(_vm.End * Size.X) : Size.X;
        for (var x = 0; x < peaks.Length; x++)
        {
            var inside = x >= startX && x <= endX;
            var value = peaks[x] * (inside ? gain : 1);
            var h = Math.Min(value, 1f) * (mid - 6);
            var colour = !inside ? new Color(0.35f, 0.38f, 0.42f) : value > 1 ? E.Red : editing ? E.Amber : E.Cyan;
            DrawLine(new Vector2(x + 0.5f, mid - h), new Vector2(x + 0.5f, mid + h), colour, 1);
        }
        DrawLine(new Vector2(0, mid), new Vector2(Size.X, mid), new Color(1, 1, 1, 0.1f), 1);
        if (editing)
        {
            // Outside the part kept, shaded; its edges, with grips.
            DrawRect(new Rect2(0, 0, startX, Size.Y), new Color(0, 0, 0, 0.45f));
            DrawRect(new Rect2(endX, 0, Size.X - endX, Size.Y), new Color(0, 0, 0, 0.45f));
            foreach (var x in new[] { startX, endX })
            {
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), E.TextBright, 2);
                DrawRect(new Rect2(x - 5, 0, 10, 14), E.TextBright);
                DrawRect(new Rect2(x - 5, Size.Y - 14, 10, 14), E.TextBright);
            }
            if (_synth.PreviewProgress is var progress and >= 0)
            {
                var x = startX + (float)progress * (endX - startX);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), E.Green, 1.5f);
            }
            DrawString(E.Body, new Vector2(8, 18), "EDITOR · drag the edges (or a new part), double click: all", HorizontalAlignment.Left, -1, 12, E.Muted);
        }
        else
        {
            DrawString(E.Body, new Vector2(8, 18), $"{_vm.Name} · {left.Length / (double)WavFile.Rate:0.00} s · {SamplerWindowViewModel.NoteName(_vm.Root)} plays it as recorded · {_vm.Mode}",
                HorizontalAlignment.Left, -1, 12, E.Muted);
            foreach (var position in _synth.Positions())
            {
                var x = (float)(position * Size.X);
                DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), E.Green with { A = 0.7f }, 1);
            }
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (!_vm.Editing)
            return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }:
                _vm.Start = 0;
                _vm.End = 1;
                _drag = 0;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                AcceptEvent();
                if (!button.Pressed)
                {
                    _drag = 0;
                    return;
                }
                var x = button.Position.X;
                var clicked = Math.Clamp(x / Size.X, 0, 1);
                if (Math.Abs(x - _vm.Start * Size.X) < 8)
                    _drag = 1;
                else if (Math.Abs(x - _vm.End * Size.X) < 8)
                    _drag = 2;
                else
                {
                    _drag = 3;
                    _anchor = clicked;
                    _vm.Start = clicked;
                    _vm.End = clicked;
                }
                break;
            case InputEventMouseMotion motion when _drag != 0:
            {
                var at = Math.Clamp(motion.Position.X / Size.X, 0, 1);
                if (_drag == 1)
                    _vm.Start = Math.Min(at, _vm.End);
                else if (_drag == 2)
                    _vm.End = Math.Max(at, _vm.Start);
                else
                {
                    _vm.Start = Math.Min(_anchor, at);
                    _vm.End = Math.Max(_anchor, at);
                }
                AcceptEvent();
                break;
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            Reset();
    }
}

/// <summary>
/// The sound of the sampler: every key a voice reading the sound faster or slower (a semitone, ×2^(1/12)), a short
/// attack, the release when the key is up (Hold) or to the end (One-shot), the pitch bend; the preview of the editor
/// as it was recorded.
/// </summary>
public sealed partial class SamplerSynth : Node, IPlayable
{
    private const int Rate = WavFile.Rate;
    private readonly SamplerWindowViewModel _vm = null!;
    private readonly AudioStreamPlayer _player = new();
    private AudioStreamGeneratorPlayback? _playback;
    private readonly List<Voice> _voices = [];
    private readonly object _gate = new();

    public float[] Left { get; private set; } = [];
    public float[] Right { get; private set; } = [];

    private sealed class Voice
    {
        public float[] Left = [];
        public float[] Right = [];
        public int Note;
        public double Position;
        public double Step = 1;
        public double Velocity = 1;
        public bool Held = true;
        public bool Releasing;
        public bool OneShot;
        public bool Preview;
        public double Attack;
        public double Fade = 1;
        public bool Done;
    }

    // Godot builds its scripts with this constructor.
    public SamplerSynth()
    {
    }

    public SamplerSynth(SamplerWindowViewModel vm)
    {
        _vm = vm;
        _player.Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.08f };
        AddChild(_player);
        _vm.Keys.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.Keys.Sustain) && !_vm.Keys.Sustain)
                lock (_gate)
                    foreach (var voice in _voices.Where(v => !v.Held && !v.OneShot && !v.Preview))
                        voice.Releasing = true;
        };
    }

    public override void _Ready()
    {
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
    }

    public void SetSample(float[] left, float[] right)
    {
        lock (_gate)
        {
            Left = left;
            Right = right;
        }
    }

    public void NoteOn(int note, double velocity = 0.8) => Start(note, velocity, _vm.Mode == "One-shot");

    private void Start(int note, double velocity, bool oneShot)
    {
        lock (_gate)
        {
            if (Left.Length == 0)
                return;
            foreach (var old in _voices.Where(v => v.Note == note && !v.Preview))
                old.Releasing = true;
            _voices.Add(new Voice
            {
                Left = Left, Right = Right, Note = note, Velocity = 0.3 + 0.7 * velocity, OneShot = oneShot,
                Step = Math.Pow(2, (note - _vm.Root) / 12.0)
            });
            if (_voices.Count > 24)
                _voices.RemoveAt(0);
        }
    }

    public void NoteOff(int note)
    {
        lock (_gate)
            foreach (var voice in _voices.Where(v => v.Note == note && v.Held && !v.Preview))
            {
                voice.Held = false;
                if (!voice.OneShot && !_vm.Keys.Sustain)
                    voice.Releasing = true;
            }
    }

    /// <summary>The pads of a MIDI keyboard: the bottom row from the root up, the top row the octave above it (one-shot).</summary>
    public void Hit(int pad, double velocity) => Start(_vm.Root + (pad >= 8 ? pad - 8 : pad + 8), velocity, true);

    /// <summary>The preview of the editor: the part kept, as recorded (an empty sound stops it).</summary>
    public void Preview((float[] Left, float[] Right) sound)
    {
        lock (_gate)
        {
            _voices.RemoveAll(v => v.Preview);
            if (sound.Left.Length > 0)
                _voices.Add(new Voice { Left = sound.Left, Right = sound.Right, Preview = true, OneShot = true, Velocity = 1, Note = -1 });
        }
    }

    /// <summary>Where the preview is (0 to 1), or -1.</summary>
    public double PreviewProgress
    {
        get
        {
            lock (_gate)
                return _voices.FirstOrDefault(v => v.Preview) is { } preview ? preview.Position / Math.Max(1, preview.Left.Length) : -1;
        }
    }

    public bool Playing
    {
        get
        {
            lock (_gate)
                return _voices.Count > 0;
        }
    }

    /// <summary>Where the notes playing the sound are in it (0 to 1).</summary>
    public IReadOnlyList<double> Positions()
    {
        lock (_gate)
            return [.. _voices.Where(v => !v.Preview && ReferenceEquals(v.Left, Left)).Select(v => v.Position / Math.Max(1, v.Left.Length))];
    }

    /// <summary>The sound as it is up to 0.8, gently held under 1 above (many notes at once do not clip).</summary>
    private static float Soft(double x) =>
        (float)(Math.Abs(x) < 0.8 ? x : Math.Sign(x) * (0.8 + 0.2 * Math.Tanh((Math.Abs(x) - 0.8) / 0.2)));

    public override void _Process(double delta)
    {
        if (_playback is null)
            return;
        var frames = _playback.GetFramesAvailable();
        if (frames <= 0)
            return;
        var buffer = new Vector2[frames];
        var gain = _vm.Keys.Volume / 100.0;
        var bend = Math.Pow(2, _vm.Keys.PitchBend * 2 / 12);
        var fade = Math.Exp(-1 / ((0.01 + _vm.Release / 100.0 * 2.5) * Rate));
        var attack = 1 / (0.003 * Rate);
        lock (_gate)
        {
            for (var i = 0; i < frames; i++)
            {
                double l = 0, r = 0;
                foreach (var v in _voices)
                {
                    if (v.Done)
                        continue;
                    var at = (int)v.Position;
                    if (at + 1 >= v.Left.Length)
                    {
                        v.Done = true;
                        continue;
                    }
                    var frac = v.Position - at;
                    v.Attack = Math.Min(1, v.Attack + attack);
                    if (v.Releasing)
                    {
                        v.Fade *= fade;
                        if (v.Fade < 1e-4)
                        {
                            v.Done = true;
                            continue;
                        }
                    }
                    var amp = v.Attack * v.Fade * v.Velocity;
                    l += (v.Left[at] + (v.Left[at + 1] - v.Left[at]) * frac) * amp;
                    r += (v.Right[at] + (v.Right[at + 1] - v.Right[at]) * frac) * amp;
                    v.Position += v.Preview ? 1 : v.Step * bend;
                }
                buffer[i] = new Vector2(Soft(l * gain), Soft(r * gain));
            }
            _voices.RemoveAll(v => v.Done);
        }
        _playback.PushBuffer(buffer);
    }
}
