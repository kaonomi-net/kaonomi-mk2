using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KAONOMI.Chat.Services;

/// <summary>
/// What the user copied in a browser, with its HTML (the titles, the paragraphs and the links of the page), read from
/// the clipboard of the system: PASTE PAGE of the collection plan window keeps it as a collected page when the site
/// refused the web scraper. On macOS the HTML flavour is asked to AppleScript, on Windows to PowerShell, on Linux to
/// wl-paste or xclip; null when the clipboard holds no HTML (the plain text is used then).
/// </summary>
public static class ClipboardPage
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>The HTML of the clipboard, or null when it holds none (or it could not be read).</summary>
    public static async Task<string?> ReadHtmlAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return DecodeAppleScriptData(await RunAsync("/usr/bin/osascript", ["-e", "the clipboard as «class HTML»"], cancellationToken));
            if (OperatingSystem.IsWindows())
                return StripWindowsHeader(await RunAsync("powershell", ["-NoProfile", "-Command",
                    "[Console]::OutputEncoding = [Text.Encoding]::UTF8; Get-Clipboard -TextFormatType Html -Raw"], cancellationToken));
            return NullIfEmpty(await RunAsync("wl-paste", ["--type", "text/html"], cancellationToken))
                   ?? NullIfEmpty(await RunAsync("xclip", ["-selection", "clipboard", "-t", "text/html", "-o"], cancellationToken));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException or System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The text AppleScript writes for a piece of data, <c>«data HTML3C703E…»</c> (the bytes in hexadecimal), as the
    /// HTML it is (UTF-8); null for anything else.
    /// </summary>
    public static string? DecodeAppleScriptData(string? output)
    {
        var text = (output ?? string.Empty).Trim();
        const string head = "«data HTML";
        if (!text.StartsWith(head, StringComparison.Ordinal) || !text.EndsWith('»'))
            return null;
        var hex = text.AsSpan(head.Length, text.Length - head.Length - 1);
        if (hex.Length == 0 || hex.Length % 2 != 0)
            return null;
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            if (!byte.TryParse(hex.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return null;
        return NullIfEmpty(Encoding.UTF8.GetString(bytes));
    }

    /// <summary>The HTML of the Windows clipboard format, without its header (Version, StartHTML…).</summary>
    public static string? StripWindowsHeader(string? output)
    {
        var text = output ?? string.Empty;
        var start = text.IndexOf('<');
        return start < 0 ? null : NullIfEmpty(text[start..]);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static async Task<string?> RunAsync(string file, string[] arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info);
        if (process is null)
            return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            return process.ExitCode == 0 ? await output : null;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return null;
        }
    }
}
