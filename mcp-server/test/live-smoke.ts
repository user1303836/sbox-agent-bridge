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

interface ComponentPropertiesResult {
  verified: {
    count: number;
    properties: ComponentPropertyResult[];
  };
}

interface ComponentPropertyResult {
  metadata: {
    name: string;
    type: string;
    fullType: string;
    canWrite: boolean;
    readOnly: boolean;
    typeConversionSupported: boolean;
    setPropertySupported: boolean;
    schema: {
      kind: string;
      nullable: boolean;
      targetType: string;
      acceptedJson: string[];
      example: unknown;
      enumValues: string[];
      reference?: {
        kind: string;
        type: string;
      } | null;
      supported: boolean;
      unsupportedReason?: string | null;
    };
  };
  value?: {
    type: string;
    value: unknown;
  };
}

interface ComponentValidationResult {
  verified: {
    property: ComponentPropertyResult["metadata"];
    current: ComponentPropertyResult;
    converted: {
      type: string;
      value: unknown;
    };
    mutationApplied: boolean;
    valid: boolean;
  };
}

interface PlayStateResult {
  verified: {
    scene: string;
    isPlaying: boolean;
    hasGameSession: boolean;
  };
}

interface LogsResult {
  verified: {
    source: string;
    exists: boolean;
    readError: string;
    returned: number;
    entries: unknown[];
  };
}

interface CompileStatusResult {
  verified: {
    source: string;
    observedGroupCount: number;
    groups: unknown[];
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 10_000)
});

const stamp = new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14);
const prefix = process.env.SBOX_AGENT_BRIDGE_SMOKE_PREFIX ?? "Agent Bridge Live Smoke";
const keepObjects = process.env.SBOX_AGENT_BRIDGE_SMOKE_KEEP_OBJECTS === "1";
const requireMutationFixture = process.env.SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE === "1";
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

  summary.feedbackLoop = await runFeedbackLoopSmoke();

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

async function runFeedbackLoopSmoke(): Promise<Record<string, unknown>> {
  const before = await bridge.send<PlayStateResult>("editor.play_state");
  const logs = await bridge.send<LogsResult>("editor.logs", {
    maxLines: 20
  });
  const compileStatus = await bridge.send<CompileStatusResult>("editor.compile_status", {
    maxDiagnostics: 10
  });
  const feedback = await bridge.send<{ verified: { playState: { isPlaying: boolean }; logs: LogsResult["verified"]; compileStatus: CompileStatusResult["verified"] } }>(
    "editor.feedback",
    {
      maxLines: 20,
      maxDiagnostics: 10
    }
  );

  ensure(logs.verified.source === "sbox-dev.log", "editor.logs did not identify sbox-dev.log as its source");
  ensure(Array.isArray(logs.verified.entries), "editor.logs entries was not an array");
  ensure(logs.verified.readError === "", `editor.logs returned a read error: ${logs.verified.readError}`);
  ensure(compileStatus.verified.source === "compile.started event observer", "editor.compile_status returned an unexpected source");
  ensure(Array.isArray(compileStatus.verified.groups), "editor.compile_status groups was not an array");
  ensure(feedback.verified.playState.isPlaying === before.verified.isPlaying, "editor.feedback play state disagreed with editor.play_state");
  ensure(feedback.verified.logs.source === logs.verified.source, "editor.feedback logs source disagreed with editor.logs");
  ensure(
    feedback.verified.compileStatus.source === compileStatus.verified.source,
    "editor.feedback compile source disagreed with editor.compile_status"
  );

  if (before.verified.isPlaying) {
    return {
      initialScene: before.verified.scene,
      initialIsPlaying: before.verified.isPlaying,
      playStopSkipped: true,
      reason: "Editor was already playing before the smoke test.",
      logLinesReturned: logs.verified.returned,
      observedCompileGroups: compileStatus.verified.observedGroupCount
    };
  }

  let startedBySmoke = false;

  try {
    await bridge.send("editor.play");
    startedBySmoke = true;
    const afterPlay = await waitForPlayState(true, 2_000);
    await bridge.send("editor.stop");
    startedBySmoke = false;
    const afterStop = await waitForPlayState(false, 2_000);

    return {
      initialScene: before.verified.scene,
      initialIsPlaying: before.verified.isPlaying,
      afterPlayIsPlaying: afterPlay.verified.isPlaying,
      afterPlayHasGameSession: afterPlay.verified.hasGameSession,
      afterStopIsPlaying: afterStop.verified.isPlaying,
      logLinesReturned: logs.verified.returned,
      observedCompileGroups: compileStatus.verified.observedGroupCount
    };
  } finally {
    if (startedBySmoke) {
      await bridge.send("editor.stop");
    }
  }
}

async function waitForPlayState(expected: boolean, timeoutMs: number): Promise<PlayStateResult> {
  const deadline = Date.now() + timeoutMs;
  let last = await bridge.send<PlayStateResult>("editor.play_state");

  while (Date.now() < deadline) {
    if (last.verified.isPlaying === expected) {
      return last;
    }

    await delay(100);
    last = await bridge.send<PlayStateResult>("editor.play_state");
  }

  throw new Error(`Timed out waiting for editor.play_state isPlaying=${expected}; last=${last.verified.isPlaying}`);
}

