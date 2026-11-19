import test from "node:test";
import assert from "node:assert/strict";
import { score, select } from "../build/LightSelection.js";
import { encode, surface, emissive } from "../build/Materials.js";
import {
  Shaders_fragment as fragment, Shaders_vertex as vertex,
  LightsModule_create as createLights, LightsModule_addPoint as addPoint, LightsModule_select as selectLights,
  LightsModule_setBudget as setBudget, LightsModule_setAmbient as setAmbient,
} from "../build/WebGLRenderer.js";
import {
  MeshColorModule_metal as metal, MeshColorModule_glass as glass, MeshColorModule_rubber as rubber,
  MeshColorModule_rgb as rgb, MeshColorModule_glow as glow, MeshColorModule_surface as surfaceOf,
} from "../build/ProceduralMeshes.js";
import { PlayerMeshes_models as models } from "../build/PlayerMeshes.js";
import { coefficients, project, apply as applySky } from "../build/SkyLight.js";
import { Universe_sectors as sectors } from "../build/Universe.js";
import {
  CombatLights_create as createPool, CombatLights_envelope as envelope, CombatLights_blast as blast,
  CombatLights_push as pushPool,
} from "../build/CombatLights.js";
import { recipe, secondaryTime } from "../build/CombatVfx.js";
import { Vec3 } from "../build/Math3D.js";

const PAINT = 0, METAL = 1, GLASS = 2, RUBBER = 3;

/** Candidate buffers as LightSelection reads them: [x, y, z, range] and [r, g, b, -]. */
const buffers = (lights) => {
  const pos = new Float32Array(lights.length * 4), col = new Float32Array(lights.length * 4);
  lights.forEach(([x, y, z, range, power], i) => { pos.set([x, y, z, range], i * 4); col.set([power, power * 0.5, power * 0.2, 0], i * 4); });
  return [pos, col];
};

test("light score keeps full power inside the range and falls with (range / distance)^2 outside", () => {
  assert.equal(score(2, 10, 4), 2);
  assert.equal(score(2, 10, 10), 2);
  assert.ok(Math.abs(score(2, 10, 20) - 0.5) < 1e-12);
  assert.ok(Math.abs(score(2, 10, 40) - 0.125) < 1e-12);
  assert.equal(score(0, 10, 1), 0);
  assert.equal(score(3, 0, 1), 0);
});

test("selection keeps the most important lights regardless of call order and respects the budget", () => {
  // Camera at the origin. Far gate light, weak nearby lights, one bright explosion close by.
  const lights = [
    [0, 0, -900, 78, 1.0], // gate: barely matters from here
    [2, 0, 3, 16, 0.6], // own engine
    [0, 1, -30, 60, 3.2], // explosion glow 30 m ahead
    [400, 0, 0, 20, 2.0], // distant enemy engine
    [1, 0, -2, 24, 0.8], // own muzzle
    [0, 0, 0, 10, 0.0], // switched off
  ];
  const [pos, col] = buffers(lights);
  const top3 = Array.from(select(pos, col, lights.length, 0, 0, 0, 3));
  assert.deepEqual(top3, [2, 4, 1]);
  // Reversed call order selects the same lights (indices mirror).
  const reversed = [...lights].reverse();
  const [rpos, rcol] = buffers(reversed);
  const picked = Array.from(select(rpos, rcol, reversed.length, 0, 0, 0, 3)).map((i) => lights.length - 1 - i);
  assert.deepEqual(picked, top3);
  // Lights without weight never take a slot, even with spare budget.
  const all = Array.from(select(pos, col, lights.length, 0, 0, 0, 32));
  assert.equal(all.length, 5);
  assert.ok(!all.includes(5));
  assert.equal(select(pos, col, lights.length, 0, 0, 0, 0).length, 0);
});

test("equal weights keep the earlier light", () => {
  const [pos, col] = buffers([[0, 0, 5, 10, 1], [0, 0, -5, 10, 1], [5, 0, 0, 10, 1]]);
  assert.deepEqual(Array.from(select(pos, col, 3, 0, 0, 0, 2)), [0, 1]);
});

test("Lights keeps a late, bright explosion although many weaker lights were added first", () => {
  const lights = createLights();
  assert.equal(lights.Budget, 16);
  for (let i = 0; i < 40; i++) addPoint(lights, 300 + i * 10, 0, 0, 20, 1, 0.45, 0.15, 0.5);
  addPoint(lights, 0, 0, -25, 55, 1, 0.62, 0.25, 7);
  selectLights(lights, 0, 0, 0);
  assert.equal(lights.PointCount, 16);
  assert.deepEqual(Array.from(lights.PointPosRange.slice(0, 4)), [0, 0, -25, 55]);
  setBudget(lights, 99);
  assert.equal(lights.Budget, 32);
  selectLights(lights, 0, 0, 0);
  assert.equal(lights.PointCount, 32);
  setBudget(lights, -3);
  selectLights(lights, 0, 0, 0);
  assert.equal(lights.PointCount, 0);
  // More candidates than the buffer holds: the brightest still replace the weakest.
  setBudget(lights, 4);
  lights.CandidateCount = 0; // Lights.clearPoints (inline)
  for (let i = 0; i < 200; i++) addPoint(lights, 0, 0, i, 5, 0.1, 0.1, 0.1, 1);
  addPoint(lights, 1, 1, 1, 40, 1, 1, 1, 5);
  selectLights(lights, 0, 0, 0);
  assert.deepEqual(Array.from(lights.PointPosRange.slice(0, 4)), [1, 1, 1, 40]);
});

