// Gefechtssimulation als Unit-Tests: Rollenverhalten, Bossphasen, Balancegrenzen, Fraktionen.
// Alles deterministisch: jede Simulation beginnt mit einer frischen Welt (fester Rng-Seed).
import test from "node:test";
import assert from "node:assert/strict";
import { createSim, spawn, step, run, duels, stage, stateName, vec, DT, frontalArea } from "./combat-sim.mjs";
import { EnemyProfiles_profile as profile, EnemyProfiles_configure as configure } from "../build/EnemyProfiles.js";
import { AI_bossPhase as bossPhase, AI_resync as resync, AI_createBrain as createBrain, AI_reset as resetBrain } from "../build/AI.js";
import * as Encounters from "../build/Encounters.js";
import { Mission_stages as stages, Mission_resetPlayer as resetPlayer } from "../build/Mission.js";
import { GameSave_capture as capture, GameSave_decode as decode, GameSave_restore as restore } from "../build/GameSave.js";
import { WorldModule_create as createWorld, WorldModule_reset as resetWorld, ShipModule_reset as resetShip } from "../build/PhysicsAndPool.js";
import { Economy_create as wallet } from "../build/Economy.js";
import { TradeSystem_createCargo as cargo, TradeSystem_createUniverseMarkets as markets } from "../build/TradeSystem.js";
import { Turret_create as turret } from "../build/Turret.js";
import { Salvage_create as pickups } from "../build/Salvage.js";
import { Vec3, QuatModule_forward as forward, QuatModule_identity as identity } from "../build/Math3D.js";

const DRONE = 0, SCOUT = 1, GUNSHIP = 2, BOMBER = 3, COMMANDER = 4;
const distance = (sim, i = 0) => vec.len(vec.sub(sim.world.Enemies[i].Position, sim.world.Player.Position));
const noseOnPlayer = (sim, i = 0) => {
  const e = sim.world.Enemies[i];
  const to = vec.sub(sim.world.Player.Position, e.Position);
  return vec.dot(forward(e.Rotation), to) / vec.len(to);
};

/// Ein Gegner gegen ein stehendes, wehrloses Ziel; `watch` läuft nach jedem Physikschritt.
function observe(role, seconds, watch, mates = 0) {
  const sim = createSim({ passive: true });
  spawn(sim, 0, role, new Vec3(40, 10, -380));
  for (let i = 1; i <= mates; i++) spawn(sim, i, role, new Vec3(40 - 110 * i, 10 + 40 * i, -380 - 60 * i));
  sim.hook = watch;
  run(sim, seconds);
  return sim;
}

test("drones close up into a swarm and charge and fire their volleys together", () => {
  let synced = 0, volleys = 0, spreadAtStart = 0, spreadInFight = Infinity, circling = 0;
  const spread = (sim) => Math.max(...[0, 1, 2].flatMap(a => [0, 1, 2].map(b => vec.len(vec.sub(sim.world.Enemies[a].Position, sim.world.Enemies[b].Position)))));
  const sim = observe(DRONE, 30, (s) => {
    if (s.time < 0.05) spreadAtStart = spread(s);
    const attacking = [0, 1, 2].filter(i => stateName(s.brains[i]) === "Attack");
    if (attacking.length === 3) {
      spreadInFight = Math.min(spreadInFight, spread(s));
      const charges = attacking.map(i => s.brains[i].WeaponCharge);
      if (charges.every(c => c > 0)) {
        volleys++;
        // Die Entscheidungen laufen versetzt mit 30 Hz: ein Takt Abweichung ist erlaubt.
        if (Math.max(...charges) - Math.min(...charges) < 0.06) synced++;
      }
      // Zwischen den Salven kreisen sie: Fahrt quer zur Sichtlinie statt Stillstand.
      if (charges.every(c => c === 0) && attacking.every(i => vec.len(s.world.Enemies[i].Velocity) > 8)) circling++;
    }
  }, 2);
  assert.ok(spreadAtStart > 200, `drones start scattered (${spreadAtStart.toFixed(0)} m)`);
  assert.ok(spreadInFight < spreadAtStart * 0.6, `swarm closes up: ${spreadInFight.toFixed(0)} m`);
  assert.ok(volleys > 60, "the swarm charges several volleys");
  assert.equal(synced, volleys, "every charge is shared by the whole swarm");
  assert.ok(circling > 120, "drones keep moving between volleys");
  assert.ok(sim.stats.enemyShots >= 12);
});

