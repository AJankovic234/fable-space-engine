namespace SpaceEngine

open System

/// Sichtbare Anbaupunkte eines Spielerrumpfs (Bug -Z, oben +Y, backbord -X). Triebwerke,
/// Mündungen, Kanzel und Kollision bleiben für alle Rümpfe in ShipHardpoints gleich.
type PlayerHull =
    { /// Positionslichter: backbord rot, steuerbord grün.
      PortLight: Vec3
      StarboardLight: Vec3
      /// Weißer Blitzer am höchsten Punkt.
      Beacon: Vec3
      /// Bremsdüsen, Austritt nach vorn (-Z).
      Retros: Vec3[]
      /// Sockel der Ausbaustufen (OutfitVisuals): Schildemitter, Panzerplatten, Schildrelais.
      Shield: Vec3[]
      Armour: Vec3[]
      Relay: Vec3 }

/// Die Schiffe des Spielers: zivile Arbeitsmaschinen mit aufgesetzter Beplankung, dunklem
/// Bauch und Signalfarben. Jede Rumpffacette ist eine eigene Platte; der Grundkörper bleibt
/// als dunkle Fuge sichtbar, damit die Form auch ohne Textur im Streiflicht Tiefe hat.
[<RequireQualifiedAccess>]
module PlayerMeshes =
    let private section = ProceduralMeshes.hullSection
    let private oval n x y z rx ry = ProceduralMeshes.ring (n, x, y, z, rx, ry)
    let private beam = ProceduralMeshes.beam
    let private panel = ProceduralMeshes.armorPanel

    let private seam = MeshColor.rgb 0.050 0.066 0.080
    let private rubber = MeshColor.rgb 0.024 0.032 0.038
    let private iron = MeshColor.rgb 0.11 0.14 0.16
    let private steel = MeshColor.rgb 0.27 0.32 0.35
    let private alloy = MeshColor.rgb 0.46 0.52 0.55
    /// Angelaufener Düsenstahl.
    let private scorch = MeshColor.rgb 0.34 0.24 0.18
    let private hazard = MeshColor.rgb 0.96 0.70 0.07
    let private glass = MeshColor.glow 0.03 0.21 0.29 0.22
    let private portRed = MeshColor.glow 1.0 0.10 0.04 0.95
    let private starGreen = MeshColor.glow 0.08 0.95 0.40 0.90
    let private white = MeshColor.glow 0.95 0.97 1.0 0.95
    let private amber = MeshColor.glow 1.0 0.70 0.25 0.85
    let private gunLamp = MeshColor.glow 0.47 0.92 1.0 0.8
    let private jet = MeshColor.glow 0.80 0.92 1.0 1.0
    let private jetWall = MeshColor.glow 0.06 0.13 0.26 0.30
    let private ember = MeshColor.glow 1.0 0.36 0.10 0.50

    /// Leicht unterschiedlich ausgeblichene Platten: gebrauchter Lack statt Neuware.
    let private worn (seed: int) (s: int) (f: int) (c: MeshColor) =
        let h = (s * 73856093) ^^^ (f * 19349663) ^^^ (seed * 83492791)
        let k = 0.89 + 0.15 * float ((h &&& 0x7fffffff) % 1000) / 1000.0
        MeshColor.rgb (min 1.0 (c.R * k)) (min 1.0 (c.G * k)) (min 1.0 (c.B * k))

    let private pair x y z = [| Vec3(-x, y, z); Vec3(x, y, z) |]

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

    /// Fügt zusätzliche Plattenstöße (Z-Schnitte) in einen Spantenrumpf ein.
    let private refine (stations: (float * float * float * float)[]) (cuts: float list) =
        Array.append (stations |> Array.map (fun (z, _, _, _) -> z)) (Array.ofList cuts)
        |> Array.distinct
        |> Array.sort
        |> Array.map (fun z ->
            let w, bottom, top = profileAt stations z
            z, w, bottom, top)

    /// Aufgesetzte Platte auf einem Viereck: ringsum eine Fuge, dunkle Fase.
    let private quadPanel (b: MeshBuilder) (pts: Vec3[]) (inside: Vec3) gap lift edge color =
        let center = (pts.[0] + pts.[1] + pts.[2] + pts.[3]) / 4.0
        let mutable shortest = 1e9
        for i in 0 .. 3 do
            shortest <- min shortest (Vec3.length (pts.[(i + 1) % 4] - pts.[i]))
        let raw = Vec3.cross (pts.[1] - pts.[0]) (pts.[3] - pts.[0])
        if shortest > gap * 2.6 && Vec3.lengthSq raw > 1e-10 then
            let unit = Vec3.normalize raw
            let n = if Vec3.dot unit (center - inside) < 0.0 then -unit else unit
            let corner k i =
                let p = pts.[i]
                p + Vec3.normalize (pts.[(i + 1) % 4] - p) * (gap * k) + Vec3.normalize (pts.[(i + 3) % 4] - p) * (gap * k)
            let foot = Array.init 4 (corner 0.4)
            let top = Array.init 4 (fun i -> corner 1.0 i + n * lift)
            let core = center - n * lift
            b.PolygonOutward(top, core, color)
            for i in 0 .. 3 do
                let j = (i + 1) % 4
                b.PolygonOutward([| foot.[i]; foot.[j]; top.[j]; top.[i] |], core, edge)

    /// Beplankter Spantenrumpf um die Längsachse bei `x`. `paint` wählt je Segment und
    /// Facette die Farbe (0 Deck, 1/7 obere Fasen, 2/6 Flanken, 3/5 untere Fasen, 4 Kiel);
    /// None lässt die Fuge stehen.
    let private plating (b: MeshBuilder) x (stations: (float * float * float * float)[]) gap lift (paint: int -> int -> MeshColor option) =
        let rings = stations |> Array.map (fun (z, w, bottom, top) -> section x z w bottom top)
        b.Loft(rings, true, true, seam)
        for s in 0 .. rings.Length - 2 do
            let a = rings.[s]
            let c = rings.[s + 1]
            let inside = (Array.reduce (+) a + Array.reduce (+) c) / 16.0
            for f in 0 .. 7 do
                match paint s f with
                | Some color ->
                    let g = (f + 1) % 8
                    quadPanel b [| a.[f]; a.[g]; c.[g]; c.[f] |] inside gap lift seam color
                | None -> ()

    /// Triebwerksglocke mit Austritt bei `exit` (Öffnung nach +Z): angelaufener Ring,
    /// kühl glimmende Innenwand und heller Kern im Hals.
    let private bell (b: MeshBuilder) (exit: Vec3) radius depth metal =
        let n = 14
        let hoop z r = oval n exit.X exit.Y z r r
        let lip = hoop exit.Z radius
        let inner = hoop exit.Z (radius * 0.84)
        let core = hoop (exit.Z - depth * 0.8) (radius * 0.50)
        b.Loft([| hoop (exit.Z - depth) (radius * 0.78); hoop (exit.Z - depth * 0.4) (radius * 0.93); lip |], true, false, metal)
        b.Loft([| hoop (exit.Z - depth * 0.30) (radius * 1.0); hoop (exit.Z - depth * 0.26) (radius * 1.05)
                  hoop (exit.Z - depth * 0.10) (radius * 1.06); hoop (exit.Z - depth * 0.06) (radius * 1.02) |], false, false, scorch)
        for i in 0 .. n - 1 do
            let j = (i + 1) % n
            let a = 2.0 * Math.PI * (float i + 0.5) / float n
            let outside = Vec3(exit.X + cos a * radius * 2.0, exit.Y + sin a * radius * 2.0, exit.Z - depth * 0.4)
            b.PolygonOutward([| lip.[i]; lip.[j]; inner.[j]; inner.[i] |], Vec3(exit.X, exit.Y, exit.Z - depth), iron)
            b.PolygonOutward([| inner.[i]; inner.[j]; core.[j]; core.[i] |], outside, jetWall)
        b.PolygonOutward(core, Vec3(exit.X, exit.Y, exit.Z - depth * 2.0), jet)

    /// Runder Maschinenblock entlang Z mit abgesetzten Spannringen.
    let private engineBlock (b: MeshBuilder) (engine: Vec3) (profile: (float * float)[]) color (collars: float list) collarColor =
        let hoop z r = oval 14 engine.X engine.Y z r r
        b.Loft(profile |> Array.map (fun (z, r) -> hoop z r), true, true, color)
        for z in collars do
            let r = profile |> Array.map snd |> Array.max
            b.Loft([| hoop z (r * 0.99); hoop (z + 0.012) (r * 1.045); hoop (z + 0.058) (r * 1.045); hoop (z + 0.07) (r * 0.99) |],
                   false, false, collarColor)

    /// Trommel quer zur Flugrichtung (Winde, Gelenk).
    let private drumX (b: MeshBuilder) (c: Vec3) radius half color =
        let hoop x r =
            Array.init 10 (fun i ->
                let a = 2.0 * Math.PI * float i / 10.0
                Vec3(x, c.Y + cos a * r, c.Z + sin a * r))
        b.Loft([| hoop (c.X - half) radius; hoop (c.X + half) radius |], true, true, color)

    /// Stehende Trommel (Drehkranz, Poller).
    let private drumY (b: MeshBuilder) (c: Vec3) radius y0 y1 color =
        let hoop y r =
            Array.init 12 (fun i ->
                let a = 2.0 * Math.PI * float i / 12.0
                Vec3(c.X + cos a * r, y, c.Z + sin a * r))
        b.Loft([| hoop y0 radius; hoop y1 radius; hoop (y1 + 0.02) (radius * 0.8) |], true, true, color)

    /// Warnbalken aus `n` gelb-schwarzen Blöcken zwischen zwei Punkten.
    let private hazardBar (b: MeshBuilder) (start: Vec3) (finish: Vec3) (half: Vec3) n =
        for i in 0 .. n - 1 do
            let p = Vec3.lerp start finish ((float i + 0.5) / float n)
            b.Box(p, half, (if i % 2 = 0 then hazard else rubber))

    /// Kurzer Arbeitslaser: Wiege am Rumpf, freier Lauf, offene Mündung am Hardpoint.
    let private gunMount (b: MeshBuilder) (gun: Vec3) housing metal =
        let collar dz r = oval 8 gun.X gun.Y (gun.Z + dz) r r
        b.Loft([| collar 0.12 0.066; collar 0.36 0.072; collar 0.41 0.11; collar 0.80 0.11 |], false, true, metal)
        b.Box(Vec3(gun.X, gun.Y + 0.016, gun.Z + 0.68), Vec3(0.14, 0.11, 0.24), housing)
        b.Box(Vec3(gun.X, gun.Y + 0.132, gun.Z + 0.66), Vec3(0.09, 0.012, 0.16), seam)
        ProceduralMeshes.turretBore b gun.X gun.Y gun.Z 0.085 0.14 metal rubber gunLamp

    /// Kleine Bremsdüse, Öffnung nach vorn.
    let private retro (b: MeshBuilder) (p: Vec3) =
        ProceduralMeshes.turretBore b p.X p.Y p.Z 0.05 0.09 steel rubber ember

    /// Lafettenring: auf Y=0.37 sitzt der Flansch des optionalen Geschützturms.
    let private turretRing (b: MeshBuilder) rim plate =
        let hoop y r =
            Array.init 16 (fun i ->
                let a = 2.0 * Math.PI * (float i + 0.5) / 16.0
                Vec3(cos a * r, y, 0.65 + sin a * r))
        b.Loft([| hoop 0.29 0.50; hoop 0.335 0.56; hoop 0.37 0.56 |], true, true, rim)
        b.Loft([| hoop 0.37 0.43; hoop 0.376 0.42 |], true, true, plate)
        for i in 0 .. 3 do
            let a = Math.PI * (0.25 + 0.5 * float i)
            b.Box(Vec3(cos a * 0.495, 0.374, 0.65 + sin a * 0.495), Vec3(0.03, 0.006, 0.03), hazard)

    let private sides =
        [| -1.0, ShipHardpoints.playerEngineLeft, ShipHardpoints.playerGunLeft
           1.0, ShipHardpoints.playerEngineRight, ShipHardpoints.playerGunRight |]

    let private upper f = f = 0 || f = 1 || f = 7
    let private flank f = f = 2 || f = 6
    let private lower f = f = 3 || f = 5

    // ---------------------------------------------------------------------------------
    //  Bergungskutter: Druckrumpf mit hohem Steuerhaus, zwei Auslegergondeln. Backbord
    //  der offene Frachtkorb, steuerbord Winde und gefalteter Bergekran.
    // ---------------------------------------------------------------------------------

    let cutterHull =
        { PortLight = Vec3(-1.81, -0.03, 1.50)
          StarboardLight = Vec3(1.81, -0.03, 1.50)
          Beacon = Vec3(0.24, 0.93, -0.80)
          Retros = pair 2.05 0.0 -0.92
          Shield = pair 0.62 0.315 1.47
          Armour = pair 0.72 0.37 0.62
          Relay = Vec3(0.0, 0.25, 1.50) }

    let private cutterKeel =
        [| -2.50, 0.24, -0.10, 0.08
           -2.25, 0.46, -0.22, 0.20
           -1.75, 0.60, -0.30, 0.28
           -0.45, 0.66, -0.32, 0.33
           0.12, 0.62, -0.32, 0.33
           0.46, 0.31, -0.28, 0.30
           1.22, 0.30, -0.26, 0.25
           1.78, 0.30, -0.18, 0.25 |]

    let cutter () : MeshData =
        let b = MeshBuilder()
        let cream = MeshColor.rgb 0.84 0.83 0.76
        let orange = MeshColor.rgb 0.94 0.57 0.05
        let slate = MeshColor.rgb 0.38 0.47 0.52
        let belly = MeshColor.rgb 0.15 0.20 0.24

        // Segmente: 0 Bug, 1-2 Vorschiff, 3-4 Mittschiff, 5 Schulter, 6-8 Maschinensteg.
        plating b 0.0 (refine cutterKeel [ -1.10; 0.85 ]) 0.022 0.014 (fun s f ->
            if f = 4 then None
            elif lower f then Some belly
            elif s = 0 then Some orange
            elif s >= 6 then Some (worn 1 s f slate)
            elif flank f && s = 3 then Some orange
            else Some (worn 1 s f cream))
        // Gummifender am Stoßbug, zwei Scheinwerfer.
        b.Box(Vec3(0.0, -0.01, -2.515), Vec3(0.20, 0.055, 0.022), rubber)
        for x in [| -0.10; 0.10 |] do
            b.Box(Vec3(x, -0.005, -2.54), Vec3(0.045, 0.022, 0.008), white)
        // Heckspiegel zwischen den Düsen: Schleppöse und Rücklicht.
        b.Box(Vec3(0.0, 0.10, 1.79), Vec3(0.20, 0.035, 0.012), rubber)
        b.Box(Vec3(0.0, 0.10, 1.803), Vec3(0.13, 0.016, 0.008), white)
        beam b (Vec3(-0.07, -0.06, 1.78)) (Vec3(0.0, -0.06, 1.90)) 0.022 steel
        beam b (Vec3(0.07, -0.06, 1.78)) (Vec3(0.0, -0.06, 1.90)) 0.022 steel

        // Steuerhaus: umlaufende Frontscheibe, Seitenfenster, Heckscheibe für die
        // Verfolgerkamera. Der Pilot sitzt bei Z = -1.30.
        let cabin =
            [| -1.95, 0.36, 0.15, 0.29
               -1.50, 0.45, 0.15, 0.64
               -0.72, 0.45, 0.15, 0.68
               -0.42, 0.38, 0.15, 0.40 |]
        plating b 0.0 cabin 0.026 0.012 (fun s f ->
            if lower f || f = 4 then None
            elif s = 1 && f = 0 then Some orange
            elif s = 2 && f <> 0 then Some cream
            else Some glass)
        // Dachträger, Arbeitsscheinwerfer, Funkmast und Radarbalken.
        for x in [| -0.31; 0.31 |] do
            beam b (Vec3(x, 0.70, -1.44)) (Vec3(x, 0.72, -0.76)) 0.016 steel
        b.Box(Vec3(0.0, 0.672, -1.43), Vec3(0.27, 0.026, 0.035), iron)
        for x in [| -0.19; -0.065; 0.065; 0.19 |] do
            b.Box(Vec3(x, 0.702, -1.43), Vec3(0.04, 0.008, 0.024), amber)
        for x in [| -0.21; 0.21 |] do
            b.Box(Vec3(x, 0.60, -0.655), Vec3(0.07, 0.022, 0.014), amber)
        beam b (Vec3(0.24, 0.68, -0.86)) cutterHull.Beacon 0.013 steel
        b.Box(cutterHull.Beacon, Vec3(0.022, 0.022, 0.022), white)
        beam b (Vec3(-0.20, 0.69, -0.95)) (Vec3(-0.20, 0.78, -0.95)) 0.014 steel
        b.Box(Vec3(-0.20, 0.79, -0.95), Vec3(0.13, 0.018, 0.035), alloy)

        turretRing b slate iron
        panel b
            [| Vec3(-0.20, 0.376, 0.33); Vec3(0.20, 0.376, 0.33); Vec3(0.30, 0.376, 0.45); Vec3(0.30, 0.376, 0.85)
               Vec3(0.20, 0.376, 0.97); Vec3(-0.20, 0.376, 0.97); Vec3(-0.30, 0.376, 0.85); Vec3(-0.30, 0.376, 0.45) |]
            Vec3.unitY 0.006 steel iron

        for side, engine, gun in sides do
            let p x y z = Vec3(side * x, y, z)
            // Zwei Querträger halten die Arbeitsgondeln; dazwischen bleibt Durchblick.
            for z in [| -0.67; 0.68 |] do
                beam b (p 0.44 0.01 (z - 0.12)) (p 1.63 -0.015 (z + 0.11)) 0.085 steel
                beam b (p 0.56 0.11 (z - 0.12)) (p 1.54 0.085 (z + 0.08)) 0.034 cream
                beam b (p 0.60 -0.12 (z - 0.05)) (p 1.40 -0.13 (z + 0.10)) 0.022 iron

            // Triebwerk: kantige Wartungshaube, dahinter der runde Maschinenblock.
            let cowl =
                [| 0.02, 0.20, -0.18, 0.22
                   0.30, 0.33, -0.30, 0.358
                   1.02, 0.33, -0.30, 0.358
                   1.22, 0.27, -0.24, 0.31 |]
            plating b engine.X (refine cowl [ 0.66 ]) 0.022 0.012 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 0 || f = 0 then Some orange
                else Some (worn 2 s (f + int side) cream))
            engineBlock b engine [| 1.10, 0.24; 1.20, 0.29; 1.74, 0.29; 1.78, 0.24 |] iron [ 1.30; 1.58 ] alloy
            bell b engine 0.30 0.26 alloy

            // Außengondel mit Rammnase, Fendern und Bremsdüse.
            let podX = 1.64 * side
            let pod =
                [| -1.27, 0.20, -0.18, 0.04
                   -0.84, 0.38, -0.30, 0.13
                   1.12, 0.40, -0.30, 0.13
                   1.46, 0.30, -0.22, 0.10 |]
            plating b podX (refine pod [ -0.20; 0.46 ]) 0.022 0.014 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 0 then Some orange
                elif flank f || f = 0 then Some (worn 3 s f slate)
                else Some (worn 3 s f cream))
            b.Box(p 2.05 -0.085 0.14, Vec3(0.022, 0.03, 0.94), rubber)
            for z in [| -0.52; 0.13; 0.78 |] do
                b.Box(p 2.065 -0.085 z, Vec3(0.03, 0.04, 0.07), rubber)
            b.Box(p 2.02 0.0 -0.82, Vec3(0.05, 0.065, 0.10), steel)
            retro b (p 2.05 0.0 -0.92)
            gunMount b gun slate steel

            // Heckspiegel der Gondel: Positionslicht außen, Arbeitslicht innen.
            b.Box(p 1.64 -0.045 1.467, Vec3(0.24, 0.07, 0.012), rubber)
            b.Box(p 1.81 -0.03 1.485, Vec3(0.055, 0.03, 0.008), (if side < 0.0 then portRed else starGreen))
            b.Box(p 1.58 -0.03 1.485, Vec3(0.10, 0.014, 0.008), white)

        // Backbord: offener Korb für bis zu drei sichtbare Frachtstücke. Das Heckgatter
        // bleibt niedrig, damit die Ladung aus der Verfolgerkamera erkennbar ist.
        b.Loft([| section -1.64 -0.88 0.34 0.06 0.18; section -1.64 1.18 0.35 0.06 0.18 |], true, true, iron)
        for bay in DeckCargo.bays do
            b.Box(Vec3(bay.X, 0.183, bay.Z), Vec3(0.26, 0.004, 0.21), steel)
        for x in [| -2.00; -1.28 |] do
            for z in [| -0.86; 0.465; 1.16 |] do
                beam b (Vec3(x, 0.12, z)) (Vec3(x, 0.43, z)) 0.042 steel
            beam b (Vec3(x, 0.40, -0.86)) (Vec3(x, 0.40, 1.16)) 0.036 orange
            beam b (Vec3(x, 0.28, -0.86)) (Vec3(x, 0.28, 1.16)) 0.016 steel
        b.Box(Vec3(-1.64, 0.27, -0.86), Vec3(0.36, 0.10, 0.04), orange)
        hazardBar b (Vec3(-1.99, 0.235, 1.16)) (Vec3(-1.29, 0.235, 1.16)) (Vec3(0.0585, 0.065, 0.04)) 6

        // Steuerbord: Arbeitsdeck mit Winde, Drehkranz und gefaltetem Kranarm; die
        // Greifbacken reichen über die Gondelnase nach vorn.
        b.Loft([| section 1.64 -0.90 0.33 0.06 0.17; section 1.64 1.18 0.34 0.06 0.17 |], true, true, steel)
        hazardBar b (Vec3(1.33, 0.175, 1.14)) (Vec3(1.95, 0.175, 1.14)) (Vec3(0.052, 0.006, 0.035)) 6
        drumX b (Vec3(1.64, 0.28, 0.98)) 0.105 0.17 seam
        for x in [| 1.45; 1.83 |] do
            drumX b (Vec3(x, 0.28, 0.98)) 0.135 0.018 alloy
            beam b (Vec3(x, 0.17, 0.86)) (Vec3(x, 0.28, 0.98)) 0.025 steel
        drumY b (Vec3(1.64, 0.0, 0.50)) 0.22 0.17 0.24 iron
        let foot, elbow, tip = Vec3(0.0, 0.25, 0.52), Vec3(0.0, 0.71, 0.34), Vec3(0.0, 0.50, -0.70)
        for x in [| 1.51; 1.77 |] do
            let at (q: Vec3) = Vec3(x, q.Y, q.Z)
            beam b (at foot) (at elbow) 0.062 orange
            // Ausleger: Signalorange, die Spitze mit Warnstreifen.
            for i in 0 .. 5 do
                let a = Vec3.lerp elbow tip (float i / 6.0)
                let c = Vec3.lerp elbow tip (float (i + 1) / 6.0)
                beam b (at a) (at c) 0.050 (if i = 4 then rubber elif i = 5 then hazard else orange)
            // Greifbacke mit Haken
            beam b (at tip) (Vec3(x, 0.34, -0.92)) 0.030 steel
            beam b (Vec3(x, 0.34, -0.92)) (Vec3(x, 0.25, -0.80)) 0.026 steel
        for t in [| 0.2; 0.45; 0.7 |] do
            let c = Vec3.lerp elbow tip t
            beam b (Vec3(1.51, c.Y, c.Z)) (Vec3(1.77, c.Y, c.Z)) 0.02 iron
        drumX b (Vec3(1.64, elbow.Y, elbow.Z)) 0.085 0.19 iron
        drumX b (Vec3(1.64, tip.Y, tip.Z)) 0.06 0.17 iron
        // Hydraulikzylinder und Arbeitslampe an der Auslegerspitze.
        beam b (Vec3(1.64, 0.19, 0.05)) (Vec3(1.64, 0.38, -0.10)) 0.038 iron
        beam b (Vec3(1.64, 0.38, -0.10)) (Vec3(1.64, 0.53, -0.22)) 0.019 alloy
        b.Box(Vec3(1.64, 0.445, -0.72), Vec3(0.06, 0.02, 0.03), amber)
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Lastkahn: hohes Schlepperhaus, sechs Frachtkassetten zwischen schweren Kufen,
    //  Rahmenspanten über dem Laderost und ein breites Maschinenhaus achtern.
    // ---------------------------------------------------------------------------------

    let haulerHull =
        { PortLight = Vec3(-1.72, 0.0, 1.66)
          StarboardLight = Vec3(1.72, 0.0, 1.66)
          Beacon = Vec3(-0.26, 0.93, -1.34)
          Retros = pair 1.99 0.02 -1.98
          Shield = pair 1.28 0.455 1.16
          Armour = pair 1.72 0.215 -1.40
          Relay = Vec3(0.0, 0.29, 1.42) }

    let private haulerKeel =
        [| -2.45, 0.40, -0.24, 0.16
           -2.20, 0.52, -0.32, 0.30
           -1.08, 0.52, -0.32, 0.30
           -0.90, 0.44, -0.30, 0.26
           0.60, 0.44, -0.30, 0.26
           0.80, 0.26, -0.28, 0.29
           1.72, 0.26, -0.20, 0.29 |]

    let hauler () : MeshData =
        let b = MeshBuilder()
        let green = MeshColor.rgb 0.15 0.40 0.40
        let petrol = MeshColor.rgb 0.06 0.24 0.27
        let ochre = MeshColor.rgb 0.94 0.54 0.08
        let sand = MeshColor.rgb 0.84 0.76 0.54
        let rust = MeshColor.rgb 0.56 0.17 0.08
        let pale = MeshColor.rgb 0.55 0.63 0.67
        let belly = MeshColor.rgb 0.07 0.10 0.11

        plating b 0.0 (refine haulerKeel [ -1.65; -0.15 ]) 0.024 0.014 (fun s f ->
            if f = 4 then None
            elif lower f then Some belly
            elif s = 0 then Some ochre
            elif s >= 3 && s <= 5 then Some (worn 4 s f petrol)
            else Some (worn 4 s f green))
        // Stumpfer Bug: Gummistoßleiste, Warnbalken, zwei Scheinwerfer.
        b.Box(Vec3(0.0, -0.10, -2.47), Vec3(0.44, 0.06, 0.03), rubber)
        hazardBar b (Vec3(-0.36, 0.05, -2.465)) (Vec3(0.36, 0.05, -2.465)) (Vec3(0.045, 0.035, 0.012)) 8
        for x in [| -0.30; 0.30 |] do
            b.Box(Vec3(x, -0.10, -2.505), Vec3(0.06, 0.03, 0.008), white)

        // Schlepperhaus: schräge Frontscheibe, Fensterband, Heckscheibe zur Ladung.
        let cab =
            [| -2.22, 0.40, 0.20, 0.42
               -1.95, 0.46, 0.20, 0.70
               -1.25, 0.46, 0.20, 0.70
               -1.08, 0.42, 0.20, 0.52 |]
        plating b 0.0 cab 0.028 0.012 (fun s f ->
            if lower f || f = 4 then None
            elif s = 0 then Some glass
            elif s = 1 && f = 0 then Some sand
            elif s = 1 && flank f then Some glass
            elif s = 2 && f = 0 then Some glass
            else Some (worn 5 s f green))
        b.Box(Vec3(0.0, 0.705, -1.99), Vec3(0.36, 0.014, 0.06), ochre)
        b.Box(Vec3(0.0, 0.725, -1.80), Vec3(0.28, 0.022, 0.035), iron)
        for x in [| -0.20; -0.07; 0.07; 0.20 |] do
            b.Box(Vec3(x, 0.752, -1.80), Vec3(0.04, 0.008, 0.024), amber)
        beam b (Vec3(-0.26, 0.70, -1.38)) haulerHull.Beacon 0.013 steel
        b.Box(haulerHull.Beacon, Vec3(0.022, 0.022, 0.022), white)
        for x in [| 0.16; 0.27 |] do
            beam b (Vec3(x, 0.74, -1.62)) (Vec3(x, 0.74, -1.30)) 0.022 alloy
        turretRing b steel iron

        // Laderost: Querträger unter den Kassetten, Rahmenspanten zwischen den Buchten.
        for z in [| -1.84; -1.005; -0.175; 0.655 |] do
            b.Box(Vec3(0.0, -0.255, z), Vec3(1.62, 0.055, 0.07), iron)
        for z in [| -1.005; -0.175 |] do
            b.Box(Vec3(0.0, 0.505, z), Vec3(1.49, 0.022, 0.028), steel)
            for x in [| -1.455; 1.455 |] do
                b.Box(Vec3(x, 0.15, z), Vec3(0.03, 0.375, 0.028), steel)
        // Maschinenschott hinter der letzten Bucht.
        b.Box(Vec3(0.0, 0.03, 0.71), Vec3(1.50, 0.335, 0.05), petrol)

        let paints = [| rust; ochre; pale; green; sand; rust |]
        for side, engine, gun in sides do
            let p x y z = Vec3(side * x, y, z)
            // Schwere Außenkufe mit Rammschuh, Fendern und Bremsdüse.
            let rail =
                [| -2.47, 0.11, -0.20, -0.02
                   -2.16, 0.22, -0.32, 0.20
                   1.15, 0.22, -0.32, 0.20
                   1.62, 0.16, -0.23, 0.10 |]
            plating b (side * 1.72) (refine rail [ -0.98; -0.15; 0.68 ]) 0.024 0.014 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 0 then Some ochre
                else Some (worn 6 s f green))
            b.Box(p 1.95 -0.07 -0.55, Vec3(0.02, 0.03, 1.45), rubber)
            for z in [| -1.55; -0.55; 0.45 |] do
                b.Box(p 1.965 -0.07 z, Vec3(0.03, 0.045, 0.08), rubber)
            b.Box(p 1.965 0.02 -1.87, Vec3(0.05, 0.07, 0.11), steel)
            retro b (p 1.99 0.02 -1.98)
            b.Box(p 1.72 -0.02 1.628, Vec3(0.13, 0.06, 0.012), rubber)
            b.Box(p 1.72 0.0 1.645, Vec3(0.07, 0.03, 0.008), (if side < 0.0 then portRed else starGreen))

            // Frachtkassetten: gesickte Flanken, dunkle Stirnrahmen, Hubösen, Etikett.
            for bay in 0 .. 2 do
                let z = -1.42 + 0.83 * float bay
                let x = side * 1.00
                let paint = paints.[(bay * 2 + (if side < 0.0 then 0 else 1) + bay / 2) % paints.Length]
                let frame dz width low high = section x (z + dz) width low high
                b.Box(Vec3(x, -0.085, z), Vec3(0.17, 0.14, 0.37), iron)
                b.Loft([| frame -0.38 0.35 0.055 0.42; frame -0.32 0.39 0.045 0.465
                          frame 0.32 0.39 0.045 0.465; frame 0.38 0.35 0.055 0.42 |], true, true, paint)
                for dz in [| -0.335; 0.335 |] do
                    b.Loft([| frame (dz - 0.03) 0.40 0.036 0.474; frame (dz + 0.03) 0.40 0.036 0.474 |], true, true, iron)
                for dx in [| -0.20; -0.07; 0.07; 0.20 |] do
                    b.Box(Vec3(x + dx, 0.468, z), Vec3(0.028, 0.012, 0.26), paint)
                for dz in [| -0.20; -0.10; 0.0; 0.10; 0.20 |] do
                    b.Box(p 1.396 0.255 (z + dz), Vec3(0.012, 0.085, 0.022), paint)
                b.Box(p 1.404 0.30 (z + 0.15), Vec3(0.008, 0.04, 0.07), sand)
            gunMount b gun petrol steel

            // Maschinenhaus: Schulter mit Radiator, runder Maschinenblock, große Glocke.
            let shoulder =
                [| 0.68, 0.30, -0.26, 0.30
                   0.86, 0.40, -0.32, 0.44
                   1.46, 0.40, -0.32, 0.44
                   1.66, 0.32, -0.24, 0.30 |]
            plating b (side * 1.28) shoulder 0.024 0.014 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 1 && f = 0 then Some steel
                else Some (worn 7 s (f + int side) green))
            for i in 0 .. 4 do
                b.Box(p 1.692 0.06 (0.92 + 0.12 * float i), Vec3(0.012, 0.15, 0.03), iron)
            b.Box(p 1.686 0.06 1.16, Vec3(0.006, 0.11, 0.27), ember)
            b.Box(p 1.28 0.025 1.650, Vec3(0.29, 0.125, 0.05), rust)
            b.Box(p 1.28 0.03 1.705, Vec3(0.24, 0.075, 0.012), rubber)
            for x in [| 1.17; 1.39 |] do
                b.Box(p x 0.03 1.724, Vec3(0.076, 0.033, 0.008), amber)
            engineBlock b engine [| 0.74, 0.30; 0.92, 0.37; 1.68, 0.37; 1.76, 0.29 |] sand [ 1.02; 1.50 ] iron
            bell b engine 0.335 0.26 alloy
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Sperber: Nadelbug, lange Kanzel, geknickte Pfeilflügel mit Randschienen und zwei
    //  ausgestellte Flossen. Schieferblau über Silber, rote Vorderkanten.
    // ---------------------------------------------------------------------------------

    let sparrowHull =
        { PortLight = Vec3(-2.04, 0.05, 0.80)
          StarboardLight = Vec3(2.04, 0.05, 0.80)
          Beacon = Vec3(0.0, 0.36, -0.20)
          Retros = pair 1.05 0.03 -0.60
          Shield = pair 0.64 0.258 1.40
          Armour = pair 0.64 0.258 -0.02
          Relay = Vec3(0.0, 0.29, 1.47) }

    let private sparrowKeel =
        [| -2.50, 0.035, -0.045, 0.015
           -2.05, 0.15, -0.11, 0.09
           -1.55, 0.24, -0.16, 0.17
           -0.60, 0.33, -0.20, 0.27
           0.10, 0.36, -0.22, 0.30
           1.17, 0.32, -0.20, 0.29
           1.76, 0.30, -0.14, 0.29 |]

    let sparrow () : MeshData =
        let b = MeshBuilder()
        let silver = MeshColor.rgb 0.80 0.83 0.86
        let slate = MeshColor.rgb 0.17 0.26 0.39
        let red = MeshColor.rgb 0.88 0.10 0.065
        let crimson = MeshColor.rgb 0.50 0.04 0.045
        let belly = MeshColor.rgb 0.09 0.12 0.15

        plating b 0.0 (refine sparrowKeel [ -1.10; 0.62 ]) 0.018 0.010 (fun s f ->
            if f = 4 then None
            elif lower f then Some belly
            elif f = 0 && s >= 2 then Some (worn 8 s f slate)
            else Some (worn 8 s f silver))
        // Roter Nadelbug
        b.Loft([| section 0.0 -2.51 0.03 -0.045 0.02; section 0.0 -2.24 0.115 -0.092 0.074 |], true, true, red)
        // Lange Kanzel mit zwei Spanten; der Pilot sitzt bei Z = -1.30.
        let canopy =
            [| -1.72, 0.13, 0.10, 0.19
               -1.25, 0.23, 0.14, 0.47
               -0.80, 0.25, 0.18, 0.52
               -0.30, 0.22, 0.20, 0.33 |]
        plating b 0.0 canopy 0.020 0.008 (fun s f ->
            if lower f || f = 4 then None
            elif s = 2 && f = 0 then Some slate
            else Some glass)
        b.Box(sparrowHull.Beacon - Vec3(0.0, 0.03, 0.0), Vec3(0.02, 0.02, 0.02), white)
        turretRing b silver slate

        for side, engine, gun in sides do
            let p x y z = Vec3(side * x, y, z)
            // Fünf Profile definieren Knick, Wölbung und eine tordierte Flügelspitze.
            let wingSection x y front rear thickness =
                let chord = rear - front
                [| p x y front; p x (y + thickness) (front + chord * 0.26)
                   p x (y + thickness * 0.55) (front + chord * 0.73); p x y rear
                   p x (y - thickness * 0.78) (front + chord * 0.43) |]
            let sections =
                [| wingSection 0.29 -0.006 -1.36 1.43 0.15
                   wingSection 0.99 -0.018 -0.66 1.46 0.11
                   wingSection 1.60 -0.028 0.43 1.53 0.078
                   wingSection 1.80 -0.011 0.69 1.50 0.059
                   wingSection 2.06 0.044 1.03 1.41 0.030 |]
            for span in 0 .. sections.Length - 2 do
                let inner = sections.[span]
                let outer = sections.[span + 1]
                let inside = (Array.reduce (+) inner + Array.reduce (+) outer) / 10.0
                for face in 0 .. inner.Length - 1 do
                    let next = (face + 1) % inner.Length
                    let quad = [| inner.[face]; inner.[next]; outer.[next]; outer.[face] |]
                    match face with
                    | 0 -> b.PolygonOutward(quad, inside, red)
                    | 1 | 2 ->
                        // Oberseite: Platten mit Fuge, Ruderfläche in Schiefer, rotes Band außen.
                        b.PolygonOutward(quad, inside, seam)
                        let paint = if span = 2 || (span = 3 && face = 1) then red elif face = 2 then slate else worn 9 span face silver
                        quadPanel b quad inside 0.022 0.008 seam paint
                    | _ -> b.PolygonOutward(quad, inside, belly)
                if span = sections.Length - 2 then b.PolygonOutward(outer, inside, crimson)
            // Randschiene mit Positionslicht vorn und Hecklicht.
            beam b (p 2.04 0.05 0.86) (p 2.04 0.05 1.56) 0.034 slate
            b.Loft([| oval 6 (side * 2.04) 0.05 0.80 0.012 0.012; oval 6 (side * 2.04) 0.05 0.87 0.05 0.05 |], true, true,
                   (if side < 0.0 then portRed else starGreen))
            b.Box(p 2.04 0.05 1.57, Vec3(0.022, 0.022, 0.012), white)
            // Rote Wangen vom Bug bis zur Tragflächenwurzel.
            panel b
                [| p 0.105 0.04 -2.10; p 0.28 0.085 -1.63; p 0.60 0.095 -0.83
                   p 0.50 0.16 -0.79; p 0.245 0.18 -1.40 |] Vec3.unitY 0.014 red crimson
            // Unter dem Flügel eingestrakte Waffenschiene, Mündung vor der Nasenkante.
            b.Loft([| section gun.X -0.56 0.11 -0.18 -0.028; section gun.X -0.18 0.153 -0.20 0.015
                      section gun.X 0.59 0.12 -0.13 0.06 |], true, true, silver)
            gunMount b gun slate steel

            // Triebwerksgondel: gepanzerter Einlauf, Wartungsplatten, Hitzeschild am Heck.
            let duct =
                [| -0.74, 0.18, -0.15, 0.12
                   -0.46, 0.27, -0.22, 0.24
                   0.30, 0.31, -0.27, 0.25
                   1.60, 0.31, -0.27, 0.25
                   1.76, 0.27, -0.22, 0.22 |]
            plating b engine.X (refine duct [ 0.95 ]) 0.018 0.010 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 0 then Some red
                elif s >= 4 then Some steel
                elif f = 0 && s = 2 then Some red
                else Some (worn 10 s (f + int side) silver))
            b.Box(Vec3(engine.X, -0.015, -0.745), Vec3(0.125, 0.095, 0.012), rubber)
            b.Box(p 1.05 0.03 -0.52, Vec3(0.045, 0.035, 0.08), steel)
            retro b (p 1.05 0.03 -0.60)
            bell b engine 0.28 0.26 alloy

            // Ausgestellte Doppelflossen: Schieferfuß, helles Feld, roter Kopf mit Hecklicht.
            let finRootFront, finRootRear = p 0.97 0.056 0.30, p 0.99 0.06 1.53
            let finTopFront, finTopRear = p 1.36 0.70 1.27, p 1.43 0.71 1.66
            let finBand low high color =
                let at t a c = Vec3.lerp a c t
                b.Slab([| at low finRootFront finTopFront; at low finRootRear finTopRear
                          at high finRootRear finTopRear; at high finRootFront finTopFront |],
                       [| 0.034 - 0.017 * low; 0.034 - 0.017 * low
                          0.034 - 0.017 * high; 0.034 - 0.017 * high |], Vec3.unitX, color)
            finBand 0.0 0.30 slate
            finBand 0.30 0.34 seam
            finBand 0.34 0.66 silver
            finBand 0.66 1.0 red
            b.Box(p 1.43 0.70 1.675, Vec3(0.014, 0.022, 0.012), white)
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Kepler-Bomber: dicker Buckelrumpf, breite Tragfläche mit zwei offenen Torpedowiegen,
    //  schwere Gondeln und hohe Außenflossen. Werftfarben Petrol, Keramik und Warngelb.
    // ---------------------------------------------------------------------------------

    let bomberHull =
        { PortLight = Vec3(-2.52, 0.10, 1.05)
          StarboardLight = Vec3(2.52, 0.10, 1.05)
          Beacon = Vec3(0.0, 0.56, 1.02)
          Retros = pair 1.45 0.09 -0.88
          Shield = [||]
          Armour = [||]
          Relay = Vec3.zero }

    let private bomberKeel =
        [| -2.88, 0.18, -0.12, 0.10
           -2.30, 0.46, -0.30, 0.28
           -0.80, 0.62, -0.44, 0.44
           0.85, 0.58, -0.40, 0.44
           1.55, 0.34, -0.24, 0.30 |]

    let bomber () : MeshData =
        let b = MeshBuilder()
        let teal = MeshColor.rgb 0.07 0.37 0.38
        let ceramic = MeshColor.rgb 0.80 0.80 0.72
        let yellow = MeshColor.rgb 0.97 0.60 0.08
        let frame = MeshColor.rgb 0.07 0.11 0.13
        let belly = MeshColor.rgb 0.06 0.09 0.10

        plating b 0.0 (refine bomberKeel [ -1.55; 0.0 ]) 0.026 0.016 (fun s f ->
            if f = 4 then None
            elif lower f then Some belly
            elif s <= 1 then Some (worn 11 s f ceramic)
            elif flank f && s = 2 then Some yellow
            else Some (worn 11 s f teal))
        b.Loft([| section 0.0 -2.90 0.17 -0.115 0.095; section 0.0 -2.74 0.26 -0.165 0.145 |], true, true, frame)
        // Gerahmte Bomberkanzel; der Pilot sitzt bei Z = -1.30.
        let canopy =
            [| -2.30, 0.28, 0.20, 0.30
               -1.68, 0.40, 0.26, 0.64
               -0.95, 0.38, 0.30, 0.70
               -0.45, 0.28, 0.32, 0.44 |]
        plating b 0.0 canopy 0.028 0.012 (fun s f ->
            if lower f || f = 4 then None
            elif s = 1 && f = 0 then Some teal
            elif s = 2 then Some (worn 12 s f teal)
            else Some glass)
        // Der heiße Wärmetauscher liegt zwischen den Gondeln im Blick der Verfolgerkamera.
        b.Box(Vec3(0.0, 0.43, 0.25), Vec3(0.30, 0.035, 0.64), frame)
        b.Box(Vec3(0.0, 0.468, 0.25), Vec3(0.22, 0.006, 0.56), ember)
        for i in 0 .. 5 do
            b.Box(Vec3(0.0, 0.48, 0.75 - float i * 0.20), Vec3(0.26, 0.032, 0.038), alloy)
        beam b (Vec3(0.0, 0.44, 0.98)) bomberHull.Beacon 0.014 steel
        b.Box(bomberHull.Beacon, Vec3(0.022, 0.022, 0.022), white)

        for rail in ShipHardpoints.bomberRails do
            let side = if rail.X < 0.0 then -1.0 else 1.0
            let p x y z = Vec3(side * x, y, z)
            // Gepfeilte Haupttragfläche; um die Glocken bleibt Durchblick.
            b.Slab([| p 0.38 0.10 -1.90; p 2.39 0.04 0.05; p 2.54 0.05 1.23; p 0.41 0.10 0.57 |],
                   Array.create 4 0.11, Vec3.unitY, frame)
            panel b [| p 0.50 0.21 -1.66; p 1.16 0.19 -1.02; p 1.16 0.19 0.72; p 0.50 0.21 0.52 |]
                Vec3.unitY 0.035 teal frame
            panel b [| p 1.22 0.188 -0.96; p 1.84 0.168 -0.36; p 1.84 0.168 0.93; p 1.22 0.188 0.74 |]
                Vec3.unitY 0.030 steel frame
            panel b [| p 1.90 0.166 -0.30; p 2.30 0.154 0.10; p 2.30 0.154 0.80; p 1.90 0.166 0.68 |]
                Vec3.unitY 0.035 teal frame
            panel b [| p 1.90 0.166 0.74; p 2.30 0.154 0.86; p 2.42 0.154 1.14; p 1.90 0.166 0.98 |]
                Vec3.unitY 0.030 ceramic frame
            // Warngelbe Flügelspitze mit Positionslicht.
            b.Box(p 2.50 0.07 0.72, Vec3(0.035, 0.08, 0.50), yellow)
            b.Box(p 2.535 0.10 1.05, Vec3(0.012, 0.035, 0.07), (if side < 0.0 then portRed else starGreen))
            // Hohe Außenflosse
            b.Slab([| p 1.97 0.16 0.42; p 2.18 0.77 0.98; p 2.23 0.88 1.52; p 2.03 0.12 1.25 |],
                   [| 0.09; 0.045; 0.035; 0.085 |], Vec3.unitX, teal)
            beam b (p 2.18 0.77 0.98) (p 2.23 0.88 1.52) 0.034 yellow
            b.Box(p 2.235 0.86 1.555, Vec3(0.014, 0.03, 0.012), white)
            // Die feste Abschusswiege bleibt sichtbar, wenn ihr Torpedo weg ist.
            for x in [| 1.40; 1.66 |] do
                beam b (p x 0.31 -0.72) (p x 0.31 0.98) 0.043 alloy
                for z in [| -0.55; 0.12; 0.80 |] do
                    b.Box(p x 0.235 z, Vec3(0.035, 0.05, 0.05), frame)
            for z in [| -0.55; 0.56 |] do
                b.Box(p 1.53 0.31 z, Vec3(0.20, 0.04, 0.06), yellow)
            hazardBar b (p 1.26 0.222 -0.80) (p 1.80 0.205 -0.30) (Vec3(0.05, 0.004, 0.05)) 5

            let engine = if side < 0.0 then ShipHardpoints.playerEngineLeft else ShipHardpoints.playerEngineRight
            let pod =
                [| -0.96, 0.21, -0.13, 0.20
                   -0.38, 0.39, -0.31, 0.40
                   1.22, 0.39, -0.33, 0.43
                   1.65, 0.31, -0.29, 0.30 |]
            plating b engine.X (refine pod [ 0.30; 0.80 ]) 0.026 0.014 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 0 then Some frame
                elif s = 3 then Some teal
                elif s = 4 then Some steel
                else Some (worn 13 s (f + int side) ceramic))
            retro b (p 1.45 0.09 -0.88)
            bell b engine 0.36 0.43 alloy
            let gun = if side < 0.0 then ShipHardpoints.playerGunLeft else ShipHardpoints.playerGunRight
            gunMount b gun teal steel
        b.Build()

    /// Dasselbe Modell sitzt auf den Außenschienen und fliegt nach dem Auslösen weiter.
    let torpedo () : MeshData =
        let b = MeshBuilder()
        let ochre = MeshColor.rgb 0.96 0.50 0.09
        let ceramic = MeshColor.rgb 0.85 0.84 0.75
        let arming = MeshColor.glow 1.0 0.30 0.08 0.75
        let shell z r = oval 10 0.0 0.0 z r r
        b.Loft([| shell -1.34 0.045; shell -1.04 0.15; shell -0.73 0.215; shell 0.67 0.215; shell 0.99 0.16 |],
               true, true, ceramic)
        b.Loft([| shell -1.34 0.05; shell -1.19 0.105 |], true, false, iron)
        b.Loft([| shell -0.73 0.223; shell -0.53 0.223 |], true, true, ochre)
        b.Loft([| shell -0.49 0.221; shell -0.45 0.221 |], true, true, arming)
        b.Loft([| shell 0.20 0.221; shell 0.26 0.221 |], true, true, iron)
        for i in 0 .. 3 do
            let a = Math.PI * (0.25 + 0.5 * float i)
            let radial = Vec3(cos a, sin a, 0.0)
            let p r z = radial * r + Vec3.unitZ * z
            b.Slab([| p 0.14 0.27; p 0.33 0.73; p 0.33 0.99; p 0.14 0.88 |],
                   Array.create 4 0.026, Vec3(-sin a, cos a, 0.0), steel)
        let exit = Vec3(0.0, 0.0, 1.08)
        let hoop z r = oval 10 exit.X exit.Y z r r
        b.Loft([| hoop 0.90 0.16; hoop 1.08 0.153 |], true, false, steel)
        b.PolygonOutward(hoop 1.0 0.12, Vec3(0.0, 0.0, 0.5), MeshColor.rgb 0.07 0.11 0.13)
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Eisvogel: Frachtkurier mit kurzer Mittelgondel zwischen zwei langen Kufen. Jede
    //  Kufe trägt drei Frachtplätze und läuft vorn in einen flachen Schnabel aus, unter
    //  dem der Arbeitslaser sitzt. Azurblau oben, Kupfer am Bauch, heller Schnabel.
    // ---------------------------------------------------------------------------------

    let kingfisherHull =
        { PortLight = Vec3(-1.47, 0.06, 1.93)
          StarboardLight = Vec3(1.47, 0.06, 1.93)
          Beacon = Vec3(0.18, 0.78, -0.34)
          Retros = pair 1.875 0.10 -0.79
          Shield = pair 1.36 0.43 1.20
          Armour = pair 1.36 0.412 -1.60
          Relay = Vec3(0.0, 0.285, 1.40) }

    let private kingfisherPod =
        [| -1.30, 0.12, -0.07, 0.22
           -0.94, 0.32, -0.16, 0.30
           0.12, 0.36, -0.20, 0.33
           0.46, 0.30, -0.18, 0.30
           1.22, 0.28, -0.14, 0.27
           1.60, 0.24, -0.04, 0.27 |]

    /// Kufe: flacher Schnabel bis Z = -0.95, dahinter der tiefe Frachtrumpf.
    let private kingfisherSkid =
        [| -2.48, 0.06, 0.10, 0.16
           -2.10, 0.26, 0.05, 0.32
           -1.80, 0.40, 0.03, 0.40
           -0.95, 0.44, 0.03, 0.40
           -0.78, 0.45, -0.30, 0.40
           0.88, 0.45, -0.30, 0.40
           1.38, 0.44, -0.22, 0.42
           1.76, 0.34, -0.14, 0.34
           1.90, 0.22, -0.08, 0.20 |]

    let kingfisher () : MeshData =
        let b = MeshBuilder()
        let azure = MeshColor.rgb 0.07 0.40 0.66
        let teal = MeshColor.rgb 0.05 0.52 0.58
        let ice = MeshColor.rgb 0.86 0.92 0.94
        let copper = MeshColor.rgb 0.86 0.39 0.12
        let deck = MeshColor.rgb 0.16 0.22 0.27
        let belly = MeshColor.rgb 0.07 0.11 0.15

        let pod = refine kingfisherPod [ -0.40 ]
        let podZ s = let z, _, _, _ = pod.[s] in z
        plating b 0.0 pod 0.022 0.014 (fun s f ->
            if f = 4 then None
            elif lower f then Some (if podZ s < 0.46 then copper else belly)
            elif podZ s < -0.94 then Some ice
            elif f = 0 && podZ s >= 0.12 then Some deck
            else Some (worn 14 s f azure))
        b.Box(Vec3(0.0, 0.02, -1.315), Vec3(0.085, 0.022, 0.008), white)
        b.Box(Vec3(0.0, 0.10, 1.608), Vec3(0.15, 0.04, 0.010), rubber)
        b.Box(Vec3(0.0, 0.10, 1.622), Vec3(0.10, 0.018, 0.008), amber)

        // Kurze Kanzel: die Schnäbel bleiben aus dem Cockpit links und rechts sichtbar.
        let cabin =
            [| -1.20, 0.13, 0.16, 0.25
               -0.82, 0.30, 0.20, 0.54
               -0.20, 0.33, 0.24, 0.56
               0.10, 0.28, 0.24, 0.36 |]
        plating b 0.0 cabin 0.024 0.012 (fun s f ->
            if lower f || f = 4 then None
            elif s = 1 && f = 0 then Some ice
            elif s = 2 && f <> 0 then Some (worn 15 s f azure)
            else Some glass)
        for x in [| -0.17; 0.17 |] do
            b.Box(Vec3(x, 0.455, 0.112), Vec3(0.06, 0.02, 0.012), amber)
        beam b (Vec3(0.18, 0.55, -0.42)) kingfisherHull.Beacon 0.013 steel
        b.Box(kingfisherHull.Beacon, Vec3(0.022, 0.022, 0.022), white)
        beam b (Vec3(-0.18, 0.55, -0.50)) (Vec3(-0.18, 0.66, -0.50)) 0.014 steel
        b.Box(Vec3(-0.18, 0.67, -0.50), Vec3(0.035, 0.016, 0.12), alloy)
        turretRing b teal iron

        let skid = refine kingfisherSkid [ -1.35; -0.63; 0.09 ]
        let skidZ s = let z, _, _, _ = skid.[s] in z
        for side, engine, gun in sides do
            let p x y z = Vec3(side * x, y, z)
            plating b (side * 1.36) skid 0.024 0.014 (fun s f ->
                let z = skidZ s
                if f = 4 then None
                elif lower f then Some (if z < -0.95 then belly else worn 16 s f copper)
                elif z < -2.10 then Some copper
                elif z < -1.35 || z >= 0.88 then Some (worn 16 s f ice)
                elif f = 0 then Some deck
                else Some (worn 16 s (f + int side) azure))
            b.Box(p 1.36 0.105 -2.492, Vec3(0.04, 0.02, 0.008), white)
            // Heller Streifen auf der Außenflanke, darunter Fender und Bremsdüse.
            panel b [| p 1.812 0.03 -0.60; p 1.812 0.19 -0.50; p 1.812 0.19 0.80; p 1.812 0.03 0.80 |]
                (p 1.0 0.0 0.0) 0.024 ice seam
            b.Box(p 1.83 -0.10 0.05, Vec3(0.02, 0.03, 0.80), rubber)
            b.Box(p 1.85 0.10 -0.68, Vec3(0.05, 0.07, 0.11), steel)
            retro b (p 1.875 0.10 -0.79)

            // Frachtdeck: zwei Führungsschienen, Stirnwand, Klammern zwischen den Plätzen.
            for dx in [| -0.18; 0.18 |] do
                b.Box(p (1.36 + dx) 0.408 -0.27, Vec3(0.031, 0.014, 1.04), steel)
            b.Box(p 1.36 0.455 -1.40, Vec3(0.34, 0.045, 0.028), azure)
            for z in [| -1.35; -0.63; 0.09; 0.81 |] do
                b.Box(p 1.70 0.435 z, Vec3(0.04, 0.03, 0.045), copper)
                b.Box(p 1.02 0.435 z, Vec3(0.04, 0.03, 0.045), steel)
            hazardBar b (p 1.08 0.418 0.86) (p 1.64 0.418 0.86) (Vec3(0.047, 0.008, 0.03)) 6
            gunMount b gun azure steel

            // Heckspiegel der Kufe: Positionslicht außen, Arbeitslicht innen.
            b.Box(p 1.36 0.06 1.906, Vec3(0.18, 0.055, 0.010), rubber)
            b.Box(p 1.47 0.06 1.920, Vec3(0.055, 0.03, 0.008), (if side < 0.0 then portRed else starGreen))
            b.Box(p 1.27 0.06 1.920, Vec3(0.08, 0.014, 0.008), white)

            // Brückenkasten und vorderer Stützholm zwischen Gondel und Kufe.
            let spar x front rear y =
                [| p x y front; p x (y + 0.09) (front + 0.05); p x (y + 0.09) (rear - 0.05)
                   p x y rear; p x (y - 0.07) (rear - 0.05); p x (y - 0.07) (front + 0.07) |]
            b.Loft([| spar 0.27 0.26 0.84 0.16; spar 0.93 0.36 0.84 0.12 |], true, true, teal)
            b.Box(p 0.62 0.232 0.57, Vec3(0.20, 0.008, 0.16), ice)
            beam b (p 0.30 0.02 -0.50) (p 0.93 0.06 -0.62) 0.06 steel
            beam b (p 0.32 0.10 -0.20) (p 0.93 0.14 -0.56) 0.024 alloy

            // Triebwerk: kantige Haube an der Kufenflanke, runder Maschinenblock, Glocke.
            let cowl =
                [| 0.80, 0.18, -0.16, 0.20
                   1.02, 0.29, -0.27, 0.31
                   1.50, 0.29, -0.27, 0.31
                   1.66, 0.25, -0.22, 0.27 |]
            plating b engine.X (refine cowl [ 1.26 ]) 0.022 0.012 (fun s f ->
                if f = 4 then None
                elif lower f then Some belly
                elif s = 0 then Some copper
                elif f = 0 then Some ice
                else Some (worn 17 s (f + int side) azure))
            engineBlock b engine [| 1.50, 0.24; 1.60, 0.285; 1.74, 0.285; 1.78, 0.24 |] iron [ 1.63 ] alloy
            bell b engine 0.30 0.26 alloy
        b.Build()

    /// Rümpfe in der Reihenfolge von ShipCatalog.hulls.
    let models = [| cutter; hauler; sparrow; kingfisher |]
    let hulls = [| cutterHull; haulerHull; sparrowHull; kingfisherHull |]

