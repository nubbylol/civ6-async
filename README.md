# civ6-async

Async-feeling Civilization VI for friends who can't all sit at the same machine. Two pieces:

- **A mod** that strips the friction out of hotseat: skips click-to-begin-turn screens, auto-ends turns when no decision is needed, stops the moment you actually have something to do.
- **A helper CLI** that coordinates a single hotseat save across players via any cloud-synced folder (Dropbox, Drive, OneDrive). One person plays their turn, helper uploads the save; the next player's helper sees it and pulls it down.

Together they get you something close to Civ's Play-by-Cloud, but you control the storage and it works for friends who can't always be on at the same time.

## Install

Download `civ6-async.exe` (Windows) or `civ6-async` (Linux) from `dist/cli/` in this repo, then run it.

**Windows**: double-click for an interactive menu, or in a terminal:
```
civ6-async.exe install
```

**Linux**: `chmod +x civ6-async && ./civ6-async install`

`install` drops the mod into your Civ 6 Mods folder. Auto-detects Windows / native Linux Aspyr / Steam Proton / Steam Flatpak. Override with `--mods-dir <path>` if needed.

Then in Civ: **Additional Content → Mods → tick civ6-async**.

## Playing a shared game

The first time you launch the helper interactively (double-click), it walks you through setup. Or do it from the terminal:

**Host creates the game**:
```
civ6-async game init MyGame --shared "C:\Users\arin\Dropbox\civ6-async" --players "arin,max" --me arin
```

**Other players join** (host pastes a one-liner from `civ6-async game invite` in Discord):
```
civ6-async game join --shared "C:\Users\max\Dropbox\civ6-async\MyGame"
```

**Each turn**:
- `civ6-async game check` → if it's your turn, downloads the latest save into your Civ saves folder
- Play in Civ, save the game
- `civ6-async game submit` → uploads, advances the manifest, optionally pings Discord

`civ6-async game watch` runs in the foreground and rings the terminal bell whenever it's your turn or you've just saved a game in Civ. Set up a Discord webhook with `game webhook <url>` to ping the channel on every submit.

`civ6-async --help` (or `game --help`) lists every command.

## Notes

- Works in single-player and hotseat. Won't load in ranked online multiplayer (Civ 6 anti-cheat blocks unsigned mods there).
- Helper supports multiple concurrent games (`game list` / `game switch`).
- Conflict detection on submit refuses bad submits (wrong player, identical to last, stale local save). Override with `--force` if you know what you're doing.
- Conflicts with any other mod that replaces `PlayerChange.lua` or `ActionPanel.lua`.

## Building from source

Requires .NET 8 SDK.

```
cd src
./build.ps1            # produces dist/cli/{win-x64,linux-x64}/civ6-async[.exe]
```
