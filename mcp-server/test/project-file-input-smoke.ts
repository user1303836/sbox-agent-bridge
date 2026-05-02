import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile } from "../src/wait-helpers.js";

interface ListFilesResult {
  verified: {
    count: number;
    results: Array<{
      path: string;
      exists: boolean;
      kind: string;
    }>;
  };
}

interface ReadFileResult {
  verified: {
    byteCount: number;
    isText: boolean;
    content: string;
    file: {
      path: string;
      exists: boolean;
      sha256: string;
    };
  };
}

interface WriteFileResult {
  verified: {
    existedBefore: boolean;
    file: {
      path: string;
      exists: boolean;
      length: number;
      sha256: string;
    };
  };
}

interface DeleteFileResult {
  verified: {
    existedBefore: boolean;
    existsAfter: boolean;
    path: string;
  };
}

interface InputActionsResult {
  verified: {
    total: number;
    count: number;
    results: InputAction[];
  };
}

interface InputActionMutationResult {
  verified: {
    existedBefore: boolean;
    before: InputAction | null;
    after?: InputAction;
    existsAfter?: boolean;
  };
}

interface InputAction {
  name: string;
  groupName: string;
  title: string;
  keyboardCode: string;
  gamepadCode: string;
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const stamp = new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14);
const scratchPath = process.env.SBOX_AGENT_BRIDGE_PROJECT_SMOKE_FILE ?? "agent_bridge/smoke/project_file_smoke.txt";
const inputActionName = process.env.SBOX_AGENT_BRIDGE_PROJECT_SMOKE_INPUT ?? `AgentBridgeSmokeInput${stamp}`;

try {
  const compileWait = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before project smoke");
  ensure(compileWait.verified.errorCount === 0, "editor compile status reported errors before project smoke");

  const settings = await bridge.send<ListFilesResult>("project.list_files", {
    root: "settings",
    glob: "*.config",
    recursive: false,
    maxResults: 20
  });
  ensure(settings.verified.results.some((entry) => entry.path === "Input.config"), "project.list_files did not find ProjectSettings/Input.config");

  const inputConfig = await bridge.send<ReadFileResult>("project.read_file", {
    root: "settings",
    path: "Input.config",
    maxBytes: 64 * 1024
  });
  ensure(inputConfig.verified.isText, "project.read_file did not treat Input.config as text");
  ensure(inputConfig.verified.content.includes("\"Actions\""), "project.read_file did not return Input.config JSON content");

  const content = `project file smoke ${stamp}\n`;
  const write = await bridge.send<WriteFileResult>("project.write_file", {
    root: "assets",
    path: scratchPath,
    content,
    overwrite: true
  });
  ensure(write.verified.file.exists, "project.write_file did not report the scratch file as existing");

  const read = await bridge.send<ReadFileResult>("project.read_file", {
    root: "assets",
    path: scratchPath
  });
  ensure(read.verified.content === content, "project.read_file did not read back the scratch file content");
  ensure(read.verified.file.sha256 === write.verified.file.sha256, "project.read_file SHA did not match write_file SHA");

  const listedScratch = await bridge.send<ListFilesResult>("project.list_files", {
    root: "assets",
    glob: "project_file_smoke.txt",
    recursive: true,
    maxResults: 20
  });
  ensure(listedScratch.verified.results.some((entry) => entry.path === scratchPath), "project.list_files did not find the scratch file");

  const existingAttack = await bridge.send<InputActionsResult>("project.input_actions", {
    query: "Attack1"
  });
  ensure(existingAttack.verified.count >= 1, "project.input_actions did not find the built-in Attack1 action");

  await bridge.send<InputActionMutationResult>("project.remove_input_action", {
    name: inputActionName
  });

  const created = await bridge.send<InputActionMutationResult>("project.upsert_input_action", {
    name: inputActionName,
    groupName: "Agent Bridge",
    title: "Project Smoke Input",
    keyboardCode: "k",
    gamepadCode: "None"
  });
  ensure(!created.verified.existedBefore, "project.upsert_input_action reported the unique smoke input already existed");
  ensure(created.verified.after?.keyboardCode === "k", "project.upsert_input_action did not set keyboardCode on create");

  const queriedCreated = await bridge.send<InputActionsResult>("project.input_actions", {
    query: inputActionName
  });
  ensure(queriedCreated.verified.count === 1, "project.input_actions did not find the created smoke input");

  const updated = await bridge.send<InputActionMutationResult>("project.upsert_input_action", {
    name: inputActionName,
    keyboardCode: "l",
    gamepadCode: "A"
  });
  ensure(updated.verified.existedBefore, "project.upsert_input_action did not report update of the smoke input");
  ensure(updated.verified.after?.keyboardCode === "l", "project.upsert_input_action did not update keyboardCode");
  ensure(updated.verified.after?.gamepadCode === "A", "project.upsert_input_action did not update gamepadCode");

  const removed = await bridge.send<InputActionMutationResult>("project.remove_input_action", {
    name: inputActionName
  });
  ensure(removed.verified.existedBefore, "project.remove_input_action did not remove the smoke input");
  ensure(removed.verified.existsAfter === false, "project.remove_input_action left the smoke input present");

  const deleted = await bridge.send<DeleteFileResult>("project.delete_file", {
    root: "assets",
    path: scratchPath
  });
  ensure(deleted.verified.existedBefore, "project.delete_file did not report the scratch file existed");
  ensure(!deleted.verified.existsAfter, "project.delete_file left the scratch file present");

  console.log(
    JSON.stringify(
      {
        ok: true,
        compileWaitMs: compileWait.verified.elapsedMs,
        settingsFiles: settings.verified.count,
        inputActions: {
          total: existingAttack.verified.total,
          attack1Count: existingAttack.verified.count,
          smokeAction: inputActionName,
          created: created.verified.after,
          updated: updated.verified.after,
          removed: true
        },
        fileOps: {
          path: scratchPath,
          writeSha256: write.verified.file.sha256,
          readBytes: read.verified.byteCount,
          listed: true,
          deleted: true
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
  try {
    await bridge.send("project.remove_input_action", { name: inputActionName });
  } catch {
    // Best-effort cleanup.
  }

  try {
    await bridge.send("project.delete_file", { root: "assets", path: scratchPath });
  } catch {
    // Best-effort cleanup.
  }
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
