using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Media;

/// <summary>
/// /synth: the keyboard of /piano playing an FM synth of six operators, after the Yamaha DX7, every value of it at hand:
/// the algorithm (32, drawn: the carriers below, the modulators above them, the feedback loop; a click on an operator
/// turns it off or on), the feedback, the transpose, the bend range; for each operator its frequency (ratio: coarse,
/// fine, detune; or fixed in Hz), its output level, its velocity and amplitude-modulation sensitivities, its rate
/// scaling, its envelope (R1–R4, L1–L4, drawn), its level scaling (break point, depths, curves); the LFO (wave, speed,
/// delay, pitch and amplitude depths, key sync, pitch sensitivity) and the pitch envelope. Sounds given (E.PIANO,
/// TUBULAR BELL, BRASS, SOLID BASS, DRAWBAR ORGAN, STRINGS, MARIMBA, SYNC LEAD, INIT VOICE), sounds saved. The knobs of a
/// MIDI keyboard: the levels of OP1 to OP6, the feedback, the algorithm.
/// </summary>
[ToolView(typeof(SynthWindowViewModel), "Synth", "🎛", 1560, 820)]
public sealed class SynthView : IToolView
{
    private static readonly Color Panel = new(0.17f, 0.12f, 0.11f);
    private static readonly Color Ink = new(0.93f, 0.89f, 0.84f);
    private static readonly Color Dim = new(0.66f, 0.6f, 0.55f);
    internal static readonly Color Carrier = new(0.36f, 0.82f, 0.86f);
    internal static readonly Color Modulator = new(0.98f, 0.66f, 0.32f);

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (SynthWindowViewModel)viewModel;
        var patch = vm.Patch;
        var synth = new FmSynth(patch, vm.Keys);
        var keyboard = new PianoKeyboard(vm.Keys, synth) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(600, 170) };
        keyboard.Knobs = (knob, value) =>
        {
            if (knob < 6)
                patch.Operators[knob].Level = value * 99 / 127;
            else if (knob == 6)
                patch.Feedback = value * 7 / 127;
            else
                patch.Algorithm = 1 + value * 31 / 127;
        };

        // The sound being made saved a second after its last change.
        var save = new Timer { WaitTime = 1, OneShot = true };
        save.Timeout += vm.SaveCurrent;
        void Changed(object? _, PropertyChangedEventArgs __) => save.Start();
        patch.PropertyChanged += Changed;
        foreach (var op in patch.Operators)
            op.PropertyChanged += Changed;
        save.TreeExiting += () =>
        {
            patch.PropertyChanged -= Changed;
            foreach (var op in patch.Operators)
                op.PropertyChanged -= Changed;
            vm.SaveCurrent();
        };

        // --synth-render=/path/x.wav (tests): every sound given played (a phrase and a chord) into a WAV file.
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--synth-render=", StringComparison.Ordinal)) is { } test)
            FmSynth.RenderPresets(test["--synth-render=".Length..].Trim('"'));

        var page = Ui.Column(8, TopBar(vm), Middle(vm), LfoRow(patch), KeyboardRow(vm, keyboard), StatusRow(vm));
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        page.AddChild(synth);
        page.AddChild(save);
        return page;
    }

    private static FmNumber Num(INotifyPropertyChanged source, string name, int min, int max, string tooltip, int width = 44) =>
        new(source, name, min, max) { TooltipText = tooltip + " · drag up or down, the wheel, or a double click to type", CustomMinimumSize = new Vector2(width, 24) };

    private static OptionButton Pick(INotifyPropertyChanged source, string name, string[] items, string tooltip, int width = 0)
    {
        var pick = Bind.Options(new OptionButton { TooltipText = tooltip, CustomMinimumSize = new Vector2(width, 0) }, items.ToList(), source, name);
        pick.AddThemeFontSizeOverride("font_size", 12);
        return pick;
    }

    private static Label Caption(string text, Color? colour = null, int size = 11) => E.Text_(text, colour ?? Dim, size);

    private static Control TopBar(SynthWindowViewModel vm)
    {
        var patch = vm.Patch;
        var name = Bind.Edit(new LineEdit { CustomMinimumSize = new Vector2(190, 0), TooltipText = "The name of the sound (💾 SAVE keeps it under this name)" }, patch, nameof(patch.Name));
        var sounds = new OptionButton { CustomMinimumSize = new Vector2(200, 0), TooltipText = "The sounds given and the sounds saved (💾)" };
        void Fill()
        {
            sounds.Clear();
            foreach (var sound in vm.Sounds)
                sounds.AddItem(sound);
            sounds.Selected = vm.Sounds.IndexOf(vm.Chosen);
        }
        Fill();
        vm.SoundsChanged += Fill;
        sounds.TreeExiting += () => vm.SoundsChanged -= Fill;
        Bind.When(sounds, vm, () => sounds.Selected = vm.Sounds.IndexOf(vm.Chosen), nameof(vm.Chosen));
        sounds.ItemSelected += index => vm.Chosen = vm.Sounds[(int)index];

        var algorithm = Num(patch, nameof(patch.Algorithm), 1, 32, "The algorithm (1 to 32): which operator modulates which");
        var midi = E.Text_("", E.Muted, 12);
        Bind.When(midi, vm.Keys, () =>
        {
            midi.Text = vm.Keys.HasMidi ? "🎹 MIDI" : "no MIDI keyboard";
            midi.TooltipText = vm.Keys.HasMidi ? vm.Keys.Midi + "\nIts knobs: the levels of OP1 to OP6, the feedback, the algorithm" : vm.Keys.Midi;
            midi.AddThemeColorOverride("font_color", vm.Keys.HasMidi ? E.Green : E.Muted);
        }, nameof(vm.Keys.Midi), nameof(vm.Keys.HasMidi));
        midi.ClipText = true;
        midi.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        midi.HorizontalAlignment = HorizontalAlignment.Right;
        var volume = Bind.Range(new HSlider { MinValue = 0, MaxValue = 100, Step = 1, CustomMinimumSize = new Vector2(110, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "The volume" }, vm.Keys, nameof(vm.Keys.Volume));
        return Ui.Toolbar(Ui.Row(8,
            E.Caption("FM SYNTH", E.TextBright, 14), name, sounds,
            Ui.Action("💾 SAVE", vm.Save, tooltip: "Save the sound under its name (a sound of the same name is kept aside, dated)"),
            Ui.Action("INIT", vm.Init, tooltip: "INIT VOICE: one sine wave, to begin a sound from nothing"),
            E.Text_("Algorithm", E.Muted, 12),
            Ui.Action("‹", () => patch.Algorithm = patch.Algorithm <= 1 ? 32 : patch.Algorithm - 1, tooltip: "The algorithm before"),
            algorithm,
            Ui.Action("›", () => patch.Algorithm = patch.Algorithm >= 32 ? 1 : patch.Algorithm + 1, tooltip: "The algorithm after"),
            E.Text_("Feedback", E.Muted, 12), Num(patch, nameof(patch.Feedback), 0, 7, "The feedback (0 to 7) of the operator fed by its own output"),
            E.Text_("Transpose", E.Muted, 12), Num(patch, nameof(patch.Transpose), -24, 24, "Transpose, in semitones"),
            E.Text_("Bend", E.Muted, 12), Num(patch, nameof(patch.BendRange), 0, 12, "The pitch bend range, in semitones"),
            E.Text_("Volume", E.Muted, 12), volume,
            midi));
    }

    private static Control Middle(SynthWindowViewModel vm)
    {
        var patch = vm.Patch;
        var algorithm = new FmAlgorithmView(patch) { CustomMinimumSize = new Vector2(250, 300), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        var left = Ui.Column(4, Caption("ALGORITHM (a click turns an operator off or on)"), algorithm);

        var grid = new GridContainer { Columns = 7, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 5);
        (string Label, Func<FmOperator, int, Control> Cell)[] rows =
        [
            ("", (op, i) =>
            {
                var title = E.Text_($"OP{i + 1}", Ink, 15);
                Bind.When(title, patch, () => title.AddThemeColorOverride("font_color", FmAlgorithms.Get(patch.Algorithm).Carrier[i] ? Carrier : Modulator), nameof(patch.Algorithm));
                var role = Caption("");
                Bind.When(role, patch, () => role.Text = FmAlgorithms.Get(patch.Algorithm).Carrier[i] ? "carrier" : "modulator", nameof(patch.Algorithm));
                return Ui.Row(6, title, Ui.Check("ON", op, nameof(op.On), "The operator on or off"), role);
            }),
            ("Mode · coarse", (op, _) => Ui.Row(4, Pick(op, nameof(op.Mode), FmOperator.Modes, "Ratio: the frequency follows the note; Fixed: a frequency in Hz", 84),
                Num(op, nameof(op.Coarse), 0, 31, "Coarse: the ratio (0 = ×0.5, 1 to 31); fixed: 0 to 3 for 1, 10, 100, 1000 Hz"))),
            ("Fine · detune", (op, _) => Ui.Row(4, Num(op, nameof(op.Fine), 0, 99, "Fine: up to ×2 the coarse ratio (fixed: up to ×10)"),
                Num(op, nameof(op.Detune), -7, 7, "Detune: a few cents (-7 to 7), for chorus between operators"))),
            ("Frequency", (op, _) =>
            {
                var frequency = E.Text_("", Ink, 13);
                Bind.When(frequency, op, () => frequency.Text = op.FrequencyText, nameof(op.Mode), nameof(op.Coarse), nameof(op.Fine), nameof(op.Detune));
                return frequency;
            }),
            ("Level · velocity", (op, _) => Ui.Row(4, Num(op, nameof(op.Level), 0, 99, "Output level (0 to 99): loudness for a carrier, brightness (modulation index) for a modulator"),
                Num(op, nameof(op.Velocity), 0, 7, "Velocity sensitivity (0 to 7): softer played, quieter (or less bright)"))),
            ("AM sens · rate scale", (op, _) => Ui.Row(4, Num(op, nameof(op.AmpMod), 0, 3, "Amplitude-modulation sensitivity (0 to 3): how much the LFO (AMD) moves its level"),
                Num(op, nameof(op.RateScale), 0, 7, "Rate scaling (0 to 7): its envelope faster up the keyboard"))),
            ("Rates R1–R4", (op, _) => Ui.Row(2, Num(op, nameof(op.R1), 0, 99, "R1: the speed of the attack (towards L1)"), Num(op, nameof(op.R2), 0, 99, "R2: towards L2"),
                Num(op, nameof(op.R3), 0, 99, "R3: towards L3 (held while the key is down)"), Num(op, nameof(op.R4), 0, 99, "R4: the release (towards L4)"))),
            ("Levels L1–L4", (op, _) => Ui.Row(2, Num(op, nameof(op.L1), 0, 99, "L1: the level reached by the attack"), Num(op, nameof(op.L2), 0, 99, "L2"),
                Num(op, nameof(op.L3), 0, 99, "L3: the sustain level"), Num(op, nameof(op.L4), 0, 99, "L4: where it starts and comes back once released"))),
            ("Envelope", (op, _) => new FmEnvelopeView(op, i => op.Rate(i), i => op.LevelAt(i), false) { CustomMinimumSize = new Vector2(180, 46) }),
            ("Break · L · R depth", (op, _) => Ui.Row(2, Num(op, nameof(op.BreakPoint), 0, 127, "Level scaling: the break point (a MIDI note: 60 = C4)"),
                Num(op, nameof(op.LeftDepth), 0, 99, "Level scaling: depth below the break point"), Num(op, nameof(op.RightDepth), 0, 99, "Level scaling: depth above the break point"))),
            ("Curves L · R", (op, _) => Ui.Row(4, Pick(op, nameof(op.LeftCurve), FmOperator.Curves, "Level scaling below the break point: - lowers, + raises; linear or exponential", 80),
                Pick(op, nameof(op.RightCurve), FmOperator.Curves, "Level scaling above the break point", 80)))
        ];
        foreach (var (label, cell) in rows)
        {
            var caption = Caption(label.ToUpperInvariant(), Dim, 10);
            caption.CustomMinimumSize = new Vector2(104, 0);
            caption.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            grid.AddChild(caption);
            for (var i = 0; i < 6; i++)
            {
                var control = cell(patch.Operators[i], i);
                if (control is Godot.Container or Label)
                    control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                grid.AddChild(control);
            }
        }
        var ops = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ops.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Panel, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 });
        var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(grid);
        ops.AddChild(scroll);
        return Ui.Row(12, left, ops);
    }

    private static Control LfoRow(FmPatch patch) => Ui.Toolbar(Ui.Row(8,
        E.Caption("LFO", E.TextBright, 12),
        Pick(patch, nameof(patch.LfoWave), FmPatch.Waves, "The wave of the LFO", 110),
        Caption("Speed"), Num(patch, nameof(patch.LfoSpeed), 0, 99, "The speed of the LFO (0 to 99: from a slow sweep to a fast flutter)"),
        Caption("Delay"), Num(patch, nameof(patch.LfoDelay), 0, 99, "The LFO coming in after the note begins (0 to 99: up to 4 s)"),
        Caption("PMD"), Num(patch, nameof(patch.Pmd), 0, 99, "Pitch modulation depth: the vibrato (the MOD strip or the wheel adds to it)"),
        Caption("AMD"), Num(patch, nameof(patch.Amd), 0, 99, "Amplitude modulation depth: the tremolo of the operators with an AM sensitivity"),
        Caption("PMS"), Num(patch, nameof(patch.Pms), 0, 7, "Pitch modulation sensitivity (0 to 7)"),
        Ui.Check("Key sync", patch, nameof(patch.LfoSync), "The LFO starts again at each note played alone"),
        Ui.Spring(),
        E.Caption("PITCH EG", E.TextBright, 12),
        Caption("R"), Num(patch, nameof(patch.Pr1), 0, 99, "Pitch envelope R1"), Num(patch, nameof(patch.Pr2), 0, 99, "Pitch envelope R2"),
        Num(patch, nameof(patch.Pr3), 0, 99, "Pitch envelope R3"), Num(patch, nameof(patch.Pr4), 0, 99, "Pitch envelope R4 (release)"),
        Caption("L"), Num(patch, nameof(patch.Pl1), 0, 99, "Pitch envelope L1 (50: the note; 0 and 99: four octaves down or up)", 54),
        Num(patch, nameof(patch.Pl2), 0, 99, "Pitch envelope L2"), Num(patch, nameof(patch.Pl3), 0, 99, "Pitch envelope L3 (held)"),
        Num(patch, nameof(patch.Pl4), 0, 99, "Pitch envelope L4 (start and release)"),
        new FmEnvelopeView(patch, i => patch.PitchRate(i), i => patch.PitchLevel(i), true) { CustomMinimumSize = new Vector2(150, 40) }));

    private static Control KeyboardRow(SynthWindowViewModel vm, PianoKeyboard keyboard)
    {
        var keys = vm.Keys;
        var octave = E.Text_("", E.TextBright, 13);
        octave.HorizontalAlignment = HorizontalAlignment.Center;
        Bind.When(octave, keys, () => octave.Text = $"C{keys.Octave} – C{keys.Octave + 3}", nameof(keys.Octave));
        var side = Ui.Column(6,
            Ui.Row(4, Ui.Action("OCT −", () => keys.Octave = Math.Max(1, keys.Octave - 1), tooltip: "The keyboard an octave lower"),
                Ui.Action("OCT +", () => keys.Octave = Math.Min(6, keys.Octave + 1), tooltip: "The keyboard an octave higher")),
            octave,
            Ui.Check("HOLD", keys, nameof(keys.Sustain), "The sustain pedal (or hold the space bar)"));
        side.CustomMinimumSize = new Vector2(130, 0);
        var row = Ui.Row(10, side, keyboard);
        row.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return row;
    }

    private static Control StatusRow(SynthWindowViewModel vm)
    {
        var played = Ui.Label(vm.Keys, nameof(vm.Keys.Played), E.Muted, 12);
        played.ClipText = true;
        played.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var status = Ui.Label(vm, nameof(vm.Status), E.Green, 12);
        return Ui.Row(10, played, status);
    }
}

