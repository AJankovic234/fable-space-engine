namespace SpaceEngine

// =====================================================================================
//  PhysicsAndPool.fs  -  Spielwelt: Flugmodell, Object Pools, Kollision, Schaden
//
//  Object Pooling
//    Laser, Partikel und Asteroiden liegen als Structure-of-Arrays in Float32Arrays
//    fester Groesse. Aktive Elemente stehen dicht gepackt in [0, Count); "Loeschen"
//    kopiert das letzte Element auf den freien Platz (swap-remove). Waehrend des
//    Spiels wird fuer sie nichts allokiert und nichts freigegeben.
//
//  Kollision
//    * Laser  <-> Asteroid : Strecke gegen Kugel (swept - schnelle Bolts tunneln nicht)
//    * Laser  <-> Schiff   : Strecke gegen Huellkugel, dann gegen die OBB des Schiffs
//    * Schiff <-> Asteroid : Kugel gegen OBB
//    * Schiff <-> Schiff   : OBB gegen OBB (Separating Axis Theorem, 15 Achsen)
//    Alle Tests sind reine Skalar-Mathematik auf vorab angelegten Scratch-Arrays.
//
//  Allokationen
//    Die Massenschleifen (Laser, Partikel, Asteroiden, Kollision) allokieren nichts.
//    Pro Schiff und Physikschritt entstehen wenige kurzlebige Vec3/Quat-Objekte
//    (Flugmodell). GameEvents sind kleine DU-Objekte und entstehen nur bei
//    Ereignissen (Treffer, Zerstoerung), nicht pro Frame.
// =====================================================================================

open System
open Fable.Core

/// Zugehoerigkeit von Schiffen und Lasern (Enum = blanke Zahl, passt in ein Uint8Array).
type Team =
    | Player = 0
    | Enemy = 1

/// Ereignisse der Simulation fuer die Spiellogik (Punkte, HUD, Wellen, Sound).
/// Main leert die Liste nach jedem Frame.
type GameEvent =
    | LaserFired of team: Team * position: Vec3
    | AsteroidDestroyed of radius: float * byTeam: Team * position: Vec3
    | ShipHit of victim: Team * damage: float * shieldAbsorbed: bool
    | ShipDestroyed of victim: Team * position: Vec3

/// Steuerbefehle eines Schiffs. Der Spieler fuellt sie aus der Eingabe,
/// die KI aus ihrer Zustandsmaschine - das Flugmodell kennt den Unterschied nicht.
type ShipControls =
    { /// -1..1, positiv = Nase hoch.
      mutable Pitch: float
      /// -1..1, positiv = Nase nach links.
      mutable Yaw: float
      /// -1..1, positiv = Rolle nach links.
      mutable Roll: float
      /// Schubhebel 0..1.
      mutable Throttle: float
      mutable Boost: bool
      mutable Brake: bool
      mutable Fire: bool }

type Ship =
    { mutable Position: Vec3
      mutable Rotation: Quat
      /// Zustand vor dem letzten Physikschritt (fuer die Render-Interpolation).
      mutable PrevPosition: Vec3
      mutable PrevRotation: Quat
      mutable Velocity: Vec3
      /// Winkelgeschwindigkeit im lokalen System (rad/s).
      mutable PitchRate: float
      mutable YawRate: float
      mutable RollRate: float
      /// Geglaetteter Boost 0..1.
      mutable Boost: float
      Controls: ShipControls
      Team: Team
      // --- Flugeigenschaften ---
      CruiseSpeed: float
      BoostSpeed: float
      MaxPitchRate: float
      MaxYawRate: float
      MaxRollRate: float
      // --- Kollision ---
      /// Radius der Huellkugel um Position (Broad Phase).
      Radius: float
      /// Mittelpunkt und halbe Kantenlaengen der OBB im Schiffssystem.
      ObbCenter: Vec3
      ObbHalf: Vec3
      // --- Bewaffnung ---
      GunLeft: Vec3
      GunRight: Vec3
      FireInterval: float
      /// Schaden pro Bolt (veraenderlich: das Wave-System skaliert die Gegner).
      mutable LaserDamage: float
      mutable FireCooldown: float
      // --- Zustand ---
      mutable MaxHull: float
      mutable MaxShield: float
      mutable Hull: float
      mutable Shield: float
      /// Zeit bis die Schildregeneration wieder einsetzt.
      mutable ShieldDelay: float
      /// 1 direkt nach einem Treffer, klingt ab (Treffer-Aufblitzen beim Rendern).
      mutable HitFlash: float
      /// > 0 direkt nach einem Schildtreffer (steuert die 3D-Energieschild-Blase).
      mutable ShieldFlash: float
      /// > 0 waehrend aktivem Waffen-Overdrive (schnellere Feuerrate + mehr Schaden).
      mutable Overdrive: float
      /// Timer fuer Partikelspuren (Rauch bei kritischer Huelle, Funken beim Boost).
      mutable TrailTimer: float
      mutable Alive: bool }

/// Kampf- und Effektparameter.
module Combat =
    [<Literal>]
    let LaserSpeed = 520.0

    /// Lebensdauer eines Bolts in Sekunden (Reichweite = Speed * Life).
    [<Literal>]
    let LaserLife = 1.25

    [<Literal>]
    let ShieldRegenDelay = 3.5

    /// Schildpunkte pro Sekunde.
    [<Literal>]
    let ShieldRegenRate = 14.0

    /// Lebenspunkte eines Asteroiden pro Einheit Radius.
    [<Literal>]
    let AsteroidHpPerRadius = 2.6

    /// Ab diesem Radius zerbricht ein Asteroid in Bruchstuecke statt zu verschwinden.
    [<Literal>]
    let SplitRadius = 7.0

[<RequireQualifiedAccess>]
module Ship =

    let private create
        (team: Team)
        (cruise: float)
        (boost: float)
        (pitchRate: float)
        (yawRate: float)
        (rollRate: float)
        (radius: float)
        (obbCenter: Vec3)
        (obbHalf: Vec3)
        (gunLeft: Vec3)
        (gunRight: Vec3)
        (fireInterval: float)
        (laserDamage: float)
        (hull: float)
        (shield: float)
        : Ship =
        { Position = Vec3.zero
          Rotation = Quat.identity
          PrevPosition = Vec3.zero
          PrevRotation = Quat.identity
          Velocity = Vec3.zero
          PitchRate = 0.0
          YawRate = 0.0
          RollRate = 0.0
          Boost = 0.0
          Controls =
            { Pitch = 0.0
              Yaw = 0.0
              Roll = 0.0
              Throttle = 0.6
              Boost = false
              Brake = false
              Fire = false }
          Team = team
          CruiseSpeed = cruise
          BoostSpeed = boost
          MaxPitchRate = pitchRate
          MaxYawRate = yawRate
          MaxRollRate = rollRate
          Radius = radius
          ObbCenter = obbCenter
          ObbHalf = obbHalf
          GunLeft = gunLeft
          GunRight = gunRight
          FireInterval = fireInterval
          LaserDamage = laserDamage
          FireCooldown = 0.0
          MaxHull = hull
          MaxShield = shield
          Hull = hull
          Shield = shield
          ShieldDelay = 0.0
          HitFlash = 0.0
          ShieldFlash = 0.0
          Overdrive = 0.0
          TrailTimer = 0.0
          Alive = false }

    let createPlayer () : Ship =
        create
            Team.Player
            75.0
            120.0
            1.5
            0.85
            2.6
            ShipHardpoints.playerRadius
            ShipHardpoints.playerObbCenter
            ShipHardpoints.playerObbHalf
            ShipHardpoints.playerGunLeft
            ShipHardpoints.playerGunRight
            0.13
            12.0
            100.0
            100.0

    /// Gegner: etwas langsamer und traeger als der Spieler, damit man ihn ausmanoevrieren kann.
    let createInterceptor () : Ship =
        create
            Team.Enemy
            62.0
            58.0
            1.3
            0.8
            2.3
            ShipHardpoints.interceptorRadius
            ShipHardpoints.interceptorObbCenter
            ShipHardpoints.interceptorObbHalf
            ShipHardpoints.interceptorGunLeft
            ShipHardpoints.interceptorGunRight
            0.26
            8.0
            45.0
            25.0

    /// Setzt ein Schiff mit voller Energie an eine neue Position (Start, Respawn).
    let reset (ship: Ship) (position: Vec3) (rotation: Quat) =
        ship.Position <- position
        ship.Rotation <- rotation
        ship.PrevPosition <- position
        ship.PrevRotation <- rotation
        ship.Velocity <- Quat.forward rotation * (ship.CruiseSpeed * 0.4)
        ship.PitchRate <- 0.0
        ship.YawRate <- 0.0
        ship.RollRate <- 0.0
        ship.Boost <- 0.0
        ship.Controls.Pitch <- 0.0
        ship.Controls.Yaw <- 0.0
        ship.Controls.Roll <- 0.0
        ship.Controls.Throttle <- 0.6
        ship.Controls.Boost <- false
        ship.Controls.Brake <- false
        ship.Controls.Fire <- false
        ship.FireCooldown <- 0.0
        ship.Hull <- ship.MaxHull
        ship.Shield <- ship.MaxShield
        ship.ShieldDelay <- 0.0
        ship.HitFlash <- 0.0
        ship.ShieldFlash <- 0.0
        ship.Overdrive <- 0.0
        ship.TrailTimer <- 0.0
        ship.Alive <- true

