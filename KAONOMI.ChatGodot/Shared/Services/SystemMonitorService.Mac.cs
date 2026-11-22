using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The GPU of a Mac (Apple Silicon: M1, M2…, or the graphics card of an Intel Mac), read from IOKit: the driver of the
/// GPU (the class <c>IOAccelerator</c>) publishes its statistics in the property <c>PerformanceStatistics</c>, which the
/// Activity Monitor reads too: <c>Device Utilization %</c> (the whole GPU), <c>Renderer Utilization %</c> and
/// <c>Tiler Utilization %</c>, and <c>In use system memory</c> (the memory the GPU uses: on Apple Silicon, of the
/// memory shared by the processor and the GPU). Its model (<c>model</c>) and its cores (<c>gpu-core-count</c>) are
/// read once. No privilege is needed; nothing here is a private API.
/// </summary>
public sealed partial class SystemMonitorService
{
    /// <summary>What the GPU of the Mac did at the last sample, beyond its utilization: the renderer and the tiler (%).</summary>
    public sealed record MacGpuActivity(double Device, double? Renderer, double? Tiler, ulong? MemoryInUse);

    /// <summary>The GPUs of the Mac: model, cores, and whether its memory is the memory of the machine (Apple Silicon).</summary>
    [SupportedOSPlatform("macos")]
    private static IReadOnlyList<GpuAdapter> ReadMacGpus(ulong totalMemory)
    {
        var gpus = new List<GpuAdapter>();
        foreach (var service in MacAccelerators())
        {
            try
            {
                var name = MacStringProperty(service, "model") ?? "GPU";
                var cores = MacNumberProperty(service, "gpu-core-count");
                // Apple Silicon: one memory for the processor and the GPU (the GPU may use most of it).
                var unified = name.StartsWith("Apple", StringComparison.Ordinal);
                gpus.Add(new GpuAdapter(name.Trim(), unified ? totalMemory : 0)
                {
                    Cores = cores is > 0 ? (int)cores.Value : 0,
                    UnifiedMemory = unified
                });
            }
            finally
            {
                IOObjectRelease(service);
            }
        }
        return gpus;
    }

    /// <summary>
    /// The activity of the GPU of the Mac (the busiest when there are several), or null when the driver gives none.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static MacGpuActivity? ReadMacGpuActivity()
    {
        MacGpuActivity? busiest = null;
        foreach (var service in MacAccelerators())
        {
            try
            {
                var statistics = MacProperty(service, "PerformanceStatistics");
                if (statistics == IntPtr.Zero)
                    continue;
                try
                {
                    if (CFGetTypeID(statistics) != CFDictionaryGetTypeID())
                        continue;
                    var device = MacDictionaryNumber(statistics, "Device Utilization %") ?? MacDictionaryNumber(statistics, "GPU Activity(%)");
                    if (device is null)
                        continue;
                    var activity = new MacGpuActivity(Math.Clamp(device.Value, 0, 100),
                        MacDictionaryNumber(statistics, "Renderer Utilization %"),
                        MacDictionaryNumber(statistics, "Tiler Utilization %"),
                        MacDictionaryNumber(statistics, "In use system memory") is { } used and >= 0 ? (ulong)used : null);
                    if (busiest is null || activity.Device > busiest.Device)
                        busiest = activity;
                }
                finally
                {
                    CFRelease(statistics);
                }
            }
            finally
            {
                IOObjectRelease(service);
            }
        }
        return busiest;
    }

    /// <summary>The services of the class IOAccelerator (each one released by the caller).</summary>
    [SupportedOSPlatform("macos")]
    private static List<uint> MacAccelerators()
    {
        var services = new List<uint>();
        try
        {
            // IOServiceGetMatchingServices takes the dictionary over (it releases it).
            var matching = IOServiceMatching("IOAccelerator");
            if (matching == IntPtr.Zero || IOServiceGetMatchingServices(0, matching, out var iterator) != 0)
                return services;
            try
            {
                uint service;
                while ((service = IOIteratorNext(iterator)) != 0)
                    services.Add(service);
            }
            finally
            {
                IOObjectRelease(iterator);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not a Mac, or IOKit not reachable: no GPU.
        }
        return services;
    }

    /// <summary>A property of a service (to release), or zero.</summary>
    private static IntPtr MacProperty(uint service, string name)
    {
        var key = CFString(name);
        try
        {
            return IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0);
        }
        finally
        {
            CFRelease(key);
        }
    }

    private static string? MacStringProperty(uint service, string name)
    {
        var value = MacProperty(service, name);
        if (value == IntPtr.Zero)
            return null;
        try
        {
            if (CFGetTypeID(value) == CFStringGetTypeID())
            {
                var buffer = new byte[512];
                return CFStringGetCString(value, buffer, buffer.Length, Utf8) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length) : null;
            }
            if (CFGetTypeID(value) == CFDataGetTypeID())
            {
                var length = (int)CFDataGetLength(value);
                var bytes = new byte[length];
                Marshal.Copy(CFDataGetBytePtr(value), bytes, 0, length);
                return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            }
            return null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    private static long? MacNumberProperty(uint service, string name)
    {
        var value = MacProperty(service, name);
        if (value == IntPtr.Zero)
            return null;
        try
        {
            return CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, SInt64, out long number) ? number : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    /// <summary>The number of the key <paramref name="name"/> of a dictionary (not released: it belongs to the dictionary), or null.</summary>
    private static double? MacDictionaryNumber(IntPtr dictionary, string name)
    {
        var key = CFString(name);
        try
        {
            var value = CFDictionaryGetValue(dictionary, key);
            return value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, SInt64, out long number) ? number : null;
        }
        finally
        {
            CFRelease(key);
        }
    }

    private static IntPtr CFString(string text) => CFStringCreateWithCString(IntPtr.Zero, text, Utf8);

    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const int SInt64 = 4;

    [DllImport(IOKit)]
    private static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKit)]
    private static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [DllImport(IOKit)]
    private static extern uint IOIteratorNext(uint iterator);

    [DllImport(IOKit)]
    private static extern int IOObjectRelease(uint service);

    [DllImport(IOKit)]
    private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr text, byte[] buffer, long size, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, int type, out long value);

    [DllImport(CoreFoundation)]
    private static extern long CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern ulong CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern ulong CFDictionaryGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern ulong CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern ulong CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern ulong CFDataGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
}
