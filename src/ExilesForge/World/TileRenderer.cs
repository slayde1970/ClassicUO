// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Assets;
using TEF.World.Entities;

namespace TEF.World
{
    /// <summary>
    /// Draws the world floor under the player: land tiles and the statics
    /// (walls, trees, floors, deco) that sit on top of them. Tile/static data
    /// comes from a shared <see cref="WorldMap"/>; this class is purely the
    /// rendering half.
    ///
    /// Land and statics are interleaved into ONE back-to-front painter's-
    /// algorithm pass, walking diagonals of increasing (tx + ty): for each
    /// tile visited, its land draws first, then its statics - matching
    /// ClassicUO's Chunk.AddGameObject, where land always sorts behind
    /// statics on the same tile via a lower priorityZ. This matters because
    /// stretched land (see TryBuildStretch) and tall statics both reach
    /// beyond their own tile's footprint into neighboring tiles' screen
    /// space; drawing all land then all statics in two separate passes let a
    /// static from a "farther back" tile paint over land genuinely in front
    /// of it (e.g. a river static over the hillside/road in front of it).
    ///
    /// Land with a valid texmap and height variation is drawn from that
    /// texmap, stretched to its neighbors' corner heights (DrawStretchedLand,
    /// ported from Land.ApplyStretch + LandView.Draw) - this is what gives
    /// rocky/mountain terrain its 3D silhouette and keeps it from showing as
    /// black gaps. Flat land falls back to the plain land art. Statics are
    /// positioned with the same art anchor math as View.DrawStatic
    /// ((w/2 - 22, h - 44) offset).
    ///
    /// KNOWN GAPS:
    ///  - Stretched land uses flat (+Z) corner normals - the height SHAPE is
    ///    correct but there's no directional lighting/shading on slopes yet.
    ///  - No animated-static frames, no light sources, no per-object depth
    ///    vs. the player (the player always draws on top - see WorldScene).
    /// </summary>
    public sealed class TileRenderer
    {
        internal const int TileSize = 22; // half-width/height of the 44x44 iso diamond, see GameObject.UpdateRealScreenPosition - internal so BlockMesh (Tier 4 #13) can share it
        private const int BlockSize = WorldMap.BlockSize;

        // Scratch buffer for the three-way (map statics + entities + player)
        // merge in DrawStaticsAt - reused across calls to avoid per-tile
        // allocations. Never mutate map.GetStaticsAt's or entities.GetAt's
        // own lists directly; both are cached/reused elsewhere.
        private readonly List<WorldMap.StaticTile> _mergedStatics = new();

        // Mouse-pick state for the in-progress Draw pass (see PickResult).
        // _pickPosition is the cursor in pre-camera-matrix space (same space
        // as the sprites' screen positions), or null when not picking.
        // Entities, map statics, and land are tracked as three INDEPENDENT
        // topmost-hit trackers (not one merged "winner") so game code can ask
        // "what entity is under the cursor" and "what tile is under the
        // cursor" separately - e.g. scripting a harvest action needs the
        // entity, while a tooltip or move-here click needs the ground tile
        // regardless of whether an entity happens to be standing on it.
        private Point? _pickPosition;
        private PickResult _entityPick;
        private PickResult _staticPick;
        private PickResult _landPick;

        // Draw-call counters for the just-completed Draw() pass - debug/
        // perf-investigation only (see DebugHud), not used by any gameplay
        // logic. Reset at the top of every Draw call.
        public int StaticDrawCalls { get; private set; }
        public int MeshedBlockDraws { get; private set; }
        public int MeshedLandQuads { get; private set; }
        public int MeshedStaticQuads { get; private set; }

