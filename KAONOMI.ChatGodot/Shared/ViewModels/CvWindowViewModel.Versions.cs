using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the CV (🕘), and its save by itself (quietly) after each change once it was saved a first time.</summary>
public sealed partial class CvWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "cv";

    public string? VersionDocumentId => IsNew ? null : CvId;

    public ICommand? AutoSaveCommand => IsNew ? null : AutoSaveQuietCommand;

    /// <summary>The save of the CV without its messages (the save by itself).</summary>
    [RelayCommand]
    private Task AutoSaveQuietAsync() => SaveEntryAsync(Snapshot(), quiet: true);

    public Task ReloadAfterRestoreAsync() => IsNew ? Task.CompletedTask : OpenCvAsync(CvId);
}
