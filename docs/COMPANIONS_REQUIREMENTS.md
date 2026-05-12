# Valhein Companions — Mod Requirements

**Status:** Draft v1
**Owner:** Pranay
**Last updated:** 2026-05-11
**Audience:** Spec-writing agents and implementers. This document is intentionally requirements-only — it does **not** prescribe implementation. Specs derived from this doc will choose data structures, RPCs, prefab pipelines, etc.

---

## 1. Vision

A Valheim mod that introduces **persistent companions** the player can recruit, command, and rely on as part of base operations and exploration.

The mod ships in evolving phases:

- **Phase 1** delivers a single, tamed wolf companion to validate the spawn / persistence / control loop end-to-end in single player.
- **Phase 2–5** layer on humanoid (player-character-styled) companions that can be assigned **work tasks** (smelting, farming, cooking) and act as **equippable combatants** who defend the base and the player.
- **Phase 6+** extends everything to multiplayer (P2P co-op, then dedicated server).

Each phase produces a shippable mod build with self-contained user value. Earlier phases must not be rewritten when later phases land — interfaces should anticipate later-phase needs without speculatively implementing them.

---

## 2. Glossary

| Term | Meaning |
| --- | --- |
| **Companion** | Any mod-spawned entity bound to a player and persisted across sessions. Wolf or humanoid worker. |
| **Combat companion** | A companion whose primary role is fighting alongside the player (Phase 1 wolf). |
| **Worker companion** | A humanoid companion that can be assigned a Task (Phase 3+). May also fight (Phase 5). |
| **Owner** | The player a companion is bound to. Phase 1: only player. Phase 6+: any networked player. |
| **Task** | A persistent assignment given to a worker companion (e.g., "smelt at this smelter"). |
| **Workstation** | Any vanilla or mod-added crafting station referenced by a Task (smelter, kiln, cooking station, cultivator-tilled plot, etc.). |
| **Home** | A designated location (typically near a bed or fire) used as the companion's spawn / fall-back / patrol anchor. |
| **Order** | A short-lived command issued by the owner (e.g., "follow", "stay", "attack that"). Distinct from a Task. |
| **Promotion** | Converting a vanilla tamed wolf into a mod Companion. |

---

## 3. Out of scope (explicit non-goals)

These are not pursued by this mod, period — call them out in specs to prevent scope creep:

- New biomes, dungeons, weapons, or recipes unrelated to companions.
- Replacing or rebalancing the vanilla taming system (we **wrap** it; we don't replace it).
- AI for vanilla creatures other than wolves promoted into companions.
- Cross-server companion transfer.
- Voice lines, dialogue trees, romance, or RPG progression beyond what tasks require.
- Replacing Valheim's pathfinding wholesale. We use what the engine gives us.

---

## 4. Architectural principles

These principles bind every phase. Any spec that violates one needs an explicit waiver in its design notes.

1. **Single-player first, MP-shaped.** Phase 1 ships SP-only, but data ownership, lifetime, and serialization decisions must be defensible when the same code runs on a dedicated server in Phase 6. No "ZDO-hostile" shortcuts (e.g., static dictionaries keyed by client-side instance IDs).
2. **Config over code.** Every player-facing knob — caps, costs, cooldowns, work durations, acquisition paths — is exposed via Jotunn `ConfigManager`. Hardcoded numbers are a red flag in review.
3. **Permadeath is real.** Companion loss must be a meaningful event (see §6.6). The mod must never silently respawn a dead companion to "be nice."
4. **Vanilla-compatible.** The mod must not break a vanilla save. If the user uninstalls, their world should still load (companion ZDOs become orphans that the game can clean up gracefully).
5. **Fail closed, log loud.** On any unexpected state (missing prefab, version mismatch, save data from a future version) the mod refuses to spawn the companion and writes a clear log line. No silent half-states.
6. **Performance budget.** Per-companion CPU budget: ≤ 0.4 ms/frame on Steam Deck-tier hardware (reference target) measured across a 60s window with the companion in active combat against 3 mobs at zone-density 1×. Idle-on-task workers ≤ 0.1 ms/frame under the same methodology. Methodology: Unity profiler capture; results documented per phase in the design doc and re-measured when AI code changes materially.
7. **No network mod requirement on clients in Phase 1.** The mod is server/host-side conceptually until Phase 6; clients should not need it installed for the SP build.

---

## 5. Cross-cutting requirements (apply to every phase)

### 5.1 Configuration
- All tunables exposed via `ConfigManager` with sensible defaults and clear descriptions.
- Configs are grouped by section: `General`, `Acquisition`, `Wolf Companion`, `Worker Companion`, `Tasks`, `Combat`, `Debug`.
- Server-synced configs are marked as such in code from day one, even when MP isn't yet supported.

### 5.2 Logging
- Use a consistent prefix (`[Valhein.Companions]`) on every log line.
- Log levels: `Debug` (per-tick noise, behind a Debug.Verbose config), `Info` (lifecycle: spawn / death / task assigned), `Warning` (recoverable: missing chest, blocked path), `Error` (unrecoverable: prefab load failure).
- Dump a one-line summary on plugin `Awake()` showing version, config hash, and active feature flags.

### 5.3 Persistence
- Companion state survives world reload, player death, server restart.
- Persistence format must be **forward-compatible**: every serialized blob has a schema version; old saves load with defaults applied for missing fields; loading a save from a newer mod version refuses with a clear error rather than corrupting it.
- Companion state lives on the companion's own ZDO where possible (so it travels with the entity), with player-scoped registry data (caps, ownership map) on the player's ZDO or a dedicated mod ZDO.
- **ZDO field discipline.** Valheim ZDOs only store primitive fields (`int`, `long`, `float`, `Vector3`, `Quaternion`, `string`, `byte[]`). Hot fields read every tick (HP, position, current order) live as native typed fields. Cold/structured state (task config, bindings, audit-log entries) is packed into a single `byte[]` blob with the schema-version envelope from above. Blob size per ZDO is capped at 8 KB; the spec must split or trim before this is hit.
- **Owner identity is SteamID, not ZDO network owner.** Each companion ZDO carries a `valhein.ownerSteamId` string field set at spawn. ZDO network ownership migrates between peers on zone load/unload (this happens in single player too, not just MP), so any code that conflates "the peer that currently owns this ZDO" with "the player this companion belongs to" is wrong. All ownership checks read the SteamID field.
- **Container writes use the vanilla owner-respecting path.** Mod code that mutates a `Container`'s `Inventory` (Phase 4 worker → bound chest) must request ZDO ownership of the container before writing, must defer when `Container.IsInUse()` is true (player has the chest open), and must use the same RPC path as `Container.RPC_RequestOpen` rather than mutating a non-owned `Inventory` directly. This is invisible in single player and corrupting in MP — it's a Phase 1 contract regardless of when MP ships.

### 5.4 Acquisition gating (config flags)
Per the Phase 1 decision, all acquisition methods are implemented but each is independently toggleable. Defaults are normative (see F1.9):
- `Acquisition.AllowConsoleSpawn` — default `true` in Phase 1, `false` from Phase 2 onward.
- `Acquisition.AllowCraftableSummon` — default `false` in Phase 1, `true` from Phase 2 onward.
- `Acquisition.AllowTamePromotion` — default `false` in Phase 1, `true` from Phase 2 onward.

### 5.5 Caps
- Companion caps are **per-type** (combat, worker), per-player.
- Defaults: `Combat.MaxPerPlayer = 1`, `Worker.MaxPerPlayer = 3`. Both configurable.
- Attempting to exceed a cap surfaces an in-game message ("You already have 1/1 combat companions. Dismiss one first.") and refuses the spawn.

### 5.6 Death

The original AskUserQuestion choice was hard permadeath. The design review surfaced that Valheim companions die predominantly to physics bugs, raid pathing, and terrain edge cases — not skill checks — so the user-approved revision below softens the **default** while retaining permadeath as an opt-in (see §9 decision log entry dated 2026-05-11).

- **Default mode: `Downed`.** When a companion's HP reaches 0, it enters an incapacitated state at its current position (no movement, no AI, friendly to all) for `Death.ReviveWindowSeconds` (default `300` = 5 min). A `[Warning]`-style in-game message posts: "Greybeard is down — revive within 5:00." The owner may interact-revive (channeled action; default cost: 1× `TrophyGreydwarf` consumed + 5 HP/s healing channel for the duration). On successful revive the companion stands up at full HP minus a configurable revive penalty.
- **Revive window expiry → permadeath.** If the window expires unrescued, ZDO is destroyed, cap slot freed, the message "Greybeard has fallen." posts, and a memento drops if `Death.DropMemento = true` (flavor only, no mechanical effect).
- **Owner-offline / distant-zone protection.** When `Death.InvulnerableWhenUnattended = true` (default `true`), companions take no damage while their owner is offline OR more than 2 zones (~128m) away. The protection lifts the moment the owner is in range — this is a "raid wipe" guard, not a god-mode farm.
- **`Death.Mode` config:**
  - `Downed` (default) — behavior above.
  - `Permadeath` — legacy hardcore: 0 HP → immediate ZDO destruction. No revive window. (Original user choice; preserved for opt-in.)
  - `TagRecovery` — drops a recoverable memento at death location; pickup by owner within `Death.ReviveWindowSeconds` restores the companion at the memento's location.
- **Audit log records both transitions** (entered Downed, transitioned to permadeath / revived / recovered).

### 5.7 Naming
- Every companion has a name. On spawn, a name is auto-generated (Norse-flavored list, configurable seed list).
- The owner can rename via a console command and (Phase 2+) via an in-game interaction.
- Names appear above the companion's head (configurable visibility), in death messages, and in the assignment UI.

### 5.8 Performance & limits
- Hard ceiling: no more than 16 mod companions in a loaded zone simultaneously, regardless of config (defense against config abuse). The `16` is a defense-in-depth budget; with default per-player caps a single player contributes ≤4 companions, so the headroom covers MP and stress testing.
- Companions in unloaded zones are dormant — no AI tick, no task progression beyond timestamp-based catch-up on zone reload (per F4.7).

### 5.9 Localization
All user-facing strings (death messages, cap-exceeded messages, UI labels, audit-log entries, command help text) are routed through Jotunn `LocalizationManager` from Phase 1. English-only ships in Phase 1; translation tables added Phase 2+. Hardcoded English in functional code is a review blocker. String keys follow the convention `valhein.<area>.<key>` (e.g., `valhein.death.fallen`).

### 5.10 Accessibility
- Color-coded markers (F4.10) must use a colorblind-safe palette — never red/green-only contrast. Markers carry a shape or letter cue in addition to color.
- Roster UI text scale follows Valheim's vanilla scale setting; no custom font sizes that bypass it.
- Every hotkey is rebindable via config (per §5.1).
- Floating names (per §5.7) respect Valheim's vanilla nameplate-toggle setting.

### 5.11 Telemetry & diagnostics
- Provide console command `valhein_diag` that dumps to clipboard and log file: plugin version, config hash, active companion count by type, per-companion brief (name, type, HP, current order, current task), recent error log lines, last 10 audit-log entries.
- No network telemetry. Nothing leaves the player's machine. Privacy is non-negotiable.
- Perf counters per §4.6 methodology are exposed via `valhein_diag --perf`.

### 5.12 Modder API surface
- Phase 1: no public extension API. All mod interfaces are marked `internal`.
- Phase 4 introduces a sealed `ITaskType` registration point so other mods can add Task types (e.g., Mining, Brewing) without forking. Surface is small and documented.
- Public API additions require a semver minor bump; breaking changes require a major bump.
- Each phase's design doc declares the API stability tier of any new types it introduces (`Internal` / `Stable` / `Experimental`).

### 5.13 Admin & security (Phase 6 prefigured, principles set Phase 1)
- Server-authoritative spawn validation: in MP, the server rejects companion-spawn RPCs whose claimed owner SteamID does not match the connecting peer's SteamID.
- Admin command `valhein_admin purge <steamid|all>` removes companions stuck or orphaned. Requires server-op privileges.
- Owner SteamID is immutable after spawn. Cross-player transfer is out of scope until explicitly requested.
- Mod RPCs use Jotunn's signed RPC channel where available; raw `ZRoutedRpc` is forbidden for any state-mutating call.

---

## 6. Phases

Phases are **incremental and additive**. Each phase ends in a shippable build. Specs for a phase may not assume features from a later phase exist, but **must** define interfaces that make the next phase land cleanly.

---

### Phase 1 — Tamed Wolf Companion (MVP)

**Goal:** Prove the end-to-end loop: acquire → control → persist → die. Single player only.

#### 6.1.1 User stories
- As a player, I can spawn a tamed wolf companion via console command.
- As a player, I can name my wolf, and the name appears above its head and in messages.
- As a player, my wolf follows me, attacks hostiles I attack, and stays put when commanded.
- As a player, my wolf survives logging out and back in, and is still mine when I return.
- As a player, if my wolf dies, it is gone permanently and the mod tells me clearly.

#### 6.1.2 Functional requirements
- **F1.1** Provide console command `valhein_spawn wolf [name?]` that spawns a Companion-tagged wolf at the player's feet.
- **F1.2** Companion wolf is bound to the spawning player. No other player can command it (relevant in Phase 6; in Phase 1, enforce the binding even though only one player exists).
- **F1.3** Companion wolf orders: `Follow` (default), `Stay`, `Aggressive` / `Defensive` stance. Issued via console command in Phase 1; UI is Phase 2.
- **F1.4** Companion wolf engagement rules:
  - Engages hostiles within `Wolf.EngagementRadius` (default `25` m from the wolf) **or** within `Wolf.OwnerEngagementRadius` (default `15` m from the owner when wolf is in Follow), whichever is closer at the time of detection.
  - Does **not** engage outside this radius unless the owner attacks first (then the engaged target is honored regardless of radius until the engagement ends per F1.12).
  - `Wolf.DoNotEngage` config is a list of creature prefab IDs the wolf will never engage (default: `Deathsquito`, `Seeker`, `SeekerBrute`, `Lox`).
  - Does **not** attack tamed creatures, other companions, or other players.
- **F1.5** Companion wolf has a configurable HP, damage, and stamina baseline distinct from vanilla wolves so balance can be tuned without affecting the base game.
- **F1.6** Companion wolf has **no inventory and no equipment slots** (per design decision).
- **F1.7** Companion wolf persists across save/load and player death.
- **F1.8** On death, §5.6 applies. Default mode is `Downed` with owner-offline / distant-zone protection enabled; `Permadeath` and `TagRecovery` are opt-in via `Death.Mode`.
- **F1.9** Acquisition methods (console / craftable summon item / tame-promotion) are all implemented and individually gated by config flags. Console default-on; craftable and tame-promotion default-off in Phase 1 (turned on in Phase 2 after polish).
- **F1.10** Craftable summoning item ("Wolf Whistle" working name): consumable or reusable per `Acquisition.WhistleConsumable` config; recipe and cost configurable; recipe registered via Jotunn.
- **F1.11** Tame-promotion: when `Acquisition.AllowTamePromotion = true`, an interaction (E by default, configurable) on a vanilla tamed wolf converts it into a Companion if under the cap. The vanilla wolf entity is destroyed and replaced by a Companion entity that inherits the wolf's level and visible state.
- **F1.12** Leash range. Wolf in Follow stays within `Wolf.FollowDistance` (configurable, range `5`–`15` m, default `8`). When the owner exceeds `Wolf.LeashTeleportRadius` (default `60` m) for more than `5` s continuously, the wolf teleports to the owner. Teleport is disabled while the wolf is in active combat (engagement still in progress per F1.4). Resolves former Q1.3.
- **F1.13** Owner identity. Each companion ZDO carries the field `valhein.ownerSteamId` (string) set at spawn. All ownership checks read this field; code may **not** treat the ZDO's network owner as a proxy for the owning player. This is a Phase 1 contract because ZDO network ownership migrates on zone load/unload even in single player. (See §5.3.)
- **F1.14** Console-command collision check. After the plugin registers commands in `Awake()`, it queries Jotunn `CommandManager` for each registered name. If any name was silently refused (Jotunn's documented behavior on collisions, per CLAUDE.md), the plugin logs `[Error]` with the colliding name and aborts initialization rather than running half-loaded.

#### 6.1.3 Non-functional requirements
- **N1.1** Wolf AI tick ≤ 0.4 ms/frame in active combat using the §4.6 methodology.
- **N1.2** Plugin loads cleanly with **only** Jotunn as a hard dependency (no other mods).
- **N1.3** Mod size on disk < 2 MB excluding shared dependencies.
- **N1.4** First-build deploy lands in `MOD_DEPLOYPATH\JotunnModStub\` (the project's existing pipeline; see CLAUDE.md).

#### 6.1.4 Acceptance criteria
- AC1.1 In a fresh world, `valhein_spawn wolf Greybeard` spawns a wolf named "Greybeard" within 2 seconds. The name renders above the wolf.
- AC1.2 The wolf follows when given Follow, stops when given Stay, engages a greydwarf within `Wolf.EngagementRadius` (per F1.4) within 1s of detection, and disengages and returns to Follow within 5s of the threat dying or leaving the radius.
- AC1.3 Saving and reloading the world preserves: companion existence, name, HP, position, current order, and ownership.
- AC1.4 Killing the wolf (via cheats or mob damage) prints "Greybeard has fallen." in the player feed, frees the combat-companion slot, and removes the entity. A second `valhein_spawn wolf` succeeds.
- AC1.5 Uninstalling the mod and loading the world does not crash; orphan companion ZDOs are removed by Valheim's normal cleanup or are inert.
- AC1.6 With `AllowConsoleSpawn = false` and only craftable enabled, the console command refuses with an explanatory message and the recipe is the sole acquisition path.

#### 6.1.5 Risks / unknowns
- **R1.a** Jotunn 2.20.3 (compile-time) vs 2.29.0 (runtime) gap (CLAUDE.md). Stable APIs only; bump NuGet if `MethodNotFoundException` appears.
- **R1.b** Vanilla taming-promotion hook surface — we may need Harmony patches around `Tameable` lifecycle. Investigate during Phase 1 spec.
- **R1.c** Console command name collision (CLAUDE.md notes Jotunn silently refuses duplicates). Audit names against vanilla `devcommands` list before registering.

---

### Phase 2 — Companion polish

**Goal:** Make Phase 1's wolf feel like a *kept* creature, not a debug spawn. Introduce shared infrastructure (UI, registry, home anchor) that humanoid workers will reuse.

#### 6.2.1 User stories
- As a player, I have a small in-game UI to see my companions, their HP, their orders, and their location.
- As a player, I can rename a companion in-game without the console.
- As a player, I can designate a "home" location for each companion; if I dismiss it, it returns there.
- As a player, I can enable craftable + tame-promotion acquisition without editing config files (in-game admin panel optional).

#### 6.2.2 Functional requirements
- **F2.1** Companion roster UI: opened via configurable hotkey (default `K`). Lists all of the player's companions with name, type, HP, order, and a "go to" / ping button.
- **F2.2** Per-companion interaction (E or hold-E on the companion) opens a small panel: rename, set home, set order, dismiss.
- **F2.3** "Home" location: a world position + (optional) bound bed/structure ZDO. Companion fast-travels (or pathwalks, configurable) to home on dismiss; uses home as fall-back when ordered to Stay without an explicit position.
- **F2.4** Default-on the craftable summoning item and tame-promotion acquisition methods (Phase 1 implemented them; Phase 2 enables and balances).
- **F2.5** Companion registry refactor: introduce a single source of truth for "all known companions in the world" that future phases (workers, MP) plug into.
- **F2.6** Audit log: persistent ring buffer (last N events per player, configurable, default 50): spawned, renamed, died, dismissed, task-assigned (placeholder for Phase 4).

#### 6.2.3 Non-functional requirements
- **N2.1** UI built with Jotunn's GUIManager — no raw IMGUI in production.
- **N2.2** Roster UI opens in < 100ms on a save with 16 companions.
- **N2.3** Phase 2 must not require resaving Phase 1 worlds beyond a one-time silent migration; migration code carries a unit test.

#### 6.2.4 Acceptance criteria
- AC2.1 Rename via UI updates the floating name immediately and persists across reload.
- AC2.2 Setting home to a bed makes the companion path back there on dismiss; if the bed is destroyed, home falls back to last valid position with a Warning log.
- AC2.3 Rebinding the roster hotkey via config takes effect without a game restart (plugin reload OK).
- AC2.4 Loading a Phase 1 save in a Phase 2 build preserves all companions; the audit log starts empty and begins recording new events.

#### 6.2.5 Risks / unknowns
- **R2.a** GUIManager version compatibility against Jotunn 2.29.0. Validate early in spec.
- **R2.b** Floating-name rendering — text-mesh perf on multiple companions in view.

---

### Phase 3 — Humanoid worker companion (basic)

**Goal:** Introduce the first humanoid (player-character-styled) companion. No tasks yet — just a humanoid that exists, follows, fights, and uses gear. This phase is intentionally about getting the **entity** right before adding the **work system**.

#### 6.3.1 User stories
- As a player, I can recruit a humanoid worker (initial acquisition: console; craftable contract item gated for Phase 4).
- As a player, my worker has an inventory and equipment slots; I can hand them a sword, a shield, and armor and they will use them.
- As a player, my worker follows me, fights, and otherwise behaves like the wolf does — but visibly humanoid.
- As a player, my worker has a name and appearance I can re-roll on creation.

#### 6.3.2 Functional requirements
- **F3.1** Console command `valhein_spawn worker [name?]` spawns a humanoid worker.
- **F3.2** Worker visual: built from Valheim's player visual stack (hair, beard, skin, base body) so reuse is high. Randomized on spawn; re-rollable from the per-companion panel until "confirmed" once.
- **F3.3** Worker has a full inventory (size configurable, default 8 slots) and equipment slots (helmet, chest, legs, cape, primary weapon, secondary/shield, utility).
- **F3.4** Equipment in slots affects worker stats: armor reduces damage taken, weapon governs damage and attack animation, shield enables block.
- **F3.5** Worker AI shares the order system with the wolf (Follow / Stay / stance) and obeys the same combat targeting rules (§F1.4).
- **F3.6** Worker counts against `Worker.MaxPerPlayer` (default 3), independent from the combat (wolf) cap.
- **F3.7** Persistence covers: name, appearance seed, inventory contents, equipped items, current order, home, HP/stamina.
- **F3.8** Worker death: per §5.6. On transition into Downed, dropped inventory is **not** released; on transition into permadeath (revive window expiry or `Mode = Permadeath`), dropped inventory follows vanilla "dead body" rules — recoverable as a tombstone-style container at the death location.
- **F3.9** Worker entity class. Worker is a `Humanoid`-derived NPC class, **not** `Player`-derived. Reused player components are restricted to: `VisEquipment` (visual gear rendering) and `ZSyncAnimation` (animation sync). Forbidden components: `PlayerController`, `Hud`, `Skills`, `SEMan` biome/comfort/food modifiers, death-screen hooks, input polling. Each Phase 3 design doc must enumerate every reused component and justify any addition beyond this allowlist. Resolves former Q3.1.

#### 6.3.3 Non-functional requirements
- **N3.1** Worker AI tick ≤ 0.6 ms/frame in active combat (1.5× the wolf budget; humanoids are heavier) using the §4.6 methodology.
- **N3.2** No new asset bundles required if reusing the player visual stack; if a custom bundle is unavoidable, it must be < 1 MB.

#### 6.3.4 Acceptance criteria
- AC3.1 `valhein_spawn worker` creates a humanoid that behaves as a wolf does for follow/stay/combat.
- AC3.2 Equipping a bronze sword and shield via the worker panel produces visible model change and measurable damage/block change in combat.
- AC3.3 Inventory persists across save/load. Worker death produces a recoverable container with all inventory + equipped items.
- AC3.4 Trying to spawn a 4th worker with default cap fails with the standard cap message.

#### 6.3.5 Risks / unknowns
- **R3.a** Humanoid AI off the player rig — Valheim's player class isn't designed to be AI-driven. We may need a `Humanoid`-derived NPC class with player visuals rather than reusing `Player` directly.
- **R3.b** Equipment-driven animation states; ensure worker uses correct attack animation per weapon class.
- **R3.c** Interaction between worker `Humanoid` and vanilla aggro tables (e.g., do greylings treat them as players?).

---

### Phase 4 — Task system (smelter / farmer / cook)

**Goal:** Workers earn their keep. Player assigns a Task with input/output bindings, worker performs it.

**Implementation model (per design decision):** **Hybrid — real navigation + abstract conversion.** The worker visually walks between bound chest(s) and station, plays animations, but the actual ore→ingot, seed→crop, raw→cooked transform is timer-based and bypasses the real station's queue. Long-term goal (post-Phase-4): real station use (the worker actually drives the smelter's input slot). Phase 4 must keep that door open.

#### 6.4.1 User stories
- As a player, I assign my worker to a smelter, point them at an input chest (ore) and an output chest (ingots), and they smelt while I'm away.
- As a player, I assign a worker to a tilled plot region, point them at a seed chest and a harvest chest, and they sow/harvest on a cycle.
- As a player, I assign a worker to a cooking station, point them at a raw-food chest and a cooked-food chest, and they cook.
- As a player, if the worker can't do the job (chest empty, station broken, no fuel), the task pauses and tells me why.

#### 6.4.2 Functional requirements
- **F4.1** Task assignment UI: per-companion panel adds a "Tasks" section. Player picks a Task type, then is prompted to bind a workstation and one or more chests by clicking on them in-world.
- **F4.2** Task types implemented in Phase 4: **Smelt**, **Farm**, **Cook**. Each has a typed schema for required bindings (e.g., Smelt requires: 1 station of class `Smelter` or `Blastfurnace`, 1 input chest, 1 output chest, 1 fuel chest).
- **F4.3** Task lifecycle: `Idle → Walking → Working → Walking → Idle`. State, current step, and last error are persisted so a reload resumes cleanly.
- **F4.4** Conversion model: timer-based per-item with rates configured per task type and per input item (e.g., `Tasks.Smelt.Tin = 30s`). Conversion timing is **gated by the visual cycle**, not run silently in the background: a conversion completes only after a successful walk-take-from-input → walk-to-station → channel timer → walk-deposit-to-output cycle. The visible loop and the data loop stay synchronized so the player can never observe a worker "appearing" to do nothing while ingots stack up. The actual smelter prefab is **not** loaded with ore by the mod in Phase 4 — its real inventory is untouched.
- **F4.5** Resource handling and container locking. Worker mutations on bound chests use the contract from §5.3:
  - Worker requests ZDO ownership of the chest before any write.
  - Worker checks `Container.IsInUse()` (player has the chest open) before mutation; if locked, defers in a backoff loop up to `Tasks.ContainerLockBackoffSeconds` (default `30`) before pausing the task with reason "Chest in use."
  - Mutations go through the same RPC path as `Container.RPC_RequestOpen`, never direct `Inventory.RemoveItem` on a non-owned container.
  - Real inventory mutations only — no fabricated items, no item duplication.
- **F4.6** Failure modes (each surfaces a distinct paused-state reason in the UI and audit log):
  - Bound chest missing or destroyed
  - Bound station missing or destroyed
  - Input chest empty
  - Output chest full
  - Fuel chest empty (where applicable)
  - Path blocked / unreachable
- **F4.7** Tasks do not progress while the zone is unloaded. On zone reload, the mod credits elapsed real-time up to `Tasks.OfflineCatchupCap` (default `7200` seconds = 2 hours), divided by the per-item conversion duration, further capped by available input materials and output chest space. Set the cap to `0` to disable catch-up entirely. Default-on catch-up makes the worker fantasy ("work happens while I'm exploring") meaningful out of the box; the cap prevents "logged in to 50,000 ingots" exploits.
- **F4.8** A worker may have at most one active Task at a time.
- **F4.9** Workers assigned to a Task continue to obey orders (Follow, Stay) — accepting an order pauses the Task. Resume on `Resume Task` action or when the worker is set back to its home.
- **F4.10** Task-bound objects (chests, stations) display a per-worker color-coded visual marker when the player is holding the assignment tool / the assigning UI is open, so the player can see what's bound to whom. Markers obey §5.10 (colorblind-safe palette + shape/letter cue).
- **F4.11** Chest sharing across workers. Multiple workers may bind the same chest as input, output, or fuel. Sharing is permitted but the mod writes a `[Warning]` audit-log entry on the duplicate binding ("Chest X is now bound by 2 workers"). Concurrent writes serialize via the F4.5 locking path; no data loss, no item duplication. Behavior on contention is "first writer wins; second worker pauses with backoff." Resolves former Q4.3.

#### 6.4.3 Non-functional requirements
- **N4.1** Idle-on-task worker (waiting for a conversion timer) ≤ 0.1 ms/frame using the §4.6 methodology.
- **N4.2** Pathfinding: use Valheim's existing `Pathfinder` / `MonsterAI` movement primitives. Custom navmesh is out of scope.

#### 6.4.4 Acceptance criteria
- AC4.1 A worker assigned to Smelt with a chest of 20 tin ore, a smelter, and a fuel chest with coal, completes 20 ingots into the output chest in `20 × Tasks.Smelt.Tin` seconds ± 10%, with the worker visibly walking input → smelter → output once per cycle.
- AC4.2 Removing the input chest mid-task pauses the task with reason "Input chest missing"; replacing a chest and rebinding via the UI resumes it.
- AC4.3 Saving mid-task and reloading resumes without item duplication or loss (validate by counting items pre/post reload).
- AC4.4 A worker on a Smelt task obeys a `Follow` order issued by the player (task pauses), and resumes the task on `Stay` at its home.
- AC4.5 Three workers running three distinct tasks in the same loaded zone do not deadlock or race on shared chests (each worker has its own bindings; sharing a chest across workers is allowed but documented as the user's risk in Phase 4).

#### 6.4.5 Risks / unknowns
- **R4.a** "Real navigation, abstract conversion" is the explicit hybrid call — guard against feature creep toward real station use until Phase 5+ revisits.
- **R4.b** Pathfinding fragility around player-built bases (steep terrain, raised stone). Need fallback behavior when path computation fails repeatedly.
- **R4.c** Inventory mutation hooks must be robust against vanilla container locking (chest in use by player).

---

### Phase 5 — Worker combat & equipment integration

**Goal:** Workers are not just laborers — they're **equippable combatants**. They defend their workstation and the player. Equipment quality matters for both work speed and combat survivability.

#### 6.5.1 User stories
- As a player, I can equip my worker with armor and a weapon and they will defend themselves and their workstation when attacked.
- As a player, I can set a worker's combat behavior: Pacifist (Phase 4 default — flee), Self-defense (fight if attacked), Garrison (defend area around home/workstation), Soldier (active combatant on Follow / Stay-Aggressive).
- As a player, equipment quality affects both how fast a worker performs Tasks (good tools → faster) and how well they fight.

#### 6.5.2 Functional requirements
- **F5.1** Combat behavior config per worker (default: **Garrison**, given the design decision that workers are fully equippable combatants).
- **F5.2** Garrison radius is configurable per-worker (default 20m around home or active workstation).
- **F5.3** Tool-bonus matrix: certain equipped tools speed up specific Tasks (e.g., a `Hoe` equipped speeds Farm by X%; a `Cultivator` slot speeds another). Matrix is config-driven; no hardcoded tool→task ratios.
- **F5.4** Workers retreat from combat to resume a paused Task once threats are out of garrison radius for `Combat.PostCombatCooldown` seconds (default 30).
- **F5.5** Workers do not friendly-fire other companions or the player.
- **F5.6** Raids (vanilla event system) trigger an "All workers to Garrison stance" override for the duration if `Combat.RaidOverride = true` (default true).

#### 6.5.3 Acceptance criteria
- AC5.1 A worker equipped with a bronze sword and shield, set to Garrison, intercepts a greydwarf entering the radius around its home and returns to Task within 30s of the threat dying.
- AC5.2 A worker equipped with a `Hoe` completes a Farm task cycle in ≤ 70% of the no-tool baseline time, validated by timing 5 cycles each (with-tool vs. no-tool) and comparing means.
- AC5.3 A "Foraging Surge" or other vanilla raid event temporarily promotes all Pacifist workers to Garrison; raid end restores their prior stance.

#### 6.5.4 Risks / unknowns
- **R5.a** Tool-bonus matrix is potentially invasive — verify it can be implemented without modifying vanilla tool code (likely via a Harmony postfix on Task tick).
- **R5.b** Combat → Task resume reliability when a worker's path home is blocked by enemy corpses or terrain damage.

---

### Phase 6 — Multiplayer (P2P co-op then dedicated server)

**Goal:** Lift the SP-only restriction. Companions are first-class networked entities.

#### 6.6.1 User stories
- As a player joining a friend's world, I can see my host's companions and they recognize me as friendly (don't attack).
- As a player on a dedicated server, my companions persist when I log out, but obey only me when I'm online.
- As an admin, I can configure server-side caps and override player caps.

#### 6.6.2 Functional requirements
- **F6.1** Companion ZDOs replicate to all clients in the zone; ownership transfers correctly when the owning player logs off (companion enters dormant / server-owned state) and back when they return.
- **F6.2** Companion ↔ player friendliness is determined by ownership; companions do not attack players in the same world by default. Configurable PvP mode opts in (`Combat.PvPCompanionsHostile`).
- **F6.3** Order issuing requires ownership. Other players see companions but cannot command them (Phase 6 baseline; "shared command" is a future opt-in).
- **F6.4** Tasks continue to advance while the owner is offline **only if** `Tasks.AdvanceWhileOwnerOffline = true` (default false to avoid surprise economy effects).
- **F6.5** Server-side configs are authoritative; client overrides for cosmetic/UI configs only.
- **F6.6** Dedicated server compatibility: the mod loads on a dedicated server and behaves correctly without a graphical client.

#### 6.6.3 Acceptance criteria
- AC6.1 Two players in a P2P session: host's wolf attacks a greydwarf, ignores the joining player, follows the host across zones, persists when the joiner disconnects.
- AC6.2 On a dedicated server: a player spawns a worker, assigns a Smelt task, logs off; the worker persists and (with default config) is paused; logging back in resumes.
- AC6.3 Admin sets `Worker.MaxPerPlayer = 1` server-side; client config overrides do not raise the cap.

#### 6.6.4 Risks / unknowns
- **R6.a** ZDO ownership migration on owner disconnect — Valheim's existing primitives may need careful Harmony work.
- **R6.b** Mod-required-on-client question: dedicated server ideally accepts vanilla clients with companions managed server-side. Investigate feasibility in spec; falling back to "mod required on all clients" is acceptable but should be a deliberate decision.

---

## 7. Open questions for spec authors

These are deliberately left open so spec authors can resolve them with eyes on the code. Each must be answered (or reaffirmed as deferred) before its phase's spec is approved.

### Phase 1
- **Q1.1** What is the exact prefab basis for the Companion wolf? Clone vanilla `Wolf` with `Tameable` pre-applied, or build from `Wolf_Cub` and grow? Pick during Phase 1 spec.
- **Q1.2** Where does a console-spawned companion's appearance vary (color, size)? Or all identical until Phase 2?
- **Q1.4** Does tame-promotion preserve the wolf's existing star level / pregnancy state, or reset?

### Phase 2
- **Q2.1** Roster UI hotkey choice — is `K` actually free in vanilla and common mods? Audit before commit.
- **Q2.2** "Home" precision — bind to a bed ZDO (durable but limited) or an arbitrary world position (flexible but orphans more easily)?
- **Q2.3** Audit-log surface — exposed in UI, or log file only?

### Phase 3
- **Q3.2** Worker visual rig: full player visual stack (and the maintenance burden that implies as Valheim updates), or a simpler humanoid template?
- **Q3.3** Do enemies treat workers as players for aggro purposes (e.g., trigger raid spawn weight)? Probably no — but the answer needs to be deliberate.
- **Q3.4** Who picks the worker's appearance — random with reroll button, or full character creator screen?

### Phase 4
- **Q4.1** UI for binding chests/stations — point-and-click in-world, or a list-based picker? Point-and-click is more diegetic but harder to build.
- **Q4.2** Should workers consume food / stamina to do tasks? (Could be an interesting balance lever; could be tedious.)
- **Q4.4** Visual marker design for bound objects — colored outline, floating icon, both? (Palette constraints already set by §5.10 + F4.10.)

### Phase 5
- **Q5.1** What's the canonical tool-bonus matrix? Needs design pass before the spec — likely a small table in-doc.
- **Q5.2** Garrison radius visualization — show as a circle when assigning, or invisible?

### Phase 6
- **Q6.1** Does a server require the mod, or can vanilla clients connect to a modded server with companions? Decide the supported deployment matrix.
- **Q6.2** PvP semantics — should there be a "kill-on-sight other players' companions" mode for hardcore PvP servers?

---

## 8. Deliverables per phase

For each phase, the spec produced from this document must yield:

1. A design doc covering data model, lifecycle, AI states, and any Harmony patches.
2. A test plan with at least one validation case per acceptance criterion (manual is OK in Phase 1; automated coverage for serialization/persistence by Phase 3).
3. A user-facing changelog entry suitable for the Thunderstore manifest.
4. An updated `CLAUDE.md` section if the build / deploy / runtime pipeline changes (e.g., new asset bundle, new dependency).
5. An update to this requirements doc if the phase reveals a baked-in assumption that no longer holds. Requirements drift is fine; silent drift is not.

---

## 9. Decision log (tracks user-confirmed choices that shaped this doc)

Recorded 2026-05-11:

- Multiplayer scope: **single-player Phase 1, co-op + dedicated server long-term** — guides §4.1 and Phase 6.
- Acquisition methods: **all three (console, craftable, tame-promotion), config-gated** — see §5.4 and F1.9–F1.11.
- Worker task model: **hybrid (real navigation + abstract conversion) Phase 4; real station use is the long-term goal** — see Phase 4 preamble and R4.a.
- Inventory model: **wolf has none; humanoid worker has full inventory + equipment slots** — see F1.6, F3.3.
- Companion caps: **separate per-type caps**, defaults `Combat=1`, `Worker=3`, both configurable — see §5.5.
- Death model: **permadeath** with no respawn or revive — see §5.6.
- Worker combat role: **fully equippable combatants**; default behavior `Garrison` — see F5.1, F5.2, F5.6.

Recorded 2026-05-11 (post-review revisions, derived from three-reviewer feedback; user approved the consolidated change set):

- **Death model softened.** Default flipped from hard `Permadeath` (original AskUserQuestion choice) to `Downed` with a 5-min revive window plus owner-offline / distant-zone invulnerability. `Permadeath` remains an opt-in `Death.Mode`. Reasoning: Valheim companions die predominantly to physics bugs and raid pathing, not skill checks; hard-permadeath default would compound the Phase 5 raid override (F5.6) into routine workforce wipes. See §5.6, F1.8.
- **Owner identity is SteamID, promoted to Phase 1.** A `valhein.ownerSteamId` ZDO string field is now a Phase 1 contract. ZDO network ownership migrates on zone load/unload even in single player, so any code conflating the two breaks silently in SP and visibly in Phase 6. See §5.3, F1.13.
- **Container locking contract surfaced in §5.3 and F4.5.** Worker writes to bound chests must respect `Container.IsInUse()` and route through the same RPC path as `Container.RPC_RequestOpen`. Phase 1 contract regardless of MP timeline.
- **Offline catch-up default reversed (0s → 7200s).** Phase 4 workers now credit up to 2 hours of real-time elapsed work on zone reload, capped further by inputs and output space. Without this, the worker fantasy collapsed the moment the player left base. See F4.7.
- **Worker entity class fixed to `Humanoid`-derived (not `Player`-derived).** Reused player components limited to `VisEquipment` and `ZSyncAnimation`; forbidden list enumerated. Avoids dragging input/HUD/skills/biome systems into AI ticks. See F3.9.
- **Wolf engagement bounded by radius + opt-out list.** F1.4 was previously "attack any creature flagged hostile to the owner" — would produce Plains/Mistlands self-deletes against deathsquitos and seekers. Now bounded by `Wolf.EngagementRadius` plus `Wolf.DoNotEngage` allowlist. See F1.4.
- **Cross-cutting sections added (§5.9–§5.13):** Localization, Accessibility, Telemetry & diagnostics, Modder API surface, Admin & security. Locks down policy now so spec authors don't re-decide per phase.
- **Perf budgets quantified.** §4.6 now defines methodology (Steam-Deck-tier reference, Unity profiler, 60s window, zone-density 1×) and ms/frame numbers. N1.1, N3.1, N4.1 all reference §4.6.
- **Acquisition defaults consolidated in §5.4** to match F1.9 (was inconsistent — said "phase-dependent" without enumerating).
- **Three open questions promoted to FRs:** Q1.3 → F1.12 (leash range), Q3.1 → F3.9 (entity class), Q4.3 → F4.11 (chest sharing).

Future amendments to this log should be append-only; do not rewrite earlier entries.