        /// <param name="entities">Live world entities (resource nodes, etc.) to interleave into the same pass, in the same tuple shape as map statics. May be null to skip entirely.</param>
        /// <param name="player">The player, drawn interleaved into the back-to-front pass on its own tile so statics on tiles in front of it can occlude it. May be null.</param>
        /// <param name="viewRangeInTiles">How many tiles out from the player to draw in each direction.</param>
        /// <param name="screenCenterOffset">Viewport center - Camera only handles zoom/peek, not centering, so the caller supplies this (see PlayerEntity.Draw for the same convention).</param>
        /// <param name="pickPosition">Cursor position in pre-camera-matrix (world-draw) space - e.g. Camera.MouseToWorldPosition(). Null to skip picking.</param>
        /// <param name="entityPick">The topmost live entity under the cursor this frame (Kind == Entity or None). Independent of <paramref name="tilePick"/> - an entity can be standing on any tile.</param>
        /// <param name="tilePick">The topmost map tile (a static if one is there, else the land) under the cursor this frame (Kind == Static, Land, or None). Never Entity - this is map data only.</param>
        /// <param name="drawStatics">When false, skips MAP statics only (debug toggle to inspect land without clutter). Entities and the player always draw regardless.</param>
        /// <param name="animatedStatics">Shared per-graphic-id animation frame table (fountains, torches, lava, ...) - see AnimatedStatics. Its current offset is added to a static's base graphic before the art lookup.</param>
        public void Draw(
            UltimaBatcher2D batcher, WorldMap map, EntityRenderSystem entities, PlayerEntity player,
            int viewRangeInTiles, Vector2 screenCenterOffset,
            Point? pickPosition, out PickResult entityPick, out PickResult tilePick,
            AnimatedStatics animatedStatics, bool drawStatics = true)
        {
            var assets = map.Assets;

            _pickPosition = pickPosition;
            _entityPick = default;
            _staticPick = default;
            _landPick = default;
            StaticDrawCalls = 0;
            MeshedBlockDraws = 0;
            MeshedLandQuads = 0;
            MeshedStaticQuads = 0;

            var playerTilePosition = player.WorldPosition;
            int centerX = (int)Math.Floor(playerTilePosition.X);
            int centerY = (int)Math.Floor(playerTilePosition.Y);

            // Every tile's screen position is relative to the player's exact
            // (fractional) iso position so movement scrolls smoothly, and the
            // player's own Z is folded into the Y origin so climbing shifts the
            // world down (player appears to rise) and vice versa. The "-
            // TileSize" matches the "- 22" bias GameObject.UpdateRealScreenPosition
            // applies to EVERY object's real screen position (mobiles
            // included) - land/static planarX/baseY below apply the same
            // bias, so it must be applied here too, or the player (missing
            // it) renders ~22px too high relative to everything else. That
            // discrepancy was invisible while the player always drew on top
            // of the world, but became visible once statics/land could
            // occlude it: front tiles were biting into the player's feet.
            //
            // player.Z is used as-is (not eased) so terrain always aligns
            // exactly with the true surface height - an earlier eased-height
            // version briefly rendered the ground at the wrong offset while
            // crossing a height boundary (see PlayerEntity.Z's doc comment).
            float playerIsoX = (playerTilePosition.X - playerTilePosition.Y) * TileSize - TileSize;
            float playerIsoY = (playerTilePosition.X + playerTilePosition.Y) * TileSize - (player.Z * 4f) - TileSize;
            var isoOrigin = new Vector2(playerIsoX, playerIsoY);

            // Tier 4 #13: pixel-snapped, not sub-pixel - see
            // Design/prd-chunk-mesh-render.md. Batcher2D.DrawBatch (used to
            // feed a BlockMesh's prebuilt vertex array each frame) only
            // accepts a whole-pixel camera offset, matching the real
            // client's own actual pixel-snapped camera (_offset is int in
            // GameSceneDrawingSorting.cs). worldOffset folds screenCenterOffset
            // in BEFORE rounding (not isoOrigin alone) so this is correct
            // even if the viewport has an odd width/height (a fractional
            // screenCenterOffset) - rounding isoOrigin alone would leave a
            // sub-pixel remainder in that case, jittering meshed content
            // (which can only take a whole-pixel offset) against
            // individually-drawn content (player, unmeshed statics) by up
            // to 1px. screenCenterOffset itself is also rounded below so
            // the player (which never subtracts isoOrigin, see its own
            // draw) stays in the same whole-pixel space as everything else.
            screenCenterOffset.X = MathF.Round(screenCenterOffset.X);
            screenCenterOffset.Y = MathF.Round(screenCenterOffset.Y);

            var worldOffset = isoOrigin - screenCenterOffset;
            worldOffset.X = MathF.Round(worldOffset.X);
            worldOffset.Y = MathF.Round(worldOffset.Y);

            int x0 = centerX - viewRangeInTiles;
            int x1 = centerX + viewRangeInTiles;
            int y0 = centerY - viewRangeInTiles;
            int y1 = centerY + viewRangeInTiles;

            entities?.Rebuild(x0, y0, x1, y1);

            // Tier 4 #13: land now comes from a persistent, texture-bucketed
            // BlockMesh per touched 8x8 block, not a per-tile Draw() call
            // (see BlockMesh's doc comment). Real depth-testing (see
            // WorldScene.Draw's SetStencil) means submission ORDER no
            // longer matters for correctness, so drawing every touched
            // block's land upfront - before the per-tile statics/player
            // loop below - is safe; the two are just independently ordered
            // draw calls the GPU depth-tests against each other, not a
            // painter's-algorithm sequence anymore. A block's own iso
            // "diamond" footprint may draw a little land beyond the exact
            // view diamond at its edges - harmless, minor overdraw, same
            // as the real client's chunk-mesh approach.
            int blockX0 = Math.Max(0, x0) >> 3;
            int blockX1 = Math.Max(0, x1) >> 3;
            int blockY0 = Math.Max(0, y0) >> 3;
            int blockY1 = Math.Max(0, y1) >> 3;
            int meshOffsetX = (int)worldOffset.X;
            int meshOffsetY = (int)worldOffset.Y;

            for (int by = blockY0; by <= blockY1; by++)
            {
                for (int bx = blockX0; bx <= blockX1; bx++)
                {
                    var blockMesh = map.GetOrBuildBlockMesh(bx, by);
                    blockMesh.Draw(batcher, meshOffsetX, meshOffsetY, drawStatics);
                    MeshedBlockDraws++;
                    MeshedLandQuads += blockMesh.LandQuadCount;
                    MeshedStaticQuads += blockMesh.StaticQuadCount;
                }
            }

            // The player is drawn when the pass reaches its tile. A mobile
            // sorts one step above same-Z statics on its tile (Chunk.
            // AddGameObject: `case Mobile: priorityZ++`), so statics with a
            // higher priorityZ on that tile - and all statics on tiles closer
            // to the camera - paint over it.
            int playerPriorityZ = player.Z + 1;

            // Single back-to-front pass, walking diagonals of increasing
            // (tx + ty) - see the class doc comment for why land and statics
            // must be interleaved here rather than drawn in two full passes.
            for (int sum = x0 + y0; sum <= x1 + y1; sum++)
            {
                int txStart = Math.Max(x0, sum - y1);
                int txEnd = Math.Min(x1, sum - y0);

                for (int tx = txStart; tx <= txEnd; tx++)
                {
                    int ty = sum - tx;
                    if (tx < 0 || ty < 0)
                    {
                        continue;
                    }

                    DrawLandTile(map, assets, tx, ty, worldOffset);

                    bool isPlayerTile = tx == centerX && ty == centerY;

                    // Always call DrawStaticsAt - entities draw unconditionally
                    // (see its drawMapStatics param), so this can't be skipped
                    // wholesale the way the old drawStatics-off branch did, or
                    // entities/the player would vanish along with map statics.
                    DrawStaticsAt(
                        batcher, map, assets, entities, drawStatics, tx, ty, worldOffset,
                        isPlayerTile ? player : null, playerPriorityZ, screenCenterOffset, animatedStatics);
                }
            }

            entityPick = _entityPick;
            // Map static beats land for the tile pick: only report a land
            // hit when no static was under the cursor.
            tilePick = _staticPick.Kind != PickKind.None ? _staticPick : _landPick;
        }

