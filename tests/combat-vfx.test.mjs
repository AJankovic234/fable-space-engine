import test from "node:test";
import assert from "node:assert/strict";
import {
  tier, recipe, budget, secondaryTime, finaleParticles, blastPoints, throttle, trailsAllowed, scaled,
  hitScale, hitSparks, boltStyles, boltStyleId,
} from "../build/CombatVfx.js";
import {
  WorldModule_create as createWorld, WorldModule_reset as resetWorld, ShipModule_reset as resetShip,
  LaserPoolModule_spawn as spawnBolt, LaserPoolModule_kill as killBolt, Physics_step as step,
  Damage_hitShip as hitShip, Effects_shipDestruction as destroy, Effects_cruiserBreakup as cruiserBreakup,
  Effects_sparks as sparks, ParticlePoolModule_spawn as spawnParticle,
} from "../build/PhysicsAndPool.js";
import { EnemyProfiles_configure as configure } from "../build/EnemyProfiles.js";
import { EnemyMeshes_hull as enemyHull } from "../build/EnemyMeshes.js";
import { Vec3, QuatModule_identity as identity } from "../build/Math3D.js";

const STEP = 1 / 120;
const [GLOW, DEBRIS, STREAK, RING, SMOKE, FUSE] = [0, 1, 2, 3, 4, 5];
const [LIGHT, HEAVY, CAPITAL, CRUISER] = [0, 1, 2, 3];
const FLASH_STRIDE = 8;

function emptyWorld() {
  const world = createWorld(16);
  resetWorld(world, 0);
  world.Player.Position = new Vec3(0, 0, 5000);
  world.Player.PrevPosition = world.Player.Position;
  return world;
}

function kinds(world) {
  const counts = [0, 0, 0, 0, 0, 0];
  for (let i = 0; i < world.Particles.Count; i++) counts[world.Particles.Kind[i]]++;
  return counts;
}

function enemy(world, slot, role, position) {
  const ship = world.Enemies[slot];
  configure(ship, role, 1);
  resetShip(ship, position, identity);
  return ship;
}

/** Runs the destruction of one enemy role to its end; returns what happened on the way. */
function watchDestruction(role) {
  const world = emptyWorld();
  const ship = enemy(world, 0, role, new Vec3(0, 0, 0));
  hitShip(world, ship, 1e6, false);
  const first = world.Particles.Count;
  const brightest = () => {
    let intensity = 0;
    for (let slot = 0; slot * FLASH_STRIDE < world.Flashes.length; slot++) intensity = Math.max(intensity, world.Flashes[slot * FLASH_STRIDE + 7]);
    return intensity;
  };
  let spawned = first, previous = first, lastBlast = 0, blasts = 0, flashes = 0, peak = first, lastIntensity = brightest();
  for (let i = 1; i <= 8 / STEP; i++) {
    step(world, STEP);
    const count = world.Particles.Count;
    if (count > previous) { spawned += count - previous; lastBlast = i * STEP; blasts++; }
    previous = count;
    peak = Math.max(peak, count);
    const intensity = brightest();
    if (intensity > lastIntensity + 0.5) flashes++;
    lastIntensity = intensity;
  }
  return { first, spawned, lastBlast, blasts, flashes, peak, left: world.Particles.Count };
}

test("destruction tiers follow the hull radius of the fleet", () => {
  const tiers = [0, 1, 2, 3, 4].map(role => tier(enemyHull(role).Radius));
  assert.deepEqual(tiers, [LIGHT, LIGHT, HEAVY, HEAVY, CAPITAL], "drone and scout pop, gunship and bomber break up, the corvette is capital");
  assert.equal(tier(3.4), LIGHT);
  assert.equal(tier(60), CRUISER);
  for (let t = LIGHT; t < CRUISER; t++) {
    assert.ok(budget(t + 1) > budget(t) * 1.5, `tier ${t + 1} is clearly bigger than tier ${t}`);
    assert.ok(recipe(t + 1).Duration > recipe(t).Duration);
    assert.ok(recipe(t + 1).Secondaries > recipe(t).Secondaries);
  }
});

