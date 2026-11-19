namespace SpaceEngine

/// Civilian ships use metres, with the bow towards -Z. Lamps and drives are
/// shared with the traffic renderer so flares stay attached through turns.
/// Belt traffic is old and overworked: dull paint, blacked-out glass and a few
/// sodium lamps where someone is still awake.
[<RequireQualifiedAccess>]
module CivilTrafficMeshes =
    let private v x y z = Vec3(x,y,z)
    let private steel = MeshColor.rgb 0.105 0.125 0.135
    let private rubber = MeshColor.rubber 0.028 0.034 0.040
    let private ivory = MeshColor.rgb 0.40 0.39 0.345
    let private teal = MeshColor.rgb 0.05 0.135 0.15
    let private ochre = MeshColor.rgb 0.50 0.27 0.045
    /// Red-oxide primer on hulls that were never painted again after their last refit.
    let private oxide = MeshColor.rgb 0.235 0.09 0.055
    let private glass = MeshColor.glass 0.035 0.045 0.052 0.08
    let private cabin = MeshColor.glow 1.0 0.56 0.16 0.72
    let private ion = MeshColor.glow 0.27 0.80 1.0 1.0
    let private alloy = MeshColor.metal 0.36 0.38 0.385
    let private smokedGlass = MeshColor.glass 0.028 0.04 0.048 0.10

    let private sleeve (b: MeshBuilder) start finish radius color =
        let direction = Vec3.normalize (finish-start)
        let reference = if abs direction.Y > 0.9 then Vec3.unitZ else Vec3.up
        let across = Vec3.normalize (Vec3.cross direction reference)
        let up = Vec3.cross direction across
        let section = ProceduralMeshes.ring(10,0.0,0.0,0.0,radius,radius)
        let at center = section |> Array.map (fun p -> center + across*p.X + up*p.Y)
        b.Loft([|at start; at finish|],true,true,color)

    let private shell (b: MeshBuilder) x sections color =
        b.Loft(sections |> Array.map (fun (z,w,low,high) ->
            ProceduralMeshes.hullSection x z w low high), true, true, color)

    let private drive (b: MeshBuilder) (exit: Vec3) radius =
        let hoop dz scale = ProceduralMeshes.ring(12,exit.X,exit.Y,exit.Z+dz,radius*scale,radius*scale)
        b.Loft([|hoop (-radius*2.8) 1.00; hoop (-radius) 1.14; hoop 0.0 1.0|],true,false,steel)
        let outer,lip,throat = hoop 0.0 1.0, hoop 0.0 0.80, hoop (-radius*0.53) 0.58
        for i in 0..11 do
            let j = (i+1)%12
            b.PolygonOutward([|outer.[i];outer.[j];lip.[j];lip.[i]|],exit - Vec3.unitZ,rubber)
            b.PolygonOutward([|lip.[i];lip.[j];throat.[j];throat.[i]|],outer.[i],steel)
        b.PolygonOutward(throat,exit-Vec3.unitZ*(2.0*radius),ion)

    let private navLights (b: MeshBuilder) (lamps: Vec3[]) =
        b.Box(lamps.[0], v 0.23 0.22 0.48, MeshColor.glow 1.0 0.035 0.018 0.92)
        b.Box(lamps.[1], v 0.23 0.22 0.48, MeshColor.glow 0.045 0.93 0.32 0.92)

    let tugDrives = [|v -4.25 -0.15 12.6,1.55; v 4.25 -0.15 12.6,1.55|]
    let tugLights = [|v -6.55 1.22 -7.8; v 6.55 1.22 -7.8|]

    /// Open grapple, offset wheelhouse and exposed winch on a short twin-screw tug.
    let tug () =
        let b = MeshBuilder()
        let box x y z hx hy hz color = b.Box(v x y z,v hx hy hz,color)
        let beam = ProceduralMeshes.beam b
        shell b 0.0 [|-6.8,2.9,-1.9,1.0; -3.0,4.0,-2.5,1.5; 6.7,4.0,-2.1,1.9; 9.5,2.9,-1.5,1.25|] steel
        for side in [-1.0;1.0] do
            let x = side*4.25
            shell b x [|-7.6,1.28,-1.4,1.5; -4.2,2.05,-2.1,2.2; 6.8,1.95,-2.1,2.0; 10.2,1.56,-1.7,1.56|] oxide
            for z in [1.4;3.1;4.8] do
                box x 2.13 z 1.12 0.15 0.27 rubber
            // Upper and lower forks meet at a padded, inward-pointing shoe.
            let pivot = v (side*5.3) 0.0 -6.1
            let elbow = v (side*6.7) 0.0 -13.6
            let jaw = v (side*5.75) 0.0 -16.9
            beam pivot elbow 0.77 ochre
            beam (v (side*4.0) -1.5 -4.8) elbow 0.36 steel
            beam elbow jaw 0.72 steel
            beam jaw (v (side*3.55) 0.0 -17.4) 0.65 rubber
            // A separately mounted hydraulic cylinder rises above each grapple arm.
            let anchor = v (side*4.25) 2.35 -4.05
            let knuckle = elbow + v (-side*0.35) 1.05 0.25
            let seal = Vec3.lerp anchor knuckle 0.59
            sleeve b anchor seal 0.53 ochre
            sleeve b (Vec3.lerp anchor knuckle 0.52) seal 0.59 rubber
            sleeve b seal knuckle 0.30 alloy
            for pin in [|pivot;elbow;jaw|] do
                sleeve b (pin-Vec3.up*0.88) (pin+Vec3.up*0.9) 0.97 steel
                sleeve b (pin+Vec3.up*0.89) (pin+Vec3.up*1.05) 0.51 alloy
            box (side*6.25) 0.38 -7.8 0.62 0.70 1.05 steel
            box (side*6.18) -0.1 4.9 0.23 0.84 1.8 rubber
            // Grilles, warning bars and rubber rubbing strips stand proud of the side plates.
            let flank y z = v (side*6.37) y z
            b.Box(flank 0.1 -1.6,v 0.13 0.87 2.12,steel)
            for z in [-2.9;-2.18;-1.46;-0.74] do
                beam (flank -0.50 z + Vec3.unitX*(side*0.16))
                     (flank 0.68 (z+0.42) + Vec3.unitX*(side*0.16)) 0.15 rubber
            for z in [3.95;5.15] do
                beam (v (side*6.43) -0.73 z) (v (side*6.43) 0.55 (z+0.69)) 0.19 ochre
            let spot = v (side*4.25) 2.42 -5.5
            b.Box(spot,v 0.66 0.48 0.55,rubber)
            b.Box(spot-v 0.0 0.0 0.56,v 0.50 0.32 0.055,cabin)
        // The pilot sits to port, with a direct view into the open jaws.
        shell b -1.8 [|-6.1,1.4,0.9,2.4; -4.2,2.02,1.3,4.6; 0.6,1.83,1.5,4.1|] ivory
        shell b -1.8 [|-6.22,1.35,2.23,2.5; -4.22,2.07,3.05,4.28; -1.1,1.97,2.98,4.03|] glass
        box -1.8 4.66 -3.35 1.62 0.14 0.95 steel
        for x in [-1.05;1.05] do
            let outer = v (-1.8+x) 2.45 -6.18
            beam outer (v (-1.8+x*1.35) 4.35 -4.12) 0.085 steel
        let axle x r = ProceduralMeshes.ring(12,0.0,0.0,0.0,r,r) |> Array.map (fun p -> v x (2.8+p.Y) (1.8+p.X))
        b.Loft([|axle 0.70 1.12; axle 3.55 1.12|],true,true,rubber)
        for x in [0.65;1.25;1.83;2.41;3.0;3.56] do
            b.Loft([|axle x 1.3; axle (x+0.13) 1.3|],true,true,steel)
        for x in [0.48;3.87] do
            beam (v x 1.3 -0.55) (v x 2.92 1.82) 0.23 ochre
            beam (v x 2.92 1.82) (v x 1.53 4.1) 0.23 ochre
        let roller = v 1.7 1.1 -6.8
        sleeve b (roller-v 0.76 0.0 0.0) (roller+v 0.76 0.0 0.0) 0.36 alloy
        beam (v 2.1 2.25 1.0) (roller+Vec3.up*0.33) 0.11 rubber
        beam (roller+Vec3.up*0.33) (v 1.2 -0.45 -8.4) 0.11 rubber
        sleeve b (v 1.2 -0.5 -7.93) (v 1.2 -0.5 -8.65) 0.32 ochre
        box 0.0 -2.24 3.4 2.5 0.25 2.9 rubber
        for nozzle,radius in tugDrives do drive b nozzle radius
        navLights b tugLights
        b.Build()

    let linerDrives = [|v -6.65 -0.8 22.6,1.45; v 6.65 -0.8 22.6,1.45|]
    let linerLights = [|v -8.45 -0.58 15.8; v 8.45 -0.58 15.8|]

    let private passengerGallery (b: MeshBuilder) =
        let arch z width rise =
            [| for x,y in [| -1.,0.; -0.91,0.57; -0.56,0.92; 0.,1.; 0.56,0.92; 0.91,0.57; 1.,0. |] ->
                   v (width*x) (4.08+rise*y) z |]
        let bow = arch -6.35 1.64 0.30
        let front = arch -4.55 2.31 1.59
        let back = arch 4.60 2.31 1.59
        let stern = arch 6.55 1.58 0.31
        b.Loft([|bow;front;back;stern|],true,true,smokedGlass)
        for z in [-4.46;-1.5;1.5;4.46] do
            b.Loft([|arch (z-0.085) 2.34 1.66; arch (z+0.085) 2.34 1.66|],false,false,ivory)
        for side in [-1.;1.] do
            ProceduralMeshes.beam b (v (side*2.25) 4.49 -4.55) (v (side*2.25) 4.49 4.60) 0.075 alloy

    let private boardingHatch (b: MeshBuilder) side =
        let center = v (side*4.20) 0.52 9.45
        let ring dx radius =
            ProceduralMeshes.ring(12,0.0,0.0,0.0,radius,radius)
            |> Array.map (fun p -> center+v (side*dx) p.Y p.X)
        let outer,rim,door = ring 0.53 1.22,ring 0.53 0.98,ring 0.34 0.94
        b.Loft([|ring -0.35 1.42;outer|],true,false,teal)
        for i in 0..11 do
            let j = (i+1)%12
            b.PolygonOutward([|outer.[i];outer.[j];rim.[j];rim.[i]|],center,ivory)
            b.PolygonOutward([|rim.[i];rim.[j];door.[j];door.[i]|],outer.[i],rubber)
        b.PolygonOutward(door,center,steel)
        b.Box(center+v (side*0.36) 0.0 0.0,v 0.018 0.89 0.045,rubber)
        for z in [-0.34;0.34] do
            b.Box(center+v (side*0.39) 0.23 z,v 0.035 0.29 0.16,glass)

    /// Two passenger decks sit above a teal keel. Swept pylons leave daylight
    /// between the tail and the engine nacelles when the liner banks past.
    let liner () =
        let b = MeshBuilder()
        shell b 0.0 [|-23.5,0.48,-0.89,-0.28; -18.5,2.9,-2.15,0.58; -10.0,4.15,-2.9,1.12
                      7.5,4.35,-3.0,1.28; 14.7,3.32,-2.4,0.85; 19.9,1.58,-1.45,0.52|] teal
        shell b 0.0 [|-22.4,0.82,-0.40,0.18; -14.9,3.5,-0.40,2.0; -9.7,4.26,-0.35,2.68
                      8.9,4.36,-0.38,2.72; 16.1,2.9,-0.35,1.4; 19.4,1.45,-0.42,0.58|] ivory
        shell b 0.0 [|-18.2,2.35,0.78,1.22; -14.0,3.53,1.12,2.26; -10.4,3.95,1.28,2.65|] glass
        shell b 0.0 [|-9.6,2.6,2.25,2.85; -6.7,3.29,2.52,4.20; 6.3,3.3,2.53,4.18; 12.3,2.17,1.87,2.97|] ivory
        passengerGallery b
        for side in [-1.0;1.0] do
            boardingHatch b side
            for i in 0..8 do
                let z = -9.9 + float i*2.17
                let x = 4.28 + (z+9.9)*0.0054
                b.Box(v (side*x) 1.15 z,v 0.12 0.42 0.81, if i%4=0 then cabin else glass)
            for i in 0..5 do
                b.Box(v (side*3.30) 3.35 (-6.3+float i*2.35),v 0.08 0.26 0.89,glass)
            let wing = [|v (side*2.8) -0.6 2.2; v (side*7.1) -0.88 8.9
                         v (side*7.1) -0.88 13.7; v (side*2.8) -0.6 10.5|]
            b.Slab(wing,[|0.64;0.28;0.32;0.7|],Vec3.up,teal)
            let x = side*6.65
            shell b x [|7.4,0.50,-1.18,-0.56; 10.8,1.54,-2.05,0.55; 15.8,1.92,-2.35,0.8
                        19.5,1.51,-2.12,0.53|] ivory
            for z in [-5.2;0.0;5.2] do
                // Lifeboats hang in contrasting cradles below the passenger windows.
                let davitX = side*4.38
                b.Box(v (side*4.02) -1.52 z,v 0.48 0.72 1.56,steel)
                shell b davitX [|z-1.84,0.24,-1.91,-1.48; z-1.16,0.74,-2.45,-1.11;
                                 z+1.12,0.74,-2.45,-1.11; z+1.82,0.28,-2.01,-1.48|] ochre
                for dz in [-0.78;0.78] do
                    shell b davitX [|z+dz,0.77,-2.50,-1.05; z+dz+0.14,0.77,-2.50,-1.05|] rubber
            let cowling z y w = v (x+side*w) y z
            ProceduralMeshes.armorPanel b
                [|cowling 11.4 0.49 0.80; cowling 16.0 0.78 0.83; cowling 17.9 0.67 0.08; cowling 13.0 0.62 -0.13|]
                Vec3.up 0.10 teal steel
        let fin = [|v 0.0 1.05 9.9; v 0.0 6.15 16.3; v 0.0 6.4 18.0; v 0.0 0.6 17.3|]
        b.Slab(fin,[|0.48;0.16;0.16;0.32|],Vec3.unitX,teal)
        for nozzle,radius in linerDrives do drive b nozzle radius
        navLights b linerLights
        b.Build()
