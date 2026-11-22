using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the entity (🕘), and its save by itself after each change once it was saved a first time.</summary>
public partial class EntityWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "ontology";

    public string? VersionDocumentId => Node is { IsNew: false } node ? node.Id : null;

    public bool IsDirty => Node?.IsDirty == true;

    public INotifyPropertyChanged? ChangeSource => Node;

    public ICommand? AutoSaveCommand => Node is { IsNew: false } node ? node.SaveCommand : null;

    public Task ReloadAfterRestoreAsync() => Node is { IsNew: false } node ? node.RefreshFromServerAsync() : Task.CompletedTask;
}
