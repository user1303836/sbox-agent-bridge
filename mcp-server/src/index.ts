import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { BridgeClient } from "./bridge-client.js";
import { registerAssetTools } from "./tools/asset.js";
import { registerComponentTools } from "./tools/component.js";
import { registerEditorTools } from "./tools/editor.js";
import { registerGameObjectTools } from "./tools/gameobject.js";
import { registerPhysicsTools } from "./tools/physics.js";
import { registerPrefabTools } from "./tools/prefab.js";
import { registerProjectTools } from "./tools/project.js";
import { registerRuntimeTools } from "./tools/runtime.js";
import { registerSceneTools } from "./tools/scene.js";
import { registerScriptTools } from "./tools/script.js";
import { registerSoundTools } from "./tools/sound.js";
import { registerVisualTools } from "./tools/visual.js";

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
registerScriptTools(server, bridge);
registerProjectTools(server, bridge);
registerAssetTools(server, bridge);
registerVisualTools(server, bridge);
registerSoundTools(server, bridge);
registerPhysicsTools(server, bridge);
registerPrefabTools(server, bridge);
registerRuntimeTools(server, bridge);

await server.connect(new StdioServerTransport());
