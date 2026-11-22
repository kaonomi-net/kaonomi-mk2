using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// The notes of <c>/obs</c>, after Obsidian: a vault, a folder of Markdown files (<c>~/kaonomi/vault</c> by default, or
/// any folder, an Obsidian vault too: its <c>.obsidian</c> folder is left alone), the notes linked by <c>[[wiki links]]</c>
/// (<c>[[Note]]</c>, <c>[[Note|text]]</c>, <c>[[Note#heading]]</c>), their backlinks, their <c>#tags</c>, a search
/// through every note, the daily notes (<c>Daily/yyyy-MM-dd.md</c>), the outline of a note and the graph of the links.
/// A note is saved by itself a moment after a change; renaming a note changes the links to it in the other notes;
/// deleting a note moves it to the <c>.trash</c> folder of the vault (as Obsidian does): nothing is erased.
/// A note is attached to a project (the field of the project of the window): its YAML front matter, the properties of
/// Obsidian, keeps it (<c>project:</c> its name, <c>projectId:</c> its id).
/// </summary>
public sealed partial class VaultWindowViewModel : ViewModelBase, IDisposable, IProjectDocument
{
    private static readonly Regex WikiLink = new(@"\[\[([^\[\]|#\n]+)(#[^\[\]|\n]*)?(\|[^\[\]\n]*)?\]\]", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"(?<![\w#/&])#([\p{L}\p{N}_][\p{L}\p{N}_/\-]*)", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private bool _loading;

    public VaultWindowViewModel()
    {
        var saved = ReadSavedVault();
        SetVault(saved ?? DefaultVault, remember: false);
        // Notes changed on another machine (the store of the vault of the chat is the search engine): read again,
        // unless the note open has changes not saved yet (read at the next change of the list).
        LocalSync.Applied += kind =>
        {
            if (kind != "vaultnote" || !IsDefaultVault || IsDirty)
                return;
            var open = Current?.Path;
            Scan();
            if (open is not null && Notes.FirstOrDefault(n => n.Path == open) is { } note)
                Open(note);
        };
    }

    /// <summary>True when the vault open is the one of the chat (~/kaonomi/vault), whose store is the search engine.</summary>
    private bool IsDefaultVault =>
        string.Equals(System.IO.Path.GetFullPath(Vault).TrimEnd(System.IO.Path.DirectorySeparatorChar),
            System.IO.Path.GetFullPath(DefaultVault).TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.Ordinal);

    /// <summary>A note of the vault written, created, renamed or deleted: the vault of the chat goes to the search engine.</summary>
    private void Touched()
    {
        if (IsDefaultVault)
            LocalSync.Touch("vaultnote");
    }

    /// <summary>A note of the vault: its file, its name (without .md) and its folder (relative to the vault, "" at the root).</summary>
    public sealed record VaultNote(string Path, string Name, string Folder);

    public static string DefaultVault => System.IO.Path.Combine(ChatSettings.KaonomiHome, "vault");

    private static string SavedVaultFile => System.IO.Path.Combine(ChatSettings.KaonomiHome, "vault-path.txt");

    /// <summary>The folder of the vault.</summary>
    [ObservableProperty]
    private string _vault = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _isError;

    /// <summary>The note open (null: none).</summary>
    [ObservableProperty]
    private VaultNote? _current;

    /// <summary>The text of the note open, as edited.</summary>
    [ObservableProperty]
    private string _text = "";

    /// <summary>True when the text of the note open has changes not saved yet.</summary>
    [ObservableProperty]
    private bool _isDirty;

    partial void OnTextChanged(string value)
    {
        if (!_loading && Current is not null)
            IsDirty = true;
    }

    partial void OnCurrentChanged(VaultNote? value)
    {
        OnPropertyChanged(nameof(HasProjectDocument));
        OnPropertyChanged(nameof(ProjectId));
    }

    // ── The project of the note (its front matter) ──

    private static readonly Regex FrontMatter = new(@"\A---\r?\n(.*?)\r?\n---[ \t]*(\r?\n|\z)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>A note is open: it can be attached to a project.</summary>
    public bool HasProjectDocument => Current is not null;

    /// <summary>The project of the note open (projectId of its front matter), null for none; set, the front matter is written.</summary>
    public string? ProjectId
    {
        get => Property(Text, "projectId");
        set
        {
            if (Current is null || value == ProjectId)
                return;
            var name = string.IsNullOrWhiteSpace(value) ? null : ProjectCatalog.NameOf(value);
            Text = SetProperties(Text, ("project", name), ("projectId", string.IsNullOrWhiteSpace(value) ? null : value));
            OnPropertyChanged();
            SaveIfDirty();
            // The editor shows the text with its front matter changed.
            Opened?.Invoke();
            SetStatus(name is null ? $"\"{Current.Name}\" is attached to no project" : $"\"{Current.Name}\" attached to the project {name}");
        }
    }

    /// <summary>The name of the project of a note (its front matter), empty without one.</summary>
    public string ProjectOf(VaultNote note)
    {
        var text = TextOf(note);
        if (Property(text, "projectId") is not { } id)
            return "";
        // The projects not loaded yet (or this one deleted): the name written in the note.
        var name = ProjectCatalog.NameOf(id);
        return name == "(unknown project)" && Property(text, "project") is { } written ? written : name;
    }

    /// <summary>A property of the front matter of <paramref name="text"/> (key: value), null when it has none.</summary>
    public static string? Property(string text, string key)
    {
        if (FrontMatter.Match(text) is not { Success: true } m)
            return null;
        foreach (var line in m.Groups[1].Value.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(colon + 1)..].Trim().Trim('"', '\'') is { Length: > 0 } value ? value : null;
        }
        return null;
    }

    /// <summary>The text with these properties of its front matter set (null: removed); the front matter is made or removed as needed.</summary>
    public static string SetProperties(string text, params (string Key, string? Value)[] properties)
    {
        var lines = new List<string>();
        var body = text;
        if (FrontMatter.Match(text) is { Success: true } m)
        {
            lines.AddRange(m.Groups[1].Value.Split('\n').Select(l => l.TrimEnd('\r')));
            body = text[m.Length..];
        }
        foreach (var (key, value) in properties)
        {
            var index = lines.FindIndex(l => l.IndexOf(':') is > 0 and var colon && l[..colon].Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                if (index >= 0)
                    lines.RemoveAt(index);
                continue;
            }
            var line = $"{key}: \"{value.Replace("\"", "'")}\"";
            if (index >= 0)
                lines[index] = line;
            else
                lines.Add(line);
        }
        lines.RemoveAll(l => l.Trim().Length == 0);
        return lines.Count == 0 ? body.TrimStart('\r', '\n') : $"---\n{string.Join('\n', lines)}\n---\n" + body;
    }

    /// <summary>The text without its front matter (for the preview).</summary>
    public static string WithoutFrontMatter(string text) =>
        FrontMatter.Match(text) is { Success: true } m ? text[m.Length..] : text;

    /// <summary>Raised when notes were added, removed or renamed, or the vault changed (the lists are drawn again).</summary>
    public event Action? Changed;

    /// <summary>Raised when a note was opened (or closed).</summary>
    public event Action? Opened;

    public List<VaultNote> Notes { get; private set; } = [];

    public void SetStatus(string text, bool error = false)
    {
        Status = text;
        IsError = error;
    }

    // ── The vault ──

    /// <summary>Opens the folder <paramref name="folder"/> as the vault (the note open is saved first).</summary>
    public void SetVault(string folder, bool remember = true)
    {
        SaveIfDirty();
        try
        {
            Directory.CreateDirectory(folder);
            Vault = System.IO.Path.GetFullPath(folder);
            if (!Directory.EnumerateFiles(Vault, "*.md", SearchOption.AllDirectories).Any())
                File.WriteAllText(System.IO.Path.Combine(Vault, "Welcome.md"), WelcomeText);
                Touched();
            if (remember)
                File.WriteAllText(SavedVaultFile, Vault);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"The vault {folder} cannot be used: {ex.Message}", true);
            return;
        }
        Current = null;
        Text = "";
        IsDirty = false;
        Scan();
        var start = Notes.FirstOrDefault(n => n.Name.Equals("Welcome", StringComparison.OrdinalIgnoreCase) && n.Folder.Length == 0) ?? Notes.FirstOrDefault();
        if (start is not null)
            Open(start);
        SetStatus($"Vault {Vault}: {Notes.Count} note(s)");
    }

    private static string? ReadSavedVault()
    {
        try
        {
            return File.Exists(SavedVaultFile) && File.ReadAllText(SavedVaultFile).Trim() is { Length: > 0 } path && Directory.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the notes of the vault again (every .md, but in the hidden folders: .obsidian, .trash…).</summary>
    public void Scan()
    {
        _texts.Clear();
        var notes = new List<VaultNote>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Vault, "*.md", SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(Vault, file);
                if (relative.Split(System.IO.Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')))
                    continue;
                var folder = System.IO.Path.GetDirectoryName(relative) ?? "";
                notes.Add(new VaultNote(file, System.IO.Path.GetFileNameWithoutExtension(file), folder));
                try
                {
                    _texts[file] = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    _texts[file] = "";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"The vault cannot be read: {ex.Message}", true);
        }
        Notes = notes.OrderBy(n => n.Folder, StringComparer.CurrentCultureIgnoreCase).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Changed?.Invoke();
    }

    /// <summary>The folders of the vault (relative, "" for the root), the empty ones too.</summary>
    public List<string> Folders()
    {
        try
        {
            return Directory.EnumerateDirectories(Vault, "*", SearchOption.AllDirectories)
                .Select(d => System.IO.Path.GetRelativePath(Vault, d))
                .Where(r => !r.Split(System.IO.Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')))
                .OrderBy(r => r, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string TextOf(VaultNote note) => note.Path == Current?.Path ? Text : _texts.GetValueOrDefault(note.Path, "");

    // ── A note ──

    public void Open(VaultNote note)
    {
        SaveIfDirty();
        _loading = true;
        Current = note;
        Text = _texts.GetValueOrDefault(note.Path) ?? (File.Exists(note.Path) ? File.ReadAllText(note.Path) : "");
        IsDirty = false;
        _loading = false;
        Opened?.Invoke();
    }

    /// <summary>The note of a wiki link (by its name, then its path from the vault); null when there is none.</summary>
    public VaultNote? Find(string link)
    {
        var target = link.Trim().Replace('\\', '/');
        if (target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            target = target[..^3];
        return Notes.FirstOrDefault(n => Relative(n).Equals(target, StringComparison.OrdinalIgnoreCase))
               ?? Notes.FirstOrDefault(n => n.Name.Equals(System.IO.Path.GetFileName(target), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Opens the note of a wiki link; it is created (empty, with its title) when it does not exist yet.</summary>
    public void OpenLink(string link)
    {
        if (Find(link) is { } note)
            Open(note);
        else if (Create(link.Trim(), "") is { } created)
            SetStatus($"\"{created.Name}\" created from its link");
    }

    /// <summary>The path of a note from the vault, without .md ("Projects/Kaonomi").</summary>
    public string Relative(VaultNote note) =>
        System.IO.Path.Combine(note.Folder, note.Name).Replace('\\', '/');

    /// <summary>Writes the note open when it has changes.</summary>
    public void SaveIfDirty()
    {
        if (!IsDirty || Current is not { } note)
            return;
        try
        {
            File.WriteAllText(note.Path, Text);
            Touched();
            _texts[note.Path] = Text;
            IsDirty = false;
            // The project may have been typed in the front matter: the field of the project follows.
            OnPropertyChanged(nameof(ProjectId));
            SetStatus($"\"{note.Name}\" saved · {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"\"{note.Name}\" not saved: {ex.Message}", true);
        }
    }

    /// <summary>A new note <paramref name="name"/> in <paramref name="folder"/> (a name "Folder/Note" makes the folder), opened.</summary>
    public VaultNote? Create(string name, string folder)
    {
        name = name.Trim().Replace('\\', '/');
        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            name = name[..^3];
        if (name.Contains('/'))
        {
            folder = System.IO.Path.Combine(folder, System.IO.Path.GetDirectoryName(name) ?? "");
            name = System.IO.Path.GetFileName(name);
        }
        name = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        if (name.Length == 0)
        {
            SetStatus("A note needs a name", true);
            return null;
        }
        try
        {
            var directory = System.IO.Path.Combine(Vault, folder);
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, name + ".md");
            for (var i = 2; File.Exists(path); i++)
                path = System.IO.Path.Combine(directory, $"{name} {i}.md");
            File.WriteAllText(path, $"# {System.IO.Path.GetFileNameWithoutExtension(path)}\n\n");
            Touched();
            Scan();
            var note = Notes.First(n => n.Path == path);
            Open(note);
            return note;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"The note was not created: {ex.Message}", true);
            return null;
        }
    }

    /// <summary>A new folder of the vault.</summary>
    public void CreateFolder(string name)
    {
        name = name.Trim().Replace('\\', '/').Trim('/');
        if (name.Length == 0 || name.Split('/').Any(part => part.StartsWith('.') || part == ".."))
        {
            SetStatus("A folder needs a name (not starting with a dot)", true);
            return;
        }
        try
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Vault, name));
            SetStatus($"Folder \"{name}\" created");
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"The folder was not created: {ex.Message}", true);
        }
    }

    /// <summary>Renames the note open; the links to it in the other notes follow. False when it could not be done.</summary>
    public bool Rename(string newName)
    {
        if (Current is not { } note)
            return false;
        newName = string.Concat(newName.Trim().Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
        if (newName.Length == 0 || newName == note.Name)
            return false;
        SaveIfDirty();
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(note.Path)!, newName + ".md");
        if (File.Exists(path))
        {
            SetStatus($"A note \"{newName}\" exists already there", true);
            return false;
        }
        try
        {
            File.Move(note.Path, path);
            Touched();
            // The links to the note in the others: [[Old]], [[Old|…]], [[Old#…]] and the same with its folder.
            var changed = 0;
            foreach (var other in Notes.Where(n => n.Path != note.Path))
            {
                var text = _texts.GetValueOrDefault(other.Path, "");
                var replaced = WikiLink.Replace(text, m =>
                {
                    var target = m.Groups[1].Value.Trim();
                    if (!target.Equals(note.Name, StringComparison.OrdinalIgnoreCase) && !target.Equals(Relative(note), StringComparison.OrdinalIgnoreCase))
                        return m.Value;
                    var renamed = target.Contains('/') ? System.IO.Path.Combine(note.Folder, newName).Replace('\\', '/') : newName;
                    return $"[[{renamed}{m.Groups[2].Value}{m.Groups[3].Value}]]";
                });
                if (replaced == text)
                    continue;
                File.WriteAllText(other.Path, replaced);
                Touched();
                changed++;
            }
            Scan();
            Open(Notes.First(n => n.Path == path));
            SetStatus($"\"{note.Name}\" renamed \"{newName}\"{(changed > 0 ? $", the links of {changed} note(s) changed" : "")}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Not renamed: {ex.Message}", true);
            return false;
        }
    }

    /// <summary>Moves the note open to the .trash folder of the vault (nothing is erased).</summary>
    public void Delete()
    {
        if (Current is not { } note)
            return;
        SaveIfDirty();
        try
        {
            var trash = System.IO.Path.Combine(Vault, ".trash");
            Directory.CreateDirectory(trash);
            var target = System.IO.Path.Combine(trash, note.Name + ".md");
            for (var i = 2; File.Exists(target); i++)
                target = System.IO.Path.Combine(trash, $"{note.Name} {i}.md");
            File.Move(note.Path, target);
            Touched();
            Current = null;
            Text = "";
            IsDirty = false;
            Scan();
            Opened?.Invoke();
            SetStatus($"\"{note.Name}\" moved to the trash of the vault (.trash)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Not moved to the trash: {ex.Message}", true);
        }
    }

    /// <summary>The note of today in Daily (yyyy-MM-dd), made when it does not exist yet.</summary>
    public void Daily()
    {
        var name = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Notes.FirstOrDefault(n => n.Name == name) is { } note)
        {
            Open(note);
            return;
        }
        if (Create(name, "Daily") is { } daily)
        {
            _loading = false;
            Text = $"# {DateTime.Today.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)}\n\n## To do\n\n- [ ] \n\n## Notes\n\n";
            SaveIfDirty();
            Opened?.Invoke();
            SetStatus($"Daily note {daily.Name} created");
        }
    }

    // ── Links, tags, search ──

    /// <summary>The notes the wiki links of <paramref name="text"/> go to (their names as written).</summary>
    public static List<string> LinksOf(string text) =>
        WikiLink.Matches(text).Select(m => m.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The notes linking to <paramref name="note"/>, each with the line of the link.</summary>
    public List<(VaultNote Note, string Line)> Backlinks(VaultNote note)
    {
        var result = new List<(VaultNote, string)>();
        foreach (var other in Notes.Where(n => n.Path != note.Path))
        {
            var text = TextOf(other);
            foreach (Match m in WikiLink.Matches(text))
            {
                var target = m.Groups[1].Value.Trim();
                if (!target.Equals(note.Name, StringComparison.OrdinalIgnoreCase) && !target.Equals(Relative(note), StringComparison.OrdinalIgnoreCase))
                    continue;
                var start = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                var end = text.IndexOf('\n', m.Index);
                result.Add((other, text[start..(end < 0 ? text.Length : end)].Trim()));
                break;
            }
        }
        return result;
    }

    /// <summary>The tags of <paramref name="text"/> (without #), but in code.</summary>
    public static List<string> TagsOf(string text)
    {
        var withoutCode = Regex.Replace(WithoutFrontMatter(text), "```[\\s\\S]*?```|`[^`\\n]*`", "");
        withoutCode = Heading.Replace(withoutCode, m => m.Groups[2].Value);
        return Tag.Matches(withoutCode).Select(m => m.Groups[1].Value).Where(t => !t.All(char.IsDigit)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every tag of the vault with how many notes have it, the most used first.</summary>
    public List<(string Tag, int Count)> AllTags() =>
        Notes.SelectMany(n => TagsOf(TextOf(n))).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First(), g.Count())).OrderByDescending(t => t.Item2).ThenBy(t => t.Item1, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>The notes whose name or text hold every word of <paramref name="term"/> ("#tag": the notes of that tag), each with a line where it is found.</summary>
    public List<(VaultNote Note, string Snippet)> Search(string term)
    {
        term = term.Trim();
        if (term.Length == 0)
            return [];
        // "project:Name": the notes of that project.
        if (term.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            var project = term[8..].Trim().Trim('"');
            return Notes.Where(n => ProjectOf(n) is { Length: > 0 } name && name.Contains(project, StringComparison.OrdinalIgnoreCase))
                .Select(n => (n, "🎯 " + ProjectOf(n))).ToList();
        }
        if (term.StartsWith('#') && !term.Contains(' '))
        {
            var tag = term[1..];
            return Notes.Where(n => TagsOf(TextOf(n)).Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase) || t.StartsWith(tag + "/", StringComparison.OrdinalIgnoreCase)))
                .Select(n => (n, "#" + tag)).ToList();
        }
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = new List<(VaultNote, string)>();
        foreach (var note in Notes)
        {
            var text = TextOf(note);
            if (!words.All(w => note.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                continue;
            var at = text.IndexOf(words[0], StringComparison.OrdinalIgnoreCase);
            var snippet = at < 0 ? "" : text[Math.Max(0, at - 40)..Math.Min(text.Length, at + 80)].Replace('\n', ' ').Trim();
            found.Add((note, snippet));
        }
        return found.OrderByDescending(f => words.Count(w => f.Item1.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>The headings of the note open: level, title, line.</summary>
    public List<(int Level, string Title, int Line)> Outline()
    {
        var result = new List<(int, string, int)>();
        var lines = Text.Split('\n');
        var inCode = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                inCode = !inCode;
            if (!inCode && Heading.Match(lines[i]) is { Success: true } m)
                result.Add((m.Groups[1].Value.Length, m.Groups[2].Value.Trim(), i));
        }
        return result;
    }

    /// <summary>The links between the notes (from, to), for the graph; a link to a note that does not exist is left out.</summary>
    public List<(VaultNote From, VaultNote To)> Edges()
    {
        var edges = new List<(VaultNote, VaultNote)>();
        foreach (var note in Notes)
            foreach (var link in LinksOf(TextOf(note)))
                if (Find(link) is { } target && target.Path != note.Path)
                    edges.Add((note, target));
        return edges.Distinct().ToList();
    }

    /// <summary>
    /// The Markdown of the note for the preview: its wiki links as links of the scheme <c>obs:</c> (opened in the vault;
    /// a link to a note that does not exist yet is shown apart), its tags as links of the search (<c>obstag:</c>).
    /// </summary>
    public string PreviewMarkdown()
    {
        var text = WikiLink.Replace(WithoutFrontMatter(Text), m =>
        {
            var target = m.Groups[1].Value.Trim();
            var shown = m.Groups[3].Success ? m.Groups[3].Value[1..] : target + m.Groups[2].Value;
            var exists = Find(target) is not null;
            return $"[{(exists ? shown : shown + " ✚")}](obs:{Uri.EscapeDataString(target)})";
        });
        return Tag.Replace(text, m => m.Index > 0 && text[m.Index - 1] == '(' ? m.Value : $"[#{m.Groups[1].Value}](obstag:{Uri.EscapeDataString(m.Groups[1].Value)})");
    }

    public void Dispose() => SaveIfDirty();

    private const string WelcomeText = """
        # Welcome

        This is your vault: a folder of Markdown notes, as in Obsidian. Every note is a `.md` file of this folder, and it can be opened by Obsidian too.

        - Link a note to another with double brackets: [[Ideas]] or [[Ideas|the ideas]]. A link to a note that does not exist yet creates it when it is clicked.
        - Tag a note with #tags: #kaonomi #getting-started. A click on a tag searches its notes.
        - The **backlinks** on the right are the notes linking to this one; the **outline** its headings.
        - **DAILY** opens the note of today, in the folder Daily.
        - **GRAPH** shows the notes and their links.
        - A note is saved by itself a moment after a change. A note deleted goes to the `.trash` folder of the vault.

        ## Next

        Start with [[Ideas]], or press ✚ NOTE.
        """;
}
