import test from 'node:test';
import assert from 'node:assert/strict';
import { ShipCatalog_create as fleet, ShipCatalog_equip as equip, ShipCatalog_startFleet as startFleet, ShipCatalog_valid as valid, RefitOrder } from '../build/ShipCatalog.js';
import { TradeSystem_createCargo as hold, TradeSystem_upgradeHullCargo as expand } from '../build/TradeSystem.js';
import { Damage_ship as hit, Physics_step as step, WorldModule_create as world } from '../build/PhysicsAndPool.js';
import { Mission_applyLoadout as loadout, Mission_resetPlayer as reset } from '../build/Mission.js';

const hull = i => new RefitOrder(0, [i]);
const kit = i => new RefitOrder(1, [i]);
const standard = { SignalLevel: 0, FixerLevel: 0 };

test('a new pilot owns the chosen hull and buys the cutter at the same shipyard as any other hull', () => {
  for (let i = 0; i < 4; i++) {
    const ships = startFleet(i);
    assert.equal(ships.Active, i);
    assert.equal(ships.Owned, 1 << i);
    assert.ok(valid(ships));
  }
  const ships = startFleet(2), cargo = hold(8);
  assert.equal(equip(ships, cargo, 199, true, hull(0)).tag, 1);
  assert.equal(equip(ships, cargo, 200, true, hull(0)).fields[0], 200);
  assert.equal(equip(ships, cargo, 0, true, hull(2)).fields[0], 0);
});

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

test('new weapon kits configure rapid fire, shield-piercing plasma and 4-bolt scatter salvos', () => {
  const w = world(1), ship = w.Player, ships = fleet(), cargo = hold(16);
  ship.Alive = true;
  // Kit 4: RapidBlaster -> faster fire interval
  assert.equal(equip(ships, cargo, 190, true, kit(4)).fields[0], 190);
  loadout(ship, [0, 0, 0], w.Pilot, ships);
  assert.ok(ship.FireInterval < 0.10);
  assert.equal(ship.PiercingShots, false);
  assert.equal(ship.ScatterShots, false);

  // Kit 5: PlasmaCannon -> piercing bolts + higher single-bolt damage
  assert.equal(equip(ships, cargo, 260, true, kit(5)).fields[0], 260);
  loadout(ship, [0, 0, 0], w.Pilot, ships);
  assert.equal(ship.PiercingShots, true);
  assert.ok(ship.LaserDamage > 20.0);
  ship.Controls.Fire = true;
  ship.FireCooldown = 0;
  step(w, 0.016);
  assert.equal(w.Lasers.Count, 2);
  assert.equal(w.Lasers.Piercing[0], 1);
  assert.equal(w.Lasers.Piercing[1], 1);

  // Kit 6: ScatterLaser -> 4 bolts per shot
  w.Lasers.Count = 0;
  assert.equal(equip(ships, cargo, 240, true, kit(6)).fields[0], 240);
  loadout(ship, [0, 0, 0], w.Pilot, ships);
  assert.equal(ship.ScatterShots, true);
  ship.FireCooldown = 0;
  step(w, 0.016);
  assert.equal(w.Lasers.Count, 4);
});
