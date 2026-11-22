using System;
using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A web page collected by the agent KAONOMI.Agent.WebScraper.Collect (RUN of the collection plan window), index
    /// <c>page</c>: its content cut by titles and paragraphs (<see cref="sections"/>), the entities the NER of the
    /// server found in it (<see cref="entities"/>: name and type), every URL it links to (<see cref="urls"/>), its
    /// pictures (<see cref="images"/>) and the picture of the page as a browser draws it (<see cref="renderingPath"/>).
    /// The files are in <see cref="folder"/>:
    /// <c>Pictures/kaonomi/scraper/&lt;collection plan&gt;/&lt;website&gt;/&lt;yyyyMMdd&gt;</c>.
    /// A <b>main page</b> (<see cref="isMainPage"/>: a front page, whose content changes with the news) is named by
    /// its title and the day (<c>&lt;title&gt; yyyyMMdd</c>) and has one document per day: collected again the same
    /// day, it is overwritten; another day, it is a new document. Any other page is named by its title and has one
    /// document, overwritten each time it is collected. The id tells it: it is made of the collection plan, the URL
    /// and, for a main page, the day.
    /// </summary>
    public class Page : EntityBase, ISoftDeletable
    {
        /// <summary>The name of the page: its title, followed by the day (<c>yyyyMMdd</c>) for a main page.</summary>
        public string name { get; set; } = string.Empty;

        /// <summary>The day it was collected, <c>yyyyMMdd</c> (the name of its folder).</summary>
        public string day { get; set; } = string.Empty;

        /// <summary>True for a main page of the collection plan (the URL ticked "Main page"), not for the pages it links to.</summary>
        public bool isMainPage { get; set; }

        /// <summary>
        /// True for a page imported by hand: the site refused the agent, so the user opened the page in a browser,
        /// copied it, and the agent kept what was copied (its content, entities and URLs; no rendering, and its
        /// pictures are listed but not downloaded).
        /// </summary>
        public bool isImported { get; set; }

        /// <summary>The URL asked.</summary>
        public string url { get; set; } = string.Empty;

        /// <summary>The URL reached after the redirections.</summary>
        public string finalUrl { get; set; } = string.Empty;

        /// <summary>The URL of the collection plan this page comes from (the same as <see cref="url"/> for a start page).</summary>
        public string startUrl { get; set; } = string.Empty;

        /// <summary>How far from the start URL (0: the start URL itself).</summary>
        public int depth { get; set; }

        /// <summary>fixed, explore or random: the mode of the URL of the collection plan.</summary>
        public string mode { get; set; } = string.Empty;

        /// <summary>The keywords of the URL of the collection plan.</summary>
        public List<string> keywords { get; set; } = new List<string>();

        /// <summary>The keywords found in the text of the page.</summary>
        public List<string> keywordsFound { get; set; } = new List<string>();

        public string collectionPlanId { get; set; } = string.Empty;

        public string collectionPlanName { get; set; } = string.Empty;

        public string websiteName { get; set; } = string.Empty;

        /// <summary>The HTTP status of the page (0 when unknown).</summary>
        public int httpStatus { get; set; }

        /// <summary>The title of the HTML page.</summary>
        public string pageTitle { get; set; } = string.Empty;

        /// <summary>The language declared by the page (<c>html lang</c>), or empty.</summary>
        public string pageLanguage { get; set; } = string.Empty;

        /// <summary>
        /// The content, cut by titles and paragraphs, in the order of the page: each title (h1 to h6) opens a section
        /// that holds the paragraphs up to the next title; what comes before the first title is a section without title.
        /// </summary>
        public List<PageSection> sections { get; set; } = new List<PageSection>();

        /// <summary>The whole text: the titles and the paragraphs one after the other.</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>SHA-256 of <see cref="text"/>: two collections with the same hash saw the same content.</summary>
        public string contentHash { get; set; } = string.Empty;

        /// <summary>The entities the NER found in the text, each once (name and type), the most frequent first.</summary>
        public List<PageEntity> entities { get; set; } = new List<PageEntity>();

        /// <summary>Every URL the page links to (absolute, each once, in the order of the page).</summary>
        public List<string> urls { get; set; } = new List<string>();

        /// <summary>The pictures of the page and where they were downloaded.</summary>
        public List<PageImage> images { get; set; } = new List<PageImage>();

        /// <summary>The full path of the picture of the whole page as a browser draws it (PNG), or empty when it failed.</summary>
        public string renderingPath { get; set; } = string.Empty;

        /// <summary>The folder of the rendering and of the pictures.</summary>
        public string folder { get; set; } = string.Empty;

        public int paragraphCount { get; set; }

        /// <summary>When the page was collected.</summary>
        public DateTime scrapedAt { get; set; }

        /// <summary>How long the collection took, in milliseconds.</summary>
        public int durationMs { get; set; }

        /// <summary>What went wrong (rendering, pictures, NER…), the page being saved anyway.</summary>
        public List<string> warnings { get; set; } = new List<string>();

        public bool isDeleted { get; set; }
    }

    /// <summary>A title of a page and the paragraphs under it.</summary>
    public class PageSection
    {
        /// <summary>1 to 6 for h1 to h6; 0 for what comes before the first title.</summary>
        public int level { get; set; }

        /// <summary>The title, or empty (level 0).</summary>
        public string title { get; set; } = string.Empty;

        public List<string> paragraphs { get; set; } = new List<string>();
    }

    /// <summary>An entity of a page: its name and its type (PERSON, ORG, LOC, DATE… or a type of the ontology).</summary>
    public class PageEntity
    {
        /// <summary>The name: the title of the entity of the ontology when it is known there, else the text as written.</summary>
        public string name { get; set; } = string.Empty;

        public string type { get; set; } = string.Empty;

        /// <summary>How many times it was found in the page.</summary>
        public int count { get; set; }
    }

    /// <summary>A picture of a page: where it comes from and where it was downloaded.</summary>
    public class PageImage
    {
        /// <summary>The absolute URL of the picture.</summary>
        public string url { get; set; } = string.Empty;

        /// <summary>The text of its <c>alt</c>, or empty.</summary>
        public string alt { get; set; } = string.Empty;

        /// <summary>
        /// The text around the picture in the page (of its figure, article, list item or link, 300 characters at most):
        /// what ties it to a title or a paragraph of the page. Empty when the page gives none.
        /// </summary>
        public string context { get; set; } = string.Empty;

        /// <summary>The full path of the downloaded file, or empty when the download failed.</summary>
        public string path { get; set; } = string.Empty;

        public string contentType { get; set; } = string.Empty;

        public long bytes { get; set; }

        /// <summary>Why the download failed, or empty.</summary>
        public string error { get; set; } = string.Empty;
    }
}
