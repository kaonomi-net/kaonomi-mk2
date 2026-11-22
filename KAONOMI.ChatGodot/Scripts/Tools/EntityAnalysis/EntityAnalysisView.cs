using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;
using KAONOMI.ChatGodot.Tools.Dissemination;
using Day = KAONOMI.Chat.ViewModels.EntityAnalysisWindowViewModel.Day;
using DayState = KAONOMI.Chat.ViewModels.EntityAnalysisWindowViewModel.DayState;
using Link = KAONOMI.Chat.ViewModels.EntityAnalysisWindowViewModel.Link;
using Passage = KAONOMI.Chat.ViewModels.EntityAnalysisWindowViewModel.Passage;

namespace KAONOMI.ChatGodot.Tools.EntityAnalysis;

/// <summary>
/// /entity:&lt;name&gt;: what the pages collected say of an entity, day by day. At the top, the name, the ways it is
/// written (each can be unticked: the days are computed again), REFRESH, ANALYSE AGAIN, CANCEL and COPY REPORT; the
/// days as a timeline (emotions, passages, valence). On the left a card per day, the newest first: its emotions, the
/// persons, places and events linked to the entity (a click opens /entity of that name), the summary of the day, the
/// sources and their writing (sentences in the conditional, adverbs, counted; what the model says of them), and the
/// passages (the entity lit, the page in the browser or its capture). On the right the whole period: its summary, the
/// writing of each source, and the persons, places and events linked the most.
/// </summary>
[ToolView(typeof(EntityAnalysisWindowViewModel), "Entity", "◆", 1450, 960)]
public sealed class EntityAnalysisView : IToolView
{
    private static readonly Color PlaceColor = new(0.66f, 0.47f, 0.93f);

