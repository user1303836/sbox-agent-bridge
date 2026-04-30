# sbox-agent-bridge

MCP server and editor bridge for AI-assisted s&box development. Inspect, control, and automate a live s&box editor session through the Model Context Protocol.

`sbox-agent-bridge` lets MCP-capable agents work with the editor instead of guessing from source files alone. Agents can read scene state, inspect GameObjects and components, make small verified edits, and use the editor as part of an AI-assisted game development loop.

The goal is not an unsafe "do anything" bridge. The bridge exposes narrow, observable editor actions that can be undone, verified, and composed safely.

## What Can Agents Do?

Ask an agent to:

- summarize the active scene
- inspect the current selection
- search the scene for GameObjects by name or component
- create, rename, move, duplicate, reparent, or delete GameObjects
- inspect components, editable properties, and resource-backed fields
- add, remove, enable, disable, or update components
- validate component property values before writing them
- search assets, assign models/materials, create simple materials, and set material parameters
- list/create/assign/preview sound events
- add colliders, rigidbodies, simple joints, and run scene raycasts
- create, inspect, list, and instantiate prefabs
- start/stop play mode and inspect play state
- read compile/hotload diagnostics and recent editor logs
- run small multi-step scene batches with read-back after each operation
- verify mutations by reading editor state back after each change

This is the shape we are building toward: point Claude, Codex, Kimi, or any MCP-capable agent at your live s&box editor and ask it to help build the scene with you.

## How It Works

The project has two pieces:

- `editor/`: an s&box library with an **Agent Bridge** editor dock.
- `mcp-server/`: a TypeScript MCP server that exposes agent-facing tools over stdio.

The local loop looks like this:

```text
Agent / MCP client
  <-> MCP server
    <-> local bridge IPC
      <-> Agent Bridge dock in s&box
        <-> live editor scene
```

The current transport is local file IPC under `%TEMP%/sbox-agent-bridge`. It is intentionally simple and inspectable, and the command envelope is designed so other transports can be added later.

## Quick Start

### 1. Build The MCP Server

Requirements:

- s&box installed
- Node.js 20 or newer
- npm
- an MCP-capable client or coding agent

```powershell
git clone https://github.com/user1303836/sbox-agent-bridge.git
cd sbox-agent-bridge\mcp-server
npm install
npm run build
```

### 2. Install The Editor Bridge

Copy this repo's `editor/` folder into your s&box project as `Libraries/sbox_agent_bridge`.

From the repo root:

```powershell
$Project = 'C:\Users\you\Documents\s&box projects\YourProject'
New-Item -ItemType Directory -Force -Path "$Project\Libraries\sbox_agent_bridge" | Out-Null
Copy-Item -Path '.\editor\*' -Destination "$Project\Libraries\sbox_agent_bridge" -Recurse -Force
```

Your project should contain:

```text
YourProject/
  Libraries/
    sbox_agent_bridge/
      sbox_agent_bridge.sbproj
      Code/
        TestFixtures/
          AgentBridgeMutationFixture.cs
      Editor/
        BridgeDock.cs
        BridgeRuntime.cs
        ...
```

Open the project in s&box, let it compile, then open:

```text
View -> Agent Bridge
```

Leave the dock open while using MCP tools. If it does not show `Status: running`, click **Start Bridge**.

### 3. Connect Your MCP Client

Point your MCP client at the built server:

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

Optional custom IPC folder:

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

### 4. Try It

Start with read-only prompts:

```text
Use the sbox-agent-bridge MCP tools to check bridge status, summarize the active scene, and inspect the current selection. Do not mutate the scene yet.
```

Then try one small verified edit:

```text
Use the sbox-agent-bridge MCP tools to create one GameObject named Agent Bridge Test, verify that it exists, then undo the creation.
```

## Example Prompts

```text
Summarize the active s&box scene and tell me what GameObjects and component types are present.
```

```text
Inspect the selected GameObject, list its components, and explain which component properties are editable.
```

```text
Create an empty parent GameObject named Arena Markers, add three child marker objects in a line, then verify the hierarchy.
```

```text
Find the object named PlayerStart, inspect its components, validate any property values before changing them, and report the before/after state.
```

## Current Capabilities

The bridge currently exposes these MCP tools: `editor`, `scene`, `gameobject`, `component`, `script`, `asset`, `sound`, `physics`, and `prefab`.

- `editor`: bridge status, active context, open scene, selection, save verification, undo, redo, frame object, play/stop, compile status, recent logs, combined feedback
- `scene`: summary, hierarchy, search, GameObject details, small verified batches
- `gameobject`: get, create, rename, transform, enable/disable, destroy, duplicate, reparent; create can optionally parent the new object
- `component`: list types, list on object, inspect, inspect property schemas, add, remove, enable/disable, set property, validate property
- `script`: create and edit C# scripts in the project `Code` directory
- `asset`: search assets, inspect assets, assign models/materials, create simple `.vmat` materials, set material parameters
- `sound`: list sound assets, create `.sound` events, assign `SoundPointComponent`, preview sound events
- `physics`: add colliders, add rigidbodies, add simple joints, raycast against the active scene
- `prefab`: create prefabs from scene GameObjects, list/inspect prefab assets, instantiate prefab roots into the active editor scene

Component property metadata includes JSON-shape hints so agents can see what a property expects before writing it, including resource references that can be set from asset paths. Property writes can also be dry-run validated without mutating the scene.

For the detailed implementation status, see [docs/status.md](docs/status.md), [docs/capability-matrix.md](docs/capability-matrix.md), and [docs/tool-limitations.md](docs/tool-limitations.md).

## Live Smoke Test

With a s&box project open and the Agent Bridge dock running:

```powershell
cd mcp-server
npm run smoke:live
```

The smoke test creates temporary GameObjects, verifies scene and GameObject actions, checks save-state reporting, validates component property schemas, runs a small `scene.batch`, mutates `AgentBridgeMutationFixture` when it is visible to the editor type library, checks undo/redo, and cleans up after itself.
It also checks the editor feedback loop by reading play state, logs, compile status, and starting/stopping play mode when the editor is not already playing.

To require fixture-backed component mutation coverage, set `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1`. If the smoke test says `AgentBridgeMutationFixture` is not available, wait for s&box hotload or reopen the project. For an already-open project that has not generated the library runtime project yet, you can temporarily copy `editor/Code/TestFixtures/AgentBridgeMutationFixture.cs` into that project's own `Code/` folder.

Current note: direct feedback-loop actions are verified, but the full smoke can be blocked by a native editor delete/undo null reference after play-mode testing in the current s&box session. See [docs/status.md](docs/status.md) before treating live smoke as fully green.

## Development

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

- [Status](docs/status.md)
- [Roadmap](docs/roadmap.md)
- [Capability Matrix](docs/capability-matrix.md)
- [Tool Limitations](docs/tool-limitations.md)
- [Testing Strategy](docs/testing.md)
- [Editor Feedback Loop](docs/editor-feedback-loop.md)
- [ARPG POC First Pass](docs/poc-arpg-first-pass.md)
- [Architecture](docs/architecture.md)
- [Protocol](docs/protocol.md)
- [Verified s&box APIs](docs/verified-sbox-apis.md)
- [Prior Art](docs/prior-art.md)
- [Contributing](CONTRIBUTING.md)

## Grounding

This repo is grounded in the official s&box docs/API schema and local public source research. The bridge uses s&box Scenes, GameObjects, Components, editor sessions, undo scopes, and type/property metadata rather than Unity/Godot assumptions.

See [docs/verified-sbox-apis.md](docs/verified-sbox-apis.md) for the currently verified API surface.
