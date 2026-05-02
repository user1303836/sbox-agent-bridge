# Testing Strategy

This project has two very different test surfaces:

1. Normal code that can run in CI.
2. Editor bridge behavior that requires a live s&box editor.

Both matter. They should be tracked separately.

## CI-Covered Checks

GitHub Actions currently verifies:

- MCP server dependencies install with `npm ci`.
- TypeScript typecheck passes with `npm run check`.
- Bridge-client file IPC and MCP wait-helper tests pass with `npm test`.
- MCP server builds with `npm run build`.
- JSON metadata and `.sbproj` files parse as valid JSON.

These checks catch broken MCP code, malformed metadata, and regressions in the request/response file-IPC client. They do not prove that the editor bridge compiles in s&box.

## Local MCP Checks

```bash
cd mcp-server
npm ci
npm run check
npm test
npm run build
```

On Windows machines where `npm` is installed but not on PATH, direct Node execution is a usable fallback:

```powershell
& 'C:\path\to\node.exe' .\node_modules\typescript\bin\tsc -p tsconfig.json --noEmit
& 'C:\path\to\node.exe' .\node_modules\typescript\bin\tsc -p tsconfig.json
& 'C:\path\to\node.exe' .\node_modules\tsx\dist\cli.mjs --test .\test\bridge-client.test.ts .\test\wait-helpers.test.ts
```

## Opt-In Live Smoke Script

When a s&box editor project is open and the bridge editor library has compiled/loaded:

```bash
cd mcp-server
npm run smoke:live
```

This script uses the same file IPC path as the MCP server. It reads editor feedback, starts/stops play mode when the editor is not already playing, checks save-state reporting, creates temporary GameObjects, verifies the core scene-editing actions, runs a small `scene.batch`, inspects available component types and property schema metadata, validates candidate property values without mutation, checks visual/spatial feedback with `asset.inspect_model` and `visual.capture_camera`, verifies orientation override storage plus `gameobject.place_asset`, mutates `AgentBridgeMutationFixture` when it is addable by the editor, reads a component from the active scene when one exists, and then attempts to clean up the temporary objects.

For external-tester MVP readiness:

```bash
cd mcp-server
npm run smoke:mvp-suite
```

This suite is the preferred external-tester gate. It first creates a saved scene through `smoke:bootstrap`, then runs the focused MVP, asset/material, physics, sound, prefab, and capability-gap smokes against that scene. It avoids assuming `scenes/minimal.scene` exists or is the intended test target.

For a narrower single-smoke pass against an existing saved scene:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_MVP_SCENE=scenes/agent_bridge/smoke/mvp_suite.scene SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:mvp
```

This smoke verifies `bridge.doctor`, compile wait, `editor.recover_scene`, scene read, GameObject creation, model/material assignment, physics inspection, sound inspection, prefab instance inspection, runtime-targeted model preview, play/stop settle, and cleanup.

For clean-room scene bootstrap coverage:

```bash
cd mcp-server
npm run smoke:bootstrap
```

This focused smoke stops stale play sessions, waits for compile, reads `editor.project_info`, creates a blank scene with `editor.new_scene`, creates a marker GameObject, saves the scene through `editor.save_scene_as`, reopens the saved scene, verifies the marker survived reload, and restores the previously active sourced scene when one existed.

For clean-room gameplay walkthrough coverage:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_BOXING_SCENE=scenes/minimal.scene SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run walkthrough:boxing
```

This walkthrough installs a reusable `BoxingDemoController` fixture through `script.create`, waits for compile, recovers the target scene, creates an isolated controller object, adds/configures the local component by exact type name, saves the scene, enters play mode, verifies runtime test actions for a boxing loop, exercises jab/block/dodge/knockdown/TKO/decision paths, captures the generated broadcast camera by GameObject id, stops play mode, and reports weak spots found during the run.

For closing implemented-but-unverified capability gaps:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:capability-gaps
```

This focused smoke creates, edits, compiles, deletes, and verifies removal of a scratch C# script using new compile sequence waits. It also creates a deliberately invalid scratch script to verify compile diagnostics, deletes it, waits for recovery to zero errors, then uses temporary scene objects to verify `SkinnedModelRenderer`, `CitizenAnimationHelper`, `ParticleEffect`, `ParticleConeEmitter`, `ParticleSpriteRenderer`, and `ParticleLightRenderer` add/configure/read-back flows.

For runtime feedback and ARPG testbed verification:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:runtime
```

This focused smoke first calls `editor.stop` with `stopAll: true`, waits for `editor.wait_stopped`, waits for `editor.wait_compile`, opens `scenes/minimal.scene`, enters play mode, waits for `editor.wait_runtime`, lists ARPG runtime test actions, invokes deterministic ARPG actions for UI state, inventory open, damage, and restore, then stops and waits for `editor.wait_stopped` again. It intentionally verifies bridge/runtime feedback rather than OS-level input focus.

