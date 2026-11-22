using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Writing;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Media;

/// <summary>
/// /piano: a copy of the Launchkey Mini 37 MK4 of Novation, played with the mouse or a MIDI keyboard. Its body, dark:
/// the little screen (the instrument, the octave, the tempo, the last knob turned), ARP and HOLD under it; the eight
/// knobs (volume, tone, release, vibrato, echo, echo time, drums, tempo: dragged up or down, or the wheel); the sixteen
/// pads in two rows (drums: kick, snare, hats, toms, clap, crash… lighting up when hit); ▲ ▼ the instrument, ▶ PLAY and
/// ● REC (a take recorded, played again in a loop); on the left OCT − / OCT + and the touch strips PITCH (two semitones,
/// back to the middle when let go) and MOD (a vibrato); the 37 keys (three octaves and a C: a click plays a key, dragged
/// over the keys a glissando; the keys of the computer play too, by their place whatever the layout: the lower row
/// Z S X D C V G B H N J M for the first octave, the upper row Q 2 W 3 E R 5 T 6 Y 7 U I for the next one; the space bar
/// held is the sustain pedal). The sound is made as it is played: piano, electric piano, organ, harpsichord. An external
/// MIDI keyboard plugged in plays it too: its keys (their velocity), its pads in drum mode (channel 10), its knobs
/// (controllers 21 to 28), its strips (pitch bend, controller 1) and its sustain pedal (controller 64); its inputs are
/// opened once for the chat (CoreMIDI cannot be opened again on macOS: a keyboard plugged in later needs the chat started
/// again). The lights of the real keyboard stay as they are (Godot sends no MIDI).
/// </summary>
[ToolView(typeof(PianoWindowViewModel), "Piano", "🎹", 1280, 640)]
public sealed class PianoView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (PianoWindowViewModel)viewModel;
        var synth = new PianoSynth(vm, broadcast: true);
        var keyboard = new PianoKeyboard(vm, synth);
        var body = new LaunchkeyBody(vm, keyboard) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var page = Ui.Column(0, body);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        page.AddChild(synth);
        return page;
    }
}

/// <summary>A knob of the piano: what it turns (a value of the model, from <see cref="Min"/> to <see cref="Max"/>).</summary>
internal sealed record PianoKnob(string Name, string Property, int Min, int Max, Func<PianoWindowViewModel, int> Get, Action<PianoWindowViewModel, int> Set, string Tooltip)
{
    public static readonly PianoKnob[] All =
    [
        new("Volume", nameof(PianoWindowViewModel.Volume), 0, 100, v => v.Volume, (v, x) => v.Volume = x, "The volume"),
        new("Tone", nameof(PianoWindowViewModel.Tone), 0, 100, v => v.Tone, (v, x) => v.Tone = x, "The brightness of the sound (a low-pass filter)"),
        new("Release", nameof(PianoWindowViewModel.Release), 0, 100, v => v.Release, (v, x) => v.Release = x, "How long the notes ring once released"),
        new("Vibrato", nameof(PianoWindowViewModel.Vibrato), 0, 100, v => v.Vibrato, (v, x) => v.Vibrato = x, "The depth of the vibrato (the MOD strip adds to it)"),
        new("Echo", nameof(PianoWindowViewModel.Echo), 0, 100, v => v.Echo, (v, x) => v.Echo = x, "The level of the echo"),
        new("Time", nameof(PianoWindowViewModel.EchoTime), 0, 100, v => v.EchoTime, (v, x) => v.EchoTime = x, "The time of the echo (50 to 800 ms)"),
        new("Drums", nameof(PianoWindowViewModel.Drums), 0, 100, v => v.Drums, (v, x) => v.Drums = x, "The volume of the drums of the pads"),
        new("Tempo", nameof(PianoWindowViewModel.Tempo), 40, 220, v => v.Tempo, (v, x) => v.Tempo = x, "The tempo of the arpeggiator (beats per minute)")
    ];

    /// <summary>What the screen shows for it.</summary>
    public string Shown(PianoWindowViewModel vm) => Property switch
    {
        nameof(PianoWindowViewModel.EchoTime) => $"{50 + 7.5 * vm.EchoTime:0} ms",
        nameof(PianoWindowViewModel.Tempo) => $"{vm.Tempo} BPM",
        _ => Get(vm).ToString()
    };
}

/// <summary>
/// The body of the Launchkey: everything but the keys (drawn here: the screen, the buttons, the knobs, the pads, the
/// strips), the keys set in their place below the pads.
/// </summary>
public sealed partial class LaunchkeyBody : Control
{
    /// <summary>The sounds of the pads, the top row first (the bottom row, closer to the hands, the kit).</summary>
    public static string[] PadNames => PianoWindowViewModel.PadNames;

    private const float M = 16, LeftW = 214, RightW = 120;
    private readonly PianoWindowViewModel _vm = null!;
    private readonly PianoKeyboard _keys = null!;
    private readonly float[] _padLight = new float[16];
    private readonly StyleBoxFlat _box = new();
    private int _dragKnob = -1;
    private float _dragY;
    private int _dragStart;
    private int _dragStrip = -1;
    private string _message = "";
    private double _messageUntil;
    private double _clock;

    // Godot builds its scripts with this constructor.
    public LaunchkeyBody()
    {
    }

    public LaunchkeyBody(PianoWindowViewModel vm, PianoKeyboard keys)
    {
        _vm = vm;
        _keys = keys;
        CustomMinimumSize = new Vector2(1100, 520);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "Piano";
        AddChild(keys);
        keys.PadHit += (pad, velocity) => _padLight[pad] = (float)Math.Max(_padLight[pad], 0.55 + 0.45 * velocity);
        keys.StateChanged += QueueRedraw;
        _vm.PropertyChanged += (_, e) =>
        {
            if (PianoKnob.All.FirstOrDefault(k => k.Property == e.PropertyName) is { } knob)
                Say($"{knob.Name.ToUpperInvariant()}  {knob.Shown(_vm)}");
            else if (e.PropertyName == nameof(_vm.Instrument))
                Say(_vm.Instrument.ToUpperInvariant());
            else if (e.PropertyName == nameof(_vm.Octave))
                Say($"OCTAVE  C{_vm.Octave} – C{_vm.Octave + 3}");
            QueueRedraw();
        };
    }

    private void Say(string message)
    {
        _message = message;
        _messageUntil = _clock + 1.6;
    }

    // Where everything is.
    private float TopH => Mathf.Clamp(Size.Y * 0.45f, 200, 280);
    private Rect2 Screen => new(M, M + 22, LeftW - M - 18, 78);
    private Rect2 ArpButton => new(M, Screen.End.Y + 12, (Screen.Size.X - 8) / 2, 30);
    private Rect2 HoldButton => new(ArpButton.End.X + 8, ArpButton.Position.Y, ArpButton.Size.X, 30);
    private float X0 => LeftW;
    private float X1 => Size.X - RightW;
    private float Col => (X1 - X0) / 8;
    private Vector2 KnobCentre(int i) => new(X0 + Col * (i + 0.5f), M + 34);
    private float KnobRadius => Mathf.Min(Col * 0.26f, 21);

