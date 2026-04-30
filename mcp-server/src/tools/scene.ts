import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerSceneTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "scene",
    "Read scene state and run small verified batches against the active s&box editor scene.",
    {
      action: z.enum(["summary", "hierarchy", "find", "details", "batch"]).describe("The scene action to run."),
      id: z.string().optional().describe("GameObject id for details."),
      includeDisabled: z.boolean().optional().describe("Include disabled GameObjects where applicable."),
      maxDepth: z.number().int().min(0).max(32).optional().describe("Maximum hierarchy depth."),
      maxNodes: z.number().int().min(1).max(1000).optional().describe("Maximum hierarchy nodes."),
      maxResults: z.number().int().min(1).max(500).optional().describe("Maximum search results."),
      nameContains: z.string().optional().describe("Case-insensitive GameObject name substring."),
      componentContains: z.string().optional().describe("Case-insensitive component type substring."),
      operations: z
        .array(
          z.object({
            key: z.string().optional().describe("Optional alias for referencing this operation's result later in the batch."),
            action: z.string().describe("Full bridge action to execute, such as gameobject.create or component.add."),
            payload: z.record(z.string(), z.any()).optional().describe("Payload for the bridge action. Use { \"$ref\": \"alias.verified.id\" } references.")
          })
        )
        .max(50)
        .optional()
        .describe("Operations for scene.batch."),
      stopOnError: z.boolean().optional().describe("Stop scene.batch after the first failed operation. Defaults to true."),
      maxOperations: z.number().int().min(1).max(50).optional().describe("Maximum allowed scene.batch operation count.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`scene.${action}`, payload));
    }
  );
}
