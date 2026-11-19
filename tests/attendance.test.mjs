import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { Vec3 } from '../build/Math3D.js';
import { Universe_sectors as ports } from '../build/Universe.js';
import { sectors, duration, watch, actor, inLane, subject, cue } from '../build/Attendance.js';
import { radius } from '../build/AttendanceMeshes.js';
import { forSector, move } from '../build/StationCollision.js';
import { models, manifests, shipPose } from '../build/Traffic.js';

const distance = (a,b) => Math.hypot(a.X-b.X,a.Y-b.Y,a.Z-b.Z);

test('one cruiser visits all eight covered ports in each shuffled round and announces its actual next stop', () => {
  assert.equal(sectors.length,8);
  assert.ok(!Array.from(sectors).includes(ports.findIndex(p => p.Id === 'schleierpass')));
  const routes = new Set();
  for (const seed of [1,2,7,25,79,173,10001,999999]) {
    for (let round=0;round<4;round++) {
      const stops=[];
      for (let i=0;i<8;i++) {
        const now=(round*8+i)*duration+20;
        const w=watch(seed,now);
        const next=watch(seed,now+duration);
        assert.equal(w.Next,next.Sector);
        assert.notEqual(w.Sector,w.Next);
        stops.push(w.Sector);
      }
      assert.deepEqual([...stops].sort(),Array.from(sectors).sort());
      routes.add(stops.join());
    }
  }
  assert.ok(routes.size>=8);
});

test('the high patrol clears berths, gates, other hulls and the asteroid field along its entire pass', () => {
  for(const index of sectors){
    const hull=forSector(index), port=ports[index];
    let prev=actor(index,0).Position;
    assert.ok(distance(prev,port.Outpost.Position)>1600);
    for(let t=0.25;t<=duration;t+=0.5){
      const p=actor(index,t).Position;
      assert.ok(inLane(index,p,radius));
      assert.equal(move(hull,radius,prev,p,new Vec3(0,0,0)),undefined);
      for(const g of port.Gates) assert.ok(distance(p,g.Position)>g.TriggerRadius+radius);
      for(const flight of manifests[index]){
        const [q]=shipPose(flight,t);
        assert.ok(distance(p,q)>radius+models[flight.Class].Radius);
      }
      prev=p;
    }
    assert.ok(distance(prev,port.Outpost.Position)>1600);
  }
});

test('dialogue matches the tracked contact, uses shipped audio and respects range, save/load and channel windows', () => {
  const seed=45, w=watch(seed,0), i=w.Sector, near=ports[i].Outpost.Position;
  let log={Seed:seed,Heard:0};
  const shifts=new Set();
  for(let round=0;round<2;round++){
    const slot=Array.from({length:8},(_,n)=>watch(seed,(round*8+n)*duration)).find(x=>x.Sector===i);
    for(const t of [26,37,47,63,75]){
      const phase={...slot,Elapsed:t};
      const line=cue(log,phase,i,near);
      assert.ok(line?.Text && existsSync(new URL('../public/assets/sfx/'+line.Clip+'.mp3',import.meta.url)));
      if(t===26) shifts.add(line.Clip);
      assert.equal(subject(t,actor(i,t),near).name,t<57?'Liner':'Pilot');
      assert.equal(cue(log,phase,(i+1)%ports.length,near),undefined);
      assert.equal(cue(log,phase,i,new Vec3(6000,6000,6000)),undefined);
      log={Seed:seed,Heard:line.Id};
      assert.equal(cue(log,phase,i,near),undefined);
    }
  }
  assert.equal(shifts.size,2);
});