    private Rect2 Pad(int i)
    {
        var top = M + 92;
        var h = (TopH - top - 10 - 4) / 2;
        return new Rect2(X0 + Col * (i % 8) + 5, top + i / 8 * (h + 10), Col - 10, h);
    }

    private Rect2 UpButton => new(X1 + 16, M + 18, (RightW - 16 - M - 8) / 2, 34);
    private Rect2 DownButton => new(UpButton.End.X + 8, UpButton.Position.Y, UpButton.Size.X, 34);
    private Rect2 PlayButton => new(X1 + 16, Pad(7).Position.Y, RightW - 16 - M, Pad(7).Size.Y);
    private Rect2 RecButton => new(X1 + 16, Pad(15).Position.Y, RightW - 16 - M, Pad(15).Size.Y);
    private Rect2 KeysRect => new(LeftW, TopH + 14, Size.X - LeftW - M, Size.Y - TopH - 14 - M - 22);
    private Rect2 OctDown => new(M, TopH + 14, (LeftW - M - 18 - 8) / 2, 30);
    private Rect2 OctUp => new(OctDown.End.X + 8, OctDown.Position.Y, OctDown.Size.X, 30);
    private Rect2 PitchStrip => new(M + 18, OctDown.End.Y + 16, 34, KeysRect.End.Y - OctDown.End.Y - 16 - 20);
    private Rect2 ModStrip => new(PitchStrip.End.X + 44, PitchStrip.Position.Y, 34, PitchStrip.Size.Y);

    private sealed record Button(string Label, Func<Rect2> Rect, Action Act, Func<bool> Lit, Color Light, string Tooltip);

    private Button[] Buttons =>
    [
        new("ARP", () => ArpButton, () => _vm.Arp = !_vm.Arp, () => _vm.Arp, E.Amber, "The arpeggiator: the notes held played one after the other, at the tempo of the last knob"),
        new("HOLD", () => HoldButton, () => _vm.Sustain = !_vm.Sustain, () => _vm.Sustain, E.Cyan, "The sustain pedal: the notes ring on once the keys are released (or hold the space bar)"),
        new("▲", () => UpButton, () => StepInstrument(-1), () => false, E.Cyan, "The instrument before"),
        new("▼", () => DownButton, () => StepInstrument(1), () => false, E.Cyan, "The instrument after"),
        new("▶", () => PlayButton, _keys.TogglePlay, () => _keys.Playing, E.Green, "Play the take recorded, in a loop (again: stop)"),
        new("●", () => RecButton, _keys.ToggleRecord, () => _keys.Recording, E.Red, "Record a take: what the keys and the pads play (again: stop)"),
        new("OCT −", () => OctDown, () => _vm.Octave = Math.Max(1, _vm.Octave - 1), () => false, E.Cyan, "The keyboard an octave lower"),
        new("OCT +", () => OctUp, () => _vm.Octave = Math.Min(6, _vm.Octave + 1), () => false, E.Cyan, "The keyboard an octave higher")
    ];

    private void StepInstrument(int step)
    {
        var all = PianoWindowViewModel.Instruments;
        var at = Array.IndexOf(all, _vm.Instrument);
        _vm.Instrument = all[((at < 0 ? 0 : at) + step + all.Length) % all.Length];
    }

