using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KAONOMI.Chat.Services;

/// <summary>A version of a document of the search engine (index <c>documentversion</c> of the server).</summary>
public sealed class DocumentVersionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;

    [JsonPropertyName("documentIndex")]
    public string DocumentIndex { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentTitle")]
    public string DocumentTitle { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>save, delete, erase or restore.</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("changed")]
    public List<string> Changed { get; set; } = [];

    /// <summary>The document as it was saved (only when one version is read).</summary>
    [JsonPropertyName("json")]
    public string Json { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }
}

/// <summary>
/// The versions the server keeps of the documents saved by the windows of the chat (every state saved): listed,
/// read and restored. Set up with the server once, when the chat starts.
/// </summary>
public static class DocumentHistory
{
    private static KaonomiAssistantService? _assistant;

    public static void Initialize(KaonomiAssistantService assistant) => _assistant = assistant;

    private static KaonomiAssistantService Server => _assistant ?? throw new InvalidOperationException("no server");

    /// <summary>The versions of a document, the newest first (without their documents).</summary>
    public static Task<IReadOnlyList<DocumentVersionEntry>> VersionsAsync(string index, string id, CancellationToken cancellationToken = default) =>
        Server.DocumentVersionsAsync(index, id, cancellationToken);

    /// <summary>One version, with its document.</summary>
    public static Task<DocumentVersionEntry?> VersionAsync(string versionId, CancellationToken cancellationToken = default) =>
        Server.DocumentVersionAsync(versionId, cancellationToken);

    /// <summary>Restores a version (a new version, a copy of it).</summary>
    public static Task<KAONOMI.Chat.Models.ServiceReturn> RestoreAsync(string versionId, CancellationToken cancellationToken = default) =>
        Server.RestoreDocumentVersionAsync(versionId, cancellationToken);
}
