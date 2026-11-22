using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Archi;

/// <summary>The reports of /archi: the landscape, the matrix, the roadmap, the graph and the AI landscape.</summary>
public static partial class ArchiReports
{
    // ── Colours ──

    public static string PhaseName(ArchiPhase phase) => phase switch
    {
        ArchiPhase.Plan => "Plan",
        ArchiPhase.PhaseIn => "Phase in",
        ArchiPhase.Active => "Active",
        ArchiPhase.PhaseOut => "Phase out",
        _ => "End of life"
    };

    public static Color PhaseColor(ArchiPhase? phase) => phase switch
    {
        ArchiPhase.Plan => new Color(0.55f, 0.62f, 0.75f),
        ArchiPhase.PhaseIn => new Color(0.2f, 0.7f, 0.9f),
        ArchiPhase.Active => new Color(0.3f, 0.75f, 0.4f),
        ArchiPhase.PhaseOut => new Color(0.95f, 0.65f, 0.2f),
        ArchiPhase.EndOfLife => new Color(0.88f, 0.3f, 0.28f),
        _ => new Color(0.55f, 0.55f, 0.58f)
    };

    private static Color Scale(string[] scale, string value, bool reversed = false)
    {
        var index = Array.IndexOf(scale, value);
        if (index <= 0)
            return new Color(0.55f, 0.55f, 0.58f);
        var t = (index - 1) / (float)Math.Max(1, scale.Length - 2);
        if (reversed)
            t = 1 - t;
        // From green (good) to red (bad).
        return new Color(0.3f, 0.75f, 0.4f).Lerp(new Color(0.95f, 0.75f, 0.2f), Math.Min(1, t * 2)).Lerp(new Color(0.88f, 0.3f, 0.28f), Math.Max(0, t * 2 - 1));
    }

    /// <summary>The colour of a fact sheet in the mode of the landscape.</summary>
    private static Color ModeColor(string mode, ArchiSheet s) => mode switch
    {
        "Business criticality" => Scale(ArchiCatalog.Criticality, s.Criticality),
        "Functional fit" => Scale(ArchiCatalog.Fit, s.FunctionalFit, reversed: true),
        "Technical fit" => Scale(ArchiCatalog.Fit, s.TechnicalFit, reversed: true),
        "AI risk" => Scale(ArchiCatalog.AiRisk, s.AiRisk),
        _ => PhaseColor(s.PhaseOn(DateTime.Today))
    };

    private static readonly string[] Modes = ["Lifecycle", "Business criticality", "Functional fit", "Technical fit", "AI risk"];

