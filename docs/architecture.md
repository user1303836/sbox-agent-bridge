# Architecture

## Goal

Give agents a narrow, reliable way to inspect and operate the live s&box editor without guessing scene state or inventing API names.

## Components

```text
Agent/MCP client
  <-> MCP server over stdio
      <-> bridge command files in %TEMP%/sbox-agent-bridge
        <-> s&box editor bridge runtime/status dock
        <-> SceneEditorSession.Active / All / GameSession
```

## Why One Repo

The editor bridge and MCP server are coupled during rapid development. A new bridge command needs a matching MCP tool, schema update, docs, and often a verification read-back. Keeping them together reduces drift.

The pieces can split later if the bridge becomes stable.

## Editor Bridge

The editor bridge lives in an s&box library under `editor/`. It is editor-only code, built around:

- a dock widget so the developer can see bridge status and manually start/stop IPC;
- an editor-frame pump that auto-starts the runtime and processes queued commands once the editor assembly loads;
- a dispatcher that maps command names to handlers;
- small handler classes for editor, scene, GameObject, component, asset, visual, sound, physics, prefab, and script operations;
- a feedback state helper for compile-event snapshots and recent editor logs.

The bridge should keep mutation behavior conservative:

- identify objects by GUID when possible;
- wrap scene changes in `SceneEditorSession.Active.UndoScope(...)`;
- return `verified` state after edits;
- return structured errors with suggestions.

## MCP Server

The MCP server is an external TypeScript process. It exposes agent-facing tools and forwards commands to the bridge.

The server owns:

- MCP stdio transport;
- tool descriptions and input schemas;
- timeout/retry behavior for bridge requests;
- compact JSON result formatting.

## Transport

The current bridge uses file IPC:

- MCP server writes `requests/request-{id}.json`.
- Editor bridge reads the file on an editor frame.
- Editor bridge writes `responses/response-{id}.json`.
- MCP server polls until timeout.

The default IPC root is `%TEMP%/sbox-agent-bridge`. Set `SBOX_AGENT_BRIDGE_IPC` for both the s&box editor process and the MCP server when a fresh-project or multi-editor walkthrough needs an isolated bridge instance.

HTTP/SSE can be added later behind the same `BridgeClient` interface.

## Feedback Sources

Editor feedback intentionally keeps sources separate:

- Play state comes from the resolved `SceneEditorSession`; runtime-aware reads can target the live `GameSession`.
- Compile status comes from observed s&box `compile.started` events and live `CompileGroup`/`Compiler` state.
- Recent logs come from `Environment.CurrentDirectory/logs/sbox-dev.log`.

The MCP-facing `editor.feedback` action combines those sources, but each nested payload still reports its own source and limitations.
