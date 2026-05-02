# sbox-agent-bridge

Provider-neutral MCP server and editor bridge for AI-assisted s&box development. Inspect, control, and automate a live s&box editor session through the Model Context Protocol.

`sbox-agent-bridge` lets MCP-capable agents work from live editor state instead of guessing from source files alone. Agents can read scenes, inspect GameObjects and components, make small undoable edits, observe compile/play/log feedback, and run deterministic runtime checks while building a game.

The goal is not an unsafe "do anything" bridge. The bridge exposes narrow, observable editor actions that can be verified, undone where applicable, and composed safely.

## What Can Agents Do?

Ask an MCP-capable agent to:

- run a bridge readiness/doctor check before editing
- read active project metadata and paths
- create a blank scene and save it to a project `.scene` path
- summarize the active editor scene or the live runtime `GameSession`
- list editor tabs/sessions and activate the scene you want to edit
- inspect the current selection
- search the scene for GameObjects by name or component
- create, rename, move, duplicate, reparent, enable/disable, or frame GameObjects
- inspect components, editable properties, schema hints, and resource-backed fields
- add, remove, enable/disable, validate, or update components
- validate component property values before writing them
- create or edit C# scripts under the project `Code` directory
- search assets, inspect model bounds/orientation candidates, assign models/materials, create simple materials, and set material parameters
- store asset orientation overrides and place known models upright/grounded with one verified command
- list/create/assign/preview sound events
- add colliders, rigidbodies, simple joints, and run scene raycasts
- create, inspect, list, and instantiate prefabs
- capture camera screenshots with luminance stats for visual feedback
- start/stop play mode, wait for runtime readiness, and inspect play state
- recover a sourced editor scene after stale play/stop tabs
- read compile/hotload diagnostics and recent editor logs with log cursors
- invoke component-authored runtime test actions for deterministic gameplay/UI assertions
- run small multi-step scene batches with read-back after each operation
- verify mutations by reading editor state back after each change

This is the shape we are building toward: point Claude, Codex, Kimi, or any MCP-capable agent at your live s&box editor and ask it to help build the scene with you.

## How It Works

The project has two pieces:

- `editor/`: an s&box library with the bridge runtime, command handlers, feedback state, and an **Agent Bridge** status dock.
- `mcp-server/`: a TypeScript MCP stdio server that exposes agent-facing tools and forwards requests to the editor bridge.

The local loop looks like this:

```text
Agent / MCP client
  <-> MCP server over stdio
    <-> local bridge IPC
      <-> s&box editor bridge runtime
        <-> SceneEditorSession.Active / All / GameSession
```

The current transport is local file IPC under `%TEMP%/sbox-agent-bridge` by default. It is intentionally simple and inspectable: the MCP server writes request JSON files, the editor-frame pump processes them on the s&box editor thread, and the bridge writes response JSON files. The command envelope is designed so other transports can be added later behind the same bridge-client boundary.

The dock is the human-facing status and control surface. The editor-frame bridge pump starts automatically once the editor library compiles and loads.

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

For a fresh-project walkthrough, create a Minimal Game project from the local s&box template first:

```powershell
.\scripts\create-minimal-sbox-project.ps1 `
  -ProjectPath 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh' `
  -Title 'Agent Bridge MVP Fresh' `
  -Ident 'agent_bridge_mvp_fresh'
```

From the repo root:

```powershell
.\scripts\install-editor-bridge.ps1 -ProjectPath 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh'
```

To launch a project from the command line, use the verified s&box startup shape:

```powershell
$env:SBOX_AGENT_BRIDGE_IPC = "$env:TEMP\sbox-agent-bridge-mvp-fresh"
.\scripts\start-sbox-project.ps1 `
  -ProjectFile 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh\agent_bridge_mvp_fresh.sbproj' `
  -IpcRoot $env:SBOX_AGENT_BRIDGE_IPC `
  -ClearIpc `
  -WaitForBridgeSeconds 90
```

Or copy manually:

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
        CommandDispatcher.cs
        Handlers/
          ...
```

Open or launch the project in s&box and let it compile. The bridge starts automatically once the editor bridge assembly loads. To view status and controls, open:

