using System;
using System.Collections.Generic;

namespace KAONOMI.Chat.Services;

/// <summary>
/// A shout heard: its level and the level usual before it (RMS, full scale 1), and how long it had lasted when it was
/// known, in seconds.
/// </summary>
public readonly record struct Shout(double Level, double Usual, double Seconds);

/// <summary>
/// Hears a shout in what the microphone hears: the level rises at once, far over what was usual, and stays there a
/// moment. The live chat asks it while the assistant reads an answer aloud, to stop the voice.
/// <para>
/// The level is measured every <see cref="WindowSeconds"/>. What is usual is the loudest of two levels: the one of the
/// last two seconds (the room, and a level that lasts: after two seconds a loud sound is the new usual, no longer a
/// shout), and the one at which the microphone hears the voice of the assistant itself (its loud moments, learnt
/// while it plays and kept from an answer to the next: the loudspeakers are heard by the microphone, and the voice
/// that starts again after a pause is no shout). A window is loud at <see cref="Ratio"/> times the usual and at
/// <see cref="Floor"/> at least; a shout is four loud windows among the last six (0.2 s: a key struck, a knock on the
/// table are too short).
/// </para>
/// <para>
/// Nothing is a shout before one second of the voice of the assistant was heard (the first second of the first answer
/// read): until then its level in the microphone is not known, and it could be taken for a shout itself.
/// </para>
/// </summary>
public sealed class ShoutDetector
{
    /// <summary>The level is measured over this long, in seconds.</summary>
    public const double WindowSeconds = 0.05;

    /// <summary>A shout is this many times louder than what was usual (2.5 times the level: +8 dB).</summary>
    public const double Ratio = 2.5;

    /// <summary>Under this level (RMS, full scale 1: −24 dB) nothing is a shout, however quiet the room is.</summary>
    public const double Floor = 0.06;

    /// <summary>The voice of the assistant counts as playing over this level of <see cref="SpeechService.Level"/>.</summary>
    public const float Audible = 0.02f;

    /// <summary>A shout: this many loud windows…</summary>
    private const int Needed = 4;

    /// <summary>…among the last ones, this many.</summary>
    private const int Span = 6;

    /// <summary>What is usual: the last two seconds.</summary>
    private const int UsualWindows = 40;

    /// <summary>The voice of the assistant as the microphone hears it: its last ten seconds.</summary>
    private const int EchoWindows = 200;

    /// <summary>The voice of the assistant is known once a second of it was heard.</summary>
    private const int EchoKnown = 20;

    /// <summary>Most of the voice of the assistant is under this share of its windows (what is learnt of it is compared with it).</summary>
    private const double Most = 0.7;

    /// <summary>
    /// A window this many times over <see cref="Most"/> of the voice is not the voice (its loud moments are 1.6 times
    /// over it: a shout is over this, the voice turned up to twice its level is under).
    /// </summary>
    private const double Unlike = 4;

    /// <summary>The voice still sounds in the room (and in the player) this many windows after its level fell to nothing.</summary>
    private const int PlayingHold = 6;

    private readonly int _window;
    private double _sum;
    private int _filled;
    private readonly Queue<double> _recent = new();
    private readonly Queue<double> _echo = new();
    private readonly Queue<bool> _last = new();
    private int _loudRun;
    private int _quietRun;
    private bool _told;
    private int _sincePlaying = PlayingHold + 1;

    public ShoutDetector(int rate) => _window = Math.Max(1, (int)(Math.Max(8000, rate) * WindowSeconds));

    /// <summary>The level of the last window heard.</summary>
    public double Level { get; private set; }

    /// <summary>What was usual when the last window was heard.</summary>
    public double Usual { get; private set; }

    /// <summary>The level from which the last window was loud.</summary>
    public double Threshold { get; private set; } = Floor;

    /// <summary>The level at which the microphone hears the voice of the assistant; null while it is not known.</summary>
    public double? Echo => _echo.Count >= EchoKnown ? Percentile(_echo) : null;

    /// <summary>True while what is heard is loud (the shout lasts).</summary>
    public bool Loud { get; private set; }

    /// <summary>
    /// Takes the next <paramref name="samples"/> of the microphone, <paramref name="playing"/> telling whether the voice
    /// of the assistant sounds meanwhile; returns the shout they complete (once per shout), else null.
    /// </summary>
    public Shout? Add(ReadOnlySpan<float> samples, bool playing)
    {
        Shout? shout = null;
        foreach (var sample in samples)
        {
            _sum += sample * sample;
            if (++_filled < _window)
                continue;
            var level = Math.Sqrt(_sum / _window);
            _sum = 0;
            _filled = 0;
            if (Window(level, playing) is { } heard)
                shout = heard;
        }
        return shout;
    }

    private Shout? Window(double level, bool playing)
    {
        _sincePlaying = playing ? 0 : Math.Min(_sincePlaying + 1, PlayingHold + 1);
        var voiced = _sincePlaying <= PlayingHold;
        var echo = Echo;
        Level = level;
        Usual = Math.Max(Percentile(_recent), echo ?? 0);
        Threshold = Math.Max(Floor, Ratio * Usual);
        // The level of the voice in the microphone is not known yet: it is learnt, nothing is a shout.
        Loud = level >= Threshold && echo is not null;
        _loudRun = Loud ? _loudRun + 1 : 0;
        _quietRun = Loud ? 0 : _quietRun + 1;
        // The shout has ended once nothing was loud for the length of the count: the next one is another shout.
        if (_quietRun >= Span)
            _told = false;

        // What is not loud is usual; so is what stays loud longer than two seconds.
        if (!Loud || _loudRun > UsualWindows)
        {
            Keep(_recent, level, UsualWindows);
            // The voice of the assistant is learnt from what is like it: far over most of what was heard of it, a
            // window is something else (a shout not told as one, because one before it was taken for the voice while
            // it was learnt: it would be taken for the voice too, and so would the next ones).
            if (voiced && (echo is null || level < Unlike * Percentile(_echo, Most) || _loudRun > UsualWindows))
                Keep(_echo, level, EchoWindows);
        }

        _last.Enqueue(Loud);
        if (_last.Count > Span)
            _last.Dequeue();
        if (!Loud || _told)
            return null;
        var loud = 0;
        foreach (var was in _last)
            if (was)
                loud++;
        if (loud < Needed)
            return null;
        // Told once, however long it lasts.
        _told = true;
        return new Shout(level, Usual, loud * WindowSeconds);
    }

    private static void Keep(Queue<double> levels, double level, int most)
    {
        levels.Enqueue(level);
        while (levels.Count > most)
            levels.Dequeue();
    }

    /// <summary>The level under which a share of the windows is (nine in ten: the loud moments, without the loudest ones).</summary>
    private static double Percentile(Queue<double> levels, double share = 0.9)
    {
        if (levels.Count == 0)
            return 0;
        var sorted = levels.ToArray();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * share) - 1, 0, sorted.Length - 1)];
    }
}
