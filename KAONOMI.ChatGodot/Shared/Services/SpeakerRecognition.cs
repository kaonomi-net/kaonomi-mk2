using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KAONOMI.Chat.Models;
using SherpaOnnx;

namespace KAONOMI.Chat.Services;

/// <summary>Who a voice is closest to: the name (null when nobody is close enough), and the similarity with the closest voice enrolled (0 to 1).</summary>
public sealed record SpeakerMatch(string? Name, double Score, string? Closest);

/// <summary>
/// Who speaks: a voice is turned into an embedding (a few hundred numbers that depend on the speaker, not on what is
/// said) and compared, by the cosine, with the voices enrolled under a name. Only open source, and everything on this
/// machine: sherpa-onnx (Apache 2.0, k2-fsa) runs the speaker model ERes2Net of 3D-Speaker (Apache 2.0, Alibaba
/// DAMO, trained on VoxCeleb), kept in <c>~/kaonomi/models/speaker</c> and downloaded when missing.
/// <para>
/// The voices enrolled are the JSON files of <c>~/kaonomi/audio/speakers/&lt;name&gt;/</c> (<see cref="VoiceDocument"/>:
/// the embedding of a sample of voice, not its sound). Several samples per name make the identification steadier.
/// A voice is someone's when its similarity with one of that name's samples, or with their average, reaches the
/// threshold. It needs a few seconds of speech: under 1.5 s nothing is said.
/// </para>
/// </summary>
public sealed partial class SpeakerRecognition : IDisposable
{
    public const string ModelFile = "3dspeaker_speech_eres2net_sv_en_voxceleb_16k.onnx";

    /// <summary>The model as the embeddings name it: the ones of another model are not compared.</summary>
    public const string ModelName = "3dspeaker_speech_eres2net_sv_en_voxceleb_16k";

    private const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/" + ModelFile;

    /// <summary>The shortest speech an embedding is computed from, in seconds.</summary>
    public const double MinSeconds = 1.5;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _gate = new();
    private SpeakerEmbeddingExtractor? _extractor;
    private Dictionary<string, List<VoiceDocument>> _voices = new(StringComparer.Ordinal);
    private bool _disposed;

    public static string ModelsDirectory => Path.Combine(ChatSettings.KaonomiHome, "models", "speaker");

    public static string ModelPath => Path.Combine(ModelsDirectory, ModelFile);

    /// <summary>The folder of the voices enrolled: one folder per name.</summary>
    public static string SpeakersDirectory => Path.Combine(ChatSettings.KaonomiHome, "audio", "speakers");

    /// <summary>The model is loaded: voices can be enrolled and identified.</summary>
    public bool IsReady
    {
        get
        {
            lock (_gate)
                return _extractor is not null;
        }
    }

    /// <summary>The names enrolled and how many samples each has.</summary>
    public IReadOnlyDictionary<string, int> Counts
    {
        get
        {
            lock (_gate)
                return _voices.ToDictionary(v => v.Key, v => v.Value.Count, StringComparer.Ordinal);
        }
    }

    /// <summary>Every sample enrolled.</summary>
    public IReadOnlyList<VoiceDocument> All
    {
        get
        {
            lock (_gate)
                return [.. _voices.Values.SelectMany(v => v)];
        }
    }

    /// <summary>A name as the name of a folder: letters, digits, spaces, - and _.</summary>
    public static string SafeName(string name)
    {
        var text = Spaces().Replace(NotName().Replace(name, string.Empty), " ").Trim();
        return text.Length > 40 ? text[..40].Trim() : text;
    }

