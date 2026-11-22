using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.UI.Desk;

namespace KAONOMI.UI.Tools.Daily;

/// <summary>
/// /todo: the tasks of the day, each a title and a text, to do or done (its box). → TOMORROW puts a task off to the next
/// day, PUT OFF THE REST moves every task not done to tomorrow; the tasks put off are listed under the list, ↩ brings one
/// back to today. A task is not removed by hand: those of the days gone are marked deleted at the first opening of the
/// next day (but those put off to it). The same page is the popup of the first opening of the day
/// (<see cref="PlanTheDay"/>). The tasks are those of the Godot chat (<see cref="DayTodoStore"/>).
/// </summary>
[ToolView(typeof(TodoWindowViewModel), "To do today", "✅", 760, 640)]
public sealed class TodoView : IToolView
{
    public Control Build(object viewModel, Shell shell) => Page((TodoWindowViewModel)viewModel);

    /// <summary>The popup of the morning, over <paramref name="owner"/>.</summary>
    public static void PlanTheDay(Window owner)
    {
        var window = new Window
        {
            Title = "Plan your day",
            Width = 780,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Border { Padding = new Thickness(18), Child = Page(new TodoWindowViewModel(planning: true)) }
        };
        _ = window.ShowDialog(owner);
    }

    public static Control Page(TodoWindowViewModel vm)
    {
        var list = new StackPanel { Spacing = 6 };
        var tomorrow = new StackPanel { Spacing = 4, Margin = new Thickness(0, 12, 0, 0) };
        var summary = Ui.Label("", Ui.Muted, 12);
        var status = Ui.Bound(vm, nameof(vm.Status), Ui.Muted, 11);

        var title = new TextBox { Watermark = "A task (Enter adds it)" };
        var text = new TextBox { Watermark = "Its text (optional)" };
        void Add()
        {
            vm.NewTitle = title.Text ?? "";
            vm.NewText = text.Text ?? "";
            if (!vm.Add())
                return;
            title.Text = text.Text = "";
            title.Focus();
        }
        foreach (var box in new[] { title, text })
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                e.Handled = true;
                Add();
            };
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,1.6*,8,Auto") };
        form.Children.Add(title);
        Grid.SetColumn(text, 2);
        form.Children.Add(text);
        var add = Ui.Button("✚ ADD", Add, "Adds the task to today (Enter)", primary: true);
        Grid.SetColumn(add, 4);
        form.Children.Add(add);

        void Fill()
        {
            list.Children.Clear();
            tomorrow.Children.Clear();
            var today = vm.Today;
            if (today.Count == 0)
                list.Children.Add(Ui.Label(vm.IsPlanning ? "Nothing planned yet: type your first task above." : "Nothing planned today: type a task above.", Ui.Muted, 13));
            foreach (var todo in today)
                list.Children.Add(Row(vm, todo));
            summary.Text = vm.Summary;
            var later = vm.Tomorrow;
            if (later.Count == 0)
                return;
            tomorrow.Children.Add(Ui.Caption($"Put off to tomorrow ({later.Count})"));
            foreach (var todo in later)
            {
                var id = todo.Id;
                var row = new DockPanel();
                var back = Ui.Button("↩ TODAY", () => vm.BackToToday(id), "Back to the tasks of today");
                DockPanel.SetDock(back, Dock.Right);
                row.Children.Add(back);
                row.Children.Add(Ui.Label(todo.Title + (todo.Text.Length > 0 ? " — " + todo.Text : ""), Ui.Muted, 12));
                tomorrow.Children.Add(row);
            }
        }

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var postpone = Ui.Button("→ PUT OFF THE REST TO TOMORROW", vm.PostponeUndone, "Every task not done goes to tomorrow");
        DockPanel.SetDock(postpone, Dock.Right);
        footer.Children.Add(postpone);
        footer.Children.Add(summary);

        var header = Ui.Column(4,
            Ui.Caption(vm.IsPlanning ? "Good morning · what do you plan to do today?" : "To do today", Ui.TextBright, 14),
            Ui.Label(vm.DayTitle + (vm.IsPlanning ? " · type the tasks of your day, one by one; the tasks of the days before are gone, those put off are here." : ""), Ui.Muted, 12, wrap: true),
            form);
        header.Margin = new Thickness(0, 0, 0, 10);
        var bottom = Ui.Column(4, footer, status);
        var page = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        page.Children.Add(header);
        page.Children.Add(bottom);
        page.Children.Add(Ui.Scroll(Ui.Column(0, list, tomorrow)));

        Action changed = () => Ui.OnMain(Fill);
        page.AttachedToVisualTree += (_, _) =>
        {
            DayTodoStore.Changed += changed;
            Fill();
            title.Focus();
        };
        page.DetachedFromVisualTree += (_, _) => DayTodoStore.Changed -= changed;
        return page;
    }

    /// <summary>A task: its box (done), its title and its text (changed when the field is left or Enter), → TOMORROW.</summary>
    private static Control Row(TodoWindowViewModel vm, DayTodo todo)
    {
        var id = todo.Id;
        var done = new CheckBox { IsChecked = todo.IsDone, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(done, todo.IsDone ? "Done: untick to do it again" : "Tick when it is done");
        done.IsCheckedChanged += (_, _) => vm.SetDone(id, done.IsChecked == true);
        var title = new TextBox { Text = todo.Title, FontSize = 14, Foreground = todo.IsDone ? Ui.Muted : Ui.TextBright, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        var text = new TextBox { Text = todo.Text, Watermark = "text", Foreground = todo.IsDone ? Ui.Muted : Ui.Text, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        title.LostFocus += (_, _) =>
        {
            if (title.Text != todo.Title)
                vm.SetTitle(id, title.Text ?? "");
        };
        text.LostFocus += (_, _) =>
        {
            if (text.Text != todo.Text)
                vm.SetText(id, text.Text ?? "");
        };
        foreach (var (box, apply) in new (TextBox, Action)[] { (title, () => vm.SetTitle(id, title.Text ?? "")), (text, () => vm.SetText(id, text.Text ?? "")) })
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                e.Handled = true;
                apply();
            };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,8,1.6*,Auto") };
        grid.Children.Add(done);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(text, 3);
        grid.Children.Add(text);
        if (!todo.IsDone)
        {
            var later = Ui.Button("→ TOMORROW", () => vm.Postpone(id), "Put it off to tomorrow");
            later.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(later, 4);
            grid.Children.Add(later);
        }
        var frame = Ui.Box(grid, 6, todo.IsDone ? Ui.Panel : null, todo.IsDone ? Ui.Green : Ui.Edge);
        if (todo.Postponed > 0)
            ToolTip.SetTip(frame, $"Put off {todo.Postponed} time(s) before");
        return frame;
    }
}
