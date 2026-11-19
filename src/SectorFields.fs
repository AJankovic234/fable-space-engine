namespace SpaceEngine

open System

// =====================================================================================
//  SectorFields.fs  -  Eigene Asteroidenlandschaft je Sektor (Handelsflug)
//
//  Früher lag in jedem Sektor derselbe flache Ring um den Ursprung, Erz zufällig
//  verteilt bis an die Station. Jetzt hat jeder Sektor eine eigene Form (Gürtel,
//  Schlackestrom, Treibnester, freigeräumter Raum, Felswall, Eisnester, Trümmerstrom,
//  Nebelbrocken) und eigene Gesteinsfarben. Erz sitzt fast nur noch in benannten
//  Nestern weit draußen (≥ 450 m von der Station) und unter einer Kruste, die man
//  erst aufbrechen muss: Rohstoffe kosten Flugzeit und Feuer.
// =====================================================================================

type FieldShape =
    | Belt
    | SlagStream
    | Pockets
    | Swept
    | Rampart
    | IceNests
    | DebrisStream
    | Boulders

type OreNest =
    { Name: string
      Center: Vec3
      Spread: float
      Count: int
      OreChance: float }

type FieldPlan =
    { Shape: FieldShape
      /// Kurzbeschreibung für die Ankunftsmeldung.
      Label: string
      /// Gesteinsfarbe (wird mit einer zufälligen Helligkeit multipliziert) und Helligkeitsbereich.
      Tint: Vec3
      ShadeMin: float
      ShadeMax: float
      Nests: OreNest[]
      /// Weitere Einsatzorte abseits von Station und Toren (für die Sektorlage).
      Hotspots: Vec3[] }

