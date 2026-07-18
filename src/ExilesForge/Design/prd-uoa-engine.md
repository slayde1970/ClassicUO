# PRD: UO Architect Engine Extraction (Tier 4.5)

Status: **Complete.** Steps 1-5 done and user-verified; step 6's core (input
binding registry) was folded into step 1, leaving only optional config-driven
rebinds (deferred until a rebinding UI exists). TEF runs on the extracted
`UOA.Engine` + `UOA.World` with no behavior change.

> **Engine name: UO Architect Engine**, root namespace **`UOA`**. Assemblies
> `UOA.Engine` (game-agnostic framework) and `UOA.World` (UO isometric-world
> toolkit). The game stays `The Exile's Forge` / namespace `TEF` and becomes
> the engine's first consumer.

## 1. Problem

The user wants to prototype **several** game ideas before settling on a final
design, all built on the same ClassicUO shared assemblies (`ClassicUO.Assets`,
`.Renderer`, `.IO`, `.Utility`, FNA). Today everything - engine plumbing and
The Exile's Forge gameplay - lives in a single `ExilesForge` project under
namespace `TEF`. Starting a second prototype would mean copy-pasting or
cross-referencing that blob, with no enforced boundary between "reusable
engine" and "this game."

In practice most of `ExilesForge/` is **already** game-agnostic - the host,
scene stack, input mapping, audio, the UI/gump system, config/save mechanisms,
and the UO isometric-world renderer are all reusable. The work here is not to
*write* a framework but to **draw the seam** between engine and game and
**enforce the dependency direction** (game depends on engine, never the
reverse) so multiple prototypes can share one evolving foundation.

## 2. Goals

- Extract a reusable, **game-agnostic** framework (**UO Architect Engine**,
  namespace `UOA`) into its own assembly(ies) that any number of prototypes can
  reference, with the engine/game boundary enforced by the compiler (separate
  assemblies), not by convention.
- `ExilesForge` becomes the **first consumer** of the engine - the best
  possible test that the seam is in the right place - with no user-visible
  behavior change (same rendering, same UI, same save/config).
- Provide the extendability the user called out explicitly:
  - **Config**: a default/engine config plus a per-game section, each game
    supplying its own settings type without editing the engine.
  - **Save**: each game (and each system within a game) adds its own custom
    save data with zero engine changes.
  - **UI/gumps**: an extendable, flexible control/gump system with
    ClassicUO-style window management (named lookup, modal/focus, z-order,
    dragging, open/close), exposed to scenes as a clean UI layer.
  - **Scripting-readiness**: the UI is built so a script layer (Lua/TS/etc.)
    can drive it *later* without re-architecting - designed for now, not
    implemented now.
