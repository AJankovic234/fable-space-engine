// Sehenswürdigkeiten und Schürfer draußen im Sektor (SectorSights).
import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3, QuatModule_rotateVec3 as rotate } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { AsteroidFieldModule_create as createField } from '../build/PhysicsAndPool.js';
import { SectorFields_generate as generate, SectorFields_planFor as planFor } from '../build/SectorFields.js';
import {
  SectorSights_forSector as forSector, SectorSights_intensity as intensity, SectorSights_partMesh as partMesh,
  SectorSights_parts as parts, SectorSights_createMiners as createMiners, SectorSights_updateMiners as updateMiners,
  SectorSights_drill as drill,
} from '../build/SectorSights.js';
import { inShippingLane as inLane } from '../build/Traffic.js';
import { Rng_$ctor_Z524259A4 as Rng } from '../build/ProceduralMeshes.js';

const distance = (a, b) => Math.hypot(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
const world = (sight, offset) => { const r = rotate(sight.Rotation, offset); return new Vec3(sight.Position.X + r.X, sight.Position.Y + r.Y, sight.Position.Z + r.Z); };
// Grobe Ausdehnung je Bauteil (Bohrturm, Boje, Schüssel, Rumpfskelett, Plattform, Kometenkern, Kreuzerhälfte, Mast).
const extent = [50, 12, 18, 80, 30, 45, 60, 15];

test('every sector has named sights with lit beacons', () => {
  const names = new Set();
  sectors.forEach((sector, i) => {
    const sights = forSector(i);
    assert.ok(sights.length >= 1, `${sector.Id} has no sights`);
    for (const s of sights) {
      assert.ok(s.Name.length > 3 && !names.has(s.Name), s.Name);
      names.add(s.Name);
      assert.ok(s.Parts.length >= 1 && s.Beacons.length >= 1, s.Name);
    }
  });
  assert.ok(new Set(sectors.map((_, i) => forSector(i).map(s => s.Parts[0][0]).join())).size >= 7, 'sectors look alike');
});

test('sights stand clear of the station, the gates and the shipping lanes', () => {
  sectors.forEach((sector, i) => {
    for (const s of forSector(i)) {
      for (const [part, offset] of s.Parts) {
        const p = world(s, offset);
        assert.ok(distance(p, sector.Outpost.Position) > 300, `${s.Name} crowds the station`);
        for (const gate of sector.Gates) assert.ok(distance(p, gate.Position) > 220, `${s.Name} blocks a gate`);
        assert.equal(inLane(i, p, extent[part]), false, `${s.Name} stands in a shipping lane`);
      }
    }
  });
});

test('every part has a buildable mesh', () => {
  for (const part of parts) {
    const { Vertices, Indices } = partMesh(part);
    assert.ok(Vertices.length > 100 && Indices.length > 30, `part ${part}`);
    assert.ok(Vertices.every(Number.isFinite));
  }
});

test('beacons blink between a dim and a bright state, steady lights stay on', () => {
  const sight = forSector(0)[0];
  const blinking = sight.Beacons.find(b => b.Period > 0);
  const steady = sight.Beacons.find(b => b.Period === 0);
  const samples = Array.from({ length: 64 }, (_, k) => intensity(blinking, k * blinking.Period / 64));
  assert.ok(Math.min(...samples) < 0.2 && Math.max(...samples) > 0.95);
  assert.equal(intensity(steady, 3.7), intensity(steady, 12.1));
});

test('miners work at the edge of their ore nest, in front of a rock, and move on when it is gone', () => {
  sectors.forEach((sector, i) => {
    const field = createField(768);
    generate(field, new Rng(5), i);
    const miners = createMiners(i);
    assert.equal(miners.length, planFor(i).Nests.length * 2);
    updateMiners(miners, i, field);
    for (const m of miners) {
      assert.ok(m.Busy, `${sector.Id}: a miner without a rock`);
      const nest = planFor(i).Nests[m.Nest];
      assert.ok(distance(m.Rock, nest.Center) < nest.Spread * 2.2);
      const gap = distance(m.Position, m.Rock) - m.RockRadius;
      assert.ok(gap > 15 && gap < 35, `${sector.Id}: miner ${gap.toFixed(1)} m from the rock surface`);
      const [, , length] = drill(m);
      assert.ok(length > 5 && length < 30);
    }
    // Felsen des ersten Schürfers entfernen: er sucht sich einen neuen.
    if (!miners.length) return;
    const first = miners[0], old = first.Rock;
    for (let k = 0; k < field.Count; k++) {
      if (Math.hypot(field.Data[k * 16] - old.X, field.Data[k * 16 + 1] - old.Y, field.Data[k * 16 + 2] - old.Z) < 1) {
        field.Data.copyWithin(k * 16, (field.Count - 1) * 16, field.Count * 16);
        field.Count--;
        break;
      }
    }
    updateMiners(miners, i, field);
    assert.ok(distance(first.Rock, old) > 1, `${sector.Id}: miner keeps drilling a destroyed rock`);
  });
});
