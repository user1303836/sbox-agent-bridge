# Capability Matrix

Status meanings:

- **Verified**: tested against a live s&box editor.
- **Partial**: useful behavior is verified, but a known limitation remains.
- **Implemented**: code exists, but live editor verification is pending.
- **Planned**: intended, not implemented.
- **Blocked**: known blocker or missing verified API.

## Current Verification Environment

- Date: 2026-04-30
- s&box project: fresh Minimal Game project
- Bridge install path: `Libraries/sbox_agent_bridge`
- Transport: file IPC at `%TEMP%/sbox-agent-bridge`

## Bridge And Editor

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Bridge status | `bridge.status` | `editor` / `status` | Verified | Returns running state, IPC root, active scene, play state. |
| Editor context | `editor.context` | `editor` / `context` | Verified | Direct file-IPC read verified with selected GameObject details. Includes active tab/session metadata so agents can see which scene tab commands will target. |
| List editor tabs | `editor.tabs` | `editor` / `tabs` | Verified | Lists open `SceneEditorSession` tabs with index, scene id, active flag, source path, dirty state, and play state. Live IPC verified with both `Untitled Scene` and `scenes/minimal.scene` open. |
| Activate editor tab | `editor.activate_tab` | `editor` / `activate_tab` | Verified | Activates an existing editor scene tab by index, scene id, source path, or scene name. Live IPC verified by raising `scenes/minimal.scene` while an unsaved untitled tab was also open. |
| Open scene | `editor.open_scene` | `editor` / `open_scene` | Verified | Opens a scene resource by path and makes its session active. Falls back through `AssetSystem.FindByPath` when direct resource lookup fails. Supports `forceReload` for reloading an already-open sourced scene when play/stop leaves the editor session stale. |
| Save scene | `editor.save_scene` | `editor` / `save_scene` | Verified | Reports before/after dirty state and scene source path; guards untitled scenes instead of opening a save-as flow. Live IPC verified dry-run, no-source skip behavior, and disk write against `scenes/minimal.scene`. |
| Undo | `editor.undo` | `editor` / `undo` | Verified | Verified by restoring a destroyed GameObject. |
| Redo | `editor.redo` | `editor` / `redo` | Verified | Verified by re-applying the destroy operation after undo. |
| Selection read | `editor.get_selection` | `editor` / `get_selection` | Verified | Returns typed selection entries; GameObject selection verified. |
| Selection set | `editor.set_selection` | `editor` / `set_selection` | Verified | Accepts GameObject ids only; verified with read-back count. |
| Frame/focus object | `editor.frame_object` | `editor` / `frame_object` | Verified | Calls `SceneEditorSession.FrameTo` for a target GameObject bounds. |
| Play state | `editor.play_state` | `editor` / `play_state` | Verified | Reads active scene and play state from `SceneEditorSession.Active`; when available, includes `gameSessionDetails` with runtime session type, scene, parent, object count, and component count. Live smoke and direct IPC verified. |
| Play mode start | `editor.play` | `editor` / `play` | Verified | Calls `SceneEditorSession.SetPlaying(...)` and returns read-back play state. |
| Play mode stop | `editor.stop` | `editor` / `stop` | Verified | Calls `SceneEditorSession.StopPlaying()` and returns immediate read-back with `transitionPending` when the editor settles on a later frame. |
| Recent logs | `editor.logs` | `editor` / `logs` | Verified | Tails `sbox-dev.log`; raw lines are authoritative and log level is explicitly inferred. |
| Compile status | `editor.compile_status` | `editor` / `compile_status` | Verified | Tracks compile groups from observed `compile.started` events; live IPC returned compiler diagnostics and zero errors after the fix. |
| Combined feedback | `editor.feedback` | `editor` / `feedback` | Verified | Returns play state, compile status, and recent logs for agent edit/test loops. |

## Scene Read

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Scene summary | `scene.summary` | `scene` / `summary` | Verified | Returns object/component counts from active scene. |
| Scene hierarchy | `scene.hierarchy` | `scene` / `hierarchy` | Verified | Live IPC read-back verified the ARPG fixture yard hierarchy after scene mutations. |
| Find GameObjects | `scene.find` | `scene` / `find` | Verified | Verified by finding a created object. |
| Object details | `scene.details` | `scene` / `details` | Verified | Includes id, parent, enabled/active state, transforms, components, child count. |
| Batch operations | `scene.batch` | `scene` / `batch` | Verified | Runs bounded action lists with per-operation result capture and `$ref` alias substitution. Live IPC verified object create/reparent/duplicate/rename/enable/selection/focus plus asset assignment, sound assignment, physics/collider/joint, material-property, and raycast actions. |
| Find in radius | `scene.find_in_radius` | TBD | Planned | Useful for spatial workflows. |

