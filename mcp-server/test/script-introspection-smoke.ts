import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile } from "../src/wait-helpers.js";

interface ScriptMutationResult {
  verified: {
    path?: string;
    existsAfter?: boolean;
    exists?: boolean;
    length?: number;
    sha256?: string;
  };
}

interface ScriptListResult {
  verified: {
    count: number;
    results: Array<{
      path: string;
      exists: boolean;
    }>;
  };
}

interface ScriptReadResult {
  verified: {
    content: string;
    script: {
      path: string;
      exists: boolean;
      sha256: string;
    };
  };
}

interface ScriptSearchResult {
  verified: {
    count: number;
    results: Array<{
      path: string;
      lineNumber: number;
      line: string;
    }>;
  };
}

interface ScriptAnalyzeResult {
  verified: {
    path: string;
    analysis: {
      classes: Array<{
        name: string;
        isComponent: boolean;
        baseTypes: string[];
      }>;
      attributes: string[];
      lifecycleMethods: string[];
      propertyAttributeCount: number;
      syncAttributeCount: number;
      rpcAttributeCount: number;
      containsSceneStartup: boolean;
      containsGameObjectNetworkEvents: boolean;
    };
  };
}

interface CompileStatusResult {
  verified: {
    groups: Array<{
      sequence: number;
    }>;
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scriptPath = process.env.SBOX_AGENT_BRIDGE_SCRIPT_INTROSPECTION_PATH ?? "AgentBridgeScratch/ScriptIntrospectionSmoke.cs";
const className = "AgentBridgeScriptIntrospectionSmoke";
const content = `using Sandbox;

public sealed class ${className} : Component
{
  [Property] public string Label { get; set; } = "Smoke";
  [Property] public float Radius { get; set; } = 32f;

  protected override void OnStart()
  {
    Label = "Started";
  }

  protected override void OnUpdate()
  {
  }
}
`;

const networkProbeContent = `using Sandbox;

public sealed class AgentBridgeNetworkProbe : Component, IGameObjectNetworkEvents
{
  [Sync] public int Score { get; set; }

  [Rpc.Broadcast]
  private void BroadcastScore()
  {
  }
}
`;

try {
  const beforeCreateSequence = await latestCompileSequence();
  await bridge.send("script.create", {
    path: scriptPath,
    content,
    overwrite: true
  });

  const createCompile = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20, sinceSequence: beforeCreateSequence });
  ensure(createCompile.verified.satisfied, "editor.wait_compile did not settle after script.create");
  ensure(createCompile.verified.errorCount === 0, "script introspection fixture did not compile cleanly");

  const listed = await bridge.send<ScriptListResult>("script.list", {
    query: "ScriptIntrospectionSmoke",
    maxResults: 20
  });
  ensure(listed.verified.results.some((script) => script.path === scriptPath), "script.list did not return the introspection fixture");

  const read = await bridge.send<ScriptReadResult>("script.read", {
    path: scriptPath
  });
  ensure(read.verified.content.includes(className), "script.read did not return the fixture content");

  const search = await bridge.send<ScriptSearchResult>("script.search", {
    query: "OnStart",
    path: scriptPath,
    maxMatches: 10
  });
  ensure(search.verified.count >= 1, "script.search did not find OnStart in the fixture");

  const analyzed = await bridge.send<ScriptAnalyzeResult>("script.analyze", {
    path: scriptPath
  });
  ensure(analyzed.verified.analysis.classes.some((item) => item.name === className && item.isComponent), "script.analyze did not identify the Component class");
  ensure(analyzed.verified.analysis.lifecycleMethods.includes("OnStart"), "script.analyze did not find OnStart");
  ensure(analyzed.verified.analysis.lifecycleMethods.includes("OnUpdate"), "script.analyze did not find OnUpdate");
  ensure(analyzed.verified.analysis.propertyAttributeCount >= 1, "script.analyze did not count Property attributes");

  const networkProbe = await bridge.send<ScriptAnalyzeResult>("script.analyze", {
    content: networkProbeContent
  });
  ensure(networkProbe.verified.analysis.syncAttributeCount >= 1, "script.analyze did not identify Sync attributes from content");
  ensure(networkProbe.verified.analysis.rpcAttributeCount >= 1, "script.analyze did not identify Rpc attributes from content");
  ensure(networkProbe.verified.analysis.containsGameObjectNetworkEvents, "script.analyze did not identify IGameObjectNetworkEvents from content");

  const beforeDeleteSequence = await latestCompileSequence();
  const deleted = await bridge.send<ScriptMutationResult>("script.delete", {
    path: scriptPath
  });
  ensure(deleted.verified.existsAfter === false, "script.delete did not remove the introspection fixture");

  const deleteCompile = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20, sinceSequence: beforeDeleteSequence });
  ensure(deleteCompile.verified.satisfied, "editor.wait_compile did not settle after script.delete");
  ensure(deleteCompile.verified.errorCount === 0, "script.delete did not return compile status to zero errors");

  console.log(
    JSON.stringify(
      {
        ok: true,
        scriptPath,
        compileWaitMs: {
          create: createCompile.verified.elapsedMs,
          delete: deleteCompile.verified.elapsedMs
        },
        listed: listed.verified.results[0] ?? null,
        read: {
          sha256: read.verified.script.sha256,
          length: read.verified.content.length
        },
        search: search.verified.results,
        analysis: analyzed.verified.analysis,
        networkProbe: networkProbe.verified.analysis
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
    await bridge.send("script.delete", { path: scriptPath });
  } catch {
    // Best-effort cleanup.
  }
}

async function latestCompileSequence(): Promise<number> {
  const status = await bridge.send<CompileStatusResult>("editor.compile_status", {});
  return Math.max(0, ...status.verified.groups.map((group) => group.sequence));
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
