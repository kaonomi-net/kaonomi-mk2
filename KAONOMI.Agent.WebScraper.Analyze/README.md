[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.Agent.WebScraper.Analyze

One-shot host of the agent that **analyses the web pages collected** by
[KAONOMI.Agent.WebScraper.Collect](../KAONOMI.Agent.WebScraper.Collect/README.md) (index `page`). With the model of the
advanced conversations, it settles the persons, organisations and events of each page, puts them in the ontology,
builds the relations between them as the data of a graph, analyses the emotions expressed about each one and how the
text is written, and analyses the pictures of the page. The result is one document per page in the index
**`pageanalysis`**, whose field **`sourcepageid`** is the id of the page.

## Run

```bash
cd KAONOMI.Agent.WebScraper.Analyze
dotnet run -- --plan <id of the collection plan>
```

One run, then the host stops. In the chat, **🕷 RUN** of the collection plan window offers it when the collection ends
(a question: Analyse starts it on the plan, for as many pages as were just saved; its steps show in the log of the
window), **🧠 ANALYZE** beside RUN starts it on the plan selected without collecting first, and `/process` starts it
too (WebScraper.Analyze). Ctrl+C (CANCEL in the chat) stops the run: the analyses
already saved stay.

| Argument | Effect |
| --- | --- |
| `--page <id>` | Analyses this page (repeatable), analysed already or not |
| `--plan <id>` | The pages of this collection plan |
| (none) | The newest collected pages |
| `--force` | Analyses again the pages analysed already |
| `--max <n>` | At most n pages in this run (`maxPagesPerRun` by default) |

Without `--page`, a page analysed already is left out, unless its content changed since (its `contentHash`).

Needs: the search engine (`http://localhost:9200`), the Mongo engine for the cluster (`mongodb://localhost:27017`) and
the LLM gateway (`ollamaUrl`, `http://localhost:11435`) with its model server (LM Studio or Ollama, `llmProvider`).

## The models

- **Text**: the model of the advanced conversations of the chat, `modes.advanced` of its `config.yml` (edited in
  `/models`); `advancedModel` of `config.json` names another one; `llmmodel` when neither is found.
- **Pictures**: `modes.image` of the same file; `imageModel` names another one.

The `config.yml` read is the one beside the binaries of the Godot chat
(`KAONOMI.ChatGodot/.godot/mono/temp/bin/Debug/config.yml`), else the one of its project; `chatConfigFile` gives
another path. The answers of the semantic cache of the gateway are not used (`X-Gateway-Cache: off`).

## What it does

For each page, its sequence of three M-A-R-I agents runs once; the analysis is saved at the end of each stage.

| Agent | Role |
| --- | --- |
| Collection | **Entities** and **ontology** |
| Analysis | **Writing**, **relations**, **emotions** |
| Actions | **Pictures**, then the analysis saved for good |

### Entities (`EntityAnalyzer`)

The entities to analyse are the **persons** (PERSON), the **organisations** (ORGANISATION) and the **events** (EVENT).
The model reads the text of the page, in parts of `llmChunkChars` characters, with the names the NER found when the
page was collected (PERSON, ORG, EVENT…). It keeps the real ones, gives each its fullest name, its other spellings in
the text (`aliases`), who or what it is (`summary`), what the page says of it (`facts`), the date of an event, and
adds the events the page names. Only the entities and the spellings really written in the page are kept; the
`maxEntitiesPerPage` named the most often are analysed.

### Ontology (`OntologyLinker`)

Each entity is looked for in the ontology: same kind, same name or one of its synonyms, whatever the case and the
accents (a word alone, a surname for instance, does not identify an entity).

- **It is there**: the link of the page is added to its related links (`relatedLinks`), and what it lacks is
  completed: the spellings of the page as synonyms (names of several words, acronyms of organisations), a summary and
  a description when it has none, a sentence of the page as an example, the date of an event. What is written there
  already is never replaced.
- **It is not**: it is created (name, type, summary, description, synonyms, the link of the page, an example).

`ontologyStatus` of the entity tells which (`existing`, `created`), `ontologyChanges` what was added. With
`"updateOntology": false`, the ontology is only read.

### Relations (`RelationAnalyzer`)

The relations possible between two kinds of entities are defined in the agent (`possibleRelations` of the analysis:
39 of them, such as PERSON → ORGANISATION `LEADS`, `MEMBER_OF`, `WORKS_FOR`; PERSON → PERSON `MET_WITH`,
`OPPONENT_OF`; PERSON → EVENT `PARTICIPATES_IN`; ORGANISATION → ORGANISATION `PARTNER_OF`, `ACQUIRES`; EVENT → EVENT
`CAUSES`). The model gives the relations the text states between the entities of each part, each with its type among
the possible ones (`RELATED_TO` otherwise), a description, the sentence it comes from (`evidence`) and how sure it is
(`confidence`, 0 to 100).

They are kept as the data of a graph, `graph`: its **nodes** are the entities (`id`: the id of the entity of the
ontology, `name`, `type`), its **edges** the relations (`sourceId`, `targetId`, `type`, `description`, `evidence`,
`confidence`, `sourcepageid`). The agent does not write into the graph database (KAONOMI.graphbase): the data is
ready to feed it.

### Emotions (`EmotionAnalyzer`)

For each person, organisation and event, the model reads the passages of the page that name it and tells what they
express about it: the share of seven emotions in per cent (angry, disgust, fearful, happy, neutral, sad, surprised, as
the emotion engine of the dialogs), the leading one, its intensity (0 to 1), the valence (-1 to 1) and one sentence
saying why (`emotion` of the entity).

### Writing (`WritingAnalyzer`)

How the text is written, for the whole page (`writing` of the analysis) and for the passages about each entity
(`writing` of the entity): the number of sentences and words, the **mean length of the sentences**
(`averageSentenceLength`, in words), the **complexity of the vocabulary** (`vocabularyComplexity`, 0 to 100, from the
share of different words, the mean length of the words and the share of long words, each given too), the **number of
adverbs** (`adverbCount`) and the **number of sentences in the conditional** (`conditionalSentenceCount`).

No model is used: everything is counted on the words, in French and in English, by the same code as `/entity` of the chat
(`KAONOMI.ChatGodot/Shared/Services/WritingCounter.cs`, compiled into this agent too). The adverbs are the words in -ment
(-ly) that are not known nouns, and a list of common adverbs; a sentence is in the conditional when one of its words
has an ending of the conditional (-erait, -irait, -drait, -rrait, aurait, serait… ; would, could, should, might).
These are counts to compare texts, not a grammatical analysis: some words are taken for what they are not.

### Pictures (`ImageAnalyzer`)

The pictures downloaded with the page (JPEG, PNG or WebP, from `minImageBytes` to `maxImageBytes`, the
`maxImagesPerPage` largest) are shown to the image model: what each one shows (`description`), its kind (photo,
drawing, chart, logo, map, screenshot), the text written in it, and the entities of the page it is about. Nobody is
recognised by their face: an entity is tied to a picture only when its caption (the `alt` of the picture) or a text
written in it names it. A page imported by hand has no picture downloaded: none is analysed.

### Time

A grain call fails after 10 minutes: each of the three stages has `stageBudgetMinutes` (8). What is not done by then
(the end of a long text, some emotions, some pictures) is left out and told in `warnings` of the analysis, with the
answers the model did not give. A long front page is read up to `maxTextChars` characters.

## Configuration

`config.json` next to the executable: the usual Kaonomi settings and `advancedModel`, `imageModel`, `chatConfigFile`,
`updateOntology`, `maxPagesPerRun`, `stageBudgetMinutes`, `llmTimeoutSeconds`, `llmChunkChars`, `maxTextChars`,
`maxEntitiesPerPage`, `emotionBatchSize`, `passageChars`, `maxImagesPerPage`, `minImageBytes`, `maxImageBytes`.

The keys are read as they are written in the classes (`url`, not `Url`, for the address of a repository).

Silo ports: 11119 / gateway 30008.

## Dependencies

- `KAONOMI.Core` (models `Page`, `PageAnalysis`, `Ontology`), M-A-R-I / Orleans hosting

## See also

- [Solution README](../README.md)
- [KAONOMI.Core](../KAONOMI.Core/README.md)
- [KAONOMI.Agent.WebScraper.Collect](../KAONOMI.Agent.WebScraper.Collect/README.md)
