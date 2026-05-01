# Capability Matrix

Status meanings:

- **Verified**: tested against a live s&box editor.
- **Partial**: useful behavior is verified, but a known limitation remains.
- **Implemented**: code exists, but live editor verification is pending.
- **Unverified**: the official docs cover this area, but no dedicated bridge smoke has verified it.
- **Planned**: intended, not implemented.
- **Blocked**: known blocker or missing verified API.

## Current Verification Environment

- Date: 2026-05-01
- s&box project: Minimal Game-derived local project with the ARPG bridge testbed
- Bridge install path: `Libraries/sbox_agent_bridge`
- Transport: file IPC at `%TEMP%/sbox-agent-bridge`
- Official docs sweep source: `https://sbox.game/dev/doc/` navigation index reviewed on 2026-05-01. Missing top-level doc areas are listed below even when the bridge has no tool support yet.

## Bridge And Editor

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Bridge status | `bridge.status` | `editor` / `status` | Verified | Returns running state, IPC root, active scene, play state. |
| Editor context | `editor.context` | `editor` / `context` | Verified | Direct file-IPC read verified with selected GameObject details. Includes active tab/session metadata so agents can see which scene tab commands will target. |
| List editor tabs | `editor.tabs` | `editor` / `tabs` | Verified | Lists open `SceneEditorSession` tabs with index, scene id, active flag, source path, dirty state, and play state. Live IPC verified with both `Untitled Scene` and `scenes/minimal.scene` open. |
| Activate editor tab | `editor.activate_tab` | `editor` / `activate_tab` | Verified | Activates an existing editor scene tab by index, scene id, source path, or scene name. Live IPC verified by raising `scenes/minimal.scene` while an unsaved untitled tab was also open. |
| Open scene | `editor.open_scene` | `editor` / `open_scene` | Verified | Opens a scene resource by path and makes its session active. Falls back through `AssetSystem.FindByPath` when direct resource lookup fails. Supports `forceReload` for reloading an already-open sourced scene when play/stop leaves the editor session stale. |
| Save scene | `editor.save_scene` | `editor` / `save_scene` | Verified | Reports before/after dirty state and scene source path; guards untitled scenes instead of opening a save-as flow. Live IPC verified dry-run, no-source skip behavior, and disk write against `scenes/minimal.scene`. |
| Undo | `editor.undo` | `editor` / `undo` | Verified | Verified during earlier scene mutation smoke. Avoid coupling current undo checks to `gameobject.destroy` until the native delete/undo issue is reverified. |
| Redo | `editor.redo` | `editor` / `redo` | Verified | Verified during earlier scene mutation smoke. Avoid coupling current redo checks to `gameobject.destroy` until the native delete/undo issue is reverified. |
| Selection read | `editor.get_selection` | `editor` / `get_selection` | Verified | Returns typed selection entries; GameObject selection verified. |
| Selection set | `editor.set_selection` | `editor` / `set_selection` | Verified | Accepts GameObject ids only; verified with read-back count. |
| Frame/focus object | `editor.frame_object` | `editor` / `frame_object` | Verified | Calls `SceneEditorSession.FrameTo` for a target GameObject bounds. |
| Play state | `editor.play_state` | `editor` / `play_state` | Verified | Reads active scene by default and supports `targetSession: runtime` to resolve the live `GameSession`; response includes target-session metadata. Live IPC verified runtime resolution after play. |
| Play mode start | `editor.play` | `editor` / `play` | Verified | Calls `SceneEditorSession.SetPlaying(...)` on the resolved editor session and returns read-back play state. Defaults to `targetSession: editor` so an active runtime tab controls its parent editor scene. |
| Play mode stop | `editor.stop` | `editor` / `stop` | Verified | Calls `SceneEditorSession.StopPlaying()` on the resolved editor session and returns immediate read-back with `transitionPending` when the editor settles on a later frame. Supports `stopAll: true` to clear every currently playing editor session before smoke tests. |
| Recent logs | `editor.logs` | `editor` / `logs` | Verified | Tails `sbox-dev.log`; raw lines are authoritative, log level is explicitly inferred, and `afterIndex` cursor reads are live verified so agents can ignore stale log lines. |
| Compile status | `editor.compile_status` | `editor` / `compile_status` | Verified | Tracks compile groups from observed `compile.started` events; live IPC returned compiler diagnostics and zero errors after the fix. |
| Combined feedback | `editor.feedback` | `editor` / `feedback` | Verified | Returns play state, compile status, and recent logs for agent edit/test loops; supports `targetSession` and `afterIndex` for runtime-aware fresh feedback. |