// -------------------------------------------------------------------------------------
//  Flugmodell (fuer Spieler und Gegner identisch)
// -------------------------------------------------------------------------------------

module Flight =

    /// Daempfung (1/s): bestimmt, wie lange das Schiff nach einer Kursaenderung
    /// noch in die alte Richtung driftet (~1/Drag Sekunden).
    [<Literal>]
    let Drag = 1.1

    [<Literal>]
    let BrakeDrag = 3.0

    /// Wie schnell die Drehraten dem Steuerbefehl folgen (1/s) - die Drehtraegheit.
    [<Literal>]
    let AngularResponse = 6.0

    [<Literal>]
    let BoostResponse = 4.0

    /// Ein fester Physikschritt.
    let step (ship: Ship) (dt: float) =
        let c = ship.Controls
        ship.PrevPosition <- ship.Position
        ship.PrevRotation <- ship.Rotation

        let boostTarget = if c.Boost && not c.Brake then 1.0 else 0.0
        ship.Boost <- ship.Boost + (boostTarget - ship.Boost) * (1.0 - exp (-BoostResponse * dt))

        // Drehung: Die Drehraten laufen dem Steuerbefehl traege nach und werden
        // dann im LOKALEN System per Quaternion integriert (kein Gimbal Lock).
        let blend = 1.0 - exp (-AngularResponse * dt)
        ship.PitchRate <- ship.PitchRate + (c.Pitch * ship.MaxPitchRate - ship.PitchRate) * blend
        ship.YawRate <- ship.YawRate + (c.Yaw * ship.MaxYawRate - ship.YawRate) * blend
        ship.RollRate <- ship.RollRate + (c.Roll * ship.MaxRollRate - ship.RollRate) * blend
        ship.Rotation <- Quat.integrateLocal ship.Rotation ship.PitchRate ship.YawRate ship.RollRate dt

        // Translation: Schub entlang der Nase, isotrope Daempfung. Der Schub ist so
        // gewaehlt, dass die Endgeschwindigkeit genau targetSpeed betraegt; weil die
        // Daempfung den alten Geschwindigkeitsvektor nur allmaehlich abbaut, driftet
        // das Schiff in Kurven sichtbar nach.
        let targetSpeed =
            if c.Brake then
                0.0
            else
                c.Throttle * ship.CruiseSpeed + ship.Boost * ship.BoostSpeed

        let drag = if c.Brake then BrakeDrag else Drag
        let forward = Quat.forward ship.Rotation
        let accelerated = Vec3.mulAdd ship.Velocity forward (targetSpeed * drag * dt)
        ship.Velocity <- accelerated * exp (-drag * dt)
        ship.Position <- Vec3.mulAdd ship.Position ship.Velocity dt

// -------------------------------------------------------------------------------------
//  Object Pools
// -------------------------------------------------------------------------------------

/// Laser-Bolts. Pro Bolt: [x, y, z, vx, vy, vz, qx, qy, qz, qw, Restlebensdauer, Schaden]
type LaserPool =
    { Data: JS.Float32Array
      Team: JS.Uint8Array
      Capacity: int
      mutable Count: int }

[<RequireQualifiedAccess>]
module LaserPool =

    [<Literal>]
    let Stride = 12

    let create (capacity: int) : LaserPool =
        { Data = JS.Constructors.Float32Array.Create(capacity * Stride)
          Team = JS.Constructors.Uint8Array.Create capacity
          Capacity = capacity
          Count = 0 }

    /// Belegt den naechsten freien Platz. Liefert false, wenn der Pool voll ist
    /// (der Schuss entfaellt dann einfach - es wird nie nachallokiert).
    let spawn
        (pool: LaserPool)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (qx: float)
        (qy: float)
        (qz: float)
        (qw: float)
        (damage: float)
        (team: Team)
        : bool =
        if pool.Count >= pool.Capacity then
            false
        else
            let o = pool.Count * Stride
            let d = pool.Data
            d.[o] <- float32 x
            d.[o + 1] <- float32 y
            d.[o + 2] <- float32 z
            d.[o + 3] <- float32 vx
            d.[o + 4] <- float32 vy
            d.[o + 5] <- float32 vz
            d.[o + 6] <- float32 qx
            d.[o + 7] <- float32 qy
            d.[o + 8] <- float32 qz
            d.[o + 9] <- float32 qw
            d.[o + 10] <- float32 Combat.LaserLife
            d.[o + 11] <- float32 damage
            pool.Team.[pool.Count] <- byte team
            pool.Count <- pool.Count + 1
            true

    /// Gibt den Platz frei: das letzte aktive Element rueckt nach.
    let kill (pool: LaserPool) (index: int) =
        let last = pool.Count - 1

        if index <> last then
            let d = pool.Data
            let src = last * Stride
            let dst = index * Stride

            for k in 0 .. Stride - 1 do
                d.[dst + k] <- d.[src + k]

            pool.Team.[index] <- pool.Team.[last]

        pool.Count <- last

/// Art eines Partikels (bestimmt Mesh und Batch beim Rendern).
module ParticleKind =
    /// Additiver Leuchtpunkt (Funken, Feuerball), zur Kamera ausgerichtet.
    [<Literal>]
    let Glow = 0

    /// Beleuchtetes, taumelndes Truemmerstueck.
    [<Literal>]
    let Debris = 1

/// Partikel. Pro Partikel: [x, y, z, vx, vy, vz, Restleben, Gesamtleben, Groesse, r, g, b]
type ParticlePool =
    { Data: JS.Float32Array
      Kind: JS.Uint8Array
      Capacity: int
      mutable Count: int }

[<RequireQualifiedAccess>]
module ParticlePool =

    [<Literal>]
    let Stride = 12

    /// Abbremsung pro Sekunde (Funken und Truemmer kommen langsam zur Ruhe).
    [<Literal>]
    let private Drag = 1.3

    let create (capacity: int) : ParticlePool =
        { Data = JS.Constructors.Float32Array.Create(capacity * Stride)
          Kind = JS.Constructors.Uint8Array.Create capacity
          Capacity = capacity
          Count = 0 }

    /// Ist der Pool voll, wird der Partikel verworfen.
    let spawn
        (pool: ParticlePool)
        (kind: int)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (life: float)
        (size: float)
        (r: float)
        (g: float)
        (b: float)
        =
        if pool.Count < pool.Capacity then
            let o = pool.Count * Stride
            let d = pool.Data
            d.[o] <- float32 x
            d.[o + 1] <- float32 y
            d.[o + 2] <- float32 z
            d.[o + 3] <- float32 vx
            d.[o + 4] <- float32 vy
            d.[o + 5] <- float32 vz
            d.[o + 6] <- float32 life
            d.[o + 7] <- float32 life
            d.[o + 8] <- float32 size
            d.[o + 9] <- float32 r
            d.[o + 10] <- float32 g
            d.[o + 11] <- float32 b
            pool.Kind.[pool.Count] <- byte kind
            pool.Count <- pool.Count + 1

    /// Bewegt alle Partikel und entfernt abgelaufene (swap-remove).
    let update (pool: ParticlePool) (dt: float) =
        let d = pool.Data
        let damping = exp (-Drag * dt)
        let mutable i = 0

        while i < pool.Count do
            let o = i * Stride
            let life = float d.[o + 6] - dt

            if life <= 0.0 then
                let last = pool.Count - 1

                if i <> last then
                    let src = last * Stride

                    for k in 0 .. Stride - 1 do
                        d.[o + k] <- d.[src + k]

                    pool.Kind.[i] <- pool.Kind.[last]

                pool.Count <- last
            // i bleibt stehen: auf dem Platz liegt jetzt ein anderer Partikel
            else
                d.[o + 6] <- float32 life
                let vx = float d.[o + 3] * damping
                let vy = float d.[o + 4] * damping
                let vz = float d.[o + 5] * damping
                d.[o + 3] <- float32 vx
                d.[o + 4] <- float32 vy
                d.[o + 5] <- float32 vz
                d.[o] <- float32 (float d.[o] + vx * dt)
                d.[o + 1] <- float32 (float d.[o + 1] + vy * dt)
                d.[o + 2] <- float32 (float d.[o + 2] + vz * dt)
                i <- i + 1

