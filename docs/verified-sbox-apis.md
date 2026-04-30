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

## Local API Schema And Source Checks

Core entries were queried from `research_raw_api_schema.json` via `scripts/sbox_api_lookup.py`. Later asset, sound, physics, prefab, and editor-tab entries were also confirmed through local source inspection, editor-library compilation, and live bridge use.

`Editor.SceneEditorSession`:

- `Active`
- `All`
- `Scene`
- `GameSession`
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
- `Serialize(...)`
- `Deserialize(...)`
- `SetPrefabSource(string path)`

`Sandbox.Scene`:

- `Directory`
- `Source`
- `CreateObject(bool enabled)`
- `ProcessDeletes()`
- `Trace`

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

`Sandbox.Component`:

- `Id`
- `Enabled`
- `Active`
- `IsValid`
- `Destroy()`

`Sandbox.Game`:

- `TypeLibrary`

`Sandbox.Internal.TypeLibrary`:

- `GetTypes(Type type)`
- `GetType(Type type)`

`Sandbox.TypeDescription`:

- `TargetType`
- `Name`
- `FullName`
- `Title`
- `Description`
- `Group`
- `Icon`
- `Properties`
- `IsEnum`

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
- `IsStatic`
- `IsPublic`
- `HasAttribute(...)`
- `GetValue(object obj)`
- `SetValue(object obj, object value)`

`Sandbox.Resource`:

- `ResourcePath`
- `ResourceName`
- `ResourceId`
- `IsValid`

`Sandbox.ResourceLibrary`:

- `Get<T>(string path)`

`Editor.AssetSystem`:

- `All`
- `FindByPath(string path)`
- `RegisterFile(string path)`
- `CreateResource(string type, string path)`

`Editor.Asset`:

- `Name`
- `Path`
- `RelativePath`
- `AbsolutePath`
- `AssetType`
- `IsDeleted`
- `IsCloud`
- `IsCompiled`
- `IsCompiledAndUpToDate`
- `IsCompileFailed`
- `HasSourceFile`
- `HasCompiledFile`
- `HasUnsavedChanges`
- `GetSourceFile(bool)`
- `GetCompiledFile(bool)`
- `LoadResource<T>()`
- `SaveToDisk(object resource)`
- `Compile(bool force)`

`Editor.Project`:

- `Current`
- `GetAssetsPath()`

`Sandbox.Model`:

- `Load(string filename)`

`Sandbox.Material`:

- `Load(string filename)`
- `Set(...)`

`Sandbox.Texture`:

- `Load(string filename, bool complain = true)`

`Sandbox.ModelRenderer`:

- `Model`
- `MaterialOverride`

`Sandbox.SoundEvent`:

- constructor from sound-file path and volume
- `Volume`
- `Pitch`
- `Decibels`
- `SelectionMode`
- `DistanceAttenuation`
- `Distance`
- `Occlusion`
- `Reflections`
- `Sounds`

`Sandbox.SoundFile`:

- `Load(string path)`
- metadata such as `Duration`, `Channels`, and `Rate` must be read defensively because some built-in handles throw before load.

`Sandbox.SoundPointComponent`:

- `SoundEvent`
- `PlayOnStart`
- `Repeat`
- `Force2d`
- `Volume`
- `Pitch`

`Sandbox.Sound` / `Sandbox.SoundHandle`:

- `Sound.Play(...)`
- handle read-back: `IsValid`, `IsPlaying`, `IsStopped`, `Name`, `Volume`, `Pitch`, `Position`

`Sandbox.PrefabFile`:

- `Load(string path)`
- `RootObject`
- `ShowInMenu`
- `MenuPath`
- `MenuIcon`

`Sandbox.Rigidbody`:

- `Gravity`
- `MotionEnabled`
- `MassOverride`

`Sandbox.Collider` and concrete collider components:

- `Static`
- `IsTrigger`
- `BoxCollider.Scale`
- `BoxCollider.Center`
- `SphereCollider.Radius`
- `SphereCollider.Center`
- `CapsuleCollider.Radius`
- `CapsuleCollider.Start`
- `CapsuleCollider.End`

`Sandbox.Joint` and concrete joint components:

- `FixedJoint`
- `HingeJoint`
- `SpringJoint`
- `BallJoint`
- `SliderJoint`
- `EnableCollision`
- `Object2` is read-only through the verified surface, so bridge target assignment is not wired yet.

`Sandbox.SceneTrace`:

- `Scene.Trace.Ray(from, to)`
- `UseRenderMeshes(bool)`
- `Run()`

`ISceneUndoScope`:

- `WithGameObjectCreations()`
- `WithGameObjectChanges(...)`
- `WithComponentCreations()`
- `WithComponentDestructions(Component component)`
- `WithComponentChanges(Component component)`
- `Push()`

`Sandbox.CompileGroup`:

- `Name`
- `Compilers`
- `NeedsBuild`
- `IsBuilding`
- `BuildResult`

`Sandbox.Compiler`:

- `Name`
- `IsBuilding`
- `NeedsBuild`
- `BuildSuccess`
- `BuildResult`
- `Diagnostics`

`Sandbox.CompilerOutput`:

- `Successful`
- `Diagnostics`
- `Exception`

`Sandbox.LogEvent`:

- `Level`
- `Logger`
- `Message`
- `Exception`
- `Time`

## Local Source Checks

Local installed s&box source in `C:\Program Files (x86)\Steam\steamapps\common\sbox` was used to confirm:

- The scene view responds to `scene.play` / `scene.stop` events and swaps view mode based on `SceneEditorSession.IsPlaying`.
- The editor compile toast listens to `compile.started` and reads `CompileGroup.Compilers[*].Diagnostics` for Roslyn errors and warnings.
- The built-in console opens `Environment.CurrentDirectory + "/logs/"`, matching the bridge's `sbox-dev.log` tail source.