    public Control Build(object viewModel, Shell shell)
    {
        var vm = (EntityAnalysisWindowViewModel)viewModel;
        var cards = new Dictionary<DateOnly, PanelContainer>();

        void OpenPicture(string path)
        {
            if (vm.OpenImage(path) is { } analysis)
                shell.Open(analysis, title: analysis.Title);
        }

        void OpenEntity(string name) => shell.RunCommand("/entity:" + name);

        // ── The top ──
        var name = Eve.Eve.Text_(vm.Query, Eve.Eve.TextBright, 20);
        var facts = Eve.Eve.Text_(string.Empty, Eve.Eve.Muted, 12, wrap: true);
        facts.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var cancel = Ui.Command("■ CANCEL", vm.CancelCommand, tooltip: "Stop the model: the days read so far are kept");
        Bind.Visible(cancel, vm, nameof(vm.IsRunning));
        var copy = Ui.Action("⧉ COPY REPORT", () =>
        {
            DisplayServer.ClipboardSet(vm.Report());
            vm.Status = "The report (Markdown) is copied";
        }, tooltip: "Copy the whole analysis as Markdown, to paste it in a document");
        var header = Ui.Toolbar(Ui.Column(6,
            Ui.Row(10, name, Ui.Fill(facts),
                Ui.Command("⟳ REFRESH", vm.RefreshCommand, tooltip: "Read the pages again; the days not analysed yet (new, or whose passages changed) are analysed"),
                Ui.Command("▶ ANALYSE AGAIN", vm.AnalyseAgainCommand, tooltip: "Every day read again by the model, whatever was kept"),
                cancel, copy)));

        var aliases = new HFlowContainer();
        aliases.AddThemeConstantOverride("h_separation", 6);
        aliases.AddThemeConstantOverride("v_separation", 4);

        var timeline = new EntityTimeline(vm);
        var timelineBox = Panel(Ui.Column(4,
            Ui.Row(8, Eve.Eve.Caption("DAY BY DAY", Eve.Eve.Amber),
                Eve.Eve.Text_("the strip: the emotions · the bar: the passages · the line: the valence (high: positive) · click a day: its card", Eve.Eve.Muted, 11)),
            timeline));

        // ── Left: the days ──
        var days = new VBoxContainer();
        days.AddThemeConstantOverride("separation", 10);
        days.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var daysScroll = Ui.FillHeight(Ui.Scroll(Ui.Margin(days, 0, 0, 10, 8)));
        daysScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        daysScroll.SizeFlagsStretchRatio = 2.2f;
        timeline.Chosen += day =>
        {
            if (cards.TryGetValue(day.Date, out var card) && GodotObject.IsInstanceValid(card))
                daysScroll.EnsureControlVisible(card);
        };

        // ── Right: the whole period ──
        var overview = new VBoxContainer();
        overview.AddThemeConstantOverride("separation", 10);
        var right = Ui.Scroll(Ui.Margin(overview, 0, 0, 10, 8));
        // The texts of the period wrap to the width of the panel (no scrolling sideways).
        right.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        right.CustomMinimumSize = new Vector2(420, 0);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        body.AddChild(daysScroll);
        body.AddChild(right);

        var page = Ui.Column(6, header, aliases, timelineBox, body, Ui.StatusBar(vm, nameof(vm.Status), nameof(vm.IsError), nameof(vm.IsRunning)));

        void RefreshTop()
        {
            name.Text = vm.DisplayName;
            facts.Text = vm.Days.Count == 0
                ? $"model {vm.Model}"
                : $"{vm.PassageCount} passage(s) in {vm.PageCount} page(s) · {vm.Websites.Count} source(s): {string.Join(", ", vm.Websites)} · {vm.Days.Count} day(s), "
                  + $"{vm.Days.Min(d => d.Date):dd/MM/yyyy} → {vm.Days.Max(d => d.Date):dd/MM/yyyy} · model {vm.Model}";
            Clear(aliases);
            if (vm.Aliases.Count > 0)
                aliases.AddChild(Eve.Eve.Caption("WRITTEN AS", Eve.Eve.Muted, 11));
            foreach (var alias in vm.Aliases)
                aliases.AddChild(AliasChip(vm, alias));
            if (vm.Suggestions.Count > 0)
            {
                aliases.AddChild(Eve.Eve.Caption("NAMES OF THE PAGES CLOSE TO IT", Eve.Eve.Muted, 11));
                foreach (var suggestion in vm.Suggestions)
                    aliases.AddChild(Ui.Action(suggestion, () => OpenEntity(suggestion), tooltip: $"/entity:{suggestion}"));
            }
            timeline.QueueRedraw();
        }

        void RefreshDay(Day day)
        {
            if (!cards.TryGetValue(day.Date, out var card) || !GodotObject.IsInstanceValid(card))
                return;
            Clear(card);
            card.AddChild(DayContent(vm, day, OpenEntity, OpenPicture));
            timeline.QueueRedraw();
            RefreshTop();
            RefreshOverview();
        }

        void RefreshDays()
        {
            Clear(days);
            cards.Clear();
            foreach (var day in vm.Days)
            {
                var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                card.AddThemeStyleboxOverride("panel", Eve.Eve.Card());
                card.AddChild(DayContent(vm, day, OpenEntity, OpenPicture));
                cards[day.Date] = card;
                days.AddChild(card);
            }
            if (vm.Days.Count == 0 && !vm.IsRunning)
                days.AddChild(Ui.Placeholder(vm.Aliases.Count == 0 ? $"No page collected names « {vm.Query} »" : "No passage with the ways of writing ticked"));
        }

        void RefreshOverview()
        {
            Clear(overview);
            overview.AddChild(Eve.Eve.Caption("THE WHOLE PERIOD", Eve.Eve.Amber));
            if (vm.OverviewReading)
            {
                overview.AddChild(new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4) });
                overview.AddChild(Eve.Eve.Text_($"{vm.Model} writes the summary…", Eve.Eve.Muted, 12, wrap: true));
            }
            else if (vm.Overview.Length > 0)
            {
                overview.AddChild(Paragraph(vm.Overview, 13));
                overview.AddChild(Eve.Eve.Caption("SOURCES", Eve.Eve.Muted, 11));
                overview.AddChild(Paragraph(vm.OverviewSources, 12));
                overview.AddChild(Eve.Eve.Caption("WRITING", Eve.Eve.Muted, 11));
                overview.AddChild(Paragraph(vm.OverviewWriting, 12));
            }
            else if (vm.OverviewError.Length > 0)
                overview.AddChild(Eve.Eve.Text_("The summary could not be written: " + vm.OverviewError, Eve.Eve.Red, 12, wrap: true));
            else
                overview.AddChild(Eve.Eve.Text_(vm.Days.Count == 0 ? "No day to sum up" : "Written once the days are analysed", Eve.Eve.Muted, 12, wrap: true));

