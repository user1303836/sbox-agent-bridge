# ARPG POC First Pass

Date: 2026-04-29
Updated: 2026-04-30

This note captures the first isometric action-RPG vertical slice built through the bridge against the local test project at:

`C:\Users\hidd3n\Documents\s&box projects\testproject`

## Implemented In Test Project

- Added `Code/ArpgDemo/ArpgDemoController.cs`.
- Added an `Inventory` input action bound to `i` in `ProjectSettings/Input.config`.
- Added a saved `ARPG Demo Controller` GameObject with an `ArpgDemoController` component to `Assets/scenes/minimal.scene`.

The controller builds the playable slice at runtime:

- Isometric follow camera.
- Click-to-move warrior with sword visual.
- Left-click primary attack and right-click alternate attack.
- Shift-click stand-ground attacks.
- Health, energy, coin count, and inventory HUD.
- Melee range checks, damage variance, critical hits, attack intervals, and alternate energy cost.
- Wandering/chasing zombie NPCs with slower melee attacks.
- Zombie death, delayed respawn, 50% coin drops, and coin pickup.
- Procedural gore chunks, blood pools, and temporary visual cleanup.
- Darker arena props using built-in/cached models.

## Second UI/Combat Pass

- Reworked the runtime HUD toward an action-RPG layout with bottom health and energy orbs, a compact combat status line, an inventory panel, and a framed action bar.
- Added four hotkeyed skills on `1`, `2`, `3`, and `4`: Cleave, Executioner's Cut, Blood Rush, and War Cry.
- Added mouse hover tooltips and click/activation visual feedback for action bar skills.
- Added cooldown readouts and energy-cost gating for skill use.
- Rebuilt the minimap as a top-right HUD panel with player, zombie, and loot blips.
- Added a simple zombie melee attack visual using tint, scale, and a short anchored lunge.

## State, Collision, And Animation Pass

- Added a first status-effect model for player buffs and NPC debuffs.
- Added top-right buff/debuff rows beside the minimap.
- Represented War Cry as a 5-second player buff that restores energy and halves incoming physical damage.
- Updated Blood Rush so enemies hit along the dash path are stunned for 1.5 seconds.
- Added visible stun swirl markers above stunned zombies.
- Changed Cleave into a frontal arc attack with a visible sweeping effect.
- Added manual 2D collision resolution for player, zombies, and arena props so actors do not freely stack or pass through obstacles.
- Added collider components to runtime actor/prop objects as editor-visible intent, while movement is still resolved manually by the prototype controller.
- Added procedural idle, move, attack, dash, cleave, execution, war cry, and zombie attack poses. This is not yet true model animation graph playback; it is a compile-safe presentation layer until we verify citizen animation graph parameters.
- Fixed shift-attack queueing so hold-position attacks do not leave a target queued for movement on the next frame.
- Fixed orb fill updates to use pixel heights so health/energy depletion moves downward visibly.

## Agent Bridge Tool-Surface Fixture Pass

- Added a saved editor-authored fixture yard named `ARPG Agent Bridge Fixture Yard ...` to `scenes/minimal.scene`.
- Authored fixture objects for a blood shrine, loot chest, breakable bone cache, cursed obelisk, training dummy, loose reparent marker, and a prefab-instantiated loot chest copy.
- Created project materials under `Assets/materials/agent_bridge/` and assigned them to fixture `ModelRenderer` components.
- Created `Assets/sounds/agent_bridge/arpg_cave_fixture.sound` from a built-in ambience sound file.
- Created `Assets/prefabs/agent_bridge/arpg_loot_chest_fixture.prefab` from a live scene GameObject and instantiated it back into the active editor scene.
- Added box colliders and static rigidbodies to fixture props, assigned sound components, added a simple joint component, and verified a raycast hit against the blood shrine collider.
- Used `scene.batch` to compose object creation, reparenting, duplicate/rename, transform, selection/focus, asset assignment, sound assignment, physics, material-property, and raycast actions.
- Saved `scenes/minimal.scene` through `editor.save_scene`, with `saveVerified: true`.

## Verification

