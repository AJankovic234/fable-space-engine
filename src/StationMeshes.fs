namespace SpaceEngine

open System

/// Stations are built in metres, relative to the docking target. World +Z faces traffic.
[<RequireQualifiedAccess>]
module StationMeshes =
    let private v x y z = Vec3(x,y,z)
    let private rgb = MeshColor.rgb
    let private glow r g b = MeshColor.glow r g b 0.92
    let private steel = rgb 0.28 0.36 0.41
    let private alloy = rgb 0.67 0.72 0.71
    let private iron = rgb 0.105 0.15 0.17
    let private ochre = rgb 0.96 0.48 0.065
    let private ice = glow 0.34 0.85 1.0
    let private lamp = glow 1.0 0.71 0.27
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

    let private windows (b: MeshBuilder) x y z count pitch color =
        let width = float count * pitch * 0.5
        box b x y z width 1.45 0.4 iron
        for i in 0 .. count-1 do
            if i % 7 <> 2 then
                box b (x-width + (float i+0.5)*pitch) y (z+0.5) (pitch*0.30) 0.71 0.2 color

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

    let private kestrel (b: MeshBuilder) =
        let rust = rgb 0.65 0.235 0.063
        let rock = v -116 38 -113
        // Exposed ore strata on the fractured quarry face. The parent rock still
        // shares its irregular silhouette with the field asteroids.
        let stone = (ProceduralMeshes.asteroid 271 2).Vertices
        let vertex i = v (float stone.[i]) (float stone.[i+1]) (float stone.[i+2])
        let strata (p: Vec3) =
            let band = sin (p.Y*12. - p.X*5.5 + 1.4*sin (p.Z*4.3))
            if band > 0.82 then rgb 0.76 0.58 0.35
            elif band < -0.55 then rgb 0.27 0.28 0.31
            else rgb 0.51 0.47 0.42
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
            deck b p.X p.Z [|p.Y+3.,2.7,2.3; p.Y+5.,2.3,1.9; p.Y+6.5,1.0,1.0|] (rgb 0.40 0.33 0.26)
        // Ballast hoist and caged control cab above the mine head.
        bar b (v -38 12 -122) (v -48 56 -124) 2.0 ochre
        bar b (v -18 18 -124) (v -48 56 -124) 1.1 alloy
        bar b (v -48 56 -124) (v -78 70 -119) 2.3 ochre
        bar b (v -78 70 -119) (v -80 46 -119) 0.66 alloy
        box b -48 53 -122 6 3 5 steel
        windows b -48 53 -116.8 4 2.5 ice

    let private helios (b: MeshBuilder) =
        let oxide = rgb 0.53 0.19 0.085
        let copper = rgb 0.83 0.39 0.15
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
        for a,c in [v -77 28 -136,v -11 50 -159; v -11 51 -159,v 61 35 -131] do
            bar b a c 2.15 steel
            bar b (a+v 0 3 0) (c+v 0 3 0) 0.62 copper
        for x in [-79.;-47.;-15.;17.;49.;81.] do
            box b x -21 -89.6 10 1.7 0.8 fire

    let private drift (b: MeshBuilder) =
        let jade = rgb 0.095 0.57 0.52
        let wine = rgb 0.56 0.16 0.25
        let enamel = rgb 0.89 0.78 0.56
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
        houseboat (v 53 52 -158) -2.94 53.0 10.0 (rgb 0.34 0.41 0.60) signs.[3]
        // Pontoon with stacked customs containers, corrugated end doors and shop signs.
        deck b 0 -124 [|-24.,22.,22.; -15.,27.,26.; 11.,24.,22.|] steel
        let colors = [|jade; ochre; wine; enamel; rgb 0.22 0.38 0.55|]
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

    let private kepler (b: MeshBuilder) =
        let paint = rgb 0.98 0.53 0.08
        let primer = rgb 0.50 0.225 0.115
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

    let private theta (b: MeshBuilder) =
        let cobalt = rgb 0.19 0.32 0.54
        let ceramic = rgb 0.83 0.88 0.91
        let signal = glow 0.41 0.68 1.0
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
        windows b 0 -12 -99.1 13 3.5 ice
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

    let private morgenlicht (b: MeshBuilder) =
        let ivory = MeshColor.glow 0.95 0.86 0.67 0.22
        let gold = MeshColor.glow 0.96 0.64 0.20 0.17
        let umber = rgb 0.30 0.17 0.15
        let crimson = rgb 0.48 0.08 0.12
        let slit = glow 1.0 0.80 0.45
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

    /// Rearward clearing volume supplements the smaller sphere around the berth.
    let worksCenter = v 0 22 -111
    let worksRadius kind =
        match kind with
        | MiningDepot -> 178.
        | Refinery -> 143.
        | FrontierBazaar -> 154.
        | OrbitalYard -> 212.
        | ResearchRelay -> 157.
        | FleetDock -> 163.

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
