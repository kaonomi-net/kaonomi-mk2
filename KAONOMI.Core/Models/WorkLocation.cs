using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A place where the user works in front of the computer, measured by SET LOCATION in /vision of the chat
    /// (index <c>worklocation</c>): the user sits in the usual working position and speaks for a few seconds; the
    /// size of the box of the face the camera sees and the level of the voice the microphone hears are kept. Then,
    /// when the face is at least that big and the voice at least that loud, the user speaks to the assistant. One
    /// document per name of place, its id given by the chat from the name (measured again: written over).
    /// </summary>
    public class WorkLocation : EntityBase
    {
        /// <summary>The name of the place, as typed (home office, desk at work…).</summary>
        public string name { get; set; } = string.Empty;

        /// <summary>The area of the box of the face, as a share of the frame (0 to 1): its mean over the measure.</summary>
        public double faceArea { get; set; }

        /// <summary>The mean box of the face, and the size of the frames, in pixels.</summary>
        public int faceWidth { get; set; }

        public int faceHeight { get; set; }

        public int frameWidth { get; set; }

        public int frameHeight { get; set; }

        /// <summary>How many frames saw the face during the measure.</summary>
        public int faceFrames { get; set; }

        /// <summary>The level of the voice: the root mean square of the samples (0 to 1) heard during the measure, and in dB.</summary>
        public double voiceLevel { get; set; }

        public double voiceDecibels { get; set; }

        /// <summary>How long it was measured, in seconds.</summary>
        public double seconds { get; set; }

        /// <summary>The camera and the microphone of the measure (another one would not give the same values).</summary>
        public string camera { get; set; } = string.Empty;

        public string microphone { get; set; } = string.Empty;

        /// <summary>
        /// What the vision model of the chat saw in a picture of the camera taken at the end of the measure: the place,
        /// the room, the objects, the light… (its text, in the language of the chat), and the model that wrote it.
        /// </summary>
        public string analysis { get; set; } = string.Empty;

        public string analysisModel { get; set; } = string.Empty;

        /// <summary>The entities the NER found in <see cref="analysis"/> (their names in the ontology when they are in it).</summary>
        public List<string> entities { get; set; } = new List<string>();

        /// <summary>The picture analysed, on the machine of the chat (<c>~/kaonomi/vision/locations/&lt;id&gt;.jpg</c>).</summary>
        public string picture { get; set; } = string.Empty;
    }
}
