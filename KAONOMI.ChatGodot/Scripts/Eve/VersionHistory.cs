using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// 🕘 in the header of a window that saves a document (<see cref="IVersionedDocument"/>): the versions the server keeps
/// of the document shown (every state saved), the newest first. Each version says what made it (saved, deleted,
/// erased, restored), when, by whom and which fields changed; selected, it shows the document as it was then, the
/// fields that changed in colour. ↺ RESTORE makes it the document again (a new version: nothing is lost).
/// </summary>
public static class VersionHistory
{
    public static void Attach(EveFrame frame, IVersionedDocument document)
    {
        Button? button = null;
        button = frame.AddHeaderButton("⟲", "The versions of this document (every state saved in the search engine); any of them can be restored", () => Open(document));
        void Follow()
        {
            if (GodotObject.IsInstanceValid(button))
                button.Disabled = string.IsNullOrWhiteSpace(document.VersionDocumentId);
        }
        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Any change may show another document (its id is often a property of its own): the button follows.
            Bind.OnMain(Follow);
        }
        document.PropertyChanged += OnChanged;
        Bind.OnGone(button, () => document.PropertyChanged -= OnChanged);
        Follow();
    }

    private static string What(DocumentVersionEntry version) => version.Operation switch
    {
        "delete" => "deleted", "erase" => "erased", "restore" => "restored", _ => version.Version == 1 ? "created" : "saved"
    };

    /// <summary>The window of the versions of <paramref name="document"/>.</summary>
    public static void Open(IVersionedDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.VersionDocumentId))
            return;
        var index = document.VersionIndex;
        var id = document.VersionDocumentId!;
        List<DocumentVersionEntry> versions = [];
        DocumentVersionEntry? shown = null;

        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(330, 0), SelectMode = ItemList.SelectModeEnum.Single };
        list.AddThemeFontSizeOverride("font_size", 12);
        var header = Eve.Text_("", Eve.TextBright, 14, wrap: true);
        var content = new RichTextLabel { BbcodeEnabled = true, SelectionEnabled = true, ScrollActive = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeFontOverride("mono_font", Eve.Mono);
        var status = Eve.Text_("Reading the versions…", Eve.Muted, 11, wrap: true);
        var restore = Eve.Button("↺ RESTORE THIS VERSION", primary: true, tooltip: "Make this version the document again (kept as a new version: nothing is lost)");
        var copy = Eve.Button("📋 COPY JSON", tooltip: "The document of this version as JSON, on the clipboard");
        restore.Disabled = true;
        copy.Disabled = true;

        async Task Load(string? select = null)
        {
            try
            {
                versions = [.. await DocumentHistory.VersionsAsync(index, id)];
                list.Clear();
                foreach (var version in versions)
                {
                    var line = $"v{version.Version}  ·  {What(version)}  ·  {version.Date.ToLocalTime():dd MMM HH:mm:ss}  ·  {version.UserName}";
                    var item = list.AddItem(line);
                    list.SetItemTooltip(item, version.Changed.Count > 0 ? "changed: " + string.Join(", ", version.Changed) : string.Empty);
                    if (version.Operation is "delete" or "erase")
                        list.SetItemCustomFgColor(item, Eve.Red);
                    else if (version.Operation == "restore")
                        list.SetItemCustomFgColor(item, Eve.Amber);
                }
                status.Text = versions.Count == 0
                    ? "No version yet: the versions are kept from the next save of this document."
                    : $"{versions.Count} version(s) of this document · the newest is the document as it is now";
                var at = select is null ? 0 : Math.Max(0, versions.FindIndex(v => v.Id == select));
                if (versions.Count > 0)
                {
                    list.Select(at);
                    await Show(versions[at]);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                status.Text = "The versions could not be read: " + ex.Message;
            }
        }

        async Task Show(DocumentVersionEntry entry)
        {
            header.Text = $"v{entry.Version} · {What(entry)} on {entry.Date.ToLocalTime():dd MMM yyyy HH:mm:ss} by {entry.UserName}"
                + (entry.Changed.Count > 0 && entry.Version > 1 ? $" · changed: {string.Join(", ", entry.Changed)}" : string.Empty);
            content.Text = "[color=#8a96a8]reading…[/color]";
            var full = await DocumentHistory.VersionAsync(entry.Id);
            shown = full;
            restore.Disabled = full is null || entry.Id == versions.FirstOrDefault()?.Id;
            copy.Disabled = full is null;
            content.Text = full is null ? "This version could not be read." : Readable(full.Json, entry.Version > 1 ? entry.Changed : []);
        }

        list.ItemSelected += index => { if (index >= 0 && index < versions.Count) _ = Show(versions[(int)index]); };
        copy.Pressed += () => { if (shown is not null) Dialogs.Copy(PrettyJson(shown.Json)); };
        restore.Pressed += async () =>
        {
            if (shown is null)
                return;
            if (!await Dialogs.Confirm("Restore a version", $"Make the version {shown.Version} ({shown.Date.ToLocalTime():dd MMM HH:mm}) the document again? The document as it is now stays in the history (the restore is a new version).", "Restore"))
                return;
            try
            {
                var result = await DocumentHistory.RestoreAsync(shown.Id);
                if (!result.IsSuccess)
                {
                    status.Text = "Not restored: " + result.Message;
                    return;
                }
                await document.ReloadAfterRestoreAsync();
                status.Text = $"✓ The version {shown.Version} is the document again";
                ActivityLog.Info("Versions", $"{index} {id}: version {shown.Version} restored");
                await Load();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                status.Text = "Not restored: " + ex.Message;
            }
        };

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        left.AddThemeConstantOverride("separation", 6);
        left.AddChild(Eve.Caption("Versions, newest first"));
        left.AddChild(list);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        right.AddChild(header);
        right.AddChild(content);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        buttons.AddChild(restore);
        buttons.AddChild(copy);
        right.AddChild(buttons);
        var split = new HSplitContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.AddChild(left);
        split.AddChild(right);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);
        page.AddChild(split);
        page.AddChild(status);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Dialogs.Show($"History · {index} · {Shorten(id)}", page, new Vector2I(1080, 700));
        _ = Load();
    }

    private static string Shorten(string id) => id.Length > 28 ? id[..28] + "…" : id;

    private static string PrettyJson(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>A document as its fields, one per line (long values cut), the ones that changed in amber.</summary>
    private static string Readable(string json, IReadOnlyCollection<string> changed)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return Escape(json);
        }
        if (root is null)
            return Escape(json);
        var text = new StringBuilder();
        foreach (var (name, value) in root.OrderBy(p => changed.Contains(p.Key) ? 0 : 1).ThenBy(p => p.Key))
        {
            var shown = value switch
            {
                null => "null",
                JsonValue plain when plain.TryGetValue<string>(out var s) => s,
                _ => value.ToJsonString()
            };
            if (shown.Length > 1200)
                shown = shown[..1200] + $"… ({shown.Length} characters)";
            var color = changed.Contains(name) ? "#f0b45a" : "#7fd3ff";
            text.Append($"[color={color}][b]{Escape(name)}[/b][/color]  {Escape(shown)}\n");
        }
        return text.ToString();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
