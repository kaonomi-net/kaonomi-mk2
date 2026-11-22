using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the map (🕘), and its save by itself after each change once it was saved a first time.</summary>
public sealed partial class MindmapWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "mindmap";

    public string? VersionDocumentId => MapId;

    public ICommand? AutoSaveCommand => MapId is null ? null : SaveToServerCommand;

    public Task ReloadAfterRestoreAsync() => MapId is { } id ? OpenFromServerAsync(id) : Task.CompletedTask;
}
