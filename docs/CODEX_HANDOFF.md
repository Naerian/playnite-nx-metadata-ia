# Codex Handoff: Metadata AI

This file is a continuity note for future Codex sessions after a machine reinstall
or context reset. It captures the project state, release workflow, local paths and
important decisions for the Playnite extension.

## Project

- Extension name: Metadata AI
- Repository: https://github.com/Naerian/playnite-nx-metadata-ia
- Add-on id: `MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83`
- Author shown in Playnite: `Narian`
- Extension type: `MetadataProvider`
- Main local checkout: `C:\Proyectos\playnite-nx-metadata-ia`
- Playnite install path used during development: `C:\Playnite`
- Installed extension folder:
  `C:\Playnite\Extensions\MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83`

## Current Release State

- Latest released version: `1.4.13`
- Latest release tag: `v1.4.13`
- Latest release commit: see tag `v1.4.13`
- Current package name:
  `MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83_1_4_13.pext`
- Release page:
  https://github.com/Naerian/playnite-nx-metadata-ia/releases/tag/v1.4.13
- Public package SHA-256 verified for v1.4.13:
  `6D67EFB2249E96D08F31289CC5FECC20207E8A0A65893F33F49D5D4AE474441A`
- UI reference for other plugins: `docs/SETTINGS-UI-GUIDE.md`
- Settings design system (Figma):
  https://www.figma.com/design/0ilhUvo6xBoEkEldkdP3Wh/Narian-Plugins-%E2%80%94-Settings-Design-System

When continuing work, first verify the current repository state instead of
assuming this file is still current.

## What The Extension Does

Metadata AI helps normalize and enrich Playnite libraries using configurable AI
providers, official/source metadata context and media sources. It is intended for
mixed libraries imported from stores, emulators, launchers and manual entries
where metadata can become inconsistent.

Main capabilities currently implemented:

- AI-generated or normalized descriptions using user templates and tokens.
- Configurable output language, tone, field lengths and strict factual behavior.
- Per-field rules: generate/apply/overwrite/append/fill-only.
- Tags, genres, categories, features, developers, publishers, ratings, regions,
  links, release date, series and sorting name.
- Local vocabulary memory for consistent terms by language.
- Official/source context support so AI can normalize from trusted data instead
  of inventing freely.
- Multiple AI providers: OpenAI, Gemini, Claude, OpenRouter, Groq, LM Studio,
  Ollama, custom OpenAI-compatible endpoints and other free/local options added
  during development.
- Media search and selection for covers, icons, backgrounds and optional logos
  for Extra Metadata Loader.
- Media sources include Steam CDN public assets, IGN, SteamGridDB, RAWG.io, Wallhaven,
  ScreenScraper, Giant Bomb, MobyGames, IGDB, PS Store, Xbox and source integration
  context. Wallhaven is an optional SFW, 16:9 background-only fallback and does not
  require an API key. ScreenScraper needs both user and developer API credentials.
- Automatic media selection with source priority, quality/resolution preferences,
  crop/output settings and fallback behavior.
- Manual media picker with candidate selection, preview, browser links, editable
  search terms, a temporary per-media format/resolution selector, and validated
  direct HTTP/HTTPS image URLs that reuse the normal crop and processing pipeline.
- First-run setup assistant.
- Settings export/import under Maintenance.
- Dry-run/preview window for metadata changes.
- History and provenance windows, with selected-game filtering.
- Library audit with selective repair for metadata and media issues.
- Media cleanup and stale file maintenance helpers.
- Support diagnostics export for sharing configuration summaries and recent
  media/audit signals without exposing API keys.
- Localizations in `Localization/*.xaml`; all locale files should contain the
  same resource keys.

## Important Product Decisions

- The extension should be framed as normalization, translation, restructuring,
  curation and gap filling. It should not claim to replace validated sources when
  those already provide accurate metadata.
- The AI should avoid inventing factual data. Names of companies, URLs and
  official ratings should be preserved. Tags/categories/features should be
  translated to the configured output language.