    private static Control Legend(string mode)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 12);
        IEnumerable<(string Text, Color Color)> items = mode switch
        {
            "Business criticality" => ArchiCatalog.Criticality.Skip(1).Select(v => (v, Scale(ArchiCatalog.Criticality, v))),
            "Functional fit" or "Technical fit" => ArchiCatalog.Fit.Skip(1).Select(v => (v, Scale(ArchiCatalog.Fit, v, reversed: true))),
            "AI risk" => ArchiCatalog.AiRisk.Skip(1).Select(v => (v, Scale(ArchiCatalog.AiRisk, v))),
            _ => Enum.GetValues<ArchiPhase>().Select(p => (PhaseName(p), PhaseColor(p)))
        };
        foreach (var (text, color) in items.Append(("not set", new Color(0.55f, 0.55f, 0.58f))))
            row.AddChild(Ui.Row(4, new ColorRect { Color = color, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }, E.Text_(text, E.Muted, 12)));
        return row;
    }

    private static OptionButton Options(IEnumerable<string> values, string current, Action<string> chosen)
    {
        var list = values.ToList();
        var option = new OptionButton();
        foreach (var v in list)
            option.AddItem(v);
        option.Selected = Math.Max(0, list.IndexOf(current));
        option.ItemSelected += i => chosen(list[(int)i]);
        return option;
    }

    /// <summary>A small button for a fact sheet, in the colour given (the chips of the landscape).</summary>
    private static Button Chip(ArchiWindowViewModel vm, ArchiSheet s, Color color, Action<string> open)
    {
        var info = ArchiCatalog.Info(s.Type);
        var chip = new Button { Text = $"{info.Icon} {s.Name}", TooltipText = $"{info.Label}: {s.Name}\n{s.Description}", ClipText = true, Alignment = HorizontalAlignment.Left };
        chip.AddThemeFontSizeOverride("font_size", 12);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
            chip.AddThemeStyleboxOverride(state, E.Box(color with { A = state == "hover" ? 0.55f : 0.32f }, color, 4, 1, 4));
        chip.AddThemeColorOverride("font_color", E.Ink);
        chip.AddThemeColorOverride("font_hover_color", E.Ink);
        chip.Pressed += () => open(s.Id);
        return chip;
    }

    // ── LANDSCAPE ──

    private static string _landscapeMode = "Lifecycle";

    /// <summary>The capability map: the capabilities of level 1 side by side, those below them, and in each the applications, agents and multi-agent systems related to it.</summary>
    public static Control Landscape(ArchiWindowViewModel vm, Action<string> open)
    {
        var page = Ui.Column(10);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var holder = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var legend = new MarginContainer();
        void Draw()
        {
            foreach (var child in holder.GetChildren())
                child.QueueFree();
            foreach (var child in legend.GetChildren())
                child.QueueFree();
            legend.AddChild(Legend(_landscapeMode));
            var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            columns.AddThemeConstantOverride("separation", 12);
            var tops = vm.Sheets.Where(s => s.Type == ArchiType.BusinessCapability && (s.ParentId is null || vm.Find(s.ParentId) is null)).OrderBy(s => s.Name).ToList();
            if (tops.Count == 0)
            {
                holder.AddChild(Ui.Placeholder("No business capability yet: add some with ✚ NEW, then relate the applications and agents to them (supports)."));
                return;
            }
            foreach (var top in tops)
                columns.AddChild(CapabilityBox(vm, top, 0, open));
            var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            scroll.AddChild(columns);
            holder.AddChild(scroll);
        }
        page.AddChild(Ui.Row(10, E.Caption("Capability map", E.TextBright, 13), E.Text_("coloured by", E.Muted, 12),
            Options(Modes, _landscapeMode, mode => { _landscapeMode = mode; Draw(); })));
        page.AddChild(legend);
        page.AddChild(holder);
        Draw();
        return page;
    }

    private static Control CapabilityBox(ArchiWindowViewModel vm, ArchiSheet capability, int level, Action<string> open)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(level == 0 ? 300 : 0, 0), SizeFlagsVertical = level == 0 ? Control.SizeFlags.Fill : Control.SizeFlags.ShrinkBegin };
        var tint = Color.FromHtml(ArchiCatalog.Info(ArchiType.BusinessCapability).Color);
        box.AddThemeStyleboxOverride("panel", E.Box(tint with { A = level == 0 ? 0.14f : 0.08f }, tint with { A = 0.6f }, 8, 1, 8));
        var column = Ui.Column(6);
        var title = E.Button($"🏛 {capability.Name}", tooltip: capability.Description);
        title.Flat = true;
        title.Alignment = HorizontalAlignment.Left;
        title.AddThemeFontSizeOverride("font_size", level == 0 ? 15 : 13);
        title.Pressed += () => open(capability.Id);
        column.AddChild(title);
        var realizers = vm.Neighbours(capability.Id)
            .Where(s => s.Type is ArchiType.Application or ArchiType.Agent or ArchiType.MultiAgentSystem or ArchiType.Process)
            .OrderBy(s => s.Type).ThenBy(s => s.Name).ToList();
        foreach (var s in realizers)
            column.AddChild(Chip(vm, s, ModeColor(_landscapeMode, s), open));
        foreach (var child in vm.Children(capability))
            column.AddChild(CapabilityBox(vm, child, level + 1, open));
        if (realizers.Count == 0 && !vm.Children(capability).Any())
            column.AddChild(E.Text_("nothing supports it yet", E.Muted, 11));
        box.AddChild(column);
        return box;
    }

    // ── MATRIX ──

    private static ArchiType _rows = ArchiType.Application, _columns = ArchiType.BusinessCapability;

    /// <summary>Two kinds of fact sheets crossed: a dot where they are related (its kinds of relations in its tooltip).</summary>
    public static Control Matrix(ArchiWindowViewModel vm, Action<string> open)
    {
        var page = Ui.Column(10);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var holder = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var labels = ArchiCatalog.Types.Select(t => $"{t.Icon} {t.Plural}").ToList();
        void Draw()
        {
            foreach (var child in holder.GetChildren())
                child.QueueFree();
            var rows = vm.Sheets.Where(s => s.Type == _rows).OrderBy(vm.FullName).ToList();
            var cols = vm.Sheets.Where(s => s.Type == _columns).OrderBy(vm.FullName).ToList();
            if (rows.Count == 0 || cols.Count == 0)
            {
                holder.AddChild(Ui.Placeholder("No fact sheet of one of these kinds yet."));
                return;
            }
            var grid = new GridContainer { Columns = cols.Count + 1 };
            grid.AddThemeConstantOverride("h_separation", 2);
            grid.AddThemeConstantOverride("v_separation", 2);
            grid.AddChild(E.Text_("", E.Muted, 11));
            foreach (var c in cols)
            {
                var head = E.Button(c.Name, tooltip: vm.FullName(c));
                head.Flat = true;
                head.ClipText = true;
                head.CustomMinimumSize = new Vector2(110, 0);
                head.AddThemeFontSizeOverride("font_size", 11);
                head.Pressed += () => open(c.Id);
                grid.AddChild(head);
            }
            foreach (var r in rows)
            {
                var head = E.Button(vm.FullName(r), tooltip: r.Description);
                head.Flat = true;
                head.Alignment = HorizontalAlignment.Left;
                head.CustomMinimumSize = new Vector2(240, 0);
                head.ClipText = true;
                head.Pressed += () => open(r.Id);
                grid.AddChild(head);
                foreach (var c in cols)
                {
                    var kinds = vm.Relations.Where(x => (x.From == r.Id && x.To == c.Id) || (x.From == c.Id && x.To == r.Id)).Select(x => x.Kind).Distinct().ToList();
                    var cell = new PanelContainer { CustomMinimumSize = new Vector2(110, 28), TooltipText = kinds.Count > 0 ? $"{r.Name} — {string.Join(", ", kinds)} — {c.Name}" : "" };
                    cell.AddThemeStyleboxOverride("panel", E.Box(kinds.Count > 0 ? E.Cyan with { A = 0.28f } : E.Sunken(0.15f), kinds.Count > 0 ? E.Cyan : E.Edge with { A = 0.25f }, 2, 1, 3));
                    var mark = E.Text_(kinds.Count > 0 ? "● " + kinds[0] : "", E.Ink, 11);
                    mark.HorizontalAlignment = HorizontalAlignment.Center;
                    mark.MouseFilter = Control.MouseFilterEnum.Ignore;
                    cell.AddChild(mark);
                    grid.AddChild(cell);
                }
            }
            var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            scroll.AddChild(grid);
            holder.AddChild(scroll);
        }
        page.AddChild(Ui.Row(10, E.Caption("Matrix", E.TextBright, 13), E.Text_("rows", E.Muted, 12),
            Options(labels, labels[(int)_rows], v => { _rows = ArchiCatalog.Types[labels.IndexOf(v)].Type; Draw(); }),
            E.Text_("× columns", E.Muted, 12),
            Options(labels, labels[(int)_columns], v => { _columns = ArchiCatalog.Types[labels.IndexOf(v)].Type; Draw(); }),
            Ui.Action("⇄", () => { (_rows, _columns) = (_columns, _rows); Draw(); }, tooltip: "Swap the rows and the columns"),
            Ui.Spring(),
            Ui.Action("🤖 Agents × models", () => { _rows = ArchiType.Agent; _columns = ArchiType.AIModel; Draw(); }),
            Ui.Action("🧩 Applications × agents", () => { _rows = ArchiType.Application; _columns = ArchiType.Agent; Draw(); }),
            Ui.Action("🏛 Applications × capabilities", () => { _rows = ArchiType.Application; _columns = ArchiType.BusinessCapability; Draw(); })));
        page.AddChild(holder);
        Draw();
        return page;
    }

    // ── ROADMAP ──

    private static int _roadmapKind = -1;

    public static Control Roadmap(ArchiWindowViewModel vm, Action<string> open)
    {
        var page = Ui.Column(10);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var canvas = new RoadmapCanvas(vm, open) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        canvas.Kind = _roadmapKind;
        var kinds = new List<string> { "All kinds" };
        kinds.AddRange(ArchiCatalog.Types.Select(t => $"{t.Icon} {t.Plural}"));
        var legend = Legend("Lifecycle");
        page.AddChild(Ui.Row(10, E.Caption("Roadmap", E.TextBright, 13),
            Options(kinds, kinds[_roadmapKind + 1], v => { _roadmapKind = kinds.IndexOf(v) - 1; canvas.Kind = _roadmapKind; }),
            E.Text_("a double-click on a row opens its fact sheet", E.Muted, 12)));
        page.AddChild(legend);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(canvas);
        page.AddChild(scroll);
        return page;
    }

    // ── GRAPH ──

    private static int _depth = 1;

    public static Control Graph(ArchiWindowViewModel vm, Action<string> open)
    {
        var page = Ui.Column(10);
        page.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var canvas = new GraphCanvas(vm, open) { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Depth = _depth };
        var depth = Options(["Neighbours", "Neighbours of the neighbours"], _depth == 1 ? "Neighbours" : "Neighbours of the neighbours", v =>
        {
            _depth = v == "Neighbours" ? 1 : 2;
            canvas.Depth = _depth;
        });
        page.AddChild(Ui.Row(10, E.Caption("Graph", E.TextBright, 13), depth,
            E.Text_("a click centres a fact sheet, a double-click opens it", E.Muted, 12)));
        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", E.Box(E.Sunken(0.2f), E.Edge with { A = 0.4f }, 0));
        panel.AddChild(canvas);
        page.AddChild(panel);
        return page;
    }

    // ── AI ──

    /// <summary>The AI landscape: the counts of the AI fact sheets, the risks, the hosting of the models, each agent and multi-agent system with what it uses, and the warnings.</summary>
    public static Control Ai(ArchiWindowViewModel vm, Action<string> open)
    {
        var page = Ui.Column(14);
        var ai = vm.Sheets.Where(s => ArchiCatalog.Info(s.Type).IsAi).ToList();

        // Counts
        var cards = new HFlowContainer();
        cards.AddThemeConstantOverride("h_separation", 10);
        cards.AddThemeConstantOverride("v_separation", 10);
        foreach (var info in ArchiCatalog.Types.Where(t => t.IsAi))
        {
            var count = ai.Count(s => s.Type == info.Type);
            var card = new PanelContainer { CustomMinimumSize = new Vector2(150, 0) };
            var tint = Color.FromHtml(info.Color);
            card.AddThemeStyleboxOverride("panel", E.Box(tint with { A = 0.16f }, tint with { A = 0.7f }, 10, 1, 8));
            var number = E.Text_(count.ToString(), E.TextBright, 26);
            card.AddChild(Ui.Column(2, number, E.Text_($"{info.Icon} {info.Plural}", E.Muted, 12)));
            cards.AddChild(card);
        }
        page.AddChild(E.Caption("AI inventory", E.TextBright, 13));
        page.AddChild(cards);

        // Risks and hosting
        var bars = Ui.Row(30);
        bars.AddChild(Bars("Risk under the EU AI Act", ArchiCatalog.AiRisk.Skip(1).Append("").Select(r => (r.Length == 0 ? "not assessed" : r, ai.Count(s => s.AiRisk == r), r.Length == 0 ? new Color(0.55f, 0.55f, 0.58f) : Scale(ArchiCatalog.AiRisk, r)))));
        var models = ai.Where(s => s.Type == ArchiType.AIModel).ToList();
        bars.AddChild(Bars("Models by hosting", models.GroupBy(m => m.Field("hosting").Length > 0 ? m.Field("hosting") : "not set").OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count(), E.Cyan))));
        bars.AddChild(Bars("Models by modality", models.GroupBy(m => m.Field("modality").Length > 0 ? m.Field("modality") : "not set").OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count(), E.Amber))));
        bars.AddChild(Bars("Agents by autonomy", ai.Where(s => s.Type == ArchiType.Agent).GroupBy(a => a.Field("autonomy").Length > 0 ? a.Field("autonomy") : "not set").Select(g => (g.Key, g.Count(), Color.FromHtml("#AB47BC")))));
        page.AddChild(bars);

        // Agents and multi-agent systems with what they use
        page.AddChild(E.Caption("Agents and multi-agent systems", E.TextBright, 13));
        var table = new GridContainer { Columns = 6 };
        table.AddThemeConstantOverride("h_separation", 14);
        table.AddThemeConstantOverride("v_separation", 6);
        foreach (var head in new[] { "Agent / system", "Models", "Tools, MCP, APIs", "Knowledge, prompts", "Agents (orchestrated / delegated)", "Guardrails, evaluations" })
            table.AddChild(E.Caption(head, E.Muted, 11));
        string Names(ArchiSheet s, params ArchiType[] types) =>
            string.Join(", ", vm.Neighbours(s.Id).Where(n => types.Contains(n.Type)).Select(n => n.Name).OrderBy(n => n)) is { Length: > 0 } names ? names : "—";
        foreach (var agent in ai.Where(s => s.Type is ArchiType.MultiAgentSystem or ArchiType.Agent).OrderByDescending(s => s.Type).ThenBy(s => s.Name))
        {
            var name = E.Button($"{ArchiCatalog.Info(agent.Type).Icon} {agent.Name}", tooltip: agent.Description);
            name.Flat = true;
            name.Alignment = HorizontalAlignment.Left;
            name.Pressed += () => open(agent.Id);
            table.AddChild(name);
            foreach (var text in new[]
                     {
                         Names(agent, ArchiType.AIModel), Names(agent, ArchiType.Tool, ArchiType.McpServer, ArchiType.RestApi),
                         Names(agent, ArchiType.KnowledgeBase, ArchiType.Prompt, ArchiType.Dataset, ArchiType.VectorStore),
                         Names(agent, ArchiType.Agent, ArchiType.MultiAgentSystem), Names(agent, ArchiType.Guardrail, ArchiType.Evaluation)
                     })
            {
                var label = E.Text_(text, text == "—" ? E.Muted : E.Text, 12, wrap: true);
                label.CustomMinimumSize = new Vector2(190, 0);
                table.AddChild(label);
            }
        }
        page.AddChild(table);

        // Warnings
        page.AddChild(E.Caption("Warnings", E.TextBright, 13));
        var warnings = Warnings(vm, ai).ToList();
        if (warnings.Count == 0)
            page.AddChild(E.Text_("✓ Nothing to report.", E.Green, 13));
        foreach (var (text, id) in warnings)
        {
            var warning = E.Button("⚠ " + text);
            warning.Flat = true;
            warning.Alignment = HorizontalAlignment.Left;
            warning.AddThemeColorOverride("font_color", E.Amber);
            warning.Pressed += () => open(id);
            page.AddChild(warning);
        }

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(Ui.Margin(page, 4));
        return scroll;
    }

    private static IEnumerable<(string Text, string Id)> Warnings(ArchiWindowViewModel vm, List<ArchiSheet> ai)
    {
        var today = DateTime.Today;
        foreach (var s in ai.Where(s => s.Type is ArchiType.AIModel or ArchiType.Agent or ArchiType.MultiAgentSystem && s.AiRisk.Length == 0))
            yield return ($"{s.Name}: its risk under the EU AI Act is not assessed", s.Id);
        foreach (var s in ai.Where(s => s.AiRisk is "High" or "Unacceptable" && !vm.Neighbours(s.Id).Any(n => n.Type == ArchiType.Guardrail)))
            yield return ($"{s.Name}: a {s.AiRisk.ToLowerInvariant()} risk and no guardrail", s.Id);
        foreach (var s in ai.Where(s => s.Type == ArchiType.Agent && s.Field("autonomy").StartsWith("Autonomous") && s.Field("human") != "Yes"
                                        && !vm.Neighbours(s.Id).Any(n => n.Type == ArchiType.Guardrail)))
            yield return ($"{s.Name}: acts alone, without a human in the loop nor a guardrail", s.Id);
        foreach (var s in ai.Where(s => s.Type is ArchiType.Agent or ArchiType.MultiAgentSystem && !vm.Neighbours(s.Id).Any(n => n.Type == ArchiType.Evaluation)))
            yield return ($"{s.Name}: never evaluated", s.Id);
        foreach (var s in ai.Where(s => s.Type == ArchiType.Agent && !vm.Neighbours(s.Id).Any(n => n.Type == ArchiType.AIModel)))
            yield return ($"{s.Name}: no model related", s.Id);
        foreach (var model in ai.Where(s => s.Type == ArchiType.AIModel && s.PhaseOn(today) is ArchiPhase.PhaseOut or ArchiPhase.EndOfLife))
            foreach (var user in vm.Neighbours(model.Id).Where(n => n.Type is ArchiType.Agent or ArchiType.Application && n.PhaseOn(today) == ArchiPhase.Active))
                yield return ($"{user.Name} uses {model.Name}, which is {PhaseName(model.PhaseOn(today)!.Value).ToLowerInvariant()}", user.Id);
        foreach (var s in vm.Sheets.Where(s => s.Type == ArchiType.Dataset && s.Field("personal") == "Yes" && !vm.Neighbours(s.Id).Any(n => n.Type == ArchiType.Guardrail)))
            yield return ($"{s.Name}: personal data and no guardrail", s.Id);
    }

    private static Control Bars(string title, IEnumerable<(string Label, int Count, Color Color)> values)
    {
        var list = values.Where(v => v.Count > 0).ToList();
        var column = Ui.Column(4, E.Caption(title, E.Muted, 11));
        column.CustomMinimumSize = new Vector2(280, 0);
        var max = Math.Max(1, list.Count == 0 ? 1 : list.Max(v => v.Count));
        if (list.Count == 0)
            column.AddChild(E.Text_("—", E.Muted, 12));
        foreach (var (label, count, color) in list)
        {
            var name = E.Text_(label, E.Text, 12);
            name.CustomMinimumSize = new Vector2(150, 0);
            name.ClipText = true;
            var bar = new ColorRect { Color = color, CustomMinimumSize = new Vector2(110f * count / max, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            column.AddChild(Ui.Row(6, name, bar, E.Text_(count.ToString(), E.Muted, 12)));
        }
        return column;
    }
}

/// <summary>The roadmap: one row per fact sheet with a lifecycle, its phases as coloured bars on a timeline, today as a line.</summary>
public sealed partial class RoadmapCanvas : Control
{
    private const float NameWidth = 300, RowHeight = 24, Top = 30;
    private readonly ArchiWindowViewModel _vm = null!;
    private readonly Action<string> _open = null!;
    private List<ArchiSheet> _rows = [];
    private DateTime _from, _to;
    private int _kind = -1;

    // Godot builds its scripts with this constructor.
    public RoadmapCanvas()
    {
    }

    public RoadmapCanvas(ArchiWindowViewModel vm, Action<string> open)
    {
        _vm = vm;
        _open = open;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>The kind shown (its index in <see cref="ArchiCatalog.Types"/>), -1 for all.</summary>
    public int Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Layout();
        }
    }

    private void Layout()
    {
        _rows = _vm.Sheets.Where(s => s.Lifecycle.Count > 0 && (_kind < 0 || s.Type == ArchiCatalog.Types[_kind].Type))
            .OrderBy(s => s.Type).ThenBy(s => s.StartOf(ArchiPhase.Plan) ?? DateTime.MaxValue).ThenBy(s => s.Name).ToList();
        var dates = _rows.SelectMany(s => Enum.GetValues<ArchiPhase>().Select(s.StartOf)).OfType<DateTime>().ToList();
        var first = dates.Count > 0 ? dates.Min() : DateTime.Today;
        var last = dates.Count > 0 ? dates.Max() : DateTime.Today;
        _from = new DateTime(Math.Min(first.Year, DateTime.Today.Year), 1, 1);
        _to = new DateTime(Math.Max(last.Year, DateTime.Today.Year) + 2, 1, 1);
        CustomMinimumSize = new Vector2(0, Top + Math.Max(1, _rows.Count) * RowHeight + 10);
        QueueRedraw();
    }

    private float X(DateTime day) => NameWidth + (float)((day - _from).TotalDays / (_to - _from).TotalDays) * Math.Max(100, Size.X - NameWidth - 10);

    public override void _Draw()
    {
        var font = E.Body;
        if (_rows.Count == 0)
        {
            DrawString(font, new Vector2(10, Top + 16), "No fact sheet of this kind has a lifecycle.", HorizontalAlignment.Left, -1, 13, E.Muted);
            return;
        }
        // Years
        for (var year = _from.Year; year <= _to.Year; year++)
        {
            var x = X(new DateTime(year, 1, 1));
            DrawLine(new Vector2(x, Top - 6), new Vector2(x, Size.Y), E.Edge with { A = 0.35f });
            DrawString(font, new Vector2(x + 4, Top - 10), year.ToString(), HorizontalAlignment.Left, -1, 12, E.Muted);
        }
        // Rows
        for (var i = 0; i < _rows.Count; i++)
        {
            var s = _rows[i];
            var y = Top + i * RowHeight;
            if (i % 2 == 0)
                DrawRect(new Rect2(0, y, Size.X, RowHeight), E.Raised(0.025f));
            var info = ArchiCatalog.Info(s.Type);
            DrawString(font, new Vector2(8, y + 16), $"{info.Icon} {s.Name}", HorizontalAlignment.Left, NameWidth - 16, 12, E.Text);
            var phases = Enum.GetValues<ArchiPhase>().Where(p => s.StartOf(p) is not null).ToList();
            for (var k = 0; k < phases.Count; k++)
            {
                var start = s.StartOf(phases[k])!.Value;
                var end = k + 1 < phases.Count ? s.StartOf(phases[k + 1])!.Value : phases[k] == ArchiPhase.EndOfLife ? start.AddMonths(2) : _to;
                var x0 = X(start);
                var x1 = Math.Max(x0 + 2, X(end));
                var color = ArchiReports.PhaseColor(phases[k]);
                DrawRect(new Rect2(x0, y + 5, x1 - x0, RowHeight - 10), color with { A = phases[k] == ArchiPhase.Plan ? 0.45f : 0.85f });
            }
        }
        // Today
        var today = X(DateTime.Today);
        DrawLine(new Vector2(today, Top - 6), new Vector2(today, Size.Y), E.Red, 2);
        DrawString(font, new Vector2(today + 4, Size.Y - 4), "today", HorizontalAlignment.Left, -1, 11, E.Red);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true } click)
        {
            var row = (int)((click.Position.Y - Top) / RowHeight);
            if (row >= 0 && row < _rows.Count)
                _open(_rows[row].Id);
        }
    }
}

