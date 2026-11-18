import test from 'node:test';
import assert from 'node:assert/strict';
import {Vec3, QuatModule_identity as identity} from '../build/Math3D.js';
import {
  CapitalFlight_create as create, CapitalFlight_beginSortie as launch, CapitalFlight_standDown as retreat,
  CapitalFlight_advance as advance, CapitalFlight_position as cruiser, CapitalFlight_lockTime as lock,
  CapitalFlight_gunOffsets as guns, CapitalFlight_allyPosition as wing
} from '../build/CapitalFlight.js';
import {WorldModule_create as world, WorldModule_reset as reset, Physics_step as physics, LaserPoolModule_spawn as bolt, CapitalCombat_defend as defend} from '../build/PhysicsAndPool.js';

const ahead = new Vec3(0,0,-1);
const origin = new Vec3(cruiser.X,cruiser.Y,cruiser.Z+260);

test('a cruiser gun emits one distinct report per launched bolt and destroyed guns stay silent', () => {
  const w=world(0), s=create();
  reset(w,0);
  launch(s,1,100,100);
  w.Player.Position=origin;
  defend(w,s,4);
  assert.equal(w.Lasers.Count,1);
  assert.deepEqual(w.Events.map(event=>event.cases()[event.tag]),['CapitalGunFired']);
  defend(w,s,.1);
  s.Guns.fill(0);
  defend(w,s,8);
  assert.equal(w.Lasers.Count,1);
  assert.equal(w.Events.length,1);
});

test('only an acquired bomber torpedo harms armour, upon impact; retreat preserves damage', () => {
  const s=create();
  launch(s,1,74,31);
  advance(s,origin,new Vec3(1,0,0),false,true,5);
  assert.equal(s.Ammo,2);
  advance(s,origin,ahead,false,false,3);
  assert.equal(s.Lock,lock);
  advance(s,origin,ahead,false,true,0.01);
  assert.equal(s.Ammo,1);
  assert.equal(s.Hits,0);
  advance(s,origin,ahead,false,false,0.2);
  assert.ok(s.Flight>0 && s.Flight<1);
  advance(s,origin,ahead,false,false,2);
  assert.equal(s.Hits,1);
  retreat(s);
  advance(s,origin,ahead,true,true,9);
  assert.equal(s.Hits,1);
  launch(s,1,74,31);
  advance(s,origin,ahead,false,true,3);
  advance(s,origin,ahead,false,false,2);
  assert.equal(s.Hits,2);
});

test('the escorted bomber holds while interceptors are present, then performs both torpedo runs', () => {
  const s=create();
  launch(s,2,90,10);
  for(let i=0;i<200;i++) advance(s,origin,ahead,false,true,.1);
  assert.equal(s.Ammo,2);
  assert.equal(s.Lock,0);
  for(let i=0;i<130;i++) advance(s,origin,ahead,true,false,.1);
  assert.equal(s.Hits,2);
  assert.equal(s.Ammo,0);
});

test('swept laser hits stop on armour, strip an exposed gun and can hit the friendly bomber', () => {
  const w=world(0), s=create();
  reset(w,0);w.Customs=s;
  const shoot=(p, team, damage) => {
    bolt(w.Lasers,p.X,p.Y,p.Z+23,0,0,-500,0,0,0,1,damage,team);
    for(let i=0;i<7;i++)physics(w,.01);
  };
  shoot(cruiser,0,800);
  assert.equal(s.Hits,0);
  assert.equal(w.Lasers.Count,0);
  const g=guns[0];
  shoot(new Vec3(cruiser.X+g.X,cruiser.Y+g.Y,cruiser.Z+g.Z),0,40);
  assert.deepEqual([...s.Guns],[0,32]);
  assert.equal(s.Hits,0);
  launch(s,2,80,10);
  shoot(wing(s),1,10);
  assert.equal(s.WingHull,90);
  retreat(s);
  assert.equal(s.Guns[0],0);
});
