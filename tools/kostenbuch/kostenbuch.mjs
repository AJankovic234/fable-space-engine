#!/usr/bin/env node
// Kostenbuch – schätzt für alle Agenten, die an diesem Projekt gearbeitet haben, die API-Kosten
// (Listenpreis, ohne Abos), die Arbeitszeit und die Leerlaufzeit (Warten auf Eingabe).
// Quellen: lokale Protokolle von Claude Code, Codex und Gemini/Antigravity. Grobe Schätzung.
//
//   node tools/kostenbuch/kostenbuch.mjs               Stand + Delta seit letztem Protokolleintrag (JSON)
//   node tools/kostenbuch/kostenbuch.mjs --write       zusätzlich README-Abschnitt aktualisieren
//   node tools/kostenbuch/kostenbuch.mjs --log "Text"  Protokolleintrag anhängen (Delta wird berechnet), dann --write
//   node tools/kostenbuch/kostenbuch.mjs --voll        alle Protokolle neu einlesen (statt nur geänderter)
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const HERE = path.dirname(fileURLToPath(import.meta.url));
export const ROOT = path.resolve(HERE, '..', '..');
const HOME = os.homedir();
// Das Repo ist öffentlich: Rohdaten je Sitzung, Protokoll und Zustand bleiben lokal beim Nutzer.
// Ins Repo gelangt nur die zusammengefasste Auswertung im README.
export const DATA = process.env.KOSTENBUCH_DATA ?? path.join(HOME, '.kostenbuch', path.basename(ROOT));
fs.mkdirSync(DATA, { recursive: true });
const FILES = {
  preise: path.join(HERE, 'preise.json'),
  sitzungen: path.join(DATA, 'sitzungen.json'),
  protokoll: path.join(DATA, 'protokoll.json'),
  zustand: path.join(DATA, 'zustand.json'),
  readme: path.join(ROOT, 'README.md'),
};
const START = '<!-- kostenbuch:start -->';
const END = '<!-- kostenbuch:end -->';

