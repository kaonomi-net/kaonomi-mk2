using System.Buffers.Binary;
using System.Text;
using KAONOMI.Core;
using KAONOMI.Core.Models;
using SherpaOnnx;

namespace KAONOMI.Server.Voices
{
    /// <summary>The body of <c>POST /voice/identify</c>: a WAV file in base 64, and the smallest similarity to say whose voice it is.</summary>
    public sealed class VoiceIdentifyRequest
    {
        /// <summary>The WAV file (PCM 8, 16, 24 or 32 bits, or 32-bit float; any rate, any number of channels), in base 64; a data URL is accepted.</summary>
        public string wav { get; set; } = string.Empty;

        /// <summary>The smallest similarity (cosine, 0 to 1) to say the voice is someone's; 0.5 when missing.</summary>
        public double? threshold { get; set; }
    }

    /// <summary>The body of <c>POST /voice/enrol</c>: the name, and a WAV file in base 64 where that person speaks alone.</summary>
    public sealed class VoiceEnrolRequest
    {
        public string name { get; set; } = string.Empty;

        /// <summary>The WAV file, in base 64 (see <see cref="VoiceIdentifyRequest.wav"/>).</summary>
        public string wav { get; set; } = string.Empty;

        /// <summary>Where the voice comes from (kept in the document); "REST" when missing.</summary>
        public string? source { get; set; }

        /// <summary>The language spoken in the WAV (FR, EN…), kept in the document; optional.</summary>
        public string? language { get; set; }
    }

    /// <summary>A name and how close its voice is.</summary>
    public sealed class VoiceScore
    {
        public string name { get; set; } = string.Empty;

        public double score { get; set; }

        public int samples { get; set; }

        /// <summary>The languages this name was enrolled in (FR, EN…); empty when none was said.</summary>
        public List<string> languages { get; set; } = [];
    }

    /// <summary>The answer of <c>POST /voice/identify</c>.</summary>
    public sealed class VoiceIdentifyResult
    {
        public bool success { get; set; }

        /// <summary>Whose voice it is: null when nobody enrolled is close enough.</summary>
        public string? name { get; set; }

        /// <summary>The similarity (cosine, 0 to 1) with the closest voice enrolled, and whose it is.</summary>
        public double score { get; set; }

        public string? closest { get; set; }

        /// <summary>The language (FR, EN…) of the sample of the closest name that is the closest to the WAV; empty when it was not said at its enrolment.</summary>
        public string language { get; set; } = string.Empty;

        public double threshold { get; set; }

        /// <summary>Every name enrolled, the closest first.</summary>
        public List<VoiceScore> scores { get; set; } = [];

        /// <summary>The length of the WAV, in seconds, and its level (root mean square, 0 to 1).</summary>
        public double seconds { get; set; }

        public double level { get; set; }

        public string model { get; set; } = VoiceService.ModelName;

        public double durationMs { get; set; }

        public string message { get; set; } = string.Empty;
    }

    /// <summary>The answer of <c>POST /voice/enrol</c>.</summary>
    public sealed class VoiceEnrolResult
    {
        public bool success { get; set; }

        /// <summary>The id of the document written in the search engine (index <c>voice</c>).</summary>
        public string? id { get; set; }

        public string name { get; set; } = string.Empty;

        /// <summary>The language kept with the sample (FR, EN…); empty when none was given.</summary>
        public string language { get; set; } = string.Empty;

        /// <summary>How many samples of voice this name has now.</summary>
        public int samples { get; set; }

        public double seconds { get; set; }

        public double level { get; set; }

        public string model { get; set; } = VoiceService.ModelName;

        public double durationMs { get; set; }

        public string message { get; set; } = string.Empty;
    }

    /// <summary>What a request asks cannot be done: said to the caller with a 400.</summary>
    public sealed class VoiceRequestException(string message) : Exception(message);

