using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// An entity found by the NER of the server in a dialog, or in a web page collected by the web scraper
    /// (KAONOMI.Agent.WebScraper.Collect, RUN of /collectionplan), kept in the search engine (index <c>detectedentity</c>),
    /// one document per entity found: the text as written and normalized, its type (any type: PERSON, ORG, LOC of the
    /// model; URL, EMAIL, DATE, MONEY, CODE… of the rules; any type of the ontology), where it is in the text, how sure,
    /// what found it, the entity of the ontology it is, and where it comes from: the dialog and session
    /// (<see cref="origin"/> "dialog"), or the page (<see cref="origin"/> "page": <see cref="sourcepageid"/>, its address,
    /// its collection plan, and the passage around the entity). The id is made from the dialog (or the page), the place
    /// and the type: the same dialog or page processed again writes the same documents over, it never doubles them.
    /// </summary>
    public class DetectedEntity : EntityBase, ISoftDeletable
    {
        /// <summary>Builds an empty entity (for the deserialization).</summary>
        public DetectedEntity()
        {
        }

        /// <summary>Builds the entity of <paramref title="entity"/> found in the dialog <paramref title="dialogId"/>.</summary>
        public DetectedEntity(NerEntity entity, string dialogId, string? sessionId)
            : base(MakeId(dialogId, entity))
        {
            text = entity.text;
            normalizedText = Normalize(entity.text);
            type = entity.type;
            start = entity.start;
            end = entity.end;
            score = entity.score;
            source = entity.source;
            ontologyId = entity.ontologyId;
            ontologyName = entity.ontologyName;
            this.dialogId = dialogId;
            this.sessionId = sessionId;
            date = DateTime.Now;
            machineName = Environment.MachineName;
            userName = Environment.UserName;
        }

        /// <summary>
        /// Builds the entity of <paramref name="entity"/> found in the part <paramref name="part"/> of the page
        /// <paramref name="pageId"/> (the text given to the NER: <paramref name="partText"/>; <see cref="start"/> and
        /// <see cref="end"/> are places in that part).
        /// </summary>
        public static DetectedEntity FromPage(NerEntity entity, string pageId, string url, string? collectionPlanId, int part, string partText)
        {
            var key = $"page|{pageId}|{part}|{entity.start}|{entity.end}|{entity.type}";
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 16).ToLowerInvariant();
            var from = Math.Clamp(entity.start - 80, 0, partText.Length);
            var to = Math.Clamp(entity.end + 80, from, partText.Length);
            return new DetectedEntity
            {
                id = id,
                origin = "page",
                text = entity.text,
                normalizedText = Normalize(entity.text),
                type = entity.type,
                start = entity.start,
                end = entity.end,
                part = part,
                score = entity.score,
                source = entity.source,
                ontologyId = entity.ontologyId,
                ontologyName = entity.ontologyName,
                sourcepageid = pageId,
                sourceUrl = url,
                collectionPlanId = collectionPlanId,
                context = string.Join(' ', partText[from..to].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)),
                date = DateTime.Now,
                machineName = Environment.MachineName,
                userName = Environment.UserName
            };
        }

        /// <summary>Where it was found: "dialog" (a prompt of a dialog) or "page" (a web page collected by the web scraper).</summary>
        public string origin { get; set; } = "dialog";

        /// <summary>For an entity of a page: the id of the page (a document of the index <c>page</c>), else null.</summary>
        public string? sourcepageid { get; set; }

        /// <summary>For an entity of a page: its address, else null.</summary>
        public string? sourceUrl { get; set; }

        /// <summary>For an entity of a page: the collection plan the page comes from, else null.</summary>
        public string? collectionPlanId { get; set; }

        /// <summary>For an entity of a page: the part of the text of the page given to the NER (0 for the first), where <see cref="start"/> and <see cref="end"/> are.</summary>
        public int part { get; set; }

        /// <summary>For an entity of a page: the passage around it (80 characters before and after).</summary>
        public string? context { get; set; }

        /// <summary>The text as written in the prompt.</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>The text trimmed, in lower case, spaces made single: to group the same entity written differently.</summary>
        public string normalizedText { get; set; } = string.Empty;

        /// <summary>The type, in upper case (PERSON, ORG, LOC, URL, EMAIL, DATE, MONEY, CODE…, or a type of the ontology).</summary>
        public string type { get; set; } = string.Empty;

        /// <summary>First character of the entity in the prompt.</summary>
        public int start { get; set; }

        /// <summary>Character after the last one of the entity (excluded).</summary>
        public int end { get; set; }

        /// <summary>0 to 1: the mean probability of the model, 1 for a rule or a title of the ontology.</summary>
        public double score { get; set; }

        /// <summary>"xlmr", "rule", "ontology" or "xlmr+ontology".</summary>
        public string source { get; set; } = string.Empty;

        /// <summary>The id of the entity of the ontology it is, or null.</summary>
        public string? ontologyId { get; set; }

        /// <summary>The title of that entity of the ontology, or null.</summary>
        public string? ontologyName { get; set; }

        /// <summary>The dialog the entity was found in.</summary>
        public string dialogId { get; set; } = string.Empty;

        /// <summary>The session of that dialog.</summary>
        public string? sessionId { get; set; }

        /// <summary>Never erased: marked deleted instead (<see cref="ISoftDeletable"/>).</summary>
        public bool isDeleted { get; set; }

        /// <summary>The text trimmed, in lower case, spaces made single.</summary>
        public static string Normalize(string? value) =>
            string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        /// <summary>The id of an entity: the same dialog, place and type give the same id.</summary>
        private static string MakeId(string dialogId, NerEntity entity)
        {
            var key = $"{dialogId}|{entity.start}|{entity.end}|{entity.type}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }
    }
}
