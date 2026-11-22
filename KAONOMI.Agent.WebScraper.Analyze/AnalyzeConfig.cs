using System.Text.RegularExpressions;
using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze
{
    /// <summary>
    /// config.json of the agent: the settings of every Kaonomi host (<see cref="Config"/>) and how the pages are
    /// analysed.
    /// </summary>
    public sealed class AnalyzeConfig : Config
    {
        /// <summary>
        /// The model that analyses the text. Empty: the model of the advanced conversations of the chat
        /// (<c>modes.advanced</c> of its config.yml, edited in /models), else <c>llmmodel</c>.
        /// </summary>
        public string advancedModel { get; set; } = string.Empty;

        /// <summary>The model that analyses the pictures. Empty: <c>modes.image</c> of the config.yml of the chat, else the model of the text.</summary>
        public string imageModel { get; set; } = string.Empty;

        /// <summary>The config.yml of the chat, where the models of each use are. Empty: looked for beside the projects.</summary>
        public string chatConfigFile { get; set; } = string.Empty;

        /// <summary>
        /// Writes the ontology: the link of the page and what is missing added to the entities found there, the others
        /// created. False: the ontology is only read (each entity is told existing or not, nothing is written).
        /// </summary>
        public bool updateOntology { get; set; } = true;

        /// <summary>Most pages analysed in one run when no page is asked (the others at the next run).</summary>
        public int maxPagesPerRun { get; set; } = 3;

        /// <summary>
        /// Minutes one stage of the analysis of a page may take (a grain call fails after 10 minutes): what is not done
        /// by then is left out, with a warning in the analysis.
        /// </summary>
        public double stageBudgetMinutes { get; set; } = 8;

        /// <summary>Seconds the model has to give one answer.</summary>
        public int llmTimeoutSeconds { get; set; } = 240;

        /// <summary>Most characters of the page given to the model at once: a longer page is read in several parts.</summary>
        public int llmChunkChars { get; set; } = 6000;

        /// <summary>Most characters of a page read by the model: the end of a longer page is left out, with a warning.</summary>
        public int maxTextChars { get; set; } = 36000;

        /// <summary>Most entities of a page analysed (those named the most often first).</summary>
        public int maxEntitiesPerPage { get; set; } = 40;

        /// <summary>How many entities the model is asked the emotions of at once.</summary>
        public int emotionBatchSize { get; set; } = 6;

        /// <summary>Most characters of the passages about an entity given to the model for its emotions.</summary>
        public int passageChars { get; set; } = 1500;

        /// <summary>Most pictures of a page analysed.</summary>
        public int maxImagesPerPage { get; set; } = 12;

        /// <summary>Smaller pictures (icons, logos of a menu) are not analysed.</summary>
        public int minImageBytes { get; set; } = 15000;

        /// <summary>Larger pictures are not analysed.</summary>
        public int maxImageBytes { get; set; } = 6_000_000;

        private (string Advanced, string Image)? _modes;

        /// <summary>The model that analyses the text, and where its name comes from.</summary>
        public (string Model, string From) TextModel()
        {
            if (!string.IsNullOrWhiteSpace(advancedModel))
                return (advancedModel.Trim(), "advancedModel of config.json");
            var (advanced, _) = Modes();
            return advanced.Length > 0 ? (advanced, "modes.advanced of the config.yml of the chat") : (llmmodel, "llmmodel of config.json");
        }

        /// <summary>The model that analyses the pictures, and where its name comes from.</summary>
        public (string Model, string From) PictureModel()
        {
            if (!string.IsNullOrWhiteSpace(imageModel))
                return (imageModel.Trim(), "imageModel of config.json");
            var (_, image) = Modes();
            return image.Length > 0 ? (image, "modes.image of the config.yml of the chat") : (TextModel().Model, "the model of the text");
        }

        /// <summary>The models <c>advanced</c> and <c>image</c> of the section <c>modes</c> of the config.yml of the chat (empty when not found).</summary>
        private (string Advanced, string Image) Modes()
        {
            if (_modes is { } known)
                return known;
            var result = (Advanced: string.Empty, Image: string.Empty);
            var file = ChatConfigFile();
            if (file is not null)
            {
                try
                {
                    var inModes = false;
                    foreach (var line in File.ReadLines(file))
                    {
                        if (Regex.IsMatch(line, @"^modes\s*:"))
                        {
                            inModes = true;
                            continue;
                        }
                        if (!inModes)
                            continue;
                        if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
                            break;
                        var match = Regex.Match(line, @"^\s+(advanced|image)\s*:\s*(.*?)\s*(#.*)?$");
                        if (!match.Success)
                            continue;
                        var value = match.Groups[2].Value.Trim().Trim('"', '\'');
                        if (match.Groups[1].Value == "advanced")
                            result.Advanced = value;
                        else
                            result.Image = value;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
            _modes = result;
            return result;
        }

        /// <summary>
        /// The config.yml the chat reads: the one given, else the one beside the binaries of the Godot chat
        /// (<c>KAONOMI.ChatGodot/.godot/mono/temp/bin/Debug</c>, where /models writes), else the one of its project, in
        /// the folder that holds the projects (found from the folder of this agent up).
        /// </summary>
        public string? ChatConfigFile()
        {
            if (!string.IsNullOrWhiteSpace(chatConfigFile))
                return File.Exists(chatConfigFile) ? chatConfigFile : null;
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                var folder = new DirectoryInfo(start);
                for (var i = 0; i < 7 && folder is not null; i++, folder = folder.Parent)
                {
                    foreach (var relative in new[]
                             {
                                 Path.Combine("KAONOMI.ChatGodot", ".godot", "mono", "temp", "bin", "Debug", "config.yml"),
                                 Path.Combine("KAONOMI.ChatGodot", "config.yml"),
                                 Path.Combine("KAONOMI.app", "Contents", "MacOS", "config.yml"),
                                 Path.Combine("KAONOMI", "config.yml")
                             })
                    {
                        var file = Path.Combine(folder.FullName, relative);
                        if (File.Exists(file))
                            return file;
                    }
                }
            }
            return null;
        }
    }
}
