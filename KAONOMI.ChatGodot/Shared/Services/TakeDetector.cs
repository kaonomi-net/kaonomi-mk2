using System;
using System.Collections.Generic;

namespace KAONOMI.Chat.Services;

/// <summary>
/// Where someone speaks in a take of MY VOICE, in a noisy room. The level is measured by frames of 20 ms on the sound
/// with its low frequencies taken away (under <see cref="DetectionHighPass"/> Hz: the hum of a computer, a fan, the
/// mains, which a microphone hears as loud as a quiet voice); the noise of the room is learnt on the first frames (the
/// moment before one speaks) and followed down; a frame is speech when it is <see cref="SpeechRatio"/> times louder
/// than that noise (6 dB). Speech has begun after 100 ms of it; it has ended after <see cref="EndSeconds"/> without it,
/// once half a second was said. The take is then the speech, with a little before and after it.
/// </summary>
public sealed class TakeDetector
{
    private const double FrameSeconds = 0.02;

    /// <summary>The frequencies under this are not measured (the hum of the room).</summary>
    public const double DetectionHighPass = 120;

    /// <summary>Louder than the noise by this (6 dB): speech.</summary>
    private const float SpeechRatio = 2f;

    /// <summary>Under this level (RMS of the sound filtered) a frame is never speech.</summary>
    private const float MinimumRms = 0.0002f;

    /// <summary>The noise is learnt on the first frames (before one speaks).</summary>
    private const int LearnFrames = 10;

    /// <summary>The silence that ends what is said, and the least said before it can end.</summary>
    public const double EndSeconds = 1.0;

    private const double MinSpeechSeconds = 0.5;

    /// <summary>Kept before the first frame of speech and after the last one.</summary>
    private const double Before = 0.25, After = 0.3;

    private readonly int _rate;
    private readonly int _frameLength;
    private readonly Biquad _filter;
    private readonly List<float> _all;
    private double _sum;
    private int _filled;
    private int _frames;
    private float _noise = -1;
    private readonly List<float> _learnt = [];
    private int _speechRun, _silentRun, _speechFrames;
    private int _firstSpeech = -1, _lastSpeech = -1;

    public TakeDetector(int rate)
    {
        _rate = Math.Max(8000, rate);
        _frameLength = Math.Max(1, (int)(_rate * FrameSeconds));
        _filter = Biquad.HighPass(_rate, DetectionHighPass);
        _all = new List<float>(_rate * 10);
    }

    /// <summary>Everything heard since the start of the take.</summary>
    public int Length => _all.Count;

    /// <summary>The noise of the room learnt (RMS of the sound filtered; -1 before it is).</summary>
    public float Noise => _noise;

    /// <summary>Speech was heard.</summary>
    public bool HeardSpeech => _firstSpeech >= 0;

    /// <summary>Takes the next samples; true once what was said has ended (a second of silence after it).</summary>
    public bool Add(ReadOnlySpan<float> samples)
    {
        var ended = false;
        foreach (var sample in samples)
        {
            _all.Add(sample);
            var filtered = _filter.Next(sample);
            _sum += filtered * filtered;
            if (++_filled < _frameLength)
                continue;
            ended |= Frame((float)Math.Sqrt(_sum / _frameLength));
            _sum = 0;
            _filled = 0;
        }
        return ended;
    }

    private bool Frame(float rms)
    {
        var index = _frames++;
        if (_noise < 0)
        {
            _learnt.Add(rms);
            if (_learnt.Count < LearnFrames)
                return false;
            // The median of the first frames: a word begun at once weighs little.
            _learnt.Sort();
            _noise = Math.Max(_learnt[_learnt.Count / 2], 1e-6f);
            return false;
        }
        var speech = rms > Math.Max(MinimumRms, _noise * SpeechRatio);
        if (speech)
        {
            _speechRun++;
            _silentRun = 0;
            if (_speechRun >= 5 && _firstSpeech < 0)
                _firstSpeech = index - _speechRun + 1;
            if (_firstSpeech >= 0)
            {
                _lastSpeech = index;
                _speechFrames++;
            }
        }
        else
        {
            _speechRun = 0;
            _silentRun++;
            // The noise followed down, and slowly up, on the frames without speech.
            _noise = rms < _noise ? (_noise + rms) / 2 : _noise * 0.995f + rms * 0.005f;
        }
        return _firstSpeech >= 0 && _speechFrames * FrameSeconds >= MinSpeechSeconds && _silentRun * FrameSeconds >= EndSeconds;
    }

    /// <summary>
    /// The take: the speech heard with a little before and after it; nothing heard as speech, everything recorded but
    /// its first and last tenth of a second (■ STOP: the user said something). Null when too short to be a take.
    /// </summary>
    public float[]? Take(bool whole)
    {
        int start, end;
        if (HeardSpeech && !whole)
        {
            start = Math.Max(0, _firstSpeech * _frameLength - (int)(Before * _rate));
            end = Math.Min(_all.Count, (_lastSpeech + 1) * _frameLength + (int)(After * _rate));
        }
        else if (HeardSpeech)
        {
            start = Math.Max(0, _firstSpeech * _frameLength - (int)(Before * _rate));
            end = _all.Count;
        }
        else
        {
            start = Math.Min(_all.Count, (int)(0.1 * _rate));
            end = Math.Max(start, _all.Count - (int)(0.1 * _rate));
        }
        if (end - start < _rate * 0.4)
            return null;
        return _all.GetRange(start, end - start).ToArray();
    }

    /// <summary>
    /// A take made ready to be learnt from: its frequencies under <paramref name="highPass"/> Hz taken away (the hum of
    /// the room), its peak brought to <paramref name="peakDb"/> dB (every take as loud).
    /// </summary>
    public static float[] Clean(float[] samples, int rate, double highPass = 60, double peakDb = -3)
    {
        var filter = Biquad.HighPass(rate, highPass);
        var clean = new float[samples.Length];
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            clean[i] = filter.Next(samples[i]);
            peak = Math.Max(peak, Math.Abs(clean[i]));
        }
        if (peak > 1e-5f)
        {
            var gain = (float)Math.Pow(10, peakDb / 20) / peak;
            for (var i = 0; i < clean.Length; i++)
                clean[i] *= gain;
        }
        return clean;
    }

    /// <summary>A filter of the second order (RBJ): its state goes from one sample to the next.</summary>
    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _x1, _x2, _y1, _y2;

        public static Biquad HighPass(int rate, double frequency)
        {
            var w = 2 * Math.PI * frequency / rate;
            var alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
            var cos = Math.Cos(w);
            var a0 = 1 + alpha;
            return new Biquad((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }

        public float Next(float x)
        {
            var y = b0 * x + b1 * _x1 + b2 * _x2 - a1 * _y1 - a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return (float)y;
        }
    }
}
