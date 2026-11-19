namespace SpaceEngine

// =====================================================================================
//  EngineTrails.fs  -  Triebwerksspuren, Manoeverduesen und Guertelstaub (GPU-Partikel)
//
//  Reine Darstellung: Main ruft die Emitter beim Zeichnen mit den interpolierten
//  Schiffslagen auf. Jede Duese ist ein Strahler mit Gedaechtnis (letzter Ort, Rest-
//  bruchteil); die faelligen Partikel verteilen sich auf den Weg seit dem letzten Bild,
//  die Spur haengt daher lueckenlos in Weltkoordinaten und schwenkt in Kurven nach.
//  Springt ein Schiff (Sprung, Neustart, erstmals sichtbar), beginnt die Spur neu.
// =====================================================================================

open System
open Fable.Core

[<RequireQualifiedAccess>]
module EngineTrails =

    /// Strahler je Schiff: Spur links/rechts bzw. bis zu fuenf Gegnerduesen, Bremsduesen,
    /// vordere und hintere Steuerduesen.
    [<Literal>]
    let Nozzles = 8

    /// Abstand, ab dem ein Ortswechsel als Sprung gilt (Meter pro Bild).
    [<Literal>]
    let private Jump = 40.0

    /// Gegner und Verkehr weiter weg ziehen keine Spur (unsichtbar, spart Budget).
    [<Literal>]
    let private Reach = 1500.0

    type Emitters =
        { /// Letzter Weltort je Strahler (x, y, z).
          Prev: JS.Float64Array
          /// Uhrzeit der letzten Emission je Strahler.
          Seen: JS.Float64Array
          /// Angesammelter Bruchteil je Strahler (Emission.remainder).
          Carry: JS.Float64Array
          Enemies: int
          Lanes: int
          /// Scratch: Weltort und Weltrichtung des aktuellen Strahlers.
          Point: JS.Float64Array
          Dir: JS.Float64Array
          mutable Clock: float
          mutable Dt: float
          /// Geglaettete Staubdichte 0..1.
          mutable Belt: float }

    let create (enemies: int) (lanes: int) : Emitters =
        let keys = (1 + enemies + lanes) * Nozzles
        let seen = JS.Constructors.Float64Array.Create keys

        for i in 0 .. keys - 1 do
            seen.[i] <- -1e9

        { Prev = JS.Constructors.Float64Array.Create(keys * 3)
          Seen = seen
          Carry = JS.Constructors.Float64Array.Create keys
          Enemies = enemies
          Lanes = lanes
          Point = JS.Constructors.Float64Array.Create 3
          Dir = JS.Constructors.Float64Array.Create 3
          Clock = 0.0
          Dt = 0.0
          Belt = 0.0 }

    /// Einmal pro Bild nach GpuParticles.setClock: Zeitschritt der Emitter.
    let frame (t: Emitters) (system: GpuParticleSystem) =
        let dt = system.Clock - t.Clock
        t.Dt <- if dt > 0.0 && dt < 0.25 then dt else 0.0
        t.Clock <- system.Clock

    /// Lokalen Punkt mit q drehen und um p verschieben -> t.Point (ohne Vec3-Objekte).
    let private place (t: Emitters) (p: Vec3) (q: Quat) (lx: float) (ly: float) (lz: float) =
        // v' = v + 2w (u x v) + 2 u x (u x v)
        let ux = q.X
        let uy = q.Y
        let uz = q.Z
        let w = q.W
        let cx = uy * lz - uz * ly
        let cy = uz * lx - ux * lz
        let cz = ux * ly - uy * lx
        t.Point.[0] <- p.X + lx + 2.0 * (w * cx + uy * cz - uz * cy)
        t.Point.[1] <- p.Y + ly + 2.0 * (w * cy + uz * cx - ux * cz)
        t.Point.[2] <- p.Z + lz + 2.0 * (w * cz + ux * cy - uy * cx)

    /// Lokale Richtung mit q drehen -> t.Dir.
    let private turn (t: Emitters) (q: Quat) (lx: float) (ly: float) (lz: float) =
        let ux = q.X
        let uy = q.Y
        let uz = q.Z
        let w = q.W
        let cx = uy * lz - uz * ly
        let cy = uz * lx - ux * lz
        let cz = ux * ly - uy * lx
        t.Dir.[0] <- lx + 2.0 * (w * cx + uy * cz - uz * cy)
        t.Dir.[1] <- ly + 2.0 * (w * cy + uz * cx - ux * cz)
        t.Dir.[2] <- lz + 2.0 * (w * cz + ux * cy - uy * cx)

    /// Weg des Strahlers seit dem letzten Bild (0 bei Neustart).
    let private moved (t: Emitters) (key: int) =
        let o = key * 3
        let dx = t.Point.[0] - t.Prev.[o]
        let dy = t.Point.[1] - t.Prev.[o + 1]
        let dz = t.Point.[2] - t.Prev.[o + 2]
        if abs (t.Clock - t.Seen.[key]) > 0.25 then 0.0 else sqrt (dx * dx + dy * dy + dz * dz)

    /// Markiert einen Strahler als ruhend: beim naechsten Mal beginnt er neu.
    let private rest (t: Emitters) (key: int) = t.Seen.[key] <- -1e9

    /// Gleichmaessiger Strom aus dem Strahler `key` am Ort t.Point: Die faelligen Partikel
    /// verteilen sich auf den Weg seit dem letzten Bild (Ort und Geburtszeit). Startge-
    /// schwindigkeit = (bvx, bvy, bvz) + t.Dir * eject + Streuung.
    let private stream
        (t: Emitters)
        (system: GpuParticleSystem)
        (key: int)
        (rate: float)
        (kind: ParticleLook)
        (bvx: float)
        (bvy: float)
        (bvz: float)
        (eject: float)
        (jitter: float)
        (life: float)
        (size: float)
        (r: float)
        (g: float)
        (b: float)
        =
        let o = key * 3
        let x = t.Point.[0]
        let y = t.Point.[1]
        let z = t.Point.[2]
        let px = t.Prev.[o]
        let py = t.Prev.[o + 1]
        let pz = t.Prev.[o + 2]
        let gap = (x - px) * (x - px) + (y - py) * (y - py) + (z - pz) * (z - pz)
        let fresh = abs (t.Clock - t.Seen.[key]) > 0.25 || gap > Jump * Jump
        let dt = t.Dt

        if fresh then
            // Neuer Anfang: zufaelliger Rest, damit Duesen nicht im Gleichtakt spucken.
            t.Carry.[key] <- system.Rng.NextFloat()
        elif dt > 0.0 && rate > 0.0 then
            let carry = t.Carry.[key]
            let n = Emission.due carry rate dt
            let rng = system.Rng
            let ex = t.Dir.[0] * eject
            let ey = t.Dir.[1] * eject
            let ez = t.Dir.[2] * eject

            for j in 0 .. n - 1 do
                let f = Emission.moment carry rate dt j
                let born = t.Clock - (1.0 - f) * dt

                GpuParticles.emit
                    system
                    kind
                    born
                    (px + (x - px) * f)
                    (py + (y - py) * f)
                    (pz + (z - pz) * f)
                    (bvx + ex + jitter * (2.0 * rng.NextFloat() - 1.0))
                    (bvy + ey + jitter * (2.0 * rng.NextFloat() - 1.0))
                    (bvz + ez + jitter * (2.0 * rng.NextFloat() - 1.0))
                    life
                    size
                    r
                    g
                    b

            t.Carry.[key] <- Emission.remainder carry rate dt

        t.Prev.[o] <- x
        t.Prev.[o + 1] <- y
        t.Prev.[o + 2] <- z
        t.Seen.[key] <- t.Clock

    let inline private smooth (edge0: float) (edge1: float) (x: float) =
        let k = max 0.0 (min 1.0 ((x - edge0) / (edge1 - edge0)))
        k * k * (3.0 - 2.0 * k)

    /// Spur einer Duese: Rate nach Fahrt und Schub, Helligkeit nach Schub und Boost.
    let private engine
        (t: Emitters)
        (system: GpuParticleSystem)
        (key: int)
        (position: Vec3)
        (rotation: Quat)
        (lx: float)
        (ly: float)
        (lz: float)
        (spacing: float)
        (drive: float)
        (life: float)
        (size: float)
        (r: float)
        (g: float)
        (b: float)
        =
        place t position rotation lx ly lz
        // Ausstoss leicht nach hinten (+Z): die Spur loest sich sichtbar von der Duese.
        turn t rotation 0.0 0.0 1.0
        let speed = moved t key / max 1e-3 t.Dt
        let rate = if drive > 0.03 then Emission.spaced speed spacing 30.0 * drive else 0.0
        stream t system key rate ParticleRecipes.trail 0.0 0.0 0.0 2.5 0.45 life size r g b

    /// Spieler: zwei feine Spuren, Bremsduesen beim Bremsen oder Schub zurueck,
    /// Steuerduesen in harten Kurven. Nur in der Verfolgerkamera (sonst unsichtbar).
    let player (t: Emitters) (system: GpuParticleSystem) (ship: Ship) (hull: PlayerHull) (position: Vec3) (rotation: Quat) (courier: bool) =
        let c = ship.Controls
        let thrust = if c.Brake then 0.0 else c.Throttle
        let boost = ship.Boost
        let allowed = CombatVfx.trailsAllowed system.Count system.Budget
        let room = Emission.throttle system.Count system.Budget * GpuParticles.detail system
        // Rate folgt dem Schub (lueckige Spur bei wenig Schub), der Boost verdichtet sie.
        let drive = if allowed then min 1.4 (thrust * 0.85 + boost * 0.9) * room else 0.0
        let bright = 0.16 + 0.34 * thrust + 0.75 * boost
        let size = 1.0 + 0.45 * boost
        let life = 0.85 + 1.0 * boost
        let spacing = 0.2 + 0.03 * boost
        let r = if courier then 1.0 * bright else 0.30 * bright
        let g = if courier then 0.48 * bright else 0.62 * bright
        let b = if courier then 0.14 * bright else 1.0 * bright
        let left = ShipHardpoints.playerEngineLeft
        let right = ShipHardpoints.playerEngineRight
        engine t system 0 position rotation left.X left.Y (left.Z + 0.25) spacing drive life size r g b
        engine t system 1 position rotation right.X right.Y (right.Z + 0.25) spacing drive life size r g b

        // Bremsduesen: Bremse (X) oder ein Schiff, das schneller ist als sein Schubhebel.
        let v = ship.Velocity
        turn t rotation 0.0 0.0 -1.0
        let fx = t.Dir.[0]
        let fy = t.Dir.[1]
        let fz = t.Dir.[2]
        let along = v.X * fx + v.Y * fy + v.Z * fz
        let speed = Vec3.length v
        let target = if c.Brake then 0.0 else c.Throttle * ship.CruiseSpeed + boost * ship.BoostSpeed
        let braking =
            if c.Brake then min 1.0 (speed / 20.0)
            else max 0.0 (min 1.0 ((along - target - 3.0) / 16.0))
        let gas = room * (if allowed then 1.0 else 0.35)

        for i in 0 .. min 1 (hull.Retros.Length - 1) do
            let nozzle = hull.Retros.[i]
            place t position rotation nozzle.X nozzle.Y (nozzle.Z - 0.05)
            turn t rotation 0.0 0.0 -1.0
            let rate = if braking > 0.03 then 95.0 * braking * gas else 0.0
            stream t system (2 + i) rate ParticleRecipes.retro v.X v.Y v.Z 17.0 2.8 1.0 (0.7 + 0.5 * braking) (0.8 + 0.6 * braking) (0.8 + 0.6 * braking) (0.8 + 0.6 * braking)

        // Steuerduesen: Gieren (positiv = Nase links) und Nicken (positiv = Nase hoch).
        let yaw = ship.YawRate / max 0.01 ship.MaxYawRate
        let pitch = ship.PitchRate / max 0.01 ship.MaxPitchRate
        let yawPush = smooth 0.55 0.95 (abs yaw)
        let pitchPush = smooth 0.55 0.95 (abs pitch)
        let pitchSign = if pitch > 0.0 then 1.0 else -1.0
        let yawSign = if yaw > 0.0 then 1.0 else -1.0

        // 0/1: vorn an den Bremsduesen, 2/3: hinten neben den Triebwerken.
        for i in (if hull.Retros.Length > 0 then 0 else 2) .. 3 do
            let front = i < 2
            let nozzle =
                if front then hull.Retros.[min (hull.Retros.Length - 1) i]
                else if i = 2 then ShipHardpoints.playerEngineLeft else ShipHardpoints.playerEngineRight
            let side = if nozzle.X < 0.0 then -1.0 else 1.0
            // Vorn feuert die Aussenseite der Kurve nach aussen, hinten die Innenseite.
            let lateral = if (front && side = yawSign) || (not front && side = -yawSign) then yawPush else 0.0
            let vertical = pitchPush
            let lx = side * lateral
            let ly = (if front then -pitchSign else pitchSign) * vertical
            let push = max lateral vertical
            let ox = if front then nozzle.X else nozzle.X + side * 0.45
            let oy = if front then nozzle.Y else nozzle.Y + 0.22
            let oz = if front then nozzle.Z + 0.1 else nozzle.Z - 0.35
            place t position rotation ox oy oz
            let norm = sqrt (lx * lx + ly * ly)

            if norm > 1e-3 then
                turn t rotation (lx / norm) (ly / norm) 0.0

            let rate = if push > 0.03 then 70.0 * push * gas else 0.0
            stream t system (4 + i) rate ParticleRecipes.retro v.X v.Y v.Z 13.0 2.0 0.7 0.6 0.75 0.75 0.75

    /// Gegner: eine Spur je Duese in der warmen Triebwerksfarbe der Flotte.
    let enemy (t: Emitters) (system: GpuParticleSystem) (slot: int) (ship: Ship) (hull: EnemyHull) (position: Vec3) (rotation: Quat) (scale: float) (eye: Vec3) =
        let first = (1 + slot) * Nozzles

        if slot < t.Enemies then
            if Vec3.distanceSq position eye > Reach * Reach then
                for i in 0 .. Nozzles - 1 do
                    rest t (first + i)
            else
                let c = ship.Controls
                let thrust = if c.Brake then 0.0 else c.Throttle
                let boost = ship.Boost
                let allowed = CombatVfx.trailsAllowed system.Count system.Budget
                let room = Emission.throttle system.Count system.Budget * GpuParticles.detail system
                let drive = if allowed then min 1.3 (0.2 + thrust * 0.6 + boost * 0.9) * room else 0.0
                let bright = 0.35 + 0.6 * thrust + 1.2 * boost
                let life = 2.0 + 0.8 * boost

                for i in 0 .. min (Nozzles - 1) (hull.Engines.Length - 1) do
                    let nozzle, radius = hull.Engines.[i]
                    let rs = radius * scale
                    engine t system (first + i) position rotation (nozzle.X * scale) (nozzle.Y * scale) (nozzle.Z * scale + rs * 0.6) (0.25 + rs * 0.4) drive life (rs * 1.7) (1.0 * bright) (0.42 * bright) (0.12 * bright)

    /// Duesenaustritt der Verkehrsboote (wie Traffic.launch, knapp dahinter).
    [<Literal>]
    let private LaneNozzleY = -0.29

    [<Literal>]
    let private LaneNozzleZ = 6.6

    /// Ziviler Verkehr: ruhige blaue Spur hinter jedem Boot in Sichtweite.
    let traffic (t: Emitters) (system: GpuParticleSystem) (fleet: Traffic.Fleet) (sector: int) (time: float) (eye: Vec3) =
        if sector >= 0 && sector < fleet.Routes.Length then
            let lanes = fleet.Routes.[sector]
            let allowed = CombatVfx.trailsAllowed system.Count system.Budget
            let room = Emission.throttle system.Count system.Budget * GpuParticles.detail system

            for i in 0 .. min (t.Lanes - 1) (lanes.Length - 1) do
                let key = (1 + t.Enemies + i) * Nozzles
                let p, q = Traffic.pose lanes.[i] time

                if Vec3.distanceSq p eye > Reach * Reach then rest t key
                else engine t system key p q 0.0 LaneNozzleY LaneNozzleZ 0.3 (if allowed then 0.8 * room else 0.0) 1.3 2.2 0.22 0.62 1.0

    /// Guertelstaub: Dichte nach den Asteroiden im Umkreis der Kamera (gewichtet mit
    /// Radius und Naehe), weich nachgefuehrt. Zeichnen uebernimmt GpuParticles.draw.
    let beltDust (t: Emitters) (system: GpuParticleSystem) (field: AsteroidField) (eye: Vec3) =
        let d = field.Data
        let reach = 320.0
        let mutable weight = 0.0

        for i in 0 .. field.Count - 1 do
            let o = i * AsteroidField.Stride
            let dx = float d.[o] - eye.X
            let dy = float d.[o + 1] - eye.Y
            let dz = float d.[o + 2] - eye.Z
            let q = dx * dx + dy * dy + dz * dz

            if q < reach * reach then
                weight <- weight + float d.[o + 6] * (1.0 - sqrt q / reach)

        let target = min 1.0 (weight / 150.0)
        t.Belt <- t.Belt + (target - t.Belt) * (1.0 - exp (-1.2 * t.Dt))
        system.DustAmount <- t.Belt
