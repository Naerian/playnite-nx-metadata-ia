# Metadata AI

Metadata AI is a Playnite metadata provider extension that uses AI, official store context, and external media sources to generate or normalize consistent game descriptions, metadata lists, links, sorting names, covers, icons, and backgrounds in the language and structure you choose.

It is designed for users who want a Playnite library with a unified editorial style instead of mixed descriptions and tags from multiple stores.

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
- Use stricter factual guards for companies, age ratings, and regions to reduce invented metadata.
- Automatically process newly imported games after library updates.
- Generate media assets from multiple configurable sources.
- Configure media source priority separately for covers, icons, and backgrounds.
- Review media candidates manually for single-game workflows before applying them, or let the extension pick automatically in batch workflows.
- Export and import extension settings for backup or Playnite reinstalls.
- Localized settings UI using Playnite's native `Localization/*.xaml` resource system.

## Supported AI providers

Metadata AI can work with several OpenAI-compatible or provider-specific endpoints:

- OpenAI
- Google Gemini
- Claude Anthropic
- OpenRouter
- Groq
- LM Studio local server
- Ollama local server
- Custom OpenAI-compatible endpoints

Cloud providers generally require their own API key and may have separate billing or quota rules. ChatGPT Plus, Claude Pro, Gemini app subscriptions, and similar consumer plans usually do not include API access for third-party tools.

For free local usage, use LM Studio or Ollama with a local model running on your PC.

## Supported media sources

Metadata AI can fetch covers, icons, and backgrounds from:

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

Store websites can be region-dependent and may change or block automated reads. If an official store does not return a reliable match, Metadata AI skips it and continues with the next enabled source based on your configured priority.

## AI factual context

Metadata AI is designed to normalize and structure metadata, not blindly invent it.

When official context is enabled, the extension first tries to read reliable store data such as Steam, PlayStation Store, Xbox Store, or Epic Store. The AI receives that information as factual context and is instructed to translate, rewrite, summarize, and structure it according to your templates and field rules.

If the plugin cannot find reliable official context, the AI can still generate descriptive text from the game name and existing Playnite metadata, but stricter options can prevent sensitive fields such as developers, publishers, age ratings, and regions from being created when confidence is low.

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

In manual single-game workflows, the media picker shows candidates grouped by media type. Click a candidate tile to select it, or open the image in your browser before applying the final selection.

## Logos and trailers

Metadata AI focuses on Playnite's standard media fields: covers, icons, and backgrounds.

For clear logos, trailer videos, microtrailers, and theme-specific extra media, use Extra Metadata Loader together with a compatible Playnite theme. Many custom themes already support Extra Metadata Loader controls, so it is better to let that extension handle those assets instead of duplicating a separate logo system here.

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
- **Maintenance**: export or import the plugin configuration as a backup.

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
