# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A Caves of Qud mod ("QudAccessibility") that adds screen reader support via Harmony patches. It vocalizes menus, character creation, and popups through the game's existing NVDA/SAPI TTS infrastructure.

## Build & Deploy

**Build (compile-time checking only):**
```
dotnet build Mods.csproj
```
This validates the source against game assemblies but produces a DLL we never deploy. The game compiles `.cs` files itself at runtime via Roslyn.

**Deploy to game:**
```
cp src/*.cs src/manifest.json "$USERPROFILE/AppData/LocalLow/Freehold Games/CavesOfQud/Mods/QudAccessibility/"
```
The game discovers the mod folder, compiles the `.cs` files, detects `[HarmonyPatch]` attributes, and calls `Harmony.PatchAll()` automatically. New mods are enabled by default.

**Verify after launch:** Check `%USERPROFILE%\AppData\LocalLow\Freehold Games\CavesOfQud\build_log.txt` for "Applying Harmony patches... Success".

## Architecture

All mod source lives in `src/`. The `decompiled/` directory contains ~5400 decompiled game `.cs` files for reference/IntelliSense only (excluded from build via `EnableDefaultCompileItems=false`).

### Source Files

- **Speech.cs** — Static TTS wrapper. `Say()`, `Interrupt()`, `SayIfNew()`. Strips `{{color|...}}` markup via `ConsoleLib.Console.ColorUtility.StripFormatting()` before speaking. Lazily creates the `WindowsTTS` MonoBehaviour on first use (the game only creates it when its own accessibility manager speaks).
- **ScreenReader.cs** — MonoBehaviour for F2 re-read, F3/F4 block navigation (with dynamic provider pattern), look mode cursor tracking, and pick target mode tracking. Dispatches to `NearbyScanner` for map-screen scanning. Contains `BuildMapBlocks()` default provider (stats, condition, location, messages, abilities).
- **NearbyScanner.cs** — Static helper for the nearby object scanner (PgUp/PgDn/Home on the map screen). Scans the current zone by category (Hostile, Friendly, Items, Corpses, Features), sorts by distance, and announces results. Also provides `GetDoorStateSuffix()` and `GetCompassDirection()` used by `ScreenReader`.
- **ScrollerPatches.cs** — Core scroller vocalization. Contains `GetElementLabel()` (extracts readable text from any `FrameworkDataElement` subclass) and postfixes for `FrameworkScroller.UpdateSelection()`, `PaperdollScroller.UpdateSelection()`, plus patches for main menu, keybinds, abilities, saves, and pick items screens.
- **ChargenPatches.cs** — Harmony postfixes on `EmbarkBuilderModuleWindowDescriptor.show()` (announces screen title) and `HorizontalScroller.UpdateSelection()` (speaks choice title + description).
- **TradePatches.cs** — Trade/container screen vocalization: title announcements, column switching, quantity tracking, F2 summary. Contains `BuildTradeBlocks()` provider for F3/F4 (trade summary, commands).
- **PopupPatches.cs** — Harmony prefixes on `Popup.WaitNewPopupMessage`, `NewPopupMessageAsync`, `Show`, `ShowAsync` (speaks popup title + message).
- **HelpScreenPatches.cs** — Help screen: title announcement, content scrolling feedback, highlight tracking, and `BuildHelpBlocks()` F3/F4 provider.
- **CharacterSheetPatches.cs** — Character sheet (StatusScreensScreen): tab name announcement and `BuildCharSheetBlocks()` F3/F4 provider for stats/points.
- **TerminalPatches.cs** — Cybernetics/generic terminal: body + first option announcement and `BuildTerminalBlocks()` F3/F4 provider.
- **GameSummaryPatches.cs** — Game summary (death/ending) screen: name/details announcement and `BuildGameSummaryBlocks()` F3/F4 provider.
- **OptionsScreenPatches.cs** — Options screen: title announcement on first show and highlight tracking for F2 content.

### Key Game APIs

