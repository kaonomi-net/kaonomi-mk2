using System;
using System.Linq;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Writing;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Support;

/// <summary>
/// /support: the request for support. On the left the form: the title, the category, the priority, the requester and
/// how to reach them, the description, and the products concerned: the usual ones (those of the requests made before,
/// the most asked first, then a few common ones) as buttons to toggle, and one or several fact sheets of /archi
/// (applications, IT components, AI models, agents, MCP servers, REST APIs…), searched and filtered by kind; SEND keeps
/// the ticket and creates its task in the kanban, project Support. On the right the requests made, the newest first,
/// with their task (⟳ creates it again when the server did not answer).
/// </summary>
[ToolView(typeof(SupportWindowViewModel), "Support", "🛟", 1280, 860)]
public sealed class SupportView : IToolView
{
    public Control Build(object viewModel, Shell shell) => new SupportPage((SupportWindowViewModel)viewModel, shell);
}

public sealed partial class SupportPage : VBoxContainer
{
    private readonly SupportWindowViewModel _vm = null!;
    private readonly HFlowContainer _usual = new();
    private readonly VBoxContainer _archi = new();
    private readonly VBoxContainer _tickets = new();
    private readonly Label _chosen = E.Text_("", E.Muted, 12, wrap: true);
    private string _search = "";
    private ArchiType? _type;

    // Godot builds its scripts with this constructor.
    public SupportPage()
    {
    }

    public SupportPage(SupportWindowViewModel vm, Shell shell)
    {
        _vm = vm;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);

        AddChild(Ui.Toolbar(Ui.Row(8,
            E.Caption("Request support", E.TextBright, 14),
            E.Text_("each request becomes a task of the kanban, project Support", E.Muted, 12),
            Ui.Spring(),
            Ui.Action("📋 KANBAN", () => _vm.OpenKanban?.Invoke(), tooltip: "The kanban of the tasks"),
            Ui.Action("🏛 ARCHI", () => _vm.OpenArchi?.Invoke(), tooltip: "The architecture repository, where the products come from"))));

        // ── The form ──
        var grid = Ui.Grid(2, 12, 8);
        grid.AddChild(Ui.Field("Title *", Ui.Line(vm, nameof(vm.Title), "What is wrong, or what is needed, in a few words")));
        grid.AddChild(Ui.Field("Category", WritingUi.Choice(vm, nameof(vm.Category), SupportWindowViewModel.Categories)));
        grid.AddChild(Ui.Field("Requester", Ui.Line(vm, nameof(vm.Requester), "Who asks")));
        grid.AddChild(Ui.Field("Priority", WritingUi.Choice(vm, nameof(vm.Priority), SupportWindowViewModel.Priorities)));
        grid.AddChild(Ui.Field("Contact", Ui.Line(vm, nameof(vm.Contact), "email, phone, office…")));
        var description = Ui.Field("Description", Ui.Lines(vm, nameof(vm.Description), 110, "What happens, since when, what was tried, the message shown…"));

        _usual.AddThemeConstantOverride("h_separation", 6);
        _usual.AddThemeConstantOverride("v_separation", 6);

        var search = new LineEdit { PlaceholderText = "🔎 Search a product of the architecture", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        search.TextChanged += text =>
        {
            _search = text.Trim();
            FillArchi();
        };
        var kinds = new OptionButton { TooltipText = "The kind of fact sheet" };
        kinds.AddItem("All kinds");
        foreach (var type in SupportWindowViewModel.ProductTypes)
        {
            var info = ArchiCatalog.Info(type);
            kinds.AddItem($"{info.Icon} {info.Plural}");
        }
        kinds.ItemSelected += i =>
        {
            _type = i == 0 ? null : SupportWindowViewModel.ProductTypes[(int)i - 1];
            FillArchi();
        };
        var archiScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _archi.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        archiScroll.AddChild(_archi);
        var archiPanel = new PanelContainer();
        archiPanel.AddThemeStyleboxOverride("panel", E.Box(E.Sunken(0.15f), E.Edge with { A = 0.35f }, 8));
        archiPanel.AddChild(archiScroll);

        var send = Ui.Action("🛟 SEND THE REQUEST", () => _ = _vm.SendAsync(), primary: true, tooltip: "Keeps the ticket and creates its task in the kanban, project Support");
        Bind.When(send, vm, () => send.Disabled = vm.IsBusy, nameof(vm.IsBusy));

        var form = Ui.Column(10,
            grid,
            description,
            E.Caption("Products concerned *", E.TextBright, 12),
            E.Caption("Usual (asked before, then common)", E.Muted, 11),
            _usual,
            E.Caption("From the architecture (/archi): one or several", E.Muted, 11),
            Ui.Row(8, search, kinds),
            archiPanel,
            _chosen);
        form.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var formScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var formMargin = Ui.Margin(form, 4, 4, 12, 4);
        formMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        formScroll.AddChild(formMargin);

        // ── The requests made ──
        var ticketsScroll = new ScrollContainer { CustomMinimumSize = new Vector2(400, 0), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tickets.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _tickets.AddThemeConstantOverride("separation", 8);
        ticketsScroll.AddChild(_tickets);
        var right = Ui.Column(8, E.Caption("Requests made", E.TextBright, 12), ticketsScroll);
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        // A fixed width: the form takes the rest.
        right.SizeFlagsHorizontal = SizeFlags.Fill;
        right.CustomMinimumSize = new Vector2(400, 0);

        // SEND stays in view under the form, whatever its scroll.
        var left = Ui.Column(8, formScroll, Ui.Row(8, Ui.Spring(), send));
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsVertical = SizeFlags.ExpandFill;
        var body = Ui.Row(14, left, right);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(body);
        AddChild(Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError), nameof(vm.IsBusy)));

