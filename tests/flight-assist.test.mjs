import test from "node:test";
import assert from "node:assert/strict";
import {
  Input_attach as attach, Input_pitch as pitch, Input_yaw as yaw,
  Input_roll as roll, Input_centerStick as centerStick,
  Input_adjustMouseSensitivity as adjustSensitivity, Input_mouseSensitivityPercent as sensitivityPercent,
  Input_takeWheelThrottle as takeWheel, Input_endFrame as endFrame, Input_toggleMouseMode as toggleMouseMode,
  Input_relaxLook as relaxLook, Input_setMouseSensitivityPercent as setSensitivity,
} from "../build/Input.js";
import {
  ShipModule_createPlayer as createPlayer, ShipModule_createInterceptor as createEnemy,
  ShipModule_reset as resetShip, Flight_step as step, Flight_adjustThrottle as adjustThrottle,
} from "../build/PhysicsAndPool.js";
import { Mission_resetPlayer as resetPlayer } from "../build/Mission.js";
import { Vec3, Vec3Module_zero as zero, QuatModule_identity as identity } from "../build/Math3D.js";

const close = (actual, expected, label) =>
  assert.ok(Math.abs(actual - expected) < 1e-10, `${label}: ${actual} != ${expected}`);
const inputState = () => withAttachedInput(state => state);
const stats = ship => [ship.CruiseSpeed, ship.BoostSpeed, ship.MaxPitchRate, ship.MaxYawRate, ship.MaxRollRate];

class EventSurface {
  listeners = new Map();
  addEventListener(name, listener) {
    if (!this.listeners.has(name)) this.listeners.set(name, []);
    this.listeners.get(name).push(listener);
  }
  emit(name, fields = {}) {
    const event = { preventDefault() { this.prevented = true; }, ...fields };
    for (const listener of this.listeners.get(name) ?? []) listener(event);
    return event;
  }
}

function withAttachedInput(check) {
  const originalWindow = globalThis.window;
  const originalDocument = globalThis.document;
  const window = new EventSurface();
  const document = new EventSurface();
  const canvas = new EventSurface();
  canvas.requestPointerLock = () => {
    document.pointerLockElement = canvas;
    document.emit("pointerlockchange");
  };
  globalThis.window = window;
  globalThis.document = document;
  try { return check(attach(canvas), window, document, canvas); }
  finally {
    if (originalWindow === undefined) delete globalThis.window;
    else globalThis.window = originalWindow;
    if (originalDocument === undefined) delete globalThis.document;
    else globalThis.document = originalDocument;
  }
}

test("player creation and mission reset share assisted speeds and start at rest", () => {
  const ship = createPlayer();
  assert.deepEqual(stats(ship), [60, 75, 1.12, 1.08, 1.65]);
  assert.equal(ship.Controls.Throttle, 0);
  ship.CruiseSpeed = 500;
  ship.BoostSpeed = 400;
  ship.MaxPitchRate = ship.MaxYawRate = ship.MaxRollRate = 10;
  resetPlayer(ship);
  resetShip(ship, zero, identity);
  assert.deepEqual(stats(ship), [60, 75, 1.12, 1.08, 1.65]);
  assert.deepEqual([ship.Velocity.X, ship.Velocity.Y, ship.Velocity.Z], [0, 0, 0]);
  assert.equal(ship.Controls.Throttle, 0);
  step(ship, 1 / 60);
  assert.deepEqual([ship.Position.X, ship.Position.Y, ship.Position.Z], [0, 0, 0]);
});

test("a short throttle tap gives fine approach control and a held key promptly brings the cutter to cruise", () => {
  const ship = createPlayer();
  const dt = 1 / 120;
  resetShip(ship, zero, identity);
  for (let tick = 1; tick <= 720; tick++) {
    ship.Controls.Throttle = adjustThrottle(ship.Controls.Throttle, 1, dt);
    step(ship, dt);
    if (tick === 12) assert.ok(ship.Controls.Throttle > 0.05 && ship.Controls.Throttle < 0.06);
    if (tick === 120) assert.ok(-ship.Velocity.Z > 20 && -ship.Velocity.Z < 24);
    if (tick === 220) assert.equal(ship.Controls.Throttle, 1);
  }
  assert.ok(Math.abs(-ship.Velocity.Z - ship.CruiseSpeed) < 0.01);
  close(adjustThrottle(0, -1, dt), 0, "throttle lower stop");
});

