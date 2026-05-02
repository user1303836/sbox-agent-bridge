import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface GameObjectResult {
  verified: {
    id: string;
    name: string;
  };
}

interface NetworkConnectionsResult {
  verified: {
    count: number;
    local?: NetworkConnection | null;
    host?: NetworkConnection | null;
    connections: NetworkConnection[];
  };
}

interface NetworkConnection {
  id: string;
  canSpawnObjects: boolean;
  canRefreshObjects: boolean;
  canDestroyObjects: boolean;
  isConnecting: boolean;
  isActive: boolean;
  state: string;
  ping: string;
  isHost: boolean;
}

interface NetworkObjectResult {
  verified: {
    gameObject: {
      id: string;
      name: string;
    };
    network: {
      networkMode: string;
      ownerTransfer: string;
      networkOrphaned: string;
      alwaysTransmit: boolean;
      accessor: {
        ownerTransfer: string;
        networkOrphaned: string;
        alwaysTransmit: boolean;
        flags: string;
      };
    };
  };
}

interface NetworkSetObjectResult {
  verified: {
    gameObject: {
      id: string;
      name: string;
    };
    before: NetworkObjectResult["verified"]["network"];
    after: NetworkObjectResult["verified"]["network"];
  };
}

interface ScriptAnalyzeResult {
  verified: {
    analysis: {
      syncAttributeCount: number;
      rpcAttributeCount: number;
      containsGameObjectNetworkEvents: boolean;
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath =
  process.env.SBOX_AGENT_BRIDGE_NETWORK_SCENE ??
  process.env.SBOX_AGENT_BRIDGE_MVP_SUITE_SCENE ??
  "scenes/agent_bridge/smoke/mvp_suite.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";

let objectId = "";

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
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 10_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before network smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before network smoke");
  ensure(compileWait.verified.errorCount === 0, "editor compile status reported errors before network smoke");

  await bridge.send("editor.recover_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    stopAll: true,
    bringToFront: true
  });

  const connections = await bridge.send<NetworkConnectionsResult>("network.connections", {});
  ensure(Array.isArray(connections.verified.connections), "network.connections did not return a connection array");
  ensure(typeof connections.verified.count === "number", "network.connections did not return a numeric count");
  ensure(connections.verified.count >= 1, "network.connections did not report the local editor connection");
  ensure(connections.verified.local?.canSpawnObjects === true, "network.connections did not expose local spawn permission");
  ensure(connections.verified.host?.isHost === true, "network.connections did not identify the host connection");

  const created = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Network Smoke Object",
    position: { x: 128, y: -128, z: 0 }
  });
  objectId = created.verified.id;

  const before = await bridge.send<NetworkObjectResult>("network.inspect_object", {
    gameObjectId: objectId
  });
  ensure(before.verified.gameObject.id === objectId, "network.inspect_object did not inspect the requested object");

  const afterSet = await bridge.send<NetworkSetObjectResult>("network.set_object_mode", {
    gameObjectId: objectId,
    networkMode: "Object",
    ownerTransfer: "Fixed",
    networkOrphaned: "Host",
    alwaysTransmit: true
  });
  ensure(afterSet.verified.after.networkMode === "Object", "network.set_object_mode did not set NetworkMode=Object");
  ensure(afterSet.verified.after.ownerTransfer === "Fixed", "network.set_object_mode did not set OwnerTransfer=Fixed");
  ensure(afterSet.verified.after.networkOrphaned === "Host", "network.set_object_mode did not set NetworkOrphaned=Host");
  ensure(afterSet.verified.after.alwaysTransmit, "network.set_object_mode did not set AlwaysTransmit=true");

  const inspected = await bridge.send<NetworkObjectResult>("network.inspect_object", {
    gameObjectId: objectId
  });
  ensure(inspected.verified.network.networkMode === "Object", "network.inspect_object did not read back NetworkMode=Object");
  ensure(inspected.verified.network.ownerTransfer === "Fixed", "network.inspect_object did not read back OwnerTransfer=Fixed");
  ensure(inspected.verified.network.networkOrphaned === "Host", "network.inspect_object did not read back NetworkOrphaned=Host");
  ensure(inspected.verified.network.alwaysTransmit, "network.inspect_object did not read back AlwaysTransmit=true");

  const networkProbe = await bridge.send<ScriptAnalyzeResult>("script.analyze", {
    content: networkProbeContent
  });
  ensure(networkProbe.verified.analysis.syncAttributeCount >= 1, "script.analyze did not identify Sync attributes from network probe source");
  ensure(networkProbe.verified.analysis.rpcAttributeCount >= 1, "script.analyze did not identify Rpc attributes from network probe source");
  ensure(networkProbe.verified.analysis.containsGameObjectNetworkEvents, "script.analyze did not identify IGameObjectNetworkEvents from network probe source");

  await cleanup();

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        compileWaitMs: compileWait.verified.elapsedMs,
        connections: {
          count: connections.verified.count,
          local: connections.verified.local ?? null,
          host: connections.verified.host ?? null
        },
        before: before.verified.network,
        after: inspected.verified.network,
        sourceAnalysis: networkProbe.verified.analysis
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
  if (!objectId) {
    return;
  }

  const id = objectId;
  objectId = "";

  try {
    await bridge.send("gameobject.destroy", { id });
  } catch {
    // Best-effort cleanup for a focused smoke script.
  }
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
