namespace SpaceEngine

open System

/// Zollkreuzer der AETHER-Blockade vor dem Helios-Tor. Gebaut in Konstruktionseinheiten
/// und mit CapitalFlight.hullScale gezeichnet. Bug +X, oben +Y; die Schauseite mit den
/// beiden echten Batterien zeigt nach +Z, zum Anflug von der Raffinerie.
[<RequireQualifiedAccess>]
module CapitalMeshes =
    // Dunkler Stahl mit hellen Panzerplatten, AETHER-Karmin und Goldkanten. Die vielen
    // kleinen roten Lichter verraten die Größe, bevor man die Silhouette einordnet.
    let private hull = MeshColor.rgb 0.12 0.13 0.15
    let private plate = MeshColor.rgb 0.28 0.295 0.31
    let private bright = MeshColor.rgb 0.45 0.46 0.47
    let private seam = MeshColor.rgb 0.035 0.04 0.05
    let private crimson = MeshColor.rgb 0.50 0.045 0.055
    let private gold = MeshColor.rgb 0.70 0.50 0.19
    let private windowLit = MeshColor.glow 1.0 0.30 0.11 0.86
    let private windowDim = MeshColor.glow 0.70 0.15 0.07 0.60
    let private signal = MeshColor.glow 1.0 0.07 0.04 1.0
    let private bridgeGlass = MeshColor.glow 0.95 0.17 0.07 0.80
    let private floodlight = MeshColor.glow 1.0 0.72 0.38 0.90
    let private heat = MeshColor.glow 0.95 0.22 0.06 0.62
    let private driveCore = MeshColor.glow 0.80 0.92 1.0 1.0
    let private driveWall = MeshColor.glow 0.22 0.42 0.95 0.60

    /// Düsenaustritte und Radien. Alle Triebwerke zeigen nach -X.
    let drives =
        [| Vec3(-85.6, 2.6, 0.0), 4.6
           Vec3(-84.1, -4.0, -5.4), 3.4
           Vec3(-84.1, -4.0, 5.4), 3.4
           Vec3(-80.3, -1.0, -19.0), 3.6
           Vec3(-80.3, -1.0, 19.0), 3.6 |]

    /// Rote Positionslichter: Masttopp, Brückennocken, Rammsporn, Kiel, Klingen, Gondeln.
    let beacons =
        [| Vec3(-22.0, 50.7, 0.0)
           Vec3(-17.0, 34.1, -18.3); Vec3(-17.0, 34.1, 18.3)
           Vec3(100.6, -0.8, 0.0); Vec3(-22.0, -24.0, 0.0)
           Vec3(24.0, -2.0, -20.2); Vec3(24.0, -2.0, 20.2)
           Vec3(-40.6, -1.0, -19.0); Vec3(-40.6, -1.0, 19.0) |]

    /// Achtkantspant quer zur Längsachse X: halbe Breite w in Z, Unter- und Oberkante in Y.
    let private rib x w bottom top =
        ProceduralMeshes.hullSection 0.0 x w bottom top |> Array.map (fun v -> Vec3(v.Z, v.Y, -v.X))

    /// Kreis in der YZ-Ebene bei Längsposition x (Düsen und Läufe entlang X).
    let private hoop n x cy cz r =
        Array.init n (fun i ->
            let a = 2.0 * Math.PI * float i / float n
            Vec3(x, cy + sin a * r, cz + cos a * r))

    /// Kreis in der XZ-Ebene bei Höhe y (Drehkränze, Radom).
    let private collar n cx y cz r =
        Array.init n (fun i ->
            let a = 2.0 * Math.PI * float i / float n
            Vec3(cx + cos a * r, y, cz + sin a * r))

    /// Außenkontur des Panzers (x, halbe Breite, Unterkante, Oberkante) vom Heck zum Sporn.
    /// Zwischen x -11 und 11 liegt die offene Inspektionsbucht.
    let private armour =
        [| -70.0, 13.0, -9.0, 8.5
           -60.0, 17.0, -11.0, 10.0
           -14.0, 17.0, -11.0, 10.0
           -11.0, 16.0, -10.2, 9.4
           11.0, 16.0, -10.2, 9.4
           14.0, 17.0, -11.0, 10.0
           38.0, 16.5, -10.8, 9.4
           58.0, 11.5, -8.5, 6.5
           76.0, 5.5, -5.0, 3.5
           92.0, 0.6, -1.0, 0.6 |]

    let private armourRib i grow =
        let x, w, bottom, top = armour.[i]
        rib x (w + grow) (bottom - grow) (top + grow)

    /// Halbe Breite, Unter- und Oberkante des Panzers an der Stelle x.
    let private profile x =
        let key i = let k, _, _, _ = armour.[i] in k
        let mutable i = 0
        while i < armour.Length - 2 && x > key (i + 1) do
            i <- i + 1
        let x0, w0, b0, t0 = armour.[i]
        let x1, w1, b1, t1 = armour.[i + 1]
        let t = max 0.0 (min 1.0 ((x - x0) / (x1 - x0)))
        w0 + (w1 - w0) * t, b0 + (b1 - b0) * t, t0 + (t1 - t0) * t

    /// Ring um den Panzer bei x, um `grow` aufgedickt (Farbbänder, Fugen).
    let private band (b: MeshBuilder) x0 x1 grow color =
        let section x =
            let w, bottom, top = profile x
            rib x (w + grow) (bottom - grow) (top + grow)
        b.Loft([| section x0; section x1 |], true, true, color)

    /// Triebwerksglocke: Hals bei x, Austritt bei x - 1.25 r. Die Glut sitzt tief im Hals.
    let private bell (b: MeshBuilder) x y z r =
        let n = 14
        let throat = hoop n x y z (r * 0.80)
        let mid = hoop n (x - r * 0.55) y z (r * 0.93)
        let lip = hoop n (x - r * 1.25) y z (r * 1.07)
        let inner = hoop n (x - r * 1.25) y z (r * 0.90)
        let core = hoop n (x - r * 0.35) y z (r * 0.70)
        b.Loft([| throat; mid; lip |], false, false, plate)
        for i in 0 .. n - 1 do
            let j = (i + 1) % n
            let a = 2.0 * Math.PI * (float i + 0.5) / float n
            let behind = Vec3(x - r * 0.8, y + sin a * r * 2.0, z + cos a * r * 2.0)
            b.PolygonOutward([| lip.[i]; lip.[j]; inner.[j]; inner.[i] |], Vec3(x, y, z), seam)
            b.PolygonOutward([| inner.[i]; inner.[j]; core.[j]; core.[i] |], behind, driveWall)
        b.PolygonOutward(core, Vec3(x + r, y, z), driveCore)

    /// Platte mit aus dem Umriss berechneter Normale (schräge Bleche, Schottflügel).
    let private sheet (b: MeshBuilder) (outline: Vec3[]) thickness color =
        let normal = Vec3.normalize (Vec3.cross (outline.[1] - outline.[0]) (outline.[3] - outline.[0]))
        b.Slab(outline, Array.create 4 thickness, normal, color)

    /// Schwerer Drillingsturm auf Barbette. `facing` ±1 entlang X; `up` -1 hängt ihn unter den Rumpf.
    let private heavyTurret (b: MeshBuilder) (mount: Vec3) facing up =
        let place (v: Vec3) = Vec3(mount.X + v.X * facing, mount.Y + v.Y * up, mount.Z + v.Z)
        let local (sections: Vec3[][]) = sections |> Array.map (Array.map place)
        b.Loft(local [| collar 12 0.0 -1.7 0.0 3.4; collar 12 0.0 0.0 0.0 3.1 |], true, true, plate)
        b.Loft(local [| rib -3.5 2.4 0.0 1.5; rib -2.7 3.2 0.0 2.4; rib 1.8 3.2 0.0 2.6; rib 3.4 2.3 0.3 1.7 |],
               true, true, hull)
        b.Loft(local [| rib -2.5 2.6 2.3 2.75; rib 1.5 2.6 2.45 2.9 |], true, true, bright)
        for z in [| -1.15; 0.0; 1.15 |] do
            let barrel x r = hoop 8 x 1.25 z r
            b.Loft(local [| barrel 2.4 0.55; barrel 3.6 0.44; barrel 10.6 0.40 |], false, false, seam)
            b.Loft(local [| barrel 10.4 0.57; barrel 11.7 0.57 |], true, true, hull)

    /// Doppelläufige Breitseitenbatterie, Läufe nach +Z (side = -1 spiegelt nach -Z).
    let private batteryInto (b: MeshBuilder) (origin: Vec3) side =
        let p (v: Vec3) = Vec3(origin.X + v.X, origin.Y + v.Y, origin.Z + v.Z * side)
        let along (sections: Vec3[][]) = sections |> Array.map (Array.map p)
        let section z w bottom top = ProceduralMeshes.hullSection 0.0 z w bottom top
        b.Loft(along [| collar 14 0.0 -2.3 0.0 5.3; collar 14 0.0 -1.0 0.0 5.5; collar 14 0.0 0.0 0.0 4.7 |],
               true, true, plate)
        b.Loft(along [| section -4.6 3.0 -1.2 1.6; section -2.4 4.7 -1.2 3.8
                        section 2.4 4.4 -1.2 3.0; section 4.2 3.4 -1.0 1.5 |], true, true, hull)
        b.Loft(along [| section -2.2 3.9 3.55 4.05; section 2.0 3.7 2.8 3.3 |], true, true, crimson)
        for x in [| -2.1; 2.1 |] do
            let tube z r = ProceduralMeshes.ring (10, x, 0.9, z, r, r)
            b.Loft(along [| tube 2.0 1.15; tube 4.6 1.05; tube 12.2 0.86 |], false, false, seam)
            b.Loft(along [| tube 11.8 1.24; tube 13.6 1.24 |], true, true, hull)
            b.PolygonOutward((along [| tube 13.66 0.74 |]).[0], p (Vec3(x, 0.9, 12.0)), signal)
        b.Box(p (Vec3(0.0, 2.45, 4.35)), Vec3(0.95, 0.42, 0.32), signal)

    let cruiser () : MeshData =
        let b = MeshBuilder()
        let rng = Rng(2601)
        let shell sections color = b.Loft(sections, true, true, color)
        let box x y z hx hy hz color = b.Box(Vec3(x, y, z), Vec3(hx, hy, hz), color)
        let beam = ProceduralMeshes.beam b

        // Druckrumpf: in der Inspektionsbucht zwischen den Panzerschalen sichtbar.
        shell [| rib -74.0 10.0 -7.0 7.0; rib -60.0 12.0 -9.5 9.0; rib 24.0 12.0 -9.5 9.0; rib 40.0 11.0 -9.0 8.0 |] hull
        // Antriebsblock mit Panzerkragen und fünf Glocken.
        shell [| rib -80.0 10.5 -7.5 7.5; rib -74.0 11.5 -8.5 8.5; rib -62.0 12.6 -9.6 9.2 |] hull
        shell [| rib -77.5 12.0 -8.9 8.9; rib -72.0 12.9 -9.7 9.7 |] plate
        for x in [| -70.5; -66.0 |] do
            shell [| rib x 13.6 -10.1 10.1; rib (x + 1.1) 13.6 -10.1 10.1 |] seam
        for p, r in drives do
            bell b (p.X + r * 1.25) p.Y p.Z r

        // Panzerschalen: Heck bis zur Bucht, dann Bug bis zum Rammsporn.
        shell [| for i in 0..3 -> armourRib i 0.0 |] plate
        shell [| for i in 4..9 -> armourRib i 0.0 |] plate
        // AETHER-Karmin um den Bug, von Goldfugen gefasst, und eine schmale Wiederholung.
        band b 42.0 46.5 0.30 crimson
        band b 41.2 41.8 0.36 gold
        band b 46.8 47.4 0.36 gold
        band b 49.0 50.2 0.28 crimson
        band b -60.0 -58.6 0.30 seam
        band b -14.0 -12.6 0.30 seam
        band b 14.0 15.4 0.30 seam

        // Inspektionsbucht: Sturz und Schwelle über beide Flanken, Gerüst und Flutlicht.
        box 0.0 8.4 0.0 11.2 1.9 16.6 plate
        box 0.0 -8.6 0.0 11.2 2.0 16.6 plate
        for side in [| -1.0; 1.0 |] do
            let p x y z = Vec3(x, y, z * side)
            for x in [| -10.2; 10.2 |] do
                beam (p x -6.6 15.9) (p x 6.5 15.9) 0.55 hull
            beam (p -10.0 6.0 16.2) (p 10.0 6.0 16.2) 0.42 hull
            beam (p -10.0 -6.1 16.2) (p 10.0 -6.1 16.2) 0.42 hull
            for x in [| -9.4; 9.4 |] do
                for y in [| -5.7; 5.6 |] do
                    box x y (15.6 * side) 0.65 0.32 0.38 floodlight
            for x in [| -6.0; 0.0; 6.0 |] do
                box x 9.2 (16.62 * side) 1.4 0.18 0.06 windowDim

        // Panzerplatten und Fensterreihen auf beiden Flanken.
        let flankPanel side x0 x1 y0 y1 color =
            let w0, _, _ = profile x0
            let w1, _, _ = profile x1
            let normal = Vec3.normalize (Vec3(-(w1 - w0), 0.0, (x1 - x0) * side))
            let pt x w y = Vec3(x, y, w * side)
            ProceduralMeshes.armorPanel b [| pt x0 w0 y0; pt x1 w1 y0; pt x1 w1 y1; pt x0 w0 y1 |] normal 0.32 color seam
        let windowRow side (width: float -> float) x0 x1 step y =
            let mutable x = x0
            while x <= x1 do
                if rng.NextFloat() > 0.2 then
                    let lit = if rng.NextFloat() > 0.28 then windowLit else windowDim
                    box x y ((width x + 0.05) * side) 0.46 0.23 0.10 lit
                x <- x + step
        let armourWidth x = let w, _, _ = profile x in w
        let deckA x = 12.5 + (11.0 - 12.5) * (x + 48.0) / 66.0
        let deckB x = 9.5 + (8.5 - 9.5) * (x + 38.0) / 38.0
        for side in [| -1.0; 1.0 |] do
            for x0, x1, y0 in [| -58.0, -44.5, -5.6; -43.5, -30.0, -5.6; -29.0, -15.0, -5.6
                                 15.0, 25.5, -5.6; 26.5, 37.5, -5.6; 51.0, 57.5, -4.8 |] do
                flankPanel side x0 x1 y0 -1.7 bright
            flankPanel side 59.0 72.0 -3.0 1.4 bright
            flankPanel side -58.0 -15.0 4.0 4.8 crimson
            windowRow side armourWidth -58.0 -15.5 1.7 3.0
            windowRow side armourWidth -58.0 -15.5 1.7 0.6
            windowRow side armourWidth 15.5 37.0 1.7 3.0
            windowRow side armourWidth 15.5 37.0 1.7 0.6
            windowRow side armourWidth 51.5 56.5 1.6 0.4
            windowRow side deckA -46.0 16.0 1.5 10.8
            windowRow side deckB -37.0 -1.0 1.4 16.2
            windowRow side (fun x -> 7.0 + (6.4 - 7.0) * (x + 33.0) / 27.0) -32.0 -7.0 1.3 21.6

        // Klingen: flache Panzerkragen entlang der Flanken, Goldkante bis zum Sporn.
        for side in [| -1.0; 1.0 |] do
            let p x z = Vec3(x, -2.0, z * side)
            b.Slab([| p -8.0 16.8; p 24.0 19.6; p 62.0 12.6; p 94.0 0.3 |], [| 0.9; 0.9; 0.6; 0.25 |], Vec3.unitY, plate)
            beam (p 24.0 19.75) (p 62.0 12.75) 0.28 gold
            beam (p 62.0 12.75) (p 94.0 0.45) 0.24 gold
            box 24.0 -2.0 (19.9 * side) 0.4 0.4 0.3 signal
        // Rammsporn unter dem Bug und Kielblatt unter der Bucht.
        b.Slab([| Vec3(34.0, -9.6, 0.0); Vec3(76.0, -4.2, 0.0); Vec3(100.0, -0.8, 0.0); Vec3(54.0, -15.5, 0.0) |],
               [| 2.2; 1.4; 0.3; 1.0 |], Vec3.unitZ, plate)
        beam (Vec3(54.0, -15.5, 0.0)) (Vec3(100.0, -0.8, 0.0)) 0.36 gold
        box 100.2 -0.8 0.0 0.45 0.4 0.4 signal
        b.Slab([| Vec3(-50.0, -10.4, 0.0); Vec3(8.0, -10.4, 0.0); Vec3(-4.0, -25.0, 0.0); Vec3(-40.0, -22.0, 0.0) |],
               [| 1.5; 1.5; 0.8; 0.8 |], Vec3.unitZ, plate)
        beam (Vec3(-40.0, -22.0, 0.0)) (Vec3(-4.0, -25.0, 0.0)) 0.34 crimson
        for x in [| -36.0; -30.0; -24.0; -18.0; -12.0 |] do
            for side in [| -1.0; 1.0 |] do
                box x -16.0 (1.3 * side) 0.9 0.22 0.12 windowLit
        box -22.0 -23.4 0.0 0.5 0.4 1.0 signal
        // Firstgrat über dem Bug.
        beam (Vec3(38.0, 9.55, 0.0)) (Vec3(58.0, 6.65, 0.0)) 0.7 plate
        beam (Vec3(58.0, 6.65, 0.0)) (Vec3(91.0, 0.75, 0.0)) 0.5 gold

        // Triebwerksgondeln mit Kühlrippen; die Rippenkanten glühen.
        for side in [| -1.0; 1.0 |] do
            let nz = 19.0 * side
            let pod x w = rib x w (-1.0 - w) (-1.0 + w) |> Array.map (fun v -> Vec3(v.X, v.Y, v.Z + nz))
            shell [| pod -76.0 3.6; pod -72.0 4.5; pod -50.0 4.5; pod -44.0 3.0; pod -41.0 1.2 |] plate
            for x in [| -68.0; -63.0; -58.0; -53.0 |] do
                shell [| pod x 4.75; pod (x + 0.9) 4.75 |] seam
            b.Slab([| Vec3(-74.0, -1.0, 12.0 * side); Vec3(-50.0, -1.0, 12.0 * side)
                      Vec3(-48.0, -1.0, 16.0 * side); Vec3(-70.0, -1.0, 16.0 * side) |],
                   Array.create 4 1.2, Vec3.unitY, hull)
            for dz in [| -2.1; 0.0; 2.1 |] do
                let z = nz + dz
                b.Slab([| Vec3(-70.0, 3.2, z); Vec3(-52.0, 3.2, z); Vec3(-55.0, 10.5, z); Vec3(-68.0, 10.5, z) |],
                       Array.create 4 0.32, Vec3.unitZ, hull)
                beam (Vec3(-67.5, 10.5, z)) (Vec3(-55.5, 10.5, z)) 0.26 heat
            for dz in [| -1.2; 1.2 |] do
                let z = nz + dz
                b.Slab([| Vec3(-70.0, -5.2, z); Vec3(-52.0, -5.2, z); Vec3(-56.0, -11.0, z); Vec3(-67.0, -11.0, z) |],
                       Array.create 4 0.32, Vec3.unitZ, hull)
                beam (Vec3(-66.5, -11.0, z)) (Vec3(-56.5, -11.0, z)) 0.26 heat
            box -40.8 -1.0 nz 0.4 0.4 0.4 signal

        // Gestufte Aufbauten: Deck A trägt die Rohrleitungen, Deck B den Kommandoturm.
        shell [| rib -54.0 10.0 8.0 11.5; rib -48.0 12.5 8.0 13.5; rib 18.0 11.0 8.0 13.5; rib 30.0 6.5 8.0 10.6 |] hull
        shell [| rib -44.0 8.0 13.0 17.0; rib -38.0 9.5 13.0 19.0; rib 0.0 8.5 13.0 19.0; rib 6.0 5.0 13.0 15.5 |] plate
        shell [| rib -37.0 5.5 18.5 21.5; rib -33.0 7.0 18.5 23.5; rib -6.0 6.4 18.5 23.5; rib -2.0 4.2 18.5 21.0 |] hull
        for z in [| -10.4; 10.4 |] do
            beam (Vec3(-47.0, 13.85, z)) (Vec3(17.0, 13.85, z * 0.9)) 0.42 seam
        // Kommandoturm: Panzersockel, umlaufendes Brückenglas, überstehendes Dach.
        shell [| rib -30.0 4.0 23.0 29.0; rib -26.0 6.5 23.0 35.5; rib -12.0 5.5 23.0 34.5; rib -8.0 3.2 23.0 30.0 |] plate
        shell [| rib -27.5 7.4 30.4 32.0; rib -24.0 9.6 30.2 31.8; rib -12.5 8.4 30.2 31.8; rib -9.5 6.2 30.4 32.0 |] plate
        shell [| rib -27.5 7.6 31.8 33.8; rib -24.0 9.8 31.6 34.2; rib -12.5 8.6 31.6 34.0; rib -9.5 6.4 31.8 33.4 |] bridgeGlass
        shell [| rib -28.5 8.2 34.2 35.2; rib -24.5 10.4 34.4 36.4; rib -12.5 9.2 34.2 36.0; rib -9.0 6.8 34.0 35.0 |] hull
        for side in [| -1.0; 1.0 |] do
            box -17.0 32.6 (13.8 * side) 3.0 1.3 4.4 plate
            box -13.9 32.8 (13.8 * side) 0.14 0.55 4.2 bridgeGlass
            box -17.0 34.05 (18.0 * side) 0.4 0.3 0.4 signal
        beam (Vec3(-21.0, 36.0, 0.0)) (Vec3(-22.0, 50.2, 0.0)) 0.6 hull
        beam (Vec3(-21.7, 45.0, -6.0)) (Vec3(-21.7, 45.0, 6.0)) 0.32 hull
        beam (Vec3(-14.0, 35.8, 0.0)) (Vec3(-14.5, 41.5, 0.0)) 0.38 hull
        box -22.0 50.5 0.0 0.45 0.4 0.45 signal
        b.Loft([| collar 10 -26.5 35.9 0.0 1.9; collar 10 -26.5 37.6 0.0 1.8; collar 10 -26.5 38.8 0.0 1.0
                  collar 10 -26.5 39.2 0.0 0.05 |], true, false, bright)

        // Aufbautenkleinteile: Lüfter, Kästen, Antennensockel. Der Maßstab entsteht hier.
        let scatter count x0 x1 z0 z1 y =
            for _ in 1..count do
                let x, z = rng.Range(x0, x1), rng.Range(z0, z1)
                let hx, hy, hz = rng.Range(0.35, 1.5), rng.Range(0.2, 0.8), rng.Range(0.35, 1.3)
                let roll = rng.NextFloat()
                let color = if roll < 0.45 then hull elif roll < 0.85 then plate elif roll < 0.95 then bright else windowDim
                box x (y + hy) z hx hy hz color
        scatter 14 -47.0 -40.0 -9.5 9.5 13.5
        scatter 18 6.0 16.0 -8.5 8.5 13.3
        scatter 12 -43.0 -38.0 -7.5 7.5 19.0
        scatter 10 0.0 5.0 -6.0 6.0 19.0
        scatter 12 -32.0 -30.5 -5.5 5.5 23.5
        scatter 10 -7.0 -3.0 -5.0 5.0 23.5
        scatter 14 -68.0 -61.0 -9.0 9.0 9.2
        scatter 10 40.0 47.0 -9.0 9.0 9.0

        // Drillingstürme: zwei überhöht am Bug, einer achtern, vier hängend unter dem Rumpf.
        heavyTurret b (Vec3(36.5, 11.0, 0.0)) 1.0 1.0
        heavyTurret b (Vec3(52.0, 8.8, 0.0)) 1.0 1.0
        heavyTurret b (Vec3(-58.0, 11.4, 0.0)) (-1.0) 1.0
        for z in [| -7.5; 7.5 |] do
            heavyTurret b (Vec3(44.0, -11.6, z)) 1.0 (-1.0)
            heavyTurret b (Vec3(-56.0, -12.5, z)) (-1.0) (-1.0)
        // Sponsons der Breitseitenbatterien. Die Schauseite trägt die echten Geschütze,
        // auf der Gegenseite sitzen baugleiche Attrappen.
        for side in [| -1.0; 1.0 |] do
            box -35.0 4.6 (17.5 * side) 6.5 1.6 4.4 plate
            box 33.0 3.9 (16.4 * side) 6.4 2.3 4.6 plate
            box 33.0 0.25 (17.0 * side) 4.0 1.4 2.0 hull
        for gun in CapitalFlight.gunOffsets do
            batteryInto b (Vec3(gun.X, gun.Y, -gun.Z) / CapitalFlight.hullScale) (-1.0)
        b.Build()

    /// Echte Batterie der Schauseite: getrennt gezeichnet, damit sie ausfallen und dem Ziel folgen kann.
    let battery () : MeshData =
        let b = MeshBuilder()
        batteryInto b Vec3.zero 1.0
        b.Build()

    /// Schweres Schott der Inspektionsbucht auf beiden Flanken. Nach dem ersten Torpedotreffer
    /// ist es aufgerissen, dahinter glüht die gebrochene Leitung.
    let shutter breached : MeshData =
        let b = MeshBuilder()
        let hot = MeshColor.glow 1.25 0.31 0.045 0.94
        let soot = MeshColor.rgb 0.05 0.04 0.035
        for side in [| -1.0; 1.0 |] do
            let p x y z = Vec3(x, y, z * side)
            if breached then
                b.Box(p 0.3 0.0 12.9, Vec3(8.4, 4.9, 0.4), soot)
                for x, y, dx, dy in [ -7.5, 3.8, 4.2, -4.2; -3.3, -0.4, 3.5, 1.5; 0.2, 1.1, 3.8, -4.3
                                      4.0, -3.2, 3.1, 1.1; -6.2, -3.6, 2.6, 2.2; 2.6, 3.4, 3.2, -1.6 ] do
                    ProceduralMeshes.beam b (p x y 13.45) (p (x + dx) (y + dy) 13.45) 0.55 hot
                // Aufgebogene Torflügel und abgerissene Riegel.
                sheet b [| p -8.4 -4.9 13.6; p -2.6 -4.9 13.9; p -3.8 -1.2 19.2; p -8.4 -0.4 17.8 |] 0.42 plate
                sheet b [| p 3.0 4.9 13.9; p 8.4 4.9 13.6; p 8.4 1.4 18.6; p 4.2 0.6 19.6 |] 0.42 plate
                ProceduralMeshes.beam b (p -7.6 -5.0 15.8) (p -5.5 0.4 18.8) 0.85 hull
                ProceduralMeshes.beam b (p 6.2 4.8 17.2) (p 3.8 1.1 19.5) 0.95 hull
            else
                b.Box(p 0.0 0.0 13.4, Vec3(8.6, 5.2, 1.0), plate)
                for x in [| -6.2; -2.1; 2.1; 6.2 |] do
                    b.Box(p x 0.0 14.5, Vec3(1.7, 4.6, 0.18), bright)
                for x in [| -7.6; -4.6; -1.6; 1.4; 4.4 |] do
                    ProceduralMeshes.beam b (p x -4.7 14.78) (p (x + 2.6) -2.1 14.78) 0.32 crimson
                ProceduralMeshes.beam b (p -8.2 2.9 14.85) (p 8.2 2.9 14.85) 0.36 gold
                for x in [| -9.0; 9.0 |] do
                    for y in [| -3.4; 3.4 |] do
                        b.Box(p x y 14.0, Vec3(0.8, 0.9, 0.9), hull)
        b.Build()