/// Asteroiden. Pro Asteroid:
/// [x, y, z, vx, vy, vz, Radius, Achse x, Achse y, Achse z, Drehrate, Phase, r, g, b, Lebenspunkte]
type AsteroidField =
    { Data: JS.Float32Array
      /// Mesh-Variante (0..Variants-1).
      Variant: JS.Uint8Array
      Capacity: int
      mutable Count: int }

[<RequireQualifiedAccess>]
module AsteroidField =

    [<Literal>]
    let Stride = 16

    /// Anzahl unterschiedlicher Asteroiden-Meshes.
    [<Literal>]
    let Variants = 4

    let create (capacity: int) : AsteroidField =
        { Data = JS.Constructors.Float32Array.Create(capacity * Stride)
          Variant = JS.Constructors.Uint8Array.Create capacity
          Capacity = capacity
          Count = 0 }

    /// Fuegt einen Asteroiden hinzu; liefert false, wenn das Feld voll ist.
    let add
        (field: AsteroidField)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (radius: float)
        (ax: float)
        (ay: float)
        (az: float)
        (spin: float)
        (phase: float)
        (r: float)
        (g: float)
        (b: float)
        (variant: int)
        : bool =
        if field.Count >= field.Capacity then
            false
        else
            let o = field.Count * Stride
            let d = field.Data
            d.[o] <- float32 x
            d.[o + 1] <- float32 y
            d.[o + 2] <- float32 z
            d.[o + 3] <- float32 vx
            d.[o + 4] <- float32 vy
            d.[o + 5] <- float32 vz
            d.[o + 6] <- float32 radius
            d.[o + 7] <- float32 ax
            d.[o + 8] <- float32 ay
            d.[o + 9] <- float32 az
            d.[o + 10] <- float32 spin
            d.[o + 11] <- float32 phase
            d.[o + 12] <- float32 r
            d.[o + 13] <- float32 g
            d.[o + 14] <- float32 b
            d.[o + 15] <- float32 (radius * Combat.AsteroidHpPerRadius)
            field.Variant.[field.Count] <- byte variant
            field.Count <- field.Count + 1
            true

    let remove (field: AsteroidField) (index: int) =
        let last = field.Count - 1

        if index <> last then
            let d = field.Data
            let src = last * Stride
            let dst = index * Stride

            for k in 0 .. Stride - 1 do
                d.[dst + k] <- d.[src + k]

            field.Variant.[index] <- field.Variant.[last]

        field.Count <- last

    /// Erzeugt das Startfeld: ein dichtes, durchfliegbares Feld vor dem Startpunkt
    /// und einen flachen Guertel um den Ursprung. Nur beim Setup/Neustart aufrufen.
    let generate (field: AsteroidField) (rng: Rng) (count: int) =
        field.Count <- 0

        for i in 0 .. count - 1 do
            let nearField = i < 90

            // Groesse: viele kleine Brocken, wenige Riesen (Riesen nur im Guertel)
            let radius =
                if nearField then
                    2.0 + 13.0 * Math.Pow(rng.NextFloat(), 2.0)
                elif rng.NextFloat() < 0.04 then
                    rng.Range(50.0, 90.0)
                else
                    2.0 + 30.0 * Math.Pow(rng.NextFloat(), 2.5)

            let mutable position =
                if nearField then
                    Vec3(0.0, 0.0, -240.0) + rng.UnitVector() * rng.Range(50.0, 300.0)
                else
                    let angle = rng.Range(0.0, 2.0 * Math.PI)
                    let distance = 60.0 + 890.0 * sqrt (rng.NextFloat())
                    let height = (rng.NextFloat() + rng.NextFloat() - 1.0) * 130.0
                    Vec3(cos angle * distance, height, sin angle * distance)

            // Startbereich des Spielers freihalten
            let clearance = 45.0 + radius

            if Vec3.length position < clearance then
                position <- Vec3.normalize (position + Vec3(0.01, 0.0, 0.0)) * (clearance + 10.0)

            let axis = rng.UnitVector()
            let shade = rng.Range(0.36, 0.62)
            let bluish = rng.NextFloat() < 0.3

            add
                field
                position.X
                position.Y
                position.Z
                0.0
                0.0
                0.0
                radius
                axis.X
                axis.Y
                axis.Z
                // Grosse Brocken drehen sich langsamer
                (rng.Range(0.05, 0.5) * 6.0 / (4.0 + radius))
                (rng.Range(0.0, 2.0 * Math.PI))
                (if bluish then shade * 0.88 else shade)
                (if bluish then shade * 0.94 else shade * 0.93)
                (if bluish then shade else shade * 0.84)
                (i % Variants)
            |> ignore

    /// Bewegt driftende Asteroiden (Bruchstuecke); die Drift klingt langsam ab.
    let update (field: AsteroidField) (dt: float) =
        let d = field.Data
        let damping = exp (-0.35 * dt)

        for i in 0 .. field.Count - 1 do
            let o = i * Stride
            let vx = float d.[o + 3]
            let vy = float d.[o + 4]
            let vz = float d.[o + 5]

            if vx <> 0.0 || vy <> 0.0 || vz <> 0.0 then
                d.[o] <- float32 (float d.[o] + vx * dt)
                d.[o + 1] <- float32 (float d.[o + 1] + vy * dt)
                d.[o + 2] <- float32 (float d.[o + 2] + vz * dt)
                d.[o + 3] <- float32 (vx * damping)
                d.[o + 4] <- float32 (vy * damping)
                d.[o + 5] <- float32 (vz * damping)

    /// Sucht den Asteroiden, der einem Schiff auf seinem Kurs am gefaehrlichsten wird
    /// (fuer die Ausweichlogik der KI). (px..pz) = Position, (fx..fz) = normalisierte
    /// Flugrichtung. Liefert die Dringlichkeit 0..1 und schreibt die normalisierte
    /// Ausweichrichtung (quer zum Kurs, weg vom Hindernis) nach out[0..2].
    let findThreat
        (field: AsteroidField)
        (px: float)
        (py: float)
        (pz: float)
        (fx: float)
        (fy: float)
        (fz: float)
        (lookAhead: float)
        (margin: float)
        (out: JS.Float32Array)
        : float =
        let d = field.Data
        let mutable best = 0.0
        let mutable bx = 0.0
        let mutable by = 0.0
        let mutable bz = 0.0

        for i in 0 .. field.Count - 1 do
            let o = i * Stride
            let dx = float d.[o] - px
            let dy = float d.[o + 1] - py
            let dz = float d.[o + 2] - pz
            let safe = float d.[o + 6] + margin
            // Abstand entlang des Kurses bis zum naechsten Punkt am Asteroiden
            let along = dx * fx + dy * fy + dz * fz

            if along > -safe && along < lookAhead + safe then
                // Seitlicher Versatz des Asteroidenmittelpunkts zur Kurslinie
                let lx = dx - fx * along
                let ly = dy - fy * along
                let lz = dz - fz * along
                let lateralSq = lx * lx + ly * ly + lz * lz

                if lateralSq < safe * safe then
                    // Je naeher und je zentraler, desto dringlicher
                    let closeness = 1.0 - Math.Max(0.0, along - safe) / lookAhead
                    let centered = 1.0 - sqrt lateralSq / safe
                    let urgency = closeness * (0.35 + 0.65 * centered)

                    if urgency > best then
                        best <- urgency
                        let lateral = sqrt lateralSq

                        if lateral > 1e-3 then
                            bx <- -lx / lateral
                            by <- -ly / lateral
                            bz <- -lz / lateral
                        else
                            // Genau auf Kollisionskurs: irgendeine Querrichtung waehlen
                            let cx = fy * 0.0 - fz * 1.0
                            let cy = fz * 0.0 - fx * 0.0
                            let cz = fx * 1.0 - fy * 0.0
                            let cl = sqrt (cx * cx + cy * cy + cz * cz)

                            if cl > 1e-3 then
                                bx <- cx / cl
                                by <- cy / cl
                                bz <- cz / cl
                            else
                                bx <- 1.0
                                by <- 0.0
                                bz <- 0.0

        out.[0] <- float32 bx
        out.[1] <- float32 by
        out.[2] <- float32 bz
        best

