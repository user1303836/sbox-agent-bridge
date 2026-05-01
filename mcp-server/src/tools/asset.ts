import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

export function registerAssetTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "asset",
    "Search assets, inspect model bounds/orientation hints, and assign common render assets to GameObjects in the active s&box editor scene.",
    {
      action: z
        .enum([
          "search",
          "get_info",
          "inspect_model",
          "get_orientation_override",
          "set_orientation_override",
          "assign_model",
          "assign_material",
          "create_material",
          "set_material_property"
        ])
        .describe("The asset action to run."),
      query: z.string().optional().describe("Asset name/path search query."),
      type: z.string().optional().describe("Optional asset type filter, such as Model, Material, SoundEvent, vmdl, or vmat."),
      path: z.string().optional().describe("Asset path for get_info, inspect_model, or create_material."),
      modelPath: z.string().optional().describe("Model resource path for inspect_model or assign_model."),
      materialPath: z.string().optional().describe("Material resource path for assign_material."),
      gameObjectId: z.string().optional().describe("Target GameObject id for assign_model or assign_material."),
      componentId: z.string().optional().describe("Target ModelRenderer component id for assign_material or set_material_property."),
      name: z.string().optional().describe("Material name for create_material."),
      shader: z.string().optional().describe("Shader path for create_material."),
      color: z.string().optional().describe("Optional material tint color for create_material, such as '#aa2222' or 'red'."),
      overwrite: z.boolean().optional().describe("Allow create_material to replace an existing file."),
      property: z.string().optional().describe("Material parameter name for set_material_property."),
      value: z.any().optional().describe("Material parameter value for set_material_property."),
      scale: z
        .object({ x: z.number(), y: z.number(), z: z.number() })
        .optional()
        .describe("Optional scale to apply while inspecting model candidate bounds."),
      yaw: z.number().optional().describe("Optional yaw angle applied to inspect_model orientation candidates."),
      baseRotation: z
        .object({
          pitch: z.number().optional(),
          yaw: z.number().optional(),
          roll: z.number().optional()
        })
        .optional()
        .describe("Base model rotation for set_orientation_override."),
      groundOffsetZ: z.number().optional().describe("Optional ground offset for set_orientation_override; calculated from render bounds when omitted."),
      forwardAxis: z.string().optional().describe("Optional semantic forward axis for set_orientation_override, such as +Y."),
      confidence: z.string().optional().describe("Optional confidence label for set_orientation_override, such as human_verified."),
      source: z.string().optional().describe("Optional source label for set_orientation_override."),
      notes: z.string().optional().describe("Optional notes for set_orientation_override."),
      includeMaterials: z.boolean().optional().describe("Include model material slots in inspect_model output."),
      maxResults: z.number().int().min(1).max(500).optional().describe("Maximum search results.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`asset.${action}`, payload));
    }
  );
}