## Scene Read

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Scene summary | `scene.summary` | `scene` / `summary` | Verified | Returns object/component counts from the resolved session; `targetSession: runtime` was live verified against the ARPG GameSession with 219 objects. |
| Scene hierarchy | `scene.hierarchy` | `scene` / `hierarchy` | Verified | Live IPC read-back verified the ARPG fixture yard hierarchy after scene mutations; now supports target-session selection. |
| Scene metadata | TBD | TBD | Planned | Official docs include scene metadata; bridge reads active scene identity/path/state, but does not inspect or edit scene metadata resources. |
| Find GameObjects | `scene.find` | `scene` / `find` | Verified | Verified by finding created objects and runtime-only ARPG components with `targetSession: runtime`. |
| Object details | `scene.details` | `scene` / `details` | Verified | Includes id, parent, enabled/active state, transforms, components, child count; supports target-session selection. |
| Batch operations | `scene.batch` | `scene` / `batch` | Verified | Runs bounded action lists with per-operation result capture and `$ref` alias substitution. Live IPC verified object create/reparent/duplicate/rename/enable/selection/focus plus asset assignment, sound assignment, physics/collider/joint, material-property, and raycast actions. |
| Find in radius | `scene.find_in_radius` | TBD | Planned | Useful for spatial workflows. |

## GameObjects

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Read GameObject | `gameobject.get` | `gameobject` / `get` | Verified | Id-targeted read returning the same detail shape as `scene.details`; supports target-session selection for runtime reads. |
| Create GameObject | `gameobject.create` | `gameobject` / `create` | Verified | Uses `SceneEditorSession.Active.Scene.CreateObject(true)` and verifies via read-back; optional `parentId` was verified through `scene.batch`. |
| Destroy GameObject | `gameobject.destroy` | `gameobject` / `destroy` | Blocked | Previously verified, but the current editor session now reports a null reference in the native editor delete/undo path after play-mode testing. Needs fresh-session verification and/or a safer delete strategy before relying on it. |
| Rename GameObject | `gameobject.rename` | `gameobject` / `rename` | Verified | Id-targeted, undo scoped, unique-name default verified by read-back. |
| Set transform | `gameobject.set_transform` | `gameobject` / `set_transform` | Verified | World position, Euler/quaternion rotation input, and world scale; verified by read-back. |
| Enable/disable | `gameobject.set_enabled` | `gameobject` / `set_enabled` | Verified | Verified false and true read-back on a live object. |
| Reparent | `gameobject.reparent` | `gameobject` / `reparent` | Verified | Preserves world transform by default; supports moving back to scene root. |
| Duplicate | `gameobject.duplicate` | `gameobject` / `duplicate` | Verified | Shallow scene-attached duplicate of name/enabled/transform/parent. Component and child cloning are future work. |
| Place model asset | `gameobject.place_asset` | `gameobject` / `place_asset` | Verified | Live IPC created `Agent Bridge Spatial V1 Obelisk 20260501-100811` from `models/agent_bridge/arpg_props/cursed_obelisk.vmdl`, used a stored override with `pitch: 90`, aligned render bounds to ground, saved `scenes/minimal.scene`, force-reloaded it, and read back the persisted `ModelRenderer`. |