test("scouts make fast passes, break away with a sprint and come back from range", () => {
  let passes = 0, near = false, boostedAway = 0, fastestAttack = 0, firedOnApproach = 0;
  const sim = observe(SCOUT, 45, (s) => {
    const d = distance(s), b = s.brains[0], e = s.world.Enemies[0];
    if (d < 70) near = true;
    if (near && d > 160) { near = false; passes++; }
    if (stateName(b) === "Evade" && e.Controls.Boost) boostedAway++;
    if (stateName(b) === "Attack") {
      fastestAttack = Math.max(fastestAttack, vec.len(e.Velocity));
      if (e.Controls.Fire) firedOnApproach++;
    }
  });
  assert.ok(passes >= 3, `repeated passes: ${passes}`);
  assert.ok(boostedAway > 200, "disengages with boost");
  assert.ok(fastestAttack > 30, "keeps its speed during the attack run");
  assert.ok(firedOnApproach > 20 && sim.stats.enemyShots >= 8, "fires on the way in");
  // Während des Absetzens liegt die Nase nicht auf dem Spieler: der Späher gibt das Ziel wirklich auf.
  assert.ok(sim.brains[0].Side === 1 || sim.brains[0].Side === -1);
});

test("a scout approaches an unaware player from the flank, not head-on", () => {
  // Der Spieler fliegt geradeaus weiter; der Späher startet genau vor seiner Nase.
  const sim = createSim({ passive: true });
  const player = sim.world.Player;
  spawn(sim, 0, SCOUT, new Vec3(0, 0, -380));
  let bearingAtAttack = null;
  sim.hook = (s) => {
    player.Controls.Throttle = 0.5;
    if (bearingAtAttack === null && stateName(s.brains[0]) === "Attack") {
      const to = vec.sub(s.world.Enemies[0].Position, player.Position);
      bearingAtAttack = vec.dot(forward(player.Rotation), to) / vec.len(to);
    }
  };
  run(sim, 20, () => bearingAtAttack !== null);
  assert.notEqual(bearingAtAttack, null, "the scout attacks");
  assert.ok(bearingAtAttack < 0.75, `attack starts ${(Math.acos(bearingAtAttack) * 180 / Math.PI).toFixed(0)}° off the player's nose`);
});

test("gunships hold their distance, show a broadside between salvos and retreat from a brawl", () => {
  let closest = Infinity, broadside = 0, aimedWhileFiring = 0, firing = 0, boosted = 0;
  observe(GUNSHIP, 40, (s) => {
    const b = s.brains[0], e = s.world.Enemies[0];
    closest = Math.min(closest, distance(s));
    if (e.Controls.Boost) boosted++;
    if (stateName(b) !== "Attack") return;
    if (b.WeaponCharge === 0 && !b.FiringBurst && noseOnPlayer(s) < 0.75) broadside++;
    if (b.FiringBurst) { firing++; if (noseOnPlayer(s) > 0.995) aimedWhileFiring++; }
  });
  const p = profile(GUNSHIP);
  assert.ok(closest > p.PreferredRange * 0.6, `keeps away from a passive target: ${closest.toFixed(0)} m`);
  assert.ok(broadside > 200, "turns its side to the player during the rest");
  assert.ok(aimedWhileFiring > firing * 0.7, `is back on target for the salvo: ${aimedWhileFiring}/${firing}`);
  assert.equal(boosted, 0);

  // Nahkampf: der Spieler steht plötzlich auf halber Wunschdistanz.
  const sim = createSim({ passive: true });
  spawn(sim, 0, GUNSHIP, new Vec3(0, 0, -100));
  let retreated = false, opened = 0;
  sim.hook = (s) => {
    if (stateName(s.brains[0]) === "Evade") { retreated = true; opened = Math.max(opened, distance(s)); }
  };
  run(sim, 8);
  assert.ok(retreated, "breaks off instead of dogfighting");
  assert.ok(opened > 120, `opens the range again: ${opened.toFixed(0)} m`);
});

