import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerProjectTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "project",
    "Safely list, read, write, and delete files inside the active s&box project, and inspect or edit ProjectSettings/Input.config actions.",
    {
      action: z
        .enum([
          "list_files",
          "read_file",
          "write_file",
          "delete_file",
          "input_actions",
          "upsert_input_action",
          "remove_input_action"
        ])
        .describe("The project action to run."),
      root: z
        .enum(["project", "root", "assets", "code", "editor", "settings", "projectsettings", "project_settings"])
        .optional()
        .describe("Project-scoped root for file actions. Defaults to assets."),
      path: z.string().optional().describe("Path under the selected project root for file actions."),
      glob: z.string().optional().describe("File glob for list_files, such as *.config or ** is not required when recursive is true."),
      recursive: z.boolean().optional().describe("Whether list_files should recurse. Defaults to true."),
      includeDirectories: z.boolean().optional().describe("Whether list_files should include directories."),
      maxResults: z.number().int().min(1).max(2000).optional().describe("Maximum list_files results."),
      maxBytes: z.number().int().min(1).max(2 * 1024 * 1024).optional().describe("Maximum bytes returned by read_file."),
      content: z.string().optional().describe("Complete UTF-8 content for write_file."),
      overwrite: z.boolean().optional().describe("Allow write_file to replace an existing file."),
      createDirectories: z.boolean().optional().describe("Create parent directories for write_file. Defaults to true."),
      query: z.string().optional().describe("Case-insensitive name/title query for input_actions."),
      name: z.string().optional().describe("Input action name for upsert_input_action or remove_input_action."),
      groupName: z.string().optional().describe("Input action group name for input_actions/upsert_input_action."),
      title: z.string().nullable().optional().describe("Input action title for upsert_input_action. Pass null to clear."),
      keyboardCode: z.string().optional().describe("Keyboard code for upsert_input_action, such as k, mouse1, or None."),
      gamepadCode: z.string().optional().describe("Gamepad code for upsert_input_action, such as A, RightTrigger, or None.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`project.${action}`, payload));
    }
  );
}
