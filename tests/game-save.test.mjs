import test from 'node:test';
import assert from 'node:assert/strict';
import { GameSave_capture as capture, GameSave_decode as decode, GameSave_restore as restore } from '../build/GameSave.js';
import { WorldModule_create as createWorld, WorldModule_reset as resetWorld, ShipModule_reset as resetShip, LaserPoolModule_spawn as laser, Physics_step as step } from '../build/PhysicsAndPool.js';
import { AI_createBrain as createBrain, AI_reset as resetBrain } from '../build/AI.js';
import { EnemyProfiles_configure as configure } from '../build/EnemyProfiles.js';
import { Mission_resetPlayer as resetPlayer, Mission_applyUpgrade as upgrade } from '../build/Mission.js';
import { Economy_create as wallet } from '../build/Economy.js';
import { TradeSystem_createCargo as cargo, TradeSystem_createUniverseMarkets as markets, TradeSystem_buyGood as buy, TradeSystem_buyPrice as ask, TradeSystem_sellPrice as bid } from '../build/TradeSystem.js';
import { Turret_create as turret } from '../build/Turret.js';
import { Salvage_create as pickups, Salvage_spawn as spawn } from '../build/Salvage.js';
import { Vec3, QuatModule_identity as identity } from '../build/Math3D.js';
import { Rng__NextFloat as random } from '../build/ProceduralMeshes.js';
import { Blockade_create as newBlockade } from '../build/Blockade.js';
import { CapitalFlight_create as newSortie, CapitalFlight_position as cruiser } from '../build/CapitalFlight.js';

// The same typed-array conversion used by GameSave.encode's emitted JSON serializer.
const json = data => JSON.stringify(data, (_, value) => ArrayBuffer.isView(value) ? Array.from(value) : value);
const settings = { Camera: 1, Muted: true, InvertPitch: true, Sensitivity: .0018 };
const run = () => ({Trading:true,SectorId:'slag-refinery',Wave:0,Phase:1,PhaseTimer:0,Spawned:2,SpawnTimer:8,Patrol:new Int32Array([0,1]),Score:1200,Kills:7,Profit:123.5,Trips:3,Docked:false,Completed:false,Combo:2,ComboTimer:3});
function rig() {
  const w=createWorld(16);resetPlayer(w.Player);resetWorld(w,12);
  return {w,brains:Array.from({length:16},(_,i)=>createBrain(i)),wallet:wallet(),cargo:cargo(24),markets:markets(),levels:new Int32Array(3),turret:turret(),pickups:pickups()};
}
function snapshot(r, state=run()) { return capture(state,settings,1794940000000,r.w,r.brains,r.wallet,r.cargo,r.markets,r.levels,r.turret,r.pickups); }
function fixture() {
  const r=rig();upgrade(r.w.Player,r.levels,0);upgrade(r.w.Player,r.levels,1);upgrade(r.w.Player,r.levels,2);
  r.w.Player.Position=new Vec3(-90,55,-150);r.w.Player.PrevPosition=r.w.Player.Position;r.w.Player.Velocity=new Vec3(2,1,-20);
  r.w.Player.Hull=73;r.w.Player.Shield=41;r.w.Player.ShieldDelay=2.5;r.w.Player.FireCooldown=.1;r.w.Player.Controls.Throttle=.4;
  r.wallet.Credits=800;r.wallet.Earned=900;r.wallet.SalvageEarned=200;r.wallet.TurretOwned=true;
  r.wallet.Credits+=buy(r.markets[0],r.cargo,r.wallet.Credits,1,8).CreditsDelta;
  r.w.Asteroids.Ore[0]=0;
  const e=r.w.Enemies[1];configure(e,1,1);resetShip(e,new Vec3(-130,45,-350),identity);e.Hull-=5;e.Shield-=2;
  resetBrain(r.brains[1],e,r.w.Player.Position);r.brains[1].Role=1;r.brains[1].StateTime=1.25;r.brains[1].WeaponCharge=.4;
  laser(r.w.Lasers,-100,40,-200,0,0,-520,0,0,0,1,12,0);
  spawn(r.pickups,new Vec3(-100,50,-160),2,2);r.pickups[0].Quantity=1;r.pickups[0].Life=42;
  r.turret.Cooldown=.17;r.turret.LastBarrel=1;r.turret.Target=1;r.w.Time=18.5;
  return r;
}
function read(value) { const result=decode(typeof value==='string'?value:json(value));assert.equal(result.tag,0,result.fields[0]);return result.fields[0]; }

