import test from "node:test";
import assert from "node:assert/strict";
import {
  Economy_create as createWallet, Economy_reset as resetWallet, Economy_earn as earn,
  Economy_available as available, Economy_price as price, Economy_buy as buy,
  Economy_priceFor as priceFor, Economy_buyFor as buyFor,
  Economy_repairCostFor as repairCostFor, Economy_repairWithCredits as repairWithCredits,
  Economy_repairWithScrap as repairWithScrap,
  Economy_hasAddon as hasAddon, Economy_addonPriceFor as addonPriceFor, Economy_buyAddonFor as buyAddonFor,
} from "../build/Economy.js";
import { TradeSystem_createCargo as createCargo, TradeSystem_loadSalvage as loadSalvage, TradeSystem_quantityOf as quantityOf } from "../build/TradeSystem.js";
import {
  Turret_create as createTurret, Turret_reset as resetTurret, Turret_update as updateTurret,
  Missiles_create as createMissiles, Missiles_tryLaunch as launchMissile,
  Missiles_update as updateMissiles, Missiles_reload as reloadMissiles,
  Missiles_configure as configureMissiles,
} from "../build/Turret.js";
import {
  WorldModule_create as createWorld, WorldModule_reset as resetWorld,
  ShipModule_createPlayer as createPlayer, ShipModule_reset as resetShip,
  AsteroidFieldModule_add as addAsteroid, Physics_step as stepPhysics,
} from "../build/PhysicsAndPool.js";
import { Vec3, Vec3Module_zero as zero, QuatModule_identity as identity } from "../build/Math3D.js";
import { ShipHardpoints_turretGunLeft as leftMuzzle, ShipHardpoints_turretGunRight as rightMuzzle } from "../build/ProceduralMeshes.js";

function shop() {
  return { wallet: createWallet(), ship: createPlayer(), levels: new Int32Array(3) };
}

function encounter(distance = 100) {
  const world = createWorld(1);
  resetWorld(world, 0);
  world.Player.Velocity = zero;
  world.Player.Controls.Throttle = 0;
  world.Player.Controls.Brake = true;
  const enemy = world.Enemies[0];
  // Align with the mount so the cooldown test does not depend on aiming convergence.
  resetShip(enemy, new Vec3(0, 0.85, -distance), identity);
  enemy.Velocity = zero;
  enemy.Controls.Throttle = 0;
  enemy.Controls.Brake = true;
  return { world, enemy, turret: createTurret() };
}

test("unaffordable and invalid purchases cannot mutate credits, ownership or module levels", () => {
  const { wallet, ship, levels } = shop();
  for (const item of [-1, 0, 1, 2, 3, 4]) assert.equal(buy(wallet, ship, levels, item), false);
  assert.equal(wallet.Credits, 0);
  assert.equal(wallet.TurretOwned, false);
  assert.deepEqual(Array.from(levels), [0, 0, 0]);
  earn(wallet, 119, false);
  assert.equal(buy(wallet, ship, levels, 1), false);
  assert.equal(wallet.Credits, 119);
  earn(wallet, 1, false);
  assert.equal(buy(wallet, ship, levels, 1), true);
  assert.equal(wallet.Credits, 0);
  assert.equal(levels[0], 1);
  assert.equal(wallet.Earned, 120);
  assert.equal(price(levels, 1), 200);
});

test("fixer discount charges the displayed module price and leaves the turret at its usual purchase price", () => {
  const { wallet, ship, levels } = shop();
  const terms = { SignalLevel: 0, FixerLevel: 1 };
  for (const [item, cost] of [[0,350], [1,90], [2,105], [3,105], [1,150]]) {
    wallet.Credits = cost - 1;
    assert.equal(priceFor(terms, levels, item), cost);
    assert.equal(buyFor(terms, wallet, ship, levels, item), false);
    wallet.Credits += 1;
    assert.equal(buyFor(terms, wallet, ship, levels, item), true);
    assert.equal(wallet.Credits, 0);
  }
});

test("the turret has one exact purchase charge and cannot be bought a second time", () => {
  const { wallet, ship, levels } = shop();
  assert.equal(price(levels, 0), 350);
  earn(wallet, 1000, false);
  assert.equal(buy(wallet, ship, levels, 0), true);
  assert.equal(wallet.Credits, 650);
  assert.equal(wallet.TurretOwned, true);
  assert.equal(available(wallet, levels, 0), false);
  assert.equal(buy(wallet, ship, levels, 0), false);
  assert.equal(wallet.Credits, 650);
  assert.deepEqual(Array.from(levels), [0, 0, 0]);
});

