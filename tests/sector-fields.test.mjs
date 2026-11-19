// Sektorlandschaften (SectorFields) und Sektorlage (SectorOps) im Handelsflug.
import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import {
  AsteroidFieldModule_create as createField, Combat_oreCrust as oreCrust, AsteroidFieldModule_add as addAsteroid,
  WorldModule_create as createWorld, WorldModule_reset as resetWorld, LaserPoolModule_spawn as spawnLaser, Physics_step as step,
} from '../build/PhysicsAndPool.js';
import { Vec3Module_zero as zero } from '../build/Math3D.js';
import { SectorFields_generate as generate, SectorFields_planFor as planFor, SectorFields_hotspots as hotspots } from '../build/SectorFields.js';
import {
  SectorOps_create as createOps, SectorOps_tick as tick, SectorOps_anchor as anchor, SectorOps_complete as complete,
  SectorOps_plan as planOp, SectorOps_roster as roster, SectorOps_kindFor as kindFor, SectorOps_FirstDelay as firstDelay,
  SectorOps_NextDelay as nextDelay,
} from '../build/SectorOps.js';
import { inShippingLane as inLane } from '../build/Traffic.js';
import { Rng_$ctor_Z524259A4 as Rng } from '../build/ProceduralMeshes.js';

const distance = (a, b) => Math.hypot(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
const at = (field, i) => new Vec3(field.Data[i * 16], field.Data[i * 16 + 1], field.Data[i * 16 + 2]);
const seeded = (index, seed = 7) => { const field = createField(768); generate(field, new Rng(seed), index); return field; };

test('ore nests and operation sites lie out in the sector, clear of station, gates and shipping lanes', () => {
  sectors.forEach((sector, i) => {
    const plan = planFor(i);
    assert.equal(plan.Nests.length > 0, !['kepler-yard','aether-relay'].includes(sector.Id));
    for (const nest of plan.Nests) assert.ok(distance(nest.Center, sector.Outpost.Position) >= 450, `${sector.Id} ${nest.Name}`);
    for (const point of hotspots(i)) {
      assert.ok(distance(point, sector.Outpost.Position) >= 450, `${sector.Id} site near the station`);
      for (const gate of sector.Gates) assert.ok(distance(point, gate.Position) >= 300, `${sector.Id} site near a gate`);
      assert.equal(inLane(i, point, 40), false, `${sector.Id} site in a shipping lane`);
    }
  });
});

test('every sector has its own landscape instead of the same belt', () => {
  const shapes = new Set(sectors.map((_, i) => planFor(i).Shape.name));
  assert.ok(shapes.size >= 7, `only ${shapes.size} landscape shapes`);
  const counts = sectors.map((_, i) => seeded(i).Count);
  assert.ok(Math.max(...counts) >= 2.5 * Math.min(...counts), `rock counts too similar: ${counts}`);
  const tints = new Set(sectors.map((_, i) => planFor(i).Tint.X.toFixed(2) + planFor(i).Tint.Z.toFixed(2)));
  assert.ok(tints.size >= 6);
});

test('rocks keep station and gates free, and ore sits under a crust away from the station', () => {
  sectors.forEach((sector, i) => {
    const field = seeded(i, 11 + i);
    let ore = 0, nearStation = 0;
    for (let k = 0; k < field.Count; k++) {
      const p = at(field, k), radius = field.Data[k * 16 + 6];
      assert.ok(distance(p, sector.Outpost.Position) > 170 + radius, `${sector.Id} rock in the station approach`);
      for (const gate of sector.Gates) assert.ok(distance(p, gate.Position) > 190 + radius, `${sector.Id} rock at a gate`);
      if (field.Ore[k] > 0) {
        ore++;
        if (distance(p, sector.Outpost.Position) < 450) nearStation++;
        const hp = field.Data[k * 16 + 15];
        assert.ok(Math.abs(hp - (radius * 2.6 + oreCrust(radius))) < 0.01, `${sector.Id} ore rock without crust`);
      }
    }
    if (['kepler-yard','aether-relay'].includes(sector.Id)) assert.equal(field.Count, 0);
    else assert.ok(ore >= 8 && ore <= 90, `${sector.Id} has ${ore} ore rocks`);
    // Nur Haldens Ruh hat drei Startbrocken auf dem Weg zur Station; sonst höchstens ein Streufund.
    assert.ok(nearStation <= (sector.Id === 'outer-belt' ? 4 : 2), `${sector.Id}: ${nearStation} ore rocks near the station`);
  });
});

test('the crust takes many laser hits before crystals come free, and enemy fire never frees them', () => {
  const events = (world, name) => world.Events.filter(e => e.cases()[e.tag] === name);
  const crustWorld = () => {
    const world = createWorld(0);
    resetWorld(world, 0);
    world.Player.Velocity = zero;
    world.Player.Controls.Throttle = 0;
    world.Player.Controls.Brake = true;
    const radius = 10;
    assert.equal(addAsteroid(world.Asteroids, 0, 0, -100, 0, 0, 0, radius, 0, 1, 0, 0, 0, 0.5, 0.5, 0.5, 0), true);
    world.Asteroids.Ore[0] = 1;
    world.Asteroids.Data[15] = radius * 2.6 + oreCrust(radius);
    return world;
  };
  // Kutterlaser: 12 Schaden je Schuss.
  const shoot = (world, team) => {
    world.Events.length = 0;
    assert.equal(spawnLaser(world.Lasers, 0, 0, -80, 0, 0, -520, 0, 0, 0, 1, 12, team), true);
    step(world, 0.05);
  };
  const world = crustWorld();
  let hits = 0;
  while (world.Asteroids.Ore[0] > 0 && hits < 60) {
    shoot(world, 0);
    hits++;
    if (world.Asteroids.Ore[0] > 0) {
      const crust = events(world, 'OreCrustHit');
      assert.equal(crust.length, 1, `hit ${hits} reports the crust`);
      assert.ok(crust[0].fields[1] > 0 && crust[0].fields[1] <= 1);
    }
  }
  assert.ok(hits >= 15, `crystals after only ${hits} hits`);
  const released = events(world, 'MineralReleased');
  assert.equal(released.length, 1);
  assert.equal(released[0].fields[2], 2, 'a 10 m rock gives two crystals');

  const enemyTarget = crustWorld();
  for (let i = 0; i < 12; i++) {
    shoot(enemyTarget, 1);
    assert.equal(events(enemyTarget, 'MineralReleased').length + events(enemyTarget, 'OreCrustHit').length, 0);
  }
  assert.equal(enemyTarget.Asteroids.Ore[0], 1, 'enemy fire does not free ore');
});

test('sector operations: arrival report, offer away from the player, engagement on site, payout once', () => {
  const ops = createOps();
  const sector = sectors.findIndex(s => s.Id === 'frontier-drift');
  const station = sectors[sector].Outpost.Position;
  const arrived = tick(ops, sector, 2, station, 0.1);
  assert.equal(arrived.name, 'Arrived');
  assert.equal(tick(ops, sector, 2, station, firstDelay - 1).name, 'Quiet');
  const offered = tick(ops, sector, 2, station, 1.5);
  assert.equal(offered.name, 'Offered');
  const op = offered.fields[0];
  assert.ok(distance(op.Position, station) >= 450);
  assert.ok(op.Roster.length >= 2 && op.Reward > 0);
  assert.equal(anchor(ops, sector), undefined);
  const engaged = tick(ops, sector, 2, new Vec3(op.Position.X, op.Position.Y, op.Position.Z + 100), 0.1);
  assert.equal(engaged.name, 'Engaged');
  assert.ok(anchor(ops, sector));
  assert.equal(anchor(ops, sector + 1), undefined, 'no anchor in another sector');
  assert.equal(tick(ops, sector, 2, station, 0.1).name, 'Quiet', 'no new offer while fighting');
  assert.equal(complete(ops, sector)?.Title, op.Title);
  assert.equal(complete(ops, sector), undefined, 'paid once');
  assert.equal(ops.Timer, nextDelay);
});

test('a jump during an operation cancels it without paying', () => {
  const ops = createOps();
  tick(ops, 0, 1, new Vec3(0, 0, 0), 0.1);
  const op = tick(ops, 0, 1, new Vec3(0, 0, 0), firstDelay + 1).fields[0];
  tick(ops, 0, 1, op.Position, 0.1);
  assert.equal(complete(ops, 3), undefined);
  assert.equal(anchor(ops, 0), undefined);
  assert.equal(tick(ops, 3, 3, new Vec3(0, 0, 0), 0.1).name, 'Arrived');
});

test('danger shapes the operations: calm sectors get no bounty hunts, flotsam there is unguarded', () => {
  for (let n = 0; n < 12; n++) assert.notEqual(kindFor(1, n).name, 'Bounty');
  assert.deepEqual(new Set([0, 1, 2, 3].map(n => kindFor(3, n).name)), new Set(['Distress', 'Bounty', 'Flotsam', 'Lifeboat']));
  assert.equal(roster(kindFor(1, 1), 1).length, 0);
  for (let danger = 1; danger <= 4; danger++) assert.ok(roster(kindFor(danger, 0), danger).length >= 2);
  const station = sectors[6].Outpost.Position;
  for (let n = 0; n < 6; n++) {
    const op = planOp(6, 1, n, station);
    assert.ok(distance(op.Position, station) >= 300);
    // Rettungskapseln (Codex) stabilisiert man aus 65 m, unbewachtes Treibgut erreicht man auf 90 m.
    const expected = op.Kind.name === 'Lifeboat' ? 65 : op.Roster.length === 0 ? 90 : 320;
    assert.equal(op.TriggerRadius, expected);
  }
});

import { SectorOps_at as placeAt, SectorOps_places as places } from '../build/SectorOps.js';

test('operation texts name the place in correct German', () => {
  assert.equal(placeAt('Kestrel-Ader'), 'an der Kestrel-Ader');
  assert.equal(placeAt('Wallader'), 'an der Wallader');
  assert.equal(placeAt('Sperrzone Band 7'), 'an der Sperrzone Band 7');
  assert.equal(placeAt('Haldenkamm'), 'am Haldenkamm');
  assert.equal(placeAt('Trümmerstrom'), 'am Trümmerstrom');
  for (let i = 0; i < sectors.length; i++) {
    for (const [name] of places(i)) assert.match(placeAt(name), /^(am|an der) /);
  }
  const op = planOp(0, 1, 0, new Vec3(0, 0, 0));
  assert.match(op.Detail, /^Piraten (am|an der) /);
});