test('save roundtrip preserves the economy, damaged upgraded ships, combat, depleted ore, loose cargo and RNG',()=>{
  const source=fixture(),saved=read(snapshot(source)),dest=rig();
  restore(saved,dest.w,dest.brains,dest.wallet,dest.cargo,dest.markets,dest.levels,dest.turret,dest.pickups);
  assert.deepEqual(JSON.parse(json(snapshot(dest))),JSON.parse(json(snapshot(source))));
  assert.equal(dest.w.Enemies[0].Alive,false,'destroyed enemy is not resurrected');
  assert.equal(dest.w.Enemies[1].Alive,true);
  assert.equal(dest.w.Player.Hull,73);assert.equal(dest.w.Player.MaxHull,125);assert.equal(dest.w.Player.MaxShield,130);
  assert.equal(dest.w.Asteroids.Ore[0],0);assert.equal(dest.pickups[0].Quantity,1);
  for(let i=0;i<6;i++) for(let j=0;j<6;j++){assert.equal(ask(dest.markets[i],j),ask(source.markets[i],j));assert.equal(bid(dest.markets[i],j),bid(source.markets[i],j));}
  for(let i=0;i<8;i++) assert.equal(random(dest.w.Rng),random(source.w.Rng));
});

test('recovery checkpoints are optional in old saves and validated for their equipment', () => {
  const data = snapshot(rig());
  assert.equal(read(data).Run.Recovery,undefined);
  data.Run.Recovery = {Latched:true,Progress:1.2};
  assert.deepEqual(read(data).Run.Recovery,{Latched:true,Progress:1.2});
  for (const invalid of [{Latched:false,Progress:1},{Latched:true,Progress:3},{Latched:1,Progress:0}]) {
    data.Run.Recovery = invalid;
    assert.equal(decode(json(data)).tag,1);
  }
});

test('legacy holds and freight saves restore correctly; malformed or over-capacity freight is rejected', () => {
  const data=JSON.parse(json(snapshot(fixture()))), dest=rig();
  delete data.Cargo.Freight;
  assert.equal(read(data).Cargo.Freight,undefined);
  const job={Origin:0,Destination:1,Tonnes:8,Fee:76};
  data.Cargo.Freight=job;
  restore(read(data),dest.w,dest.brains,dest.wallet,dest.cargo,dest.markets,dest.levels,dest.turret,dest.pickups);
  assert.deepEqual(dest.cargo.Freight,job);
  for(const patch of [{Fee:760},{Destination:5},{Origin:1},{Tonnes:16},{Tonnes:24,Fee:172}]) {
    data.Cargo.Freight={...job,...patch};
    assert.equal(decode(json(data)).tag,1);
  }
});

test('old saves fly the cutter; the fleet, kit, large hold and damaged ship survive a reload', () => {
  const data = snapshot(fixture());
  const restored = rig();
  const load = () => restore(read(data),restored.w,restored.brains,restored.wallet,restored.cargo,restored.markets,restored.levels,restored.turret,restored.pickups);
  load();
  assert.equal(restored.w.Player.CruiseSpeed, 60);
  assert.equal(restored.w.Player.ShieldRechargeDelay, 3.5);
  data.Run.Fleet = {Active:1,Owned:3,Kit:1,Kits:3};
  data.Cargo.Capacity=56;
  data.Cargo.Quantities[1]=45;
  load();
  assert.equal(restored.w.Player.CruiseSpeed, 60, '48 base plus 12 from the courier kit');
  assert.equal(restored.w.Player.LaserDamage, 12, '10 base with a level-one laser');
  assert.equal(restored.w.Player.Hull, 73);
  assert.equal(restored.cargo.Quantities[1], 45);
  for (const patch of [{Active:2},{Active:12},{Kit:2},{Owned:3.5},{Kits:2}]) {
    const bad=JSON.parse(json(data));
    Object.assign(bad.Run.Fleet,patch);
    assert.equal(decode(json(bad)).tag,1);
  }
  data.Run.Fleet = {Active:1,Owned:3,Kit:3,Kits:11};
  data.Player.Status[2] = 0.75;
  load();
  assert.equal(restored.w.Player.ShieldRechargeDelay, 2);
  assert.equal(restored.w.Player.ShieldDelay, 0.75);
});

