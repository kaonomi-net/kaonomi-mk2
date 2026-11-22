using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the analysis of the picture (🕘): every state saved; SAVE keeps a new one.</summary>
public partial class ImageAnalysisViewModel : IVersionedDocument
{
    public string VersionIndex => "imageanalysis";

    public string? VersionDocumentId => _entry.Id ?? ImageAnalysisEntry.IdFor(ImagePath);

    public Task ReloadAfterRestoreAsync() => LoadAsync();
}