- Strict behavior for developers, publishers, age ratings and regions should be
  the default when accuracy is uncertain.
- Do not add trailer support unless there is a clear reason. Logos are supported
  only as optional assets for Extra Metadata Loader.
- The Fullscreen experience exists but should stay conservative because complex
  review flows are much more usable in Desktop.
- Settings UI direction (2026-08): **plugin-owned chrome** with fixed structure
  tokens and user-selectable **color presets** (Midnight, Paper, OLED, Ocean,
  Ember). Do not chase every Playnite desktop theme for contrast. Source of
  truth: `docs/SETTINGS-UI-GUIDE.md` + the Figma file above. Optional later:
  a “Follow Playnite” preset. Until migration lands, shipped XAML may still use
  Playnite brushes; new work should target the preset system.
- If a repair/action fails because of quota, cancellation, no candidates or
  provider errors, the audit issue should remain visible.

## Local Files To Treat Carefully

- `package.ps1` is the supported manual build/pack script. It builds Release,
  stages a clean package in `%TEMP%`, and writes the `.pext` into
  `dist/<version>/` (for example `dist/1.4.9/`).
- At the end of a coding task, always run `package.ps1` so `dist/<version>/`
  has a fresh `.pext` for local install/test.
- Do **not** bump `extension.yaml` / AssemblyInfo to a new version just to
  package. Keep packaging the version already declared. Only bump when the user
  asks for a release **and** the previous version already has a GitHub release
  (or they explicitly request a new version). If the current declared version
  has no release uploaded yet, keep iterating and packaging under that same
  version.
- Do not restore old install helpers such as `BuildAndInstall.ps1`; packaging
  must not install into Playnite unless the user explicitly asks.
- `docs/PLAYNITE-SDK.md` is a general Playnite SDK reference. Do not restore
  CSM leftover files (`PLAYNITE-INTEGRATION.md`, `PLAYNITE-ADDON-SUBMISSION.md`).
- Never commit API keys, provider secrets, local Playnite settings or generated
  user data.
- `bin/`, `obj/`, `dist/`, `.vs/` and package outputs are ignored and should
  remain untracked.
- `media/icon.png` is intentionally tracked and used by `extension.yaml` and the
  Playnite add-on browser manifest.

## Build

This project is a classic .NET Framework Playnite plugin project. Do not use
`dotnet build` as the main build command.

Preferred build command:

```powershell
C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe C:\Proyectos\playnite-nx-metadata-ia\MetaDataIAPlugin.sln /p:Configuration=Release
```

Expected output:

```text
C:\Proyectos\playnite-nx-metadata-ia\bin\Release\MetaDataIAPlugin.dll
```

Some .NET Framework reference/architecture warnings have been seen before and
are not automatically release blockers. Real compile errors are blockers.

## Package

Use `package.ps1` for a manual Release build and `.pext`:

```powershell
.\package.ps1
# or: .\package.ps1 -Version 1.4.6
```

It packs from a clean staging directory in `%TEMP%` (never leave a `stage`
folder inside `dist/`). The package includes the extension output and required
assets, but not helper scripts or unrelated runtime files such as `mscorlib.dll`
or `norm*.nlp`. After packing, the `.pext` is under `dist/<version>/`
(for example `dist/1.4.9/MetaDataIAPlugin_..._1_4_9.pext`). Other version
folders in `dist/` are left intact.

Verify installer manifest:

```powershell
C:\Playnite\Toolbox.exe verify Installer C:\Proyectos\playnite-nx-metadata-ia\installer.yaml
```

## Install Locally

Before installing into `C:\Playnite`, always check both Playnite processes:

```powershell
Get-Process Playnite.DesktopApp,Playnite.FullscreenApp -ErrorAction SilentlyContinue
```

If either process is running, do not overwrite the installed DLL. Ask the user to
close Playnite first or stop before copying.

When Playnite is closed, copy the Release output and required folders into:

```text
C:\Playnite\Extensions\MetaDataIAPlugin_2f42c46c-9e3f-48cb-99b6-7f41f12d9b83
```