    [GeneratedRegex(@"[^\w \-]")]
    private static partial Regex NotName();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// Downloads the model when it is missing (<paramref name="report"/> says so), loads it and reads the voices
    /// enrolled. Throws when the model cannot be downloaded or loaded.
    /// </summary>
    public async Task LoadAsync(Action<string> report, CancellationToken cancellationToken = default)
    {
        if (IsReady)
            return;
        if (!File.Exists(ModelPath))
        {
            report($"Downloading the speaker model {ModelFile} into {ModelsDirectory} (once, 26 MB)…");
            Directory.CreateDirectory(ModelsDirectory);
            var partial = ModelPath + ".part";
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(partial);
                await response.Content.CopyToAsync(file, cancellationToken);
            }
            File.Move(partial, ModelPath, overwrite: true);
        }
        await Task.Run(() =>
        {
            var extractor = new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig { Model = ModelPath, NumThreads = 2, Debug = 0, Provider = "cpu" });
            lock (_gate)
            {
                if (_disposed || _extractor is not null)
                {
                    extractor.Dispose();
                    return;
                }
                _extractor = extractor;
            }
            Reload();
        }, cancellationToken);
    }

    /// <summary>Reads the voices enrolled again from their folder (the ones of this model only).</summary>
    public void Reload()
    {
        var voices = new Dictionary<string, List<VoiceDocument>>(StringComparer.Ordinal);
        if (Directory.Exists(SpeakersDirectory))
        {
            foreach (var folder in Directory.EnumerateDirectories(SpeakersDirectory).OrderBy(f => f, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(folder);
                foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<VoiceDocument>(File.ReadAllText(file), Json) is not { } voice || voice.Model != ModelName || voice.Feature.Count == 0)
                            continue;
                        // The folder and the file say who and which: a file moved by hand is the voice of its folder.
                        voice.Name = name;
                        voice.File = Path.GetFileName(file);
                        voice.Id = VoiceDocument.IdOf(name, voice.File);
                        if (!voices.TryGetValue(name, out var list))
                            voices[name] = list = [];
                        list.Add(voice);
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                    {
                        ActivityLog.Error("Recorder", $"The voice {file} could not be read", ex);
                    }
                }
            }
        }
        lock (_gate)
            _voices = voices;
    }

    /// <summary>
    /// The embedding (of length 1) of the voice in <paramref name="samples"/> (mono, at <paramref name="rate"/> Hz;
    /// any rate, it is brought to the one of the model). Null when the model is not loaded or the sample is shorter
    /// than <see cref="MinSeconds"/>.
    /// </summary>
    public float[]? Embed(float[] samples, int rate)
    {
        if (rate <= 0 || samples.Length < MinSeconds * rate)
            return null;
        lock (_gate)
        {
            if (_extractor is not { } extractor)
                return null;
            // A quiet microphone: the voice is brought to a full level first (the model was trained on full ones).
            var peak = 0f;
            foreach (var sample in samples)
                peak = Math.Max(peak, Math.Abs(sample));
            if (peak is > 1e-5f and < 0.5f)
            {
                var gain = 0.8f / peak;
                var louder = new float[samples.Length];
                for (var i = 0; i < samples.Length; i++)
                    louder[i] = samples[i] * gain;
                samples = louder;
            }
            using var stream = extractor.CreateStream();
            stream.AcceptWaveform(rate, samples);
            stream.InputFinished();
            if (!extractor.IsReady(stream))
                return null;
            var embedding = extractor.Compute(stream);
            var norm = Math.Sqrt(embedding.Sum(v => (double)v * v));
            if (norm < 1e-9)
                return null;
            for (var i = 0; i < embedding.Length; i++)
                embedding[i] = (float)(embedding[i] / norm);
            return embedding;
        }
    }

    /// <summary>The level of a sound (root mean square, 0 to 1): what is too quiet is not a voice to identify.</summary>
    public static double Level(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
            return 0;
        double sum = 0;
        foreach (var sample in samples)
            sum += (double)sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>
    /// Keeps this voice under <paramref name="name"/> (with the language spoken, FR, EN…, when it is known): a JSON file in its folder. Null when the name is empty or the
    /// sample holds too little speech.
    /// </summary>
    public VoiceDocument? Enrol(string name, float[] samples, int rate, string source, string language = "")
    {
        name = SafeName(name);
        if (name.Length == 0 || Embed(samples, rate) is not { } embedding)
            return null;
        var file = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
        var voice = new VoiceDocument
        {
            Id = VoiceDocument.IdOf(name, file), Name = name, Title = name, File = file, Seconds = Math.Round(samples.Length / (double)rate, 1), Source = source,
            Language = language.Trim().ToUpperInvariant(),
            Feature = [.. embedding.Select(v => Math.Round((double)v, 6))], Model = ModelName
        };
        Write(voice);
        lock (_gate)
        {
            if (!_voices.TryGetValue(name, out var list))
                _voices[name] = list = [];
            list.Add(voice);
        }
        return voice;
    }

    /// <summary>Writes a voice in its folder (one enrolled here, or one brought back from the search engine).</summary>
    public static void Write(VoiceDocument voice)
    {
        var folder = Path.Combine(SpeakersDirectory, voice.Name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, voice.File), JsonSerializer.Serialize(voice, Json));
    }

    /// <summary>Forgets a name: its folder is deleted. False when it was not enrolled.</summary>
    public bool Forget(string name)
    {
        name = SafeName(name);
        if (name.Length == 0)
            return false;
        bool known;
        lock (_gate)
            known = _voices.Remove(name);
        var folder = Path.Combine(SpeakersDirectory, name);
        // Only a folder of the voices is ever deleted.
        if (Directory.Exists(folder) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)), Path.GetFullPath(SpeakersDirectory), StringComparison.Ordinal))
        {
            Directory.Delete(folder, recursive: true);
            known = true;
        }
        return known;
    }

    /// <summary>
    /// Whose voice this embedding is: the name whose samples (each, and their average) are the closest, when that
    /// similarity reaches <paramref name="threshold"/> (0 to 1); else nobody, with the closest name and its score.
    /// </summary>
    public SpeakerMatch Identify(float[] embedding, double threshold)
    {
        string? best = null;
        double bestScore = 0;
        lock (_gate)
        {
            foreach (var (name, voices) in _voices)
            {
                double score = 0;
                var mean = new double[embedding.Length];
                foreach (var voice in voices.Where(v => v.Feature.Count == embedding.Length))
                {
                    double dot = 0;
                    for (var i = 0; i < embedding.Length; i++)
                    {
                        dot += embedding[i] * voice.Feature[i];
                        mean[i] += voice.Feature[i];
                    }
                    score = Math.Max(score, dot);
                }
                var norm = Math.Sqrt(mean.Sum(v => v * v));
                if (norm > 1e-9)
                {
                    double dot = 0;
                    for (var i = 0; i < embedding.Length; i++)
                        dot += embedding[i] * mean[i] / norm;
                    score = Math.Max(score, dot);
                }
                if (score > bestScore)
                    (best, bestScore) = (name, score);
            }
        }
        return new SpeakerMatch(bestScore >= threshold ? best : null, bestScore, best);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _extractor?.Dispose();
            _extractor = null;
        }
    }
}
