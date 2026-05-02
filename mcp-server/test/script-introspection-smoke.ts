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
      containsScenePhysicsEvents: boolean;
      containsGameObjectNetworkEvents: boolean;
      containsNetworkSnapshot: boolean;
      containsNetworkVisible: boolean;
      containsNetworkSpawn: boolean;
      containsNetworkListener: boolean;
      domainMarkers: {
        physics: {
          scenePhysicsEvents: boolean;
        };
        networking: {
          networkSnapshot: boolean;
          networkVisible: boolean;
          networkSpawn: boolean;
          networkListener: boolean;
          http: boolean;
          webSocket: boolean;
        };
        rendering: {
          sceneCamera: boolean;
          renderTarget: boolean;
          commandList: boolean;
          hudPainter: boolean;
          screenPanel: boolean;
          shaderGraph: boolean;
          vr: boolean;
        };
        ui: {
          panel: boolean;
          virtualGrid: boolean;
          localization: boolean;
          razorComponent: boolean;
        };
        assets: {
          gameResource: boolean;
          assetType: boolean;
          clothing: boolean;
          citizen: boolean;
          firstPersonWeapon: boolean;
          storage: boolean;
        };
        world: {
          navMesh: boolean;
          terrain: boolean;
          clutter: boolean;
        };
        animation: {
          animationGraph: boolean;
          animationStateMachine: boolean;
          animationEvent: boolean;
          ik: boolean;
        };
        services: {
          achievement: boolean;
          auth: boolean;
          leaderboard: boolean;
          stats: boolean;
          webApi: boolean;
        };
        media: {
          video: boolean;
        };
        editor: {
          widget: boolean;
          dialog: boolean;
          menu: boolean;
          assetPicker: boolean;
          actionGraph: boolean;
          movieMaker: boolean;
          gameMount: boolean;
        };
        input: {
          gamepad: boolean;
          rawInput: boolean;
          glyph: boolean;
        };
      };
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

const domainProbeContent = `using Sandbox;
using Editor;

public sealed class AgentBridgeDomainProbe : Component, ISceneStartup, IScenePhysicsEvents, IGameObjectNetworkEvents, Component.INetworkSnapshot, Component.INetworkVisible
{
  [Property] public GameResource Resource { get; set; }
  [Sync] public int Score { get; set; }

  public void MarkerReferences()
  {
    _ = nameof(INetworkSpawn);
    _ = nameof(INetworkListener);
    _ = nameof(HttpClient);
    _ = nameof(WebSocket);
    _ = nameof(SceneCamera);
    _ = nameof(RenderTarget);
    _ = nameof(CommandList);
    _ = nameof(HudPainter);
    _ = nameof(ScreenPanel);
    _ = nameof(ShaderGraph);
    _ = nameof(VR);
    _ = nameof(Panel);
    _ = nameof(VirtualGrid);
    _ = nameof(Localization);
    _ = nameof(ComponentBase);
    _ = nameof(AssetType);
    _ = nameof(Clothing);
    _ = nameof(Citizen);
    _ = nameof(FirstPerson);
    _ = nameof(Storage);
    _ = nameof(NavMesh);
    _ = nameof(Terrain);
    _ = nameof(Clutter);
    _ = nameof(AnimationGraph);
    _ = nameof(AnimationStateMachine);
    _ = nameof(AnimationEvent);
    _ = nameof(IK);
    _ = nameof(Achievement);
    _ = nameof(Auth);
    _ = nameof(Leaderboard);
    _ = nameof(Stats);
    _ = nameof(WebApi);
    _ = nameof(Video);
    _ = nameof(Widget);
    _ = nameof(Dialog);
    _ = nameof(Menu);
    _ = nameof(AssetPicker);
    _ = nameof(ActionGraph);
    _ = nameof(MovieMaker);
    _ = nameof(GameMount);
    _ = nameof(Gamepad);
    _ = nameof(RawInput);
    _ = nameof(Glyph);
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

  const domainProbe = await bridge.send<ScriptAnalyzeResult>("script.analyze", {
    content: domainProbeContent
  });
  const domain = domainProbe.verified.analysis.domainMarkers;
  ensure(domainProbe.verified.analysis.containsScenePhysicsEvents, "script.analyze did not identify IScenePhysicsEvents from content");
  ensure(domainProbe.verified.analysis.containsNetworkSnapshot, "script.analyze did not identify INetworkSnapshot from content");
  ensure(domainProbe.verified.analysis.containsNetworkVisible, "script.analyze did not identify INetworkVisible from content");
  ensure(domainProbe.verified.analysis.containsNetworkSpawn, "script.analyze did not identify INetworkSpawn from content");
  ensure(domainProbe.verified.analysis.containsNetworkListener, "script.analyze did not identify INetworkListener from content");
  ensure(domain.physics.scenePhysicsEvents, "script.analyze domain markers did not identify scene physics events");
  ensure(domain.networking.http && domain.networking.webSocket, "script.analyze domain markers did not identify HTTP/WebSocket markers");
  ensure(
    domain.rendering.sceneCamera &&
      domain.rendering.renderTarget &&
      domain.rendering.commandList &&
      domain.rendering.hudPainter &&
      domain.rendering.screenPanel &&
      domain.rendering.shaderGraph &&
      domain.rendering.vr,
    "script.analyze domain markers did not identify render/UI render markers"
  );
  ensure(domain.ui.panel && domain.ui.virtualGrid && domain.ui.localization && domain.ui.razorComponent, "script.analyze domain markers did not identify UI markers");
  ensure(
    domain.assets.gameResource &&
      domain.assets.assetType &&
      domain.assets.clothing &&
      domain.assets.citizen &&
      domain.assets.firstPersonWeapon &&
      domain.assets.storage,
    "script.analyze domain markers did not identify asset workflow markers"
  );
  ensure(domain.world.navMesh && domain.world.terrain && domain.world.clutter, "script.analyze domain markers did not identify world-system markers");
  ensure(
    domain.animation.animationGraph && domain.animation.animationStateMachine && domain.animation.animationEvent && domain.animation.ik,
    "script.analyze domain markers did not identify animation markers"
  );
  ensure(domain.services.achievement && domain.services.auth && domain.services.leaderboard && domain.services.stats && domain.services.webApi, "script.analyze domain markers did not identify service markers");
  ensure(domain.media.video, "script.analyze domain markers did not identify media markers");
  ensure(
    domain.editor.widget && domain.editor.dialog && domain.editor.menu && domain.editor.assetPicker && domain.editor.actionGraph && domain.editor.movieMaker && domain.editor.gameMount,
    "script.analyze domain markers did not identify editor tool markers"
  );
  ensure(domain.input.gamepad && domain.input.rawInput && domain.input.glyph, "script.analyze domain markers did not identify input markers");

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
        networkProbe: networkProbe.verified.analysis,
        domainProbe: domainProbe.verified.analysis
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