// -------------------------------------------------------------------------------------
//  Welt
// -------------------------------------------------------------------------------------

/// Kurzlebige Point Lights fuer Explosionen. Pro Licht:
/// [x, y, z, Reichweite, r, g, b, Intensitaet]
module Flashes =
    [<Literal>]
    let Slots = 4

    [<Literal>]
    let Stride = 8

type World =
    { Player: Ship
      /// Feste Anzahl Plaetze; unbenutzte haben Alive = false.
      Enemies: Ship[]
      Lasers: LaserPool
      Particles: ParticlePool
      Asteroids: AsteroidField
      Flashes: JS.Float32Array
      /// Ereignisse seit dem letzten Leeren (die Liste behaelt ihre Kapazitaet).
      Events: ResizeArray<GameEvent>
      Rng: Rng
      mutable Time: float }

[<RequireQualifiedAccess>]
module World =

    let create (maxEnemies: int) : World =
        { Player = Ship.createPlayer ()
          Enemies = Array.init maxEnemies (fun _ -> Ship.createInterceptor ())
          Lasers = LaserPool.create 512
          Particles = ParticlePool.create 4096
          Asteroids = AsteroidField.create 768
          Flashes = JS.Constructors.Float32Array.Create(Flashes.Slots * Flashes.Stride)
          Events = ResizeArray<GameEvent>(64)
          Rng = Rng(20261117)
          Time = 0.0 }

    /// Setzt die Welt fuer ein neues Spiel zurueck: Pools leeren (nur die Zaehler -
    /// der Speicher bleibt), Gegner entfernen, neues Asteroidenfeld erzeugen.
    /// Der Zufallsgenerator laeuft weiter, jedes Spiel bekommt also ein anderes Feld.
    let reset (world: World) (asteroidCount: int) =
        world.Lasers.Count <- 0
        world.Particles.Count <- 0
        world.Events.Clear()

        for i in 0 .. world.Flashes.length - 1 do
            world.Flashes.[i] <- 0.0f

        for i in 0 .. world.Enemies.Length - 1 do
            world.Enemies.[i].Alive <- false

        AsteroidField.generate world.Asteroids world.Rng asteroidCount
        Ship.reset world.Player Vec3.zero Quat.identity

// -------------------------------------------------------------------------------------
//  Zielvorhalt
// -------------------------------------------------------------------------------------

