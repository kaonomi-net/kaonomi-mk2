using System;

namespace KAONOMI.Core.Models
{
    /// <summary>The smallest, the largest, the mean and the standard deviation of a measure over a period.</summary>
    public class Spread
    {
        public double min { get; set; }

        public double max { get; set; }

        public double mean { get; set; }

        public double std { get; set; }
    }

    /// <summary>The share of each emotion a face expresses, in per cent (they add up to 100).</summary>
    public class EmotionShares
    {
        public double angry { get; set; }

        public double disgust { get; set; }

        public double fearful { get; set; }

        public double happy { get; set; }

        public double neutral { get; set; }

        public double sad { get; set; }

        public double surprised { get; set; }
    }

    /// <summary>
    /// What /vision of the chat saw of a face during a period where its state stayed the same (index
    /// <c>faceepisode</c>): who it is, what it expresses (the share of each emotion, in per cent, averaged over the
    /// period), how large its face is in the picture and how much it moves. The document is written again every
    /// second while the state lasts; another one starts when the emotion that leads changes (and holds 2 seconds),
    /// when the person recognised changes, when the face moves more or less (another class of activity), when it
    /// leaves the picture, or after 60 seconds. Its id is given by the chat (session, face followed, number).
    /// <para>
    /// The area is the one of the rectangle of the face, in per cent of the picture: it grows when the person comes
    /// closer. The movement is how far the middle of that rectangle goes, in per cent of the width of the face per
    /// second: it does not depend on the distance to the camera.
    /// </para>
    /// </summary>
    public class FaceEpisode : EntityBase
    {
        /// <summary>One opening of /vision: its episodes and observations share it.</summary>
        public string session { get; set; } = string.Empty;

        /// <summary>The face followed in the session (a number): two unknown faces are two tracks.</summary>
        public int track { get; set; }

        /// <summary>The name of the face, or <c>unknown</c> when it is not recognised.</summary>
        public string name { get; set; } = string.Empty;

        public bool known { get; set; }

        /// <summary>How sure the identification is (cosine similarity, 0 to 1), averaged over the episode.</summary>
        public double similarity { get; set; }

        public DateTime start { get; set; }

        public DateTime end { get; set; }

        /// <summary>How long the episode lasts, and how many frames it sums up.</summary>
        public double seconds { get; set; }

        public int frames { get; set; }

        /// <summary>The emotion that leads over the episode, and the share of each, in per cent.</summary>
        public string emotion { get; set; } = string.Empty;

        public EmotionShares emotions { get; set; } = new EmotionShares();

        /// <summary>The area of the rectangle of the face, in per cent of the picture.</summary>
        public Spread area { get; set; } = new Spread();

        /// <summary>The movement of the face, in per cent of its width per second.</summary>
        public Spread movement { get; set; } = new Spread();

        /// <summary>How much it moves, as a class: <c>still</c>, <c>moving</c> or <c>agitated</c>.</summary>
        public string activity { get; set; } = string.Empty;

        /// <summary>
        /// What ended the episode: <c>emotion</c>, <c>identity</c>, <c>activity</c>, <c>left</c> (the face left the
        /// picture), <c>timeout</c> (60 seconds) or <c>stopped</c> (the window closed); empty while it lasts.
        /// </summary>
        public string closedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// What /vision saw of a face during one second, written at the instant something changed (index
    /// <c>faceobservation</c>): the face appeared or left, the emotion that leads, the person recognised or the
    /// activity changed. Between two of them, the episodes (<see cref="FaceEpisode"/>) hold the rest: together they
    /// give the exact time of each change without one document per second.
    /// </summary>
    public class FaceObservation : EntityBase
    {
        public string session { get; set; } = string.Empty;

        public int track { get; set; }

        public string name { get; set; } = string.Empty;

        public bool known { get; set; }

        /// <summary>How sure the identification is during this second (cosine similarity, 0 to 1).</summary>
        public double similarity { get; set; }

        /// <summary>How many frames this second sums up.</summary>
        public int frames { get; set; }

        public string emotion { get; set; } = string.Empty;

        public EmotionShares emotions { get; set; } = new EmotionShares();

        public Spread area { get; set; } = new Spread();

        public Spread movement { get; set; } = new Spread();

        public string activity { get; set; } = string.Empty;

        /// <summary>
        /// What changed: <c>appeared</c>, <c>emotion</c>, <c>identity</c>, <c>activity</c>, <c>left</c> (the face left
        /// the picture) or <c>stopped</c> (the window closed, or the recording was switched off).
        /// </summary>
        public string change { get; set; } = string.Empty;

        /// <summary>What it was before the change (the former emotion, name or activity); empty when the face appears.</summary>
        public string previous { get; set; } = string.Empty;

        /// <summary>The episode that starts with this change (the one that ends, when the face leaves).</summary>
        public string episode { get; set; } = string.Empty;
    }
}
