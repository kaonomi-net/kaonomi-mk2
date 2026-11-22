using System;
using Godot;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// A node of the main window that is shown the keys pressed before anything else has them. The windows of the desk
/// draw in a viewport of their own, and receive a key only once the main window did nothing of it: Tab never reaches
/// them (the main window moves the keyboard to its next field with it). What needs such a key in a window (the
/// completion of a field) puts a tap in the main window (<see cref="For"/>): it takes the key, or leaves it.
/// </summary>
public partial class KeyTap : Node
{
    private readonly Func<InputEventKey, bool>? _take;

    // Godot builds its scripts with this constructor.
    public KeyTap()
    {
    }

    private KeyTap(Func<InputEventKey, bool> take) => _take = take;

    /// <summary>
    /// A tap that lives as long as <paramref name="owner"/> is in the tree: <paramref name="take"/> is shown each key
    /// pressed in the application, first; true when it did something of it (nothing else then has the key).
    /// </summary>
    public static void For(Node owner, Func<InputEventKey, bool> take)
    {
        KeyTap? tap = null;
        void Enter()
        {
            if (tap is not null)
                return;
            tap = new KeyTap(take);
            owner.GetTree().Root.CallDeferred(Node.MethodName.AddChild, tap);
        }
        owner.TreeEntered += Enter;
        owner.TreeExiting += () =>
        {
            if (tap is not null && IsInstanceValid(tap))
                tap.QueueFree();
            tap = null;
        };
        if (owner.IsInsideTree())
            Enter();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } key && _take?.Invoke(key) == true)
            GetViewport().SetInputAsHandled();
    }
}
