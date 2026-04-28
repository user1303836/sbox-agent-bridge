import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerEditorTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "editor",
    "Inspect the s&box editor bridge status and active editor context.",
    {
      action: z.enum(["status", "context", "get_selection", "set_selection"]).describe("The editor action to run."),
      ids: z.array(z.string()).optional().describe("GameObject ids to select when action is set_selection.")
    },
    async ({ action, ...payload }) => {
      const bridgeAction = action === "status" ? "bridge.status" : `editor.${action}`;
      return asJsonText(await bridge.send(bridgeAction, payload));
    }
  );
}
