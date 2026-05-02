import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { BridgeClient } from "../src/bridge-client.js";
import { waitForCompile, waitForRuntime, waitForStopped } from "../src/wait-helpers.js";

interface CompileStatusResult {
  verified: {
    groups: Array<{ sequence: number }>;
  };
}

interface GameObjectResult {
  verified: {
    id: string;
  };
}

interface ComponentAddResult {
  verified: {
    creationMode: string;
    component: {
      id: string;
      type: string;
      fullType: string;
    };
  };
}

interface RuntimeListResult {
  verified: {
    count: number;
    components: Array<{
      component: {
        id: string;
        type: string;
      };
      actions: string[];
      propertyProtocol: {
        canRun: boolean;
      };
    }>;
  };
}

interface RuntimeRunResult {
  verified: {
    invocationMode: string;
    result: any;
  };
}

interface CaptureResult {
  verified: {
    path: string;
    byteCount: number;
    luminance: {
      average: number;
      max: number;
      darkPixelRatio: number;
    };
  };
}

const bridge = new BridgeClient({
  root: process.env.SBOX_AGENT_BRIDGE_IPC,
  timeoutMs: Number(process.env.SBOX_AGENT_BRIDGE_TIMEOUT_MS ?? 30_000)
});

const scenePath = process.env.SBOX_AGENT_BRIDGE_ARPG_SCENE ?? "scenes/agent_bridge/walkthrough/arpg_cleanroom.scene";
const scriptPath = "ArpgCleanroom/ArpgCleanroomController.cs";
const fixturePath = fileURLToPath(new URL("./fixtures/ArpgCleanroomController.cs", import.meta.url));
const controllerName = process.env.SBOX_AGENT_BRIDGE_ARPG_CONTROLLER_NAME ?? "Agent Bridge ARPG Cleanroom Controller";
let runtimeComponentId = "";

const weakSpots = [
  "Runtime verification uses deterministic AgentBridge test actions because generic OS/mouse input synthesis is still a verified bridge gap.",
  "The ARPG controller self-reports UI state because arbitrary ScreenPanel hierarchy and HUD pixel inspection are still verified gaps.",
  "The fixture uses simple box-based runtime geometry and animation state names so the bridge can verify gameplay systems without relying on external art assets.",
  "Script authoring is still full-file replacement; this walkthrough intentionally keeps the generated controller isolated under Code/ArpgCleanroom."
];