test("surface classes ride in the sign of the emissive channel and decode exactly", () => {
  assert.equal(encode(PAINT, 0.7), 0.7);
  assert.equal(encode(PAINT, 1.4), 1.4);
  for (const cls of [METAL, GLASS, RUBBER]) {
    for (const glowValue of [0, 0.22, 0.6, 2.0]) {
      const channel = Math.fround(encode(cls, glowValue));
      assert.ok(channel < 0);
      assert.equal(surface(channel), cls);
      assert.ok(Math.abs(emissive(channel) - Math.min(glowValue, 0.95)) < 1e-6);
    }
  }
  assert.equal(surface(0.5), PAINT);
  assert.equal(emissive(0.5), 0.5);
  assert.equal(surfaceOf(metal(0.3, 0.3, 0.3)), METAL);
  assert.equal(surfaceOf(glass(0.03, 0.2, 0.3, 0.22)), GLASS);
  assert.equal(surfaceOf(rubber(0.02, 0.03, 0.04)), RUBBER);
  assert.equal(surfaceOf(rgb(0.5, 0.5, 0.5)), PAINT);
  assert.equal(glow(1, 1, 1, 0.9).Emissive, 0.9);
});

test("player hulls use glass canopies, bare metal nozzles and rubber fenders next to their paint", () => {
  for (const build of models) {
    const v = build().Vertices;
    const counts = [0, 0, 0, 0];
    for (let i = 9; i < v.length; i += 10) counts[surface(v[i])]++;
    const total = v.length / 10;
    assert.ok(counts[GLASS] >= 30, `glass ${counts[GLASS]}`);
    assert.ok(counts[METAL] >= 300, `metal ${counts[METAL]}`);
    assert.ok(counts[RUBBER] >= 36, `rubber ${counts[RUBBER]}`);
    // Paint stays the main material.
    assert.ok(counts[PAINT] > total * 0.5, `paint ${counts[PAINT]} of ${total}`);
  }
});

test("shaders decode the surface class and shade with GGX, Smith, Schlick and sky light", () => {
  assert.ok(vertex.includes("vSurface"));
  assert.ok(vertex.includes("floor(-channel)"));
  assert.ok(fragment.includes("#define MAX_POINT_LIGHTS 32"));
  for (const term of ["void shade(", "envBrdf", "skyIrradiance", "skyRadiance", "uSkySh[4]", "pow(1.0 - max(dot(V, H), 0.0), 5.0)"]) {
    assert.ok(fragment.includes(term), term);
  }
  // Additive effects keep their unlit early-out.
  assert.ok(fragment.includes("if (vColor.a > 0.999)"));
});

test("every sector gets its own positive sky light from the sky dome colours", () => {
  const hues = sectors.map((sector, i) => {
    const sh = coefficients(i, sector.Atmosphere.Ambient);
    assert.equal(sh.length, 12);
    for (const value of sh) assert.ok(Number.isFinite(value));
    for (let c = 0; c < 3; c++) {
      const grad = Math.hypot(sh[3 + c], sh[6 + c], sh[9 + c]);
      assert.ok(sh[c] > 0 && sh[c] - grad > 0.3 * sh[c], `sector ${i} channel ${c} goes dark`);
    }
    return [sh[0], sh[1], sh[2]];
  });
  const id = (name) => sectors.findIndex((s) => s.Id === name);
  const helios = hues[id("slag-refinery")], aether = hues[id("aether-relay")], drift = hues[id("frontier-drift")];
  assert.ok(helios[0] > helios[2], "Helios sky light is warm");
  assert.ok(aether[2] > aether[0], "Relais Theta sky light is cool");
  assert.ok(drift[1] > drift[0], "Grenz-Drift sky light is green-teal");
  // The raw projection sees the planet in front of the band (world -Z).
  const raw = project(0);
  assert.ok(raw[9] + raw[10] + raw[11] < 0);
  const lights = createLights();
  setAmbient(lights, 0.18, 0.13, 0.11);
  applySky(lights, id("slag-refinery"));
  assert.ok(lights.SkySh[0] > lights.SkySh[2]);
});

test("destruction lights follow the CombatVfx schedule and fade out", () => {
  assert.equal(envelope(-0.1, 0.3), 0);
  assert.ok(envelope(0.02, 0.3) > 0.4 && envelope(0.02, 0.3) < 0.6);
  assert.ok(Math.abs(envelope(0.04, 0.3) - 1) < 1e-9);
  assert.ok(envelope(0.5, 0.3) < envelope(0.2, 0.3));
  const tier = 2; // command corvette: ten secondaries, then the core breach
  const r = recipe(tier);
  const pool = createPool();
  blast(pool, 100, new Vec3(0, 0, 0), new Vec3(0, 0, 1), 12, 16, tier);
  assert.equal(pool.Count, 1 + r.Secondaries + 1);
  const lights = createLights();
  setBudget(lights, 32);
  const lit = (t) => { lights.CandidateCount = 0; pushPool(pool, lights, 100 + t); return lights.CandidateCount; };
  assert.equal(lit(0.01), 1, "only the first stage burns at t = 0");
  assert.ok(lit(secondaryTime(tier, 4) + 0.01) >= 2, "secondaries light up on schedule");
  const finaleReach = 16 * r.FlashRange * 1.1;
  const hasFinale = () => Array.from({ length: lights.CandidateCount }, (_, i) => lights.CandidatePosRange[i * 4 + 3])
    .some((range) => Math.abs(range - finaleReach) < 1e-3);
  lit(r.Finale - 0.005);
  assert.ok(!hasFinale(), "the finale waits for its fuse");
  lit(r.Finale + 0.02);
  assert.ok(hasFinale(), "the finale glows on schedule");
  lit(30);
  assert.equal(pool.Count, 0, "spent lights free their slots");
});