    /// <summary>
    /// Who speaks, for the REST API: <c>POST /voice/identify</c> and <c>POST /voice/enrol</c> take a WAV file in base 64
    /// in their JSON. The voice becomes an embedding (192 numbers that depend on the speaker, not on what is said)
    /// with the same open source parts as /recorder of the chat: sherpa-onnx (Apache 2.0) runs the speaker model
    /// ERes2Net of 3D-Speaker (Apache 2.0), kept in <c>~/kaonomi/models/speaker</c> and downloaded when missing. So a
    /// voice enrolled here is recognised by the chat, and the other way round: both compare the documents of the
    /// index <c>voice</c> of the search engine (<see cref="Voice"/>), by the cosine.
    /// <para>
    /// The voices are read from the search engine and kept in memory: read again every minute, and followed at once
    /// when one is written or erased through this server (the search engine lists a document a second or two after it
    /// was saved).
    /// </para>
    /// </summary>
    public sealed class VoiceService
    {
        public const string ModelFile = "3dspeaker_speech_eres2net_sv_en_voxceleb_16k.onnx";

        /// <summary>The model as the documents name it: the embeddings of another model are not compared.</summary>
        public const string ModelName = "3dspeaker_speech_eres2net_sv_en_voxceleb_16k";

        private const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/" + ModelFile;

        /// <summary>The shortest speech an embedding is computed from, in seconds; the longest WAV read.</summary>
        public const double MinSeconds = 1.5, MaxSeconds = 120;

        /// <summary>Under this level (root mean square) the WAV is silence.</summary>
        private const double SilenceLevel = 0.0006;

        private const int MaxVoices = 5000;

        private static readonly TimeSpan ReadAgainEvery = TimeSpan.FromMinutes(1), JustChanged = TimeSpan.FromSeconds(15);

        private static VoiceService? _instance;
        private static readonly object InstanceGate = new();

        private readonly SessionManager _session;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _loading = new(1, 1);
        private SpeakerEmbeddingExtractor? _extractor;
        private Dictionary<string, Voice> _voices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _written = new(StringComparer.Ordinal), _erased = new(StringComparer.Ordinal);
        private DateTime _readAt = DateTime.MinValue;

        private VoiceService(SessionManager session) => _session = session;

        public static VoiceService Get(SessionManager session)
        {
            lock (InstanceGate)
                return _instance ??= new VoiceService(session);
        }

        public static string ModelPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "kaonomi", "models", "speaker", ModelFile);

        // ── The model ──

