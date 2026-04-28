import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

const vector3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number()
});

export function registerGameObjectTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "gameobject",
    "Perform small, undoable GameObject mutations in the active s&box editor scene.",
    {
      action: z.enum(["create"]).describe("The GameObject action to run."),
      name: z.string().optional().describe("Name for a newly created GameObject."),
      position: vector3Schema.optional().describe("Optional world position for the new GameObject.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`gameobject.${action}`, payload));
    }
  );
}
