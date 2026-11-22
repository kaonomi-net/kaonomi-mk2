[www.kaonomi.net](https://www.kaonomi.net)

# KAONOMI.graphbase

A small **graph database** served by a **GraphQL API**: nodes with labels (`Person`, `Organization`, `Place`…) and
properties (any JSON object), and typed edges between them (`KNOWS`, `WORKS_FOR`, `LOCATED_IN`…) with their own
properties. Made for what KAONOMI links: entities of the ontology, intels, people, places, and the relations between
them. It is one process, with no other server to install: the graph is kept in memory and written to a SQLite file.

## Run

```bash
dotnet run --project KAONOMI.graphbase
```

The API is at `http://localhost:4000/graphql` (plain HTTP: no HTTPS, no certificate). Opened in a browser, the same address shows the IDE (Nitro, served
from the package: nothing is loaded from the Internet): the schema, its documentation, autocompletion, a history.
Stop the server with Ctrl+C or `SIGTERM`. A second server on the same `path.data` stops at once (`graphbase.lock`), and
the port is checked before the graph is opened.

| Option | |
| --- | --- |
| `--config file.yml` | Another configuration (by default `config/graphbase.yml`) |
| `--port 4001`, `--data folder` | `http.port`, `path.data` |
| `--http` | Accepted and ignored (plain HTTP is the only mode) |
| `--set key=value` | Any key of the configuration |

### Plain HTTP

There is no HTTPS any more (an `https.*` key left in a configuration is ignored). By hand:

```bash
curl -H 'Content-Type: application/json' -d '{"query":"{ stats { nodes edges } }"}' http://localhost:4000/graphql
```

With `network.host: 0.0.0.0` the API is reached from the network in clear: keep `localhost` unless the network is trusted.

## Configuration: `config/graphbase.yml`

Keys nested or dotted; `--set key=value` overrides one. Relative paths are relative to the folder of the file.

| Key | Default | Role |
| --- | --- | --- |
| `path.data` | `~/kaonomi/graphbase` | The folder of the graph (`graph.db`, SQLite) |
| `path.logs` | (console only) | The folder of `graphbase.log` |
| `network.host`, `http.port` | `localhost`, `4000` | Where the server listens (`0.0.0.0`: every interface) |
| `graphql.path` | `/graphql` | The path of the API, of the IDE and of the subscriptions (WebSocket) |
| `graphql.ide` | `true` | The IDE at `GET /graphql` |
| `graphql.max_depth` | `20` | Deepest selection accepted (a node, its neighbours, theirs…) |
| `graphql.max_results` | `10000` | Most items of a list: a larger `first` is cut to it |
| `graphql.max_hops` | `10` | Deepest search of `shortestPath` and `subgraph` |
| `security.enabled`, `security.users` | `false` | Basic authentication of every request (`username`, `password`) |
| `log.requests` | `false` | Logs every request (method, path, status, time) |

`GET /` answers what the server is: `{"name": "KAONOMI.graphbase", "version": …, "nodes": …, "edges": …}`.

## The graph

- A **node**: an `id` (given, or made: 32 hexadecimal characters), its `labels` (none, one or more; case counts),
  its `properties` (a JSON object: strings, numbers, booleans, arrays, objects), `created`, `updated`.
- An **edge**: an `id`, its `type`, the node it starts from (`from`) and the one it goes to (`to`), its `properties`,
  `created`, `updated`. Its ends never change. An edge can go from a node to itself; two nodes can have several edges
  of the same type.
- A property set to `null` is removed: a node never holds a null property.

## The API

The whole schema, with the documentation of every field, is in the IDE. In short:

### Queries

| Field | |
| --- | --- |
| `node(id)`, `edge(id)` | One node, one edge (null when there is none) |
| `nodes(ids, label, where, search, orderBy, descending, first, skip)` | Nodes: by ids, label, conditions, text; ordered, paged (`first` 100 by default) |
| `nodeCount(label, where, search)` | How many (all of them, not a page) |
| `edges(ids, type, from, to, where, orderBy, descending, first, skip)`, `edgeCount(…)` | The same for the edges |
| `shortestPath(from, to, direction, types, maxDepth)` | The path with the fewest edges, or null |
| `subgraph(ids, depth, direction, types, limit)` | The nodes within `depth` edges of others, and the edges between them: what to draw around nodes |
| `labels`, `edgeTypes` | The labels and the types of edge, with how many use them |
| `stats` | Version, start, numbers of nodes and edges, labels, types |

On a node: `property(name)`, `edges(direction, type, first)`, `neighbors(direction, type, label, first)`,
`degree(direction, type)`. On an edge: `from`, `to` (the nodes), `fromId`, `toId`, `property(name)`. `direction` is
`OUT`, `IN` or `BOTH` (by default).

**`where`**: a list of conditions, all of which must hold, each on a property (its path: `name`, `address.city`,
`aliases.0`): `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `in`, `contains`, `startsWith`, `matches` (a regular expression;
these three ignore case), `exists`. A property that is an array holds when one of its values does (`eq: "fr"` holds
for `["en", "fr"]`); a missing property holds only `ne` and `exists: false`. Numbers compare as numbers (`1` = `1.0`),
strings in ordinal order (ISO dates sort as dates).

**`search`**: a text found, case ignored, in the id, a label or any string of the properties.

**`orderBy`**: `id`, `created`, `updated`, the path of a property (the items without it come last) or, for the
nodes, `degree` (their number of edges: `orderBy: "degree", descending: true` gives the hubs of the graph); by default
by creation, or in the order of `ids`.

### Mutations

| Field | |
| --- | --- |
| `createNode(id, labels, properties)` | A new node (`ALREADY_EXISTS` when the id is taken) |
| `updateNode(id, properties, replaceProperties, removeProperties, labels, addLabels, removeLabels)` | Properties merged (or replaced), labels changed; a node already as asked is not written again |
| `mergeNode(label, key, value, labels, properties)` | The node of `label` whose property `key` is `value`, completed; made when there is none (as `MERGE` of Cypher) |
| `deleteNode(id, detach)` | Deletes a node and its edges (`detach: false`: refused while it has edges) |
| `deleteNodes(ids, label, where, search)` | Deletes every node found and their edges (one criterion at least: never the whole graph) |
| `createEdge(type, from, to, id, properties)`, `mergeEdge(type, from, to, properties)` | A new edge; the edge of that type between these nodes, completed or made |
| `updateEdge(id, properties, replaceProperties, removeProperties)`, `deleteEdge(id)`, `deleteEdges(ids)` | |
| `importGraph(nodes, edges, mergeEdges, replace)` | Many nodes, then many edges, **all or nothing**: what exists is completed, the rest made; the edges can go to the nodes of the same import. `replace: true` (the import is the source of what it gives): what exists becomes what is given, its labels and properties those given only, an edge of other ends or type made again |

### Subscriptions

`graphChanged` sends every change as it is written (`kind`: `NODE_CREATED`, `NODE_UPDATED`, `NODE_DELETED`,
`EDGE_CREATED`, `EDGE_UPDATED`, `EDGE_DELETED`; the `node` or the `edge`, its last state when deleted; `at`), over
WebSocket (`graphql-transport-ws`, `graphql-ws`) or Server-Sent Events (a POST with `Accept: text/event-stream`).

### Errors

A request the graph refuses answers a GraphQL error with its message and a code in `extensions.code`: `NOT_FOUND`,
`ALREADY_EXISTS`, `INVALID_ARGUMENT`, `NODE_HAS_EDGES`; with security on, a request without a valid user gets `401`.

## Examples

```graphql
mutation {
  ada: createNode(id: "ada", labels: ["Person"], properties: {name: "Ada Lovelace", born: 1815, tags: ["math"]}) { id }
  charles: createNode(id: "charles", labels: ["Person"], properties: {name: "Charles Babbage"}) { id }
  london: mergeNode(label: "Place", key: "name", value: "London", properties: {country: "UK"}) { id }
  knows: createEdge(type: "KNOWS", from: "ada", to: "charles", properties: {since: 1833}) { id }
}
```

```graphql
{
  nodes(label: "Person", where: [{property: "born", lt: 1850}, {property: "tags", eq: "math"}], orderBy: "born") {
    id
    properties
    neighbors(type: "KNOWS") { id property(name: "name") }
  }
  shortestPath(from: "ada", to: "charles") { length nodes { id } edges { type } }
}
```

An import from a script, the nodes and edges in variables:

```graphql
mutation Load($nodes: [NodeInput!], $edges: [EdgeInput!]) {
  importGraph(nodes: $nodes, edges: $edges) { nodesCreated nodesUpdated edgesCreated edgesUpdated unchanged }
}
```

```json
{
  "nodes": [{"id": "paris", "labels": ["Place"], "properties": {"name": "Paris", "country": "FR"}}],
  "edges": [{"type": "VISITED", "from": "ada", "to": "paris", "properties": {"year": 1835}}]
}
```

In a GraphQL literal the keys of a JSON object are GraphQL names (`{name: "Paris"}`); a key with a space or a dash goes
through the variables (plain JSON).

## In the chat: `/graphbase`

`/graphbase` in KAONOMI.ChatGodot shows the graph in 3D (the nodes as spheres in the colour of their kind, the edges
as arrows), with a search (a text, a label, a depth) and a GraphQL extension: what you want in your words written as
a query by a model (✨ TO GRAPHQL: the schema of `GET /graphql?sdl`, the labels and types of edges are given to it),
any query or mutation, its answer, and the nodes and edges it names drawn (see `KAONOMI.ChatGodot/README.md`).
`/process` starts and stops the server.

## The architecture of `/archi`

The chat writes the repository of `/archi` (`~/kaonomi/archi/workspace.json`) into the graph while the graphbase
runs: when the window opens, 2 s after each change, and at once with its **⇄ GRAPHBASE** button
(`KAONOMI.ChatGodot/Shared/Services/ArchiGraph.cs`).

| In `/archi` | In the graph |
| --- | --- |
| A fact sheet | A node `archi:<id>`, labels `Archi` and its kind (`Application`, `AIModel`, `Agent`, `MultiAgentSystem`, `McpServer`…); properties `name`, `fullName` (with its parents), `type`, `layer`, `description`, `owner`, `tags`, `phase` (today), `lifecycle`, `criticality`, `functionalFit`, `technicalFit`, `aiRisk`, `completeness`, `updated`, `archiId`, and the fields of its kind (`provider`, `hosting`, `autonomy`, `url`…) |
| A relation | An edge `archi:<id>`, its kind as the type (`supports` → `SUPPORTS`, `runs on` → `RUNS_ON`, `delegates to` → `DELEGATES_TO`); properties `kind`, `description`, `archiId` |
| A parent | An edge `CHILD_OF` from the fact sheet to its parent (`archi:parent:<id>`) |

Every node and edge of `/archi` has `source: "archi"`. Each writing makes the graph what the repository is: the fact
sheets and relations removed from `/archi` are removed, the others written as they are (`importGraph` with
`replace`); the nodes and edges that do not come from `/archi` are not touched, an edge from them to a fact sheet too.

```graphql
{
  # The agents, their models and what they call
  nodes(label: "Agent", orderBy: "name") {
    name: property(name: "name")
    aiRisk: property(name: "aiRisk")
    models: neighbors(direction: OUT, type: "USES", label: "AIModel") { name: property(name: "name") }
    calls: neighbors(direction: OUT, type: "CALLS") { name: property(name: "name") }
  }
  # The AI fact sheets of high risk
  risky: nodes(label: "Archi", where: [{property: "aiRisk", eq: "High"}]) { id properties }
}
```

## The ontology and the relations

The chat writes the ontology and the relations of KAONOMI.Server into the graph
(`KAONOMI.ChatGodot/Shared/Services/KnowledgeGraph.cs`, `GraphbaseSync.cs`): all of them when `/ontology` or
`/relation` opens (once every 10 minutes at most) and with ⇄ SYNC of `/graphbase`; an entity created or changed, a
relation saved or deleted in the chat, at once.

| On the server | In the graph |
| --- | --- |
| An entity of the ontology | A node `onto:<id>`, labels `Ontology`, its entity type (`PERSON`, `ORGANISATION`, `DOMAIN`…) and `Class` for a class; properties `name`, `entityType`, `summary`, `description`, `symbol`, `synonyms`, `tags`, `links`, `startDate`, `endDate`, `confidentiality`, `emotions`, `ontologyId`, `updated` |
| Its parent (`nodeparentId`) | An edge `CHILD_OF` from the entity to its parent (`onto:parent:<id>`) |
| Its related entities (`relatedEntities`, by name) | An edge `RELATED_TO` to each entity of that name or synonym, case ignored (three at most when the name is shared), `onto:related:<id>:<other>`; a name found nowhere gives no edge |
| A relation of `/relation` (its last version) | An edge from its source to each destination, `rel:<id>`, its nature as the type (`est membre de` → `EST_MEMBRE_DE`, the accents taken away); properties `nature`, `title`, `description`, `probability`, `dateFrom`, `dateTo`, `version`, `fromInformation`, `toInformation`, `relationId`, `date`, `userName` |
| An end of a relation that is no entity | A node `RelationEnd` (`relend:<window>:<object>`) |

Their nodes and edges have `source: "ontology"` or `source: "relation"`; a whole writing makes the graph what the server
holds (removed on the server: removed from the graph), the other nodes and edges are not touched. When the server
gives 10 000 entities or relations (the most it is asked), nothing is removed: the list may be cut.

```graphql
{
  # An entity, its parent, its related entities; then relations of /relation
  node(id: "onto:ia-shannon") {
    name: property(name: "name")
    parent: neighbors(direction: OUT, type: "CHILD_OF") { name: property(name: "name") }
    related: neighbors(direction: OUT, type: "RELATED_TO") { name: property(name: "name") }
  }
  edges(type: "EST_MEMBRE_DE", first: 5) {
    from { name: property(name: "name") }
    to { name: property(name: "name") }
    probability: property(name: "probability")
  }
}
```

## Guarantees

- **Atomic writes**: a mutation is checked, written to SQLite in one transaction, and only then applied in memory;
  when it fails nothing of it is kept (an import of a thousand nodes too).
- **Durable**: SQLite in WAL mode (`synchronous = NORMAL`): a crash of the process loses nothing written; a power cut
  can lose the last writes, never the file.
- **Consistent reads**: the writes go one at a time, the reads together; a node or an edge read never changes under
  the request that reads it.
- **Bounded requests**: depth (`graphql.max_depth`), list sizes (`graphql.max_results`), searches of paths
  (`graphql.max_hops`), regular expressions (250 ms at most per value).

The whole graph is in memory (around a kilobyte per node or edge with small properties): fine for hundreds of
thousands of items on a workstation, not for hundreds of millions. There is no query language (Cypher, Gremlin) and no index on the properties: a
`where` reads the nodes of the label (or all of them).

## Tests

```bash
dotnet test KAONOMI.graphbase.Tests
```

The graph (writes, restart, import all or nothing, conditions, paths, concurrent writes) and the API (a server started
in the process of the tests).
