import assert from "node:assert/strict";
import test from "node:test";
import type { BridgeClient } from "../src/bridge-client.js";
import { registerEditorTools } from "../src/tools/editor.js";
import { waitForCompile, waitForRuntime, waitForStopped } from "../src/wait-helpers.js";

test("waitForCompile waits for a newer settled compile sequence", async () => {
  let calls = 0;
  const bridge = fakeBridge(async (action) => {
    assert.equal(action, "editor.compile_status");
    calls += 1;

    return {
      verified: {
        observedGroupCount: 1,
        groups: [
          {
            sequence: calls === 1 ? 1 : 2,
            isBuilding: calls === 1,
            needsBuild: false,
            buildSuccess: calls > 1,
            errorCount: 0,
            compilers: []
          }
        ]
      }
    };
  });

  const result = await waitForCompile(bridge, {
    sinceSequence: 1,
    timeoutMs: 500,
    pollMs: 25
  });

  assert.equal(result.verified.satisfied, true);
  assert.equal(result.verified.latestSequence, 2);
  assert.equal(result.verified.attempts, 2);
});

test("waitForRuntime polls through transient missing GameSession state", async () => {
  let playStateCalls = 0;
  const bridge = fakeBridge(async (action) => {
    if (action === "editor.play_state") {
      playStateCalls += 1;

      if (playStateCalls === 1) {
        throw new Error("No live playing GameSession was found.");
      }

      return {
        verified: {
          targetSession: {
            resolvedTarget: "gameSession",
            session: {
              isGameSession: true
            }
          }
        }
      };
    }

    assert.equal(action, "scene.summary");
    return {
      verified: {
        objectCount: 4,
        componentCount: 6,
        targetSession: {
          resolvedTarget: "gameSession",
          session: {
            isGameSession: true
          }
        }
      }
    };
  });

  const result = await waitForRuntime(bridge, {
    timeoutMs: 500,
    pollMs: 25,
    minObjects: 1
  });

  assert.equal(result.verified.satisfied, true);
  assert.equal(result.verified.attempts, 2);
  assert.equal(result.verified.runtimeResolved, true);
  assert.equal(result.verified.sceneReady, true);
});

test("waitForStopped ignores derived GameSession tabs by default", async () => {
  const bridge = fakeBridge(async (action) => {
    assert.equal(action, "editor.tabs");

    return {
      verified: {
        count: 2,
        tabs: [
          {
            index: 0,
            id: "editor",
            scene: "Minimal",
            sourcePath: "scenes/minimal.scene",
            isGameSession: false,
            playState: {
              isPlaying: false,
              hasGameSession: false
            }
          },
          {
            index: 1,
            id: "runtime",
            scene: "Minimal",
            sourcePath: "",
            isGameSession: true,
            playState: {
              isPlaying: true,
              hasGameSession: false
            }
          }
        ]
      }
    };
  });

  const result = await waitForStopped(bridge, {
    timeoutMs: 500,
    pollMs: 25
  });

  assert.equal(result.verified.satisfied, true);
  assert.equal(result.verified.playingEditorTabCount, 0);
  assert.equal(result.verified.gameSessionTabCount, 1);
});

test("editor MCP tool routes wait_compile through shared wait helper", async () => {
  const bridge = fakeBridge(async (action) => {
    assert.equal(action, "editor.compile_status");

    return {
      verified: {
        observedGroupCount: 1,
        groups: [
          {
            sequence: 8,
            isBuilding: false,
            needsBuild: false,
            buildSuccess: true,
            errorCount: 0,
            compilers: []
          }
        ]
      }
    };
  });
  let handler: ((args: Record<string, unknown>) => Promise<{ content: Array<{ type: "text"; text: string }> }>) | undefined;
  const server = {
    tool: (_name: string, _description: string, _schema: unknown, registeredHandler: typeof handler) => {
      handler = registeredHandler;
    }
  };

  registerEditorTools(server as never, bridge);
  assert.ok(handler, "editor tool handler was not registered");

  const response = await handler({
    action: "wait_compile",
    timeoutMs: 500,
    pollMs: 25
  });
  const body = JSON.parse(response.content[0]?.text ?? "{}") as { verified?: { satisfied?: boolean; wait?: string } };

  assert.equal(body.verified?.wait, "compile");
  assert.equal(body.verified?.satisfied, true);
});

function fakeBridge(send: (action: string, payload: Record<string, unknown>) => Promise<unknown>): BridgeClient {
  return { send } as BridgeClient;
}
