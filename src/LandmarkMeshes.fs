namespace SpaceEngine

open System

[<RequireQualifiedAccess>]
module LandmarkMeshes =
    let private tau = 2.0 * Math.PI
    let private polar a r z = Vec3(r * cos a, r * sin a, z)
    let private graphite = MeshColor.rgb 0.045 0.06 0.08
    let private steel = MeshColor.rgb 0.125 0.155 0.175
    let private ceramic = MeshColor.rgb 0.33 0.36 0.36
    let private copper = MeshColor.rgb 0.42 0.20 0.07
    let private ion = MeshColor.glow 0.23 0.88 1.0 0.93
    let private lamp = MeshColor.glow 1.0 0.45 0.09 0.94

    // Achtkantprofil mit echten Fasen. Auch im Streiflicht bleibt die Tiefe der Schienen sichtbar.
    let private rail (b: MeshBuilder) a0 a1 inner outer halfDepth bevel color =
        let rib a =
            [| polar a (inner + bevel) -halfDepth; polar a (outer - bevel) -halfDepth
               polar a outer (-halfDepth + bevel); polar a outer (halfDepth - bevel)
               polar a (outer - bevel) halfDepth; polar a (inner + bevel) halfDepth
               polar a inner (halfDepth - bevel); polar a inner (-halfDepth + bevel) |]
        b.Loft([| rib a0; rib ((a0 + a1) * 0.5); rib a1 |], true, true, color)

    /// TG-9. Die drei großen Injektoren umschließen eine durchsichtige Öffnung in der XY-Ebene.
    let gateFrame () =
        let b = MeshBuilder()
        let step = tau / 18.0
        for i in 0..17 do
            let a = Math.PI / 2.0 + float i * step
            let span = step * 0.5
            rail b (a-span) (a+span) 52.3 61.3 2.8 0.75 graphite
            rail b (a-span+0.009) (a+span-0.009) 51.1 52.5 2.1 0.55 ion
            rail b (a-span+0.012) (a+span-0.012) 55.3 64.0 4.6 1.35 (if i % 3 = 1 then steel else ceramic)
            // Auf beiden Schauseiten eine eingelassene Fuge, Kupferlasche und Statuslichter.
            let plate r0 r1 a0 a1 z thickness color =
                let face = [| polar a0 r0 z; polar a0 r1 z; polar a1 r1 z; polar a1 r0 z |]
                let n = if z > 0.0 then Vec3.unitZ else -Vec3.unitZ
                ProceduralMeshes.armorPanel b face n thickness color graphite
            for side in [-1.0; 1.0] do
                plate 57.3 59.0 (a-span+0.029) (a+0.045) (side*4.62) 0.18 graphite
                plate 57.6 58.5 (a-span+0.042) (a-0.022) (side*4.85) 0.12 ion
                plate 59.5 62.1 (a+0.071) (a+0.097) (side*4.62) 0.28 copper
                plate 59.5 62.1 (a+0.109) (a+0.121) (side*4.62) 0.26 copper
            if i % 3 = 0 then
                // Keilförmige Polschuhe: drei große Sender, dazwischen drei kleinere Rückleiter.
                let large = i % 6 = 0
                let tip, root, width = if large then 43.0, 70.0, 4.6 else 47.5, 65.8, 2.8
                let local r t z = polar a r z + Vec3(-sin a * t, cos a * t, 0.0)
                let socket r w depth =
                    [| local r -w -depth; local r w -depth; local r w depth; local r -w depth |]
                b.Loft([| socket tip (width*0.45) 2.1; socket 53.0 width 6.9
                          socket 61.0 (width*1.28) 8.2; socket root (width*0.63) 4.8 |], true, true, graphite)
                for side in [-1.0; 1.0] do
                    let cheek r w z = local r w (side*z)
                    let face = [| cheek 48.5 0.0 4.9; cheek 55.0 -width 7.6; cheek 61.0 -width 8.5
                                  cheek root 0.0 5.3; cheek 61.0 width 8.5; cheek 55.0 width 7.6 |]
                    ProceduralMeshes.armorPanel b face (Vec3.unitZ * side) 0.7 steel copper
                    ProceduralMeshes.beam b (cheek 50.0 0.0 6.2) (cheek 60.5 0.0 9.35) 0.72 ion
                    ProceduralMeshes.beam b (cheek 63.2 (-width*0.34) 8.5) (cheek 63.2 (width*0.34) 8.5) 0.57 lamp
                b.Loft([| socket (tip-0.85) (width*0.30) 1.25; socket tip (width*0.40) 1.8 |],true,true,ion)
        b.Build()

    /// Gewölbte Membran mit durchsichtigem Zentrum und geschwungenen Interferenzsäumen.
    /// Beide Wicklungen liefern bei aktivem Backface-Culling genau eine additive Lage pro Blickseite.
    let gateHorizon () =
        let b = MeshBuilder()
        let segments = 72
        let radii = [| 0.0; 0.17; 0.32; 0.48; 0.63; 0.76; 0.86; 0.93; 0.973; 0.99; 1.0 |]
        let sample u i =
            let a = tau * float (i % segments) / float segments
            let wave = 0.55 * sin (3.0*a + 4.0*u) + 0.35 * sin (5.0*a - 3.0*u)
            let r = 52.0 * (u + 0.040 * u * (1.0-u) * wave)
            let arc = (0.5 + 0.5 * cos (3.0*a + 7.6*u + 0.72*sin (2.0*a - 4.0*u))) ** 9.0
            let fringe = u ** 9.0
            let veil = u*u * (0.075 + 0.13*arc)
            let c = MeshColor.glow (veil*0.12 + fringe*0.13) (veil*0.57 + fringe*0.80) (veil*1.4 + fringe*0.98) 1.0
            polar a r (-4.2*(1.0-u*u) + 1.5*wave*u*(1.0-u)), c
        let triangle (p,c) (q,d) (r,e) =
            b.TriangleColors(p,q,r,c,d,e)
            b.TriangleColors(p,r,q,c,e,d)
        let rings = radii |> Array.map (fun u -> Array.init segments (sample u))
        for k in 1..rings.Length-1 do
            for i in 0..segments-1 do
                let j = (i+1) % segments
                triangle rings.[k-1].[i] rings.[k].[i] rings.[k].[j]
                if k > 1 then triangle rings.[k-1].[i] rings.[k].[j] rings.[k-1].[j]
        // Drei schmale Wellenkämme laufen zum Rand hin aus; ihre Ränder blenden weich in die Membran.
        for arm in 0..2 do
            let ridge i edge =
                let t = float i / 36.0
                let fade = sin (Math.PI * t)
                let a = float arm * tau / 3.0 + 1.6*t + 0.20*sin (4.0*t + float arm)
                let u = 0.48 + 0.47*t + 0.028*sin (4.5*t) + edge * (0.006 + 0.015*fade)
                let light = fade * (1.0 - abs edge)
                polar a (52.0*u) (-4.2*(1.0-u*u)), MeshColor.glow (0.14*light) ((0.19+0.17*t)*light) (0.55*light) 1.0
            for i in 0..35 do
                for edge in [-1.0; 0.0] do
                    let p,q = ridge i edge, ridge (i+1) edge
                    let r,s = ridge (i+1) (edge+1.0), ridge i (edge+1.0)
                    triangle p q r
                    triangle p r s
        b.Build()

    let gateRotation (sector: SectorDef) (gate: JumpGate) =
        // Beim Zollkreuzer beginnt die Panzerung rund 20 m vor der Torebene (+Z).
        if sector.Id = "slag-refinery" && gate.TargetSectorId = "kepler-yard" then Quat.identity
        else Quat.lookRotation (gate.Position - sector.Outpost.Position) Vec3.up

    let sovereignPosition (sector: SectorDef) = sector.Outpost.Position + Vec3(-505.0,115.0,-945.0)
    let sovereignRotation =
        Quat.fromAxisAngle Vec3.unitY 0.43 * Quat.fromAxisAngle Vec3.unitZ 0.045 * Quat.fromAxisAngle Vec3.unitX 0.30

    type Models =
        { Frame: InstanceBatch
          Horizon: InstanceBatch
          Gates: (Vec3 * Quat)[][]
          Sovereign: Lazy<InstanceBatch[]>
          Beacons: (Vec3 * MeshColor * float)[] }

    let create renderer =
        let gates =
            // Die Tore zeichnet Main.pushJumpGates (Durchflug-Tore); hier bleibt nur die Sovereign aktiv.
            Universe.sectors |> Array.map (fun _ -> Array.empty<Vec3 * Quat>)
        let capacity = 1
        let batch make count blend = Renderer.createBatch renderer (Renderer.createMesh renderer (make ())) count blend
        let frame = batch gateFrame capacity Opaque
        frame.Shininess <- 56.0
        frame.Specular <- 0.42
        let bastion = Universe.sectors |> Array.find (fun sector -> sector.Id = "morgenlicht-bastion")
        let p = sovereignPosition bastion
        { Frame = frame; Horizon = batch gateHorizon capacity Additive; Gates = gates
          Sovereign = lazy (
              [| SovereignMeshes.hull; SovereignMeshes.regalia |] |> Array.map (fun make ->
                  let b = batch make 1 Opaque
                  b.Shininess <- 52.0
                  b.Specular <- 0.50
                  InstanceBatch.push b p sovereignRotation Vec3.one 1.0 1.0 1.0 0.0
                  b))
          Beacons = SovereignMeshes.beacons |> Array.map (fun (v,c,r) -> p + Quat.rotateVec3 sovereignRotation v, c, r) }

    let pushLights models sectorIndex time lights halo cameraRotation =
        let pulse = 0.94 + 0.06 * sin (time * 1.6)
        for pos, _ in models.Gates.[sectorIndex] do
            Lights.addPoint lights pos.X pos.Y pos.Z 99.0 0.28 0.78 1.0 (1.2 * pulse)
        if Universe.sectors.[sectorIndex].Id = "morgenlicht-bastion" then
            for i in 0..models.Beacons.Length-1 do
                let p, color, radius = models.Beacons.[i]
                let phase = (time * 0.24 + float i * 0.17) * tau
                let intensity = 0.18 + 0.82 * (0.5 + 0.5 * cos phase) ** 6.0
                InstanceBatch.push halo p cameraRotation (Vec3(radius,radius,radius))
                    (color.R*intensity) (color.G*intensity) (color.B*intensity) 1.0

    let drawOpaque renderer models sectorIndex frustum time =
        InstanceBatch.clear models.Frame
        InstanceBatch.clear models.Horizon
        let spin = Quat.fromAxisAngle Vec3.unitZ (time * 0.085)
        for pos, rot in models.Gates.[sectorIndex] do
            if Frustum.intersectsSphere frustum pos.X pos.Y pos.Z 72.0 then
                InstanceBatch.push models.Frame pos rot Vec3.one 1.0 1.0 1.0 0.0
                InstanceBatch.push models.Horizon pos (rot * spin) Vec3.one 1.0 1.0 1.0 1.0
        Renderer.draw renderer models.Frame
        let sector = Universe.sectors.[sectorIndex]
        if sector.Id = "morgenlicht-bastion" then
            let p = sovereignPosition sector
            if Frustum.intersectsSphere frustum p.X p.Y p.Z 760.0 then
                for batch in models.Sovereign.Value do Renderer.draw renderer batch