        private async Task<SpeakerEmbeddingExtractor> ExtractorAsync()
        {
            if (_extractor is { } ready)
                return ready;
            await _loading.WaitAsync();
            try
            {
                if (_extractor is { } loaded)
                    return loaded;
                if (!File.Exists(ModelPath))
                {
                    Console.WriteLine($"[voice] downloading {ModelFile} into {Path.GetDirectoryName(ModelPath)} (once, 26 MB)…");
                    Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
                    var partial = ModelPath + ".part";
                    using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                    using (var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        await using var file = File.Create(partial);
                        await response.Content.CopyToAsync(file);
                    }
                    File.Move(partial, ModelPath, overwrite: true);
                }
                var extractor = await Task.Run(() => new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig { Model = ModelPath, NumThreads = 2, Debug = 0, Provider = "cpu" }));
                Console.WriteLine($"[voice] speaker model {ModelName} loaded ({extractor.Dim} numbers per voice)");
                return _extractor = extractor;
            }
            finally
            {
                _loading.Release();
            }
        }

        /// <summary>The embedding (of length 1) of the voice in these samples; a quiet voice is brought to a full level first.</summary>
        private float[] Embed(SpeakerEmbeddingExtractor extractor, float[] samples, int rate)
        {
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
            float[] embedding;
            lock (_gate)
            {
                using var stream = extractor.CreateStream();
                stream.AcceptWaveform(rate, samples);
                stream.InputFinished();
                if (!extractor.IsReady(stream))
                    throw new VoiceRequestException("The WAV holds too little speech to compute a voice");
                embedding = extractor.Compute(stream);
            }
            var norm = Math.Sqrt(embedding.Sum(v => (double)v * v));
            if (norm < 1e-9)
                throw new VoiceRequestException("No voice could be computed from the WAV");
            for (var i = 0; i < embedding.Length; i++)
                embedding[i] = (float)(embedding[i] / norm);
            return embedding;
        }

        // ── The WAV ──

        /// <summary>The samples of a WAV file given in base 64 (the channels mixed into one, -1 to 1) and its rate.</summary>
        public static (float[] Samples, int Rate) ReadWav(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                throw new VoiceRequestException("\"wav\" is missing: the WAV file in base 64");
            var text = base64.Trim();
            // A data URL: data:audio/wav;base64,…
            if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && text.IndexOf(',') is > 0 and var comma)
                text = text[(comma + 1)..];
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                throw new VoiceRequestException("\"wav\" is not base 64");
            }
            if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
                throw new VoiceRequestException("\"wav\" is not a WAV file (RIFF/WAVE): send PCM or float WAV");
            int format = 0, channels = 0, rate = 0, bits = 0;
            var position = 12;
            while (position + 8 <= bytes.Length)
            {
                var id = Encoding.ASCII.GetString(bytes, position, 4);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4));
                var start = position + 8;
                if (id == "fmt " && start + 16 <= bytes.Length)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start));
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 2));
                    rate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + 4));
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 14));
                    // WAVE_FORMAT_EXTENSIBLE: the real format is the start of its sub-format.
                    if (format == 0xFFFE && size >= 26 && start + 26 <= bytes.Length)
                        format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(start + 24));
                }
                else if (id == "data")
                {
                    if (channels <= 0 || rate <= 0 || bits is not (8 or 16 or 24 or 32) || format is not (1 or 3) || (format == 3 && bits != 32))
                        throw new VoiceRequestException($"WAV format not read (format {format}, {bits} bits, {channels} channel(s), {rate} Hz): send PCM 8/16/24/32 bits or 32-bit float");
                    // A file cut short says 0 (or too much): what is there is read.
                    var available = bytes.Length - start;
                    var length = (int)(size == 0 || size > available ? available : size);
                    var frameBytes = bits / 8 * channels;
                    var frames = (int)Math.Min(length / frameBytes, (long)(MaxSeconds * rate));
                    var samples = new float[frames];
                    for (var frame = 0; frame < frames; frame++)
                    {
                        double sum = 0;
                        for (var channel = 0; channel < channels; channel++)
                        {
                            var at = start + frame * frameBytes + channel * (bits / 8);
                            sum += bits switch
                            {
                                8 => (bytes[at] - 128) / 128.0,
                                16 => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at)) / 32768.0,
                                24 => ((bytes[at] | (bytes[at + 1] << 8) | ((sbyte)bytes[at + 2] << 16))) / 8388608.0,
                                _ => format == 3 ? BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at)) : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)) / 2147483648.0
                            };
                        }
                        samples[frame] = (float)(sum / channels);
                    }
                    return (samples, rate);
                }
                position = (int)Math.Min(int.MaxValue, start + size + (size & 1));
            }
            throw new VoiceRequestException("The WAV file has no samples (no data chunk)");
        }

        private static double Level(float[] samples)
        {
            if (samples.Length == 0)
                return 0;
            double sum = 0;
            foreach (var sample in samples)
                sum += (double)sample * sample;
            return Math.Sqrt(sum / samples.Length);
        }

        /// <summary>The samples of the WAV, checked: long enough and not silent.</summary>
        private static (float[] Samples, int Rate, double Seconds, double Level) Speech(string wav)
        {
            var (samples, rate) = ReadWav(wav);
            var seconds = samples.Length / (double)rate;
            var level = Level(samples);
            if (seconds < MinSeconds)
                throw new VoiceRequestException($"The WAV is too short ({seconds:0.0} s): {MinSeconds:0.0} s of speech at least");
            if (level < SilenceLevel)
                throw new VoiceRequestException($"The WAV is silent (level {level:0.00000})");
            return (samples, rate, seconds, level);
        }

        // ── The voices enrolled ──

        /// <summary>The voices of the search engine (the ones of this model), kept in memory and read again every minute.</summary>
        private List<Voice> Voices()
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (now - _readAt >= ReadAgainEvery)
                {
                    var read = _session.VoicesLast(MaxVoices).Where(v => v.model == ModelName && v.feature.Count > 0).ToDictionary(v => v.id, StringComparer.Ordinal);
                    // What was just written or erased here may not be listed (or still be) yet: kept as it is here.
                    foreach (var (id, at) in _written.ToList())
                    {
                        if (now - at > JustChanged)
                            _written.Remove(id);
                        else if (!read.ContainsKey(id) && _voices.TryGetValue(id, out var voice))
                            read[id] = voice;
                    }
                    foreach (var (id, at) in _erased.ToList())
                    {
                        if (now - at > JustChanged)
                            _erased.Remove(id);
                        else
                            read.Remove(id);
                    }
                    _voices = read;
                    _readAt = now;
                }
                return [.. _voices.Values];
            }
        }

        /// <summary>A voice was written through this server (enrolled here, or sent by the chat): followed at once.</summary>
        public void Written(Voice voice)
        {
            lock (_gate)
            {
                _erased.Remove(voice.id);
                _written[voice.id] = DateTime.UtcNow;
                if (voice.model == ModelName && voice.feature.Count > 0)
                    _voices[voice.id] = voice;
                else
                    _voices.Remove(voice.id);
            }
        }

        /// <summary>A voice was erased through this server: followed at once.</summary>
        public void Erased(string id)
        {
            lock (_gate)
            {
                _written.Remove(id);
                _erased[id] = DateTime.UtcNow;
                _voices.Remove(id);
            }
        }

        /// <summary>A name as the chat writes it (the name of a folder): letters, digits, spaces, - and _; 40 characters at most.</summary>
        public static string SafeName(string name)
        {
            var kept = new StringBuilder();
            foreach (var c in name)
                if (char.IsLetterOrDigit(c) || c is '_' or '-' || char.IsWhiteSpace(c))
                    kept.Append(char.IsWhiteSpace(c) ? ' ' : c);
            var text = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return text.Length > 40 ? text[..40].Trim() : text;
        }

        /// <summary>A language as it is kept: FR, EN, PT-BR… (upper case; letters, digits and -; 10 characters at most); empty when none.</summary>
        public static string SafeLanguage(string? language)
        {
            var text = string.Concat((language ?? string.Empty).Trim().Replace('_', '-').Where(c => char.IsLetterOrDigit(c) || c == '-')).ToUpperInvariant();
            return text.Length > 10 ? text[..10] : text;
        }

        /// <summary>The id of the document of this sample of this name, as the chat computes it.</summary>
        private static string IdOf(string name, string file) =>
            string.Concat($"voice-{name}-{Path.GetFileNameWithoutExtension(file)}".Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

        // ── The two services ──

        /// <summary>
        /// Whose voice the WAV is: each name enrolled is as close as the closest of its samples, or their average; the
        /// closest name is the answer when its similarity reaches the threshold.
        /// </summary>
        public async Task<VoiceIdentifyResult> IdentifyAsync(VoiceIdentifyRequest request)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var threshold = Math.Clamp(request.threshold ?? 0.5, 0.0, 1.0);
            var (samples, rate, seconds, level) = Speech(request.wav);
            var extractor = await ExtractorAsync();
            var embedding = await Task.Run(() => Embed(extractor, samples, rate));
            var scores = new List<VoiceScore>();
            // For each name, the language of its sample the closest to the WAV.
            var closestLanguages = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var person in Voices().GroupBy(v => v.name, StringComparer.Ordinal))
            {
                double best = 0, bestSample = double.MinValue;
                var mean = new double[embedding.Length];
                var count = 0;
                var language = string.Empty;
                foreach (var voice in person.Where(v => v.feature.Count == embedding.Length))
                {
                    double dot = 0;
                    for (var i = 0; i < embedding.Length; i++)
                    {
                        dot += embedding[i] * voice.feature[i];
                        mean[i] += voice.feature[i];
                    }
                    if (dot > bestSample)
                        (bestSample, language) = (dot, voice.language ?? string.Empty);
                    best = Math.Max(best, dot);
                    count++;
                }
                if (count == 0)
                    continue;
                var norm = Math.Sqrt(mean.Sum(v => v * v));
                if (norm > 1e-9)
                {
                    double dot = 0;
                    for (var i = 0; i < embedding.Length; i++)
                        dot += embedding[i] * mean[i] / norm;
                    best = Math.Max(best, dot);
                }
                scores.Add(new VoiceScore
                {
                    name = person.Key, score = Math.Round(best, 4), samples = count,
                    languages = [.. person.Select(v => v.language ?? string.Empty).Where(l => l.Length > 0).Distinct().OrderBy(l => l, StringComparer.Ordinal)]
                });
                closestLanguages[person.Key] = language;
            }
            scores = [.. scores.OrderByDescending(s => s.score)];
            var top = scores.FirstOrDefault();
            var known = top is not null && top.score >= threshold;
            return new VoiceIdentifyResult
            {
                success = true, name = known ? top!.name : null, score = top?.score ?? 0, closest = top?.name, threshold = threshold, scores = scores,
                language = top is not null ? closestLanguages[top.name] : string.Empty,
                seconds = Math.Round(seconds, 2), level = Math.Round(level, 5), durationMs = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
                message = scores.Count == 0 ? "Nobody is enrolled" : known ? $"The voice of {top!.name} ({top.score * 100:0} %)" : $"Voice not recognised (the closest is {top!.name}, {top.score * 100:0} %)"
            };
        }

        /// <summary>Keeps the voice of the WAV under the name: a document of the index <c>voice</c>, which the chat brings back at its next start.</summary>
        public async Task<VoiceEnrolResult> EnrolAsync(VoiceEnrolRequest request)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var name = SafeName(request.name ?? string.Empty);
            if (name.Length == 0 || name.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                throw new VoiceRequestException("\"name\" is missing: the name of who speaks (letters, digits, spaces, - and _)");
            var (samples, rate, seconds, level) = Speech(request.wav);
            var extractor = await ExtractorAsync();
            var embedding = await Task.Run(() => Embed(extractor, samples, rate));
            var file = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.json";
            var language = SafeLanguage(request.language);
            var voice = new Voice
            {
                id = IdOf(name, file), name = name, title = name, file = file, seconds = Math.Round(seconds, 1), language = language,
                source = string.IsNullOrWhiteSpace(request.source) ? "REST" : request.source.Trim(),
                feature = [.. embedding.Select(v => Math.Round((double)v, 6))], model = ModelName,
                date = DateTime.UtcNow, appName = "KAONOMI.Server", machineName = Environment.MachineName, userName = Environment.UserName
            };
            var saved = _session.VoiceSave(voice);
            if (!saved.Success)
                return new VoiceEnrolResult { success = false, name = name, language = language, seconds = Math.Round(seconds, 2), level = Math.Round(level, 5), message = saved.Message, durationMs = Math.Round(watch.Elapsed.TotalMilliseconds, 1) };
            Written(voice);
            var count = Voices().Count(v => v.name == name);
            return new VoiceEnrolResult
            {
                success = true, id = voice.id, name = name, language = language, samples = count, seconds = Math.Round(seconds, 2), level = Math.Round(level, 5),
                durationMs = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
                message = $"{name} enrolled: {seconds:0.0} s of voice{(language.Length > 0 ? $" in {language}" : string.Empty)}, {count} sample(s) of this voice"
            };
        }
    }
}