## Components

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| List component types | `component.list_types` | `component` / `list_types` | Partial | Uses `Game.TypeLibrary.GetTypes(typeof(Component))`; live smoke returned built-in/editor-visible types. Local game components such as `AgentBridgeArpgFixture` are not enumerated even after instances exist. |
| List GameObject components | `component.list_on_gameobject` | `component` / `list_on_gameobject` | Verified | Id-targeted GameObject component list; supports target-session selection for runtime components. |
| Read component | `component.get` | `component` / `get` | Verified | Id-targeted component read with owning GameObject context; supports target-session selection. |
| Get properties | `component.get_properties` | `component` / `get_properties` | Verified | Read-only metadata/value inspection; defaults to `[Property]` inspector properties and includes schema hints, attributes, enum values, and reference targets for settable JSON shapes; supports runtime target sessions. |
| Add component | `component.add` | `component` / `add` | Verified | Built-in/editor-visible components use TypeLibrary. Local compiled game components can be added by exact C# type name through a serialized-probe fallback that resolves the runtime type, then calls `GameObject.AddComponent<T>()`; live verified with `AgentBridgeArpgFixture` on ARPG fixture objects without duplicating existing components. |
| Remove component | `component.remove` | `component` / `remove` | Verified | Undo scoped; live smoke verifies `component.get` fails after removal, undo restores, redo removes again. |
| Set property | `component.set_property` | `component` / `set_property` | Verified | Live smoke verifies string, bool, numeric primitives, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject`, and `Component` values through `AgentBridgeMutationFixture`; live IPC also verified resource paths on built-in `ModelRenderer.Model`, `ModelRenderer.MaterialOverride`, and non-inspector `DecalRenderer.Material` via `includeAll: true`; local `AgentBridgeArpgFixture` enum/string/int properties are verified; supports `dryRun: true`. |
| Validate property | `component.validate_property` | `component` / `validate_property` | Verified | Converts and resolves a candidate value without mutation; live smoke verifies valid conversion, invalid rejection, and unchanged fixture values. |
| Enable/disable component | `component.set_enabled` | `component` / `set_enabled` | Verified | Live smoke verifies false and true read-back. |
| Component lifecycle, interfaces, and event hooks | TBD | TBD | Planned | Official docs cover component methods/interfaces, async, events, `ISceneStartup`, `IGameObjectNetworkEvents`, execution order, and temporary effects; bridge does not inspect or validate source/runtime lifecycle hooks. |

## Scripts

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Create script | `script.create` | `script` / `create` | Verified | Live IPC created `Code/ArpgDemo/AgentBridgeArpgFixture.cs`; compile status stayed green. |
| Edit script | `script.edit` | `script` / `edit` | Verified | Live IPC edited the same script and verified SHA/length change plus green compile. |
| Delete script | `script.delete` | `script` / `delete` | Blocked | Implementation exists, but live deletion was intentionally not exercised. Add a dedicated scratch-file smoke before treating it as verified. |

## Assets, Materials, Sounds, Physics, And Prefabs

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Search assets | `asset.search` | `asset` / `search` | Verified | Live IPC found built-in models/materials and newly created ARPG materials. |
| Inspect asset | `asset.get_info` | `asset` / `get_info` | Verified | Live IPC inspected built-in material metadata and project-created assets. |
| Inspect model orientation/bounds | `asset.inspect_model` | `asset` / `inspect_model` | Verified | Live IPC inspected `models/agent_bridge/arpg_props/cursed_obelisk.vmdl`, returning asset/model metadata, model/render/physics bounds, material slots, common orientation candidates, ground offsets, footprints, and explicit semantic-orientation limitations. |
| Get model orientation override | `asset.get_orientation_override` | `asset` / `get_orientation_override` | Verified | Live IPC read back the cursed obelisk override from `Assets/agent_bridge/orientation_overrides.json` with `found:true` and `baseRotation.pitch: 90`. |
| Set model orientation override | `asset.set_orientation_override` | `asset` / `set_orientation_override` | Verified | Live IPC wrote the cursed obelisk override to `Assets/agent_bridge/orientation_overrides.json`; omitted ground offset was calculated from render bounds and reused by `gameobject.place_asset`. |
| Assign model | `asset.assign_model` | `asset` / `assign_model` | Verified | Live IPC assigned `models/dev/box.vmdl` to ARPG fixture objects with read-back. |
| Create material | `asset.create_material` | `asset` / `create_material` | Verified | Writes a simple `.vmat` source, registers it, and compiles it. Verified four project materials under `materials/agent_bridge/`. |
| Assign material | `asset.assign_material` | `asset` / `assign_material` | Verified | Live IPC assigned project-created materials to `ModelRenderer.MaterialOverride`. |
| Set material property | `asset.set_material_property` | `asset` / `set_material_property` | Verified | Live IPC set `g_vColorTint` on an assigned material and returned `success: true`. |
| List sounds | `sound.list` | `sound` / `list` | Verified | Live IPC listed sound events and sound files, including built-in melee impacts and ambience. |
| Inspect sound | `sound.get_info` | `sound` / `get_info` | Verified | Live IPC inspected a project-created sound event. Sound-file duration/channel fields are best-effort because some built-in sound handles throw when metadata is read before load. |
| Create sound event | `sound.create_event` | `sound` / `create_event` | Verified | Created `sounds/agent_bridge/arpg_cave_fixture.sound` from `sounds/ambience/cave-loop.vsnd` and compiled it. |
| Assign sound | `sound.assign` | `sound` / `assign` | Verified | Added/updated `SoundPointComponent` on shrine and dummy fixture objects. |
| Preview sound | `sound.preview` | `sound` / `preview` | Verified | Played `sounds/impacts/melee/impact-melee-flesh.sound` and returned a valid playing `SoundHandle`. |
| Add collider | `physics.add_collider` | `physics` / `add_collider` | Verified | Added box colliders to ARPG fixture props and raycasted against them. |
| Add physics body | `physics.add_physics` | `physics` / `add_physics` | Verified | Added static/non-motion rigidbodies to fixture props. |
| Add joint | `physics.add_joint` | `physics` / `add_joint` | Partial | Creates joint components. Target assignment is blocked in v0 because `Joint.Object2` is read-only through the verified API. |
| Raycast | `physics.raycast` | `physics` / `raycast` | Verified | Live IPC raycast hit the Blood Shrine fixture through its collider. |
| Scene physics events | TBD | TBD | Planned | Official docs include `IScenePhysicsEvents`; bridge has no helper to inspect, register, or verify physics event callbacks. |
| Create prefab | `prefab.create` | `prefab` / `create` | Verified | Serialized a live GameObject to `prefabs/agent_bridge/arpg_loot_chest_fixture.prefab`; list/get-info verified the resource. |
| List prefabs | `prefab.list` | `prefab` / `list` | Verified | Live IPC listed built-in prefabs and the project-created ARPG prefab. |
| Inspect prefab | `prefab.get_info` | `prefab` / `get_info` | Verified | Uses `AssetSystem` fallback when `PrefabFile.Load(path)` cannot load a project-created prefab path. |
| Instantiate prefab | `prefab.instantiate` | `prefab` / `instantiate` | Verified | Instantiates by deserializing prefab `RootObject` into the active editor scene with fresh GUIDs; live IPC created `Loot Chest Prefab Instance - bridge fixture`. |
| Prefab instance overrides and templates | TBD | TBD | Planned | Bridge can create/list/inspect/instantiate prefabs, but has no helper for instance override metadata or prefab templates. |
| Asset file-system operations | TBD | TBD | Planned | Official docs cover asset file-system workflows; bridge only searches, inspects, creates selected resource types, and assigns assets today. Need safe project-scoped browse/read/write helpers before exposing general file operations. |
| Cloud asset/package discovery | TBD | TBD | Planned | Asset search returns `isCloud` metadata when available, but there is no cloud search/import/package workflow. |
| Custom asset/resource authoring | TBD | TBD | Planned | Bridge can create simple `.vmat`, `.sound`, and `.prefab` resources, but does not expose general custom asset or `GameResource` extension workflows. |
| Clothing, citizen, and first-person weapon asset workflows | TBD | TBD | Planned | Official docs include these asset domains; bridge has no specialized inspection, authoring, or validation helpers for them yet. |
| Storage/UGC asset workflows | TBD | TBD | Planned | Official docs include storage/UGC; bridge has no storage APIs, upload/download helpers, or safety model for user-generated content yet. |

## Rendering, Lighting, Post-Processing, And VFX

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Ambient fill light | `gameobject.create`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | Live IPC created an `AmbientLight` readability fill and set its `Color` during the ARPG lighting pass. |
| Directional light tuning | `component.set_property` | `component` / `set_property` | Verified | Live IPC set `DirectionalLight.LightColor`, `SkyColor`, `FogMode`, `FogStrength`, `Shadows`, `ShadowBias`, and `ShadowHardness` on the scene Sun. |
| Point light authoring | `gameobject.create`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | Live IPC created a warm brazier point light and set radius, attenuation, fog, color, and shadow properties. |
| Spot light authoring | `gameobject.create`, `gameobject.set_transform`, `component.add`, `component.set_property` | `gameobject`, `component` | Verified | Live IPC created an obelisk spot light and set cone, radius, attenuation, fog, color, and shadow properties. |
| Post-process components | `component.add`, `component.set_property` | `component` | Verified | Live IPC configured `FilmGrain`, `Tonemapping`, `Bloom`, `PostProcessVolume`, `Vignette`, and `ColorAdjustments` for the ARPG visual pass. |
| DecalRenderer material assignment | `component.set_property` with `includeAll: true` | `component` | Verified | `DecalRenderer.Material` is public/non-inspector; setting it works when callers opt into all readable properties. Verified with blood/gold/void/bone decal materials. |
| Basic particle stack setup | `gameobject.create`, `component.add`, `component.set_property` | `gameobject`, `component` | Partial | Live IPC created `ParticleEffect`, `ParticleConeEmitter`, `ParticleSpriteRenderer`, and `ParticleLightRenderer` and set basic bool/number/color properties. Complex particle wrapper types remain unsupported. |
| Camera capture feedback | `visual.capture_camera` | `visual` / `capture_camera` | Verified | Live IPC rendered the active main `CameraComponent` to a PNG under `%TEMP%/sbox-agent-bridge/captures` and returned camera metadata plus luminance stats. First ARPG smoke capture at 640x360 reported average luminance `0.1332` and dark pixel ratio `0.3145`. |
| Viewport/HUD capture | TBD | TBD | Planned | `visual.capture_camera` captures camera output, not the editor/game viewport overlay. Runtime UI state can now be inspected through test actions, but pixel/viewport HUD capture is still missing. |
| Effects, beams, tracers, and effect lifetime | TBD | TBD | Planned | Basic particle component setup is partially covered, but there are no domain helpers for effect resources, beams, tracers, animated effects, or lifetime configuration. |
| Shader and ShaderGraph authoring | TBD | TBD | Planned | Bridge can create simple material sources and set known material params, but cannot inspect or edit custom shaders or ShaderGraph assets. |
| Render hooks/custom rendering | TBD | TBD | Planned | No bridge action covers render hooks, command lists, custom rendering, `SceneCamera`, or render targets. |
| ScreenPanel and UI-render-target helpers | TBD | TBD | Planned | No direct helpers for screen panels or rendering images to UI. |
| VR rendering | TBD | TBD | Planned | No VR render/session helpers. |

## Project, Code, And Editor Tooling Coverage

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Game/addon/editor project metadata | `bridge.status`, `editor.context` | `editor` / `status` / `context` | Partial | Bridge assumes the installed editor library and active project; it exposes the current scene/editor context, but has no project-type inspector or project metadata workflow. |
| Editor widgets, dialogs, menus, tools, and asset picker | TBD | TBD | Planned | Bridge provides the Agent Bridge dock and frame pump, but no generic API for creating/querying editor widgets, dialogs, menubar entries, tools, asset picker state, or scene-editor extensions. |
| Asset previews, model editor, mapping, and texture generators | TBD | TBD | Planned | Official editor docs include asset previews, mapping, the model editor, and texture generators; bridge has no editor-tool automation for these surfaces. |
| Custom editors and property attributes | TBD | TBD | Planned | Bridge can inspect/set serialized properties generically, but does not author or validate custom inspector/editor code or property attribute behavior. |
| Code diagnostics beyond compile events | `editor.compile_status` | `editor` / `compile_status` | Partial | Compile groups and recent diagnostics are visible, but there is no code navigation, symbol search, generated API lookup, analyzer surface, or explicit hotload-wait action. |
| Code generation and hotload control | `editor.compile_status`, `editor.feedback` | `editor` | Partial | Compile/hotload feedback is observable through editor events and logs, but bridge cannot inspect generated code artifacts or explicitly block until a named hotload generation is complete. |
| s&box C# unit tests | TBD | TBD | Planned | Official docs include unit tests; repo CI covers the MCP TypeScript client, but bridge has no s&box C# test runner integration. |
| Console variables, API whitelist, math types, and library reference lookup | TBD | TBD | Planned | No bridge helper searches the local API reference, console variables, whitelist, or library docs. |
| Standalone/export workflows | TBD | TBD | Planned | Official docs cover standalone builds; bridge intentionally has no packaging/export automation yet. |

## Networking And Multiplayer

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Networking systems/state inspection | TBD | TBD | Planned | No bridge tools inspect network mode, host/client state, connection state, transport state, or network diagnostics. |
| Connection and user permissions | TBD | TBD | Planned | No helpers inspect or mutate connection permission/user permission state. |
| Networked objects, ownership, visibility, and custom snapshot data | TBD | TBD | Planned | No tools inspect networked object registrations, ownership, visibility filtering, or custom snapshot payloads. |
| Sync properties | TBD | TBD | Planned | No tools inspect `[Sync]`/networked properties, dirty state, or replication behavior. |
| RPC messages and network events | TBD | TBD | Planned | No bridge support for discovering RPC methods/network events, validating call targets, or tracing invocation flow. |
| Serverside code, dedicated servers, and local multiplayer testing | TBD | TBD | Planned | No helpers launch or attach dedicated servers, start multi-client local test sessions, or report server/client roles. |
| HTTP requests and WebSockets | TBD | TBD | Planned | No s&box-networking wrapper for HTTP/WebSocket diagnostics or controlled request tests. |

## UI

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Runtime UI state inspection | `runtime.run_test_action` | `runtime` / `run_test_action` | Verified | The ARPG controller exposes a property-protocol test action that reports logical HUD/orb/inventory/player/combat state. Live smoke verified inventory open, damage, restore, skills, and zombie count. It also reported that actual ScreenPanel child panels are not built in the current testbed. |
| Screen UI/panel hierarchy inspection | TBD | TBD | Planned | No generic bridge tool inspects UI panel trees, Razor component instances, or resolved styles. Runtime self-report can expose component-authored UI state, but not arbitrary panel hierarchy/pixels. |
| Razor/component authoring helpers | `script.create`, `script.edit` | `script` | Partial | Agents can edit source files, but there are no UI-specific templates, schemas, style checks, or live panel verification. |
| HudPainter | TBD | TBD | Planned | No helpers inspect or verify immediate-mode HUD drawing. |
| Localization | TBD | TBD | Planned | No helpers inspect localization files, translation keys, or active locale state. |
| Styling, style properties, events, and class state | TBD | TBD | Planned | No helpers inspect Razor/CSS class state, event bindings, focus/hover state, or style resolution. |
| VirtualGrid | TBD | TBD | Planned | No helpers inspect virtualized UI data sources, visible ranges, or selection/focus state. |

## ActionGraph, Movie Maker, And Game Mounts

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| ActionGraph inspection/authoring | TBD | TBD | Planned | Official docs include ActionGraph, but bridge has no graph inspection, node authoring, or validation support. |
| Movie Maker workflows | TBD | TBD | Planned | No support for movie maker timelines, cameras, keyframes, preview capture, or export. |
| Game mounts | TBD | TBD | Planned | No support for mount configuration or mounted-content inspection beyond normal asset search. |

## Media

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Video playback/media assets | TBD | TBD | Planned | Official docs include video; bridge has no video asset inspection, playback, or capture validation helpers. |
| Audio media workflows | `sound.list`, `sound.get_info`, `sound.create_event`, `sound.assign`, `sound.preview` | `sound` | Partial | Sound events and preview are verified, but there are no broader audio media helpers for playback graph/state inspection or non-sound-event workflows. |
| Sound API coverage | `sound.*` | `sound` | Partial | Bridge covers common sound asset/event operations, but not the full sound API surface documented by s&box. |

## Gameplay Systems

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Input actions/settings | TBD | TBD | Planned | ARPG proof-of-concept work edited `ProjectSettings/Input.config` manually; bridge has no input binding inspector/editor. |
| Controller input, raw input, and glyphs | TBD | TBD | Planned | No helpers inspect gamepad/raw input state, action glyphs, or input device metadata. |
| Navigation/navmesh | TBD | TBD | Planned | No navigation mesh build/read helpers, navmesh agent inspection, area/cost/filter controls, obstacle inspection, or link diagnostics. |
| Terrain | TBD | TBD | Planned | No terrain sculpt, paint, material, read, or placement helpers. |
| Clutter system | TBD | TBD | Planned | No clutter authoring, inspection, or density validation helpers. |
| Runtime gameplay state/self-report | `runtime.list_test_actions`, `runtime.run_test_action`, `scene.* targetSession=runtime` | `runtime`, `scene` | Verified | Live-smoked against the ARPG GameSession. Runtime scene reads target the live `GameSession`; component-authored test actions provide gameplay/UI state without relying on screenshots. |
| Runtime input/test actions | `runtime.run_test_action` | `runtime` / `run_test_action` | Verified | Deterministic component-authored test actions are verified through a property protocol. This replaces shell-level OS keypresses for agent verification; focused viewport input injection is still future work. |
| VR gameplay | TBD | TBD | Planned | No VR gameplay/session/controller helpers. |

## Services

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Achievements | TBD | TBD | Planned | No achievement definition, read, unlock, or reset helpers. |
| Auth tokens/identity | TBD | TBD | Planned | No auth token, session identity, or permission helper. |
| Leaderboards and stats | TBD | TBD | Planned | No leaderboard or stats read/write helpers. |
| Web API calls | TBD | TBD | Planned | No s&box Web API wrapper; any network access should remain explicit and scoped. |

## Animation

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Animation graph inspection | TBD | TBD | Planned | No animation graph, parameter, transition, or clip inspection. |
| Animation state machines, layers, and IK | TBD | TBD | Planned | No state machine, layer, IK, or blend tree helpers. |
| SkinnedModelRenderer/citizen animation helpers | `component.add`, `component.set_property` | `component` | Unverified | Generic component tools may add/configure visible animation components by exact type/property, but no dedicated animation smoke has verified this workflow. |
| Animation events and automated animation | TBD | TBD | Planned | No tools for animation event inspection, automated animation setup, or event validation. |

## Testing And CI

| Capability | Status | Notes |
|---|---|---|
| MCP TypeScript build | Verified | `npm` is not on PATH in the current shell and `npm run check` hits an `Access is denied` shim issue, but direct execution through the installed Node runtime works: `node node_modules/typescript/bin/tsc -p tsconfig.json --noEmit` and `node node_modules/typescript/bin/tsc -p tsconfig.json` both passed. |
| MCP bridge-client tests | Verified | `npm test` covers success, bridge error, and timeout behavior with fake file IPC. |
| CI MCP build/test | Implemented | GitHub Actions workflow runs typecheck, tests, and build. |
| JSON/sbproj validation | Implemented | GitHub Actions workflow added. |
| Runtime feedback smoke script | Verified | `mcp-server/test/runtime-feedback-smoke.ts` verifies `targetSession: runtime`, runtime test-action listing/invocation, ARPG logical UI state, inventory open, damage, and restore. |
| Live editor smoke script | Blocked | Feedback actions, compile status, save-state reporting, `scene.batch`, and resource-backed ModelRenderer property mutations were directly verified, but the full smoke is currently blocked by the `gameobject.destroy` native delete/undo null reference in this editor session. Fixture-backed component mutation is skipped unless `AgentBridgeMutationFixture` is visible; use `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` to require it. |
| Automated s&box editor tests | Blocked | Requires a reliable way to run/control s&box editor in CI. |
