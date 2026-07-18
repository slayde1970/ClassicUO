# PRD: GPU-Resident Chunk-Mesh Render Redesign (Tier 4 #13)

Status: **Design in progress - not yet implemented.**

## 1. Problem

`TileRenderer` issues one `batcher.Draw()` call per land tile and per static,
every frame, for the entire view range - measured at ~19,900 `Draw()` calls
in a dense town plaza (5,352 land + 14,567 static + 1,831 of the land count
being stretched/texmap tiles). `Batcher2D.Flush` already coalesces
*consecutive* same-texture calls into one real GPU submission, so the actual
GPU draw count there was ~2,386 (10 flushes + 2,376 texture switches) - far
fewer than 19,900, but still high, and a large fraction of those switches
come from a specific, confirmed cause: `Texmap` (stretched/rocky land) uses
its **own separate `TextureAtlas`** (2048x2048) from `Art`'s (4096x4096,
used for flat land + all statics). TEF's per-tile diagonal draw order
interleaves stretched and flat land tile-by-tile, forcing a texture bind
switch every time a stretched tile sits next to a flat one - which is
common in mixed terrain. On identical content/zoom/window, the real
ClassicUO.Client renders the same scene at ~249 FPS vs TEF's ~80-99 FPS.

`ClassicUO.Client` solves this with `ChunkMesh`: persistent, texture-bucketed
GPU vertex buffers built once per 8x8 chunk and rebuilt only when the
chunk's static/land composition actually changes, with a real GPU depth
buffer replacing per-tile draw-order occlusion. This PRD scopes an
equivalent for TEF, informed by reading the real implementation in depth
(`ChunkMesh.cs`, `GameSceneDrawingSorting.cs`, `View.CalculateDepthZ`)
rather than guessing at the mechanics.

## 2. Goals

- Persistent, texture-bucketed per-block (8x8, matching `WorldMap.BlockSize`)
  GPU vertex/index buffers for **map land + map statics only**, rebuilt only
  when that block's composition changes (which, for TEF today, is
  essentially never after initial load/read - see 4.6).
- A real GPU depth buffer for occlusion, replacing the current manual
  back-to-front painter's-algorithm sort for the meshed content. This is a
  hard requirement, not a nice-to-have: texture-bucketing sprites from
  different depths into one draw call is only visually correct if a
  hardware Z-test - not draw order - determines what's in front.
- Meaningfully close the ~3x FPS gap on the same dense-plaza test scene
  used throughout this investigation.
- No regression in the depth-sorting correctness this project already
  fixed three times over (fountain-over-pavers, river-over-hillside,
  player-occlusion - see `next-steps.md` Tier 1 items 1-2's history).

## 3. Non-goals (explicitly deferred)

- **Animated statics, foliage, trees, and rocks are excluded from the
  mesh**, matching `ChunkMesh.IsStaticExcludedFromMesh` exactly. They stay
  on today's per-object draw path (`TileRenderer`'s existing per-static
  `batcher.Draw` loop, `AnimatedStatics.CurrentOffset` unaffected). This is
  not a compromise - the real client does the same thing, because these
  need per-frame UV/texture changes a fixed-slot mesh quad can't cheaply
  express.
- **Entities (`EntityRenderSystem`) and the player are not meshed.** They
  stay on the existing per-object draw path, same as real-client mobiles.
  They still need to correctly occlude/be-occluded against meshed geometry
  - achieved by writing the same depth-key formula into their vertex Z
    (see 4.4), not by baking them into a block's mesh.
- **No incremental per-slot mesh patching.** Any dirty trigger rebuilds the
  whole block's mesh (both counting passes), matching `ChunkMesh.Build`'s
  coarse, structural-only dirty model. Acceptable because TEF's map data
  is read-only today - see 4.6 for why this makes TEF's case *simpler*
  than the real client's.
- **The deferred "harvest map-static trees" mechanic** (suppress a map
  static, spawn a stand-in entity - see the note in `next-steps.md`) is not
  implemented here, but this PRD's dirty-invalidation hook is exactly what
  that mechanic will need later (marking a block's mesh dirty when a
  static is suppressed/restored). Noted, not built.
- **No transparent/translucent object handling.** The real client keeps a
  separate CPU-drawn list for fading/CoT/translucent objects specifically
  because meshed (depth-writing) objects would otherwise block them
  incorrectly. TEF has no such objects today (`IsPartialHue` only affects
  hue, not alpha) - revisit only if one is actually needed later.
- **Mouse picking is not accelerated by the mesh** - see 4.5. It becomes a
  separate CPU-side pass, matching the real client (which also never made
  picking cheaper via meshing - only drawing).