test("modules charge the advertised escalating prices and stop at level three", () => {
  const { wallet, ship, levels } = shop();
  earn(wallet, 3000, false);
  let expectedBalance = 3000;
  for (let item = 1; item <= 3; item++) {
    for (let level = 0; level < 3; level++) {
      const expectedPrice = (item === 1 ? 120 : 140) + level * 80;
      assert.equal(price(levels, item), expectedPrice);
      assert.equal(buy(wallet, ship, levels, item), true);
      expectedBalance -= expectedPrice;
      assert.equal(wallet.Credits, expectedBalance);
      assert.equal(levels[item - 1], level + 1);
    }
    assert.equal(available(wallet, levels, item), false);
    assert.equal(buy(wallet, ship, levels, item), false);
    assert.equal(wallet.Credits, expectedBalance);
  }
  assert.deepEqual(Array.from(levels), [3, 3, 3]);
  assert.equal(ship.MaxShield, 190);
  assert.equal(ship.MaxHull, 175);
  assert.ok(Math.abs(ship.LaserDamage - 19.2) < 1e-9);
});

test("earnings distinguish salvage, reject negative grants and reset with ownership", () => {
  const { wallet, ship, levels } = shop();
  earn(wallet, 500, false);
  earn(wallet, 28, true);
  earn(wallet, -50, true);
  earn(wallet, 0, false);
  assert.deepEqual([wallet.Credits, wallet.Earned, wallet.SalvageEarned], [528, 528, 28]);
  buy(wallet, ship, levels, 0);
  resetWallet(wallet);
  assert.deepEqual([wallet.Credits, wallet.Earned, wallet.SalvageEarned, wallet.TurretOwned], [0, 0, 0, false]);
});

test("dock hull repair supports discounted credits and salvaged titanium scrap", () => {
  const { wallet, ship } = shop();
  const cargo = createCargo(16);
  const standard = { SignalLevel: 0, FixerLevel: 0 };
  const fixer = { SignalLevel: 0, FixerLevel: 1 };
  ship.Hull = 40;
  ship.Shield = 10;
  assert.equal(repairCostFor(standard, ship), 120);
  assert.equal(repairCostFor(fixer, ship), 90);
  // Salvaged TitaniumScrap patches +35 hull for free and recharges shield.
  assert.equal(repairWithScrap(cargo, ship), false);
  loadSalvage(cargo, 0, 1);
  assert.equal(repairWithScrap(cargo, ship), true);
  assert.equal(quantityOf(cargo, 0), 0);
  assert.equal(ship.Hull, 75);
  assert.equal(ship.Shield, ship.MaxShield);
  // Remaining 25 hull costs 50 CR standard (37 CR with Fixer).
  assert.equal(repairCostFor(fixer, ship), 37);
  earn(wallet, 36, false);
  assert.equal(repairWithCredits(fixer, wallet, ship), false);
  earn(wallet, 1, false);
  assert.equal(repairWithCredits(fixer, wallet, ship), true);
  assert.equal(wallet.Credits, 0);
  assert.equal(ship.Hull, ship.MaxHull);
  assert.equal(repairCostFor(standard, ship), 0);
  assert.equal(repairWithCredits(standard, wallet, ship), false);
});

test("homing micro-missiles require a locked target in range, track, damage and recharge", () => {
  const { world, enemy } = encounter(220);
  enemy.MaxHull = 80;
  enemy.Hull = 80;
  enemy.Shield = 0;
  const rack = createMissiles();
  // Rejects invalid target or out-of-range target.
  assert.equal(launchMissile(world, rack, -1).tag, 1);
  enemy.Position = new Vec3(0, 0, -500);
  assert.equal(launchMissile(world, rack, 0).tag, 1);
  // Launches cleanly inside 440 m and hits an off-axis enemy via homing turn.
  enemy.Position = new Vec3(24, 6, -180);
  const fired = launchMissile(world, rack, 0);
  assert.equal(fired.tag, 0);
  assert.equal(rack.Ammo, 3);
  let enemyHits = 0;
  for (let i = 0; i < 120 && enemyHits === 0; i++) {
    const impacts = updateMissiles(world, rack, 0.02);
    for (const impact of impacts) if (impact.HitEnemy) enemyHits++;
  }
  assert.equal(enemyHits, 1);
  assert.equal(enemy.Hull, 42);
  // Recharges 1 missile after 16 seconds.
  updateMissiles(world, rack, 16.0);
  assert.equal(rack.Ammo, 4);
  // Reload resets to full immediately.
  rack.Ammo = 1;
  reloadMissiles(rack);
  assert.equal(rack.Ammo, 4);
});

test("stackable ship addons apply fixer discounts, block duplicates and stack with MissilePod", () => {
  const { wallet } = shop();
  const standard = { SignalLevel: 0, FixerLevel: 0 };
  const fixer = { SignalLevel: 0, FixerLevel: 1 };
  assert.equal(addonPriceFor(standard, 0), 210);
  assert.equal(addonPriceFor(fixer, 0), 157);
  earn(wallet, 200, false);
  assert.equal(buyAddonFor(standard, wallet, 0), false);
  assert.equal(buyAddonFor(fixer, wallet, 0), true);
  assert.equal(hasAddon(wallet, 0), true);
  assert.equal(wallet.Credits, 43);
  assert.equal(buyAddonFor(fixer, wallet, 0), false);
  const rack = createMissiles();
  configureMissiles(rack, true, hasAddon(wallet, 0));
  assert.equal(rack.Capacity, 8);
  assert.equal(rack.Damage, 52);
  assert.ok(Math.abs(rack.ReloadInterval - 9.0 * 0.65) < 1e-9);
});

