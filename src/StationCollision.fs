namespace SpaceEngine

/// Coarse, static hulls in the same metre coordinates as StationMeshes.
[<RequireQualifiedAccess>]
module StationCollision =
    type private Body =
        | Sphere of center: Vec3 * radius: float
        | Box of center: Vec3 * half: Vec3 * axisX: Vec3 * axisY: Vec3 * axisZ: Vec3

    type Hull = private { Bodies: Body[]; Port: Vec3; Bounds: (Vec3 * float)[] }

    let private v x y z = Vec3(x,y,z)
    let private box x y z hx hy hz =
        Box(v x y z, v hx hy hz, Vec3.unitX, Vec3.unitY, Vec3.unitZ)

    let private beam (start: Vec3) (finish: Vec3) width =
        let span = finish - start
        let axis = Vec3.normalize span
        let reference = if abs axis.Y > 0.9 then Vec3.unitX else Vec3.unitY
        let right = Vec3.normalize (Vec3.cross axis reference)
        Box((start+finish)*0.5, v width width (Vec3.length span*0.5), right, Vec3.cross axis right, axis)

    let private houseboat x y z yaw length radius =
        Box(v x y z, v (0.88*length) radius radius,
            v (cos yaw) 0 (sin yaw), Vec3.unitY, v (-sin yaw) 0 (cos yaw))

    let private dish hub direction radius =
        let axis = Vec3.normalize direction
        let u = Vec3.normalize (Vec3.cross Vec3.up axis)
        Box(hub + axis*(0.20*radius), v (0.85*radius) (0.85*radius) (0.20*radius), u, Vec3.cross axis u, axis)

    let private bodies = function
        | MiningDepot ->
            // Quarry rock, winch house, silo platform and the two ore bins.
            [| Sphere(v -116 38 -113, 49.)
               box -13 -1 -113 34 29 21; box 85 -20 -118 53 11 22
               box 65 19 -119 18 38 17; box 112 13 -119 18 30 17
               box -11 -64 -125 14 28 14; box 68 -61 -125 18 22 18
               for struct (a,c) in StationMeshes.quarryClamps do
                   beam a c 5.0
                   beam (a-v 0 0 20) (c-v 0 0 20) 3.2 |]
        | Refinery ->
            // Furnace floor, control deck, and the three independent furnace stacks.
            [| box 0 -20 -122 109 16 30; box 7 -10 -91 30 10 12
               box -77 34 -129 18 45 18; box -11 64 -153 22 75 22
               box 61 46 -123 16 58 16
               box -5 -66 -137 34 28 21
               for i in 0 .. StationMeshes.refineryRadiators.Length-1 do
                   let p = StationMeshes.refineryRadiators.[i]
                   let h = 27. - 3. * float (i%3)
                   box p.X p.Y p.Z 9.5 (h-4.) 4.5 |]
        | FrontierBazaar ->
            [| box 0 -6 -124 24 17 23
               houseboat -56 30 -143 0.22 68. 15.; houseboat 57 -12 -104 -0.29 76. 14.
               houseboat -83 -23 -102 2.87 45. 10.; houseboat 53 52 -158 -2.94 53. 10.
               for p in StationMeshes.bazaarPavilions do
                   box p.X (p.Y+3.) p.Z 19.0 12.0 13.0
                   box p.X (p.Y+19.) p.Z 12.0 5.0 9.0 |]
        | OrbitalYard ->
            // Both dry-dock sills, the cruiser stern and its raised superstructure.
            // The exposed bow frames and the space between the sills remain open.
            [| box 0 -38 -84 159 9 7; box 0 -38 -211 159 9 7
               box -84 12 -148 43 20 35; box 0 13 -148 76 23 37
               box 5 56 -148 55 15 26; box 0 -21 -93 18 11 11
               box -163 49 -218 12 95 17; box 163 49 -218 12 95 17
               box 0 146 -218 162 10 17; box -18 125 -218 18 8 11 |]
        | ResearchRelay ->
            [| box 0 -15 -124 30 16 23; box 12 50 -152 5 63 5
               dish (v -75 55 -136) (v -0.48 0.63 0.64) 61.
               dish (v 88 27 -123) (v 0.84 0.33 0.35) 40.
               for side in [-1.; 1.] do
                   let root, span, chord, face = StationMeshes.thetaSolarFrame side
                   Box(root + span * 95.0, v 95.0 3.3 3.3, span, chord, face)
                   for a,d in StationMeshes.thetaSolarSections do
                       let centre = root + span * ((a + d) * 0.5)
                       Box(centre, v ((d - a) * 0.5) StationMeshes.thetaSolarHalfHeight 1.5, span, chord, face) |]
        | FleetDock ->
            let wing side =
                let x = Vec3.normalize (v side 0.34 0)
                Box(v (side*93.) 14 -137, v 62 13 21, x, Vec3.cross Vec3.unitZ x, Vec3.unitZ)
            [| box 0 -1 -131 68 22 38; box 0 -34 -131 27 19 19
               box 0 34 -137 32 24 23; box 0 71 -141 18 17 15
               wing -1.; wing 1.
               for side in [-1.;1.] do
                   let x = side * 145.
                   box x 46 -147 21 17 22
                   box x 90 -149 16 28 19 |]

    let private hulls =
        Universe.sectors |> Array.mapi (fun index s ->
            let port = s.Outpost
            let wrecks = WreckFields.forSector index
            let wreckBodies = wrecks |> Array.map (fun wreck ->
                let q = wreck.Rotation
                Box(wreck.Position - port.Position, WreckFields.solidHalf wreck.Kind, Quat.right q, Quat.up q, -Quat.forward q))
            let site = PortWork.sites.[index]
            let outworks =
                [| for m in site.Modules do
                       yield Box(site.Offset+m.Center,m.Half,Vec3.unitX,Vec3.unitY,Vec3.unitZ)
                   for a,c in site.Braces do yield beam (site.Offset+a) (site.Offset+c) 2.2
                   if PortWork.hasHoist index then
                       let a,c = PortWork.hoistRail
                       yield beam (site.Offset+a) (site.Offset+c) 1.1
                       for top in [a;c] do
                           yield beam (site.Offset+v top.X -14 -17) (site.Offset+top) 1.45
                           yield beam (site.Offset+v top.X -14 1) (site.Offset+top) 1.1 |]
            { Bodies = Array.concat [|bodies port.Kind; wreckBodies; outworks|]
              Port = port.Position
              Bounds =
                  [| yield port.Position + StationMeshes.worksCenter, StationMeshes.worksRadius port.Kind
                     yield port.Position + site.Offset, 87.0
                     for wreck in wrecks do
                         yield wreck.Position, Vec3.length (WreckFields.solidHalf wreck.Kind) |] })

    let forSector index = hulls.[max 0 (min (hulls.Length-1) index)]

    let private nearHull hull padding x0 y0 z0 x1 y1 z1 =
        hull.Bounds |> Array.exists (fun (c, radius) ->
            let r = radius + padding
            let overlaps p q mid = min p q <= mid+r && max p q >= mid-r
            overlaps x0 x1 c.X && overlaps y0 y1 c.Y && overlaps z0 z1 c.Z)

    let private slab start delta half =
        if abs delta < 1e-12 then
            if abs start <= half then struct (-infinity, infinity) else struct (infinity, -infinity)
        else
            let a, b = (-half-start)/delta, (half-start)/delta
            struct (min a b, max a b)

    // Unclamped entry and exit parameters, so the same query can recover a
    // ship whose saved centre is already inside one or more overlapping hulls.
    let private interval body start delta padding =
        match body with
        | Box(center, half, x, y, z) ->
            let local = start - center
            let struct (x0,x1) = slab (Vec3.dot local x) (Vec3.dot delta x) (half.X+padding)
            let struct (y0,y1) = slab (Vec3.dot local y) (Vec3.dot delta y) (half.Y+padding)
            let struct (z0,z1) = slab (Vec3.dot local z) (Vec3.dot delta z) (half.Z+padding)
            struct (max x0 (max y0 z0), min x1 (min y1 z1))
        | Sphere(center, radius) ->
            let local = start - center
            let a, b = Vec3.lengthSq delta, Vec3.dot local delta
            let r = radius + padding
            let c = Vec3.lengthSq local - r*r
            if a < 1e-24 then
                if c <= 0. then struct (-infinity, infinity) else struct (infinity, -infinity)
            else
                let d = b*b-a*c
                if d < 0. then struct (infinity, -infinity)
                else
                    let root = sqrt d
                    struct ((-b-root)/a, (-b+root)/a)

    let private first hull start delta padding =
        let mutable index, time = -1, 2.
        for i in 0 .. hull.Bodies.Length-1 do
            let struct (entry, exit) = interval hull.Bodies.[i] start delta padding
            if entry < time && entry <= 1. && exit >= entry && exit >= 0. then
                time <- entry
                index <- i
        struct (index, time)

    /// Earliest station or wreck hit along a bolt, or 2.0 beyond the segment.
    let laserHit hull x0 y0 z0 x1 y1 z1 =
        if nearHull hull 0. x0 y0 z0 x1 y1 z1 then
            let start = v (x0-hull.Port.X) (y0-hull.Port.Y) (z0-hull.Port.Z)
            let struct (_, t) = first hull start (v (x1-x0) (y1-y0) (z1-z0)) 0.
            max 0. t
        else 2.

    let private normalAt body point padding =
        match body with
        | Sphere(center, _) -> Vec3.normalize (point-center)
        | Box(center, half, x, y, z) ->
            let local = point-center
            let px, py, pz = Vec3.dot local x, Vec3.dot local y, Vec3.dot local z
            let dx, dy, dz = half.X+padding-abs px, half.Y+padding-abs py, half.Z+padding-abs pz
            let signed (axis: Vec3) p = if p < 0. then -axis else axis
            if dx <= dy && dx <= dz then signed x px
            elif dy <= dz then signed y py
            else signed z pz

    let private skin = 0.002
    let private exitDirections = [| Vec3.unitZ; -Vec3.unitZ; Vec3.unitY; -Vec3.unitY; Vec3.unitX; -Vec3.unitX |]

    let private recover hull start radius =
        let mutable nearest, outward = infinity, Vec3.zero
        for dir in exitDirections do
            let mutable distance, previous = 0., -1.
            while distance > previous do
                previous <- distance
                for body in hull.Bodies do
                    let struct (entry, exit) = interval body start dir radius
                    if entry <= distance && distance <= exit then distance <- exit+skin
            if distance < nearest then
                nearest <- distance
                outward <- dir
        struct (Vec3.mulAdd start outward nearest, outward)

    let private slide v normal = v - normal * min 0. (Vec3.dot v normal)

    /// Sweep a ship's bounding sphere. A shallow contact preserves tangential
    /// flight. Recovery also moves the interpolation origin out of solid geometry.
    let move hull radius (start: Vec3) (finish: Vec3) velocity =
        if not (nearHull hull radius start.X start.Y start.Z finish.X finish.Y finish.Z) then ValueNone
        else
            let mutable position, motion = start-hull.Port, finish-start
            let mutable speed, previous = velocity, start
            let mutable changed, complete = false, false
            let mutable contacts = 0
            while not complete && contacts < 4 do
                let struct (i, time) = first hull position motion radius
                if i < 0 then
                    position <- position + motion
                    complete <- true
                else
                    changed <- true
                    contacts <- contacts + 1
                    let normal =
                        if time < 0. then
                            let struct (outside, normal) = recover hull position radius
                            position <- outside
                            if contacts = 1 then previous <- hull.Port + outside
                            normal
                        else
                            let contact = Vec3.mulAdd position motion time
                            let normal = normalAt hull.Bodies.[i] contact radius
                            position <- Vec3.mulAdd contact normal skin
                            motion <- motion * (1.-time)
                            normal
                    motion <- slide motion normal
                    speed <- slide speed normal
            if changed then ValueSome (struct (previous, hull.Port+position, speed)) else ValueNone
