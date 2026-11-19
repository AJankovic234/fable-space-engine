namespace SpaceEngine

// =====================================================================================
//  GpuParticles.fs  -  GPU-Partikelsystem: die CPU legt Partikel nur an
//
//  Jeder Partikel wird genau einmal geschrieben: Startpunkt, Startgeschwindigkeit,
//  Beschleunigung und Drag, Geburtszeit, Lebensdauer, Groesse und Farbe am Anfang
//  und am Ende, Form. Bewegung, Alterung und Ausblenden rechnet der Vertex-Shader
//  aus der Uhrzeit - pro Frame laeuft auf der CPU keine Partikelschleife.
//
//  Ringpuffer
//    Die Partikel liegen in einem Ring fester Kapazitaet (CPU-Spiegel + VBO).
//    Lebende Partikel bilden einen zusammenhaengenden Bogen [Tail, Tail + Count).
//    Neue Partikel haengen am Kopf an; der Bogen ist hoechstens `Budget` lang -
//    darueber weicht der aelteste. Abgelaufene Partikel geben das Bogenende frei,
//    sobald sie dort angekommen sind. Gezeichnet wird nur der Bogen (ein bis zwei
//    Instanz-Draws), hochgeladen werden nur die in diesem Frame neuen Plaetze.
//
//  Zeit
//    Geburtszeiten sind Weltzeit (World.Time). Auf der GPU liegt die Zeit relativ zu
//    einer Epoche, damit float32 auch nach Stunden Spielzeit genau bleibt; alle
//    17 Minuten wird die Epoche nachgezogen und der Bogen einmal neu hochgeladen.
//
//  Zeichnen: additiv, Tiefentest an, Tiefenschreiben aus, ins HDR-Szenenziel
//  zwischen Renderer.beginFrame und Renderer.endFrame. Danach steht der GL-Zustand
//  wieder so, wie ihn der Zustandscache des Renderers erwartet (deckend, Hauptshader).
//
//  Ringverwaltung (ParticleRing) und Emissions-Timing (Emission) sind reine Funktionen.
// =====================================================================================

open System
open Fable.Core
open Browser.Types

/// Aussehen und Verhalten einer Partikelsorte (reine Daten, Rezepte in ParticleRecipes).
type ParticleLook =
    { /// ParticleShape.Glow, Spark oder Smoke.
      Shape: float
      /// Mittlere Lebensdauer in Sekunden.
      Life: float
      /// Streuung der Lebensdauer als Anteil (0,3 = +-30 %).
      LifeJitter: float
      /// Abbremsung (1/s); 0 = ungebremst.
      Drag: float
      /// Groesse (Radius in Metern) am Anfang und am Ende.
      Size0: float
      Size1: float
      /// Farbe am Anfang und am Ende (HDR, linear; wird additiv aufgetragen).
      R0: float
      G0: float
      B0: float
      R1: float
      G1: float
      B1: float
      /// Funken: Schweiflaenge als Flugweg dieser Anzahl Sekunden.
      Stretch: float
      /// Einblenden als Anteil der Lebensdauer (0 = sofort voll).
      FadeIn: float
      /// Ausblenden: Helligkeit * (1 - t)^FadeOut.
      FadeOut: float }

[<RequireQualifiedAccess>]
module ParticleShape =
    /// Zur Kamera gedrehter Leuchtpunkt mit heissem Kern.
    [<Literal>]
    let Glow = 0.0

    /// Nach der Geschwindigkeit gestreckter Funke (Bewegungsunschaerfe).
    [<Literal>]
    let Spark = 1.0

    /// Weiche, aufquellende Wolke.
    [<Literal>]
    let Smoke = 2.0

/// Ringverwaltung als reine Funktionen auf Ganzzahlen (keine Allokation).
[<RequireQualifiedAccess>]
module ParticleRing =

    /// Platz nach `index` im Ring.
    let inline next (index: int) (capacity: int) = if index + 1 >= capacity then 0 else index + 1

    /// Platz `steps` hinter `index` im Ring.
    let inline advance (index: int) (steps: int) (capacity: int) = (index + steps) % capacity

    /// Wie viele der aeltesten Partikel weichen muessen, damit ein neuer ins Budget passt.
    let dropFor (count: int) (budget: int) : int = max 0 (count - max 1 budget + 1)

    /// Wie viele Partikel am Bogenende (ab `tail`) bis `now` abgelaufen sind. Haelt am
    /// ersten noch lebenden an: Der Bogen bleibt zusammenhaengend, schlimmstenfalls
    /// zeichnet er einige schon erloschene Partikel mit (der Shader verwirft sie).
    let expired (death: JS.Float64Array) (tail: int) (count: int) (capacity: int) (now: float) : int =
        let mutable n = 0
        let mutable slot = tail

        while n < count && death.[slot] <= now do
            n <- n + 1
            slot <- next slot capacity

        n

    /// Laenge des ersten zusammenhaengenden Stuecks von [start, start + count) im Ring.
    /// Der Rest (count - firstSpan) beginnt bei Platz 0.
    let firstSpan (start: int) (count: int) (capacity: int) : int = min (min count capacity) (capacity - start)

    /// Laenge des zweiten Stuecks (ab Platz 0), 0 wenn der Bogen nicht umlaeuft.
    let secondSpan (start: int) (count: int) (capacity: int) : int = min count capacity - firstSpan start count capacity

