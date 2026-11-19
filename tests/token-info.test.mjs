// Token-Zählung des Kostenbuchs (tools/kostenbuch) und die Daten für den Info-Schalter im Hauptmenü.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// Das Kostenbuch legt beim Laden seinen lokalen Datenordner an: im Test ein Wegwerfordner.
process.env.KOSTENBUCH_DATA = fs.mkdtempSync(path.join(os.tmpdir(), 'kostenbuch-test-'));
const k = await import('../tools/kostenbuch/kostenbuch.mjs');
const jsonl = rows => rows.map(r => JSON.stringify(r)).join('\n');

test('Gemini: input_tokens is the uncached part, unlabelled steps count, steps before the project start do not', () => {
  const s = k.parseGemini('gemini-brain', jsonl([
    { step_index: 0, type: 'USER_INPUT', source: 'USER_EXPLICIT', created_at: '2026-11-10T10:00:00Z', content: 'anderes Projekt' },
    { step_index: 1, created_at: '2026-11-10T10:00:05Z', input_tokens: 5000, cache_read_tokens: 0, output_tokens: 100 },
    { step_index: 2, type: 'USER_INPUT', source: 'USER_EXPLICIT', created_at: '2026-11-17T10:00:00Z', content: 'e:\\fable-space-engine\\src' },
    { step_index: 3, created_at: '2026-11-17T10:00:05Z', input_tokens: 3000, cache_read_tokens: 10000, output_tokens: 200 },
    { step_index: 4, created_at: '2026-11-17T10:00:10Z', model: 'gemini-4-neon-medium', input_tokens: 1000, cache_read_tokens: 13000, output_tokens: 300 },
  ]));
  assert.equal(s.relevant, true);
  const m = s.modelle['gemini-4-neon-medium'];
  assert.ok(m, 'the step without a model label belongs to the session model');
  assert.equal(m.anfragen, 2);
  assert.equal(m.eingabe, 4000, 'cache reads are not subtracted a second time');
  assert.equal(m.cacheLesen, 23000);
  assert.equal(m.ausgabe, 500);
  // Gemini 4 Neon: $3 input, $0.15 cache read, $15 output per 1M tokens
  assert.ok(Math.abs(m.usd - (4000 * 3 + 23000 * 0.15 + 500 * 15) / 1e6) < 1e-9);
  assert.equal(Object.keys(s.modelle).length, 1, 'nothing from before the project start');
});

test('Codex: input_tokens includes cached input, reasoning is part of output', () => {
  const s = k.parseCodex('rollout-test.jsonl', jsonl([
    { timestamp: '2026-11-17T09:00:00Z', type: 'session_meta', payload: { cwd: 'E:\\fable-space-engine' } },
    { timestamp: '2026-11-17T09:00:00Z', type: 'turn_context', payload: { model: 'gpt-6.2-astra', cwd: 'E:\\fable-space-engine' } },
    { timestamp: '2026-11-17T09:00:05Z', type: 'token_usage_record', payload: { response_id: 'r1', usage: {
      input_tokens: 10000, cached_input_tokens: 9000, cache_write_input_tokens: 0, output_tokens: 50, reasoning_output_tokens: 20, total_tokens: 10050 } } },
    { timestamp: '2026-11-17T09:00:06Z', type: 'token_usage_record', payload: { response_id: 'r1', usage: {
      input_tokens: 10000, cached_input_tokens: 9000, cache_write_input_tokens: 0, output_tokens: 50, reasoning_output_tokens: 20, total_tokens: 10050 } } },
  ]));
  const m = s.modelle['gpt-6.2-astra'];
  assert.equal(m.anfragen, 1, 'each response is counted once');
  assert.equal(m.eingabe, 1000);
  assert.equal(m.cacheLesen, 9000);
  assert.equal(m.ausgabe, 50);
});

test('shell write heuristic ignores arrow functions, comparisons and stream redirects', () => {
  assert.deepEqual(k.shellWrites(`node tools/kostenbuch/kostenbuch.mjs | node -e 'process.stdin.on("data",d=>s+=d)'`), []);
  assert.deepEqual(k.shellWrites('node tools/benchmark-totals.mjs 2>&1'), []);
  assert.deepEqual(k.shellWrites('if (a >= 3) cat README.md > /dev/null'), []);
  assert.deepEqual(k.shellWrites('echo x > src/Main.fs'), ['src/Main.fs']);
});

test('totals add up tokens per model and per AI; the menu data holds only sums', () => {
  const gemini = k.parseGemini('g', jsonl([
    { step_index: 0, created_at: '2026-11-17T10:00:00Z', model: 'gemini-4-hydrogen-medium', content: 'fable-space-engine', input_tokens: 2000, cache_read_tokens: 8000, output_tokens: 100 },
  ]));
  const codex = k.parseCodex('c', jsonl([
    { timestamp: '2026-11-17T09:00:00Z', type: 'turn_context', payload: { model: 'gpt-6.2-sol', cwd: 'E:\\fable-space-engine' } },
    { timestamp: '2026-11-17T09:00:05Z', type: 'token_usage_record', payload: { response_id: 'r', usage: { input_tokens: 5000, cached_input_tokens: 4000, output_tokens: 70 } } },
  ]));
  const t = k.totals({ dateien: { a: { relevant: true, sitzung: gemini }, b: { relevant: true, sitzung: codex } } });
  assert.equal(t.summe.token, 2000 + 8000 + 100 + 5000 + 70);
  assert.deepEqual(t.anbieter.map(a => a.name).sort(), ['Google', 'OpenAI']);
  const info = k.tokenInfo(t, Date.parse('2026-11-19T12:00:00Z'));
  assert.equal(info.summe.token, info.modelle.reduce((s, m) => s + m.token, 0));
  assert.equal(info.modelle.find(m => m.name === 'GPT-6.2 Sol').liste.eingabe, 2);
  assert.doesNotMatch(JSON.stringify(info), /datei|dateien|ids|[A-Za-z]:\\|\/Users\//);
});

test('public/token-info.json is consistent and contains no session data', () => {
  const file = new URL('../public/token-info.json', import.meta.url);
  const text = fs.readFileSync(file, 'utf8');
  const d = JSON.parse(text);
  assert.ok(Number.isFinite(Date.parse(d.stand)));
  assert.ok(d.usdProEur > 0);
  const sum = key => d.modelle.reduce((s, m) => s + m[key], 0);
  for (const key of ['token', 'eingabe', 'cacheLesen', 'cacheSchreiben', 'ausgabe', 'anfragen']) {
    assert.equal(d.summe[key], sum(key), key);
    assert.equal(d.anbieter.reduce((s, a) => s + a[key], 0), d.summe[key], `${key} per AI`);
  }
  for (const m of d.modelle) assert.equal(m.token, m.eingabe + m.cacheLesen + m.cacheSchreiben + m.ausgabe, m.name);
  assert.ok(Math.abs(d.summe.usd - sum('usd')) < 0.05);
  // Öffentliches Repo: keine Pfade, Benutzernamen, Sitzungs- oder Konto-IDs.
  assert.doesNotMatch(text, /[A-Za-z]:\\|\/Users\/|\/home\/|~\/|[0-9a-f]{8}-[0-9a-f]{4}-|"dateien"|"ids"/i);
});