/// Lebenszeichen am eigenen Schiff ohne eigene Partikel: Positionslichter, Blitzer,
/// nachglühende Düsen und Bremsdüsen. Nutzt die vorhandenen additiven Batches.
[<RequireQualifiedAccess>]
module ShipLife =
    let mutable private heat = 0.0
    let mutable private last = 0.0

    /// Düsenwärme 0..1: folgt dem Schub rasch nach oben und klingt über Sekunden ab.
    let warm (current: float) (drive: float) (dt: float) =
        if drive > current then current + (drive - current) * (1.0 - exp (-5.0 * dt))
        else max drive (current - 0.4 * dt)

    /// Doppelblitz alle 1,8 s.
    let strobe (time: float) =
        let phase = time - 1.8 * floor (time / 1.8)
        phase < 0.07 || (phase > 0.22 && phase < 0.29)

    let push
        (halo: InstanceBatch) (exhaust: InstanceBatch) (facing: Quat) (time: float) (hull: PlayerHull)
        (position: Vec3) (rotation: Quat) (drive: float) (braking: float) =
        let glow (local: Vec3) size r g b =
            let p = position + Quat.rotateVec3 rotation local
            InstanceBatch.pushTRS halo p.X p.Y p.Z facing.X facing.Y facing.Z facing.W size size size r g b 1.0
        let breathe = 0.85 + 0.15 * sin (time * 2.3)
        glow hull.PortLight 0.34 (0.75 * breathe) (0.06 * breathe) (0.03 * breathe)
        glow hull.StarboardLight 0.34 (0.04 * breathe) (0.62 * breathe) (0.24 * breathe)
        if strobe time then glow hull.Beacon 0.55 0.85 0.9 1.0

        // Nachglühen: die Glocken bleiben nach dem Schub noch einige Sekunden warm.
        let dt = max 0.0 (min 0.1 (time - last))
        last <- time
        heat <- warm heat (min 1.0 drive) dt
        let afterglow = max 0.0 (heat - min 1.0 drive)
        if afterglow > 0.02 then
            for engine in [| ShipHardpoints.playerEngineLeft; ShipHardpoints.playerEngineRight |] do
                glow (engine + Vec3(0.0, 0.0, 0.04)) 0.62 (1.25 * afterglow) (0.40 * afterglow) (0.05 * afterglow)

        // Bremsdüsen feuern nach vorn, solange die Bremse Fahrt abbaut.
        if braking > 0.02 then
            let reverse = rotation * Quat.fromAxisAngle Vec3.unitY Math.PI
            let length = (0.35 + 0.75 * braking) * (0.9 + 0.1 * sin (time * 83.0))
            for nozzle in hull.Retros do
                let c = position + Quat.rotateVec3 rotation (Vec3(nozzle.X, nozzle.Y, nozzle.Z - length * 0.5))
                InstanceBatch.pushTRS exhaust c.X c.Y c.Z reverse.X reverse.Y reverse.Z reverse.W 0.13 0.13 length (0.55 * braking) (0.75 * braking) (1.0 * braking) 1.0
                glow (nozzle - Vec3(0.0, 0.0, 0.03)) 0.30 (0.35 * braking) (0.55 * braking) (0.8 * braking)
