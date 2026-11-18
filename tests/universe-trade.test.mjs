import test from "node:test";
import assert from "node:assert/strict";
import { Vec3 } from "../build/Math3D.js";
import { DeckCargo_iter as visitDeck } from "../build/ProceduralMeshes.js";
import {
  Universe_sectors as sectors,
  Universe_tryFindSector as tryFindSector,
  Universe_evaluateDocking as evaluateDocking,
  Universe_approachProgress as approachProgress,
  Universe_findTriggeredGate as findTriggeredGate,
} from "../build/Universe.js";
import {
  TradeSystem_createCargo as createCargo,
  TradeSystem_usedVolume as usedVolume,
  TradeSystem_freeVolume as freeVolume,
  TradeSystem_quantityOf as quantityOf,
  TradeSystem_loadSalvage as loadSalvage,
  TradeSystem_createMarket as createMarket,
  TradeSystem_buyPrice as buyPrice,
  TradeSystem_sellPrice as sellPrice,
  TradeSystem_buyGood as buyGood,
  TradeSystem_sellGood as sellGood,
  TradeSystem_sellAll as sellAll,
  TradeSystem_upgradeCargo as upgradeCargo,
  TradeSystem_cargoUpgradePrice as cargoUpgradePrice,
} from "../build/TradeSystem.js";

test("all universe sectors have valid outposts and jump gates pointing to existing sectors", () => {
  assert.ok(sectors.length >= 6);
  for (const sector of sectors) {
    assert.ok(sector.Outpost.DockRadius > 0);
    assert.ok(sector.Outpost.ApproachRadius > sector.Outpost.DockRadius);
    assert.ok(sector.Gates.length >= 2);
    for (const gate of sector.Gates) {
      assert.ok(tryFindSector(gate.TargetSectorId), `Unknown gate target: ${gate.TargetSectorId}`);
    }
  }
});

test("outpost docking transitions from TooFar -> Approaching -> ReadyToDock and blocks during combat", () => {
  const outer = tryFindSector("outer-belt");
  const pos = outer.Outpost.Position;

  const far = evaluateDocking(outer.Outpost, new Vec3(pos.X + 400, pos.Y, pos.Z), false);
  assert.equal(far.tag, 0); // TooFar
  assert.equal(approachProgress(outer.Outpost, new Vec3(pos.X + 400, pos.Y, pos.Z)), 0);

  const mid = evaluateDocking(outer.Outpost, new Vec3(pos.X + 150, pos.Y, pos.Z), false);
  assert.equal(mid.tag, 1); // Approaching
  const prog = approachProgress(outer.Outpost, new Vec3(pos.X + 150, pos.Y, pos.Z));
  assert.ok(prog > 0 && prog < 1);

  const blocked = evaluateDocking(outer.Outpost, new Vec3(pos.X + 30, pos.Y, pos.Z), true);
  assert.equal(blocked.tag, 3); // BlockedByCombat

  const ready = evaluateDocking(outer.Outpost, new Vec3(pos.X + 30, pos.Y, pos.Z), false);
  assert.equal(ready.tag, 2); // ReadyToDock
  assert.equal(approachProgress(outer.Outpost, new Vec3(pos.X + 30, pos.Y, pos.Z)), 1);
});

test("jump gate triggers only inside trigger radius", () => {
  const outer = tryFindSector("outer-belt");
  const gate = outer.Gates[0];
  assert.equal(findTriggeredGate(outer, new Vec3(0, 0, 0)), undefined);
  const hit = findTriggeredGate(outer, new Vec3(gate.Position.X + 10, gate.Position.Y, gate.Position.Z));
  assert.equal(hit.TargetSectorId, gate.TargetSectorId);
});

test("cargo hold tracks capacity, salvage loading and upgrades", () => {
  const cargo = createCargo(16);
  assert.equal(usedVolume(cargo), 0);
  assert.equal(freeVolume(cargo), 16);

  // CryoOre = 1 vol, PlasmaCores = 2 vol
  assert.equal(loadSalvage(cargo, 1, 10), 10);
  assert.equal(usedVolume(cargo), 10);
  assert.equal(loadSalvage(cargo, 2, 5), 3); // only 6 volume left -> max 3 PlasmaCores
  assert.equal(freeVolume(cargo), 0);

  const upg = upgradeCargo(cargo, 200);
  assert.equal(upg.Ok, true);
  assert.equal(upg.CreditsDelta, -110);
  assert.equal(cargo.Capacity, 24);
  assert.equal(freeVolume(cargo), 8);
  for (const [cost, capacity] of [[200, 32], [290, 40]]) {
    assert.equal(cargoUpgradePrice(cargo), cost);
    const expanded = upgradeCargo(cargo, cost);
    assert.equal(expanded.CreditsDelta, -cost);
    assert.equal(cargo.Capacity, capacity);
  }
  assert.equal(cargoUpgradePrice(cargo), 0);
  assert.equal(upgradeCargo(cargo, 10000).Ok, false);
  assert.equal(cargo.Capacity, 40);
});

