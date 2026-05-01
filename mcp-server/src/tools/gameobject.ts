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
        .enum(["get", "create", "rename", "set_transform", "set_enabled", "destroy", "duplicate", "reparent", "place_asset"])
        .describe("The GameObject action to run."),
      id: z.string().optional().describe("Target GameObject id for read or mutation actions."),
      targetSession: z
        .enum(["active", "editor", "playing", "runtime", "game"])
        .optional()
        .describe("For get, choose which session to read from. Mutations still operate on the active editor scene."),
      sessionId: z.string().optional().describe("Optional editor session id selector for get."),
      sessionIndex: z.number().int().min(0).optional().describe("Optional editor session index selector for get."),
      sessionPath: z.string().optional().describe("Optional scene source path selector for get."),
      sessionScene: z.string().optional().describe("Optional scene name selector for get."),
      parentId: z.string().optional().describe("Parent GameObject id for create or reparent; omit during reparent to move to scene root."),
      name: z.string().optional().describe("Name for create or rename actions."),
      modelPath: z.string().optional().describe("Model path for place_asset."),
      materialPath: z.string().optional().describe("Optional material path for place_asset."),
      makeUnique: z.boolean().optional().describe("Make the final GameObject name unique when renaming."),
      keepWorldPosition: z.boolean().optional().describe("Preserve world transform when reparenting."),
      enabled: z.boolean().optional().describe("Enabled state for set_enabled."),
      alignToGround: z.boolean().optional().describe("For place_asset, lift the object so transformed model bounds sit on the requested ground position."),
      requireOrientationOverride: z.boolean().optional().describe("For place_asset, fail if no stored orientation override exists for the model."),
      yaw: z.number().optional().describe("Yaw offset in degrees for place_asset."),
      baseRotation: z
        .object({
          pitch: z.number().optional(),
          yaw: z.number().optional(),
          roll: z.number().optional()
        })
        .optional()
        .describe("Optional per-call base rotation for place_asset, overriding the stored orientation profile."),
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
