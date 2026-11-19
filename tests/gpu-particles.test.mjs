import test from "node:test";
import assert from "node:assert/strict";
import {
  ParticleRing_dropFor as dropFor, ParticleRing_expired as expired, ParticleRing_firstSpan as firstSpan, ParticleRing_secondSpan as secondSpan,
  Emission_due as due, Emission_remainder as remainder, Emission_moment as moment, Emission_spaced as spaced, Emission_scaled as scaled,
  GpuParticles_createCpu as createCpu, GpuParticles_setBudget as setBudget, GpuParticles_setClock as setClock, GpuParticles_retire as retire,
  GpuParticles_emit as emit, GpuParticles_clear as clear, GpuParticles_attach as attach, GpuParticles_detach as detach,
  GpuParticles_create as createGpu, GpuParticles_draw as draw, GpuParticles_presets as presets,
} from "../build/GpuParticles.js";
import { trail, spark, ember, smoke, retro } from "../build/ParticleRecipes.js";
import { create as createTrails, frame, player, enemy } from "../build/EngineTrails.js";
import {
  WorldModule_create as createWorld, WorldModule_reset as resetWorld, ShipModule_reset as resetShip,
  Physics_step as step, Damage_hitShip as hitShip,
} from "../build/PhysicsAndPool.js";
import { EnemyProfiles_configure as configure } from "../build/EnemyProfiles.js";
import { EnemyMeshes_hull as enemyHull } from "../build/EnemyMeshes.js";
import { PlayerMeshes_hulls as playerHulls } from "../build/PlayerMeshes.js";
import { Vec3, QuatModule_identity as identity } from "../build/Math3D.js";
import { budget } from "../build/CombatVfx.js";

const STRIDE = 24;
const STEP = 1 / 120;
const [GLOW, SPARK, SMOKE] = [0, 1, 2];

function shapes(system) {
  const counts = [0, 0, 0];
  for (let i = 0, slot = system.Tail; i < system.Count; i++, slot = (slot + 1) % system.Capacity) counts[system.Data[slot * STRIDE + 20]]++;
  return counts;
}

test("the ring arc splits into at most two spans and drops the oldest at the budget", () => {
  assert.equal(firstSpan(10, 5, 100), 5);
  assert.equal(secondSpan(10, 5, 100), 0);
  assert.equal(firstSpan(97, 5, 100), 3, "wraps after the last slot");
  assert.equal(secondSpan(97, 5, 100), 2);
  assert.equal(firstSpan(0, 250, 100) + secondSpan(0, 250, 100), 100, "never more than the ring");
  assert.equal(dropFor(10, 100), 0);
  assert.equal(dropFor(100, 100), 1, "a full arc gives up its oldest particle");
  assert.equal(dropFor(500, 100), 401, "a lowered budget trims the arc on the next spawn");
});

test("expired particles leave the arc end only up to the first living one", () => {
  const death = new Float64Array([1, 2, 9, 1, 1]);
  assert.equal(expired(death, 0, 5, 5, 3), 2, "stops at the long-lived third particle");
  assert.equal(expired(death, 3, 4, 5, 3), 4, "wraps around the ring");
  assert.equal(expired(death, 0, 5, 5, 0.5), 0);
});

test("emission timing keeps the long-run rate and spreads particles over the frame", () => {
  let carry = 0.3, total = 0;
  for (let i = 0; i < 1000; i++) {
    const dt = i % 3 === 0 ? 1 / 30 : 1 / 144;
    total += due(carry, 75, dt);
    carry = remainder(carry, 75, dt);
    assert.ok(carry >= 0 && carry < 1);
  }
  const seconds = 334 / 30 + 666 / 144;
  assert.ok(Math.abs(total - 75 * seconds) <= 1.5, `${total} vs ${75 * seconds}`);
  const n = due(0.6, 300, 1 / 60);
  assert.equal(n, 5);
  const moments = Array.from({ length: n }, (_, j) => moment(0.6, 300, 1 / 60, j));
  for (let j = 1; j < n; j++) assert.ok(moments[j] > moments[j - 1], "later particles are younger");
  assert.ok(moments[0] > 0 && moments[n - 1] <= 1);
  assert.ok(Math.abs(moments[1] - moments[0] - 1 / 5) < 1e-9, "equal spacing in time and therefore along the path");
  assert.equal(spaced(60, 0.3, 10), 200);
  assert.equal(spaced(0, 0.3, 10), 10, "standing still keeps the minimum rate");
  assert.equal(scaled(0, 1), 0);
  assert.equal(scaled(3, 0.01), 1);
  assert.equal(scaled(10, 0.5), 5);
});

