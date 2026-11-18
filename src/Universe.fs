namespace SpaceEngine

// =====================================================================================
//  Universe.fs  -  Sektoren-Graph, anfliegbare Handelsposten und Sprungtore
//
//  Erweitert die bisherige lineare Wellenfolge zu einem zusammenhaengenden Universum:
//  Jeder Sektor traegt eigene Licht-/Nebelstimmung, mindestens einen physisch im
//  3D-Raum anfliegbaren Handelsposten (Docking-Zone) sowie Sprungtore zu Nachbarn.
// =====================================================================================

type StationKind =
    | MiningDepot
    | Refinery
    | OrbitalYard
    | ResearchRelay
    | FrontierBazaar
    | FleetDock

type Outpost =
    { Id: string
      Name: string
      Kind: StationKind
      Position: Vec3
      DockRadius: float
      ApproachRadius: float
      HasRepair: bool
      HasUpgrades: bool }

type JumpGate =
    { TargetSectorId: string
      Label: string
      Position: Vec3
      TriggerRadius: float }

type SectorAtmosphere =
    { SunDir: Vec3
      SunColor: Vec3
      Ambient: Vec3
      ClearColor: Vec3 }

type SectorDef =
    { Id: string
      Name: string
      Subtitle: string
      DangerLevel: int
      AsteroidDensity: float
      OreRichness: float
      Outpost: Outpost
      Gates: JumpGate[]
      Atmosphere: SectorAtmosphere }

type DockState =
    | TooFar of distance: float
    | Approaching of distance: float
    | ReadyToDock of distance: float
    | BlockedByCombat of distance: float

