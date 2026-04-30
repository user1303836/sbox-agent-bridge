import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerVisualTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "visual",
    "Capture rendered visual feedback from the active s&box editor scene.",
    {
      action: z.enum(["capture_camera"]).describe("The visual feedback action to run."),
      cameraComponentId: z.string().optional().describe("Optional CameraComponent id to capture from."),
      gameObjectId: z.string().optional().describe("Optional GameObject id containing the CameraComponent to capture from."),
      width: z.number().int().min(64).max(2048).optional().describe("Capture width in pixels."),
      height: z.number().int().min(64).max(2048).optional().describe("Capture height in pixels."),
      name: z.string().optional().describe("Optional capture label used in the saved PNG filename.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`visual.${action}`, payload));
    }
  );
}