const readJson = (file, fallback) => { try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return fallback; } };
// Atomar schreiben: Hook, Wache und Kostenbuch-Lauf können gleichzeitig laufen.
const writeJson = (file, data) => {
  const tmp = `${file}.${process.pid}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(data, null, 1) + '\n');
  fs.renameSync(tmp, file);
};
const PRICES = readJson(FILES.preise, null);
if (!PRICES) throw new Error('preise.json fehlt oder ist kein gültiges JSON');
const MARKERS = PRICES.projektMarker.map(m => m.toLowerCase());
const PROJECT_START = Date.parse(PRICES.projektStart ?? '2026-11-16');
// Ändert sich die Preistabelle oder die Auswertung (Version), werden alle relevanten Protokolle neu gelesen.
// 5: Gemini-Eingabe ohne doppelten Cache-Abzug, Gemini-Schritte ohne Modellangabe, nichts vor Projektbeginn.
const PARSER_VERSION = 5;
const PRICE_HASH = crypto.createHash('sha1').update(PARSER_VERSION + JSON.stringify(PRICES.modelle) + JSON.stringify(PRICES.zeit) + JSON.stringify(PRICES.projektMarker) + PRICES.projektStart).digest('hex').slice(0, 10);
const hasMarker = s => { const l = String(s ?? '').toLowerCase(); return MARKERS.some(m => l.includes(m)); };
// Manche Sitzungen (vor allem Antigravity) liefen schon vor Projektbeginn für andere Arbeit.
const vorStart = t => Number.isFinite(t) && t < PROJECT_START;

// ---------------------------------------------------------------------------------------------
// Preise
function priceFor(model, t) {
  const id = String(model).toLowerCase();
  let best = null;
  for (const p of PRICES.modelle) {
    if (!id.includes(p.muster)) continue;
    if (p.ab && t < Date.parse(p.ab)) continue;
    if (p.bis && t >= Date.parse(p.bis) + 864e5) continue;
    if (!best || p.muster.length > best.muster.length) best = p;
  }
  return best;
}

// u: { eingabe (ungecacht), cacheLesen, cacheSchreiben5m, cacheSchreiben1h, cacheSchreiben, ausgabe, fast }
function costUsd(p, u) {
  if (!p) return null;
  const prompt = u.eingabe + u.cacheLesen + u.cacheSchreiben5m + u.cacheSchreiben1h + u.cacheSchreiben;
  const r = p.langAb && prompt > p.langAb ? { ...p, ...p.lang } : p;
  const w5 = r.cacheSchreiben5m ?? r.cacheSchreiben ?? r.eingabe * 1.25;
  const w1 = r.cacheSchreiben1h ?? r.cacheSchreiben ?? r.eingabe * 2;
  const wx = r.cacheSchreiben ?? r.cacheSchreiben5m ?? r.eingabe * 1.25;
  let usd = (u.eingabe * r.eingabe + u.cacheLesen * (r.cacheLesen ?? r.eingabe) + u.cacheSchreiben5m * w5
    + u.cacheSchreiben1h * w1 + u.cacheSchreiben * wx + u.ausgabe * r.ausgabe) / 1e6;
  if (u.fast && p.fast) usd *= p.fast;
  return usd;
}

// ---------------------------------------------------------------------------------------------
// Sitzung: Modellsummen und Zeitachse
// Gespeicherte Pfade ohne Benutzerordner, denn das Kostenbuch darf ins Repo.
const anon = p => (p.toLowerCase().startsWith(HOME.toLowerCase()) ? '~' + p.slice(HOME.length) : p).replace(/\\/g, '/');

function newSession(werkzeug, datei) {
  return { werkzeug, datei: anon(datei), id: path.basename(datei), relevant: false, start: null, ende: null,
    modelle: {}, arbeitMs: 0, leerlaufMs: 0, intervalle: [], events: [], offen: false };
}

function modelRec(s, model, t) {
  const p = priceFor(model, t);
  return (s.modelle[model] ??= { name: p?.name ?? model, anbieter: p?.anbieter ?? '?', anfragen: 0,
    eingabe: 0, cacheLesen: 0, cacheSchreiben: 0, ausgabe: 0, usd: 0, ohnePreis: !p, dateien: {} });
}

// ---------------------------------------------------------------------------------------------
// Wer hat wo geschrieben: Projektdateien je Modell (Pfad relativ zum Projekt, letzter Schreibzeitpunkt).
// Der erste Marker ist der Projektordner; weitere Marker sind Orchestrierungsordner mit Worktrees
// (wp-*, integration, base), deren Pfade auf das Projekt abgebildet werden.
export function projectPath(raw, cwd) {
  let p = String(raw ?? '').trim().replace(/^["'`]+|["'`,;)]+$/g, '').replace(/\\+/g, '/');
  if (!p) return null;
  if (!/^([a-z]:)?\//i.test(p)) {
    if (!cwd) return null;
    p = String(cwd).replace(/\\+/g, '/').replace(/\/$/, '') + '/' + p;
  }
  const l = p.toLowerCase();
  let rel = null;
  PRICES.projektMarker.forEach((m, i) => {
    const at = l.indexOf('/' + m.toLowerCase() + '/');
    if (rel !== null || at < 0) return;
    rel = p.slice(at + m.length + 2);
    if (i > 0) { const seg = rel.split('/')[0]; rel = /^(wp-|integration$|base$)/.test(seg) ? rel.slice(seg.length + 1) : null; }
  });
  if (!rel || rel.length > 160 || !/^[^\n*<>|"]+\.[A-Za-z0-9]{1,8}$/.test(rel)) return null;
  if (rel.startsWith('..') || /^(build|dist|node_modules|bin|obj|\.git)\//.test(rel) || /(^|\/)_[^/]*$/.test(rel) || /\/(bin|obj)\//.test(rel)) return null;
  return rel.replace(/^\.\//, '');
}

function recordWrite(s, model, t, raw, cwd) {
  const rel = projectPath(raw, cwd);
  if (!rel || !model || model === '<synthetic>') return;
  s.relevant = true;
  const d = modelRec(s, model, t).dateien;
  d[rel] = Math.max(d[rel] ?? 0, Number.isFinite(t) ? t : 0);
}

// Shell-Befehle mit Schreibabsicht: Projektpfade darin gelten als geschrieben (grobe Heuristik).
// Eine Umleitung `>` zählt, `=>` (Pfeilfunktion), `->`, `>=`, `2>&1` und `> /dev/null` nicht.
const WRITE_HINT = /(sed\s+-i|(?<![=\-<>])>>?\s*["']?(?!&|\/dev\/null)[\w./\\:-]|\btee\b|\bcp\b|\bmv\b|writeFile|write_text|open\([^)]*["'][wa]|Set-Content|Out-File|apply_patch|git\s+apply|\bpatch\b|\.write\()/;
export function shellWrites(cmd) {
  const text = String(cmd ?? '');
  if (!WRITE_HINT.test(text)) return [];
  const out = new Set();
  for (const m of text.matchAll(/[A-Za-z]:[\\/][^\s"'`;|&<>()]+|\/[a-z]\/[^\s"'`;|&<>()]+/g)) out.add(m[0].replace(/^\/([a-z])\//, '$1:/'));
  for (const m of text.matchAll(/(?:^|[\s"'`(=])((?:src|tests|tools|public|artifacts)\/[\w./-]+\.\w+|[A-Za-z_]+\.(?:md|html|json|mjs|fsproj))\b/g)) out.add(m[1]);
  return [...out];
}

function addUsage(s, model, t, u) {
  const p = priceFor(model, t);
  const m = modelRec(s, model, t);
  m.anfragen += 1;
  m.eingabe += u.eingabe; m.cacheLesen += u.cacheLesen;
  m.cacheSchreiben += u.cacheSchreiben5m + u.cacheSchreiben1h + u.cacheSchreiben;
  m.ausgabe += u.ausgabe;
  m.usd += costUsd(p, u) ?? 0;
}

// Arbeit = Lücken zwischen Agentenereignissen (bis arbeitsLueckeMaxMin).
// Leerlauf = Warten auf deine Eingabe (Lücke vor einer Eingabe, bis leerlaufMaxMin); längere Lücken sind Pausen.
function timeline(s) {
  const workMax = PRICES.zeit.arbeitsLueckeMaxMin * 6e4, idleMax = PRICES.zeit.leerlaufMaxMin * 6e4;
  const ev = s.events.filter(e => Number.isFinite(e.t)).sort((a, b) => a.t - b.t);
  s.start = ev[0]?.t ?? null; s.ende = ev.at(-1)?.t ?? null;
  let open = null;
  for (let i = 1; i < ev.length; i++) {
    const a = ev[i - 1], b = ev[i], gap = b.t - a.t;
    if (gap <= 0) continue;
    if (!b.user && gap <= workMax) {
      s.arbeitMs += gap;
      if (open && open[1] === a.t) open[1] = b.t; else { open = [a.t, b.t]; s.intervalle.push(open); }
    } else if (gap <= idleMax) s.leerlaufMs += gap;
  }
  delete s.events;
  return s;
}

const lines = text => text.split('\n').filter(Boolean).map(l => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);

// ---------------------------------------------------------------------------------------------
// Claude Code: ~/.claude/projects/*/<sitzung>.jsonl und <sitzung>/subagents/*.jsonl
function claudeUserInput(o, subagent) {
  if (subagent || o.type !== 'user' || o.isMeta || o.isSidechain) return false;
  const bad = /^\s*<(task-notification|system-reminder|local-command|command-|ide_)/;
  const c = o.message?.content;
  if (typeof c === 'string') return !bad.test(c);
  if (!Array.isArray(c) || c.some(b => b.type === 'tool_result')) return false;
  return c.some(b => (b.type === 'text' && !bad.test(b.text ?? '')) || b.type === 'image');
}

export function parseClaude(file, text) {
  const s = newSession('Claude Code', file);
  const subagent = /[\\/]subagents[\\/]/.test(file);
  const requests = new Map();
  for (const o of lines(text)) {
    const t = Date.parse(o.timestamp);
    if (vorStart(t)) continue;
    if (!s.relevant && o.cwd && hasMarker(o.cwd)) s.relevant = true;
    const msg = o.message;
    if (o.type === 'assistant' && msg?.usage && msg.model && msg.model !== '<synthetic>') {
      const key = o.requestId ?? msg.id ?? `${t}`;
      const u = msg.usage, prev = requests.get(key);
      const cc = u.cache_creation ?? {};
      const w1h = cc.ephemeral_1h_input_tokens ?? (cc.ephemeral_5m_input_tokens == null ? u.cache_creation_input_tokens ?? 0 : 0);
      const cur = { model: msg.model, t, eingabe: u.input_tokens ?? 0, cacheLesen: u.cache_read_input_tokens ?? 0,
        cacheSchreiben5m: cc.ephemeral_5m_input_tokens ?? 0, cacheSchreiben1h: w1h, cacheSchreiben: 0,
        ausgabe: u.output_tokens ?? 0, fast: u.speed === 'fast' };
      if (!prev) requests.set(key, cur);
      else for (const k of ['eingabe', 'cacheLesen', 'cacheSchreiben5m', 'cacheSchreiben1h', 'ausgabe']) prev[k] = Math.max(prev[k], cur[k]);
      for (const b of Array.isArray(msg.content) ? msg.content : []) {
        if (b.type !== 'tool_use') continue;
        const input = b.input ?? {};
        if (!s.relevant && hasMarker(JSON.stringify(input))) s.relevant = true;
        if (['Edit', 'Write', 'MultiEdit', 'NotebookEdit'].includes(b.name)) recordWrite(s, msg.model, t, input.file_path ?? input.notebook_path, o.cwd);
        else if (b.name === 'Bash' || b.name === 'PowerShell') for (const p of shellWrites(input.command)) recordWrite(s, msg.model, t, p, o.cwd);
      }
    }
    if (o.type === 'assistant' && msg?.stop_reason) s.offen = msg.stop_reason !== 'end_turn';
    else if (o.type === 'user' && !o.isMeta) s.offen = true;
    s.events.push({ t, user: claudeUserInput(o, subagent) });
  }
  for (const r of requests.values()) addUsage(s, r.model, r.t, r);
  return timeline(s);
}

// Codex: ~/.codex/sessions/JJJJ/MM/TT/rollout-*.jsonl (Unterthreads haben eigene Dateien)
export function parseCodex(file, text) {
  const s = newSession('Codex', file);
  let model = 'unbekannt', cwd = null;
  const seen = new Set(), fallback = [];
  for (const o of lines(text)) {
    const t = Date.parse(o.timestamp), p = o.payload ?? {};
    let user = false;
    if (o.type === 'session_meta') { cwd = p.cwd ?? cwd; if (hasMarker(p.cwd)) s.relevant = true; s.id = p.id ?? s.id; }
    else if (o.type === 'turn_context') { model = p.model ?? model; cwd = p.cwd ?? cwd; if (hasMarker(p.cwd)) s.relevant = true; }
    // Modell und Arbeitsordner gelten weiter, Verbrauch und Ereignisse vor Projektbeginn nicht.
    if (vorStart(t)) continue;
    if (o.type === 'response_item' && /call$/.test(p.type ?? '')) {
      // Ältere Sitzungen speichern den Patch JSON-maskiert (\n als Zeichenfolge).
      const input = String(p.input ?? p.arguments ?? '').replace(/(?<!\\)\\n/g, '\n');
      for (const m of input.matchAll(/\*\*\* (?:Update|Add|Delete) File: ([^\n`]+)|\*\*\* Move to: ([^\n`]+)/g)) recordWrite(s, model, t, (m[1] ?? m[2]).trim(), cwd);
      if (!/apply_patch/.test(input)) for (const w of shellWrites(input)) recordWrite(s, model, t, w, cwd);
    }
    else if (o.type === 'token_usage_record' && p.usage && !seen.has(p.response_id)) {
      seen.add(p.response_id);
      const u = p.usage, cached = u.cached_input_tokens ?? 0, write = u.cache_write_input_tokens ?? 0;
      addUsage(s, model, t, { eingabe: Math.max(0, (u.input_tokens ?? 0) - cached - write), cacheLesen: cached,
        cacheSchreiben5m: 0, cacheSchreiben1h: 0, cacheSchreiben: write, ausgabe: u.output_tokens ?? 0 });
    } else if (o.type === 'event_msg' && p.type === 'token_count' && p.info?.last_token_usage) fallback.push({ model, t, u: p.info.last_token_usage });
    else if (o.type === 'event_msg' && (p.type === 'task_started' || p.type === 'user_message')) user = true;
    if (o.type === 'response_item' && !s.relevant && /call$/.test(p.type ?? '')) s.relevant = hasMarker(p.arguments ?? p.input);
    if (o.type === 'event_msg' && p.type === 'task_started') s.offen = true;
    else if (o.type === 'event_msg' && /^(task_complete|turn_aborted)$/.test(p.type ?? '')) s.offen = false;
    s.events.push({ t, user });
  }
  if (seen.size === 0) for (const { model: m, t, u } of fallback) {
    const cached = u.cached_input_tokens ?? 0;
    addUsage(s, m, t, { eingabe: Math.max(0, (u.input_tokens ?? 0) - cached), cacheLesen: cached,
      cacheSchreiben5m: 0, cacheSchreiben1h: 0, cacheSchreiben: u.cache_write_input_tokens ?? 0, ausgabe: u.output_tokens ?? 0 });
  }
  return timeline(s);
}

// Gemini/Antigravity: ~/.gemini/antigravity/brain/<id>/.system_generated/logs/chunks/transcript_full/*.jsonl
export function parseGemini(dir, text) {
  const s = newSession('Gemini', dir);
  const steps = new Map();
  let lastModel = null;
  const ohneModell = [];
  for (const o of lines(text)) steps.set(o.step_index ?? steps.size, o);
  for (const o of steps.values()) {
    const t = Date.parse(o.created_at);
    if (vorStart(t)) { if (o.model) lastModel = o.model; continue; }
    if (!s.relevant) s.relevant = hasMarker(o.content) || hasMarker(JSON.stringify(o.tool_calls ?? ''));
    // Antigravity speichert Argumente als JSON-Strings: '"e:\\HTML\\…"'.
    const arg = v => { if (typeof v !== 'string') return v; try { return JSON.parse(v); } catch { return v; } };
    for (const c of o.tool_calls ?? []) {
      const a = c.args ?? {};
      if (/^(edit_file|write_file|write_to_file|replace_file_content|multi_replace_file_content|create_file|delete_file)$/.test(c.name ?? ''))
        recordWrite(s, o.model ?? lastModel, t, arg(a.Path ?? a.TargetFile ?? a.AbsolutePath ?? a.FilePath), null);
      else if (c.name === 'run_command') for (const w of shellWrites(arg(a.CommandLine))) recordWrite(s, o.model ?? lastModel, t, w, arg(a.Cwd));
    }
    if (o.model) lastModel = o.model;
    if (lastModel) for (const { t: tp, u } of ohneModell.splice(0)) addUsage(s, lastModel, tp, u);
    // Antigravity zählt input_tokens OHNE den Cache: Prompt = input_tokens + cache_read_tokens
    // (cache_read eines Schritts ≈ ganzer Prompt des vorigen). output_tokens enthält die Denk-Token.
    if (o.input_tokens != null) {
      const u = { eingabe: o.input_tokens, cacheLesen: o.cache_read_tokens ?? 0,
        cacheSchreiben5m: 0, cacheSchreiben1h: 0, cacheSchreiben: 0, ausgabe: o.output_tokens ?? 0 };
      // Schritte ohne Modellangabe gehören zum Modell der Sitzung (vorheriges, sonst erstes späteres).
      if (lastModel) addUsage(s, lastModel, t, u); else ohneModell.push({ t, u });
    }
    s.events.push({ t, user: o.type === 'USER_INPUT' && String(o.source ?? '').startsWith('USER') });
    s.offen = o.type === 'USER_INPUT' || (o.tool_calls?.length ?? 0) > 0 || (o.status != null && o.status !== 'DONE');
  }
  return timeline(s);
}

// ---------------------------------------------------------------------------------------------
// Quellen finden (nur Dateien ab Projektbeginn)
function walk(dir, accept, out = []) {
  let entries; try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return out; }
  for (const e of entries) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, accept, out); else if (accept(p)) out.push(p);
  }
  return out;
}
const statOf = p => { try { const st = fs.statSync(p); return { size: st.size, mtimeMs: Math.round(st.mtimeMs) }; } catch { return null; } };

function sources() {
  const list = [];
  const claudeRoot = path.join(HOME, '.claude', 'projects');
  for (const f of walk(claudeRoot, p => p.endsWith('.jsonl'))) list.push({ key: f, kind: 'claude', files: [f] });
  for (const sub of ['sessions', 'archived_sessions'])
    for (const f of walk(path.join(HOME, '.codex', sub), p => /rollout-.*\.jsonl$/.test(p))) list.push({ key: f, kind: 'codex', files: [f] });
  const brain = path.join(HOME, '.gemini', 'antigravity', 'brain');
  let ids = []; try { ids = fs.readdirSync(brain); } catch {}
  for (const id of ids) {
    const chunks = path.join(brain, id, '.system_generated', 'logs', 'chunks');
    const full = walk(path.join(chunks, 'transcript_full'), p => p.endsWith('.jsonl')).sort();
    const files = full.length ? full : walk(path.join(chunks, 'transcript'), p => p.endsWith('.jsonl')).sort();
    if (files.length) list.push({ key: path.join(brain, id), kind: 'gemini', files });
  }
  return list;
}

// ---------------------------------------------------------------------------------------------
// Auswertung mit Zwischenspeicher: unveränderte Dateien werden nicht erneut gelesen. Sitzungen,
// deren Protokoll inzwischen gelöscht wurde (Claude Code räumt nach 30 Tagen auf), bleiben erhalten.
export function analyse({ voll = false } = {}) {
  const ledger = readJson(FILES.sitzungen, { version: 1, preise: '', dateien: {} });
  const fresh = voll || ledger.preise !== PRICE_HASH;
  const seen = new Set();
  let gelesen = 0;
  for (const src of sources()) {
    const stats = src.files.map(statOf);
    if (stats.some(st => !st)) continue;
    const mtime = Math.max(...stats.map(st => st.mtimeMs));
    if (mtime < PROJECT_START) continue;
    const sig = stats.map(st => `${st.size}:${st.mtimeMs}`).join('|');
    const key = anon(src.key);
    seen.add(key);
    const old = ledger.dateien[key];
    if (old && old.sig === sig && !fresh) continue;
    const text = src.files.map(f => fs.readFileSync(f, 'utf8')).join('\n');
    if (!hasMarker(text)) { ledger.dateien[key] = { sig, relevant: false }; continue; }
    gelesen++;
    const s = src.kind === 'claude' ? parseClaude(src.key, text) : src.kind === 'codex' ? parseCodex(src.key, text) : parseGemini(src.key, text);
    ledger.dateien[key] = s.relevant ? { sig, relevant: true, sitzung: s } : { sig, relevant: false };
  }
  for (const [key, rec] of Object.entries(ledger.dateien)) if (!seen.has(key) && rec.relevant) rec.fehlt = true;
  ledger.preise = PRICE_HASH;
  return { ledger, gelesen };
}

// ---------------------------------------------------------------------------------------------
// Summen
function mergeIntervals(list) {
  const sorted = list.map(i => [...i]).sort((a, b) => a[0] - b[0]);
  const out = [];
  for (const i of sorted) { const last = out.at(-1); if (last && i[0] <= last[1]) last[1] = Math.max(last[1], i[1]); else out.push(i); }
  return out.reduce((sum, [a, b]) => sum + (b - a), 0);
}

export function totals(ledger) {
  const sessions = Object.values(ledger.dateien).filter(r => r.relevant).map(r => r.sitzung);
  const modelle = {}, werkzeuge = {};
  for (const s of sessions) {
    const w = (werkzeuge[s.werkzeug] ??= { sitzungen: 0, arbeitMs: 0, leerlaufMs: 0, intervalle: [], usd: 0, erste: Infinity, letzte: 0 });
    w.sitzungen++; w.arbeitMs += s.arbeitMs; w.leerlaufMs += s.leerlaufMs; w.intervalle.push(...s.intervalle);
    if (s.start) w.erste = Math.min(w.erste, s.start); if (s.ende) w.letzte = Math.max(w.letzte, s.ende);
    // Die Arbeitszeit einer Sitzung zählt für ihr Hauptmodell (höchste Kosten).
    const entries = Object.entries(s.modelle);
    const lead = entries.reduce((best, e) => (!best || e[1].usd > best[1].usd ? e : best), null)?.[0];
    for (const [id, m] of entries) {
      const key = `${s.werkzeug}|${m.name}`;
      const t = (modelle[key] ??= { werkzeug: s.werkzeug, name: m.name, anbieter: m.anbieter, ids: [], anfragen: 0, eingabe: 0,
        cacheLesen: 0, cacheSchreiben: 0, ausgabe: 0, usd: 0, ohnePreis: false, arbeitMs: 0, dateien: {}, schreibSitzungen: {} });
      if (!t.ids.includes(id)) t.ids.push(id);
      for (const k of ['anfragen', 'eingabe', 'cacheLesen', 'cacheSchreiben', 'ausgabe', 'usd']) t[k] += m[k];
      t.ohnePreis ||= m.ohnePreis;
      if (id === lead) t.arbeitMs += s.arbeitMs;
      for (const [f, ts] of Object.entries(m.dateien ?? {})) {
        t.dateien[f] = Math.max(t.dateien[f] ?? 0, ts);
        t.schreibSitzungen[f] = (t.schreibSitzungen[f] ?? 0) + 1;
      }
      w.usd += m.usd;
    }
  }
  const all = Object.values(werkzeuge);
  // Token wie im Änderungsprotokoll: Eingabe (neu + Cache gelesen + Cache geschrieben) + Ausgabe (inkl. Denk-Token).
  const KEYS = ['anfragen', 'eingabe', 'cacheLesen', 'cacheSchreiben', 'ausgabe', 'token', 'usd', 'arbeitMs'];
  const liste = Object.values(modelle).sort((a, b) => b.usd - a.usd);
  const anbieter = {};
  for (const m of liste) {
    m.token = m.eingabe + m.cacheLesen + m.cacheSchreiben + m.ausgabe;
    const a = (anbieter[m.anbieter] ??= { name: m.anbieter, ...Object.fromEntries(KEYS.map(k => [k, 0])), modelle: [] });
    for (const k of KEYS) a[k] += m[k];
    a.modelle.push(m.name);
  }
  const summe = { ...Object.fromEntries(KEYS.map(k => [k, liste.reduce((s, m) => s + m[k], 0)])),
    usd: all.reduce((a, w) => a + w.usd, 0), arbeitMs: all.reduce((a, w) => a + w.arbeitMs, 0),
    leerlaufMs: all.reduce((a, w) => a + w.leerlaufMs, 0), wandzeitMs: mergeIntervals(all.flatMap(w => w.intervalle)) };
  for (const w of all) { w.wandzeitMs = mergeIntervals(w.intervalle); delete w.intervalle; }
  return { modelle: liste, anbieter: Object.values(anbieter).sort((a, b) => b.usd - a.usd), werkzeuge, summe };
}

// Kompakte Sicht für das Protokoll: Kosten je Modell, Zeiten je Werkzeug
const snapshot = t => ({
  usd: t.summe.usd, arbeitMs: t.summe.arbeitMs, leerlaufMs: t.summe.leerlaufMs,
  modelle: Object.fromEntries(t.modelle.map(m => [`${m.werkzeug} · ${m.name}`, +m.usd.toFixed(4)])),
  werkzeuge: Object.fromEntries(Object.entries(t.werkzeuge).map(([k, w]) => [k, { arbeitMs: w.arbeitMs, leerlaufMs: w.leerlaufMs }])),
});

export function delta(t, log) {
  const prev = log.at(-1)?.stand ?? { usd: 0, arbeitMs: 0, leerlaufMs: 0, modelle: {}, werkzeuge: {} };
  const cur = snapshot(t);
  const modelle = {};
  for (const [k, v] of Object.entries(cur.modelle)) { const d = v - (prev.modelle[k] ?? 0); if (Math.abs(d) >= 0.005) modelle[k] = +d.toFixed(2); }
  const werkzeuge = {};
  for (const [k, w] of Object.entries(cur.werkzeuge)) {
    const p = prev.werkzeuge[k] ?? { arbeitMs: 0, leerlaufMs: 0 };
    const a = w.arbeitMs - p.arbeitMs, l = w.leerlaufMs - p.leerlaufMs;
    if (a > 30e3 || l > 30e3) werkzeuge[k] = { arbeit: fmtDur(a), leerlauf: fmtDur(l) };
  }
  // Wer hat seit dem letzten Eintrag was wo geleistet: Kosten und geschriebene Dateien je Modell.
  const since = Date.parse(log.at(-1)?.zeit ?? '') || 0;
  const beitraege = {};
  for (const m of t.modelle) {
    const key = `${m.werkzeug} · ${m.name}`;
    const files = Object.entries(m.dateien).filter(([, ts]) => ts > since).sort((a, b) => b[1] - a[1]).map(([f]) => f);
    if (modelle[key] || files.length) beitraege[key] = { usd: modelle[key] ?? 0, dateien: files };
  }
  return { seit: log.at(-1)?.zeit ?? null, usd: +(cur.usd - prev.usd).toFixed(2), modelle, werkzeuge, beitraege, stand: cur };
}

// Hauptdateien eines Modells: am häufigsten geschrieben, bei Gleichstand die jüngsten.
const mainFiles = (m, n = 3) => Object.keys(m.dateien)
  .sort((a, b) => (m.schreibSitzungen[b] - m.schreibSitzungen[a]) || (m.dateien[b] - m.dateien[a])).slice(0, n);

// ---------------------------------------------------------------------------------------------
// Darstellung (README ist englisch)
export function fmtDur(ms) {
  const m = Math.round(Math.max(0, ms) / 6e4);
  return m < 60 ? `${m} min` : `${Math.floor(m / 60)} h ${String(m % 60).padStart(2, '0')} min`;
}
const usd = v => `$${v.toFixed(v < 10 ? 2 : 0).replace(/\B(?=(\d{3})+(?!\d))/g, ',')}`;
const mtok = v => v >= 1e9 ? `${(v / 1e9).toFixed(2)} B` : v >= 1e6 ? `${(v / 1e6).toFixed(1)} M` : `${Math.round(v / 1e3)} k`;
// Durchschnittspreis: effektive Kosten je 1 Mio. Token über alle Tokenarten.
const perM = (cost, tok) => tok > 0 ? `$${(cost * 1e6 / tok).toFixed(2)}` : '–';
const AI_NAME = { Anthropic: 'Claude', OpenAI: 'GPT', Google: 'Gemini' };
const stamp = ms => {
  const d = new Date(ms), off = -d.getTimezoneOffset(), pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())} (UTC${off >= 0 ? '+' : '-'}${Math.abs(off / 60)})`;
};

const files = list => list.map(f => `\`${path.posix.basename(f)}\``).join(', ');
const signed = v => `${v < 0 ? '−' : '+'}${usd(Math.abs(v))}`;

function render(t, log) {
  const eur = t.summe.usd / PRICES.usdProEur;
  const rows = t.modelle.filter(m => m.anfragen > 0).map(m =>
    `| ${m.name}${m.ohnePreis ? ' ⚠️ no price' : ''} | ${m.werkzeug} | ${m.anfragen.toLocaleString('en-US')} | ${mtok(m.eingabe + m.cacheLesen + m.cacheSchreiben)} (${mtok(m.cacheLesen)} cached) | ${mtok(m.ausgabe)} | ${mtok(m.token)} | ${usd(m.usd)} | ${perM(m.usd, m.token)} | ${fmtDur(m.arbeitMs)} | ${files(mainFiles(m)) || '–'} |`);
  const ais = t.anbieter.map(a =>
    `| ${AI_NAME[a.name] ?? a.name} (${a.name}) | ${a.modelle.length} | ${mtok(a.token)} | ${(a.token / t.summe.token * 100).toFixed(0)} % | ${usd(a.usd)} | ${(a.usd / t.summe.usd * 100).toFixed(0)} % | ${perM(a.usd, a.token)} |`);
  const order = ['Claude Code', 'Codex', 'Gemini'];
  const tools = Object.entries(t.werkzeuge).sort((a, b) => order.indexOf(a[0]) - order.indexOf(b[0])).map(([k, w]) =>
    `| ${k} | ${w.sitzungen} | ${usd(w.usd)} | ${fmtDur(w.arbeitMs)} | ${fmtDur(w.wandzeitMs)} | ${fmtDur(w.leerlaufMs)} |`);
  const recent = log.slice(-8).reverse().flatMap(e => {
    const head = `- **${stamp(Date.parse(e.zeit))}** · ${e.text}`;
    if (!e.beitraege) {
      const costs = Object.entries(e.delta.modelle).map(([k, v]) => `${k.split(' · ')[1]} ${signed(v)}`).join(', ') || 'no new cost';
      return [`${head} · ${costs}`];
    }
    return [head, ...Object.entries(e.beitraege).map(([k, b]) => {
      const [tool, name] = k.split(' · ');
      const where = b.dateien.length ? ` · ${files(b.dateien.slice(0, 4))}${b.dateien.length > 4 ? ` +${b.dateien.length - 4}` : ''}` : '';
      return `  - **${name}** (${tool}) ${signed(b.usd)}${b.was ? ` — ${b.was}` : ''}${where}`;
    })];
  });
  return [START,
    '### Effective cost & agent time (estimated)',
    '',
    `*Who did what, where, and what it effectively cost. **Effective cost** = the tokens each model used, priced at public **API list prices** (USD); the subscriptions actually paid (Claude, ChatGPT/Codex, Gemini) are not deducted. **Tokens** = input (new, cache reads and cache writes) + output (including reasoning), counted as in the change log; **Ø per 1M** = effective cost ÷ tokens, an average over all token kinds (far below the input list price because most input is read from the prompt cache). Read from the local session logs of Claude Code, Codex and Gemini (Antigravity) from the project start on. Agent time = the agents working, credited to each session's main model; idle = waiting for user input (gaps over ${PRICES.zeit.leerlaufMaxMin} min count as breaks). Rough estimates, not an invoice; the change log above stays the measured token record. The **ⓘ KI-Token & Kosten** button in the game's main menu shows the same figures (\`public/token-info.json\`). Maintained by the \`kostenbuch\` Haiku agent (\`tools/kostenbuch/\`), last update ${stamp(Date.now())}.*`,
    '',
    `**Total effective cost: ${usd(t.summe.usd)} (≈ ${eur.toFixed(0)} €)** for ${mtok(t.summe.token)} tokens (Ø ${perM(t.summe.usd, t.summe.token)} per 1M) · agent working time ${fmtDur(t.summe.arbeitMs)} (wall clock ${fmtDur(t.summe.wandzeitMs)}) · idle ${fmtDur(t.summe.leerlaufMs)}`,
    '',
    '| AI | Models | Tokens | Token share | Effective cost | Cost share | Ø per 1M tokens |',
    '| --- | ---: | ---: | ---: | ---: | ---: | ---: |',
    ...ais,
    '',
    '| Tool | Sessions | Effective cost | Agent time | Wall clock | Idle |',
    '| --- | ---: | ---: | ---: | ---: | ---: |',
    ...tools,
    '',
    '| Model | Tool | Requests | Input tokens | Output tokens | Total tokens | Effective cost | Ø per 1M | Agent time | Main files written |',
    '| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |',
    ...rows,
    '',
    ...(recent.length ? ['**Who did what, where** (latest entries)', '', ...recent, ''] : []),
    `<sub>Prices per 1M tokens and sources: \`tools/kostenbuch/preise.json\`. Agent time sums parallel agents; wall clock counts overlapping work once. Files are attributed from each tool's edit calls (shell edits by heuristic).</sub>`,
    END].join('\n');
}

// Der Abschnitt steht unter dem Änderungsprotokoll des Benchmarks (tools/benchmark-totals.mjs pflegt
// dessen Summen). Andere Agenten schreiben dieselbe README: geschrieben wird nur, wenn sie sich während
// der Berechnung nicht geändert hat.
export function writeReadme(t, log) {
  const ANCHOR = '<!-- /ai-benchmark:changes -->';
  for (let attempt = 0; attempt < 3; attempt++) {
    const before = fs.readFileSync(FILES.readme, 'utf8');
    const eol = before.includes('\r\n') ? '\r\n' : '\n';
    const block = render(t, log).replace(/\n/g, eol);
    const a = before.indexOf(START), b = before.indexOf(END), i = before.indexOf(ANCHOR);
    const text = a >= 0 && b > a ? before.slice(0, a) + block + before.slice(b + END.length)
      : i >= 0 ? before.slice(0, i + ANCHOR.length) + eol + eol + block + before.slice(i + ANCHOR.length)
      : before.replace(/\s*$/, eol + eol + block + eol);
    if (fs.readFileSync(FILES.readme, 'utf8') !== before) continue;
    fs.writeFileSync(FILES.readme, text);
    return;
  }
  throw new Error('README wurde während der Aktualisierung geändert. Bitte erneut ausführen.');
}

// Für den Info-Schalter im Hauptmenü (public/token-info.js): nur Summen je Anbieter und Modell, keine
// Sitzungs-IDs, Pfade oder Dateien. Geschrieben wird nur, wenn sich die Zahlen geändert haben.
export const TOKEN_INFO = path.join(ROOT, 'public', 'token-info.json');
export function tokenInfo(t, now = Date.now()) {
  const pick = o => ({ anfragen: o.anfragen, eingabe: o.eingabe, cacheLesen: o.cacheLesen, cacheSchreiben: o.cacheSchreiben,
    ausgabe: o.ausgabe, token: o.token, usd: +o.usd.toFixed(2), arbeitMs: Math.round(o.arbeitMs) });
  return {
    stand: new Date(now).toISOString(),
    usdProEur: PRICES.usdProEur,
    summe: pick(t.summe),
    anbieter: t.anbieter.map(a => ({ name: a.name, ...pick(a) })),
    modelle: t.modelle.filter(m => m.anfragen > 0).map(m => {
      const p = priceFor(m.ids[0], now);
      return { name: m.name, anbieter: m.anbieter, werkzeug: m.werkzeug, ...pick(m),
        liste: p ? { eingabe: p.eingabe, ausgabe: p.ausgabe, cacheLesen: p.cacheLesen ?? p.eingabe } : null };
    }),
  };
}
export function writeTokenInfo(t) {
  const info = tokenInfo(t);
  const old = readJson(TOKEN_INFO, null);
  if (old && JSON.stringify({ ...old, stand: '' }) === JSON.stringify({ ...info, stand: '' })) return false;
  writeJson(TOKEN_INFO, info);
  return true;
}

// ---------------------------------------------------------------------------------------------
// Projektdateien laut git (ohne die Kostenbuch-Dateien) mit Größe und Änderungszeit.
function projectFiles() {
  let status = '';
  try { status = execFileSync('git', ['status', '--porcelain', '-uall'], { cwd: ROOT, encoding: 'utf8', maxBuffer: 64e6 }); } catch {}
  const out = [];
  for (const line of status.split('\n')) {
    const file = line.slice(3).trim().replace(/^"|"$/g, '');
    if (!file || file === 'README.md' || file === 'public/token-info.json' || file.startsWith('tools/kostenbuch/') || file.startsWith('.claude/')) continue;
    out.push({ code: line.slice(0, 2), file, st: statOf(path.join(ROOT, file)) });
  }
  return out;
}

// Dateien, die seit dem letzten Protokolleintrag geändert wurden (Stoff für die Zusammenfassung des Agenten).
export const changedSince = ms => projectFiles().filter(f => !f.st || f.st.mtimeMs > ms).map(f => f.file);

// Fingerabdruck des Projekts – der Stop-Hook vergleicht ihn mit dem Stand des letzten Eintrags.
export function fingerprint() {
  const parts = projectFiles().map(f => `${f.code} ${f.file} ${f.st ? `${f.st.size}:${f.st.mtimeMs}` : '-'}`);
  let head = ''; try { head = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: ROOT, encoding: 'utf8' }).trim(); } catch {}
  return crypto.createHash('sha1').update(head + '\n' + parts.join('\n')).digest('hex');
}

// ---------------------------------------------------------------------------------------------
// Aktivität: Wer arbeitet gerade am Projekt? Liest nur seit dem letzten Lauf geänderte
// Protokolle. Eine Sitzung "arbeitet", wenn ihr letztes Ereignis jünger als ruheMin ist oder ihr
// Agent noch am Zug ist (offener Turn, höchstens 15 min still). Reine Haiku-Hilfssitzungen zählen nicht.
// `selbst`: Sitzungs-ID der aufrufenden Claude-Code-Sitzung (samt ihrer Subagenten).
export function aktivitaet({ selbst = null, ruheMin = 3, fensterMin = 30 } = {}) {
  const { ledger, gelesen } = analyse();
  if (gelesen > 0) try { writeJson(FILES.sitzungen, ledger); } catch {}
  const now = Date.now();
  const sitzungen = [];
  for (const rec of Object.values(ledger.dateien)) {
    const s = rec.sitzung;
    if (!rec.relevant || !s?.ende || now - s.ende > fensterMin * 6e4) continue;
    const models = Object.values(s.modelle).sort((a, b) => b.usd - a.usd);
    if (models.length && models.every(m => /haiku/i.test(m.name))) continue;
    const alter = now - s.ende;
    const arbeitet = alter < ruheMin * 6e4 || (s.offen && alter < 15 * 6e4);
    const dateien = [...new Set(models.flatMap(m => Object.entries(m.dateien ?? {}).filter(([, ts]) => now - ts < fensterMin * 6e4).map(([f]) => f)))];
    sitzungen.push({ werkzeug: s.werkzeug, modell: models[0]?.name ?? '?', status: arbeitet ? 'arbeitet' : 'ruhig',
      letzteAktivitaetVorMin: +(alter / 6e4).toFixed(1), turnOffen: !!s.offen, dateien,
      selbst: !!selbst && s.datei.includes(selbst) });
  }
  sitzungen.sort((a, b) => a.letzteAktivitaetVorMin - b.letzteAktivitaetVorMin);
  const andere = sitzungen.filter(x => !x.selbst && x.status === 'arbeitet');
  return { zeit: new Date(now).toISOString(), belegt: andere.length > 0,
    belegteDateien: [...new Set(andere.flatMap(x => x.dateien))], sitzungen };
}

// Claude-Code-CLI: KOSTENBUCH_CLAUDE, sonst `claude` im PATH, sonst die neueste VS-Code-Erweiterung.
export function findClaude() {
  if (process.env.KOSTENBUCH_CLAUDE) return process.env.KOSTENBUCH_CLAUDE;
  const exe = process.platform === 'win32' ? 'claude.exe' : 'claude';
  for (const dir of (process.env.PATH ?? '').split(path.delimiter)) {
    const p = path.join(dir, exe);
    if (fs.existsSync(p)) return p;
  }
  const ext = path.join(HOME, '.vscode', 'extensions');
  let dirs = [];
  try { dirs = fs.readdirSync(ext).filter(d => d.startsWith('anthropic.claude-code-')); } catch {}
  const version = d => d.match(/(\d+)\.(\d+)\.(\d+)/)?.slice(1).map(Number) ?? [0, 0, 0];
  dirs.sort((a, b) => { const x = version(a), y = version(b); return x[0] - y[0] || x[1] - y[1] || x[2] - y[2]; });
  for (const d of dirs.reverse()) {
    const p = path.join(ext, d, 'resources', 'native-binary', exe);
    if (fs.existsSync(p)) return p;
  }
  return null;
}

// ---------------------------------------------------------------------------------------------
function main() {
  const args = process.argv.slice(2);
  if (args.includes('--aktiv')) {
    const i = args.indexOf('--selbst');
    console.log(JSON.stringify(aktivitaet({ selbst: i >= 0 ? args[i + 1] : process.env.KOSTENBUCH_SELBST ?? null }), null, 1));
    return;
  }
  const logIdx = args.indexOf('--log');
  const text = logIdx >= 0 ? (args[logIdx + 1] ?? '').trim() : null;
  // --was "Werkzeug · Modell=Was es geleistet hat" (mehrfach): Beschreibung je Modell für den Eintrag.
  const was = {};
  args.forEach((a, i) => { if (a === '--was' && args[i + 1]?.includes('=')) { const [k, ...v] = args[i + 1].split('='); was[k.trim()] = v.join('=').trim(); } });
  const { ledger, gelesen } = analyse({ voll: args.includes('--voll') });
  const t = totals(ledger);
  const log = readJson(FILES.protokoll, []);
  const d = delta(t, log);
  const geaendert = changedSince(Date.parse(log.at(-1)?.zeit ?? '') || 0);
  writeJson(FILES.sitzungen, ledger);
  if (text !== null) {
    const beitraege = Object.fromEntries(Object.entries(d.beitraege).map(([k, b]) =>
      [k, { usd: b.usd, dateien: b.dateien.slice(0, 12), ...(was[k] ? { was: was[k] } : {}) }]));
    log.push({ zeit: new Date().toISOString(), text: text || 'Project files changed.', beitraege, delta: { usd: d.usd, modelle: d.modelle, werkzeuge: d.werkzeuge }, stand: d.stand });
    writeJson(FILES.protokoll, log);
  }
  if (text !== null || args.includes('--write')) {
    writeReadme(t, log);
    writeTokenInfo(t);
    writeJson(FILES.zustand, { fingerprint: fingerprint(), aktualisiert: new Date().toISOString() });
  }
  const out = {
    geaenderteDateien: geaendert.length > 40 ? [...geaendert.slice(0, 40), `... und ${geaendert.length - 40} weitere`] : geaendert,
    gelesen, gesamtUsd: +t.summe.usd.toFixed(2), arbeit: fmtDur(t.summe.arbeitMs), leerlauf: fmtDur(t.summe.leerlaufMs),
    seitLetztemEintrag: { seit: d.seit, usd: d.usd, werkzeuge: d.werkzeuge },
    // Je Modell: neue Kosten und seither geschriebene Projektdateien. Der Agent beschreibt jeden Schlüssel mit --was.
    beitraege: Object.fromEntries(Object.entries(d.beitraege).map(([k, b]) => [k, { usd: b.usd, dateien: b.dateien.slice(0, 15) }])),
    ohnePreis: t.modelle.filter(m => m.ohnePreis).map(m => m.ids.join(', ')),
  };
  console.log(JSON.stringify(out, null, 1));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
