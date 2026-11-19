namespace SpaceEngine

open System

/// The inspection point is the local origin. Leave room there for a ship to stop.
[<RequireQualifiedAccess>]
module OuterSiteMeshes =
    let private v x y z = Vec3(x, y, z)
    let private tau = 2.0 * Math.PI
    let private iron = MeshColor.rgb 0.105 0.132 0.15
    let private bare = MeshColor.metal 0.43 0.47 0.46
    let private oxide = MeshColor.rgb 0.40 0.14 0.058
    let private ochre = MeshColor.rgb 0.66 0.34 0.055
    let private ivory = MeshColor.rgb 0.56 0.57 0.50
    let private soot = MeshColor.rubber 0.038 0.048 0.06
    let private tile = MeshColor.rgb 0.09 0.19 0.34
    let private basalt = MeshColor.metal 0.125 0.20 0.25
    let private nacre = MeshColor.metal 0.40 0.53 0.59
    let private ice = MeshColor.glass 0.36 0.67 0.83 0.16
    let private amber = MeshColor.glow 1.30 0.50 0.10 0.96
    let private aqua = MeshColor.glow 0.19 1.05 1.35 0.98
    let private violet = MeshColor.glow 0.75 0.40 1.45 0.98
    let private green = MeshColor.glow 0.29 1.22 0.68 0.96
    let private red = MeshColor.glow 1.18 0.10 0.035 0.93
    let private beam = ProceduralMeshes.beam
    let private ring = ProceduralMeshes.ring
    let private rotate axis angle = Quat.fromAxisAngle axis angle
    let private build f =
        let b = MeshBuilder()
        f b
        b.Build()

    let private box (b: MeshBuilder) x y z hx hy hz color =
        b.Box(v x y z, v hx hy hz, color)

    let private rim b (points: Vec3[]) width color =
        for i in 0 .. points.Length - 1 do
            beam b points.[i] points.[(i+1) % points.Length] width color

    let private hull (b: MeshBuilder) (at: Vec3) sections color =
        b.Loft(sections |> Array.map (fun (z, width, low, high) ->
            ProceduralMeshes.hullSection at.X (at.Z + z) width (at.Y + low) (at.Y + high)), true, true, color)

    let private spindle (b: MeshBuilder) (at: Vec3) n sections color =
        let profile (y, r) =
            Array.init n (fun i ->
                let a = tau * float i / float n
                at + v (r * cos a) y (r * sin a))
        b.Loft(Array.map profile sections, true, true, color)

    let private panelWing b (p: Vec3) side n width length =
        beam b p (p + v (side * (float n * width + 4.0)) 0.0 0.0) 1.1 bare
        for i in 0..n-1 do
            let x = p.X + side * (4.0 + width * (float i + 0.5))
            box b x p.Y p.Z (width*0.48) 1.1 length bare
            for face in [-1.0; 1.0] do
                for j in [-1.0; 1.0] do
                    box b x (p.Y + face*1.17) (p.Z + j*length*0.5) (width*0.44) 0.15 (length*0.46) tile

    // The hollow break exposes the launch cradle for Halden's recorder.
    let private kestrel (b: MeshBuilder) =
        let aft = v -35.0 -15.0 24.0
        hull b aft [| -36.,27.,-11.,16.; 32.,27.,-12.,18.; 55.,21.,-10.,11. |] iron
        hull b (v 9.0 -5.0 -80.0) [| -43.,5.,-6.,2.; -19.,21.,-11.,11.; 18.,26.,-11.,14. |] ivory
        for side in [-1.; 1.] do
            let x = aft.X + side*24.
            beam b (v x -10. -45.) (v x -10. 76.) 2.3 oxide
            for z in [-30.; -17.; -3.] do
                beam b (v x -15. (z-8.)) (v x 4. z) 1.6 bare
            let drill = v (x+side*10.) -17. 30.
            beam b drill (v x -14. 52.) 4.0 ochre
            let collar z r = ring(10,drill.X,drill.Y,z,r,r)
            b.Loft([|collar -12. 1.; collar 5. 7.; collar 31. 8.|],true,true,oxide)
            for z in [3.; 10.; 17.; 24.] do rim b (collar z (6.5+z*0.062)) 1.15 bare
            box b x -8. 56. 3. 2. 13. amber
        box b -35. -9. -24. 9. 6. 8. ochre
        box b -35. -8. -32.5 6. 2.2 0.8 amber

    let private smelter (b: MeshBuilder) =
        box b -34. -28. 10. 54. 5. 44. iron
        for x,z,h in [-65.,21.,74.; -31.,24.,104.; -53.,-20.,49.] do
            let at = v x -23. z
            spindle b at 12 [| 0.,18.; 24.,22.; 39.,13.; h,8. |] oxide
            for y in [8.; 28.; h-8.] do
                let r = if y < 33. then 22. else 9.2
                spindle b at 12 [|y,r; y+2.6,r|] bare
            spindle b at 12 [|h-0.2,8.1; h+1.,7.9|] soot
            box b x -13. (z-18.) 7.5 3. 2. amber
        beam b (v -60. 9. -21.) (v 37. -40. -39.) 4.8 ochre
        for x in [-65.; -5.] do
            beam b (v x -57. 36.) (v x -30. 36.) 3.2 bare
            beam b (v x -56. -7.) (v x -30. 36.) 2.2 oxide

    let private dish (b: MeshBuilder) radius color =
        let loop z r = ring(16,0.,0.,z,r,r)
        let lip = loop (-radius*0.35) radius
        let mid = loop (-radius*0.11) (radius*0.58)
        rim b lip (radius*0.045) color
        rim b mid (radius*0.027) bare
        for i in 0..7 do
            let k = i*2
            beam b Vec3.zero mid.[k] (radius*0.032) bare
            beam b mid.[k] lip.[k] (radius*0.037) color
        let horn = v 0. 0. (-radius*0.72)
        for k in [0;5;11] do beam b lip.[k] horn (radius*0.022) bare
        b.Box(horn,v (radius*0.089) (radius*0.089) (radius*0.12),amber)

    let private illicitBuoy (b: MeshBuilder) =
        let at = v -24. 0. 0.
        spindle b at 8 [|-47.,5.; -26.,10.; -18.,22.; 6.,20.; 15.,8.; 30.,4.|] ochre
        spindle b at 8 [|-15.,21.8; -8.,21.5|] soot
        for side in [-1.;1.] do panelWing b (at + v 0. -7. 0.) side 3 11. 9.
        beam b (at+v 0. 12. 0.) (at+v 0. 42. 0.) 2.0 bare
        // A hand-wired thirteenth transmitter hangs below the licensed aerial.
        for x in [-29.;-20.;-16.] do box b x -4. -20.2 0.75 3.4 0.65 ivory
        beam b (at+v -8. -15. 3.) (at+v -27. -34. 10.) 1.0 bare
        box b -51. -36. 10. 3.5 5. 4. violet

    // Two ways on the dock. The ship on the far side is capped at the bows,
    // but the aft third is bare ribs, showing how far the builders got.
    let private drydock (b: MeshBuilder) =
        let x = -68.
        let frame z w h = ring(12,x,-12.,z,w,h)
        b.Loft([|frame -190. 8. 6.; frame -147. 37. 29.; frame -103. 46. 34.; frame 18. 46. 34.|],true,true,ivory)
        for z in [-131.;-90.;-41.; 11.; 43.; 75.;107.;139.;163.] do
            let w = if z > 128. then 39. else 46.5
            rim b (frame z w (w*0.74)) 2.2 (if z > 18. then oxide else bare)
        for a in [0.;2.1;3.15;4.2;5.25] do
            beam b (v (x+38.*cos a) (-12.+29.*sin a) 163.) (v (x+46.*cos a) (-12.+34.*sin a) 18.) 2.0 iron
        // Longitudinal stacks of habitat windows follow the intact section.
        for side in [-1.;1.] do
            for deck in [-1.;1.] do
                for z in -96..12..0 do
                    box b (x + side*44.2) (-12.+deck*5.) (float z) 1.9 1.25 3.9 (if z % 36 = 0 then amber else soot)
            let railX = x + side*66.
            beam b (v railX -49. -185.) (v railX -49. 184.) 4.1 iron
            beam b (v railX -38. -185.) (v railX -38. 184.) 2.1 ochre
            for z in [-156.; -100.; -44.; 12.; 68.; 124.] do
                beam b (v railX -49. z) (v railX -38. (z+35.)) 1.3 bare
                beam b (v railX -49. z) (v x -53. z) 3.1 oxide
                box b railX -35. z 3.7 1.6 7. aqua
        hull b (v (x+11.) 13. -145.) [|-20.,6.,-3.,4.; 0.,13.,-5.,10.; 13.,12.,-5.,7.|] iron
        box b (x+11.) 17. -162. 5. 2. 1.7 amber

    let private crane (b: MeshBuilder) =
        for x in [-66.;66.] do
            for z in [-9.;9.] do
                beam b (v x -36. z) (v (x*0.87) 57. (z*0.65)) 2.7 ochre
            for h in [-21.;0.;21.;42.] do
                let xx = x*(1.-(h+36.)*0.13/93.)
                beam b (v xx h -8.) (v xx (h+15.) 8.) 1.4 bare
            box b x -34. 0. 5. 2.5 14. soot
            box b (x*0.87) 58. 0. 3.5 2.8 8. amber
        for z in [-6.;6.] do
            for y in [50.;61.] do beam b (v -60. y z) (v 60. y z) 1.9 ochre
            for x in -56..14..42 do beam b (v (float x) 50. z) (v (float x+14.) 61. z) 1.15 bare
        box b 18. 48. 0. 7. 4. 7. oxide
        for x in [14.;22.] do beam b (v x 48. 0.) (v x 30. 0.) 0.62 iron
        box b 18. 28. 0. 7. 2.2 3. ochre

    let private polar a radius depth = v (radius*cos a) (radius*sin a) depth

    /// Local arc around +X. The chevron at the back is visible through the fracture.
    let private resonator (b: MeshBuilder) =
        let profile a =
            [|polar a 116. -7.; polar a 133. -11.; polar a 149. -3.; polar a 147. 8.; polar a 126. 12.; polar a 115. 5.|]
        b.Loft([|profile -0.117;profile 0.;profile 0.117|],true,true,basalt)
        for z in [-11.;12.] do
            for j in [-1.;1.] do
                beam b (polar (j*0.099) 134. z) (polar 0. 124. z) 1.35 aqua
            beam b (polar -0.09 142. (z*0.67)) (polar 0.09 142. (z*0.67)) 2.0 nacre
        beam b (polar -0.097 115. 0.) (polar 0.097 115. 0.) 2.0 aqua

    let private ringSegments = 22
    let private ringAngle i = tau * float i / float ringSegments
    let private ringTilt = rotate Vec3.up 0.40 * rotate Vec3.right -0.17
    let private thetaSupports (b: MeshBuilder) =
        // A partial outer rail survives on the lower half. The upper fragments float free.
        for i in 10..18 do
            let a = ringAngle i
            let point a r z = Quat.rotateVec3 ringTilt (polar a r z)
            for z in [-5.5;5.5] do
                beam b (point a 157. z) (point (ringAngle (i+1)) 157. z) 2.8 nacre
            beam b (point a 142. 0.) (point a 157. 0.) 3.2 basalt

    let private sleeve (b: MeshBuilder) =
        let x = -42.
        let barrel z r = ring(10,x,2.,z,r,r*0.87)
        b.Loft([|barrel -39. 5.;barrel -29. 12.;barrel 24. 12.;barrel 33. 7.|],true,true,ivory)
        for z in [-24.;-10.;6.;21.] do rim b (barrel z 12.1) 1.1 bare
        for side in [-1.;1.] do panelWing b (v x 0. 0.) side 4 10. 14.
        for z in [-17.;-6.;5.;16.] do
            box b (x-14.) 3. z 2.3 5. 3.3 soot
            box b (x-16.4) 4. z 0.35 1.4 2. green
        // The cold-side shroud conceals these small green sleeve indicators from port.
        for j in 0..4 do
            let z = -37.+float j*16.
            beam b (v -74. -16. z) (v -74. 23. z) 1.2 iron
            box b -75. 4. z 0.8 22. 7.5 iron

    let private iceShard (b: MeshBuilder) (p: Vec3) (size: Vec3) lean =
        let pt x y z = p + v (size.X*x + lean*y) (size.Y*y) (size.Z*z)
        let waist = [|pt -1. -0.2 -0.3;pt -0.3 0.1 -1.;pt 1. 0. 0.;pt 0.42 -0.1 1.;pt -0.62 -0.15 0.7|]
        for i in 0..4 do
            let j = (i+1)%5
            b.TriangleOutward(waist.[i],waist.[j],pt 0. 1. 0.,p,if i%3=0 then nacre else ice)
            b.TriangleOutward(waist.[j],waist.[i],pt 0. -0.65 0.,p,basalt)

    let private iceDrift (b: MeshBuilder) =
        for i in 0..10 do
            let a = float i * 2.4
            let p = v (-133.+26.*float i) (-45.+16.*sin a) (49.*cos a)
            iceShard b p (v (13.+float (i%4)*3.) (10.+float (i%3)*8.) 14.) (9.*sin a)

    let private lens (b: MeshBuilder) =
        let n = 18
        for i in 0..n-1 do
            let a, c = tau*float i/float n, tau*float (i+1)/float n
            let profile t = [|polar t 12. -4.;polar t 27. -9.;polar t 53. 0.;polar t 26. 9.;polar t 12. 4.|]
            b.Loft([|profile a;profile c|],false,false,if i%3=0 then nacre else ice)
            for z in [-4.;4.] do beam b (polar a 12. z) (polar c 12. z) 1.4 aqua

    let private oreLighter (b: MeshBuilder) (p: Vec3) length width cargoBays =
        let z u = length*u
        hull b p [|z -0.62,width*0.42,-8.,1.; z -0.43,width,-12.,0.; z 0.44,width,-12.,0.; z 0.55,width*0.79,-9.,7.|] iron
        for side in [-1.;1.] do
            let x = p.X+side*width*0.92
            beam b (v x p.Y (p.Z+z -0.44)) (v x p.Y (p.Z+z 0.48)) 2.4 oxide
            for i in 0..cargoBays-1 do
                let dz = z (-0.35 + float i*0.75/float cargoBays)
                let at = p + v (side*width*0.51) 3. dz
                let halfBay = length*0.31/float cargoBays
                // A missing container on the nearer flank exposes a torn diagonal.
                if side > 0. || i%3 <> 1 then
                    b.Box(at, v (width*0.35) 7. halfBay, if i%2=0 then oxide else ivory)
                    for d in [-0.65;0.65] do
                        b.Box(at+v 0. 7.1 (d*halfBay),v (width*0.35) 0.7 0.9,bare)
                else beam b at (at+v -9. 9. 12.) 1.0 bare
            let exit = p + v (side*width*0.60) -3. (z 0.54)
            let hoop dz r = ring(8,exit.X,exit.Y,exit.Z+dz,r,r)
            b.Loft([|hoop -3. 5.;hoop 6. 6.|],true,false,bare)
            b.Loft([|hoop 5.9 4.7;hoop 2. 2.8|],false,true,soot)
        hull b (p+v 0. 6. (z -0.48)) [|-8.,width*0.25,-7.,1.;2.,width*0.60,-7.,12.;11.,width*0.54,-7.,12.|] ivory
        b.Box(p+v 0. 12. (z -0.48 - 7.),v (width*0.24) 1.8 1.7,amber)

    let private raiderOffset = v 112. 16. -62.
    let private thrusterOffset = raiderOffset + v 13. -0.6 24.
    let private convoy (b: MeshBuilder) =
        oreLighter b (v -73. -4. 40.) 185. 27. 5
        oreLighter b (v 99. 45. -63.) 117. 22. 3
        // The silhouette under the second lighter has swept wings, paired bores
        // and cold main engines. A steering jet briefly fires under the wing.
        let p = raiderOffset
        hull b p [|-40.,2.2,-1.5,2.; -12.,10.,-5.,7.; 19.,11.,-5.,6.; 28.,7.,-4.,3.|] soot
        for side in [-1.;1.] do
            let at x y z = p + v (side*x) y z
            b.Slab([|at 5. 0. -23.; at 34. -1. 11.; at 36. 3. 26.; at 7. 0. 16.|],
                [|2.4;1.8;1.3;2.5|],Vec3.up,iron)
            beam b (at 8. -1. -36.) (at 8. -1. 2.) 1.2 bare
            beam b (at 25. 1.5 12.) (at 31. 2.8 17.) 1.15 oxide
        let nozzle z r = ring(8,thrusterOffset.X,thrusterOffset.Y,thrusterOffset.Z+z,r,r)
        b.Loft([|nozzle -8. 2.5;nozzle -1. 3.;nozzle 0. 2.6|],true,false,bare)
        for i in 0..9 do
            let k = float i
            let at = v (-27.+k*11.) (-23.+7.*sin (k*1.53)) (65.+19.*cos k)
            b.Slab([|at;at+v 5. 0. -3.;at+v 14. 0. 3.;at+v 3. 0. 5.|],
                [|0.35;0.4;0.8;0.55|],Vec3.up,if i%3=0 then oxide else bare)

    let private falseBeacon = v 104. -14. 26.
    let private beaconHousing (b: MeshBuilder) (p: Vec3) color =
        spindle b p 8 [|-14.,2.;-7.,5.7; 4.,5.8;7.,3.;11.,3.|] ivory
        spindle b p 8 [|-4.,6.1;-1.,6.1|] color
        spindle b p 8 [|11.,3.8;15.,2.7|] color
        for x in [-1.;1.] do
            beam b (p+v (x*4.) 0. 0.) (p+v (x*8.) -9. 0.) 0.7 bare
            b.Box(p+v (x*8.) -9. 0.,v 1.4 1. 2.2,ochre)

    let private prismFoot (b: MeshBuilder) =
        let p = v -45. -9. -9.
        for i in 0..2 do
            let a = float i*tau/3.
            let at r y = p + v (r*cos a) y (r*sin a)
            beam b (at 64. -47.) (at 35. -24.) 4.2 basalt
            beam b (at 35. -24.) (at 43. -4.) 2.7 nacre
            beam b (at 43. -4.) (at 46. 4.) 2.3 violet
        let inactive = MeshColor.glass 0.27 0.039 0.019 0.08
        beaconHousing b falseBeacon inactive
        // The counterfeiter has bolted a second broadcast head onto a stock beacon.
        let p2 = falseBeacon + v 9. 8. -3.
        beam b (falseBeacon+v 0. 3. 0.) p2 1.5 oxide
        b.Box(p2,v 2.6 4. 2.8,inactive)

    let private counterfeitLamp (b: MeshBuilder) =
        spindle b Vec3.zero 8 [|11.1,3.95;15.1,2.85|] red
        box b 9. 8. -3. 2.8 4.2 3. red

    let private prism (b: MeshBuilder) =
        let p a r y = v (r*cos a) y (r*sin a)
        for i in 0..2 do
            let a, c = tau*float i/3., tau*float (i+1)/3.
            let ridge = p a 38. 0.
            let next = p c 38. 0.
            let crown = v 5. 71. -1.
            let foot = v -3. -41. 0.
            b.TriangleOutward(ridge,next,crown,Vec3.zero,if i=0 then ice else nacre)
            b.TriangleOutward(next,ridge,foot,Vec3.zero,basalt)
            beam b ridge crown 1.3 violet
            beam b ridge next 1.9 violet

    // Each site has one opaque fixed batch and one batch for moving parts.
    let private siteForms =
        [|kestrel;smelter;illicitBuoy;drydock;thetaSupports;sleeve;iceDrift;convoy;prismFoot|]

    let private movingForms : (MeshBuilder -> unit)[] =
        [| (fun b ->
                hull b Vec3.zero [|-16.,5.,-2.,1.;2.,7.,-4.,3.;13.,4.,-3.,2.|] oxide
                beam b (v -6. 0. 0.) (v -9. 4. 13.) 1.2 bare
                box b 0. 3. 2. 2.2 0.9 5. amber)
           (fun b ->
                // Seized slag conveyor; only the take-up wheel still turns.
                let point a r z = polar a r z
                for i in 0..9 do
                    let a, c = tau*float i/10., tau*float (i+1)/10.
                    beam b (point a 12. 0.) (point c 12. 0.) 2.2 oxide
                    beam b (point a 3. 0.) (point a 12. 0.) 1.3 (if i=1 then amber else bare))
           (fun b -> dish b 27. bare)
           crane
           resonator
           (fun b -> dish b 10. soot)
           lens
           (fun b ->
                let jet z r = ring(8,0.,0.,z,r,r)
                b.Loft([|jet 0. 2.05;jet 2.3 1.7;jet 5.2 0.2|],true,true,amber))
           prism |]

    // The guides fly with the bow on local -Z, like the other traffic.
    let private tanker (b: MeshBuilder) =
        hull b Vec3.zero [|-20.,2.4,-2.,0.;-14.,5.,-3.,4.;13.,5.,-3.,2.;20.,4.,-2.,2.|] ochre
        for x in [-7.;7.] do
            let body z r = ring(12,x,0.,z,r,r)
            b.Loft([|body -13. 2.;body -9. 4.1;body 12. 4.1;body 15. 2.|],true,true,ivory)
            for z in [-8.;0.;8.] do rim b (body z 4.2) 0.48 oxide
            box b x 0. 15.1 1.8 1.8 0.65 amber

    let private courier (b: MeshBuilder) =
        hull b Vec3.zero [|-17.,0.8,-1.,0.; -4.,4.6,-2.,3.;12.,5.,-2.,2.;15.,2.,-1.,1.|] iron
        for side in [-1.;1.] do
            let at x y z = v (side*x) y z
            b.Slab([|at 1. 0. -10.;at 12. -1. 7.;at 10. 0.5 13.;at 3. 0. 7.|],
                [|0.9;0.3;0.6;1.0|],Vec3.up,tile)
            beam b (at 2. 2. -7.) (at 2. 3. -2.) 0.45 violet
            box b (side*2.) 0. 14. 0.85 0.85 1.2 aqua

    let private survey (b: MeshBuilder) =
        hull b Vec3.zero [|-17.,2.,-2.,1.;-7.,5.,-3.,3.;11.,5.,-3.,3.;17.,3.,-2.,2.|] ivory
        for side in [-1.;1.] do
            beam b (v (side*3.) 0. -5.) (v (side*13.) 1. -12.) 0.65 bare
            beam b (v (side*13.) 1. -16.) (v (side*13.) 1. -7.) 1.15 tile
            box b (side*13.) 1. -16.2 0.75 0.75 0.6 aqua
            box b (side*2.) 0. 17.2 1. 1. 0.5 aqua
        box b 0. 3.1 -7. 3. 0.65 2. soot

    let private listeningPlatform (b: MeshBuilder) =
        dish b 17. ivory
        spindle b (v 0. -12. 3.) 8 [|-9.,4.;-6.,7.;3.,6.;5.,3.|] iron
        for side in [-1.;1.] do panelWing b (v 0. -13. 3.) side 2 5. 4.

    let private salvageTender (b: MeshBuilder) =
        hull b Vec3.zero [|-23.,4.,-4.,1.;-16.,9.,-5.,4.;17.,9.,-5.,1.;23.,6.,-3.,1.|] oxide
        hull b (v -2. 3. -12.) [|-8.,3.,-2.,1.;-2.,5.,-2.,6.;5.,5.,-2.,6.|] ivory
        box b -2. 6. -16. 3. 1. 1. amber
        for side in [-1.;1.] do
            let x = side*7.
            beam b (v x 1. 2.) (v x 10. 7.) 0.8 bare
            beam b (v x 10. 7.) (v x 11. 19.) 0.75 ochre
            box b x 10. 19. 1. 1. 2. aqua
            for z in [0.;7.;14.] do box b x 2. z 2.3 1.9 2.4 ivory
            box b (side*4.) -1. 22.9 1.2 1.2 0.7 amber

    let private carriedFrame (b: MeshBuilder) =
        for z in [-9.;9.] do rim b (ring(8,0.,0.,z,11.,8.)) 0.85 oxide
        for side in [-1.;1.] do
            beam b (v (side*11.) 0. -9.) (v (side*11.) 0. 9.) 0.77 bare
            beam b (v (side*10.6) 0. 0.) (v (side*18.) 0. 2.) 0.38 ochre

    type Models =
        private
            { Sites: Lazy<InstanceBatch * InstanceBatch>[]
              Guides: Lazy<InstanceBatch>[]
              FalseLamp: Lazy<InstanceBatch>
              Tow: Lazy<InstanceBatch> }

    let create (renderer: Renderer) =
        let batch mesh count = Renderer.createBatch renderer (Renderer.createMesh renderer mesh) count Opaque
        let guide make count = lazy (batch (make ()) count)
        let tug = guide CivilTrafficMeshes.tug 2
        { Sites = siteForms |> Array.mapi (fun i make -> lazy (
              let count = if i = 4 then ringSegments else 1
              batch (build make) 1, batch (build movingForms.[i]) count))
          Guides = [|tug; guide (fun () -> build tanker) 1; guide (fun () -> build courier) 1; tug
                     guide (fun () -> build listeningPlatform) 1; guide AetherTrafficMeshes.escort 1
                     guide (fun () -> build survey) 1; guide (fun () -> build salvageTender) 1
                     guide (fun () -> build (fun b -> beaconHousing b Vec3.zero green)) 1|]
          FalseLamp = guide (fun () -> build counterfeitLamp) 1
          Tow = guide (fun () -> build carriedFrame) 1 }

    let private drawAt renderer batch position rotation brightness =
        InstanceBatch.clear batch
        InstanceBatch.push batch position rotation Vec3.one brightness brightness brightness 0.
        Renderer.draw renderer batch

    let draw renderer models (sector: int) (origin: Vec3) (time: float) (scanPulse: float) =
        let fixedPart, moving = models.Sites.[sector].Value
        drawAt renderer fixedPart origin Quat.identity 1.
        let place offset rotation brightness = drawAt renderer moving (origin+offset) rotation brightness
        match sector with
        | 0 -> place (v 15. (5.+2.*sin (time*0.22)) -31.) (rotate Vec3.up (time*0.084)) 1.
        | 1 -> place (v 26. -34. -39.) (rotate Vec3.unitZ (-time*0.14)) 1.
        | 2 -> place (v -24. 42. 0.) (rotate Vec3.up (time*0.27)) 1.
        | 3 -> place (v -68. 0. (85.*sin (time*0.087))) Quat.identity 1.
        | 4 ->
            InstanceBatch.clear moving
            for i in 0..ringSegments-1 do
                if i <> 2 && i <> 6 && i <> 7 && i <> 20 then
                    let a = ringAngle i
                    let response = (0.5 + 0.5*cos (time*1.85-a*2.)) ** 5.
                    let upper = i>=3 && i<=5
                    let shift = if upper then 5. + 4.5*response + 4.*scanPulse else 0.
                    let offset = Quat.rotateVec3 ringTilt (polar a shift (shift*0.46))
                    let rotation = ringTilt * rotate Vec3.unitZ a
                    let level = 0.57 + 0.60*response + (if upper then 1.8*scanPulse else 0.)
                    InstanceBatch.push moving (origin+offset) rotation Vec3.one level level level 0.
            Renderer.draw renderer moving
        | 5 -> place (v -42. 14. -15.) (rotate Vec3.up (0.6*sin (time*0.23))) 0.75
        | 6 ->
            let drift = v (6.*sin (time*0.15)) (3.*cos (time*0.17)) (2.*sin (time*0.24))
            place drift (rotate Vec3.up 0.37 * rotate Vec3.unitZ (time*0.06)) 1.
        | 7 ->
            // The seemingly dead ship corrects its heading in two short bursts.
            let phase = time % 5.
            let level = if phase<0.16 || (phase>0.32 && phase<0.50) then 1.4 else 0.035
            place thrusterOffset Quat.identity level
        | _ ->
            place (v -45. (-2.+2.5*sin (time*0.26)) -9.) (rotate Vec3.up (time*0.15)) 1.
            let phase = time % 2.8
            let level = if phase<0.21 || (phase>0.43 && phase<0.64) then 1.6 else 0.08
            drawAt renderer models.FalseLamp.Value (origin+falseBeacon) Quat.identity level

    let drawGuide renderer models (sector: int) (position: Vec3) (rotation: Quat) (time: float) =
        let guide = models.Guides.[sector].Value
        let swing = sin (time*0.63)
        match sector with
        | 3 ->
            let pose = rotation * rotate Vec3.unitZ (0.026*swing)
            InstanceBatch.clear guide
            for side in [-1.;1.] do
                let offset = Quat.rotateVec3 pose (v (24.*side) 0. 0.)
                InstanceBatch.push guide (position+offset) pose Vec3.one 1. 1. 1. 0.
            Renderer.draw renderer guide
            drawAt renderer models.Tow.Value position pose 1.
        | 4 -> drawAt renderer guide position (rotation * rotate Vec3.up (0.23*swing)) 1.
        | 8 ->
            let brightness = 0.87+0.20*cos (time*2.)
            drawAt renderer guide position rotation brightness
        | _ ->
            let bank = 0.056*sin (time*0.55+float sector)
            drawAt renderer guide position (rotation * rotate Vec3.unitZ bank) 1.

