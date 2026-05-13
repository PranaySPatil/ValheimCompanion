# Phase 1 Spec — Tamed Wolf Companion

**Status:** Draft v1
**Owner:** Pranay
**Last updated:** 2026-05-11
**Derived from:** `COMPANIONS_REQUIREMENTS.md` (§4–§5 cross-cutting + Phase 1 §6.1)
**Audience:** Implementer working in `JotunnModStub/`. Assumes `CLAUDE.md` build/deploy pipeline.

---

## 1. Scope

Phase 1 ships a single, persistent, owner-bound wolf companion in single-player. End-to-end loop: **acquire → control → persist → die**. Three acquisition paths land in code (console default-on; whistle and tame-promotion default-off), all gated by `Acquisition.*` config flags per §5.4. UI, humanoid workers, tasks, and MP are explicitly out of scope; interfaces and persistence shapes here must accommodate them without rework.

Hard non-goals for this phase:
- No roster UI, no in-world interaction panel (Phase 2).
- No equipment / inventory on the wolf (F1.6).
- No automated tests beyond a small set of pure-function unit tests on the schema codec; Phase 1 acceptance is manual per §8.2.

---

## 2. Module layout

New folders under `JotunnModStub\` (existing csproj picks up `*.cs` by glob — no additions to `JotunnModStub.csproj` should be needed for source):

```
JotunnModStub/
├── JotunnModStub.cs                     // unchanged plugin entry; wires Bootstrap
├── Companions/
│   ├── Bootstrap.cs                     // ordered init: configs → prefabs → recipes → commands → harmony
│   ├── CompanionPlugin.cs               // partial of JotunnModStub for clarity (optional)
│   ├── Config/
│   │   ├── CompanionConfig.cs           // all ConfigEntry<T> declarations grouped per §5.1
│   │   └── ConfigHash.cs                // stable hash of active config for diag
│   ├── Data/
│   │   ├── ZdoKeys.cs                   // string constants — single source of truth
│   │   ├── CompanionState.cs            // POCO; cold blob payload
│   │   ├── CompanionStateCodec.cs       // versioned (de)serializer
│   │   └── CompanionRegistry.cs         // in-process index keyed by ZDOID
│   ├── Prefabs/
│   │   ├── WolfCompanionPrefab.cs       // clones vanilla "Wolf"; strips/replaces components
│   │   └── WolfWhistlePrefab.cs         // craftable summoning item
│   ├── AI/
│   │   ├── WolfCompanionAI.cs           // MonsterAI subclass: order state machine
│   │   ├── EngagementPolicy.cs          // pure target-selection rules (F1.4)
│   │   └── LeashController.cs           // F1.12 follow distance + teleport
│   ├── Lifecycle/
│   │   ├── Spawner.cs                   // single entry point used by all 3 acquisition paths
│   │   ├── DeathController.cs           // §5.6 modes: Downed / Permadeath / TagRecovery
│   │   └── ReviveInteraction.cs         // channeled-revive Hoverable+Interactable on Downed
│   ├── Acquisition/
│   │   ├── ConsoleSpawnCommand.cs       // valhein_spawn wolf [name?]
│   │   ├── OrderCommands.cs             // valhein_order follow|stay|aggressive|defensive [name?]
│   │   ├── WhistleItem.cs               // ItemDrop wiring + use handler
│   │   └── TamePromotion.cs             // Harmony hooks on Tameable interact
│   ├── Identity/
│   │   ├── OwnerIdentity.cs             // SteamID resolution + ownership predicates
│   │   └── NameGenerator.cs             // Norse-flavored seed list + RNG
│   ├── Diagnostics/
│   │   ├── DiagCommand.cs               // valhein_diag, valhein_diag --perf
│   │   ├── PerfCounters.cs              // §4.6 measurement hooks
│   │   └── AuditLog.cs                  // ring buffer; surfaces in diag (UI in Phase 2)
│   └── Localization/
│       └── Strings.cs                   // key constants; English table loaded via Jotunn
└── HarmonyPatches/
    ├── HarmonyBootstrap.cs              // single Harmony instance, PatchAll
    └── TameablePromotionPatch.cs        // hooks vanilla tame interaction
```

Rationale: the directory boundaries match the cross-cutting sections of the requirements doc so future phases drop in next to like-with-like. `Companions/` is the entire mod surface; nothing leaks into the root namespace.

---

## 3. Data model

### 3.1 ZDO field map

Per §5.3: hot fields native, cold/structured state in a single capped `byte[]` blob. **All keys live as `const string` in `ZdoKeys.cs` to prevent typo drift.**

| Key | Type | Hot? | Purpose |
| --- | --- | --- | --- |
| `valhein.companion` | `int` (1) | hot | Tag — presence marks the ZDO as a Companion. Values reserved: `1`=wolf, `2`=worker (Phase 3). |
| `valhein.ownerSteamId` | `string` | hot | F1.13. Set once at spawn, immutable. |
| `valhein.ownerNameCached` | `string` | warm | Display only — last-known owner display name. Refreshed on attach. Never used for ownership checks. |
| `valhein.name` | `string` | hot | Companion display name. |
| `valhein.order` | `int` | hot | `0=Follow`, `1=Stay`, `2=Aggressive`, `3=Defensive`. |
| `valhein.stayPos` | `Vector3` | hot | Anchor for `Stay`. |
| `valhein.deathState` | `int` | hot | `0=Alive`, `1=Downed`, `2=DeadPendingMemento` (`TagRecovery` only). |
| `valhein.downedAtUnix` | `long` | hot (when Downed) | Unix-seconds timestamp of Downed entry. Revive expiry computed on demand. |
| `valhein.schemaVer` | `int` | hot | Top-level companion schema version (currently `1`). Used to gate the cold blob decode. |
| `valhein.cold` | `byte[]` | cold | `CompanionState` blob — see §3.2. Cap 8 KB per §5.3. |

Notes on placement:
- All fields live on the wolf's own ZDO (so state migrates cleanly with the entity), per §5.3.
- A separate **per-player registry ZDO** is _not_ created in Phase 1; the cap check (§5.5) is computed by enumerating `CompanionRegistry` (§3.4), which is itself rebuilt from a scan on world load. A registry ZDO is added in Phase 2 along with the roster UI.
- `valhein.ownerSteamId` is a string, not a long, because Steam tools and admin commands universally use the decimal-string form and cross-platform peer identifiers in future may not fit in `long`.

### 3.2 Cold-blob schema (`CompanionState`)

Encoded with `BinaryWriter` over a `MemoryStream`. Order is normative; reader uses field-count and version to skip unknown trailing bytes.

```
header:
  uint16  magic          = 0xVA10           // "Valhein, schema v1.0"
  uint16  schemaVersion   = 1
  uint16  fieldCount      = N               // count of fields written below

