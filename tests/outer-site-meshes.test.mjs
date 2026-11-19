import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3, QuatModule_identity as identity } from '../build/Math3D.js';
import { create, draw, drawGuide } from '../build/OuterSiteMeshes.js';

// Capture the renderer's real mesh and instance uploads without a GPU.
function captureRenderer() {
  const meshes = [];
  const instances = [];
  const noop = () => {};
  const gl = {
    createBuffer: () => ({}), createVertexArray: () => ({}),
    bindBuffer: noop, bindVertexArray: noop, enableVertexAttribArray: noop,
    vertexAttribPointer: noop, vertexAttribDivisor: noop, uniform1f: noop,
    drawElementsInstanced: noop,
    bufferData(_target, data) {
      if (data instanceof Float32Array) meshes.push(data);
    },
    bufferSubData(_target, _dstOffset, data, start, count) {
      instances.push(data.slice(start, start + count));
    },
  };
  return {
    renderer: { Gl: gl, BlendState: 0, ViewProjState: 0, DrawCalls: 0, InstancesDrawn: 0 },
    meshes, instances,
  };
}

const origin = new Vec3(2130, -340, 1634);
const at = (instance) => Array.from(instance.slice(12, 15));

test('outer sites build and draw at the supplied world position, with instanced moving parts', () => {
  const { renderer, meshes, instances } = captureRenderer();
  const models = create(renderer);
  assert.equal(meshes.length, 0, 'entering the game eagerly uploads distant sites');
  for (let sector = 0; sector < 9; sector++) {
    instances.length = 0;
    draw(renderer, models, sector, origin, 0.05, 0);
    assert.deepEqual(at(instances[0]), [origin.X, origin.Y, origin.Z]);
    const draws = sector === 8 ? 3 : 2;
    assert.equal(instances.length, draws);
    const initial = instances[1];
    const uploaded = meshes.length;
    draw(renderer, models, sector, origin, 2.4, 0);
    assert.equal(meshes.length, uploaded, 'animation rebuilt a mesh');
    const moving = instances[draws + 1];
    assert.notDeepEqual(initial, moving, 'animation left the entire second batch still');
    if (sector === 4) {
      assert.ok(moving.length >= 12 * 20, 'missing resonance segments');
      draw(renderer, models, sector, origin, 2.4, 1);
      const response = instances.at(-1);
      let replies = 0;
      for (let i = 16; i < response.length; i += 20) {
        if (response[i] > moving[i] + 1) replies++;
      }
      assert.equal(replies, 3, 'scan should light the three floating segments');
    }
    if (sector === 6) {
      const [x, y, z] = at(moving);
      assert.ok(Math.hypot(x-origin.X, y-origin.Y, z-origin.Z) < 10, 'lens escaped scan range');
    }
    if (sector === 8) {
      const level = (t) => {
        draw(renderer, models, sector, origin, t, 0);
        return instances.at(-1)[16];
      };
      assert.ok(level(0.1) > 1 && level(0.3) < 0.1 && level(0.5) > 1 && level(0.8) < 0.1,
        'false beacon must give the approach pilot a visible double flash');
    }
    instances.length = 0;
    drawGuide(renderer, models, sector, origin, identity, 1.5);
    assert.ok(instances.every((batch) => batch.every(Number.isFinite)));
    if (sector === 3) {
      assert.equal(instances[0].length, 2 * 20, 'tandem tow lacks a tug');
      assert.deepEqual(at(instances[1]), [origin.X, origin.Y, origin.Z], 'tow lost its hull section');
    }
  }
  for (const mesh of meshes) {
    assert.equal(mesh.length % 30, 0, 'malformed triangle');
    assert.ok(mesh.length > 0 && mesh.every(Number.isFinite));
  }
});
