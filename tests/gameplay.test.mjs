import test from "node:test";
import assert from "node:assert/strict";
import { EnemyProfiles_profile as profile, EnemyProfiles_configure as configure } from "../build/EnemyProfiles.js";
import { AI_createBrain as createBrain, AI_reset as resetBrain, AI_update as updateAI } from "../build/AI.js";
import {
  ShipModule_createInterceptor as createEnemy,
  ShipModule_createPlayer as createPlayer,
  ShipModule_reset as resetShip,
  WorldModule_create as createWorld,
  WorldModule_reset as resetWorld,
} from "../build/PhysicsAndPool.js";
import {
  Mission_stages as stages,
  Mission_stage as stage,
  Mission_applyUpgrade as upgrade,
  Mission_resetPlayer as resetPlayer,
} from "../build/Mission.js";
import { Vec3, Vec3Module_zero as zero, QuatModule_identity as identity } from "../build/Math3D.js";

const roles = [0, 1, 2, 3, 4];
const heavyRoles = [2, 3, 4];
const close = (actual, expected, label) =>
  assert.ok(Math.abs(actual - expected) < 1e-9, `${label}: ${actual} != ${expected}`);
const vector = (value) => [value.X, value.Y, value.Z];
const stateName = (brain) => brain.State.cases()[brain.State.tag];

function configuredStats(ship) {
  return {
    speed: [ship.CruiseSpeed, ship.BoostSpeed],
    turn: [ship.MaxPitchRate, ship.MaxYawRate, ship.MaxRollRate],
    health: [ship.MaxHull, ship.MaxShield],
    weapon: [ship.FireInterval, ship.LaserDamage],
    radius: ship.Radius,
    center: vector(ship.ObbCenter),
    half: vector(ship.ObbHalf),
    left: vector(ship.GunLeft),
    right: vector(ship.GunRight),
  };
}

function encounter(role, distance = profile(role).PreferredRange) {
  const world = createWorld(1);
  resetWorld(world, 0);
  const ship = world.Enemies[0];
  configure(ship, role, 1);
  resetShip(ship, zero, identity);
  ship.Velocity = zero;
  world.Player.Position = new Vec3(0, 0, -distance);
  world.Player.Velocity = zero;
  const brain = createBrain(0);
  brain.Role = role;
  resetBrain(brain, ship, zero);
  return { world, ship, brain };
}

