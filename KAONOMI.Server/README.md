[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.Server

ASP.NET Core REST host that saves dialogs, runs a M-A-R-I agent sequence against Ollama, and exposes message-flow, cache, and symbolic-pattern APIs. Swagger UI is available at startup.

## Run

```bash
dotnet run --project KAONOMI.Server
```

The server listens in **plain HTTP**, on `http://localhost:49165`, on this machine only (127.0.0.1 and ::1; the port is
`httpsPort` in `config.json`, its name kept for the files already written). There is no HTTPS and no certificate any more
(the former `~/kaonomi/certs` and `HelperServerCertificate` are gone); an address of this machine left in `https` in a
`config.yml` of the chat is read in `http`.

```bash
curl http://localhost:49165/swagger/index.html
```

Nothing is opened in the browser at the start: Swagger is at `/swagger` (`/rest` in the chat). The M-A-R-I (Orleans)
dashboard is served on `http://localhost:8090`: `/orleans` in the chat shows it in a web window.

## Configuration

`config.json` next to the executable:

| Key | Purpose |
| --- | --- |
| `appName` | Application identity for logs |
| `ollamaUrl` | Local Ollama base URL |
| `fooocusUrl` | Fooocus API base URL, for the image routes (default `http://127.0.0.1:8888`) |
| `llmmodel` | Default model name |
| `dataRepository` / `dialogRepository` / `adminRepository` | Elasticsearch endpoints |

## Main endpoint

`POST /aiassistant` — body (`DialogGenericRequest`):

```json
{
  "model": "mistral-small3.2",
  "stream": false,
  "messages": [
    { "role": "user", "content": "Hello" }
  ]
}
```

Flow: persist dialog → run M-A-R-I sequence `jfo` with the dialog id → return `DialogGenericResponse` (chat-completion style).

## NER: the entities of a text

```http
POST /ner
{ "text": "Jérôme Fortias présente Kaonomi à Paris le 12/11/2026, voir https://www.kaonomi.net.", "types": [] }
```

```json
{ "entities": [
    { "text": "Jérôme Fortias", "type": "PERSON",   "start": 0,  "end": 14, "score": 0.95, "source": "xlmr+ontology", "ontologyId": "…", "ontologyName": "Jerome Fortias" },
    { "text": "Kaonomi",        "type": "SOFTWARE", "start": 24, "end": 31, "score": 0.92, "source": "xlmr+ontology", "ontologyId": "…", "ontologyName": "Kaonomi" },
    { "text": "Paris",          "type": "CITY",     "start": 34, "end": 39, "score": 0.64, "source": "xlmr+ontology", "ontologyId": "…", "ontologyName": "Paris" },
    { "text": "12/11/2026",     "type": "DATE",     "start": 43, "end": 53, "score": 1,    "source": "rule" },
    { "text": "https://www.kaonomi.net", "type": "URL", "start": 60, "end": 83, "score": 1, "source": "rule" } ],
  "durationMs": 8.2, "model": "FacebookAI/xlm-roberta-large-finetuned-conll03-english int8 (model_quantized.onnx)", "warnings": [] }
```

`types` (optional) keeps only those types. `start` / `end` are positions of characters in `text` (end excluded).
`GET /ner/types` lists the types of each layer, the number of entities of the ontology read, and the state of the model.

Three layers (`Ner/`):

| Layer | What | Types |
| --- | --- | --- |
| Model | [`FacebookAI/xlm-roberta-large-finetuned-conll03-english`](https://huggingface.co/FacebookAI/xlm-roberta-large-finetuned-conll03-english) (XLM-RoBERTa large fine-tuned on CoNLL-2003; trained on English, it reads French and the other languages of XLM-R), exported to ONNX and quantized in **int8** (561 MB), run on the CPU by ONNX Runtime: about 20 ms a sentence. The former [`Davlan/xlm-roberta-base-ner-hrl`](https://huggingface.co/Davlan/xlm-roberta-base-ner-hrl) (278 MB, about 5 ms, with `DATE`) is used when the large one is not there | `PERSON`, `ORG`, `LOC`, `MISC` |
| Rules | the extractor of `KAONOMI.LLMGateway` (its source compiled here too, the gateway stays on its own) | `URL`, `EMAIL`, `IP`, `DATE`, `TIME`, `MONEY`, `PERCENT`, `PHONE`, `NUMBER`, `FILE`, `HASHTAG`, `MENTION`, `CODE`, `PROGRAMMING_LANGUAGE`, `MATH` |
| Ontology | every entity of the ontology (not a class, not deleted), by its name and its synonyms, whatever the case and the accents, the longest name first, on whole words; read again every 10 minutes | the types of the ontology (`SOFTWARE`, `HARDWARE`, `COUNTRY`, `CITY`, `EVENT`…) |

How they combine: a URL, an e-mail, a file, code, an IP, a hashtag or a mention is kept whole (no name looked for
inside it); a name of the model that a name of the ontology covers whole (the same, or a longer one) takes the type
and the id of the ontology (`xlmr+ontology`), but a name of the ontology inside a longer name of the model is not that
name ("Olaf", the OLAF of the ontology, in "Olaf Scholz" leaves Olaf Scholz a person); a name of the ontology the model
did not see is added (`ontology`); the other rules (dates,
amounts…) fill where no name is. The model's own output is made whole: a word it cut in two ("J" + "érôme") is one
entity, a lone punctuation ("'" of "l'ESA") is dropped. Without the model folder, the rules and the ontology still
answer, with a warning.

The tokenizer (`Ner/XlmrTokenizer.cs`, SentencePiece Unigram read from `tokenizer.json`) gives the same pieces and
positions as the one of Hugging Face, and the entities before they are made whole are those of the pipeline of
Transformers on the same int8 model (checked on 30 French, English, German and Spanish sentences; the int8 model finds
the same entities as the original fp32 one in 28 of them).

On the 229 paragraphs of a page of Wikipedia (Commission européenne), the large model finds 577 names where the base one
found 403 (persons and organizations missed before, `MISC` for the adjectives of nationality and the events).

The agent of the entities of the dialog sequence (`DialogEntityVAgent`) runs the same NER on **the question of the
user alone**: not the previous exchanges the chat writes before it (their entities, and their knowledge, came back on
every prompt), nor the name of the preset it is addressed to ("MARI please …" brought the knowledge of the framework
MARI to every question). It keeps these entities with the dialog (`Dialog.Entities`). The model and the ontology are
loaded in the background when the server starts.

A name the model finds that the ontology does not write the same way still finds its entity when it is a typo
(`OntologyGazetteer.Closest`): the same number of words, each with the same first letter and one letter apart at
most (two from eight letters), two in all ("Jérôme Forties" → Jérôme Fortias; a one-word name needs six letters;
two entities as close: none). Its `source` is then `xlmr+ontology~`.

**From the entities to the model.** The same agent then collects what Kaonomi knows about these entities
(`Dialog.Knowledge`, code in `Knowledge/EntityKnowledge.cs`), and the processor (`DialogProcessorVAgent`) uses it:

1. **NER**: the entities of the question (`DialogEntityVAgent`). A follow-up that names nothing ("Il est né
   quand ?", "Qui l'a créé ?", "Et où ?": it starts with "et" or holds a word that points back, `ContextManager.IsFollowUp`),
   or a question about an entity the ontology says nothing it may write, takes the subject of **the last exchange**:
   the entities of the question of the user, else of the answer. A question that follows nothing ("Combien font
   17 fois 23 ?") gets nothing from the history.
2. **Ontology**: each entity the ontology knows is read there; its type, synonyms, summary (`summary`), full
   `description` and dates become an item of knowledge when it has one of them. Each entity it lists as related
   (`relatedEntities`, by name, synonym or id) and that the prompt does not name gives its summary only, once (the description the seed writes, « Cité par 3 intels : … »,
   is not one). An entity classified SECRET or above is never written in a prompt.
3. **Intels of the entities**: the intels are matched (weights, trigger level, excluded entities, as in the
   pre-processing) against the question read with the names and the synonyms the ontology gives to its entities; an
   intel that matches gives its instructions, places, times, related entities and questions. For a prompt, **one entity
   of the intel at least must be written as it is**: a typo alone does not count (`IntelMatcher.Match(text,
   exactEntity: true)`; fuzzy matches on common words gave "connais" → Cannabis, "aussi" → ANSSI, "passe" → PASSI,
   "automatisation" → Automutilation). The 3 best at most.
4. **Compression** (`Knowledge/PromptCompressor.cs`, rules only, no model): the knowledge is written at the end of the
   system prompt, with what is not about the question left to the general knowledge of the model (repeated sentences
   left out, also inside a description, each item cut to its share, at most `promptKnowledgeMaxChars`
   characters, 3000 by default); the previous exchanges the chat writes before the question are shortened, then the
   oldest dropped, until the whole fits `promptMaxChars` (12000 by default). The question itself is never changed.
5. **LLM gateway**: the processor asks the model at `ollamaUrl` (the gateway when it points to it: semantic cache and
   router). The knowledge is in the system prompt, so the gateway counts it as context and still compares the question
   alone with its cache.

**The context of a conversation.** A request may name its conversation (`sessionId`; the chat gives one per
conversation, a new one at each start and each new conversation; a batch sends none). The server then keeps the context
of that conversation (`DialogContext`, index `dialogcontext`, its id the id of the session; `GET
/dialogcontext/{sessionId}` reads it):

- after the answer, `DialogContextUpdateVAgent` adds the exchange (the question without the name it is addressed to,
  the answer, and their entities, found by the NER on both);
- before the next question, `DialogProcessorVAgent` writes this context in the place of the exchanges the chat wrote in
  the prompt, the older an exchange the more compressed (`Knowledge/ContextManager.cs`, by rules, no model):

| Level | Exchanges (config.json) | Question / answer kept |
|---|---|---|
| recent | the last `contextRecentTurns` (2) | as they were said |
| short | the `contextShortTurns` before (4) | 300 / 600 characters |
| brief | the `contextBriefTurns` before (8) | 160 / 220 characters |
| digest | the older ones (60 at most) | 100 / 120 characters, cut for good: the original texts are dropped |

An entity **comes back** when it is in two exchanges or more (the question being asked counts as one). At every level,
the sentences that name such an entity are kept beyond the limit, up to as many characters again: what the conversation
keeps talking about stays, the rest fades. Above `contextMaxChars` (6000), the oldest exchanges are left out. A question
asked again right away (its answer was disliked) replaces its exchange. A request without `sessionId` is answered as
before, with the exchanges its prompt holds.

The dialog keeps what was sent (`PromptSentSystemText`, `PromptSentUserText`) and what the compression did
(`PromptCompression`: characters before and after, items of knowledge, previous messages kept); each step is logged.

**The model** is not in git. It is in `~/kaonomi/models/ner/xlm-roberta-large-finetuned-conll03-english-int8`
(`nerModelFolder` in config.json to put it elsewhere; without it, the former `xlm-roberta-base-ner-hrl-int8` is taken
when the large one is missing). To make it again, with Python 3.10 or later:

```bash
python -m venv ~/kaonomi/tools/ner-export
~/kaonomi/tools/ner-export/Scripts/python -m pip install torch --index-url https://download.pytorch.org/whl/cpu
~/kaonomi/tools/ner-export/Scripts/python -m pip install "optimum[onnxruntime]" transformers sentencepiece
~/kaonomi/tools/ner-export/Scripts/python KAONOMI.Server/Ner/export/ner_export.py ~/kaonomi/models/ner/xlm-roberta-large-finetuned-conll03-english-int8
# the former model: … ner_export.py ~/kaonomi/models/ner/xlm-roberta-base-ner-hrl-int8 Davlan/xlm-roberta-base-ner-hrl
```

(on macOS and Linux: `bin/python` instead of `Scripts/python`). On Windows, keep the environment on a short path: the
files of PyTorch go beyond 260 characters in a deep folder. The script also writes `reference.json`, the pieces and the
entities of Python for 30 sentences, to compare with.

## The assistant does not introduce itself again

The persona of the preset of the chat ("Tu t'appelles…") is the system prompt of every prompt; alone, it made the
model say who it is on each answer. Three things prevent it (`DialogProcessorVAgent`, `Knowledge/PromptCompressor.cs`):

- **Once the conversation has exchanges** (a context kept by the server, or "Discussions précédentes" in the prompt),
  the processor adds to the system prompt the instruction not to introduce itself, not to greet again and not to
  write its name before the answer: the prompt `dialog.ongoing` of `/prompts` of the chat. The first prompt of a
  conversation does not get it.
- **The history names no one**: the answers of the assistant are labelled `Assistant : ` (they were `Kaonomi : `,
  which the model took for its name whatever the persona said; the old label is still read). The header of the
  knowledge block says "What you know…" for the same reason.
- **A label copied from the history** at the start of an answer ("Assistant : …", "**Kaonomi :** …") is removed.
- **The name the question is addressed to is not sent to the model**: the chat writes "KAONOMI please ça va ?"
  (the preset in use), and the model took the name for the subject ("Le KAONOMI est en bonne santé…"). The processor
  sends "ça va ?" (`ContextManager.WithoutAddress`; `PromptSentUserText` of the dialog shows it). The request and
  `PromptUserText` keep the prompt as it came, for the agents before the processor.

## The chat never repeats itself

When the model gives an answer it **already gave in the conversation**, the processor (`DialogProcessorVAgent`) does
not return it: it sends the prompt again with the instruction not to repeat itself, up to 2 times
(`Knowledge/AnswerRepeat.cs`).

- **Compared with**: the assistant messages of the request and the answers of the dialogs of the session
  (`sessionId`), the deleted ones and the failures left out.
- **The same answer**: the same text once the case, the punctuation and the Markdown are left out, or 90 % of the
  words in common for a close length. Answers under 20 letters ("Oui.", "42") are not compared.
- **Made of pieces of what was said**: an answer that copies no single previous answer but whose pairs of
  consecutive words were already written, for 70 % of them, in the previous answers taken together, is a repetition
  too, when it is a recitation: 20 pairs at least (about 20 words). A short answer that says a fact again
  ("Kaonomi a été créé par Jérôme Fortias.") answers a new question and is kept.
- **Asked again**: the instruction is added at the end of the prompt and of the system prompt, followed in the prompt
  by every answer not to say again (the one repeated, and the ones the tries fall back on); the request goes past the
  semantic cache of the gateway (`X-Gateway-Cache: off`) and the temperature rises on each try (1.1, then 1.4). **The
  last try leaves out the previous exchanges**: a history where the same answer comes back makes a small model copy
  it once more. The instruction is the prompt `dialog.norepeat` of `/prompts` of the chat.
- **A repeated answer is never returned**: when the model still repeats itself after 2 tries (or fails), the
  assistant says it has nothing new to add, with a sentence it did not say yet in the conversation: the prompt
  `dialog.norepeat.fallback` of `/prompts`, one sentence per line (5 by default; once all said, the first one comes
  back numbered).
- **Kept on the dialog**: `RepeatRetries` (how many times the model was asked again), `RepeatFallback` (the answer is
  the sentence above, not the one of the model) and `PromptSentUserText` (the prompt of the last try); the tokens of
  the tries add up.

### A mere reaction of the user: "cool", "merci"

A message that asks nothing (4 words at most, no question mark, none of the words of a request such as "plus",
"pourquoi", "dis", "continue": `ContextManager.IsRequest`) and names no entity is not a question about the previous
exchanges. Sent with them and with the knowledge of their entities, a small model had nothing to answer and recited
them again. So:

- `DialogEntityVAgent` gives no knowledge for it;
- `DialogProcessorVAgent` sends the message alone, without the previous exchanges, with the instruction to answer in
  one short sentence: the prompt `dialog.reaction` of `/prompts` of the chat.

A follow-up such as "tu sais m'en dire plus ?" is a request: it keeps the history and the knowledge.

## Emotions of the prompts: the emotion engine

The emotion engine of the dialog sequence (`DialogEmotionEngineVAgent`) looks at the emotions of the person who
writes. **Every 3 prompts of a conversation, it asks the model the emotions of these 3 prompts together** and keeps
them as one document of the index `detectedemotion` (`DetectedEmotion`); the two prompts in between are only
counted. A prompt asked again (regenerate) is not another prompt. A failure never stops the answer: it is logged.
**The analysis runs in the background**: it no longer holds the answer back (about 1 s every 3 prompts; the model
serves both requests at once), and it is asked past the semantic cache of the gateway (`X-Gateway-Cache: off`: the
cache gave back the emotions of other conversations for messages that looked alike).

```json
{ "id": "detectedemotion-<dialog>", "sessionId": "…", "dialogId": "<the 3rd dialog>", "dialogIds": ["…", "…", "…"],
  "prompts": ["…", "…", "…"], "promptNumber": 3,
  "emotion": "angry",
  "emotions": { "angry": 85.7, "disgust": 4.8, "fearful": 4.8, "happy": 0, "neutral": 4.8, "sad": 0, "surprised": 0 },
  "intensity": 9, "valence": -0.8, "summary": "…", "model": "lfm3-omni-2b", "promptVersion": 1 }
```

`emotions` is the share of each emotion in per cent (they add up to 100): the same seven as the ones `/vision`
reads on a face, so that what is written and what is shown can be compared. `intensity` goes from 0 to 10, `valence`
from -1 (very unpleasant) to 1 (very pleasant). The model is the one of the dialog; what it is told is the prompt
`dialog.emotions`, edited in `/prompts` of the chat (`promptVersion`: the version used, 0 for the default of the
code).

### What if emotion: a text of the system prompt for each emotion

The engine runs **before the processor** (after the model selection), so that what it finds changes the answer.
For each of the seven emotions, `/emotion` of the chat keeps a text as the prompt `emotion.<name>`
(`emotion.angry`, `emotion.happy`…) of the index `prompttemplate`: versions, never erased; emptying a text is one
more version. When the latest version of the text of the emotion that leads is not empty, the engine writes it on the
dialog (`EmotionSystemText`, with `Emotion` and `EmotionPromptVersion`) and the processor adds it at the end of the
system prompt sent to the model (`PromptSystemText` of the dialog shows it). Each prompt gets the text of the last
emotion found in the conversation: the analysis of the 3rd prompt leads **from the 4th** (it runs in the background
while the 3rd is answered), the one of the 6th from the 7th, and so on; the first three prompts of a conversation have
none. `systemText` and `systemTextVersion` of the `detectedemotion` document say what applies from the next prompt
(empty and 0: nothing).

Routes: `GET /detectedemotion/last?minutes=0&size=50` (the newest first; of the last minutes, or all of them) and
`GET /detectedemotion/session/{sessionId}` (the ones of a conversation, the oldest first).

## Prompt templates: the versions of the prompts of the chat

The prompts the tools of the chat send to the models are edited in `/prompts`. Each change is a new version, one
document of the index `prompttemplate` (`key`, `version`, `text`, `source`: `config`, `edit`, `upload`, `restore`,
`default`; `comment`; who and when), its id `prompt-<key>-v<version>`. The version in use is the one with the largest
number. A version is written once: `PUT /prompttemplate` refuses a version that exists, and there is **no route to
change or erase one**.

Routes: `PUT /prompttemplate` (a new version), `POST /prompttemplate/search` (the key, the title, the text, the
comment; body `SearchCriterias`), `POST /prompttemplate/last`, `GET /prompttemplate/{id}`.

## The tools of the chat whose store is the search engine

`/calendar`, `/archi`, `/todo`, `/support`, `/obs` and `/entity:` of the chat keep their store here, the chat keeping a
copy on its machine to work without the server (`LocalSync` of the chat). One index per tool, the same shape for all
(`SyncedRecord`): `calendarevent`, `archiitem`, `daytodo`, `supportticket`, `vaultnote`, `entityanalysis`. Each item
has its id (given by the chat), `title`, `type` and `text` (what the searches read), `payload` (the item as the chat
keeps it, its JSON in base 64, so that the searches do not read its field names), `revision` (a new one at each save:
two machines compare it to know who changed what), `isDeleted` (never erased: a deleted item stays, marked, for the
other machines to delete theirs). Versioned at each save. `POST /search` looks into them.

Routes: `PUT /synced/{kind}` (an item created, written again or marked deleted), `POST /synced/{kind}/all` (every
item of the kind, the deleted ones included, newest first; body `SearchCriterias`, `size` at most 10000).

## The messages of the internal mail of the chat

The messages of `/message` (the internal mail between the users of the chat) are files of the machine of the chat
(`~/kaonomi/messages`); the chat writes each one sent into the index `mail` (`Mail`), for the searches (`POST /search`
looks into it): its id is the one of its file, the sender and the receivers (logins and names), the subject, the text
(its pictures by their file names), the entities found when it was sent. Never the drafts nor the notes of the users;
an encrypted message gives its envelope only (sender, receivers, subject, date). Never erased: marked deleted once
every user removed it. The chat compares its mail with the index when it starts, then every 10 minutes.

Routes: `PUT /mail` and `POST /mail/update` (saved again with the same id), `POST /mail/search` (subject, text,
sender, receivers, entities; body `SearchCriterias`), `POST /mail/last`, `GET /mail/{id}`, `DELETE /mail/{id}`.

## The versions of the documents: every state saved is kept

The documents the user edits in the windows of the chat are **versioned at every save**, by the server itself
(`ElasticRepository.SaveImpl`): their classes are marked `IVersioned` — `annotation`, `bpmn`, `infra`, `cachebuilder`,
`cluster`, `collectionplan`, `cv`, `diary`, `ghostwriter`, `idea`, `imageanalysis`, `intel`, `interview`, `mindmap`,
`ontology`, `preset`, `project`, `relation`, `symbolicpattern`, `taskitem`, `whitenote`, `wiki`, `youtubevideo`, and the
items of `calendarevent`, `archiitem`, `daytodo`, `supportticket`, `vaultnote`, `entityanalysis`. Each
save writes the whole document as it was saved into the index `documentversion` (`DocumentVersion`, one document per
version, `ver-<key>-v<n>`, written once, never erased), with what changed since the version before (`changed`: the
fields, the date aside) and what made it (`operation`): `save`, `delete` (marked deleted), `erase` (its last state,
kept just before it is erased for good) or `restore`. A save that changes nothing makes no version. The newest
number of each document is kept in memory by the server (no extra read at each save).

```json
{ "id": "ver-18875192e3ecf70585fe93b9-v2", "documentIndex": "idea", "documentId": "3ffe0b98-…", "version": 2,
  "operation": "save", "changed": ["ideaDescription"], "documentTitle": "…", "json": "{…the idea as saved…}",
  "size": 412, "date": "…", "userName": "jerome", "appName": "KAONOMI.Chat" }
```

Routes: `GET /documentversion/{index}/{id}` (the versions of a document, the newest first, without their JSON),
`GET /documentversion/{versionId}` (one version with its document), `POST /documentversion/last` (the newest versions
of every document) and `POST /documentversion/restore` `{ "id": "<version>" }`: the document is written back as it
was in that version, which is kept as a new version (`restore`). Restoring takes back the whole document, its
own counters too (the `version` of a wiki page or a diagram, `isDeleted`).

## Video notes: what /video keeps, versioned

The player of `/video` in the chat keeps everything done on a video as **notes**, index `videonote` (`VideoNote`):
an analysis of a segment (`segment`: the sheet of its pictures and what is said, read by a vision model), of a frame
(`frame`), of the sound (`sound`: the transcript, who speaks, the measures), a picture kept (`picture`) or an
annotation (`annotation`). **One document per version** (`videonote-<noteId>-v<n>`), written once: editing, running
an analysis again, restoring a former version or deleting a note writes a new version (`operation`: `create`, `edit`,
`rerun`, `restore`, `delete`, `project`; `isDeleted` on the version that deletes). `projectId` is the project chosen in the player: changing it writes a version of each note with the new one. The note is its version with the largest
number.

```json
{ "id": "videonote-3f2a…-v2", "noteId": "3f2a…", "version": 2, "kind": "segment", "operation": "rerun",
  "videoKey": "a547fb09…", "videoPath": "/Users/…/presentation.mp4", "videoName": "presentation.mp4", "videoDuration": 171.4,
  "start": 5, "end": 25, "question": "De quoi parle cette vidéo ?", "text": "**Résumé** …", "transcript": "…",
  "speakers": ["00:05.0 → 00:24.9  Jerome"], "model": "lfm3-omni-2b", "promptVersion": 1, "picture": "<JPEG base 64>",
  "durationMs": 19300, "isDeleted": false }
```

`videoKey` is made of the path and the size of the file. Routes: `PUT /videonote` (a new version; refused when it
exists), `GET /videonote/{id}`, `GET /videonote/video/{videoKey}` (every version of every note of a video, the oldest
first), `POST /videonote/search`, `POST /videonote/last`. No route writes a version over or erases one.

## Faces: what /vision records

While `/vision` of the chat is open (its **RECORD** switch on), what it sees of each face goes to the search
engine, summed up every second: who it is, the share of each emotion and how much the face moves. Two kinds of
documents, so that every second is not a document:

- **`faceepisode`**: a period where the state of a face stays the same (the same person, the same leading emotion,
  the same activity). It is written again every second while it lasts (the same id); another one starts when
  something changes and holds 2 seconds, when the face leaves, or after 60 seconds.
- **`faceobservation`**: the second where something changed (`change`: `appeared`, `emotion`, `identity`,
  `activity`, `left`, `stopped`; `previous`: what it was before), with the id of the episode that starts there.

```json
{ "id": "faceepisode-f6a30896e63b-1-2", "session": "f6a30896e63b", "track": 1,
  "name": "Anna", "known": true, "similarity": 0.81,
  "start": "2026-11-12T17:01:53.567Z", "end": "2026-11-12T17:01:58.631Z", "seconds": 5.1, "frames": 117,
  "emotion": "happy",
  "emotions": { "angry": 1.2, "disgust": 0.4, "fearful": 0.9, "happy": 78.5, "neutral": 15.1, "sad": 1.6, "surprised": 2.3 },
  "area": { "min": 2.51, "max": 2.57, "mean": 2.56, "std": 0.02 },
  "movement": { "min": 0, "max": 24.9, "mean": 0.4, "std": 2.8 },
  "activity": "still", "closedBy": "activity" }
```

| Field | What it is |
| --- | --- |
| `session`, `track` | One opening of the window; the face followed in it (two unknown faces are two tracks) |
| `name`, `known`, `similarity` | Who it is (`unknown` when not recognised), and how sure (cosine similarity, 0 to 1, averaged) |
| `emotions` | The share of each of the seven emotions, in per cent (they add up to 100), averaged over the frames; `emotion` is the one that leads |
| `area` | The area of the rectangle of the face, in per cent of the picture: smallest, largest, mean and standard deviation (it grows when the person comes closer) |
| `movement` | How far the middle of that rectangle goes, in per cent of the width of the face per second: smallest, largest, mean and standard deviation (it does not depend on the distance to the camera) |
| `activity` | `still` (under 25), `moving` (under 120) or `agitated`, by the mean movement |
| `closedBy` | What ended the episode: `emotion`, `identity`, `activity`, `left`, `timeout`, `stopped`; empty while it lasts |

A `faceobservation` holds the same measures for its second, with `change`, `previous` and `episode` in place of
`start`, `end`, `seconds` and `closedBy`.

Routes, the same for both (`faceepisode`, `faceobservation`): `PUT /faceepisode`, `POST /faceepisode/update` (create
or write again), `POST /faceepisode/search` (the name, the emotion, the activity, the session…; body
`SearchCriterias`), `POST /faceepisode/last`, `GET /faceepisode/{id}`, `DELETE /faceepisode/{id}` (really erased).

## Objects: what /vision records

While `/vision` of the chat is open (its **RECORD** switch on), everything it detected goes to the search
engine once a minute, as one document of the index `visionobjects` (and one for the last, shorter period when the
window closes):

```json
{ "id": "visionobjects-3f9a1c2b7d10-4", "session": "3f9a1c2b7d10",
  "start": "2026-11-12T17:22:00.000Z", "end": "2026-11-12T17:23:00.000Z", "seconds": 60, "frames": 1480,
  "model": "YOLOv8 nano", "device": "mps", "threshold": 0.5,
  "objects": [
    { "name": "person", "classId": 0, "max": 2, "mean": 1.12, "presence": 98.4, "confidence": 0.88, "best": 0.93, "firstSeen": 0, "lastSeen": 59.9 },
    { "name": "cup", "classId": 41, "max": 1, "mean": 0.31, "presence": 31.0, "confidence": 0.61, "best": 0.74, "firstSeen": 12.4, "lastSeen": 44.0 } ],
  "names": ["person", "cup"], "maxObjects": 3, "maxPersons": 2, "overlap": 4.2, "alertClass": "wine glass", "alert": 0 }
```

For each kind of object: `max` the most of them in one frame, `mean` their mean number over the frames, `presence`
the share of the frames where one was seen (%), `confidence` and `best` how sure the detections were, `firstSeen` and
`lastSeen` in seconds from the start of the minute. `overlap` and `alert` are the share of the frames (%) where two
persons overlapped, and where an object of `alertClass` was seen.

Routes: `PUT /visionobjects`, `POST /visionobjects/update`, `POST /visionobjects/search` (the names of the objects,
the model, the session; body `SearchCriterias`), `POST /visionobjects/last`, `GET /visionobjects/{id}`,
`DELETE /visionobjects/{id}` (really erased).

## Work locations: SET LOCATION of /vision

**📍 SET LOCATION** in `/vision` of the chat measures the place where the user works: the user sits in the usual
working position and speaks for 5 seconds; the mean box of the face and the level of the voice are kept, one
document of the index `worklocation` per name of place (its id `worklocation-<name>`: measured again, written over).
With that place chosen, the chat frames the picture in red when the face is at least that big and the voice of the
last second at least that loud: the user speaks to the assistant.

```json
{ "id": "worklocation-bureau", "name": "Bureau", "faceArea": 0.0876, "faceWidth": 257, "faceHeight": 419,
  "frameWidth": 1280, "frameHeight": 960, "faceFrames": 109, "voiceLevel": 0.0188, "voiceDecibels": -34.5,
  "seconds": 5, "camera": "Elgato Facecam Pro", "microphone": "MacBook Pro Microphone" }
```

`faceArea` is the area of the box of the face as a share of the frame (0 to 1); `voiceLevel` the root mean square of
the samples heard (0 to 1), `voiceDecibels` the same in dB. At the end of the measure the chat also takes a picture of
the camera (as it is, nothing drawn on it), has it described by its vision model (`modes.image` of its config.yml)
and has the entities of that description found by the NER (`POST /ner`): `analysis` (the text), `analysisModel`,
`entities` (their names in the ontology when they are in it) and `picture` (the picture, kept on the machine of the
chat in `~/kaonomi/vision/locations/<id>.jpg`) are added to the document a moment after it is written.

Routes: `PUT /worklocation`, `POST /worklocation/update`, `POST /worklocation/search` (the name, the camera, the
microphone, the analysis of its picture, its entities; body `SearchCriterias`), `POST /worklocation/last`, `GET /worklocation/{id}`, `DELETE /worklocation/{id}`
(really erased).

## Voices: who speaks

Two services tell whose voice a sound is, and teach a voice. The sound is a **WAV file in base 64, in the JSON**
(PCM 8, 16, 24 or 32 bits, or 32-bit float; any rate, any number of channels; a data URL
`data:audio/wav;base64,…` is accepted; 1.5 s of speech at least, 120 s read at most).

```
POST /voice/enrol
{ "name": "Anna", "wav": "UklGRiQ…", "language": "FR", "source": "phone" }
```

keeps the voice of the WAV under the name. `language` (optional: `FR`, `EN`, `PT-BR`…, kept in upper case) is the
language spoken in the WAV; `source` is optional too (`REST` by default). The WAV should hold that person
speaking alone; several samples of a name (other words, another day) make the answer steadier.

```json
{ "success": true, "id": "voice-Anna-20261112-152225-555", "name": "Anna", "language": "FR", "samples": 1, "seconds": 10.6,
  "level": 0.0712, "model": "3dspeaker_speech_eres2net_sv_en_voxceleb_16k", "durationMs": 87.3,
  "message": "Anna enrolled: 10.6 s of voice in FR, 1 sample(s) of this voice" }
```

```
POST /voice/identify
{ "wav": "UklGRiQ…", "threshold": 0.5 }
```

says whose voice the WAV is: `name` is null when nobody enrolled is close enough (`score` under `threshold`, 0.5 by
default); `closest` and `scores` say how close each name is. `language` is the language of the sample of the closest
name that is the closest to the WAV (empty when it was not said at its enrolment), and `languages` the ones each name
was enrolled in. The language is an information kept with the voice: the voices are compared whatever their language.

```json
{ "success": true, "name": "Anna", "score": 0.9278, "closest": "Anna", "language": "FR", "threshold": 0.5,
  "scores": [ { "name": "Anna", "score": 0.9278, "samples": 2, "languages": ["EN", "FR"] },
              { "name": "Tom", "score": 0.0549, "samples": 1, "languages": [] } ],
  "seconds": 8.6, "level": 0.0689, "model": "3dspeaker_speech_eres2net_sv_en_voxceleb_16k", "durationMs": 85.2,
  "message": "The voice of Anna (93 %)" }
```

Both answer **400** with `success: false` and the reason in `message` when the WAV is missing, not base 64, not a
WAV, too short or silent, or the name is missing.

```bash
curl -s http://localhost:49165/voice/enrol -H 'Content-Type: application/json' \
  -d "{\"name\":\"Anna\",\"language\":\"FR\",\"wav\":\"$(base64 < anna.wav | tr -d '\n')\"}"
curl -s http://localhost:49165/voice/identify -H 'Content-Type: application/json' \
  -d "{\"wav\":\"$(base64 < who.wav | tr -d '\n')\"}"
```

**How.** Only open source, on this machine: [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) (Apache 2.0) runs the
speaker model ERes2Net of [3D-Speaker](https://github.com/modelscope/3D-Speaker) (Apache 2.0, trained on VoxCeleb),
in `~/kaonomi/models/speaker`, downloaded the first time (26 MB). A voice becomes an embedding: 192 numbers that
depend on who speaks, not on what is said; two voices are compared by the cosine of theirs. A name is as close as
the closest of its samples, or their average. A quiet voice is brought to a full level first. The sound itself is
never kept: only the embedding.

**The same voices as the chat.** A voice enrolled is a document of the index `voice` (`name`, `file`, `seconds`,
`source`, `language`, `feature`, `model`), the one `/recorder` of the chat writes for the voices it enrols (same model, same
embeddings): a voice enrolled here is recognised by the chat (it brings the document back at its next start), and a
voice enrolled in the chat is recognised here. The documents themselves: `PUT /voice`, `POST /voice/update`,
`POST /voice/search`, `POST /voice/last`, `GET /voice/{id}`, `DELETE /voice/{id}` (really erased). The service keeps
the voices in memory: read again from the search engine every minute, and followed at once when one is written or
erased through these routes.

## Other routes

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/version` | Version string |
| `GET` | `/` | Redirect to Swagger |
| `GET` | `/tokensusage/last?minutes=N` | Token usages (one per LLM call: model, prompt and completion tokens) of the last N minutes, newest first; `minutes` defaults to 60 |
| `GET` | `/log/since?minutes=N` | All the logs of the last N minutes (every application), newest first, read page by page without the 10,000 limit; `minutes` defaults to 60 |
| `GET` | `/log/last?size=N` | The N most recent logs (all applications), newest first; `size` defaults to and is capped at 10,000 |
| `DELETE` | `/log` | Erases ALL the logs (the index of the logs is deleted, created again empty at the next log); `returnedId`: the number erased |
| `GET` | `/messageflow/{id}` | Get ticker message |
| `POST` | `/messageflow/search` | Search the `text` field (`term`) |
| `POST` | `/messageflow/last` | Up to `size` messages matching `term` (`*` when empty) |
| `POST` | `/messageflow/update` | Save (create or overwrite) a ticker message |
| `PUT` | `/messageflow` | Create ticker message |
| `GET` | `/cache/{id}` | Get cache document |
| `POST` | `/cache/search` | Search the `prompt` field (`term`) |
| `POST` | `/cache/last` | Up to `size` cache entries matching `term` (`*` when empty) |
| `POST` | `/cache/update` | Update cache document |
| `PUT` | `/cache` | Create cache document |
| `GET` | `/symbolicpattern/{id}` | Get symbolic pattern |
| `POST` | `/symbolicpattern/search` | Search the `regexPattern` field (`term`) |
| `POST` | `/symbolicpattern/last` | Up to `size` patterns matching `term` (`*` when empty) |
| `POST` | `/symbolicpattern/update` | Update pattern |
| `PUT` | `/symbolicpattern` | Create pattern |
| `GET` | `/idea/{id}` | Get idea (404 when it does not exist) |
| `POST` | `/idea/search` | Search `ideaTitle`, `ideaDescription`, `category` and `tags` (`term`, `size`; `*` or empty: newest ideas) |
| `POST` | `/idea/last` | The `size` most recent ideas, newest first |
| `POST` | `/idea/update` | Update idea |
| `PUT` | `/idea` | Create idea (date set to now when missing) |
| `DELETE` | `/idea/{id}` | Delete idea |
| `GET` | `/diary/{id}` | Get diary entry (404 when it does not exist) |
| `POST` | `/diary/search` | Search the `text` field only (`term`, `size`; `*` or empty: newest entries); deleted entries are left out |
| `POST` | `/diary/last` | The `size` most recent diary entries, newest first, without the deleted ones |
| `POST` | `/diary/update` | Update diary entry |
| `PUT` | `/diary` | Create diary entry (date set to now when missing) |
| `DELETE` | `/diary/{id}` | Soft delete: nothing is erased, the entry gets `isDeleted = true` (still readable with `GET`, restored by `POST /diary/update` with `isDeleted = false`) |
| `GET` | `/task/{id}` | Get task, deleted or not (404 when it does not exist) |
| `POST` | `/task/search` | Search `name`, `description`, `type`, `status` and `comment` (`term`, `size`; `*` or empty: newest tasks); deleted tasks are left out |
| `POST` | `/task/last` | The `size` most recent tasks, newest first, without the deleted ones |
| `POST` | `/task/update` | Update task |
| `PUT` | `/task` | Create task (date set to now when missing) |
| `DELETE` | `/task/{id}` | Soft delete: nothing is erased, the task gets `isDeleted = true` (still readable with `GET`, restored by `POST /task/update` with `isDeleted = false`) |
| `GET` | `/intel/{id}` | Get intel, deleted or not (404 when it does not exist) |
| `POST` | `/intel/search` | Search all the text fields: `name`, `description`, `categories`, `triggers`, `relatedEntities`, `excludedEntities`, `events`, `signal`, `tags`, `instruction`, `actions`, `comment`, `appName`, `userName`, `machineName` (`term`, `size`; `*` or empty: newest intels); deleted intels are left out |
| `POST` | `/intel/last` | The `size` most recent intels, newest first, without the deleted ones |
| `POST` | `/intel/update` | Update intel |
| `PUT` | `/intel` | Create intel (date set to now when missing) |
| `DELETE` | `/intel/{id}` | Soft delete: nothing is erased, the intel gets `isDeleted = true` (still readable with `GET`, restored by `POST /intel/update` with `isDeleted = false`) |
| `PUT` | `/useractivity` | Save an activity of the journal of the chat, index `useractivities` (`action`: SAVE, `windowName`, `link`: the command of the window, `description`) |
| `POST` | `/useractivity/last` | The `size` most recent activities, newest first |
| `POST` | `/useractivity/search` | Search `action`, `windowName`, `link`, `description` (`term`, `size`; `*` or empty: newest) |
| `PUT` | `/gamescore` | Save the score of a finished game of the chat (`game`: game1, `score`, `lines`, `durationSeconds`, `end`: stopped or blocked, `comment`) |
| `POST` | `/gamescore/best` | The `size` best scores of the game named by `term` (game1 when empty), the highest first; deleted scores left out |
| `POST` | `/gamescore/last` | The `size` most recent scores (all games), newest first |
| `DELETE` | `/gamescore/{id}` | Soft delete: the score gets `isDeleted = true` |
| `GET` | `/collectionplan/{id}` | Get collection plan (of the web scraper), deleted or not (404 when it does not exist) |
| `POST` | `/collectionplan/search` | Search all the text fields: `name`, `description`, `project`, `instructions`, `websites.name`, `websites.urlProcesses.url`, `websites.urlProcesses.keywords`, `comment`, `appName`, `userName`, `machineName` (`term`, `size`; `*` or empty: newest plans); deleted plans are left out |
| `POST` | `/collectionplan/last` | The `size` most recent collection plans, newest first, without the deleted ones |
| `POST` | `/collectionplan/update` | Update collection plan |
| `PUT` | `/collectionplan` | Create collection plan (date set to now when missing) |
| `DELETE` | `/collectionplan/{id}` | Soft delete: nothing is erased, the plan gets `isDeleted = true` (still readable with `GET`, restored by `POST /collectionplan/update` with `isDeleted = false`) |
| `GET` | `/youtubevideo/{id}` | Get YouTube video, deleted or not (404 when it does not exist) |
| `POST` | `/youtubevideo/search` | Search `title` and `description` (`term`, `size`; `*` or empty: newest videos); deleted videos left out |
| `POST` | `/youtubevideo/last` | The `size` most recent YouTube videos, newest first, without the deleted ones |
| `POST` | `/youtubevideo/update` | Update YouTube video |
| `PUT` | `/youtubevideo` | Create YouTube video (date set to now when missing) |
| `DELETE` | `/youtubevideo/{id}` | Soft delete: sets `isDeleted = true` (restored with `/youtubevideo/update` and `isDeleted = false`) |
| `GET` | `/document/{id}` | Get document, deleted or not (404 when it does not exist) |
| `POST` | `/document/search` | Fuzzy search on `title`, `description` and the content of every version (`contents.content`) (`term`, `size`; `*` or empty: newest documents); deleted documents left out |
| `POST` | `/document/last` | The `size` most recent documents, newest first, without the deleted ones |
| `POST` | `/document/update` | Update document |
| `PUT` | `/document` | Create document (date set to now when missing) |
| `DELETE` | `/document/{id}` | Soft delete: sets `isDeleted = true` (restored with `/document/update` and `isDeleted = false`) |
| `GET` | `/completion/{id}` | Get completion (404 when it does not exist) |
| `POST` | `/completion/search` | Search `prompt` and `completion` (`term`, `size`; `*` or empty: newest completions) |
| `POST` | `/completion/last` | The `size` most recent completions, newest first |
| `POST` | `/completion/update` | Update completion |
| `PUT` | `/completion` | Create completion (prompt, completion, model, word counts…) |
| `DELETE` | `/completion/{id}` | Delete completion |
| `GET` | `/relation/{id}` | Get relation with all its versions, deleted or not (404 when it does not exist) |
| `PUT` / `POST` | `/relation`, `/relation/update` | Save a version of a relation between windows of the chat (`Relation`, index `relation`, `category` `interdata` by default). A relation is **versioned in one document**: `versions` lists them oldest first (the last one is the current one), each with its `version` number, `date`, `userName`, `nature`, `title`, `description`, `probability` (-100 absolutely impossible to 100 absolutely certain, 0 to evaluate), optional `dateFrom` / `dateTo` (nullable dates of the link), `source` and `destinations` (one or more, in the order of the string; each with `windowName`, `objectId`, `information`, `context`). Without an id (or an unknown one) the relation is created with the versions brought, numbered from 1; with the id of a stored relation, the last version brought is added with the next number, the previous ones kept. The call returns once the relation can be searched (`refresh=wait_for`), so a search right after it finds it |
| `POST` | `/relation/search` | Fuzzy search on the nature, title, description and the window, object and information of the ends, in all the versions (`term`, `size`); deleted ones left out |
| `POST` | `/relation/last` | The `size` most recently saved relations, without the deleted ones |
| `DELETE` | `/relation/{id}` | Soft delete: sets `isDeleted = true`; returns once the searches leave it out (`refresh=wait_for`) |
| `GET` | `/annotation/{id}` | Get an annotation, deleted or not (404 when it does not exist) |
| `PUT` / `POST` | `/annotation`, `/annotation/update` | Save an annotation of any object of the application (`Annotation`, index `annotation`): `text`, `tags` (normalized to upper-case hashtags, no duplicates: `software` → `#SOFTWARE`, `work of art` → `#WORK_OF_ART`), the object annotated (`targetType`: `Entity`, `TextBlock`, `Image`, `ProjectItem`, `Idea`… required; `targetId`, `targetTitle`), `excerpt` (the part annotated: a block of text, a region of an image) and `context` (window and data as JSON text, as for a white note). Without an id it is created, with the id of a stored one it is replaced (an update without a date keeps the date it was written). Refused without a text and a tag, or without `targetType`. Returns once it can be searched |
| `POST` | `/annotation/search` | Fuzzy search on the text, tags, object annotated (type, id, title), excerpt and comment (`term`, `size`); deleted ones left out |
| `POST` | `/annotation/last` | The `size` most recent annotations, without the deleted ones |
| `DELETE` | `/annotation/{id}` | Soft delete: sets `isDeleted = true` (left out of the searches at once) |
| `GET` | `/imageanalysis/{id}` | Get image analysis, deleted or not (404 when it does not exist) |
| `GET` | `/imageanalysis/image?path=…` | The analysis of the picture at this path (404 when it has none yet) |
| `POST` | `/imageanalysis/search` | Fuzzy search on `title`, `fileName`, `aiAnalysis`, `comments.text`, `annotations.comment`, `locationName`, `imagePath` (`term`, `size`); deleted ones left out |
| `POST` | `/imageanalysis/last` | The `size` most recent image analyses, without the deleted ones |
| `PUT` / `POST` | `/imageanalysis`, `/imageanalysis/update` | Save the analysis of a picture (`ImageAnalysis`, index `imageanalysis`): one per picture, its id is the SHA-256 of the path of the picture; the link `imageUrl` (file://) is set when missing; `category` is `image`, or `subimage` for the part of a picture cut out by an annotation rectangle (`parentId`, `parentImagePath`, and the origin in its metadata) |
| `DELETE` | `/imageanalysis/{id}` | Soft delete: sets `isDeleted = true` |
| `GET` | `/image/status` | State of the Fooocus API: version, default model, `busy` (503 when it does not answer) |
| `GET` | `/image/options` | Styles, performances, aspect ratios and models an image generation accepts, and the defaults |
| `POST` | `/image/generate` | Generates images from a prompt with Fooocus (body: `ImageGenerationRequest`, see [Images](#images-fooocus)); 400 for an empty prompt, 502 when Fooocus fails |

The multi-field searches (`/idea/search`, `/diary/search`, `/task/search`, `/intel/search`, `/youtubevideo/search`, `/document/search`, `/completion/search`) take the logical operators `and`, `or` (any case; `and` binds tighter than `or`) and parentheses: `recette and (sauce or soupe)`. Each term between operators is searched as before (all its words, fuzzy, or as a part of the text, in any of the fields); a "quoted text" is one term even with `and` / `or` inside; a missing closing parenthesis is accepted.

Search bodies use `SearchCriterias`:

```json
{ "size": 100, "term": "*" }
```

Notes:

- `/search` routes only use `term`; `size` is ignored there.
- `/last` routes apply no sort: they return the first `size` hits in search-relevance order, not necessarily the most recent documents.
- `update` / `PUT` routes return a `ServiceReturn` (`ReturnedId`, `Message`); `ReturnedId` is `"-1"` on failure.

### Images (Fooocus)

The `/image` routes generate images with [Fooocus](https://github.com/lllyasviel/Fooocus) (Stable Diffusion XL), through a small REST API that runs in the Python environment of an installed Fooocus: [`Fooocus/fooocus_api.py`](Fooocus/fooocus_api.py). It starts Fooocus without its web interface (same models, presets, styles and image log, in the `outputs` folder of Fooocus) and listens on `127.0.0.1:8888` (`--host`, `--port`; `--fooocus` or `FOOOCUS_HOME` for the folder of Fooocus, the Pinokio install `~/pinokio/api/fooocus.git/app` by default). Start it before using the routes (the first generation loads the model, about a minute):

```bash
~/pinokio/api/fooocus.git/app/env/bin/python Fooocus/fooocus_api.py
```

Or with a Fooocus cloned in `~/kaonomi/fooocus` (a Python 3.10 venv in its `env` folder, made with `uv venv --python 3.10 --seed env` plus `fastapi`, `uvicorn` and `pygit2`; its first start installs torch and the Fooocus packages and downloads the models, about 8 GB):

```bash
cd ~/kaonomi/fooocus && env/bin/python ~/Projects/kaonomi/KAONOMI.Server/Fooocus/fooocus_api.py --fooocus ~/kaonomi/fooocus
```

Its own routes are `GET /health`, `GET /options` and `POST /generate` (docs at `http://127.0.0.1:8888/docs`). One generation runs at a time; the others wait their turn. Only text to image: the image inputs of Fooocus (upscale, vary, inpaint, image prompts, enhance) are left off.

`POST /image/generate` body (every field but `prompt` is optional; empty fields take the defaults of Fooocus):

```json
{
  "prompt": "a small white robot reading a book in a cozy library, warm light",
  "negativePrompt": null,
  "styles": ["Fooocus V2", "Fooocus Enhance", "Fooocus Sharp"],
  "performance": "Speed",
  "aspectRatio": "1152*896",
  "imageNumber": 1,
  "seed": -1,
  "outputFormat": "png",
  "baseModel": "juggernautXL_v8Rundiffusion.safetensors",
  "guidanceScale": 4,
  "includeBase64": true
}
```

The answer (`ImageGenerationResult`) lists the `images` (`path` of the file, `base64` of its data unless `includeBase64` is false), the `seed` used (`-1` asks for a random one), the `model` and the `seconds` taken; `error` explains a failure. Each generation is logged. On an Apple M5 Max, one 1152 × 896 image takes about a minute in `Speed`, 20 s in `Extreme Speed`. `Extreme Speed`, `Lightning` and `Hyper-SD` need a LoRA that Fooocus downloads from Hugging Face the first time they are used (about 400 MB each).

### Message flow (ticker)

`MessageFlow` documents feed the scrolling ticker of the Chat client:

```json
{
  "text": "CAC 40 -1.2%",
  "color": "red",
  "priority": 0,
  "url": "https://example.com/news"
}
```

| Field | Meaning |
| --- | --- |
| `text` | Message shown in the ticker |
| `color` | `red` (falling, ▼), `green` (rising, ▲); any other value is shown in white |
| `priority` | `0` low, `1` medium, `2` high |
| `url` | Optional link opened when the message is clicked |

## Agents

The active dialog sequence wires inbound guardrail → processor → outbound guardrail (and related steps defined in `Program.cs`). Additional VAgents live in this project (preprocessing, entities, symbolic analysis, forecast, cache, emotions, context update, etc.) for use in sequences.

## Dependencies

- `KAONOMI.Core`
- ASP.NET Core, Swagger, M-A-R-I

## See also

- [Solution README](../README.md)
- [KAONOMI.Chat](../KAONOMI.Chat/README.md)
