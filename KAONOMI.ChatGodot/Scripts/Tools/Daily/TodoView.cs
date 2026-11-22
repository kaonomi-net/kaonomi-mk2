using System;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using E = KAONOMI.ChatGodot.Eve.Eve;

namespace KAONOMI.ChatGodot.Tools.Daily;

/// <summary>
/// /todo: the tasks of the day, each a title and a text, to do or done (its box). → TOMORROW puts a task off to the next
/// day, PUT OFF THE REST moves every task not done to tomorrow; the tasks put off are listed under the list, ↩ brings one
/// back to today. A task is not removed by hand: those of the days gone are marked deleted at the first opening of the
/// next day (but those put off to it). The same page is the popup of the first opening of the day.
/// </summary>
[ToolView(typeof(TodoWindowViewModel), "To do today", "✅", 760, 640)]
public sealed class TodoView : IToolView
{
    public Control Build(object viewModel, Shell shell) => new TodoPage((TodoWindowViewModel)viewModel);
}

public sealed partial class TodoPage : VBoxContainer
{
    private readonly TodoWindowViewModel _vm = null!;
    private readonly VBoxContainer _list = new();
    private readonly VBoxContainer _tomorrow = new();
    private readonly Label _summary = E.Text_("", E.Muted, 12);
    private readonly LineEdit _title = new() { PlaceholderText = "A task (Enter adds it)", SizeFlagsHorizontal = SizeFlags.ExpandFill };

    // Godot builds its scripts with this constructor.
    public TodoPage()
    {
    }

    public TodoPage(TodoWindowViewModel vm)
    {
        _vm = vm;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);

        AddChild(E.Caption(vm.IsPlanning ? "Good morning · what do you plan to do today?" : "To do today", E.TextBright, 14));
        var intro = E.Text_(vm.DayTitle + (vm.IsPlanning ? " · type the tasks of your day, one by one; the tasks of the days before are gone, those put off are here." : ""), E.Muted, 12, wrap: true);
        // A width for the wrapped text (else, in the popup, it is as narrow as a word and as tall as the screen).
        intro.CustomMinimumSize = new Vector2(600, 0);
        AddChild(intro);

        // ── A new task ──
        var text = new LineEdit { PlaceholderText = "Its text (optional)", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        void Add()
        {
            _vm.NewTitle = _title.Text;
            _vm.NewText = text.Text;
            if (!_vm.Add())
                return;
            _title.Text = text.Text = "";
            _title.GrabFocus();
        }
        _title.TextSubmitted += _ => Add();
        text.TextSubmitted += _ => Add();
        var add = Ui.Action("✚ ADD", Add, primary: true, tooltip: "Adds the task to today (Enter)");
        var form = Ui.Row(8, _title, text, add);
        form.GetChild<Control>(0).SizeFlagsStretchRatio = 1;
        form.GetChild<Control>(1).SizeFlagsStretchRatio = 1.6f;
        AddChild(form);

        // ── The list ──
        _list.AddThemeConstantOverride("separation", 6);
        _tomorrow.AddThemeConstantOverride("separation", 4);
        var content = Ui.Column(12, _list, _tomorrow);
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 240) };
        scroll.AddChild(content);
        AddChild(scroll);

        var postpone = Ui.Action("→ PUT OFF THE REST TO TOMORROW", _vm.PostponeUndone, tooltip: "Every task not done goes to tomorrow");
        AddChild(Ui.Row(8, _summary, Ui.Spring(), postpone));
        AddChild(Ui.Label(vm, nameof(vm.Status), E.Muted, 11));

        DayTodoStore.Changed += OnChanged;
        Bind.OnGone(this, () => DayTodoStore.Changed -= OnChanged);
        Fill();
        Callable.From(() =>
        {
            if (IsInstanceValid(_title) && _title.IsVisibleInTree())
                _title.GrabFocus();
        }).CallDeferred();
    }

    private void OnChanged() => Bind.OnMain(() =>
    {
        if (IsInstanceValid(this))
            Fill();
    });

    private void Fill()
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        foreach (var child in _tomorrow.GetChildren())
            child.QueueFree();
        var today = _vm.Today;
        if (today.Count == 0)
            _list.AddChild(E.Text_(_vm.IsPlanning ? "Nothing planned yet: type your first task above." : "Nothing planned today: type a task above.", E.Muted, 13));
        foreach (var todo in today)
            _list.AddChild(Row(todo));
        _summary.Text = _vm.Summary;

        var tomorrow = _vm.Tomorrow;
        if (tomorrow.Count == 0)
            return;
        _tomorrow.AddChild(E.Caption($"Put off to tomorrow ({tomorrow.Count})", E.Muted, 11));
        foreach (var todo in tomorrow)
        {
            var title = E.Text_(todo.Title + (todo.Text.Length > 0 ? " — " + todo.Text : ""), E.Muted, 12);
            title.ClipText = true;
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var id = todo.Id;
            _tomorrow.AddChild(Ui.Row(6, title, Ui.Action("↩ TODAY", () => _vm.BackToToday(id), tooltip: "Back to the tasks of today")));
        }
    }

    /// <summary>A task: its box (done), its title and its text (changed when the field is left or Enter), → TOMORROW.</summary>
    private Control Row(DayTodo todo)
    {
        var id = todo.Id;
        var done = new CheckBox { ButtonPressed = todo.IsDone, TooltipText = todo.IsDone ? "Done: untick to do it again" : "Tick when it is done" };
        done.Toggled += on => _vm.SetDone(id, on);
        var color = todo.IsDone ? E.Muted : E.TextBright;
        var title = new LineEdit { Text = todo.Title, SizeFlagsHorizontal = SizeFlags.ExpandFill, Flat = true };
        title.AddThemeColorOverride("font_color", color);
        title.AddThemeFontSizeOverride("font_size", 14);
        title.TextSubmitted += value => _vm.SetTitle(id, value);
        title.FocusExited += () =>
        {
            if (title.Text != todo.Title)
                _vm.SetTitle(id, title.Text);
        };
        var text = new LineEdit { Text = todo.Text, PlaceholderText = "text", SizeFlagsHorizontal = SizeFlags.ExpandFill, Flat = true };
        text.AddThemeColorOverride("font_color", todo.IsDone ? E.Muted : E.Text);
        text.TextSubmitted += value => _vm.SetText(id, value);
        text.FocusExited += () =>
        {
            if (text.Text != todo.Text)
                _vm.SetText(id, text.Text);
        };
        var fields = Ui.Row(8, title, text);
        fields.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fields.GetChild<Control>(1).SizeFlagsStretchRatio = 1.6f;
        var row = Ui.Row(6, done, fields);
        if (!todo.IsDone)
            row.AddChild(Ui.Action("→ TOMORROW", () => _vm.Postpone(id), tooltip: "Put it off to tomorrow"));
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", E.Box(todo.IsDone ? E.Green with { A = 0.08f } : E.Raised(0.03f), (todo.IsDone ? E.Green : E.Edge) with { A = 0.5f }, 6, 1, 6));
        panel.AddChild(row);
        if (todo.Postponed > 0)
            panel.TooltipText = $"Put off {todo.Postponed} time(s) before";
        return panel;
    }
}
