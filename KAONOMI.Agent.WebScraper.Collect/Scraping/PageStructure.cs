using System.Text.RegularExpressions;
using HtmlAgilityPack;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Collect.Scraping
{
    /// <summary>
    /// Cuts the HTML of a page by titles and paragraphs: the text blocks (p, li, blockquote, h1 to h6, dd, figcaption,
    /// pre) out of the parts that are not content (scripts, styles, menus, headers and footers, forms). Each title opens
    /// a section that holds the paragraphs up to the next title; what comes before the first title is a section without
    /// title. A block that holds another block keeps only the inner ones; blocks shorter than the minimum length are
    /// left out (a title is kept from three characters), and a text met twice is kept once.
    /// </summary>
    public static class PageStructure
    {
        private static readonly string[] NotContent =
            { "script", "style", "noscript", "nav", "footer", "header", "aside", "form", "svg", "iframe", "button", "select", "template" };

        private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
            { "p", "li", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6", "dd", "figcaption", "pre" };

        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        public static List<PageSection> Split(string html, int minLength)
        {
            var document = new HtmlDocument();
            document.LoadHtml(html ?? string.Empty);
            var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;

            // A header inside the content (the title block of an article) is content: only the headers of the page go.
            foreach (var name in NotContent)
                foreach (var node in root.SelectNodes($".//{name}")?.ToList() ?? new List<HtmlNode>())
                    if (name != "header" || !node.Ancestors().Any(a => a.Name is "article" or "main" or "section"))
                        node.Remove();
            // Hidden parts (cookie banners, menus closed) are not content either.
            foreach (var node in root.SelectNodes(".//*[@aria-hidden='true' or @hidden]")?.ToList() ?? new List<HtmlNode>())
                node.Remove();

            var sections = new List<PageSection>();
            PageSection? current = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in root.Descendants().Where(n => n.NodeType == HtmlNodeType.Element && Blocks.Contains(n.Name)))
            {
                // The innermost blocks only: a li holding p keeps the p.
                if (node.Descendants().Any(d => d.NodeType == HtmlNodeType.Element && Blocks.Contains(d.Name)))
                    continue;

                var text = Spaces.Replace(HtmlEntity.DeEntitize(node.InnerText) ?? string.Empty, " ").Trim();
                var isTitle = node.Name.Length == 2 && node.Name[0] == 'h' && char.IsDigit(node.Name[1]);
                if (text.Length < (isTitle ? 3 : minLength) || !seen.Add(text))
                    continue;
                if (isTitle)
                {
                    current = new PageSection { level = node.Name[1] - '0', title = text };
                    sections.Add(current);
                    continue;
                }
                if (current is null)
                {
                    current = new PageSection();
                    sections.Add(current);
                }
                current.paragraphs.Add(text);
            }
            return sections;
        }

        /// <summary>The titles and the paragraphs, one after the other (what the NER reads).</summary>
        public static IEnumerable<string> BlocksOf(IEnumerable<PageSection> sections)
        {
            foreach (var section in sections)
            {
                if (section.title.Length > 0)
                    yield return section.title;
                foreach (var paragraph in section.paragraphs)
                    yield return paragraph;
            }
        }

        /// <summary>The whole text: the titles and the paragraphs, a blank line between two.</summary>
        public static string TextOf(IEnumerable<PageSection> sections) => string.Join("\n\n", BlocksOf(sections));
    }
}
