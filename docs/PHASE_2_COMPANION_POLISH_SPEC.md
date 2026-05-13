# Phase 2 Spec — Companion Polish

**Status:** Draft v1
**Owner:** Pranay
**Last updated:** 2026-05-12
**Derived from:** `COMPANIONS_REQUIREMENTS.md` §6.2 + Phase 1 retrospective
**Audience:** Implementer working in `JotunnModStub/Companions/`. Builds on Phase 1 (commit `d9e7aa7`).

---

## 1. Scope

Phase 2 makes the Phase 1 wolf feel like a *kept* creature, not a debug spawn, and lays infrastructure later phases reuse. End-to-end loop additions:

- **Roster UI** — hotkey opens a list of your companions with HP, order, and a "go to" ping.
- **Per-companion interaction panel** — hold-E on a wolf to rename, set home, change order, dismiss.
- **Floating nameplate** — overhead label honoring vanilla nameplate visibility setting.
- **Home** — world position + optional bound bed ZDO; affects `Stay` fallback and dismiss.
- **Audit log surface** — the cold-blob ring buffer Phase 1 already writes becomes user-visible in the interaction panel.
- **Acquisition flips** — `AllowCraftableSummon` and `AllowTamePromotion` default `true`.
- **Registry refactor** — keep the in-process `CompanionRegistry` (Phase 1's `ConcurrentDictionary`), but introduce a stable interface so Phase 3 workers and Phase 6 MP can plug in.

Hard non-goals for Phase 2:
- Equipment / inventory on the wolf (deferred — still F1.6 territory).
- Task system (Phase 3 / Phase 4).
- Server-sync (Phase 6).
- Appearance variation **mechanics** beyond the `appearanceSeed` already reserved in the cold blob — Phase 2 only wires the seed to a deterministic material tint; full art passes are Phase 5.

Migration constraint (N2.3): a Phase 1 save **must** load in Phase 2 without resave. Every new ZDO key and cold-blob field must default to a safe value when absent.

---

## 2. Module additions

Phase 1 directory layout is preserved; new folders under `JotunnModStub/Companions/`:

```
Companions/
├── UI/
│   ├── RosterPanel.cs            // F2.1 hotkey-opened list
│   ├── CompanionPanel.cs         // F2.2 per-companion interaction panel
│   ├── Nameplate.cs              // §5 floating name component on the wolf
│   ├── UIRegistry.cs             // tracks open panels so Esc + hotkey re-toggle work
│   └── UIStyle.cs                // shared colors/sizes consistent with Jotunn GUIManager
├── Home/
│   ├── HomeAnchor.cs             // resolves homePos / homeBoundZdoId at runtime
│   └── HomePathing.cs            // dispatch helper for dismiss-to-home (fast-travel or path)
├── Migration/
│   └── PhaseMigrations.cs        // run-once migrators keyed by valhein.schemaVer
└── Interaction/
    └── WolfInteractable.cs       // Hoverable + Interactable on the wolf prefab
```

`Companions/Data/CompanionRegistry.cs` is updated in place to expose an interface `ICompanionRegistry` (see §8). Phase 1's static type stays as the only implementer; Phase 3 / Phase 6 can swap or wrap it.

`Companions/Acquisition/OrderCommands.cs` gains a `valhein_rename [oldName] <newName>` command for parity with the UI (useful when the panel isn't open).

---

## 3. Data model deltas

### 3.1 ZDO field additions

| Key | Type | Hot? | Purpose |
| --- | --- | --- | --- |
| `valhein.homePos` | `Vector3` | hot | Home anchor. `(NaN,NaN,NaN)` = unset (default). |
| `valhein.homeBedZdoId` | `long` (packed) | hot | Optional bound bed ZDO. `0` = unset. |
| `valhein.renameCount` | `int` | warm | Diagnostic counter to detect rename loops in audit log. Optional, defaults to `0`. |

Why hot (not cold-blob): the roster UI reads these per companion on open, and the home anchor is consulted on every dismiss / Stay-fallback. Cold-blob's `homePos` and `homeBoundZdoId` reservations (Phase 1 §3.2 fieldIds 2 and 3) are **still encoded** for forensic audit continuity, but the hot ZDO field is the source of truth. The migrator (§9) copies the cold-blob values into the hot fields on first Phase 2 load.

### 3.2 Cold-blob — no schema bump

Schema stays at `1`. Phase 1 already reserved:

| fieldId | Name | Phase 2 usage |
| --- | --- | --- |
| 1 | `appearanceSeed` | Now actively written. Random `int32` at spawn; deterministic. Drives Nameplate color and (Phase 2-light) a coat-tint multiplier on the wolf's renderer. |
| 2 | `homePos` | Mirror of `valhein.homePos` for forensic continuity. Hot ZDO is authoritative. |
| 3 | `homeBoundZdoId` | Mirror of `valhein.homeBedZdoId`. |
| 4 | `auditRing` | Now surfaced in `CompanionPanel`. Already capped at 50; Phase 2 leaves the cap. |

Forward compatibility note: Phase 1's reader skips unknown fieldIds, so a future Phase 3 worker companion can add `workerStateBlob` (fieldId 7+) without breaking Phase 2 readers.

### 3.3 Cold-blob size budget revisit

Audit ring is the only dynamic field. Phase 1 budget (worst ~4 KB) still holds — new fields are fixed-width. No change to the 8 KB cap.

---

## 4. UI architecture (Jotunn `GUIManager`)

Per N2.1, **no raw IMGUI**. All panels use `GUIManager.Instance.CreateWoodpanel` / `CreateButton` / `CreateInputField` / `CreateText` so the look stays consistent with vanilla and respects Valheim's UI scale.

### 4.1 Initialization

- Subscribe `GUIManager.OnCustomGUIAvailable` (Jotunn 2.20+ surface; stable in 2.29 per `CLAUDE.md`).
- Build the roster panel once, hide on close, show on hotkey — no re-build on each open (N2.2: <100ms open).
- The per-companion panel is **instantiated on demand** with the target wolf set as state; closing destroys the GameObject. Trade-off: a couple of allocations per open, no stale wolf reference to manage.

### 4.2 Roster panel (`RosterPanel`)

Hotkey: configurable. **Default `L`** — `K` is map in vanilla and most popular mod managers leave `L` free. The choice is auditable via Q2.1 (§14). The hotkey is bound from `Hotkeys.RosterToggle : KeyboardShortcut = L` and **rebindable at runtime** per AC2.3.

Layout:

```
┌─ Companions ──────────────────────────────┐
│ ◯  Greybeard   Wolf  ★★  HP 200/200       │
│        order: Follow      [Go to]  [Open] │
│ ◯  Skoll       Wolf  ★    HP 65/200 (low) │
│        order: Stay        [Go to]  [Open] │
│ …                                          │
│                                            │
│            [Cap 2/4]                       │
└────────────────────────────────────────────┘
```

Behavior:
- **Go to** — pings the companion's position on the map (calls `Minimap.instance.AddPin` with a temporary `Death` icon, removed after 10s). Why a pin vs camera-snap: matches how players navigate in Valheim.
- **Open** — opens `CompanionPanel` for that wolf (and closes the roster to avoid overlap).
- Rows are stable-sorted by acquisition time (ZDO creation order, available via `ZDO.m_dataRevision`).

Performance (N2.2):
- Row construction is allocation-free per refresh: rows are pooled at panel build time (`CompanionRegistry` max size known by `Combat.MaxPerPlayer + Worker.MaxPerPlayer` later).
- HP text is the only per-tick changing field; refreshed at 4 Hz while the panel is open, not at all when closed.

### 4.3 Per-companion panel (`CompanionPanel`)

Opens via the **Open** roster button or via the in-world `WolfInteractable` hold-E (F2.2). Layout:

```
┌─ Greybeard ────────────────────────────┐
│  Wolf ★★   HP 200/200                  │
│  Owner: Pranay   Acquired: 2026-05-12  │
│                                         │
│  Name        [____________]  [Rename]   │
│  Order       [Follow ▾]                 │
│  Home        Unset           [Set here] │
│              (or look at bed + [Bind])  │
│                                         │
│  Audit (last 8):                        │
│   2026-05-12 14:02  Spawned via=Console │
│   2026-05-12 14:08  OrderChanged …      │
│                                         │
│              [Dismiss]   [Close]        │
└─────────────────────────────────────────┘
```

Behavior:
- **Rename** — validates length 1–24, strips control chars and `<color>` tags, calls `Spawner.Rename(handle, newName)` (added in §8). On success: nameplate updates immediately (AC2.1), audit-append `Renamed from=… to=…`.
- **Order dropdown** — same four orders, persists via the existing `WolfCompanionAI.SetOrder` path.
- **Set here** — captures current player position into `valhein.homePos`, clears `valhein.homeBedZdoId`. Audit-append `HomeSet pos=…`.
- **Bind** — if the player is looking at a `Bed` within 5 m, captures that bed's ZDO id into `valhein.homeBedZdoId`. Audit-append `HomeBoundBed zdoid=…`.
- **Dismiss** — pre-dismiss confirmation modal. On confirm:
  - If a home is set, route the wolf to home (`HomePathing.Dismiss`) before destroying the ZDO so the player sees it run off. If no home, behave like Phase 1's `valhein_dismiss` (immediate destroy).
- The audit table is read-only; full ring is dumped via the existing `valhein_diag` command (no change).

---

## 5. Nameplate (`Nameplate.cs`)

The floating overhead label called out in F1.13 / §5.7 but punted from Phase 1.

Design:

- A `Nameplate` MonoBehaviour is added to the wolf prefab in `WolfCompanionPrefab.Reconfigure` (same place we add `CompanionMarker`).
- Uses a single `TextMeshPro` text mesh attached to a child GameObject at `head` bone height; faces the camera every `LateUpdate`.
- Text content is `<ZDO name> <stars>` (e.g., `Greybeard ★★`).
- **Visibility gate** (matches vanilla nameplate toggle):
  - Hide if `Hud.IsUserHidden()` (vanilla nameplates off).
  - Hide if distance to camera > 18 m (configurable `Wolf.NameplateMaxDistance`).
  - Hide if wolf is in `Downed` state (replaced by `ReviveInteraction` hover text which is more useful at that moment).
- Color = HSV mapped from `appearanceSeed` (Phase 2 actually reads the seed). Determined once at Awake; cached on the component.
- Owner-pet view only by default (`Wolf.NameplateOwnerOnly = true`). MP-aware from day one even though MP isn't supported.

Perf budget (R2.b): one text mesh per loaded companion. With `Combat.MaxPerPlayer = 1` in single-player and a hard observable cap of ~4 in a session, this is negligible. The text **string** is rebuilt only when name or level changes — not per frame. A `_text.SetText` call only when the cached value changes.

If `TextMeshPro` isn't reliably available through the Valheim assembly bundle, fall back to legacy `TextMesh` with the same anchor logic. Decision point in §14.

---

## 6. Home (`Home/HomeAnchor.cs`, `HomePathing.cs`)

### 6.1 Resolution (`HomeAnchor.Resolve(zdo) -> Vector3?`)

```
if homeBedZdoId is set:
    bedZdo = ZDOMan.GetZDO(homeBedZdoId)
    if bedZdo != null && bedZdo is valid:
        return bedZdo.GetPosition() + 1 m offset
    else:
        log warning, fall through to homePos
if homePos is set (not NaN):
    return homePos
return null
```

Phase 1's ZDO key `valhein.stayPos` (Stay order anchor) is **distinct** from home. Stay continues to capture the wolf's current position at order time. The intersection: if a player issues `Stay` while the wolf is on its home anchor, behavior is identical — and that's fine.

If `Stay` is issued without an explicit position and the wolf has a home, the spec **does** prefer the home as the anchor over the wolf's current spot (per F2.3 fallback rule). Implemented by `WolfCompanionAI.SetOrder(Stay)` calling `HomeAnchor.Resolve(zdo) ?? currentPos`.

### 6.2 Dismiss-to-home (`HomePathing.Dismiss(handle)`)

Two modes via `Home.DismissBehavior : enum {Vanish, RunHome} = RunHome`:

- `Vanish` — Phase 1 behavior: destroy ZDO immediately.
- `RunHome` — set order to a transient `GoingHome` state (not persisted to ZDO `valhein.order`; tracked in-memory only). Wolf paths to `HomeAnchor.Resolve()`. On arrival within 3 m, destroy ZDO. Hard timeout of 60 s to avoid wolves wandering forever if pathing fails.

The transient `GoingHome` state is achieved by setting `m_follow` to a temporary anchor GameObject at the resolved home position (same pattern as Stay anchor in Phase 1's smooth-follow fix), with a tag the AI tick checks for ZDO destroy on arrival.

### 6.3 Bed binding integrity (AC2.2)

A bed-bound home is a soft dependency. The `HomeAnchor` log line `home bed missing — falling back to last home pos (zdoid=…)` covers AC2.2. We do **not** automatically clear `homeBedZdoId` on miss — the bed may be temporarily unloaded; clearing would be destructive. The cleanup only happens via the UI's **Set here** or **Bind** explicit user action.

---

## 7. Audit log surface

Phase 1's `AuditLog.Append` already writes into the cold-blob ring (max 50, 256 B/entry). Phase 2 adds:

- New audit codes: `Renamed`, `HomeSet`, `HomeBoundBed`, `HomeCleared`, `DismissedViaHome`, `PingedOnRoster`.
- `CompanionPanel` reads the last 8 entries via existing `AuditLog.Tail(state, 8)`.
- No persistence-shape change; no schema bump.

The Phase 1 audit ring is **per-companion**. Phase 2's req F2.6 ("per player") is interpreted as a roll-up view: the roster panel header line shows the most recent audit entry across all companions when one would prefer "what just happened?" at a glance. That's a read-only view over per-companion data; we don't introduce a separate per-player ring.

---

## 8. Registry refactor (F2.5)

Phase 1 has `static class CompanionRegistry` on a `ConcurrentDictionary<ZDOID, CompanionHandle>`. Phase 2 splits the contract:

```csharp
internal interface ICompanionRegistry
{
    void Add(CompanionHandle h);
    void Remove(ZDOID id);
    int  CountFor(string ownerSteamId, int companionType);
    IEnumerable<CompanionHandle> AllOwnedBy(string ownerSteamId);
    IEnumerable<CompanionHandle> All();
    event Action<CompanionHandle> Added;
    event Action<ZDOID> Removed;
    bool TryGet(ZDOID id, out CompanionHandle h);
}
```

`CompanionHandle` grows: `string DisplayName` (snapshot from ZDO), `float CachedHpFraction`, `long AcquiredAtUnix`. These are recomputed on UI refresh from the ZDO (the registry doesn't tail-poll).

Phase 1's `CompanionRegistry` is rewritten to implement `ICompanionRegistry` and exposed via `CompanionRegistry.Instance`. Existing static call sites get a thin shim:

```csharp
internal static class CompanionRegistry
{
    public static ICompanionRegistry Instance { get; } = new InProcessRegistry();
    public static void Add(CompanionHandle h)        => Instance.Add(h);
    public static void Remove(ZDOID id)              => Instance.Remove(id);
    // ... etc
}
```

So no churn in `Spawner` / `OrderCommands` / `DismissCommand` / Harmony patches. Future phases that need a different backing (e.g., a Phase 6 server-side registry that replicates to clients) can swap `Instance`.

Renaming (the new operation) gets its own helper next to spawn so the audit + nameplate update happens atomically:

```csharp
static class Spawner
{
    public static bool Rename(CompanionHandle h, string newName); // validates, mutates ZDO, fires Updated event
}
```

A new event `event Action<CompanionHandle> Updated` is added to `ICompanionRegistry` for rename / order-change so UI rows refresh without re-polling everyone.

---

## 9. Migration (N2.3, AC2.4)

`PhaseMigrations.RunOnce(zdo)` is called from the existing `ZNetSceneAwakePatch` postfix for each companion ZDO during seed.

Steps for a Phase 1 ZDO:
1. Read `valhein.schemaVer`. If `>= 2`, skip (already migrated).
2. Decode cold blob; if `homePos` (fieldId 2) is set, copy to `valhein.homePos` ZDO key.
3. If `homeBoundZdoId` (fieldId 3) is set, copy to `valhein.homeBedZdoId`.
4. If `appearanceSeed` (fieldId 1) is `0`, generate a stable seed from `Hash(ownerSteamId, ZdoId)` so existing wolves get a deterministic nameplate color from this point on. Re-encode cold blob with the new seed.
5. Set `valhein.schemaVer = 2`.

`schemaVer` bumps to `2` to mark "Phase 2 migration applied." Phase 1 codec still reads `schemaVer 1` payloads — the **cold-blob schemaVersion** stays at `1`. The ZDO-level `schemaVer` is the migration marker, not the codec version.

Migration is idempotent and runs in <1 ms per companion.

Unit test (one xUnit case per N2.3):
- `PhaseMigrations_RunOnce_BackfillsAppearanceSeed_FromHash`.
- `PhaseMigrations_RunOnce_IsIdempotent` — second call leaves state unchanged.

---

## 10. Acquisition default flips (F2.4)

```
Acquisition.AllowConsoleSpawn:    true  -> false   (per requirements §5.4)
Acquisition.AllowCraftableSummon: false -> true
Acquisition.AllowTamePromotion:   false -> true
```

The whistle recipe was registered every build in Phase 1 (the recipe row was just `Enabled=false`). Flipping the default to `true` enables the recipe immediately on a fresh install.

**Important user-facing note** to bake into the changelog: players who edited their Phase 1 config to enable the whistle and tame-promotion will see no behavior change. Players who took the defaults will now find:
- `valhein_spawn` is refused (use a whistle or promote a tame).
- A Wolf Whistle recipe appears at the workbench.
- Hold-E on a tamed wolf converts it.

The console command is left **registered** (not removed), and the runtime check just routes to the disabled message — flipping `AllowConsoleSpawn = true` in config restores it without a code change.

---

## 11. Configuration additions

New entries in `CompanionConfig` (all server-synced via `IsAdminOnly = true` except hotkeys and the nameplate distance, which are client-local):

```
[Hotkeys]
  RosterToggle : KeyboardShortcut = L
  CompanionPanelInteractKey : KeyboardShortcut = E      // already used in Phase 1 for tame-promote; reused as the hold-interact for our wolf

[Wolf Companion]
  NameplateMaxDistance : float = 18
  NameplateOwnerOnly   : bool  = true

[Home]
  DismissBehavior      : enum {Vanish, RunHome} = RunHome
  DismissTimeoutSeconds: int = 60
  BindBedRadiusMeters  : float = 5
```

Phase 1's `Wolf.NameplateVisible` is repurposed: it now toggles the nameplate component entirely (was reserved as a stub in Phase 1). Default stays `true`.

---

## 12. Localization keys (Phase 2 additions)

| Key | English |
| --- | --- |
| `valhein.ui.roster.title` | "Companions" |
| `valhein.ui.roster.empty` | "No companions yet. Try `valhein_spawn wolf`." |
| `valhein.ui.roster.cap_label` | "Cap {0}/{1}" |
| `valhein.ui.roster.goto` | "Go to" |
| `valhein.ui.roster.open` | "Open" |
| `valhein.ui.panel.rename` | "Rename" |
| `valhein.ui.panel.order` | "Order" |
| `valhein.ui.panel.home` | "Home" |
| `valhein.ui.panel.set_here` | "Set here" |
| `valhein.ui.panel.bind_bed` | "Bind to bed" |
| `valhein.ui.panel.dismiss_confirm` | "Dismiss {0}? This is permanent." |
| `valhein.audit.renamed` | "{0} renamed to {1}" |
| `valhein.audit.home_set` | "Home set" |
| `valhein.audit.home_bound_bed` | "Bound to bed" |
| `valhein.audit.dismissed_via_home` | "Returned home and dismissed" |

Existing Phase 1 keys are unchanged.

---

## 13. Harmony patches

Two additions to Phase 1's patch list:

| Target | Type | Purpose |
| --- | --- | --- |
| `Player.Update` (or local `InputManager` if Jotunn provides it) | Postfix | Listen for `Hotkeys.RosterToggle` and toggle the roster panel. Single guard against vanilla console / UI input modal. |
| `Hud.SetVisible` | Postfix | Hide/show nameplates when the vanilla nameplate-toggle changes. |

The Phase 1 Tameable / OnDeath / Damage / ZNetScene / Humanoid.UseItem patches are unchanged.

---

## 14. Open questions / risks

| Tag | Question | Resolution proposed in this spec |
| --- | --- | --- |
| Q2.1 | Roster hotkey — is `L` actually free? | `L` is unbound in vanilla Valheim 0.218+. Spec sets default `L`; spec author should grep top-3 popular Valheim mods on Thunderstore (Equipment Toolbar, AzuClock, etc.) for `L` conflicts before merge. Trivially user-rebindable via config (AC2.3). |
| Q2.2 | Home precision — bed ZDO vs world position? | **Both.** `valhein.homePos` is the always-set primary; `valhein.homeBedZdoId` is optional and takes precedence at resolve time when valid. Captures the durability of bed-binding without forcing it. |
| R2.a | GUIManager API compatibility 2.20.3 ↔ 2.29.0 | Mitigated by limiting calls to `GUIManager.OnCustomGUIAvailable`, `CreateWoodpanel`, `CreateText`, `CreateButton`, `CreateInputField`, `CreateDropDown`, `BlockInput`. All stable since Jotunn 2.5. If a `MissingMethodException` shows, bump NuGet ref per `CLAUDE.md`. |
| R2.b | TextMeshPro on multiple companions perf | Per §5 budget. Fallback to legacy `TextMesh` documented; switch behind `Wolf.NameplateRenderer : enum {TMP, Legacy} = TMP`. |
| New | UI input blocking — typing a name in the rename field shouldn't fire vanilla movement keys | Wrap the input field's focus in `GUIManager.BlockInput(true/false)` on focus/blur. |
| New | Hot-rebinding the roster hotkey at runtime (AC2.3) | The patch reads `Hotkeys.RosterToggle.Value.MainKey` each tick — no caching. Cheap (1 hashed lookup) and naturally picks up config changes. |

---

## 15. Acceptance-criteria traceability

| AC | Spec section | Implementation file(s) |
| --- | --- | --- |
| AC2.1 | §4.3 (rename flow) + §5 (Nameplate) | `CompanionPanel.cs`, `Nameplate.cs`, `Spawner.Rename` |
| AC2.2 | §6.1 (resolve fallback) + §6.3 (warning log) | `HomeAnchor.cs`, `CompanionPanel.cs` (bind action) |
| AC2.3 | §13 + §14 R2.a notes (hot-read hotkey) | `HarmonyPatches/PlayerHotkeyPatch.cs` |
| AC2.4 | §9 migration + §3.1 default-safe ZDO keys | `Migration/PhaseMigrations.cs` + unit tests |

Phase 1 acceptance criteria (AC1.1–AC1.6, F1.11–F1.14, N1.1) must remain green. Phase 2 implementation PR should re-run the Phase 1 manual checklist (§19.2 of Phase 1 spec).

---

## 16. Implementation order

Same review-friendly cadence as Phase 1: each step ends in a build that loads in r2modman without errors.

1. **Registry refactor** (§8) + shim, no behavior change. Easy diff to review.
2. **Migration** scaffolding (§9) + the two unit tests. No-op for fresh installs; covers AC2.4.
3. **Home** ZDO keys + `HomeAnchor` resolution. Wire `Stay` fallback. No UI yet.
4. **Nameplate** (§5) on the wolf prefab. Visible on next spawn.
5. **`WolfInteractable`** + hold-E plumbing (`Interaction/WolfInteractable.cs`). Empty panel for now.
6. **`CompanionPanel`** (§4.3) without rename — read-only audit + order dropdown.
7. **Rename** flow (`Spawner.Rename` + UI input + nameplate refresh) — earliest point AC2.1 lights up.
8. **Home UI** (Set here / Bind) — closes out AC2.2.
9. **`RosterPanel`** (§4.2) + hotkey wiring — closes AC2.3.
10. **`HomePathing.Dismiss`** (§6.2) — RunHome behavior + 60 s timeout.
11. **Acquisition default flips** (§10) — last so the rest of the test path stays hospitable.
12. **Localization table** additions (§12).
13. **Phase 1 regression pass** + Phase 2 manual acceptance.

Suggested PR boundary: steps 1–4 in one PR (foundation), 5–10 in one PR (UI + home), 11–13 in the closing PR.

---

## 17. Deliverables

1. **This document** — design doc. ✅
2. **Test plan** — manual checklist mirroring §19.2 of Phase 1, plus the two codec migration unit tests in §9.
3. **Changelog entry** for `Package/manifest.json`:
   > Phase 2: Companion polish — roster UI (default hotkey `L`), per-companion interaction panel (hold-E), floating nameplate honoring vanilla toggle, home anchor (world pos or bound bed), audit log surfaced in the panel. Whistle and tame-promotion now default-on; console spawn now default-off.
4. **`CLAUDE.md` updates** — none expected (build pipeline unchanged). If the TextMeshPro decision shifts to legacy `TextMesh`, document under "Dev gotchas."
5. **Requirements doc updates** — close out Q1.2 (appearance seed wired). Phase 2 §6.2.5's R2.a and R2.b stay open until validated in implementation.
6. **README.MD updates** — add `valhein_rename` to the commands table, mention the roster hotkey, note the acquisition default flip.

---

## 18. Out of scope reminder

The following requirements-doc items are touched only in passing and **deferred**:

- F1.6 — equipment / inventory on wolf (deferred to a Phase 5-ish "combat companions" pass).
- F2.6 task-assignment audit code — placeholder only; Phase 3 lights it up.
- Phase 6 server-sync of new configs — entries are already marked `IsAdminOnly`; no code work in Phase 2.
- Phase 5 appearance art passes — Phase 2 stops at "deterministic tint from seed."

---

## 19. Effort estimate (rough)

| Step | Estimate |
| --- | --- |
| Registry refactor + migration + unit tests | 0.5 day |
| Home anchor + Stay fallback + HomePathing | 1 day |
| Nameplate + visibility wiring | 0.5 day |
| WolfInteractable + CompanionPanel base | 1 day |
| Rename UI + audit codes | 0.5 day |
| RosterPanel + hotkey | 1 day |
| Localization + polish + manual acceptance | 0.5 day |
| **Total** | **~5 days** |
