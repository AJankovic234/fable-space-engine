namespace SpaceEngine

// =====================================================================================
//  ParticleRecipes.fs  -  Partikelsorten (Daten) und Kampf-Emitter des GPU-Systems
//
//  Die Sorten beschreiben Form, Lebensdauer, Groesse und Farbe; die Emitter setzen
//  Treffer und Zerstoerungsstufen in GPU-Funken, Glut und kurz gluehenden Rauch um.
//  Mengen folgen den Rezepten in CombatVfx (Stufe, Funken, Splitter, Rauch), Groessen
//  der Explosionsgroesse. Alles kommt zusaetzlich zu den CPU-Effekten in Effects und
//  laeuft ueber das mit GpuParticles.attach angemeldete System - ohne eines sind die
//  Emitter wirkungslos (Tests, fehlender Shader). Die Zufallsfolge der Welt bleibt
//  unberuehrt: Gestreut wird mit dem eigenen Generator des Partikelsystems.
// =====================================================================================

open System
open Fable.Core

[<RequireQualifiedAccess>]
module ParticleRecipes =

    let private look shape life jitter drag size0 size1 r0 g0 b0 r1 g1 b1 stretch fadeIn fadeOut : ParticleLook =
        { Shape = shape
          Life = life
          LifeJitter = jitter
          Drag = drag
          Size0 = size0
          Size1 = size1
          R0 = r0
          G0 = g0
          B0 = b0
          R1 = r1
          G1 = g1
          B1 = b1
          Stretch = stretch
          FadeIn = fadeIn
          FadeOut = fadeOut }

    // ---------------------------------------------------------------------------------
    //  Sorten
    // ---------------------------------------------------------------------------------

    /// Heller Funke: weissgelb, kuehlt nach Orange ab, Schweif aus dem Flugweg.
    let spark = look ParticleShape.Spark 0.42 0.5 2.4 0.05 0.028 4.2 2.8 1.3 1.6 0.42 0.06 0.028 0.0 1.1

    /// Glut: langsame Splitter, die von Gelb ueber Orange nach Dunkelrot verglimmen.
    let ember = look ParticleShape.Glow 2.0 0.4 1.25 0.14 0.09 3.4 1.7 0.5 1.1 0.16 0.02 0.0 0.0 0.9

    /// Kurz gluehender Rauch: quillt auf, glimmt orange und verlischt zu Russbraun.
    let smoke = look ParticleShape.Smoke 1.7 0.35 1.6 0.3 1.0 0.42 0.15 0.045 0.03 0.016 0.012 0.0 0.22 1.6

    /// Blaue Schildfunken.
    let shieldSpark = look ParticleShape.Spark 0.3 0.4 3.0 0.04 0.02 0.9 1.9 3.6 0.12 0.4 1.3 0.03 0.0 1.2

    /// Gesteinsstaub eines zerplatzenden Asteroiden (Farbe kommt vom Gestein).
    let rockDust = look ParticleShape.Smoke 1.8 0.3 1.1 0.45 1.35 0.30 0.28 0.26 0.03 0.03 0.03 0.0 0.1 1.2

    /// Feine Triebwerksspur: Gluehpunkte, die rasch verblassen (Farbe vom Triebwerk).
    let trail = look ParticleShape.Glow 0.5 0.15 1.2 0.12 0.26 1.0 1.0 1.0 0.22 0.22 0.22 0.0 0.0 1.7

    /// Ausstoss einer Bremsduese: kaltes, kurz aufleuchtendes Gas.
    let retro = look ParticleShape.Smoke 0.34 0.3 4.0 0.08 0.5 0.55 0.72 1.0 0.03 0.04 0.06 0.0 0.04 1.5

    // ---------------------------------------------------------------------------------
    //  Ausstoesse
    // ---------------------------------------------------------------------------------

    let private direction = JS.Constructors.Float64Array.Create 3

    let private randomDirection (rng: Rng) =
        let z = rng.Range(-1.0, 1.0)
        let angle = rng.Range(0.0, 2.0 * Math.PI)
        let r = sqrt (1.0 - z * z)
        direction.[0] <- r * cos angle
        direction.[1] <- r * sin angle
        direction.[2] <- z

    /// `count` Partikel aus (x, y, z): Startpunkte bis `spread` Meter verstreut, Richtungen
    /// als Kegel um (nx, ny, nz) - `cone` 0 ist ein Strahl, 1 etwa ein Halbraum, eine
    /// Normale (0, 0, 0) streut in alle Richtungen. (vx, vy, vz) erbt die Quelle.
    let spray
        (system: GpuParticleSystem)
        (kind: ParticleLook)
        (time: float)
        (count: int)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (nx: float)
        (ny: float)
        (nz: float)
        (cone: float)
        (spread: float)
        (speedMin: float)
        (speedMax: float)
        (sizeMin: float)
        (sizeMax: float)
        (r: float)
        (g: float)
        (b: float)
        =
        let rng = system.Rng

        for _ in 1..count do
            randomDirection rng
            let rx = direction.[0]
            let ry = direction.[1]
            let rz = direction.[2]
            let mutable dx = nx + rx * cone
            let mutable dy = ny + ry * cone
            let mutable dz = nz + rz * cone
            let length = sqrt (dx * dx + dy * dy + dz * dz)

            if length > 1e-4 then
                dx <- dx / length
                dy <- dy / length
                dz <- dz / length
            else
                dx <- rx
                dy <- ry
                dz <- rz

            let speed = rng.Range(speedMin, speedMax)
            let offset = spread * rng.NextFloat()

            GpuParticles.emit
                system
                kind
                time
                (x + rx * offset)
                (y + ry * offset)
                (z + rz * offset)
                (vx + dx * speed)
                (vy + dy * speed)
                (vz + dz * speed)
                1.0
                (rng.Range(sizeMin, sizeMax))
                r
                g
                b

    /// Detailgrad mal Drosselung nach Fuellstand (fuer haeufige, kleine Effekte).
    let private thrift (system: GpuParticleSystem) =
        GpuParticles.detail system * Emission.throttle system.Count system.Budget

    // ---------------------------------------------------------------------------------
    //  Kampf (Haken in Effects, PhysicsAndPool.fs). `time` ist World.Time.
    // ---------------------------------------------------------------------------------

    /// Huellentreffer: Funkenfaecher von der Oberflaeche, Glutsplitter, ein Rauchwoelkchen.
    let hullHit (time: float) x y z nx ny nz vx vy vz (radius: float) (damage: float) =
        match GpuParticles.current () with
        | Some s ->
            let scale = CombatVfx.hitScale radius
            let hits = float (CombatVfx.hitSparks radius damage)
            let f = thrift s
            let thick = sqrt scale
            spray s spark time (Emission.scaled (hits * 2.2) f) x y z vx vy vz nx ny nz 0.8 0.1 22.0 (70.0 + 12.0 * scale) (0.8 * thick) (1.3 * thick) 1.0 1.0 1.0
            spray s ember time (Emission.scaled (hits * 0.45) f) x y z vx vy vz nx ny nz 1.0 0.15 4.0 18.0 (0.8 * thick) (1.4 * thick) 1.0 1.0 1.0
            spray s smoke time (Emission.scaled 2.0 f) x y z vx vy vz nx ny nz 0.6 0.1 1.5 5.0 (0.7 * scale) (1.1 * scale) 0.7 0.7 0.7
        | None -> ()

    /// Schildtreffer: wenige blaue Funken, die ueber die Blase spritzen.
    let shieldHit (time: float) x y z nx ny nz vx vy vz (radius: float) =
        match GpuParticles.current () with
        | Some s ->
            let scale = CombatVfx.hitScale radius
            let f = thrift s
            spray s shieldSpark time (Emission.scaled 12.0 f) x y z vx vy vz nx ny nz 1.6 0.2 12.0 34.0 (0.8 * sqrt scale) (1.2 * sqrt scale) 1.0 1.0 1.0
        | None -> ()

    /// Allgemeiner Funkenregen (Streifschuss am Gestein, Kollision, Torrahmen).
    let sparks (time: float) x y z (count: int) (r: float) (g: float) (b: float) =
        match GpuParticles.current () with
        | Some s ->
            // Die CPU-Farben liegen um 1,5; die GPU-Funken bringen ihre Hitze selbst mit.
            let k = 1.0 / 1.5
            spray s spark time (Emission.scaled (float count * 1.6) (thrift s)) x y z 0.0 0.0 0.0 0.0 0.0 0.0 1.0 0.2 10.0 45.0 0.8 1.2 (r * k) (g * k) (b * k)
        | None -> ()

    /// Erste Stufe einer Zerstoerung an einem Feuerball (Effects.blastLight und je Feuerball
    /// von Effects.breakup). `share` ist der Anteil dieses Feuerballs an der Stufe; die
    /// Mengen folgen dem CombatVfx-Rezept der Stufe, die Groessen dem Radius `s`.
    let blast (time: float) (tier: int) x y z vx vy vz (s: float) (share: float) =
        match GpuParticles.current () with
        | Some p ->
            let r = CombatVfx.recipe tier
            let f = GpuParticles.detail p * share
            let thick = 1.0 + Math.Min(2.0, s * 0.06)
            spray p spark time (Emission.scaled (float r.Sparks * 3.0) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.3) 30.0 (90.0 + s * 3.0) (1.1 * thick) (1.7 * thick) 1.0 1.0 1.0
            spray p ember time (Emission.scaled (float (r.Shards * 3 + r.Debris)) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.4) 6.0 (26.0 + s * 1.2) (1.0 * thick) (1.6 * thick) 1.0 1.0 1.0
            spray p smoke time (Emission.scaled (2.0 + float r.Fire * 0.15 + float r.Smoke * 0.5) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.35) 2.0 (4.0 + s * 0.5) (s * 0.5) (s * 0.8) 1.0 1.0 1.0
        | None -> ()

    /// Folgeexplosion einer schweren Zerstoerung.
    let secondary (time: float) x y z vx vy vz (s: float) =
        match GpuParticles.current () with
        | Some p ->
            let f = GpuParticles.detail p
            let thick = 1.0 + Math.Min(2.0, s * 0.06)
            spray p spark time (Emission.scaled 36.0 f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.2) 25.0 (70.0 + s * 3.0) (1.0 * thick) (1.5 * thick) 1.0 1.0 1.0
            spray p ember time (Emission.scaled 18.0 f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.3) 5.0 (20.0 + s) (1.0 * thick) (1.5 * thick) 1.0 1.0 1.0
            spray p smoke time (Emission.scaled 4.0 f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.3) 2.0 (3.0 + s * 0.4) (s * 0.45) (s * 0.7) 1.0 1.0 1.0
        | None -> ()

    /// Hauptexplosion schwerer Schiffe und Kernbruch der Grossschiffe; die Menge folgt
    /// CombatVfx.finaleParticles der Stufe.
    let finale (time: float) (tier: int) x y z vx vy vz (s: float) =
        match GpuParticles.current () with
        | Some p ->
            let total = float (CombatVfx.finaleParticles tier)
            let f = GpuParticles.detail p
            let thick = 1.0 + Math.Min(2.5, s * 0.06)
            let reach = if tier >= CombatVfx.TierCapital then 1.35 else 1.0
            spray p spark time (Emission.scaled (total * 1.3) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.3) 35.0 ((110.0 + s * 3.5) * reach) (1.2 * thick) (1.9 * thick) 1.0 1.0 1.0
            spray p ember time (Emission.scaled (total * 0.7) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.45) 8.0 ((32.0 + s * 1.5) * reach) (1.1 * thick) (1.8 * thick) 1.0 1.0 1.0
            spray p smoke time (Emission.scaled (total * 0.09) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (s * 0.5) 3.0 (6.0 + s * 0.6) (s * 0.6) s 1.0 1.0 1.0
        | None -> ()

    /// Vorexplosion auf der Huelle eines schwer angeschlagenen Schiffs.
    let hullPop (time: float) x y z vx vy vz (s: float) =
        match GpuParticles.current () with
        | Some p ->
            let f = thrift p
            spray p spark time (Emission.scaled 12.0 f) x y z vx vy vz 0.0 0.0 0.0 0.0 0.2 15.0 45.0 0.9 1.3 1.0 1.0 1.0
            spray p ember time (Emission.scaled 8.0 f) x y z vx vy vz 0.0 0.0 0.0 0.0 0.2 4.0 14.0 0.9 1.3 1.0 1.0 1.0
            spray p smoke time (Emission.scaled 1.0 f) x y z vx vy vz 0.0 0.0 0.0 0.0 0.1 1.0 3.0 (s * 0.8) (s * 1.1) 0.8 0.8 0.8
        | None -> ()

    /// Zerplatzender Asteroid: Gesteinsstaub in der Farbe des Brockens und helle Splitter.
    let rockBurst (time: float) x y z vx vy vz (radius: float) (r: float) (g: float) (b: float) =
        match GpuParticles.current () with
        | Some p ->
            let scale = Math.Min(radius, 30.0)
            let f = GpuParticles.detail p
            spray p rockDust time (Emission.scaled (5.0 + scale * 0.5) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (radius * 0.5) 1.5 (4.0 + scale * 0.35) (radius * 0.35) (radius * 0.6) (r * 2.0) (g * 2.0) (b * 2.0)
            spray p spark time (Emission.scaled (12.0 + scale) f) x y z vx vy vz 0.0 0.0 0.0 0.0 (radius * 0.3) 15.0 (50.0 + scale) 0.9 1.4 1.0 0.85 0.7
        | None -> ()
