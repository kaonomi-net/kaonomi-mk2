using System.Text.Json;
using System.Text.RegularExpressions;

namespace KAONOMI.graphbase.Storage;

/// <summary>
/// A condition on a property of a node or of an edge (its path: <c>name</c>, <c>address.city</c>); every condition
/// given must hold. A property that is an array holds when one of its values does (as MongoDB): <c>eq: "fr"</c> holds
/// for <c>["en", "fr"]</c>; <c>ne</c> when none is equal. A missing property holds only <c>ne</c>, <c>exists: false</c>.
/// </summary>
public sealed class PropertyCondition
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public required string Property { get; init; }
    public JsonElement? Eq { get; init; }
    public JsonElement? Ne { get; init; }
    public JsonElement? Gt { get; init; }
    public JsonElement? Gte { get; init; }
    public JsonElement? Lt { get; init; }
    public JsonElement? Lte { get; init; }
    public IReadOnlyList<JsonElement>? In { get; init; }
    /// <summary>A string containing this text, case ignored.</summary>
    public string? Contains { get; init; }
    /// <summary>A string starting with this text, case ignored.</summary>
    public string? StartsWith { get; init; }
    /// <summary>A string matching this regular expression, case ignored.</summary>
    public Regex? Matches { get; init; }
    public bool? Exists { get; init; }

    /// <summary>The regular expression of <see cref="Matches"/>, or an error that says why it is not one.</summary>
    public static Regex Pattern(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new GraphException(GraphException.Invalid, $"matches: [{pattern}] is not a regular expression: {ex.Message}");
        }
    }

    public bool HoldsFor(JsonElement properties)
    {
        if (string.IsNullOrWhiteSpace(Property))
            throw new GraphException(GraphException.Invalid, "a condition needs the property it is on");
        var found = JsonValues.TryGet(properties, Property, out var value) && value.ValueKind != JsonValueKind.Null;
        if (Exists is { } exists && exists != found)
            return false;
        if (!found)
            return Eq is null && Gt is null && Gte is null && Lt is null && Lte is null && In is null && Contains is null && StartsWith is null && Matches is null;

        // The value and, for an array, each of its values.
        var values = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Prepend(value).ToList() : [value];
        if (Eq is { } eq && !values.Any(v => JsonValues.DeepEquals(v, eq)))
            return false;
        if (Ne is { } ne && values.Any(v => JsonValues.DeepEquals(v, ne)))
            return false;
        if (In is { } any && !values.Any(v => any.Any(a => JsonValues.DeepEquals(v, a))))
            return false;
        if (Gt is { } gt && !values.Any(v => JsonValues.Compare(v, gt) > 0))
            return false;
        if (Gte is { } gte && !values.Any(v => JsonValues.Compare(v, gte) >= 0))
            return false;
        if (Lt is { } lt && !values.Any(v => JsonValues.Compare(v, lt) < 0))
            return false;
        if (Lte is { } lte && !values.Any(v => JsonValues.Compare(v, lte) <= 0))
            return false;
        var texts = values.Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList();
        if (Contains is { } contains && !texts.Any(t => t.Contains(contains, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (StartsWith is { } start && !texts.Any(t => t.StartsWith(start, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (Matches is { } regex && !texts.Any(t => IsMatch(regex, t)))
            return false;
        return true;
    }

    private static bool IsMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
