// Headless-Gefechtssimulation gegen die kompilierten Module (kein Test, sondern gemeinsame
// Grundlage für tests/combat.test.mjs und das Audit tests/combat-audit.mjs).
// Ein Skript-Pilot fliegt den Spielerkutter: Vorhalt, 1,5°-Feuerkegel, wandernder Zielfehler,
// optional Ausweichen auf sichtbare Aufladung. Deterministisch über den festen World-Rng.
import { EnemyProfiles_profile as profile, EnemyProfiles_configure as configure } from "../build/EnemyProfiles.js";
import { AI_createBrain as createBrain, AI_reset as resetBrain, AI_update as updateAI } from "../build/AI.js";
import {
  ShipModule_reset as resetShip,
  WorldModule_create as createWorld,
  WorldModule_reset as resetWorld,
  Physics_step as stepPhysics,
  Ballistics_interceptTime as interceptTime,
} from "../build/PhysicsAndPool.js";
import { Rng__NextFloat as nextFloat } from "../build/ProceduralMeshes.js";
import { Mission_stages as stages, Mission_resetPlayer as resetPlayer, Mission_applyUpgrade as applyUpgrade } from "../build/Mission.js";
import {
  Vec3,
  QuatModule_rotateVec3 as rotate,
  QuatModule_forward as forward,
  QuatModule_right as right,
  QuatModule_up as up,
  QuatModule_lookRotation as lookRotation,
  Vec3Module_normalize as normalize,
} from "../build/Math3D.js";

// Fehlt das Modul (Audit gegen einen älteren Build), gilt der alte Takt ohne Bossreserve.
const encounters = await import("../build/Encounters.js").catch(() => null);
const rosterLimit = encounters ? encounters.rosterLimit : (roster) => roster.length;
const takeEscortCall = encounters ? encounters.takeEscortCall : () => false;

export const DT = 1 / 120;
export const LASER_SPEED = 520;
export const roleNames = ["Drohne", "Späher", "Kanonenboot", "Bomber", "Kommandoschiff"];
export const stateName = (brain) => brain.State.cases()[brain.State.tag];

