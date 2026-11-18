namespace SpaceEngine

// =====================================================================================
//  GlbLoader.fs  -  Schlanker glTF-2.0-Binary (.glb) Loader fuer MeshData
//
//  Liest Blender-/Asset-Exporte (.glb) direkt in das Engine-Format
//  [px py pz nx ny nz r g b e] + 16-Bit-Indizes, ohne Three.js oder externe Pakete.
//  Unterstuetzt Node-Hierarchien (matrix / translation / rotation / scale),
//  PBR baseColorFactor + emissiveFactor, COLOR_0 sowie automatische Normalen.
// =====================================================================================

open Fable.Core
open Fable.Core.JsInterop

[<RequireQualifiedAccess>]
module GlbLoader =

    [<Literal>]
    let private GlbMagic = 0x46546C67

    [<Literal>]
    let private ChunkJson = 0x4E4F534A

    [<Literal>]
    let private ChunkBin = 0x004E4942

    [<Emit("new DataView($0)")>]
    let private dataView (buf: JS.ArrayBuffer) : JS.DataView = jsNative

    [<Emit("new TextDecoder('utf-8').decode(new Uint8Array($0, $1, $2))")>]
    let private decodeUtf8 (buf: JS.ArrayBuffer) (offset: int) (length: int) : string = jsNative

    [<Emit("JSON.parse($0)")>]
    let private jsonParse (text: string) : obj = jsNative

    [<Emit("fetch($0).then(function(r){ if(!r.ok) throw new Error('GLB HTTP ' + r.status); return r.arrayBuffer(); }).then($1)")>]
    let private fetchAndParse (url: string) (onBuffer: JS.ArrayBuffer -> MeshData) : JS.Promise<MeshData> = jsNative

    [<Emit("($0 && $0[$1] !== undefined) ? $0[$1] : null")>]
    let private prop (o: obj) (name: string) : obj = jsNative

    let private propInt (o: obj) (name: string) (fallback: int) : int =
        let v = prop o name
        if isNull v then fallback else unbox<int> v

    let private propFloat (o: obj) (name: string) (fallback: float) : float =
        let v = prop o name
        if isNull v then fallback else unbox<float> v

    let private compCount (kind: string) =
        match kind with
        | "SCALAR" -> 1
        | "VEC2" -> 2
        | "VEC3" -> 3
        | "VEC4" -> 4
        | "MAT4" -> 16
        | _ -> 0

    let private compBytes (compType: int) =
        match compType with
        | 5120 | 5121 -> 1 // BYTE / UNSIGNED_BYTE
        | 5122 | 5123 -> 2 // SHORT / UNSIGNED_SHORT
        | 5125 | 5126 -> 4 // UNSIGNED_INT / FLOAT
        | _ -> 0

    let private readComponent (dv: JS.DataView) (byteOffset: int) (compType: int) (normalized: bool) : float =
        match compType with
        | 5126 -> float (dv.getFloat32 (byteOffset, true))
        | 5121 ->
            let v = float (dv.getUint8 byteOffset)
            if normalized then v / 255.0 else v
        | 5123 ->
            let v = float (dv.getUint16 (byteOffset, true))
            if normalized then v / 65535.0 else v
        | 5120 ->
            let v = float (dv.getInt8 byteOffset)
            if normalized then max -1.0 (v / 127.0) else v
        | 5122 ->
            let v = float (dv.getInt16 (byteOffset, true))
            if normalized then max -1.0 (v / 32767.0) else v
        | 5125 -> float (dv.getUint32 (byteOffset, true))
        | _ -> 0.0

    let private readAccessor (gltf: obj) (binDv: JS.DataView) (binOffset: int) (accessorIdx: int) =
        let accessors = unbox<obj[]> (prop gltf "accessors")
        let bufferViews = unbox<obj[]> (prop gltf "bufferViews")
        let acc = accessors.[accessorIdx]
        let bvIdx = propInt acc "bufferView" -1
        let count = propInt acc "count" 0
        let compType = propInt acc "componentType" 5126
        let kind = unbox<string> (prop acc "type")
        let normalized =
            let n = prop acc "normalized"
            not (isNull n) && unbox<bool> n
        let accOffset = propInt acc "byteOffset" 0
        let bv = bufferViews.[bvIdx]
        let bvOffset = propInt bv "byteOffset" 0
        let stride =
            let s = propInt bv "byteStride" 0
            if s > 0 then s else compCount kind * compBytes compType
        let baseByte = binOffset + bvOffset + accOffset
        let comps = compCount kind
        let cBytes = compBytes compType
        let out = Array.zeroCreate<float> (count * comps)
        for i in 0 .. count - 1 do
            let row = baseByte + i * stride
            for c in 0 .. comps - 1 do
                out.[i * comps + c] <- readComponent binDv (row + c * cBytes) compType normalized
        out, comps, count

    let private nodeLocalMatrix (node: obj) : Mat4 =
        let m = Mat4.create ()
        let rawMat = prop node "matrix"
        if not (isNull rawMat) then
            let arr = unbox<float[]> rawMat
            for i in 0 .. 15 do
                m.Data.[i] <- float32 arr.[i]
            m
        else
            let tArr =
                let raw = prop node "translation"
                if isNull raw then [| 0.0; 0.0; 0.0 |] else unbox<float[]> raw
            let rArr =
                let raw = prop node "rotation"
                if isNull raw then [| 0.0; 0.0; 0.0; 1.0 |] else unbox<float[]> raw
            let sArr =
                let raw = prop node "scale"
                if isNull raw then [| 1.0; 1.0; 1.0 |] else unbox<float[]> raw
            Mat4.composeTRS
                m.Data
                0
                tArr.[0]
                tArr.[1]
                tArr.[2]
                rArr.[0]
                rArr.[1]
                rArr.[2]
                rArr.[3]
                sArr.[0]
                sArr.[1]
                sArr.[2]
            m

    let private transformPoint (m: Mat4) (x: float) (y: float) (z: float) =
        let d = m.Data
        let nx = float d.[0] * x + float d.[4] * y + float d.[8] * z + float d.[12]
        let ny = float d.[1] * x + float d.[5] * y + float d.[9] * z + float d.[13]
        let nz = float d.[2] * x + float d.[6] * y + float d.[10] * z + float d.[14]
        nx, ny, nz

    let private transformNormal (m: Mat4) (x: float) (y: float) (z: float) =
        let d = m.Data
        let sx = max 1e-12 (float d.[0] * float d.[0] + float d.[1] * float d.[1] + float d.[2] * float d.[2])
        let sy = max 1e-12 (float d.[4] * float d.[4] + float d.[5] * float d.[5] + float d.[6] * float d.[6])
        let sz = max 1e-12 (float d.[8] * float d.[8] + float d.[9] * float d.[9] + float d.[10] * float d.[10])
        let vx, vy, vz = x / sx, y / sy, z / sz
        let nx = float d.[0] * vx + float d.[4] * vy + float d.[8] * vz
        let ny = float d.[1] * vx + float d.[5] * vy + float d.[9] * vz
        let nz = float d.[2] * vx + float d.[6] * vy + float d.[10] * vz
        let len = sqrt (nx * nx + ny * ny + nz * nz)
        if len > 1e-8 then nx / len, ny / len, nz / len else 0.0, 1.0, 0.0

    let private computeVertexNormals (posArr: float[]) (vCount: int) (idxArr: int[]) =
        let acc = Array.zeroCreate<float> (vCount * 3)
        let triCount = idxArr.Length / 3
        for t in 0 .. triCount - 1 do
            let i0, i1, i2 = idxArr.[t * 3], idxArr.[t * 3 + 1], idxArr.[t * 3 + 2]
            if i0 < vCount && i1 < vCount && i2 < vCount then
                let ax, ay, az = posArr.[i0 * 3], posArr.[i0 * 3 + 1], posArr.[i0 * 3 + 2]
                let ux, uy, uz = posArr.[i1 * 3] - ax, posArr.[i1 * 3 + 1] - ay, posArr.[i1 * 3 + 2] - az
                let vx, vy, vz = posArr.[i2 * 3] - ax, posArr.[i2 * 3 + 1] - ay, posArr.[i2 * 3 + 2] - az
                let cx = uy * vz - uz * vy
                let cy = uz * vx - ux * vz
                let cz = ux * vy - uy * vx
                for idx in [| i0; i1; i2 |] do
                    acc.[idx * 3] <- acc.[idx * 3] + cx
                    acc.[idx * 3 + 1] <- acc.[idx * 3 + 1] + cy
                    acc.[idx * 3 + 2] <- acc.[idx * 3 + 2] + cz
        for i in 0 .. vCount - 1 do
            let x, y, z = acc.[i * 3], acc.[i * 3 + 1], acc.[i * 3 + 2]
            let len = sqrt (x * x + y * y + z * z)
            if len > 1e-8 then
                acc.[i * 3] <- x / len
                acc.[i * 3 + 1] <- y / len
                acc.[i * 3 + 2] <- z / len
            else
                acc.[i * 3 + 1] <- 1.0
        acc

    let private materialColor (gltf: obj) (prim: obj) =
        let mutable r, g, b, e = 0.85, 0.88, 0.92, 0.0
        let matIdx = propInt prim "material" -1
        let matsObj = prop gltf "materials"
        if matIdx >= 0 && not (isNull matsObj) then
            let mat = (unbox<obj[]> matsObj).[matIdx]
            let pbr = prop mat "pbrMetallicRoughness"
            if not (isNull pbr) then
                let bc = prop pbr "baseColorFactor"
                if not (isNull bc) then
                    let arr = unbox<float[]> bc
                    r <- arr.[0]
                    g <- arr.[1]
                    b <- arr.[2]
            let em = prop mat "emissiveFactor"
            if not (isNull em) then
                let arr = unbox<float[]> em
                let strength = max arr.[0] (max arr.[1] arr.[2])
                if strength > 0.01 then
                    e <- min 1.0 strength
                    r <- max r arr.[0]
                    g <- max g arr.[1]
                    b <- max b arr.[2]
        r, g, b, e

    /// Parst einen binären glTF-2.0-Buffer (.glb) in ein Engine-MeshData.
    let parse (buffer: JS.ArrayBuffer) : MeshData =
        let dv = dataView buffer
        if buffer.byteLength < 20 then failwith "GLB-Datei ist zu klein."
        if int (dv.getUint32 (0, true)) <> GlbMagic then failwith "Ungültige GLB-Signatur."
        if int (dv.getUint32 (4, true)) <> 2 then failwith "Nur glTF 2.0 wird unterstützt."

        let totalLength = min buffer.byteLength (int (dv.getUint32 (8, true)))
        let mutable offset = 12
        let mutable jsonText = ""
        let mutable binOffset = -1

        while offset + 8 <= totalLength do
            let chunkLen = int (dv.getUint32 (offset, true))
            let chunkType = int (dv.getUint32 (offset + 4, true))
            let dataStart = offset + 8
            if chunkType = ChunkJson then
                jsonText <- decodeUtf8 buffer dataStart chunkLen
            elif chunkType = ChunkBin && binOffset < 0 then
                binOffset <- dataStart
            offset <- dataStart + chunkLen

        if jsonText = "" || binOffset < 0 then
            failwith "GLB-Chunks (JSON/BIN) unvollständig."

        let gltf = jsonParse jsonText
        let meshes = unbox<obj[]> (prop gltf "meshes")

        let verts = ResizeArray<float32>()
        let indices = ResizeArray<uint16>()

        let appendMesh (meshIdx: int) (worldMat: Mat4) =
            let mesh = meshes.[meshIdx]
            let primitives = unbox<obj[]> (prop mesh "primitives")
            for prim in primitives do
                let attrs = prop prim "attributes"
                let posIdx = propInt attrs "POSITION" -1
                if posIdx >= 0 then
                    let posArr, _, vCount = readAccessor gltf dv binOffset posIdx
                    let idxAcc = propInt prim "indices" -1
                    let idxArr =
                        if idxAcc >= 0 then
                            let rawIdx, _, iCount = readAccessor gltf dv binOffset idxAcc
                            Array.init iCount (fun k -> int rawIdx.[k])
                        else
                            Array.init vCount id
                    let normIdx = propInt attrs "NORMAL" -1
                    let normArr =
                        if normIdx >= 0 then
                            let arr, _, _ = readAccessor gltf dv binOffset normIdx
                            arr
                        else
                            computeVertexNormals posArr vCount idxArr
                    let colIdx = propInt attrs "COLOR_0" -1
                    let colArr, colComps =
                        if colIdx >= 0 then
                            let arr, comps, _ = readAccessor gltf dv binOffset colIdx
                            arr, comps
                        else Array.empty, 0
                    let mr, mg, mb, me = materialColor gltf prim

                    let baseVertex = verts.Count / Layout.VertexFloats
                    if baseVertex + vCount > 65535 then
                        failwith "GLB übersteigt das 16-Bit-Vertexlimit (65.535)."

                    for i in 0 .. vCount - 1 do
                        let px, py, pz = transformPoint worldMat posArr.[i * 3] posArr.[i * 3 + 1] posArr.[i * 3 + 2]
                        let nx, ny, nz =
                            if normArr.Length >= (i + 1) * 3 then
                                transformNormal worldMat normArr.[i * 3] normArr.[i * 3 + 1] normArr.[i * 3 + 2]
                            else 0.0, 1.0, 0.0
                        let vr, vg, vb =
                            if colComps >= 3 && colArr.Length >= (i + 1) * colComps then
                                mr * colArr.[i * colComps], mg * colArr.[i * colComps + 1], mb * colArr.[i * colComps + 2]
                            else mr, mg, mb

                        verts.Add(float32 px)
                        verts.Add(float32 py)
                        verts.Add(float32 pz)
                        verts.Add(float32 nx)
                        verts.Add(float32 ny)
                        verts.Add(float32 nz)
                        verts.Add(float32 vr)
                        verts.Add(float32 vg)
                        verts.Add(float32 vb)
                        verts.Add(float32 me)

                    for k in 0 .. idxArr.Length - 1 do
                        indices.Add(uint16 (baseVertex + idxArr.[k]))

        let nodesObj = prop gltf "nodes"
        let scenesObj = prop gltf "scenes"
        if not (isNull nodesObj) && not (isNull scenesObj) then
            let nodes = unbox<obj[]> nodesObj
            let scenes = unbox<obj[]> scenesObj
            let sceneIdx = propInt gltf "scene" 0
            let rootNodes = unbox<int[]> (prop scenes.[sceneIdx] "nodes")
            let rec visit (nodeIdx: int) (parentMat: Mat4) =
                let node = nodes.[nodeIdx]
                let localMat = nodeLocalMatrix node
                let worldMat = Mat4.create ()
                Mat4.multiply worldMat parentMat localMat
                let meshIdx = propInt node "mesh" -1
                if meshIdx >= 0 then appendMesh meshIdx worldMat
                let chObj = prop node "children"
                if not (isNull chObj) then
                    for ch in unbox<int[]> chObj do
                        visit ch worldMat
            let ident = Mat4.create ()
            Mat4.identity ident
            for rootIdx in rootNodes do
                visit rootIdx ident
        else
            let ident = Mat4.create ()
            Mat4.identity ident
            for i in 0 .. meshes.Length - 1 do
                appendMesh i ident

        let vOut = JS.Constructors.Float32Array.Create verts.Count
        for i in 0 .. verts.Count - 1 do
            vOut.[i] <- verts.[i]
        let iOut = JS.Constructors.Uint16Array.Create indices.Count
        for i in 0 .. indices.Count - 1 do
            iOut.[i] <- indices.[i]
        { Vertices = vOut; Indices = iOut }

    /// Zentriert und skaliert ein geparstes MeshData auf einen gewuenschten Zielradius.
    let normalizeToRadius (targetRadius: float) (mesh: MeshData) : MeshData =
        let v = mesh.Vertices
        let count = v.length / Layout.VertexFloats
        if count = 0 then mesh
        else
            let mutable minX, minY, minZ = System.Double.PositiveInfinity, System.Double.PositiveInfinity, System.Double.PositiveInfinity
            let mutable maxX, maxY, maxZ = System.Double.NegativeInfinity, System.Double.NegativeInfinity, System.Double.NegativeInfinity
            for i in 0 .. count - 1 do
                let o = i * Layout.VertexFloats
                let x, y, z = float v.[o], float v.[o + 1], float v.[o + 2]
                if x < minX then minX <- x
                if y < minY then minY <- y
                if z < minZ then minZ <- z
                if x > maxX then maxX <- x
                if y > maxY then maxY <- y
                if z > maxZ then maxZ <- z
            let cx = (minX + maxX) * 0.5
            let cy = (minY + maxY) * 0.5
            let cz = (minZ + maxZ) * 0.5
            let mutable maxR = 1e-6
            for i in 0 .. count - 1 do
                let o = i * Layout.VertexFloats
                let dx, dy, dz = float v.[o] - cx, float v.[o + 1] - cy, float v.[o + 2] - cz
                let r = sqrt (dx * dx + dy * dy + dz * dz)
                if r > maxR then maxR <- r
            let scale = targetRadius / maxR
            let out = JS.Constructors.Float32Array.Create v.length
            for i in 0 .. count - 1 do
                let o = i * Layout.VertexFloats
                out.[o] <- float32 ((float v.[o] - cx) * scale)
                out.[o + 1] <- float32 ((float v.[o + 1] - cy) * scale)
                out.[o + 2] <- float32 ((float v.[o + 2] - cz) * scale)
                for k in 3 .. Layout.VertexFloats - 1 do
                    out.[o + k] <- v.[o + k]
            { Vertices = out; Indices = mesh.Indices }

    /// Laedt eine .glb-Datei per fetch() und konvertiert sie direkt in MeshData.
    let loadAsync (url: string) : JS.Promise<MeshData> =
        fetchAndParse url parse

    [<Emit("$0.then($1)")>]
    let onLoaded (promise: JS.Promise<MeshData>) (callback: MeshData -> unit) : unit = jsNative
