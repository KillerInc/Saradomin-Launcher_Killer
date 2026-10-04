# Killer Launcher Changelog

This file tracks **Killer launcher changes only**. Any upstream Saradomin changelog or release notes are intentionally preserved separately.

## v1.6.8 — 2026-10-04

- Added a separate **Font Scale** control beside UI Scale for per-display font tuning.
- UI Scale remains unchanged and continues to control the whole client/window the existing way.
- Font Scale is passed only to the Killer RT4 font generator with `-DkillerFontScale`.
- Default Font Scale is `1.0`; supported range is `0.5` to `2.0` in 0.1 steps.

## v1.6.7 — 2026-10-04

- Restored fractional **UI Scale** steps in 0.1 increments (1.0, 1.1, 1.2, 1.5, 1.7, etc.).
- Removed the separate **Text Scale** control and setting.
- UI Scale remains the single user-facing scale value.
- Killer RT4 font generation now follows the same UI Scale automatically.

## v1.6.6 — 2026-10-04

- Experimental intermediate build; superseded by v1.6.7.
- Updated release notes for vector-generated RT4 fonts.
- Continues to use `KillerInc/RT4-Client-Killer` releases rather than the old upstream GitLab client artifact.

## v1.6.5 — 2026-10-04

- Split **Text Scale** from **UI Scale** after testing showed that coupling them caused oversized overlapping text.
- Added an independent Text Scale control.

## v1.6.4 — 2026-10-04

- Switched client downloads and SHA checking to `KillerInc/RT4-Client-Killer`.
- Added the first launcher-to-Killer-RT4 text-scale argument.
- Added ZIP packaging to the Windows launcher release workflow.

## v1.6.3 — 2026-10-04

- Fixed Windows file/folder picker paths so spaces stay as normal spaces instead of being stored as `%20`.
- Applied the same URI-path fix to Java executable browsing.

## v1.6.2 — 2026-10-04

- Added **Move Game Location** to Settings.
- Added portable/movable 2009Scape data locations.
- Existing installs remain where they are until explicitly moved.
- Fresh installs can use a `2009scape` data folder beside the launcher.
- Bundled Java path follows moved game data automatically.
- Portable paths are stored relative to the launcher when appropriate.
- Added automatic correction of stale bundled Java paths after launcher/game moves.
- Added manual Windows launcher release workflow.

---
Going forward, new Killer launcher changes should be added here without modifying or replacing upstream changelogs.
