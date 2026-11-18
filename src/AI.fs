namespace SpaceEngine

// =====================================================================================
//  AI.fs  -  Steuerung der Gegner-Schiffe
//
//  Zustandsmaschine:
//
//      Patrol --(Spieler entdeckt)--> Chase --(in Reichweite)--> Attack
//         ^                             ^                           |
//         |                             |          (zu nah / zu lange / beschossen)
//         +---(Spieler zerstoert)-------+---(Ausweichen beendet)-- Evade
//
//  Die KI erzeugt ausschliesslich Steuerbefehle (ShipControls). Geflogen wird mit
//  demselben Flugmodell wie der Spieler - Gegner haben also dieselbe Traegheit
//  und koennen genauso ausmanoevriert werden.
//
//  Die Entscheidungen ("think") laufen pro Gegner mit 30 Hz und zeitlich versetzt;
//  zwischen zwei Entscheidungen bleiben die Steuerbefehle stehen. Das haelt die
//  Zahl kurzlebiger Vec3-Objekte klein und kostet keine spuerbare Praezision.
// =====================================================================================

open System
open Fable.Core

type AiState =
    /// Fliegt Wegpunkte um den eigenen Startbereich ab.
    | Patrol
    /// Hat den Spieler entdeckt und schliesst mit Vollschub auf.
    | Chase
    /// In Schussweite: haelt vor und feuert.
    | Attack
    /// Bricht den Angriff ab und entfernt sich im Zickzack.
    | Evade

type AiBrain =
    { mutable Role: EnemyRole
      mutable State: AiState
      /// Sekunden seit Eintritt in den aktuellen Zustand.
      mutable StateTime: float
      /// Zeit bis zur naechsten Entscheidung.
      mutable ThinkTimer: float
      mutable Home: Vec3
      mutable Waypoint: Vec3
      mutable EvadeDirection: Vec3
      /// Huelle + Schild beim Eintritt in den Zustand (erkennt erlittenen Schaden).
      mutable HealthAtEntry: float
      mutable AttackDuration: float
      mutable EvadeDuration: float
      /// Zuletzt berechneter Vorhaltepunkt in Weltkoordinaten (fuer HUD/Debug).
      mutable AimPoint: Vec3
      /// Sichtbare Aufladung vor einer Salve (0..1); Main zeichnet die Warnleuchten.
      mutable WeaponCharge: float
      mutable FiringBurst: bool
      /// Individueller Phasenversatz, damit nicht alle Gegner synchron wackeln.
      Phase: float }

