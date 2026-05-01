# Bridge Protocol

## Request

```json
{
  "id": "uuid",
  "action": "scene.summary",
  "payload": {}
}
```

## Response

```json
{
  "id": "uuid",
  "ok": true,
  "result": {
    "message": "Scene summary read",
    "verified": {}
  }
}
```

Errors use the same envelope:

```json
{
  "id": "uuid",
  "ok": false,
  "error": {
    "message": "No active editor scene",
    "suggestion": "Open a scene in the s&box editor, then confirm the bridge library compiled and the Agent Bridge dock is available."
  }
}
```

## Actions

- `bridge.status`
- `bridge.doctor`
- `editor.context`
- `editor.project_info`
- `editor.tabs`
- `editor.activate_tab`
- `editor.new_scene`
- `editor.open_scene`
- `editor.recover_scene`
- `editor.get_selection`
- `editor.set_selection`
- `editor.save_scene`
- `editor.save_scene_as`
- `editor.undo`
- `editor.redo`
- `editor.frame_object`
- `editor.play_state`
- `editor.play`
- `editor.stop`
- `editor.logs`
- `editor.compile_status`
- `editor.feedback`
- `runtime.list_test_actions`
- `runtime.run_test_action`
- `script.create`
- `script.edit`
- `script.delete`
- `asset.search`
- `asset.get_info`
- `asset.inspect_model`
- `asset.inspect_material`
- `asset.set_material_source_property`
- `asset.preview_model`
- `asset.get_orientation_override`
- `asset.set_orientation_override`
- `asset.assign_model`
- `asset.assign_material`
- `asset.create_material`
- `asset.set_material_property`
- `visual.capture_camera`
- `sound.list`
- `sound.get_info`
- `sound.inspect`
- `sound.create_event`
- `sound.assign`
- `sound.preview`
- `physics.inspect`
- `physics.add_physics`
- `physics.add_collider`
- `physics.add_joint`
- `physics.raycast`
- `prefab.create`
- `prefab.list`
- `prefab.get_info`
- `prefab.inspect_instance`
- `prefab.instantiate`
- `scene.summary`
- `scene.hierarchy`
- `scene.find`
- `scene.details`
- `scene.batch`
- `gameobject.get`
- `gameobject.create`
- `gameobject.rename`
- `gameobject.set_transform`
- `gameobject.set_enabled`
- `gameobject.destroy`
- `gameobject.duplicate`
- `gameobject.reparent`
- `gameobject.place_asset`
- `component.list_types`
- `component.list_on_gameobject`
- `component.get`
- `component.get_properties`
- `component.add`
- `component.remove`
- `component.set_enabled`
- `component.set_property`
- `component.validate_property`

## Mutation Rule

Mutations should return a `verified` object read back from the editor after the operation. If `verified` is missing, the caller should assume the change may not have stuck.

Vector payloads must be complete. For `Vector3` fields, direct protocol callers must include numeric `x`, `y`, and `z` values; partial vector objects are rejected before mutation.

`editor.project_info` reads active project metadata and paths, including project title/type/ident, root/assets/code/editor paths, compiler availability, bridge install path, and current process directory. It does not create, switch, or open projects.

`editor.new_scene` creates a default editor scene tab. It accepts optional `name`, `bringToFront`, and `discardUnsaved`. If `path` is supplied, it also saves the new scene to that project path; `overwrite` allows replacing an existing scene asset and `activateAfterSave` reopens the saved scene asset after writing.

`editor.open_scene` accepts `path`, optional `bringToFront`, optional `forceReload`, and optional `discardUnsaved`. Use `forceReload: true` to reload an already-open sourced scene from disk after play-mode transitions leave the active editor session stale. If the open session has unsaved changes, the bridge refuses to reload unless `discardUnsaved: true` is also provided; reserve that for scratch/test scenes.

`bridge.doctor` is a read-only readiness check for testers and agents. It reports bridge/MCP version data, IPC writability, project paths, tab/session health, compile health, bridge-related logs, pass/warn/fail checks, and `nextSuggestedAction`.

`editor.recover_scene` accepts optional `path`, `stopAll`, `bringToFront`, `forceReload`, and `discardUnsaved`. If `path` is omitted, it tries to infer the active sourced editor scene. It stops playing sessions by default, reloads/reactivates the sourced scene, and returns before/after tab snapshots. Use `discardUnsaved:true` only for scratch/test scenes.

`editor.save_scene` returns before/after save state. `dryRun: true` reads save state without writing. Untitled scenes without a source path are guarded: the bridge returns `saveAttempted: false` and a `skippedReason` instead of opening a surprise save-as flow. When a save is attempted, `saveVerified` is true only if the after-state reports no unsaved changes.

`editor.save_scene_as` saves the active editor scene to a supplied project `path` without opening the human save-as dialog. It accepts `overwrite`, `bringToFront`, and `activateAfterSave`. The bridge registers/compiles the written scene asset, returns file/resource read-back, and can reopen the saved asset so the active tab has a source path.

`component.set_property` also accepts `dryRun: true`. In dry-run mode, the bridge resolves the component/property and converts the input value, but does not call `PropertyDescription.SetValue`.

