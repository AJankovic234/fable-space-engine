namespace SpaceEngine

open Fable.Core
open Browser.Types

/// A deterministic environment asset baked once on the GPU. Flight only samples
/// the panorama: no per-frame noise, ray marching, downloads or texture uploads.
type SkyBackdrop =
    { Program: WebGLProgram
      Texture: WebGLTexture
      Vao: WebGLVertexArrayObject
      UView: WebGLUniformLocation
      ULens: WebGLUniformLocation }

[<RequireQualifiedAccess>]
module SkyBackdrop =
    let private vertex = """#version 300 es
precision highp float;
out vec2 vUv;
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
"""

    let private bakeFragment = """#version 300 es
precision highp float;
in vec2 vUv;
out vec4 outColor;
const float PI = 3.14159265359;
const vec3 SUN = vec3(0.8393, 0.4519, 0.3013);
float hash(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}
float noise(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i+vec3(1,0,0)), f.x),
                   mix(hash(i+vec3(0,1,0)), hash(i+vec3(1,1,0)), f.x), f.y),
               mix(mix(hash(i+vec3(0,0,1)), hash(i+vec3(1,0,1)), f.x),
                   mix(hash(i+vec3(0,1,1)), hash(i+vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm(vec3 p) {
    float n = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) {
        n += noise(p) * a;
        p = p * 2.03 + vec3(9.2, 3.8, 5.1);
        a *= 0.5;
    }
    return n;
}
float sphere(vec3 rd, vec3 center, float radius) {
    float b = dot(rd, center);
    float h = b*b - dot(center, center) + radius*radius;
    if (h < 0.0 || b < 0.0) return 1e5;
    return b - sqrt(h);
}
vec3 stars(vec3 d, float scale, float threshold) {
    vec3 p = d * scale, cell = floor(p), f = fract(p) - 0.5;
    float seed = hash(cell);
    float star = (1.0-smoothstep(0.0, 0.28, length(f))) * step(threshold, seed);
    return mix(vec3(0.52,0.74,1.0), vec3(1.0,0.78,0.49), hash(cell+9.0)) * star;
}
void main() {
    float lon = (vUv.x - 0.5) * 2.0 * PI;
    float lat = (vUv.y - 0.5) * PI;
    vec3 d = vec3(sin(lon)*cos(lat), sin(lat), -cos(lon)*cos(lat));

    float warp = fbm(d * 3.3 + vec3(1.0,4.0,7.0));
    float band = exp(-pow((d.y + d.x * 0.38 - 0.12 + (warp - 0.5) * 0.34) * 3.4, 2.0));
    float cloud = fbm(d * 8.0 + warp * 2.5);
    float wisps = fbm(d * 22.0 + cloud * 3.0);
    float dust = smoothstep(0.40, 0.68, fbm(d * 13.0 + vec3(3,7,1)));
    vec3 color = vec3(0.003, 0.006, 0.017);
    vec3 nebula = mix(vec3(0.13,0.035,0.18), vec3(0.025,0.22,0.27), smoothstep(-0.7,0.7,d.x));
    color += nebula * band * (0.15 + cloud * cloud * 1.5) * (0.48 + wisps);
    color *= 1.0 - dust * band * 0.85;
    color += vec3(0.08,0.13,0.20) * pow(wisps, 4.0) * band;
    color += stars(d, 570.0, 0.986) * 0.85 + stars(d, 240.0, 0.995) * 1.5;

    // Ray/sphere planet and ray/plane rings share depth, including the far side
    // of the rings and the planet's shadow. They stay fixed in world space.
    vec3 center = normalize(vec3(0.57, 0.24, -1.0)) * 12.0;
    float radius = 2.12;
    float planetT = sphere(d, center, radius);
    float approach = length(cross(d, center));
    float halo = exp(-max(approach-radius,0.0)*22.0) * step(radius, approach) * step(0.0,dot(d,center));
    color += vec3(0.06,0.30,0.49) * halo * 0.7;
    if (planetT < 1e4) {
        vec3 n = normalize(d * planetT - center);
        float turbulence = fbm(n * 8.0);
        float bands = sin(n.y * 39.0 + turbulence * 3.2 + n.x * 0.7);
        vec3 surface = mix(vec3(0.20,0.29,0.32),vec3(0.51,0.63,0.61),smoothstep(-1.0,1.0,bands));
        surface *= 0.85 + turbulence * 0.32 + sin(n.y*170.0+turbulence*6.0)*0.025;
        surface = mix(surface,vec3(0.62,0.45,0.29),smoothstep(0.57,0.76,turbulence)*0.8);
        float daylight = max(dot(n,SUN),0.0);
        float limb = pow(1.0-max(dot(n,-d),0.0),3.0);
        color = surface * (vec3(0.022,0.035,0.06) + vec3(1.0,0.88,0.73)*daylight*0.9);
        color += vec3(0.07,0.34,0.52)*limb*(0.12+daylight*0.8);
    }
    vec3 ringN = normalize(vec3(0.18,0.79,0.42));
    float denom = dot(d,ringN);
    if (abs(denom) > 0.0001) {
        float t = dot(center,ringN)/denom;
        vec3 p = d*t-center;
        float r = length(p);
        if (t > 0.0 && t < planetT && r > 2.65 && r < 4.55) {
            float stripes = 0.5+0.5*sin(r*54.0+noise(p*9.0)*0.18);
            float density = smoothstep(2.65,2.82,r)*(1.0-smoothstep(4.25,4.55,r));
            density *= (0.48+stripes*0.18)*(0.05+smoothstep(0.015,0.06,abs(r-3.50))*0.95);
            float shadow = sphere(SUN,-p,radius) < 1e4 ? 0.12 : 1.0;
            vec3 ringColor = mix(vec3(0.37,0.39,0.37),vec3(0.67,0.59,0.45),stripes) * shadow;
            color = mix(color,ringColor,density);
        }
    }

    vec3 moonCenter = normalize(vec3(-0.57,0.36,-1.0))*16.0;
    float moonT = sphere(d,moonCenter,0.52);
    if (moonT < 1e4) {
        vec3 n = normalize(d*moonT-moonCenter);
        float rock = fbm(n*14.0);
        color = vec3(0.30,0.34,0.40)*(0.4+rock)*(0.035+max(dot(n,SUN),0.0));
    }
    float sun = max(dot(d,SUN),0.0);
    color += vec3(1.0,0.64,0.30) * (pow(sun,3400.0)*2.0 + pow(sun,120.0)*0.14);
    color = color/(1.0+color*0.35);
    outColor = vec4(pow(max(color,0.0),vec3(1.0/2.2)),1.0);
}
"""

    let private displayFragment = """#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uSky;
uniform mat4 uView;
uniform vec2 uLens;
out vec4 outColor;
void main() {
    vec2 xy = (vUv*2.0-1.0)*uLens;
    vec3 ray = normalize(transpose(mat3(uView))*vec3(xy,-1.0));
    vec2 uv = vec2(atan(ray.x,-ray.z)/6.28318530718+0.5,asin(clamp(ray.y,-1.0,1.0))/3.14159265359+0.5);
    vec3 color = texture(uSky,uv).rgb;
    float vignette = 1.0 - 0.16 * dot(vUv-0.5,vUv-0.5);
    outColor = vec4(color*vignette,1.0);
}
"""

    [<Emit("$0.texImage2D($0.TEXTURE_2D, 0, $0.RGBA, $1, $2, 0, $0.RGBA, $0.UNSIGNED_BYTE, null)")>]
    let private allocate (gl: WebGL2RenderingContext) (width: int) (height: int) : unit = jsNative

    let create (renderer: Renderer) : SkyBackdrop =
        let gl = renderer.Gl
        let vao = gl.createVertexArray ()
        let bake = Renderer.linkProgram gl vertex bakeFragment
        let program = Renderer.linkProgram gl vertex displayFragment
        let texture = gl.createTexture ()
        gl.bindTexture (gl.TEXTURE_2D, texture)
        // 18 MiB at full quality, bounded by the device's actual texture limit.
        let width = min 3072 (unbox<int> (gl.getParameter gl.MAX_TEXTURE_SIZE))
        let height = width / 2
        allocate gl width height
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
        let framebuffer = gl.createFramebuffer ()
        gl.bindFramebuffer (gl.FRAMEBUFFER, framebuffer)
        gl.framebufferTexture2D (gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0.0)
        if gl.checkFramebufferStatus gl.FRAMEBUFFER <> gl.FRAMEBUFFER_COMPLETE then
            failwith "Die Himmelstextur konnte nicht erstellt werden."
        gl.viewport (0.0, 0.0, float width, float height)
        gl.disable gl.DEPTH_TEST
        gl.disable gl.CULL_FACE
        gl.useProgram bake
        gl.bindVertexArray vao
        gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)
        gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
        gl.deleteFramebuffer framebuffer
        gl.deleteProgram bake
        gl.bindVertexArray (unbox null)
        gl.enable gl.DEPTH_TEST
        gl.enable gl.CULL_FACE
        gl.useProgram program
        gl.uniform1i (gl.getUniformLocation (program, "uSky"), 0.0)
        { Program = program
          Texture = texture
          Vao = vao
          UView = gl.getUniformLocation (program, "uView")
          ULens = gl.getUniformLocation (program, "uLens") }

    let draw (renderer: Renderer) (sky: SkyBackdrop) (camera: Camera) =
        let gl = renderer.Gl
        gl.disable gl.DEPTH_TEST
        gl.depthMask false
        gl.useProgram sky.Program
        gl.uniformMatrix4fv (sky.UView, false, camera.View.Data)
        gl.uniform2f (sky.ULens, 1.0 / float camera.Projection.Data.[0], 1.0 / float camera.Projection.Data.[5])
        gl.activeTexture gl.TEXTURE0
        gl.bindTexture (gl.TEXTURE_2D, sky.Texture)
        gl.bindVertexArray sky.Vao
        gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)
        gl.enable gl.DEPTH_TEST
        gl.depthMask true
        gl.useProgram renderer.Program
        renderer.DrawCalls <- renderer.DrawCalls + 1