body (each field is tagged):
  byte    fieldId
  varlen  payload (per fieldId)
```

| fieldId | Name | Payload | Meaning |
| --- | --- | --- | --- |
| 1 | `appearanceSeed` | `int32` | Reserved Phase 2 (Q1.2). Always written; default `0` in Phase 1. |
| 2 | `homePos` | `Vector3` (3×float) | Reserved Phase 2 (`Home`). Default `(NaN,NaN,NaN)` = unset. |
| 3 | `homeBoundZdoId` | `int64` (ZDOID packed) | Reserved Phase 2. Default `0` = unset. |
| 4 | `auditRing` | length-prefixed `string[]` | Last 50 audit entries (§5.6 + Phase 2 audit log seeded here). Each entry is `unix\tcode\tpayload`. |
| 5 | `tameLevel` | `int32` | Star level inherited via tame-promotion (Q1.4). `0` for console/whistle spawns. |
| 6 | `acquisition` | `byte` | `0=Console`, `1=Whistle`, `2=TamePromotion`. Diagnostic only. |

**Forward compatibility (§5.3):**
- Reader checks `schemaVersion`. If `> 1`, the spawn aborts with `[Error]` per §4.5 ("save data from a future version").
- Unknown `fieldId` values are skipped (length-prefixed payloads make this safe). New fields MUST be appended; existing IDs MUST NOT be reused.
- Missing fields in an older blob default to the table's "default" column.

Codec target: < 1 ms encode/decode per companion on the spawn/persist path. The blob is rewritten only on state change (order change, name change, audit append, death-state transition), not per tick.

### 3.3 Cold-blob size budget

| Field | Typical | Worst |
| --- | --- | --- |
| header | 6 B | 6 B |
| 1, 5, 6 | 9 B | 9 B |
| 2, 3 | 20 B | 20 B |
| 4 (50 entries × ~80 B each) | 0–4 KB | ~4 KB |

Worst case under §5.3's 8 KB cap with margin. `AuditLog` enforces a 50-entry ring (default) and trims any single entry > 256 B before append.

### 3.4 In-process registry

`CompanionRegistry` is a `ConcurrentDictionary<ZDOID, CompanionHandle>` rebuilt on:
- Plugin `Awake()` post-Harmony patch (no-op until world load).
- `ZNetScene.Awake()` patch — scan loaded ZDOs for `valhein.companion != 0`.
- New companion spawn (registers immediately).
- Wolf component `OnDestroy` (deregisters).

The registry is the **source of truth for cap checks (§5.5) and `valhein_diag`**. It is never persisted; ZDOs are.

---

## 4. Prefab pipeline (resolves Q1.1)

**Decision: clone vanilla `Wolf` (adult)** at runtime via Jotunn `PrefabManager.CreateClonedPrefab("ValheinCompanionWolf", "Wolf")`. Reasons:
- No growth/age state to model in Phase 1.
- `Wolf_Cub` is a separate prefab whose grow-up is a `Growup` component pointing at `Wolf` — adds a transition we don't need.
- Adult stats are the balance baseline players expect from "tamed wolf."

### 4.1 Component transforms

After clone, but before `PrefabManager` registration, mutate components:

| Vanilla component | Action | Rationale |
| --- | --- | --- |
| `MonsterAI` | **Replace** with `WolfCompanionAI` (subclass, see §6) | Vanilla AI doesn't model the order state machine. |
| `Tameable` | **Remove** | Companion is "born tame"; vanilla taming progress UI is wrong here. |
| `Procreation` | **Remove** | Wolves don't breed — explicit non-goal §3. |
| `Growup` (if present) | Remove | Adult only. |
| `Humanoid` (HP/damage) | **Reconfigure** at spawn from `Wolf.*` configs (§7) | F1.5 — distinct baseline from vanilla. |
| `CharacterDrop` | Trim to a single `TrophyWolf` only when `Death.Mode = Permadeath` and `Death.DropMemento = false` (handled at death time, not prefab time) | Avoid duplicate drops via memento. |
| `ZNetView` | Keep | Required for ZDO. |
| `ZSyncTransform`, `ZSyncAnimation` | Keep | Standard creature sync. |
| `LevelEffects` | Keep | Star-visual carry-through for tame-promotion. |

Clone happens in `WolfCompanionPrefab.Register()`, called once from `Bootstrap` after `PrefabManager.OnVanillaPrefabsAvailable` fires. The clone is registered as a `CustomPrefab` with `fixReference: true` so script refs are resolved.

### 4.2 Faction & tags

- `Character.m_faction = Character.Faction.Players` — companions cannot be targeted by other tames or by the player and are friendly to other tames (matches vanilla `Tameable` behavior).
- Add component-level marker `CompanionMarker` (empty MonoBehaviour) for fast `GetComponent<>` lookups. Avoids string-tag costs.

### 4.3 Acceptance vs. CLAUDE.md gotchas

- The `[Info: JotunnModStub] ModStub has landed` line continues to fire — registration code logs an additional `[Info: Valhein.Companions] wolf prefab registered (vanilla 'Wolf' cloned)` so the deploy-path sanity check is double-keyed.
- The Jotunn 2.20.3↔2.29.0 gap (R1.a) is touched only via stable surfaces: `PrefabManager.CreateClonedPrefab`, `ItemManager.AddItem`, `CommandManager.AddConsoleCommand`, `LocalizationManager.AddLocalization`. No GUIManager use in Phase 1.

---

## 5. Lifecycle

### 5.1 Plugin init order (in `Bootstrap.Run` from `Awake()`)

1. Bind configs (`CompanionConfig.Bind(plugin.Config)`).
2. Compute and stash config hash for diag (`ConfigHash`).
3. Apply Harmony patches (`HarmonyBootstrap.PatchAll`) — must precede any prefab registration that depends on them (Tameable promotion uses one).
4. Register prefabs via Jotunn lifecycle events:
   - `PrefabManager.OnVanillaPrefabsAvailable += WolfCompanionPrefab.Register`
   - `ItemManager.OnVanillaItemsAvailable += WolfWhistlePrefab.Register`
5. Register English localization table (Phase 1 ships `English` only per §5.9).
6. Register console commands; immediately re-query `CommandManager.Instance.CustomCommands` and verify each registered name is present (F1.14). On any missing name, log `[Error] Console command '<name>' was refused (likely vanilla collision). Aborting plugin init.` and **early-return without subscribing the registry-rebuild hook** so the mod fails closed (§4.5).
7. Subscribe `ZNetScene.Awake` postfix to seed `CompanionRegistry` from existing ZDOs.

### 5.2 Spawn flow (single entry point — `Spawner.SpawnWolfFor(player, name?, acquisition, originPos)`)

1. **Cap check** (§5.5): query `CompanionRegistry.CountFor(ownerSteamId, type=Wolf)`. If `>= Combat.MaxPerPlayer`, post chat message via `MessageHud.ShowMessage` with key `valhein.cap.combat_full` and return `null`.
2. **Resolve owner SteamID** via `OwnerIdentity.GetSteamIdOf(player)`. If null (rare — host-without-Steam), abort with `[Error]` and a player-facing message.
3. Pick name: provided arg ?? `NameGenerator.Next(seed: Hash(ownerSteamId, DateTime.UtcNow.Ticks))`.
4. Instantiate via `ZNetScene.instance.GetPrefab("ValheinCompanionWolf")` and `Object.Instantiate` at `originPos` (player feet for console/whistle; existing wolf transform for tame-promotion).
5. Stamp ZDO fields per §3.1 (set `valhein.companion=1`, owner SteamID, name, default order = `Follow`, schemaVer = `1`).
6. Encode and store cold blob (`acquisition` field set, `tameLevel` set if applicable).
7. Apply combat baseline: `humanoid.SetMaxHealth(Wolf.HealthBase)`, `humanoid.m_health = humanoid.GetMaxHealth()`, attack damage scaled via `ItemDrop.m_itemData.m_shared.m_damages` on the spawned bite item.
8. Register with `CompanionRegistry`.
9. Audit-append `Spawned`.
10. Announce: `MessageHud.ShowMessage(MessageType.TopLeft, valhein.spawn.welcome, name)`.

### 5.3 Load flow

`ZNetScene.Awake` postfix scans `ZDOMan.instance.m_objectsByID` for `GetInt("valhein.companion") == 1`. For each:
- Validate `valhein.schemaVer` (abort on future version per §4.5).
- Decode cold blob; on decode error, log `[Error]`, set `deathState=Downed` with `downedAtUnix = now` so the player sees the failure and the entity isn't silently lost.
- Insert into `CompanionRegistry`.
- The entity itself is instantiated by Valheim's normal ZNetScene zone-load path; no extra spawn needed.

### 5.4 Unload flow

Per §5.8, dormant companions in unloaded zones do not tick. No special handling beyond: `OnDestroy` on the `CompanionMarker` deregisters from the in-process registry **only if** the destroy was an actual deletion (ZDO gone), not a zone-unload. We distinguish by `ZNetScene.instance.m_tempRemoved` membership inside the destroy callback — Jotunn samples already use this pattern. On uncertain destroys we re-scan on next zone load.

---

## 6. AI & order state machine

### 6.1 States (`WolfCompanionAI : MonsterAI`)

| Order | Movement target | Engagement allowed? | Notes |
| --- | --- | --- | --- |
| `Follow` (default) | Owner position, holding `Wolf.FollowDistance` (default `8` m) | Yes per §6.2 | Leash teleport per F1.12. |
| `Stay` | `valhein.stayPos` ± 2 m wander | Yes per §6.2 | Anchor set when order issued; defaults to current position. |
| `Aggressive` | Same as current `Follow`/`Stay` | Yes; engagement radius **doubled** to `2× Wolf.EngagementRadius` | Modifier on existing order, not a separate movement mode. |
| `Defensive` | Same as current `Follow`/`Stay` | Only on owner-attacked or self-attacked targets | Ignores opportunistic detection. |

Implementation: `Order` is the persistent ZDO field; `Stance` (Aggressive/Defensive) is also persisted. The `Order` enum encodes both (4 values) for ZDO compactness; AI computes movement from base order and engagement radius from stance.

### 6.2 Engagement policy (`EngagementPolicy.cs`, pure-function)

```
SelectTarget(ai, ownerPos, candidates) -> Character?
  if (ai.OwnerEngagedTarget != null && stillAlive(ai.OwnerEngagedTarget)):
    return ai.OwnerEngagedTarget   // honor owner-attacked target regardless of radius (F1.4)

  if (ai.Stance == Defensive && !ai.SelfAttackedRecently):
    return null

  effectiveRadius = ai.Stance == Aggressive
    ? Wolf.EngagementRadius * 2
    : Wolf.EngagementRadius

  best = null; bestDist = float.MaxValue
  foreach c in candidates where Hostile(c) && !DoNotEngage(c):
    if (c is tamed || c is Player || c is Companion) continue
    distSelf = dist(c, ai)
    distOwner = dist(c, ownerPos)
    if (distSelf <= effectiveRadius || (ai.Order == Follow && distOwner <= Wolf.OwnerEngagementRadius)):
      d = min(distSelf, distOwner)
      if (d < bestDist): best = c; bestDist = d
  return best
