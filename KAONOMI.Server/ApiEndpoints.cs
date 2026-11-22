namespace KAONOMI.Server
{
    using KAONOMI.Core;
    using KAONOMI.Core.Data;
    using KAONOMI.Core.Models;
    using KAONOMI.Core.Models.LLM;

    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Extension methods that register message flow, cache, symbolic pattern, and utility HTTP routes.
    /// </summary>
    internal static class ApiEndpoints
    {
        /// <summary>
        /// Maps version, message-flow, cache, and symbolic-pattern CRUD/search endpoints onto the application.
        /// </summary>
        /// <param title="app">The endpoint route builder.</param>
        /// <param title="session">Session manager used for persistence operations.</param>
        /// <returns>The same <paramref title="app"/> instance for chaining.</returns>
        public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app, SessionManager session)
        {
            // POST /ner {text, types?}: the entities of the text (XLM-RoBERTa int8: PERSON, ORG, LOC; the rules: URL, EMAIL,
            // DATE, MONEY, CODE…; the names of the ontology, with their id), each with its type, position, score and source.
            app.MapPost("/ner", (Ner.NerRequest p) => Task.FromResult(Ner.NerService.Get(session.Config).Extract(p.text, p.types)));

            // GET /ner/types: the types the NER finds (of the model, of the rules, of the ontology) and the state of the model.
            app.MapGet("/ner/types", () => Task.FromResult(Ner.NerService.Get(session.Config).Types()));

            // GET /detectedentity/top?minutes=60&size=60: the entities found by the NER in the dialogs of the last minutes
            // (all of them when minutes is 0), counted, the most frequent first.
            app.MapGet("/detectedentity/top", (int? minutes, int? size) =>
                Task.FromResult(session.DetectedEntitiesTop(minutes ?? 60, size ?? 60)));

            // GET /detectedemotion/last?minutes=0&size=50: the emotions the emotion engine found in the prompts (every 3
            // prompts of a conversation, on these 3 prompts), newest first; of the last minutes, or all of them when minutes is 0.
            app.MapGet("/detectedemotion/last", (int? minutes, int? size) =>
                Task.FromResult(session.DetectedEmotionsLast(minutes ?? 0, size ?? 50)));

            // GET /detectedemotion/session/{sessionId}: the emotions found in one conversation, the oldest first.
            app.MapGet("/detectedemotion/session/{sessionId}", (string sessionId) =>
                Task.FromResult(session.DetectedEmotionsOfSession(sessionId)));

            // GET /detectedsymbol/top?minutes=60&size=60: the symbols found by the symbolic patterns in the dialogs of the
            // last minutes (all of them when minutes is 0), counted by symbol and signals, the most frequent first.
            app.MapGet("/detectedsymbol/top", (int? minutes, int? size) =>
                Task.FromResult(session.DetectedSymbolsTop(minutes ?? 60, size ?? 60)));

            // GET /detectedtag/top?minutes=60&size=60: the tags found through the detections (patterns, entities of the
            // ontology, tags written) in the dialogs of the last minutes, counted, the most frequent first.
            app.MapGet("/detectedtag/top", (int? minutes, int? size) =>
                Task.FromResult(session.DetectedTagsTop(minutes ?? 60, size ?? 60)));

            // GET /version: returns the server version string.
            app.MapGet("/version", () =>
            {
                string res = "version 0.01.01";
                return Task.FromResult(res);
            });

            // GET /: redirects the root URL to the Swagger UI.
            app.MapGet("/", (request) =>
            {
                request.Response.Redirect("swagger");
                return Task.CompletedTask;
            });


            // GET /messageflow/{id}: loads one ticker message by its document id.
            app.MapGet("/messageflow/{id}", (string id) =>
            {
                MessageFlow result = new MessageFlow();
                try
                {
                    result = session.MessageFlowGet(id);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // GET /tokensusage/last?minutes=N: the token usages (model, prompt and completion tokens per LLM call) of the last N minutes, newest first; N defaults to 60.
            app.MapGet("/tokensusage/last", (int? minutes) =>
            {
                try
                {
                    return Results.Ok(session.TokensUsageLast(minutes ?? 60));
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Token usages could not be read");
                }
            });

            // GET /log/since?minutes=N: all the logs (every application) of the last N minutes, newest first, without the 10,000 limit of /log/last; N defaults to 60.
            app.MapGet("/log/since", (int? minutes) =>
            {
                try
                {
                    return Results.Ok(session.LogsSince(minutes ?? 60));
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Logs could not be read");
                }
            });

            // GET /performance/since?minutes=N: every performance measurement (operation, start, end, duration in ms,
            // metadata; written by the agents) of the last N minutes, newest first; N defaults to 60.
            app.MapGet("/performance/since", (int? minutes) =>
            {
                try
                {
                    return Results.Ok(session.PerformancesSince(minutes ?? 60));
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Performances could not be read");
                }
            });

            // GET /performance/last?size=N: the N most recent performance measurements, newest first; N defaults to and is capped at 10,000.
            app.MapGet("/performance/last", (int? size) =>
            {
                try
                {
                    return Results.Ok(session.PerformancesLast(size ?? ElasticRepository.MaxResults));
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Performances could not be read");
                }
            });

            // GET /log/last?size=N: the N most recent logs (all applications), newest first; N defaults to and is capped at 10,000.
            app.MapGet("/log/last", (int? size) =>
            {
                try
                {
                    return Results.Ok(session.LogsLast(size ?? ElasticRepository.MaxResults));
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError, title: "Logs could not be read");
                }
            });

            // DELETE /log: erases ALL the logs (the index of the logs is deleted, created again empty at the next log);
            // returns the number erased in ReturnedId, or "-1" with the error message.
            app.MapDelete("/log", () =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.LogsDeleteAll();
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            //PUT
            app.MapPut("/log", (Log p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.LogSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            app.MapPut("/completion", (Completion p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.CompletionSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            app.MapPut("/systemlog", (SystemLog p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.SystemLogSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            app.MapPut("/whitenote", (WhiteNote p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.WhiteNoteSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // GET /whitenote/last?size=200: the last white notes, newest first: their text and their (blurred) date only,
            // never their context.
            app.MapGet("/whitenote/last", (int? size) =>
                Task.FromResult(session.WhiteNotesLast(size ?? 200).Select(n => new { n.text, n.date }).ToList()));


            // POST /messageflow/update: saves (creates or overwrites) a ticker message; returns the saved id, or "-1" with the error message.
            app.MapPost("/messageflow/update", (MessageFlow p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.MessageFlowSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // POST /messageflow/search: free-text search on the message "text" field (body: SearchCriterias, only "term" is used).
            app.MapPost("/messageflow/search", (SearchCriterias p) =>
            {
                List<MessageFlow> result = new List<MessageFlow>();
                try
                {
                    result = session.MessageFlowsSearch(p.term);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // POST /messageflow/last: returns up to "size" ticker messages matching "term" ("*" when empty). No sort is applied, so hits come in search-relevance order.
            app.MapPost("/messageflow/last", (SearchCriterias p) =>
            {
                if (p.term == null || p.term == "")
                {
                    p.term = "*";
                }
                if (p.size == null)
                {
                    p.size = 10;
                }
                List<MessageFlow> result = new List<MessageFlow>();
                try
                {
                    result = session.MessageFlowsSearch(p.term, p.size);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // PUT /messageflow: creates a ticker message (same save as /messageflow/update); returns the new id, or "-1" with the error message.
            app.MapPut("/messageflow", (MessageFlow p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.MessageFlowSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });


            // GET /cache/{id}: loads one cached dialog by its document id.
            app.MapGet("/cache/{id}", (string id) =>
            {
                Cache result = new Cache();
                try
                {
                    result = session.CacheGet(id);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // DELETE /cache/{id}, /dialog/{id}, /ontology/{id}: the document is marked deleted (isDeleted = true), never erased
            // (as every DELETE of the server: cache, completion, dialog, diary, document, idea, imageanalysis, intel, ontology,
            // relation, taskitem, youtubevideo stay in their index).
            app.MapDelete("/cache/{id}", (string id) => Task.FromResult(SoftDelete(() => session.CacheDelete(id))));
            app.MapDelete("/dialog/{id}", (string id) => Task.FromResult(SoftDelete(() => session.DialogDelete(id))));
            app.MapDelete("/ontology/{id}", (string id) => Task.FromResult(SoftDelete(() => session.OntologyDelete(id))));

            // POST /cache/update: saves (creates or overwrites) a cached dialog; returns the saved id, or "-1" with the error message.
            app.MapPost("/cache/update", (Cache p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.CacheSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // POST /cache/search: free-text search on the cached "prompt" field (body: SearchCriterias: "term", "size"
            // (10 when missing) and "fuzzy": with "fuzzy", a loose search (one word with a typo or as the start of a word,
            // accents ignored, is enough), which the clients ask for when the plain search found nothing).
            app.MapPost("/cache/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                List<Cache> result = new List<Cache>();
                try
                {
                    result = p.fuzzy
                        ? session.CachesSearchFuzzy(p.term ?? string.Empty, size)
                        : session.CachesSearch(p.term ?? string.Empty, size);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // POST /search: a loose search of "term" in several indices at once (body: MultiSearchCriterias: "term", "indices"
            // (cache, completion, dialog, diary, document, idea, imageanalysis, intel, ontology, relation, taskitem,
            // youtubevideo, mail…; all of them when empty), "size" per index): one word with a typo or as the start of a word is enough.
            app.MapPost("/search", (MultiSearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.SearchAll(p.term ?? string.Empty, p.indices, size));
            });

            // POST /recent: the documents of the last "minutes" minutes (60 by default) in "indices" (the same ones as /search;
            // all of them when empty), "size" per index, the newest first, each with its title and its text (/think of the chat).
            app.MapPost("/recent", (MultiSearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                var minutes = p.minutes > 0 ? p.minutes : 60;
                return Task.FromResult(session.RecentAll(minutes, p.indices, size));
            });

            // POST /cache/last: returns up to "size" cached dialogs matching "term" ("*" when empty). No sort is applied, so hits come in search-relevance order.
            app.MapPost("/cache/last", (SearchCriterias p) =>
            {
                if(p.term == null || p.term == "")
                {
                    p.term = "*";
                }
                if(p.size == null)
                {
                    p.size = 10;
                }
                List<Cache> result = new List<Cache>();
                try
                {
                    result = session.CachesSearch(p.term, p.size);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });



            // PUT /cache: creates a cached dialog (same save as /cache/update); returns the new id, or "-1" with the error message.
            app.MapPut("/cache", (Cache p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.CacheSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // GET /ontology/{id}: loads one ontology by its document id.
            app.MapGet("/ontology/{id}", (string id) =>
            {
                Ontology result = new Ontology();
                try
                {
                    result = session.OntologyGet(id);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // PUT /evaluation: saves (creates or overwrites) an evaluation; returns the saved id, or "-1" with the error message.
            app.MapPut("/evaluation", (Evaluation p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.EvaluationSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // POST /ontology/update: saves (creates or overwrites) an ontology; returns the saved id, or "-1" with the error message.
            app.MapPost("/ontology/update", (Ontology p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.OntologySave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // POST /ontology/search: free-text search on the ontology "prompt" field (body: SearchCriterias, only "term" is used).
            app.MapPost("/ontology/search", (SearchCriterias p) =>
            {
                List<Ontology> result = new List<Ontology>();
                try
                {
                    result = session.OntologiesSearch(p.term);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // POST /ontology/last: returns up to "size" ontologies matching "term" ("*" when empty). No sort is applied, so hits come in search-relevance order.
            app.MapPost("/ontology/last", (SearchCriterias p) =>
            {
                if (p.term == null || p.term == "")
                {
                    p.term = "*";
                }
                if (p.size == null)
                {
                    p.size = 10;
                }
                List<Ontology> result = new List<Ontology>();
                try
                {
                    result = session.OntologiesSearch(p.term, p.size);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // PUT /ontology: creates an ontology (same save as /ontology/update); returns the new id, or "-1" with the error message.
            app.MapPut("/ontology", (Ontology p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.OntologySave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });


            // GET /symbolicpattern/{id}: loads one symbolic pattern by its document id.
            app.MapGet("/symbolicpattern/{id}", (string id) =>
            {
                SymbolicPattern result = new SymbolicPattern();
                try
                {
                    result = session.SymbolicPatternGet(id);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // POST /symbolicpattern/update: saves (creates or overwrites) a symbolic pattern; returns the saved id, or "-1" with the error message.
            app.MapPost("/symbolicpattern/update", (SymbolicPattern p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.SymbolicPatternSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });

            // POST /symbolicpattern/search: free-text search on the "regexPatterns" field (body: SearchCriterias, only "term" is used).
            app.MapPost("/symbolicpattern/search", (SearchCriterias p) =>
            {
                List<SymbolicPattern> result = new List<SymbolicPattern>();
                try
                {
                    result = session.SymbolicPatternsSearch(p.term);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // POST /symbolicpattern/last: returns up to "size" symbolic patterns matching "term" ("*" when empty). No sort is applied, so hits come in search-relevance order.
            app.MapPost("/symbolicpattern/last", (SearchCriterias p) =>
            {
                if (p.term == null || p.term == "")
                {
                    p.term = "*";
                }
                if (p.size == null)
                {
                    p.size = 10;
                }
                List<SymbolicPattern> result = new List<SymbolicPattern>();
                try
                {
                    result = session.SymbolicPatternsSearch(p.term, p.size);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
                return Task.FromResult(result);
            });

            // PUT /symbolicpattern: creates a symbolic pattern (same save as /symbolicpattern/update); returns the new id, or "-1" with the error message.
            app.MapPut("/symbolicpattern", (SymbolicPattern p) =>
            {
                ServiceReturn result = new ServiceReturn();
                result.ReturnedId = "";
                result.Message = "";
                try
                {
                    result = session.SymbolicPatternSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                }
                return Task.FromResult(result);
            });


            // GET /idea/{id}: loads one idea by its document id (404 when it does not exist).
            // ── Clusters: the groups of elements drawn with the pen of the graph of relations (never erased: DELETE marks isDeleted) ──

            // GET /cluster/{id}: loads one cluster by its document id (404 when it does not exist).
            app.MapGet("/cluster/{id}", (string id) =>
            {
                Cluster result = session.ClusterGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /cluster: creates a cluster; returns the new id, or "-1" with the error message.
            app.MapPut("/cluster", (Cluster p) => { p.Normalize(); return Task.FromResult(SoftDelete(() => session.ClusterSave(p))); });

            // POST /cluster/update: saves (creates or overwrites) a cluster; returns the saved id, or "-1" with the error message.
            app.MapPost("/cluster/update", (Cluster p) => { p.Normalize(); return Task.FromResult(SoftDelete(() => session.ClusterSave(p))); });

            // POST /cluster/search: free-text search on title, description, tags and members (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/cluster/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.ClustersSearch(p.term ?? "*", size));
            });

            // POST /cluster/last: the "size" most recent clusters, newest first ("term" is ignored).
            app.MapPost("/cluster/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.ClustersLast(size));
            });

            // DELETE /cluster/{id}: marks the cluster deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/cluster/{id}", (string id) => Task.FromResult(SoftDelete(() => session.ClusterDelete(id))));

            // ── Presets: the voice, the effects, the 3D animation and the personality of the avatar of the chat (/preset; never erased) ──

            // GET /preset/{id}: loads one preset by its document id (404 when it does not exist).
            app.MapGet("/preset/{id}", (string id) =>
            {
                Preset result = session.PresetGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /preset: creates a preset (its title in upper case, its effects within their ranges); returns the new id, or "-1" with the error message.
            app.MapPut("/preset", (Preset p) => { p.Normalize(); return Task.FromResult(SoftDelete(() => session.PresetSave(p))); });

            // POST /preset/update: saves (creates or overwrites) a preset; returns the saved id, or "-1" with the error message.
            app.MapPost("/preset/update", (Preset p) => { p.Normalize(); return Task.FromResult(SoftDelete(() => session.PresetSave(p))); });

            // POST /preset/search: free-text search on title, description, voice, animation, persona and tags (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/preset/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.PresetsSearch(p.term ?? "*", size));
            });

            // POST /preset/last: the "size" most recent presets, newest first ("term" is ignored).
            app.MapPost("/preset/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.PresetsLast(size));
            });

            // DELETE /preset/{id}: marks the preset deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/preset/{id}", (string id) => Task.FromResult(SoftDelete(() => session.PresetDelete(id))));

            // GET /dialogcontext/{sessionId}: the context the server keeps for a conversation (its exchanges, the older the
            // more compressed, and its entities); 404 when it has none.
            app.MapGet("/dialogcontext/{sessionId}", (string sessionId) =>
            {
                var result = session.DialogContextGet(sessionId);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // ── Projects: a name, a description, goals and tags (/project in the chat; never erased) ──

            // GET /project/{id}: loads one project by its document id (404 when it does not exist).
            app.MapGet("/project/{id}", (string id) =>
            {
                Project result = session.ProjectGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /project (create) and POST /project/update (create or overwrite): save a project; return its id, or
            // "-1" with the error message (a project needs a name). Its tags, when it has none, are the declared tags
            // found in its name, its description and its goals.
            app.MapPut("/project", (Project p) => Task.FromResult(SoftDelete(() => session.ProjectSave(WithTags(session, p)))));
            app.MapPost("/project/update", (Project p) => Task.FromResult(SoftDelete(() => session.ProjectSave(WithTags(session, p)))));

            // POST /project/search: free-text search on name, description, goals, tags and comment (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/project/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.ProjectsSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /project/last: the "size" most recent projects, newest first ("term" is ignored).
            app.MapPost("/project/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.ProjectsLast(size));
            });

            // DELETE /project/{id}: marks the project deleted (isDeleted = true).
            app.MapDelete("/project/{id}", (string id) => Task.FromResult(SoftDelete(() => session.ProjectDelete(id))));

            // ── Face episodes: what /vision saw of a face while its state stayed the same (who, the share of each emotion, the area of the face and its movement: min, max, mean, standard deviation); written again every second while it lasts ──

            // GET /faceepisode/{id}: loads one by its document id (404 when it does not exist).
            app.MapGet("/faceepisode/{id}", (string id) =>
            {
                FaceEpisode result = session.FaceEpisodeGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /faceepisode and POST /faceepisode/update: save (create or overwrite); return the id, or "-1" with the error message.
            app.MapPut("/faceepisode", (FaceEpisode p) => Task.FromResult(SoftDelete(() => session.FaceEpisodeSave(p))));
            app.MapPost("/faceepisode/update", (FaceEpisode p) => Task.FromResult(SoftDelete(() => session.FaceEpisodeSave(p))));

            // POST /faceepisode/search: free-text search on the name, the emotion, the activity, the session (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/faceepisode/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.FaceEpisodesSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /faceepisode/last: the "size" most recent ones, newest first ("term" is ignored).
            app.MapPost("/faceepisode/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.FaceEpisodesLast(size));
            });

            // DELETE /faceepisode/{id}: erases it for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/faceepisode/{id}", (string id) => Task.FromResult(SoftDelete(() => session.FaceEpisodeDelete(id))));

            // ── Face observations: one second of a face, written by /vision at the instant something changed (appeared, emotion, identity, activity, left) ──

            // GET /faceobservation/{id}: loads one by its document id (404 when it does not exist).
            app.MapGet("/faceobservation/{id}", (string id) =>
            {
                FaceObservation result = session.FaceObservationGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /faceobservation and POST /faceobservation/update: save (create or overwrite); return the id, or "-1" with the error message.
            app.MapPut("/faceobservation", (FaceObservation p) => Task.FromResult(SoftDelete(() => session.FaceObservationSave(p))));
            app.MapPost("/faceobservation/update", (FaceObservation p) => Task.FromResult(SoftDelete(() => session.FaceObservationSave(p))));

            // POST /faceobservation/search: free-text search on the name, the emotion, the activity, the session (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/faceobservation/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.FaceObservationsSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /faceobservation/last: the "size" most recent ones, newest first ("term" is ignored).
            app.MapPost("/faceobservation/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.FaceObservationsLast(size));
            });

            // DELETE /faceobservation/{id}: erases it for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/faceobservation/{id}", (string id) => Task.FromResult(SoftDelete(() => session.FaceObservationDelete(id))));

            // ── Vision objects: everything /vision of the chat detected during one minute (each kind of object: how
            // many at once, how often, how sure; the persons; the alerts), one document per minute ──

            // GET /visionobjects/{id}: loads one by its document id (404 when it does not exist).
            app.MapGet("/visionobjects/{id}", (string id) =>
            {
                VisionObjects result = session.VisionObjectsGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /visionobjects and POST /visionobjects/update: save (create or overwrite); return the id, or "-1" with the error message.
            app.MapPut("/visionobjects", (VisionObjects p) => Task.FromResult(SoftDelete(() => session.VisionObjectsSave(p))));
            app.MapPost("/visionobjects/update", (VisionObjects p) => Task.FromResult(SoftDelete(() => session.VisionObjectsSave(p))));

            // POST /visionobjects/search: free-text search on the names of the objects, the model, the session (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/visionobjects/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.VisionObjectsSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /visionobjects/last: the "size" most recent ones, newest first ("term" is ignored).
            app.MapPost("/visionobjects/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.VisionObjectsLast(size));
            });

            // DELETE /visionobjects/{id}: erases it for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/visionobjects/{id}", (string id) => Task.FromResult(SoftDelete(() => session.VisionObjectsDelete(id))));

            // ── Work locations: SET LOCATION of /vision in the chat (the size of the face and the level of the voice
            // of the user in the usual working position), one document per name of place ──

            // GET /worklocation/{id}: loads one by its document id (404 when it does not exist).
            app.MapGet("/worklocation/{id}", (string id) =>
            {
                WorkLocation result = session.WorkLocationGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /worklocation and POST /worklocation/update: save (create or overwrite); return the id, or "-1" with the error message.
            app.MapPut("/worklocation", (WorkLocation p) => Task.FromResult(SoftDelete(() => session.WorkLocationSave(p))));
            app.MapPost("/worklocation/update", (WorkLocation p) => Task.FromResult(SoftDelete(() => session.WorkLocationSave(p))));

            // POST /worklocation/search: free-text search on the name, the camera, the microphone, the analysis of its picture and its entities (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/worklocation/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.WorkLocationSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /worklocation/last: the "size" most recent ones, newest first ("term" is ignored).
            app.MapPost("/worklocation/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.WorkLocationLast(size));
            });

            // DELETE /worklocation/{id}: erases it for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/worklocation/{id}", (string id) => Task.FromResult(SoftDelete(() => session.WorkLocationDelete(id))));

            // ── Prompt templates: the versions of the prompts the chat sends to the models, edited in /prompts. One
            // document per version; a version is written once: no route writes one over or erases one ──

            // GET /prompttemplate/{id}: loads one version by its document id, prompt-<key>-v<version> (404 when it does not exist).
            app.MapGet("/prompttemplate/{id}", (string id) =>
            {
                PromptTemplate result = session.PromptTemplateGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /prompttemplate: writes a new version; "-1" with the reason when that version exists (never written over).
            app.MapPut("/prompttemplate", (PromptTemplate p) => Task.FromResult(SoftDelete(() => session.PromptTemplateSave(p))));

            // POST /prompttemplate/search: free-text search on the key, the title, the text, the comment (body: SearchCriterias).
            app.MapPost("/prompttemplate/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.PromptTemplatesSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /prompttemplate/last: the "size" most recent versions, newest first ("term" is ignored).
            app.MapPost("/prompttemplate/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.PromptTemplatesLast(size));
            });

            // ── The versions of the documents: every state of a document the user edits (an idea, a task, a page of the
            // wiki…), kept at each save in the index documentversion; any of them can be restored ──

            // GET /documentversion/{index}/{id}: the versions of a document, the newest first (without their documents).
            app.MapGet("/documentversion/{index}/{id}", (string index, string id) => Task.FromResult(session.DocumentVersions(index, id)));

            // GET /documentversion/{versionId}: one version with its document (its JSON); 404 when it does not exist.
            app.MapGet("/documentversion/{versionId}", (string versionId) =>
            {
                DocumentVersion result = session.DocumentVersionGet(versionId);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // POST /documentversion/last: the "size" newest versions of every document (without their documents).
            app.MapPost("/documentversion/last", (SearchCriterias p) => Task.FromResult(session.DocumentVersionsLast(p.size > 0 ? p.size : 50)));

            // POST /documentversion/restore: { "id": "<version>" } — the document written back as it was in that version (a new version).
            app.MapPost("/documentversion/restore", (DocumentVersion p) => Task.FromResult(session.DocumentVersionRestore(p.id)));

            // ── Video notes: the analyses (segment, frame, sound), pictures and annotations of /video in the chat. One
            // document per version; a version is written once: no route writes one over or erases one ──

            // GET /videonote/{id}: loads one version by its document id, videonote-<noteId>-v<version> (404 when it does not exist).
            app.MapGet("/videonote/{id}", (string id) =>
            {
                VideoNote result = session.VideoNoteGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // GET /videonote/video/{videoKey}: every version of every note of a video, the oldest first.
            app.MapGet("/videonote/video/{videoKey}", (string videoKey) => Task.FromResult(session.VideoNotesOfVideo(videoKey)));

            // PUT /videonote: writes a new version; "-1" with the reason when that version exists (never written over).
            app.MapPut("/videonote", (VideoNote p) => Task.FromResult(SoftDelete(() => session.VideoNoteSave(p))));

            // POST /videonote/search: free-text search on the name of the video, the title, the text, the transcript (body: SearchCriterias).
            app.MapPost("/videonote/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.VideoNotesSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /videonote/last: the "size" most recent versions, newest first ("term" is ignored).
            app.MapPost("/videonote/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.VideoNotesLast(size));
            });

            // ── Faces: the faces enrolled by /vision in the chat, one document per picture kept (the picture, the
            // name, what the recognition model says of it); really erased when the name is forgotten ──

            // GET /face/{id}: loads one face by its document id (404 when it does not exist).
            app.MapGet("/face/{id}", (string id) =>
            {
                Face result = session.FaceGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /face and POST /face/update: save (create or overwrite) a face; return its id, or "-1" with the error message.
            app.MapPut("/face", (Face p) => Task.FromResult(SoftDelete(() => session.FaceSave(p))));
            app.MapPost("/face/update", (Face p) => Task.FromResult(SoftDelete(() => session.FaceSave(p))));

            // POST /face/search: free-text search on the name (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/face/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.FacesSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /face/last: the "size" most recent faces, newest first ("term" is ignored).
            app.MapPost("/face/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.FacesLast(size));
            });

            // DELETE /face/{id}: erases the face for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/face/{id}", (string id) => Task.FromResult(SoftDelete(() => session.FaceDelete(id))));

            // ── Voices: the voices enrolled by /recorder in the chat (who speaks), one document per sample kept (the
            // name and the embedding of the speaker model, not the sound); really erased when the name is forgotten ──

            // GET /voice/{id}: loads one voice by its document id (404 when it does not exist).
            app.MapGet("/voice/{id}", (string id) =>
            {
                Voice result = session.VoiceGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /voice and POST /voice/update: save (create or overwrite) a voice; return its id, or "-1" with the error message.
            // (what is written or erased here is followed at once by the voices POST /voice/identify compares with)
            app.MapPut("/voice", (Voice p) => Task.FromResult(SoftDelete(() => VoiceSaved(session, p))));
            app.MapPost("/voice/update", (Voice p) => Task.FromResult(SoftDelete(() => VoiceSaved(session, p))));

            // POST /voice/identify {wav, threshold?}: whose voice the WAV file is ("wav": the file in base 64, PCM or float,
            // any rate; 1.5 s of speech at least). Answers the name (null when nobody enrolled is close enough: the
            // similarity under "threshold", 0.5 by default), the similarity, the closest name and the score of every name.
            // 400 with the reason when the WAV is not read, too short or silent.
            app.MapPost("/voice/identify", async (Voices.VoiceIdentifyRequest p) =>
            {
                try
                {
                    return Results.Ok(await Voices.VoiceService.Get(session).IdentifyAsync(p));
                }
                catch (Voices.VoiceRequestException ex)
                {
                    return Results.BadRequest(new Voices.VoiceIdentifyResult { success = false, message = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.Json(new Voices.VoiceIdentifyResult { success = false, message = "Error : " + ex.Message }, statusCode: 500);
                }
            });

            // POST /voice/enrol {name, wav, source?}: keeps the voice of the WAV file (base 64; where this person speaks
            // alone, 1.5 s at least) under the name: a document of the index voice, recognised from then on by
            // POST /voice/identify and by /recorder of the chat. Enrol several samples of a name for a steadier answer.
            app.MapPost("/voice/enrol", async (Voices.VoiceEnrolRequest p) =>
            {
                try
                {
                    return Results.Ok(await Voices.VoiceService.Get(session).EnrolAsync(p));
                }
                catch (Voices.VoiceRequestException ex)
                {
                    return Results.BadRequest(new Voices.VoiceEnrolResult { success = false, message = ex.Message });
                }
                catch (Exception ex)
                {
                    return Results.Json(new Voices.VoiceEnrolResult { success = false, message = "Error : " + ex.Message }, statusCode: 500);
                }
            });

            // POST /voice/search: free-text search on the name (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/voice/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.VoicesSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /voice/last: the "size" most recent voices, newest first ("term" is ignored).
            app.MapPost("/voice/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.VoicesLast(size));
            });

            // DELETE /voice/{id}: erases the voice for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/voice/{id}", (string id) => Task.FromResult(SoftDelete(() =>
            {
                var deleted = session.VoiceDelete(id);
                if (deleted.Success)
                    Voices.VoiceService.Get(session).Erased(id);
                return deleted;
            })));

            // ── Audio records: the recordings of /recorder in the chat, their metadata and everything done to them
            // (notes, annotated manipulations, named selections, samples saved with their filters; never erased) ──

            // GET /audiorecord/{id}: loads one audio record by its document id (404 when it does not exist).
            app.MapGet("/audiorecord/{id}", (string id) =>
            {
                AudioRecord result = session.AudioRecordGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /audiorecord and POST /audiorecord/update: save (create or overwrite) an audio record; return its id, or "-1" with the error message.
            app.MapPut("/audiorecord", (AudioRecord p) => Task.FromResult(SoftDelete(() => session.AudioRecordSave(p))));
            app.MapPost("/audiorecord/update", (AudioRecord p) => Task.FromResult(SoftDelete(() => session.AudioRecordSave(p))));

            // POST /audiorecord/search: free-text search (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/audiorecord/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.AudioRecordsSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /audiorecord/last: the "size" most recent audio records, newest first ("term" is ignored).
            app.MapPost("/audiorecord/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.AudioRecordsLast(size));
            });

            // DELETE /audiorecord/{id}: erases the audio record for good (the document leaves the index; "-1" when it does not exist).
            app.MapDelete("/audiorecord/{id}", (string id) => Task.FromResult(SoftDelete(() => session.AudioRecordDelete(id))));

            // ── Cache builders: prompts and their completions written by hand (/cache in the chat; never erased) ──

            // GET /cachebuilder/{id}: loads one cache builder by its document id (404 when it does not exist).
            app.MapGet("/cachebuilder/{id}", (string id) =>
            {
                CacheBuilder result = session.CacheBuilderGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /cachebuilder: creates a cache builder; returns the new id, or "-1" with the error message. Its related
            // entities and its tags, when it has none, are detected in its prompts and completions (see WithDetections).
            app.MapPut("/cachebuilder", (CacheBuilder p) => Task.FromResult(SoftDelete(() => session.CacheBuilderSave(WithDetections(session, p)))));

            // POST /cachebuilder/update: saves (creates or overwrites) a cache builder; returns the saved id, or "-1" with the error message.
            app.MapPost("/cachebuilder/update", (CacheBuilder p) => Task.FromResult(SoftDelete(() => session.CacheBuilderSave(WithDetections(session, p)))));

            // POST /cachebuilder/search: free-text search on title, prompts, completions, model, language, comment and tags (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/cachebuilder/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.CacheBuildersSearch(string.IsNullOrWhiteSpace(p.term) ? "*" : p.term, size));
            });

            // POST /cachebuilder/last: the "size" most recent cache builders, newest first ("term" is ignored).
            app.MapPost("/cachebuilder/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.CacheBuildersLast(size));
            });

            // POST /cachebuilder/{id}/publish: writes every prompt-completion pair of the cache builder in the index "cache"
            // (one document each; a pair already in the cache is written over, never added twice; the pairs published before and gone are marked deleted).
            app.MapPost("/cachebuilder/{id}/publish", (string id) => Task.FromResult(SoftDelete(() => session.CacheBuilderPublish(id))));

            // DELETE /cachebuilder/{id}: marks the cache builder deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/cachebuilder/{id}", (string id) => Task.FromResult(SoftDelete(() => session.CacheBuilderDelete(id))));

            // ── Wiki: the pages of the wiki of Kaonomi (/wiki in the chat), one document each, its id the slug of its title; never erased ──

            // GET /wiki/{id}: loads a page by its slug (404 when it does not exist or is deleted).
            app.MapGet("/wiki/{id}", (string id) =>
            {
                Wiki result = session.WikiGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // GET /wiki/{id}/backlinks: the pages that link to it ([text](wiki:id) in their content), by title.
            app.MapGet("/wiki/{id}/backlinks", (string id) => Task.FromResult(session.WikiBacklinks(id)));

            // PUT /wiki: creates a page (its id the slug of its title, or of the id given); "-1" when a page of that slug exists.
            app.MapPut("/wiki", (Wiki p) => Task.FromResult(SoftDelete(() => session.WikiSave(p, create: true))));

            // POST /wiki/update: saves a new version of a page (created when it does not exist); returns its id.
            app.MapPost("/wiki/update", (Wiki p) => Task.FromResult(SoftDelete(() => session.WikiSave(p, create: false))));

            // POST /wiki/search: free-text search on title, content, summary, tags and author (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/wiki/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.WikisSearch(p.term ?? "*", size));
            });

            // POST /wiki/last: the "size" pages changed last, newest first ("term" is ignored).
            app.MapPost("/wiki/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.WikisLast(size));
            });

            // DELETE /wiki/{id}: marks the page deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/wiki/{id}", (string id) => Task.FromResult(SoftDelete(() => session.WikiDelete(id))));

            // ── CVs (/cv in the chat): the person, the summary, the educations, the experiences, the skills, the creations; never erased ──

            // GET /cv/{id}: loads a CV by its id (404 when it does not exist or is deleted).
            app.MapGet("/cv/{id}", (string id) =>
            {
                Cv result = session.CvGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /cv and POST /cv/update: save a CV (created, or a new version of it); return its id, or "-1" with the error message.
            app.MapPut("/cv", (Cv p) => Task.FromResult(SoftDelete(() => session.CvSave(p))));
            app.MapPost("/cv/update", (Cv p) => Task.FromResult(SoftDelete(() => session.CvSave(p))));

            // POST /cv/search: free-text search on the person, the summary, the skills, the experiences, the educations and the creations (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/cv/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.CvsSearch(p.term ?? "*", size));
            });

            // POST /cv/last: the "size" CVs changed last, newest first ("term" is ignored).
            app.MapPost("/cv/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.CvsLast(size));
            });

            // DELETE /cv/{id}: marks the CV deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/cv/{id}", (string id) => Task.FromResult(SoftDelete(() => session.CvDelete(id))));

            // ── The tools of the chat whose store is the search engine, a copy on the machine of the chat: /calendar, /archi,
            //    /todo, /support, /obs, /entity: (kinds calendarevent, archiitem, daytodo, supportticket, vaultnote, entityanalysis) ──

            // PUT /synced/{kind}: saves an item (created, written again, or marked deleted with "isDeleted": true); its id
            // and its "revision" are given by the machine. Returns its id, or "-1" with the error message.
            app.MapPut("/synced/{kind}", (string kind, SyncedRecord p) =>
                SessionManager.IsSyncedKind(kind)
                    ? Results.Ok(SoftDelete(() => session.SyncedSave(kind, p)))
                    : Results.NotFound($"Unknown kind {kind}"));

            // POST /synced/{kind}/all: every item of the kind, the ones marked deleted included, newest first (body:
            // SearchCriterias, "size": at most 10000; "term" is ignored).
            app.MapPost("/synced/{kind}/all", (string kind, SearchCriterias p) =>
                SessionManager.IsSyncedKind(kind)
                    ? Results.Ok(session.SyncedAll(kind, p.size > 0 ? Math.Min(p.size, 10000) : 10000))
                    : Results.NotFound($"Unknown kind {kind}"));

            // ── The internal mail of the chat (/message): its messages, for the searches; the files stay in the chat, which writes them here ──

            // GET /mail/{id}: loads a message by its id (404 when it does not exist or is deleted).
            app.MapGet("/mail/{id}", (string id) =>
            {
                Mail result = session.MailGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /mail and POST /mail/update: save a message (its id the one of its file in the chat; written again when
            // it is there); an encrypted message keeps its envelope only. Return its id, or "-1" with the error message.
            app.MapPut("/mail", (Mail p) => Task.FromResult(SoftDelete(() => session.MailSave(p))));
            app.MapPost("/mail/update", (Mail p) => Task.FromResult(SoftDelete(() => session.MailSave(p))));

            // POST /mail/search: free-text search on the subject, the text, the sender, the receivers and the entities (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/mail/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.MailsSearch(p.term ?? "*", size));
            });

            // POST /mail/last: the "size" latest messages, newest first ("term" is ignored).
            app.MapPost("/mail/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.MailsLast(size));
            });

            // DELETE /mail/{id}: marks a message deleted (every user removed it in the chat); returns its id, or "-1" with the error message.
            app.MapDelete("/mail/{id}", (string id) => Task.FromResult(SoftDelete(() => session.MailDelete(id))));

            // ── Interviews (/interview in the chat): the interview (the person, the notes, the recordings) and its transcription, one document per paragraph; never erased ──

            // GET /interview/{id}: loads an interview by its id (404 when it does not exist or is deleted).
            app.MapGet("/interview/{id}", (string id) =>
            {
                Interview result = session.InterviewGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // GET /interview/{id}/paragraphs: the paragraphs of its transcription, by recording then by rank.
            app.MapGet("/interview/{id}/paragraphs", (string id) => Task.FromResult(session.InterviewParagraphs(id)));

            // PUT /interview and POST /interview/update: save an interview (created, or a new version of it); return its id, or "-1" with the error message.
            // Each save runs the NER (the one of POST /ner) on its notes and gathers the entities of its transcription: "entities" of the interview.
            app.MapPut("/interview", (Interview p) => Task.FromResult(SoftDelete(() => session.InterviewSave(WithEntities(session, p)))));
            app.MapPost("/interview/update", (Interview p) => Task.FromResult(SoftDelete(() => session.InterviewSave(WithEntities(session, p)))));

            // POST /interview/search: free-text search on the person, the notes and the tags (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/interview/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.InterviewsSearch(p.term ?? "*", size));
            });

            // POST /interview/last: the "size" interviews changed last, newest first ("term" is ignored).
            app.MapPost("/interview/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.InterviewsLast(size));
            });

            // PUT /interviewparagraph: saves one paragraph of a transcription (the live transcription), its id made of its interview, recording and rank.
            // The entities of its text (the NER) are stored with it.
            app.MapPut("/interviewparagraph", (InterviewParagraph p) => Task.FromResult(SoftDelete(() => session.InterviewParagraphSave(WithEntities(session, p)))));

            // POST /interview/transcript: saves the transcription of one recording, paragraph by paragraph, in place of the one stored;
            // each paragraph with the entities of its text.
            app.MapPost("/interview/transcript", (InterviewTranscript p) => Task.FromResult(SoftDelete(() =>
            {
                foreach (var paragraph in p?.paragraphs ?? new List<InterviewParagraph>())
                {
                    WithEntities(session, paragraph);
                }
                var saved = session.InterviewTranscriptSave(p);
                if (saved.ReturnedId is not (null or "-1"))
                {
                    RememberTranscript(p.interviewId, Path.GetFileName(p.audioFile ?? string.Empty), p.paragraphs.SelectMany(x => x.entities ?? new List<NerEntity>()).ToList());
                }
                return saved;
            })));

            // DELETE /interview/{id}: marks the interview and its paragraphs deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/interview/{id}", (string id) => Task.FromResult(SoftDelete(() => session.InterviewDelete(id))));

            // ── BPMN: the BPMN 2.0 diagrams of the chat (/bpmn), one document each (its XML and a comment), its id the slug of its title; never erased ──

            // GET /bpmn/{id}: loads a diagram by its slug (404 when it does not exist or is deleted).
            app.MapGet("/bpmn/{id}", (string id) =>
            {
                Bpmn result = session.BpmnGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /bpmn: creates a diagram (its id the slug of its title, or of the id given); "-1" when a diagram of that slug exists.
            app.MapPut("/bpmn", (Bpmn p) => Task.FromResult(SoftDelete(() => session.BpmnSave(p, create: true))));

            // POST /bpmn/update: saves a new version of a diagram (created when it does not exist); returns its id.
            app.MapPost("/bpmn/update", (Bpmn p) => Task.FromResult(SoftDelete(() => session.BpmnSave(p, create: false))));

            // POST /bpmn/search: free-text search on title, comment, summary, the names of the elements, tags and author (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/bpmn/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.BpmnsSearch(p.term ?? "*", size));
            });

            // POST /bpmn/last: the "size" diagrams changed last, newest first ("term" is ignored).
            app.MapPost("/bpmn/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.BpmnsLast(size));
            });

            // DELETE /bpmn/{id}: marks the diagram deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/bpmn/{id}", (string id) => Task.FromResult(SoftDelete(() => session.BpmnDelete(id))));

            // ── Infrastructure: the infrastructure diagrams of the chat (/infra), stored as the BPMN diagrams, in the infra index; never erased ──

            // GET /infra/{id}: loads a diagram by its slug (404 when it does not exist or is deleted).
            app.MapGet("/infra/{id}", (string id) =>
            {
                Infra result = session.InfraGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /infra: creates a diagram (its id the slug of its title); "-1" when a diagram of that slug exists.
            app.MapPut("/infra", (Infra p) => Task.FromResult(SoftDelete(() => session.InfraSave(p, create: true))));

            // POST /infra/update: saves a new version of a diagram (created when it does not exist); returns its id.
            app.MapPost("/infra/update", (Infra p) => Task.FromResult(SoftDelete(() => session.InfraSave(p, create: false))));

            // POST /infra/search: free-text search on title, comment, summary, the names of the elements, tags and author.
            app.MapPost("/infra/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.InfrasSearch(p.term ?? "*", size));
            });

            // POST /infra/last: the "size" diagrams changed last, newest first.
            app.MapPost("/infra/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.InfrasLast(size));
            });

            // DELETE /infra/{id}: marks the diagram deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/infra/{id}", (string id) => Task.FromResult(SoftDelete(() => session.InfraDelete(id))));

            // ── Mind maps: the mind maps of the chat (/mindmap), one document each (its FreeMind XML and a comment), its id the slug of its title; never erased ──

            // GET /mindmap/{id}: loads a map by its slug (404 when it does not exist or is deleted).
            app.MapGet("/mindmap/{id}", (string id) =>
            {
                Mindmap result = session.MindmapGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /mindmap: creates a map (its id the slug of its title, or of the id given); "-1" when a map of that slug exists.
            app.MapPut("/mindmap", (Mindmap p) => Task.FromResult(SoftDelete(() => session.MindmapSave(p, create: true))));

            // POST /mindmap/update: saves a new version of a map (created when it does not exist); returns its id.
            app.MapPost("/mindmap/update", (Mindmap p) => Task.FromResult(SoftDelete(() => session.MindmapSave(p, create: false))));

            // POST /mindmap/search: free-text search on title, comment, summary, the texts of the nodes, tags and author (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/mindmap/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.MindmapsSearch(p.term ?? "*", size));
            });

            // POST /mindmap/last: the "size" maps changed last, newest first ("term" is ignored).
            app.MapPost("/mindmap/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.MindmapsLast(size));
            });

            // DELETE /mindmap/{id}: marks the map deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/mindmap/{id}", (string id) => Task.FromResult(SoftDelete(() => session.MindmapDelete(id))));

            // ── Ghost writer: the writings of the chat (/write), one document each (general information, actors, locations, chapters and their texts), its id the slug of its title; never erased ──

            // GET /ghostwriter/{id}: loads a writing by its slug (404 when it does not exist or is deleted).
            app.MapGet("/ghostwriter/{id}", (string id) =>
            {
                GhostWriter result = session.GhostWriterGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /ghostwriter: creates a writing (its id the slug of its title, or of the id given); "-1" when a writing of that slug exists.
            app.MapPut("/ghostwriter", (GhostWriter p) => Task.FromResult(SoftDelete(() => session.GhostWriterSave(p, create: true))));

            // POST /ghostwriter/update: saves a new version of a writing (created when it does not exist); returns its id.
            app.MapPost("/ghostwriter/update", (GhostWriter p) => Task.FromResult(SoftDelete(() => session.GhostWriterSave(p, create: false))));

            // POST /ghostwriter/search: free-text search on title, description, goals, audience, style and author (body: SearchCriterias; "*" or empty: the newest ones).
            app.MapPost("/ghostwriter/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.GhostWritersSearch(p.term ?? "*", size));
            });

            // POST /ghostwriter/last: the "size" writings changed last, newest first ("term" is ignored).
            app.MapPost("/ghostwriter/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.GhostWritersLast(size));
            });

            // DELETE /ghostwriter/{id}: marks the writing deleted (isDeleted = true); returns its id, or "-1" with the error message.
            app.MapDelete("/ghostwriter/{id}", (string id) => Task.FromResult(SoftDelete(() => session.GhostWriterDelete(id))));

            // ── The journal of the writings: one entry per action that changes a text (save, chapter, generation, correction…). It only grows: no update, no delete. ──

            // PUT /ghostwriter/journal: adds an entry (a new id and the date of now, whatever was sent); returns its id, or "-1" with the error message.
            app.MapPut("/ghostwriter/journal", (GhostWriterJournal p) => Task.FromResult(SoftDelete(() => session.GhostWriterJournalAdd(p))));

            // POST /ghostwriter/journal/search: the "size" last entries of the writing "term" (its id), newest first.
            app.MapPost("/ghostwriter/journal/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 200;
                return Task.FromResult(session.GhostWriterJournalOf(p.term ?? string.Empty, size));
            });

            app.MapGet("/idea/{id}", (string id) =>
            {
                Idea result = session.IdeaGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /idea: creates an idea; returns the new id, or "-1" with the error message.
            app.MapPut("/idea", (Idea p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.IdeaSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /idea/update: saves (creates or overwrites) an idea; returns the saved id, or "-1" with the error message.
            app.MapPost("/idea/update", (Idea p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.IdeaSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /idea/search: free-text search on title, description, category and tags (body: SearchCriterias; "*" or empty: the newest ideas).
            app.MapPost("/idea/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.IdeasSearch(p.term ?? "*", size));
            });

            // POST /idea/last: the "size" most recent ideas, newest first ("term" is ignored).
            app.MapPost("/idea/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.IdeasLast(size));
            });

            // DELETE /idea/{id}: deletes an idea; returns its id, or "-1" with the error message.
            app.MapDelete("/idea/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.IdeaDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });


            // GET /diary/{id}: loads one diary entry by its document id (404 when it does not exist).
            app.MapGet("/diary/{id}", (string id) =>
            {
                Diary result = session.DiaryGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /diary: creates a diary entry; returns the new id, or "-1" with the error message.
            app.MapPut("/diary", (Diary p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.DiarySave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /diary/update: saves (creates or overwrites) a diary entry; returns the saved id, or "-1" with the error message.
            app.MapPost("/diary/update", (Diary p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.DiarySave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /diary/search: free-text search on the "text" field only (body: SearchCriterias; "*" or empty: the newest entries).
            app.MapPost("/diary/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.DiariesSearch(p.term ?? "*", size));
            });

            // POST /diary/last: the "size" most recent diary entries, newest first ("term" is ignored).
            app.MapPost("/diary/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.DiariesLast(size));
            });

            // DELETE /diary/{id}: soft delete, nothing is erased: the entry gets isDeleted = true (left out of search and last);
            // returns its id, or "-1" with the error message.
            app.MapDelete("/diary/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.DiaryDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /task/{id}: loads one task by its document id, deleted or not (404 when it does not exist).
            app.MapGet("/task/{id}", (string id) =>
            {
                TaskItem result = session.TaskItemGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /task: creates a task; returns the new id, or "-1" with the error message.
            app.MapPut("/task", (TaskItem p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.TaskItemSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /task/update: saves (creates or overwrites) a task; returns the saved id, or "-1" with the error message.
            app.MapPost("/task/update", (TaskItem p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.TaskItemSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /task/search: free-text search on title, description, type, status and comment (body: SearchCriterias; "*" or empty: the newest tasks); deleted tasks are left out.
            app.MapPost("/task/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.TaskItemsSearch(p.term ?? "*", size));
            });

            // POST /task/last: the "size" most recent tasks, newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/task/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.TaskItemsLast(size));
            });

            // DELETE /task/{id}: soft delete, nothing is erased: the task gets isDeleted = true (left out of search and last);
            // returns its id, or "-1" with the error message.
            app.MapDelete("/task/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.TaskItemDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });


            // GET /intel/{id}: loads one intel by its document id, deleted or not (404 when it does not exist).
            app.MapGet("/intel/{id}", (string id) =>
            {
                Intel result = session.IntelGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /intel: creates an intel; returns the new id, or "-1" with the error message.
            app.MapPut("/intel", (Intel p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.IntelSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /intel/update: saves (creates or overwrites) an intel; returns the saved id, or "-1" with the error message.
            app.MapPost("/intel/update", (Intel p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.IntelSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /intel/search: free-text search in all the text fields of the intels, lists included (body: SearchCriterias; "*" or empty: the newest intels); deleted intels are left out.
            app.MapPost("/intel/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.IntelsSearch(p.term ?? "*", size));
            });

            // POST /intel/last: the "size" most recent intels, newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/intel/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.IntelsLast(size));
            });

            // DELETE /intel/{id}: soft delete, nothing is erased: the intel gets isDeleted = true (left out of search and last);
            // returns its id, or "-1" with the error message.
            app.MapDelete("/intel/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.IntelDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });


            // PUT /useractivity: saves an activity of the journal of the chat (index useractivities); returns the new id, or "-1" with the error message.
            app.MapPut("/useractivity", (UserActivities p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.UserActivitySave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /useractivity/last: the "size" most recent activities, newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/useractivity/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.UserActivitiesLast(size));
            });

            // POST /useractivity/search: search in the action, window, link and description ("*" or empty: the newest); deleted ones left out.
            app.MapPost("/useractivity/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 50;
                return Task.FromResult(session.UserActivitiesSearch(p.term ?? "*", size));
            });

            // PUT /gamescore: saves the score of a finished game of the chat (game1: the morpion solitaire of /game:solitaire); returns the new id, or "-1" with the error message.
            app.MapPut("/gamescore", (GameScore p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.GameScoreSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /gamescore/best: the "size" best scores of the game named by "term" (game1 when empty), the highest first; deleted scores are left out.
            app.MapPost("/gamescore/best", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                var game = string.IsNullOrWhiteSpace(p.term) || p.term == "*" ? "game1" : p.term.Trim();
                return Task.FromResult(session.GameScoresBest(game, size));
            });

            // POST /gamescore/last: the "size" most recent scores (all games), newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/gamescore/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.GameScoresLast(size));
            });

            // DELETE /gamescore/{id}: soft delete, the score gets isDeleted = true (left out of best and last); returns its id, or "-1" with the error message.
            app.MapDelete("/gamescore/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.GameScoreDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });


            // GET /collectionplan/{id}: loads one collection plan by its document id, deleted or not (404 when it does not exist).
            app.MapGet("/collectionplan/{id}", (string id) =>
            {
                CollectionPlan result = session.CollectionPlanGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /collectionplan: creates a collection plan; returns the new id, or "-1" with the error message.
            app.MapPut("/collectionplan", (CollectionPlan p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.CollectionPlanSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /collectionplan/update: saves (creates or overwrites) a collection plan; returns the saved id, or "-1" with the error message.
            app.MapPost("/collectionplan/update", (CollectionPlan p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.CollectionPlanSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /collectionplan/search: free-text search in all the text fields of the collection plans (body: SearchCriterias; "*" or empty: the newest plans); deleted plans are left out.
            app.MapPost("/collectionplan/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.CollectionPlansSearch(p.term ?? "*", size));
            });

            // POST /collectionplan/last: the "size" most recent collection plans, newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/collectionplan/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.CollectionPlansLast(size));
            });

            // DELETE /collectionplan/{id}: soft delete, nothing is erased: the plan gets isDeleted = true (left out of search and last);
            // returns its id, or "-1" with the error message.
            app.MapDelete("/collectionplan/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.CollectionPlanDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });


            // GET /youtubevideo/{id}: loads one YouTube video by its document id, deleted or not (404 when it does not exist).
            app.MapGet("/youtubevideo/{id}", (string id) =>
            {
                YoutubeVideo result = session.YoutubeVideoGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /youtubevideo: creates a YouTube video; returns the new id, or "-1" with the error message.
            app.MapPut("/youtubevideo", (YoutubeVideo p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.YoutubeVideoSave(WithEntities(session, p));
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /youtubevideo/update: saves (creates or overwrites) a YouTube video; returns the saved id, or "-1" with the error message.
            app.MapPost("/youtubevideo/update", (YoutubeVideo p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.YoutubeVideoSave(WithEntities(session, p));
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /youtubevideo/search: free-text search on "title", "description" and "entities" (body: SearchCriterias; "*" or empty: the newest videos); deleted videos are left out.
            app.MapPost("/youtubevideo/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.YoutubeVideosSearch(p.term ?? "*", size));
            });

            // POST /youtubevideo/last: the "size" most recent YouTube videos, newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/youtubevideo/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.YoutubeVideosLast(size));
            });

            // DELETE /youtubevideo/{id}: soft delete, nothing is erased: the YouTube video gets isDeleted = true (left out of search
            // and last); returns its id, or "-1" with the error message.
            app.MapDelete("/youtubevideo/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.YoutubeVideoDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /document/{id}: loads one document by its id, deleted or not (404 when it does not exist).
            app.MapGet("/document/{id}", (string id) =>
            {
                Document result = session.DocumentGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /document: creates a document; returns the new id, or "-1" with the error message.
            app.MapPut("/document", (Document p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.DocumentSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /document/update: saves (creates or overwrites) a document; returns the saved id, or "-1" with the error message.
            app.MapPost("/document/update", (Document p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.DocumentSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /document/search: fuzzy search on "title", "description" and the content of every version (body: SearchCriterias; "*" or empty: the newest documents); deleted documents are left out.
            app.MapPost("/document/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.DocumentsSearch(p.term ?? "*", size));
            });

            // POST /document/last: the "size" most recent documents, newest first, without the deleted ones ("term" is ignored).
            app.MapPost("/document/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.DocumentsLast(size));
            });

            // DELETE /document/{id}: soft delete, nothing is erased: the document gets isDeleted = true (left out of search and
            // last); returns its id, or "-1" with the error message.
            app.MapDelete("/document/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.DocumentDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /completion/{id}: loads one completion by its document id (404 when it does not exist).
            app.MapGet("/completion/{id}", (string id) =>
            {
                Completion result = session.CompletionGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // POST /completion/update: saves (creates or overwrites) a completion; returns the saved id, or "-1" with the error message.
            app.MapPost("/completion/update", (Completion p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.CompletionSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /completion/search: free-text search on "prompt" and "completion" (body: SearchCriterias; "*" or empty: the newest completions).
            app.MapPost("/completion/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.CompletionsSearch(p.term ?? "*", size));
            });

            // POST /completion/query: the advanced search (body: CompletionQuery): a text in the prompt and/or the completion, a period
            // (from, to), models, languages, topics, collections, the order (newest, oldest, relevance), the size; the deleted ones left out.
            app.MapPost("/completion/query", (CompletionQuery p) => Task.FromResult(session.CompletionsQuery(p)));

            // POST /completion/last: the "size" most recent completions, newest first ("term" is ignored).
            app.MapPost("/completion/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.CompletionsLast(size));
            });

            // DELETE /completion/{id}: deletes a completion; returns its id, or "-1" with the error message.
            app.MapDelete("/completion/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.CompletionDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /relation/{id}: loads one relation by its id, deleted or not (404 when it does not exist).
            app.MapGet("/relation/{id}", (string id) =>
            {
                Relation result = session.RelationGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /relation: saves a version of a relation between windows (category "interdata" by default): a new relation is created with its versions; for a stored one (its id), the last version brought is added with the next number. Returns its id, or "-1" with the error message.
            app.MapPut("/relation", (Relation p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.RelationSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /relation/update: same as PUT /relation (adds a version to a stored relation).
            app.MapPost("/relation/update", (Relation p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.RelationSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /relation/search: fuzzy search on the nature, title, description and ends of all the versions (body: SearchCriterias); deleted ones left out.
            app.MapPost("/relation/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.RelationsSearch(p.term ?? "*", size));
            });

            // POST /relation/last: the "size" most recent relations, without the deleted ones.
            app.MapPost("/relation/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.RelationsLast(size));
            });

            // DELETE /relation/{id}: soft delete (isDeleted = true; restored with /relation/update and isDeleted = false).
            app.MapDelete("/relation/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.RelationDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /annotation/{id}: loads one annotation by its id, deleted or not (404 when it does not exist).
            app.MapGet("/annotation/{id}", (string id) =>
            {
                Annotation result = session.AnnotationGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /annotation: saves an annotation of an object of the application (an entity, a block of text, an image, an element of a project, an idea…): created without an id, replaced with the id of a stored one; tags normalized (#SOFTWARE…). Returns its id, or "-1" with the error message.
            app.MapPut("/annotation", (Annotation p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.AnnotationSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /annotation/update: same as PUT /annotation (replaces a stored annotation).
            app.MapPost("/annotation/update", (Annotation p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.AnnotationSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /annotation/search: fuzzy search on the text, tags, object annotated (type, id, title) and excerpt (body: SearchCriterias); deleted ones left out.
            app.MapPost("/annotation/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.AnnotationsSearch(p.term ?? "*", size));
            });

            // POST /annotation/last: the "size" most recent annotations, without the deleted ones.
            app.MapPost("/annotation/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.AnnotationsLast(size));
            });

            // DELETE /annotation/{id}: soft delete (isDeleted = true; restored with /annotation/update and isDeleted = false).
            app.MapDelete("/annotation/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.AnnotationDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /imageanalysis/{id}: loads one image analysis by its id, deleted or not (404 when it does not exist).
            app.MapGet("/imageanalysis/{id}", (string id) =>
            {
                ImageAnalysis result = session.ImageAnalysisGet(id);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // GET /imageanalysis/image?path=…: the analysis of the picture at this path (404 when it has none yet).
            app.MapGet("/imageanalysis/image", (string path) =>
            {
                ImageAnalysis result = session.ImageAnalysisOfImage(path);
                return result is null ? Results.NotFound() : Results.Ok(result);
            });

            // PUT /imageanalysis: saves the analysis of a picture (one per picture: its id comes from the path of the picture); returns the id, or "-1" with the error message.
            app.MapPut("/imageanalysis", (ImageAnalysis p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.ImageAnalysisSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /imageanalysis/update: same as PUT /imageanalysis (creates or overwrites the analysis of the picture).
            app.MapPost("/imageanalysis/update", (ImageAnalysis p) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.ImageAnalysisSave(p);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // POST /imageanalysis/search: fuzzy search on the file title, the AI analysis, the comments, the annotations and the place (body: SearchCriterias); deleted ones left out.
            app.MapPost("/imageanalysis/search", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.ImageAnalysesSearch(p.term ?? "*", size));
            });

            // POST /imageanalysis/last: the "size" most recent image analyses, without the deleted ones.
            app.MapPost("/imageanalysis/last", (SearchCriterias p) =>
            {
                var size = p.size > 0 ? p.size : 10;
                return Task.FromResult(session.ImageAnalysesLast(size));
            });

            // DELETE /imageanalysis/{id}: soft delete (isDeleted = true; restored with /imageanalysis/update and isDeleted = false).
            app.MapDelete("/imageanalysis/{id}", (string id) =>
            {
                ServiceReturn result = new ServiceReturn();
                try
                {
                    result = session.ImageAnalysisDelete(id);
                }
                catch (Exception ex)
                {
                    result.ReturnedId = "-1";
                    result.Message = ex.Message;
                    result.Success = false;
                }
                return Task.FromResult(result);
            });

            // GET /image/status: state of the Fooocus API that generates the images (version, default model, busy); 503 when it does not answer.
            app.MapGet("/image/status", async () =>
            {
                try
                {
                    return Results.Ok(await session.ImageStatusAsync());
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Fooocus API not reachable: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

            // GET /image/options: the styles, performances, aspect ratios and models an image generation accepts, and the defaults of Fooocus.
            app.MapGet("/image/options", async () =>
            {
                try
                {
                    return Results.Ok(await session.ImageOptionsAsync());
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Fooocus API not reachable: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

            // POST /image/generate: generates images from a prompt with Fooocus (body: ImageGenerationRequest); answers when they are done, with
            // their files and their data in base 64 (400 for an empty prompt, 502 when Fooocus fails or does not answer).
            app.MapPost("/image/generate", async (ImageGenerationRequest p) =>
            {
                if (string.IsNullOrWhiteSpace(p.prompt))
                    return Results.BadRequest(new ImageGenerationResult { error = "The prompt is empty" });
                var result = await session.ImageGenerateAsync(p);
                return result.error is null ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status502BadGateway);
            });

            return app;
        }

        /// <summary>A soft delete asked by a DELETE: its result, or the error as a result.</summary>
        /// <summary>
        /// The video with its entities: those the NER finds in its title, description and comment (found by the
        /// model, by name, once each; read again at each save since the text may have changed), then those added by
        /// hand (<c>manualEntities</c>, always kept). When the NER fails the entities found before are kept: the
        /// video is saved anyway.
        /// </summary>
        private static YoutubeVideo WithEntities(SessionManager session, YoutubeVideo video)
        {
            if (video is null)
            {
                return video;
            }
            static string Plain(string text) => string.Join(' ', (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            video.manualEntities = (video.manualEntities ?? new List<string>()).Select(Plain).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = video.entities ?? new List<string>();
            var text = string.Join("\n", new[] { video.title, video.description, video.comment }.Where(t => !string.IsNullOrWhiteSpace(t)));
            try
            {
                found = text.Length == 0 ? new List<string>() : Knowledge.ContextManager.EntityNames(Ner.NerService.Get(session.Config).Extract(text).entities);
            }
            catch (Exception ex)
            {
                Console.WriteLine("NER of a YouTube video: " + ex.Message);
            }
            video.entities = found.Concat(video.manualEntities).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return video;
        }

        /// <summary>The paragraph with the entities the NER finds in its text (kept as they are when the NER fails: the paragraph is saved anyway).</summary>
        private static InterviewParagraph WithEntities(SessionManager session, InterviewParagraph paragraph)
        {
            // A paragraph sent back with its entities (its emotions stored after an analysis) is not read again: its text does not change.
            if (paragraph is null || string.IsNullOrWhiteSpace(paragraph.text) || paragraph.entities is { Count: > 0 })
            {
                return paragraph;
            }
            try
            {
                paragraph.entities = Ner.NerService.Get(session.Config).Extract(paragraph.text).entities;
            }
            catch (Exception ex)
            {
                Console.WriteLine("NER of an interview paragraph: " + ex.Message);
                paragraph.entities ??= new List<NerEntity>();
            }
            return paragraph;
        }

        /// <summary>
        /// The entities of the transcriptions saved lately, by interview then by recording: an interview saved right
        /// after its transcription takes them from here, the search engine not showing the new paragraphs yet.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, Dictionary<string, List<NerEntity>> Files)> RecentTranscripts = new();

        private static void RememberTranscript(string interviewId, string audioFile, List<NerEntity> entities)
        {
            if (string.IsNullOrWhiteSpace(interviewId))
            {
                return;
            }
            foreach (var old in RecentTranscripts.Where(r => DateTime.UtcNow - r.Value.At > TimeSpan.FromMinutes(10)).Select(r => r.Key).ToList())
            {
                RecentTranscripts.TryRemove(old, out _);
            }
            RecentTranscripts.AddOrUpdate(interviewId.Trim(),
                _ => (DateTime.UtcNow, new Dictionary<string, List<NerEntity>> { [audioFile] = entities }),
                (_, recent) =>
                {
                    lock (recent.Files)
                    {
                        recent.Files[audioFile] = entities;
                    }
                    return (DateTime.UtcNow, recent.Files);
                });
        }

        /// <summary>Pictures, links and marks of the Markdown of the notes, taken away before the NER reads them.</summary>
        private static readonly System.Text.RegularExpressions.Regex NotesMarkup =
            new(@"!\[[^\]]*\]\([^)]*\)|\]\([^)]*\)|\*\*\[\d{2}:\d{2}:\d{2}\]\*\*|[*_#`>\[\]|~]+|<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// The interview with its entities: the ones the NER finds in its notes (paragraph by paragraph) and the ones
        /// stored with the paragraphs of its transcription, each once with its count. When the NER fails the entities
        /// sent are kept: the interview is saved anyway.
        /// </summary>
        private static Interview WithEntities(SessionManager session, Interview interview)
        {
            if (interview is null)
            {
                return interview;
            }
            try
            {
                var ner = Ner.NerService.Get(session.Config);
                var found = new List<NerEntity>();
                foreach (var block in (interview.content ?? string.Empty).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var text = NotesMarkup.Replace(block, " ").Trim();
                    if (text.Length > 0)
                    {
                        found.AddRange(ner.Extract(text).entities);
                    }
                }
                if (!string.IsNullOrWhiteSpace(interview.id))
                {
                    // The recordings transcribed in the last minutes: their entities as they were just saved.
                    var recent = new Dictionary<string, List<NerEntity>>();
                    if (RecentTranscripts.TryGetValue(interview.id.Trim(), out var remembered))
                    {
                        lock (remembered.Files)
                        {
                            recent = new Dictionary<string, List<NerEntity>>(remembered.Files);
                        }
                    }
                    found.AddRange(recent.Values.SelectMany(e => e));
                    foreach (var paragraph in session.InterviewParagraphs(interview.id).Where(x => !recent.ContainsKey(x.audioFile ?? string.Empty)))
                    {
                        // A paragraph stored before the NER was run on it is read now.
                        found.AddRange(paragraph.entities is { Count: > 0 } ? paragraph.entities : ner.Extract(paragraph.text).entities);
                    }
                }
                interview.entities = InterviewEntity.Merge(found);
            }
            catch (Exception ex)
            {
                Console.WriteLine("NER of an interview: " + ex.Message);
                interview.entities ??= new List<InterviewEntity>();
            }
            return interview;
        }

        /// <summary>
        /// A cache builder about to be saved, completed with what its prompts and completions say when it was left
        /// empty: its related entities (the named entities the NER finds: people, organisations, places…, by their name
        /// in the ontology when they are known there; the dates, numbers and amounts of the rules are left out) and its
        /// tags (the declared tags the text holds, see <see cref="Symbolic.TagGazetteer"/>). What the user filled is
        /// never touched; a detection that fails leaves the list empty.
        /// </summary>
        private static CacheBuilder WithDetections(SessionManager session, CacheBuilder builder)
        {
            if (builder is null)
            {
                return builder;
            }
            var texts = (builder.prompts ?? new List<string>()).Concat(builder.completions ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (texts.Count == 0)
            {
                return builder;
            }

            if (builder.relatedEntities is not { Count: > 0 })
            {
                try
                {
                    var ner = Ner.NerService.Get(session.Config);
                    var names = new List<string>();
                    foreach (var text in texts)
                    {
                        foreach (var entity in ner.Extract(text).entities.Where(e => e.source != "rule"))
                        {
                            var name = (string.IsNullOrWhiteSpace(entity.ontologyName) ? entity.text : entity.ontologyName).Trim();
                            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                            {
                                names.Add(name);
                            }
                        }
                    }
                    builder.relatedEntities = names;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("NER of a cache builder: " + ex.Message);
                }
            }

            if (builder.Tags is not { Count: > 0 })
            {
                try
                {
                    var found = Symbolic.TagGazetteer.Get(session.Config).Find(string.Join("\n", texts)).Tags.Select(t => t.Tag);
                    builder.Tags = TagRules.NormalizeTags(found);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Tags of a cache builder: " + ex.Message);
                }
            }
            return builder;
        }

        /// <summary>
        /// The project with its tags filled when it has none: the declared tags its name, its description and its goals
        /// hold (see <see cref="Symbolic.TagGazetteer"/>). What the user filled is kept; a failure leaves it without tags.
        /// </summary>
        private static Project WithTags(SessionManager session, Project project)
        {
            if (project.Tags is { Count: > 0 })
                return project;
            try
            {
                var text = string.Join("\n", new[] { project.name, project.description }.Concat(project.goals ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)));
                var found = Symbolic.TagGazetteer.Get(session.Config).Find(text).Tags.Select(t => t.Tag);
                project.Tags = TagRules.NormalizeTags(found);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Tags of a project: " + ex.Message);
            }
            return project;
        }

        /// <summary>Saves a voice, and tells the voices kept in memory for POST /voice/identify.</summary>
        private static ServiceReturn VoiceSaved(SessionManager session, Voice voice)
        {
            var saved = session.VoiceSave(voice);
            if (saved.Success)
                Voices.VoiceService.Get(session).Written(voice);
            return saved;
        }

        private static ServiceReturn SoftDelete(Func<ServiceReturn> mark)
        {
            try
            {
                return mark();
            }
            catch (Exception ex)
            {
                return new ServiceReturn { ReturnedId = "-1", Message = ex.Message, Success = false };
            }
        }

    }
}
