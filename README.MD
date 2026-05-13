# Valhein

A Valheim BepInEx + Jötunn mod focused on long-lived **companions** — creatures that follow you, fight alongside you, and persist across reloads.

Built against Jötunn 2.20.3 (compile-time) / 2.29.0 (r2modman runtime). Single-player only at this point; the architecture is shaped for multiplayer in a later phase.

## What's shipped

### Phase 1 — Tamed Wolf Companion *(current)*

A persistent, owner-bound combat wolf with an order state machine, configurable death modes, and three acquisition paths.

- **Owner-bound by SteamID** — saved on the wolf's ZDO, immutable after spawn.
- **Smooth follow** — drives vanilla `MonsterAI.m_follow`, so movement feels like a vanilla tamed wolf (no pathing fight).
- **Two stars by default** — configurable via `[Wolf Companion] DefaultStarLevel`.
- **Three death modes** — `Permadeath` (default), `Downed` (revivable for 5 min with a `TrophyGreydwarf`), `TagRecovery` (drops a memento on death).
- **Leash-teleport** — wolf catches up if it falls >60 m behind out of combat for 5 s.
- **Persistence** — ZDO hot fields + versioned cold-blob codec; survives save/reload and forward-compatible schema upgrades.
- **Cap check** — `Combat.MaxPerPlayer = 1` by default.

Full design doc: [`docs/PHASE_1_WOLF_COMPANION_SPEC.md`](docs/PHASE_1_WOLF_COMPANION_SPEC.md).

## Commands

All commands work from the F5 console with no `devcommands` requirement.

| Command | What it does |
| --- | --- |
| `valhein_spawn wolf [name]` | Spawn a companion wolf bound to you. Name is optional — picks from a Norse pool if omitted. |
| `valhein_order follow [name]` | Stick to the owner. Default order at spawn. |
| `valhein_order stay [name]` | Hold position at current spot (wanders ±2 m). |
| `valhein_order aggressive [name]` | Engage hostiles inside 2× engagement radius. |
| `valhein_order defensive [name]` | Only engage when the owner or wolf is attacked. |
| `valhein_dismiss [name]` | Permanently remove a companion and free the slot. |
| `valhein_diag` | Print companion state to the console and copy to clipboard. |
| `valhein_diag --perf` | Adds AI-tick and leash-tick perf stats. |

`[name]` is optional when you have only one wolf; required when you have several.

Also retained from earlier: `heal2 [amount]` — convenience self-heal.

## Configuration

After the first launch, BepInEx writes `<r2modman profile>\BepInEx\config\com.jotunn.jotunnmodstub.cfg`. Highlights:

| Section / key | Default | Notes |
| --- | --- | --- |
| `Acquisition.AllowConsoleSpawn` | `true` | `valhein_spawn` works only when this is on. |
| `Acquisition.AllowCraftableSummon` | `false` | Enables a craftable **Wolf Whistle** at the workbench. Recipe in `WhistleRecipe`. |
| `Acquisition.AllowTamePromotion` | `false` | Hold E on a vanilla tamed wolf to convert it into a companion (preserves star count). |
| `Wolf Companion.HealthBase` | `200` | |
| `Wolf Companion.DamageBlunt` / `DamageSlash` | `25` / `25` | |
| `Wolf Companion.EngagementRadius` | `25 m` | Doubled in Aggressive stance. |
| `Wolf Companion.FollowDistance` | `8 m` | Clamped to `[5, 15]`. |
| `Wolf Companion.LeashTeleportRadius` / `LeashTeleportSeconds` | `60 m` / `5 s` | Out-of-combat catch-up. |
| `Wolf Companion.DefaultStarLevel` | `2` | Stars on spawn (0–3). Tame-promotion ignores this. |
| `Death.Mode` | `Permadeath` | `Permadeath` / `Downed` / `TagRecovery`. |
| `Death.ReviveWindowSeconds` | `300` | Used by `Downed` and `TagRecovery`. |
| `Death.ReviveItem` / `ReviveItemCount` | `TrophyGreydwarf` / `1` | Channeled revive cost. |
| `Combat.MaxPerPlayer` | `1` | Per-owner cap. |

## Build & deploy

This machine launches Valheim via **r2modman**; the Steam install's `BepInEx\plugins` is ignored. Build outputs are copied to:

```
%APPDATA%\r2modmanPlus-local\Valheim\profiles\Default\BepInEx\plugins\JotunnModStub\
```

`Environment.props` (gitignored) bakes the per-machine paths. To build:

- `Ctrl+Shift+B` in Visual Studio, or
- `MSBuild.exe JotunnModStub.sln -t:Build` from a Developer PowerShell.

To run: open **r2modman → Start modded**. Launching from Steam runs vanilla and the mod won't load.

Mods do **not** hot-reload — quit Valheim fully between iterations.

For background on the build pipeline, r2modman quirks, and the Jötunn API gap, see [`CLAUDE.md`](CLAUDE.md).

## Logs

`<r2modman profile>\BepInEx\LogOutput.log` is the source of truth. On a healthy boot you should see:

```
[Info: JotunnModStub] ModStub has landed
[Info: Valhein.Companions] Harmony patches applied
[Info: Valhein.Companions] wolf prefab registered (vanilla 'Wolf' cloned)
[Info: Valhein.Companions] v0.1.0 — config hash … — features: console=on whistle=off promote=off death.mode=Permadeath
```

If the wolf-prefab line is missing, your `MOD_DEPLOYPATH` is wrong or the DLL never landed in r2modman's plugins folder.

## Roadmap

- **Phase 2** — roster UI, in-world interaction panel, named-companion HUD nameplates, appearance variation.
- **Phase 3** — humanoid worker companions (tasks, station assignment).
- **Phase 6** — multiplayer server-sync of `Acquisition.*`, `Wolf.*`, `Death.*`. Config entries are already marked `IsAdminOnly` so the seam is in place.
