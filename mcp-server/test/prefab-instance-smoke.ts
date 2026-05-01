import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface GameObjectResult {
  verified: {
    id: string;
    name: string;
  };
}

interface PrefabCreateResult {
  verified: {
    prefab: {
      hasRootObject: boolean;
    };
    bindSource: boolean;
  };
}

interface PrefabInfoResult {
  verified: {
    hasRootObject: boolean;
  };
}

interface PrefabInstantiateResult {
  verified: {
    gameObject: {
      id: string;
      name: string;
    };
  };
}

interface PrefabInstanceInspectResult {
  verified: {
    instance: {
      isPrefabInstance: boolean;
      prefabPath: string;
      patch: {
        exists: boolean;
        addedObjectCount: number;
        removedObjectCount: number;
        propertyOverrideCount: number;
        movedObjectCount: number;
        propertyOverrideSamples: Array<{
          property: string;
          value: unknown;
        }>;
      };
      prefabIdToInstanceId: {
        count: number;
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
const prefabPath = process.env.SBOX_AGENT_BRIDGE_PREFAB_SMOKE_PATH ?? "prefabs/agent_bridge/smoke/prefab_instance_smoke.prefab";
const modelPath = process.env.SBOX_AGENT_BRIDGE_PREFAB_SMOKE_MODEL ?? "models/dev/box.vmdl";

let sourceId = "";
let instanceId = "";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 5_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before prefab smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before prefab smoke");

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    bringToFront: true
  });

  const source = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Prefab Smoke Source",
    position: { x: -96, y: 128, z: 32 }
  });
  sourceId = source.verified.id;

  await bridge.send("asset.assign_model", {
    gameObjectId: sourceId,
    modelPath
  });

  const created = await bridge.send<PrefabCreateResult>("prefab.create", {
    gameObjectId: sourceId,
    path: prefabPath,
    overwrite: true,
    bindSource: true,
    showInMenu: true,
    menuPath: "Agent Bridge/Smoke",
    menuIcon: "inventory_2"
  });
  ensure(created.verified.prefab.hasRootObject, "prefab.create did not write a root object");
  ensure(created.verified.bindSource === true, "prefab.create did not report bindSource=true");

  const info = await bridge.send<PrefabInfoResult>("prefab.get_info", { path: prefabPath });
  ensure(info.verified.hasRootObject, "prefab.get_info did not load the smoke prefab root object");

  const sourceInspect = await bridge.send<PrefabInstanceInspectResult>("prefab.inspect_instance", {
    gameObjectId: sourceId,
    maxSamples: 10
  });
  ensure(sourceInspect.verified.instance.isPrefabInstance, "bound source was not reported as a prefab instance");
  ensure(
    normalizePath(sourceInspect.verified.instance.prefabPath) === normalizePath(prefabPath),
    "bound source prefab path did not match the created prefab"
  );

  const instance = await bridge.send<PrefabInstantiateResult>("prefab.instantiate", {
    path: prefabPath,
    name: "Agent Bridge Prefab Smoke Instance",
    position: { x: -64, y: 96, z: 48 },
    rotation: { yaw: 45 },
    scale: { x: 1, y: 1, z: 1 }
  });
  instanceId = instance.verified.gameObject.id;

  const instantiated = await bridge.send<PrefabInstanceInspectResult>("prefab.inspect_instance", {
    gameObjectId: instanceId,
    maxSamples: 20
  });
  ensure(instantiated.verified.instance.isPrefabInstance, "instantiated prefab was not reported as a prefab instance");
  ensure(instantiated.verified.instance.prefabIdToInstanceId.count > 0, "prefab instance id map was empty");

  await bridge.send("gameobject.set_transform", {
    id: instanceId,
    position: { x: -32, y: 96, z: 64 },
    rotation: { yaw: 90 }
  });

  const patched = await bridge.send<PrefabInstanceInspectResult>("prefab.inspect_instance", {
    gameObjectId: instanceId,
    maxSamples: 20
  });
  ensure(patched.verified.instance.patch.exists, "prefab instance patch was not present after transform override");
  ensure(patched.verified.instance.patch.propertyOverrideCount > 0, "prefab instance patch reported no property overrides");
  ensure(
    patched.verified.instance.patch.propertyOverrideSamples.some((sample) => sample.property === "Position" || sample.property === "Rotation"),
    "prefab instance patch did not include transform override samples"
  );

  await cleanup();

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        prefabPath,
        modelPath,
        compileWaitMs: compileWait.verified.elapsedMs,
        sourcePrefabPath: sourceInspect.verified.instance.prefabPath,
        instantiatedIdMapCount: instantiated.verified.instance.prefabIdToInstanceId.count,
        patch: {
          addedObjectCount: patched.verified.instance.patch.addedObjectCount,
          removedObjectCount: patched.verified.instance.patch.removedObjectCount,
          propertyOverrideCount: patched.verified.instance.patch.propertyOverrideCount,
          movedObjectCount: patched.verified.instance.patch.movedObjectCount,
          propertyOverrideSamples: patched.verified.instance.patch.propertyOverrideSamples
        }
      },
      null,
      2
    )
  );
} catch (error) {
  await cleanup();
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function cleanup(): Promise<void> {
  const ids = [instanceId, sourceId].filter(Boolean);
  instanceId = "";
  sourceId = "";

  for (const id of ids) {
    try {
      await bridge.send("gameobject.destroy", { id });
    } catch {
      // Best-effort cleanup for a focused smoke script.
    }
  }
}

function normalizePath(path: string): string {
  return path.replace(/\\/g, "/").replace(/^assets\//i, "").toLowerCase();
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
