import { spawn } from "node:child_process";
import { performance } from "node:perf_hooks";

interface SuiteStep {
  name: string;
  script: string;
  env?: Record<string, string>;
}

const suiteScenePath = process.env.SBOX_AGENT_BRIDGE_MVP_SUITE_SCENE ?? "scenes/agent_bridge/smoke/mvp_suite.scene";
const commonEnv: Record<string, string> = {
  SBOX_AGENT_BRIDGE_DISCARD_UNSAVED: "1",
  SBOX_AGENT_BRIDGE_RUNTIME_SCENE: suiteScenePath,
  SBOX_AGENT_BRIDGE_MVP_SCENE: suiteScenePath
};

const steps: SuiteStep[] = [
  {
    name: "bootstrap",
    script: "test/bootstrap-smoke.ts",
    env: {
      SBOX_AGENT_BRIDGE_BOOTSTRAP_SCENE: suiteScenePath,
      SBOX_AGENT_BRIDGE_BOOTSTRAP_SCENE_NAME: "Agent Bridge MVP Suite",
      SBOX_AGENT_BRIDGE_BOOTSTRAP_MARKER: "Agent Bridge MVP Suite Marker",
      SBOX_AGENT_BRIDGE_BOOTSTRAP_RESTORE: "0"
    }
  },
  { name: "mvp", script: "test/mvp-smoke.ts" },
  { name: "assets-materials", script: "test/asset-material-smoke.ts" },
  { name: "physics", script: "test/physics-smoke.ts" },
  { name: "sounds", script: "test/sound-smoke.ts" },
  { name: "prefabs", script: "test/prefab-instance-smoke.ts" },
  { name: "capability-gaps", script: "test/capability-gap-smoke.ts" }
];

const results: Array<{ name: string; script: string; ok: boolean; elapsedMs: number; code: number | null }> = [];

try {
  for (const step of steps) {
    const started = performance.now();
    const code = await runStep(step);
    const elapsedMs = Math.round(performance.now() - started);
    results.push({ name: step.name, script: step.script, ok: code === 0, elapsedMs, code });

    if (code !== 0) {
      throw new Error(`MVP suite step '${step.name}' failed with exit code ${code}.`);
    }
  }

  console.log(
    JSON.stringify(
      {
        ok: true,
        suiteScenePath,
        steps: results
      },
      null,
      2
    )
  );
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  console.error(JSON.stringify({ ok: false, suiteScenePath, steps: results }, null, 2));
  process.exitCode = 1;
}

function runStep(step: SuiteStep): Promise<number | null> {
  const env = {
    ...process.env,
    ...commonEnv,
    ...step.env
  };

  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ["--import", "tsx", step.script], {
      cwd: process.cwd(),
      env,
      stdio: "inherit",
      windowsHide: true
    });

    child.on("error", reject);
    child.on("close", resolve);
  });
}