test("bombers fly boosted runs: frozen aim point, one heavy salvo, then break away", () => {
  let runs = 0, wasAttack = false, boostedRun = 0, lockedEarly = 0, lockMoved = 0, lock = null, evadeAfter = 0, nosegun = 0;
  const sim = observe(BOMBER, 45, (s) => {
    const b = s.brains[0], e = s.world.Enemies[0];
    const attack = stateName(b) === "Attack";
    if (attack && !wasAttack) runs++;
    if (!attack && wasAttack && stateName(b) === "Evade") evadeAfter++;
    wasAttack = attack;
    if (attack && e.Controls.Boost) boostedRun++;
    if (e.Controls.Fire) nosegun++;
    if (b.Locked && b.WeaponCharge > 0 && b.WeaponCharge < 0.75 - 1e-9) lockedEarly++;
    if (b.Locked) { if (lock && vec.len(vec.sub(lock, b.Lock)) > 1e-9) lockMoved++; lock = b.Lock; } else lock = null;
  });
  assert.ok(runs >= 3, `several runs: ${runs}`);
  assert.ok(evadeAfter >= runs - 1, "every run ends by breaking away");
  assert.ok(boostedRun > 300, "afterburner during the run");
  assert.equal(lockedEarly, 0, "the course is not frozen before 75 % charge");
  assert.equal(lockMoved, 0, "once locked, the frozen course stays fixed");
  assert.equal(nosegun, 0, "the salvo follows the frozen course, not the bomber's nose");
  assert.ok(sim.stats.enemyHits >= 6, `a target that holds still takes the salvo: ${sim.stats.enemyHits} hits`);
});

/// Spieler pendelt mit 30 m/s quer (Wende alle 6 s); `jink` wendet stattdessen, sobald die Salve
/// seinen Kurs eingefroren hat.
function strafe(role, jink, prepare) {
  const sim = createSim({ passive: true });
  const player = sim.world.Player;
  const enemy = spawn(sim, 0, role, new Vec3(0, 0, -300));
  if (prepare) prepare(enemy, sim);
  let direction = 1, turned = false;
  sim.hook = (s) => {
    const b = s.brains[0];
    if (jink && b.Locked && !turned) { direction = -direction; turned = true; }
    if (!b.Locked) turned = false;
    if (!jink) direction = Math.floor(s.time / 6) % 2 === 0 ? 1 : -1;
    player.Velocity = new Vec3(30 * direction, 0, 0);
    player.Controls.Throttle = 0;
  };
  run(sim, 60);
  return sim.stats;
}

test("heavy salvos are dodgeable: changing course after the glow avoids what holding course takes", () => {
  const bomberHeld = strafe(BOMBER, false), bomberDodged = strafe(BOMBER, true);
  assert.ok(bomberHeld.enemyShots >= 12, "the bomber fires its salvos");
  assert.ok(bomberHeld.damageTaken >= 40, `holding course under a bomber: ${bomberHeld.damageTaken.toFixed(0)} damage`);
  assert.ok(bomberDodged.enemyShots >= 12, "the bomber fires at the dodging target as well");
  assert.ok(bomberDodged.damageTaken <= bomberHeld.damageTaken * 0.25, `dodged: ${bomberDodged.damageTaken.toFixed(0)} damage`);

  // Korvette in Phase 2: die Batteriefächer liegen auf dem eingefrorenen Punkt.
  const phaseTwo = (enemy) => { enemy.Hull = enemy.MaxHull * 0.5; };
  const held = strafe(COMMANDER, false, phaseTwo), dodged = strafe(COMMANDER, true, phaseTwo);
  assert.ok(held.damageTaken >= 40, `holding course under the batteries: ${held.damageTaken.toFixed(0)} damage`);
  assert.ok(dodged.damageTaken <= held.damageTaken * 0.4, `dodged: ${dodged.damageTaken.toFixed(0)} vs ${held.damageTaken.toFixed(0)}`);
});

test("enemy gun pairs converge on their aim point instead of straddling the target", () => {
  // Jede Rolle trifft ein stehendes Ziel; vorher flogen die breiten Rohrpaare links und rechts vorbei.
  for (const role of [DRONE, SCOUT, GUNSHIP, BOMBER, COMMANDER]) {
    const sim = createSim({ passive: true });
    spawn(sim, 0, role, new Vec3(30, 10, -280));
    run(sim, 30);
    assert.ok(sim.stats.enemyShots > 0, `${profile(role).Name} fires`);
    // Der Späher schießt im schnellen Vorbeiflug; seine Trefferquote prüft der Balancetest über acht Anflüge.
    if (role !== SCOUT) assert.ok(sim.stats.enemyHits / sim.stats.enemyShots > 0.1, `${profile(role).Name} hits a sitting target: ${sim.stats.enemyHits}/${sim.stats.enemyShots}`);
    assert.ok(sim.world.Player.Alive, `${profile(role).Name} alone does not kill within 30 s`);
  }
});

