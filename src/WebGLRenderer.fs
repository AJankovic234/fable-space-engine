namespace SpaceEngine

// =====================================================================================
//  WebGLRenderer.fs  -  nativer WebGL2-Renderer ohne externe 3D-Bibliothek
//
//  Pipeline in einem Satz: ALLES wird instanziert gezeichnet. Ein InstanceBatch
//  verbindet ein Mesh mit einem Instanz-Buffer (Modellmatrix + Farbe pro Instanz)
//  und kostet genau einen Draw-Call - egal ob 1 Spielerschiff oder 2000 Partikel.
//
//  Vertex-Layout (pro Vertex, 10 Floats):  location 0 = Position, 1 = Normale,
//                                           location 2 = vec4 (RGB, Emissive 0..1;
//                                           negativ = Oberflaechenklasse, siehe Materials)
//  Instanz-Layout (pro Instanz, 20 Floats): location 3..6 = mat4 Modellmatrix,
//                                           location 7 = vec4 (RGB, Emissive 0..1)
//
//  Endfarbe = Vertexfarbe * Instanzfarbe; Emissive = Maximum beider Werte. So kann
//  ein Mesh eigene Farbzonen tragen (Cockpit, Triebwerksgluehen) und trotzdem pro
//  Instanz eingefaerbt werden.
//
//  Beleuchtung: physikalisch basiert (GGX, Smith, Schlick) mit Sonne, Himmelslicht
//  des Sektors (SkyLight) und bis zu 32 Point Lights, von denen Lights pro Frame die
//  wichtigsten waehlt (LightSelection). Emissive = 1 schaltet die Beleuchtung fuer
//  die Instanz ab (Laser, Sterne, Partikel).
//
//  Der Frame-Pfad (beginFrame / InstanceBatch.push* / draw) allokiert nichts:
//  alle Puffer und Uniform-Arrays werden beim Setup angelegt.
// =====================================================================================

open Fable.Core
open Browser.Types
open Browser.Dom

// -------------------------------------------------------------------------------------
//  WebGL2-Bindings. Fable.Browser.WebGL (1.3.0) deckt nur WebGL1 ab; die WebGL2-
//  Erweiterungen, die die Engine braucht (VAOs, Instancing), werden hier ergaenzt.
// -------------------------------------------------------------------------------------

/// Opaques Handle eines Vertex Array Objects.
type WebGLVertexArrayObject =
    interface
    end

type WebGL2RenderingContext =
    inherit WebGLRenderingContext
    abstract createVertexArray: unit -> WebGLVertexArrayObject
    abstract bindVertexArray: vertexArray: WebGLVertexArrayObject -> unit
    abstract deleteVertexArray: vertexArray: WebGLVertexArrayObject -> unit
    abstract vertexAttribDivisor: index: float * divisor: float -> unit
    abstract drawArraysInstanced: mode: float * first: float * count: float * instanceCount: float -> unit

    abstract drawElementsInstanced:
        mode: float * count: float * ``type``: float * offset: float * instanceCount: float -> unit

    /// WebGL2-Ueberladung von bufferSubData: laedt nur `length` Elemente ab `srcOffset`
    /// hoch, ohne dass dafuer eine subarray()-View allokiert werden muss.
    [<Emit("$0.bufferSubData($1, $2, $3, $4, $5)")>]
    abstract bufferSubDataRange:
        target: float * dstByteOffset: float * srcData: JS.Float32Array * srcOffset: float * length: float -> unit

    [<Emit("$0.texImage2D($0.TEXTURE_2D, 0, $0.RGBA, $1, $2, 0, $0.RGBA, $0.UNSIGNED_BYTE, null)")>]
    abstract allocRgbaTexture: width: int * height: int -> unit

    [<Emit("$0.texImage2D($0.TEXTURE_2D, 0, $0.RGBA16F, $1, $2, 0, $0.RGBA, $0.HALF_FLOAT, null)")>]
    abstract allocHalfFloatTexture: width: int * height: int -> unit

    [<Emit("$0.renderbufferStorage($0.RENDERBUFFER, $0.DEPTH_COMPONENT24, $1, $2)")>]
    abstract allocDepth24: width: int * height: int -> unit

    [<Emit("$0.renderbufferStorageMultisample($0.RENDERBUFFER, $1, $2 ? $0.RGBA16F : $0.RGBA8, $3, $4)")>]
    abstract allocMsaaColor: samples: int * hdr: bool * width: int * height: int -> unit

    [<Emit("$0.renderbufferStorageMultisample($0.RENDERBUFFER, $1, $0.DEPTH_COMPONENT24, $2, $3)")>]
    abstract allocMsaaDepth: samples: int * width: int * height: int -> unit

    [<Emit("$0.getParameter($0.MAX_SAMPLES)")>]
    abstract maxSamples: unit -> int

    /// Tiefentextur mit Hardware-Vergleich (sampler2DShadow) und linearem Filter (PCF).
    [<Emit("($0.texImage2D($0.TEXTURE_2D, 0, $0.DEPTH_COMPONENT24, $1, $1, 0, $0.DEPTH_COMPONENT, $0.UNSIGNED_INT, null), $0.texParameteri($0.TEXTURE_2D, $0.TEXTURE_COMPARE_MODE, $0.COMPARE_REF_TO_TEXTURE), $0.texParameteri($0.TEXTURE_2D, $0.TEXTURE_COMPARE_FUNC, $0.LEQUAL))")>]
    abstract allocShadowTexture: size: int -> unit

    /// Framebuffer ohne Farbanhang (nur Tiefe).
    [<Emit("($0.drawBuffers([$0.NONE]), $0.readBuffer($0.NONE))")>]
    abstract depthOnlyTarget: unit -> unit

    [<Emit("($0.enable($0.POLYGON_OFFSET_FILL), $0.polygonOffset($1, $2))")>]
    abstract enableDepthBias: factor: float * units: float -> unit

    [<Emit("$0.disable($0.POLYGON_OFFSET_FILL)")>]
    abstract disableDepthBias: unit -> unit

    /// Loest das Multisample-Ziel `source` in das einfach abgetastete Ziel `target` auf.
    [<Emit("($0.bindFramebuffer($0.READ_FRAMEBUFFER, $1), $0.bindFramebuffer($0.DRAW_FRAMEBUFFER, $2), $0.blitFramebuffer(0, 0, $3, $4, 0, 0, $3, $4, $0.COLOR_BUFFER_BIT, $0.NEAREST))")>]
    abstract resolveColor: source: WebGLFramebuffer * target: WebGLFramebuffer * width: int * height: int -> unit

// -------------------------------------------------------------------------------------
//  Datentypen
// -------------------------------------------------------------------------------------

/// Feste Groessen der Vertex- und Instanzformate.
module Layout =
    /// Floats pro Vertex: Position (3) + Normale (3) + Farbe/Emissive (4).
    [<Literal>]
    let VertexFloats = 10

    /// Offset der Farbe innerhalb eines Vertex.
    [<Literal>]
    let VertexColorOffset = 6

    /// Floats pro Instanz: Modellmatrix (16) + Farbe/Emissive (4).
    [<Literal>]
    let InstanceFloats = 20

    /// Offset der Farbe innerhalb einer Instanz.
    [<Literal>]
    let InstanceColorOffset = 16

    /// Obergrenze der gleichzeitig aktiven Point Lights (muss zum Shader passen).
    /// Wie viele davon ein Frame nutzt, bestimmt Lights.setBudget.
    [<Literal>]
    let MaxPointLights = 32

    /// Anzahl der Bloom-Eingänge des Composite-Shaders.
    [<Literal>]
    let MaxBloomLevels = 3

    /// Halbe Kantenlaenge des beschatteten Wuerfels um den Spieler im freien Flug (Meter).
    /// Nahe grosser Bauwerke waehlt der Aufrufer einen groesseren Wert.
    [<Literal>]
    let ShadowRadius = 80.0

    /// Halbe Tiefe der Schattenkarte entlang der Sonnenrichtung (Meter).
    [<Literal>]
    let ShadowDepth = 300.0

    /// Textureinheit der Schattenkarte; 0..3 gehoeren dem Post-Processing.
    [<Literal>]
    let ShadowUnit = 4

/// Geometrie auf der CPU-Seite: verschachtelte Vertices [px py pz nx ny nz r g b e]
/// und 16-Bit-Indizes (max. 65 536 Vertices pro Mesh - reicht fuer Low-Poly).
type MeshData =
    { Vertices: JS.Float32Array
      Indices: JS.Uint16Array }

/// Geometrie auf der GPU.
type Mesh =
    { VertexBuffer: WebGLBuffer
      IndexBuffer: WebGLBuffer
      IndexCount: int }

/// Wie ein Batch mit dem Framebuffer verrechnet wird.
type BlendMode =
    /// Deckend, schreibt in den Tiefenpuffer.
    | Opaque
    /// Additiv (Leuchten: Laser, Partikel, Sterne), schreibt nicht in den Tiefenpuffer.
    | Additive
    /// Halbtransparent (vorbereitet fuer Schildblasen, Trails, Hologramme), schreibt nicht in den Tiefenpuffer.
    | Alpha

/// Ein Mesh plus Instanz-Buffer = ein Draw-Call.
/// Pro Frame: clear -> push* -> Renderer.draw.
type InstanceBatch =
    { Mesh: Mesh
      Vao: WebGLVertexArrayObject
      InstanceBuffer: WebGLBuffer
      /// CPU-Spiegel des Instanz-Buffers (Capacity * Layout.InstanceFloats).
      Data: JS.Float32Array
      Capacity: int
      Blend: BlendMode
      /// Anzahl der in diesem Frame geschriebenen Instanzen.
      mutable Count: int
      /// true, sobald sich Instanzdaten geaendert haben. Statische Batches (Sterne)
      /// werden so nur ein einziges Mal zur GPU hochgeladen.
      mutable NeedsUpload: bool
      /// true = Positionen sind relativ zur Kamera (Himmel): der Batch wird ohne
      /// Kamera-Translation gezeichnet und wirkt dadurch unendlich weit entfernt.
      mutable CameraRelative: bool
      /// Glanzexponent (groesser = kleineres, schaerferes Glanzlicht); der Shader macht daraus die GGX-Rauheit.
      mutable Shininess: float
      /// Staerke des Glanzlichts (0 = matt).
      mutable Specular: float
      /// 0 = lackiertes Metall, 1 = raues Gestein, 2 = Eis, 3 = Meteoreisen.
      mutable Material: float }

