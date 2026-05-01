import type { BridgeClient } from "./bridge-client.js";

export interface WaitOptions {
  timeoutMs?: number;
  pollMs?: number;
}

export interface CompileWaitOptions extends WaitOptions {
  maxDiagnostics?: number;
  requireObservedCompile?: boolean;
  sinceSequence?: number;
}

export interface RuntimeWaitOptions extends WaitOptions {
  targetSession?: "active" | "editor" | "playing" | "runtime" | "game";
  sessionId?: string;
  sessionIndex?: number;
  sessionPath?: string;
  sessionScene?: string;
  minObjects?: number;
  requireSceneSummary?: boolean;
}

export interface StoppedWaitOptions extends WaitOptions {
  requireNoGameSessions?: boolean;
}

interface CompileStatusResult {
  verified: {
    observedGroupCount: number;
    groups: CompileGroup[];
  };
}

interface CompileGroup {
  sequence: number;
  isBuilding: boolean;
  needsBuild: boolean;
  buildSuccess: boolean;
  errorCount: number;
  compilers?: Array<{
    isBuilding: boolean;
    needsBuild: boolean;
    buildSuccess: boolean;
    errorCount: number;
  }>;
}

interface PlayStateResult {
  verified: {
    targetSession?: {
      resolvedTarget: string;
      session: {
        isGameSession: boolean;
      };
    };
  };
}

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

interface TabsResult {
  verified: {
    count: number;
    tabs: Array<{
      index: number;
      id: string;
      scene: string;
      sourcePath: string;
      isGameSession?: boolean;
      playState?: {
        isPlaying: boolean;
        hasGameSession: boolean;
      };
    }>;
  };
}

export async function waitForCompile(bridge: BridgeClient, options: CompileWaitOptions = {}) {
  const timeoutMs = clampNumber(options.timeoutMs, 10_000, 100, 120_000);
  const pollMs = clampNumber(options.pollMs, 100, 25, 5_000);
  const maxDiagnostics = clampNumber(options.maxDiagnostics, 20, 0, 100);
  const requireObservedCompile = options.requireObservedCompile ?? options.sinceSequence !== undefined;
  const started = Date.now();
  const deadline = started + timeoutMs;
  let attempts = 0;
  let lastStatus: CompileStatusResult | null = null;
  let lastError = "";
  let evaluation: CompileEvaluation = {
    observed: false,
    idle: false,
    hasRequiredSequence: false,
    latestSequence: null,
    buildSucceeded: false,
    errorCount: 0
  };

  while (Date.now() <= deadline) {
    attempts += 1;

    try {
      lastStatus = await bridge.send<CompileStatusResult>("editor.compile_status", { maxDiagnostics });
      lastError = "";
      evaluation = evaluateCompileStatus(lastStatus, {
        requireObservedCompile,
        sinceSequence: options.sinceSequence
      });

      if (evaluation.idle && evaluation.hasRequiredSequence && (!requireObservedCompile || evaluation.observed)) {
        return buildWaitResult("compile", true, started, timeoutMs, pollMs, attempts, {
          requireObservedCompile,
          sinceSequence: options.sinceSequence ?? null,
          ...evaluation,
          compileStatus: lastStatus.verified
        });
      }
    } catch (error) {
      lastError = formatError(error);
    }

    await sleepUntilNextPoll(deadline, pollMs);
  }

  return buildWaitResult("compile", false, started, timeoutMs, pollMs, attempts, {
    requireObservedCompile,
    sinceSequence: options.sinceSequence ?? null,
    ...evaluation,
    lastError,
    compileStatus: lastStatus?.verified ?? null
  });
}

export async function waitForRuntime(bridge: BridgeClient, options: RuntimeWaitOptions = {}) {
  const timeoutMs = clampNumber(options.timeoutMs, 10_000, 100, 120_000);
  const pollMs = clampNumber(options.pollMs, 100, 25, 5_000);
  const minObjects = clampNumber(options.minObjects, 1, 0, Number.MAX_SAFE_INTEGER);
  const requireSceneSummary = options.requireSceneSummary ?? true;
  const started = Date.now();
  const deadline = started + timeoutMs;
  const targetPayload = buildSessionPayload(options, "runtime");
  let attempts = 0;
  let playState: PlayStateResult | null = null;
  let sceneSummary: SceneSummaryResult | null = null;
  let lastError = "";

  while (Date.now() <= deadline) {
    attempts += 1;

    try {
      playState = await bridge.send<PlayStateResult>("editor.play_state", targetPayload);
      sceneSummary = requireSceneSummary ? await bridge.send<SceneSummaryResult>("scene.summary", targetPayload) : sceneSummary;
      lastError = "";

      const runtimeResolved = isRuntimeSession(playState.verified.targetSession) || isRuntimeSession(sceneSummary?.verified.targetSession);
      const sceneReady = !requireSceneSummary || (sceneSummary !== null && sceneSummary.verified.objectCount >= minObjects);

      if (runtimeResolved && sceneReady) {
        return buildWaitResult("runtime", true, started, timeoutMs, pollMs, attempts, {
          targetSession: targetPayload,
          minObjects,
          runtimeResolved,
          sceneReady,
          playState: playState.verified,
          sceneSummary: sceneSummary?.verified ?? null
        });
      }
    } catch (error) {
      lastError = formatError(error);
    }

    await sleepUntilNextPoll(deadline, pollMs);
  }

  return buildWaitResult("runtime", false, started, timeoutMs, pollMs, attempts, {
    targetSession: targetPayload,
    minObjects,
    runtimeResolved: isRuntimeSession(playState?.verified.targetSession) || isRuntimeSession(sceneSummary?.verified.targetSession),
    sceneReady: sceneSummary !== null && sceneSummary.verified.objectCount >= minObjects,
    lastError,
    playState: playState?.verified ?? null,
    sceneSummary: sceneSummary?.verified ?? null
  });
}

