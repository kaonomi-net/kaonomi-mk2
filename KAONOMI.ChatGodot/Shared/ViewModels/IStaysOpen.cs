namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// A window that works without being touched (a camera that watches and records): while <see cref="StaysOpen"/> is
/// true, the desktop does not close it for want of use (<c>windowIdleCloseSeconds</c>).
/// </summary>
public interface IStaysOpen
{
    /// <summary>True while the window works by itself.</summary>
    bool StaysOpen { get; }
}