test("large ships die in stages over two to four seconds", () => {
  assert.equal(recipe(LIGHT).Secondaries, 0);
  assert.equal(recipe(LIGHT).Finale, 0);
  assert.ok(recipe(HEAVY).Finale > 0.3 && recipe(HEAVY).Finale < 1.0, "heavy ships blow shortly after breaking up");
  for (const t of [CAPITAL, CRUISER]) {
    const r = recipe(t);
    assert.ok(r.Finale >= 2.0 && r.Finale <= 3.5, "core breach between 2 and 3.5 s");
    assert.ok(r.Duration >= 2.0 && r.Duration <= 4.0 && r.Duration > r.Finale);
    for (let i = 0; i < r.Secondaries; i++) {
      const time = secondaryTime(t, i);
      assert.ok(time > 0 && time < r.Finale, "secondaries lead up to the finale");
      if (i > 0) assert.ok(time > secondaryTime(t, i - 1));
    }
  }
});

test("each role's destruction stays inside its particle budget and plays its timeline", () => {
  const results = [0, 1, 2, 3, 4].map(watchDestruction);
  const tiers = [LIGHT, LIGHT, HEAVY, HEAVY, CAPITAL];
  results.forEach((result, role) => {
    const t = tiers[role];
    assert.ok(result.spawned <= budget(t), `role ${role}: ${result.spawned} particles within budget ${budget(t)}`);
    assert.ok(result.spawned >= budget(t) * 0.85, `role ${role}: the recipe is actually used (${result.spawned}/${budget(t)})`);
    assert.equal(result.left, 0, "everything burns out");
    assert.equal(result.blasts, recipe(t).Secondaries + (recipe(t).Finale > 0 ? 1 : 0), "every fuse fires exactly once");
    assert.ok(Math.abs(result.lastBlast - recipe(t).Finale) < 0.05, `role ${role}: finale at ${result.lastBlast}s`);
  });
  assert.ok(results[0].spawned < results[2].spawned && results[2].spawned < results[4].spawned, "bigger hulls, bigger explosions");
  assert.ok(results[4].flashes >= 6, `the corvette throws light flashes through its break-up (${results[4].flashes})`);
  assert.equal(results[0].flashes, 0, "a drone is one flash, set on the kill itself");
});

test("a small ship explodes at once with fire, sparks, debris and one ring", () => {
  const world = emptyWorld();
  hitShip(world, enemy(world, 0, 0, new Vec3(0, 0, 0)), 1e6, false);
  const [glow, debris, streak, ring, smoke, fuse] = kinds(world);
  const r = recipe(LIGHT);
  assert.equal(glow, r.Fire + r.Core + 1);
  assert.equal(streak, r.Sparks + r.Shards);
  assert.equal(debris, r.Debris);
  assert.deepEqual([ring, smoke, fuse], [1, 0, 0]);
});

test("heavy and capital ships add chunks, afterglow and fuses along the hull", () => {
  const world = emptyWorld();
  const ship = enemy(world, 0, 4, new Vec3(100, 0, 0));
  hitShip(world, ship, 1e6, false);
  const [, debris, , , smoke, fuse] = kinds(world);
  const r = recipe(CAPITAL);
  assert.equal(fuse, r.Secondaries + 1);
  assert.ok(smoke >= r.Smoke - blastPoints(CAPITAL) && smoke > 0);
  assert.ok(debris > r.Debris, "hull chunks on top of small debris");
  // Fuses sit along the hull axis (Z of the unrotated ship), not in a ball around the centre.
  let alongZ = 0, alongX = 0;
  for (let i = 0; i < world.Particles.Count; i++) {
    if (world.Particles.Kind[i] !== FUSE) continue;
    alongZ = Math.max(alongZ, Math.abs(world.Particles.Data[i * 15 + 2]));
    alongX = Math.max(alongX, Math.abs(world.Particles.Data[i * 15] - 100));
  }
  assert.ok(alongZ > alongX, "the chain runs bow to stern");
});

