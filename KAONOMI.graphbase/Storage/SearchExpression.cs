namespace KAONOMI.graphbase.Storage;

/// <summary>
/// A search with logical operators (the argument <c>match</c>): texts joined by <c>AND</c> and <c>OR</c> (any case),
/// grouped by parentheses. Words side by side are one text, found as written (<c>Parti démocrate</c>); a text in double
/// quotes may hold AND, OR and parentheses (<c>"rock and roll"</c>). AND binds first: <c>a OR b AND c</c> is
/// <c>a OR (b AND c)</c>. A quoted text or a group next to another text, without operator, is joined to it by AND.
/// </summary>
public abstract class SearchExpression
{
    /// <summary>The deepest nesting of parentheses accepted.</summary>
    public const int MaxDepth = 64;

    private SearchExpression()
    {
    }

    /// <summary>True when the expression holds, <paramref name="contains"/> telling whether a text is found.</summary>
    public abstract bool Matches(Func<string, bool> contains);

    /// <summary>The expression of a text; a <see cref="GraphException"/> (invalid) when it is not well formed.</summary>
    public static SearchExpression Parse(string text)
    {
        var parser = new Parser(Tokenize(text));
        if (parser.AtEnd)
            throw Invalid("nothing to search");
        var expression = parser.Or(0);
        if (!parser.AtEnd)
            throw Invalid("a ) closes no (");
        return expression;
    }

    /// <summary>The expression written back, every group in parentheses and every text in quotes (to read how it was understood).</summary>
    public abstract override string ToString();

    private static GraphException Invalid(string reason) => new(GraphException.Invalid, $"match: {reason}");

    private sealed class Text(string value) : SearchExpression
    {
        public override bool Matches(Func<string, bool> contains) => contains(value);
        public override string ToString() => $"\"{value}\"";
    }

    private sealed class All(IReadOnlyList<SearchExpression> items) : SearchExpression
    {
        public override bool Matches(Func<string, bool> contains) => items.All(i => i.Matches(contains));
        public override string ToString() => $"({string.Join(" AND ", items)})";
    }

    private sealed class Any(IReadOnlyList<SearchExpression> items) : SearchExpression
    {
        public override bool Matches(Func<string, bool> contains) => items.Any(i => i.Matches(contains));
        public override string ToString() => $"({string.Join(" OR ", items)})";
    }

    private enum Kind { Word, Quoted, And, Or, Open, Close }

    private readonly record struct Token(Kind Kind, string Text, int Start, int End);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '(' or ')')
            {
                tokens.Add(new Token(c == '(' ? Kind.Open : Kind.Close, c.ToString(), i, i + 1));
                i++;
            }
            else if (c == '"')
            {
                var close = text.IndexOf('"', i + 1);
                if (close < 0)
                    throw Invalid("a \" is not closed");
                var quoted = text[(i + 1)..close];
                if (quoted.Trim().Length == 0)
                    throw Invalid("nothing between the quotes \"\"");
                tokens.Add(new Token(Kind.Quoted, quoted, i, close + 1));
                i = close + 1;
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')' or '"'))
                    i++;
                var word = text[start..i];
                var kind = word.Equals("AND", StringComparison.OrdinalIgnoreCase) ? Kind.And
                    : word.Equals("OR", StringComparison.OrdinalIgnoreCase) ? Kind.Or
                    : Kind.Word;
                tokens.Add(new Token(kind, word, start, i));
            }
        }
        return Phrases(tokens, text);
    }

    /// <summary>The words side by side made one text, as written between them (its spaces kept).</summary>
    private static List<Token> Phrases(List<Token> tokens, string text)
    {
        var merged = new List<Token>();
        foreach (var token in tokens)
        {
            if (token.Kind == Kind.Word && merged.Count > 0 && merged[^1].Kind == Kind.Word)
            {
                var start = merged[^1].Start;
                merged[^1] = new Token(Kind.Word, text[start..token.End], start, token.End);
            }
            else
            {
                merged.Add(token);
            }
        }
        return merged;
    }

    /// <summary>
    /// or := and (OR and)* · and := item (AND? item)* · item := text | "text" | ( or ).
    /// </summary>
    private sealed class Parser(List<Token> tokens)
    {
        private int _next;

        public bool AtEnd => _next >= tokens.Count;

        private Token? Peek => AtEnd ? null : tokens[_next];

        public SearchExpression Or(int depth)
        {
            var items = new List<SearchExpression> { And(depth) };
            while (Peek?.Kind == Kind.Or)
            {
                _next++;
                items.Add(And(depth));
            }
            return items.Count == 1 ? items[0] : new Any(items);
        }

        private SearchExpression And(int depth)
        {
            var items = new List<SearchExpression> { Item(depth) };
            while (Peek is { } token)
            {
                if (token.Kind == Kind.And)
                    _next++;
                else if (token.Kind is not (Kind.Word or Kind.Quoted or Kind.Open))
                    break;
                items.Add(Item(depth));
            }
            return items.Count == 1 ? items[0] : new All(items);
        }

        private SearchExpression Item(int depth)
        {
            var before = _next > 0 ? tokens[_next - 1] : (Token?)null;
            if (Peek is not { } token)
                throw Invalid(before is { Kind: Kind.And or Kind.Or } op ? $"{op.Text} needs a text after it" : "nothing to search");
            switch (token.Kind)
            {
                case Kind.Word or Kind.Quoted:
                    _next++;
                    return new Text(token.Text);
                case Kind.Open:
                    if (depth >= MaxDepth)
                        throw Invalid($"more than {MaxDepth} parentheses one in another");
                    _next++;
                    if (Peek?.Kind == Kind.Close)
                        throw Invalid("nothing between ( and )");
                    var inside = Or(depth + 1);
                    if (Peek?.Kind != Kind.Close)
                        throw Invalid("a ( is not closed");
                    _next++;
                    return inside;
                case Kind.And or Kind.Or:
                    throw Invalid(before is { Kind: Kind.And or Kind.Or } previous
                        ? $"{previous.Text} {token.Text}: a text is missing between them"
                        : $"{token.Text} needs a text before it");
                default:
                    throw Invalid(before is { Kind: Kind.And or Kind.Or } op ? $"{op.Text} needs a text after it" : "a ) closes no (");
            }
        }
    }
}
