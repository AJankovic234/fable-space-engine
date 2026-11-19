namespace SpaceEngine

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

type private EmblemPass =
    { Program: WebGLProgram
      Vao: WebGLVertexArrayObject
      Texture: WebGLTexture
      ViewProj: WebGLUniformLocation
      Center: WebGLUniformLocation
      Right: WebGLUniformLocation
      Forward: WebGLUniformLocation
      Sun: WebGLUniformLocation
      Hdr: WebGLUniformLocation }

type ShipEmblem = private { Pass: Lazy<EmblemPass>; mutable Visible: bool }

[<RequireQualifiedAccess>]
module ShipEmblem =
    let private vertex = """#version 300 es
uniform mat4 uViewProj;
uniform vec3 uCenter, uRight, uForward;
out vec2 vUv;
void main() {
    vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
    vUv = vec2(corner.x, 1.0 - corner.y);
    vec3 world = uCenter + (corner.x - 0.5) * uRight + (corner.y - 0.5) * uForward;
    gl_Position = uViewProj * vec4(world, 1.0);
}
"""

    let private fragment = """#version 300 es
precision highp float;
uniform sampler2D uEmblem;
uniform vec3 uRight, uForward, uSun;
uniform bool uHdr;
in vec2 vUv;
out vec4 outColor;
void main() {
    vec4 ink = texture(uEmblem, vUv);
    if (ink.a < 0.01) discard;
    vec3 normal = normalize(cross(uRight, uForward));
    float light = 0.48 + 0.52 * max(0.0, dot(normal, uSun));
    vec3 radiance = pow(ink.rgb, vec3(2.2)) * light;
    vec3 color = uHdr ? radiance : pow(radiance, vec3(1.0 / 2.2));
    outColor = vec4(color, ink.a);
}
"""

    [<Import("mountShipEmblem", "../src/ship-emblem.mjs")>]
    let private mountSettings (_update: HTMLElement -> unit) : unit = jsNative

    [<Emit("$0.texImage2D($0.TEXTURE_2D, 0, $0.RGBA, $0.RGBA, $0.UNSIGNED_BYTE, $1)")>]
    let private upload (gl: WebGL2RenderingContext) (image: HTMLElement) : unit = jsNative

    let create (renderer: Renderer) =
        let gl = renderer.Gl
        let pass = lazy (
            let program = Renderer.linkProgram gl vertex fragment
            let uniform name = gl.getUniformLocation (program, name)
            { Program = program; Vao = gl.createVertexArray (); Texture = gl.createTexture ()
              ViewProj = uniform "uViewProj"; Center = uniform "uCenter"
              Right = uniform "uRight"; Forward = uniform "uForward"
              Sun = uniform "uSun"; Hdr = uniform "uHdr" })
        let emblem = { Pass = pass; Visible = false }
        mountSettings (fun image ->
            if not (isNull image) then
                gl.activeTexture gl.TEXTURE0
                gl.bindTexture (gl.TEXTURE_2D, pass.Value.Texture)
                upload gl image
                gl.generateMipmap gl.TEXTURE_2D
                gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR)
                gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
                gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
                gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
                gl.bindTexture (gl.TEXTURE_2D, unbox null)
            emblem.Visible <- not (isNull image))
        emblem

    // Abstände und Flächen folgen den lokalen Rumpfmeshes. +Y ist oben, -Z ist vorn.
    let private surface (fleet: HangarFleet) loaded bomber turret =
        if bomber then Vec3(0.0, 0.374, 1.30), 0.46, 0.0, -0.20
        else
            match fleet.Active with
            | 1 -> Vec3(0.0, 0.769, -1.80), 0.44, 0.0, 0.0
            | 2 when loaded -> Vec3(1.96, 0.088, 1.14), 0.22, 0.1582, -0.040
            | 2 -> Vec3(1.60, 0.066, 0.95), 0.44, -0.0068, -0.0679
            | 3 -> Vec3(0.60, 0.330, 1.30), 0.38, 0.0, 0.0
            | _ when turret && fleet.Kit <> ShipCatalog.ShieldRelay -> Vec3(0.0, 0.272, 1.52), 0.46, 0.0, 0.0
            | _ -> Vec3(0.25, 0.352, -0.15), 0.48, 0.0, 0.0

    // Nach deckenden Modellen zeichnen, solange der Tiefenpuffer die Flächen verdeckt.
    let draw (renderer: Renderer) emblem position rotation fleet loaded bomber turret =
        if emblem.Visible then
            let pass = emblem.Pass.Value
            let gl = renderer.Gl
            let center, width, slopeX, slopeZ = surface fleet loaded bomber turret
            let transform v = Quat.rotateVec3 rotation v
            let vector location (v: Vec3) = gl.uniform3f (location, v.X, v.Y, v.Z)
            gl.useProgram pass.Program
            gl.uniformMatrix4fv (pass.ViewProj, false, renderer.ViewProj.Data)
            vector pass.Center (position + transform center)
            vector pass.Right (transform (Vec3(width, slopeX * width, 0.0)))
            vector pass.Forward (transform (Vec3(0.0, -slopeZ * width, -width)))
            gl.uniform3fv (pass.Sun, renderer.Lights.SunDirection)
            gl.uniform1i (pass.Hdr, if renderer.Post.Hdr then 1.0 else 0.0)
            gl.activeTexture gl.TEXTURE0
            gl.bindTexture (gl.TEXTURE_2D, pass.Texture)
            gl.bindVertexArray pass.Vao
            gl.enable gl.BLEND
            gl.blendFunc (gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA)
            gl.depthMask false
            gl.drawArrays (gl.TRIANGLE_STRIP, 0.0, 4.0)
            gl.depthMask true
            gl.disable gl.BLEND
            renderer.BlendState <- 0
            gl.bindVertexArray (unbox null)
            gl.bindTexture (gl.TEXTURE_2D, unbox null)
            gl.useProgram renderer.Program
            renderer.DrawCalls <- renderer.DrawCalls + 1