test("released player axes stabilize quickly while an actively commanded axis responds directly", () => {
  const ship = createPlayer();
  resetShip(ship, zero, identity);
  ship.PitchRate = 0.6;
  ship.YawRate = -0.4;
  ship.RollRate = 0.3;
  ship.Controls.Yaw = 0.25;
  step(ship, 0.1);
  close(ship.PitchRate, 0.6 * Math.exp(-1.15), "released pitch");
  close(ship.RollRate, 0.3 * Math.exp(-1.15), "released roll");
  close(ship.YawRate, -0.4 + (0.25 * 1.08 + 0.4) * (1 - Math.exp(-0.7)), "commanded yaw");
  close(Math.hypot(ship.Rotation.X, ship.Rotation.Y, ship.Rotation.Z, ship.Rotation.W), 1, "rotation stays normalized");
});

test("only the player gains stronger drift damping and a stronger brake", () => {
  for (const [factory, drift, braking] of [[createPlayer, 2.5, 6.25], [createEnemy, 1.1, 3]]) {
    const ship = factory();
    resetShip(ship, zero, identity);
    ship.Velocity = new Vec3(12, 0, 0);
    ship.Controls.Throttle = 0;
    step(ship, 0.25);
    close(ship.Velocity.X, 12 * Math.exp(-drift * 0.25), "drift damping");
    ship.Velocity = new Vec3(12, 0, 0);
    ship.Controls.Brake = true;
    ship.Controls.Throttle = 0.8;
    step(ship, 0.25);
    close(ship.Velocity.X, 12 * Math.exp(-braking * 0.25), "brake damping");
    assert.equal(ship.Controls.Throttle, ship.Team === 0 ? 0 : 0.8);
  }
});

test("releasing the player brake cannot revive throttle or residual boost", () => {
  const ship = createPlayer();
  resetShip(ship, zero, identity);
  ship.Controls.Throttle = 1;
  ship.Boost = 1;
  ship.Controls.Brake = true;
  step(ship, 0.01);
  assert.equal(ship.Controls.Throttle, 0);
  assert.equal(ship.Boost, 0);
  ship.Controls.Brake = false;
  for (let frame = 0; frame < 60; frame++) step(ship, 1 / 60);
  assert.deepEqual([ship.Velocity.X, ship.Velocity.Y, ship.Velocity.Z], [0, 0, 0]);
  assert.equal(ship.Controls.Throttle, 0);
  ship.Controls.Throttle = 0.25;
  step(ship, 0.1);
  assert.ok(ship.Velocity.Z < 0, "a new deliberate throttle command still accelerates");
});

test("enemy reset, angular response and boost smoothing retain the original flight model", () => {
  const ship = createEnemy();
  resetShip(ship, zero, identity);
  assert.deepEqual(stats(ship), [62, 58, 1.3, 0.8, 2.3]);
  assert.equal(ship.Controls.Throttle, 0.6);
  close(ship.Velocity.Z, -62 * 0.4, "enemy spawn speed");
  ship.PitchRate = 0.6;
  ship.YawRate = -0.4;
  ship.RollRate = 0.3;
  ship.Controls.Yaw = 0.25;
  ship.Controls.Boost = true;
  step(ship, 0.1);
  close(ship.PitchRate, 0.6 * Math.exp(-0.6), "enemy pitch response");
  close(ship.RollRate, 0.3 * Math.exp(-0.6), "enemy roll response");
  close(ship.YawRate, -0.4 + (0.25 * 0.8 + 0.4) * (1 - Math.exp(-0.6)), "enemy yaw response");
  close(ship.Boost, 1 - Math.exp(-0.4), "enemy boost response");
});

test("keyboard controls yaw with A/D, roll with Q/E and expose combat mode on F2", () => {
  withAttachedInput((state, window) => {
    assert.equal(state.Down.length, 41);
    assert.equal(state.Pressed.length, 41);
    window.emit("keydown", { code: "KeyJ" });
    assert.equal(state.Pressed[26], 1);
    window.emit("keyup", { code: "KeyJ" });
    assert.equal(state.MouseSensitivity, 0.0018);
    for (const [code, expectedYaw, expectedRoll] of [["KeyA", 1, 0], ["KeyD", -1, 0], ["KeyQ", 0, 1], ["KeyE", 0, -1]]) {
      window.emit("keydown", { code });
      assert.equal(yaw(state), expectedYaw, code);
      assert.equal(roll(state), expectedRoll, code);
      window.emit("keyup", { code });
    }
    const key = window.emit("keydown", { code: "F2" });
    assert.equal(state.Down[22], 1);
    assert.equal(state.Pressed[22], 1);
    assert.equal(key.prevented, true);
    for (const [code, action] of [["KeyG",24], ["KeyV",25], ["KeyF",9], ["Comma",28], ["Period",29]]) {
      window.emit("keydown",{code});
      assert.equal(state.Pressed[action],1,code);
      window.emit("keyup",{code});
    }
  });
});

