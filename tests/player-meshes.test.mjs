import test from "node:test";
import assert from "node:assert/strict";
import {
  PlayerMeshes_models as models, PlayerMeshes_hulls as hulls, PlayerMeshes_bomber as bomber,
  PlayerMeshes_bomberHull as bomberHull, PlayerMeshes_torpedo as torpedo,
  ShipLife_warm as warm, ShipLife_strobe as strobe,
} from "../build/PlayerMeshes.js";
import {
  ShipHardpoints_playerEngineLeft as engineLeft, ShipHardpoints_playerEngineRight as engineRight,
  ShipHardpoints_playerGunLeft as gunLeft, ShipHardpoints_playerGunRight as gunRight,
  ShipHardpoints_playerObbCenter as obbCenter, ShipHardpoints_playerObbHalf as obbHalf,
  ShipHardpoints_bomberRails as rails,
  DeckCargo_bays as bays, DeckCargo_haulerMounts as haulerMounts, DeckCargo_escortMounts as escortMounts,
  DeckCargo_courierMounts as courierMounts,
} from "../build/ProceduralMeshes.js";
import { Turret_Mount as turretMount } from "../build/Turret.js";
import { ShipCatalog_hulls as catalog } from "../build/ShipCatalog.js";

const names = ["cutter", "hauler", "sparrow", "kingfisher"];

/** Vertices as [x, y, z, emissive] and triangles as vertex triples. */
const read = (mesh) => {
  const v = mesh.Vertices;
  const points = [];
  for (let i = 0; i < v.length; i += 10) points.push([v[i], v[i + 1], v[i + 2], v[i + 9]]);
  return points;
};
const player = models.map((build) => read(build()));
const bomberMesh = read(bomber());

/** Height of the topmost surface below (x, z), or -Infinity over empty space. */
const deckHeight = (points, x, z) => {
  let top = -Infinity;
  for (let i = 0; i < points.length; i += 3) {
    const [a, b, c] = [points[i], points[i + 1], points[i + 2]];
    const d = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2]);
    if (Math.abs(d) < 1e-12) continue;
    const u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / d;
    const w = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / d;
    if (u < 0 || w < 0 || u + w > 1) continue;
    top = Math.max(top, u * a[1] + w * b[1] + (1 - u - w) * c[1]);
  }
  return top;
};
const nearest = (points, p, glowing = false) => {
  let best = Infinity;
  for (const q of points) if (!glowing || q[3] > 0.5) best = Math.min(best, Math.hypot(q[0] - p.X, q[1] - p.Y, q[2] - p.Z));
  return best;
};

test("every player hull stays within one 16-bit mesh and inside the flight envelope", () => {
  for (const [i, points] of [...player, bomberMesh, read(torpedo())].entries()) {
    assert.ok(points.length > 300 && points.length <= 65535, `mesh ${i}: ${points.length} vertices`);
  }
  assert.equal(models.length, catalog.length, "one model per catalogue hull");
  assert.equal(hulls.length, models.length);
  player.forEach((points, i) => {
    const min = [0, 1, 2].map((k) => Math.min(...points.map((p) => p[k])));
    const max = [0, 1, 2].map((k) => Math.max(...points.map((p) => p[k])));
    // The collision box is shared by all hulls. Only masts and lamps may rise above it.
    assert.ok(max[0] <= obbCenter.X + obbHalf.X + 0.02 && min[0] >= obbCenter.X - obbHalf.X - 0.02, `${names[i]} width ${min[0]}..${max[0]}`);
    assert.ok(min[2] >= obbCenter.Z - obbHalf.Z - 0.06 && max[2] <= engineLeft.Z + 1e-6, `${names[i]} length ${min[2]}..${max[2]}`);
    assert.ok(min[1] >= obbCenter.Y - obbHalf.Y - 0.08 && max[1] <= obbCenter.Y + obbHalf.Y + 0.28, `${names[i]} height ${min[1]}..${max[1]}`);
    assert.ok(max[0] - min[0] > 3.6 && max[2] - min[2] > 4.3, `${names[i]} fills the envelope`);
  });
});

test("engine bells open at the flame hardpoints and glow inside", () => {
  for (const [i, points] of [...player, bomberMesh].entries()) {
    for (const engine of [engineLeft, engineRight]) {
      // A rim vertex sits on the exit plane; the glowing core lies inside the bell.
      const rim = points.filter((p) => Math.abs(p[2] - engine.Z) < 1e-6 && Math.hypot(p[0] - engine.X, p[1] - engine.Y) < 0.4);
      assert.ok(rim.length >= 12, `hull ${i}: bell rim at the exit plane`);
      const core = points.filter((p) => p[3] > 0.9 && Math.hypot(p[0] - engine.X, p[1] - engine.Y) < 0.25 && p[2] < engine.Z && p[2] > engine.Z - 0.5);
      assert.ok(core.length >= 3, `hull ${i}: glowing core`);
      assert.ok(!points.some((p) => p[2] > engine.Z + 1e-6), `hull ${i}: nothing behind the exit plane`);
    }
  }
});

