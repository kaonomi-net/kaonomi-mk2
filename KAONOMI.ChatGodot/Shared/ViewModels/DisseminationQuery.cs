using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KAONOMI.Chat.ViewModels;

/// <summary>
/// A search of <c>/dissimination</c>: names of entities (or any words) joined by the logical operators AND, OR and
/// NOT, with parentheses. The operators are written in capital letters (ET, OU, NON and SAUF are accepted too); words
/// that follow each other are one name (<c>Emmanuel Macron AND Airbus</c> has two terms); quotation marks keep a name
/// that holds an operator or a parenthesis; two terms side by side without operator are joined by AND. NOT is applied
/// first, then AND, then OR. An empty search matches every paragraph that names an entity.
/// </summary>
public abstract class DisseminationQuery
{
    /// <summary>True when the paragraph answers the search.</summary>
    public abstract bool Matches(DisseminationHit hit);

    /// <summary>The terms looked for (not those under a NOT), folded: what is highlighted in the results.</summary>
    public abstract IEnumerable<string> Terms();

    /// <summary>The search written back, fully parenthesised where needed.</summary>
    public abstract override string ToString();

    private sealed class All : DisseminationQuery
    {
        public override bool Matches(DisseminationHit hit) => hit.Entities.Count > 0;
        public override IEnumerable<string> Terms() => [];
        public override string ToString() => string.Empty;
    }

    private sealed class Term(string written) : DisseminationQuery
    {
        private readonly string _folded = Fold(written);
        public override bool Matches(DisseminationHit hit) => hit.Names(_folded);
        public override IEnumerable<string> Terms() => [_folded];
        public override string ToString() => written.Any(c => c is '(' or ')') || Operators.Contains(written) ? $"\"{written}\"" : written;
    }

    private sealed class And(DisseminationQuery left, DisseminationQuery right) : DisseminationQuery
    {
        public override bool Matches(DisseminationHit hit) => left.Matches(hit) && right.Matches(hit);
        public override IEnumerable<string> Terms() => left.Terms().Concat(right.Terms());
        public override string ToString() => $"{Wrap(left)} AND {Wrap(right)}";
        private static string Wrap(DisseminationQuery q) => q is Or ? $"({q})" : q.ToString();
    }

    private sealed class Or(DisseminationQuery left, DisseminationQuery right) : DisseminationQuery
    {
        public override bool Matches(DisseminationHit hit) => left.Matches(hit) || right.Matches(hit);
        public override IEnumerable<string> Terms() => left.Terms().Concat(right.Terms());
        public override string ToString() => $"{left} OR {right}";
    }