            // The writing of each source over the period, counted.
            var totals = vm.SourceTotals();
            if (totals.Count > 0)
            {
                overview.AddChild(Eve.Eve.Caption("WRITING OF EACH SOURCE (COUNTED)", Eve.Eve.Muted, 11));
                var grid = Ui.Grid(4, 10, 4);
                foreach (var title in new[] { "Source", "Passages", "Conditional", "Adverbs" })
                    grid.AddChild(Eve.Eve.Text_(title, Eve.Eve.Muted, 11));
                foreach (var source in totals)
                {
                    var website = Eve.Eve.Text_(source.Website, Eve.Eve.TextBright, 12);
                    website.TooltipText = $"{source.Website}: {source.Days} day(s), {source.Pages} page(s), {source.Passages} passage(s), {source.Sentences} sentence(s) "
                                          + $"of {source.AverageSentenceLength:0.#} words on average, {source.Conditional} in the conditional, {source.Adverbs} adverb(s)";
                    website.MouseFilter = Control.MouseFilterEnum.Stop;
                    website.ClipText = true;
                    website.CustomMinimumSize = new Vector2(90, 0);
                    website.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    grid.AddChild(website);
                    grid.AddChild(Eve.Eve.Text_($"{source.Passages} · {source.Days} d", Eve.Eve.Text, 12));
                    grid.AddChild(Eve.Eve.Text_($"{source.Conditional} · {source.ConditionalShare:0} %", source.Conditional > 0 ? Eve.Eve.Amber : Eve.Eve.Text, 12));
                    grid.AddChild(Eve.Eve.Text_($"{source.Adverbs} · {source.AdverbsPer100Words:0.#}/100 w", Eve.Eve.Text, 12));
                }
                overview.AddChild(grid);
                overview.AddChild(Eve.Eve.Text_("Conditional: the sentences with a verb in the conditional, and their share of the sentences. Adverbs: how many, per 100 words. "
                                                + "Counted on the passages that name the entity, without a model.", Eve.Eve.Muted, 11, wrap: true));
            }

