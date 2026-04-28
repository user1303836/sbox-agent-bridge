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

## Mutation Rule

Mutations should return a `verified` object read back from the editor after the operation. If `verified` is missing, the caller should assume the change may not have stuck.