test('the trade campaign accepts legacy saves and restores a launched torpedo without granting a free shot', () => {
  const data=snapshot(rig());
  assert.equal(read(data).Run.Sortie,undefined);
  data.Run.Blockade=Object.assign(newBlockade(),{Started:true,BomberReady:true});
  data.Run.Sortie=Object.assign(newSortie(),{Role:1,Ammo:1,Flight:.7,ParkedHull:73,ParkedShield:32,
    Launch:new Vec3(cruiser.X,cruiser.Y,cruiser.Z+230)});
  const saved=read(data);
  assert.equal(saved.Run.Sortie.Flight,.7);
  assert.equal(saved.Run.Sortie.Ammo,1);
  assert.equal(saved.Run.Sortie.ParkedHull,73);
  for(const invalid of [{Flight:1.1},{Role:0},{Hits:2},{Guns:[-1,32]},{Launch:{X:0,Y:0,Z:700}}]) {
    const bad=structuredClone(saved);Object.assign(bad.Run.Sortie,invalid);
    assert.equal(decode(json(bad)).tag,1);
  }
  const combat=structuredClone(saved);combat.Run.Trading=false;
  assert.equal(decode(json(combat)).tag,1);
});

test('dock contact history survives a round trip and is optional for older flights',()=>{
  const data=JSON.parse(json(snapshot(fixture())));
  delete data.Run.HeardContacts;
  assert.equal(read(data).Run.HeardContacts,undefined);
  data.Run.HeardContacts=(1<<7)|1;
  assert.equal(read(data).Run.HeardContacts,129);
  data.Run.HeardContacts |= (1<<12) | (1<<17);
  assert.equal(read(data).Run.HeardContacts,(1<<17)+(1<<12)+129);
  for(const invalid of [-1,1<<18,0.5,'1']){
    data.Run.HeardContacts=invalid;
    assert.equal(decode(json(data)).tag,1);
  }
});

test('flight school checkpoints fit their sector and live target; old games stay outside the lesson',()=>{
  const saved=JSON.parse(json(snapshot(rig())));
  Object.assign(saved.Run,{SectorId:'outer-belt',Phase:2,Spawned:0,Patrol:[]});
  assert.equal(read(saved).Run.School,undefined);
  saved.Run.School={Step:1,PaidTargets:0};
  assert.deepEqual(read(saved).Run.School,{Step:1,PaidTargets:0});
  for(const invalid of [{Step:3,PaidTargets:0},{Step:9,PaidTargets:0},{Step:2,PaidTargets:3}]){
    const bad=structuredClone(saved);bad.Run.School=invalid;
    assert.equal(decode(json(bad)).tag,1);
  }
  saved.Run.SectorId='slag-refinery';
  assert.equal(decode(json(saved)).tag,1);
  saved.Run.School={Step:0,PaidTargets:2};
  assert.equal(read(saved).Run.School.PaidTargets,2);
});

test('stack, scan history, extra hold and consumed rescues survive a save; legacy data migrates',()=>{
  const source=fixture(), dest=rig();
  Object.assign(source.w.Pilot,{Visited:63, Relics:63, Rescues:2, PulseCooldown:17, PulseTime:0, BypassCooldown:9});
  source.w.Pilot.Levels.set([3,3,3]); source.cargo.Capacity=48;
  const data=read(snapshot(source));
  restore(data,dest.w,dest.brains,dest.wallet,dest.cargo,dest.markets,dest.levels,dest.turret,dest.pickups);
  assert.equal(dest.w.Pilot.Rescues,2);
  assert.equal(dest.cargo.Capacity,48);
  assert.ok(Math.abs(dest.w.Player.LaserDamage-17.28)<1e-9);
  const bad=structuredClone(data); bad.Pilot.Visited=1; bad.Pilot.Relics=0;
  assert.equal(decode(json(bad)).tag,1);
  const old=JSON.parse(json(snapshot(fixture()))); delete old.Pilot; delete old.Lasers.Piercing;
  restore(read(old),dest.w,dest.brains,dest.wallet,dest.cargo,dest.markets,dest.levels,dest.turret,dest.pickups);
  assert.equal(dest.w.Pilot.Visited,3);
  assert.equal(dest.w.Pilot.Rescues,0);
  assert.equal(dest.w.Player.CruiseSpeed,60);
});