    public override void _Ready() => Place();

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            Place();
    }

    private void Place()
    {
        _keys.Position = KeysRect.Position;
        _keys.Size = KeysRect.Size;
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        for (var i = 0; i < 16; i++)
            _padLight[i] *= (float)Math.Exp(-delta * 3.5);
        if (IsVisibleInTree())
            QueueRedraw();
    }

    private void Box(Rect2 rect, Color fill, float radius, Color? border = null, int borderWidth = 1)
    {
        _box.BgColor = fill;
        _box.SetCornerRadiusAll((int)radius);
        _box.SetBorderWidthAll(border is null ? 0 : borderWidth);
        if (border is { } b)
            _box.BorderColor = b;
        _box.AntiAliasing = true;
        DrawStyleBox(_box, rect);
    }

    public static Color PadColour(int i) => Color.FromHsv(i < 8 ? 0.47f + i * 0.035f : Mathf.PosMod(0.97f + (i - 8) * 0.028f, 1f), 0.78f, 1f);

    public override void _Draw()
    {
        var font = E.Body;
        var mono = E.Mono;
        var label = new Color(0.62f, 0.63f, 0.66f);
        Box(new Rect2(Vector2.Zero, Size), new Color(0.105f, 0.105f, 0.115f), 16, new Color(0.24f, 0.24f, 0.27f));
        // The name, above the screen.
        DrawString(font, new Vector2(M + 2, M + 12), "KAONOMI", HorizontalAlignment.Left, -1, 13, new Color(0.92f, 0.92f, 0.94f));
        DrawString(font, new Vector2(M + 84, M + 12), "MINI 37", HorizontalAlignment.Left, -1, 11, label);

        // The screen.
        var screen = Screen;
        Box(screen, new Color(0.01f, 0.01f, 0.015f), 6, new Color(0.28f, 0.28f, 0.3f));
        var ink = new Color(0.86f, 0.95f, 1f);
        DrawString(mono, screen.Position + new Vector2(10, 24), _vm.Instrument.ToUpperInvariant(), HorizontalAlignment.Left, screen.Size.X - 20, 16, ink);
        var info = _clock < _messageUntil ? _message : $"C{_vm.Octave}–C{_vm.Octave + 3}  {_vm.Tempo} BPM";
        DrawString(mono, screen.Position + new Vector2(10, 46), info, HorizontalAlignment.Left, screen.Size.X - 20, 12, ink.Darkened(0.2f));
        var flags = new (string Text, bool On)[] { ("ARP", _vm.Arp), ("HOLD", _vm.Sustain), ("REC", _keys.Recording && _clock % 1 < 0.6), ("PLAY", _keys.Playing), ("MIDI", _vm.HasMidi) };
        var fx = screen.Position.X + 10;
        foreach (var (text, on) in flags)
        {
            DrawString(mono, new Vector2(fx, screen.End.Y - 10), text, HorizontalAlignment.Left, -1, 10, on ? ink : new Color(0.22f, 0.25f, 0.28f));
            fx += font.GetStringSize(text, HorizontalAlignment.Left, -1, 10).X + 12;
        }

        // The buttons.
        foreach (var button in Buttons)
        {
            var rect = button.Rect();
            var lit = button.Lit();
            Box(rect, lit ? button.Light.Darkened(0.15f) : new Color(0.19f, 0.19f, 0.21f), 5, new Color(0.3f, 0.3f, 0.33f));
            var keyInk = lit ? new Color(0.05f, 0.05f, 0.06f) : new Color(0.88f, 0.88f, 0.9f);
            var centre = rect.GetCenter();
            // PLAY and REC drawn (the fonts would make emoji of them).
            if (button.Label == "▶")
            {
                DrawColoredPolygon([centre + new Vector2(-7, -9), centre + new Vector2(10, 0), centre + new Vector2(-7, 9)], lit ? keyInk : E.Green);
                continue;
            }
            if (button.Label == "●")
            {
                DrawCircle(centre, 8, lit ? keyInk : new Color(0.9f, 0.3f, 0.3f));
                continue;
            }
            var size = button.Label.Length <= 1 ? 16 : 11;
            DrawString(font, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y / 2 + size * 0.36f), button.Label, HorizontalAlignment.Center, rect.Size.X, size,
                keyInk);
        }
        DrawString(font, new Vector2(X1 + 16, UpButton.End.Y + 16), "SOUND", HorizontalAlignment.Center, RightW - 16 - M, 10, label);

        // The knobs.
        for (var i = 0; i < PianoKnob.All.Length; i++)
        {
            var knob = PianoKnob.All[i];
            var c = KnobCentre(i);
            var r = KnobRadius;
            var value = (knob.Get(_vm) - knob.Min) / (float)(knob.Max - knob.Min);
            const float from = Mathf.Pi * 0.75f, span = Mathf.Pi * 1.5f;
            DrawArc(c, r + 5, from, from + span, 40, new Color(0.25f, 0.25f, 0.28f), 3, true);
            if (value > 0.001f)
                DrawArc(c, r + 5, from, from + span * value, 40, E.Cyan, 3, true);
            DrawCircle(c, r, new Color(0.06f, 0.06f, 0.07f));
            DrawCircle(c, r - 3, new Color(0.17f, 0.17f, 0.19f));
            var angle = from + span * value;
            DrawLine(c + Vector2.FromAngle(angle) * (r * 0.25f), c + Vector2.FromAngle(angle) * (r - 4), new Color(0.95f, 0.95f, 0.97f), 2.5f, true);
            DrawString(font, new Vector2(c.X - Col / 2, c.Y + r + 22), knob.Name.ToUpperInvariant(), HorizontalAlignment.Center, Col, 10, label);
        }

        // The pads.
        for (var i = 0; i < 16; i++)
        {
            var rect = Pad(i);
            var colour = PadColour(i);
            var light = _padLight[i];
            if (light > 0.02f)
                Box(rect.Grow(3 * light), colour with { A = 0.25f * light }, 9);
            Box(rect, colour.Darkened(0.74f).Lerp(colour.Lerp(Colors.White, 0.25f), light), 6, new Color(0f, 0f, 0f, 0.5f));
            DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 8), PadNames[i].ToUpperInvariant(), HorizontalAlignment.Center, rect.Size.X, 9,
                light > 0.4f ? new Color(0.05f, 0.05f, 0.06f, 0.8f) : new Color(1, 1, 1, 0.45f));
        }

        // The touch strips: their lights where they are.
        Strip(PitchStrip, "PITCH", (float)_vm.PitchBend, true);
        Strip(ModStrip, "MOD", (float)_vm.Modulation, false);

        // The well of the keys, and what is said under it.
        var keys = KeysRect;
        Box(keys.Grow(5), new Color(0.03f, 0.03f, 0.035f), 6);
        var status = _vm.Midi;
        DrawString(font, new Vector2(LeftW, Size.Y - M - 2), status, HorizontalAlignment.Left, (Size.X - LeftW - M) * 0.5f, 11, _vm.HasMidi ? E.Green : label);
        DrawString(font, new Vector2(LeftW + (Size.X - LeftW - M) * 0.5f, Size.Y - M - 2), _vm.Played, HorizontalAlignment.Right, (Size.X - LeftW - M) * 0.5f, 11, label);
    }

    private void Strip(Rect2 rect, string name, float value, bool centred)
    {
        Box(rect, new Color(0.05f, 0.05f, 0.055f), 8, new Color(0.27f, 0.27f, 0.3f));
        const int dots = 14;
        var level = centred ? (value + 1) / 2 : value;
        for (var i = 0; i < dots; i++)
        {
            var at = (i + 0.5f) / dots; // 0 at the bottom
            var y = rect.End.Y - 8 - at * (rect.Size.Y - 16);
            var lit = centred ? (value >= 0 ? at >= 0.5f - 0.5f / dots && at <= level + 0.5f / dots : at <= 0.5f + 0.5f / dots && at >= level - 0.5f / dots)
                : value > 0.01f && at <= level;
            DrawCircle(new Vector2(rect.GetCenter().X, y), 3.2f, lit ? E.Cyan : new Color(0.17f, 0.17f, 0.19f));
        }
        DrawString(E.Body, new Vector2(rect.Position.X - 10, rect.End.Y + 16), name, HorizontalAlignment.Center, rect.Size.X + 20, 10, new Color(0.62f, 0.63f, 0.66f));
    }

    private int KnobAt(Vector2 at)
    {
        for (var i = 0; i < PianoKnob.All.Length; i++)
            if (at.DistanceTo(KnobCentre(i)) <= KnobRadius + 8)
                return i;
        return -1;
    }

    private int PadAt(Vector2 at)
    {
        for (var i = 0; i < 16; i++)
            if (Pad(i).HasPoint(at))
                return i;
        return -1;
    }

    private void SetStrip(int strip, Vector2 at)
    {
        var rect = strip == 0 ? PitchStrip : ModStrip;
        var level = Mathf.Clamp((rect.End.Y - 8 - at.Y) / (rect.Size.Y - 16), 0, 1);
        if (strip == 0)
            _vm.PitchBend = level * 2 - 1;
        else
            _vm.Modulation = level;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel when KnobAt(wheel.Position) is var k and >= 0:
            {
                var knob = PianoKnob.All[k];
                var step = Math.Max(1, (knob.Max - knob.Min) / 50);
                knob.Set(_vm, Math.Clamp(knob.Get(_vm) + (wheel.ButtonIndex == MouseButton.WheelUp ? step : -step), knob.Min, knob.Max));
                AcceptEvent();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                AcceptEvent();
                if (button.Pressed)
                {
                    _keys.CallDeferred(Control.MethodName.GrabFocus);
                    _keys.TakeMidi();
                    var at = button.Position;
                    if (KnobAt(at) is var knob and >= 0)
                    {
                        _dragKnob = knob;
                        _dragY = at.Y;
                        _dragStart = PianoKnob.All[knob].Get(_vm);
                    }
                    else if (PadAt(at) is var pad and >= 0)
                    {
                        // Higher on the pad, harder.
                        var rect = Pad(pad);
                        _keys.HitPad(pad, Mathf.Clamp(1.1f - (at.Y - rect.Position.Y) / rect.Size.Y * 0.6f, 0.4f, 1f));
                    }
                    else if (PitchStrip.Grow(6).HasPoint(at) || ModStrip.Grow(6).HasPoint(at))
                    {
                        _dragStrip = PitchStrip.Grow(6).HasPoint(at) ? 0 : 1;
                        SetStrip(_dragStrip, at);
                    }
                    else if (Buttons.FirstOrDefault(b => b.Rect().HasPoint(at)) is { } pressed)
                        pressed.Act();
                }
                else
                {
                    // The pitch strip let go: back to the middle.
                    if (_dragStrip == 0)
                        _vm.PitchBend = 0;
                    _dragKnob = -1;
                    _dragStrip = -1;
                }
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Left) != 0:
                if (_dragKnob >= 0)
                {
                    var knob = PianoKnob.All[_dragKnob];
                    var value = _dragStart + (int)Math.Round((_dragY - motion.Position.Y) / 160f * (knob.Max - knob.Min));
                    knob.Set(_vm, Math.Clamp(value, knob.Min, knob.Max));
                    AcceptEvent();
                }
                else if (_dragStrip >= 0)
                {
                    SetStrip(_dragStrip, motion.Position);
                    AcceptEvent();
                }
                break;
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (KnobAt(atPosition) is var k and >= 0)
            return $"{PianoKnob.All[k].Tooltip}: {PianoKnob.All[k].Shown(_vm)} (drag it up or down, or the wheel)";
        if (PadAt(atPosition) is var p and >= 0)
            return $"{PadNames[p]} (a drum pad)";
        if (PitchStrip.HasPoint(atPosition))
            return "PITCH: two semitones down or up (back to the middle when let go)";
        if (ModStrip.HasPoint(atPosition))
            return "MOD: a vibrato";
        if (Screen.HasPoint(atPosition))
            return "The screen: the instrument, the keyboard, the tempo, the last thing changed";
        return Buttons.FirstOrDefault(b => b.Rect().HasPoint(atPosition))?.Tooltip ?? "";
    }
}

