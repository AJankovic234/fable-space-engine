import test from "node:test";
import assert from "node:assert/strict";
import {
  AsteroidFieldModule_create as createField,
  AsteroidFieldModule_add as addAsteroid,
  AsteroidFieldModule_remove as removeAsteroid,
  AsteroidFieldModule_generate as generateField,
  ShipModule_createPlayer as createPlayer,
  ShipModule_reset as resetShip,
  WorldModule_create as createWorld,
  WorldModule_reset as resetWorld,
  LaserPoolModule_spawn as spawnLaser,
  Damage_asteroid as damageAsteroid,
  Physics_step as stepPhysics,
} from "../build/PhysicsAndPool.js";
import { Salvage_create as createSalvage, Salvage_spawn as spawn, Salvage_update as update, Salvage_collect as collect } from "../build/Salvage.js";
import {
  TradeSystem_createCargo as createCargo, TradeSystem_resetCargo as resetCargo,
  TradeSystem_loadSalvage as loadCargo, TradeSystem_quantityOf as quantityOf,
  TradeSystem_usedVolume as usedVolume,
} from "../build/TradeSystem.js";
import { Vec3, Vec3Module_zero as zero, QuatModule_identity as identity } from "../build/Math3D.js";

const close = (actual, expected, label) =>
  assert.ok(Math.abs(actual - expected) < 1e-7, `${label}: ${actual} != ${expected}`);
