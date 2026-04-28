import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerSceneTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "scene",
    "Read scene summary, hierarchy, search results, and GameObject details from the active s&box editor scene.",
    {
      action: z.enum(["summary", "hierarchy", "find", "details"]).describe("The scene action to run."),
      id: z.string().optional().describe("GameObject id for details."),
      includeDisabled: z.boolean().optional().describe("Include disabled GameObjects where applicable."),
      maxDepth: z.number().int().min(0).max(32).optional().describe("Maximum hierarchy depth."),
      maxNodes: z.number().int().min(1).max(1000).optional().describe("Maximum hierarchy nodes."),
      maxResults: z.number().int().min(1).max(500).optional().describe("Maximum search results."),
      nameContains: z.string().optional().describe("Case-insensitive GameObject name substring."),
      componentContains: z.string().optional().describe("Case-insensitive component type substring.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`scene.${action}`, payload));
    }
  );
}
