using CommunityToolkit.Mvvm.ComponentModel;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The piano (<c>/piano</c>): a keyboard played with the mouse (or the keys of the computer), its sound made as it is
/// played; an external MIDI keyboard plugged in plays it too. What the window keeps: the instrument, the octave of the
/// keyboard, the volume, the sustain pedal.
/// </summary>
public sealed partial class PianoWindowViewModel : ViewModelBase
{
    public static readonly string[] Instruments = ["Piano", "Electric piano", "Organ", "Harpsichord"];

    /// <summary>The sounds of the sixteen pads, the top row first (the bottom row, closer to the hands, the kit).</summary>
    public static readonly string[] PadNames =
    [
        "Clap", "Rim", "Shaker", "Ride", "Cowbell", "Block", "808", "Zap",
        "Kick", "Snare", "Hat", "Open hat", "Low tom", "Mid tom", "High tom", "Crash"
    ];

    [ObservableProperty]
    private string _instrument = "Piano";

    /// <summary>The octave of the first key (C3 by default): the keyboard shows three octaves from it.</summary>
    [ObservableProperty]
    private int _octave = 3;

    /// <summary>0 to 100.</summary>
    [ObservableProperty]
    private int _volume = 70;

    /// <summary>True while the sustain pedal is down (the notes ring on once the keys are released).</summary>
    [ObservableProperty]
    private bool _sustain;

    /// <summary>The MIDI keyboards plugged in (their names), or why there is none.</summary>
    [ObservableProperty]
    private string _midi = "";

    /// <summary>True when a MIDI keyboard is plugged in.</summary>
    [ObservableProperty]
    private bool _hasMidi;

    /// <summary>The brightness of the sound, 0 (muffled) to 100 (bright): a low-pass filter on the mix (knob 2).</summary>
    [ObservableProperty]
    private int _tone = 80;

    /// <summary>How long the notes ring once released, 0 to 100 (knob 3).</summary>
    [ObservableProperty]
    private int _release = 25;

    /// <summary>The depth of the vibrato, 0 to 100 (knob 4; the MOD strip adds to it).</summary>
    [ObservableProperty]
    private int _vibrato;

    /// <summary>The level of the echo, 0 to 100 (knob 5).</summary>
    [ObservableProperty]
    private int _echo;

    /// <summary>The time of the echo, 0 (50 ms) to 100 (800 ms) (knob 6).</summary>
    [ObservableProperty]
    private int _echoTime = 40;

    /// <summary>The volume of the drums of the pads, 0 to 100 (knob 7).</summary>
    [ObservableProperty]
    private int _drums = 80;

    /// <summary>The tempo of the arpeggiator, in beats per minute (knob 8).</summary>
    [ObservableProperty]
    private int _tempo = 120;

    /// <summary>True while the arpeggiator plays the notes held one after the other.</summary>
    [ObservableProperty]
    private bool _arp;

    /// <summary>The PITCH strip (or the pitch bend of a MIDI keyboard): -1 to 1, two semitones down or up.</summary>
    [ObservableProperty]
    private double _pitchBend;

    /// <summary>The MOD strip (or the modulation of a MIDI keyboard): 0 to 1, a vibrato.</summary>
    [ObservableProperty]
    private double _modulation;

    /// <summary>The notes played last (their names), shown under the keyboard.</summary>
    [ObservableProperty]
    private string _played = "";
}
