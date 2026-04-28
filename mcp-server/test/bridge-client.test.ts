import { randomUUID } from "node:crypto";
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import assert from "node:assert/strict";
import test from "node:test";
import { BridgeClient } from "../src/bridge-client.js";

test("BridgeClient sends a command and returns a successful response", async () => {
  const root = await makeTempRoot();
  const responder = respondOnce(root, (request) => ({
    id: request.id,
    ok: true,
    result: {
      action: request.action,
      payload: request.payload
    }
  }));

  try {
    const client = new BridgeClient({ root, timeoutMs: 1_000, pollMs: 5 });
    const result = await client.send("scene.summary", { includeDisabled: true });

    assert.deepEqual(result, {
      action: "scene.summary",
      payload: { includeDisabled: true }
    });

    await responder;
    assert.deepEqual(await listResponses(root), []);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("BridgeClient surfaces bridge errors with suggestions", async () => {
  const root = await makeTempRoot();
  const responder = respondOnce(root, (request) => ({
    id: request.id,
    ok: false,
    error: {
      message: "No active editor scene.",
      suggestion: "Open a scene in s&box."
    }
  }));

  try {
    const client = new BridgeClient({ root, timeoutMs: 1_000, pollMs: 5 });

    await assert.rejects(
      () => client.send("scene.summary"),
      /No active editor scene\. Suggestion: Open a scene in s&box\./
    );

    await responder;
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("BridgeClient times out when no response appears", async () => {
  const root = await makeTempRoot();

  try {
    const client = new BridgeClient({ root, timeoutMs: 25, pollMs: 5 });

    await assert.rejects(
      () => client.send("bridge.status"),
      /Timed out waiting for s&box bridge response/
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

interface BridgeRequest {
  id: string;
  action: string;
  payload: Record<string, unknown>;
}

interface BridgeResponse {
  id: string;
  ok: boolean;
  result?: unknown;
  error?: {
    message: string;
    suggestion?: string;
  };
}

async function makeTempRoot(): Promise<string> {
  return await mkdtemp(join(tmpdir(), `sbox-agent-bridge-test-${randomUUID()}-`));
}

async function respondOnce(root: string, buildResponse: (request: BridgeRequest) => BridgeResponse): Promise<void> {
  const requestDir = join(root, "requests");
  const responseDir = join(root, "responses");
  const deadline = Date.now() + 1_000;

  while (Date.now() < deadline) {
    await mkdir(requestDir, { recursive: true });
    await mkdir(responseDir, { recursive: true });

    const requests = (await readdir(requestDir)).filter((name) => name.startsWith("request-"));

    if (requests.length > 0) {
      const requestPath = join(requestDir, requests[0]!);
      const raw = await readFile(requestPath, "utf8");
      const request = JSON.parse(raw) as BridgeRequest;
      const responsePath = join(responseDir, `response-${request.id}.json`);

      await writeFile(responsePath, JSON.stringify(buildResponse(request), null, 2), "utf8");
      return;
    }

    await sleep(5);
  }

  throw new Error("Timed out waiting for test request file.");
}

async function listResponses(root: string): Promise<string[]> {
  return await readdir(join(root, "responses"));
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
