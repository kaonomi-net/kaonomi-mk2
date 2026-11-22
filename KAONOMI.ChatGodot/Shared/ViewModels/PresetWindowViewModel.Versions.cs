using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the preset (🕘), and its save by itself after each change once it was saved a first time.</summary>
public partial class PresetWindowViewModel : IVersionedDocument, IAutoSaveDocument
{
    public string VersionIndex => "preset";

    public string? VersionDocumentId => Selected?.Id;

    public ICommand? AutoSaveCommand => Selected?.Id is null ? null : SaveCommand;

    public Task ReloadAfterRestoreAsync() => RefreshAsync();
}
