import test from "node:test";
import assert from "node:assert/strict";
import {
  EnemyMeshes_drone as drone, EnemyMeshes_scout as scout, EnemyMeshes_gunship as gunship,
  EnemyMeshes_bomber as bomber, EnemyMeshes_corvette as corvette, EnemyMeshes_hull as hull,
  EnemyMeshes_blocker as blocker,
} from "../build/EnemyMeshes.js";
import { ShipModule_createInterceptor as createEnemy } from "../build/PhysicsAndPool.js";

const builders = [drone, scout, gunship, bomber, corvette, blocker];
const names = ["drone", "scout", "gunship", "bomber", "corvette", "blocker"];
const axes = ["X", "Y", "Z"];
// Side offset of the two firing muzzles of the former models (1.2 * 1.6 * role scale). A wider
// spacing would let centred salvos straddle the player, so the new models keep it.
const formerGunSpacing = [1.632, 1.824, 2.496, 2.88, 4.032];

const meshes = builders.map((build) => {
  const mesh = build();
  const v = mesh.Vertices;
  const min = [Infinity, Infinity, Infinity];
  const max = [-Infinity, -Infinity, -Infinity];
  for (let i = 0; i < v.length; i += 10) {
    for (let k = 0; k < 3; k++) {
      min[k] = Math.min(min[k], v[i + k]);
      max[k] = Math.max(max[k], v[i + k]);
    }
  }
  const nearest = (p) => {
    let best = Infinity;
    for (let i = 0; i < v.length; i += 10) best = Math.min(best, Math.hypot(v[i] - p.X, v[i + 1] - p.Y, v[i + 2] - p.Z));
    return best;
  };
  return { mesh, count: v.length / 10, min, max, size: [0, 1, 2].map((k) => max[k] - min[k]), nearest };
});

test("enemy models stay within 16-bit indices and contain only finite vertex data", () => {
  meshes.forEach(({ mesh, count }, role) => {
    assert.ok(count > 500 && count <= 65535, `${names[role]}: ${count} vertices`);
    assert.equal(mesh.Indices.length, count);
    assert.ok(mesh.Vertices.every(Number.isFinite), names[role]);
  });
});

test("the original roles increase in size and the blocker has a compact, wide silhouette", () => {
  const diagonal = meshes.slice(0, 5).map(({ size }) => Math.hypot(...size));
  for (let role = 1; role < diagonal.length; role++) {
    assert.ok(diagonal[role] > diagonal[role - 1], `${names[role]} ${diagonal[role]} <= ${names[role - 1]}`);
  }
  // Formerly about 4-10 m; every model now spans at least 6 m and the drone stays the compact one.
  assert.ok(Math.max(...meshes[0].size) >= 5.5 && Math.max(...meshes[0].size) < 7.0);
  for (const { size } of meshes.slice(1)) assert.ok(Math.max(...size) >= 8.5);
  const corvetteLength = meshes[4].size[2];
  assert.ok(corvetteLength >= 22 && corvetteLength <= 28, `corvette length ${corvetteLength}`);
  // The needle scout is the longest ship relative to its span, the gunship the widest.
  assert.ok(meshes[1].size[2] / meshes[1].size[0] > 1.5);
  assert.ok(meshes[2].size[0] > meshes[2].size[2]);
  const [span, , length] = meshes[5].size;
  assert.ok(span > length * 1.1 && span < 11, "the blocker has a wider silhouette than it is long");
});