/// <summary>
/// A number of the synth, small enough for six operators side by side: dragged up or down (faster with Shift), the
/// wheel, the arrow keys once clicked, a double click to type it.
/// </summary>
public sealed partial class FmNumber : Control
{
    private readonly INotifyPropertyChanged _source = null!;
    private readonly string _name = "";
    private readonly int _min;
    private readonly int _max;
    private float _dragY;
    private int _dragStart;
    private bool _dragging;
    private LineEdit? _edit;

    // Godot builds its scripts with this constructor.
    public FmNumber()
    {
    }

    public FmNumber(INotifyPropertyChanged source, string name, int min, int max)
    {
        _source = source;
        _name = name;
        _min = min;
        _max = max;
        FocusMode = FocusModeEnum.Click;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Vsize;
        Bind.When(this, source, QueueRedraw, name);
    }

    private int Value
    {
        get => Bind.Get(_source, _name) is IConvertible v ? v.ToInt32(System.Globalization.CultureInfo.InvariantCulture) : 0;
        set => Bind.Set(_source, _name, Math.Clamp(value, _min, _max));
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, HasFocus() ? new Color(0.13f, 0.16f, 0.13f) : new Color(0.07f, 0.08f, 0.07f));
        DrawRect(rect, HasFocus() ? new Color(0.55f, 0.95f, 0.6f) : new Color(0.33f, 0.29f, 0.25f), false, 1);
        // A thin bar: where the value is in its range.
        var part = (Value - _min) / (float)Math.Max(1, _max - _min);
        DrawRect(new Rect2(2, Size.Y - 4, (Size.X - 4) * part, 2), new Color(0.98f, 0.66f, 0.32f, 0.8f));
        DrawString(E.Mono, new Vector2(0, Size.Y / 2 + 5), Value.ToString(), HorizontalAlignment.Center, Size.X, 13, new Color(0.55f, 0.95f, 0.6f));
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }:
                Type();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                _dragY = button.Position.Y;
                _dragStart = Value;
                if (button.Pressed)
                    GrabFocus();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                Value += wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                var range = _max - _min;
                var per = motion.ShiftPressed ? 1f : Math.Max(1f, range / 99f) * 0.25f;
                Value = _dragStart + (int)Math.Round((_dragY - motion.Position.Y) * per * (range > 20 ? 1 : 0.15f));
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true } key when key.Keycode is Key.Up or Key.Down or Key.Right or Key.Left:
                Value += key.Keycode is Key.Up or Key.Right ? 1 : -1;
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter }:
                Type();
                AcceptEvent();
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationFocusEnter or (int)NotificationFocusExit)
            QueueRedraw();
    }

    /// <summary>A field over the number to type it (Enter or leaving it keeps it, Escape forgets it).</summary>
    private void Type()
    {
        if (_edit is not null)
            return;
        _edit = new LineEdit { Text = Value.ToString(), Size = Size, SelectAllOnFocus = true, Alignment = HorizontalAlignment.Center };
        _edit.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_edit);
        _edit.GrabFocus();
        _edit.SelectAll();
        void Done(bool keep)
        {
            if (_edit is null)
                return;
            if (keep && int.TryParse(_edit.Text.Trim(), out var typed))
                Value = typed;
            var edit = _edit;
            _edit = null;
            edit.QueueFree();
        }
        _edit.TextSubmitted += _ => Done(true);
        _edit.FocusExited += () => Done(true);
        _edit.GuiInput += input =>
        {
            if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
                Done(false);
        };
    }
}

