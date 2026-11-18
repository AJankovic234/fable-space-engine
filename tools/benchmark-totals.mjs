import fs from 'node:fs';

// The change log in the README is the source for both project and model totals.
const readme = new URL('../README.md', import.meta.url);
const source = fs.readFileSync(readme, 'utf8');
const log = between('ai-benchmark:changes');
const oldTotals = between('ai-benchmark:totals');
const models = new Map();
const refs = new Set();

function between(name) {
  const start = '<!-- ' + name + ' -->', end = '<!-- /' + name + ' -->';
  const from = source.indexOf(start), to = source.indexOf(end);
  if (from < 0 || to < from || source.lastIndexOf(start) !== from || source.lastIndexOf(end) !== to) {
    throw new Error('Missing or repeated README marker: ' + name);
  }
  return source.slice(from + start.length, to);
}

for (const line of log.trim().split(/\r?\n/).slice(2)) {
  const row = line.split('|').map(cell => cell.trim());
  if (row.length !== 7 || row[0] || row[6]) throw new Error('Invalid benchmark row: ' + line);
  const [, date, model, change, tokens, reference] = row;
  if (!date || !model || !change || !reference || refs.has(reference)) throw new Error('Invalid or repeated entry: ' + reference);
  refs.add(reference);
  const record = models.get(model) ?? { measured: 0n, hasMeasurement: false, open: 0 };
  if (tokens === 'Open') record.open++;
  else {
    if (!/^(?:0|[1-9]\d{0,2}(?:,\d{3})*|[1-9]\d*)$/.test(tokens)) throw new Error('Invalid token count: ' + tokens);
    record.measured += BigInt(tokens.replaceAll(',', ''));
    record.hasMeasurement = true;
  }
  models.set(model, record);
}
if (models.size === 0) throw new Error('Empty benchmark log');

const format = n => n.toLocaleString('en-US');
const table = ['| Model | Recorded tokens | Entries awaiting counts |', '| --- | ---: | ---: |'];
let sum = 0n, open = 0;
for (const [model, stats] of models) {
  table.push(`| ${model} | ${stats.hasMeasurement ? format(stats.measured) : 'Open'} | ${stats.open} |`);
  sum += stats.measured;
  open += stats.open;
}
table.push(`| **Project · measured subtotal** | **${format(sum)}** | **${open}** |`);
const eol = source.includes('\r\n') ? '\r\n' : '\n';
const totals = eol + table.join(eol) + eol;
if (process.argv.includes('--write') && totals !== oldTotals) {
  if (fs.readFileSync(readme, 'utf8') !== source) throw new Error('README changed during calculation. Run again.');
  const marker = '<!-- ai-benchmark:totals -->';
  const start = source.indexOf(marker) + marker.length;
  fs.writeFileSync(readme, source.slice(0, start) + totals + source.slice(start + oldTotals.length));
} else if (totals !== oldTotals) {
  console.error('README totals are stale. Run node tools/benchmark-totals.mjs --write.');
  process.exitCode = 1;
}
console.log(`${refs.size} entries, ${format(sum)} measured tokens, ${open} awaiting counts.`);