const releases = (world) => world.Events.filter(event => event.cases()[event.tag] === "MineralReleased");
const distance = (a, b) => Math.hypot(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

test("demodulator pulls a distant data capsule into cargo as Nav-Daten", () => {
  const ship = createPlayer(), cargo = createCargo(16), pool = createSalvage();
  resetShip(ship, zero, identity);
  spawn(pool, new Vec3(70,0,0), 3, 1);
  pool[0].Velocity = zero;
  collect(pool,ship,cargo,55,0.1);
  assert.equal(pool[0].Velocity.X,0);
  collect(pool,ship,cargo,85,0.1);
  assert.ok(pool[0].Velocity.X<0);
  for(let i=0;i<190 && pool.length;i++) collect(pool,ship,cargo,85,0.05);
  assert.equal(pool.length,0);
  assert.equal(quantityOf(cargo,3),1);
  assert.equal(quantityOf(cargo,1),0);
});

function asteroid(field, { x = 0, y = 0, z = -100, radius = 10, ore = 1, variant = 0 } = {}) {
  const index = field.Count;
  assert.equal(addAsteroid(field, x, y, z, 0, 0, 0, radius, 0, 1, 0, 0, 0, 0.5, 0.5, 0.5, variant), true);
  field.Ore[index] = ore;
  return index;
}

function miningWorld() {
  const world = createWorld(0);
  resetWorld(world, 0);
  world.Player.Velocity = zero;
  world.Player.Controls.Throttle = 0;
  world.Player.Controls.Brake = true;
  return world;
}

function laser(world, { team = 0, x = 0, z = -80, damage = 2 } = {}) {
  assert.equal(spawnLaser(world.Lasers, x, 0, z, 0, 0, -520, 0, 0, 0, 1, damage, team), true);
}

function playerAt(position = zero) {
  const player = createPlayer();
  resetShip(player, position, identity);
  player.Velocity = zero;
  return player;
}

function stationaryPickup(pool, position, kind = 1) {
  spawn(pool, position, kind, 1);
  const pickup = pool.at(-1);
  pickup.Position = position;
  pickup.Velocity = zero;
  return pickup;
}

test("start fields contain roughly one quarter ore and accessible deposits ahead of the player", () => {
  const world = miningWorld();
  const field = createField(400);
  generateField(field, world.Rng, 360);
  assert.equal(field.Count, 360);
  const ores = Array.from(field.Ore.slice(0, field.Count));
  const ratio = ores.filter(kind => kind > 0).length / field.Count;
  assert.ok(ratio > 0.18 && ratio < 0.35, `ore ratio ${ratio}`);
  assert.ok(ores.includes(1) && ores.includes(2));
  for (let i = 0; i < 3; i++) {
    assert.ok(field.Ore[i] > 0);
    assert.ok(field.Data[i * 16 + 2] < -50 && field.Data[i * 16 + 2] > -300);
    assert.ok(Math.abs(field.Data[i * 16]) < 50);
  }
});

test("swap removal preserves the moved asteroid's ore and reused add slots start empty", () => {
  const field = createField(3);
  asteroid(field, { x: 10, ore: 1, variant: 0 });
  asteroid(field, { x: 20, ore: 0, variant: 1 });
  asteroid(field, { x: 30, ore: 2, variant: 2 });
  removeAsteroid(field, 0);
  assert.equal(field.Count, 2);
  assert.equal(field.Data[0], 30);
  assert.equal(field.Variant[0], 2);
  assert.equal(field.Ore[0], 2);
  assert.equal(field.Ore[1], 0);
  // The inactive tail still contained ore; add must overwrite it independently of generation.
  assert.equal(addAsteroid(field, 40, 0, -100, 0, 0, 0, 3, 0, 1, 0, 0, 0, 0.5, 0.5, 0.5, 0), true);
  assert.equal(field.Ore[2], 0);
});

test("the first player hit releases ore once even when two bolts land in the same step", () => {
  const world = miningWorld();
  asteroid(world.Asteroids, { ore: 2 });
  laser(world);
  laser(world);
  stepPhysics(world, 0.05);
  assert.equal(world.Asteroids.Ore[0], 0);
  assert.equal(releases(world).length, 1);
  const event = releases(world)[0];
  assert.equal(event.tag, 4);
  assert.equal(event.fields.length, 3);
  assert.ok(event.fields[0] instanceof Vec3);
  assert.equal(event.fields[1], 2);
  assert.ok(Number.isInteger(event.fields[2]) && event.fields[2] >= 2 && event.fields[2] <= 5);
  close(distance(event.fields[0], new Vec3(0, 0, -100)), 16, "safe surface distance");
  close(event.fields[0].Z, -84, "player-facing release position");
  laser(world);
  stepPhysics(world, 0.05);
  assert.equal(releases(world).length, 1);
});

test("an off-centre impact places the release beyond the surface along the line to the player", () => {
  const world = miningWorld();
  asteroid(world.Asteroids);
  laser(world, { x: 6 });
  stepPhysics(world, 0.05);
  const position = releases(world)[0].fields[0];
  const hit = new Vec3(6, 0, -92); // x² + (z + 100)² = radius².
  close(distance(position, new Vec3(0, 0, -100)), 16, "expanded asteroid shell");
  assert.ok(distance(position, zero) < distance(hit, zero));
  close((position.X - hit.X) / -6, (position.Z - hit.Z) / 92, "towards player from actual impact");
});

test("enemy fire preserves unmined ore for a later player laser", () => {
  const world = miningWorld();
  asteroid(world.Asteroids);
  laser(world, { team: 1 });
  stepPhysics(world, 0.05);
  assert.equal(world.Asteroids.Ore[0], 1);
  assert.equal(releases(world).length, 0);
  laser(world);
  stepPhysics(world, 0.05);
  assert.equal(releases(world).length, 1);
  assert.equal(world.Asteroids.Ore[0], 0);
});

test("collision damage cannot mine ore and destroyed asteroid fragments contain none", () => {
  const world = miningWorld();
  asteroid(world.Asteroids, { radius: 12, ore: 2 });
  world.Player.Position = new Vec3(0, 0, -86);
  world.Player.PrevPosition = world.Player.Position;
  world.Player.Velocity = new Vec3(0, 0, -20);
  world.Player.Controls.Brake = false;
  stepPhysics(world, 0.02);
  assert.equal(releases(world).length, 0);
  assert.equal(world.Asteroids.Ore[0], 2);
  assert.ok(world.Asteroids.Data[15] < 12 * 2.6, "the collision applied asteroid damage");
  damageAsteroid(world, 0, 1000, 0);
  assert.ok(world.Asteroids.Count >= 2 && world.Asteroids.Count <= 3);
  for (let i = 0; i < world.Asteroids.Count; i++) assert.equal(world.Asteroids.Ore[i], 0);
  assert.equal(releases(world).length, 0);
});

test("a fatal player hit releases before swap removal without consuming the moved deposit", () => {
  const world = miningWorld();
  asteroid(world.Asteroids, { radius: 5, ore: 1 });
  asteroid(world.Asteroids, { x: 100, radius: 5, ore: 2 });
  laser(world, { damage: 100 });
  stepPhysics(world, 0.05);
  assert.equal(releases(world).length, 1);
  assert.equal(releases(world)[0].fields[1], 1);
  assert.equal(world.Asteroids.Count, 1);
  assert.equal(world.Asteroids.Data[0], 100);
  assert.equal(world.Asteroids.Ore[0], 2);
  laser(world, { x: 100 });
  stepPhysics(world, 0.05);
  assert.equal(releases(world).length, 2);
  assert.equal(releases(world)[1].fields[1], 2);
});

test("nearby minerals load cargo units exactly once while remote minerals remain", () => {
  const pool = createSalvage();
  const player = playerAt();
  const cargo = createCargo(16);
  stationaryPickup(pool, new Vec3(10, 0, 0), 1);
  stationaryPickup(pool, new Vec3(0, 10, 0), 2);
  stationaryPickup(pool, new Vec3(500, 0, 0), 2);
  assert.equal(update(pool, player, cargo, 0.016), 3);
  assert.equal(quantityOf(cargo, 1), 3);
  assert.equal(usedVolume(cargo), 3);
  assert.equal(pool.length, 1);
  assert.equal(pool[0].Position.X, 500);
  assert.equal(update(pool, player, cargo, 0.016), 0);
  assert.equal(pool.length, 1);
});

test("the collection radius includes the hull and swept fast flybys do not miss minerals", () => {
  const player = playerAt();
  const pool = createSalvage();
  const cargo = createCargo(16);
  stationaryPickup(pool, new Vec3(12 + player.Radius - 0.05, 0, 0));
  assert.equal(update(pool, player, cargo, 0.001), 1);
  stationaryPickup(pool, zero, 2);
  player.PrevPosition = new Vec3(-200, 0, 0);
  player.Position = new Vec3(200, 0, 0);
  assert.equal(update(pool, player, cargo, 0.016), 2);
  assert.equal(quantityOf(cargo, 1), 3);
  assert.equal(pool.length, 0);
});

test("magnet attraction is limited to nearby living players", () => {
  const player = playerAt();
  const pool = createSalvage();
  const cargo = createCargo(16);
  const near = stationaryPickup(pool, new Vec3(40, 0, 0));
  const far = stationaryPickup(pool, new Vec3(80, 0, 0));
  assert.equal(update(pool, player, cargo, 0.1), 0);
  assert.ok(near.Position.X < 40 && near.Velocity.X < 0);
  assert.equal(far.Position.X, 80);
  assert.equal(far.Velocity.X, 0);
  player.Alive = false;
  const deadPool = createSalvage();
  const inside = stationaryPickup(deadPool, zero);
  const outside = stationaryPickup(deadPool, new Vec3(40, 0, 0));
  assert.equal(update(deadPool, player, cargo, 0.1), 0);
  assert.equal(deadPool.length, 2);
  assert.equal(inside.Position.X, 0);
  assert.equal(outside.Position.X, 40);
});

test("pickup capacity and lifetime are bounded, expired drops load nothing and freed slots are reusable", () => {
  const player = playerAt();
  const pool = createSalvage();
  const cargo = createCargo(16);
  spawn(pool, new Vec3(500, 0, 0), 1, 500);
  assert.equal(pool.length, 96);
  assert.ok(pool.every(pickup => pickup.Life === 90 && pickup.Quantity === 1 && pickup.Kind === 1));
  spawn(pool, zero, 2, 10);
  assert.equal(pool.length, 96);
  assert.equal(update(pool, player, cargo, 90), 0);
  assert.equal(usedVolume(cargo), 0);
  assert.equal(pool.length, 0);
  spawn(pool, zero, 0, 1);
  spawn(pool, zero, 1, -1);
  assert.equal(pool.length, 0);
  spawn(pool, zero, 2, 1);
  assert.equal(pool[0].Quantity, 2);
  assert.equal("Value" in pool[0], false);
  assert.equal(update(pool, player, cargo, 0), 0);
  assert.equal(pool[0].Life, 90);
  assert.equal(update(pool, player, cargo, 0.016), 2);
  assert.equal(pool.length, 0);
});

test("a full hold neither attracts nor consumes minerals and can collect them after unloading", () => {
  const player = playerAt();
  const cargo = createCargo(16);
  loadCargo(cargo, 1, 16);
  const pool = createSalvage();
  const near = stationaryPickup(pool, new Vec3(40, 0, 0));
  const touching = stationaryPickup(pool, zero, 2);
  assert.equal(update(pool, player, cargo, 0.1), 0);
  assert.equal(near.Position.X, 40);
  assert.equal(near.Velocity.X, 0);
  assert.equal(touching.Quantity, 2);
  assert.equal(pool.length, 2);
  assert.equal(usedVolume(cargo), 16);
  resetCargo(cargo, 16);
  assert.equal(update(pool, player, cargo, 0.1), 2);
  assert.equal(pool.length, 1);
  assert.equal(quantityOf(cargo, 1), 2);
});

test("dense amber crystals split exactly at the hold limit and retain their physical remainder", () => {
  const player = playerAt();
  const cargo = createCargo(16);
  loadCargo(cargo, 1, 15);
  const pool = createSalvage();
  const crystal = stationaryPickup(pool, zero, 2);
  assert.equal(update(pool, player, cargo, 0.016), 1);
  assert.equal(usedVolume(cargo), 16);
  assert.equal(pool.length, 1);
  assert.equal(pool[0], crystal);
  assert.equal(crystal.Kind, 2);
  assert.equal(crystal.Quantity, 1);
  assert.equal(update(pool, player, cargo, 0.016), 0);
  assert.equal(crystal.Quantity, 1);
  resetCargo(cargo, 16);
  assert.equal(update(pool, player, cargo, 0.016), 1);
  assert.equal(pool.length, 0);
  assert.equal(quantityOf(cargo, 1), 1);
  assert.equal(update(pool, player, cargo, 0.016), 0);
});

test("multiple simultaneous pickups cannot overfill cargo or delete an uncollected crystal", () => {
  const player = playerAt();
  const cargo = createCargo(16);
  loadCargo(cargo, 1, 15);
  const pool = createSalvage();
  stationaryPickup(pool, zero, 1);
  const dense = stationaryPickup(pool, zero, 2);
  assert.equal(update(pool, player, cargo, 0.016), 1);
  assert.equal(usedVolume(cargo), 16);
  assert.equal(pool.length, 1);
  assert.equal(pool[0], dense);
  assert.equal(dense.Quantity, 2);
  assert.equal(update(pool, player, cargo, 90), 0);
  assert.equal(pool.length, 0);
  assert.equal(usedVolume(cargo), 16);
});
