using System.Net.Http.Json;
using System.Text.Json;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// Asks a model of the LLM server of the configuration (<c>ollamaUrl</c>: the LLM gateway, in front of LM Studio or
    /// Ollama) for an answer in JSON, about a text or about a picture. The answers of the semantic cache of the gateway
    /// are not used (<c>X-Gateway-Cache: off</c>): each page is read by the model.
    /// </summary>
    public sealed class Llm : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly bool _openAi;
        private readonly TimeSpan _timeout;

        public Llm(AnalyzeConfig config)
        {
            _baseUrl = (config.ollamaUrl ?? "http://localhost:11435").TrimEnd('/');
            _openAi = KAONOMI.Core.Helpers.HelperOllamaClient.IsOpenAiCompatible(config.llmProvider);
            _timeout = TimeSpan.FromSeconds(Math.Max(20, config.llmTimeoutSeconds));
            _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>How many times the model was asked, and how many answers could not be read.</summary>
        public int Calls { get; private set; }

        public int Failures { get; private set; }

        /// <summary>The last reason a model gave no answer that could be read.</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>
        /// The JSON object the model answers to <paramref name="user"/> (asked twice when the first answer is not JSON),
        /// or null when it gave none; <paramref name="imagePath"/> is a picture shown to the model with the question.
        /// </summary>
        public async Task<JsonDocument?> AskJsonAsync(string model, string system, string user, CancellationToken cancellationToken, string? imagePath = null)
        {
            for (var attempt = 0; attempt < 2 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                Calls++;
                var content = await AskAsync(model, system, attempt == 0 ? user : user + "\n\nAnswer with the JSON object only, nothing before or after.", imagePath, cancellationToken);
                if (content is null)
                    break;
                var json = ExtractJson(content);
                if (json is not null)
                    return json;
                LastError = "the answer of the model was not JSON";
            }
            Failures++;
            return null;
        }

        /// <summary>The text the model answers, or null (the reason in <see cref="LastError"/>).</summary>
        private async Task<string?> AskAsync(string model, string system, string user, string? imagePath, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                object body;
                string endpoint;
                if (_openAi)
                {
                    endpoint = (_baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? _baseUrl : _baseUrl + "/v1") + "/chat/completions";
                    object userContent = imagePath is null
                        ? user
                        : new object[]
                        {
                            new { type = "text", text = user },
                            new { type = "image_url", image_url = new { url = $"data:{MimeOf(imagePath)};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, timeout.Token))}" } }
                        };
                    body = new
                    {
                        model,
                        stream = false,
                        temperature = 0.1,
                        messages = new object[] { new { role = "system", content = system }, new { role = "user", content = userContent } }
                    };
                }
                else
                {
                    endpoint = _baseUrl + "/api/chat";
                    object userMessage = imagePath is null
                        ? new { role = "user", content = user }
                        : new { role = "user", content = user, images = new[] { Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, timeout.Token)) } };
                    body = new
                    {
                        model,
                        stream = false,
                        format = "json",
                        options = new { temperature = 0.1 },
                        messages = new object[] { new { role = "system", content = system }, userMessage }
                    };
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(body) };
                request.Headers.TryAddWithoutValidation("X-Gateway-Cache", "off");
                using var response = await _http.SendAsync(request, timeout.Token);
                var text = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"HTTP {(int)response.StatusCode} from {endpoint}: {Short(text)}";
                    return null;
                }
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                string? content = null;
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var openAiContent))
                    content = openAiContent.ValueKind == JsonValueKind.String ? openAiContent.GetString() : openAiContent.ToString();
                else if (root.TryGetProperty("message", out var ollamaMessage) && ollamaMessage.TryGetProperty("content", out var ollamaContent))
                    content = ollamaContent.GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    LastError = "the model gave an empty answer";
                    return null;
                }
                return content;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LastError = $"no answer of {model} within {_timeout.TotalSeconds:0} s";
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
            {
                LastError = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// The JSON object of an answer: the answer itself, or what is between its first { and its last } (a model may
        /// wrap it in a code block, or think aloud before it).
        /// </summary>
        public static JsonDocument? ExtractJson(string content)
        {
            var text = content.Trim();
            // What a reasoning model writes before its answer.
            var think = text.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (think >= 0)
                text = text[(think + "</think>".Length)..];
            var first = text.IndexOf('{');
            var last = text.LastIndexOf('}');
            if (first < 0 || last <= first)
                return null;
            try
            {
                return JsonDocument.Parse(text[first..(last + 1)], new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };

        private static string Short(string text) => text.Length > 200 ? text[..200] + "…" : text;

        public void Dispose() => _http.Dispose();
    }

    /// <summary>Reads the fields of the JSON a model answers, whatever it wrote where a text, a number or a list was asked.</summary>
    public static class Json
    {
        public static string Text(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
                return string.Empty;
            return value.ValueKind switch
            {
                JsonValueKind.String => (value.GetString() ?? string.Empty).Trim(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.Array => string.Join(" ", value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())).Trim(),
                _ => string.Empty
            };
        }

        public static double Number(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
                return 0;
            return value.ValueKind switch
            {
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.String when double.TryParse((value.GetString() ?? string.Empty).Replace('%', ' ').Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) => number,
                _ => 0
            };
        }

        public static List<string> Texts(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
                return new List<string>();
            if (value.ValueKind == JsonValueKind.String)
                return (value.GetString() ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => (v.GetString() ?? string.Empty).Trim()).Where(v => v.Length > 0).ToList()
                : new List<string>();
        }

        public static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object).ToList()
                : Enumerable.Empty<JsonElement>();
    }
}
