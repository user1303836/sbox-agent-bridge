# Testing Strategy

This project has two very different test surfaces:

1. Normal code that can run in CI.
2. Editor bridge behavior that requires a live s&box editor.

Both matter. They should be tracked separately.

## CI-Covered Checks

GitHub Actions currently verifies:

- MCP server dependencies install with `npm ci`.
- TypeScript typecheck passes with `npm run check`.
- MCP server builds with `npm run build`.
- JSON metadata and `.sbproj` files parse as valid JSON.

These checks catch broken MCP code and malformed metadata. They do not prove that the editor bridge compiles in s&box.

## Local MCP Checks

```bash
cd mcp-server
npm ci
npm run check
npm run build
```

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
