using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KAONOMI.Chat.Models;

/// <summary>
/// An operator of the FM synth (<c>/synth</c>, after the six operators of the Yamaha DX7): a sine wave whose frequency
/// follows the note (a ratio: coarse, fine, detune) or not (fixed), its output level, its envelope (four rates, four
/// levels: L4 the level it starts from and goes back to, L1 L2 reached in turn, L3 held while the key is down), how it
/// follows the velocity, the LFO (amplitude), the keyboard (rates faster up the keys, levels scaled on both sides of a
/// break point).
/// </summary>
public sealed partial class FmOperator : ObservableObject
{
    public static readonly string[] Modes = ["Ratio", "Fixed"];
    public static readonly string[] Curves = ["-LIN", "-EXP", "+EXP", "+LIN"];

    [ObservableProperty] private bool _on = true;
    [ObservableProperty] private string _mode = "Ratio";
    /// <summary>0 to 31 (0: ×0.5); fixed: 0 to 3 (1, 10, 100, 1000 Hz).</summary>
    [ObservableProperty] private int _coarse = 1;
    [ObservableProperty] private int _fine;
    /// <summary>-7 to 7 (a few cents).</summary>
    [ObservableProperty] private int _detune;
    [ObservableProperty] private int _level;
    /// <summary>0 to 7.</summary>
    [ObservableProperty] private int _velocity = 2;
    /// <summary>0 to 3: how much the LFO (AMD) moves its level.</summary>
    [ObservableProperty] private int _ampMod;
    /// <summary>0 to 7: the envelope faster up the keyboard.</summary>
    [ObservableProperty] private int _rateScale;
    [ObservableProperty] private int _r1 = 99;
    [ObservableProperty] private int _r2 = 99;
    [ObservableProperty] private int _r3 = 99;
    [ObservableProperty] private int _r4 = 99;
    [ObservableProperty] private int _l1 = 99;
    [ObservableProperty] private int _l2 = 99;
    [ObservableProperty] private int _l3 = 99;
    [ObservableProperty] private int _l4;
    /// <summary>The break point of the level scaling, a MIDI note (39: D♯2… 60: C4).</summary>
    [ObservableProperty] private int _breakPoint = 60;
    [ObservableProperty] private int _leftDepth;
    [ObservableProperty] private int _rightDepth;
    [ObservableProperty] private string _leftCurve = "-LIN";
    [ObservableProperty] private string _rightCurve = "-LIN";

    [JsonIgnore]
    public bool Fixed => Mode == "Fixed";

    /// <summary>Its ratio to the note (ratio mode).</summary>
    [JsonIgnore]
    public double Ratio => (Coarse == 0 ? 0.5 : Coarse) * (1 + Fine / 100.0) * Math.Pow(2, Detune * 1.2 / 1200);

    /// <summary>Its frequency (fixed mode): 1, 10, 100 or 1000 Hz, ×10 at most by the fine.</summary>
    [JsonIgnore]
    public double FixedHz => Math.Pow(10, (Coarse & 3) + Fine / 100.0);

    /// <summary>What its frequency is, said.</summary>
    [JsonIgnore]
    public string FrequencyText => Fixed ? (FixedHz >= 1000 ? $"{FixedHz / 1000:0.00} kHz" : $"{FixedHz:0.##} Hz") : $"×{Ratio:0.00}";

    public int Rate(int stage) => stage switch { 0 => R1, 1 => R2, 2 => R3, _ => R4 };
    public int LevelAt(int stage) => stage switch { 0 => L1, 1 => L2, 2 => L3, _ => L4 };

    /// <summary>The output level for <paramref name="note"/>, once scaled on its side of the break point (0 to 99).</summary>
    public double ScaledLevel(int note)
    {
        var octaves = (note - BreakPoint) / 12.0;
        var depth = octaves < 0 ? LeftDepth : RightDepth;
        var curve = octaves < 0 ? LeftCurve : RightCurve;
        var distance = Math.Min(Math.Abs(octaves), 5);
        var amount = curve.EndsWith("EXP") ? (Math.Pow(2, distance) - 1) / 31 * depth : distance / 5 * depth;
        return Math.Clamp(Level + (curve.StartsWith('+') ? amount : -amount), 0, 99);
    }
}

