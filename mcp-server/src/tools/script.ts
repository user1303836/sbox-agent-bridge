import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerScriptTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "script",
    "Create, edit, or delete C# scripts under the current s&box project's Code directory.",
    {
      action: z.enum(["create", "edit", "delete"]).describe("The script action to run."),
      path: z.string().describe("Path under Code, such as Components/MyComponent.cs."),
      content: z.string().optional().describe("Complete C# source content for create/edit."),
      overwrite: z.boolean().optional().describe("Allow create to replace an existing script.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`script.${action}`, payload));
    }
  );
}