/// <summary>The algorithm drawn: the carriers on the output line, each modulator above what it modulates, the feedback loop.</summary>
public sealed partial class FmAlgorithmView : Control
{
    private readonly FmPatch _patch = null!;

    // Godot builds its scripts with this constructor.
    public FmAlgorithmView()
    {
    }

    public FmAlgorithmView(FmPatch patch)
    {
        _patch = patch;
        MouseFilter = MouseFilterEnum.Stop;
        void Redraw(object? _, PropertyChangedEventArgs __) => QueueRedraw();
        patch.PropertyChanged += Redraw;
        foreach (var op in patch.Operators)
            op.PropertyChanged += Redraw;
        TreeExiting += () =>
        {
            patch.PropertyChanged -= Redraw;
            foreach (var op in patch.Operators)
                op.PropertyChanged -= Redraw;
        };
    }

    private Vector2[] Places()
    {
        var algorithm = FmAlgorithms.Get(_patch.Algorithm);
        var depth = new int[6];
        var x = new float[6];
        var carriers = 0;
        for (var k = 0; k < 6; k++)
        {
            var targets = algorithm.Targets(k).ToList();
            depth[k] = algorithm.Carrier[k] ? 0 : 1 + targets.Select(t => depth[t]).DefaultIfEmpty(0).Max();
            x[k] = algorithm.Carrier[k] ? carriers++ : targets.Count > 0 ? targets.Average(t => x[t]) : 0;
        }
        // In each row, the operators set apart.
        foreach (var row in Enumerable.Range(0, 6).GroupBy(k => depth[k]))
        {
            var placed = row.OrderBy(k => x[k]).ThenBy(k => k).ToList();
            for (var j = 1; j < placed.Count; j++)
                x[placed[j]] = Math.Max(x[placed[j]], x[placed[j - 1]] + 1);
        }
        var minX = x.Min();
        var maxX = Math.Max(x.Max() - minX, 1);
        var maxDepth = Math.Max(depth.Max(), 1);
        var places = new Vector2[6];
        for (var k = 0; k < 6; k++)
            places[k] = new Vector2(30 + (x[k] - minX) / maxX * (Size.X - 70), Size.Y - 50 - depth[k] * (Size.Y - 110) / maxDepth);
        if (x.Max() - minX < 0.5f)
            for (var k = 0; k < 6; k++)
                places[k].X = Size.X / 2;
        return places;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return;
        var places = Places();
        for (var k = 0; k < 6; k++)
            if (click.Position.DistanceTo(places[k]) < 18)
            {
                _patch.Operators[k].On = !_patch.Operators[k].On;
                AcceptEvent();
                return;
            }
    }

