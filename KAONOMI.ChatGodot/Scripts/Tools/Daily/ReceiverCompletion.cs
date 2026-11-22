using System;
using System.Collections.Generic;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Daily;

/// <summary>
/// The completion of the receivers of /msg: while a login or a name is typed in the field To, a list opens under it
/// with the users that match (<see cref="MessageWindowViewModel.SuggestTo"/>), each with its name, its login and its
/// level. ↑ and ↓ move in the list, Tab or Enter writes the login chosen in the field, followed by a comma for the next
/// receiver, Esc closes the list; a click on a line writes it too. Only the part of the field the cursor is in is
/// completed: the receivers already typed stay.
/// </summary>
public partial class ReceiverCompletion : PanelContainer
{
    private const float RowHeight = 26;

    private readonly LineEdit? _input;
    private readonly MessageWindowViewModel? _vm;
    private readonly VBoxContainer _rows = new();
    private IReadOnlyList<MailUser> _matches = [];
    private int _selected;

    // Godot builds its scripts with this constructor.
    public ReceiverCompletion()
    {
    }

    /// <summary>The completion of <paramref name="input"/>, the field of the receivers of the message <paramref name="vm"/> writes.</summary>
    public ReceiverCompletion(LineEdit input, MessageWindowViewModel vm)
    {
        _input = input;
        _vm = vm;
        // Over everything, wherever the window is clipped.
        TopLevel = true;
        ZIndex = 80;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", Eve.Eve.Box(Eve.Eve.PanelSolid, Eve.Eve.EdgeBright with { A = 0.7f }, 8, 6));
        _rows.AddThemeConstantOverride("separation", 0);
        AddChild(_rows);
        input.TextChanged += _ => Refresh();
        input.GuiInput += OnKey;
        input.FocusExited += () => Callable.From(HideUnlessHovered).CallDeferred();
        input.Resized += Place;
        KeyTap.For(this, OnTab);
    }

    /// <summary>The list is open.</summary>
    public bool IsOpen => Visible && _matches.Count > 0;

    private void HideUnlessHovered()
    {
        // The field left with the interface (/logout, a new theme): there is no list any more.
        if (!IsInstanceValid(this) || !IsInsideTree())
            return;
        // A click on a line takes the focus of the field for an instant: the list stays for it.
        if (!GetGlobalRect().HasPoint(GetGlobalMousePosition()))
            Close();
    }

    public void Close()
    {
        Visible = false;
        _matches = [];
    }

    /// <summary>What is typed changed: the list follows.</summary>
    public void Refresh()
    {
        if (_input is null || _vm is null)
            return;
        _matches = _vm.SuggestTo(_input.Text, _input.CaretColumn);
        if (_matches.Count == 0)
        {
            Close();
            return;
        }
        _selected = 0;
        Visible = true;
        Build();
    }

    /// <summary>The keys of the list, before the field has them.</summary>
    private void OnKey(InputEvent e)
    {
        if (_input is null || !IsOpen || e is not InputEventKey { Pressed: true } key)
            return;
        switch (key.Keycode)
        {
            case Key.Up:
                Move(-1);
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Enter or Key.KpEnter:
                Choose(_matches[_selected]);
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }
        _input.AcceptEvent();
    }

    /// <summary>
    /// Tab with the list open writes the user chosen. It is taken in the main window (<see cref="KeyTap"/>): a window
    /// of the desk never sees Tab, the main window moves the keyboard to its next field with it.
    /// </summary>
    private bool OnTab(InputEventKey key)
    {
        if (_input is null || !IsInstanceValid(_input) || !IsOpen || !_input.HasFocus()
            || key is not { Keycode: Key.Tab, ShiftPressed: false, CtrlPressed: false, AltPressed: false, MetaPressed: false })
            return false;
        Choose(_matches[_selected]);
        return true;
    }

    /// <summary>Moves in the list (it turns around at its ends).</summary>
    private void Move(int by)
    {
        _selected = ((_selected + by) % _matches.Count + _matches.Count) % _matches.Count;
        Build();
    }

    /// <summary>The login of <paramref name="user"/> written in place of what was typed, a comma after it.</summary>
    private void Choose(MailUser user)
    {
        if (_input is null || _vm is null)
            return;
        var (text, caret) = MessageWindowViewModel.CompleteTo(_input.Text, _input.CaretColumn, user.Login);
        Close();
        _vm.ComposeTo = text;
        if (_input.Text != text)
            _input.Text = text;
        _input.GrabFocus();
        _input.CaretColumn = caret;
    }

    private void Build()
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        for (var i = 0; i < _matches.Count; i++)
            _rows.AddChild(Row(_matches[i], i == _selected));
        _rows.AddChild(Eve.Eve.Caption("↑ ↓: choose  ·  Tab or Enter: write  ·  Esc: close", Eve.Eve.Muted, 10));
        Place();
    }

    private Control Row(MailUser user, bool selected)
    {
        var row = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        row.AddThemeStyleboxOverride("panel", Eve.Eve.Box(selected ? Eve.Eve.Cyan with { A = 0.22f } : Colors.Transparent, Colors.Transparent, 5, 0));
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        var name = Eve.Eve.Text_(user.Name + (user.IsMe ? " (me)" : string.Empty), selected ? Eve.Eve.Ink : Eve.Eve.TextBright, 13);
        name.CustomMinimumSize = new Vector2(170, 0);
        name.ClipText = true;
        var login = Eve.Eve.Text_(user.Login, Eve.Eve.Text, 12);
        login.AddThemeFontOverride("font", Eve.Eve.Mono);
        login.CustomMinimumSize = new Vector2(110, 0);
        login.ClipText = true;
        var what = Eve.Eve.Text_(user.Email.Length > 0 ? $"{user.Role} · {user.Email}" : user.Role, Eve.Eve.Muted, 11);
        what.ClipText = true;
        what.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var part in new Control[] { name, login, what })
        {
            part.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(part);
        }
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddChild(line);
        row.AddChild(margin);
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                row.AcceptEvent();
                Choose(user);
            }
        };
        return row;
    }

    /// <summary>Under the field, as wide as it (wide enough for a name, a login and a level; not wider than they need).</summary>
    private void Place()
    {
        if (_input is null || !IsInstanceValid(_input) || !Visible)
            return;
        var box = _input.GetGlobalRect();
        ResetSize();
        Size = new Vector2(Math.Clamp(box.Size.X, 460, 620), GetCombinedMinimumSize().Y);
        GlobalPosition = new Vector2(box.Position.X, box.End.Y + 3);
    }
}
