import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForRuntime, waitForStopped } from "../src/wait-helpers.js";

interface DoctorResult {
  verified: {
    overall: "pass" | "warn" | "fail";
    nextSuggestedAction: string;
  };
}

interface SceneSummaryResult {
  verified: {
    objectCount: number;
  };
}

interface GameObjectResult {
  verified: {
    id: string;
  };
}

interface PrefabInstantiateResult {
  verified: {
    gameObject: {
      id: string;
    };
  };
}

interface PrefabInstanceInspectResult {
  verified: {
    instance: {
      isPrefabInstance: boolean;
      prefabIdToInstanceId: {
        count: number;
      };
    };
  };
}

interface PhysicsInspectResult {
  verified: {
    rigidbodies: unknown[];
    colliders: unknown[];
  };
}

interface SoundInspectResult {
  verified: {
    count: number;
  };
}

interface PreviewModelResult {
  verified: {
    byteCount: number;
    luminance: {
      average: number;
      max: number;
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_MVP_SCENE ?? process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";
const materialPath = process.env.SBOX_AGENT_BRIDGE_MVP_MATERIAL ?? "materials/agent_bridge/smoke/mvp_smoke.vmat";
const prefabPath = process.env.SBOX_AGENT_BRIDGE_MVP_PREFAB ?? "prefabs/agent_bridge/smoke/mvp_smoke.prefab";
const soundEventPath = process.env.SBOX_AGENT_BRIDGE_MVP_SOUND_EVENT ?? "sounds/agent_bridge/smoke/mvp_smoke.sound";
const modelPath = process.env.SBOX_AGENT_BRIDGE_MVP_MODEL ?? "models/dev/box.vmdl";
const soundFilePath = process.env.SBOX_AGENT_BRIDGE_MVP_SOUND_FILE ?? "sounds/ambience/cave-loop.vsnd";

let sourceId = "";
let prefabInstanceId = "";

try {
  const initialDoctor = await bridge.send<DoctorResult>("bridge.doctor", {
    mcpServerVersion: "0.1.0",
    maxLines: 20,
    maxDiagnostics: 10
  });
  ensure(initialDoctor.verified.overall !== "fail", `bridge.doctor failed: ${initialDoctor.verified.nextSuggestedAction}`);

  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 10_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before MVP smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before MVP smoke");
  ensure(compileWait.verified.errorCount === 0, "editor compile status reported errors before MVP smoke");

  if (scenePath) {
    await bridge.send("editor.recover_scene", {
      path: scenePath,
      discardUnsaved,
      forceReload: true,
      stopAll: true,
      bringToFront: true
    });
  } else {
    await bridge.send("editor.recover_scene", {
      discardUnsaved,
      forceReload: true,
      stopAll: true,
      bringToFront: true
    });
  }

  const summary = await bridge.send<SceneSummaryResult>("scene.summary");
  ensure(summary.verified.objectCount > 0, "MVP smoke scene has no objects");

  const source = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge MVP Smoke Source",
    position: { x: 512, y: -512, z: 80 }
  });
  sourceId = source.verified.id;

  await bridge.send("asset.assign_model", { gameObjectId: sourceId, modelPath });
  await bridge.send("asset.create_material", {
    path: materialPath,
    name: "mvp_smoke",
    color: "#315a7d",
    overwrite: true
  });
  await bridge.send("asset.assign_material", { gameObjectId: sourceId, materialPath });

  await bridge.send("physics.add_physics", {
    gameObjectId: sourceId,
    gravity: false,
    motionEnabled: false,
    mass: 10
  });
  await bridge.send("physics.add_collider", {
    gameObjectId: sourceId,
    type: "box",
    scale: { x: 48, y: 48, z: 48 },
    static: true,
    trigger: false
  });
  const physics = await bridge.send<PhysicsInspectResult>("physics.inspect", { gameObjectId: sourceId });
  ensure(physics.verified.rigidbodies.length === 1, "MVP smoke physics.inspect did not report a Rigidbody");
  ensure(physics.verified.colliders.length === 1, "MVP smoke physics.inspect did not report a Collider");

  await bridge.send("sound.create_event", {
    path: soundEventPath,
    soundFilePath,
    overwrite: true,
    volume: 0.25,
    pitch: 1
  });
  await bridge.send("sound.assign", {
    gameObjectId: sourceId,
    eventPath: soundEventPath,
    playOnStart: false,
    repeat: false,
    force2d: true,
    volume: 0.25,
    pitch: 1
  });
  const sound = await bridge.send<SoundInspectResult>("sound.inspect", { gameObjectId: sourceId });
  ensure(sound.verified.count === 1, "MVP smoke sound.inspect did not report a SoundPointComponent");

  await bridge.send("prefab.create", {
    gameObjectId: sourceId,
    path: prefabPath,
    overwrite: true,
    bindSource: true
  });
  const prefabInstance = await bridge.send<PrefabInstantiateResult>("prefab.instantiate", {
    path: prefabPath,
    name: "Agent Bridge MVP Smoke Prefab Instance",
    position: { x: 576, y: -512, z: 80 }
  });
  prefabInstanceId = prefabInstance.verified.gameObject.id;
  const prefabInspect = await bridge.send<PrefabInstanceInspectResult>("prefab.inspect_instance", {
    gameObjectId: prefabInstanceId,
    maxSamples: 10
  });
  ensure(prefabInspect.verified.instance.isPrefabInstance, "MVP smoke prefab instance was not linked");
  ensure(prefabInspect.verified.instance.prefabIdToInstanceId.count > 0, "MVP smoke prefab instance id map was empty");

  await bridge.send("editor.play");
  const runtimeWait = await waitForRuntime(bridge, { timeoutMs: 10_000, minObjects: 1 });
  ensure(runtimeWait.verified.satisfied, "editor.wait_runtime did not settle during MVP smoke");

  const preview = await bridge.send<PreviewModelResult>("asset.preview_model", {
    targetSession: "runtime",
    modelPath,
    materialPath,
    width: 320,
    height: 240,
    name: "mvp-smoke"
  });
  ensure(preview.verified.byteCount > 1000, "MVP smoke preview_model produced an unexpectedly small PNG");
  ensure(preview.verified.luminance.average > 0.02, "MVP smoke preview_model produced a near-black PNG");
  ensure(preview.verified.luminance.max > 0.1, "MVP smoke preview_model produced no bright pixels");

  await bridge.send("editor.stop", { stopAll: true });
  const finalStopped = await waitForStopped(bridge, { timeoutMs: 10_000 });
  ensure(finalStopped.verified.satisfied, "editor.wait_stopped did not settle after MVP smoke");

  await cleanup();

  const finalDoctor = await bridge.send<DoctorResult>("bridge.doctor", {
    mcpServerVersion: "0.1.0",
    maxLines: 20,
    maxDiagnostics: 10
  });
  ensure(finalDoctor.verified.overall !== "fail", `final bridge.doctor failed: ${finalDoctor.verified.nextSuggestedAction}`);

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath: scenePath || "active sourced scene",
        compileWaitMs: compileWait.verified.elapsedMs,
        editorObjects: summary.verified.objectCount,
        materialPath,
        prefabPath,
        soundEventPath,
        preview: preview.verified,
        finalDoctor: finalDoctor.verified.overall
      },
      null,
      2
    )
  );
} catch (error) {
  try {
    await bridge.send("editor.stop", { stopAll: true });
  } catch {
    // Best-effort recovery for a focused smoke script.
  }

  await cleanup();
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function cleanup(): Promise<void> {
  const ids = [prefabInstanceId, sourceId].filter(Boolean);
  prefabInstanceId = "";
  sourceId = "";

  for (const id of ids) {
    try {
      await bridge.send("gameobject.destroy", { id });
    } catch {
      try {
        await bridge.send("gameobject.set_enabled", { id, enabled: false });
      } catch {
        // Best-effort cleanup for MVP smoke.
      }
    }
  }
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
