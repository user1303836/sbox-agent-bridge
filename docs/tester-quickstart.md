# Tester Quickstart

This is the shortest path for someone validating the bridge in a normal s&box project.

## 1. Build The MCP Server

```powershell
cd C:\path\to\sbox-agent-bridge\mcp-server
npm install
npm run build
```

## 2. Install The Editor Bridge

```powershell
cd C:\path\to\sbox-agent-bridge
.\scripts\install-editor-bridge.ps1 -ProjectPath 'C:\Users\you\Documents\s&box projects\YourProject'
```

Open the project in s&box and wait for compile. The bridge starts automatically after the editor library loads. The status dock is under:

```text
View -> Agent Bridge
```

## 3. Run The Readiness Check

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

## 4. Run The MVP Smoke

Use a saved test scene. For scratch scenes, allow discard/reload recovery:

```powershell
cd C:\path\to\sbox-agent-bridge\mcp-server
$env:SBOX_AGENT_BRIDGE_MVP_SCENE='scenes/minimal.scene'
$env:SBOX_AGENT_BRIDGE_DISCARD_UNSAVED='1'
npm run smoke:mvp
```

The smoke verifies the main external-tester path: doctor, compile wait, scene recovery, scene read, object creation, model/material assignment, physics read-back, sound event/component read-back, prefab creation/instantiation/inspection, runtime model preview capture, play/stop settle, and cleanup.

For a broader gameplay walkthrough after the MVP smoke passes:

```powershell
$env:SBOX_AGENT_BRIDGE_BOXING_SCENE='scenes/minimal.scene'
$env:SBOX_AGENT_BRIDGE_DISCARD_UNSAVED='1'
npm run walkthrough:boxing
```

This installs and verifies a small boxing game loop in the open test project.

## 5. MCP Client Config

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