test('decode rejects malformed, incompatible or inconsistent data before a live restore can happen',()=>{
  const base=JSON.parse(json(snapshot(fixture())));
  const corruptions=[
    s=>s.Version=2,s=>delete s.Player,s=>s.Wallet.Credits=-1,s=>s.Wallet.Credits='200',s=>s.Run.Trading='true',
    s=>s.Cargo.Quantities[2]=40,s=>s.Cargo.Capacity=17,s=>s.Cargo.AvgCost[0]=null,s=>s.Upgrades=[0,9,0],
    s=>s.Markets[0].Stocks[2]=30,s=>s.Markets[0].SectorId='unknown',s=>s.Markets.pop(),
    s=>s.Player.Rotation=[0,0,0,0],s=>s.Player.Position[0]=null,s=>s.Player.Status[0]=0,
    s=>s.Enemies.push(s.Enemies[0]),s=>s.Enemies[0].Index=4,s=>s.Enemies[0].Role=4,s=>s.Enemies[0].Ship.Status[0]=9999,
    s=>s.Run.Phase=2,s=>s.Run.Spawned=0,s=>s.Run.Docked=true,s=>s.Run.Completed=true,s=>s.Run.SectorId='missing',
    s=>s.Asteroids.Data.pop(),s=>s.Asteroids.Variants[0]=4,s=>s.Asteroids.Ore[0]=3,
    s=>s.Lasers.Teams[0]=9,s=>s.Pickups[0].Quantity=3,s=>s.RandomState=0,s=>s.Settings.Sensitivity=100,
    s=>s.Pickups=Array(97).fill(s.Pickups[0]),s=>s.Enemies=Array(17).fill(s.Enemies[0]),
  ];
  for(const change of corruptions){const value=structuredClone(base);change(value);assert.equal(decode(json(value)).tag,1,change.toString());}
  for(const invalid of ['','null','{broken','[]','{}',' '.repeat(2000001)]) assert.equal(decode(invalid).tag,1);
});

test('saved player controls release held weapons and steering without changing throttle or momentum',()=>{
  const source=fixture();source.w.Player.Controls.Pitch=1;source.w.Player.Controls.Fire=true;source.w.Player.Controls.Boost=true;
  source.w.Player.Boost=.7;source.w.Player.PitchRate=.4;
  const saved=read(snapshot(source)),dest=rig();restore(saved,dest.w,dest.brains,dest.wallet,dest.cargo,dest.markets,dest.levels,dest.turret,dest.pickups);
  assert.equal(dest.w.Player.Controls.Fire,false);assert.equal(dest.w.Player.Controls.Boost,false);assert.equal(dest.w.Player.Controls.Pitch,0);
  assert.equal(dest.w.Player.Controls.Throttle,.4);assert.equal(dest.w.Player.PitchRate,.4);assert.equal(dest.w.Player.Boost,.7);
});

test('ordinary simulation snapshots, quiet trading and all combat mission phases are valid',()=>{
  const r=fixture();for(let i=0;i<240;i++)step(r.w,1/120);
  read(snapshot(r));
  const empty=rig();empty.w.Enemies.forEach(e=>e.Alive=false);
  read(snapshot(empty,{...run(),SectorId:'outer-belt',Phase:2,Spawned:0,Patrol:[]}));
  for(let wave=0;wave<=6;wave++){
    // Resolve sector order from the engine's markets rather than relying on labels.
    const sector=empty.markets[Math.max(0,wave-1)].SectorId;
    const state={...run(),Trading:false,SectorId:sector,Wave:wave,Phase:0,Spawned:0,Patrol:[]};
    read(snapshot(empty,state));
    if(wave>0)read(snapshot(empty,{...state,Phase:2}));
  }
});
