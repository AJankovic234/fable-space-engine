namespace SpaceEngine

type JumpGateEvent =
    | ApertureCrossed
    | FrameContact

[<RequireQualifiedAccess>]
module JumpFlight =
    let exitDistance = 52.0
    let transitDuration = 0.72

    let private firstContact includeInitial (boxes: struct (Vec3 * Vec3 * Vec3 * Vec3 * Vec3)[]) radius start finish =
        let mutable nearest = infinity
        for struct (center, right, up, axis, half) in boxes do
            let a, b = start - center, finish - center
            let ax, ay, az = Vec3.dot a right, Vec3.dot a up, Vec3.dot a axis
            let hx, hy, hz = half.X + radius, half.Y + radius, half.Z + radius
            // Old saves can place a ship inside a strut. Let it move out of that volume.
            if includeInitial || abs ax > hx || abs ay > hy || abs az > hz then
                let t = Collision.segmentBox ax ay az (Vec3.dot b right) (Vec3.dot b up) (Vec3.dot b axis) hx hy hz
                if t >= 0.0 && t < nearest then nearest <- t
        nearest

    let private apertureSides =
        let count = ProceduralMeshes.jumpGateRingSegments
        let step = 2.0 * System.Math.PI / float count
        Array.init count (fun i ->
            let a = (float i + 0.5) * step
            Vec3(cos a, sin a, 0.0))

    let private apertureEdge =
        ProceduralMeshes.jumpGateOpeningRadius * cos (System.Math.PI / float ProceduralMeshes.jumpGateRingSegments)

    /// Trace the whole physics step in gate-local space, including the actual frame depth.
    let trace sector (gate: JumpGate) time radius (start: Vec3) (finish: Vec3) =
        let a, b = start - gate.Position, finish - gate.Position
        // Broad phase: the longest pylon and its beacon fit within 36 m.
        let reach = 36.0 + radius + Vec3.distance start finish
        if Vec3.lengthSq a > reach * reach || Vec3.lengthSq (b - a) < 1e-12 then None
        else
            let frame = Quat.conjugate (Universe.gateRotation sector gate)
            let ring = Quat.conjugate (Universe.gateRingRotation sector gate time)
            let frameA, frameB = Quat.rotateVec3 frame a, Quat.rotateVec3 frame b
            let ringA, ringB = Quat.rotateVec3 ring a, Quat.rotateVec3 ring b
            let contact =
                min (firstContact false ProceduralMeshes.jumpGateFrameBoxes radius frameA frameB)
                    (firstContact false ProceduralMeshes.jumpGateRingBoxes radius ringA ringB)
            let crosses = (ringA.Z > 0.0 && ringB.Z <= 0.0) || (ringA.Z < 0.0 && ringB.Z >= 0.0)
            let crossing =
                if crosses then
                    let t = ringA.Z / (ringA.Z - ringB.Z)
                    let point = Vec3.lerp ringA ringB t
                    if apertureSides |> Array.forall (fun side -> Vec3.dot side point < apertureEdge) then
                        // A shallow diagonal can fit at the plane yet clip the opposite rim.
                        // Check the remaining thickness to make sure the whole hull fits.
                        let depth = 5.0 + radius
                        let beyond = t + depth / abs (ringB.Z - ringA.Z)
                        let framePoint = Vec3.lerp frameA frameB t
                        let farFrame = Vec3.lerp frameA frameB beyond
                        let farRing = Vec3.lerp ringA ringB beyond
                        if firstContact true ProceduralMeshes.jumpGateFrameBoxes radius framePoint farFrame = infinity &&
                           firstContact true ProceduralMeshes.jumpGateRingBoxes radius point farRing = infinity then t
                        else infinity
                    else infinity
                else infinity
            if contact <= 1.0 && contact <= crossing then Some(struct (FrameContact, contact))
            elif crossing <= 1.0 then Some(struct (ApertureCrossed, crossing))
            else None

    /// Arrivals face away from the reciprocal gate. Use its free side when the cruiser
    /// occupies the approach lane, leaving the existing blockade encounter in place.
    let arrival sector (gate: JumpGate) radius cruiserPresent =
        let normal = Quat.forward (Universe.gateRotation sector gate)
        let direction =
            if cruiserPresent then
                let a = gate.Position + normal * exitDistance - CapitalFlight.position
                let b = a + normal * 60.0
                let obstructed =
                    CapitalFlight.plates |> Array.exists (fun (center, half) ->
                        let p, q = a - center, b - center
                        Collision.segmentBox p.X p.Y p.Z q.X q.Y q.Z (half.X + radius) (half.Y + radius) (half.Z + radius) >= 0.0)
                if obstructed then -normal else normal
            else normal
        gate.Position + direction * exitDistance, direction
