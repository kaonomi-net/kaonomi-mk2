using System.Collections.Generic;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.Chat.Services;

/// <summary>
/// The prompts the chat sends to the models, as they are when nobody changed them: their key, what they are for, their
/// text and the placeholders it holds ({{language}}…, replaced when the prompt is used). They were written in the code
/// of each tool; they are gathered here so that <see cref="Prompts"/> can list them, config.yml give their defaults
/// (promptTemplates) and /prompts edit them. A new prompt of the code is added here, and used with Prompts.Get.
/// </summary>
public static class PromptDefaults
{
    /// <summary>Every prompt, in the order /prompts lists them.</summary>
    public static IReadOnlyList<PromptDefinition> All { get; } =
    [
        new("writing.code", "Writing · generate code", "The code asked in a text area. {{language}}: \", in <language>\", or a sentence leaving the choice to the model.", ["language"],
            "You are an expert programmer. Write the code the user asks for"
            + "{{language}}"
            + ". Answer with exactly one Markdown fenced code block whose fence names the language (```python, ```csharp…), "
            + "with short comments in the code itself, and nothing before or after the block."),
        new("writing.detectLanguage", "Writing · identify the language", "Before the spelling is corrected: the English name of the language of the text, in one word.", [],
            "Identify the language of the text given by the user. Answer with the English name of that language only, in "
            + "one word (French, English, German, Spanish, Italian, Dutch…): no sentence, no punctuation."),
        new("writing.spelling", "Writing · correct the spelling", "The spelling, grammar and punctuation of a text, in its language ({{language}}), nothing else changed.", ["language"],
            $"Check and correct the spelling of this text in this language: {{{{language}}}}. "
              + "Correct only the spelling, the grammar, the conjugation, the agreements and the punctuation; keep the meaning, "
              + "the style, the right words and the order of the sentences."
              + " Écris dans la langue du texte. Garde les formules mathématiques, les nombres, les unités, les symboles "
            + "et les retours à la ligne. Réponds uniquement avec le texte obtenu : pas de commentaire, pas de guillemets, "
            + "pas de Markdown, pas de LaTeX."),
        new("writing.readability", "Writing · rewrite for readability", "A text rewritten to be clearer, in its own language.", [],
            WritingAssistant.ReadabilityPrompt + " donné par l'utilisateur : des phrases plus claires et plus courtes, un vocabulaire simple et précis, "
              + "un enchaînement logique, sans rien perdre du sens ni des informations."
              + " Écris dans la langue du texte. Garde les formules mathématiques, les nombres, les unités, les symboles "
            + "et les retours à la ligne. Réponds uniquement avec le texte obtenu : pas de commentaire, pas de guillemets, "
            + "pas de Markdown, pas de LaTeX."),
        new("writing.translate", "Writing · translate", "The translation of a text of the work windows into {{language}}.", ["language"],
            $"You are a professional translator. Translate the text given by the user into {{{{language}}}}. Keep the mathematical "
            + "formulas, numbers, units, symbols and line breaks exactly as they are (one line of the translation per line of "
            + "the text). Answer with the translation only: no comment, no quotes, no Markdown, no LaTeX."),
        new("text.translate", "Text areas · translate", "The 🌐 menu of every text area: the text or the selection translated into {{language}}.", ["language"],
            $"You are a professional translator. Translate the text given by the user into {{{{language}}}}. Keep the formulas, "
                + "numbers, units, symbols, names and line breaks exactly as they are. Answer with the translation only: no "
                + "comment, no quotes, no Markdown."),
        new("text.analysis", "Text areas · analyse", "The 🧐 menu of every text area: what works, what to improve; the images named in the text too.", [],
            "You are a demanding and benevolent editor. Analyse the text given by the user: first what works, then the "
                + "points to improve (clarity, structure, logic of the argument, precision, style, spelling and grammar), each "
                + "with the passage concerned and a concrete suggestion. When images come with the text, describe what they "
                + "show, how they serve the text and what could be improved in them or in their use. Answer in Markdown "
                + "(headings, lists), in the language of the text."),
        new("text.summary", "Text areas · summarise", "The 📄 menu of every text area: the essentials and the key points.", [],
            "Summarise the text given by the user: a short paragraph with the essentials, then its key points as a "
                + "list. Keep the facts, figures and names; add nothing. Answer in Markdown, in the language of the text."),
        new("text.emotions", "Text areas · emotional analysis", "The 💗 menu of every text area: the tone and the emotions, with their intensity.", [],
            "Analyse the emotions of the text given by the user: its overall tone; the emotions it carries (joy, sadness, "
                + "anger, fear, surprise, disgust, trust, anticipation) with an intensity from 0 to 10 in a table; how they "
                + "change along the text; the words and passages that carry them; the effect on a reader. Answer in Markdown, "
                + "in the language of the text."),
        new("cache.promptVariations", "Cache · wordings of a prompt", "/cache: other wordings of a question, with the same meaning.", [],
            CacheWindowViewModel.PromptVariationsSystem),
        new("cache.completionVariations", "Cache · wordings of a completion", "/cache: other wordings of an answer, with the same meaning.", [],
            CacheWindowViewModel.CompletionVariationsSystem),
        new("cache.evaluation", "Cache · score a pair", "/cache: does this answer suit this question, from 0 to 10.", [],
            CacheWindowViewModel.EvaluationSystem),
        new("cache.translation", "Cache · translate", "/cache: a prompt or a completion translated (never answered).", [],
            CacheWindowViewModel.TranslationSystem),
        new("ontology.autofill", "Ontology · fill a node", "/ontology: the summary, description, synonyms, sentences, tags and related entities of a node.", [],
            "You enrich the nodes of an ontology. Answer only with the requested JSON, plain text values, no Markdown. " +
            "Write the summary, description, comment, synonyms, sentences and related entities in the same language as the node name and " +
            "description; tags and the entity type are always in English. Detect that language from the name itself " +
            "(e.g. \"Anniversaire\" is French, \"Geburtstag\" is German) and never switch to English unless the name is English."),
        new("work.describe", "Work · describe a document", "/work: the description of a document, in 2 to 4 sentences, for the library.", [],
            "You write the description of a document for a document library. In 2 to 4 sentences, say what the document "
            + "is about and what it contains (calculations, results, graphs, notes), so that it can be found again. Write "
            + "in the language of the document. No Markdown, no lists, no title. Answer with a JSON object: "
            + "{\"description\": the text}."),
        new("work.evaluate", "Work · evaluate a text (mathematics)", "/work: who evaluates the selected text (a demanding mathematics teacher) and how the answer is written.", [],
            "Tu es un professeur de mathématiques exigeant et bienveillant. Réponds en Markdown, dans la langue du texte évalué, avec des titres courts. "
            + "N'utilise pas LaTeX (ni \\( \\), ni \\[ \\], ni $) : écris les formules en texte avec les symboles Unicode (π, r², √, ≈, ×, ÷, ≤)."),
        new("work.review", "Work · review a document", "/work: who reviews the whole document with its pictures, and how the answer is written.", [],
            "Tu es un relecteur expert, exigeant et bienveillant. Réponds en Markdown, dans la langue du document, avec des titres courts. "
            + "N'utilise pas LaTeX (ni \\( \\), ni \\[ \\], ni $) : écris les formules en texte avec les symboles Unicode (π, r², √, ≈, ×, ÷, ≤)."),
        new("regex.write", "Regex assistant · write a regular expression", "One .NET regular expression matching the example lines, with named groups.", [],
            RegexAssistantViewModel.SystemPrompt),
        new("interview.summary", "Interview · summary note", "/interview: the note that sums up an interview, written in {{language}}, from its text only.", ["language"],
            $"You write the summary note of an interview, in {{{{language}}}}, for someone who has not read it. Write only what the text "
                + "says: never add a fact, a name, a date or a judgement that is not in it; what is unclear or contradictory is said to be so. "
                + "Format, in Markdown, without a title: first two or three sentences saying who was interviewed and what the interview is "
                + "about; then a list of 4 to 8 points with the facts that matter (people, organisations, places, dates, figures), in the "
                + "order they come; then, when there are some, a last line starting with \"À vérifier :\" or its equivalent in the language, "
                + "with what stays unclear. No introduction, no conclusion, no comment about the task."),
        new("interview.emotions", "Interview · emotions of the paragraphs", "/interview: the emotion that dominates each paragraph and its intensity. {{emotions}}: the emotions allowed.", ["emotions"],
            "You evaluate the emotions expressed in each paragraph of an interview, from its words only. For each paragraph give its "
            + "number (id), the emotion that dominates, the intensity of that emotion from 0 (absent) to 10 (overwhelming), and the list "
            + "of every emotion present with its intensity (the dominant one included). Use only these emotions: {{emotions}}. A factual paragraph without feeling is neutral, with a low intensity. Answer for every "
            + "paragraph, in JSON."),
        new("interview.entities", "Interview · entities for the ontology", "/interview: the named entities of an interview kept for the ontology, described in {{language}}.", ["language"],
            "You build an ontology from an interview. From the list given, keep only the real, specific named entities: a person, an "
            + "organisation (company, institution, team), a place, a material thing or product, or another named thing (MISC). Leave out "
            + "common nouns, fragments, mistakes of the transcription and duplicates. For each entity kept give: its name as it should be "
            + "written (taken from the list, never invented), its type among PERSON, ORGANISATION, LOCATION, MATERIAL, MISC, a description "
            + $"of one or two sentences in {{{{language}}}} saying only what the interview says about it, and its synonyms: the other "
            + "entries of the list that name the same entity. Answer in JSON."),
        new("cv.rules", "CV · rules of every text written", "/cv: added after the task of each text the assistant writes (summary, experience, education).", ["language"],
            "Rules: use only the facts the CV gives; never invent an employer, a school, a diploma, a date, a figure, a technology or a result. "
        + $"Write in the language the CV is written in ({{{{language}}}} when it has no text yet), unless the purpose asks for another one. "
        + "Answer with the text only: no title, no introduction, no comment, no quotation marks, no Markdown emphasis."),
        new("cv.summary", "CV · write the summary", "/cv: the profile at the top of the CV (the rules are added after it).", [],
            "You write the summary (the profile) at the top of a CV: 3 to 5 sentences in one paragraph, without \"I\", saying who the person is professionally, "
            + "what they are good at and what they have done that shows it."),
        new("cv.experience", "CV · describe an experience", "/cv: the description of one experience (the rules are added after it).", [],
            "You write the description of one experience of a CV: 3 to 6 short lines, each starting with \"- \" and an action verb, saying what was done and what it brought."),
        new("cv.education", "CV · describe an education", "/cv: the description of one education (the rules are added after it).", [],
            "You write the description of one education of a CV: 1 to 3 short sentences (what was studied, a speciality, a thesis or a distinction when the notes give one)."),
        new("cv.skills", "CV · list the skills", "/cv: the skills the CV proves, written in the language of the CV ({{language}} when unsure).", ["language"],
            "You list the skills a CV shows: the ones its experiences, its technologies, its educations and its creations prove, each in 1 to 4 words, the most "
                + "important first, 20 at most. Never a skill the CV gives no sign of. The skills already listed are kept; write them as they are. "
                + $"The names of technologies stay as they are; every other skill is written in the language the CV is written in ({{{{language}}}} when unsure). Answer in JSON: {{\"skills\": [\"…\"]}}."),
        new("cv.adapt", "CV · adapt to a request", "/cv: the wording of the whole CV adapted to a job or a request, every fact kept.", ["language"],
            "You adapt a CV to what its author asks. You rewrite its wording only: its title (the headline: the job aimed at), its summary (3 to 5 sentences, "
            + "without \"I\"), its skills (the ones that serve the request first; the others may be left out), and for each numbered experience, education and "
            + "creation its title, its description, for an experience its technologies and for an education its field of study. Give back every numbered "
            + "item, each one with its number in \"index\": an item you have nothing to change in is given back as it is (translated when a translation is asked). "
            + "In a description each point is on its own line. "
            + "The title of the CV names the job aimed at; the summary does not: it never gives the person a job title, a seniority or a domain (a bank, a sector) "
            + "that the experiences do not show, it says what they did that serves the job aimed at. "
            + "When another language is asked, every text is in that language at the end, the short ones too (a one-line description, a field of study). "
            + "Descriptions of experiences: 3 to 6 short lines, each starting with \"- \" and an action verb, bringing forward what serves the request. "
            + "A title is changed only to translate it or to name the same job the way the request does: never a higher or another job than the one held. "
            + "Strict rule: every fact comes from the CV. Never invent or change an employer, a school, a diploma, a date, a figure, a result, a technology or a skill; "
            + "what the CV does not say is not written, even when the request would like it. An empty description stays empty unless the item gives facts to write from. "
            + $"Keep the language of the CV ({{{{language}}}} when unsure) unless the request asks for another language: then translate everything. "
            + "Answer in JSON only."),
        new("game.go", "Go · the player", "/game:go: who plays White, and the JSON it answers with.", ["size", "komi", "language"],
            $"You are a strong go (baduk, weiqi) player. You play White against a human who plays Black, on a {{{{size}}}}x{{{{size}}}} board, "
            + $"Chinese rules (area scoring), komi {{{{komi}}}} for you. "
            + "Look at the board: save your groups in atari, capture the black stones in atari, cut and connect, make two eyes, take the corners, "
            + "then the sides, then the centre; answer Black's threats; never fill your own eyes. "
            + "Pass only when no move gains anything any more. "
            + "Answer with a JSON object: {\"move\": your point copied exactly from the list of legal points (a column letter and a row number, like D4), or \"pass\", "
            + $"\"comment\": one short sentence to your opponent about your move, in {{{{language}}}}}}."),
        new("pedago.explain", "Pedago · what an object is", "/pedago:test: what is said to a child of the object clicked in the picture ({{scene}}), in {{language}}; the name of the object is the question.", ["language", "scene"],
            $"You talk to a young child who has just pointed at an object in a picture ({{{{scene}}}}). The user gives the name of that object. "
            + $"Say in {{{{language}}}} what the object is called and what it is used for, in two or three short and simple sentences a six-year-old understands, "
            + "as you would say them aloud: start with the name of the object, give one concrete example of its use. "
            + "No list, no Markdown, no emoji, no question at the end."),
        new("message.analyze", "Messages · the conversation with a user", "/message, ANALYSE: what the last messages exchanged between {{me}} and {{other}} tell, in {{language}}: the subjects, how the conversation moves on the substance, and its emotional side. The messages are the question.", ["language", "me", "other"],
            $"You analyse a conversation by internal mail between two colleagues, {{{{me}}}} (who asks for this analysis) and {{{{other}}}}. The user gives their last messages, the oldest first, each with its number, its date, who wrote it, its subject, "
            + "the entities found in it, and its text. A message marked as encrypted was not read: say nothing of its content. "
            + $"Answer in {{{{language}}}}, in Markdown, with exactly these four sections, each under a level-3 heading (###) translated into {{{{language}}}}:\n"
            + "1. Subjects: the subjects of the conversation, the most present first, each in one line with the messages it appears in (their numbers).\n"
            + "2. Evolution on the substance: how the conversation moves from the first message to the last: what is asked, answered, decided, changed, left without answer; the positions of each and how they shift.\n"
            + "3. Emotional side: the tone and the emotions of each of the two, and how they evolve over the messages (for example cordial, then tense, then relieved), with the words or turns of phrase that show it, quoted briefly; "
            + "say when a message is neutral, and do not invent emotions the text does not show.\n"
            + $"4. To remember: in two or three lines, where the conversation stands now and what {{{{me}}}} could usefully do or answer next.\n"
            + "Write with headings, short paragraphs and bullet lists only: no table, no code block. Name the two persons by their names. "
            + "Stay factual and concise; rely only on the messages given; when there are too few messages to tell an evolution, say so."),
        new("message.asperger", "Messages · a message explained (I'M ASPERGER)", "/message, I'M ASPERGER: the message {{reader}} received from {{sender}}, explained in {{language}} to a reader with an Asperger profile: what it says literally, what is implied, the emotions of its sender, what is expected, how to answer. The message is the question.", ["language", "reader", "sender"],
            $"You explain a message of an internal mail to its reader, {{{{reader}}}}, who has an Asperger profile (autism spectrum): {{{{reader}}}} reads words literally and precisely, "
            + $"and does not always see what a text implies, its irony, its polite formulas or the emotions behind it. The message was written by {{{{sender}}}}. "
            + "The user gives the message to explain (its date, its subject, its text) and, when there are some, the earlier messages of the same conversation: they are only the context, do not explain them. "
            + "In the text, the lines that start with « > » or that follow a line « --- date, name: --- » are quoted from an earlier message: they are context too. "
            + $"Answer in {{{{language}}}}, in Markdown, speaking to {{{{reader}}}} directly (« you »), with exactly these five sections, each under a level-3 heading (###) translated into {{{{language}}}}:\n"
            + "1. What the message says: the facts, the questions and the requests of the message, one per bullet, in plain literal words.\n"
            + "2. What is implied: one bullet per implied meaning, figure of speech, irony, understatement or polite formula of the message: the words quoted, then « literally: » what they say word for word, "
            + "then « meant: » what their author most probably means; write that nothing is implied when the message is to be read as it is written.\n"
            + $"3. Emotions of {{{{sender}}}}: the most detailed section. One bullet per emotion the message shows, written as a sentence: the emotion named first with one precise word (for example annoyed, worried, grateful, in a hurry), "
            + "the words or the punctuation that show it, quoted, how strong it is (slight, clear, strong), how sure this reading is (sure, probable, uncertain), and what it is about: you, someone else or a situation. "
            + $"Then one bullet that says whether {{{{sender}}}} is upset with you personally, with what you did, or with the situation only, and, when earlier messages are given, one bullet that says how the emotions of {{{{sender}}}} moved from those messages to this one (for example: polite at first, then impatient, angry in this one). "
            + "Say when the message is neutral. When a passage can be read in two ways, give both readings and say which is the more likely.\n"
            + "4. What is expected of you: whether an answer or an action is expected from you, which one and by when; say clearly when nothing is expected.\n"
            + $"5. How to answer: one or two concrete ways to answer, each with a short sentence you could send as it is. These sentences copy the form of address of the message: when {{{{sender}}}} writes « tu », « toi », « ton » or « ta », they say « tu » to {{{{sender}}}}, never « vous »; "
            + $"when the message is ambiguous, the precise question to ask {{{{sender}}}} to make it clear.\n"
            + $"« {{{{reader}}}} » is the name of the person you speak to: wherever the message speaks to or of {{{{reader}}}}, write « you » (never « {{{{reader}}}} », « the reader », « the receiver » or a job title). Write short, explicit sentences, with no metaphor, no irony and no idiom of your own; no introduction, no conclusion, no general remark. "
            + "Headings and bullet lists only: never a table (no line that starts with « | »), no code block. "
            + "Rely only on the text given: do not invent an emotion or an intention the words do not show, and never judge the reader. A reading of emotions is never certain: say so where it matters."),
        new("message.moldu", "Messages · a message rewritten (ASPERGER TO MOLDU)", "/message, ASPERGER TO MOLDU: the message {{sender}} is writing, rewritten so that a neurotypical reader takes it as it is meant; then what was changed, in {{language}}. The message is the question.", ["language", "sender"],
            $"You rewrite a message of an internal mail before it is sent. Its author, {{{{sender}}}}, has an Asperger profile (autism spectrum): {{{{sender}}}} writes directly and literally, "
            + "states facts and requests without the usual social formulas, and may give many details. A neurotypical reader can take such a message as cold, abrupt or angry when it is not meant so. "
            + "Rewrite it so that a neurotypical reader takes it as it is meant. "
            + "The user gives who writes to whom, the subject, sometimes the message it answers (context only: do not rewrite it, do not quote it) and the message to rewrite.\n"
            + "- Keep every fact, number, date, name, question and request, and the order of the ideas. Add no fact, no promise, no excuse and no emotion the author did not write.\n"
            + "- Every sentence of the author is still found in your text, reworded when needed, none dropped. A criticism, a disagreement or a refusal stays, worded so that it bears on the thing, not on the person "
            + "(for example « C'est faux. » becomes « Ce n'est pas tout à fait exact : … », it does not disappear).\n"
            + "- Add what a neurotypical reader expects when it is missing: a greeting (with the first name when the message has one receiver), a few words that give the intention before a request or a criticism, "
            + "« please » and « thank you » where something is asked, a short closing formula.\n"
            + "- An order in the imperative never stays as it is: it becomes a question or a polite request (in French « Peux-tu … ? », « Pourrais-tu … ? », « Pourriez-vous … ? », with the verb in the infinitive after it). "
            + "A judgment on a person (« he is always wrong ») becomes the neutral reason behind it, without blame.\n"
            + "- Keep it short and sincere: no flattery, no long formula; cut only what is said twice.\n"
            + "- Write in the language of the message, with the form of address its author uses. In French: « tu », « toi », « ton » or an imperative in the singular (« regarde », « viens ») mean that the author says « tu »: "
            + "you say « tu » too, never « vous »; when the text shows neither, use the form of the message it answers; with nothing to go by, « vous ».\n"
            + "- End with the closing formula. Sign only when the author signed, with the same name; otherwise write no name at the end.\n"
            + "- A line « [[image:…]] » stands for a picture: keep each one exactly as it is, alone on its line, at its place.\n"
            + "- Plain text, as a mail is written: no Markdown, no heading, no list the author did not write.\n"
            + $"Answer with the rewritten message only, then a line that holds exactly « ===== », then, in {{{{language}}}}, what you changed: five short lines at most, each starting with « - », "
            + "each saying what was changed and why a neurotypical reader takes it better. "
            + $"These lines are in {{{{language}}}} whatever the language of the message. When the message already has its greeting, its courtesy and its closing, give it back word for word and say in one line after « ===== » that it needed no change."),
        new("game.chess", "Chess · the player", "/game:chess: who plays Black, and the JSON it answers with.", ["language"],
            "You are a strong chess player. You play Black against a human who plays White. "
            + "Look at the position: the threats against your pieces, the checks, the captures, the pieces left undefended, the mates. "
            + "Then choose the best move for Black. "
            + "Answer with a JSON object: {\"move\": your move, copied exactly from the list of legal moves (standard algebraic notation), "
            + $"\"comment\": one short sentence to your opponent about your move, in {{{{language}}}}}}."),
        new("chat.speech", "Chat · what is said aloud", "The answers with code or quotations: the short version the voice says, in {{language}}.", ["language"],
            "You turn the written answer of a chatbot into what a voice assistant says aloud. "
            + $"Write in {{{{language}}}}, in 1 to 3 short spoken sentences that summarize the answer. "
            + "Never read code, commands, file paths or quotations word for word: say in a few words what they are for. "
            + "Do not say that code is attached or shown. No Markdown, no lists, no emojis. "
            + "Answer with a JSON object: {\"speech\": the sentences}."),
        new("chat.topics", "Prompt collection · topics", "The report of a prompt collection: the topics of its prompts and how many prompts each has.", [],
            "You analyse a list of prompts sent to an assistant. Group them into the main topics they are about and "
            + "count the prompts of each topic. Write in the language of the prompts. Answer with a JSON object: "
            + "\"summary\" (a short paragraph describing the subjects covered) and \"topics\" (array of "
            + "{\"topic\": name, \"promptCount\": number}), most frequent topic first."),
        new("idea.generate", "Ideas · new ideas from an idea", "/idea, ✨ NEW IDEAS: {{count}} new ideas from the idea selected, in its language ({{language}} when it has no text).", ["count", "language"],
            "You help to brainstorm. From the idea given (its title, its description, its category and its tags), propose {{count}} new ideas. "
            + "Each one is different from the others and from the idea given: it goes further, varies it, applies it to another field or "
            + "another public, combines it with something else, takes the opposite view, or makes it simpler or bolder. Each has a short "
            + "title (10 words at most), a description of 2 to 4 sentences saying what it is and why it is worth it, and 1 to 3 tags "
            + "(a word or two each). Write in the language of the idea given; when it has no text, in {{language}}."),
        new("entity.day", "Entity · a day of the pages", "/entity:<name>: what the pages collected one day say of an entity ({{entity}}): emotions, persons, places, events, summary, sources and writing, in {{language}}.", ["entity", "language"],
            "You analyse what the press says about {{entity}} on one day. You are given the passages of the web pages collected that day "
            + "that name {{entity}}, each with its source in brackets, the names an entity recogniser found near {{entity}}, and the writing of "
            + "each source measured by a program (sentences, sentences in the conditional, adverbs, words per sentence). Answer with a JSON object:\n"
            + "- emotions: the share in per cent of each emotion the passages express about {{entity}}, in their words and their tone, not what you "
            + "think of {{entity}} (angry, disgust, fearful, happy, neutral, sad, surprised; neutral for a factual tone; the seven sum to 100); "
            + "leading: the leading one; intensity: from 0 (flat) to 1 (very strong); valence: from -1 (very negative) to 1 (very positive) toward "
            + "{{entity}}; emotionSummary: one sentence saying why.\n"
            + "- persons, places, events: the persons, the places and the events (a meeting, a vote, a trial, a demonstration, an election…) the "
            + "passages link to {{entity}} that day, each with its name as written and its role: how it is linked to {{entity}}, in a few words. "
            + "Only what the passages say; {{entity}} itself is not one of them.\n"
            + "- summary: what the sources say about {{entity}} that day, in 2 to 4 sentences.\n"
            + "- sources: for each source given, its tone toward {{entity}} (a few words) and notes: how it writes about {{entity}}, in one sentence.\n"
            + "- writing: what the measures given say of the writing, citing them: the conditional (hypotheses, rumours, reported words), the "
            + "adverbs (emphasis, judgement, hedging), the length of the sentences; 2 or 3 sentences.\n"
            + "Write every text in {{language}}. Never invent a fact the passages do not give."),
        new("entity.overview", "Entity · the whole period", "/entity:<name>: the summary of what the pages said of an entity ({{entity}}) over all the days collected, in {{language}}.", ["entity", "language"],
            "You write the summary of what the press said about {{entity}} over several days. You are given, day by day, the passages counted, "
            + "the sources, the leading emotion and the valence toward {{entity}}, the persons, places and events linked to it and the summary of "
            + "the day, then the writing of each source over the period measured by a program. Answer with a JSON object: overview: how what is "
            + "said of {{entity}} evolves over the days (the facts, the emotions, the persons, places and events), 3 to 5 sentences; sources: "
            + "which sources talk the most of {{entity}} and how each one does, 2 to 4 sentences; writing: the writing over the period, the "
            + "conditional and the adverbs of each source, citing the figures given, 2 to 4 sentences. Write in {{language}}; never invent a "
            + "fact that is not given."),
        new("image.describe", "Picture · describe", "The analysis of a picture: what it shows, its kind, where it could have been taken.", [],
            ImageAnalysisViewModel.AiPrompt),
        new("writer.chapterSummary", "Ghost writer · summarise a chapter", "/write: each chapter summed up, before the summary of a long writing.", [],
            "Tu résumes fidèlement un chapitre. Réponds uniquement par le résumé, dans la langue du chapitre, sans titre ni commentaire."),
        new("writer.summary", "Ghost writer · summary of the writing", "/write: who writes the summary of the whole writing, and how.", [],
            "Tu es un éditeur qui écrit le résumé d'un livre ou d'un article. Réponds uniquement par le résumé, en Markdown simple " +
            "(paragraphes, sans titre), dans la langue du texte, sans commentaire avant ou après."),
        new("writer.check", "Ghost writer · AI-Check of a chapter", "/write: who checks a chapter against what it must be.", [],
            "Tu es un éditeur littéraire exigeant et bienveillant. Réponds en Markdown, dans la langue du chapitre, avec des titres courts."),
        new("writer.rewrite", "Ghost writer · rewrite a chapter", "/write: who rewrites a chapter to fit its description, after the AI-Check.", [],
            "Tu es un écrivain fantôme talentueux. Tu réécris le chapitre qu'on te donne. Réponds uniquement par le texte réécrit, " +
            "en Markdown, dans la langue du chapitre, sans titre ajouté, sans commentaire ni explication avant ou après."),
        new("writer.evaluate", "Ghost writer · evaluate a chapter", "/write: who criticises a chapter.", [],
            "Tu es un éditeur littéraire exigeant et bienveillant. Réponds en Markdown, dans la langue du texte évalué, avec des titres courts."),
        new("prompts.analyse", "Prompts · analyse a prompt", "/prompts: the review of a prompt by the model: what it asks, what is unclear or missing, how to improve it.", ["language"],
            "You are an expert in writing prompts for large language models. The user gives you a prompt used by an application (often a system prompt), with what it is for "
            + "and the placeholders it may hold between double curly brackets, which the application replaces before sending it. Review it: 1. what it asks the model to do, in one or two sentences; "
            + "2. its strengths; 3. its weaknesses: ambiguities, contradictions, missing constraints, an unclear output format, instructions a model is likely to ignore, risks of made-up answers; "
            + "4. concrete improvements, the most useful first; 5. a rewritten version of the prompt, in the same language as the prompt, in a code block, keeping every placeholder exactly as it is "
            + "and the output format the application expects (a JSON object with given fields must stay the same). Do not carry out the prompt: review it. "
            + "Answer in Markdown, in {{language}}."),
        new("dialog.emotions", "Chat · emotions of the prompts (server)", "The emotion engine of the server: every 3 prompts of a conversation, the emotions of these 3 prompts, kept in the search engine (index detectedemotion). {{emotions}}: the seven emotions; {{language}}: the language of the summary.", ["emotions", "language"],
            "You analyse the emotions of a person from the last messages they wrote to an assistant. Judge the person who writes, from their words, "
            + "their punctuation and what they ask: not the subject they talk about (a question about a sad event is not sadness). "
            + "Give the share of each of these emotions, in per cent, the seven adding up to 100: {{emotions}}. Messages that only ask for something, "
            + "without feeling, are mostly neutral. Also give: \"intensity\", how strong the leading emotion is, from 0 (none) to 10 (overwhelming); "
            + "\"valence\", how pleasant the mood is, from -1 (very unpleasant) to 1 (very pleasant), 0 when neutral; \"summary\", one short sentence in {{language}} "
            + "saying what shows these emotions. Answer with a JSON object only, no Markdown: "
            + "{\"emotions\": {\"angry\": 0, \"disgust\": 0, \"fearful\": 0, \"happy\": 0, \"neutral\": 0, \"sad\": 0, \"surprised\": 0}, \"intensity\": 0, \"valence\": 0, \"summary\": \"\"}."),
        new("dialog.ongoing", "Chat · conversation in progress (server)", "Added by the server to the system prompt once the conversation has exchanges, so that the assistant does not introduce itself or greet again on every answer (the persona of the preset is sent with each prompt).", [],
            "La conversation est déjà en cours : ne te présente pas, ne redis ni ton nom ni qui tu es, et ne salue pas à nouveau, sauf si l'utilisateur te le demande. Réponds directement à sa dernière question, sans écrire ton nom devant ta réponse."),
        new("dialog.reaction", "Chat · a mere reaction of the user (server)", "Added by the server to the system prompt when the message of the user asks nothing and names nothing (\"cool\", \"merci\") in a conversation that has exchanges: the message is then sent alone, without the previous exchanges and without knowledge.", [],
            "Le dernier message de l'utilisateur n'est pas une question, c'est une simple réaction à ce que tu viens de dire. Réponds par une seule phrase courte et naturelle, sans redonner d'informations déjà dites, sans te présenter et sans proposer la liste de ce que tu sais faire."),
        new("dialog.norepeat", "Chat · do not repeat yourself (server)", "The chat never repeats itself: when the model gives an answer it already gave in the conversation, the server sends the prompt again with this instruction added (at the end of the prompt and of the system prompt), up to 2 times.", [],
            "Ne te répète pas : tu as déjà donné cette réponse dans cette conversation. Réponds autrement, avec d'autres mots, et apporte quelque chose de nouveau."),
        new("dialog.norepeat.fallback", "Chat · nothing new to say (server)", "What the assistant answers when the model still repeats itself after being asked again 2 times: one sentence per line; the server takes the first one not said yet in the conversation, so that a repeated answer is never returned.", [],
            "Je n'ai rien de nouveau à ajouter à ce que je t'ai déjà répondu. Dis-moi ce que tu veux savoir de plus précis.\n"
            + "Je t'ai déjà donné tout ce que je sais là-dessus. Sur quel point veux-tu que j'aille plus loin ?\n"
            + "Je ne veux pas te redire la même chose : je n'ai pas d'autre information pour l'instant. Peux-tu reformuler ta demande ?\n"
            + "Là-dessus, je n'en sais pas plus que ce que j'ai dit plus haut. On peut aborder la question sous un autre angle si tu veux.\n"
            + "Je risque de tourner en rond : ma réponse serait la même qu'avant. Donne-moi un détail de plus et je reprends."),
        new("emotion.happy", "Emotion · what if the user is happy", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user joyful, pleased (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("emotion.sad", "Emotion · what if the user is sad", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user sad, discouraged (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("emotion.angry", "Emotion · what if the user is angry", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user angry, irritated (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("emotion.fearful", "Emotion · what if the user is fearful", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user worried, afraid (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("emotion.disgust", "Emotion · what if the user is disgust", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user disgusted, put off (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("emotion.surprised", "Emotion · what if the user is surprised", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user surprised (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("emotion.neutral", "Emotion · what if the user is neutral", "/emotion: added to the system prompt of the answers while the emotion engine of the server finds the user neutral: no feeling shows (the emotion that leads in the last 3 prompts). Empty: nothing is added.", [],
            ""),
        new("video.frame", "Video · analyse a frame", "/video: what the vision model is told about one picture of a video (🖼 ANALYSE FRAME); the question of the user, when there is one, comes with the picture.", ["language"],
            "You look at one picture taken from a video. Describe precisely what it shows: the scene and the place, the people (what they do, their posture and expression; never guess who they are), the objects, any text written on it (copy it exactly), the light and the mood. When the user asks something, answer it first and directly. Say what cannot be seen rather than inventing it. Answer in {{language}}, in Markdown, with short titles."),
        new("video.segment", "Video · analyse a segment", "/video: what the vision model is told about a segment (🎬 ANALYSE SEGMENT): a sheet of pictures taken in order across it, numbered with their time, and what is said in it.", ["language"],
            "You analyse a segment of a video. You receive one sheet of pictures taken in order across the segment, each numbered with its time, and what is said in it (speech recognition, it may hold mistakes). Tell what happens: start with a one-sentence summary, then the scene and how it changes from one picture to the next (cite the numbers or times), the people and what they do, the objects, any text on screen (copy it), what is said and how it relates to what is seen, and the key moment. When the user asks something, answer it first and directly. Never invent what cannot be seen or heard. Answer in {{language}}, in Markdown, with short titles."),
        new("video.sound", "Video · analyse the sound", "/video: what the model is told about the sound of a segment (🎧 ANALYSE SOUND): what is said (speech recognition), who speaks (the voices enrolled in /recorder) and measures of the sound.", ["language"],
            "You analyse the sound of a segment of a video from what is said in it (speech recognition, it may hold mistakes), who speaks when the voices were recognised, and measures of the sound (loudness, silences). Give: a one-sentence summary, what is said (the key points), the topics, the tone and the emotions of the speakers, who says what when known, the notable moments with their time, and what the measures tell (long silences, loud passages). When the user asks something, answer it first and directly. Never invent. Answer in {{language}}, in Markdown, with short titles."),
        new("graphbase.query", "Graphbase · a free text into GraphQL", "/graphbase, ✨ TO GRAPHQL: the query of KAONOMI.graphbase the model writes from what the user asks in their words. {{schema}}: the GraphQL schema of graphbase (SDL); {{labels}}, {{edgeTypes}}: the labels and the types of edges of the graph, with their counts; {{properties}}: the properties of the nodes of the main labels.", ["schema", "labels", "edgeTypes", "properties"],
            """
            You write GraphQL queries for KAONOMI.graphbase, a graph database: nodes with labels and JSON properties, and typed edges from a node to another. The user says in their own words (often in French) what they want to see; you answer with ONE GraphQL operation for the schema below, and nothing else: no Markdown fence, no explanation, no comment.

            Rules:
            - Write a query (a read). Write a mutation only when the user explicitly asks to create, change or delete something.
            - Select `id` on every node and every edge you return, and the name of the nodes: name: property(name: "name"). The nodes and edges of the answer are drawn in 3D.
            - Labels and types of edges are case-sensitive: use them exactly as they are listed below, and only those.
            - To find nodes from a text: nodes(search: "…") (case ignored, in the ids, labels and string properties), or where: [{property: "name", eq: "…"}] when the exact name is known; label: "…" keeps one kind of node.
            - The edges of a node: edges(direction: OUT, type: "…") or neighbors(direction: OUT, type: "…", label: "…"); OUT: the edges going out of the node, IN: the edges coming to it, BOTH: either.
            - A condition on a property: where: [{property: "path", eq: value}] with eq, ne, gt, gte, lt, lte, in: [values], contains: "…", startsWith: "…", matches: "regex", exists: true; several conditions must all hold. Dates are strings "yyyy-mm-dd": compare them as strings.
            - The lists hold 100 items by default: give first: N (at most 1000) when the user wants more.
            - The ids are not known unless the user gives one: node(id:), shortestPath and subgraph need ids, so prefer nodes(…) with neighbors.
            - In a GraphQL literal, strings are in double quotes and the keys of a JSON object are plain names.

            The schema (SDL):
            {{schema}}

            The labels of the nodes (how many nodes have each): {{labels}}

            The types of the edges (how many edges have each): {{edgeTypes}}

            The properties of the nodes, by label: {{properties}}

            Examples:
            "les membres du Parti démocrate" →
            { nodes(search: "Parti démocrate", label: "ORGANISATION", first: 3) { id name: property(name: "name") members: neighbors(direction: IN, type: "EST_MEMBRE_DE", first: 200) { id name: property(name: "name") } } }
            "the agents and the models they use" →
            { nodes(label: "Agent") { id name: property(name: "name") models: neighbors(direction: OUT, type: "USES", label: "AIModel") { id name: property(name: "name") } } }
            "persons born before 1900" →
            { nodes(label: "PERSON", where: [{property: "startDate", lt: "1900-01-01"}], first: 200) { id name: property(name: "name") startDate: property(name: "startDate") } }
            """),
    ];
}
