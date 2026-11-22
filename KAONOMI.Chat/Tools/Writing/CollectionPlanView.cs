using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.ViewModels;
using KAONOMI.UI.Desk;

namespace KAONOMI.UI.Tools.Writing;

/// <summary>
/// /collectionplan: the collection plans of the web scraper, the latest ones listed when it opens, a search in all their
/// fields (Enter or Search; and, or, parentheses; empty: the latest ones), New (⌘/Ctrl N), the plans on the left (date,
/// project, websites, badges active / new / modified, title, description), the selected one editable on the right (name,
/// active, project, description, instructions, the websites with their URLs (address, mode fixed / explore / random,
/// main page, keywords), comment) with its id, date, status, 🕷 RUN (starts the agent KAONOMI.Agent.WebScraper.Collect on the
/// plan: it collects the pages and saves them in the index page; its steps under the header, CANCEL stops it), Delete (confirmed;
/// only marked deleted), Clone, Discard changes and Save (⌘/Ctrl S).
/// </summary>
[ToolView(typeof(CollectionPlanWindowViewModel), "Collection plans", "🕷", 1100, 750)]
public sealed class CollectionPlanView : IToolView
{
    public Control Build(object viewModel, Shell shell)
    {
        var vm = (CollectionPlanWindowViewModel)viewModel;
        vm.ConfirmAsync = WritingUi.Confirm;

        var search = WritingUi.SearchBar(vm, nameof(vm.Term), vm.SearchCommand, "Search the collection plans in all their fields (and, or, parentheses) · empty: latest plans", out var searchBox,
            WritingUi.Command("NEW", vm.NewCommand, tooltip: "Create a new collection plan (Ctrl+N / ⌘N)"));

        var left = EntryParts.List(vm.Results, entry => ResultRow(vm, entry), "No results");
        var editor = WritingUi.Swap(vm, nameof(vm.SelectedResult), value => value is CollectionPlanEntryViewModel entry ? Editor(vm, entry) : null);

        var page = WritingUi.Page([WritingUi.Toolbar(search)], WritingUi.SplitH(left, editor, 340, minLeft: 260),
            WritingUi.StatusBar(vm, nameof(vm.Status), "IsError", "IsRunning", (System.ComponentModel.INotifyPropertyChanged)vm.SearchCommand));

        WritingUi.Shortcuts(page, key =>
        {
            if (WritingUi.IsCommand(key, Key.S))
                return WritingUi.Run(vm.SelectedResult?.SaveCommand);
            if (WritingUi.IsCommand(key, Key.N))
                return WritingUi.Run(vm.NewCommand);
            return false;
        });
        WritingUi.FocusWhenShown(searchBox);

        return page;
    }

    /// <summary>A collection plan of the list: its summary, badges active / new / modified, title and description.</summary>
    private static Control ResultRow(CollectionPlanWindowViewModel vm, CollectionPlanEntryViewModel entry)
    {
        var description = WritingUi.Clamp(Ui.Bound(entry, nameof(entry.Description), Ui.Muted, 11), 1).Visible(entry, nameof(entry.HasDescription));
        var content = Ui.Column(2,
            Ui.Row(6, Ui.Bound(entry, nameof(entry.Summary), Ui.Muted, 10),
                WritingUi.Badge("active", Ui.Accent, entry, nameof(entry.IsActive)),
                WritingUi.Badge("new", Ui.Green, entry, nameof(entry.IsNew)),
                WritingUi.Badge("modified", WritingUi.Amber, entry, nameof(entry.IsDirty))),
            WritingUi.Clamp(Ui.Bound(entry, nameof(entry.Title), Ui.TextBright, 13), 2),
            description);
        return WritingUi.ResultRow(vm, nameof(vm.SelectedResult), () => ReferenceEquals(vm.SelectedResult, entry), () => vm.SelectedResult = entry, content);
    }