test("laser muzzles sit on the gun hardpoints with a free line of fire", () => {
  for (const [i, points] of [...player, bomberMesh].entries()) {
    for (const gun of [gunLeft, gunRight]) {
      assert.ok(nearest(points, gun) < 0.11, `hull ${i}: muzzle at the hardpoint`);
      const blocked = points.some((p) => p[2] < gun.Z - 0.01 && Math.hypot(p[0] - gun.X, p[1] - gun.Y) < 0.09);
      assert.ok(!blocked, `hull ${i}: geometry in front of the muzzle`);
    }
  }
});

test("the turret flange rests on a clear deck ring on every hull", () => {
  const flange = turretMount.Y - 0.48;
  player.forEach((points, i) => {
    for (const [dx, dz] of [[0, 0], [0.45, 0], [-0.45, 0], [0, 0.45], [0, -0.45], [0.36, 0.36], [-0.36, -0.36]]) {
      const deck = deckHeight(points, turretMount.X + dx, turretMount.Z + dz);
      assert.ok(deck <= flange + 0.02 && deck >= flange - 0.01, `${names[i]}: deck ${deck} under the flange at ${dx},${dz}`);
    }
    // The rotating head sweeps a disc above the flange.
    const sweep = points.some((p) => p[1] > flange + 0.05 && Math.hypot(p[0] - turretMount.X, p[2] - turretMount.Z) < 0.5);
    assert.ok(!sweep, `${names[i]}: superstructure inside the turret ring`);
  });
});

test("upgrade sockets lie on the skin of their hull", () => {
  player.forEach((points, i) => {
    const hull = hulls[i];
    assert.equal(hull.Shield.length, 2);
    assert.equal(hull.Armour.length, 2);
    for (const [kind, sockets, tolerance] of [["shield", hull.Shield, 0.05], ["armour", hull.Armour, 0.03], ["relay", [hull.Relay], 0.03]]) {
      for (const socket of sockets) {
        const skin = deckHeight(points, socket.X, socket.Z);
        assert.ok(Math.abs(skin - socket.Y) <= tolerance, `${names[i]} ${kind}: socket ${socket.Y} above skin ${skin}`);
        // Modules stay clear of the turret ring.
        if (kind !== "relay") assert.ok(Math.hypot(socket.X - turretMount.X, socket.Z - turretMount.Z) > 0.55 + 0.1, `${names[i]} ${kind} inside the turret ring`);
      }
      assert.ok(sockets.length === 1 || sockets[0].X === -sockets[1].X, `${names[i]} ${kind} mirrored`);
    }
  });
});

test("freight places are free and supported: cutter basket, hauler cassettes, sparrow wings, kingfisher skids", () => {
  const [cutter, hauler, sparrow, kingfisher] = player;
  for (const bay of bays) {
    const floor = deckHeight(cutter, bay.X, bay.Z);
    assert.ok(Math.abs(floor - bay.Y) < 0.02, `basket floor ${floor} under bay ${bay.Z}`);
    const blocked = cutter.some((p) => Math.abs(p[0] - bay.X) < 0.3 && Math.abs(p[2] - bay.Z) < 0.26 && p[1] > bay.Y + 0.02);
    assert.ok(!blocked, `basket bay ${bay.Z} is obstructed`);
  }
  // The rear gate stays below a stacked crate so the chase camera sees the load.
  const gate = cutter.filter((p) => Math.abs(p[0] - bays[0].X) < 0.3 && p[2] > 1.1 && p[2] < 1.25);
  assert.ok(Math.max(...gate.map((p) => p[1])) < bays[0].Y + 0.2);
  for (const [name, points, mounts] of [["hauler", hauler, haulerMounts], ["sparrow", sparrow, escortMounts], ["kingfisher", kingfisher, courierMounts]]) {
    for (const mount of mounts) {
      const top = deckHeight(points, mount.X, mount.Z);
      assert.ok(top <= mount.Y + 0.03 && top >= mount.Y - 0.06, `${name}: freight at ${mount.Y} on surface ${top}`);
      assert.ok(Math.abs(mount.X) <= obbHalf.X && Math.hypot(mount.X - turretMount.X, mount.Z - turretMount.Z) > 0.85);
    }
  }
});

