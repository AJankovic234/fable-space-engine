import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import {
  parse as parseGlb,
  normalizeToRadius,
} from "../build/GlbLoader.js";

function buildTestGlb() {
  // 2 Vertices + 1 weiteres Dreieck (3 Vertices), Node-Translation (10, 0, 0) & Scale 2
  const positions = new Float32Array([
    0, 0, 0,
    1, 0, 0,
    0, 1, 0,
  ]);
  const normals = new Float32Array([
    0, 0, 1,
    0, 0, 1,
    0, 0, 1,
  ]);
  const indices = new Uint16Array([0, 1, 2]);

  const posBytes = new Uint8Array(positions.buffer);
  const normBytes = new Uint8Array(normals.buffer);
  const idxBytes = new Uint8Array(indices.buffer);
  const binPad = (4 - ((posBytes.length + normBytes.length + idxBytes.length) % 4)) % 4;
  const binLen = posBytes.length + normBytes.length + idxBytes.length + binPad;
  const binBuf = new Uint8Array(binLen);
  binBuf.set(posBytes, 0);
  binBuf.set(normBytes, posBytes.length);
  binBuf.set(idxBytes, posBytes.length + normBytes.length);

  const gltf = {
    asset: { version: "2.0" },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ mesh: 0, translation: [10, 0, 0], scale: [2, 2, 2] }],
    materials: [
      {
        pbrMetallicRoughness: { baseColorFactor: [0.2, 0.6, 0.9, 1.0] },
        emissiveFactor: [0.0, 0.8, 0.8],
      },
    ],
    meshes: [
      {
        primitives: [
          {
            attributes: { POSITION: 0, NORMAL: 1 },
            indices: 2,
            material: 0,
          },
        ],
      },
    ],
    buffers: [{ byteLength: binLen }],
    bufferViews: [
      { buffer: 0, byteOffset: 0, byteLength: posBytes.length },
      { buffer: 0, byteOffset: posBytes.length, byteLength: normBytes.length },
      { buffer: 0, byteOffset: posBytes.length + normBytes.length, byteLength: idxBytes.length },
    ],
    accessors: [
      { bufferView: 0, componentType: 5126, count: 3, type: "VEC3" },
      { bufferView: 1, componentType: 5126, count: 3, type: "VEC3" },
      { bufferView: 2, componentType: 5123, count: 3, type: "SCALAR" },
    ],
  };

  const jsonRaw = new TextEncoder().encode(JSON.stringify(gltf));
  const jsonPad = (4 - (jsonRaw.length % 4)) % 4;
  const jsonLen = jsonRaw.length + jsonPad;
  const totalLen = 12 + 8 + jsonLen + 8 + binLen;
  const out = new ArrayBuffer(totalLen);
  const dv = new DataView(out);
  const bytes = new Uint8Array(out);

  dv.setUint32(0, 0x46546c67, true);
  dv.setUint32(4, 2, true);
  dv.setUint32(8, totalLen, true);
  dv.setUint32(12, jsonLen, true);
  dv.setUint32(16, 0x4e4f534a, true);
  bytes.set(jsonRaw, 20);
  for (let i = 0; i < jsonPad; i++) bytes[20 + jsonRaw.length + i] = 0x20;

  const binHeader = 20 + jsonLen;
  dv.setUint32(binHeader, binLen, true);
  dv.setUint32(binHeader + 4, 0x004e4942, true);
  bytes.set(binBuf, binHeader + 8);
  return out;
}

test("GlbLoader parses glTF 2.0 binary into interleaved MeshData with node transform and PBR colors", () => {
  const mesh = parseGlb(buildTestGlb());
  assert.equal(mesh.Vertices.length, 3 * 10);
  assert.deepEqual(Array.from(mesh.Indices), [0, 1, 2]);

  // Vertex 0: (0,0,0) * 2 + (10,0,0) = (10,0,0)
  assert.ok(Math.abs(mesh.Vertices[0] - 10) < 1e-4);
  // Vertex 1: (1,0,0) * 2 + (10,0,0) = (12,0,0)
  assert.ok(Math.abs(mesh.Vertices[10] - 12) < 1e-4);
  // Normal stays normalized (0,0,1)
  assert.ok(Math.abs(mesh.Vertices[5] - 1) < 1e-4);
  // PBR baseColorFactor + emissiveFactor
  assert.ok(Math.abs(mesh.Vertices[6] - 0.2) < 1e-4);
  assert.ok(Math.abs(mesh.Vertices[7] - 0.8) < 1e-4);
  assert.ok(Math.abs(mesh.Vertices[9] - 0.8) < 1e-4);

  const normalized = normalizeToRadius(5.0, mesh);
  let maxR = 0;
  for (let i = 0; i < 3; i++) {
    const o = i * 10;
    const r = Math.hypot(normalized.Vertices[o], normalized.Vertices[o + 1], normalized.Vertices[o + 2]);
    if (r > maxR) maxR = r;
  }
  assert.ok(Math.abs(maxR - 5.0) < 1e-3);
});

