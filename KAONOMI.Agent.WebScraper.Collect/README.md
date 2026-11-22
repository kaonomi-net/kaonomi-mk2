[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.Agent.WebScraper.Collect

One-shot host of the agent that **collects the web pages of the collection plans**. Started on demand (it never runs
in a loop), its M-A-R-I agents collect each URL with a headless browser and save each page in the search engine, index
`page`: its content cut by titles and paragraphs, its entities (name and type, from the NER of the Kaonomi server),
every URL it links to, its pictures and the picture of the whole page as the browser draws it.

## Run

**🕷 RUN** of the collection plan window of the chat (`/collectionplan`) starts it on the plan selected
(`--plan <id>`); `/process` (WebScraper.Collect) and a terminal start it on the active collection plans.

```bash
cd KAONOMI.Agent.WebScraper.Collect
dotnet run -- --plan <id of the collection plan>
```

One run, then the host stops. Ctrl+C (or CANCEL in the chat) stops the run: the pages already saved stay.

| Argument | Effect |
| --- | --- |
| `--plan <id>` | Collects this collection plan now, active or not: every URL of its websites |
| `--url <url>` | Collects this URL instead of the collection plans (repeatable) |
| `--mode fixed\|explore\|random` | Mode of the `--url` URLs (fixed by default) |
| `--main` | The `--url` URLs are main pages |
| `--keywords "a,b"` | Keywords of the `--url` URLs |
| `--import <file> --url <url>` | With `--plan <id>`: keeps the content of the file (a page the user copied in a browser, HTML or plain text) as the page of that URL, without asking anything to the site (PASTE PAGE of the chat) |
| (none) | The active collection plans (`isActive`, not deleted) |

Needs: the search engine (`http://localhost:9200`), the Mongo engine for the cluster (`mongodb://localhost:27017`),
the Kaonomi server for the NER (`dialogServerUrl`, `http://localhost:49165`; without it the pages are saved without
entities, with a warning), and Google Chrome or Microsoft Edge (else the browser of Playwright, installed with
`pwsh bin/Debug/net8.0/playwright.ps1 install chromium`).

## What it does

| Agent | Role |
| --- | --- |
| Collection | The URLs to collect, each with its mode, its keywords and whether it is a main page: those of the plan asked, of the active plans, or of the command line |
| Analysis | Collects each URL with the headless browser: `fixed` the page only, `explore` the links of the same site up to `maxDepth` / `maxPagesPerUrl` (links holding a keyword first), `random` the page and `randomPages` of its links. Respects robots.txt. **Saves each page as soon as it is collected.** Stops at `runBudgetMinutes` (the rest goes to the next run) |
| Actions | Reports what was saved: pages, entities, URLs, pictures, renderings |

### The page saved (index `page`, model `Page` of KAONOMI.Core)

- **Name and id**. A URL ticked **Main page** in the collection plan (a front page, whose content changes with the
  news) is named `<title of the page> <yyyyMMdd>` and has one document per day: collected again the same day, it is
  **overwritten**; another day, it is a new document. Any other page (a URL not ticked, or a page reached by a link in
  explore or random mode) is named by its title and has one document, overwritten each time it is collected. The id
  says it: it is made of the collection plan, the URL (without fragment) and, for a main page, the day.
- **`sections`**: the content by titles and paragraphs, in the order of the page. Each title (h1 to h6) opens a
  section holding the paragraphs up to the next title (p, li, blockquote, dd, figcaption, pre); what comes before the
  first title is a section without title. Menus, headers and footers of the page, forms and scripts are left out, and
  so are the blocks shorter than `minParagraphLength` characters. `text` is the whole of it.
- **`entities`**: what `POST /ner` of the server finds in the titles and paragraphs, each entity once: its `name`
  (the title of the ontology when it is known there, else the text as written), its `type` (PERSON, ORG, LOC, DATE… or
  a type of the ontology) and how many times it was found (`count`), the most frequent first.
- **In `detectedentity` too**: every entity the NER finds in the page, at each place it is found, is a document of the
  index `detectedentity` (model `DetectedEntity` of KAONOMI.Core), as the entities of the dialogs are: its text, type,
  place, score, source and entity of the ontology, with `origin` "page", `sourcepageid` (the id of the page),
  `sourceUrl`, `collectionPlanId`, `part` (the part of the text given to the NER, where `start` and `end` are) and
  `context` (the passage around it). Collected again, a page writes its entities over (same page, place and type: same
  id), and those not found any more (the page changed) are marked deleted. They count in the entities found most often
  (`/detectedentity/top` of the server).
- **`urls`**: every http(s) URL the page links to, absolute, each once, in the order of the page.
- **`images`**: the pictures of the page (those shown, lazy ones included, the `<picture>` sources, the backgrounds set
  in the style of an element, og:image), each with its address, its `alt`, the text around it in the page (`context`: of its
  figure, article, list item or link, which ties it to a title or a paragraph) and the file it was downloaded to; at most
  `maxImagesPerPage`, those under `minImageBytes` (tracking pixels) left out.
- **`renderingPath`**: the picture of the whole page as the browser draws it (PNG, full page).

### The files

Everything goes to `Pictures/kaonomi/scraper/<name of the collection plan>/<name of the website>/<yyyyMMdd>/`
(`folder` of the page; the host of the URL when the website has no name; the characters a file system refuses are
replaced):

- `<title>-<id>.png`: the rendering of the page;
- `<title>-<id>-001-<name>.<ext>`, `-002-`…: its pictures.

`<title>` is the title of the page in letters and digits, `<id>` the first eight characters of the id of the
document. The files of a page collected again the same day are replaced.

### Not twice

- A page already collected in the same run (the same URL twice in a plan, or reached from two URLs) is collected once.
- A page collected less than `revisitHours` ago is not collected again when it is reached by a link (explore, random)
  or, in a run over the active plans, when it is not a main page. A main page, and every URL of the plan asked by
  `--plan`, is always collected.

### A site that refuses

A site that protects itself against robots (DataDome, Cloudflare, Akamai…) answers 401, 403 or 429 to the agent. The
agent does not go round such a protection: it says so (`Refused by the site (HTTP 403, protection against robots:
DataDome): <url> is not collected`), leaves the page out and goes on with the other URLs of the plan.

Such a page can be **imported by hand**. In the collection plan window of the chat, the pages refused during RUN are
listed under the log: **OPEN** opens the page in the browser of the user, who reads it as any reader, selects the whole
page and copies it; **PASTE PAGE** then gives what was copied to the agent (`--import`), which keeps it as the page of
that URL (`isImported`), named and identified as a page it would have collected: cut by titles and paragraphs, with
its entities and its links. What is copied has no `<title>`: a main page is named by its website and the day, any
other page by its first main title. Nothing is asked to the site: there is no rendering, and the pictures are listed
by their address but not downloaded. It is one gesture per page (`PageImporter`).

## Configuration

`config.json` next to the executable: the usual Kaonomi settings and `runBudgetMinutes`, `revisitHours`,
`maxPagesPerUrl`, `maxDepth`, `randomPages`, `maxImagesPerPage`, `minImageBytes`, `minParagraphLength`,
`pageTimeoutSeconds`, `delayBetweenPagesMs`, `respectRobotsTxt`, `browserChannel` (empty: Chrome, else Edge, else the
browser of Playwright), `userAgent`, `outputFolder` (empty: `Pictures/kaonomi/scraper`).

The keys are read as they are written in the classes (`url`, not `Url`, for the address of a repository): a key
written otherwise is ignored and its default value is used.

Silo ports: 11118 / gateway 30007.

## Dependencies

- `KAONOMI.Core` (models `Page`, `CollectionPlan`), M-A-R-I / Orleans hosting
- Microsoft.Playwright, HtmlAgilityPack

## See also

- [Solution README](../README.md)
- [KAONOMI.Core](../KAONOMI.Core/README.md)
- [KAONOMI.ChatGodot](../KAONOMI.ChatGodot/README.md): `/collectionplan`