- **Chunk-mesh GPU buffer eviction policy left simple**: tied to the same
  trigger as `WorldMap.EvictFarBlocks` (4.7), not a separately-tuned radius,
  unless testing shows GPU memory pressure warrants one.

## 4. Design

### 4.1 Data model: one `BlockMesh` per 8x8 block

A new `BlockMesh` class (rendering-only, owned alongside `WorldMap`'s
existing per-block caches, not inside `WorldMap.StaticTile` itself) holds
two `MeshLayer`-equivalents - `Land` and `Statics` - each a flat parallel
array structure:

```
Vertices: PositionNormalTextureColor4[]   // reused from ClassicUO.Renderer - already used by DrawStretchedLand, includes per-corner Normal
Textures: Texture2D[]                     // one per quad, for bucketing
Visible:  bool[]                          // per-quad show/hide, no rebuild needed to toggle
VertexBuffer: DynamicVertexBuffer
Count: int
```

Building a layer is a two-pass bucket-insert (mirrors `TextureBucketTracker`
+ `ChunkMesh.CountLand`/`TryAddLand`): pass 1 counts quads per distinct
texture (an Art atlas page, or the separate Texmap atlas - see 4.2), pass 2
writes each quad into its texture-grouped slot. This directly fixes the
confirmed texmap/Art-atlas interleaving problem from section 1: all
same-texture quads in a block end up contiguous in the vertex buffer
regardless of their original tile-diagonal draw order, so `Batcher2D.Flush`
(or a comparable draw call here) coalesces them into far fewer real GPU
submissions per block.

### 4.2 Texture bucketing across two atlases

Flat land and statics sample `Art`'s atlas (`assets.Art.GetLand`/`GetArt`);
stretched land samples `Texmap`'s separate atlas
(`assets.Texmaps.GetTexmap`). Both are valid `Texture2D` bucket keys in the
same `Land` layer - the two-pass bucketing naturally groups all Art-atlas
land quads together and all Texmap-atlas quads together, wherever they fall
in the block, eliminating the ping-pong entirely rather than working around
it.

### 4.3 Dirty tracking

A block's mesh is built lazily on first draw (or block-cache load) and
marked dirty only by:

- The block being newly read from disk (fresh entry in `WorldMap`'s
  existing block/statics cache).
- (Future) a static being suppressed/restored by the not-yet-built
  harvest-map-static mechanic.

Nothing else invalidates it - matching `ChunkMesh`'s coarse, structural-only
model (`MarkDirtyIfNeeded` only fires on add/remove, never on hue/fade/
selection changes in the real client). TEF has no hue-changing or
fade/translucency logic on map statics today, so there is currently no
"patch in place without a rebuild" case to handle at all for this first
pass - simpler than the real client's equivalent.

### 4.4 Depth-key formula (the part that must not be guessed wrong)

Read directly from `ClassicUO.Client`'s `View.CalculateDepthZ()`
(`Game/GameObjects/Views/View.cs:35-84`):

```
depth = (x + y) + (127 + priorityZ) * 0.01f
```

where `x, y` are the object's tile coordinates (with a small +1 nudge to
the neighbor-facing coordinate based on which screen-space quadrant the
object's sub-tile `Offset` currently leans toward - irrelevant for meshed,
tile-locked land/statics, which never have an offset; relevant only for
the player's continuous movement, see below) and `priorityZ` is the same
adjusted-Z concept TEF already computes (`WorldMap.ComputePriorityZ`) -
**this is nearly identical to TEF's existing sort key** (`sum = tx+ty` for
the diagonal walk, `PriorityZ`/`ReadOrder` for in-tile tiebreaking), just
repurposed as a literal vertex Z value consumed by a real depth test
instead of a `List.Sort` comparison. TEF adopts the same formula, using
`ReadOrder` as a further sub-`0.01` tiebreak where needed (matching how
`ReadOrder` already breaks `PriorityZ` ties in `GetBlockStatics`).

