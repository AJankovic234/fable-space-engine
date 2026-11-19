namespace SpaceEngine

open System

/// Abmessungen eines Gegnermodells in Metern (Bug -Z, oben +Y). Kollision, Geschütze und
/// Leuchtpunkte gehören zum Modell, damit Hülle, Treffer und Glühen deckungsgleich bleiben.
type EnemyHull =
    { /// Hüllkugel um die Schiffsposition (Broad Phase); umschließt die OBB.
      Radius: float
      ObbCenter: Vec3
      ObbHalf: Vec3
      /// Mündungen der beiden feuernden Rohre. Ihr Seitenabstand entscheidet, wie oft eine
      /// mittig gezielte Salve den Spieler trifft, und entspricht deshalb dem alten Modell.
      GunLeft: Vec3
      GunRight: Vec3
      /// Weitere Rohre, die beim Aufladen mitglühen (nur Optik).
      Batteries: Vec3[]
      /// Größe von Mündungsfeuer und Ladeglühen.
      MuzzleSize: float
      /// Düsenaustritt (Mitte) und Radius; die Flamme reicht nach +Z.
      Engines: (Vec3 * float)[]
      /// Positionslicht in Rollenfarbe, auch auf große Entfernung als Punkt sichtbar.
      Beacon: Vec3
      /// Karminrote Blitzer an den Außenkanten.
      Strobes: Vec3[] }

