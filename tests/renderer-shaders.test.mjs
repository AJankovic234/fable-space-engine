import test from 'node:test';
import assert from 'node:assert/strict';
import { Shaders_fragment as mainFrag, Shaders_vertex as mainVert, RendererModule_create as createRenderer } from '../build/WebGLRenderer.js';
import { SkyBackdropModule_create as createSky, SkyBackdropModule_draw as drawSky } from '../build/SkyBackdrop.js';
import { CameraModule_create as createCamera, CameraModule_updateMatrices as updateCamera } from '../build/Camera.js';
import { ProceduralMeshes_jumpGateFrame as jumpGateFrame, ProceduralMeshes_jumpGateRing as jumpGateRing } from '../build/ProceduralMeshes.js';
import { RendererModule_applyQuality as applyQuality, RendererModule_resize as resize } from '../build/WebGLRenderer.js';
import { parse, preset } from '../build/GraphicsQuality.js';

function createMockGl() {
  const shaders = [];
  const uniforms = new Map();
  let uniformId = 1;
  const mockGl = {
    VERTEX_SHADER: 0x8B31,
    FRAGMENT_SHADER: 0x8B30,
    COMPILE_STATUS: 0x8B81,
    LINK_STATUS: 0x8B82,
    DEPTH_TEST: 0x0B71,
    CULL_FACE: 0x0B44,
    BLEND: 0x0BE2,
    LEQUAL: 0x0203,
    BACK: 0x0405,
    CCW: 0x0901,
    TEXTURE0: 0x84C0,
    TEXTURE1: 0x84C1,
    TEXTURE_2D: 0x0DE1,
    TEXTURE_MIN_FILTER: 0x2801,
    TEXTURE_MAG_FILTER: 0x2800,
    TEXTURE_WRAP_S: 0x2802,
    TEXTURE_WRAP_T: 0x2803,
    TEXTURE_COMPARE_MODE: 0x884C,
    TEXTURE_COMPARE_FUNC: 0x884D,
    COMPARE_REF_TO_TEXTURE: 0x884E,
    LINEAR: 0x2601,
    CLAMP_TO_EDGE: 0x812F,
    REPEAT: 0x2901,
    FRAMEBUFFER: 0x8D40,
    RENDERBUFFER: 0x8D41,
    COLOR_ATTACHMENT0: 0x8CE0,
    DEPTH_ATTACHMENT: 0x8D00,
    FRAMEBUFFER_COMPLETE: 0x8CD5,
    MAX_SAMPLES: 0x8D57,
    MAX_TEXTURE_SIZE: 0x0D33,
    RGBA: 0x1908,
    RGBA8: 0x8058,
    RGBA16F: 0x881A,
    UNSIGNED_BYTE: 0x1401,
    HALF_FLOAT: 0x140B,
    DEPTH_COMPONENT: 0x1902,
    DEPTH_COMPONENT24: 0x81A6,
    UNSIGNED_INT: 0x1405,
    NONE: 0,
    TRIANGLES: 0x0004,
    NO_ERROR: 0,
    createShader: (type) => ({ type, src: '' }),
    shaderSource: (s, src) => { s.src = src; shaders.push(s); },
    compileShader: () => {},
    getShaderParameter: () => true,
    getShaderInfoLog: () => '',
    deleteShader: () => {},
    createProgram: () => ({ id: Math.random() }),
    attachShader: () => {},
    linkProgram: () => {},
    getProgramParameter: () => true,
    getProgramInfoLog: () => '',
    deleteProgram: () => {},
    useProgram: () => {},
    getUniformLocation: (_prog, name) => {
      const loc = { id: uniformId++, name };
      uniforms.set(name, null);
      return loc;
    },
    uniform1i: (loc, v) => { if (loc) uniforms.set(loc.name, v); },
    uniform1f: (loc, v) => { if (loc) uniforms.set(loc.name, v); },
    uniform2f: (loc, x, y) => { if (loc) uniforms.set(loc.name, [x, y]); },
    uniform3fv: (loc, v) => { if (loc) uniforms.set(loc.name, Array.from(v)); },
    uniform4fv: (loc, v) => { if (loc) uniforms.set(loc.name, Array.from(v)); },
    uniformMatrix4fv: (loc, _t, v) => { if (loc) uniforms.set(loc.name, Array.from(v)); },
    enable: () => {},
    disable: () => {},
    depthFunc: () => {},
    depthMask: () => {},
    cullFace: () => {},
    frontFace: () => {},
    activeTexture: () => {},
    createTexture: () => ({}),
    bindTexture: () => {},
    texParameteri: () => {},
    texImage2D: () => {},
    createFramebuffer: () => ({}),
    bindFramebuffer: () => {},
    deleteFramebuffer: () => {},
    framebufferTexture2D: () => {},
    framebufferRenderbuffer: () => {},
    checkFramebufferStatus: () => 0x8CD5,
    createRenderbuffer: () => ({}),
    bindRenderbuffer: () => {},
    renderbufferStorage: () => {},
    renderbufferStorageMultisample: () => {},
    drawBuffers: () => {},
    readBuffer: () => {},
    createVertexArray: () => ({}),
    bindVertexArray: () => {},
    viewport: () => {},
    drawArrays: () => {},
    getParameter: (p) => (p === 0x0D33 ? 4096 : 4),
    getExtension: () => null,
    getError: () => 0,
  };
  const canvas = { getContext: () => mockGl, clientWidth: 1280, clientHeight: 720, width: 1280, height: 720 };
  return { mockGl, canvas, shaders, uniforms };
}