module Ballistics =

    /// Zeit bis ein Projektil mit Geschwindigkeit `speed` ein Ziel trifft, das sich
    /// relativ zum Schuetzen bei (px, py, pz) befindet und mit (vx, vy, vz) bewegt.
    /// Loest |P + V t| = speed * t; liefert -1, wenn das Ziel nicht einholbar ist.
    /// Der Vorhaltepunkt ist dann Zielposition + Zielgeschwindigkeit * t.
    let interceptTime
        (px: float)
        (py: float)
        (pz: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (speed: float)
        : float =
        let a = vx * vx + vy * vy + vz * vz - speed * speed
        let b = 2.0 * (px * vx + py * vy + pz * vz)
        let c = px * px + py * py + pz * pz

        if abs a < 1e-6 then
            // Ziel so schnell wie das Projektil: lineare Gleichung
            if abs b < 1e-9 then -1.0
            else
                let t = -c / b
                if t > 0.0 then t else -1.0
        else
            let discriminant = b * b - 4.0 * a * c

            if discriminant < 0.0 then
                -1.0
            else
                let root = sqrt discriminant
                let t1 = (-b - root) / (2.0 * a)
                let t2 = (-b + root) / (2.0 * a)
                let lo = Math.Min(t1, t2)
                let hi = Math.Max(t1, t2)

                if lo > 0.0 then lo
                elif hi > 0.0 then hi
                else -1.0

// -------------------------------------------------------------------------------------
//  Kollisionstests (reine Skalar-Mathematik)
// -------------------------------------------------------------------------------------

module Collision =

    // Scratch-Speicher: einmal angelegt, von den Tests wiederverwendet.
    let private local = JS.Constructors.Float32Array.Create 6
    let private axesA = JS.Constructors.Float32Array.Create 9
    let private axesB = JS.Constructors.Float32Array.Create 9
    let private rot = JS.Constructors.Float32Array.Create 9
    let private absRot = JS.Constructors.Float32Array.Create 9

    /// Strecke (x0,y0,z0)-(x1,y1,z1) gegen Kugel. Liefert den Streckenparameter
    /// t in [0, 1] des ersten Treffers oder -1. Beginnt die Strecke in der Kugel, ist t = 0.
    let segmentSphere
        (x0: float)
        (y0: float)
        (z0: float)
        (x1: float)
        (y1: float)
        (z1: float)
        (cx: float)
        (cy: float)
        (cz: float)
        (radius: float)
        : float =
        let dx = x1 - x0
        let dy = y1 - y0
        let dz = z1 - z0
        let mx = x0 - cx
        let my = y0 - cy
        let mz = z0 - cz
        let c = mx * mx + my * my + mz * mz - radius * radius

        if c <= 0.0 then
            0.0
        else
            let b = mx * dx + my * dy + mz * dz

            // Startpunkt ausserhalb und Bewegung von der Kugel weg
            if b >= 0.0 then
                -1.0
            else
                let a = dx * dx + dy * dy + dz * dz
                let discriminant = b * b - a * c

                if discriminant < 0.0 || a < 1e-12 then
                    -1.0
                else
                    let t = (-b - sqrt discriminant) / a
                    if t <= 1.0 then t else -1.0

    /// Strecke gegen achsenparallele Box um den Ursprung (Slab-Test).
    /// Liefert t in [0, 1] des Eintritts oder -1.
    let segmentBox
        (x0: float)
        (y0: float)
        (z0: float)
        (x1: float)
        (y1: float)
        (z1: float)
        (hx: float)
        (hy: float)
        (hz: float)
        : float =
        let mutable tMin = 0.0
        let mutable tMax = 1.0
        let mutable hit = true

        // X-Slab
        let dx = x1 - x0

        if abs dx < 1e-9 then
            if abs x0 > hx then
                hit <- false
        else
            let inv = 1.0 / dx
            let ta = (-hx - x0) * inv
            let tb = (hx - x0) * inv
            tMin <- Math.Max(tMin, Math.Min(ta, tb))
            tMax <- Math.Min(tMax, Math.Max(ta, tb))

        // Y-Slab
        let dy = y1 - y0

        if abs dy < 1e-9 then
            if abs y0 > hy then
                hit <- false
        else
            let inv = 1.0 / dy
            let ta = (-hy - y0) * inv
            let tb = (hy - y0) * inv
            tMin <- Math.Max(tMin, Math.Min(ta, tb))
            tMax <- Math.Min(tMax, Math.Max(ta, tb))

        // Z-Slab
        let dz = z1 - z0

        if abs dz < 1e-9 then
            if abs z0 > hz then
                hit <- false
        else
            let inv = 1.0 / dz
            let ta = (-hz - z0) * inv
            let tb = (hz - z0) * inv
            tMin <- Math.Max(tMin, Math.Min(ta, tb))
            tMax <- Math.Min(tMax, Math.Max(ta, tb))

        if hit && tMin <= tMax then tMin else -1.0

    /// Rechnet einen Weltpunkt in das OBB-System eines Schiffs um (Ursprung =
    /// Boxmittelpunkt, Achsen = Schiffsachsen) und schreibt ihn nach out[offset..].
    let private toObbSpace (ship: Ship) (x: float) (y: float) (z: float) (out: JS.Float32Array) (offset: int) =
        let vx = x - ship.Position.X
        let vy = y - ship.Position.Y
        let vz = z - ship.Position.Z
        // Inverse Rotation = Rotation mit der konjugierten Quaternion
        let qx = -ship.Rotation.X
        let qy = -ship.Rotation.Y
        let qz = -ship.Rotation.Z
        let qw = ship.Rotation.W
        let tx = 2.0 * (qy * vz - qz * vy)
        let ty = 2.0 * (qz * vx - qx * vz)
        let tz = 2.0 * (qx * vy - qy * vx)
        out.[offset] <- float32 (vx + qw * tx + (qy * tz - qz * ty) - ship.ObbCenter.X)
        out.[offset + 1] <- float32 (vy + qw * ty + (qz * tx - qx * tz) - ship.ObbCenter.Y)
        out.[offset + 2] <- float32 (vz + qw * tz + (qx * ty - qy * tx) - ship.ObbCenter.Z)

    /// Strecke gegen die OBB eines Schiffs (um `padding` vergroessert).
    /// Liefert t in [0, 1] oder -1.
    let segmentObb
        (ship: Ship)
        (x0: float)
        (y0: float)
        (z0: float)
        (x1: float)
        (y1: float)
        (z1: float)
        (padding: float)
        : float =
        toObbSpace ship x0 y0 z0 local 0
        toObbSpace ship x1 y1 z1 local 3

        segmentBox
            (float local.[0])
            (float local.[1])
            (float local.[2])
            (float local.[3])
            (float local.[4])
            (float local.[5])
            (ship.ObbHalf.X + padding)
            (ship.ObbHalf.Y + padding)
            (ship.ObbHalf.Z + padding)

    /// Kugel gegen die OBB eines Schiffs. Liefert die Eindringtiefe (> 0 bei
    /// Ueberlappung, sonst -1) und schreibt die Stossnormale in Weltkoordinaten
    /// (von der Kugel zum Schiff) nach out[0..2].
    let sphereObb (ship: Ship) (cx: float) (cy: float) (cz: float) (radius: float) (out: JS.Float32Array) : float =
        toObbSpace ship cx cy cz local 0
        let lx = float local.[0]
        let ly = float local.[1]
        let lz = float local.[2]
        let hx = ship.ObbHalf.X
        let hy = ship.ObbHalf.Y
        let hz = ship.ObbHalf.Z
        // Naechster Punkt der Box zum Kugelmittelpunkt
        let qx = Math.Max(-hx, Math.Min(hx, lx))
        let qy = Math.Max(-hy, Math.Min(hy, ly))
        let qz = Math.Max(-hz, Math.Min(hz, lz))
        let dx = qx - lx
        let dy = qy - ly
        let dz = qz - lz
        let distSq = dx * dx + dy * dy + dz * dz

        if distSq >= radius * radius then
            -1.0
        else
            let dist = sqrt distSq
            let mutable nx = 0.0
            let mutable ny = 1.0
            let mutable nz = 0.0

            if dist > 1e-6 then
                nx <- dx / dist
                ny <- dy / dist
                nz <- dz / dist
            else
                // Kugelmittelpunkt liegt in der Box: vom Mittelpunkt weg zur Boxmitte
                let len = sqrt (lx * lx + ly * ly + lz * lz)

                if len > 1e-6 then
                    nx <- -lx / len
                    ny <- -ly / len
                    nz <- -lz / len

            // Normale zurueck in Weltkoordinaten drehen
            let rx = ship.Rotation.X
            let ry = ship.Rotation.Y
            let rz = ship.Rotation.Z
            let rw = ship.Rotation.W
            let tx = 2.0 * (ry * nz - rz * ny)
            let ty = 2.0 * (rz * nx - rx * nz)
            let tz = 2.0 * (rx * ny - ry * nx)
            out.[0] <- float32 (nx + rw * tx + (ry * tz - rz * ty))
            out.[1] <- float32 (ny + rw * ty + (rz * tx - rx * tz))
            out.[2] <- float32 (nz + rw * tz + (rx * ty - ry * tx))
            radius - dist

    /// Schreibt die drei Achsen einer Orientierung zeilenweise nach out[0..8].
    let private writeAxes (q: Quat) (out: JS.Float32Array) =
        let x = q.X
        let y = q.Y
        let z = q.Z
        let w = q.W
        out.[0] <- float32 (1.0 - 2.0 * (y * y + z * z))
        out.[1] <- float32 (2.0 * (x * y + w * z))
        out.[2] <- float32 (2.0 * (x * z - w * y))
        out.[3] <- float32 (2.0 * (x * y - w * z))
        out.[4] <- float32 (1.0 - 2.0 * (x * x + z * z))
        out.[5] <- float32 (2.0 * (y * z + w * x))
        out.[6] <- float32 (2.0 * (x * z + w * y))
        out.[7] <- float32 (2.0 * (y * z - w * x))
        out.[8] <- float32 (1.0 - 2.0 * (x * x + y * y))

    /// OBB gegen OBB nach dem Separating Axis Theorem: Zwei konvexe Koerper
    /// ueberlappen genau dann, wenn auf KEINER der 15 Kandidatenachsen (3 + 3
    /// Flaechennormalen, 9 Kantenkreuzprodukte) eine Luecke zwischen ihren
    /// Projektionen liegt.
    let obbObb (a: Ship) (b: Ship) : bool =
        writeAxes a.Rotation axesA
        writeAxes b.Rotation axesB
        let ea0 = a.ObbHalf.X
        let ea1 = a.ObbHalf.Y
        let ea2 = a.ObbHalf.Z
        let eb0 = b.ObbHalf.X
        let eb1 = b.ObbHalf.Y
        let eb2 = b.ObbHalf.Z

        // Boxmittelpunkte in Weltkoordinaten: Position + R * ObbCenter
        let acx =
            a.Position.X
            + float axesA.[0] * a.ObbCenter.X
            + float axesA.[3] * a.ObbCenter.Y
            + float axesA.[6] * a.ObbCenter.Z

        let acy =
            a.Position.Y
            + float axesA.[1] * a.ObbCenter.X
            + float axesA.[4] * a.ObbCenter.Y
            + float axesA.[7] * a.ObbCenter.Z

        let acz =
            a.Position.Z
            + float axesA.[2] * a.ObbCenter.X
            + float axesA.[5] * a.ObbCenter.Y
            + float axesA.[8] * a.ObbCenter.Z

        let bcx =
            b.Position.X
            + float axesB.[0] * b.ObbCenter.X
            + float axesB.[3] * b.ObbCenter.Y
            + float axesB.[6] * b.ObbCenter.Z

        let bcy =
            b.Position.Y
            + float axesB.[1] * b.ObbCenter.X
            + float axesB.[4] * b.ObbCenter.Y
            + float axesB.[7] * b.ObbCenter.Z

        let bcz =
            b.Position.Z
            + float axesB.[2] * b.ObbCenter.X
            + float axesB.[5] * b.ObbCenter.Y
            + float axesB.[8] * b.ObbCenter.Z

        // rot[i*3+j] = Achse i von A . Achse j von B  (B ausgedrueckt im System von A)
        for i in 0..2 do
            for j in 0..2 do
                let r =
                    float axesA.[i * 3] * float axesB.[j * 3]
                    + float axesA.[i * 3 + 1] * float axesB.[j * 3 + 1]
                    + float axesA.[i * 3 + 2] * float axesB.[j * 3 + 2]

                rot.[i * 3 + j] <- float32 r
                // Epsilon faengt nahezu parallele Kanten ab (Kreuzprodukt ~ 0)
                absRot.[i * 3 + j] <- float32 (abs r + 1e-5)

        // Verbindungsvektor der Mittelpunkte im System von A
        let dx = bcx - acx
        let dy = bcy - acy
        let dz = bcz - acz
        let t0 = dx * float axesA.[0] + dy * float axesA.[1] + dz * float axesA.[2]
        let t1 = dx * float axesA.[3] + dy * float axesA.[4] + dz * float axesA.[5]
        let t2 = dx * float axesA.[6] + dy * float axesA.[7] + dz * float axesA.[8]

        let r00 = float rot.[0]
        let r01 = float rot.[1]
        let r02 = float rot.[2]
        let r10 = float rot.[3]
        let r11 = float rot.[4]
        let r12 = float rot.[5]
        let r20 = float rot.[6]
        let r21 = float rot.[7]
        let r22 = float rot.[8]
        let a00 = float absRot.[0]
        let a01 = float absRot.[1]
        let a02 = float absRot.[2]
        let a10 = float absRot.[3]
        let a11 = float absRot.[4]
        let a12 = float absRot.[5]
        let a20 = float absRot.[6]
        let a21 = float absRot.[7]
        let a22 = float absRot.[8]

        // Achsen von A
        if abs t0 > ea0 + eb0 * a00 + eb1 * a01 + eb2 * a02 then false
        elif abs t1 > ea1 + eb0 * a10 + eb1 * a11 + eb2 * a12 then false
        elif abs t2 > ea2 + eb0 * a20 + eb1 * a21 + eb2 * a22 then false
        // Achsen von B
        elif abs (t0 * r00 + t1 * r10 + t2 * r20) > ea0 * a00 + ea1 * a10 + ea2 * a20 + eb0 then false
        elif abs (t0 * r01 + t1 * r11 + t2 * r21) > ea0 * a01 + ea1 * a11 + ea2 * a21 + eb1 then false
        elif abs (t0 * r02 + t1 * r12 + t2 * r22) > ea0 * a02 + ea1 * a12 + ea2 * a22 + eb2 then false
        // Kreuzprodukte A0 x B0, A0 x B1, A0 x B2
        elif abs (t2 * r10 - t1 * r20) > ea1 * a20 + ea2 * a10 + eb1 * a02 + eb2 * a01 then false
        elif abs (t2 * r11 - t1 * r21) > ea1 * a21 + ea2 * a11 + eb0 * a02 + eb2 * a00 then false
        elif abs (t2 * r12 - t1 * r22) > ea1 * a22 + ea2 * a12 + eb0 * a01 + eb1 * a00 then false
        // A1 x B0, A1 x B1, A1 x B2
        elif abs (t0 * r20 - t2 * r00) > ea0 * a20 + ea2 * a00 + eb1 * a12 + eb2 * a11 then false
        elif abs (t0 * r21 - t2 * r01) > ea0 * a21 + ea2 * a01 + eb0 * a12 + eb2 * a10 then false
        elif abs (t0 * r22 - t2 * r02) > ea0 * a22 + ea2 * a02 + eb0 * a11 + eb1 * a10 then false
        // A2 x B0, A2 x B1, A2 x B2
        elif abs (t1 * r00 - t0 * r10) > ea0 * a10 + ea1 * a00 + eb1 * a22 + eb2 * a21 then false
        elif abs (t1 * r01 - t0 * r11) > ea0 * a11 + ea1 * a01 + eb0 * a22 + eb2 * a20 then false
        elif abs (t1 * r02 - t0 * r12) > ea0 * a12 + ea1 * a02 + eb0 * a21 + eb1 * a20 then false
        else true

// -------------------------------------------------------------------------------------
//  Effekte: Partikel-Explosionen und Lichtblitze (alles aus den Pools)
// -------------------------------------------------------------------------------------

module Effects =

    let private direction = JS.Constructors.Float32Array.Create 3

    /// Gleichverteilte Zufallsrichtung nach direction[0..2] (ohne Vec3-Objekt).
    let private randomDirection (rng: Rng) =
        let z = rng.Range(-1.0, 1.0)
        let angle = rng.Range(0.0, 2.0 * Math.PI)
        let r = sqrt (1.0 - z * z)
        direction.[0] <- float32 (r * cos angle)
        direction.[1] <- float32 (r * sin angle)
        direction.[2] <- float32 z

    /// Wirft `count` Partikel aus einem Punkt (mit `spread` als Streuradius) in
    /// zufaellige Richtungen; (vx, vy, vz) ist die mitgegebene Grundgeschwindigkeit.
    let burst
        (world: World)
        (kind: int)
        (count: int)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (spread: float)
        (speedMin: float)
        (speedMax: float)
        (lifeMin: float)
        (lifeMax: float)
        (sizeMin: float)
        (sizeMax: float)
        (r: float)
        (g: float)
        (b: float)
        =
        let rng = world.Rng

        for _ in 1..count do
            randomDirection rng
            let dx = float direction.[0]
            let dy = float direction.[1]
            let dz = float direction.[2]
            let speed = rng.Range(speedMin, speedMax)
            let offset = spread * rng.NextFloat()

            ParticlePool.spawn
                world.Particles
                kind
                (x + dx * offset)
                (y + dy * offset)
                (z + dz * offset)
                (vx + dx * speed)
                (vy + dy * speed)
                (vz + dz * speed)
                (rng.Range(lifeMin, lifeMax))
                (rng.Range(sizeMin, sizeMax))
                r
                g
                b

    /// Setzt einen Lichtblitz in den schwaechsten der festen Licht-Plaetze.
    let flash
        (world: World)
        (x: float)
        (y: float)
        (z: float)
        (range: float)
        (r: float)
        (g: float)
        (b: float)
        (intensity: float)
        =
        let f = world.Flashes
        let mutable slot = 0
        let mutable weakest = Double.MaxValue

        for i in 0 .. Flashes.Slots - 1 do
            let current = float f.[i * Flashes.Stride + 7]

            if current < weakest then
                weakest <- current
                slot <- i

        if intensity > weakest then
            let o = slot * Flashes.Stride
            f.[o] <- float32 x
            f.[o + 1] <- float32 y
            f.[o + 2] <- float32 z
            f.[o + 3] <- float32 range
            f.[o + 4] <- float32 r
            f.[o + 5] <- float32 g
            f.[o + 6] <- float32 b
            f.[o + 7] <- float32 intensity

    /// Lichtblitze klingen exponentiell ab.
    let updateFlashes (world: World) (dt: float) =
        let f = world.Flashes
        let decay = exp (-5.0 * dt)

        for i in 0 .. Flashes.Slots - 1 do
            let o = i * Flashes.Stride + 7
            let v = float f.[o] * decay
            f.[o] <- float32 (if v < 0.02 then 0.0 else v)

    /// Kleiner Funkenregen an einem Einschlagpunkt.
    let sparks (world: World) (x: float) (y: float) (z: float) (count: int) (r: float) (g: float) (b: float) =
        burst world ParticleKind.Glow count x y z 0.0 0.0 0.0 0.2 8.0 34.0 0.15 0.45 0.10 0.26 r g b

    /// Explosion eines Schiffs: Feuerball, Funken, Truemmer, Lichtblitz.
    let shipExplosion (world: World) (x: float) (y: float) (z: float) (vx: float) (vy: float) (vz: float) (size: float) =
        // Feuerball: grosse, langsame Leuchtpunkte
        burst world ParticleKind.Glow 26 x y z vx vy vz (size * 0.6) 2.0 14.0 0.5 1.2 (size * 0.5) (size * 1.2) 1.0 0.5 0.14
        // Heisser Kern
        burst world ParticleKind.Glow 8 x y z vx vy vz (size * 0.2) 0.5 4.0 0.25 0.5 (size * 0.8) (size * 1.5) 1.6 1.4 1.0
        // Funken
        burst world ParticleKind.Glow 70 x y z vx vy vz 0.3 25.0 85.0 0.5 1.4 0.12 0.3 1.4 1.0 0.5
        // Wrackteile
        burst world ParticleKind.Debris 22 x y z vx vy vz (size * 0.4) 8.0 40.0 1.5 3.5 0.2 0.6 0.32 0.32 0.36
        flash world x y z (size * 22.0) 1.0 0.62 0.25 7.0

    /// Zerplatzen eines Asteroiden: Staubwolke, Gesteinssplitter, schwacher Blitz.
    let rockBurst
        (world: World)
        (x: float)
        (y: float)
        (z: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (radius: float)
        (r: float)
        (g: float)
        (b: float)
        =
        let scale = Math.Min(radius, 30.0)
        let dust = 8 + int (scale * 1.2)
        let chips = 6 + int (scale * 1.5)

        burst
            world
            ParticleKind.Glow
            dust
            x
            y
            z
            vx
            vy
            vz
            (radius * 0.7)
            1.0
            (6.0 + scale * 0.6)
            0.6
            1.5
            (radius * 0.25)
            (radius * 0.6)
            (r * 0.4)
            (g * 0.36)
            (b * 0.32)

        burst world ParticleKind.Glow 14 x y z vx vy vz (radius * 0.3) 15.0 50.0 0.3 0.8 0.10 0.26 1.3 0.8 0.35

        burst
            world
            ParticleKind.Debris
            chips
            x
            y
            z
            vx
            vy
            vz
            (radius * 0.6)
            5.0
            (16.0 + scale)
            1.5
            3.5
            (0.15 + radius * 0.04)
            (0.35 + radius * 0.10)
            r
            g
            b

        flash world x y z (20.0 + radius * 4.0) 1.0 0.7 0.4 (1.5 + scale * 0.12)

// -------------------------------------------------------------------------------------
//  Schaden
// -------------------------------------------------------------------------------------

module Damage =

    /// Schaden an einem Schiff: erst der Schild, der Rest geht auf die Huelle.
    let ship (world: World) (target: Ship) (amount: float) =
        if target.Alive && amount > 0.0 then
            let absorbed = Math.Min(target.Shield, amount)
            target.Shield <- target.Shield - absorbed
            target.Hull <- target.Hull - (amount - absorbed)
            target.ShieldDelay <- Combat.ShieldRegenDelay
            target.HitFlash <- 1.0

            if absorbed > 0.0 then
                target.ShieldFlash <- 1.0

            world.Events.Add(ShipHit(target.Team, amount, absorbed > 0.0))

            if target.Hull <= 0.0 then
                target.Hull <- 0.0
                target.Alive <- false
                target.ShieldFlash <- 0.0
                // Ein totes Schiff bewegt sich nicht mehr: Interpolation einfrieren
                target.PrevPosition <- target.Position
                target.PrevRotation <- target.Rotation
                let p = target.Position
                let v = target.Velocity
                Effects.shipExplosion world p.X p.Y p.Z (v.X * 0.5) (v.Y * 0.5) (v.Z * 0.5) (if target.Team = Team.Player then target.Radius * 0.8 else target.Radius)
                world.Events.Add(ShipDestroyed(target.Team, p))
                // Das Wrack steht still (sonst zoege der Raumstaub weiter Streifen)
                target.Velocity <- Vec3.zero

    /// Schaden an Asteroid `index`. Bei 0 Lebenspunkten zerplatzt er; grosse
    /// Brocken zerfallen dabei in 2-3 Bruchstuecke, die aus dem Pool kommen.
    /// Achtung: Zerstoerung ordnet das Feld um (swap-remove) und haengt hinten an.
    let asteroid (world: World) (index: int) (amount: float) (byTeam: Team) =
        let field = world.Asteroids
        let d = field.Data
        let o = index * AsteroidField.Stride
        let hp = float d.[o + 15] - amount

        if hp > 0.0 then
            d.[o + 15] <- float32 hp
        else
            let rng = world.Rng
            let x = float d.[o]
            let y = float d.[o + 1]
            let z = float d.[o + 2]
            let vx = float d.[o + 3]
            let vy = float d.[o + 4]
            let vz = float d.[o + 5]
            let radius = float d.[o + 6]
            let r = float d.[o + 12]
            let g = float d.[o + 13]
            let b = float d.[o + 14]
            // Erst entfernen - das schafft sicher Platz fuer die Bruchstuecke
            AsteroidField.remove field index
            Effects.rockBurst world x y z vx vy vz radius r g b

            if radius >= Combat.SplitRadius then
                let pieces = if rng.NextFloat() < 0.5 then 2 else 3

                for _ in 1..pieces do
                    // Zufallsrichtung skalar erzeugen (kein Vec3 im Spielbetrieb)
                    let dz = rng.Range(-1.0, 1.0)
                    let angle = rng.Range(0.0, 2.0 * Math.PI)
                    let dr = sqrt (1.0 - dz * dz)
                    let dx = dr * cos angle
                    let dy = dr * sin angle
                    let pieceRadius = radius * rng.Range(0.45, 0.62)
                    let speed = rng.Range(5.0, 14.0)
                    // Neue Drehachse: senkrecht zur Flugrichtung des Bruchstuecks
                    let ax = dy
                    let ay = -dx
                    let az = 0.0
                    let al = Math.Max(1e-3, sqrt (ax * ax + ay * ay))

                    AsteroidField.add
                        field
                        (x + dx * radius * 0.4)
                        (y + dy * radius * 0.4)
                        (z + dz * radius * 0.4)
                        (vx + dx * speed)
                        (vy + dy * speed)
                        (vz + dz * speed)
                        pieceRadius
                        (ax / al)
                        (ay / al)
                        az
                        (rng.Range(0.3, 1.4))
                        (rng.Range(0.0, 2.0 * Math.PI))
                        r
                        g
                        b
                        (int (rng.NextFloat() * float AsteroidField.Variants) % AsteroidField.Variants)
                    |> ignore

            world.Events.Add(AsteroidDestroyed(radius, byTeam, Vec3(x, y, z)))

// -------------------------------------------------------------------------------------
//  Simulation
// -------------------------------------------------------------------------------------

[<RequireQualifiedAccess>]
module Physics =

    let private normal = JS.Constructors.Float32Array.Create 3

    /// Feuert aus beiden Kanonen je einen Bolt (aus dem Pool).
    let private fire (world: World) (ship: Ship) =
        let q = ship.Rotation
        let forward = Quat.forward q
        let vx = forward.X * Combat.LaserSpeed + ship.Velocity.X
        let vy = forward.Y * Combat.LaserSpeed + ship.Velocity.Y
        let vz = forward.Z * Combat.LaserSpeed + ship.Velocity.Z
        let left = ship.Position + Quat.rotateVec3 q ship.GunLeft
        let right = ship.Position + Quat.rotateVec3 q ship.GunRight
        let dmg = if ship.Overdrive > 0.0 then ship.LaserDamage * 1.3 else ship.LaserDamage

        LaserPool.spawn world.Lasers left.X left.Y left.Z vx vy vz q.X q.Y q.Z q.W dmg ship.Team
        |> ignore

        LaserPool.spawn world.Lasers right.X right.Y right.Z vx vy vz q.X q.Y q.Z q.W dmg ship.Team
        |> ignore

        world.Events.Add(LaserFired(ship.Team, ship.Position))

    /// Flugmodell, Bewaffnung und Schildregeneration eines Schiffs.
    let private stepShip (world: World) (ship: Ship) (dt: float) =
        if ship.Alive then
            Flight.step ship dt

            if ship.Overdrive > 0.0 then
                ship.Overdrive <- Math.Max(0.0, ship.Overdrive - dt)

            ship.FireCooldown <- ship.FireCooldown - dt

            if ship.Controls.Fire && ship.FireCooldown <= 0.0 then
                fire world ship
                let interval = if ship.Overdrive > 0.0 then ship.FireInterval * 0.62 else ship.FireInterval
                ship.FireCooldown <- interval
            elif ship.FireCooldown < 0.0 then
                ship.FireCooldown <- 0.0

            if ship.ShieldDelay > 0.0 then
                ship.ShieldDelay <- ship.ShieldDelay - dt
            elif ship.Shield < ship.MaxShield then
                ship.Shield <- Math.Min(ship.MaxShield, ship.Shield + Combat.ShieldRegenRate * dt)

            // Partikelspuren bei kritischer Huelle (Funken/Feuer) oder aktivem Boost
            ship.TrailTimer <- ship.TrailTimer - dt

            if ship.TrailTimer <= 0.0 then
                ship.TrailTimer <- 0.045
                let hullRatio = ship.Hull / ship.MaxHull

                if hullRatio < 0.42 then
                    let p = ship.Position + Quat.rotateVec3 ship.Rotation (Vec3(0.0, 0.1, 1.4))
                    let v = ship.Velocity
                    Effects.burst world ParticleKind.Glow 2 p.X p.Y p.Z (v.X * 0.35) (v.Y * 0.35) (v.Z * 0.35) 0.5 3.0 11.0 0.25 0.55 0.18 0.42 1.2 0.45 0.12

                if ship.Team = Team.Player && ship.Boost > 0.45 then
                    let pL = ship.Position + Quat.rotateVec3 ship.Rotation ShipHardpoints.playerEngineLeft
                    let pR = ship.Position + Quat.rotateVec3 ship.Rotation ShipHardpoints.playerEngineRight
                    let v = ship.Velocity
                    Effects.burst world ParticleKind.Glow 1 pL.X pL.Y pL.Z (v.X * 0.2) (v.Y * 0.2) (v.Z * 0.2) 0.15 2.0 8.0 0.12 0.26 0.12 0.24 0.25 0.85 1.4
                    Effects.burst world ParticleKind.Glow 1 pR.X pR.Y pR.Z (v.X * 0.2) (v.Y * 0.2) (v.Z * 0.2) 0.15 2.0 8.0 0.12 0.26 0.12 0.24 0.25 0.85 1.4

        if ship.HitFlash > 0.0 then
            ship.HitFlash <- Math.Max(0.0, ship.HitFlash - dt * 6.0)

        if ship.ShieldFlash > 0.0 then
            ship.ShieldFlash <- Math.Max(0.0, ship.ShieldFlash - dt * 3.2)

    /// Prueft einen Bolt (Strecke p0-p1) gegen ein Schiff: erst die Huellkugel,
    /// dann die OBB. Liefert t in [0, 1] oder -1.
    let private laserVsShip
        (ship: Ship)
        (x0: float)
        (y0: float)
        (z0: float)
        (x1: float)
        (y1: float)
        (z1: float)
        : float =
        if not ship.Alive then
            -1.0
        elif
            Collision.segmentSphere x0 y0 z0 x1 y1 z1 ship.Position.X ship.Position.Y ship.Position.Z ship.Radius < 0.0
        then
            -1.0
        else
            Collision.segmentObb ship x0 y0 z0 x1 y1 z1 0.15

    /// Bewegt alle Bolts und wertet Treffer aus (Asteroiden, gegnerische Schiffe).
    let private updateLasers (world: World) (dt: float) =
        let pool = world.Lasers
        let d = pool.Data
        let field = world.Asteroids
        let a = field.Data
        let enemies = world.Enemies
        let mutable i = 0

        while i < pool.Count do
            let o = i * LaserPool.Stride
            let x0 = float d.[o]
            let y0 = float d.[o + 1]
            let z0 = float d.[o + 2]
            let x1 = x0 + float d.[o + 3] * dt
            let y1 = y0 + float d.[o + 4] * dt
            let z1 = z0 + float d.[o + 5] * dt
            let team = int pool.Team.[i]
            let damage = float d.[o + 11]

            // Fruehesten Treffer entlang der Strecke suchen
            let mutable hitT = 2.0
            let mutable hitAsteroid = -1
            let mutable hitShip = -2 // -2 = keiner, -1 = Spieler, >= 0 = Gegner-Index

            for k in 0 .. field.Count - 1 do
                let ao = k * AsteroidField.Stride

                let t =
                    Collision.segmentSphere
                        x0
                        y0
                        z0
                        x1
                        y1
                        z1
                        (float a.[ao])
                        (float a.[ao + 1])
                        (float a.[ao + 2])
                        (float a.[ao + 6])

                if t >= 0.0 && t < hitT then
                    hitT <- t
                    hitAsteroid <- k

            if team = int Team.Player then
                for k in 0 .. enemies.Length - 1 do
                    let t = laserVsShip enemies.[k] x0 y0 z0 x1 y1 z1

                    if t >= 0.0 && t < hitT then
                        hitT <- t
                        hitAsteroid <- -1
                        hitShip <- k
            else
                let t = laserVsShip world.Player x0 y0 z0 x1 y1 z1

                if t >= 0.0 && t < hitT then
                    hitT <- t
                    hitAsteroid <- -1
                    hitShip <- -1

            if hitT <= 1.0 then
                let hx = x0 + (x1 - x0) * hitT
                let hy = y0 + (y1 - y0) * hitT
                let hz = z0 + (z1 - z0) * hitT

                if hitAsteroid >= 0 then
                    Effects.sparks world hx hy hz 5 1.2 0.8 0.4
                    Damage.asteroid world hitAsteroid damage (enum<Team> team)
                else
                    let target = if hitShip = -1 then world.Player else enemies.[hitShip]

                    // Schildtreffer funken blau, Huellentreffer orange
                    if target.Shield > 0.0 then
                        Effects.sparks world hx hy hz 7 0.4 0.8 1.5
                    else
                        Effects.sparks world hx hy hz 9 1.5 0.8 0.3

                    Effects.flash world hx hy hz 14.0 1.0 0.7 0.4 1.6
                    Damage.ship world target damage

                LaserPool.kill pool i
            // i bleibt stehen: auf dem Platz liegt jetzt ein anderer Bolt
            else
                let life = float d.[o + 10] - dt

                if life <= 0.0 then
                    LaserPool.kill pool i
                else
                    d.[o] <- float32 x1
                    d.[o + 1] <- float32 y1
                    d.[o + 2] <- float32 z1
                    d.[o + 10] <- float32 life
                    i <- i + 1

    /// Schiff gegen alle Asteroiden: Kugel gegen OBB, Herausschieben, Abprallen,
    /// Schaden nach Aufprallgeschwindigkeit. Laeuft rueckwaerts, weil ein
    /// zerplatzender Asteroid das Feld umordnet.
    let private collideShipAsteroids (world: World) (ship: Ship) =
        if ship.Alive then
            let field = world.Asteroids
            let a = field.Data
            let mutable k = field.Count - 1

            while k >= 0 do
                let o = k * AsteroidField.Stride
                let radius = float a.[o + 6]
                let dx = float a.[o] - ship.Position.X
                let dy = float a.[o + 1] - ship.Position.Y
                let dz = float a.[o + 2] - ship.Position.Z
                let reach = radius + ship.Radius

                // Broad Phase: Huellkugeln
                if dx * dx + dy * dy + dz * dz < reach * reach then
                    let depth = Collision.sphereObb ship (float a.[o]) (float a.[o + 1]) (float a.[o + 2]) radius normal

                    if depth > 0.0 then
                        let nx = float normal.[0]
                        let ny = float normal.[1]
                        let nz = float normal.[2]
                        // Relativgeschwindigkeit entlang der Stossnormale (< 0 = Annaeherung)
                        let rvx = ship.Velocity.X - float a.[o + 3]
                        let rvy = ship.Velocity.Y - float a.[o + 4]
                        let rvz = ship.Velocity.Z - float a.[o + 5]
                        let vn = rvx * nx + rvy * ny + rvz * nz

                        ship.Position <-
                            Vec3(
                                ship.Position.X + nx * depth,
                                ship.Position.Y + ny * depth,
                                ship.Position.Z + nz * depth
                            )

                        if vn < 0.0 then
                            // Abprallen mit Energieverlust
                            ship.Velocity <-
                                Vec3(
                                    (ship.Velocity.X - nx * vn * 1.4) * 0.85,
                                    (ship.Velocity.Y - ny * vn * 1.4) * 0.85,
                                    (ship.Velocity.Z - nz * vn * 1.4) * 0.85
                                )

                            let impact = -vn

                            if impact > 6.0 then
                                Damage.ship world ship ((impact - 6.0) * 0.9)
                                // Wer schnell genug rammt, zertruemmert kleine Brocken
                                Damage.asteroid world k (impact * 0.5) ship.Team

                k <- k - 1

    /// Zwei Schiffe: Huellkugeln, dann OBB gegen OBB; bei Kontakt auseinander-
    /// schieben, Geschwindigkeit entlang der Verbindungslinie tauschen, Rammschaden.
    let private collideShips (world: World) (a: Ship) (b: Ship) =
        if a.Alive && b.Alive then
            let dx = b.Position.X - a.Position.X
            let dy = b.Position.Y - a.Position.Y
            let dz = b.Position.Z - a.Position.Z
            let reach = a.Radius + b.Radius
            let distSq = dx * dx + dy * dy + dz * dz

            if distSq < reach * reach && Collision.obbObb a b then
                let dist = Math.Max(1e-3, sqrt distSq)
                let nx = dx / dist
                let ny = dy / dist
                let nz = dz / dist
                // Annaeherungsgeschwindigkeit entlang der Verbindungslinie
                let vn =
                    (b.Velocity.X - a.Velocity.X) * nx
                    + (b.Velocity.Y - a.Velocity.Y) * ny
                    + (b.Velocity.Z - a.Velocity.Z) * nz

                // Leicht auseinanderschieben, damit sie sich nicht verhaken
                let push = (reach - dist) * 0.08
                a.Position <- Vec3(a.Position.X - nx * push, a.Position.Y - ny * push, a.Position.Z - nz * push)
                b.Position <- Vec3(b.Position.X + nx * push, b.Position.Y + ny * push, b.Position.Z + nz * push)

                if vn < 0.0 then
                    // Gleiche Massen: jeder bekommt den halben (leicht ueberhoehten) Impuls
                    let j = vn * 0.9
                    a.Velocity <- Vec3(a.Velocity.X + nx * j, a.Velocity.Y + ny * j, a.Velocity.Z + nz * j)
                    b.Velocity <- Vec3(b.Velocity.X - nx * j, b.Velocity.Y - ny * j, b.Velocity.Z - nz * j)
                    let impact = -vn

                    if impact > 5.0 then
                        let mx = (a.Position.X + b.Position.X) * 0.5
                        let my = (a.Position.Y + b.Position.Y) * 0.5
                        let mz = (a.Position.Z + b.Position.Z) * 0.5
                        Effects.sparks world mx my mz 14 1.5 0.9 0.4
                        Damage.ship world a ((impact - 5.0) * 0.8)
                        Damage.ship world b ((impact - 5.0) * 0.8)

    /// Ein fester Simulationsschritt. Die Steuerbefehle aller Schiffe muessen
    /// vorher gesetzt sein (Spieler: Eingabe, Gegner: AI.update).
    let step (world: World) (dt: float) =
        world.Time <- world.Time + dt
        let enemies = world.Enemies

        stepShip world world.Player dt

        for i in 0 .. enemies.Length - 1 do
            stepShip world enemies.[i] dt

        updateLasers world dt
        AsteroidField.update world.Asteroids dt

        collideShipAsteroids world world.Player

        for i in 0 .. enemies.Length - 1 do
            collideShipAsteroids world enemies.[i]

        for i in 0 .. enemies.Length - 1 do
            collideShips world world.Player enemies.[i]

            for j in i + 1 .. enemies.Length - 1 do
                collideShips world enemies.[i] enemies.[j]

        ParticlePool.update world.Particles dt
        Effects.updateFlashes world dt
