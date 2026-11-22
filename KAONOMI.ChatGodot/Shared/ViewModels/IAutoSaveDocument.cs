using System.Windows.Input;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// A window whose document is saved by itself: a few seconds after the last change (<see cref="IUnsavedChanges.IsDirty"/>
/// true), the desktop runs <see cref="AutoSaveCommand"/> when it can run, and the server keeps that state as a version
/// (see <see cref="IVersionedDocument"/>). Each action that changes the document is so saved and can be traced.
/// </summary>
public interface IAutoSaveDocument : IUnsavedChanges, System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>What saves the document (the SAVE of the window).</summary>
    ICommand? AutoSaveCommand { get; }

    /// <summary>
    /// The part of the window whose changes are the edits of the document (the entry selected in a list), when it is
    /// not the window itself; its changes start the wait before the save too.
    /// </summary>
    System.ComponentModel.INotifyPropertyChanged? ChangeSource => null;
}
