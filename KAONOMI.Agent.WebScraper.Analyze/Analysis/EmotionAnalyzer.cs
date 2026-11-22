using KAONOMI.Core.Models;

namespace KAONOMI.Agent.WebScraper.Analyze.Analysis
{
    /// <summary>
    /// The emotional analysis of each person, organisation and event of a page: what the passages of the page about it
    /// express. The model of the advanced conversations is given the passages of a few entities at once and tells, for
    /// each, the share of seven emotions (as the emotion engine of the dialogs), the leading one, its intensity, the
    /// valence and one sentence saying why.
    /// </summary>
    public static class EmotionAnalyzer
    {
        private static string System(string language) =>
            "You analyse the emotions a press text expresses about the entities it names. For each entity you are given the passages about it. "
            + "Tell what these passages express about that entity, in their words and their tone (not what you think of it): "
            + "the share of each emotion in per cent, the seven summing to 100 (" + string.Join(", ", DetectedEmotion.Names) + "; neutral for a factual tone), "
            + "the leading one, its intensity from 0 (flat) to 1 (very strong), the valence from -1 (very negative) to 1 (very positive), "
            + $"and one sentence in {language} saying why. Answer with a JSON object only: "
            + "{\"entities\":[{\"name\":\"exact name given\",\"emotions\":{\"angry\":0,\"disgust\":0,\"fearful\":0,\"happy\":0,\"neutral\":100,\"sad\":0,\"surprised\":0},"
            + "\"leading\":\"neutral\",\"intensity\":0.2,\"valence\":0.0,\"summary\":\"...\"}]}";

        public static async Task AnalyseAsync(Llm llm, string model, AnalyzeConfig config, IReadOnlyList<string> blocks, List<AnalysedEntity> entities,
            List<string> warnings, Action<string> log, CancellationToken cancellationToken)
        {
            var language = EntityAnalyzer.LanguageName(config.language);
            var foldedBlocks = blocks.Select(TextTools.Fold).ToList();
            var batch = Math.Max(1, config.emotionBatchSize);
            var done = 0;
            for (var start = 0; start < entities.Count && !cancellationToken.IsCancellationRequested; start += batch)
            {
                var group = entities.Skip(start).Take(batch).ToList();
                var user = string.Join("\n\n", group.Select(e =>
                    $"### {e.name} ({e.type})\n" + TextTools.Clip(string.Join("\n", TextTools.Passages(blocks, foldedBlocks, e)), Math.Max(300, config.passageChars))));
                using var answer = await llm.AskJsonAsync(model, System(language), user, cancellationToken);
                if (answer is null)
                {
                    warnings.Add($"Emotions of {string.Join(", ", group.Select(e => e.name))}: {llm.LastError}");
                    continue;
                }
                foreach (var item in Json.Items(answer.RootElement, "entities"))
                {
                    var name = TextTools.Fold(Json.Text(item, "name"));
                    var entity = group.FirstOrDefault(e => TextTools.FoldedNames(e).Contains(name)) ?? group.FirstOrDefault(e => name.StartsWith(TextTools.Fold(e.name), StringComparison.Ordinal));
                    if (entity is null || !item.TryGetProperty("emotions", out var shares))
                        continue;
                    entity.emotion = Read(item, shares);
                    done++;
                }
            }
            if (cancellationToken.IsCancellationRequested)
                warnings.Add("Emotions: the time of the stage ended before every entity was analysed");
            log($"Emotions: {done} of {entities.Count} entit(ies) analysed");
        }

        private static EntityEmotion Read(System.Text.Json.JsonElement item, System.Text.Json.JsonElement shares)
        {
            var values = DetectedEmotion.Names.ToDictionary(n => n, n => Math.Max(0, Json.Number(shares, n)));
            // Shares given from 0 to 1, or not summing to 100: brought back to per cent.
            var sum = values.Values.Sum();
            if (sum <= 0)
            {
                values["neutral"] = 100;
                sum = 100;
            }
            foreach (var name in DetectedEmotion.Names)
                values[name] = Math.Round(values[name] * 100 / sum, 1);
            var leading = Json.Text(item, "leading").ToLowerInvariant();
            if (!DetectedEmotion.Names.Contains(leading))
                leading = values.OrderByDescending(v => v.Value).First().Key;
            return new EntityEmotion
            {
                emotions = new EmotionShares
                {
                    angry = values["angry"], disgust = values["disgust"], fearful = values["fearful"], happy = values["happy"],
                    neutral = values["neutral"], sad = values["sad"], surprised = values["surprised"]
                },
                leading = leading,
                intensity = Math.Round(Math.Clamp(Json.Number(item, "intensity"), 0, 1), 2),
                valence = Math.Round(Math.Clamp(Json.Number(item, "valence"), -1, 1), 2),
                summary = Json.Text(item, "summary")
            };
        }
    }
}