[<RequireQualifiedAccess>]
module SectorFields =
    let private v x y z = Vec3(x, y, z)

    /// Mindestabstand von Erznestern und Einsatzorten zur Station.
    [<Literal>]
    let NestStationClearance = 450.0

    /// Ausserhalb der Nester ist Erz selten.
    [<Literal>]
    let StrayOreChance = 0.02

    /// Felsfreier Raum um Station und Tore (Anflug und Sprungankunft).
    let stationClearance = 170.0
    let gateClearance = 190.0

    let private nest name center spread count ore =
        { Name = name; Center = center; Spread = spread; Count = count; OreChance = ore }

    let private plan shape label tint lo hi nests hotspots =
        { Shape = shape; Label = label; Tint = tint; ShadeMin = lo; ShadeMax = hi; Nests = nests; Hotspots = hotspots }

    let private plans =
        Universe.sectors |> Array.map (fun sector ->
            match sector.Id with
            | "outer-belt" ->
                plan Belt "Erzgürtel · Erz in zwei Nestern am Rand" (v 1.0 0.93 0.84) 0.36 0.62
                    [| nest "Kestrel-Ader" (v -600.0 20.0 -150.0) 70.0 34 0.5
                       nest "Haldenkamm" (v 480.0 -30.0 180.0) 65.0 30 0.5 |]
                    [| v -150.0 60.0 180.0; v 700.0 10.0 -350.0 |]
            | "slag-refinery" ->
                plan SlagStream "Schlackestrom · Erz an der Zunge des Stroms" (v 1.0 0.70 0.56) 0.26 0.46
                    [| nest "Schlackenzunge" (v -800.0 -40.0 260.0) 75.0 36 0.45
                       nest "Kühlhalde" (v 420.0 -20.0 -180.0) 60.0 26 0.4 |]
                    [| v -520.0 0.0 -900.0; v 150.0 -30.0 80.0 |]
            | "frontier-drift" ->
                plan Pockets "Freihafen · zwischen den Liegeplätzen und den Treibnestern liegt freier Raum" (v 0.98 0.88 0.76) 0.34 0.58
                    [| nest "Stimmennest" (v -560.0 20.0 -480.0) 70.0 32 0.5
                       nest "Schürfergrab" (v 120.0 -60.0 120.0) 65.0 30 0.5 |]
                    [| v -300.0 40.0 -140.0; v 620.0 10.0 -200.0; v -700.0 -30.0 160.0; v 760.0 -40.0 -720.0; v 750.0 -40.0 150.0 |]
            | "kepler-yard" ->
                plan Swept "Werftraum · freie Sicht auf Helling und Schleppverbände" (v 0.92 0.93 0.96) 0.38 0.6
                    [||]
                    [| v -650.0 30.0 -650.0; v 0.0 -40.0 60.0 |]
            | "aether-relay" ->
                plan Swept "Horchraum · freie Peilung vom Antennengitter bis zum Pulsar" (v 0.9 0.92 1.0) 0.4 0.62
                    [||]
                    [| v 100.0 -30.0 -100.0; v -750.0 40.0 -500.0 |]
            | "morgenlicht-bastion" ->
                plan Rampart "Felswall · offene Mitte, Erz im Wall" (v 0.86 0.87 0.94) 0.3 0.5
                    [| nest "Wallader" (v -760.0 0.0 -560.0) 70.0 32 0.5 |]
                    [| v -150.0 -60.0 0.0; v 500.0 -30.0 -1000.0 |]
            | "kaltstrom" ->
                plan IceNests "Eisnester · das reichste Erz, weit verstreut" (v 0.82 0.92 1.0) 0.58 0.86
                    [| nest "Reifnest" (v -520.0 0.0 -420.0) 80.0 40 0.6
                       nest "Glasbruch" (v -150.0 -40.0 120.0) 75.0 38 0.6
                       nest "Raureif-Grat" (v 620.0 -60.0 -1000.0) 75.0 36 0.55 |]
                    [| v 420.0 30.0 120.0 |]
            | "wracksaum" ->
                plan DebrisStream "Trümmerstrom · wenig Erz, viel Deckung" (v 1.0 0.78 0.6) 0.28 0.48
                    [| nest "Konvoierz" (v -620.0 -30.0 -850.0) 60.0 24 0.4 |]
                    [| v -300.0 -20.0 80.0; v 300.0 40.0 -600.0 |]
            | _ ->
                plan Boulders "Nebelbrocken · wenige Riesen, Erz im Zollschatten" (v 0.86 0.82 0.96) 0.3 0.48
                    [| nest "Zollschatten" (v 350.0 -80.0 -950.0) 70.0 28 0.5 |]
                    [| v -600.0 -20.0 -700.0; v 150.0 -40.0 150.0 |])

    let planFor (index: int) = plans.[max 0 (min (plans.Length - 1) index)]

    /// Einsatzorte für die Sektorlage: Erznester zuerst, dann die übrigen Orte.
    let hotspots (index: int) =
        let p = planFor index
        Array.append (p.Nests |> Array.map (fun n -> n.Center)) p.Hotspots

    // ---------------------------------------------------------------------------------
    //  Erzeugung

    let private smallRadius (rng: Rng) = 2.0 + 11.0 * Math.Pow(rng.NextFloat(), 2.0)

    let private mixedRadius (rng: Rng) =
        if rng.NextFloat() < 0.03 then rng.Range(45.0, 80.0) else 2.0 + 26.0 * Math.Pow(rng.NextFloat(), 2.5)

    let private clear (sector: SectorDef) (p: Vec3) radius =
        Vec3.distance p sector.Outpost.Position > stationClearance + radius
        && sector.Gates |> Array.forall (fun g -> Vec3.distance p g.Position > gateClearance + radius)

    /// Legt einen Fels an; `oreChance` entscheidet über Erz, `rich` über Reicherz.
    let private rock (field: AsteroidField) (rng: Rng) (sector: SectorDef) (p: FieldPlan) (position: Vec3) radius oreChance =
        if field.Count < field.Capacity && clear sector position radius then
            let axis = rng.UnitVector()
            let shade = rng.Range(p.ShadeMin, p.ShadeMax)
            let variant = int (rng.NextFloat() * float AsteroidField.Variants) % AsteroidField.Variants
            let added =
                AsteroidField.add field position.X position.Y position.Z 0.0 0.0 0.0 radius axis.X axis.Y axis.Z
                    (rng.Range(0.05, 0.5) * 6.0 / (4.0 + radius)) (rng.Range(0.0, 2.0 * Math.PI))
                    (shade * p.Tint.X) (shade * p.Tint.Y) (shade * p.Tint.Z) variant
            if added && rng.NextFloat() < oreChance then
                let i = field.Count - 1
                field.Ore.[i] <- if rng.NextFloat() < sector.OreRichness then 2uy else 1uy
                let o = i * AsteroidField.Stride
                field.Data.[o + 15] <- float32 (radius * Combat.AsteroidHpPerRadius + Combat.oreCrust radius)

    /// Gaußähnliche Streuung um einen Punkt, flach gedrückt wie ein Feld im Raum.
    let private around (rng: Rng) (center: Vec3) spread =
        let r = spread * (rng.NextFloat() + rng.NextFloat() + rng.NextFloat()) / 1.5
        let d = rng.UnitVector()
        center + Vec3(d.X * r, d.Y * r * 0.55, d.Z * r)

    let private ring (field: AsteroidField) (rng: Rng) (sector: SectorDef) (p: FieldPlan) (center: Vec3) inner outer thickness count =
        for _ in 1 .. count do
            let angle = rng.Range(0.0, 2.0 * Math.PI)
            let distance = rng.Range(inner, outer)
            let height = (rng.NextFloat() + rng.NextFloat() - 1.0) * thickness
            rock field rng sector p (center + Vec3(cos angle * distance, height, sin angle * distance)) (mixedRadius rng) StrayOreChance

    let private stream (field: AsteroidField) (rng: Rng) (sector: SectorDef) (p: FieldPlan) (a: Vec3) (b: Vec3) (width: float) count (radius: Rng -> float) =
        for _ in 1 .. count do
            let t = rng.NextFloat()
            let wobble = sin (t * 9.0) * width * 0.6
            let side = Vec3.normalize (Vec3.cross (b - a) Vec3.up)
            let point = Vec3.lerp a b t + side * wobble
            rock field rng sector p (around rng point width) (radius rng) StrayOreChance

    let private nests (field: AsteroidField) (rng: Rng) (sector: SectorDef) (p: FieldPlan) =
        for n in p.Nests do
            for _ in 1 .. n.Count do
                rock field rng sector p (around rng n.Center n.Spread) (2.5 + 15.0 * Math.Pow(rng.NextFloat(), 1.8)) n.OreChance

    /// Erzeugt die Landschaft des Sektors neu (nur beim Betreten oder Neustart).
    let generate (field: AsteroidField) (rng: Rng) (index: int) =
        let sector = Universe.sectorByIndex index
        let p = planFor index
        field.Count <- 0
        let ring = ring field rng sector p
        let stream = stream field rng sector p
        match p.Shape with
        | Belt ->
            // Drei Startbrocken zwischen Ankunft und Kestrel erklären das Aufbrechen.
            for i in 0 .. 2 do
                rock field rng sector p (v ((float i - 1.0) * 34.0) (float (i % 2) * 18.0 - 6.0) (-130.0 - float i * 45.0)) (9.0 + float i * 1.5) 1.0
            ring (v -830.0 100.0 -430.0) 380.0 690.0 70.0 200
        | SlagStream ->
            stream (v -340.0 10.0 -330.0) (v -860.0 -50.0 330.0) 90.0 200 mixedRadius
        | Pockets ->
            for center in Array.skip 2 p.Hotspots do
                for _ in 1 .. 12 do rock field rng sector p (around rng center 60.0) (smallRadius rng) StrayOreChance
        | Swept -> ()
        | Rampart ->
            ring (v 0.0 0.0 -250.0) 1410.0 1590.0 60.0 230
        | IceNests ->
            let core = p.Hotspots.[0]
            let tail = -sector.Atmosphere.SunDir
            stream (core + tail * 55.) (core + tail * 440.) 45.0 68 smallRadius
        | DebrisStream ->
            stream (v -850.0 0.0 -150.0) (v 250.0 -60.0 250.0) 65.0 86 smallRadius
        | Boulders ->
            for _ in 1 .. 16 do
                let point = v (rng.Range(-900.0, 900.0)) (rng.Range(-120.0, 170.0)) (rng.Range(-1100.0, 400.0))
                rock field rng sector p point (rng.Range(38.0, 85.0)) 0.0
        nests field rng sector p
