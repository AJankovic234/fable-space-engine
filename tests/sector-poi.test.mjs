import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { sites as outerSites } from '../build/OuterRoutes.js';
import { sites, bySector, empty, scan, flight, target, doneAt, kindIndex } from '../build/SectorPoi.js';
import { TradeSystem_createCargo as hold, TradeSystem_freeVolume as free, TradeSystem_quantityOf as quantity } from '../build/TradeSystem.js';

const offset = (site,x=0,y=0,z=0) => new Vec3(site.Position.X+x,site.Position.Y+y,site.Position.Z+z);
const distance = (a,b) => Math.hypot(a.X-b.X,a.Y-b.Y,a.Z-b.Z);

test('each sector has two distinct reachable POIs clear of the existing expedition and gates', () => {
  assert.deepEqual(sites.map(s=>s.Id),Array.from({length:18},(_,i)=>i));
  assert.equal(new Set(sites.map(s=>s.Name)).size,sites.length);
  for (const [i,sector] of sectors.entries()) {
    const [near,far] = bySector[i];
    assert.notEqual(kindIndex(near.Kind),kindIndex(far.Kind));
    assert.ok(distance(near.Position,sector.Outpost.Position)<2000);
    assert.ok(distance(far.Position,sector.Outpost.Position)>3000);
    for (const site of [near,far]) {
      assert.equal(site.Sector,i);
      assert.ok(distance(site.Position,outerSites[i].Archive)>950,site.Name);
      assert.ok(distance(site.Position,outerSites[i].Origin)>1150,site.Name);
      for (const gate of sector.Gates) assert.ok(distance(site.Position,gate.Position)>350,site.Name);
    }
  }
});

test('depots transfer the whole consignment exactly once and account for reserved freight tonnage', () => {
  const site=sites[3], cargo=hold(4);
  cargo.Freight={Tonnes:1};
  assert.equal(scan(empty,site,site.Position,0,cargo).name,'Advice');
  assert.equal(free(cargo),3);
  cargo.Freight=undefined;
  const take=scan({...empty,Pinned:site.Id},site,offset(site,90),0,cargo);
  assert.equal(take.name,'Completed');
  assert.equal(quantity(cargo,2),2);
  assert.equal(free(cargo),0);
  assert.equal(take.fields[0].Pinned,undefined);
  assert.equal(scan(take.fields[0],site,site.Position,0,cargo).name,'Advice');
  assert.equal(quantity(cargo,2),2);
});

test('each receiver pair requires two separate physical scans; a restored first fix selects the east head', () => {
  const cargo=hold(10);
  for (const site of sites.filter(s=>kindIndex(s.Kind)===1)) {
    assert.equal(scan(empty,site,offset(site,85),0,cargo).name,'Advice');
    assert.equal(scan(empty,site,site.Position,0,cargo).name,'Advice');
    const west=scan(empty,site,offset(site,-85),20,cargo);
    assert.equal(west.name,'FirstReading');
    const saved=JSON.parse(JSON.stringify(west.fields[0]));
    assert.equal(target(saved,site).X,site.Position.X+85);
    assert.equal(scan(saved,site,offset(site,-85),0,cargo).name,'Advice');
    const east=scan(saved,site,offset(site,85),0,cargo);
    assert.equal(east.name,'Completed');
    assert.ok(doneAt(east.fields[0],site));
    assert.equal(east.fields[0].WestFix,0);
  }
});

test('service couplers require low speed and proximity; calibration rings use a swept hull-sized aperture', () => {
  const station=sites[2],ring=sites[1],cargo=hold(10);
  assert.equal(scan(empty,station,station.Position,13,cargo).name,'Advice');
  assert.equal(scan(empty,station,offset(station,91),10,cargo).name,'Advice');
  assert.equal(scan(empty,station,offset(station,90),12,cargo).name,'Completed');
  assert.equal(scan(empty,station,offset(station,250),0,cargo).name,'OutOfRange');
  assert.equal(scan(empty,ring,ring.Position,0,cargo).name,'Advice');
  assert.equal(flight(empty,ring,offset(ring,40,0,-100),offset(ring,40,0,100),6).name,'OutOfRange');
  assert.equal(flight(empty,ring,offset(ring,0,0,5),offset(ring,0,0,15),4).name,'OutOfRange');
  const run=flight({...empty,Pinned:4},ring,offset(ring,0,0,300),offset(ring,0,0,-150),6);
  assert.equal(run.name,'Completed');
  assert.equal(run.fields[0].Pinned,4);
  assert.equal(flight(run.fields[0],ring,offset(ring,0,0,-10),offset(ring,0,0,10),4).name,'OutOfRange');
});
