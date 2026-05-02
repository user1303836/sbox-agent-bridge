# ARPG POC First Pass

Date: 2026-04-29
Updated: 2026-05-01

This note captures the first isometric action-RPG vertical slice built through the bridge against a local s&box test project. Paths below are project-relative unless explicitly marked as temp output.

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

## Special Object Hover And Local Component Pass

- The runtime ARPG controller now discovers editor-authored special objects from the live scene.
- Special objects are detected through `AgentBridgeArpgFixture` when present, with bridge-fixture names as a fallback.
- Hovering the loot chest, blood shrine, breakable bone cache, cursed obelisk, or related bridge fixture objects now tints and pulses the object and shows a center-bottom interaction prompt.
- Special objects are added to the runtime manual obstacle list so the player and zombies do not walk through them.
- Special objects appear on the minimap, with a brighter marker when hovered.
- `component.add` can now add local compiled game components by exact C# type name through a serialized-probe fallback. Live verified by adding `AgentBridgeArpgFixture` to the loot chest, blood shrine, breakable bone cache, and cursed obelisk.
- `component.set_property` was live verified on those local fixture components for enum, string, and integer properties.
- The scene was saved after this pass through `editor.save_scene`, with `saveVerified: true`.

## Generated Prop Visual Pass

- Imported a generated dark-fantasy prop kit into `Assets/models/agent_bridge/arpg_props/source/`.
- Converted valid GLB meshes to OBJ sources, extracted embedded albedo images for later material work, and normalized converted GLB origins so their bases sit at local `z = 0`.
- Created `.vmdl` ModelDoc wrappers for 12 props: cobblestone floor tile, ruined wall, ruined arch, wooden barricade, iron brazier, candle cluster, loot chest, cursed obelisk, bone pile, grave marker, hanging banner, and waypoint rune pedestal.
- Created simple project `.vmat` materials under `Assets/materials/agent_bridge/arpg_props/`.
- Updated `ArpgDemoController` to use generated props for runtime arena dressing: cobblestone tile grid, ruin walls/arch, barricades, graves, bone piles, banner, waypoint pedestal, candle clusters, and brazier lights.
- Swapped saved bridge-fixture special objects from dev boxes to generated prop models and saved `scenes/minimal.scene`.
- Live bridge testing surfaced a direct-IPC vector footgun: `{ "z": 0 }` passed to `gameobject.set_transform` zeroed omitted axes. The bridge now rejects incomplete `Vector3` payloads unless `x`, `y`, and `z` are all present.

## Verification

- `editor.compile_status` reports `local.testproject` with `buildSuccess: true` and `errorCount: 0`.
- After the state/collision pass, `editor.compile_status` sequence 61 reports `local.testproject` with `buildSuccess: true` and `errorCount: 0`.
- Earlier local runs saw `ArpgDemoController` through component lookup while adding the controller object, but current verification shows local component enumeration is not reliable through `component.list_types`; use exact-name `component.add` for compiled local types until local discovery is improved.
- `scene.batch` created the controller object, added the component, and ran `editor.save_scene`.
- `editor.save_scene` reported `saveVerified: true` for `scenes/minimal.scene`.
- Tool-surface fixture pass ended with `editor.compile_status` sequence 80, `buildSuccess: true`, `errorCount: 0`.
- `scene.hierarchy` verified the fixture yard with 7 children and expected component counts.
- `sound.preview` returned a valid playing `SoundHandle` for `sounds/impacts/melee/impact-melee-flesh.sound`.
- `prefab.create`, `prefab.list`, `prefab.get_info`, and `prefab.instantiate` were all verified after adding an `AssetSystem` load fallback and a RootObject deserialization path.
- `component.add` local game-component creation by type name was verified with `AgentBridgeArpgFixture` through `creationMode: serializedProbe`.
- `component.set_property` was verified on local `AgentBridgeArpgFixture` properties including `Kind`, `DisplayName`, `Tooltip`, `Effect`, and `Value`.
- `scene.summary` after local fixture metadata showed 18 objects and 46 components, including 4 `AgentBridgeArpgFixture` components and no temporary probe objects left behind.
- `editor.save_scene` saved the fixture metadata with `saveVerified: true` and returned `hasUnsavedChanges: false`.
- All 12 generated `.vmdl` prop wrappers were force-loaded through `asset.assign_model` and then read back as compiled/up-to-date with `isCompileFailed: false`.
- Generated prop assignment to saved special fixtures was verified through `scene.batch` and persisted through `editor.save_scene`.
- Direct IPC negative test verified incomplete vector payloads now fail with `Payload property 'position' must include numeric x, y, and z fields`, and read-back confirmed the target object position was unchanged.
- Play/stop smoke after the generated prop pass reported compile success and returned to `isPlaying: false`.
- `editor.open_scene` with `forceReload: true` reloads `scenes/minimal.scene` after play/stop session staleness and verifies:
  - `rootCount: 10`
  - `componentCount: 21`
  - `ArpgDemoController: 1`
