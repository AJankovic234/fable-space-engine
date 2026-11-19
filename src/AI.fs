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
//  Jede Rolle fliegt dieselbe Maschine anders (Kommentare an `think`):
//      Drohne       Schwarm: rueckt zusammen, kreist und feuert im gemeinsamen Takt.
//      Spaeher      Flanke: kommt von der Seite, kurzer Vorbeiflug, loest sich mit Sprint.
//      Kanonenboot  haelt Abstand, zeigt in der Feuerpause die Breitseite, weicht Nahkampf aus.
//      Bomber       Anlauf mit Nachbrenner, eingefrorene schwere Salve, dreht ab.
//      Korvette     drei Phasen nach Huelle: Buggeschuetze, dazu Batteriefaecher, am Ende Nahkampf.
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
      Phase: float
      // ---- Rollentaktik. Steht nicht im Spielstand; AI.resync leitet sie nach dem Laden ab. ----
      /// +1/-1: Flanken- und Kreisrichtung; Spaeher wechseln sie nach jedem Anflug.
      mutable Side: float
      /// Schwere Salven frieren waehrend der Aufladung den Kurs des Spielers ein (Ort, Geschwindigkeit,
      /// Weltzeit) und feuern entlang dieser Bahn: wer danach den Kurs wechselt, weicht aus.
      mutable Locked: bool
      mutable Lock: Vec3
      mutable LockVelocity: Vec3
      mutable LockTime: float
      /// Korvette: 1..3, steigt mit sinkender Huelle.
      mutable BossPhase: int
      /// Beim Phasenwechsel gesetzt; Encounters.takeEscortCall holt es ab.
      mutable EscortCall: bool
      mutable BatteryTimer: float
      /// Restzeit des Schildstosses nach einem Phasenwechsel (Korvette unverwundbar).
      mutable PhaseShift: float }

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

    /// Ab diesem Anteil der Aufladung steht der Zielpunkt schwerer Salven fest.
    [<Literal>]
    let LockAt = 0.75

    /// Abstand zwischen zwei Batteriefaechern der Korvette.
    [<Literal>]
    let BatteryInterval = 0.34

    /// Korvettenphase aus der Huelle: ueber zwei Drittel 1, ueber ein Drittel 2, darunter 3.
    let bossPhase (ship: Ship) : int =
        let hull = if ship.MaxHull > 0.0 then ship.Hull / ship.MaxHull else 1.0
        if hull > 0.66 then 1 elif hull > 0.33 then 2 else 3

    /// Pause zwischen zwei Salven; die Korvette verkuerzt sie in ihrer letzten Phase.
    let restTime (brain: AiBrain) (profile: EnemyProfile) : float =
        if brain.Role = EnemyRole.Commander && brain.BossPhase >= 3 then profile.RestTime * EnemyProfiles.EnragedRest
        else profile.RestTime

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
          Phase = float index * 1.618
          Side = (if index % 2 = 0 then 1.0 else -1.0)
          Locked = false
          Lock = Vec3.zero
          LockVelocity = Vec3.zero
          LockTime = 0.0
          BossPhase = 1
          EscortCall = false
          BatteryTimer = 0.0
          PhaseShift = 0.0 }

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
        brain.Locked <- false
        brain.BossPhase <- 1
        brain.EscortCall <- false
        brain.BatteryTimer <- 0.0
        brain.PhaseShift <- 0.0
        brain.ThinkTimer <- (brain.Phase % 1.0) * ThinkInterval
        enter brain ship chase

    /// Nach dem Laden: Taktikfelder aus dem gespeicherten Zustand ableiten, ohne dass die
    /// Korvette ihren Phasenwechsel (Schild, Eskortenruf) ein zweites Mal ausloest.
    let resync (world: World) (brains: AiBrain[]) =
        for i in 0 .. brains.Length - 1 do
            let brain = brains.[i]
            brain.Locked <- false
            brain.EscortCall <- false
            brain.BatteryTimer <- 0.0
            brain.PhaseShift <- 0.0
            brain.Side <- if sin brain.Phase >= 0.0 then 1.0 else -1.0
            brain.BossPhase <-
                if brain.Role = EnemyRole.Commander && world.Enemies.[i].Alive then bossPhase world.Enemies.[i] else 1

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
        let forward = Quat.forward ship.Rotation
        let right = Quat.right ship.Rotation
        let up = Quat.up ship.Rotation

        let away =
            if distance > 1e-3 then
                toPlayer * (-1.0 / distance)
            else
                forward

        match brain.Role with
        | EnemyRole.Scout ->
            // Loesen: mit voller Fahrt am Spieler vorbei und geradeaus weiter (keine Kehre im Nahbereich),
            // damit der naechste Anflug wieder aus der Distanz und von der Flanke kommt
            brain.EvadeDirection <- Vec3.normalize (forward + right * (side * 0.45) + up * rng.Range(-0.3, 0.3) + away * 0.2)
            brain.EvadeDuration <- rng.Range(4.0, 5.0)
        | EnemyRole.Gunship ->
            // Abstand zurueckgewinnen statt Kurvenkampf
            brain.EvadeDirection <- Vec3.normalize (away + right * (side * 0.35) + up * rng.Range(-0.2, 0.2))
            brain.EvadeDuration <- rng.Range(4.0, 5.0)
        | EnemyRole.Bomber ->
            // Abdrehen: am Spieler vorbei und Raum fuer den naechsten Anlauf schaffen
            let profile = EnemyProfiles.profile brain.Role
            brain.EvadeDirection <- Vec3.normalize (forward * 0.5 + right * (brain.Side * 0.9) + up * 0.3 + away * 0.6)
            brain.EvadeDuration <- profile.RestTime + rng.Range(-0.4, 0.4)
            brain.Side <- -brain.Side
        | EnemyRole.Blocker ->
            // Nach der Salve seitlich abdrehen und mit dem naechsten Durchgang die Seite wechseln.
            brain.EvadeDirection <- Vec3.normalize (right * brain.Side + forward * 0.4 + away * 0.2 + up * 0.24)
            brain.EvadeDuration <- (EnemyProfiles.profile brain.Role).RestTime
            brain.Side <- -brain.Side
        | _ ->
            // Seitlich wegbrechen, dabei Fahrt behalten und sich vom Spieler entfernen
            brain.EvadeDirection <-
                Vec3.normalize (forward * 0.6 + right * (side * 0.9) + up * rng.Range(-0.5, 0.5) + away * 0.3)
            brain.EvadeDuration <- rng.Range(1.4, 2.0)

        brain.Locked <- false
        enter brain ship evade

    /// Vorhalt auf die eingefrorene Bahn: wo waere der Spieler beim Einschlag, wenn er den Kurs
    /// seit dem Einfrieren gehalten haette? Liefert den Zielpunkt relativ zum Schiff.
    let private lockedAim (world: World) (brain: AiBrain) (ship: Ship) : Vec3 =
        let track = Vec3.mulAdd brain.Lock brain.LockVelocity (world.Time - brain.LockTime) - ship.Position
        let relative = brain.LockVelocity - ship.Velocity
        let time = Ballistics.interceptTime track.X track.Y track.Z relative.X relative.Y relative.Z Combat.LaserSpeed
        if time > 0.0 then Vec3.mulAdd track relative time else track

    /// Gesteuerte Salven. Bomber und Korvette folgen einem eingefrorenen Kurs,
    /// der Sperrjaeger deckt zur Schusszeit einen kurzen, breiten Korridor ab.
    let private fireSalvo (world: World) (brain: AiBrain) (ship: Ship) (slot: int) (focus: Vec3) =
        let bow = Quat.forward ship.Rotation
        let mutable fired = false
        let shoot (origin: Vec3) (target: Vec3) (damage: float) (gimbal: float) =
            let direction = Vec3.normalize (target - origin)
            if Vec3.dot direction bow > gimbal then
                let v = Vec3.mulAdd ship.Velocity direction Combat.LaserSpeed
                let q = Quat.lookRotation direction Vec3.unitY
                if LaserPool.spawn world.Lasers origin.X origin.Y origin.Z v.X v.Y v.Z q.X q.Y q.Z q.W damage Team.Enemy then
                    if brain.Role = EnemyRole.Blocker then
                        LaserPool.tag world.Lasers (CombatVfx.OwnerEnemySlot + slot)
                    fired <- true
        if brain.Role = EnemyRole.Commander then
            let hull = EnemyMeshes.hull (int brain.Role)
            let scale = ShipHardpoints.interceptorScale
            let right = Quat.right ship.Rotation
            let up = Quat.up ship.Rotation
            let damage = ship.LaserDamage * EnemyProfiles.BatteryDamage
            // Obere und untere Batteriepaare wechseln sich ab
            let upper = int (world.Time / BatteryInterval) % 2 = 0
            for k in 0 .. hull.Batteries.Length - 1 do
              if (k < 2) = upper then
                let origin = ship.Position + Quat.rotateVec3 ship.Rotation (hull.Batteries.[k] * scale)
                let sx = if k % 2 = 0 then -1.0 else 1.0
                let sy = if k < 2 then 1.0 else -1.0
                shoot origin (focus + right * (sx * EnemyProfiles.BatterySpread) + up * (sy * EnemyProfiles.BatterySpread * 0.25)) damage 0.6
        elif brain.Role = EnemyRole.Blocker then
            let left = ship.Position + Quat.rotateVec3 ship.Rotation ship.GunLeft
            let right = ship.Position + Quat.rotateVec3 ship.Rotation ship.GunRight
            let lateral = Quat.right ship.Rotation
            let outer = lateral * (Vec3.distance ship.Position focus * 0.075)
            let inner = lateral * 0.8
            shoot left (focus - outer) ship.LaserDamage 0.82
            shoot left (focus - inner) ship.LaserDamage 0.82
            shoot right (focus + inner) ship.LaserDamage 0.82
            shoot right (focus + outer) ship.LaserDamage 0.82
            // Der normale, gespeicherte Cooldown verhindert eine zweite Salve im Fenster.
            if fired then ship.FireCooldown <- ship.FireInterval
        else
            shoot (ship.Position + Quat.rotateVec3 ship.Rotation ship.GunLeft) focus ship.LaserDamage 0.9
            shoot (ship.Position + Quat.rotateVec3 ship.Rotation ship.GunRight) focus ship.LaserDamage 0.9
            // Muendungsfeuer wie bei einem normalen Schuss (Main liest FireCooldown)
            if fired then ship.FireCooldown <- ship.FireInterval
        if fired then world.Events.Add(LaserFired(Team.Enemy, ship.Position, slot))

    /// Eine Entscheidung: Zustandswechsel pruefen, dann Steuerbefehle setzen.
    let private think (world: World) (brains: AiBrain[]) (slot: int) (ship: Ship) =
        let brain = brains.[slot]
        let player = world.Player
        let rng = world.Rng
        let c = ship.Controls
        let toPlayer = player.Position - ship.Position
        let distance = Vec3.length toPlayer
        let playerAlive = player.Alive
        let role = brain.Role
        let profile = EnemyProfiles.profile role

        let cycle = profile.ChargeTime + profile.BurstTime + restTime brain profile

        // Spaeher: noch auf dem Weg zur Flanke? Sonst (seitlich des Spielers oder schon nah) eindrehen.
        let flanking =
            role = EnemyRole.Scout && brain.StateTime < EnemyProfiles.FlankPatience && distance > EnemyProfiles.FlankCommit
            && -(Vec3.dot (Quat.forward player.Rotation) toPlayer) / distance >= EnemyProfiles.FlankBearing

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
            let ready =
                if distance >= profile.AttackRange then false
                elif (role = EnemyRole.Bomber || role = EnemyRole.Blocker) && distance > 1e-3 then
                    // Der Anlauf beginnt erst, wenn die Nase auf dem Spieler liegt.
                    Vec3.dot (Quat.forward ship.Rotation) toPlayer / distance > 0.94
                elif role = EnemyRole.Scout && distance > 1e-3 then
                    // Spaeher greifen erst an, wenn sie neben oder hinter dem Spieler stehen
                    // und die eigene Nase schon grob auf ihm liegt (kein Angriff aus der Kehre).
                    not flanking && Vec3.dot (Quat.forward ship.Rotation) toPlayer / distance > 0.8
                else true

            if not playerAlive then
                enter brain ship patrol
            elif ready then
                brain.AttackDuration <-
                    match role with
                    | EnemyRole.Scout -> rng.Range(3.2, 4.6)
                    | EnemyRole.Drone -> rng.Range(8.0, 11.0)
                    | _ -> rng.Range(18.0, 25.0)
                brain.Locked <- false
                enter brain ship attack
        | Attack ->
            let damageTaken = brain.HealthAtEntry - (ship.Hull + ship.Shield)
            let cycleTime = brain.StateTime % cycle
            let committed = cycleTime > profile.ChargeTime * 0.5 && cycleTime < profile.ChargeTime + profile.BurstTime

            if not playerAlive then
                enter brain ship patrol
            elif (role = EnemyRole.Bomber || role = EnemyRole.Blocker) && brain.StateTime > profile.ChargeTime + profile.BurstTime then
                // Ein Anlauf, eine Salve: danach abdrehen
                startEvade world brain ship toPlayer distance
            elif role = EnemyRole.Gunship && distance < profile.PreferredRange * 0.5 && not committed then
                startEvade world brain ship toPlayer distance
            elif distance < profile.BreakDistance || brain.StateTime > brain.AttackDuration
                 || (role = EnemyRole.Scout && damageTaken > 26.0) then
                startEvade world brain ship toPlayer distance
            elif distance > profile.AttackRange * 1.25 then
                enter brain ship chase
        | Evade ->
            let clear = role = EnemyRole.Scout && brain.StateTime > 1.2 && distance > EnemyProfiles.ScoutBreakAway
            // Bomber holen erst Anlauf, bevor sie wieder eindrehen (hoechstens doppelt so lange).
            let cramped = role = EnemyRole.Bomber && distance < EnemyProfiles.BomberRunUp && brain.StateTime < brain.EvadeDuration * 2.0
            if (brain.StateTime > brain.EvadeDuration && not cramped) || clear then
                if role = EnemyRole.Scout then brain.Side <- -brain.Side
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
            c.Throttle <- 1.0

            match role with
            | EnemyRole.Scout ->
                // Flankenpunkt seitlich und leicht hinter dem Spieler anfliegen
                if flanking then
                    desired <-
                        desired
                        + Quat.right player.Rotation * (profile.FlankOffset * brain.Side)
                        - Quat.forward player.Rotation * 40.0
                        + Quat.up player.Rotation * (sin brain.Phase * 30.0)
                // Kurze Sprints; auch ihr Sprint bleibt langsamer als der Spieler.
                c.Boost <- distance > 200.0 && (world.Time + brain.Phase) % 6.0 < 1.5
            | EnemyRole.Drone ->
                // Schwarm: zu den anderen Drohnen aufschliessen und gemeinsam pendeln
                let mutable cx = 0.0
                let mutable cy = 0.0
                let mutable cz = 0.0
                let mutable mates = 0
                for i in 0 .. brains.Length - 1 do
                    let other = world.Enemies.[i]
                    if i <> slot && other.Alive && brains.[i].Role = EnemyRole.Drone then
                        cx <- cx + other.Position.X
                        cy <- cy + other.Position.Y
                        cz <- cz + other.Position.Z
                        mates <- mates + 1
                let mutable heading = Vec3.normalize desired
                if mates > 0 then
                    let toSwarm = Vec3(cx / float mates, cy / float mates, cz / float mates) - ship.Position
                    let gap = Vec3.length toSwarm
                    if gap > 45.0 then
                        heading <- heading + toSwarm * (Math.Min(0.7, (gap - 45.0) / 110.0) / gap)
                let w = world.Time * 1.4 + (if mates > 0 then 0.0 else brain.Phase)
                desired <- heading + Quat.right ship.Rotation * (sin w * 0.16) + Quat.up ship.Rotation * (cos (w * 0.7) * 0.1)
            | EnemyRole.Bomber ->
                // Zu nah fuer einen Anlauf und nicht ausgerichtet: erst Abstand holen statt eng zu kreisen
                if distance > 1e-3 && distance < EnemyProfiles.BomberRunUp * 0.6
                   && Vec3.dot (Quat.forward ship.Rotation) toPlayer / distance < 0.94 then
                    desired <- toPlayer * (-1.0 / distance) + Quat.right ship.Rotation * (brain.Side * 0.3)
            | _ -> ()
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

            let mutable aim =
                Vec3(
                    lead.X + sin w * wobble,
                    lead.Y + cos (w * 1.3) * wobble,
                    lead.Z + sin (w * 0.7 + 1.0) * wobble
                )

            aimGain <- 1.5

            // Jede Salve beginnt mit einer echten, sichtbaren Vorwarnung. Zwischen
            // den Salven entsteht Zeit zum Gegenangriff oder zum Positionswechsel.
            // Drohnen teilen sich die Weltuhr: der ganze Schwarm laedt und feuert gemeinsam.
            let swarm = role = EnemyRole.Drone
            let cycleTime = (if swarm then world.Time else brain.StateTime) % cycle
            // Eine Drohne, die mitten im Takt dazukommt, wartet auf die naechste Schwarmsalve.
            let joined = not swarm || brain.StateTime >= cycleTime
            let salvoEnd = profile.ChargeTime + profile.BurstTime
            let locks = role = EnemyRole.Bomber || (role = EnemyRole.Commander && brain.BossPhase >= 2)

            if not joined then
                ()
            elif cycleTime < profile.ChargeTime then
                let charge = cycleTime / profile.ChargeTime
                brain.WeaponCharge <- charge
                if charge < LockAt then
                    brain.Locked <- false
                elif locks && not brain.Locked then
                    // Kurs einfrieren: die Salve folgt dieser Bahn, nicht mehr dem Spieler.
                    brain.Lock <- player.Position
                    brain.LockVelocity <- player.Velocity
                    brain.LockTime <- world.Time
                    brain.Locked <- true
                    brain.BatteryTimer <- 0.0
            elif cycleTime < salvoEnd then
                brain.FiringBurst <- true
            else
                brain.Locked <- false

            // Bomber feuern die Buggeschuetze entlang der eingefrorenen Bahn; vorher halten sie
            // ohne Zielfehler vor, damit beim Einfrieren nichts springt.
            if role = EnemyRole.Bomber then
                if brain.Locked then
                    aim <- lockedAim world brain ship
                    brain.AimPoint <- ship.Position + aim
                else
                    aim <- lead

            desired <- aim

            // Abstand halten: aufschliessen, wenn weit weg; Schub raus, wenn nah
            let preferred = if role = EnemyRole.Commander && brain.BossPhase >= 3 then EnemyProfiles.EnragedRange else profile.PreferredRange
            let holdRange =
                if distance > preferred * 1.3 then 0.85
                elif distance > preferred then 0.35
                else 0.0

            // Kreisbahn um den Spieler (Drohnen, Kanonenboot): quer zur Sichtlinie mit Abstandskorrektur
            let orbit (pull: float) =
                let line = if distance > 1e-3 then toPlayer / distance else Quat.forward ship.Rotation
                let axis = if abs line.Y > 0.9 then Vec3.unitX else Vec3.unitY
                let tangent = Vec3.normalize (Vec3.cross line axis) * brain.Side
                let radial = Math.Max(-0.8, Math.Min(0.8, (distance - preferred) / preferred * 2.0))
                tangent * pull + line * radial

            let resting = joined && cycleTime >= salvoEnd
            let restElapsed = cycleTime - salvoEnd

            match role with
            | EnemyRole.Scout ->
                // Vorbeiflug: Fahrt behalten, bis der Abstand zum Loesen zwingt
                c.Throttle <- 0.8
            | EnemyRole.Bomber ->
                // Anlauf mit Nachbrenner
                c.Throttle <- 1.0
                c.Boost <- true
            | EnemyRole.Blocker ->
                // Bereits beim Laden bremsen: der Faecher sperrt die kurze Anfluglinie.
                c.Throttle <- holdRange
                c.Brake <- distance < preferred
            | EnemyRole.Drone ->
                // Zwischen zwei Schwarmsalven kreisen, rechtzeitig vor der Aufladung eindrehen
                if resting && restElapsed < profile.RestTime * 0.5 then
                    desired <- orbit 0.9
                    aimGain <- 1.0
                    c.Throttle <- 0.75
                else
                    c.Throttle <- max holdRange 0.2
            | EnemyRole.Gunship ->
                // In der Feuerpause Breitseite zeigen und seitlich versetzen
                if resting && restElapsed < profile.RestTime - EnemyProfiles.GunshipTurnIn then
                    desired <- orbit 0.8
                    aimGain <- 1.0
                    // Erst abdrehen, dann Fahrt aufnehmen: sonst schiebt jede Pause das Boot naeher heran
                    let offLine = distance < 1e-3 || Vec3.dot (Quat.forward ship.Rotation) toPlayer / distance < 0.7
                    c.Throttle <- if offLine || distance > preferred then 0.7 else 0.1
                else
                    c.Throttle <- holdRange
            | EnemyRole.Commander ->
                c.Throttle <- if brain.BossPhase >= 3 && distance > preferred then 1.0 else holdRange
            | _ ->
                c.Throttle <- holdRange

            let aimLength = Vec3.length aim
            // Rohre auf die Zielentfernung einstellen
            ship.Convergence <- aimLength

            if brain.FiringBurst && role = EnemyRole.Blocker && ship.FireCooldown <= 0.0 && distance < profile.AttackRange then
                fireSalvo world brain ship slot (ship.Position + aim)
            elif brain.FiringBurst && role <> EnemyRole.Bomber && role <> EnemyRole.Blocker && distance < profile.AttackRange * 1.1 && aimLength > 1e-3 then
                let cosAngle = Vec3.dot (Quat.forward ship.Rotation) aim / aimLength
                c.Fire <- cosAngle > FireConeCos

            // Gerichtete Salven: Bomber immer, Korvette ab Phase 2 zusaetzlich zu den Buggeschuetzen.
            if locks && brain.FiringBurst && brain.Locked then
                brain.BatteryTimer <- brain.BatteryTimer - ThinkInterval
                if brain.BatteryTimer <= 0.0 then
                    brain.BatteryTimer <- (if role = EnemyRole.Bomber then profile.FireInterval else BatteryInterval)
                    fireSalvo world brain ship slot (ship.Position + lockedAim world brain ship)
        | Evade ->
            // Ausweichrichtung mit ueberlagertem Zickzack
            let w = world.Time * 1.6 + brain.Phase

            desired <-
                brain.EvadeDirection
                + Quat.up ship.Rotation * (sin w * 0.2)
                + Quat.right ship.Rotation * (cos (w * 0.8) * 0.25)

            c.Throttle <- 1.0
            // Spaeher loesen sich mit Sprint
            c.Boost <- role = EnemyRole.Scout || role = EnemyRole.Blocker

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

    /// Korvette: Phasen nach Huelle. Jeder Wechsel haelt die Huelle an der Schwelle fest, laedt
    /// den Schild neu, ruft Eskorte und beginnt mit einer frischen, sichtbaren Aufladung.
    /// Waehrend des kurzen Schildstosses prallt alles ab - so findet jede Phase auch dann statt,
    /// wenn der Spieler mit Overdrive feuert.
    let private updateBoss (brain: AiBrain) (ship: Ship) (dt: float) =
        if bossPhase ship > brain.BossPhase then
            brain.BossPhase <- brain.BossPhase + 1
            ship.Hull <- max ship.Hull (ship.MaxHull * (if brain.BossPhase = 2 then 0.34 else 0.02))
            brain.PhaseShift <- EnemyProfiles.PhaseShield
            brain.EscortCall <- true
            brain.Locked <- false
            if brain.State = attack then
                brain.StateTime <- 0.0
        if brain.PhaseShift > 0.0 then
            brain.PhaseShift <- brain.PhaseShift - dt
            ship.Shield <- ship.MaxShield
            ship.ShieldDelay <- 0.0
            ship.ShieldFlash <- max ship.ShieldFlash 0.7

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
                if brain.Role = EnemyRole.Commander then updateBoss brain ship dt
                brain.StateTime <- brain.StateTime + dt
                brain.ThinkTimer <- brain.ThinkTimer - dt

                if brain.ThinkTimer <= 0.0 then
                    brain.ThinkTimer <- brain.ThinkTimer + ThinkInterval

                    if brain.ThinkTimer < 0.0 then
                        brain.ThinkTimer <- 0.0

                    think world brains i ship
