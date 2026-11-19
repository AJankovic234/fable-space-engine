import test from "node:test";
import assert from "node:assert/strict";
import {
  Research_create as createResearch,
  Research_valid as validResearch,
  Research_installLab as installLab,
  Research_unlock as unlockTech,
  Research_has as hasTech,
  Research_unlockedCount as unlockedCount,
  Research_bonusHold as bonusHold,
  Research_bonusShield as bonusShield,
  Research_bonusHull as bonusHull,
  Research_escortFeeScale as escortFeeScale,
  Research_marginBonusPercent as marginBonusPercent,
  Research_canResearch as canResearch,
  Research_nodes as researchNodes,
} from "../build/Research.js";
import { Economy_create as createWallet, Economy_earn as earn } from "../build/Economy.js";
import { TradeSystem_createCargo as createCargo, TradeSystem_loadSalvage as loadSalvage, TradeSystem_quantityOf as quantityOf } from "../build/TradeSystem.js";
import { ShipCatalog_create as createFleet, ShipCatalog_equip as equip, RefitOrder } from "../build/ShipCatalog.js";
import {
  WorldModule_create as createWorld, WorldModule_reset as resetWorld,
  ShipModule_reset as resetShip, LaserPoolModule_spawn as spawnLaser,
  Physics_step as stepPhysics, Physics_clearEnemyBolts as clearEnemyBolts,
} from "../build/PhysicsAndPool.js";
import { Mission_applyLoadout as applyLoadout } from "../build/Mission.js";
import {
  Missiles_create as createMissiles, Missiles_applyResearch as applyMissileResearch,
  Missiles_tryLaunch as launchMissile,
} from "../build/Turret.js";
import { Vec3, Vec3Module_zero as zero, QuatModule_identity as identity } from "../build/Math3D.js";

test("RT-9 onboard lab installs exclusively at Relais Theta with exact credits and cargo", () => {
  const research = createResearch();
  const wallet = createWallet();
  const cargo = createCargo(32);
  earn(wallet, 2000, false);
  loadSalvage(cargo, 3, 3); // 3x NavData
  loadSalvage(cargo, 2, 2); // 2x PlasmaCores

  // Rejected at Kestrel and Kepler-07.
  assert.equal(installLab(research, "outer-belt", wallet, cargo).tag, 1);
  assert.equal(installLab(research, "kepler-yard", wallet, cargo).tag, 1);
  assert.equal(research.LabInstalled, false);

  // Accepted at Relais Theta, consuming 900 CR + 3 NAV + 2 PLS.
  assert.equal(installLab(research, "aether-relay", wallet, cargo).tag, 0);
  assert.equal(research.LabInstalled, true);
  assert.equal(wallet.Credits, 1100);
  assert.equal(quantityOf(cargo, 3), 0);
  assert.equal(quantityOf(cargo, 2), 0);
  assert.equal(validResearch(research), true);
});

test("research tree enforces prerequisites, exclusive hubs, and immediately grants exotic weapons & bonuses", () => {
  const research = createResearch();
  const wallet = createWallet();
  const cargo = createCargo(120);
  const fleet = createFleet();
  earn(wallet, 15000, false);
  // Stock enough materials for Lab + Weapon branch through PhaseRailgun + QuantumHold.
  loadSalvage(cargo, 0, 20); // TitaniumScrap
  loadSalvage(cargo, 1, 20); // CryoOre
  loadSalvage(cargo, 2, 10); // PlasmaCores (20t)
  loadSalvage(cargo, 3, 12); // NavData
  loadSalvage(cargo, 4, 15); // FuelCells

  // Cannot buy research-only weapon kit 10 or 11 in the shipyard before researching.
  assert.equal(equip(fleet, cargo, 5000, true, new RefitOrder(1, [10])).tag, 1);
  assert.equal(equip(fleet, cargo, 5000, true, new RefitOrder(1, [11])).tag, 1);

  assert.equal(installLab(research, "aether-relay", wallet, cargo).tag, 0);

  // Cannot skip A0 (HighEnergyOptics) to research A1 (CryoDisruptor).
  assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, 1).tag, 1);
  // Cannot research at Kestrel (not a research hub).
  assert.equal(unlockTech(research, "outer-belt", 0, wallet, cargo, fleet, 0).tag, 1);

  // Unlock A0 -> A1 -> A2 -> A3 at Kepler-07 & Relais Theta.
  assert.equal(unlockTech(research, "kepler-yard", 0, wallet, cargo, fleet, 0).tag, 0);
  assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, 1).tag, 0);
  assert.equal(unlockTech(research, "kepler-yard", 0, wallet, cargo, fleet, 2).tag, 0);
  // The Phase-Railgun breakthrough is built from Continuity drone rails: Kepler-07 only.
  assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, 3).tag, 1);
  assert.equal(unlockTech(research, "kepler-yard", 0, wallet, cargo, fleet, 3).tag, 0);
  assert.equal(unlockedCount(research), 4);
  assert.equal(hasTech(research, 3), true);

  // Exotic kits 10 & 11 are now owned and free to equip.
  assert.equal(equip(fleet, cargo, 0, true, new RefitOrder(1, [11])).tag, 0);
  const world = createWorld(1);
  resetWorld(world, 0);
  applyLoadout(world.Player, [0, 0, 0], world.Pilot, fleet);
  assert.equal(world.Player.BoltSpeedScale, 2.0);
  assert.equal(world.Player.PiercingShots, true);

  // Logistics branch: C0 -> C1 expands cargo capacity by +16t immediately.
  const beforeCap = cargo.Capacity;
  assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, 10).tag, 0);
  assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, 11).tag, 0);
  assert.equal(cargo.Capacity, beforeCap + 16);
  assert.equal(bonusHold(research), 16);
  assert.equal(marginBonusPercent(research), 5);
  assert.equal(escortFeeScale(research), 1.0);
  assert.equal(bonusShield(research), 0);
  assert.equal(bonusHull(research), 0);
  assert.equal(validResearch(research), true);
});

