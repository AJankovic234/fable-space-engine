#!/usr/bin/env node
// Claude-Code-Hook (UserPromptSubmit): Arbeiten gerade andere Agenten (Codex, Gemini, weitere
// Claude-Sitzungen) am Projekt, bekommt Claude das mit den betroffenen Dateien als Kontext.
import fs from 'node:fs';
import { aktivitaet } from './kostenbuch.mjs';

if (process.env.KOSTENBUCH_AGENT === '1') process.exit(0);
try {
  let input = {};
  try { input = JSON.parse(fs.readFileSync(0, 'utf8')); } catch {}
  const a = aktivitaet({ selbst: input.session_id ?? null });
  if (a.belegt) {
    const wer = a.sitzungen.filter(s => !s.selbst && s.status === 'arbeitet').map(s => {
      const f = s.dateien.slice(0, 8).join(', ') + (s.dateien.length > 8 ? ` (+${s.dateien.length - 8})` : '');
      return `- ${s.werkzeug} (${s.modell}), letzte Aktivität vor ${s.letzteAktivitaetVorMin} min${f ? `, schreibt an: ${f}` : ''}`;
    }).join('\n');
    console.log(JSON.stringify({ hookSpecificOutput: { hookEventName: 'UserPromptSubmit',
      additionalContext: `Kostenbuch-Lotse: Gerade arbeiten andere Agenten an diesem Projekt.\n${wer}\nVor Änderungen an diesen Dateien den aktuellen Stand neu lesen und Überschneidungen dem Nutzer melden. Genauer: Agent "lotse" oder node tools/kostenbuch/kostenbuch.mjs --aktiv.` } }));
  }
} catch {}
process.exit(0);