## GameObjects

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Read GameObject | `gameobject.get` | `gameobject` / `get` | Verified | Id-targeted read returning the same detail shape as `scene.details`. |
| Create GameObject | `gameobject.create` | `gameobject` / `create` | Verified | Uses `SceneEditorSession.Active.Scene.CreateObject(true)` and verifies via read-back; optional `parentId` was verified through `scene.batch`. |
| Destroy GameObject | `gameobject.destroy` | `gameobject` / `destroy` | Blocked | Previously verified, but the current editor session now reports a null reference in the native editor delete/undo path after play-mode testing. Needs fresh-session verification and/or a safer delete strategy before relying on it. |
| Rename GameObject | `gameobject.rename` | `gameobject` / `rename` | Verified | Id-targeted, undo scoped, unique-name default verified by read-back. |
| Set transform | `gameobject.set_transform` | `gameobject` / `set_transform` | Verified | World position, Euler/quaternion rotation input, and world scale; verified by read-back. |
| Enable/disable | `gameobject.set_enabled` | `gameobject` / `set_enabled` | Verified | Verified false and true read-back on a live object. |
| Reparent | `gameobject.reparent` | `gameobject` / `reparent` | Verified | Preserves world transform by default; supports moving back to scene root. |
| Duplicate | `gameobject.duplicate` | `gameobject` / `duplicate` | Verified | Shallow scene-attached duplicate of name/enabled/transform/parent. Component and child cloning are future work. |

## Components

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| List component types | `component.list_types` | `component` / `list_types` | Partial | Uses `Game.TypeLibrary.GetTypes(typeof(Component))`; live smoke returned built-in/editor-visible types. Local game components such as `AgentBridgeArpgFixture` are not enumerated even after instances exist. |
| List GameObject components | `component.list_on_gameobject` | `component` / `list_on_gameobject` | Verified | Id-targeted GameObject component list. |
| Read component | `component.get` | `component` / `get` | Verified | Id-targeted component read with owning GameObject context. |
| Get properties | `component.get_properties` | `component` / `get_properties` | Verified | Read-only metadata/value inspection; defaults to `[Property]` inspector properties and includes schema hints, attributes, enum values, and reference targets for settable JSON shapes. |
| Add component | `component.add` | `component` / `add` | Verified | Built-in/editor-visible components use TypeLibrary. Local compiled game components can be added by exact C# type name through a serialized-probe fallback that resolves the runtime type, then calls `GameObject.AddComponent<T>()`; live verified with `AgentBridgeArpgFixture` on ARPG fixture objects without duplicating existing components. |
| Remove component | `component.remove` | `component` / `remove` | Verified | Undo scoped; live smoke verifies `component.get` fails after removal, undo restores, redo removes again. |
| Set property | `component.set_property` | `component` / `set_property` | Verified | Live smoke verifies string, bool, numeric primitives, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject`, and `Component` values through `AgentBridgeMutationFixture`; live IPC also verified resource paths on built-in `ModelRenderer.Model` and `ModelRenderer.MaterialOverride`, plus local `AgentBridgeArpgFixture` enum/string/int properties; supports `dryRun: true`. |
| Validate property | `component.validate_property` | `component` / `validate_property` | Verified | Converts and resolves a candidate value without mutation; live smoke verifies valid conversion, invalid rejection, and unchanged fixture values. |
| Enable/disable component | `component.set_enabled` | `component` / `set_enabled` | Verified | Live smoke verifies false and true read-back. |

## Scripts

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Create script | `script.create` | `script` / `create` | Verified | Live IPC created `Code/ArpgDemo/AgentBridgeArpgFixture.cs`; compile status stayed green. |
| Edit script | `script.edit` | `script` / `edit` | Verified | Live IPC edited the same script and verified SHA/length change plus green compile. |
| Delete script | `script.delete` | `script` / `delete` | Not run | Local file deletion requires explicit action-time confirmation; no scratch script was deleted in this pass. |

## Assets, Materials, Sounds, Physics, And Prefabs

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Search assets | `asset.search` | `asset` / `search` | Verified | Live IPC found built-in models/materials and newly created ARPG materials. |
| Inspect asset | `asset.get_info` | `asset` / `get_info` | Verified | Live IPC inspected built-in material metadata and project-created assets. |
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
| Create prefab | `prefab.create` | `prefab` / `create` | Verified | Serialized a live GameObject to `prefabs/agent_bridge/arpg_loot_chest_fixture.prefab`; list/get-info verified the resource. |
| List prefabs | `prefab.list` | `prefab` / `list` | Verified | Live IPC listed built-in prefabs and the project-created ARPG prefab. |
| Inspect prefab | `prefab.get_info` | `prefab` / `get_info` | Verified | Uses `AssetSystem` fallback when `PrefabFile.Load(path)` cannot load a project-created prefab path. |
| Instantiate prefab | `prefab.instantiate` | `prefab` / `instantiate` | Verified | Instantiates by deserializing prefab `RootObject` into the active editor scene with fresh GUIDs; live IPC created `Loot Chest Prefab Instance - bridge fixture`. |

## Testing And CI

| Capability | Status | Notes |
|---|---|---|
| MCP TypeScript build | Blocked in current shell | Previously verified, but this Windows shell currently returns `Access is denied` for the available `node.exe` shim and `npm` is not on PATH. `dist/` was manually kept in sync with `src/` for the new MCP tools. |
| MCP bridge-client tests | Verified | `npm test` covers success, bridge error, and timeout behavior with fake file IPC. |
| CI MCP build/test | Implemented | GitHub Actions workflow runs typecheck, tests, and build. |
| JSON/sbproj validation | Implemented | GitHub Actions workflow added. |
| Live editor smoke script | Blocked | Feedback actions, compile status, save-state reporting, `scene.batch`, and resource-backed ModelRenderer property mutations were directly verified, but the full smoke is currently blocked by the `gameobject.destroy` native delete/undo null reference in this editor session. Fixture-backed component mutation is skipped unless `AgentBridgeMutationFixture` is visible; use `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` to require it. |
| Automated s&box editor tests | Blocked | Requires a reliable way to run/control s&box editor in CI. |