```text
View -> Agent Bridge
```

If the dock does not show `Status: running`, click **Start Bridge**.

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

Set the same `SBOX_AGENT_BRIDGE_IPC` value in the environment before launching the s&box editor when you need an isolated bridge instance, such as a fresh-project walkthrough while another editor is already open. `scripts/start-sbox-project.ps1 -IpcRoot ...` sets that value for the launched editor process.

### 4. Try It

Start with read-only prompts:

```text
Use the sbox-agent-bridge MCP tools to run the bridge doctor, list editor tabs, summarize the active scene, and inspect the current selection. Do not mutate the scene yet.
```

Then try one small verified edit:

```text
Use the sbox-agent-bridge MCP tools to create one GameObject named Agent Bridge Test, verify that it exists, then undo the creation.
```

For play-mode feedback, try:

```text
Use the sbox-agent-bridge MCP tools to read compile status, start play mode, wait for the runtime scene, summarize the runtime GameSession, then stop play mode.
```

## Current Tool Surface

The MCP server currently registers these tools: `editor`, `scene`, `gameobject`, `component`, `script`, `asset`, `visual`, `sound`, `physics`, `prefab`, and `runtime`.

- `editor`: bridge status/doctor, project info, active context, editor tabs, tab activation, new/open/recover scene, selection, save/save-as verification, undo, redo, frame object, play/stop, play state, compile status, recent logs, combined feedback, and MCP-side `wait_compile` / `wait_runtime` / `wait_stopped` helpers
- `scene`: summary, hierarchy, search, GameObject details, target-session-aware runtime reads, and small verified batches with `$ref` aliasing
- `gameobject`: get, create, rename, transform, enable/disable, destroy, duplicate, reparent, and place assets with orientation overrides plus ground alignment; create can optionally parent the new object
- `component`: list types, list on object, inspect, inspect property schemas, add, remove, enable/disable, set property, and validate property
- `script`: create, edit, and delete C# scripts in the project `Code` directory
- `asset`: search assets, inspect assets, inspect model bounds/orientation candidates, get/set model orientation overrides, assign models/materials, create simple `.vmat` materials, and set material parameters
- `visual`: capture rendered camera PNGs with camera metadata and luminance statistics
- `sound`: list sound assets, inspect sound assets/events, create `.sound` events, assign `SoundPointComponent`, and preview sound events
- `physics`: inspect physics components, add colliders, add rigidbodies, add simple joints, and raycast against the active scene
- `prefab`: create prefabs from scene GameObjects, list/inspect prefab assets, instantiate prefab roots into the active editor scene, and inspect prefab instance metadata
- `runtime`: list and invoke component-authored deterministic runtime test actions in the live `GameSession`

Component property metadata includes JSON-shape hints so agents can see what a property expects before writing it, including resource references that can be set from asset paths. Property writes can also be dry-run validated without mutating the scene.

Target-session-aware reads can use `targetSession: "runtime"` / `"game"` to inspect the live `GameSession` while play mode is running, rather than accidentally reading a stale editor tab.

For detailed implementation status, see [docs/status.md](docs/status.md) and [docs/capability-matrix.md](docs/capability-matrix.md).

## Verification And Smoke Tests

### CI-safe MCP checks

```powershell
cd mcp-server
npm run ci
```

`npm run ci` typechecks the TypeScript server, runs bridge-client and wait-helper tests, and builds `dist/`. GitHub Actions also validates JSON metadata and `.sbproj` files.

These checks do **not** prove live editor behavior because CI does not launch a real s&box editor.

### Live editor smoke

With a s&box project open and the bridge loaded:

```powershell
cd mcp-server
npm run smoke:live
```

The live smoke uses the same file IPC path as the MCP server. It reads editor feedback, checks save-state reporting, creates temporary GameObjects, verifies core scene and GameObject actions, runs a small `scene.batch`, validates component property schemas, checks visual/spatial feedback with `asset.inspect_model` and `visual.capture_camera`, verifies orientation override storage plus `gameobject.place_asset`, mutates `AgentBridgeMutationFixture` when it is addable by the editor, checks undo/redo, and attempts cleanup.

