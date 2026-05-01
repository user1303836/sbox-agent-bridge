import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerRuntimeTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "runtime",
    "Inspect and invoke deterministic runtime test hooks in the live s&box GameSession.",
    {
      action: z.enum(["list_test_actions", "run_test_action"]).describe("The runtime action to run."),
      targetSession: z
        .enum(["active", "editor", "playing", "runtime", "game"])
        .optional()
        .describe("Session to inspect. Defaults to runtime so test actions run against the live GameSession."),
      sessionId: z.string().optional().describe("Optional editor session id selector."),
      sessionIndex: z.number().int().min(0).optional().describe("Optional editor session index selector."),
      sessionPath: z.string().optional().describe("Optional scene source path selector."),
      sessionScene: z.string().optional().describe("Optional scene name selector."),
      componentId: z.string().optional().describe("Specific component id exposing AgentBridgeRunTestAction or AgentBridgeTestAction."),
      gameObjectId: z.string().optional().describe("Limit action lookup to one GameObject."),
      componentType: z.string().optional().describe("Limit action lookup to a component type name/full name substring."),
      includeAllCandidates: z.boolean().optional().describe("For list_test_actions, include matched components even when no supported test-action method is detected."),
      testAction: z.string().optional().describe("Runtime test action name for run_test_action."),
      payload: z.record(z.string(), z.any()).optional().describe("JSON payload passed to the component-authored test action.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`runtime.${action}`, payload));
    }
  );
}
