import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForStopped } from "../src/wait-helpers.js";

interface GameObjectResult {
  verified: {
    id: string;
    name: string;
  };
}

interface ScriptCreateResult {
  verified: {
    path: string;
    exists: boolean;
    length: number;
    sha256: string;
  };
}

interface ScriptEditResult {
  verified: {
    before: {
      sha256: string;
      length: number;
    };
    after: {
      sha256: string;
      length: number;
      exists: boolean;
    };
  };
}

interface ScriptDeleteResult {
  verified: {
    path: string;
    existedBefore: boolean;
    existsAfter: boolean;
    before: {
      sha256: string;
      length: number;
    } | null;
  };
}

interface AssetInfoResult {
  verified: {
    asset: {
      path: string;
      resourceType: string;
      isCompiled: boolean;
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

interface ComponentMutationResult {
  verified: {
    creationMode?: string;
    component: {
      id: string;
      type: string;
      fullType: string;
      enabled: boolean;
      active: boolean;
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
    total: number;
    properties: ComponentPropertyResult[];
  };
}

interface ComponentPropertyResult {
  metadata: {
    name: string;
    setPropertySupported: boolean;
    schema: {
      kind: string;
      enumValues: string[];
      supported: boolean;
    };
  };
  value?: {
    type: string;
    value: unknown;
  };
}

type JsonObject = Record<string, unknown>;

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_RUNTIME_SCENE ?? "scenes/minimal.scene";
const discardUnsaved = process.env.SBOX_AGENT_BRIDGE_DISCARD_UNSAVED === "1";
const scratchScriptPath =
  process.env.SBOX_AGENT_BRIDGE_CAPABILITY_SMOKE_SCRIPT ?? "AgentBridgeScratch/CapabilityGapSmokeFixture.cs";
const citizenModelPath = process.env.SBOX_AGENT_BRIDGE_CITIZEN_MODEL ?? "models/citizen/citizen.vmdl";

let animationRootId = "";
let particleRootId = "";

try {
  await bridge.send("editor.stop", { stopAll: true });
  const stopped = await waitForStopped(bridge, { timeoutMs: 5_000 });
  ensure(stopped.verified.satisfied, "editor.wait_stopped did not settle before capability-gap smoke");

  const initialCompile = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(initialCompile.verified.satisfied, "editor.wait_compile did not settle before capability-gap smoke");
  ensure(initialCompile.verified.errorCount === 0, "Project had compile errors before capability-gap smoke");

  const scriptVerification = await verifyScriptDelete();

  await bridge.send("editor.open_scene", {
    path: scenePath,
    forceReload: true,
    discardUnsaved,
    bringToFront: true
  });

  const animationVerification = await verifyAnimationHelpers();
  const particleVerification = await verifyParticleStack();

  await cleanup();

  const finalCompile = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(finalCompile.verified.satisfied, "editor.wait_compile did not settle after capability-gap smoke");
  ensure(finalCompile.verified.errorCount === 0, "Project had compile errors after capability-gap smoke");

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        compileWaitMs: {
          initial: initialCompile.verified.elapsedMs,
          final: finalCompile.verified.elapsedMs
        },
        script: scriptVerification,
        animation: animationVerification,
        particles: particleVerification
      },
      null,
      2
    )
  );
} catch (error) {
  await cleanup();

  try {
    await bridge.send("script.delete", { path: scratchScriptPath });
  } catch {
    // Best-effort cleanup for a focused smoke script.
  }

  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function verifyScriptDelete(): Promise<Record<string, unknown>> {
  const createdContent = buildScratchScriptContent("created");
  const editedContent = buildScratchScriptContent("edited");
  const invalidContent = buildInvalidScratchScriptContent();

  const beforeCreateSequence = await getLatestCompileSequence();
  const created = await bridge.send<ScriptCreateResult>("script.create", {
    path: scratchScriptPath,
    content: createdContent,
    overwrite: true
  });
  ensure(created.verified.exists, "script.create did not read back the scratch script");
  ensure(created.verified.length === Buffer.byteLength(createdContent), "script.create length read-back did not match");

  const createdCompile = await waitForNextCompile("script.create", beforeCreateSequence);

  const beforeEditSequence = await getLatestCompileSequence();
  const edited = await bridge.send<ScriptEditResult>("script.edit", {
    path: scratchScriptPath,
    content: editedContent
  });
  ensure(edited.verified.after.exists, "script.edit did not read back the scratch script");
  ensure(edited.verified.before.sha256 !== edited.verified.after.sha256, "script.edit did not change the scratch script hash");
  ensure(edited.verified.after.length === Buffer.byteLength(editedContent), "script.edit length read-back did not match");

  const editedCompile = await waitForNextCompile("script.edit", beforeEditSequence);

  const beforeDeleteSequence = await getLatestCompileSequence();
  const deleted = await bridge.send<ScriptDeleteResult>("script.delete", {
    path: scratchScriptPath
  });
  ensure(deleted.verified.existedBefore, "script.delete did not report the scratch script as existing before delete");
  ensure(deleted.verified.existsAfter === false, "script.delete left the scratch script on disk");
  ensure(deleted.verified.before?.sha256 === edited.verified.after.sha256, "script.delete before hash did not match edited script");

  const deletedCompile = await waitForNextCompile("script.delete", beforeDeleteSequence);

  const beforeInvalidCreateSequence = await getLatestCompileSequence();
  const invalidCreated = await bridge.send<ScriptCreateResult>("script.create", {
    path: scratchScriptPath,
    content: invalidContent,
    overwrite: true
  });
  ensure(invalidCreated.verified.exists, "script.create did not read back the invalid scratch script");

  const invalidCompile = await waitForNextCompile("invalid script.create", beforeInvalidCreateSequence, {
    expectErrors: true
  });
  ensure(invalidCompile.verified.errorCount > 0, "Invalid scratch script did not produce compile errors");

  const beforeInvalidDeleteSequence = await getLatestCompileSequence();
  const invalidDeleted = await bridge.send<ScriptDeleteResult>("script.delete", {
    path: scratchScriptPath
  });
  ensure(invalidDeleted.verified.existedBefore, "script.delete did not report the invalid scratch script before delete");
  ensure(invalidDeleted.verified.existsAfter === false, "script.delete left the invalid scratch script on disk");

  const invalidDeletedCompile = await waitForNextCompile("invalid script.delete", beforeInvalidDeleteSequence);

  return {
    path: deleted.verified.path,
    createLength: created.verified.length,
    editHashChanged: edited.verified.before.sha256 !== edited.verified.after.sha256,
    existedBeforeDelete: deleted.verified.existedBefore,
    existsAfterDelete: deleted.verified.existsAfter,
    diagnostics: {
      invalidScriptProducedErrors: invalidCompile.verified.errorCount > 0,
      invalidScriptErrors: invalidCompile.verified.errorCount,
      invalidScriptExistsAfterDelete: invalidDeleted.verified.existsAfter
    },
    compileWaitMs: {
      create: createdCompile.verified.elapsedMs,
      edit: editedCompile.verified.elapsedMs,
      delete: deletedCompile.verified.elapsedMs,
      invalidCreate: invalidCompile.verified.elapsedMs,
      invalidDelete: invalidDeletedCompile.verified.elapsedMs
    }
  };
}

async function verifyAnimationHelpers(): Promise<Record<string, unknown>> {
  const modelInfo = await bridge.send<AssetInfoResult>("asset.get_info", { path: citizenModelPath });
  ensure(modelInfo.verified.asset.resourceType === "Sandbox.Model", "Citizen model asset did not resolve as a model");
  ensure(modelInfo.verified.asset.isCompiled, "Citizen model asset is not compiled");

  const root = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Animation Capability Smoke",
    position: { x: 672, y: -512, z: 96 }
  });
  animationRootId = root.verified.id;

