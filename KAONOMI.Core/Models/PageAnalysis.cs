using System;
using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// The analysis of a web page collected by KAONOMI.Agent.WebScraper.Collect, made by the agent
    /// KAONOMI.Agent.WebScraper.Analyze: index <c>pageanalysis</c>, one document per page (its id is made of the id of
    /// the page, so analysing a page again overwrites its analysis); <see cref="sourcepageid"/> is the id of the page.
    /// It holds the persons, organisations and events of the page with what was done in the ontology for each
    /// (<see cref="entities"/>: linked or created, its emotional analysis and how the text about it is written), the
    /// relations between them as the data of a graph (<see cref="possibleRelations"/>, <see cref="graph"/>), how the
    /// page is written (<see cref="writing"/>) and the analysis of its pictures (<see cref="images"/>).
    /// </summary>
    public class PageAnalysis : EntityBase, ISoftDeletable
    {
        /// <summary>The id of the page analysed (a document of the index <c>page</c>).</summary>
        public string sourcepageid { get; set; } = string.Empty;

        /// <summary>The name of the page analysed.</summary>
        public string sourcePageName { get; set; } = string.Empty;

        public string sourceUrl { get; set; } = string.Empty;

        /// <summary>The <c>contentHash</c> of the page when it was analysed: the page changed since when its hash differs.</summary>
        public string sourceContentHash { get; set; } = string.Empty;

        public string collectionPlanId { get; set; } = string.Empty;

        public string collectionPlanName { get; set; } = string.Empty;

        public string websiteName { get; set; } = string.Empty;

        /// <summary>The day the page was collected, <c>yyyyMMdd</c>.</summary>
        public string day { get; set; } = string.Empty;

        /// <summary>The model that analysed the text (the model of the advanced conversations).</summary>
        public string model { get; set; } = string.Empty;

        /// <summary>The model that analysed the pictures.</summary>
        public string imageModel { get; set; } = string.Empty;

        /// <summary>The steps done: entities, ontology, relations, emotions, writing, images.</summary>
        public List<string> steps { get; set; } = new List<string>();

        /// <summary>The persons, organisations and events of the page.</summary>
        public List<AnalysedEntity> entities { get; set; } = new List<AnalysedEntity>();

        /// <summary>The relations that may hold between two kinds of entities: what the relations of <see cref="graph"/> are chosen from.</summary>
        public List<PossibleRelation> possibleRelations { get; set; } = new List<PossibleRelation>();

        /// <summary>The relations found in the page, as the nodes and edges of a graph.</summary>
        public RelationGraph graph { get; set; } = new RelationGraph();

        /// <summary>How the whole page is written.</summary>
        public WritingProfile writing { get; set; } = new WritingProfile();

        /// <summary>The analysis of the pictures of the page.</summary>
        public List<AnalysedImage> images { get; set; } = new List<AnalysedImage>();

        public DateTime analysedAt { get; set; }

        /// <summary>How long the analysis took, in milliseconds.</summary>
        public int durationMs { get; set; }

        /// <summary>What went wrong or was left out (a model that did not answer, a time budget reached…), the analysis being saved anyway.</summary>
        public List<string> warnings { get; set; } = new List<string>();

        public bool isDeleted { get; set; }

        /// <summary>The id of the analysis of the page <paramref name="pageId"/>.</summary>
        public static string IdFor(string pageId) => "analysis-" + pageId;
    }

    /// <summary>A person, an organisation or an event of a page, and what the analysis says of it.</summary>
    public class AnalysedEntity
    {
        public string name { get; set; } = string.Empty;

        /// <summary>PERSON, ORGANISATION or EVENT.</summary>
        public string type { get; set; } = string.Empty;

        /// <summary>The other names it is written with in the page.</summary>
        public List<string> aliases { get; set; } = new List<string>();

        /// <summary>Who or what it is, according to the page (one or two sentences).</summary>
        public string summary { get; set; } = string.Empty;

        /// <summary>What the page says about it.</summary>
        public string facts { get; set; } = string.Empty;

        /// <summary>For an event: its date when the page gives it (<c>yyyy-MM-dd</c>), else empty.</summary>
        public string date { get; set; } = string.Empty;

        /// <summary>How many blocks of the page (titles, paragraphs) name it.</summary>
        public int mentions { get; set; }

        /// <summary>The id of its entity in the ontology, or empty when the ontology was not written.</summary>
        public string ontologyId { get; set; } = string.Empty;

        /// <summary>"existing" (it was in the ontology), "created" (it was added), or "none" (the ontology was not written).</summary>
        public string ontologyStatus { get; set; } = "none";

        /// <summary>What was added to its entity of the ontology (the link of the page, synonyms, a summary…).</summary>
        public List<string> ontologyChanges { get; set; } = new List<string>();

        /// <summary>The emotions the page expresses about it, or null when they were not analysed.</summary>
        public EntityEmotion? emotion { get; set; }

        /// <summary>How the passages about it are written, or null when no passage names it.</summary>
        public WritingProfile? writing { get; set; }
    }

    /// <summary>
    /// The emotions a text expresses about an entity: the share of each of seven emotions (in per cent), the leading
    /// one, its intensity (0 to 1), the valence (-1 negative to 1 positive) and one sentence saying why.
    /// </summary>
    public class EntityEmotion
    {
        public EmotionShares emotions { get; set; } = new EmotionShares();

        /// <summary>angry, disgust, fearful, happy, neutral, sad or surprised.</summary>
        public string leading { get; set; } = string.Empty;

        public double intensity { get; set; }

        public double valence { get; set; }

        public string summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// How a text is written, measured on its sentences and words. The adverbs and the conditional are found by their
    /// form (endings and a list of words), in French and in English: counts to compare texts, not a grammatical analysis.
    /// </summary>
    public class WritingProfile
    {
        public int sentenceCount { get; set; }

        public int wordCount { get; set; }

        /// <summary>The mean number of words in a sentence.</summary>
        public double averageSentenceLength { get; set; }

        /// <summary>The mean number of letters in a word.</summary>
        public double averageWordLength { get; set; }

        /// <summary>The share of different words among the words (0 to 1), measured on windows of 100 words so that long texts compare with short ones.</summary>
        public double lexicalDiversity { get; set; }

        /// <summary>The share of long words (9 letters or more), 0 to 1.</summary>
        public double longWordShare { get; set; }

        /// <summary>The complexity of the vocabulary, 0 (simple) to 100 (rich and long words): from the diversity, the length of the words and the share of long words.</summary>
        public double vocabularyComplexity { get; set; }

        public int adverbCount { get; set; }

        /// <summary>How many sentences have a verb in the conditional.</summary>
        public int conditionalSentenceCount { get; set; }
    }

    /// <summary>A relation that may hold between an entity of <see cref="sourceType"/> and one of <see cref="targetType"/>.</summary>
    public class PossibleRelation
    {
        public string sourceType { get; set; } = string.Empty;

        public string targetType { get; set; } = string.Empty;

        /// <summary>The name of the relation (MEMBER_OF, LEADS, PARTICIPATES_IN…).</summary>
        public string type { get; set; } = string.Empty;

        public string meaning { get; set; } = string.Empty;
    }

    /// <summary>The data of a graph of relations: its nodes (the entities) and its edges (the relations).</summary>
    public class RelationGraph
    {
        public List<GraphNodeData> nodes { get; set; } = new List<GraphNodeData>();

        public List<GraphEdgeData> edges { get; set; } = new List<GraphEdgeData>();
    }

    /// <summary>A node of the graph: an entity. Its id is the id of its entity of the ontology when it has one.</summary>
    public class GraphNodeData
    {
        public string id { get; set; } = string.Empty;

        public string name { get; set; } = string.Empty;

        /// <summary>PERSON, ORGANISATION or EVENT: the label of the node.</summary>
        public string type { get; set; } = string.Empty;

        public string ontologyId { get; set; } = string.Empty;
    }

    /// <summary>An edge of the graph: a relation from one entity to another, with what the page says of it.</summary>
    public class GraphEdgeData
    {
        public string id { get; set; } = string.Empty;

        public string sourceId { get; set; } = string.Empty;

        public string targetId { get; set; } = string.Empty;

        public string source { get; set; } = string.Empty;

        public string target { get; set; } = string.Empty;

        /// <summary>The name of the relation, one of <see cref="PageAnalysis.possibleRelations"/> for the kinds of its two ends.</summary>
        public string type { get; set; } = string.Empty;

        public string description { get; set; } = string.Empty;

        /// <summary>The sentence of the page the relation comes from.</summary>
        public string evidence { get; set; } = string.Empty;

        /// <summary>How sure the model is, 0 to 100.</summary>
        public int confidence { get; set; }

        /// <summary>The id of the page the relation comes from.</summary>
        public string sourcepageid { get; set; } = string.Empty;
    }

    /// <summary>The analysis of a picture of a page by the image model.</summary>
    public class AnalysedImage
    {
        public string url { get; set; } = string.Empty;

        /// <summary>The full path of the file analysed.</summary>
        public string path { get; set; } = string.Empty;

        public string alt { get; set; } = string.Empty;

        /// <summary>What the picture shows.</summary>
        public string description { get; set; } = string.Empty;

        /// <summary>photo, drawing, chart, logo, map, screenshot…</summary>
        public string kind { get; set; } = string.Empty;

        /// <summary>The text written in the picture, or empty.</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>
        /// The entities of the page the picture is about, told by its caption (its alt) or by what is written in it;
        /// nobody is recognised by their face.
        /// </summary>
        public List<string> entities { get; set; } = new List<string>();

        /// <summary>Why it was not analysed, or empty.</summary>
        public string error { get; set; } = string.Empty;
    }
}
