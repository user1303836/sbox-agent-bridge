import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForRuntime, waitForStopped } from "../src/wait-helpers.js";

interface MaterialInspectResult {
  verified: {
    path: string;
    source: {
      exists: boolean;
      propertyCount: number;
      textures: Array<{ key: string; value: string }>;
      colors: Array<{ key: string; value: string }>;
    };
  };
}

interface MaterialSourceSetResult {
  verified: {
    property: string;
    value: string;
    replaced: boolean;
    after: MaterialInspectResult["verified"];
  };
}

interface PreviewModelResult {
  verified: {
    path: string;
    byteCount: number;
    luminance: {
      average: number;
      max: number;
      darkPixelRatio: number;
    };
    targetSession: {
      resolvedTarget: string;
    };
    previewRig: {
      root: {
        name: string;
      };
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "scenes/minimal.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";
const materialPath = process.env.SBOX_AGENT_BRIDGE_ASSET_SMOKE_MATERIAL ?? "materials/agent_bridge/smoke/asset_material_smoke.vmat";
const modelPath = process.env.SBOX_AGENT_BRIDGE_ASSET_SMOKE_MODEL ?? "models/dev/box.vmdl";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const initialStopped = await waitForStopped(bridge, { timeoutMs: 5_000 });
  ensure(initialStopped.verified.satisfied, "editor.wait_stopped did not settle before asset smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before asset smoke");

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    bringToFront: true
  });

  await bridge.send("asset.create_material", {
    path: materialPath,
    name: "asset_material_smoke",
    color: "#6b2418",
    overwrite: true
  });

  const inspected = await bridge.send<MaterialInspectResult>("asset.inspect_material", { materialPath });
  ensure(inspected.verified.source.exists, "asset.inspect_material did not find the material source file");
  ensure(inspected.verified.source.propertyCount >= 8, "asset.inspect_material reported too few source properties");
  ensure(
    inspected.verified.source.textures.some((property) => property.key === "TextureColor"),
    "asset.inspect_material did not report TextureColor"
  );

  const tint = await bridge.send<MaterialSourceSetResult>("asset.set_material_source_property", {
    materialPath,
    property: "g_vColorTint",
    value: { r: 0.35, g: 0.08, b: 0.045, a: 1 }
  });
  ensure(tint.verified.value === "[0.35 0.08 0.045 1]", "set_material_source_property did not format color vector");

  const texture = await bridge.send<MaterialSourceSetResult>("asset.set_material_source_property", {
    materialPath,
    property: "TextureColor",
    value: { path: "materials/default/default_color.tga" }
  });
  ensure(texture.verified.value === "materials/default/default_color.tga", "set_material_source_property did not format texture path");

  await bridge.send("editor.play");
  const runtimeWait = await waitForRuntime(bridge, { timeoutMs: 10_000, minObjects: 1 });
  ensure(runtimeWait.verified.satisfied, "editor.wait_runtime did not settle before preview_model");

  const preview = await bridge.send<PreviewModelResult>("asset.preview_model", {
    targetSession: "runtime",
    modelPath,
    materialPath,
    width: 320,
    height: 240,
    name: "asset-material-smoke"
  });
  ensure(preview.verified.targetSession.resolvedTarget === "gameSession", "asset.preview_model did not target the runtime GameSession");
  ensure(preview.verified.byteCount > 1000, "asset.preview_model produced an unexpectedly small PNG");
  ensure(preview.verified.luminance.average > 0.02, "asset.preview_model produced a near-black image");
  ensure(preview.verified.luminance.max > 0.1, "asset.preview_model produced no bright pixels");

  await bridge.send("editor.stop", { stopAll: true });
  const finalStopped = await waitForStopped(bridge, { timeoutMs: 5_000 });
  ensure(finalStopped.verified.satisfied, "editor.wait_stopped did not settle after asset smoke");

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        materialPath,
        modelPath,
        compileWaitMs: compileWait.verified.elapsedMs,
        runtimeWaitMs: runtimeWait.verified.elapsedMs,
        materialPropertyCount: inspected.verified.source.propertyCount,
        tint: tint.verified.value,
        texture: texture.verified.value,
        preview: {
          path: preview.verified.path,
          byteCount: preview.verified.byteCount,
          luminance: preview.verified.luminance,
          rig: preview.verified.previewRig.root.name
        }
      },
      null,
      2
    )
  );
} catch (error) {
  try {
    await bridge.send("editor.stop", { stopAll: true });
  } catch {
    // Best-effort cleanup for a focused smoke script.
  }

  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