/// <summary>The graph: the fact sheet selected in the middle, its neighbours around it, theirs further out, with their relations.</summary>
public sealed partial class GraphCanvas : Control
{
    private readonly ArchiWindowViewModel _vm = null!;
    private readonly Action<string> _open = null!;
    private readonly Dictionary<string, Vector2> _places = [];
    private int _depth = 1;
    private const float NodeWidth = 190, NodeHeight = 34;

    // Godot builds its scripts with this constructor.
    public GraphCanvas()
    {
    }

    public GraphCanvas(ArchiWindowViewModel vm, Action<string> open)
    {
        _vm = vm;
        _open = open;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        // Nothing selected, or a fact sheet without relations: the graph starts from the most related one.
        if (_vm.Selected is null || !vm.Neighbours(_vm.SelectedId!).Any())
            _vm.SelectedId = vm.Sheets.OrderByDescending(s => vm.Neighbours(s.Id).Count()).FirstOrDefault()?.Id ?? _vm.SelectedId;
    }

    public int Depth
    {
        get => _depth;
        set
        {
            _depth = value;
            QueueRedraw();
        }
    }

    private void Place()
    {
        _places.Clear();
        if (_vm.Selected is not { } centre)
            return;
        var middle = Size / 2;
        _places[centre.Id] = middle;
        var ring1 = _vm.Neighbours(centre.Id).OrderBy(s => s.Type).ThenBy(s => s.Name).ToList();
        var radius1 = Math.Min(Size.X, Size.Y) * (_depth == 1 ? 0.38f : 0.25f);
        for (var i = 0; i < ring1.Count; i++)
        {
            var angle = Mathf.Tau * i / ring1.Count - Mathf.Pi / 2;
            _places[ring1[i].Id] = middle + new Vector2(MathF.Cos(angle) * radius1 * 1.6f, MathF.Sin(angle) * radius1);
        }
        if (_depth < 2)
            return;
        var ring2 = ring1.SelectMany(n => _vm.Neighbours(n.Id).Select(o => (Parent: n, Node: o)))
            .Where(p => !_places.ContainsKey(p.Node.Id)).GroupBy(p => p.Node.Id).Select(g => g.First()).Take(60).ToList();
        var radius2 = Math.Min(Size.X, Size.Y) * 0.45f;
        for (var i = 0; i < ring2.Count; i++)
        {
            var angle = Mathf.Tau * i / Math.Max(1, ring2.Count) - Mathf.Pi / 2;
            _places[ring2[i].Node.Id] = middle + new Vector2(MathF.Cos(angle) * radius2 * 1.7f, MathF.Sin(angle) * radius2);
        }
    }

