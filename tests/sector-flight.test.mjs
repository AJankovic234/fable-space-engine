import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { Universe_sectors as sectors } from '../build/Universe.js';
import { ProceduralMeshes_sectorAsteroid as rock } from '../build/ProceduralMeshes.js';
import { CameraModule_create as camera, CameraModule_update as update,
  CameraModule_steeringGain as gain, CameraModule_toggleMode as switchCamera } from '../build/Camera.js';
import { Vec3, QuatModule_identity as identity } from '../build/Math3D.js';

test('regional rocks have distinct geometry and material, all bounded by the existing collision sphere', () => {
  const shapes = new Set(), colours = new Set();
  for (const { Outpost: port } of sectors) {
    const mesh = rock(port.Kind, 37, 2);
    const shape = createHash('sha256'), colour = createHash('sha256');
    let maxRadius = 0;
    for (let i = 0; i < mesh.Vertices.length; i += 10) {
      const vertex = mesh.Vertices.subarray(i, i + 10);
      maxRadius = Math.max(maxRadius, Math.hypot(...vertex.subarray(0, 3)));
      assert.ok(Math.abs(Math.hypot(...vertex.subarray(3, 6)) - 1) < 0.001);
      shape.update(Buffer.from(vertex.buffer, vertex.byteOffset, 12));
      colour.update(Buffer.from(vertex.buffer, vertex.byteOffset + 24, 12));
    }
    assert.ok(maxRadius > 0.999 && maxRadius <= 1, port.Name);
    shapes.add(shape.digest('hex'));
    colours.add(colour.digest('hex'));
  }
  const kinds = new Set(sectors.map(sector => sector.Outpost.Kind.tag)).size;
  assert.equal(shapes.size, kinds);
  assert.equal(colours.size, kinds);
});

test('optics preserve angular precision and give the same 1.8x magnification in either camera', () => {
  const cam = camera(), origin = new Vec3(0, 0, 0);
  const settle = boost => { for (let i = 0; i < 240; i++) update(cam, origin, identity, boost, 1 / 60); };
  for (const boost of [0, 1]) {
    settle(boost);
    const wide = cam.Fov;
    assert.equal(gain(cam), 1);
    cam.OpticsActive = true;
    update(cam, origin, identity, boost, 1 / 60);
    assert.ok(cam.Fov < wide && cam.Fov > wide / 1.8);
    settle(boost);
    const magnification = Math.tan(wide / 2) / Math.tan(cam.Fov / 2);
    assert.ok(Math.abs(magnification - 1.8) < 0.001);
    assert.ok(Math.abs(gain(cam) * magnification - 1) < 0.001);
    cam.OpticsActive = false;
    settle(boost);
    assert.ok(Math.abs(cam.Fov - wide) < 0.0001);
    switchCamera(cam);
  }
});
