# Tester Quickstart

This is the shortest path for someone validating the bridge in a fresh or normal s&box project.

## 1. Build The MCP Server

```powershell
cd C:\path\to\sbox-agent-bridge\mcp-server
npm install
npm run build
```

## 2. Create Or Choose A Project

For a true fresh-project walkthrough, instantiate a Minimal Game project from the local s&box template:

```powershell
cd C:\path\to\sbox-agent-bridge
.\scripts\create-minimal-sbox-project.ps1 `
  -ProjectPath 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh' `
  -Title 'Agent Bridge MVP Fresh' `
  -Ident 'agent_bridge_mvp_fresh'
```

You can also use an existing project. There is no in-editor bridge action for switching projects, but `scripts/start-sbox-project.ps1` can launch a chosen `.sbproj` with an optional isolated IPC root.

## 3. Install The Editor Bridge

```powershell
cd C:\path\to\sbox-agent-bridge
.\scripts\install-editor-bridge.ps1 -ProjectPath 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh'
```

Launch the project through the helper script, or open it normally in s&box:

```powershell
$env:SBOX_AGENT_BRIDGE_IPC = "$env:TEMP\sbox-agent-bridge-mvp-fresh"
.\scripts\start-sbox-project.ps1 `
  -ProjectFile 'C:\Users\you\Documents\s&box projects\AgentBridgeMvpFresh\agent_bridge_mvp_fresh.sbproj' `
  -IpcRoot $env:SBOX_AGENT_BRIDGE_IPC `
  -ClearIpc `
  -WaitForBridgeSeconds 90
```

Wait for compile. The bridge starts automatically after the editor library loads. The status dock is under:

```text
View -> Agent Bridge
```

## 4. Run The Readiness Check

From an MCP-capable agent, run the `editor` tool with `action: "doctor"`.

Direct file-IPC callers can send:

```json
{
  "id": "doctor-check",
  "action": "bridge.doctor",
  "payload": {}
}
```

The doctor should report `overall: "pass"` or an actionable `nextSuggestedAction`.

## 5. Run The MVP Suite

The MVP suite creates its own saved scene and then runs the focused bootstrap, MVP, asset/material, physics, sound, prefab, matrix-core, project-file/input, script-introspection, script/animation, and particle checks against it:

```powershell
cd C:\path\to\sbox-agent-bridge\mcp-server
npm run smoke:mvp-suite
```

The suite defaults to `scenes/agent_bridge/smoke/mvp_suite.scene`. Override it with `SBOX_AGENT_BRIDGE_MVP_SUITE_SCENE` when needed.

For a narrower single-smoke pass after the suite has created a scene:

```powershell
$env:SBOX_AGENT_BRIDGE_MVP_SCENE='scenes/agent_bridge/smoke/mvp_suite.scene'
npm run smoke:mvp
```

That smoke verifies the main external-tester path: doctor, compile wait, scene recovery, scene read, object creation, model/material assignment, physics read-back, sound event/component read-back, prefab creation/instantiation/inspection, runtime model preview capture, play/stop settle, and cleanup.

For a broader gameplay walkthrough after the MVP smoke passes:

```powershell
$env:SBOX_AGENT_BRIDGE_BOXING_SCENE='scenes/agent_bridge/smoke/mvp_suite.scene'
$env:SBOX_AGENT_BRIDGE_DISCARD_UNSAVED='1'
npm run walkthrough:boxing
```

This installs and verifies a small boxing game loop in the open test project.

## 6. MCP Client Config

Point your client at the built server:

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

## Troubleshooting

- If the dock is missing, check `sbox-dev.log` for compile errors in `local.sbox_agent_bridge`.
- If reads are stale after play mode, run `editor.recover_scene` with the saved scene path.
- If `npm` is unavailable on PATH, use the direct Node commands in [testing.md](testing.md).