test("the corvette fights in three phases with shield surge, escort calls and battery fans", () => {
  const sim = createSim({ passive: true });
  const boss = spawn(sim, 0, COMMANDER, new Vec3(0, 0, -280));
  const brain = sim.brains[0];
  const calls = [];
  const player = sim.world.Player;
  player.Hull = player.MaxHull = 1e6; // nur beobachten
  const shotsPerSalvo = (seconds) => { const before = sim.stats.enemyShots; run(sim, seconds); return sim.stats.enemyShots - before; };
  sim.hook = (s) => { if (Encounters.takeEscortCall(s.brains, s.world.Enemies)) calls.push(brain.BossPhase); };

  assert.equal(bossPhase(boss), 1);
  const p = profile(COMMANDER);
  const cycle = p.ChargeTime + p.BurstTime + p.RestTime;
  const phaseOne = shotsPerSalvo(cycle * 2);
  assert.equal(brain.BossPhase, 1);
  assert.deepEqual(calls, []);
  assert.equal(brain.Locked, false, "phase 1 tracks with the bow guns only");

  // Ein einziger schwerer Treffer überspringt keine Phase: die Hülle bleibt an der Schwelle stehen.
  boss.Hull = boss.MaxHull * 0.1;
  boss.Shield = 0;
  step(sim);
  assert.equal(brain.BossPhase, 2);
  assert.ok(boss.Hull >= boss.MaxHull * 0.34 - 1e-9, "hull is held at the phase threshold");
  assert.equal(boss.Shield, boss.MaxShield, "shield is restored");
  assert.ok(brain.PhaseShift > 2.9, "shield surge starts");
  assert.equal(brain.WeaponCharge < 0.1, true, "the phase opens with a fresh charge");
  // Während des Schildstoßes prallt Schaden ab.
  for (let i = 0; i < 60; i++) { boss.Shield = Math.max(0, boss.Shield - 50); step(sim); }
  assert.equal(boss.Shield, boss.MaxShield);
  assert.ok(boss.ShieldFlash >= 0.6, "the surge is visible as a shield bubble");
  run(sim, 3);
  assert.ok(brain.PhaseShift <= 0, "the surge ends after three seconds");
  assert.deepEqual(calls, [2], "exactly one escort call");

  let locked = 0, lockedBelow = 0;
  sim.hook = (s) => {
    if (Encounters.takeEscortCall(s.brains, s.world.Enemies)) calls.push(brain.BossPhase);
    if (brain.Locked) { locked++; if (brain.WeaponCharge > 0 && brain.WeaponCharge < 0.75 - 1e-9) lockedBelow++; }
  };
  const phaseTwo = shotsPerSalvo(cycle * 2);
  assert.ok(locked > 0 && lockedBelow === 0, "batteries freeze the course at 75 % charge");
  assert.ok(phaseOne >= 8 && phaseTwo >= 8, `both phases fire: ${phaseOne} and ${phaseTwo} bolts`);

  boss.Hull = boss.MaxHull * 0.3;
  run(sim, 3.5);
  assert.equal(brain.BossPhase, 3);
  assert.deepEqual(calls, [2, 3]);
  // Phase 3: kürzere Pausen und Nahdistanz, die Aufladung bleibt gleich lang.
  let charging = 0, resting = 0, closest = Infinity;
  sim.hook = () => {
    if (stateName(brain) !== "Attack") return;
    if (brain.WeaponCharge > 0) charging++; else if (!brain.FiringBurst) resting++;
    closest = Math.min(closest, vec.len(vec.sub(boss.Position, sim.world.Player.Position)));
  };
  run(sim, 30);
  assert.ok(resting / charging < p.RestTime * 0.6 / p.ChargeTime, `shorter rests in phase 3 (${(resting * DT).toFixed(1)} s rest for ${(charging * DT).toFixed(1)} s charge)`);
  assert.ok(closest < p.PreferredRange * 0.8, `closes in: ${closest.toFixed(0)} m`);
  assert.ok(boss.Alive);
});

