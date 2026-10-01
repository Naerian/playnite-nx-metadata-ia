# Changelog

## 1.4.32 — 2026-10-01
- Genres, tags and features stay more consistent across a full library fill: store brand noise is stripped, and each game can reuse spellings already in Playnite.
- Batches run one game at a time and switch AI providers faster when a free tier hits limits.
- The progress window closes reliably when a batch finishes or you cancel.

## 1.4.31 — 2026-09-30
- Clearer Library help for filling empty genres, tags and features.
- Genres, tags and features use more natural wording in your language instead of awkward word-for-word translations.
- Compound genres can be shortened to the distinctive part when that reads better (for example extraction shooter → Extracción), without inventing extra labels.
- Optional PCGamingWiki source: genres, play modes, camera perspective, companies and release date (by Steam AppID or exact title).
- Two store genres joined with a hyphen or slash (like Acción-Aventura) are split into separate labels.
- Setting only genres no longer copies those terms into Features by mistake.
- Library titles such as “… (2011) - Multiplayer” (also Single Player / Campaign) can reuse genres from the main game when the year is in the name; titles without a year are not guessed from a vague franchise name.

## 1.4.30 — 2026-09-30
- Descriptions now follow your Excluded terms and Kept terms settings.
- If you exclude a word like Action, that English spelling is blocked — the description can still say the same idea in your language (for example acción).
- Kept terms stay in their usual gaming form on genres, tags and features (Action, Indie, PvP). In description text, everyday words are still translated; only natural loanwords and acronyms stay as-is.
- Feature labels from the store are normalized to your kept spelling when needed (for example JcJ becomes PvP if PvP is on the keep list).
- Clearer on-screen help for both lists in Library, in all plugin languages.

## 1.4.29 — 2026-09-29
- Organize is more reliable with small local models: messy JSON is repaired when possible, and English libraries still apply store/IGDB lists if the model puts labels in the wrong field or drops existing append tags.
- Excluded terms work again after IGDB/organize, so blocked words are removed before saving.
- Excluded terms: quotes mean exact match only (e.g. `"Death"`); without quotes, matching is still “contains” (Death also blocks Deathmatch).
- Library → Kept terms: editable whitelist with defaults (Indie, Party, Roguelike, RPG, …). These stay in their industry form in every language and are not dropped as untranslated English.
- If you add a keep-list entry like Action, known translations (Acción, Aktion, …) are rewritten to that spelling.
- Optional “Derive from local game text” when genres/tags/features have no store/IGDB list (default: leave empty).
- Short industry labels such as Party stay as Party (party-game sense), not everyday translations like Fiesta.
- Organize debug log works again and includes store/IGDB tags in the context dump.
- Title matching ignores accents, so library titles like Pokemon match store/IGDB Pokémon (Snap, Stadium 2, and similar ROM dumps).

## 1.4.28 — 2026-09-29
- PlayStation Store works again for covers, backgrounds and official game info (description, genres, publisher, release date).
- Epic Store works again for official description, companies, tags and hero artwork.
- Emulated and retro libraries: ScreenScraper, TheGamesDB and MobyGames can now supply genres and other factual lists for AI organize—not only cover art. Turn on the new metadata checkboxes under Sources.
- ScreenScraper matches more reliably when the game has a console platform set in Playnite.
- Existing installs that already used these sources for media keep metadata enabled automatically after update.

