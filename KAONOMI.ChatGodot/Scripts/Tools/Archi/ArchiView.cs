using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Archi;

/// <summary>
/// /archi: the architecture repository, after SAP LeanIX (without its security nor its access rights), with the fact
/// sheets of AI (models, agents, multi-agent systems, MCP servers, tools, REST APIs, prompts, datasets, vector stores,
/// knowledge bases, evaluations, guardrails). Its tabs: INVENTORY (the facets of the kinds and of the lifecycle, a
/// search, the list, the fact sheet selected in its editor with its relations), LANDSCAPE (the capability map with the
/// applications and agents supporting each capability, coloured by lifecycle, criticality, fit or AI risk), MATRIX
/// (two kinds of fact sheets crossed by their relations), ROADMAP (the lifecycles on a timeline), GRAPH (the fact sheet
/// selected and its neighbours) and AI (the AI landscape: counts, EU AI Act risks, the agents with their models, tools
/// and APIs, the warnings). NEW adds a fact sheet of any kind; JSON and CSV export, IMPORT reads a JSON; GRAPHBASE writes
/// the repository to KAONOMI.graphbase now.
/// </summary>
[ToolView(typeof(ArchiWindowViewModel), "Architecture", "🏗", 1560, 920)]
public sealed class ArchiView : IToolView
{
    public Control Build(object viewModel, Shell shell) => new ArchiPage((ArchiWindowViewModel)viewModel);
}

/// <summary>The page of /archi: its toolbar, its tabs and its status.</summary>
public sealed partial class ArchiPage : VBoxContainer
{
    private readonly ArchiWindowViewModel _vm = null!;
    private readonly MarginContainer _body = new() { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Dictionary<string, Button> _tabs = [];
    private string _tab = "inventory";
    private ArchiInventory? _inventory;

    // Godot builds its scripts with this constructor.
    public ArchiPage()
    {
    }

    public ArchiPage(ArchiWindowViewModel vm)
    {
        _vm = vm;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);

        var group = new ButtonGroup();
        var tabRow = new HBoxContainer();
        tabRow.AddThemeConstantOverride("separation", 4);
        foreach (var (key, text, tooltip) in new[]
                 {
                     ("inventory", "📋 INVENTORY", "The fact sheets: facets, search, list and the editor of the one selected"),
                     ("landscape", "🗺 LANDSCAPE", "The capability map: the applications and agents supporting each capability"),
                     ("matrix", "▦ MATRIX", "Two kinds of fact sheets crossed by their relations"),
                     ("roadmap", "📅 ROADMAP", "The lifecycles on a timeline"),
                     ("graph", "🕸 GRAPH", "The fact sheet selected and its neighbours"),
                     ("ai", "🤖 AI", "The AI landscape: models, agents, multi-agent systems, APIs, risks and warnings")
                 })
        {
            var button = E.Button(text, tooltip: tooltip);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.Pressed += () => ShowTab(key);
            _tabs[key] = button;
            tabRow.AddChild(button);
        }

        var add = new MenuButton { Text = "✚ NEW ▾", TooltipText = "A new fact sheet, of any kind", Flat = false };
        add.AddThemeStyleboxOverride("normal", E.Box(E.Cyan with { A = 0.18f }, E.Cyan, 6));
        var popup = add.GetPopup();
        var index = 0;
        foreach (var layer in ArchiCatalog.Layers)
        {
            popup.AddSeparator(layer == ArchiCatalog.AiLayer ? "AI" : layer);
            index++;
            foreach (var info in ArchiCatalog.Types.Where(t => t.Layer == layer))
            {
                popup.AddItem($"{info.Icon}  {info.Label}", (int)info.Type);
                index++;
            }
        }
        popup.IdPressed += id =>
        {
            _vm.Add((ArchiType)(int)id);
            ShowTab("inventory");
        };

        var header = Ui.Toolbar(Ui.Row(8, [
            E.Caption("Architecture", E.TextBright, 14),
            tabRow,
            Ui.Spring(),
            add,
            Ui.Action("⬇ JSON", () => _ = ExportAsync(json: true), tooltip: "Export the whole repository as JSON"),
            Ui.Action("⬇ CSV", () => _ = ExportAsync(json: false), tooltip: "Export the inventory as CSV (for Excel)"),
            Ui.Action("⬆ IMPORT", () => _ = ImportAsync(), tooltip: "Replace the repository by a JSON exported before (the current one is kept in ~/kaonomi/archi/history)"),
            Ui.Action("⇄ GRAPHBASE", () => _ = _vm.SyncGraphbaseAsync(), tooltip: "Write every fact sheet and relation to KAONOMI.graphbase now, as a graph (done by itself when the window opens and after each change, while graphbase runs)")]));
        AddChild(header);
        AddChild(_body);
        AddChild(Ui.StatusBar(_vm, nameof(_vm.Status), nameof(_vm.IsError)));

        _vm.Changed += OnChanged;
        Bind.OnGone(this, () => _vm.Changed -= OnChanged);
        ShowTab("inventory");
    }