    /// <summary>The editor of the selected collection plan.</summary>
    private static Control Editor(CollectionPlanWindowViewModel vm, CollectionPlanEntryViewModel entry)
    {
        var header = WritingUi.Flow(8,
            EntryParts.Identity(entry),
            EntryParts.Status(entry),
            WritingUi.Command("🕷 RUN", vm.RunCommand, tooltip: "Start the agent KAONOMI.Agent.WebScraper.Collect on this plan, as it is saved: it collects each URL of its websites by its mode and saves each page in the search engine (index page: content by titles and paragraphs, entities, URLs); a Main page is named by its title and the day, and overwritten when collected again the same day. Renderings and pictures: Pictures/kaonomi/scraper/<plan>/<website>/<yyyyMMdd>. At the end, a question offers to analyse the pages collected (the agent KAONOMI.Agent.WebScraper.Analyze)"),
            WritingUi.Command("🧠 ANALYZE", vm.AnalyzeCommand, tooltip: "Start the agent KAONOMI.Agent.WebScraper.Analyze on the pages collected for this plan that are not analysed yet (a few per run): their persons, organisations and events put in the ontology (entities are created and completed there), their relations, the emotions, the writing and the pictures; one document per page in the index pageanalysis. From half a minute to several minutes a page"),
            WritingUi.Command("■ CANCEL", vm.CancelRunCommand, tooltip: "Stop the agent (the pages it saved stay)").Visible(vm, nameof(vm.IsRunning)),
            WritingUi.Command("DELETE", vm.DeleteCommand, tooltip: "Delete this collection plan (only marked deleted on the server: isDeleted = true)"),
            WritingUi.Command("CLONE", vm.CloneCommand, tooltip: "Create a copy of this collection plan (new id on save)"),
            WritingUi.Command("DISCARD CHANGES", entry.RevertCommand),
            WritingUi.Command("SAVE", entry.SaveCommand, primary: true, tooltip: "Save (Ctrl+S / ⌘S)"));

        var identity = WritingUi.Cells(2,
            WritingUi.Field("Name", WritingUi.Line(entry, nameof(entry.Name))),
            WritingUi.Field("Project", WritingUi.Line(entry, nameof(entry.Project))));

        var fields = Ui.Column(10,
            identity,
            Ui.Check("Active (run by the web scraper)", entry, nameof(entry.IsActive)),
            WritingUi.Field("Description", WritingUi.Lines(entry, nameof(entry.Description), 100)),
            WritingUi.Field("Instructions", WritingUi.Lines(entry, nameof(entry.Instructions), 160)),
            Websites(entry),
            WritingUi.Field("Comment", WritingUi.Lines(entry, nameof(entry.Comment), 44)));
        return EntryParts.Editor(header, fields, RunLog(vm), Refused(vm));
    }