        /// <summary>
        /// Tier 4 #13 task 8: land no longer draws OR recomputes its own
        /// position/stretch here - both now live entirely in BlockMesh
        /// (built once per block). This just reads back the pick info
        /// BlockMesh already cached at build time and runs the same diamond
        /// hit test as before, instead of redoing TryBuildStretch's
        /// up-to-11-neighbor lookup every frame for every visible tile
        /// purely to answer "what's under the cursor".
        /// </summary>
        private void DrawLandTile(
            WorldMap map, GameAssets assets,
            int tx, int ty, Vector2 worldOffset)
        {
            int blockX = tx >> 3;
            int blockY = ty >> 3;
            int localX = tx & (BlockSize - 1);
            int localY = ty & (BlockSize - 1);

            var pick = map.GetOrBuildBlockMesh(blockX, blockY).GetLandPick(localX, localY);
            if (!pick.HasLand)
            {
                return;
            }

            var screenPos = new Vector2(pick.ScreenX, pick.ScreenY) - worldOffset;

            // Diamond hit test at the un-stretched position - an
            // approximation for stretched tiles (their corners are pushed by
            // yOffsets), fine since land picking is secondary to object
            // picking.
            TestLandPick(assets, screenPos, tx, ty, pick.TileId);
        }

        /// <summary>
        /// Land is picked by its iso-diamond shape (exact and cheap) rather
        /// than the pixel picker: a point is inside the 44x44 tile's diamond
        /// when |dx| + |dy| &lt;= 22 from its center. Recorded into _landPick
        /// (not _pick) so any solid static/entity hit takes precedence.
        /// </summary>
        private void TestLandPick(GameAssets assets, Vector2 screenPos, int tx, int ty, ushort tileId)
        {
            if (_pickPosition is not Point p)
            {
                return;
            }

            float dx = p.X - (screenPos.X + TileSize);
            float dy = p.Y - (screenPos.Y + TileSize);

            if (Math.Abs(dx) + Math.Abs(dy) <= TileSize)
            {
                _landPick = new PickResult
                {
                    Kind = PickKind.Land,
                    TileX = tx,
                    TileY = ty,
                    Graphic = tileId,
                    Name = assets.Files.TileData.LandData[tileId].Name,
                };
            }
        }

