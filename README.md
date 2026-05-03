# civ6-async
https://github.com/nubbylol/civ6-async/blob/master/README.md

Async-feeling Civilization VI for friends who can't all sit at the same machine. Two pieces:

- **A mod** that strips the friction out of hotseat: skips click-to-begin-turn screens, auto-ends turns when no decision is needed, stops the moment you actually have something to do.
- **A helper CLI** that coordinates a single hotseat save across players via any cloud-synced folder (Google Drive, Dropbox, OneDrive). One person plays their turn, helper uploads the save; the next player's helper sees it and pulls it down.

Together they get you something close to Civ's Play-by-Cloud, but you control the storage and it works for friends who can't always be on at the same time.

## TL;DR — get playing in 5 minutes

**Everyone needs**: Civilization VI, a shared cloud folder (Google Drive easiest — anyone can sync to it on Windows / Mac / Steam Deck via rclone), and `civ6-async` from `dist/cli/`.

**The host** (one person, once per game):

1. Run `civ6-async` (double-click on Windows; `chmod +x civ6-async && ./civ6-async` on Linux).
2. First-run wizard: pick a player name → **Create a new game** → enter a game name → enter your shared-folder root (e.g. `G:\My Drive\civ6-async`) → list every player including yourself.
3. Run **More options → Invite (paste link)**, copy the line, paste it in your Discord channel.
4. Optional: **More options → Discord webhook (set/clear)** to get pinged in Discord on every submit.
5. In Civ: **Additional Content → Mods → tick civ6-async**, start a hotseat game with one civ per player, save it once.

**Everyone else**:

1. Run `civ6-async` → pick your player name → **Join an existing game** → paste the path the host sent you.
2. In Civ: tick the **civ6-async** mod the same way.

**Every turn** (anyone):

1. Open `civ6-async`, pick **Whose turn?** to check.
2. If it's yours, pick **Download latest save** (drops it into your Civ saves folder).
3. Open Civ → Load Game → pick the file beginning `civ6-async-…`. Play your turn, save the game.
4. Back in `civ6-async`, pick **Submit my turn**, choose the save you just made.

That's it.

## Install

Download the binary for your OS from `dist/cli/` in this repo:

| OS | File |
|---|---|
| Windows | `dist/cli/win-x64/civ6-async.exe` |
| Linux / Steam Deck | `dist/cli/linux-x64/civ6-async` |

**Windows**: double-click `civ6-async.exe`. The first time you run it, a setup wizard walks you through identity + first game.

**Linux / Steam Deck** (Desktop Mode):
```
chmod +x civ6-async
./civ6-async
```

The `install` step (or the wizard's first action) drops the mod into your Civ 6 Mods folder. Auto-detects Windows, native Linux Aspyr, Steam Proton (default Steam Deck path), Steam Flatpak. Override with `--mods-dir <path>` if it can't find yours.

Then in Civ: **Additional Content → Mods → tick civ6-async**.

## Playing a shared game

After the first-run wizard, the typical loop is:

- `civ6-async game status` — whose turn is it?
- `civ6-async game check` — if it's yours, downloads the latest save into your Civ saves folder
- Play in Civ, save the game
- `civ6-async game submit` — uploads to the shared folder, advances the manifest

Or from the interactive menu (just run `civ6-async` with no args).

**Host creates a game**:
```
civ6-async game init MyGame \
  --shared "G:\My Drive\civ6-async" \
  --players "arin,max,jess" \
  --me arin
```

**Other players join** (host pastes a one-liner from `civ6-async game invite` into Discord):
```
civ6-async game join --shared "G:\My Drive\civ6-async\MyGame"
```

Note: `init` takes the *parent* shared folder; `join` takes the *specific game's* folder. The `invite` command prints the right path for joiners.

## Notifications

`civ6-async game watch` runs alongside Civ and rings the terminal bell when it's your turn or when you've saved a game ready to submit.

For phone-friendly pings, set a Discord webhook on the game (`game webhook` from the menu, or `game webhook <url>` from the CLI). Every submit posts to that channel — works on mobile via Discord's notifications.

## Notes

- Works in single-player and hotseat. Won't load in ranked online multiplayer (Civ 6 anti-cheat blocks unsigned mods there).
- Helper supports multiple concurrent games (`game list` / `game switch`).
- Submit refuses bad submits by default (wrong player, identical to last save, stale local file). `--force` overrides if needed.
- Conflicts with any other mod that replaces `PlayerChange.lua` or `ActionPanel.lua`.

## Building from source

Requires .NET 8 SDK.

```
cd src
./build.ps1            # produces dist/cli/{win-x64,linux-x64}/civ6-async[.exe]
```

`civ6-async --help` lists every command.
