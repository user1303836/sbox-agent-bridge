import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { BridgeClient } from "../bridge-client.js";
import { asJsonText } from "./result.js";

const vector3Schema = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number()
});

export function registerSoundTools(server: McpServer, bridge: BridgeClient): void {
  server.tool(
    "sound",
    "List sound assets, inspect scene sound components, create sound events, assign SoundPointComponents, and preview sound events in the active s&box editor session.",
    {
      action: z.enum(["list", "get_info", "inspect", "create_event", "assign", "preview"]).describe("The sound action to run."),
      query: z.string().optional().describe("Sound asset name/path search query for list."),
      kind: z.enum(["event", "soundevent", "file", "soundfile"]).optional().describe("Optional sound kind filter for list."),
      path: z.string().optional().describe("Sound asset path for get_info or create_event."),
      soundFilePath: z.string().optional().describe("Optional SoundFile path to include in create_event."),
      eventPath: z.string().optional().describe("Sound event path for assign or preview."),
      gameObjectId: z.string().optional().describe("Target GameObject id for inspect or assign."),
      targetSession: z.string().optional().describe("Session target for read-only inspect: active, editor, runtime, playing, or game."),
      sessionId: z.string().optional().describe("Optional editor session id selector for inspect."),
      sessionIndex: z.number().int().optional().describe("Optional editor session index selector for inspect."),
      sessionPath: z.string().optional().describe("Optional scene resource path selector for inspect."),
      sessionScene: z.string().optional().describe("Optional scene name selector for inspect."),
      playOnStart: z.boolean().optional().describe("Whether assigned sound should play on start."),
      repeat: z.boolean().optional().describe("Whether assigned sound should repeat."),
      force2d: z.boolean().optional().describe("Whether assigned sound should ignore 3D spatialization."),
      volume: z.number().optional().describe("Sound event or component volume."),
      pitch: z.number().optional().describe("Sound event or component pitch."),
      decibels: z.number().int().optional().describe("Sound event decibel adjustment."),
      overwrite: z.boolean().optional().describe("Allow create_event to replace an existing sound event file."),
      position: vector3Schema.optional().describe("Optional world position for preview."),
      fadeIn: z.number().optional().describe("Preview fade-in time in seconds."),
      maxResults: z.number().int().min(1).max(500).optional().describe("Maximum list results.")
    },
    async ({ action, ...payload }) => {
      return asJsonText(await bridge.send(`sound.${action}`, payload));
    }
  );
}
