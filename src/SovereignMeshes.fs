namespace SpaceEngine

open System

/// AETHERs goldenes Flaggschiff vor Morgenlicht. Alle Maße in Metern, Bug +X.
[<RequireQualifiedAccess>]
module SovereignMeshes =
    let private graphite = MeshColor.rgb 0.075 0.080 0.102
    let private gunmetal = MeshColor.rgb 0.17 0.17 0.19
    // Old, heavy gold over black: the only warm hull in a cold fleet, and it belongs to one man.
    let private bronze = MeshColor.rgb 0.30 0.165 0.055
    let private gold = MeshColor.rgb 0.64 0.40 0.095
    let private beatenGold = MeshColor.rgb 0.48 0.275 0.065
    let private paleGold = MeshColor.rgb 0.78 0.56 0.21
    let private platinum = MeshColor.rgb 0.52 0.52 0.50
    let private garnet = MeshColor.rgb 0.34 0.038 0.047
    let private amber = MeshColor.glow 1.0 0.56 0.17 0.84
    let private ivory = MeshColor.glow 1.0 0.86 0.55 0.85
    let private signal = MeshColor.glow 1.0 0.115 0.045 1.0
    let private ion = MeshColor.glow 0.43 0.81 1.0 1.0

    // X, Z-Mitte, halbe Breite, Unterkante, Oberkante. Dieselben Spanten tragen
    // die Fenster und Einlegearbeiten; die Linien folgen dadurch auch den Schrägen.
    let private spine =
        [| -560., 0., 54., -51., 40.
           -473., 0., 98., -68., 58.
           -324., 0., 114., -77., 65.
           -128., 0., 126., -70., 70.
           54., 0., 119., -56., 60.
           241., 0., 98., -47., 44.
           425., 0., 72., -32., 31.
           574., 0., 38., -14., 21.
           752., 0., 1.4, 7., 12. |]

    let private wing side =
        [| -587., 196., 26., -35., 21.
           -502., 191., 37., -39., 31.
           -370., 166., 41., -40., 29.
           -210., 135., 44., -36., 24.
           -62., 115., 34., -26., 17.
           112., 93., 22., -19., 10.
           239., 63., 2., -12., -4. |]
        |> Array.map (fun (x,z,w,lo,hi) -> x,z*side,w,lo,hi)

    let private contour =
        [| -0.39,1.; 0.39,1.; 0.85,0.65; 1.,0.19; 1.,-0.22; 0.75,-0.69
           0.37,-1.; -0.37,-1.; -0.75,-0.69; -1.,-0.22; -1.,0.19; -0.85,0.65 |]

    let private mix (a: float) b t = a + (b-a)*t

    let private rib (x,z,w,lo,hi) inset =
        let y = (hi+lo)*0.5
        contour |> Array.map (fun (u,v) -> Vec3(x,y+v*((hi-lo)*0.5+inset),z+u*(w+inset)))

    let private between (x0,z0,w0,l0,h0) (x1,z1,w1,l1,h1) t =
        mix x0 x1 t, mix z0 z1 t, mix w0 w1 t, mix l0 l1 t, mix h0 h1 t

    let private loft (b: MeshBuilder) sections color =
        b.Loft(sections |> Array.map (fun frame -> rib frame 0.), true, true, color)

    // A blade of gilt armour is separated from its neighbour by a narrow dark reveal.
    // Each applique is cut from the same pair of linearly interpolated pressure frames.
    let private clad (b: MeshBuilder) frames isWing =
        loft b frames graphite
        for startFrame,endFrame in Array.pairwise frames do
            let xa,za,_,la,ha = startFrame
            let xb,zb,_,lb,hb = endFrame
            let inside = Vec3((xa+xb)*0.5,(la+ha+lb+hb)*0.25,(za+zb)*0.5)
            let span = xb-xa
            let point f t u outset =
                let ring = rib (between startFrame endFrame t) outset
                let a,c = ring.[f], ring.[(f+1)%contour.Length]
                a+(c-a)*u
            let patch face outset (t0,u0) (t1,u1) (t2,u2) (t3,u3) color =
                let p = point face
                b.PolygonOutward([|p t0 u0 outset; p t1 u1 outset; p t2 u2 outset; p t3 u3 outset|], inside, color)
            let tiles = max 1 (int (Math.Round(span/65.)))
            for tile in 0 .. tiles-1 do
                let a,c = (float tile + 0.025)/float tiles, (float (tile+1) - 0.025)/float tiles
                let cut = (c-a)*0.11
                let shade = if tile%2=0 then gold else beatenGold
                for face in [|0;1;2;4;5;6;7;8;10;11|] do
                    let color =
                        match face with
                        | 0 | 1 | 11 -> if isWing then paleGold else shade
                        | 2 | 10 -> if isWing then beatenGold else gold
                        | 4 | 8 -> if isWing then bronze else beatenGold
                        | _ -> bronze
                    let sweep = if face>=6 then a+cut,c,a,c-cut else a,c-cut,a+cut,c
                    let p,q,r,s = sweep
                    patch face 1.2 (p,0.025) (q,0.025) (s,0.975) (r,0.975) color
                for face in [|2;10|] do
                    let u = if face=2 then 0.79 else 0.16
                    patch face 1.85 (a+cut,u) (c-cut,u) (c-cut,u+0.05) (a+cut,u+0.05) platinum

            // A double passenger-deck light band. Dark bulkheads interrupt each run of lights.
            let bays = max 1 (int (span / (if isWing then 14. else 10.)))
            for face in [|3;9|] do
                patch face 0.7 (0.,0.06) (1.,0.06) (1.,0.14) (0.,0.14) paleGold
                patch face 0.7 (0.,0.86) (1.,0.86) (1.,0.94) (0.,0.94) bronze
                for j in 0 .. bays-1 do
                    let t = (float j+0.18)/float bays
                    let c = (float j+0.78)/float bays
                    for row in 0..1 do
                        if (j+row)%5<>0 then
                            let u = 0.26+0.36*float row
                            patch face 1.3 (t,u) (c,u) (c,u+0.12) (t,u+0.12) (if (j+row)%3=0 then amber else ivory)

    // Triebwerksmund (hinten bei x), außen Metallsegmente, innen ein vertiefter Ionenring.
    let private bell (b: MeshBuilder) x y z radius =
        let hoop dx scale =
            Array.init 16 (fun i ->
                let a = float i * Math.PI / 8.
                Vec3(x+dx*radius,y+sin a*scale*radius,z+cos a*scale*radius))
        let lip,inner,throat = hoop 0. 1.,hoop 0. 0.82,hoop 0.45 0.59
        b.Loft([|lip; hoop 0.38 0.98; hoop 1.2 0.72; hoop 2.6 0.95|],false,true,gunmetal)
        for j in 0..15 do
            let k = (j+1)%16
            let behind = Vec3(x+radius,y,z)
            b.PolygonOutward([|lip.[j]; lip.[k]; inner.[k]; inner.[j]|],behind,(if j%4=0 then platinum else bronze))
            let outside = Vec3(x-radius, y+(lip.[j].Y-y)*2., z+(lip.[j].Z-z)*2.)
            b.PolygonOutward([|inner.[j]; inner.[k]; throat.[k]; throat.[j]|],outside,MeshColor.glow 0.07 0.32 0.64 0.66)
        b.PolygonOutward(throat,Vec3(x+radius,y,z),ion)
        b.Loft([|hoop 0.30 0.26; hoop 0.45 0.36|],true,false,gunmetal)

    let private rudder (b: MeshBuilder) side =
        let v x y z = Vec3(x,y,z*side)
        let outline = [|v -558. 17. 195.; v -494. 25. 189.; v -515. 78. 199.; v -620. 123. 221.|]
        b.Slab(outline,[|8.;8.;5.;1.4|],Vec3.unitZ,beatenGold)
        for surface in [-1.;1.] do
            let p x y z w = v x y (z+surface*w)
            let flash = [|p -550. 30. 196. 9.; p -521. 36. 195. 8.; p -534. 68. 201. 5.7; p -612. 116. 219. 2.4|]
            b.PolygonOutward(flash,v -528. 48. 197.,paleGold)

    let hull () : MeshData =
        let b = MeshBuilder()
        clad b spine false
        for side in [-1.;1.] do
            clad b (wing side) true
            rudder b side
            bell b -636. -7. (side*196.) 25.
            bell b -602. 4. (side*27.) 21.
        bell b -595. -31. 0. 16.
        // The keel projects under the gallery and gives the underside its own stepped silhouette.
        loft b [|-493.,0.,25.,-69.,-49.; -335.,0.,47.,-100.,-56.; -144.,0.,43.,-112.,-62.
                 85.,0.,32.,-76.,-48.; 425.,0.,1.5,-29.,-20.|] gunmetal
        loft b [|-329.,0.,13.,-101.,-92.; -144.,0.,18.,-120.,-102.; 83.,0.,3.,-77.,-73.|] paleGold
        b.Build()

    // A thin octagonal terrace in the XZ plane, clipped at all four corners.
    let private terraceRing x y halfX halfZ =
        [|for u,v in [|-1.,-0.51; -0.81,-1.; 0.72,-1.; 1.,-0.40; 1.,0.40; 0.72,1.; -0.81,1.; -1.,0.51|] ->
              Vec3(x+u*halfX,y,v*halfZ)|]

    let private terrace (b: MeshBuilder) x y halfX halfZ height =
        let ring dy s = terraceRing x (y+dy) (halfX*s) (halfZ*s)
        b.Loft([|ring 0. 0.98; ring (height*0.24) 1.; ring (height*0.76) 0.97|],true,false,gunmetal)
        b.Loft([|ring (height*0.74) 1.01; ring height 0.95|],false,true,gold)
        for side in [-1.;1.] do
            for i in -7..7 do
                let dx = float i * halfX*0.087
                b.Box(Vec3(x+dx,y+height*0.48,halfZ*0.992*side),Vec3(halfX*0.026,height*0.10,0.75),ivory)

    // The open solar crown above Vance's bridge. Eight bevelled segments frame a
    // suspended platinum lozenge, so the star field is visible through the bridge.
    let private crown (b: MeshBuilder) =
        let polygon =
            [| -354.,115.; -299.,181.; -218.,249.; -161.,210.
               -85.,136.; -151.,102.; -226.,87.; -304.,95. |]
        let cx,cy = -222.,142.
        let ring scale z = polygon |> Array.map (fun (x,y) -> Vec3(mix cx x scale,mix cy y scale,z))
        let outer = ring 1. 0.
        let hole = ring 0.74 0.
        for i in 0..polygon.Length-1 do
            let j = (i+1)%polygon.Length
            let mid = (outer.[i]+outer.[j]+hole.[i]+hole.[j])*0.25
            let face (a:Vec3[]) (c:Vec3[]) color =
                b.PolygonOutward([|a.[i];a.[j];c.[j];c.[i]|],mid,color)
            for side in [-1.;1.] do
                let goldEdge,step,inlay,bore = ring 0.96 (11.*side),ring 0.91 (13.*side),ring 0.82 (13.*side),ring 0.74 (7.*side)
                face outer goldEdge paleGold
                face goldEdge step gold
                face step inlay (if i=1 || i=2 then platinum else beatenGold)
                face inlay bore paleGold
            face (ring 0.74 -7.) (ring 0.74 7.) bronze
        // A tiny point of light hangs in the empty volume, visible on both flanks.
        let at y xw zw = terraceRing -218. y xw zw
        b.Loft([|at 165. 0.9 0.8; at 189. 11. 6.; at 205. 2. 2.; at 222. 1.4 1.4|],true,true,platinum)
        b.Loft([|at 186. 9. 6.2; at 190. 11.8 7.|],true,true,ivory)

    let beacons : (Vec3 * MeshColor * float)[] =
        [| Vec3(751.,11.,0.), ivory, 8.
           Vec3(-218.,250.,0.), amber, 10.
           Vec3(-620.,123.,221.), signal, 7.
           Vec3(-620.,123.,-221.), signal, 7.
           Vec3(-144.,-120.,0.), amber, 7.
           Vec3(-637.,-7.,196.), ion, 22.
           Vec3(-637.,-7.,-196.), ion, 22.
           Vec3(-603.,4.,27.), ion, 15.
           Vec3(-603.,4.,-27.), ion, 15. |]

    let regalia () : MeshData =
        let b = MeshBuilder()
        terrace b -171. 49. 248. 86. 23.
        terrace b -200. 72. 180. 65. 24.
        terrace b -224. 96. 119. 44. 22.
        crown b
        let beam = ProceduralMeshes.beam b
        // Four flying buttresses join the crown to the bridge's outboard galleries.
        for side in [-1.;1.] do
            beam (Vec3(-341.,72.,68.*side)) (Vec3(-295.,170.,8.*side)) 4.3 platinum
            beam (Vec3(-126.,76.,58.*side)) (Vec3(-111.,144.,8.*side)) 4.0 gold
        // Lacquered dorsal spine framed by pale metal; its prow tapers to a fine point.
        loft b [| -93.,0.,30.,59.,78.; 1.,0.,32.,57.,81.; 214.,0.,22.,41.,63.
                  424.,0.,14.,28.,46.; 584.,0.,5.,17.,27.; 735.,0.,1.,9.,12. |] platinum
        loft b [| -75.,0.,18.,73.,81.; 0.,0.,22.,75.,83.; 214.,0.,14.,58.,65.
                  424.,0.,8.,42.,48.; 645.,0.,0.7,17.,20. |] garnet
        // Seven gilded buttresses along each side of the leading deck.
        for i in 0..6 do
            let x = 50.+float i*49.
            let top = if x < 241. then mix 60. 44. ((x-54.)/187.) else mix 44. 31. ((x-241.)/184.)
            let width = if x < 241. then mix 119. 98. ((x-54.)/187.) else mix 98. 72. ((x-241.)/184.)
            for side in [-1.;1.] do
                let pt xx yy zz = Vec3(xx,yy,zz*side)
                let arc = [|pt x (top+6.) (width*0.24); pt (x+11.) (top+5.) (width*0.24)
                            pt (x-14.) (top-9.) (width*0.67); pt (x-26.) (top-7.) (width*0.67)|]
                b.Slab(arc,[|1.3;1.3;1.9;1.9|],Vec3.unitY,paleGold)
        for pos,color,r in beacons do
            if r<=10. then b.Box(pos,Vec3(1.6,1.2,1.6),color)
        b.Build()