    private void OnChanged()
    {
        if (_tab == "inventory")
            _inventory?.Refresh();
        else
            ShowTab(_tab);
    }

    /// <summary>Shows the tab <paramref name="key"/> (built again: the reports follow the repository).</summary>
    public void ShowTab(string key)
    {
        _tab = key;
        _tabs[key].SetPressedNoSignal(true);
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        _inventory = null;
        Control content = key switch
        {
            "landscape" => ArchiReports.Landscape(_vm, Open),
            "matrix" => ArchiReports.Matrix(_vm, Open),
            "roadmap" => ArchiReports.Roadmap(_vm, Open),
            "graph" => ArchiReports.Graph(_vm, Open),
            "ai" => ArchiReports.Ai(_vm, Open),
            _ => _inventory = new ArchiInventory(_vm)
        };
        _body.AddChild(content);
    }

    /// <summary>A fact sheet clicked in a report: selected and shown in the inventory.</summary>
    private void Open(string id)
    {
        _vm.SelectedId = id;
        ShowTab("inventory");
    }

    private async Task ExportAsync(bool json)
    {
        var name = $"archi-{DateTime.Now:yyyyMMdd-HHmm}.{(json ? "json" : "csv")}";
        var path = await Dialogs.SaveFile(json ? "Export the repository" : "Export the inventory", name,
            json ? ["*.json ; JSON"] : ["*.csv ; CSV"], ArchiWindowViewModel.Folder);
        if (path is null)
            return;
        try
        {
            await File.WriteAllTextAsync(path, json ? _vm.ExportJson() : "﻿" + _vm.ExportCsv());
            _vm.SetStatus($"Exported to {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.SetStatus($"Not exported: {ex.Message}", true);
        }
    }

    private async Task ImportAsync()
    {
        var path = await Dialogs.OpenFile("Import a repository (JSON)", ["*.json ; JSON"], ArchiWindowViewModel.Folder);
        if (path is null)
            return;
        if (!await Dialogs.Confirm("Import", $"The repository is replaced by {Path.GetFileName(path)}. The current one is kept in {ArchiWindowViewModel.HistoryFolder}.", "Import"))
            return;
        if (_vm.Import(path))
            ShowTab(_tab);
    }
}

/// <summary>The tab INVENTORY: the facets on the left, the list in the middle, the fact sheet selected on the right.</summary>
public sealed partial class ArchiInventory : HBoxContainer
{
    private readonly ArchiWindowViewModel _vm = null!;
    private readonly HashSet<ArchiType> _types = [];
    private readonly VBoxContainer _facets = new();
    private readonly Tree _list = new();
    private readonly MarginContainer _editorHost = new();
    private readonly Label _count = E.Text_("", E.Muted, 11);
    private string _search = "";
    private ArchiPhase? _phase;
    private bool _filling;
    private string? _editing;
    private ArchiEditor? _editor;

    // Godot builds its scripts with this constructor.
    public ArchiInventory()
    {
    }

