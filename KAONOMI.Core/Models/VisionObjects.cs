using System;
using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>A kind of object seen during a period: how many at once, how often, how sure.</summary>
    public class ObjectSeen
    {
        /// <summary>The class of the object, as the model names it (<c>person</c>, <c>cup</c>, <c>cell phone</c>…), and its number.</summary>
        public string name { get; set; } = string.Empty;

        public int classId { get; set; }

        /// <summary>The most of them seen in one frame, and their mean number over all the frames of the period.</summary>
        public int max { get; set; }

        public double mean { get; set; }

        /// <summary>The share of the frames where at least one was seen, in per cent.</summary>
        public double presence { get; set; }

        /// <summary>How sure the detections were (0 to 1): their mean and their best.</summary>
        public double confidence { get; set; }

        public double best { get; set; }

        /// <summary>How long after the start of the period it was seen for the first and the last time, in seconds.</summary>
        public double firstSeen { get; set; }

        public double lastSeen { get; set; }
    }

    /// <summary>
    /// Everything /vision of the chat detected during one minute (index <c>visionobjects</c>): each kind of
    /// object with how many of them at once, how often and how sure, the number of persons, and the alerts (two
    /// persons overlapping, an object of the class watched). One document per minute while the window is open and
    /// its RECORD switch on, and one for the last, shorter period when it closes. Its id is given by the chat
    /// (session, number of the period).
    /// </summary>
    public class VisionObjects : EntityBase
    {
        /// <summary>One opening of /vision: its periods share it.</summary>
        public string session { get; set; } = string.Empty;

        public DateTime start { get; set; }

        public DateTime end { get; set; }

        /// <summary>How long the period lasts (60 seconds, less for the last one), and how many frames it sums up.</summary>
        public double seconds { get; set; }

        public int frames { get; set; }

        /// <summary>The model that detected (<c>YOLOv8 nano</c>, <c>YOLOv8 medium</c>), where it ran, and the smallest confidence kept.</summary>
        public string model { get; set; } = string.Empty;

        public string device { get; set; } = string.Empty;

        public double threshold { get; set; }

        /// <summary>The kinds of objects seen, the most present first.</summary>
        public List<ObjectSeen> objects { get; set; } = new List<ObjectSeen>();

        /// <summary>Their names alone, for the search (<c>person</c>, <c>cup</c>…).</summary>
        public List<string> names { get; set; } = new List<string>();

        /// <summary>The most objects (of every kind) seen in one frame, and the most persons.</summary>
        public int maxObjects { get; set; }

        public int maxPersons { get; set; }

        /// <summary>The share of the frames where two persons overlapped, in per cent.</summary>
        public double overlap { get; set; }

        /// <summary>The class of objects watched (empty: none), and the share of the frames where one was seen, in per cent.</summary>
        public string alertClass { get; set; } = string.Empty;

        public double alert { get; set; }
    }
}
