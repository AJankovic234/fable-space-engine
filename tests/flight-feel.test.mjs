import test from "node:test";
import assert from "node:assert/strict";
import {
  CameraModule_create as createCamera, CameraModule_snap as snap, CameraModule_update as update,
  CameraModule_updateMatrices as updateMatrices, CameraModule_feel as feel,
  CameraModule_shakeRotation as shakeRotation, CameraModule_viewRotation as viewRotation,
  CameraModule_chaseDistanceFactor as distanceFactor, CameraModule_toggleMode as toggleMode,
} from "../build/Camera.js";
import { indicator, nextByDistance } from "../build/CombatFeedback.js";
import {
  HudModule_create as createHud, HudModule_damageFrom as damageFrom, HudModule_update as updateHud,
  HudModule_targetNearest as targetNearest, HudModule_confirmHit as confirmHit, HudModule_confirmKill as confirmKill,
} from "../build/HUD.js";
import {
  WorldModule_create as createWorld, WorldModule_reset as resetWorld, ShipModule_reset as resetShip,
  LaserPoolModule_spawn as spawnBolt, Physics_step as step,
} from "../build/PhysicsAndPool.js";
import { Vec3, Vec3Module_zero as zero, QuatModule_identity as identity, QuatModule_fromAxisAngle as axisAngle } from "../build/Math3D.js";

const aspect = 16 / 9;
const eventName = (event) => event.cases()[event.tag];

/** Projects a world point with the camera's matrices; returns NDC [x, y] and clip w. */
function project(camera, p) {
  const data = (mat) => mat.Data ?? mat.fields?.[0] ?? mat;
  const v = data(camera.View), m = data(camera.Projection);
  const ex = v[0] * p.X + v[4] * p.Y + v[8] * p.Z + v[12];
  const ey = v[1] * p.X + v[5] * p.Y + v[9] * p.Z + v[13];
  const ez = v[2] * p.X + v[6] * p.Y + v[10] * p.Z + v[14];
  const ew = v[3] * p.X + v[7] * p.Y + v[11] * p.Z + v[15];
  const cx = m[0] * ex + m[4] * ey + m[8] * ez + m[12] * ew;
  const cy = m[1] * ex + m[5] * ey + m[9] * ez + m[13] * ew;
  const cw = m[3] * ex + m[7] * ey + m[11] * ez + m[15] * ew;
  return { x: cx / cw, y: cy / cw, w: cw };
}

function framedCamera(zoom = 1) {
  const camera = createCamera();
  camera.Zoom = zoom;
  snap(camera, zero, identity);
  updateMatrices(camera, aspect);
  return camera;
}

test("the chase camera frames the ship low and small, keeping the sight line just above centre", () => {
  const camera = framedCamera();
  const ship = project(camera, zero);
  const sight = project(camera, new Vec3(0, 0, -800));
  // Schiff im unteren Drittel (ueber dem Radar), Fadenkreuz knapp ueber der Mitte
  assert.ok(ship.y < -0.36 && ship.y > -0.5, `ship NDC y ${ship.y}`);
  assert.ok(Math.abs(ship.x) < 1e-9);
  assert.ok(sight.y > 0.0 && sight.y < 0.2, `sight NDC y ${sight.y}`);
  // Mehr Abstand als frueher (10,6 m): das Schiff wirkt rund ein Viertel kleiner
  const distance = Math.hypot(camera.Position.X, camera.Position.Y, camera.Position.Z);
  assert.ok(distance > 14 && distance < 15.5, `distance ${distance}`);
});

test("the distance keys scale the chase distance without moving the ship on screen", () => {
  const base = project(framedCamera(1), zero).y;
  for (const zoom of [0.6, 1.4, 2.2]) {
    const camera = framedCamera(zoom);
    assert.ok(Math.abs(project(camera, zero).y - base) < 1e-5, `zoom ${zoom}: ${project(camera, zero).y} vs ${base}`);
    const distance = Math.hypot(camera.Position.X, camera.Position.Y, camera.Position.Z);
    assert.ok(Math.abs(distance - zoom * Math.hypot(4.4, 14)) < 1e-9, `zoom ${zoom}: ${distance}`);
  }
});

