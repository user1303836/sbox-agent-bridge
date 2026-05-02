import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface GameObjectResult {
  verified: {
    id: string;
  };
}

interface PhysicsInspectResult {
  verified: {
    gameObject: {
      id: string;
    };
    rigidbodies: Array<{
      gravity: boolean;
      motionEnabled: boolean;
      massOverride: number;
    }>;
    colliders: Array<{
      staticCollider: boolean;
      isTrigger: boolean;
      shape: {
        type: string;
        scale?: { x: number; y: number; z: number };
        center?: { x: number; y: number; z: number };
      };
    }>;
    joints: Array<{
      enableCollision: boolean;
      body: {
        id: string;
      } | null;
      target: {
        id: string;
      } | null;
    }>;
  };
}

interface RaycastResult {
  verified: {
    hit: boolean;
    gameObject: {
      id: string;
      name: string;
    } | null;
    collider: {
      type: string;
    } | null;
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "scenes/minimal.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";

let targetId = "";
let anchorId = "";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 5_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before physics smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before physics smoke");

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    bringToFront: true
  });

  const target = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Physics Smoke Target",
    position: { x: 384, y: -384, z: 64 }
  });
  targetId = target.verified.id;

  const anchor = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Physics Smoke Anchor",
    position: { x: 448, y: -384, z: 64 }
  });
  anchorId = anchor.verified.id;

  await bridge.send("physics.add_physics", {
    gameObjectId: targetId,
    gravity: false,
    motionEnabled: false,
    mass: 25
  });

  await bridge.send("physics.add_collider", {
    gameObjectId: targetId,
    type: "box",
    scale: { x: 48, y: 48, z: 48 },
    center: { x: 0, y: 0, z: 0 },
    static: true,
    trigger: false
  });

  await bridge.send("physics.add_joint", {
    gameObjectId: targetId,
    targetGameObjectId: anchorId,
    type: "fixed",
    enableCollision: true
  });

  const inspected = await bridge.send<PhysicsInspectResult>("physics.inspect", { gameObjectId: targetId });
  ensure(inspected.verified.gameObject.id === targetId, "physics.inspect returned the wrong GameObject");
  ensure(inspected.verified.rigidbodies.length === 1, "physics.inspect did not report one Rigidbody");
  ensure(inspected.verified.rigidbodies[0]?.gravity === false, "Rigidbody gravity read-back was not false");
  ensure(inspected.verified.rigidbodies[0]?.motionEnabled === false, "Rigidbody motionEnabled read-back was not false");
  ensure(inspected.verified.rigidbodies[0]?.massOverride === 25, "Rigidbody massOverride read-back was not 25");
  ensure(inspected.verified.colliders.length === 1, "physics.inspect did not report one collider");
  ensure(inspected.verified.colliders[0]?.shape.type === "box", "physics.inspect did not report a box collider");
  ensure(inspected.verified.colliders[0]?.shape.scale?.x === 48, "Box collider scale read-back was not preserved");
  ensure(inspected.verified.colliders[0]?.staticCollider === true, "Collider static read-back was not true");
  ensure(inspected.verified.colliders[0]?.isTrigger === false, "Collider trigger read-back was not false");
  ensure(inspected.verified.joints.length === 1, "physics.inspect did not report one joint");
  ensure(inspected.verified.joints[0]?.enableCollision === true, "Joint enableCollision read-back was not true");
  ensure(
    inspected.verified.joints[0]?.body?.id === anchorId || inspected.verified.joints[0]?.target?.id === anchorId,
    "Joint target/body read-back did not reference the anchor GameObject"
  );

  const raycast = await bridge.send<RaycastResult>("physics.raycast", {
    from: { x: 384, y: -384, z: 128 },
    to: { x: 384, y: -384, z: 32 }
  });
  ensure(raycast.verified.hit, "physics.raycast did not hit the smoke collider");
  ensure(raycast.verified.gameObject?.id === targetId, "physics.raycast hit the wrong GameObject");
  ensure(raycast.verified.collider?.type === "BoxCollider", "physics.raycast did not report the box collider");

  await cleanup();

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        compileWaitMs: compileWait.verified.elapsedMs,
        rigidbody: inspected.verified.rigidbodies[0],
        collider: inspected.verified.colliders[0],
        joint: inspected.verified.joints[0],
        raycast: {
          hit: raycast.verified.hit,
          gameObject: raycast.verified.gameObject,
          collider: raycast.verified.collider
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
  const ids = [targetId, anchorId].filter(Boolean);
  targetId = "";
  anchorId = "";

  for (const id of ids) {
    try {
      await bridge.send("gameobject.destroy", { id });
    } catch {
      // Best-effort cleanup for a focused smoke script.
    }
  }
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