test("worst case stays inside the particle pool: cruiser plus a full wave dying together", () => {
  const world = emptyWorld();
  const roles = [4, 3, 3, 2, 2, 2, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0];
  cruiserBreakup(world, new Vec3(0, 0, -900), new Vec3(1, 0, 0), -170, 205, 37);
  roles.forEach((role, slot) => hitShip(world, enemy(world, slot, role, new Vec3(slot * 40, 0, 0)), 1e6, false));
  const planned = budget(CRUISER) + roles.reduce((sum, role) => sum + budget(tier(enemyHull(role).Radius)), 0);
  assert.ok(planned <= world.Particles.Capacity, `sum of budgets ${planned} fits the pool of ${world.Particles.Capacity}`);
  let peak = world.Particles.Count;
  for (let i = 0; i < 5 / STEP; i++) { step(world, STEP); peak = Math.max(peak, world.Particles.Count); }
  assert.ok(peak <= planned && peak < world.Particles.Capacity * 0.75, `peak ${peak} leaves headroom`);
  assert.ok(budget(CRUISER) < world.Particles.Capacity / 2);
});

test("a full pool drops effects instead of growing or failing", () => {
  const world = emptyWorld();
  for (let i = 0; i < world.Particles.Capacity; i++) spawnParticle(world.Particles, GLOW, 0, 0, 0, 0, 0, 0, 60, 1, 1, 1, 1);
  assert.equal(world.Particles.Count, world.Particles.Capacity);
  destroy(world, enemy(world, 0, 4, new Vec3(0, 0, 0)));
  sparks(world, 0, 0, 0, 20, 1, 1, 1);
  step(world, STEP);
  assert.equal(world.Particles.Count, world.Particles.Capacity);
});

test("cosmetic particles throttle as the pool fills", () => {
  assert.equal(throttle(0, 4000), 1);
  assert.equal(throttle(2400, 4000), 1);
  assert.ok(throttle(3200, 4000) < 0.7);
  assert.equal(throttle(4000, 4000), 0.25);
  assert.equal(trailsAllowed(2999, 4000), true);
  assert.equal(trailsAllowed(3000, 4000), false);
  assert.equal(scaled(20, 0.25), 5);
  assert.equal(scaled(2, 0.25), 1, "never less than one");

  const count = (fill) => {
    const world = emptyWorld();
    for (let i = 0; i < fill; i++) spawnParticle(world.Particles, GLOW, 0, 0, 0, 0, 0, 0, 60, 1, 1, 1, 1);
    sparks(world, 0, 0, 0, 20, 1, 1, 1);
    return world.Particles.Count - fill;
  };
  const capacity = emptyWorld().Particles.Capacity;
  assert.equal(count(0), 20);
  assert.equal(count(Math.floor(capacity * 0.6)), 20, "no throttling up to 60 % fill");
  assert.equal(count(capacity - 100), scaled(20, throttle(capacity - 100, capacity)));
  assert.ok(count(capacity - 100) <= 6);
});

test("hit effects grow with the target", () => {
  const radii = [0, 1, 2, 3, 4].map(role => enemyHull(role).Radius);
  for (let i = 1; i < radii.length; i++) {
    assert.ok(hitScale(radii[i]) > hitScale(radii[i - 1]));
    assert.ok(hitSparks(radii[i], 6) >= hitSparks(radii[i - 1], 6));
  }
  assert.ok(hitScale(radii[4]) > 2 * hitScale(radii[0]), "corvette hits read twice as large as drone hits");
  assert.ok(hitSparks(3, 1) >= 6 && hitSparks(200, 200) <= 22, "bounded per hit");
  assert.ok(hitSparks(7, 12) > hitSparks(7, 4), "heavier bolts throw more sparks");
});

