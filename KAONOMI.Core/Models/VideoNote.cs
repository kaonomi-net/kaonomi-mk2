namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A version of a note of /video (index <c>videonote</c>): what was done on a video of the chat and what came out
    /// of it. A note is an analysis of a segment (<c>segment</c>: a sheet of its pictures and what is said in it, read
    /// by a vision model), of one picture (<c>frame</c>), of the sound of a segment (<c>sound</c>: what is said, who
    /// speaks, how loud), a picture kept (<c>picture</c>) or an annotation written by the user (<c>annotation</c>).
    /// One document per version, its id made of the note and the number of the version
    /// (<c>videonote-&lt;noteId&gt;-v3</c>): a version is written once, never written over and never erased. Editing,
    /// running an analysis again, restoring a former version or deleting a note writes a new version; the one with
    /// the largest number is the note as it is now (deleted when <see cref="isDeleted"/>).
    /// </summary>
    public class VideoNote : EntityBase
    {
        /// <summary>The note all its versions belong to.</summary>
        public string noteId { get; set; } = string.Empty;

        /// <summary>The number of the version, from 1.</summary>
        public int version { get; set; }

        /// <summary>What the note is: <c>segment</c>, <c>frame</c>, <c>sound</c>, <c>picture</c> or <c>annotation</c>.</summary>
        public string kind { get; set; } = string.Empty;

        /// <summary>What this version did: <c>create</c>, <c>edit</c>, <c>rerun</c>, <c>restore</c> or <c>delete</c>.</summary>
        public string operation { get; set; } = string.Empty;

        /// <summary>The video: a key made of its path and its size (the notes of a video are found by it).</summary>
        public string videoKey { get; set; } = string.Empty;

        /// <summary>The path of the video on the machine where the note was written.</summary>
        public string videoPath { get; set; } = string.Empty;

        /// <summary>The name of the file of the video.</summary>
        public string videoName { get; set; } = string.Empty;

        /// <summary>How long the video lasts, in seconds.</summary>
        public double videoDuration { get; set; }

        /// <summary>Where the note starts in the video, in seconds (the time of the picture for a frame).</summary>
        public double start { get; set; }

        /// <summary>Where the note ends in the video, in seconds (the same as <see cref="start"/> for a frame).</summary>
        public double end { get; set; }

        /// <summary>What the user asked to look for (empty: a general analysis).</summary>
        public string question { get; set; } = string.Empty;

        /// <summary>The result of the analysis, or the text of the annotation (Markdown).</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>What is said in the segment (the speech recognition), when the sound was listened to.</summary>
        public string transcript { get; set; } = string.Empty;

        /// <summary>Who speaks in the segment (the voices enrolled in /recorder), with when.</summary>
        public List<string> speakers { get; set; } = new List<string>();

        /// <summary>The model that answered (empty for an annotation).</summary>
        public string model { get; set; } = string.Empty;

        /// <summary>The version of the prompt used (the prompts video.* of /prompts; 0: the default).</summary>
        public int promptVersion { get; set; }

        /// <summary>The picture of the note, a JPEG in base 64: the frame, or the sheet of the pictures of the segment.</summary>
        public string picture { get; set; } = string.Empty;

        /// <summary>Where the picture was saved on the disk, when it was (a picture kept).</summary>
        public string pictureFile { get; set; } = string.Empty;

        /// <summary>How long the analysis took, in milliseconds.</summary>
        public long durationMs { get; set; }

        /// <summary>True for the version that deletes the note: the note is hidden, its versions are kept.</summary>
        public bool isDeleted { get; set; }

        /// <summary>The id of the version <paramref name="version"/> of the note <paramref name="noteId"/>.</summary>
        public static string IdOf(string noteId, int version) => $"videonote-{noteId}-v{version}";
    }
}
