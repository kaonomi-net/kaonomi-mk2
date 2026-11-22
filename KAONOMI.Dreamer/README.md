[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.Dreamer

Background host for the **Dreamer** M-A-R-I agents (collect → analysis → actions). Runs the `Dreamer` sequence every 15 minutes during the night window (22:00–06:00), outside the main chat request path.

## Run

```bash
dotnet run --project KAONOMI.Dreamer
```

Press Ctrl+C to stop the loop.

## Configuration

`config.json` next to the executable: `appName`, `ollamaUrl`, `llmmodel`, Elasticsearch repositories (`dataRepository`, `dialogRepository`, `adminRepository`).

| Key | Meaning |
| --- | --- |
| `archiWiki` | `false` turns off the action that writes the wiki of the architecture (on by default) |
| `archiRepository` | The file of the repository of `/archi`; empty: `~/kaonomi/archi/workspace.json` of the user that runs the Dreamer (in a container, mount that file and give its path here) |

## Agents

| Type | Role |
| --- | --- |
| Collect | Gathers dreamer-related context |
| Analysis | Interprets collected data |
| Actions | Applies updates / records performance; writes the wiki of the architecture from `/archi` (below) |

Schedule constants (`NightStart`, `NightEnd`, `RunInterval`) are defined in `Program.cs`.

## The wiki of the architecture, from /archi

The actions agent (`DreamerActionsVAgent`) keeps the wiki of Kaonomi (`/wiki` in the chat) in step with the architecture
repository of `/archi`. `ArchiWikiAction` reads `workspace.json`, `ArchiWikiBuilder` writes the pages, and those that
changed are saved in the `wiki` index (a new version each, through `SessionManager.WikiSave`).

**The pages** (in French; the id of each is the slug of its title): `kaonomi` (the home page, with the map of the wiki
and the repository in numbers), the overview of the architecture, the business capabilities, one page per application
of Kaonomi (the chat, the server, the search engine, the vision), the agents, the AI models, the tools and APIs, the
data and knowledge, the technical base, the organisation and projects, the roadmap, the governance of AI, and
« Les autres fiches » when the repository has fact sheets of a kind without a page (a process, another application).
Every fact sheet has its section on the page of its kind: what it is, its lifecycle and its phase today, its
criticality, its risk under the EU AI Act, the fields of its kind and its relations, with links between the pages.

**What it does not do**

- It never overwrites a page changed by hand: only the pages whose last author ends with `(d'après /archi)` (this
  action, or the first writing of the wiki) are saved again; the others are kept and named in the log.
- It saves nothing that did not change: a page the same as in the wiki is left alone, so the night makes no version
  for nothing. The repository is read again only when its file changed or the day did.
- It does not fail on another repository: the sentences written for the architecture of Kaonomi itself (what happens
  to a question asked in the chat, to a collection on the web…) name fact sheets; a block that names one the
  repository no longer has, or says something it no longer shows, is left out. The description of a fact sheet is
  shown in French while it is the one of the example; once changed in `/archi`, it is shown as it is written there.

The pages are in the format of the rich editor of `/wiki` (one paragraph per line, `#` `##` `###` headings, `**bold**`,
`[text](wiki:slug)` links), the characters it reads as formatting escaped. What a run did is in the log of the
application (`Wiki of the architecture from /archi: 2 page(s) created, 1 saved again, 12 the same…`).

## Dependencies

- `KAONOMI.Core`, M-A-R-I / Orleans hosting

## See also

- [Solution README](../README.md)
- [KAONOMI.Core](../KAONOMI.Core/README.md)
