using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KAONOMI.Server.Knowledge
{
    /// <summary>
    /// Tells whether an answer of the model repeats one it already gave in the conversation. The processor then asks
    /// again, with the instruction not to repeat itself added to the prompt (<see cref="DefaultInstruction"/>, the
    /// prompt <c>dialog.norepeat</c> of /prompts of the chat).
    /// </summary>
    public static class AnswerRepeat
    {
        /// <summary>How many times the model is asked again when it keeps repeating itself.</summary>
        public const int MaxRetries = 2;

        /// <summary>Under this many letters and digits, an answer is not compared ("Oui.", "42": the same short answer is not a repetition).</summary>
        public const int MinChars = 20;

        /// <summary>From this share of common words (0 to 1), two answers are the same one.</summary>
        public const double SameShare = 0.9;

        /// <summary>
        /// From this share (0 to 1) of its pairs of words already written in the previous answers taken together, an
        /// answer says nothing new: it is made of pieces of what was said, even when it copies no single answer.
        /// </summary>
        public const double SaidShare = 0.7;

        /// <summary>
        /// Under this many pairs of words (about 20 words), an answer is not checked for pieces of the previous ones:
        /// a short answer that says a fact again ("Kaonomi a été créé par Jérôme Fortias.") answers a new question.
        /// </summary>
        public const int MinPairs = 20;

        /// <summary>The pairs of words that follow each other in a normalised text.</summary>
        private static HashSet<string> Pairs(string normalised)
        {
            var words = normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var pairs = new HashSet<string>();
            for (var i = 0; i + 1 < words.Length; i++)
                pairs.Add(words[i] + " " + words[i + 1]);
            return pairs;
        }

        /// <summary>What is added to the prompt when the search engine holds no version of the prompt <c>dialog.norepeat</c>.</summary>
        public const string DefaultInstruction =
            "Ne te répète pas : tu as déjà donné cette réponse dans cette conversation. Réponds autrement, avec d'autres mots, et apporte quelque chose de nouveau.";

        /// <summary>
        /// What the assistant says when the model still repeats itself after every try, one sentence per line, when
        /// the search engine holds no version of the prompt <c>dialog.norepeat.fallback</c>: the first one not said
        /// yet in the conversation is the answer, so that a repeated answer is never returned.
        /// </summary>
        public const string DefaultFallbacks =
            "Je n'ai rien de nouveau à ajouter à ce que je t'ai déjà répondu. Dis-moi ce que tu veux savoir de plus précis.\n" +
            "Je t'ai déjà donné tout ce que je sais là-dessus. Sur quel point veux-tu que j'aille plus loin ?\n" +
            "Je ne veux pas te redire la même chose : je n'ai pas d'autre information pour l'instant. Peux-tu reformuler ta demande ?\n" +
            "Là-dessus, je n'en sais pas plus que ce que j'ai dit plus haut. On peut aborder la question sous un autre angle si tu veux.\n" +
            "Je risque de tourner en rond : ma réponse serait la même qu'avant. Donne-moi un détail de plus et je reprends.";

        /// <summary>
        /// The sentence of <paramref name="fallbacks"/> (one per line) not said yet in <paramref name="previous"/>;
        /// when they were all said, the first one with how many times the question came back, which makes it new.
        /// </summary>
        public static string Fallback(string? fallbacks, IReadOnlyCollection<string?> previous)
        {
            var lines = (string.IsNullOrWhiteSpace(fallbacks) ? DefaultFallbacks : fallbacks).Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0)
                lines = DefaultFallbacks.Split('\n').ToList();
            foreach (var line in lines)
            {
                var text = Normalise(line);
                if (!previous.Any(p => Normalise(p) == text))
                    return line;
            }
            for (var n = 2; ; n++)
            {
                var line = $"{lines[0]} ({n})";
                var text = Normalise(line);
                if (!previous.Any(p => Normalise(p) == text))
                    return line;
            }
        }

        /// <summary>
        /// What is added to the prompt: <paramref name="instruction"/>, then every answer in <paramref name="repeated"/>,
        /// quoted (cut when long), so that the model knows what not to say again.
        /// </summary>
        public static string Addition(string instruction, IEnumerable<string?> repeated)
        {
            var text = new StringBuilder(instruction);
            var given = repeated.Select(r => (r ?? string.Empty).Trim()).Where(r => r.Length > 0).Select(r => r.Length > 1200 ? r[..1200] + "…" : r).ToList();
            if (given.Count > 0)
                text.Append(given.Count == 1 ? "\n\nRéponse déjà donnée, à ne pas redire :" : "\n\nRéponses déjà données, à ne pas redire :");
            foreach (var answer in given)
                text.Append("\n\"\"\"\n").Append(answer).Append("\n\"\"\"");
            return text.ToString();
        }

        /// <summary>The temperature of the model on each new try: it rises, so that the answer moves away from the one it keeps giving.</summary>
        public static double Temperature(int attempt) => Math.Min(0.8 + 0.3 * attempt, 1.5);

        /// <summary>The letters and digits of <paramref name="text"/> in lower case, one space between the words: the Markdown, the punctuation and the case do not count.</summary>
        public static string Normalise(string? text)
        {
            var result = new StringBuilder();
            var space = false;
            foreach (var c in text ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (space && result.Length > 0)
                        result.Append(' ');
                    result.Append(char.ToLowerInvariant(c));
                    space = false;
                }
                else
                {
                    space = true;
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// The answer of <paramref name="previous"/> that <paramref name="answer"/> repeats, or null: the same text
        /// once normalised, or nearly the same words (<see cref="SameShare"/>) for a close length.
        /// </summary>
        public static string? Repeated(string? answer, IReadOnlyCollection<string?> previous)
        {
            var text = Normalise(answer);
            if (text.Replace(" ", string.Empty).Length < MinChars)
                return null;
            var words = text.Split(' ').ToHashSet();
            foreach (var other in previous)
            {
                var known = Normalise(other);
                if (known.Length == 0)
                    continue;
                if (known == text)
                    return other;
                if (Math.Min(known.Length, text.Length) < 0.8 * Math.Max(known.Length, text.Length))
                    continue;
                var knownWords = known.Split(' ').ToHashSet();
                var common = words.Count(knownWords.Contains);
                if (common >= SameShare * (words.Count + knownWords.Count - common))
                    return other;
            }

            // Made of pieces of several answers: most of its pairs of words were already written. The answer it takes
            // the most from is the one given back.
            var pairs = Pairs(text);
            if (pairs.Count < MinPairs)
                return null;
            var said = new HashSet<string>();
            string? closest = null;
            var most = 0;
            foreach (var other in previous)
            {
                var otherPairs = Pairs(Normalise(other));
                var shared = pairs.Count(otherPairs.Contains);
                if (shared > most)
                    (most, closest) = (shared, other);
                said.UnionWith(otherPairs);
            }
            return closest is not null && pairs.Count(said.Contains) >= SaidShare * pairs.Count ? closest : null;
        }
    }
}
