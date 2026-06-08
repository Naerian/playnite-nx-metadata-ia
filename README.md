# Metadata IA

Metadata IA is a Playnite metadata provider extension that uses AI and external media sources to generate consistent game descriptions, metadata lists, links, sorting names, and artwork in the language and structure you choose.

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

Metadata IA can work with several OpenAI-compatible or provider-specific endpoints:

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

Metadata IA can fetch covers, icons, and backgrounds from:

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

## Installation

1. Close Playnite.
2. Build the project in Release mode.
3. Copy the compiled extension files to your Playnite extensions folder.

The included helper script can build and install directly to `C:\Playnite\Extensions\MetaDataIAPlugin`:

```powershell
.\BuildAndInstall.ps1
```

If your Playnite installation is in a different path, adjust the script before running it.

## Build requirements

- Windows
- Playnite installed
- .NET Framework 4.6.2 compatible build environment
- MSBuild

The project references Playnite assemblies from:

```text
C:\Playnite\Playnite.SDK.dll
C:\Playnite\Newtonsoft.Json.dll
```

If your Playnite installation is elsewhere, update the references in `MetaDataIAPlugin.csproj`.

## Configuration overview

Open Playnite, go to the add-ons/extensions settings, and configure Metadata IA.

The main sections are:

- **AI**: provider, endpoint, model, API key, output language, tone, length, token lengths, fallback providers, and extra instructions.
- **Templates**: saved HTML templates and available description tokens.
- **Media**: media API keys, enabled media sources, media formats, and selection preferences.
- **Import**: automatic processing for newly imported games.
- **Fields**: per-field generation and apply rules.
- **Rules**: automatic template selection by game type, platform, or source.

## Description templates

Templates can use HTML because Playnite renders HTML descriptions. This keeps generated descriptions readable instead of appearing as a single block of text.

Common tokens include:

- `{short}`
- `{synopsis}`
- `{premise}`
- `{gameplay}`
- `{tone}`
- `{setting}`
- `{perspective}`
- `{playModes}`
- `{estimatedLength}`
- `{similarGames}`
- `{notes}`
- `{features}`
- `{recommendedFor}`
- `{genres}`
- `{tags}`
- `{developers}`
- `{publishers}`
- `{ageRatings}`
- `{regions}`
- `{categories}`

Example:

```html
<p>{short}</p>
<p>{synopsis}</p>
{features}
```

## Localization

The extension uses Playnite's native localization pattern:

```text
Localization/en_US.xaml
Localization/es_ES.xaml
Localization/pl_PL.xaml
```

XAML uses `DynamicResource` keys and code uses `PlayniteApi.Resources.GetString(...)`.

Currently included languages:

- English
- Spanish
- Polish

## Repository

Recommended repository name:

```text
playnite-nx-metadata-ia
```

## Author

Narian