test("navigation lights, beacons and retro nozzles exist on the model", () => {
  [...player, bomberMesh].forEach((points, i) => {
    const hull = i < hulls.length ? hulls[i] : bomberHull;
    assert.ok(hull.PortLight.X < 0 && hull.StarboardLight.X === -hull.PortLight.X, `hull ${i}: red to port, green to starboard`);
    for (const lamp of [hull.PortLight, hull.StarboardLight, hull.Beacon]) {
      assert.ok(nearest(points, lamp, true) < 0.12, `hull ${i}: lamp geometry at ${lamp.X},${lamp.Y},${lamp.Z}`);
    }
    assert.equal(hull.Retros.length, 2);
    for (const nozzle of hull.Retros) {
      assert.ok(nearest(points, nozzle) < 0.08, `hull ${i}: retro nozzle`);
      const blocked = points.some((p) => p[2] < nozzle.Z - 0.01 && Math.hypot(p[0] - nozzle.X, p[1] - nozzle.Y) < 0.03);
      assert.ok(!blocked, `hull ${i}: retro nozzle fires into the hull`);
    }
  });
});

test("bomber cradles carry the torpedoes without intersecting them", () => {
  const body = read(torpedo());
  const radius = Math.max(...body.filter((p) => p[2] < 0.25).map((p) => Math.hypot(p[0], p[1])));
  for (const rail of rails) {
    const under = bomberMesh.filter((p) => Math.abs(p[0] - rail.X) < 0.3 && Math.abs(p[2] - rail.Z) < 1.0 && p[1] > rail.Y - radius - 0.12 && p[1] < rail.Y);
    assert.ok(under.length > 30, "cradle below the torpedo");
    // Only the saddle clamps may touch the casing.
    const inside = bomberMesh.filter((p) => Math.abs(p[2] - rail.Z) < 1.2 && Math.hypot(p[0] - rail.X, p[1] - rail.Y) < radius - 0.06);
    assert.equal(inside.length, 0, "bomber geometry inside a torpedo");
  }
});

test("nozzles heat up quickly, cool over seconds, and the beacon double-flashes", () => {
  let heat = 0;
  for (let i = 0; i < 60; i++) heat = warm(heat, 1, 1 / 60);
  assert.ok(heat > 0.95, `one second of thrust heats the bell (${heat})`);
  let cooling = heat;
  for (let i = 0; i < 60; i++) cooling = warm(cooling, 0, 1 / 60);
  assert.ok(cooling > 0.4 && cooling < heat - 0.3, `afterglow after one second (${cooling})`);
  for (let i = 0; i < 180; i++) cooling = warm(cooling, 0, 1 / 60);
  assert.equal(cooling, 0);
  assert.equal(warm(0.2, 0.5, 0), 0.2);
  assert.equal(warm(0.8, 0.5, 10), 0.5, "never cooler than the running engine");
  let flashes = 0, lit = 0;
  for (let step = 0; step < 180; step++) {
    const on = strobe(7.2 + step / 100);
    if (on && !strobe(7.2 + (step - 1) / 100)) flashes++;
    if (on) lit++;
  }
  assert.equal(flashes, 2);
  assert.ok(lit < 20, "the beacon is dark most of the cycle");
});

test("every hull is weathered and carries the struck registration mark of the courier SIN", () => {
  // Paint only: metal, glass, rubber and lamps code their class in the emissive channel.
  const stencil = [0.62, 0.61, 0.56], strike = [0.60, 0.055, 0.035];
  models.forEach((build, i) => {
    const v = build().Vertices;
    const is = (k, [r, g, b]) => Math.abs(v[k + 6] - r) < 2e-3 && Math.abs(v[k + 7] - g) < 2e-3 && Math.abs(v[k + 8] - b) < 2e-3;
    let paint = 0, luminance = 0, peak = 0, bars = 0, struck = 0;
    for (let k = 0; k < v.length; k += 10) {
      if (v[k + 9] !== 0) continue;
      paint++;
      luminance += 0.2126 * v[k + 6] + 0.7152 * v[k + 7] + 0.0722 * v[k + 8];
      peak = Math.max(peak, v[k + 6], v[k + 7], v[k + 8]);
      if (is(k, stencil)) bars++;
      if (is(k, strike)) struck++;
    }
    // Faded hazard yellow is the brightest paint on board; nothing is showroom white or candy orange.
    assert.ok(luminance / paint < 0.18, `${names[i]}: mean paint luminance ${luminance / paint}`);
    assert.ok(peak <= 0.72, `${names[i]}: brightest paint channel ${peak}`);
    // Thirteen bars of two triangles each, crossed by one deletion stroke.
    assert.ok(bars >= 13 * 6, `${names[i]}: ${bars} barcode vertices`);
    assert.ok(struck >= 6, `${names[i]}: ${struck} strike vertices`);
  });
});
