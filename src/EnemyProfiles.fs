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
      /// Rollenfarbe von Positionslicht und Triebwerkshöfen; das Modell trägt seine Farben selbst.
      Tint: Vec3
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
          AimError = 0.010
          ChargeTime = 0.65
          BurstTime = 0.35
          RestTime = 2.4
          FlankOffset = 0.0 }

    let private scout =
        { drone with
            Name = "Späher"
            Color = "#60e3ed"
            Tint = Vec3(0.35, 1.15, 1.3)
            CruiseSpeed = 42.0
            BoostSpeed = 14.0
            PitchRate = 1.05
            YawRate = 0.7
            RollRate = 1.6
            // Werte bleiben: gespeicherte Gegner duerfen nie ueber dem Profil liegen (GameSave.valid).
            Hull = 38.0
            Shield = 12.0
            FireInterval = 0.4
            LaserDamage = 5.0
            AttackRange = 245.0
            PreferredRange = 145.0
            BreakDistance = 50.0
            AimError = 0.008
            ChargeTime = 0.4
            BurstTime = 0.7
            RestTime = 2.1
            FlankOffset = 150.0 }

    let private gunship =
        { drone with
            Name = "Kanonenboot"
            Color = "#ff798e"
            Tint = Vec3(1.3, 0.35, 0.5)
            CruiseSpeed = 23.0
            PitchRate = 0.56
            YawRate = 0.4
            RollRate = 0.65
            Hull = 110.0
            Shield = 40.0
            FireInterval = 0.28
            LaserDamage = 6.0
            AttackRange = 345.0
            PreferredRange = 245.0
            BreakDistance = 65.0
            AimError = 0.006
            ChargeTime = 1.05
            BurstTime = 0.85
            RestTime = 2.8 }

    let private bomber =
        { gunship with
            Name = "Bomber"
            Color = "#d895ff"
            Tint = Vec3(0.95, 0.42, 1.35)
            CruiseSpeed = 19.0
            // Nachbrenner nur im Anlauf
            BoostSpeed = 16.0
            PitchRate = 0.45
            YawRate = 0.32
            RollRate = 0.5
            Hull = 150.0
            Shield = 40.0
            // Dichte, schwere Salve auf einen eingefrorenen Punkt: trifft nur, wer den Kurs haelt.
            FireInterval = 0.2
            LaserDamage = 10.0
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
            CruiseSpeed = 17.0
            PitchRate = 0.4
            YawRate = 0.3
            RollRate = 0.45
            // Die Korvette bietet von vorn 1,4-mal so viel Trefferfläche wie das alte Kommandoschiff.
            // Ihr Schild lädt bei jedem Phasenwechsel neu (AI.think), damit alle drei Phasen stattfinden.
            Hull = 400.0
            Shield = 160.0
            FireInterval = 0.24
            LaserDamage = 7.0
            AttackRange = 365.0
            PreferredRange = 260.0
            BreakDistance = 85.0
            AimError = 0.006
            ChargeTime = 1.3
            BurstTime = 1.0
            RestTime = 3.2 }

    /// Schaden eines Batteriebolzens der Korvette relativ zu ihren Buggeschuetzen.
    [<Literal>]
    let BatteryDamage = 1.0

    /// Halbe Breite des Batteriefaechers um den eingefrorenen Zielpunkt (m).
    [<Literal>]
    let BatterySpread = 1.4

    /// Dauer des Schildstosses beim Phasenwechsel der Korvette (s).
    [<Literal>]
    let PhaseShield = 3.0

    /// Wunschabstand und Anteil der Feuerpause der Korvette in ihrer letzten Phase.
    [<Literal>]
    let EnragedRange = 170.0

    [<Literal>]
    let EnragedRest = 0.45

    /// So lange vor der naechsten Aufladung dreht das Kanonenboot wieder ein.
    [<Literal>]
    let GunshipTurnIn = 1.6

    /// Mindestabstand, den ein Bomber nach dem Abdrehen fuer den naechsten Anlauf sucht.
    [<Literal>]
    let BomberRunUp = 200.0

    /// Unterhalb dieses Abstands greift ein Spaeher auch frontal an (der Spieler hat ihn im Blick).
    [<Literal>]
    let FlankCommit = 130.0

    /// So lange sucht ein Spaeher je Anflug die Flanke, bevor er frontal angreift (s).
    [<Literal>]
    let FlankPatience = 4.5

    /// So weit setzt sich ein Spaeher ab, bevor er zum naechsten Anflug eindreht.
    [<Literal>]
    let ScoutBreakAway = 140.0

    /// Spaeher greifen an, sobald sie so weit seitlich der Spielernase stehen (cos des Winkels).
    [<Literal>]
    let FlankBearing = 0.45

    let profile (role: EnemyRole) : EnemyProfile =
        match role with
        | EnemyRole.Scout -> scout
        | EnemyRole.Gunship -> gunship
        | EnemyRole.Bomber -> bomber
        | EnemyRole.Commander -> commander
        | _ -> drone

    /// Trefferhilfe: der flache Spaeher bietet von vorn nur 4,8 m² (altes Modell 10,8 m²) und war
    /// damit kaum zu treffen. Die Kollisionsbox reicht deshalb etwas ueber den Rumpf hinaus.
    let hitPadding (role: EnemyRole) : Vec3 =
        if role = EnemyRole.Scout then Vec3(0.3, 0.45, 0.0) else Vec3.zero

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
        // Kollisionsform und Rohre stammen aus dem Rollenmodell (EnemyMeshes, in Metern),
        // damit Mesh, OBB und Waffen deckungsgleich bleiben.
        let hull = EnemyMeshes.hull (int role)
        let scale = ShipHardpoints.interceptorScale
        let half = hull.ObbHalf + hitPadding role
        ship.Radius <- (Vec3.length hull.ObbCenter + Vec3.length half) * scale
        ship.ObbCenter <- hull.ObbCenter * scale
        ship.ObbHalf <- half * scale
        ship.GunLeft <- hull.GunLeft * scale
        ship.GunRight <- hull.GunRight * scale
