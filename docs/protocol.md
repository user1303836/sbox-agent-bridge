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
    "suggestion": "Open a scene in the s&box editor and reopen the Agent Bridge dock."
  }
}
```

## Actions

- `bridge.status`
- `editor.context`
- `editor.get_selection`
- `editor.set_selection`
- `editor.save_scene`
- `editor.undo`
- `editor.redo`
- `editor.frame_object`
- `scene.summary`
- `scene.hierarchy`
- `scene.find`
- `scene.details`
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

`component.set_property` also accepts `dryRun: true`. In dry-run mode, the bridge resolves the component/property and converts the input value, but does not call `PropertyDescription.SetValue`.

`component.validate_property` performs the same conversion check without mutation. Its verified payload includes:

- `property`: metadata and schema for the target property.
- `current`: current read-back value.
- `converted`: converted value that would be assigned.
- `mutationApplied: false`.
- `valid: true`.

Property metadata includes `typeConversionSupported`, `setPropertySupported`, and a `schema` block with kind, nullability, accepted JSON shapes, an example value, enum values, reference target, support status, and unsupported reason where applicable.

## File Handoff

Writers should create request and response files through an atomic same-directory rename:

1. Write the JSON body to a hidden/temp file in the target directory.
2. Rename it to `request-{id}.json` or `response-{id}.json` only after the write handle is closed.

The editor bridge ignores locked request files and will retry them on a later pump. This keeps Windows file-lock races from turning into false command failures.