[<RequireQualifiedAccess>]
module AI =

    /// Abstand zwischen zwei Entscheidungen (30 Hz).
    [<Literal>]
    let ThinkInterval = 0.03333333333333333

    /// cos(1.5 Grad): gefeuert wird nur, wenn die Nase so genau auf dem Vorhaltepunkt liegt
    /// (in 200 Einheiten Entfernung sind das rund 5 Einheiten Abweichung).
    [<Literal>]
    let FireConeCos = 0.99966

    // Fable erzeugt fuer jeden feldlosen DU-Fall bei jeder Verwendung ein neues
    // Objekt. Diese einmal angelegten Instanzen machen Zustandswechsel allokationsfrei.
    let private patrol = Patrol
    let private chase = Chase
    let private attack = Attack
    let private evade = Evade

    let private threat = JS.Constructors.Float32Array.Create 3

    let createBrain (index: int) : AiBrain =
        { Role = EnemyRole.Drone
          State = patrol
          StateTime = 0.0
          // Versetzte Entscheidungszeitpunkte verteilen die Last ueber die Frames
          ThinkTimer = float index * ThinkInterval * 0.37
          Home = Vec3.zero
          Waypoint = Vec3.zero
          EvadeDirection = Vec3.unitX
          HealthAtEntry = 0.0
          AttackDuration = 6.0
          EvadeDuration = 2.5
          AimPoint = Vec3.zero
          WeaponCharge = 0.0
          FiringBurst = false
          Phase = float index * 1.618 }

    let private enter (brain: AiBrain) (ship: Ship) (state: AiState) =
        brain.State <- state
        brain.StateTime <- 0.0
        brain.HealthAtEntry <- ship.Hull + ship.Shield

    /// Nach dem (Re-)Spawn sofort aufschliessen; die konfigurierte Rolle bleibt erhalten.
    let reset (brain: AiBrain) (ship: Ship) (home: Vec3) =
        brain.Home <- home
        brain.Waypoint <- home
        brain.AimPoint <- home
        brain.WeaponCharge <- 0.0
        brain.FiringBurst <- false
        brain.ThinkTimer <- (brain.Phase % 1.0) * ThinkInterval
        enter brain ship chase

    let private clampUnit (v: float) : float =
        if v > 1.0 then 1.0
        elif v < -1.0 then -1.0
        else v

    /// Uebersetzt eine gewuenschte Flugrichtung (Welt, normalisiert) in Pitch/Yaw/Roll.
    /// Strategie wie bei einem Flugzeug: das Ziel durch Rollen in die Hochachsen-
    /// Ebene bringen und dann mit dem (schnelleren) Pitch hinziehen; Yaw korrigiert fein.
    /// `gain` > 1 fuehrt die Nase straffer nach (genaueres Zielen im Angriff).
    let private steerTowards (ship: Ship) (direction: Vec3) (gain: float) =
        let c = ship.Controls
        // Zielrichtung im Schiffssystem: -Z vorne, +X rechts, +Y oben
        let local = Quat.rotateVec3 (Quat.conjugate ship.Rotation) direction
        let x = local.X
        let y = local.Y
        let z = local.Z

        // Yaw: positiv = Ziel liegt links
        c.Yaw <- clampUnit (Math.Atan2(-x, -z) * 2.5 * gain)

        // Pitch: liegt das Ziel hinter dem Schiff, voll durchziehen
        c.Pitch <-
            if z > 0.0 then
                (if y >= 0.0 then 1.0 else -1.0)
            else
                clampUnit (Math.Atan2(y, sqrt (x * x + z * z)) * 3.0 * gain)

        // Roll: Ziel in die Ebene der Hochachse drehen (oben ODER unten, je nachdem
        // was naeher liegt); ohne seitlichen Versatz wird nicht gerollt.
        let rollError = Math.Atan2((if y >= 0.0 then -x else x), abs y)
        c.Roll <- clampUnit (rollError * 2.0) * Math.Min(1.0, abs x * 5.0)

    let private startEvade (world: World) (brain: AiBrain) (ship: Ship) (toPlayer: Vec3) (distance: float) =
        let rng = world.Rng
        let side = if rng.NextFloat() < 0.5 then -1.0 else 1.0

        let away =
            if distance > 1e-3 then
                toPlayer * (-1.0 / distance)
            else
                Quat.forward ship.Rotation

        // Seitlich wegbrechen, dabei Fahrt behalten und sich vom Spieler entfernen
        brain.EvadeDirection <-
            Vec3.normalize (
                Quat.forward ship.Rotation * 0.6
                + Quat.right ship.Rotation * (side * 0.9)
                + Quat.up ship.Rotation * rng.Range(-0.5, 0.5)
                + away * 0.3
            )

        brain.EvadeDuration <-
            if brain.Role = EnemyRole.Scout then rng.Range(1.6, 2.3)
            else rng.Range(1.4, 2.0)
        enter brain ship evade

    /// Eine Entscheidung: Zustandswechsel pruefen, dann Steuerbefehle setzen.
    let private think (world: World) (brain: AiBrain) (ship: Ship) =
        let player = world.Player
        let rng = world.Rng
        let c = ship.Controls
        let toPlayer = player.Position - ship.Position
        let distance = Vec3.length toPlayer
        let playerAlive = player.Alive
        let profile = EnemyProfiles.profile brain.Role

        // ---- 1. Zustandswechsel ----
        match brain.State with
        | Patrol ->
            // Missionsgegner geben auch einen weit entfernten Spieler nicht auf.
            if playerAlive then
                enter brain ship chase
            elif
                Vec3.distanceSq ship.Position brain.Waypoint < 1600.0
                || brain.StateTime > 14.0
                // Das Patrouillengebiet ist gewandert: alter Wegpunkt ist ueberholt
                || Vec3.distanceSq brain.Waypoint brain.Home > 160000.0
            then
                // Naechster Wegpunkt in einer flachen Scheibe um das Patrouillengebiet
                brain.Waypoint <-
                    Vec3(
                        brain.Home.X + rng.Range(-260.0, 260.0),
                        brain.Home.Y + rng.Range(-90.0, 90.0),
                        brain.Home.Z + rng.Range(-260.0, 260.0)
                    )

                brain.StateTime <- 0.0
        | Chase ->
            if not playerAlive then
                enter brain ship patrol
            elif distance < profile.AttackRange then
                brain.AttackDuration <-
                    match brain.Role with
                    | EnemyRole.Scout -> rng.Range(5.0, 7.0)
                    | EnemyRole.Drone -> rng.Range(8.0, 11.0)
                    | _ -> rng.Range(18.0, 25.0)
                enter brain ship attack
        | Attack ->
            let damageTaken = brain.HealthAtEntry - (ship.Hull + ship.Shield)

            if not playerAlive then
                enter brain ship patrol
            elif distance < profile.BreakDistance || brain.StateTime > brain.AttackDuration
                 || (brain.Role = EnemyRole.Scout && damageTaken > 22.0) then
                startEvade world brain ship toPlayer distance
            elif distance > profile.AttackRange * 1.25 then
                enter brain ship chase
        | Evade ->
            if brain.StateTime > brain.EvadeDuration then
                if playerAlive then
                    enter brain ship chase
                else
                    enter brain ship patrol

        // ---- 2. Verhalten des (ggf. neuen) Zustands ----
        c.Fire <- false
        c.Boost <- false
        c.Brake <- false
        brain.WeaponCharge <- 0.0
        brain.FiringBurst <- false
        let mutable desired = Quat.forward ship.Rotation
        let mutable aimGain = 1.0

        match brain.State with
        | Patrol ->
            desired <- brain.Waypoint - ship.Position
            c.Throttle <- 0.45
        | Chase ->
            // Auf die vorausberechnete Spielerposition zuhalten statt hinterherzufliegen
            let lookTime = Math.Min(distance / ship.CruiseSpeed, 1.0)
            desired <- Vec3.mulAdd toPlayer player.Velocity lookTime
            if profile.FlankOffset > 0.0 then
                let side = if sin brain.Phase >= 0.0 then 1.0 else -1.0
                desired <-
                    desired
                    + Quat.right player.Rotation * (profile.FlankOffset * side)
                    + Quat.up player.Rotation * (sin brain.Phase * 30.0)
            c.Throttle <- 1.0
            // Nur Spaeherschiffe sprinten kurz; auch ihr Sprint bleibt langsamer als der Spieler.
            c.Boost <- brain.Role = EnemyRole.Scout && distance > 340.0
                       && (world.Time + brain.Phase) % 12.0 < 0.65
        | Attack ->
            // Lead-Target-Prediction: Wo ist der Spieler, wenn der Bolt ankommt?
            // Bolts erben die Geschwindigkeit des Schuetzen, deshalb zaehlt die
            // RELATIVE Geschwindigkeit des Ziels.
            let relative = player.Velocity - ship.Velocity

            let time =
                Ballistics.interceptTime
                    toPlayer.X
                    toPlayer.Y
                    toPlayer.Z
                    relative.X
                    relative.Y
                    relative.Z
                    Combat.LaserSpeed

            let lead =
                if time > 0.0 then
                    Vec3.mulAdd toPlayer relative time
                else
                    toPlayer

            brain.AimPoint <- ship.Position + lead

            // Kleine, langsam wandernde Zielabweichung - sonst waere jeder Schuss ein Treffer
            let wobble = distance * profile.AimError
            let w = world.Time * 0.85 + brain.Phase

            let aim =
                Vec3(
                    lead.X + sin w * wobble,
                    lead.Y + cos (w * 1.3) * wobble,
                    lead.Z + sin (w * 0.7 + 1.0) * wobble
                )

            desired <- aim
            aimGain <- 1.5

            // Abstand halten: aufschliessen, wenn weit weg; Schub raus, wenn nah
            c.Throttle <-
                if distance > profile.PreferredRange * 1.3 then 0.85
                elif distance > profile.PreferredRange then 0.35
                elif brain.Role = EnemyRole.Scout then 0.55
                else 0.0

            // Jede Salve beginnt mit einer echten, sichtbaren Vorwarnung. Zwischen
            // den Salven entsteht Zeit zum Gegenangriff oder zum Positionswechsel.
            let cycle = profile.ChargeTime + profile.BurstTime + profile.RestTime
            let cycleTime = brain.StateTime % cycle
            if cycleTime < profile.ChargeTime then
                brain.WeaponCharge <- cycleTime / profile.ChargeTime
            elif cycleTime < profile.ChargeTime + profile.BurstTime then
                brain.FiringBurst <- true

            let aimLength = Vec3.length aim

            if brain.FiringBurst && distance < profile.AttackRange * 1.1 && aimLength > 1e-3 then
                let cosAngle = Vec3.dot (Quat.forward ship.Rotation) aim / aimLength
                c.Fire <- cosAngle > FireConeCos
        | Evade ->
            // Ausweichrichtung mit ueberlagertem Zickzack
            let w = world.Time * 1.6 + brain.Phase

            desired <-
                brain.EvadeDirection
                + Quat.up ship.Rotation * (sin w * 0.2)
                + Quat.right ship.Rotation * (cos (w * 0.8) * 0.25)

            c.Throttle <- 1.0
            c.Boost <- false

        // Nachbarn frueh seitlich umfliegen. Skalare Akkumulatoren vermeiden pro
        // Nachbar neue Vektoren und halten auch groessere Gegnergruppen bezahlbar.
        let intended = Vec3.normalize desired
        let mutable separationX = 0.0
        let mutable separationY = 0.0
        let mutable separationZ = 0.0
        let mutable crowding = 0.0
        for other in world.Enemies do
            if other.Alive && not (obj.ReferenceEquals(ship, other)) then
                let dx = ship.Position.X - other.Position.X
                let dy = ship.Position.Y - other.Position.Y
                let dz = ship.Position.Z - other.Position.Z
                let distanceSq = dx * dx + dy * dy + dz * dz
                let space = ship.Radius + other.Radius + 26.0
                if distanceSq < space * space then
                    let gap = sqrt distanceSq
                    let pressure = 1.0 - gap / space
                    crowding <- max crowding pressure
                    if gap > 0.01 then
                        let weight = pressure * pressure / gap
                        separationX <- separationX + dx * weight
                        separationY <- separationY + dy * weight
                        separationZ <- separationZ + dz * weight
                    else
                        separationX <- separationX + sin brain.Phase
                        separationY <- separationY + cos brain.Phase

        let direction =
            Vec3.normalize (intended + Vec3(separationX, separationY, separationZ) * 2.8)

        if crowding > 0.15 then
            c.Throttle <- max c.Throttle 0.3
        if crowding > 0.45 then
            c.Throttle <- min c.Throttle 0.4
            c.Boost <- false
        if Vec3.dot intended direction < 0.995 then
            c.Fire <- false

        // ---- 3. Hindernisvermeidung (hat Vorrang vor allem anderen) ----
        let speed = Vec3.length ship.Velocity

        let heading =
            if speed > 5.0 then
                ship.Velocity / speed
            else
                Quat.forward ship.Rotation

        let urgency =
            AsteroidField.findThreat
                world.Asteroids
                ship.Position.X
                ship.Position.Y
                ship.Position.Z
                heading.X
                heading.Y
                heading.Z
                (40.0 + speed * 1.6)
                (ship.Radius + 6.0)
                threat

        if urgency > 0.0 then
            let away = Vec3(float threat.[0], float threat.[1], float threat.[2])
            let weight = Math.Min(1.0, urgency * 2.5)
            // Quer zum Kurs ausweichen, aber etwas Vorwaertsanteil behalten
            steerTowards ship (Vec3.normalize (direction * (1.0 - weight) + (heading * 0.4 + away) * weight)) 1.0

            if urgency > 0.5 then
                c.Fire <- false
                c.Boost <- false
                c.Throttle <- min c.Throttle 0.3
        else
            steerTowards ship direction aimGain

    /// Fuehrt fuer alle lebenden Gegner faellige Entscheidungen aus.
    /// Einmal pro Physikschritt aufrufen, vor Physics.step.
    let update (world: World) (brains: AiBrain[]) (dt: float) =
        let enemies = world.Enemies

        for i in 0 .. enemies.Length - 1 do
            let ship = enemies.[i]

            if ship.Alive && world.Pilot.PulseTime > 5.0 && brains.[i].Role = EnemyRole.Drone then
                ship.Controls.Fire <- false
                brains.[i].FiringBurst <- false
                brains.[i].WeaponCharge <- 0.0
                brains.[i].State <- AiState.Patrol
                brains.[i].ThinkTimer <- 0.0
            elif ship.Alive then
                let brain = brains.[i]
                brain.StateTime <- brain.StateTime + dt
                brain.ThinkTimer <- brain.ThinkTimer - dt

                if brain.ThinkTimer <= 0.0 then
                    brain.ThinkTimer <- brain.ThinkTimer + ThinkInterval

                    if brain.ThinkTimer < 0.0 then
                        brain.ThinkTimer <- 0.0

                    think world brain ship
