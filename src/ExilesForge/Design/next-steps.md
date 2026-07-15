# The Exile's Forge — Next Steps

Roadmap for the world/engine foundation still needed before gameplay design and
survival/crafting features can be built on top. Ordered by how much each item
unblocks; items within a tier can be reordered but the tiers themselves are
dependency-ordered.

Current status (as of this writing): asset loading, rendering, action-mapped
input, audio, an animated player with 8-way facing, land/static/stretched-
terrain rendering, collision + player Z tracking, depth-sorted player/statics,
and a working entity system are all done. See `World/TileRenderer.cs` for the
current known gaps in the renderer itself.

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

4. **Mouse picking (tile + object under cursor)**
   "What am I pointing at / what did I click." Every interaction — chop tree,
   mine rock, place a wall — needs this. Uses the pixel-pick data the
   renderer loaders already build (`Art.PixelCheck`, etc.).

5. **UI / HUD layer**
   Text + panels using the renderer's `Fonts`/`FontGlyphAtlas`. Needed for
   survival bars (hunger/thirst/health), inventory windows, crafting menus,
   tooltips. A meaningful subsystem on its own (a lightweight control/gump
   system) — budget real time for it.

## Tier 3 — Simulation & durability

6. **Game clock + simulation tick**
   A stable fixed-step tick separate from render, plus a world clock.
   Survival timers, resource respawn, and day/night all hang off this.

7. **Persistence (save/load)**
   A single-player sandbox needs it, and it's cheaper to design the
   entity/world model as serializable now than to retrofit later. Even a
   stub save format early keeps the data model honest.

8. **Chunk cache / streaming**
   Load map/statics blocks once and keep them until the player moves away,
   instead of re-reading every frame. A performance foundation, and a
   natural home for mutable world state (placed buildings, harvested nodes).

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

## Recommended order

Tier 1 (1 → 2 → 3) is now fully done — they interlocked and defined the world
model. Next up is Tier 2 (4 → 5) for interaction and UI. Then Tier 3 (6/7/8)
as gameplay systems start needing time, saves, and scale. Tier 4 last.

**Pivot point:** with items 1-3 done, mouse picking (item 4) is the next
unblock toward a real gameplay loop (walk up to a tree, click it, chop it,
get wood) - the `Harvestable` debug tree already proves the deplete/respawn
half of that loop end to end; picking is what's needed to trigger it from a
real click instead of a debug key.
