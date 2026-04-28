# sbox-agent-bridge

Tiny proof-of-concept bridge for letting MCP-capable coding agents inspect and carefully mutate the s&box editor.

The project is intentionally small: one editor-side bridge library and one external MCP server. The bridge knows how to talk to the live editor. The MCP server knows how to talk to agents.

## Status

Experimental POC. The first target is a reliable local loop:

1. Install the editor bridge into a s&box project `Libraries/` folder.
2. Open the Agent Bridge dock in the s&box editor.
3. Run the MCP server from `mcp-server/`.
4. Ask an agent for editor status, context, scene summary, hierarchy, or a very small test mutation.

## Repository Layout

```text
editor/       s&box library/editor bridge source
mcp-server/   TypeScript MCP server that forwards tool calls to the bridge
schemas/      JSON schemas for bridge command/response envelopes
docs/         architecture, protocol, and verified s&box API notes
examples/     install and usage notes
```

## Design

The bridge uses file IPC for the first POC. It is less elegant than HTTP/SSE, but it is simple, debuggable, and avoids spending the first week arguing with transport details. The protocol is stable enough that a later HTTP/SSE transport can reuse the same command envelope.

Default IPC root:

```text
%TEMP%/sbox-agent-bridge/
  requests/
  responses/
```

## Initial Tool Surface

- `editor`: `status`, `context`
- `scene`: `summary`, `hierarchy`, `find`
- `gameobject`: `create`

Every mutation should return a read-back `verified` payload. If a mutation cannot verify its own result, callers should treat it as incomplete.

## Local Development

Install dependencies and build the MCP server:

```bash
cd mcp-server
npm install
npm run build
```

Example MCP config after building:

```json
{
  "mcpServers": {
    "sbox-agent-bridge": {
      "command": "node",
      "args": ["C:/path/to/sbox-agent-bridge/mcp-server/dist/index.js"]
    }
  }
}
```

See [examples/minimal-sbox-project-install.md](examples/minimal-sbox-project-install.md) for editor bridge installation.

## Grounding

This repo is grounded in the official s&box docs/API schema:

- Editor projects can access editor tools and game code and are not sandboxed.
- `SceneEditorSession.Active` exposes the active editor scene/session.
- Scene edits should run on the editor main thread and use undo scopes.
- Normal game code is whitelisted, so this bridge belongs in editor/library code, not runtime gameplay components.

See [docs/verified-sbox-apis.md](docs/verified-sbox-apis.md).
