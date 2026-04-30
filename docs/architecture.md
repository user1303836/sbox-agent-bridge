# Architecture

## Goal

Give agents a narrow, reliable way to inspect and operate the live s&box editor without guessing scene state or inventing API names.

## Components

```text
Agent/MCP client
  <-> MCP server over stdio
    <-> bridge command files in %TEMP%/sbox-agent-bridge
      <-> s&box editor bridge dock
        <-> SceneEditorSession.Active
```

## Why One Repo

The editor bridge and MCP server are coupled during the POC. A new bridge command needs a matching MCP tool, schema update, docs, and often a verification read-back. Keeping them together reduces drift.

The pieces can split later if the bridge becomes stable.

## Editor Bridge

The editor bridge lives in an s&box library under `editor/`. It is editor-only code, built around:

- a dock widget so the developer can see bridge status;
- an editor-frame pump that processes queued commands;
- a dispatcher that maps command names to handlers;
- small handler classes for editor, scene, component, and gameobject operations;
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

The POC uses file IPC:

- MCP server writes `requests/request-{id}.json`.
- Editor bridge reads the file on an editor frame.
- Editor bridge writes `responses/response-{id}.json`.
- MCP server polls until timeout.

HTTP/SSE can be added later behind the same `BridgeClient` interface.

## Feedback Sources

Editor feedback intentionally keeps sources separate:

- Play state comes from `SceneEditorSession.Active`.
- Compile status comes from observed s&box `compile.started` events and live `CompileGroup`/`Compiler` state.
- Recent logs come from `Environment.CurrentDirectory/logs/sbox-dev.log`.

The MCP-facing `editor.feedback` action combines those sources, but each nested payload still reports its own source and limitations.