            // The persons, places and events linked the most.
            foreach (var (title, analysed, found, color) in new (string, Func<Day, List<Link>>, Func<Day, List<Link>>, Color)[]
                     {
                         ("PERSONS LINKED THE MOST", d => d.Persons, d => d.FoundPersons, DisseminationColors.Kind("PERSON")),
                         ("PLACES LINKED THE MOST", d => d.Places, d => d.FoundPlaces, PlaceColor),
                         ("EVENTS LINKED THE MOST", d => d.Events, d => d.FoundEvents, DisseminationColors.Kind("EVENT"))
                     })
            {
                var links = vm.TopLinks(analysed, found);
                if (links.Count == 0)
                    continue;
                var bars = new RankBars { Hint = "Click: /entity of this name" };
                bars.Set(links.Select(l => (l.Name, l.Mentions, color)), null);
                bars.Chosen += OpenEntity;
                overview.AddChild(Panel(Ui.Column(4, Eve.Eve.Caption(title, Eve.Eve.Muted, 11), bars)));
            }
        }

        void RefreshAll()
        {
            if (!GodotObject.IsInstanceValid(page))
                return;
            RefreshTop();
            RefreshDays();
            RefreshOverview();
        }

        Action changed = () => Bind.OnMain(RefreshAll);
        Action<Day> dayChanged = day => Bind.OnMain(() =>
        {
            if (GodotObject.IsInstanceValid(page))
                RefreshDay(day);
        });
        Action overviewChanged = () => Bind.OnMain(() =>
        {
            if (GodotObject.IsInstanceValid(page))
                RefreshOverview();
        });
        vm.Changed += changed;
        vm.DayChanged += dayChanged;
        vm.OverviewChanged += overviewChanged;
        Bind.OnGone(page, () =>
        {
            vm.Changed -= changed;
            vm.DayChanged -= dayChanged;
            vm.OverviewChanged -= overviewChanged;
        });
        RefreshAll();
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

    private static Label Paragraph(string text, int size)
    {
        var label = Eve.Eve.Text_(text, Eve.Eve.Text, size, wrap: true);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>A way of writing the entity: ticked, its passages are analysed; a click ticks or unticks it.</summary>
    private static Button AliasChip(EntityAnalysisWindowViewModel vm, EntityAnalysisWindowViewModel.Alias alias)
    {
        var color = alias.Type.Length > 0 ? DisseminationColors.Kind(alias.Type.ToUpperInvariant() is "PER" or "PERS" ? "PERSON" : alias.Type.ToUpperInvariant() is "ORG" ? "ORGANISATION" : alias.Type.ToUpperInvariant())
            : Eve.Eve.Muted;
        var chip = new Button
        {
            Text = $"{(alias.Chosen ? "☑" : "☐")}  {alias.Name}  {alias.Mentions}",
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"{alias.Name} · {(alias.Type.Length > 0 ? alias.Type.ToLowerInvariant() + ", " : string.Empty)}found by the {alias.Origin} · written in {alias.Mentions} title(s) or paragraph(s)\n"
                          + (alias.Chosen ? "Ticked: its passages are analysed. Click: left out" : "Left out. Click: its passages analysed too")
        };
        chip.AddThemeFontSizeOverride("font_size", 12);
        chip.AddThemeColorOverride("font_color", alias.Chosen ? Eve.Eve.TextBright : Eve.Eve.Muted);
        chip.AddThemeStyleboxOverride("normal", Eve.Eve.Box(color with { A = alias.Chosen ? 0.22f : 0.05f }, color with { A = alias.Chosen ? 0.9f : 0.35f }, 4, 1, 10));
        chip.AddThemeStyleboxOverride("hover", Eve.Eve.Box(color with { A = 0.3f }, color, 4, 1, 10));
        chip.AddThemeStyleboxOverride("pressed", Eve.Eve.Box(color with { A = 0.4f }, color, 4, 1, 10));
        chip.Pressed += () => vm.ToggleAlias(alias);
        return chip;
    }

    /// <summary>
    /// The card of a day: its date and counts, its emotions, the persons, places and events linked to the entity, its
    /// summary, its sources and their writing, and its passages.
    /// </summary>
    private static Control DayContent(EntityAnalysisWindowViewModel vm, Day day, Action<string> openEntity, Action<string> openPicture)
    {
        var column = Ui.Column(8);
        var state = day.State switch
        {
            DayState.Reading => Ui.Badge("being read…", Eve.Eve.Amber),
            DayState.Failed => Ui.Badge("not analysed", Eve.Eve.Red),
            DayState.Done when day.FromCache => Ui.Badge("kept", Eve.Eve.Green),
            DayState.Done => Ui.Badge("analysed", Eve.Eve.Green),
            _ => Ui.Badge("waiting", Eve.Eve.Muted)
        };
        state.TooltipText = day.State switch
        {
            DayState.Done when day.FromCache => $"Analysed by {day.Model} at an earlier opening: the passages of the day did not change",
            DayState.Done => $"Analysed by {day.Model}, {day.PassagesRead} passage(s) of {day.Passages.Count} read",
            DayState.Failed => day.Error,
            _ => string.Empty
        };
        column.AddChild(Ui.Row(10,
            Eve.Eve.Text_(day.Date.ToString("dddd d MMMM yyyy"), Eve.Eve.TextBright, 16),
            Eve.Eve.Text_($"{day.Passages.Count} passage(s) · {day.Pages} page(s) · {day.Sources.Count} source(s)", Eve.Eve.Muted, 12),
            Ui.Spring(),
            state));
        if (day.State == DayState.Reading)
            column.AddChild(new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4) });
        if (day.State == DayState.Failed)
            column.AddChild(Eve.Eve.Text_("The model gave no analysis: " + day.Error, Eve.Eve.Red, 12, wrap: true));

        // The emotions and the summary.
        if (day.Analysed)
        {
            var emotions = new EmotionBar();
            emotions.Set(day.Emotions, day.Valence);
            var feeling = Eve.Eve.Text_($"{day.Leading} · intensity {day.Intensity:0.00} · valence {day.Valence:+0.00;-0.00;0.00}\n{day.EmotionSummary}",
                DisseminationColors.Emotion(day.Leading).Lerp(Eve.Eve.Text, 0.35f), 12, wrap: true);
            feeling.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var emotionBox = Ui.Column(4, Eve.Eve.Caption("EMOTIONS", Eve.Eve.Muted, 11), emotions);
            emotionBox.CustomMinimumSize = new Vector2(300, 0);
            var summary = Ui.Column(4, Eve.Eve.Caption("SUMMARY", Eve.Eve.Muted, 11), Paragraph(day.Summary, 13), feeling);
            summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            column.AddChild(Ui.Row(12, emotionBox, summary));
        }

        // The persons, places and events: those of the model (with their role), else those of the entity recogniser.
        var links = Ui.Row(12);
        foreach (var (title, said, found, color) in new (string, List<Link>, List<Link>, Color)[]
                 {
                     ("PERSONS", day.Persons, day.FoundPersons, DisseminationColors.Kind("PERSON")),
                     ("PLACES", day.Places, day.FoundPlaces, PlaceColor),
                     ("EVENTS", day.Events, day.FoundEvents, DisseminationColors.Kind("EVENT"))
                 })
        {
            var shown = day.Analysed && said.Count > 0 ? said : found;
            var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 4);
            foreach (var link in shown)
                flow.AddChild(LinkChip(link, color, openEntity, fromModel: day.Analysed && said.Count > 0));
            if (shown.Count == 0)
                flow.AddChild(Eve.Eve.Text_("none", Eve.Eve.Muted, 11));
            var box = Ui.Column(4, Eve.Eve.Caption(title, Eve.Eve.Muted, 11), flow);
            box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            links.AddChild(box);
        }
        column.AddChild(links);

        // The sources and their writing.
        column.AddChild(Eve.Eve.Caption("SOURCES & WRITING", Eve.Eve.Muted, 11));
        if (day.WritingNotes.Length > 0)
            column.AddChild(Paragraph(day.WritingNotes, 12));
        var grid = Ui.Grid(6, 12, 3);
        foreach (var title in new[] { "Source", "Passages", "Sentences", "Conditional", "Adverbs", "Tone" })
            grid.AddChild(Eve.Eve.Text_(title, Eve.Eve.Muted, 11));
        foreach (var source in day.Sources)
        {
            var w = source.Writing;
            grid.AddChild(Eve.Eve.Text_(source.Website, Eve.Eve.TextBright, 12));
            grid.AddChild(Eve.Eve.Text_($"{source.Passages} ({source.Pages} p.)", Eve.Eve.Text, 12));
            grid.AddChild(Eve.Eve.Text_(w is null ? "–" : $"{w.SentenceCount} · {w.AverageSentenceLength:0.#} w", Eve.Eve.Text, 12));
            grid.AddChild(Eve.Eve.Text_(w is null ? "–" : w.ConditionalSentenceCount.ToString(), w is { ConditionalSentenceCount: > 0 } ? Eve.Eve.Amber : Eve.Eve.Text, 12));
            grid.AddChild(Eve.Eve.Text_(w is null ? "–" : w.AdverbCount.ToString(), Eve.Eve.Text, 12));
            var tone = Eve.Eve.Text_(source.Tone.Length > 0 ? source.Tone : "–", Eve.Eve.Text, 12);
            tone.TooltipText = source.Notes;
            tone.MouseFilter = Control.MouseFilterEnum.Stop;
            grid.AddChild(tone);
        }
        column.AddChild(grid);
        if (day.Adverbs.Count > 0)
            column.AddChild(Eve.Eve.Text_("Adverbs: " + string.Join(" · ", day.Adverbs.Take(14).Select(a => a.Count > 1 ? $"{a.Adverb} ×{a.Count}" : a.Adverb)), Eve.Eve.Muted, 12, wrap: true));
        if (day.Conditionals.Count > 0)
            column.AddChild(Fold($"{day.Conditionals.Count} sentence(s) in the conditional", () =>
            {
                var list = Ui.Column(4);
                foreach (var sentence in day.Conditionals)
                    list.AddChild(Eve.Eve.Text_("« " + sentence + " »", Eve.Eve.Text, 12, wrap: true));
                return list;
            }));

        // The passages.
        column.AddChild(Fold($"{day.Passages.Count} passage(s)", () =>
        {
            var names = vm.Aliases.Where(a => a.Chosen).Select(a => a.Folded).ToList();
            var list = Ui.Column(6);
            foreach (var passage in day.Passages)
                list.AddChild(PassageRow(passage, names, openPicture));
            return list;
        }));
        return column;
    }

    /// <summary>A person, a place or an event: a click opens /entity of its name; its role (said by the model) in its tooltip.</summary>
    private static Button LinkChip(Link link, Color color, Action<string> openEntity, bool fromModel)
    {
        var chip = new Button
        {
            Text = link.Mentions > 0 ? $"{link.Name}  {link.Mentions}" : link.Name,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = (link.Role.Length > 0 ? link.Role + "\n" : string.Empty)
                          + (link.Mentions > 0 ? $"Named in {link.Mentions} passage(s) of the day\n" : string.Empty)
                          + (fromModel ? "Linked by the model" : "Found near the entity by the entity recogniser") + "\nClick: /entity of this name"
        };
        chip.AddThemeFontSizeOverride("font_size", 11);
        chip.AddThemeColorOverride("font_color", Eve.Eve.TextBright);
        chip.AddThemeStyleboxOverride("normal", Eve.Eve.Box(color with { A = fromModel ? 0.18f : 0.07f }, color with { A = fromModel ? 0.8f : 0.4f }, 4, 1, 10));
        chip.AddThemeStyleboxOverride("hover", Eve.Eve.Box(color with { A = 0.3f }, color, 4, 1, 10));
        chip.AddThemeStyleboxOverride("pressed", Eve.Eve.Box(color with { A = 0.4f }, color, 4, 1, 10));
        chip.Pressed += () => openEntity(link.Name);
        return chip;
    }

    /// <summary>A part shown on demand: ▸ its title; the content is built the first time it opens.</summary>
    private static Control Fold(string title, Func<Control> build)
    {
        var holder = Ui.Column(4);
        Control? content = null;
        var button = new Button { Text = "▸ " + title, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.AddThemeColorOverride("font_color", Eve.Eve.Cyan);
        button.Pressed += () =>
        {
            if (content is null)
            {
                content = Ui.Margin(build(), 14, 0, 0, 4);
                holder.AddChild(content);
            }
            else
                content.Visible = !content.Visible;
            button.Text = (content.Visible ? "▾ " : "▸ ") + title;
        };
        holder.AddChild(button);
        return holder;
    }

    /// <summary>A passage: its source, its section, its text with the entity lit, and what opens its page or the capture of it.</summary>
    private static Control PassageRow(Passage passage, List<string> names, Action<string> openPicture)
    {
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = DisseminationView.Lit(passage.Text, passage.Folded, names, Eve.Eve.Cyan)
        };
        text.AddThemeFontSizeOverride("normal_font_size", passage.IsTitle ? 14 : 12);
        text.AddThemeFontSizeOverride("bold_font_size", passage.IsTitle ? 14 : 12);
        text.AddThemeColorOverride("default_color", Eve.Eve.Text);
        var capture = Ui.Action("🖼", () => openPicture(passage.RenderingPath), tooltip: "The capture of the whole page, at its real size");
        capture.Disabled = !passage.HasRendering;
        var header = Ui.Row(6, Ui.Badge(passage.Website, Eve.Eve.Cyan),
            Eve.Eve.Text_(passage.PageName, Eve.Eve.Muted, 11), Ui.Spring(), capture,
            Ui.Action("🔗", () => OS.ShellOpen(passage.Url), tooltip: $"The page in the browser\n{passage.Url}"));
        if (!passage.Names)
        {
            // It speaks of the entity without its name: shown lighter.
            text.Modulate = new Color(1, 1, 1, 0.7f);
            header.AddChild(Eve.Eve.Text_("without its name", Eve.Eve.Muted, 10));
        }
        var column = Ui.Column(2, header);
        if (!passage.IsTitle && passage.SectionTitle.Length > 0)
            column.AddChild(Eve.Eve.Text_(passage.SectionTitle, Eve.Eve.Muted, 11, wrap: true));
        column.AddChild(text);
        return column;
    }
}
