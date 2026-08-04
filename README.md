# Metadata AI

Metadata AI is a Playnite metadata provider extension that uses AI, official store context, and external media sources to generate or normalize consistent game descriptions, metadata lists, links, sorting names, covers, icons, and backgrounds in the language and structure you choose.

It is designed for users who want a Playnite library with a unified editorial style instead of mixed descriptions and tags from multiple stores.

## Documentation

The complete user guide is available in the project Wiki:

- [English documentation](https://github.com/Naerian/playnite-nx-metadata-ia/wiki/EN-Overview-and-Installation)
- [Documentación en español](https://github.com/Naerian/playnite-nx-metadata-ia/wiki/ES-Descripcion-General-e-Instalacion)
- [Wiki language selector](https://github.com/Naerian/playnite-nx-metadata-ia/wiki)

The Wiki covers initial setup, AI providers, templates and tokens, field rules, vocabulary consistency, media sources and priorities, automatic imports, batch actions, backups, credential security, cleanup, and troubleshooting.

## Who this extension is for

Metadata AI is mainly intended for users who want a consistent Playnite library across many different sources: Steam, GOG, Epic, emulators, random launchers and manual entries.

It is useful when your library has mixed description styles, missing metadata, duplicated tags, inconsistent categories, untranslated content, or no clear sorting names for sequels and collections.

It is not meant to replace validated metadata sources when those already provide exactly what you need. Instead, it complements them by normalizing, translating, restructuring and filling gaps according to your own templates and rules.

The extension does not generate AI artwork. Media assets are fetched from configured sources such as Steam public assets, PlayStation Store, Xbox Store, Epic Store, SteamGridDB, RAWG, MobyGames and IGDB.

## Features

- Generate game descriptions with customizable HTML templates.
- Use reusable templates for short, medium, long, RPG, adventure, indie, emulation, platform-specific, source-specific, or rule-based descriptions.
- Generate and apply genres, tags, features, developers, publishers, age ratings, regions, categories, links, and sorting names.
- Control each field independently: skip, fill only empty fields, append without deleting, or overwrite.
- Set per-field maximum item counts for lists such as tags, genres, categories, features, and links.
- Prefer existing Playnite terms to keep tags, genres, features, and categories consistent.
- Use blacklist rules and prefixes for generated tags and categories.
- Generate metadata in a selected output language from a dropdown.
- Use official store data as factual AI context before rewriting, translating, or normalizing text.
- Reuse the Playnite library integration that imported each game as an exact, trusted metadata and media source.
- Use stricter factual guards for developers, publishers, age ratings, and regions to reduce invented metadata.
- Fill release dates and series only when an exact origin integration or trusted official source supplies the value; conflicting trusted values are shown for review and are not applied automatically.
- Start with a guided first-time setup assistant that configures a safe profile without touching the library.
- Simulate metadata changes before applying them, including before/after values and field-level source information.
- Record the last 20 Metadata AI operations and undo metadata or media changes, including restored media file backups.
- Inspect field provenance to see whether a value was normalized from an origin integration, official store context, existing Playnite metadata, a local deterministic rule, or unsupported AI generation that should be reviewed.
- Automatically process newly imported games after library updates.
- Generate media assets from multiple configurable sources.
- Configure media source priority separately for covers, icons, and backgrounds.
- Control automatic media selection by source, usable resolution, or strict no-upscaling rules.
- Audit the current library for missing enabled fields, broken image references, unreadable or nearly blank images, and media below configurable resolution thresholds.
- Lock cover, icon, background, or optional logo media per game so automatic, batch, and simulated workflows cannot replace them.
- Repair low-quality media selectively and replace existing files only when a validated candidate is measurably better.
- Choose crop origin and JPEG output quality independently from the final image dimensions.
- Review media candidates manually for single-game workflows before applying them, or let the extension pick automatically in batch workflows.
- Remove replaced media automatically and scan for unreferenced covers, icons, and backgrounds from the Maintenance tab.
- Export and import extension settings for backup or Playnite reinstalls.
- Export a support diagnostics report with configuration summaries and recent media/audit signals without including API keys.
- Localized settings UI using Playnite's native `Localization/*.xaml` resource system.

## Supported AI providers

Metadata AI can work with several OpenAI-compatible or provider-specific endpoints:

- OpenRouter Free
- Groq free tier
- Google Gemini free tier
- Cerebras free tier
- Mistral AI Free mode
- LM Studio local server
- Ollama local server
- OpenAI
- Claude Anthropic
- OpenRouter paid and manually selected models
- Custom OpenAI-compatible endpoints

Cloud providers generally require their own API key and may have separate billing or quota rules. ChatGPT Plus, Claude Pro, Gemini app subscriptions, and similar consumer plans usually do not include API access for third-party tools.

Free cloud tiers are limited and can be rate-limited or temporarily unavailable. For free usage without external quotas, use LM Studio or Ollama with a local model running on your PC.

## Choosing and configuring an AI provider

The provider list places cloud services with a free tier first, followed by local providers and paid services. Selecting a provider does not immediately replace the current endpoint and model. Click **Apply provider** to load the recommended endpoint and default model for that provider.

New installations start with Groq selected because it is generally the simplest fast cloud option with a free tier. Existing installations keep their currently configured provider.

General setup:

1. Open **Add-ons > Extension settings > Metadata AI > AI**.
2. Select a provider and click **Apply provider**.
3. Use **Open provider page** to create the required API key.
4. Paste the key into **API key**. Local providers do not need one.
5. Keep the suggested model initially, then click **Test provider**.
6. Save the extension settings after a successful test.

| Provider | Payment required | Default model | How to use it |
| --- | --- | --- | --- |
| OpenRouter Free | No, with daily and availability limits | `openrouter/free` | Create a key at [OpenRouter](https://openrouter.ai/settings/keys). The free router chooses an available free model for every request, so speed and consistency can vary. |
| Groq | No, within the free plan limits | `llama-3.1-8b-instant` | Create a key in the [GroqCloud Console](https://console.groq.com/keys). Groq is usually the fastest free cloud option, but request and token limits apply. |
| Google Gemini | No, within the Gemini API free tier | `gemini-2.5-flash` | Create a key in [Google AI Studio](https://aistudio.google.com/app/apikey). A Gemini app subscription is unrelated to Gemini API quotas. |
| Cerebras | No, within the free tier limits | `gpt-oss-120b` | Create a key in [Cerebras Cloud](https://cloud.cerebras.ai/). The free tier provides fast inference with lower rate limits than paid tiers. |
| Mistral AI | No, in Studio Free mode | `mistral-small-latest` | Create a key in [Mistral Studio](https://console.mistral.ai/). Free mode does not require a credit card, but usage and rate limits apply. |
| LM Studio | No; runs on your PC | `local-model` | Install LM Studio, download and load a model, then enable its local server in the Developer tab. Keep LM Studio running while Metadata AI is working. |
| Ollama | No; runs on your PC | `llama3.1` | Install Ollama, download a model with `ollama pull`, and keep the Ollama service running. The model field must match a model shown by `ollama list`. |
| OpenAI | Yes, API billing or credit | `gpt-4.1-mini` | Create an API key in the OpenAI Platform. ChatGPT Plus or Pro does not include API credit. |
| Claude Anthropic | Usually yes, through separate API billing | `claude-sonnet-4-5` | Create a key in Anthropic Console. A claude.ai subscription does not include Anthropic API usage. |
| OpenRouter | Depends on the selected model | `openrouter/auto` | Use this preset for paid models or enter an exact OpenRouter model ID. Use the separate OpenRouter Free preset when no paid routing is desired. |

Provider limits and model availability can change. The model field remains editable so users can replace a discontinued or unavailable model with another model offered by the same provider.

### Provider usage and limits

The AI settings page includes a **Usage and limits** panel. It keeps the last rate-limit information returned by the selected provider and can help distinguish a temporary request/token limit from exhausted credit or an unavailable service.

- OpenRouter is queried through its API-key status endpoint and can report key credit limits, remaining credits, and recent usage. OpenRouter does not expose the exact number of free requests remaining today through that endpoint.
- OpenAI, Claude, Groq, Cerebras, and custom OpenAI-compatible endpoints can expose request or token limits in HTTP response headers. Clicking **Refresh usage** sends one minimal test request and therefore consumes a small amount of quota.
- Normal metadata requests also update the cached limit information, including headers returned with provider errors such as HTTP 429.
- Gemini and Mistral do not provide a portable remaining-quota value that Metadata AI can reliably query with the configured key. Use **Open usage page** to inspect their account dashboards.
- LM Studio and Ollama run locally and have no external API quota.

Displayed values are the last values reported for the selected provider and model. They may represent rate limits rather than account balance, and they can reset over different time windows depending on the provider.

### OpenRouter Free reliability

`openrouter/free` is a dynamic router rather than one fixed model. Two games can therefore be processed by different free models, and a selected model may be busy, slow, or less reliable at following the requested JSON structure.

When OpenRouter Free returns valid metadata but leaves every token required by the active description template empty, Metadata AI retries the request once with a stricter instruction. If the retry is still empty, the extension reports the problem and does not overwrite the existing description. Enabling official store context gives free models more reliable factual material to summarize and normalize.

## Supported media sources

Metadata AI can fetch covers, icons, and backgrounds from:

- The Playnite library integration that owns the game
- Official Steam public assets
- Official Steam screenshots
- PlayStation Store
- Xbox Store
- Epic Store
- SteamGridDB
- RAWG
- MobyGames
- IGDB

Steam public assets, PlayStation Store, Xbox Store, and Epic Store do not require an API key. SteamGridDB, RAWG, MobyGames, and IGDB require their own API credentials.

For IGDB, the extension uses Twitch `Client ID` and `Client Secret` and automatically obtains the access token internally, so users do not need to copy temporary access tokens manually.

When available, Metadata AI can ask the exact library integration that imported the game for its metadata and media. It identifies that integration through the game's `PluginId`, so it never tries an unrelated integration merely because a title looks similar. Individual integrations can be disabled from the Media settings.

Store websites can be region-dependent and may change or block automated reads. If the origin integration or an official store does not return the requested field, Metadata AI skips it and continues with the next enabled source based on your configured priority.

## AI factual context

Metadata AI is designed to normalize and structure metadata, not blindly invent it.

When trusted context is enabled, the extension first asks the Playnite library integration that owns the game. It can then use reliable public store data such as Steam, PlayStation Store, Xbox Store, or Epic Store as additional context and fallback. The AI receives that information as factual context and is instructed to translate, rewrite, summarize, and structure it according to your templates and field rules.

If the plugin cannot find reliable official context, the AI can still generate descriptive text from the game name and existing Playnite metadata, but stricter options can prevent sensitive fields such as developers, publishers, age ratings, and regions from being created when confidence is low.

## Safe workflow, simulation and provenance

On a new installation, the setup assistant asks for the output language, intended workflow, AI provider and enabled fields. Finishing the assistant only saves settings; it never starts a library update. Existing installations are migrated as already configured and are not reset or forced through the assistant. It can be reopened later from **Maintenance** or the **Metadata AI** extension menu.

Use **Preview and choose Metadata AI changes** from a game's context menu, or **Preview and choose metadata changes for selected games or current list** from the extension menu, to generate an in-memory dry run. The preview shows the current and proposed value for every field together with its source, confidence, and a local recommendation. You can select changes per field or per game, choose only the recommended metadata changes, or clear the selection. For a single game, the same window also preloads the best validated cover, icon, and background proposals according to the configured automatic media priorities. Each proposal can be replaced through the regular media picker. Nothing is downloaded or written until you select a media change and apply the final selection.

Recommendations are calculated locally from completeness, provenance, confidence, and possible information loss. They do not make another AI request and are intended as guidance rather than a factual guarantee. Applying from the preview reuses the generated result and only writes the selected metadata and media changes.

Every successful Metadata AI apply operation is added to the change history. History stores affected fields, provenance, and the previous Playnite values. The game context menu provides a history filtered to that game, while the extension menu retains the complete history. Undoing one game removes that entry from the general operation as well. When media is replaced, its old internal file is copied into Metadata AI's private history storage before cleanup so the operation can restore it. The history keeps the latest 20 operations and contains no provider API keys.

Provenance is deliberately explicit about uncertainty. Trusted origin integrations and exact official-store matches are marked separately from existing Playnite context. Values generated only from the game identity are marked as low confidence and should be reviewed before applying.

## Media options

The extension can apply media automatically or show a candidate picker for single-game workflows.

Supported presets include:

- Covers: original, Playnite vertical, square, horizontal/banner.
- Icons: original/transparent, square, rounded, circle.
- Backgrounds: original, Steam hero, Full HD, QHD, 4K.

Selection criteria include:

- Avoid NSFW assets when marked by the source.
- Avoid blurred styles when possible.
- Prefer official assets.
- Avoid console-branded covers when possible.
- Prefer square grids for square icons.
- Prefer backgrounds with or without logos.

Automatic media selection follows the per-kind source priority configured in settings. For example, covers, icons, and backgrounds can each prefer different sources. If a source returns no reliable candidates for a game, the extension continues with the next enabled source.

New installations place the origin library integration in the recommended priority position. When an existing installation is upgraded, Metadata AI appends this source to each saved priority instead of resetting or reordering the user's customized sources.

The selected format and resolution describe the final stored file. Automatic priority can favor configured sources, usable source resolution, or strict quality that skips candidates requiring upscaling after crop. Cover and background crop origins are configurable, and processed JPEG quality can be reduced to save disk space without changing output dimensions.

When Metadata AI replaces a Playnite cover, icon, or background, it removes the previous internal file after the game reference has been updated and only when no other game still references it. The Maintenance tab can also scan existing game storage for unreferenced image files and shows the file count and recoverable space before asking for confirmation.

In manual single-game workflows, the media picker shows candidates grouped by media type. Click a candidate tile to select it, or open the image in your browser before applying the final selection.

## Logos and trailers

Metadata AI focuses primarily on Playnite's standard media fields: covers, icons, and backgrounds. An optional integration can search SteamGridDB logos and save the selected transparent PNG in Extra Metadata Loader's per-game folder. This requires Extra Metadata Loader and is disabled by default; Metadata AI does not register a competing theme-media system.

Extra Metadata Loader remains responsible for exposing logos to compatible themes. Trailer videos, microtrailers, and other theme-specific media are intentionally left to that extension.

## Installation

Download the `.pext` package from releases and install it in Playnite.

For manual installation during development:

1. Build the project in Release mode.
2. Copy the build output into a folder under Playnite's Extensions directory.
3. Restart Playnite.

## Playnite add-on browser

After approval in the Playnite add-on database, Metadata AI can be installed from Playnite's integrated add-on browser.

Direct install URI:

`playnite://playnite/installaddon/MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83`

Web add-on page:

`https://playnite.link/addons.html#MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83`

## Configuration overview

Open Playnite, go to the add-ons/extensions settings, and configure Metadata AI.

The main sections are:

- **AI**: provider, endpoint, model, API key, output language, tone, length, token lengths, fallback providers, and extra instructions.
- **Templates**: saved HTML templates and available description tokens.
- **Media**: media API keys, enabled media sources, source priority, media formats, and selection preferences.
- **Import**: automatic processing for newly imported games.
- **Fields**: per-field generation and apply rules.
- **Rules**: automatic template selection by game type, platform, or source.
- **Maintenance**: reopen the setup assistant, run the library audit and selective media repair, export or import configuration backups, scan for obsolete unreferenced media, and inspect or undo the last 20 Metadata AI operations. API credentials are protected with Windows DPAPI for the current user, both in Playnite's stored settings and in exported backups.

## Description templates

Templates can use HTML because Playnite renders HTML descriptions. This keeps generated descriptions readable instead of appearing as a single block of text.

| Token | Description |
| --- | --- |
| `{short}` | Brief one-block summary of the game. |
| `{synopsis}` | Broader synopsis of the game, usually one or more paragraphs depending on the selected token length. |
| `{premise}` | Narrative or conceptual premise. |
| `{gameplay}` | Main gameplay loop, mechanics, and how the game is played. |
| `{tone}` | Tone or mood, such as serious, humorous, dark, cozy, arcade-like, or competitive. |
| `{setting}` | World, setting, period, or fictional context. |
| `{perspective}` | Camera, viewpoint, or presentation style. |
| `{playModes}` | Known play modes, such as single-player, co-op, online multiplayer, local multiplayer, or PvP. |
| `{estimatedLength}` | Estimated completion or playtime information when it can be inferred. |
| `{similarGames}` | Comparable games or useful reference points. |
| `{notes}` | Extra editorial notes that do not fit another token. |
| `{features}` | Concise feature list generated for the game. |
| `{recommendedFor}` | Type of player or audience the game may appeal to. |
| `{genres}` | Generated genre list. |
| `{tags}` | Generated tag list. |
| `{developers}` | Developer or studio names. |
| `{publishers}` | Publisher names. |
| `{ageRatings}` | Age rating values. |
| `{regions}` | Region values. |
| `{categories}` | Generated category list. |

Example:

```html
<p>{short}</p>
<p>{synopsis}</p>
{features}
```

## Localization

The plugin uses Playnite localization resource dictionaries under `Localization/`.

Translations are stored as locale-specific XAML resource dictionaries. To add or update a translation, copy an existing locale file, rename it to the target locale, and translate the string values while keeping the same resource keys.

Community translation contributions are welcome.

## Support

If you find this project useful and want to support its development, consider buying me a coffee!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/naerian)