```

`DoNotEngage(c)` checks the prefab name against the `Wolf.DoNotEngage` config list (default `Deathsquito,Seeker,SeekerBrute,Lox` per F1.4).

The "owner attacked first" hook is a Harmony postfix on `Player.AddNoise`/`Character.Damage` that records the player's most-recent damage target into a small per-player ring buffer the AI consults.

### 6.3 Tick budget (N1.1)

- AI tick uses Valheim's existing `MonsterAI.UpdateAI` cadence — Phase 1 does not introduce a new `Update` loop.
- `EngagementPolicy.SelectTarget` runs at the same cadence as vanilla `MonsterAI.UpdateTarget` (every 0.5s in vanilla), with candidate list sourced from `Character.GetAllCharacters()` filtered by squared distance to the owner _before_ entering the policy function (early exit cheap).
- `LeashController` runs in `FixedUpdate` at most every 4th tick (~0.08s); teleport check uses cached "out-of-range duration."

Per-companion measurement is exposed via `valhein_diag --perf` (PerfCounters wraps the AI tick in a `Stopwatch`).

### 6.4 Order-issuance commands (F1.3)

- `valhein_order follow [name?]`
- `valhein_order stay [name?]`
- `valhein_order aggressive [name?]`
- `valhein_order defensive [name?]`

If `name` omitted and the player has exactly one wolf, target it; if multiple, error with usage hint listing names. UI replacement for these commands is Phase 2 (F2.1/F2.2).

---

## 7. Configuration (§5.1)

All entries go through `plugin.Config.Bind(section, key, default, description)` and are server-synced where indicated. **No magic numbers in code; all references to defaults below are sourced from the bound `ConfigEntry<T>`.**

### 7.1 `[General]`
- `General.LogVerbose : bool = false` — enables `Debug` log lines per §5.2.
- `General.NamePoolSeed : string = ""` — comma-separated list; if empty, built-in Norse list is used.

### 7.2 `[Acquisition]` (§5.4)
- `Acquisition.AllowConsoleSpawn : bool = true`
- `Acquisition.AllowCraftableSummon : bool = false`
- `Acquisition.AllowTamePromotion : bool = false`
- `Acquisition.WhistleConsumable : bool = true`
- `Acquisition.WhistleRecipe : string = "Wood:5,LeatherScraps:2,WolfFang:1"` — parsed at recipe registration.
- `Acquisition.TamePromotionInteractKey : KeyboardShortcut = E` — held while interacting with a vanilla tamed wolf.

### 7.3 `[Wolf Companion]`
- `Wolf.HealthBase : float = 200`
- `Wolf.DamageBlunt : float = 25`
- `Wolf.DamageSlash : float = 25`
- `Wolf.StaminaBase : float = 100`
- `Wolf.EngagementRadius : float = 25` (F1.4)
- `Wolf.OwnerEngagementRadius : float = 15` (F1.4)
- `Wolf.DoNotEngage : string = "Deathsquito,Seeker,SeekerBrute,Lox"` (F1.4)
- `Wolf.FollowDistance : float = 8` (F1.12; clamped to `[5, 15]` at read time)
- `Wolf.LeashTeleportRadius : float = 60` (F1.12)
- `Wolf.LeashTeleportSeconds : float = 5` (F1.12)
- `Wolf.NameplateVisible : bool = true` (§5.7; respects vanilla nameplate toggle when true)

### 7.4 `[Death]` (§5.6)
- `Death.Mode : enum {Downed, Permadeath, TagRecovery} = Downed`
- `Death.ReviveWindowSeconds : int = 300`
- `Death.InvulnerableWhenUnattended : bool = true`
- `Death.UnattendedZoneRadius : int = 2` (zones from owner; matches §5.6 ~128m)
- `Death.ReviveItem : string = "TrophyGreydwarf"`
- `Death.ReviveItemCount : int = 1`
- `Death.ReviveChannelHpPerSec : float = 5`
- `Death.ReviveHealthPenaltyPct : float = 0` (Phase 1: no penalty by default)
- `Death.DropMemento : bool = true`

### 7.5 `[Combat]` (caps live here for cross-phase consistency)
- `Combat.MaxPerPlayer : int = 1` (§5.5)

### 7.6 `[Debug]`
- `Debug.Verbose : bool = false`
- `Debug.PerfSampleSeconds : int = 60` (§4.6 measurement window for `valhein_diag --perf`)

### 7.7 Server-sync flags (§5.1, prefigured for Phase 6)
Every entry above is bound with `ConfigurationManagerAttributes { IsAdminOnly = true }` **except** `General.LogVerbose`, `Wolf.NameplateVisible`, and `Debug.*` which are client-local. This is set from day one even though MP isn't supported, per the principle.

---

## 8. Death (§5.6 implementation)

### 8.1 State machine

`DeathController` listens for `Character.OnDeath` (Harmony postfix on the wolf prefab's `Character` only — gated by `CompanionMarker.GetComponent`).

```
OnDeath:
  if Death.Mode == Permadeath:
    AnnounceFallen(); AuditAppend(Permadeath); DestroyZdo()
    return

  if Death.Mode == TagRecovery:
    SpawnMemento(deathPos)
    AnnounceFallen(); AuditAppend(TagRecoveryDropped)
    Set deathState = DeadPendingMemento; ScheduleZdoDestroyAt(now + Death.ReviveWindowSeconds)
    return

  // Default: Downed
  Set deathState = Downed; downedAtUnix = now
  Freeze AI (movement off, target null, faction unchanged)
  Set m_health = 1; disable Damage receiver via component flag
  AnnounceDown(name, Death.ReviveWindowSeconds)
  AuditAppend(EnteredDowned)
  // Caller does NOT call Destroy; the entity stays at its pose.