/// Lichtzustand der Szene; wird in beginFrame als Uniforms hochgeladen.
type Lights =
    { /// Normalisierte Richtung ZUR Sonne (Weltkoordinaten).
      SunDirection: JS.Float32Array
      SunColor: JS.Float32Array
      /// Flacher Sektorwert: Nebelfarbe und Grundpegel des Himmelslichts.
      Ambient: JS.Float32Array
      /// Himmelslicht als L1-Kugelflaechenfunktion (Irradianz in Shader-Einheiten):
      /// 4 x RGB = Konstante, dann die Anteile entlang x, y und z.
      SkySh: JS.Float32Array
      /// Pro Licht: xyz = Position, w = Reichweite. Die in beginFrame gewaehlten Lichter.
      PointPosRange: JS.Float32Array
      /// Pro Licht: rgb = Farbe * Intensitaet, w ungenutzt.
      PointColor: JS.Float32Array
      mutable PointCount: int
      /// Kandidaten dieses Frames (LightSelection.Capacity), gleiches Layout wie oben.
      CandidatePosRange: JS.Float32Array
      CandidateColor: JS.Float32Array
      mutable CandidateCount: int
      /// Hoechstzahl aktiver Lichter pro Frame (1 .. Layout.MaxPointLights).
      mutable Budget: int
      /// Scratch der Auswahl.
      Scores: float[]
      Order: int[] }

/// Offscreen-Renderziel (4x MSAA, aufgeloest in SceneTex) + dreistufige Bloom-Kette
/// (1/4, 1/8, 1/16 der Aufloesung, je ein Ping-Pong-Paar; keine Frame-Allokationen).
type PostProcess =
    { Vao: WebGLVertexArrayObject
      SceneFbo: WebGLFramebuffer
      SceneTex: WebGLTexture
      SceneDepth: WebGLRenderbuffer
      /// Multisample-Ziel, in das die Szene gezeichnet wird, solange Msaa = true.
      mutable MsaaFbo: WebGLFramebuffer
      mutable MsaaColor: WebGLRenderbuffer
      mutable MsaaDepth: WebGLRenderbuffer
      MsaaSamples: int
      mutable Msaa: bool
      mutable MsaaAllowed: bool
      mutable BloomLevels: int
      BlackTex: WebGLTexture
      mutable NeedsResize: bool
      BloomFboA: WebGLFramebuffer[]
      BloomTexA: WebGLTexture[]
      BloomFboB: WebGLFramebuffer[]
      BloomTexB: WebGLTexture[]
      BrightProg: WebGLProgram
      UBrightScene: WebGLUniformLocation
      UBrightHdr: WebGLUniformLocation
      BlurProg: WebGLProgram
      UBlurTex: WebGLUniformLocation
      UBlurDir: WebGLUniformLocation
      CompositeProg: WebGLProgram
      UCompScene: WebGLUniformLocation
      UCompBloom: WebGLUniformLocation[]
      UCompBoost: WebGLUniformLocation
      UCompHit: WebGLUniformLocation
      UCompHdr: WebGLUniformLocation
      SupportsHdr: bool
      mutable Hdr: bool
      mutable BoostEffect: float
      mutable HitEffect: float
      BloomW: int[]
      BloomH: int[]
      mutable Ready: bool }

type Renderer =
    { Canvas: HTMLCanvasElement
      Gl: WebGL2RenderingContext
      Program: WebGLProgram
      UViewProj: WebGLUniformLocation
      UCameraPos: WebGLUniformLocation
      UVeil: WebGLUniformLocation
      mutable VeilDensity: float
      USunDir: WebGLUniformLocation
      USunColor: WebGLUniformLocation
      UAmbient: WebGLUniformLocation
      UPointCount: WebGLUniformLocation
      UPointPosRange: WebGLUniformLocation
      UPointColor: WebGLUniformLocation
      UShininess: WebGLUniformLocation
      USpecular: WebGLUniformLocation
      UMaterial: WebGLUniformLocation
      USkySh: WebGLUniformLocation
      UHdr: WebGLUniformLocation
      // --- Sonnenschatten: eine orthografische Tiefenkarte um den Spieler ---
      UShadowMatrix: WebGLUniformLocation
      UShadowStrength: WebGLUniformLocation
      UShadowOffset: WebGLUniformLocation
      ShadowProgram: WebGLProgram
      UShadowLight: WebGLUniformLocation
      ShadowFbo: WebGLFramebuffer
      ShadowTex: WebGLTexture
      /// Welt -> Clipraum der Sonne; dieselbe Matrix zeichnet und liest die Karte.
      ShadowMatrix: Mat4
      /// false, wenn die Tiefenkarte nicht angelegt werden konnte.
      mutable ShadowSupported: bool
      mutable ShadowSize: int
      mutable ShadowsOn: bool
      Lights: Lights
      /// Scratch: Projektion * View des aktuellen Frames (auch vom HUD nutzbar).
      ViewProj: Mat4
      /// Scratch: View-Matrix ohne Translation und die zugehoerige Projektion * View
      /// fuer kamerarelative Batches.
      SkyView: Mat4
      SkyViewProj: Mat4
      /// Scratch: Kameraposition als Uniform-Quelle.
      CameraPos: JS.Float32Array
      /// Hintergrundfarbe (RGB).
      ClearColor: JS.Float32Array
      /// Groesse des Drawing Buffers in physischen Pixeln.
      mutable Width: int
      mutable Height: int
      /// Verhaeltnis physische Pixel / CSS-Pixel des aktuellen Frames.
      mutable PixelRatio: float
      mutable RenderScale: float
      mutable DprLimit: float
      MaxTargetSize: float
      /// Aktiver Blend-Zustand: 0 = Opaque, 1 = Additive (vermeidet redundante GL-Calls).
      mutable BlendState: int
      /// Aktive uViewProj-Matrix: 0 = Welt, 1 = kamerarelativ.
      mutable ViewProjState: int
      /// Statistik des aktuellen Frames.
      mutable DrawCalls: int
      mutable InstancesDrawn: int
      Post: PostProcess }

// -------------------------------------------------------------------------------------
//  Shader (GLSL ES 3.00)
// -------------------------------------------------------------------------------------

