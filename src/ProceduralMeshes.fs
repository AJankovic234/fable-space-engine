namespace SpaceEngine

// =====================================================================================
//  ProceduralMeshes.fs  -  prozedurale Geometrie (alles Setup-Zeit, nichts pro Frame)
//
//  Alle Meshes sind flach schattiert: jedes Dreieck besitzt eigene
//  Vertices mit der Flaechennormale. Die Generatoren arbeiten mit einem kleinen
//  MeshBuilder, der die Windungsrichtung selbst korrigiert ("...Outward"): man
//  beschreibt nur die Form, nicht die Reihenfolge der Eckpunkte.
//
//  Schiffskonvention: Nase zeigt nach -Z, +Y ist oben, +X ist rechts.
// =====================================================================================

open System
open System.Collections.Generic
open Fable.Core

/// Kleiner deterministischer Zufallsgenerator (xorshift32). Gleicher Seed =
/// gleiche Welt - wichtig fuer reproduzierbare Meshes und Tests.
type Rng(seed: int) =
    let mutable state: uint32 = if seed = 0 then 0x9E3779B9u else uint32 seed

    /// Spielstände setzen die Zufallsfolge ohne erneute Weltgenerierung fort.
    member _.State with get() = state and set value = state <- value

    /// Gleichverteilt in [0, 1).
    member _.NextFloat() : float =
        let mutable x = state
        x <- x ^^^ (x <<< 13)
        x <- x ^^^ (x >>> 17)
        x <- x ^^^ (x <<< 5)
        state <- x
        float x / 4294967296.0

    /// Gleichverteilt in [min, max).
    member this.Range(min: float, max: float) : float = min + (max - min) * this.NextFloat()

    /// Gleichverteilter Punkt auf der Einheitskugel.
    member this.UnitVector() : Vec3 =
        let z = this.Range(-1.0, 1.0)
        let angle = this.Range(0.0, 2.0 * Math.PI)
        let r = sqrt (1.0 - z * z)
        Vec3(r * cos angle, r * sin angle, z)

/// Vertexfarbe: RGB (linear) plus Emissive (0 = beleuchtet, 1 = selbstleuchtend).
type MeshColor =
    { R: float
      G: float
      B: float
      Emissive: float }

[<RequireQualifiedAccess>]
module MeshColor =
    let rgb (r: float) (g: float) (b: float) : MeshColor =
        { R = r; G = g; B = b; Emissive = 0.0 }

    let glow (r: float) (g: float) (b: float) (emissive: float) : MeshColor =
        { R = r
          G = g
          B = b
          Emissive = emissive }

    let white = rgb 1.0 1.0 1.0

/// Sammelt flach schattierte Dreiecke und erzeugt daraus ein MeshData.
type MeshBuilder() =
    let data = ResizeArray<float>()
    let mutable vertexCount = 0

    member _.VertexCount = vertexCount

    member _.Vertex(p: Vec3, n: Vec3, c: MeshColor) =
        data.Add p.X
        data.Add p.Y
        data.Add p.Z
        data.Add n.X
        data.Add n.Y
        data.Add n.Z
        data.Add c.R
        data.Add c.G
        data.Add c.B
        data.Add c.Emissive
        vertexCount <- vertexCount + 1

    /// Dreieck (gegen den Uhrzeigersinn = Vorderseite) mit eigener Farbe pro Ecke.
    /// Entartete Dreiecke (Flaeche ~ 0) werden uebersprungen.
    member this.TriangleColors(a: Vec3, b: Vec3, c: Vec3, ca: MeshColor, cb: MeshColor, cc: MeshColor) =
        let n = Vec3.cross (b - a) (c - a)

        if Vec3.lengthSq n > 1e-14 then
            let n = Vec3.normalize n
            this.Vertex(a, n, ca)
            this.Vertex(b, n, cb)
            this.Vertex(c, n, cc)

    member this.Triangle(a: Vec3, b: Vec3, c: Vec3, color: MeshColor) =
        this.TriangleColors(a, b, c, color, color, color)

    /// Dreieck, dessen Vorderseite von `inside` weg zeigt - die Reihenfolge der
    /// Eckpunkte ist egal.
    member this.TriangleOutward(a: Vec3, b: Vec3, c: Vec3, inside: Vec3, color: MeshColor) =
        let n = Vec3.cross (b - a) (c - a)
        let center = (a + b + c) / 3.0

        if Vec3.dot n (center - inside) >= 0.0 then
            this.Triangle(a, b, c, color)
        else
            this.Triangle(a, c, b, color)

    /// Konvexes Polygon als Faecher, Vorderseite von `inside` weg.
    member this.PolygonOutward(points: Vec3[], inside: Vec3, color: MeshColor) =
        for i in 1 .. points.Length - 2 do
            this.TriangleOutward(points.[0], points.[i], points.[i + 1], inside, color)

    /// Hexaeder aus 8 Punkten: [0..3] = unterer Ring, [4..7] = oberer Ring
    /// (gleiche Umlaufrichtung).
    member this.Hexahedron(p: Vec3[], color: MeshColor) =
        let mutable sum = Vec3.zero

        for q in p do
            sum <- sum + q

        let inside = sum / 8.0
        this.PolygonOutward([| p.[0]; p.[1]; p.[2]; p.[3] |], inside, color)
        this.PolygonOutward([| p.[4]; p.[5]; p.[6]; p.[7] |], inside, color)
        this.PolygonOutward([| p.[0]; p.[1]; p.[5]; p.[4] |], inside, color)
        this.PolygonOutward([| p.[1]; p.[2]; p.[6]; p.[5] |], inside, color)
        this.PolygonOutward([| p.[2]; p.[3]; p.[7]; p.[6] |], inside, color)
        this.PolygonOutward([| p.[3]; p.[0]; p.[4]; p.[7] |], inside, color)

    /// Achsenparalleler Quader.
    member this.Box(center: Vec3, half: Vec3, color: MeshColor) =
        let corner (sx: float) (sy: float) (sz: float) =
            Vec3(center.X + half.X * sx, center.Y + half.Y * sy, center.Z + half.Z * sz)

        this.Hexahedron(
            [| corner -1.0 -1.0 -1.0
               corner 1.0 -1.0 -1.0
               corner 1.0 -1.0 1.0
               corner -1.0 -1.0 1.0
               corner -1.0 1.0 -1.0
               corner 1.0 1.0 -1.0
               corner 1.0 1.0 1.0
               corner -1.0 1.0 1.0 |],
            color
        )

    /// Platte: ein Viereck-Umriss, an jeder Ecke um +/- halfThickness entlang
    /// `normal` aufgedickt (Tragflaechen, Leitwerke).
    member this.Slab(outline: Vec3[], halfThickness: float[], normal: Vec3, color: MeshColor) =
        let bottom = Array.init 4 (fun i -> outline.[i] - normal * halfThickness.[i])
        let top = Array.init 4 (fun i -> outline.[i] + normal * halfThickness.[i])
        this.Hexahedron(Array.append bottom top, color)

    /// Verbindet aufeinanderfolgende Ringe (gleiche Punktzahl) zu einer Roehre.
    /// Ein auf einen Punkt geschrumpfter Ring ergibt eine Spitze.
    member this.Loft(rings: Vec3[][], capStart: bool, capEnd: bool, color: MeshColor) =
        let centroid (ring: Vec3[]) =
            let mutable sum = Vec3.zero

            for q in ring do
                sum <- sum + q

            sum / float ring.Length

        for r in 0 .. rings.Length - 2 do
            let a = rings.[r]
            let b = rings.[r + 1]
            let n = a.Length
            let inside = (centroid a + centroid b) * 0.5

            for i in 0 .. n - 1 do
                let j = (i + 1) % n
                this.TriangleOutward(a.[i], a.[j], b.[j], inside, color)
                this.TriangleOutward(a.[i], b.[j], b.[i], inside, color)

        if capStart then
            this.PolygonOutward(rings.[0], centroid rings.[1], color)

        if capEnd then
            let last = rings.Length - 1
            this.PolygonOutward(rings.[last], centroid rings.[last - 1], color)

    member _.Build() : MeshData =
        if vertexCount > 65535 then
            failwith "Mesh hat mehr als 65535 Vertices (16-Bit-Indizes)."

        let vertices = JS.Constructors.Float32Array.Create data.Count

        for i in 0 .. data.Count - 1 do
            vertices.[i] <- float32 data.[i]

        let indices = JS.Constructors.Uint16Array.Create vertexCount

        for i in 0 .. vertexCount - 1 do
            indices.[i] <- uint16 i

        { Vertices = vertices
          Indices = indices }

