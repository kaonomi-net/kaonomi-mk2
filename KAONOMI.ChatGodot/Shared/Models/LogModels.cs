using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KAONOMI.Chat.Models;

/// <summary>Activity log entry sent to <c>PUT /log</c> (<c>Log</c> on the server side).</summary>
public sealed class LogEntry
{
    /// <summary>Only value for a successful action or a plain event.</summary>
    public const string Info = "INFO";

    /// <summary>Only value for a failed action.</summary>
    public const string Error = "ERROR";

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "KAONOMI.Chat";

    /// <summary>The project the document is attached to (the id of a <c>Project</c>, chosen by its name in the header of the window), or null.</summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = Environment.MachineName;

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = Environment.UserName;

    [JsonPropertyName("comment")]
    /// <summary>What happened: "Area | action: details".</summary>
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    /// <summary><see cref="Info"/> or <see cref="Error"/>.</summary>
    public string Type { get; set; } = Info;
}

/// <summary>A detail of a performance measurement: its key and its value (text, integer or decimal).</summary>
public sealed class PerformanceMetaData
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("value_String")] public string? ValueString { get; set; }
    [JsonPropertyName("value_Int")] public int ValueInt { get; set; }
    [JsonPropertyName("value_Double")] public double ValueDouble { get; set; }

    /// <summary>The value as it reads: the text, else the decimal, else the integer.</summary>
    public string Value => !string.IsNullOrEmpty(ValueString) ? ValueString
        : ValueDouble != 0 ? ValueDouble.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        : ValueInt.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A performance measurement (<c>Performance</c> of the server, index <c>performance</c>, written by the agents): the
/// operation measured, its start, end and duration in milliseconds, its details, the application that measured it.
/// </summary>
public sealed class PerformanceEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("appName")] public string? AppName { get; set; }
    [JsonPropertyName("machineName")] public string? MachineName { get; set; }
    [JsonPropertyName("userName")] public string? UserName { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("operationName")] public string? OperationName { get; set; }
    [JsonPropertyName("startTime")] public DateTimeOffset StartTime { get; set; }
    [JsonPropertyName("endTime")] public DateTimeOffset EndTime { get; set; }
    [JsonPropertyName("durationMilliseconds")] public int DurationMilliseconds { get; set; }
    [JsonPropertyName("metadatas")] public List<PerformanceMetaData>? Metadatas { get; set; }
}
