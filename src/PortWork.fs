namespace SpaceEngine

open System

/// Outlying harbour installations. All geometry and collision shapes use metres relative to the dock.
[<RequireQualifiedAccess>]
module PortWork =
    type Part = Tank | Cabin | Radiator | Drum | Antenna
    type Module = { Shape: Part; Center: Vec3; Half: Vec3 }
    type Site =
        { Name: string; Packet: string; Fee: int; Offset: Vec3; Paint: MeshColor; Light: MeshColor
          Modules: Module[]; Braces: (Vec3 * Vec3)[] }

    let private v x y z = Vec3(x,y,z)
    let private part shape x y z w h d = { Shape = shape; Center = v x y z; Half = v w h d }
    let private site name packet fee offset paint light modules braces =
        { Name = name; Packet = packet; Fee = fee; Offset = offset; Paint = paint; Light = light
          Modules = modules; Braces = braces }
    let private rust = MeshColor.rgb 0.43 0.18 0.065
    let private teal = MeshColor.rgb 0.10 0.35 0.35
    let private white = MeshColor.rgb 0.54 0.53 0.47
    let private gold = MeshColor.glow 1.0 0.50 0.11 0.9
    let private ice = MeshColor.glow 0.42 0.84 1.0 0.85
    let private red = MeshColor.glow 0.97 0.12 0.07 0.85
    let private brace a b = a,b
    let sites =
        [| site "Seilwinde Vier" "Schichtrollen" 180 (v 220 -95 195) rust gold
               [| part Drum -27 2 0 18 18 13; part Cabin 2 0 0 14 8 11; part Drum 32 2 0 11 11 13 |]
               [| brace (v -45 -20 0) (v 49 -20 0); brace (v -41 -20 0) (v -27 4 0); brace (v 43 -20 0) (v 30 4 0) |]
           site "Kühlfächer West" "Druckschreiber" 205 (v -220 135 -75) rust red
               [| part Tank 0 3 0 12 12 22; for x in [-48; -24; 24; 48] do part Radiator x 0 0 7 26 2 |]
               [| brace (v -54 -11 0) (v 54 -11 0); brace (v -54 11 0) (v 54 11 0) |]
           site "Freie Krankenbucht" "Patientenpost" 195 (v -220 -95 140) teal gold
               [| part Cabin -31 6 0 22 8 10; part Cabin 13 -6 2 19 7 10; part Tank 40 -9 2 7 7 14 |]
               [| brace (v -37 20 0) (v 23 9 2); brace (v -20 17 0) (v -9 -5 1) |]
           site "Spantlager B" "Prüfstreifen" 235 (v -310 90 -75) rust ice
               [| part Cabin -43 -10 0 15 7 10; for x in [-22; 1; 24; 47] do part Radiator x 6 0 2 22 9 |]
               [| brace (v -51 -16 0) (v 51 -16 0); brace (v -21 29 -7) (v 47 29 -7) |]
           site "Fernpeiler Theta" "Peilband" 225 (v -220 -95 35) white ice
               [| part Antenna -27 19 0 21 20 5; part Cabin 0 -6 0 12 7 9; part Antenna 27 7 0 13 13 3 |]
               [| brace (v -27 16 -5) (v -7 -5 0); brace (v 27 6 -3) (v 8 -6 0) |]
           site "Zählstelle Neun" "Durchlassliste" 265 (v -220 -95 195) white red
               [| part Cabin 0 22 0 36 6 10; part Radiator -30 0 0 5 18 7; part Radiator 30 0 0 5 18 7 |]
               [| brace (v -31 -17 -5) (v -44 -30 -5); brace (v 31 -17 -5) (v 44 -30 -5) |]
           site "Kaltstrom-Speicher" "Temperaturfilm" 185 (v 220 -95 140) teal ice
               [| for x in [-30; 0; 30] do part Tank x 2 0 12 12 23
                  part Cabin 0 -18 0 16 6 9 |]
               [| brace (v -43 -10 9) (v 43 -10 9); brace (v -43 -10 -9) (v 43 -10 -9) |]
           site "Netzwinde am Wracksaum" "Fundregister" 250 (v 275 -140 140) rust gold
               [| part Drum -34 0 0 13 13 11; part Cabin 0 3 0 11 7 10; part Drum 37 13 0 15 15 12 |]
               [| brace (v -44 -14 0) (v 48 2 0); brace (v -43 13 0) (v 43 28 0); brace (v -8 -7 0) (v 9 22 0) |]
           site "Blinder Horchposten" "Echoaufnahme" 235 (v -220 135 -75) teal ice
               [| part Antenna -12 7 0 28 28 7; part Cabin 25 -15 0 14 7 9; part Radiator 45 -9 0 3 15 4 |]
               [| brace (v -12 8 -7) (v 19 -16 0); brace (v 19 -16 0) (v 48 -16 0) |] |]

    let center index = Universe.sectors.[index].Outpost.Position + sites.[index].Offset
    let terminal index = center index + v 0 0 52
    let range = 36.0
    let maxSpeed = 18.0
    let linkSeconds = 3.0

    let hasHoist index = index = 0 || index = 3 || index = 7
    let hoistRail = v -37 37 -9, v 37 37 -9
    let trolley index time =
        let a,b = hoistRail
        center index + Vec3.lerp a b (0.5 + 0.47 * sin (time * 0.095 + float index))

    let slingLoad () =
        let b = MeshBuilder()
        let steel = MeshColor.metal 0.33 0.37 0.38
        b.Box(v 0 0 0,v 4.2 1.8 2.4,rust)
        for x in [-3.;3.] do
            for z in [-2.;2.] do
                ProceduralMeshes.beam b (v (x*0.45) -1. z) (v x -9. z) 0.26 steel
        b.Box(v 0 -12 0,v 4.8 3.2 3.6,MeshColor.rgb 0.43 0.33 0.16)
        for x in [-3.5;0.;3.5] do b.Box(v x -12 0,v 0.43 3.4 3.8,steel)
        b.Build()

    // Two bits per port: available, aboard, delivered. Partial radio links are transient.
    let status mask sector = (mask >>> (sector * 2)) &&& 3
    let valid mask =
        mask >= 0 && mask < (1 <<< (2 * sites.Length))
        && Array.forall (fun i -> status mask i <> 3) [|0 .. sites.Length-1|]
    let collect mask sector =
        if status mask sector = 0 then mask ||| (1 <<< (2*sector)) else mask
    let deliver mask sector =
        if status mask sector = 1 then mask + (1 <<< (2*sector)), sites.[sector].Fee else mask, 0

    let appendMesh (b: MeshBuilder) sector =
        let s = sites.[sector]
        let steel = MeshColor.metal 0.25 0.29 0.31
        let dark = MeshColor.rgb 0.085 0.10 0.12
        let beam a c r color = ProceduralMeshes.beam b (s.Offset+a) (s.Offset+c) r color
        let box p half color = b.Box(s.Offset+p,half,color)
        for a,c in s.Braces do
            let drop = v 0 -2.8 -2.
            beam a c 0.92 steel
            beam (a+drop) (c+drop) 0.75 steel
            let bays = max 1 (int (Vec3.distance a c / 8.))
            for i in 0..bays-1 do
                let u,w = Vec3.lerp a c (float i / float bays), Vec3.lerp a c (float (i+1) / float bays)
                beam u (w+drop) 0.46 steel
                beam w (w+drop) 0.32 steel
        if hasHoist sector then
            let a,c = hoistRail
            for top in [a;c] do
                beam (v top.X -14. -17.) top 1.45 s.Paint
                beam (v top.X -14. 1.) top 1.1 steel
                box top (v 3.6 2. 2.2) steel
            beam a c 0.65 steel
            beam (a+v 0 2 0) (c+v 0 2 0) 0.48 dark
        for m in s.Modules do
            let p,h = m.Center,m.Half
            let boxAt x y z w height d color = box (p+v x y z) (v w height d) color
            let ring scale depth =
                let w = s.Offset+p
                ProceduralMeshes.ring(16,w.X,w.Y,w.Z+depth,h.X*scale,h.Y*scale)
            match m.Shape with
            | Drum | Tank ->
                b.Loft([|ring 0.78 -h.Z;ring 1.0 (-h.Z*0.75);ring 1.0 (h.Z*0.75);ring 0.78 h.Z|],true,true,s.Paint)
                for k in [-0.74; -0.26; 0.26; 0.74] do
                    b.Loft([|ring 1.04 (h.Z*k-0.8);ring 1.04 (h.Z*k+0.8)|],false,false,steel)
                if m.Shape = Drum then
                    for i in 0..10 do
                        let z = h.Z * (-0.72 + float i * 0.14)
                        b.Loft([|ring 1.01 z;ring 1.025 (z+0.52)|],false,false,dark)
                    for side in [-1.;1.] do
                        b.Loft([|ring 1.12 (side*h.Z-0.65);ring 1.12 (side*h.Z+0.65)|],true,true,steel)
                    b.Loft([|ring 0.25 (h.Z+0.69);ring 0.24 (h.Z+1.9)|],false,true,s.Paint)
                    for k in 0..5 do
                        let a = float k*Math.PI/3.
                        beam (p+v 0 0 (h.Z+1.1)) (p+v (cos a*h.X*0.94) (sin a*h.Y*0.94) (h.Z+1.1)) 0.83 dark
                else boxAt 0. 0. (h.Z+0.4) 1.5 (h.Y*0.48) 0.6 s.Light
            | Antenna ->
                // An open concave receiver with a raised feed and three support struts.
                b.Loft([|ring 0.14 -h.Z;ring 0.62 0.;ring 1.0 h.Z|],false,false,white)
                b.Loft([|ring 1.0 h.Z;ring 0.62 0.;ring 0.14 -h.Z|],false,false,steel)
                let feed = p+v 0 0 (h.Z+10.)
                for i in 0..2 do
                    let a = float i*Math.PI*2./3.
                    beam (p+v (cos a*h.X) (sin a*h.Y) h.Z) feed 0.65 dark
                box feed (v 1.4 1.4 2.) s.Light
            | Cabin ->
                let section z scale =
                    ProceduralMeshes.hullSection 0. z (h.X*scale) (-h.Y*scale) (h.Y*scale)
                    |> Array.map (fun q -> s.Offset+p+q)
                b.Loft([|section -h.Z 0.80;section (-h.Z*0.63) 1.0;section (h.Z*0.73) 1.0; section h.Z 0.86|],true,true,s.Paint)
                boxAt 0. 0. (h.Z+0.1) (h.X*0.73) (h.Y*0.31) 0.23 dark
                for i in -2..2 do
                    let x = float i*h.X*0.28
                    boxAt x 0. (h.Z+0.42) (h.X*0.105) (h.Y*0.18) 0.24 (if i=1 then steel else s.Light)
                for i in 0..4 do
                    boxAt (-h.X*0.45 + float i*1.45) (h.Y*0.98) 0. 0.40 0.55 (h.Z*0.52) dark
                let endCap = p+v (h.X*0.79) (-h.Y*0.75) (h.Z*0.73)
                beam (endCap-v 0. (h.Y*0.3) 2.) (endCap+v 0. (h.Y*1.4) 2.) 0.55 steel
                if sector = 2 then
                    let plate = p+v (-h.X*0.48) (h.Y*0.8+2.6) (h.Z*0.68)
                    box plate (v 5.4 4.7 0.5) dark
                    box (plate+v 0 0 0.6) (v 3.2 0.9 0.3) ice
                    box (plate+v 0 0 0.65) (v 0.9 3.2 0.32) ice
                    for i in [0.;1.] do
                        let x = h.X*(0.25+i*0.32)
                        let a,c = p+v x (h.Y+1.) (-h.Z*0.55), p+v x (h.Y+1.) (h.Z*0.50)
                        beam a c 1.35 white
                        box (a+v 0 0 2.) (v 1.55 1.4 0.32) teal
            | Radiator ->
                box p h dark
                for k in -3..3 do boxAt 0. (float k*h.Y*0.27) (h.Z+0.2) h.X 0.56 0.6 s.Paint
                boxAt 0. h.Y 0. h.X 0.7 h.Z s.Light
        // A fork of guide lamps marks the transfer point in open space.
        for side in [-1.;1.] do
            let tip = v (side*18.) -9. 36.
            beam (v (side*9.) -5. 0.) tip 0.85 steel
            box tip (v 1.4 1.3 2.2) s.Light
