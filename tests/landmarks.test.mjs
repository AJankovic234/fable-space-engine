import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3, QuatModule_rotateVec3 as rotate } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { CapitalFlight_position as cruiser, CapitalFlight_extent as armour, CapitalFlight_hullScale as hullScale } from '../build/CapitalFlight.js';
import { cruiser as cruiserMesh } from '../build/CapitalMeshes.js';
import { gateFrame, gateHorizon, gateRotation, sovereignPosition, sovereignRotation } from '../build/LandmarkMeshes.js';
import { hull, regalia } from '../build/SovereignMeshes.js';

function* vertices(mesh) {
  const data = mesh.Vertices;
  assert.ok(data.length > 0 && data.length / 10 <= 65535);
  for (let i = 0; i < data.length; i += 10) {
    yield new Vec3(data[i], data[i + 1], data[i + 2]);
  }
}

test('both layers of the Helios gate clear the entire cruiser armour envelope', () => {
  const helios = sectors.find(s => s.Id === 'slag-refinery');
  const gate = helios.Gates.find(g => g.TargetSectorId === 'kepler-yard');
  const rotation = gateRotation(helios, gate);
  let nearestZ = -Infinity;
  for (const mesh of [gateFrame(), gateHorizon()]) {
    for (const point of vertices(mesh)) {
      nearestZ = Math.max(nearestZ, gate.Position.Z + rotate(rotation, point).Z);
    }
  }
  assert.ok(cruiser.Z - armour.Z - nearestZ > 5, 'separating plane behind the cruiser');
});

test('exterior fittings beyond the cruiser armour also clear the gate', () => {
  const helios = sectors.find(s => s.Id === 'slag-refinery');
  const gate = helios.Gates.find(g => g.TargetSectorId === 'kepler-yard');
  const rotation = gateRotation(helios, gate);
  function triangleBoxes(mesh, transform) {
    const points = Array.from(vertices(mesh), transform);
    const boxes = [];
    for (let i = 0; i < points.length; i += 3) {
      boxes.push(['X', 'Y', 'Z'].map(axis => {
        const a = points[i][axis], b = points[i + 1][axis], c = points[i + 2][axis];
        return [Math.min(a,b,c), Math.max(a,b,c)];
      }));
    }
    return boxes;
  }
  // Bounds in gate-centred world coordinates. Only the rear fittings approach the frame.
  const frame = triangleBoxes(gateFrame(), p => rotate(rotation, p));
  const front = Math.max(...frame.map(box => box[2][1]));
  const hull = triangleBoxes(cruiserMesh(), p => new Vec3(
    cruiser.X - gate.Position.X + p.X*hullScale, cruiser.Y - gate.Position.Y + p.Y*hullScale,
    cruiser.Z - gate.Position.Z + p.Z*hullScale));
  for (const box of hull.filter(box => box[2][0] < front + 2)) {
    assert.ok(frame.every(other => other.some(([lo,hi], a) => hi + 2 < box[a][0] || box[a][1] + 2 < lo)));
  }
  // The field spins around local Z. Bound all phases against the closest hull vertex.
  const normal = rotate(rotation, new Vec3(0,0,1));
  const maxFieldZ = Math.max(...Array.from(vertices(gateHorizon()), p =>
    p.Z*normal.Z + Math.hypot(p.X,p.Y)*Math.hypot(normal.X,normal.Y)));
  assert.ok(Math.min(...hull.map(box => box[2][0])) - maxFieldZ > 2);
});

test('all gates present their aperture to the station approach', () => {
  for (const sector of sectors) {
    for (const gate of sector.Gates) {
      const front = rotate(gateRotation(sector, gate), new Vec3(0, 0, 1));
      const p = gate.Position, s = sector.Outpost.Position;
      const dx = s.X - p.X, dy = s.Y - p.Y, dz = s.Z - p.Z;
      assert.ok((front.X*dx + front.Y*dy + front.Z*dz) / Math.hypot(dx, dy, dz) > 0.8, gate.Label);
    }
  }
});

test('the kilometre-long Sovereign sits beyond the station and the Bastion gates', () => {
  const bastion = sectors.find(s => s.Id === 'morgenlicht-bastion');
  const origin = sovereignPosition(bastion);
  let minX = Infinity, maxX = -Infinity, nearestZ = -Infinity;
  for (const mesh of [hull(), regalia()]) {
    for (const point of vertices(mesh)) {
      minX = Math.min(minX, point.X);
      maxX = Math.max(maxX, point.X);
      nearestZ = Math.max(nearestZ, origin.Z + rotate(sovereignRotation, point).Z);
    }
  }
  assert.ok(maxX - minX >= 1200);
  assert.ok(nearestZ < bastion.Outpost.Position.Z - bastion.Outpost.ApproachRadius);
  for (const gate of bastion.Gates) assert.ok(nearestZ < gate.Position.Z - gate.TriggerRadius);
});
