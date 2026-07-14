# The Exile's Forge — Next Steps

Roadmap for the world/engine foundation still needed before gameplay design and
survival/crafting features can be built on top. Ordered by how much each item
unblocks; items within a tier can be reordered but the tiers themselves are
dependency-ordered.

Current status (as of this writing): asset loading, rendering, action-mapped
input, audio, an animated player with 8-way facing, and land/static/stretched-
terrain rendering are all working. See `World/TileRenderer.cs` for the current
known gaps in the renderer itself.

## Tier 1 — Core world interaction

Gameplay can't really start without these; they define the world model
everything else builds on.

1. **Collision & walkability + player Z tracking**
   The player currently free-floats in 2D tile space, ignoring terrain height
   and obstacles. Needed: read the surface Z at the player's tile so they
   stand *on* the ground (and step up/down slopes), and block movement into
   impassable tiles, water, and statics flagged `Impassable`. `TileData` flags
   and the statics already read by `TileRenderer` provide everything needed.

2. **Depth sorting: player/entities interleaved with statics**
   The player currently always draws on top of everything. Fold the player
   (and future entities) into the same back-to-front pass as statics, keyed
   on tile position + Z, so the player can walk behind walls and buildings.
   Natural extension of the diagonal static pass already in `TileRenderer`.

3. **Entity system**
   A general world-object model beyond the single `PlayerEntity` — NPCs,
   resource nodes, dropped items, placeables. A simple entity manager
   (position, graphic, animation state, draw hook) that plugs into the depth
   sort. Worth designing with serialization in mind (see Tier 3, item 7)
   since most gameplay features become entities.

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

## Recommended order

Tier 1 (1 → 2 → 3) first — they interlock and define the world model. Then
Tier 2 (4 → 5) for interaction and UI. Then Tier 3 (6/7/8) as gameplay systems
start needing time, saves, and scale. Tier 4 last.

**Pivot point:** once items 1-4 are done, a real gameplay loop is prototypable
(walk up to a tree, click it, chop it, get wood) — those four are the critical
path if the goal is reaching "fun" fastest; 5-8 can come in as the loop
demands them.
