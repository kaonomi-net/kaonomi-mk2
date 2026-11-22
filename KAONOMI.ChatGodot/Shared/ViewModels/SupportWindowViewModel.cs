using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.Chat.Models
{
    /// <summary>A request for support made with <c>/support</c>, kept in <c>~/kaonomi/support/tickets.json</c>.</summary>
    public sealed class SupportTicket
    {
        /// <summary>SUP-yyyyMMdd-NNN.</summary>
        public string Id { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.Now;
        public string Requester { get; set; } = "";
        public string Contact { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "Incident";
        public string Priority { get; set; } = "Normal";

        /// <summary>The products concerned, by name (the usual ones and those of /archi).</summary>
        public List<string> Products { get; set; } = [];

        /// <summary>The fact sheets of /archi among them (their ids).</summary>
        public List<string> ArchiIds { get; set; } = [];

        /// <summary>The task of the kanban made for it (in the Support project), null until it is created.</summary>
        public string? TaskId { get; set; }
    }
}

namespace KAONOMI.Chat.ViewModels
{
    /// <summary>
    /// The request for support (<c>/support</c>): a title, a description, a category, a priority, the requester and how to
    /// reach them, and the products concerned, chosen among the usual ones (those of the requests made before, the most
    /// asked first, then a few common ones) and, one or several, among the fact sheets of <c>/archi</c> (applications, IT
    /// components, interfaces, AI models, agents, multi-agent systems, MCP servers, tools, REST APIs, knowledge bases…).
    /// SEND keeps the ticket in <c>~/kaonomi/support/tickets.json</c> (nothing is ever removed from it), then creates a
    /// task in the kanban, in the project Support (made when it does not exist yet): a bug for an incident or a bug, a
    /// change otherwise, to do, tagged #SUPPORT and its priority. A ticket whose task could not be created (the server
    /// down) keeps it to be created again.
    /// </summary>
    public sealed partial class SupportWindowViewModel : ViewModelBase
    {
        public const string SupportProject = "Support";

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        /// <summary>The JSON of the file of the requests (for <see cref="SupportSync"/>).</summary>
        internal static JsonSerializerOptions FileJson => Json;

        /// <summary>A few products asked for often, after those of the requests made before.</summary>
        private static readonly string[] CommonProducts =
            ["KAONOMI Chat", "KAONOMI Server", "KAONOMI SearchEngine", "Ollama", "LM Studio", "Workstation (Mac / PC)", "Network / Wi-Fi", "Email", "Office suite", "Printer", "Phone"];

        /// <summary>The kinds of fact sheets of /archi offered as products.</summary>
        public static readonly ArchiType[] ProductTypes =
        [
            ArchiType.Application, ArchiType.ITComponent, ArchiType.Interface, ArchiType.AIModel, ArchiType.Agent, ArchiType.MultiAgentSystem,
            ArchiType.McpServer, ArchiType.Tool, ArchiType.RestApi, ArchiType.KnowledgeBase, ArchiType.VectorStore, ArchiType.Dataset
        ];

        public static readonly string[] Categories = ["Incident", "Bug", "Question", "Request", "Change"];
        public static readonly string[] Priorities = ["Low", "Normal", "High", "Critical"];

        private readonly KaonomiAssistantService? _assistant;

        public SupportWindowViewModel(KaonomiAssistantService? assistant)
        {
            _assistant = assistant;
            Tickets = Load();
            // Requests changed on another machine (the store is the search engine): read again.
            LocalSync.Applied += kind =>
            {
                if (kind != "supportticket")
                    return;
                Tickets.Clear();
                Tickets.AddRange(Load());
                Changed?.Invoke();
            };
            Archi = ArchiWindowViewModel.ReadWorkspace();
            Requester = Environment.UserName;
        }

        public static string Folder => Path.Combine(ChatSettings.KaonomiHome, "support");

        public static string FilePath => Path.Combine(Folder, "tickets.json");

        /// <summary>The tickets made, the newest first.</summary>
        public List<SupportTicket> Tickets { get; }

        /// <summary>The repository of /archi, read when the window opened.</summary>
        public ArchiWorkspace Archi { get; }

        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _description = "";
        [ObservableProperty] private string _category = "Incident";
        [ObservableProperty] private string _priority = "Normal";
        [ObservableProperty] private string _requester = "";
        [ObservableProperty] private string _contact = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private bool _isError;
        [ObservableProperty] private bool _isBusy;

        /// <summary>The usual products chosen (by name).</summary>
        public HashSet<string> ChosenUsual { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The fact sheets of /archi chosen (by id).</summary>
        public HashSet<string> ChosenArchi { get; } = [];

        /// <summary>Opens the kanban (set by the chat).</summary>
        public Action? OpenKanban { get; init; }

        /// <summary>Opens the architecture repository (set by the chat).</summary>
        public Action? OpenArchi { get; init; }

        /// <summary>Raised when the tickets or the choices changed.</summary>
        public event Action? Changed;

        /// <summary>The usual products: those of the requests made before, the most asked first, then the common ones.</summary>
        public IReadOnlyList<(string Name, int Count)> UsualProducts
        {
            get
            {
                var asked = Tickets.SelectMany(t => t.Products).GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .Select(g => (Name: g.First(), Count: g.Count())).OrderByDescending(p => p.Count).ThenBy(p => p.Name).ToList();
                foreach (var common in CommonProducts)
                    if (!asked.Any(p => p.Name.Equals(common, StringComparison.OrdinalIgnoreCase)))
                        asked.Add((common, 0));
                return asked.Take(16).ToList();
            }
        }

        /// <summary>The fact sheets of /archi offered as products, of <paramref name="type"/> (all when null), holding <paramref name="search"/>.</summary>
        public List<ArchiSheet> ArchiProducts(ArchiType? type, string search) =>
            Archi.Sheets.Where(s => ProductTypes.Contains(s.Type) && (type is null || s.Type == type)
                                    && (search.Length == 0 || s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || s.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(s => Array.IndexOf(ProductTypes, s.Type)).ThenBy(s => s.Name).ToList();

        /// <summary>The names of every product chosen.</summary>
        public List<string> ChosenNames() =>
            ChosenUsual.Concat(ChosenArchi.Select(id => Archi.Sheets.FirstOrDefault(s => s.Id == id)?.Name).OfType<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public void Toggle(string usual, bool on)
        {
            if (on)
                ChosenUsual.Add(usual);
            else
                ChosenUsual.Remove(usual);
            Changed?.Invoke();
        }

        public void ToggleArchi(string id, bool on)
        {
            if (on)
                ChosenArchi.Add(id);
            else
                ChosenArchi.Remove(id);
            Changed?.Invoke();
        }

        private void SetStatus(string text, bool error = false)
        {
            Status = text;
            IsError = error;
        }

        /// <summary>Keeps the ticket, then creates its task in the kanban (project Support).</summary>
        public async Task SendAsync()
        {
            if (Title.Trim().Length == 0)
            {
                SetStatus("Say in a few words what the problem is (the title)", true);
                return;
            }
            var products = ChosenNames();
            if (products.Count == 0)
            {
                SetStatus("Choose the product (or the products) concerned", true);
                return;
            }
            var ticket = new SupportTicket
            {
                Id = NextId(), Requester = Requester.Trim(), Contact = Contact.Trim(), Title = Title.Trim(), Description = Description.Trim(),
                Category = Category, Priority = Priority, Products = products, ArchiIds = [.. ChosenArchi]
            };
            Tickets.Insert(0, ticket);
            if (!Save())
                return;
            IsBusy = true;
            await CreateTaskAsync(ticket);
            IsBusy = false;
            if (!IsError)
            {
                Title = Description = "";
                ChosenUsual.Clear();
                ChosenArchi.Clear();
            }
            Changed?.Invoke();
        }

        /// <summary>Creates the task of <paramref name="ticket"/> in the kanban (again, when it failed before).</summary>
        public async Task CreateTaskAsync(SupportTicket ticket)
        {
            if (_assistant is null)
            {
                SetStatus($"Ticket {ticket.Id} kept, but no server to create its task", true);
                return;
            }
            try
            {
                var projectId = await SupportProjectIdAsync();
                var task = new TaskEntry
                {
                    AppName = "KAONOMI.Chat",
                    ProjectId = projectId,
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    Name = $"[{ticket.Id}] {ticket.Title}",
                    Title = $"[{ticket.Id}] {ticket.Title}",
                    Description = Describe(ticket),
                    Type = ticket.Category is "Incident" or "Bug" ? "bug" : "change",
                    Status = "todo",
                    Tags = MetaTags.Parse($"#SUPPORT #PRIORITY-{ticket.Priority} #{ticket.Category}", MetaTags.Tag),
                    StartTime = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc),
                    Comment = $"Support ticket {ticket.Id}, asked with /support by {ticket.Requester} on {ticket.Created:yyyy-MM-dd HH:mm}"
                };
                var result = await _assistant.CreateTaskAsync(task);
                if (!result.Success || result.ReturnedId == "-1")
                    throw new HttpRequestException(result.Message);
                ticket.TaskId = result.ReturnedId;
                Save();
                SetStatus($"Ticket {ticket.Id} sent: its task is in the kanban, project {SupportProject} (to do)");
                ActivityLog.Info("Support", $"Ticket {ticket.Id} \"{ticket.Title}\" → task {ticket.TaskId} in the project {SupportProject}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                SetStatus($"Ticket {ticket.Id} kept, but its task was not created ({ex.Message}): ⟳ in the list creates it again", true);
                ActivityLog.Error("Support", $"The task of the ticket {ticket.Id} was not created", ex);
            }
            Changed?.Invoke();
        }

        /// <summary>The id of the project Support, made when it does not exist yet.</summary>
        private async Task<string> SupportProjectIdAsync()
        {
            await ProjectCatalog.RefreshAsync();
            if (ProjectCatalog.Projects.FirstOrDefault(p => p.Name.Equals(SupportProject, StringComparison.OrdinalIgnoreCase)) is { } known)
                return known.Id;
            var created = await _assistant!.CreateProjectAsync(new ProjectEntry
            {
                AppName = "KAONOMI.Chat", MachineName = Environment.MachineName, UserName = Environment.UserName, Name = SupportProject,
                Description = "The support requests made with /support: each ticket becomes a task of the kanban in this project.",
                Goals = ["Answer every support request", "Know which products need support most"], StartDate = DateTimeOffset.Now.Date
            });
            if (!created.Success || created.ReturnedId == "-1")
                throw new HttpRequestException($"the project {SupportProject} was not created: {created.Message}");
            _ = ProjectCatalog.RefreshAsync();
            ActivityLog.Info("Support", $"Project {SupportProject} created ({created.ReturnedId})");
            return created.ReturnedId;
        }

        /// <summary>The description of the task: the request and who asked it.</summary>
        private string Describe(SupportTicket ticket)
        {
            var text = new StringBuilder();
            text.AppendLine(ticket.Description.Length > 0 ? ticket.Description : ticket.Title);
            text.AppendLine();
            text.AppendLine($"Ticket: {ticket.Id} · {ticket.Category} · priority {ticket.Priority}");
            text.AppendLine($"Requester: {ticket.Requester}{(ticket.Contact.Length > 0 ? $" ({ticket.Contact})" : "")}");
            text.AppendLine($"Products: {string.Join(", ", ticket.Products)}");
            foreach (var id in ticket.ArchiIds)
                if (Archi.Sheets.FirstOrDefault(s => s.Id == id) is { } sheet)
                    text.AppendLine($"  · {ArchiCatalog.Info(sheet.Type).Label}: {sheet.Name}{(sheet.Owner.Length > 0 ? $", owner {sheet.Owner}" : "")}");
            return text.ToString().TrimEnd();
        }

        private string NextId()
        {
            var day = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var count = Tickets.Count(t => t.Id.StartsWith($"SUP-{day}-", StringComparison.Ordinal));
            return $"SUP-{day}-{count + 1:000}";
        }

        private static List<SupportTicket> Load()
        {
            try
            {
                return File.Exists(FilePath)
                    ? (JsonSerializer.Deserialize<List<SupportTicket>>(File.ReadAllText(FilePath), Json) ?? []).OrderByDescending(t => t.Created).ToList()
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                ActivityLog.Error("Support", $"Cannot read {FilePath}", ex);
                return [];
            }
        }

        private bool Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Tickets, Json));
                LocalSync.Touch("supportticket");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"The ticket was not kept: {ex.Message}", true);
                return false;
            }
        }
    }
}
