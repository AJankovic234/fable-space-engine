namespace SpaceEngine

/// AETHERs Verkehrsverbände. Lokale Maße in Metern, Bug -Z, oben +Y.
/// Die Orbitals fliegen sauber: kaltes Platin über schwarzem Gürtel, Gold nur am Schnabel und am
/// Wappen. Die Brücken sind verspiegelt, die Fenster kalt, und was rot leuchtet, misst dich.
[<RequireQualifiedAccess>]
module AetherTrafficMeshes =
    let private ivory = MeshColor.rgb 0.50 0.505 0.50
    let private shadowIvory = MeshColor.rgb 0.235 0.245 0.255
    let private gold = MeshColor.rgb 0.54 0.345 0.07
    let private bronze = MeshColor.metal 0.30 0.20 0.085
    let private graphite = MeshColor.rgb 0.032 0.037 0.045
    let private steel = MeshColor.metal 0.20 0.22 0.24
    let private carmine = MeshColor.rgb 0.34 0.016 0.024
    let private redLens = MeshColor.glass 0.85 0.045 0.02 0.80
    let private canopy = MeshColor.glass 0.028 0.036 0.045 0.08
    let private window = MeshColor.glow 0.72 0.88 1.0 0.70
    /// Abtaster: ein schmales rotes Auge, das die Kennung jedes Schiffs im Korridor liest.
    let private scanner = MeshColor.glow 1.0 0.07 0.03 0.92
    let private ion = MeshColor.glow 0.68 0.89 1.0 1.0
    let private ionWall = MeshColor.glow 0.18 0.45 0.72 0.56

    let private mirrored x y z = [| Vec3(-x,y,z); Vec3(x,y,z) |]

    // Düsenaustritte und die Lampen an den äußersten Schultern, Backbord vor Steuerbord.
    let frigateDrives = [| Vec3(0.,-0.65,37.8),3.0; Vec3(-8.6,-0.30,35.8),2.0; Vec3(8.6,-0.30,35.8),2.0 |]
    let frigateLights = mirrored 11.23 0.05 22.5
    let escortDrives = [| Vec3(0.,-0.28,15.4),2.1 |]
    let escortLights = mirrored 12.72 -0.48 -8.6

    let private rib = ProceduralMeshes.hullSection

    /// Das Oberdeck, die Schulter, der vertikale Gürtel und der Kiel bekommen eigene Werkstoffe.
    let private shell (b: MeshBuilder) (ribs: Vec3[][]) deck shoulder flank bottom =
        let colors = [| deck; shoulder; flank; bottom; bottom; bottom; flank; shoulder |]
        let center (r: Vec3[]) = (r.[0] + r.[4]) * 0.5
        for a, c in Array.pairwise ribs do
            let inside = (center a + center c) * 0.5
            for i in 0 .. 7 do
                let j = (i + 1) % 8
                b.PolygonOutward([| a.[i]; a.[j]; c.[j]; c.[i] |], inside, colors.[i])
        b.PolygonOutward(ribs.[0], center ribs.[1], flank)
        b.PolygonOutward(Array.last ribs, center ribs.[ribs.Length - 2], flank)

    /// Aufgesetzte Bleche und Fenster direkt auf einer Spantfläche (t längs, u quer).
    let private panel (b: MeshBuilder) (a: Vec3[]) (c: Vec3[]) face t0 t1 u0 u1 depth color edge =
        let j = (face + 1) % 8
        let at t u = Vec3.lerp (Vec3.lerp a.[face] a.[j] u) (Vec3.lerp c.[face] c.[j] u) t
        let points = [| at t0 u0; at t1 u0; at t1 u1; at t0 u1 |]
        let normal = Vec3.normalize (Vec3.cross (points.[1] - points.[0]) (points.[3] - points.[0]))
        let inside = (a.[0] + a.[4] + c.[0] + c.[4]) * 0.25
        let outward = if Vec3.dot normal (points.[0] - inside) > 0.0 then normal else -normal
        ProceduralMeshes.armorPanel b points outward depth color edge

    /// Tief liegender Ionenring mit einzeln gefassten Düsenblättern.
    let private drive (b: MeshBuilder) (exit: Vec3) r =
        let ring dz scale = ProceduralMeshes.ring (12,exit.X,exit.Y,exit.Z + dz*r,scale*r,scale*r)
        let back, neck, lip = ring -1.7 0.88, ring -0.76 0.80, ring 0. 1.
        let inner, throat = ring 0. 0.78, ring -0.54 0.55
        b.Loft([| back; neck |], true, false, graphite)
        for i in 0 .. 11 do
            let j = (i + 1) % 12
            b.PolygonOutward([| neck.[i]; neck.[j]; lip.[j]; lip.[i] |], exit,
                             (if i % 3 = 0 then bronze else steel))
            b.PolygonOutward([| lip.[i]; lip.[j]; inner.[j]; inner.[i] |], exit - Vec3.unitZ, shadowIvory)
            b.Triangle(inner.[i], inner.[j], throat.[j], ionWall)
            b.Triangle(inner.[i], throat.[j], throat.[i], ionWall)
        b.PolygonOutward(throat, exit - Vec3.unitZ * r, ion)

    let private positionLamp (b: MeshBuilder) (p: Vec3) size =
        b.Box(p, Vec3(size * 0.48,size * 0.7,size * 1.75), bronze)
        let side = if p.X < 0. then -1. else 1.
        for dz in [| -0.76; 0.76 |] do
            b.Box(p + Vec3(side * size * 0.48,0.,dz * size), Vec3(size * 0.35,size * 0.48,size * 0.52), redLens)

    /// Achteckiges Deck quer zum Brückenturm.
    let private terrace y z w length =
        [| -0.66,-1.; 0.66,-1.; 1.,-0.63; 1.,0.69; 0.70,1.; -0.70,1.; -1.,0.69; -1.,-0.63 |]
        |> Array.map (fun (u,v) -> Vec3(u*w,y,z+v*length))

    /// Karminschild mit gebrochenem A und freistehendem Stern im Winkel.
    let private fleetShield (b: MeshBuilder) (origin: Vec3) (across: Vec3) (up: Vec3) (normal: Vec3) =
        let point (x,y) = origin + across*x + up*y
        let outline =
            [| -1.,0.68; -0.76,0.95; 0.76,0.95; 1.,0.68; 0.84,-0.31; 0.,-1.; -0.84,-0.31 |]
        ProceduralMeshes.armorPanel b (Array.map point outline) normal 0.075 carmine bronze
        let inlay polygon color =
            let points = polygon |> Array.map (fun uv -> point uv + normal*0.09)
            b.PolygonOutward(points,origin-normal,color)
        inlay [| -0.72,-0.36; -0.08,0.68; 0.10,0.38; -0.38,-0.36 |] gold
        inlay [| 0.06,0.19; 0.22,0.45; 0.72,-0.36; 0.41,-0.36 |] gold
        inlay [| 0.,-0.04; 0.14,-0.24; 0.,-0.46; -0.14,-0.24 |] ivory

    /// Das rote Auge sitzt in einer dunklen, von der Seite her spitz zulaufenden Fassung.
    let private watchEye (b: MeshBuilder) (center: Vec3) width height =
        let face dx dy depth = center + Vec3(dx*width,dy*height,-depth)
        let diamond halfWidth halfHeight depth =
            [| face -halfWidth 0. depth; face 0. halfHeight depth
               face halfWidth 0. depth; face 0. -halfHeight depth |]
        ProceduralMeshes.armorPanel b (diamond 1. 1. 0.) (-Vec3.unitZ) 0.12 graphite bronze
        ProceduralMeshes.armorPanel b (diamond 0.82 0.67 0.13) (-Vec3.unitZ) 0.05 redLens carmine
        b.PolygonOutward(diamond 0.16 0.46 0.19,center,scanner)

    /// Gepanzerte Doppellafette mit zwei kantigen Projektorköpfen vor der Wiege.
    let private pulseTurret (b: MeshBuilder) y z scale =
        let platform h w l = terrace (y+h*scale) z (w*scale) (l*scale)
        b.Loft([| platform -0.88 2.76 3.10; platform 0.20 2.97 3.36; platform 0.74 2.61 3.02 |],true,true,graphite)
        b.Loft([| platform 0.20 3.00 3.39; platform 0.46 2.89 3.25 |],false,false,bronze)
        let section x dz w lo hi = rib (x*scale) (z+dz*scale) (w*scale) (y+lo*scale) (y+hi*scale)
        let housing =
            [| section 0. -3.40 2.28 0.91 2.13; section 0. -1.72 3.18 0.56 3.22
               section 0. 1.86 3.09 0.59 3.22; section 0. 3.52 2.24 0.95 2.44 |]
        shell b housing shadowIvory ivory graphite bronze
        panel b housing.[1] housing.[2] 0 0.07 0.93 0.23 0.77 0.10 carmine bronze
        for x in [| -1.49; 1.49 |] do
            let tube dz width halfHeight = section x dz width (1.77-halfHeight) (1.77+halfHeight)
            shell b [| tube -2.6 0.76 0.73; tube -4.65 0.87 0.77; tube -5.37 0.64 0.49
                       tube -8.05 0.50 0.43 |] steel bronze graphite steel
            shell b [| tube -7.76 0.62 0.57; tube -8.35 0.78 0.65; tube -10.32 0.69 0.52 |]
                    graphite shadowIvory carmine graphite
            let mouth = tube -10.34 0.41 0.27
            b.PolygonOutward(mouth,Vec3(x*scale,y,z),ionWall)

    let frigate () : MeshData =
        let b = MeshBuilder()
        let beam = ProceduralMeshes.beam b
        let keel =
            [| rib 0. -41.0 0.25 -0.62 0.18
               rib 0. -29.0 4.65 -3.20 2.15
               rib 0. -16.5 6.65 -4.05 3.65
               rib 0. -5.0 7.40 -4.30 4.30
               rib 0. 13.0 7.40 -4.30 4.30
               rib 0. 26.0 6.50 -3.90 3.65
               rib 0. 34.2 4.60 -3.55 2.90 |]
        shell b keel shadowIvory ivory graphite bronze
        // Goldener Schnabel; die Naht des Druckrumpfes folgt bis zum Heck dem Panzergürtel.
        for i in 0 .. keel.Length - 2 do
            let a, c = keel.[i], keel.[i+1]
            let bow = i <= 1
            for face in [| 0; 1; 7 |] do
                panel b a c face 0.018 0.977 0.025 0.975 0.15 (if bow then gold else ivory) bronze
            for face in [| 3; 5 |] do
                panel b a c face 0.055 0.95 0.06 0.94 0.10 (if bow then bronze else shadowIvory) graphite
            for face in [| 2; 6 |] do
                panel b a c face 0.01 0.98 0.41 0.51 0.10 gold bronze
                // Je ein Abtasterauge an den Bugwangen, über dem Goldgürtel.
                if i = 0 then panel b a c face 0.60 0.93 0.17 0.31 0.12 scanner graphite
                if i >= 2 && i <= 4 then
                    let u = if face = 2 then 0.17 else 0.69
                    for k in 0 .. 4 do
                        let t = 0.055 + 0.19 * float k
                        panel b a c face t (t + 0.090) u (u + 0.13) 0.07 window graphite

        // Durchgehende Mittelrippe, bei Streiflicht auch von der Unterseite gut sichtbar.
        shell b [| rib 0. -40.2 0.18 -0.85 -0.25; rib 0. -22.5 0.72 -4.45 -2.70
                   rib 0. -4.0 1.35 -6.20 -4.0; rib 0. 11.5 1.15 -6.0 -4.0
                   rib 0. 22.0 0.78 -3.95 -3.40 |] bronze graphite steel gold

        // Die zweite Barbette steht höher und ihre Projektoren reichen über den vorderen Turm.
        pulseTurret b 2.95 -22.7 0.99
        pulseTurret b 4.64 -8.65 1.21
        let deckNormal = Vec3.normalize (Vec3(0.,1.,0.05))
        let forward = Vec3.normalize (Vec3(0.,0.05,-1.))
        fleetShield b (Vec3(0.,4.08,21.0)) (Vec3.unitX*2.52) (forward*3.04) deckNormal

        // Gestufte Kommandobrücke; die offene Krone darüber ist aus dem Anflugkorridor zu sehen.
        let tower rings color = b.Loft(rings, true, true, color)
        tower [| terrace 3.95 6.4 4.65 8.4; terrace 7.55 7.2 3.55 6.4 |] shadowIvory
        tower [| terrace 7.46 7.2 3.60 6.45; terrace 8.28 6.85 4.75 7.1 |] gold
        let sill, crown = terrace 8.28 6.85 4.62 7.0, terrace 9.68 7.25 4.29 6.45
        b.Loft([| sill; crown |], false, false, canopy)
        for i in 0 .. sill.Length - 1 do
            let j = (i+1) % sill.Length
            for t in [| 0.; 0.5 |] do
                beam (Vec3.lerp sill.[i] sill.[j] t) (Vec3.lerp crown.[i] crown.[j] t) 0.095 graphite
        tower [| terrace 9.68 7.25 4.43 6.60; terrace 10.28 7.6 3.75 5.8 |] ivory
        // Abtasterleiste quer vor dem Brückenband.
        b.Box(Vec3(0.,8.98,0.18),Vec3(2.55,0.13,0.10),scanner)
        // Zwei gespreizte Pfeiler lassen unter dem schwarzen Querhaupt den Himmel durchscheinen.
        for side in [| -1.; 1. |] do
            let p x y z = Vec3(side*x,y,z)
            b.Slab([| p 2.7 9.7 4.8; p 5.8 13.7 5.4; p 5.6 14.3 11.7; p 2.7 9.7 12.6 |],
                   [| 0.72; 0.57; 0.57; 0.72 |], Vec3.unitX, shadowIvory)
            beam (p 2.7 9.7 4.8) (p 5.8 13.7 5.4) 0.26 bronze
        shell b [| rib 0. 4.95 6.85 13.45 15.45; rib 0. 6.85 8.70 13.12 16.22
                   rib 0. 10.90 7.95 13.32 16.08; rib 0. 12.45 6.15 13.70 15.40 |]
                shadowIvory ivory graphite bronze
        watchEye b (Vec3(0.,14.45,4.94)) 5.86 0.92

        // Seitliche Maschinenhäuser. Die freistehenden Glocken sitzen hinter den Kühllamellen.
        for side in [| -1.; 1. |] do
            let r x z w lo hi = rib (side*x) z w lo hi
            let cheek = [| r 5.95 -27.5 0.52 -1.52 1.72; r 7.40 -18.6 2.10 -3.62 3.32
                           r 7.60 -6.2 2.55 -4.10 4.42; r 7.60 2.1 2.55 -4.10 4.42
                           r 6.14 5.6 1.04 -2.62 3.22 |]
            shell b cheek ivory shadowIvory graphite bronze
            let flank = if side > 0. then 2 else 6
            panel b cheek.[1] cheek.[2] flank 0.05 0.91 0.14 0.86 0.13 steel bronze
            fleetShield b (Vec3(side*10.16,0.13,-2.16)) (Vec3.unitZ*(-side*3.55)) (Vec3.unitY*2.18) (Vec3.unitX*side)
            let pod = [| r 5.0 1.0 0.85 -1.2 1.4; r 7.85 12.8 2.35 -2.45 2.85
                         r 8.6 19.0 2.65 -2.85 3.00; r 8.6 27.0 2.65 -2.85 3.00
                         r 8.6 33.1 2.15 -2.45 2.25 |]
            shell b pod ivory shadowIvory graphite steel
            for z in [| 19.75; 21.1; 22.45; 23.8; 25.15; 26.50 |] do
                b.Box(Vec3(side*8.6,3.06,z),Vec3(1.48,0.14,0.28),graphite)
                b.Box(Vec3(side*8.6,3.21,z-0.14),Vec3(1.40,0.08,0.09),bronze)
            for a,c in Array.pairwise pod do
                let outer = if side > 0. then 2 else 6
                panel b a c outer 0.025 0.97 0.25 0.75 0.09 carmine bronze
            // Kleine Kontrollkanzel unter der vorderen Back; das Gehäuse wächst aus der Kielschräge.
            let blister = [| r 2.8 -15.5 0.55 -3.65 -2.95; r 3.2 -12.8 1.10 -4.60 -2.70
                             r 3.2 -9.0 1.00 -4.35 -2.95 |]
            shell b blister steel shadowIvory canopy graphite
        for nozzle,radius in frigateDrives do drive b nozzle radius
        for p in frigateLights do positionLamp b p 0.48
        b.Build()

    // The telescopic passage stows in the starboard nacelle, aft of the scanner.
    let boardingCollar = Vec3(9.7, -0.1, -2.45)

    /// Die Eskortkorvette trägt ihre Scannergondeln in zwei offenen Sicheln.
    let escort () : MeshData =
        let b = MeshBuilder()
        let beam = ProceduralMeshes.beam b
        let body =
            [| rib 0. -16.1 0.30 -0.62 0.10; rib 0. -11.0 1.45 -1.10 0.95
               rib 0. -5.0 2.65 -1.65 1.55; rib 0. 4.0 3.35 -2.0 1.70
               rib 0. 10.9 2.62 -2.18 1.66; rib 0. 13.0 1.96 -1.95 1.49 |]
        shell b body ivory shadowIvory graphite shadowIvory
        // Lange, verspiegelte Tandemkanzel; Gold bleibt den Vorderkanten und Klauen vorbehalten.
        let hood = [| rib 0. -11.35 0.32 0.66 0.96; rib 0. -6.2 1.35 1.06 2.36
                      rib 0. -2.25 1.47 1.37 2.67; rib 0. 1.7 0.90 1.52 1.93 |]
        shell b hood canopy canopy bronze graphite
        for i in [| 1; 2 |] do
            for f in [| 0; 1; 7 |] do beam hood.[i].[f] hood.[i].[(f+1)%8] 0.095 bronze

        for side in [| -1.; 1. |] do
            let p x y z = Vec3(x*side,y,z)
            // Hinterer Holm und gebogener Bugholm schließen ein freies Dreieck um die Gondel.
            let root = [| p 1.72 0.06 4.85; p 1.7 0.12 11.20 |]
            let elbow = [| p 7.0 -0.02 -1.8; p 7.8 -0.15 5.2 |]
            let tip = [| p 12.75 -0.52 -10.90; p 12.60 -0.52 -5.9 |]
            let wing (a: Vec3[]) (c: Vec3[]) ha hc color =
                let outline = [| a.[0]; c.[0]; c.[1]; a.[1] |]
                b.Slab(outline,[| ha; hc; hc; ha |],Vec3.unitY,graphite)
                let upper = [| a.[0] + Vec3.unitY*ha; c.[0] + Vec3.unitY*hc
                               c.[1] + Vec3.unitY*hc; a.[1] + Vec3.unitY*ha |]
                ProceduralMeshes.armorPanel b upper Vec3.unitY 0.11 color bronze
                let underside = [| a.[0] - Vec3.unitY*ha; c.[0] - Vec3.unitY*hc
                                   c.[1] - Vec3.unitY*hc; a.[1] - Vec3.unitY*ha |]
                ProceduralMeshes.armorPanel b underside (-Vec3.unitY) 0.10 shadowIvory graphite
                beam a.[0] c.[0] (min ha hc * 0.8) gold
            wing root elbow 0.85 0.63 ivory
            wing elbow tip 0.63 0.28 graphite
            let forwardRoot = [| p 1.44 0.20 -9.55; p 1.75 0.28 -7.05 |]
            let forwardPod = [| p 8.25 0.26 -7.00; p 8.22 0.25 -5.36 |]
            wing forwardRoot forwardPod 0.49 0.40 graphite
            let outerFace = if side > 0. then 2 else 6
            panel b body.[1] body.[2] outerFace 0.06 0.94 0.18 0.78 0.07 carmine bronze

            // Außenprojektoren: Das rote Impulsfenster liegt frei zwischen zwei vorgezogenen Schutzklauen.
            let gondola x z w lo hi = rib (side*x) z w lo hi
            let pod = [| gondola 8.42 -9.70 1.12 -1.17 0.91; gondola 8.42 -6.8 1.48 -1.55 1.34
                         gondola 8.42 -1.05 1.48 -1.55 1.34; gondola 8.34 2.25 1.20 -1.20 1.05
                         gondola 8.04 4.35 0.62 -0.76 0.53 |]
            shell b pod graphite ivory graphite bronze
            panel b pod.[1] pod.[2] outerFace 0.07 0.90 0.15 0.80 0.08 carmine bronze
            b.Box(p 9.99 -0.10 -4.50,Vec3(0.08,0.38,1.58),graphite)
            b.Box(p 10.08 -0.10 -4.50,Vec3(0.035,0.20,1.22),scanner)
            for offset in [| -0.78; 0.78 |] do
                let tooth = gondola (8.42+offset)
                shell b [| tooth -13.20 0.29 -0.52 0.20; tooth -12.38 0.43 -0.68 0.39
                           tooth -9.05 0.42 -1.02 0.70; tooth -7.6 0.41 -1.10 0.82 |] gold shadowIvory steel graphite
            b.Box(p 8.42 -0.16 -9.75,Vec3(0.42,0.34,0.09),bronze)
            b.Box(p 8.42 -0.16 -9.85,Vec3(0.27,0.20,0.045),redLens)
            fleetShield b (p 8.42 1.36 -3.9) (Vec3.unitX*0.98) (-Vec3.unitZ*1.78) Vec3.unitY
            fleetShield b (p 8.42 -1.57 -3.9) (-Vec3.unitX*0.98) (-Vec3.unitZ*1.78) (-Vec3.unitY)

            // Gestaffelte Kühlschlitze an der Flügelwurzel folgen ihrer Pfeilung.
            for i in 0 .. 3 do
                let t = 0.12 + float i * 0.18
                let at u = Vec3.lerp (Vec3.lerp root.[0] root.[1] u) (Vec3.lerp elbow.[0] elbow.[1] u) t
                let lift = Vec3.unitY * (0.98 - 0.22*t)
                beam (at 0.40 + lift) (at 0.76 + lift) 0.12 graphite
            // Nach außen geneigte Stabilisatoren bilden aus der Heckansicht ein V.
            b.Slab([| p 1.6 1.15 4.8; p 3.5 4.60 7.9; p 3.75 4.78 10.3; p 2.15 0.90 12.2 |],
                   [| 0.36; 0.15; 0.13; 0.40 |], Vec3.unitX, ivory)
            beam (p 1.6 1.15 4.8) (p 3.5 4.60 7.9) 0.16 gold
        // Das Auge unter dem Bug hängt auch aus einem tiefen Anflug frei vor dem Rumpf.
        shell b [| rib 0. -8.6 0.32 -1.76 -1.04; rib 0. -4.0 1.05 -2.60 -1.44
                   rib 0. 1.3 0.90 -2.50 -1.55 |] graphite bronze canopy graphite
        shell b [| rib 0. -6.50 1.56 -2.70 -1.50; rib 0. -5.20 2.08 -2.95 -1.25
                   rib 0. -2.90 1.25 -2.62 -1.44 |] graphite bronze graphite shadowIvory
        watchEye b (Vec3(0.,-2.10,-6.51)) 1.40 0.53
        b.Box(Vec3(0.,-2.62,-3.45),Vec3(0.57,0.10,0.25),scanner)
        b.Box(boardingCollar,Vec3(0.36,0.90,1.16),graphite)
        b.Box(boardingCollar+Vec3(0.35,0.,0.),Vec3(0.08,0.80,0.94),steel)
        for nozzle,radius in escortDrives do drive b nozzle radius
        for p in escortLights do positionLamp b p 0.39
        b.Build()
