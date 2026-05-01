import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForRuntime, waitForStopped } from "../src/wait-helpers.js";

interface CompileStatusResult {
  verified: {
    groups: Array<{
      sequence: number;
    }>;
  };
}

interface FindResult {
  verified: {
    count: number;
    results: Array<{
      id: string;
      name: string;
    }>;
  };
}

interface GameObjectResult {
  verified: {
    id: string;
  };
}

interface ComponentAddResult {
  verified: {
    creationMode: string;
    component: {
      id: string;
      type: string;
      fullType: string;
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

interface BoxingState {
  phase: string;
  round: number;
  lastEvent: string;
  winner: string;
  player: BoxerSnapshot;
  opponent: BoxerSnapshot;
  controls: string[];
  bridgeVerified: boolean;
}

interface BoxerSnapshot {
  health: number;
  stamina: number;
  guard: number;
  totalScore: number;
  knockdowns: number;
  punchesThrown: number;
  punchesLanded: number;
  blocking: boolean;
  dodging: boolean;
  down: boolean;
  winner: boolean;
}

interface RuntimeRunResult {
  verified: {
    invocationMode: string;
    result: BoxingState;
  };
}

interface CaptureResult {
  verified: {
    path: string;
    byteCount: number;
    luminance: {
      average: number;
      max: number;
      darkPixelRatio: number;
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 20_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_BOXING_SCENE ?? process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "scenes/minimal.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";
const controllerName = process.env.SBOX_AGENT_BRIDGE_BOXING_CONTROLLER_NAME ?? "Agent Bridge Boxing POC Controller";
const scriptPath = "BoxingDemo/BoxingDemoController.cs";
const fixturePath = fileURLToPath(new URL("./fixtures/BoxingDemoController.cs", import.meta.url));
let runtimeComponentId = "";

const weakSpots = [
  "No bridge action creates a brand-new s&box project or switches the open editor project; this walkthrough uses the currently open project.",
  "No create_scene/save_as_scene action exists yet; this walkthrough recovers an existing saved scene and adds an isolated controller object.",
  "Script authoring is full-file replacement only; a structured patch/edit API would be safer for large gameplay scripts.",
  "Runtime verification uses explicit AgentBridge test actions because the bridge cannot synthesize OS keypresses or assert real player input yet.",
  "visual.capture_camera defaults can select an existing scene camera; the walkthrough resolves the generated camera by name and captures by GameObject id.",
  "Property-protocol test components should ignore empty AgentBridgeTestAction assignments because scene deserialization can replay serialized empty values."
];

try {
  const content = await readFile(fixturePath, "utf8");

  await bridge.send("bridge.doctor", {
    mcpServerVersion: "0.1.0",
    maxLines: 20,
    maxDiagnostics: 20
  });

  await bridge.send("editor.stop", { stopAll: true });
  const stoppedBefore = await waitForStopped(bridge, { timeoutMs: 10_000, requireNoGameSessions: true });
  ensure(stoppedBefore.verified.satisfied, "editor.wait_stopped did not settle before boxing walkthrough");

  await bridge.send("editor.recover_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    stopAll: true,
    bringToFront: true
  });

  const beforeCompile = await bridge.send<CompileStatusResult>("editor.compile_status", { maxDiagnostics: 5 });
  const beforeSequence = latestSequence(beforeCompile);

  await bridge.send("script.create", {
    path: scriptPath,
    content,
    overwrite: true
  });

  const compileWait = await waitForCompile(bridge, {
    timeoutMs: 45_000,
    maxDiagnostics: 30,
    sinceSequence: beforeSequence
  });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not observe a post-script compile");
  ensure(compileWait.verified.errorCount === 0, "boxing controller compile reported errors");

  await bridge.send("editor.recover_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    stopAll: true,
    bringToFront: true
  });

  const existing = await bridge.send<FindResult>("scene.find", {
    nameContains: controllerName,
    maxResults: 10
  });
  for (const result of existing.verified.results) {
    await destroyBestEffort(result.id);
  }

  const controllerObject = await bridge.send<GameObjectResult>("gameobject.create", {
    name: controllerName,
    position: { x: 0, y: 0, z: 0 }
  });

  const controller = await bridge.send<ComponentAddResult>("component.add", {
    gameObjectId: controllerObject.verified.id,
    type: "BoxingDemoController",
    startEnabled: true
  });
  ensure(controller.verified.component.type === "BoxingDemoController", "component.add did not create BoxingDemoController");
  runtimeComponentId = controller.verified.component.id;

  await bridge.send("component.set_property", {
    id: controller.verified.component.id,
    property: "RunInEditorForBridge",
    value: true
  });
  await bridge.send("component.set_property", {
    id: controller.verified.component.id,
    property: "RoundDuration",
    value: 45
  });
  await bridge.send("component.set_property", {
    id: controller.verified.component.id,
    property: "MaxRounds",
    value: 3
  });

  await bridge.send("editor.save_scene");

  await bridge.send("editor.play");
  const runtimeWait = await waitForRuntime(bridge, { timeoutMs: 15_000, minObjects: 1 });
  ensure(runtimeWait.verified.satisfied, "editor.wait_runtime did not resolve a boxing GameSession");

  const actions = await bridge.send<RuntimeListResult>("runtime.list_test_actions", {
    componentType: "BoxingDemoController"
  });
  const listedComponent = actions.verified.components.find((entry) => entry.component.id === runtimeComponentId) ?? actions.verified.components[0];
  ensure(listedComponent !== undefined, "expected at least one BoxingDemoController runtime test component");
  ensure(listedComponent.propertyProtocol.canRun === true, "BoxingDemoController does not expose the property runtime protocol");
  ensure(listedComponent.actions.includes("boxing.state"), "boxing.state action was not listed");
  ensure(listedComponent.actions.includes("boxing.force_decision"), "boxing.force_decision action was not listed");

  const initial = await runBoxing("boxing.state");
  ensure(initial.verified.result.phase === "Fighting", "boxing game did not start in Fighting phase");
  ensure(initial.verified.result.controls.length >= 5, "boxing state did not expose controls");

  const jab = await runBoxing("boxing.jab");
  ensure(jab.verified.result.player.punchesThrown >= 1, "boxing.jab did not increment player punches");
  ensure(jab.verified.result.opponent.health < initial.verified.result.opponent.health, "boxing.jab did not damage the opponent");

  const blocked = await runBoxing("boxing.block", { seconds: 1.2 });
  ensure(blocked.verified.result.player.blocking, "boxing.block did not set player blocking state");

  const dodged = await runBoxing("boxing.dodge", { direction: -1 });
  ensure(dodged.verified.result.player.dodging || dodged.verified.result.player.stamina < blocked.verified.result.player.stamina, "boxing.dodge did not set dodge state or consume stamina");

  const knockdown = await runBoxing("boxing.damage_opponent", { amount: 130 });
  ensure(knockdown.verified.result.opponent.knockdowns >= 1 || knockdown.verified.result.phase === "Finished", "boxing.damage_opponent did not trigger a knockdown/TKO path");

  await runBoxing("boxing.reset");
  await runBoxing("boxing.force_decision");
  const decision = await runBoxing("boxing.state");
  ensure(decision.verified.result.phase === "Finished", "boxing.force_decision did not finish the match");

  const cameraFind = await bridge.send<FindResult>("scene.find", {
    targetSession: "runtime",
    nameContains: "Broadcast camera",
    maxResults: 5
  });
  ensure(cameraFind.verified.count >= 1, "could not find the boxing Broadcast camera in the runtime scene");
  const cameraObject = cameraFind.verified.results[0]!;

  const capture = await bridge.send<CaptureResult>("visual.capture_camera", {
    targetSession: "runtime",
    gameObjectId: cameraObject.id,
    width: 480,
    height: 300,
    name: "boxing-poc"
  });
  ensure(capture.verified.byteCount > 1000, "boxing camera capture produced an unexpectedly small PNG");
  ensure(capture.verified.luminance.average > 0.02, "boxing camera capture was near black");
  ensure(capture.verified.luminance.max > 0.1, "boxing camera capture had no bright pixels");

  await bridge.send("editor.stop", { stopAll: true });
  const stoppedAfter = await waitForStopped(bridge, { timeoutMs: 10_000, requireNoGameSessions: true });
  ensure(stoppedAfter.verified.satisfied, "editor.wait_stopped did not settle after boxing walkthrough");

  const finalDoctor = await bridge.send<{ verified: { overall: string } }>("bridge.doctor", {
    mcpServerVersion: "0.1.0",
    maxLines: 20,
    maxDiagnostics: 20
  });
  ensure(finalDoctor.verified.overall !== "fail", "final bridge.doctor failed");

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        controllerName,
        scriptPath,
        componentCreationMode: controller.verified.creationMode,
        compileWaitMs: compileWait.verified.elapsedMs,
        runtimeWaitMs: runtimeWait.verified.elapsedMs,
        runtimeActionComponentCount: actions.verified.count,
        runtimeActions: listedComponent.actions,
        jab: {
          playerPunchesThrown: jab.verified.result.player.punchesThrown,
          opponentHealth: jab.verified.result.opponent.health
        },
        knockdown: {
          phase: knockdown.verified.result.phase,
          opponentKnockdowns: knockdown.verified.result.opponent.knockdowns,
          lastEvent: knockdown.verified.result.lastEvent
        },
        decision: {
          phase: decision.verified.result.phase,
          winner: decision.verified.result.winner,
          lastEvent: decision.verified.result.lastEvent
        },
        cameraObject,
        capture: capture.verified,
        finalDoctor: finalDoctor.verified.overall,
        weakSpots
      },
      null,
      2
    )
  );
} catch (error) {
  try {
    await bridge.send("editor.stop", { stopAll: true });
    await waitForStopped(bridge, { timeoutMs: 5_000, requireNoGameSessions: true });
  } catch {
    // Best-effort cleanup for a live editor walkthrough.
  }

  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function runBoxing(testAction: string, payload: Record<string, unknown> = {}): Promise<RuntimeRunResult> {
  const result = await bridge.send<RuntimeRunResult>("runtime.run_test_action", {
    componentId: runtimeComponentId,
    testAction,
    payload
  });
  ensure(result.verified.invocationMode === "propertyProtocol", `${testAction} did not use the propertyProtocol runtime path`);
  ensure(result.verified.result.bridgeVerified === true, `${testAction} did not return a verified boxing state`);
  return result;
}

async function destroyBestEffort(id: string): Promise<void> {
  try {
    await bridge.send("gameobject.destroy", { id });
  } catch {
    try {
      await bridge.send("gameobject.set_enabled", { id, enabled: false });
    } catch {
      // Leave stale test objects alone if the editor is already in a bad state.
    }
  }
}

function latestSequence(status: CompileStatusResult): number | undefined {
  const sequences = status.verified.groups.map((group) => group.sequence ?? 0);
  return sequences.length > 0 ? Math.max(...sequences) : undefined;
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
