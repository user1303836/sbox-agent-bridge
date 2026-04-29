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

This script uses the same file IPC path as the MCP server. It creates temporary GameObjects, verifies the core scene-editing actions, inspects available component types, mutates `AgentBridgeMutationFixture`, reads a component from the active scene when one exists, and then cleans up the temporary objects.

Useful environment variables:

- `SBOX_AGENT_BRIDGE_IPC`: override the bridge IPC root.
- `SBOX_AGENT_BRIDGE_TIMEOUT_MS`: override command timeout.
- `SBOX_AGENT_BRIDGE_SMOKE_PREFIX`: override temporary object name prefix.
- `SBOX_AGENT_BRIDGE_SMOKE_KEEP_OBJECTS=1`: leave smoke-test objects in the scene for inspection.

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
2. `gameobject.rename`
3. `gameobject.set_transform`
4. `gameobject.set_enabled` false, then true
5. `editor.set_selection`
6. `editor.get_selection`
7. `scene.details`
8. `gameobject.get`

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

For component mutation changes, verify:

1. `component.add`
2. `component.set_enabled` false, then true
3. `component.set_property` through `AgentBridgeMutationFixture` for string, bool, int/uint/long, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject`, and `Component`
4. `component.remove`
5. `component.get` fails after removal
6. `editor.undo` restores the removed component
7. `editor.redo` removes it again

`AgentBridgeMutationFixture` lives at `editor/Code/TestFixtures/AgentBridgeMutationFixture.cs`. It is runtime/library code, not editor-only code, because the smoke test needs to add it to a scene GameObject by type name. If an already-open s&box project has not generated or hotloaded the library runtime project yet, copy the fixture into that test project's own `Code` folder or reopen the project before running `npm run smoke:live`.

This is intentionally local-only for now. A CI runner does not have a live s&box editor client, so CI should not claim this coverage until the project has a reliable headless/editor automation story.

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
