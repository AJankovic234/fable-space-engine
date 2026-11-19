import test from 'node:test';
import assert from 'node:assert/strict';
import { Vec3 } from '../build/Math3D.js';
import { create, draw } from '../build/PoiMeshes.js';

// Record mesh data and instance matrices the renderer would submit together.
function capture() {
  const calls = [];
  let bound, vao, meshes = 0;
  const noop = () => {};
  const gl = {
    createBuffer: () => ({}), createVertexArray: () => ({}),
    bindBuffer(_target, buffer) { bound = buffer; },
    bindVertexArray(value) { vao = value; },
    bufferData(_target, data) {
      if (data instanceof Float32Array) { bound.vertices = data; meshes++; }
    },
    vertexAttribPointer(location) { if (location === 0) vao.vertexBuffer = bound; },
    bufferSubData(_target, _offset, data, start, count) { bound.transforms = data.slice(start, start+count); },
    drawElementsInstanced() { calls.push([vao.vertexBuffer.vertices, bound.transforms]); },
    enableVertexAttribArray: noop, vertexAttribDivisor: noop, uniform1f: noop,
  };
  const renderer = { Gl: gl, BlendState: 0, ViewProjState: 0, DrawCalls: 0, InstancesDrawn: 0 };
  return { renderer, calls, uploads: () => meshes };
}

const origin = new Vec3(1221, -541, 2503);

function checkBounds(vertices, transforms, ring) {
  for (let j = 0; j < transforms.length; j += 20) {
    const m = transforms.subarray(j, j+20);
    assert.ok(m.every(Number.isFinite));
    for (let i = 0; i < vertices.length; i += 30) {
      const corners = [];
      for (let k = i; k < i+30; k += 10) {
        const [x,y,z] = vertices.subarray(k,k+3);
        const wx = m[0]*x + m[4]*y + m[8]*z + m[12] - origin.X;
        const wy = m[1]*x + m[5]*y + m[9]*z + m[13] - origin.Y;
        const wz = m[2]*x + m[6]*y + m[10]*z + m[14] - origin.Z;
        assert.ok(wx*wx + wy*wy + wz*wz < 180*180, 'geometry escapes the culling sphere');
        if (ring) corners.push([wx,wy]);
      }
      if (ring) {
        // The entire triangle must lie beyond a tangent to the 44 m flight cylinder.
        const nx = corners[0][0] + corners[1][0] + corners[2][0];
        const ny = corners[0][1] + corners[1][1] + corners[2][1];
        const length = Math.hypot(nx,ny);
        assert.ok(corners.every(([x,y]) => x*nx + y*ny > 44*length), 'obstructed calibration route');
      }
    }
  }
}

test('sector POIs keep geometry, paired receiver lights and the flying route aligned', () => {
  const { renderer, calls, uploads } = capture();
  const models = create(renderer);
  assert.equal(uploads(),0);
  for (let kind = 0; kind < 4; kind++) {
    const lengths = new Set();
    for (let sector = 0; sector < 9; sector++) {
      calls.length = 0;
      draw(renderer, models, kind, sector, origin, 1.5, false);
      assert.equal(calls.length,3);
      const [, frame] = calls[0];
      assert.deepEqual([...frame.slice(12,15)], [origin.X,origin.Y,origin.Z]);
      lengths.add(calls[0][0].length);
      for (const [vertices, transforms] of calls) checkBounds(vertices, transforms, kind === 3);
      if (kind === 1) {
        const [, dishes] = calls[1], [, lights] = calls[2];
        for (const [i,side] of [[0,-1],[20,1]]) {
          const x = origin.X + side*85;
          assert.deepEqual([...dishes.slice(i+12,i+15)], [x,origin.Y,origin.Z]);
          assert.ok(Math.hypot(lights[i+12]-x,lights[i+13]-origin.Y,lights[i+14]-origin.Z) < 25);
        }
        assert.ok(lights[16] > lights[18]*2 && lights[38] > lights[36]*2,
          'west receiver should shine amber and east receiver cyan');
      }
      const [, initialMotion] = calls[1], [, initialLight] = calls[2];
      const before = uploads();
      calls.length = 0;
      draw(renderer, models, kind, sector, origin, 5, true);
      assert.equal(uploads(),before,'second draw uploaded new geometry');
      assert.notDeepEqual(calls[1][1],initialMotion,'mechanism stayed still');
      const status = kind === 1 ? 56 : 16;
      assert.ok(calls[2][1][status+1] > calls[2][1][status], 'completed site has no green ready lamp');
      assert.notEqual(calls[2][1][status],initialLight[status]);
    }
    assert.ok(lengths.size >= 3, 'sector variants share the same geometry');
  }
});
