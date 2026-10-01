namespace MetaDataIAPlugin
{
    /// <summary>
    /// Single source for genres/tags/features prompt policy. Organize, knowledge, and the
    /// main metadata call assemble wrappers around these shared rules.
    /// </summary>
    internal static class TermFieldPrompts
    {
        private const string ClosedJsonFieldsShape =
            "Return ONLY one JSON object. The response must start with { and end with }. " +
            "No markdown fences, no prose before/after, no JSON wrapped inside a string. " +
            "Output shape: {\"fields\":[{\"field\":\"genres\",\"terms\":[\"...\"]}]} ";

        private const string LanguageAndKeepListBody =
            "If target language is NOT English: " +
            "Translate all common descriptive nouns and generic store terms completely into the target language " +
            "(e.g., Action -> Acción, Shooter -> Disparos, Strategy -> Estrategia, Puzzle -> Puzle, or equivalent). " +
            "Keeplist: keep any incoming label that appears in keepLoanwords exactly as written (prefer the spelling from keepLoanwords). " +
            "Do not translate, synonymize, or replace those labels (e.g. if keepLoanwords contains Action, output Action — never Acción/Aktion). " +
            "Uppercase acronyms in keepLoanwords stay UPPERCASE. " +
            "For other industry labels not in keepLoanwords: translate into the target language like ordinary store terms. " +
            "Gaming-domain sense: translate within the video game context. Never replace an industry genre or tag with a literal, non-gaming everyday definition " +
            "(e.g., Party as party-game, not celebration — when Party is in keepLoanwords, keep \"Party\"). " +
            "Consistency: never output synonyms, mixed languages, or both localized and raw English variants for the same concept. " +
            "If target language IS English: keep standard canonical English labels. " +
            "Vocabulary priority: if playniteLibraryVocabulary defines a preferred spelling for a concept, reuse that exact spelling " +
            "(except keepLoanwords entries, which win over everyday translations). " +
            "preferredSpellings (soft, scoped): when a concept in incoming/existing is already covered by preferredSpellings, " +
            "reuse that exact string. Priority: playniteLibraryVocabulary (if locked) > preferredSpellings > keepLoanwords for matching loanwords > new label. " +
            "Never invent labels from preferredSpellings alone. Never omit an incoming concept only because it is missing from preferredSpellings. " +
            "Do not merge labels by vague similarity; only reuse when the same concept is clearly the one listed. ";

        private const string DistillationAndDedup =
            "Exactly one label per concept. " +
            "Never mix synonyms or languages for the same concept. Labels should sound like store/library metadata (concise, no final punctuation). " +
            "Grammar & Casing: Use bare nouns (e.g., 'Guardado', not 'Guardar'). Apply standard Sentence case (capitalize only the first letter) UNLESS the target language's grammar strictly dictates otherwise (e.g., capitalizing all nouns in German). Always keep protected acronyms UPPERCASE. " +
            "Distill compounds for naturalness: If an incoming compound genre (e.g., 'extraction shooter', 'looter shooter') becomes unnaturally long or clunky in the target language, distill it down to its core distinctive mechanic or theme (e.g., 'extraction shooter' becomes just 'Extracción'; 'looter shooter' becomes 'Botín' or 'Loot'). " +
            "Subsume generic terms: If you distill a compound by dropping the generic part, rely on other incoming labels (like 'Shooter' or 'Acción') to cover that base. Do not generate a 4-word label just to preserve both concepts. " +
            "Never join two independent store genres with a hyphen, slash or similar. If incoming already joins them that way, split into separate labels (Acción and Aventura). " +
            "When tags are also in this request, prefer putting bare camera perspective there; when the compound is already in genres incoming, adapt it naturally in genres (or keepLoanwords spelling such as TPS when listed). " +
            "Never invent a sibling perspective. Do not repeat the same concept across multiple fields. ";

        private const string FeatureNormalization =
            "Feature Normalization (Aggressive Deduplication): For the 'features' field, you must be ruthlessly concise and consistent. " +
            "1. Eradicate Store Brands & Clean Names: Strip platform names (like 'Steam', 'Xbox', 'Epic') from ALL features in ANY language. Exception: Keep 'Workshop' or 'Mods' for modding support, NEVER translate it literally to everyday words (e.g., never 'Taller'). " +
            "2. Unify Hardware & VR: Collapse specific controllers (DualShock, DualSense, etc.) into exactly ONE strict canonical phrase for full support and one for partial in the target language. NEVER use abbreviations (e.g., never use 'Compat.'). Use the most formal, standard noun (e.g., extrapolating formats like 'Full Controller Support' or 'Soporte total para mando' universally). Normalize Virtual Reality to 'VR'. " +
            "3. Unify display, network & community: Consolidate family sharing options into a single formal term (e.g., strictly 'Family Sharing' or 'Préstamo familiar', avoiding verbs like 'Compartir'). Consolidate leaderboards into a single short noun (e.g., 'Leaderboards' or 'Clasificaciones'). Consolidate remote play variants into a single standard term. Keep universal acronyms (e.g., keep 'MMO'). " +
            "Fix obvious typos in incoming data. ";

        private const string HandlingModesOrganize =
            "overwrite: terms must contain only normalized concepts from incoming. " +
            "append: merge unique concepts from existing and incoming; if a concept already exists in existing, do not add a translated/synonym duplicate from incoming. " +
            "empty: if existing already has items, return existing unchanged; if existing is empty, populate from incoming. " +
            "Item caps are applied by the plugin after your response — return the full normalized set from incoming; do not pretuncate.";

        /// <summary>
        /// Closed organize call for Set genres/tags/features (store/IGDB incoming lists).
        /// </summary>
        public static readonly string OrganizeSystemPrompt =
            "You edit short game metadata labels. " +
            ClosedJsonFieldsShape +
            "Rules: " +
            "1. Output format: Echo each input field once. In \"terms\", return only the final normalized labels as a flat string array (not nested arrays). " +
            "2. Source and Grounding: Organize strictly from each field's existing and incoming lists. " +
            "Never invent concepts absent from those lists. If an incoming list is empty, return an empty terms array for that field. " +
            "3. Language and Terminology: " +
            "Read target language from \"language\" and \"languageName\". " +
            LanguageAndKeepListBody +
            "4. Canonical label, Deduplication & Distillation: " +
            DistillationAndDedup +
            FeatureNormalization +
            "5. Handling mode: " +
            HandlingModesOrganize;

        /// <summary>
        /// Closed knowledge call when there is no store list and local-text fallback is enabled.
        /// </summary>
        public static readonly string KnowledgePrompt =
            "No store returned a list for the fields in this request. Extract short, accurate metadata labels only from the provided game facts and description. " +
            ClosedJsonFieldsShape +
            "Rules: " +
            "1. Language and Terminology: Echo each input field once. In \"terms\", return a flat string array. " +
            "Read target language from \"language\" and \"languageName\". " +
            "If language is NOT English: translate all common descriptive nouns into the requested language " +
            "(e.g., Shooter -> Disparos, Action -> Acción, Adventure -> Aventura, Puzzle -> Puzle, or the equivalent). " +
            "Keeplist: keep any label that appears in keepLoanwords exactly as written (prefer keepLoanwords spelling; acronyms stay UPPERCASE). " +
            "Do not translate or replace keepLoanwords entries (e.g. Action stays Action, never Acción). Other labels not in keepLoanwords: translate like ordinary terms. " +
            "Gaming-domain sense: translate within the video game context; never a literal non-gaming everyday definition " +
            "(e.g., Party as party-game, not celebration — when Party is in keepLoanwords, keep \"Party\"). " +
            "Consistency: never mix languages or synonyms for the same concept. " +
            "If English: keep standard English labels. " +
            "2. Strict Concept Normalization: " +
            DistillationAndDedup +
            "Make sure that when you distill a compound, you also output the base generic genre (e.g., Disparos, Acción) as a separate label if it is not already covered. " +
            "3. Local-text grounding (Zero Hallucination): Anchor labels strictly to phrases and facts in the provided description and release fields. " +
            "Do NOT use outside knowledge of the title beyond those supplied facts. " +
            "Do NOT extrapolate features or genres from modern remakes or subsequent ports. " +
            "For retro releases, never invent modern technical features (e.g., no cloud saves or online co-op for 8/16-bit console titles). " +
            "If the description/facts are insufficient to support a label with high confidence, omit it. If the game cannot be identified from the supplied facts, return an empty terms array. " +
            "4. Field Categorization & Feature Normalization: genres: Core video game store genres only. tags: Setting, theme, gameplay mechanics, and camera perspectives (e.g., First-person, Third-person) clearly supported by the text. " +
            "features: Functional gameplay traits for this specific platform release only when the text states them explicitly (player count, local co-op, controller support). " +
            FeatureNormalization +
            "At most 4 concise feature items. Never put player counts or features into genres or tags. " +
            "5. Handling mode: overwrite: terms must contain only new normalized labels. " +
            "append: preserve existing labels and add missing unique labels for this release without adding synonyms or language duplicates. " +
            "empty: if existing already has items, return existing unchanged; otherwise populate.";

        /// <summary>
        /// Block appended to the full-metadata system prompt when genres/tags/features are organized in-call.
        /// </summary>
        public static string MainCallFieldCategorizationRules(int maxFeatures)
        {
            var cap = maxFeatures < 1 ? 1 : maxFeatures;
            return
                "Field Categorization Rules (genres, tags, features): " +
                "1. Source and Grounding: When termCandidates is present, organize genres, tags, and features strictly from each field's existing and incoming lists. " +
                "Never invent concepts absent from those lists. If an incoming list is empty, return an empty array for that field. " +
                "2. Language and Terminology: Read target language from targetLanguage / targetLanguageName. " +
                LanguageAndKeepListBody +
                DistillationAndDedup +
                FeatureNormalization +
                "3. Field Sorting Logic: features: Player count, input devices, co-op modes, achievements, and controller support. " +
                "Populate up to " + cap + " verified items. NEVER invent or hallucinate items just to reach a quota. If evidence only supports 1 item, return exactly 1. " +
                "genres: Core video game store genres only. " +
                "tags: Setting, theme, artistic style, perspective, and gameplay mechanics. " +
                "Do not duplicate the same concept across multiple fields (player count/modes belong to features; store genres stay in genres; theme/style stay in tags). " +
                "4. Handling mode: overwrite: Output only normalized concepts derived from incoming. " +
                "append: Keep existing labels and append unique concepts from incoming; if a concept already exists in existing, do not add a translated/synonym duplicate from incoming. " +
                "empty: If existing already has items, return existing unchanged; if existing is empty, populate from incoming. " +
                "Item caps (MaxGenres / MaxTags / MaxFeatures) are applied by the plugin after your response — return the full normalized set from incoming; do not pretuncate.";
        }

        /// <summary>
        /// User-message suffix when organize/knowledge left raw foreign store labels.
        /// </summary>
        public static string TranslationRetrySuffix(string knowledgeHint)
        {
            return
                "\n\nRETRY: Your previous answer still copied raw store/IGDB labels that are not in the target language. " +
                (knowledgeHint ?? string.Empty) +
                "Rewrite EVERY ordinary store label into the target language. " +
                "Do not mix languages in the same terms array. " +
                DistillationAndDedup +
                FeatureNormalization + 
                "CRITICAL: Maintain aggressive deduplication and brevity. Do not write full sentences to sound 'natural' (e.g. Shooter -> Disparos in Spanish, never 'Juego de disparos'). " +
                "Prefer keepLoanwords spelling when listed (e.g. TPS for third-person shooter). " +
                "Keep labels listed in keepLoanwords unchanged (and acronyms UPPERCASE). " +
                "Return the full JSON object again.";
        }
    }
}
