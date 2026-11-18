namespace SpaceEngine

/// Rollen bleiben stabile Zahlen fuer Missionen, HUD und gespeicherte Statistiken.
type EnemyRole =
    | Drone = 0
    | Scout = 1
    | Gunship = 2
    | Bomber = 3
    | Commander = 4

type EnemyProfile =
    { Name: string
      Color: string
      Tint: Vec3
      /// Multiplikator auf das bestehende Interceptor-Mesh und seine Kollisionsform.
      Scale: Vec3
      CruiseSpeed: float
      BoostSpeed: float
      PitchRate: float
      YawRate: float
      RollRate: float
      Hull: float
      Shield: float
      FireInterval: float
      LaserDamage: float
      AttackRange: float
      PreferredRange: float
      BreakDistance: float
      AimError: float
      /// Vorwarnung, Feuerfenster und anschliessende Ruhe in Sekunden.
      ChargeTime: float
      BurstTime: float
      RestTime: float
      FlankOffset: float }

[<RequireQualifiedAccess>]
module EnemyProfiles =
    let private drone =
        { Name = "Drohne"
          Color = "#ffb75a"
          Tint = Vec3(1.2, 0.72, 0.3)
          Scale = Vec3(0.85, 0.85, 0.85)
          CruiseSpeed = 26.0
          BoostSpeed = 0.0
          PitchRate = 0.65
          YawRate = 0.45
          RollRate = 0.8
          Hull = 28.0
          Shield = 0.0
          FireInterval = 0.7
          LaserDamage = 4.0
          AttackRange = 205.0
          PreferredRange = 115.0
          BreakDistance = 35.0
          AimError = 0.025
          ChargeTime = 0.65
          BurstTime = 0.35
          RestTime = 2.4
          FlankOffset = 0.0 }

    let private scout =
        { drone with
            Name = "Späher"
            Color = "#60e3ed"
            Tint = Vec3(0.35, 1.15, 1.3)
            Scale = Vec3(0.95, 0.95, 0.95)
            CruiseSpeed = 42.0
            BoostSpeed = 14.0
            PitchRate = 1.05
            YawRate = 0.7
            RollRate = 1.6
            Hull = 38.0
            Shield = 12.0
            FireInterval = 0.4
            LaserDamage = 5.0
            AttackRange = 245.0
            PreferredRange = 145.0
            BreakDistance = 50.0
            AimError = 0.018
            ChargeTime = 0.4
            BurstTime = 0.7
            RestTime = 2.1
            FlankOffset = 95.0 }

    let private gunship =
        { drone with
            Name = "Kanonenboot"
            Color = "#ff798e"
            Tint = Vec3(1.3, 0.35, 0.5)
            Scale = Vec3(1.3, 1.3, 1.3)
            CruiseSpeed = 23.0
            PitchRate = 0.56
            YawRate = 0.4
            RollRate = 0.65
            Hull = 72.0
            Shield = 32.0
            FireInterval = 0.28
            LaserDamage = 6.0
            AttackRange = 345.0
            PreferredRange = 245.0
            BreakDistance = 65.0
            AimError = 0.015
            ChargeTime = 1.05
            BurstTime = 0.85
            RestTime = 2.8 }

    let private bomber =
        { gunship with
            Name = "Bomber"
            Color = "#d895ff"
            Tint = Vec3(0.95, 0.42, 1.35)
            Scale = Vec3(1.5, 1.5, 1.5)
            CruiseSpeed = 19.0
            PitchRate = 0.45
            YawRate = 0.32
            RollRate = 0.5
            Hull = 96.0
            Shield = 20.0
            FireInterval = 0.5
            LaserDamage = 12.0
            AttackRange = 390.0
            PreferredRange = 285.0
            BreakDistance = 80.0
            AimError = 0.022
            ChargeTime = 1.6
            BurstTime = 0.6
            RestTime = 3.8 }

    let private commander =
        { gunship with
            Name = "Kommandoschiff"
            Color = "#ffe17c"
            Tint = Vec3(1.4, 1.0, 0.35)
            Scale = Vec3(2.1, 2.1, 2.1)
            CruiseSpeed = 17.0
            PitchRate = 0.4
            YawRate = 0.3
            RollRate = 0.45
            Hull = 260.0
            Shield = 130.0
            FireInterval = 0.24
            LaserDamage = 7.0
            AttackRange = 365.0
            PreferredRange = 260.0
            BreakDistance = 85.0
            AimError = 0.013
            ChargeTime = 1.3
            BurstTime = 1.0
            RestTime = 3.2 }

    let profile (role: EnemyRole) : EnemyProfile =
        match role with
        | EnemyRole.Scout -> scout
        | EnemyRole.Gunship -> gunship
        | EnemyRole.Bomber -> bomber
        | EnemyRole.Commander -> commander
        | _ -> drone

    /// Vor Ship.reset aufrufen. Wiederverwendung eines Slots skaliert nie kumulativ.
    let configure (ship: Ship) (role: EnemyRole) (level: int) =
        let p = profile role
        let progression = float (max 0 (min 12 (level - 1)))
        let healthScale = 1.0 + progression * 0.04
        ship.CruiseSpeed <- p.CruiseSpeed
        ship.BoostSpeed <- p.BoostSpeed
        ship.MaxPitchRate <- p.PitchRate
        ship.MaxYawRate <- p.YawRate
        ship.MaxRollRate <- p.RollRate
        ship.MaxHull <- p.Hull * healthScale
        ship.MaxShield <- p.Shield * healthScale
        ship.FireInterval <- p.FireInterval
        ship.LaserDamage <- p.LaserDamage * (1.0 + progression * 0.015)
        // Alle Profile skalieren gleichfoermig; Mesh, OBB und Waffen bleiben deckungsgleich.
        let scale = p.Scale.X
        ship.Radius <- ShipHardpoints.interceptorRadius * scale
        ship.ObbCenter <- ShipHardpoints.interceptorObbCenter * scale
        ship.ObbHalf <- ShipHardpoints.interceptorObbHalf * scale
        ship.GunLeft <- ShipHardpoints.interceptorGunLeft * scale
        ship.GunRight <- ShipHardpoints.interceptorGunRight * scale
