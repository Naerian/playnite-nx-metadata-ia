# Changelog




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