  const renderer = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId: animationRootId,
    type: "SkinnedModelRenderer",
    startEnabled: true
  });
  ensure(renderer.verified.creationMode === "typeLibrary", "SkinnedModelRenderer was not created through TypeLibrary");
  ensure(renderer.verified.component.active, "SkinnedModelRenderer did not read back active");

  const helper = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId: animationRootId,
    type: "CitizenAnimationHelper",
    startEnabled: true
  });
  ensure(helper.verified.creationMode === "typeLibrary", "CitizenAnimationHelper was not created through TypeLibrary");
  ensure(helper.verified.component.active, "CitizenAnimationHelper did not read back active");

  const rendererProperties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: renderer.verified.component.id,
    includeAll: false,
    maxProperties: 80
  });
  const helperProperties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: helper.verified.component.id,
    includeAll: false,
    maxProperties: 80
  });

  requireWritableProperty(rendererProperties, "Model", "resourceReference");
  requireWritableProperty(rendererProperties, "Tint", "color");
  requireWritableProperty(rendererProperties, "PlaybackRate", "number");
  requireWritableProperty(rendererProperties, "UseAnimGraph", "bool");
  requireWritableProperty(rendererProperties, "RenderType", "enum");
  requireWritableProperty(helperProperties, "Target", "componentReference");
  requireWritableProperty(helperProperties, "Height", "number");
  requireWritableProperty(helperProperties, "LookAtEnabled", "bool");
  requireWritableProperty(helperProperties, "LookAt", "gameObjectReference");

  const modelSet = await setProperty(renderer.verified.component.id, "Model", citizenModelPath);
  const tintSet = await setProperty(renderer.verified.component.id, "Tint", { r: 0.18, g: 0.32, b: 0.72, a: 1 });
  const playbackSet = await setProperty(renderer.verified.component.id, "PlaybackRate", 1.2);
  const graphSet = await setProperty(renderer.verified.component.id, "UseAnimGraph", false);
  const shadowSet = await setProperty(renderer.verified.component.id, "RenderType", "Off");
  const targetSet = await setProperty(helper.verified.component.id, "Target", renderer.verified.component.id);
  const heightSet = await setProperty(helper.verified.component.id, "Height", 1.05);
  const lookAtEnabledSet = await setProperty(helper.verified.component.id, "LookAtEnabled", true);
  const lookAtSet = await setProperty(helper.verified.component.id, "LookAt", animationRootId);

  ensureResourcePath(propertyValue(modelSet), citizenModelPath, "SkinnedModelRenderer.Model");
  ensureColor(propertyValue(tintSet), { r: 0.18, g: 0.32, b: 0.72, a: 1 }, "SkinnedModelRenderer.Tint");
  ensureClose(propertyValue(playbackSet), 1.2, "SkinnedModelRenderer.PlaybackRate");
  ensure(propertyValue(graphSet) === false, "SkinnedModelRenderer.UseAnimGraph did not read back false");
  ensure(propertyValue(shadowSet) === "Off", "SkinnedModelRenderer.RenderType did not read back Off");
  ensure((propertyValue(targetSet) as JsonObject).id === renderer.verified.component.id, "CitizenAnimationHelper.Target id mismatch");
  ensureClose(propertyValue(heightSet), 1.05, "CitizenAnimationHelper.Height");
  ensure(propertyValue(lookAtEnabledSet) === true, "CitizenAnimationHelper.LookAtEnabled did not read back true");
  ensure((propertyValue(lookAtSet) as JsonObject).id === animationRootId, "CitizenAnimationHelper.LookAt id mismatch");

  return {
    root: root.verified,
    modelPath: modelInfo.verified.asset.path,
    components: {
      renderer: renderer.verified.component,
      helper: helper.verified.component
    },
    propertyCounts: {
      renderer: rendererProperties.verified.count,
      helper: helperProperties.verified.count
    },
    readBack: {
      model: propertyValue(modelSet),
      tint: propertyValue(tintSet),
      playbackRate: propertyValue(playbackSet),
      useAnimGraph: propertyValue(graphSet),
      renderType: propertyValue(shadowSet),
      target: propertyValue(targetSet),
      height: propertyValue(heightSet),
      lookAtEnabled: propertyValue(lookAtEnabledSet),
      lookAt: propertyValue(lookAtSet)
    }
  };
}

