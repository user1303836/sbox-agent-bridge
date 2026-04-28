# Capability Matrix

Status meanings:

- **Verified**: tested against a live s&box editor.
- **Implemented**: code exists, but live editor verification is pending.
- **Planned**: intended, not implemented.
- **Blocked**: known blocker or missing verified API.

## Current Verification Environment

- Date: 2026-04-28
- s&box project: fresh Minimal Game project
- Bridge install path: `Libraries/sbox_agent_bridge`
- Transport: file IPC at `%TEMP%/sbox-agent-bridge`

## Bridge And Editor

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Bridge status | `bridge.status` | `editor` / `status` | Verified | Returns running state, IPC root, active scene, play state. |
| Editor context | `editor.context` | `editor` / `context` | Verified | Direct file-IPC read verified with selected GameObject details. |
| Save scene | `editor.save_scene` | `editor` / `save_scene` | Implemented | Calls active session save; live verification pending because the smoke scene was untitled. |
| Undo | `editor.undo` | `editor` / `undo` | Verified | Verified by restoring a destroyed GameObject. |
| Redo | `editor.redo` | `editor` / `redo` | Verified | Verified by re-applying the destroy operation after undo. |
| Selection read | `editor.get_selection` | `editor` / `get_selection` | Verified | Returns typed selection entries; GameObject selection verified. |
| Selection set | `editor.set_selection` | `editor` / `set_selection` | Verified | Accepts GameObject ids only; verified with read-back count. |
| Frame/focus object | `editor.frame_object` | `editor` / `frame_object` | Verified | Calls `SceneEditorSession.FrameTo` for a target GameObject bounds. |
| Play mode start | `editor.play` | TBD | Planned | Use active scene/session APIs. |
| Play mode stop | `editor.stop` | TBD | Planned | Keep runtime/editor state separate. |
| Recent logs | `editor.logs` | TBD | Planned | Need reliable editor log capture path. |

## Scene Read

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Scene summary | `scene.summary` | `scene` / `summary` | Verified | Returns object/component counts from active scene. |
| Scene hierarchy | `scene.hierarchy` | `scene` / `hierarchy` | Implemented | Needs MCP end-to-end verification. |
| Find GameObjects | `scene.find` | `scene` / `find` | Verified | Verified by finding a created object. |
| Object details | `scene.details` | `scene` / `details` | Verified | Includes id, parent, enabled/active state, transforms, components, child count. |
| Find in radius | `scene.find_in_radius` | TBD | Planned | Useful for spatial workflows. |

## GameObjects

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Read GameObject | `gameobject.get` | `gameobject` / `get` | Verified | Id-targeted read returning the same detail shape as `scene.details`. |
| Create GameObject | `gameobject.create` | `gameobject` / `create` | Verified | Uses `SceneEditorSession.Active.Scene.CreateObject(true)` and verifies via read-back. |
| Destroy GameObject | `gameobject.destroy` | `gameobject` / `destroy` | Verified | Id-targeted, undo scoped, verified by directory lookup and undo/redo smoke test. |
| Rename GameObject | `gameobject.rename` | `gameobject` / `rename` | Verified | Id-targeted, undo scoped, unique-name default verified by read-back. |
| Set transform | `gameobject.set_transform` | `gameobject` / `set_transform` | Verified | World position, Euler/quaternion rotation input, and world scale; verified by read-back. |
| Enable/disable | `gameobject.set_enabled` | `gameobject` / `set_enabled` | Verified | Verified false and true read-back on a live object. |
| Reparent | `gameobject.reparent` | `gameobject` / `reparent` | Verified | Preserves world transform by default; supports moving back to scene root. |
| Duplicate | `gameobject.duplicate` | `gameobject` / `duplicate` | Verified | Shallow scene-attached duplicate of name/enabled/transform/parent. Component and child cloning are future work. |

## Components

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| List component types | `component.list_types` | TBD | Planned | Use s&box type metadata. |
| Add component | `component.add` | TBD | Planned | Verify created component type/id. |
| Remove component | `component.remove` | TBD | Planned | Undoable destruction. |
| Get properties | `component.get_properties` | TBD | Planned | Public readable properties, compact values. |
| Set property | `component.set_property` | TBD | Planned | Type conversion is the hard part. |
| Enable/disable component | `component.set_enabled` | TBD | Planned | Verify `Enabled` read-back. |

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
| Live editor smoke tests | Verified | Manual direct file-IPC flow verified core GameObject edits on 2026-04-28. |
| Automated s&box editor tests | Blocked | Requires a reliable way to run/control s&box editor in CI. |
