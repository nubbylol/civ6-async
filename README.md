# civ6-async

Civilization VI hotseat without the friction. Skips the click-to-begin-turn screens between players and auto-ends every turn the moment no decision is needed. Hotseat ends up feeling like async play, where you only stop when the game actually wants you.

## What it does

- **Skips the "Click to begin your turn" popup** at hotseat handoff (unless that player has a password set).
- **Skips the "Please Wait" popup** during engine handoff.
- **Auto-ends turns** when there's nothing for you to do: no units waiting on orders, no choices to make, no notifications open.
- **Knows when to stop**: anything that calls for your attention (a freshly-produced unit, a met civ, a discovered wonder, a research or civic prompt) pauses the auto-advance until you handle it.

## Install

The mod ships as a small command-line tool that installs the mod files into Civilization VI's Mods folder for you. The tool is a single self-contained binary, no .NET runtime needed.

### Windows

Download `civ6-async.exe`, then in any terminal:

```
civ6-async.exe install
```

### Linux

Download the `civ6-async` binary, make it executable, then:

```
chmod +x civ6-async
./civ6-async install
```

Works against the native Aspyr Linux build of Civ 6, the Windows build via Steam Proton, and the Steam Flatpak. The tool auto-detects which install you have.

### Then enable the mod in-game

Launch Civilization VI → **Additional Content → Mods** → tick **civ6-async** → confirm.

### Other commands

| Command | What it does |
|---|---|
| `civ6-async install`   | Copy the mod files into Civ's Mods folder. Run again to update. |
| `civ6-async uninstall` | Remove them. |
| `civ6-async status`    | One-line summary of whether the mod is installed. |
| `civ6-async health`    | Detailed report: paths detected, file integrity, write permissions. |

Add `--mods-dir <path>` to override auto-detection. Add `-y` to skip confirmation prompts.

## Building from source

Requires .NET 8 SDK.

```
cd src
./build.ps1            # produces dist/cli/{win-x64,linux-x64}/civ6-async[.exe]
```

## Notes

- Single-player and hotseat. Won't load in ranked online multiplayer (Civ 6 anti-cheat blocks unsigned mods there).
- Conflicts with other mods that override `PlayerChange.lua` or `ActionPanel.lua`.
