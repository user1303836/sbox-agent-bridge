import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerScriptTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "script",
    "Create, edit, delete, list, read, search, or statically analyze C# scripts under the current s&box project's Code directory.",
    {
      action: z.enum(["create", "edit", "delete", "list", "read", "search", "analyze"]).describe("The script action to run."),
      path: z.string().optional().describe("Path under Code, such as Components/MyComponent.cs."),
      content: z.string().optional().describe("Complete C# source content for create/edit."),
      overwrite: z.boolean().optional().describe("Allow create to replace an existing script."),
      query: z.string().optional().describe("Search query for list/search."),
      caseSensitive: z.boolean().optional().describe("Use case-sensitive matching for search."),
      maxResults: z.number().int().min(1).max(1000).optional().describe("Maximum script list results."),
      maxMatches: z.number().int().min(1).max(1000).optional().describe("Maximum search matches."),
      maxBytes: z.number().int().min(1).max(2 * 1024 * 1024).optional().describe("Maximum script bytes returned by read.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`script.${action}`, payload));
    }
  );
}