    public override void _Draw()
    {
        var algorithm = FmAlgorithms.Get(_patch.Algorithm);
        var places = Places();
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.06f, 0.07f, 0.06f));
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.3f, 0.26f, 0.22f), false, 1);
        DrawString(E.Mono, new Vector2(10, 22), $"ALG {algorithm.Number:00}", HorizontalAlignment.Left, -1, 16, new Color(0.55f, 0.95f, 0.6f));
        DrawString(E.Mono, new Vector2(Size.X - 80, 22), $"FB {_patch.Feedback}", HorizontalAlignment.Left, -1, 13, new Color(0.55f, 0.95f, 0.6f));
        var line = new Color(0.7f, 0.68f, 0.62f);
        // The output line, under the carriers.
        var outY = Size.Y - 20;
        var carriers = Enumerable.Range(0, 6).Where(k => algorithm.Carrier[k]).ToList();
        DrawLine(new Vector2(carriers.Min(k => places[k].X), outY), new Vector2(carriers.Max(k => places[k].X), outY), line, 2);
        foreach (var k in carriers)
            DrawLine(places[k] + new Vector2(0, 14), new Vector2(places[k].X, outY), line, 2);
        for (var k = 0; k < 6; k++)
            foreach (var m in algorithm.ModulatedBy[k])
                DrawLine(places[m] + new Vector2(0, 14), places[k] - new Vector2(0, 14), line, 2, true);
        // The feedback: from the output of an operator back into the top of another (or itself).
        var from = places[algorithm.FeedbackFrom];
        var to = places[algorithm.FeedbackTo];
        var fb = _patch.Feedback > 0 ? new Color(0.98f, 0.4f, 0.35f) : line with { A = 0.4f };
        var side = Math.Max(from.X, to.X) + 26;
        Vector2[] loop = [from + new Vector2(14, 6), new Vector2(side, from.Y + 6), new Vector2(side, to.Y - 22), new Vector2(to.X, to.Y - 22), to - new Vector2(0, 14)];
        DrawPolyline(loop, fb, 2, true);
        for (var k = 0; k < 6; k++)
        {
            var on = _patch.Operators[k].On;
            var colour = algorithm.Carrier[k] ? SynthView.Carrier : SynthView.Modulator;
            var rect = new Rect2(places[k] - new Vector2(16, 14), new Vector2(32, 28));
            DrawRect(rect, on ? colour : new Color(0.25f, 0.25f, 0.25f));
            DrawRect(rect, Colors.Black, false, 1);
            DrawString(E.Body, new Vector2(rect.Position.X, rect.Position.Y + 20), (k + 1).ToString(), HorizontalAlignment.Center, rect.Size.X, 16,
                on ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.55f, 0.55f, 0.55f));
        }
        DrawString(E.Body, new Vector2(10, Size.Y - 4), "OUT", HorizontalAlignment.Left, -1, 10, line);
    }
}

