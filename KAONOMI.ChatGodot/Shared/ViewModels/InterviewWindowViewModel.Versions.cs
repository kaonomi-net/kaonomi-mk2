using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The versions of the interview (🕘), and its save by itself after each change once it was saved a first time:
/// quiet, and without the summary by the model (the SAVE of the window still makes it).
/// </summary>
public sealed partial class InterviewWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "interview";

    public string? VersionDocumentId => IsNew ? null : InterviewId;

    public ICommand? AutoSaveCommand => IsNew ? null : AutoSaveQuietCommand;

    [RelayCommand]
    private Task AutoSaveQuietAsync() => SaveCoreAsync(quiet: true, summarise: false);

    public Task ReloadAfterRestoreAsync() => IsNew ? Task.CompletedTask : OpenInterviewAsync(InterviewId);
}
