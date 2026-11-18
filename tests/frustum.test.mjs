import test from "node:test";
import assert from "node:assert/strict";
import {
  Frustum_create as createFrustum,
  Frustum_update as updateFrustum,
  Frustum_intersectsSphere as intersects,
  Mat4Module_create as matrix,
  Mat4Module_perspective as perspective,
  Mat4Module_lookAt as lookAt,
  Mat4Module_multiply as multiply,
  Vec3,
} from "../build/Math3D.js";

function camera(eye = [0, 0, 0], target = [0, 0, -1], aspect = 1) {
  const projection = matrix();
  const view = matrix();
  const viewProjection = matrix();
  perspective(projection, Math.PI / 2, aspect, 1, 9);
  lookAt(view, new Vec3(...eye), new Vec3(...target), new Vec3(0, 1, 0));
  multiply(viewProjection, projection, view);
  return viewProjection;
}

test("all six planes keep tangent spheres and reject spheres fully beyond them", () => {
  const planes = createFrustum();
  updateFrustum(planes, camera());
  const radius = 0.5;
  const tangent = 5 + radius * Math.SQRT2;
  for (const direction of [-1, 1]) {
    assert.ok(intersects(planes, direction * tangent, 0, -5, radius));
    assert.ok(!intersects(planes, direction * (tangent + 0.01), 0, -5, radius));
    assert.ok(intersects(planes, 0, direction * tangent, -5, radius));
    assert.ok(!intersects(planes, 0, direction * (tangent + 0.01), -5, radius));
  }
  assert.ok(intersects(planes, 0, 0, -0.5, radius));
  assert.ok(!intersects(planes, 0, 0, -0.49, radius));
  assert.ok(intersects(planes, 0, 0, -9.5, radius));
  assert.ok(!intersects(planes, 0, 0, -9.51, radius));
  assert.ok(!intersects(planes, 0, 0, 2, radius));
  assert.ok(intersects(planes, 0, 0, 2, 4));
});

test("moving, turning, and resizing the camera replace the previous frame's planes", () => {
  const planes = createFrustum();
  updateFrustum(planes, camera());
  assert.ok(intersects(planes, 0, 0, -5, 0.2));
  updateFrustum(planes, camera([30, -7, 12], [31, -7, 12]));
  assert.ok(!intersects(planes, 0, 0, -5, 0.2));
  assert.ok(intersects(planes, 35, -7, 12, 0.2));
  assert.ok(!intersects(planes, 25, -7, 12, 0.2));
  assert.ok(!intersects(planes, 35, -7, 20, 0.2));
  updateFrustum(planes, camera([30, -7, 12], [31, -7, 12], 2));
  assert.ok(intersects(planes, 35, -7, 20, 0.2));
  assert.ok(!intersects(planes, 35, 1, 12, 0.2));
});

test("an oblique camera agrees with WebGL homogeneous clip bounds for point centers", () => {
  const m = camera([7, -4, 11], [4, -2, 6], 1.7);
  const planes = createFrustum();
  updateFrustum(planes, m);
  const classifications = new Set();
  for (let x = -4.27; x < 18; x += 2) {
    for (let y = -11.2; y < 9; y += 2) {
      for (let z = -3.71; z < 16; z += 2) {
        const clip = row => m[row] * x + m[4 + row] * y + m[8 + row] * z + m[12 + row];
        const w = clip(3);
        const inside = [0, 1, 2].every(row => Math.abs(clip(row)) <= w);
        assert.equal(intersects(planes, x, y, z, 0), inside);
        classifications.add(inside);
      }
    }
  }
  assert.equal(classifications.size, 2);
});