    private sealed class Not(DisseminationQuery inner) : DisseminationQuery
    {
        public override bool Matches(DisseminationHit hit) => !inner.Matches(hit);
        public override IEnumerable<string> Terms() => [];
        public override string ToString() => inner is Term ? $"NOT {inner}" : $"NOT ({inner})";
    }

    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal) { "AND", "OR", "NOT", "ET", "OU", "NON", "SAUF" };

    private enum Kind { Open, Close, And, Or, Not, Term }

    private sealed record Token(Kind Kind, string Text);

    /// <summary>The search read from <paramref name="text"/>, or what is wrong with it.</summary>
    public static (DisseminationQuery? Query, string? Error) Parse(string? text)
    {
        var tokens = Tokens(text ?? string.Empty);
        if (tokens.Count == 0)
            return (new All(), null);
        var at = 0;
        try
        {
            var query = ReadOr(tokens, ref at);
            if (at < tokens.Count)
                return (null, tokens[at].Kind == Kind.Close ? "A ) has no ( before it" : $"\"{tokens[at].Text}\" is not expected there");
            return (query, null);
        }
        catch (FormatException ex)
        {
            return (null, ex.Message);
        }
    }

    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        var words = new List<string>();
        void Flush()
        {
            if (words.Count > 0)
                tokens.Add(new Token(Kind.Term, string.Join(' ', words)));
            words.Clear();
        }
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c is '(' or ')')
            {
                Flush();
                tokens.Add(new Token(c == '(' ? Kind.Open : Kind.Close, c.ToString()));
                i++;
                continue;
            }
            if (c is '"' or '“' or '”' or '«')
            {
                Flush();
                var end = i + 1;
                while (end < text.Length && text[end] is not ('"' or '“' or '”' or '»'))
                    end++;
                var quoted = text[(i + 1)..Math.Min(end, text.Length)].Trim();
                if (quoted.Length > 0)
                    tokens.Add(new Token(Kind.Term, quoted));
                i = end + 1;
                continue;
            }
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')' or '"' or '“' or '”' or '«' or '»'))
                i++;
            var word = text[start..i];
            switch (word)
            {
                case "AND" or "ET":
                    Flush();
                    tokens.Add(new Token(Kind.And, word));
                    break;
                case "OR" or "OU":
                    Flush();
                    tokens.Add(new Token(Kind.Or, word));
                    break;
                case "NOT" or "NON" or "SAUF":
                    Flush();
                    tokens.Add(new Token(Kind.Not, word));
                    break;
                default:
                    words.Add(word);
                    break;
            }
        }
        Flush();
        return tokens;
    }

    private static DisseminationQuery ReadOr(List<Token> tokens, ref int at)
    {
        var left = ReadAnd(tokens, ref at);
        while (at < tokens.Count && tokens[at].Kind == Kind.Or)
        {
            var op = tokens[at++].Text;
            if (at >= tokens.Count)
                throw new FormatException($"Nothing after {op}");
            left = new Or(left, ReadAnd(tokens, ref at));
        }
        return left;
    }

    private static DisseminationQuery ReadAnd(List<Token> tokens, ref int at)
    {
        var left = ReadUnary(tokens, ref at);
        while (at < tokens.Count && tokens[at].Kind is Kind.And or Kind.Not or Kind.Term or Kind.Open)
        {
            // Two terms side by side are joined by AND.
            if (tokens[at].Kind == Kind.And)
            {
                var op = tokens[at++].Text;
                if (at >= tokens.Count)
                    throw new FormatException($"Nothing after {op}");
            }
            left = new And(left, ReadUnary(tokens, ref at));
        }
        return left;
    }

    private static DisseminationQuery ReadUnary(List<Token> tokens, ref int at)
    {
        if (at >= tokens.Count)
            throw new FormatException("The search ends too soon");
        var token = tokens[at];
        switch (token.Kind)
        {
            case Kind.Not:
                at++;
                if (at >= tokens.Count)
                    throw new FormatException($"Nothing after {token.Text}");
                return new Not(ReadUnary(tokens, ref at));
            case Kind.Open:
                at++;
                if (at < tokens.Count && tokens[at].Kind == Kind.Close)
                    throw new FormatException("Nothing between ( and )");
                var inner = ReadOr(tokens, ref at);
                if (at >= tokens.Count || tokens[at].Kind != Kind.Close)
                    throw new FormatException("A ) is missing");
                at++;
                return inner;
            case Kind.Term:
                at++;
                return new Term(token.Text);
            case Kind.Close:
                throw new FormatException("A ) has no ( before it");
            default:
                throw new FormatException($"{token.Text} needs a name before it");
        }
    }

    /// <summary>
    /// The text in lower case and without accents, character for character (its length does not change, so that a
    /// place found in the folded text is the same place in the text).
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var folded = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var c = ch is '’' or '‘' ? '\'' : ch is ' ' or ' ' or '\t' or '\n' or '\r' ? ' ' : ch;
            if (c > 127)
            {
                var parts = c.ToString().Normalize(NormalizationForm.FormD);
                c = parts.FirstOrDefault(p => CharUnicodeInfo.GetUnicodeCategory(p) != UnicodeCategory.NonSpacingMark, c);
            }
            folded.Append(char.ToLowerInvariant(c));
        }
        return folded.ToString();
    }

    /// <summary>The places of <paramref name="name"/> written as whole words in <paramref name="folded"/> (both folded).</summary>
    public static IEnumerable<int> Places(string folded, string name)
    {
        if (name.Length == 0)
            yield break;
        var at = folded.IndexOf(name, StringComparison.Ordinal);
        while (at >= 0)
        {
            var end = at + name.Length;
            if ((at == 0 || !char.IsLetterOrDigit(folded[at - 1])) && (end >= folded.Length || !char.IsLetterOrDigit(folded[end])))
                yield return at;
            at = folded.IndexOf(name, at + 1, StringComparison.Ordinal);
        }
    }
}
