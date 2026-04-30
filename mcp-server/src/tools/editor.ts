import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerEditorTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "editor",
    "Inspect the s&box editor bridge status and active editor context.",
    {
      action: z
        .enum([
          "status",
          "context",
          "get_selection",
          "set_selection",
          "save_scene",
          "undo",
          "redo",
          "frame_object",
          "play_state",
          "play",
          "stop",
          "logs",
          "compile_status",
          "feedback"
        ])
        .describe("The editor action to run."),
      id: z.string().optional().describe("Target GameObject id for frame_object."),
      ids: z.array(z.string()).optional().describe("GameObject ids to select when action is set_selection."),
      saveAs: z.boolean().optional().describe("Force a save-as flow when saving the active scene."),
      dryRun: z.boolean().optional().describe("For save_scene, report save verification state without writing."),
      maxLines: z.number().int().positive().max(1000).optional().describe("Maximum editor log lines to return for logs or feedback."),
      contains: z.string().optional().describe("Case-insensitive log substring filter for logs or feedback."),
      level: z
        .enum(["all", "info", "warn", "error", "trace"])
        .optional()
        .describe("Best-effort inferred log level filter for logs or feedback."),
      maxDiagnostics: z
        .number()
        .int()
        .min(0)
        .max(100)
        .optional()
        .describe("Maximum compiler diagnostics to return for compile_status or feedback.")
    },
    async ({ action, ...payload }) => {
      const bridgeAction = action === "status" ? "bridge.status" : `editor.${action}`;
      return asJsonText(await bridge.send(bridgeAction, payload));
    }
  );
}