/// Befestigungspunkte und Abmessungen der Schiffe in lokalen Koordinaten
/// (fuer Triebwerkslichter, Abgasflammen, Laser-Muendungen und Kollision).
module ShipHardpoints =
    let playerEngineLeft = Vec3(-0.62, 0.02, 2.0)
    let playerEngineRight = Vec3(0.62, 0.02, 2.0)
    // Externe Schienen des Kepler-Bombers, in seiner tatsächlichen Modellgröße.
    let bomberRails = [| Vec3(-1.53, 0.53, 0.10); Vec3(1.53, 0.53, 0.10) |]
    let playerGunLeft = Vec3(-1.25, -0.1, -0.95)
    let playerGunRight = Vec3(1.25, -0.1, -0.95)
    /// Lokale Austrittsmitten des beweglichen Turret-Kopfes (vor Rueckstoss).
    let turretGunLeft = Vec3(-0.30, 0.20, -1.60)
    let turretGunRight = Vec3(0.30, 0.20, -1.60)
    /// Kameraposition der Cockpit-Perspektive (vor der Kanzel, Blick ueber die Nase).
    let playerCockpit = Vec3(0.0, 0.40, -1.3)
    /// Kollisionsbox (OBB) im Schiffssystem und Radius ihrer Huellkugel.
    let playerObbCenter = Vec3(0.0, 0.2, -0.3)
    let playerObbHalf = Vec3(2.1, 0.5, 2.2)
    let playerRadius = 3.4

    /// Gegnermodelle (EnemyMeshes) sind in Metern gebaut und werden mit diesem Faktor
    /// gezeichnet. Die Werte darunter beschreiben die Drohne, mit der ein frischer
    /// Gegner-Slot startet, bis EnemyProfiles.configure seine Rolle einträgt.
    let interceptorScale = 1.0
    /// Rohrabstand wie beim alten Modell: er bestimmt die Trefferquote mittiger Salven.
    let interceptorGunLeft = Vec3(-1.63, 0.0, -2.35)
    let interceptorGunRight = Vec3(1.63, 0.0, -2.35)
    let interceptorObbCenter = Vec3(0.0, 0.0, -0.05)
    let interceptorObbHalf = Vec3(2.6, 1.15, 2.1)
    let interceptorRadius = Vec3.length interceptorObbCenter + Vec3.length interceptorObbHalf

/// Der Außenkorb zeigt eine nach belegtem Volumen gewichtete Auswahl aus dem Laderaum.
module DeckCargo =
    let bays = [| Vec3(-1.64,0.18,0.79); Vec3(-1.64,0.18,0.14); Vec3(-1.64,0.18,-0.51) |]
    let sealedIndex = TradeSystem.commodities.Length
    let haulerMounts = [| for z in [| 0.24; -0.59; -1.42 |] do for x in [| -1.0; 1.0 |] do yield Vec3(x,0.472,z) |]
    let escortMounts = [| Vec3(-1.52,0.05,0.92); Vec3(1.52,0.05,0.92) |]
    let courierMounts = [| for z in [| -0.99; -0.27; 0.45 |] do for x in [| -1.36; 1.36 |] do yield Vec3(x,0.42,z) |]

    let iter (cargo: CargoHold) visit =
        let used = TradeSystem.usedVolume cargo
        let occupied = min bays.Length ((used * bays.Length + cargo.Capacity - 1) / cargo.Capacity)
        let mutable commodity = 0
        let volume i =
            if i = sealedIndex then cargo.Freight.Value.Tonnes
            else cargo.Quantities.[i] * TradeSystem.commodities.[i].UnitVolume
        let mutable endVolume = volume 0
        for bay in 0 .. occupied-1 do
            let sample = (2 * bay + 1) * used / (2 * occupied)
            while endVolume <= sample do
                commodity <- commodity + 1
                endVolume <- endVolume + volume commodity
            let visible = if bay = occupied - 1 && cargo.Freight.IsSome then sealedIndex else commodity
            visit bays.[bay] visible

