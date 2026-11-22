using System.Text.RegularExpressions;
using Godot;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// What a viewer (<see cref="UserRole.Viewer"/>) cannot do in the windows: every button that saves, deletes or publishes
/// (its text, or the name of a tool of a ribbon, says SAVE, DELETE, REMOVE, PUBLISH, 💾 or 🗑 when it is shown) is dimmed and takes neither the click nor
/// the keyboard. The commands refused to a viewer are in <see cref="UserSession.CanRun"/>. A button named otherwise, a
/// text written after the button is shown, or a keyboard shortcut of a window is not stopped here.
/// </summary>
public static class AccessControl
{
    /// <summary>A meta on a button a viewer may press anyway (its own profile, /user).</summary>
    public const string AllowedMeta = "allowed_for_viewers";

    private static readonly Regex Changes = new(@"\b(SAVE|DELETE|REMOVE|PUBLISH)\b|💾|🗑", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool _hooked;

    /// <summary>
    /// Watches the buttons of every window (<see cref="SceneTree.NodeAdded"/>), once for the whole run of the chat:
    /// whoever is logged in when a button is shown decides (a viewer, then somebody else after /logout: the interface is
    /// built again for each of them). Nothing is done for the other levels.
    /// </summary>
    public static void Hook(SceneTree tree)
    {
        if (_hooked)
            return;
        _hooked = true;
        tree.NodeAdded += node =>
        {
            // The tools of a ribbon have no text of their own: their name is a label inside them.
            if (UserSession.CanChange || node is not Button button || button.HasMeta(AllowedMeta)
                || !Changes.IsMatch(button.Text) && !(button.FindChild("Label", true, false) is Label name && Changes.IsMatch(name.Text)))
                return;
            button.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.FocusMode = Control.FocusModeEnum.None;
            button.Shortcut = null;
            button.Modulate = button.Modulate with { A = 0.35f };
        };
    }
}