- `editor.open_scene` now falls back through `AssetSystem.FindByPath` when direct `ResourceLibrary.Get<SceneFile>` fails from the bridge add-on context. This recovered `scenes/minimal.scene` after a stale/empty active editor session.
- `asset.inspect_model` was live verified on `models/agent_bridge/arpg_props/cursed_obelisk.vmdl`.
- `visual.capture_camera` was live verified against the active main camera and produced a non-empty PNG with camera metadata and luminance stats.

## Lighting And Visual/Spatial Feedback Pass

- Added brighter runtime and saved-scene lighting so the dark-fantasy scene remains readable: ambient fill, directional light tuning, warm point lights, spot lights, and post-process adjustments.
- Verified rendering-oriented component mutation through AmbientLight, PointLight, SpotLight, DirectionalLight, FilmGrain, Tonemapping, Bloom, PostProcessVolume, Vignette, ColorAdjustments, decals, and basic particle components.
- Added `asset.inspect_model` so agents can inspect model bounds, render bounds, physics bounds, material slots, orientation candidates, footprints, and ground offsets before placing assets.
- Added `visual.capture_camera` so agents can capture the active camera to a PNG under `%TEMP%/sbox-agent-bridge/captures` and read luminance stats. The first ARPG capture reported average luminance `0.1332` and dark pixel ratio `0.3145`, which gives agents a concrete visibility signal instead of relying only on scene metadata.
- Documented the remaining spatial reasoning gap: geometry bounds can suggest candidate rotations and ground offsets, but they cannot prove that a prop is semantically upright. Spatial placement v1 now provides persistent orientation overrides and a high-level placement helper; the remaining work is seeding/confirming overrides across the prop kit.

## Spatial Placement V1 Pass

- Added project-local orientation override storage at `Assets/agent_bridge/orientation_overrides.json`.
- Live IPC wrote and read back a cursed obelisk override with `baseRotation.pitch: 90`, `forwardAxis: +Y`, and `confidence: human_verified`.
- Added `gameobject.place_asset`, which creates a renderer-backed model object, applies the stored base rotation plus requested yaw, aligns transformed render bounds to ground, and returns object/component/bounds read-back.
- Verified placement by creating `Agent Bridge Spatial V1 Obelisk 20260501-100811` from `models/agent_bridge/arpg_props/cursed_obelisk.vmdl` at a requested ground position, saving `scenes/minimal.scene`, force-reloading the scene, finding the object again, and reading back a valid persisted `ModelRenderer.Model` resource reference.

## ARPG Feature Slice: Orbs, Terrain, Whirlwind, Elites, Inventory, And Loot