```

A periodic check (1 Hz; piggybacks on existing AI tick gate) on Downed companions:
- If `now - downedAtUnix >= Death.ReviveWindowSeconds`: AnnounceFallen, AuditAppend(`PermadeathByExpiry`), drop memento if configured, destroy ZDO.
- If owner is attempting revive: ReviveInteraction handles state transitions atomically.

### 8.2 Revive interaction (`ReviveInteraction : MonoBehaviour, Hoverable, Interactable`)

Attached only while `deathState == Downed`. On `Interact(Player p, bool hold)`:
1. Verify `p` is the owner (SteamID match per F1.13). Otherwise `MessageHud` "Not your companion."
2. Verify inventory contains `Death.ReviveItemCount` of `Death.ReviveItem`. Otherwise hint the requirement.
3. Begin channeled state — store start tick on the companion's ZDO under a transient key (`valhein.reviveStartTick`).
4. Per tick: compute `elapsed = now - startTick`. Heal `Death.ReviveChannelHpPerSec * dt`. On `m_health >= maxHealth`:
   - Consume `Death.ReviveItemCount` of `Death.ReviveItem` (real inventory mutation through `Inventory.RemoveItem`).
   - Set `deathState = Alive`, clear `downedAtUnix`, reset `m_health = maxHealth * (1 - Death.ReviveHealthPenaltyPct/100)`.
   - Re-enable AI; resume previous order.
   - Audit-append `Revived`.
   - Announce.
5. On player movement away or interaction cancel: clear transient key, do not consume the item.

### 8.3 Owner-offline / distant-zone protection (§5.6)

Implemented in `DeathController` as a `Character.RPC_Damage` Harmony prefix that — when `Death.InvulnerableWhenUnattended = true` — zeroes the damage if **either**:
- The owner SteamID has no matching connected `Player.m_localPlayer` in single player (i.e., this is a server tick with no owner present), **or**
- `ZoneSystem.instance.GetZone(ownerPos)` differs from `ZoneSystem.instance.GetZone(wolfPos)` by more than `Death.UnattendedZoneRadius` zones in either axis.

In single player this most commonly fires when the player crashes mid-session and the wolf is loaded by the host process briefly during the world-save tick. The protection prevents "found my wolf dead at login from a leftover damage event."

### 8.4 Memento

`MementoItem` is registered as a custom `ItemDrop` cloned from `TrophyWolf`. Carries no mechanical effect (per §5.6). Configured icon and tooltip pull from localization. In `TagRecovery` mode, picking it up by the owner within the window restores the companion at the memento's location (re-instantiate prefab, restore cold blob from a snapshot embedded in the memento's own ZDO via the same codec).

---

## 9. Acquisition (F1.9–F1.11)

All three paths funnel into `Spawner.SpawnWolfFor`. They differ only in trigger, gating, and `acquisition` enum stamped in the cold blob.

### 9.1 Console (`valhein_spawn wolf [name?]`)

- Refused if `Acquisition.AllowConsoleSpawn = false` — print: `valhein.acquire.console_disabled` (AC1.6).
- Vanilla console must be open (`Console.instance.IsCheatsEnabled()` is _not_ required — this is a mod command, not a cheat).
- Origin: `Player.m_localPlayer.transform.position` + small forward offset so the wolf doesn't clip the player.

Help text: `valhein_spawn wolf [name] — spawn a Companion wolf bound to you.`

### 9.2 Wolf Whistle (`WolfWhistlePrefab`)

- Item: cloned from `Horn_Bronze` (visual stand-in; small handheld). Registered via `ItemManager.AddItem` with a `Recipe` parsed from `Acquisition.WhistleRecipe`.
- On primary use (`ItemDrop.ItemData.m_shared.m_useDurability = false` initially; consumption handled manually): if `Acquisition.AllowCraftableSummon = false`, print `valhein.acquire.whistle_disabled` and bail without consuming.
- Otherwise call `Spawner.SpawnWolfFor(..., acquisition: Whistle)`. If `Acquisition.WhistleConsumable = true`, decrement stack on success only (failures don't consume).
- The whistle is registered every build but its recipe is hidden from the workbench when `Acquisition.AllowCraftableSummon = false` (Jotunn `CustomRecipe.Recipe.m_enabled = false`). This keeps existing whistles in inventories from disappearing on a config flip.

### 9.3 Tame-promotion (`TamePromotion` + `TameablePromotionPatch`)

- Harmony postfix on `Tameable.Interact(Humanoid user, bool hold, bool alt)`.
- Conditions: `Acquisition.AllowTamePromotion = true` AND `hold = true` (long-press to disambiguate from vanilla tame-petting) AND the held key matches `Acquisition.TamePromotionInteractKey` AND the target's prefab name is `Wolf` AND `Tameable.IsTamed()` returns true AND the user is the wolf's tame-owner.
- On match:
  1. Cap check — refuse with cap message on full.
  2. Read source wolf's level (`Character.GetLevel()`) for Q1.4 inheritance.
  3. Capture position/rotation.
  4. Destroy the vanilla wolf via `ZNetScene.instance.Destroy(go)` after marking the source ZDO for removal (`ZDOMan.instance.DestroyZDO`).
  5. `Spawner.SpawnWolfFor(..., originPos, acquisition: TamePromotion, tameLevel: capturedLevel)`.
  6. Audit `PromotedFromTame`.

**Q1.4 resolution:** preserve `Character.m_level` (star count) into the new companion via the `tameLevel` cold-blob field, applied at spawn through `LevelEffects.SetupLevel`. **Pregnancy state is NOT preserved** — companions don't breed (Procreation removed per §4.1). Any vanilla `Procreation` mid-cycle on the source wolf is dropped.

---

## 10. Leash & teleport (F1.12)

`LeashController` (component on the wolf, ticked from `FixedUpdate`):

```
state: float outOfRangeAccumSec = 0
state: bool isInCombat   // true while EngagementPolicy.SelectTarget != null

