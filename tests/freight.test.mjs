import test from 'node:test';
import assert from 'node:assert/strict';
import {FreightAction, Freight_apply as act, Freight_valid as valid, Freight_hint as dockHint,
  Freight_flightHint as flightHint, Freight_nextGate as nextGate} from '../build/Freight.js';
import {TradeSystem_createCargo as hold, TradeSystem_freeVolume as free, TradeSystem_loadSalvage as load,
  TradeSystem_createUniverseMarkets as markets, TradeSystem_sellAll as sell, TradeSystem_buyGood as buy} from '../build/TradeSystem.js';
import {ShipCatalog_create as fleet, ShipCatalog_equip as equip, RefitOrder} from '../build/ShipCatalog.js';
import {DeckCargo_iter as deck, DeckCargo_sealedIndex as sealed} from '../build/ProceduralMeshes.js';
import {TradeRoutes_nextHop as hop, TradeRoutes_neighbors as neighbors} from '../build/TradeRoutes.js';
import {Universe_sectors as sectors} from '../build/Universe.js';

const accept = (to, tonnes) => new FreightAction(0,[to,tonnes]);
const deliver = new FreightAction(1,[]), giveBack = new FreightAction(2,[]);

test('the flight follows the next physical gate, the dock offers travel and the destination requests delivery', () => {
  const cargo = hold(16);
  act(cargo, 0, accept(1, 8));
  const job = cargo.Freight;
  assert.match(dockHint(0, job), /Am Dock.*Helios/);
  assert.match(flightHint(0, job), /Frachtkurs.*Sprungtor.*Helios/);
  assert.equal(nextGate(0, job), sectors[1].Id);
  const detour = 5;
  const next = nextGate(detour, job);
  const gate = sectors[detour].Gates.find(g => g.TargetSectorId === next);
  assert.ok(gate);
  assert.notEqual(next, sectors[1].Id);
  assert.ok(flightHint(detour, job).includes(gate.Label));
  assert.equal(nextGate(1, job), undefined);
  assert.match(flightHint(1, job), /am Handelsmarkt abliefern/);
});

test('sealed cargo occupies real space, never enters a market sale and pays once at its destination', () => {
  const cargo=hold(16), stations=markets();
  assert.equal(act(cargo,0,accept(1,24)),undefined);
  assert.equal(act(cargo,0,accept(0,8)),undefined);
  assert.equal(act(cargo,0,accept(5,8)),undefined);
  assert.equal(act(cargo,0,accept(1,8)),0);
  assert.equal(free(cargo),8);
  assert.equal(act(cargo,0,accept(2,8)),undefined);
  assert.equal(act(cargo,0,deliver),undefined);
  assert.equal(act(cargo,2,giveBack),undefined);
  assert.equal(load(cargo,1,7),7);
  assert.equal(buy(stations[0],cargo,1000,2,1).Ok,false,'plasma requires two free tonnes');
  const shown=[];
  deck(cargo,(bay,kind)=>shown.push(kind));
  assert.ok(shown.includes(sealed) && shown.includes(1));
  assert.ok(sell(stations[1],cargo).Ok);
  assert.equal(sell(stations[1],cargo).Ok,false);
  assert.equal(free(cargo),8);
  assert.equal(act(cargo,1,deliver),76);
  assert.equal(act(cargo,1,deliver),undefined);
  assert.equal(free(cargo),16);
  assert.equal(act(cargo,1,accept(0,8)),0);
  assert.equal(act(cargo,1,giveBack),0);
  assert.equal(act(cargo,0,deliver),undefined);
});

test('a large load blocks switching to a smaller ship, and detours retain a navigable path', () => {
  const ships=fleet(), cargo=hold(16);
  equip(ships,cargo,380,true,new RefitOrder(0,[1]));
  assert.equal(act(cargo,0,accept(1,24)),0);
  assert.equal(free(cargo),8);
  assert.equal(equip(ships,cargo,320,true,new RefitOrder(0,[2])).tag,1);
  const courier = new RefitOrder(0,[3]);
  assert.equal(equip(ships,cargo,539,true,courier).tag,1);
  assert.equal(equip(ships,cargo,540,true,courier).fields[0],540);
  assert.equal(free(cargo),0);
  assert.equal(ships.Active,3);
  assert.ok(valid(cargo.Freight));
  for(const dest of sectors.keys()) for(const current of sectors.keys()) {
    const seen=new Set();
    for(let here=current;here!==dest;here=hop(here,dest)) {
      assert.ok(!seen.has(here)); seen.add(here);
      assert.ok(neighbors(here).includes(hop(here,dest)));
    }
  }
  assert.equal(act(cargo,1,deliver),172);
});

test('freight boards at the new ports support both the outward and return legs', () => {
  for (const [origin, destination] of [[6, 1], [7, 2], [4, 6], [5, 7]]) {
    const holdToShip = hold(16);
    assert.equal(act(holdToShip, origin, accept(destination, 8)), 0);
    const job = holdToShip.Freight;
    assert.equal(nextGate(origin, job), sectors[destination].Id);
    assert.equal(act(holdToShip, origin, deliver), undefined);
    assert.ok(act(holdToShip, destination, deliver) > 0);
    assert.equal(holdToShip.Freight, undefined);
  }
});