    public ArchiInventory(ArchiWindowViewModel vm)
    {
        _vm = vm;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
        // The first fact sheet is shown at the start.
        _vm.SelectedId ??= _vm.Filter([], "").FirstOrDefault()?.Id;

        // ── The facets ──
        var search = new LineEdit { PlaceholderText = "🔎 Search name, owner, tag, field…", ClearButtonEnabled = true };
        search.TextChanged += text =>
        {
            _search = text;
            FillList();
        };
        var phase = new OptionButton { TooltipText = "The phase of the lifecycle today" };
        phase.AddItem("Any phase today");
        foreach (var p in Enum.GetValues<ArchiPhase>())
            phase.AddItem(ArchiReports.PhaseName(p));
        phase.ItemSelected += i =>
        {
            _phase = i == 0 ? null : (ArchiPhase)(int)(i - 1);
            FillList();
        };
        var facets = Ui.Column(8, search, phase, _facets);
        var facetsScroll = new ScrollContainer { CustomMinimumSize = new Vector2(250, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        facetsScroll.AddChild(facets);
        facets.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // ── The list ──
        _list.Columns = 6;
        _list.HideRoot = true;
        _list.ColumnTitlesVisible = true;
        _list.SelectMode = Tree.SelectModeEnum.Row;
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.SizeFlagsVertical = SizeFlags.ExpandFill;
        string[] titles = ["Name", "Kind", "Phase today", "Criticality", "Owner", "Complete"];
        int[] widths = [240, 140, 90, 140, 110, 70];
        for (var i = 0; i < titles.Length; i++)
        {
            _list.SetColumnTitle(i, titles[i]);
            _list.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            _list.SetColumnCustomMinimumWidth(i, widths[i]);
            _list.SetColumnExpand(i, i == 0);
        }
        _list.ItemSelected += () =>
        {
            if (_filling || _list.GetSelected() is not { } item)
                return;
            _vm.SelectedId = item.GetMetadata(0).AsString();
            ShowEditor();
        };
        var listColumn = Ui.Column(4, _list, _count);
        listColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listColumn.SizeFlagsVertical = SizeFlags.ExpandFill;

        // ── The editor ──
        var editorScroll = new ScrollContainer { CustomMinimumSize = new Vector2(500, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        _editorHost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        editorScroll.AddChild(_editorHost);

        AddChild(facetsScroll);
        AddChild(listColumn);
        AddChild(editorScroll);
        Refresh();
        ShowEditor();
    }

    /// <summary>The repository changed: the facets and the list again; the relations of the fact sheet shown.</summary>
    public void Refresh()
    {
        FillFacets();
        FillList();
        if (_vm.SelectedId != _editing || (_vm.Selected is null && _editor is not null))
            ShowEditor();
        else
            _editor?.RefreshRelations();
    }

    private void FillFacets()
    {
        foreach (var child in _facets.GetChildren())
            child.QueueFree();
        var all = E.Button(_types.Count == 0 ? "● All kinds" : "○ All kinds", tooltip: "Every kind of fact sheet");
        all.Alignment = HorizontalAlignment.Left;
        all.Pressed += () =>
        {
            _types.Clear();
            FillFacets();
            FillList();
        };
        _facets.AddChild(all);
        foreach (var layer in ArchiCatalog.Layers)
        {
            _facets.AddChild(E.Caption(layer == ArchiCatalog.AiLayer ? "AI" : layer, layer == ArchiCatalog.AiLayer ? E.Cyan : E.Muted, 11));
            foreach (var info in ArchiCatalog.Types.Where(t => t.Layer == layer))
            {
                var count = _vm.Sheets.Count(s => s.Type == info.Type);
                var check = new CheckBox { Text = $"{info.Icon} {info.Plural} ({count})", ButtonPressed = _types.Contains(info.Type) };
                check.AddThemeColorOverride("font_color", count == 0 ? E.Muted : E.Text);
                var type = info.Type;
                check.Toggled += on =>
                {
                    if (on)
                        _types.Add(type);
                    else
                        _types.Remove(type);
                    FillList();
                };
                _facets.AddChild(check);
            }
        }
    }

    private void FillList()
    {
        _filling = true;
        _list.Clear();
        var root = _list.CreateItem();
        var sheets = _vm.Filter(_types, _search, _phase);
        TreeItem? selected = null;
        foreach (var s in sheets)
        {
            var info = ArchiCatalog.Info(s.Type);
            var item = _list.CreateItem(root);
            item.SetText(0, $"{info.Icon}  {_vm.FullName(s)}");
            item.SetMetadata(0, s.Id);
            item.SetText(1, info.Label);
            item.SetCustomColor(1, Color.FromHtml(info.Color).Lightened(E.IsLight ? 0 : 0.25f));
            var phase = s.PhaseOn(DateTime.Today);
            item.SetText(2, phase is { } p ? ArchiReports.PhaseName(p) : "—");
            item.SetCustomColor(2, ArchiReports.PhaseColor(phase));
            item.SetText(3, s.Criticality.Length > 0 ? s.Criticality : "—");
            item.SetText(4, s.Owner);
            var complete = _vm.Completeness(s);
            item.SetText(5, $"{complete} %");
            item.SetCustomColor(5, complete >= 80 ? E.Green : complete >= 50 ? E.Amber : E.Red);
            item.SetTooltipText(0, s.Description);
            if (s.Id == _vm.SelectedId)
                selected = item;
        }
        if (selected is not null)
        {
            selected.Select(0);
            _list.ScrollToItem(selected);
        }
        _count.Text = $"{sheets.Count} of {_vm.Sheets.Count} fact sheets · {_vm.Relations.Count} relations · saved in {ArchiWindowViewModel.FilePath}";
        _filling = false;
    }

    private void ShowEditor()
    {
        foreach (var child in _editorHost.GetChildren())
        {
            _editorHost.RemoveChild(child);
            child.QueueFree();
        }
        _editing = _vm.SelectedId;
        _editor = null;
        if (_vm.Selected is { } sheet)
            _editorHost.AddChild(_editor = new ArchiEditor(_vm, sheet, id =>
            {
                _vm.SelectedId = id;
                Refresh();
            }));
        else
            _editorHost.AddChild(Ui.Placeholder("Select a fact sheet in the list, or add one with ✚ NEW."));
    }
}

/// <summary>The editor of a fact sheet: its fields, its lifecycle, its scores, the fields of its kind and its relations.</summary>
public sealed partial class ArchiEditor : VBoxContainer
{
    private readonly ArchiWindowViewModel _vm = null!;
    private readonly ArchiSheet _sheet = null!;
    private readonly Action<string> _open = null!;
    private readonly VBoxContainer _relations = new();
    private readonly Label _complete = E.Text_("", E.Muted, 12);

    // Godot builds its scripts with this constructor.
    public ArchiEditor()
    {
    }

    public ArchiEditor(ArchiWindowViewModel vm, ArchiSheet sheet, Action<string> open)
    {
        _vm = vm;
        _sheet = sheet;
        _open = open;
        var info = ArchiCatalog.Info(sheet.Type);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);

        // ── Its kind, its name, its description ──
        var kind = E.Caption($"{info.Icon}  {info.Label.ToUpperInvariant()}", Color.FromHtml(info.Color).Lightened(E.IsLight ? 0 : 0.3f), 13);
        AddChild(Ui.Row(8, kind, Ui.Spring(), _complete));
        var name = new LineEdit { Text = sheet.Name, PlaceholderText = "Name" };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.TextChanged += text =>
        {
            sheet.Name = text.Trim();
            _vm.Touch(sheet);
        };
        name.TextSubmitted += _ => _vm.Touch(sheet, redraw: true);
        name.FocusExited += () => _vm.Touch(sheet, redraw: true);
        AddChild(name);
        var description = new TextEdit { Text = sheet.Description, PlaceholderText = "Description", CustomMinimumSize = new Vector2(0, 80), WrapMode = TextEdit.LineWrappingMode.Boundary };
        description.TextChanged += () =>
        {
            sheet.Description = description.Text;
            _vm.Touch(sheet);
        };
        AddChild(Ui.Field("Description", description));

        var grid = Ui.Grid(2, 12, 8);
        grid.AddChild(Ui.Field("Owner", Text(sheet.Owner, "Who answers for it", v => sheet.Owner = v, redraw: true)));
        grid.AddChild(Ui.Field("Tags", Text(sheet.Tags, "comma separated", v => sheet.Tags = v)));
        grid.AddChild(Ui.Field("Parent", Parent()));
        grid.AddChild(Ui.Field("Business criticality", Choice(ArchiCatalog.Criticality, sheet.Criticality, v => sheet.Criticality = v)));
        grid.AddChild(Ui.Field("Functional fit", Choice(ArchiCatalog.Fit, sheet.FunctionalFit, v => sheet.FunctionalFit = v)));
        grid.AddChild(Ui.Field("Technical fit", Choice(ArchiCatalog.Fit, sheet.TechnicalFit, v => sheet.TechnicalFit = v)));
        if (info.IsAi)
            grid.AddChild(Ui.Field("Risk (EU AI Act)", Choice(ArchiCatalog.AiRisk, sheet.AiRisk, v => sheet.AiRisk = v)));
        AddChild(grid);

        // ── The lifecycle ──
        AddChild(E.Caption("Lifecycle (the day each phase starts, yyyy-mm-dd)", E.Muted, 11));
        var phases = Ui.Grid(5, 8, 4);
        foreach (var phase in Enum.GetValues<ArchiPhase>())
            phases.AddChild(Ui.Field(ArchiReports.PhaseName(phase), Date(phase)));
        AddChild(phases);

        // ── The fields of its kind ──
        if (info.Fields.Length > 0)
        {
            AddChild(E.Caption(info.Label, E.Muted, 11));
            var fields = Ui.Grid(2, 12, 8);
            foreach (var field in info.Fields)
            {
                var key = field.Key;
                fields.AddChild(Ui.Field(field.Caption, field.IsChoice
                    ? Choice(field.Choices, sheet.Field(key), v => sheet.Fields[key] = v)
                    : Text(sheet.Field(key), field.Hint, v => sheet.Fields[key] = v)));
            }
            AddChild(fields);
        }

        // ── Its relations ──
        AddChild(E.Separator());
        AddChild(_relations);
        RefreshRelations();

        AddChild(E.Separator());
        var duplicate = Ui.Action("⎘ DUPLICATE", () => _vm.Duplicate(sheet), tooltip: "A copy of this fact sheet, with its relations to the others");
        var delete = Ui.Action("✕ DELETE", () => _ = DeleteAsync(), tooltip: "Remove this fact sheet and its relations");
        delete.AddThemeColorOverride("font_color", E.Red);
        AddChild(Ui.Row(8, duplicate, delete, Ui.Spring(), E.Text_($"Updated {sheet.Updated:yyyy-MM-dd HH:mm}", E.Muted, 11)));
    }

    private async Task DeleteAsync()
    {
        var count = _vm.Neighbours(_sheet.Id).Count();
        if (await Dialogs.Confirm("Delete", $"\"{_sheet.Name}\" and its {count} relation(s) are removed. The repository as it was when the window opened stays in the history.", "Delete"))
            _vm.Delete(_sheet);
    }

    private LineEdit Text(string value, string placeholder, Action<string> set, bool redraw = false)
    {
        var edit = new LineEdit { Text = value, PlaceholderText = placeholder };
        edit.TextChanged += text =>
        {
            set(text.Trim());
            _vm.Touch(_sheet);
            UpdateComplete();
        };
        if (redraw)
            edit.FocusExited += () => _vm.Touch(_sheet, redraw: true);
        return edit;
    }

    private OptionButton Choice(string[] values, string current, Action<string> set)
    {
        var option = new OptionButton();
        var list = values.ToList();
        if (current.Length > 0 && !list.Contains(current))
            list.Add(current);
        foreach (var value in list)
            option.AddItem(value.Length == 0 ? "—" : value);
        option.Selected = Math.Max(0, list.IndexOf(current));
        option.ItemSelected += i =>
        {
            set(list[(int)i]);
            _vm.Touch(_sheet, redraw: true);
        };
        return option;
    }

    private OptionButton Parent()
    {
        var option = new OptionButton { TooltipText = "The fact sheet of the same kind above this one (a capability of level 1 above one of level 2…)" };
        var candidates = _vm.Sheets.Where(s => s.Type == _sheet.Type && s.Id != _sheet.Id && !IsBelow(s, _sheet)).OrderBy(_vm.FullName).ToList();
        option.AddItem("— none");
        foreach (var c in candidates)
            option.AddItem(_vm.FullName(c));
        option.Selected = _sheet.ParentId is { } id ? candidates.FindIndex(c => c.Id == id) + 1 : 0;
        option.ItemSelected += i =>
        {
            _sheet.ParentId = i == 0 ? null : candidates[(int)i - 1].Id;
            _vm.Touch(_sheet, redraw: true);
        };
        return option;
    }

    /// <summary>Whether <paramref name="candidate"/> is under <paramref name="sheet"/> (it cannot be its parent).</summary>
    private bool IsBelow(ArchiSheet candidate, ArchiSheet sheet)
    {
        var seen = new HashSet<string>();
        for (var p = candidate.ParentId; p is not null && seen.Add(p); p = _vm.Find(p)?.ParentId)
            if (p == sheet.Id)
                return true;
        return false;
    }

    private LineEdit Date(ArchiPhase phase)
    {
        var edit = new LineEdit { Text = _sheet.Lifecycle.GetValueOrDefault(phase, ""), PlaceholderText = "yyyy-mm-dd" };
        edit.AddThemeColorOverride("font_color", ArchiReports.PhaseColor(phase));
        edit.TextChanged += text =>
        {
            text = text.Trim();
            var valid = text.Length == 0 || DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
            edit.AddThemeColorOverride("font_color", valid ? ArchiReports.PhaseColor(phase) : E.Red);
            if (!valid)
                return;
            if (text.Length == 0)
                _sheet.Lifecycle.Remove(phase);
            else
                _sheet.Lifecycle[phase] = text;
            _vm.Touch(_sheet, redraw: true);
        };
        return edit;
    }

    private void UpdateComplete()
    {
        var complete = _vm.Completeness(_sheet);
        _complete.Text = $"{complete} % complete";
        _complete.AddThemeColorOverride("font_color", complete >= 80 ? E.Green : complete >= 50 ? E.Amber : E.Red);
    }

    /// <summary>The relations from and to the fact sheet, and the row to add one.</summary>
    public void RefreshRelations()
    {
        if (_sheet is null)
            return;
        UpdateComplete();
        foreach (var child in _relations.GetChildren())
            child.QueueFree();
        _relations.AddThemeConstantOverride("separation", 4);
        _relations.AddChild(E.Caption("Relations", E.TextBright, 12));
        var outgoing = _vm.Outgoing(_sheet.Id).ToList();
        var incoming = _vm.Incoming(_sheet.Id).ToList();
        if (outgoing.Count + incoming.Count == 0)
            _relations.AddChild(E.Text_("None yet: relate it to the others below.", E.Muted, 12));
        foreach (var relation in outgoing.OrderBy(r => r.Kind))
            if (_vm.Find(relation.To) is { } to)
                _relations.AddChild(RelationRow("→ " + relation.Kind, to, relation));
        foreach (var relation in incoming.OrderBy(r => r.Kind))
            if (_vm.Find(relation.From) is { } source)
                _relations.AddChild(RelationRow("← " + relation.Kind + " by", source, relation));

        // A new relation: its kind, the fact sheet it goes to.
        var others = _vm.Sheets.Where(s => s.Id != _sheet.Id).OrderBy(s => s.Type).ThenBy(_vm.FullName).ToList();
        var target = new OptionButton { TooltipText = "The fact sheet it goes to", SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false };
        target.AddItem("Relate to…");
        foreach (var other in others)
            target.AddItem($"{ArchiCatalog.Info(other.Type).Icon} {_vm.FullName(other)}");
        var kind = new OptionButton { TooltipText = "The kind of relation" };
        foreach (var k in ArchiCatalog.RelationKinds)
            kind.AddItem(k);
        target.ItemSelected += i =>
        {
            if (i > 0)
                kind.Selected = Array.IndexOf(ArchiCatalog.RelationKinds, ArchiCatalog.SuggestedKind(_sheet.Type, others[(int)i - 1].Type));
        };
        var add = Ui.Action("✚ RELATE", () =>
        {
            if (target.Selected <= 0)
            {
                _vm.SetStatus("Choose the fact sheet to relate it to", true);
                return;
            }
            _vm.Relate(_sheet.Id, others[target.Selected - 1].Id, ArchiCatalog.RelationKinds[kind.Selected]);
        }, primary: true);
        _relations.AddChild(Ui.Row(6, kind, target, add));
    }

    private Control RelationRow(string label, ArchiSheet other, ArchiRelation relation)
    {
        var info = ArchiCatalog.Info(other.Type);
        var caption = E.Text_(label, E.Muted, 12);
        caption.CustomMinimumSize = new Vector2(150, 0);
        var link = E.Button($"{info.Icon} {_vm.FullName(other)}", tooltip: $"{info.Label} — open it");
        link.Alignment = HorizontalAlignment.Left;
        link.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        link.ClipText = true;
        link.Pressed += () => _open(other.Id);
        var remove = E.Button("✕", tooltip: "Remove this relation");
        remove.Pressed += () => _vm.Unrelate(relation);
        return Ui.Row(6, caption, link, remove);
    }
}