/// <summary>A sound of the FM synth: its six operators, how they are set together (the algorithm), the LFO, the pitch envelope.</summary>
public sealed partial class FmPatch : ObservableObject
{
    public static readonly string[] Waves = ["Triangle", "Saw down", "Saw up", "Square", "Sine", "S&H"];

    [ObservableProperty] private string _name = "INIT VOICE";
    /// <summary>1 to 32.</summary>
    [ObservableProperty] private int _algorithm = 1;
    /// <summary>0 to 7.</summary>
    [ObservableProperty] private int _feedback;
    /// <summary>-24 to 24 semitones.</summary>
    [ObservableProperty] private int _transpose;
    /// <summary>0 to 12 semitones.</summary>
    [ObservableProperty] private int _bendRange = 2;
    [ObservableProperty] private string _lfoWave = "Triangle";
    [ObservableProperty] private int _lfoSpeed = 35;
    [ObservableProperty] private int _lfoDelay;
    /// <summary>Pitch modulation depth (vibrato), 0 to 99.</summary>
    [ObservableProperty] private int _pmd;
    /// <summary>Amplitude modulation depth (tremolo, by the operators' AM sensitivity), 0 to 99.</summary>
    [ObservableProperty] private int _amd;
    [ObservableProperty] private bool _lfoSync = true;
    /// <summary>Pitch modulation sensitivity, 0 to 7.</summary>
    [ObservableProperty] private int _pms = 3;
    [ObservableProperty] private int _pr1 = 99;
    [ObservableProperty] private int _pr2 = 99;
    [ObservableProperty] private int _pr3 = 99;
    [ObservableProperty] private int _pr4 = 99;
    [ObservableProperty] private int _pl1 = 50;
    [ObservableProperty] private int _pl2 = 50;
    [ObservableProperty] private int _pl3 = 50;
    [ObservableProperty] private int _pl4 = 50;

    /// <summary>OP1 to OP6.</summary>
    public List<FmOperator> Operators { get; set; } = [new(), new(), new(), new(), new(), new()];

    public int PitchRate(int stage) => stage switch { 0 => Pr1, 1 => Pr2, 2 => Pr3, _ => Pr4 };
    public int PitchLevel(int stage) => stage switch { 0 => Pl1, 1 => Pl2, 2 => Pl3, _ => Pl4 };

    /// <summary>Takes every value of <paramref name="other"/> (the same objects stay: what is bound to them follows).</summary>
    public void CopyFrom(FmPatch other)
    {
        Copy(other, this);
        for (var i = 0; i < 6; i++)
            Copy(other.Operators[i], Operators[i]);
    }

    private static void Copy(object from, object to)
    {
        foreach (var property in from.GetType().GetProperties().Where(p => p.CanWrite && p.CanRead && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string))))
            property.SetValue(to, property.GetValue(from));
    }
}

/// <summary>
/// The 32 algorithms (after the DX7): which operator modulates which, the carriers (heard), the feedback (an operator
/// fed by its own output, or 4 → 6 and 5 → 6 in the algorithms 4 and 6). A modulator always has a higher number than the
/// operator it modulates: OP6 is computed first, OP1 last.
/// </summary>
public static class FmAlgorithms
{
    public sealed record Algorithm(int Number, int[][] ModulatedBy, bool[] Carrier, int FeedbackFrom, int FeedbackTo)
    {
        /// <summary>The operators that <paramref name="op"/> modulates (0-based).</summary>
        public IEnumerable<int> Targets(int op) => Enumerable.Range(0, 6).Where(t => ModulatedBy[t].Contains(op));
    }

