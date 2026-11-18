import test from 'node:test';
import assert from 'node:assert/strict';
import {
  Sound_create as createSound,
  Sound_unlock as unlockSound,
  Sound_toggleMute as toggleMute,
  Sound_setVolumes as setVolumes,
  Sound_getVolumes as getVolumes,
  Sound_setListener as setListener,
  Sound_setFlightContext as setFlightContext,
  Sound_setSceneMode as setSceneMode,
  Sound_updateEngine as updateEngine,
  Sound_laserAt as laserAt,
  Sound_explosionAt as explosionAt,
  Sound_turretFireAt as turretFireAt,
  Sound_collisionImpact as collisionImpact,
  Sound_mineralCrack as mineralCrack,
  Sound_capitalHit as capitalHit,
  Sound_overdriveOn as overdriveOn,
  Sound_shieldBreak as shieldBreak,
  Sound_shieldRecharge as shieldRecharge,
  Sound_boostEmpty as boostEmpty,
  Sound_refit as refit,
  Sound_torpedoImpact as torpedoImpact,
} from '../build/Sound.js';
import { Vec3, QuatModule_identity as identity } from '../build/Math3D.js';
import { WorldModule_create as createWorld, WorldModule_reset as resetWorld, Physics_step as stepPhysics, LaserPoolModule_spawn as spawnLaser } from '../build/PhysicsAndPool.js';
import { CapitalFlight_create as createSortie, CapitalFlight_position as cruiserPos } from '../build/CapitalFlight.js';

function createAudioHarness() {
  const storage = new Map();
  const sources = [];
  const gains = [];
  const filters = [];
  const panners = [];

  class FakeParam {
    constructor(v = 0) {
      this.value = v;
      this.targets = [];
    }
    setTargetAtTime(v) {
      this.value = v;
      this.targets.push(v);
    }
  }

  class FakeNode {
    connect() { return this; }
  }

  class FakeGain extends FakeNode {
    constructor() {
      super();
      this.gain = new FakeParam(1);
      gains.push(this);
    }
  }

  class FakeFilter extends FakeNode {
    constructor() {
      super();
      this.type = 'lowpass';
      this.frequency = new FakeParam(1000);
      this.Q = new FakeParam(1);
      filters.push(this);
    }
  }

  class FakePanner extends FakeNode {
    constructor() {
      super();
      this.pan = new FakeParam(0);
      panners.push(this);
    }
  }

  class FakeSource extends FakeNode {
    constructor() {
      super();
      this.buffer = null;
      this.loop = false;
      this.playbackRate = new FakeParam(1);
      this.startedAt = null;
      this.connections = [];
      sources.push(this);
    }
    connect(dest) {
      this.connections.push(dest);
      return dest;
    }
    start(when = 0) {
      this.startedAt = when;
    }
    stop() {}
  }

  class FakeAudioContext {
    constructor() {
      this.sampleRate = 48000;
      this.currentTime = 10;
      this.state = 'running';
      this.destination = new FakeNode();
    }
    resume() { return Promise.resolve(); }
    createGain() { return new FakeGain(); }
    createBiquadFilter() { return new FakeFilter(); }
    createStereoPanner() { return new FakePanner(); }
    createBufferSource() { return new FakeSource(); }
    createDelay() { return Object.assign(new FakeNode(), { delayTime: new FakeParam(0.1) }); }
    createDynamicsCompressor() {
      return Object.assign(new FakeNode(), {
        threshold: new FakeParam(-14),
        knee: new FakeParam(18),
        ratio: new FakeParam(6),
        attack: new FakeParam(0.005),
        release: new FakeParam(0.16),
      });
    }
    createBuffer(ch, len, sr) {
      return {
        duration: len / sr,
        getChannelData: () => new Float32Array(len),
      };
    }
    createConvolver() { return new FakeNode(); }
    decodeAudioData() {
      return Promise.resolve({ duration: 0.4 });
    }
  }

  globalThis.AudioContext = FakeAudioContext;
  globalThis.localStorage = {
    getItem: (k) => (storage.has(k) ? storage.get(k) : null),
    setItem: (k, v) => storage.set(k, String(v)),
  };
  globalThis.fetch = () => Promise.resolve({ ok: true, arrayBuffer: () => Promise.resolve(new ArrayBuffer(8)) });

  return { storage, sources, gains, filters, panners };
}

test('mixer volumes clamp, persist to localStorage and update audio busses', async () => {
  const { storage, gains } = createAudioHarness();
  const s = createSound();
  unlockSound(s);
  await new Promise((r) => setTimeout(r, 15));

  setVolumes(s, 1.5, 0.4, -0.2, 0.75);
  assert.deepEqual([...getVolumes(s)], [1.0, 0.4, 0.0, 0.75]);
  const saved = JSON.parse(storage.get('space2601.audio'));
  assert.deepEqual(saved, { master: 1.0, sfx: 0.4, radio: 0.0, ambience: 0.75 });
  // master(0), sfxBus(1), ambienceBus(2)
  assert.ok(Math.abs(gains[0].gain.value - 0.56) < 1e-6);
  assert.ok(Math.abs(gains[1].gain.value - 0.4) < 1e-6);
  assert.ok(Math.abs(gains[2].gain.value - 0.75) < 1e-6);

  toggleMute(s);
  assert.equal(gains[0].gain.value, 0.0);
  toggleMute(s);
  assert.ok(Math.abs(gains[0].gain.value - 0.56) < 1e-6);
});