/// <summary>An envelope drawn: from L4 to L1, L2, L3 (held), back to L4, each segment as long as its rate makes it.</summary>
public sealed partial class FmEnvelopeView : Control
{
    private readonly Func<int, int> _rate = null!;
    private readonly Func<int, int> _level = null!;
    private readonly bool _pitch;

    // Godot builds its scripts with this constructor.
    public FmEnvelopeView()
    {
    }

    public FmEnvelopeView(INotifyPropertyChanged source, Func<int, int> rate, Func<int, int> level, bool pitch)
    {
        _rate = rate;
        _level = level;
        _pitch = pitch;
        MouseFilter = MouseFilterEnum.Ignore;
        void Redraw(object? _, PropertyChangedEventArgs __) => QueueRedraw();
        source.PropertyChanged += Redraw;
        TreeExiting += () => source.PropertyChanged -= Redraw;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.06f, 0.07f, 0.06f));
        if (_pitch)
            DrawLine(new Vector2(0, Size.Y / 2), new Vector2(Size.X, Size.Y / 2), new Color(0.3f, 0.35f, 0.3f), 1);
        // Each segment's time, as the synth makes it (level units at the rate's speed), squeezed so that a slow one stays in view.
        var levels = new[] { _level(3), _level(0), _level(1), _level(2), _level(2), _level(3) };
        var times = new double[5];
        for (var i = 0; i < 5; i++)
            times[i] = i == 3 ? 0.6 : Math.Sqrt(Math.Abs(levels[i + 1] - levels[i]) / FmSynth.Speed(_rate(i < 3 ? i : 3)) + 0.002);
        var total = times.Sum();
        var points = new Vector2[6];
        var x = 2.0;
        for (var i = 0; i < 6; i++)
        {
            points[i] = new Vector2((float)x, Size.Y - 3 - levels[i] / 99f * (Size.Y - 6));
            if (i < 5)
                x += times[i] / total * (Size.X - 4);
        }
        DrawPolyline(points, new Color(0.55f, 0.95f, 0.6f), 1.5f, true);
        DrawLine(new Vector2(points[3].X, 0), new Vector2(points[3].X, Size.Y), new Color(0.3f, 0.35f, 0.3f), 1);
        DrawLine(new Vector2(points[4].X, 0), new Vector2(points[4].X, Size.Y), new Color(0.3f, 0.35f, 0.3f), 1);
    }
}

