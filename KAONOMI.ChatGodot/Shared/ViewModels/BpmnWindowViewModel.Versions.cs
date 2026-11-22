using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the diagram (🕘, bpmn or infra), and its save by itself after each change once it was saved a first time.</summary>
public partial class BpmnWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => Route;

    public string? VersionDocumentId => DiagramId;

    public ICommand? AutoSaveCommand => DiagramId is null ? null : SaveToServerCommand;

    public Task ReloadAfterRestoreAsync() => DiagramId is { } id ? OpenFromServerAsync(id) : Task.CompletedTask;
}
