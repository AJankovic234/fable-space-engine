import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3, QuatModule_rotateVec3 as rotate } from '../build/Math3D.js';
import {
  Universe_sectors as sectors, Universe_gateRotation as frameRotation,
  Universe_gateRingRotation as ringRotation,
} from '../build/Universe.js';
import {
  JumpFlight_trace as trace, JumpFlight_arrival as arrival, JumpFlight_exitDistance as exitDistance,
} from '../build/JumpFlight.js';

const aperture = 0;
const frame = 1;
const radius = 3.4;
const [kestrel, helios, drift] = sectors;
const add = (a, b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
const scale = (v, n) => new Vec3(v.X * n, v.Y * n, v.Z * n);
const point = (gate, rotation, x, y, z) => add(gate.Position, rotate(rotation, new Vec3(x, y, z)));

test('fast segments through each Kestrel aperture trigger one crossing in either direction', () => {
  for (const gate of kestrel.Gates) {
    const rotation = frameRotation(kestrel, gate);
    for (const sign of [-1, 1]) {
      const from = point(gate, rotation, 0, 0, sign * 0.54);
      const to = point(gate, rotation, 0, 0, -sign * 0.62);
      const next = point(gate, rotation, 0, 0, -sign * 1.78);
      const hit = trace(kestrel, gate, 7, radius, from, to);
      assert.equal(hit[0].tag, aperture);
      assert.ok(hit[1] > 0 && hit[1] < 1);
      assert.equal(trace(kestrel, gate, 7, radius, to, next), undefined);
    }
  }
});

test('stopping short, crossing outside the frame and moving parallel to the plane cannot jump', () => {
  const gate = kestrel.Gates[0];
  const p = (x, y, z) => point(gate, frameRotation(kestrel, gate), x, y, z);
  for (const [from, to] of [
    [p(0, 0, -12), p(0, 0, -5)],
    [p(0, 0, -5), p(0, 0, -5)],
    [p(43, 0, -12), p(43, 0, 12)],
    [p(-5, 0, -0.5), p(5, 0, -0.5)],
  ]) {
    assert.equal(trace(kestrel, gate, 3.1, radius, from, to), undefined);
  }
});

test('outer struts and rotating emitter boxes report contact before crossing the center plane', () => {
  const gate = kestrel.Gates[0];
  const time = 5.1;
  const midAngle = Math.PI / 12;
  const emitterRadius = 16.1 * Math.cos(midAngle) * 0.92;
  const fixtures = [
    [frameRotation(kestrel, gate), 25, 0],
    [ringRotation(kestrel, gate, time), emitterRadius * Math.cos(midAngle), emitterRadius * Math.sin(midAngle)],
  ];
  for (const [rotation, x, y] of fixtures) {
    const hit = trace(kestrel, gate, time, radius, point(gate, rotation, x, y, -24), point(gate, rotation, x, y, 21));
    assert.equal(hit[0].tag, frame);
    assert.ok(hit[1] > 0 && hit[1] < 24 / 45);
  }
});

test('every route has a reciprocal gate whose exit grants forward clearance and allows a deliberate return', () => {
  for (const origin of sectors) {
    for (const entry of origin.Gates) {
      const target = sectors.find(sector => sector.Id === entry.TargetSectorId);
      const exit = target.Gates.find(gate => gate.TargetSectorId === origin.Id);
      assert.ok(exit, entry.Label);
      const [position, direction] = arrival(target, exit, radius, target === helios);
      const offset = add(position, scale(exit.Position, -1));
      assert.ok(Math.abs(Math.hypot(offset.X, offset.Y, offset.Z) - exitDistance) < 1e-8);
      assert.ok(offset.X * direction.X + offset.Y * direction.Y + offset.Z * direction.Z > 50);
      assert.equal(trace(target, exit, 0, radius, position, add(position, scale(direction, 4))), undefined);
      assert.equal(trace(target, exit, 0, radius, position, add(exit.Position, scale(direction, -2)))[0].tag, aperture);
    }
  }
  assert.deepEqual(kestrel.Gates.map(gate => gate.TargetSectorId), [helios.Id, drift.Id]);
});