`component.validate_property` performs the same conversion check without mutation. Its verified payload includes:

- `property`: metadata and schema for the target property.
- `current`: current read-back value.
- `converted`: converted value that would be assigned.
- `mutationApplied: false`.
- `valid: true`.

Property metadata includes `typeConversionSupported`, `setPropertySupported`, reflected attribute type names, and a `schema` block with kind, nullability, accepted JSON shapes, an example value, enum values, reference target, support status, and unsupported reason where applicable.

Resource-backed properties use `schema.kind: "resourceReference"` for `Sandbox.Resource` subclasses. They accept a string asset path, `{ "path": "..." }`, or `{ "resourcePath": "..." }`. Resource read-back includes:

- `path`: resource path reported by s&box.
- `name`: resource name.
- `id`: resource id.
- `isValid`: whether s&box loaded a valid resource.

## Visual And Spatial Feedback Actions

`asset.inspect_model` accepts `path` or `modelPath`, plus optional `scale`, `yaw`, and `includeMaterials`. It loads the model resource and returns model/render/physics bounds, material slots, common orientation candidates, candidate ground offsets, footprints, and limitations. Bounds are geometry facts; callers should not treat them as proof of semantic uprightness.

`asset.inspect_material` accepts `path` or `materialPath`. It loads the material and, when a readable `.vmat` source file exists, parses key/value properties, texture slots, color/vector values, and scalar-style params.

`asset.set_material_source_property` accepts `path` or `materialPath`, `property`, and `value`. Values can be booleans, numbers, strings, numeric arrays, `{ path }` / `{ resourcePath }`, color objects `{ r, g, b, a }`, or vector objects `{ x, y, z, w }`. It updates or inserts the `.vmat` source property, recompiles the asset, and returns material inspection read-back.

`asset.preview_model` accepts `path` or `modelPath`, optional `materialPath`, `targetSession`, session selectors, `width`, `height`, `name`, `scale`, `pitch`, `yaw`, and `roll`. It creates or reuses a temporary `NotSaved` preview rig in the resolved session, renders the model from a dedicated camera, and writes a PNG with luminance stats. Use `targetSession: "runtime"` while playing for reliable render output; stopped editor sessions can render black on current s&box builds.

`asset.get_orientation_override` accepts `path` or `modelPath` and reads the project-local override stored at `Assets/agent_bridge/orientation_overrides.json`. Missing overrides return `found: false` instead of failing.

`asset.set_orientation_override` accepts `path` or `modelPath`, `baseRotation`, optional `groundOffsetZ`, `forwardAxis`, `confidence`, `source`, and `notes`. If `groundOffsetZ` is omitted, the bridge calculates it from the model render bounds after applying the base rotation at scale 1. The file is written atomically without a UTF-8 BOM.

`gameobject.place_asset` accepts `modelPath`, optional `materialPath`, `name`, `parentId`, `position`, `yaw`, `scale`, `baseRotation`, `alignToGround`, and `requireOrientationOverride`. It creates a GameObject, adds a disabled `ModelRenderer`, assigns the model/material, applies the stored orientation override plus yaw, optionally lifts the object so transformed render bounds sit on the requested ground position, enables the renderer, and returns GameObject/component/bounds read-back. If `requireOrientationOverride` is true, missing orientation metadata is an error; otherwise the bridge falls back to the imported orientation and reports `orientationSource`.

`visual.capture_camera` accepts optional `targetSession`, `sessionId`, `sessionIndex`, `sessionPath`, or `sessionScene`, plus optional `cameraComponentId` or `gameObjectId`, `width`, `height`, and `name`. Without a camera id, it captures the enabled main camera, or the first enabled camera in the resolved session. The response includes a PNG path under `%TEMP%/sbox-agent-bridge/captures`, camera metadata, byte count, and luminance statistics:

- `average`: average relative luminance across the capture.
- `min` / `max`: darkest and brightest sampled pixel luminance.
- `darkPixelRatio`: fraction of pixels below the bridge's dark threshold.
- `brightPixelRatio`: fraction of pixels above the bridge's bright threshold.

## Prefab Actions

`prefab.instantiate` deserializes the prefab root into the active editor scene, remaps prefab GUIDs to fresh instance GUIDs, writes `__Prefab`/`__PrefabIdToInstanceId` metadata, applies the requested transform, and returns the created GameObject.

`prefab.inspect_instance` accepts `gameObjectId`, optional `targetSession` plus session selectors, `maxSamples`, and `includeSerialized`. It serializes the live GameObject and returns whether it is a prefab instance, its prefab path, prefab asset metadata, patch counts/samples from `__PrefabInstancePatch`, and the `__PrefabIdToInstanceId` count. Set `includeSerialized: true` only when debugging raw prefab metadata, because scene serialization can be large.

## Physics Actions

`physics.inspect` accepts `gameObjectId`, optional `targetSession`, and session selectors. It returns Rigidbody summaries, collider summaries with shape-specific dimensions, trigger/static flags, joint summaries, and target read-back when the editor API exposes it.

