# Capability Matrix

Status meanings:

- **Verified**: tested against a live s&box editor.
- **Verified gap**: tested against a live editor, source-analysis smoke, reference lookup, or local CI check and confirmed as an intentional current boundary rather than untested behavior.
- **Partial**: useful behavior is verified, but a known limitation remains.
- **Implemented**: code exists, but live editor verification is pending.
- **Unverified**: the official docs cover this area, but no dedicated bridge smoke has verified it.
- **Planned**: intended, not implemented.
- **Blocked**: known blocker or missing verified API.

## Current Verification Environment

- Date: 2026-05-02
- s&box project: Generated fresh Minimal Game project `local.agent_bridge_mvp_fresh`, plus existing ARPG/boxing testbeds for prior gameplay smokes
- Bridge install path: `Libraries/sbox_agent_bridge`
- Transport: file IPC at `%TEMP%/sbox-agent-bridge-fresh-mvp` for the latest full-suite verification
- Official docs sweep source: `https://sbox.game/dev/doc/` navigation index reviewed on 2026-05-01. Missing top-level doc areas are listed below even when the bridge has no tool support yet.

## Bridge And Editor

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Bridge status | `bridge.status` | `editor` / `status` | Verified | Returns running state, IPC root, active scene, play state. |
| Bridge doctor/readiness | `bridge.doctor` | `editor` / `doctor` | Verified | Reports bridge/MCP version context, IPC writability, project/session health, compile health, recent bridge logs, stale play-tab warnings, and a next suggested action. Live IPC returned all-pass checks on the MVP test project and on the generated fresh `local.agent_bridge_mvp_fresh` project. |
| Editor context | `editor.context` | `editor` / `context` | Verified | Direct file-IPC read verified with selected GameObject details. Includes active tab/session metadata so agents can see which scene tab commands will target. |
| Project info | `editor.project_info` | `editor` / `project_info` | Verified | Reads active project identity, root/assets/code/editor paths, compiler availability, bridge install path, and current process directory. Verified by `mcp-server/test/bootstrap-smoke.ts`. |
| List editor tabs | `editor.tabs` | `editor` / `tabs` | Verified | Lists open `SceneEditorSession` tabs with index, scene id, active flag, source path, dirty state, and play state. Live IPC verified with both `Untitled Scene` and `scenes/minimal.scene` open. |
| Activate editor tab | `editor.activate_tab` | `editor` / `activate_tab` | Verified | Activates an existing editor scene tab by index, scene id, source path, or scene name. Live IPC verified by raising `scenes/minimal.scene` while an unsaved untitled tab was also open. |
| Create new scene | `editor.new_scene` | `editor` / `new_scene` | Verified | Creates a default editor scene tab, optionally names it, and guards dirty active scenes unless `discardUnsaved:true` is supplied. Live bootstrap smoke creates a blank scene before any scene mutation. |
| Open scene | `editor.open_scene` | `editor` / `open_scene` | Verified | Opens a scene resource by path and makes its session active. Falls back through `AssetSystem.FindByPath` when direct resource lookup fails. Supports `forceReload` for reloading an already-open sourced scene when play/stop leaves the editor session stale. |
| Recover editor scene | `editor.recover_scene` | `editor` / `recover_scene` | Verified | Stops playing sessions by default, reloads/reactivates a sourced scene, and returns before/after tab snapshots. Live IPC restored `scenes/minimal.scene` to one active tab. |
| Save scene | `editor.save_scene` | `editor` / `save_scene` | Verified | Reports before/after dirty state and scene source path; guards untitled scenes instead of opening a save-as flow. Live IPC verified dry-run, no-source skip behavior, and disk write against `scenes/minimal.scene`. |
| Save scene as path | `editor.save_scene_as` | `editor` / `save_scene_as` | Verified | Saves the active editor scene to a supplied project scene path without opening UI. Filters invalid/null serialized children before writing, registers/compiles the scene asset, can activate/reload the saved scene, and verifies persisted object read-back in `smoke:bootstrap`. |
| Undo | `editor.undo` | `editor` / `undo` | Verified | Verified during earlier scene mutation smoke. Avoid coupling current undo checks to `gameobject.destroy` until the native delete/undo issue is reverified. |
| Redo | `editor.redo` | `editor` / `redo` | Verified | Verified during earlier scene mutation smoke. Avoid coupling current redo checks to `gameobject.destroy` until the native delete/undo issue is reverified. |
| Selection read | `editor.get_selection` | `editor` / `get_selection` | Verified | Returns typed selection entries; GameObject selection verified. |
| Selection set | `editor.set_selection` | `editor` / `set_selection` | Verified | Accepts GameObject ids only; verified with read-back count. |
| Frame/focus object | `editor.frame_object` | `editor` / `frame_object` | Verified | Calls `SceneEditorSession.FrameTo` for a target GameObject bounds. |
| Play state | `editor.play_state` | `editor` / `play_state` | Verified | Reads active scene by default and supports `targetSession: runtime` to resolve the live `GameSession`; response includes target-session metadata. Live IPC verified runtime resolution after play. |
| Play mode start | `editor.play` | `editor` / `play` | Verified | Calls `SceneEditorSession.SetPlaying(...)` on the resolved editor session and returns read-back play state. Defaults to `targetSession: editor` so an active runtime tab controls its parent editor scene. |
| Play mode stop | `editor.stop` | `editor` / `stop` | Verified | Calls `SceneEditorSession.StopPlaying()` on the resolved editor session and returns immediate read-back with `transitionPending` when the editor settles on a later frame. Supports `stopAll: true` to clear every currently playing editor session before smoke tests. |
| Compile wait | Composed MCP helper | `editor` / `wait_compile` | Verified | MCP-side polling helper that waits for observed compile groups to settle without blocking the editor frame pump. Supports `sinceSequence` for post-edit waits. |
| Runtime ready wait | Composed MCP helper | `editor` / `wait_runtime` | Verified | MCP-side polling helper that waits for runtime-targeted play state and `scene.summary targetSession=runtime` to resolve a live `GameSession`. Runtime smoke now uses this instead of fixed sleeps. |
| Stop settle wait | Composed MCP helper | `editor` / `wait_stopped` | Verified | MCP-side polling helper that waits until no editor scene sessions are still playing after `editor.stop` or `stopAll`. Uses `editor.tabs` session metadata. |
| Bootstrap smoke | Composed smoke script | `npm run smoke:bootstrap` | Verified | Clean-room editor bootstrap flow: stops play mode, waits for compile, reads project info, creates a new scene, creates a marker GameObject, saves as `scenes/agent_bridge/smoke/bootstrap_smoke.scene`, reloads it, verifies persisted marker read-back, and restores the original sourced scene. |
| Recent logs | `editor.logs` | `editor` / `logs` | Verified | Tails `sbox-dev.log`; raw lines are authoritative, log level is explicitly inferred, and `afterIndex` cursor reads are live verified so agents can ignore stale log lines. |
| Compile status | `editor.compile_status` | `editor` / `compile_status` | Verified | Tracks compile groups from observed `compile.started` events; live IPC returned compiler diagnostics and zero errors after the fix. |
| Combined feedback | `editor.feedback` | `editor` / `feedback` | Verified | Returns play state, compile status, and recent logs for agent edit/test loops; supports `targetSession` and `afterIndex` for runtime-aware fresh feedback. |