each tick:
  if Order != Follow: outOfRangeAccumSec = 0; return
  if isInCombat: outOfRangeAccumSec = 0; return    // teleport disabled in combat per F1.12
  d = distance(self, owner)
  if d > Wolf.LeashTeleportRadius:
    outOfRangeAccumSec += dt
    if outOfRangeAccumSec >= Wolf.LeashTeleportSeconds:
      TeleportToOwner(); outOfRangeAccumSec = 0
  else:
    outOfRangeAccumSec = 0
```

`TeleportToOwner` snaps to a position behind the owner with a brief `vfx_player_taunt`-style spark for player-visible feedback (vanilla particle, no new asset). If owner zone unloaded → noop (registry handles re-attach on next zone load).

---

## 11. Owner identity (F1.13, §5.3)

`OwnerIdentity` exposes:
- `string GetSteamIdOf(Player p)` — returns `p.GetPlayerID().ToString()`. Phase 1 lives in SP; `Player.GetPlayerID()` returns the local player's Steam ID via Valheim's `ZNet.GetUID()` on the host. If unavailable returns `null`.
- `bool IsOwner(ZDO zdo, Player p)` — string compare on `valhein.ownerSteamId`. Returns false on null/empty.
- `bool IsOwner(ZDO zdo, string steamId)` — overload for non-Player call sites (admin commands later).

**No code path may consult `zdo.GetOwner()` to determine the owning player.** A Roslyn analyzer or simple grep-driven CI check is desirable but out of Phase 1 scope; PR review enforces.

---

## 12. Console-command collision check (F1.14)

After each `CommandManager.Instance.AddConsoleCommand(cmd)` in `Bootstrap`:

```csharp
var registered = CommandManager.Instance.CustomCommands;
foreach (var cmd in expectedCommands)
{
    if (!registered.Any(c => c.Name == cmd.Name))
    {
        Logger.LogError($"[Valhein.Companions] Console command '{cmd.Name}' was refused " +
                        "(likely vanilla collision). Aborting plugin init.");
        _aborted = true;
    }
}
if (_aborted) return; // skip subsequent registration steps
```

Phase 1 commands: `valhein_spawn`, `valhein_order`, `valhein_diag`. None collide with vanilla `devcommands` or with the existing `heal2` mod command, but the check defends against future vanilla additions and downstream mod conflicts.

---

## 13. Logging (§5.2)

- Use Jotunn's `Jotunn.Logger` wrappers for `LogInfo`/`LogWarning`/`LogError`. For `Debug` level, a small `Log.Debug(msg)` helper checks `General.LogVerbose` before forwarding to `Jotunn.Logger.LogDebug` (vanilla level filter still applies in BepInEx config).
- Prefix `[Valhein.Companions]` on every line — enforced by the `Log` helper, not on call sites.
- `Awake()` summary line: `[Valhein.Companions] v0.1 — config hash a3b4c5d6 — features: console=on whistle=off promote=off death.mode=Downed`.

---

## 14. Localization (§5.9)

All player-facing strings go through `Localization.instance.Localize("$valhein.<key>")` (Jotunn loads keys without `$` from the table; runtime lookup uses the prefix). Keys defined in `Strings.cs` as constants, English values registered through Jotunn `LocalizationManager.AddTranslation("English", dict)`.

Initial Phase 1 keys (non-exhaustive):
- `valhein.spawn.welcome` — "{0} is by your side."
- `valhein.cap.combat_full` — "You already have {0}/{1} combat companions. Dismiss one first."
- `valhein.death.down` — "{0} is down — revive within {1}."
- `valhein.death.fallen` — "{0} has fallen."
- `valhein.death.revived` — "{0} is back on its feet."
- `valhein.acquire.console_disabled` — "Console spawning is disabled in current config."
- `valhein.acquire.whistle_disabled` — "Whistle summoning is disabled in current config."
- `valhein.acquire.promote_disabled` — "Tame-promotion is disabled in current config."
- `valhein.order.set` — "{0}: {1}."  (e.g., "Greybeard: Stay.")
- `valhein.cmd.unknown_companion` — "No companion named '{0}' found."

---

## 15. Telemetry (`valhein_diag`, §5.11)

`valhein_diag` (no args) outputs to console **and** clipboard (via `GUIUtility.systemCopyBuffer`):

```
== Valhein Companions diag ==
plugin: 0.1.0    config_hash: a3b4c5d6
features: console=on whistle=off promote=off
companions (1):
  [Wolf]  Greybeard  hp=180/200  order=Follow  stance=Defensive  death=Alive  steamid=765xxxxxxxxx