function hitEffect(shield) {
  const world = emptyWorld();
  const ship = enemy(world, 0, 2, new Vec3(0, 0, -100));
  ship.Shield = shield ? ship.MaxShield : 0;
  ship.ShieldDelay = 10;
  spawnBolt(world.Lasers, 0, 0, -60, 0, 0, -520, 0, 0, 0, 1, 1, 0);
  for (let i = 0; i < 20 && world.Lasers.Count > 0; i++) step(world, STEP);
  assert.equal(world.Lasers.Count, 0, "the bolt hit");
  return { world, ship, counts: kinds(world) };
}

test("shield hits ripple on the bubble, hull hits throw sparks and shards", () => {
  const shield = hitEffect(true);
  assert.equal(shield.counts[RING], 2, "two rings on the shield surface");
  assert.equal(shield.counts[DEBRIS], 0);
  // The ring lies on the surface facing the shooter (+Z) and sits on the hull side that was hit.
  const p = shield.world.Particles;
  for (let i = 0; i < p.Count; i++) {
    if (p.Kind[i] !== RING) continue;
    assert.ok(p.Data[i * 15 + 14] > 0.5, "ring normal points back at the shooter");
    assert.ok(p.Data[i * 15 + 2] > -100, "ring sits on the near side of the ship");
  }

  const hull = hitEffect(false);
  assert.equal(hull.counts[RING], 0);
  assert.equal(hull.counts[DEBRIS], 1);
  assert.ok(hull.counts[STREAK] >= hitSparks(hull.ship.Radius, 1), "sparks plus glowing shards");
  let towardsShooter = 0, streaks = 0;
  for (let i = 0; i < hull.world.Particles.Count; i++) {
    if (hull.world.Particles.Kind[i] !== STREAK) continue;
    streaks++;
    if (hull.world.Particles.Data[i * 15 + 5] > 0) towardsShooter++;
  }
  assert.ok(towardsShooter > streaks * 0.6, "sparks spray off the surface, not through the ship");
});

test("bolt styles separate every source", () => {
  assert.equal(boltStyles.length, 10);
  const ids = {
    player: boltStyleId(1, true, false, false, 0),
    overdrive: boltStyleId(1, true, true, false, 0),
    focus: boltStyleId(1, true, false, true, 0),
    turret: boltStyleId(0, true, true, true, 0),
    battery: boltStyleId(2, false, false, false, 0),
    drone: boltStyleId(3, false, false, false, 0),
    scout: boltStyleId(4, false, false, false, 1),
    gunship: boltStyleId(5, false, false, false, 2),
    bomber: boltStyleId(6, false, false, false, 3),
    commander: boltStyleId(7, false, false, false, 4),
  };
  assert.equal(new Set(Object.values(ids)).size, 10, "ten sources, ten styles");
  assert.equal(boltStyleId(0, false, false, false, 0), ids.drone, "a loaded enemy bolt falls back to the plain hostile style");
  assert.equal(boltStyleId(3, false, false, false, 99), ids.commander, "roles are clamped");

  const style = (name) => boltStyles[ids[name]];
  const hue = (s) => [s.R, s.G, s.B];
  for (const name of Object.keys(ids)) {
    const s = style(name);
    assert.ok(s.CoreR >= s.R * 0.5 && s.CoreR + s.CoreG + s.CoreB > s.R + s.G + s.B, `${name}: the core is brighter than the fringe`);
    assert.ok(s.Thickness > 0.3 && s.Length > 8 && s.Halo > 1 && s.Muzzle > 1);
  }
  for (const a of Object.keys(ids)) for (const b of Object.keys(ids)) {
    if (a >= b) continue;
    const [x, y] = [style(a), style(b)];
    const colour = Math.hypot(...hue(x).map((v, i) => v - hue(y)[i]));
    const shape = Math.abs(x.Thickness - y.Thickness) / Math.max(x.Thickness, y.Thickness);
    assert.ok(colour > 0.3 || shape > 0.3, `${a} and ${b} differ in colour or weight`);
  }
  // Friend and foe never share a hue family: player weapons are cool, hostile fire is warm.
  for (const name of ["player", "overdrive", "focus", "turret"]) assert.ok(style(name).R < 1 && Math.max(style(name).G, style(name).B) > 1.5, name);
  for (const name of ["battery", "drone", "scout", "gunship", "bomber", "commander"]) assert.ok(style(name).R > 1.8, name);
  assert.ok(style("focus").B > style("focus").G * 3 && style("focus").R > 0.8, "the pulse-focus kit is violet");
  assert.ok(style("battery").Thickness > 2 * style("commander").Thickness, "the cruiser battery dwarfs ship guns");
});