## Scene Read

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Scene summary | `scene.summary` | `scene` / `summary` | Verified | Returns object/component counts from the resolved session; `targetSession: runtime` was live verified against the ARPG GameSession with 219 objects. |
| Scene hierarchy | `scene.hierarchy` | `scene` / `hierarchy` | Verified | Live IPC read-back verified the ARPG fixture yard hierarchy after scene mutations; now supports target-session selection. |
| Scene metadata | `scene.metadata` | `scene` / `metadata` | Verified | Reads active scene source path, active scene metadata entries, and source-file title/description metadata. Live `smoke:matrix-core` verified `scenes/agent_bridge/smoke/mvp_suite.scene` source metadata read-back. |
| Find GameObjects | `scene.find` | `scene` / `find` | Verified | Verified by finding created objects and runtime-only ARPG components with `targetSession: runtime`. |
| Object details | `scene.details` | `scene` / `details` | Verified | Includes id, parent, enabled/active state, transforms, components, child count; supports target-session selection. |
| Batch operations | `scene.batch` | `scene` / `batch` | Verified | Runs bounded action lists with per-operation result capture and `$ref` alias substitution. Live IPC verified object create/reparent/duplicate/rename/enable/selection/focus plus asset assignment, sound assignment, physics/collider/joint, material-property, and raycast actions. |
| Find in radius | `scene.find_in_radius` | `scene` / `find_in_radius` | Verified | Spatial GameObject query with center/radius, name/component filters, max results, and distance-sorted read-back. Live `smoke:matrix-core` verified a near object was returned and a far object was excluded. |

## GameObjects

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Read GameObject | `gameobject.get` | `gameobject` / `get` | Verified | Id-targeted read returning the same detail shape as `scene.details`; supports target-session selection for runtime reads. |
| Create GameObject | `gameobject.create` | `gameobject` / `create` | Verified | Uses `SceneEditorSession.Active.Scene.CreateObject(true)` and verifies via read-back; optional `parentId` was verified through `scene.batch`. |
| Destroy GameObject | `gameobject.destroy` | `gameobject` / `destroy` | Verified | Uses a direct undo-scoped editor destruction path, processes deletes, and verifies the target is gone. Live `smoke:matrix-core` verified destroy, `editor.undo` restore, and `editor.redo` destroy again. |
| Rename GameObject | `gameobject.rename` | `gameobject` / `rename` | Verified | Id-targeted, undo scoped, unique-name default verified by read-back. |
| Set transform | `gameobject.set_transform` | `gameobject` / `set_transform` | Verified | World position, Euler/quaternion rotation input, and world scale; verified by read-back. |
| Enable/disable | `gameobject.set_enabled` | `gameobject` / `set_enabled` | Verified | Verified false and true read-back on a live object. |
| Reparent | `gameobject.reparent` | `gameobject` / `reparent` | Verified | Preserves world transform by default; supports moving back to scene root. |
| Duplicate | `gameobject.duplicate` | `gameobject` / `duplicate` | Verified | Shallow scene-attached duplicate of name/enabled/transform/parent. Component and child cloning are future work. |
| Place model asset | `gameobject.place_asset` | `gameobject` / `place_asset` | Verified | Live IPC created `Agent Bridge Spatial V1 Obelisk 20260501-100811` from `models/agent_bridge/arpg_props/cursed_obelisk.vmdl`, used a stored override with `pitch: 90`, aligned render bounds to ground, saved `scenes/minimal.scene`, force-reloaded it, and read back the persisted `ModelRenderer`. |

