import { BridgeClient } from "../src/bridge-client.js";

interface VerifiedEnvelope<T> {
  message: string;
  verified: T;
}

interface GameObjectSummary {
  id: string;
  name: string;
  enabled: boolean;
  parent?: {
    id: string;
    name: string;
  } | null;
}

interface SceneFindResult {
  verified: {
    count: number;
    results: GameObjectSummary[];
  };
}

interface SceneSummaryResult {
  verified: {
    componentCount: number;
    componentCounts: Array<{
      type: string;
      count: number;
    }>;
  };
}

interface ComponentListResult {
  verified: {
    count: number;
    components: Array<{
      id: string;
      type: string;
    }>;
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 10_000)
});

const stamp = new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14);
const prefix = process.env.SBOX_AGENT_BRIDGE_SMOKE_PREFIX ?? "Agent Bridge Live Smoke";
const keepObjects = process.env.SBOX_AGENT_BRIDGE_SMOKE_KEEP_OBJECTS === "1";
const createdIds: string[] = [];
const summary: Record<string, unknown> = {};

try {
  await bridge.send("bridge.status");

  const parent = await createObject(`${prefix} Parent ${stamp}`, { x: 80, y: 0, z: 64 });
  const source = await createObject(`${prefix} Source ${stamp}`, { x: 96, y: 0, z: 64 });
  createdIds.push(parent.id, source.id);

  await bridge.send("gameobject.rename", {
    id: source.id,
    name: `${prefix} Source Renamed ${stamp}`
  });

  await bridge.send("gameobject.set_transform", {
    id: source.id,
    position: { x: 112, y: 4, z: 64 },
    rotation: { pitch: 0, yaw: 45, roll: 0 },
    scale: { x: 1.25, y: 1.25, z: 1.25 }
  });

  await bridge.send("gameobject.set_enabled", { id: source.id, enabled: false });
  await bridge.send("gameobject.set_enabled", { id: source.id, enabled: true });
  await bridge.send("editor.set_selection", { ids: [source.id] });

  const selection = await bridge.send<{ verified: { count: number } }>("editor.get_selection");

  const duplicateEnvelope = await bridge.send<VerifiedEnvelope<GameObjectSummary> & { shallow?: boolean }>(
    "gameobject.duplicate",
    {
      id: source.id,
      name: `${prefix} Duplicate ${stamp}`,
      offset: { x: 16, y: 0, z: 0 }
    }
  );
  const duplicate = duplicateEnvelope.verified;
  createdIds.push(duplicate.id);

  const underParent = await bridge.send<VerifiedEnvelope<GameObjectSummary>>("gameobject.reparent", {
    id: duplicate.id,
    parentId: parent.id,
    keepWorldPosition: true
  });

  const rooted = await bridge.send<VerifiedEnvelope<GameObjectSummary>>("gameobject.reparent", {
    id: duplicate.id,
    keepWorldPosition: true
  });

  await bridge.send("editor.frame_object", { id: duplicate.id });

  const objectComponents = await bridge.send<ComponentListResult>("component.list_on_gameobject", {
    gameObjectId: source.id
  });

  const componentTypes = await bridge.send<{ verified: { count: number } }>("component.list_types", {
    maxResults: 5
  });

  const inspectedComponent = await inspectExistingComponent();

  await bridge.send("gameobject.destroy", { id: duplicate.id });
  const undo = await bridge.send<{ verified: { undone: boolean } }>("editor.undo");
  await bridge.send("gameobject.get", { id: duplicate.id });
  const redo = await bridge.send<{ verified: { redone: boolean } }>("editor.redo");
  const findAfterRedo = await bridge.send<SceneFindResult>("scene.find", {
    nameContains: duplicate.name,
    includeDisabled: true,
    maxResults: 5
  });

  summary.core = {
    parentId: parent.id,
    sourceId: source.id,
    duplicateId: duplicate.id,
    selectionCount: selection.verified.count,
    duplicateShallow: duplicateEnvelope.shallow === true,
    parentAfterReparent: underParent.verified.parent?.id,
    parentAfterRoot: rooted.verified.parent,
    undoApplied: undo.verified.undone,
    redoApplied: redo.verified.redone,
    countAfterRedo: findAfterRedo.verified.count
  };

  summary.components = {
    availableTypeSampleCount: componentTypes.verified.count,
    sourceComponentCount: objectComponents.verified.count,
    inspectedExistingComponent: inspectedComponent
  };

  if (!keepObjects) {
    for (const id of createdIds.reverse()) {
      try {
        await bridge.send("gameobject.destroy", { id });
      } catch {
        // Smoke cleanup is best-effort because the test may have already destroyed an object.
      }
    }
  }

  console.log(JSON.stringify({ ok: true, summary }, null, 2));
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function createObject(name: string, position: { x: number; y: number; z: number }): Promise<GameObjectSummary> {
  const result = await bridge.send<VerifiedEnvelope<GameObjectSummary>>("gameobject.create", {
    name,
    position
  });

  return result.verified;
}

async function inspectExistingComponent(): Promise<Record<string, unknown> | null> {
  const sceneSummary = await bridge.send<SceneSummaryResult>("scene.summary");
  const firstComponentType = sceneSummary.verified.componentCounts[0]?.type;

  if (!firstComponentType) {
    return null;
  }

  const found = await bridge.send<SceneFindResult>("scene.find", {
    componentContains: firstComponentType,
    includeDisabled: true,
    maxResults: 1
  });
  const firstObject = found.verified.results[0];

  if (!firstObject) {
    return null;
  }

  const componentList = await bridge.send<ComponentListResult>("component.list_on_gameobject", {
    gameObjectId: firstObject.id
  });
  const firstComponent = componentList.verified.components[0];

  if (!firstComponent) {
    return null;
  }

  const properties = await bridge.send<{ verified: { count: number } }>("component.get_properties", {
    id: firstComponent.id,
    includeAll: false,
    maxProperties: 20
  });
  await bridge.send("component.get", {
    id: firstComponent.id
  });

  return {
    gameObjectId: firstObject.id,
    componentId: firstComponent.id,
    componentType: firstComponent.type,
    propertyCount: properties.verified.count
  };
}
