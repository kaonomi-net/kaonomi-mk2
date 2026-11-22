using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>The versions of the video edited (🕘): every state saved.</summary>
public partial class YoutubeWindowViewModel : IVersionedDocument
{
    public string VersionIndex => "youtubevideo";

    public string? VersionDocumentId => EditingId;

    /// <summary>The list read again, and the same video selected in it.</summary>
    public async Task ReloadAfterRestoreAsync()
    {
        var id = EditingId;
        await SearchAsync(CancellationToken.None);
        if (id is not null && Videos.FirstOrDefault(v => v.Entry.Id == id) is { } again)
            SelectedVideo = again;
    }
}
