import test from 'node:test';
import assert from 'node:assert/strict';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { route as chartRoute, routeThrough, validStop, nextPort as courseGate } from '../build/StarMap.js';
import {
  TradeRoutes_neighbors as neighbors, TradeRoutes_tryPlan as plan,
  TradeRoutes_escortFee as fee, TradeRoutes_patrol as patrol,
  TradeRoutes_quoteCargo as quote, TradeRoutes_quoteSaleFor as quoteSale,
  TradeRoutes_quoteCargoFor as pilotQuote, TradeRoutes_tryPlanFor as pilotPlan,
  TradeRoutes_escortFeeFor as pilotFee, TradeRoutes_patrolDelay as delay,
  TradeRoutes_galacticAverageSell as avgSell, TradeRoutes_bestNeighborBid as bestBid,
} from '../build/TradeRoutes.js';
import {
  TradeSystem_createCargo as cargo, TradeSystem_createMarket as market,
  TradeSystem_loadSalvage as load, TradeSystem_sellAll as sell,
  TradeSystem_sellPrice as baseBid, TradeSystem_pilotSellPrice as pilotBid,
  TradeSystem_sellGoodFor as sellFor, TradeSystem_sellAllFor as sellAllFor,
  TradeSystem_buyGood as buy, TradeSystem_cargoUpgradePriceFor as upgradePrice,
  TradeSystem_upgradeCargoFor as upgrade,
} from '../build/TradeSystem.js';

test('routes follow the universe graph, allow returns and reject invalid destinations', () => {
  assert.equal(neighbors(-1).length, 0);
  assert.equal(neighbors(99).length, 0);
  for (let from=0; from<sectors.length; from++) {
    for (let to=-1; to<=sectors.length; to++) {
      assert.equal(!!plan(from,to,false,0), neighbors(from).includes(to));
    }
    for (const to of neighbors(from)) assert.ok(neighbors(to).includes(from));
  }
});

test('escort requires the exact fee and prevents patrols; independent travel retains bounded risk', () => {
  for (let from=0; from<sectors.length; from++) {
    for (const to of neighbors(from)) {
      const price=fee(to), paid=plan(from,to,true,price), independent=plan(from,to,false,0);
      assert.equal(paid.Fee,price);
      assert.equal(paid.Patrol.length,0);
      if (price>0) assert.equal(plan(from,to,true,price-1),undefined);
      assert.equal(independent.Fee,0);
      assert.deepEqual(independent.Patrol,patrol(to));
      if(sectors[to].DangerLevel>1) assert.ok(independent.Patrol.length>=2 && independent.Patrol.length<=4);
      else assert.equal(independent.Patrol.length,0);
      assert.equal(independent.Patrol.includes(4),false,'commander stays in optional combat mission');
    }
  }
});

test('a chart course uses the first gate of each remaining leg and ends at its destination', () => {
  const port = id => sectors.findIndex(sector => sector.Id === id);
  const kaltstrom = 'kaltstrom', wracksaum = 'wracksaum';
  assert.deepEqual(chartRoute(port('outer-belt'), port(kaltstrom)),
    [port('outer-belt'), port('slag-refinery'), port(kaltstrom)]);
  assert.equal(courseGate(port('outer-belt'), kaltstrom), port('slag-refinery'));
  assert.equal(courseGate(port('slag-refinery'), kaltstrom), port(kaltstrom));
  assert.equal(courseGate(port(kaltstrom), kaltstrom), undefined);
  assert.deepEqual(chartRoute(port('outer-belt'), port(wracksaum)),
    [port('outer-belt'), port('frontier-drift'), port(wracksaum)]);
  assert.equal(courseGate(port('outer-belt'), 'missing'), undefined);
  assert.deepEqual(chartRoute(port(kaltstrom), port(wracksaum)),
    [port(kaltstrom), port('schleierpass'), port(wracksaum)]);
  assert.deepEqual(Array.from(patrol(port('schleierpass'))), [5, 1, 5]);
});

test('a waypoint picks either route from Kestrel to Kepler and rejects backtracking', () => {
  const port = id => sectors.findIndex(sector => sector.Id === id);
  const kestrel = port('outer-belt'), kepler = port('kepler-yard');
  for (const stop of ['slag-refinery', 'frontier-drift']) {
    assert.deepEqual(Array.from(routeThrough(kestrel, kepler, port(stop))), [kestrel, port(stop), kepler]);
    assert.equal(courseGate(kestrel, 'kepler-yard', stop), port(stop));
    assert.equal(validStop(port(stop), 'kepler-yard', stop), false, 'the stop is cleared on arrival');
  }
  assert.equal(routeThrough(kestrel, port('slag-refinery'), port('kaltstrom')), undefined);
});

