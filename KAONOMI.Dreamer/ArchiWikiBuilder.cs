namespace KAONOMI.Dreamer
{
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using KAONOMI.Core.Models;

    /// <summary>A fact sheet of the architecture repository of /archi, as <c>workspace.json</c> keeps it.</summary>
    internal sealed class ArchiSheet
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Owner { get; set; }
        public string? ParentId { get; set; }
        /// <summary>Plan, PhaseIn, Active, PhaseOut, EndOfLife: a date (yyyy-MM-dd) each, or nothing.</summary>
        public Dictionary<string, JsonElement>? Lifecycle { get; set; }
        public string? Criticality { get; set; }
        public string? AiRisk { get; set; }
        /// <summary>The fields of its kind (the provider of a model, the autonomy of an agent…).</summary>
        public Dictionary<string, JsonElement>? Fields { get; set; }
    }

    /// <summary>A relation between two fact sheets: its kind ("uses", "runs on"…) from one to the other.</summary>
    internal sealed class ArchiRelation
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }

    /// <summary>The architecture repository of /archi (<c>~/kaonomi/archi/workspace.json</c>).</summary>
    internal sealed class ArchiRepository
    {
        public string? Name { get; set; }
        public List<ArchiSheet> Sheets { get; set; } = new();
        public List<ArchiRelation> Relations { get; set; } = new();

        /// <summary>Reads the repository from the text of <c>workspace.json</c>.</summary>
        public static ArchiRepository Parse(string json) =>
            JsonSerializer.Deserialize<ArchiRepository>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ArchiRepository();
    }

    /// <summary>A page of the wiki as the builder writes it: its id (the slug of its title), its title, its content, its tags.</summary>
    internal sealed record ArchiWikiPage(string Id, string Title, string Content, List<string> Tags);

    /// <summary>
    /// Writes the wiki of the architecture from the repository of /archi: the home page (<c>kaonomi</c>), the overview,
    /// the business capabilities, a page per application of Kaonomi (the chat, the server, the search engine, the
    /// vision), the agents, the AI models, the tools and APIs, the data and knowledge, the technical base, the
    /// organisation and projects, the roadmap and the governance of AI. In French; the names of the fact sheets stay
    /// those of the repository. Each fact sheet has its section (what it is, its lifecycle, its criticality, its fields,
    /// its relations) on the page of its kind, with links between the pages (<c>[text](wiki:slug)</c>).
    /// <para>
    /// The content is in the format of the rich editor of /wiki: one paragraph per line, <c>#</c> <c>##</c> <c>###</c>
    /// headings, <c>**bold**</c>, <c>`code`</c>, links; the characters the editor reads as formatting are escaped.
    /// </para>
    /// <para>
    /// The sentences written for the architecture of Kaonomi itself (what happens to a question asked in the chat, to a
    /// collection on the web…) name fact sheets: a block that names one the repository no longer has, or that says
    /// something the repository no longer shows, is left out, the rest of the page is written. A fact sheet of a kind
    /// without a page of its own (a process, another application) goes to « Les autres fiches ».
    /// </para>
    /// <para>
    /// Nothing depends on the hour: the same repository gives the same pages, until a fact sheet enters another phase
    /// of its lifecycle (<see cref="AsOf"/> is the day the pages last changed for that reason or because the repository
    /// was changed).
    /// </para>
    /// </summary>
    internal sealed class ArchiWikiBuilder
    {
        private sealed class Missing : Exception
        {
        }

        private readonly List<ArchiSheet> _sheets;
        private readonly List<ArchiRelation> _relations;
        private readonly Dictionary<string, ArchiSheet> _byId;
        private readonly Dictionary<string, ArchiSheet> _byName;
        private readonly DateOnly _today;

        /// <summary>The day the pages describe: the day the repository was changed, or the last day a fact sheet entered a phase, the later of the two.</summary>
        public DateOnly AsOf { get; }

        /// <summary>The pages of <paramref name="repository"/> as of <paramref name="today"/>; <paramref name="changed"/> is the day the repository was last changed.</summary>
        public ArchiWikiBuilder(ArchiRepository repository, DateOnly today, DateOnly changed)
        {
            _sheets = repository.Sheets.Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            _byId = new Dictionary<string, ArchiSheet>();
            _byName = new Dictionary<string, ArchiSheet>();
            foreach (var sheet in _sheets)
            {
                _byId.TryAdd(sheet.Id, sheet);
                _byName.TryAdd(sheet.Name, sheet);
            }
            _relations = repository.Relations.Where(r => _byId.ContainsKey(r.From) && _byId.ContainsKey(r.To)).ToList();
            _today = today;
            var passed = _sheets.SelectMany(s => PhaseKeys.Select(k => Date(s, k))).Where(d => d is not null && d <= today).Select(d => d!.Value).ToList();
            AsOf = passed.Count > 0 && passed.Max() > changed ? passed.Max() : changed;
        }

        // ── The pages, and the page of each kind of fact sheet ──

        private const string Home = "kaonomi";
        private const string Others = "autres-fiches";

        private static readonly (string Key, string Title)[] Pages =
        {
            (Home, "Kaonomi"),
            ("architecture", "Vue d'ensemble de l'architecture"),
            ("capacites-metier", "Les capacités métier"),
            ("le-chat", "Le chat"),
            ("le-serveur", "Le serveur"),
            ("le-moteur-de-recherche", "Le moteur de recherche"),
            ("la-vision", "La vision"),
            ("les-agents", "Les agents"),
            ("les-modeles-ia", "Les modèles d'IA"),
            ("outils-et-api", "Les outils et les API"),
            ("donnees-et-connaissances", "Les données et les connaissances"),
            ("socle-technique", "Le socle technique"),
            ("organisation-et-projets", "L'organisation et les projets"),
            ("feuille-de-route", "La feuille de route"),
            ("gouvernance-ia", "La gouvernance de l'IA"),
            (Others, "Les autres fiches"),
        };

        private static string TitleOf(string key) => Pages.First(p => p.Key == key).Title;

        private static readonly Dictionary<string, string> PageOfType = new()
        {
            ["BusinessCapability"] = "capacites-metier",
            ["AIModel"] = "les-modeles-ia", ["Provider"] = "les-modeles-ia",
            ["Agent"] = "les-agents", ["MultiAgentSystem"] = "les-agents", ["Prompt"] = "les-agents", ["Evaluation"] = "les-agents", ["Guardrail"] = "les-agents",
            ["Tool"] = "outils-et-api", ["McpServer"] = "outils-et-api", ["RestApi"] = "outils-et-api", ["Interface"] = "outils-et-api",
            ["DataObject"] = "donnees-et-connaissances", ["Dataset"] = "donnees-et-connaissances", ["KnowledgeBase"] = "donnees-et-connaissances",
            ["VectorStore"] = "donnees-et-connaissances",
            ["ITComponent"] = "socle-technique", ["TechCategory"] = "socle-technique",
            ["Organization"] = "organisation-et-projets", ["UserGroup"] = "organisation-et-projets", ["Project"] = "organisation-et-projets",
        };

        private const string Chat = "KAONOMI Chat (Godot)";
        private const string OldChat = "KAONOMI Chat (Avalonia)";
        private const string ChatToServer = "Chat → Server";
        private const string Server = "KAONOMI Server";
        private const string Api = "Kaonomi REST API";
        private const string SearchEngine = "KAONOMI SearchEngine";
        private const string Vision = "KAONOMI Vision";
        private const string Assistant = "Kaonomi assistant";
        private const string Project = "Agents on the server";

        private static readonly Dictionary<string, string> PageOfName = new()
        {
            [Chat] = "le-chat", [OldChat] = "le-chat", [ChatToServer] = "le-chat",
            [Server] = "le-serveur", [Api] = "le-serveur",
            [SearchEngine] = "le-moteur-de-recherche", [Vision] = "la-vision",
        };

        private static string PageOf(ArchiSheet sheet) =>
            PageOfName.TryGetValue(sheet.Name, out var byName) ? byName : PageOfType.TryGetValue(sheet.Type, out var byType) ? byType : Others;

        // ── French ──

        private static readonly Dictionary<string, (string One, string Many)> TypeFr = new()
        {
            ["BusinessCapability"] = ("capacité métier", "capacités métier"), ["Process"] = ("processus", "processus"),
            ["Application"] = ("application", "applications"), ["Interface"] = ("interface", "interface"),
            ["DataObject"] = ("objet de données", "objets de données"), ["ITComponent"] = ("composant technique", "composants techniques"),
            ["TechCategory"] = ("catégorie technique", "catégorie technique"), ["Provider"] = ("fournisseur", "fournisseurs"),
            ["Organization"] = ("organisation", "organisation"), ["UserGroup"] = ("groupe d'utilisateurs", "groupe d'utilisateurs"),
            ["Project"] = ("projet", "projet"), ["AIModel"] = ("modèle d'IA", "modèles d'IA"), ["Agent"] = ("agent", "agents"),
            ["MultiAgentSystem"] = ("système multi-agents", "systèmes multi-agents"), ["McpServer"] = ("serveur MCP", "serveur MCP"),
            ["Tool"] = ("outil", "outil"), ["RestApi"] = ("API REST", "API REST"), ["Prompt"] = ("prompt", "prompt"),
            ["Dataset"] = ("jeu de données", "jeu de données"), ["VectorStore"] = ("base vectorielle", "base vectorielle"),
            ["KnowledgeBase"] = ("base de connaissances (RAG)", "base de connaissances (RAG)"),
            ["Evaluation"] = ("évaluation", "évaluation"), ["Guardrail"] = ("garde-fou", "garde-fou"),
        };

        private static string TypeOne(string type) => TypeFr.TryGetValue(type, out var fr) ? fr.One : Esc(type);

        private static string TypeCount(string type, int count) => TypeFr.TryGetValue(type, out var fr) ? (count > 1 ? fr.Many : fr.One) : Esc(type);

        /// <summary>
        /// What each fact sheet of the architecture of Kaonomi is: its description in the repository, and the same in
        /// French, written while the description is still that one; a description that was changed, and another fact
        /// sheet, keep their own text.
        /// </summary>
        private static readonly Dictionary<string, (string En, string Fr)> Fr = new()
        {
            ["Intelligence"] = ("Collect, analyse and share information", "Collecter, analyser et partager l'information. C'est la capacité de premier niveau du renseignement : elle se décompose en Collection et en Analysis."),
            ["Collection"] = ("Gather information from the web, documents and sensors", "Rassembler l'information venue du web, des documents et des capteurs."),
            ["Analysis"] = ("Understand and link what was collected", "Comprendre et relier ce qui a été collecté."),
            ["Assistance"] = ("Help people by conversation and voice", "Aider les personnes par la conversation et par la voix. C'est la capacité de premier niveau de l'assistance : elle se décompose en Conversation et en Perception."),
            ["Conversation"] = ("Answer questions, by text and voice", "Répondre aux questions, par le texte et par la voix."),
            ["Perception"] = ("See and hear: faces, objects, speech", "Voir et entendre : les visages, les objets, la parole."),
            ["Knowledge management"] = ("Keep and search documents, notes and relations", "Conserver et retrouver les documents, les notes et les relations."),
            [Chat] = ("The desktop of Kaonomi: the chat, the tools, the 3D avatar", "Le poste de travail de Kaonomi : le chat, les outils et l'avatar 3D."),
            [OldChat] = ("The first desktop of Kaonomi, replaced by the Godot chat", "Le premier poste de travail de Kaonomi, remplacé par le chat Godot."),
            [Server] = ("The server of Kaonomi: its REST API, its agents, its grains", "Le serveur de Kaonomi : son API REST, ses agents et ses grains."),
            [SearchEngine] = ("The search engine of the documents, notes, intels and their vectors", "Le moteur de recherche des documents, des notes, des intels et de leurs vecteurs."),
            [Vision] = ("Faces, objects and movements from the camera (OpenCV, YOLO)", "Les visages, les objets et les mouvements vus par la caméra (OpenCV, YOLO)."),
            [ChatToServer] = ("The chat calls the REST API of the server", "Le chat appelle l'API REST du serveur."),
            ["Conversations"] = ("The exchanges with the chat", "Les échanges avec le chat."),
            ["Documents"] = ("Documents, web pages and notes collected", "Les documents, les pages web et les notes collectés."),
            [".NET 8"] = ("The runtime of the server and of the chat", "L'environnement d'exécution du serveur et du chat."),
            ["Godot 4 (.NET)"] = ("The engine of the desktop", "Le moteur du poste de travail."),
            ["LM Studio"] = ("Runs local models behind an OpenAI compatible API", "Exécute des modèles locaux derrière une API compatible OpenAI."),
            ["Microsoft Orleans"] = ("The virtual actors (grains) of the server", "Les acteurs virtuels (grains) du serveur."),
            ["MongoDB"] = ("The database of the server", "La base de données du serveur."),
            ["Ollama"] = ("Runs the local models", "Exécute les modèles locaux."),
            ["AI runtimes"] = ("Where the models run", "Là où les modèles tournent."),
            ["Liquid AI"] = ("Small multimodal models", "Des petits modèles multimodaux."),
            ["Mistral AI"] = ("Models with open weights", "Des modèles à poids ouverts."),
            ["Kaonomi team"] = ("The people building Kaonomi", "Les personnes qui construisent Kaonomi."),
            ["Analysts"] = ("The users of the intelligence tools", "Les utilisateurs des outils de renseignement."),
            [Project] = ("Move the agents of the chat to the server, behind MCP", "Déplacer les agents du chat vers le serveur, derrière MCP."),
            ["Face recognition (OpenCV)"] = ("Recognises the faces enrolled in /vision:face", "Reconnaît les visages enrôlés dans /vision:face."),
            ["Next reasoning model"] = ("A larger model for the agents, under study", "Un modèle plus grand pour les agents, à l'étude."),
            ["Piper voices"] = ("Text to speech of the avatar", "La synthèse vocale de l'avatar."),
            ["YOLO"] = ("Detection of the objects of the camera", "La détection des objets de la caméra."),
            ["lfm3-omni-2b"] = ("The small multimodal model of the chat (text, audio, vision)", "Le petit modèle multimodal du chat (texte, audio, vision)."),
            ["mistral-small3.2"] = ("The vision model reading the pictures of the pages and the scans", "Le modèle de vision qui lit les images des pages et les scans."),
            ["nomic-embed-text"] = ("The embeddings of the documents", "Les plongements (embeddings) des documents."),
            ["whisper (ggml-small)"] = ("Speech to text of the dictation and the recorder", "La reconnaissance vocale de la dictée et de l'enregistreur."),
            ["Collection analyst"] = ("Reads the pages of a collection plan and writes the analysis", "Lit les pages d'un plan de collecte et rédige l'analyse."),
            ["Dreamer"] = ("Links the knowledge at night: relations, clusters, ideas", "Relie les connaissances la nuit : relations, regroupements, idées."),
            [Assistant] = ("Answers in the chat, with its persona and its voice", "Répond dans le chat, avec sa persona et sa voix."),
            ["Page reader"] = ("Reads the pictures of a web page, screen by screen", "Lit les images d'une page web, écran par écran."),
            ["Server agents"] = ("The agents of the server, as Orleans grains", "Les agents du serveur, sous forme de grains Orleans."),
            ["Web scraping pipeline"] = ("The scraper, the page reader and the analyst, one after the other", "Le scraper, le lecteur de pages et l'analyste, l'un après l'autre."),
            ["Kaonomi MCP"] = ("The tools of Kaonomi offered to other assistants", "Les outils de Kaonomi offerts à d'autres assistants."),
            ["Search"] = ("Searches the indices of the search engine", "Interroge les index du moteur de recherche."),
            [Api] = ("The API of the server", "L'API du serveur."),
            ["LM Studio API"] = ("The OpenAI compatible API of LM Studio", "L'API compatible OpenAI de LM Studio."),
            ["Ollama API"] = ("The API of Ollama", "L'API d'Ollama."),
            ["Collection analysis"] = ("The prompt of the collection analyst", "Le prompt de l'analyste de collecte."),
            ["Collected pages"] = ("The pages read by the collection plans", "Les pages lues par les plans de collecte."),
            ["Search engine vectors"] = ("The vectors of the documents in the search engine", "Les vecteurs des documents dans le moteur de recherche."),
            ["Kaonomi knowledge"] = ("Documents, notes and intels, searched by the chat", "Les documents, les notes et les intels, interrogés par le chat."),
            ["Answer faithfulness"] = ("Are the answers backed by the sources?", "Les réponses sont-elles appuyées par les sources ?"),
            ["Personal data masking"] = ("Masks the personal data before a model reads it", "Masque les données personnelles avant qu'un modèle ne les lise."),
        };

        /// <summary>What the fact sheet <paramref name="name"/> is: its French text, else its description in the repository as it is (a full stop at its end).</summary>
        private string About(string name)
        {
            var own = (_byName.TryGetValue(name, out var sheet) ? sheet.Description : null)?.Trim() ?? string.Empty;
            if (Fr.TryGetValue(name, out var known) && (sheet is null || own == known.En))
                return known.Fr;
            var description = Esc(own);
            if (description.Length == 0)
                return "Pas de description dans le référentiel.";
            return ".!?…".Contains(description[^1]) ? description : description + ".";
        }

        private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

        private static string Upper(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary><paramref name="text"/> to go on a sentence: its first letter in lower case, its last character (a full stop) left out.</summary>
        private static string Inline(string text) => text.Length < 2 ? text : Lower(text)[..^1];

        private static readonly (string Key, string Label)[] FieldFr =
        {
            ("kind", "Nature"), ("status", "Statut"), ("sponsor", "Sponsor"), ("provider", "Fournisseur"), ("family", "Famille"),
            ("parameters", "Paramètres"), ("context", "Fenêtre de contexte"), ("modality", "Modalité"), ("hosting", "Hébergement"),
            ("serving", "Servi par"), ("license", "Licence"), ("framework", "Framework"), ("autonomy", "Autonomie"),
            ("human", "Humain dans la boucle"), ("memory", "Mémoire"), ("goal", "But"), ("trigger", "Déclencheur"),
            ("pattern", "Schéma d'orchestration"), ("protocol", "Protocole"), ("termination", "Fin"), ("frequency", "Fréquence"),
            ("transport", "Transport"), ("tools", "Outils"), ("effects", "Effets"), ("url", "Adresse"), ("openapi", "OpenAPI"),
            ("style", "Style"), ("endpoints", "Points d'entrée"), ("key", "Clé"), ("version", "Version"), ("category", "Catégorie"),
            ("classification", "Classification"), ("personal", "Données personnelles"), ("source", "Source"), ("usage", "Usage"),
            ("retrieval", "Recherche"), ("engine", "Moteur"), ("metric", "Métrique"), ("score", "Score"), ("size", "Taille"),
            ("website", "Site"),
        };

        private static readonly Dictionary<string, string> ValueFr = new()
        {
            ["Local"] = "local", ["Open source"] = "open source", ["Open weights"] = "poids ouverts", ["Vision"] = "vision", ["Text"] = "texte",
            ["Multimodal"] = "multimodal", ["Embedding"] = "plongements (embeddings)", ["Text to speech"] = "synthèse vocale",
            ["Speech to text"] = "reconnaissance vocale", ["Yes"] = "oui", ["No"] = "non", ["Custom"] = "sur mesure",
            ["Assistant (answers)"] = "assistant (il répond)", ["Agent (acts, confirmed)"] = "agent (il agit, après confirmation)",
            ["Autonomous (acts alone)"] = "autonome (il agit seul)", ["Conversation"] = "la conversation", ["Knowledge graph"] = "le graphe de connaissances",
            ["None"] = "aucune", ["Schedule"] = "une planification", ["RUN of a collection plan"] = "le RUN d'un plan de collecte",
            ["Answer the user"] = "répondre à l'utilisateur", ["Custom built"] = "développement interne",
            ["Supervisor / hierarchical"] = "superviseur / hiérarchique", ["Sequential"] = "séquentiel", ["Orleans grains"] = "grains Orleans",
            ["In process"] = "dans le processus", ["When every page is read"] = "quand toutes les pages sont lues", ["Planned"] = "planifié",
            ["Hybrid"] = "hybride", ["Real time"] = "temps réel", ["OpenAI compatible"] = "compatible OpenAI", ["Confidential"] = "confidentiel",
            ["Internal"] = "interne", ["Retrieval (RAG)"] = "recherche (RAG)", ["Read only"] = "lecture seule", ["REST call"] = "appel REST",
            ["Runtime"] = "environnement d'exécution", ["Platform"] = "plateforme", ["Database"] = "base de données", ["Team"] = "équipe",
            ["Personal data masking"] = "masquage des données personnelles", ["Other"] = "autre",
        };

        /// <summary>A value of the repository in French, when it is one of its lists; as it is otherwise (not escaped).</summary>
        private static string Value(string value) => ValueFr.TryGetValue(value, out var fr) ? fr : value;

        private static readonly Dictionary<string, string> CriticalityFr = new()
        {
            ["Mission critical"] = "critique pour la mission", ["Business critical"] = "critique pour l'activité",
            ["Business operational"] = "opérationnelle pour l'activité", ["Administrative service"] = "service administratif",
        };

        private static string Criticality(string? value) => value is null ? string.Empty : CriticalityFr.TryGetValue(value, out var fr) ? fr : Esc(value);

        private static readonly Dictionary<string, string> RiskFr = new()
        {
            ["High"] = "élevé", ["Limited"] = "limité", ["Minimal"] = "minimal", ["Unacceptable"] = "inacceptable", ["Not assessed"] = "non évalué",
        };

        private static string Risk(string? value) => string.IsNullOrEmpty(value) ? "non évalué" : RiskFr.TryGetValue(value, out var fr) ? fr : Esc(value);

        /// <summary>A relation as a sentence: its source, this verb, its target (verbs that need no agreement with the source).</summary>
        private static readonly Dictionary<string, string> VerbFr = new()
        {
            ["uses"] = "utilise", ["supports"] = "soutient la capacité", ["runs on"] = "s'exécute sur", ["is part of"] = "fait partie de",
            ["calls"] = "appelle", ["reads"] = "lit", ["orchestrates"] = "orchestre", ["hosted by"] = "a pour hébergeur", ["writes"] = "écrit",
            ["exposes"] = "expose", ["evaluated by"] = "a pour évaluation", ["implements"] = "met en œuvre", ["provides"] = "fournit",
            ["consumes"] = "consomme", ["guarded by"] = "a pour garde-fou", ["prompted by"] = "reçoit ses consignes de", ["indexes"] = "indexe",
            ["embeds with"] = "calcule ses vecteurs avec", ["owned by"] = "appartient à", ["used by"] = "a pour utilisateurs",
        };

        private static string Verb(string kind) => VerbFr.TryGetValue(kind, out var fr) ? fr : "→ " + Esc(kind) + " →";

        // ── The repository ──

        private static readonly string[] PhaseKeys = { "Plan", "PhaseIn", "Active", "PhaseOut", "EndOfLife" };

        /// <summary>The characters the editor would read as formatting, written as it writes them.</summary>
        private static string Esc(string? text)
        {
            var escaped = new StringBuilder();
            foreach (var c in text ?? string.Empty)
                escaped.Append(c switch
                {
                    '\\' => "\\\\", '*' => "\\*", '`' => "\\`", '[' => "\\[", ']' => "\\]", '&' => "&amp;", '<' => "&lt;",
                    '\n' or '\r' => " ",
                    _ => c.ToString()
                });
            return escaped.ToString();
        }

        private static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.ToString()
        };

        private static string FieldOf(ArchiSheet sheet, string key) =>
            sheet.Fields is not null && sheet.Fields.TryGetValue(key, out var value) ? Text(value).Trim() : string.Empty;

        private static DateOnly? Date(ArchiSheet sheet, string key)
        {
            if (sheet.Lifecycle is null || !sheet.Lifecycle.TryGetValue(key, out var value))
                return null;
            var text = Text(value);
            return text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        }

        private static string DateFr(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        /// <summary>The fact sheet named <paramref name="name"/>; the block that needs it is left out when the repository has none.</summary>
        private ArchiSheet S(string name) => _byName.TryGetValue(name, out var sheet) ? sheet : throw new Missing();

        private static void Require(bool condition)
        {
            if (!condition)
                throw new Missing();
        }

        private string Field(string name, string key) => FieldOf(S(name), key);

        /// <summary>The date the fact sheet <paramref name="name"/> enters <paramref name="phase"/>; the block is left out when it has none.</summary>
        private string DateOf(string name, string phase) => Date(S(name), phase) is { } date ? DateFr(date) : throw new Missing();

        /// <summary>The phase of a fact sheet today, from its lifecycle.</summary>
        private string Phase(ArchiSheet sheet)
        {
            bool Reached(string key) => Date(sheet, key) is { } date && _today >= date;
            return Reached("EndOfLife") ? "fin de vie"
                : Reached("PhaseOut") ? "en retrait"
                : Reached("Active") ? "actif"
                : Reached("PhaseIn") ? "en déploiement"
                : Reached("Plan") ? "planifié"
                : "non daté";
        }

        private string Lifecycle(ArchiSheet sheet)
        {
            var labels = new[] { ("Plan", "planifié le"), ("PhaseIn", "déploiement à partir du"), ("Active", "actif à partir du"),
                ("PhaseOut", "retrait à partir du"), ("EndOfLife", "fin de vie le") };
            var steps = labels.Where(l => Date(sheet, l.Item1) is not null).Select(l => $"{l.Item2} {DateFr(Date(sheet, l.Item1)!.Value)}").ToList();
            return steps.Count > 0 ? string.Join(", ", steps) + $" — aujourd'hui : {Phase(sheet)}" : Phase(sheet);
        }

        /// <summary>The name of a fact sheet, as a link to its page from the page <paramref name="here"/> (in bold on its own page).</summary>
        private static string Ref(ArchiSheet sheet, string here)
        {
            var name = Esc(sheet.Name);
            var target = PageOf(sheet);
            return target == here ? $"**{name}**" : $"[{name}](wiki:{target})";
        }

        private string Ref(string name, string here) => Ref(S(name), here);

        /// <summary>The names of several fact sheets, each a link: « a, b et c ».</summary>
        private string Refs(IEnumerable<string> names, string here)
        {
            var items = names.Select(n => Ref(S(n), here)).ToList();
            return items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " et " + items[^1];
        }

        /// <summary>As <see cref="Refs(IEnumerable{string}, string)"/>, of a list that must not be empty for the sentence to hold.</summary>
        private string Some(IEnumerable<string> names, string here)
        {
            var list = names.ToList();
            Require(list.Count > 0);
            return Refs(list, here);
        }

        private List<string> OutOf(string name, string kind)
        {
            var sheet = S(name);
            return _relations.Where(r => r.From == sheet.Id && r.Kind == kind).Select(r => _byId[r.To].Name).ToList();
        }

        private List<string> Into(string name, string kind)
        {
            var sheet = S(name);
            return _relations.Where(r => r.To == sheet.Id && r.Kind == kind).Select(r => _byId[r.From].Name).ToList();
        }

        /// <summary>The names of the fact sheets of these types, by name whatever the case.</summary>
        private List<string> OfType(params string[] types) =>
            _sheets.Where(s => types.Contains(s.Type)).Select(s => s.Name).OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal).ToList();

        private List<string> Names(string type) => _sheets.Where(s => s.Type == type).Select(s => s.Name).ToList();

        private int Count(string type) => _sheets.Count(s => s.Type == type);

        private bool IsModel(string name) => _byName.TryGetValue(name, out var sheet) && sheet.Type == "AIModel";

        private static string ValueOf(string key, string value)
        {
            if (key is "url" or "openapi" or "website")
                return Uri.IsWellFormedUriString(value, UriKind.Absolute) ? $"[{Esc(value)}]({value})" : Esc(value);
            if (key is "key" or "endpoints" or "tools" or "metric")
                return string.Join(", ", value.Replace("…", string.Empty).Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => $"`{p.Replace("\\", "\\\\").Replace("`", "\\`")}`"))
                       + (value.Contains('…') ? " …" : string.Empty);
            return Esc(Value(value));
        }

        private static string Link(string key, string? text = null) => $"[{text ?? TitleOf(key)}](wiki:{key})";

        // ── The parts of a page ──

        /// <summary>A fact sheet as a section: what it is, its attributes, its relations.</summary>
        private List<string> Card(string name, string here, int level = 3)
        {
            if (!_byName.TryGetValue(name, out var sheet))
                return new List<string>();
            var lines = new List<string> { $"{new string('#', level)} {Esc(sheet.Name)}", string.Empty, About(name), string.Empty };
            lines.Add($"- **Type** : {TypeOne(sheet.Type)}");
            if (sheet.ParentId is { Length: > 0 } parent && _byId.TryGetValue(parent, out var above))
                lines.Add($"- **Fiche parente** : {Ref(above, here)}");
            var children = _sheets.Where(c => c.ParentId == sheet.Id).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            if (children.Count > 0)
                lines.Add("- **Se décompose en** : " + string.Join(", ", children.Select(c => Ref(c, here))));
            if (!string.IsNullOrWhiteSpace(sheet.Owner))
                lines.Add($"- **Responsable** : {Esc(sheet.Owner)}");
            lines.Add($"- **Cycle de vie** : {Lifecycle(sheet)}");
            if (!string.IsNullOrEmpty(sheet.Criticality))
                lines.Add($"- **Criticité** : {Criticality(sheet.Criticality)}");
            if (!string.IsNullOrEmpty(sheet.AiRisk))
                lines.Add($"- **Risque au sens de l'AI Act européen** : {Risk(sheet.AiRisk)}");
            foreach (var (key, label) in FieldFr)
                if (FieldOf(sheet, key) is { Length: > 0 } value)
                    lines.Add($"- **{label}** : {ValueOf(key, value)}");
            // The fields the repository has that this list does not know yet: under their own name.
            foreach (var key in (sheet.Fields?.Keys ?? Enumerable.Empty<string>()).Where(k => FieldFr.All(f => f.Key != k)).OrderBy(k => k, StringComparer.Ordinal))
                if (FieldOf(sheet, key) is { Length: > 0 } value)
                    lines.Add($"- **{Esc(key)}** : {Esc(value)}");
            var outgoing = _relations.Where(r => r.From == sheet.Id).ToList();
            var incoming = _relations.Where(r => r.To == sheet.Id).ToList();
            if (outgoing.Count > 0 || incoming.Count > 0)
            {
                lines.AddRange(new[] { string.Empty, "**Ses relations**", string.Empty });
                // What it does to the others, then what the others do to it (the fact sheet itself in bold).
                foreach (var relation in outgoing)
                    lines.Add($"- {Verb(relation.Kind)} {Ref(_byId[relation.To], here)}");
                foreach (var relation in incoming)
                    lines.Add($"- {Ref(_byId[relation.From], here)} {Verb(relation.Kind)} **{Esc(sheet.Name)}**");
            }
            lines.Add(string.Empty);
            return lines;
        }

        private List<string> Cards(IEnumerable<string> names, string here, int level = 3) => names.SelectMany(n => Card(n, here, level)).ToList();

        private List<string> Footer(string here, params string[] see)
        {
            var lines = new List<string> { "## Voir aussi", string.Empty };
            lines.AddRange(see.Select(key => $"- {Link(key)}"));
            if (here != Home)
                lines.Add($"- {Link(Home, "Retour à l’accueil du wiki")}");
            lines.Add(string.Empty);
            lines.Add($"*Page rédigée à partir du référentiel d'architecture /archi ({_sheets.Count} fiches, {_relations.Count} relations), état du {DateFr(AsOf)}. "
                      + "Pour corriger un fait, corrigez d'abord la fiche dans /archi.*");
            return lines;
        }

        /// <summary>
        /// Adds the lines of <paramref name="block"/> to <paramref name="page"/>, or <paramref name="otherwise"/> when it
        /// names a fact sheet the repository no longer has or says something that no longer holds.
        /// </summary>
        private static void Add(List<string> page, Func<IEnumerable<string>> block, params string[] otherwise)
        {
            try
            {
                page.AddRange(block().ToList());
            }
            catch (Exception ex) when (ex is Missing or KeyNotFoundException)
            {
                page.AddRange(otherwise);
            }
        }

        /// <summary>The line <paramref name="line"/> writes, or nothing when it names a fact sheet the repository no longer has.</summary>
        private static IEnumerable<string> Line(Func<string> line)
        {
            string text;
            try
            {
                text = line();
            }
            catch (Exception ex) when (ex is Missing or KeyNotFoundException)
            {
                yield break;
            }
            yield return text;
        }

        /// <summary>The groups of <paramref name="items"/> by <paramref name="key"/>, the largest first, those of the same size in the order they first appear.</summary>
        private static List<(TKey Key, int Count)> MostCommon<T, TKey>(IEnumerable<T> items, Func<T, TKey> key) where TKey : notnull =>
            items.GroupBy(key).Select(g => (g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ToList();

        // ── The pages ──

        /// <summary>The pages of the wiki, the home page first; the id of each is the slug of its title.</summary>
        public List<ArchiWikiPage> Build()
        {
            var others = _sheets.Where(s => PageOf(s) == Others).ToList();
            var pages = new List<(string Key, List<string> Lines)>
            {
                (Home, HomePage(others.Count > 0)),
                ("architecture", Architecture()),
                ("capacites-metier", Capabilities()),
                ("le-chat", ChatPage()),
                ("le-serveur", ServerPage()),
                ("le-moteur-de-recherche", SearchEnginePage()),
                ("la-vision", VisionPage()),
                ("les-agents", Agents()),
                ("les-modeles-ia", Models()),
                ("outils-et-api", Tools()),
                ("donnees-et-connaissances", Data()),
                ("socle-technique", Technical()),
                ("organisation-et-projets", Organisation()),
                ("feuille-de-route", Roadmap()),
                ("gouvernance-ia", Governance()),
            };
            if (others.Count > 0)
                pages.Add((Others, OthersPage(others)));

            // The id of a page is the slug of its title (the rule of the wiki: a link written from the title finds the page).
            var ids = Pages.ToDictionary(p => p.Key, p => Wiki.Slug(p.Title));
            return pages.Select(page =>
            {
                var content = string.Join('\n', page.Lines).TrimEnd('\n') + "\n";
                content = Regex.Replace(content, @"\]\(wiki:([a-z0-9-]+)\)", m => ids.TryGetValue(m.Groups[1].Value, out var id) ? $"](wiki:{id})" : m.Value);
                return new ArchiWikiPage(ids[page.Key], TitleOf(page.Key), content, Tags[page.Key].ToList());
            }).ToList();
        }

        private static readonly Dictionary<string, string[]> Tags = new()
        {
            [Home] = new[] { "#KAONOMI", "#WIKI" }, ["architecture"] = new[] { "#ARCHITECTURE" }, ["capacites-metier"] = new[] { "#ARCHITECTURE", "#CAPACITES" },
            ["le-chat"] = new[] { "#ARCHITECTURE", "#APPLICATION", "#CHAT" }, ["le-serveur"] = new[] { "#ARCHITECTURE", "#APPLICATION", "#SERVEUR" },
            ["le-moteur-de-recherche"] = new[] { "#ARCHITECTURE", "#APPLICATION", "#RECHERCHE" }, ["la-vision"] = new[] { "#ARCHITECTURE", "#APPLICATION", "#VISION" },
            ["les-agents"] = new[] { "#ARCHITECTURE", "#IA", "#AGENTS" }, ["les-modeles-ia"] = new[] { "#ARCHITECTURE", "#IA", "#MODELES" },
            ["outils-et-api"] = new[] { "#ARCHITECTURE", "#IA", "#API" }, ["donnees-et-connaissances"] = new[] { "#ARCHITECTURE", "#DONNEES" },
            ["socle-technique"] = new[] { "#ARCHITECTURE", "#TECHNIQUE" }, ["organisation-et-projets"] = new[] { "#ARCHITECTURE", "#ORGANISATION" },
            ["feuille-de-route"] = new[] { "#ARCHITECTURE", "#FEUILLE-DE-ROUTE" }, ["gouvernance-ia"] = new[] { "#ARCHITECTURE", "#IA", "#AI-ACT" },
            [Others] = new[] { "#ARCHITECTURE" },
        };

        private List<string> HomePage(bool withOthers)
        {
            var c = new List<string>
            {
                "# Kaonomi", "",
                "Bienvenue dans le wiki de Kaonomi. Kaonomi est un poste de travail d'assistance et de renseignement : on y converse avec un assistant, par le texte et par la voix, "
                + "on y collecte de l'information sur le web et dans les documents, on l'analyse, et on conserve ce qu'on a appris pour le retrouver.",
                "",
                "Ce wiki décrit l'architecture de Kaonomi telle qu'elle est tenue dans le référentiel /archi : ce que le système sait faire, les applications qui le font, "
                + "l'intelligence artificielle qu'elles emploient, les données qu'elles manipulent et le socle technique sur lequel tout repose.",
                "", "## Par où commencer", "",
                $"- {Link("architecture")} : comment les pièces s'assemblent, en suivant une question, une collecte et une nuit de travail.",
                $"- {Link("capacites-metier")} : ce que Kaonomi sait faire, et quelle application soutient quoi.",
                $"- {Link("feuille-de-route")} : ce qui est en service, ce qui est en retrait, ce qui est prévu.",
                "", "## Les applications", "",
                $"- {Link("le-chat")} — {Lower(Fr[Chat].Fr)}",
                $"- {Link("le-serveur")} — {Lower(Fr[Server].Fr)}",
                $"- {Link("le-moteur-de-recherche")} — {Lower(Fr[SearchEngine].Fr)}",
                $"- {Link("la-vision")} — {Lower(Fr[Vision].Fr)}",
                "", "## L'intelligence artificielle", "",
                $"- {Link("les-agents")} : les {Count("Agent")} agents, les {Count("MultiAgentSystem")} systèmes multi-agents, leur prompt, leur évaluation et leur garde-fou.",
                $"- {Link("les-modeles-ia")} : les {Count("AIModel")} modèles, leurs fournisseurs et l'endroit où ils tournent.",
                $"- {Link("outils-et-api")} : l'outil de recherche, le serveur MCP et les API REST.",
                $"- {Link("gouvernance-ia")} : les risques au sens de l'AI Act européen et les points d'attention.",
                "", "## Les données et la technique", "",
                $"- {Link("donnees-et-connaissances")} : les objets de données, le jeu de données, la base de connaissances et la base vectorielle.",
                $"- {Link("socle-technique")} : les {Count("ITComponent")} composants techniques et leur catégorie.",
                $"- {Link("organisation-et-projets")} : l'équipe, les utilisateurs et le projet en cours.",
            };
            if (withOthers)
                c.Add($"- {Link(Others)} : les fiches des genres qui n'ont pas de page à eux.");
            c.AddRange(new[] { "", "## Le référentiel en chiffres", "", $"Le référentiel compte {_sheets.Count} fiches et {_relations.Count} relations.", "" });
            foreach (var (type, count) in MostCommon(_sheets, s => s.Type).OrderByDescending(t => t.Count).ThenBy(t => TypeOne(t.Key), StringComparer.Ordinal))
                c.Add($"- {count} {TypeCount(type, count)}");
            c.AddRange(new[]
            {
                "", "## Comment ce wiki est tenu", "",
                "Chaque page reprend les fiches de /archi : leur description, leur cycle de vie, leur criticité, leurs attributs et leurs relations. "
                + "Les noms des fiches sont ceux du référentiel, en anglais, pour qu'on les retrouve tels quels dans /archi.",
                "",
                "Un lien vers une page qui n'existe pas encore la commence : clique dessus, écris-la, puis SAVE.",
                "",
                $"*Wiki rédigé à partir du référentiel d'architecture /archi ({_sheets.Count} fiches, {_relations.Count} relations), état du {DateFr(AsOf)}.*",
            });
            return c;
        }

        private List<string> Architecture()
        {
            const string h = "architecture";
            var c = new List<string> { $"# {TitleOf(h)}", "" };
            Add(c, () => new[]
            {
                $"Kaonomi tient en quatre applications. {Ref(Chat, h)} est le poste de travail de l'utilisateur ; {Ref(Server, h)} porte l'API, les agents et les données ; "
                + $"{Ref(SearchEngine, h)} retrouve les documents ; {Ref(Vision, h)} regarde par la caméra. "
                + $"Une cinquième, {Ref(OldChat, h)}, est le premier poste de travail : elle est {Phase(S(OldChat))} depuis le {DateOf(OldChat, "PhaseOut")}.",
                "",
            });
            Add(c, () =>
            {
                Require(OfType("AIModel").All(m => Field(m, "hosting") == "Local") && Field(Chat, "hosting") == "Local" && Field(Server, "hosting") == "Local"
                        && Field("Ollama", "hosting") == "Local" && Field("LM Studio", "hosting") == "Local");
                return new[]
                {
                    $"Les {Count("AIModel")} modèles d'IA sont hébergés en local, comme le chat, le serveur et les deux moteurs d'exécution des modèles : ce sont les seuls hébergements que le référentiel renseigne.",
                    "",
                };
            });
            c.AddRange(new[] { "## Les applications et ce qu’elles soutiennent", "" });
            foreach (var app in new[] { Chat, Server, SearchEngine, Vision, OldChat })
                c.AddRange(Line(() =>
                {
                    var sheet = S(app);
                    var bits = new List<string> { $"soutient {Some(OutOf(app, "supports"), h)}" };
                    if (OutOf(app, "runs on").Count > 0)
                        bits.Add($"s'exécute sur {Refs(OutOf(app, "runs on"), h)}");
                    var critical = string.IsNullOrEmpty(sheet.Criticality) ? string.Empty : $" ({Criticality(sheet.Criticality)})";
                    return $"- {Ref(sheet, h)}{critical} : {string.Join(" ; ", bits)}.";
                }));
            Add(c, () => new[]
            {
                "", "## Une question posée dans le chat", "",
                $"1. L'utilisateur écrit ou parle dans {Ref(Chat, h)}. La parole est transcrite par {Ref("whisper (ggml-small)", h)}.",
                $"2. Le chat confie la question à {Ref(Assistant, h)}, l'agent qui répond avec sa persona et sa voix.",
                $"3. L'assistant raisonne avec {Some(OutOf(Assistant, "uses").Where(IsModel), h)}, le petit modèle multimodal, qu'il atteint par {Some(OutOf(Assistant, "calls"), h)} ; "
                + $"ce modèle est hébergé par {Some(OutOf("lfm3-omni-2b", "hosted by"), h)}.",
                $"4. Pour s'appuyer sur des sources, il lit {Some(OutOf(Assistant, "reads"), h)} et se sert de l'outil {Ref("Search", h)}, qui appelle {Some(OutOf("Search", "calls"), h)}.",
                $"5. La base de connaissances indexe {Some(OutOf("Kaonomi knowledge", "indexes"), h)}, dont les vecteurs sont calculés avec {Some(OutOf("Search engine vectors", "embeds with"), h)} ; "
                + $"elle lit {Some(OutOf("Kaonomi knowledge", "reads"), h)}.",
                $"6. La réponse revient dans le chat, et l'avatar la dit avec {Ref("Piper voices", h)}. Les échanges sont conservés comme {Ref("Conversations", h)}.",
                "",
                $"La qualité des réponses est mesurée par {Some(OutOf(Assistant, "evaluated by"), h)} (métrique `{Field("Answer faithfulness", "metric")}`, score {Field("Answer faithfulness", "score")}). "
                + $"Un garde-fou, {Some(OutOf(Assistant, "guarded by").Where(g => Phase(S(g)) is "planifié" or "en déploiement"), h)}, est prévu pour protéger l'assistant : il n'est pas encore en service (voir {Link("feuille-de-route")}).",
            });
            Add(c, () => new[]
            {
                "", "## Une collecte sur le web", "",
                $"La collecte est le travail de {Ref("Web scraping pipeline", h)}, un système multi-agents {Value(Field("Web scraping pipeline", "pattern"))} : "
                + "le scraper, le lecteur de pages et l'analyste se suivent.",
                "",
                "1. Le RUN d'un plan de collecte lance le pipeline.",
                $"2. {Ref("Page reader", h)} lit les images de chaque page, écran par écran, avec {Some(OutOf("Page reader", "uses"), h)}, qu'il atteint par {Some(OutOf("Page reader", "calls"), h)}.",
                $"3. {Ref("Collection analyst", h)} lit les pages et rédige l'analyse avec {Some(OutOf("Collection analyst", "uses"), h)}, en suivant le prompt {Some(OutOf("Collection analyst", "prompted by"), h)}.",
                $"4. Le pipeline s'arrête {Value(Field("Web scraping pipeline", "termination"))}. Les pages lues forment le jeu de données {Ref("Collected pages", h)}, que lit la base de connaissances.",
            });
            Add(c, () => new[]
            {
                "", "## La nuit, sur le serveur", "",
                $"{Ref("Server agents", h)} regroupe les agents du serveur, organisés selon un schéma {Value(Field("Server agents", "pattern"))} et portés par des {Value(Field("Server agents", "protocol"))}. "
                + $"Il orchestre {Some(OutOf("Server agents", "orchestrates"), h)}, qui relie les connaissances la nuit : relations, regroupements, idées. "
                + $"Cet agent est {Value(Field("Dreamer", "autonomy"))}, se déclenche par {Value(Field("Dreamer", "trigger"))}, garde en mémoire {Value(Field("Dreamer", "memory"))} "
                + $"et raisonne avec {Some(OutOf("Dreamer", "uses"), h)}.",
            });
            Add(c, () => new[]
            {
                "", "## Voir et entendre", "",
                $"{Ref(Vision, h)} utilise {Some(OutOf(Vision, "uses"), h)} pour détecter les objets et reconnaître les visages. "
                + $"Du côté de la voix, le chat utilise {Ref("whisper (ggml-small)", h)} pour entendre et {Ref("Piper voices", h)} pour parler.",
            });
            Add(c, () =>
            {
                var runtimes = Into("AI runtimes", "is part of");
                Require(runtimes.Count == 2 && runtimes.Contains("Ollama") && runtimes.Contains("LM Studio"));
                var lines = new List<string>
                {
                    "", "## Où tournent les modèles", "",
                    $"Deux moteurs d'exécution forment la catégorie {Ref("AI runtimes", h)} : {Refs(runtimes, h)}.",
                };
                foreach (var runtime in runtimes)
                    lines.Add($"- {Ref(runtime, h)} héberge {Some(Into(runtime, "hosted by"), h)} et répond par {Ref(runtime == "LM Studio" ? "LM Studio API" : "Ollama API", h)}.");
                return lines;
            });
            Add(c, () => new[]
            {
                "", "## Ce qui change bientôt", "",
                $"Le projet {Ref(Project, h)} prévoit de déplacer les agents du chat vers le serveur, derrière MCP. "
                + $"Il met en œuvre {Some(OutOf(Project, "implements"), h)}. Le détail des dates est dans {Link("feuille-de-route")}.",
            });
            c.Add("");
            c.AddRange(Footer(h, "capacites-metier", "le-chat", "le-serveur", "les-agents", "les-modeles-ia"));
            return c;
        }

        private List<string> Capabilities()
        {
            const string h = "capacites-metier";
            var roots = _sheets.Where(s => s.Type == "BusinessCapability" && string.IsNullOrEmpty(s.ParentId)).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
            List<ArchiSheet> Kids(ArchiSheet root) => _sheets.Where(s => s.ParentId == root.Id).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
            var split = roots.Where(r => Kids(r).Count > 0).ToList();
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                $"Une capacité métier dit ce que Kaonomi sait faire, indépendamment de l'application qui le fait. Le référentiel en compte {Count("BusinessCapability")}, "
                + $"sur deux niveaux : {roots.Count} capacités de premier niveau, dont {split.Count} se décomposent.",
                "", "## La carte des capacités", "",
            };
            foreach (var root in roots)
            {
                var kids = Kids(root);
                var critical = string.IsNullOrEmpty(root.Criticality) ? string.Empty : $" — {Criticality(root.Criticality)}";
                c.Add($"- **{Esc(root.Name)}**{critical}");
                foreach (var kid in kids)
                {
                    var apps = Into(kid.Name, "supports");
                    c.Add($"- → **{Esc(kid.Name)}** : soutenue par {(apps.Count > 0 ? Refs(apps, h) : "aucune application")}");
                }
                if (kids.Count == 0)
                {
                    var apps = Into(root.Name, "supports");
                    c.Add($"- → soutenue directement par {(apps.Count > 0 ? Refs(apps, h) : "aucune application")}");
                }
            }
            c.Add("");
            if (split.Count == 2 && split.All(r => Into(r.Name, "supports").Count == 0))
                c.AddRange(new[]
                {
                    "Les deux capacités de premier niveau qui se décomposent ne sont soutenues par aucune application directement : ce sont leurs sous-capacités qui le sont.",
                    "",
                });
            foreach (var root in roots)
            {
                c.AddRange(Card(root.Name, h, 2));
                foreach (var kid in Kids(root))
                    c.AddRange(Card(kid.Name, h, 3));
            }
            c.AddRange(new[] { "## Quelle application soutient quoi", "" });
            foreach (var app in Names("Application"))
                c.Add($"- {Ref(app, h)} : {(OutOf(app, "supports").Count > 0 ? Refs(OutOf(app, "supports"), h) : "aucune capacité")}");
            c.Add("");
            c.AddRange(Footer(h, "architecture", "le-chat", "le-serveur", "le-moteur-de-recherche", "la-vision"));
            return c;
        }

        private List<string> ChatPage()
        {
            const string h = "le-chat";
            var c = new List<string> { $"# {TitleOf(h)}", "" };
            Add(c, () => new[]
            {
                "Le chat est le poste de travail de Kaonomi : c'est par lui que l'utilisateur converse avec l'assistant, lance les outils et voit l'avatar 3D. "
                + $"Deux applications portent ce nom dans le référentiel : **{Esc(S(Chat).Name)}**, en service, et **{Esc(S(OldChat).Name)}**, le premier poste de travail, qu'il a remplacé.",
                "",
            });
            Add(c, () =>
            {
                Require(!string.IsNullOrEmpty(S(Chat).Criticality) && OutOf(Chat, "supports").Count == 2);
                return new[]
                {
                    $"Le chat Godot est {Criticality(S(Chat).Criticality)}. Il soutient deux capacités, {Refs(OutOf(Chat, "supports"), h)}, "
                    + $"et il est utilisé par le groupe {Some(OutOf(Chat, "used by"), h)}.",
                    "",
                };
            });
            Add(c, () => new[]
            {
                "## Ce que fait le chat", "",
                $"- **Il converse.** Il confie les questions à {Some(OutOf(Chat, "uses").Where(n => S(n).Type == "Agent"), h)}, qui répond avec sa persona et sa voix.",
                $"- **Il entend et il parle.** La dictée et l'enregistreur passent par {Ref("whisper (ggml-small)", h)} ; l'avatar parle avec {Ref("Piper voices", h)}.",
                $"- **Il s'appuie sur le serveur.** Il appelle {Some(OutOf(Chat, "calls"), h)} par l'interface **{Esc(S(ChatToServer).Name)}**, en {Value(Field(ChatToServer, "frequency"))}.",
                $"- **Il garde la trace des échanges.** Il écrit {Some(OutOf(Chat, "writes"), h)}, des données de classification « {Value(Field("Conversations", "classification"))} » qui contiennent des données personnelles.",
                "",
            });
            Add(c, () => new[]
            {
                "## Sur quoi il tourne", "",
                $"Le chat s'exécute sur {Some(OutOf(Chat, "runs on"), h)}. Son hébergement est {Value(Field(Chat, "hosting"))}.",
                "",
            });
            c.AddRange(Card(Chat, h, 2));
            c.AddRange(Card(ChatToServer, h, 2));
            c.AddRange(Card(OldChat, h, 2));
            Add(c, () =>
            {
                Require(OutOf(Chat, "supports").Contains("Conversation") && OutOf(OldChat, "supports").Contains("Conversation") && OutOf(Chat, "supports").Contains("Perception"));
                return new[]
                {
                    "## Du chat Avalonia au chat Godot", "",
                    $"Les deux applications soutiennent la capacité {Ref("Conversation", h)}. Le chat Avalonia a été actif à partir du {DateOf(OldChat, "Active")} ; "
                    + $"son retrait a commencé le {DateOf(OldChat, "PhaseOut")}. Le chat Godot ajoute la capacité {Ref("Perception", h)}, "
                    + "l'avatar 3D et l'assistant : c'est lui qui est en service aujourd'hui.",
                    "",
                };
            });
            c.AddRange(Footer(h, "le-serveur", "les-agents", "les-modeles-ia", "donnees-et-connaissances", "socle-technique"));
            return c;
        }

        private List<string> ServerPage()
        {
            const string h = "le-serveur";
            var c = new List<string> { $"# {TitleOf(h)}", "" };
            Add(c, () =>
            {
                var top = _sheets.Where(s => s.Criticality == "Mission critical").Select(s => s.Name).ToList();
                Require(top.Count == 2 && top.Contains(Server) && top.Contains(Api));
                return new[]
                {
                    $"Le serveur est le cœur de Kaonomi : il porte l'API REST, les agents et leurs grains. Avec son API, c'est la seule fiche du référentiel classée {Criticality(S(Server).Criticality)}.",
                    "",
                };
            }, "Le serveur est le cœur de Kaonomi : il porte l'API REST, les agents et leurs grains.", "");
            Add(c, () =>
            {
                Require(OutOf(Server, "supports").Count == 3);
                return new[]
                {
                    "## Ce que fait le serveur", "",
                    $"- **Il soutient trois capacités** : {Refs(OutOf(Server, "supports"), h)}.",
                    $"- **Il expose l'API.** {Ref(Api, h)} est appelée par {Some(Into(Api, "calls"), h)}.",
                    $"- **Il écrit les documents.** Il écrit {Some(OutOf(Server, "writes"), h)}, de classification « {Value(Field("Documents", "classification"))} ».",
                    $"- **Il fait tourner des agents.** {Ref("Server agents", h)} y vit sous forme de {Value(Field("Server agents", "protocol"))}.",
                    "",
                };
            });
            Add(c, () =>
            {
                var runsOn = OutOf(Server, "runs on");
                Require(runsOn.SequenceEqual(new[] { ".NET 8", "Microsoft Orleans", "MongoDB" }));
                return new[]
                {
                    "## Sur quoi il tourne", "",
                    $"Le serveur s'exécute sur {Refs(runsOn, h)} : {Inline(About(".NET 8"))}, {Inline(About("Microsoft Orleans"))}, et {Inline(About("MongoDB"))}.",
                    "",
                };
            });
            Add(c, () =>
            {
                Require(Field(Api, "style").Length > 0 && Field(Api, "url").Length > 0 && Field(Api, "openapi").Length > 0 && Field(Api, "endpoints").Length > 0);
                return new[]
                {
                    "## L'API REST", "",
                    $"L'API est de style {Esc(Field(Api, "style"))}. Le référentiel donne son adresse, {ValueOf("url", Field(Api, "url"))}, et sa description OpenAPI, {ValueOf("openapi", Field(Api, "openapi"))}. "
                    + $"Parmi ses points d'entrée : {ValueOf("endpoints", Field(Api, "endpoints"))}",
                    "",
                };
            });
            Add(c, () =>
            {
                Require(Into(Api, "calls").Count == 2 && Into("Search", "exposes").Contains("Kaonomi MCP"));
                return new[]
                {
                    $"Elle a deux clients dans le référentiel : le chat, qui l'appelle directement, et l'outil {Ref("Search", h)}, que {Ref("Kaonomi MCP", h)} exposera à d'autres assistants.",
                    "",
                };
            });
            c.AddRange(Card(Server, h, 2));
            c.AddRange(Card(Api, h, 2));
            Add(c, () => new[]
            {
                "## Ce qui est prévu", "",
                $"Le projet {Ref(Project, h)} ({Value(Field(Project, "status"))}) déplacera les agents du chat vers le serveur, derrière MCP. "
                + $"Il s'appuie sur {Some(OutOf(Project, "implements"), h)}.",
                "",
            });
            c.AddRange(Footer(h, "le-chat", "le-moteur-de-recherche", "les-agents", "outils-et-api", "socle-technique"));
            return c;
        }

        private List<string> SearchEnginePage()
        {
            const string h = "le-moteur-de-recherche";
            var c = new List<string> { $"# {TitleOf(h)}", "" };
            Add(c, () =>
            {
                Require(!string.IsNullOrEmpty(S(SearchEngine).Criticality) && OutOf(Server, "supports").Intersect(OutOf(SearchEngine, "supports")).Any());
                return new[]
                {
                    $"Le moteur de recherche retrouve les documents, les notes et les intels, par leur texte et par leurs vecteurs. Il est {Criticality(S(SearchEngine).Criticality)} "
                    + $"et soutient, avec le serveur, la capacité {Refs(OutOf(SearchEngine, "supports"), h)}.",
                    "",
                };
            }, "Le moteur de recherche retrouve les documents, les notes et les intels, par leur texte et par leurs vecteurs.", "");
            Add(c, () => new[]
            {
                "## Comment la recherche est construite", "",
                $"- **Les vecteurs.** {Ref("Search engine vectors", h)} garde les vecteurs des documents ; son moteur est {Esc(Field("Search engine vectors", "engine"))}.",
                $"- **Les plongements.** Ces vecteurs sont calculés avec {Some(OutOf("Search engine vectors", "embeds with"), h)}, un modèle hébergé par {Some(OutOf("nomic-embed-text", "hosted by"), h)}.",
                $"- **La base de connaissances.** {Ref("Kaonomi knowledge", h)} indexe ces vecteurs et lit {Some(OutOf("Kaonomi knowledge", "reads"), h)} ; sa recherche est {Value(Field("Kaonomi knowledge", "retrieval"))}, "
                + "c'est-à-dire qu'elle combine le texte et les vecteurs.",
                $"- **L'outil.** {Ref("Search", h)} interroge les index en {Value(Field("Search", "effects"))} ; c'est par lui que {Some(Into("Search", "uses"), h)} cherche.",
                "",
            });
            c.AddRange(Card(SearchEngine, h, 2));
            Add(c, () =>
            {
                var sheet = S(SearchEngine);
                Require(_relations.Count(r => r.From == sheet.Id || r.To == sheet.Id) == 1 && OutOf(SearchEngine, "supports").Count == 1);
                return new[]
                {
                    "## Ce que le référentiel ne dit pas encore", "",
                    "La fiche du moteur de recherche n'a qu'une relation : la capacité qu'elle soutient. Le référentiel ne relie pas encore l'application à la base vectorielle, "
                    + "à la base de connaissances ni au serveur ; ces liens sont à ajouter dans /archi pour que les cartes et les matrices les montrent.",
                    "",
                };
            });
            c.AddRange(Footer(h, "donnees-et-connaissances", "les-modeles-ia", "outils-et-api", "le-serveur"));
            return c;
        }

        private List<string> VisionPage()
        {
            const string h = "la-vision";
            var c = new List<string> { $"# {TitleOf(h)}", "" };
            Add(c, () =>
            {
                Require(OutOf(Vision, "supports").Count == 1 && OutOf(Chat, "supports").Intersect(OutOf(Vision, "supports")).Any());
                return new[]
                {
                    $"La vision est l'application qui regarde par la caméra : elle reconnaît les visages, détecte les objets et suit les mouvements. Elle soutient la capacité {Refs(OutOf(Vision, "supports"), h)}, "
                    + "que le chat soutient aussi pour la voix.",
                    "",
                };
            }, "La vision est l'application qui regarde par la caméra : elle reconnaît les visages, détecte les objets et suit les mouvements.", "");
            Add(c, () =>
            {
                var models = OutOf(Vision, "uses");
                Require(models.Count > 0);
                var lines = new List<string> { $"## Ses {models.Count} modèles", "" };
                foreach (var model in models)
                    lines.Add($"- {Ref(model, h)} : {Inline(About(model))} — modalité {Value(Field(model, "modality"))}, hébergement {Value(Field(model, "hosting"))}, "
                              + $"licence {Value(Field(model, "license"))}, risque {Risk(S(model).AiRisk)}.");
                lines.Add("");
                return lines;
            });
            Add(c, () =>
            {
                const string face = "Face recognition (OpenCV)";
                Require(_sheets.Where(s => s.AiRisk == "High").Select(s => s.Name).SequenceEqual(new[] { face })
                        && OutOf(face, "guarded by").Count == 0 && OutOf(face, "evaluated by").Count == 0);
                return new[]
                {
                    "## Un point d'attention", "",
                    $"{Ref(face, h)} est le seul modèle du référentiel classé à risque {Risk("High")} au sens de l'AI Act européen : il reconnaît des personnes. "
                    + $"Le référentiel ne lui associe ni garde-fou ni évaluation. Voir {Link("gouvernance-ia")}.",
                    "",
                };
            });
            c.AddRange(Card(Vision, h, 2));
            c.AddRange(Footer(h, "les-modeles-ia", "gouvernance-ia", "capacites-metier", "le-chat"));
            return c;
        }

        private List<string> Agents()
        {
            const string h = "les-agents";
            var agents = OfType("Agent");
            var custom = agents.Count > 0 && agents.All(a => Field(a, "framework") == "Custom");
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                $"Un agent est un programme qui confie une tâche à un modèle d'IA et agit avec ce qu'il répond. Le référentiel décrit {Count("Agent")} agents et {Count("MultiAgentSystem")} systèmes multi-agents, "
                + "ainsi que ce qui les encadre : un prompt, une évaluation et un garde-fou." + (custom ? " Tous sont développés sur mesure." : string.Empty),
                "", "## Les agents en un coup d’œil", "",
            };
            foreach (var agent in agents)
                c.Add($"- **{Esc(agent)}** : {Inline(About(agent))} — {Value(Field(agent, "autonomy"))}, humain dans la boucle : {Value(Field(agent, "human"))}, "
                      + $"modèle : {Refs(OutOf(agent, "uses").Where(IsModel), h)}, risque {Risk(S(agent).AiRisk)}.");
            Add(c, () =>
            {
                Require(Field(Assistant, "autonomy") == "Assistant (answers)" && Field("Collection analyst", "autonomy") == "Agent (acts, confirmed)"
                        && agents.Where(a => Field(a, "autonomy") == "Autonomous (acts alone)").SequenceEqual(new[] { "Dreamer", "Page reader" }));
                return new[]
                {
                    "", "## Trois niveaux d'autonomie", "",
                    "- **Assistant** : il répond, et c'est l'utilisateur qui agit. C'est le cas de Kaonomi assistant.",
                    "- **Agent** : il agit, après confirmation d'une personne. C'est le cas de Collection analyst.",
                    "- **Autonome** : il agit seul, sans personne dans la boucle. C'est le cas de Dreamer et de Page reader.",
                };
            });
            c.AddRange(new[] { "", "## Les agents", "" });
            c.AddRange(Cards(agents, h));
            c.AddRange(new[]
            {
                "## Les systèmes multi-agents", "",
                "Un système multi-agents fait travailler plusieurs agents ensemble, selon un schéma d'orchestration.",
                "",
            });
            c.AddRange(Cards(OfType("MultiAgentSystem"), h));
            c.AddRange(new[] { "## Ce qui encadre les agents", "" });
            c.AddRange(Cards(OfType("Prompt").Concat(OfType("Evaluation")).Concat(OfType("Guardrail")), h));
            c.AddRange(Footer(h, "les-modeles-ia", "outils-et-api", "gouvernance-ia", "architecture"));
            return c;
        }

        private List<string> Models()
        {
            const string h = "les-modeles-ia";
            var models = OfType("AIModel");
            var local = models.Count > 0 && models.All(m => Field(m, "hosting") == "Local");
            var open = models.All(m => Field(m, "license") is "Open source" or "Open weights" or "");
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                $"Kaonomi emploie {Count("AIModel")} modèles d'IA." + (local ? " Tous sont hébergés en local" + (open ? " ; ceux dont la licence est renseignée sont open source ou à poids ouverts." : ".") : string.Empty)
                + " Chacun a un rôle précis : raisonner, lire des images, calculer des vecteurs, entendre, parler, voir.",
                "", "## Les modèles par rôle", "",
            };
            foreach (var group in models.GroupBy(m => Field(m, "modality")))
                c.Add($"- **{Upper(group.Key.Length > 0 ? Value(group.Key) : "rôle non renseigné")}** : " + string.Join(", ", group.Select(m => $"**{Esc(m)}**")));
            c.AddRange(new[] { "", "## Qui utilise quoi", "" });
            foreach (var model in models)
            {
                var users = Into(model, "uses").Concat(Into(model, "embeds with")).ToList();
                var planned = Into(model, "implements");
                c.Add(users.Count > 0 ? $"- **{Esc(model)}** est utilisé par {Refs(users, h)}."
                    : planned.Count > 0 ? $"- **{Esc(model)}** n'est pas encore utilisé : il est porté par le projet {Refs(planned, h)}."
                    : $"- **{Esc(model)}** n'est relié à aucun utilisateur dans le référentiel.");
            }
            c.AddRange(new[] { "", "## Où ils tournent", "" });
            Add(c, () => Into("AI runtimes", "is part of").Where(r => Into(r, "hosted by").Count > 0).Select(r => $"- {Ref(r, h)} héberge {Refs(Into(r, "hosted by"), h)}."));
            var unhosted = models.Where(m => OutOf(m, "hosted by").Count == 0).ToList();
            if (unhosted.Count > 0)
                c.Add($"- Les autres ({string.Join(", ", unhosted.Select(m => $"**{Esc(m)}**"))}) n'ont pas de moteur d'exécution relié dans le référentiel.");
            c.AddRange(new[] { "", "## Les modèles", "" });
            c.AddRange(Cards(models, h));
            c.AddRange(new[] { "## Les fournisseurs", "" });
            c.AddRange(Cards(OfType("Provider"), h));
            c.AddRange(Footer(h, "les-agents", "socle-technique", "gouvernance-ia", "la-vision"));
            return c;
        }

        private List<string> Tools()
        {
            const string h = "outils-et-api";
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                "Cette page décrit ce par quoi les agents et les applications se parlent : un outil, un serveur MCP, trois API REST et une interface.",
                "", "## L'outil des agents", "",
            };
            c.AddRange(Cards(OfType("Tool"), h));
            c.AddRange(new[]
            {
                "## Le serveur MCP", "",
                "MCP (Model Context Protocol) est le protocole par lequel un assistant découvre et appelle des outils. Le serveur MCP de Kaonomi offrira ses outils à d'autres assistants.",
                "",
            });
            c.AddRange(Cards(OfType("McpServer"), h));
            c.AddRange(new[]
            {
                "## Les API REST", "",
                $"L'API du serveur a sa propre section dans {Link("le-serveur")} ; les deux autres sont celles des moteurs d'exécution des modèles.",
                "",
            });
            c.AddRange(Cards(OfType("RestApi"), h));
            Add(c, () => new[]
            {
                "## L'interface entre le chat et le serveur", "",
                $"L'interface **{Esc(S(ChatToServer).Name)}** est décrite dans {Link("le-chat")} : protocole {Esc(Field(ChatToServer, "protocol"))}, en {Value(Field(ChatToServer, "frequency"))}.",
                "",
            });
            c.AddRange(Cards(OfType("Interface").Where(n => n != ChatToServer), h));
            c.AddRange(Footer(h, "le-serveur", "les-agents", "les-modeles-ia", "socle-technique"));
            return c;
        }

        private List<string> Data()
        {
            const string h = "donnees-et-connaissances";
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                "Cette page suit l'information de Kaonomi, de ce qui est collecté à ce qui est retrouvé : les objets de données, le jeu de données de la collecte, "
                + "la base de connaissances et la base vectorielle.",
                "",
            };
            Add(c, () =>
            {
                Require(OutOf("Kaonomi knowledge", "reads").Count == 2);
                return new[]
                {
                    "## Le chemin de l'information", "",
                    $"1. Les plans de collecte lisent des pages web : c'est le jeu de données **{Esc(S("Collected pages").Name)}** (source {Esc(Field("Collected pages", "source"))}, usage {Value(Field("Collected pages", "usage"))}).",
                    $"2. {Some(Into("Documents", "writes"), h)} écrit les **Documents** : documents, pages web et notes collectés.",
                    $"3. **{Esc(S("Kaonomi knowledge").Name)}** lit ces deux sources et indexe **{Esc(S("Search engine vectors").Name)}**, dont les vecteurs sont calculés avec {Some(OutOf("Search engine vectors", "embeds with"), h)}.",
                    $"4. {Some(Into("Kaonomi knowledge", "reads"), h)} lit la base de connaissances pour répondre.",
                    $"5. Les échanges eux-mêmes sont écrits par {Some(Into("Conversations", "writes"), h)} : ce sont les **Conversations**.",
                    "",
                };
            });
            c.AddRange(new[] { "## La sensibilité des données", "" });
            foreach (var name in OfType("DataObject").Concat(OfType("Dataset")))
            {
                var bits = new List<string>();
                if (Field(name, "classification") is { Length: > 0 } classification)
                    bits.Add($"classification « {Esc(Value(classification))} »");
                if (Field(name, "personal") is { Length: > 0 } personal)
                    bits.Add($"données personnelles : {Esc(Value(personal))}");
                c.Add($"- **{Esc(name)}** : {(bits.Count > 0 ? string.Join(" ; ", bits) : "non renseignée")}.");
            }
            c.Add("");
            Add(c, () =>
            {
                Require(Field("Documents", "personal").Length == 0 && Field("Collected pages", "classification").Length == 0 && Count("Dataset") == 1);
                return new[]
                {
                    "La classification des Documents ne dit pas s'ils contiennent des données personnelles, et le jeu de données n'a pas de classification : deux champs à compléter dans /archi.",
                    "",
                };
            });
            c.AddRange(new[] { "## Les objets de données", "" });
            c.AddRange(Cards(OfType("DataObject"), h));
            c.AddRange(new[] { "## Le jeu de données", "" });
            c.AddRange(Cards(OfType("Dataset"), h));
            c.AddRange(new[] { "## La base de connaissances", "" });
            c.AddRange(Cards(OfType("KnowledgeBase"), h));
            c.AddRange(new[] { "## La base vectorielle", "" });
            c.AddRange(Cards(OfType("VectorStore"), h));
            c.AddRange(Footer(h, "le-moteur-de-recherche", "les-agents", "gouvernance-ia", "le-serveur"));
            return c;
        }

        private List<string> Technical()
        {
            const string h = "socle-technique";
            var runtimes = _byName.ContainsKey("AI runtimes") ? Into("AI runtimes", "is part of").Count : 0;
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                $"Le socle technique est ce sur quoi les applications et les modèles tournent : {Count("ITComponent")} composants"
                + (runtimes > 0 ? $", dont {runtimes} forment la catégorie des moteurs d'exécution de l'IA." : "."),
                "", "## Qui tourne sur quoi", "",
            };
            foreach (var component in OfType("ITComponent"))
            {
                var apps = Into(component, "runs on");
                var hosted = Into(component, "hosted by");
                var bits = new List<string>();
                if (apps.Count > 0)
                    bits.Add($"sert de support à {Refs(apps, h)}");
                if (hosted.Count > 0)
                    bits.Add($"héberge {Refs(hosted, h)}");
                var version = Field(component, "version") is { Length: > 0 } v ? $" (version {Esc(v)})" : string.Empty;
                var category = Field(component, "category") is { Length: > 0 } k ? $", {Esc(Value(k))}" : string.Empty;
                c.Add($"- **{Esc(component)}**{version}{category} : {(bits.Count > 0 ? string.Join(" ; ", bits) : "aucune relation dans le référentiel")}.");
            }
            c.AddRange(new[] { "", "## Les composants", "" });
            c.AddRange(Cards(OfType("ITComponent"), h));
            c.AddRange(new[] { "## La catégorie technique", "" });
            c.AddRange(Cards(OfType("TechCategory"), h));
            c.AddRange(Footer(h, "le-chat", "le-serveur", "les-modeles-ia", "outils-et-api"));
            return c;
        }

        private List<string> Organisation()
        {
            const string h = "organisation-et-projets";
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                "Cette page dit qui construit Kaonomi, qui s'en sert et ce qui est en projet.",
                "", "## Qui est responsable de quoi", "",
            };
            foreach (var (owner, count) in MostCommon(_sheets, s => string.IsNullOrWhiteSpace(s.Owner) ? "(sans responsable)" : s.Owner!))
                c.Add(count > 1 ? $"- **{Esc(owner)}** est responsable de {count} fiches sur {_sheets.Count}." : $"- **{Esc(owner)}** est responsable d'une fiche.");
            c.AddRange(new[] { "", "## L'équipe", "" });
            c.AddRange(Cards(OfType("Organization"), h));
            c.AddRange(new[] { "## Les utilisateurs", "" });
            c.AddRange(Cards(OfType("UserGroup"), h));
            c.AddRange(new[] { "## Le projet", "" });
            c.AddRange(Cards(OfType("Project"), h));
            Add(c, () =>
            {
                Require(OfType("Provider").SequenceEqual(new[] { "Liquid AI", "Mistral AI" }));
                return new[]
                {
                    "## Les fournisseurs", "",
                    $"Les deux fournisseurs de modèles, Liquid AI et Mistral AI, sont décrits dans {Link("les-modeles-ia")}.",
                    "",
                };
            }, "## Les fournisseurs", "", $"Les fournisseurs de modèles sont décrits dans {Link("les-modeles-ia")}.", "");
            c.AddRange(Footer(h, "feuille-de-route", "les-agents", "le-chat"));
            return c;
        }

        private static readonly Dictionary<string, (string One, string Many)> PhaseAdjective = new()
        {
            ["actif"] = ("active", "actives"), ["planifié"] = ("planifiée", "planifiées"), ["en retrait"] = ("en retrait", "en retrait"),
            ["en déploiement"] = ("en déploiement", "en déploiement"), ["fin de vie"] = ("en fin de vie", "en fin de vie"), ["non daté"] = ("sans date", "sans date"),
        };

        private string ByType(IEnumerable<ArchiSheet> members) =>
            string.Join(", ", MostCommon(members, s => s.Type).Select(t => $"{t.Count} {TypeCount(t.Key, t.Count)}"));

        private List<string> Roadmap()
        {
            const string h = "feuille-de-route";
            var phases = MostCommon(_sheets, Phase);
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                $"La feuille de route se lit dans le cycle de vie des fiches : planifié, en déploiement, actif, en retrait, fin de vie. Au {DateFr(AsOf)}, "
                + string.Join(", ", phases.Select(p => $"{p.Count} fiche{(p.Count > 1 ? "s" : "")} {(p.Count > 1 ? "sont" : "est")} {(p.Count > 1 ? PhaseAdjective[p.Key].Many : PhaseAdjective[p.Key].One)}")) + ".",
                "", "## Aujourd'hui", "",
            };
            foreach (var name in new[] { "actif", "en déploiement", "planifié", "en retrait", "fin de vie" })
            {
                var members = _sheets.Where(s => Phase(s) == name).ToList();
                if (members.Count == 0)
                    continue;
                c.Add(members.Count > 12
                    ? $"- **{Upper(name)}** ({members.Count}) : {ByType(members)}."
                    : $"- **{Upper(name)}** ({members.Count}) : {Refs(members.Select(s => s.Name), h)}.");
            }
            c.AddRange(new[] { "", "## Les vagues", "" });
            // The fact sheets with the same dates make a wave; the waves by their dates, the earliest first.
            var labels = new[] { ("Plan", "planifié"), ("PhaseIn", "déploiement"), ("Active", "actif"), ("PhaseOut", "retrait"), ("EndOfLife", "fin de vie") };
            var waves = _sheets.GroupBy(s => string.Join("|", PhaseKeys.Where(k => Date(s, k) is not null).Select(k => $"{k}={Date(s, k)!.Value:yyyy-MM-dd}")))
                .Select(g => (Dates: PhaseKeys.Where(k => Date(g.First(), k) is not null).Select(k => (Key: k, Date: Date(g.First(), k)!.Value)).ToList(), Members: g.ToList()))
                .Where(w => w.Dates.Count > 0)
                .ToList();
            waves.Sort((a, b) =>
            {
                for (var i = 0; i < Math.Min(a.Dates.Count, b.Dates.Count); i++)
                {
                    var byKey = string.CompareOrdinal(a.Dates[i].Key, b.Dates[i].Key);
                    if (byKey != 0)
                        return byKey;
                    var byDate = a.Dates[i].Date.CompareTo(b.Dates[i].Date);
                    if (byDate != 0)
                        return byDate;
                }
                return a.Dates.Count.CompareTo(b.Dates.Count);
            });
            foreach (var (dates, members) in waves)
            {
                c.Add("### " + string.Join(" → ", labels.Where(l => dates.Any(d => d.Key == l.Item1)).Select(l => $"{l.Item2} {DateFr(dates.First(d => d.Key == l.Item1).Date)}")));
                c.Add("");
                if (members.Count > 12)
                    c.Add($"{members.Count} fiches suivent ce calendrier : c'est le socle de Kaonomi, tout ce qui n'est pas cité dans les vagues suivantes.");
                else
                    foreach (var sheet in members)
                        c.Add($"- {Ref(sheet, h)} ({TypeOne(sheet.Type)}) : {Lower(About(sheet.Name))}");
                c.Add("");
            }
            var planned = _sheets.Where(s => Phase(s) == "planifié").ToList();
            Add(c, () =>
            {
                const string guard = "Personal data masking";
                Require(planned.Count > 1 && planned.Select(s => string.Join("|", PhaseKeys.Select(k => Date(s, k)))).Distinct().Count() == 1
                        && planned.Any(s => s.Name == Project) && planned.Any(s => s.Name == guard)
                        && !_relations.Any(r => (_byId[r.From].Name == Project && _byId[r.To].Name == guard) || (_byId[r.From].Name == guard && _byId[r.To].Name == Project)));
                return new[]
                {
                    "## Ce qui arrive", "",
                    $"Les {planned.Count} fiches planifiées suivent le même calendrier. Le projet {Ref(Project, h)} déplace les agents du chat vers le serveur, derrière MCP : "
                    + $"il met en œuvre {Some(OutOf(Project, "implements"), h)}. Le garde-fou {Ref(guard, h)} a les mêmes dates, sans être relié au projet dans le référentiel.",
                    "",
                };
            });
            Add(c, () =>
            {
                Require(OutOf(Assistant, "guarded by").Any(g => Phase(S(g)) == "planifié") && Phase(S("Next reasoning model")) == "planifié"
                        && OutOf("Next reasoning model", "evaluated by").Contains("Answer faithfulness"));
                return new[]
                {
                    $"D'ici là, {Ref(Assistant, h)} est relié à un garde-fou qui n'est pas encore en service, et {Ref("Next reasoning model", h)} "
                    + $"est déjà relié à l'évaluation {Ref("Answer faithfulness", h)}.",
                    "",
                };
            });
            Add(c, () =>
            {
                Require(Phase(S(OldChat)) == "en retrait" && Date(S(OldChat), "EndOfLife") is null);
                return new[]
                {
                    "## Ce qui part", "",
                    $"{Ref(OldChat, h)} est en retrait depuis le {DateOf(OldChat, "PhaseOut")}. "
                    + "Le référentiel ne lui donne pas encore de date de fin de vie.",
                    "",
                };
            });
            c.AddRange(Footer(h, "organisation-et-projets", "les-agents", "gouvernance-ia", "architecture"));
            return c;
        }

        private List<string> Governance()
        {
            const string h = "gouvernance-ia";
            var ai = _sheets.Where(s => s.Type is "AIModel" or "Agent" or "MultiAgentSystem").ToList();
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                "Cette page rassemble ce que le référentiel dit des risques de l'IA de Kaonomi : le niveau de risque de chaque modèle, agent et système multi-agents au sens de l'AI Act européen, "
                + "puis les points d'attention qui en découlent. Elle ne remplace pas une analyse de conformité : elle montre ce que les fiches contiennent, et ce qu'elles ne contiennent pas encore.",
                "", "## Les niveaux de risque", "",
            };
            foreach (var level in new[] { "Unacceptable", "High", "Limited", "Minimal" })
            {
                var members = ai.Where(s => s.AiRisk == level).ToList();
                if (members.Count > 0)
                    c.Add($"- **Risque {Risk(level)}** ({members.Count}) : {Refs(members.Select(s => s.Name), h)}.");
            }
            var unrated = ai.Where(s => string.IsNullOrEmpty(s.AiRisk) || s.AiRisk == "Not assessed").ToList();
            c.Add(unrated.Count > 0 ? $"- **Non évalué** : {Refs(unrated.Select(s => s.Name), h)}." : "- Toutes les fiches d'IA ont un niveau de risque renseigné.");
            c.AddRange(new[] { "", "## Les points d'attention", "" });
            foreach (var sheet in ai.Where(s => s.AiRisk == "High"))
            {
                var guarded = OutOf(sheet.Name, "guarded by").Count > 0;
                var evaluated = OutOf(sheet.Name, "evaluated by").Count > 0;
                c.Add($"- **Un risque élevé{(guarded ? string.Empty : " sans garde-fou")}.** {Ref(sheet, h)} est classé à risque élevé ; le référentiel ne lui associe "
                      + $"{(guarded ? "un garde-fou" : "aucun garde-fou")} et {(evaluated ? "une évaluation" : "aucune évaluation")}.");
            }
            var alone = _sheets.Where(s => s.Type == "Agent" && FieldOf(s, "human") == "No" && OutOf(s.Name, "guarded by").Count == 0).Select(s => s.Name).ToList();
            if (alone.Count > 1)
                c.Add($"- **Des agents autonomes sans personne ni garde-fou.** {Refs(alone, h)} agissent seuls, sans humain dans la boucle, et aucun garde-fou ne leur est relié.");
            else if (alone.Count == 1)
                c.Add($"- **Un agent autonome sans personne ni garde-fou.** {Refs(alone, h)} agit seul, sans humain dans la boucle, et aucun garde-fou ne lui est relié.");
            var guards = _sheets.Where(s => s.Type == "Guardrail").ToList();
            c.AddRange(Line(() =>
            {
                Require(guards.Count == 1 && Phase(guards[0]) is "planifié" or "en déploiement");
                var guard = guards[0];
                return $"- **Un garde-fou qui n'est pas encore en service.** {Ref(guard, h)} est le seul garde-fou du référentiel ; il protège {Some(Into(guard.Name, "guarded by"), h)}, "
                       + $"mais il est {Phase(guard)} et ne sera actif qu'à partir du {DateOf(guard.Name, "Active")}.";
            }));
            c.AddRange(Line(() =>
            {
                var never = _sheets.Where(s => s.Type is "Agent" or "MultiAgentSystem" && OutOf(s.Name, "evaluated by").Count == 0).Select(s => s.Name).ToList();
                var evaluated = ai.Where(s => OutOf(s.Name, "evaluated by").Count > 0).Select(s => s.Name).ToList();
                var evaluations = evaluated.SelectMany(n => OutOf(n, "evaluated by")).Distinct().ToList();
                Require(evaluated.Count > 1 && never.Count > 1 && evaluations.Count == 1);
                var score = Field(evaluations[0], "score") is { Length: > 0 } s ? $", score {Esc(s)}" : string.Empty;
                return $"- **Peu d'évaluations.** Seuls {Refs(evaluated, h)} sont reliés à une évaluation ({Ref(evaluations[0], h)}{score}). "
                       + $"{Refs(never, h)} ne sont reliés à aucune évaluation.";
            }));
            c.AddRange(Line(() =>
            {
                var personal = _sheets.Where(s => FieldOf(s, "personal") == "Yes").Select(s => s.Name).ToList();
                Require(personal.Count == 1 && S(personal[0]).Type == "DataObject");
                return $"- **Des données personnelles.** L'objet de données {Refs(personal, h)} contient des données personnelles ; il est écrit par {Some(Into(personal[0], "writes"), h)}.";
            }));
            var limits = new List<string>();
            if (Count("AIModel") > 0 && OfType("AIModel").All(m => Field(m, "hosting") == "Local"))
                limits.Add($"- **Les modèles sont locaux.** Les {Count("AIModel")} modèles du référentiel sont hébergés en local.");
            var watched = _sheets.Where(s => s.Type == "Agent" && FieldOf(s, "human") == "Yes").Select(s => s.Name).ToList();
            if (watched.Count > 0)
                limits.Add($"- **Une personne reste dans la boucle** pour {Refs(watched, h)}.");
            limits.AddRange(Line(() =>
            {
                Require(Count("Tool") == 1 && Field("Search", "effects") == "Read only");
                return $"- **L'outil des agents ne modifie rien.** {Ref("Search", h)} est en lecture seule.";
            }));
            if (limits.Count > 0)
            {
                c.AddRange(new[] { "", "## Ce qui limite déjà le risque", "" });
                c.AddRange(limits);
            }
            c.AddRange(new[] { "", "## Le détail, fiche par fiche", "" });
            var order = new[] { "Unacceptable", "High", "Limited", "Minimal" };
            foreach (var sheet in ai.OrderBy(s => Array.IndexOf(order, s.AiRisk ?? string.Empty) is var i and >= 0 ? i : order.Length).ThenBy(s => s.Name.ToLowerInvariant(), StringComparer.Ordinal))
            {
                var bits = new List<string> { TypeOne(sheet.Type), $"risque {Risk(sheet.AiRisk)}", Phase(sheet) };
                if (sheet.Type == "Agent")
                {
                    bits.Add(Esc(Value(FieldOf(sheet, "autonomy"))));
                    bits.Add($"humain dans la boucle : {Esc(Value(FieldOf(sheet, "human")))}");
                }
                var guard = OutOf(sheet.Name, "guarded by");
                var evaluation = OutOf(sheet.Name, "evaluated by");
                bits.Add("garde-fou : " + (guard.Count > 0 ? Refs(guard, h) : "aucun"));
                bits.Add("évaluation : " + (evaluation.Count > 0 ? Refs(evaluation, h) : "aucune"));
                c.Add($"- {Ref(sheet, h)} — {string.Join(" ; ", bits)}.");
            }
            c.Add("");
            c.AddRange(Footer(h, "les-agents", "les-modeles-ia", "la-vision", "donnees-et-connaissances", "feuille-de-route"));
            return c;
        }

        /// <summary>The fact sheets of the kinds without a page of their own (a process, an application that is not one of Kaonomi), by kind.</summary>
        private List<string> OthersPage(List<ArchiSheet> others)
        {
            const string h = Others;
            var c = new List<string>
            {
                $"# {TitleOf(h)}", "",
                "Cette page rassemble les fiches du référentiel dont le genre n'a pas de page à lui dans ce wiki.",
                "",
            };
            foreach (var group in others.GroupBy(s => s.Type).OrderBy(g => TypeOne(g.Key), StringComparer.Ordinal))
            {
                c.AddRange(new[] { $"## {Upper(TypeCount(group.Key, group.Count()))}", "" });
                c.AddRange(Cards(group.Select(s => s.Name).OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal), h));
            }
            c.AddRange(Footer(h, "architecture", "capacites-metier"));
            return c;
        }
    }
}
