# Changelog

## 1.4.16 — unpublished
- System requirements are copied from the store, then localized in a dedicated AI pass (labels and boilerplate only). The description HTML is rebuilt after that pass. Invalid or failed localization leaves a placeholder instead of English store text.

## 1.4.15 — 2026-08-31
- System requirement tokens {min_sys_req} / {recommended_sys_req} filled from Steam pc_requirements (not AI).
- Hardened Steam AppID resolve, store fetch (cookies, success checks, language/country, TLS), and false batch-cancel on timeouts.
- System requirements render as HTML lists with bold labels; empty placeholders follow the output language.
- Plugin windows (audit, history, simulation, media picker, etc.) use Playnite chrome and the selected appearance preset.
- Localized remaining Sources-tab UI, logo/IGDB errors, and provenance details; removed the unused custom review window.

