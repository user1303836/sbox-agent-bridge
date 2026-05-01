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

export function registerPrefabTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "prefab",
    "Create, list, inspect, instantiate, and inspect s&box prefab instances in the editor scene.",
    {
      action: z.enum(["create", "list", "get_info", "inspect_instance", "instantiate"]).describe("The prefab action to run."),
      query: z.string().optional().describe("Prefab name/path search query for list."),
      path: z.string().optional().describe("Prefab resource path for create, get_info, or instantiate."),
      name: z.string().optional().describe("Instance name for instantiate."),
      gameObjectId: z.string().optional().describe("Source GameObject id for create, or prefab instance GameObject id for inspect_instance."),
      parentId: z.string().optional().describe("Optional parent GameObject id for instantiate."),
      position: vector3Schema.optional().describe("World position for instantiate."),
      rotation: rotationSchema.optional().describe("World rotation for instantiate."),
      scale: vector3Schema.optional().describe("World scale for instantiate."),
      targetSession: z.string().optional().describe("Session target for read-only inspect_instance: active, editor, runtime, playing, or game."),
      sessionId: z.string().optional().describe("Optional editor session id selector for inspect_instance."),
      sessionIndex: z.number().int().optional().describe("Optional editor session index selector for inspect_instance."),
      sessionPath: z.string().optional().describe("Optional scene resource path selector for inspect_instance."),
      sessionScene: z.string().optional().describe("Optional scene name selector for inspect_instance."),
      includeSerialized: z.boolean().optional().describe("Include raw serialized prefab instance JSON in inspect_instance results."),
      maxSamples: z.number().int().min(0).max(100).optional().describe("Maximum prefab patch sample rows returned by inspect_instance."),
      overwrite: z.boolean().optional().describe("Allow create to replace an existing prefab file."),
      bindSource: z.boolean().optional().describe("Set the source GameObject as an instance of the newly created prefab."),
      showInMenu: z.boolean().optional().describe("Whether the created prefab should appear in create menus."),
      menuPath: z.string().optional().describe("Menu path for the created prefab."),
      menuIcon: z.string().optional().describe("Menu icon for the created prefab."),
      maxResults: z.number().int().min(1).max(500).optional().describe("Maximum list results.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`prefab.${action}`, payload));
    }
  );
}
