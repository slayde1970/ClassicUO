# The Exile's Forge — Next Steps

Roadmap for the world/engine foundation still needed before gameplay design and
survival/crafting features can be built on top. Ordered by how much each item
unblocks; items within a tier can be reordered but the tiers themselves are
dependency-ordered.

Current status (as of this writing): asset loading, rendering, action-mapped
input, audio, an animated player with 8-way facing, land/static/stretched-
terrain rendering, collision + player Z tracking, depth-sorted player/statics,
a working entity system, and mouse picking (land/static/entity/player) are
all done. See `World/TileRenderer.cs` for the current known gaps in the
renderer itself.

## Tier 1 — Core world interaction

Gameplay can't really start without these; they define the world model
everything else builds on.

1. ~~**Collision & walkability + player Z tracking**~~ **[DONE]**
   Extracted a shared `World/WorldMap.cs` data layer (persistent block/statics
   cache) used by both the renderer and gameplay. Player stands on the surface
   Z, and movement is blocked into impassable land (water/mountains) and
   Surface/Impassable statics, with wall-sliding on blocked diagonals.
   `WorldMap.TryGetStandZ` is the walkability check. Height changes snap
   instantly (an eased visual transition was tried and reverted — see Tier 4
   item 11).
   - Remaining polish (deferred, "good enough for now"): a few minor collision
     edge cases; `MaxStepUp` climb allowance is a tunable constant that could
     be refined; `WorldMap`'s block cache has no eviction yet (folds into the
     chunk-cache item).

2. ~~**Depth sorting: player/entities interleaved with statics**~~ **[DONE]**
   The player draws inside `TileRenderer`'s back-to-front pass, interleaved
   on its own tile at `priorityZ = Z + 1` (matching `Chunk.AddGameObject`'s
   mobile priority) so statics in front of or above it correctly occlude it.
   Uncovered and fixed two related bugs along the way: (a) the world's iso
   origin was missing the same "-22" screen-position bias every land/static
   tile applies (`GameObject.UpdateRealScreenPosition`), causing the player
   to render offset from everything else; (b) see Tier 4 item 11 for the
   eased-Z-transition clipping bug this also exposed.

3. ~~**Entity system**~~ **[DONE]** — see `Design/prd-entity-system.md` for
   the full design. Hand-rolled ID-based entity registry (no third-party ECS
   library), data-only components (`Transform`, `Appearance`, `Harvestable`,
   `Interactable` in `World/Entities/`), plain systems (`HarvestSystem`,
   `EntityRenderSystem`), and a three-way merge into `TileRenderer`'s
   back-to-front pass (map statics + entities + player) using the same tuple
   shape as `WorldMap.StaticTile` - no new render interface needed. Verified:
   a debug `Harvestable` tree renders correctly depth-sorted (player walks
   behind it) and deplete/respawn (tree -> stump -> tree) works end to end
   via a left-click debug trigger in `WorldScene`. `PlayerEntity` stays
   separate from the registry for now, per the PRD's deferred scope.

## Tier 2 — Interaction & presentation surface

4. ~~**Mouse picking (tile + object under cursor)**~~ **[DONE]**
   Rides the existing back-to-front render pass rather than a separate
   spatial query, matching ClassicUO's `SelectedObject`: every candidate
   drawn under the cursor is hit-tested as it's drawn, and since the pass is
   back-to-front, the last (frontmost) hit wins for free. Statics/entities
   use per-pixel alpha (`Art.PixelCheck`, keyed by graphic id); land uses a
   cheap diamond bounds test (no per-pixel needed - it's a solid known
   shape); the player uses a bounding-box test (a first attempt used
   `Animations.PixelCheck`, the animation-frame equivalent, but its key has
   more moving parts - frame numbering, body-conversion lookups - than
   Art's plain per-graphic-id key, and it never registered a hit despite the
   geometry matching exactly; not confidently debuggable without runtime
   introspection, and a bounding box is an acceptable simplification for a
   mostly-opaque standing humanoid).

   Two independent results come out of `TileRenderer.Draw` each frame -
   `entityPick` (topmost live entity or the player, if either is under the
   cursor) and `tilePick` (topmost map static, falling back to land - map
   data only, never an entity) - rather than one merged "winner", so game
   code can ask each question separately (e.g. a harvest action wants the
   entity; a "walk here" click wants the ground tile regardless of what's
   standing on it). See `World/PickResult.cs`. Verified: hovering shows the
   correct name (tiledata name for land/statics, "Player" for the player) in
   the title bar for land, statics, entities, and the player; left-click
   harvests the specific tree entity under the cursor, replacing the earlier
   debug-key wiring.

