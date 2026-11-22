using System.Threading.Tasks;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// A Short being filmed by <c>/remote</c>: the 3D animation of the chat in 9:16 with its voice. Made by the front end
/// that draws the animation (the Godot chat); <see cref="StopAsync"/> ends it and writes the MP4.
/// </summary>
public interface IShortRecording
{
    /// <summary>Ends the filming and writes the MP4: its path, or null when it could not be written (the log says why).</summary>
    Task<string?> StopAsync();
}
