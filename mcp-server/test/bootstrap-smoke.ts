import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface ProjectInfoResult {
  verified: {
    title: string;
    fullIdent: string;
    rootPath: string;
    assetsPath: string;
    bridgeInstalled: boolean;
  };
}

interface TabsResult {
  verified: {
    activeId: string;
    tabs: Array<{
      id: string;
      isActive: boolean;
      scene: string;
      hasSourcePath: boolean;
      sourcePath: string;
    }>;
  };
}

interface GameObjectResult {
  verified: {
    id: string;
    name: string;
    position: { x: number; y: number; z: number };
  };
}

interface SceneFindResult {
  verified: {
    count: number;
    results: Array<{
      id: string;
      name: string;
      position: { x: number; y: number; z: number };
    }>;
  };
}

interface SaveSceneAsResult {
  verified: {
    before: {
      hasSourcePath: boolean;
    };
    saveAs: {
      relativePath: string;
      existsAfter: boolean;
      length: number;
      sceneFile: {
        isValid: boolean;
        gameObjectCount: number;
      } | null;
    };
    open: {
      scene: string;
      source: {
        path: string;
        isValid: boolean;
      } | null;
    } | null;
    active: {
      sourcePath: string;
      hasSourcePath: boolean;
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 20_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_BOOTSTRAP_SCENE ?? "scenes/agent_bridge/smoke/bootstrap_smoke.scene";
const sceneName = process.env.SBOX_AGENT_BRIDGE_BOOTSTRAP_SCENE_NAME ?? "Agent Bridge Bootstrap Smoke";
const markerName = process.env.SBOX_AGENT_BRIDGE_BOOTSTRAP_MARKER ?? "Agent Bridge Bootstrap Smoke Marker";
const shouldRestore = process.env.SBOX_AGENT_BRIDGE_BOOTSTRAP_RESTORE !== "0";

let originalScenePath = "";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 10_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before bootstrap smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 20_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before bootstrap smoke");
  ensure(compileWait.verified.errorCount === 0, "editor compile status reported errors before bootstrap smoke");

  const tabsBefore = await bridge.send<TabsResult>("editor.tabs");
  originalScenePath = tabsBefore.verified.tabs.find((tab) => tab.id === tabsBefore.verified.activeId)?.sourcePath ?? "";

  const project = await bridge.send<ProjectInfoResult>("editor.project_info");
  ensure(project.verified.rootPath.length > 0, "editor.project_info did not return rootPath");
  ensure(project.verified.assetsPath.length > 0, "editor.project_info did not return assetsPath");
  ensure(project.verified.bridgeInstalled, "editor.project_info did not see the bridge installed in the active project");

  await bridge.send("editor.new_scene", {
    name: sceneName,
    discardUnsaved: true,
    bringToFront: true
  });

  const marker = await bridge.send<GameObjectResult>("gameobject.create", {
    name: markerName,
    position: { x: 32, y: -16, z: 48 }
  });
  ensure(marker.verified.name === markerName, "gameobject.create did not create the bootstrap marker");

  const saved = await bridge.send<SaveSceneAsResult>("editor.save_scene_as", {
    path: scenePath,
    overwrite: true,
    activateAfterSave: true,
    bringToFront: true
  });
  ensure(!saved.verified.before.hasSourcePath, "bootstrap scene was expected to start unsourced");
  ensure(saved.verified.saveAs.relativePath === scenePath, "editor.save_scene_as returned an unexpected relative path");
  ensure(saved.verified.saveAs.existsAfter, "editor.save_scene_as did not create the scene file");
  ensure(saved.verified.saveAs.length > 100, "saved scene file is unexpectedly small");
  ensure(saved.verified.saveAs.sceneFile?.isValid, "saved scene resource did not load as a valid SceneFile");
  ensure(saved.verified.saveAs.sceneFile.gameObjectCount >= 1, "saved scene resource did not include GameObjects");
  ensure(saved.verified.open?.source?.path === scenePath, "editor.save_scene_as did not activate the saved scene");
  ensure(saved.verified.active.hasSourcePath, "active tab did not become sourced after save_scene_as");

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved: true,
    bringToFront: true
  });

  const found = await bridge.send<SceneFindResult>("scene.find", {
    nameContains: markerName,
    maxResults: 5
  });
  ensure(found.verified.count === 1, "scene.find did not find exactly one persisted bootstrap marker");
  ensure(found.verified.results[0]?.id === marker.verified.id, "persisted marker id changed after scene reload");
  ensure(found.verified.results[0]?.position.z === 48, "persisted marker position did not survive scene reload");

  if (shouldRestore && originalScenePath) {
    await bridge.send("editor.open_scene", {
      path: originalScenePath,
      forceReload: true,
      discardUnsaved: true,
      bringToFront: true
    });
  }

  console.log(
    JSON.stringify(
      {
        ok: true,
        project: {
          title: project.verified.title,
          fullIdent: project.verified.fullIdent,
          rootPath: project.verified.rootPath,
          assetsPath: project.verified.assetsPath
        },
        scenePath,
        markerId: marker.verified.id,
        originalScenePath,
        restored: Boolean(shouldRestore && originalScenePath),
        saved: {
          length: saved.verified.saveAs.length,
          gameObjectCount: saved.verified.saveAs.sceneFile?.gameObjectCount ?? 0
        }
      },
      null,
      2
    )
  );
} catch (error) {
  if (shouldRestore && originalScenePath) {
    try {
      await bridge.send("editor.open_scene", {
        path: originalScenePath,
        forceReload: true,
        discardUnsaved: true,
        bringToFront: true
      });
    } catch {
      // Best-effort restore after a failed bootstrap smoke.
    }
  }

  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