    private static readonly string[] Table =
    [
        "6>5 5>4 4>3 2>1|1 3|6>6", "6>5 5>4 4>3 2>1|1 3|2>2", "6>5 5>4 3>2 2>1|1 4|6>6", "6>5 5>4 3>2 2>1|1 4|4>6",
        "6>5 4>3 2>1|1 3 5|6>6", "6>5 4>3 2>1|1 3 5|5>6", "6>5 5>3 4>3 2>1|1 3|6>6", "6>5 5>3 4>3 2>1|1 3|4>4",
        "6>5 5>3 4>3 2>1|1 3|2>2", "6>4 5>4 3>2 2>1|1 4|3>3", "6>4 5>4 3>2 2>1|1 4|6>6", "6>3 5>3 4>3 2>1|1 3|2>2",
        "6>3 5>3 4>3 2>1|1 3|6>6", "6>4 5>4 4>3 2>1|1 3|6>6", "6>4 5>4 4>3 2>1|1 3|2>2", "6>5 5>1 4>3 3>1 2>1|1|6>6",
        "6>5 5>1 4>3 3>1 2>1|1|2>2", "6>5 5>4 4>1 3>1 2>1|1|3>3", "6>5 6>4 3>2 2>1|1 4 5|6>6", "6>4 5>4 3>2 3>1|1 2 4|3>3",
        "6>5 6>4 3>2 3>1|1 2 4 5|3>3", "6>5 6>4 6>3 2>1|1 3 4 5|6>6", "6>5 6>4 3>2|1 2 4 5|6>6", "6>5 6>4 6>3|1 2 3 4 5|6>6",
        "6>5 6>4|1 2 3 4 5|6>6", "6>4 5>4 3>2|1 2 4|6>6", "6>4 5>4 3>2|1 2 4|3>3", "5>4 4>3 2>1|1 3 6|5>5",
        "6>5 4>3|1 2 3 5|6>6", "5>4 4>3|1 2 3 6|5>5", "6>5|1 2 3 4 5|6>6", "|1 2 3 4 5 6|6>6"
    ];

    public static readonly Algorithm[] All = [.. Table.Select(Parse)];

    public static Algorithm Get(int number) => All[Math.Clamp(number, 1, 32) - 1];

