import test from "node:test";
import assert from "node:assert/strict";
import { summarize } from "../build/Bench.js";

test("Benchmark aggregates FPS by elapsed time and keeps real stalls in the tail", () => {
  const samples = [180, ...Array(95).fill(10), ...Array(4).fill(25)];
  const row = summarize("Hoch", samples);
  assert.equal(row.tier, "Hoch");
  assert.equal(row.avgFps, 81.30);
  assert.equal(row.p95ms, 10);
  assert.equal(row.p99ms, 25);
  assert.equal(row.maxMs, 180);
});