test("bolts remember who fired them and keep that through pool compaction", () => {
  const world = emptyWorld();
  world.Player.Controls.Fire = true;
  const foe = enemy(world, 5, 2, new Vec3(0, 0, 2000));
  foe.Controls.Fire = true;
  step(world, STEP);
  assert.equal(world.Lasers.Count, 4);
  const owners = Array.from(world.Lasers.Owner.slice(0, 4));
  assert.deepEqual(owners, [1, 1, 3 + 5, 3 + 5], "player ship, then enemy slot 5");
  killBolt(world.Lasers, 0);
  assert.deepEqual(Array.from(world.Lasers.Owner.slice(0, 3)), [8, 1, 8], "swap-remove moves the owner along");
  spawnBolt(world.Lasers, 0, 0, 0, 0, 0, -520, 0, 0, 0, 1, 10, 0);
  assert.equal(world.Lasers.Owner[3], 0, "a plain spawn (turret, loaded save) has no owner");
  // Firing lights the muzzle: one flash slot carries the shot.
  let lit = 0;
  for (let slot = 0; slot * FLASH_STRIDE < world.Flashes.length; slot++) if (world.Flashes[slot * FLASH_STRIDE + 7] > 0) lit++;
  assert.equal(lit, 2);
  assert.ok(world.Flashes.length / FLASH_STRIDE <= 6, "flashes leave point lights for shields and engines");
});

test("boost and fast enemies leave engine trails, slow ones do not", () => {
  const trail = (setup) => {
    const world = emptyWorld();
    setup(world);
    for (let i = 0; i < 30; i++) step(world, STEP);
    return world.Particles.Count;
  };
  assert.equal(trail(() => {}), 0, "idle: nothing");
  assert.ok(trail(world => { world.Player.Boost = 1; world.Player.Controls.Boost = true; world.Player.Controls.Throttle = 1; }) >= 8, "player boost");
  const fast = trail(world => { const e = enemy(world, 0, 1, new Vec3(0, 0, 0)); e.Velocity = new Vec3(0, 0, -60); e.Controls.Throttle = 1; e.Controls.Boost = true; e.Boost = 1; });
  const slow = trail(world => { const e = enemy(world, 0, 3, new Vec3(0, 0, 0)); e.Velocity = new Vec3(0, 0, -10); });
  assert.ok(fast >= 3, `fast scout trails (${fast})`);
  assert.equal(slow, 0);
  let filled = 0;
  const crowded = trail(world => {
    filled = Math.ceil(world.Particles.Capacity * 0.8);
    for (let i = 0; i < filled; i++) spawnParticle(world.Particles, GLOW, 0, 0, 0, 0, 0, 0, 60, 1, 1, 1, 1);
    world.Player.Boost = 1; world.Player.Controls.Boost = true; world.Player.Controls.Throttle = 1;
  });
  assert.equal(crowded, filled, "no trails once the pool is three quarters full");
  assert.ok(finaleParticles(CAPITAL) > finaleParticles(HEAVY) && finaleParticles(LIGHT) === 0);
});