| Class | Namespace | What We Use |
|-------|-----------|-------------|
| `WindowsTTS` | (global) | `.Speak()`, `.Stop()`, `.instance` — auto-detects NVDA, falls back to SAPI |
| `ColorUtility` | `ConsoleLib.Console` | `.StripFormatting()` — removes `{{color\|...}}` markup |
| `FrameworkScroller` | `XRL.UI.Framework` | Base scroller; `.scrollContext.data`, `.selectedPosition`, `.UpdateSelection()` |
| `HorizontalScroller` | `XRL.UI.Framework` | Chargen scroller; extends `FrameworkScroller`, has `.descriptionText` |
| `FrameworkDataElement` | `XRL.UI.Framework` | Base data class with `.Id`, `.Description` |
| `ChoiceWithColorIcon` | `XRL.UI.Framework` | Extends above; adds `.Title` — used in chargen choices |
| `MainMenuOptionData` | `XRL.UI.Framework` | Extends above; adds `.Text`, `.Command` — used in main menu |
| `EmbarkBuilderModuleWindowDescriptor` | `XRL.CharacterBuilds` | `.show()`, `.title`, `.name`, `.window.GetBreadcrumb()` |
| `MainMenu` | `Qud.UI` | `.Show()`, `.leftScroller`, `.rightScroller` |
| `Popup` | `XRL.UI` | `.Show()`, `.ShowAsync()`, `.WaitNewPopupMessage()`, `.NewPopupMessageAsync()` |

### Block Provider Pattern (F3/F4 Navigation)

`ScreenReader` supports navigable content blocks via F3 (next) / F4 (previous). Blocks are sourced through a priority chain:

1. **Screen-specific provider** (`_blockProvider`) — set via `SetBlockProvider(Func<List<ContentBlock>>)` when a screen opens. If the provider returns `null`, it auto-clears and falls through.
2. **Static blocks** (`_blocks`) — set via `SetBlocks()`. Used by chargen summary where content is fixed at screen open time.
3. **Default provider** (`_defaultProvider`) — set once at init to `BuildMapBlocks()`. Fallback for the map screen.

Providers are self-validating: they check whether their screen is still active and return `null` if not, which auto-clears them. No explicit cleanup or screen-close patching is needed.

**Adding a new screen provider:**
1. Write a `BuildXxxBlocks()` method that returns `List<ContentBlock>` (or `null` if the screen is inactive)
2. Call `ScreenReader.SetBlockProvider(BuildXxxBlocks)` in the screen's open/show patch
3. The provider auto-clears when F3/F4 is pressed after the screen closes

**Existing providers:**
- `BuildMapBlocks()` in `ScreenReader.cs` — Stats, Condition, Location, Messages, Abilities
- `BuildTradeBlocks()` in `TradePatches.cs` — Trade Summary, Commands
- `BuildHelpBlocks()` in `HelpScreenPatches.cs` — Current help topic text
- `BuildCharSheetBlocks()` in `CharacterSheetPatches.cs` — Character/skill points
- `BuildTerminalBlocks()` in `TerminalPatches.cs` — Terminal body + options
- `BuildGameSummaryBlocks()` in `GameSummaryPatches.cs` — Name, Details
- Chargen summary uses static `SetBlocks()` in `ChargenPatches.cs`

Blocks are regenerated fresh on each F3/F4 press, so data is always current.

### Speech Deduplication

`Speech.SayIfNew()` tracks the last spoken string to avoid re-announcing the same element every frame. `FrameworkScroller.Update()` calls `UpdateSelection()` whenever `selectedPosition` changes, so our postfix fires on actual navigation only — but `SayIfNew` is a safety net.

### HorizontalScroller Double-Fire

`HorizontalScroller.UpdateSelection()` calls `base.UpdateSelection()`, so both the chargen patch and the base scroller patch fire. The chargen patch speaks title+description; the base patch speaks title only. `SayIfNew` deduplicates since the chargen patch runs first (postfix on the override, before the base postfix).

## Conventions

- Namespace: `QudAccessibility`
- All patches use `[HarmonyPatch]` attribute-based declarations
- Postfixes for "announce what just appeared"; prefixes for "announce before showing"
- Always strip formatting before TTS — game text is full of `{{C|colored}}` markup
- Beware `ColorUtility` ambiguity: `UnityEngine.ColorUtility` vs `ConsoleLib.Console.ColorUtility` — always fully qualify the latter
- Beware `GameObject` ambiguity: `UnityEngine.GameObject` vs `XRL.World.GameObject` — when both `using` directives are present, fully qualify (e.g. `new UnityEngine.GameObject(...)`, `XRL.World.GameObject player`)
- Beware `Physics` ambiguity: `UnityEngine.Physics` vs `XRL.World.Parts.Physics` — fully qualify as `XRL.World.Parts.Physics`
