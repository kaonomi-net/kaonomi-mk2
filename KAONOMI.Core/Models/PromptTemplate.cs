namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A version of a prompt the chat sends to the models (index <c>prompttemplate</c>): the prompts of its tools,
    /// edited in /prompts. One document per version, its id made of the key and the number of the version
    /// (<c>prompt-cv.summary-v3</c>): a version is written once, never written over and never erased. The version in
    /// use is the one with the largest number; the others are the history of the prompt.
    /// </summary>
    public class PromptTemplate : EntityBase
    {
        /// <summary>The key of the prompt, as the code of the chat asks for it (<c>cv.summary</c>, <c>game.chess</c>…).</summary>
        public string key { get; set; } = string.Empty;

        /// <summary>The number of the version, from 1.</summary>
        public int version { get; set; }

        /// <summary>The prompt; what is between double curly brackets (<c>{{language}}</c>) is replaced by the chat when it uses it.</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>
        /// Where the version comes from: <c>config</c> (the default of config.yml, written when the search engine had
        /// none), <c>edit</c>, <c>upload</c> (a JSON file), <c>restore</c> (a former version taken again),
        /// <c>default</c> (back to the default).
        /// </summary>
        public string source { get; set; } = string.Empty;
    }
}