- Reworked the health and energy orbs so the visible colored fill is bottom-anchored and changes height directly from current health/energy instead of relying on a dark overlay mask.
- Added runtime terrain elevation support through deterministic ground-height helpers plus raised terraces, ramps, and ash mounds. Actor movement now resolves back onto the computed ground height after flat 2D collision solving.
- Changed the runtime player and zombies to use `SkinnedModelRenderer` plus `CitizenAnimationHelper`, with the citizen animation graph explicitly assigned at `models/citizen/citizen.vanmgrph`.
- Adjusted the player weapon presentation so the sword attaches to the citizen `hold_R`/`hand_R` attachment when available, with a body-relative fallback, and changes pose during attacks and Whirlwind.
- Replaced the first skill with Whirlwind. Holding hotkey `1` now drains energy over time, spins the character, damages nearby zombies on a tick interval, reduces movement speed by 20%, and disables player-vs-zombie collision while the channel is active.
- Added elite zombie variants with higher health, damage, larger collision radius, slower movement, purple tint, guaranteed coin drops, and better item-drop odds.
- Added a Path of Exile-style grid inventory model with 10 columns by 5 rows. Items have slot sizes such as 2x3 greatswords and 1x1 amulets.
- Added equipment slots for mainhand, offhand, head, chest, amulet, and boots. Dragging an item panel onto a compatible equipment slot equips it; equipped stats affect player damage, armor, health, energy, movement speed, and crit chance.
- Added zombie loot tables that drop coins and usable gear including weapons, armor, boots, offhands, and amulets. Item hover tooltips show rarity, size, slot, and stats.
- Fixed a runtime startup bug exposed by bridge verification: if `OnStart` ran while not playing, the ARPG world could remain unbuilt in play mode. `OnUpdate` now lazily builds the world the first time play mode is active.

Verification:

- `editor.compile_status` after the pass reported `local.testproject` and `local.testproject.editor` with `buildSuccess: true` and `errorCount: 0`.
- A follow-up hotload after the `hold_R` sword attachment pass reported `local.testproject` and `local.testproject.editor` with `buildSuccess: true` and `errorCount: 0`; the remaining warnings were from the installed bridge editor library.
- `editor.open_scene` with `forceReload: true` recovered the saved scene after play/stop stale-session behavior.
- `editor.play` reported `hasGameSession: true`.
- `visual.capture_camera` succeeded after the lazy runtime build fix and wrote `20260501-150633-arpg-feature-slice-citizen-graph-854f95651641455fac8c592ba20f7f42.png` under `%TEMP%/sbox-agent-bridge/captures`.
- A final post-attachment smoke wrote `20260501-151835-arpg-sword-attachment-smoke-d0d752a2eef143e1990b3281034dab23.png` under `%TEMP%/sbox-agent-bridge/captures` with nonzero byte count and average luminance `0.1415`.
- The capture verified a nonblank runtime scene with visible elevation changes. Camera capture does not include the screen UI overlay, so the orb fill, inventory grid, equipment drag/drop, and item tooltips still need human/editor visual verification or a future UI/runtime inspection bridge.
- Runtime log verification was initially weak because `editor.logs` returned stale compile errors even after current compile status was green. The bridge now supports `afterIndex` cursor reads; callers should establish a baseline before each change and inspect only newer lines.

## Bridge Lessons