test("SwarmMissiles fires a 3-missile salvo and PhaseReflector clears nearby enemy bolts", () => {
  const world = createWorld(3);
  resetWorld(world, 0);
  for (let i = 0; i < 3; i++) {
    resetShip(world.Enemies[i], new Vec3((i - 1) * 30, 0, -160), identity);
  }
  const rack = createMissiles();
  applyMissileResearch(rack, true, true);
  assert.equal(launchMissile(world, rack, 0).tag, 0);
  assert.equal(rack.Ammo, 3);
  const alive = rack.Missiles.filter(m => m.Alive);
  assert.equal(alive.length, 3);

  // Spawn 2 nearby enemy bolts + 1 distant enemy bolt + 1 player bolt.
  spawnLaser(world.Lasers, 10, 0, 0, 0, 0, 100, 0, 0, 0, 1, 5, 1);
  spawnLaser(world.Lasers, -20, 0, 0, 0, 0, 100, 0, 0, 0, 1, 5, 1);
  spawnLaser(world.Lasers, 200, 0, 0, 0, 0, 100, 0, 0, 0, 1, 5, 1);
  spawnLaser(world.Lasers, 5, 0, 0, 0, 0, -100, 0, 0, 0, 1, 10, 0);
  assert.equal(clearEnemyBolts(world, zero, 45.0), 2);
  assert.equal(world.Lasers.Count, 2);
});

test("apex nodes need a neighbouring branch, the matching archive find and their home dock", () => {
  const research = createResearch();
  const wallet = createWallet();
  const cargo = createCargo(400);
  const fleet = createFleet();
  earn(wallet, 40000, false);
  for (let good = 0; good < 6; good++) loadSalvage(cargo, good, 40);
  assert.equal(installLab(research, "aether-relay", wallet, cargo).tag, 0);

  // Weapons ladder up to the Railgun, all at Kepler-07.
  for (const id of [0, 1, 2, 3]) assert.equal(unlockTech(research, "kepler-yard", 0, wallet, cargo, fleet, id).tag, 0);

  // Single-Seat-Schwarm (4) also needs Seras Horchantenne (12) from the logistics branch.
  const trockendock = 1 << 3;
  assert.equal(unlockTech(research, "kepler-yard", trockendock, wallet, cargo, fleet, 4).tag, 1);
  for (const id of [10, 11, 12]) assert.equal(unlockTech(research, "kepler-yard", 0, wallet, cargo, fleet, id).tag, 0);

  // Still sealed without the Trockendock B find; a different find does not open it.
  assert.equal(canResearch(research, "kepler-yard", 0, wallet, cargo, 4), false);
  assert.equal(unlockTech(research, "kepler-yard", 1 << 5, wallet, cargo, fleet, 4).tag, 1);
  // With the find it is still bound to Kepler-07.
  assert.equal(unlockTech(research, "aether-relay", trockendock, wallet, cargo, fleet, 4).tag, 1);
  assert.equal(unlockTech(research, "kepler-yard", trockendock, wallet, cargo, fleet, 4).tag, 0);
  assert.equal(hasTech(research, 4), true);

  // Spiegelkennung Graue Wacht (14): Kursprotokoll 17, Puffer-Siphon (6) and Kipper at Drift-9.
  const kursprotokoll = 1 << 8;
  assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, 13).tag, 0);
  assert.equal(unlockTech(research, "frontier-drift", kursprotokoll, wallet, cargo, fleet, 14).tag, 1);
  for (const id of [5, 6]) assert.equal(unlockTech(research, "aether-relay", 0, wallet, cargo, fleet, id).tag, 0);
  assert.equal(unlockTech(research, "aether-relay", kursprotokoll, wallet, cargo, fleet, 14).tag, 1);
  assert.equal(unlockTech(research, "frontier-drift", kursprotokoll, wallet, cargo, fleet, 14).tag, 0);
  assert.equal(escortFeeScale(research), 0.5);
  assert.equal(validResearch(research), true);

  // A save that holds an apex without its cross-branch prerequisite is rejected.
  assert.equal(validResearch({ LabInstalled: true, Unlocked: 0b11111 }), false);

  // Every node carries a source and a band fragment; every dock named exists as a sector id.
  for (const node of researchNodes) {
    assert.ok(node.Source.length > 0 && node.Log.length > 0, node.Name);
    assert.ok(node.Ports.length > 0 && node.Ports.every(p => ["aether-relay", "kepler-yard", "frontier-drift"].includes(p)), node.Name);
  }
});
