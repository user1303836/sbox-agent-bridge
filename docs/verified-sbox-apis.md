# Verified s&box APIs

This file records the local API/doc facts used for the POC scaffold.

## Official Docs

- s&box games/addons are C#, with fast hotload.
- Scenes are made from `Scene`, `GameObject`, and `Component`.
- Editor projects are created with an `editor` folder and can access editor tooling and game code.
- Editor projects are not limited by the normal runtime API whitelist.
- Normal runtime game code is whitelisted; editor bridge code should not be normal gameplay code.
- Editor widgets can be docked with `[Dock("Editor", "...", "...")]`.
- Editor events can be handled with `[Event("...")]`; `tool.frame` runs every editor frame.
- Scene mutations should use `SceneEditorSession.Active.UndoScope(...)`.

Docs:

- https://sbox.game/dev/doc/
- https://sbox.game/dev/doc/editor/editor-project/
- https://sbox.game/dev/doc/editor/editor-widgets/
- https://sbox.game/dev/doc/editor/editor-events/
- https://sbox.game/dev/doc/editor/undo-system/
- https://sbox.game/dev/doc/code/code-basics/api-whitelist/

## Local API Schema Queries

Queried from `research_raw_api_schema.json` via `scripts/sbox_api_lookup.py`.

`Editor.SceneEditorSession`:

- `Active`
- `Scene`
- `Selection`
- `GetSelection()`
- `PushUndoSelection()`
- `IsPlaying`
- `SetPlaying(Scene scene)`
- `StopPlaying()`
- `Save(bool saveAs)`
- `FrameTo(BBox box)`
- `UndoScope(string name)`
- `UndoSystem`

`Sandbox.GameTask`:

- `MainThread()`
- `RunInThreadAsync(...)`
- delay/yield helpers

`Editor.DockAttribute`:

- constructor `(string target, string name, string icon)`

`Sandbox.EventAttribute`:

- constructor `(string eventName)`
- `Priority`

`Sandbox.GameObject`:

- `Id`
- `Name`
- `Enabled`
- `Active`
- `Parent`
- `Children`
- `LocalPosition`
- `LocalRotation`
- `LocalScale`
- `WorldPosition`
- `WorldRotation`
- `WorldScale`
- `Components`
- `MakeNameUnique()`
- `SetParent(GameObject value, bool keepWorldPosition)`
- `Destroy()`
- `GetBounds()`

`Sandbox.Scene`:

- `Directory`
- `CreateObject(bool enabled)`
- `ProcessDeletes()`

`Sandbox.GameObjectDirectory`:

- `FindByGuid(Guid guid)`
- `FindComponentByGuid(Guid guid)`

`Sandbox.SelectionSystem`:

- `Clear()`
- `Add(object obj)`
- `Set(object obj)`
- `Remove(object obj)`
- `Count`

`Rotation`:

- `From(float pitch, float yaw, float roll)`
- `Angles()`
- `x`, `y`, `z`, `w`

`BBox`:

- `Center`
- `Size`

`Sandbox.Helpers.UndoSystem`:

- `Undo()`
- `Redo()`

`Sandbox.ComponentList`:

- `GetAll()`
- `Create(TypeDescription type, bool startEnabled)`
- `Get(Guid id)`

`Sandbox.Game`:

- `TypeLibrary`

`Sandbox.Internal.TypeLibrary`:

- `GetTypes(Type type)`
- `GetType(Type type)`

`Sandbox.TypeDescription`:

- `Name`
- `FullName`
- `Title`
- `Description`
- `Group`
- `Icon`
- `Properties`

`Sandbox.PropertyDescription`:

- `Name`
- `Title`
- `Description`
- `Group`
- `PropertyType`
- `CanRead`
- `CanWrite`
- `ReadOnly`
- `IsIndexer`
- `GetValue(object obj)`

`ISceneUndoScope`:

- `WithGameObjectCreations()`
- `WithGameObjectChanges(...)`
- `WithComponentCreations()`
- `Push()`