/// <summary>
/// The sound of the piano: an audio stream filled as it plays, every note a voice (its harmonics, each fading at its
/// own pace, the attack and the decay of the instrument), the drums of the pads made of noise and falling tones, all
/// mixed: the pitch strip and the vibrato on the notes, the tone (a low-pass), then the echo, a soft limit.
/// </summary>
/// <summary>What the keyboard plays: the sounds of /piano, or the FM synth of /synth.</summary>
public interface IPlayable
{
    void NoteOn(int note, double velocity = 0.8);
    void NoteOff(int note);
    void Hit(int pad, double velocity);
}

public sealed partial class PianoSynth : Node, IPlayable
{
    private const int Rate = 44100;
    private readonly PianoWindowViewModel _vm = null!;
    private readonly AudioStreamPlayer _player = new();
    private AudioStreamGeneratorPlayback? _playback;
    private readonly List<Voice> _voices = [];
    private readonly List<Drum> _drums = [];
    private readonly object _gate = new();
    private readonly float[] _echo = new float[Rate];
    private int _echoAt;
    private double _low;
    private double _clock;
    private uint _noise = 2463534242;
    private readonly bool _broadcast;

    /// <summary>What a piano plays (not the looper's own synths): a note down or up as heard, a pad hit.</summary>
    public readonly record struct PianoPlayed(bool Pad, int Value, double Velocity, bool On, string Instrument);

    /// <summary>Raised on the main thread by the synths of /piano: the looper records it.</summary>
    public static event Action<PianoPlayed>? Played;

    private sealed class Voice
    {
        public int Note;
        public double Frequency;
        public double Time;
        public double Phase;
        public bool Held = true;
        public double Release = -1;
        public double Velocity;
    }

    private sealed class Drum
    {
        public int Pad;
        public double Time;
        public double Velocity;
        public double Phase;
        public double Low;
    }

    // Godot builds its scripts with this constructor.
    public PianoSynth()
    {
    }