        /// <summary>
        /// Draws a tile's statics AND entities in one merged priorityZ order,
        /// optionally interleaving the player at
        /// <paramref name="playerPriorityZ"/> when <paramref name="player"/>
        /// is non-null (i.e. this is the player's tile). Entries sorting at
        /// or below the player's priority draw first (behind it); entries
        /// above draw after (in front). See Design/prd-entity-system.md
        /// section 4.6 for why entities merge into this same tuple shape
        /// rather than getting their own interface/draw path.
        /// </summary>
        private void DrawStaticsAt(
            UltimaBatcher2D batcher, WorldMap map, GameAssets assets, EntityRenderSystem entities, bool drawMapStatics,
            int tx, int ty, Vector2 worldOffset,
            PlayerEntity player, int playerPriorityZ, Vector2 playerScreenCenter, AnimatedStatics animatedStatics)
        {
            // Map statics are the only thing gated by drawMapStatics (the F6/F7
            // debug toggles) - entities always draw regardless, so hiding map
            // statics to declutter the view (or debug "is my entity even
            // rendering") never hides the entities themselves.
            var mapStatics = drawMapStatics ? map.GetStaticsAt(tx, ty) : null;
            var entityStatics = entities?.GetAt(tx, ty);

            List<WorldMap.StaticTile> list;

            if (entityStatics == null || entityStatics.Count == 0)
            {
                list = mapStatics;
            }
            else if (mapStatics == null || mapStatics.Count == 0)
            {
                list = entityStatics;
            }
            else
            {
                // Both present - merge into scratch storage rather than
                // mutating either cached list (map's is persistent; the
                // entity system's is rebuilt but still owned by it).
                _mergedStatics.Clear();
                _mergedStatics.AddRange(mapStatics);
                _mergedStatics.AddRange(entityStatics);
                _mergedStatics.Sort(static (a, b) =>
                {
                    int cmp = a.PriorityZ.CompareTo(b.PriorityZ);
                    return cmp != 0 ? cmp : a.ReadOrder.CompareTo(b.ReadOrder);
                });
                list = _mergedStatics;
            }

            if (list == null)
            {
                // Nothing here, but the player still needs drawing if this
                // is its tile.
                if (player != null)
                {
                    DrawPlayerAndPick(batcher, assets, player, playerScreenCenter);
                }
                return;
            }

            float planarX = (tx - ty) * TileSize - TileSize;
            float baseY = (tx + ty) * TileSize - TileSize;
            bool playerDrawn = false;

            foreach (var s in list)
            {
                // Insert the player just before the first static that sorts
                // above it. Checked against the full sorted list (before the
                // CanDrawStatic visibility filter) so the insertion point is
                // stable regardless of which statics are visible.
                if (player != null && !playerDrawn && s.PriorityZ > playerPriorityZ)
                {
                    DrawPlayerAndPick(batcher, assets, player, playerScreenCenter);
                    playerDrawn = true;
                }

                // s.Drawable is baked in once at block-load time (map
                // statics) or construction time (entity-projected tiles) -
                // see WorldMap.StaticTile.Drawable / CanDrawStatic - rather
                // than recomputed here every frame for every static in view.
                if (!s.Drawable)
                {
                    continue;
                }

                // The base graphic drives hue/name/tiledata lookups (s.HueVector
                // is already baked from it); only the ART lookup uses the
                // current animation-frame offset, matching View.DrawStaticAnimated's
                // `graphic + index.AnimOffset` - a static's identity doesn't
                // change frame to frame, only which sub-frame sprite it shows.
                ushort animatedGraphic = (ushort)(s.Graphic + animatedStatics.CurrentOffset(s.Graphic));

                ref readonly var sprite = ref assets.Art.GetArt(animatedGraphic);
                if (sprite.Texture == null)
                {
                    continue;
                }

                // Art anchor for statics: bottom-center of the sprite sits on
                // the tile. See View.DrawStatic.
                int offX = (sprite.UV.Width >> 1) - TileSize;
                int offY = sprite.UV.Height - (2 * TileSize);

                float drawX = planarX - offX;
                float drawY = baseY - (s.Z << 2) - offY;
                var screenPos = new Vector2(drawX, drawY) - worldOffset;

                // Tier 4 #13: a mesh-eligible MAP static (EntityId == 0, not
                // animated/foliage/tree/rock) is already baked into this
                // block's BlockMesh and drawn from there - skip the
                // individual draw here to avoid double-drawing it, but
                // still resolve picking below (not yet decoupled from
                // drawing - see task list item 8). Entity-sourced statics
                // and mesh-excluded map statics (animated statics in
                // particular, since their UV changes every frame) still
                // need their own per-object draw call every frame, exactly
                // as before.
                bool isBakedIntoMesh = s.EntityId == 0 && !StaticMeshFilter.IsExcludedFromMesh(assets, s.Graphic);

                if (!isBakedIntoMesh)
                {
                    batcher.Draw(
                        sprite.Texture,
                        screenPos,
                        sprite.UV,
                        s.HueVector,
                        DepthKey.Compute(tx, ty, s.PriorityZ)
                    );
                    StaticDrawCalls++;
                }

                TestStaticPick(assets, s, animatedGraphic, screenPos, sprite.UV.Width, sprite.UV.Height, tx, ty);
            }

            // Player sorts above every static on the tile.
            if (player != null && !playerDrawn)
            {
                DrawPlayerAndPick(batcher, assets, player, playerScreenCenter);
            }
        }