test("a one-pixel aim correction registers and a diagonal sweep preserves its direction at full stick", () => {
  withAttachedInput((state, _window, document, canvas) => {
    const move = (movementX, movementY) => document.emit("mousemove", { movementX, movementY });
    move(140, 0);
    assert.equal(yaw(state), 0);
    canvas.emit("mousedown", { button: 0 });
    move(1, 0);
    assert.ok(-yaw(state) > 0 && -yaw(state) < 0.01);
    move(-1, 0);
    close(yaw(state), 0, "opposite movement centers the aim");
    move(200, -100);
    close(-yaw(state) / pitch(state), 2, "diagonal direction");
    close(Math.hypot(yaw(state), pitch(state)), 1, "full deflection");
    document.pointerLockElement = null;
    document.emit("pointerlockchange");
    close(Math.hypot(yaw(state), pitch(state)), 0, "releasing the mouse centers the controls");
  });
});

test("a short swipe steers the ship while a small correction stays below one degree", () => {
  for (const [pixels, minDegrees, maxDegrees] of [[12, 0.5, 1], [216, 15, 22]]) {
    withAttachedInput((state, _window, document, canvas) => {
      canvas.emit("mousedown", { button: 0 });
      const ship = createPlayer();
      const dt = 1 / 120;
      for (let tick = 0; tick < 264; tick++) {
        if (tick < 24 && tick % 2 === 0) {
          document.emit("mousemove", { movementX: pixels / 12, movementY: 0 });
        }
        ship.Controls.Yaw = yaw(state);
        centerStick(state, dt);
        step(ship, dt);
      }
      const degrees = -2 * Math.atan2(ship.Rotation.Y, ship.Rotation.W) * 180 / Math.PI;
      assert.ok(degrees > minDegrees && degrees < maxDegrees, `${pixels} pixels turned ${degrees} degrees`);
      close(yaw(state), 0, "steering settles after the swipe");
    });
  }
});

test("saved mouse calibration scales both axes and adjustment stops at the menu limits", () => {
  withAttachedInput((state, window, document, canvas) => {
    state.MouseSensitivity = 0.0018; // Existing save files.
    assert.equal(sensitivityPercent(state), 100);
    canvas.emit("mousedown", { button: 0 });
    const sample = () => {
      window.emit("keydown", { code: "Escape" });
      document.emit("mousemove", { movementX: 20, movementY: 10 });
      return [state.StickX, state.StickY];
    };
    const normal = sample();
    adjustSensitivity(state, true);
    assert.equal(sensitivityPercent(state), 125);
    sample().forEach((value, axis) => close(value, 1.25 * normal[axis], "axis sensitivity"));
    for (let i = 0; i < 12; i++) adjustSensitivity(state, true);
    assert.equal(sensitivityPercent(state), 250);
    for (let i = 0; i < 12; i++) adjustSensitivity(state, false);
    assert.equal(sensitivityPercent(state), 25);
  });
});

test("pitch inversion consistently reverses keyboard and mouse commands", () => {
  const state = inputState();
  state.Down[0] = 1;
  assert.equal(pitch(state), -1);
  state.InvertPitch = true;
  assert.equal(pitch(state), 1);
  state.Down[0] = 0;
  state.StickY = 0.4;
  state.InvertPitch = false;
  const normal = pitch(state);
  assert.ok(normal < 0);
  state.InvertPitch = true;
  close(pitch(state), -normal, "mouse pitch inversion");
});

test("automatic stick centering is time-step invariant and does not alter held keyboard inputs", () => {
  for (const frames of [1, 30, 60, 120]) {
    const state = inputState();
    state.StickX = 0.8;
    state.StickY = -0.45;
    state.Down[4] = 1;
    for (let i = 0; i < frames; i++) centerStick(state, 1 / frames);
    close(state.StickX, 0.8 * Math.exp(-4.5), "centered X");
    close(state.StickY, -0.45 * Math.exp(-4.5), "centered Y");
    assert.equal(state.Down[4], 1);
  }
  const state = inputState();
  state.StickX = 0.7;
  centerStick(state, 0);
  centerStick(state, -1);
  assert.equal(state.StickX, 0.7);
  state.Down[10] = 1;
  centerStick(state, 0);
  assert.equal(state.StickX, 0);
});

test("X and Escape immediately center the mouse without suppressing native Escape behavior", () => {
  withAttachedInput((state, window) => {
    for (const code of ["KeyX", "Escape"]) {
      state.StickX = 0.8;
      state.StickY = -0.4;
      const event = window.emit("keydown", { code });
      assert.equal(state.StickX, 0);
      assert.equal(state.StickY, 0);
      if (code === "Escape") assert.equal(event.prevented, undefined);
      window.emit("keyup", { code });
    }
  });
});

