import test from 'node:test';
import assert from 'node:assert/strict';
import { adjustScale, autoTier, frameMean, key, label, parse, preset, resolution, validFrame } from '../build/GraphicsQuality.js';

test('presets round-trip storage keys and retain the original high settings', () => {
  const keys = ['low', 'medium', 'high', 'ultra'];
  const tiers = keys.map(parse);
  assert.deepEqual(tiers.map(key), keys);
  assert.deepEqual(tiers.map(label), ['Niedrig', 'Mittel', 'Hoch', 'Ultra']);
  assert.equal(parse(null), undefined);
  assert.equal(parse('damaged'), undefined);
  const settings = tiers.map(preset);
  assert.deepEqual(settings.map(p => p.Shadows), [false, true, true, true]);
  assert.deepEqual(settings.map(p => p.Msaa), [false, false, true, true]);
  assert.deepEqual(settings.map(p => p.BloomLevels), [1, 2, 3, 3]);
  assert.deepEqual(settings.map(p => p.ShadowSize), [512, 1024, 2048, 4096]);
  assert.deepEqual(settings.map(p => p.DprLimit), [1, 1.5, 2, Infinity]);
  assert.deepEqual(settings.map(p => p.RenderScale), [.7, .85, 1, 1]);
  assert.equal(settings[2].SmallObjectDistance, Infinity);
  for (const field of ['SmallObjectDistance', 'PointLights', 'Particles']) {
    assert.ok(settings.every((p, i) => i === 0 || p[field] >= settings[i - 1][field]));
  }
});

test('auto selection uses foreground flight timings and discounts isolated stalls', () => {
  assert.deepEqual([0, -1, NaN, 500, 3000].map(validFrame), Array(5).fill(false));
  assert.equal(validFrame(80), true);
  assert.equal(frameMean(10 * 16 + 140, 11, 140), 16);
  assert.equal(frameMean(240, 3, 80), 80);
  assert.deepEqual([8.3, 16.7, 24, 42].map(t => key(autoTier(t))), ['ultra', 'high', 'medium', 'low']);
});

test('resolution needs sustained pressure to fall and a full stable window to recover', () => {
  const steady = Array(8).fill(16.7);
  const slow = [21, 19, ...steady.slice(2)];
  const initial = resolution(.8);
  assert.equal(adjustScale(1, 3000, [40, ...steady.slice(1)], initial).Scale, .8);
  assert.equal(adjustScale(1, 3000, slow, initial).Scale, .75);
  assert.equal(adjustScale(1, 3000, [...steady.slice(1), 0], initial).Scale, .8);
  assert.equal(adjustScale(1, 3000, [...steady.slice(1), 17.9], initial).Scale, .8);
  const raised = adjustScale(1, 3000, steady, initial);
  assert.equal(raised.Scale, .85);
  const reverted = adjustScale(1, 4050, slow, raised);
  assert.equal(reverted.Scale, .8);
  assert.equal(adjustScale(1, 7000, steady, reverted).Scale, .8);
  assert.equal(adjustScale(1, 20000, steady, reverted).Scale, .85);
  assert.equal(adjustScale(.7, 3000, steady, resolution(.7)).Scale, .7);
  assert.equal(adjustScale(1, 3000, slow, resolution(.5)).Scale, .5);
});
