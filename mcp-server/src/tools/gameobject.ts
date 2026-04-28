import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

const vector3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number()
});

const rotationSchema = z.union([
  z.object({
    pitch: z.number().optional(),
    yaw: z.number().optional(),
    roll: z.number().optional()
  }),
  z.object({
    x: z.number(),
    y: z.number(),
    z: z.number(),
    w: z.number()
  })
]);

export function registerGameObjectTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "gameobject",
    "Perform small, undoable GameObject mutations in the active s&box editor scene.",
    {
      action: z
        .enum(["get", "create", "rename", "set_transform", "set_enabled", "destroy", "duplicate", "reparent"])
        .describe("The GameObject action to run."),
      id: z.string().optional().describe("Target GameObject id for read or mutation actions."),
      parentId: z.string().optional().describe("Parent GameObject id for reparent; omit to move to scene root."),
      name: z.string().optional().describe("Name for create or rename actions."),
      makeUnique: z.boolean().optional().describe("Make the final GameObject name unique when renaming."),
      keepWorldPosition: z.boolean().optional().describe("Preserve world transform when reparenting."),
      enabled: z.boolean().optional().describe("Enabled state for set_enabled."),
      position: vector3Schema.optional().describe("Optional world position."),
      offset: vector3Schema.optional().describe("Optional world-position offset for duplicate."),
      rotation: rotationSchema.optional().describe("Optional world rotation, either Euler degrees or quaternion."),
      scale: vector3Schema.optional().describe("Optional world scale.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`gameobject.${action}`, payload));
    }
  );
}