5. ~~**UI / HUD layer**~~ **[DONE]** — `UI/DebugHud.cs`: a minimal
   screen-space overlay (its own `batcher.Begin()/End()`, no camera matrix)
   with a `Panel`-style dark translucent background sized to fit its text.
   FPS counter (`Time.Fps`, tracked in `GameController.Draw`) always renders
   as the top line when shown, with the tile/entity-under-cursor readout
   (from `TileRenderer`'s picking) stacked below it - each independently
   toggleable (F8 debug info, F9 FPS; both default on), not tied together.

   Also built the general-purpose lightweight control/gump system this item
   needed beyond the narrow HUD: `UI/Control.cs` (base - local X/Y resolved
   to screen space each frame, children, visibility, a `HitTestVisible` flag
   so purely decorative controls like `Label` can't steal hover/click from
   an interactive parent underneath them), `UI/UIManager.cs` (owns root
   controls in Z order, recursive frontmost-wins hit-testing, routes
   hover/click, exposes `IsMouseOverUI` so world input - e.g. click-to-
   harvest - can be suppressed when a click actually lands on UI),
   `UI/Controls/Label.cs`, `Panel.cs` (solid-color background via
   `SolidColorTextureCache`), and `Button.cs` (Panel + Label + hover/click).
   Verified end to end with a real "Resources" panel in `WorldScene`
   (top-right corner, 5px margin from the screen edge, "Resources" title,
   a live "Wood: N" counter that increments on harvest, and a "Reset"
   button whose hover highlight and click both work correctly - including
   while hovering directly over its label text - without also triggering a
   harvest on a tree behind the panel). This is the foundation inventory
   windows, crafting menus, survival bars, and tooltips will build on.

## Tier 3 — Simulation & durability

6. ~~**Game clock + simulation tick**~~ **[DONE]** — `Core/SimulationClock.cs`,
   a fixed-step accumulator (20 Hz, `MaxTicksPerFrame` guard against the
   spiral of death) decoupled from the variable render frame rate.
   `Core/WorldClock.cs` tracks in-game day/time (`Day`, `Hour`, `Minute`,
   12-hour `Hour12`/`MeridiemTag`), advanced only on the fixed tick;
   `DayLengthSeconds` is a tunable placeholder (day/night lighting itself is
   still Tier 4). `Scene.FixedUpdate(float)` is a new virtual hook, wired
   through `SceneManager` and driven from `GameController.Update` alongside
   `WorldClock.Advance`. `HarvestSystem.Update` (tree respawn timers) moved
   off `Update`'s variable `Time.Delta` and onto this fixed tick in
   `WorldScene.FixedUpdate`, so respawn timing no longer depends on
   framerate. Verified: the debug HUD's "Time: Day N HH:MM AM/PM" line
   counts up correctly, and chopping a tree still respawns it after ~10s.

7. ~~**Persistence (save/load)**~~ **[DONE]** — see `Design/prd-persistence.md`
   for the full design. `Persistence/SaveData.cs` (flat DTOs, independent of
   runtime types) + `Persistence/SaveManager.cs` (JSON via
   `System.Text.Json`, one save slot at `%AppData%\ExilesForge\save.json`).
   `WorldScene` gained a `SaveData`-accepting constructor overload,
   `RestoreFromSave`, and a public `SaveGame()` - saves happen on a 60s
   autosave timer (render `Delta`) and on clean exit
   (`GameController.OnExiting`). New `Scenes/TitleScene.cs` (New Game /
   Continue - Continue only shown if a save exists), built entirely on the
   Tier 2 control/gump system - its first real use beyond the `WorldScene`
   demo panel - including a confirm-before-wipe dialog when New Game is
   clicked with an existing save. `Program.cs` now starts on `TitleScene`.
   Explicitly deferred (see PRD non-goals): multiple save slots,
   "while you were away" offline progression (timers freeze on save, resume
   unchanged on load), and a compact/binary save format (JSON chosen
   deliberately for now - revisit before the full game project ships).
   Verified end to end: harvest a tree partway, quit, relaunch, Continue
   restores the exact player position/tree state/wood count/world clock;
   autosave updates the file without quitting; New Game's wipe-confirm
   dialog (Cancel/Yes) both work correctly. Also added a title screen
   background image (`Content/title-bg.png`, letterboxed "contain" fit) as
   a follow-up polish request, not part of the original PRD scope.

8. **Chunk cache / streaming**
   Load map/statics blocks once and keep them until the player moves away,
   instead of re-reading every frame. A performance foundation, and a
   natural home for mutable world state (placed buildings, harvested nodes).

## Before Tier 4: review map rendering optimizations

**[TODO]** User-requested checkpoint before starting Tier 4 - review
`WorldMap`/`TileRenderer` for rendering-performance opportunities (not just
the chunk-cache eviction in item 8 above). Candidates to look at when this
comes up: whether `EntityRenderSystem.Rebuild` (runs fresh every `Draw`
call, unconditionally, over the current view range - see
`Design/prd-entity-system.md` 4.6) is worth caching/dirtying instead now
that entity counts may grow; whether `TileRenderer`'s per-tile diamond
back-to-front pass has any redundant work at higher zoom levels/view
ranges; GPU-side batching (draw call counts) once `UltimaBatcher2D` usage
patterns are clearer. Not investigated yet - this is a placeholder to
revisit, not a design already decided.

## Tier 4 — Polish (defer until Tiers 1-3 are in)

9. **Lighting**
   Directional shading via the per-corner normals already computed in
   `TileRenderer` for stretched land, plus light sources (flames/braziers)
   and day/night.

10. **Animated statics & app-shell completeness**
    Animated statics (flames, water); a config file instead of the hardcoded
    `--uopath`/`--clientversion` defaults in `Program.cs`; main-menu /
    character-spawn scenes instead of a fixed spawn tile.

11. **Smooth Z transitions (revisit in a final polish pass)**
    Player height changes currently snap instantly (see `PlayerEntity.Z`'s doc
    comment) rather than easing, on purpose: a first attempt eased a separate
    `RenderZ` toward the logical `Z` and used it for the world's vertical draw
    offset, but `TileRenderer` positions terrain using each tile's own true,
    un-eased Z - so during the ease window after crossing a height boundary,
    the ground briefly rendered at the wrong offset relative to the player's
    fixed feet position. This only showed up while walking (never at rest,
    once the ease caught up), which made it confusing to track down. Snapping
    instantly sidesteps the whole class of bug and isn't very jarring in
    practice since UO's per-tile height steps are small. If a smooth step
    feels worth adding back later, it needs to be purely cosmetic on the
    player SPRITE draw (a bob that doesn't feed into where the world/terrain
    is positioned), not a shared "world height" value - the two must never be
    allowed to disagree on where the ground actually is.

## Deferred gameplay-design notes (game-dev phase, not engine phase)

Ideas captured for later, once we're past the engine/world-foundation tiers
above and into actual gameplay/skill development. Not scheduled into a tier
yet on purpose.

### Harvesting existing map-static trees (not just spawned entities)

Right now `Harvestable` entities are ones we spawn ourselves (see
`WorldScene.SpawnDebugTrees`) at hand-picked positions - there's no way yet
to harvest one of the thousands of tree *statics* already placed in the raw
map data everywhere. The user has seen this pattern work well in a custom
RunUO server + matching custom ClassicUO client, and wants it for actual
lumberjacking (a real gathering skill), likely in a later tier once
gathering skills are being built - this note exists so the shape isn't
forgotten before then:

1. Player targets a static tree tile (via mouse-picking's `tilePick`, which
   already resolves map statics - see Tier 2 item 4).
2. That specific static instance gets *suppressed* from `WorldMap`'s draw
   and pick output (needs a per-(tile, graphic) exclusion overlay -
   `WorldMap`'s static list is cached straight from disk today and has no
   concept of "hide this one instance"; walkability probably doesn't need to
   change, since both the static and its stand-in entity are equally solid).
3. An entity is spawned at that exact tile with the **same graphic** -
   visually nothing changes at the moment of harvest start; the entity is
   just now the "live" thing standing in for that tree.
4. Harvesting depletes the entity exactly like today's debug trees
   (`Appearance.Graphic` swaps to a trunk/stump graphic via `HarvestSystem`).
5. After the respawn timer, instead of swapping the entity's graphic back to
   a tree (today's behavior), the entity is **destroyed** and the
   suppression from step 2 is lifted - the original map static reappears and
   the tile fully reverts to being "just world data" again, with zero
   lingering entity/memory footprint once nothing is mid-harvest.

This turns "every tree instance ever harvested" from a permanent entity into
a transient one that only exists while its harvest state differs from the
map's default - important for memory/scale once this applies to every tree
on the map rather than 3 debug ones. The suppression-overlay mechanism this
needs (step 2) is also the natural building block for anything else that
temporarily hides/replaces a map static (chopped-down rocks, mined ore
veins, a wall knocked down and rebuilt, etc.), so it's worth designing once
generally rather than special-casing trees specifically when the time comes.

## Recommended order

Tier 1 and all of Tier 2 (mouse picking, UI/HUD + control system) are now
fully done. The "walk up to a tree, click it, chop it, get wood" loop is
real end to end, with a real UI counter surfacing it (the "Resources" panel)
instead of just title-bar text. Next up is Tier 3 (6/7/8) as gameplay
systems start needing time, saves, and scale. Tier 4 last.