test('cargo destination quotes use marginal prices and capacity without mutating live trade state', () => {
  const hold=cargo(24), station=market(sectors[0]);
  load(hold,1,16);load(hold,4,4);
  hold.AvgCost[1]=18;
  const before=JSON.stringify({hold,station});
  const estimated=quote(station,hold);
  const [, margin] = quoteSale({SignalLevel: 0, FixerLevel: 0},station,hold);
  assert.equal(JSON.stringify({hold,station}),before);
  assert.equal(estimated.VolumeDelta,-9,'only five ore plus four fuel fit');
  const actual=sell(station,hold);
  assert.equal(actual.CreditsDelta,estimated.CreditsDelta);
  assert.equal(margin, actual.CreditsDelta - 5 * 18, 'only accepted ore has an acquisition cost; salvaged fuel is free');
  assert.equal(actual.VolumeDelta,estimated.VolumeDelta);
  assert.equal(hold.Quantities[1],11);
  const fullQuote=quote(station,hold);
  assert.equal(fullQuote.CreditsDelta,0);
  assert.equal(fullQuote.VolumeDelta,0);
  assert.equal(quoteSale({SignalLevel: 0, FixerLevel: 0},station,hold)[1], 0);
});

test('fixer fare, pirate countdown and shielded hold use the purchased ranks', () => {
  const first = {SignalLevel: 0, FixerLevel: 1};
  const second = {...first, FixerLevel: 2};
  const to = neighbors(0).find(i => fee(i) > 0);
  const discounted = Math.floor(fee(to) * 0.75);
  assert.equal(pilotFee(first, to), discounted);
  assert.equal(pilotPlan(first, 0, to, true, discounted).Fee, discounted);
  assert.equal(pilotPlan(first, 0, to, true, discounted - 1), undefined);
  assert.equal(delay(first), 8);
  assert.equal(delay(second), 20);
  const hold = cargo(24);
  for (const cost of [82, 150, 217]) {
    assert.equal(upgradePrice(second, hold), cost);
    assert.equal(upgrade(second, hold, cost - 1).Ok, false);
    assert.equal(upgrade(second, hold, cost).CreditsDelta, -cost);
  }
  assert.equal(hold.Capacity, 48);
  assert.equal(upgrade(second, hold, 1000).Ok, false);
});

test('forensics and council pay the quoted bonus on positive margins, including partial bulk sales', () => {
  const terms = {SignalLevel: 2, FixerLevel: 3};
  const station = market(sectors[2]), hold = cargo(48);
  for (const [id, basis, rate] of [[3, 12.5, 50], [4, 9, 15], [0, 350, 15]]) {
    const bid = baseBid(station, id);
    assert.equal(pilotBid(terms, station, hold, id), bid, 'unknown future purchase has no bonus quote');
    load(hold, id, 16);
    hold.AvgCost[id] = basis;
    const expected = bid + Math.floor(Math.max(0, bid - basis) * rate / 100);
    assert.equal(pilotBid(terms, station, hold, id), expected);
    assert.equal(sellFor(terms, station, hold, id, 1).CreditsDelta, expected);
  }
  const preview = pilotQuote(terms, station, hold);
  const actual = sellAllFor(terms, station, hold);
  assert.equal(actual.CreditsDelta, preview.CreditsDelta);
  assert.equal(actual.VolumeDelta, preview.VolumeDelta);
  assert.ok(hold.Quantities.some(q => q > 0));
  for (const sector of sectors) {
    for (let id = 0; id < 6; id++) {
      const local = market(sector), empty = cargo(48);
      const purchase = buy(local, empty, 100000, id, 40);
      const resale = sellAllFor(terms, local, empty);
      assert.ok(purchase.CreditsDelta + resale.CreditsDelta < 0, 'same-market round trip cannot mint credits');
    }
  }
});

test('galactic average and best neighbor bid stay read-only and surface profitable routes', () => {
  const markets = sectors.map((s) => market(s));
  const before = markets.map((m) => m.Entries.map((e) => e.Stock));
  const hold = cargo(16);
  const terms = { FixerLevel: 0, ForensicsLevel: 0, CouncilLevel: 0 };

  const cryoAvg = avgSell(markets, 0);
  assert.ok(cryoAvg > 0, `Expected positive galactic average for CryoOre, got ${cryoAvg}`);

  const bestFromKestrel = bestBid(terms, markets, hold, 0, 0);
  assert.ok(bestFromKestrel && bestFromKestrel[0] === 1 && bestFromKestrel[1] >= cryoAvg,
    `Expected Helios (1) as top CryoOre buyer from Kestrel, got ${bestFromKestrel}`);
  assert.equal(bestBid(terms, markets, hold, -1, 0), undefined);
  assert.deepEqual(markets.map((m) => m.Entries.map((e) => e.Stock)), before);
});
