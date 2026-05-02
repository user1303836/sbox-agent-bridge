import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerNetworkTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "network",
    "Inspect s&box network connections and read or mutate GameObject network metadata in the active editor session.",
    {
      action: z.enum(["connections", "inspect_object", "set_object_mode"]).describe("The network action to run."),
      gameObjectId: z.string().optional().describe("Target GameObject id for inspect_object or set_object_mode."),
      targetSession: z
        .enum(["active", "editor", "playing", "runtime", "game"])
        .optional()
        .describe("For inspect_object, choose which session to read from. Mutations operate on the active editor scene."),
      sessionId: z.string().optional().describe("Optional editor session id selector for inspect_object."),
      sessionIndex: z.number().int().min(0).optional().describe("Optional editor session index selector for inspect_object."),
      sessionPath: z.string().optional().describe("Optional scene source path selector for inspect_object."),
      sessionScene: z.string().optional().describe("Optional scene name selector for inspect_object."),
      networkMode: z.enum(["Never", "Object", "Snapshot"]).optional().describe("GameObject NetworkMode value."),
      ownerTransfer: z.enum(["Takeover", "Fixed", "Request"]).optional().describe("GameObject OwnerTransfer policy."),
      networkOrphaned: z.enum(["Destroy", "Host", "Random", "ClearOwner"]).optional().describe("GameObject NetworkOrphaned policy."),
      alwaysTransmit: z.boolean().optional().describe("Whether the object should always transmit.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`network.${action}`, payload));
    }
  );
}