[<RequireQualifiedAccess>]
module Universe =

    let private v x y z = Vec3(x, y, z)

    let sectors: SectorDef[] =
        [| { Id = "outer-belt"
             Name = "ÄUSSERER GÜRTEL"
             Subtitle = "Rohstoffreicher Randsektor mit leichter Drohnen-Präsenz"
             DangerLevel = 1
             AsteroidDensity = 1.0
             OreRichness = 0.35
             Outpost =
                 { Id = "station-kestrel"
                   Name = "Bergbau-Depot Kestrel"
                   Kind = MiningDepot
                   Position = v 260.0 24.0 -520.0
                   DockRadius = 90.0
                   ApproachRadius = 260.0
                   HasRepair = true
                   HasUpgrades = true }
             Gates =
                 [| { TargetSectorId = "slag-refinery"
                      Label = "Sprungtor → Schmelzwerk Helios"
                      Position = v -340.0 -15.0 -920.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "frontier-drift"
                      Label = "Sprungtor → Grenz-Drift"
                      Position = v 440.0 40.0 -880.0
                      TriggerRadius = 85.0 } |]
             Atmosphere =
                 { SunDir = Vec3.normalize (v 0.65 0.55 -0.45)
                   SunColor = v 1.05 0.98 0.90
                   Ambient = v 0.14 0.17 0.23
                   ClearColor = v 0.004 0.006 0.016 } }

           { Id = "slag-refinery"
             Name = "SCHMELZWERK HELIOS"
             Subtitle = "Industriesektor mit hoher Nachfrage nach Roherzen und Schrott"
             DangerLevel = 2
             AsteroidDensity = 0.85
             OreRichness = 0.25
             Outpost =
                 { Id = "station-helios"
                   Name = "Raffinerie Helios-IV"
                   Kind = Refinery
                   Position = v -180.0 35.0 -480.0
                   DockRadius = 95.0
                   ApproachRadius = 280.0
                   HasRepair = true
                   HasUpgrades = true }
             Gates =
                 [| { TargetSectorId = "outer-belt"
                      Label = "Sprungtor → Äußerer Gürtel"
                      Position = v 320.0 0.0 420.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "kepler-yard"
                      Label = "Sprungtor → Orbitalwerft 07"
                      Position = v 120.0 60.0 -980.0
                      TriggerRadius = 85.0 } |]
             Atmosphere =
                 { SunDir = Vec3.normalize (v -0.5 0.6 -0.6)
                   SunColor = v 1.15 0.86 0.62
                   Ambient = v 0.18 0.13 0.11
                   ClearColor = v 0.014 0.006 0.004 } }

           { Id = "frontier-drift"
             Name = "GRENZ-DRIFT"
             Subtitle = "Unruhiger Handelskorridor mit Schwarzmarkt-Bazar"
             DangerLevel = 2
             AsteroidDensity = 1.15
             OreRichness = 0.30
             Outpost =
                 { Id = "station-bazaar"
                   Name = "Freihafen Drift-9"
                   Kind = FrontierBazaar
                   Position = v 310.0 -28.0 -540.0
                   DockRadius = 90.0
                   ApproachRadius = 260.0
                   HasRepair = true
                   HasUpgrades = false }
             Gates =
                 [| { TargetSectorId = "outer-belt"
                      Label = "Sprungtor → Äußerer Gürtel"
                      Position = v -360.0 -20.0 380.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "aether-relay"
                      Label = "Sprungtor → Äther-Relais"
                      Position = v 460.0 20.0 -960.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "kepler-yard"
                      Label = "Sprungtor → Orbitalwerft 07"
                      Position = v -190.0 45.0 -920.0
                      TriggerRadius = 85.0 } |]
             Atmosphere =
                 { SunDir = Vec3.normalize (v 0.4 0.7 0.5)
                   SunColor = v 0.85 1.02 0.96
                   Ambient = v 0.12 0.18 0.17
                   ClearColor = v 0.004 0.012 0.012 } }

           { Id = "kepler-yard"
             Name = "ORBITALWERFT 07"
             Subtitle = "Zentraler Knotenpunkt für Schiffsausbau und Flottenversorgung"
             DangerLevel = 3
             AsteroidDensity = 0.65
             OreRichness = 0.15
             Outpost =
                 { Id = "station-kepler"
                   Name = "Orbitalwerft Kepler-07"
                   Kind = OrbitalYard
                   Position = v 0.0 40.0 -560.0
                   DockRadius = 105.0
                   ApproachRadius = 300.0
                   HasRepair = true
                   HasUpgrades = true }
             Gates =
                 [| { TargetSectorId = "slag-refinery"
                      Label = "Sprungtor → Schmelzwerk Helios"
                      Position = v -420.0 0.0 340.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "frontier-drift"
                      Label = "Sprungtor → Grenz-Drift"
                      Position = v 420.0 -10.0 340.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "morgenlicht-bastion"
                      Label = "Sprungtor → Bastion Morgenlicht"
                      Position = v 0.0 75.0 -1080.0
                      TriggerRadius = 90.0 } |]
             Atmosphere =
                 { SunDir = Vec3.normalize (v 0.3 0.8 -0.5)
                   SunColor = v 0.92 0.98 1.15
                   Ambient = v 0.13 0.16 0.24
                   ClearColor = v 0.004 0.008 0.018 } }

           { Id = "aether-relay"
             Name = "ÄTHER-RELAIS"
             Subtitle = "Forschungssektor am Rand eines blauen Nebels; hohe Kurse für Daten & Kerne"
             DangerLevel = 3
             AsteroidDensity = 0.90
             OreRichness = 0.28
             Outpost =
                 { Id = "station-aether"
                   Name = "Forschungs-Relais Theta"
                   Kind = ResearchRelay
                   Position = v -280.0 65.0 -610.0
                   DockRadius = 90.0
                   ApproachRadius = 260.0
                   HasRepair = true
                   HasUpgrades = true }
             Gates =
                 [| { TargetSectorId = "frontier-drift"
                      Label = "Sprungtor → Grenz-Drift"
                      Position = v 340.0 0.0 380.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "morgenlicht-bastion"
                      Label = "Sprungtor → Bastion Morgenlicht"
                      Position = v -140.0 80.0 -1050.0
                      TriggerRadius = 90.0 } |]
             Atmosphere =
                 { SunDir = Vec3.normalize (v -0.7 0.4 -0.5)
                   SunColor = v 0.78 0.92 1.20
                   Ambient = v 0.11 0.16 0.26
                   ClearColor = v 0.005 0.007 0.022 } }

           { Id = "morgenlicht-bastion"
             Name = "BASTION MORGENLICHT"
             Subtitle = "Vorderster Flotten-Vorposten vor der Blockade; knappe Vorräte, höchste Preise"
             DangerLevel = 4
             AsteroidDensity = 1.20
             OreRichness = 0.22
             Outpost =
                 { Id = "station-bastion"
                   Name = "Flotten-Dock Morgenlicht"
                   Kind = FleetDock
                   Position = v 220.0 30.0 -640.0
                   DockRadius = 100.0
                   ApproachRadius = 280.0
                   HasRepair = true
                   HasUpgrades = true }
             Gates =
                 [| { TargetSectorId = "kepler-yard"
                      Label = "Sprungtor → Orbitalwerft 07"
                      Position = v -360.0 -20.0 420.0
                      TriggerRadius = 85.0 }
                    { TargetSectorId = "aether-relay"
                      Label = "Sprungtor → Äther-Relais"
                      Position = v 360.0 20.0 420.0
                      TriggerRadius = 85.0 } |]
             Atmosphere =
                 { SunDir = Vec3.normalize (v 0.8 0.35 -0.45)
                   SunColor = v 1.20 0.82 0.55
                   Ambient = v 0.18 0.12 0.14
                   ClearColor = v 0.016 0.005 0.008 } } |]

    let tryFindSector (id: string) =
        sectors |> Array.tryFind (fun s -> s.Id = id)

    let sectorByIndex (index: int) =
        sectors.[max 0 (min (sectors.Length - 1) index)]

    /// Bewertet, ob das Schiff nahe genug am Handelsposten ist, um anzudocken.
    /// Aktiver Kampf blockiert das Andocken nur, wenn Feinde noch nahe der Station stehen.
    let evaluateDocking (outpost: Outpost) (shipPos: Vec3) (combatActive: bool) =
        let dist = Vec3.distance shipPos outpost.Position
        if dist <= outpost.DockRadius then
            if combatActive then BlockedByCombat dist
            else ReadyToDock dist
        elif dist <= outpost.ApproachRadius then
            Approaching dist
        else
            TooFar dist

    /// Normierter Fortschritt 0..1 innerhalb der Anflugzone (fuer HUD-Balken / Leitstrahl).
    let approachProgress (outpost: Outpost) (shipPos: Vec3) =
        let dist = Vec3.distance shipPos outpost.Position
        if dist <= outpost.DockRadius then 1.0
        elif dist >= outpost.ApproachRadius then 0.0
        else 1.0 - (dist - outpost.DockRadius) / max 1.0 (outpost.ApproachRadius - outpost.DockRadius)

    /// Liefert das naechstgelegene Sprungtor, falls das Schiff im Ausloesebereich steht.
    let findTriggeredGate (sector: SectorDef) (shipPos: Vec3) =
        sector.Gates
        |> Array.tryFind (fun gate -> Vec3.distance shipPos gate.Position <= gate.TriggerRadius)