/// <summary>
/// The sound of /synth: six operators per note (sine waves read from a table), set together by the algorithm of the
/// patch, each with its envelope (in level units: 0.75 dB a step, like the DX7), the feedback, the LFO (vibrato, tremolo),
/// the pitch envelope, the pitch bend; sixteen notes at once, a soft limit.
/// </summary>
public sealed partial class FmSynth : Node, IPlayable
{
    private const int Rate = 44100;
    private const int ControlEvery = 16;
    private const int TableSize = 4096;
    private const double ModIndex = Math.Tau * 1.6;
    private static readonly float[] Table = BuildTable();
    private static readonly double[] PmsSemitones = [0, 0.08, 0.16, 0.3, 0.5, 0.9, 1.8, 3.6];

    private readonly FmPatch _patch = null!;
    private readonly PianoWindowViewModel _keys = null!;
    private readonly AudioStreamPlayer _player = new();
    private AudioStreamGeneratorPlayback? _playback;
    private readonly List<Voice> _voices = [];
    private readonly object _gate = new();
    private double _lfoPhase;
    private double _lfo;
    private double _sampleHold;
    private uint _noise = 0x9E3779B9;
    private int _tick;

    private sealed class Voice
    {
        public int Note;
        public double Velocity;
        public bool Held = true;
        public bool Released;
        public bool Done;
        public double Time;
        public readonly double[] Phase = new double[6];
        public readonly double[] Env = new double[6];
        public readonly int[] Stage = new int[6];
        public readonly double[] Out = new double[6];
        public readonly double[] Amp = new double[6];
        /// <summary>How much the level moves each sample until the next control tick (no zipper noise, no click).</summary>
        public readonly double[] AmpStep = new double[6];
        public readonly double[] Increment = new double[6];
        public double FeedbackBefore;
        public double PitchEnv;
        public int PitchStage;
    }

    // Godot builds its scripts with this constructor.
    public FmSynth()
    {
    }

