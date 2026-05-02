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
    previewId: string;
    handle: {
      isValid: boolean;
      isPlaying: boolean;
      isStopped: boolean;
      name: string;
      volume: number;
      pitch: number;
    };
  };
}

interface SoundPreviewStatusResult {
  verified: {
    count: number;
    playingCount: number;
    results: Array<{
      previewId: string;
      eventPath: string;
      handle: {
        isValid: boolean;
        isPlaying: boolean;
        isStopped: boolean;
      };
    }>;
  };
}

interface SoundStopPreviewResult {
  verified: {
    stoppedCount: number;
    results: Array<{
      previewId: string;
      handle: {
        isStopped: boolean;
      };
    }>;
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
  ensure(preview.verified.previewId.length > 0, "sound.preview did not return a previewId");

  const previewStatus = await bridge.send<SoundPreviewStatusResult>("sound.preview_status", {
    previewId: preview.verified.previewId
  });
  ensure(previewStatus.verified.count === 1, "sound.preview_status did not return the tracked preview");
  ensure(previewStatus.verified.results[0]?.eventPath === eventPath, "sound.preview_status event path read-back did not match");

  const stoppedPreview = await bridge.send<SoundStopPreviewResult>("sound.stop_preview", {
    previewId: preview.verified.previewId,
    fadeOut: 0
  });
  ensure(stoppedPreview.verified.stoppedCount === 1, "sound.stop_preview did not stop the tracked preview");

  const stoppedStatus = await bridge.send<SoundPreviewStatusResult>("sound.preview_status", {
    previewId: preview.verified.previewId
  });
  ensure(stoppedStatus.verified.results[0]?.handle.isStopped === true, "sound.preview_status did not report the preview as stopped after stop_preview");

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
        preview: {
          previewId: preview.verified.previewId,
          started: preview.verified.handle,
          status: previewStatus.verified.results[0],
          stopped: stoppedPreview.verified.results[0],
          stoppedStatus: stoppedStatus.verified.results[0]
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
    await bridge.send("sound.stop_preview", { stopAll: true, fadeOut: 0 });
  } catch {
    // Best-effort cleanup for tracked preview handles.
  }

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
