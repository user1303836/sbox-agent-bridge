# s&box Agent Bridge

Experimental local editor bridge and MCP server for letting coding agents inspect and carefully operate the live s&box editor.

The goal is simple: an MCP-capable agent should be able to ask the editor what is actually in the current scene, make small undoable changes, and read back the result instead of guessing from files alone.

This repo contains both halves:

- `editor/`: an s&box library with an **Agent Bridge** editor dock.
- `mcp-server/`: a TypeScript MCP server that exposes agent-facing tools over stdio.

The project is intentionally provider-neutral. It is not built for one model or one MCP client.

## Current Status

This is an early proof of concept, but the local loop works.

As of 2026-04-29, the bridge has been verified against a minimal s&box project on Windows:

- The editor library compiles and the **Agent Bridge** dock appears in s&box.
- The dock listens through local file IPC at `%TEMP%/sbox-agent-bridge`.
- The MCP server can read editor status, active context, selection, scene summaries, hierarchy, object details, component lists, and component properties.
- GameObject mutations are undo-scoped and read back after the edit: create, rename, transform, enable/disable, reparent, duplicate, destroy.
- Component mutations are undo-scoped and read back after the edit: add, remove, enable/disable, and set property.
- `component.set_property` is live-smoked against `AgentBridgeMutationFixture` for string, bool, integer, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject` reference, and `Component` reference values.
- GitHub Actions runs metadata validation, typecheck, tests, and build for the MCP server.

Still experimental:

- The editor bridge must be installed into each s&box project that should expose live editor access.
- The s&box editor must be open and the Agent Bridge dock must be running.
- CI does not run a real s&box editor, so live editor behavior is verified with local smoke tests.
- `gameobject.duplicate` is currently shallow: it copies name, enabled state, transform, and parent, but not components or children.
- `component.set_property` does not yet support resource references or collection/list editing.

## Quick Start

### 1. Clone And Build The MCP Server

Requirements:

- s&box installed
- Node.js 20 or newer
- npm
- An MCP-capable client or coding agent

```powershell
git clone https://github.com/user1303836/sbox-agent-bridge.git
cd sbox-agent-bridge
cd mcp-server
npm install
npm run build
```

### 2. Install The Editor Bridge Into A s&box Project

Create or open a s&box project, then copy this repo's `editor/` folder into that project's `Libraries/sbox_agent_bridge/` folder.

From the repo root in PowerShell:

```powershell
$Project = 'C:\Users\you\Documents\s&box projects\YourProject'
New-Item -ItemType Directory -Force -Path "$Project\Libraries\sbox_agent_bridge" | Out-Null
Copy-Item -Path '.\editor\*' -Destination "$Project\Libraries\sbox_agent_bridge" -Recurse -Force
```

The installed project should look like this:

```text
YourProject/
  Libraries/
    sbox_agent_bridge/
      sbox_agent_bridge.sbproj
      Code/
        AgentBridgeMutationFixture.cs
      Editor/
        BridgeDock.cs
        BridgeRuntime.cs
        ...
