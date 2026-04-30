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
- `editor.context`
- `editor.tabs`
- `editor.activate_tab`
- `editor.open_scene`
- `editor.get_selection`
- `editor.set_selection`
- `editor.save_scene`
- `editor.undo`
- `editor.redo`
- `editor.frame_object`
- `editor.play_state`
- `editor.play`
- `editor.stop`
- `editor.logs`
- `editor.compile_status`
- `editor.feedback`
- `script.create`
- `script.edit`
- `script.delete`
- `asset.search`
- `asset.get_info`
- `asset.inspect_model`
- `asset.assign_model`
- `asset.assign_material`
- `asset.create_material`
- `asset.set_material_property`
- `visual.capture_camera`
- `sound.list`
- `sound.get_info`
- `sound.create_event`
- `sound.assign`
- `sound.preview`
- `physics.add_physics`
- `physics.add_collider`
- `physics.add_joint`
- `physics.raycast`
- `prefab.create`
- `prefab.list`
- `prefab.get_info`
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

`editor.open_scene` accepts `path`, optional `bringToFront`, and optional `forceReload`. Use `forceReload: true` only when the scene has no unsaved changes; it reloads an already-open sourced scene from disk and is useful after play-mode transitions leave the active editor session stale.

`editor.save_scene` returns before/after save state. `dryRun: true` reads save state without writing. Untitled scenes without a source path are guarded: the bridge returns `saveAttempted: false` and a `skippedReason` instead of opening a surprise save-as flow. When a save is attempted, `saveVerified` is true only if the after-state reports no unsaved changes.

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

`visual.capture_camera` accepts optional `cameraComponentId` or `gameObjectId`, plus `width`, `height`, and `name`. Without a camera id, it captures the enabled main camera, or the first enabled camera. The response includes a PNG path under `%TEMP%/sbox-agent-bridge/captures`, camera metadata, byte count, and luminance statistics:

- `average`: average relative luminance across the capture.
- `min` / `max`: darkest and brightest sampled pixel luminance.
- `darkPixelRatio`: fraction of pixels below the bridge's dark threshold.
- `brightPixelRatio`: fraction of pixels above the bridge's bright threshold.

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

`editor.play_state` reads active play mode state from `SceneEditorSession.Active`.

`editor.play` and `editor.stop` request play-mode transitions and return a play-state read-back. The response includes `expectedIsPlaying` and `transitionPending` because s&box may settle a play/stop transition on a later editor frame. Agents should follow with `editor.play_state` when `transitionPending` is true.

`editor.compile_status` returns compile groups observed from s&box `compile.started` events. If no compile event has been observed since the bridge loaded, it returns an explicit zero-group state rather than claiming success or failure.

`editor.logs` tails `sbox-dev.log`. The raw line is authoritative; the returned `level` is an inferred filter helper.

`editor.feedback` combines play state, compile status, and recent logs. It accepts the same `maxDiagnostics`, `maxLines`, `contains`, and `level` payload fields used by the individual compile/log actions.

## File Handoff

Writers should create request and response files through an atomic same-directory rename:

1. Write the JSON body to a hidden/temp file in the target directory.
2. Rename it to `request-{id}.json` or `response-{id}.json` only after the write handle is closed.

The editor bridge ignores locked request files and will retry them on a later pump. This keeps Windows file-lock races from turning into false command failures.
