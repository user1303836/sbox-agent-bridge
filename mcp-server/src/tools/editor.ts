import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";
import { waitForCompile, waitForRuntime, waitForStopped } from "../wait-helpers.js";

const MCP_SERVER_VERSION = "0.1.0";

export function registerEditorTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "editor",
    "Inspect, diagnose, recover, and control the s&box editor bridge and active editor context.",
    {
      action: z
        .enum([
          "status",
          "doctor",
          "context",
          "project_info",
          "tabs",
          "activate_tab",
          "new_scene",
          "open_scene",
          "recover_scene",
          "get_selection",
          "set_selection",
          "save_scene",
          "save_scene_as",
          "undo",
          "redo",
          "frame_object",
          "play_state",
          "play",
          "stop",
          "wait_compile",
          "wait_runtime",
          "wait_stopped",
          "logs",
          "compile_status",
          "feedback"
        ])
        .describe("The editor action to run."),
      id: z.string().optional().describe("Target GameObject id for frame_object."),
      index: z.number().int().min(0).optional().describe("Editor tab index for activate_tab."),
      scene: z.string().optional().describe("Scene/tab name selector for activate_tab."),
      path: z.string().optional().describe("Scene resource path for new_scene, open_scene, recover_scene, or save_scene_as, such as scenes/minimal.scene."),
      name: z.string().optional().describe("Scene name/title for new_scene."),
      targetSession: z
        .enum(["active", "editor", "playing", "runtime", "game"])
        .optional()
        .describe("For play/play_state/stop/feedback, choose which session to inspect or control. Use playing/runtime/game to target the live GameSession instead of the active editor tab."),
      sessionId: z.string().optional().describe("Optional editor session id selector for target-session-aware reads."),
      sessionIndex: z.number().int().min(0).optional().describe("Optional editor session index selector for target-session-aware reads."),
      sessionPath: z.string().optional().describe("Optional scene source path selector for target-session-aware reads."),
      sessionScene: z.string().optional().describe("Optional scene name selector for target-session-aware reads."),
      bringToFront: z.boolean().optional().describe("Bring the opened scene editor tab to front for open_scene."),
      forceReload: z.boolean().optional().describe("For open_scene or recover_scene, reload an already-open sourced scene if it has no unsaved changes."),
      discardUnsaved: z
        .boolean()
        .optional()
        .describe("For open_scene/recover_scene with forceReload, allow discarding unsaved changes in the open scene session. Use only for scratch/test scenes."),
      stopAll: z.boolean().optional().describe("For stop or recover_scene, stop every currently playing editor session before returning read-back state."),
      overwrite: z.boolean().optional().describe("For new_scene with path or save_scene_as, allow replacing an existing scene asset."),
      activateAfterSave: z.boolean().optional().describe("For new_scene with path or save_scene_as, open the saved scene asset after writing it."),
      timeoutMs: z.number().int().min(100).max(120_000).optional().describe("For wait_* actions, maximum time to wait."),
      pollMs: z.number().int().min(25).max(5_000).optional().describe("For wait_* actions, polling interval."),
      sinceSequence: z
        .number()
        .int()
        .min(0)
        .optional()
        .describe("For wait_compile, require a compile group sequence greater than this baseline before settling."),
      requireObservedCompile: z.boolean().optional().describe("For wait_compile, require at least one observed compile group before settling."),
      minObjects: z.number().int().min(0).optional().describe("For wait_runtime, minimum runtime scene object count required before settling."),
      requireSceneSummary: z.boolean().optional().describe("For wait_runtime, require scene.summary targetSession=runtime to succeed before settling."),
      requireNoGameSessions: z.boolean().optional().describe("For wait_stopped, also require derived GameSession tabs to disappear."),
      ids: z.array(z.string()).optional().describe("GameObject ids to select when action is set_selection."),
      saveAs: z.boolean().optional().describe("Force the editor's human-visible save-as flow when saving the active scene. Prefer save_scene_as with path for noninteractive automation."),
      dryRun: z.boolean().optional().describe("For save_scene, report save verification state without writing."),
      maxLines: z.number().int().positive().max(1000).optional().describe("Maximum editor log lines to return for logs or feedback."),
      afterIndex: z
        .number()
        .int()
        .min(-1)
        .optional()
        .describe("For logs or feedback, return only log entries with a stable file line index greater than this value. Use verified.logs.nextAfterIndex from a prior response as the next cursor."),
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
      if (action === "wait_compile") {
        return asJsonText(await waitForCompile(bridge, payload));
      }

      if (action === "wait_runtime") {
        return asJsonText(await waitForRuntime(bridge, payload));
      }

      if (action === "wait_stopped") {
        return asJsonText(await waitForStopped(bridge, payload));
      }

      if (action === "doctor") {
        return asJsonText(await bridge.send("bridge.doctor", { ...payload, mcpServerVersion: MCP_SERVER_VERSION }));
      }

      const bridgeAction = action === "status" ? "bridge.status" : `editor.${action}`;
      return asJsonText(await bridge.send(bridgeAction, payload));
    }
  );
}
