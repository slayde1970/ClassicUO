# PRD: Persistence / Save-Load + Title Screen (Tier 3 #7)

Status: **Implemented and verified.** All acceptance criteria in section 5
confirmed: fresh-run title screen shows only New Game; harvesting a tree
partway then quitting produces `save.json` under `%AppData%\ExilesForge\`;
Continue restores exact player position, tree harvest/respawn state, wood
count, and world clock; autosave updates the file's on-disk timestamp during
a long session without quitting; the New Game wipe-confirmation dialog
(Cancel keeps the save, Yes deletes it and starts fresh) both work. The
title screen background artwork (`Content/title-bg.png`) was added as a
follow-up polish request, not part of the original PRD scope.

## 1. Problem

Every session starts from the same hardcoded spawn tile and the same 3 debug
trees — nothing survives closing the game. A single-player sandbox needs to
remember player position, world/entity state, and in-game time across runs,
and needs a way for the player to choose between resuming that state or
starting over.

## 2. Goals

- Player progress (position, harvested/respawning trees, the wood counter,
  the world clock) survives closing and reopening the game.
- A title screen with **New Game** and **Continue** — the first real
  application of the Tier 2 control/gump system beyond the demo panel.
- Save format stays easy to inspect/hand-edit while the project is small.

## 3. Non-goals (explicitly deferred)

- **Multiple save slots / multiple characters.** One save file. Revisit if
  the project ever grows a character-select screen.
- **Advancing simulation time while the game is closed** ("while you were
  away"). Explicitly decided against for this phase: `Harvestable` respawn
  countdowns and the `WorldClock` freeze exactly where they were on save and
  resume unchanged on load. No wall-clock timestamp is stored. Revisit only
  if idle-progression ever becomes an actual gameplay goal.
- **Binary/compact save format.** JSON via `System.Text.Json` (already in
  the BCL for `net10.0`, no new package needed). Human-readable, trivial to
  version, and save size is irrelevant at this scale. **Note for later:**
  reconsider this choice before the full game project ships — a compact
  binary format may be worth it once save data is much larger or players
  can hand-edit saves in ways that matter.
- **Cloud saves / Steam Cloud / multiple machines.** Local `%AppData%` file
  only.
- **Undo / multiple save backups / save corruption recovery.** A single
  file, overwritten in place. Not building rollback protection this phase.
- **Saving audio/graphics settings.** `GameSettings` (uopath, client
  version) stays a launch-arg/config concern, unrelated to this save file.

## 4. Design

### 4.1 File format and location

- `System.Text.Json`, indented, one file: a `SaveData` root object (4.2).
- Path: `%AppData%\ExilesForge\save.json` (`Environment.SpecialFolder.
  ApplicationData`) — survives project rebuilds, standard per-user location,
  doesn't need install/admin permissions. `Persistence/SaveManager.cs` owns
  path resolution so nothing else hardcodes it.
- `SaveManager` exposes `Exists()`, `Save(SaveData)`, `Load()` (returns
  `null` if missing/unreadable — never throws into caller code), and
  `Delete()`.
- `SaveData.SaveVersion` (int, starts at 1) exists from day one even though
  there's only one version so far — the point is to never need a "does this
  field exist" migration hack later because the very first save predates
  versioning.

### 4.2 Save data shape

Flat DTOs, independent of runtime types (`EntityWorld`'s dictionaries,
`PlayerEntity`, `WorldClock`) so the save format doesn't have to change
shape every time an internal class does:

```csharp
class SaveData
{
    int SaveVersion = 1;
    int MapIndex;
    PlayerSaveData Player;
    WorldClockSaveData Clock;
    int WoodCollected;          // stand-in resource counter - see WorldScene
    List<EntitySaveData> Entities = new();
}

class PlayerSaveData
{
    float X, Y;       // WorldPosition
    sbyte Z;
    byte Facing;      // Direction enum, stored as its underlying byte
}

class WorldClockSaveData
{
    uint Day;
    float TimeOfDay;
}

class EntitySaveData
{
    float X, Y;       // Transform.WorldPosition
    sbyte Z;
    ushort Graphic;
    ushort Hue;
    byte Height;

    bool HasHarvestable;
    ResourceType Resource;
    int YieldRemaining;
    int YieldMax;
    ushort AvailableGraphic;
    ushort DepletedGraphic;
    bool IsDepleted;
    float RespawnCountdown;
    float RespawnDuration;

