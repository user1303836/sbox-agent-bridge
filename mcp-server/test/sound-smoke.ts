import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface GameObjectResult {
  verified: {
    id: string;
  };
}

interface SoundInfoResult {
  verified: {
    soundEvent: {
      resource: {
        path: string;
      } | null;
      volume: {
        fixedValue: number;
      };
      pitch: {
        fixedValue: number;
      };
      sounds: Array<{
        path: string;
        isValid: boolean;
      }>;
    } | null;
  };
}

interface SoundInspectResult {
  verified: {
    count: number;
    components: Array<{
      soundEvent: {
        resource: {
          path: string;
        } | null;
      } | null;
      playOnStart: boolean;
      repeat: boolean;
      force2d: boolean;
      volume: number;
      pitch: number;
    }>;
  };
}

interface SoundPreviewResult {
  verified: {
    handle: {
      isValid: boolean;
      isPlaying: boolean;
      name: string;
      volume: number;
      pitch: number;
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "scenes/minimal.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";
const eventPath = process.env.SBOX_AGENT_BRIDGE_SOUND_SMOKE_EVENT ?? "sounds/agent_bridge/smoke/sound_smoke.sound";
const soundFilePath = process.env.SBOX_AGENT_BRIDGE_SOUND_SMOKE_FILE ?? "sounds/ambience/cave-loop.vsnd";

let sourceId = "";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 5_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before sound smoke");

  const compileWait = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before sound smoke");

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    bringToFront: true
  });

  await bridge.send("sound.create_event", {
    path: eventPath,
    soundFilePath,
    overwrite: true,
    volume: 0.42,
    pitch: 1.05,
    decibels: 0
  });

  const info = await bridge.send<SoundInfoResult>("sound.get_info", { path: eventPath });
  ensure(info.verified.soundEvent !== null, "sound.get_info did not return a SoundEvent");
  ensure(info.verified.soundEvent.sounds.length >= 1, "created SoundEvent did not include a sound file");
  ensure(info.verified.soundEvent.volume.fixedValue === 0.42, "SoundEvent volume read-back did not match");
  ensure(info.verified.soundEvent.pitch.fixedValue === 1.05, "SoundEvent pitch read-back did not match");

  const source = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Sound Smoke Source",
    position: { x: 256, y: -256, z: 48 }
  });
  sourceId = source.verified.id;

  await bridge.send("sound.assign", {
    gameObjectId: sourceId,
    eventPath,
    playOnStart: false,
    repeat: true,
    force2d: true,
    volume: 0.5,
    pitch: 0.9
  });

  const inspected = await bridge.send<SoundInspectResult>("sound.inspect", { gameObjectId: sourceId });
  ensure(inspected.verified.count === 1, "sound.inspect did not report one SoundPointComponent");
  const component = inspected.verified.components[0];
  ensure(component?.soundEvent?.resource?.path === eventPath, "sound.inspect event path read-back did not match");
  ensure(component.playOnStart === false, "SoundPointComponent playOnStart read-back did not match");
  ensure(component.repeat === true, "SoundPointComponent repeat read-back did not match");
  ensure(component.force2d === true, "SoundPointComponent force2d read-back did not match");
  ensure(component.volume === 0.5, "SoundPointComponent volume read-back did not match");
  ensure(component.pitch === 0.9, "SoundPointComponent pitch read-back did not match");

  const preview = await bridge.send<SoundPreviewResult>("sound.preview", {
    eventPath,
    position: { x: 256, y: -256, z: 48 },
    fadeIn: 0
  });
  ensure(preview.verified.handle.isValid, "sound.preview did not return a valid SoundHandle");

  await cleanup();

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        eventPath,
        soundFilePath,
        compileWaitMs: compileWait.verified.elapsedMs,
        soundEvent: {
          path: info.verified.soundEvent.resource?.path,
          volume: info.verified.soundEvent.volume.fixedValue,
          pitch: info.verified.soundEvent.pitch.fixedValue,
          sounds: info.verified.soundEvent.sounds
        },
        component,
        preview: preview.verified.handle
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
  const id = sourceId;
  sourceId = "";

  if (!id) {
    return;
  }

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
