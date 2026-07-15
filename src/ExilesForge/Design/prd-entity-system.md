# PRD: Entity System (Tier 1 #3)

Status: **Implemented and verified.** All files listed in this doc exist
under `World/Entities/`; `TileRenderer.DrawStaticsAt` does the three-way
merge described in 4.6; a debug `Harvestable` tree is spawned in `WorldScene`
and confirmed correctly depth-sorted (player walks behind it) with working
deplete/respawn via a left-click debug trigger.

## 1. Problem

`TileRenderer`/`WorldMap` render land and statics; `PlayerEntity` is a
one-off, hand-written class. There is no general way to represent world
objects that aren't the player or raw map data — resource nodes (trees,
rocks), and eventually NPCs and placeables. This is the last piece needed
before Tier 2 (mouse picking, UI) and the first real gameplay loop ("walk up
to a tree, click it, chop it, get wood").

## 2. Goals

- A general entity representation that resource nodes (this phase) and NPCs
  (later) can both use.
- Renders correctly interleaved with land/statics/player in
  `TileRenderer`'s existing back-to-front pass — no new occlusion bugs.
- Shaped so it doesn't fight a future co-op/multiplayer pass. Not
  implementing networking now — just not painting ourselves into a corner.

## 3. Non-goals (explicitly deferred)

- **No third-party ECS library** (e.g. DefaultECS). Hand-roll the minimal
  version now; the API shape below (ID-based entities, data-only components,
  plain systems) is deliberately the same shape those libraries expose, so
  adopting one later is a swap, not a rewrite. Revisit only if we hit an
  actual pain point (query performance, entity counts in the thousands).
- **`PlayerEntity` is not migrated into the registry this phase.** It stays
  its own class. Once the entity system is proven against resource nodes,
  folding the player in (as an entity with a `PlayerControlled` marker) is a
  small, well-understood follow-up — not worth doing before the pattern is
  validated against a second use case.
- **No NPC movement / fractional sub-tile position.** This phase's entities
  (resource nodes) are stationary and tile-aligned, like statics. `Transform`
  is still shaped so this doesn't require a breaking change later (see 4.1).
- **No animated statics work.** Investigated `ClassicUO.Client`'s
  `AnimatedStaticsManager` — it's a *global, per-graphic-ID* frame table
  (not per-instance), so it's entirely orthogonal to the entity system.
  Stays a Tier 4 rendering-layer item.
- **No persistence / save-load.** Tier 3. Data shapes below (see 4.3) are
  chosen to not actively work against it, but nothing is serialized yet.
- **No actual networking code.** Just avoid components with behavior
  methods or direct references to `Game`/`TileRenderer`/`WorldMap`, so nothing
  here assumes single-player-only.

## 4. Design

### 4.1 Registry

- `EntityId` — a plain `int`, handed out by a single `EntityWorld` registry.
  This registry is the seed of an "authority" concept for later networking:
  whoever owns it decides what entities exist.
- Components are stored **per-type**, not as a dictionary-of-dictionaries:
  `Dictionary<EntityId, T>` per component type. Adding a new component type
  never touches existing storage; `TryGet<T>(id)` / `Has<T>(id)` are simple
  lookups.

### 4.2 Components (this phase)

All components are plain data. No `Draw()`, no `Update()`, no references to
engine types (`Game`, `TileRenderer`, `WorldMap`). Behavior lives entirely in
systems (4.4).

```csharp
struct Transform
{
    Vector2 WorldPosition; // fractional tile coords, same convention as PlayerEntity.WorldPosition
    sbyte Z;
}

struct Appearance
{
    ushort Graphic;
    ushort Hue;
    byte Height; // mirrors StaticTiles.Height - see 4.5 (priority) and 4.6 (render merge)
}

struct Harvestable
{
    ResourceType Resource;      // enum: Wood, Stone, ... (define alongside this)
    int YieldRemaining;
    int YieldMax;
    ushort AvailableGraphic;    // Appearance.Graphic value when not depleted
    ushort DepletedGraphic;     // Appearance.Graphic value when depleted (e.g. a stump)
    bool IsDepleted;
    float RespawnCountdown;     // seconds remaining; only meaningful while IsDepleted
    float RespawnDuration;      // seconds, reset onto RespawnCountdown when depleted
}

struct Interactable
{
    // marker only, no fields yet - Has<Interactable>(id) is the whole check
}
```

`Transform.WorldPosition` is a `Vector2` (not integer tile coords) even
though this phase's entities never move, specifically so NPC movement later
doesn't require changing the component shape - it'll just start being
written to by a movement system instead of only at spawn time.

### 4.3 Respawn timer: countdown, not absolute tick

`RespawnCountdown` is decremented by `Time.Delta` each frame in
`HarvestSystem.Update()` — **not** an absolute future tick
(`RespawnAtTick = currentTick + duration`). Rationale: Tier 3's game clock
doesn't exist yet, and an absolute-tick value becomes meaningless the moment
the clock source changes or a save is reloaded with a different "tick 0". A
countdown is clock-agnostic now and trivially save/load-friendly later —
serialize the remaining duration, resume, no translation needed.

Explicitly **not decided**: whether resources should respawn while the game
isn't running ("while you were away"). A pure countdown does not do this
unless something later fast-forwards it using elapsed real time on load.
Flag for Tier 3 persistence design, not this phase.

### 4.4 Systems

Plain classes, no scheduler/dependency graph - called explicitly and in a
fixed order from `WorldScene.Update`/`Draw`.

- **`HarvestSystem`** — owns the harvest action (triggered later by
  mouse-picking + an interact input) and the respawn countdown tick. On
  harvest: decrement `YieldRemaining`; at 0, set `IsDepleted = true`,
  `RespawnCountdown = RespawnDuration`, and write `Appearance.Graphic =
  DepletedGraphic`. Each update: for depleted entities, count down; at 0,
  reset `YieldRemaining = YieldMax`, `IsDepleted = false`, write
  `Appearance.Graphic = AvailableGraphic`. The render system never inspects
  `Harvestable` at all — it only ever reads whatever `Appearance.Graphic`
  currently says.
- **`EntityRenderSystem`** — see 4.6.

### 4.5 Priority (render + pick order)

Entities need a `priorityZ` to interleave correctly with map statics
(`WorldMap.ComputePriorityZ`) and the player (`Z + 1`, matching
`Chunk.AddGameObject`'s mobile rule). Computed the same way statics already
are, from `Appearance.Height` (mirroring `StaticTiles.Height` — this is why
`Appearance` carries `Height` instead of inventing a one-off rule per entity
type):

```
priorityZ = Z + (Height != 0 ? 1 : 0)
```

Extend later (e.g. a `Background`-equivalent) only if a concrete component
actually needs it - don't pre-add flags speculatively.

### 4.6 Render integration: merge into the existing tuple shape, no interface

`WorldMap.StaticTile` is `{ Graphic, Hue, Z, PriorityZ, ReadOrder }`.
`EntityRenderSystem.GetAt(tx, ty)` returns entities at that tile in the
**same tuple shape** (an `EntityStaticTile`-equivalent, or reuse
`StaticTile` directly if the fields line up). `TileRenderer.DrawStaticsAt`
changes from a two-way merge (map statics + player) to a **three-way merge**
(map statics + entities + player), still ordered by `PriorityZ`.

No `IRenderable`/`ITileOccupant` interface for this phase - deliberately.
Components stay pure data; the render system's whole job is projecting
entity component data into the same tuple shape `WorldMap` already produces,
and `DrawStaticsAt` doesn't need to know or care whether a given tuple came
from disk or from live entity state. Revisit this decision if/when an entity
needs custom per-instance draw logic (e.g. an animated NPC) that a flat
`(Graphic, Hue, Z)` tuple can't express.

**Caching differs from `WorldMap` on purpose.** `WorldMap`'s static block
cache is safe to keep forever - mul data never changes. Entities are
mutable (harvested, spawned, despawned later), so `EntityRenderSystem`
cannot reuse that persistent-cache pattern. It rebuilds its per-tile lookup
(`Dictionary<(int,int), List<EntityId>>`) fresh every `Draw` call, filtered
to the current view-range bounding box. Fine at expected entity counts
(tens to low hundreds for a single-player game); revisit if that changes.

### 4.7 Mouse picking (Tier 2 groundwork, not built this phase)

Not implementing this yet, but the entity system's render integration is
designed to make it close to free when Tier 2 arrives: `ClassicUO.Client`
does picking as a side effect of drawing (`Art.PixelCheck` /
`PixelPicker.Get` against a per-pixel alpha mask), not a separate spatial
query - the frontmost thing under the cursor wins for free because draw
order already is depth order. Land tiles don't need per-pixel testing (a
`|dx| + |dy| <= 22` bounds check against the known diamond shape suffices).

Since entities already merge into the same per-tile candidate list as
statics for rendering (4.6), they show up "for free" as pickable candidates
once `TileRenderer.Draw` grows a `Point? mouseScreenPos` parameter and a
`PickResult` output - no separate entity-picking path needed. Not building
this now; noting it so 4.6 isn't accidentally shaped in a way that makes it
harder later.

## 5. Acceptance criteria for this phase

- `EntityWorld` + the four components above exist and compile.
- At least one `Harvestable` resource node (e.g. a tree) is spawned in
  `WorldScene`, renders correctly interleaved with land/statics/player
  (occludes and is occluded correctly - walk behind it, stand in front of
  it), and its `PriorityZ` is derived from `Appearance.Height` per 4.5.
- `HarvestSystem` can deplete and respawn that node's state end-to-end
  (even without a player-facing harvest *action* yet — a debug key or direct
  call is fine to prove the countdown/graphic-swap logic works).
- No regression in existing land/static/player depth sorting.

## 6. Open questions carried forward (not blocking this phase)

- Whether resource respawn should progress while the game is closed
  ("while you were away") - Tier 3 persistence decision.
- Whether/when to introduce a render interface for entities with
  per-instance draw logic (animated NPCs) - revisit when the first such
  entity is actually needed, not before.
