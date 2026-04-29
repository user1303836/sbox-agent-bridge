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

interface ComponentMutationResult {
  verified: {
    component: {
      id: string;
      type: string;
      enabled: boolean;
    };
    property?: {
      value?: {
        type: string;
        value: unknown;
      };
    };
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

  const addedComponent = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId: source.id,
    type: "CameraComponent",
    startEnabled: true
  });
  const addedComponentId = addedComponent.verified.component.id;

  await bridge.send("component.get", {
    id: addedComponentId
  });
  const disabledComponent = await bridge.send<ComponentMutationResult>("component.set_enabled", {
    id: addedComponentId,
    enabled: false
  });
  const enabledComponent = await bridge.send<ComponentMutationResult>("component.set_enabled", {
    id: addedComponentId,
    enabled: true
  });
  const fovSet = await bridge.send<ComponentMutationResult>("component.set_property", {
    id: addedComponentId,
    property: "FieldOfView",
    value: 80
  });
  const orthographicSet = await bridge.send<ComponentMutationResult>("component.set_property", {
    id: addedComponentId,
    property: "Orthographic",
    value: true
  });
  const backgroundSet = await bridge.send<ComponentMutationResult>("component.set_property", {
    id: addedComponentId,
    property: "BackgroundColor",
    value: { r: 0.1, g: 0.2, b: 0.3, a: 1 }
  });
  const addedProperties = await bridge.send<{ verified: { count: number } }>("component.get_properties", {
    id: addedComponentId,
    includeAll: false,
    maxProperties: 30
  });
  await bridge.send("component.remove", {
    id: addedComponentId
  });
  const removedComponentGetFailed = await rejectsBridgeCommand(() =>
    bridge.send("component.get", {
      id: addedComponentId
    })
  );
  const componentUndo = await bridge.send<{ verified: { undone: boolean } }>("editor.undo");
  await bridge.send("component.get", {
    id: addedComponentId
  });
  const componentRedo = await bridge.send<{ verified: { redone: boolean } }>("editor.redo");
  const removedAgainGetFailed = await rejectsBridgeCommand(() =>
    bridge.send("component.get", {
      id: addedComponentId
    })
  );
  ensure(disabledComponent.verified.component.enabled === false, "component.set_enabled false did not read back false");
  ensure(enabledComponent.verified.component.enabled === true, "component.set_enabled true did not read back true");
  ensure(fovSet.verified.property?.value?.value === 80, "FieldOfView did not read back as 80");
  ensure(orthographicSet.verified.property?.value?.value === true, "Orthographic did not read back as true");
  ensure(removedComponentGetFailed, "component.remove did not make component.get fail");
  ensure(componentUndo.verified.undone, "editor.undo did not restore removed component");
  ensure(componentRedo.verified.redone, "editor.redo did not re-remove component");
  ensure(removedAgainGetFailed, "component.get succeeded after redo removed the component");

  const optionalStringProperty = await tryStringPropertySmoke(source.id, stamp);
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
    addedComponentId,
    disabledReadback: disabledComponent.verified.component.enabled,
    enabledReadback: enabledComponent.verified.component.enabled,
    fieldOfView: fovSet.verified.property?.value?.value,
    orthographic: orthographicSet.verified.property?.value?.value,
    backgroundColor: backgroundSet.verified.property?.value?.value,
    addedPropertyCount: addedProperties.verified.count,
    removedComponentGetFailed,
    componentUndoApplied: componentUndo.verified.undone,
    componentRedoApplied: componentRedo.verified.redone,
    removedAgainGetFailed,
    optionalStringProperty,
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

async function tryStringPropertySmoke(gameObjectId: string, stamp: string): Promise<Record<string, unknown> | null> {
  const customTypes = await bridge.send<{ verified: { count: number } }>("component.list_types", {
    query: "MyComponent",
    maxResults: 5
  });

  if (customTypes.verified.count < 1) {
    return null;
  }

  const added = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId,
    type: "MyComponent",
    startEnabled: true
  });
  const componentId = added.verified.component.id;
  const expected = `smoke-${stamp}`;

  const set = await bridge.send<ComponentMutationResult>("component.set_property", {
    id: componentId,
    property: "StringProperty",
    value: expected
  });

  ensure(set.verified.property?.value?.value === expected, "StringProperty did not read back expected string");

  await bridge.send("component.remove", {
    id: componentId
  });

  return {
    componentId,
    value: set.verified.property?.value?.value
  };
}

async function rejectsBridgeCommand(command: () => Promise<unknown>): Promise<boolean> {
  try {
    await command();
    return false;
  } catch {
    return true;
  }
}

function ensure(condition: boolean, message: string): void {
  if (!condition) {
    throw new Error(message);
  }
}