- `editor.open_scene` needed a `forceReload` mode. After play/stop, the active editor session can remain present but expose an empty scene to bridge reads until the sourced scene is reopened from disk.
- `editor.open_scene` also needs editor-asset resolution, not only resource-library resolution. In the live ARPG iteration, `ResourceLibrary.Get<SceneFile>("scenes/minimal.scene")` failed from the bridge context while `AssetSystem.FindByPath("scenes/minimal.scene").LoadResource<SceneFile>()` succeeded.
- The initial play feedback loop was not enough for runtime inspection. `editor.play` could report play mode started, but follow-up scene reads sometimes targeted a stale editor session instead of the live game session. The bridge now supports explicit `targetSession: "runtime"` reads and `editor.stop stopAll`; post-transition stale-tab restoration is still needed.
- In the 2026-04-30 UI pass, `editor.play` reported `hasGameSession: true` immediately, but subsequent `editor.play_state` reads reported `isPlaying: true` with `hasGameSession: false`, and `scene.summary` only saw the saved editor scene. Explicit runtime targeting addresses the core read-path issue; generic viewport/HUD capture remains open.
- In the 2026-05-01 ARPG feature slice, runtime camera capture only worked after fixing the game component to lazily build the world once play mode is active. This was a game-code bug, but it also reinforced the need for a runtime self-report or game-session object query.
- `editor.logs` now supports `afterIndex` cursor reads, which avoids treating older compile/runtime errors as current when callers establish a baseline first. Structured log-event capture is still future work.
- `ModelRenderer` should be created disabled until a valid model is assigned, then enabled. Creating it enabled with no model can assert in s&box.
- `AssetSystem.CreateResource("material", ...)` returned null for `.vmat` creation on this install. The bridge now writes a minimal `.vmat` source file, registers it, and compiles it.
- `SoundEvent` read-back needs defensive metadata access. Some built-in sound files throw when reading duration/channel/rate before load; the bridge now reports those fields best-effort.
- `PrefabFile.Load(path)` did not load a project-created prefab by the same path returned by `AssetSystem`. The bridge now falls back to `AssetSystem.FindByPath(...).LoadResource<PrefabFile>()`.
- Runtime-oriented `GameObject.Clone(prefab, ...)` failed in the editor bridge context with `No Active Scene`. The bridge now instantiates prefabs by deserializing `PrefabFile.RootObject` into the active editor scene with fresh GUIDs.
- `component.add` can now add local compiled game components by exact C# type name, but `component.list_types` still cannot enumerate those local types through editor-side `Game.TypeLibrary`.
- A rejected local-component fallback is documented in current status notes: do not deserialize a modified full target GameObject blob just to append a component, because live testing showed it duplicates existing components. The safe path only uses a temporary empty serialized probe to resolve the runtime type.
- Direct IPC callers need bridge-side schema validation too, not only MCP-side Zod schemas. The generated prop pass showed why `Vector3` parsing must reject partial vectors before mutation.

## POC Gaps

- The first generated prop pass imports static environment models, but still uses simple tint/material overrides rather than fully authored PBR material maps.
- The visual asset pass still uses built-in/cached citizen/dev models for the player and zombies. Generated or authored animated characters need a separate model/animation graph pass.
- Sound hooks exist as component properties, but defaults are empty because the local project does not yet include reliable ambient/zombie/weapon sound events. A proper sound asset import/use path is still needed.
- Local game-component type discovery remains unresolved. Agents can add a known compiled type by exact name, but they cannot yet ask the bridge to list all local project component types.
- Joint creation works, but target assignment is not wired because the verified `Joint.Object2` property is read-only.
- The bridge can now inspect the live `GameSession` with `targetSession: "runtime"` and component-authored runtime test actions. Generic runtime-only object queries and panel hierarchy inspection are still limited to what the bridge can resolve through normal scene/component reads or explicit self-report hooks.
- `visual.capture_camera` verifies the world camera but not the screen UI overlay. The ARPG runtime self-report can verify logical orb/inventory/equipment state, but pixel/viewport HUD capture or generic panel hierarchy inspection is still needed for visual verification.
- Combat has simple state/visuals rather than animation graph integration. Animation graph/property support should come after runtime feedback is trustworthy.

## Recommended Next Bridge Work

1. Seed human-verified orientation overrides for the rest of the generated ARPG prop kit.
2. Expand runtime inspection beyond the verified `targetSession: "runtime"` reads and component-authored self-report hooks, especially generic panel hierarchy and runtime-only object queries.
3. Add structured log-event capture if a stable editor-library hook is verified.
4. Standardize reusable runtime self-report hooks for gameplay state: player health, energy, position, zombie count, loot count, and last event.
5. Add resource/asset import and selection helpers for models, decals, particles, and sounds.
