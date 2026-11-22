using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the writing (🕘); it already saves itself (every minute, and its drafts).</summary>
public sealed partial class GhostWriterWindowViewModel : IVersionedDocument
{
    public string VersionIndex => "ghostwriter";

    public string? VersionDocumentId => WritingId;

    public Task ReloadAfterRestoreAsync() => WritingId is { } id ? OpenFromServerAsync(id) : Task.CompletedTask;
}