async function delay(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
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

  if (fixtureTypes.verified.count < 1) {
    ensure(
      !requireMutationFixture,
      "AgentBridgeMutationFixture is not available. Copy the bridge library into the project, wait for hotload, or unset SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE."
    );

    return {
      available: false,
      skipped: true,
      reason:
        "AgentBridgeMutationFixture is not visible through component.list_types in this editor session. Set SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1 to make this a hard failure."
    };
  }

  const added = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId,
    type: "AgentBridgeMutationFixture",
    startEnabled: true
  });
  const componentId = added.verified.component.id;

  await bridge.send("component.get", {
    id: componentId
  });

  const initialProperties = await getFixtureProperties(componentId);
  const schemaSummary = assertFixtureSchemas(initialProperties);
  const floatValidation = await validateFixtureProperty(componentId, "FloatValue", 12.5);
  const stringDryRun = await dryRunSetFixtureProperty(componentId, "StringValue", `dry-${stamp}`);
  const invalidValidationFailed = await rejectsBridgeCommand(() =>
    validateFixtureProperty(componentId, "IntValue", "not-an-int")
  );
  const floatAfterValidation = await getFixturePropertyValue(componentId, "FloatValue");
  const stringAfterDryRun = await getFixturePropertyValue(componentId, "StringValue");

  ensure(floatValidation.verified.valid, "component.validate_property did not report FloatValue as valid");
  ensure(floatValidation.verified.mutationApplied === false, "component.validate_property reported a mutation");
  ensureClose(floatValidation.verified.converted.value, 12.5, "FloatValue validation converted value");
  ensure(stringDryRun.verified.valid, "component.set_property dryRun did not report StringValue as valid");
  ensure(stringDryRun.verified.mutationApplied === false, "component.set_property dryRun reported a mutation");
  ensure(stringDryRun.verified.converted.value === `dry-${stamp}`, "StringValue dryRun converted value mismatch");
  ensure(invalidValidationFailed, "component.validate_property accepted an invalid IntValue");
  ensureClose(floatAfterValidation, 0, "FloatValue changed during validation");
  ensure(stringAfterDryRun === "", "StringValue changed during dryRun validation");

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
  const modelSet = await setFixtureProperty(componentId, "ModelValue", "models/dev/plane_blend.vmdl");
  const materialSet = await setFixtureProperty(componentId, "MaterialValue", "materials/dev/reflectivity_30.vmat");
  const textureSet = await setFixtureProperty(componentId, "TextureValue", "textures/cubemaps/default2.vtex");
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
  ensureResourcePath(propertyValue(modelSet), "models/dev/plane_blend.vmdl", "ModelValue");
  ensureResourcePath(propertyValue(materialSet), "materials/dev/reflectivity_30.vmat", "MaterialValue");
  ensureResourcePath(propertyValue(textureSet), "textures/cubemaps/default2.vtex", "TextureValue");
  ensure((propertyValue(gameObjectReferenceSet) as JsonObject).id === referenceGameObjectId, "GameObjectReference id mismatch");
  ensure((propertyValue(componentReferenceSet) as JsonObject).id === componentId, "ComponentReference id mismatch");

  const properties = await getFixtureProperties(componentId);

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
    schemaSummary,
    validation: {
      floatConverted: floatValidation.verified.converted.value,
      stringDryRunConverted: stringDryRun.verified.converted.value,
      invalidValidationFailed,
      floatAfterValidation,
      stringAfterDryRun
    },
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
      model: propertyValue(modelSet),
      material: propertyValue(materialSet),
      texture: propertyValue(textureSet),
      gameObjectReference: propertyValue(gameObjectReferenceSet),
      componentReference: propertyValue(componentReferenceSet)
    }
  };
}

async function getFixtureProperties(componentId: string): Promise<ComponentPropertiesResult> {
  return await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: componentId,
    includeAll: false,
    maxProperties: 50
  });
}

async function getFixturePropertyValue(componentId: string, property: string): Promise<unknown> {
  const properties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: componentId,
    includeAll: false,
    query: property,
    maxProperties: 5
  });

  return requireProperty(properties, property).value?.value;
}

async function validateFixtureProperty(
  componentId: string,
  property: string,
  value: unknown
): Promise<ComponentValidationResult> {
  return await bridge.send<ComponentValidationResult>("component.validate_property", {
    id: componentId,
    property,
    value
  });
}

async function dryRunSetFixtureProperty(
  componentId: string,
  property: string,
  value: unknown
): Promise<ComponentValidationResult> {
  return await bridge.send<ComponentValidationResult>("component.set_property", {
    id: componentId,
    property,
    value,
    dryRun: true
  });
}

async function setFixtureProperty(componentId: string, property: string, value: unknown): Promise<ComponentMutationResult> {
  return await bridge.send<ComponentMutationResult>("component.set_property", {
    id: componentId,
    property,
    value
  });
}