    public FmSynth(FmPatch patch, PianoWindowViewModel keys)
    {
        _patch = patch;
        _keys = keys;
        _player.Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.08f };
        AddChild(_player);
        _keys.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_keys.Sustain) && !_keys.Sustain)
                lock (_gate)
                    foreach (var voice in _voices.Where(v => !v.Held && !v.Released))
                        Release(voice);
        };
    }

    /// <summary>Tests: every sound given played (C4 E4 G4 C5 one after the other, then together) into a WAV file.</summary>
    public static void RenderPresets(string path)
    {
        var left = new List<float>();
        foreach (var preset in FmPresets.All())
        {
            var patch = new FmPatch();
            patch.CopyFrom(preset);
            var synth = new FmSynth(patch, new PianoWindowViewModel { Volume = 80 });
            void Play(int frames)
            {
                foreach (var frame in synth.Mix(frames))
                    left.Add(frame.X);
            }
            foreach (var note in new[] { 60, 64, 67, 72 })
            {
                synth.NoteOn(note, 0.8);
                Play(Rate / 4);
                synth.NoteOff(note);
                Play(Rate / 10);
            }
            foreach (var note in new[] { 48, 60, 64, 67 })
                synth.NoteOn(note, 0.9);
            Play(Rate);
            foreach (var note in new[] { 48, 60, 64, 67 })
                synth.NoteOff(note);
            Play(Rate);
            var peak = left.Skip(Math.Max(0, left.Count - Rate * 3)).Max(Math.Abs);
            KAONOMI.Chat.Services.ActivityLog.Info("Synth", $"Rendered {preset.Name}: peak {peak:0.00}");
            synth.QueueFree();
        }
        var samples = left.ToArray();
        KAONOMI.Chat.Services.WavFile.Write(path, samples, samples);
    }

    private static float[] BuildTable()
    {
        var table = new float[TableSize + 1];
        for (var i = 0; i <= TableSize; i++)
            table[i] = (float)Math.Sin(Math.Tau * i / TableSize);
        return table;
    }

    /// <summary>The speed of a rate (0 to 99), in level units a second: 99 at once, 50 about 0.4 s for the whole range.</summary>
    public static double Speed(int rate) => 0.6 * Math.Exp(Math.Clamp(rate, 0, 99) * 0.12);

    public override void _Ready()
    {
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
    }

    public void NoteOn(int note, double velocity = 0.8)
    {
        lock (_gate)
        {
            foreach (var old in _voices.Where(v => v.Note == note && !v.Released))
                Release(old);
            if (_patch.LfoSync && _voices.All(v => v.Released))
                _lfoPhase = 0;
            var voice = new Voice { Note = note, Velocity = velocity, PitchEnv = _patch.Pl4 };
            for (var k = 0; k < 6; k++)
                voice.Env[k] = _patch.Operators[k].L4;
            Control(voice, 0);
            _voices.Add(voice);
            if (_voices.Count > 16)
                _voices.RemoveAt(0);
        }
    }

    public void NoteOff(int note)
    {
        lock (_gate)
            foreach (var voice in _voices.Where(v => v.Note == note && v.Held))
            {
                voice.Held = false;
                if (!_keys.Sustain)
                    Release(voice);
            }
    }

    /// <summary>The pads of a MIDI keyboard: the FM synth has no drums.</summary>
    public void Hit(int pad, double velocity)
    {
    }

    private static void Release(Voice voice)
    {
        voice.Released = true;
        for (var k = 0; k < 6; k++)
            voice.Stage[k] = 3;
        voice.PitchStage = 3;
    }

    public override void _Process(double delta)
    {
        if (_playback is null)
            return;
        var frames = _playback.GetFramesAvailable();
        if (frames > 0)
            _playback.PushBuffer(Mix(frames));
    }

    private static double Sine(double cycles)
    {
        var x = cycles * TableSize;
        var floor = Math.Floor(x);
        var i = (int)((long)floor & (TableSize - 1));
        var frac = (float)(x - floor);
        return Table[i] + (Table[i + 1] - Table[i]) * frac;
    }

    public Vector2[] Mix(int frames)
    {
        var algorithm = FmAlgorithms.Get(_patch.Algorithm);
        var on = _patch.Operators.Select(o => o.On).ToArray();
        var modulatedBy = algorithm.ModulatedBy;
        var carrier = algorithm.Carrier;
        var feedbackFrom = algorithm.FeedbackFrom;
        var feedbackTo = algorithm.FeedbackTo;
        var feedback = _patch.Feedback == 0 ? 0 : Math.Pow(2, _patch.Feedback - 7) * 0.75;
        var carriers = Math.Max(1, carrier.Count(c => c));
        var gain = _keys.Volume / 100.0 * 0.5 / Math.Sqrt(carriers);
        var buffer = new Vector2[frames];
        lock (_gate)
        {
            for (var i = 0; i < frames; i++)
            {
                if (_tick++ % ControlEvery == 0)
                    ControlTick(ControlEvery / (double)Rate);
                double mix = 0;
                foreach (var v in _voices)
                {
                    for (var k = 5; k >= 0; k--)
                    {
                        v.Amp[k] = Math.Max(0, v.Amp[k] + v.AmpStep[k]);
                        if (!on[k] || v.Amp[k] <= 0)
                        {
                            if (k == feedbackFrom)
                                v.FeedbackBefore = 0;
                            v.Out[k] = 0;
                            v.Phase[k] += v.Increment[k];
                            continue;
                        }
                        double mod = 0;
                        foreach (var m in modulatedBy[k])
                            mod += v.Out[m];
                        mod *= ModIndex / Math.Tau;
                        if (k == feedbackTo && feedback > 0)
                            mod += feedback * (v.Out[feedbackFrom] + v.FeedbackBefore) * 0.5;
                        var output = v.Amp[k] * Sine(v.Phase[k] + mod);
                        if (k == feedbackFrom)
                            v.FeedbackBefore = v.Out[k];
                        v.Out[k] = output;
                        v.Phase[k] += v.Increment[k];
                        if (v.Phase[k] > 1024)
                            v.Phase[k] -= Math.Floor(v.Phase[k]);
                    }
                    for (var k = 0; k < 6; k++)
                        if (carrier[k])
                            mix += v.Out[k];
                }
                var s = (float)Math.Tanh(mix * gain);
                buffer[i] = new Vector2(s, s);
            }
            _voices.RemoveAll(v => v.Done);
        }
        return buffer;
    }

    private double NextNoise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return _noise / (double)uint.MaxValue * 2 - 1;
    }

    /// <summary>The LFO and, for every note, its envelopes, its levels and its frequencies (every 16 samples).</summary>
    private void ControlTick(double dt)
    {
        _lfoPhase += 0.06 * Math.Exp(_patch.LfoSpeed * 0.068) * dt;
        if (_lfoPhase >= 1)
        {
            _lfoPhase -= Math.Floor(_lfoPhase);
            _sampleHold = NextNoise();
        }
        var p = _lfoPhase;
        _lfo = _patch.LfoWave switch
        {
            "Saw down" => 1 - 2 * p,
            "Saw up" => 2 * p - 1,
            "Square" => p < 0.5 ? 1 : -1,
            "Sine" => Math.Sin(Math.Tau * p),
            "S&H" => _sampleHold,
            _ => p < 0.5 ? 4 * p - 1 : 3 - 4 * p
        };
        foreach (var voice in _voices)
            Control(voice, dt);
    }

    private void Control(Voice v, double dt)
    {
        var patch = _patch;
        var algorithm = FmAlgorithms.Get(patch.Algorithm);
        v.Time += dt;
        var delay = patch.LfoDelay / 99.0 * 4;
        var fade = delay <= 0 ? 1 : Math.Clamp((v.Time - delay) / 0.6, 0, 1);
        var lfo = _lfo * fade;

        // The pitch: the envelope (50 the note, 0 and 99 four octaves away), the vibrato, the bend.
        v.PitchEnv = Step(v.PitchEnv, ref v.PitchStage, i => patch.PitchLevel(i), i => patch.PitchRate(i), 0, dt, v.Released);
        var semitones = (v.PitchEnv - 50) / 50 * 48
                        + lfo * patch.Pmd / 99.0 * PmsSemitones[Math.Clamp(patch.Pms, 0, 7)]
                        + lfo * _keys.Modulation * 0.6
                        + _keys.PitchBend * patch.BendRange;
        var pitch = Math.Pow(2, semitones / 12);
        var note = 440 * Math.Pow(2, (v.Note + patch.Transpose - 69) / 12.0);
        var tremolo = (lfo + 1) / 2 * patch.Amd / 99.0;
        var done = true;
        for (var k = 0; k < 6; k++)
        {
            var op = patch.Operators[k];
            var scale = op.RateScale * Math.Max(0, v.Note - 21) / 88.0 * 14;
            v.Env[k] = Step(v.Env[k], ref v.Stage[k], op.LevelAt, op.Rate, scale, dt, v.Released);
            var level = op.ScaledLevel(v.Note);
            double amp = 0;
            if (v.Env[k] > 0.01 && level > 0)
            {
                var attenuation = (99 - v.Env[k]) * 0.75 + (99 - level) * 0.75;
                amp = Math.Pow(10, -attenuation / 20);
                amp *= 1 - op.Velocity / 7.0 * (1 - v.Velocity) * 0.9;
                amp *= 1 - op.AmpMod / 3.0 * tremolo;
            }
            // The level reached at the next tick, sample by sample.
            v.AmpStep[k] = (amp - v.Amp[k]) / ControlEvery;
            v.Increment[k] = (op.Fixed ? op.FixedHz : note * op.Ratio * pitch) / Rate;
            if (algorithm.Carrier[k] && op.On && !(v.Released && v.Stage[k] == 3 && Math.Abs(v.Env[k] - op.L4) < 0.5 && amp < 0.002) && amp > 0.0002)
                done = false;
        }
        v.Done = v.Released && done;
    }

    /// <summary>An envelope a step further: towards the level of its stage at the speed of its rate, on to the next stage once there (L3 held).</summary>
    private static double Step(double value, ref int stage, Func<int, int> level, Func<int, int> rate, double scale, double dt, bool released)
    {
        var target = level(stage);
        var speed = Speed((int)Math.Min(99, rate(stage) + scale));
        var move = speed * dt;
        if (Math.Abs(target - value) <= move)
        {
            value = target;
            if (stage < 2 && !released)
                stage++;
        }
        else
            value += Math.Sign(target - value) * move;
        return value;
    }
}