    public PianoSynth(PianoWindowViewModel vm, bool broadcast = false)
    {
        _vm = vm;
        _broadcast = broadcast;
        _player.Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.08f };
        AddChild(_player);
        _vm.PropertyChanged += (_, e) =>
        {
            // The pedal lifted: the notes not held stop.
            if (e.PropertyName == nameof(_vm.Sustain) && !_vm.Sustain)
            {
                List<int> released = [];
                lock (_gate)
                    foreach (var voice in _voices.Where(v => !v.Held && v.Release < 0))
                    {
                        voice.Release = voice.Time;
                        released.Add(voice.Note);
                    }
                if (_broadcast)
                    foreach (var note in released.Distinct())
                        Played?.Invoke(new PianoPlayed(false, note, 0, false, _vm.Instrument));
            }
        };
    }

    public override void _Ready()
    {
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
    }

    public void NoteOn(int note, double velocity = 0.8)
    {
        lock (_gate)
        {
            // The same note again: the old voice stops quickly.
            foreach (var old in _voices.Where(v => v.Note == note && v.Release < 0))
                old.Release = old.Time;
            _voices.Add(new Voice { Note = note, Frequency = 440 * Math.Pow(2, (note - 69) / 12.0), Velocity = velocity });
            if (_voices.Count > 24)
                _voices.RemoveAt(0);
        }
        if (_broadcast)
            Played?.Invoke(new PianoPlayed(false, note, velocity, true, _vm.Instrument));
    }

    /// <summary>Every note stops at once (the looper stopped).</summary>
    public void AllOff()
    {
        StopClips();
        lock (_gate)
            foreach (var voice in _voices.Where(v => v.Release < 0))
            {
                voice.Held = false;
                voice.Release = voice.Time;
            }
    }

    public void NoteOff(int note)
    {
        var released = false;
        lock (_gate)
            foreach (var voice in _voices.Where(v => v.Note == note && v.Held))
            {
                voice.Held = false;
                if (!_vm.Sustain)
                {
                    voice.Release = voice.Time;
                    released = true;
                }
            }
        if (_broadcast && released)
            Played?.Invoke(new PianoPlayed(false, note, 0, false, _vm.Instrument));
    }

    /// <summary>A pad hit (its drum, <see cref="LaunchkeyBody.PadNames"/>).</summary>
    public void Hit(int pad, double velocity)
    {
        lock (_gate)
        {
            _drums.Add(new Drum { Pad = pad, Velocity = velocity });
            if (_drums.Count > 16)
                _drums.RemoveAt(0);
        }
        if (_broadcast && pad is >= 0 and < 16)
            Played?.Invoke(new PianoPlayed(true, pad, velocity, true, _vm.Instrument));
    }

    public override void _Process(double delta)
    {
        if (_playback is null)
            return;
        var frames = _playback.GetFramesAvailable();
        if (frames > 0)
            _playback.PushBuffer(Mix(frames));
    }

    /// <summary>An audio file played by the looper: its samples (44.1 kHz), where it is, where it stops.</summary>
    private sealed class Clip
    {
        public float[] Left = [];
        public float[] Right = [];
        public int At;
        public int End;
    }

    private readonly List<Clip> _clips = [];

    /// <summary>Plays an audio file (44.1 kHz) for <paramref name="frames"/> frames at most (the loop), at the volume of the synth.</summary>
    public void PlayClip(float[] left, float[] right, int frames)
    {
        lock (_gate)
        {
            _clips.Clear();
            _clips.Add(new Clip { Left = left, Right = right, End = Math.Min(frames, left.Length) });
        }
    }

    public void StopClips()
    {
        lock (_gate)
            _clips.Clear();
    }

    /// <summary>The next <paramref name="frames"/> frames of the sound (played live, or rendered by the looper's export).</summary>
    public Vector2[] Mix(int frames)
    {
        var gain = _vm.Volume / 100.0 * 0.22;
        var instrument = _vm.Instrument;
        var dt = 1.0 / Rate;
        var bend = Math.Pow(2, _vm.PitchBend * 2 / 12);
        var vibrato = Math.Max(_vm.Vibrato / 100.0, _vm.Modulation) * 0.012;
        var release = 1 / (0.3 + 2.8 * _vm.Release / 100.0);
        var cutoff = 300 * Math.Pow(60, _vm.Tone / 100.0);
        var smooth = 1 - Math.Exp(-Math.Tau * cutoff / Rate);
        var echoMix = _vm.Echo / 100.0 * 0.6;
        var echoDelay = (int)(Rate * (0.05 + 0.75 * _vm.EchoTime / 100.0));
        var drumGain = _vm.Drums / 100.0 * 0.9;
        var buffer = new Vector2[frames];
        lock (_gate)
        {
            for (var i = 0; i < frames; i++)
            {
                double sample = 0;
                var pitch = bend * (1 + vibrato * Math.Sin(Math.Tau * 5.5 * _clock));
                foreach (var voice in _voices)
                {
                    sample += Sample(voice, instrument, release) * voice.Velocity;
                    voice.Time += dt;
                    voice.Phase += voice.Frequency * pitch * dt;
                    if (voice.Phase > 1000)
                        voice.Phase -= Math.Floor(voice.Phase);
                }
                _low += smooth * (sample * gain * 1.6 - _low);
                double drums = 0;
                foreach (var drum in _drums)
                {
                    drums += DrumSample(drum) * drum.Velocity;
                    drum.Time += dt;
                }
                var dry = _low + drums * drumGain * _vm.Volume / 100.0;
                var delayed = _echo[(_echoAt - echoDelay + _echo.Length) % _echo.Length];
                _echo[_echoAt] = (float)(dry + delayed * 0.45);
                _echoAt = (_echoAt + 1) % _echo.Length;
                // A soft limit: many notes at once do not clip.
                var s = (float)Math.Tanh(dry + delayed * echoMix);
                float clipL = 0, clipR = 0;
                foreach (var clip in _clips)
                    if (clip.At < clip.End)
                    {
                        // A short fade at its end (no click when the loop cuts it).
                        var fade = Math.Min(1f, (clip.End - clip.At) / 256f);
                        clipL += clip.Left[clip.At] * fade;
                        clipR += clip.Right[clip.At] * fade;
                        clip.At++;
                    }
                var clipGain = (float)(_vm.Volume / 100.0);
                buffer[i] = new Vector2(Math.Clamp(s + clipL * clipGain, -1f, 1f), Math.Clamp(s + clipR * clipGain, -1f, 1f));
                _clock += dt;
            }
            _voices.RemoveAll(v => Envelope(v, instrument, release) < 0.0005 && v.Time > 0.05);
            _drums.RemoveAll(d => d.Time > (d.Pad is 3 or 6 or 15 ? 3 : 1.2));
            _clips.RemoveAll(c => c.At >= c.End);
        }
        return buffer;
    }

    private double Noise()
    {
        _noise ^= _noise << 13;
        _noise ^= _noise >> 17;
        _noise ^= _noise << 5;
        return _noise / (double)uint.MaxValue * 2 - 1;
    }

    /// <summary>A sample of a drum: falling tones (kicks, toms), noise (snare, hats, cymbals), bright clicks.</summary>
    private double DrumSample(Drum d)
    {
        var t = d.Time;
        var dt = 1.0 / Rate;
        var noise = Noise();
        d.Low += 0.35 * (noise - d.Low);
        var high = noise - d.Low; // the noise without its lows: hats, cymbals
        double Tone(double frequency)
        {
            d.Phase += frequency * dt;
            return Math.Sin(Math.Tau * d.Phase);
        }
        return d.Pad switch
        {
            0 => noise * (Burst(t, 0) + Burst(t, 0.011) + Burst(t, 0.022) + 0.6 * Math.Exp(-t * 11)) * 0.45, // clap
            1 => Math.Sin(Math.Tau * 1700 * t) * Math.Exp(-t * 110) * 0.6 + noise * Math.Exp(-t * 220) * 0.3, // rim
            2 => high * (t < 0.05 ? t / 0.05 : Math.Exp(-(t - 0.05) * 28)) * 0.5, // shaker
            3 => high * Math.Exp(-t * 2.6) * 0.16 + (Math.Sin(Math.Tau * 2950 * t) + Math.Sin(Math.Tau * 4170 * t)) * 0.05 * Math.Exp(-t * 2.2), // ride
            4 => (Math.Sign(Math.Sin(Math.Tau * 545 * t)) + Math.Sign(Math.Sin(Math.Tau * 815 * t))) * 0.1 * Math.Exp(-t * 9), // cowbell
            5 => Math.Sin(Math.Tau * 1050 * t) * Math.Exp(-t * 38) * 0.7, // block
            6 => Tone(42 + 60 * Math.Exp(-t * 20)) * Math.Exp(-t * 1.6) * 0.95, // 808
            7 => Tone(80 + 1800 * Math.Exp(-t * 18)) * Math.Exp(-t * 6) * 0.4, // zap
            8 => Tone(45 + 115 * Math.Exp(-t * 32)) * Math.Exp(-t * 7) * 1.0 + noise * Math.Exp(-t * 300) * 0.25, // kick
            9 => Math.Sin(Math.Tau * 185 * t) * Math.Exp(-t * 22) * 0.45 + noise * Math.Exp(-t * 15) * 0.55, // snare
            10 => high * Math.Exp(-t * 55) * 0.5, // hat
            11 => high * Math.Exp(-t * 6) * 0.38, // open hat
            12 => Tone(88 + 40 * Math.Exp(-t * 12)) * Math.Exp(-t * 6) * 0.85, // low tom
            13 => Tone(128 + 50 * Math.Exp(-t * 12)) * Math.Exp(-t * 6.5) * 0.8, // mid tom
            14 => Tone(178 + 60 * Math.Exp(-t * 12)) * Math.Exp(-t * 7) * 0.75, // high tom
            Metronome => Math.Sin(Math.Tau * 1000 * t) * Math.Exp(-t * 70) * 0.5, // the metronome
            MetronomeAccent => Math.Sin(Math.Tau * 1600 * t) * Math.Exp(-t * 60) * 0.6, // the metronome, the first beat
            _ => high * Math.Exp(-t * 1.9) * 0.34 + noise * Math.Exp(-t * 30) * 0.15 // crash
        };
    }

    /// <summary>The clicks of the metronome (the looper), after the sixteen pads.</summary>
    public const int Metronome = 16, MetronomeAccent = 17;

    private static double Burst(double t, double at) => t >= at ? Math.Exp(-(t - at) * 90) : 0;

    /// <summary>The loudness of a voice now (attack, decay of the instrument, release once the key is up).</summary>
    private static double Envelope(Voice v, string instrument, double release)
    {
        var t = v.Time;
        double attack = instrument == "Organ" ? 0.01 : 0.004;
        double level = Math.Min(1, t / attack);
        level *= instrument switch
        {
            "Organ" => 0.75,
            "Harpsichord" => Math.Exp(-t * (2.2 + v.Frequency / 700)),
            "Electric piano" => Math.Exp(-t * (0.9 + v.Frequency / 1400)),
            _ => 0.6 * Math.Exp(-t * (0.6 + v.Frequency / 900)) + 0.4 * Math.Exp(-t * (3.5 + v.Frequency / 400))
        };
        if (v.Release >= 0)
            level *= Math.Exp(-(t - v.Release) * (instrument == "Organ" ? 30 : 9) * release);
        return level;
    }

    /// <summary>A sample of a voice: its harmonics, the higher ones fading faster.</summary>
    private static double Sample(Voice v, string instrument, double release)
    {
        var envelope = Envelope(v, instrument, release);
        if (envelope < 0.0002)
            return 0;
        var p = v.Phase * Math.Tau;
        var t = v.Time;
        double s;
        switch (instrument)
        {
            case "Organ":
                s = Math.Sin(p) + 0.5 * Math.Sin(2 * p) + 0.35 * Math.Sin(3 * p) + 0.25 * Math.Sin(4 * p) + 0.15 * Math.Sin(6 * p) + 0.1 * Math.Sin(8 * p);
                s *= 0.45;
                break;
            case "Electric piano":
                // A tine: the fundamental, a bell-like harmonic fading fast.
                s = Math.Sin(p + 0.9 * Math.Exp(-t * 6) * Math.Sin(p * 14)) * 0.8 + 0.2 * Math.Sin(2 * p) * Math.Exp(-t * 2);
                break;
            case "Harpsichord":
                s = 0;
                for (var h = 1; h <= 10; h++)
                    s += Math.Sin(h * p) / h * Math.Exp(-t * h * 0.6) * (h % 2 == 0 ? 0.8 : 1);
                s *= 0.55;
                break;
            default:
                // A piano: harmonics a little stretched (the stiffness of the strings), each fading at its pace.
                s = 0;
                double[] amplitudes = [1, 0.55, 0.32, 0.2, 0.12, 0.08, 0.05];
                for (var h = 1; h <= amplitudes.Length; h++)
                {
                    var stretch = 1 + 0.0004 * h * h;
                    s += amplitudes[h - 1] * Math.Sin(h * stretch * p) * Math.Exp(-t * (h - 1) * (0.8 + v.Frequency / 1200));
                }
                s *= 0.5;
                break;
        }
        return s * envelope;
    }
}