`physics.raycast` accepts complete `from` and `to` vectors and optional `renderMeshes`. It returns hit state, hit positions, normal, GameObject/component/collider read-back, and distance/fraction details.

## Sound Actions

`sound.inspect` accepts `gameObjectId`, optional `targetSession`, and session selectors. It returns all `SoundPointComponent` instances on the GameObject with sound event metadata, play-on-start, repeat, force-2D, volume, and pitch read-back.

`sound.preview` accepts `eventPath`, optional `position`, and optional `fadeIn`. It starts the sound through s&box and returns `SoundHandle` read-back such as validity, playing/stopped state, name, volume, pitch, and position.

## Batch Actions

`scene.batch` runs a bounded list of existing bridge actions. It is meant for common create/configure/verify flows, not arbitrary code execution.

Example:

```json
{
  "operations": [
    {
      "key": "root",
      "action": "gameobject.create",
      "payload": { "name": "Arena Root" }
    },
    {
      "key": "child",
      "action": "gameobject.create",
      "payload": {
        "name": "Arena Mesh",
        "parentId": { "$ref": "root.verified.id" }
      }
    },
    {
      "key": "renderer",
      "action": "component.add",
      "payload": {
        "gameObjectId": "$child.verified.id",
        "type": "ModelRenderer"
      }
    }
  ]
}
```

Each operation returns its own result under `verified.results`. A `key` stores that operation's result for later references. References can be object-form `{ "$ref": "alias.verified.id" }` or string-form `"$alias.verified.id"`. Batches stop on the first failure by default; set `stopOnError: false` to collect later failures too.

## Feedback Actions

Target-session-aware read actions accept `targetSession: "active" | "editor" | "playing" | "runtime" | "game"` and optional `sessionId`, `sessionIndex`, `sessionPath`, or `sessionScene`. Use `targetSession: "runtime"` to inspect the live `GameSession` while playing. Supported actions include `editor.play_state`, `editor.feedback`, `scene.summary`, `scene.hierarchy`, `scene.find`, `scene.details`, `gameobject.get`, component read actions, and `visual.capture_camera`.

`editor.play_state` reads active play mode state by default. With `targetSession: "runtime"` it resolves the live `GameSession` instead of assuming the active scene tab is the runtime scene.

`editor.play` and `editor.stop` request play-mode transitions and return a play-state read-back. They accept the same session selectors as other target-session-aware reads; by default they resolve to the editor session so an active runtime tab controls its parent editor scene. `editor.stop` also accepts `stopAll: true` to stop every currently playing editor session. The response includes `expectedIsPlaying` and `transitionPending` because s&box may settle a play/stop transition on a later editor frame. Agents should follow with `editor.play_state` when `transitionPending` is true.

`editor.compile_status` returns compile groups observed from s&box `compile.started` events. If no compile event has been observed since the bridge loaded, it returns an explicit zero-group state rather than claiming success or failure.

`editor.logs` tails `sbox-dev.log`. The raw line is authoritative; the returned `level` is an inferred filter helper.

`editor.logs` and `editor.feedback` accept `afterIndex`. Use `verified.logs.nextAfterIndex` from a baseline response as the next cursor to avoid treating old log lines as current failures.

`editor.feedback` combines play state, compile status, and recent logs. It accepts the same `targetSession`, `maxDiagnostics`, `maxLines`, `afterIndex`, `contains`, and `level` payload fields used by the individual play-state/compile/log actions.

The MCP `editor` tool also exposes `wait_compile`, `wait_runtime`, and `wait_stopped`. These are MCP-side polling helpers over existing bridge actions, not raw file-IPC bridge commands; direct protocol callers should poll `editor.compile_status`, `editor.play_state`/`scene.summary`, or `editor.tabs` with the same conditions.

`runtime.list_test_actions` resolves a target session, defaulting to `runtime`, and lists components that expose the Agent Bridge runtime test-action protocol. Components can expose a method protocol (`AgentBridgeRunTestAction` / `AgentBridgeTestAction`) when reflection supports it, or the verified property protocol:

- `AgentBridgeTestActions`: readable string of action names separated by `|`, comma, whitespace, or newlines.
- `AgentBridgeTestPayloadJson`: writable string payload.
- `AgentBridgeTestAction`: writable string action; the setter should synchronously execute the action.
- `AgentBridgeTestResultJson`: readable JSON string result.

`runtime.run_test_action` accepts `testAction`, optional `payload`, and optional `componentId`, `gameObjectId`, or `componentType` selectors. It invokes the selected runtime component and returns the invocation mode, selected component, result JSON, and parsed result when possible.

For property-protocol components, make the `AgentBridgeTestAction` setter ignore null/empty/whitespace values. s&box scene deserialization can set serialized string properties during scene cloning, and empty action values should not execute test logic or throw. Bridge-side errors from property-protocol invocation include the component type and requested action when the underlying setter throws.

## File Handoff

Writers should create request and response files through an atomic same-directory rename:

1. Write the JSON body to a hidden/temp file in the target directory.
2. Rename it to `request-{id}.json` or `response-{id}.json` only after the write handle is closed.

The editor bridge ignores locked request files and will retry them on a later pump. This keeps Windows file-lock races from turning into false command failures.