To require fixture-backed component mutation coverage, set `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1`. If the smoke test says `AgentBridgeMutationFixture` is not available, wait for s&box hotload or reopen the project. Do not leave duplicate fixture files in the project tree; duplicate component type definitions can break a cold compile and prevent the Agent Bridge dock from loading.

### MVP tester smoke

For external testers, prefer the MVP suite:

```powershell
cd mcp-server
npm run smoke:mvp-suite
```

It creates its own saved scene, then verifies `bridge.doctor`, compile wait, scene recovery, scene read, object creation, model/material assignment, physics/sound/prefab read-back, runtime model preview capture, play/stop settle, script delete/compile recovery, animation helper setup, basic particle setup, and cleanup without relying on the ARPG-specific runtime hooks.

### Bootstrap smoke

To verify clean-room scene creation and save-as behavior:

```powershell
cd mcp-server
npm run smoke:bootstrap
```

It reads active project info, creates a blank scene, creates a marker object, saves the scene to `scenes/agent_bridge/smoke/bootstrap_smoke.scene`, reopens it, verifies persisted marker read-back, and restores the previously active sourced scene.

### Boxing walkthrough

For a clean-room gameplay build that covers a different genre:

```powershell
cd mcp-server
$env:SBOX_AGENT_BRIDGE_BOXING_SCENE='scenes/minimal.scene'
$env:SBOX_AGENT_BRIDGE_DISCARD_UNSAVED='1'
npm run walkthrough:boxing
```

It installs a `BoxingDemoController` through the bridge, adds it to the scene, verifies jab/block/dodge/knockdown/TKO/decision runtime actions, captures the generated broadcast camera, and reports bridge gaps found during the walkthrough.

### Runtime feedback smoke

For deterministic play-mode/runtime verification:

```powershell
cd mcp-server
SBOX_AGENT_BRIDGE_DISCARD_UNSAVED=1 npm run smoke:runtime
```

The runtime smoke stops stale play sessions, waits for compile/stopped/runtime transitions, opens the configured scene, enters play mode, waits for a live runtime `GameSession`, lists component-authored runtime test actions, invokes ARPG testbed actions for logical UI/gameplay state, then stops and waits for the editor to settle again.

Useful environment variables are documented in [docs/testing.md](docs/testing.md), including `SBOX_AGENT_BRIDGE_IPC`, `SBOX_AGENT_BRIDGE_TIMEOUT_MS`, `SBOX_AGENT_BRIDGE_MVP_SUITE_SCENE`, `SBOX_AGENT_BRIDGE_SMOKE_PREFIX`, `SBOX_AGENT_BRIDGE_SMOKE_KEEP_OBJECTS`, `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE`, `SBOX_AGENT_BRIDGE_RUNTIME_SCENE`, `SBOX_AGENT_BRIDGE_BOXING_SCENE`, `SBOX_AGENT_BRIDGE_CAPABILITY_SMOKE_SCRIPT`, and `SBOX_AGENT_BRIDGE_DISCARD_UNSAVED`.

## Current Caveats

This project is useful now, but the docs intentionally keep the status honest:

- The editor bridge must be installed into each s&box project that should expose live editor access.
- The s&box editor must be open and the bridge editor library must compile/load.
- CI covers the MCP server and metadata only; live editor behavior is local-smoke verified.
- `gameobject.destroy` is reverified by focused smokes, but cleanup scripts still fall back to disabling objects if the native editor delete/undo path fails in a stale session.
- `script.delete` is verified by `smoke:capability-gaps` against a scratch C# file, but script tooling is still full-file create/edit/delete rather than source-aware patching.
- `component.list_types` discovers built-in/editor-visible components, but local project component discovery is still partial. `component.add` can add compiled local components by exact C# type name in verified cases.
- `gameobject.duplicate` is shallow: it copies name, enabled state, transform, and parent, but not components or children.
- `visual.capture_camera` captures camera output, not the editor/game viewport overlay. Runtime UI self-report exists through test actions, but generic HUD/panel pixel verification remains future work.
- Bounds and orientation candidates do not prove semantic uprightness. Use `asset.set_orientation_override`, `gameobject.place_asset`, captures, and human/vision confirmation for ambiguous models.

