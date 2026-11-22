using System.ComponentModel;
using System.Threading.Tasks;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// Implemented by the view model of every window that saves a document in the search engine: the server keeps each
/// state saved as a version (index <c>documentversion</c>), and the header of the window shows 🕘 HISTORY, the
/// versions of the document shown, any of them to restore.
/// <para>
/// The view model raises <see cref="INotifyPropertyChanged.PropertyChanged"/> for <see cref="VersionDocumentId"/>
/// when another document is shown, so the button follows.
/// </para>
/// </summary>
public interface IVersionedDocument : INotifyPropertyChanged
{
    /// <summary>The index of the document in the search engine (<c>idea</c>, <c>wiki</c>…).</summary>
    string VersionIndex { get; }

    /// <summary>The id of the document shown; null when there is none, or it was never saved.</summary>
    string? VersionDocumentId { get; }

    /// <summary>Shows the document again from the server, after one of its versions was restored.</summary>
    Task ReloadAfterRestoreAsync();
}