        /// <summary>
        /// Draws the player and, if picking is active, per-pixel hit-tests
        /// its currently-drawn sprite (PlayerEntity.TryPick shares frame
        /// resolution with its own Draw, so this can never drift out of sync
        /// with what's actually on screen).
        /// </summary>
        private void DrawPlayerAndPick(UltimaBatcher2D batcher, GameAssets assets, PlayerEntity player, Vector2 screenCenterOffset)
        {
            player.Draw(batcher, assets, screenCenterOffset);

            if (_pickPosition is Point p && player.TryPick(assets, p, screenCenterOffset))
            {
                _entityPick = new PickResult
                {
                    Kind = PickKind.Player,
                    TileX = (int)MathF.Floor(player.WorldPosition.X),
                    TileY = (int)MathF.Floor(player.WorldPosition.Y),
                    Graphic = player.Graphic,
                    Name = "Player",
                };
            }
        }

        /// <summary>
        /// Per-pixel hit test for a static/entity sprite, using the same
        /// pixel-alpha mask ClassicUO's StaticView.CheckMouseSelection uses
        /// (Art.PixelCheck, keyed by the raw graphic id, populated by the
        /// GetArt call just above). Because the pass is back-to-front, later
        /// (frontmost) hits overwrite earlier ones, so the topmost visible
        /// hit wins for free - tracked separately for entities vs. map
        /// statics (see the _entityPick/_staticPick doc comment) so both are
        /// independently available to game code, not just whichever "won".
        /// </summary>
        private void TestStaticPick(GameAssets assets, in WorldMap.StaticTile s, ushort animatedGraphic, Vector2 screenPos, int width, int height, int tx, int ty)
        {
            if (_pickPosition is not Point p)
            {
                return;
            }

            int lx = p.X - (int)screenPos.X;
            int ly = p.Y - (int)screenPos.Y;

            if (lx < 0 || ly < 0 || lx >= width || ly >= height)
            {
                return;
            }

            // Pixel-check against whichever sub-frame is ACTUALLY on screen
            // this frame (animatedGraphic), so hit-testing can't drift from
            // what's rendered - but the reported result below still uses the
            // base graphic (s.Graphic) for name/identity, matching the real
            // client (a fountain is still "a fountain" regardless of which
            // animation frame it's showing).
            if (!assets.Art.PixelCheck(animatedGraphic, lx, ly))
            {
                return;
            }

            var result = new PickResult
            {
                Kind = s.EntityId != 0 ? PickKind.Entity : PickKind.Static,
                TileX = tx,
                TileY = ty,
                Graphic = s.Graphic,
                Name = assets.Files.TileData.StaticData[s.Graphic].Name,
                EntityId = s.EntityId,
            };

            if (s.EntityId != 0)
            {
                _entityPick = result;
            }
            else
            {
                _staticPick = result;
            }
        }

