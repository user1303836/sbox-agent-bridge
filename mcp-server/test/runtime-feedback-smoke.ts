import { BridgeClient } from "../src/bridge-client.js";

interface SceneSummaryResult {
  verified: {
    objectCount: number;
    componentCount: number;
    targetSession?: {
      resolvedTarget: string;
      session: {
        isGameSession: boolean;
      };
    };
  };
}

interface RuntimeListResult {
  verified: {
    count: number;
    components: Array<{
      component: {
        id: string;
        type: string;
      };
      actions: string[];
      propertyProtocol: {
        canRun: boolean;
      };
    }>;
  };
}

interface RuntimeRunResult {
  verified: {
    invocationMode: string;
    result: {
      hud?: {
        root: boolean;
        healthOrb: boolean;
        energyOrb: boolean;
        inventoryOpen: boolean;
      };
      player?: {
        health: number;
        maxHealth: number;
        healthPercent: number;
      };
      combat?: {
        zombieCount: number;
      };
      skills?: unknown[];
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 10_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "scenes/minimal.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";

try {
  await bridge.send("editor.stop", { stopAll: true });
  await delay(500);

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    bringToFront: true
  });

  const editorSummary = await bridge.send<SceneSummaryResult>("scene.summary");
  ensure(editorSummary.verified.objectCount > 0, "editor scene has no objects after open_scene");

  await bridge.send("editor.play");
  await delay(1_000);

  const runtimeSummary = await bridge.send<SceneSummaryResult>("scene.summary", {
    targetSession: "runtime"
  });
  ensure(runtimeSummary.verified.targetSession?.resolvedTarget === "gameSession", "runtime summary did not target a GameSession");
  ensure(runtimeSummary.verified.targetSession?.session.isGameSession === true, "runtime summary target is not marked as a game session");
  ensure(runtimeSummary.verified.objectCount > 0, "runtime scene has no objects");

  const actions = await bridge.send<RuntimeListResult>("runtime.list_test_actions", {
    componentType: "ArpgDemoController"
  });
  ensure(actions.verified.count === 1, "expected exactly one ArpgDemoController runtime test-action component");
  ensure(actions.verified.components[0]?.propertyProtocol.canRun === true, "ARPG runtime component does not expose the property protocol");
  ensure(actions.verified.components[0]?.actions.includes("arpg.ui_state"), "ARPG runtime actions did not include arpg.ui_state");

  const state = await runArpg("arpg.ui_state");
  ensure(state.verified.invocationMode === "propertyProtocol", "ARPG runtime action did not use the property protocol");
  ensure(Array.isArray(state.verified.result.skills) && state.verified.result.skills.length >= 4, "ARPG state did not report skills");

  const opened = await runArpg("arpg.open_inventory");
  ensure(opened.verified.result.hud?.inventoryOpen === true, "arpg.open_inventory did not report inventoryOpen=true");

  const damaged = await runArpg("arpg.damage_player", { amount: 17 });
  ensure(damaged.verified.result.player?.health === 83, "arpg.damage_player did not reduce health to 83");

  const restored = await runArpg("arpg.restore_player");
  ensure(
    restored.verified.result.player?.health === restored.verified.result.player?.maxHealth,
    "arpg.restore_player did not restore health"
  );

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        editorObjects: editorSummary.verified.objectCount,
        runtimeObjects: runtimeSummary.verified.objectCount,
        runtimeComponents: runtimeSummary.verified.componentCount,
        arpgActions: actions.verified.components[0]?.actions,
        hud: restored.verified.result.hud,
        zombieCount: restored.verified.result.combat?.zombieCount
      },
      null,
      2
    )
  );
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function runArpg(testAction: string, payload: Record<string, unknown> = {}): Promise<RuntimeRunResult> {
  return bridge.send<RuntimeRunResult>("runtime.run_test_action", {
    componentType: "ArpgDemoController",
    testAction,
    payload
  });
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}

async function delay(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}
