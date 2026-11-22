[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.Core

Shared class library for the Kaonomi platform: domain models, Elasticsearch persistence (`NEST`), Ollama HTTP helpers, configuration loading, and the `SessionManager` façade used by the server and agent hosts.

## Responsibilities

- **Models** — `EntityBase`, dialogs, logs, performance, completions, LLM request/response DTOs, `Cache`, `SymbolicPattern`, `MessageFlow` (ticker messages: `text`, `color`, `priority`, `url`), search criteria, service return values; and, among the documents of the chat:
  - `Intel` (name, description, `categories`, triggers, related and excluded entities, events, signal, tags, instruction, actions, start and end dates)
  - `CollectionPlan` (the plans of the web scraper: name, description, project, instructions, active, and its `websites`, each with its `urlProcesses`: url, mode fixed / explore / random, `isMainPage` (a front page, whose content changes with the news: the home page of a site, the front page of a section), keywords)
  - `Page` (a web page collected by `KAONOMI.Agent.WebScraper.Collect`, index `page`: `name` (its title, with the day `yyyyMMdd` for a main page), `sections` (its content by titles and paragraphs), `entities` (name, type, count), `urls` (every URL it links to), `images` (each with its `alt` and its `context`: the text around it in the page, which ties it to a paragraph), `renderingPath` and `folder` (`Pictures/kaonomi/scraper/<plan>/<website>/<yyyyMMdd>`), `isImported` (a page its site refused to the agent, kept from what the user copied in a browser); its id is made of the plan, the URL and, for a main page, the day, so that collecting it again overwrites it)
  - `DetectedEntity` (an entity found by the NER, index `detectedentity`: in a dialog, `origin` "dialog", or in a page collected by the web scraper, `origin` "page" with `sourcepageid`, `sourceUrl`, `collectionPlanId` and `context`; `DetectedEntitiesOfPageSave` writes the entities of a page and marks deleted those it no longer holds)
  - `PageAnalysis` (the analysis of a collected page by `KAONOMI.Agent.WebScraper.Analyze`, index `pageanalysis`, one document per page: `sourcepageid` (the id of the page), `entities` (its persons, organisations and events: what was done in the ontology, their emotional analysis, how the passages about them are written), `possibleRelations` and `graph` (the relations as nodes and edges), `writing` (sentences, vocabulary, adverbs, conditional), `images` (the analysis of its pictures))
  - `Cluster` (the groups drawn with the pen; `category`: where it was drawn, graphnode, bpmnai, infra, mindmap)
  - `Annotation` (its `AnnotationTargetTypes`, with `DiagramElement` and `MindmapTopic`)
  - `GameScore` (the scores of `/game:solitaire`, game `game1`: score, lines, durationSeconds, end, comment)
  - `UserActivities` (the journal of the activities, index `useractivities`: action, windowName, link, description)
- **Data** — `ElasticRepository` (save, load, field search with match + case-insensitive wildcard; `DeleteAll<T>` erases a whole index, used by `DELETE /log`); the index of a type is its name in lower case (`UserActivities` → `useractivities`)
- **Helpers** — `HelperOllamaClient`, `HelperFooocusClient` (image generation through the Fooocus API), `HelperConfigManager`, parsers/tools, hashing
- **SessionManager** — high-level save/get/search for logs (and erase them all), dialogs, cache, symbolic patterns, message flows, token usage, intels, collection plans, collected pages (`PageSave`, `PageGet`, `PagesLast`) and their analyses (`PageAnalysisSave`, `PageAnalysisGet`), game scores (the best of a game), the journal of the activities
- **Hosting** — MARI/Orleans silo builder extensions (`UseKaonomiNode`). Every host (the server and the agents) is a silo of the same cluster, on this machine. A host that was killed stays "active" in the membership table (MongoDB): `UseKaonomiNode` lets a silo declare it dead alone (direct probes only, one vote), else a server started without the agents went on sending its grain calls to them (`Unable to connect to S127.0.0.1:11112…`, a 500 of `/aiassistant`)

## Target framework

.NET 8.

## Dependencies

- `NEST` 7.x (Elasticsearch)
- Microsoft Orleans / MARI-related packages used by hosting extensions

## Configuration consumers

Projects that reference Core typically load a `config.json` with:

- `ollamaUrl`
- `llmProvider` (`ollama`, or `lmstudio` when `ollamaUrl` is the address of LM Studio)
- `llmmodel`
- `dataRepository` / `dialogRepository` / `adminRepository` (Elasticsearch URL, user, password, prefix)

## Build

```bash
dotnet build KAONOMI.Core/KAONOMI.Core.csproj
```

## See also

- [Solution README](../README.md)
- [KAONOMI.Server](../KAONOMI.Server/README.md)
