import test from 'node:test';
import assert from 'node:assert/strict';
import { PilotSkills_create as pilot } from '../build/PilotSkills.js';
import { StoryRadio_act as act, StoryRadio_atPort as first, StoryRadio_calls as calls, StoryRadio_contact as contact,
         StoryRadio_earlier as earlier, StoryRadio_logbook as logbook, StoryRadio_heardMask as heardMask,
         RadioChatter_pick as pickChatter, RadioChatter_exchanges as chatterExchanges, RadioContext } from '../build/StoryRadio.js';
import { Universe_sectors as sectors } from '../build/Universe.js';

const ports = sectors.map(s => s.Outpost.Id);
const veteran = () => Object.assign(pilot(), { Visited: 0x1ff, Relics: 0x1ff });

test('every station answers, and each call owns one save bit', () => {
  for (const port of ports) assert.ok(first(port), `${port} has a first call`);
  const beats = ports.flatMap(port => calls(veteran(), port).map(call => call.Beat));
  assert.equal(new Set(beats).size, beats.length);
  for (const beat of beats) assert.ok(beat >= 0 && beat < 30 && (heardMask & (1 << beat)) !== 0);
});

test('acts follow visits and finds, and the last act waits for Morgenlicht', () => {
  const p = pilot();
  assert.equal(act(p), 1);
  p.Visited = 0b1111; p.Relics = 0b1;            // 4 + 2
  assert.equal(act(p), 2);
  p.Visited = 0b111011111; p.Relics = 0b111;     // 8 + 6, Morgenlicht (bit 5) not yet seen
  assert.equal(act(p), 2);
  p.Visited |= 1 << 5;
  assert.equal(act(p), 3);
});

test('a newer call replaces the station greeting and the older ones stay on file', () => {
  const p = pilot();
  assert.equal(contact(p, 'station-kestrel').Beat, first('station-kestrel').Beat);
  assert.deepEqual(earlier(p, 'station-kestrel'), []);
  const v = veteran();
  assert.equal(contact(v, 'station-kestrel').Subject, 'Voss und sein Schlepper');
  assert.equal(earlier(v, 'station-kestrel').length, 2);
  assert.equal(contact(v, 'station-wracksaum').Name, 'Odo Karst');
  assert.equal(contact(v, 'unknown'), undefined);
});

test('the logbook grows with visited sectors and acts', () => {
  assert.deepEqual(logbook(pilot()).map(e => e[0]), ['Kestrel']);
  const all = logbook(veteran()).map(e => e[0]);
  assert.equal(all.length, 11);
  assert.deepEqual(all.slice(-2), ['Zweiter Akt', 'Dritter Akt']);
});

test('faction radio filters by sector, act, AETHER proximity, and alternates stereo L/R on AETHER exchanges', () => {
  const aetherCruising = new RadioContext('slag-refinery', 1, true, false);
  const picked = pickChatter(aetherCruising, [], 0.0);
  assert.ok(picked);
  assert.equal(picked.Id, 'aether-rustbucket');
  assert.equal(picked.Lines.length, 2);
  assert.ok(picked.Lines[0].Pan < 0 && picked.Lines[1].Pan > 0);
  assert.equal(picked.Lines[0].CleanFilter, true);

  // Recent ID deduplication skips the already-heard exchange
  const second = pickChatter(aetherCruising, ['aether-rustbucket'], 0.0);
  assert.ok(second && second.Id !== 'aether-rustbucket');

  // Act 3 nervous AETHER exchange is excluded in Act 1 and available in Act 3
  const act3Ids = chatterExchanges.filter(ex => ex.Id === 'aether-act3-nervous');
  assert.equal(act3Ids.length, 1);
  const noAetherSchleier = new RadioContext('schleierpass', 1, false, false);
  const gw = pickChatter(noAetherSchleier, [], 0.0);
  assert.ok(gw);
  assert.equal(gw.Lines[0].CleanFilter, false);
});
