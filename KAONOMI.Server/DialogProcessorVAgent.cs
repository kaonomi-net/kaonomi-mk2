using MARI.Framework.Core;
using MARI.Framework.Core.Abstractions;
using KAONOMI.Core;
using KAONOMI.Core.Helpers;
using KAONOMI.Core.Models;
using KAONOMI.Core.Models.LLM;
using Microsoft.Extensions.Logging;

namespace KAONOMI.Server
{
    /// <summary>
    /// Produces the answer of a dialog turn: writes in the prompt what the agent of the entities found (the entities of
    /// the ontology and the intels, <see cref="Dialog.Knowledge"/>), compresses the prompt to the budget of config.json
    /// (<c>promptMaxChars</c>, <c>promptKnowledgeMaxChars</c>; see <see cref="Knowledge.PromptCompressor"/>), writes
    /// before the question the context the server keeps for the conversation (<see cref="Knowledge.ContextManager"/>), then asks
    /// the model through <c>ollamaUrl</c>: the LLM gateway (semantic cache, router of the models), or Ollama / LM Studio.
    /// </summary>
    internal class DialogProcessorVAgent : VGrainBase<IDialogProcessorVAgent>, IDialogProcessorVAgent
    {
        /// <summary>Creates the agent with MARI logging support.</summary>
        /// <param title="logger">Logger for this grain type.</param>
        public DialogProcessorVAgent(ILogger<IDialogProcessorVAgent> logger) : base(logger)
        {
        }

        /// <inheritdoc />
        public async Task<string> DoProcess(string idDialog, IExecutionContext Context)
        {
            // Load configuration (locally or injected)
            Config config = HelperConfigManager.GetFromFile<Config>("config.json");
            string appName = config.appName + "_ProcssorVAgent";
            var session = new SessionManager(config);

            session.LogSave("Agent Called", appName, "INFO");

            Performance perf = new Performance();
            perf.date = DateTime.Now;
            perf.comment = "Dialog processor DoProcess execution time.";
            perf.machineName = Environment.MachineName;
            perf.userName = Environment.UserName;
            perf.appName = appName;
            perf.operationName = "DialogProcessorVAgent.DoProcess";
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
                    session.LogSave("Error loading dialog: " + ex.Message, appName, "ERROR");
                    throw; // Stop execution if the dialog cannot be loaded
                }

                // Main dialog processing (with await)
                await RunAsync(dialog, config, session, appName);