    /// <summary>What RUN does, one line per page read and per step, the last one in view (shown once a run started).</summary>
    private static Control RunLog(CollectionPlanWindowViewModel vm)
    {
        var log = WritingUi.ReadOnly(mono: true, size: 11);
        log.Height = 130;
        Ui.When(log, vm, () =>
        {
            log.Text = vm.RunLog;
            log.CaretIndex = log.Text?.Length ?? 0;
        }, nameof(vm.RunLog));
        var progress = new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4 }.Visible(vm, nameof(vm.IsRunning));
        var box = Ui.Column(4, Ui.Caption("RUN", WritingUi.Amber, 11), progress, log);
        box.Margin = new Thickness(0, 6, 0, 4);
        return box.Visible(vm, nameof(vm.HasRunLog));
    }

    /// <summary>
    /// The pages of the last RUN that their site refused to the agent, each with OPEN (the page in the browser of the
    /// user) and PASTE PAGE (what the user copied there is kept as the page collected); shown when there is one.
    /// </summary>
    private static Control Refused(CollectionPlanWindowViewModel vm)
    {
        var list = new StackPanel { Spacing = 4 };
        WritingUi.Items(list, vm.RefusedPages, page => RefusedRow(vm, page));
        var box = Ui.Column(4, Ui.Caption("REFUSED PAGES", WritingUi.Amber, 11),
            Ui.Label("Refused by their site (a protection against robots, which the agent does not go round). OPEN a page in your browser, select the whole page (⌘A / Ctrl+A) and copy it (⌘C / Ctrl+C), then PASTE PAGE keeps what you copied: its titles and paragraphs, its entities and its links (no rendering, no pictures).", Ui.Muted, 11, wrap: true), list);
        box.Margin = new Thickness(0, 2, 0, 4);
        return box.Visible(vm, nameof(vm.HasRefusedPages));
    }

    private static Control RefusedRow(CollectionPlanWindowViewModel vm, RefusedPage page)
    {
        var address = Ui.Label(page.Url, size: 12);
        address.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        address.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(address, $"{page.Url}\n{page.Why}");
        var why = Ui.Label(page.Why, Ui.Muted, 11);
        why.VerticalAlignment = VerticalAlignment.Center;
        var open = Ui.Button("OPEN", () => vm.OpenRefusedCommand.Execute(page), "Open this page in your browser, as any reader does");
        var paste = Ui.Button("PASTE PAGE", () => vm.PasteRefusedCommand.Execute(page), "Keep what you copied in the browser as the page collected (index page): cut by titles and paragraphs, with its entities and its links; a main page is named by its website and the day");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(why, 1);
        Grid.SetColumn(open, 2);
        Grid.SetColumn(paste, 3);
        grid.Children.Add(address);
        grid.Children.Add(why);
        grid.Children.Add(open);
        grid.Children.Add(paste);
        return grid;
    }

    /// <summary>The websites of the plan, each with its URLs, and ADD WEBSITE.</summary>
    private static Control Websites(CollectionPlanEntryViewModel entry)
    {
        var list = new StackPanel { Spacing = 8 };
        var empty = WritingUi.Placeholder("No website: ADD WEBSITE adds one");
        WritingUi.Items(list, entry.Websites, website => Website(entry, website), empty);
        var title = Ui.Label("Websites", Ui.TextBright, 13, bold: true);
        return Ui.Column(6,
            Ui.Dock((Ui.Button("ADD WEBSITE", entry.AddWebsite, "Add a website to collect"), Dock.Right), (title, Dock.Left)),
            list,
            empty);
    }

    /// <summary>A website: its name, REMOVE, its URLs (address, mode, main page, keywords) and ADD URL.</summary>
    private static Control Website(CollectionPlanEntryViewModel entry, CollectionPlanWebsiteViewModel website)
    {
        var name = WritingUi.Line(website, nameof(website.Name), "Name of the website");
        var remove = Ui.Button("REMOVE", () => entry.RemoveWebsite(website), "Remove this website and its URLs");
        remove.Margin = new Thickness(6, 0, 0, 0);
        var urls = new StackPanel { Spacing = 4 };
        var noUrl = Ui.Label("No URL", Ui.Muted, 12);
        WritingUi.Items(urls, website.UrlProcesses, url => Url(website, url), noUrl);
        var inner = Ui.Column(4, urls, noUrl, Ui.Row(6, Ui.Button("ADD URL", website.AddUrl, "Add a URL to this website")));
        inner.Margin = new Thickness(16, 0, 0, 0);
        return Ui.Column(4,
            WritingUi.Separator(),
            Ui.Dock((remove, Dock.Right), (name, Dock.Left)),
            inner);
    }

    /// <summary>A URL of a website: the address, the mode (fixed, explore, random), main page (a front page, whose
    /// content changes with the news), the keywords and ✕.</summary>
    private static Control Url(CollectionPlanWebsiteViewModel website, CollectionPlanUrlViewModel url)
    {
        var address = WritingUi.Line(url, nameof(url.Url), "https://…");
        var mode = WritingUi.Choice(url, nameof(url.Mode), CollectionPlanUrlViewModel.Modes);
        mode.MinWidth = 100;
        ToolTip.SetTip(mode, "fixed: this page only · explore: the pages it links to · random: pages picked at random");
        var main = Ui.Check("Main page", url, nameof(url.IsMainPage));
        main.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(main, "Main page: a front page, whose content changes with the news (the home page of a site, the front page of a section)");
        var keywords = WritingUi.Line(url, nameof(url.Keywords), "Keywords, separated by commas");
        var remove = Ui.Button("✕", () => website.RemoveUrl(url), "Remove this URL");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,Auto,Auto,*,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(mode, 1);
        Grid.SetColumn(main, 2);
        Grid.SetColumn(keywords, 3);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(address);
        grid.Children.Add(mode);
        grid.Children.Add(main);
        grid.Children.Add(keywords);
        grid.Children.Add(remove);
        return grid;
    }
}