const add = (a, b, s = 1) => new Vec3(a.X + b.X * s, a.Y + b.Y * s, a.Z + b.Z * s);
const sub = (a, b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
const dot = (a, b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
const len = (a) => Math.hypot(a.X, a.Y, a.Z);
const clamp = (v) => Math.max(-1, Math.min(1, v));
const conj = (q) => ({ X: -q.X, Y: -q.Y, Z: -q.Z, W: q.W });
export const vec = { add, sub, dot, len };

/// Trefferflächen vor Runde 1: der alte Abfangjäger, je Rolle gleichförmig skaliert.
const legacyScale = [0.85, 0.95, 1.3, 1.5, 2.1];
const legacyHalf = [2.6, 1.15, 2.1];
export function legacyShape(ship, role) {
  const s = legacyScale[role];
  ship.ObbCenter = new Vec3(0, 0, -0.05 * s);
  ship.ObbHalf = new Vec3(legacyHalf[0] * s, legacyHalf[1] * s, legacyHalf[2] * s);
  ship.Radius = (0.05 + Math.hypot(...legacyHalf)) * s;
  if (role === 4) { ship.MaxHull *= 260 / 340; ship.Hull = ship.MaxHull; }
}

export function frontalArea(ship) { return 4 * ship.ObbHalf.X * ship.ObbHalf.Y; }

/// Wie steerTowards der KI, nur für den Skript-Piloten.
function steer(ship, direction, gain) {
  const c = ship.Controls;
  const l = rotate(conj(ship.Rotation), direction);
  c.Yaw = clamp(Math.atan2(-l.X, -l.Z) * gain);
  c.Pitch = l.Z > 0 ? (l.Y >= 0 ? 1 : -1) : clamp(Math.atan2(l.Y, Math.hypot(l.X, l.Z)) * gain);
  c.Roll = 0;
}

/// Zwei Pilotenprofile: "average" ist die Referenz für die Balance, "ace" die obere Grenze.
export const pilots = {
  average: { aimError: 0.025, cone: 0.99905, range: 240, gain: 3, reaction: 0.6 },
  ace: { aimError: 0.015, cone: 0.99905, range: 300, gain: 4, reaction: 0.2 },
};

/// options: pilot ("average" | "ace"), dodge (weicht sichtbar aufgeladenen Salven aus), aimError (rad, wandernd), legacy (alte Trefferflächen)
export function createSim(options = {}) {
  const world = createWorld(16);
  resetPlayer(world.Player);
  resetWorld(world, 0);
  const brains = Array.from({ length: 16 }, (_, i) => createBrain(i));
  options = { ...pilots[options.pilot ?? "average"], ...options };
  const sim = {
    world, brains, options, reaction: 0, time: 0, target: -1, dodge: 0, dodgeSide: 1,
    stats: { shots: 0, hits: 0, enemyShots: 0, enemyHits: 0, damageTaken: 0, hullLost: 0, kills: 0, killTimes: [], playerDead: false },
    combo: 1, comboTimer: 0, onKill: null,
  };
  return sim;
}

export function spawn(sim, index, role, position, level = 0) {
  const { world, brains } = sim;
  const enemy = world.Enemies[index];
  configure(enemy, role, level);
  if (sim.options.legacy) legacyShape(enemy, role);
  const toPlayer = sub(world.Player.Position, position);
  resetShip(enemy, position, lookRotation(len(toPlayer) > 1e-3 ? toPlayer : new Vec3(0, 0, -1), new Vec3(0, 1, 0)));
  brains[index].Role = role;
  resetBrain(brains[index], enemy, world.Player.Position);
  return enemy;
}

/// Position wie Main.spawnEnemy: 320–440 m entfernt, vorwiegend im vorderen Halbraum.
export function spawnAhead(sim, index, role, level = 0) {
  const { world } = sim;
  const range = (min, max) => min + (max - min) * nextFloat(world.Rng);
  const angle = index * 2.39996 + range(-0.25, 0.25);
  const local = normalize(new Vec3(Math.sin(angle) * 0.9, range(-0.22, 0.22), -0.7 - Math.abs(Math.cos(angle)) * 0.5));
  const direction = rotate(world.Player.Rotation, local);
  return spawn(sim, index, role, add(world.Player.Position, direction, range(320, 440)), level);
}

function pilot(sim) {
  const { world, brains, options } = sim;
  const player = world.Player;
  const c = player.Controls;
  if (!player.Alive) return;
  if (options.passive) { c.Fire = false; return; }
  // Ziel: das nächste lebende Schiff, bis es zerstört ist.
  if (sim.target < 0 || !world.Enemies[sim.target].Alive) {
    sim.target = -1;
    sim.reaction = options.reaction;
    let best = Infinity;
    world.Enemies.forEach((e, i) => {
      if (!e.Alive) return;
      const d = len(sub(e.Position, player.Position));
      if (d < best) { best = d; sim.target = i; }
    });
  }
  if (sim.target < 0) { c.Fire = false; c.Throttle = 0.3; c.Pitch = c.Yaw = c.Roll = 0; return; }
  const enemy = world.Enemies[sim.target];
  const to = sub(enemy.Position, player.Position);
  const distance = len(to);
  const rel = sub(enemy.Velocity, player.Velocity);
  const t = interceptTime(to.X, to.Y, to.Z, rel.X, rel.Y, rel.Z, LASER_SPEED);
  const lead = t > 0 ? add(to, rel, t) : to;
  const error = distance * options.aimError;
  const w = sim.time * 1.1;
  const aim = new Vec3(lead.X + Math.sin(w) * error, lead.Y + Math.cos(w * 1.3) * error, lead.Z + Math.sin(w * 0.7 + 2) * error);
  const aimLength = len(aim);
  let desired = aim;
  c.Boost = false;
  c.Brake = false;
  c.Throttle = distance > 230 ? 1 : distance > 130 ? 0.55 : 0.2;
  sim.reaction -= DT;
  c.Fire = sim.reaction <= 0 && distance < options.range && dot(forward(player.Rotation), aim) / aimLength > options.cone;

  if (options.dodge) {
    // Eine sichtbare Aufladung jenseits der Hälfte oder eine laufende Salve in Reichweite: quer wegziehen.
    let threatened = false;
    world.Enemies.forEach((e, i) => {
      if (!e.Alive) return;
      const b = brains[i];
      const d = len(sub(e.Position, player.Position));
      if (d < 430 && (b.Role >= 3 || (b.Role === 2 && i === sim.target)) && b.WeaponCharge > 0.6) threatened = true;
    });
    // Ein Haken je Aufladung, danach wieder angreifen (kein Dauer-Ausweichen im Pulk).
    sim.dodge -= DT;
    if (threatened && sim.dodge <= -1.6) { sim.dodge = 0.8; sim.dodgeSide = -sim.dodgeSide; }
    if (sim.dodge > 0) {
      desired = add(add(normalize(aim), right(player.Rotation), 0.9 * sim.dodgeSide), up(player.Rotation), 0.5);
      c.Throttle = 1;
      c.Boost = true;
      c.Fire = false;
    }
  }
  steer(player, normalize(desired), options.gain);
}

function drainEvents(sim) {
  const { world, stats } = sim;
  for (const event of world.Events) {
    const name = event.cases()[event.tag];
    if (name === "LaserFired") {
      if (event.fields[0] === 0) stats.shots += 2; else stats.enemyShots += 2;
    } else if (name === "ShipHit") {
      if (event.fields[0] === 1) stats.hits += 1;
      else { stats.enemyHits += 1; stats.damageTaken += event.fields[1]; }
    } else if (name === "ShipDestroyed") {
      if (event.fields[0] === 1) {
        stats.kills += 1;
        stats.killTimes.push(sim.time);
        if (sim.onKill) sim.onKill(event.fields[2]);
      } else stats.playerDead = true;
    }
  }
  world.Events.length = 0;
}

export function step(sim) {
  const { world, brains } = sim;
  const hull = world.Player.Hull;
  pilot(sim);
  updateAI(world, brains, DT);
  stepPhysics(world, DT);
  sim.time += DT;
  sim.stats.hullLost += Math.max(0, hull - world.Player.Hull);
  drainEvents(sim);
  if (sim.hook) sim.hook(sim);
}

export function run(sim, seconds, until) {
  const steps = Math.round(seconds / DT);
  for (let i = 0; i < steps; i++) {
    step(sim);
    if (until && until(sim)) break;
  }
  return sim;
}

const alive = (sim) => sim.world.Enemies.filter(e => e.Alive).length;

/// Zweikampf: ein Gegner der Rolle startet wie eine Verstärkung 380 m entfernt im vorderen Halbraum.
export function duel(role, options = {}) {
  const sim = createSim(options);
  const bearing = options.bearing ?? 0.1;
  spawn(sim, 0, role, new Vec3(Math.sin(bearing) * 380, 10, -Math.cos(bearing) * 380), options.level ?? 0);
  run(sim, options.limit ?? 180, s => alive(s) === 0 || !s.world.Player.Alive);
  return summary(sim);
}

const median = (values) => { const v = [...values].sort((a, b) => a - b); return v[Math.floor(v.length / 2)]; };
const mean = (values) => values.reduce((a, b) => a + b, 0) / values.length;

/// Acht Zweikämpfe mit unterschiedlicher Anflugrichtung: Median der Dauer, Mittel der übrigen Werte.
export function duels(role, options = {}) {
  const runs = Array.from({ length: 8 }, (_, i) => duel(role, { ...options, bearing: -1.05 + i * 0.3 }));
  return {
    time: median(runs.map(r => r.won ? r.time : (options.limit ?? 180))),
    wins: runs.filter(r => r.won).length,
    deaths: runs.filter(r => r.playerDead).length,
    hitRate: mean(runs.map(r => r.hitRate)),
    enemyHitRate: mean(runs.map(r => r.enemyHitRate)),
    damageTaken: mean(runs.map(r => r.damageTaken)),
    hullLost: mean(runs.map(r => r.hullLost)),
    runs,
  };
}

export function summary(sim) {
  const s = sim.stats;
  const p = sim.world.Player;
  return {
    time: sim.time,
    won: alive(sim) === 0 && p.Alive,
    playerDead: !p.Alive,
    hitRate: s.shots ? s.hits / s.shots : 0,
    enemyHitRate: s.enemyShots ? s.enemyHits / s.enemyShots : 0,
    damageTaken: s.damageTaken,
    hullLost: s.hullLost,
    hullLeft: p.Hull,
    kills: s.kills,
    killTimes: s.killTimes,
  };
}

const upgradeOrder = [0, 1, 2, 0, 1];

/// Ein Missionsabschnitt mit dem Verstärkungstakt aus Main (3 zu Beginn, dann Zweiergruppen,
/// Bossreserve und Eskortenruf über Encounters).
export function stage(wave, options = {}) {
  const sim = createSim(options);
  const player = sim.world.Player;
  const levels = new Int32Array(3);
  for (let i = 0; i < wave - 1; i++) applyUpgrade(player, levels, upgradeOrder[i]);
  player.Hull = Math.min(player.MaxHull, options.startHull ?? player.MaxHull);
  player.Shield = player.MaxShield;
  const roster = Array.from(stages[wave - 1].Enemies);
  const interval = options.interval ?? 5;
  let spawned = Math.min(3, roster.length);
  let timer = interval;
  for (let i = 0; i < spawned; i++) spawnAhead(sim, i, roster[i], wave - 1);
  sim.onKill = () => {
    // Wie Main: Schildbonus je Abschuss, ab Dreierkombo Waffen-Overdrive.
    player.Shield = Math.min(player.MaxShield, player.Shield + 16);
    sim.combo = sim.comboTimer > 0 ? Math.min(4, sim.combo + 1) : 1;
    sim.comboTimer = 8;
    if (sim.combo >= 3) player.Overdrive = 7.5;
  };
  sim.hook = () => {
    sim.comboTimer -= DT;
    const allowed = rosterLimit(roster, sim.brains, sim.world.Enemies);
    if (takeEscortCall(sim.brains, sim.world.Enemies)) { timer = Math.min(timer, 1.5); sim.escortCalls = (sim.escortCalls ?? 0) + 1; }
    if (spawned < allowed) {
      timer -= DT;
      if (timer <= 0) {
        const next = Math.min(allowed, spawned + 2);
        for (let i = spawned; i < next; i++) spawnAhead(sim, i, roster[i], wave - 1);
        spawned = next;
        timer = interval;
      }
    }
    if (options.onStep) options.onStep(sim, { roster, spawned, allowed });
  };
  run(sim, options.limitSeconds ?? 600, s => (spawned >= roster.length && alive(s) === 0) || !s.world.Player.Alive);
  return { ...summary(sim), spawned, roster: roster.length, sim };
}
