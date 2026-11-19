namespace SpaceEngine

open System

// =====================================================================================
//  SectorSights.fs  -  Sehenswürdigkeiten und Betrieb draußen im Sektor (Handelsflug)
//
//  Zwischen Tor und Station war der Raum leer: Felsen, sonst nichts. Jeder Sektor
//  bekommt jetzt eigene Bauwerke an seinen Einsatzorten, die man mit ihren
//  Leuchtfeuern schon von weitem sieht (Bohrtürme, die zwölf Notbojen, Rumpfskelette,
//  Horchgitter, Abwehrplattformen, ein Komet, ein zerbrochener Kreuzer, Zollbaken,
//  Schlackemasten). In den Erznestern arbeiten Schürferschiffe mit Bohrstrahlen.
//  Nur Darstellung: keine Kollision, kein Spielstand.
// =====================================================================================

type SightPart =
    | Rig = 0
    | Buoy = 1
    | Dish = 2
    | Skeleton = 3
    | Platform = 4
    | CometCore = 5
    | CruiserHalf = 6
    | Pylon = 7

type Beacon =
    { Offset: Vec3
      Color: Vec3
      Size: float
      /// Blinkperiode in Sekunden (0 = ruhiges Licht) und Phasenversatz 0..1.
      Period: float
      Phase: float }

type Sight =
    { Name: string
      Position: Vec3
      Rotation: Quat
      Parts: (SightPart * Vec3 * Quat * float)[]
      Beacons: Beacon[]
      /// Radius für Sichtprüfung und HUD.
      Radius: float
      /// Kometenschweif (Richtung, Länge) oder keiner.
      Tail: (Vec3 * float) option }

/// Ein Schürferschiff an einem Erznest: hängt vor einem Felsen und bohrt.
type Miner =
    { mutable Rock: Vec3
      mutable RockRadius: float
      mutable Position: Vec3
      mutable Rotation: Quat
      mutable Busy: bool
      Nest: int
      Slot: int }