                try
                {
                    idDialog = session.DialogSave(dialog);
                    session.LogSave("Dialog Updated", appName, "INFO");
                }
                catch (Exception ex)
                {
                    session.LogSave("Error saving dialog: " + ex.Message, appName, "ERROR");
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
        /// The chat never repeats itself: when the answer of the model is one it already gave in the conversation (the
        /// assistant messages of the request and the dialogs of the session, see <see cref="Knowledge.AnswerRepeat"/>),
        /// the prompt is sent again with the instruction not to repeat itself (the prompt <c>dialog.norepeat</c> of
        /// /prompts of the chat), past the cache of the gateway, up to <see cref="Knowledge.AnswerRepeat.MaxRetries"/>
        /// times. Returns the first answer that is new, else the last one.
        /// </summary>
        private static async Task<OllamaChatResult> WithoutRepeatAsync(Dialog dialog, OllamaChatResult response, string user, string system, string model,
            Config config, SessionManager session, string appName)
        {
            if (response.Error is not null)
                return response;
            try
            {
                var previous = new List<string>();
                if (dialog.request?.messages is { } messages)
                    previous.AddRange(messages.Where(m => m?.role == "assistant").Select(m => m.content));
                if (!string.IsNullOrWhiteSpace(dialog.SessionId))
                {
                    previous.AddRange((await Task.Run(() => session.DialogsOfSession(dialog.SessionId)))
                        .Where(d => d.id != dialog.id)
                        .SelectMany(d => d.response?.choices ?? new List<Choice>())
                        .Where(c => c?.finish_reason != "error" && c?.message is not null)
                        .Select(c => c.message.content));
                }
                if (previous.Count == 0 || Knowledge.AnswerRepeat.Repeated(response.Content, previous) is not { } repeated)
                    return response;
                session.LogSave($"The answer repeats one already given: \"{(response.Content.Length > 160 ? response.Content[..160] + "…" : response.Content)}\"", appName, "INFO");

                var template = await Task.Run(() => session.PromptTemplateCurrent("dialog.norepeat"));
                var instruction = template?.text is { Length: > 0 } edited && !string.IsNullOrWhiteSpace(edited) ? edited.Trim() : Knowledge.AnswerRepeat.DefaultInstruction;
                // Every answer the tries fall back on: they are all quoted as not to be said again.
                var forbidden = new List<string> { repeated };
                var spent = (Prompt: response.PromptTokens, Response: response.ResponseTokens, Total: response.TotalTokens);
                for (var attempt = 1; attempt <= Knowledge.AnswerRepeat.MaxRetries; attempt++)
                {
                    // The last try leaves out the previous exchanges: a history where the same answer comes back makes
                    // a small model copy it once more, whatever it is told.
                    var last = attempt == Knowledge.AnswerRepeat.MaxRetries;
                    var question = last ? Knowledge.ContextManager.Question(user, bare: true) : user.TrimEnd();
                    session.LogSave($"The answer repeats one already given in the conversation: the prompt is sent again with \"{instruction}\" (try {attempt} of {Knowledge.AnswerRepeat.MaxRetries}{(last ? ", without the previous exchanges" : string.Empty)})", appName, "INFO");
                    var asked = question.TrimEnd() + "\n\n" + Knowledge.AnswerRepeat.Addition(instruction, forbidden);
                    var again = await HelperOllamaClient.SendChatMessageAsync(config.ollamaUrl, model, asked,
                        string.IsNullOrWhiteSpace(system) ? instruction : system.TrimEnd() + "\n\n" + instruction, config.llmProvider, noCache: true,
                        temperature: Knowledge.AnswerRepeat.Temperature(attempt));
                    dialog.RepeatRetries = attempt;
                    dialog.PromptSentUserText = asked;
                    if (again.Error is not null)
                    {
                        session.LogSave($"Asked again, the model gave no answer ({again.Error})", appName, "ERROR");
                        break;
                    }
                    // The tokens of the tries add up: they were all spent.
                    spent = (spent.Prompt + again.PromptTokens, spent.Response + again.ResponseTokens, spent.Total + again.TotalTokens);
                    again.Content = Knowledge.PromptCompressor.WithoutLabel(again.Content);
                    response = again;
                    if (Knowledge.AnswerRepeat.Repeated(response.Content, previous) is not { } still)
                    {
                        session.LogSave($"Asked again, the model gave a new answer (try {attempt})", appName, "INFO");
                        (response.PromptTokens, response.ResponseTokens, response.TotalTokens) = spent;
                        return response;
                    }
                    if (!forbidden.Contains(still))
                        forbidden.Add(still);
                }

                // The model keeps repeating itself: its answer is not returned. The assistant says it has nothing new,
                // with a sentence it did not say yet (the prompt dialog.norepeat.fallback, one sentence per line).
                var fallbacks = await Task.Run(() => session.PromptTemplateCurrent("dialog.norepeat.fallback"));
                response.Content = Knowledge.AnswerRepeat.Fallback(fallbacks?.text, previous);
                (response.PromptTokens, response.ResponseTokens, response.TotalTokens) = spent;
                dialog.RepeatFallback = true;
                session.LogSave($"The model still repeats itself after {dialog.RepeatRetries} tries: its answer is not returned, the assistant says \"{response.Content}\"", appName, "WARNING");
            }
            catch (Exception ex)
            {
                session.LogSave("Repeated answers not checked: " + ex.Message, appName, "ERROR");
            }
            return response;
        }

        private async Task RunAsync(Dialog dialog, Config config, SessionManager session, string appName)
        {
            DialogGenericRequest req = dialog.request;
            dialog.PromptUserText = HelperLlmParsersAndTools.GetUserPromptFromRequest(req);
            dialog.PromptSystemText = HelperLlmParsersAndTools.GetSystemPromptFromRequest(req);
            // What the emotion engine found for this conversation (the text of /emotion for the emotion that leads).
            if (!string.IsNullOrWhiteSpace(dialog.EmotionSystemText))
            {
                dialog.PromptSystemText = string.IsNullOrWhiteSpace(dialog.PromptSystemText)
                    ? dialog.EmotionSystemText.Trim()
                    : dialog.PromptSystemText.TrimEnd() + "\n\n" + dialog.EmotionSystemText.Trim();
                session.LogSave($"System prompt: the text of the emotion {dialog.Emotion} (version {dialog.EmotionPromptVersion}) is added", appName, "INFO");
            }

            // The knowledge of the entities goes in the system prompt, and the whole is made to fit; the question of the
            // user is sent as it is (the semantic cache of the gateway compares it).
            // The context of the conversation, when the request names one and the server keeps it: its exchanges, the
            // older the more compressed, except what they say about the entities that come back (see ContextManager).
            Knowledge.RenderedContext? context = null;
            try
            {
                if (session.DialogContextGet(dialog.SessionId) is { turns.Count: > 0 } kept)
                {
                    var question = Knowledge.ContextManager.Question(dialog.PromptUserText, bare: true);
                    // The entity agent already ran the NER on this question (the bare question): its entities are on the dialog.
                    var entities = dialog.Entities is { Count: > 0 } found ? found
                        : await Task.Run(() => Ner.NerService.Get(config).Extract(question, types: null).entities);
                    context = Knowledge.ContextManager.Render(kept, Knowledge.ContextManager.EntityNames(entities), config, dialog.request?.regenerate == true ? question : null);
                }
            }
            catch (Exception ex)
            {
                session.LogSave("Context of the conversation not read: " + ex.Message, appName, "ERROR");
            }

            // A mere reaction of the user ("cool", "merci": nothing asked, no knowledge found for it) in a conversation
            // that has exchanges: the model is sent the message alone, with the instruction to answer in one short
            // sentence. With the previous exchanges before it, a small model has nothing to answer and says them again.
            var reaction = (context is not null || Knowledge.PromptCompressor.HasHistory(dialog.PromptUserText))
                && (dialog.Knowledge is null || dialog.Knowledge.Count == 0)
                && !Knowledge.ContextManager.IsRequest(Knowledge.ContextManager.Question(dialog.PromptUserText, bare: true));

            // A conversation that already has exchanges: the model is told not to introduce itself again (the persona of
            // the preset, alone in the system prompt on every prompt, made it say who it is each time).
            if (context is not null || Knowledge.PromptCompressor.HasHistory(dialog.PromptUserText))
            {
                var ongoing = await Task.Run(() => session.PromptTemplateCurrent("dialog.ongoing"));
                var instruction = !string.IsNullOrWhiteSpace(ongoing?.text) ? ongoing.text.Trim() : Knowledge.PromptCompressor.OngoingInstruction;
                dialog.PromptSystemText = string.IsNullOrWhiteSpace(dialog.PromptSystemText) ? instruction : dialog.PromptSystemText.TrimEnd() + "\n\n" + instruction;
            }

            var userText = dialog.PromptUserText;
            if (reaction)
            {
                var template = await Task.Run(() => session.PromptTemplateCurrent("dialog.reaction"));
                var instruction = !string.IsNullOrWhiteSpace(template?.text) ? template.text.Trim() : Knowledge.PromptCompressor.ReactionInstruction;
                dialog.PromptSystemText = string.IsNullOrWhiteSpace(dialog.PromptSystemText) ? instruction : dialog.PromptSystemText.TrimEnd() + "\n\n" + instruction;
                userText = Knowledge.ContextManager.Question(dialog.PromptUserText, bare: false);
                context = null;
                session.LogSave($"\"{Knowledge.ContextManager.Question(dialog.PromptUserText, bare: true)}\" is a reaction, not a request: it is sent alone, without the previous exchanges", appName, "INFO");
            }

            var prompt = Knowledge.PromptCompressor.Compress(userText, dialog.PromptSystemText, dialog.Knowledge,
                config.promptMaxChars, config.promptKnowledgeMaxChars, context);
            // The model is not sent the name the chat addresses the question to ("KAONOMI please …"): it took it for the
            // subject of the question. The agents before (cache, entities, context) still read the prompt as it came.
            var sentUser = Knowledge.ContextManager.WithoutAddress(prompt.User);
            dialog.PromptSentUserText = sentUser;
            dialog.PromptSentSystemText = prompt.System;
            dialog.PromptCompression = prompt.Stats;
            session.LogSave($"Prompt: {prompt.Stats.originalChars} → {prompt.Stats.sentChars} characters ({prompt.Stats.ratio} %)"
                + (prompt.Stats.steps.Count > 0 ? " · " + string.Join(" · ", prompt.Stats.steps) : " · nothing to compress"), appName, "INFO");

            string selectedModel = req.model;
            // Through ollamaUrl: the LLM gateway when it points to it (cache, router), else the model server itself.
            // The previous exchanges stay written as text in the prompt of the user: sent as turns of the conversation,
            // the answers of the model were as right and half as long again (it follows its own previous answers).
            OllamaChatResult response = await HelperOllamaClient.SendChatMessageAsync(
                config.ollamaUrl, selectedModel, sentUser, prompt.System, config.llmProvider);
            session.LogSave($"Model asked at {config.ollamaUrl} ({config.llmProvider}): {selectedModel} asked, {response.Model} answered", appName, "INFO");
            if (response.Error is null)
                response.Content = Knowledge.PromptCompressor.WithoutLabel(response.Content);
            response = await WithoutRepeatAsync(dialog, response, sentUser, prompt.System, selectedModel, config, session, appName);
            if (response.Error is null)
                response.Content = Knowledge.PromptCompressor.WithoutLabel(response.Content);

            dialog.response.id = Guid.NewGuid().ToString();
            dialog.response.choices = new List<Choice>
            {
                new Choice
                {
                    message = new Message
                    {
                        content = response.Content,
                        role = "assistant"
                    },
                    index = 0,
                    // A failure of the model is said as one ("error"): the message shown is not an answer. The agents
                    // that follow and the client tell them apart (nothing is kept in the context of the conversation).
                    finish_reason = response.Error is null ? "stop" : "error"
                }
            };
            if (response.Error is not null)
            {
                session.LogSave($"The model gave no answer ({config.ollamaUrl}, {selectedModel}): {response.Error}", appName, "ERROR");
                dialog.response.model = dialog.request.model;
                return;
            }

            // The model that answered (the gateway may have routed the request to another one than the model asked).
            dialog.response.model = string.IsNullOrWhiteSpace(response.Model) ? dialog.request.model : response.Model;
            dialog.response.usage = new Usage
            {
                prompt_tokens = response.PromptTokens,
                completion_tokens = response.ResponseTokens,
                total_tokens = response.TotalTokens
            };

            try
            {
                var tokensUsage = new TokensUsage
                {
                    prompt_tokens = response.PromptTokens,
                    completion_tokens = response.ResponseTokens,
                    date = DateTime.UtcNow,
                    model = dialog.response.model,
                    appName = appName,
                    comment = "",
                    machineName = Environment.MachineName,
                    userName = Environment.UserName
                };

                session.TokensUsageSave(tokensUsage);
            }
            catch (Exception ex)
            {
                session.LogSave("Error saving tokens usage: " + ex.Message, appName, "ERROR");
            }
        }
    }
}