        _vm.Changed += Refresh;
        Bind.OnGone(this, () => _vm.Changed -= Refresh);
        Refresh();
    }

    private void Refresh()
    {
        Bind.OnMain(() =>
        {
            if (!IsInstanceValid(this))
                return;
            FillUsual();
            FillArchi();
            FillTickets();
            var chosen = _vm.ChosenNames();
            _chosen.Text = chosen.Count == 0 ? "No product chosen yet." : "Chosen: " + string.Join(", ", chosen);
            _chosen.AddThemeColorOverride("font_color", chosen.Count == 0 ? E.Muted : E.Cyan);
        });
    }

    private void FillUsual()
    {
        foreach (var child in _usual.GetChildren())
            child.QueueFree();
        foreach (var (name, count) in _vm.UsualProducts)
        {
            var button = E.Button(count > 0 ? $"{name}  ·{count}" : name, tooltip: count > 0 ? $"Asked {count} time(s) before" : "A product asked for often");
            button.ToggleMode = true;
            button.SetPressedNoSignal(_vm.ChosenUsual.Contains(name));
            if (button.ButtonPressed)
                E.StylePrimary(button);
            var product = name;
            button.Toggled += on => _vm.Toggle(product, on);
            _usual.AddChild(button);
        }
    }

    private void FillArchi()
    {
        foreach (var child in _archi.GetChildren())
            child.QueueFree();
        if (_vm.Archi.Sheets.Count == 0)
        {
            _archi.AddChild(E.Text_("The architecture repository is empty: open /archi (🏛 ARCHI above) to describe the products, then open /support again.", E.Muted, 12, wrap: true));
            return;
        }
        var products = _vm.ArchiProducts(_type, _search);
        if (products.Count == 0)
            _archi.AddChild(E.Text_("No product of the architecture matches.", E.Muted, 12));
        foreach (var sheet in products)
        {
            var info = ArchiCatalog.Info(sheet.Type);
            var check = new CheckBox { Text = $"{info.Icon} {sheet.Name}", ButtonPressed = _vm.ChosenArchi.Contains(sheet.Id), TooltipText = $"{info.Label}\n{sheet.Description}" };
            var kind = E.Text_(info.Label + (sheet.Owner.Length > 0 ? $" · {sheet.Owner}" : ""), E.Muted, 11);
            var id = sheet.Id;
            check.Toggled += on => _vm.ToggleArchi(id, on);
            _archi.AddChild(Ui.Row(8, check, Ui.Spring(), kind));
        }
    }

    private void FillTickets()
    {
        foreach (var child in _tickets.GetChildren())
            child.QueueFree();
        if (_vm.Tickets.Count == 0)
            _tickets.AddChild(E.Text_("No request yet.", E.Muted, 12));
        foreach (var ticket in _vm.Tickets.Take(100))
        {
            var card = new PanelContainer();
            var tint = ticket.Priority switch { "Critical" => E.Red, "High" => E.Amber, _ => E.Edge };
            card.AddThemeStyleboxOverride("panel", E.Box(E.Raised(0.03f), tint with { A = 0.6f }, 10, 1, 8));
            var head = Ui.Row(6, E.Caption(ticket.Id, E.Cyan, 11), Ui.Spring(), E.Text_($"{ticket.Created:yyyy-MM-dd HH:mm}", E.Muted, 11));
            var title = E.Text_(ticket.Title, E.TextBright, 13, wrap: true);
            var meta = E.Text_($"{ticket.Category} · {ticket.Priority} · {string.Join(", ", ticket.Products)}", E.Muted, 11, wrap: true);
            var column = Ui.Column(4, head, title, meta);
            if (ticket.TaskId is not null)
                column.AddChild(E.Text_("✓ task in the kanban (project Support)", E.Green, 11));
            else
            {
                var current = ticket;
                column.AddChild(Ui.Row(6, E.Text_("✕ no task yet", E.Red, 11), Ui.Spring(),
                    Ui.Action("⟳ CREATE THE TASK", () => _ = _vm.CreateTaskAsync(current), tooltip: "Creates its task in the kanban again")));
            }
            card.AddChild(column);
            _tickets.AddChild(card);
        }
    }
}
