using System;
using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// The emotions found in the prompts of a conversation, kept in the search engine (index <c>detectedemotion</c>).
    /// The emotion engine of the dialog sequence looks at them every 3 prompts, on these 3 prompts together: one
    /// document per group of 3, with the share of each emotion in per cent (the same seven as the ones /vision
    /// reads on a face, so that what is written and what is shown can be compared), the one that leads, how strong it
    /// is, whether the mood is pleasant or not, and one sentence saying why. The id is made from the dialog that
    /// closes the group: a dialog processed again writes its document over, it never doubles it.
    /// </summary>
    public class DetectedEmotion : EntityBase, ISoftDeletable
    {
        /// <summary>The emotions the analysis tells apart, in the order of <see cref="EmotionShares"/>.</summary>
        public static readonly string[] Names = ["angry", "disgust", "fearful", "happy", "neutral", "sad", "surprised"];

        /// <summary>How many prompts are looked at together, and how often (every that many prompts).</summary>
        public const int Window = 3;

        /// <summary>
        /// What the model is told when the search engine holds no version of the prompt <c>dialog.emotions</c> (edited
        /// in /prompts of the chat, whose default is this text).
        /// </summary>
        public const string DefaultPrompt =
            "You analyse the emotions of a person from the last messages they wrote to an assistant. Judge the person who writes, from their words, "
            + "their punctuation and what they ask: not the subject they talk about (a question about a sad event is not sadness). "
            + "Give the share of each of these emotions, in per cent, the seven adding up to 100: {{emotions}}. Messages that only ask for something, "
            + "without feeling, are mostly neutral. Also give: \"intensity\", how strong the leading emotion is, from 0 (none) to 10 (overwhelming); "
            + "\"valence\", how pleasant the mood is, from -1 (very unpleasant) to 1 (very pleasant), 0 when neutral; \"summary\", one short sentence in {{language}} "
            + "saying what shows these emotions. Answer with a JSON object only, no Markdown: "
            + "{\"emotions\": {\"angry\": 0, \"disgust\": 0, \"fearful\": 0, \"happy\": 0, \"neutral\": 0, \"sad\": 0, \"surprised\": 0}, \"intensity\": 0, \"valence\": 0, \"summary\": \"\"}.";

        public DetectedEmotion()
        {
        }

        /// <summary>The emotions of the group of prompts that the dialog <paramref title="dialogId"/> closes.</summary>
        public DetectedEmotion(string dialogId, string? sessionId) : base("detectedemotion-" + dialogId)
        {
            this.dialogId = dialogId;
            this.sessionId = sessionId;
            date = DateTime.Now;
            machineName = Environment.MachineName;
            userName = Environment.UserName;
        }

        /// <summary>The conversation, and the dialog that closes the group of prompts (the last of them).</summary>
        public string? sessionId { get; set; }

        public string dialogId { get; set; } = string.Empty;

        /// <summary>The dialogs of the group, the oldest first, and their prompts as the user wrote them.</summary>
        public List<string> dialogIds { get; set; } = new List<string>();

        public List<string> prompts { get; set; } = new List<string>();

        /// <summary>The rank in the conversation of the last prompt of the group (3, 6, 9…).</summary>
        public int promptNumber { get; set; }

        /// <summary>The emotion that leads, and the share of each, in per cent.</summary>
        public string emotion { get; set; } = string.Empty;

        public EmotionShares emotions { get; set; } = new EmotionShares();

        /// <summary>How strong the leading emotion is, from 0 to 10.</summary>
        public double intensity { get; set; }

        /// <summary>How pleasant the mood is, from -1 (very unpleasant) to 1 (very pleasant).</summary>
        public double valence { get; set; }

        /// <summary>One sentence saying what shows these emotions.</summary>
        public string summary { get; set; } = string.Empty;

        /// <summary>The model that analysed, and the version of the prompt it was given (0: the default of the code).</summary>
        public string model { get; set; } = string.Empty;

        public int promptVersion { get; set; }

        /// <summary>
        /// What was added to the system prompt of the answer for this emotion (the text of /emotion of the chat for the
        /// emotion that leads), and its version; empty when /emotion gives no text for it.
        /// </summary>
        public string systemText { get; set; } = string.Empty;

        public int systemTextVersion { get; set; }

        public bool isDeleted { get; set; } = false;
    }
}