export async function waitForStopped(bridge: BridgeClient, options: StoppedWaitOptions = {}) {
  const timeoutMs = clampNumber(options.timeoutMs, 10_000, 100, 120_000);
  const pollMs = clampNumber(options.pollMs, 100, 25, 5_000);
  const requireNoGameSessions = options.requireNoGameSessions ?? false;
  const started = Date.now();
  const deadline = started + timeoutMs;
  let attempts = 0;
  let tabs: TabsResult | null = null;
  let lastError = "";
  let counts = summarizeTabs(null);

  while (Date.now() <= deadline) {
    attempts += 1;

    try {
      tabs = await bridge.send<TabsResult>("editor.tabs");
      lastError = "";
      counts = summarizeTabs(tabs);

      if (counts.playingEditorTabCount === 0 && (!requireNoGameSessions || counts.gameSessionTabCount === 0)) {
        return buildWaitResult("stopped", true, started, timeoutMs, pollMs, attempts, {
          requireNoGameSessions,
          ...counts,
          tabs: tabs.verified
        });
      }
    } catch (error) {
      lastError = formatError(error);
    }

    await sleepUntilNextPoll(deadline, pollMs);
  }

  return buildWaitResult("stopped", false, started, timeoutMs, pollMs, attempts, {
    requireNoGameSessions,
    ...counts,
    lastError,
    tabs: tabs?.verified ?? null
  });
}

function evaluateCompileStatus(
  status: CompileStatusResult,
  options: { requireObservedCompile: boolean; sinceSequence?: number }
): CompileEvaluation {
  const groups = status.verified.groups ?? [];
  const latestSequence = groups.length > 0 ? Math.max(...groups.map((group) => group.sequence ?? 0)) : null;
  const observed = groups.length > 0;
  const idle =
    groups.length === 0
      ? !options.requireObservedCompile
      : groups.every(
          (group) =>
            !group.isBuilding &&
            !group.needsBuild &&
            (group.compilers ?? []).every((compiler) => !compiler.isBuilding && !compiler.needsBuild)
        );
  const hasRequiredSequence = options.sinceSequence === undefined || (latestSequence !== null && latestSequence > options.sinceSequence);
  const errorCount = groups.reduce((total, group) => total + (group.errorCount ?? 0), 0);
  const buildSucceeded = groups.length > 0 && groups.every((group) => group.buildSuccess && group.errorCount === 0);

  return {
    observed,
    idle,
    hasRequiredSequence,
    latestSequence,
    buildSucceeded,
    errorCount
  };
}

interface CompileEvaluation {
  observed: boolean;
  idle: boolean;
  hasRequiredSequence: boolean;
  latestSequence: number | null;
  buildSucceeded: boolean;
  errorCount: number;
}

function summarizeTabs(tabs: TabsResult | null) {
  const allTabs = tabs?.verified.tabs ?? [];
  const editorTabs = allTabs.filter((tab) => tab.isGameSession !== true);
  const gameSessionTabs = allTabs.filter((tab) => tab.isGameSession === true);
  const playingEditorTabs = editorTabs.filter((tab) => tab.playState?.isPlaying === true || tab.playState?.hasGameSession === true);

  return {
    tabCount: allTabs.length,
    editorTabCount: editorTabs.length,
    gameSessionTabCount: gameSessionTabs.length,
    playingEditorTabCount: playingEditorTabs.length,
    playingEditorTabs: playingEditorTabs.map((tab) => ({
      index: tab.index,
      id: tab.id,
      scene: tab.scene,
      sourcePath: tab.sourcePath,
      isPlaying: tab.playState?.isPlaying === true,
      hasGameSession: tab.playState?.hasGameSession === true
    }))
  };
}

function buildSessionPayload(options: RuntimeWaitOptions, defaultTarget: RuntimeWaitOptions["targetSession"]) {
  return {
    targetSession: options.targetSession ?? defaultTarget,
    ...(options.sessionId ? { sessionId: options.sessionId } : {}),
    ...(options.sessionIndex !== undefined ? { sessionIndex: options.sessionIndex } : {}),
    ...(options.sessionPath ? { sessionPath: options.sessionPath } : {}),
    ...(options.sessionScene ? { sessionScene: options.sessionScene } : {})
  };
}

function isRuntimeSession(targetSession: PlayStateResult["verified"]["targetSession"] | SceneSummaryResult["verified"]["targetSession"] | undefined): boolean {
  return targetSession?.resolvedTarget === "gameSession" || targetSession?.session?.isGameSession === true;
}

function buildWaitResult(wait: string, satisfied: boolean, started: number, timeoutMs: number, pollMs: number, attempts: number, detail: Record<string, unknown>) {
  const elapsedMs = Date.now() - started;

  return {
    message: satisfied ? `Editor ${wait} wait satisfied` : `Editor ${wait} wait timed out`,
    verified: {
      wait,
      satisfied,
      timedOut: !satisfied,
      timeoutMs,
      pollMs,
      elapsedMs,
      attempts,
      ...detail
    }
  };
}

async function sleepUntilNextPoll(deadline: number, pollMs: number): Promise<void> {
  const remaining = deadline - Date.now();
  if (remaining <= 0) {
    return;
  }

  await sleep(Math.min(pollMs, remaining));
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function clampNumber(value: number | undefined, fallback: number, min: number, max: number): number {
  if (value === undefined || !Number.isFinite(value)) {
    return fallback;
  }

  return Math.min(max, Math.max(min, Math.floor(value)));
}

function formatError(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
