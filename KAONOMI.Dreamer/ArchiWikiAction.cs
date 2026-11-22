namespace KAONOMI.Dreamer
{
    using System.Text.Json;
    using KAONOMI.Core.Models;

    /// <summary>
    /// The action of the Dreamer that keeps the wiki of the architecture up to date: it reads the repository of /archi
    /// (<c>~/kaonomi/archi/workspace.json</c>, or <c>archiRepository</c> of config.json), writes its pages
    /// (<see cref="ArchiWikiBuilder"/>) and saves in the wiki those that changed.
    /// <para>
    /// A page is created when the wiki has none of that id, saved as a new version when its content or its title
    /// changed, and left alone when it is the same. A page someone changed by hand is never overwritten: only the pages
    /// whose last author is this action (or the first writing of the wiki from /archi) are, which their author says
    /// (<see cref="Mark"/>); the others are kept as they are, and told in the log.
    /// </para>
    /// <para>
    /// The repository is read again only when its file changed or the day did (a fact sheet may enter another phase
    /// of its lifecycle): the other runs of the night do nothing. <c>"archiWiki": false</c> in config.json turns the
    /// action off.
    /// </para>
    /// </summary>
    internal static class ArchiWikiAction
    {
        /// <summary>What ends the name of the author of a page written from /archi.</summary>
        public const string Mark = "(d'après /archi)";

        /// <summary>The author of the pages this action writes.</summary>
        public const string Author = "Kaonomi Dreamer " + Mark;

        /// <summary>What a run did: the pages created, saved again, the same, kept (changed by hand) and not saved.</summary>
        public sealed record Result(List<string> Created, List<string> Updated, List<string> Unchanged, List<string> Kept, List<string> Failed, string? Error = null)
        {
            /// <summary>True when everything that was to be saved was.</summary>
            public bool Success => Error is null && Failed.Count == 0;

            /// <summary>One line for the log.</summary>
            public string Summary => Error is not null
                ? "Wiki of the architecture not written: " + Error
                : $"Wiki of the architecture from /archi: {Created.Count} page(s) created, {Updated.Count} saved again, {Unchanged.Count} the same"
                  + (Kept.Count > 0 ? $", {Kept.Count} changed by hand and kept ({string.Join(", ", Kept)})" : string.Empty)
                  + (Failed.Count > 0 ? $", {Failed.Count} NOT saved ({string.Join(", ", Failed)})" : string.Empty);
        }

        /// <summary>The repository of /archi of the user that runs the Dreamer.</summary>
        public static string DefaultRepository =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "kaonomi", "archi", "workspace.json");

        /// <summary>
        /// What config.json says of the action: <c>archiWiki</c> (false turns it off; on by default) and
        /// <c>archiRepository</c> (the file of the repository; <see cref="DefaultRepository"/> by default).
        /// </summary>
        public static (bool Enabled, string Repository) Settings(string configFile = "config.json")
        {
            var enabled = true;
            var repository = DefaultRepository;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(configFile));
                if (json.RootElement.TryGetProperty("archiWiki", out var on) && on.ValueKind == JsonValueKind.False)
                    enabled = false;
                if (json.RootElement.TryGetProperty("archiRepository", out var file) && file.ValueKind == JsonValueKind.String && file.GetString() is { Length: > 0 } path)
                    repository = path.StartsWith("~/", StringComparison.Ordinal)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
                        : path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // No config.json to read: the action runs as by default.
            }
            return (enabled, repository);
        }

        private static readonly object Gate = new();
        private static string _done = string.Empty;

        /// <summary>
        /// Writes the wiki from the repository <paramref name="repositoryFile"/> as of <paramref name="today"/>:
        /// <paramref name="get"/> reads a page of the wiki (null: none), <paramref name="save"/> saves one (true: a new
        /// page). Null when there is nothing to do: no repository, or neither its file nor the day changed since the
        /// last run that saved everything. With <paramref name="dryRun"/>, nothing is saved: the result says what would be.
        /// </summary>
        public static Result? Run(Func<string, Wiki?> get, Func<Wiki, bool, ServiceReturn> save, string repositoryFile, DateOnly today, string appName, bool dryRun = false)
        {
            lock (Gate)
            {
                FileInfo file;
                string stamp;
                ArchiRepository repository;
                try
                {
                    file = new FileInfo(repositoryFile);
                    if (!file.Exists)
                        return null;
                    stamp = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{file.Length}|{today:yyyy-MM-dd}";
                    if (!dryRun && stamp == _done)
                        return null;
                    repository = ArchiRepository.Parse(File.ReadAllText(file.FullName));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
                {
                    return new Result(new(), new(), new(), new(), new(), $"{repositoryFile} cannot be read ({ex.Message})");
                }
                // An empty repository (a file being written, a new one) writes nothing: the wiki keeps what it has.
                if (repository.Sheets.Count == 0)
                    return null;

                var result = new Result(new(), new(), new(), new(), new());
                List<ArchiWikiPage> pages;
                try
                {
                    pages = new ArchiWikiBuilder(repository, today, DateOnly.FromDateTime(file.LastWriteTime)).Build();
                }
                catch (Exception ex)
                {
                    return result with { Error = "its pages could not be written (" + ex.Message + ")" };
                }

                foreach (var page in pages)
                {
                    try
                    {
                        var existing = get(page.Id);
                        if (existing is not null && existing.content == page.Content && existing.title == page.Title)
                        {
                            result.Unchanged.Add(page.Id);
                            continue;
                        }
                        // A page someone wrote or changed by hand is theirs.
                        if (existing is not null && !(existing.author ?? string.Empty).TrimEnd().EndsWith(Mark, StringComparison.Ordinal))
                        {
                            result.Kept.Add(page.Id);
                            continue;
                        }
                        if (!dryRun)
                        {
                            var saved = save(new Wiki
                            {
                                id = page.Id,
                                title = page.Title,
                                content = page.Content,
                                author = Author,
                                appName = appName,
                                machineName = Environment.MachineName,
                                userName = Environment.UserName,
                                comment = $"Written from the repository of /archi ({repository.Sheets.Count} fact sheets, {repository.Relations.Count} relations)",
                                projectId = existing?.projectId,
                                Tags = page.Tags,
                                Events = existing?.Events ?? new List<string>(),
                                Signals = existing?.Signals ?? new List<string>(),
                            }, existing is null);
                            if (!saved.Success || saved.ReturnedId is null or "-1")
                            {
                                result.Failed.Add(page.Id);
                                continue;
                            }
                        }
                        (existing is null ? result.Created : result.Updated).Add(page.Id);
                    }
                    catch (Exception)
                    {
                        // The search engine does not answer for this page: the next run tries again.
                        result.Failed.Add(page.Id);
                    }
                }
                if (!dryRun && result.Failed.Count == 0)
                    _done = stamp;
                return result;
            }
        }
    }
}