For asset/material preview verification:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:assets
```

This focused smoke creates a project material, inspects its `.vmat` source properties, mutates `g_vColorTint` and `TextureColor`, enters play mode, waits for runtime readiness, captures `asset.preview_model` with `targetSession: runtime`, verifies the PNG is nonblack, then stops and waits for `editor.wait_stopped`.

For prefab instance metadata verification:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:prefabs
```

This focused smoke creates a simple model-backed prefab, binds the source GameObject to it, reloads prefab info, instantiates it, inspects prefab instance metadata, verifies a nonempty prefab id map, mutates the instance transform, verifies name/position/rotation patch samples, and removes the temporary scene objects.

For physics verification:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:physics
```

This focused smoke creates temporary physics objects, adds a Rigidbody, box collider, and fixed joint, verifies `physics.inspect` read-back for body/collider/joint fields, raycasts through the temporary collider, then removes the temporary scene objects.

For sound verification:

```bash
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:sounds
```

This focused smoke creates a project sound event, inspects it, assigns it to a temporary `SoundPointComponent`, verifies `sound.inspect` read-back for component settings, previews the event, checks for a valid playing handle, then removes the temporary scene object.

Useful environment variables:

- `SBOX_AGENT_BRIDGE_IPC`: override the bridge IPC root. Set it for both the MCP server and the s&box editor process when isolating a fresh-project or multi-editor walkthrough.
- `SBOX_AGENT_BRIDGE_TIMEOUT_MS`: override command timeout.
- `SBOX_AGENT_BRIDGE_BOOTSTRAP_SCENE`: scene path written by `smoke:bootstrap`; defaults to `scenes/agent_bridge/smoke/bootstrap_smoke.scene`.
- `SBOX_AGENT_BRIDGE_BOOTSTRAP_SCENE_NAME`: scene name used by `smoke:bootstrap`.
- `SBOX_AGENT_BRIDGE_BOOTSTRAP_MARKER`: marker GameObject name used by `smoke:bootstrap`.
- `SBOX_AGENT_BRIDGE_BOOTSTRAP_RESTORE=0`: leave the bootstrap scene active instead of restoring the previously active sourced scene.
- `SBOX_AGENT_BRIDGE_MVP_SUITE_SCENE`: scene path created and reused by `smoke:mvp-suite`; defaults to `scenes/agent_bridge/smoke/mvp_suite.scene`.
- `SBOX_AGENT_BRIDGE_BOXING_SCENE`: scene path used by the boxing clean-room walkthrough; defaults to `SBOX_AGENT_BRIDGE_RUNTIME_SCENE` or `scenes/minimal.scene`.
- `SBOX_AGENT_BRIDGE_BOXING_CONTROLLER_NAME`: override the scene object name used by the boxing walkthrough.
- `SBOX_AGENT_BRIDGE_SMOKE_PREFIX`: override temporary object name prefix.
- `SBOX_AGENT_BRIDGE_SMOKE_KEEP_OBJECTS=1`: leave smoke-test objects in the scene for inspection.
- `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1`: fail if `AgentBridgeMutationFixture` cannot be added and mutated. The fixture may be addable by exact type name even when it is not visible to `component.list_types`.
- `SBOX_AGENT_BRIDGE_RUNTIME_SCENE`: override the runtime feedback smoke scene path; defaults to `scenes/minimal.scene`.
- `SBOX_AGENT_BRIDGE_CAPABILITY_SMOKE_SCRIPT`: override the scratch script path used by `smoke:capability-gaps`; defaults to `AgentBridgeScratch/CapabilityGapSmokeFixture.cs`.
- `SBOX_AGENT_BRIDGE_CITIZEN_MODEL`: override the citizen model path used by `smoke:capability-gaps`; defaults to `models/citizen/citizen.vmdl`.
- `SBOX_AGENT_BRIDGE_MVP_SCENE`: scene path used by the MVP smoke. If omitted, the smoke asks `editor.recover_scene` to infer a sourced open scene.
- `SBOX_AGENT_BRIDGE_MVP_MODEL`: model path used by the MVP smoke; defaults to `models/dev/box.vmdl`.
- `SBOX_AGENT_BRIDGE_MVP_MATERIAL`: material path written by the MVP smoke; defaults to `materials/agent_bridge/smoke/mvp_smoke.vmat`.
- `SBOX_AGENT_BRIDGE_MVP_PREFAB`: prefab path written by the MVP smoke; defaults to `prefabs/agent_bridge/smoke/mvp_smoke.prefab`.
- `SBOX_AGENT_BRIDGE_MVP_SOUND_EVENT`: sound event path written by the MVP smoke; defaults to `sounds/agent_bridge/smoke/mvp_smoke.sound`.
- `SBOX_AGENT_BRIDGE_MVP_SOUND_FILE`: sound file used by the MVP smoke; defaults to `sounds/ambience/cave-loop.vsnd`.
- `SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1`: allow the runtime feedback smoke to recover a stale unsaved test scene with `editor.open_scene forceReload`.
- `SBOX_AGENT_BRIDGE_ASSET_SMOKE_MATERIAL`: override the asset/material smoke material path; defaults to `materials/agent_bridge/smoke/asset_material_smoke.vmat`.
- `SBOX_AGENT_BRIDGE_ASSET_SMOKE_MODEL`: override the asset/material smoke model path; defaults to `models/dev/box.vmdl`.
- `SBOX_AGENT_BRIDGE_PREFAB_SMOKE_PATH`: override the prefab smoke prefab path; defaults to `prefabs/agent_bridge/smoke/prefab_instance_smoke.prefab`.
- `SBOX_AGENT_BRIDGE_PREFAB_SMOKE_MODEL`: override the prefab smoke model path; defaults to `models/dev/box.vmdl`.
- `SBOX_AGENT_BRIDGE_SOUND_SMOKE_EVENT`: override the sound smoke event path; defaults to `sounds/agent_bridge/smoke/sound_smoke.sound`.
- `SBOX_AGENT_BRIDGE_SOUND_SMOKE_FILE`: override the sound file used by the sound smoke; defaults to `sounds/ambience/cave-loop.vsnd`.

## Live Editor Smoke Checks

Use these when bridge code changes.

To create a fresh Minimal Game project from the local s&box template before opening it in the editor:

```powershell
.\scripts\create-minimal-sbox-project.ps1 -ProjectPath 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh' -Title 'Agent Bridge MVP Fresh'
.\scripts\install-editor-bridge.ps1 -ProjectPath 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh'
```

Open the generated `.sbproj` in s&box, wait for compile, then run `npm run smoke:mvp-suite`.

1. Copy `editor/` into a test project:

```text
YourSboxProject/Libraries/sbox_agent_bridge
```

2. Open the project in s&box and let it compile.
3. Optionally open the **Agent Bridge** dock to view status and controls.
4. Confirm the bridge is running through the dock or a `bridge.status` request.
5. Send a direct request:

```json
{
  "id": "manual-test",
  "action": "bridge.status",
  "payload": {}
}
```

6. Confirm a response appears under `%TEMP%/sbox-agent-bridge/responses`.
7. Test one read action such as `scene.summary`.
8. Test one mutation such as `gameobject.create`.
9. Verify the mutation through a separate read action such as `scene.find`.
10. Confirm the mutation is visible and undoable in the editor.

For core scene editing changes, also verify this direct file-IPC chain:

1. `gameobject.create`
2. `gameobject.create` with `parentId`
3. `gameobject.rename`
4. `gameobject.set_transform`
5. `gameobject.set_enabled` false, then true
6. `editor.set_selection`
7. `editor.get_selection`
8. `scene.details`
9. `gameobject.get`

For save-scene changes, verify:

1. `editor.save_scene` with `dryRun: true` returns before/after state without attempting a write.
2. An untitled scene returns `saveAttempted: false`, `saveVerified: false`, and a clear `skippedReason` when called without `saveAs`.
3. A scene with a source path returns `saveAttempted: true`; after a successful save, `after.hasUnsavedChanges` should be false and `saveVerified` should be true.

For scene bootstrap changes, verify:

1. `editor.project_info` returns active project paths and `bridgeInstalled: true`.
2. `editor.new_scene` creates an unsourced editor scene and refuses to discard dirty work unless `discardUnsaved:true` is supplied.
3. `editor.save_scene_as` writes a supplied `.scene` path with `overwrite:true`, registers/compiles it, and can activate the saved scene with `activateAfterSave:true`.
4. `editor.open_scene` can force-reload the saved scene.
5. `scene.find` can find an object created before the save/reload cycle.

For batch scene changes, verify:

1. `scene.batch` can create a root GameObject and a child GameObject using `{ "$ref": "root.verified.id" }`.
2. Later batch operations can use both object-form refs and string refs such as `"$renderer.verified.component.id"`.
3. The batch can add `ModelRenderer`, set `Model` and `MaterialOverride`, and read `scene.details` for the child.
4. A failed operation is captured in `verified.results`; with default `stopOnError`, later operations are skipped.

For extended core scene editing changes, verify the non-destructive path first:

1. `gameobject.duplicate`
2. `gameobject.reparent` to another GameObject
3. `gameobject.reparent` back to scene root
4. `editor.frame_object`
5. `editor.undo`
6. `editor.redo`

In a fresh editor session, separately reverify the destructive path before relying on it:

1. `gameobject.destroy`
2. `editor.undo`
3. `gameobject.get` on the restored object
4. `editor.redo`
5. `scene.find` to confirm the object is gone again

For component discovery changes, verify:

1. `component.list_types`
2. `component.list_on_gameobject`
3. `component.get`
4. `component.get_properties`
5. `component.get_properties` includes `metadata.schema` with supported kind, accepted JSON shapes, enum values, and reference targets where applicable

For component mutation changes, verify:

1. `component.add`
2. `component.set_enabled` false, then true
3. `component.validate_property` accepts a valid value and rejects an invalid value without mutation
4. `component.set_property` with `dryRun: true` converts a value without mutation
5. `component.set_property` through `AgentBridgeMutationFixture` for string, bool, int/uint/long, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `Model`, `Material`, `Texture`, `GameObject`, and `Component`
6. `component.remove`
7. `component.get` fails after removal
8. `editor.undo` restores the removed component
9. `editor.redo` removes it again

`AgentBridgeMutationFixture` lives at `editor/Code/TestFixtures/AgentBridgeMutationFixture.cs` in this repo and should be installed as `Libraries/sbox_agent_bridge/Code/TestFixtures/AgentBridgeMutationFixture.cs` in a test project. It is runtime/library code, not editor-only code, because the smoke test needs to add it to a scene GameObject by type name. If an already-open s&box project has not generated or hotloaded the library runtime project yet, reopen the project before running `npm run smoke:live`. Do not keep a second copy in the project's own `Code/` folder; duplicate component class definitions can break cold compilation and prevent the Agent Bridge dock from loading. Some editor sessions may not expose local game components through `Game.TypeLibrary`; the bridge now has an exact-type-name fallback for `component.add`, but `component.list_types` may still not list the fixture. Use `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` when you specifically need fixture-backed component mutation coverage.

For resource-backed component property changes, also verify at least one built-in component with known asset paths:

1. Add `ModelRenderer` to a temporary GameObject.
2. Confirm `component.get_properties` reports `Model` and `MaterialOverride` as `resourceReference` schema entries.
3. Validate and set `ModelRenderer.Model` with `models/dev/plane_blend.vmdl`.
4. Validate and set `ModelRenderer.MaterialOverride` with `materials/dev/reflectivity_30.vmat`.
5. Confirm read-back includes `path`, `name`, `id`, and `isValid: true`.

For editor feedback-loop changes, verify:

1. `editor.play_state` returns the active scene name and current play state.
2. `editor.logs` returns a `sbox-dev.log` source, raw log entries, and no read error.
3. `editor.compile_status` returns a compile-event source and either observed compile groups or an explicit no-observed-groups note.
4. `editor.feedback` agrees with the individual play/log/compile actions.
5. If the editor is not already playing, `editor.play` transitions to `isPlaying: true`.
6. `editor.stop` transitions back to `isPlaying: false`.

For visual/spatial feedback changes, verify:

1. `asset.search` finds a known model asset.
2. `asset.inspect_model` returns model/render/physics bounds, material slots, orientation candidates, and ground offsets for that model.
3. `asset.set_orientation_override` stores a base rotation and either a supplied or calculated `groundOffsetZ` under `Assets/agent_bridge/orientation_overrides.json`.
4. `asset.get_orientation_override` reads the same model path back with `found:true`.
5. `gameobject.place_asset` with `requireOrientationOverride:true` creates a renderer-backed object and reports `orientationSource: stored-override`.
6. When `alignToGround:true`, verify `finalPosition.z` equals requested ground Z plus `calculatedGroundOffsetZ`.
7. `visual.capture_camera` captures the active camera to a PNG path under `%TEMP%/sbox-agent-bridge/captures`.
8. The capture response includes camera metadata and luminance stats.
9. Open or inspect the capture when visual correctness matters; luminance stats help flag visibility problems, but they do not prove composition or semantic orientation.

This is intentionally local-only for now. A CI runner does not have a live s&box editor client, so CI should not claim this coverage until the project has a reliable headless/editor automation story.

Current live-test note: direct feedback-loop actions are verified in the local editor, including compile status with zero errors. `smoke:capability-gaps` passed locally with `SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1`. The legacy full smoke remains partial because it has older fixture assumptions; prefer the focused smokes plus `smoke:mvp` for current verification.

## MCP End-To-End Checks

Once the MCP server is configured in a client:

1. Call `editor` with `action=status`.
2. Call `scene` with `action=summary`.
3. Call `gameobject` with `action=create`.
4. Call `scene` with `action=find` for the created object name.

This proves the complete path:

```text
MCP client -> MCP server -> file IPC -> s&box editor bridge -> live editor state
```

## Regression Rule

Any newly verified bridge behavior should update [capability-matrix.md](capability-matrix.md). Any bridge behavior found broken should be marked `Blocked` or downgraded from `Verified` until fixed.
