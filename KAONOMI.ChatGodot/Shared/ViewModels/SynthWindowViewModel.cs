using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The FM synth (<c>/synth</c>): the keyboard of <c>/piano</c> (its octave, its sustain, the MIDI keyboard) playing a
/// sound of six operators (after the Yamaha DX7) whose every value can be changed. The sounds given with it, the
/// sounds saved (<c>~/kaonomi/synth/patches/*.json</c>, never erased: saved again under the same name, the old one is
/// kept aside); the sound being made kept in <c>~/kaonomi/synth/current.json</c>.
/// </summary>
public sealed partial class SynthWindowViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "synth");

    public static string PatchFolder => Path.Combine(Folder, "patches");

    private static string CurrentPath => Path.Combine(Folder, "current.json");

    /// <summary>The sound being played and changed (the same object all along: what is bound to it follows).</summary>
    public FmPatch Patch { get; } = FmPresets.Init();

    /// <summary>The state of the keyboard (octave, sustain, MIDI, pitch bend, modulation, volume).</summary>
    public PianoWindowViewModel Keys { get; } = new();

    /// <summary>The sound chosen in the list (a given one, or "💾 name" for a saved one).</summary>
    [ObservableProperty]
    private string _chosen = "";

    [ObservableProperty]
    private string _status = "";

    /// <summary>The sounds offered: those given, then those saved.</summary>
    public List<string> Sounds { get; } = [];

    public event Action? SoundsChanged;

    public SynthWindowViewModel()
    {
        try
        {
            if (File.Exists(CurrentPath) && JsonSerializer.Deserialize<FmPatch>(File.ReadAllText(CurrentPath)) is { Operators.Count: 6 } current)
                Patch.CopyFrom(current);
            else
                Patch.CopyFrom(FmPresets.All()[1]);
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Synth", "The current sound could not be read: " + ex.Message);
        }
        Refresh();
        _chosen = Sounds.FirstOrDefault(s => s == Patch.Name || s == "💾 " + Patch.Name) ?? "";
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Chosen) && !string.IsNullOrEmpty(Chosen))
                Load(Chosen);
        };
    }

    public void Refresh()
    {
        Sounds.Clear();
        Sounds.AddRange(FmPresets.All().Select(p => p.Name));
        if (Directory.Exists(PatchFolder))
            Sounds.AddRange(Directory.GetFiles(PatchFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().Select(n => "💾 " + n));
        SoundsChanged?.Invoke();
    }

    private void Load(string name)
    {
        try
        {
            if (name.StartsWith("💾 ", StringComparison.Ordinal))
            {
                var path = Path.Combine(PatchFolder, name[3..] + ".json");
                if (JsonSerializer.Deserialize<FmPatch>(File.ReadAllText(path)) is { Operators.Count: 6 } saved)
                    Patch.CopyFrom(saved);
            }
            else if (FmPresets.All().FirstOrDefault(p => p.Name == name) is { } given)
                Patch.CopyFrom(given);
            Status = "";
            SaveCurrent();
        }
        catch (Exception ex)
        {
            Status = $"{name} could not be read: {ex.Message}";
        }
    }

    public void Init()
    {
        Patch.CopyFrom(FmPresets.Init());
        SaveCurrent();
    }

    /// <summary>The sound saved under its name (the file of the same name kept aside first, dated).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(PatchFolder);
            var name = string.Concat(Patch.Name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            if (name.Length == 0)
                name = "SOUND";
            var path = Path.Combine(PatchFolder, name + ".json");
            if (File.Exists(path))
            {
                var history = Path.Combine(PatchFolder, "history");
                Directory.CreateDirectory(history);
                File.Copy(path, Path.Combine(history, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(Patch, Json));
            Refresh();
            Chosen = "💾 " + name;
            Status = "Saved: " + path;
        }
        catch (Exception ex)
        {
            Status = "The sound could not be saved: " + ex.Message;
        }
    }

    public void SaveCurrent()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(CurrentPath, JsonSerializer.Serialize(Patch, Json));
        }
        catch (Exception ex)
        {
            ActivityLog.Info("Synth", "The current sound could not be saved: " + ex.Message);
        }
    }
}