test('3D spatial panning and rear attenuation follow listener orientation', async () => {
  const { panners, filters } = createAudioHarness();
  const s = createSound();
  unlockSound(s);
  await new Promise((r) => setTimeout(r, 15));

  setListener(s, new Vec3(0, 0, 0), identity);

  // First call triggers lazy decode of explosion/crunch/sub/enemy samples; await decode & replay
  explosionAt(s, 1.0, new Vec3(100, 0, 0));
  laserAt(s, false, new Vec3(0, 0, -100), false, 0, 0, 1);
  await new Promise((r) => setTimeout(r, 20));
  assert.ok(panners.some((p) => p.pan.value > 0.65), 'Expected lazy-loaded explosion replay to pan right');

  const beforePanners = panners.length;
  explosionAt(s, 1.0, new Vec3(100, 0, 0)); // far right
  const rightPans = panners.slice(beforePanners).map((p) => p.pan.value);
  assert.ok(rightPans.length >= 1 && rightPans.every((v) => v > 0.25) && rightPans.some((v) => v > 0.7), `Expected right pan, got ${rightPans}`);

  const beforeLeft = panners.length;
  explosionAt(s, 1.0, new Vec3(-100, 0, 0)); // far left
  const leftPans = panners.slice(beforeLeft).map((p) => p.pan.value);
  assert.ok(leftPans.length >= 1 && leftPans.every((v) => v < -0.25) && leftPans.some((v) => v < -0.7), `Expected left pan, got ${leftPans}`);

  // Enemy laser ahead vs behind: behind gets warmer (lower lowpass cutoff)
  const fAheadStart = filters.length;
  laserAt(s, false, new Vec3(0, 0, -100), false, 0, 0, 1);
  const aheadCutoff = filters[fAheadStart].frequency.value;

  const fBehindStart = filters.length;
  laserAt(s, false, new Vec3(0, 0, 100), false, 0, 0, 1);
  const behindCutoff = filters[fBehindStart].frequency.value;
  assert.ok(behindCutoff < aheadCutoff * 0.85, `Expected rear cutoff (${behindCutoff}) < ahead cutoff (${aheadCutoff})`);

  // New spatial & combat cues run cleanly without throwing
  turretFireAt(s, new Vec3(40, 0, -40));
  collisionImpact(s, new Vec3(0, 0, -10), 18.0, true);
  mineralCrack(s, new Vec3(20, 5, -30));
  capitalHit(s, new Vec3(-30, 0, -80), true, false);
  overdriveOn(s);
  shieldBreak(s);
  boostEmpty(s);
  refit(s);
  torpedoImpact(s);
});

test('ship hull profiles adjust engine pitch and filter curves', async () => {
  const { sources, filters } = createAudioHarness();
  const s = createSound();
  unlockSound(s);
  await new Promise((r) => setTimeout(r, 15));

  const engineSrc = sources.find((src) => src.loop && src.playbackRate.value === 0.86);
  const engineFlt = filters[2]; // engFilter created right after echoTone & revTone
  assert.ok(engineSrc && engineFlt);

  setFlightContext(s, 1, 0, 1, false, 1, 0.0, false); // Lastkahn
  updateEngine(s, true, 0.5, 0.0);
  const haulerRate = engineSrc.playbackRate.value;
  const haulerFreq = engineFlt.frequency.value;

  setFlightContext(s, 2, 0, 4, true, 3, 0.5, false); // Sperber in cockpit
  updateEngine(s, true, 0.5, 0.0);
  const fighterRate = engineSrc.playbackRate.value;
  const fighterFreq = engineFlt.frequency.value;

  assert.ok(fighterRate > haulerRate + 0.3, `Expected Sperber rate (${fighterRate}) > Lastkahn (${haulerRate})`);
  assert.ok(fighterFreq > haulerFreq, `Expected Sperber freq (${fighterFreq}) > Lastkahn (${haulerFreq})`);
});

test('physics emits CapitalHit and CollisionImpact events cleanly', () => {
  const w = createWorld(0);
  const sortie = createSortie();
  resetWorld(w, 0);
  w.Customs = sortie;
  spawnLaser(w.Lasers, cruiserPos.X, cruiserPos.Y, cruiserPos.Z + 23, 0, 0, -500, 0, 0, 0, 1, 20, 0);
  for (let i = 0; i < 7; i++) stepPhysics(w, 0.01);
  assert.ok(w.Events.some((e) => e.cases()[e.tag] === 'CapitalHit'), 'Expected CapitalHit event on cruiser armour');
});

test('scene mode separates menu silence, paused space ambience and hangar audio, and supports ShieldRelay kit', async () => {
  const { gains } = createAudioHarness();
  const s = createSound();
  unlockSound(s);
  await new Promise((r) => setTimeout(r, 15));

  const spaceGain = gains[7];
  const hangarGain = gains[8];
  assert.ok(spaceGain && hangarGain);

  setFlightContext(s, 0, 3, 0, false, 0, 0, false); // ShieldRelay kit = 3

  setSceneMode(s, 0); // Menu / End screen: neither space nor hangar ambience
  updateEngine(s, false, 0, 0);
  assert.equal(spaceGain.gain.value, 0.0);
  assert.equal(hangarGain.gain.value, 0.0);

  setSceneMode(s, 2); // Paused in space: soft space ambience, no hangar ambience
  updateEngine(s, false, 0, 0);
  assert.ok(spaceGain.gain.value > 0.0, `Expected paused spaceGain > 0, got ${spaceGain.gain.value}`);
  assert.equal(hangarGain.gain.value, 0.0);

  setSceneMode(s, 3); // Refitting in hangar: hangar ambience on, space ambience off
  updateEngine(s, false, 0, 0);
  assert.equal(spaceGain.gain.value, 0.0);
  assert.ok(hangarGain.gain.value > 0.0, `Expected refit hangarGain > 0, got ${hangarGain.gain.value}`);

  shieldRecharge(s);
  laserAt(s, true, new Vec3(0, 0, -10), false, 0, 0, 0);
});
