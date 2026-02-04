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
- **MainMenuPatches.cs** — Harmony postfixes on `MainMenu.Show()` (announces "Main Menu" + first option) and `FrameworkScroller.UpdateSelection()` (speaks any highlighted element in any scroller).
- **ChargenPatches.cs** — Harmony postfixes on `EmbarkBuilderModuleWindowDescriptor.show()` (announces screen title) and `HorizontalScroller.UpdateSelection()` (speaks choice title + description).
- **PopupPatches.cs** — Harmony prefixes on `Popup.WaitNewPopupMessage`, `NewPopupMessageAsync`, `Show`, `ShowAsync` (speaks popup title + message).

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