test("trading arbitrage works between MiningDepot and Refinery and updates station stock/prices", () => {
  const mining = createMarket(tryFindSector("outer-belt"));
  const refinery = createMarket(tryFindSector("slag-refinery"));
  const cargo = createCargo(16);

  const buyPriceBefore = buyPrice(mining, 1); // CryoOre at MiningDepot
  const sellPriceAtRefinery = sellPrice(refinery, 1);
  assert.ok(sellPriceAtRefinery > buyPriceBefore, "Refinery should pay more for CryoOre than MiningDepot");

  const bought = buyGood(mining, cargo, 500, 1, 8);
  assert.equal(bought.Ok, true);
  assert.equal(quantityOf(cargo, 1), 8);
  assert.ok(buyPrice(mining, 1) >= buyPriceBefore, "Buying reduces stock and keeps/raises price");

  const sold = sellGood(refinery, cargo, 1, 5);
  assert.equal(sold.Ok, true);
  assert.ok(sold.CreditsDelta > 0);
  assert.equal(quantityOf(cargo, 1), 3);

  const rest = sellAll(refinery, cargo);
  assert.equal(rest.Ok, true);
  assert.equal(usedVolume(cargo), 0);
  assert.ok(bought.CreditsDelta + sold.CreditsDelta + rest.CreditsDelta > 0, "Travel between specialized stations remains profitable");
});

test("same-station round trips lose money for every station, commodity and batch size", () => {
  for (const sector of sectors) {
    for (let id = 0; id < 6; id++) {
      const maximum = createMarket(sector).Entries[id].MaxStock;
      for (let amount = 1; amount <= maximum; amount++) {
        const market = createMarket(sector);
        const cargo = createCargo(128);
        const initial = market.Entries[id];
        const bought = buyGood(market, cargo, 100000, id, amount);
        const acquired = quantityOf(cargo, id);
        assert.equal(bought.Ok, true);
        assert.equal(market.Entries[id].Stock + acquired, initial.Stock);
        const sold = sellGood(market, cargo, id, acquired);
        assert.equal(sold.Ok, true);
        assert.ok(bought.CreditsDelta + sold.CreditsDelta < 0, `${sector.Id}/${id}/${amount}: buy then sell`);
        assert.equal(quantityOf(cargo, id), 0);
        assert.equal(market.Entries[id].Stock, initial.Stock);
        assert.equal(market.Entries[id].Price, initial.Price);

        // The inverse route must not profit by first flooding the station either.
        loadSalvage(cargo, id, amount);
        const sale = sellGood(market, cargo, id, amount);
        const transferred = amount - quantityOf(cargo, id);
        assert.equal(sale.Ok, true);
        const replacement = buyGood(market, cargo, 100000, id, transferred);
        assert.equal(replacement.Ok, true);
        assert.ok(sale.CreditsDelta + replacement.CreditsDelta < 0, `${sector.Id}/${id}/${amount}: sell then buy`);
        assert.equal(quantityOf(cargo, id), amount);
        assert.equal(market.Entries[id].Stock, initial.Stock);
      }
    }
  }
});

test("bulk trades match successive exact unit quotes, respecting cash and cargo capacity", () => {
  for (const sector of sectors) {
    for (let id = 0; id < 6; id++) {
      for (const credits of [0, 50, 1000]) {
        const bulkMarket = createMarket(sector);
        const unitMarket = createMarket(sector);
        const bulkCargo = createCargo(16);
        const unitCargo = createCargo(16);
        const bulk = buyGood(bulkMarket, bulkCargo, credits, id, 30);
        let spent = 0;
        for (let i = 0; i < 30; i++) {
          const quote = buyPrice(unitMarket, id);
          const purchase = buyGood(unitMarket, unitCargo, credits - spent, id, 1);
          if (!purchase.Ok) break;
          assert.equal(purchase.CreditsDelta, -quote);
          spent -= purchase.CreditsDelta;
        }
        assert.equal(bulk.CreditsDelta, spent === 0 ? 0 : -spent);
        assert.ok(spent <= credits);
        assert.equal(bulk.VolumeDelta, usedVolume(bulkCargo));
        assert.ok(usedVolume(bulkCargo) <= bulkCargo.Capacity);
        assert.deepEqual(bulkCargo.Quantities, unitCargo.Quantities);
        assert.deepEqual(bulkMarket.Entries, unitMarket.Entries);
        const quantity = quantityOf(bulkCargo, id);
        if (quantity > 0) assert.ok(Math.abs(bulkCargo.AvgCost[id] - spent / quantity) < 1e-9);
      }

      const bulkMarket = createMarket(sector);
      const unitMarket = createMarket(sector);
      const bulkCargo = createCargo(64);
      const unitCargo = createCargo(64);
      loadSalvage(bulkCargo, id, 30);
      loadSalvage(unitCargo, id, 30);
      const before = bulkMarket.Entries[id].Stock + quantityOf(bulkCargo, id);
      const bulk = sellGood(bulkMarket, bulkCargo, id, 30);
      let revenue = 0;
      let volume = 0;
      for (let i = 0; i < 30; i++) {
        const quote = sellPrice(unitMarket, id);
        const sale = sellGood(unitMarket, unitCargo, id, 1);
        if (!sale.Ok) break;
        assert.equal(sale.CreditsDelta, quote);
        revenue += sale.CreditsDelta;
        volume += sale.VolumeDelta;
      }
      assert.equal(bulk.CreditsDelta, revenue);
      assert.equal(bulk.VolumeDelta, volume);
      assert.deepEqual(bulkCargo.Quantities, unitCargo.Quantities);
      assert.deepEqual(bulkMarket.Entries, unitMarket.Entries);
      assert.equal(bulkMarket.Entries[id].Stock + quantityOf(bulkCargo, id), before);
      assert.equal(sellPrice(bulkMarket, id), 0);
    }
  }
});

