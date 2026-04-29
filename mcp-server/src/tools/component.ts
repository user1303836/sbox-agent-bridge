import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerComponentTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "component",
    "Discover component types and inspect components/properties in the active s&box editor scene.",
    {
      action: z
        .enum(["list_types", "list_on_gameobject", "get", "get_properties"])
        .describe("The component action to run."),
      id: z.string().optional().describe("Target Component id for get or get_properties."),
      gameObjectId: z.string().optional().describe("Target GameObject id for list_on_gameobject."),
      query: z.string().optional().describe("Case-insensitive filter for component types or properties."),
      includeAbstract: z.boolean().optional().describe("Include abstract component types in list_types."),
      includeAll: z
        .boolean()
        .optional()
        .describe("Include non-[Property] readable properties in get_properties."),
      maxResults: z.number().int().min(1).max(1000).optional().describe("Maximum component types to return."),
      maxProperties: z.number().int().min(1).max(500).optional().describe("Maximum properties to return.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`component.${action}`, payload));
    }
  );
}
