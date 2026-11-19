import test from "node:test";
import assert from "node:assert/strict";
import { Anchor, arrange, around, radarOffset, rect, targetBox } from "../build/HudLayout.js";
import { Mat4Module_create as createMatrix, Vec3 } from "../build/Math3D.js";
import { HudModule_create as createHud, HudModule_drawStation as drawStation } from "../build/HUD.js";
import { Universe_sectorByIndex as sectorByIndex } from "../build/Universe.js";

const intersects = (a, b) =>
  a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

test("coincident approach labels clear the sight, enemy frame, glyphs and one another", () => {
  const reserved = [around(480, 278, 38), around(522, 238, 26)];
  const anchors = [
    new Anchor(483, 277, 8, 210, 24, 50), // gate nearly on the station
    new Anchor(480, 278, 10, 240, 42, 100),
    new Anchor(596, 257, 8, 212, 24, 60),
    new Anchor(631, 220, 7, 156, 42, 65),
  ];
  const plan = arrange(960, 540, reserved, anchors);
  assert.equal(plan[0].Index, 1);
  assert.equal(plan[0].Symbol, false, "the station square would obscure the sight");
  assert.ok(plan.every(p => p.Label), "there is room to route all four labels");
  const occupied = [...reserved];
  for (const p of plan) {
    const a = anchors[p.Index];
    if (p.Symbol) occupied.push(around(a.X, a.Y, a.Radius));
  }
  for (const { Label, Line } of plan) {
    assert.ok(occupied.every(r => !intersects(r, Label)));
    occupied.push(Label);
    if (Line) {
      for (const r of reserved) {
        const segment = rect(Math.min(Line.X1, Line.X2) - 0.5, Math.min(Line.Y1, Line.Y2) - 0.5,
          Math.abs(Line.X2 - Line.X1) + 1, Math.abs(Line.Y2 - Line.Y1) + 1);
        assert.ok(!intersects(r, segment), "a leader must also clear the sight and frame");
      }
    }
  }
  assert.deepEqual(arrange(960, 540, reserved, anchors), plan);
});

test("a crowded viewport keeps the priority label and omits labels that cannot fit", () => {
  const anchors = Array.from({ length: 12 }, (_, i) => new Anchor(130, 64, 6, 116, 34, i));
  const plan = arrange(260, 128, [], anchors);
  assert.equal(plan[0].Index, 11);
  assert.ok(plan[0].Symbol && plan[0].Label);
  assert.equal(plan.filter(p => p.Symbol).length, 1);
  assert.ok(plan.some(p => !p.Label));
  for (const { Label } of plan.filter(p => p.Label)) {
    assert.ok(Label.X >= 4 && Label.X + Label.W <= 256 && Label.Y >= 4 && Label.Y + Label.H <= 124);
  }
});

test("quiet contacts are symbol-only and yield to a protected target even at the edge", () => {
  const anchors = [new Anchor(12, 34, 5, 0, 24, 50), new Anchor(68, 50, 5, 0, 24, 100)];
  const plan = arrange(200, 120, [around(68, 50, 21)], anchors);
  assert.equal(plan[0].Symbol, false);
  assert.equal(plan[1].Symbol, true);
  assert.ok(plan.every(p => !p.Label && !p.Line));
});

test("target information clears the actual sight on wide and compact displays", () => {
  for (const [w, h, walletVisible] of [[1280, 720, true], [390, 680, false], [720, 400, false]]) {
    const sight = around(w - 100, walletVisible ? 202 : 165, 50);
    const box = targetBox(w, h, walletVisible, sight, [sight]);
    assert.ok(!intersects(sight, box));
    assert.ok(box.X >= 0 && box.X + box.W <= w && box.Y >= 0 && box.Y + box.H <= h);
  }
});

test("a reachable trading dock keeps its interaction cue during a local fight", () => {
  const hud = createHud({ getContext: () => ({}) });
  hud.Width = 960;
  hud.Height = 540;
  hud.InCombat = true;
  const port = sectorByIndex(0).Outpost;
  for (const blocked of [true, false]) {
    drawStation(hud, createMatrix(), 16 / 9, port, port.Position, blocked);
  }
  assert.equal(hud.Markers[0].Quiet, true);
  assert.equal(hud.Markers[1].Quiet, false);
  assert.match(hud.Markers[1].Title, /\[B\] Andocken/);
});

test("radar altitude has a signed stalk and distant bearings stay on the disc", () => {
  const above = radarOffset(460, 74, 30, new Vec3(1900, 18, -800));
  const below = radarOffset(460, 74, 30, new Vec3(1900, -18, -800));
  const level = radarOffset(460, 74, 30, new Vec3(1900, 0, -800));
  assert.deepEqual(above.slice(0, 2), below.slice(0, 2));
  assert.ok(above[2] < above[1] && below[2] > below[1]);
  assert.equal(level[1], level[2]);
  assert.ok(Math.hypot(level[0] / 74, level[1] / 30) <= 0.941);
});
