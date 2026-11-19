namespace SpaceEngine

open System

[<RequireQualifiedAccess>]
module PoiMeshes =
    let private v x y z = Vec3(x,y,z)
    let private tau = 2.0 * Math.PI
    let private beam = ProceduralMeshes.beam
    let private ring = ProceduralMeshes.ring
    let private panel = ProceduralMeshes.armorPanel
    let private steel = MeshColor.metal 0.34 0.38 0.39
    let private iron = MeshColor.rgb 0.105 0.14 0.16
    let private rubber = MeshColor.rubber 0.055 0.06 0.063
    let private bone = MeshColor.rgb 0.55 0.56 0.50
    let private brass = MeshColor.metal 0.55 0.37 0.12
    let private foil = MeshColor.metal 0.43 0.52 0.56
    let private glass = MeshColor.glass 0.075 0.19 0.29 0.15
    let private warm = MeshColor.glow 1.16 0.45 0.065 0.97
    let private cold = MeshColor.glow 0.16 0.90 1.20 0.98
    let private marker = MeshColor.glow 1.0 1.0 1.0 1.0
    let private paint =
        [| MeshColor.rgb 0.63 0.34 0.085; MeshColor.rgb 0.56 0.16 0.06
           MeshColor.rgb 0.08 0.39 0.35; MeshColor.rgb 0.43 0.50 0.38
           MeshColor.rgb 0.33 0.28 0.48; MeshColor.rgb 0.65 0.49 0.23
           MeshColor.rgb 0.27 0.52 0.62; MeshColor.rgb 0.39 0.17 0.13
           MeshColor.rgb 0.36 0.28 0.39 |]

    let private build make =
        let b = MeshBuilder()
        make b
        b.Build()

    let private box (b: MeshBuilder) (p: Vec3) x y z color =
        b.Box(p,v x y z,color)

    let private barrel (b: MeshBuilder) (p: Vec3) radius length color =
        let hoop dz r = ring(12,p.X,p.Y,p.Z+dz,r,r)
        let h = length*0.5
        b.Loft([|hoop -h (radius*0.67); hoop (-h+1.8) radius
                 hoop (h-1.8) radius; hoop h (radius*0.67)|],true,true,color)
        for z in [-h*0.65;h*0.65] do
            b.Loft([|hoop z (radius+0.4);hoop (z+1.) (radius+0.4)|],false,false,iron)

    let private crate (b: MeshBuilder) (p: Vec3) width height depth color =
        box b p width height depth color
        for z in [-1.;1.] do
            box b (p+v 0. 0. (z*depth*0.73)) (width+0.4) (height+0.4) 0.75 iron
        for x in [-1.;1.] do
            box b (p+v (x*(width+0.15)) 0. 0.) 0.25 (height*0.61) 0.9 brass
        box b (p+v 0. (height*0.32) (-depth-0.1)) (width*0.43) 0.65 0.25 bone

    let private depot sector (b: MeshBuilder) =
        let color = paint.[sector]
        let bays = 3 + sector%3
        let span = float (bays-1)*11.
        for z in [-28.;28.] do
            beam b (v (-span-15.) -26. z) (v (span+15.) -26. z) 3.2 iron
            beam b (v (-span-18.) -32. z) (v (span+18.) -32. z) 2.1 color
            for i in 0..bays-1 do
                let x = float i*22.-span
                beam b (v (x-9.) -32. z) (v (x+9.) -25. z) 1.25 steel
        for i in 0..bays-1 do
            let x = float i*22.-span
            beam b (v x -27. -32.) (v x -27. 32.) 3.0 color
            for side in [-1.;1.] do
                let p = v x -17. (side*19.)
                match sector%3 with
                | 0 -> barrel b p 7.9 21. color
                | 1 ->
                    crate b p 8. 7. 10. color
                    if (i+sector)%2=0 then crate b (p+v 0. 11. 0.) 6. 3.7 8.7 bone
                | _ ->
                    crate b p 8.5 7. 11. bone
                    for dz in [-7.;-3.5;0.;3.5;7.] do
                        box b (p+v 0. 8. dz) 8.8 1.6 0.65 color
        // The transfer track lies outside the stowage racks.
        let endX = span+14.
        for x in [endX;endX+8.] do
            beam b (v x -19. -38.) (v x -19. 38.) 1.3 brass
        for side in [-1.;1.] do
            let z = side*29.
            beam b (v (-span-11.) -26. z) (v (-span-11.) 8. z) 2.2 color
            beam b (v (-span-11.) 8. z) (v (-span+2.) 13. z) 1.8 steel
            box b (v (-span+2.) 13. z) 2.8 2.2 2.8 warm

    let private shuttle (b: MeshBuilder) =
        box b Vec3.zero 6.3 2.2 7.1 iron
        crate b (v 0. 4.8 0.) 4.7 2.5 5.2 bone
        for x in [-5.4;5.4] do
            for z in [-4.;4.] do box b (v x -1.3 z) 1.2 1.7 1.5 rubber
        box b (v 0. 2.4 -7.15) 2.7 0.72 0.35 cold

    let private flare (b: MeshBuilder) =
        // Eight faces keep small marker lenses legible from either side.
        let edge = ring(8,0.,0.,0.,1.,1.)
        b.Loft([|ring(8,0.,0.,-0.7,0.67,0.67);edge;ring(8,0.,0.,0.7,0.67,0.67)|],true,true,marker)

    let private nodeBase (b: MeshBuilder) x color =
        let p = v x -11. 0.
        let frame y r =
            Array.init 10 (fun i ->
                let a = tau*float i/10.
                p+v (r*cos a) y (r*sin a))
        b.Loft([|frame -4. 8.;frame 0. 11.;frame 5. 8.5|],true,true,iron)
        b.Loft([|frame 1. 10.7;frame 3. 10.2|],false,false,color)
        for dx in [-5.;5.] do beam b (p+v dx -1. 0.) (v (x+dx) 0. 0.) 1.4 steel

    let private listeningArray sector (b: MeshBuilder) =
        let color = paint.[sector]
        let armDepth = 16. + 3.*float (sector%3)
        for side in [-1.;1.] do
            let x = side*85.
            let shoulder = v (side*34.) -30. armDepth
            let tip = v x -12. 0.
            for z in [-4.;4.] do
                let dz = v 0. 0. z
                beam b (v 0. -29. armDepth + dz) (shoulder+dz) 2.0 iron
                beam b (shoulder+dz) (tip+dz) 1.8 color
            beam b (v (side*20.) -35. armDepth) (tip+v 0. -4. 0.) 1.6 iron
            for j in 1..4 do
                let t = float j/5.
                let a = Vec3.lerp shoulder tip t
                beam b (a+v -2. 0. -4.) (a+v 2. 0. 4.) 0.9 steel
            nodeBase b x (if side<0. then warm else cold)
        box b (v 0. -29. armDepth) 12. 6. 10. color
        let sails = 2 + sector%3
        for side in [-1.;1.] do
            for i in 0..sails-1 do
                let x = side*(17.+float i*11.)
                let p = v x -27. (armDepth+14.)
                box b p 5.3 1.1 12.7 steel
                for z in [-6.;6.] do box b (p+v 0. 1.1 z) 4.9 0.25 5.7 glass

    // Perforated reflector with a solid outer annulus and a supported feed.
    let private receiver (b: MeshBuilder) =
        let at a r y = v (r*cos a) y (r*sin a)
        let n = 16
        for i in 0..n-1 do
            let a, c = tau*float i/float n,tau*float (i+1)/float n
            let lip, next = at a 20. 5.,at c 20. 5.
            let mid, nextMid = at a 13. 1.6,at c 13. 1.6
            panel b [|mid;nextMid;next;lip|] Vec3.up 0.7 foil iron
            if i%2=0 then
                panel b [|at a 4. 0.;at c 4. 0.;nextMid;mid|] Vec3.up 0.5 bone iron
                beam b mid (at a 2. 0.) 0.46 steel
            if i%4=0 then beam b lip (v 0. 14. 0.) 0.55 brass
        box b (v 0. 14. 0.) 1.7 2.2 1.7 iron

    let private maintenance sector (b: MeshBuilder) =
        let color = paint.[sector]
        // A service slip opens towards -Z, with tools, stores and the rescue pod
        // on the far side of its low deck.
        for x in [-25.;25.] do
            beam b (v x -24. -42.) (v x -24. 45.) 3.1 iron
            beam b (v (x*1.23) -18. -41.) (v (x*1.23) -18. 43.) 2.0 color
            for z in [-34.;-15.;4.;23.;42.] do
                beam b (v x -24. z) (v (x*1.23) -18. z) 1.3 steel
            beam b (v x -23. 25.) (v (x*1.14) 9. 25.) 3.4 color
            beam b (v (x*1.14) 9. 25.) (v (x*1.42) 20. 15.) 2.3 steel
            box b (v (x*1.42) 20. 15.) 4.8 2.8 4.8 iron
        for z in [-31.;0.;31.] do beam b (v -28. -24. z) (v 28. -24. z) 2.9 steel
        let cabin = v 0. 0. 48.
        barrel b cabin 10.5 28. bone
        box b (cabin+v 0. -8. 0.) 15. 5. 16. color
        for side in [-1.;1.] do
            box b (cabin+v (side*10.) 2. 0.) 0.55 1.6 7. glass
            beam b (v (side*18.) -16. 32.) (v (side*8.) -7. 43.) 2.0 iron
        match sector%3 with
        | 0 ->
            for z in [-11.;5.;21.] do barrel b (v 38. -13. z) 5. 14. bone
        | 1 ->
            for side in [-1.;1.] do
                for z in [-13.;8.] do crate b (v (side*34.) -12. z) 4.8 5. 8. color
        | _ ->
            for h in [-7.;0.;7.] do
                box b (v 0. h 63.) 15.5 0.7 7.2 color
            beam b (v 0. -11. 62.) (v 0. 11. 62.) 1.5 brass

    let private jaw (b: MeshBuilder) =
        beam b Vec3.zero (v 0. 3. -16.) 2.5 bone
        beam b (v 0. 3. -16.) (v 0. -6. -22.) 1.75 brass
        box b (v 0. -6. -22.) 2.3 2.5 3. rubber
        box b (v 0. 4. -13.) 1.25 1.1 2.2 warm

    let private polar a r z = v (cos a*r) (sin a*r) z

    let private calibrationRing sector (b: MeshBuilder) =
        let color = paint.[sector]
        let n = 7 + sector%5
        let step = tau/float n
        let profile a = [|polar a 61. -3.;polar a 67. -7.;polar a 75. -5.;polar a 78. 1.;polar a 74. 6.;polar a 62. 5.|]
        for i in 0..n-1 do
            let a = step*float i
            b.Loft([|profile (a-step*0.45);profile a;profile (a+step*0.45)|],true,true,color)
            for z in [-3.;3.] do
                beam b (polar (a+step*0.40) 68. z) (polar (a+step*0.60) 68. z) 1.5 steel
            for z in [-7.;6.] do
                beam b (polar (a-step*0.30) 66. z) (polar (a+step*0.30) 66. z) 1.55 cold
            if i%2=0 then
                let r = 85.+float (sector%3)*7.
                let fin = [|polar (a-0.16) 75. 0.;polar (a-0.055) r -3.;polar (a+0.055) r -3.;polar (a+0.16) 75. 0.|]
                panel b fin Vec3.unitZ 1.4 foil steel
                beam b (polar a 76. 0.) (polar a r 0.) 1.4 brass

    let private caliper (b: MeshBuilder) =
        // Three shoes traverse the inside face; even their tips clear the 44 m route.
        let profile a = [|polar a 53. -4.;polar a 59. -3.;polar a 59. 3.;polar a 53. 4.|]
        b.Loft([|profile -0.115;profile 0.;profile 0.115|],true,true,brass)
        for z in [-4.;4.] do beam b (polar -0.075 54. z) (polar 0.075 54. z) 1.35 marker

    let private frames = [|depot;listeningArray;maintenance;calibrationRing|]
    let private mechanisms = [|shuttle;receiver;jaw;caliper|]

    type Models =
        private
            { Frame: Lazy<InstanceBatch>[][]
              Mechanism: Lazy<InstanceBatch>[]
              Lamps: Lazy<InstanceBatch> }

    let create renderer =
        let batch make count = lazy (Renderer.createBatch renderer (Renderer.createMesh renderer (build make)) count Opaque)
        { Frame = frames |> Array.map (fun make -> Array.init paint.Length (fun sector -> batch (make sector) 1))
          Mechanism = mechanisms |> Array.map (fun make -> batch make 3)
          Lamps = batch flare 3 }

    let draw renderer models (kind: int) (sector: int) (position: Vec3) (time: float) (completed: bool) =
        let frame = models.Frame.[kind].[sector].Value
        let moving = models.Mechanism.[kind].Value
        let lamps = models.Lamps.Value
        InstanceBatch.clear frame
        InstanceBatch.clear moving
        InstanceBatch.clear lamps
        InstanceBatch.push frame position Quat.identity Vec3.one 1. 1. 1. 0.
        let place offset rotation scale =
            InstanceBatch.push moving (position+offset) rotation scale 1. 1. 1. 0.
        let lamp offset size (color: Vec3) level =
            InstanceBatch.push lamps (position+offset) Quat.identity (v size size size)
                (color.X*level) (color.Y*level) (color.Z*level) 0.
        let phase = time*0.77 + float sector
        let status = if completed then v 0.20 1.06 0.45 else v 1.15 0.54 0.16
        let pulse = if completed then 0.91 else 0.76+0.25*cos phase
        match kind with
        | 0 ->
            let span = float (2+sector%3)*11.
            place (v (span+18.) -15. (30.*sin (phase*0.25))) Quat.identity Vec3.one
            for side in [-1.;1.] do lamp (v (-span-11.) 10. (side*29.)) 3.5 status pulse
        | 1 ->
            for side in [-1.;1.] do
                let x = side*85.
                let rotation = Quat.fromAxisAngle Vec3.up (side*0.30 + 0.32*sin (phase*0.48))
                               * Quat.fromAxisAngle Vec3.right (side*0.68)
                place (v x 0. 0.) rotation Vec3.one
                let feed = Quat.rotateVec3 rotation (v 0. 16.4 0.)
                let hue = if side<0. then v 1.2 0.48 0.09 else v 0.13 0.91 1.18
                lamp (v x 0. 0. + feed) 3.6 hue (0.91+0.12*cos (phase+side))
            lamp (v 0. -21. (16.+3.*float (sector%3))) 2.8 status pulse
        | 2 ->
            for side in [-1.;1.] do
                let basePos = v (side*35.5) 20. 15.
                let yaw = side*(0.34+0.27*sin (phase*0.62))
                place basePos (Quat.fromAxisAngle Vec3.up yaw) Vec3.one
                lamp (basePos+v 0. 3. 0.) 2.6 status pulse
        | _ ->
            let tint = if completed then v 0.18 1.06 0.44 else v 0.22 0.85 1.19
            for i in 0..2 do
                let a = float i*tau/3. + time*0.16
                place Vec3.zero (Quat.fromAxisAngle Vec3.unitZ a) Vec3.one
                lamp (polar a 51. 0.) 2.8 tint pulse
        Renderer.draw renderer frame
        Renderer.draw renderer moving
        Renderer.draw renderer lamps
