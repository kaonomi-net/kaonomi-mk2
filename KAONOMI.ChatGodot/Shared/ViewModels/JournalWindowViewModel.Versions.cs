using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the entry (🕘), and its save by itself after each change once it was saved a first time.</summary>
public partial class JournalWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "diary";

    public string? VersionDocumentId => EntryId;

    public ICommand? AutoSaveCommand => EntryId is null ? null : SaveCommand;

    /// <summary>The list of the entries read again, and the entry shown opened again from it.</summary>
    public async Task ReloadAfterRestoreAsync()
    {
        if (EntryId is not { } id)
            return;
        await SearchHistoryAsync(CancellationToken.None);
        if (History.FirstOrDefault(h => h.Entry.Id == id) is { } item)
            await OpenEntryAsync(item);
    }
}
