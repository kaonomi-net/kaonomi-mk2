using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KAONOMI.graphbase.Storage;

/// <summary>The JSON of the properties: read a path, compare two values, merge a change.</summary>
public static class JsonValues
{
    public static readonly JsonElement EmptyObject = JsonSerializer.SerializeToElement(new JsonObject());

    /// <summary>A copy of <paramref name="element"/> that lives on its own (no document to dispose).</summary>
    public static JsonElement Detach(JsonElement element) => element.Clone();

    public static JsonElement ToElement(JsonNode? node) => node is null ? JsonSerializer.SerializeToElement<object?>(null) : JsonSerializer.SerializeToElement(node);

    public static JsonObject ToObject(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? JsonNode.Parse(element.GetRawText())!.AsObject() : new JsonObject();

    public static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    /// <summary>
    /// The properties given by a request: a JSON object (null: none), its null values left out. Anything else than an
    /// object is refused.
    /// </summary>
    public static JsonElement Properties(JsonElement? given, string what = "properties")
    {
        if (given is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return EmptyObject;
        if (value.ValueKind != JsonValueKind.Object)
            throw new GraphException(GraphException.Invalid, $"{what} must be a JSON object, not {Kind(value)}");
        var result = new JsonObject();
        foreach (var property in value.EnumerateObject())
            if (property.Value.ValueKind != JsonValueKind.Null)
                result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        return ToElement(result);
    }

    /// <summary>
    /// The properties after a change: <paramref name="patch"/> merged into <paramref name="current"/> (a null value
    /// removes its property), or in its place when <paramref name="replace"/>; then the properties of
    /// <paramref name="remove"/> taken away.
    /// </summary>
    public static JsonElement Merge(JsonElement current, JsonElement? patch, bool replace, IEnumerable<string>? remove)
    {
        var result = replace ? new JsonObject() : ToObject(current);
        if (patch is { } given && given.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (given.ValueKind != JsonValueKind.Object)
                throw new GraphException(GraphException.Invalid, $"properties must be a JSON object, not {Kind(given)}");
            foreach (var property in given.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                    result.Remove(property.Name);
                else
                    result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }
        foreach (var name in remove ?? [])
            result.Remove(name);
        return ToElement(result);
    }

    /// <summary>The value at <paramref name="path"/> (<c>name</c>, <c>address.city</c>, <c>aliases.0</c>) of an object.</summary>
    public static bool TryGet(JsonElement element, string path, out JsonElement value)
    {
        value = element;
        foreach (var part in path.Split('.'))
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object when value.TryGetProperty(part, out var child):
                    value = child;
                    break;
                case JsonValueKind.Array when int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < value.GetArrayLength():
                    value = value[index];
                    break;
                default:
                    value = default;
                    return false;
            }
        }
        return true;
    }

    /// <summary>The same JSON value (the order of the properties of an object does not count, nor 1 against 1.0).</summary>
    public static bool DeepEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            return CompareNumbers(a, b) == 0;
        if (a.ValueKind != b.ValueKind)
            return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                    return false;
                using (var x = a.EnumerateArray().GetEnumerator())
                using (var y = b.EnumerateArray().GetEnumerator())
                    while (x.MoveNext() && y.MoveNext())
                        if (!DeepEquals(x.Current, y.Current))
                            return false;
                return true;
            case JsonValueKind.Object:
                var count = 0;
                foreach (var property in a.EnumerateObject())
                {
                    count++;
                    if (!b.TryGetProperty(property.Name, out var other) || !DeepEquals(property.Value, other))
                        return false;
                }
                return count == b.EnumerateObject().Count();
            default:
                return true; // true, false, null
        }
    }

    /// <summary>The order of two values of the same kind (numbers, strings, booleans), null when they cannot be compared.</summary>
    public static int? Compare(JsonElement a, JsonElement b) => (a.ValueKind, b.ValueKind) switch
    {
        (JsonValueKind.Number, JsonValueKind.Number) => CompareNumbers(a, b),
        (JsonValueKind.String, JsonValueKind.String) => string.CompareOrdinal(a.GetString(), b.GetString()),
        (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => a.GetBoolean().CompareTo(b.GetBoolean()),
        _ => null
    };

    private static int CompareNumbers(JsonElement a, JsonElement b) =>
        a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x.CompareTo(y) : a.GetDouble().CompareTo(b.GetDouble());

    /// <summary>True when a string of <paramref name="element"/> (at any depth) contains <paramref name="text"/>, case ignored.</summary>
    public static bool ContainsText(JsonElement element, string text) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!.Contains(text, StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Any(e => ContainsText(e, text)),
        JsonValueKind.Object => element.EnumerateObject().Any(p => ContainsText(p.Value, text)),
        _ => false
    };

    public static string Kind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null"
    };
}