module Shaders =

    // Shared by the final composite and the compatibility path which draws
    // directly in display space. HDR geometry and additive effects stay linear.
    let displayTransform = """
vec3 displayColor(vec3 radiance) {
    vec3 x = max(radiance, vec3(0.0));
    vec3 a = x * (x + 0.0245786) - 0.000090537;
    vec3 b = x * (0.983729 * x + 0.4329510) + 0.238081;
    return pow(clamp(a / b, 0.0, 1.0), vec3(1.0 / 2.2));
}
"""

    let vertex =
        """#version 300 es
precision highp float;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aColor;   // rgb = Vertexfarbe, a = Emissive
layout(location = 3) in mat4 iModel;   // belegt location 3..6
layout(location = 7) in vec4 iColor;   // rgb = Instanzfarbe, a = Emissive

uniform mat4 uViewProj;

out vec3 vWorldPos;
out vec3 vNormal;
out vec4 vColor;
out vec3 vLocalPos;
flat out float vScale;
flat out float vSurface;   // Materials: 0 Lack, 1 Metall, 2 Glas, 3 Gummi

void main() {
    vec4 world = iModel * vec4(aPosition, 1.0);
    vWorldPos = world.xyz;
    vLocalPos = aPosition;

    // Normalenmatrix fuer M = R * S (auch nicht-uniforme Skalierung):
    // (M^-1)^T = R * S^-1 = M * S^-2. Die quadrierten Skalierungen sind die
    // quadrierten Spaltenlaengen von M - das spart ein inverse() pro Vertex.
    mat3 m = mat3(iModel);
    vec3 scaleSq = vec3(dot(m[0], m[0]), dot(m[1], m[1]), dot(m[2], m[2]));
    vNormal = m * (aNormal / scaleSq);
    vScale = sqrt(scaleSq.x);

    // Negativer Emissive-Kanal = -(Oberflaechenklasse + Eigenleuchten), siehe Materials.encode.
    float channel = aColor.a;
    vSurface = channel < 0.0 ? floor(-channel) : 0.0;
    float glow = channel < 0.0 ? -channel - vSurface : channel;
    vColor = vec4(aColor.rgb * iColor.rgb, max(glow, iColor.a));
    gl_Position = uViewProj * world;
}
"""

    let fragment =
        """#version 300 es
precision highp float;

#define MAX_POINT_LIGHTS 32
#define PI 3.14159265

in vec3 vWorldPos;
in vec3 vNormal;
in vec4 vColor;
in vec3 vLocalPos;
flat in float vScale;
flat in float vSurface;

uniform vec3 uCameraPos;
uniform float uVeil;
uniform vec3 uSunDir;      // normalisiert, zeigt ZUR Sonne
uniform vec3 uSunColor;
uniform vec3 uAmbient;
uniform int uPointCount;
uniform vec4 uPointPosRange[MAX_POINT_LIGHTS]; // xyz = Position, w = Reichweite
uniform vec4 uPointColor[MAX_POINT_LIGHTS];    // rgb = Farbe * Intensitaet
uniform float uShininess;
uniform float uSpecular;
uniform float uMaterial;
uniform vec3 uSkySh[4];   // Himmelslicht (L1): Konstante, Anteile entlang x, y, z

uniform bool uHdr;
uniform mat4 uShadowMatrix;
uniform highp sampler2DShadow uShadowMap;
uniform float uShadowStrength;
uniform float uShadowOffset;   // Versatz entlang der Normale in Metern (waechst mit der Texelgroesse)

out vec4 outColor;

        """ + displayTransform + """
// Anteil des Sonnenlichts, der den Punkt erreicht (1 = frei, 0 = im Schatten).
// Vier Hardware-PCF-Abgriffe im gedrehten Raster ergeben eine weiche Kante. Zum
// Rand der Karte hin blendet der Schatten aus, damit keine Kante durchs Bild laeuft.
float sunVisibility(vec3 n) {
    if (uShadowStrength <= 0.0) return 1.0;
    vec3 s = (uShadowMatrix * vec4(vWorldPos + n * uShadowOffset, 1.0)).xyz * 0.5 + 0.5;
    vec2 border = min(s.xy, 1.0 - s.xy);
    float inside = smoothstep(0.0, 0.06, min(border.x, border.y)) * step(0.0, s.z) * step(s.z, 1.0);
    if (inside <= 0.0) return 1.0;
    float texel = 1.0 / 2048.0;
    float slope = max(dot(n, uSunDir), 0.0);
    float ref = s.z - mix(0.00075, 0.00022, slope);
    float lit = texture(uShadowMap, vec3(s.xy + texel * vec2(-0.75, -0.25), ref))
              + texture(uShadowMap, vec3(s.xy + texel * vec2( 0.25, -0.75), ref))
              + texture(uShadowMap, vec3(s.xy + texel * vec2( 0.75,  0.25), ref))
              + texture(uShadowMap, vec3(s.xy + texel * vec2(-0.25,  0.75), ref));
    return mix(1.0, lit * 0.25, inside * uShadowStrength);
}
float hash31(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}
float grain(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash31(i), hash31(i + vec3(1,0,0)), f.x),
                   mix(hash31(i + vec3(0,1,0)), hash31(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash31(i + vec3(0,0,1)), hash31(i + vec3(1,0,1)), f.x),
                   mix(hash31(i + vec3(0,1,1)), hash31(i + vec3(1,1,1)), f.x), f.y), f.z);
}
// Himmelslicht aus der L1-Naeherung des Sektorhimmels (SkyLight): diffuse Irradianz ...
vec3 skyIrradiance(vec3 n) {
    return max(uSkySh[0] + uSkySh[1] * n.x + uSkySh[2] * n.y + uSkySh[3] * n.z, uSkySh[0] * 0.2);
}
// ... und gespiegelte Strahldichte: Der Verlauf ist dort 1,5-mal staerker als in der
// Irradianz, raue Flaechen sehen nur den geglaetteten Verlauf.
vec3 skyRadiance(vec3 r, float rough) {
    vec3 grad = uSkySh[1] * r.x + uSkySh[2] * r.y + uSkySh[3] * r.z;
    return max(uSkySh[0] + grad * mix(1.5, 1.0, rough), uSkySh[0] * 0.15);
}
// Umgebungs-BRDF (Karis' analytische Naeherung des Split-Sum-Integrals): Faktor und Versatz fuer F0.
vec2 envBrdf(float rough, float NdotV) {
    const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
    const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
    vec4 r = rough * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    return vec2(-1.04, 1.04) * a004 + r.zw;
}
// Ein Licht: GGX-Verteilung, hoehenkorrelierter Smith-Term, Schlick-Fresnel. `irradiance`
// in Shader-Einheiten wie bisher (Lambert = Albedo * E * NdotL); was das Fresnel spiegelt,
// fehlt dem Diffusanteil.
void shade(vec3 L, vec3 irradiance, vec3 N, vec3 V, float NdotV, float a2, vec3 f0, inout vec3 diff, inout vec3 spec) {
    float NdotL = dot(N, L);
    if (NdotL <= 0.0) return;
    vec3 H = normalize(L + V);
    float NdotH = max(dot(N, H), 0.0);
    float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
    float D = a2 / (PI * d * d);
    float gv = NdotL * sqrt(NdotV * NdotV * (1.0 - a2) + a2);
    float gl = NdotV * sqrt(NdotL * NdotL * (1.0 - a2) + a2);
    // Im Streiflicht waechst der Term stark an; ebene Facetten brennen sonst ganz aus.
    float vis = min(0.5 / max(gv + gl, 1e-5), 0.6);
    vec3 F = f0 + (1.0 - f0) * pow(1.0 - max(dot(V, H), 0.0), 5.0);
    vec3 E = irradiance * NdotL;
    diff += E * (1.0 - F);
    spec += E * F * (PI * D * vis);
}

void main() {
    vec3 albedo = vColor.rgb;
    // Additive effects stay linear in brightness and avoid all surface work.
    if (vColor.a > 0.999) {
        vec3 emission = max(albedo, 0.0) * vColor.a;
        outColor = vec4(uHdr ? emission : displayColor(emission), 1.0);
        return;
    }
    vec3 N = normalize(vNormal);
    vec3 faceN = N;
    vec3 V = normalize(uCameraPos - vWorldPos);
    float specScale = 1.0;
    float plateVar = 0.5;   // Streuung der Rauheit je Platte (blankes Metall)
    bool ice = uMaterial > 1.5 && uMaterial < 2.5;
    // Oberflaechenklasse aus dem Vertex; Gestein und Eis haben eigene Zweige.
    float surface = uMaterial > 0.5 ? 0.0 : vSurface;
    bool metal = surface > 0.5 && surface < 1.5;
    bool glass = surface > 1.5 && surface < 2.5;
    bool rubber = surface > 2.5;

    if (uMaterial > 0.5) {
        // Das Mesh hat Radius 1, das Muster wuechse also mit dem Brocken mit. Grosse
        // Brocken erhalten deshalb zwei feinere Lagen: eine mittlere fuer die Ansicht
        // aus der Distanz und eine in fester Weltgroesse fuer den Vorbeiflug. Jede Lage
        // blendet aus, bevor sie kleiner als ein Pixel wird. Bis 6 m Radius ist
        // growth = 1 und das Ergebnis identisch zum einlagigen Muster.
        float growth = max(1.0, vScale / 6.0);
        float large = smoothstep(1.0, 3.0, growth);
        vec3 mid = vLocalPos * sqrt(growth);
        vec3 rock = vLocalPos * growth;
        float midFade = 1.0 - smoothstep(0.012, 0.06, length(fwidth(mid)));
        float fineFade = (1.0 - smoothstep(0.012, 0.06, length(fwidth(rock)))) * large;

        float strata = grain(vLocalPos * 7.0);
        strata = mix(strata, strata * 0.62 + grain(mid * 7.0 + 31.0) * 0.38, large);
        if (ice) {
            // Two sets of fractures run underneath the smooth exposed faces.
            vec2 planes = vec2(dot(vLocalPos, vec3(0.62, 1.0, -0.35)), dot(vLocalPos, vec3(-0.73, 0.26, 0.81)));
            planes += vec2(0.13, -0.19) + vec2(0.26, 0.19) * (strata - 0.5);
            vec2 seam = abs(planes);
            vec2 width = max(fwidth(planes), vec2(0.008));
            vec2 frost = 1.0 - smoothstep(vec2(0.008), vec2(0.017) + width, seam);
            float branch = 0.64 * frost.y * smoothstep(-0.01, 0.14, planes.x);
            float line = max(frost.x, branch) * midFade;
            albedo *= vec3(0.76, 1.03, 1.14) + 0.22 * strata;
            albedo = mix(albedo, vec3(0.52, 0.79, 0.85), line * 0.68);
            specScale = mix(1.2, 0.48, line);
        } else {
            bool iron = uMaterial > 2.5;
            albedo *= mix(0.70, 1.16, strata) * mix(1.0, 0.83 + grain(mid * 39.0) * 0.34, midFade);
            albedo *= mix(1.0, 0.88 + grain(rock * 39.0 + 11.0) * 0.24, fineFade);
            float exposed = smoothstep(0.53, 0.78, strata);
            albedo *= mix(vec3(1.0), iron ? vec3(1.21, 1.22, 1.25) : vec3(1.09, 0.96, 0.88), exposed);
            // The height gradient follows the rotating surface in world space.
            float height = grain(mid * (iron ? 5.0 : 12.0)) * vScale / sqrt(growth);
            vec3 dx = dFdx(vWorldPos), dy = dFdy(vWorldPos);
            vec3 tx = cross(dy, N), ty = cross(N, dx);
            float det = dot(dx, tx);
            vec3 slope = (tx * dFdx(height) + ty * dFdy(height)) * sign(det) / max(0.00001, abs(det));
            N = normalize(N - slope * (iron ? 0.035 : 0.069) * midFade);
            specScale = iron ? mix(0.6, 1.5, exposed) : mix(0.35, 1.25, exposed);
        }
    } else if (uSpecular > 0.05) {
        // Prozedurale Rumpfplatten (Haupt- & Mikro-Panellinien + Kantenabnutzung) fuer Schiffe, Turret und Stationen.
        float detailFade = 1.0 - smoothstep(0.015, 0.075, length(fwidth(vLocalPos)));
        vec3 pScaled = vLocalPos * 4.5;
        vec3 cell = floor(pScaled);
        vec3 fracP = abs(fract(pScaled) - 0.5);
        float edge = max(max(fracP.x, fracP.y), fracP.z);
        float seam = smoothstep(0.455, 0.495, edge) * detailFade;
        float edgeWear = smoothstep(0.41, 0.455, edge) * (1.0 - smoothstep(0.455, 0.485, edge)) * detailFade;

        vec3 pMicro = vLocalPos * 14.5;
        vec3 fracM = abs(fract(pMicro) - 0.5);
        float microEdge = max(max(fracM.x, fracM.y), fracM.z);
        float microSeam = smoothstep(0.465, 0.495, microEdge) * detailFade * 0.45;

        float panelTone = mix(0.92, 1.08, hash31(cell));
        plateVar = mix(0.5, hash31(cell + 13.0), detailFade);
        albedo *= mix(1.0, panelTone, 0.80 * detailFade);
        albedo *= 1.0 - seam * 0.24 - microSeam * 0.14;
        albedo += vec3(0.05, 0.07, 0.09) * edgeWear;
        specScale = mix(1.0, 0.88 + 0.28 * hash31(cell + 7.0), detailFade) * (1.0 - seam * 0.45) + edgeWear * 0.35;

        // Analytische Kantenfase entlang der naechsten Plattenfuge fuer realistische Glanzkanten im Streiflicht.
        vec3 sSign = sign(fract(pScaled) - 0.5);
        vec3 seamAxis = (fracP.x >= fracP.y && fracP.x >= fracP.z) ? vec3(sSign.x, 0.0, 0.0) :
                        (fracP.y >= fracP.z) ? vec3(0.0, sSign.y, 0.0) : vec3(0.0, 0.0, sSign.z);
        N = normalize(N + seamAxis * (edgeWear - seam * 0.65) * 0.16);
    }

    // Physikalisch basierte Parameter: Der Blinn-Phong-Exponent des Batches wird zur
    // GGX-Rauheit, Panellinien, Kantenabnutzung und Gestein (specScale) rauen auf oder
    // glaetten. uSpecular 0,5 entspricht dem Lack-F0 von 0,04.
    float rough = clamp(1.25 * sqrt(sqrt(2.0 / (uShininess + 2.0))) * inversesqrt(max(specScale, 0.25)), 0.08, 1.0);
    // Metalliclack: Flocken toenen den Glanz leicht in die Lackfarbe.
    vec3 specTint = (uMaterial <= 0.5) ? mix(vec3(1.0), albedo / max(max(albedo.r, albedo.g), max(albedo.b, 0.04)), 0.18) : vec3(1.0);
    vec3 f0 = vec3(0.08 * uSpecular) * specTint;
    float kd = 1.0;
    if (metal) {
        // Blankes Metall: Die Farbe ist die Reflexionsfarbe, kein Diffusanteil.
        f0 = min(albedo * 1.1 + 0.04, vec3(0.92));
        kd = 0.0;
        // Gewalztes, nicht poliertes Metall mit Platten unterschiedlicher Abnutzung: Ein
        // gleichmaessiger Glanz waere auf grossen ebenen Facetten nur ganz weiss oder schwarz.
        rough = clamp(rough * mix(0.75, 1.35, plateVar), 0.3, 1.0);
    } else if (glass) {
        f0 = vec3(0.07);
        rough = 0.07;
    } else if (rubber) {
        f0 = vec3(0.028);
        rough = max(rough, 0.8);
    }
    float a = rough * rough;
    // Direkte Lichter nicht schaerfer als Rauheit 0,14 (Glas 0,2): Sonst flimmert der
    // Glanzpunkt auf Facetten oder trifft nur bei exakt passendem Winkel.
    float aDirect = max(rough, glass ? 0.2 : 0.14);
    aDirect *= aDirect;
    float NdotV = max(dot(N, V), 1e-3);

    vec3 diff = vec3(0.0);
    vec3 spec = vec3(0.0);
    float sunVis = dot(N, uSunDir) > -0.25 ? sunVisibility(faceN) : 0.0;
    shade(uSunDir, uSunColor * sunVis, N, V, NdotV, aDirect * aDirect, f0, diff, spec);

    // Point Lights mit weichem, endlichem Abfall
    for (int i = 0; i < MAX_POINT_LIGHTS; i++) {
        if (i >= uPointCount) break;
        vec3 toLight = uPointPosRange[i].xyz - vWorldPos;
        float range = uPointPosRange[i].w;
        float d2 = dot(toLight, toLight);
        if (d2 < range * range) {
            float dist = sqrt(d2);
            float falloff = 1.0 - dist / range;
            shade(toLight / max(dist, 1e-4), uPointColor[i].rgb * (falloff * falloff), N, V, NdotV, aDirect * aDirect, f0, diff, spec);
        }
    }

    // Himmelslicht: diffus aus der Irradianz, Glanz als Spiegelung des Himmelsverlaufs.
    // Die Hemisphaeren-Verdunkelung nach unten bleibt als grobe Umgebungsverdeckung.
    vec2 env = envBrdf(rough, NdotV);
    vec3 envF = f0 * env.x + env.y;
    float hemiAo = (uMaterial <= 0.5) ? mix(0.68, 1.0, N.y * 0.5 + 0.5) : mix(0.84, 1.06, N.y * 0.5 + 0.5);
    vec3 ambient = skyIrradiance(N) * hemiAo * (1.0 - envF);
    // Glas sieht zusaetzlich Sterne und Nebeldetail, die der glatte Verlauf nicht enthaelt.
    vec3 reflection = skyRadiance(reflect(-V, N), rough) * envF * (glass ? 1.8 : mix(hemiAo, 1.0, 0.5));
    vec3 base = albedo * kd * (diff + ambient);
    vec3 gloss = (spec + reflection) * clamp(specScale, 0.3, 1.0);

    // Cool reflected nebula light separates the unlit silhouettes from space, tinted gently by the sector ambient.
    float rim = pow(1.0 - NdotV, 3.0);
    vec3 rims = vec3(0.0);
    if (ice) rims += albedo * vec3(0.05, 0.14, 0.20) * (0.40 + 0.60 * rim);
    vec3 rimTint = mix(vec3(0.055, 0.15, 0.21), uAmbient * 0.80 + vec3(0.02, 0.04, 0.06), 0.45);
    rims += rimTint * rim * (uMaterial > 0.5 ? 0.22 : 0.40);
    // Sonnenseitiger Streiflicht-Saum fuer klare Silhouetten gegen den Weltraum.
    float sunRim = rim * smoothstep(-0.22, 0.72, dot(N, uSunDir)) * sunVis;
    rims += uSunColor * sunRim * (uMaterial > 0.5 ? 0.14 : 0.20);

    // Emissive blendet von "beleuchtet" zu "selbstleuchtend" und pusht Leuchtteile leicht ueber 1.0 fuer sattere Highlights.
    // Glas spiegelt vor dem Kabinenlicht: Seine Spiegelung liegt ueber dem Leuchten.
    vec3 emissiveGlow = albedo * (1.0 + vColor.a * 0.45);
    vec3 color = glass ? mix(base + rims, emissiveGlow, vColor.a) + gloss
                       : mix(base + gloss + rims, emissiveGlow, vColor.a);

    // Kosmischer Distanznebel laesst weit entfernte Asteroiden und Stationen weich in die dunkle Sektorfarbe abtauchen.
    float camDist = length(uCameraPos - vWorldPos);
    float fogFactor = smoothstep(700.0, 2600.0, camDist);
    // Arbeitslichter und selbstleuchtende Fremdmaterialien bleiben auf Außenpeilung lesbar.
    color = mix(color, uAmbient * 0.14, fogFactor * mix(0.85, 0.32, vColor.a));
    color = mix(color, vec3(0.12,0.075,0.17), uVeil * smoothstep(80.0, 860.0, camDist) * 0.77);

    outColor = vec4(uHdr ? color : displayColor(color), 1.0);
}
"""

    /// Tiefen-Pass der Schattenkarte: dieselben Attribute wie der Hauptshader.
    let shadowVertex =
        """#version 300 es
precision highp float;
layout(location = 0) in vec3 aPosition;
layout(location = 3) in mat4 iModel;
uniform mat4 uLight;
void main() {
    gl_Position = uLight * (iModel * vec4(aPosition, 1.0));
}
"""

    let shadowFragment =
        """#version 300 es
precision highp float;
void main() { }
"""

    let postVertex =
        """#version 300 es
precision highp float;
out vec2 vUv;
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
"""

    let brightFragment =
        """#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uScene;
uniform bool uHdr;
out vec4 outColor;
vec3 highlight(vec3 c) {
    float peak = max(c.r, max(c.g, c.b));
    if (!uHdr) return c * smoothstep(0.64, 0.96, peak);
    // Threshold 1.0 in scene light, with a soft knee spanning 0.6 .. 1.4.
    float knee = clamp(peak - 0.6, 0.0, 0.8);
    return c * (max(peak - 1.0, knee * knee / 1.6) / max(peak, 0.0001));
}
void main() {
    // Four bilinear taps cover the source footprint of a quarter-size pixel.
    vec2 d = 1.0 / vec2(textureSize(uScene, 0));
    vec3 sum = highlight(texture(uScene, vUv - d).rgb)
             + highlight(texture(uScene, vUv + d).rgb)
             + highlight(texture(uScene, vUv + vec2(d.x, -d.y)).rgb)
             + highlight(texture(uScene, vUv + vec2(-d.x, d.y)).rgb);
    outColor = vec4(sum * 0.25, 1.0);
}
"""

    let blurFragment =
        """#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uTex;
uniform vec2 uDir;
out vec4 outColor;
void main() {
    vec3 sum = texture(uTex, vUv).rgb * 0.227027;
    sum += texture(uTex, vUv + uDir * 1.384615).rgb * 0.316216;
    sum += texture(uTex, vUv - uDir * 1.384615).rgb * 0.316216;
    sum += texture(uTex, vUv + uDir * 3.230769).rgb * 0.070270;
    sum += texture(uTex, vUv - uDir * 3.230769).rgb * 0.070270;
    outColor = vec4(sum, 1.0);
}
"""

    let compositeFragment =
        """#version 300 es
precision highp float;
in vec2 vUv;
uniform sampler2D uScene;
uniform sampler2D uBloom0;
uniform sampler2D uBloom1;
uniform sampler2D uBloom2;
uniform float uBoost;
uniform float uHitFlash;
uniform bool uHdr;
out vec4 outColor;
        """ + displayTransform + """
float edgeLuma(vec3 rgb) {
    return sqrt(max(0.0, dot(rgb, vec3(0.2126, 0.7152, 0.0722))));
}
// The scene texture is single-sampled. Soften diagonal stair steps during the
// existing composite pass, keeping isolated stars and straight panel seams crisp.
vec3 resolveEdge(vec3 center) {
    float luma = edgeLuma(center);
    if (fwidth(luma) < max(0.028, 0.10*luma)) return center;
    vec2 px = 1.0 / vec2(textureSize(uScene, 0));
    float left  = edgeLuma(texture(uScene, vUv - vec2(px.x, 0.0)).rgb);
    float right = edgeLuma(texture(uScene, vUv + vec2(px.x, 0.0)).rgb);
    float down  = edgeLuma(texture(uScene, vUv - vec2(0.0, px.y)).rgb);
    float up    = edgeLuma(texture(uScene, vUv + vec2(0.0, px.y)).rgb);
    vec2 slope = vec2(right-left, up-down);
    float major = max(abs(slope.x), abs(slope.y));
    float minor = min(abs(slope.x), abs(slope.y));
    if (minor < max(0.035, 0.28*major)) return center;
    vec2 stepUV = slope * (0.48/major) * px;
    return 0.5 * (texture(uScene, vUv-stepUV).rgb + texture(uScene, vUv+stepUV).rgb);
}
void main() {
    vec2 centered = vUv - 0.5;
    float r2 = dot(centered, centered);
    float chroma = uBoost * 0.009 * r2;
    vec2 dir = centered * chroma;
    vec3 scene = resolveEdge(texture(uScene, vUv).rgb);
    if (chroma > 0.0002) {
        scene.r = texture(uScene, vUv + dir).r;
        scene.b = texture(uScene, vUv - dir).b;
    }
    // Enger Saum, mittlerer Hof und weiter Schein aus drei Aufloesungsstufen.
    vec3 bloom = texture(uBloom0, vUv).rgb * 0.46
               + texture(uBloom1, vUv).rgb * 0.34
               + texture(uBloom2, vUv).rgb * 0.30;
    vec3 combined = scene + bloom * ((uHdr ? 0.32 : 0.58) + uBoost * 0.16);
    float vignette = 1.0 - (0.14 + uBoost * 0.30) * r2;
    combined *= vignette;
    float edgeMask = smoothstep(0.14, 0.48, r2);
    combined = mix(combined, combined + vec3(0.55, 0.06, 0.04), clamp(uHitFlash, 0.0, 1.0) * edgeMask * 0.65);
    vec3 display = uHdr ? displayColor(combined)
                        : combined / (1.0 + max(combined - 0.85, 0.0) * 0.45);
    // Dither von +-0.5 Ausgabestufen: bricht Farbbaender, bleibt aber unsichtbar.
    float dither = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    outColor = vec4(clamp(display + (dither - 0.5) / 255.0, 0.0, 1.0), 1.0);
}
"""

