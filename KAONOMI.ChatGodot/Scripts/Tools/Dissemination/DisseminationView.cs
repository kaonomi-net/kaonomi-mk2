using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Dissemination;

/// <summary>
/// /dissimination: how the entities spread in the pages the web scraper collected and analysed, in a window that
/// fills the desktop. At the top, the selection: the collection plan, the period (from, to, or the last 7 or 30 days)
/// and the search, names of entities joined by AND, OR, NOT and parentheses; each search added is a series with its
/// colour (✕ removes it, a click shows its results). On the left, the results per day and per search as bars (a
/// click filters a day), then the results: each title or paragraph with its page, its entities, the pictures tied to
/// it in a small carousel, and what opens the capture of the page, its address or the whole section (READ). On the
/// right, for the search selected: the websites, the entities named with it, the emotions about them and the relations
/// between them; a click there filters the results or adds a search.
/// </summary>
[ToolView(typeof(DisseminationWindowViewModel), "Dissemination", "📡", 1500, 950)]
public sealed class DisseminationView : IToolView
{
    private const string SearchHint = "Entities with AND, OR, NOT and ( ) · Macron AND (Airbus OR \"von der Leyen\") · Enter adds the search";

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (DisseminationWindowViewModel)viewModel;
        var loader = new ThumbnailLoader();

        void OpenPicture(string path)
        {
            if (vm.OpenImage(path) is { } analysis)
                shell.Open(analysis, title: analysis.Title);
        }

        // ── The selection ──
        var plan = new OptionButton { CustomMinimumSize = new Vector2(230, 0), TooltipText = "The collection plan whose analysed pages are searched" };
        plan.ItemSelected += index =>
        {
            if (index >= 0 && index < vm.Plans.Count)
                vm.PlanId = vm.Plans[(int)index].Id;
        };
        var from = Ui.Line(vm, nameof(vm.DateFrom), "yyyy-MM-dd");
        var to = Ui.Line(vm, nameof(vm.DateTo), "yyyy-MM-dd");
        foreach (var date in new[] { from, to })
        {
            date.CustomMinimumSize = new Vector2(112, 0);
            date.TooltipText = "A day, yyyy-MM-dd; Enter applies the period";
            date.TextSubmitted += _ => vm.ApplyDates();
        }
        var query = Ui.Line(vm, nameof(vm.QueryText), SearchHint);
        query.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Several searches are typed one after the other: Enter adds one and the field stays ready for the next.
        query.KeepEditingOnTextSubmit = true;
        query.TooltipText = "Names of entities (or any words) joined by AND, OR and NOT, with parentheses; the operators in capital letters.\n"
                            + "Words that follow each other are one name; quotation marks keep a name as it is.\n"
                            + "A paragraph that writes « Macron » answers « Emmanuel Macron » when the analysis knows both.";
        query.TextSubmitted += _ => vm.AddSearchCommand.Execute(null);
        var toolbar = Ui.Toolbar(Ui.Column(6,
            Ui.Row(8,
                Eve.Eve.Caption("PLAN"), plan,
                Eve.Eve.Caption("FROM"), from, Eve.Eve.Caption("TO"), to,
                Ui.Action("7 D", () => vm.LastDays(7), tooltip: "The last 7 days up to the newest page"),
                Ui.Action("30 D", () => vm.LastDays(30), tooltip: "The last 30 days up to the newest page"),
                Ui.Action("ALL", () => vm.LastDays(0), tooltip: "The whole period of the analysed pages"),
                Ui.Spring(),
                Ui.Command("⟳ REFRESH", vm.RefreshCommand, tooltip: "Read the analyses again")),
            Ui.Row(8,
                Eve.Eve.Caption("SEARCH"), query,
                Ui.Command("ADD", vm.AddSearchCommand, primary: true, tooltip: "Keep this search as a series of the chart (Enter)"))));

