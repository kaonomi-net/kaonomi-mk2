using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KAONOMI.Chat.Models;

/// <summary>
/// Voice and animation preset of the <c>/config</c> window: the French voice (<c>tts.voices.FR</c>), its pitch, speed
/// robotic effect, vibrato, assistant sheen and horror treatment (<c>tts.pitch</c>, <c>tts.speed</c>, <c>tts.robot</c>,
/// <c>tts.vibrato</c>, <c>tts.assistant</c>, <c>tts.horror</c>), the 3D animation (<c>animation</c>) and the personality
/// added to the system prompt (<c>persona</c>).
/// </summary>
public sealed record ChatPreset(
    string Name, string Description, string Voice, double Pitch, double Speed, double Robot, SceneAnimation Animation,
    double Vibrato = 0, double Assistant = 0, double Horror = 0, string Persona = "")
{
    /// <summary>Language whose voice a preset sets (the voices of the presets are French).</summary>
    public const string VoiceLanguage = "FR";

    /// <summary>
    /// The presets offered by the picker of the chat, the drop-down of <c>/config</c>, <c>/remote</c> and <c>/preset</c>:
    /// the default one(s) of config.yml (<see cref="BuiltIn"/>) first, then the ones stored in the search engine
    /// (<see cref="PresetStore"/>; until the server has answered, the ones of the application and of presets.json), one
    /// per name. A default preset also stored in the search engine is the stored one (edited with /preset: its system
    /// prompt, its voice…); config.yml gives it until the server has answered.
    /// </summary>
    public static IReadOnlyList<ChatPreset> All
    {
        get
        {
            var others = (PresetStore.Loaded ? PresetStore.Presets : [.. Defaults, .. CustomPresetStore.Presets])
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            bool IsBuiltIn(ChatPreset p) => BuiltIn.Any(b => string.Equals(b.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            var first = BuiltIn.Select(b => PresetStore.Loaded
                ? others.FirstOrDefault(p => string.Equals(p.Name, b.Name, StringComparison.OrdinalIgnoreCase)) ?? b
                : b);
            return [.. first, .. others.Where(p => !IsBuiltIn(p))];
        }
    }

    /// <summary>True for a preset stored in the search engine (it can be replaced or deleted), not for one of config.yml.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCustom => !CustomPresetStore.IsBuiltInName(Name)
        && (PresetStore.Loaded ? PresetStore.Presets : CustomPresetStore.Presets).Any(p => string.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The presets of the application: the ones of the <c>presets</c> section of config.yml (read with the settings,
    /// <see cref="Configure"/>), else <see cref="Defaults"/>.
    /// </summary>
    public static IReadOnlyList<ChatPreset> BuiltIn => _configured ?? Defaults;

    private static IReadOnlyList<ChatPreset>? _configured;

    /// <summary>The presets of the <c>presets</c> section of config.yml; null or empty keeps <see cref="Defaults"/>.</summary>
    public static void Configure(IReadOnlyList<ChatPreset>? presets) => _configured = presets is { Count: > 0 } ? presets : null;

    /// <summary>The presets used when config.yml has no <c>presets</c> section.</summary>
    public static IReadOnlyList<ChatPreset> Defaults { get; } =
    [
        new("MARI", "Female voice (siwis), pitch 1.3, MARI animation, no robotic effect",
            "fr_FR-siwis-medium", Pitch: 1.3, Speed: 1, Robot: 0, SceneAnimation.Mari,
            Persona: "Tu t'appelles Mari. Tu es gentille et tu t'intéresses à l'utilisateur."),
        // The calm, measured voice of the AI of the films: a slightly lower pitch, an unhurried speed, a faint metallic
        // touch and the assistant sheen (doubling, small-room reverb, presence) instead of the robotic buzz.
        new("JARVIS", "Male voice (tom), pitch 0.92, speed 1.05, faint metallic touch, AI assistant sheen, JARVIS animation",
            "fr_FR-tom-medium", Pitch: 0.92, Speed: 1.05, Robot: 0.12, SceneAnimation.Jarvis, Assistant: 1),
        new("HAL", "Male voice (tom), pitch 0.9, speed 1.3, robotic effect, HAL animation",
            "fr_FR-tom-medium", Pitch: 0.9, Speed: 1.3, Robot: 1, SceneAnimation.Hal),
        // A stressing horror-film doll: her child voice, a little slower, wavering, with a demonic shadow under it, breath,
        // stutters and a dark hall.
        new("MARIEKILLEUSE", "MARI's voice (siwis) 0.1 higher, pitch 1.4, slower, vibrato, horror-film treatment, MARIEKILLEUSE animation (robot doll with a knife)",
            "fr_FR-siwis-medium", Pitch: 1.4, Speed: 0.9, Robot: 0, SceneAnimation.MarieKilleuse, Vibrato: 1, Horror: 1,
            Persona: "Tu t'appelles Marie Killeuse. Tu es méchante et tu te moques de l'utilisateur."),
        new("MUSUKO", "Male voice (tom), pitch 1.3, MUSUKO animation (robot boy)",
            "fr_FR-tom-medium", Pitch: 1.3, Speed: 1, Robot: 0, SceneAnimation.Musuko,
            Persona: "Tu t'appelles 'Musuko'. Tu parles beaucoup, et à la fin de ta réponse tu poses toujours plusieurs questions à la fois."),
        new("KAONAMI", "Male voice (tom), pitch 0.75, KAONAMI animation (masked robot)",
            "fr_FR-tom-medium", Pitch: 0.75, Speed: 1, Robot: 0, SceneAnimation.Kaonami,
            Persona: "Tu t'appelles Sans 'Visage'. Tu parles peu, et tu réponds le plus rapidement possible avec le moins de détails, tant qu'on ne te demande pas d'en dire plus."),
        new("GREEN", "Male voice (tom), pitch 1.15, faint robotic touch, GREEN animation (android child with green eyes)",
            "fr_FR-tom-medium", Pitch: 1.15, Speed: 1, Robot: 0.2, SceneAnimation.Green),
        new("CIRCUIT", "Female voice (siwis), pitch 1, speed 1.1, faint assistant sheen, CIRCUIT animation (android woman with green circuits)",
            "fr_FR-siwis-medium", Pitch: 1, Speed: 1.1, Robot: 0, SceneAnimation.Circuit, Assistant: 0.3),
        new("MOTUKO", "Female voice (siwis), pitch 0.95, speed 1, faint robotic touch, MOTUKO animation (android woman in a white armour)",
            "fr_FR-siwis-medium", Pitch: 0.95, Speed: 1, Robot: 0.15, SceneAnimation.Motuko),
        new("KAONOMI", "Male voice (tom), pitch 0.8, speed 1, robotic effect, KAONOMI animation (hooded robot with one yellow eye)",
            "fr_FR-tom-medium", Pitch: 0.8, Speed: 1, Robot: 0.6, SceneAnimation.Kaonomi),
        new("RIVET", "Male voice (tom), pitch 1.1, speed 1.05, robotic effect, RIVET animation (black and white tin robot with goggle eyes)",
            "fr_FR-tom-medium", Pitch: 1.1, Speed: 1.05, Robot: 0.5, SceneAnimation.Rivet),
        new("SHIRO", "Male voice (tom), pitch 1.25, faint robotic touch, SHIRO animation (drawn android boy with green eyes)",
            "fr_FR-tom-medium", Pitch: 1.25, Speed: 1, Robot: 0.15, SceneAnimation.Shiro),
        new("KINGYO", "Female voice (siwis), pitch 1.35, speed 1.05, faint robotic touch, KINGYO animation (golden robot fish)",
            "fr_FR-siwis-medium", Pitch: 1.35, Speed: 1.05, Robot: 0.2, SceneAnimation.Kingyo),
        new("HIKARI", "Female voice (siwis), pitch 1.2, faint robotic touch, HIKARI animation (android girl with blond hair and green eyes)",
            "fr_FR-siwis-medium", Pitch: 1.2, Speed: 1, Robot: 0.1, SceneAnimation.Hikari),
        new("KAMEN", "Male voice (tom), pitch 0.7, slower, robotic touch, a little horror treatment, KAMEN animation (white mask with red marks)",
            "fr_FR-tom-medium", Pitch: 0.7, Speed: 0.95, Robot: 0.3, SceneAnimation.Kamen, Horror: 0.5),
        new("AOI", "Female voice (siwis), pitch 1.05, AI assistant sheen, AOI animation (head of glass with blue circuits)",
            "fr_FR-siwis-medium", Pitch: 1.05, Speed: 1, Robot: 0, SceneAnimation.Aoi, Assistant: 0.7),
        new("HAGANE", "Female voice (siwis), pitch 0.95, robotic touch, HAGANE animation (android woman with machinery around her head)",
            "fr_FR-siwis-medium", Pitch: 0.95, Speed: 1, Robot: 0.25, SceneAnimation.Hagane),
        new("SORA", "Female voice (siwis), pitch 1.15, faint robotic touch, SORA animation (android girl with short blond hair, teal eyes and an earpiece)",
            "fr_FR-siwis-medium", Pitch: 1.15, Speed: 1, Robot: 0.12, SceneAnimation.Sora),
        new("TAMA", "Male voice (tom), pitch 1.3, a little faster, robotic, TAMA animation (white robot with a round head and camera eyes)",
            "fr_FR-tom-medium", Pitch: 1.3, Speed: 1.05, Robot: 0.45, SceneAnimation.Tama),
        new("BENI", "Female voice (siwis), pitch 1.35, a little faster, robotic touch, BENI animation (small red and white robot with a screen for a face)",
            "fr_FR-siwis-medium", Pitch: 1.35, Speed: 1.08, Robot: 0.3, SceneAnimation.Beni)
    ];

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>
    /// <paramref name="yaml"/> (the text of config.yml) with the values of this preset: the lines of the keys are
    /// rewritten in place (comments and the other keys are kept), and the missing keys are added.
    /// </summary>
    public string ApplyTo(string yaml)
    {
        var newLine = yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        SetKey(lines, 0, lines.Count, "", "animation", Animation.ToString().ToUpperInvariant());
        SetKey(lines, 0, lines.Count, "", "persona", Quoted(Persona));

        var (ttsStart, ttsEnd) = Section(lines, 0, lines.Count, "", "tts");
        if (ttsStart < 0)
        {
            lines.Add("tts:");
            (ttsStart, ttsEnd) = (lines.Count - 1, lines.Count);
        }
        ttsEnd = SetKey(lines, ttsStart + 1, ttsEnd, "  ", "speed", Number(Speed));
        ttsEnd = SetKey(lines, ttsStart + 1, ttsEnd, "  ", "pitch", Number(Pitch));
        ttsEnd = SetKey(lines, ttsStart + 1, ttsEnd, "  ", "robot", Number(Robot));
        ttsEnd = SetKey(lines, ttsStart + 1, ttsEnd, "  ", "vibrato", Number(Vibrato));
        ttsEnd = SetKey(lines, ttsStart + 1, ttsEnd, "  ", "assistant", Number(Assistant));
        ttsEnd = SetKey(lines, ttsStart + 1, ttsEnd, "  ", "horror", Number(Horror));

        var (voicesStart, voicesEnd) = Section(lines, ttsStart + 1, ttsEnd, "  ", "voices");
        if (voicesStart < 0)
        {
            lines.Insert(ttsEnd, "  voices:");
            (voicesStart, voicesEnd) = (ttsEnd, ttsEnd + 1);
        }
        SetKey(lines, voicesStart + 1, voicesEnd, "    ", VoiceLanguage, Voice);

        return string.Join(newLine, lines);
    }

    /// <summary>A YAML double-quoted string (accents and apostrophes kept as they are).</summary>
    /// <summary>
    /// <paramref name="text"/> as a YAML string between double quotes, on one line: the line breaks of a persona of
    /// several lines are written \n (written as they are, they broke config.yml, which then could not be read at all:
    /// the chat started with its defaults, the models of LM Studio asked to an Ollama that is not there).
    /// </summary>
    private static string Quoted(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n").Replace("\t", "\\t") + "\"";

    private static string Number(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Sets <c>indent + key: value</c> on the line of that key between <paramref name="start"/> and
    /// <paramref name="end"/> (not on a comment), or inserts it after the last entry of the block; returns the new end.
    /// </summary>
    private static int SetKey(List<string> lines, int start, int end, string indent, string key, string value)
    {
        var pattern = new Regex($"^{Regex.Escape(indent)}{Regex.Escape(key)}\\s*:", RegexOptions.IgnoreCase);
        for (var i = start; i < end; i++)
            if (pattern.IsMatch(lines[i]))
            {
                // The old value goes whole, the lines it ran over too (else they would stay behind, orphans).
                var extra = Math.Min(ValueContinuation(lines, i, indent), end - i - 1);
                lines[i] = $"{indent}{key}: {value}";
                lines.RemoveRange(i + 1, extra);
                return end - extra;
            }

        // After the last line that is not blank, so that the blank lines before the next section stay where they are, nor
        // a comment less indented than the block (it belongs to the next section).
        var at = end;
        while (at > start && (string.IsNullOrWhiteSpace(lines[at - 1])
                              || lines[at - 1].TrimStart().StartsWith('#') && lines[at - 1].Length - lines[at - 1].TrimStart().Length < indent.Length))
            at--;
        lines.Insert(at, $"{indent}{key}: {value}");
        return end + 1;
    }

    /// <summary>
    /// How many lines after <paramref name="line"/> still belong to the value of its key: the rest of a string between
    /// quotes running over several lines (whatever their indentation, as the chat wrote them before line breaks were
    /// escaped), or the lines of a block (<c>|</c>, <c>&gt;</c>) or of a plain value, more indented than the key. A string
    /// never closed takes nothing away.
    /// </summary>
    private static int ValueContinuation(List<string> lines, int line, string indent)
    {
        var text = lines[line];
        var colon = text.IndexOf(':');
        var value = colon < 0 ? string.Empty : text[(colon + 1)..].TrimStart();
        if (value.Length > 0 && value[0] is '"' or '\'')
        {
            var quote = value[0];
            if (Closes(value[1..], quote))
                return 0;
            for (var next = line + 1; next < lines.Count; next++)
                if (Closes(lines[next], quote))
                    return next - line;
            return 0;
        }
        var count = 0;
        for (var next = line + 1; next < lines.Count; next++)
        {
            // Blank lines inside a block belong to it, the ones after it do not.
            if (string.IsNullOrWhiteSpace(lines[next]))
                continue;
            if (lines[next].Length - lines[next].TrimStart().Length <= indent.Length)
                break;
            count = next - line;
        }
        return count;
    }

    /// <summary>True when <paramref name="text"/> holds the quote that closes a string (not one escaped by \ or doubled).</summary>
    private static bool Closes(string text, char quote)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (quote == '"' && text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text[i] != quote)
                continue;
            if (quote == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
            {
                i++;
                continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Lines of the block <c>indent + key:</c> between <paramref name="start"/> and <paramref name="end"/>: the line of
    /// the key, and the end of the block (the next line indented as much or less, comments and blank lines aside);
    /// (-1, -1) when the key is missing.
    /// </summary>
    private static (int Start, int End) Section(List<string> lines, int start, int end, string indent, string key)
    {
        var pattern = new Regex($"^{Regex.Escape(indent)}{Regex.Escape(key)}\\s*:", RegexOptions.IgnoreCase);
        for (var i = start; i < end; i++)
        {
            if (!pattern.IsMatch(lines[i]))
                continue;
            var j = i + 1;
            for (; j < end; j++)
            {
                var line = lines[j];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    continue;
                if (line.Length - line.TrimStart().Length <= indent.Length)
                    break;
            }
            return (i, j);
        }
        return (-1, -1);
    }
}
