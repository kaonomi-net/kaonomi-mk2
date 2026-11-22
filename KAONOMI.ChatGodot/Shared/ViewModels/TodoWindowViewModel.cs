using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.Models
{
    /// <summary>A task of the day (<c>/todo</c>): a title, a text, todo or done, the day it is for.</summary>
    public sealed class DayTodo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";

        /// <summary>todo or done.</summary>
        public string Status { get; set; } = "todo";

        /// <summary>The day it is planned for (yyyy-MM-dd).</summary>
        public string Day { get; set; } = "";

        public DateTime Created { get; set; } = DateTime.Now;

        /// <summary>How many times it was put off to the next day.</summary>
        public int Postponed { get; set; }

        /// <summary>A task of a day gone (and not put off to today) is marked deleted at the first opening of the day: it is never erased, and never removed by hand.</summary>
        public bool IsDeleted { get; set; }

        public bool IsDone => Status == "done";
    }
}

namespace KAONOMI.Chat.Services
{
    /// <summary>
    /// The tasks of the day, kept in <c>~/kaonomi/todo/todos.json</c>, shared by both chats (Godot and Avalonia): each
    /// change reads the file again, changes it and writes it. A new day marks the tasks of the days before deleted
    /// (<see cref="DayTodo.IsDeleted"/>, nothing is erased); those put off to the next day (<c>/todo</c>) are its tasks.
    /// <c>~/kaonomi/todo/state.json</c> keeps the day the planning popup was shown last: once a day, in either chat.
    /// </summary>
    public static class DayTodoStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        private static readonly object Gate = new();

        /// <summary>The JSON of the file of the tasks (for <see cref="TodoSync"/>).</summary>
        internal static JsonSerializerOptions FileJson => Json;

        public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "todo");

        public static string FilePath => Path.Combine(Folder, "todos.json");

        private static string StatePath => Path.Combine(Folder, "state.json");

        public static string Today => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static string Tomorrow => DateTime.Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Raised after every change (the windows of /todo draw their list again).</summary>
        public static event Action? Changed;

        private sealed record State(string? LastPlanned);

        /// <summary>The tasks (not deleted) of <paramref name="day"/>, those to do first, then in their order of creation.</summary>
        public static List<DayTodo> Of(string day) =>
            Load().Where(t => !t.IsDeleted && t.Day == day).OrderBy(t => t.IsDone).ThenBy(t => t.Created).ToList();

        /// <summary>Changes the tasks: <paramref name="change"/> on the list read again, then written; raises <see cref="Changed"/>.</summary>
        public static void Change(Action<List<DayTodo>> change)
        {
            lock (Gate)
            {
                var all = Load();
                change(all);
                Save(all);
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The start of a day, at the first opening of the day of either chat: the tasks of the days before (not put off to
        /// this one) are marked deleted. Returns how many.
        /// </summary>
        public static int StartDay()
        {
            var today = Today;
            var count = 0;
            lock (Gate)
            {
                var all = Load();
                foreach (var todo in all.Where(t => !t.IsDeleted && string.CompareOrdinal(t.Day, today) < 0))
                {
                    todo.IsDeleted = true;
                    count++;
                }
                if (count > 0)
                    Save(all);
            }
            if (count > 0)
            {
                ActivityLog.Info("Todo", $"{count} task(s) of the days before marked deleted");
                Changed?.Invoke();
            }
            return count;
        }

        /// <summary>True the first time it is asked on a day (the popup of the planning is to be shown), then false that day; the day is kept.</summary>
        public static bool FirstTimeToday()
        {
            lock (Gate)
            {
                try
                {
                    var state = File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) : null;
                    if (state?.LastPlanned == Today)
                        return false;
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(StatePath, JsonSerializer.Serialize(new State(Today), Json));
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    ActivityLog.Error("Todo", $"Cannot read or write {StatePath}", ex);
                    return false;
                }
            }
        }

        private static List<DayTodo> Load()
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<DayTodo>>(File.ReadAllText(FilePath), Json) ?? [] : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                ActivityLog.Error("Todo", $"Cannot read {FilePath}", ex);
                return [];
            }
        }

        private static void Save(List<DayTodo> all)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActivityLog.Error("Todo", $"Cannot write {FilePath}", ex);
            }
        }
    }
}

