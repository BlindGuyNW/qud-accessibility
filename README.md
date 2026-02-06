# QudAccessibility

A [Caves of Qud](https://www.cavesofqud.com/) mod that adds screen reader support, making the game playable for blind and low-vision players. Works with NVDA and SAPI (Windows built-in) text-to-speech.

## Features

### Automatic Announcements

The mod automatically vocalizes screen content as you navigate:

- **Menus** — Main menu, options, keybinds, help, saves, character sheet tabs, abilities, equipment, inventory
- **Character creation** — All stages (race, class, attributes, mutations, summary) with choices, descriptions, and attribute values
- **Popups and dialogs** — Title and message announced immediately, interrupting other speech
- **Trade and containers** — Screen title, column switching, quantity changes, trade summary
- **Books and terminals** — Page content and options
- **Game summary** — Death/ending screen with full summary

### Keyboard Shortcuts

| Key | Function |
|-----|----------|
| **F2** | Re-read current screen content |
| **F3** / **F4** | Navigate forward/backward through content blocks |
| **PgUp** / **PgDn** | Cycle through nearby objects in current category |
| **Ctrl+PgUp** / **Ctrl+PgDn** | Switch scanner category (Creatures, Items, Corpses, Features) |
| **Home** | Re-announce current scanned object with updated direction |

### Content Blocks (F3/F4)

On the map screen, F3/F4 cycles through:

- **Stats** — HP, AV, DV, and other combat stats
- **Condition** — Food, temperature, active effects
- **Location** — Current zone and time
- **Messages** — Last 5 message log entries
- **Abilities** — Ready/cooldown status of activated abilities

Other screens provide their own context-specific blocks (trade summary, help text, terminal options, etc.).

### Look Mode

Moving the cursor automatically announces what's under it — object name, color, and coordinates. Empty tiles are announced as well. F2 on an object reads its full tooltip and description.

### Nearby Object Scanner

From the map screen, scan for objects around you without entering look mode:

- **Ctrl+PgUp/PgDn** to pick a category and see how many were found
- **PgUp/PgDn** to cycle through them — announces name, distance, and compass direction (e.g., "fire beetle, 3 northeast")

## Installation

1. Download or clone this repository
2. Copy the contents of `src/` to your Caves of Qud mods folder:
   ```
   %USERPROFILE%\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\QudAccessibility\
   ```
   The folder should contain all `.cs` files and `manifest.json`.
3. Launch the game — new mods are enabled by default
4. Verify in the build log (`%USERPROFILE%\AppData\LocalLow\Freehold Games\CavesOfQud\build_log.txt`) that you see "Applying Harmony patches... Success"

### Requirements

- Caves of Qud (Steam or GOG)
- Windows (required for NVDA/SAPI TTS)
- NVDA screen reader (recommended) or Windows built-in SAPI voices

## For Developers

### How It Works

The mod uses [Harmony](https://harmony.pardeike.net/) patches to hook into the game's UI framework. Postfix patches announce content after the game renders it; prefix patches announce before showing (used for popups). All text is stripped of the game's `{{color|markup}}` and sanitized of decorative Unicode before being sent to TTS.

### Building

```
dotnet build Mods.csproj
```

This validates the source against game assemblies but produces a DLL that is **not** deployed. The game compiles `.cs` files at runtime via Roslyn.

### Source Files

| File | Purpose |
|------|---------|
| `Speech.cs` | TTS wrapper — `Say()`, `Interrupt()`, `SayIfNew()` with priority and deduplication |
| `ScreenReader.cs` | F2 re-read, F3/F4 block navigation, look mode, nearby object scanner |
| `ScrollerPatches.cs` | Menu/list vocalization — extracts labels from any scroller element |
| `ChargenPatches.cs` | Character creation screen and choice announcements |
| `TradePatches.cs` | Trade/container screen vocalization and summary blocks |
| `PopupPatches.cs` | Popup and dialog announcements |

### Project Structure

- `src/` — All mod source files (deployed to the game)
- `decompiled/` — ~5400 decompiled game files for reference/IntelliSense (gitignored)

## License

This project is provided as-is for the Caves of Qud community.
