namespace SpaceEngine

open System

/// Surface geology is baked once per sector. The saved spin axis supplies a second
/// silhouette for each saved variant, even after a pool swap or reload.
[<RequireQualifiedAccess>]
module AsteroidMeshes =
    let variant saved axisX =
        let family = if saved < 4 then saved else saved + 4
        if axisX < 0.0 then family + 4 else family

    let private palette kind =
        match kind with
        | MiningDepot    -> Vec3(0.28,0.25,0.23), Vec3(0.79,0.66,0.52), Vec3(1.08,0.91,0.71)
        | Refinery       -> Vec3(0.20,0.17,0.20), Vec3(0.62,0.36,0.28), Vec3(1.09,0.58,0.34)
        | FrontierBazaar -> Vec3(0.18,0.29,0.27), Vec3(0.63,0.69,0.54), Vec3(0.98,0.89,0.70)
        | OrbitalYard    -> Vec3(0.17,0.35,0.46), Vec3(0.59,0.80,0.89), Vec3(0.95,1.06,1.09)
        | ResearchRelay  -> Vec3(0.26,0.19,0.30), Vec3(0.65,0.51,0.67), Vec3(0.95,0.77,0.82)
        | FleetDock      -> Vec3(0.22,0.26,0.30), Vec3(0.57,0.57,0.59), Vec3(0.88,0.72,0.61)

    let private tau = 2.0 * Math.PI
    let private sides = 16
    // Thin resistant beds have their own upper and lower edges.
    let private levels = [|-0.93; -0.76; -0.55; -0.46; -0.18; 0.04; 0.14; 0.38; 0.59; 0.68; 0.82; 0.94|]

    // Radius of a regular polygon at this bearing. Sharp shoulders are defined
    // by the mesh itself and survive the far-field shading cutoff.
    let private facets count a =
        let step = tau / float count
        let local = a - floor (a / step) * step - step * 0.5
        cos (step * 0.5) / cos local

    let private newSilhouette index a h lobe bed =
        let cosA, sinA = cos a, sin a
        let round = sqrt (1.0 - h*h)
        let block = Math.Pow(1.0 - h ** 4.0, 0.42)
        match index with
        | 8 -> // dense iron with broad, peeled-off faces
            let width = block * facets 5 a * (0.90 + 0.10*lobe)
            Vec3(min (0.58 + 0.26*h) (width*cosA), h*0.93 + width*sinA*0.16, width*sinA*0.91)
        | 9 -> // porous, tapered carbonaceous rock
            let width = round * lobe * (1.0 + 0.42*h + 0.14*bed)
            Vec3(width*cosA+h*0.29, h*1.15 + 0.09*sin (a*5.0)*width, width*sinA*0.83)
        | 10 -> // thin, fractured ice shelf
            let width = block * facets 6 (a+0.22)
            Vec3(width*cosA + h*0.06, h*0.28 + 0.13*width*cosA, width*sinA*0.71 + 0.10*h)
        | 11 -> // oblique glacial needle with cleavage facets
            let width = (0.94 - 0.72*abs (h+0.10)) * facets 4 (a+0.35*h)
            Vec3(width*cosA*0.58 + h*0.52, h*1.42 + width*sinA*0.17, width*sinA*0.59)
        | 12 -> // a rubble pile with an off-centre smaller lobe
            let bulge mid radius =
                let d = (h-mid) / radius
                sqrt (max 0.0 (1.0 - d*d))
            let width = 0.12*round + max (0.88*bulge (-0.36) 0.61) (0.57*bulge 0.61 0.46)
            let offset = max 0.0 h * 0.37
            Vec3(width*lobe*cosA + offset, h*0.98 + 0.07*width*sinA, width*lobe*sinA*0.80 - offset*0.25)
        | 13 -> // a broad basalt wedge with a broken diagonal end
            let width = block * facets 4 (a+0.61)
            let x = width*cosA
            Vec3(x*1.18 + h*0.32, h*0.52 + 0.36*x, min (0.21 - x*0.39 - h*0.16) (width*sinA*0.86))
        | 14 -> // a round impact body with one dominant basin
            let width = round * (0.94 + 0.06*lobe)
            Vec3(width*cosA*1.07, h*0.81+0.13*width*cosA, width*sinA)
        | _ -> // frost-covered breccia with a jagged, asymmetrical rim
            let width = round * facets 7 a * (lobe + 0.19*sin (5.0*a + h*2.5))
            Vec3(width*cosA*1.18, h*0.56 + 0.22*width*sinA + 0.15*sin (a*3.0)*width, width*sinA*0.96 - 0.20*h)

    let private rockPalette kind index =
        let dark, stone, pale = palette kind
        // The polar bands and exposed faces are geological detail. Ore stays
        // invisible until the rock breaks, whatever its shape or material.
        match index with
        | 8 -> Vec3(0.19,0.18,0.20), Vec3.lerp (Vec3(0.39,0.43,0.48)) stone 0.24, Vec3(0.81,0.81,0.83)
        | 9 -> Vec3(0.12,0.105,0.099), Vec3.lerp (Vec3(0.32,0.27,0.22)) stone 0.20, Vec3(0.56,0.47,0.39)
        | 10 -> Vec3(0.28,0.51,0.65), Vec3.lerp (Vec3(0.64,0.81,0.87)) stone 0.16, Vec3(0.94,1.02,1.04)
        | 11 -> Vec3(0.12,0.29,0.43), Vec3.lerp (Vec3(0.39,0.64,0.75)) stone 0.20, Vec3(0.79,0.92,0.99)
        | 13 -> Vec3(0.22,0.13,0.12), Vec3.lerp (Vec3(0.60,0.32,0.20)) stone 0.21, Vec3(0.86,0.61,0.40)
        | 14 -> Vec3(0.22,0.23,0.25), Vec3.lerp (Vec3(0.57,0.57,0.57)) stone 0.22, Vec3(0.82,0.82,0.81)
        | 15 -> Vec3(0.27,0.24,0.27), Vec3.lerp (Vec3(0.62,0.64,0.65)) stone 0.26, Vec3(0.93,0.91,0.85)
        | _ -> dark, stone, pale

    let rock kind index =
        let seed = float index * 2.37
        let dark, stone, pale = rockPalette kind index
        let sx, sy, sz =
            match index with
            | 0 -> 1.08,0.91,1.0     // impact basin
            | 1 -> 1.23,0.53,0.94    // bedding slab
            | 2 -> 1.44,0.81,0.82    // contact binary
            | 3 -> 0.78,1.31,0.78    // standing shard
            | 4 -> 0.96,0.76,1.18    // broken wedge
            | 5 -> 1.18,0.92,0.74    // breccia block
            | 6 -> 0.85,0.51,1.25    // splinter
            | _ -> 1.13,1.06,0.88    // irregular boulder
        let top = levels.Length - 1
        let geology (p: Vec3) a h bed =
            match kind with
            | MiningDepot -> p
            | Refinery -> p * (0.91 + 0.09 * min 1.0 (abs (sin (3.0*a + h + seed)) * 3.2))
            | FrontierBazaar -> Vec3(p.X*1.09, p.Y*(0.85 + 0.19*abs p.X), p.Z)
            | OrbitalYard ->
                let bevel value limit = value - 0.79 * max 0.0 (value - limit)
                Vec3(-bevel (-p.X) 0.80, bevel p.Y (0.64 - 0.19*p.X), bevel p.Z (0.77 + 0.18*h))
            | ResearchRelay -> Vec3(p.X*(1.0 + 0.09*bed), p.Y*0.93, p.Z*(1.0 + 0.09*bed))
            | FleetDock -> p * (1.0 - 0.12 * max 0.0 (sin (4.0*a + seed + h) * cos (5.0*h)))
        let surface u row =
            let k = min (top-1) (int row)
            let h = levels.[k] + (levels.[k+1] - levels.[k]) * (row - float k)
            let a = u * tau / float sides + float (index%3)*0.42 + 0.07 * sin (h * 4.4 + seed) + 0.075 * sin (u * tau / 8.0 + h + seed)
            let lobe = 1.0 + 0.14*cos (3.0*a + h + seed) + 0.075*sin (5.0*a - 2.4*h + seed)
            let bed = max 0.0 (1.0 - abs (row - 5.9)) + 0.65 * max 0.0 (1.0 - abs (row - 2.9))
            let p =
                if index >= 8 then newSilhouette index a h lobe bed
                else
                    let width = sqrt (1.0 - h*h) * (lobe + 0.095 * bed * (0.6 + 0.4*cos (a+seed)))
                    let x,z = width * cos a, width * sin a
                    let waist = if index = 2 then 0.52 + 0.64 * min 1.0 (abs (x+0.09) * 1.9) else 1.0
                    let cut = if index = 4 || index = 6 then 0.47 else 0.79
                    let px = min (cut - 0.25*h + 0.14*z) (x + 0.15*h)
                    let py = h * waist + 0.17*x - 0.13*z + 0.095*sin (2.0*a+seed) * width + 0.08*x*z
                    Vec3(px*sx, py*sy, (z*waist - 0.16*h)*sz)
            geology p a h bed

        let b = MeshBuilder()
        let shade (p: Vec3) exposure =
            let mottling = 0.12 * sin (5.0*p.X + 2.0*p.Z + seed) + 0.10 * sin (8.0*p.Z - 3.0*p.Y)
            let t = max 0.0 (min 1.45 (exposure + mottling))
            let c = if t < 0.8 then Vec3.lerp dark stone (t / 0.8) else Vec3.lerp stone pale ((t-0.8)/0.65)
            MeshColor.rgb c.X c.Y c.Z
        let triangle a c d exposure = b.TriangleColors(a,c,d,shade a exposure,shade c exposure,shade d exposure)
        let quad a c d e exposure =
            triangle a c d exposure
            triangle a d e exposure
        let grid = Array.init levels.Length (fun k -> Array.init sides (fun i -> surface (float i) (float k)))
        let point i k = grid.[k].[i % sides]

        // Remove complete patches of crust. The crater apron shares every edge with
        // the surrounding grid, including on the longitudinal seam.
        let scars =
            match index with
            | 8 -> [| (2,4,4,4); (11,2,3,3) |]
            | 9 -> [| (0,1,2,3); (5,2,2,2); (9,6,2,2); (13,4,2,3); (4,7,2,3) |]
            | 10 -> [| (2,5,2,3) |]
            | 11 -> Array.empty
            | 12 -> [| (3,2,3,3); (11,7,3,3) |]
            | 13 -> [| (0,3,3,3); (9,4,3,4) |]
            | 14 -> [| (2,3,7,6); (12,5,3,3) |]
            | 15 -> [| (0,4,3,3); (5,2,2,4); (10,5,3,4); (13,1,2,3) |]
            | _ -> [| (2,3,3 + index%2,6); (9,2 + index%2,3,4); (13,7,2,3) |]
        for k in 0..top-1 do
            for i in 0..sides-1 do
                if not (scars |> Array.exists (fun (x,y,w,h) -> i >= x && i < x+w && k >= y && k < y+h)) then
                    let bed =
                        if k = 2 || k = 5 || k = 8 then 0.42 * sin (float i*tau/float sides + seed + float k)
                        elif k = 4 then -0.18 else 0.0
                    quad (point i k) (point i (k+1)) (point (i+1) (k+1)) (point (i+1) k) (0.81 + bed)

        for x,y,w,h in scars do
            let edge =
                [| for j in 0..h-1 do yield x,y+j
                   for j in 0..w-1 do yield x+j,y+h
                   for j in 0..h-1 do yield x+w,y+h-j
                   for j in 0..w-1 do yield x+w-j,y |]
            let cx,cy = float x + float w*0.48, float y + float h*0.47
            let centre = surface cx cy
            let normal = Vec3.normalize (Vec3.cross (surface cx (cy+0.1) - centre) (surface (cx+0.1) cy - centre))
            let rim fraction relief =
                edge |> Array.map (fun (i,j) ->
                    let a = atan2 ((float j-cy)/float h) ((float i-cx)/float w)
                    let chipped = 1.0 + 0.095 * sin (3.0*a + seed) + 0.065 * cos (5.0*a)
                    let f = fraction * chipped
                    surface (cx + cos a * float w*f) (cy + sin a * float h*f) + normal * (relief * chipped))
            let crest = rim 0.40 0.042
            let lip = rim 0.32 0.022
            let foot = rim 0.20 -0.096
            let floor = centre - normal*0.117
            for j in 0..edge.Length-1 do
                let n = (j+1) % edge.Length
                quad (point (fst edge.[j]) (snd edge.[j])) (point (fst edge.[n]) (snd edge.[n])) crest.[n] crest.[j] 0.88
                quad crest.[j] crest.[n] lip.[n] lip.[j] (1.11 + 0.16*sin (float j+seed))
                quad lip.[j] lip.[n] foot.[n] foot.[j] (0.62 + 0.08*cos (float j+seed))
                triangle foot.[j] foot.[n] floor 0.44

        let cap row height =
            let middle = Array.fold (+) Vec3.zero grid.[row] / float sides
            middle + Vec3.unitY * (height * sy)
        let bottom, crown = cap 0 -0.045, cap top 0.045
        for i in 0..sides-1 do
            triangle (point i 0) (point (i+1) 0) bottom 0.62
            triangle (point (i+1) top) (point i top) crown 0.94
        let mesh = b.Build()
        let v = mesh.Vertices
        let mutable radiusSq = 0.0
        for i in 0..Layout.VertexFloats..v.length-1 do
            radiusSq <- max radiusSq (float v.[i] ** 2.0 + float v.[i+1] ** 2.0 + float v.[i+2] ** 2.0)
        let scale = float32 (0.999999 / sqrt radiusSq)
        for i in 0..Layout.VertexFloats..v.length-1 do
            for axis in 0..2 do v.[i+axis] <- v.[i+axis] * scale
        mesh

    let batches renderer capacity (sector: SectorDef) =
        let kind = sector.Outpost.Kind
        let sectorMaterial =
            match kind with
            | OrbitalYard -> 2.0, 56.0, 0.43
            | FleetDock -> 3.0, 38.0, 0.38
            | _ -> 1.0, 12.0, 0.06
        lazy (Array.init 16 (fun index ->
            let batch = Renderer.createBatch renderer (Renderer.createMesh renderer (rock kind index)) capacity Opaque
            let material, shine, reflection =
                match index with
                | 8 -> 3.0, 47.0, 0.46
                | 10 | 11 -> 2.0, 56.0, 0.43
                | i when i >= 8 -> 1.0, 12.0, 0.06
                | _ -> sectorMaterial
            batch.Shininess <- shine
            batch.Specular <- reflection
            batch.Material <- material
            batch))
