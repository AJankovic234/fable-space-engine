#!/usr/bin/env node
// Claude-Code-Hook (Stop und SessionStart): Hat sich das Projekt seit dem letzten Kostenbuch-Eintrag
// geändert – durch Claude Code, Codex, Gemini oder von Hand –, startet er den Haiku-Agenten
// `kostenbuch` im Hintergrund (lauf.mjs). Die laufende Sitzung wartet nicht darauf.
//   KOSTENBUCH_FORCE=1  startet auch ohne Änderung (zum Testen)
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { DATA, ROOT, fingerprint } from './kostenbuch.mjs';

// Der Agent selbst läuft als Claude-Code-Sitzung im Projekt; seine eigenen Hooks starten nichts.
if (process.env.KOSTENBUCH_AGENT === '1') process.exit(0);

const LOCK = path.join(DATA, '.lauf.lock');
const MIN_ABSTAND_MS = 3 * 60e3;
const now = Date.now();
const readJson = (f, d) => { try { return JSON.parse(fs.readFileSync(f, 'utf8')); } catch { return d; } };

try {
  const zustand = readJson(path.join(DATA, 'zustand.json'), {});
  const force = process.env.KOSTENBUCH_FORCE === '1';
  if (!force && zustand.fingerprint === fingerprint()) process.exit(0);
  if (!force && zustand.gestartet && now - Date.parse(zustand.gestartet) < MIN_ABSTAND_MS) process.exit(0);
  try {
    if (now - fs.statSync(LOCK).mtimeMs < 15 * 60e3) process.exit(0);
  } catch {}
  fs.writeFileSync(LOCK, String(now));
  fs.writeFileSync(path.join(DATA, 'zustand.json'), JSON.stringify({ ...zustand, gestartet: new Date(now).toISOString() }, null, 1) + '\n');
  const child = spawn(process.execPath, [path.join(ROOT, 'tools', 'kostenbuch', 'lauf.mjs')], {
    cwd: ROOT, detached: true, stdio: 'ignore', windowsHide: true,
    env: { ...process.env, KOSTENBUCH_AGENT: '1' },
  });
  child.unref();
} catch (e) {
  // Ein Hook darf die Sitzung nie stören.
  try { fs.rmSync(LOCK, { force: true }); } catch {}
}
process.exit(0);