async function verifyParticleStack(): Promise<Record<string, unknown>> {
  const root = await bridge.send<GameObjectResult>("gameobject.create", {
    name: "Agent Bridge Particle Capability Smoke",
    position: { x: 768, y: -512, z: 96 }
  });
  particleRootId = root.verified.id;

  const effect = await addComponent(particleRootId, "ParticleEffect");
  const emitter = await addComponent(particleRootId, "ParticleConeEmitter");
  const sprite = await addComponent(particleRootId, "ParticleSpriteRenderer");
  const light = await addComponent(particleRootId, "ParticleLightRenderer");

  const effectProperties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: effect.verified.component.id,
    includeAll: false,
    maxProperties: 100
  });
  const emitterProperties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: emitter.verified.component.id,
    includeAll: false,
    maxProperties: 80
  });
  const spriteProperties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: sprite.verified.component.id,
    includeAll: false,
    maxProperties: 80
  });
  const lightProperties = await bridge.send<ComponentPropertiesResult>("component.get_properties", {
    id: light.verified.component.id,
    includeAll: false,
    maxProperties: 80
  });

  requireWritableProperty(effectProperties, "MaxParticles", "integer");
  requireWritableProperty(effectProperties, "Tint", "color");
  requireWritableProperty(effectProperties, "ApplyColor", "bool");
  requireWritableProperty(effectProperties, "ForceDirection", "vector3");
  requireWritableProperty(effectProperties, "Space", "enum");
  requireWritableProperty(emitterProperties, "Loop", "bool");
  requireWritableProperty(emitterProperties, "InVolume", "bool");
  requireWritableProperty(spriteProperties, "Additive", "bool");
  requireWritableProperty(spriteProperties, "Alignment", "enum");
  requireWritableProperty(spriteProperties, "Scale", "number");
  requireWritableProperty(spriteProperties, "TextureFilter", "enum");
  requireWritableProperty(lightProperties, "MaximumLights", "integer");
  requireWritableProperty(lightProperties, "Ratio", "number");
  requireWritableProperty(lightProperties, "UseParticleColor", "bool");

  const maxParticlesSet = await setProperty(effect.verified.component.id, "MaxParticles", 128);
  const tintSet = await setProperty(effect.verified.component.id, "Tint", { r: 0.95, g: 0.38, b: 0.1, a: 0.85 });
  const applyColorSet = await setProperty(effect.verified.component.id, "ApplyColor", true);
  const forceSet = await setProperty(effect.verified.component.id, "ForceDirection", { x: 0, y: 0, z: 96 });
  const spaceSet = await setProperty(effect.verified.component.id, "Space", "Local");
  const loopSet = await setProperty(emitter.verified.component.id, "Loop", false);
  const volumeSet = await setProperty(emitter.verified.component.id, "InVolume", true);
  const additiveSet = await setProperty(sprite.verified.component.id, "Additive", true);
  const alignmentSet = await setProperty(sprite.verified.component.id, "Alignment", "Object");
  const scaleSet = await setProperty(sprite.verified.component.id, "Scale", 1.75);
  const filterSet = await setProperty(sprite.verified.component.id, "TextureFilter", "Point");
  const maxLightsSet = await setProperty(light.verified.component.id, "MaximumLights", 3);
  const ratioSet = await setProperty(light.verified.component.id, "Ratio", 0.5);
  const particleColorSet = await setProperty(light.verified.component.id, "UseParticleColor", false);

  ensure(propertyValue(maxParticlesSet) === 128, "ParticleEffect.MaxParticles did not read back 128");
  ensureColor(propertyValue(tintSet), { r: 0.95, g: 0.38, b: 0.1, a: 0.85 }, "ParticleEffect.Tint");
  ensure(propertyValue(applyColorSet) === true, "ParticleEffect.ApplyColor did not read back true");
  ensureVector(propertyValue(forceSet), { x: 0, y: 0, z: 96 }, "ParticleEffect.ForceDirection");
  ensure(propertyValue(spaceSet) === "Local", "ParticleEffect.Space did not read back Local");
  ensure(propertyValue(loopSet) === false, "ParticleConeEmitter.Loop did not read back false");
  ensure(propertyValue(volumeSet) === true, "ParticleConeEmitter.InVolume did not read back true");
  ensure(propertyValue(additiveSet) === true, "ParticleSpriteRenderer.Additive did not read back true");
  ensure(propertyValue(alignmentSet) === "Object", "ParticleSpriteRenderer.Alignment did not read back Object");
  ensureClose(propertyValue(scaleSet), 1.75, "ParticleSpriteRenderer.Scale");
  ensure(propertyValue(filterSet) === "Point", "ParticleSpriteRenderer.TextureFilter did not read back Point");
  ensure(propertyValue(maxLightsSet) === 3, "ParticleLightRenderer.MaximumLights did not read back 3");
  ensureClose(propertyValue(ratioSet), 0.5, "ParticleLightRenderer.Ratio");
  ensure(propertyValue(particleColorSet) === false, "ParticleLightRenderer.UseParticleColor did not read back false");

  return {
    root: root.verified,
    components: {
      effect: effect.verified.component,
      emitter: emitter.verified.component,
      sprite: sprite.verified.component,
      light: light.verified.component
    },
    propertyCounts: {
      effect: effectProperties.verified.count,
      emitter: emitterProperties.verified.count,
      sprite: spriteProperties.verified.count,
      light: lightProperties.verified.count
    },
    readBack: {
      maxParticles: propertyValue(maxParticlesSet),
      tint: propertyValue(tintSet),
      applyColor: propertyValue(applyColorSet),
      forceDirection: propertyValue(forceSet),
      space: propertyValue(spaceSet),
      loop: propertyValue(loopSet),
      inVolume: propertyValue(volumeSet),
      additive: propertyValue(additiveSet),
      alignment: propertyValue(alignmentSet),
      scale: propertyValue(scaleSet),
      textureFilter: propertyValue(filterSet),
      maximumLights: propertyValue(maxLightsSet),
      ratio: propertyValue(ratioSet),
      useParticleColor: propertyValue(particleColorSet)
    }
  };
}