        /// <summary>
        /// Builds the corner-height offsets and per-corner normals for a
        /// stretched land tile, and reports whether the tile should actually
        /// stretch (its 3x3+ neighborhood isn't flat). Direct port of
        /// ClassicUO.Client's Land.ApplyStretch. Returns false for a locally
        /// flat tile, in which case the caller falls back to flat art.
        /// </summary>
        // internal (not private) so BlockMesh (Tier 4 #13) can reuse this
        // exact port instead of duplicating it.
        internal static bool TryBuildStretch(
            WorldMap map, int x, int y, sbyte z,
            out UltimaBatcher2D.YOffsets yOffsets,
            out Vector3 normalTop, out Vector3 normalRight, out Vector3 normalLeft, out Vector3 normalBottom,
            out sbyte averageZ)
        {
            //  _____ _____
            // | top | rig |
            // |_____|_____|
            // | lef | bot |
            // |_____|_____|
            sbyte zTop = z;
            sbyte zRight = map.GetLandZ(x + 1, y);
            sbyte zLeft = map.GetLandZ(x, y + 1);
            sbyte zBottom = map.GetLandZ(x + 1, y + 1);

            yOffsets = new UltimaBatcher2D.YOffsets
            {
                Top = zTop * 4,
                Right = zRight * 4,
                Left = zLeft * 4,
                Bottom = zBottom * 4,
            };

            // Matches Land.ApplyStretch's AverageZ - the pick-of-two-diagonals
            // average used (among other things) as this tile's depth-sort Z.
            averageZ = Math.Abs(zTop - zBottom) <= Math.Abs(zLeft - zRight)
                ? (sbyte)((zTop + zBottom) >> 1)
                : (sbyte)((zLeft + zRight) >> 1);

            //  _____ _____ _____ _____
            // |     | t10 | t20 |     |
            // |_____|_____|_____|_____|
            // | t01 |  z  | t21 | t31 |
            // |_____|_____|_____|_____|
            // | t02 | t12 | t22 | t32 |
            // |_____|_____|_____|_____|
            // |     | t13 | t23 |     |
            // |_____|_____|_____|_____|
            sbyte t10 = map.GetLandZ(x, y - 1);
            sbyte t20 = map.GetLandZ(x + 1, y - 1);
            sbyte t01 = map.GetLandZ(x - 1, y);
            sbyte t21 = zRight;
            sbyte t31 = map.GetLandZ(x + 2, y);
            sbyte t02 = map.GetLandZ(x - 1, y + 1);
            sbyte t12 = zLeft;
            sbyte t22 = zBottom;
            sbyte t32 = map.GetLandZ(x + 2, y + 1);
            sbyte t13 = map.GetLandZ(x, y + 2);
            sbyte t23 = map.GetLandZ(x + 1, y + 2);

            bool stretched = false;
            stretched |= CalculateNormal(z, t10, t21, t12, t01, out normalTop);
            stretched |= CalculateNormal(t21, t20, t31, t22, z, out normalRight);
            stretched |= CalculateNormal(t22, t21, t32, t23, t12, out normalBottom);
            stretched |= CalculateNormal(t12, z, t22, t13, t02, out normalLeft);

            return stretched;
        }