test("GlbLoader rejects invalid GLB signatures", () => {
  assert.throws(() => parseGlb(new ArrayBuffer(32)), /GLB-Signatur/);
});

test("bundled space station, jump gate and freighter GLB assets parse cleanly with emissive zones", () => {
  const modelsDir = path.resolve("public/assets/models");
  for (const file of ["outpost-station.glb", "jump-gate.glb", "cargo-freighter.glb"]) {
    const raw = fs.readFileSync(path.join(modelsDir, file));
    const buf = raw.buffer.slice(raw.byteOffset, raw.byteOffset + raw.byteLength);
    const mesh = normalizeToRadius(10.0, parseGlb(buf));
    assert.ok(mesh.Vertices.length >= 300, `${file} should have substantial geometry`);
    assert.ok(mesh.Indices.length >= 30, `${file} should have triangle indices`);
    let hasEmissive = false;
    for (let i = 9; i < mesh.Vertices.length; i += 10) {
      if (mesh.Vertices[i] > 0.5) {
        hasEmissive = true;
        break;
      }
    }
    assert.ok(hasEmissive, `${file} should include emissive glow vertices for Bloom`);
  }
});

test("GlbLoader computes triangle normals when omitted and preserves perpendicularity under non-uniform scale", () => {
  // Triangle in X-Y plane stretched 4x on X, 1x on Y, 1x on Z -> normal still points along +Z,
  // and diagonal slope in X-Z plane (0,0,0)-(1,0,0)-(0,1,1) scaled (2,1,1) has inverse-transpose normal (-1, -2, 2)/3
  const positions = new Float32Array([0, 0, 0, 1, 0, 0, 0, 1, 1]);
  const posBytes = new Uint8Array(positions.buffer);
  const gltf = {
    asset: { version: "2.0" },
    scene: 0,
    scenes: [{ nodes: [0] }],
    nodes: [{ mesh: 0, scale: [2, 1, 1] }],
    meshes: [{ primitives: [{ attributes: { POSITION: 0 } }] }],
    buffers: [{ byteLength: posBytes.length }],
    bufferViews: [{ buffer: 0, byteOffset: 0, byteLength: posBytes.length }],
    accessors: [{ bufferView: 0, componentType: 5126, count: 3, type: "VEC3" }],
  };
  const jsonRaw = new TextEncoder().encode(JSON.stringify(gltf));
  const jsonPad = (4 - (jsonRaw.length % 4)) % 4;
  const jsonLen = jsonRaw.length + jsonPad;
  const totalLen = 12 + 8 + jsonLen + 8 + posBytes.length;
  const out = new ArrayBuffer(totalLen);
  const dv = new DataView(out);
  const bytes = new Uint8Array(out);
  dv.setUint32(0, 0x46546c67, true);
  dv.setUint32(4, 2, true);
  dv.setUint32(8, totalLen, true);
  dv.setUint32(12, jsonLen, true);
  dv.setUint32(16, 0x4e4f534a, true);
  bytes.set(jsonRaw, 20);
  for (let i = 0; i < jsonPad; i++) bytes[20 + jsonRaw.length + i] = 0x20;
  const binHeader = 20 + jsonLen;
  dv.setUint32(binHeader, posBytes.length, true);
  dv.setUint32(binHeader + 4, 0x004e4942, true);
  bytes.set(posBytes, binHeader + 8);

  const mesh = parseGlb(out);
  // Transformed vertices: p0=(0,0,0), p1=(2,0,0), p2=(0,1,1) -> edge1=(2,0,0), edge2=(0,1,1) -> normal ~ (0, -2, 2)/sqrt(8)
  const nx = mesh.Vertices[3], ny = mesh.Vertices[4], nz = mesh.Vertices[5];
  const invLen = 1 / Math.hypot(0, -2, 2);
  assert.ok(Math.abs(nx) < 1e-4, `Expected nx=0, got ${nx}`);
  assert.ok(Math.abs(ny - -2 * invLen) < 1e-4, `Expected ny=${-2 * invLen}, got ${ny}`);
  assert.ok(Math.abs(nz - 2 * invLen) < 1e-4, `Expected nz=${2 * invLen}, got ${nz}`);
});