- `editor.compile_status` reports `local.testproject` with `buildSuccess: true` and `errorCount: 0`.
- After the state/collision pass, `editor.compile_status` sequence 61 reports `local.testproject` with `buildSuccess: true` and `errorCount: 0`.
- `component.list_types` finds `ArpgDemoController`.
- `scene.batch` created the controller object, added the component, and ran `editor.save_scene`.
- `editor.save_scene` reported `saveVerified: true` for `scenes/minimal.scene`.
- Tool-surface fixture pass ended with `editor.compile_status` sequence 80, `buildSuccess: true`, `errorCount: 0`.
- `scene.hierarchy` verified the fixture yard with 7 children and expected component counts.
- `sound.preview` returned a valid playing `SoundHandle` for `sounds/impacts/melee/impact-melee-flesh.sound`.
- `prefab.create`, `prefab.list`, `prefab.get_info`, and `prefab.instantiate` were all verified after adding an `AssetSystem` load fallback and a RootObject deserialization path.
- `editor.open_scene` with `forceReload: true` reloads `scenes/minimal.scene` after play/stop session staleness and verifies:
  - `rootCount: 10`
  - `componentCount: 21`
  - `ArpgDemoController: 1`
- `editor.open_scene` now falls back through `AssetSystem.FindByPath` when direct `ResourceLibrary.Get<SceneFile>` fails from the bridge add-on context. This recovered `scenes/minimal.scene` after a stale/empty active editor session.

## Bridge Lessons

- `editor.open_scene` needed a `forceReload` mode. After play/stop, the active editor session can remain present but expose an empty scene to bridge reads until the sourced scene is reopened from disk.
- `editor.open_scene` also needs editor-asset resolution, not only resource-library resolution. In the live ARPG iteration, `ResourceLibrary.Get<SceneFile>("scenes/minimal.scene")` failed from the bridge context while `AssetSystem.FindByPath("scenes/minimal.scene").LoadResource<SceneFile>()` succeeded.
- The current play feedback loop is not enough for runtime inspection. `editor.play` can report play mode started, but follow-up scene reads may target a stale editor session instead of the live game session. This is a blocker for agent confidence during game POCs.
- In the 2026-04-30 UI pass, `editor.play` reported `hasGameSession: true` immediately, but subsequent `editor.play_state` reads reported `isPlaying: true` with `hasGameSession: false`, and `scene.summary` only saw the saved editor scene. Runtime-scene targeting needs to be made explicit before agents can verify HUD/zombie objects that are created during play.
- `editor.logs` needs cursor/timestamp support. Right now it tails the file and can surface stale compile/runtime errors as if they are current unless the caller uses very specific filters.
- `ModelRenderer` should be created disabled until a valid model is assigned, then enabled. Creating it enabled with no model can assert in s&box.
- `AssetSystem.CreateResource("material", ...)` returned null for `.vmat` creation on this install. The bridge now writes a minimal `.vmat` source file, registers it, and compiles it.
- `SoundEvent` read-back needs defensive metadata access. Some built-in sound files throw when reading duration/channel/rate before load; the bridge now reports those fields best-effort.
- `PrefabFile.Load(path)` did not load a project-created prefab by the same path returned by `AssetSystem`. The bridge now falls back to `AssetSystem.FindByPath(...).LoadResource<PrefabFile>()`.
- Runtime-oriented `GameObject.Clone(prefab, ...)` failed in the editor bridge context with `No Active Scene`. The bridge now instantiates prefabs by deserializing `PrefabFile.RootObject` into the active editor scene with fresh GUIDs.
- `component.add` still cannot add local game components by C# type name from the editor bridge. Existing local component instances can be inspected, but editor-side type lookup does not surface `ArpgDemoController`, `AgentBridgeMutationFixture`, or newly created `AgentBridgeArpgFixture` as addable types.

## POC Gaps

- The visual asset pass uses built-in/cached citizen/dev models and procedural props. A real distributable sample should include an explicit free asset pipeline or a documented s&box asset dependency.
- Sound hooks exist as component properties, but defaults are empty because the local project does not yet include reliable ambient/zombie/weapon sound events. A proper sound asset import/use path is still needed.
- Local game-component creation by type name remains unresolved. This blocks agent-authored custom marker/metadata components until we find the editor's component-picker API or a safe scene-serialization path.
- Joint creation works, but target assignment is not wired because the verified `Joint.Object2` property is read-only.
- The bridge cannot yet inspect runtime-only objects created during play. For the next POC iteration, add a runtime feedback channel or play-session scene targeting so an agent can verify the warrior, zombies, HUD, loot, and gore state directly.
- Combat has simple state/visuals rather than animation graph integration. Animation graph/property support should come after runtime feedback is trustworthy.

## Recommended Next Bridge Work

1. Add play-session-aware scene reads, or a separate runtime snapshot command that can inspect the live game session.
2. Add timestamp/cursor-based log reads so feedback ignores stale errors.
3. Add a small runtime self-report component/tool path for gameplay state: player health, energy, position, zombie count, loot count, and last event.
4. Add resource/asset import and selection helpers for models, decals, particles, and sounds.
