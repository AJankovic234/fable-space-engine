import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { rock, variant } from '../build/AsteroidMeshes.js';
import { routes, pose, capacity } from '../build/Traffic.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { forSector, move } from '../build/StationCollision.js';
import { CapitalFlight_position as cruiser, CapitalFlight_plates as plates } from '../build/CapitalFlight.js';
import { Vec3, QuatModule_rotateVec3 as rotate } from '../build/Math3D.js';

test('all saved variants resolve to two stable silhouettes within the collision and mesh budgets', () => {
  assert.deepEqual([0,1,2,3].flatMap(v => [variant(v, 0.25), variant(v, -0.25)]), [0,4,1,5,2,6,3,7]);
  for (const sector of sectors) {
    const shapes = new Set();
    for (let i = 0; i < 8; i++) {
      const { Vertices, Indices } = rock(sector.Outpost.Kind, i);
      assert.ok(Indices.length <= 2400); // Previous field averaged 800 triangles per visible rock.
      const shape = createHash('sha256'), edges = new Map();
      let radius = 0;
      for (let t = 0; t < Indices.length; t += 3) {
        const triangle = [];
        for (let j = 0; j < 3; j++) {
          const at = Indices[t+j]*10;
          const xyz = Vertices.subarray(at,at+3);
          radius = Math.max(radius, Math.hypot(...xyz));
          assert.ok(Math.abs(Math.hypot(...Vertices.subarray(at+3,at+6))-1) < 0.0001);
          shape.update(xyz);
          triangle.push(xyz.join(','));
        }
        for (let j = 0; j < 3; j++) {
          const a = triangle[j], b = triangle[(j+1)%3];
          const key = a < b ? a+'|'+b : b+'|'+a;
          edges.set(key,(edges.get(key) ?? 0) + (a < b ? 1 : -1));
        }
      }
      assert.ok(radius > 0.999 && radius <= 1);
      assert.ok([...edges.values()].every(n => n === 0), 'every crater rim and cap shares oppositely wound edges');
      shapes.add(shape.digest('hex'));
    }
    assert.equal(shapes.size,8,sector.Name);
  }
});

test('civilian timetables loop on the saved clock, face the direction of travel and clear the flight corridor', () => {
  const forward = new Vec3(0,0,-1);
  for (const [index,sector] of sectors.entries()) {
    const lanes = routes(sector), port = sector.Outpost.Position;
    assert.equal(lanes.length,sector.Gates.length+1);
    assert.ok(lanes.length <= capacity);
    for (const lane of lanes) {
      const [initial] = pose(lane,0);
      const [lap] = pose(lane,lane.Period);
      assert.ok(Math.hypot(initial.X-lap.X,initial.Y-lap.Y,initial.Z-lap.Z) < 0.00001);
      let prev = initial;
      for (let n = 1; n <= 240; n++) {
        const t = n*lane.Period/240;
        const [p,q] = pose(lane,t), [next] = pose(lane,t+0.0001);
        const heading = rotate(q,forward);
        const dx = next.X-p.X, dy = next.Y-p.Y, dz = next.Z-p.Z;
        assert.ok((dx*heading.X + dy*heading.Y + dz*heading.Z)/Math.hypot(dx,dy,dz) > 0.999);
        assert.equal(move(forSector(index),7.5,prev,p,forward),undefined);
        if (p.Z >= port.Z)
          assert.ok(Math.hypot(p.X-port.X,p.Y-port.Y) > 35);
        if (sector.Id === 'slag-refinery') {
          for (const [c,h] of plates) {
            const gap = axis => Math.max(0,Math.abs(p[axis]-cruiser[axis]-c[axis])-h[axis]);
            assert.ok(Math.hypot(gap('X'),gap('Y'),gap('Z')) > 9);
          }
        }
        prev = p;
      }
    }
  }
});
