using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the node selected (🕘); its edits are saved in its window (EntityView).</summary>
public partial class OntologyWindowViewModel : IVersionedDocument
{
    public string VersionIndex => "ontology";

    public string? VersionDocumentId => SelectedNode is { IsNew: false } node ? node.Id : null;

    public Task ReloadAfterRestoreAsync() => RefreshAsync(CancellationToken.None);
}