        // ── The searches kept ──
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 8);
        chips.AddThemeConstantOverride("v_separation", 4);

        // ── Left: the chart and the results ──
        var chart = new TimelineChart(vm);
        var chartBox = Panel(Ui.Column(4,
            Ui.Row(8, Eve.Eve.Caption("RESULTS PER DAY AND PER SEARCH", Eve.Eve.Amber),
                Eve.Eve.Text_("a bar per search · the dot: the valence of the day · click a day to filter", Eve.Eve.Muted, 11)),
            chart));
        var resultsTitle = Eve.Eve.Caption("RESULTS", Eve.Eve.Amber);
        var filters = Ui.Row(6);
        var results = new VBoxContainer();
        results.AddThemeConstantOverride("separation", 8);
        results.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var resultsScroll = Ui.FillHeight(Ui.Scroll(Ui.Margin(results, 0, 0, 10, 8)));
        var left = Ui.Column(8, chartBox, Ui.Row(8, resultsTitle, filters), resultsScroll);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 2.6f;

        // ── Right: the websites, the entities, the emotions, the relations ──
        var websites = new RankBars { Hint = "Click: only the results of this website (again: all of them)" };
        websites.Chosen += vm.ToggleWebsite;
        var entities = new RankBars { Hint = "Click: only the results that name it (again: all of them) · right click: a search of it" };
        entities.Chosen += vm.ToggleEntity;
        entities.Asked += name => vm.Search(DisseminationWindowViewModel.Quoted(name));
        var emotions = new EmotionBar();
        var relations = new VBoxContainer();
        relations.AddThemeConstantOverride("separation", 2);
        var right = Ui.Scroll(Ui.Margin(Ui.Column(10,
            Panel(Ui.Column(4, Eve.Eve.Caption("WEBSITES", Eve.Eve.Amber), websites)),
            Panel(Ui.Column(4, Ui.Row(8, Eve.Eve.Caption("ENTITIES NAMED", Eve.Eve.Amber), Legend()), entities)),
            Panel(Ui.Column(4, Eve.Eve.Caption("EMOTIONS", Eve.Eve.Amber), emotions)),
            Panel(Ui.Column(4, Eve.Eve.Caption("RELATIONS", Eve.Eve.Amber), relations))), 0, 0, 10, 8));
        right.CustomMinimumSize = new Vector2(380, 0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        body.AddChild(left);
        body.AddChild(right);

        var page = Ui.Column(6, toolbar, chips, body, Ui.StatusBar(vm, nameof(vm.Status), busy: nameof(vm.IsRunning)));

        void Refresh()
        {
            if (!GodotObject.IsInstanceValid(page))
                return;
            // The plans.
            if (plan.ItemCount != vm.Plans.Count || Enumerable.Range(0, plan.ItemCount).Any(i => plan.GetItemText(i) != vm.Plans[i].Name))
            {
                plan.Clear();
                foreach (var (_, name) in vm.Plans)
                    plan.AddItem(name);
            }
            var chosen = vm.Plans.FindIndex(p => p.Id == vm.PlanId);
            if (chosen >= 0 && plan.Selected != chosen)
                plan.Select(chosen);

            // The searches.
            Clear(chips);
            foreach (var search in vm.Searches)
                chips.AddChild(Chip(vm, search));

            chart.QueueRedraw();

            // The results.
            var color = DisseminationColors.Series(vm.Selected.Color);
            resultsTitle.Text = $"RESULTS · {vm.Results.Count} in {vm.PageCount} page(s)";
            Clear(filters);
            if (vm.DayFilter is { } day)
                filters.AddChild(Filter($"📅 {day:yyyy-MM-dd}", () => vm.ToggleDay(day)));
            if (vm.WebsiteFilter is { } website)
                filters.AddChild(Filter($"🌐 {website}", () => vm.ToggleWebsite(website)));
            if (vm.EntityFilter is { } entity)
                filters.AddChild(Filter($"◆ {entity}", () => vm.ToggleEntity(entity)));
            Clear(results);
            foreach (var hit in vm.Results.Take(vm.Shown))
                results.AddChild(Card(vm, shell, hit, color, loader, OpenPicture));
            if (vm.Results.Count > vm.Shown)
                results.AddChild(Ui.Action($"SHOW MORE ({vm.Results.Count - vm.Shown} left)", vm.ShowMore, tooltip: "Show the next results"));
            if (vm.Results.Count == 0)
                results.AddChild(Ui.Placeholder(vm.AnalysedPages == 0
                    ? "No page analysed yet: 🕷 RUN then 🧠 ANALYZE in /collectionplan"
                    : "No result: another search, a longer period, or fewer filters"));

            // The panels.
            websites.Set(vm.Websites.Select(w => (w.Name, w.Count, color)), vm.WebsiteFilter);
            entities.Set(vm.TopEntities.Select(e => (e.Name, e.Count, DisseminationColors.Kind(e.Kind))), vm.EntityFilter);
            emotions.Set(vm.Emotions, vm.MeanValence);
            Clear(relations);
            foreach (var relation in vm.Relations)
                relations.AddChild(Relation(vm, relation));
            if (vm.Relations.Count == 0)
                relations.AddChild(Eve.Eve.Text_("None between the entities of these results", Eve.Eve.Muted, 11));
        }

        Action changed = () => Bind.OnMain(Refresh);
        vm.Changed += changed;
        Bind.OnGone(page, () =>
        {
            vm.Changed -= changed;
            loader.Dispose();
        });
        Refresh();
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(query))
                query.GrabFocus();
        }).CallDeferred();
        return page;
    }

    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static PanelContainer Panel(Control content)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Eve.Eve.Card());
        panel.AddChild(content);
        return panel;
    }

    /// <summary>The colours of the kinds of entities.</summary>
    private static Control Legend()
    {
        var row = Ui.Row(8);
        foreach (var (kind, label) in new[] { ("PERSON", "person"), ("ORGANISATION", "organisation"), ("EVENT", "event") })
            row.AddChild(Eve.Eve.Text_("■ " + label, DisseminationColors.Kind(kind), 10));
        return row;
    }

    /// <summary>A search kept: its colour, its text and its number of results; a click shows its results, ✕ removes it.</summary>
    private static Control Chip(DisseminationWindowViewModel vm, DisseminationSearch search)
    {
        var color = DisseminationColors.Series(search.Color);
        var selected = ReferenceEquals(search, vm.Selected);
        var label = new Button
        {
            Text = $"●  {search.Label}   {search.Hits.Count}",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"{search.Label}: {search.Hits.Count} result(s) in the period\nClick: show its results"
        };
        label.AddThemeColorOverride("font_color", selected ? Eve.Eve.TextBright : Eve.Eve.Text);
        label.AddThemeColorOverride("font_hover_color", Eve.Eve.TextBright);
        label.AddThemeFontSizeOverride("font_size", 12);
        label.Pressed += () => vm.Select(search);
        var remove = new Button { Text = "✕", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Remove this search" };
        remove.AddThemeFontSizeOverride("font_size", 11);
        remove.Pressed += () => vm.Remove(search);
        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", Eve.Eve.Box(color with { A = selected ? 0.3f : 0.12f }, color with { A = selected ? 1f : 0.5f }, 2, selected ? 2 : 1, 12));
        chip.AddChild(Ui.Row(0, label, remove));
        return chip;
    }

    /// <summary>A filter set by a click in a chart: a click on it clears it.</summary>
    private static Button Filter(string text, Action clear)
    {
        var button = Ui.Action(text + "  ✕", clear, tooltip: "Clear this filter");
        button.AddThemeFontSizeOverride("font_size", 11);
        return button;
    }

    /// <summary>A relation among the results: a click searches the paragraphs that name both entities.</summary>
    private static Button Relation(DisseminationWindowViewModel vm, DisseminationRelationCount relation)
    {
        var button = new Button
        {
            Text = $"{relation.Source}  —{relation.Type}→  {relation.Target}" + (relation.Pages > 1 ? $"   ×{relation.Pages}" : string.Empty),
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = $"{relation.Source} —{relation.Type}→ {relation.Target} · stated by {relation.Pages} page(s)"
                          + (relation.Evidence.Length > 0 ? $"\n« {relation.Evidence} »" : string.Empty) + "\nClick: search the paragraphs that name both"
        };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += () => vm.Search($"{DisseminationWindowViewModel.Quoted(relation.Source)} AND {DisseminationWindowViewModel.Quoted(relation.Target)}");
        return button;
    }

    /// <summary>
    /// A result: its day, its website and its page, the title of its section, its text with what the search looks for
    /// lit, the entities it names, the carousel of its pictures, and what opens the capture of the page, its address
    /// and the whole section.
    /// </summary>
    private static Control Card(DisseminationWindowViewModel vm, Shell shell, DisseminationHit hit, Color color, ThumbnailLoader loader, Action<string> openPicture)
    {
        var page = hit.Page;
        var name = new Button
        {
            Text = page.Name,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = $"{page.Name}\n{page.Url}\nClick: read the whole section"
        };
        name.AddThemeFontSizeOverride("font_size", 12);
        name.AddThemeColorOverride("font_color", Eve.Eve.TextBright);
        name.Pressed += () => Read(vm, shell, hit, color, loader, openPicture);
        var header = Ui.Row(8,
            Eve.Eve.Text_(page.Day.ToString("yyyy-MM-dd"), Eve.Eve.Muted, 11),
            Ui.Badge(page.Website, color),
            name);
        if (page.IsMainPage)
            header.AddChild(Ui.Badge("main page", Eve.Eve.Cyan));
        if (hit.IsTitle)
            header.AddChild(Ui.Badge("title", Eve.Eve.Amber));
        var capture = Ui.Action("🖼 PAGE", () => openPicture(page.RenderingPath), tooltip: "Open the capture of the whole page at its real size");
        capture.Disabled = !page.HasRendering;
        if (!page.HasRendering)
            capture.TooltipText = "No capture of this page (a page imported by hand has none)";
        header.AddChild(capture);
        header.AddChild(Ui.Action("🔗", () => OS.ShellOpen(page.Url), tooltip: $"Open the page in the browser\n{page.Url}"));
        header.AddChild(Ui.Action("⤢ READ", () => Read(vm, shell, hit, color, loader, openPicture), tooltip: "Read the whole section, with its pictures"));

        var column = Ui.Column(6, header);
        if (!hit.IsTitle && hit.SectionTitle.Length > 0)
            column.AddChild(Eve.Eve.Text_(hit.SectionTitle, Eve.Eve.Muted, 11, wrap: true));
        column.AddChild(Rich(Lit(hit, vm.Selected.Query, color), hit.IsTitle ? 15 : 13));

        if (hit.Entities.Count > 0)
        {
            var named = new HFlowContainer();
            named.AddThemeConstantOverride("h_separation", 6);
            named.AddThemeConstantOverride("v_separation", 4);
            foreach (var entity in hit.Entities)
                named.AddChild(EntityChip(vm, entity));
            column.AddChild(named);
        }
        if (hit.Pictures.Count > 0)
            column.AddChild(new PictureCarousel(hit.Pictures, loader, openPicture));

        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Eve.Eve.Card());
        card.AddChild(column);
        return card;
    }

    /// <summary>An entity a result names: its kind by its colour, the emotion about it in its tooltip; a click filters, a right click searches it.</summary>
    private static Button EntityChip(DisseminationWindowViewModel vm, DisseminationEntity entity)
    {
        var color = DisseminationColors.Kind(entity.Type);
        var chip = new Button
        {
            Text = (entity.HasEmotion ? "● " : string.Empty) + entity.Name,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"{entity.Name} · {entity.Type.ToLowerInvariant()}"
                          + (entity.HasEmotion ? $"\n{entity.Leading}, valence {entity.Valence:+0.00;-0.00;0.00}, intensity {entity.Intensity:0.00}\n{entity.Summary}" : string.Empty)
                          + "\nClick: only the results that name it · right click: a search of it"
        };
        chip.AddThemeFontSizeOverride("font_size", 11);
        chip.AddThemeColorOverride("font_color", entity.HasEmotion ? DisseminationColors.Valence(entity.Valence).Lerp(Eve.Eve.TextBright, 0.55f) : Eve.Eve.Text);
        var selected = vm.EntityFilter == entity.Name;
        chip.AddThemeStyleboxOverride("normal", Eve.Eve.Box(color with { A = selected ? 0.35f : 0.12f }, color with { A = selected ? 1f : 0.55f }, 4, 1, 10));
        chip.AddThemeStyleboxOverride("hover", Eve.Eve.Box(color with { A = 0.3f }, color, 4, 1, 10));
        chip.AddThemeStyleboxOverride("pressed", Eve.Eve.Box(color with { A = 0.4f }, color, 4, 1, 10));
        chip.Pressed += () => vm.ToggleEntity(entity.Name);
        chip.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            {
                vm.Search(DisseminationWindowViewModel.Quoted(entity.Name));
                chip.AcceptEvent();
            }
        };
        return chip;
    }

    private static RichTextLabel Rich(string bbcode, int size)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = bbcode
        };
        label.AddThemeFontSizeOverride("normal_font_size", size);
        label.AddThemeFontSizeOverride("bold_font_size", size);
        label.AddThemeColorOverride("default_color", Eve.Eve.Text);
        return label;
    }

    /// <summary>
    /// The text of the result as BBCode, with what the search looks for lit in the colour of the search: its terms,
    /// and the names of the entities of the paragraph that answer a term (« Macron » for « Emmanuel Macron »).
    /// </summary>
    private static string Lit(DisseminationHit hit, DisseminationQuery query, Color color) => Lit(hit.Text, hit.Folded, Names(hit, query), color);

    private static List<string> Names(DisseminationHit hit, DisseminationQuery query)
    {
        var terms = query.Terms().Distinct().ToList();
        // An empty search looks for every entity: all of them are lit.
        if (terms.Count == 0)
            return hit.Entities.SelectMany(e => e.FoldedNames).Distinct().ToList();
        return terms.Concat(hit.Entities.Where(e => e.FoldedNames.Any(terms.Contains)).SelectMany(e => e.FoldedNames)).Distinct().ToList();
    }

    internal static string Lit(string text, string folded, List<string> names, Color color)
    {
        var lit = new bool[text.Length];
        if (folded.Length == text.Length)
            foreach (var name in names)
                foreach (var at in DisseminationQuery.Places(folded, name))
                    for (var i = at; i < at + name.Length && i < lit.Length; i++)
                        lit[i] = true;
        var back = (color with { A = 0.3f }).ToHtml(true);
        var bbcode = new StringBuilder(text.Length + 64);
        for (var i = 0; i < text.Length; i++)
        {
            if (lit[i] && (i == 0 || !lit[i - 1]))
                bbcode.Append($"[bgcolor=#{back}][b]");
            bbcode.Append(text[i] == '[' ? "[lb]" : text[i].ToString());
            if (lit[i] && (i == text.Length - 1 || !lit[i + 1]))
                bbcode.Append("[/b][/bgcolor]");
        }
        return bbcode.ToString();
    }

    /// <summary>
    /// READ: the whole section of a result in its own window: its title and its paragraphs (the one of the result
    /// marked), what the search looks for lit, the pictures of the section, and what opens the capture of the page and
    /// its address.
    /// </summary>
    private static void Read(DisseminationWindowViewModel vm, Shell shell, DisseminationHit hit, Color color, ThumbnailLoader loader, Action<string> openPicture)
    {
        var page = hit.Page;
        var names = Names(hit, vm.Selected.Query);
        var column = Ui.Column(10,
            Eve.Eve.Text_(page.Name, Eve.Eve.TextBright, 16, wrap: true),
            Eve.Eve.Text_($"{page.Day:dddd d MMMM yyyy} · {page.Website}{(page.PlanName.Length > 0 ? " · " + page.PlanName : string.Empty)}", Eve.Eve.Muted, 11, wrap: true));
        var capture = Ui.Action("🖼 CAPTURE OF THE PAGE", () => openPicture(page.RenderingPath), tooltip: "Open the capture of the whole page at its real size");
        capture.Disabled = !page.HasRendering;
        column.AddChild(Ui.Row(8, capture, Ui.Action("🔗 OPEN IN THE BROWSER", () => OS.ShellOpen(page.Url), tooltip: page.Url)));
        column.AddChild(Eve.Eve.Separator());

        var text = new StringBuilder();
        for (var i = 0; i < hit.Section.Count; i++)
        {
            var block = hit.Section[i];
            var isTitle = i == 0 && hit.SectionTitle.Length > 0;
            var body = Lit(block, DisseminationQuery.Fold(block), names, color);
            var mark = block == hit.Text ? $"[color=#{color.ToHtml(false)}]▌[/color] " : string.Empty;
            text.Append(isTitle ? $"[font_size=17][b]{mark}{body}[/b][/font_size]\n\n" : $"{mark}{body}\n\n");
        }
        column.AddChild(Rich(text.ToString().TrimEnd(), 14));

        // The pictures of every paragraph of the section, once each.
        var pictures = vm.PicturesOfSection(hit);
        if (pictures.Count > 0)
        {
            column.AddChild(Eve.Eve.Caption($"PICTURES ({pictures.Count})", Eve.Eve.Amber));
            column.AddChild(new PictureCarousel(pictures, loader, openPicture, new Vector2(420, 260)));
        }
        if (hit.Entities.Count > 0)
        {
            column.AddChild(Eve.Eve.Caption("ENTITIES", Eve.Eve.Amber));
            foreach (var entity in hit.Entities)
                column.AddChild(Eve.Eve.Text_(
                    $"{entity.Name} · {entity.Type.ToLowerInvariant()}" + (entity.HasEmotion ? $" · {entity.Leading}, valence {entity.Valence:+0.00;-0.00;0.00} · {entity.Summary}" : string.Empty),
                    DisseminationColors.Kind(entity.Type).Lerp(Eve.Eve.Text, 0.5f), 12, wrap: true));
        }

        var key = $"DisseminationRead:{page.Id}:{(uint)hit.Text.GetHashCode()}";
        if (!shell.TryBringBack(key))
            shell.OpenControl(key, page.Name, "📄", Ui.Scroll(Ui.Margin(column, 14, 10, 18, 14)), new Vector2(860, 720));
    }
}
