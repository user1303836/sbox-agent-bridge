import { randomUUID } from "node:crypto";
import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

export interface BridgeClientOptions {
  root?: string;
  timeoutMs?: number;
  pollMs?: number;
}

export interface BridgeResponse<T = unknown> {
  id: string;
  ok: boolean;
  result?: T;
  error?: {
    message: string;
    suggestion?: string;
  };
}

export class BridgeClient {
  private readonly root: string;
  private readonly timeoutMs: number;
  private readonly pollMs: number;

  public constructor(options: BridgeClientOptions = {}) {
    this.root = options.root ?? join(tmpdir(), "sbox-agent-bridge");
    this.timeoutMs = options.timeoutMs ?? 10_000;
    this.pollMs = options.pollMs ?? 100;
  }

  public async send<T = unknown>(action: string, payload: Record<string, unknown> = {}): Promise<T> {
    const id = randomUUID();
    const requestDir = join(this.root, "requests");
    const responseDir = join(this.root, "responses");
    const requestPath = join(requestDir, `request-${id}.json`);
    const requestTempPath = join(requestDir, `.request-${id}.tmp`);
    const responsePath = join(responseDir, `response-${id}.json`);

    await mkdir(requestDir, { recursive: true });
    await mkdir(responseDir, { recursive: true });
    await writeFile(requestTempPath, JSON.stringify({ id, action, payload }, null, 2), "utf8");
    await rename(requestTempPath, requestPath);

    const started = Date.now();

    while (Date.now() - started < this.timeoutMs) {
      if (existsSync(responsePath)) {
        let raw: string;

        try {
          raw = await readFile(responsePath, "utf8");
        } catch (error) {
          if (isTransientFileAccessError(error)) {
            await sleep(this.pollMs);
            continue;
          }

          throw error;
        }

        await rm(responsePath, { force: true });

        const response = JSON.parse(stripBom(raw)) as BridgeResponse<T>;

        if (!response.ok) {
          const suffix = response.error?.suggestion ? ` Suggestion: ${response.error.suggestion}` : "";
          throw new Error(`${response.error?.message ?? "Bridge command failed."}${suffix}`);
        }

        return response.result as T;
      }

      await sleep(this.pollMs);
    }

    throw new Error(`Timed out waiting for s&box bridge response for ${action}. Is the Agent Bridge dock open?`);
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function stripBom(text: string): string {
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

function isTransientFileAccessError(error: unknown): boolean {
  const code = (error as { code?: string }).code;
  return code === "EBUSY" || code === "EPERM" || code === "EACCES" || code === "ENOENT";
}
