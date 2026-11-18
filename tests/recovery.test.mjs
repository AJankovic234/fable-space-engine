import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import {
  PilotSkills_create as create, PilotSkills_learn as learn, PilotSkills_visit as visit,
  PilotSkills_points as points, RelicArchive_position as archivePosition
} from '../build/PilotSkills.js';
import { Recovery_scan as scan } from '../build/Recovery.js';

test('every sector archive answers a manual scan at the spherical boundary and pays once', () => {
  const pilot = create();
  for (const [sector, { Outpost }] of sectors.entries()) {
    visit(pilot, sector);
    const source = archivePosition(Outpost.Position, sector);
    const scanner = new Vec3(source.X - 210, source.Y, source.Z - 280);
    const before = points(pilot);
    const outside = new Vec3(scanner.X, scanner.Y + 1, scanner.Z);
    assert.equal(scan(pilot, sector, outside, source).tag, 0, 'outside the sphere');
    assert.equal(points(pilot), before);
    const hit = scan(pilot, sector, scanner, source);
    assert.equal(hit.tag, 2, 'new recording');
    assert.equal(hit.fields[0], 350);
    assert.equal(points(pilot), before + 1);
    assert.equal(scan(pilot, sector, source, source).tag, 1, 'already on file at zero distance');
    assert.equal(points(pilot), before + 1);
  }
  assert.equal(pilot.Relics, 63);
});

test('RT-9 doubles the active scan range even directly above or behind the ship', () => {
  const pilot = create();
  const ship = new Vec3(-30, 20, 80);
  const above = new Vec3(ship.X, ship.Y + 700, ship.Z);
  assert.equal(scan(pilot, 0, ship, above).tag, 0);
  assert.equal(learn(pilot, 0, 1), true);
  assert.equal(scan(pilot, 0, ship, new Vec3(ship.X, ship.Y, ship.Z + 700.001)).tag, 0);
  assert.equal(scan(pilot, 0, ship, above).tag, 2);
  assert.equal(scan(pilot, 0, ship, new Vec3(ship.X, ship.Y, ship.Z + 700)).tag, 1);
  assert.equal(scan(pilot, 1, ship, ship).tag, 0, 'unvisited sector');
  assert.equal(pilot.Relics, 1);
});