- Do it **incrementally**: every step independently builds, runs, and is
  visually verified (the project's standard verify loop). No big-bang rewrite.

## 3. Non-goals (explicitly deferred)

- **No scripting interpreter is written this phase.** We add the *seam*
  (`ControlFactory` + string-addressable properties/events + `IScriptHost`
  interface) so a VM can be bolted on additively later. Choosing and wiring an
  actual Lua/JS engine is a separate future item. (Leaning MoonSharp/Lua -
  pure C#, no native deps, sandboxable - when the time comes.)
- **No second prototype is built this phase.** This only extracts and proves
  the engine against TEF.
- **No gray-zone speculative extraction.** The generic entity registry
  (`EntityWorld`/`Components`) is ECS-ish scaffolding that *might* be reusable,
  but until a second prototype actually needs it, it stays in the game
  (rule-of-three). Real reuse pulls code up; we don't pre-abstract for a
  consumer that doesn't exist.
- **No feature work.** This is a pure structural refactor plus the three
  extendability mechanisms; no new gameplay, no new rendering.

## 4. Design

### 4.1 Layering and dependency direction

Three layers above the shared ClassicUO libs, dependencies pointing **up only**:

```
ClassicUO.Assets / .Renderer / .IO / .Utility + FNA
        ▲
   UOA.Engine       UO Architect Engine (namespace UOA): game-agnostic host,
        ▲                          scenes, input, UI/gumps, audio, UO content
        ▲                          provider, config, save, script seam
   UOA.World        UO isometric-world toolkit: map reader, TileRenderer,
        ▲                          BlockMesh, camera, depth, picking, day/night
   ExilesForge      the game (TEF): concrete scenes, gameplay, its save/config
        ▲                          schema
   <NextPrototype>  another game: references UOA.Engine (+ UOA.World if it uses
                                   a UO map)
```

`UOA.Engine` **must never** reference a game type. Separate assemblies make
that a compile error, not a code-review nicety. `UOA.World` sits above the
engine so a pure-UI or non-tile prototype can reference only `UOA.Engine`.

### 4.2 What moves where (from the current file layout)

Engine/world files are renamed from `TEF.*` to `UOA.*`; the game keeps `TEF.*`.

**→ `UOA.Engine` (namespace `UOA`):**
- `Core/GameController.cs` → `GameHost` (batcher, graphics, sim/world clock,
  service wiring; owns only engine services)
- `Core/{Time, SimulationClock, WorldClock}.cs`
- `Scenes/{Scene, SceneManager}.cs` + per-scene `Camera`
- `Input/InputManager.cs` (the `GameAction` enum does **not** come along - see 4.6)
- `Audio/AudioManager.cs`
- `UI/{Control, UIManager, BackgroundImage}.cs`, `UI/Controls/{Label, Panel, Button}.cs`
- `Assets/GameAssets.cs` → `UoContent` (UO file/atlas provider; engine-for-UO,
  and every prototype here is UO-art-based)
- `Assets/LightColors.cs` (internal, used by `GameAssets` during asset load -
  UO content infrastructure, not game code)
- `Persistence/{ConfigManager, SaveManager}.cs` → generalized (4.3, 4.4)

**→ `UOA.World` (namespace `UOA.World`):**
- `World/{WorldMap, TileRenderer, BlockMesh, DepthKey, StaticMeshFilter,
  PickResult, AnimatedStatics, DayNightOverlay}.cs`

**Stays in `ExilesForge` (namespace `TEF`):**
- `Scenes/{TitleScene, SpawnSelectScene, WorldScene}.cs`
- `World/{PlayerEntity, Direction}.cs`, `World/Entities/*`, `HarvestSystem`
- `Persistence/SaveData.cs` (its schema), a game-specific config type
- `Input/GameAction.cs` (+ `Input/InputActions.cs`, the game-side binding shim
  added in step 1 - see 4.6), `UI/DebugHud.cs`, `Program.cs`

### 4.3 Config: layered, app-name-parameterized

Split today's sealed `GameSettings` into engine-owned `EngineSettings` (UO
dir, client version, FPS, resolution, volume, keybinds) plus a game section.
Manager becomes generic and app-name-parameterized:

```
ConfigManager<TConfig>   where TConfig exposes an EngineSettings Engine { get; }
```

Writes `%AppData%/<AppId>/config.json`, where `<AppId>` is supplied by the game
(no more hardcoded `"ExilesForge"`). `ConfigManager<TefConfig>` for TEF; each
prototype supplies its own `TConfig` embedding `EngineSettings`. Same
null-on-missing/parse-fail contract as today.

### 4.4 Save: participant/section registry

Replace the single sealed `SaveData` DTO with a section registry. Each system
that has persistent state implements:

```
interface ISaveParticipant
{
    string Section { get; }              // e.g. "engine.clock", "world.entities", "game.inventory"
    void Write(Utf8JsonWriter writer);
    void Read(JsonElement section);
}
```

`SaveManager` owns a single JSON document `{ section -> payload }`, iterating
registered participants to write, and dispatching each section back on load.
Adding save data = registering a participant; **zero engine changes**. Missing
sections on load are tolerated (forward/backward compatibility as games gain
systems). Keeps the current one-slot, `%AppData%/<AppId>/save.json`,
never-throw-on-read behavior.

### 4.5 UI: gump parity + script-ready construction

The retained-mode `Control`/`UIManager` tree stays; two upgrades:

**(a) ClassicUO-style window management on `UIManager`** - the methods the user
cited from CUO's `GameController`/`UIManager`/scene:
- named/typed gump lookup (`GetGump<T>()`, `GetByName`)
- modal + keyboard-focus stack
- z-order control (`BringToFront`), open/close lifecycle hooks
- dragging
- `Scene` gets `PushGump` / `CloseGump` convenience so scenes interact with a
  clean UI layer, mirroring how CUO scenes drive `UIManager`.

**(b) Script-ready construction seam (built now, interpreter later):**
- `ControlFactory`: string type-name → control constructor registry.
- String-addressable properties and a named event/callback bus
  (`onClick="harvest"`), so controls can be built data-drivenly instead of
  hardwired in C#.
- `IScriptHost` interface the engine core depends on (never a concrete VM).
- Result: adding a Lua/TS interpreter later is **additive** - it just calls the
  same factory + event bus. No UI re-architecture required.

### 4.6 Input: binding registry, not a shared enum

`GameAction` is game-specific and does not belong in the engine. The engine
ships a small set of engine actions (camera zoom, screenshot, debug toggles)
plus a **binding table keyed by string (or int)** loaded from config. Games
define their own actions and register bindings. String action names are also
exactly what a future Lua UI script references, so this choice pays off twice.
`InputManager` keeps typed convenience for hot-path movement.

### 4.7 GameHost / service composition

`GameHost` (was `GameController`) owns only engine services: batcher,
graphics, `InputManager`, `SceneManager`, `AudioManager`, sim/world clock,
`UoContent`, config, save. The game supplies: initial scene, its `TConfig`/save
participants, its action bindings, and gameplay. `Scene` still holds a
back-reference to the host, but typed as the engine `GameHost`, so scenes in
any prototype get the same services without the engine knowing about any game.

## 5. Rollout (incremental, each step builds + runs + is visually verified)

1. **[DONE]** Create `UOA.Engine.csproj`; move the pure-engine files; rename
   their `TEF.*` namespaces to `UOA.*`; fix `ExilesForge` usings. Build + run -
   visually identical, user-verified. Two decouplings were required for the
   engine to compile without a game reference (both done): `InputManager`
   became action-agnostic (int-keyed registry; game keeps `GameAction` + a
   `TEF.Input.InputActions` extension shim so call sites are unchanged - this
   also completes most of step 6), and `GameController.OnExiting` now calls a
   `Scene.OnHostExiting()` virtual hook that `WorldScene` overrides instead of
   referencing `WorldScene` directly. `UOA.Engine` builds standalone with zero
   game references.
2. **[DONE]** Create `UOA.World.csproj`; move the UO-map toolkit (namespace
   `UOA.World`, the 8 files listed in 4.2). Build + run - user-verified (world,
   day/night, player occlusion, entity harvest, picking all unchanged). One
   decoupling was required: `TileRenderer.Draw` took a concrete `PlayerEntity`
   and `EntityRenderSystem`, which would have inverted the dependency. Both are
   now the interfaces `IWorldPlayer` / `IWorldEntitySource` (defined in
   `UOA.World/WorldInterfaces.cs`), implemented by the game's `PlayerEntity` /
   `EntityRenderSystem` - so those gameplay types stay in the game per 4.2 and
   `UOA.World` builds standalone with zero game references.
3. **[DONE]** Generalize config (4.3) and save (4.4); port TEF's schema onto
   the new APIs. Build + run - user-verified (config loads with FPS preserved;
   save/load round-trip restores player/tree/wood/clock). Details: `GameSettings`
   renamed to engine `EngineSettings`; new generic `ConfigManager<TConfig>` +
   `SaveManager`/`ISaveParticipant`/`SaveSection` live in `UOA.Persistence`
   (UOA.Engine); the game's `TefConfig` wraps `EngineSettings` (+ `TefApp.AppId`
   = "ExilesForge"); WorldScene registers four `SaveSection`s (game/player/
   clock/entities). NOTE: both config and save file **formats changed** (config
   now nests engine settings under `"Engine"`; save is now a section-keyed
   document), so pre-4.5 files are incompatible - Program.cs self-heals a
   missing/empty engine section by reseeding to defaults rather than crashing.
   Also fixed a latent bug: `WorldScene.OnHostExiting` (save-on-clean-exit) was
   never actually overridden after step 1 - autosave masked it; now in place.
