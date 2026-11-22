using System;
using System.Threading.Tasks;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Writing;

/// <summary>
/// /collectionplan: the collection plans of the web scraper, the latest ones listed when it opens, a search in all their
/// fields (Enter or Search; and, or, parentheses; empty: the latest ones), New (⌘/Ctrl N), the plans on the left (date,
/// project, websites, badges active / new / modified, title, description), the selected one editable on the right
/// (name, active, project, description, instructions, the websites with their URLs (address, mode fixed / explore /
/// random, main page, keywords), comment; the application, user and machine are kept but never shown) with its id, date, status, 🕷 RUN
/// (starts the agent KAONOMI.Agent.WebScraper.Collect on the plan: it collects the pages and saves them in the index
/// page; its steps under the header, CANCEL stops it; at the end a question offers to analyse the pages collected, which
/// starts the agent KAONOMI.Agent.WebScraper.Analyze), 🧠 ANALYZE (starts that agent without collecting first), 🗂 RESULTS
/// (the pages collected), Delete (confirmed; only marked deleted), Clone, Discard changes
/// and Save (⌘/Ctrl S).
/// </summary>
[ToolView(typeof(CollectionPlanWindowViewModel), "Collection plans", "🕷", 1100, 750)]
public sealed class CollectionPlanView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (CollectionPlanWindowViewModel)viewModel;
        vm.ConfirmAsync = WritingUi.Confirm;
        vm.ClipboardText = () => DisplayServer.ClipboardGet();

        var search = Ui.SearchBar(vm, nameof(vm.Term), vm.SearchCommand, "Search the collection plans in all their fields (and, or, parentheses) · empty: latest plans",
            Ui.Command("NEW", vm.NewCommand, tooltip: "Create a new collection plan (Ctrl+N / ⌘N)"));
        var searchBox = (LineEdit)search.GetChild(0);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 2);
        var empty = Ui.Placeholder("No results");
        Bind.Items(list, vm.Results, entry => ResultRow(vm, entry), empty);
        var left = Ui.Column(0, Ui.FillHeight(Ui.Scroll(list)), empty);
        left.CustomMinimumSize = new Vector2(300, 0);

        var editor = Ui.Swap(vm, nameof(vm.SelectedResult), value => value is CollectionPlanEntryViewModel entry ? Editor(vm, entry, shell) : null);

        var page = Ui.Column(6, Ui.Toolbar(search), Ui.SplitH(left, editor, 340),
            WritingUi.StatusBar(vm, nameof(vm.Status), (System.ComponentModel.INotifyPropertyChanged)vm.SearchCommand));

        WritingUi.Shortcuts(page, key =>
        {
            if (WritingUi.IsCommand(key, Key.S))
                return WritingUi.Run(vm.SelectedResult?.SaveCommand);
            if (WritingUi.IsCommand(key, Key.N))
                return WritingUi.Run(vm.NewCommand);
            return false;
        });
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(searchBox))
                searchBox.GrabFocus();
        }).CallDeferred();

        return page;
    }

    /// <summary>A collection plan of the list: its summary, badges active / new / modified, title and description.</summary>
    private static Control ResultRow(CollectionPlanWindowViewModel vm, CollectionPlanEntryViewModel entry)
    {
        var description = WritingUi.Clamp(Ui.Label(entry, nameof(entry.Description), Eve.Eve.Muted, 11), 1);
        Bind.Visible(description, entry, nameof(entry.HasDescription));
        var content = Ui.Column(2,
            Ui.Row(6, Ui.Label(entry, nameof(entry.Summary), Eve.Eve.Muted, 10),
                Ui.Badge("active", Eve.Eve.Cyan, entry, nameof(entry.IsActive)),
                Ui.Badge("new", Eve.Eve.Green, entry, nameof(entry.IsNew)),
                Ui.Badge("modified", Eve.Eve.Amber, entry, nameof(entry.IsDirty))),
            WritingUi.Clamp(Ui.Label(entry, nameof(entry.Title), Eve.Eve.TextBright, 13), 2),
            description);
        return WritingUi.ResultRow(vm, nameof(vm.SelectedResult), () => ReferenceEquals(vm.SelectedResult, entry), () => vm.SelectedResult = entry, content);
    }

    /// <summary>The editor of the selected collection plan.</summary>
    private static Control Editor(CollectionPlanWindowViewModel vm, CollectionPlanEntryViewModel entry, Shell shell)
    {
        var dot = Bind.Visible(Eve.Eve.Text_("●", Eve.Eve.Red, 10), entry, nameof(entry.IsError));
        var status = Ui.Label(entry, nameof(entry.Status), Eve.Eve.Text, 12);
        status.ClipText = true;
        status.CustomMinimumSize = new Vector2(80, 0);
        status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        status.HorizontalAlignment = HorizontalAlignment.Right;
        Bind.When(status, entry, () => status.TooltipText = entry.Status, nameof(entry.Status));
        var idLabel = Ui.Line(entry, nameof(entry.IdLabel));
        idLabel.Editable = false;
        idLabel.Flat = true;
        idLabel.AddThemeFontSizeOverride("font_size", 12);
        var statusRow = Ui.Row(6, dot, status);
        statusRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var header = WritingUi.Flow(8, 6,
            Ui.Fill(Ui.Column(0, idLabel, Ui.Label(entry, nameof(entry.DateLabel), Eve.Eve.Muted, 10))),
            statusRow,
            Ui.Command("🕷 RUN", vm.RunCommand, tooltip: "Start the agent KAONOMI.Agent.WebScraper.Collect on this plan, as it is saved: it collects each URL of its websites by its mode and saves each page in the search engine (index page: content by titles and paragraphs, entities, URLs); a Main page is named by its title and the day, and overwritten when collected again the same day. Renderings and pictures: Pictures/kaonomi/scraper/<plan>/<website>/<yyyyMMdd>. At the end, a question offers to analyse the pages collected (the agent KAONOMI.Agent.WebScraper.Analyze)"),
            Ui.Command("🧠 ANALYZE", vm.AnalyzeCommand, tooltip: "Start the agent KAONOMI.Agent.WebScraper.Analyze on the pages collected for this plan that are not analysed yet (a few per run): their persons, organisations and events put in the ontology (entities are created and completed there), their relations, the emotions, the writing and the pictures; one document per page in the index pageanalysis. From half a minute to several minutes a page"),
            Bind.Visible(Ui.Command("■ CANCEL", vm.CancelRunCommand, tooltip: "Stop the agent (the pages it saved stay)"), vm, nameof(vm.IsRunning)),
            Results(vm, entry, shell),
            Ui.Command("DELETE", vm.DeleteCommand, tooltip: "Delete this collection plan (only marked deleted on the server: isDeleted = true)"),
            Ui.Command("CLONE", vm.CloneCommand, tooltip: "Create a copy of this collection plan (new id on save)"),
            Ui.Command("DISCARD CHANGES", entry.RevertCommand),
            Ui.Command("SAVE", entry.SaveCommand, primary: true, tooltip: "Save (Ctrl+S / ⌘S)"));

        var identity = WritingUi.Cells(2,
            Ui.Field("Name", Ui.Line(entry, nameof(entry.Name))),
            Ui.Field("Project", Ui.Line(entry, nameof(entry.Project))));

        var fields = Ui.Column(10,
            identity,
            Ui.Check("Active (run by the web scraper)", entry, nameof(entry.IsActive)),
            Ui.Field("Description", Ui.Lines(entry, nameof(entry.Description), 100)),
            Ui.Field("Instructions", Ui.Lines(entry, nameof(entry.Instructions), 160)),
            Websites(entry),
            Ui.Field("Comment", Ui.Lines(entry, nameof(entry.Comment), 44)));
        return Ui.Column(8, Ui.Margin(header, 8, 4, 8, 0), Ui.Margin(RunLog(vm), 8, 0, 8, 0), Ui.Margin(Refused(vm), 8, 0, 8, 0), Eve.Eve.Separator(), Ui.FillHeight(Ui.Scroll(Ui.Margin(fields, 8, 0, 12, 12))));
    }

    /// <summary>
    /// 🗂 RESULTS: the window of what the agent collected for the plan (its pages, their rendering, pictures, text and
    /// entities), one per plan; enabled once the plan is saved (it has an id).
    /// </summary>
    private static Button Results(CollectionPlanWindowViewModel vm, CollectionPlanEntryViewModel entry, Shell shell)
    {
        var button = Ui.Action("🗂 RESULTS", () =>
        {
            if (entry.IsNew)
                return;
            var results = vm.CreateResults(entry);
            shell.Open(results, $"ScraperResults:{entry.Id}", results.Title);
        }, tooltip: "See what RUN collected for this plan: its pages, their rendering, pictures, text and entities (a picture opens in the image analysis at its real size)");
        button.Disabled = entry.IsNew;
        return button;
    }

    /// <summary>What RUN does: the steps of the agent, one line each, the last one in view (shown once a run started).</summary>
    private static Control RunLog(CollectionPlanWindowViewModel vm)
    {
        var log = new TextEdit
        {
            Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 130),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ContextMenuEnabled = true
        };
        log.AddThemeFontOverride("font", Eve.Eve.Mono);
        log.AddThemeFontSizeOverride("font_size", 11);
        Bind.When(log, vm, () =>
        {
            log.Text = vm.RunLog;
            // The last line in view, once the lines are laid out (they wrap: their count is not the height of the text).
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(log))
                    log.ScrollVertical = log.GetVScrollBar().MaxValue;
            }).CallDeferred();
        }, nameof(vm.RunLog));
        var progress = Bind.Visible(new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4) }, vm, nameof(vm.IsRunning));
        var box = Ui.Column(4, Eve.Eve.Caption("RUN", Eve.Eve.Amber, 11), progress, log);
        return Bind.Visible(box, vm, nameof(vm.HasRunLog));
    }

    /// <summary>
    /// The pages of the last RUN that their site refused to the agent, each with OPEN (the page in the browser of the
    /// user) and PASTE PAGE (what the user copied there is kept as the page collected); shown when there is one.
    /// </summary>
    private static Control Refused(CollectionPlanWindowViewModel vm)
    {
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 4);
        Bind.Items(list, vm.RefusedPages, page => RefusedRow(vm, page));
        var hint = Eve.Eve.Text_("Refused by their site (a protection against robots, which the agent does not go round). OPEN a page in your browser, select the whole page (⌘A / Ctrl+A) and copy it (⌘C / Ctrl+C), then PASTE PAGE keeps what you copied: its titles and paragraphs, its entities and its links (no rendering, no pictures).", Eve.Eve.Muted, 11, wrap: true);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var box = Ui.Column(4, Eve.Eve.Caption("REFUSED PAGES", Eve.Eve.Amber, 11), hint, list);
        return Bind.Visible(box, vm, nameof(vm.HasRefusedPages));
    }

    private static Control RefusedRow(CollectionPlanWindowViewModel vm, RefusedPage page)
    {
        var address = Eve.Eve.Text_(page.Url, Eve.Eve.TextBright, 12);
        address.ClipText = true;
        address.CustomMinimumSize = new Vector2(120, 0);
        address.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        address.TooltipText = $"{page.Url}\n{page.Why}";
        address.MouseFilter = Control.MouseFilterEnum.Stop;
        var why = Eve.Eve.Text_(page.Why, Eve.Eve.Muted, 11);
        return Ui.Row(6, address, why,
            Ui.Action("OPEN", () => vm.OpenRefusedCommand.Execute(page), tooltip: "Open this page in your browser, as any reader does"),
            Ui.Action("PASTE PAGE", () => vm.PasteRefusedCommand.Execute(page), tooltip: "Keep what you copied in the browser as the page collected (index page): cut by titles and paragraphs, with its entities and its links; a main page is named by its website and the day"));
    }

    /// <summary>The websites of the plan, each with its URLs, and ADD WEBSITE.</summary>
    private static Control Websites(CollectionPlanEntryViewModel entry)
    {
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 8);
        var empty = Ui.Placeholder("No website: ADD WEBSITE adds one");
        Bind.Items(list, entry.Websites, website => Website(entry, website), empty);
        var title = Eve.Eve.Text_("Websites", Eve.Eve.TextBright, 13);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return Ui.Column(6,
            Ui.Row(6, title, Ui.Action("ADD WEBSITE", entry.AddWebsite, tooltip: "Add a website to collect")),
            list,
            empty);
    }

    /// <summary>A website: its name, REMOVE, its URLs (address, mode, main page, keywords) and ADD URL.</summary>
    private static Control Website(CollectionPlanEntryViewModel entry, CollectionPlanWebsiteViewModel website)
    {
        var name = Ui.Line(website, nameof(website.Name), "Name of the website");
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var urls = new VBoxContainer();
        urls.AddThemeConstantOverride("separation", 4);
        var noUrl = Ui.Placeholder("No URL");
        Bind.Items(urls, website.UrlProcesses, url => Url(website, url), noUrl);
        return Ui.Column(4,
            Eve.Eve.Separator(),
            Ui.Row(6, name, Ui.Action("REMOVE", () => entry.RemoveWebsite(website), tooltip: "Remove this website and its URLs")),
            Ui.Margin(Ui.Column(4, urls, noUrl, Ui.Row(6, Ui.Action("ADD URL", website.AddUrl, tooltip: "Add a URL to this website"))), 16, 0, 0, 0));
    }

    /// <summary>A URL of a website: the address, the mode (fixed, explore, random), main page (a front page, whose
    /// content changes with the news), the keywords and ✕.</summary>
    private static Control Url(CollectionPlanWebsiteViewModel website, CollectionPlanUrlViewModel url)
    {
        var address = Ui.Line(url, nameof(url.Url), "https://…");
        address.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        address.SizeFlagsStretchRatio = 2;
        var mode = WritingUi.Choice(url, nameof(url.Mode), CollectionPlanUrlViewModel.Modes);
        mode.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        mode.CustomMinimumSize = new Vector2(100, 0);
        mode.TooltipText = "fixed: this page only · explore: the pages it links to · random: pages picked at random";
        var main = Ui.Check("Main page", url, nameof(url.IsMainPage), "Main page: a front page, whose content changes with the news (the home page of a site, the front page of a section)");
        var keywords = Ui.Line(url, nameof(url.Keywords), "Keywords, separated by commas");
        keywords.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return Ui.Row(6, address, mode, main, keywords, Ui.Action("✕", () => website.RemoveUrl(url), tooltip: "Remove this URL"));
    }
}