/// Emissions-Timing als reine Funktionen. Ein Strahler sammelt Bruchteile (carry) ueber
/// die Frames an; faellige Partikel werden auf den Frame verteilt, damit Spuren auch bei
/// hoher Fahrt und niedriger Bildrate gleichmaessige Abstaende behalten.
[<RequireQualifiedAccess>]
module Emission =

    /// Partikel, die bei `rate` pro Sekunde in diesem Frame faellig sind.
    let due (carry: float) (rate: float) (dt: float) : int = int (floor (carry + max 0.0 (rate * dt)))

    /// Bruchteil, der in den naechsten Frame uebernommen wird (0 <= Rest < 1).
    let remainder (carry: float) (rate: float) (dt: float) : float =
        let total = carry + max 0.0 (rate * dt)
        total - floor total

    /// Zeitpunkt des Partikels `j` (ab 0) als Anteil des Frames in (0, 1]: 0 = letzter
    /// Frame, 1 = jetzt. Der Ort folgt daraus durch Interpolation der Duesenposition.
    let moment (carry: float) (rate: float) (dt: float) (j: int) : float =
        let step = max 0.0 (rate * dt)
        if step <= 1e-9 then 1.0 else max 0.0 (min 1.0 ((float (j + 1) - carry) / step))

    /// Rate fuer einen Abstand `spacing` (Meter) entlang der Bahn, mindestens `minimum`.
    let spaced (speed: float) (spacing: float) (minimum: float) : float = max minimum (abs speed / max 1e-3 spacing)

    /// Partikelzahl eines Ausstosses nach Detailgrad; 0 bleibt 0, sonst mindestens 1.
    let scaled (count: float) (factor: float) : int =
        if count <= 0.0 || factor <= 0.0 then 0 else max 1 (int (count * factor + 0.5))

    /// Drosselung kosmetischer Partikel nach Fuellstand des Budgets (wie CombatVfx.throttle).
    let throttle (live: int) (budget: int) : float = CombatVfx.throttle live budget

/// GL-Seite des Systems; fehlt im reinen CPU-Betrieb (Tests, fehlgeschlagener Shader).
type GpuParticleGl =
    { Program: WebGLProgram
      Vao: WebGLVertexArrayObject
      Buffer: WebGLBuffer
      UViewProj: WebGLUniformLocation
      UCamPos: WebGLUniformLocation
      UCamRight: WebGLUniformLocation
      UCamUp: WebGLUniformLocation
      UTime: WebGLUniformLocation
      UPixel: WebGLUniformLocation
      UMaxSize: WebGLUniformLocation
      UHdr: WebGLUniformLocation
      DustProgram: WebGLProgram
      DustVao: WebGLVertexArrayObject
      UDustViewProj: WebGLUniformLocation
      UDustCamPos: WebGLUniformLocation
      UDustCamRight: WebGLUniformLocation
      UDustCamUp: WebGLUniformLocation
      UDustVelocity: WebGLUniformLocation
      UDustSun: WebGLUniformLocation
      UDustColor: WebGLUniformLocation
      UDustCell: WebGLUniformLocation
      UDustAmount: WebGLUniformLocation
      UDustTime: WebGLUniformLocation
      UDustPixel: WebGLUniformLocation
      UDustStretch: WebGLUniformLocation
      UDustHdr: WebGLUniformLocation }

type GpuParticleSystem =
    { Capacity: int
      /// CPU-Spiegel des Rings: Capacity * GpuParticles.Stride Floats.
      Data: JS.Float32Array
      /// Todeszeit (Weltzeit) je Platz, fuer das Freigeben des Bogenendes.
      Death: JS.Float64Array
      /// Eigener Zufall: Kosmetik veraendert die Zufallsfolge der Spielwelt nicht.
      Rng: Rng
      /// Hoechstzahl gleichzeitig lebender Partikel (Qualitaetsstufe).
      mutable Budget: int
      mutable Head: int
      mutable Tail: int
      mutable Count: int
      /// Seit dem letzten Hochladen geschriebene Plaetze (ab DirtyStart).
      mutable DirtyStart: int
      mutable DirtyCount: int
      /// Nach einem Epochenwechsel muss der ganze Bogen neu hoch.
      mutable AllDirty: bool
      /// Weltzeit des aktuellen Bildes.
      mutable Clock: float
      mutable Epoch: float
      /// Statistik: insgesamt angelegte Partikel.
      mutable Spawned: int
      /// Guertelstaub: Dichte 0..1 (0 = aus).
      mutable DustAmount: float
      Gl: GpuParticleGl option }