4. **[DONE]** `UIManager` window-management upgrades (4.5a). Build + run -
   user-verified. `UIManager` gained named/typed lookup (`GetByName`,
   `GetGump<T>`), z-order (`BringToFront`, auto on drag-grab), a modal stack,
   open/close lifecycle (`Control.OnOpened`/`OnClosed`), and dragging of
   `Draggable` controls; `Control` gained `Name`/`IsModal`/`Draggable`/
   `FindByName`. `Scene` now owns a shared `Ui` layer with `PushGump`/
   `CloseGump`; all three game scenes moved off their private `_ui` fields onto
   it. Exercised in TEF: the Resources panel is named + draggable (anchors
   top-right once, then drag-to-move + raise-to-front); the New Game confirm
   dialog is `IsModal` (blocks the title buttons underneath via the manager
   rather than the dim panel).
5. **[DONE]** `ControlFactory` + `IScriptHost` seam (4.5b), no interpreter.
   Build + run - user-verified. `ControlFactory` (type-name -> ctor registry,
   `CreateDefault` for the built-ins), `ControlProperties.Set` (reflection +
   coercion string-property setter), `IScriptHost` + `DelegateScriptHost`
   (`UOA.Scripting`) for name-dispatched actions. `Button` gained a
   parameterless ctor + settable `Text` so it's factory-constructible.
   Exercised in TEF: the Resources panel's title label + Reset button are built
   via the factory + string properties, and the button dispatches
   `Invoke("resetWood")` through the host.