// -------------------------------------------------------------------------------------
//  Lichter
// -------------------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Lights =
    /// Aktive Punktlichter pro Frame, solange niemand setBudget aufruft (Stufe "Hoch").
    [<Literal>]
    let DefaultBudget = 16

    let create () : Lights =
        let f32 (n: int) = JS.Constructors.Float32Array.Create n
        let sunDir = f32 3
        sunDir.[1] <- 1.0f
        let sunColor = f32 3
        sunColor.[0] <- 1.0f
        sunColor.[1] <- 1.0f
        sunColor.[2] <- 1.0f

        { SunDirection = sunDir
          SunColor = sunColor
          Ambient = f32 3
          SkySh = f32 12
          PointPosRange = f32 (Layout.MaxPointLights * 4)
          PointColor = f32 (Layout.MaxPointLights * 4)
          PointCount = 0
          CandidatePosRange = f32 (LightSelection.Capacity * LightSelection.Stride)
          CandidateColor = f32 (LightSelection.Capacity * LightSelection.Stride)
          CandidateCount = 0
          Budget = DefaultBudget
          Scores = Array.zeroCreate LightSelection.Capacity
          Order = Array.zeroCreate LightSelection.Capacity }

    /// Richtung ZUR Sonne (wird normalisiert) und ihre Farbe/Intensitaet.
    let setSun (l: Lights) (dx: float) (dy: float) (dz: float) (r: float) (g: float) (b: float) =
        let len = sqrt (dx * dx + dy * dy + dz * dz)
        let inv = if len > 1e-12 then 1.0 / len else 0.0
        l.SunDirection.[0] <- float32 (dx * inv)
        l.SunDirection.[1] <- float32 (dy * inv)
        l.SunDirection.[2] <- float32 (dz * inv)
        l.SunColor.[0] <- float32 r
        l.SunColor.[1] <- float32 g
        l.SunColor.[2] <- float32 b

    /// Himmelslicht als L1-Koeffizienten (12 Werte: Konstante, x, y, z je RGB), siehe SkyLight.
    let setSky (l: Lights) (coefficients: float[]) =
        for i in 0 .. 11 do
            l.SkySh.[i] <- float32 coefficients.[i]

    /// Flacher Sektorwert (Nebel, Saum). Setzt zugleich ein Standard-Himmelslicht wie vor
    /// dem Himmelslicht: oben heller als unten, dazu ein kuehler Fuelllicht-Anteil von
    /// links vorn. SkyLight.apply ersetzt es durch den Himmel des Sektors.
    let setAmbient (l: Lights) (r: float) (g: float) (b: float) =
        l.Ambient.[0] <- float32 r
        l.Ambient.[1] <- float32 g
        l.Ambient.[2] <- float32 b
        let ambient = [| r; g; b |]
        let low = [| 0.55; 0.66; 0.86 |]
        let high = [| 1.15; 1.28; 1.45 |]
        let cool = [| 0.08; 0.20; 0.27 |]
        let lift = [| 0.02; 0.05; 0.07 |]
        let dirLen = sqrt (0.8 * 0.8 + 0.3 * 0.3 + 0.4 * 0.4)
        let dir = [| -0.8 / dirLen; 0.3 / dirLen; -0.4 / dirLen |]
        for c in 0 .. 2 do
            let fill = cool.[c] * 0.55 + (ambient.[c] * 0.95 + lift.[c]) * 0.45
            // Ein geklemmter Kosinus hat die L1-Anteile 1/4 (konstant) und 1/2 (Richtung).
            l.SkySh.[c] <- float32 (ambient.[c] * 0.5 * (low.[c] + high.[c]) + fill * 0.25)
            l.SkySh.[3 + c] <- float32 (fill * 0.5 * dir.[0])
            l.SkySh.[6 + c] <- float32 (ambient.[c] * 0.5 * (high.[c] - low.[c]) + fill * 0.5 * dir.[1])
            l.SkySh.[9 + c] <- float32 (fill * 0.5 * dir.[2])

    /// Hoechstzahl aktiver Punktlichter ab dem naechsten Frame (0 .. Layout.MaxPointLights).
    let setBudget (l: Lights) (n: int) =
        l.Budget <- System.Math.Max(0, System.Math.Min(Layout.MaxPointLights, n))

    /// Entfernt alle Point Lights (einmal pro Frame vor addPoint aufrufen).
    let inline clearPoints (l: Lights) =
        l.CandidateCount <- 0
        l.PointCount <- 0

    /// Meldet ein Point Light fuer diesen Frame. Welche Lichter der Shader bekommt,
    /// entscheidet select nach Wichtigkeit; die Reihenfolge der Aufrufe spielt keine Rolle.
    /// Sind alle Kandidatenplaetze belegt, ersetzt ein staerkeres Licht das schwaechste.
    let addPoint
        (l: Lights)
        (x: float)
        (y: float)
        (z: float)
        (range: float)
        (r: float)
        (g: float)
        (b: float)
        (intensity: float)
        =
        if range > 0.0 && intensity > 0.0 then
            let mutable slot = -1

            if l.CandidateCount < LightSelection.Capacity then
                slot <- l.CandidateCount
                l.CandidateCount <- l.CandidateCount + 1
            else
                let mutable weakest = LightSelection.power r g b * intensity * range

                for i in 0 .. LightSelection.Capacity - 1 do
                    let o = i * LightSelection.Stride
                    let s =
                        LightSelection.power (float l.CandidateColor.[o]) (float l.CandidateColor.[o + 1]) (float l.CandidateColor.[o + 2])
                        * float l.CandidatePosRange.[o + 3]
                    if s < weakest then
                        weakest <- s
                        slot <- i

            if slot >= 0 then
                let o = slot * LightSelection.Stride
                l.CandidatePosRange.[o] <- float32 x
                l.CandidatePosRange.[o + 1] <- float32 y
                l.CandidatePosRange.[o + 2] <- float32 z
                l.CandidatePosRange.[o + 3] <- float32 range
                l.CandidateColor.[o] <- float32 (r * intensity)
                l.CandidateColor.[o + 1] <- float32 (g * intensity)
                l.CandidateColor.[o + 2] <- float32 (b * intensity)
                l.CandidateColor.[o + 3] <- 0.0f

    /// Uebernimmt die nach LightSelection wichtigsten Kandidaten (hoechstens Budget) in die
    /// Shader-Puffer. Laeuft in beginFrame mit der Kameraposition.
    let select (l: Lights) (cx: float) (cy: float) (cz: float) =
        let n = LightSelection.selectInto l.CandidatePosRange l.CandidateColor l.CandidateCount cx cy cz l.Budget l.Scores l.Order

        for k in 0 .. n - 1 do
            let src = l.Order.[k] * LightSelection.Stride
            let dst = k * 4

            for c in 0 .. 3 do
                l.PointPosRange.[dst + c] <- l.CandidatePosRange.[src + c]
                l.PointColor.[dst + c] <- l.CandidateColor.[src + c]

        l.PointCount <- n

