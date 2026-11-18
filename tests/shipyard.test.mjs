import test from 'node:test';
import assert from 'node:assert/strict';
import { ShipCatalog_create as fleet, ShipCatalog_equip as equip, RefitOrder } from '../build/ShipCatalog.js';
import { TradeSystem_createCargo as hold, TradeSystem_upgradeHullCargo as expand } from '../build/TradeSystem.js';
import { Damage_ship as hit, Physics_step as step, WorldModule_create as world } from '../build/PhysicsAndPool.js';
import { Mission_applyLoadout as loadout, Mission_resetPlayer as reset } from '../build/Mission.js';

const hull = i => new RefitOrder(0, [i]);
const kit = i => new RefitOrder(1, [i]);
const standard = { SignalLevel: 0, FixerLevel: 0 };

test('changing hulls preserves paid expansions and cargo, and owned ships are free to fly again', () => {
  const ships = fleet(), cargo = hold(24);
  const refit = (order, funds, workshop = true) => equip(ships, cargo, funds, workshop, order);
  assert.equal(refit(hull(1), 379).tag, 1);
  assert.equal(refit(hull(1), 380, false).tag, 1);
  assert.deepEqual([ships.Active, ships.Owned, cargo.Capacity], [0, 1, 24]);
  assert.equal(refit(hull(1), 380).fields[0], 380);
  assert.equal(cargo.Capacity, 40);
  assert.equal(expand(standard, 32, cargo, 200).CreditsDelta, -200);
  cargo.Quantities[1] = 28;
  cargo.AvgCost[1] = 21.5;
  const before = JSON.stringify([ships,cargo]);
  assert.equal(refit(hull(2), 320).tag, 1);
  assert.equal(JSON.stringify([ships,cargo]), before);
  cargo.Quantities[1] = 20;
  assert.equal(refit(hull(2), 320).fields[0], 320);
  assert.deepEqual([cargo.Capacity, cargo.Quantities[1], cargo.AvgCost[1]], [24,20,21.5]);
  assert.equal(refit(hull(1), 0).fields[0], 0);
  assert.equal(cargo.Capacity, 48);
});

test('the tuning slot holds one kit and previously bought kits can be refitted for free', () => {
  const ships = fleet(), cargo = hold(16);
  assert.equal(equip(ships,cargo,90,true,kit(1)).fields[0], 90);
  assert.equal(equip(ships,cargo,130,true,kit(2)).fields[0], 130);
  assert.deepEqual([ships.Kit, ships.Kits], [2,7]);
  assert.equal(equip(ships,cargo,0,true,kit(1)).fields[0], 0);
  assert.equal(equip(ships,cargo,0,true,kit(1)).tag, 1);
});

test('the relay restarts its shorter delay on every hit, while shield capacity and recharge rate stay the same', () => {
  const w = world(1), ship = w.Player, ships = fleet(), cargo = hold(16);
  ship.Alive = true;
  assert.equal(equip(ships,cargo,149,true,kit(3)).tag, 1);
  assert.equal(equip(ships,cargo,150,true,kit(3)).fields[0], 150);
  loadout(ship,[0,0,0],w.Pilot,ships);
  hit(w,ship,10);
  assert.equal(ship.ShieldDelay, 2);
  for (let i = 0; i < 60; i++) step(w,1/60);
  hit(w,ship,10);
  assert.equal(ship.ShieldDelay, 2);
  for (let i = 0; i < 121; i++) step(w,1/60);
  assert.ok(Math.abs(ship.Shield - 80) < 0.25);
  for (let i = 0; i < 30; i++) step(w,1/60);
  assert.ok(Math.abs(ship.Shield - 87) < 0.25);
  assert.equal(ship.MaxShield, 100);
  assert.equal(w.Enemies[0].ShieldRechargeDelay, 3.5);
  assert.equal(equip(ships,cargo,0,true,kit(0)).fields[0], 0);
  loadout(ship,[0,0,0],w.Pilot,ships);
  hit(w,ship,1);
  assert.equal(ship.ShieldDelay, 3.5);
  assert.equal(equip(ships,cargo,0,true,kit(3)).fields[0], 0);
  loadout(ship,[0,0,0],w.Pilot,ships);
  reset(ship);
  assert.equal(ship.ShieldRechargeDelay, 3.5);
});
