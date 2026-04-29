# Minimal s&box Project Install

1. Create or open a minimal s&box game project.
2. Copy this repo's `editor/` folder into the project as:

```text
YourSboxProject/
  Libraries/
    sbox_agent_bridge/
      sbox_agent_bridge.sbproj
      Code/
        TestFixtures/
          AgentBridgeMutationFixture.cs
      Editor/
        ...
```

3. Open the project in the s&box editor and let it compile.
4. Open `View -> Agent Bridge`.
5. Leave the dock open while using MCP tools.
6. Build and run the MCP server from this repo's `mcp-server/` folder.

The `Code/TestFixtures/AgentBridgeMutationFixture.cs` file is runtime/library code used by `npm run smoke:live` to verify component property mutation. If an already-open project does not expose that component type after hotload, reopen the project or temporarily copy the fixture into the project's own `Code/` folder.

The bridge uses this IPC folder by default:

```text
%TEMP%/sbox-agent-bridge/
```

Set `SBOX_AGENT_BRIDGE_IPC` for the MCP server if you need a custom folder.
