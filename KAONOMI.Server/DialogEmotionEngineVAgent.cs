namespace KAONOMI.Server
{
    using MARI.Framework.Core;
    using MARI.Framework.Core.Abstractions;
    using KAONOMI.Core;
    using KAONOMI.Core.Helpers;
    using KAONOMI.Core.Models;
    using KAONOMI.Core.Models.LLM;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// The emotion engine of the dialog sequence: every 3 prompts of a conversation, it asks the model the emotions of
    /// these 3 prompts together (the share of each of seven emotions, the leading one, its intensity, the valence, one
    /// sentence saying why) and keeps them in the search engine (<see cref="DetectedEmotion"/>, index
    /// <c>detectedemotion</c>). The prompt it gives the model is <c>dialog.emotions</c>, edited in /prompts of the chat.
    /// It runs before the processor: when /emotion of the chat gives a text for the emotion that leads in the
    /// conversation (the one found now, or the last one found), that text is added to the system prompt of the answer.
    /// </summary>
    internal class DialogEmotionEngineVAgent : VGrainBase<IDialogEmotionEngineVAgent>, IDialogEmotionEngineVAgent
    {
        /// <summary>Creates the agent with MARI logging support.</summary>
        /// <param title="logger">Logger for this grain type.</param>
        public DialogEmotionEngineVAgent(ILogger<IDialogEmotionEngineVAgent> logger) : base(logger)
        {
        }

        /// <inheritdoc />
        public async Task<string> DoProcess(string idDialog, IExecutionContext Context)
        {
            Config config = HelperConfigManager.GetFromFile<Config>("config.json");
            string appName = config.appName + "_EmotionEngineVAgent";

            var session = new SessionManager(config);
            session.LogSave("Agent Called", appName, "INFO");

            Performance perf = new Performance();
            perf.date = DateTime.Now;
            perf.comment = "Emotion engine DoProcess execution time.";
            perf.machineName = Environment.MachineName;
            perf.userName = Environment.UserName;
            perf.appName = appName;
            perf.operationName = "DialogEmotionEngineVAgent.DoProcess";
            perf.startTime = perf.date;
            perf.metadatas = new List<MetaData>();
            MetaData dialogIdMeta = new MetaData();
            dialogIdMeta.Key = "DialogId";
            dialogIdMeta.Value_String = idDialog;
            perf.metadatas.Add(dialogIdMeta);

            try
            {
                Dialog dialog;
                try
                {
                    dialog = session.DialogGet(idDialog);
                    session.LogSave("Dialog Loaded", appName, "INFO");
                }
                catch (Exception ex)
                {
                    session.LogSave($"Error loading dialog '{idDialog}': {ex.Message}", appName, "ERROR");
                    throw; // Stop execution to avoid analyzing emotions on a null object
                }

                // Run emotion engine
                await RunEmotionEngineAsync(dialog, session, appName, config);

                try
                {
                    idDialog = session.DialogSave(dialog);
                    session.LogSave("Dialog Updated", appName, "INFO");
                }
                catch (Exception ex)
                {
                    session.LogSave($"Error saving dialog '{idDialog}': {ex.Message}", appName, "ERROR");
                    throw;
                }

                return idDialog;
            }
            finally
            {
                perf.endTime = DateTime.Now;
                perf.durationMilliseconds = (int)(perf.endTime - perf.startTime).TotalMilliseconds;
                session.PerformanceSave(perf);
            }
        }

        /// <summary>
        /// Every <see cref="DetectedEmotion.Window"/> prompts of a conversation, the emotions of these prompts together
        /// are asked to the model and kept in the search engine (<see cref="DetectedEmotion"/>). The other prompts are
        /// only counted. Nothing here ever stops the answer: a failure is logged and the dialog goes on.
        /// </summary>
        private async Task RunEmotionEngineAsync(Dialog dialog, SessionManager session, string appName, Config config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dialog.SessionId) || dialog.request?.regenerate == true)
                {
                    session.LogSave("Emotion engine: nothing to do (no conversation, or the same prompt asked again)", appName, "INFO");
                    return;
                }
                // The prompts of the conversation before this one (the same prompt asked again is not another prompt).
                // This dialog is counted apart: the search engine may not list it yet.
                var before = await Task.Run(() => session.DialogsOfSession(dialog.SessionId)
                    .Where(d => d.id != dialog.id && d.request?.regenerate != true && d.date <= dialog.date).ToList());
                var number = before.Count + 1;

                // The emotion found last in the conversation leads this answer (its text of /emotion, if any). The
                // analysis due on this prompt runs in the background: it no longer holds the answer back (it asked the
                // model, about 1 s, every 3 prompts); what it finds leads from the next prompt.
                var last = await Task.Run(() => session.DetectedEmotionsOfSession(dialog.SessionId).LastOrDefault());
                if (last is not null)
                    await ApplyAsync(dialog, last.emotion, session, appName);
                if (number % DetectedEmotion.Window != 0)
                {
                    session.LogSave($"Emotion engine: prompt {number} of the conversation, the emotions are looked at every {DetectedEmotion.Window} prompts", appName, "INFO");
                    return;
                }
                var group = before.TakeLast(DetectedEmotion.Window - 1).Append(dialog).ToList();
                // This engine runs before the processor: the prompt of this dialog is still in its request.
                var prompts = group.Select(d => Knowledge.ContextManager.Question(
                    string.IsNullOrEmpty(d.PromptUserText) ? HelperLlmParsersAndTools.GetUserPromptFromRequest(d.request) : d.PromptUserText, bare: true).Trim()).ToList();
                var (dialogId, sessionId, model, ids) = (dialog.id, dialog.SessionId, dialog.request?.model ?? string.Empty, group.Select(d => d.id).ToList());
                session.LogSave($"Emotion engine: prompts {number - DetectedEmotion.Window + 1} to {number} analysed in the background", appName, "INFO");
                _ = Task.Run(() => AnalyseAsync(config, appName, dialogId, sessionId, model, ids, prompts, number));
            }
            catch (Exception ex)
            {
                session.LogSave("Emotion engine: " + ex.Message, appName, "ERROR");
            }
        }

        /// <summary>
        /// The analysis of the emotions of <paramref name="prompts"/>, in the background (its own session with the
        /// search engine): the model is asked past the semantic cache of the gateway (another conversation's emotions
        /// were given back for messages that looked alike), what it finds is kept in the search engine with the text
        /// of /emotion that leads from the next prompt. A failure is logged.
        /// </summary>
        private static async Task AnalyseAsync(Config config, string appName, string dialogId, string sessionId, string model,
            List<string> dialogIds, List<string> prompts, int number)
        {
            var session = new SessionManager(config);
            try
            {
                // The prompt edited in /prompts of the chat when the search engine has one, else the default.
                var template = session.PromptTemplateCurrent("dialog.emotions");
                var system = (template?.text is { Length: > 0 } edited ? edited : DetectedEmotion.DefaultPrompt)
                    .Replace("{{emotions}}", string.Join(", ", DetectedEmotion.Names))
                    .Replace("{{language}}", LanguageOf(config));
                var user = new StringBuilder($"The last {prompts.Count} messages of the person, the oldest first:\n\n");
                for (var i = 0; i < prompts.Count; i++)
                    user.Append($"[{i + 1}] {(prompts[i].Length > 2000 ? prompts[i][..2000] + "…" : prompts[i])}\n\n");

                var answer = await HelperOllamaClient.SendChatMessageAsync(config.ollamaUrl, model, user.ToString(), system, config.llmProvider, noCache: true);
                if (!string.IsNullOrEmpty(answer.Error))
                {
                    session.LogSave($"Emotion engine: the model did not answer ({answer.Error})", appName, "ERROR");
                    return;
                }
                if (Read(answer.Content) is not { } read)
                {
                    session.LogSave($"Emotion engine: the answer of the model is not the JSON asked: {(answer.Content?.Length > 200 ? answer.Content[..200] : answer.Content)}", appName, "ERROR");
                    return;
                }
                var detected = new DetectedEmotion(dialogId, sessionId)
                {
                    appName = appName,
                    dialogIds = dialogIds,
                    prompts = prompts.Select(p => p.Length > 2000 ? p[..2000] + "…" : p).ToList(),
                    promptNumber = number,
                    emotions = read.Shares,
                    emotion = read.Leading,
                    intensity = read.Intensity,
                    valence = read.Valence,
                    summary = read.Summary,
                    title = $"{read.Leading} {read.Intensity:0}/10",
                    model = string.IsNullOrWhiteSpace(answer.Model) ? model : answer.Model,
                    promptVersion = template?.version ?? 0
                };
                // The text of /emotion for the emotion that leads: added to the system prompt from the next prompt.
                var probe = new Dialog();
                await ApplyAsync(probe, read.Leading, session, appName);
                detected.systemText = probe.EmotionSystemText ?? string.Empty;
                detected.systemTextVersion = probe.EmotionPromptVersion;
                var saved = session.DetectedEmotionSave(detected);
                session.LogSave(saved.Success
                    ? $"Emotion engine: prompts {number - DetectedEmotion.Window + 1} to {number}: {read.Leading} ({read.Intensity:0}/10, valence {read.Valence:0.0}) saved"
                    : $"Emotion engine: the emotions were not saved: {saved.Message}", appName, saved.Success ? "INFO" : "ERROR");
            }
            catch (Exception ex)
            {
                session.LogSave("Emotion engine (background): " + ex.Message, appName, "ERROR");
            }
        }

        /// <summary>
        /// The "what if" of an emotion: when /emotion of the chat gives a text for the emotion that leads (the prompt
        /// <c>emotion.&lt;name&gt;</c>, its last version, not empty), that text is kept on the dialog; the processor adds
        /// it to the system prompt of the answer. An emotion without a text changes nothing.
        /// </summary>
        private static async Task ApplyAsync(Dialog dialog, string emotion, SessionManager session, string appName)
        {
            dialog.Emotion = emotion;
            dialog.EmotionSystemText = null;
            dialog.EmotionPromptVersion = 0;
            if (string.IsNullOrWhiteSpace(emotion))
                return;
            var template = await Task.Run(() => session.PromptTemplateCurrent("emotion." + emotion));
            if (template is null || string.IsNullOrWhiteSpace(template.text))
            {
                session.LogSave($"Emotion engine: {emotion} leads in the conversation; /emotion gives no text for it, the system prompt is not changed", appName, "INFO");
                return;
            }
            dialog.EmotionSystemText = template.text.Trim();
            dialog.EmotionPromptVersion = template.version;
            session.LogSave($"Emotion engine: {emotion} leads in the conversation; its text (version {template.version}, {template.text.Trim().Length} characters) goes in the system prompt", appName, "INFO");
        }

        /// <summary>The language the summary is written in: the one of the server when it says one, else English.</summary>
        private static string LanguageOf(Config config)
        {
            var language = (config.GetType().GetProperty("language")?.GetValue(config) as string ?? string.Empty).Trim().ToUpperInvariant();
            return language switch { "FR" => "French", "DE" => "German", "ES" => "Spanish", "IT" => "Italian", "NL" => "Dutch", "JA" => "Japanese", "EN" => "English", _ => "the language of the messages" };
        }

        /// <summary>
        /// What the model answered, read leniently (the JSON may be wrapped in a code block or followed by a comment):
        /// the shares brought to 100 %, the leading emotion, the intensity and the valence kept in their ranges. Null
        /// when no JSON object with the emotions is found.
        /// </summary>
        private static (EmotionShares Shares, string Leading, double Intensity, double Valence, string Summary)? Read(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;
            int start = content.IndexOf('{'), end = content.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(content[start..(end + 1)]);
                var root = document.RootElement;
                if (!root.TryGetProperty("emotions", out var emotions) || emotions.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return null;
                static double Number(System.Text.Json.JsonElement element) => element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number => element.GetDouble(),
                    System.Text.Json.JsonValueKind.String when double.TryParse(element.GetString()?.TrimEnd('%', ' '), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                    _ => 0
                };
                var values = DetectedEmotion.Names.ToDictionary(n => n, n => emotions.TryGetProperty(n, out var v) ? Math.Max(0, Number(v)) : 0);
                var total = values.Values.Sum();
                if (total <= 0)
                    return null;
                // Shares given from 0 to 1, or that do not add up to 100: brought to per cent of their sum.
                foreach (var name in DetectedEmotion.Names)
                    values[name] = Math.Round(values[name] * 100 / total, 1);
                var shares = new EmotionShares
                {
                    angry = values["angry"], disgust = values["disgust"], fearful = values["fearful"], happy = values["happy"],
                    neutral = values["neutral"], sad = values["sad"], surprised = values["surprised"]
                };
                var leading = values.OrderByDescending(v => v.Value).First().Key;
                var intensity = Math.Clamp(root.TryGetProperty("intensity", out var i) ? Number(i) : 0, 0, 10);
                var valence = Math.Clamp(root.TryGetProperty("valence", out var val) ? Number(val) : 0, -1, 1);
                var summary = root.TryGetProperty("summary", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String ? s.GetString() ?? string.Empty : string.Empty;
                return (shares, leading, intensity, valence, summary.Trim());
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
