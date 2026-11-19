namespace SpaceEngine

open System

/// Civilian services and AETHER patrols follow local timetables on the saved world clock.
[<RequireQualifiedAccess>]
module Traffic =
    let private v x y z = Vec3(x,y,z)
    let private engine = v 0.0 -0.29 6.45
    let private portLight = v -3.86 0.80 1.9
    let private starboardLight = v 3.86 0.80 1.9

    let launch () =
        let b = MeshBuilder()
        // Port launches are as worn as the rest of the belt: grimy white, faded signal paint, blacked-out glass.
        let steel = MeshColor.rgb 0.095 0.115 0.13
        let ivory = MeshColor.rgb 0.40 0.39 0.345
        let orange = MeshColor.rgb 0.50 0.195 0.04
        let glass = MeshColor.glass 0.035 0.045 0.052 0.08
        let shell x profile color =
            b.Loft(profile |> Array.map (fun (z,w,lo,hi) -> ProceduralMeshes.hullSection x z w lo hi), true, true, color)
        // Pushed-out wheelhouse, short keel and two detachable service containers.
        shell 0.0 [|-7.1,0.9,-0.8,-0.2; -4.3,1.76,-1.1,0.84; 0.3,1.95,-1.2,1.0; 5.2,1.3,-0.9,0.76|] ivory
        shell 0.0 [|-5.6,1.15,0.06,0.32; -3.8,1.49,0.55,1.7; -1.2,1.53,0.72,1.85|] glass
        shell 0.0 [|-3.82,1.5,1.62,1.8; -1.25,1.61,1.60,2.03; -0.7,1.48,0.88,1.8|] ivory
        for side in [-1.0;1.0] do
            let x = side*2.6
            shell x [|-2.8,0.75,-0.57,0.58; -1.8,1.27,-1.0,1.1; 3.8,1.27,-1.0,1.1; 4.8,0.88,-0.72,0.68|] orange
            for z in [-0.85;2.95] do
                shell x [|z,1.31,-1.06,1.17; z+0.32,1.31,-1.06,1.17|] steel
            b.Box(v (side*3.88) 0.06 0.98, v 0.045 0.24 0.92, ivory)
            ProceduralMeshes.beam b (v (side*0.8) 1.02 3.8) (v x 1.18 3.1) 0.16 steel
        let nozzle z r = ProceduralMeshes.ring(10,engine.X,engine.Y,z,r,r)
        b.Loft([|nozzle 3.7 1.32; nozzle 5.4 1.20; nozzle engine.Z 0.91|],true,true,steel)
        b.Loft([|nozzle engine.Z 0.71; nozzle (engine.Z+0.03) 0.69|],false,true,MeshColor.glow 0.26 0.78 1.0 0.97)
        b.Box(portLight, v 0.15 0.19 0.49, MeshColor.glow 1.0 0.03 0.02 1.0)
        b.Box(starboardLight, v 0.15 0.19 0.49, MeshColor.glow 0.06 1.0 0.37 1.0)
        b.Build()

    type Route =
        { Points: Vec3[]
          Tangents: Vec3[]
          Ends: float[]
          Period: float
          Offset: float }

    let private timetable (points: Vec3[]) speed phase =
        let n = points.Length
        let tangents = points |> Array.mapi (fun i p ->
            let a,b = points.[(i+n-1)%n], points.[(i+1)%n]
            Vec3.normalize (b-a) * min (Vec3.distance a p) (Vec3.distance p b))
        let mutable duration = 0.0
        let ends = points |> Array.mapi (fun i p ->
            duration <- duration + Vec3.distance p points.[(i+1)%n] / speed
            duration)
        { Points=points; Tangents=tangents; Ends=ends; Period=duration; Offset=phase*duration }

    let routes (sector: SectorDef) =
        let port = sector.Outpost.Position
        let feeders = sector.Gates |> Array.mapi (fun i gate ->
            let delta = gate.Position - port
            let side = if delta.X < 0.0 then -1.0 else 1.0
            let at x y z = port + v (side*x) (y + float i*13.0) z
            // The Kepler feeder at Helios crosses above the customs cruiser's mast.
            let rise =
                if sector.Id = "slag-refinery" && gate.TargetSectorId = "kepler-yard" then CapitalFlight.crown + 26.0
                else 0.0
            let nearGate = gate.Position - Vec3.normalize delta * (gate.TriggerRadius+12.0) + v 0.0 rise 0.0
            let across = Vec3.normalize (Vec3.cross Vec3.up delta) * 52.0
            let points =
                [| yield at 70.0 6.0 49.0
                   yield at 235.0 36.0 57.0
                   yield nearGate+across
                   yield nearGate+v 0.0 40.0 0.0
                   yield nearGate-across
                   // The return leg from Morgenlicht passes outside Kepler's construction gantry.
                   if sector.Id = "kepler-yard" && gate.TargetSectorId = "morgenlicht-bastion" then
                       yield port + v 218.0 76.0 -262.0
                   yield at 259.0 82.0 105.0
                   yield at 113.0 58.0 182.0 |]
            timetable points (18.0+float i*2.5) (0.04+float i*0.37))
        let local =
            [| v -84.0 8.0 63.0; v -147.0 19.0 127.0; v -92.0 51.0 209.0
               v 66.0 48.0 169.0; v 82.0 32.0 70.0; v -28.0 43.0 93.0 |]
            |> Array.map ((+) port)
        Array.append feeders [|timetable local 11.0 0.31|]

    /// Cubic Hermite interpolation with an analytic tangent, including the wrap
    /// through the first stop. No integration or changing a waypoint on a frame.
    let pose (route: Route) time =
        let t = (time + route.Offset) % route.Period
        let i = route.Ends |> Array.findIndex (fun finish -> t < finish)
        let start = if i = 0 then 0.0 else route.Ends.[i-1]
        let u = (t-start) / (route.Ends.[i]-start)
        let u2 = u*u
        let j = (i+1) % route.Points.Length
        let a,b = route.Points.[i], route.Points.[j]
        let m,n = route.Tangents.[i], route.Tangents.[j]
        let position = a + (b-a)*(u2*(3.0-2.0*u)) + m*(u*(1.0-u)*(1.0-u)) + n*(u2*(u-1.0))
        let heading = (b-a)*(6.0*u*(1.0-u)) + m*(3.0*u2-4.0*u+1.0) + n*(3.0*u2-2.0*u)
        position, Quat.lookRotation heading Vec3.up

    type ShipClass = Shuttle = 0 | Tug = 1 | Liner = 2 | Frigate = 3 | Escort = 4

    type ShipModel =
        { Name: string; Aether: bool; Radius: float; Mesh: unit -> MeshData
          Drives: (Vec3 * float)[]; Lamps: Vec3[] }

    let models =
        [| { Name="Hafenshuttle"; Aether=false; Radius=7.2; Mesh=launch
             Drives=[|engine,0.85|]; Lamps=[|portLight;starboardLight|] }
           { Name="Bergungsschlepper"; Aether=false; Radius=18.9; Mesh=CivilTrafficMeshes.tug
             Drives=CivilTrafficMeshes.tugDrives; Lamps=CivilTrafficMeshes.tugLights }
           { Name="Passagierliner"; Aether=false; Radius=24.1; Mesh=CivilTrafficMeshes.liner
             Drives=CivilTrafficMeshes.linerDrives; Lamps=CivilTrafficMeshes.linerLights }
           { Name="Patrouillenfregatte"; Aether=true; Radius=41.1; Mesh=AetherTrafficMeshes.frigate
             Drives=AetherTrafficMeshes.frigateDrives
             Lamps=AetherTrafficMeshes.frigateLights }
           { Name="Eskortkorvette"; Aether=true; Radius=16.9; Mesh=AetherTrafficMeshes.escort
             Drives=AetherTrafficMeshes.escortDrives; Lamps=AetherTrafficMeshes.escortLights } |]

    type Flight = { Class: ShipClass; Lane: Route; WingOffset: Vec3 }

    let private schedules (sector: SectorDef) =
        let port = sector.Outpost.Position
        let flight kind lane offset = { Class=kind; Lane=lane; WingOffset=offset }
        let single kind lane = flight kind lane Vec3.zero
        let local points speed phase = timetable (points |> Array.map ((+) port)) speed phase
        let tugLane () =
            local [|v -110.0 -38.0 166.0; v -215.0 -52.0 90.0; v -295.0 -64.0 -40.0
                    v -200.0 -69.0 -40.0; v -108.0 -62.0 22.0; v -80.0 -44.0 96.0|] 12.5 0.02
        let linerLane () =
            // An express bypass above the shuttle feeders, turning back short of the first gate.
            let gate = sector.Gates.[0].Position
            let delta = gate-port
            let along = Vec3.normalize (v delta.X 0.0 delta.Z)
            let across = Vec3.cross along Vec3.up
            let length = sqrt (delta.X*delta.X + delta.Z*delta.Z)
            let at d x y = along*d + across*x + Vec3.up*y
            local [|at -55.0 88.0 151.0; at 80.0 138.0 161.0; at (length*0.66) 98.0 175.0
                    at (length*0.78) 8.0 177.0; at (length*0.65) -100.0 188.0
                    at 55.0 -128.0 173.0; at -98.0 -40.0 159.0|] 26.0 0.05
        let patrolLane () =
            local [|v -175.0 256.0 274.0; v -385.0 276.0 45.0; v -297.0 293.0 -223.0
                    v 35.0 310.0 -354.0; v 351.0 287.0 -182.0; v 308.0 264.0 101.0
                    v 95.0 246.0 295.0|] 32.0 0.015
        [| for lane in routes sector do yield single ShipClass.Shuttle lane
           match sector.Outpost.Kind with
           | MiningDepot | Refinery | OrbitalYard -> yield single ShipClass.Tug (tugLane ())
           | _ -> ()
           match sector.Id with
           | "outer-belt" | "frontier-drift" | "kepler-yard" | "aether-relay" | "morgenlicht-bastion" ->
               yield single ShipClass.Liner (linerLane ())
           | "schleierpass" -> yield single ShipClass.Tug (tugLane ())
           | _ -> ()
           match sector.Id with
           | "morgenlicht-bastion" | "aether-relay" | "slag-refinery" ->
               let patrol = patrolLane ()
               yield single ShipClass.Frigate patrol
               yield flight ShipClass.Escort patrol (v -64.0 8.0 66.0)
               if sector.Id = "morgenlicht-bastion" then
                   yield flight ShipClass.Escort patrol (v 64.0 8.0 66.0)
           | "outer-belt" | "kepler-yard" | "kaltstrom" ->
               yield single ShipClass.Escort (patrolLane ())
           | _ -> () |]

    let manifests = Array.map schedules Universe.sectors
    let capacity = manifests |> Array.map Array.length |> Array.max
    let driveCapacity =
        manifests |> Array.map (Array.sumBy (fun flight -> models.[int flight.Class].Drives.Length)) |> Array.max
    let lampCapacity = driveCapacity + 2*capacity

    let shipPose flight time =
        let p,q = pose flight.Lane time
        if flight.WingOffset = Vec3.zero then p,q
        else p + Quat.rotateVec3 q flight.WingOffset,q

    // Sample each swept hull only when its sector is first populated. Half a
    // second of travel fits within the extra clearance on either side of a sample.
    let private clearances = manifests |> Array.map (fun flights -> lazy (
        flights |> Array.map (fun flight ->
            let count = int (ceil (flight.Lane.Period*2.0))
            let points = Array.init count (fun i -> fst (shipPose flight (float i*flight.Lane.Period/float count)))
            models.[int flight.Class].Radius + 17.0, points)))

    let inShippingLane sector point rockRadius =
        clearances.[sector].Value |> Array.exists (fun (radius,points) ->
            let clearance = radius + rockRadius
            points |> Array.exists (fun p -> Vec3.distanceSq point p <= clearance*clearance))

    type Fleet = { Hulls: InstanceBatch[]; Positions: Vec3[]; Rotations: Quat[] }

    let create renderer =
        let hulls = models |> Array.mapi (fun i model ->
            let count = manifests |> Array.map (Array.sumBy (fun f -> if int f.Class = i then 1 else 0)) |> Array.max
            let batch = Renderer.createBatch renderer (Renderer.createMesh renderer (model.Mesh ())) count Opaque
            batch.Shininess <- if model.Aether then 50.0 else 36.0
            batch.Specular <- if model.Aether then 0.39 else 0.26
            batch)
        { Hulls=hulls; Positions=Array.create capacity Vec3.zero; Rotations=Array.create capacity Quat.identity }

    let push fleet sector time eye flame halo =
        for hull in fleet.Hulls do InstanceBatch.clear hull
        if sector >= 0 then
            let flights = manifests.[sector]
            for i in 0..flights.Length-1 do
                let flight = flights.[i]
                let p,q = shipPose flight time
                fleet.Positions.[i] <- p
                fleet.Rotations.[i] <- q
                let distance = Vec3.distance eye p
                if distance < 1600.0 then
                    let model = models.[int flight.Class]
                    InstanceBatch.push fleet.Hulls.[int flight.Class] p q Vec3.one 1.0 1.0 1.0 0.0
                    let flare = 1.0 + 0.08*sin (time*3.2 + float i)
                    let lamp = min 2.8 (1.1 + distance*0.002)
                    let red,green,blue = if model.Aether then 1.2,0.53,0.16 else 0.20,0.64,1.15
                    for nozzle,radius in model.Drives do
                        flame p q nozzle (radius*5.4*flare) (radius*1.3) red green blue
                        halo p q nozzle (max lamp (radius*1.8)) red green blue
                    if model.Aether then
                        let phase = (time*0.8 + float i*0.37) % 1.0
                        let pulse = if phase < 0.07 || (phase > 0.14 && phase < 0.20) then 1.0 else 0.12
                        for light in model.Lamps do halo p q light (lamp*1.45) (1.4*pulse) (0.05*pulse) (0.025*pulse)
                    else
                        halo p q model.Lamps.[0] lamp 0.93 0.035 0.02
                        halo p q model.Lamps.[1] lamp 0.035 0.84 0.22