[<RequireQualifiedAccess>]
module ProceduralMeshes =

    /// Drei verwachsene Prismen. Die Instanzfarbe bestimmt die Erzqualität.
    let mineralCrystal () : MeshData =
        let b = MeshBuilder()
        let shard (basePoint: Vec3) (axis: Vec3) radius phase =
            let mid = basePoint + axis * 0.70
            let tip = basePoint + axis
            let vertex (center: Vec3) size i =
                let angle = phase + float i * Math.PI / 3.0
                center + Vec3(cos angle * size, 0.0, sin angle * size)
            let inside = basePoint + axis * 0.45
            for i in 0..5 do
                let j = i + 1
                let shade = [|0.39; 0.67; 0.86; 0.54; 0.73; 0.92|].[i]
                let face = MeshColor.glow shade shade shade 0.31
                let crown = MeshColor.glow (shade+0.12) (shade+0.12) (shade+0.12) 0.61
                let low = vertex basePoint (radius*0.54) i
                let nextLow = vertex basePoint (radius*0.54) j
                let high = vertex mid radius i
                let nextHigh = vertex mid radius j
                b.PolygonOutward([|low; nextLow; nextHigh; high|], inside, face)
                b.TriangleOutward(tip,high,nextHigh,inside,crown)
                b.TriangleOutward(basePoint-Vec3(0.0,0.26,0.0),low,nextLow,inside,face)
        shard (Vec3(0.0,-0.63,0.0)) (Vec3(0.18,2.24,0.06)) 0.41 0.12
        shard (Vec3(-0.12,-0.72,0.09)) (Vec3(-0.85,1.38,0.22)) 0.32 0.56
        shard (Vec3(0.19,-0.60,0.12)) (Vec3(0.58,0.96,0.40)) 0.29 0.83
        b.Build()

    /// n-Eck in der XY-Ebene bei Tiefe z, Mittelpunkt (cx, cy), Halbachsen rx/ry.
    let internal ring (n: int, cx: float, cy: float, z: float, rx: float, ry: float) : Vec3[] =
        Array.init n (fun i ->
            let angle = 2.0 * Math.PI * float i / float n
            Vec3(cx + cos angle * rx, cy + sin angle * ry, z))

    /// Spiegelt Punkte an der YZ-Ebene (linke/rechte Schiffshaelfte).
    let private mirrorX (points: Vec3[]) : Vec3[] =
        points |> Array.map (fun p -> Vec3(-p.X, p.Y, p.Z))

    /// Aufgesetzte, gefaste Panzerplatte. Die dunkle Fase bleibt echte Geometrie,
    /// damit Plattenfugen auch ohne Textur im Streiflicht lesbar sind.
    let internal armorPanel (b: MeshBuilder) (outline: Vec3[]) (normal: Vec3) thickness color edge =
        let center = outline |> Array.fold (+) Vec3.zero |> fun p -> p / float outline.Length
        let top = outline |> Array.map (fun p -> Vec3.lerp p center 0.045 + normal * thickness)
        let inside = center + normal * (thickness * 0.4)
        b.PolygonOutward(outline, inside, edge)
        b.PolygonOutward(top, inside, color)

        for i in 0 .. outline.Length - 1 do
            let j = (i + 1) % outline.Length
            b.PolygonOutward([| outline.[i]; outline.[j]; top.[j]; top.[i] |], inside, edge)

    /// Vierkantstrebe zwischen beliebigen Punkten, etwa Kanzelrahmen oder Rippen.
    let internal beam (b: MeshBuilder) (start: Vec3) (finish: Vec3) width color =
        let axis = Vec3.normalize (finish - start)
        let reference = if abs axis.Y > 0.9 then Vec3.unitX else Vec3.unitY
        let right = Vec3.normalize (Vec3.cross axis reference) * width
        let up = Vec3.normalize (Vec3.cross axis right) * width
        let corners p = [| p - right - up; p + right - up; p + right + up; p - right + up |]
        b.Loft([| corners start; corners finish |], true, true, color)

    /// Free-floating approach buoy. The separate Fresnel lens takes the port's signal colour.
    let approachBuoy () : MeshData * MeshData =
        let frame = MeshBuilder()
        let lamp = MeshBuilder()
        let alloy = MeshColor.rgb 0.49 0.55 0.56
        let rubber = MeshColor.rgb 0.045 0.063 0.078
        let paint = MeshColor.rgb 0.88 0.50 0.09
        let lens = MeshColor.glow 1.0 1.0 1.0 1.0
        let collar y radius =
            ring(8,0.0,0.0,0.0,radius,radius) |> Array.map (fun p -> Vec3(p.X,y,p.Y))
        frame.Loft([|collar -1.85 0.38; collar -1.28 0.84; collar -0.58 0.84|],true,true,alloy)
        frame.Loft([|collar -1.13 0.86; collar -0.83 0.86|],false,false,paint)
        frame.Loft([|collar -0.58 0.61; collar 0.54 0.33; collar 0.79 0.81|],true,true,rubber)
        frame.Loft([|collar 0.78 0.82; collar 0.99 0.82|],true,true,alloy)
        frame.Loft([|collar 1.64 0.79; collar 1.88 0.46|],true,true,alloy)
        for i in 0..3 do
            let a = float i * Math.PI / 2.0
            let upright y r = Vec3(cos a*r,y,sin a*r)
            beam frame (upright 0.92 0.73) (upright 1.68 0.71) 0.07 rubber
            let fin = [|upright -0.6 0.48; upright -0.98 1.15; upright -1.72 1.20; upright -1.43 0.36|]
            frame.Slab(fin,[|0.07;0.055;0.055;0.07|],Vec3(-sin a,0.0,cos a),paint)
        lamp.Loft([|collar 0.98 0.57;collar 1.17 0.70;collar 1.45 0.70;collar 1.65 0.54|],true,true,lens)
        frame.Build(), lamp.Build()

    let private jumpGateFrameParts strut box =
        let hull = MeshColor.rgb 0.56 0.63 0.66
        let steel = MeshColor.rgb 0.14 0.19 0.24
        let trim = MeshColor.rgb 0.82 0.45 0.10
        let beaconCyan = MeshColor.glow 0.18 0.92 1.15 1.4
        let beaconAmber = MeshColor.glow 1.25 0.66 0.14 1.5
        let n = 12
        let front = ring(n, 0.0, 0.0, -3.6, 22.8, 22.8)
        let mid = ring(n, 0.0, 0.0, 0.0, 25.4, 25.4)
        let rear = ring(n, 0.0, 0.0, 3.6, 22.8, 22.8)
        for i in 0 .. n - 1 do
            let j = (i + 1) % n
            strut mid.[i] mid.[j] 1.35 (if i % 3 = 0 then trim else hull)
            strut front.[i] front.[j] 0.68 steel
            strut rear.[i] rear.[j] 0.68 steel
            strut front.[i] mid.[i] 0.52 steel
            strut rear.[i] mid.[i] 0.52 steel
            strut front.[i] mid.[j] 0.34 hull
        for k in 0 .. 3 do
            let a = Math.PI * 0.25 + float k * Math.PI * 0.5
            let dir = Vec3(cos a, sin a, 0.0)
            let tang = Vec3(-sin a, cos a, 0.0)
            let pIn = dir * 20.6
            let pBase = dir * 24.4
            let pTip = dir * 32.0
            strut (pBase - tang * 1.4) pTip 0.95 steel
            strut (pBase + tang * 1.4) pTip 0.95 steel
            strut (pBase - Vec3(0.0, 0.0, 3.6)) pTip 0.62 hull
            strut (pBase + Vec3(0.0, 0.0, 3.6)) pTip 0.62 hull
            strut (dir * 23.0) (pTip + dir * 0.8) 1.05 trim
            // Tip beacon & inner emitter head
            box (pTip + dir * 1.35) (Vec3(1.15, 1.15, 1.45)) beaconAmber
            box pIn (Vec3(0.85, 0.85, 1.85)) beaconCyan

    let jumpGateOpeningRadius = 16.1
    let jumpGateRingSegments = 12

    let private jumpGateInnerEdge =
        ring(jumpGateRingSegments, 0.0, 0.0, 0.0, jumpGateOpeningRadius, jumpGateOpeningRadius)

    let jumpGateEmitters =
        jumpGateInnerEdge |> Array.mapi (fun i p ->
            let mid = Vec3.lerp p jumpGateInnerEdge.[(i + 1) % jumpGateRingSegments] 0.5
            mid * (if i % 2 = 0 then 0.92 else 1.0))

    let private jumpGateRingParts strut box =
        let alloy = MeshColor.rgb 0.72 0.79 0.85
        let core = MeshColor.rgb 0.22 0.30 0.38
        let coil = MeshColor.glow 1.0 1.0 1.0 1.35
        let n = jumpGateRingSegments
        let outer = ring(n, 0.0, 0.0, 0.0, 18.2, 18.2)
        for i in 0 .. n - 1 do
            let j = (i + 1) % n
            strut outer.[i] outer.[j] 0.62 alloy
            for z in [-1.25; 1.25] do
                let depth = Vec3(0.0, 0.0, z)
                strut (jumpGateInnerEdge.[i] + depth) (jumpGateInnerEdge.[j] + depth) 0.36 core
            if i % 2 = 0 then
                let mid = Vec3.lerp jumpGateInnerEdge.[i] jumpGateInnerEdge.[j] 0.5
                strut ((mid + Vec3(0.0, 0.0, -1.25)) * 0.96) outer.[i] 0.55 alloy
            let half = if i % 2 = 0 then Vec3(0.75, 0.75, 1.45) else Vec3(0.45, 0.45, 1.3)
            box jumpGateEmitters.[i] half coil

    let private jumpGateMesh parts =
        let b = MeshBuilder()
        parts (beam b) (fun center half color -> b.Box(center, half, color))
        b.Build()

    let jumpGateFrame () = jumpGateMesh jumpGateFrameParts
    let jumpGateRing () = jumpGateMesh jumpGateRingParts

    /// Local oriented boxes built from the same struts and emitters as both meshes.
    let private jumpGateBoxes parts =
        let boxes = ResizeArray<_>()
        let strut start finish width _ =
            let delta = finish - start
            let axis = Vec3.normalize delta
            let reference = if abs axis.Y > 0.9 then Vec3.unitX else Vec3.unitY
            let right = Vec3.normalize (Vec3.cross axis reference)
            boxes.Add(struct (Vec3.lerp start finish 0.5, right, Vec3.cross axis right, axis,
                              Vec3(width, width, Vec3.length delta * 0.5)))
        let box center half _ =
            boxes.Add(struct (center, Vec3.unitX, Vec3.unitY, Vec3.unitZ, half))
        parts strut box
        boxes.ToArray()

    let jumpGateFrameBoxes = jumpGateBoxes jumpGateFrameParts
    let jumpGateRingBoxes = jumpGateBoxes jumpGateRingParts

    /// Sechs geborgene Datenträger. Gehäuse und Statuslicht teilen sich ein Mesh.
    let archiveRelic (kind: StationKind) (recorderOnBoard: bool) : MeshData =
        let b = MeshBuilder()
        let iron = MeshColor.rgb 0.17 0.23 0.26
        let alloy = MeshColor.rgb 0.58 0.65 0.63
        let ceramic = MeshColor.rgb 0.74 0.72 0.60
        let oxide = MeshColor.rgb 0.52 0.23 0.10
        let brass = MeshColor.rgb 0.66 0.47 0.20
        let signal = MeshColor.glow 0.87 0.41 1.12 0.85
        let box x y z hx hy hz c = b.Box(Vec3(x,y,z),Vec3(hx,hy,hz),c)
        let bar p q w c = beam b p q w c
        let rim (points: Vec3[]) w c =
            for i in 0 .. points.Length-1 do bar points.[i] points.[(i+1)%points.Length] w c
        match kind with
        | MiningDepot ->
            // Abgerissenes Bugsegment. Die schwarzen Bruchkanten legen den orangefarbenen Rekorder frei.
            let nose = ring(6,-0.4,-0.1,-4.6,0.9,0.72)
            let shoulder = ring(6,0.0,0.0,-1.4,3.35,1.95)
            let torn = shoulder |> Array.mapi (fun i p -> Vec3(p.X,p.Y,[|2.4;0.4;1.65;3.3;1.05;2.0|].[i]))
            b.Loft([|nose;shoulder|],true,false,ceramic)
            for i in [0;2;3;4] do
                let j = (i+1)%6
                b.PolygonOutward([|shoulder.[i];shoulder.[j];torn.[j];torn.[i]|],Vec3.zero,alloy)
                bar shoulder.[i] torn.[i] 0.12 iron
            bar (Vec3(0.0,-1.2,-2.2)) (Vec3(3.95,-1.1,3.8)) 0.24 iron
            if recorderOnBoard then
                box 0.45 -0.15 1.25 1.12 0.66 1.16 oxide
                box 0.45 -0.12 2.44 0.7 0.17 0.05 signal
        | Refinery ->
            // Genormter feuerfester Schichtlistenbehälter, in zwei Transportkufen verriegelt.
            box 0.0 0.0 0.0 2.85 1.75 1.95 oxide
            box 0.0 1.78 0.0 2.97 0.14 2.04 iron
            for side in [-1.0;1.0] do
                let x = side*1.86
                box x 0.02 0.0 0.14 1.85 2.0 alloy
                box x -1.9 0.0 0.31 0.2 2.44 iron
                box x 1.12 2.05 0.3 0.4 0.13 brass
            box 0.0 0.34 2.02 1.0 0.57 0.08 ceramic
            box 0.0 0.07 2.13 0.62 0.08 0.03 signal
        | FrontierBazaar ->
            // Horchboje mit offener Parabolantenne, Dreibein und abgesetztem Empfänger.
            let hub = Vec3(0.0,1.0,0.15)
            let lip = ring(16,0.0,1.0,1.7,3.1,3.1)
            let middle = ring(16,0.0,1.0,0.45,1.48,1.48)
            rim lip 0.12 alloy
            rim middle 0.07 iron
            for i in 0..7 do
                bar hub middle.[2*i] 0.09 brass
                bar middle.[2*i] lip.[2*i] 0.09 brass
            let receiver = Vec3(0.0,1.0,3.5)
            for i in [0;5;11] do bar lip.[i] receiver 0.09 alloy
            box receiver.X receiver.Y receiver.Z 0.27 0.27 0.32 signal
            box 0.0 -0.8 -0.5 0.62 1.6 0.6 iron
            for i in 0..2 do
                let a = float i * 2.0*Math.PI/3.0
                bar (Vec3(0.0,-1.6,-0.5)) (Vec3(2.25*cos a,-3.8,-0.5+2.25*sin a)) 0.13 alloy
        | OrbitalYard ->
            // Freie Spanten und ein Fundamentholm, ohne Außenhaut.
            let frames =
                [| for z,r in [ -5.7,2.15; -2.5,3.65; 1.0,3.85; 4.75,2.85 ] ->
                    ring(8,0.0,0.0,z,r,r*0.8) |]
            for f in frames do rim f 0.19 alloy
            for i in [0;4;6] do bar frames.[0].[i] frames.[3].[i] 0.22 iron
            for i in 0..2 do bar frames.[i].[5] frames.[i+1].[6] 0.16 oxide
            box 0.0 -2.86 0.0 0.55 0.32 6.3 oxide
            box 0.0 -2.84 5.95 0.35 0.18 0.39 signal
        | ResearchRelay ->
            // Gestaffelte Strahlenschutzplatten, dahinter ein Bandgerät mit zwei Spulen.
            box 0.0 0.0 -0.15 2.0 1.6 2.65 iron
            for z in [-2.75;-1.9;-1.05;-0.2;0.65;1.5] do
                b.Loft([|ring(8,0.0,0.0,z,2.9,2.45);ring(8,0.0,0.0,z+0.4,2.9,2.45)|],true,true,ceramic)
            box 0.0 0.0 2.52 2.12 1.45 0.27 alloy
            for x in [-0.92;0.92] do
                b.Loft([|ring(12,x,0.24,2.8,0.75,0.75);ring(12,x,0.24,2.95,0.75,0.75)|],true,true,iron)
                box x 0.24 2.99 0.17 0.17 0.06 brass
            box 0.0 -0.91 2.82 0.73 0.12 0.05 signal
        | FleetDock ->
            // Hermetische Bergungskapsel mit Kühlschlaufen und zwei langen Kollektoren.
            let shell z r = ring(12,0.0,0.0,z,r,r*0.88)
            b.Loft([|shell -4.0 0.65;shell -3.1 1.75;shell 2.6 1.75;shell 3.65 0.9|],true,true,alloy)
            for z in [-2.4;1.9] do
                b.Loft([|shell z 1.83;shell (z+0.3) 1.83|],false,false,brass)
            for side in [-1.0;1.0] do
                bar (Vec3(side,0.0,0.0)) (Vec3(side*6.85,0.0,0.0)) 0.15 brass
                for i in 0..2 do
                    let x = side*(3.1+float i*1.43)
                    box x 0.02 0.0 0.70 0.10 1.8 brass
                    box x 0.15 0.0 0.61 0.04 1.68 (MeshColor.rgb 0.09 0.24 0.38)
            box 0.0 0.0 3.68 0.46 0.16 0.05 signal
        b.Build()

    /// Offene Duese mit Metallrand, dunkler Innenwand und tiefer liegendem Kern.
    /// Die sichtbare Austrittsflaeche bleibt genau am bestehenden Hardpoint.
    let private nozzle (b: MeshBuilder) x y z radius depth metal interior glow =
        let segments = 12
        let outerBack = ring (segments, x, y, z - depth, radius * 1.05, radius * 1.05)
        let outerLip = ring (segments, x, y, z, radius, radius)
        let innerLip = ring (segments, x, y, z, radius * 0.77, radius * 0.77)
        let core = ring (segments, x, y, z - depth * 0.65, radius * 0.62, radius * 0.62)
        b.Loft([| outerBack; outerLip |], false, false, metal)

        for i in 0 .. segments - 1 do
            let j = (i + 1) % segments
            b.Triangle(outerLip.[i], outerLip.[j], innerLip.[j], metal)
            b.Triangle(outerLip.[i], innerLip.[j], innerLip.[i], metal)
            // Innenwand: Normalen zeigen in den offenen Brennraum.
            b.Triangle(innerLip.[i], innerLip.[j], core.[j], interior)
            b.Triangle(innerLip.[i], core.[j], core.[i], interior)

        b.PolygonOutward(core, Vec3(x, y, z - depth), glow)

    /// Civilian line hauler, built around a keel with two rows of removable pods.
    let freightHauler () : MeshData =
        let b = MeshBuilder()
        let steel = MeshColor.rgb 0.23 0.29 0.32
        let enamel = MeshColor.rgb 0.80 0.78 0.66
        let recess = MeshColor.rgb 0.044 0.070 0.085
        let ochre = MeshColor.rgb 0.88 0.37 0.055
        let oxide = MeshColor.rgb 0.55 0.17 0.075
        let ice = MeshColor.glow 0.30 0.78 0.94 0.68
        let yellow = MeshColor.glow 1.0 0.58 0.10 0.9
        let box x y z hx hy hz color = b.Box(Vec3(x,y,z),Vec3(hx,hy,hz),color)
        let crossSection x y z w h =
            let c = min w h * 0.32
            [| Vec3(x-w+c,y+h,z); Vec3(x+w-c,y+h,z); Vec3(x+w,y+h-c,z); Vec3(x+w,y-h+c,z)
               Vec3(x+w-c,y-h,z); Vec3(x-w+c,y-h,z); Vec3(x-w,y-h+c,z); Vec3(x-w,y+h-c,z) |]
        // High forward bridge, narrow load-bearing spine, and a broad engine house.
        box 0.0 -0.6 0.0 1.45 1.24 12.0 steel
        b.Loft([|crossSection 0.0 0.0 -15.5 2.25 1.5; crossSection 0.0 0.4 -13.3 3.65 2.5
                 crossSection 0.0 0.4 -9.0 3.65 2.5; crossSection 0.0 0.0 -8.0 2.55 1.6|],true,true,enamel)
        b.Loft([|crossSection 0.0 2.1 -14.3 2.1 0.46; crossSection 0.0 2.9 -11.8 3.05 0.94
                 crossSection 0.0 2.9 -10.0 3.05 0.94|],true,true,steel)
        for side in [-1.0;1.0] do
            for window in 0..2 do
                box (side*3.09) 2.92 (-11.68+0.66*float window) 0.06 0.40 0.22 ice
            box (side*1.1) 2.15 -14.34 0.78 0.20 0.10 ice
            // Exposed floor rails and six separate ribbed containers.
            box (side*3.2) -1.55 0.0 1.46 0.44 9.6 steel
            for bay in 0..2 do
                let z = -5.8 + 5.8 * float bay
                let x = side*3.05
                let paint = if (bay = 0) = (side > 0.0) then oxide else ochre
                let section dz w h = crossSection x 0.63 (z+dz) w h
                b.Loft([|section -2.66 1.76 1.65; section -2.35 1.96 1.83
                         section 2.35 1.96 1.83; section 2.66 1.76 1.65|],true,true,paint)
                for strap in [-1.85;1.85] do
                    b.Loft([|section (strap-0.13) 2.03 1.9;section (strap+0.13) 2.03 1.9|],false,false,steel)
                for rib in -1..1 do
                    box (side*5.03) 0.63 (z+float rib*0.65) 0.055 1.1 0.07 paint
                box (side*5.11) 1.35 (z-0.6) 0.03 0.22 0.51 enamel
            let x = side * 2.45
            b.Loft([|crossSection x 0.34 8.66 2.1 1.9; crossSection x 0.40 10.3 2.56 2.4
                     crossSection x 0.40 13.1 2.32 2.15|],true,true,enamel)
            nozzle b x 0.38 14.8 1.65 2.0 steel recess ice
            for fin in 0..3 do box x (2.7+float fin*0.29) 11.3 1.92 0.08 1.75 steel
            box (side*4.95) 0.6 10.1 0.18 0.22 0.65 yellow
        b.Build()

    /// Offene achtkantige Waffenmuendung nach -Z; keine Scheibe vor dem Lauf.
    let internal turretBore (b: MeshBuilder) x y z radius depth metal dark cyan =
        let back = ring (8, x, y, z + depth, radius * 1.06, radius * 1.06)
        let outer = ring (8, x, y, z, radius, radius)
        let inner = ring (8, x, y, z, radius * 0.72, radius * 0.72)
        let core = ring (8, x, y, z + depth * 0.78, radius * 0.52, radius * 0.52)
        b.Loft([| back; outer |], false, false, metal)
        for i in 0 .. 7 do
            let j = (i + 1) % 8
            b.PolygonOutward([| outer.[i]; outer.[j]; inner.[j]; inner.[i] |], Vec3(x, y, z + depth), metal)
            let wallInside =
                Vec3((outer.[i].X + outer.[j].X) * 0.5, (outer.[i].Y + outer.[j].Y) * 0.5, z + depth * 0.4)
            b.PolygonOutward([| inner.[i]; inner.[j]; core.[j]; core.[i] |], wallInside, dark)
        b.PolygonOutward(core, Vec3(x, y, z + depth), cyan)

    /// Fester, am Deck anliegender Drehkranz. Der Kopf wird separat geschwenkt.
    let autoTurretBase () : MeshData =
        let b = MeshBuilder()
        let steel = MeshColor.rgb 0.18 0.25 0.31
        let edge = MeshColor.rgb 0.36 0.44 0.49
        let dark = MeshColor.rgb 0.035 0.052 0.068
        let ivory = MeshColor.rgb 0.80 0.81 0.75
        let cyan = MeshColor.glow 0.035 0.72 0.95 0.65
        let amber = MeshColor.rgb 0.95 0.52 0.075
        let horizontal y radius =
            ring (16, 0.0, 0.0, 0.0, radius, radius)
            |> Array.map (fun p -> Vec3(p.X, y, p.Y))

        // Breiter Deckflansch, gefaste Trommel und dunkles Kugellager.
        b.Loft([| horizontal -0.48 0.49; horizontal -0.435 0.55; horizontal -0.40 0.55 |], true, true, edge)
        b.Loft([| horizontal -0.405 0.53; horizontal -0.29 0.51; horizontal -0.245 0.44 |], true, true, steel)
        b.Loft([| horizontal -0.25 0.425; horizontal -0.12 0.425; horizontal -0.05 0.32 |], true, true, dark)
        b.Loft([| horizontal -0.155 0.432; horizontal -0.137 0.432 |], false, false, cyan)
        b.Loft([| horizontal -0.13 0.435; horizontal -0.085 0.385 |], false, false, edge)

        for i in 0 .. 7 do
            let angle = float i * Math.PI / 4.0
            let radial = Vec3(cos angle, 0.0, sin angle)
            let tangent = Vec3(-sin angle, 0.0, cos angle)
            let point radius y side = radial * radius + tangent * side + Vec3(0.0, y, 0.0)
            armorPanel b
                [| point 0.535 -0.425 -0.068; point 0.535 -0.425 0.068
                   point 0.475 -0.26 0.056; point 0.475 -0.26 -0.056 |]
                radial 0.016 (if i % 2 = 0 then ivory else steel) edge
            // Schraubenkopf und tragende Strebe bleiben aus jeder Richtung erkennbar.
            let boltCenter = point 0.40 -0.23 0.0
            let boltRing y =
                ring (8, 0.0, 0.0, 0.0, 0.025, 0.025)
                |> Array.map (fun p -> Vec3(boltCenter.X + p.X, y, boltCenter.Z + p.Y))
            b.Loft([| boltRing -0.24; boltRing -0.215 |], true, true, dark)
            beam b (point 0.41 -0.24 0.0) (point 0.34 -0.07 0.0) 0.022 steel
            if i % 2 = 0 then
                armorPanel b
                    [| point 0.531 -0.38 -0.037; point 0.531 -0.38 -0.011
                       point 0.503 -0.31 0.037; point 0.503 -0.31 0.011 |]
                    radial 0.021 amber dark
        b.Build()

    /// Nur der bewegliche Kopf: zwei getrennte Laeufe, Gelenke und Serviceheck.
    /// Proportionen und Materialaufteilung folgen dem vorhandenen Turret-Asset.
    let autoTurret () : MeshData =
        let b = MeshBuilder()
        let blue = MeshColor.rgb 0.22 0.32 0.40
        let steel = MeshColor.rgb 0.13 0.19 0.24
        let edge = MeshColor.rgb 0.45 0.53 0.57
        let ivory = MeshColor.rgb 0.83 0.84 0.77
        let dark = MeshColor.rgb 0.028 0.043 0.058
        let cyan = MeshColor.glow 0.035 0.78 1.02 0.78
        let dimCyan = MeshColor.glow 0.045 0.40 0.50 0.35
        let amber = MeshColor.rgb 0.98 0.52 0.055
        let rotor y radius =
            ring (16, 0.0, 0.0, 0.0, radius, radius)
            |> Array.map (fun p -> Vec3(p.X, y, p.Y))
        b.Loft([| rotor -0.055 0.30; rotor 0.025 0.32; rotor 0.065 0.27 |], true, true, steel)
        b.Loft(
            [| ring (8, 0.0, 0.205, -0.39, 0.35, 0.15)
               ring (8, 0.0, 0.205, -0.16, 0.46, 0.20)
               ring (8, 0.0, 0.205, 0.34, 0.43, 0.20)
               ring (8, 0.0, 0.205, 0.46, 0.36, 0.15) |], true, true, dark)

        for side in [| -1.0; 1.0 |] do
            let normal = Vec3(side, 0.0, 0.0)
            // Die Schulterplatten verjuengen sich nach vorn; offene Fugen zeigen das Chassis.
            armorPanel b
                [| Vec3(0.18 * side, 0.39, -0.32); Vec3(0.43 * side, 0.34, -0.34)
                   Vec3(0.50 * side, 0.37, 0.25); Vec3(0.33 * side, 0.43, 0.39)
                   Vec3(0.16 * side, 0.43, 0.28) |]
                Vec3.unitY 0.035 ivory edge
            armorPanel b
                [| Vec3(0.455 * side, 0.10, -0.37); Vec3(0.515 * side, 0.12, -0.25)
                   Vec3(0.535 * side, 0.31, 0.24); Vec3(0.485 * side, 0.36, 0.40)
                   Vec3(0.445 * side, 0.08, 0.40) |]
                normal 0.023 ivory blue
            beam b (Vec3(0.39 * side, 0.045, 0.32)) (Vec3(0.43 * side, 0.11, -0.28)) 0.022 edge

            // Seitliche Elevationsgelenke mit eingelassenem Leuchtring und vier Schrauben.
            let hinge x radius =
                ring (12, 0.0, 0.0, 0.0, radius, radius)
                |> Array.map (fun p -> Vec3(x * side, 0.20 + p.X, 0.09 + p.Y))
            b.Loft([| hinge 0.49 0.145; hinge 0.55 0.16; hinge 0.58 0.13 |], true, true, steel)
            b.PolygonOutward(hinge 0.581 0.107, Vec3(0.5 * side, 0.20, 0.09), cyan)
            b.PolygonOutward(hinge 0.584 0.085, Vec3(0.5 * side, 0.20, 0.09), blue)
            for i in 0 .. 3 do
                let angle = float i * Math.PI / 2.0 + Math.PI / 4.0
                b.Box(Vec3(0.587 * side, 0.20 + cos angle * 0.108, 0.09 + sin angle * 0.108), Vec3(0.008, 0.012, 0.012), edge)

            let gun = if side < 0.0 then ShipHardpoints.turretGunLeft else ShipHardpoints.turretGunRight
            let x = gun.X
            let y = gun.Y
            // Achtkantiger Verschluss, freier Lauf und vier getrennte Kuehlmanschetten.
            b.Loft([| ring (8, x, y, 0.14, 0.155, 0.155)
                      ring (8, x, y, -0.31, 0.17, 0.155)
                      ring (8, x, y, -0.54, 0.125, 0.12) |], true, true, steel)
            b.Loft([| ring (8, x, y, -0.45, 0.103, 0.103)
                      ring (8, x, y, -0.77, 0.095, 0.095)
                      ring (8, x, y, -1.44, 0.085, 0.085) |], true, false, dark)
            for z in [| -0.59; -0.79; -0.99; -1.19 |] do
                b.Loft([| ring (8, x, y, z, 0.117, 0.115)
                          ring (8, x, y, z - 0.055, 0.117, 0.115) |], true, true, edge)
            armorPanel b
                [| Vec3(x - 0.10, y + 0.115, -0.56); Vec3(x + 0.10, y + 0.115, -0.56)
                   Vec3(x + 0.077, y + 0.098, -1.40); Vec3(x - 0.077, y + 0.098, -1.40) |]
                Vec3.unitY 0.024 ivory edge
            armorPanel b
                [| Vec3(x + side * 0.112, y - 0.065, -0.66); Vec3(x + side * 0.112, y + 0.045, -0.66)
                   Vec3(x + side * 0.098, y + 0.035, -1.36); Vec3(x + side * 0.098, y - 0.065, -1.36) |]
                normal 0.014 blue edge
            beam b (Vec3(x, y + 0.15, -0.63)) (Vec3(x, y + 0.132, -1.32)) 0.014 steel
            beam b (Vec3(x + side * 0.121, y - 0.027, -1.08)) (Vec3(x + side * 0.115, y - 0.027, -1.36)) 0.012 cyan
            beam b (Vec3(x - side * 0.08, y - 0.105, 0.03)) (Vec3(x - side * 0.08, y - 0.105, -0.88)) 0.018 steel
            for i in 0 .. 2 do
                let z = -0.73 - float i * 0.09
                armorPanel b
                    [| Vec3(x + side * 0.128, y + 0.055, z); Vec3(x + side * 0.128, y + 0.055, z - 0.037)
                       Vec3(x + side * 0.128, y - 0.002, z - 0.065); Vec3(x + side * 0.128, y - 0.002, z - 0.028) |]
                    normal 0.004 amber dark
            turretBore b x y gun.Z 0.13 0.17 edge dark cyan

            // Heckduesen sind kleine Waermetauscher; sie leuchten schwaecher als die Optik.
            nozzle b (side * 0.28) 0.16 0.535 0.075 0.08 edge dark dimCyan
            b.Box(Vec3(side * 0.28, 0.305, 0.493), Vec3(0.055, 0.012, 0.010), cyan)

        // Erhoehte, eigenstaendige Tracking-Optik zwischen den beiden Waffen.
        b.Loft([| ring (8, 0.0, 0.425, 0.09, 0.125, 0.095)
                  ring (8, 0.0, 0.425, -0.30, 0.145, 0.105)
                  ring (8, 0.0, 0.425, -0.45, 0.11, 0.09) |], true, false, blue)
        turretBore b 0.0 0.425 -0.485 0.086 0.075 ivory dark cyan
        armorPanel b
            [| Vec3(-0.092, 0.516, -0.26); Vec3(0.092, 0.516, -0.26)
               Vec3(0.080, 0.507, 0.075); Vec3(-0.080, 0.507, 0.075) |]
            Vec3.unitY 0.018 blue edge

        // Im Verfolgerblick lesbares Serviceheck mit Fase, dunklem Einsatz und Kuehlrippen.
        armorPanel b
            [| Vec3(-0.34, 0.07, 0.462); Vec3(0.34, 0.07, 0.462)
               Vec3(0.37, 0.29, 0.462); Vec3(0.25, 0.365, 0.462)
               Vec3(-0.25, 0.365, 0.462); Vec3(-0.37, 0.29, 0.462) |]
            Vec3.unitZ 0.022 ivory edge
        b.Box(Vec3(0.0, 0.215, 0.495), Vec3(0.19, 0.105, 0.012), dark)
        for i in 0 .. 4 do
            b.Box(Vec3(0.0, 0.145 + float i * 0.035, 0.515), Vec3(0.165, 0.008, 0.016), steel)
        b.Box(Vec3(0.0, 0.339, 0.497), Vec3(0.085, 0.010, 0.009), amber)
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Spantenprofil und Decksfracht; die Schiffe des Spielers stehen in PlayerMeshes
    // ---------------------------------------------------------------------------------

    /// Achtkantspant mit flachen Deckflächen für aufgesetzte Baugruppen.
    let internal hullSection x z width bottom top =
        let bevel = (top - bottom) * 0.23
        [| Vec3(x - width * 0.74, top, z); Vec3(x + width * 0.74, top, z)
           Vec3(x + width, top - bevel, z); Vec3(x + width, bottom + bevel, z)
           Vec3(x + width * 0.74, bottom, z); Vec3(x - width * 0.74, bottom, z)
           Vec3(x - width, bottom + bevel, z); Vec3(x - width, top - bevel, z) |]

    // ---------------------------------------------------------------------------------
    let sealedFreight () : MeshData =
        let b = MeshBuilder()
        let case = MeshColor.rgb 0.57 0.13 0.036
        let lid = MeshColor.rgb 0.84 0.29 0.07
        let strap = MeshColor.rgb 0.095 0.14 0.16
        let label = MeshColor.rgb 0.90 0.85 0.62
        let seal = MeshColor.glow 0.12 0.91 0.78 0.68
        let box x y z hx hy hz color = b.Box(Vec3(x,y,z),Vec3(hx,hy,hz),color)
        b.Loft([| hullSection 0.0 -0.27 0.27 0.035 0.32; hullSection 0.0 0.27 0.27 0.035 0.32 |],true,true,case)
        box 0.0 0.325 0.0 0.27 0.022 0.27 lid
        for z in [-0.19;0.19] do
            box 0.0 0.35 z 0.27 0.014 0.027 strap
            for x in [-0.27;0.27] do box x 0.18 z 0.013 0.17 0.027 strap
        for x in [-0.18;0.18] do box x 0.028 0.0 0.048 0.028 0.28 strap
        box -0.055 0.35 0.0 0.111 0.006 0.069 label
        for i in 0..4 do box (-0.14 + 0.028 * float i) 0.358 0.0 0.006 0.004 0.047 strap
        box 0.14 0.287 0.277 0.065 0.024 0.012 seal
        b.Build()

    let deckPackage (kind: CommodityId) : MeshData =
        let b = MeshBuilder()
        let iron = MeshColor.rgb 0.18 0.24 0.25
        let alloy = MeshColor.rgb 0.65 0.69 0.63
        let cream = MeshColor.rgb 0.91 0.87 0.74
        let box x y z hx hy hz color = b.Box(Vec3(x,y,z),Vec3(hx,hy,hz),color)
        match kind with
        | CommodityId.TitaniumScrap ->
            for tier in 0..2 do
                let y = float tier * 0.103
                let offset = if tier = 1 then -0.024 else 0.018
                b.Loft([|hullSection offset -0.25 0.26 y (y+0.095);hullSection offset 0.25 0.24 y (y+0.095)|],true,true,alloy)
            for x in [-0.15;0.15] do box x 0.31 0.0 0.023 0.016 0.25 iron
        | CommodityId.CryoOre | CommodityId.FuelCells | CommodityId.PlasmaCores ->
            let paint =
                match kind with
                | CommodityId.CryoOre -> MeshColor.rgb 0.18 0.61 0.77
                | CommodityId.FuelCells -> MeshColor.rgb 0.94 0.51 0.08
                | _ -> MeshColor.rgb 0.68 0.36 0.68
            box 0.0 0.047 0.0 0.296 0.047 0.255 iron
            for x in [-0.148;0.148] do
                let collar y radius = ring(10,x,0.0,y,radius,radius) |> Array.map (fun v -> Vec3(v.X,v.Z,v.Y))
                b.Loft([|collar 0.08 0.11;collar 0.12 0.14;collar 0.38 0.14;collar 0.43 0.087|],true,true,paint)
                b.Loft([|collar 0.33 0.145;collar 0.36 0.145|],false,false,cream)
                b.Loft([|collar 0.43 0.076;collar 0.47 0.064|],true,true,alloy)
        | _ ->
            let med = kind = CommodityId.MedSupplies
            let seal = if med then MeshColor.rgb 0.08 0.55 0.39 else MeshColor.rgb 0.25 0.34 0.68
            b.Loft([|hullSection 0.0 -0.255 0.272 0.0 0.33;hullSection 0.0 0.255 0.272 0.0 0.33|],true,true,cream)
            box 0.0 0.33 0.0 0.29 0.030 0.258 seal
            for x in [-0.18;0.18] do box x 0.17 0.0 0.025 0.169 0.262 seal
            if med then box 0.0 0.373 0.0 0.09 0.011 0.15 cream
            else
                for z in [-0.12;0.12] do beam b (Vec3(-0.10,0.41,z)) (Vec3(0.10,0.41,z)) 0.025 iron
        b.Build()

    //  Asteroid: verformter Ikosaeder (Radius der Huellkugel = 1)
    // ---------------------------------------------------------------------------------

    /// Ikosphaere mit gemeinsamen Eckpunkten (noetig, damit die Verformung
    /// keine Risse zwischen Nachbardreiecken erzeugt).
    let private icosphere (subdivisions: int) : Vec3[] * int[] =
        let t = (1.0 + sqrt 5.0) / 2.0

        let raw =
            [| -1.0; t; 0.0
               1.0; t; 0.0
               -1.0; -t; 0.0
               1.0; -t; 0.0
               0.0; -1.0; t
               0.0; 1.0; t
               0.0; -1.0; -t
               0.0; 1.0; -t
               t; 0.0; -1.0
               t; 0.0; 1.0
               -t; 0.0; -1.0
               -t; 0.0; 1.0 |]

        let positions = ResizeArray<Vec3>()

        for i in 0..11 do
            positions.Add(Vec3.normalize (Vec3(raw.[i * 3], raw.[i * 3 + 1], raw.[i * 3 + 2])))

        let mutable triangles =
            [| 0; 11; 5
               0; 5; 1
               0; 1; 7
               0; 7; 10
               0; 10; 11
               1; 5; 9
               5; 11; 4
               11; 10; 2
               10; 7; 6
               7; 1; 8
               3; 9; 4
               3; 4; 2
               3; 2; 6
               3; 6; 8
               3; 8; 9
               4; 9; 5
               2; 4; 11
               6; 2; 10
               8; 6; 7
               9; 8; 1 |]

        let midpoints = Dictionary<int, int>()

        let midpoint (a: int) (b: int) : int =
            let key = if a < b then a * 65536 + b else b * 65536 + a

            match midpoints.TryGetValue key with
            | true, index -> index
            | _ ->
                positions.Add(Vec3.normalize ((positions.[a] + positions.[b]) * 0.5))
                let index = positions.Count - 1
                midpoints.[key] <- index
                index

        for _ in 1..subdivisions do
            let next = ResizeArray<int>()

            for f in 0 .. triangles.Length / 3 - 1 do
                let a = triangles.[f * 3]
                let b = triangles.[f * 3 + 1]
                let c = triangles.[f * 3 + 2]
                let ab = midpoint a b
                let bc = midpoint b c
                let ca = midpoint c a
                next.AddRange [| a; ab; ca; b; bc; ab; c; ca; bc; ab; bc; ca |]

            triangles <- next.ToArray()

        positions.ToArray(), triangles

    /// Regional geology is purely visual. The same surface is used for barren and ore-bearing bodies.
    let sectorAsteroid kind (seed: int) (subdivisions: int) : MeshData =
        let rng = Rng(seed)
        let positions, triangles = icosphere subdivisions

        // Raeumlich koharente Gesteinsform: grobe Lappen statt unabhaengigem
        // Vertexrauschen. Derselbe Seed behaelt damit seine Form ueber alle LODs.
        let lobeCount = 6
        let lobeDirection = Array.init lobeCount (fun _ -> rng.UnitVector())
        let lobeFrequency = Array.init lobeCount (fun _ -> rng.Range(1.5, 5.0))
        let lobeAmplitude = Array.init lobeCount (fun _ -> rng.Range(0.04, 0.14))
        let lobePhase = Array.init lobeCount (fun _ -> rng.Range(0.0, 2.0 * Math.PI))
        let squashY = rng.Range(0.65, 1.0)
        let squashZ = rng.Range(0.75, 1.2)

        // Wenige Bruchflaechen erhalten kantige Silhouetten, ohne die gesamte
        // Oberflaeche in einen regelmaessigen Polyeder zu verwandeln.
        let cutCount = 3
        let cutNormal = Array.init cutCount (fun _ -> rng.UnitVector())
        let cutDistance = Array.init cutCount (fun _ -> rng.Range(0.70, 0.92))
        let craterCount = 5
        let craterDirection = Array.init craterCount (fun _ -> rng.UnitVector())
        let craterWidth = Array.init craterCount (fun _ -> rng.Range(0.18, 0.44))
        let craterDepth = Array.init craterCount (fun _ -> rng.Range(0.07, 0.18))
        let strataDirection = rng.UnitVector()
        let mineralDirection = rng.UnitVector()
        let detailPhase = rng.Range(0.0, 2.0 * Math.PI)

        let craterBowl (direction: Vec3) =
            let mutable depression = 0.0
            let mutable rim = 0.0

            for k in 0 .. craterCount - 1 do
                let d = sqrt (max 0.0 (2.0 * (1.0 - Vec3.dot direction craterDirection.[k]))) / craterWidth.[k]
                if d < 1.0 then
                    let bowl = 1.0 - d * d
                    depression <- depression + craterDepth.[k] * bowl * bowl
                if d > 0.72 && d < 1.28 then
                    let lip = 1.0 - abs (d - 1.0) / 0.28
                    rim <- rim + craterDepth.[k] * 0.27 * lip * lip

            depression, rim

        let displaced =
            positions
            |> Array.map (fun direction ->
                let detail = sin (direction.X * 17.0 + detailPhase) * sin (direction.Y * 13.0 - detailPhase) * sin (direction.Z * 19.0)
                let mutable radius = 1.0 + detail * 0.025

                for k in 0 .. lobeCount - 1 do
                    radius <-
                        radius
                        + lobeAmplitude.[k]
                          * sin (lobeFrequency.[k] * Vec3.dot direction lobeDirection.[k] + lobePhase.[k])

                let depression, rim = craterBowl direction
                radius <- max 0.35 (radius - depression + rim)

                let mutable p =
                    Vec3(direction.X * radius, direction.Y * radius * squashY, direction.Z * radius * squashZ)

                for k in 0 .. cutCount - 1 do
                    let over = Vec3.dot p cutNormal.[k] - cutDistance.[k]

                    if over > 0.0 then
                        p <- p - cutNormal.[k] * over

                match kind with
                | MiningDepot -> p
                | Refinery ->
                    // Cooled slag: broad rafts of crust sit above recessed seams.
                    let joint = abs (sin (direction.X * 4.2 + detailPhase + 1.6 * direction.Y * direction.Z))
                    p * (0.91 + 0.12 * min 1.0 (joint * 3.5))
                | FrontierBazaar ->
                    // Two lobes meet at a worn neck. Vary the join with the original seed.
                    let waist = 0.74 + 0.28 * min 1.0 (abs (p.X + 0.12 * p.Z) * 2.8)
                    Vec3(p.X * 1.12, p.Y * waist, p.Z * waist)
                | OrbitalYard ->
                    // Broad cleavage planes on opaque blue ice.
                    Vec3(max -0.79 (min 0.86 p.X), min 0.57 (max -0.67 p.Y), p.Z * 0.96)
                | ResearchRelay ->
                    let bed = sin (direction.Y * 13.0 + direction.Z * 2.4 + detailPhase)
                    Vec3(p.X * (1.0 + 0.075 * bed), p.Y * 0.84, p.Z * (1.0 + 0.075 * bed))
                | FleetDock ->
                    // Rounded regmaglypts give the iron meteorites a thumb-pressed surface.
                    let pocket = max 0.0 (sin (direction.X * 7.0 + detailPhase) * cos (direction.Y * 8.0 - direction.Z * 4.0))
                    p * (1.0 - 0.15 * pocket * pocket))

        // Auf Huellkugelradius 1 normieren: Instanz-Skalierung = Kollisionsradius
        let maxRadius = displaced |> Array.fold (fun m p -> Math.Max(m, Vec3.length p)) 0.0
        // Kleine Reserve fuer die anschliessende Konvertierung nach Float32.
        let scale = 0.999999 / maxRadius
        let b = MeshBuilder()

        // Mineraladern und Sedimentschichten werden aus der Position abgeleitet,
        // nicht zufaellig pro Dreieck gewuerfelt. So bleiben grosse Felsformen lesbar.
        let rockColor (direction: Vec3) =
            let strata = sin (Vec3.dot direction strataDirection * 15.0 + sin (direction.Z * 7.0) * 1.3)
            let mineral = sin (Vec3.dot direction mineralDirection * 9.0 + detailPhase + sin (direction.X * 11.0) * 0.6)
            let depression, _ = craterBowl direction
            let shade = 0.53 + strata * 0.055 - depression * 0.65
            let vein = max 0.0 (1.0 - abs mineral * 8.0)
            let surface (from: Vec3) (towards: Vec3) amount =
                let c = Vec3.lerp from towards (max 0.0 (min 1.0 amount))
                MeshColor.rgb c.X c.Y c.Z
            match kind with
            | MiningDepot -> MeshColor.rgb (shade + vein * 0.16) (shade * 0.92 + vein * 0.09) (shade * 0.82 + vein * 0.025)
            | Refinery ->
                let joint = abs (sin (direction.X * 4.2 + detailPhase + 1.6 * direction.Y * direction.Z))
                let seam = max 0.0 (1.0 - joint * 3.5)
                let crust = surface (Vec3(0.20,0.14,0.15)) (Vec3(0.93,0.31,0.065)) (0.14 + 0.14 * strata + 0.8 * seam)
                { crust with Emissive = 0.46 * seam * seam }
            | FrontierBazaar ->
                surface (Vec3(0.12,0.23,0.22)) (Vec3(0.53,0.65,0.49)) (0.40 + 0.29 * strata + vein * 0.23)
            | OrbitalYard ->
                let frost = 0.60 + 0.27 * direction.Y + 0.21 * strata - 0.52 * (1.0 - abs mineral) ** 4.0
                surface (Vec3(0.10,0.35,0.49)) (Vec3(0.88,0.96,1.0)) frost
            | ResearchRelay ->
                let bed = sin (direction.Y * 13.0 + direction.Z * 2.4 + detailPhase)
                surface (Vec3(0.19,0.15,0.29)) (Vec3(0.69,0.53,0.66)) (0.41 + 0.38 * bed + 0.12 * strata)
            | FleetDock ->
                surface (Vec3(0.23,0.26,0.31)) (Vec3(0.65,0.49,0.38)) (0.38 + 0.24 * strata + 0.27 * vein - depression)

        let colors = positions |> Array.map rockColor

        // Geglaettete Normalen: flaechengewichtetes Mittel der angrenzenden Dreiecke.
        // Gewoelbte Flaechen wirken dadurch rund statt facettiert; an Bruchkanten
        // (Winkel zur Flaechennormale ueber ca. 50 Grad) bleibt die harte Kante.
        let smooth = Array.create displaced.Length Vec3.zero

        for f in 0 .. triangles.Length / 3 - 1 do
            let ia = triangles.[f * 3]
            let ib = triangles.[f * 3 + 1]
            let ic = triangles.[f * 3 + 2]
            let n = Vec3.cross (displaced.[ib] - displaced.[ia]) (displaced.[ic] - displaced.[ia])
            smooth.[ia] <- smooth.[ia] + n
            smooth.[ib] <- smooth.[ib] + n
            smooth.[ic] <- smooth.[ic] + n

        for f in 0 .. triangles.Length / 3 - 1 do
            let ia = triangles.[f * 3]
            let ib = triangles.[f * 3 + 1]
            let ic = triangles.[f * 3 + 2]
            let face = Vec3.cross (displaced.[ib] - displaced.[ia]) (displaced.[ic] - displaced.[ia])

            if Vec3.lengthSq face > 1e-14 then
                let face = Vec3.normalize face

                let corner (i: int) =
                    let n = smooth.[i]
                    let n = if Vec3.lengthSq n > 1e-14 then Vec3.normalize n else face
                    let crease = if kind = OrbitalYard then 0.985 else 0.64
                    b.Vertex(displaced.[i] * scale, (if Vec3.dot n face > crease then n else face), colors.[i])

                corner ia
                corner ib
                corner ic

        b.Build()

    /// Original belt rock, also shared with the small debris pool.
    let asteroid seed subdivisions = sectorAsteroid MiningDepot seed subdivisions

    // ---------------------------------------------------------------------------------
    //  Laser-Bolt: gestrecktes Oktaeder im Einheitswuerfel (Laenge entlang Z)
    // ---------------------------------------------------------------------------------

    /// Instanz-Skalierung (Dicke, Dicke, Laenge) ergibt die endgueltige Form.
    let laserBolt () : MeshData =
        let b = MeshBuilder()
        let color = MeshColor.glow 1.0 1.0 1.0 1.0
        let front = Vec3(0.0, 0.0, -0.5)
        let back = Vec3(0.0, 0.0, 0.5)

        let middle =
            [| Vec3(0.5, 0.0, 0.0)
               Vec3(0.0, 0.5, 0.0)
               Vec3(-0.5, 0.0, 0.0)
               Vec3(0.0, -0.5, 0.0) |]

        for i in 0..3 do
            let j = (i + 1) % 4
            b.TriangleOutward(front, middle.[i], middle.[j], Vec3.zero, color)
            b.TriangleOutward(back, middle.[i], middle.[j], Vec3.zero, color)

        b.Build()

    /// Energiebolt und Funkenstrich: Spindel im Einheitswuerfel, Flugrichtung -Z.
    /// Der Kopf sitzt weit vorn und leuchtet voll, der lange Schweif laeuft dunkel
    /// aus - additiv gezeichnet entsteht ein Komet statt eines Strichs.
    let energyBolt () : MeshData =
        let b = MeshBuilder()
        let sides = 6
        let zs = [| -0.5; -0.34; -0.08; 0.5 |]
        let radii = [| 0.0; 0.5; 0.36; 0.0 |]
        let power = [| 1.0; 1.0; 0.55; 0.0 |]
        let color k = MeshColor.glow power.[k] power.[k] power.[k] 1.0

        let point k i =
            let angle = 2.0 * Math.PI * float (i % sides) / float sides
            Vec3(cos angle * radii.[k], sin angle * radii.[k], zs.[k])

        // Nur die Aussenseite: die Spindel ist konvex, jede Blickrichtung sieht sie.
        let outward (p0: Vec3) (p1: Vec3) (p2: Vec3) c0 c1 c2 =
            let n = Vec3.cross (p1 - p0) (p2 - p0)
            let center = (p0 + p1 + p2) / 3.0
            if Vec3.dot n (Vec3(center.X, center.Y, 0.0)) >= 0.0 then b.TriangleColors(p0, p1, p2, c0, c1, c2)
            else b.TriangleColors(p0, p2, p1, c0, c2, c1)

        for k in 0 .. 2 do
            for i in 0 .. sides - 1 do
                outward (point k i) (point (k + 1) i) (point (k + 1) (i + 1)) (color k) (color (k + 1)) (color (k + 1))
                outward (point k i) (point (k + 1) (i + 1)) (point k (i + 1)) (color k) (color (k + 1)) (color k)

        b.Build()

    /// Stossring: flacher Kreisring in der XY-Ebene mit Aussenradius 1. Die Helligkeit
    /// steigt von innen zur Front an und bricht am Rand ab. Beidseitig sichtbar.
    let shockRing () : MeshData =
        let b = MeshBuilder()
        let segments = 56
        let radii = [| 0.74; 0.92; 0.972; 1.0 |]
        let power = [| 0.0; 0.2; 1.0; 0.0 |]
        let color k = MeshColor.glow power.[k] power.[k] power.[k] 1.0

        let point k i =
            let angle = 2.0 * Math.PI * float (i % segments) / float segments
            Vec3(cos angle * radii.[k], sin angle * radii.[k], 0.0)

        for i in 0 .. segments - 1 do
            for k in 0 .. 2 do
                let p0, p1, p2, p3 = point k i, point (k + 1) i, point (k + 1) (i + 1), point k (i + 1)
                let c0, c1 = color k, color (k + 1)
                b.TriangleColors(p0, p1, p2, c0, c1, c1)
                b.TriangleColors(p0, p2, p3, c0, c1, c0)
                b.TriangleColors(p0, p2, p1, c0, c1, c1)
                b.TriangleColors(p0, p3, p2, c0, c0, c1)

        b.Build()

    // ---------------------------------------------------------------------------------
    //  Triebwerksplume: gekreuzte, zum Rand und Heck auslaufende Lichtbaender.
    //  Die Laenge bleibt wie beim Laser normalisiert auf -0.5 .. +0.5.
    // ---------------------------------------------------------------------------------

    let exhaustPlume () : MeshData =
        let b = MeshBuilder()
        let zs = [| -0.5; -0.30; 0.05; 0.5 |]
        let widths = [| 0.30; 0.50; 0.29; 0.0 |]
        let power = [| 0.85; 0.55; 0.16; 0.0 |]
        let edge = MeshColor.glow 0.0 0.0 0.0 1.0
        for plane in 0 .. 3 do
            let angle = float plane * Math.PI / 4.0
            for side in [| -1.0; 1.0 |] do
                for k in 0 .. 2 do
                    let a = Vec3(0.0, 0.0, zs.[k])
                    let d = Vec3(0.0, 0.0, zs.[k + 1])
                    let e = Vec3(cos angle * widths.[k] * side, sin angle * widths.[k] * side, zs.[k])
                    let f = Vec3(cos angle * widths.[k + 1] * side, sin angle * widths.[k + 1] * side, zs.[k + 1])
                    let ca = MeshColor.glow power.[k] power.[k] power.[k] 1.0
                    let cd = MeshColor.glow power.[k + 1] power.[k + 1] power.[k + 1] 1.0
                    // Double-sided ribbons remain visible from every flight angle.
                    b.TriangleColors(a, e, d, ca, edge, cd)
                    b.TriangleColors(d, e, f, cd, edge, edge)
                    b.TriangleColors(d, e, a, cd, edge, ca)
                    b.TriangleColors(f, e, d, edge, edge, cd)
        b.Build()

    // ---------------------------------------------------------------------------------
    //  Glow-Sprite: Scheibe mit radialem Helligkeitsverlauf (Sterne, Sonne, Partikel)
    // ---------------------------------------------------------------------------------

    /// Scheibe in der XY-Ebene mit Radius 1, Vorderseite +Z. Die Helligkeit faellt
    /// von der Mitte (weiss) zum Rand (schwarz) ab - additiv gezeichnet entsteht ein
    /// weicher Lichtpunkt. `rings` konzentrische Ringe formen den Verlauf: 3 genuegen
    /// fuer wenige Pixel grosse Sterne, 6 ergeben ein glattes Gluehen (Sonne, Explosionen).
    /// Instanzfarben > 1 lassen den Kern zu einer festen Scheibe ausbrennen.
    let glowSprite (segments: int) (rings: int) : MeshData =
        let b = MeshBuilder()

        // Der Shader gamma-korrigiert die Ausgabe (hoch 1/2.2). Damit das Gluehen
        // auf dem Bildschirm quadratisch ausklingt, muss es linear mit (1-r)^4.4 fallen.
        let intensity (r: float) = System.Math.Pow(1.0 - r, 4.4)

        // Ringe zur Mitte hin dichter, weil die Kurve dort am steilsten ist
        let radius (k: int) =
            System.Math.Pow(float k / float rings, 1.5)

        let color (r: float) =
            let i = intensity r
            MeshColor.glow i i i 1.0

        let point (k: int) (i: int) =
            let angle = 2.0 * Math.PI * float (i % segments) / float segments
            let r = radius k
            Vec3(cos angle * r, sin angle * r, 0.0)

        for i in 0 .. segments - 1 do
            // Innerer Faecher
            b.TriangleColors(Vec3.zero, point 1 i, point 1 (i + 1), color 0.0, color (radius 1), color (radius 1))

            // Ringsegmente
            for k in 1 .. rings - 1 do
                let inner = color (radius k)
                let outer = color (radius (k + 1))
                b.TriangleColors(point k i, point (k + 1) i, point (k + 1) (i + 1), inner, outer, outer)
                b.TriangleColors(point k i, point (k + 1) (i + 1), point k (i + 1), inner, outer, inner)

        b.Build()

    // ---------------------------------------------------------------------------------
    //  Energieschild-Blase: geodaetische Kugel mit leuchtenden Wabenkanten
    // ---------------------------------------------------------------------------------

    /// Geodaetische Sphäre (Radius 1), deren Dreieckskanten hell leuchten und deren
    /// Flaechenmitten dunkel bleiben. Additiv gerendert entsteht ein 3D-Energiegitter
    /// um getroffene Schiffe.
    let shieldBubble () : MeshData =
        let b = MeshBuilder()
        let positions, triangles = icosphere 2
        let edgeColor = MeshColor.glow 0.95 0.95 1.0 1.0
        let coreColor = MeshColor.glow 0.015 0.015 0.02 1.0

        for f in 0 .. triangles.Length / 3 - 1 do
            let p0 = positions.[triangles.[f * 3]]
            let p1 = positions.[triangles.[f * 3 + 1]]
            let p2 = positions.[triangles.[f * 3 + 2]]
            let center = Vec3.normalize ((p0 + p1 + p2) / 3.0)
            let n = Vec3.cross (p1 - p0) (p2 - p0)
            let a, bPt, c = if Vec3.dot n center >= 0.0 then p0, p1, p2 else p0, p2, p1
            b.TriangleColors(a, bPt, center, edgeColor, edgeColor, coreColor)
            b.TriangleColors(bPt, c, center, edgeColor, edgeColor, coreColor)
            b.TriangleColors(c, a, center, edgeColor, edgeColor, coreColor)

        b.Build()

    // ---------------------------------------------------------------------------------
    //  Sternenhimmel: fuellt einen (kamerarelativen) Batch aus Glow-Sprites
    // ---------------------------------------------------------------------------------

    /// Sichtbare Sonne: ausgebrannter Kern plus zwei weiche Halos (3 Instanzen).
    /// Der Batch muss kamerarelativ sein und ein glattes Glow-Sprite verwenden.
    let fillSun (batch: InstanceBatch) (radius: float) (sunDirection: Vec3) =
        InstanceBatch.clear batch
        let sun = Vec3.normalize sunDirection
        let rotation = Quat.lookRotation sun Vec3.unitY
        let position = sun * radius

        let layer (size: float) (r: float) (g: float) (bl: float) =
            let s = radius * size
            InstanceBatch.push batch position rotation (Vec3(s, s, s)) r g bl 1.0

        layer 0.07 9.0 8.4 7.0 // Kern: Farbe weit ueber 1 -> feste Scheibe mit weichem Rand
        layer 0.22 1.5 1.25 0.9
        layer 0.60 0.42 0.34 0.26

    /// Verteilt Sterne auf einer Kugel mit `radius` um den Ursprung.
    /// Die Sprites zeigen zum Mittelpunkt - bei einem kamerarelativen Batch sind
    /// sie damit automatisch Billboards. Nutzt die gesamte Kapazitaet des Batches.
    let fillStarfield (batch: InstanceBatch) (seed: int) (radius: float) =
        let rng = Rng(seed)
        InstanceBatch.clear batch

        // Die Haelfte der Sterne liegt in einem geneigten Band ("Milchstrasse")
        let bandRotation = Quat.fromAxisAngle (Vec3(1.0, 0.3, 0.2)) 1.1

        while batch.Count < batch.Capacity do
            let inBand = rng.NextFloat() < 0.5

            let direction =
                if inBand then
                    let angle = rng.Range(0.0, 2.0 * Math.PI)
                    // Summe dreier Zufallszahlen ~ Glockenkurve um die Bandmitte
                    let latitude = (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 1.5) * 0.3

                    Quat.rotateVec3
                        bandRotation
                        (Vec3(cos angle * cos latitude, sin latitude, sin angle * cos latitude))
                else
                    rng.UnitVector()

            // Wenige helle, viele schwache Sterne
            let m = rng.NextFloat()
            let brightness = m * m * m

            let size =
                if inBand then
                    radius * (0.0020 + 0.0022 * brightness)
                else
                    radius * (0.0024 + 0.0060 * brightness)

            // Werte > 1 lassen den Kern heller Sterne ausbrennen
            let intensity = (if inBand then 0.9 else 1.3) + 2.6 * brightness

            // Farbtemperatur: blau-weiss, weiss, gelblich, orange
            let temperature = rng.NextFloat()
            let mutable r = 1.0
            let mutable g = 1.0
            let mutable bl = 1.0

            if temperature < 0.2 then
                r <- 0.65
                g <- 0.78
            elif temperature < 0.65 then
                ()
            elif temperature < 0.88 then
                g <- 0.9
                bl <- 0.68
            else
                g <- 0.68
                bl <- 0.45

            InstanceBatch.push
                batch
                (direction * radius)
                (Quat.lookRotation direction Vec3.unitY)
                (Vec3(size, size, size))
                (r * intensity)
                (g * intensity)
                (bl * intensity)
                1.0