        // Direct port of Land.CalculateNormal. Returns false (and a flat +Z
        // normal) when the tile and all four neighbors share a height.
        private static bool CalculateNormal(sbyte tile, sbyte top, sbyte right, sbyte bottom, sbyte left, out Vector3 normal)
        {
            if (tile == top && tile == right && tile == bottom && tile == left)
            {
                normal.X = 0;
                normal.Y = 0;
                normal.Z = 1f;

                return false;
            }

            var u = new Vector3();
            var v = new Vector3();
            var ret = new Vector3();

            u.X = -22;
            u.Y = -22;
            u.Z = (left - tile) * 4;
            v.X = -22;
            v.Y = 22;
            v.Z = (bottom - tile) * 4;
            Vector3.Cross(ref v, ref u, out ret);

            u.X = -22;
            u.Y = 22;
            u.Z = (bottom - tile) * 4;
            v.X = 22;
            v.Y = 22;
            v.Z = (right - tile) * 4;
            Vector3.Cross(ref v, ref u, out normal);
            Vector3.Add(ref ret, ref normal, out ret);

            u.X = 22;
            u.Y = 22;
            u.Z = (right - tile) * 4;
            v.X = 22;
            v.Y = -22;
            v.Z = (top - tile) * 4;
            Vector3.Cross(ref v, ref u, out normal);
            Vector3.Add(ref ret, ref normal, out ret);

            u.X = 22;
            u.Y = -22;
            u.Z = (top - tile) * 4;
            v.X = -22;
            v.Y = -22;
            v.Z = (left - tile) * 4;
            Vector3.Cross(ref v, ref u, out normal);
            Vector3.Add(ref ret, ref normal, out ret);

            Vector3.Normalize(ref ret, out normal);

            return true;
        }
    }
}