## Components

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| List component types | `component.list_types` | `component` / `list_types` | Verified | Combines `Game.TypeLibrary.GetTypes(typeof(Component))` with runtime assembly scanning for compiled local component classes. Live `smoke:matrix-core` verified `AgentBridgeMutationFixture` appears through runtime assembly discovery. |
| List GameObject components | `component.list_on_gameobject` | `component` / `list_on_gameobject` | Verified | Id-targeted GameObject component list; supports target-session selection for runtime components. |
| Read component | `component.get` | `component` / `get` | Verified | Id-targeted component read with owning GameObject context; supports target-session selection. |
| Get properties | `component.get_properties` | `component` / `get_properties` | Verified | Read-only metadata/value inspection; defaults to `[Property]` inspector properties and includes schema hints, attributes, enum values, and reference targets for settable JSON shapes; supports runtime target sessions. |
| Add component | `component.add` | `component` / `add` | Verified | Built-in/editor-visible components use TypeLibrary. Local compiled game components can be added by exact C# type name through a serialized-probe fallback that resolves the runtime type, then calls `GameObject.AddComponent<T>()`; live verified with `AgentBridgeArpgFixture` on ARPG fixture objects without duplicating existing components. |
| Remove component | `component.remove` | `component` / `remove` | Verified | Undo scoped; live smoke verifies `component.get` fails after removal, undo restores, redo removes again. |
| Set property | `component.set_property` | `component` / `set_property` | Verified | Live smoke verifies string, bool, numeric primitives, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject`, and `Component` values through `AgentBridgeMutationFixture`; live IPC also verified resource paths on built-in `ModelRenderer.Model`, `ModelRenderer.MaterialOverride`, and non-inspector `DecalRenderer.Material` via `includeAll: true`; local `AgentBridgeArpgFixture` enum/string/int properties are verified; supports `dryRun: true`. |
| Validate property | `component.validate_property` | `component` / `validate_property` | Verified | Converts and resolves a candidate value without mutation; live smoke verifies valid conversion, invalid rejection, and unchanged fixture values. |
| Enable/disable component | `component.set_enabled` | `component` / `set_enabled` | Verified | Live smoke verifies false and true read-back. |
| Component lifecycle, interfaces, and event hooks | `script.analyze` | `script` / `analyze` | Verified | Source-level introspection detects component classes, base/interface lists, lifecycle methods such as `OnStart`/`OnUpdate`, attributes such as `[Property]`, and source markers including `ISceneStartup`/`IGameObjectNetworkEvents`. Live `smoke:scripts` verified analysis against a compiled scratch component and a network-event source probe. |

## Scripts

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Create script | `script.create` | `script` / `create` | Verified | Live IPC created `Code/ArpgDemo/AgentBridgeArpgFixture.cs`; compile status stayed green. |
| Edit script | `script.edit` | `script` / `edit` | Verified | Live IPC edited the same script and verified SHA/length change plus green compile. |
| Delete script | `script.delete` | `script` / `delete` | Verified | `mcp-server/test/capability-gap-smoke.ts` creates, edits, compiles, deletes, and verifies removal of `AgentBridgeScratch/CapabilityGapSmokeFixture.cs` using new compile sequence waits; post-delete compile status stayed green. |

## Assets, Materials, Sounds, Physics, And Prefabs

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Search assets | `asset.search` | `asset` / `search` | Verified | Live IPC found built-in models/materials and newly created ARPG materials. |
| Inspect asset | `asset.get_info` | `asset` / `get_info` | Verified | Live IPC inspected built-in material metadata and project-created assets. |
| Inspect model orientation/bounds | `asset.inspect_model` | `asset` / `inspect_model` | Verified | Live IPC inspected `models/agent_bridge/arpg_props/cursed_obelisk.vmdl`, returning asset/model metadata, model/render/physics bounds, material slots, common orientation candidates, ground offsets, footprints, and explicit semantic-orientation limitations. |
| Preview model asset | `asset.preview_model` | `asset` / `preview_model` | Verified | Creates/reuses a temporary `NotSaved` preview rig in the resolved session, renders a model from its own camera, and writes a PNG with luminance stats. Live asset smoke verified runtime-targeted preview for `models/dev/box.vmdl` with a nonblack capture. Use `targetSession: runtime` while playing for reliable captures. |
| Get model orientation override | `asset.get_orientation_override` | `asset` / `get_orientation_override` | Verified | Live IPC read back the cursed obelisk override from `Assets/agent_bridge/orientation_overrides.json` with `found:true` and `baseRotation.pitch: 90`. |
| Set model orientation override | `asset.set_orientation_override` | `asset` / `set_orientation_override` | Verified | Live IPC wrote the cursed obelisk override to `Assets/agent_bridge/orientation_overrides.json`; omitted ground offset was calculated from render bounds and reused by `gameobject.place_asset`. |
| Assign model | `asset.assign_model` | `asset` / `assign_model` | Verified | Live IPC assigned `models/dev/box.vmdl` to ARPG fixture objects with read-back. |
| Create material | `asset.create_material` | `asset` / `create_material` | Verified | Writes a simple `.vmat` source, registers it, and compiles it. Verified four project materials under `materials/agent_bridge/`. |
| Inspect material source | `asset.inspect_material` | `asset` / `inspect_material` | Verified | Loads a material and parses readable `.vmat` source key/value pairs into properties, textures, color/vector values, and scalars. Live asset smoke verified 18 properties on `materials/agent_bridge/smoke/asset_material_smoke.vmat`. |
| Assign material | `asset.assign_material` | `asset` / `assign_material` | Verified | Live IPC assigned project-created materials to `ModelRenderer.MaterialOverride`. |
| Set material property | `asset.set_material_property` | `asset` / `set_material_property` | Verified | Live IPC set `g_vColorTint` on an assigned material and returned `success: true`. |
| Set material source property | `asset.set_material_source_property` | `asset` / `set_material_source_property` | Verified | Updates or inserts `.vmat` key/value pairs in project material source files, recompiles, and returns inspect read-back. Live asset smoke verified `g_vColorTint` vector formatting and `TextureColor` path mutation. |
| List sounds | `sound.list` | `sound` / `list` | Verified | Live IPC listed sound events and sound files, including built-in melee impacts and ambience. |
| Inspect sound | `sound.get_info` | `sound` / `get_info` | Verified | Live IPC inspected a project-created sound event. Sound-file duration/channel fields are best-effort because some built-in sound handles throw when metadata is read before load. |
| Inspect scene sound components | `sound.inspect` | `sound` / `inspect` | Verified | Reads `SoundPointComponent` settings from a GameObject, including event resource, play-on-start, repeat, force-2D, volume, and pitch. Live sound smoke verified assigned component read-back. |
| Create sound event | `sound.create_event` | `sound` / `create_event` | Verified | Created `sounds/agent_bridge/arpg_cave_fixture.sound` from `sounds/ambience/cave-loop.vsnd` and compiled it. |
| Assign sound | `sound.assign` | `sound` / `assign` | Verified | Added/updated `SoundPointComponent` on shrine and dummy fixture objects. |
| Preview sound | `sound.preview`, `sound.preview_status`, `sound.stop_preview` | `sound` / `preview` / `preview_status` / `stop_preview` | Verified | Live sound smoke starts a tracked preview handle, reads status by preview id including play state/time/amplitude, stops it, and verifies stopped read-back. |
| Inspect physics components | `physics.inspect` | `physics` / `inspect` | Verified | Reads Rigidbody, Collider, and Joint summaries from a GameObject, including rigidbody flags/mass, collider shape dimensions, trigger/static flags, joint collision state, and target read-back. Live physics smoke verified all fields. |
| Add collider | `physics.add_collider` | `physics` / `add_collider` | Verified | Added box colliders to ARPG fixture props and raycasted against them. Live physics smoke verified box scale/center/static/trigger read-back through `physics.inspect`. |
| Add physics body | `physics.add_physics` | `physics` / `add_physics` | Verified | Added static/non-motion rigidbodies to fixture props. Live physics smoke verified gravity, motion, and mass override read-back. |
| Add joint | `physics.add_joint` | `physics` / `add_joint` | Verified | Creates joint components, sets collision state, assigns the linked body when supplied, and reports body/target read-back through `physics.inspect`. Live physics smoke verified a `FixedJoint` linked to the temporary anchor object. |
| Raycast | `physics.raycast` | `physics` / `raycast` | Verified | Live IPC raycast hit the Blood Shrine fixture through its collider; live physics smoke also verified a temporary box collider hit and collider read-back. |
| Scene physics event declarations | `script.analyze` | `script` / `analyze` | Verified gap | Live `smoke:scripts` verifies source-level detection of `IScenePhysicsEvents`. The bridge still does not simulate physics contact callbacks or assert callback firing. |
| Create prefab | `prefab.create` | `prefab` / `create` | Verified | Serialized a live GameObject to `prefabs/agent_bridge/arpg_loot_chest_fixture.prefab`; list/get-info verified the resource. |
| List prefabs | `prefab.list` | `prefab` / `list` | Verified | Live IPC listed built-in prefabs and the project-created ARPG prefab. |
| Inspect prefab | `prefab.get_info` | `prefab` / `get_info` | Verified | Uses `AssetSystem` fallback when `PrefabFile.Load(path)` cannot load a project-created prefab path. |
| Instantiate prefab | `prefab.instantiate` | `prefab` / `instantiate` | Verified | Instantiates by deserializing prefab `RootObject` into the active editor scene, remapping prefab GUIDs to fresh instance GUIDs, and preserving `__PrefabIdToInstanceId`. Live prefab smoke verified id-map count and transform override patching. |
| Inspect prefab instance metadata | `prefab.inspect_instance` | `prefab` / `inspect_instance` | Verified | Reads serialized `__Prefab`, `__PrefabInstancePatch`, patch counts/samples, and prefab id maps from a live GameObject. Live prefab smoke verified source binding, instance id maps, and name/position/rotation overrides. |
| Prefab save/apply overrides beyond creation templates | `prefab.create`, `prefab.inspect_instance` | `prefab` | Verified gap | Live prefab smoke verifies reusable prefab creation, GUID-remapped instantiation, and instance override metadata read-back. Applying/saving overrides back into an existing prefab asset remains outside the current bridge surface. |
| Asset file-system operations | `project.list_files`, `project.read_file`, `project.write_file`, `project.delete_file` | `project` / `list_files` / `read_file` / `write_file` / `delete_file` | Verified | Safe project-scoped roots cover assets, code, editor, settings, and project root paths with traversal guards. Live `smoke:project` verified asset-root scratch file write/read/list/delete and settings-root `Input.config` read-back. |
| Cloud asset/package discovery | `asset.cloud_packages`, `asset.search` | `asset` / `cloud_packages`, `asset` / `search` | Verified | Live `smoke:asset-resources` verifies installed/referenced package-cache reads and `asset.search` exposes per-asset `isCloud` metadata where present. Cloud import/download remains intentionally unexposed. |
| Registered asset type discovery | `asset.list_types` | `asset` / `list_types` | Verified | Live `smoke:asset-resources` verifies `AssetType.All` enumeration, GameResource filtering, file extensions, categories, flags, editor availability, and resource types. |
| Custom GameResource authoring | `asset.create_resource`, `asset.list_types`, `asset.get_info` | `asset` / `create_resource` | Verified | Live `smoke:asset-resources` creates a generic `.sound` `Sandbox.SoundEvent` through `AssetSystem.CreateResource`, compiles it, and reads it back through `asset.get_info`. |
| Clothing, citizen, and first-person weapon asset workflow declarations | `script.analyze`, `reference.search`, `asset.list_types` | `script`, `reference`, `asset` | Verified gap | Live `smoke:scripts` verifies source markers for clothing/citizen/first-person workflow code, and `asset.list_types`/reference lookup can discover registered asset/API surfaces. Specialized clothing/citizen/weapon authoring tools are not implemented. |
| Storage/UGC asset workflow declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies source markers for storage/UGC workflow code. The bridge intentionally has no upload/download or UGC mutation helper until a safety model is designed. |

## Rendering, Lighting, Post-Processing, And VFX

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Ambient fill light | `gameobject.create`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | Live IPC created an `AmbientLight` readability fill and set its `Color` during the ARPG lighting pass. |
| Directional light tuning | `component.set_property` | `component` / `set_property` | Verified | Live IPC set `DirectionalLight.LightColor`, `SkyColor`, `FogMode`, `FogStrength`, `Shadows`, `ShadowBias`, and `ShadowHardness` on the scene Sun. |
| Point light authoring | `gameobject.create`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | Live IPC created a warm brazier point light and set radius, attenuation, fog, color, and shadow properties. |
| Spot light authoring | `gameobject.create`, `gameobject.set_transform`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | Live IPC created an obelisk spot light and set cone, radius, attenuation, fog, color, and shadow properties. |
| Post-process components | `component.add`, `component.set_property` | `component` | Verified | Live IPC configured `FilmGrain`, `Tonemapping`, `Bloom`, `PostProcessVolume`, `Vignette`, and `ColorAdjustments` for the ARPG visual pass. |
| DecalRenderer material assignment | `component.set_property` with `includeAll: true` | `component` | Verified | `DecalRenderer.Material` is public/non-inspector; setting it works when callers opt into all readable properties. Verified with blood/gold/void/bone decal materials. |
| Basic particle stack setup | `gameobject.create`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | `mcp-server/test/capability-gap-smoke.ts` creates `ParticleEffect`, `ParticleConeEmitter`, `ParticleSpriteRenderer`, and `ParticleLightRenderer`, then verifies bool, number, integer, color, vector, and enum property read-back. |
| Complex particle wrapper properties | `component.get_properties`, `component.validate_property`, `script.analyze` | `component`, `script` | Verified gap | Basic particle stack setup is live-verified. Wrapper/curve/gradient types such as `ParticleFloat`, `ParticleVector3`, and `ParticleGradient` remain known unsupported conversion targets rather than untested behavior. |
| Camera capture feedback | `visual.capture_camera` | `visual` / `capture_camera` | Verified | Live IPC rendered the active main `CameraComponent` to a PNG under `%TEMP%/sbox-agent-bridge/captures` and returned camera metadata plus luminance stats. First ARPG smoke capture at 640x360 reported average luminance `0.1332` and dark pixel ratio `0.3145`. |
| Viewport/HUD capture | `visual.capture_camera`, `runtime.run_test_action` | `visual`, `runtime` | Verified gap | Camera pixel capture and logical runtime UI self-report are live-verified. `smoke:scripts` verifies ScreenPanel/HudPainter source markers, but editor viewport overlays and arbitrary HUD pixels are not captured. |
| Effects, beams, tracers, and effect lifetime declarations | `component.*`, `script.analyze` | `component`, `script` | Verified gap | Basic particle components and common properties are live-verified, and source marker analysis can identify effect-related code. Dedicated beam/tracer/lifetime authoring helpers are not implemented. |
| Shader and ShaderGraph declarations | `asset.create_material`, `asset.inspect_material`, `asset.list_types`, `script.analyze` | `asset`, `script` | Verified gap | Material source creation/inspection is live-verified and `smoke:scripts` verifies ShaderGraph source marker detection. Custom shader/ShaderGraph graph editing remains outside the bridge. |
| Render hooks/custom rendering declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies markers for `SceneCamera`, `RenderTarget`, `CommandList`, and related rendering code. Runtime render-hook execution and render-target capture helpers are not implemented. |
| ScreenPanel and UI-render-target declarations | `script.analyze`, `runtime.run_test_action` | `script`, `runtime` | Verified gap | Live `smoke:scripts` verifies ScreenPanel/Panel source markers and runtime test actions verify logical UI state. Generic screen-panel tree inspection and UI render-target helpers remain absent. |
| VR rendering declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies VR source marker detection. The bridge has no VR session/rendering automation. |

## Project, Code, And Editor Tooling Coverage

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Game/addon/editor project metadata | `editor.project_info`, `bridge.status`, `editor.context` | `editor` / `project_info` / `status` / `context` | Verified | Reads active project title/type/ident, root/assets/code/editor paths, bridge install path, and compiler availability from the currently running editor process. |
| Create minimal project from template | `scripts/create-minimal-sbox-project.ps1` | CLI script | Verified | Instantiates the local s&box `game.minimal` template, writes project title/org/ident, removes template metadata, and was run locally against `agent_bridge_mvp_fresh` before installing the bridge. |
| Launch project with isolated IPC | `scripts/start-sbox-project.ps1` | CLI script | Verified | Launches a selected `.sbproj` through `sbox-dev.exe sbox-launcher.dll sbox-dev.dll -project ...`, optionally sets `SBOX_AGENT_BRIDGE_IPC`, and can wait for bridge IPC folders. Live-verified by launching `local.agent_bridge_mvp_fresh` beside the existing test editor and running `smoke:mvp-suite` through the isolated IPC root. |
| Switch already-open editor project | `editor.project_info`, CLI launch script | `editor` / `project_info` | Verified gap | Live bootstrap/fresh-project checks verify current-project identity and separate-process launch with isolated IPC. No verified in-process s&box API is exposed for switching an already-open editor process to another project. |
| Editor widgets, dialogs, menus, tools, and asset picker declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies source markers for `Widget`, `Dialog`, `Menu`, `AssetPicker`, and editor tool code. Bridge automation is intentionally limited to the Agent Bridge dock and narrow scene/project actions. |
| Asset previews, model editor, mapping, and texture generators | `asset.preview_model`, `asset.list_types`, `reference.search` | `asset`, `reference` | Verified gap | Runtime-targeted model preview capture and asset type discovery are live-verified. The native model editor, mapping tools, texture generators, and editor preview widgets are not automated. |
| Custom editors and property attributes | `script.analyze`, `component.get_properties`, `component.validate_property` | `script`, `component` | Verified gap | Source-level property/custom editor markers plus live component property metadata/validation are verified. Authoring or asserting native custom inspector behavior remains outside current bridge tooling. |
| Code diagnostics beyond compile events | `editor.compile_status`, composed MCP wait helper, `script.list`, `script.read`, `script.search`, `script.analyze` | `editor` / `compile_status`, `editor` / `wait_compile`, `script` | Verified | Compile groups, recent diagnostics, hotload settle waits, code file listing/reading, text symbol search, and source-level lifecycle/attribute analysis are verified. `smoke:capability-gaps` verifies compiler error/recovery; `smoke:scripts` verifies script list/read/search/analyze around a compiled scratch component. Generated API lookup and full Roslyn analyzer integration remain outside the current bridge surface. |
| Code generation and hotload control | `editor.compile_status`, `editor.feedback`, composed wait helpers | `editor` | Verified gap | Compile/hotload feedback, compile-error diagnostics, and recovery waits are live-smoked. Generated code artifact inspection and named hotload gating are confirmed outside the current bridge surface. |
| s&box C# unit test runner integration | `reference.search`, `editor.compile_status` | `reference`, `editor` | Verified gap | Reference lookup can discover unit-test APIs and compile status catches test-code build errors, but the bridge has no verified runner invocation or result parser for s&box C# unit tests. |
| Console variables, API whitelist, math types, and library reference lookup | `reference.search`, `reference.type`, `reference.console`, `reference.whitelist` | `reference` | Verified | Live `smoke:reference` searched installed XML docs for `GameObject.NetworkMode`, inspected the loaded `Sandbox.GameObject` type, resolved s&box `Vector3` members while avoiding the `System.Numerics.Vector3` name collision, read the `snd_mute` console variable, found whitelist docs, and inspected whitelist metadata. |
| Standalone/export workflows | `reference.search`, CLI docs | `reference` | Verified gap | Reference/docs lookup can discover export APIs, but packaging/export automation is intentionally absent from the editor bridge MVP. |

## Networking And Multiplayer

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Networking systems/state inspection | `network.connections`, `network.inspect_object` | `network` | Verified | Live `smoke:network` reads local/host connection state and GameObject network mode/accessor metadata, including network root, owner/proxy booleans, owner ids, flags, owner transfer, orphan behavior, always-transmit, and interpolation. Low-level transport diagnostics remain outside the current bridge surface. |
| Connection and user permissions | `network.connections` | `network` / `connections` | Verified | Live `smoke:network` verified the local/host `Connection` read-back and permission booleans: `canSpawnObjects`, `canRefreshObjects`, and `canDestroyObjects`, plus host identity, active/connecting state, and ping. |
| Networked object metadata and ownership policy | `network.inspect_object`, `network.set_object_mode` | `network` / `inspect_object` / `set_object_mode` | Verified | Live `smoke:network` creates a temporary GameObject, reads default network metadata, sets `NetworkMode:Object`, `OwnerTransfer:Fixed`, `NetworkOrphaned:Host`, and `AlwaysTransmit:true`, then verifies read-back through both mutation and inspection responses. |
| Network visibility and custom snapshot declarations | `network.inspect_object`, `script.analyze` | `network`, `script` | Verified gap | Network flags/always-transmit are inspected live, and `smoke:scripts` verifies source detection for `Component.INetworkSnapshot`, `Component.INetworkVisible`, `INetworkSpawn`, and `INetworkListener`. Multi-client visibility filtering and payload replication are not yet smoked. |
| Sync properties | `script.analyze` | `script` / `analyze` | Verified | Live `smoke:network` and `smoke:scripts` run source-only probes that detect `[Sync]` declarations. Runtime dirty-state and cross-client replication are not currently exercised. |
| RPC messages and network events | `script.analyze` | `script` / `analyze` | Verified | Live smokes verify source-level detection of `[Rpc.*]` methods and `IGameObjectNetworkEvents`. Invocation tracing and target validation remain future multi-client work. |
| Serverside code, dedicated servers, and local multiplayer testing | `network.connections`, `script.analyze`, `reference.search` | `network`, `script`, `reference` | Verified gap | Single-editor local/host connection state is live-verified and source/API detection covers multiplayer declarations. Dedicated server launch/attach and multi-client local orchestration are confirmed missing. |
| HTTP requests and WebSockets declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies HTTP/WebSocket source marker detection. Controlled request execution and WebSocket diagnostics are intentionally not exposed. |

## UI

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Runtime UI state inspection | `runtime.run_test_action` | `runtime` / `run_test_action` | Verified | The ARPG controller exposes a property-protocol test action that reports logical HUD/orb/inventory/player/combat state. Live smoke verified inventory open, damage, restore, skills, and zombie count. The boxing walkthrough verified gameplay/HUD self-report for a second genre. Generic ScreenPanel tree inspection is still missing. |
| Screen UI/panel hierarchy inspection | `script.analyze`, `runtime.run_test_action` | `script`, `runtime` | Verified gap | Source markers for Panel/ScreenPanel are live-verified and component-authored UI self-report is verified. Arbitrary panel hierarchy, resolved style, and pixel inspection are not implemented. |
| Razor/component authoring helpers | `script.create`, `script.edit`, `script.analyze` | `script` | Verified gap | Agents can create/edit/read/search/analyze source, and `smoke:scripts` verifies Razor/component marker detection. UI-specific templates, Razor schema checks, and live panel verification remain absent. |
| HudPainter declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies `HudPainter` source marker detection. Immediate-mode HUD drawing output is not inspected. |
| Localization declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies localization source marker detection. Localization file/key inventory and active locale helpers are not implemented. |
| Styling, style properties, events, and class state | `script.analyze`, `project.read_file` | `script`, `project` | Verified gap | Source/file tooling can inspect authored UI/CSS files, but resolved style, event binding, focus/hover state, and class state are confirmed outside current bridge support. |
| VirtualGrid declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies `VirtualGrid` source marker detection. Virtualized visible ranges, selection, and focus state are not inspected. |

## ActionGraph, Movie Maker, And Game Mounts

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| ActionGraph inspection/authoring declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies ActionGraph source marker detection. Graph inspection, node authoring, and validation are not implemented. |
| Movie Maker workflow declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies MovieMaker source marker detection. Timelines, keyframes, preview capture, and export remain unsupported. |
| Game mounts declarations | `script.analyze`, `reference.search`, `asset.search` | `script`, `reference`, `asset` | Verified gap | Live `smoke:scripts` verifies GameMount marker detection and normal asset search covers mounted content once visible to `AssetSystem`. Mount configuration automation is not implemented. |

## Media

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Video playback/media asset declarations | `script.analyze`, `reference.search`, `project.list_files` | `script`, `reference`, `project` | Verified gap | Live `smoke:scripts` verifies video/media source marker detection. Video playback state, media asset inspection, and capture validation are not implemented. |
| Audio media workflows | `sound.list`, `sound.get_info`, `sound.create_event`, `sound.assign`, `sound.inspect`, `sound.preview`, `sound.preview_status`, `sound.stop_preview` | `sound` | Verified | Common editor-side audio workflows are covered: sound asset listing, sound event authoring/info, scene `SoundPointComponent` assignment/inspection, and preview handle lifecycle. Live sound smoke verifies all of these against a project-created event. |
| Sound API coverage | `sound.*` | `sound` | Verified | Bridge-supported sound coverage includes `SoundEvent`, `SoundFile` metadata where available, `SoundPointComponent`, and tracked `SoundHandle` preview control. Low-level mixer/voice internals remain outside the bridge's intended editor automation surface. |

## Gameplay Systems

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Input actions/settings | `project.input_actions`, `project.upsert_input_action`, `project.remove_input_action` | `project` / `input_actions` / `upsert_input_action` / `remove_input_action` | Verified | Structured helpers inspect and mutate `ProjectSettings/Input.config` actions. Live `smoke:project` verified built-in action lookup plus create/update/remove of a temporary keyboard/gamepad binding. |
| Controller input, raw input, and glyph declarations | `project.input_actions`, `script.analyze`, `reference.search` | `project`, `script`, `reference` | Verified gap | Structured input action config is live-verified, and `smoke:scripts` verifies gamepad/raw-input/glyph source markers. Live device state and glyph rendering are not inspected. |
| Navigation/navmesh declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies NavMesh/NavMeshAgent source marker detection. Navmesh build/read, agent diagnostics, areas, obstacles, and links are not implemented. |
| Terrain declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies terrain source marker detection. Terrain sculpt/paint/material/read helpers are not implemented. |
| Clutter system declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies clutter source marker detection. Clutter authoring, inspection, and density validation are not implemented. |
| Runtime gameplay state/self-report | `runtime.list_test_actions`, `runtime.run_test_action`, `scene.* targetSession=runtime` | `runtime`, `scene` | Verified | Live-smoked against the ARPG GameSession and the boxing walkthrough. Runtime scene reads target the live `GameSession`; component-authored test actions provide gameplay/UI state without relying on screenshots. |
| Runtime input/test actions | `runtime.run_test_action` | `runtime` / `run_test_action` | Verified | Deterministic component-authored test actions are verified through a property protocol. The bridge now unwraps property-protocol setter exceptions with component/action context. Components should ignore empty `AgentBridgeTestAction` assignments because scene deserialization can replay serialized empty values. Focused viewport input injection is still future work. |
| VR gameplay declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies VR source marker detection. VR sessions, controller state, and gameplay helpers are not implemented. |

## Services

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Achievement declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies achievement source marker detection. Achievement definition/read/unlock/reset helpers are not implemented. |
| Auth token/identity declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies auth source marker detection. Token/session identity helpers are intentionally absent. |
| Leaderboard and stats declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies leaderboard/stat source marker detection. Read/write/reset helpers are not implemented. |
| Web API call declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies Web API source marker detection. Runtime Web API wrappers are not exposed through the bridge. |

## Animation

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Animation graph declarations | `script.analyze`, `reference.search`, `component.get_properties` | `script`, `reference`, `component` | Verified gap | Live `smoke:scripts` verifies AnimationGraph source marker detection and capability-gap smoke verifies citizen animation helper graph flags. Graph parameter/transition/clip inspection is not implemented. |
| Animation state machines, layers, and IK declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies animation state machine and IK markers. State-machine/layer/IK/blend-tree authoring and diagnostics are not implemented. |
| SkinnedModelRenderer/citizen animation helpers | `component.add`, `component.set_property` | `component` | Verified | `mcp-server/test/capability-gap-smoke.ts` adds `SkinnedModelRenderer` and `CitizenAnimationHelper`, assigns `models/citizen/citizen.vmdl`, links `CitizenAnimationHelper.Target`, and verifies tint, playback, animation graph flag, look-at, height, and enum read-back. |
| Animation events and automated animation declarations | `script.analyze`, `reference.search` | `script`, `reference` | Verified gap | Live `smoke:scripts` verifies AnimationEvent marker detection. Animation event inspection and automated animation setup are not implemented. |

## Testing And CI

| Capability | Status | Notes |
|---|---|---|
| MCP TypeScript build | Verified | `npm run check` and `npm run build` pass locally. Direct Node commands remain documented as a Windows fallback if npm shim issues recur. |
| MCP bridge-client and wait-helper tests | Verified | `npm test` covers bridge-client success/error/timeout behavior plus compile/runtime/stopped wait-helper polling logic. |
| CI MCP build/test | Verified | GitHub Actions workflow runs typecheck, tests, and build; local `npm run ci` is used as the workflow-equivalent gate before publishing bridge changes. |
| JSON/sbproj validation | Verified | GitHub Actions workflow added; workflow-equivalent local Python validation passed for `.json` and `.sbproj` files under `schemas`, `editor`, and `mcp-server`. |
| Runtime feedback smoke script | Verified | `mcp-server/test/runtime-feedback-smoke.ts` verifies `wait_compile`, `wait_stopped`, `wait_runtime`, runtime test-action listing/invocation, ARPG logical UI state, inventory open, damage, and restore. |
| MVP smoke script | Verified | `mcp-server/test/mvp-smoke.ts` verifies doctor, compile wait, scene recovery, scene read, object creation, model/material assignment, physics inspection, sound inspection, prefab inspection, runtime preview capture, play/stop settle, and cleanup without ARPG-specific test actions. |
| MVP suite script | Verified | `mcp-server/test/mvp-suite.ts` creates `scenes/agent_bridge/smoke/mvp_suite.scene` through the bootstrap smoke, then runs MVP, asset/material, asset-resource/cloud, physics, sound, prefab, matrix-core, project-file/input, script-introspection, reference, network, and capability-gap smokes against that scene. It passed locally without relying on `scenes/minimal.scene`, including against a generated fresh Minimal Game project launched with isolated IPC. |
| Matrix core smoke script | Verified | `mcp-server/test/matrix-core-smoke.ts` verifies scene metadata, spatial radius search, runtime assembly component discovery, and undoable GameObject destruction against the saved MVP suite scene. |
| Project file/input smoke script | Verified | `mcp-server/test/project-file-input-smoke.ts` verifies safe project-scoped file list/read/write/delete plus structured `ProjectSettings/Input.config` action inspection, creation, update, and removal. |
| Script introspection smoke script | Verified | `mcp-server/test/script-introspection-smoke.ts` verifies script create/delete compile waits plus `script.list`, `script.read`, `script.search`, lifecycle/attribute/interface detection, and domain source markers for physics, networking, rendering, UI, assets, world systems, animation, services, media, editor tools, and input. |
| Reference smoke script | Verified | `mcp-server/test/reference-smoke.ts` verifies installed XML doc search, loaded C# type reflection, console variable reads, and API whitelist metadata lookup. |
| Network smoke script | Verified | `mcp-server/test/network-smoke.ts` verifies `network.connections`, GameObject network metadata inspection/mutation, and source-level `[Sync]`/`[Rpc.*]`/`IGameObjectNetworkEvents` analysis against the saved MVP suite scene. |
| Asset resource/cloud smoke script | Verified | `mcp-server/test/asset-resource-cloud-smoke.ts` verifies registered GameResource asset type discovery, generic `AssetSystem.CreateResource` authoring/read-back, and installed/referenced cloud package-cache metadata reads. |
| Boxing clean-room walkthrough | Verified | `mcp-server/test/boxing-poc-walkthrough.ts` installs a boxing controller through `script.create`, adds/configures the local component by exact type name, verifies jab/block/dodge/knockdown/TKO/decision runtime actions, captures the generated broadcast camera by GameObject id, and reports project/scene bootstrap, script-editing, input, and camera-targeting gaps. |
| Capability gap smoke script | Verified | `mcp-server/test/capability-gap-smoke.ts` verifies scratch script create/edit/delete with new compile sequence waits, controlled compile-error diagnostics and recovery, animation helper component setup, and basic particle stack component/property mutation. |
| Asset/material smoke script | Verified | `mcp-server/test/asset-material-smoke.ts` verifies material creation, material source inspection/mutation, runtime-targeted model preview capture, and wait-helper cleanup. |
| Prefab instance smoke script | Verified | `mcp-server/test/prefab-instance-smoke.ts` verifies prefab creation, source binding, prefab info reload, GUID-remapped instantiation, instance id maps, and transform override patch samples. |
| Physics smoke script | Verified | `mcp-server/test/physics-smoke.ts` verifies physics body creation/read-back, box collider creation/read-back, joint creation/read-back including linked body/target metadata, and a raycast hit against the temporary collider. |
| Sound smoke script | Verified | `mcp-server/test/sound-smoke.ts` verifies sound event creation/info, SoundPointComponent assignment/read-back, and tracked preview handle start/status/stop. |
| Live editor smoke script | Verified | Broad regression smoke for older scene-editing workflows. Passed live against the fresh MVP project with `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1`, including fixture type listing, exact-type component add, property mutation, undo/redo, batch, visual capture, play/stop, logs, and compile feedback. Prefer `smoke:mvp-suite` for external testers. |
| Automated s&box editor tests in CI | Verified gap | Local live editor smoke coverage is verified, but a reliable unattended s&box editor runner for CI is not available. CI remains limited to MCP/server build, unit tests, and static validation. |
