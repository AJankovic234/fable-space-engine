namespace SpaceEngine

open System

/// Stations are built in metres, relative to the docking target. World +Z faces traffic.
/// The belt runs on what the Eclipse left behind: dull steel, faded signal paint, sodium lamps
/// and fluorescent tubes, and a good third of the windows dark. Only Morgenlicht is fully lit.
[<RequireQualifiedAccess>]
module StationMeshes =
    let private v x y z = Vec3(x,y,z)
    let private rgb = MeshColor.rgb
    let private glow r g b = MeshColor.glow r g b 0.92
    let private steel = rgb 0.115 0.145 0.165
    let private alloy = rgb 0.33 0.35 0.35
    let private iron = rgb 0.045 0.058 0.066
    let private ochre = rgb 0.52 0.25 0.04
    /// Fluorescent tubes behind the crew windows: a pale, slightly green white.
    let private ice = glow 0.62 0.90 0.74
    /// Sodium-vapour work light.
    let private lamp = glow 1.0 0.50 0.12
    let private pane = MeshColor.glass 0.03 0.04 0.05 0.0
    let private red = glow 1.0 0.16 0.06
    let private bar = ProceduralMeshes.beam
    let private box (b: MeshBuilder) x y z hx hy hz color = b.Box(v x y z, v hx hy hz, color)

    // Octagonal, chamfered decks. Each profile gives height and horizontal half extents.
    let private deck (b: MeshBuilder) x z (profile: (float * float * float)[]) color =
        let section (y,w,d) =
            let c = min w d * 0.38
            [| v (x-w+c) y (z+d); v (x+w-c) y (z+d); v (x+w) y (z+d-c); v (x+w) y (z-d+c)
               v (x+w-c) y (z-d); v (x-w+c) y (z-d); v (x-w) y (z-d+c); v (x-w) y (z+d-c) |]
        b.Loft(Array.map section profile, true, true, color)

    let private drum (b: MeshBuilder) sides x z (profile: (float * float)[]) color =
        let section (y,r) =
            Array.init sides (fun i ->
                let a = (float i + 0.5) * 2.0 * Math.PI / float sides
                v (x + r*cos a) y (z + r*sin a))
        b.Loft(Array.map section profile, true, true, color)

    /// Window band. `dark` of every eight panes stay unlit; which ones depends on the band.
    let private windowBand (b: MeshBuilder) dark x y z count pitch color =
        let width = float count * pitch * 0.5
        box b x y z width 1.45 0.4 iron
        for i in 0 .. count-1 do
            let h = ((i * 73856093) ^^^ (int (abs x + 2.0 * abs y) * 19349663)) &&& 0x7fffffff
            let centre = x-width + (float i+0.5)*pitch
            if h % 8 >= dark then box b centre y (z+0.5) (pitch*0.30) 0.71 0.2 color
            else box b centre y (z+0.45) (pitch*0.30) 0.71 0.15 pane

    /// Belt stations ration their power: three panes in eight are dark.
    let private windows (b: MeshBuilder) x y z count pitch color = windowBand b 3 x y z count pitch color

    // The same AETHER inspection head looks down on arriving ships at Helios and Morgenlicht.
    let private inspectionHead (b: MeshBuilder) x y z =
        deck b x z [|y-2.,5.8,4.8; y+1.,5.8,4.8; y+3.,3.6,3.0|] steel
        for side in [-1.; 1.] do
            box b (x+side*4.0) (y+4.) z 0.8 2.8 1.7 alloy
        let rim depth radius = ProceduralMeshes.ring(12, x, y+6., z+depth, radius, radius)
        b.Loft([|rim -3. 3.4; rim 0. 4.2; rim 4. 3.8|],true,true,iron)
        b.Loft([|rim 4. 2.9; rim 4.6 2.4|],true,true,MeshColor.metal 0.39 0.33 0.19)
        b.Loft([|rim 4.6 2.35; rim 5.2 1.45|],true,true,MeshColor.glass 0.33 0.035 0.023 0.18)
        box b x (y+6.) (z+5.25) 1.3 0.27 0.16 red
        box b x (y+9.9) (z+1.3) 4.7 0.5 5.2 steel

    // Keep the cruiser's authored normals and stop at the forward inspection bulkhead.
    let private cruiserStern (b: MeshBuilder) (origin: Vec3) =
        let mesh = CapitalMeshes.cruiser()
        let data = mesh.Vertices
        let offset i = int mesh.Indices.[i] * Layout.VertexFloats
        let behindBulkhead i = float data.[offset i] <= 11.05
        for t in 0 .. mesh.Indices.length / 3 - 1 do
            let first = 3*t
            if behindBulkhead first && behindBulkhead (first+1) && behindBulkhead (first+2) then
                for k in 0..2 do
                    let i = offset (first+k)
                    let vec j = v (float data.[i+j]) (float data.[i+j+1]) (float data.[i+j+2])
                    let color = MeshColor.glow (float data.[i+6]) (float data.[i+7]) (float data.[i+8]) (float data.[i+9])
                    b.Vertex(origin + vec 0 * CapitalFlight.hullScale, vec 3, color)

    let private dock (b: MeshBuilder) =
        let hoop z r = ProceduralMeshes.ring(12,0.0,0.0,z,r,r)
        let front, rear = hoop 3.0 15.8, hoop -5.0 16.7
        let signal = glow 0.12 0.99 0.80
        for i in 0..11 do
            let j = (i+1)%12
            bar b front.[i] front.[j] 1.55 alloy
            bar b rear.[i] rear.[j] 1.6 steel
            bar b rear.[i] front.[i] 1.18 iron
            bar b rear.[i] front.[j] 0.54 ochre
            let point t = Vec3.lerp front.[i] front.[j] t + v 0.0 0.0 1.67
            bar b (point 0.13) (point 0.87) 0.64 signal
        // The pressure bridge meets the lower lip. The centre stays open right through it.
        bar b (v 0 -15 -5) (v 0 -15 -99) 3.6 steel
        for z in [-18.;-34.;-50.;-66.;-82.] do
            box b 0 -15 z 4.2 4.05 1.0 alloy
            box b -4.3 -15 z 0.35 0.65 1.0 signal

    let private conveyor (b: MeshBuilder) (start: Vec3) (finish: Vec3) bays =
        let point t y z = Vec3.lerp start finish t + v 0 y z
        for side in [-5.;5.] do
            bar b (point 0.0 0 side) (point 1.0 0 side) 1.3 ochre
            bar b (point 0.0 -7 side) (point 1.0 -7 side) 1.0 steel
            for i in 0..bays-1 do
                let a,c = float i/float bays, float (i+1)/float bays
                let y = if i%2=0 then 0.0 else -7.0
                bar b (point a y side) (point c (-7.0-y) side) 0.82 alloy
        bar b start finish 3.2 iron
        for i in 0..bays do
            let t = float i / float bays
            bar b (point t 3.1 -4) (point t 3.1 4) 0.7 steel

    let internal quarryClamps =
        let ring = ProceduralMeshes.ring(12,-116.,38.,-133.,72.,68.)
        [| for start in [0; 6] do
               for i in start .. start+4 do
                   yield struct (ring.[i], ring.[i+1]) |]

    let internal refineryRadiators =
        [| for side in [-1.; 1.] do
               for i in 0..2 do
                   let n = float i
                   yield v (side*(107.+24.*n)) (40.-14.*n) (-159.-8.*n) |]

    let internal bazaarPavilions =
        [| v -139 76 -157; v -88 92 -164; v -31 110 -155
           v 29 106 -146; v 83 98 -156; v 135 71 -158 |]

    let private kestrel (b: MeshBuilder) =
        let rust = rgb 0.33 0.115 0.04
        let rock = v -116 38 -113
        // Exposed ore strata on the fractured quarry face. The parent rock still
        // shares its irregular silhouette with the field asteroids.
        let stone = (ProceduralMeshes.asteroid 271 2).Vertices
        let vertex i = v (float stone.[i]) (float stone.[i+1]) (float stone.[i+2])
        let strata (p: Vec3) =
            let band = sin (p.Y*12. - p.X*5.5 + 1.4*sin (p.Z*4.3))
            if band > 0.82 then rgb 0.55 0.42 0.25
            elif band < -0.55 then rgb 0.195 0.20 0.225
            else rgb 0.37 0.34 0.30
        for t in 0..stone.length/(3*Layout.VertexFloats)-1 do
            let i = t*3*Layout.VertexFloats
            let a,c,d = vertex i,vertex (i+Layout.VertexFloats),vertex (i+2*Layout.VertexFloats)
            b.TriangleColors(rock+a*57.,rock+c*57.,rock+d*57.,strata a,strata c,strata d)
        // The cutting head bites into the exposed face above the first belt.
        let cutter r z = ProceduralMeshes.ring(10,-100.,49.,z,r,r)
        b.Loft([|cutter 8.0 -89.; cutter 7.2 -79.; cutter 4.2 -75.|],true,true,ochre)
        b.Loft([|cutter 3.4 -75.4; cutter 3.4 -74.4|],true,true,iron)
        for dx,dy,z in [-3.,4.,-75.; 3.,-5.,-75.; -30.,-17.,-77.] do
            box b (-100.+dx) (49.+dy) z 1.35 1.25 1.3 lamp
        // Two winch houses and three tension lines hold the rock off the machinery.
        for anchor,winch in [v -103 70 -105,v -40 24 -109; v -94 26 -79,v -42 -4 -91; v -130 13 -106,v -44 -25 -121] do
            bar b rock anchor 2.1 iron
            b.Box(anchor,v 3.8 4.1 3.8,ochre)
            bar b anchor winch 0.94 alloy
            b.Box(winch,v 4.8 5 4.0,rust)
        deck b -13 -113 [|-31.,29.,17.; -20.,38.,22.; 9.,38.,22.; 16.,26.,17.|] steel
        deck b -15 -112 [|11.,20.,16.; 25.,24.,19.; 30.,22.,17.|] rust
        windows b -14 2 -90.6 13 3.7 lamp
        deck b 85 -118 [|-31.,49.,17.; -20.,54.,23.; -11.,54.,23.; -8.,50.,21.|] iron
        for x,top in [65.,57.;112.,43.] do
            // Flared ore bins over narrow discharge chutes, with striped service gantries.
            deck b x -119 [|-16.,8.,11.; 4.,10.,12.; 18.,19.,17.; top-6.,19.,17.|] rust
            deck b x -119 [|top-7.,20.,18.; top-3.,22.,20.; top,22.,20.|] ochre
            deck b x -119 [|top,17.,15.; top+0.5,17.,15.|] iron
            for dx in [-14.;14.] do
                bar b (v (x+dx) -20 -103) (v (x+dx) (top-6.) -103) 1.35 alloy
            box b x (top-15.) -101.4 10 1.3 0.6 lamp
            for dx in [-14.;-7.;0.;7.;14.] do
                bar b (v (x+dx) (top+0.8) -132) (v (x+dx) (top+0.8) -106) 1.0 steel
            for dy in [0.;4.;8.] do
                box b x (top-23.+dy) -101.5 12 0.65 0.9 rust
            windows b x -14 -94.6 6 3.5 ice
        conveyor b (v -90 47 -85) (v -20 17 -99) 8
        conveyor b (v -2 23 -104) (v 65 59 -113) 9
        conveyor b (v 25 -12 -118) (v 110 2 -123) 8
        for i in 1..7 do
            let p = Vec3.lerp (v -89 47 -85) (v -20 17 -99) (float i/8.)
            deck b p.X p.Z [|p.Y+3.,2.7,2.3; p.Y+5.,2.3,1.9; p.Y+6.5,1.0,1.0|] (rgb 0.24 0.20 0.16)
        // Ballast hoist and caged control cab above the mine head.
        bar b (v -38 12 -122) (v -48 56 -124) 2.0 ochre
        bar b (v -18 18 -124) (v -48 56 -124) 1.1 alloy
        bar b (v -48 56 -124) (v -78 70 -119) 2.3 ochre
        bar b (v -78 70 -119) (v -80 46 -119) 0.66 alloy
        box b -48 53 -122 6 3 5 steel
        windows b -48 53 -116.8 4 2.5 ice

        // The salvage collar holds the quarry between two open jaws. The wide
        // crossmembers carry the load back to the platform, with tethers inside.
        for struct (a,c) in quarryClamps do
            bar b a c 5.0 rust
            bar b (a-v 0 0 20) (c-v 0 0 20) 3.2 iron
            bar b a (c-v 0 0 20) 1.8 ochre
            let joint = Vec3.lerp a c 0.5
            b.Box(joint+v 0 0 5.1,v 3.4 3.4 0.6,ochre)
            b.Box(joint+v 0 0 5.8,v 1.55 1.55 0.5,lamp)
        bar b (v -51 59 -142) (v 27 12 -142) 4.0 steel
        bar b (v -55 5 -143) (v 40 -27 -134) 3.4 ochre

        // Counterweights and gravity separators under the loading floor.
        for x,r,foot in [-11.,17.,-97.;68.,22.,-86.] do
            drum b 12 x -125 [|foot,r*0.60; foot+9.,r; -43.,r; -32.,r*0.77|] steel
            drum b 12 x -125 [|-61.,r+1.0; -59.,r+1.0|] ochre
            for i in -2..2 do
                let cx = x + float i*r*0.24
                let front = -125. + sqrt (r*r - (cx-x)*(cx-x))
                box b cx -52 front 1.4 4.4 0.55 ice
            bar b (v (x-r) -48 -124) (v (x-r-8.) -31 -114) 2.2 rust

    let private helios (b: MeshBuilder) =
        let oxide = rgb 0.25 0.085 0.045
        let copper = rgb 0.40 0.18 0.065
        let fire = glow 1.0 0.27 0.035
        deck b 0 -122 [|-37.,76.,22.; -24.,111.,31.; -10.,115.,33.; -4.,104.,28.|] iron
        for x,z,h,r in [-77.,-129.,80.,24.; -11.,-153.,139.,30.; 61.,-123.,105.,22.] do
            // Bosh, belly, tapering stack and a separately jacketed chimney.
            drum b 12 x z [|-10.,r*0.76; 5.,r; h*0.28,r*1.08; h*0.50,r*0.73; h*0.72,r*0.61|] oxide
            drum b 12 x z [|h*0.70,r*0.74; h*0.74,r*0.74; h*0.78,r*0.53; h-5.,r*0.48|] steel
            drum b 12 x z [|h-7.,r*0.54; h-3.,r*0.61; h,r*0.61|] copper
            drum b 12 x z [|h,r*0.45; h+0.15,r*0.45|] fire
            for y,rr in [h*0.12,r*1.06; h*0.32,r*1.05; h*0.56,r*0.73] do
                drum b 12 x z [|y-1.8,rr; y+1.8,rr|] copper
            for a in [0.30;0.93;1.56;2.19;2.82] do
                let wall y radial = v (x+cos a*radial) y (z+sin a*radial)
                bar b (wall (h*0.14) (r*1.06)) (wall (h*0.28) (r*1.09)) 2.0 fire
                bar b (wall (h*0.16) (r*1.13)) (wall (h*0.23) (r*1.16)) 0.73 lamp
            // Inspection catwalk follows the widest part of each furnace.
            let walk y a = v (x+cos a*r*1.17) y (z+sin a*r*1.17)
            for i in 0..9 do
                let a,d,y = float i*Math.PI/5.,float (i+1)*Math.PI/5.,h*0.34
                bar b (walk y a) (walk y d) 1.15 steel
                bar b (walk y a) (walk (y+3.0) a) 0.42 alloy
                bar b (walk (y+3.0) a) (walk (y+3.0) d) 0.42 alloy
            let pipe dx y dz = v (x+dx*r) y (z+dz*r)
            for side in [-1.;1.] do
                let run = [|pipe (side*0.43) (h*0.63) 0.48; pipe (side*1.0) (h*0.59) 0.62
                            pipe (side*1.27) (h*0.48) 0.64; pipe (side*1.27) 2.0 0.64; pipe (side*1.01) -8.0 0.64|]
                for i in 1..run.Length-1 do bar b run.[i-1] run.[i] 2.7 copper
        // Heat exchangers feed the common manifold below the furnace floor.
        for x in [-111.;-99.;92.;104.] do
            drum b 10 x -113 [|-18.,5.; 9.,5.; 15.,3.2; 32.,3.2; 34.,3.8|] alloy
            drum b 10 x -113 [|23.,3.5; 26.,3.5|] oxide
        deck b 7 -91 [|-21.,32.,13.; -4.,32.,13.; 1.,26.,9.|] steel
        windows b 7 -9 -77.7 16 3.0 lamp
        inspectionHead b 17. 1. -88.
        for a,c in [v -77 28 -136,v -11 50 -159; v -11 51 -159,v 61 35 -131] do
            bar b a c 2.15 steel
            bar b (a+v 0 3 0) (c+v 0 3 0) 0.62 copper
        for x in [-79.;-47.;-15.;17.;49.;81.] do
            box b x -21 -89.6 10 1.7 0.8 fire

        // Heat-recovery wings step outward from the furnaces. Dark fins break up
        // the hot ceramic faces, with cooler copper toward the outer tips.
        let copperFrame = MeshColor.metal 0.40 0.21 0.10
        for i in 0 .. refineryRadiators.Length-1 do
            let p = refineryRadiators.[i]
            let h = 27. - 3. * float (i%3)
            let face = p.Z+3.8
            let ceramic = MeshColor.glow 0.93 0.29 0.073 (0.60 - 0.12*float (i%3))
            deck b p.X p.Z [|p.Y-h,6.,2.8; p.Y-h+7.,11.,4.; p.Y+h-5.,11.,4.; p.Y+h,7.5,2.8|] iron
            for dx in [-9.;9.] do
                bar b (v (p.X+dx) (p.Y-h+7.) face) (v (p.X+dx) (p.Y+h-5.) face) 1.1 copperFrame
            for row in -3..3 do
                let y = p.Y+float row*5.1
                box b p.X y (face+0.35) 7.25 1.55 0.55 ceramic
                box b p.X (y-1.8) (face+1.1) 8.4 0.45 1.05 copper
            bar b (p-v 0 h 1.8) (v (p.X*0.71) -29 -143) 3.2 copper
        deck b -5 -137 [|-99.,20.,16.; -84.,36.,24.; -61.,44.,29.; -34.,37.,24.|] oxide
        deck b -5 -137 [|-81.,36.,24.; -74.,42.,29.; -71.,42.,29.|] copper
        for x in [-33.;-19.;-5.;9.;23.] do
            bar b (v x -73 -108) (v x -46 -110) 1.55 fire

    let private drift (b: MeshBuilder) =
        // Dark, grimy hulls and tarpaulin roofs under the neon: the signs are the only clean colour on Drift-9.
        let jade = rgb 0.03 0.115 0.11
        let wine = rgb 0.15 0.038 0.065
        let enamel = rgb 0.225 0.20 0.15
        let ochre = rgb 0.27 0.135 0.028
        let signs = [|glow 1.0 0.23 0.51; glow 0.05 1.0 0.80; lamp; glow 0.50 0.43 1.0|]
        // Retired hulls point to different moorings. Their old engine blocks, raised
        // wheelhouses and rows of portholes remain visible below the market decks.
        let houseboat (centre: Vec3) yaw length radius paint sign =
            let point x y z = centre + v (x*cos yaw - z*sin yaw) y (x*sin yaw + z*cos yaw)
            let rib (t,w,low,high) =
                let x,h = t*length,w*radius
                [|point x (high*radius) (-0.70*h); point x (high*radius) (0.70*h)
                  point x (high*radius-2.0) h; point x (low*radius+3.0) h
                  point x (low*radius) (0.46*h); point x (low*radius) (-0.46*h)
                  point x (low*radius+3.0) -h; point x (high*radius-2.0) -h|]
            let shell profile color = b.Loft(Array.map rib profile,true,true,color)
            shell [|-1.,0.62,-0.69,0.4; -0.83,1.,-1.,0.64; 0.42,1.,-0.90,0.64
                    0.77,0.71,-0.51,0.71; 1.,0.13,0.30,0.56|] paint
            for t in [-0.65;-0.56;0.21] do
                shell [|t,1.03,-0.98,0.66; t+0.035,1.03,-0.98,0.66|] steel
            shell [|-0.68,0.72,0.62,1.22; -0.53,0.78,0.63,1.48; -0.30,0.71,0.63,1.35; -0.19,0.51,0.63,0.93|] enamel
            for side in [-1.;1.] do
                let flank x y z = point (x*length) (y*radius) (side*z*radius)
                bar b (flank -0.53 1.14 0.76) (flank -0.32 1.13 0.72) 0.90 sign
                bar b (flank -0.83 -0.32 1.0) (flank 0.42 -0.32 1.0) 1.4 steel
                bar b (flank 0.42 -0.32 1.0) (flank 0.91 0.35 0.39) 1.2 steel
                for i in 0..15 do
                    let x = -0.78 + float i*0.077
                    bar b (flank x 0.24 1.015) (flank (x+0.028) 0.24 1.015) 0.58 lamp
            for dz in [-0.36;0.36] do
                bar b (point (-length) 0 (dz*radius)) (point (-length-3.) 0 (dz*radius)) 2.7 iron

        houseboat (v -56 30 -143) 0.22 68.0 15.0 jade signs.[1]
        houseboat (v 57 -12 -104) -0.29 76.0 14.0 wine signs.[0]
        houseboat (v -83 -23 -102) 2.87 45.0 10.0 ochre signs.[2]
        houseboat (v 53 52 -158) -2.94 53.0 10.0 (rgb 0.07 0.09 0.145) signs.[3]
        // Pontoon with stacked customs containers, corrugated end doors and shop signs.
        deck b 0 -124 [|-24.,22.,22.; -15.,27.,26.; 11.,24.,22.|] steel
        let colors = [|jade; ochre; wine; enamel; rgb 0.055 0.095 0.145|]
        let container x y z i =
            let paint = colors.[i%colors.Length]
            box b x y z 7.0 4.3 9.0 paint
            for dx in [-5.6;-2.8;0.;2.8;5.6] do box b (x+dx) y (z+9.) 0.36 3.6 0.42 steel
            box b x (y+4.2) (z+9.3) 6.9 0.50 0.48 paint
        for start,y,z,count in [-86.,45.,-141.,4; 32.,1.,-100.,5; -19.,17.,-122.,3; 34.,64.,-157.,3] do
            for i in 0..count-1 do
                let x = start+15.*float i
                container x y z (i + int (abs start))
                if i%3=1 then container x (y+8.8) z (i+2)
        for a,c in [v -97 -13 -101,v -71 29 -136; v -8 31 -132,v 26 48 -152
                    v 84 50 -151,v 106 -4 -115; v 11 -13 -105,v -21 -24 -108] do
            bar b a c 1.75 alloy
            bar b (a+v 0 3.2 0) (c+v 0 3.2 0) 0.48 ochre
        // Stretched awnings and festoon lights identify the open-air trading decks.
        for x,y,z,i in [-71.,53.,-126.,0; -45.,52.,-119.,1; 38.,13.,-89.,2; 65.,12.,-93.,0] do
            let p dx dy dz = v (x+dx) (y+dy) (z+dz)
            let roof = [|p -11 2 -6; p 11 3 -6; p 11 -3 9; p -11 -1 9|]
            b.Slab(roof,Array.create 4 0.38,Vec3.up,colors.[(i+1)%5])
            for dx in [-11.;11.] do bar b (p dx -10 8) (p dx -2 8) 0.55 alloy
            bar b roof.[2] roof.[3] 0.65 signs.[i]
        let masts = [|v -114 69 -151,35.; v -30 76 -137,33.; v 54 87 -157,57.
                      v 113 29 -118,-5.; v -83 7 -99,-18.; v -1 34 -104,8.|]
        for p,foot in masts do bar b (v p.X foot p.Z) p 0.84 alloy
        for i,j in [0,1;1,2;2,3;4,5] do
            let a,c = fst masts.[i],fst masts.[j]
            let point t = Vec3.lerp a c t - v 0 (38.*t*(1.-t)) 0
            for i in 1..12 do
                let p = point (float i/12.)
                bar b (point (float (i-1)/12.)) p 0.26 iron
                b.Box(p-v 0 0.8 0,v 0.88 1.4 0.88,signs.[i%4])

        // The bazaar grew above the old freighters. Salvaged greenhouse roofs,
        // shopfronts and lamp bridges make up a second skyline.
        for i in 0 .. bazaarPavilions.Length-1 do
            let p = bazaarPavilions.[i]
            let paint = colors.[(i+2)%colors.Length]
            let light = signs.[i%signs.Length]
            deck b p.X p.Z [|p.Y-12.,14.,10.; p.Y-5.,23.,16.; p.Y+2.,23.,16.|] steel
            deck b p.X p.Z [|p.Y+2.,21.,15.; p.Y+13.,21.,15.; p.Y+14.,20.,14.|] paint
            deck b p.X p.Z [|p.Y+13.,22.8,16.7; p.Y+15.,23.,17.; p.Y+24.,10.,10.; p.Y+26.,7.,8.|] paint
            let foot = if i < 3 then v -58 41 -147 else v 61 54 -155
            bar b (p-v 11 10 4) (foot-v 5 0 0) 2.3 steel
            bar b (p+v 11 -10 -4) (foot+v 5 0 0) 2.1 alloy
            for dx in [-13.;0.;13.] do
                box b (p.X+dx) (p.Y+7.5) (p.Z+15.3) 5.5 3.6 0.85 iron
                box b (p.X+dx) (p.Y+8.2) (p.Z+16.2) 4.25 1.75 0.45 light
            bar b (p+v -19 14 16) (p+v 19 14 16) 1.0 light
            for side in [-1.;1.] do
                bar b (p+v (side*18.) 16 15) (p+v (side*8.) 23 10) 0.82 enamel
            if i > 0 then
                let prev = bazaarPavilions.[i-1]
                for dz in [-2.;2.] do
                    let a,c = prev+v 9 -2 dz,p+v -9 -2 dz
                    bar b a c 1.30 alloy
                    bar b (a+v 0 3 0) (c+v 0 3 0) 0.40 ochre
                    for t in [0.34;0.50;0.66] do
                        b.Box(Vec3.lerp a c t+v 0 1.3 0,v 0.84 1.6 0.58,light)

    let private kepler (b: MeshBuilder) =
        let paint = rgb 0.46 0.245 0.045
        let primer = rgb 0.27 0.11 0.06
        let weld = glow 0.52 0.87 1.0
        let origin = v 52 14 -148
        let k = CapitalFlight.hullScale
        cruiserStern b origin
        // The heavy cruiser is at flight scale. Its unfinished forebody exposes
        // transverse frames and four longitudinal stringers beyond the last bulkhead.
        let rib x w bottom top =
            ProceduralMeshes.hullSection 0.0 x w bottom top
            |> Array.map (fun p -> origin + v p.Z p.Y (-p.X) * k)
        let frames =
            [|for i in 0..6 do
                  let t = float i/6.
                  yield rib (11.+34.*t) (16.-2.8*t) (-10.+1.4*t) (9.4-1.2*t)|]
        for i in 0..frames.Length-1 do
            let frame = frames.[i]
            for j in 0..frame.Length-1 do
                bar b frame.[j] frame.[(j+1)%frame.Length] 1.35 primer
                if i>0 && j%2=0 then bar b frames.[i-1].[j] frame.[j] 1.18 alloy
        // Parallel dock sills and transverse cradles. The near sill stays below the
        // waterline of the hull so that the construction gaps read from the approach.
        for z in [-84.;-211.] do
            deck b 0 z [|-48.,149.,5.; -41.,162.,7.; -32.,162.,7.; -29.,156.,6.|] steel
            for i in -7..7 do
                let x = float i*21.
                box b x -38 (z+7.2) 6 1.3 0.4 paint
                box b x -38 (z+7.65) 1.1 0.8 0.2 lamp
        for x in [-137.;-68.;0.;68.;139.] do
            bar b (v x -40 -83) (v x -40 -212) 3.4 iron
            for z,dz in [-84.,-25.;-211.,25.] do
                bar b (v x -34 z) (v x -14 (z+dz)) 3.1 paint
                box b x -13 (z+dz) 5.4 2.8 5.4 alloy
        for x,h,dx in [-142.,79.,32.; -58.,123.,43.; 31.,94.,33.; 137.,62.,-25.] do
            let z = -208.
            let left y = v (x-5.) y z
            let right y = v (x+5.) y z
            bar b (left -32) (left h) 2.2 paint
            bar b (right -32) (right h) 2.2 paint
            for i in 0..5 do
                let y = -31.+float i*(h+31.)/6.
                let top = y+(h+31.)/6.
                bar b (left y) (right top) 1.05 alloy
                bar b (right top) (left top) 0.85 alloy
            let knee,tip = v x h z, v (x+dx) (h+8.) -132
            bar b knee tip 2.3 paint
            bar b (knee+v -3 10 0) tip 1.1 alloy
            bar b (knee+v -3 10 0) (knee-v 0 7 0) 1.2 paint
            bar b tip (tip-v 0 26 0) 0.65 steel
            b.Box(tip-v 0 26 0,v 2.9 1.7 2.8,paint)
            box b x (h-3.) (z+3.) 6 3.2 5.8 steel
            windows b x (h-3.) (z+9.) 4 2.4 ice
        for x,y,z in [72.,30.,-110.; 101.,-5.,-117.; -74.,24.,-99.] do
            box b x y z 2.1 2.0 1.9 weld
            for dx,dy in [-3.,5.;4.,-3.;1.2,-5.] do
                bar b (v x y z) (v (x+dx) (y+dy) (z+2.0)) 0.40 lamp
        deck b 0 -93 [|-32.,20.,12.; -25.,21.,12.; -10.,14.,9.|] alloy
        windows b 0 -20 -81.5 8 3.2 ice

        // Dock 07's overhead transfer crane is tall enough to raise a finished hull.
        // Keep its loading bay and the construction frames open below the girder.
        let blackened = MeshColor.metal 0.22 0.27 0.29
        let rail = MeshColor.metal 0.36 0.38 0.39
        let floodlight = MeshColor.glow 1.0 0.62 0.26 0.82
        for side in [-1.; 1.] do
            let x = side * 163.0
            deck b x -218. [|-49.,18.,21.; -34.,19.,23.; -11.,15.,19.; 126.,12.,17.; 148.,16.,19.|] blackened
            deck b x -218. [|-38.,20.,24.; -32.,20.,24.; -27.,17.,21.|] paint
            deck b x -218. [|117.,14.,20.; 124.,17.,22.; 130.,17.,21.|] paint
            for y in [5.; 37.; 69.; 101.] do
                box b x y -199.5 10.5 1.35 1.1 rail
                box b (x - 7.0) (y + 10.0) -200.4 1.1 5.2 0.8 lamp
                box b (x + 7.0) (y + 10.0) -200.4 1.1 5.2 0.8 lamp
            bar b (v x -34. -237.) (v x 144. -253.) 3.2 steel
            bar b (v x 8. -234.) (v x 144. -253.) 1.2 paint
            bar b (v x 32. -203.) (v x 124. -202.) 1.8 paint
        deck b 0. -218. [|134.,151.,14.; 138.,165.,18.; 149.,165.,18.; 157.,152.,15.|] steel
        bar b (v -162. 145. -253.) (v 162. 145. -253.) 2.6 blackened
        for bay in -7..7 do
            let x = float bay * 21.0
            let p, q = v (x - 9.5) 139. -199., v (x + 9.5) 149.5 -199.
            let start, finish = if bay % 2 = 0 then p, q else v p.X q.Y p.Z, v q.X p.Y q.Z
            bar b start finish 1.15 paint
            bar b (v x 149. -234.) (v x 145. -253.) 1.4 rail
            box b x 134. -205. 5.3 1.65 1.75 blackened
            box b x 133. -202.7 3.6 0.65 0.5 floodlight
        for z in [-210.; -225.] do
            bar b (v -153. 133. z) (v 153. 133. z) 1.3 rail
        deck b -18. -218. [|116.,15.,9.; 123.,20.,11.; 134.,20.,14.|] paint
        windows b -18. 124. -206.6 7 3.2 ice
        bar b (v -22. 117. -216.) (v -22. 91. -216.) 0.7 rail
        bar b (v -14. 117. -216.) (v -14. 91. -216.) 0.7 rail
        box b -18. 90. -216. 6.0 1.65 3.4 paint

    // Wing coordinates are shared with the oriented collision boxes.
    let internal thetaSolarSections = [| 40., 82.; 92., 134.; 144., 188. |]
    let internal thetaSolarHalfHeight = 24.0
    let internal thetaSolarFrame side =
        let root = v (side * 22.0) -23. -155.
        let span = Vec3.normalize (v side -0.34 -0.30)
        let upright = v 0. 1. -0.35
        let chord = Vec3.normalize (upright - span * Vec3.dot upright span)
        let face = Vec3.cross (span * side) chord
        root, span, chord, face

    let private theta (b: MeshBuilder) =
        let cobalt = rgb 0.07 0.115 0.20
        let ceramic = rgb 0.40 0.43 0.44
        let signal = glow 0.41 0.68 1.0
        // The relay still runs on RT-9's analogue path: amber tube displays behind the crew windows.
        let phosphor = glow 1.0 0.62 0.18
        let dish (hub: Vec3) (direction: Vec3) (radius: float) =
            let axis = Vec3.normalize direction
            let u = Vec3.normalize (Vec3.cross Vec3.up axis)
            let w = Vec3.cross axis u
            let at (t: float) (a: float) = hub + (u*cos a + w*sin a)*(radius*t) + axis*(radius*0.40*t*t)
            let slices = 24
            let angle i = float i*2.0*Math.PI/float slices
            let heel = hub - axis*(0.23*radius)
            // Separate ceramic petals on a graphite backing. Both the concave face
            // and the exposed ribs on the far dish are visible from the buoy lane.
            for i in 0..slices-1 do
                let a,d = angle i,angle (i+1)
                for tier in 0..2 do
                    let r0,r1 = float tier/3.,float (tier+1)/3.
                    let quad t0 t1 a0 a1 = [|at t0 a0; at t1 a0; at t1 a1; at t0 a1|]
                    let basePlate = quad r0 r1 a d
                    b.PolygonOutward(basePlate,hub+axis*(radius*2.),steel)
                    b.PolygonOutward(basePlate,hub-axis*(radius*2.),iron)
                    let face = quad (r0+0.009) (r1-0.010) (a+0.009) (d-0.009) |> Array.map (fun p -> p+axis*0.27)
                    let tile = if (i+tier)%5=0 then alloy else ceramic
                    b.PolygonOutward(face,hub-axis*(radius*2.),tile)
                bar b (at 1. a) (at 1. d) 1.02 alloy
                if i%3=0 then
                    bar b heel (at 0.68 a-axis*2.) 1.3 cobalt
                    bar b (at 0.68 a-axis*2.) (at 1. a) 1.05 steel
                if i%8=0 then bar b (at 0.95 a) (hub+axis*(radius*0.78)) 0.95 iron
            bar b (hub+axis*(radius*0.70)) (hub+axis*(radius*0.88)) 2.1 cobalt
            b.Box(hub+axis*(radius*0.89),v 1.45 1.45 1.45,signal)
            heel
        let left = dish (v -75 55 -136) (v -0.48 0.63 0.64) 61.0
        let right = dish (v 88 27 -123) (v 0.84 0.33 0.35) 40.0
        deck b 0 -124 [|-32.,25.,19.; -18.,34.,25.; -5.,30.,22.; 1.,21.,18.|] cobalt
        windows b 0 -12 -99.1 13 3.5 phosphor
        deck b 12 -150 [|-17.,13.,11.; -6.,14.,12.; -2.,11.,10.|] steel
        for (hub: Vec3), x in [left,-65.;right,79.] do
            drum b 12 x -133 [|-26.,11.; -21.,14.; -15.,10.|] alloy
            for dx in [-8.;8.] do
                bar b (v (x+dx) -20 -134) hub 2.4 steel
                bar b (v (x+dx) -21 -130) (v 0 -21 -125) 2.1 ceramic
            bar b (v x -18 -130) (v 0 -5 -121) 1.5 cobalt
        // Tapered interferometer mast with crossarms, guy wires and beacon.
        let mast leg y =
            let r = 10.0 - 8.4 * ((y+6.)/120.)
            let a = float leg * 2.0 * Math.PI / 3.0
            v (12.+r*cos a) y (-152.+r*sin a)
        for leg in 0..2 do
            bar b (mast leg -6) (mast leg 114) 1.25 alloy
            for i in 0..7 do
                let y = float i*15.-6.
                bar b (mast leg y) (mast ((leg+1)%3) (y+15.)) 0.75 cobalt
        for y,w in [82.,15.;97.,9.] do
            bar b (v (12.-w) y -152) (v (12.+w) y -152) 0.92 alloy
            for dx in [-w;w] do bar b (v (12.+dx) (y-2.) -152) (v (12.+dx) (y+3.) -152) 0.65 signal
        bar b (v 12 113 -152) (v 12 140 -152) 0.85 alloy
        box b 12 140 -152 1.55 1.8 1.55 red

        // Independent solar banks descend behind the dishes. Their dark gaps
        // expose the panel mounts and let the two antenna silhouettes read.
        let frame = MeshColor.metal 0.20 0.24 0.32
        let backing = rgb 0.05 0.075 0.13
        let cellBlue = MeshColor.glass 0.10 0.17 0.36 0.08
        let cellIndigo = MeshColor.glass 0.06 0.11 0.27 0.06
        for side in [-1.; 1.] do
            let root, span, chord, face = thetaSolarFrame side
            let at along height depth = root + span * along + chord * height + face * depth
            let plate a d low high depth =
                [| at a low depth; at d low depth; at d high depth; at a high depth |]
            bar b (v 0. -21. -143.) root 3.8 cobalt
            bar b root (at 191. 0. -0.8) 3.3 frame
            for a,d in thetaSolarSections do
                let h = thetaSolarHalfHeight
                let centre = 0.5 * (a + d)
                b.Slab(plate a d -h h 0.0, [| 1.45; 1.45; 1.45; 1.45 |], face, backing)
                for y in [-h; h] do bar b (at a y 0.) (at d y 0.) 1.7 ceramic
                for edge in [a; d] do
                    bar b (at edge -h 0.) (at edge h 0.) 1.6 frame
                    bar b (at edge -19. 1.7) (at edge -14. 1.7) 0.63 signal
                for col in 0..6 do
                    for row in 0..5 do
                        let pitch = (d - a - 4.0) / 7.0
                        let x = a + 2.0 + float col * pitch
                        let y = -21.0 + float row * 7.0
                        let outline = plate (x + 0.34) (x + pitch - 0.34) (y + 0.28) (y + 6.72) 1.61
                        let cell = if (row + col) % 4 = 0 then cellBlue else cellIndigo
                        b.PolygonOutward(outline, at centre 0. 0., cell)
                bar b (at centre 0. -5.1) (at (a + 1.0) (-h + 1.0) -1.5) 1.15 frame
                bar b (at centre 0. -5.1) (at (d - 1.0) (h - 1.0) -1.5) 1.15 frame
                b.Box(at centre 0. -3.6, v 4.7 3.1 3.4, cobalt)
            b.Box(at 189. 0. 0., v 1.9 1.55 1.55, red)

    let private morgenlicht (b: MeshBuilder) =
        // Vance's seat matches his fleet: cold platinum over a black keel, gold on the edges, carmine
        // heraldry and cold light behind every slit. Nobody here rations power.
        let ivory = MeshColor.glow 0.55 0.56 0.55 0.12
        let gold = MeshColor.glow 0.60 0.385 0.085 0.10
        let umber = rgb 0.045 0.05 0.06
        let crimson = rgb 0.34 0.016 0.024
        let slit = glow 0.72 0.88 1.0
        let windows (b: MeshBuilder) x y z count pitch color = windowBand b 0 x y z count pitch color
        // An armoured flying citadel: upswept flanks, deep central keel and a
        // stepped keep. The flanks are readable by silhouette as well as heraldry.
        deck b 0 -131 [|-59.,13.,9.; -33.,42.,25.; -14.,70.,37.; 8.,77.,40.; 22.,63.,34.|] umber
        deck b 0 -131 [|-11.,68.,37.; 5.,77.,41.; 16.,72.,39.|] ivory
        deck b 0 -131 [|8.,77.,41.; 11.,76.5,41.|] gold
        let plate side footprint depth thickness color =
            let outline = footprint |> Array.map (fun (x,y) -> v (side*x) y depth)
            ProceduralMeshes.armorPanel b outline Vec3.unitZ thickness color umber
        for side in [-1.;1.] do
            let wing = [|30.,-17.; 72.,-7.; 113.,4.; 152.,40.; 139.,44.; 69.,26.; 30.,20.|]
            let wingFace z = wing |> Array.map (fun (x,y) -> v (side*x) y z)
            b.Loft([|wingFace -159.;wingFace -117.|],true,true,umber)
            plate side wing -116.8 2.0 gold
            plate side [|40.,-8.; 75.,1.; 109.,10.; 135.,34.; 76.,20.; 40.,17.|] -114.6 1.8 ivory
            plate side [|51.,-3.; 82.,4.; 119.,21.; 105.,9.; 72.,-1.|] -112.6 0.85 crimson
            // Layered outriggers carry a pair of twin-barrel defence turrets per flank.
            for x,z,y in [102.,-133.,28.; 57.,-153.,42.] do
                let cx = side*x
                deck b cx z [|y-24.,12.,14.; y-11.,15.,15.; y-2.,12.,12.|] ivory
                drum b 12 cx z [|y-3.,10.8; y,11.4; y+2.,10.1|] gold
                deck b cx z [|y+1.,9.,8.; y+8.,10.,9.; y+13.,6.4,6.4|] steel
                for dz in [-3.;3.] do
                    let start = v (cx+side*5.) (y+9.) (z+dz)
                    let tip = start + v (side*22.) 7 8
                    bar b start tip 1.35 umber
                    bar b (Vec3.lerp start tip 0.77) tip 1.85 gold
            for i in 0..4 do
                box b (side*(46.+float i*9.0)) (8.+float i*1.8) -111.8 1.6 0.88 0.45 slit
            // Overlapping pectoral plates follow the slope of the wing.
            for i in 0..2 do
                let x,y = 71.+float i*15.,5.+float i*5.6
                plate side [|x,y; x+11.,y+3.; x+17.,y+10.; x+4.,y+7.|] -112.5 1.2 ivory
            for i in 0..3 do
                let x = side*(17.+float i*10.5)
                windows b x -0.8 -90.0 3 2.5 slit
        deck b 0 -137 [|14.,42.,27.; 32.,38.,25.; 39.,28.,20.; 57.,24.,17.|] ivory
        deck b 0 -137 [|32.,39.,26.; 36.,37.,25.|] gold
        windows b 0 28 -111.4 13 3.7 slit
        deck b 0 -141 [|54.,26.,19.; 61.,28.,21.; 68.,18.,15.; 85.,15.,13.|] umber
        deck b 0 -141 [|65.,20.,16.; 73.,18.,15.; 88.,12.,11.|] ivory
        windows b 0 59 -116.8 10 3.0 slit
        inspectionHead b 0. 56. -122.
        for side in [-1.;1.] do
            box b (side*9.) 45 -114 5.7 1.3 6.4 gold
            plate side [|4.,48.; 14.,47.; 13.,17.; 8.,10.; 4.,16.|] -108.5 0.9 crimson
            plate side [|5.,41.; 12.,43.; 9.,35.; 6.,35.|] -107.4 0.5 gold
        // The two golden vanes frame a narrow illuminated slot above the flag deck.
        for side in [-1.;1.] do
            plate side [|1.5,82.; 13.,86.; 17.,103.; 5.,96.; 1.5,119.|] -136.0 2.5 gold
        bar b (v 0 82 -140) (v 0 110 -140) 1.2 slit
        deck b 0 -96 [|-28.,12.,12.; -22.,15.,14.; -11.,13.,12.|] steel
        windows b 0 -18 -82.7 7 3.1 slit

        // Armoured bastions rise from the ends of both flanks. Their stepped
        // faces leave the command keep isolated in the centre of the silhouette.
        let brushedGold = MeshColor.metal 0.50 0.34 0.12
        let armour = MeshColor.metal 0.28 0.29 0.31
        for side in [-1.; 1.] do
            let x = side * 145.0
            deck b x -147. [|22.,15.,15.; 39.,24.,23.; 58.,22.,24.; 64.,20.,21.|] umber
            deck b x -147. [|48.,23.,25.; 61.,24.,26.; 64.,22.,24.|] brushedGold
            deck b x -149. [|59.,19.,20.; 69.,21.,23.; 98.,17.,21.; 117.,13.,17.|] ivory
            deck b x -151. [|95.,17.,23.; 102.,20.,23.; 108.,19.,22.|] armour
            for dx in [-10.; 10.] do
                deck b (x+dx) -151. [|105.,5.,12.; 116.,6.,12.; 133.,3.5,8.|] gold
                box b (x+dx) 122. -140.2 1.65 3.1 0.95 slit
            // Thick frames and shutter slats give the fleet-facing walls depth.
            plate side [|128.,76.; 161.,76.; 159.,99.; 131.,103.|] -125.6 1.5 umber
            plate side [|133.,78.; 157.,78.; 156.,96.; 134.,98.|] -124.0 0.9 crimson
            for row in 0..4 do
                let y = 80. + float row * 3.5
                bar b (v (side * 134.) y -122.7) (v (side * 156.) (y + 1.4) -122.7) 0.95 armour
            for edge in [-1.; 1.] do
                let rimX = x + edge * 16.0
                bar b (v rimX 74. -123.) (v (x + edge * 12.0) 104. -125.) 1.1 brushedGold
            plate side [|149.,24.; 171.,41.; 164.,63.; 155.,50.|] -122.8 2.1 ivory
            plate side [|152.,29.; 167.,42.; 162.,54.; 155.,47.|] -120.0 0.85 crimson
            windows b x 55. -120.3 8 3.4 slit
            let foot, shoulder = v (side * 51.) -30. -151., v (side * 155.) 32. -151.
            bar b foot shoulder 4.1 umber
            bar b (foot + v 0. 1. -5.) (shoulder + v 0. 1. -5.) 2.1 brushedGold

    /// Rearward clearing volume supplements the smaller sphere around the berth.
    let worksCenter = v 0 22 -111
    let worksRadius kind =
        match kind with
        | MiningDepot -> 204.
        | Refinery -> 186.
        | FrontierBazaar -> 192.
        | OrbitalYard -> 262.
        | ResearchRelay -> 251.
        | FleetDock -> 209.

    let station kind =
        let b = MeshBuilder()
        dock b
        match kind with
        | MiningDepot -> kestrel b
        | Refinery -> helios b
        | FrontierBazaar -> drift b
        | OrbitalYard -> kepler b
        | ResearchRelay -> theta b
        | FleetDock -> morgenlicht b
        b.Build()