## 1.4.27 — 2026-09-28
- History and provenance record the AI provider and model used for each field when Metadata AI writes or normalizes values. Older history entries without a model show “Not recorded” / “Sin definir”.
- Ordered backup provider profiles replace free local LM Studio/Ollama fallback. Existing local fallback settings migrate once into the chain.
- AI Provider settings split into Primary and Backup tabs. Backup list uses icon actions; add/edit open a form dialog with the same provider page link, short provider hint, and editable model dropdown (with refresh) as Primary. Usage becomes Check limit (modal) plus Open usage page, with a notice when the provider exposes none. Form and usage modals use the same DPI-aware centering as the setup assistant (Playnite main window / its monitor work area).
- On a hard provider failure mid-batch (rate limit, quota, auth, saturation, connection), remaining games continue on the next enabled profile after a short probe. Soft failures stay per-game. Rate-limited games requeue on the backup instead of failing the rest of the batch on the same provider.
- The batch results dialog can retry pending games with the same provider or with another profile from a dropdown.
- When the plugin language is not English, organize applies the model response. Leftovers that are exact copies of non-localized Incoming (Incoming − LocalizedIncoming for that request) trigger one retry and are stripped; there is no hard-coded English word list.
- If the organize/knowledge provider call fails (for example HTTP 503 high demand), the plugin waits and retries once. After retries are exhausted the game is reported as not updated — it no longer applies a localized-store-only list that would look complete while omitting IGDB and other sources. Unusable model JSON likewise leaves non-English term fields empty and fails the game instead of a false-success fallback.
- Before a metadata batch (or single-game generate), the plugin probes the AI provider with a tiny ping. If the provider is saturated or unreachable, it shows the error and starts no games.
- Cancel unblocks the progress dialog immediately instead of waiting for in-flight provider calls; remaining games are marked cancelled.
- Progress dialogs drop the “in progress” count. The footer shows elapsed time (hh:mm:ss), provider and model as hint text.
- Organize retries once when the model returns unparseable JSON, and once when the answer still copies non-localized store labels. Knowledge guesses (no store list) always get one localization confirmation retry when the plugin language is not English. Prompts require a single `{...}` object, forbid mixed-language terms arrays for ordinary nouns, and ask for acronyms in UPPERCASE (MMO, RPG). Short all-caps acronyms are preserved by casing.
- Organize, knowledge and main-call genre/tag/feature prompts spell out universal gaming loanwords and acronyms (Roguelike, Metroidvania, Indie, MOBA, RPG, …) so the model keeps them without a hard-coded allowlist in code.
- Organize/apply writes an append-only debug log at `%AppData%\Playnite\ExtensionsData\<plugin-id>\logs\organize-debug.log` with metadata sources (Steam, PSN, Xbox, Epic, IGDB, IGN, VNDB, Wikidata, …), request JSON, model response, retry, and final applied terms.
- Current-metadata mode is advanced-only, simplified to Use as context / Ignore (legacy Normalize maps to context), with a clearer help text. Persisted values are stable codes (`context` / `ignore`), not UI language strings.

## 1.4.26 — 2026-09-27
- IGDB accepts the same release when the title only differs by a roman range or a year: IV-VI matches IV•V•VI Remastered (2025), and not the Deluxe or I-III entries. If a query has no usable hit, up to four search variants are tried.
- Store and IGDB searches use the library title as written. Edition, year, region and remaster words are no longer stripped from the query.
- Organize accepts both JSON shapes: fields[].terms and the flat genres/tags/features arrays. A valid model answer is no longer discarded as a failed organize.
- Genre, tag and feature merges reserve room for non-localized sources such as IGDB, so a short Steam list cannot drop the rest before the model runs.
- Removed the hard-coded English reject list. Item caps are applied after the model response. Organize and the main metadata call use the same genre, tag and feature rules.
- HTML description templates keep their original casing. Sentence case no longer lowercases the whole HTML block.
- The dedicated organize pass still runs after full metadata generation. Genre, tag and feature matching ignores letter case.
- Main metadata, organize, knowledge and system-requirements prompts were rewritten shorter and stricter. Prefer-existing genres, tags and features are included in playniteLibraryVocabulary when those options are enabled.

