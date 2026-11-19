import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { sites, empty, depart, guidePose, scanPoint, survey, bearing, linked, triggers, threat, veilDensity, chartFee } from '../build/OuterRoutes.js';
import { Universe_sectors as sectors } from '../build/Universe.js';

const distance = (a,b) => Math.hypot(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
const offset = (p,x,y,z) => new Vec3(p.X+x,p.Y+y,p.Z+z);

test('nine exterior expeditions lead away from every gate, offer bearings in stages and pay once', () => {
  let log = empty;
  assert.equal(sites.length, sectors.length);
  for (const [i, s] of sites.entries()) {
    assert.ok(distance(s.Port,s.Origin) >= 2000 && distance(s.Port,s.Origin) <= 5000);
    for (const gate of sectors[i].Gates) assert.ok(distance(gate.Position,s.Origin)>1400);
    assert.equal(survey(i,s.Port,log)[1],0);
    assert.equal(bearing(i,s.Port,empty)[0],s.Relay);
    assert.equal(bearing(i,s.Relay,empty)[0],s.Origin);
    assert.ok(distance(s.Port,s.Archive) > 700);
    assert.equal(survey(i,scanPoint(i),{...log,Engaged:true})[1],0);
    const [next, fee] = survey(i,scanPoint(i),log);
    assert.equal(fee,s.Fee+(i===8 ? chartFee : 0));
    assert.equal(survey(i,scanPoint(i),next)[1],0);
    log = depart(next);
  }
  assert.equal(log.Finds,511);
});

test('the broken upper arc provides a cross-bearing to the lens, the lens to the prism', () => {
  assert.equal(survey(4,sites[4].Origin,empty)[1],0);
  const [ring] = survey(4,scanPoint(4),empty);
  assert.ok(linked(ring.Finds,6));
  assert.equal(bearing(6,sites[6].Port,ring)[0],sites[6].Origin);
  const [lens] = survey(6,scanPoint(6),ring);
  assert.ok(linked(lens.Finds,8));
  assert.equal(bearing(8,sites[8].Port,lens)[0],sites[8].Origin);
  const [prism] = survey(8,scanPoint(8),empty);
  const [second] = survey(6,scanPoint(6),prism);
  assert.equal(survey(4,scanPoint(4),second)[1],sites[4].Fee+chartFee);
});

test('the raider and the counterfeit beacon can be identified and bypassed for the full reward', () => {
  for (const i of [7,8]) {
    const s=sites[i], [danger]=threat(i,20);
    assert.ok(triggers(i,20,danger,empty));
    assert.equal(triggers(i,20,s.Port,empty),false);
    const safe = offset(s.Origin,-120,5,0);
    assert.equal(triggers(i,20,safe,empty),false);
    const [log, fee] = survey(i,safe,empty);
    assert.equal(fee,s.Fee);
    assert.equal(triggers(i,20,danger,log),false);
    const escaped = depart({...empty,Engaged:true,Sprung:1<<i});
    assert.equal(escaped.Engaged,false);
    assert.equal(triggers(i,20,danger,escaped),false);
    assert.equal(survey(i,safe,escaped)[1],s.Fee);
  }
  const sweep=guidePose(5,0)[0];
  assert.ok(triggers(5,0,sweep,empty));
  assert.equal(triggers(5,36,sweep,empty),false);
  for (const i of [0,1,2,3,4,6]) assert.equal(triggers(i,0,sites[i].Origin,empty),false);
});

test('traffic poses survive time jumps; the dust banks in Schleierpass have clear gaps', () => {
  for (const [i,s] of sites.entries()) {
    const first=guidePose(i,0)[0];
    const excursion = Math.max(...[20,40,60,80,100,120,140,160].map(t=>distance(first,guidePose(i,t)[0])));
    if (i!==4 && i!==8) assert.ok(excursion>400,`no excursion in sector ${i}`);
    else assert.equal(excursion,0);
    for (const time of [0,180,1e8]) {
      const [p,q]=guidePose(i,time);
      assert.ok(distance(p,s.Port)<5200);
      assert.ok(Math.abs(Math.hypot(q.X,q.Y,q.Z,q.W)-1)<1e-6);
    }
  }
  const s=sites[8], d=distance(s.Port,s.Origin);
  const at = t => offset(s.Port,(s.Origin.X-s.Port.X)*t/d,(s.Origin.Y-s.Port.Y)*t/d,(s.Origin.Z-s.Port.Z)*t/d);
  assert.ok(veilDensity(8,at(940),0)>.95);
  assert.equal(veilDensity(8,at(1400),0),0);
  assert.equal(veilDensity(3,at(940),0),0);
});
