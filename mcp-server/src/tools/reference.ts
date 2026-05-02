import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerReferenceTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "reference",
    "Search installed s&box XML API docs, inspect loaded C# types, read console variables, and inspect API whitelist metadata from the open editor.",
    {
      action: z.enum(["search", "type", "console", "whitelist"]).describe("The reference action to run."),
      query: z.string().optional().describe("Search query for search or whitelist actions."),
      kind: z.enum(["all", "type", "property", "method", "field", "event"]).optional().describe("Optional XML doc member kind filter."),
      typeName: z.string().optional().describe("Loaded C# type name or full name for type inspection."),
      name: z.string().optional().describe("Console variable name for console reads."),
      maxResults: z.number().int().min(1).max(500).optional().describe("Maximum results returned by search or whitelist actions.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`reference.${action}`, payload));
    }
  );
}
