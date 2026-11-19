// Balance-Audit: node tests/combat-audit.mjs [legacy]
// Zweikämpfe je Rolle (acht Anflugrichtungen) und alle sechs Missionsabschnitte.
// "legacy" rechnet mit den Trefferflächen vor dem Modellwechsel (alter Abfangjäger, skaliert).
import { duels, stage, roleNames, createSim, spawn, frontalArea } from "./combat-sim.mjs";
import { Vec3 } from "../build/Math3D.js";

const legacy = process.argv.includes("legacy");
const f = (v, d = 1) => v.toFixed(d).padStart(6);

console.log("== Trefferfläche von vorn (m²), Hülle + Schild ==");
for (let role = 0; role < 5; role++) {
  const ship = (old) => spawn(createSim({ legacy: old }), 0, role, new Vec3(0, 0, -300));
  const now = ship(false), old = ship(true);
  console.log(roleNames[role].padEnd(15), "alt", f(frontalArea(old)), "neu", f(frontalArea(now)), "x" + (frontalArea(now) / frontalArea(old)).toFixed(2), "  HP", now.MaxHull, "+", now.MaxShield);
}

console.log(`\n== Zweikampf${legacy ? " (alte Trefferflächen)" : ""}: Median aus 8 Anflügen ==`);
console.log("                 stehendes Ziel 45 s | Durchschnittspilot, hält Kurs    | Durchschnittspilot, weicht aus   | Könner, hält Kurs");
console.log("Rolle            Schaden  Treffer    | TTK s  Schaden  eig. Treffer     | TTK s  Schaden  eig. Treffer     | TTK s  Schaden");
for (let role = 0; role < 5; role++) {
  const p = duels(role, { legacy, passive: true, limit: 45 });
  const a = duels(role, { legacy });
  const b = duels(role, { legacy, dodge: true });
  const c = duels(role, { legacy, pilot: "ace" });
  const row = (r) => `${f(r.time)}   ${f(r.damageTaken, 0)}        ${f(r.hitRate * 100, 0)}%`;
  console.log(roleNames[role].padEnd(15), `${f(p.damageTaken, 0)}    ${f(p.enemyHitRate * 100, 0)}%${p.deaths ? ` (${p.deaths}/8 tot)` : ""}`.padEnd(21), "|", row(a).padEnd(32), "|", row(b).padEnd(32), "|", `${f(c.time)}   ${f(c.damageTaken, 0)}`);
}

for (const [label, options] of [["Durchschnittspilot, hält Kurs", {}], ["Durchschnittspilot, weicht aus", { dodge: true }], ["Könner, hält Kurs", { pilot: "ace" }]]) {
  console.log(`\n== Missionsabschnitte: ${label} ==`);
  console.log("Abschnitt  Gegner  Dauer s  eig. Treffer  Gegnertreffer  Schaden  Hülle verloren  Ergebnis");
  for (let wave = 1; wave <= 6; wave++) {
    const r = stage(wave, { legacy, ...options });
    console.log(`${wave}          ${String(r.roster).padStart(2)}     ${f(r.time)}        ${f(r.hitRate * 100, 0)}%        ${f(r.enemyHitRate * 100, 0)}%   ${f(r.damageTaken, 0)}          ${f(r.hullLost, 0)}  ${r.won ? "Sieg" : r.playerDead ? `tot nach ${r.kills} Abschüssen` : `offen (${r.kills})`}`);
  }
}