See [docs/status.md](docs/status.md) for the full current caveat list.

## Troubleshooting

### Agent Bridge Dock Is Missing

If `View -> Agent Bridge` is missing, the editor bridge assembly probably did not compile or load. Check the s&box editor log:

```text
C:\Program Files (x86)\Steam\steamapps\common\sbox\logs\sbox-dev.log
```

Look for errors such as `Compile of 'local.sbox_agent_bridge' Failed` or `Broken Reference`. When the bridge assembly fails to compile, s&box cannot register the dock, menu entry, or bridge IPC loop.

One common cause during smoke-test or POC work is duplicate fixture scripts. After installing the bridge library, keep exactly one copy of:

```text
YourProject/Libraries/sbox_agent_bridge/Code/TestFixtures/AgentBridgeMutationFixture.cs
```

Remove any extra `AgentBridgeMutationFixture.cs` copies from `YourProject/Code/` or `YourProject/Libraries/sbox_agent_bridge/Code/`. Then reopen the project so s&box can cold-compile the library cleanly.

### Live Reads Look Empty Or Stale After Play Mode

Play/stop transitions can leave duplicate or stale editor tabs in some sessions. Use `editor.recover_scene` with the saved scene path to stop play sessions and reload/reactivate the editor scene. For scratch/test scenes only, `discardUnsaved: true` can allow recovery when an unsaved stale tab blocks reload.

### Windows npm Shim Issues

On some Windows shells, `npm` may not be on `PATH`, or `npm run check` may hit an `Access is denied` shim issue. Direct Node execution is a workable fallback; see [docs/testing.md](docs/testing.md) and [docs/agent-handoff.md](docs/agent-handoff.md) for examples.

## Example Prompts

```text
Summarize the active s&box scene and tell me what GameObjects and component types are present.
```

```text
Inspect the selected GameObject, list its components, and explain which component properties are editable and settable.
```

```text
Create an empty parent GameObject named Arena Markers, add three child marker objects in a line, then verify the hierarchy.
```

```text
Find the object named PlayerStart, inspect its components, validate any property values before changing them, and report the before/after state.
```

```text
Start play mode, wait for the runtime GameSession, list available runtime test actions, run the relevant state check, then stop play mode and report any new errors from the editor log cursor.
```

## Development

```powershell
cd mcp-server
npm install
npm run ci
```

Useful scripts:

- `scripts/create-minimal-sbox-project.ps1`: create a fresh Minimal Game project from the local s&box template.
- `scripts/install-editor-bridge.ps1`: install the editor bridge library into a project.
- `scripts/start-sbox-project.ps1`: launch a `.sbproj` with optional isolated bridge IPC.
- `npm run dev`: run the MCP server from TypeScript.
- `npm run check`: TypeScript typecheck without emitting files.
- `npm test`: run bridge-client and wait-helper unit tests.
- `npm run build`: compile the MCP server to `dist/`.
- `npm run ci`: typecheck, unit test, and build.
- `npm run walkthrough:boxing`: build and verify the clean-room boxing gameplay walkthrough.
- `npm run smoke:bootstrap`: verify project info, blank scene creation, save-as, reload, and persisted object read-back.
- `npm run smoke:capability-gaps`: verify scratch script delete, animation helper setup, and basic particle stack setup.
- `npm run smoke:live`: run the live editor smoke test against an already-open bridge.
- `npm run smoke:mvp`: run the focused MVP smoke against an already-open bridge and saved scene.
- `npm run smoke:mvp-suite`: create a fresh smoke scene and run the MVP plus focused category smokes against it.
- `npm run smoke:runtime`: run the focused runtime feedback smoke against an already-open bridge.

## Project Docs

Start with [docs/README.md](docs/README.md). Human testers should use [docs/tester-quickstart.md](docs/tester-quickstart.md).

## Grounding

This repo is grounded in the official s&box docs/API schema and local public source research. The bridge uses s&box Scenes, GameObjects, Components, editor sessions, undo scopes, compile/play/log feedback, runtime `GameSession` targeting, and type/property metadata rather than Unity/Godot assumptions.

See [docs/verified-sbox-apis.md](docs/verified-sbox-apis.md) for the currently verified API surface.