/// <summary>The keys of the piano: white and black, the ones down in the accent colour, their names and their keys of the computer.</summary>
public sealed partial class PianoKeyboard : Control
{
    private static readonly string[] Names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
    private static readonly bool[] IsBlack = [false, true, false, true, false, false, true, false, true, false, true, false];

    // The keys of the computer, by their place (QWERTY names, any layout): the lower row the first octave, the upper row the next one.
    private static readonly Key[] Lower = [Key.Z, Key.S, Key.X, Key.D, Key.C, Key.V, Key.G, Key.B, Key.H, Key.N, Key.J, Key.M, Key.Comma, Key.L, Key.Period, Key.Semicolon, Key.Slash];
    private static readonly Key[] Upper = [Key.Q, Key.Key2, Key.W, Key.Key3, Key.E, Key.R, Key.Key5, Key.T, Key.Key6, Key.Y, Key.Key7, Key.U, Key.I, Key.Key9, Key.O, Key.Key0, Key.P, Key.Bracketleft, Key.Equal, Key.Bracketright];

    private readonly PianoWindowViewModel _vm = null!;
    private readonly IPlayable _synth = null!;
    private readonly HashSet<int> _down = [];

    /// <summary>The knobs of a MIDI keyboard (controllers 21 to 28: 0 to 7, the value 0 to 127), when not the piano's.</summary>
    public Action<int, int>? Knobs { get; set; }

    /// <summary>The keyboard that plays the MIDI keyboard: the one opened or used last (/piano and /synth do not both sound).</summary>
    private static PianoKeyboard? _midiOwner;

    /// <summary>This keyboard takes the MIDI keyboard.</summary>
    public void TakeMidi() => _midiOwner = this;
    private readonly Dictionary<Key, int> _keysDown = [];
    private int? _mouseNote;
    private readonly List<string> _played = [];

    /// <summary>A pad hit (its number, its velocity 0 to 1): the body lights it.</summary>
    public event Action<int, double>? PadHit;

    /// <summary>The arpeggiator or the take changed (the body shows it).</summary>
    public event Action? StateChanged;

    // The arpeggiator: the notes held, played one after the other (eighths at the tempo).
    private readonly SortedSet<int> _arpHeld = [];
    private readonly Dictionary<int, double> _arpVelocity = [];
    private int _arpIndex = -1;
    private int? _arpNote;
    private double _arpTimer;

    // The take: what was played while REC was lit, played again in a loop by PLAY.
    private readonly record struct TakeEvent(double At, bool Pad, int Value, double Velocity, bool On);
    private readonly List<TakeEvent> _take = [];
    private readonly HashSet<int> _playDown = [];
    private double _takeLength;
    private double _recClock;
    private double _playClock;
    public bool Recording { get; private set; }
    public bool Playing { get; private set; }

    // Godot builds its scripts with this constructor.
    public PianoKeyboard()
    {
    }