    private static Algorithm Parse(string text, int index)
    {
        var parts = text.Split('|');
        var modulatedBy = Enumerable.Range(0, 6).Select(_ => new List<int>()).ToArray();
        foreach (var link in parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            modulatedBy[link[2] - '1'].Add(link[0] - '1');
        var carrier = new bool[6];
        foreach (var c in parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            carrier[int.Parse(c) - 1] = true;
        return new Algorithm(index + 1, [.. modulatedBy.Select(l => l.ToArray())], carrier, parts[2][0] - '1', parts[2][2] - '1');
    }
}

/// <summary>The sounds given with the synth (made for it, after the classic sounds of FM synthesis).</summary>
public static class FmPresets
{
    private static FmOperator Op(int coarse, int level, int[] r, int[] l, int velocity = 2, int fine = 0, int detune = 0, int ampMod = 0, int rateScale = 2) => new()
    {
        Coarse = coarse, Level = level, Fine = fine, Detune = detune, Velocity = velocity, AmpMod = ampMod, RateScale = rateScale,
        R1 = r[0], R2 = r[1], R3 = r[2], R4 = r[3], L1 = l[0], L2 = l[1], L3 = l[2], L4 = l[3]
    };

    private static FmPatch Patch(string name, int algorithm, int feedback, params FmOperator[] ops)
    {
        var patch = new FmPatch { Name = name, Algorithm = algorithm, Feedback = feedback };
        for (var i = 0; i < 6; i++)
            patch.Operators[i] = ops[i];
        return patch;
    }

    private static readonly int[] Organ = [99, 99, 99, 99];
    private static readonly int[] OrganLevels = [99, 99, 99, 0];

    public static FmPatch Init() => Patch("INIT VOICE", 1, 0,
        Op(1, 99, Organ, OrganLevels), Op(1, 0, Organ, OrganLevels), Op(1, 0, Organ, OrganLevels),
        Op(1, 0, Organ, OrganLevels), Op(1, 0, Organ, OrganLevels), Op(1, 0, Organ, OrganLevels));

    public static IReadOnlyList<FmPatch> All() =>
    [
        Init(),
        Patch("E.PIANO", 5, 6,
            Op(1, 99, [96, 25, 25, 67], [99, 75, 0, 0], 2), Op(14, 58, [95, 50, 35, 78], [99, 0, 0, 0], 7),
            Op(1, 99, [95, 20, 20, 50], [99, 95, 0, 0], 2), Op(1, 89, [95, 29, 20, 50], [99, 95, 0, 0], 6),
            Op(1, 79, [95, 20, 20, 50], [99, 95, 0, 0], 0, detune: 3), Op(1, 79, [95, 29, 20, 50], [99, 95, 0, 0], 6, detune: -3)),
        Patch("TUBULAR BELL", 5, 7,
            Op(1, 99, [99, 30, 22, 35], [99, 70, 0, 0], 2), Op(3, 80, [99, 33, 25, 35], [99, 55, 0, 0], 4, fine: 50),
            Op(1, 92, [99, 28, 20, 35], [99, 60, 0, 0], 2, detune: 5), Op(3, 78, [99, 30, 24, 35], [99, 50, 0, 0], 4, fine: 50, detune: -4),
            Op(2, 80, [99, 26, 18, 35], [99, 55, 0, 0], 2), Op(7, 62, [99, 40, 30, 35], [99, 30, 0, 0], 5)),
        Patch("BRASS", 22, 7,
            Op(1, 99, [72, 76, 99, 71], [99, 88, 96, 0], 1), Op(1, 82, [62, 51, 29, 71], [82, 95, 96, 0], 3),
            Op(1, 98, [72, 76, 99, 71], [99, 88, 96, 0], 1, detune: 2), Op(1, 97, [72, 76, 99, 71], [99, 88, 96, 0], 1, detune: -2),
            Op(1, 96, [72, 76, 99, 71], [99, 88, 96, 0], 1, detune: 1), Op(1, 84, [64, 40, 45, 71], [99, 92, 90, 0], 3)),
        Patch("SOLID BASS", 16, 5,
            Op(0, 99, [99, 40, 30, 60], [99, 80, 0, 0], 2), Op(0, 82, [99, 55, 30, 60], [99, 50, 0, 0], 5),
            Op(1, 76, [99, 70, 30, 60], [99, 20, 0, 0], 5), Op(3, 58, [99, 80, 30, 60], [99, 0, 0, 0], 6),
            Op(1, 72, [99, 50, 30, 60], [99, 60, 0, 0], 4), Op(2, 66, [99, 60, 30, 60], [99, 30, 0, 0], 4)),
        Patch("DRAWBAR ORGAN", 32, 0,
            Op(0, 90, Organ, OrganLevels, 0), Op(1, 99, Organ, OrganLevels, 0), Op(2, 90, Organ, OrganLevels, 0),
            Op(3, 82, Organ, OrganLevels, 0), Op(4, 78, Organ, OrganLevels, 0), Op(6, 72, Organ, OrganLevels, 0)),
        StringPatch(),
        Patch("MARIMBA", 7, 3,
            Op(1, 99, [99, 45, 30, 45], [99, 0, 0, 0], 3), Op(4, 72, [99, 75, 40, 50], [99, 0, 0, 0], 6),
            Op(1, 92, [99, 50, 30, 45], [99, 0, 0, 0], 3, detune: 2), Op(10, 58, [99, 85, 40, 50], [99, 0, 0, 0], 6),
            Op(3, 55, [99, 70, 40, 50], [99, 0, 0, 0], 5), Op(7, 48, [99, 80, 40, 50], [99, 0, 0, 0], 5)),
        Patch("SYNC LEAD", 1, 6,
            Op(1, 99, [85, 50, 40, 60], [99, 90, 85, 0], 2), Op(1, 80, [80, 45, 40, 60], [99, 80, 70, 0], 3, detune: 4),
            Op(1, 99, [85, 50, 40, 60], [99, 90, 85, 0], 2, detune: -4), Op(2, 78, [80, 40, 35, 60], [99, 75, 65, 0], 3),
            Op(1, 74, [75, 35, 35, 60], [99, 70, 60, 0], 3), Op(1, 70, [70, 30, 30, 60], [99, 80, 70, 0], 3))
    ];

    private static FmPatch StringPatch()
    {
        var patch = Patch("STRINGS", 2, 4,
            Op(1, 99, [45, 30, 20, 40], [99, 92, 90, 0], 1, ampMod: 1), Op(1, 74, [40, 30, 20, 40], [85, 80, 78, 0], 2),
            Op(1, 97, [42, 30, 20, 40], [99, 92, 90, 0], 1, detune: 4, ampMod: 1), Op(1, 68, [38, 28, 20, 40], [80, 78, 75, 0], 2),
            Op(3, 60, [36, 28, 20, 40], [78, 74, 70, 0], 2, detune: -3), Op(1, 62, [40, 30, 20, 40], [80, 78, 75, 0], 2));
        patch.LfoSpeed = 34;
        patch.LfoDelay = 33;
        patch.Pmd = 6;
        patch.Amd = 4;
        patch.Pms = 3;
        return patch;
    }
}