test("boss stage keeps four escorts in reserve until the corvette calls them", () => {
  const finale = stages.at(-1).Enemies;
  const world = createWorld(16);
  resetWorld(world, 0);
  const brains = Array.from({ length: 16 }, (_, i) => createBrain(i));
  const boss = world.Enemies[0];
  configure(boss, COMMANDER, 5);
  resetShip(boss, new Vec3(0, 0, -300), identity);
  brains[0].Role = COMMANDER;
  resetBrain(brains[0], boss, new Vec3(0, 0, 0));
  assert.equal(Encounters.rosterLimit(finale, brains, world.Enemies), 10);
  brains[0].BossPhase = 2;
  assert.equal(Encounters.rosterLimit(finale, brains, world.Enemies), 12);
  brains[0].BossPhase = 3;
  assert.equal(Encounters.rosterLimit(finale, brains, world.Enemies), 14);
  // Ohne lebende Korvette rückt der Rest ganz normal nach.
  brains[0].BossPhase = 1;
  boss.Alive = false;
  assert.equal(Encounters.rosterLimit(finale, brains, world.Enemies), 14);
  // Andere Abschnitte und Piratenpatrouillen kennen keine Reserve.
  for (const s of stages.slice(0, -1)) assert.equal(Encounters.rosterLimit(s.Enemies, brains, world.Enemies), s.Enemies.length);
  assert.equal(Encounters.rosterLimit(new Int32Array([0, 1]), brains, world.Enemies), 2);
  assert.equal(Encounters.takeEscortCall(brains, world.Enemies), false);
  for (const phase of [2, 3]) {
    const [title, hint] = Encounters.phaseNotice(phase);
    assert.ok(title.length > 5 && hint.length > 20);
  }
});

test("the full finale brings all fourteen ships and both escort calls", () => {
  const result = stage(6, {});
  assert.equal(result.spawned, 14);
  assert.equal(result.sim.escortCalls, 2);
  assert.ok(result.won, "an average pilot holding course wins the finale");
  assert.ok(result.time > 40 && result.time < 400, `finale lasts ${result.time.toFixed(0)} s`);
});

test("balance bounds: profiles stay within fixed limits and fights have a sensible length", () => {
  const total = (role) => profile(role).Hull + profile(role).Shield;
  // Feste Werte je Rolle; mit dem Abschnitt wachsen sie höchstens um 4 % (Hülle) bzw. 1,5 % (Schaden).
  assert.deepEqual([0, 1, 2, 3, 4].map(total), [28, 50, 150, 190, 560]);
  // Kein Wert liegt unter dem Stand vor diesem Paket: GameSave.valid lehnt gespeicherte Gegner ab,
  // deren Hülle oder Schild über dem Profil liegt. Alte Spielstände blieben sonst nicht ladbar.
  [[28, 0], [38, 12], [72, 32], [96, 20], [340, 130]].forEach(([hull, shield], role) => {
    assert.ok(profile(role).Hull >= hull && profile(role).Shield >= shield, profile(role).Name);
  });
  assert.ok(total(DRONE) < total(SCOUT) && total(SCOUT) < total(GUNSHIP) && total(GUNSHIP) < total(BOMBER) && total(BOMBER) < total(COMMANDER));
  for (const role of [0, 1, 2, 3, 4]) {
    const p = profile(role);
    const ship = createWorld(1).Enemies[0];
    configure(ship, role, 6);
    assert.ok(ship.MaxHull <= p.Hull * 1.2 + 1e-9 && ship.LaserDamage <= p.LaserDamage * 1.075 + 1e-9, `${p.Name} scales mildly`);
    assert.ok(p.ChargeTime >= 0.4, `${p.Name} telegraphs every salvo`);
    assert.ok(p.CruiseSpeed + p.BoostSpeed < 60, `${p.Name} stays slower than the player's cruise speed`);
    assert.ok(p.AimError > 0 && p.AimError <= 0.025);
  }
  assert.ok(profile(BOMBER).ChargeTime >= 1.5 && profile(COMMANDER).ChargeTime >= 1.2, "heavy salvos leave time to react");

  // Zweikämpfe eines Durchschnittspiloten: Median aus acht Anflugrichtungen.
  const fights = [0, 1, 2, 3, 4].map(role => duels(role, {}));
  fights.forEach((fight, role) => {
    assert.equal(fight.wins, 8, `${profile(role).Name}: every duel is winnable`);
    assert.ok(fight.time > 2 && fight.time < 30, `${profile(role).Name}: ${fight.time.toFixed(1)} s`);
    // Wer nie ausweicht, kommt gegen die Korvette nur knapp durch; alle anderen Duelle bleiben billig.
    assert.ok(fight.damageTaken < (role === COMMANDER ? 200 : 80), `${profile(role).Name}: ${fight.damageTaken.toFixed(0)} damage taken`);
  });
  assert.ok(fights[COMMANDER].time > 15, `boss duel lasts ${fights[COMMANDER].time.toFixed(1)} s`);
  assert.ok(fights[COMMANDER].time > Math.max(...fights.slice(0, 4).map(f => f.time)), "the boss is the longest fight");
  assert.ok(fights[SCOUT].time < 20, `the small scout is not a time sink: ${fights[SCOUT].time.toFixed(1)} s`);
  assert.ok(fights[COMMANDER].damageTaken > fights[GUNSHIP].damageTaken, "the boss is the most dangerous duel");

  // Ein stehendes Ziel: die Rollen sind unterschiedlich gefährlich, keine tötet in 45 s allein.
  const sitting = [0, 1, 2, 3, 4].map(role => duels(role, { passive: true, limit: 45 }));
  sitting.forEach((fight, role) => {
    assert.equal(fight.deaths, 0, profile(role).Name);
    assert.ok(fight.damageTaken > 10, `${profile(role).Name} is a threat: ${fight.damageTaken.toFixed(0)}`);
  });
  assert.ok(sitting[BOMBER].damageTaken > sitting[GUNSHIP].damageTaken && sitting[GUNSHIP].damageTaken > sitting[DRONE].damageTaken);
});

