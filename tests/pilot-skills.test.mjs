import test from 'node:test';
import assert from 'node:assert/strict';
import { PilotSkills_create as create, PilotSkills_learn as learn, PilotSkills_points as points, PilotSkills_visit as visit, PilotSkills_scan as scan, PilotSkills_pulse as pulse, PilotSkills_tick as tick } from '../build/PilotSkills.js';
import { WorldModule_create as world, Damage_ship as hit, Damage_hitShip as piercingHit } from '../build/PhysicsAndPool.js';
import { Mission_applyPilot as apply } from '../build/Mission.js';
import { AI_createBrain as brain, AI_update as updateAI } from '../build/AI.js';
import { StoryRadio_archive as recordings, StoryRadio_atPort as greeting } from '../build/StoryRadio.js';

test('finds unlock their own recordings and Sera needs both RT-9 and contact with Theta', () => {
  const p = create();
  const beats = () => recordings(p).map(entry => entry.Call.Beat);
  assert.equal(greeting('station-helios').Beat,12);
  assert.deepEqual(beats(),[]);
  visit(p,4);
  assert.deepEqual(beats(),[]);
  assert.equal(learn(p,0,1),true);
  assert.deepEqual(beats(),[16]);
  assert.equal(scan(p,0),true);
  assert.deepEqual(beats(),[17,16]);
  for (const sector of [1,2,4]) { visit(p,sector); scan(p,sector); }
  assert.deepEqual(beats(),[17,13,14,15,16]);
  const untravelled = create();
  learn(untravelled,0,1);
  assert.deepEqual(recordings(untravelled),[]);
});

test('all nine skills can be earned once from the six sectors without repeat farming', () => {
  const p = create();
  assert.equal(points(p), 1);
  assert.equal(learn(p, 0, 2), false);
  assert.equal(learn(p, 0, 1), true);
  assert.equal(learn(p, 0, 1), false);
  assert.equal(learn(p, 0, 2), false);
  assert.equal(scan(p, 3), false);
  for (let s=0; s<6; s++) {
    visit(p,s); visit(p,s);
    assert.equal(scan(p,s), true);
    assert.equal(scan(p,s), false);
  }
  for (let branch=0; branch<3; branch++)
    for (let rank=p.Levels[branch]+1; rank<=3; rank++) assert.equal(learn(p,branch,rank), true);
  assert.equal(points(p),0);
});

test('sleeve bonuses compose with modules, trigger on shield break and rescue once in each sector', () => {
  const w=world(0), p=w.Pilot, ship=w.Player;
  p.Levels[2]=3;
  for (let i=0;i<2;i++) apply(ship,[1,0,0],p);
  assert.ok(Math.abs(ship.LaserDamage-17.28)<1e-9);
  assert.equal(ship.CruiseSpeed,66);
  assert.equal(ship.BrakeAssist,1.35);
  ship.Alive=true; ship.Shield=4; ship.Hull=100;
  hit(w,ship,5);
  assert.equal(ship.Overdrive,4);
  assert.equal(p.BypassCooldown,25);
  ship.Overdrive=0; ship.Shield=2; hit(w,ship,3);
  assert.equal(ship.Overdrive,0);
  hit(w,ship,1000);
  assert.equal(ship.Alive,true);
  assert.equal(ship.Hull,ship.MaxHull*.3);
  assert.equal(ship.Shield,ship.MaxShield);
  w.SectorIndex=1;
  hit(w,ship,1000);
  assert.equal(ship.Alive,true);
  w.SectorIndex=0;
  hit(w,ship,1000);
  assert.equal(ship.Alive,false);
});

test('ghost pulse suppresses drone fire; shield piercing only bypasses a quarter of damage', () => {
  const w=world(1), p=w.Pilot, drone=w.Enemies[0], pilotBrain=brain(0);
  drone.Alive=true; drone.Controls.Fire=true; pilotBrain.FiringBurst=true;
  assert.equal(pulse(p),false);
  p.Levels[0]=3;
  assert.equal(pulse(p),true);
  updateAI(w,[pilotBrain],1/60);
  assert.equal(drone.Controls.Fire,false);
  tick(p,8); assert.equal(p.PulseTime,0);
  assert.equal(pulse(p),false);
  tick(p,37); assert.equal(pulse(p),true);
  drone.Hull=50; drone.Shield=50;
  piercingHit(w,drone,10,true);
  assert.equal(drone.Hull,47.5);
  assert.equal(drone.Shield,42.5);
});
