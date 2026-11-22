using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A voice enrolled under a name by /recorder of the chat (who speaks): one document per sample of voice kept
    /// (index <c>voice</c>), its id given by the chat from the name and the file. It holds what the speaker model says
    /// of the sample (its embedding: a few hundred numbers; two voices are compared by their cosine), not the sound
    /// itself, so that the voices enrolled on a machine are found again on another one, or after its folder was lost.
    /// Really erased when the name is forgotten: nothing of a voice is kept then.
    /// </summary>
    public class Voice : EntityBase
    {
        /// <summary>The name the voice was enrolled under.</summary>
        public string name { get; set; } = string.Empty;

        /// <summary>The name of the file of the sample in the folder of the name (<c>~/kaonomi/audio/speakers/&lt;name&gt;</c>).</summary>
        public string file { get; set; } = string.Empty;

        /// <summary>How long the sample of voice was, in seconds, and where it came from (the microphone, a recording).</summary>
        public double seconds { get; set; }

        public string source { get; set; } = string.Empty;

        /// <summary>The language spoken in the sample (FR, EN…), when it was said; empty when it was not.</summary>
        public string language { get; set; } = string.Empty;

        /// <summary>The embedding of the voice, of length 1.</summary>
        public List<double> feature { get; set; } = new List<double>();

        /// <summary>The speaker model the embedding comes from: embeddings of two models cannot be compared.</summary>
        public string model { get; set; } = string.Empty;
    }
}
