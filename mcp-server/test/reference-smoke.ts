import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile } from "../src/wait-helpers.js";

interface ReferenceSearchResult {
  verified: {
    count: number;
    documentCount: number;
    results: Array<{
      name: string;
      kind: string;
      summary: string;
      assembly: string;
    }>;
  };
}

interface ReferenceTypeResult {
  verified: {
    type: {
      fullName: string;
      properties: Array<{
        name: string;
        type: string;
        canRead: boolean;
        canWrite: boolean;
        summary: string;
      }>;
      fields: Array<{
        name: string;
        type: string;
      }>;
      methods: Array<{
        name: string;
        returnType: string;
      }>;
    };
  };
}

interface ConsoleResult {
  verified: {
    name: string;
    value: string;
    intValue: number;
    floatValue: number;
  };
}

interface WhitelistResult {
  verified: {
    available: boolean;
    count: number;
    results: string[];
    readError: string;
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 15_000)
});

try {
  const compileWait = await waitForCompile(bridge, { timeoutMs: 15_000, maxDiagnostics: 20 });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not settle before reference smoke");
  ensure(compileWait.verified.errorCount === 0, "editor compile status reported errors before reference smoke");

  const networkSearch = await bridge.send<ReferenceSearchResult>("reference.search", {
    query: "GameObject.NetworkMode",
    maxResults: 20
  });
  ensure(networkSearch.verified.documentCount >= 1, "reference.search did not enumerate installed XML docs");
  ensure(
    networkSearch.verified.results.some((entry) => entry.name === "P:Sandbox.GameObject.NetworkMode"),
    "reference.search did not find GameObject.NetworkMode docs"
  );

  const gameObjectType = await bridge.send<ReferenceTypeResult>("reference.type", {
    typeName: "Sandbox.GameObject"
  });
  ensure(gameObjectType.verified.type.fullName === "Sandbox.GameObject", "reference.type did not resolve Sandbox.GameObject");
  ensure(
    gameObjectType.verified.type.properties.some((property) => property.name === "NetworkMode" && property.canWrite),
    "reference.type did not expose writable GameObject.NetworkMode"
  );

  const vectorType = await bridge.send<ReferenceTypeResult>("reference.type", {
    typeName: "Vector3"
  });
  ensure(vectorType.verified.type.fullName === "Vector3" || vectorType.verified.type.fullName === "Sandbox.Vector3", "reference.type did not resolve Vector3");
  ensure(
    vectorType.verified.type.fields.some((field) => field.name === "x") ||
      vectorType.verified.type.properties.some((property) => property.name === "x"),
    "reference.type did not expose Vector3 x member"
  );

  const consoleValue = await bridge.send<ConsoleResult>("reference.console", {
    name: "snd_mute"
  });
  ensure(consoleValue.verified.name === "snd_mute", "reference.console did not read requested convar name");
  ensure(typeof consoleValue.verified.value === "string", "reference.console did not return a string value");

  const whitelistSearch = await bridge.send<ReferenceSearchResult>("reference.search", {
    query: "WhitelistedSystemMembers",
    maxResults: 20
  });
  ensure(
    whitelistSearch.verified.results.some((entry) => entry.name.includes("WhitelistedSystemMembers")),
    "reference.search did not find API whitelist reference docs"
  );

  const whitelist = await bridge.send<WhitelistResult>("reference.whitelist", {
    maxResults: 10
  });
  ensure(typeof whitelist.verified.available === "boolean", "reference.whitelist did not return availability");

  console.log(
    JSON.stringify(
      {
        ok: true,
        compileWaitMs: compileWait.verified.elapsedMs,
        docs: {
          documentCount: networkSearch.verified.documentCount,
          networkMatch: networkSearch.verified.results.find((entry) => entry.name === "P:Sandbox.GameObject.NetworkMode") ?? null,
          whitelistDoc: whitelistSearch.verified.results[0] ?? null
        },
        gameObject: {
          propertyCount: gameObjectType.verified.type.properties.length,
          networkMode: gameObjectType.verified.type.properties.find((property) => property.name === "NetworkMode") ?? null
        },
        vector3: {
          fields: vectorType.verified.type.fields.map((field) => field.name).slice(0, 10),
          properties: vectorType.verified.type.properties.map((property) => property.name).slice(0, 10)
        },
        console: consoleValue.verified,
        whitelist: whitelist.verified
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
