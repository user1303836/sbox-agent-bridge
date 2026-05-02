import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile } from "../src/wait-helpers.js";

interface AssetTypesResult {
  verified: {
    count: number;
    gameResourceCount: number;
    results: Array<{
      friendlyName: string;
      fileExtension: string;
      fileExtensions: string[];
      isGameResource: boolean;
      resourceType: string;
    }>;
  };
}

interface ResourceCreateResult {
  verified: {
    assetType: string;
    path: string;
    asset: {
      asset: {
        relativePath: string;
        extension: string;
        resourceType: string;
      };
      absolutePath: string;
      hasSourceFile: boolean;
    };
  };
}

interface AssetInfoResult {
  verified: ResourceCreateResult["verified"]["asset"];
}

interface CloudPackagesResult {
  verified: {
    installedCount: number;
    referencedCount: number;
    installed: unknown[];
    referenced: unknown[];
    truncated: boolean;
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

const resourcePath = process.env.SBOX_AGENT_BRIDGE_RESOURCE_SMOKE_PATH ?? "sounds/agent_bridge/smoke/generic_resource_smoke.sound";

try {
  const compileWait = await waitForCompile(bridge, { timeoutMs: 10_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before asset resource/cloud smoke");

  const assetTypes = await bridge.send<AssetTypesResult>("asset.list_types", {
    query: "sound",
    onlyGameResources: true,
    maxResults: 100
  });
  ensure(assetTypes.verified.count > 0, "asset.list_types returned no GameResource asset types");

  const soundType = assetTypes.verified.results.find((type) => {
    return (
      type.fileExtension === "sound" ||
      type.fileExtensions.includes("sound") ||
      type.resourceType.endsWith(".SoundEvent")
    );
  });
  ensure(soundType, "asset.list_types did not expose the SoundEvent GameResource asset type");

  const created = await bridge.send<ResourceCreateResult>("asset.create_resource", {
    path: resourcePath,
    assetType: "sound",
    overwrite: true
  });
  ensure(created.verified.assetType === "sound", "asset.create_resource returned the wrong asset type");
  ensure(created.verified.path.endsWith(".sound"), "asset.create_resource did not normalize the .sound extension");
  ensure(created.verified.asset.asset.relativePath.endsWith("generic_resource_smoke.sound"), "asset.create_resource did not return the expected asset");
  ensure(created.verified.asset.asset.extension === "sound", "created resource did not register as a sound asset");
  ensure(created.verified.asset.hasSourceFile, "created resource did not report a source file");

  const readBack = await bridge.send<AssetInfoResult>("asset.get_info", {
    path: resourcePath
  });
  ensure(readBack.verified.asset.relativePath.endsWith("generic_resource_smoke.sound"), "asset.get_info could not read back the created resource");

  const cloud = await bridge.send<CloudPackagesResult>("asset.cloud_packages", {
    includeInstalled: true,
    includeReferenced: true,
    maxResults: 50
  });
  ensure(Number.isInteger(cloud.verified.installedCount), "asset.cloud_packages did not report installedCount");
  ensure(Number.isInteger(cloud.verified.referencedCount), "asset.cloud_packages did not report referencedCount");
  ensure(Array.isArray(cloud.verified.installed), "asset.cloud_packages did not return installed package metadata");
  ensure(Array.isArray(cloud.verified.referenced), "asset.cloud_packages did not return referenced package metadata");

  console.log(
    JSON.stringify(
      {
        ok: true,
        compileWaitMs: compileWait.verified.elapsedMs,
        assetTypes: {
          count: assetTypes.verified.count,
          gameResourceCount: assetTypes.verified.gameResourceCount,
          soundType
        },
        created: {
          path: created.verified.path,
          relativePath: created.verified.asset.asset.relativePath,
          extension: created.verified.asset.asset.extension,
          resourceType: created.verified.asset.asset.resourceType
        },
        cloud: {
          installedCount: cloud.verified.installedCount,
          referencedCount: cloud.verified.referencedCount,
          truncated: cloud.verified.truncated
        }
      },
      null,
      2
    )
  );
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