async function addComponent(gameObjectId: string, type: string): Promise<ComponentMutationResult> {
  const result = await bridge.send<ComponentMutationResult>("component.add", {
    gameObjectId,
    type,
    startEnabled: true
  });
  ensure(result.verified.creationMode === "typeLibrary", `${type} was not created through TypeLibrary`);
  ensure(result.verified.component.active, `${type} did not read back active`);

  return result;
}

async function setProperty(componentId: string, property: string, value: unknown): Promise<ComponentMutationResult> {
  return await bridge.send<ComponentMutationResult>("component.set_property", {
    id: componentId,
    property,
    value
  });
}

function requireWritableProperty(properties: ComponentPropertiesResult, name: string, kind: string): ComponentPropertyResult {
  const property = properties.verified.properties.find((item) => item.metadata.name === name);
  ensure(property !== undefined, `${name} metadata was not returned by component.get_properties`);
  ensure(property.metadata.schema.kind === kind, `${name} schema kind was ${property.metadata.schema.kind}, expected ${kind}`);
  ensure(property.metadata.schema.supported, `${name} schema was not supported`);
  ensure(property.metadata.setPropertySupported, `${name} was not writable through component.set_property`);

  return property;
}

async function cleanup(): Promise<void> {
  const ids = [animationRootId, particleRootId].filter(Boolean);
  animationRootId = "";
  particleRootId = "";

  for (const id of ids) {
    try {
      await bridge.send("gameobject.destroy", { id });
    } catch {
      try {
        await bridge.send("gameobject.set_enabled", { id, enabled: false });
      } catch {
        // Best-effort cleanup for a focused smoke script.
      }
    }
  }
}