recent errors (last 10):
  ... 
audit (last 10):
  2026-05-11T14:02:13Z Spawned name=Greybeard via=Console
  2026-05-11T14:08:51Z OrderChanged from=Follow to=Stay
```

`valhein_diag --perf` adds a `PerfCounters` table:

```
perf (last 60s window):
  AI tick avg: 0.21 ms   p99: 0.38 ms   samples: 1804
  Leash tick avg: 0.04 ms  p99: 0.07 ms
```

`PerfCounters` is a fixed-size circular buffer of microsecond samples, allocated once. No GC pressure on the hot path.

**No network telemetry. Ever.** (§5.11.)

---

## 16. Harmony patches

Single Harmony id: `com.jotunn.jotunnmodstub.companions`. `PatchAll` over the `HarmonyPatches/` namespace. Patch list:

| Target | Type | Purpose |
| --- | --- | --- |
| `Tameable.Interact(Humanoid, bool, bool)` | Postfix | Tame-promotion entry (§9.3). |
| `Character.RPC_Damage(...)` | Prefix | Owner-offline / distant-zone invulnerability (§8.3). |
| `Character.OnDeath()` | Postfix | Death state machine entry (§8.1). Filtered via `CompanionMarker`. |
| `ZNetScene.Awake()` | Postfix | Seed `CompanionRegistry` from existing ZDOs (§5.3 load flow). |
| `Player.OnDamaged(...)` (or `Character.Damage`) | Postfix | Record owner's most-recent damage target for engagement honor (§6.2). |

Each patch lives in its own file under `HarmonyPatches/` for diff readability and to keep R1.b/R1.c surfaced individually.

---

## 17. Risks revisited

| Tag | Risk | Mitigation in this spec |
| --- | --- | --- |
| R1.a | Jotunn 2.20.3 ↔ 2.29.0 API gap | §4 limits us to stable surfaces; if a `MethodNotFoundException` shows in `LogOutput.log`, bump NuGet per `CLAUDE.md`. |
| R1.b | Tameable hook surface | §9.3 commits to a single Harmony postfix on `Tameable.Interact`; falsified during spike if signature differs. |
| R1.c | Console name collision | §12 implements F1.14 with fail-closed init. |
| New | ZDO ownership migration during spawn (race between `Object.Instantiate` and `ZNetView`'s claim of ZDO ownership) | Spawner sets `valhein.ownerSteamId` immediately after `ZNetView` Awake runs but before any AI tick — checked by stamping inside the same frame as `Object.Instantiate` and validating in `WolfCompanionAI.Awake`. If the SteamID is empty in `Awake`, AI throws `InvalidOperationException` (fail closed, §4.5). |
| New | Tame-promotion on a remote-owned wolf in dedicated-server-on-localhost setups | Phase 1 SP-only — the `Tameable.IsTamed()` + local-player ownership gate is sufficient; Phase 6 will revisit with server validation. |

---

## 18. Open questions reaffirmed / resolved

- **Q1.1 (prefab basis)** — **Resolved** in §4: clone vanilla adult `Wolf`.
- **Q1.2 (appearance variation)** — **Deferred to Phase 2.** Phase 1 spawns are visually identical (vanilla wolf material). `appearanceSeed` reserved in cold blob (§3.2 fieldId 1) so Phase 2 can branch on a stored seed without a schema bump.
- **Q1.4 (tame-promotion preserves star/pregnancy)** — **Resolved** in §9.3: star level preserved via `tameLevel` cold-blob field; pregnancy state explicitly dropped (Procreation removed at prefab time).

---

## 19. Test plan (Phase 1 acceptance is manual; deliverables §8.2)

Manual-only is acceptable for Phase 1 per the requirements doc; serialization unit tests are the one exception because they're cheap and the codec is the one piece most likely to silently corrupt saves.

### 19.1 Automated (xUnit, in-test-project to be added next phase or alongside Phase 1)

- `CompanionStateCodec_Roundtrip` — every fieldId roundtrips bytewise.
- `CompanionStateCodec_ForwardCompatRead` — a v1 payload with an added v2 fieldId at the tail decodes successfully under v1, ignoring the unknown field.
- `CompanionStateCodec_FutureVersionRefused` — `schemaVersion = 2` returns the reserved sentinel that `Spawner` translates into the `[Error]` abort.
- `EngagementPolicy_HonorsDoNotEngage` — Deathsquito candidate is filtered.
- `EngagementPolicy_HonorsOwnerEngagementRadius` — target out of self radius but inside owner radius is selected when in `Follow`.
- `EngagementPolicy_DefensivePassive` — no target selected absent owner-attacked / self-attacked signal.
- `OwnerIdentity_NeverFallsBackToZdoOwner` — the API surface has no method that returns the ZDO owner; intentional structural test.

### 19.2 Manual checklist (mapped to acceptance criteria — see §20 traceability)

| Test | AC |
| --- | --- |
| Fresh world, run `valhein_spawn wolf Greybeard` → wolf appears within 2s with floating "Greybeard" name | AC1.1 |
| Issue `valhein_order follow`, `stay`, then aggro a greydwarf within 25m → wolf engages within 1s, returns to follow within 5s after threat dies | AC1.2 |
| Save & reload → wolf still present, named, HP/position/order/owner intact | AC1.3 |
| Kill the wolf via console damage; verify `Downed` mode message; let revive window expire → "Greybeard has fallen." prints, slot freed, second `valhein_spawn wolf` succeeds | AC1.4 |
| Same kill but with `Death.Mode = Permadeath` → entity removed immediately | AC1.4 (variant) |
| Uninstall mod, load world → no crash; orphan ZDO inert | AC1.5 |
| Set `Acquisition.AllowConsoleSpawn = false`, `Acquisition.AllowCraftableSummon = true` → console refuses with disabled message; whistle craftable & spawns wolf | AC1.6 |
| Set `Acquisition.AllowTamePromotion = true`, tame a vanilla wolf via vanilla path, hold-E it → it converts; star level preserved | F1.11 / Q1.4 |
| Stand at the edge of `Wolf.LeashTeleportRadius` for >5s out of combat → wolf teleports; repeat while wolf is in combat → wolf does not teleport | F1.12 |
| Run `valhein_diag` → output dumps and is on clipboard | §5.11 |
| Type a console name that collides (manually rebind one to `heal` and rebuild) → plugin aborts init with `[Error]` line | F1.14 |
| Profile Steam-Deck reference for 60s, wolf in combat with 3 mobs at 1× density → AI tick ≤ 0.4 ms/frame avg | N1.1 / §4.6 |

### 19.3 Test-environment notes

- Use a fresh r2modman profile to avoid collisions with other mods (per `CLAUDE.md` deploy path note).
- The Steam Deck reference test can be approximated on desktop by capping the editor at the Deck CPU profile and confirming the budget; final reading is on actual hardware (Phase 1 does not block ship if desktop-approximate measurement passes and Deck measurement is queued).

---

## 20. Acceptance-criteria traceability

| AC | Spec section | Implementation file(s) |
| --- | --- | --- |
| AC1.1 | §5.2, §9.1, §14 | `Spawner.cs`, `ConsoleSpawnCommand.cs`, `NameGenerator.cs` |
| AC1.2 | §6.1–§6.2 | `WolfCompanionAI.cs`, `EngagementPolicy.cs` |
| AC1.3 | §3, §5.3 | `CompanionStateCodec.cs`, `ZdoKeys.cs` |
| AC1.4 | §8 | `DeathController.cs`, `ReviveInteraction.cs` |
| AC1.5 | §3, §4.2 | `WolfCompanionPrefab.cs` (faction & marker), §3 (no orphan-incompatible references) |
| AC1.6 | §9 | `Acquisition/*.cs` |
| F1.11 | §9.3 | `TamePromotion.cs`, `TameablePromotionPatch.cs` |
| F1.12 | §10 | `LeashController.cs` |
| F1.13 | §11 | `OwnerIdentity.cs`, `ZdoKeys.cs` |
| F1.14 | §12 | `Bootstrap.cs` |
| N1.1 | §6.3, §15 | `PerfCounters.cs`, `WolfCompanionAI.cs` |

---

## 21. Deliverables (per requirements §8)

1. **This document** — design doc (data model, lifecycle, AI, Harmony patches). ✅ in scope of this PR.
2. **Test plan** — §19.
3. **Changelog entry** for `Package/manifest.json`:
   > Phase 1: Tamed Wolf Companion — spawn via `valhein_spawn wolf [name]`, command via `valhein_order follow|stay|aggressive|defensive`, persistent across reload, configurable death mode (Downed / Permadeath / TagRecovery). Whistle and tame-promotion shipped behind config flags.
4. **`CLAUDE.md` updates** — none expected: build/deploy pipeline unchanged. If a new asset bundle is added (none planned), document under "Build pipeline."
5. **Requirements doc updates** — none expected if this spec lands as written.

---

## 22. Implementation order (suggested for the implementing PR)

1. `Bootstrap`, `CompanionConfig`, `Log` helper, `ZdoKeys`. (No behavior — sets up the scaffolding.)
2. `CompanionStateCodec` + unit tests (the one piece worth covering before integration).
3. `WolfCompanionPrefab` registration + `CompanionMarker` + faction.
4. `Spawner` + `ConsoleSpawnCommand` (smallest end-to-end loop).
5. `WolfCompanionAI` + `EngagementPolicy` (AI tick).
6. `LeashController`.
7. `DeathController` (default `Downed` mode) + `ReviveInteraction`.
8. `Permadeath` + `TagRecovery` mode branches.
9. `OwnerIdentity` (used throughout — extracted as standalone for testability).
10. `OrderCommands`.
11. `WhistleItem` + recipe.
12. `TamePromotion` + Harmony patch.
13. `DiagCommand` + `PerfCounters`.
14. Localization table.
15. F1.14 collision check (added last so the failure path is exercised against a complete command list).

Each step ends in a build that loads in r2modman without errors. PR is merged in this order to keep diffs reviewable.
