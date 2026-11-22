using System.ComponentModel;
using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// Saves the document of a window by itself (<see cref="IAutoSaveDocument"/>): <see cref="Delay"/> seconds after the
/// last change, when it has changes not saved and its save can run, the save runs; a new change pushes it back. The
/// window says nothing more than its own save says; the journal of the chat notes each save made so.
/// </summary>
public sealed partial class AutoSave : Node
{
    /// <summary>The seconds of quiet after a change before the save.</summary>
    public const double Delay = 3;

    private readonly IAutoSaveDocument _document;
    private readonly string _window;
    private double _left = -1;
    private INotifyPropertyChanged? _watched;

    private AutoSave(IAutoSaveDocument document, string window)
    {
        _document = document;
        _window = window;
    }

    /// <summary>Saves the document of <paramref name="document"/> by itself while <paramref name="owner"/> is in the tree.</summary>
    public static void Attach(Node owner, IAutoSaveDocument document, string window)
    {
        var saver = new AutoSave(document, window);
        owner.AddChild(saver);
    }

    public override void _EnterTree()
    {
        _document.PropertyChanged += OnChanged;
        Watch();
    }

    public override void _ExitTree()
    {
        _document.PropertyChanged -= OnChanged;
        if (_watched is not null)
            _watched.PropertyChanged -= OnChanged;
        _watched = null;
    }

    /// <summary>Follows the part whose changes are the edits (the entry selected), when it changed.</summary>
    private void Watch()
    {
        var source = _document.ChangeSource;
        if (ReferenceEquals(source, _watched) || ReferenceEquals(source, _document))
            return;
        if (_watched is not null)
            _watched.PropertyChanged -= OnChanged;
        _watched = source;
        if (_watched is not null)
            _watched.PropertyChanged += OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _document))
            Watch();
        // Any change of the document (its fields, or the flag of the changes): the save waits for the quiet again.
        if (_document.IsDirty)
            _left = Delay;
    }

    public override void _Process(double delta)
    {
        if (_left < 0)
            return;
        _left -= delta;
        if (_left > 0)
            return;
        _left = -1;
        if (!_document.IsDirty || _document.AutoSaveCommand is not { } save || !save.CanExecute(null))
            return;
        save.Execute(null);
        ActivityLog.Info("AutoSave", $"{_window}: saved by itself after a change");
    }
}
