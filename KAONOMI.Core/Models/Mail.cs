using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// A message of the internal mail of the chat (<c>/message</c>, <c>/msg</c>), one document of the <c>mail</c> index,
    /// for the searches (<c>POST /search</c>, <c>POST /mail/search</c>): the message itself stays a file of the machine
    /// of the chat (<c>~/kaonomi/messages</c>), which writes it here when it is sent and gathers the ones missing when
    /// it starts. Its id is the one of that file. Only what the receivers read: the drafts and the notes of each user
    /// are not here; an encrypted message gives its envelope only (sender, receivers, subject, date), never its text,
    /// its pictures or its entities. Never erased: marked deleted once every user removed it (<see cref="ISoftDeletable"/>).
    /// </summary>
    public class Mail : EntityBase, ISoftDeletable
    {
        /// <summary>Never erased: marked deleted instead (<see cref="ISoftDeletable"/>).</summary>
        public bool isDeleted { get; set; } = false;

        /// <summary>The login of the sender.</summary>
        public string from { get; set; } = string.Empty;

        /// <summary>The name of the sender (its display name in the chat).</summary>
        public string fromName { get; set; } = string.Empty;

        /// <summary>The logins of the receivers.</summary>
        public List<string> to { get; set; } = new List<string>();

        /// <summary>The names of the receivers.</summary>
        public List<string> toNames { get; set; } = new List<string>();

        public string subject { get; set; } = string.Empty;

        /// <summary>The text of the message, its pictures left out (empty for an encrypted message).</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>True for a message encrypted in the chat (AES-256): only its envelope is here.</summary>
        public bool encrypted { get; set; }

        /// <summary>How many pictures the message holds.</summary>
        public int pictureCount { get; set; }

        /// <summary>The file names of its pictures (none for an encrypted message).</summary>
        public List<string> pictureNames { get; set; } = new List<string>();

        /// <summary>The entities the NER found in it when it was sent, "name (TYPE)" (none for an encrypted message).</summary>
        public List<string> entities { get; set; } = new List<string>();

        /// <summary>The message it answers or forwards (its id), when there is one.</summary>
        public string? replyTo { get; set; }

        /// <summary>Made ready to be saved: its title the subject, its lists without blanks.</summary>
        public void Normalize()
        {
            subject = (subject ?? string.Empty).Trim();
            title = subject.Length > 0 ? subject : "(no subject)";
            text = encrypted ? string.Empty : (text ?? string.Empty).Trim();
            to = Clean(to);
            toNames = Clean(toNames);
            pictureNames = encrypted ? new List<string>() : Clean(pictureNames);
            entities = encrypted ? new List<string>() : Clean(entities);
        }

        private static List<string> Clean(List<string>? values)
        {
            var result = new List<string>();
            foreach (var value in values ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value.Trim()))
                    result.Add(value.Trim());
            return result;
        }
    }
}
