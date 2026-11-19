namespace SpaceEngine

/// Eine überschaubare Mission statt unendlich wachsender Gegnerwerte.
type MissionStage =
    { Name: string
      Objective: string
      Hint: string
      Enemies: EnemyRole[] }

[<RequireQualifiedAccess>]
module Mission =
    let stages =
        [| { Name = "HALDENS RUH"
             Objective = "Räume die erste Patrouille aus dem Sprungkorridor."
             Hint = "Drohnen feuern im Schwarm gleichzeitig. Mit T ein Ziel wählen und der Vorhaltemarke folgen."
             Enemies = [| EnemyRole.Drone; EnemyRole.Drone; EnemyRole.Drone; EnemyRole.Scout |] }
           { Name = "SPÄHERNETZ"
             Objective = "Fange die Späher ab, bevor du weiter vorrückst."
             Hint = "Späher kommen von der Seite, ziehen vorbei und setzen sich ab. Triff sie im Anflug."
             Enemies = [| EnemyRole.Scout; EnemyRole.Drone; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Drone; EnemyRole.Scout |] }
           { Name = "BLOCKADE"
             Objective = "Zerschlage die Kanonenboote und ihre Eskorte."
             Hint = "Kanonenboote halten Abstand. In der Feuerpause zeigen sie die Breitseite: dann angreifen."
             Enemies = [| EnemyRole.Gunship; EnemyRole.Drone; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Drone; EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Drone |] }
           { Name = "SCHWERES GESCHWADER"
             Objective = "Schalte die Bomber im inneren Asteroidenfeld aus."
             Hint = "Bomber zielen beim Aufglühen auf deinen Kurs. Danach die Richtung wechseln."
             Enemies = [| EnemyRole.Bomber; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Drone; EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Bomber; EnemyRole.Drone; EnemyRole.Drone; EnemyRole.Scout |] }
           { Name = "LETZTE VERTEIDIGUNG"
             Objective = "Öffne den Zugang zur feindlichen Kommandozentrale."
             Hint = "Erst leichte Ziele abbauen. Jeder Abschuss gibt Schildenergie zurück."
             Enemies = [| EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Drone; EnemyRole.Bomber; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Gunship; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Bomber; EnemyRole.Drone; EnemyRole.Scout |] }
           { Name = "MORGENLICHT"
             Objective = "Zerstöre das Kommandoschiff und die gesamte Eskorte."
             Hint = "Die Korvette kämpft in drei Phasen und ruft Eskorte. Nach jedem Aufglühen den Kurs wechseln."
             Enemies = [| EnemyRole.Commander; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Drone; EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Bomber; EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Gunship; EnemyRole.Drone; EnemyRole.Bomber; EnemyRole.Scout; EnemyRole.Drone |] } |]

    let stage (wave: int) = stages.[max 0 (min (stages.Length - 1) (wave - 1))]

    let resetPlayer (ship: Ship) =
        ship.BrakeAssist <- 1.0
        ship.MaxHull <- 100.0
        ship.MaxShield <- 100.0
        ship.ShieldRechargeDelay <- Combat.ShieldRegenDelay
        ship.LaserDamage <- 12.0
        ship.FireInterval <- 0.13
        ship.PiercingShots <- false
        ship.ScatterShots <- false
        ship.BoltSpeedScale <- 1.0
        ship.CruiseSpeed <- Ship.cutterCruise
        ship.BoostSpeed <- Ship.cutterBoost
        ship.MaxPitchRate <- Ship.cutterPitch
        ship.MaxYawRate <- Ship.cutterYaw
        ship.MaxRollRate <- Ship.cutterRoll

    let applyLoadout (ship: Ship) (modules: int[]) pilot fleet =
        let hull = ShipCatalog.hulls.[fleet.Active]
        let trained = PilotSkills.level pilot 2 >= 1
        let turns = hull.Handling * ShipCatalog.handlingMultiplier fleet.Kit * (if trained then 1.2 else 1.0)
        ship.LaserDamage <- ShipCatalog.laser hull fleet.Kit modules.[0] trained
        ship.FireInterval <- ShipCatalog.fireInterval fleet.Kit
        ship.PiercingShots <- ShipCatalog.piercing fleet.Kit
        ship.ScatterShots <- ShipCatalog.scatter fleet.Kit
        ship.BoltSpeedScale <- ShipCatalog.boltSpeedMultiplier fleet.Kit
        ship.ShieldRechargeDelay <- ShipCatalog.rechargeDelay fleet.Kit
        ship.MaxPitchRate <- Ship.cutterPitch * turns
        ship.MaxYawRate <- Ship.cutterYaw * turns
        ship.MaxRollRate <- Ship.cutterRoll * turns
        ship.CruiseSpeed <- ShipCatalog.cruise hull fleet.Kit trained
        ship.BoostSpeed <- ShipCatalog.boostSpeed fleet.Kit
        ship.BrakeAssist <- if trained then 1.35 else 1.0

    let applyPilot ship modules pilot = applyLoadout ship modules pilot (ShipCatalog.create ())

    /// Genau einmal beim Verlassen der Werkstatt aufrufen. Stufen gelten nur für diesen Flug.
    let applyUpgrade (ship: Ship) (levels: int[]) (choice: int) =
        if choice >= 0 && choice < 3 then
            levels.[choice] <- levels.[choice] + 1
            ship.LaserDamage <- 12.0 * (1.0 + 0.2 * float levels.[0])
            ship.MaxShield <- ShipCatalog.shieldCapacity levels.[1]
            ship.MaxHull <- ShipCatalog.hullStrength levels.[2]
            ship.Hull <- min ship.MaxHull (ship.Hull + 35.0 + (if choice = 2 then 25.0 else 0.0))
            ship.Shield <- ship.MaxShield
            ship.ShieldDelay <- 0.0
            ship.Overdrive <- 0.0