    bool HasInteractable;
}
```

`EntitySaveData` is a flat "has-component" shape (mirroring `EntityWorld`'s
per-type dictionaries) rather than a polymorphic component list — simplest
thing that works while there are only 4 component types, and avoids a
`System.Text.Json` polymorphic-serialization setup for a save format this
small. Revisit only if the component count grows enough that this gets
unwieldy.

`Transform`/`Appearance` are assumed present on every saved entity (matches
today's `EntityWorld` usage — nothing creates one without the other);
`Harvestable`/`Interactable` are optional per entity via the `Has*` flags.

### 4.3 Building/restoring `SaveData`

`WorldScene` owns both directions (it already owns all the runtime state
being saved):

- `SaveGame()` — builds a fresh `SaveData` from `_player`, `Game.World`
  (the `WorldClock`), `_woodCollected`, and every id in
  `_entities.Transforms.Keys` (the authoritative "this entity exists" set),
  looking up `Harvestables`/`Interactables` via `TryGetValue` for the
  optional fields. Calls `SaveManager.Save(data)`.
- A `WorldScene(GameController game, SaveData saveData = null)` constructor
  overload. In `Load()`: if `saveData` is null, today's fresh-start path
  (`_player.Spawn(...)`, `SpawnDebugTrees()`) runs unchanged. If not null,
  a new `RestoreFromSave(saveData)` runs instead: restores the player via a
  new `PlayerEntity.RestoreState(Vector2 position, sbyte z, Direction
  facing)` (bypasses `Spawn`'s `ResolveSpawnZ` call — the exact Z is already
  known), `Game.World`'s `WorldClock.Restore(uint day, float timeOfDay)`
  (new method — `Day`/`TimeOfDay` currently have private setters, only
  mutated by `Advance`), `_woodCollected`, and re-creates each saved entity
  via `_entities.CreateEntity()` plus writing its components back.

### 4.4 When saves happen

- **Autosave**: `WorldScene.Update` accumulates `Time.Delta` in a timer;
  every 60 seconds (a `const float AutosaveIntervalSeconds`), calls
  `SaveGame()` and resets the timer. Uses render `Delta`, not the fixed sim
  tick — autosave timing has no gameplay-determinism requirement, so the
  simpler accumulator is fine.
- **On clean exit**: `GameController` overrides FNA's `protected virtual
  void OnExiting(object, EventArgs)` (fires once, right before the app
  shuts down after a clean `Game.Exit()`/window close — see
  `external/FNA/src/Game.cs`); if `Scenes.Current` is a `WorldScene`, calls
  its (now public) `SaveGame()` before calling `base.OnExiting`. A crash or
  force-kill loses at most one autosave interval, not the whole session.
- Nothing else triggers a save (no manual save key this phase — not asked
  for, easy to add later as a debug key if it turns out to be useful during
  testing).

### 4.5 Title screen

New `Scenes/TitleScene.cs`, built entirely on the Tier 2 control/gump
system (`Control`/`UIManager`/`Panel`/`Label`/`Button`) — its first real use
beyond the `WorldScene` demo panel:

- A title `Label` and two `Button`s: **New Game** (always present) and
  **Continue** (only added to the panel if `SaveManager.Exists()` — no
  disabled-but-visible state needed since `Control` has no disabled concept
  yet, and there's nothing to continue if there's no save).
- **Continue** click → `Game.Scenes.ChangeScene(new WorldScene(Game,
  SaveManager.Load()))`.
- **New Game** click:
  - If no save exists: `Game.Scenes.ChangeScene(new WorldScene(Game))`
    immediately (today's fresh-start path).
  - If a save exists: show a confirmation panel (a semi-transparent
    full-screen dim `Panel` plus a small centered `Panel` with a warning
    `Label` and **Yes**/**Cancel** `Button`s), added as a second `UIManager`
    root so it renders on top and — being hit-tested last-added-first —
    blocks clicks reaching the title buttons underneath while it's up.
    **Yes** calls `SaveManager.Delete()` then changes to a fresh
    `WorldScene(Game)` (deletes immediately, rather than waiting for the
    first autosave to overwrite it, so a crash before that autosave can't
    leave a stale old save for a future "Continue" to pick up by mistake).
    **Cancel** just removes the confirmation root.
- `Program.cs` changes its initial scene from `new WorldScene(game)` to
  `new TitleScene(game)`.

## 5. Acceptance criteria for this phase

- [ ] Fresh run with no save file: title screen shows only **New Game**;
  clicking it starts a normal new session (today's spawn tile + 3 debug
  trees), identical to current behavior.
- [ ] Playing for a while (move the player, partially/fully harvest at
  least one tree, let the wood counter increment, let some in-game time
  pass), then closing the window cleanly: a `save.json` appears under
  `%AppData%\ExilesForge\`.
- [ ] Relaunching shows **Continue** alongside **New Game**; clicking
  **Continue** restores the player's exact position, each tree's exact
  harvested/respawning state, the wood counter, and the world clock's
  day/time — verified by comparing values before quitting and after
  continuing.
- [ ] Waiting 60+ real seconds during a session (without quitting) produces
  an autosave — verified by checking `save.json`'s on-disk modified time
  updates without closing the game.
- [ ] Clicking **New Game** when a save exists shows the confirmation
  panel; **Cancel** returns to the title screen with the save intact;
  **Yes** deletes it and starts a fresh session.
- [ ] No regression: existing gameplay (movement, harvest, HUD, resource
  panel) all still work identically inside a loaded session.

## 6. Open questions carried forward (not blocking this phase)

- Whether to add a manual/debug save key once real testing surfaces a need
  for it.
- Whether "while you were away" idle progression is ever wanted — explicitly
  deferred, not designed against (a wall-clock timestamp could be added to
  `SaveData` later without breaking the existing fields).
- Whether JSON stays the right format once save data is much larger, or
  once real inventory/building data joins `SaveData` — flagged above, not
  a decision to revisit yet.