test("a particle is written once with start, motion, life, sizes, colours and shape", () => {
  const s = createCpu(64, 64);
  setClock(s, 10);
  emit(s, spark, 10.25, 1, 2, 3, 4, 5, 6, 1, 2, 0.5, 1, 1);
  assert.equal(s.Count, 1);
  const d = s.Data;
  assert.deepEqual([d[0], d[1], d[2]], [1, 2, 3]);
  assert.ok(Math.abs(d[3] + s.Epoch - 10.25) < 1e-5, "birth time relative to the epoch");
  assert.deepEqual([d[4], d[5], d[6]], [4, 5, 6]);
  assert.ok(d[7] > 0 && Math.abs(d[7] - spark.Life) <= spark.Life * spark.LifeJitter + 1e-6);
  assert.ok(Math.abs(d[11] - spark.Drag) < 1e-6);
  assert.ok(Math.abs(d[12] - spark.R0 * 0.5) < 1e-5 && Math.abs(d[13] - spark.G0) < 1e-5, "tint multiplies the start colour");
  assert.ok(Math.abs(d[15] - spark.Size0 * 2) < 1e-6 && Math.abs(d[19] - spark.Size1 * 2) < 1e-6);
  assert.equal(d[20], SPARK);
  assert.ok(Math.abs(s.Death[0] - (10.25 + d[7])) < 1e-5);
  assert.equal(s.DirtyCount, 1);
});

