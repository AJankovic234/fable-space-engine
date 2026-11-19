import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import {
  WreckFields_forSector as forSector, WreckFields_mesh as mesh,
  WreckFields_nearest as nearest, WreckFields_recover as recover,
  WreckFields_debrisRadius as debrisRadius,
} from '../build/WreckFields.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { forSector as hullFor, laserHit as hit } from '../build/StationCollision.js';
import { TradeSystem_createCargo as createCargo, TradeSystem_loadSalvage as loadCargo } from '../build/TradeSystem.js';
import { AsteroidFieldModule_create as createField, AsteroidFieldModule_generateForSector as seedField } from '../build/PhysicsAndPool.js';
import { Rng_$ctor_Z524259A4 as rng } from '../build/ProceduralMeshes.js';

const index = id => sectors.findIndex(sector => sector.Id === id);
const offset = (p, x, y, z) => new Vec3(p.X + x, p.Y + y, p.Z + z);
const ray = (hull, a, b) => hit(hull, a.X, a.Y, a.Z, b.X, b.Y, b.Z);

test('the two wreck fields fit their culling volume and solid hulls provide cover', () => {
  for (const id of ['wracksaum', 'schleierpass']) {
    const sector = index(id);
    const sites = forSector(sector);
    assert.equal(sites.length, 2);
    assert.notEqual(sites[0].Kind.tag, sites[1].Kind.tag);
    for (const site of sites) {
      const { Vertices, Indices } = mesh(site.Kind);
      for (let i = 0; i < Vertices.length; i += 10) {
        assert.ok(Math.hypot(...Vertices.subarray(i, i + 3)) <= debrisRadius);
      }
      assert.ok(Indices instanceof Uint16Array && Indices.every(i => i < Vertices.length / 10));
      const t = ray(hullFor(sector), offset(site.Position, 0, 0, 50), offset(site.Position, 0, 0, -50));
      assert.ok(t > 0 && t < 1);
      assert.equal(nearest(sites, offset(site.Position, 0, 0, 65)), site);
      assert.notEqual(nearest(sites, offset(site.Position, 0, 0, 65.01)), site);
    }
  }
});

test('wreck salvage is limited by hold and reserve space, with the remainder available on return', () => {
  const site = forSector(index('schleierpass'))[0];
  const firstHold = createCargo(8);
  assert.equal(loadCargo(firstHold, 4, 6), 6);
  const [remaining, titanium, missiles] = recover(firstHold, 1, site.Stock);
  assert.deepEqual([titanium, missiles, firstHold.Quantities[0]], [2, 1, 2]);
  assert.deepEqual([remaining.Titanium, remaining.Missiles], [2, 2]);
  const [full, extraTitanium, extraMissiles] = recover(firstHold, 0, remaining);
  assert.deepEqual([full.Titanium, full.Missiles, extraTitanium, extraMissiles], [2, 2, 0, 0]);
  const [empty, laterTitanium, laterMissiles] = recover(createCargo(16), 4, remaining);
  assert.deepEqual([empty.Titanium, empty.Missiles, laterTitanium, laterMissiles], [0, 0, 2, 2]);
});

test('fresh fields use sector density and ore richness independently of the rock type', () => {
  const frost = createField(768), scrapyard = createField(768);
  seedField(frost, rng(101), 420, sectors[index('kaltstrom')]);
  seedField(scrapyard, rng(101), 420, sectors[index('wracksaum')]);
  assert.equal(frost.Count, 294);
  assert.equal(scrapyard.Count, 525);
  assert.equal(new Set(frost.Variant.subarray(0, frost.Count)).size, 8);
  const oreShare = field => field.Ore.subarray(0, field.Count).filter(ore => ore > 0).length / field.Count;
  assert.ok(oreShare(frost) > oreShare(scrapyard) * 2);
});

import {
  WreckVoices_update as hearWrecks, WreckVoices_restore as restoreVoices, WreckVoices_saved as savedVoices,
  WreckVoices_state as voiceState, WreckVoices_recorded as recorded, WreckVoices_cutIfSending as cutVoice,
  WreckVoices_kept as keptVoices, WreckVoices_cutCount as cutCount, WreckVoices_valid as validVoices,
  WreckVoices_calls as wreckCalls, WreckVoices_maxMask as maxVoices,
} from '../build/WreckFields.js';
import { WreckFields_stocks as allStocks } from '../build/WreckFields.js';

test('every sector has a wreck, clear of its station and gates, and each wreck has a voice', () => {
  const ids = new Set();
  sectors.forEach((sector, i) => {
    const sites = forSector(i);
    assert.ok(sites.length >= 1, `${sector.Id} has a wreck`);
    for (const site of sites) {
      ids.add(site.Stock.Id);
      const far = p => Math.hypot(site.Position.X - p.X, site.Position.Y - p.Y, site.Position.Z - p.Z);
      assert.ok(far(sector.Outpost.Position) > sector.Outpost.ApproachRadius + 60, `${site.Stock.Id} clears the approach`);
      for (const gate of sector.Gates) assert.ok(far(gate.Position) > 300, `${site.Stock.Id} clears the gates`);
    }
  });
  assert.equal(ids.size, allStocks.length);
  for (const id of ids) assert.ok(wreckCalls.has ? wreckCalls.has(id) : true);
});

test('a distress call is secured by waiting in range and cut by salvaging early', () => {
  restoreVoices(0);
  const sites = forSector(index('wracksaum'));
  const [first, second] = sites;
  const near = offset(first.Position, 0, 0, 40), away = offset(first.Position, 0, 0, 400);
  assert.equal(hearWrecks(sites, away, 5), undefined);
  assert.equal(hearWrecks(sites, near, 3).tag, 0);            // Heard
  assert.ok(Math.abs(recorded(first.Stock.Id) - 3 / 7) < 1e-9);
  assert.equal(hearWrecks(sites, away, 60), undefined);       // leaving pauses the recording
  assert.equal(hearWrecks(sites, near, 4).tag, 1);            // Secured
  assert.equal(voiceState(first.Stock.Id).tag, 1);
  assert.equal(cutVoice(first.Stock.Id), false);              // a kept voice stays kept
  assert.equal(cutVoice(second.Stock.Id), true);
  assert.equal(cutVoice(second.Stock.Id), false);
  assert.equal(hearWrecks(sites, offset(second.Position, 0, 0, 10), 30), undefined);
  assert.deepEqual([keptVoices().length, cutCount()], [1, 1]);
  const mask = savedVoices();
  restoreVoices(0);
  assert.deepEqual([keptVoices().length, cutCount()], [0, 0]);
  restoreVoices(mask);
  assert.deepEqual([keptVoices()[0].Ship, cutCount()], ['Erzfrachter »Mittagsschicht«', 1]);
  assert.equal(validVoices(mask), true);
  assert.equal(validVoices(3), false);
  assert.equal(validVoices(maxVoices + 1), false);
  restoreVoices(0);
});