test("speed feel lags behind the real speed and widens distance and view only slightly", () => {
  const camera = createCamera();
  snap(camera, zero, identity);
  const fov0 = camera.Fov;
  feel(camera, 1, 0, 0.1);
  assert.ok(camera.SpeedFeel > 0.1 && camera.SpeedFeel < 0.3, `after 0.1 s: ${camera.SpeedFeel}`);
  for (let i = 0; i < 300; i++) { feel(camera, 1.8, 0, 1 / 60); update(camera, zero, identity, 0, 1 / 60); }
  assert.ok(camera.SpeedFeel > 0.99 && camera.SpeedFeel <= 1, `after 5 s: ${camera.SpeedFeel}`);
  const degrees = (camera.Fov - fov0) * 180 / Math.PI;
  assert.ok(degrees > 2.5 && degrees < 3.5, `fov +${degrees} deg`);
  assert.ok(Math.abs(distanceFactor(camera, 0) - 1.08) < 1e-3);
  // Boost bleibt der groessere Effekt und haeuft sich mit dem Tempo nur maessig
  assert.ok(Math.abs(distanceFactor(camera, 1) - 1.25 * 1.08) < 1e-3);
  // Frame-rate unabhaengig: 2 x 0.05 s == 1 x 0.1 s
  const a = createCamera(), b = createCamera();
  feel(a, 1, 0.5, 0.05); feel(a, 1, 0.5, 0.05); feel(b, 1, 0.5, 0.1);
  assert.ok(Math.abs(a.SpeedFeel - b.SpeedFeel) < 1e-12 && Math.abs(a.Bank - b.Bank) < 1e-12);
});

test("the chase camera banks gently into yaw turns, the cockpit stays level", () => {
  const camera = createCamera();
  for (let i = 0; i < 120; i++) feel(camera, 0, 1, 1 / 60);
  assert.ok(camera.Bank > 0.065 && camera.Bank <= 0.07, `left turn bank ${camera.Bank}`);
  for (let i = 0; i < 120; i++) feel(camera, 0, -3, 1 / 60);
  assert.ok(camera.Bank < -0.065 && camera.Bank >= -0.07, `clamped right turn bank ${camera.Bank}`);
  toggleMode(camera);
  for (let i = 0; i < 240; i++) feel(camera, 0, 1, 1 / 60);
  assert.ok(Math.abs(camera.Bank) < 1e-3, `cockpit bank ${camera.Bank}`);
  // Snap (Start, Heckblick) beginnt ohne Neigung und ohne Wackeln
  camera.Bank = 0.05;
  camera.Jolt = axisAngle(new Vec3(1, 0, 0), 0.01);
  snap(camera, zero, identity);
  assert.equal(camera.Bank, 0);
  assert.equal(camera.Jolt.W, 1);
});

test("camera shake is a bounded rotation that only affects the rendered view", () => {
  const still = shakeRotation(0, 1.23);
  assert.deepEqual([still.X, still.Y, still.Z, still.W], [0, 0, 0, 1]);
  let maxAngle = 0;
  for (let t = 0; t < 2; t += 0.003) {
    const q = shakeRotation(50, t);
    maxAngle = Math.max(maxAngle, 2 * Math.acos(Math.min(1, Math.abs(q.W))));
  }
  // Je Achse hoechstens 2 * 0.012 rad; zusammen deutlich unter 3 Grad
  assert.ok(maxAngle > 0.01 && maxAngle < 0.05, `max shake ${maxAngle}`);
  const a = shakeRotation(1, 0.1), b = shakeRotation(1, 0.13);
  assert.notDeepEqual([a.X, a.Y, a.Z], [b.X, b.Y, b.Z]);

  const camera = createCamera();
  snap(camera, zero, identity);
  const plain = viewRotation(camera);
  assert.deepEqual([plain.X, plain.Y, plain.Z, plain.W], [camera.Rotation.X, camera.Rotation.Y, camera.Rotation.Z, camera.Rotation.W]);
  camera.Jolt = shakeRotation(1, 0.37);
  camera.Bank = 0.05;
  const before = camera.Rotation;
  update(camera, zero, identity, 0, 1 / 60);
  // Die geglaettete Rotation kennt weder Wackeln noch Neigung
  assert.ok(Math.abs(camera.Rotation.X - before.X) < 1e-12 && Math.abs(camera.Rotation.W - before.W) < 1e-12);
  const shaken = viewRotation(camera);
  assert.ok(Math.abs(shaken.X - camera.Rotation.X) + Math.abs(shaken.Z - camera.Rotation.Z) > 1e-3);
});

test("damage indicators point toward the source in screen space", () => {
  const angle = (x, y, z) => indicator(x, y, z)[0];
  const weight = (x, y, z) => indicator(x, y, z)[1];
  const near = (a, b) => Math.abs(a - b) < 1e-9;
  assert.ok(near(angle(1, 0, 0), 0), "right");
  assert.ok(near(angle(0, 1, 0), -Math.PI / 2), "above = screen up");
  assert.ok(near(angle(0, -1, 0), Math.PI / 2), "below = screen down");
  assert.ok(near(Math.abs(angle(-1, 0, 0)), Math.PI), "left");
  assert.ok(near(angle(0, 0, 1), Math.PI / 2), "straight behind points down like the edge arrows");
  assert.ok(near(Math.abs(angle(-0.5, 0, 0.86)), Math.PI), "behind-left reads as left");
  assert.equal(weight(0, 0, 1), 1);
  assert.equal(weight(1, 0, 0), 1);
  assert.equal(weight(0, 0, -1), 0.25, "straight ahead is already on screen");
  const ahead = weight(0.2, 0, -1);
  assert.ok(ahead > 0.25 && ahead < 1, `slightly off-axis ahead ${ahead}`);
});

