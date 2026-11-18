import test from 'node:test';
import assert from 'node:assert/strict';
import {
  Blockade_create as create, Blockade_valid as valid, Blockade_beginRun as begin,
  Blockade_onTravel as travel, Blockade_apply as apply, Blockade_lift as lift,
  Blockade_canSortie as canLaunch, Blockade_manifestLeg as manifestLeg,
  Blockade_forecastMarket as forecast, BlockadeAction,
} from '../build/Blockade.js';
import { Economy_create as wallet } from '../build/Economy.js';
import {
  TradeSystem_createUniverseMarkets as markets, TradeSystem_createCargo as hold,
  TradeSystem_buyGood as buy, TradeSystem_buyPrice as ask, TradeSystem_loadSalvage as load,
} from '../build/TradeSystem.js';
import { TradeRoutes_neighbors as neighbors, TradeRoutes_tryPlanWithEscort as route } from '../build/TradeRoutes.js';

const helios = 1, drift = 2, kepler = 3;
const commonTerms = {SignalLevel: 0, FixerLevel: 0};
const clearance = new BlockadeAction(0, []), filters = new BlockadeAction(1, []);
const commission = (kind, port) => new BlockadeAction(2, [kind, port]);
const reassign = (kind, port) => new BlockadeAction(3, [kind, port]);

function flight() {
  const state = create(), ports = markets(), account = wallet(), cargo = hold(16);
  account.Credits = 200;
  const act = (port, order) => apply(state, port, account, cargo, ports, order);
  return { state, ports, account, cargo, act };
}

test('Helios stock, prices, the paid single-use manifest and Orlov receipt share live market data', () => {
  const { state, ports, account, cargo, act } = flight(), market = ports[helios];
  const normal = ask(market, 5);
  assert.equal(act(drift, clearance), false);
  begin(state, ports);
  assert.equal(market.Entries[5].Stock, 2);
  assert.ok(ask(market, 5) > normal);
  buy(market, cargo, account.Credits, 5, 1);
  begin(state, ports);
  assert.equal(market.Entries[5].Stock, 1);
  assert.equal(act(helios, filters), false);
  assert.equal(act(helios, clearance), false);
  account.Credits = 34;
  assert.equal(act(drift, clearance), false);
  account.Credits = 35;
  assert.equal(act(drift, clearance), true);
  assert.equal(account.Credits, 0);
  assert.equal(act(drift, clearance), false);
  assert.equal(act(drift, filters), false);
  assert.equal(act(helios, filters), true);
  assert.equal(account.Credits, 65);
  assert.equal(market.Entries[5].Stock, 4);
  assert.equal(act(helios, filters), false);
  assert.equal(state.Lifted, false);
  assert.equal(valid(state), true);
});

test('a paid manifest follows reachable hops to Helios through a reload and ends with the receipt', () => {
  const { state, ports, act } = flight();
  begin(state, ports);
  assert.equal(manifestLeg(state, drift), undefined);
  act(drift, clearance);
  const saved = JSON.parse(JSON.stringify(state));
  assert.equal(manifestLeg(saved, drift).NextPort, 0, 'Kestrel is the safe first leg');
  for (let port = 0; port < ports.length; port++) {
    let current = port;
    const visited = new Set();
    while (current !== helios) {
      assert.ok(!visited.has(current));
      visited.add(current);
      const { NextPort } = manifestLeg(saved, current);
      assert.ok(neighbors(current).includes(NextPort));
      current = NextPort;
    }
  }
  assert.equal(manifestLeg(saved, helios).NextPort, undefined);
  assert.match(manifestLeg(saved, helios).Hint, /quittieren/);
  act(helios, filters);
  assert.equal(manifestLeg(state, helios), undefined);
});

test('arrival forecast includes the newly completed tanker and the next medical convoy exactly once', () => {
  const { state, ports, cargo, act } = flight();
  begin(state, ports);
  state.BomberReady = true;
  lift(state, ports);
  load(cargo, 0, 2); load(cargo, 4, 1);
  assert.ok(act(kepler, commission(1, helios)));
  const before = JSON.stringify({state, ports});
  const projected = forecast(state, helios, ports[helios]);
  assert.equal(JSON.stringify({state, ports}), before);
  assert.equal(projected.Entries[4].Stock, ports[helios].Entries[4].Stock + 3);
  assert.equal(projected.Entries[5].Stock, ports[helios].Entries[5].Stock + 5);
  travel(state, ports, kepler, helios);
  assert.deepEqual(projected, ports[helios]);
});