function buildScratchScriptContent(label: string): string {
  return `using Sandbox;

public sealed class AgentBridgeCapabilityGapSmokeFixture : Component
{
\t[Property] public string Label { get; set; } = "${label}";
}
`;
}

function buildInvalidScratchScriptContent(): string {
  return `using Sandbox;

public sealed class AgentBridgeCapabilityGapSmokeFixture : Component
{
\t[Property] public int Broken { get; set; } = ;
}
`;
}

async function getLatestCompileSequence(): Promise<number> {
  const status = await bridge.send<CompileStatusResult>("editor.compile_status", { maxDiagnostics: 0 });
  const sequences = status.verified.groups.map((group) => group.sequence ?? 0);

  return sequences.length > 0 ? Math.max(...sequences) : -1;
}

async function waitForNextCompile(
  label: string,
  sinceSequence: number,
  options: { expectErrors?: boolean } = {}
): Promise<Awaited<ReturnType<typeof waitForCompile>>> {
  const compile = await waitForCompile(bridge, {
    sinceSequence,
    timeoutMs: 30_000,
    maxDiagnostics: 20
  });

  ensure(compile.verified.satisfied, `editor.wait_compile did not settle after ${label}`);
  ensure(compile.verified.hasRequiredSequence, `editor.wait_compile did not observe a new compile sequence after ${label}`);

  if (options.expectErrors) {
    ensure(compile.verified.errorCount > 0, `${label} did not produce compile errors`);
  } else {
    ensure(compile.verified.errorCount === 0, `${label} introduced compile errors`);
  }

  return compile;
}