test("salvaged missiles back up an empty magazine and survive a field reload", () => {
  const { world } = encounter(180);
  const rack = createMissiles();
  rack.Reserve = 2;
  assert.equal(launchMissile(world, rack, 0).tag, 0);
  assert.equal(rack.Reserve, 2);
  reloadMissiles(rack);
  rack.Ammo = 0;
  assert.equal(launchMissile(world, rack, -1).tag, 1);
  assert.equal(rack.Reserve, 2);
  assert.equal(launchMissile(world, rack, 0).tag, 0);
  assert.equal(rack.Ammo, 0);
  assert.equal(rack.Reserve, 1);
  reloadMissiles(rack);
  assert.equal(rack.Reserve, 1);
});

test("turrets cannot fire before purchase, for dead players, at dead targets or outside 280 units", () => {
  for (const scenario of ["unowned", "dead-player", "dead-enemy", "out-of-range"]) {
    const { world, enemy, turret } = encounter(scenario === "out-of-range" ? 281 : 100);
    if (scenario === "dead-player") world.Player.Alive = false;
    if (scenario === "dead-enemy") enemy.Alive = false;
    updateTurret(world, turret, scenario !== "unowned", 1);
    assert.equal(world.Lasers.Count, 0, scenario);
    assert.equal(turret.Target, -1, scenario);
    assert.equal(turret.Flash, 0, scenario);
  }
});

test("an owned turret fires real player bolts, observes cooldown and damages its live target", () => {
  const { world, enemy, turret } = encounter();
  updateTurret(world, turret, true, 0.05);
  assert.equal(turret.Target, 0);
  assert.equal(world.Lasers.Count, 1);
  assert.equal(world.Lasers.Team[0], 0);
  assert.ok(world.Lasers.Data[5] < -500, "bolt flies towards the target");
  assert.equal(world.Lasers.Data[11], 10);
  assert.equal(turret.Flash, 1);
  assert.equal(turret.LastBarrel, 0);
  assert.ok(Math.abs(world.Lasers.Data[0] - leftMuzzle.X) < 1e-6, "first shot exits the left barrel");
  assert.ok(Math.abs(world.Lasers.Data[1] - (0.85 + leftMuzzle.Y)) < 1e-6, "muzzle height matches the model");
  assert.ok(Math.abs(world.Lasers.Data[2] - (0.65 + leftMuzzle.Z)) < 1e-6, "muzzle depth matches the model");
  assert.ok(turret.Cooldown > 0);
  updateTurret(world, turret, true, 0.1);
  assert.equal(world.Lasers.Count, 1);
  updateTurret(world, turret, true, 0.27);
  assert.equal(world.Lasers.Count, 2);
  assert.equal(turret.LastBarrel, 1);
  assert.ok(Math.abs(world.Lasers.Data[12] - rightMuzzle.X) < 1e-6, "next shot exits the right barrel");
  const health = enemy.Hull + enemy.Shield;
  stepPhysics(world, 0.2);
  assert.equal(world.Lasers.Count, 0);
  assert.equal(enemy.Hull + enemy.Shield, health - 20);
  assert.equal(world.Events.filter(event => event.cases()[event.tag] === "TurretFired").length, 2);
});

test("asteroids block turret sight and removing the obstruction allows firing", () => {
  const { world, turret } = encounter();
  addAsteroid(world.Asteroids, 0, 0.85, -50, 0, 0, 0, 8, 0, 1, 0, 0, 0, 0.5, 0.5, 0.5, 0);
  updateTurret(world, turret, true, 1);
  assert.equal(world.Lasers.Count, 0);
  assert.equal(turret.Target, -1);
  world.Asteroids.Count = 0;
  updateTurret(world, turret, true, 1);
  assert.equal(world.Lasers.Count, 1);
  assert.equal(turret.Target, 0);
});

test("turret reset clears target, flash, cooldown and aim", () => {
  const { world, turret } = encounter();
  updateTurret(world, turret, true, 0.05);
  turret.Rotation = { X: 0, Y: 1, Z: 0, W: 0 };
  resetTurret(turret);
  assert.equal(turret.Target, -1);
  assert.equal(turret.Cooldown, 0);
  assert.equal(turret.LastBarrel, -1);
  assert.equal(turret.Flash, 0);
  assert.deepEqual([turret.Rotation.X, turret.Rotation.Y, turret.Rotation.Z, turret.Rotation.W], [0, 0, 0, 1]);
});

test("shadow council turret fires 40 percent faster and hits shield and hull on the same bolt", () => {
  const {world, enemy, turret} = encounter();
  world.Pilot.Levels[1] = 3;
  enemy.Shield = enemy.MaxShield;
  const {Hull, Shield} = enemy;
  updateTurret(world, turret, true, 0.05);
  assert.ok(Math.abs(turret.Cooldown - 0.36 / 1.4) < 1e-9);
  stepPhysics(world, 0.21);
  assert.equal(enemy.Hull, Hull - 2.5);
  assert.equal(enemy.Shield, Shield - 7.5);
});