```

Open the project in the s&box editor and let it compile. Then open:

```text
View -> Agent Bridge
```

Leave the dock open while using the MCP server. If the dock does not show `Status: running`, click **Start Bridge**.

### 3. Connect Your MCP Client

After `npm run build`, point your MCP client at the built server:

```json
{
  "mcpServers": {
    "sbox-agent-bridge": {
      "command": "node",
      "args": ["C:/absolute/path/to/sbox-agent-bridge/mcp-server/dist/index.js"]
    }
  }
}
```

The default IPC root is:

```text
%TEMP%/sbox-agent-bridge
```

If you need a custom IPC folder, set `SBOX_AGENT_BRIDGE_IPC` for the MCP server:

```json
{
  "mcpServers": {
    "sbox-agent-bridge": {
      "command": "node",
      "args": ["C:/absolute/path/to/sbox-agent-bridge/mcp-server/dist/index.js"],
      "env": {
        "SBOX_AGENT_BRIDGE_IPC": "C:/temp/my-sbox-agent-bridge"
      }
    }
  }
}
```

### 4. Try A Safe First Prompt

With s&box open and the Agent Bridge dock running, ask your agent something like:

```text
Use the sbox-agent-bridge MCP tools to check the editor bridge status, summarize the active scene, and list the current selection. Do not mutate the scene yet.
```

Then try a tiny mutation:

```text
Use the sbox-agent-bridge MCP tools to create one GameObject named Agent Bridge Test, verify that it exists, then undo the creation.
```

## MCP Tools

The MCP server exposes four compact tools. Each tool has an `action` field.

### `editor`

Editor/session operations:

- `status`
- `context`
- `get_selection`
- `set_selection`
- `save_scene`
- `undo`
- `redo`
- `frame_object`

### `scene`

Scene inspection:

- `summary`
- `hierarchy`
- `find`
- `details`

### `gameobject`

Small undoable GameObject operations:

- `get`
- `create`
- `rename`
- `set_transform`
- `set_enabled`
- `destroy`
- `duplicate`
- `reparent`

### `component`

Component discovery, inspection, and mutation:

- `list_types`
- `list_on_gameobject`
- `get`
- `get_properties`
- `add`
- `remove`
- `set_enabled`
- `set_property`

Every mutation should return a `verified` payload read back from the editor. If a mutation cannot verify its own result, callers should treat it as incomplete.

## Live Smoke Test

The MCP server includes an opt-in smoke test that talks to a real open s&box editor through the same file IPC path:

```powershell
cd mcp-server
npm run smoke:live
```

This test creates temporary GameObjects, verifies scene and GameObject actions, mutates `AgentBridgeMutationFixture`, checks component property readbacks, tests undo/redo, and cleans up after itself.

If the smoke test says `AgentBridgeMutationFixture` is not available, wait for s&box hotload or reopen the project. For an already-open project that has not generated the library runtime project yet, you can temporarily copy `editor/Code/AgentBridgeMutationFixture.cs` into that project's own `Code/` folder.

## Architecture

The first transport is local file IPC:

```text
Agent / MCP client
  <-> MCP server over stdio
    <-> request/response JSON files in %TEMP%/sbox-agent-bridge
      <-> Agent Bridge dock in the s&box editor
        <-> SceneEditorSession.Active
```

Request and response files use an atomic same-directory rename so Windows file-lock races do not become false command failures.

File IPC is not the final dream transport. It is the smallest useful transport for this POC: local, debuggable, and easy to inspect. A later HTTP/SSE or named-pipe transport can reuse the same bridge command envelope.

## Repository Layout

```text
editor/       s&box library/editor bridge source
mcp-server/   TypeScript MCP server that forwards tool calls to the bridge
schemas/      JSON schemas for bridge command/response envelopes
docs/         architecture, protocol, roadmap, testing, and API notes
examples/     install and usage notes
```

## Local Development

```powershell
cd mcp-server
npm install
npm run ci
```

Useful scripts:

- `npm run dev`: run the MCP server from TypeScript.
- `npm run build`: compile the MCP server to `dist/`.
- `npm run ci`: typecheck, unit test, and build.
- `npm run smoke:live`: run the live editor smoke test against an already-open bridge.

## Project Docs

- [Roadmap](docs/roadmap.md)
- [Capability Matrix](docs/capability-matrix.md)
- [Testing Strategy](docs/testing.md)
- [Architecture](docs/architecture.md)
- [Protocol](docs/protocol.md)
- [Verified s&box APIs](docs/verified-sbox-apis.md)
- [Prior Art](docs/prior-art.md)
- [Contributing](CONTRIBUTING.md)

## Grounding

This repo is grounded in the official s&box docs/API schema and local public source research:

- s&box editor projects can access editor tools and game code.
- `SceneEditorSession.Active` exposes the active editor scene/session.
- Scene edits should run on the editor main thread and use undo scopes.
- Normal game code is sandboxed/restricted, so the live editor bridge belongs in editor/library code.

See [docs/verified-sbox-apis.md](docs/verified-sbox-apis.md) for the currently verified API surface.