test('switching quality releases unused GL targets and reuses buffers between frames', () => {
  const { canvas, mockGl: gl } = createMockGl();
  const live = new Set();
  let allocations = 0;
  let storageCalls = 0;
  for (const kind of ['Framebuffer', 'Renderbuffer', 'Texture']) {
    gl['create' + kind] = () => {
      const handle = { kind, id: ++allocations };
      live.add(handle);
      return handle;
    };
    gl['delete' + kind] = h => assert.ok(live.delete(h), 'each handle is released once');
  }
  gl.texImage2D = () => storageCalls++;
  gl.MAX_RENDERBUFFER_SIZE = 0x84e8;
  gl.getParameter = p => p === gl.MAX_SAMPLES ? 4 : 16384;
  const previous = globalThis.window;
  globalThis.window = { devicePixelRatio: 3 };
  try {
    const renderer = createRenderer(canvas);
    const use = key => {
      const p = preset(parse(key));
      applyQuality(renderer, p, p.RenderScale);
      resize(renderer);
    };
    use('high');
    const highCount = live.size;
    assert.equal(canvas.width, 2560);
    use('low');
    assert.equal(canvas.width, 896);
    assert.equal(live.size, highCount - 11); // 4 Bloom-Paare (FBO/Textur), 3 MSAA-Handles
    assert.equal(renderer.ShadowsOn, false);
    assert.equal(renderer.Post.Msaa, false);
    use('medium');
    assert.equal(renderer.ShadowSize, 1024);
    assert.equal(renderer.Post.BloomLevels, 2);
    use('ultra');
    assert.equal(canvas.width, 3840);
    assert.equal(renderer.Post.Msaa, true);
    assert.equal(live.size, highCount);
    const before = [allocations, storageCalls];
    use('ultra');
    assert.equal(resize(renderer), false);
    assert.deepEqual([allocations, storageCalls], before);
    renderer.RenderScale = .8;
    assert.equal(resize(renderer), true);
    assert.equal(allocations, before[0]);
    assert.equal(live.size, highCount);
  } finally {
    if (previous === undefined) delete globalThis.window;
    else globalThis.window = previous;
  }
});

test('WebGLRenderer fragment shader includes slope bias, metallic specTint, panel bevel, solar rim and calibrated deep-space fog', () => {
  assert.ok(mainVert.startsWith('#version 300 es'));
  assert.ok(mainFrag.includes('mix(0.00075, 0.00022, slope)'));
  assert.ok(mainFrag.includes('specTint'));
  assert.ok(mainFrag.includes('seamAxis'));
  assert.ok(mainFrag.includes('sunRim'));
  assert.ok(mainFrag.includes('uAmbient * 0.14'));
});

test('SkyBackdrop compiles and uploads sector-matched uSunColor on draw', () => {
  const { canvas, shaders, uniforms } = createMockGl();
  const renderer = createRenderer(canvas);
  const sky = createSky(renderer);
  const cam = createCamera();
  updateCamera(cam, 16 / 9);

  renderer.Lights.SunColor[0] = 0.78;
  renderer.Lights.SunColor[1] = 0.92;
  renderer.Lights.SunColor[2] = 1.20;
  drawSky(renderer, sky, cam, 4);

  const skyFrag = shaders.find((s) => s.src.includes('uniform vec3 uSunColor;'));
  assert.ok(skyFrag, 'Expected SkyBackdrop displayFragment to declare uSunColor');
  assert.ok(!skyFrag.src.includes('vignette'), 'SkyBackdrop should not double-vignette over WebGLRenderer');
  const uploaded = uniforms.get('uSunColor');
  assert.ok(uploaded && Math.abs(uploaded[2] - 1.2) < 1e-5, `Expected uSunColor[2] ≈ 1.2, got ${uploaded}`);
});

test('ProceduralMeshes builds finite 3D jump gate frame and accelerator ring with emissive beacons', () => {
  for (const [label, build] of [['jumpGateFrame', jumpGateFrame], ['jumpGateRing', jumpGateRing]]) {
    const mesh = build();
    assert.ok(mesh.Vertices.length >= 1000 && mesh.Indices.length >= 100, `${label} geometry too small`);
    assert.equal(mesh.Vertices.length % 10, 0, `${label} stride mismatch`);
    let hasEmissive = false;
    let maxR = 0;
    for (let i = 0; i < mesh.Vertices.length; i += 10) {
      for (let k = 0; k < 10; k++) {
        assert.ok(Number.isFinite(mesh.Vertices[i + k]), `${label} non-finite vertex attribute`);
      }
      const r = Math.hypot(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
      if (r > maxR) maxR = r;
      if (mesh.Vertices[i + 9] > 1.0) hasEmissive = true;
    }
    assert.ok(hasEmissive, `${label} should include glowing beacons/coils`);
    assert.ok(maxR > 15 && maxR < 40, `${label} unexpected radius ${maxR}`);
  }
});
