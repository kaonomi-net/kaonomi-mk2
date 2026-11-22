<p align="center">
  <a href="https://www.kaonomi.net">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="docs/images/kaonomi-logo-dark.png" />
      <img src="docs/images/kaonomi-logo.png" alt="Kaonomi.net, 100 % non-profit AGI's exploration, distributed AI and meta AI" width="500" />
    </picture>
  </a>
</p>

<p align="center"><a href="https://www.kaonomi.net">www.kaonomi.net</a></p>

# KAONOMI

> [!WARNING]
> **This project is a prototype.** Its purpose is to explore many different aspects of a personal AI: agents,
> models, memory, voice, interfaces, tools, security… It is a playground for experiments, not a finished product.
> At the end of the experimentation, development will start again **from scratch**, keeping what was learned here,
> not the code.

A conversational .NET platform: a REST API orchestrates a M-A-R-I agent sequence that talks to [Ollama](https://ollama.com), desktop and console clients send prompts, and dialogs, logs, performance data, and token usage are stored in Elasticsearch. Background agent hosts (Dreamer, Vision, Reflex, Metacognition, EarlyWarningSystem, ACHEngine) run scheduled sequences for context work. An optional LLM gateway in front of Ollama adds a semantic cache and routes each prompt to a model by its entities, and the immune system scans the machine for AI-generated images, code hidden in Office files and the AI services of the browser histories.

Documentation: [https://www.kaonomi.net](https://www.kaonomi.net)

## What Kaonomi is

Kaonomi is a personal AI project. It runs locally, on your own machines, but it still connects to external services
when it needs them. It will be available in several editions:

| Edition | For |
| --- | --- |
| **Kaonomi Family** | An AI for the whole family |
| **Kaonomi Family Dys/ASD** | Families with dyslexic (or other "dys") children and/or autistic members |
| **Kaonomi AI** | AI enthusiasts who dream of building their own personal JARVIS |
| **SyDE AI** | IT teams: a solution for architecture, AI, cybersecurity and knowledge bases |

Kaonomi speaks and listens through a 3D avatar whose mouth follows its voice. Each preset gives it a face, a voice and
a system prompt (`/preset` in the chat):

<p align="center">
  <img src="KAONOMI.ChatGodot/Assets/kaonomi-face.png" alt="Kaonomi" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/kaonami-face.png" alt="Kaonami" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/kaonomi-robot-face.png" alt="Kaonomi robot" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/rivet-face.png" alt="Rivet" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/shiro-face.png" alt="Shiro" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/kingyo-face.png" alt="Kingyo" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/hikari-face.png" alt="Hikari" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/kamen-face.png" alt="Kamen" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/aoi-face.png" alt="Aoi" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/hagane-face.png" alt="Hagane" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/sora-face.png" alt="Sora" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/tama-face.png" alt="Tama" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/beni-face.png" alt="Beni" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/circuit-face.png" alt="Circuit" width="110" />
  <img src="KAONOMI.ChatGodot/Assets/green-face.png" alt="Green" width="110" />
</p>

## Architecture

Everything runs on your machine; the dotted lines are the optional parts and the connections going outside.

```mermaid
flowchart LR
    user(["You"])

    subgraph clients["Clients"]
        chat["KAONOMI.ChatGodot<br/>chat, voice, tool windows"]
        admin["KAONOMI.Admin<br/>console REPL"]
    end

    subgraph core["Core"]
        server["KAONOMI.Server<br/>REST API http :49165<br/>M-A-R-I dialog sequence"]
        hosts["6 agent hosts<br/>Dreamer · Vision · Reflex<br/>Metacognition · EarlyWarningSystem · ACHEngine"]
    end

    subgraph data["Storage"]
        search[("KAONOMI.SearchEngine<br/>or Elasticsearch :9200<br/>dialogs, logs, tokens, documents")]
        mongo[("KAONOMI.MongoEngine<br/>or MongoDB :27017<br/>MARI cluster membership")]
        graphdb[("KAONOMI.graphbase<br/>GraphQL http :4000<br/>entities and relations")]
    end

    subgraph llm["Models"]
        gateway["KAONOMI.LLMGateway :11435<br/>semantic cache, model router"]
        ollama["Ollama :11434<br/>gpt-oss, mistral-small…"]
    end

    immune["KAONOMI.ImmuneSystem<br/>one-shot scan, JSON report"]
    outside(["Web, Fooocus,<br/>Whisper, Piper"])

    user --> chat
    user --> admin
    chat -- "HTTP" --> server
    admin --> ollama
    server <-- "MARI / Orleans cluster" --> hosts
    server --> search
    hosts --> search
    server --> mongo
    hosts --> mongo
    server --> gateway
    hosts --> gateway
    gateway --> ollama
    chat -. "/gateway" .-> gateway
    chat -. "/immune" .-> immune
    chat -. "scraper, images, speech" .-> outside
```

Each prompt sent to `POST /aiassistant` is saved, then goes through the 13 virtual agents of the dialog sequence `jfo`
(`KAONOMI.Server/Program.cs`), one after the other:

```mermaid
flowchart TB
    subgraph s1["1 · understand"]
        direction LR
        in(["prompt"]) --> g1["Inbound<br/>guardrail"] --> pre["Pre-<br/>processing"] --> cache["Cache"] --> ent["Entities"]
    end
    subgraph s2["2 · decide and answer"]
        direction LR
        sym["Symbolic<br/>analysis"] --> fval["Forecasting<br/>validator"] --> sel["Model<br/>selection"] --> emo["Emotion<br/>engine"] --> proc["Processor<br/>(the LLM)"]
    end
    subgraph s3["3 · refine and remember"]
        direction LR
        post["Post-<br/>processing"] --> fc["Forecasting"] --> ctx["Context<br/>update"] --> g2["Outbound<br/>guardrail"] --> out(["completion"])
    end
    s1 --> s2 --> s3
```

## Prerequisites

Every prerequisite and how to install it: [PREREQUISITES.md](PREREQUISITES.md) (English), [PREREQUIS.md](PREREQUIS.md) (français). The installers hold all of them but LM Studio, with the applications, the models and the data folder: [installer/macos](installer/macos/README.md) for a Mac with Apple Silicon, [installer/windows](installer/windows/README.md) for Windows 11 (both built on a Mac). In short:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Ollama](https://ollama.com) running locally (`http://localhost:11434`) with the configured models (`gpt-oss:20b`, `mistral-small3.2` for the vision and the AI tools of the text areas)
- Elasticsearch 7.14 running locally (`http://localhost:9200`), or [`KAONOMI.SearchEngine`](KAONOMI.SearchEngine/README.md) in its place (its data in `~/kaonomi/data`)
- MongoDB 8 running locally (`mongodb://localhost:27017`, the Orleans cluster membership of MARI), or [`KAONOMI.MongoEngine`](KAONOMI.MongoEngine/README.md) in its place
- For the chat: Godot 4.7.2 .NET; optional: Fooocus (images), Whisper (speech to text), Python and Piper (text to speech)
- The MARI packages (`MARI.*` 1.0.0) come from nuget.org, like every other package: `nuget.config` has no other source
- Optional, for the NER of the server (`POST /ner`): the XLM-RoBERTa int8 model in `~/kaonomi/models/ner/xlm-roberta-base-ner-hrl-int8`, made once by `KAONOMI.Server/Ner/export/ner_export.py` (see [`KAONOMI.Server/README.md`](KAONOMI.Server/README.md)); without it, the rules and the ontology still answer
- Optional, for the semantic cache of the LLM gateway: `ollama pull nomic-embed-text` (without it the gateway uses a local embedding of the words)

## Solution

| Project | Role |
| --- | --- |
| [`KAONOMI.Core`](KAONOMI.Core/README.md) | Shared models (incl. `MessageFlow`), Ollama helpers, Elasticsearch access (`NEST`), session manager |
| [`KAONOMI.Server`](KAONOMI.Server/README.md) | ASP.NET Core API, M-A-R-I dialog sequence, Swagger, message-flow, cache & symbolic-pattern APIs, NER (`POST /ner`: XLM-RoBERTa int8, rules, ontology) |
| [`KAONOMI.Admin`](KAONOMI.Admin/README.md) | Interactive console REPL for Ollama |
| [`KAONOMI.Dreamer`](KAONOMI.Dreamer/README.md) | Background dreamer / context-update agent host (every 15 min, 22:00–06:00) |
| [`KAONOMI.Vision`](KAONOMI.Vision/README.md) | Background vision analysis agent host (every second) |
| [`KAONOMI.EarlyWarningSystem`](KAONOMI.EarlyWarningSystem/README.md) | Background early warning system agent host (every second; cloned from Vision, agents to write) |
| [`KAONOMI.ACHEngine`](KAONOMI.ACHEngine/README.md) | Background ACH engine agent host (every second; cloned from Vision, agents to write) |
| [`KAONOMI.Reflex`](KAONOMI.Reflex/README.md) | Background reflex agent host (every 100 ms) |
| [`KAONOMI.Agent.WebScraper.Analyze`](KAONOMI.Agent.WebScraper.Analyze/README.md) | One-shot agent host of the web scraper: analyses the pages collected with the model of the advanced conversations (persons, organisations and events put in the ontology, relations as the data of a graph, emotions, writing, pictures) into the index `pageanalysis`, one document per page |
| [`KAONOMI.Agent.WebScraper.Collect`](KAONOMI.Agent.WebScraper.Collect/README.md) | One-shot agent host of the web scraper: collects the pages of the collection plans (RUN of `/collectionplan`) into the index `page`, with their entities, URLs, pictures and rendering |
| [`KAONOMI.Metacognition`](KAONOMI.Metacognition/README.md) | Background metacognition agent host (every second) |
| [`KAONOMI.SearchEngine`](KAONOMI.SearchEngine/README.md) | Small Elasticsearch 7.14 compatible engine (one node, Lucene.Net): what KAONOMI uses of Elasticsearch, YAML configuration |
| `KAONOMI.SearchEngine.Tests` | Tests of the engine through the repository of KAONOMI (NEST), compared with a real Elasticsearch when one is running |
| [`KAONOMI.MongoEngine`](KAONOMI.MongoEngine/README.md) | Small MongoDB compatible server (wire protocol, standalone) for MARI and the Orleans cluster membership, YAML configuration |
| `KAONOMI.MongoEngine.Tests` | Tests of the MongoDB engine with the official driver and real Orleans silos |
| [`KAONOMI.graphbase`](KAONOMI.graphbase/README.md) | Graph database with a GraphQL API (`http://localhost:4000/graphql`, HotChocolate): nodes with labels and JSON properties, typed edges, filters, shortest path, neighbourhood, atomic import, live subscriptions; in memory, kept in SQLite (`~/kaonomi/graphbase`), the IDE in the browser |
| `KAONOMI.graphbase.Tests` | Tests of the graph database: the store (writes, restart, import, conditions, paths, concurrent writes) and the GraphQL API |
| [`KAONOMI.Media`](KAONOMI.Media/README.md) | `kaonomi-media` (Swift, AVFoundation): the videos of `/video` — probe, thumbnails, the strip of the timeline, the sheet of a segment, the sound of a segment, the stream of the player |
| [`KAONOMI.LLMGateway`](KAONOMI.LLMGateway/README.md) | Gateway in front of Ollama (`http://localhost:11435`): Ollama and OpenAI APIs, semantic cache, router of the models by the entities of the prompt, export / import of the cached phrases, admin API `/gateway/…` |
| `KAONOMI.LLMGateway.Tests` | Tests of the gateway: cache, entity extractor, router, end to end |
| [`KAONOMI.ImmuneSystem`](KAONOMI.ImmuneSystem/README.md) | Console scanner (self-contained, read only, no network): AI-generated or AI-edited images (C2PA, IPTC, generator parameters), code in Office files (VBA, Excel 4.0 macros, DDE, OLE, Power Query…), browser histories (AI services visited, files downloaded); one JSON report |
| [`KAONOMI.VisionOpenCV`](KAONOMI.VisionOpenCV/README.md) | The vision applications in Python with OpenCV, shown in windows of the chat: `/vision:move`, the motion detection (moving zones, contours, gravity centre), and `/vision`, the face and facial expression recognition with the object detection of YOLOv8 on the same frames |
| [`KAONOMI.ChatGodot`](KAONOMI.ChatGodot/README.md) | The Kaonomi chat (Godot 4.7 .NET, EVE Online look): the chat, its voice mode, its desktop of tool windows, the journal of the activities, `/think`, the games (`/game:solitaire`, `/game:chess`, `/game:go`), `/fractal`, `/immune`, `/gateway` |
| [`KAONOMI.Chat`](KAONOMI.Chat/README.md) | The same chat in Avalonia, aligned on the Godot chat: the same view models, services, commands and tool windows (linked from `KAONOMI.ChatGodot/Shared`), with the full chat on a vertical screen when one is connected |

Solution file: `KAONOMI.slnx`.

## Getting started

The start order:

```mermaid
flowchart LR
    build["dotnet build"] --> engines["MongoEngine<br/>SearchEngine"] --> hosts["the 6 agent hosts"] --> server["Server"] --> chat["ChatGodot"]
    engines -. "optional" .-> gateway["LLMGateway"] -.-> server
```

```bash
dotnet build KAONOMI.slnx
dotnet run --project KAONOMI.MongoEngine      # or MongoDB
dotnet run --project KAONOMI.SearchEngine     # or Elasticsearch
cd KAONOMI.Dreamer && dotnet run              # the 6 agent hosts, before the server, each from its own folder:
cd KAONOMI.Vision && dotnet run               #   Dreamer, Vision, Reflex, Metacognition,
cd KAONOMI.Reflex && dotnet run               #   EarlyWarningSystem, ACHEngine
cd KAONOMI.Metacognition && dotnet run
cd KAONOMI.EarlyWarningSystem && dotnet run
cd KAONOMI.ACHEngine && dotnet run
cd KAONOMI.Server && dotnet run
cd KAONOMI.LLMGateway && dotnet run           # optional: the gateway, http://localhost:11435 → Ollama
dotnet run --project KAONOMI.graphbase        # optional: the graph database, http://localhost:4000/graphql
cd KAONOMI.ChatGodot && godot-mono --path .   # the chat (the first time: godot-mono --headless --path . --import)
dotnet run --project KAONOMI.Chat             # or the chat in Avalonia (no Godot needed)
```

The server listens in plain HTTP on `http://localhost:49165`, on this machine only (no HTTPS, no certificate). The root path redirects to Swagger.
Start the 6 agent hosts before (or with) the server: the dialog sequence needs their agents, and the server refuses to
start while hosts stopped a few minutes ago are still registered as alive in the cluster. From the chat, `/process`
starts, stops and restarts all of them (START CORE, STOP ALL), and `/killall` stops everything at once.

Each executable reads its configuration (`config.json`, and for Chat also `config.yml`) from the current folder: start
the agent hosts and the server from their project folder, as above (from the root of the repository they stop at once
on `Could not find file '…\config.json'`).

To send the prompts of the server and of the agents through the gateway, set `"ollamaUrl": "http://localhost:11435"`
in their `config.json`; the chat finds the gateway with `llmGatewayUrl` in its `config.yml`.

The immune system is a one-shot scan, not a service:

```bash
dotnet run --project KAONOMI.ImmuneSystem                 # the home folder; --machine for the whole machine
dotnet run --project KAONOMI.ImmuneSystem -- ~/Downloads --out report.json
```

When the repository sits on a drive also written by a Mac, macOS leaves `._*` files (AppleDouble metadata) next to the
sources, and the C# compiler stops on them (`CS2015: '…\._File.cs' is a binary file`): delete them before building.

The console applications (Server, Admin, Dreamer, Vision, Reflex, Metacognition, EarlyWarningSystem, ACHEngine) get their look from `consoleTheme` in their config.json (`HelperConsoleTheme` in Core):

| `consoleTheme` | Look |
| --- | --- |
| `matrix` | Black screen and phosphor green text, digital rain intro ("Wake up..."), KAONOMI banner, timestamped status lines |
| `jarvis` | Iron Man / J.A.R.V.I.S. HUD: navy screen, arc reactor blue text with gold accents, arc reactor power-up animation, system diagnostics with progress bars, greeting, `JARVIS ▸` prompt |
| `ubuntu` | Ubuntu terminal: aubergine screen, Ubuntu orange accents, boot splash with the loading dots, systemd `[  OK  ]` boot messages, message-of-the-day banner, green/blue `user@host:~/path$` prompt, `HH:mm:ss [ INFO ]` status lines |
| anything else | Classic console (plain text, default colors) |

Themed consoles are 30 % transparent. Press any key to skip the intro, or set the environment variable `KAONOMI_NO_INTRO` to turn it off; it is also skipped when the output is redirected.

On a PC with several screens, the background hosts open on the smallest screen (by surface), each in one cell of a 3x2 grid (3 columns, 2 rows): Dreamer, Metacognition and Reflex on the top row, Vision, EarlyWarningSystem and ACHEngine on the bottom row.

## What's new in the chat

Every tool is described in [`KAONOMI.ChatGodot/README.md`](KAONOMI.ChatGodot/README.md); the latest ones:

| Command | What |
| --- | --- |
| `/archi` | The architecture repository, after SAP LeanIX (without its security nor its access rights): fact sheets of enterprise architecture (capabilities, processes, applications, interfaces, data, IT components, providers, projects…) and of AI (models, agents, multi-agent systems, MCP servers, tools, REST APIs, prompts, datasets, vector stores, RAG, evaluations, guardrails), with their lifecycle, criticality, fit, EU AI Act risk and relations; inventory, capability map, matrix, roadmap, graph and AI landscape with its warnings; saved in `~/kaonomi/archi` (history kept), JSON and CSV export |
| `/piano` | A copy of the Launchkey Mini 37 MK4: 37 keys played by clicking (or with the keys of the computer, or the real MIDI keyboard), a screen, eight knobs (volume, tone, release, vibrato, echo, tempo…), sixteen drum pads, PITCH and MOD strips, an arpeggiator, a take recorded and played in a loop; piano, electric piano, organ or harpsichord |
| `/synth` | An FM synth of six operators after the Yamaha DX7, played by the piano keyboard or a MIDI keyboard: the 32 algorithms drawn, the feedback, every operator (ratio or fixed frequency, level, velocity, envelope R1–R4 L1–L4, level scaling), the LFO, the pitch envelope; sounds given (E.PIANO, BELL, BRASS, BASS, ORGAN, STRINGS…) and saved |
| `/sampler` | The piano keyboard playing a sound recorded with the microphone (or a sound file): a small editor keeps a part of it (dragged on the waveform, auto-trim), sets its gain (normalize), previews it; once validated the keys play it, higher or lower than its root note, one-shot or held |
| `/paint` | A drawing program: pencil, brush, spray, eraser, line, rectangle, ellipse, fill, colour picker, text, selection (moved, copied, pasted, cropped); two colours and a palette, undo/redo, zoom, flip, rotate, resize; PNG/JPG/WEBP opened and saved |
| `/looper` | A looper: loops recorded over each other from what `/piano` plays (keys, pads, MIDI keyboard); the tempo (or TAP), the beats of a bar, the bars of a loop, the quantize, the metronome and a counted bar; eight tracks with their sound (an instrument of the piano, the drum kit or one sound of the pads: kick, clap…) and an audio file (WAV, or MP3/M4A… through ffmpeg), volume, mute, undo and clear; export in WAV or MP3 |
| `/obs` | The notes, after Obsidian: a vault of Markdown notes (an Obsidian vault too), `[[wiki links]]`, backlinks, `#tags`, search, outline, daily notes and the graph of the links; saved by itself, deleted notes go to the `.trash` of the vault |
| `/todo` | The tasks of the day (title, text, to do or done), put off to tomorrow when they are not done; no task is removed by hand: at the first opening of the day, in either chat, those of the days before are marked deleted (but those put off to that day) and a popup asks for the tasks of the day |
| `/support` | A request for support: its products chosen among the usual ones and among the fact sheets of `/archi` (one or several); the ticket is kept in `~/kaonomi/support` and becomes a task of the kanban in the project Support. `/kanban` shows one project at a time (its Project field), the new tasks going into it |
| `/game:chess` | Chess against the LLM (you play White): every rule (castling, en passant, promotion, fifty moves, threefold repetition…), the model held to its legal moves by the structured output of Ollama, its comment for each move; UNDO, RESIGN, PGN |
| `/game:go` | Go against the LLM (you play Black) on 9×9, 13×13 or 19×19: Chinese rules, positional superko, dead stones marked at the end, COUNT with komi 7.5; UNDO, RESIGN, SGF |
| `/fractal` | Fractal generator on the GPU: escape time (Mandelbrot, Julia, Burning Ship, Newton…, or your own formula of z and c), L-systems, IFS; palettes, zoom box, Julia of a point, SAVE as JSON, PNG |
| `/immune` | Runs `KAONOMI.ImmuneSystem` from the chat: the scan animated as an immune cell, live counters and findings, then the report (overview charts, AI images, Office code, browsing) and the reports kept |
| `/orleans` | The Orleans dashboard of the server (`http://localhost:8090/`) in a web window of the desktop |
| `/rest` | The Swagger page of the REST API of the server (`http://localhost:49165/swagger/index.html`) in a web window of the desktop |
| `/doc`, `/documentation` | The documentation of Kaonomi (`https://www.kaonomi.net/search/label/help`) in a web window of the desktop |
| `/emotion` | What if emotion: for each emotion the server finds in the prompts (happy, sad, angry, fearful, disgust, surprised, neutral), a text added to the system prompt of the answers while that emotion leads; each change is a new version (never erased) |
| `/prompts` | The prompts the tools send to the models: defaults in `config.yml`, versions in the search engine (never erased); edit, analyse with the model, save and load as JSON |
| `/vision:move` | Motion detection with Python and OpenCV (`KAONOMI.VisionOpenCV`) in a window of the desktop: moving zones, contours, foreground mask, gravity centre and its trail; view, sliders, pause, picture |
| `/vision` | **The faces and the objects in one window** (title "Vision · faces, objects & voice"), on the same frames of one camera, one process (`vision_face.py --objects` runs the YOLO of `vision_yolo.py`; DETECT on the line OBJECTS switches the objects off and on): a line FACES and a line OBJECTS of settings, the boxes of YOLO drawn under the faces, the line under the picture telling both, RECORD (faces) and RECORD OBJECTS (once a minute) written apart. **Who you are, told to the model**: while the window runs, the person enrolled it recognises in front of the camera (the nearest face recognised, still them 30 seconds after the face is lost) is named at the end of the system prompt sent with each question, « L'utilisateur est <name>. » (the line under the picture says `the user: <name>`, the log each time the name changes); nobody recognised, nothing is added. **VOICE**, under the picture: the spectrum and the identification of the voice of `/recorder`, on the microphone (opened with the window, closed with it; nothing is recorded): who speaks (LIVE about once a second, the smallest similarity, 👥 VOICES, 📁, the names enrolled; the voices are the ones of `/recorder`, enrolled there), the VU meter, the spectrum with the envelope of the voice, the pitch F0, the formants F1 to F4 and the vowel they make. **An unknown face is enrolled with its voice**: the popup of a face marked `unknown` says the person must speak for 5 seconds; OK shows SPEAK and a bar emptying from 5 to 0 s, then the voice heard is enrolled under the name (as in `/recorder`) and the face with it; no voice heard (silence, too little speech), neither is enrolled and the popup says why; CANCEL enrols neither. A face already recognised is enrolled again without its voice. **📍 SET LOCATION**: the name of a place is asked, then you sit in front of the camera in your usual working position; OK shows SPEAK and a bar emptying from 5 to 0 s while you speak; the mean area of the box of your face (a share of the picture) and the level of your voice are kept under that name, in the search engine (index `worklocation`, see KAONOMI.Server) and in `~/kaonomi/vision/locations.json`, then a picture of the camera (nothing drawn on it) is taken, kept in `~/kaonomi/vision/locations/<id>.jpg`, described by the vision model (`modes.image`: the place, the objects, the screens, the texts, the light…) and the entities of that description found by the NER: the description (`analysis`), the model, the entities and the picture are added to the place a moment after; and the place is chosen in the list beside the button (kept in `~/kaonomi/vision/location.txt`; "no place" turns it off). With a place chosen, when your face is at least as big and the voice of the last second at least as loud as measured, the picture is framed in red and the line under it starts with SPEAKING TO THE ASSISTANT (in red); the line also gives the face and the voice now against the measure. **The 3D animation follows you**: with a place chosen, when your face is at least as big as measured there (the voice aside), the 3D animation turns its head towards your face as you move (sideways and up and down, the same way as your face in the picture of the camera; JARVIS, which has no head, leans your way), and in the chat it also comes closer (its camera nearer by 30 %, JARVIS grows), not in the voice mode (the animation in full screen). Away from the computer (where « dis <name> » is needed), it turns towards what moves in the room: **MOVEMENT** (on the line FACES, on by default) draws in magenta the rectangle around what moves in the picture (the motion detection of `/vision:move`, MOG2, just its rectangle). Nothing moving, or while it walks on the desk, it looks ahead again. The faces: Face recognition and facial expression recognition with Python and OpenCV (`KAONOMI.VisionOpenCV`, open source models of the OpenCV Zoo, everything local) in a window of the desktop: a click on a face names it (enrolled under that name, then it is recognised) or forgets its name; 7 expressions **The objects**: Object detection with Python, YOLOv8 and OpenCV (`KAONOMI.VisionOpenCV`): boxes, centre and direction of each person, alerts (two persons overlap, an object of a class is seen), nano or medium model, JSON log |
| `/alarm` | Alarm clock: several alarms, once on a day or weekly on the days ticked (every day, Mon–Fri, weekend; one week in 1 to 4), 4 ringtones made by the code, snooze, the label said by the avatar; they ring even with the window closed (STOP / SNOOZE) |
| `/iq` | IQ tests drawn at random from a bank of 1000 questions in JSON (`KAONOMI.ChatGodot/Data/iq-questions.json`): number series, matrices of shapes (in the manner of Raven's), words (FR / EN), memory of digits, 10 questions each harder and harder or a full test of 24, 60 s a question; a **training** mode where the time stops at each answer and the right one is shown and explained; an indicative score (not a measured IQ) and the review of every answer |
| `/calendar` | Calendar: the month with each day's events and alarms, events once or repeated (daily, weekly, monthly, yearly), all day or with a time, colours, notes, and reminders rung like the alarms, the title said by the avatar |
| `/gateway` | Drives `KAONOMI.LLMGateway`: cache and routing settings, the rules, the last requests (model asked → chosen, HIT / MISS), the cached answers and phrases (export, import, search), ANALYZE |
| `/entity:<name>` | What the pages collected say of an entity day by day (the name found despite accents, case or a typing mistake): per day the emotions about it, the persons, places and events linked to it, a summary, the tone of each source and the writing (sentences in the conditional, adverbs, counted), read by the model of the advanced conversations; then the whole period |
| `/dissimination` | Dataviz of what the web scraper collected and analysed, in a window that fills the desktop: a collection plan, a period, searches of entities with `AND`, `OR`, `NOT` and parentheses, the results per day and per search as bars, the websites, entities, emotions and relations; each paragraph with its pictures in a carousel, the capture of its page and its whole section |
| `/collectionplan` 🕷 RUN | Starts the agent `KAONOMI.Agent.WebScraper.Collect` on a collection plan: it collects the pages (fixed, explore, random; robots.txt followed) and saves each one in the search engine (index `page`: titles and paragraphs, entities of the NER, URLs), with its rendering and pictures in `Pictures/kaonomi/scraper/<plan>/<website>/<yyyyMMdd>`; a main page is named by its title and the day |
| Voice mode | A command line above the microphone (prompts and `/tools` typed there), the windows shown one at a time at 90 % of the screen (◀ ▶ between them, ⏬ puts them in a row at the bottom), the desktop back as it was afterwards |

`/ngame:chess`, `/ngame:go` and `/nimmune` open the same tool in its own window.

## Main API

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/aiassistant` | Saves the dialog, runs the M-A-R-I sequence, returns the completion |
| `POST` | `/ner` | The entities of a text: persons, organizations, places, dates (XLM-RoBERTa int8 in ONNX), URLs, e-mails, amounts, code… (rules) and the names of the ontology with their id; `GET /ner/types` lists the types |
| `GET` | `/version` | Current version string |
| `GET` / `POST` / `PUT` | `/messageflow…` | Ticker message CRUD and search (shown in the Chat ticker band) |
| `GET` / `POST` / `PUT` | `/cache…` | Cache document CRUD and search |
| `GET` / `POST` / `PUT` | `/symbolicpattern…` | Symbolic pattern CRUD and search |
| `GET` | `/tokensusage/last?minutes=N` | Token usages (model, prompt and completion tokens per LLM call) of the last N minutes, newest first (default 60) |
| `GET` | `/log/since?minutes=N` | All the logs of the last N minutes (every application), newest first, without the 10,000 limit (default 60) |
| `GET` | `/log/last?size=N` | The N most recent logs of every application, newest first (default and maximum 10,000) |
| `DELETE` | `/log` | Erases ALL the logs (the index of the logs, created again at the next log) |
| `GET` / `POST` / `PUT` / `DELETE` | `/intel…`, `/collectionplan…` | Intels and collection plans of the web scraper: CRUD, search, last (a delete only marks them deleted) |
| `PUT` / `POST` | `/gamescore`, `/gamescore/best` | The scores of `/game:solitaire` (game `game1`), and the best ones of a game |
| `PUT` / `POST` | `/useractivity`, `/useractivity/last`, `/useractivity/search` | The journal of the activities of the chat (index `useractivities`) |
| `GET` | `/` | Redirects to Swagger |

See [`KAONOMI.Server/README.md`](KAONOMI.Server/README.md) for full route details.

The gateway (`http://localhost:11435`) speaks the API of Ollama (`/api/chat`, `/api/generate`, the others passed
through) and of OpenAI (`/v1/chat/completions`), and is driven by `/gateway/status`, `/gateway/settings`,
`/gateway/rules`, `/gateway/cache…` (with `/gateway/cache/export` and `/gateway/cache/import`), `/gateway/log` and
`/gateway/analyze`: see [`KAONOMI.LLMGateway/README.md`](KAONOMI.LLMGateway/README.md). Each answer says what was done in
its `X-Gateway-Model`, `X-Gateway-Rule`, `X-Gateway-Cache` and `X-Gateway-Score` headers.

The path of a prompt through the gateway:

```mermaid
flowchart TD
    req(["request: /api/chat, /api/generate,<br/>/v1/chat/completions"]) --> ent["entities of the last prompt<br/>CODE, MATH, URL, LANGUAGE, PERSON…"]
    ent --> route{"a rule matches?<br/>by priority, model installed"}
    route -- "yes" --> model["the model of the rule"]
    route -- "no" --> def["the model asked,<br/>or defaultModel for auto"]
    model --> look{"close prompt in the cache?<br/>same model, context, entities"}
    def --> look
    look -- "HIT: minAnswers kept" --> hit(["one of the kept answers,<br/>never the last one given"])
    look -- "MISS" --> ollama["Ollama"] --> keep["answer kept in the cache"] --> resp(["answer"])
```

## License

MIT. See `LICENSE.txt`.

## Screenshots

The windows of the chat (`KAONOMI.ChatGodot`, light theme). Each tool opens in the desktop between the bar of commands
on the left and the chat on the right; here every one is shown full size.

### Knowledge and writing

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/ontology.jpg" alt="/ontology" /><br /><sub><b><code>/ontology</code></b>: the ontology tree, its entity types and nodes</sub></td>
    <td width="50%"><img src="docs/screenshots/relation.jpg" alt="/relation" /><br /><sub><b><code>/relation</code></b>: the graph of the relations between the entities</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/journal.jpg" alt="/journal" /><br /><sub><b><code>/journal</code></b>: the journal, its ribbon and its feelings</sub></td>
    <td><img src="docs/screenshots/idea.jpg" alt="/idea" /><br /><sub><b><code>/idea</code></b>: the ideas, the latest ones and their editor</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/project.jpg" alt="/kanban" /><br /><sub><b><code>/kanban</code></b>: the project board, Todo to Archived</sub></td>
    <td><img src="docs/screenshots/completion.jpg" alt="/completion" /><br /><sub><b><code>/completion</code></b>: the completions, their filters by model, language and topic</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/wiki.jpg" alt="/wiki" /><br /><sub><b><code>/wiki</code></b>: the wiki of Kaonomi and its links between pages</sub></td>
    <td><img src="docs/screenshots/search.jpg" alt="/search" /><br /><sub><b><code>/search</code></b>: one search in every index of Kaonomi</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/collectionplan.jpg" alt="/collectionplan" /><br /><sub><b><code>/collectionplan</code></b>: the collection plans of the web scraper</sub></td>
    <td></td>
  </tr>
</table>

### Diagrams

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/bpmnai.jpg" alt="/bpmnai" /><br /><sub><b><code>/bpmnai</code></b>: the editor of AI processes (BPMN 2.0, agents, LLM, RAG, MCP…)</sub></td>
    <td width="50%"><img src="docs/screenshots/infra.jpg" alt="/infra" /><br /><sub><b><code>/infra</code></b>: the editor of infrastructure diagrams</sub></td>
  </tr>
</table>

### Games and fractals

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/game-chess.jpg" alt="/game:chess" /><br /><sub><b><code>/game:chess</code></b>: chess against the LLM</sub></td>
    <td width="50%"><img src="docs/screenshots/game-go.jpg" alt="/game:go" /><br /><sub><b><code>/game:go</code></b>: go against the LLM, 9×9 to 19×19</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/game-solitaire.jpg" alt="/game:solitaire" /><br /><sub><b><code>/game:solitaire</code></b>: the morpion solitaire and its scores</sub></td>
    <td><img src="docs/screenshots/fractal.jpg" alt="/fractal" /><br /><sub><b><code>/fractal</code></b>: the fractal generator on the GPU</sub></td>
  </tr>
</table>

### Models, avatar and monitoring

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/gateway.jpg" alt="/gateway" /><br /><sub><b><code>/gateway</code></b>: the LLM gateway, its semantic cache and its router</sub></td>
    <td width="50%"><img src="docs/screenshots/preset.jpg" alt="/preset" /><br /><sub><b><code>/preset</code></b>: the presets of the avatar: voice, 3D animation, system prompt</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/process.jpg" alt="/process" /><br /><sub><b><code>/process</code></b>: the processes of Kaonomi, START / STOP / RESTART</sub></td>
    <td><img src="docs/screenshots/system.jpg" alt="/system" /><br /><sub><b><code>/system</code></b>: CPU, memory, GPU, VRAM, network and drives</sub></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/log.jpg" alt="/log" /><br /><sub><b><code>/log</code></b>: the logs of every application</sub></td>
    <td><img src="docs/screenshots/token.jpg" alt="/token" /><br /><sub><b><code>/token</code></b>: the tokens used, by model and by application</sub></td>
  </tr>
</table>