try {
  const content = await readFile(fixturePath, "utf8");

  await bridge.send("bridge.doctor", {
    mcpServerVersion: "0.1.0",
    maxLines: 20,
    maxDiagnostics: 20
  });

  await bridge.send("editor.stop", { stopAll: true });
  const stoppedBefore = await waitForStopped(bridge, { timeoutMs: 10_000, requireNoGameSessions: true });
  ensure(stoppedBefore.verified.satisfied, "editor.wait_stopped did not settle before ARPG walkthrough");

  const beforeCompile = await bridge.send<CompileStatusResult>("editor.compile_status", { maxDiagnostics: 5 });
  const beforeSequence = latestSequence(beforeCompile);

  await bridge.send("script.create", {
    path: scriptPath,
    content,
    overwrite: true
  });

  const compileWait = await waitForCompile(bridge, {
    timeoutMs: 60_000,
    maxDiagnostics: 40,
    sinceSequence: beforeSequence
  });
  ensure(compileWait.verified.satisfied, "editor.wait_compile did not observe a post-script compile");
  ensure(compileWait.verified.errorCount === 0, "ARPG cleanroom controller compile reported errors");

  await bridge.send("editor.new_scene", {
    name: "Agent Bridge ARPG Cleanroom",
    path: scenePath,
    overwrite: true,
    discardUnsaved: true,
    bringToFront: true,
    activateAfterSave: true
  });

  const controllerObject = await bridge.send<GameObjectResult>("gameobject.create", {
    name: controllerName,
    position: { x: 0, y: 0, z: 0 }
  });

  const controller = await bridge.send<ComponentAddResult>("component.add", {
    gameObjectId: controllerObject.verified.id,
    type: "ArpgCleanroomController",
    startEnabled: true
  });
  ensure(controller.verified.component.type === "ArpgCleanroomController", "component.add did not create ArpgCleanroomController");
  runtimeComponentId = controller.verified.component.id;

  await bridge.send("component.set_property", {
    id: controller.verified.component.id,
    property: "RunInEditorForBridge",
    value: true
  });

  await bridge.send("editor.save_scene");

  await bridge.send("editor.play");
  const runtimeWait = await waitForRuntime(bridge, { timeoutMs: 20_000, minObjects: 1 });
  ensure(runtimeWait.verified.satisfied, "editor.wait_runtime did not resolve an ARPG GameSession");

  const actions = await bridge.send<RuntimeListResult>("runtime.list_test_actions", {
    componentType: "ArpgCleanroomController"
  });
  const listedComponent = actions.verified.components.find((entry) => entry.component.id === runtimeComponentId) ?? actions.verified.components[0];
  ensure(listedComponent !== undefined, "expected at least one ArpgCleanroomController runtime test component");
  ensure(listedComponent.propertyProtocol.canRun, "ArpgCleanroomController does not expose the property runtime protocol");
  for (const required of [
    "arpg.state",
    "arpg.create_character",
    "arpg.use_skill",
    "arpg.kill_zombie",
    "arpg.open_chest",
    "arpg.talk_vendor",
    "arpg.equip_item"
  ]) {
    ensure(listedComponent.actions.includes(required), `ARPG runtime actions did not include ${required}`);
  }

  const creation = await runArpg("arpg.state");
  ensure(creation.verified.result.phase === "CharacterCreation", "ARPG did not start on the character creation screen");
  ensure(creation.verified.result.characterCreation.availableClasses.includes("Warrior"), "character creation did not include Warrior");
  ensure(creation.verified.result.characterCreation.availableClasses.includes("Mage"), "character creation did not include Mage");
  ensure(creation.verified.result.characterCreation.availableGenders.includes("Male"), "character creation did not include Male");
  ensure(creation.verified.result.characterCreation.availableGenders.includes("Female"), "character creation did not include Female");

  const warrior = await runArpg("arpg.create_character", {
    class: "Warrior",
    gender: "Male",
    name: "Doran"
  });
  ensure(warrior.verified.result.phase === "InGame", "create_character did not enter the starting zone");
  ensure(warrior.verified.result.player.className === "Warrior", "created warrior did not report className=Warrior");
  ensure(warrior.verified.result.player.gender === "Male", "created warrior did not report gender=Male");
  ensure(warrior.verified.result.player.name === "Doran", "created warrior did not preserve custom name");
  ensure(warrior.verified.result.ui.healthOrb.percent === 1, "health orb did not start full");
  ensure(warrior.verified.result.ui.manaOrb.percent === 1, "mana orb did not start full");
  ensure(warrior.verified.result.skills.length === 4, "warrior did not expose four skills");
  ensure(warrior.verified.result.skills.some((skill: any) => skill.name === "Whirlwind"), "warrior skills did not include Whirlwind");
  ensure(warrior.verified.result.skills.some((skill: any) => skill.name === "Charge"), "warrior skills did not include Charge");
  ensure(warrior.verified.result.ui.hotkeyBar.some((slot: any) => slot.key === "1"), "hotkey bar did not bind skill 1");
  ensure(warrior.verified.result.ui.hotkeyBar.some((slot: any) => slot.key === "2"), "hotkey bar did not bind skill 2");
  ensure(warrior.verified.result.combat.collision.defaultPlayerZombie, "default player/zombie collision was not enabled");
  ensure(warrior.verified.result.combat.collision.defaultPlayerNeutralNpc, "default player/NPC collision was not enabled");

  const damaged = await runArpg("arpg.damage_player", { amount: 25 });
  ensure(damaged.verified.result.player.health < warrior.verified.result.player.health, "damage_player did not lower health");
  ensure(damaged.verified.result.ui.healthOrb.percent < warrior.verified.result.ui.healthOrb.percent, "health orb did not change after damage");

  const restored = await runArpg("arpg.restore_player");
  ensure(restored.verified.result.player.health === restored.verified.result.player.maxHealth, "restore_player did not refill health");

  const manaSpent = await runArpg("arpg.spend_mana", { amount: 20 });
  ensure(manaSpent.verified.result.player.mana < restored.verified.result.player.mana, "spend_mana did not lower mana");
  ensure(manaSpent.verified.result.ui.manaOrb.percent < restored.verified.result.ui.manaOrb.percent, "mana orb did not change after mana spend");
  await runArpg("arpg.restore_mana");

  const stationaryAttack = await runArpg("arpg.use_skill", { skill: "left_click", shift: true });
  ensure(stationaryAttack.verified.result.player.stationaryAttack, "shift-held left click did not report stationary attack");
  ensure(stationaryAttack.verified.result.player.animation === "Warrior Sword Slash", "warrior left click did not use its unique animation");

  const altAttack = await runArpg("arpg.use_skill", { skill: "right_click", shift: false });
  ensure(altAttack.verified.result.player.animation === "Warrior Heavy Cleave", "warrior right click did not use its unique animation");

  const whirlwind = await runArpg("arpg.use_skill", { skill: "1" });
  ensure(whirlwind.verified.result.player.buffs.some((buff: any) => buff.name === "Whirlwind"), "Whirlwind did not apply a buff");
  ensure(whirlwind.verified.result.combat.collision.whirlwindDisablesEnemyCollision, "Whirlwind did not disable enemy collision");
  ensure(whirlwind.verified.result.player.mana < restored.verified.result.player.maxMana, "Whirlwind did not spend mana");

  const charge = await runArpg("arpg.use_skill", { skill: "2" });
  ensure(charge.verified.result.player.animation === "Warrior Shoulder Charge", "Charge did not use its unique animation");
  ensure(
    charge.verified.result.combat.mobs.some((mob: any) => mob.stunned),
    "Charge did not stun any mob"
  );

  const aggroFar = await runArpg("arpg.aggro_probe", { near: false });
  ensure(aggroFar.verified.result.combat.mobs.some((mob: any) => !mob.elite && !mob.aggroed), "zombie aggroed outside aggro radius");
  const aggroNear = await runArpg("arpg.aggro_probe", { near: true });
  ensure(aggroNear.verified.result.combat.mobs.some((mob: any) => !mob.elite && mob.aggroed), "zombie did not aggro inside aggro radius");
  ensure(
    aggroNear.verified.result.combat.mobs.filter((mob: any) => !mob.elite).every((mob: any) => mob.moveSpeed < aggroNear.verified.result.combat.playerSpeed),
    "zombies were not significantly slower than the player"
  );

  const inventoryOpen = await runArpg("arpg.toggle_inventory_hotkey");
  ensure(inventoryOpen.verified.result.inventory.open, "I hotkey inventory toggle did not open inventory");

  const zombieLoot = await runArpg("arpg.kill_zombie", { forceHealthOrb: true });
  ensure(zombieLoot.verified.result.inventory.itemCount > inventoryOpen.verified.result.inventory.itemCount, "zombie kill did not add an item");
  ensure(zombieLoot.verified.result.loot.coins > inventoryOpen.verified.result.loot.coins, "zombie kill did not add coins");
  ensure(zombieLoot.verified.result.loot.healthOrbs.some((orb: any) => !orb.pickedUp), "zombie kill did not drop a health orb");
  const lootItem = zombieLoot.verified.result.inventory.items[zombieLoot.verified.result.inventory.items.length - 1];

  const tooltip = await runArpg("arpg.hover_item", { id: lootItem.id });
  ensure(String(tooltip.verified.result.inventory.tooltip).includes(lootItem.name), "item mouseover did not report tooltip statistics");

  const dragged = await runArpg("arpg.drag_item", { id: lootItem.id, x: 6, y: 0 });
  const draggedItem = dragged.verified.result.inventory.items.find((item: any) => item.id === lootItem.id);
  ensure(draggedItem.x === 6 && draggedItem.y === 0, "drag_item did not move the item in the grid inventory");

  const equipped = await runArpg("arpg.equip_item", { id: lootItem.id });
  ensure(equipped.verified.result.inventory.equipped.some((item: any) => item.id === lootItem.id), "equip_item did not equip the item");

  const chestOne = await runArpg("arpg.open_chest");
  const chestTwo = await runArpg("arpg.open_chest");
  ensure(chestTwo.verified.result.chest.openCount >= chestOne.verified.result.chest.openCount + 1, "repeatable chest did not open repeatedly");
  ensure(chestTwo.verified.result.loot.coins > chestOne.verified.result.loot.coins, "repeatable chest did not grant coins on the second open");
  ensure(chestTwo.verified.result.chest.animation.includes("coin"), "chest did not report a coin burst animation");

  const vendor = await runArpg("arpg.talk_vendor");
  ensure(vendor.verified.result.neutralNpc.dialogueOpen, "talk_vendor did not open dialogue");
  ensure(vendor.verified.result.neutralNpc.vendorOpen, "talk_vendor did not open vendor UI");

  const bought = await runArpg("arpg.buy_item", { id: "vendor-wand" });
  ensure(
    bought.verified.result.inventory.items.some((item: any) => String(item.id).startsWith("bought-")),
    "buy_item did not add a vendor item to inventory"
  );
  const sellCandidate = bought.verified.result.inventory.items.find((item: any) => String(item.id).startsWith("bought-"));
  const sold = await runArpg("arpg.sell_item", { id: sellCandidate.id });
  ensure(
    !sold.verified.result.inventory.items.some((item: any) => item.id === sellCandidate.id),
    "sell_item did not remove the sold item"
  );

  const elite = await runArpg("arpg.kill_elite");
  ensure(elite.verified.result.combat.aliveEliteCount === 0, "kill_elite did not defeat the rare elite");
  ensure(
    elite.verified.result.inventory.items.some((item: any) => String(item.name).includes("Rare")),
    "rare elite did not drop a rare item"
  );

  const afterOrbDamage = await runArpg("arpg.damage_player", { amount: 35 });
  const pickup = await runArpg("arpg.pickup_health_orb");
  ensure(pickup.verified.result.player.health > afterOrbDamage.verified.result.player.health, "health orb pickup did not restore health");

  const mageCreation = await runArpg("arpg.reset_character_creation");
  ensure(mageCreation.verified.result.phase === "CharacterCreation", "reset_character_creation did not return to character creation");
  const mage = await runArpg("arpg.create_character", {
    class: "Mage",
    gender: "Female",
    name: "Lyra"
  });
  ensure(mage.verified.result.player.className === "Mage", "created mage did not report className=Mage");
  ensure(mage.verified.result.player.gender === "Female", "created mage did not report gender=Female");
  ensure(mage.verified.result.player.name === "Lyra", "created mage did not preserve custom name");
  ensure(mage.verified.result.skills.length === 4, "mage did not expose four skills");
  ensure(mage.verified.result.skills.some((skill: any) => skill.name === "Frostbolt"), "mage skills did not include Frostbolt");
  ensure(mage.verified.result.skills.some((skill: any) => skill.name === "Fireblast"), "mage skills did not include Fireblast");

  const mageMelee = await runArpg("arpg.use_skill", { skill: "left_click", shift: true });
  ensure(mageMelee.verified.result.player.animation === "Mage Staff Strike", "mage left click did not use melee animation");

  const projectile = await runArpg("arpg.use_skill", { skill: "right_click" });
  ensure(projectile.verified.result.player.animation === "Mage Arcane Projectile", "mage right click did not use projectile animation");

  const frostCast = await runArpg("arpg.use_skill", { skill: "1" });
  ensure(frostCast.verified.result.player.casting, "Frostbolt did not start a cast");
  ensure(frostCast.verified.result.player.stationaryAttack, "Frostbolt did not force stationary casting");
  const frostImpact = await runArpg("arpg.advance", { seconds: 1.1 });
  ensure(!frostImpact.verified.result.player.casting, "Frostbolt did not finish after the cast time");
  ensure(
    frostImpact.verified.result.combat.mobs.some((mob: any) => mob.slowed),
    "Frostbolt did not slow any target"
  );

  const fireblast = await runArpg("arpg.use_skill", { skill: "2" });
  ensure(fireblast.verified.result.player.animation === "Mage Fireblast Instant", "Fireblast did not use its unique animation");
  ensure(fireblast.verified.result.player.fireblastCooldown > 2.5, "Fireblast did not set a 3 second cooldown");
  const fireblastAgain = await runArpg("arpg.use_skill", { skill: "2" });
  ensure(
    String(fireblastAgain.verified.result.lastEvent).includes("cooling down"),
    "Fireblast could be recast immediately despite cooldown"
  );
  ensure(fireblastAgain.verified.result.camera?.gameObjectId, "ARPG runtime state did not expose its generated camera id");

  const capture = await bridge.send<CaptureResult>("visual.capture_camera", {
    targetSession: "runtime",
    gameObjectId: fireblastAgain.verified.result.camera.gameObjectId,
    width: 640,
    height: 360,
    name: "arpg-cleanroom"
  });
  ensure(capture.verified.byteCount > 1000, "ARPG camera capture produced an unexpectedly small PNG");
  ensure(capture.verified.luminance.average > 0.02, "ARPG camera capture was near black");
  ensure(capture.verified.luminance.max > 0.1, "ARPG camera capture had no bright pixels");

  await bridge.send("editor.stop", { stopAll: true });
  const stoppedAfter = await waitForStopped(bridge, { timeoutMs: 10_000, requireNoGameSessions: true });
  ensure(stoppedAfter.verified.satisfied, "editor.wait_stopped did not settle after ARPG walkthrough");

  const finalDoctor = await bridge.send<{ verified: { overall: string } }>("bridge.doctor", {
    mcpServerVersion: "0.1.0",
    maxLines: 20,
    maxDiagnostics: 20
  });
  ensure(finalDoctor.verified.overall !== "fail", "final bridge.doctor failed");

  console.log(
    JSON.stringify(
      {
        ok: true,
        scenePath,
        scriptPath,
        controllerName,
        componentCreationMode: controller.verified.creationMode,
        compileWaitMs: compileWait.verified.elapsedMs,
        runtimeWaitMs: runtimeWait.verified.elapsedMs,
        runtimeActionComponentCount: actions.verified.count,
        runtimeActions: listedComponent.actions,
        warrior: {
          name: warrior.verified.result.player.name,
          className: warrior.verified.result.player.className,
          skillCount: warrior.verified.result.skills.length,
          whirlwindCollisionDisabled: whirlwind.verified.result.combat.collision.whirlwindDisablesEnemyCollision,
          chargeStunnedMob: charge.verified.result.combat.mobs.some((mob: any) => mob.stunned)
        },
        inventory: {
          open: inventoryOpen.verified.result.inventory.open,
          itemCountAfterLoot: zombieLoot.verified.result.inventory.itemCount,
          equipped: equipped.verified.result.inventory.equipped
        },
        chest: chestTwo.verified.result.chest,
        vendor: {
          dialogueOpen: vendor.verified.result.neutralNpc.dialogueOpen,
          vendorOpen: vendor.verified.result.neutralNpc.vendorOpen
        },
        mage: {
          name: mage.verified.result.player.name,
          className: mage.verified.result.player.className,
          skillCount: mage.verified.result.skills.length,
          frostboltSlowed: frostImpact.verified.result.combat.mobs.some((mob: any) => mob.slowed),
          fireblastCooldown: fireblast.verified.result.player.fireblastCooldown
        },
        capture: capture.verified,
        finalDoctor: finalDoctor.verified.overall,
        weakSpots
      },
      null,
      2
    )
  );
} catch (error) {
  try {
    await bridge.send("editor.stop", { stopAll: true });
    await waitForStopped(bridge, { timeoutMs: 5_000, requireNoGameSessions: true });
  } catch {
    // Best-effort cleanup for a live editor walkthrough.
  }

  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}

async function runArpg(testAction: string, payload: Record<string, unknown> = {}): Promise<RuntimeRunResult> {
  const result = await bridge.send<RuntimeRunResult>("runtime.run_test_action", {
    componentId: runtimeComponentId,
    testAction,
    payload
  });
  ensure(result.verified.invocationMode === "propertyProtocol", `${testAction} did not use the propertyProtocol runtime path`);
  ensure(result.verified.result.bridgeVerified === true, `${testAction} did not return a verified ARPG state`);
  return result;
}

function latestSequence(status: CompileStatusResult): number | undefined {
  const sequences = status.verified.groups.map((group) => group.sequence ?? 0);
  return sequences.length > 0 ? Math.max(...sequences) : undefined;
}

function ensure(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message);
  }
}