function assertFixtureSchemas(properties: ComponentPropertiesResult): Record<string, unknown> {
  const stringProperty = requireProperty(properties, "StringValue");
  const intProperty = requireProperty(properties, "IntValue");
  const enumProperty = requireProperty(properties, "EnumValue");
  const vector3Property = requireProperty(properties, "Vector3Value");
  const rotationProperty = requireProperty(properties, "RotationValue");
  const transformProperty = requireProperty(properties, "TransformValue");
  const modelProperty = requireProperty(properties, "ModelValue");
  const materialProperty = requireProperty(properties, "MaterialValue");
  const textureProperty = requireProperty(properties, "TextureValue");
  const gameObjectProperty = requireProperty(properties, "GameObjectReference");
  const componentProperty = requireProperty(properties, "ComponentReference");

  ensureSchema(stringProperty, "string", "string");
  ensureSchema(intProperty, "integer", "integer");
  ensureSchema(enumProperty, "enum", "string enum name");
  ensure(enumProperty.metadata.schema.enumValues.includes("Complete"), "EnumValue schema did not include Complete");
  ensureSchema(vector3Property, "vector3", "object { x: number, y: number, z: number }");
  ensureSchema(rotationProperty, "rotation", "object { pitch?: number, yaw?: number, roll?: number }");
  ensureSchema(transformProperty, "transform", "object { position?: Vector3, rotation?: Rotation, scale?: Vector3 }");
  ensureSchema(modelProperty, "resourceReference", "string resource path");
  ensureSchema(materialProperty, "resourceReference", "string resource path");
  ensureSchema(textureProperty, "resourceReference", "string resource path");
  ensureSchema(gameObjectProperty, "gameObjectReference", "string GameObject id");
  ensureSchema(componentProperty, "componentReference", "string Component id");
  ensure(modelProperty.metadata.schema.reference?.kind === "Resource", "ModelValue schema did not describe a Resource reference");
  ensure(materialProperty.metadata.schema.reference?.kind === "Resource", "MaterialValue schema did not describe a Resource reference");
  ensure(textureProperty.metadata.schema.reference?.kind === "Resource", "TextureValue schema did not describe a Resource reference");
  ensure(gameObjectProperty.metadata.schema.reference?.kind === "GameObject", "GameObjectReference schema did not describe a GameObject reference");
  ensure(componentProperty.metadata.schema.reference?.kind === "Component", "ComponentReference schema did not describe a Component reference");

  return {
    stringKind: stringProperty.metadata.schema.kind,
    intKind: intProperty.metadata.schema.kind,
    enumValues: enumProperty.metadata.schema.enumValues,
    vector3AcceptedJson: vector3Property.metadata.schema.acceptedJson,
    rotationAcceptedJson: rotationProperty.metadata.schema.acceptedJson,
    transformAcceptedJson: transformProperty.metadata.schema.acceptedJson,
    modelResource: modelProperty.metadata.schema.reference,
    materialResource: materialProperty.metadata.schema.reference,
    textureResource: textureProperty.metadata.schema.reference,
    gameObjectReference: gameObjectProperty.metadata.schema.reference,
    componentReference: componentProperty.metadata.schema.reference
  };
}

function requireProperty(properties: ComponentPropertiesResult, property: string): ComponentPropertyResult {
  const found = properties.verified.properties.find((item) => item.metadata.name === property);
  ensure(found !== undefined, `${property} metadata was not returned by component.get_properties`);

  return found;
}

function ensureSchema(property: ComponentPropertyResult, kind: string, acceptedJson: string): void {
  ensure(property.metadata.canWrite, `${property.metadata.name} should be writable`);
  ensure(!property.metadata.readOnly, `${property.metadata.name} should not be read-only`);
  ensure(property.metadata.typeConversionSupported, `${property.metadata.name} should have supported value conversion`);
  ensure(property.metadata.setPropertySupported, `${property.metadata.name} should be supported by set_property`);
  ensure(property.metadata.schema.supported, `${property.metadata.name} schema should be supported`);
  ensure(property.metadata.schema.kind === kind, `${property.metadata.name} schema kind should be ${kind}`);
  ensure(
    property.metadata.schema.acceptedJson.includes(acceptedJson),
    `${property.metadata.name} schema did not include accepted JSON shape: ${acceptedJson}`
  );
}

async function rejectsBridgeCommand(command: () => Promise<unknown>): Promise<boolean> {
  try {
    await command();
    return false;
  } catch {
    return true;
  }
}

function ensure(condition: boolean, message: string): asserts condition {
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

function ensureResourcePath(actual: unknown, expectedPath: string, label: string): void {
  ensure(typeof actual === "object" && actual !== null, `${label} did not read back as a resource object`);
  const value = actual as Record<string, unknown>;
  ensure(typeof value.path === "string", `${label}.path did not read back as a string`);
  ensure(value.path.toLowerCase() === expectedPath.toLowerCase(), `${label} expected ${expectedPath}, got ${value.path}`);
  ensure(value.isValid === true, `${label} did not read back as a valid resource`);
}