test("the budget caps live particles, applies to new particles and stays within the ring", () => {
  const s = createCpu(4096, 4096);
  setBudget(s, 1000);
  assert.equal(s.Budget, 1000);
  for (let i = 0; i < 1500; i++) emit(s, ember, 0, i, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  assert.equal(s.Count, 1000);
  assert.equal(s.Data[s.Tail * STRIDE], 500, "the oldest gave way");
  setBudget(s, 600);
  assert.equal(s.Count, 1000, "lowering the budget leaves existing particles alone");
  emit(s, ember, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  assert.equal(s.Count, 600, "until the next spawn");
  setBudget(s, 10);
  assert.equal(s.Budget, 512, "minimum budget");
  setBudget(s, 1e6);
  assert.equal(s.Budget, 4096, "never above the ring capacity");
  assert.deepEqual(Array.from(presets), [3072, 6144, 12288, 24576]);
});

test("the clock retires dead particles, survives a long session and clears on a time jump", () => {
  const s = createCpu(256, 256);
  setClock(s, 5);
  emit(s, trail, 5, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  emit(s, ember, 5, 0, 0, 0, 0, 0, 0, 4, 1, 1, 1, 1);
  setClock(s, 6); retire(s);
  assert.equal(s.Count, 1, "the short trail particle is gone, the ember glows on");
  // Long session: the epoch follows, relative birth times keep their absolute value.
  const before = s.Data[s.Tail * STRIDE + 3] + s.Epoch;
  for (let t = 6; t <= 1100; t += 10) setClock(s, t);
  assert.ok(s.Epoch > 1000);
  assert.ok(Math.abs(s.Data[s.Tail * STRIDE + 3] + s.Epoch - before) < 1e-3);
  assert.ok(s.AllDirty, "the shifted arc is uploaded again");
  setClock(s, 3); // loading an older save
  assert.equal(s.Count, 0);
  assert.equal(s.Epoch, 3);
  emit(s, trail, 3, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  clear(s);
  assert.equal(s.Count, 0);
});

function emptyWorld() {
  const world = createWorld(16);
  resetWorld(world, 0);
  world.Player.Position = new Vec3(0, 0, 5000);
  world.Player.PrevPosition = world.Player.Position;
  return world;
}

function killRole(world, role) {
  const ship = world.Enemies[0];
  configure(ship, role, 1);
  resetShip(ship, new Vec3(0, 0, 0), identity);
  hitShip(world, ship, 1e6, false);
  for (let i = 0; i < 5 / STEP; i++) step(world, STEP);
}

test("combat hooks add GPU sparks, embers and smoke that grow with the destruction tier", () => {
  detach();
  const plain = emptyWorld();
  killRole(plain, 0);
  const spawned = [];
  for (const role of [0, 2, 4]) {
    const s = createCpu(32768, 32768);
    attach(s);
    const world = emptyWorld();
    killRole(world, role);
    detach();
    const counts = shapes(s);
    assert.ok(counts[GLOW] > 0 && counts[SPARK] > 0 && counts[SMOKE] > 0, `role ${role}: ${counts}`);
    spawned.push(s.Spawned);
  }
  assert.ok(spawned[0] > budget(0) * 1.5, `a drone blast gains clearly more particles than its CPU budget (${spawned[0]} vs ${budget(0)})`);
  assert.ok(spawned[1] > spawned[0] * 2 && spawned[2] > spawned[1] * 1.5, `tiers: ${spawned}`);
});

test("cosmetic GPU particles never touch the world's random sequence or CPU pool", () => {
  detach();
  const a = emptyWorld();
  killRole(a, 2);
  const s = createCpu(32768, 32768);
  attach(s);
  const b = emptyWorld();
  killRole(b, 2);
  detach();
  assert.ok(s.Spawned > 0);
  assert.equal(b.Rng.State, a.Rng.State);
  assert.equal(b.Particles.Count, a.Particles.Count);
});

function flyPlayer({ throttle = 0, boost = 0, brake = false, speed = 0, yawRate = 0, frames = 60, jumpAt = -1 }) {
  const s = createCpu(32768, 12288);
  const t = createTrails(16, 8);
  const world = emptyWorld();
  const ship = world.Player;
  const hull = playerHulls[0];
  ship.Controls.Throttle = throttle; ship.Controls.Brake = brake; ship.Boost = boost; ship.YawRate = yawRate;
  ship.Velocity = new Vec3(0, 0, -speed);
  let z = 0;
  for (let i = 0; i < frames; i++) {
    setClock(s, 1 + i / 60);
    frame(t, s);
    z -= speed / 60;
    if (i === jumpAt) z -= 5000;
    player(t, s, ship, hull, new Vec3(0, 0, z), identity, false);
  }
  return s;
}

test("engine trails follow thrust, brighten with boost and hang evenly spaced in world space", () => {
  assert.equal(flyPlayer({}).Spawned, 0, "idle engines leave no trail");
  const cruise = flyPlayer({ throttle: 1, speed: 60 });
  const boost = flyPlayer({ throttle: 1, boost: 1, speed: 130 });
  assert.ok(cruise.Spawned > 300, `cruise trail (${cruise.Spawned})`);
  assert.ok(boost.Spawned > cruise.Spawned * 1.5, "boost thickens the trail");
  const peak = s => Math.max(...Array.from({ length: s.Count }, (_, i) => s.Data[((s.Tail + i) % s.Capacity) * STRIDE + 12]));
  assert.ok(peak(boost) > peak(cruise) * 1.5, "boost is clearly brighter");
  // One nozzle: consecutive particles sit at equal distances along the path.
  const zs = [];
  for (let i = 0; i < cruise.Count; i++) {
    const o = ((cruise.Tail + i) % cruise.Capacity) * STRIDE;
    if (cruise.Data[o + 20] === GLOW && cruise.Data[o] < 0) zs.push(cruise.Data[o + 2]);
  }
  const gaps = zs.slice(1).map((z, i) => Math.abs(z - zs[i]));
  const mean = gaps.reduce((a, b) => a + b, 0) / gaps.length;
  assert.ok(mean > 0.15 && mean < 0.4, `spacing ${mean}`);
  assert.ok(Math.max(...gaps) < mean * 1.6, "no clumps at frame boundaries");
  // Exhaust drifts backwards gently and does not travel with the ship.
  const o = cruise.Tail * STRIDE;
  assert.ok(Math.abs(cruise.Data[o + 6]) < 4, "trail particles stay roughly where they were born");
});

test("a jump restarts the trail instead of streaking across the sector", () => {
  const s = flyPlayer({ throttle: 1, speed: 60, jumpAt: 30 });
  for (let i = 0; i < s.Count; i++) {
    const z = s.Data[((s.Tail + i) % s.Capacity) * STRIDE + 2];
    assert.ok(z > -100 || z < -5000, `no particle in the gap (${z})`);
  }
});

test("retro jets fire forward while braking and side jets puff in hard turns", () => {
  const braking = flyPlayer({ brake: true, speed: 40, frames: 20 });
  const counts = shapes(braking);
  assert.ok(counts[SMOKE] > 10, `retro puffs (${counts})`);
  let ahead = 0;
  for (let i = 0; i < braking.Count; i++) {
    const o = ((braking.Tail + i) % braking.Capacity) * STRIDE;
    if (braking.Data[o + 20] === SMOKE && braking.Data[o + 6] < -40) ahead++;
  }
  assert.ok(ahead > counts[SMOKE] * 0.8, "gas leaves the nose faster than the ship");
  assert.equal(flyPlayer({ speed: 0, frames: 20 }).Spawned, 0);
  const turning = flyPlayer({ yawRate: 5, frames: 20 });
  let sideways = 0;
  for (let i = 0; i < turning.Count; i++) {
    const o = ((turning.Tail + i) % turning.Capacity) * STRIDE;
    if (Math.abs(turning.Data[o + 4]) > 8) sideways++;
  }
  assert.ok(sideways > 5, `side jets (${sideways})`);
});

test("enemy trails come from every nozzle and stop beyond sight or once the budget runs full", () => {
  const run = (eye, fill) => {
    const s = createCpu(32768, 12288);
    for (let i = 0; i < fill; i++) emit(s, ember, 0, 0, 0, 0, 0, 0, 0, 100, 1, 1, 1, 1);
    const t = createTrails(16, 8);
    const world = emptyWorld();
    const ship = world.Enemies[0];
    configure(ship, 4, 1);
    ship.Controls.Throttle = 1;
    for (let i = 0; i < 60; i++) {
      setClock(s, 1 + i / 60);
      frame(t, s);
      enemy(t, s, 0, ship, enemyHull(4), new Vec3(0, 0, -40 * i / 60), identity, 1, eye);
    }
    return s.Spawned - fill;
  };
  const near = run(new Vec3(0, 0, 80), 0);
  assert.ok(near > 5 * 20, `five nozzles (${near})`);
  assert.equal(run(new Vec3(0, 0, 4000), 0), 0, "too far to see");
  assert.equal(run(new Vec3(0, 0, 80), 10000), 0, "trails yield to combat effects near the budget");
});

function mockGl() {
  const calls = [];
  let id = 0;
  const gl = new Proxy({}, {
    get(_, name) {
      if (typeof name !== "string") return undefined;
      if (/^[A-Z0-9_]+$/.test(name)) return name;
      return (...args) => {
        calls.push([name, ...args]);
        if (name === "getShaderParameter" || name === "getProgramParameter") return true;
        if (name.startsWith("create") || name === "getUniformLocation") return { id: ++id, name: args[1] };
        return undefined;
      };
    },
  });
  return { gl, calls };
}

test("drawing uploads only new particles and hands the renderer back its cached state", () => {
  const { gl, calls } = mockGl();
  const renderer = {
    Gl: gl, Program: { main: true }, ViewProj: new Float32Array(16), CameraPos: new Float32Array(3), Height: 1080,
    Post: { Hdr: true }, Lights: { SunDirection: new Float32Array([0, 1, 0]), SunColor: new Float32Array([1, 1, 1]) },
    DrawCalls: 0, InstancesDrawn: 0, BlendState: 1,
  };
  const s = createGpu(renderer, 12288);
  assert.ok(s.Gl != null, "programs linked");
  const view = new Float32Array(16); view[0] = view[5] = view[10] = view[15] = 1;
  setClock(s, 1);
  for (let i = 0; i < 40; i++) emit(s, smoke, 1, i, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  calls.length = 0;
  draw(renderer, s, view, 1.0, 0, 0, 0);
  const uploads = calls.filter(c => c[0] === "bufferSubData");
  assert.equal(uploads.length, 1);
  assert.equal(uploads[0][5], 40 * STRIDE, "exactly the 40 new particles");
  assert.ok(calls.some(c => c[0] === "drawArraysInstanced" && c[4] === 40));
  const last = name => calls.map(c => c[0]).lastIndexOf(name);
  assert.equal(calls[last("useProgram")][1], renderer.Program, "main shader active again");
  assert.ok(calls.findLast(c => c[0] === "depthMask")[1] === true);
  assert.equal(calls.findLast(c => c[0] === "disable")[1], "BLEND");
  assert.equal(calls.findLast(c => c[0] === "enable")[1], "CULL_FACE");
  assert.equal(calls.findLast(c => c[0] === "bindVertexArray")[1], null);
  assert.equal(renderer.BlendState, 0);
  calls.length = 0;
  draw(renderer, s, view, 1.0, 0, 0, 0);
  assert.equal(calls.filter(c => c[0] === "bufferSubData").length, 0, "nothing new, nothing uploaded");
  // Across the end of the ring: one upload and one draw per span.
  for (let i = 0; i < s.Capacity - 140; i++) emit(s, ember, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  draw(renderer, s, view, 1.0, 0, 0, 0);
  calls.length = 0;
  for (let i = 0; i < 300; i++) emit(s, ember, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1);
  draw(renderer, s, view, 1.0, 0, 0, 0);
  const spans = calls.filter(c => c[0] === "bufferSubData");
  assert.deepEqual(spans.map(c => c[5] / STRIDE), [100, 200], "tail of the ring, then its start");
  const draws = calls.filter(c => c[0] === "drawArraysInstanced").map(c => c[4]);
  assert.equal(draws.reduce((a, b) => a + b, 0), s.Budget, "the arc is capped by the budget");
  assert.equal(draws.length, 2);
});
