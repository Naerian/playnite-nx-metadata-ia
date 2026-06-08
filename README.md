# Metadata AI

Metadata AI is a Playnite metadata provider extension that uses AI and external media sources to generate consistent game descriptions, metadata lists, links, sorting names, and artwork in the language and structure you choose.

It is designed for users who want a Playnite library with a unified editorial style instead of mixed descriptions and tags from multiple stores.

## Features

- Generate game descriptions with customizable HTML templates.
- Use reusable templates for short, medium, long, RPG, adventure, indie, emulation, platform-specific, source-specific, or rule-based descriptions.
- Generate and apply genres, tags, features, developers, publishers, age ratings, regions, categories, links, and sorting names.
- Control each field independently: skip, fill only empty fields, append without deleting, or overwrite.
- Set per-field maximum item counts for lists such as tags, genres, categories, features, and links.
- Prefer existing Playnite terms to keep tags, genres, features, and categories consistent.
- Use blacklist rules and prefixes for generated tags and categories.
- Generate metadata in a selected output language from a dropdown.
- Automatically process newly imported games after library updates.
- Generate media assets from multiple configurable sources.
- Review media candidates manually for single-game workflows before applying them.
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
- SteamGridDB
- RAWG
- MobyGames
- IGDB

Steam public assets do not require an API key. SteamGridDB, RAWG, MobyGames, and IGDB require their own API credentials.

For IGDB, the extension uses Twitch `Client ID` and `Client Secret` and automatically obtains the access token internally, so users do not need to copy temporary access tokens manually.

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

## Logos and trailers

Metadata AI focuses on Playnite's standard media fields: covers, icons, and backgrounds.

For clear logos, trailer videos, microtrailers, and theme-specific extra media, use Extra Metadata Loader together with a compatible Playnite theme. Many custom themes already support Extra Metadata Loader controls, so it is better to let that extension handle those assets instead of duplicating a separate logo system here.

## Installation

Download the `.pext` package from releases and install it in Playnite.

For manual installation during development:

1. Build the project in Release mode.
2. Copy the build output into a folder under Playnite's Extensions directory.
3. Restart Playnite.

## Configuration overview

Open Playnite, go to the add-ons/extensions settings, and configure Metadata AI.

The main sections are:

- **AI**: provider, endpoint, model, API key, output language, tone, length, token lengths, fallback providers, and extra instructions.
- **Templates**: saved HTML templates and available description tokens.
- **Media**: media API keys, enabled media sources, media formats, and selection preferences.
- **Import**: automatic processing for newly imported games.
- **Fields**: per-field generation and apply rules.
- **Rules**: automatic template selection by game type, platform, or source.

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