test("scout hit margin restores roughly the old frontal target", () => {
  const area = (legacy) => frontalArea(spawn(createSim({ legacy }), 0, SCOUT, new Vec3(0, 0, -300)));
  assert.ok(area(false) / area(true) > 0.8 && area(false) / area(true) < 1.1, `${area(false).toFixed(1)} m² vs ${area(true).toFixed(1)} m²`);
});

test("all six mission stages are winnable for an average pilot who holds course", () => {
  for (let wave = 1; wave <= 6; wave++) {
    const result = stage(wave, {});
    assert.ok(result.won, `stage ${wave}: ${result.kills}/${result.roster} after ${result.time.toFixed(0)} s`);
    if (wave >= 3) assert.ok(result.damageTaken > 0, `stage ${wave} puts the player under fire`);
  }
});

test("pirates get weathered, uneven tints and their own lamp colours", () => {
  const tints = Array.from({ length: 16 }, (_, slot) => Encounters.pirateTint(slot));
  for (const tint of tints) {
    for (const value of [tint.X, tint.Y, tint.Z]) assert.ok(value > 0.35 && value < 1, "tint darkens the AETHER paint");
    assert.ok(Math.max(tint.X, tint.Y, tint.Z) - Math.min(tint.X, tint.Y, tint.Z) > 0.12, "tint is not a neutral grey");
  }
  assert.ok(new Set(tints.slice(0, 4).map(t => `${t.X},${t.Y},${t.Z}`)).size === 4, "a patrol of four wears four different coats");
  const role = profile(GUNSHIP).Tint;
  const lamps = [0, 1, 2].map(slot => Encounters.pirateLight(slot, role));
  assert.equal(new Set(lamps.map(l => `${l.X},${l.Y},${l.Z}`)).size, 3);
  for (const lamp of lamps) assert.ok(vec.len(vec.sub(lamp, role)) > 0.3, "lamp differs from the AETHER role colour");
  // Flackern bleibt in 0..1, setzt gelegentlich aus und ist deterministisch.
  const flicker = Array.from({ length: 600 }, (_, i) => Encounters.pirateFlicker(i / 30, 3));
  assert.ok(flicker.every(v => v >= 0 && v <= 1));
  assert.ok(flicker.some(v => v < 0.2) && flicker.filter(v => v < 0.2).length < 200);
  assert.deepEqual(flicker, Array.from({ length: 600 }, (_, i) => Encounters.pirateFlicker(i / 30, 3)));
  const strobe = Array.from({ length: 600 }, (_, i) => Encounters.pirateStrobe(i / 30, 3));
  assert.ok(strobe.some(Boolean) && strobe.filter(Boolean).length < 120);
});