## 1.4.25 — 2026-09-27
- After the main metadata call, genres/tags/features that are still not in the plugin language are re-organized and translated (for example Shooter to Disparos). Empty-only fields that are vacant also go through that pass instead of keeping raw English store labels.
- Progress dialogs name the active action (for example Organizing genres). A single game shows the title after a middle dot; multi-game batches omit the title and show an honest status such as "2 in progress · 12/70 done" while up to two games run in parallel, still in library list order.
- Completion messages list the metadata fields that actually changed (for example Genres and Tags), including after cancel or partial batch errors. If nothing changed, the dialog says so instead of a generic update count.
- A malformed AI JSON response for one game no longer stops the rest of a multi-game metadata batch; that game is skipped and others continue.
- AI responses that over-escape JSON quotes (for example `{ \"genres\": [...] }`) or wrap the object in a JSON string are repaired before parsing, so games such as It Takes Two no longer fail with `Invalid property identifier character: \`.
- Set genres, tags, features or categories skips the large metadata request and keeps the small organize/normalize AI call over store and IGDB lists.
- Full metadata generation organizes genres, tags and features in the same main AI response instead of a second round-trip. Focused menus send a shorter system prompt limited to the active fields.
- HTTP 429 responses retry with backoff and no longer stop the whole batch after retries are exhausted.

## 1.4.24 — 2026-09-27
- Tag and category prefixes are applied only to labels newly added by the plugin. Existing tags and categories already on the game are left unchanged in Append without deleting mode.

## 1.4.23 — 2026-09-26
- Tag and category prefixes keep brackets and casing as typed (for example [MAI]). Prefixes are applied after capitalization, and Uppercase only affects the label body.
- When IGDB metadata is enabled, IGDB genres and themes are always fetched as enrichment alongside store lists (not only when those lists are short). Steam Action/Adventure can merge with IGDB Shooter and Science fiction before the AI organises them.
- Genres, tags and features from English sources such as IGDB are translated into the plugin language (for example Shooter to Disparos in Spanish). Mixed Steam+IGDB lists no longer skip translation when Steam alone was already localised.

## 1.4.22 — 2026-09-26
- Library â†’ Fields table row backgrounds span all six columns again after the Uppercase column was added.
- Batch result dialog shows short reasons, a View detail modal for the full error, status badges for updated and pending games, and tooltips on the action buttons.
- API keys are remembered per AI provider, so switching between Claude, OpenAI, Groq and others no longer reuses the previous key and fails the model list with HTTP 401.
- Model list feedback is a visible status panel with Ready, Loading, Warning and Error badges instead of faint hint text, and HTTP 401 explains that the key must belong to the selected provider.
- Cloud providers clear the API key field when switching to a provider without a saved key, and the model list stays disabled until a key is entered.
- Official store context and strict company, age and region handling are always on. The former settings checkboxes were removed.
- Sources can enable metadata and media separately for Steam, PlayStation, Xbox, Epic, IGDB and IGN. Each pipeline respects those toggles. Test buttons under API key fields have clearer spacing.
- Batch error list rows and the View detail text box use the theme text brush so dark presets stay readable.
- The Sources tab help text explains that metadata sources feed verified facts for the AI context, while media sources supply covers, icons and backgrounds on their own path.

## 1.4.20 — 2026-09-25
- Genres, tags, features and categories are normalized through the plugin canonical vocabulary first, then matched to clean Playnite names, otherwise created. Dirty launcher spellings (for example Steam Spanish categories) are mapped to stable preferred terms and no longer overwrite AI output as final text.
- Changing AI provider refreshes the model list reliably: pending refreshes are queued, stale responses are ignored, and entering an API key triggers an automatic model reload.

## 1.4.19 — 2026-09-24
- A failed IGN catalogue request no longer aborts provider tests or metadata generation. IGN details currently return HTTP 400 because their persisted query asks for a removed field.

## 1.4.18 — 2026-09-24
- Selecting a provider applies its endpoint and default model immediately and refreshes that provider's model list. The Apply provider button is removed.
- Gemini 2.5 and 3 requests use a low reasoning effort so thinking tokens fit the completion budget. An invalid Gemini API key reported as HTTP 400 is shown as an authentication error.

## 1.4.17 — 2026-09-17
- Fixed Custom OpenAI-compatible endpoints that only had a base URL: the plugin now appends /chat/completions when it is missing (DeepSeek and similar providers).
- Clarified HTTP 404 errors so bare endpoint failures are no longer reported as a missing model, and include a short provider detail snippet.
- Added endpoint field help text explaining that base URLs and full /chat/completions URLs are both accepted.

## 1.4.16 — 2026-08-31
- System requirements are copied from the store, then localized in a dedicated AI pass. The description HTML is rebuilt after that pass.
- Faster metadata generation: session cache for store context, smaller per-game prompts, JSON object mode on supported cloud providers, and generous max_tokens by length.
- Templates tab: HTML formatting help matches the media-picker expander; the token table stays hidden until View tokens, then uses a 60/40 editor split.

## 1.4.15 — 2026-08-31
- System requirement tokens {min_sys_req} / {recommended_sys_req} filled from Steam pc_requirements (not AI).
- Hardened Steam AppID resolve, store fetch (cookies, success checks, language/country, TLS), and false batch-cancel on timeouts.
- System requirements render as HTML lists with bold labels; empty placeholders follow the output language.
- Plugin windows (audit, history, simulation, media picker, etc.) use Playnite chrome and the selected appearance preset.
- Localized remaining Sources-tab UI, logo/IGDB errors, and provenance details; removed the unused custom review window.

