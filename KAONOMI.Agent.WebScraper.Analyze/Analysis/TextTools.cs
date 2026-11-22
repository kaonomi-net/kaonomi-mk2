using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>The text of a collected page, as the analysis reads it: its blocks (titles and paragraphs), parts, passages about a name.</summary>
    public static class TextTools
    {
        /// <summary>The text in lower case and without accents: names are found whatever their case and accents.</summary>
        public static string Fold(string? text)
        {
            var folded = new StringBuilder();
            foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    folded.Append(char.ToLowerInvariant(ch == '’' ? '\'' : ch));
            return Regex.Replace(folded.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>The titles and the paragraphs of the page, one after the other.</summary>
        public static List<string> Blocks(Page page)
        {
            var blocks = new List<string>();
            foreach (var section in page.sections ?? new List<PageSection>())
            {
                if (!string.IsNullOrWhiteSpace(section.title))
                    blocks.Add(section.title.Trim());
                blocks.AddRange((section.paragraphs ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            }
            return blocks;
        }

        /// <summary>True when the block names one of <paramref name="foldedNames"/> as a whole word (the names already folded).</summary>
        public static bool Names(string foldedBlock, IEnumerable<string> foldedNames)
        {
            foreach (var name in foldedNames)
            {
                if (name.Length < 2)
                    continue;
                var at = foldedBlock.IndexOf(name, StringComparison.Ordinal);
                while (at >= 0)
                {
                    var before = at == 0 || !char.IsLetterOrDigit(foldedBlock[at - 1]);
                    var end = at + name.Length;
                    var after = end >= foldedBlock.Length || !char.IsLetterOrDigit(foldedBlock[end]);
                    if (before && after)
                        return true;
                    at = foldedBlock.IndexOf(name, at + 1, StringComparison.Ordinal);
                }
            }
            return false;
        }

        /// <summary>
        /// The blocks grouped into parts of at most <paramref name="chunkChars"/> characters, up to
        /// <paramref name="maxChars"/> in all: <paramref name="leftOut"/> tells how many blocks of the end were not kept.
        /// </summary>
        public static List<List<string>> Parts(IReadOnlyList<string> blocks, int chunkChars, int maxChars, out int leftOut)
        {
            var parts = new List<List<string>>();
            var current = new List<string>();
            int size = 0, total = 0;
            leftOut = 0;
            for (var i = 0; i < blocks.Count; i++)
            {
                if (total + blocks[i].Length > maxChars && total > 0)
                {
                    leftOut = blocks.Count - i;
                    break;
                }
                if (size + blocks[i].Length > chunkChars && current.Count > 0)
                {
                    parts.Add(current);
                    current = new List<string>();
                    size = 0;
                }
                current.Add(blocks[i]);
                size += blocks[i].Length + 1;
                total += blocks[i].Length + 1;
            }
            if (current.Count > 0)
                parts.Add(current);
            return parts;
        }

        /// <summary>The blocks that name the entity (its name or one of its aliases).</summary>
        public static List<string> Passages(IReadOnlyList<string> blocks, IReadOnlyList<string> foldedBlocks, AnalysedEntity entity)
        {
            var names = FoldedNames(entity);
            var passages = new List<string>();
            for (var i = 0; i < blocks.Count; i++)
                if (Names(foldedBlocks[i], names))
                    passages.Add(blocks[i]);
            return passages;
        }

        public static List<string> FoldedNames(AnalysedEntity entity) =>
            (entity.aliases ?? new List<string>()).Prepend(entity.name).Select(Fold).Where(n => n.Length >= 2).Distinct().ToList();

        /// <summary>The start of <paramref name="text"/>, cut at a word, at most <paramref name="most"/> characters.</summary>
        public static string Clip(string text, int most)
        {
            if (text.Length <= most)
                return text;
            var cut = text.LastIndexOf(' ', most);
            return text[..(cut > most / 2 ? cut : most)] + "…";
        }
    }
}