function propertyValue(result: ComponentMutationResult): unknown {
  return result.verified.property?.value?.value;
}

function ensureResourcePath(value: unknown, path: string, label: string): void {
  ensure(typeof value === "object" && value !== null, `${label} did not read back a resource object`);
  const resource = value as { path?: string; resourcePath?: string };
  ensure(resource.path === path || resource.resourcePath === path, `${label} path mismatch`);
}

function ensureColor(value: unknown, expected: { r: number; g: number; b: number; a: number }, label: string): void {
  ensure(typeof value === "object" && value !== null, `${label} did not read back a color object`);
  const color = value as { r?: number; g?: number; b?: number; a?: number };
  ensureClose(color.r, expected.r, `${label}.r`);
  ensureClose(color.g, expected.g, `${label}.g`);
  ensureClose(color.b, expected.b, `${label}.b`);
  ensureClose(color.a, expected.a, `${label}.a`);
}

function ensureVector(value: unknown, expected: { x: number; y: number; z: number }, label: string): void {
  ensure(typeof value === "object" && value !== null, `${label} did not read back a vector object`);
  const vector = value as { x?: number; y?: number; z?: number };
  ensureClose(vector.x, expected.x, `${label}.x`);
  ensureClose(vector.y, expected.y, `${label}.y`);
  ensureClose(vector.z, expected.z, `${label}.z`);
}

function ensureClose(actual: unknown, expected: number, label: string): void {
  ensure(typeof actual === "number", `${label} did not read back a number`);
  ensure(Math.abs(actual - expected) < 0.001, `${label} expected ${expected}, got ${actual}`);
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
