#!/usr/bin/env node
// Wache: wartet, bis außer der eigenen Sitzung kein Agent mehr am Projekt arbeitet, und lässt dann
// den Haiku-Agenten `lotse` zusammenfassen, was die anderen zuletzt geändert haben. Gedacht als
// Hintergrundprozess: Er endet mit Status 0, sobald das Repo frei ist (1 nach Ablauf von --max).
//   node tools/kostenbuch/wache.mjs --selbst <sitzungs-id> [--ruhe 5] [--max 180] [--ohne-haiku]
import { spawnSync } from 'node:child_process';
import { ROOT, aktivitaet, findClaude } from './kostenbuch.mjs';

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const selbst = opt('--selbst', process.env.KOSTENBUCH_SELBST ?? null);
const ruheMin = +opt('--ruhe', 5), maxMin = +opt('--max', 180);
const start = Date.now();
const stamp = () => new Date().toTimeString().slice(0, 5);
const beschreibe = a => a.sitzungen.filter(s => !s.selbst && s.status === 'arbeitet')
  .map(s => `${s.werkzeug} (${s.modell})`).join(', ');

let zuletzt = null;
for (;;) {
  const a = aktivitaet({ selbst, ruheMin });
  const jetzt = a.belegt ? beschreibe(a) : '';
  if (jetzt !== zuletzt) console.log(`[${stamp()}] ${a.belegt ? `belegt: ${jetzt}` : 'frei'}`);
  zuletzt = jetzt;
  if (!a.belegt) break;
  if (Date.now() - start > maxMin * 6e4) { console.log(`[${stamp()}] Nach ${maxMin} min immer noch belegt.`); process.exit(1); }
  await new Promise(r => setTimeout(r, 30e3));
}

console.log(`[${stamp()}] Seit ${ruheMin} min arbeitet kein anderer Agent mehr am Projekt.`);
const claude = args.includes('--ohne-haiku') ? null : findClaude();
if (claude) {
  const r = spawnSync(claude, [
    '-p', 'Das Repo ist gerade frei geworden. Fasse zusammen, was die anderen Agenten in der letzten halben Stunde geändert haben.',
    '--agent', 'lotse', '--model', 'haiku', '--permission-mode', 'dontAsk',
    '--allowedTools', 'Bash(node tools/kostenbuch/kostenbuch.mjs --aktiv*)', 'Bash(git status*)', 'Bash(git diff --stat*)', 'Read',
  ], { cwd: ROOT, encoding: 'utf8', timeout: 4 * 60e3, windowsHide: true, env: { ...process.env, KOSTENBUCH_AGENT: '1', KOSTENBUCH_SELBST: selbst ?? '' } });
  console.log(r.status === 0 ? (r.stdout ?? '').trim() : `(Haiku-Zusammenfassung fehlgeschlagen: Status ${r.status})`);
}