// -------------------------------------------------------------------------------------
//  Instanz-Batches
// -------------------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module InstanceBatch =

    /// Verwirft alle Instanzen des letzten Frames (Speicher bleibt erhalten).
    let inline clear (batch: InstanceBatch) =
        batch.Count <- 0
        batch.NeedsUpload <- true

    /// Schreibt eine Instanz aus reinen Skalaren - der allokationsfreie Pfad fuer
    /// Pools und Schleifen. Ist der Batch voll, wird die Instanz verworfen.
    let pushTRS
        (batch: InstanceBatch)
        (px: float)
        (py: float)
        (pz: float)
        (qx: float)
        (qy: float)
        (qz: float)
        (qw: float)
        (sx: float)
        (sy: float)
        (sz: float)
        (r: float)
        (g: float)
        (b: float)
        (emissive: float)
        =
        if batch.Count < batch.Capacity then
            let o = batch.Count * Layout.InstanceFloats
            let d = batch.Data
            Mat4.composeTRS d o px py pz qx qy qz qw sx sy sz
            d.[o + 16] <- float32 r
            d.[o + 17] <- float32 g
            d.[o + 18] <- float32 b
            d.[o + 19] <- float32 emissive
            batch.Count <- batch.Count + 1
            batch.NeedsUpload <- true

    /// Schreibt eine Instanz aus Vec3/Quat (bequem fuer Einzelobjekte wie Schiffe).
    let push
        (batch: InstanceBatch)
        (position: Vec3)
        (rotation: Quat)
        (scale: Vec3)
        (r: float)
        (g: float)
        (b: float)
        (emissive: float)
        =
        pushTRS
            batch
            position.X
            position.Y
            position.Z
            rotation.X
            rotation.Y
            rotation.Z
            rotation.W
            scale.X
            scale.Y
            scale.Z
            r
            g
            b
            emissive

    /// Schreibt eine Instanz mit fertiger Modellmatrix.
    let pushMatrix (batch: InstanceBatch) (model: Mat4) (r: float) (g: float) (b: float) (emissive: float) =
        if batch.Count < batch.Capacity then
            let o = batch.Count * Layout.InstanceFloats
            let d = batch.Data
            let m = model.Data

            for i in 0..15 do
                d.[o + i] <- m.[i]

            d.[o + 16] <- float32 r
            d.[o + 17] <- float32 g
            d.[o + 18] <- float32 b
            d.[o + 19] <- float32 emissive
            batch.Count <- batch.Count + 1
            batch.NeedsUpload <- true