namespace KAONOMI.Chat.ViewModels
{
    /// <summary>
    /// The tasks of the day (<c>/todo</c>, and the popup of the first opening of the day): each a title and a text, to do
    /// or done. Only the tasks of today: a task not done can be put off to tomorrow (it leaves the list, and is a task of
    /// tomorrow), all those not done at once too; the ones put off show under the list and can come back to today. A task
    /// cannot be removed by hand: those of the days gone are marked deleted at the first opening of the next day
    /// (<see cref="DayTodoStore.StartDay"/>), but those put off to that day.
    /// <see cref="IsPlanning"/>: the popup of the morning (the same list, with its own words).
    /// </summary>
    public sealed partial class TodoWindowViewModel : ViewModelBase
    {
        public TodoWindowViewModel(bool planning = false)
        {
            IsPlanning = planning;
        }

        public bool IsPlanning { get; }

        [ObservableProperty] private string _newTitle = "";
        [ObservableProperty] private string _newText = "";
        [ObservableProperty] private string _status = "";

        public List<DayTodo> Today => DayTodoStore.Of(DayTodoStore.Today);

        public List<DayTodo> Tomorrow => DayTodoStore.Of(DayTodoStore.Tomorrow);

        public string DayTitle => DateTime.Today.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture);

        public string Summary
        {
            get
            {
                var today = Today;
                var done = today.Count(t => t.IsDone);
                return today.Count == 0 ? "Nothing planned today yet." : $"{done} of {today.Count} done{(today.Count - done > 0 ? $" · {today.Count - done} to do" : " · all done ✓")}";
            }
        }

        /// <summary>Adds the task typed (its title at least).</summary>
        public bool Add()
        {
            var title = NewTitle.Trim();
            if (title.Length == 0)
            {
                Status = "A task needs a title";
                return false;
            }
            var todo = new DayTodo { Title = title, Text = NewText.Trim(), Day = DayTodoStore.Today };
            DayTodoStore.Change(all => all.Add(todo));
            NewTitle = NewText = "";
            Status = $"\"{title}\" added";
            return true;
        }

        public void SetDone(string id, bool done) => Edit(id, t => t.Status = done ? "done" : "todo");

        public void SetTitle(string id, string title)
        {
            if (title.Trim().Length > 0)
                Edit(id, t => t.Title = title.Trim());
        }

        public void SetText(string id, string text) => Edit(id, t => t.Text = text.Trim());

        /// <summary>Puts the task off to tomorrow.</summary>
        public void Postpone(string id)
        {
            Edit(id, t =>
            {
                t.Day = DayTodoStore.Tomorrow;
                t.Postponed++;
                t.Status = "todo";
            });
            Status = "Put off to tomorrow";
        }

        /// <summary>Puts every task not done off to tomorrow.</summary>
        public void PostponeUndone()
        {
            var count = 0;
            DayTodoStore.Change(all =>
            {
                foreach (var t in all.Where(t => !t.IsDeleted && t.Day == DayTodoStore.Today && !t.IsDone))
                {
                    t.Day = DayTodoStore.Tomorrow;
                    t.Postponed++;
                    count++;
                }
            });
            Status = count == 0 ? "Nothing left to do today" : $"{count} task(s) put off to tomorrow";
        }

        /// <summary>A task put off to tomorrow comes back to today.</summary>
        public void BackToToday(string id)
        {
            Edit(id, t => t.Day = DayTodoStore.Today);
            Status = "Back to today";
        }

        private static void Edit(string id, Action<DayTodo> change) =>
            DayTodoStore.Change(all =>
            {
                if (all.FirstOrDefault(t => t.Id == id) is { } todo)
                    change(todo);
            });
    }
}
