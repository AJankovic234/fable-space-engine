namespace SpaceEngine

open System

[<RequireQualifiedAccess>]
module UpgradeMeshes =
    // Palette: monochrome gunmetal base, one accent per family (laser cyan, shield blue, armour brass, relay copper/teal)
    let private gun = MeshColor.rgb 0.20 0.22 0.25
    let private light = MeshColor.rgb 0.38 0.41 0.45
    let private steel = MeshColor.rgb 0.09 0.10 0.12
    let private brass = MeshColor.rgb 0.70 0.50 0.18
    let private copper = MeshColor.rgb 0.75 0.34 0.16
    let private graphite = MeshColor.rgb 0.04 0.04 0.05
    let private cyan = MeshColor.glow 0.10 0.90 1.00 2.0
    let private blue = MeshColor.glow 0.10 0.45 1.00 1.4
    let private teal = MeshColor.glow 0.00 0.90 0.70 2.0

    let private clampRank (r: int) = max 1 (min 3 r)

    // Circle/ellipse ring around the Z axis (barrels, coils)
    let private ringZ cx cy z rx ry (n: int) =
        Array.init n (fun i ->
            let a = 2.0 * Math.PI * float i / float n
            Vec3(cx + rx * Math.Cos a, cy + ry * Math.Sin a, z))

    // Circle/ellipse ring around the Y axis (capacitors, bases)
    let private ringY cx y cz rx rz (n: int) =
        Array.init n (fun i ->
            let a = 2.0 * Math.PI * float i / float n
            Vec3(cx + rx * Math.Cos a, y, cz + rz * Math.Sin a))

    // Tapered square-section strut / vane from (x0,y0,z0) to (x1,y1,z1)
    let private post (b: MeshBuilder) x0 z0 y0 x1 z1 y1 w col =
        let q x y z h =
            [| Vec3(x - h, y, z - h); Vec3(x + h, y, z - h); Vec3(x + h, y, z + h); Vec3(x - h, y, z + h) |]
        b.Loft([| q x0 y0 z0 w; q x1 y1 z1 (w * 0.5) |], true, true, col)

    // ---------------- LASER ----------------
    // Hollow collimator barrel: open lip at zf, recessed cyan lens, light collars
    let private barrel (b: MeshBuilder) x y rad zf zb =
        let o z r = ringZ x y z r r 12
        b.Loft([| o zb (rad * 0.8); o (zb - 0.08) rad; o (zf + 0.14) rad |], true, false, gun)
        ProceduralMeshes.turretBore b x y zf (rad * 1.08) 0.15 light graphite cyan
        for z in [ zf + 0.22; zf + 0.36; zb - 0.20 ] do
            b.Loft([| o z (rad * 1.14); o (z + 0.04) (rad * 1.14) |], true, true, light)

    let laser (rank: int) : MeshData =
        let k = clampRank rank
        let b = MeshBuilder()
        let cy = 0.18
        let rr = if k = 1 then 0.18 else 0.14
        b.Box(Vec3(0.0, 0.04, 0.40), Vec3(0.10 + 0.04 * float k, 0.04, 0.36), steel)
        if k = 1 then
            barrel b 0.0 cy rr (-0.04) 0.80
        else
            // central firing barrel stays on the muzzle line, forked prongs flank it
            barrel b 0.0 cy 0.115 (-0.04) 0.56
            b.Loft([| ringZ 0.0 cy 0.82 (rr * 0.8) (rr * 0.8) 12; ringZ 0.0 cy 0.76 rr rr 12
                      ringZ 0.0 cy 0.42 rr rr 12; ringZ 0.0 cy 0.38 (rr * 0.7) (rr * 0.7) 12 |], true, true, gun)
            let px = 0.10 + 0.03 * float k
            let pf = if k = 2 then 0.11 else -0.11
            for sx in [ -1.0; 1.0 ] do
                barrel b (sx * px) cy 0.06 pf 0.56
            b.Box(Vec3(0.0, cy, 0.50), Vec3(px, 0.03, 0.04), light)
        // heat sink fins along the aft spine
        for i in 0 .. k do
            b.Box(Vec3(0.0, cy + rr * 0.95 + 0.02, 0.50 + 0.08 * float i), Vec3(0.05 + 0.02 * float k, 0.025, 0.015), steel)
        if k = 3 then
            for sx in [ -1.0; 1.0 ] do
                post b (sx * 0.10) 0.70 (cy + rr) (sx * 0.16) 0.80 (cy + rr + 0.07) 0.025 cyan
                b.Box(Vec3(sx * 0.195, cy, 0.32), Vec3(0.034, 0.085, 0.26), light)
                b.Box(Vec3(sx * 0.198, cy + 0.085, 0.32), Vec3(0.017, 0.008, 0.19), cyan)
        b.Build()

    // ---------------- SHIELD ----------------
    let private crystal (b: MeshBuilder) cx cz r h col =
        let o y rr = ringY cx y cz rr rr 6
        b.Loft([| o 0.04 (r * 0.75); o (0.04 + 0.30 * h) r; o (0.04 + 0.72 * h) (r * 0.8); o (0.04 + h) (r * 0.12) |], true, true, col)
        b.Loft([| o 0.04 (r * 1.15); o 0.09 (r * 1.05) |], true, true, steel)

    let shield (rank: int) : MeshData =
        let k = clampRank rank
        let b = MeshBuilder()
        let rx = 0.21 + 0.045 * float (k - 1)
        let rz = if k = 1 then 0.26 else 0.32
        let h = 0.18 + 0.065 * float (k - 1)
        b.Loft([| ringY 0.0 0.0 0.0 rx rz 8; ringY 0.0 0.05 0.0 rx rz 8
                  ringY 0.0 0.075 0.0 (rx * 0.86) (rz * 0.86) 8 |], true, true, gun)
        match k with
        | 1 -> crystal b 0.0 0.0 0.13 h blue
        | 2 -> for sz in [ -1.0; 1.0 ] do crystal b 0.0 (sz * 0.17) 0.11 h blue
        | _ ->
            crystal b 0.0 0.0 0.12 h blue
            for sz in [ -1.0; 1.0 ] do crystal b 0.0 (sz * 0.23) 0.08 (h * 0.7) cyan
        // partial frame: two side arches, open front and back
        let fh = h * 0.7 + 0.04
        for sx in [ -1.0; 1.0 ] do
            let x = sx * (rx - 0.04)
            for sz in [ -1.0; 1.0 ] do
                post b x (sz * (rz - 0.10)) 0.05 (x * 0.85) (sz * (rz - 0.10)) fh 0.025 gun
            b.Box(Vec3(x * 0.85, fh, 0.0), Vec3(0.015, 0.015, rz - 0.10), light)
        if k >= 2 then
            b.Box(Vec3(0.0, fh, rz - 0.10), Vec3(rx * 0.85, 0.015, 0.015), light)
        // tier 3: splayed field vanes give a crown silhouette
        if k = 3 then
            for sx in [ -1.0; 1.0 ] do
                for sz in [ -1.0; 1.0 ] do
                    post b (sx * 0.17) (sz * 0.12) 0.06 (sx * 0.27) (sz * 0.12) (h + 0.03) 0.03 blue
        b.Build()

    // ---------------- ARMOUR ----------------
    let private slab (b: MeshBuilder) x z0 z1 hw bot top col =
        let s z w = ProceduralMeshes.hullSection x z w bot top
        b.Loft([| s z0 (hw * 0.8); s (z0 + 0.04) hw; s (z1 - 0.04) hw; s z1 (hw * 0.9) |], true, true, col)

    let armour (rank: int) : MeshData =
        let k = clampRank rank
        let b = MeshBuilder()
        let n = k + 2
        let len = 0.83 / float n
        let th = 0.10 + 0.045 * float (k - 1)
        let hw = 0.20
        for i in 0 .. n - 1 do
            let z0 = -0.415 + len * float i
            let z1 = min 0.415 (z0 + len + 0.05)
            let top = th - 0.01 * float i
            slab b 0.0 z0 z1 hw 0.0 top gun
            b.Box(Vec3(0.0, top + 0.006, z0 + 0.03), Vec3(hw * 0.8, 0.008, 0.012), steel)
            for sx in [ -1.0; 1.0 ] do
                b.Box(Vec3(sx * (hw - 0.004), top - 0.02, (z0 + z1) * 0.5), Vec3(0.012, 0.012, (z1 - z0) * 0.5), brass)
                b.Box(Vec3(sx * hw * 0.7, top + 0.004, (z0 + z1) * 0.5), Vec3(0.012, 0.012, 0.012), brass)
        if k >= 2 then
            // side wings widen coverage to ~0.5 and lengthen with tier
            let wl = if k = 2 then 0.50 else 0.78
            let wt = th * 0.7
            for sx in [ -1.0; 1.0 ] do
                slab b (sx * 0.18) (-wl * 0.5) (wl * 0.5) 0.07 0.0 wt light
                b.Box(Vec3(sx * 0.245, wt - 0.02, 0.0), Vec3(0.012, 0.012, wl * 0.5), brass)
                b.Box(Vec3(sx * 0.10, th + 0.002, 0.0), Vec3(0.012, 0.012, 0.36), steel)
        if k = 3 then
            // raised brass ridge spine
            slab b 0.0 (-0.30) 0.30 0.06 (th - 0.05) (th + 0.05) brass
        b.Build()

    // ---------------- RELAY ----------------
    let relay () : MeshData =
        let b = MeshBuilder()
        let cy = 0.165
        b.Loft([| ringY 0.0 0.0 0.0 0.21 0.29 8; ringY 0.0 0.04 0.0 0.21 0.29 8
                  ringY 0.0 0.06 0.0 0.18 0.26 8 |], true, true, gun)
        b.Box(Vec3(0.0, cy, 0.0), Vec3(0.07, 0.07, 0.27), graphite)
        // six copper turns around the graphite block
        for i in -3 .. 2 do
            let z = (float i + 0.5) * 0.07
            let o zz r = ringZ 0.0 cy zz r r 14
            b.Loft([| o (z - 0.03) 0.10; o (z - 0.015) 0.115; o (z + 0.015) 0.115; o (z + 0.03) 0.10 |], false, false, copper)
        for sz in [ -1.0; 1.0 ] do
            b.Box(Vec3(0.0, 0.08, sz * 0.25), Vec3(0.10, 0.03, 0.02), steel)
            b.Box(Vec3(0.0, cy, sz * 0.275), Vec3(0.05, 0.05, 0.01), teal)
            for sx in [ -1.0; 1.0 ] do
                b.Box(Vec3(sx * 0.15, 0.07, sz * 0.23), Vec3(0.02, 0.012, 0.02), teal)
                post b (sx * 0.11) (sz * 0.12) 0.06 (sx * 0.09) (sz * 0.12) (cy + 0.04) 0.02 copper
        b.Build()