/// Gegnerflotte im AETHER-Stil: Elfenbeinpanzer über schwarzem Bauch, Goldkanten, Karmin.
/// Die großen Hell-Dunkel-Flächen tragen die Silhouette vor Sternen und hellem Nebel; die
/// Rollenfarbe des HUD leuchtet in Sensoren, Kanzeln und Lichtleisten. Alle Maße in Metern.
[<RequireQualifiedAccess>]
module EnemyMeshes =
    let private ivory = MeshColor.rgb 0.47 0.475 0.47
    let private black = MeshColor.rgb 0.035 0.04 0.05
    let private graphite = MeshColor.rgb 0.065 0.07 0.085
    let private steel = MeshColor.rgb 0.19 0.20 0.22
    let private gold = MeshColor.rgb 0.54 0.345 0.07
    let private carmine = MeshColor.rgb 0.34 0.016 0.024
    let private redLamp = MeshColor.glow 1.0 0.10 0.05 1.0
    let private hot = MeshColor.glow 1.0 0.95 0.84 1.0
    let private window = MeshColor.glow 1.0 0.30 0.10 0.84
    let private engineCore = MeshColor.glow 1.0 0.66 0.34 1.0
    let private engineWall = MeshColor.glow 0.90 0.26 0.08 0.72

    let private oval n x y z rx ry = ProceduralMeshes.ring (n, x, y, z, rx, ry)
    let private section = ProceduralMeshes.hullSection

    /// Spantenrumpf entlang Z aus (z, halbe Breite, Unterkante, Oberkante).
    let private keel (b: MeshBuilder) (stations: (float * float * float * float)[]) color =
        b.Loft(stations |> Array.map (fun (z, w, bottom, top) -> section 0.0 z w bottom top), true, true, color)

    /// Dunkler Bauch: untere Rumpfhälfte, leicht aufgedickt. Zweifarbig bleibt der Rumpf
    /// vor dunklem Raum (Elfenbein) wie vor hellem Nebel (Schwarz) lesbar.
    let private belly (b: MeshBuilder) (stations: (float * float * float * float)[]) share color =
        let last = stations.Length - 1
        let rings =
            stations
            |> Array.mapi (fun i (z, w, bottom, top) ->
                // Bug- und Heckdeckel liegen knapp vor denen des Rumpfs (kein Z-Fighting).
                let z = if i = 0 then z - 0.02 elif i = last then z + 0.02 else z
                section 0.0 z (w + 0.03) (bottom - 0.03) (bottom + (top - bottom) * share))
        b.Loft(rings, true, true, color)

    /// Halbe Breite, Unter- und Oberkante eines Spantenrumpfs an der Stelle z.
    let private profileAt (stations: (float * float * float * float)[]) z =
        let key i = let k, _, _, _ = stations.[i] in k
        let mutable i = 0
        while i < stations.Length - 2 && z > key (i + 1) do
            i <- i + 1
        let z0, w0, b0, t0 = stations.[i]
        let z1, w1, b1, t1 = stations.[i + 1]
        let t = max 0.0 (min 1.0 ((z - z0) / (z1 - z0)))
        w0 + (w1 - w0) * t, b0 + (b1 - b0) * t, t0 + (t1 - t0) * t

    /// Farbband um den Rumpf zwischen z0 und z1.
    let private band (b: MeshBuilder) stations z0 z1 grow color =
        let ring z =
            let w, bottom, top = profileAt stations z
            section 0.0 z (w + grow) (bottom - grow) (top + grow)
        b.Loft([| ring z0; ring z1 |], true, true, color)

    /// Triebwerksglocke mit Austritt bei `exit` (Öffnung nach +Z); die Glut sitzt im Hals.
    let private bell (b: MeshBuilder) (exit: Vec3) radius =
        let n = 12
        let depth = radius * 0.9
        let hoop z r = oval n exit.X exit.Y z r r
        let throat = hoop (exit.Z - depth) (radius * 0.82)
        let lip = hoop exit.Z radius
        let inner = hoop exit.Z (radius * 0.80)
        let core = hoop (exit.Z - depth * 0.6) (radius * 0.58)
        b.Loft([| throat; lip |], true, false, steel)
        for i in 0 .. n - 1 do
            let j = (i + 1) % n
            let a = 2.0 * Math.PI * (float i + 0.5) / float n
            let outside = Vec3(exit.X + cos a * radius * 2.0, exit.Y + sin a * radius * 2.0, exit.Z - depth * 0.4)
            b.PolygonOutward([| lip.[i]; lip.[j]; inner.[j]; inner.[i] |], Vec3(exit.X, exit.Y, exit.Z - depth), black)
            b.PolygonOutward([| inner.[i]; inner.[j]; core.[j]; core.[i] |], outside, engineWall)
        b.PolygonOutward(core, Vec3(exit.X, exit.Y, exit.Z - depth * 2.0), engineCore)

    /// Rundes Geschützrohr entlang -Z mit dunkler Mündungsbremse vor `muzzle`.
    let private barrel (b: MeshBuilder) (muzzle: Vec3) rearZ rearRadius frontRadius =
        let tube z r = oval 10 muzzle.X muzzle.Y z r r
        let brake = muzzle.Z + frontRadius * 1.6
        b.Loft([| tube rearZ rearRadius; tube brake frontRadius |], true, false, steel)
        b.Loft([| tube brake (frontRadius * 1.35); tube muzzle.Z (frontRadius * 1.35) |], true, true, black)

    /// Tragfläche als Hexaeder zwischen Wurzel (x0) und Spitze (x1).
    let private wing (b: MeshBuilder) x0 front0 rear0 half0 x1 front1 rear1 half1 y color =
        b.Hexahedron(
            [| Vec3(x0, y - half0, front0); Vec3(x1, y - half1, front1); Vec3(x1, y - half1, rear1); Vec3(x0, y - half0, rear0)
               Vec3(x0, y + half0, front0); Vec3(x1, y + half1, front1); Vec3(x1, y + half1, rear1); Vec3(x0, y + half0, rear0) |],
            color)

    /// Elliptischer Rahmenring um die Längsachse aus n Segmenten (eigene Farbe je Segment).
    let private hoopFrame (b: MeshBuilder) n rx ry ix iy z0 z1 (paint: float -> MeshColor) =
        let p (a: float) sx sy z = Vec3(cos a * sx, sin a * sy, z)
        for i in 0 .. n - 1 do
            let a0 = 2.0 * Math.PI * float i / float n
            let a1 = 2.0 * Math.PI * float (i + 1) / float n
            b.Hexahedron(
                [| p a0 rx ry z0; p a1 rx ry z0; p a1 ix iy z0; p a0 ix iy z0
                   p a0 rx ry z1; p a1 rx ry z1; p a1 ix iy z1; p a0 ix iy z1 |],
                paint ((a0 + a1) * 0.5))

    let private lamp (b: MeshBuilder) (p: Vec3) size color = b.Box(p, Vec3(size, size, size), color)

    let private mirrored (p: Vec3) = [| Vec3(-p.X, p.Y, p.Z); p |]

    let private frame center half (gun: Vec3) batteries muzzle engines beacon strobes =
        { Radius = Vec3.length center + Vec3.length half
          ObbCenter = center
          ObbHalf = half
          GunLeft = Vec3(-gun.X, gun.Y, gun.Z)
          GunRight = gun
          Batteries = batteries
          MuzzleSize = muzzle
          Engines = engines
          Beacon = beacon
          Strobes = strobes }

    // ---------------------------------------------------------------------------------
    //  Drohne: Linsenkern mit Sensorauge in einem breiten Rahmenring. Von vorn ein
    //  leuchtendes Auge im Ring, von der Seite eine flache Scheibe mit Finnen.
    // ---------------------------------------------------------------------------------

    let private droneAccent = MeshColor.glow 1.0 0.42 0.06 1.0

    let private droneHull =
        { Radius = ShipHardpoints.interceptorRadius
          ObbCenter = ShipHardpoints.interceptorObbCenter
          ObbHalf = ShipHardpoints.interceptorObbHalf
          GunLeft = ShipHardpoints.interceptorGunLeft
          GunRight = ShipHardpoints.interceptorGunRight
          Batteries = [||]
          MuzzleSize = 0.9
          Engines = [| Vec3(0.0, 0.0, 2.25), 0.5 |]
          Beacon = Vec3(0.0, 2.05, 0.6)
          Strobes = mirrored (Vec3(2.36, 0.81, -0.15)) }

    let drone () : MeshData =
        let b = MeshBuilder()
        let lens z rx ry = oval 12 0.0 0.0 z rx ry
        b.Loft([| lens -1.98 0.66 0.54; lens -1.70 1.02 0.82; lens -0.70 1.20 0.92
                  lens 0.90 1.12 0.86; lens 1.85 0.76 0.60 |], true, true, ivory)
        b.Loft([| lens -0.34 1.235 0.955; lens 0.34 1.215 0.94 |], true, true, black)
        b.Loft([| lens -1.66 1.06 0.855; lens -1.55 1.085 0.87 |], true, true, gold)
        // Sensorauge: schwarzer Kragen, Rollenfarbe, heiße Pupille
        b.Loft([| lens -1.95 0.71 0.58; lens -2.12 0.60 0.49 |], false, true, black)
        b.PolygonOutward(lens -2.15 0.47 0.38, Vec3.zero, droneAccent)
        b.PolygonOutward(oval 10 0.0 0.0 -2.18 0.17 0.15, Vec3.zero, hot)

        // Rahmenring: Elfenbein oben und unten, schwarze Flanken, Goldkante vorn
        hoopFrame b 16 2.85 1.38 2.42 1.0 -0.72 0.42 (fun a -> if abs (sin a) > 0.55 then ivory else black)
        hoopFrame b 16 2.89 1.42 2.74 1.29 -0.79 -0.69 (fun _ -> gold)

        let gun = ShipHardpoints.interceptorGunRight
        for side in [| -1.0; 1.0 |] do
            let x v = side * v
            b.Box(Vec3(x 1.85, 0.0, -0.15), Vec3(0.65, 0.15, 0.38), graphite)
            b.Box(Vec3(x 2.56, 0.0, -0.80), Vec3(0.07, 0.40, 0.03), droneAccent)
            b.Box(Vec3(x gun.X, 0.0, -0.95), Vec3(0.21, 0.24, 0.62), steel)
            b.Box(Vec3(x gun.X, 0.26, -0.95), Vec3(0.15, 0.03, 0.5), gold)
            barrel b (Vec3(x gun.X, gun.Y, gun.Z)) -1.5 0.11 0.08
            lamp b (Vec3(x 2.36, 0.81, -0.15)) 0.09 redLamp
            b.Box(Vec3(x 2.87, 0.0, -0.15), Vec3(0.035, 0.32, 0.5), droneAccent)

        // Rückenfinne mit Positionslicht, Bauchfinne
        b.Slab([| Vec3(0.0, 0.75, -0.7); Vec3(0.0, 1.98, 0.15); Vec3(0.0, 1.98, 1.0); Vec3(0.0, 0.7, 1.45) |],
               [| 0.09; 0.05; 0.05; 0.09 |], Vec3.unitX, graphite)
        ProceduralMeshes.beam b (Vec3(0.0, 0.82, -0.66)) (Vec3(0.0, 1.98, 0.15)) 0.06 gold
        b.Slab([| Vec3(0.0, -0.75, -0.4); Vec3(0.0, -1.62, 0.3); Vec3(0.0, -1.62, 0.95); Vec3(0.0, -0.7, 1.3) |],
               [| 0.08; 0.05; 0.05; 0.08 |], Vec3.unitX, black)
        b.Box(droneHull.Beacon, Vec3(0.08, 0.07, 0.2), droneAccent)
        for nozzle, radius in droneHull.Engines do
            bell b nozzle radius
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Späher: Nadelrumpf mit langer Kanzel, gepfeilten Klingen und Nadelkanonen.
    // ---------------------------------------------------------------------------------

    let private scoutAccent = MeshColor.glow 0.30 0.92 1.0 1.0

    let private scoutStations =
        [| -5.4, 0.05, -0.04, 0.04
           -4.3, 0.26, -0.20, 0.18
           -2.4, 0.52, -0.38, 0.40
           -0.2, 0.66, -0.44, 0.50
           2.0, 0.70, -0.44, 0.46
           3.3, 0.52, -0.34, 0.34 |]

    let private scoutHull =
        frame (Vec3(0.0, 0.1, -0.88)) (Vec3(2.0, 0.6, 4.52)) (Vec3(1.82, 0.0, -2.9)) [||] 0.8
            [| Vec3(-1.0, 0.0, 3.65), 0.32; Vec3(1.0, 0.0, 3.65), 0.32 |]
            (Vec3(0.0, 1.31, 2.9)) (mirrored (Vec3(2.64, -0.06, 1.9)))

    let scout () : MeshData =
        let b = MeshBuilder()
        let glass = MeshColor.glow 0.22 0.72 0.86 0.78
        keel b scoutStations ivory
        belly b scoutStations 0.38 black
        band b scoutStations -3.05 -2.75 0.025 carmine
        band b scoutStations -2.75 -2.62 0.03 gold
        // Rollenfarbene Flankenlinie, auch auf der Schattenseite lesbar
        for side in [| -1.0; 1.0 |] do
            let flank z = let w, _, _ = profileAt scoutStations z in Vec3(side * (w + 0.02), 0.06, z)
            ProceduralMeshes.beam b (flank -2.4) (flank -0.2) 0.04 scoutAccent
            ProceduralMeshes.beam b (flank -0.2) (flank 0.4) 0.04 scoutAccent
        b.Loft([| section 0.0 3.3 0.53 -0.35 0.35; section 0.0 3.45 0.40 -0.26 0.26 |], false, true, black)
        b.Box(Vec3(0.0, 0.0, -4.95), Vec3(0.07, 0.07, 0.35), scoutAccent)

        // Lange Kanzel in Rollenfarbe
        b.Loft([| section 0.0 -2.6 0.08 0.30 0.38; section 0.0 -1.9 0.30 0.34 0.66
                  section 0.0 -0.4 0.34 0.42 0.72; section 0.0 0.5 0.22 0.44 0.58 |], true, true, glass)
        ProceduralMeshes.beam b (Vec3(0.0, 0.67, -1.9)) (Vec3(0.0, 0.73, -0.4)) 0.035 black

        let gun = scoutHull.GunRight
        for side in [| -1.0; 1.0 |] do
            let p x y z = Vec3(side * x, y, z)
            b.Slab([| p 0.55 0.0 -1.4; p 2.55 -0.06 1.2; p 2.62 -0.06 2.2; p 0.55 0.0 2.7 |],
                   [| 0.12; 0.05; 0.05; 0.12 |], Vec3.unitY, black)
            ProceduralMeshes.armorPanel b [| p 0.66 0.10 -0.9; p 2.2 0.03 1.3; p 2.25 0.03 1.95; p 0.66 0.10 2.3 |]
                Vec3.unitY 0.035 ivory graphite
            ProceduralMeshes.beam b (p 0.62 0.04 -1.36) (p 2.55 -0.03 1.17) 0.05 scoutAccent
            b.Box(p 2.6 -0.06 1.75, Vec3(0.07, 0.07, 0.5), scoutAccent)
            // Nadelkanone vor der Klinge
            let muzzle = Vec3(side * gun.X, gun.Y, gun.Z)
            let tube z r = oval 8 muzzle.X 0.0 z r r
            b.Loft([| tube 1.0 0.16; tube -0.4 0.17; tube -1.2 0.11 |], true, false, graphite)
            barrel b muzzle -1.2 0.11 0.06
            // Triebwerksgondel
            let pod z r = oval 10 (side * 1.0) 0.0 z r r
            b.Loft([| pod 0.0 0.12; pod 0.7 0.34; pod 3.0 0.37; pod 3.35 0.34 |], true, true, graphite)
            b.Loft([| pod 1.2 0.38; pod 1.45 0.38 |], true, true, carmine)
            lamp b (p 2.64 -0.06 1.9) 0.07 redLamp

        // Heckfinne mit Positionslicht
        b.Slab([| Vec3(0.0, 0.42, 1.4); Vec3(0.0, 1.25, 2.6); Vec3(0.0, 1.25, 3.15); Vec3(0.0, 0.36, 3.3) |],
               [| 0.07; 0.04; 0.04; 0.07 |], Vec3.unitX, graphite)
        ProceduralMeshes.beam b (Vec3(0.0, 0.46, 1.45)) (Vec3(0.0, 1.25, 2.6)) 0.04 gold
        b.Box(scoutHull.Beacon, Vec3(0.06, 0.06, 0.16), scoutAccent)
        for nozzle, radius in scoutHull.Engines do
            bell b nozzle radius
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Kanonenboot: breiter Querträger mit Seitenbatterien und zwei schweren Bugrohren.
    // ---------------------------------------------------------------------------------

    let private gunshipAccent = MeshColor.glow 1.0 0.26 0.34 1.0

    let private gunshipStations =
        [| -4.8, 0.55, -0.30, 0.25
           -3.8, 1.25, -0.70, 0.60
           -1.0, 1.55, -0.85, 0.85
           2.6, 1.55, -0.85, 0.80
           4.4, 1.10, -0.60, 0.55 |]

    let private gunshipHull =
        frame (Vec3(0.0, 0.15, -0.05)) (Vec3(5.0, 0.95, 4.75)) (Vec3(2.5, 0.0, -3.9)) [||] 1.5
            [| Vec3(-0.62, 0.0, 4.75), 0.48; Vec3(0.62, 0.0, 4.75), 0.48
               Vec3(-4.45, 0.0, 3.3), 0.42; Vec3(4.45, 0.0, 3.3), 0.42 |]
            (Vec3(0.0, 2.08, 2.6)) (mirrored (Vec3(4.45, 0.9, 0.0)))

    let gunship () : MeshData =
        let b = MeshBuilder()
        keel b gunshipStations ivory
        belly b gunshipStations 0.5 black
        band b gunshipStations -3.3 -2.95 0.03 carmine
        band b gunshipStations -2.95 -2.82 0.035 gold

        // Brücke mit Fensterband und Mast
        b.Loft([| section 0.0 0.2 0.55 0.75 1.25; section 0.0 0.8 0.85 0.75 1.45
                  section 0.0 2.9 0.85 0.75 1.40; section 0.0 3.4 0.60 0.75 1.20 |], true, true, graphite)
        b.Box(Vec3(0.0, 1.47, 1.8), Vec3(0.55, 0.03, 0.9), gold)
        ProceduralMeshes.beam b (Vec3(-0.46, 1.36, 0.47)) (Vec3(0.46, 1.36, 0.47)) 0.05 window
        ProceduralMeshes.beam b (Vec3(0.0, 1.45, 2.4)) (Vec3(0.0, 2.0, 2.6)) 0.05 steel
        b.Box(gunshipHull.Beacon, Vec3(0.08, 0.08, 0.08), gunshipAccent)

        let gun = gunshipHull.GunRight
        for side in [| -1.0; 1.0 |] do
            let p x y z = Vec3(side * x, y, z)
            // Querträger: schwarze Front, Elfenbeinplatten, leuchtende Vorderkante
            wing b (side * 1.3) -1.9 2.0 0.34 (side * 4.0) -1.1 1.7 0.30 0.0 black
            ProceduralMeshes.armorPanel b [| p 1.6 0.33 -1.6; p 3.9 0.30 -0.95; p 3.9 0.30 1.5; p 1.6 0.33 1.8 |]
                Vec3.unitY 0.05 graphite black
            ProceduralMeshes.beam b (p 1.6 0.0 -1.86) (p 3.95 0.0 -1.13) 0.05 gunshipAccent
            // Schweres Bugrohr in der Schulter
            let muzzle = Vec3(side * gun.X, gun.Y, gun.Z)
            b.Box(p gun.X 0.0 -1.75, Vec3(0.34, 0.33, 0.5), steel)
            b.Box(p gun.X 0.34 -1.75, Vec3(0.26, 0.03, 0.4), gold)
            barrel b muzzle -2.2 0.21 0.15
            // Seitenkasematte mit drei Breitseitenpforten
            let x = side * 4.45
            let sponson = [| -2.9, 0.42, -0.55, 0.50; -2.3, 0.70, -0.85, 0.85; 2.2, 0.70, -0.85, 0.85; 2.9, 0.48, -0.60, 0.60 |]
            b.Loft(sponson |> Array.map (fun (z, w, bottom, top) -> section x z w bottom top), true, true, ivory)
            b.Loft([| section x -2.95 0.44 -0.57 0.52; section x -2.75 0.55 -0.70 0.68 |], true, true, black)
            b.Loft([| section x -1.7 0.73 -0.88 0.88; section x -1.4 0.73 -0.88 0.88 |], true, true, carmine)
            b.Loft([| section x -0.4 0.73 -0.88 -0.1; section x 2.3 0.73 -0.88 -0.1 |], true, true, black)
            for z in [| -1.0; 0.1; 1.2 |] do
                b.Box(p 5.16 0.25 z, Vec3(0.03, 0.15, 0.24), gunshipAccent)
                ProceduralMeshes.beam b (p 5.15 0.25 z) (p 5.5 0.25 z) 0.07 steel
            lamp b (p 4.45 0.9 0.0) 0.08 redLamp

        for nozzle, radius in gunshipHull.Engines do
            bell b nozzle radius
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Bomber: Buckelrumpf über einem großen Nutzlastbehälter, zwei schwere Gondeln.
    // ---------------------------------------------------------------------------------

    let private bomberAccent = MeshColor.glow 0.78 0.40 1.0 1.0

    let private bomberStations =
        [| -6.0, 0.45, -0.25, 0.20
           -5.0, 1.10, -0.70, 0.70
           -2.6, 1.55, -0.85, 1.25
           1.5, 1.60, -0.85, 1.30
           4.6, 1.20, -0.70, 0.85
           5.6, 0.85, -0.50, 0.55 |]

    let private bomberHull =
        frame (Vec3(0.0, -0.45, 0.0)) (Vec3(4.95, 1.85, 6.0)) (Vec3(2.88, 0.0, -3.55)) [||] 1.5
            [| Vec3(-4.15, 0.0, 5.3), 0.68; Vec3(4.15, 0.0, 5.3), 0.68; Vec3(0.0, 0.1, 6.0), 0.5 |]
            (Vec3(0.0, 2.08, 1.12)) (mirrored (Vec3(5.56, 0.0, 1.1)))

    let bomber () : MeshData =
        let b = MeshBuilder()
        let glass = MeshColor.glow 0.55 0.32 0.85 0.72
        keel b bomberStations ivory
        belly b bomberStations 0.42 black
        band b bomberStations -4.5 -4.15 0.03 carmine
        band b bomberStations -4.15 -4.02 0.035 gold
        b.Loft([| section 0.0 -5.1 0.45 0.45 0.62; section 0.0 -4.3 0.85 0.55 1.00
                  section 0.0 -3.3 0.90 0.70 1.22 |], true, true, glass)
        // Rückenwulst mit Goldschienen und Mast
        b.Loft([| section 0.0 -2.4 0.5 1.1 1.42; section 0.0 2.8 0.7 1.1 1.45; section 0.0 4.2 0.4 0.8 1.1 |], true, true, black)
        for side in [| -1.0; 1.0 |] do
            ProceduralMeshes.beam b (Vec3(side * 0.5, 1.42, -2.2)) (Vec3(side * 0.66, 1.45, 2.6)) 0.04 gold
        ProceduralMeshes.beam b (Vec3(0.0, 1.43, 0.8)) (Vec3(0.0, 2.0, 1.1)) 0.05 steel
        b.Box(bomberHull.Beacon, Vec3(0.08, 0.08, 0.08), bomberAccent)

        // Nutzlast: Karminspitze, schwarzer Körper, Ladungsringe in Rollenfarbe
        let can z r = oval 12 0.0 -1.5 z r (r * 0.9)
        b.Loft([| can -4.75 0.08; can -4.55 0.35; can -4.1 0.70; can -3.6 0.88 |], true, false, carmine)
        b.Loft([| can -3.6 0.88; can 2.4 0.92; can 3.2 0.60; can 3.5 0.30 |], false, true, black)
        for z in [| -2.1; 0.5 |] do
            b.Loft([| can z 0.95; can (z + 0.25) 0.95 |], true, true, bomberAccent)
        for z in [| -2.6; 1.2 |] do
            b.Box(Vec3(0.0, -0.85, z), Vec3(0.3, 0.2, 0.45), graphite)

        let gun = bomberHull.GunRight
        for side in [| -1.0; 1.0 |] do
            let p x y z = Vec3(side * x, y, z)
            wing b (side * 1.3) -2.2 3.2 0.28 (side * 3.5) -1.3 2.8 0.22 0.05 black
            ProceduralMeshes.armorPanel b [| p 1.65 0.33 -1.95; p 3.4 0.28 -1.2; p 3.4 0.28 2.6; p 1.65 0.33 3.0 |]
                Vec3.unitY 0.05 ivory graphite
            ProceduralMeshes.beam b (p 1.4 0.05 -2.25) (p 3.5 0.05 -1.35) 0.06 bomberAccent
            // Bugrohre in der Flügelwurzel
            let muzzle = Vec3(side * gun.X, gun.Y, gun.Z)
            b.Box(p gun.X -0.05 -1.75, Vec3(0.26, 0.26, 0.45), steel)
            barrel b muzzle -2.15 0.16 0.12
            // Schwere Gondel mit leuchtendem Einlass
            let x = side * 4.15
            let pod z r = oval 12 x 0.0 z r r
            b.Loft([| pod -3.0 0.32; pod -2.5 0.80; pod -1.45 0.82 |], true, false, ivory)
            b.Loft([| pod -1.45 0.82; pod 3.8 0.84; pod 4.6 0.74 |], false, true, graphite)
            b.Loft([| pod -3.0 0.34; pod -3.08 0.26 |], false, true, black)
            b.PolygonOutward(oval 10 x 0.0 -3.1 0.22 0.22, Vec3(x, 0.0, 0.0), bomberAccent)
            b.Loft([| pod -1.6 0.85; pod -1.3 0.85 |], true, true, carmine)
            b.Loft([| pod 3.4 0.86; pod 3.65 0.86 |], true, true, gold)
            ProceduralMeshes.beam b (p 4.87 0.42 -2.3) (p 4.88 0.42 3.5) 0.05 bomberAccent
            // Außenflügel mit Raketen und Blitzer
            wing b (side * 4.8) -0.7 2.4 0.13 (side * 5.5) 0.3 1.9 0.09 0.0 black
            let rocket z r = oval 8 (side * 5.1) -0.32 z r r
            b.Loft([| rocket -1.32 0.03; rocket -1.0 0.165 |], true, false, bomberAccent)
            b.Loft([| rocket -1.0 0.165; rocket 1.8 0.165; rocket 2.1 0.10 |], false, true, ivory)
            lamp b (p 5.56 0.0 1.1) 0.08 redLamp

        for nozzle, radius in bomberHull.Engines do
            bell b nozzle radius
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Kommando-Korvette: dunkler Dolchrumpf mit goldenem Bug, Kommandoturm und Mast,
    //  Seitenbatterien und fünf Triebwerken. Die Fensterreihen verraten ihre Größe.
    // ---------------------------------------------------------------------------------

    let private corvetteAccent = MeshColor.glow 1.0 0.80 0.32 1.0

    let private corvetteStations =
        [| -13.0, 0.35, -0.25, 0.20
           -10.5, 1.70, -0.95, 0.80
           -5.5, 3.10, -1.55, 1.25
           1.0, 3.80, -1.80, 1.55
           8.0, 3.80, -1.80, 1.55
           10.6, 3.30, -1.55, 1.30 |]

    let private corvetteHull =
        frame (Vec3(0.0, 0.4, -0.85)) (Vec3(7.0, 2.6, 12.15)) (Vec3(4.03, 0.0, -8.05))
            (Array.append (mirrored (Vec3(6.55, 0.4, -6.65))) (mirrored (Vec3(6.55, -0.4, -6.65)))) 2.0
            [| Vec3(-2.3, 0.0, 11.3), 0.95; Vec3(0.0, 0.15, 11.4), 1.15; Vec3(2.3, 0.0, 11.3), 0.95
               Vec3(-6.55, 0.0, 7.3), 0.5; Vec3(6.55, 0.0, 7.3), 0.5 |]
            (Vec3(0.0, 6.1, 4.55))
            (Array.append (mirrored (Vec3(6.55, 1.02, 1.0))) [| Vec3(0.0, -3.1, 5.2) |])

    let corvette () : MeshData =
        let b = MeshBuilder()
        let stations = corvetteStations
        keel b stations graphite
        belly b stations 0.5 black
        band b stations -8.3 -7.7 0.04 carmine
        band b stations -7.7 -7.5 0.05 gold
        band b stations 8.85 9.0 0.05 gold
        band b stations 9.0 9.6 0.04 carmine

        let top z = let _, _, t = profileAt stations z in t
        // Goldener Bugpanzer und Elfenbeinplatten beiderseits des Turms. Jede Platte liegt
        // innerhalb eines Spantenabschnitts, wo das Deck eben ist.
        let deck (outline: (float * float)[]) color =
            let points = outline |> Array.map (fun (x, z) -> Vec3(x, top z, z))
            let normal = Vec3.normalize (Vec3.cross (points.[2] - points.[0]) (points.[1] - points.[0]))
            let normal = if normal.Y < 0.0 then -normal else normal
            ProceduralMeshes.armorPanel b points normal 0.07 color black
        deck [| 0.0, -12.6; 0.9, -10.6; -0.9, -10.6 |] gold
        deck [| 0.0, -10.3; 1.25, -8.6; 2.05, -5.6; -2.05, -5.6; -1.25, -8.6 |] gold
        for s in [| -1.0; 1.0 |] do
            deck [| s * 1.0, -5.3; s * 2.2, -5.2; s * 2.7, 0.9; s * 1.0, 0.9 |] ivory
            deck [| s * 1.0, 1.1; s * 2.75, 1.1; s * 2.75, 7.9; s * 1.0, 7.9 |] ivory

        // Flanken: Karminstreifen über einer Fensterreihe
        let side z =
            let w, bottom, top = profileAt stations z
            let bevel = (top - bottom) * 0.23
            w, top - bevel
        for s in [| -1.0; 1.0 |] do
            let zs = [| -9.0; -5.5; -2.5; 1.0; 4.5; 8.0; 8.6 |]
            for k in 0 .. zs.Length - 2 do
                let z0, z1 = zs.[k], zs.[k + 1]
                let w0, h0 = side z0
                let w1, h1 = side z1
                b.PolygonOutward(
                    [| Vec3(s * (w0 + 0.03), h0 - 0.12, z0); Vec3(s * (w1 + 0.03), h1 - 0.12, z1)
                       Vec3(s * (w1 + 0.03), h1 - 0.42, z1); Vec3(s * (w0 + 0.03), h0 - 0.42, z0) |],
                    Vec3(0.0, h0 - 0.3, (z0 + z1) * 0.5), carmine)
            let mutable z = -8.6
            while z < 8.2 do
                let w, h = side z
                b.Box(Vec3(s * (w + 0.02), h - 0.72, z), Vec3(0.04, 0.10, 0.22), window)
                z <- z + 0.95

        // Kommandoturm mit Brücke, Fensterband und Mast
        b.Loft([| section 0.0 -3.2 1.2 1.3 1.85; section 0.0 -1.8 1.9 1.3 2.3
                  section 0.0 7.6 1.9 1.3 2.3; section 0.0 9.0 1.4 1.3 1.95 |], true, true, graphite)
        b.Loft([| section 0.0 0.2 1.3 2.2 2.95; section 0.0 1.3 1.75 2.2 3.6
                  section 0.0 5.2 1.75 2.2 3.6; section 0.0 6.2 1.35 2.2 3.2 |], true, true, ivory)
        b.Loft([| section 0.0 1.1 1.25 3.5 3.95; section 0.0 4.6 1.25 3.5 4.0 |], true, true, gold)
        ProceduralMeshes.beam b (Vec3(-1.2, 3.3, 0.70)) (Vec3(1.2, 3.3, 0.70)) 0.09 window
        ProceduralMeshes.beam b (Vec3(-0.95, 3.76, 1.04)) (Vec3(0.95, 3.76, 1.04)) 0.06 corvetteAccent
        for s in [| -1.0; 1.0 |] do
            let mutable z = 1.8
            while z < 5.0 do
                b.Box(Vec3(s * 1.78, 3.0, z), Vec3(0.03, 0.12, 0.2), window)
                z <- z + 0.6
        ProceduralMeshes.beam b (Vec3(0.0, 3.95, 3.6)) (Vec3(0.0, 6.0, 4.5)) 0.1 steel
        ProceduralMeshes.beam b (Vec3(-1.1, 5.2, 4.15)) (Vec3(1.1, 5.2, 4.15)) 0.06 steel
        for s in [| -1.0; 1.0 |] do
            lamp b (Vec3(s * 1.12, 5.2, 4.15)) 0.07 redLamp
        b.Box(corvetteHull.Beacon, Vec3(0.14, 0.14, 0.14), corvetteAccent)

        // Kielflosse
        b.Slab([| Vec3(0.0, -1.7, 0.5); Vec3(0.0, -3.0, 2.8); Vec3(0.0, -3.0, 7.6); Vec3(0.0, -1.7, 9.6) |],
               [| 0.16; 0.1; 0.1; 0.16 |], Vec3.unitX, black)
        ProceduralMeshes.beam b (Vec3(0.0, -1.75, 0.6)) (Vec3(0.0, -3.0, 2.8)) 0.06 gold
        lamp b (Vec3(0.0, -3.06, 5.2)) 0.09 redLamp

        let gun = corvetteHull.GunRight
        for s in [| -1.0; 1.0 |] do
            let p x y z = Vec3(s * x, y, z)
            // Tragflächen zu den Batterien
            wing b (s * 3.4) -3.8 7.6 0.42 (s * 6.0) -1.6 6.2 0.32 -0.1 black
            ProceduralMeshes.armorPanel b [| p 3.9 0.33 -3.2; p 5.9 0.24 -1.4; p 5.9 0.24 5.9; p 3.9 0.33 7.0 |]
                Vec3.unitY 0.06 ivory graphite
            ProceduralMeshes.beam b (p 3.5 -0.1 -3.85) (p 6.0 -0.1 -1.65) 0.07 corvetteAccent
            // Schwere Hauptrohre in der Flügelwurzel
            let muzzle = Vec3(s * gun.X, gun.Y, gun.Z)
            b.Box(p gun.X 0.0 -3.6, Vec3(0.5, 0.48, 0.85), steel)
            b.Box(p gun.X 0.5 -3.6, Vec3(0.38, 0.04, 0.7), gold)
            barrel b muzzle -4.4 0.30 0.22
            // Seitenbatterie mit Zwillingsrohren
            let x = s * 6.55
            let sponson = [| -4.6, 0.45, -0.50, 0.45; -3.7, 0.80, -0.95, 0.90; 5.6, 0.80, -0.95, 0.90; 6.8, 0.55, -0.65, 0.65 |]
            b.Loft(sponson |> Array.map (fun (z, w, bottom, top) -> section x z w bottom top), true, true, ivory)
            b.Loft([| section x -3.0 0.83 -0.98 -0.05; section x 5.7 0.83 -0.98 -0.05 |], true, true, black)
            b.Loft([| section x -2.2 0.84 -0.99 0.94; section x -1.8 0.84 -0.99 0.94 |], true, true, carmine)
            ProceduralMeshes.beam b (p 6.55 0.93 -3.2) (p 6.55 0.93 5.2) 0.05 gold
            ProceduralMeshes.beam b (p 7.37 0.3 -3.4) (p 7.37 0.3 5.4) 0.06 corvetteAccent
            for y in [| -0.4; 0.4 |] do
                barrel b (Vec3(x, y, -6.65)) -3.8 0.17 0.13
            lamp b (p 6.55 1.0 1.0) 0.09 redLamp

        for nozzle, radius in corvetteHull.Engines do
            bell b nozzle radius
        b.Build()

    // Sperrjaeger: zwei nach vorn gezogene Waffenarme um den schmalen Rumpf.
    let private blockerStations =
        [| -3.0, 0.49, -0.36, 0.30
           -1.72, 1.29, -0.75, 0.78
           0.46, 1.43, -0.86, 0.95
           2.61, 1.19, -0.65, 0.74
           3.56, 0.73, -0.41, 0.52 |]

    let private blockerHull =
        frame (Vec3(0.0, 0.13, -0.29)) (Vec3(4.75, 0.84, 4.02)) (Vec3(3.59, 0.0, -4.45)) [||] 1.2
            [| Vec3(-0.74, -0.09, 3.79), 0.35; Vec3(0.74, -0.09, 3.79), 0.35 |]
            (Vec3(0.0, 1.38, 2.66)) (mirrored (Vec3(4.78, 0.18, -3.06)))

    let blocker () : MeshData =
        let b = MeshBuilder()
        let enamel = MeshColor.rgb 0.27 0.31 0.165
        let accent = MeshColor.glow 0.83 1.0 0.30 0.96
        let glass = MeshColor.glass 0.14 0.23 0.18 0.22
        keel b blockerStations steel
        belly b blockerStations 0.46 black
        band b blockerStations -1.4 -1.08 0.035 enamel
        b.Loft([| section 0.0 -2.18 0.18 0.25 0.43; section 0.0 -1.31 0.63 0.43 1.10
                  section 0.0 0.51 0.61 0.72 1.16; section 0.0 1.09 0.38 0.74 0.96 |], true, true, glass)
        ProceduralMeshes.beam b (Vec3(0.0, 1.10, -1.31)) (Vec3(0.0, 1.16, 0.51)) 0.04 steel
        ProceduralMeshes.beam b (Vec3(0.0, 0.66, 2.05)) blockerHull.Beacon 0.1 graphite
        lamp b blockerHull.Beacon 0.12 accent

        let gun = blockerHull.GunRight
        for side in [| -1.0; 1.0 |] do
            let p x y z = Vec3(side*x, y, z)
            // Volle Wurzel, geknickter Holm, schmaler Haken an der Fluegelspitze.
            wing b (side*0.98) -0.92 2.92 0.27 (side*2.72) -1.04 2.72 0.31 -0.16 graphite
            wing b (side*2.72) -1.04 2.72 0.31 (side*4.22) -4.06 0.14 0.24 -0.16 graphite
            wing b (side*4.22) -4.06 0.14 0.24 (side*4.79) -4.26 -2.77 0.13 -0.16 steel
            ProceduralMeshes.armorPanel b
                [| p 1.16 0.13 -0.68; p 2.56 0.16 -0.78; p 2.90 0.17 1.98; p 1.17 0.13 2.55 |]
                Vec3.unitY 0.065 ivory graphite
            ProceduralMeshes.armorPanel b
                [| p 2.74 0.17 -0.92; p 4.18 0.10 -3.82; p 4.25 0.10 -0.16; p 3.04 0.17 1.93 |]
                Vec3.unitY 0.075 enamel graphite
            ProceduralMeshes.beam b (p 1.17 0.14 -0.88) (p 2.72 0.18 -0.96) 0.065 accent
            ProceduralMeshes.beam b (p 2.72 0.18 -0.96) (p 4.24 0.11 -4.01) 0.065 accent
            ProceduralMeshes.beam b (p 4.20 -0.12 -1.8) (p 4.78 0.18 -3.06) 0.11 graphite
            lamp b (p 4.78 0.18 -3.06) 0.09 redLamp
            b.Box(p gun.X -0.02 -2.65, Vec3(0.45, 0.47, 0.94), black)
            b.Box(p gun.X 0.49 -2.45, Vec3(0.32, 0.07, 0.68), enamel)
            barrel b (p gun.X gun.Y gun.Z) -2.87 0.26 0.18
            for vent in 0..3 do
                b.Box(p (1.64 + float vent*0.18) 0.25 1.06, Vec3(0.055, 0.085, 0.5), graphite)
            let nozzle z radius = oval 8 (side*0.74) -0.09 z radius radius
            b.Loft([| nozzle 2.52 0.39; nozzle 3.16 0.48; nozzle 3.56 0.39 |], true, true, graphite)

        for nozzle, radius in blockerHull.Engines do
            bell b nozzle radius
        b.Build()

    let private hulls = [| droneHull; scoutHull; gunshipHull; bomberHull; corvetteHull; blockerHull |]

    /// Maße und Effektpunkte der Rolle (EnemyRole als Zahl).
    let hull (role: int) : EnemyHull = hulls.[max 0 (min (hulls.Length - 1) role)]