6. **[CORE DONE in step 1]** Input binding registry (4.6): `InputManager` is
   already int-keyed and TEF's `GameAction` set is ported onto it via the
   `InputActions` extension shim. Remaining (deferred, no consumer yet):
   persist rebinds to `config.json` - add when a rebinding UI exists.

Each step is independently shippable. If any step is a bad time to continue,
the project is left in a working, verified state.

## 6. Acceptance criteria

- [x] `UOA.Engine` and `UOA.World` build as separate assemblies; neither
  references any `ExilesForge`/`TEF` type (verified: both compile standalone;
  only `TEF.` left in engine code is a comment).
- [x] `ExilesForge` runs identically to pre-refactor: title → spawn select →
  world, harvesting, save/load, config, day/night, all debug toggles, the
  chunk-mesh renderer and picking - all unchanged, verified across steps 1-5.
- [x] Config: TEF's settings load/save through `ConfigManager<TefConfig>` with
  the engine section split out; hand-editing the file still round-trips (plus
  self-heal for a missing/empty engine section).
- [x] Save: TEF's data persists via registered `ISaveParticipant`s; `SaveManager.Load`
  skips sections a participant didn't find, so a partial/older save doesn't throw.
- [x] UI: the Resources panel works through the upgraded `UIManager` - it's
  named (`"resources"`) and its z-order path is exercised (drag raises it via
  `BringToFront`). `GetByName`/`GetGump<T>` available for consumers.
- [x] The `ControlFactory`/`IScriptHost` seam exists and builds the Resources
  panel's label + button data-drivenly, dispatching the click by name - no
  interpreter.
- [ ] A throwaway "hello scene" in a second, empty test project referencing
  only `UOA.Engine`. NOT built - the standalone compile of `UOA.Engine` already
  proves independence; the real second-consumer proof lands when the next
  prototype is created.

## 7. Open questions (not blocking design)

- Exact `IScriptHost` surface - settle when the interpreter is actually chosen;
  keep the interface minimal until then.
- Whether `EngineSettings` keybinds and the input binding registry (4.6) share
  one serialized representation or stay separate.
- When (not whether) to promote the entity registry to a `UOA.Gameplay`
  module - defer until a second prototype needs it (rule-of-three).
