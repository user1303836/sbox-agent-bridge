import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface GameObjectResult {
  verified: {
    id: string;
    name: string;
  };
}

interface SceneMetadataResult {
  verified: {
    scene: string;
    sourcePath: string;
    activeSceneMetadata: {
      count: number;
      entries: Array<{
        key: string;
        valueJson: string;
      }>;
    };
    sourceMetadata: {
      hasSource: boolean;
      title: string;
      description: string;
      readError: string;
    };
  };
}

interface RadiusResult {
  verified: {
    count: number;
    results: Array<{
      distance: number;
      gameObject: {
        id: string;
        name: string;
      };
    }>;
  };
}

interface ComponentTypesResult {
  verified: {
    count: number;
    runtimeAssemblyTotal: number;
    results: Array<{
      source: string;
      name: string;
      fullName: string;
    }>;
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath =
  process.env.SBOX_AGENT_BRIDGE_MATRIX_CORE_SCENE ??
  process.env.SBOX_AGENT_BRIDGE_MVP_SUITE_SCENE ??
  "scenes/agent_bridge/smoke/mvp_suite.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";

let nearId = "";
let farId = "";
let destroyId = "";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 10_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before matrix core smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before matrix core smoke");
  ensure(compileWait.verified.errorCount === 0, "editor compile status reported errors before matrix core smoke");

  await bridge.send("editor.recover_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    stopAll: true,
    bringToFront: true
  });

  const metadata = await bridge.send<SceneMetadataResult>("scene.metadata", {});
  ensure(metadata.verified.sourcePath === scenePath, `scene.metadata sourcePath was not ${scenePath}`);
  ensure(metadata.verified.sourceMetadata.hasSource, "scene.metadata did not report source metadata");
  ensure(!metadata.verified.sourceMetadata.readError, `scene.metadata source read failed: ${metadata.verified.sourceMetadata.readError}`);
  ensure(
    metadata.verified.activeSceneMetadata.entries.some((entry) => entry.key === "Title") ||
      metadata.verified.sourceMetadata.title.length > 0,
    "scene.metadata did not expose active or source title metadata"
  );

  const near = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Matrix Radius Near",
    position: { x: 32, y: 32, z: 0 }
  });
  nearId = near.verified.id;

  const far = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Matrix Radius Far",
    position: { x: 2048, y: 2048, z: 0 }
  });
  farId = far.verified.id;

  const radius = await bridge.send<RadiusResult>("scene.find_in_radius", {
    center: { x: 0, y: 0, z: 0 },
    radius: 128,
    nameContains: "Agent Bridge Matrix Radius",
    maxResults: 10
  });
  ensure(radius.verified.results.some((result) => result.gameObject.id === nearId), "scene.find_in_radius did not include the near object");
  ensure(!radius.verified.results.some((result) => result.gameObject.id === farId), "scene.find_in_radius included the far object");

  const componentTypes = await bridge.send<ComponentTypesResult>("component.list_types", {
    query: "AgentBridgeMutationFixture",
    includeRuntimeAssemblies: true,
    maxResults: 20
  });
  ensure(componentTypes.verified.count > 0, "component.list_types did not find AgentBridgeMutationFixture through runtime assembly scanning");
  ensure(
    componentTypes.verified.results.some((type) => type.name === "AgentBridgeMutationFixture"),
    "component.list_types returned results, but not AgentBridgeMutationFixture"
  );

  const destroyTarget = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Matrix Destroy Target",
    position: { x: 96, y: 32, z: 0 }
  });
  destroyId = destroyTarget.verified.id;

  await bridge.send("gameobject.destroy", { id: destroyId });
  await expectBridgeFailure(() => bridge.send("gameobject.get", { id: destroyId }), "destroyed object should not be readable");

  await bridge.send("editor.undo", {});
  const restored = await bridge.send<GameObjectResult>("gameobject.get", { id: destroyId });
  ensure(restored.verified.id === destroyId, "editor.undo did not restore destroyed GameObject");

  await bridge.send("editor.redo", {});
  await expectBridgeFailure(() => bridge.send("gameobject.get", { id: destroyId }), "redo-destroyed object should not be readable");
  destroyId = "";

  await cleanup();

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        compileWaitMs: compileWait.verified.elapsedMs,
        metadata: {
          sourcePath: metadata.verified.sourcePath,
          sourceTitle: metadata.verified.sourceMetadata.title,
          activeMetadataCount: metadata.verified.activeSceneMetadata.count
        },
        radius: {
          count: radius.verified.count,
          nearest: radius.verified.results[0] ?? null
        },
        componentTypes: {
          count: componentTypes.verified.count,
          runtimeAssemblyTotal: componentTypes.verified.runtimeAssemblyTotal,
          match: componentTypes.verified.results.find((type) => type.name === "AgentBridgeMutationFixture") ?? null
        },
        destroyUndoRedo: true
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
  const ids = [nearId, farId, destroyId].filter(Boolean);
  nearId = "";
  farId = "";
  destroyId = "";

  for (const id of ids) {
    try {
      await bridge.send("gameobject.destroy", { id });
    } catch {
      // Best-effort cleanup for a focused smoke script.
    }
  }
}

async function expectBridgeFailure(fn: () => Promise<unknown>, message: string): Promise<void> {
  try {
    await fn();
  } catch {
    return;
  }

  throw new Error(message);
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