Keep the install scoped to this extension folder. Do not delete broad paths.

## Version And Release Checklist

When the user asks for "sube a git y genera release/nueva version", perform the
complete workflow unless they explicitly say otherwise:

1. Check `git status --short`.
2. Decide the next version number.
3. Update all version sources:
   - `extension.yaml`
   - `Properties\AssemblyInfo.cs`
   - `installer.yaml`
   - About/version display if it is not derived from assembly metadata.
4. Build Release with classic MSBuild.
5. Package a clean `.pext`.
6. Verify `installer.yaml` with Playnite Toolbox.
7. Commit only intended files.
8. Push `main`.
9. Create and push the tag.
10. Create the GitHub release and upload the `.pext`.
11. Download or inspect the public release asset and compare SHA-256 against the
    local package.
12. Verify the raw or API-visible `installer.yaml` references the new package.

Write all public release text in English. This includes the GitHub release body,
release notes and the `installer.yaml` changelog. Keep the standard release title
format `Metadata AI vX.Y.Z`. Use clear notes focused on user-visible features and
changes, even when the conversation and development work are in Spanish.

## Git Commands Usually Used

```powershell
git status --short
git add <intended-files-only>
git commit -m "Release Metadata AI x.y.z"
git push origin main
git tag vX.Y.Z
git push origin vX.Y.Z
gh release create vX.Y.Z <path-to-pext> --title "Metadata AI vX.Y.Z" --notes "<release notes>"
```

For documentation-only commits, use a specific commit message such as:

```powershell
git commit -m "Add Codex handoff documentation"
```

## Useful Validation Checks

Check all localization files contain the same keys:

```powershell
$files = Get-ChildItem C:\Proyectos\playnite-nx-metadata-ia\Localization\*.xaml
$keySets = @{}
foreach ($file in $files) {
    $keys = Select-String -Path $file.FullName -Pattern 'x:Key="([^"]+)"' |
        ForEach-Object { $_.Matches[0].Groups[1].Value } |
        Sort-Object -Unique
    $keySets[$file.Name] = $keys
}
$baseline = $keySets['en_US.xaml']
foreach ($name in $keySets.Keys) {
    $missing = $baseline | Where-Object { $keySets[$name] -notcontains $_ }
    $extra = $keySets[$name] | Where-Object { $baseline -notcontains $_ }
    if ($missing -or $extra) {
        "$name missing=$($missing.Count) extra=$($extra.Count)"
    }
}
```

Check package/asset hash:

```powershell
Get-FileHash C:\Proyectos\playnite-nx-metadata-ia\dist\<version>\<package>.pext -Algorithm SHA256
```

## Current Follow-Up Ideas

These are not mandatory, but they are useful next places to improve the plugin:

- Keep refining the audit UX so users understand exactly what is broken, why it
  matters and what action will repair it.
- Improve provider quota/status feedback where APIs expose usable limits.
- Continue hardening factual metadata generation, especially series and sorting
  names.
- Improve media candidate ranking by exact title matching, source confidence,
  platform/source context, resolution and broken URL detection.
- Consider lightweight telemetry-free diagnostics export for GitHub issues:
  plugin version, Playnite version, settings summary without secrets, last error
  category and relevant logs.
- Expand wiki pages and screenshots for first-time users.

## After Reinstalling The PC

Recommended recovery flow:

1. Install Playnite and Codex CLI.
2. Clone the repo:

   ```powershell
   git clone https://github.com/Naerian/playnite-nx-metadata-ia.git C:\Proyectos\playnite-nx-metadata-ia
   ```

3. Restore or reinstall Playnite at `C:\Playnite` if that path is still desired.
4. Restore Metadata AI settings from the exported Maintenance backup.
5. Re-enter API keys if Windows/user-machine protected secrets do not survive
   the reinstall.
6. Ask Codex to read this file before continuing:

   ```text
   Continue Metadata AI. Read docs/CODEX_HANDOFF.md first.
   ```
