# Testing Strategy

This project has two very different test surfaces:

1. Normal code that can run in CI.
2. Editor bridge behavior that requires a live s&box editor.

Both matter. They should be tracked separately.

## CI-Covered Checks

GitHub Actions currently verifies:

- MCP server dependencies install with `npm ci`.
- TypeScript typecheck passes with `npm run check`.
- Bridge-client file IPC tests pass with `npm test`.
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

## Opt-In Live Smoke Script

When a s&box editor project is open and the **Agent Bridge** dock is running:

```bash
cd mcp-server
npm run smoke:live
```

This script uses the same file IPC path as the MCP server. It reads editor feedback, starts/stops play mode when the editor is not already playing, checks save-state reporting, creates temporary GameObjects, verifies the core scene-editing actions, runs a small `scene.batch`, inspects available component types and property schema metadata, validates candidate property values without mutation, mutates `AgentBridgeMutationFixture` when it is visible to the editor type library, reads a component from the active scene when one exists, and then cleans up the temporary objects.

Useful environment variables:

- `SBOX_AGENT_BRIDGE_IPC`: override the bridge IPC root.
- `SBOX_AGENT_BRIDGE_TIMEOUT_MS`: override command timeout.
- `SBOX_AGENT_BRIDGE_SMOKE_PREFIX`: override temporary object name prefix.
- `SBOX_AGENT_BRIDGE_SMOKE_KEEP_OBJECTS=1`: leave smoke-test objects in the scene for inspection.
- `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1`: fail if `AgentBridgeMutationFixture` is not visible to `component.list_types`.

## Live Editor Smoke Checks

Use these when bridge code changes.

1. Copy `editor/` into a test project:

```text
YourSboxProject/Libraries/sbox_agent_bridge
```

2. Open the project in s&box.
3. Open the **Agent Bridge** dock.
4. Confirm the dock says `Status: running`.
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

For batch scene changes, verify:

1. `scene.batch` can create a root GameObject and a child GameObject using `{ "$ref": "root.verified.id" }`.
2. Later batch operations can use both object-form refs and string refs such as `"$renderer.verified.component.id"`.
3. The batch can add `ModelRenderer`, set `Model` and `MaterialOverride`, and read `scene.details` for the child.
4. A failed operation is captured in `verified.results`; with default `stopOnError`, later operations are skipped.

For extended core scene editing changes, verify:

1. `gameobject.duplicate`
2. `gameobject.reparent` to another GameObject
3. `gameobject.reparent` back to scene root
4. `editor.frame_object`
5. `gameobject.destroy`
6. `editor.undo`
7. `gameobject.get` on the restored object
8. `editor.redo`
9. `scene.find` to confirm the object is gone again

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

`AgentBridgeMutationFixture` lives at `editor/Code/TestFixtures/AgentBridgeMutationFixture.cs`. It is runtime/library code, not editor-only code, because the smoke test needs to add it to a scene GameObject by type name. If an already-open s&box project has not generated or hotloaded the library runtime project yet, copy the fixture into that test project's own `Code` folder or reopen the project before running `npm run smoke:live`. Some editor sessions may not expose local game components through `Game.TypeLibrary`; use `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` when you specifically need fixture-backed component mutation coverage.

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

This is intentionally local-only for now. A CI runner does not have a live s&box editor client, so CI should not claim this coverage until the project has a reliable headless/editor automation story.

Current live-test note: direct feedback-loop actions are verified in the local editor, including compile status with zero errors. The full smoke is currently blocked in this editor session by a native null reference while deleting GameObjects through the editor delete/undo path after play-mode testing. Reopen the s&box project and rerun `npm run smoke:live` before marking the full smoke green again.

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
