#!/usr/bin/env node
// Hintergrundlauf des Kostenbuchs: startet den Haiku-Agenten `kostenbuch` (.claude/agents/kostenbuch.md)
// headless über die Claude-Code-CLI. Fehlt die CLI oder scheitert der Lauf, wird das Kostenbuch
// ohne Zusammenfassung direkt aktualisiert, damit Kosten und Zeiten trotzdem stimmen.
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { DATA, HERE, ROOT, findClaude } from './kostenbuch.mjs';

const LOCK = path.join(DATA, '.lauf.lock');
const LOG = path.join(DATA, 'lauf.log');
const log = msg => fs.appendFileSync(LOG, `[${new Date().toISOString()}] ${msg}\n`);

try {
  try { if (fs.statSync(LOG).size > 200e3) fs.rmSync(LOG); } catch {}
  const claude = findClaude();
  let ok = false;
  if (claude) {
    const r = spawnSync(claude, [
      '-p', 'Aktualisiere das Kostenbuch dieses Projekts.',
      '--agent', 'kostenbuch', '--model', 'haiku',
      '--permission-mode', 'dontAsk',
      '--allowedTools', 'Bash(node tools/kostenbuch/kostenbuch.mjs)', 'Bash(node tools/kostenbuch/kostenbuch.mjs *)', 'Read',
    ], { cwd: ROOT, encoding: 'utf8', timeout: 5 * 60e3, windowsHide: true, env: { ...process.env, KOSTENBUCH_AGENT: '1' } });
    ok = r.status === 0;
    log(`Haiku-Agent ${ok ? 'fertig' : `fehlgeschlagen (Status ${r.status}, ${r.error?.message ?? ''})`}: ${(r.stdout ?? '').trim().slice(0, 400)}`);
  } else log('Claude-Code-CLI nicht gefunden.');
  if (!ok) {
    const r = spawnSync(process.execPath, [path.join(HERE, 'kostenbuch.mjs'), '--log', 'Project files changed (automatic entry, no summary).'],
      { cwd: ROOT, encoding: 'utf8', timeout: 5 * 60e3, windowsHide: true });
    log(`Ersatzlauf ohne Agent: Status ${r.status}`);
  }
} catch (e) {
  log(`Fehler: ${e.message}`);
} finally {
  fs.rmSync(LOCK, { force: true });
}