[<RequireQualifiedAccess>]
module SectorSights =
    let private v x y z = Vec3(x, y, z)
    let private tau = 2.0 * Math.PI
    let private yaw a = Quat.fromAxisAngle Vec3.up a

    // ---------------------------------------------------------------------------------
    //  Meshes

    let private steel = MeshColor.rgb 0.16 0.18 0.2
    let private darkSteel = MeshColor.rgb 0.07 0.08 0.09
    let private rust = MeshColor.rgb 0.36 0.16 0.07
    let private paint = MeshColor.rgb 0.5 0.27 0.05
    let private ivory = MeshColor.rgb 0.42 0.41 0.37
    let private rock = MeshColor.rgb 0.24 0.21 0.18
    let private lamp = MeshColor.glow 1.0 0.55 0.15 0.95
    let private ion = MeshColor.glow 0.3 0.85 1.0 0.9
    let private ember = MeshColor.glow 1.0 0.32 0.06 0.95

    let private beam b a c w color = ProceduralMeshes.beam b a c w color

    /// Zylinder (n-Eck) zwischen zwei Punkten.
    let private tube (b: MeshBuilder) (a: Vec3) (c: Vec3) radius n color =
        let axis = Vec3.normalize (c - a)
        let reference = if abs axis.Y > 0.9 then Vec3.unitX else Vec3.unitY
        let right = Vec3.normalize (Vec3.cross axis reference)
        let up = Vec3.cross axis right
        let ring (p: Vec3) r = Array.init n (fun i -> let t = tau * float i / float n in p + right * (cos t * r) + up * (sin t * r))
        b.Loft([| ring a radius; ring c radius |], true, true, color)

    /// Unregelmäßiger Felsklumpen (für Fundamente und den Kometenkern).
    let private lump (b: MeshBuilder) (center: Vec3) radius seed color =
        let rng = Rng(seed)
        let rows = 7
        let rings =
            Array.init rows (fun j ->
                let lat = Math.PI * (float j + 0.5) / float rows - Math.PI / 2.0
                Array.init 10 (fun i ->
                    let lon = tau * float i / 10.0
                    let r = radius * rng.Range(0.82, 1.12)
                    center + v (cos lat * cos lon * r) (sin lat * r) (cos lat * sin lon * r)))
        b.Loft(rings, true, true, color)

    let private rig () =
        let b = MeshBuilder()
        lump b (v 0.0 -24.0 0.0) 26.0 4411 rock
        b.Box(v 0.0 0.0 0.0, v 22.0 2.5 22.0, steel)
        b.Box(v 0.0 2.8 0.0, v 20.0 0.4 20.0, darkSteel)
        let corner sx sz h inset = v (sx * (8.0 - inset * h / 65.0)) (3.0 + h) (sz * (8.0 - inset * h / 65.0))
        for sx in [ -1.0; 1.0 ] do
            for sz in [ -1.0; 1.0 ] do
                beam b (corner sx sz 0.0 5.0) (corner sx sz 62.0 5.0) 0.9 rust
        for h in [ 10.0; 22.0; 34.0; 46.0; 58.0 ] do
            let c sx sz = corner sx sz h 5.0
            beam b (c -1.0 -1.0) (c 1.0 -1.0) 0.5 rust
            beam b (c 1.0 -1.0) (c 1.0 1.0) 0.5 rust
            beam b (c 1.0 1.0) (c -1.0 1.0) 0.5 rust
            beam b (c -1.0 1.0) (c -1.0 -1.0) 0.5 rust
            beam b (c -1.0 -1.0) (corner 1.0 1.0 (h + 12.0) 5.0) 0.35 steel
        tube b (v 0.0 70.0 0.0) (v 0.0 -48.0 0.0) 1.6 8 steel
        tube b (v 0.0 -40.0 0.0) (v 0.0 -50.0 0.0) 3.2 8 paint
        beam b (v 0.0 52.0 0.0) (v 36.0 54.0 0.0) 1.0 paint
        beam b (v -12.0 52.0 0.0) (v 0.0 52.0 0.0) 1.0 paint
        b.Box(v -14.0 52.0 0.0, v 3.0 3.0 3.0, darkSteel)
        beam b (v 34.0 53.0 0.0) (v 34.0 30.0 0.0) 0.2 darkSteel
        b.Box(v 34.0 28.0 0.0, v 3.0 2.5 3.0, ivory)
        b.Box(v -13.0 8.0 -11.0, v 6.0 5.0 6.0, ivory)
        b.Box(v -13.0 9.5 -4.9, v 4.5 0.8 0.15, lamp)
        b.Box(v 0.0 71.0 0.0, v 1.3 1.3 1.3, lamp)
        b.Build()

    let private dish () =
        let b = MeshBuilder()
        b.Box(v 0.0 -22.0 0.0, v 4.0 1.5 4.0, darkSteel)
        tube b (v 0.0 -21.0 0.0) (v 0.0 -1.0 0.0) 0.9 6 steel
        let ring y r = ProceduralMeshes.ring (16, 0.0, 0.0, 0.0, r, r) |> Array.map (fun p -> v p.X y p.Y)
        // Flache Schüssel, Öffnung nach oben.
        b.Loft([| ring 0.0 2.0; ring 2.2 8.0; ring 4.0 13.0; ring 4.6 14.0 |], true, false, ivory)
        b.Loft([| ring 4.5 14.0; ring 2.0 8.5; ring -0.2 2.2 |], false, true, darkSteel)
        for i in 0 .. 2 do
            let a = tau * float i / 3.0
            beam b (v (cos a * 12.0) 4.4 (sin a * 12.0)) (v 0.0 13.0 0.0) 0.22 steel
        b.Box(v 0.0 13.5 0.0, v 0.9 0.9 0.9, ion)
        b.Build()

    let private skeleton () =
        let b = MeshBuilder()
        b.Box(v 0.0 -15.0 0.0, v 3.0 3.0 72.0, steel)
        for k in 0 .. 8 do
            let z = -60.0 + float k * 15.0
            let points =
                Array.init 9 (fun i ->
                    let a = Math.PI * (1.08 + 0.84 * float i / 8.0)
                    v (cos a * 22.0) (sin a * 18.0) z)
            for i in 0 .. points.Length - 2 do beam b points.[i] points.[i + 1] 0.9 (if k % 3 = 0 then paint else rust)
            beam b points.[0] (v -22.0 6.0 z) 0.7 rust
            beam b points.[8] (v 22.0 6.0 z) 0.7 rust
        // Ein halbes Stück Außenhaut, damit man erkennt, was hier entsteht.
        for k in 0 .. 2 do
            let z = 30.0 + float k * 10.0
            b.Box(v 0.0 -18.4 z, v 12.0 0.4 4.6, ivory)
        // Portalkran über dem Bug.
        beam b (v -30.0 -20.0 -55.0) (v -30.0 26.0 -55.0) 1.2 paint
        beam b (v 30.0 -20.0 -55.0) (v 30.0 26.0 -55.0) 1.2 paint
        beam b (v -31.0 26.0 -55.0) (v 31.0 26.0 -55.0) 1.4 paint
        b.Box(v 8.0 24.0 -55.0, v 4.0 2.0 3.0, darkSteel)
        b.Box(v 0.0 -11.5 -66.0, v 1.0 1.0 1.0, lamp)
        b.Build()

    let private platform () =
        let b = MeshBuilder()
        let hex y r = ProceduralMeshes.ring (6, 0.0, 0.0, 0.0, r, r) |> Array.map (fun p -> v p.X y p.Y)
        b.Loft([| hex -6.0 14.0; hex -2.0 24.0; hex 2.0 24.0; hex 4.0 20.0 |], true, true, steel)
        lump b (v 0.0 -16.0 0.0) 14.0 9907 rock
        b.Box(v 0.0 7.0 0.0, v 7.0 3.0 7.0, darkSteel)
        b.Box(v 0.0 11.0 -2.0, v 4.5 2.0 5.0, ivory)
        for x in [ -2.0; 2.0 ] do tube b (v x 11.5 -6.0) (v x 11.5 -26.0) 0.7 6 darkSteel
        tube b (v 12.0 4.0 12.0) (v 12.0 30.0 12.0) 0.5 6 steel
        b.Box(v 12.0 30.5 12.0, v 0.8 0.8 0.8, lamp)
        for i in 0 .. 2 do
            let a = tau * float i / 3.0 + 0.5
            b.Box(v (cos a * 20.0) 4.6 (sin a * 20.0), v 1.4 0.6 1.4, lamp)
        b.Build()

    let private cometCore () =
        let b = MeshBuilder()
        lump b Vec3.zero 34.0 7703 (MeshColor.rgb 0.62 0.7 0.78)
        lump b (v 18.0 10.0 -6.0) 16.0 7704 (MeshColor.rgb 0.72 0.8 0.86)
        lump b (v -14.0 -12.0 10.0) 14.0 7705 (MeshColor.rgb 0.5 0.56 0.64)
        b.Build()

    let private cruiserHalf () =
        let b = MeshBuilder()
        let section z r jag =
            Array.init 8 (fun i ->
                let a = tau * (float i + 0.5) / 8.0
                let rr = r * (1.0 + (if jag then (float ((i * 7) % 5) - 2.0) * 0.12 else 0.0))
                v (cos a * rr) (sin a * rr * 0.62) z)
        b.Loft([| section -10.0 9.0 false; section 10.0 21.0 false; section 70.0 23.0 false; section 112.0 19.0 true |], true, false, darkSteel)
        b.Loft([| section 111.0 18.0 true; section 106.0 12.0 true |], false, true, ember)
        b.Box(v 0.0 15.0 40.0, v 7.0 5.0 16.0, steel)
        b.Box(v 0.0 22.0 34.0, v 3.0 4.0 5.0, rust)
        for z in [ 20.0; 60.0; 90.0 ] do
            b.Box(v 14.0 6.0 z, v 4.0 3.0 6.0, rust)
            tube b (v 14.0 7.0 (z - 4.0)) (v 14.0 7.0 (z - 18.0)) 0.7 6 darkSteel
        for z in [ 30.0; 75.0 ] do b.Box(v -20.0 -2.0 z, v 0.4 3.0 8.0, ember)
        b.Build()

    let private pylon () =
        let b = MeshBuilder()
        tube b (v 0.0 -22.0 0.0) (v 0.0 20.0 0.0) 1.0 6 steel
        b.Box(v 0.0 -23.0 0.0, v 3.0 1.2 3.0, darkSteel)
        beam b (v -9.0 14.0 0.0) (v 9.0 14.0 0.0) 0.6 paint
        for x in [ -9.0; 9.0 ] do b.Box(v x 12.0 0.0, v 1.2 2.0 1.2, darkSteel)
        b.Box(v 0.0 21.5 0.0, v 2.2 1.6 2.2, darkSteel)
        b.Box(v 0.0 23.4 0.0, v 1.2 0.6 1.2, lamp)
        b.Build()

    let partMesh part =
        match part with
        | SightPart.Rig -> rig ()
        | SightPart.Buoy -> fst (ProceduralMeshes.approachBuoy ())
        | SightPart.Dish -> dish ()
        | SightPart.Skeleton -> skeleton ()
        | SightPart.Platform -> platform ()
        | SightPart.CometCore -> cometCore ()
        | SightPart.CruiserHalf -> cruiserHalf ()
        | _ -> pylon ()

    let parts = [| SightPart.Rig; SightPart.Buoy; SightPart.Dish; SightPart.Skeleton; SightPart.Platform
                   SightPart.CometCore; SightPart.CruiserHalf; SightPart.Pylon |]

    // ---------------------------------------------------------------------------------
    //  Aufstellung je Sektor

    let private red = v 1.0 0.12 0.06
    let private amber = v 1.0 0.58 0.14
    let private white = v 0.95 0.95 1.0
    let private cyan = v 0.25 0.85 1.0
    let private orange = v 1.0 0.36 0.06

    let private beacon offset color size period phase =
        { Offset = offset; Color = color; Size = size; Period = period; Phase = phase }

    let private sight name position heading parts beacons radius =
        { Name = name; Position = position; Rotation = yaw heading; Parts = parts; Beacons = beacons; Radius = radius; Tail = None }

    let private one part = [| part, Vec3.zero, Quat.identity, 1.0 |]

    let private drillRig name position heading =
        sight name position heading (one SightPart.Rig)
            [| beacon (v 0.0 72.0 0.0) red 9.0 1.6 0.0
               beacon (v 21.0 3.5 21.0) amber 5.0 0.0 0.0
               beacon (v -21.0 3.5 -21.0) amber 5.0 0.0 0.0
               beacon (v 34.0 26.0 0.0) white 4.0 2.4 0.3 |] 90.0

    /// Die zwölf Stimmen: Notbojen im Ring, die Lichter laufen reihum.
    let private buoyRing name position color =
        let n = 12
        let parts = Array.init n (fun i ->
            let a = tau * float i / float n
            SightPart.Buoy, v (cos a * 110.0) (sin (a * 3.0) * 9.0) (sin a * 110.0), yaw a, 5.0)
        let beacons = Array.init n (fun i ->
            let a = tau * float i / float n
            beacon (v (cos a * 110.0) (sin (a * 3.0) * 9.0 + 7.5) (sin a * 110.0)) color 6.0 3.0 (float i / float n))
        sight name position 0.0 parts beacons 125.0

    let private dishArray name position heading =
        let parts = [| for x in -1 .. 1 do for z in -1 .. 1 do yield SightPart.Dish, v (float x * 55.0) 0.0 (float z * 55.0), yaw (float (x * 3 + z) * 0.7), 1.0 |]
        let beacons = [| for x in -1 .. 1 do for z in -1 .. 1 do yield beacon (v (float x * 55.0) 14.0 (float z * 55.0)) cyan 5.0 2.2 (float ((x + 1) * 3 + z + 1) * 0.07) |]
        sight name position heading parts beacons 100.0

    let private hullSkeleton name position heading =
        sight name position heading (one SightPart.Skeleton)
            [| beacon (v 0.0 -11.5 -66.0) amber 6.0 0.0 0.0
               beacon (v -31.0 27.5 -55.0) red 6.0 1.4 0.0
               beacon (v 31.0 27.5 -55.0) red 6.0 1.4 0.5
               beacon (v 0.0 -12.0 66.0) white 5.0 0.0 0.0 |] 95.0

    let private gunPlatform name position heading =
        sight name position heading (one SightPart.Platform)
            [| beacon (v 12.0 31.5 12.0) red 8.0 1.2 0.0
               beacon (v 0.0 13.5 -27.0) amber 4.0 0.0 0.0 |] 45.0

    let private comet name position (sunDir: Vec3) =
        { Name = name; Position = position; Rotation = Quat.identity
          Parts = [| SightPart.CometCore, Vec3.zero, yaw 0.4, 1.0 |]
          Beacons = [| beacon (v 0.0 0.0 0.0) (v 0.55 0.8 1.0) 70.0 0.0 0.0 |]
          Radius = 60.0
          // Der Schweif zeigt von der Sonne weg.
          Tail = Some (Vec3.normalize (sunDir * -1.0), 380.0) }

    let private cruiserWreck name position heading =
        sight name position heading
            [| SightPart.CruiserHalf, v -40.0 0.0 -110.0, Quat.fromAxisAngle Vec3.unitZ 0.35, 1.0
               SightPart.CruiserHalf, v 30.0 18.0 120.0, yaw Math.PI * Quat.fromAxisAngle Vec3.unitZ -0.6, 1.0 |]
            [| beacon (v -40.0 0.0 0.0) orange 14.0 0.37 0.0
               beacon (v 30.0 18.0 10.0) orange 12.0 0.53 0.4
               beacon (v -60.0 -2.0 -80.0) orange 6.0 0.29 0.2 |] 150.0

    /// Masten in einer Reihe entlang der lokalen Z-Achse.
    let private mastLine name position heading count spacing color period =
        let parts = Array.init count (fun i -> SightPart.Pylon, v 0.0 (float (i % 2) * 6.0) ((float i - float (count - 1) / 2.0) * spacing), Quat.identity, 1.0)
        let beacons = Array.init count (fun i -> beacon (v 0.0 (float (i % 2) * 6.0 + 24.0) ((float i - float (count - 1) / 2.0) * spacing)) color 6.0 period (if i % 2 = 0 then 0.0 else 0.5))
        sight name position heading parts beacons (spacing * float count / 2.0)

    let private sights =
        Universe.sectors |> Array.mapi (fun i sector ->
            let spots = (SectorFields.planFor i).Hotspots
            let at k = spots.[min k (spots.Length - 1)]
            match sector.Id with
            | "outer-belt" ->
                [| drillRig "Stillgelegter Bohrturm K-3" (at 0) 0.4
                   drillRig "Bohrturm K-7" (at 1) 2.1 |]
            | "slag-refinery" ->
                [| mastLine "Schlackemasten" (v -513.0 -20.0 68.0) -0.67 6 95.0 orange 2.0
                   drillRig "Schlackebohrer" (at 1) 1.2 |]
            | "frontier-drift" ->
                [| buoyRing "Die zwölf Stimmen" (at 0) red
                   drillRig "Schürferplattform Drift-9" (at 1) 3.0 |]
            | "kepler-yard" ->
                [| hullSkeleton "Kiellegung 0412" (at 0) 0.9
                   hullSkeleton "Rumpf »Jahrnull«" (at 1) -0.5 |]
            | "aether-relay" ->
                [| dishArray "Horchgitter Band 7" (at 0) 0.3
                   buoyRing "Sperrbojen" (at 1) cyan |]
            | "morgenlicht-bastion" ->
                [| gunPlatform "Abwehrplattform West" (at 0) 0.8
                   gunPlatform "Abwehrplattform Nord" (at 1) -2.4 |]
            | "kaltstrom" ->
                [| comet "Komet Raureif" (at 0) sector.Atmosphere.SunDir |]
            | "wracksaum" ->
                [| cruiserWreck "Wrack der »Langen Leine«" (at 0) 0.6
                   drillRig "Bergungsbohrer" (at 1) -1.0 |]
            | _ ->
                [| mastLine "Zollbaken West" (at 0) 0.5 8 70.0 amber 1.1
                   mastLine "Zollbaken Süd" (at 1) -0.9 8 70.0 amber 1.1 |])

    let forSector (index: int) = sights.[max 0 (min (sights.Length - 1) index)]

    /// Helligkeit eines Leuchtfeuers zur Zeit `time`: ruhig oder kurz aufblitzend.
    let intensity (b: Beacon) time =
        if b.Period <= 0.0 then 0.85
        else
            let phase = ((time / b.Period + b.Phase) % 1.0 + 1.0) % 1.0
            0.12 + 0.88 * Math.Pow(max 0.0 (cos (phase * tau)), 8.0)

    let beaconPosition (s: Sight) (b: Beacon) = s.Position + Quat.rotateVec3 s.Rotation b.Offset

    // ---------------------------------------------------------------------------------
    //  Schürfer an den Erznestern

    [<Literal>]
    let MinersPerNest = 2

    [<Literal>]
    let TailPuffs = 16

    let createMiners (sector: int) =
        (SectorFields.planFor sector).Nests
        |> Array.mapi (fun nest _ ->
            Array.init MinersPerNest (fun slot ->
                { Rock = Vec3.zero; RockRadius = 0.0; Position = Vec3.zero; Rotation = Quat.identity
                  Busy = false; Nest = nest; Slot = slot }))
        |> Array.concat

    /// Sucht für jeden Schürfer einen Felsen am Rand seines Nests (Erz zuerst) und stellt
    /// ihn davor. Felsen, die inzwischen zerstört sind, werden neu vergeben.
    let updateMiners (miners: Miner[]) (sector: int) (field: AsteroidField) =
        let nests = (SectorFields.planFor sector).Nests
        let d = field.Data
        let exists (p: Vec3) =
            let mutable found = false
            let mutable i = 0
            while not found && i < field.Count do
                let o = i * AsteroidField.Stride
                let dx = float d.[o] - p.X
                let dy = float d.[o + 1] - p.Y
                let dz = float d.[o + 2] - p.Z
                if dx * dx + dy * dy + dz * dz < 1.0 then found <- true
                i <- i + 1
            found
        for m in miners do
            if not m.Busy || not (exists m.Rock) then
                let nest = nests.[m.Nest]
                // Jeder Schürfer arbeitet auf seiner Seite des Nests.
                let side = v (if m.Slot = 0 then 1.0 else -1.0) 0.2 (if m.Slot = 0 then 0.4 else -0.3)
                let target = nest.Center + Vec3.normalize side * nest.Spread * 0.9
                let mutable best = -1
                let mutable bestScore = Double.MaxValue
                for i in 0 .. field.Count - 1 do
                    let o = i * AsteroidField.Stride
                    let p = v (float d.[o]) (float d.[o + 1]) (float d.[o + 2])
                    let r = float d.[o + 6]
                    if r >= 4.0 && r <= 20.0 && Vec3.distance p nest.Center < nest.Spread * 2.2 then
                        let score = Vec3.distance p target - (if field.Ore.[i] > 0uy then 40.0 else 0.0)
                        if score < bestScore then
                            best <- i
                            bestScore <- score
                if best >= 0 then
                    let o = best * AsteroidField.Stride
                    let rockPos = v (float d.[o]) (float d.[o + 1]) (float d.[o + 2])
                    let r = float d.[o + 6]
                    let outward = Vec3.normalize (rockPos - nest.Center + side * 4.0)
                    m.Rock <- rockPos
                    m.RockRadius <- r
                    m.Position <- rockPos + outward * (r + 22.0) + v 0.0 4.0 0.0
                    m.Rotation <- Quat.lookRotation (rockPos - m.Position) Vec3.up
                    m.Busy <- true
                else m.Busy <- false

    /// Bohrstrahl vom Bug des Schürfers zur Felsoberfläche.
    let drill (m: Miner) =
        let toRock = m.Rock - m.Position
        let length = max 1.0 (Vec3.length toRock - m.RockRadius)
        let nose = m.Position + Vec3.normalize toRock * 6.0
        nose, Vec3.normalize toRock, max 1.0 (length - 6.0)

    // ---------------------------------------------------------------------------------
    //  Zeichnen

    type Models =
        { Parts: InstanceBatch[]
          Miner: InstanceBatch
          Beam: InstanceBatch
          Halo: InstanceBatch
          mutable MinerSector: int
          mutable Miners: Miner[]
          mutable NextScan: float }

    let create (renderer: Renderer) =
        let batch mesh count blend = Renderer.createBatch renderer (Renderer.createMesh renderer mesh) count blend
        let partBatches = parts |> Array.map (fun p -> batch (partMesh p) 16 Opaque)
        for b in partBatches do
            b.Shininess <- 48.0
            b.Specular <- 0.35
        let beamMesh =
            let b = MeshBuilder()
            b.Box(v 0.0 0.0 -0.5, v 0.35 0.35 0.5, MeshColor.glow 1.0 0.62 0.25 1.0)
            b.Build()
        { Parts = partBatches
          Miner = batch (CivilTrafficMeshes.tug ()) 8 Opaque
          Beam = batch beamMesh 8 Additive
          Halo = batch (ProceduralMeshes.glowSprite 12 3) 128 Additive
          MinerSector = -1
          Miners = Array.empty
          NextScan = 0.0 }

    /// Deckende Bauwerke und Schürfer; vor den additiven Effekten aufrufen.
    let drawOpaque renderer (models: Models) sector (field: AsteroidField) frustum time =
        for b in models.Parts do InstanceBatch.clear b
        InstanceBatch.clear models.Miner
        InstanceBatch.clear models.Beam
        for s in forSector sector do
            if Frustum.intersectsSphere frustum s.Position.X s.Position.Y s.Position.Z (s.Radius + 40.0) then
                for part, offset, rotation, scale in s.Parts do
                    let attitude =
                        if part = SightPart.Dish && sector = 4 then
                            let axis = Vec3.normalize (OuterRoutes.sites.[sector].Origin - s.Position)
                            let aim = Vec3.normalize (axis + Vec3.up * (0.24 * sin (time * 0.035 + 4.)))
                            Quat.fromAxisAngle (Vec3.normalize (Vec3.cross Vec3.up aim)) (acos (Vec3.dot Vec3.up aim)) * rotation
                        else s.Rotation * rotation
                    InstanceBatch.push models.Parts.[int part] (s.Position + Quat.rotateVec3 s.Rotation offset)
                        attitude (v scale scale scale) 1.0 1.0 1.0 0.0
        if models.MinerSector <> sector then
            models.MinerSector <- sector
            models.Miners <- createMiners sector
            models.NextScan <- 0.0
        if time >= models.NextScan then
            updateMiners models.Miners sector field
            models.NextScan <- time + 1.0
        for m in models.Miners do
            if m.Busy then
                let bob = v 0.0 (sin (time * 0.6 + float m.Slot) * 0.8) 0.0
                InstanceBatch.push models.Miner (m.Position + bob) m.Rotation Vec3.one 1.0 1.0 1.0 0.0
                let nose, direction, length = drill m
                let pulse = 0.65 + 0.35 * sin (time * 23.0 + float m.Slot * 2.0)
                InstanceBatch.push models.Beam (nose + bob) (Quat.lookRotation direction Vec3.up) (v pulse pulse length) pulse pulse pulse 1.0
        for b in models.Parts do if b.Count > 0 then Renderer.draw renderer b
        if models.Miner.Count > 0 then Renderer.draw renderer models.Miner

    /// Leuchtfeuer, Bohrfunken und Lichter. `lights`: Kandidaten für die Lichtauswahl.
    let pushLights (models: Models) sector time (lights: Lights) (cameraRotation: Quat) (viewer: Vec3) =
        InstanceBatch.clear models.Halo
        for s in forSector sector do
            for b in s.Beacons do
                let p = beaconPosition s b
                let k = intensity b time
                InstanceBatch.push models.Halo p cameraRotation (v b.Size b.Size b.Size) (b.Color.X * k) (b.Color.Y * k) (b.Color.Z * k) 1.0
                if k > 0.5 && Vec3.distance p viewer < 450.0 then
                    Lights.addPoint lights p.X p.Y p.Z (b.Size * 9.0) b.Color.X b.Color.Y b.Color.Z (0.9 * k)
            // Kometenschweif als Kette weicher Lichtballen, nach hinten größer und schwächer.
            match s.Tail with
            | Some (direction, length) ->
                let side = Vec3.normalize (Vec3.cross direction Vec3.up)
                for k in 0 .. TailPuffs - 1 do
                    let t = float k / float (TailPuffs - 1)
                    let sway = side * (sin (time * 0.35 + t * 4.0) * 10.0 * t)
                    let p = s.Position + direction * (25.0 + t * length) + sway
                    let size = 26.0 + 70.0 * t
                    let fade = 0.34 * Math.Pow(1.0 - t, 1.3)
                    InstanceBatch.push models.Halo p cameraRotation (v size size size) (0.45 * fade) (0.72 * fade) (1.0 * fade) 1.0
            | None -> ()
        for m in models.Miners do
            if m.Busy then
                let nose, direction, length = drill m
                let hit = nose + direction * length
                let flicker = 0.6 + 0.4 * sin (time * 31.0 + float m.Slot)
                InstanceBatch.push models.Halo hit cameraRotation (v 5.0 5.0 5.0) (1.0 * flicker) (0.55 * flicker) (0.18 * flicker) 1.0
                if Vec3.distance hit viewer < 400.0 then
                    Lights.addPoint lights hit.X hit.Y hit.Z 45.0 1.0 0.55 0.2 (0.8 * flicker)

    /// Additive Teile: Bohrstrahlen, Leuchtfeuer und Kometenschweif.
    let drawAdditive renderer (models: Models) =
        if models.Beam.Count > 0 then Renderer.draw renderer models.Beam
        if models.Halo.Count > 0 then Renderer.draw renderer models.Halo