test("five finite, distinct enemy profiles remain slower than the former interceptor", () => {
  const profiles = roles.map(profile);
  for (const p of profiles) {
    for (const [key, value] of Object.entries(p)) {
      if (typeof value === "number") assert.ok(Number.isFinite(value), `${p.Name}.${key}`);
    }
    for (const value of [...vector(p.Scale), ...vector(p.Tint)]) assert.ok(Number.isFinite(value));
    assert.ok(p.CruiseSpeed > 0 && p.CruiseSpeed + p.BoostSpeed < 62, p.Name);
    assert.ok(p.Hull > 0 && p.Shield >= 0, p.Name);
    assert.ok(p.Scale.X > 0);
    assert.equal(p.Scale.X, p.Scale.Y);
    assert.equal(p.Scale.X, p.Scale.Z);
    assert.match(p.Color, /^#[0-9a-f]{6}$/i);
  }
  for (const values of [profiles.map(p => p.Name), profiles.map(p => p.CruiseSpeed),
    profiles.map(p => p.Scale.X), profiles.map(p => p.Hull + p.Shield)]) {
    assert.equal(new Set(values).size, 5);
  }
});

test("reused enemy slots configure absolutely and keep collider and guns aligned with their mesh", () => {
  const base = createEnemy();
  const reused = createEnemy();
  for (const role of roles) {
    const fresh = createEnemy();
    configure(fresh, role, 4);
    configure(reused, (role + 2) % 5, 10);
    configure(reused, role, 4);
    assert.deepEqual(configuredStats(reused), configuredStats(fresh));
    configure(reused, role, 4);
    assert.deepEqual(configuredStats(reused), configuredStats(fresh));
    const scale = profile(role).Scale.X;
    close(reused.Radius, base.Radius * scale, "radius");
    for (const name of ["ObbCenter", "ObbHalf", "GunLeft", "GunRight"]) {
      vector(reused[name]).forEach((value, axis) => close(value, vector(base[name])[axis] * scale, name));
    }
    resetShip(reused, zero, identity);
    assert.equal(reused.Hull, reused.MaxHull);
    assert.equal(reused.Shield, reused.MaxShield);
  }
});

test("brain reset preserves its assigned role and clears a previous weapon charge", () => {
  const brain = createBrain(3);
  assert.equal(brain.Role, 0);
  for (const role of roles) {
    const ship = createEnemy();
    configure(ship, role, 2);
    resetShip(ship, zero, identity);
    brain.Role = role;
    brain.WeaponCharge = 0.9;
    brain.FiringBurst = true;
    brain.StateTime = 20;
    resetBrain(brain, ship, new Vec3(5, 6, 7));
    assert.equal(brain.Role, role);
    assert.equal(brain.WeaponCharge, 0);
    assert.equal(brain.FiringBurst, false);
    assert.equal(brain.StateTime, 0);
    assert.equal(stateName(brain), "Chase");
    assert.deepEqual(vector(brain.Home), [5, 6, 7]);
  }
});

for (const role of heavyRoles) {
  test(`${profile(role).Name} telegraphs a burst, fires, rests and charges again without boosting`, () => {
    const { world, ship, brain } = encounter(role);
    const p = profile(role);
    // Fixed target and world clock isolate the weapon cadence from flight and aim wobble.
    updateAI(world, [brain], 0.04);
    assert.equal(stateName(brain), "Attack");
    const advanceTo = (time) => {
      updateAI(world, [brain], time - brain.StateTime);
      assert.equal(ship.Controls.Boost, false);
      assert.equal(stateName(brain), "Attack");
    };
    advanceTo(p.ChargeTime / 2);
    close(brain.WeaponCharge, 0.5, "half charge");
    assert.equal(brain.FiringBurst, false);
    assert.equal(ship.Controls.Fire, false);
    advanceTo(p.ChargeTime + p.BurstTime / 2);
    assert.equal(brain.WeaponCharge, 0);
    assert.equal(brain.FiringBurst, true);
    assert.equal(ship.Controls.Fire, true);
    advanceTo(p.ChargeTime + p.BurstTime + p.RestTime / 2);
    assert.equal(brain.WeaponCharge, 0);
    assert.equal(brain.FiringBurst, false);
    assert.equal(ship.Controls.Fire, false);
    advanceTo(p.ChargeTime + p.BurstTime + p.RestTime + p.ChargeTime / 2);
    close(brain.WeaponCharge, 0.5, "next charge");
    assert.equal(ship.Controls.Fire, false);
  });
}

test("all roles keep pursuing a live player beyond the former detection and loss ranges", () => {
  for (const role of roles) {
    const { world, ship, brain } = encounter(role, 2000);
    for (let second = 0; second < 15; second++) {
      world.Time = second;
      updateAI(world, [brain], 1);
      assert.equal(stateName(brain), "Chase", profile(role).Name);
      assert.equal(ship.Controls.Throttle, 1);
      if (role !== 1) assert.equal(ship.Controls.Boost, false);
      for (const value of [ship.Controls.Pitch, ship.Controls.Yaw, ship.Controls.Roll]) {
        assert.ok(Number.isFinite(value));
      }
    }
  }
});

test("mission has six increasing stages and exactly one commander in the finale", () => {
  assert.deepEqual(stages.map(s => s.Enemies.length), [4, 6, 8, 10, 12, 14]);
  assert.equal(stages.slice(0, -1).flatMap(s => Array.from(s.Enemies)).includes(4), false);
  assert.equal(Array.from(stages.at(-1).Enemies).filter(role => role === 4).length, 1);
  stages.forEach((value, index) => {
    assert.equal(stage(index + 1), value);
    assert.ok(value.Name && value.Objective && value.Hint);
    for (const role of value.Enemies) assert.ok(roles.includes(role));
  });
  assert.equal(stage(0), stages[0]);
  assert.equal(stage(99), stages.at(-1));
});

test("upgrades improve the chosen stat, repair damage and restore shields without exceeding hull capacity", () => {
  const ship = createPlayer();
  const levels = new Int32Array(3);
  resetPlayer(ship);
  resetShip(ship, zero, identity);
  ship.Hull = 10;
  ship.Shield = 3;
  ship.ShieldDelay = 3;
  ship.Overdrive = 4;
  upgrade(ship, levels, 0);
  close(ship.LaserDamage, 14.4, "laser upgrade");
  assert.equal(ship.Hull, 45);
  assert.equal(ship.Shield, 100);
  assert.equal(ship.ShieldDelay, 0);
  assert.equal(ship.Overdrive, 0);
  upgrade(ship, levels, 1);
  assert.equal(ship.MaxShield, 130);
  assert.equal(ship.Shield, 130);
  assert.equal(ship.Hull, 80);
  upgrade(ship, levels, 2);
  assert.equal(ship.MaxHull, 125);
  assert.equal(ship.Hull, 125);
  assert.deepEqual(Array.from(levels), [1, 1, 1]);
  upgrade(ship, levels, 0);
  close(ship.LaserDamage, 16.8, "second laser upgrade");
  assert.equal(ship.Hull, ship.MaxHull);
  const before = configuredStats(ship);
  upgrade(ship, levels, -1);
  upgrade(ship, levels, 3);
  assert.deepEqual(configuredStats(ship), before);
  assert.deepEqual(Array.from(levels), [2, 1, 1]);
});

test("new-flight reset removes upgrades, combat state, live enemies and pooled effects", () => {
  const world = createWorld(3);
  const ship = world.Player;
  resetWorld(world, 0);
  const levels = new Int32Array(3);
  for (const choice of [0, 0, 1, 2]) upgrade(ship, levels, choice);
  ship.Hull = 0;
  ship.Shield = 0;
  ship.Alive = false;
  ship.FireCooldown = 5;
  ship.ShieldDelay = 4;
  ship.HitFlash = 1;
  ship.ShieldFlash = 1;
  ship.Overdrive = 5;
  ship.Boost = 1;
  ship.Controls.Fire = true;
  ship.Controls.Boost = true;
  ship.Controls.Brake = true;
  world.Enemies.forEach(enemy => { enemy.Alive = true; });
  world.Lasers.Count = 2;
  world.Particles.Count = 4;
  world.Flashes.fill(1);
  world.Events.push({});
  // These public operations are called by Main.startGame; its UI state is browser-tested.
  resetPlayer(ship);
  resetWorld(world, 0);
  assert.deepEqual([ship.MaxHull, ship.Hull, ship.MaxShield, ship.Shield], [100, 100, 100, 100]);
  assert.equal(ship.LaserDamage, 12);
  assert.equal(ship.CruiseSpeed, 60);
  assert.equal(ship.BoostSpeed, 75);
  assert.equal(ship.Alive, true);
  assert.deepEqual(vector(ship.Position), [0, 0, 0]);
  for (const field of ["FireCooldown", "ShieldDelay", "HitFlash", "ShieldFlash", "Overdrive", "Boost"]) {
    assert.equal(ship[field], 0, field);
  }
  for (const field of ["Fire", "Boost", "Brake"]) assert.equal(ship.Controls[field], false, field);
  assert.equal(world.Enemies.some(enemy => enemy.Alive), false);
  assert.equal(world.Lasers.Count, 0);
  assert.equal(world.Particles.Count, 0);
  assert.equal(world.Asteroids.Count, 0);
  assert.equal(world.Flashes.some(value => value !== 0), false);
  assert.equal(world.Events.length, 0);
});
