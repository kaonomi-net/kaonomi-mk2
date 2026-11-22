using System.Security.Cryptography;
using System.Text;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A document the user edits in a window of the chat (an idea, a task, a page of the wiki, a mind map…): each time
    /// it is saved, the server keeps that state as a version (<see cref="DocumentVersion"/>, index
    /// <c>documentversion</c>); nothing saved is lost, and any version can be restored.
    /// </summary>
    public interface IVersioned
    {
    }

    /// <summary>
    /// A version of a document of the search engine (index <c>documentversion</c>): the whole document as it was saved
    /// (<see cref="json"/>), its number, what changed since the version before and what made it (<c>save</c>,
    /// <c>delete</c> when it was marked deleted, <c>erase</c> just before it was erased for good, <c>restore</c> when a
    /// former version was taken again). Written once, never written over nor erased; the newest version is the
    /// document as it is now.
    /// </summary>
    public class DocumentVersion : EntityBase
    {
        /// <summary>The document all its versions belong to: a key made of its index and its id.</summary>
        public string documentKey { get; set; } = string.Empty;

        /// <summary>The index of the document (without the prefix of the server): <c>idea</c>, <c>wiki</c>…</summary>
        public string documentIndex { get; set; } = string.Empty;

        /// <summary>The id of the document in its index.</summary>
        public string documentId { get; set; } = string.Empty;

        /// <summary>The number of the version, from 1.</summary>
        public int version { get; set; }

        /// <summary>What made the version: <c>save</c>, <c>delete</c>, <c>erase</c> or <c>restore</c>.</summary>
        public string operation { get; set; } = string.Empty;

        /// <summary>The title of the document then (to recognise it in a list).</summary>
        public string documentTitle { get; set; } = string.Empty;

        /// <summary>The fields that changed since the version before (all of them for the first one).</summary>
        public List<string> changed { get; set; } = new List<string>();

        /// <summary>The document as it was saved, as the search engine keeps it (JSON).</summary>
        public string json { get; set; } = string.Empty;

        /// <summary>The length of <see cref="json"/>, in characters.</summary>
        public int size { get; set; }

        /// <summary>The key of a document: its index and its id, hashed (only letters and digits, for an exact search).</summary>
        public static string KeyOf(string index, string id) =>
            Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(index + "|" + id)))[..24].ToLowerInvariant();

        /// <summary>The id of the version <paramref name="version"/> of the document of <paramref name="key"/>.</summary>
        public static string IdOf(string key, int version) => $"ver-{key}-v{version}";
    }
}
