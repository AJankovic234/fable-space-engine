import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { forSector, laserHit } from '../build/StationCollision.js';
import { inShippingLane } from '../build/Traffic.js';
import { sites, center, terminal, status, valid, collect, deliver } from '../build/PortWork.js';
import { SectorOps_create as create, SectorOps_tick as tick, SectorOps_snapshot as snapshot,
  SectorOps_restore as restore, SectorOps_anchor as anchor, SectorOps_complete as complete } from '../build/SectorOps.js';

test('each port has reachable working machinery outside the berth and NPC lanes', () => {
  assert.equal(new Set(sites.map(s => s.Name)).size, sectors.length);
  for (const [i, site] of sites.entries()) {
    const c = center(i), t = terminal(i), hull = forSector(i);
    assert.equal(inShippingLane(i,c,85), false, site.Name);
    assert.ok(laserHit(hull,t.X,t.Y,t.Z,t.X,t.Y,t.Z+80) > 1);
    const m = site.Modules[0].Center;
    assert.ok(laserHit(hull,c.X+m.X,c.Y+m.Y,c.Z+m.Z+45,c.X+m.X,c.Y+m.Y,c.Z+m.Z-45) <= 1);
  }
});

test('packets from different ports survive together and each clerk pays only their own fee once', () => {
  let mask = collect(collect(0,0),8);
  assert.ok(valid(mask));
  assert.deepEqual(deliver(mask,3),[mask,0]);
  const [paid, fee] = deliver(mask,0);
  assert.equal(fee,180);
  assert.equal(status(paid,8),1);
  assert.equal(status(paid,0),2);
  assert.deepEqual(deliver(collect(paid,0),0),[paid,0]);
  assert.ok(!valid(3) && !valid(1 << (sites.length*2)));
});

test('expired signals rotate at a dock or gate and can send a real wing into open space', () => {
  const i=2, sector=sectors[i];
  for (const p of [sector.Outpost.Position,sector.Gates[0].Position,terminal(i)]) {
    const ops=create();
    restore(ops,i,{Ticket:1,Site:0,Active:false,Inbound:false,Remaining:1});
    assert.equal(tick(ops,i,2,p,2).name,'Offered');
    assert.notEqual(ops.Current.Kind.name,'Bounty');
  }
  const p=new Vec3(1400,340,600), ops=create();
  restore(ops,i,{Ticket:1,Site:0,Active:false,Inbound:false,Remaining:1});
  assert.equal(tick(ops,i,2,p,2).name,'Engaged');
  assert.ok(ops.Inbound && ops.Current.Roster.length >= 2);
  assert.equal(anchor(ops,i),undefined);
  const other=create();
  restore(other,i,snapshot(ops,i));
  assert.equal(other.Current.Title,ops.Current.Title);
  assert.equal(complete(other,i).Reward,250);
  assert.equal(complete(other,i),undefined);
});

test('a lifeboat pays the rescue service fee without combat, cargo space or mining', () => {
  const ops=create(), i=0;
  restore(ops,i,{Ticket:2,Site:1,Active:false,Inbound:false,Remaining:65});
  assert.equal(ops.Current.Kind.name,'Lifeboat');
  const p=ops.Current.Position;
  assert.equal(tick(ops,i,1,new Vec3(p.X+66,p.Y,p.Z),0.1).name,'Quiet');
  assert.equal(tick(ops,i,1,p,0.1).name,'Engaged');
  assert.equal(ops.Current.Roster.length,0);
  assert.equal(complete(ops,i).Reward,150);
});
