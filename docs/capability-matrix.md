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
| Editor context | `editor.context` | `editor` / `context` | Implemented | Needs MCP end-to-end verification. |
| Save scene | `editor.save_scene` | TBD | Planned | Should call active session save and verify dirty state. |
| Undo | `editor.undo` | TBD | Planned | Need verify editor undo API entry point. |
| Redo | `editor.redo` | TBD | Planned | Need verify editor redo API entry point. |
| Selection read | `editor.get_selection` | TBD | Planned | Important before mutating selected objects. |
| Selection set | `editor.set_selection` | TBD | Planned | Should accept object ids only. |
| Play mode start | `editor.play` | TBD | Planned | Use active scene/session APIs. |
| Play mode stop | `editor.stop` | TBD | Planned | Keep runtime/editor state separate. |
| Recent logs | `editor.logs` | TBD | Planned | Need reliable editor log capture path. |

## Scene Read

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Scene summary | `scene.summary` | `scene` / `summary` | Verified | Returns object/component counts from active scene. |
| Scene hierarchy | `scene.hierarchy` | `scene` / `hierarchy` | Implemented | Needs MCP end-to-end verification. |
| Find GameObjects | `scene.find` | `scene` / `find` | Verified | Verified by finding a created object. |
| Object details | `scene.details` | TBD | Planned | Should include transform, parent, children, components, tags. |
| Find in radius | `scene.find_in_radius` | TBD | Planned | Useful for spatial workflows. |

## GameObjects

| Capability | Bridge Action | MCP Tool | Status | Notes |
|---|---|---|---|---|
| Create GameObject | `gameobject.create` | `gameobject` / `create` | Verified | Uses `SceneEditorSession.Active.Scene.CreateObject(true)` and verifies via read-back. |
| Destroy GameObject | `gameobject.destroy` | TBD | Planned | Must use undo scope and id targeting. |
| Rename GameObject | `gameobject.rename` | TBD | Planned | Verify unique-name behavior. |
| Set transform | `gameobject.set_transform` | TBD | Planned | Position, rotation, scale; verify exact read-back. |
| Enable/disable | `gameobject.set_enabled` | TBD | Planned | Include disabled subtree behavior in tests. |
| Reparent | `gameobject.reparent` | TBD | Planned | Preserve world transform by default. |
| Duplicate | `gameobject.duplicate` | TBD | Planned | Return created id and source id. |

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
| CI MCP build | Implemented | GitHub Actions workflow added. |
| JSON/sbproj validation | Implemented | GitHub Actions workflow added. |
| Live editor smoke tests | Planned | Needs script or documented manual flow. |
| Automated s&box editor tests | Blocked | Requires a reliable way to run/control s&box editor in CI. |
