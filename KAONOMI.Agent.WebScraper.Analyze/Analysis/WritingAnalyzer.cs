using KAONOMI.Chat.Services;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// How a text is written (<see cref="WritingProfile"/>): the mean length of its sentences, the complexity of its
    /// vocabulary, how many adverbs it has and how many of its sentences are in the conditional. The counting is the one
    /// of the chat (<see cref="WritingCounter"/>, the file KAONOMI.ChatGodot/Shared/Services/WritingCounter.cs, compiled
    /// here too), so that /entity of the chat and this agent count the same way.
    /// </summary>
    public static class WritingAnalyzer
    {
        /// <summary>The sentences of the blocks of a text: a block is at least one sentence (a title has no full stop).</summary>
        public static List<string> Sentences(IEnumerable<string> blocks) => WritingCounter.Sentences(blocks);

        /// <summary>The profile of the blocks of text, or null when they hold no word.</summary>
        public static WritingProfile? Profile(IEnumerable<string> blocks, string? language) =>
            WritingCounter.Count(blocks, language) is { } counts
                ? new WritingProfile
                {
                    sentenceCount = counts.SentenceCount,
                    wordCount = counts.WordCount,
                    averageSentenceLength = counts.AverageSentenceLength,
                    averageWordLength = counts.AverageWordLength,
                    lexicalDiversity = counts.LexicalDiversity,
                    longWordShare = counts.LongWordShare,
                    vocabularyComplexity = counts.VocabularyComplexity,
                    adverbCount = counts.AdverbCount,
                    conditionalSentenceCount = counts.ConditionalSentenceCount
                }
                : null;

        public static bool IsAdverb(string word, bool english) => WritingCounter.IsAdverb(word, english);

        public static bool IsConditional(string word) => WritingCounter.IsConditional(word);
    }
}
