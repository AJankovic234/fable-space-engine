import test from 'node:test';
import assert from 'node:assert/strict';
import {FreightAction, Freight_apply as act, Freight_valid as valid} from '../build/Freight.js';
import {TradeSystem_createCargo as hold, TradeSystem_freeVolume as free, TradeSystem_loadSalvage as load,
  TradeSystem_createUniverseMarkets as markets, TradeSystem_sellAll as sell, TradeSystem_buyGood as buy} from '../build/TradeSystem.js';
import {ShipCatalog_create as fleet, ShipCatalog_equip as equip, RefitOrder} from '../build/ShipCatalog.js';
import {DeckCargo_iter as deck, DeckCargo_sealedIndex as sealed} from '../build/ProceduralMeshes.js';
import {TradeRoutes_nextHop as hop, TradeRoutes_neighbors as neighbors} from '../build/TradeRoutes.js';

const accept = (to, tonnes) => new FreightAction(0,[to,tonnes]);
const deliver = new FreightAction(1,[]), giveBack = new FreightAction(2,[]);

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
  assert.ok(valid(cargo.Freight));
  for(const dest of [0,1,2,3,4,5]) for(let current=0;current<6;current++) {
    const seen=new Set();
    for(let here=current;here!==dest;here=hop(here,dest)) {
      assert.ok(!seen.has(here)); seen.add(here);
      assert.ok(neighbors(here).includes(hop(here,dest)));
    }
  }
  assert.equal(act(cargo,1,deliver),172);
});