test("colliders hug the visible hull and the broad-phase sphere encloses them", () => {
  meshes.forEach(({ min, max, size }, role) => {
    const h = hull(role);
    axes.forEach((axis, k) => {
      const low = h.ObbCenter[axis] - h.ObbHalf[axis];
      const high = h.ObbCenter[axis] + h.ObbHalf[axis];
      assert.ok(low >= min[k] - 0.06 && high <= max[k] + 0.06, `${names[role]} ${axis} box ${low}..${high} outside model`);
      // Masts, fins and keels may stick out vertically; length and span must be covered.
      const cover = (high - low) / size[k];
      assert.ok(cover >= (axis === "Y" ? 0.45 : 0.7), `${names[role]} ${axis} covers only ${cover}`);
    });
    for (const sx of [-1, 1]) for (const sy of [-1, 1]) for (const sz of [-1, 1]) {
      const corner = Math.hypot(h.ObbCenter.X + sx * h.ObbHalf.X, h.ObbCenter.Y + sy * h.ObbHalf.Y, h.ObbCenter.Z + sz * h.ObbHalf.Z);
      assert.ok(corner <= h.Radius + 1e-9, `${names[role]} corner outside radius`);
    }
  });
});

test("muzzles keep the former spacing and sit on modelled barrels; engines and lights sit on the hull", () => {
  meshes.forEach(({ nearest }, role) => {
    const h = hull(role);
    if (role < formerGunSpacing.length) assert.ok(Math.abs(h.GunRight.X - formerGunSpacing[role]) < 0.01, `${names[role]} gun spacing ${h.GunRight.X}`);
    assert.equal(h.GunLeft.X, -h.GunRight.X);
    assert.equal(h.GunLeft.Y, 0);
    assert.equal(h.GunRight.Y, 0);
    assert.equal(h.GunLeft.Z, h.GunRight.Z);
    assert.ok(h.GunRight.Z < h.ObbCenter.Z - h.ObbHalf.Z * 0.4, `${names[role]} muzzles not forward`);
    for (const gun of [h.GunLeft, h.GunRight, ...h.Batteries]) assert.ok(nearest(gun) < 0.35, `${names[role]} muzzle off barrel`);
    assert.ok(h.Engines.length >= 1 && h.MuzzleSize > 0);
    for (const [nozzle, radius] of h.Engines) {
      assert.ok(radius > 0 && nearest(nozzle) <= radius, `${names[role]} nozzle off model`);
      assert.ok(nozzle.Z > h.ObbCenter.Z + h.ObbHalf.Z * 0.4, `${names[role]} nozzle not aft`);
    }
    for (const light of [h.Beacon, ...h.Strobes]) assert.ok(nearest(light) < 0.3, `${names[role]} light floats`);
  });
  // The commander is the only role with extra batteries and the most engines.
  assert.equal(hull(4).Batteries.length, 4);
  assert.ok(hull(4).Engines.length > Math.max(...[0, 1, 2, 3].map((role) => hull(role).Engines.length)));
});

test("a fresh enemy slot starts with the drone collider until its role is configured", () => {
  const fresh = createEnemy();
  const h = hull(0);
  assert.equal(fresh.Radius, h.Radius);
  for (const name of ["ObbCenter", "ObbHalf", "GunLeft", "GunRight"]) {
    for (const axis of axes) assert.equal(fresh[name][axis], h[name][axis], `${name}.${axis}`);
  }
});

test("drones and the customs blocker wear dull fleet paint and keep their lamps", () => {
  builders.forEach((build, r) => {
    const v = build().Vertices;
    let paint = 0, luminance = 0, peak = 0, lit = 0;
    for (let i = 0; i < v.length; i += 10) {
      if (v[i + 9] > 0.5) lit++;
      if (v[i + 9] !== 0) continue;
      paint++;
      luminance += 0.2126 * v[i + 6] + 0.7152 * v[i + 7] + 0.0722 * v[i + 8];
      peak = Math.max(peak, v[i + 6], v[i + 7], v[i + 8]);
    }
    assert.ok(luminance / paint < 0.32, `${names[r]}: mean paint luminance ${luminance / paint}`);
    assert.ok(peak <= 0.6, `${names[r]}: brightest paint channel ${peak}`);
    assert.ok(lit > 0, `${names[r]}: no lamps`);
  });
});