[<RequireQualifiedAccess>]
module ParticleShaders =

    let vertex =
        """#version 300 es
precision highp float;

layout(location = 0) in vec2 aCorner;
layout(location = 1) in vec4 iPosTime;   // xyz Start, w Geburtszeit (relativ zur Epoche)
layout(location = 2) in vec4 iVelLife;   // xyz Startgeschwindigkeit, w Lebensdauer
layout(location = 3) in vec4 iAccDrag;   // xyz Beschleunigung, w Drag (1/s)
layout(location = 4) in vec4 iColor0;    // rgb Startfarbe (HDR), w Startgroesse
layout(location = 5) in vec4 iColor1;    // rgb Endfarbe, w Endgroesse
layout(location = 6) in vec4 iShape;     // x Form, y Streckung (s), z Einblenden, w Ausblend-Exponent

uniform mat4 uViewProj;
uniform vec3 uCamPos;
uniform vec3 uCamRight;
uniform vec3 uCamUp;
uniform float uTime;
uniform float uPixel;     // Weltgroesse eines Pixels in 1 m Abstand
uniform float uMaxSize;   // groesster Bildradius in Pixeln (Fuellrate)

out vec3 vUv;     // x quer, y laengs (in Groessen), z Schweiflaenge (in Groessen)
out vec3 vColor;
flat out float vShape;

void main() {
    float age = uTime - iPosTime.w;
    float life = iVelLife.w;
    vUv = vec3(aCorner, 0.0);
    vShape = iShape.x;
    if (age < 0.0 || age >= life) {
        // Ungeboren oder erloschen: ausserhalb des Clipraums, es entstehen keine Fragmente.
        vColor = vec3(0.0);
        gl_Position = vec4(0.0, 0.0, 2.0, 1.0);
        return;
    }
    float t = age / life;
    float k = iAccDrag.w;
    float decay = exp(-k * age);
    float reach = k > 1e-4 ? (1.0 - decay) / k : age;
    float drift = k > 1e-4 ? (age - reach) / k : 0.5 * age * age;
    vec3 pos = iPosTime.xyz + iVelLife.xyz * reach + iAccDrag.xyz * drift;
    vec3 vel = iVelLife.xyz * decay + iAccDrag.xyz * reach;

    bool smoke = iShape.x > 1.5;
    bool spark = !smoke && iShape.x > 0.5;
    // Rauch quillt schnell auf und waechst dann langsam weiter.
    float size = mix(iColor0.w, iColor1.w, smoke ? sqrt(t) : t);
    vec3 color = mix(iColor0.rgb, iColor1.rgb, t);
    float fade = pow(1.0 - t, iShape.w);
    if (iShape.z > 0.0) fade *= clamp(t / iShape.z, 0.0, 1.0);

    vec3 toCam = uCamPos - pos;
    float dist = max(length(toCam), 1e-3);
    vec3 view = toCam / dist;
    // Direkt vor der Linse ausblenden statt das Bild zu fuellen.
    fade *= smoothstep(0.8, 4.0, dist);
    // Pixelboden: nie duenner als gut ein Pixel. Was kleiner waere, wird dunkler
    // statt zu flimmern (gleiche Lichtmenge auf groesserer Flaeche).
    float floorSize = 1.1 * uPixel * dist;
    if (size < floorSize) {
        float ratio = size / floorSize;
        fade *= spark ? ratio : ratio * ratio;
        size = floorSize;
    }
    float maxSize = uMaxSize * uPixel * dist;
    size = min(size, maxSize);

    vec3 corner;
    if (spark) {
        vec3 across = vel - view * dot(vel, view);
        float speed = length(across);
        vec3 dir = speed > 1e-3 ? across / speed : uCamUp;
        vec3 side = normalize(cross(dir, view));
        float tail = min(speed * iShape.y, maxSize);
        float along = aCorner.y > 0.0 ? size : -tail - size;
        corner = pos + side * (aCorner.x * size) + dir * along;
        vUv = vec3(aCorner.x, along / size, tail / size);
    } else {
        corner = pos + (uCamRight * aCorner.x + uCamUp * aCorner.y) * size;
    }
    vColor = color * fade;
    gl_Position = uViewProj * vec4(corner, 1.0);
}
"""

    let fragment =
        """#version 300 es
precision highp float;

in vec3 vUv;
in vec3 vColor;
flat in float vShape;

uniform bool uHdr;

out vec4 outColor;
        """ + Shaders.displayTransform + """
void main() {
    float r2 = dot(vUv.xy, vUv.xy);
    float a;
    if (vShape < 0.5) {
        // Gluehpunkt: heisser Kern, weicher Hof.
        float f = max(0.0, 1.0 - r2);
        a = f * f * (0.4 + 0.6 * f * f);
    } else if (vShape < 1.5) {
        // Funke: Kapsel mit rundem Kopf; der Schweif laeuft nach hinten aus.
        float tail = vUv.z;
        float y = vUv.y > 0.0 ? vUv.y : min(0.0, vUv.y + tail);
        float f = max(0.0, 1.0 - vUv.x * vUv.x - y * y);
        float fall = clamp(-vUv.y / max(tail, 1e-3), 0.0, 1.0);
        a = f * f * (1.0 - 0.85 * fall);
    } else {
        // Rauch: sehr weicher Rand.
        float f = max(0.0, 1.0 - r2);
        a = f * f * f;
    }
    vec3 c = vColor * a;
    outColor = vec4(uHdr ? c : displayColor(c), 1.0);
}
"""

    /// Guertelstaub: feste Koerner in einem Wuerfel, der sich periodisch um die Kamera
    /// wiederholt. Die CPU tut pro Frame nichts ausser ein paar Uniforms zu setzen.
    let dustVertex =
        """#version 300 es
precision highp float;

layout(location = 0) in vec2 aCorner;
layout(location = 1) in vec4 iGrain;   // xyz Lage in der Zelle, w Groesse
layout(location = 2) in vec4 iSeed;    // x Rang 0..1, y Helligkeit, z Phase, w Funkelrate

uniform mat4 uViewProj;
uniform vec3 uCamPos;
uniform vec3 uCamRight;
uniform vec3 uCamUp;
uniform vec3 uVelocity;   // Eigenbewegung der Kamera: die Koerner ziehen relativ dazu Striche
uniform vec3 uSunDir;     // zeigt ZUR Sonne
uniform vec3 uColor;      // Sonnenlicht * Staubfarbe
uniform float uCell;
uniform float uAmount;    // Dichte 0..1: Koerner mit Rang darunter sind sichtbar
uniform float uTime;
uniform float uPixel;
uniform float uStretch;

out vec3 vUv;     // x quer, y laengs (in Groessen), z Schweiflaenge (in Groessen)
out vec3 vColor;
flat out float vShape;

void main() {
    vUv = vec3(aCorner, 0.0);
    vShape = 1.0;
    float show = smoothstep(iSeed.x, iSeed.x + 0.08, uAmount);
    vec3 d = iGrain.xyz - uCamPos;
    float radius = 0.5 * uCell;
    d -= floor(d / uCell) * uCell + radius;
    float fade = show * max(0.0, 1.0 - dot(d, d) / (radius * radius));
    float dist = max(length(d), 1e-3);
    fade *= smoothstep(1.2, 6.0, dist);
    if (fade <= 0.0) {
        vColor = vec3(0.0);
        gl_Position = vec4(0.0, 0.0, 2.0, 1.0);
        return;
    }
    vec3 pos = uCamPos + d;
    vec3 ray = d / dist;
    vec3 view = -ray;
    // Vorwaertsstreuung: gegen die Sonne geblickt leuchtet der Staub auf, im Ruecken
    // der Sonne bleibt nur ein schwacher Schimmer.
    float c = max(dot(ray, uSunDir), 0.0);
    float phase = 0.4 + 0.8 * c * c + 3.0 * pow(c, 10.0);
    // Taumelnde Koerner blitzen kurz auf, wenn eine Flaeche die Sonne spiegelt.
    float glint = 0.55 + 0.45 * max(0.0, sin(uTime * iSeed.w + iSeed.z));
    float size = iGrain.w;
    float floorSize = 1.1 * uPixel * dist;
    if (size < floorSize) {
        fade *= size / floorSize;
        size = floorSize;
    }
    vec3 rel = -uVelocity;
    vec3 across = rel - view * dot(rel, view);
    float speed = length(across);
    vec3 dir = speed > 1e-3 ? across / speed : uCamUp;
    vec3 side = normalize(cross(dir, view));
    float tail = min(speed * uStretch, 0.35 * dist);
    // Ein langer Strich verteilt dasselbe Licht auf mehr Flaeche.
    fade *= 0.35 + 0.65 * size / (size + 0.25 * tail);
    float along = aCorner.y > 0.0 ? size : -tail - size;
    vec3 corner = pos + side * (aCorner.x * size) + dir * along;
    vUv = vec3(aCorner.x, along / size, tail / size);
    vColor = uColor * iSeed.y * phase * glint * fade;
    gl_Position = uViewProj * vec4(corner, 1.0);
}
"""