test("the mouse wheel adjusts throttle in steps, is collected once and ignored while a menu is open", () => {
  withAttachedInput((state, _window, _document, canvas) => {
    const up = canvas.emit("wheel", { deltaY: -100 });
    canvas.emit("wheel", { deltaY: -3 });
    assert.equal(up.prevented, true);
    close(state.WheelThrottle, 0.2, "two notches up");
    close(takeWheel(state), 0.2, "collected amount");
    assert.equal(takeWheel(state), 0);
    canvas.emit("wheel", { deltaY: 100 });
    close(takeWheel(state), -0.1, "one notch down");
    state.UiMode = true;
    canvas.emit("wheel", { deltaY: -100 });
    assert.equal(takeWheel(state), 0);
  });
});

test("losing the pointer lock or the window focus raises a one-frame flag", () => {
  withAttachedInput((state, window, document, canvas) => {
    document.emit("pointerlockchange");
    assert.equal(state.FocusLost, false, "never locked, nothing lost");
    canvas.emit("mousedown", { button: 0 });
    assert.equal(state.PointerLocked, true);
    assert.equal(state.FocusLost, false);
    document.pointerLockElement = null;
    document.emit("pointerlockchange");
    assert.equal(state.FocusLost, true);
    endFrame(state);
    assert.equal(state.FocusLost, false);
    window.emit("blur");
    assert.equal(state.FocusLost, true);
  });
});

test("hold mode keeps the stick deflected, with a dead zone, full authority at the rim and X to center", () => {
  withAttachedInput((state, window) => {
    assert.equal(state.HoldStick, false, "swipe mode stays the default");
    toggleMouseMode(state);
    assert.equal(state.HoldStick, true);
    state.StickX = -0.8;
    for (let i = 0; i < 120; i++) centerStick(state, 1 / 120);
    assert.equal(state.StickX, -0.8);
    const held = yaw(state);
    assert.ok(held > 0.6 && held < 0.8, `held yaw ${held}`);
    state.StickX = -0.05;
    assert.equal(yaw(state), 0);
    state.StickX = -0.2;
    assert.ok(yaw(state) > 0 && yaw(state) < 0.1, "fine control near the center");
    state.StickX = -0.6;
    state.StickY = -0.8;
    close(Math.hypot(yaw(state), pitch(state)), 1, "full deflection");
    close(pitch(state) / yaw(state), 0.8 / 0.6, "diagonal direction");
    window.emit("keydown", { code: "KeyX" });
    assert.equal(state.StickX, 0);
    window.emit("keyup", { code: "KeyX" });
    toggleMouseMode(state);
    state.StickX = 0.8;
    centerStick(state, 1);
    close(state.StickX, 0.8 * Math.exp(-4.5), "swipe mode centers again");
  });
});

test("throttle shortcuts, rear view, mouse mode and the statistics toggle have their own keys", () => {
  withAttachedInput((state, window) => {
    for (const [code, action] of [["ArrowRight", 30], ["ArrowLeft", 31], ["KeyH", 32], ["KeyU", 33], ["F3", 34], ["KeyL", 35], ["PageUp", 36], ["PageDown", 37], ["F4", 38], ["F1", 39], ["KeyO", 40]]) {
      const event = window.emit("keydown", { code });
      assert.equal(state.Pressed[action], 1, code);
      assert.equal(state.Down[action], 1, code);
      assert.equal(event.prevented, true, code);
      window.emit("keyup", { code });
      assert.equal(state.Down[action], 0, code);
    }
  });
});

test("the held middle button turns the view instead of the ship and the view swings back on release", () => {
  withAttachedInput((state, window, document, canvas) => {
    const move = (movementX, movementY) => document.emit("mousemove", { movementX, movementY });
    canvas.emit("mousedown", { button: 0 });
    const middle = canvas.emit("mousedown", { button: 1 });
    assert.equal(middle.prevented, true);
    assert.equal(state.MouseLook, true);
    move(100, -50);
    assert.equal(yaw(state), 0, "the ship keeps its heading");
    close(state.LookX, 0.4, "look right");
    close(state.LookY, -0.2, "look up");
    move(5000, 5000);
    close(state.LookX, 2.6, "side limit");
    close(state.LookY, 1.2, "vertical limit");
    relaxLook(state, 1);
    close(state.LookX, 2.6, "held view stays");
    window.emit("mouseup", { button: 1 });
    assert.equal(state.MouseLook, false);
    move(14, 0);
    assert.ok(-yaw(state) > 0, "the mouse steers again");
    for (let i = 0; i < 240; i++) relaxLook(state, 1 / 60);
    assert.equal(state.LookX, 0);
    assert.equal(state.LookY, 0);
  });
});

test("the menu slider sets the calibration directly and respects the pause-menu limits", () => {
  const state = inputState();
  setSensitivity(state, 150);
  assert.equal(sensitivityPercent(state), 150);
  setSensitivity(state, 5);
  assert.equal(sensitivityPercent(state), 25);
  setSensitivity(state, 900);
  assert.equal(sensitivityPercent(state), 250);
});
