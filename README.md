# civ6-async
https://github.com/nubbylol/civ6-async/blob/master/README.md

Async-feeling Civilization VI for friends who can't all sit at the same machine. Two pieces:

- **A mod** that strips the friction out of hotseat: skips click-to-begin-turn screens, auto-ends turns when no decision is needed, stops the moment you actually have something to do.
- **A helper CLI** that coordinates a single hotseat save across players via either **Dropbox** (direct API — recommended) or any **local cloud-synced folder** (Drive / Dropbox desktop / OneDrive / Syncthing / NAS). One person plays their turn, helper uploads the save; the next player's helper sees it and pulls it down.

Together they get you something close to Civ's Play-by-Cloud, but you control the storage and it works for friends who can't always be on at the same time.

## TL;DR — joining a game

**You need**: Civilization VI, and either the invite zip your host sent you OR the join command they pasted in Discord.

**Setup, the easy way (host sent you a zip)**:

1. Unzip — you get `civ6-async[.exe]` and `config.json` next to each other.
2. Double-click the binary (or `./civ6-async` on Linux / Steam Deck).
3. Wizard skips straight to **"Which player are you?"** — pick yourself.
4. Done. Mod auto-installs and auto-enables.

**Setup, the manual way (host sent you a join command)**:

1. Download the binary for your OS from `dist/cli/` in this repo (`.exe` for Windows, no-extension binary for Linux / Steam Deck).
2. Run it (double-click or `./civ6-async`).
3. First-run wizard: pick a player name → **Join an existing game** → choose Dropbox or Local-folder → paste what the host sent you.

**Every turn**:

1. Run `civ6-async` → pick **Sync**.
2. If it's your turn, the latest save is downloaded automatically; open the file beginning `civ6-async-…` in Civ.
3. Play your turn, save the game in Civ.
4. Sync auto-submits. Move on with your day.

That's it. While Sync is running:
- A countdown shows when the next check happens (every 60s — only when waiting on someone else; zero polling on your own turn).
- New events (your turn / submit confirmations / Discord pings) print as they happen.

## Install

Download the binary for your OS from `dist/cli/` in this repo:

| OS | File |
|---|---|
| Windows | `dist/cli/win-x64/civ6-async.exe` |
| Linux / Steam Deck | `dist/cli/linux-x64/civ6-async` |

Run it. The first launch is a wizard. Mod install + enable happens automatically once you've created or joined a game.

## Hosting a game

If you're the one running the server-side (creating the game), you have two storage options:

### Dropbox (recommended)

Direct HTTPS — sub-second propagation, no waiting on a desktop sync client.

1. Go to https://www.dropbox.com/developers/apps and create an app:
   - Choose **Scoped access** → **App folder**
   - Permissions tab: tick `files.content.read`, `files.content.write`, `files.metadata.read`, hit Submit
   - Settings tab: scroll to **Generated access token** → Generate, copy
2. Run `civ6-async` → **More options → ... → game init** OR from the wizard pick **Create a new game** → **Dropbox**.
3. Paste the token when asked.
4. Files end up in your Dropbox at `/Apps/<your-app-name>/civ6-async/<game-name>/` — visible in dropbox.com or your Dropbox desktop client. The token only grants access to that one App folder.

### Local folder (Drive / Dropbox-desktop / Syncthing / etc.)

Use whatever cloud-sync layer you already have. Helper just reads and writes a path.

```
civ6-async game init MyGame \
  --shared "G:\My Drive\civ6-async" \
  --players "arin,max,jess" \
  --me arin
```

Note: Drive's desktop sync has been observed to take *hours* to propagate small writes — Dropbox API or Syncthing are dramatically faster. If you must use Drive, expect lag.

## Inviting friends

Two options:

**Send a zip (zero-typing for friends)**:

```
civ6-async game pack
```

…produces `<GameName>-invite.zip` containing the binary + a stripped `config.json`. Friends unzip, double-click, the wizard fast-paths to "Which player are you?". Works for both Dropbox and local-folder games. *Caveat: for Dropbox games, the zip contains your access token. Don't post it publicly.*

**Send a join command**:

```
civ6-async game invite
```

Prints a one-line `civ6-async game join …` they paste in their terminal.

## Notifications

`Sync` mode rings the terminal bell on your-turn handoffs and after auto-submit. For mobile push, set a Discord webhook on the game once:

```
civ6-async game webhook https://discord.com/api/webhooks/...
```

Every submit posts to that channel afterwards.

## Other commands worth knowing

| Command | What it does |
|---|---|
| `game list` / `game switch` / `game leave` | Multiple concurrent games |
| `game history` | Turn-by-turn audit log |
| `game repair` | Validate manifest + storage |
| `reset` | Wipe local state (config + downloaded saves), leaves cloud untouched |

`civ6-async --help` and `civ6-async game --help` list everything.

## Notes

- Works in single-player and hotseat. Won't load in ranked online multiplayer (Civ 6 anti-cheat blocks unsigned mods there).
- Submit refuses bad submits by default (wrong player, identical to last save, stale local file). `--force` overrides if needed.
- Conflicts with any other mod that replaces `PlayerChange.lua` or `ActionPanel.lua`.
- Helper config (`config.json`) lives next to the binary — fully portable.
- Mod auto-enable in Civ requires Civ to have been launched at least once (so it's scanned the Mods folder). If you install before ever opening Civ, just tick civ6-async manually in Additional Content → Mods this once.

## Building from source

Requires .NET 8 SDK.

```
cd src
./build.ps1            # produces dist/cli/{win-x64,linux-x64}/civ6-async[.exe]
```
