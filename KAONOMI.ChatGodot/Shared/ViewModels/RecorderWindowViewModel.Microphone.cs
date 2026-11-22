using System;
using System.Collections.Generic;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The microphone of /recorder: which one (the devices of the system, the default one of the system first) and its gain
/// (-20 to +30 dB), seen on the VU meter. The microphone chosen is the one of the whole chat (as /audiosettings: the
/// live chat and /vision listen to it too), the gain too (<see cref="AudioDevices.InputGainDb"/>).
/// </summary>
public partial class RecorderWindowViewModel
{
    /// <summary>The first choice of the list: the default microphone of the system.</summary>
    public const string SystemMicrophone = "Default of the system";

    /// <summary>The microphones: the default of the system first, then the devices.</summary>
    public IReadOnlyList<string> Microphones
    {
        get
        {
            try
            {
                return [SystemMicrophone, .. AudioDevices.Inputs()];
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or PortAudioSharp.PortAudioException)
            {
                return [SystemMicrophone];
            }
        }
    }

    /// <summary>The microphone chosen (<see cref="SystemMicrophone"/>: the default of the system).</summary>
    public string Microphone
    {
        get => AudioDevices.Input ?? SystemMicrophone;
        set
        {
            var name = value == SystemMicrophone || string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(AudioDevices.Input, name, StringComparison.Ordinal))
                return;
            // The windows that listen (this one, the live chat…) open it: see OnInputChanged.
            AudioDevices.ChooseInput(name);
            OnPropertyChanged();
        }
    }

    /// <summary>The gain of the microphone, in dB.</summary>
    public double GainInputDb
    {
        get => AudioDevices.InputGainDb;
        set
        {
            if (Math.Abs(value - AudioDevices.InputGainDb) < 0.01)
                return;
            AudioDevices.InputGainDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GainInputLabel));
        }
    }

    public string GainInputLabel => $"{AudioDevices.InputGainDb:+0;-0;0} dB";

    private bool _followsInput;
    private int _voiceWindows;

    /// <summary>
    /// The window does not close for want of use while it records, nor while MY VOICE (which listens through it) is
    /// open: closed, the microphone would be closed under it.
    /// </summary>
    public bool StaysOpen => IsRecording || Volatile.Read(ref _voiceWindows) > 0;

    /// <summary>MY VOICE opens (true) or closes (false): see <see cref="StaysOpen"/>.</summary>
    public void VoiceWindow(bool open) => Interlocked.Add(ref _voiceWindows, open ? 1 : -1);

    /// <summary>Raised when the window closes (the microphone with it): MY VOICE closes too.</summary>
    public event Action? Disposed;

    /// <summary>The microphone chosen changed (here or in /audiosettings): the one listened to is reopened.</summary>
    private void OnInputChanged()
    {
        if (_disposed)
            return;
        OnPropertyChanged(nameof(Microphone));
        try
        {
            if (Recorder.IsListening && Recorder.Restart())
                Report($"Listening to {Recorder.SourceName} ({Recorder.Rate} Hz)", false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PortAudioSharp.PortAudioException or DllNotFoundException or TypeInitializationException)
        {
            Report($"The microphone could not be opened: {ex.Message}", true);
            ActivityLog.Error("Recorder", "The microphone could not be opened", ex);
        }
    }

    /// <summary>Follows the microphone chosen (once, from Listen); left in Dispose.</summary>
    private void FollowInput()
    {
        if (_followsInput)
            return;
        _followsInput = true;
        AudioDevices.InputChanged += OnInputChanged;
    }
}
