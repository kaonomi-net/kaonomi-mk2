using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The versions of the collection plan shown (🕘), and its save by itself after each change once it was saved a first time
/// (its changes are the ones of the entry selected).
/// </summary>
public partial class CollectionPlanWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "collectionplan";

    public string? VersionDocumentId => SelectedResult?.Id;

    public bool IsDirty => SelectedResult?.IsDirty == true;

    public INotifyPropertyChanged? ChangeSource => SelectedResult;

    public ICommand? AutoSaveCommand => SelectedResult is { IsNew: false } entry ? entry.SaveCommand : null;

    /// <summary>The list read again, and the same document selected in it.</summary>
    public async Task ReloadAfterRestoreAsync()
    {
        var id = SelectedResult?.Id;
        await SearchAsync(CancellationToken.None);
        if (id is not null && Results.FirstOrDefault(r => r.Id == id) is { } again)
            SelectedResult = again;
    }
}