    public override void _Draw()
    {
        Place();
        var font = E.Body;
        if (_places.Count == 0)
        {
            DrawString(font, new Vector2(20, 30), "Select a fact sheet in the inventory to see it here with its neighbours.", HorizontalAlignment.Left, -1, 13, E.Muted);
            return;
        }
        foreach (var relation in _vm.Relations)
        {
            if (!_places.TryGetValue(relation.From, out var a) || !_places.TryGetValue(relation.To, out var b))
                continue;
            var central = relation.From == _vm.SelectedId || relation.To == _vm.SelectedId;
            DrawLine(a, b, (central ? E.Cyan : E.Edge) with { A = central ? 0.7f : 0.35f }, central ? 2 : 1, true);
            // The arrow head at the end, outside the node.
            var direction = (b - a).Normalized();
            var tip = b - direction * (NodeHeight * 0.75f);
            var side = new Vector2(-direction.Y, direction.X) * 5;
            DrawColoredPolygon([tip, tip - direction * 10 + side, tip - direction * 10 - side], (central ? E.Cyan : E.Edge) with { A = 0.8f });
            var mid = (a + b) / 2;
            DrawString(font, mid + new Vector2(-40, -4), relation.Kind, HorizontalAlignment.Center, 80, 10, E.Muted);
        }
        foreach (var (id, at) in _places)
        {
            if (_vm.Find(id) is not { } s)
                continue;
            var info = ArchiCatalog.Info(s.Type);
            var tint = Color.FromHtml(info.Color);
            var rect = new Rect2(at - new Vector2(NodeWidth, NodeHeight) / 2, new Vector2(NodeWidth, NodeHeight));
            var selected = id == _vm.SelectedId;
            DrawStyleBox(E.Box(E.PanelSolid, selected ? E.Cyan : tint, 0, selected ? 3 : 2, 8), rect);
            DrawRect(new Rect2(rect.Position + new Vector2(2, 2), new Vector2(6, NodeHeight - 4)), tint);
            DrawString(font, rect.Position + new Vector2(14, 22), $"{info.Icon} {s.Name}", HorizontalAlignment.Left, NodeWidth - 20, 12, E.TextBright);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return;
        foreach (var (id, at) in _places)
        {
            if (!new Rect2(at - new Vector2(NodeWidth, NodeHeight) / 2, new Vector2(NodeWidth, NodeHeight)).HasPoint(click.Position))
                continue;
            if (click.DoubleClick)
                _open(id);
            else
            {
                _vm.SelectedId = id;
                QueueRedraw();
            }
            AcceptEvent();
            return;
        }
    }
}
