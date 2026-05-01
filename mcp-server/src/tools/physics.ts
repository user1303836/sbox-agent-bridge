import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

const vector3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number()
});

export function registerPhysicsTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "physics",
    "Inspect physics components, add physics/collider/joint components, and run simple scene raycasts in the active s&box editor scene.",
    {
      action: z.enum(["inspect", "add_physics", "add_collider", "add_joint", "raycast"]).describe("The physics action to run."),
      gameObjectId: z.string().optional().describe("Target GameObject id for inspect, add_physics, add_collider, or add_joint."),
      targetSession: z.string().optional().describe("Session target for read-only inspect: active, editor, runtime, playing, or game."),
      sessionId: z.string().optional().describe("Optional editor session id selector for inspect."),
      sessionIndex: z.number().int().optional().describe("Optional editor session index selector for inspect."),
      sessionPath: z.string().optional().describe("Optional scene resource path selector for inspect."),
      sessionScene: z.string().optional().describe("Optional scene name selector for inspect."),
      targetGameObjectId: z.string().optional().describe("Optional second GameObject id for add_joint."),
      type: z.string().optional().describe("Collider or joint type. Collider: box/sphere/capsule. Joint: fixed/hinge/spring/ball/slider."),
      gravity: z.boolean().optional().describe("Rigidbody gravity state."),
      motionEnabled: z.boolean().optional().describe("Rigidbody motion enabled state."),
      mass: z.number().optional().describe("Rigidbody mass override."),
      static: z.boolean().optional().describe("Collider Static setting."),
      trigger: z.boolean().optional().describe("Collider IsTrigger setting."),
      enableCollision: z.boolean().optional().describe("Joint collision setting."),
      scale: vector3Schema.optional().describe("Box collider scale."),
      center: vector3Schema.optional().describe("Collider center."),
      radius: z.number().optional().describe("Sphere/capsule collider radius."),
      start: vector3Schema.optional().describe("Capsule start point."),
      end: vector3Schema.optional().describe("Capsule end point."),
      from: vector3Schema.optional().describe("Raycast start position."),
      to: vector3Schema.optional().describe("Raycast end position."),
      renderMeshes: z.boolean().optional().describe("Include render meshes in raycast.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`physics.${action}`, payload));
    }
  );
}
