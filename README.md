# DraftModeTOUM

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Among Us** running [Town of Us: Mira Edition (TOUM)](https://github.com/AU-Avengers/TOU-Mira) that adds a **Draft Mode** — players take turns picking their roles before the game begins instead of having them assigned randomly.

---

## Requirements

- Among Us (compatible version with TOUM)
- [BepInEx IL2CPP](https://github.com/BepInEx/BepInEx)
- [Reactor](https://github.com/NuclearPowered/Reactor)
- [Town of Us: Mira Edition](https://github.com/AU-Avengers/TOU-Mira)

---

## Installation

1. Make sure BepInEx, Reactor, and TOUM are already installed and working.
2. Download the latest `DraftModeTOUM.dll` from [Releases](../../releases).
3. Drop it into your `BepInEx/plugins/` folder.
4. Launch Among Us — the mod will load automatically.

---

## How It Works

### Starting a Draft

Only the **host** can start a draft. Once everyone is in the lobby, the host types in chat:

```
/draft
```

This kicks off the draft sequence:

1. Each player is assigned a random **slot number** (their turn order).
2. The role pool is built from the full list of available TOUM roles (see [Role Pool](#role-pool) below).
3. Players pick one at a time in slot order.

---

### Taking Your Turn

When it's your turn, you'll see a chat message like:

```
YOUR TURN!
1. Sheriff
2. Jester
3. Engineer
4. RANDOM
```

Type `1`, `2`, `3`, or `4` in chat to make your pick. Choosing `4` assigns you a fully random role from the remaining pool.

Other players will see:

```
Player 3 is picking...
```

---

### Turn Timer

Each player has **10 seconds** to pick. If the timer runs out, a random role is automatically assigned and the draft moves on. The host can change the timer duration in code via `DraftManager.TurnDuration` (default: `10f` seconds).

---

### Draft Recap

After every player has picked, a recap is shown in chat by default:

```
── DRAFT RECAP ──
Player 1: Sheriff
Player 2: Jester
Player 3: Engineer
```

To **hide the recap** (so roles stay secret), the host can type:

```
/draftrecap or the Toggle Recap Setting In The Host Options
```

This toggles the recap on or off. When off, players only see `── DRAFT COMPLETE ──` at the end with no roles listed. Toggle it again to turn it back on. The current state is confirmed in chat:

```
Draft recap is now: OFF
```

> The recap setting persists until toggled again or the game is restarted.

---

## Chat Commands

| Command | Who | Description |
|---|---|---|
| `/draft` | Host only | Starts the draft |
| `/draftrecap` | Host only | Toggles Draft Recap
| `/draftend` | Host Only | Ends the current active draft | 

---

## Local Settings

If you go to settings and click on `Mira Draft`turn on override, no matter hosts settings it will always be cards. If you set `Use Circle Style` to true it will always use the circle UI, no matter the hosts settings.

---

## Role Pool

The roles that can be chosen are now controlled by the roles and there chances in the TOUM Role Settings set by the lobby's designated host.

---

## License

MIT
