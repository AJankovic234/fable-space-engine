namespace SpaceEngine

open System

/// Civilian service launches. Each timetable and its geometry are fixed in world space.
[<RequireQualifiedAccess>]
module Traffic =
    let private v x y z = Vec3(x,y,z)
    let private engine = v 0.0 -0.29 6.45
    let private portLight = v -3.86 0.80 1.9
    let private starboardLight = v 3.86 0.80 1.9
    let capacity = 1 + (Universe.sectors |> Array.map (fun sector -> sector.Gates.Length) |> Array.max)

    let launch () =
        let b = MeshBuilder()
        let steel = MeshColor.rgb 0.18 0.24 0.28
        let ivory = MeshColor.rgb 0.86 0.87 0.77
        let orange = MeshColor.rgb 0.94 0.39 0.075
        let glass = MeshColor.glow 0.10 0.30 0.37 0.28
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
            timetable [|at 70.0 6.0 49.0; at 235.0 36.0 57.0; nearGate+across
                        nearGate+v 0.0 40.0 0.0; nearGate-across; at 259.0 82.0 105.0
                        at 113.0 58.0 182.0|] (18.0+float i*2.5) (0.04+float i*0.37))
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

    type Fleet = { Hulls: InstanceBatch; Routes: Route[][] }

    let create renderer =
        let hulls = Renderer.createBatch renderer (Renderer.createMesh renderer (launch ())) capacity Opaque
        hulls.Shininess <- 36.0
        hulls.Specular <- 0.26
        { Hulls=hulls; Routes=Array.map routes Universe.sectors }

    let push fleet sector time eye flame halo =
        InstanceBatch.clear fleet.Hulls
        if sector >= 0 then
            let lanes = fleet.Routes.[sector]
            for i in 0..lanes.Length-1 do
                let p,q = pose lanes.[i] time
                let distance = Vec3.distance eye p
                if distance < 1600.0 then
                    let blue = if i = lanes.Length-1 then 0.76 else 1.0
                    InstanceBatch.push fleet.Hulls p q Vec3.one 1.0 1.0 blue 0.0
                    let flare = 1.0 + 0.09*sin (time*3.2 + float i)
                    flame p q engine (4.6*flare) 1.10 0.20 0.64 1.15
                    let lamp = min 2.8 (1.1 + distance*0.002)
                    halo p q engine (lamp*1.45) 0.25 0.70 1.0
                    halo p q portLight lamp 0.93 0.035 0.02
                    halo p q starboardLight lamp 0.035 0.84 0.22
