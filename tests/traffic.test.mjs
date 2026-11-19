import test from 'node:test';
import assert from 'node:assert/strict';
import { models, manifests, shipPose, inShippingLane, capacity, driveCapacity, lampCapacity } from '../build/Traffic.js';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { forSector, move } from '../build/StationCollision.js';
import { CapitalFlight_position as cruiser, CapitalFlight_plates as armour } from '../build/CapitalFlight.js';
import { Vec3 } from '../build/Math3D.js';
import { sites, roles, actor, clock, phase, period, bridgeExtension, linerScale, transmission } from '../build/Seizure.js';

const distance = (a, b) => Math.hypot(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
const hullRadii = models.map(model => {
  const { Vertices: v } = model.Mesh();
  let radius = 0;
  for (let i=0; i<v.length; i+=10) radius = Math.max(radius, Math.hypot(v[i],v[i+1],v[i+2]));
  return radius;
});

test('traffic models have lit geometry, with lamps and exhausts on the modelled hull', () => {
  for (const [i,model] of models.entries()) {
    assert.ok(hullRadii[i] <= model.Radius && hullRadii[i]+1 > model.Radius, model.Name);
    const {Vertices: v, Indices: indices} = model.Mesh();
    assert.ok(indices instanceof Uint16Array && indices.every(i => i < v.length/10));
    assert.ok(v.every(Number.isFinite));
    let emissive = 0;
    for (let i=0; i<v.length; i+=10) {
      assert.ok(Math.abs(Math.hypot(v[i+3], v[i+4], v[i+5])-1) < 0.0001, model.Name);
      if (v[i+9] > 0) emissive++;
    }
    assert.ok(emissive > 0);
    function nearest(point) {
      let gap = Infinity;
      for (let i=0; i<v.length; i+=10)
        gap = Math.min(gap, Math.hypot(v[i]-point.X,v[i+1]-point.Y,v[i+2]-point.Z));
      return gap;
    }
    assert.equal(model.Lamps.length, 2);
    assert.ok(model.Lamps[0].X < 0 && model.Lamps[1].X > 0);
    for (const lamp of model.Lamps) assert.ok(nearest(lamp) < 1, model.Name);
    for (const [socket,radius] of model.Drives)
      assert.ok(socket.Z > 0 && nearest(socket) <= radius + 0.05, model.Name);
  }
});

test('sector services fit their draw and exhaust batches and the Bastion retains an escort on each wing', () => {
  const classes = new Set();
  for (const flights of manifests) {
    const drives = flights.reduce((sum, f) => sum + models[f.Class].Drives.length, 0);
    assert.ok(flights.length <= capacity && drives <= driveCapacity && drives+2*flights.length <= lampCapacity);
    for (const flight of flights) classes.add(flight.Class);
  }
  // Doppel-A uses a single intersector duty roster; the remaining classes fly local timetables.
  assert.deepEqual([...classes].sort(), [0,1,2,3,4]);
  assert.equal(models[5].Name, 'D/AA · Doppel-A');
  const bastion = manifests[sectors.findIndex(s => s.Id === 'morgenlicht-bastion')];
  const [lead, port, starboard] = bastion.filter(f => models[f.Class].Aether);
  assert.ok(port.WingOffset.X < 0 && starboard.WingOffset.X > 0);
  for (let i=0; i<=72; i++) {
    const clock = lead.Lane.Period*i/72;
    const [p, q] = shipPose(lead, clock);
    for (const wing of [port, starboard]) {
      const [w,r] = shipPose(wing, clock);
      assert.deepEqual(r,q);
      assert.ok(Math.abs(distance(w,p) - Math.hypot(wing.WingOffset.X,wing.WingOffset.Y,wing.WingOffset.Z)) < 1e-6);
      assert.ok(distance(w,p) > hullRadii[lead.Class]+hullRadii[wing.Class]);
    }
  }
});

test('new flight paths loop clear of ports, gate apertures, wrecks and the Helios blockade cruiser', () => {
  for (const [s,flights] of manifests.entries()) {
    const hull = forSector(s), sector = sectors[s];
    assert.equal(inShippingLane(s,new Vec3(0,5000,0),90),false);
    for (const flight of flights.filter(f => f.Class !== 0)) {
      const radius = hullRadii[flight.Class], name = sector.Name+' / '+models[flight.Class].Name;
      const [initial] = shipPose(flight,0);
      let prev = initial;
      for (let i=1; i<=240; i++) {
        const [p] = shipPose(flight,flight.Lane.Period*i/240);
        const edge = new Vec3(p.X+radius+5,p.Y,p.Z);
        assert.ok(inShippingLane(s,edge,5),name);
        assert.equal(move(hull,radius,prev,p,new Vec3(0,0,-1)),undefined,name);
        for (const gate of sector.Gates) assert.ok(distance(p,gate.Position) > gate.TriggerRadius+radius,name);
        if (p.Z >= sector.Outpost.Position.Z)
          assert.ok(Math.hypot(p.X-sector.Outpost.Position.X,p.Y-sector.Outpost.Position.Y) > radius+35,name);
        if (sector.Id === 'slag-refinery') {
          for (const [c,h] of armour) {
            const gap = axis => Math.max(0, Math.abs(p[axis]-cruiser[axis]-c[axis])-h[axis]);
            assert.ok(Math.hypot(gap('X'),gap('Y'),gap('Z')) > radius,name);
          }
        }
        prev = p;
      }
      assert.ok(distance(initial,prev) < 1e-6,name);
    }
  }
});

test('no traffic hull wears showroom paint: belt ships are grimy and AETHER is cold', () => {
  for (const model of models) {
    const v = model.Mesh().Vertices;
    let paint = 0, luminance = 0, peak = 0;
    for (let i=0; i<v.length; i+=10) {
      if (v[i+9] !== 0) continue;
      paint++;
      luminance += 0.2126*v[i+6] + 0.7152*v[i+7] + 0.0722*v[i+8];
      peak = Math.max(peak, v[i+6], v[i+7], v[i+8]);
    }
    assert.ok(paint > 0 && luminance/paint < 0.32, `${model.Name}: mean paint luminance ${luminance/paint}`);
    assert.ok(peak <= 0.6, `${model.Name}: brightest paint channel ${peak}`);
  }
});

test('requisition stops the ferry, attaches the corvette and hands over its registry before departure', () => {
  const active = sectors.map((s,i) => sites[i] && s.Id).filter(Boolean);
  assert.deepEqual(active, ['slag-refinery', 'kepler-yard']);
  for (const site of sites.filter(Boolean)) {
    const before = actor(site, 37, roles[0]);
    assert.equal(before.Thrust, 0);
    assert.deepEqual(actor(site, 53, roles[0]), before);
    assert.deepEqual(actor(site, 53, roles[2]), actor(site, 37, roles[2]));
    assert.equal(bridgeExtension(32), 0);
    assert.equal(bridgeExtension(35), 0.5);
    assert.equal(bridgeExtension(49), 1);
    assert.equal(bridgeExtension(57), 0);
    assert.equal(phase(48.9).tag, 2);
    assert.equal(phase(49).tag, 3);
    const t = period * 30 - site.Offset + 50.1;
    assert.equal(clock(site, t), clock(site, t+period));
    assert.equal(transmission(site, t, 0.2, before.Position)[2], 0);
    assert.equal(transmission(site, t+0.2, 0.2, before.Position), undefined);
    assert.equal(transmission(site, t, 0.2, new Vec3(0, 5000, 0)), undefined);
    for (const role of roles) {
      assert.ok(actor(site, 74, role).Thrust > 0);
      assert.equal(actor(site, 100, role), undefined);
    }
  }
});

test('both gate crossings reserve the scaled hulls and clear the station works and blockade cruiser', () => {
  const radii = [models[2].Radius*linerScale, models[3].Radius, models[4].Radius];
  for (const [sector,site] of sites.entries()) {
    if (!site) continue;
    const hull = forSector(sector);
    for (const [index,role] of roles.entries()) {
      const radius = radii[index];
      for (let t=0; t<93; t+=0.5) {
        const first = actor(site,t,role), next = actor(site,t+0.5,role);
        if (!first || !next) continue;
        const p = first.Position;
        assert.ok(inShippingLane(sector,new Vec3(p.X+radius,p.Y,p.Z),0));
        assert.equal(move(hull,radius,p,next.Position,new Vec3(0,0,-1)),undefined);
        if (sectors[sector].Id === 'slag-refinery') {
          for (const [c,h] of armour) {
            const gap = axis => Math.max(0, Math.abs(p[axis]-cruiser[axis]-c[axis])-h[axis]);
            assert.ok(Math.hypot(gap('X'),gap('Y'),gap('Z')) > radius);
          }
        }
      }
    }
  }
});
