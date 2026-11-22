using System;
using System.Runtime.InteropServices;
using Godot;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The switch to another application turned off while the voice mode is locked (<see cref="KioskLock"/>), on Windows
/// and Linux (macOS turns Cmd+Tab off with the options of presentation of the application, in <see cref="KioskLock"/>).
/// <list type="bullet">
/// <item>Windows: a low-level hook of the keyboard drops Alt+Tab, Alt+Esc, Ctrl+Esc and the Windows keys (the Start
/// menu, Win+Tab, Win+D…) while a window of the chat is in front. Ctrl+Alt+Del cannot be caught (Windows keeps it) and
/// Ctrl+Shift+Esc (the Task Manager) is left, as the way out of last resort, like Cmd+Option+Esc on macOS.</item>
/// <item>Linux under X11: the window of the chat grabs the keyboard (<c>XGrabKeyboard</c>), so the window manager no
/// longer gets Alt+Tab, Super nor its other shortcuts; the grab is taken again every second (a window that was mapped
/// again loses it). Under Wayland the compositor keeps its shortcuts: Alt+Tab cannot be turned off there.</item>
/// </list>
/// </summary>
public static class AppSwitchBlock
{
    private static bool _on;

    /// <summary>Turns the switch to another application off (nothing when it is already off).</summary>
    public static void Start()
    {
        if (_on)
            return;
        _on = true;
        try
        {
            if (OperatingSystem.IsWindows())
                StartWindows();
            else if (OperatingSystem.IsLinux())
                StartLinux();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            ActivityLog.Error("Kiosk", "Alt+Tab cannot be turned off", ex);
        }
    }

    /// <summary>The switch to another application back on.</summary>
    public static void Stop()
    {
        if (!_on)
            return;
        _on = false;
        try
        {
            if (OperatingSystem.IsWindows())
                StopWindows();
            else if (OperatingSystem.IsLinux())
                StopLinux();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            ActivityLog.Error("Kiosk", "Alt+Tab cannot be turned back on", ex);
        }
    }

    // ── Windows ──

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    private const int KeyboardLowLevel = 13;   // WH_KEYBOARD_LL
    private const int AltDown = 0x20;          // LLKHF_ALTDOWN
    private const int VkTab = 0x09, VkEscape = 0x1B, VkLeftWindows = 0x5B, VkRightWindows = 0x5C, VkControl = 0x11, VkShift = 0x10;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int hook, LowLevelKeyboardProc procedure, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);

    /// <summary>Kept in a field: the hook calls it from native code, the collector must not free it.</summary>
    private static LowLevelKeyboardProc? _procedure;
    private static IntPtr _hook;

    /// <summary>
    /// The hook, set from the main thread of Godot: Windows calls it in the loop of messages of that thread, which Godot
    /// runs every frame.
    /// </summary>
    private static void StartWindows()
    {
        _procedure ??= OnKey;
        _hook = SetWindowsHookExW(KeyboardLowLevel, _procedure, GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero)
            ActivityLog.Info("Kiosk", $"Alt+Tab cannot be turned off: SetWindowsHookEx failed ({Marshal.GetLastWin32Error()})");
        else
            ActivityLog.Info("Kiosk", "Alt+Tab, Alt+Esc, Ctrl+Esc and the Windows keys are off until the voice mode is left");
    }

    private static void StopWindows()
    {
        if (_hook == IntPtr.Zero)
            return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        ActivityLog.Info("Kiosk", "Alt+Tab is back");
    }

    /// <summary>A key of the whole system: dropped when it switches to another application and the chat is in front.</summary>
    private static IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _on && ChatInFront())
        {
            // KBDLLHOOKSTRUCT: vkCode, scanCode, flags, time, dwExtraInfo.
            var key = Marshal.ReadInt32(data);
            var flags = Marshal.ReadInt32(data, 8);
            var alt = (flags & AltDown) != 0;
            var control = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
            var shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
            if ((key == VkTab && alt)
                || (key == VkEscape && (alt || (control && !shift)))
                || key is VkLeftWindows or VkRightWindows)
                return 1;
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary>True when the window in front is one of this process (the hook leaves the keys of the others alone).</summary>
    private static bool ChatInFront()
    {
        var window = GetForegroundWindow();
        return window != IntPtr.Zero && GetWindowThreadProcessId(window, out var process) != 0 && process == (uint)System.Environment.ProcessId;
    }

    // ── Linux (X11) ──

    private const int GrabModeAsync = 1, GrabSuccess = 0;

    [DllImport("libX11.so.6")]
    private static extern int XGrabKeyboard(IntPtr display, IntPtr window, int ownerEvents, int pointerMode, int keyboardMode, IntPtr time);

    [DllImport("libX11.so.6")]
    private static extern int XUngrabKeyboard(IntPtr display, IntPtr time);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    private static IntPtr _display;
    private static bool _grabbed;
    private static ulong _nextGrab, _grabSince;
    private static bool _grabFailureLogged;

    private static void StartLinux()
    {
        if (DisplayServer.GetName() != "X11")
        {
            ActivityLog.Info("Kiosk", $"Alt+Tab cannot be turned off under {DisplayServer.GetName()}: the compositor keeps its shortcuts (X11 lets the chat grab the keyboard)");
            return;
        }
        _display = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.DisplayHandle);
        if (_display == IntPtr.Zero)
            return;
        _grabbed = _grabFailureLogged = false;
        _nextGrab = 0;
        _grabSince = Time.GetTicksMsec();
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame += KeepGrab;
    }

    /// <summary>
    /// Each frame: the grab asked until the window takes it (it cannot before it is shown again in full screen), then
    /// asked again every second.
    /// </summary>
    private static void KeepGrab()
    {
        if (!_on || _display == IntPtr.Zero)
        {
            ((SceneTree)Engine.GetMainLoop()).ProcessFrame -= KeepGrab;
            return;
        }
        var now = Time.GetTicksMsec();
        if (now < _nextGrab)
            return;
        var window = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        var result = window == IntPtr.Zero ? -1 : XGrabKeyboard(_display, window, 1, GrabModeAsync, GrabModeAsync, IntPtr.Zero);
        XFlush(_display);
        var grabbed = result == GrabSuccess;
        _nextGrab = now + (grabbed ? 1000UL : 100UL);
        if (grabbed && !_grabbed)
            ActivityLog.Info("Kiosk", "The keyboard is grabbed: Alt+Tab and the shortcuts of the window manager are off until the voice mode is left");
        else if (!grabbed && !_grabFailureLogged && now - _grabSince > 5000)
        {
            _grabFailureLogged = true;
            ActivityLog.Info("Kiosk", $"The keyboard cannot be grabbed (XGrabKeyboard: {result}): Alt+Tab stays on");
        }
        _grabbed = grabbed;
    }

    private static void StopLinux()
    {
        if (_display == IntPtr.Zero)
            return;
        ((SceneTree)Engine.GetMainLoop()).ProcessFrame -= KeepGrab;
        XUngrabKeyboard(_display, IntPtr.Zero);
        XFlush(_display);
        if (_grabbed)
            ActivityLog.Info("Kiosk", "The keyboard is released: Alt+Tab is back");
        _grabbed = false;
        _display = IntPtr.Zero;
    }
}