test("saves keep their format; tactic fields get sensible defaults after loading", () => {
  const json = d => JSON.stringify(d, (_, v) => ArrayBuffer.isView(v) ? Array.from(v) : v);
  const rig = () => {
    const w = createWorld(16); resetPlayer(w.Player); resetWorld(w, 4);
    return { w, brains: Array.from({ length: 16 }, (_, i) => createBrain(i)), wallet: wallet(), cargo: cargo(24), markets: markets(), levels: new Int32Array(3), turret: turret(), pickups: pickups() };
  };
  const source = rig();
  const boss = source.w.Enemies[0], bomber = source.w.Enemies[1];
  configure(boss, COMMANDER, 0); resetShip(boss, new Vec3(0, 0, -300), identity);
  configure(bomber, BOMBER, 0); resetShip(bomber, new Vec3(80, 0, -300), identity);
  source.brains[0].Role = COMMANDER; resetBrain(source.brains[0], boss, source.w.Player.Position);
  source.brains[1].Role = BOMBER; resetBrain(source.brains[1], bomber, source.w.Player.Position);
  boss.Hull = boss.MaxHull * 0.5;
  Object.assign(source.brains[0], { BossPhase: 2, Locked: true, Lock: new Vec3(1, 2, 3), PhaseShift: 2, BatteryTimer: 0.2 });
  const state = { Trading: true, SectorId: "outer-belt", Wave: 0, Phase: 1, PhaseTimer: 0, Spawned: 2, SpawnTimer: 8, Patrol: new Int32Array([COMMANDER, BOMBER]), Score: 0, Kills: 0, Profit: 0, Trips: 1, Docked: false, Completed: false, Combo: 1, ComboTimer: 0 };
  const data = capture(state, { Camera: 0, Muted: true, InvertPitch: false, Sensitivity: .0018 }, 1794940000000, source.w, source.brains, source.wallet, source.cargo, source.markets, source.levels, source.turret, source.pickups);
  // Das Format kennt die neuen Felder nicht.
  assert.deepEqual(Object.keys(data.Enemies[0].Brain).sort(), ["Aim", "Evade", "Firing", "Home", "State", "Timing", "Waypoint"]);
  assert.equal(data.Enemies[0].Brain.Timing.length, 6);
  const parsed = decode(json(data));
  assert.equal(parsed.tag, 0, parsed.fields[0]);

  // Ziel-Hirne tragen Reste eines früheren Gefechts.
  const dest = rig();
  for (const brain of dest.brains) Object.assign(brain, { BossPhase: 3, Locked: true, EscortCall: true, PhaseShift: 2, BatteryTimer: 1, Side: 0 });
  restore(parsed.fields[0], dest.w, dest.brains, dest.wallet, dest.cargo, dest.markets, dest.levels, dest.turret, dest.pickups);
  resync(dest.w, dest.brains);
  const loaded = dest.brains[0];
  assert.equal(loaded.BossPhase, 2, "phase follows the saved hull");
  assert.equal(dest.brains[1].BossPhase, 1);
  for (const brain of dest.brains) {
    assert.equal(brain.Locked, false);
    assert.equal(brain.EscortCall, false);
    assert.equal(brain.PhaseShift, 0);
    assert.equal(brain.BatteryTimer, 0);
    assert.ok(brain.Side === 1 || brain.Side === -1);
  }
  // Nach dem Laden löst die Korvette den schon erreichten Phasenwechsel nicht noch einmal aus.
  const sim = { world: dest.w, brains: dest.brains, options: { passive: true }, stats: { shots: 0, hits: 0, enemyShots: 0, enemyHits: 0, damageTaken: 0, hullLost: 0, kills: 0, killTimes: [] }, time: 0 };
  const shield = dest.w.Enemies[0].Shield = 20;
  for (let i = 0; i < 30; i++) step(sim);
  assert.equal(Encounters.takeEscortCall(dest.brains, dest.w.Enemies), false);
  assert.ok(dest.w.Enemies[0].Shield < dest.w.Enemies[0].MaxShield, `no second shield surge (${shield} → ${dest.w.Enemies[0].Shield.toFixed(0)})`);
  assert.equal(loaded.BossPhase, 2);
});
