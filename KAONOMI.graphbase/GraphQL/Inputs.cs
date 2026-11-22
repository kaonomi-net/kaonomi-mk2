using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using KAONOMI.graphbase.Storage;

namespace KAONOMI.graphbase.GraphQL;

[GraphQLDescription("""
    A condition on a property (its path: "name", "address.city"); every condition given must hold. A property that is an
    array holds when one of its values does; a missing property holds only "ne" and "exists: false".
    """)]
public sealed class PropertyFilterInput
{
    [GraphQLDescription("The path of the property: \"name\", \"address.city\", \"aliases.0\".")]
    public string Property { get; set; } = string.Empty;

    [GraphQLType(typeof(JsonType))] public JsonElement? Eq { get; set; }
    [GraphQLType(typeof(JsonType))] public JsonElement? Ne { get; set; }
    [GraphQLType(typeof(JsonType))] public JsonElement? Gt { get; set; }
    [GraphQLType(typeof(JsonType))] public JsonElement? Gte { get; set; }
    [GraphQLType(typeof(JsonType))] public JsonElement? Lt { get; set; }
    [GraphQLType(typeof(JsonType))] public JsonElement? Lte { get; set; }

    [GraphQLType(typeof(ListType<NonNullType<JsonType>>))]
    [GraphQLDescription("Equal to one of these values.")]
    public IReadOnlyList<JsonElement>? In { get; set; }

    [GraphQLDescription("A string containing this text, case ignored.")]
    public string? Contains { get; set; }

    [GraphQLDescription("A string starting with this text, case ignored.")]
    public string? StartsWith { get; set; }

    [GraphQLDescription("A string matching this regular expression (.NET), case ignored.")]
    public string? Matches { get; set; }

    [GraphQLDescription("true: the property is there (and not null); false: it is not.")]
    public bool? Exists { get; set; }

    internal PropertyCondition ToCondition() => new()
    {
        Property = Property.Trim(),
        Eq = Value(Eq), Ne = Value(Ne), Gt = Value(Gt), Gte = Value(Gte), Lt = Value(Lt), Lte = Value(Lte),
        In = In?.Select(v => v.Clone()).ToList(),
        Contains = Contains, StartsWith = StartsWith,
        Matches = Matches is null ? null : PropertyCondition.Pattern(Matches),
        Exists = Exists
    };

    private static JsonElement? Value(JsonElement? value) =>
        value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } given ? given.Clone() : null;

    internal static IReadOnlyList<PropertyCondition>? Conditions(IReadOnlyList<PropertyFilterInput>? where) =>
        where?.Select(w => w.ToCondition()).ToList();
}

[GraphQLDescription("A node of an import: the node of that id is completed (labels added, properties merged), else made.")]
public sealed class NodeInput
{
    [GraphQLType(typeof(IdType))]
    [GraphQLDescription("Its id; none: a new one is made (the edges of the import cannot reach it then).")]
    public string? Id { get; set; }

    public IReadOnlyList<string>? Labels { get; set; }

    [GraphQLType(typeof(JsonType))]
    public JsonElement? Properties { get; set; }

    internal NodeWrite ToWrite() => new(Id, Labels, Properties?.Clone());
}

[GraphQLDescription("An edge of an import: the edge of that id is completed, else made (without id: the one of the same type between the same nodes, when mergeEdges).")]
public sealed class EdgeInput
{
    [GraphQLType(typeof(IdType))]
    public string? Id { get; set; }

    public string Type { get; set; } = string.Empty;

    [GraphQLType(typeof(NonNullType<IdType>))]
    public string From { get; set; } = string.Empty;

    [GraphQLType(typeof(NonNullType<IdType>))]
    public string To { get; set; } = string.Empty;

    [GraphQLType(typeof(JsonType))]
    public JsonElement? Properties { get; set; }

    internal EdgeWrite ToWrite() => new(Id, Type, From, To, Properties?.Clone());
}
