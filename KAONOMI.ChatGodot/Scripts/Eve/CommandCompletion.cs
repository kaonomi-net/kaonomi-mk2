using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The completion of the slash commands of the chat: while a command is typed in the message box (a line starting
/// with "/", in one word), a list opens over the box with the commands that match, each with its icon and what it
/// does: the ones starting with what is typed first, then the ones holding it. ↑ and ↓ move in the list, Tab writes
/// the command chosen in the box, Enter writes it and runs it, Esc closes the list; a click on a line runs it too.
/// The commands and what they do come from the text of /help (<see cref="MainViewModel.Commands"/>).
/// </summary>
public partial class CommandCompletion : PanelContainer
{
    /// <summary>The most lines shown at once.</summary>
    private const int Most = 9;

    private const float RowHeight = 26;

    private readonly TextEdit? _input;
    private readonly Action<string, bool>? _choose;
    private readonly VBoxContainer _rows = new();
    private readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private List<ChatCommandInfo> _matches = [];
    private int _selected;
    private string _typed = string.Empty;

    // Godot builds its scripts with this constructor.
    public CommandCompletion()
    {
    }

    /// <summary>
    /// The completion of <paramref name="input"/>; <paramref name="choose"/> is called with the command chosen and
    /// whether it is to be run at once (Enter, a click) or only written in the box (Tab).
    /// </summary>
    public CommandCompletion(TextEdit input, Action<string, bool> choose)
    {
        _input = input;
        _choose = choose;
        foreach (var tool in Neocom.Tools)
            _icons[tool.Command] = tool.Icon;
        // Over everything, wherever the chat is clipped.
        TopLevel = true;
        ZIndex = 80;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        var box = Eve.Box(Eve.PanelSolid, Eve.EdgeBright with { A = 0.7f }, 8, 6);
        AddThemeStyleboxOverride("panel", box);
        _rows.AddThemeConstantOverride("separation", 0);
        AddChild(_rows);
        input.TextChanged += Refresh;
        input.FocusExited += () => Callable.From(HideUnlessHovered).CallDeferred();
        input.Resized += Place;
    }

    /// <summary>The list is open.</summary>
    public bool IsOpen => Visible && _matches.Count > 0;

    /// <summary>The command chosen in the list, when it is open.</summary>
    public string? Chosen => IsOpen ? _matches[_selected].Command : null;

    /// <summary>What was typed is exactly the command chosen: Enter sends it as it is.</summary>
    public bool IsExact => IsOpen && string.Equals(_matches[_selected].Command, _typed, StringComparison.OrdinalIgnoreCase);

    private void HideUnlessHovered()
    {
        // The box left with the interface (/logout, a new theme): there is no list any more.
        if (!IsInstanceValid(this) || !IsInsideTree())
            return;
        // A click on a line takes the focus of the box for an instant: the list stays for it.
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
        if (_input is null)
            return;
        var text = _input.Text;
        // A command: one line, starting with a slash, in one word.
        if (text.Length == 0 || text[0] != '/' || text.Any(char.IsWhiteSpace))
        {
            Close();
            return;
        }
        _typed = text;
        var commands = MainViewModel.Commands;
        var starting = commands.Where(c => c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase));
        var holding = text.Length > 1
            ? commands.Where(c => !c.Command.StartsWith(text, StringComparison.OrdinalIgnoreCase)
                                  && (c.Command.Contains(text[1..], StringComparison.OrdinalIgnoreCase) || c.Description.Contains(text[1..], StringComparison.OrdinalIgnoreCase)))
            : [];
        // The ones starting with what is typed by their name; the others after them.
        _matches = [.. starting.OrderBy(c => c.Command, StringComparer.OrdinalIgnoreCase), .. holding];
        if (_matches.Count == 0)
        {
            Close();
            return;
        }
        _selected = 0;
        Visible = true;
        Build();
    }

    /// <summary>Moves in the list (it turns around at its ends).</summary>
    public void Move(int by)
    {
        if (!IsOpen)
            return;
        _selected = ((_selected + by) % _matches.Count + _matches.Count) % _matches.Count;
        Build();
    }

    /// <summary>The lines of the list: the ones around the one chosen when there are more than <see cref="Most"/>.</summary>
    private void Build()
    {
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        var first = Math.Clamp(_selected - Most / 2, 0, Math.Max(0, _matches.Count - Most));
        var last = Math.Min(_matches.Count, first + Most);
        for (var i = first; i < last; i++)
            _rows.AddChild(Row(_matches[i], i == _selected));
        if (_matches.Count > Most)
        {
            var more = Eve.Caption($"{_selected + 1} / {_matches.Count}  ·  ↑ ↓", Eve.Muted, 10);
            more.HorizontalAlignment = HorizontalAlignment.Right;
            _rows.AddChild(more);
        }
        var hint = Eve.Caption("Tab: write  ·  Enter: run  ·  Esc: close", Eve.Muted, 10);
        _rows.AddChild(hint);
        Place();
    }

    private Control Row(ChatCommandInfo command, bool selected)
    {
        var row = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        row.AddThemeStyleboxOverride("panel", Eve.Box(selected ? Eve.Cyan with { A = 0.22f } : Colors.Transparent, Colors.Transparent, 5, 0));
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 8);
        var icon = Eve.Text_(_icons.GetValueOrDefault(command.Command, "›"), Eve.TextBright, 13);
        icon.CustomMinimumSize = new Vector2(22, 0);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var name = Eve.Text_(command.Command, selected ? Eve.Ink : Eve.TextBright, 13);
        name.AddThemeFontOverride("font", Eve.Mono);
        name.CustomMinimumSize = new Vector2(150, 0);
        var what = Eve.Text_(command.Description, Eve.Muted, 11);
        what.ClipText = true;
        what.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (var part in new Control[] { icon, name, what })
        {
            part.MouseFilter = MouseFilterEnum.Ignore;
            line.AddChild(part);
        }
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddChild(line);
        row.AddChild(margin);
        row.TooltipText = command.Description;
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                row.AcceptEvent();
                _choose?.Invoke(command.Command, true);
            }
        };
        return row;
    }

    /// <summary>Over the message box, as wide as it.</summary>
    private void Place()
    {
        if (_input is null || !IsInstanceValid(_input) || !Visible)
            return;
        var box = _input.GetGlobalRect();
        ResetSize();
        var height = GetCombinedMinimumSize().Y;
        Size = new Vector2(Math.Max(box.Size.X, 320), height);
        GlobalPosition = new Vector2(box.Position.X, Math.Max(4, box.Position.Y - height - 6));
    }
}