    public PianoKeyboard(PianoWindowViewModel vm, IPlayable synth)
    {
        _vm = vm;
        _synth = synth;
        _midiOwner = this;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(600, 180);

        // An external MIDI keyboard: the inputs opened once for the chat (CoreMIDI on macOS cannot be opened again),
        // the keyboards looked for again every few seconds.
        if (!_midiOpened)
        {
            OS.OpenMidiInputs();
            _midiOpened = true;
        }
        CheckMidi();
        var check = new Timer { WaitTime = 3, Autostart = true };
        check.Timeout += CheckMidi;
        AddChild(check);
        Main.MidiReceived += OnMidi;
        TreeExiting += () =>
        {
            Main.MidiReceived -= OnMidi;
            foreach (var note in _down.ToList())
                _synth.NoteOff(note);
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.Octave))
            {
                foreach (var note in _down.ToList())
                    _synth.NoteOff(note);
                _down.Clear();
                _keysDown.Clear();
                _arpHeld.Clear();
                QueueRedraw();
            }
            if (e.PropertyName == nameof(_vm.Arp) && !_vm.Arp)
            {
                // The arpeggiator off: the notes still held sound as they are.
                foreach (var note in _arpHeld)
                    _synth.NoteOn(note, _arpVelocity.GetValueOrDefault(note, 0.8));
                _arpHeld.Clear();
            }
        };
    }

    private int First => 12 * (_vm.Octave + 1);

    /// <summary>The notes shown: three octaves and the C after them.</summary>
    private IEnumerable<int> Notes => Enumerable.Range(First, 37);

    private int WhiteCount => Notes.Count(n => !IsBlack[n % 12]);

    private float WhiteWidth => Size.X / WhiteCount;

    private Rect2 KeyRect(int note)
    {
        var whitesBefore = Enumerable.Range(First, note - First).Count(n => !IsBlack[n % 12]);
        var w = WhiteWidth;
        if (!IsBlack[note % 12])
            return new Rect2(whitesBefore * w, 0, w, Size.Y);
        var bw = w * 0.6f;
        return new Rect2(whitesBefore * w - bw / 2, 0, bw, Size.Y * 0.62f);
    }

    private int? NoteAt(Vector2 at)
    {
        foreach (var note in Notes.Where(n => IsBlack[n % 12]))
            if (KeyRect(note).HasPoint(at))
                return note;
        foreach (var note in Notes.Where(n => !IsBlack[n % 12]))
            if (KeyRect(note).HasPoint(at))
                return note;
        return null;
    }

    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Comma"] = ",", ["Period"] = ".", ["Semicolon"] = ";", ["Slash"] = "/", ["Bracketleft"] = "[", ["Bracketright"] = "]",
        ["Equal"] = "=", ["Minus"] = "-", ["Apostrophe"] = "'", ["Quoteleft"] = "`", ["Backslash"] = "\\",
        ["Quote"] = "'", ["QuoteDbl"] = "\"", ["Parenleft"] = "(", ["Parenright"] = ")", ["AsciiCircum"] = "^", ["Dollar"] = "$",
        ["Ampersand"] = "&", ["Exclam"] = "!", ["Section"] = "§", ["Asterisk"] = "*", ["Colon"] = ":", ["Less"] = "<", ["Greater"] = ">",
        ["Eacute"] = "é", ["Egrave"] = "è", ["Ccedilla"] = "ç", ["Agrave"] = "à", ["Ugrave"] = "ù", ["Numbersign"] = "#", ["Underscore"] = "_"
    };

    /// <summary>The key of the computer that plays <paramref name="note"/>, as the layout of this machine writes it.</summary>
    private string Shortcut(int note)
    {
        var index = note - First;
        Key? physical = index >= 0 && index < 12 ? Lower[index] : index - 12 >= 0 && index - 12 < Upper.Length ? Upper[index - 12] : null;
        if (physical is not { } key)
            return "";
        var name = OS.GetKeycodeString(DisplayServer.KeyboardGetKeycodeFromPhysical(key));
        return Symbols.GetValueOrDefault(name, name);
    }

    public override void _Draw()
    {
        var font = E.Body;
        foreach (var note in Notes.Where(n => !IsBlack[n % 12]))
        {
            var rect = KeyRect(note);
            var down = _down.Contains(note) || _arpNote == note;
            DrawRect(rect.Grow(-1), down ? E.Cyan.Lerp(Colors.White, 0.35f) : new Color(0.97f, 0.97f, 0.96f));
            DrawRect(rect.Grow(-1), new Color(0.2f, 0.2f, 0.22f), false, 1);
            var name = Names[note % 12] + (note / 12 - 1);
            DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 30), name, HorizontalAlignment.Center, rect.Size.X, 12,
                note % 12 == 0 ? new Color(0.15f, 0.15f, 0.18f) : new Color(0.45f, 0.45f, 0.5f));
            if (Shortcut(note) is { Length: > 0 } key)
                DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 12), key, HorizontalAlignment.Center, rect.Size.X, 11, new Color(0.55f, 0.55f, 0.6f));
        }
        foreach (var note in Notes.Where(n => IsBlack[n % 12]))
        {
            var rect = KeyRect(note);
            var down = _down.Contains(note) || _arpNote == note;
            DrawRect(rect, down ? E.Cyan : new Color(0.08f, 0.08f, 0.1f));
            DrawRect(new Rect2(rect.Position + new Vector2(3, 0), new Vector2(rect.Size.X - 6, rect.Size.Y - 8)), down ? E.Cyan.Lightened(0.2f) : new Color(0.18f, 0.18f, 0.2f));
            if (Shortcut(note) is { Length: > 0 } key)
                DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 12), key, HorizontalAlignment.Center, rect.Size.X, 10, new Color(0.7f, 0.7f, 0.75f));
        }
    }

    private void Press(int note, double velocity = 0.8, bool replayed = false)
    {
        if (!_down.Add(note))
            return;
        if (Recording && !replayed)
            _take.Add(new TakeEvent(_recClock, false, note, velocity, true));
        if (_vm.Arp && !replayed)
        {
            _arpHeld.Add(note);
            _arpVelocity[note] = velocity;
        }
        else
            _synth.NoteOn(note, velocity);
        _played.Add(Names[note % 12] + (note / 12 - 1));
        if (_played.Count > 24)
            _played.RemoveAt(0);
        _vm.Played = "♪ " + string.Join("  ", _played);
        QueueRedraw();
    }

    private void Release(int note, bool replayed = false)
    {
        if (!_down.Remove(note))
            return;
        if (Recording && !replayed)
            _take.Add(new TakeEvent(_recClock, false, note, 0, false));
        if (!_arpHeld.Remove(note))
            _synth.NoteOff(note);
        QueueRedraw();
    }

    /// <summary>A pad hit: its drum sounds, the body lights it, the take keeps it.</summary>
    public void HitPad(int pad, double velocity, bool replayed = false)
    {
        if (pad is < 0 or > 15)
            return;
        _synth.Hit(pad, velocity);
        if (Recording && !replayed)
            _take.Add(new TakeEvent(_recClock, true, pad, velocity, true));
        PadHit?.Invoke(pad, velocity);
    }

    /// <summary>REC: a new take begins (the old one forgotten), or ends (its notes still held closed).</summary>
    public void ToggleRecord()
    {
        if (Recording)
        {
            Recording = false;
            _takeLength = Math.Max(_recClock, 0.5);
            foreach (var note in _down)
                _take.Add(new TakeEvent(_takeLength - 0.001, false, note, 0, false));
        }
        else
        {
            StopPlaying();
            _take.Clear();
            _recClock = 0;
            Recording = true;
        }
        StateChanged?.Invoke();
    }

    /// <summary>PLAY: the take played again in a loop, or stopped (while recording: the take ends and plays).</summary>
    public void TogglePlay()
    {
        if (Playing)
            StopPlaying();
        else
        {
            if (Recording)
                ToggleRecord();
            if (_take.Count > 0)
            {
                Playing = true;
                _playClock = 0;
            }
        }
        StateChanged?.Invoke();
    }

    private void StopPlaying()
    {
        Playing = false;
        foreach (var note in _playDown.ToList())
            Release(note, true);
        _playDown.Clear();
    }

    public override void _Process(double delta)
    {
        Arpeggiate(delta);
        if (Recording)
            _recClock += delta;
        if (!Playing || _takeLength <= 0)
            return;
        var from = _playClock;
        _playClock += delta;
        Replay(from, Math.Min(_playClock, _takeLength));
        if (_playClock >= _takeLength)
        {
            _playClock -= _takeLength;
            Replay(0, _playClock);
        }
    }

    private void Replay(double from, double to)
    {
        foreach (var e in _take.Where(e => e.At >= from && e.At < to).ToList())
        {
            if (e.Pad)
                HitPad(e.Value, e.Velocity, true);
            else if (e.On)
            {
                Press(e.Value, e.Velocity, true);
                _playDown.Add(e.Value);
            }
            else
            {
                Release(e.Value, true);
                _playDown.Remove(e.Value);
            }
        }
    }

    private void Arpeggiate(double delta)
    {
        if (!_vm.Arp || _arpHeld.Count == 0)
        {
            if (_arpNote is { } last)
            {
                _synth.NoteOff(last);
                _arpNote = null;
                QueueRedraw();
            }
            _arpTimer = 0;
            _arpIndex = -1;
            return;
        }
        var step = 60.0 / Math.Max(30, _vm.Tempo) / 2;
        _arpTimer -= delta;
        // Each note held for 70 % of its step.
        if (_arpNote is { } current && _arpTimer < step * 0.3)
        {
            _synth.NoteOff(current);
            _arpNote = null;
            QueueRedraw();
        }
        if (_arpTimer > 0)
            return;
        var notes = _arpHeld.ToList();
        _arpIndex = (_arpIndex + 1) % notes.Count;
        var note = notes[_arpIndex];
        _synth.NoteOn(note, _arpVelocity.GetValueOrDefault(note, 0.8));
        _arpNote = note;
        _arpTimer = Math.Max(_arpTimer + step, step * 0.5);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                AcceptEvent();
                if (button.Pressed)
                {
                    GrabFocus();
                    if (NoteAt(button.Position) is { } note)
                    {
                        _mouseNote = note;
                        Press(note);
                    }
                }
                else if (_mouseNote is { } held)
                {
                    Release(held);
                    _mouseNote = null;
                }
                break;
            case InputEventMouseMotion motion when _mouseNote is { } current && (motion.ButtonMask & MouseButtonMask.Left) != 0:
                // A glissando: dragged over the keys.
                if (NoteAt(motion.Position) is { } over && over != current)
                {
                    Release(current);
                    _mouseNote = over;
                    Press(over);
                }
                break;
            case InputEventKey key when !key.Echo:
                var physical = key.PhysicalKeycode;
                if (physical == Key.Space)
                {
                    _vm.Sustain = key.Pressed;
                    AcceptEvent();
                    return;
                }
                if (key.Pressed)
                {
                    var index = Array.IndexOf(Lower, physical) is var l and >= 0 && l < 12 ? l : Array.IndexOf(Upper, physical) is var u and >= 0 ? u + 12 : -1;
                    if (index < 0)
                        return;
                    var note = First + index;
                    _keysDown[physical] = note;
                    Press(note);
                    AcceptEvent();
                }
                else if (_keysDown.Remove(physical, out var note))
                {
                    Release(note);
                    AcceptEvent();
                }
                break;
        }
    }

    /// <summary>True once the MIDI inputs are opened (once for the chat).</summary>
    private static bool _midiOpened;

    private string[] _midiInputs = [];

    /// <summary>A port that drives a music program rather than playing (the "DAW" port of a Launchkey, of an Akai…).</summary>
    private static bool IsControlPort(string name) =>
        name.Contains("DAW", StringComparison.OrdinalIgnoreCase) || name.Contains("Control", StringComparison.OrdinalIgnoreCase);

    /// <summary>The MIDI keyboards plugged in (said in the journal when they change).</summary>
    private void CheckMidi()
    {
        var inputs = OS.GetConnectedMidiInputs();
        if (!inputs.SequenceEqual(_midiInputs))
        {
            _midiInputs = inputs;
            KAONOMI.Chat.Services.ActivityLog.Info("Piano", inputs.Length == 0 ? "No MIDI keyboard" : "MIDI keyboard(s): " + string.Join(", ", inputs));
        }
        var played = inputs.Where(name => !IsControlPort(name)).ToList();
        _vm.HasMidi = played.Count > 0;
        _vm.Midi = played.Count == 0 ? "no MIDI keyboard (one plugged in once the chat runs: start the chat again) · click the keys, or the keys of the computer"
            : "🎹 MIDI: " + string.Join(", ", played);
    }

    /// <summary>A message of a MIDI keyboard: a note down (its velocity) or up, the sustain pedal (controller 64).</summary>
    private void OnMidi(InputEventMidi midi)
    {
        if (!IsInstanceValid(this) || !IsVisibleInTree())
            return;
        if (_midiOwner != this && _midiOwner is not null && IsInstanceValid(_midiOwner) && _midiOwner.IsVisibleInTree())
            return;
        var port = midi.Device >= 0 && midi.Device < _midiInputs.Length ? _midiInputs[midi.Device] : "?";
        // The port of a keyboard that drives a music program (DAW: its pads, its transport) does not play.
        if (IsControlPort(port))
        {
            Seen(port, midi, false);
            return;
        }
        switch (midi.Message)
        {
            // The pads in drum mode: channel 10.
            case MidiMessage.NoteOn when midi.Channel == 9:
                if (midi.Velocity > 0)
                    HitPad(DrumPad(midi.Pitch), 0.3 + 0.7 * midi.Velocity / 127.0);
                break;
            case MidiMessage.NoteOff when midi.Channel == 9:
                break;
            case MidiMessage.PitchBend:
                _vm.PitchBend = Math.Clamp((midi.Pitch - 8192) / 8192.0, -1, 1);
                break;
            case MidiMessage.ControlChange when midi.ControllerNumber == 1:
                _vm.Modulation = midi.ControllerValue / 127.0;
                break;
            case MidiMessage.ControlChange when midi.ControllerNumber is >= 21 and <= 28 && Knobs is not null:
                Knobs(midi.ControllerNumber - 21, midi.ControllerValue);
                break;
            case MidiMessage.ControlChange when midi.ControllerNumber is >= 21 and <= 28:
            {
                var knob = PianoKnob.All[midi.ControllerNumber - 21];
                knob.Set(_vm, knob.Min + (int)Math.Round((knob.Max - knob.Min) * midi.ControllerValue / 127.0));
                break;
            }
            case MidiMessage.NoteOn when midi.Velocity > 0:
                Press(midi.Pitch, 0.25 + 0.75 * midi.Velocity / 127.0);
                break;
            case MidiMessage.NoteOn:
            case MidiMessage.NoteOff:
                Release(midi.Pitch);
                break;
            case MidiMessage.ControlChange when midi.ControllerNumber == 64:
                _vm.Sustain = midi.ControllerValue >= 64;
                break;
            default:
                Seen(port, midi, true);
                break;
        }
    }

    /// <summary>The pad of a drum note: the Novation layout (bottom row 36–39 44–47, top row 40–43 48–51), -1 for none.</summary>
    private static int DrumPad(int note)
    {
        int[] top = [40, 41, 42, 43, 48, 49, 50, 51];
        int[] bottom = [36, 37, 38, 39, 44, 45, 46, 47];
        return Array.IndexOf(top, note) is var t and >= 0 ? t : Array.IndexOf(bottom, note) is var b and >= 0 ? 8 + b : -1;
    }

    private static readonly HashSet<string> _seen = [];

    /// <summary>A MIDI message not played, said once in the journal (to know what a keyboard sends).</summary>
    private static void Seen(string port, InputEventMidi midi, bool played)
    {
        var what = $"{port}: {midi.Message} channel {midi.Channel + 1}" + (midi.Message == MidiMessage.ControlChange ? $" controller {midi.ControllerNumber}" : "");
        if (_seen.Count < 200 && _seen.Add(what))
            KAONOMI.Chat.Services.ActivityLog.Info("Piano", (played ? "MIDI not used: " : "MIDI left aside: ") + what);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
        if (what == NotificationFocusEnter)
            _midiOwner = this;
        // The focus lost (another window): the keys held stop.
        if (what == NotificationFocusExit)
        {
            foreach (var note in _keysDown.Values.ToList())
                Release(note);
            _keysDown.Clear();
        }
    }
}