test("partial selling preserves unsold cargo and sellAll reports actual units and volume", () => {
  const market = createMarket(tryFindSector("outer-belt"));
  const cargo = createCargo(40);
  loadSalvage(cargo, 1, 16);
  loadSalvage(cargo, 2, 8);
  const beforeCryo = market.Entries[1].Stock;
  const beforePlasma = market.Entries[2].Stock;
  const sale = sellAll(market, cargo);
  assert.equal(sale.Ok, true);
  assert.equal(quantityOf(cargo, 1), 11);
  assert.equal(quantityOf(cargo, 2), 1);
  assert.equal(sale.VolumeDelta, -19); // 5 ore + 7 cores of 2 volume each.
  assert.equal(usedVolume(cargo), 13);
  assert.equal(market.Entries[1].Stock - beforeCryo, 5);
  assert.equal(market.Entries[2].Stock - beforePlasma, 7);
  assert.match(sale.Message, /^12 Einheiten/);
  assert.match(sale.Message, /12 Einheiten bleiben an Bord/);
  const blocked = sellAll(market, cargo);
  assert.equal(blocked.Ok, false);
  assert.equal(blocked.CreditsDelta, 0);
  assert.equal(blocked.VolumeDelta, 0);
  assert.match(blocked.Message, /Stationslager voll/);
  assert.equal(usedVolume(cargo), 13);
  assert.match(sellAll(market, createCargo(16)).Message, /leer/);
});

test("invalid amounts, insufficient funds and unavailable stock cannot change trade balances", () => {
  const market = createMarket(tryFindSector("outer-belt"));
  const cargo = createCargo(40);
  loadSalvage(cargo, 1, 10);
  const beforeStock = market.Entries[1].Stock;
  const nextBuy = buyPrice(market, 1);
  for (const amount of [-5, 0]) {
    assert.equal(buyGood(market, cargo, 1000, 1, amount).Ok, false);
    assert.equal(sellGood(market, cargo, 1, amount).Ok, false);
  }
  for (const credits of [-1, 0, nextBuy - 1]) {
    assert.equal(buyGood(market, cargo, credits, 1, 1).Ok, false);
  }
  assert.equal(quantityOf(cargo, 1), 10);
  assert.equal(market.Entries[1].Stock, beforeStock);
  const exact = buyGood(market, cargo, nextBuy, 1, 20);
  assert.equal(exact.CreditsDelta, -nextBuy);
  assert.equal(quantityOf(cargo, 1), 11);

  const empty = createCargo(64);
  const drained = buyGood(market, empty, 100000, 1, 1000);
  assert.equal(drained.Ok, true);
  assert.equal(market.Entries[1].Stock, 0);
  assert.equal(buyPrice(market, 1), 0);
  assert.equal(buyGood(market, empty, 100000, 1, 1).Ok, false);
  assert.ok(usedVolume(empty) <= empty.Capacity);
});

test("the cutter's outside rack follows occupied volume and samples only goods on board", () => {
  const cargo = createCargo(12);
  const rack = () => {
    const packages = [];
    visitDeck(cargo, (bay, good) => packages.push([bay.Z, good]));
    return packages;
  };
  assert.deepEqual(rack(), []);
  loadSalvage(cargo, 2, 2); // Four volume units of plasma cores occupy the first bay.
  assert.deepEqual(rack().map(p => p[1]), [2]);
  loadSalvage(cargo, 5, 4);
  assert.deepEqual(rack().map(p => p[1]), [2, 5]);
  loadSalvage(cargo, 0, 4);
  const full = rack();
  assert.deepEqual(full.map(p => p[1]), [0, 2, 5]);
  assert.equal(new Set(full.map(p => p[0])).size, 3);
  cargo.Capacity = 24;
  assert.equal(rack().length, 2);
  cargo.Quantities.fill(0);
  assert.deepEqual(rack(), []);
});
