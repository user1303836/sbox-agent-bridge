# Capability Matrix

Status meanings:

- **Verified**: tested against a live s&box editor.
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
| Editor context | `editor.context` | `editor` / `context` | Verified | Direct file-IPC read verified with selected GameObject details. |
| Open scene | `editor.open_scene` | `editor` / `open_scene` | Verified | Opens a scene resource by path and makes its session active. Supports `forceReload` for reloading an already-open sourced scene when play/stop leaves the editor session stale. |
| Save scene | `editor.save_scene` | `editor` / `save_scene` | Verified | Reports before/after dirty state and scene source path; guards untitled scenes instead of opening a save-as flow. Live IPC verified dry-run, no-source skip behavior, and disk write against `scenes/minimal.scene`. |
| Undo | `editor.undo` | `editor` / `undo` | Verified | Verified by restoring a destroyed GameObject. |
| Redo | `editor.redo` | `editor` / `redo` | Verified | Verified by re-applying the destroy operation after undo. |
| Selection read | `editor.get_selection` | `editor` / `get_selection` | Verified | Returns typed selection entries; GameObject selection verified. |
| Selection set | `editor.set_selection` | `editor` / `set_selection` | Verified | Accepts GameObject ids only; verified with read-back count. |
| Frame/focus object | `editor.frame_object` | `editor` / `frame_object` | Verified | Calls `SceneEditorSession.FrameTo` for a target GameObject bounds. |
| Play state | `editor.play_state` | `editor` / `play_state` | Verified | Reads active scene and play state from `SceneEditorSession.Active`; live smoke and direct IPC verified. |
| Play mode start | `editor.play` | `editor` / `play` | Verified | Calls `SceneEditorSession.SetPlaying(...)` and returns read-back play state. |
| Play mode stop | `editor.stop` | `editor` / `stop` | Verified | Calls `SceneEditorSession.StopPlaying()` and returns immediate read-back with `transitionPending` when the editor settles on a later frame. |
| Recent logs | `editor.logs` | `editor` / `logs` | Verified | Tails `sbox-dev.log`; raw lines are authoritative and log level is explicitly inferred. |
| Compile status | `editor.compile_status` | `editor` / `compile_status` | Verified | Tracks compile groups from observed `compile.started` events; live IPC returned compiler diagnostics and zero errors after the fix. |
| Combined feedback | `editor.feedback` | `editor` / `feedback` | Verified | Returns play state, compile status, and recent logs for agent edit/test loops. |

## Scene Read

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Scene summary | `scene.summary` | `scene` / `summary` | Verified | Returns object/component counts from active scene. |
| Scene hierarchy | `scene.hierarchy` | `scene` / `hierarchy` | Implemented | Needs MCP end-to-end verification. |
| Find GameObjects | `scene.find` | `scene` / `find` | Verified | Verified by finding a created object. |
| Object details | `scene.details` | `scene` / `details` | Verified | Includes id, parent, enabled/active state, transforms, components, child count. |
| Batch operations | `scene.batch` | `scene` / `batch` | Verified | Runs a bounded list of existing bridge actions with per-operation result capture and `$ref` alias substitution; direct IPC verified create parent/child, add `ModelRenderer`, set model/material properties, save-state check, and details read-back. |
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
| List component types | `component.list_types` | `component` / `list_types` | Verified | Uses `Game.TypeLibrary.GetTypes(typeof(Component))`; live smoke returned 131 types. |
| List GameObject components | `component.list_on_gameobject` | `component` / `list_on_gameobject` | Verified | Id-targeted GameObject component list. |
| Read component | `component.get` | `component` / `get` | Verified | Id-targeted component read with owning GameObject context. |
| Get properties | `component.get_properties` | `component` / `get_properties` | Verified | Read-only metadata/value inspection; defaults to `[Property]` inspector properties and includes schema hints, attributes, enum values, and reference targets for settable JSON shapes. |
| Add component | `component.add` | `component` / `add` | Verified | Live smoke adds `AgentBridgeMutationFixture` by type name and verifies id/type read-back. |
| Remove component | `component.remove` | `component` / `remove` | Verified | Undo scoped; live smoke verifies `component.get` fails after removal, undo restores, redo removes again. |
| Set property | `component.set_property` | `component` / `set_property` | Verified | Live smoke verifies string, bool, numeric primitives, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject`, and `Component` values through `AgentBridgeMutationFixture`; live IPC also verified resource paths on built-in `ModelRenderer.Model` and `ModelRenderer.MaterialOverride`; supports `dryRun: true`. |
| Validate property | `component.validate_property` | `component` / `validate_property` | Verified | Converts and resolves a candidate value without mutation; live smoke verifies valid conversion, invalid rejection, and unchanged fixture values. |
| Enable/disable component | `component.set_enabled` | `component` / `set_enabled` | Verified | Live smoke verifies false and true read-back. |

## Assets And Prefabs

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Search assets | `asset.search` | TBD | Planned | First asset capability to implement. |
| Open asset | `asset.open` | TBD | Planned | Useful for human-visible workflows. |
| Inspect prefab | `prefab.inspect` | TBD | Planned | Prefer editor/resource APIs over raw JSON when possible. |
| Instantiate prefab | `prefab.instantiate` | TBD | Planned | Return root object id and prefab path. |

## Testing And CI

| Capability | Status | Notes |
|---|---|---|
| MCP TypeScript build | Verified | `npm run build` passes locally. |
| MCP bridge-client tests | Verified | `npm test` covers success, bridge error, and timeout behavior with fake file IPC. |
| CI MCP build/test | Implemented | GitHub Actions workflow runs typecheck, tests, and build. |
| JSON/sbproj validation | Implemented | GitHub Actions workflow added. |
| Live editor smoke script | Blocked | Feedback actions, compile status, save-state reporting, `scene.batch`, and resource-backed ModelRenderer property mutations were directly verified, but the full smoke is currently blocked by the `gameobject.destroy` native delete/undo null reference in this editor session. Fixture-backed component mutation is skipped unless `AgentBridgeMutationFixture` is visible; use `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` to require it. |
| Automated s&box editor tests | Blocked | Requires a reliable way to run/control s&box editor in CI. |
