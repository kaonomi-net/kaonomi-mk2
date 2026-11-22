using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KAONOMI.Chat.Services;

/// <summary>The measures of how a text is written (see <see cref="WritingCounter"/>).</summary>
public sealed record WritingCounts(
    int SentenceCount,
    int WordCount,
    double AverageSentenceLength,
    double AverageWordLength,
    double LexicalDiversity,
    double LongWordShare,
    double VocabularyComplexity,
    int AdverbCount,
    int ConditionalSentenceCount);

/// <summary>
/// How a text is written, counted on its sentences and words, without a model: the mean length of its sentences, the
/// complexity of its vocabulary, how many adverbs it has and how many of its sentences are in the conditional. In
/// French and in English: the adverbs are the words in -ment (-ly) that are not known nouns, and a list of common
/// adverbs; a sentence is in the conditional when one of its words has an ending of the conditional (-erait, -irait,
/// -drait, -rrait, aurait, serait… ; would, could, should, might). They are counts to compare texts, not a grammatical
/// analysis: some words are taken for what they are not. Shared by the chat (/entity) and the agent
/// KAONOMI.Agent.WebScraper.Analyze, which compiles this file too.
/// </summary>
public static class WritingCounter
{
    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?…])[»”""')\]]*\s+(?=[«“""(\[]?\p{Lu})", RegexOptions.Compiled);

    /// <summary>French nouns in -ment, which are not adverbs.</summary>
    private static readonly HashSet<string> NounsInMent = new(StringComparer.OrdinalIgnoreCase)
    {
        "gouvernement", "moment", "parlement", "mouvement", "événement", "evénement", "évènement", "département", "bâtiment", "logement",
        "document", "argument", "élément", "changement", "développement", "investissement", "financement", "engagement", "traitement",
        "règlement", "jugement", "comportement", "environnement", "sentiment", "monument", "instrument", "médicament", "appartement",
        "enseignement", "établissement", "renseignement", "licenciement", "paiement", "équipement", "armement", "déplacement", "classement",
        "rassemblement", "affrontement", "effondrement", "lancement", "commencement", "complément", "supplément", "segment", "ciment",
        "vêtement", "aliment", "tourment", "serment", "régiment", "campement", "abonnement", "stationnement", "fonctionnement",
        "réchauffement", "remaniement", "emplacement", "hébergement", "recrutement", "isolement", "soulèvement", "bombardement",
        "redressement", "rendement", "châtiment", "dénouement", "dévouement", "amendement", "avertissement", "renforcement", "ralentissement",
        "vieillissement", "élargissement", "accroissement", "rapprochement", "désarmement", "détournement", "enlèvement", "épuisement",
        "remboursement", "versement", "prélèvement", "placement", "remplacement", "renouvellement", "rétablissement", "ressentiment",
        "tremblement", "glissement", "gisement", "pavement", "ornement", "testament", "tempérament", "firmament", "parchemin", "fragment",
        "pigment", "ligament", "sacrement", "sédiment", "détriment", "compartiment", "assortiment", "divertissement", "blanchiment",
        "confinement", "déconfinement", "allègement", "désengagement", "acheminement", "attachement", "détachement", "dégagement",
        "encadrement", "endettement", "surendettement", "démantèlement", "harcèlement"
    };

    private static readonly HashSet<string> FrenchAdverbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "très", "toujours", "jamais", "souvent", "parfois", "déjà", "encore", "aussi", "plutôt", "trop", "assez", "beaucoup", "ici",
        "hier", "demain", "maintenant", "ensuite", "enfin", "alors", "ainsi", "pourtant", "cependant", "toutefois", "néanmoins",
        "désormais", "bientôt", "tard", "tôt", "vite", "presque", "surtout", "ailleurs", "dedans", "dehors", "partout", "longtemps",
        "davantage", "quasi", "volontiers", "exprès", "soudain", "aussitôt", "autrefois", "jadis", "naguère", "dorénavant", "auparavant",
        "certes", "peut-être", "environ", "mieux", "pis", "guère", "nullement", "tantôt"
    };

    private static readonly HashSet<string> NotAdverbsInLy = new(StringComparer.OrdinalIgnoreCase)
    {
        "family", "italy", "july", "supply", "reply", "apply", "rely", "ally", "belly", "bully", "jelly", "lily", "holy", "ugly",
        "assembly", "anomaly", "butterfly", "monopoly", "imply", "comply", "multiply", "fly", "ply", "sly", "rally", "tally", "folly",
        "melancholy", "homily", "silly", "jolly", "chilly", "hilly", "lonely", "lovely", "friendly", "elderly", "costly", "deadly", "likely"
    };

    /// <summary>Verbs in the imperfect that look like a conditional (their stem ends in -ir or -er): not counted.</summary>
    private static readonly HashSet<string> NotConditional = new(StringComparer.OrdinalIgnoreCase)
    {
        "tirait", "tiraient", "retirait", "retiraient", "attirait", "attiraient", "désirait", "désiraient", "admirait", "admiraient",
        "respirait", "respiraient", "inspirait", "inspiraient", "virait", "viraient", "expirait", "expiraient", "aspirait", "aspiraient",
        "soupirait", "soupiraient", "empirait", "empiraient", "chavirait", "délirait", "errait", "erraient", "serrait", "serraient",
        "terrait", "enterrait", "enterraient", "barrait", "barraient", "bourrait", "narrait", "portrait", "extrait", "retrait", "attrait",
        "distrait", "abstrait", "soustrait", "trait", "forfait",
        "ouvrait", "ouvraient", "couvrait", "couvraient", "découvrait", "découvraient", "recouvrait", "livrait", "livraient", "délivrait",
        "éclairait", "éclairaient", "cadrait", "encadrait", "encadraient"
    };

    private static readonly Regex Conditional = new(
    @"^(\p{L}*(e|i|r|d|tt|ît|v|ud|ompr|cl|oi|ui)|au|se|fe|sau)(rais|rait|rions|riez|raient)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EnglishConditional = new(@"\b(would|could|should|might)\b|\b\p{L}+'d\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True when <paramref name="language"/> (fr, en-US, English…) is English.</summary>
    public static bool IsEnglish(string? language) => (language ?? string.Empty).Trim().StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>The sentences of the blocks of a text: a block is at least one sentence (a title has no full stop).</summary>
    public static List<string> Sentences(IEnumerable<string> blocks) =>
    blocks.SelectMany(b => SentenceEnd.Split(b)).Select(s => s.Trim()).Where(s => Word.IsMatch(s)).ToList();

    /// <summary>The measures of the blocks of text, or null when they hold no word.</summary>
    public static WritingCounts? Count(IEnumerable<string> blocks, string? language)
    {
    var english = IsEnglish(language);
    var sentences = Sentences(blocks);
    var words = new List<string>();
    int adverbs = 0, conditional = 0;
    foreach (var sentence in sentences)
    {
        var inConditional = IsConditionalSentence(sentence, english);
        foreach (Match match in Word.Matches(sentence))
        {
                words.Add(match.Value.ToLowerInvariant());
                if (IsAdverb(match.Value, english))
                    adverbs++;
        }
        if (inConditional)
                conditional++;
    }
    // "peut-être" is two words for the count above.
    if (!english)
        adverbs += sentences.Sum(s => Regex.Matches(s, @"\bpeut-être\b", RegexOptions.IgnoreCase).Count);
    if (words.Count == 0)
        return null;

    // The elided letters (l', d', qu') are not words of the vocabulary.
    var real = words.Where(w => w.Length > 1).ToList();
    if (real.Count == 0)
        real = words;
    var averageWord = real.Average(w => (double)w.Length);
    var longShare = real.Count(w => w.Length >= 9) / (double)real.Count;
    var diversity = Diversity(real);
    var complexity = 100 * (0.4 * Scale(diversity, 0.55, 0.9) + 0.3 * Scale(averageWord, 4.2, 6.5) + 0.3 * Scale(longShare, 0.05, 0.3));
    return new WritingCounts(
        sentences.Count,
        words.Count,
        Math.Round(words.Count / (double)Math.Max(1, sentences.Count), 1),
        Math.Round(averageWord, 2),
        Math.Round(diversity, 3),
        Math.Round(longShare, 3),
        Math.Round(complexity, 1),
        adverbs,
        conditional);
    }

    /// <summary>The adverbs of the blocks of text, each with how many times it is written, the most used first.</summary>
    public static List<(string Adverb, int Count)> Adverbs(IEnumerable<string> blocks, string? language)
    {
    var english = IsEnglish(language);
    var found = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var block in blocks)
    {
        foreach (Match match in Word.Matches(block))
                if (IsAdverb(match.Value, english))
                    found[match.Value.ToLowerInvariant()] = found.GetValueOrDefault(match.Value.ToLowerInvariant()) + 1;
        if (!english)
                foreach (Match _ in Regex.Matches(block, @"\bpeut-être\b", RegexOptions.IgnoreCase))
                    found["peut-être"] = found.GetValueOrDefault("peut-être") + 1;
    }
    return found.OrderByDescending(a => a.Value).ThenBy(a => a.Key, StringComparer.Ordinal).Select(a => (a.Key, a.Value)).ToList();
    }

    /// <summary>The sentences of the blocks of text that are in the conditional.</summary>
    public static List<string> ConditionalSentences(IEnumerable<string> blocks, string? language)
    {
    var english = IsEnglish(language);
    return Sentences(blocks).Where(s => IsConditionalSentence(s, english)).ToList();
    }

    /// <summary>True when one of the words of <paramref name="sentence"/> is in the conditional.</summary>
    public static bool IsConditionalSentence(string sentence, bool english) =>
    english ? EnglishConditional.IsMatch(sentence) : Word.Matches(sentence).Any(m => IsConditional(m.Value));

    public static bool IsAdverb(string word, bool english)
    {
    if (english)
        return word.Length >= 5 && word.EndsWith("ly", StringComparison.OrdinalIgnoreCase) && !NotAdverbsInLy.Contains(word);
    if (FrenchAdverbs.Contains(word))
        return true;
    return word.Length >= 6 && word.EndsWith("ment", StringComparison.OrdinalIgnoreCase) && !NounsInMent.Contains(word);
    }

    public static bool IsConditional(string word) =>
    word.Length >= 6 && !NotConditional.Contains(word) && Conditional.IsMatch(word);

    /// <summary>
    /// The share of different words: over the whole text when it is short, else the mean over every window of 100
    /// words (a long text repeats its words more, which says nothing of its vocabulary).
    /// </summary>
    private static double Diversity(List<string> words)
    {
    const int window = 100;
    if (words.Count <= window)
        return words.Distinct().Count() / (double)words.Count;
    var counts = new Dictionary<string, int>();
    for (var i = 0; i < window; i++)
        counts[words[i]] = counts.GetValueOrDefault(words[i]) + 1;
    double sum = counts.Count;
    var windows = 1;
    for (var i = window; i < words.Count; i++, windows++)
    {
        var leaving = words[i - window];
        if (--counts[leaving] == 0)
                counts.Remove(leaving);
        counts[words[i]] = counts.GetValueOrDefault(words[i]) + 1;
        sum += counts.Count;
    }
    return sum / windows / window;
    }

    private static double Scale(double value, double low, double high) => Math.Clamp((value - low) / (high - low), 0, 1);
}
