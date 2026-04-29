import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { BridgeClient } from "./bridge-client.js";
import { registerComponentTools } from "./tools/component.js";
import { registerEditorTools } from "./tools/editor.js";
import { registerGameObjectTools } from "./tools/gameobject.js";
import { registerSceneTools } from "./tools/scene.js";

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC
});

const server = new McpServer({
  name: "sbox-agent-bridge",
  version: "0.1.0"
});

registerEditorTools(server, bridge);
registerSceneTools(server, bridge);
registerGameObjectTools(server, bridge);
registerComponentTools(server, bridge);

await server.connect(new StdioServerTransport());