// -------------------------------------------------------------------------------------
//  Renderer
// -------------------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Renderer =

    let private compileShader (gl: WebGL2RenderingContext) (kind: float) (label: string) (source: string) : WebGLShader =
        let shader = gl.createShader kind
        gl.shaderSource (shader, source)
        gl.compileShader shader

        if not (unbox<bool> (gl.getShaderParameter (shader, gl.COMPILE_STATUS))) then
            let log = gl.getShaderInfoLog shader
            gl.deleteShader shader
            failwith ("Shader-Kompilierung fehlgeschlagen (" + label + "):\n" + log)

        shader

    let linkProgram (gl: WebGL2RenderingContext) (vertexSource: string) (fragmentSource: string) : WebGLProgram =
        let vs = compileShader gl gl.VERTEX_SHADER "Vertex" vertexSource
        let fs = compileShader gl gl.FRAGMENT_SHADER "Fragment" fragmentSource
        let program = gl.createProgram ()
        gl.attachShader (program, vs)
        gl.attachShader (program, fs)
        gl.linkProgram program
        // Nach dem Linken werden die Shader-Objekte nicht mehr gebraucht.
        gl.deleteShader vs
        gl.deleteShader fs

        if not (unbox<bool> (gl.getProgramParameter (program, gl.LINK_STATUS))) then
            let log = gl.getProgramInfoLog program
            gl.deleteProgram program
            failwith ("Shader-Programm konnte nicht gelinkt werden:\n" + log)

        program

    let private createColorTarget (gl: WebGL2RenderingContext) =
        let tex = gl.createTexture ()
        gl.bindTexture (gl.TEXTURE_2D, tex)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
        gl.allocRgbaTexture (1, 1)
        let fbo = gl.createFramebuffer ()
        gl.bindFramebuffer (gl.FRAMEBUFFER, fbo)
        gl.framebufferTexture2D (gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0.0)
        fbo, tex

    let private createPostProcess (gl: WebGL2RenderingContext) : PostProcess =
        let vao = gl.createVertexArray ()
        let sceneFbo, sceneTex = createColorTarget gl
        let sceneDepth = gl.createRenderbuffer ()
        gl.bindRenderbuffer (gl.RENDERBUFFER, sceneDepth)
        gl.allocDepth24 (1, 1)
        gl.bindFramebuffer (gl.FRAMEBUFFER, sceneFbo)
        gl.framebufferRenderbuffer (gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, sceneDepth)
        let bloomA = Array.init Layout.MaxBloomLevels (fun _ -> createColorTarget gl)
        let bloomB = Array.init Layout.MaxBloomLevels (fun _ -> createColorTarget gl)
        // Nicht aktive Composite-Eingänge lesen transparentes Schwarz (WebGL null-init).
        let blackFbo, blackTex = createColorTarget gl
        gl.deleteFramebuffer blackFbo
        // Multisample-Ziel: Speicher folgt in resizePostProcess.
        let msaaFbo = gl.createFramebuffer ()
        let msaaColor = gl.createRenderbuffer ()
        let msaaDepth = gl.createRenderbuffer ()
        let samples = min 4 (gl.maxSamples ())
        gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
        gl.bindRenderbuffer (gl.RENDERBUFFER, unbox null)
        gl.bindTexture (gl.TEXTURE_2D, unbox null)

        let brightProg = linkProgram gl Shaders.postVertex Shaders.brightFragment
        let blurProg = linkProgram gl Shaders.postVertex Shaders.blurFragment
        let compProg = linkProgram gl Shaders.postVertex Shaders.compositeFragment
        { Vao = vao
          SceneFbo = sceneFbo
          SceneTex = sceneTex
          SceneDepth = sceneDepth
          MsaaFbo = msaaFbo
          MsaaColor = msaaColor
          MsaaDepth = msaaDepth
          MsaaSamples = samples
          Msaa = false
          MsaaAllowed = true
          BloomLevels = Layout.MaxBloomLevels
          BlackTex = blackTex
          NeedsResize = true
          BloomFboA = bloomA |> Array.map fst
          BloomTexA = bloomA |> Array.map snd
          BloomFboB = bloomB |> Array.map fst
          BloomTexB = bloomB |> Array.map snd
          BrightProg = brightProg
          UBrightScene = gl.getUniformLocation (brightProg, "uScene")
          UBrightHdr = gl.getUniformLocation (brightProg, "uHdr")
          BlurProg = blurProg
          UBlurTex = gl.getUniformLocation (blurProg, "uTex")
          UBlurDir = gl.getUniformLocation (blurProg, "uDir")
          CompositeProg = compProg
          UCompScene = gl.getUniformLocation (compProg, "uScene")
          UCompBloom = Array.init Layout.MaxBloomLevels (fun i -> gl.getUniformLocation (compProg, "uBloom" + string i))
          UCompBoost = gl.getUniformLocation (compProg, "uBoost")
          UCompHit = gl.getUniformLocation (compProg, "uHitFlash")
          UCompHdr = gl.getUniformLocation (compProg, "uHdr")
          SupportsHdr = not (isNull (gl.getExtension "EXT_color_buffer_float"))
          Hdr = false
          BoostEffect = 0.0
          HitEffect = 0.0
          BloomW = Array.create Layout.MaxBloomLevels 1
          BloomH = Array.create Layout.MaxBloomLevels 1
          Ready = false }

    let private resizePostProcess (gl: WebGL2RenderingContext) (post: PostProcess) (width: int) (height: int) =
        for level in 0 .. post.BloomLevels - 1 do
            post.BloomW.[level] <- max 1 (width / (4 <<< level))
            post.BloomH.[level] <- max 1 (height / (4 <<< level))
        gl.bindRenderbuffer (gl.RENDERBUFFER, post.SceneDepth)
        gl.allocDepth24 (width, height)
        let depthReady = gl.getError () = gl.NO_ERROR
        gl.bindRenderbuffer (gl.RENDERBUFFER, unbox null)
        let allocate hdr tex fbo w h =
            gl.bindTexture (gl.TEXTURE_2D, tex)
            if hdr then gl.allocHalfFloatTexture (w, h) else gl.allocRgbaTexture (w, h)
            gl.bindFramebuffer (gl.FRAMEBUFFER, fbo)
            gl.getError () = gl.NO_ERROR && gl.checkFramebufferStatus gl.FRAMEBUFFER = gl.FRAMEBUFFER_COMPLETE
        let allocateTargets hdr =
            let mutable ok = allocate hdr post.SceneTex post.SceneFbo width height
            for level in 0 .. post.BloomLevels - 1 do
                ok <- ok
                      && allocate hdr post.BloomTexA.[level] post.BloomFboA.[level] post.BloomW.[level] post.BloomH.[level]
                      && allocate hdr post.BloomTexB.[level] post.BloomFboB.[level] post.BloomW.[level] post.BloomH.[level]
            ok
        // An advertised format can still fail at this resolution. Commit a mode
        // only after all its attachments are usable, including on later resizes.
        post.Hdr <- depthReady && post.SupportsHdr && allocateTargets true
        post.Ready <- post.Hdr || (depthReady && allocateTargets false)

        // Echte Kantenglaettung: Die Szene entsteht in einem Multisample-Ziel und wird
        // vor dem Post-Processing nach SceneTex aufgeloest. Schlaegt die Anlage fehl,
        // zeichnet der Renderer wie bisher direkt nach SceneFbo.
        post.Msaa <- false
        if post.Ready && post.MsaaAllowed && post.MsaaSamples > 1 then
            gl.bindRenderbuffer (gl.RENDERBUFFER, post.MsaaColor)
            gl.allocMsaaColor (post.MsaaSamples, post.Hdr, width, height)
            gl.bindRenderbuffer (gl.RENDERBUFFER, post.MsaaDepth)
            gl.allocMsaaDepth (post.MsaaSamples, width, height)
            gl.bindRenderbuffer (gl.RENDERBUFFER, unbox null)
            gl.bindFramebuffer (gl.FRAMEBUFFER, post.MsaaFbo)
            gl.framebufferRenderbuffer (gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.RENDERBUFFER, post.MsaaColor)
            gl.framebufferRenderbuffer (gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, post.MsaaDepth)
            post.Msaa <- gl.getError () = gl.NO_ERROR && gl.checkFramebufferStatus gl.FRAMEBUFFER = gl.FRAMEBUFFER_COMPLETE
        gl.bindTexture (gl.TEXTURE_2D, unbox null)
        gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)

    /// Initialisiert WebGL2 auf dem Canvas, kompiliert die Shader und setzt den
    /// festen GL-Zustand. Wirft eine Exception mit Klartext, wenn WebGL2 fehlt.
    let create (canvas: HTMLCanvasElement) : Renderer =
        let attributes =
            JsInterop.createObj
                [ // Geglaettet wird im Offscreen-Ziel; der Canvas zeigt nur das fertige Bild.
                  "antialias", box false
                  "alpha", box false
                  "depth", box true
                  "stencil", box false
                  "powerPreference", box "high-performance" ]

        let context = canvas.getContext ("webgl2", attributes)

        if isNull context then
            failwith "WebGL2 wird von diesem Browser bzw. dieser Grafikkarte nicht unterstuetzt."

        let gl = unbox<WebGL2RenderingContext> context
        let program = linkProgram gl Shaders.vertex Shaders.fragment
        let uniform (name: string) = gl.getUniformLocation (program, name)

        gl.enable gl.DEPTH_TEST
        gl.depthFunc gl.LEQUAL
        gl.enable gl.CULL_FACE
        gl.cullFace gl.BACK
        gl.frontFace gl.CCW
        gl.disable gl.BLEND

        // Schattenkarte: eine Tiefentextur, die der Hauptshader ueber sampler2DShadow liest.
        let shadowProgram = linkProgram gl Shaders.shadowVertex Shaders.shadowFragment
        let shadowTex = gl.createTexture ()
        gl.activeTexture (gl.TEXTURE0 + float Layout.ShadowUnit)
        gl.bindTexture (gl.TEXTURE_2D, shadowTex)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE)
        gl.texParameteri (gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE)
        let initial = GraphicsQuality.preset GraphicsQuality.High
        gl.allocShadowTexture initial.ShadowSize
        let shadowFbo = gl.createFramebuffer ()
        gl.bindFramebuffer (gl.FRAMEBUFFER, shadowFbo)
        gl.framebufferTexture2D (gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.TEXTURE_2D, shadowTex, 0.0)
        gl.depthOnlyTarget ()
        let shadowSupported = gl.checkFramebufferStatus gl.FRAMEBUFFER = gl.FRAMEBUFFER_COMPLETE
        gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
        gl.activeTexture gl.TEXTURE0
        // Der Sampler zeigt dauerhaft auf die Einheit der Schattenkarte.
        gl.useProgram program
        gl.uniform1i (uniform "uShadowMap", float Layout.ShadowUnit)

        let clearColor = JS.Constructors.Float32Array.Create 3
        clearColor.[0] <- 0.004f
        clearColor.[1] <- 0.006f
        clearColor.[2] <- 0.016f
        let post = createPostProcess gl

        { Canvas = canvas
          Gl = gl
          Program = program
          UViewProj = uniform "uViewProj"
          UCameraPos = uniform "uCameraPos"
          UVeil = uniform "uVeil"
          VeilDensity = 0.
          USunDir = uniform "uSunDir"
          USunColor = uniform "uSunColor"
          UAmbient = uniform "uAmbient"
          UPointCount = uniform "uPointCount"
          UPointPosRange = uniform "uPointPosRange[0]"
          UPointColor = uniform "uPointColor[0]"
          UShininess = uniform "uShininess"
          USpecular = uniform "uSpecular"
          UMaterial = uniform "uMaterial"
          USkySh = uniform "uSkySh[0]"
          UHdr = uniform "uHdr"
          UShadowMatrix = uniform "uShadowMatrix"
          UShadowStrength = uniform "uShadowStrength"
          UShadowOffset = uniform "uShadowOffset"
          ShadowProgram = shadowProgram
          UShadowLight = gl.getUniformLocation (shadowProgram, "uLight")
          ShadowFbo = shadowFbo
          ShadowTex = shadowTex
          ShadowMatrix = Mat4.create ()
          ShadowSupported = shadowSupported
          ShadowSize = initial.ShadowSize
          ShadowsOn = shadowSupported
          Lights = Lights.create ()
          ViewProj = Mat4.create ()
          SkyView = Mat4.create ()
          SkyViewProj = Mat4.create ()
          CameraPos = JS.Constructors.Float32Array.Create 3
          ClearColor = clearColor
          Width = 0
          Height = 0
          PixelRatio = 1.0
          RenderScale = initial.RenderScale
          DprLimit = initial.DprLimit
          MaxTargetSize = min (unbox<float> (gl.getParameter gl.MAX_TEXTURE_SIZE)) (unbox<float> (gl.getParameter gl.MAX_RENDERBUFFER_SIZE))
          BlendState = 0
          ViewProjState = 0
          DrawCalls = 0
          InstancesDrawn = 0
          Post = post }

    /// Laedt Geometrie in statische GPU-Buffer. Nur beim Setup aufrufen.
    let createMesh (renderer: Renderer) (data: MeshData) : Mesh =
        let gl = renderer.Gl
        // Kein VAO gebunden lassen, damit der ELEMENT_ARRAY_BUFFER-Bind unten
        // nicht versehentlich den Zustand eines fremden VAOs veraendert.
        gl.bindVertexArray (unbox null)

        let vbo = gl.createBuffer ()
        gl.bindBuffer (gl.ARRAY_BUFFER, vbo)
        gl.bufferData (gl.ARRAY_BUFFER, U3.Case2(unbox<JS.ArrayBufferView> data.Vertices), gl.STATIC_DRAW)

        let ibo = gl.createBuffer ()
        gl.bindBuffer (gl.ELEMENT_ARRAY_BUFFER, ibo)
        gl.bufferData (gl.ELEMENT_ARRAY_BUFFER, U3.Case2(unbox<JS.ArrayBufferView> data.Indices), gl.STATIC_DRAW)

        { VertexBuffer = vbo
          IndexBuffer = ibo
          IndexCount = data.Indices.length }

    /// Legt einen Batch fuer bis zu `capacity` Instanzen eines Meshes an
    /// (VAO + dynamischer Instanz-Buffer). Nur beim Setup aufrufen.
    let createBatch (renderer: Renderer) (mesh: Mesh) (capacity: int) (blend: BlendMode) : InstanceBatch =
        let gl = renderer.Gl
        let floatBytes = 4
        let vertexStride = float (Layout.VertexFloats * floatBytes)
        let instanceStride = float (Layout.InstanceFloats * floatBytes)

        let vao = gl.createVertexArray ()
        gl.bindVertexArray vao

        // Pro-Vertex-Attribute aus dem Mesh
        gl.bindBuffer (gl.ARRAY_BUFFER, mesh.VertexBuffer)
        gl.enableVertexAttribArray 0.0
        gl.vertexAttribPointer (0.0, 3.0, gl.FLOAT, false, vertexStride, 0.0)
        gl.enableVertexAttribArray 1.0
        gl.vertexAttribPointer (1.0, 3.0, gl.FLOAT, false, vertexStride, float (3 * floatBytes))
        gl.enableVertexAttribArray 2.0

        gl.vertexAttribPointer (
            2.0,
            4.0,
            gl.FLOAT,
            false,
            vertexStride,
            float (Layout.VertexColorOffset * floatBytes)
        )
        // Der Index-Buffer gehoert zum VAO-Zustand.
        gl.bindBuffer (gl.ELEMENT_ARRAY_BUFFER, mesh.IndexBuffer)

        // Pro-Instanz-Attribute: eine mat4 belegt vier aufeinanderfolgende Locations.
        let instanceBuffer = gl.createBuffer ()
        gl.bindBuffer (gl.ARRAY_BUFFER, instanceBuffer)
        gl.bufferData (gl.ARRAY_BUFFER, U3.Case1(float capacity * instanceStride), gl.DYNAMIC_DRAW)

        for column in 0..3 do
            let location = float (3 + column)
            gl.enableVertexAttribArray location
            gl.vertexAttribPointer (location, 4.0, gl.FLOAT, false, instanceStride, float (column * 4 * floatBytes))
            gl.vertexAttribDivisor (location, 1.0)

        gl.enableVertexAttribArray 7.0

        gl.vertexAttribPointer (
            7.0,
            4.0,
            gl.FLOAT,
            false,
            instanceStride,
            float (Layout.InstanceColorOffset * floatBytes)
        )

        gl.vertexAttribDivisor (7.0, 1.0)

        gl.bindVertexArray (unbox null)
        gl.bindBuffer (gl.ARRAY_BUFFER, unbox null)

        { Mesh = mesh
          Vao = vao
          InstanceBuffer = instanceBuffer
          Data = JS.Constructors.Float32Array.Create(capacity * Layout.InstanceFloats)
          Capacity = capacity
          Blend = blend
          Count = 0
          NeedsUpload = true
          CameraRelative = false
          Shininess = 48.0
          Specular = 0.5
          Material = 0.0 }

    /// Gleicht den Drawing Buffer an CSS-Groesse, Gerät und aktuelle Renderskala an.
    /// Liefert true, wenn sich die Groesse geaendert hat. Pro Frame aufrufbar.
    let resize (renderer: Renderer) : bool =
        let cssWidth = renderer.Canvas.clientWidth
        let cssHeight = renderer.Canvas.clientHeight
        let targetRatio = min (max 1.0 window.devicePixelRatio) renderer.DprLimit * renderer.RenderScale
        let ratio = min targetRatio (renderer.MaxTargetSize / max 1.0 (max cssWidth cssHeight))
        let width = max 1 (int (cssWidth * ratio))
        let height = max 1 (int (cssHeight * ratio))
        renderer.PixelRatio <- ratio

        let resized = width <> renderer.Width || height <> renderer.Height
        if resized then
            renderer.Canvas.width <- float width
            renderer.Canvas.height <- float height
            renderer.Width <- width
            renderer.Height <- height
        if resized || renderer.Post.NeedsResize then
            resizePostProcess renderer.Gl renderer.Post width height
            renderer.Post.NeedsResize <- false
        resized

    /// Seitenverhaeltnis des Drawing Buffers.
    let inline aspect (renderer: Renderer) =
        float renderer.Width / float renderer.Height

    /// Beginnt einen Frame: Viewport, Clear, Kamera- und Licht-Uniforms.
    let beginFrame (renderer: Renderer) (view: Mat4) (projection: Mat4) (cameraPosition: Vec3) =
        let gl = renderer.Gl
        let lights = renderer.Lights
        renderer.DrawCalls <- 0
        renderer.InstancesDrawn <- 0

        if renderer.Post.Ready then
            gl.bindFramebuffer (gl.FRAMEBUFFER, (if renderer.Post.Msaa then renderer.Post.MsaaFbo else renderer.Post.SceneFbo))
        else
            gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)

        // Deckenden Zustand herstellen - depthMask muss vor dem Clear aktiv sein,
        // sonst wird der Tiefenpuffer nicht geloescht.
        gl.disable gl.BLEND
        gl.depthMask true
        renderer.BlendState <- 0

        gl.viewport (0.0, 0.0, float renderer.Width, float renderer.Height)

        gl.clearColor (
            float renderer.ClearColor.[0],
            float renderer.ClearColor.[1],
            float renderer.ClearColor.[2],
            1.0
        )

        gl.clear (gl.COLOR_BUFFER_BIT + gl.DEPTH_BUFFER_BIT)

        Mat4.multiply renderer.ViewProj projection view
        Vec3.writeTo renderer.CameraPos 0 cameraPosition

        // Kamerarelative Variante: dieselbe Blickrichtung, aber ohne Translation.
        Mat4.copy renderer.SkyView view
        let skyView = renderer.SkyView.Data
        skyView.[12] <- 0.0f
        skyView.[13] <- 0.0f
        skyView.[14] <- 0.0f
        Mat4.multiply renderer.SkyViewProj projection renderer.SkyView

        gl.useProgram renderer.Program
        gl.uniform1i (renderer.UHdr, if renderer.Post.Hdr then 1.0 else 0.0)
        gl.uniformMatrix4fv (renderer.UViewProj, false, renderer.ViewProj.Data)
        renderer.ViewProjState <- 0
        gl.uniform3fv (renderer.UCameraPos, renderer.CameraPos)
        gl.uniform1f (renderer.UVeil, renderer.VeilDensity)
        gl.uniform3fv (renderer.USunDir, lights.SunDirection)
        gl.uniform3fv (renderer.USunColor, lights.SunColor)
        gl.uniform3fv (renderer.UAmbient, lights.Ambient)
        gl.uniform3fv (renderer.USkySh, lights.SkySh)
        // Die wichtigsten gemeldeten Punktlichter aus Sicht dieser Kamera.
        Lights.select lights cameraPosition.X cameraPosition.Y cameraPosition.Z
        gl.uniform1i (renderer.UPointCount, float lights.PointCount)
        gl.uniform4fv (renderer.UPointPosRange, lights.PointPosRange)
        gl.uniform4fv (renderer.UPointColor, lights.PointColor)

    /// Laedt geaenderte Instanzdaten hoch und setzt den Draw-Call ab (Haupt- und Schatten-Pass).
    let private drawInstances (renderer: Renderer) (batch: InstanceBatch) =
        let gl = renderer.Gl
        gl.bindVertexArray batch.Vao

        if batch.NeedsUpload then
            gl.bindBuffer (gl.ARRAY_BUFFER, batch.InstanceBuffer)

            // Nur den tatsaechlich benutzten Teil des Instanz-Buffers hochladen.
            gl.bufferSubDataRange (
                gl.ARRAY_BUFFER,
                0.0,
                batch.Data,
                0.0,
                float (batch.Count * Layout.InstanceFloats)
            )

            batch.NeedsUpload <- false

        gl.drawElementsInstanced (
            gl.TRIANGLES,
            float batch.Mesh.IndexCount,
            gl.UNSIGNED_SHORT,
            0.0,
            float batch.Count
        )

        renderer.DrawCalls <- renderer.DrawCalls + 1

    /// Zeichnet alle Instanzen eines Batches in EINEM Draw-Call.
    /// Reihenfolge im Frame: erst alle Opaque-Batches, dann die Additive-Batches.
    let draw (renderer: Renderer) (batch: InstanceBatch) =
        if batch.Count > 0 then
            let gl = renderer.Gl

            match batch.Blend with
            | Opaque ->
                if renderer.BlendState <> 0 then
                    gl.disable gl.BLEND
                    gl.depthMask true
                    renderer.BlendState <- 0
            | Additive ->
                if renderer.BlendState <> 1 then
                    gl.enable gl.BLEND
                    gl.blendFunc (gl.ONE, gl.ONE)
                    gl.depthMask false
                    renderer.BlendState <- 1
            | Alpha ->
                if renderer.BlendState <> 2 then
                    gl.enable gl.BLEND
                    gl.blendFunc (gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA)
                    gl.depthMask false
                    renderer.BlendState <- 2

            if batch.CameraRelative then
                if renderer.ViewProjState <> 1 then
                    gl.uniformMatrix4fv (renderer.UViewProj, false, renderer.SkyViewProj.Data)
                    renderer.ViewProjState <- 1
            elif renderer.ViewProjState <> 0 then
                gl.uniformMatrix4fv (renderer.UViewProj, false, renderer.ViewProj.Data)
                renderer.ViewProjState <- 0

            gl.uniform1f (renderer.UShininess, batch.Shininess)
            gl.uniform1f (renderer.USpecular, batch.Specular)
            gl.uniform1f (renderer.UMaterial, batch.Material)

            drawInstances renderer batch
            renderer.InstancesDrawn <- renderer.InstancesDrawn + batch.Count

    /// Beginnt den Schatten-Pass: richtet die Sonnenmatrix auf `center` aus und bindet
    /// die Tiefenkarte. `radius` ist die halbe Kantenlaenge des beschatteten Bereichs:
    /// klein = scharfe Schatten am Schiff, gross = auch ein nahes Bauwerk liegt darin.
    /// Zwischen beginFrame und den Draw-Calls der Szene aufrufen, danach drawShadow
    /// fuer jeden Schattenwerfer und zum Schluss endShadows.
    let beginShadows (renderer: Renderer) (center: Vec3) (radius: float) =
        let gl = renderer.Gl
        let sun = renderer.Lights.SunDirection
        let lx = float sun.[0]
        let ly = float sun.[1]
        let lz = float sun.[2]
        // Rechts = Hilfsachse x Sonne, Oben = Sonne x Rechts: Rechts x Oben zeigt zur
        // Sonne, die Windungsrichtung der Dreiecke bleibt also erhalten.
        let ax, ay, az = if abs ly > 0.95 then 1.0, 0.0, 0.0 else 0.0, 1.0, 0.0
        let mutable rx = ay * lz - az * ly
        let mutable ry = az * lx - ax * lz
        let mutable rz = ax * ly - ay * lx
        let inv = 1.0 / sqrt (rx * rx + ry * ry + rz * rz)
        rx <- rx * inv
        ry <- ry * inv
        rz <- rz * inv
        let ux = ly * rz - lz * ry
        let uy = lz * rx - lx * rz
        let uz = lx * ry - ly * rx
        let depth = Layout.ShadowDepth
        // Mittelpunkt auf das Texelraster legen, sonst flimmern die Schattenkanten im Flug.
        let texel = 2.0 * radius / float renderer.ShadowSize
        let snap (v: float) = floor (v / texel) * texel
        let cr = snap (rx * center.X + ry * center.Y + rz * center.Z)
        let cu = snap (ux * center.X + uy * center.Y + uz * center.Z)
        let cl = lx * center.X + ly * center.Y + lz * center.Z
        let m = renderer.ShadowMatrix.Data
        m.[0] <- float32 (rx / radius)
        m.[4] <- float32 (ry / radius)
        m.[8] <- float32 (rz / radius)
        m.[12] <- float32 (-cr / radius)
        m.[1] <- float32 (ux / radius)
        m.[5] <- float32 (uy / radius)
        m.[9] <- float32 (uz / radius)
        m.[13] <- float32 (-cu / radius)
        // Tiefe waechst von der Sonne weg.
        m.[2] <- float32 (-lx / depth)
        m.[6] <- float32 (-ly / depth)
        m.[10] <- float32 (-lz / depth)
        m.[14] <- float32 (cl / depth)
        m.[3] <- 0.0f
        m.[7] <- 0.0f
        m.[11] <- 0.0f
        m.[15] <- 1.0f
        // Der Hauptshader ist hier noch aktiv (beginFrame).
        gl.uniform1f (renderer.UShadowOffset, System.Math.Max(0.10, 1.3 * texel))

        if renderer.ShadowsOn then
            gl.bindFramebuffer (gl.FRAMEBUFFER, renderer.ShadowFbo)
            gl.viewport (0.0, 0.0, float renderer.ShadowSize, float renderer.ShadowSize)
            gl.disable gl.BLEND
            gl.depthMask true
            renderer.BlendState <- 0
            gl.clear gl.DEPTH_BUFFER_BIT
            gl.useProgram renderer.ShadowProgram
            gl.uniformMatrix4fv (renderer.UShadowLight, false, m)
            gl.enableDepthBias (2.0, 4.0)

    /// Zeichnet einen deckenden Batch in die Schattenkarte.
    let drawShadow (renderer: Renderer) (batch: InstanceBatch) =
        if renderer.ShadowsOn && batch.Count > 0 && not batch.CameraRelative then
            match batch.Blend with
            | Opaque -> drawInstances renderer batch
            | _ -> ()

    /// Beendet den Schatten-Pass und stellt Ziel, Viewport und Hauptshader wieder her.
    let endShadows (renderer: Renderer) =
        let gl = renderer.Gl

        if renderer.ShadowsOn then
            gl.disableDepthBias ()
            let post = renderer.Post

            if post.Ready then
                gl.bindFramebuffer (gl.FRAMEBUFFER, (if post.Msaa then post.MsaaFbo else post.SceneFbo))
            else
                gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)

            gl.viewport (0.0, 0.0, float renderer.Width, float renderer.Height)
            gl.useProgram renderer.Program

        gl.uniformMatrix4fv (renderer.UShadowMatrix, false, renderer.ShadowMatrix.Data)
        gl.uniform1f (renderer.UShadowStrength, (if renderer.ShadowsOn then 1.0 else 0.0))
        gl.activeTexture (gl.TEXTURE0 + float Layout.ShadowUnit)
        gl.bindTexture (gl.TEXTURE_2D, renderer.ShadowTex)
        gl.activeTexture gl.TEXTURE0

    let private setShadows (renderer: Renderer) enabled size =
        if renderer.ShadowsOn <> enabled || renderer.ShadowSize <> size then
            let gl = renderer.Gl
            gl.activeTexture (gl.TEXTURE0 + float Layout.ShadowUnit)
            gl.bindTexture (gl.TEXTURE_2D, renderer.ShadowTex)
            gl.bindFramebuffer (gl.FRAMEBUFFER, renderer.ShadowFbo)
            // texImage2D ersetzt den alten Speicher. Auch ohne Schatten braucht
            // sampler2DShadow eine vollständige Tiefentextur.
            gl.allocShadowTexture (if enabled then size else 1)
            let ready = gl.getError () = gl.NO_ERROR && gl.checkFramebufferStatus gl.FRAMEBUFFER = gl.FRAMEBUFFER_COMPLETE
            renderer.ShadowSupported <- ready
            renderer.ShadowsOn <- enabled && ready
            renderer.ShadowSize <- size
            if not ready then gl.allocShadowTexture 1
            gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
            gl.activeTexture gl.TEXTURE0

    let toggleShadows (renderer: Renderer) : bool =
        setShadows renderer (not renderer.ShadowsOn) renderer.ShadowSize
        renderer.ShadowsOn

    let applyQuality (renderer: Renderer) (preset: GraphicsQuality.Preset) scale =
        let gl = renderer.Gl
        let post = renderer.Post
        renderer.RenderScale <- max 0.5 (min scale preset.RenderScale)
        renderer.DprLimit <- preset.DprLimit
        setShadows renderer preset.Shadows (min preset.ShadowSize (int renderer.MaxTargetSize))
        if preset.Msaa <> post.MsaaAllowed then
            post.MsaaAllowed <- preset.Msaa
            if preset.Msaa then
                post.MsaaFbo <- gl.createFramebuffer ()
                post.MsaaColor <- gl.createRenderbuffer ()
                post.MsaaDepth <- gl.createRenderbuffer ()
            else
                gl.deleteFramebuffer post.MsaaFbo
                gl.deleteRenderbuffer post.MsaaColor
                gl.deleteRenderbuffer post.MsaaDepth
                post.MsaaFbo <- unbox null
                post.MsaaColor <- unbox null
                post.MsaaDepth <- unbox null
                post.Msaa <- false
            post.NeedsResize <- true
        if preset.BloomLevels <> post.BloomLevels then
            let setLevels (fbos: WebGLFramebuffer[]) (textures: WebGLTexture[]) =
                for i in preset.BloomLevels .. post.BloomLevels - 1 do
                    gl.deleteFramebuffer fbos.[i]
                    gl.deleteTexture textures.[i]
                    fbos.[i] <- unbox null
                    textures.[i] <- unbox null
                for i in post.BloomLevels .. preset.BloomLevels - 1 do
                    let fbo, tex = createColorTarget gl
                    fbos.[i] <- fbo
                    textures.[i] <- tex
            setLevels post.BloomFboA post.BloomTexA
            setLevels post.BloomFboB post.BloomTexB
            gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
            gl.bindTexture (gl.TEXTURE_2D, unbox null)
            post.BloomLevels <- preset.BloomLevels
            post.NeedsResize <- true

    /// Schliesst den Frame ab: MSAA aufloesen, Bright-Pass, dreistufige Bloom-Kette
    /// und Composite-Pass auf den Default-Framebuffer.
    let endFrame (renderer: Renderer) =
        let post = renderer.Post
        if post.Ready then
            let gl = renderer.Gl
            if post.Msaa then
                gl.resolveColor (post.MsaaFbo, post.SceneFbo, renderer.Width, renderer.Height)
            gl.disable gl.DEPTH_TEST
            gl.disable gl.CULL_FACE
            gl.disable gl.BLEND
            gl.depthMask false
            gl.bindVertexArray post.Vao
            gl.activeTexture gl.TEXTURE0

            // 1) Bright-Pass + 1/4-Downsample in die erste Stufe
            gl.bindFramebuffer (gl.FRAMEBUFFER, post.BloomFboA.[0])
            gl.viewport (0.0, 0.0, float post.BloomW.[0], float post.BloomH.[0])
            gl.useProgram post.BrightProg
            gl.bindTexture (gl.TEXTURE_2D, post.SceneTex)
            gl.uniform1i (post.UBrightScene, 0.0)
            gl.uniform1i (post.UBrightHdr, if post.Hdr then 1.0 else 0.0)
            gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)

            // 2) Pro Stufe ein separierter Gauss. Der horizontale Durchgang liest ab der
            //    zweiten Stufe die fertige groessere Stufe und halbiert sie dabei.
            gl.useProgram post.BlurProg
            gl.uniform1i (post.UBlurTex, 0.0)
            for level in 0 .. post.BloomLevels - 1 do
                let dx = 1.0 / float post.BloomW.[level]
                let dy = 1.0 / float post.BloomH.[level]
                gl.viewport (0.0, 0.0, float post.BloomW.[level], float post.BloomH.[level])
                // Die erste Stufe traegt den harten Schwellwert des Bright-Pass und wird
                // zweimal geglaettet, damit duenne helle Kanten nicht als Bloecke durchschlagen.
                for round in 0 .. (if level = 0 then 1 else 0) do
                    gl.bindFramebuffer (gl.FRAMEBUFFER, post.BloomFboB.[level])
                    gl.bindTexture (gl.TEXTURE_2D, post.BloomTexA.[(if round = 0 then max 0 (level - 1) else level)])
                    gl.uniform2f (post.UBlurDir, dx, 0.0)
                    gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)

                    gl.bindFramebuffer (gl.FRAMEBUFFER, post.BloomFboA.[level])
                    gl.bindTexture (gl.TEXTURE_2D, post.BloomTexB.[level])
                    gl.uniform2f (post.UBlurDir, 0.0, dy)
                    gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)

            // 3) Composite auf den Default-Framebuffer
            gl.bindFramebuffer (gl.FRAMEBUFFER, unbox null)
            gl.viewport (0.0, 0.0, float renderer.Width, float renderer.Height)
            gl.useProgram post.CompositeProg
            gl.bindTexture (gl.TEXTURE_2D, post.SceneTex)
            gl.uniform1i (post.UCompScene, 0.0)
            for level in 0 .. Layout.MaxBloomLevels - 1 do
                gl.activeTexture (gl.TEXTURE1 + float level)
                gl.bindTexture (gl.TEXTURE_2D, (if level < post.BloomLevels then post.BloomTexA.[level] else post.BlackTex))
                gl.uniform1i (post.UCompBloom.[level], float (1 + level))
            gl.uniform1i (post.UCompHdr, if post.Hdr then 1.0 else 0.0)
            gl.uniform1f (post.UCompBoost, post.BoostEffect)
            gl.uniform1f (post.UCompHit, post.HitEffect)
            gl.drawArrays (gl.TRIANGLES, 0.0, 3.0)

            for level in 0 .. Layout.MaxBloomLevels - 1 do
                gl.activeTexture (gl.TEXTURE1 + float level)
                gl.bindTexture (gl.TEXTURE_2D, unbox null)
            gl.activeTexture gl.TEXTURE0
            gl.bindTexture (gl.TEXTURE_2D, unbox null)
            gl.bindVertexArray (unbox null)
            gl.enable gl.DEPTH_TEST
            gl.enable gl.CULL_FACE
            gl.depthMask true
            gl.useProgram renderer.Program
            renderer.BlendState <- 0
            renderer.DrawCalls <- renderer.DrawCalls + 4 + 2 * post.BloomLevels

    /// Setzt dynamische Post-Processing-Effekte (Boost-Aberration + Treffer-Randimpuls).
    let setEffects (renderer: Renderer) (boost: float) (hitFlash: float) =
        renderer.Post.BoostEffect <- max 0.0 (min 1.0 boost)
        renderer.Post.HitEffect <- max 0.0 (min 1.0 hitFlash)
