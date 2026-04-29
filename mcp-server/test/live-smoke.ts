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

  const mutationFixture = await runMutationFixtureSmoke(source.id, parent.id, stamp);
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
    mutationFixture,
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

async function runMutationFixtureSmoke(
  gameObjectId: string,
  referenceGameObjectId: string,
  stamp: string
): Promise<Record<string, unknown>> {
  const fixtureTypes = await bridge.send<{ verified: { count: number } }>("component.list_types", {
    query: "AgentBridgeMutationFixture",
    maxResults: 5
  });
  ensure(
    fixtureTypes.verified.count >= 1,
    "AgentBridgeMutationFixture is not available. Copy the bridge library into the project and wait for hotload."
  );

  const added = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId,
    type: "AgentBridgeMutationFixture",
    startEnabled: true
  });
  const componentId = added.verified.component.id;

  await bridge.send("component.get", {
    id: componentId
  });

  const disabledComponent = await bridge.send<ComponentMutationResult>("component.set_enabled", {
    id: componentId,
    enabled: false
  });
  const enabledComponent = await bridge.send<ComponentMutationResult>("component.set_enabled", {
    id: componentId,
    enabled: true
  });
  ensure(disabledComponent.verified.component.enabled === false, "component.set_enabled false did not read back false");
  ensure(enabledComponent.verified.component.enabled === true, "component.set_enabled true did not read back true");

  const stringValue = `smoke-${stamp}`;
  const stringSet = await setFixtureProperty(componentId, "StringValue", stringValue);
  const boolSet = await setFixtureProperty(componentId, "BoolValue", true);
  const intSet = await setFixtureProperty(componentId, "IntValue", 42);
  const uintSet = await setFixtureProperty(componentId, "UIntValue", 43);
  const longSet = await setFixtureProperty(componentId, "LongValue", "123456789");
  const floatSet = await setFixtureProperty(componentId, "FloatValue", 12.5);
  const doubleSet = await setFixtureProperty(componentId, "DoubleValue", 987.125);
  const enumSet = await setFixtureProperty(componentId, "EnumValue", "Complete");
  const vector2Set = await setFixtureProperty(componentId, "Vector2Value", { x: 1.25, y: 2.5 });
  const vector3Set = await setFixtureProperty(componentId, "Vector3Value", { x: 3.5, y: 4.75, z: 5.25 });
  const rotationSet = await setFixtureProperty(componentId, "RotationValue", { pitch: 10, yaw: 20, roll: 30 });
  const anglesSet = await setFixtureProperty(componentId, "AnglesValue", { pitch: 15, yaw: 25, roll: 35 });
  const transformSet = await setFixtureProperty(componentId, "TransformValue", {
    position: { x: 6, y: 7, z: 8 },
    rotation: { pitch: 1, yaw: 2, roll: 3 },
    scale: { x: 1.5, y: 2, z: 2.5 }
  });
  const colorSet = await setFixtureProperty(componentId, "ColorValue", { r: 0.2, g: 0.4, b: 0.6, a: 0.8 });
  const gameObjectReferenceSet = await setFixtureProperty(componentId, "GameObjectReference", referenceGameObjectId);
  const componentReferenceSet = await setFixtureProperty(componentId, "ComponentReference", componentId);

  ensure(propertyValue(stringSet) === stringValue, "StringValue did not read back expected string");
  ensure(propertyValue(boolSet) === true, "BoolValue did not read back true");
  ensure(propertyValue(intSet) === 42, "IntValue did not read back 42");
  ensure(propertyValue(uintSet) === 43, "UIntValue did not read back 43");
  ensure(propertyValue(longSet) === 123456789, "LongValue did not read back 123456789");
  ensureClose(propertyValue(floatSet), 12.5, "FloatValue did not read back 12.5");
  ensureClose(propertyValue(doubleSet), 987.125, "DoubleValue did not read back 987.125");
  ensure(propertyValue(enumSet) === "Complete", "EnumValue did not read back Complete");
  ensureVector(propertyValue(vector2Set), { x: 1.25, y: 2.5 }, "Vector2Value");
  ensureVector(propertyValue(vector3Set), { x: 3.5, y: 4.75, z: 5.25 }, "Vector3Value");
  ensureVector((propertyValue(rotationSet) as JsonObject).angles, { pitch: 10, yaw: 20, roll: 30 }, "RotationValue angles");
  ensureVector(propertyValue(anglesSet), { pitch: 15, yaw: 25, roll: 35 }, "AnglesValue");

  const transform = propertyValue(transformSet) as JsonObject;
  ensureVector(transform.position, { x: 6, y: 7, z: 8 }, "TransformValue position");
  ensureVector(transform.scale, { x: 1.5, y: 2, z: 2.5 }, "TransformValue scale");
  ensureColor(propertyValue(colorSet), { r: 0.2, g: 0.4, b: 0.6, a: 0.8 }, "ColorValue");
  ensure((propertyValue(gameObjectReferenceSet) as JsonObject).id === referenceGameObjectId, "GameObjectReference id mismatch");
  ensure((propertyValue(componentReferenceSet) as JsonObject).id === componentId, "ComponentReference id mismatch");

  const properties = await bridge.send<{ verified: { count: number } }>("component.get_properties", {
    id: componentId,
    includeAll: false,
    maxProperties: 50
  });

  await bridge.send("component.remove", {
    id: componentId
  });
  const removedComponentGetFailed = await rejectsBridgeCommand(() =>
    bridge.send("component.get", {
      id: componentId
    })
  );
  const componentUndo = await bridge.send<{ verified: { undone: boolean } }>("editor.undo");
  await bridge.send("component.get", {
    id: componentId
  });
  const componentRedo = await bridge.send<{ verified: { redone: boolean } }>("editor.redo");
  const removedAgainGetFailed = await rejectsBridgeCommand(() =>
    bridge.send("component.get", {
      id: componentId
    })
  );

  ensure(removedComponentGetFailed, "component.remove did not make component.get fail");
  ensure(componentUndo.verified.undone, "editor.undo did not restore removed component");
  ensure(componentRedo.verified.redone, "editor.redo did not re-remove component");
  ensure(removedAgainGetFailed, "component.get succeeded after redo removed the component");

  return {
    componentId,
    disabledReadback: disabledComponent.verified.component.enabled,
    enabledReadback: enabledComponent.verified.component.enabled,
    propertyCount: properties.verified.count,
    removedComponentGetFailed,
    componentUndoApplied: componentUndo.verified.undone,
    componentRedoApplied: componentRedo.verified.redone,
    removedAgainGetFailed,
    values: {
      string: propertyValue(stringSet),
      bool: propertyValue(boolSet),
      int: propertyValue(intSet),
      uint: propertyValue(uintSet),
      long: propertyValue(longSet),
      float: propertyValue(floatSet),
      double: propertyValue(doubleSet),
      enum: propertyValue(enumSet),
      vector2: propertyValue(vector2Set),
      vector3: propertyValue(vector3Set),
      rotation: propertyValue(rotationSet),
      angles: propertyValue(anglesSet),
      transform: propertyValue(transformSet),
      color: propertyValue(colorSet),
      gameObjectReference: propertyValue(gameObjectReferenceSet),
      componentReference: propertyValue(componentReferenceSet)
    }
  };
}

async function setFixtureProperty(componentId: string, property: string, value: unknown): Promise<ComponentMutationResult> {
  return await bridge.send<ComponentMutationResult>("component.set_property", {
    id: componentId,
    property,
    value
  });
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

function propertyValue(result: ComponentMutationResult): unknown {
  return result.verified.property?.value?.value;
}

interface JsonObject {
  [key: string]: unknown;
}

function ensureClose(actual: unknown, expected: number, label: string): void {
  ensure(typeof actual === "number", `${label} did not read back as a number`);
  ensure(Math.abs(actual - expected) < 0.001, `${label} expected ${expected}, got ${actual}`);
}

function ensureVector(actual: unknown, expected: Record<string, number>, label: string): void {
  ensure(typeof actual === "object" && actual !== null, `${label} did not read back as an object`);
  const values = actual as Record<string, unknown>;

  for (const [key, value] of Object.entries(expected)) {
    ensureClose(values[key], value, `${label}.${key}`);
  }
}

function ensureColor(actual: unknown, expected: Record<string, number>, label: string): void {
  ensureVector(actual, expected, label);
}
