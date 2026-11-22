using System;
using System.Collections.Generic;

namespace KAONOMI.Core.Models
{
    /// <summary>
    /// An item of a tool of the chat whose store is the search engine, the chat keeping a copy of it on its machine to
    /// work without the server (<c>LocalSync</c> of the chat): an event of <c>/calendar</c>, a fact sheet of <c>/archi</c>,
    /// a task of <c>/todo</c>, a request of <c>/support</c>, a note of <c>/obs</c>, an analysis of <c>/entity:</c>. One
    /// index per tool (<see cref="Kinds"/>), the same shape for all: what the searches read (<c>title</c>, <c>text</c>,
    /// <c>type</c>) and the item itself as the chat keeps it (<see cref="payload"/>, its JSON in base 64, so that the
    /// searches do not read its field names). <see cref="revision"/> changes at each save: two machines compare it to
    /// know who changed what. Never erased: a deleted item stays, marked, for the other machines to delete theirs;
    /// versioned at each save (<see cref="IVersioned"/>).
    /// </summary>
    public class SyncedRecord : EntityBase, ISoftDeletable, IVersioned
    {
        /// <summary>Marked deleted (on a machine): the others delete their copy.</summary>
        public bool isDeleted { get; set; } = false;

        /// <summary>What it is within its tool (an event, a fact sheet of a type, a relation, a note…).</summary>
        public string type { get; set; } = string.Empty;

        /// <summary>What the searches read: the text of the item, without its formatting.</summary>
        public string text { get; set; } = string.Empty;

        /// <summary>The item as the chat keeps it: its JSON, in base 64.</summary>
        public string payload { get; set; } = string.Empty;

        /// <summary>A new value at each save, given by the machine that saved it.</summary>
        public string revision { get; set; } = string.Empty;

        /// <summary>The kinds (their index) and their class.</summary>
        public static readonly IReadOnlyDictionary<string, Type> Kinds = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["calendarevent"] = typeof(CalendarEvent),
            ["archiitem"] = typeof(ArchiItem),
            ["daytodo"] = typeof(DayTodo),
            ["supportticket"] = typeof(SupportTicket),
            ["vaultnote"] = typeof(VaultNote),
            ["entityanalysis"] = typeof(EntityAnalysis)
        };

        /// <summary>A copy of <paramref name="source"/> as a <typeparamref name="T"/> (the class of its index).</summary>
        public static T As<T>(SyncedRecord source) where T : SyncedRecord, new() => new()
        {
            id = source.id,
            date = source.date,
            appName = source.appName,
            machineName = source.machineName,
            userName = source.userName,
            comment = source.comment,
            projectId = source.projectId,
            title = source.title,
            Tags = source.Tags ?? new List<string>(),
            isDeleted = source.isDeleted,
            type = source.type,
            text = source.text,
            payload = source.payload,
            revision = source.revision
        };
    }

    /// <summary>An event of <c>/calendar</c> (index <c>calendarevent</c>).</summary>
    public class CalendarEvent : SyncedRecord { }

    /// <summary>A fact sheet, a relation or the name of the repository of <c>/archi</c> (index <c>archiitem</c>).</summary>
    public class ArchiItem : SyncedRecord { }

    /// <summary>A task of the day of <c>/todo</c> (index <c>daytodo</c>).</summary>
    public class DayTodo : SyncedRecord { }

    /// <summary>A request for support of <c>/support</c> (index <c>supportticket</c>).</summary>
    public class SupportTicket : SyncedRecord { }

    /// <summary>A Markdown note of the vault of <c>/obs</c> (index <c>vaultnote</c>), its id its path in the vault.</summary>
    public class VaultNote : SyncedRecord { }

    /// <summary>The analyses of an entity by <c>/entity:</c> (index <c>entityanalysis</c>), day by day.</summary>
    public class EntityAnalysis : SyncedRecord { }
}