test('one slipway consumes exact materials, advances only for a route, and a torpedo victory resupplies Helios in three stages', () => {
  const { state, ports, cargo, act } = flight(), market = ports[helios], order = commission(3, helios);
  begin(state, ports);
  assert.equal(lift(state, ports), false);
  load(cargo, 0, 2); load(cargo, 2, 1);
  cargo.AvgCost[0] = 14; cargo.AvgCost[2] = 50;
  assert.equal(act(kepler, order), false);
  assert.equal(cargo.Quantities[0], 2);
  load(cargo, 2, 1);
  assert.equal(act(helios, order), false);
  assert.equal(act(kepler, commission(3, drift)), false);
  assert.equal(act(kepler, order), true);
  assert.equal(cargo.Quantities[0], 1);
  assert.equal(cargo.AvgCost[0], 14);
  assert.equal(cargo.Quantities[2], 0);
  assert.equal(cargo.AvgCost[2], 0);
  load(cargo, 0, 2); load(cargo, 4, 1);
  assert.equal(act(kepler, commission(1, drift)), false);
  assert.equal(valid(JSON.parse(JSON.stringify(state))), true);
  travel(state, ports, kepler, kepler);
  assert.equal(state.BuildLegs, 1);
  travel(state, ports, kepler, helios);
  assert.equal(canLaunch(state, helios), true);
  assert.equal(canLaunch(state, drift), false);
  const quotes = [ask(market, 5)];
  assert.equal(lift(state, ports), true);
  assert.equal(lift(state, ports), false);
  quotes.push(ask(market, 5));
  for (let i = 0; i < 3; ++i) {
    travel(state, ports, helios, kepler);
    travel(state, ports, kepler, helios);
    quotes.push(ask(market, 5));
  }
  assert.ok(quotes[0] > quotes[1] && quotes[1] > quotes[2] && quotes[2] > quotes[3]);
  assert.equal(quotes[4], quotes[3]);
  assert.equal(market.Entries[5].Stock, 17);
  assert.equal(canLaunch(state, helios), false);
  assert.equal(valid(state), true);
});

test('built transports and escorts serve their assigned port; Kepler can send either to another useful port', () => {
  const { state, ports, cargo, act } = flight();
  begin(state, ports);
  load(cargo, 0, 4); load(cargo, 2, 1); load(cargo, 4, 1);
  assert.equal(act(kepler, commission(1, drift)), true);
  const fuel = ports[drift].Entries[4].Stock;
  travel(state, ports, kepler, drift);
  assert.equal(ports[drift].Entries[4].Stock, fuel + 3);
  assert.equal(act(drift, reassign(1, helios)), false);
  assert.equal(act(kepler, reassign(1, helios)), true);
  travel(state, ports, kepler, drift);
  assert.equal(ports[drift].Entries[4].Stock, fuel + 3);
  assert.equal(act(kepler, commission(2, 0)), false, 'there are no patrols at Kestrel');
  assert.equal(act(kepler, commission(2, helios)), true);
  travel(state, ports, kepler, helios);
  const protectedRoute = route(commonTerms, state.EscortPort, kepler, helios, true, 0);
  assert.equal(protectedRoute.Fee, 0);
  assert.equal(protectedRoute.Patrol.length, 0);
  assert.equal(route(commonTerms, state.EscortPort, kepler, drift, true, 0), undefined);
  assert.equal(route(commonTerms, state.EscortPort, drift, helios, true, 0), undefined);
  assert.equal(act(kepler, commission(2, drift)), false);
  assert.equal(act(kepler, reassign(2, drift)), true);
  assert.equal(valid(state), true);
});

test('old runs can initialise once and malformed strategic states cannot be imported', () => {
  const { state, ports } = flight();
  assert.equal(valid(state), true);
  begin(state, ports);
  for (const patch of [
    {Started: 'yes'}, {Lifted: true}, {Permit: 0.5}, {BuildKind: 3, BuildPort: drift, BuildLegs: 1},
    {BuildKind: 0, BuildPort: 0}, {Deliveries: 1}, {TransportPort: 6}, {EscortPort: 0},
    {Started: false, BomberReady: true}, {BuildKind: 3, BuildPort: helios, BuildLegs: 1, BomberReady: true},
  ]) assert.equal(valid({...state, ...patch}), false);
});
