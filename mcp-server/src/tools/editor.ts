import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerEditorTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "editor",
    "Inspect the s&box editor bridge status and active editor context.",
    {
      action: z.enum(["status", "context"]).describe("The editor action to run.")
    },
    async ({ action }) => {
      const bridgeAction = action === "status" ? "bridge.status" : "editor.context";
      return asJsonText(await bridge.send(bridgeAction));
    }
  );
}
