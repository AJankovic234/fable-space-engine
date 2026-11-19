import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { Universe_sectors as sectors, Universe_evaluateDocking as docking } from '../build/Universe.js';
import { forSector, laserHit as hit, move } from '../build/StationCollision.js';
import {
  WorldModule_create as createWorld, WorldModule_reset as resetWorld,
  LaserPoolModule_spawn as fire, Physics_step as step
} from '../build/PhysicsAndPool.js';

const vector = (x, y, z) => new Vec3(x, y, z);
const laserHit = (hull, a, b) => hit(hull, a.X, a.Y, a.Z, b.X, b.Y, b.Z);
function at(port, x, y, z) {
  return vector(port.Position.X+x, port.Position.Y+y, port.Position.Z+z);
}

// Original body, clear gap and new outer structure, in each station's local XY plane.
const probes = {
  MiningDepot: [[65, 35], [89, 35], [-116, 105]],  // Ore bin and quarry collar
  Refinery: [[-11, 92], [24, 85], [155, 12]],      // Furnace and radiator
  FrontierBazaar: [[-56, 30], [10, 75], [-31, 115]], // Market hull and pavilion
  OrbitalYard: [[-75, 13], [114, 15], [163, 85]],  // Cruiser and gantry
  ResearchRelay: [[-75, 55], [-2, 95], [182, -80]], // Dish and solar panel
  FleetDock: [[0, 68], [61, 69], [145, 94]],      // Keep and shield tower
};
for (const [index, { Outpost: port }] of sectors.entries()) {
  test(`${port.Name}: high-speed crossings hit the works, gaps and the docking approach stay open`, () => {
    const hull = forSector(index);
    const [[x,y], [gapX,gapY], [outerX,outerY]] = probes[port.Kind.cases()[port.Kind.tag]];
    const p = at(port,x,y,120), q = at(port,x,y,-320);
    const t = laserHit(hull,p,q);
    assert.ok(t > 0 && t < 1, 'a bolt crossing the entire station hits a structure');
    const motion = move(hull, 3, p, q, vector(0,0,-440));
    assert.ok(motion && Math.hypot(motion[1].X-q.X, motion[1].Y-q.Y, motion[1].Z-q.Z) > 5,
      'the ship is stopped or deflected');
    const outer = laserHit(hull, at(port,outerX,outerY,120), at(port,outerX,outerY,-320));
    assert.ok(outer > 0 && outer < 1, 'the new outer structure stops a bolt');
    assert.equal(laserHit(hull, at(port,gapX,gapY,120), at(port,gapX,gapY,-320)), 2);
    const approach = at(port,0,0,650);
    for (const [dx,dy] of [[0,0], [16,0], [-16,0], [0,16], [0,-16]]) {
      const rear = at(port,dx,dy,-14);
      assert.equal(laserHit(hull,approach,rear), 2);
      assert.equal(move(hull,7,approach,rear,vector(0,0,-100)), undefined);
    }
    const boundary = at(port,0,0,port.DockRadius);
    const state = docking(port,boundary,false);
    assert.equal(state.cases()[state.tag], 'ReadyToDock');
  });
}

test('fixed-step player and enemy flight slides along the near face without tunnelling or losing tangential speed', () => {
  const w = createWorld(1);
  resetWorld(w,0);
  const port = sectors[0].Outpost;
  const enemy = w.Enemies[0];
  enemy.Alive = true;
  for (const [ship,x] of [[w.Player,65], [enemy,112]]) {
    ship.Position = at(port,x,30,-64);
    ship.Velocity = vector(24,0,-11000);
    ship.Controls.Throttle = 0;
  }
  step(w,1/60);
  for (const [ship,x] of [[w.Player,65], [enemy,112]]) {
    assert.ok(ship.Position.Z > port.Position.Z-102);
    assert.ok(ship.Position.X > port.Position.X+x);
    assert.equal(ship.Velocity.Z,0);
    assert.ok(ship.Velocity.X > 20);
    const contact = ship.Position.Z;
    step(w,1/60);
    assert.equal(ship.Position.Z,contact);
  }
});

test('bolts from either team stop at the first obstruction and still hit nearer ships', () => {
  for (const team of [0,1]) {
    for (const behind of [true,false]) {
      const w = createWorld(1);
      resetWorld(w,0);
      const port = sectors[0].Outpost;
      const target = team === 0 ? w.Enemies[0] : w.Player;
      target.Alive = true;
      target.Controls.Throttle = 0;
      target.Position = at(port,65,30,behind ? -180 : -55);
      const muzzle = at(port,65,30,-20), shield = target.Shield;
      fire(w.Lasers,muzzle.X,muzzle.Y,muzzle.Z,0,0,-12000,0,0,0,1,12,team);
      step(w,1/60);
      assert.equal(w.Lasers.Count,0);
      assert.equal(target.Shield,behind ? shield : shield-12);
    }
  }
});
