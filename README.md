# civ6-async

Civilization VI hotseat without the friction. Skips the click-to-begin-turn screens between players and auto-ends every turn the moment no decision is needed. Hotseat ends up feeling like async play, where you only stop when the game actually wants you.

## What it does

- **Skips the "Click to begin your turn" popup** at hotseat handoff (unless that player has a password set).
- **Skips the "Please Wait" popup** during engine handoff.
- **Auto-ends turns** when there's nothing for you to do: no units waiting on orders, no choices to make, no notifications open.
- **Knows when to stop**: anything that calls for your attention (a freshly-produced unit, a met civ, a discovered wonder, a research or civic prompt) pauses the auto-advance until you handle it.

## Install

**Installer**: run `dist\civ6-async-Setup-1.0.0.exe`. Per-user, no admin needed. Run the same `.exe` again to uninstall.

**Manual**: copy this folder into

```
Documents\My Games\Sid Meier's Civilization VI\Mods\
```

Then enable **civ6-async** in **Additional Content → Mods**.

## Notes

- Single-player and hotseat. Won't load in ranked online multiplayer (Civ 6 anti-cheat blocks unsigned mods there).
- Conflicts with other mods that override `PlayerChange.lua` or `ActionPanel.lua`.
