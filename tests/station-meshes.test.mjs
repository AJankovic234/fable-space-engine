import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3, QuatModule_fromAxisAngle as turn, QuatModule_rotateVec3 as rotate } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { RelicArchive_position as archivePosition } from '../build/PilotSkills.js';
import { Collision_segmentBox as segmentBox, Collision_segmentSphere as segmentSphere } from '../build/PhysicsAndPool.js';
import { ProceduralMeshes_archiveRelic as relic, ProceduralMeshes_freightHauler as hauler } from '../build/ProceduralMeshes.js';
import { station, worksCenter, worksRadius } from '../build/StationMeshes.js';

const origin = new Vec3(0, 0, 0);
const berth = rotate(turn(new Vec3(0, 1, 0), -0.39),
  rotate(turn(new Vec3(1, 0, 0), 0.16), new Vec3(30.5, -12, 14.7)));

function meshRadius({ Vertices }, center = origin) {
  let radius = 0;
  for (let i = 0; i < Vertices.length; i += 10) {
    radius = Math.max(radius, Math.hypot(Vertices[i] - center.X, Vertices[i + 1] - center.Y, Vertices[i + 2] - center.Z));
  }
  return radius;
}
const freightRadius = meshRadius(hauler());

// Bounding spheres cover the ships at every heading, including the rotation into the berth.
function trafficRoutes(outpost, sectorIndex) {
  const archive = archivePosition(origin, sectorIndex);
  const routes = [
    ['cutter approach', origin, new Vec3(0, 0, 650), 7],
    ['freighter berth', berth, berth, freightRadius * 0.70],
    ['archive relic', archive, archive, meshRadius(relic(outpost.Kind, true))],
  ];
  for (const side of [-1, 1]) {
    routes.push(['approach buoys', new Vec3(side * 10, -5.6, outpost.DockRadius * 0.66),
      new Vec3(side * 22, -5.6, outpost.ApproachRadius * 0.91), 2]);
  }
  if (sectorIndex === 1) {
    const holding = new Vec3(-71, 21, 174);
    const waiting = new Vec3(-7, 26, 222);
    routes.push(['Helios docking run', holding, berth, freightRadius * 0.70],
      ['Helios waiting freighter', waiting, waiting, freightRadius]);
  }
  // The ellipse lies within this distance of each chord between adjacent samples.
  const segments = 48;
  const chordMargin = 76 * (1 - Math.cos(Math.PI / segments));
  const orbit = i => new Vec3(76 * Math.cos(i * 2 * Math.PI / segments), 34, 41 * Math.sin(i * 2 * Math.PI / segments) - 9);
  for (let i = 0; i < segments; i++) {
    routes.push(['freighter orbit', orbit(i), orbit(i + 1), freightRadius + chordMargin]);
  }
  return routes;
}

function midpoint(a, b) {
  return new Vec3((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
}

// The farthest vertex defines a sphere containing the whole triangle. Refine only
// where that sphere overlaps the swept hull, down to a conservative 1 mm tolerance.
function intersectsSweptHull(p, q, radius, a, b, c) {
  const x = (a.X + b.X + c.X) / 3, y = (a.Y + b.Y + c.Y) / 3, z = (a.Z + b.Z + c.Z) / 3;
  const extent = Math.max(...[a, b, c].map(v => Math.hypot(v.X - x, v.Y - y, v.Z - z)));
  const hits = r => segmentSphere(p.X, p.Y, p.Z, q.X, q.Y, q.Z, x, y, z, r) >= 0;
  if (!hits(radius + extent)) return false;
  if (hits(radius) || extent <= 0.001) return true;
  const ab = midpoint(a, b), bc = midpoint(b, c), ca = midpoint(c, a);
  return intersectsSweptHull(p, q, radius, a, ab, ca)
    || intersectsSweptHull(p, q, radius, ab, b, bc)
    || intersectsSweptHull(p, q, radius, ca, bc, c)
    || intersectsSweptHull(p, q, radius, ab, bc, ca);
}

function assertRoutesClear({ Vertices, Indices }, routes) {
  const vertex = offset => new Vec3(Vertices[offset], Vertices[offset + 1], Vertices[offset + 2]);
  for (let i = 0; i < Indices.length; i += 3) {
    const a = Indices[i] * 10, b = Indices[i + 1] * 10, c = Indices[i + 2] * 10;
    const center = [], half = [];
    for (let axis = 0; axis < 3; axis++) {
      const lo = Math.min(Vertices[a + axis], Vertices[b + axis], Vertices[c + axis]);
      const hi = Math.max(Vertices[a + axis], Vertices[b + axis], Vertices[c + axis]);
      center.push((lo + hi) * 0.5);
      half.push((hi - lo) * 0.5);
    }
    for (const [name, p, q, radius] of routes) {
      const hit = segmentBox(p.X - center[0], p.Y - center[1], p.Z - center[2],
        q.X - center[0], q.Y - center[1], q.Z - center[2],
        half[0] + radius, half[1] + radius, half[2] + radius);
      if (hit >= 0 && intersectsSweptHull(p, q, radius, vertex(a), vertex(b), vertex(c))) {
        assert.fail(`${name} reaches triangle ${i / 3} near ${center} within its ${radius.toFixed(2)} m clearance radius`);
      }
    }
  }
}

for (const [i, { Outpost }] of sectors.entries()) {
  test(`${Outpost.Name} fits its clearing and 16-bit mesh, with a lit dock and clear traffic routes`, () => {
    const mesh = station(Outpost.Kind);
    const { Vertices, Indices } = mesh;
    const count = Vertices.length / 10;
    assert.ok(Number.isInteger(count) && count > 0 && count <= 65535);
    assert.ok(Vertices.every(Number.isFinite));
    assert.ok(Indices instanceof Uint16Array && Indices.length > 0 && Indices.length % 3 === 0);
    assert.ok(Indices.every(index => index < count));
    assert.ok(meshRadius(mesh, worksCenter) <= worksRadius(Outpost.Kind), 'asteroid clearing encloses the hull');

    let left = Infinity, right = -Infinity, bottom = Infinity, top = -Infinity;
    const litQuadrants = new Set();
    for (let j = 0; j < Vertices.length; j += 10) {
      const x = Vertices[j], y = Vertices[j + 1], z = Vertices[j + 2];
      left = Math.min(left, x); right = Math.max(right, x);
      bottom = Math.min(bottom, y); top = Math.max(top, y);
      const radius = Math.hypot(x, y);
      if (Math.abs(z) < 9 && radius >= 10 && radius < 25 && Vertices[j + 9] >= 0.6) {
        litQuadrants.add((x > 0 ? 2 : 0) + (y > 0 ? 1 : 0));
      }
    }
    assert.ok(Math.hypot(right - left, top - bottom) >= 200, 'landmark span as seen on arrival');
    assert.equal(litQuadrants.size, 4, 'lit collar surrounds the docking axis');
    assertRoutesClear(mesh, trafficRoutes(Outpost, i));
  });
}