function fleet(positions) {
  const world = createWorld(positions.length);
  resetWorld(world, 0);
  positions.forEach((p, i) => {
    if (p) resetShip(world.Enemies[i], new Vec3(...p), identity);
    else world.Enemies[i].Alive = false;
  });
  return world.Enemies;
}

test("N steps through targets from nearest to farthest and wraps around", () => {
  const enemies = fleet([[0, 0, -150], [30, 0, -60], null, [-40, 10, -100], [0, 0, 150]]);
  const order = [];
  let current = -1;
  for (let i = 0; i < 5; i++) { current = nextByDistance(enemies, zero, current); order.push(current); }
  assert.deepEqual(order, [1, 3, 0, 4, 1]);
  // Ein totes aktuelles Ziel faellt auf das naechstgelegene zurueck
  enemies[3].Alive = false;
  assert.equal(nextByDistance(enemies, zero, 3), 1);
  // Gleiche Entfernung: der Index entscheidet, ohne Schleife
  const twins = fleet([[10, 0, 0], [-10, 0, 0], [0, 50, 0]]);
  assert.deepEqual([nextByDistance(twins, zero, -1), nextByDistance(twins, zero, 0), nextByDistance(twins, zero, 1)], [0, 1, 2]);
  assert.equal(nextByDistance(fleet([null, null]), zero, -1), -1);
});

test("the HUD keeps hit directions, refreshes repeated hits and confirms hits and kills", () => {
  const hud = createHud({ getContext: () => ({}) });
  const dirs = hud.DamageDirs;
  damageFrom(hud, new Vec3(1, 0, 0));
  assert.deepEqual(Array.from(dirs.slice(0, 4)), [1, 0, 0, 1.4]);
  updateHud(hud, 0.5);
  damageFrom(hud, new Vec3(0.995, 0.0998, 0));
  assert.equal(dirs[3], 1.4, "nearly the same direction refreshes its slot");
  assert.equal(dirs.filter((_, i) => i % 4 === 3 && dirs[i] > 0).length, 1);
  for (const d of [[0, 1, 0], [0, 0, 1], [-1, 0, 0]]) { updateHud(hud, 0.1); damageFrom(hud, new Vec3(...d)); }
  updateHud(hud, 0.1);
  damageFrom(hud, new Vec3(0, -1, 0));
  // Der aelteste Platz (Treffer von rechts) wurde ersetzt
  const slots = [0, 1, 2, 3].map(s => Array.from(dirs.slice(s * 4, s * 4 + 3)));
  assert.ok(!slots.some(([x]) => x === 1), JSON.stringify(slots));
  updateHud(hud, 2);
  assert.ok(dirs.every((v, i) => i % 4 !== 3 || v === 0), "indicators expire");

  confirmHit(hud, true);
  assert.ok(hud.HitMarker > 0 && hud.HitShield);
  confirmKill(hud);
  assert.ok(hud.KillMarker > 0 && hud.HitMarker === 0);
  updateHud(hud, 1);
  assert.equal(hud.KillMarker, 0);

  const enemies = fleet([[0, 0, -150], [30, 0, -60]]);
  hud.Target = 0; hud.ManualLock = false;
  assert.equal(targetNearest(hud, enemies, zero), true);
  assert.deepEqual([hud.Target, hud.ManualLock], [1, true], "an auto target is replaced by the nearest");
  targetNearest(hud, enemies, zero);
  assert.equal(hud.Target, 0, "a second press steps outward");
});

test("an enemy bolt that hits the player reports where it came from", () => {
  const world = createWorld(1);
  resetWorld(world, 0);
  resetShip(world.Player, zero, identity);
  world.Player.Velocity = new Vec3(0, 0, -20);
  const source = new Vec3(-12, 3, 20);
  const length = Math.hypot(source.X, source.Y, source.Z);
  const d = [source.X / length, source.Y / length, source.Z / length];
  spawnBolt(world.Lasers, source.X, source.Y, source.Z, -d[0] * 520, -d[1] * 520, -d[2] * 520, 0, 0, 0, 1, 6, 1);
  for (let i = 0; i < 10 && !world.Events.some(e => eventName(e) === "ShipHit"); i++) step(world, 1 / 60);
  const names = world.Events.map(eventName);
  assert.ok(names.includes("ShipHit"), names.join());
  const from = world.Events.find(e => eventName(e) === "PlayerHitFrom").fields[0];
  assert.ok(Math.abs(Math.hypot(from.X, from.Y, from.Z) - 1) < 1e-9);
  // Relativ zur eigenen Fahrt: die Quelle liegt hinten links, leicht oben
  assert.ok(from.X < -0.3 && from.Z > 0.6 && from.Y > 0, JSON.stringify(from));
});