[<RequireQualifiedAccess>]
module GpuParticles =

    /// Floats pro Partikel: Start+Zeit, Geschwindigkeit+Leben, Beschleunigung+Drag,
    /// Farbe+Groesse am Anfang, am Ende, Form.
    [<Literal>]
    let Stride = 24

    /// Kompiliermaximum des Rings (Stufe Ultra). 3 MB Speicher je Seite.
    [<Literal>]
    let MaxCapacity = 32768

    /// Partikelbudget der Stufe "Hoch".
    [<Literal>]
    let DefaultBudget = 12288

    /// Kleinstes sinnvolles Budget (Triebwerksspuren plus ein Treffer).
    [<Literal>]
    let MinBudget = 512

    /// Koerner des Guertelstaubs und Kantenlaenge ihres Wuerfels (Meter).
    [<Literal>]
    let DustGrains = 2400

    [<Literal>]
    let DustCell = 110.0

    /// Nach so vielen Sekunden wird die Zeitbasis auf der GPU nachgezogen.
    [<Literal>]
    let EpochSpan = 1024.0

    /// Vorschlaege fuer die Qualitaetsstufen Niedrig, Mittel, Hoch, Ultra.
    let presets = [| 3072; 6144; DefaultBudget; 24576 |]

    /// Reiner CPU-Betrieb: Ring, Zeit und Emission ohne GL (Tests, Fallback).
    let createCpu (capacity: int) (budget: int) : GpuParticleSystem =
        let capacity = max 1 capacity
        { Capacity = capacity
          Data = JS.Constructors.Float32Array.Create(capacity * Stride)
          Death = JS.Constructors.Float64Array.Create capacity
          Rng = Rng(26011)
          Budget = max 1 (min capacity budget)
          Head = 0
          Tail = 0
          Count = 0
          DirtyStart = 0
          DirtyCount = 0
          AllDirty = false
          Clock = 0.0
          Epoch = 0.0
          Spawned = 0
          DustAmount = 0.0
          Gl = None }

    /// Partikelbudget (Qualitaetsstufe). Wirkt auf neue Partikel: Laeuft der Bogen
    /// ueber das neue Budget, weichen beim naechsten Anlegen die aeltesten.
    let setBudget (system: GpuParticleSystem) (n: int) =
        system.Budget <- max (min MinBudget system.Capacity) (min system.Capacity n)

    /// Detailgrad fuer Ausstoesse: 1 ab dem Budget der Stufe Hoch, darunter anteilig.
    let detail (system: GpuParticleSystem) : float =
        max 0.25 (min 1.0 (float system.Budget / float DefaultBudget))

    /// Lebende (bzw. noch nicht freigegebene) Partikel.
    let live (system: GpuParticleSystem) = system.Count

    /// Entfernt alle Partikel (Sektorwechsel, Neustart).
    let clear (system: GpuParticleSystem) =
        system.Head <- 0
        system.Tail <- 0
        system.Count <- 0
        system.DirtyStart <- 0
        system.DirtyCount <- 0
        system.AllDirty <- false

    /// Setzt die Uhr des aktuellen Bildes (Weltzeit, wie sie gezeichnet wird). Ein
    /// Zeitsprung (Laden eines Spielstands) leert den Ring; nach EpochSpan Sekunden
    /// zieht die Zeitbasis nach, damit float32 auf der GPU genau bleibt.
    let setClock (system: GpuParticleSystem) (time: float) =
        if time < system.Clock - 0.5 || time > system.Clock + 30.0 then
            clear system
            system.Epoch <- time
        elif time - system.Epoch > EpochSpan then
            let shift = float32 (time - system.Epoch)
            let d = system.Data
            let mutable slot = system.Tail

            for _ in 1 .. system.Count do
                let o = slot * Stride + 3
                d.[o] <- d.[o] - shift
                slot <- ParticleRing.next slot system.Capacity

            system.Epoch <- time
            system.AllDirty <- true

        system.Clock <- time

    /// Gibt abgelaufene Partikel am Bogenende frei.
    let retire (system: GpuParticleSystem) =
        let n = ParticleRing.expired system.Death system.Tail system.Count system.Capacity system.Clock

        if n > 0 then
            system.Tail <- ParticleRing.advance system.Tail n system.Capacity
            system.Count <- system.Count - n

    /// Reserviert den naechsten Platz am Kopf des Rings.
    let private allocate (system: GpuParticleSystem) : int =
        let drop = ParticleRing.dropFor system.Count system.Budget

        if drop > 0 then
            system.Tail <- ParticleRing.advance system.Tail drop system.Capacity
            system.Count <- system.Count - drop

        let slot = system.Head
        system.Head <- ParticleRing.next slot system.Capacity
        system.Count <- system.Count + 1

        if system.DirtyCount = 0 then
            system.DirtyStart <- slot

        system.DirtyCount <- min system.Capacity (system.DirtyCount + 1)
        system.Spawned <- system.Spawned + 1
        slot

    /// Legt einen Partikel an: `time` ist die Weltzeit der Geburt, (x, y, z) der Ort und
    /// (vx, vy, vz) die Geschwindigkeit zu diesem Zeitpunkt, (ax, ay, az) eine feste
    /// Beschleunigung. `life` und `size` skalieren Lebensdauer und Groesse der Sorte,
    /// (r, g, b) faerbt beide Farben ein.
    let emitAccel
        (system: GpuParticleSystem)
        (look: ParticleLook)
        (time: float)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (ax: float)
        (ay: float)
        (az: float)
        (life: float)
        (size: float)
        (r: float)
        (g: float)
        (b: float)
        =
        let lifetime = look.Life * life * (1.0 + look.LifeJitter * (2.0 * system.Rng.NextFloat() - 1.0))

        if lifetime > 0.0 && size > 0.0 then
            let slot = allocate system
            let o = slot * Stride
            let d = system.Data
            d.[o] <- float32 x
            d.[o + 1] <- float32 y
            d.[o + 2] <- float32 z
            d.[o + 3] <- float32 (time - system.Epoch)
            d.[o + 4] <- float32 vx
            d.[o + 5] <- float32 vy
            d.[o + 6] <- float32 vz
            d.[o + 7] <- float32 lifetime
            d.[o + 8] <- float32 ax
            d.[o + 9] <- float32 ay
            d.[o + 10] <- float32 az
            d.[o + 11] <- float32 look.Drag
            d.[o + 12] <- float32 (look.R0 * r)
            d.[o + 13] <- float32 (look.G0 * g)
            d.[o + 14] <- float32 (look.B0 * b)
            d.[o + 15] <- float32 (look.Size0 * size)
            d.[o + 16] <- float32 (look.R1 * r)
            d.[o + 17] <- float32 (look.G1 * g)
            d.[o + 18] <- float32 (look.B1 * b)
            d.[o + 19] <- float32 (look.Size1 * size)
            d.[o + 20] <- float32 look.Shape
            d.[o + 21] <- float32 look.Stretch
            d.[o + 22] <- float32 look.FadeIn
            d.[o + 23] <- float32 look.FadeOut
            system.Death.[slot] <- time + lifetime

    /// Wie emitAccel, ohne Beschleunigung (der Normalfall: nur Drag).
    let emit (system: GpuParticleSystem) (look: ParticleLook) (time: float) x y z vx vy vz (life: float) (size: float) (r: float) (g: float) (b: float) =
        emitAccel system look time x y z vx vy vz 0.0 0.0 0.0 life size r g b

    // ---------------------------------------------------------------------------------
    //  Senke fuer die Emissionshaken in Effects (PhysicsAndPool.fs)
    // ---------------------------------------------------------------------------------

    let mutable private sink: GpuParticleSystem option = None

    /// Meldet das System als Ziel der Kampf-Emitter an (einmal beim Aufbau der Szene).
    let attach (system: GpuParticleSystem) = sink <- Some system

    let detach () = sink <- None

    /// Das angemeldete System (None in Tests ohne Partikelsystem).
    let current () = sink

    // ---------------------------------------------------------------------------------
    //  GL
    // ---------------------------------------------------------------------------------

    let private corners (gl: WebGL2RenderingContext) =
        let data = JS.Constructors.Float32Array.Create 8
        let values = [| -1.0f; -1.0f; 1.0f; -1.0f; -1.0f; 1.0f; 1.0f; 1.0f |]

        for i in 0..7 do
            data.[i] <- values.[i]

        let buffer = gl.createBuffer ()
        gl.bindBuffer (gl.ARRAY_BUFFER, buffer)
        gl.bufferData (gl.ARRAY_BUFFER, U3.Case2(unbox<JS.ArrayBufferView> data), gl.STATIC_DRAW)
        gl.enableVertexAttribArray 0.0
        gl.vertexAttribPointer (0.0, 2.0, gl.FLOAT, false, 8.0, 0.0)
        buffer

    /// Instanz-Attribute 1..n mit je 4 Floats ab Platz `first`.
    let private pointAttributes (gl: WebGL2RenderingContext) (stride: int) (attributes: int) (first: int) =
        let bytes = first * stride * 4

        for k in 0 .. attributes - 1 do
            gl.vertexAttribPointer (float (1 + k), 4.0, gl.FLOAT, false, float (stride * 4), float (bytes + k * 16))

    let private instanceAttributes (gl: WebGL2RenderingContext) (stride: int) (attributes: int) =
        for k in 0 .. attributes - 1 do
            gl.enableVertexAttribArray (float (1 + k))
            gl.vertexAttribDivisor (float (1 + k), 1.0)

        pointAttributes gl stride attributes 0

    /// Feste Staubkoerner: Lage in der Zelle, Groesse, Rang, Helligkeit, Funkelphase.
    let private dustGrains () =
        let rng = Rng(9173)
        let data = JS.Constructors.Float32Array.Create(DustGrains * 8)

        for i in 0 .. DustGrains - 1 do
            let o = i * 8
            data.[o] <- float32 (rng.Range(0.0, DustCell))
            data.[o + 1] <- float32 (rng.Range(0.0, DustCell))
            data.[o + 2] <- float32 (rng.Range(0.0, DustCell))
            // Viele sehr feine, wenige groebere Koerner.
            data.[o + 3] <- float32 (0.02 + 0.07 * Math.Pow(rng.NextFloat(), 3.0))
            data.[o + 4] <- float32 ((float i + rng.NextFloat()) / float DustGrains * 0.92)
            data.[o + 5] <- float32 (rng.Range(0.45, 1.0))
            data.[o + 6] <- float32 (rng.Range(0.0, 2.0 * Math.PI))
            data.[o + 7] <- float32 (rng.Range(0.6, 3.2))

        data

    let private createGl (renderer: Renderer) (capacity: int) : GpuParticleGl =
        let gl = renderer.Gl
        let program = Renderer.linkProgram gl ParticleShaders.vertex ParticleShaders.fragment
        let dustProgram = Renderer.linkProgram gl ParticleShaders.dustVertex ParticleShaders.fragment
        gl.bindVertexArray (unbox null)

        let vao = gl.createVertexArray ()
        gl.bindVertexArray vao
        corners gl |> ignore
        let buffer = gl.createBuffer ()
        gl.bindBuffer (gl.ARRAY_BUFFER, buffer)
        gl.bufferData (gl.ARRAY_BUFFER, U3.Case1(float (capacity * Stride * 4)), gl.DYNAMIC_DRAW)
        instanceAttributes gl Stride 6

        let dustVao = gl.createVertexArray ()
        gl.bindVertexArray dustVao
        corners gl |> ignore
        let grains = gl.createBuffer ()
        gl.bindBuffer (gl.ARRAY_BUFFER, grains)
        gl.bufferData (gl.ARRAY_BUFFER, U3.Case2(unbox<JS.ArrayBufferView> (dustGrains ())), gl.STATIC_DRAW)
        instanceAttributes gl 8 2

        gl.bindVertexArray (unbox null)
        gl.bindBuffer (gl.ARRAY_BUFFER, unbox null)
        let u name = gl.getUniformLocation (program, name)
        let ud name = gl.getUniformLocation (dustProgram, name)
        // Der Hauptshader des Renderers bleibt aktiv (Zustandscache).
        gl.useProgram renderer.Program

        { Program = program
          Vao = vao
          Buffer = buffer
          UViewProj = u "uViewProj"
          UCamPos = u "uCamPos"
          UCamRight = u "uCamRight"
          UCamUp = u "uCamUp"
          UTime = u "uTime"
          UPixel = u "uPixel"
          UMaxSize = u "uMaxSize"
          UHdr = u "uHdr"
          DustProgram = dustProgram
          DustVao = dustVao
          UDustViewProj = ud "uViewProj"
          UDustCamPos = ud "uCamPos"
          UDustCamRight = ud "uCamRight"
          UDustCamUp = ud "uCamUp"
          UDustVelocity = ud "uVelocity"
          UDustSun = ud "uSunDir"
          UDustColor = ud "uColor"
          UDustCell = ud "uCell"
          UDustAmount = ud "uAmount"
          UDustTime = ud "uTime"
          UDustPixel = ud "uPixel"
          UDustStretch = ud "uStretch"
          UDustHdr = ud "uHdr" }

    /// Legt das System mit GPU-Puffern an. Schlaegt das Shader-Programm fehl, laeuft das
    /// Spiel ohne GPU-Partikel weiter (die CPU-Effekte bleiben).
    let create (renderer: Renderer) (budget: int) : GpuParticleSystem =
        let cpu = createCpu MaxCapacity budget

        try
            let g = createGl renderer MaxCapacity
            // Aufwaermen: Manche Treiber (ANGLE/D3D11) uebersetzen Shader erst beim ersten
            // Draw. Ein leerer Platz (Lebensdauer 0) bzw. Staubdichte 0 wird im Shader
            // verworfen, kostet aber den Ruckler jetzt statt bei der ersten Explosion.
            let gl = renderer.Gl
            gl.useProgram g.Program
            gl.bindVertexArray g.Vao
            gl.drawArraysInstanced (gl.TRIANGLE_STRIP, 0.0, 4.0, 1.0)
            gl.useProgram g.DustProgram
            gl.uniform1f (g.UDustAmount, 0.0)
            gl.uniform1f (g.UDustCell, DustCell)
            gl.bindVertexArray g.DustVao
            gl.drawArraysInstanced (gl.TRIANGLE_STRIP, 0.0, 4.0, 1.0)
            gl.bindVertexArray (unbox null)
            gl.useProgram renderer.Program
            { cpu with Gl = Some g }
        with ex ->
            Browser.Dom.console.warn ("GPU-Partikel abgeschaltet: " + ex.Message)
            renderer.Gl.useProgram renderer.Program
            cpu

    /// Laedt die seit dem letzten Bild neuen Plaetze hoch (ein bis zwei bufferSubData).
    let private upload (gl: WebGL2RenderingContext) (system: GpuParticleSystem) =
        let all = system.AllDirty
        let start = if all then system.Tail else system.DirtyStart
        let count = if all then system.Count else system.DirtyCount
        let first = ParticleRing.firstSpan start count system.Capacity
        let second = ParticleRing.secondSpan start count system.Capacity

        if first > 0 then
            gl.bufferSubDataRange (gl.ARRAY_BUFFER, float (start * Stride * 4), system.Data, float (start * Stride), float (first * Stride))

        if second > 0 then
            gl.bufferSubDataRange (gl.ARRAY_BUFFER, 0.0, system.Data, 0.0, float (second * Stride))

        system.AllDirty <- false
        system.DirtyCount <- 0
        system.DirtyStart <- system.Head

    /// Zeichnet Partikel und Guertelstaub additiv ins aktuelle Szenenziel. Nach den
    /// additiven Batches und vor Renderer.endFrame aufrufen. `view` und `fov` sind die der
    /// Kamera, (vx, vy, vz) ihre Eigenbewegung (Staubstriche).
    let draw (renderer: Renderer) (system: GpuParticleSystem) (view: Mat4) (fov: float) (vx: float) (vy: float) (vz: float) =
        match system.Gl with
        | None -> ()
        | Some g ->
            retire system
            let dust = system.DustAmount > 0.002
            let gl = renderer.Gl

            if system.Count > 0 || dust then
                let m = view.Data
                // Zeilen der View-Rotation: Kamera-Rechts und -Oben in Weltkoordinaten.
                let rx = float m.[0]
                let ry = float m.[4]
                let rz = float m.[8]
                let ux = float m.[1]
                let uy = float m.[5]
                let uz = float m.[9]
                let pixel = 2.0 * tan (fov * 0.5) / float (max 1 renderer.Height)
                let hdr = if renderer.Post.Hdr then 1.0 else 0.0
                let cam = renderer.CameraPos

                gl.enable gl.BLEND
                gl.blendFunc (gl.ONE, gl.ONE)
                gl.depthMask false
                // Funken drehen ihre Flaeche frei: keine Rueckseiten verwerfen.
                gl.disable gl.CULL_FACE

                if system.Count > 0 then
                    gl.useProgram g.Program
                    gl.uniformMatrix4fv (g.UViewProj, false, renderer.ViewProj.Data)
                    gl.uniform3fv (g.UCamPos, cam)
                    gl.uniform3f (g.UCamRight, rx, ry, rz)
                    gl.uniform3f (g.UCamUp, ux, uy, uz)
                    gl.uniform1f (g.UTime, system.Clock - system.Epoch)
                    gl.uniform1f (g.UPixel, pixel)
                    gl.uniform1f (g.UMaxSize, 0.22 * float renderer.Height)
                    gl.uniform1i (g.UHdr, hdr)
                    gl.bindVertexArray g.Vao
                    gl.bindBuffer (gl.ARRAY_BUFFER, g.Buffer)

                    if system.AllDirty || system.DirtyCount > 0 then
                        upload gl system

                    let first = ParticleRing.firstSpan system.Tail system.Count system.Capacity
                    let second = ParticleRing.secondSpan system.Tail system.Count system.Capacity
                    pointAttributes gl Stride 6 system.Tail
                    gl.drawArraysInstanced (gl.TRIANGLE_STRIP, 0.0, 4.0, float first)

                    if second > 0 then
                        pointAttributes gl Stride 6 0
                        gl.drawArraysInstanced (gl.TRIANGLE_STRIP, 0.0, 4.0, float second)
                        renderer.DrawCalls <- renderer.DrawCalls + 1

                    renderer.DrawCalls <- renderer.DrawCalls + 1
                    renderer.InstancesDrawn <- renderer.InstancesDrawn + system.Count

                if dust then
                    let sun = renderer.Lights.SunDirection
                    let light = renderer.Lights.SunColor
                    // Rauchgrauer Gesteinsstaub im Sonnenlicht.
                    let amount = min 1.0 system.DustAmount
                    let grains = min DustGrains (int (ceil ((amount + 0.1) * float DustGrains)))
                    gl.useProgram g.DustProgram
                    gl.uniformMatrix4fv (g.UDustViewProj, false, renderer.ViewProj.Data)
                    gl.uniform3fv (g.UDustCamPos, cam)
                    gl.uniform3f (g.UDustCamRight, rx, ry, rz)
                    gl.uniform3f (g.UDustCamUp, ux, uy, uz)
                    gl.uniform3f (g.UDustVelocity, vx, vy, vz)
                    gl.uniform3fv (g.UDustSun, sun)
                    gl.uniform3f (g.UDustColor, float light.[0] * 1.9, float light.[1] * 1.76, float light.[2] * 1.56)
                    gl.uniform1f (g.UDustCell, DustCell)
                    gl.uniform1f (g.UDustAmount, amount)
                    gl.uniform1f (g.UDustTime, system.Clock - system.Epoch)
                    gl.uniform1f (g.UDustPixel, pixel)
                    gl.uniform1f (g.UDustStretch, 0.045)
                    gl.uniform1i (g.UDustHdr, hdr)
                    gl.bindVertexArray g.DustVao
                    gl.drawArraysInstanced (gl.TRIANGLE_STRIP, 0.0, 4.0, float grains)
                    renderer.DrawCalls <- renderer.DrawCalls + 1
                    renderer.InstancesDrawn <- renderer.InstancesDrawn + grains

                // Zustandscache des Renderers: deckend, Hauptshader, kein VAO.
                gl.bindVertexArray (unbox null)
                gl.bindBuffer (gl.ARRAY_BUFFER, unbox null)
                gl.enable gl.CULL_FACE
                gl.disable gl.BLEND
                gl.depthMask true
                gl.useProgram renderer.Program
                renderer.BlendState <- 0
