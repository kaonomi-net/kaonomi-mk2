using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the page (🕘), and its save by itself after each change once it was saved a first time.</summary>
public sealed partial class WikiWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "wiki";

    public string? VersionDocumentId => PageId;

    public ICommand? AutoSaveCommand => PageId is null ? null : SaveCommand;

    public Task ReloadAfterRestoreAsync() => PageId is { } id ? OpenPageAsync(id, remember: false) : Task.CompletedTask;
}