Every draw this frame - meshed land/statics AND the still-per-object
player/entities/animated statics - writes its depth via this same formula
into `Position.Z`, so the hardware Z-test (not draw order) resolves
occlusion consistently across both paths. `GraphicsDeviceManager`'s
already-set `PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8`
(currently unused) becomes actually used; a `DepthStencilState` with
`DepthBufferEnable = true` replaces TEF's current 2D default during the
world pass only (screen-space UI/HUD passes stay depth-disabled, matching
`Batcher2D`'s existing default 2D stencil state).

**Do not implement the offset-quadrant nudge for the player/entities on
the first pass** - TEF has no sub-tile bouncing effects yet (real client's
nudge exists for smoothly-moving/bouncing objects mid-tile); use
`Math.Floor(WorldPosition)` for the player's depth-key tile coordinate,
same quantization `TileRenderer.Draw` already uses for `centerX`/`centerY`.
Revisit only if a visible occlusion glitch appears at a tile boundary
while moving.

### 4.5 Mouse picking becomes its own pass, decoupled from drawing

Meshed statics no longer get individually visited by a draw call, so
`TestStaticPick`/`TestLandPick` (currently side effects of the per-object
draw loop) can't stay where they are. `WorldMap`'s block/statics cache
already holds everything picking needs (`GetStaticsAt`, `TryGetLand`)
completely independent of whether that data is meshed for rendering -
matching what the real client actually does (`AddTileToRenderList` still
visits every object's `CheckMouseSelection` for CPU hit-testing regardless
of whether it took the mesh fast path for drawing). TEF's picking logic
moves into its own walk over the same view-range tiles (no draw calls
emitted), using `Art.PixelCheck` exactly as today. This is a clean
decoupling, not a hack - entities/player picking is entirely unaffected
since they were never meshed.

### 4.6 Why TEF's case is simpler than the real client's (for now)

The real client's chunks hold mutable, server-driven, frequently-changing
object sets (players placing/moving items, monsters spawning). TEF's map
data is read straight from disk and **never mutates** today (no
build/place system, no harvest-map-static suppression yet). So a
`BlockMesh`, once built, is realistically permanent for this pass - the
only "dirty" event is the initial build. This substantially de-risks the
redesign versus the real client's problem, which had to solve continuous
churn; TEF only has to solve "build once, reuse forever until evicted."

**This won't stay true forever.** The user has described a future player
building system (claim a plot, build houses, plant gardens - land tiles
themselves stay fixed, no terraforming planned, but trees will likely be
choppable to clear space for building). Both "place a building/garden
static" and "chop down a map-static tree" are exactly the kind of
structural mutation that would need to mark a block's `BlockMesh` dirty
(4.3) - the dirty-invalidation hook this PRD builds is deliberately the
same hook that future mechanic will need, matching the note already in
`next-steps.md`'s harvest-map-static section. Not designed further here -
just confirming this PRD's dirty model doesn't paint that future feature
into a corner.

### 4.7 Interaction with existing chunk-cache eviction

`WorldMap.EvictFarBlocks` (Tier 3 #8) already evicts far-away
`MapBlock?`/statics-list cache entries. `BlockMesh` GPU resources (vertex
buffers) for the same block get disposed at the same trigger point, so
there's one eviction radius/policy to reason about, not two independently
tuned ones.

## 5. Acceptance criteria for this phase

- [ ] Same dense-plaza test scene: FPS measurably improves over the
  current ~94-99 FPS baseline (exact target TBD - report the real number,
  don't pre-commit to a specific figure before measuring).
- [ ] GPU flush/texture-switch count (debug HUD) drops meaningfully for
  that scene, confirming the texmap/Art-atlas ping-pong is actually fixed,
  not just moved around.
- [ ] No regression in the three historical depth-sorting bugs this
  project already fixed: fountain-over-pavers, river/water-over-hillside,
  player occlusion (walk behind/in-front of statics and trees correctly at
  the exact spots those bugs originally showed up, per `next-steps.md`
  Tier 1 items 1-2's history).
- [ ] Mouse picking (hover names, harvest-click) still works identically
  after being decoupled from drawing - verified at the same fountain/tree
  spots used throughout this session's picking work.
- [ ] Animated statics (fountains, torches) still animate correctly -
  unaffected since excluded from the mesh.
- [ ] Day/night ambient overlay (`DayNightOverlay`) still composites
  correctly - it's a post-process over the finished frame, independent of
  this change, but worth an explicit re-check given it also touches
  render-target/blend-state plumbing.
- [ ] Chunk-cache eviction (F-key toggles, "Blocks cached" HUD line) still
  behaves correctly with `BlockMesh` disposal wired to the same trigger.

## 6. Open questions carried forward (not blocking this phase)

- Exact eviction radius/policy for `BlockMesh` GPU resources if profiling
  later shows they should differ from the data-cache radius.
- Whether the offset-quadrant depth nudge (4.4) is ever needed once real
  gameplay (moving NPCs, thrown effects) exists - deferred until an actual
  visible glitch justifies it.
- How the harvest-map-static suppression mechanic (still unbuilt) will
  trigger a `BlockMesh` dirty rebuild when it lands - the hook point (4.3)
  is designed to support it, but the mechanic itself is out of scope here.
