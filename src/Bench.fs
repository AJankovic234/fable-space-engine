module SpaceEngine.Bench

open System
open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

[<Emit("new URLSearchParams(window.location.search).has('bench')")>]
let requested () : bool = jsNative

type ResultRow =
    { tier: string
      avgFps: float
      p95ms: float
      p99ms: float
      maxMs: float }

/// Perzentile nach nearest rank. Die FPS gewichten jeden Frame mit seiner tatsächlichen Dauer.
let summarize tier (frames: float[]) =
    let n = frames.Length
    if n = 0 then invalidArg "frames" "Die Messreihe ist leer."
    let sorted = Array.sort frames
    let percentile p = sorted.[int (ceil (float n * p)) - 1]
    let round2 x = round (x * 100.0) / 100.0
    { tier = tier
      avgFps = round2 (1000.0 * float n / Array.sum frames)
      p95ms = round2 (percentile 0.95)
      p99ms = round2 (percentile 0.99)
      maxMs = round2 sorted.[n - 1] }

let private position role t =
    let phase = float role * 1.26
    Vec3(sin (t * 0.72 + phase) * (15.0 + float role * 3.0),
         cos (t * 0.45 + phase) * 7.5,
         -34.0 - float role * 12.0 + 8.0 * cos (t + phase))

/// Die Szene bekommt ihre eigene Welt. Alle Abschüsse und Effekt-Zufallszahlen
/// folgen beim Aufwärmen und bei jedem Messlauf derselben 120-Hz-Zeitachse.
let private createScene (renderer: Renderer) =
    let camera = Camera.create ()
    let sky = SkyBackdrop.create renderer
    renderer.Gl.useProgram renderer.Program
    let ambient = Universe.sectors.[0].Atmosphere
    Lights.setSun renderer.Lights ambient.SunDir.X ambient.SunDir.Y ambient.SunDir.Z ambient.SunColor.X ambient.SunColor.Y ambient.SunColor.Z
    Lights.setAmbient renderer.Lights ambient.Ambient.X ambient.Ambient.Y ambient.Ambient.Z
    let batch data capacity blend = Renderer.createBatch renderer (Renderer.createMesh renderer data) capacity blend
    let hulls =
        [| EnemyMeshes.drone; EnemyMeshes.scout; EnemyMeshes.gunship; EnemyMeshes.bomber; EnemyMeshes.corvette |]
        |> Array.map (fun mesh -> batch (mesh ()) 1 Opaque)
    let rocks = batch (ProceduralMeshes.asteroid 271 2) 320 Opaque
    rocks.Material <- 1.0
    let debris = Renderer.createBatch renderer rocks.Mesh 2048 Opaque
    let bolts = batch (ProceduralMeshes.energyBolt ()) 2048 Additive
    let glow = batch (ProceduralMeshes.glowSprite 12 3) 2048 Additive
    let rings = batch (ProceduralMeshes.shockRing ()) 128 Additive
    let world = World.create hulls.Length
    let destroyedAt = Array.zeroCreate hulls.Length
    let mutable tick = 0
    for i = 0 to hulls.Length - 1 do
        EnemyProfiles.configure world.Enemies.[i] (enum<EnemyRole> i) 1
        hulls.[i].Shininess <- 60.0
        hulls.[i].Specular <- 0.55

    let reset () =
        world.Rng.State <- 2601u
        World.reset world 0
        tick <- 0
        Array.fill destroyedAt 0 destroyedAt.Length -10.0

    let configure (preset: GraphicsQuality.Preset) =
        Renderer.applyQuality renderer preset preset.RenderScale
        let rng = Rng(2601)
        InstanceBatch.clear rocks
        for _ in 1 .. rocks.Capacity do
            let depth = rng.Range(60.0, 2100.0)
            let p = Vec3(rng.Range(-0.95, 0.95) * depth, rng.Range(-0.48, 0.48) * depth, -depth)
            let radius = rng.Range(1.8, 8.0)
            if Vec3.length p < preset.SmallObjectDistance then
                InstanceBatch.push rocks p Quat.identity (Vec3.one * radius) 0.8 0.77 0.72 0.0
        reset ()

    let render (seconds: float) =
        // Fester Schritt für die Kampfeffekte; auch bei niedrigen FPS fällt kein Abschuss aus.
        let stop = int (min 10.0 seconds * 120.0)
        while tick < stop do
            tick <- tick + 1
            let phase = tick % 600
            if phase % 120 = 72 then
                let i = phase / 120
                let ship = world.Enemies.[i]
                destroyedAt.[i] <- float tick / 120.0
                ship.Position <- position i destroyedAt.[i]
                Effects.shipDestruction world ship
            Effects.updateFuses world (1.0 / 120.0)
            ParticlePool.update world.Particles (1.0 / 120.0)
            Effects.updateFlashes world (1.0 / 120.0)

        Renderer.resize renderer |> ignore
        let center = Vec3(0.0, 0.0, -55.0)
        camera.Position <- Vec3(9.0 * sin (seconds * 0.35), 5.0 + 3.0 * sin seconds, 8.0)
        camera.Rotation <- Quat.lookRotation (center - camera.Position) Vec3.unitY
        Camera.updateMatrices camera (Renderer.aspect renderer)
        InstanceBatch.clear bolts
        InstanceBatch.clear glow
        InstanceBatch.clear rings
        InstanceBatch.clear debris
        for i = 0 to hulls.Length - 1 do
            InstanceBatch.clear hulls.[i]
            if seconds - destroyedAt.[i] > 1.6 then
                let p = position i seconds
                let facing = Quat.lookRotation (camera.Position - p) Vec3.unitY
                InstanceBatch.push hulls.[i] p facing Vec3.one 1.0 1.0 1.0 0.0
                let muzzle = p + Quat.rotateVec3 facing (EnemyMeshes.hull i).GunLeft
                let target = camera.Position + Vec3(-4.0, -2.0, 0.0)
                let style = CombatVfx.boltStyles.[CombatVfx.StyleEnemy + i]
                for k = 0 to 2 do
                    let travel = (seconds * 1.6 + float (k + i) / 3.0) % 1.0
                    InstanceBatch.push bolts (Vec3.lerp muzzle target travel) facing (Vec3(style.Thickness, style.Thickness, style.Length * 0.5)) style.R style.G style.B 1.0

        let pool = world.Particles
        let d = pool.Data
        for i = 0 to pool.Count - 1 do
            let o = i * ParticlePool.Stride
            let kind = int pool.Kind.[i]
            if kind <> ParticleKind.Fuse then
                let life = float d.[o + 6] / float d.[o + 7]
                let size = float d.[o + 8]
                let mutable s = size
                let mutable z = size
                let mutable q = camera.Rotation
                let mutable target = glow
                if kind = ParticleKind.Ring then
                    target <- rings
                    s <- size * (1.0 - 0.9 * life * life)
                elif kind = ParticleKind.Streak then
                    target <- bolts
                    q <- Quat.lookRotation (Vec3(float d.[o+3], float d.[o+4], float d.[o+5])) Vec3.unitY
                    z <- size * 10.0
                elif kind = ParticleKind.Debris then target <- debris
                InstanceBatch.pushTRS target (float d.[o]) (float d.[o+1]) (float d.[o+2])
                    q.X q.Y q.Z q.W s s z (float d.[o+9] * life) (float d.[o+10] * life) (float d.[o+11] * life)
                    (if kind = ParticleKind.Debris then 0.0 else 1.0)

        Lights.clearPoints renderer.Lights
        let flashes = world.Flashes
        for i = 0 to Flashes.Slots - 1 do
            let o = i * Flashes.Stride
            if flashes.[o+7] > 0.02f then
                Lights.addPoint renderer.Lights (float flashes.[o]) (float flashes.[o+1]) (float flashes.[o+2]) (float flashes.[o+3])
                    (float flashes.[o+4]) (float flashes.[o+5]) (float flashes.[o+6]) (float flashes.[o+7])
        Renderer.beginShadows renderer center 90.0
        Renderer.drawShadow renderer rocks
        for hull in hulls do Renderer.drawShadow renderer hull
        Renderer.endShadows renderer
        Renderer.beginFrame renderer camera.View camera.Projection camera.Position
        SkyBackdrop.draw renderer sky camera 0
        Renderer.draw renderer rocks
        Renderer.draw renderer debris
        for hull in hulls do Renderer.draw renderer hull
        Renderer.draw renderer bolts
        Renderer.draw renderer glow
        Renderer.draw renderer rings
        Renderer.endFrame renderer
    configure, reset, render

let start (canvas: HTMLCanvasElement) =
    let renderer = Renderer.create canvas
    let configure, reset, render = createScene renderer
    let panel = document.createElement "section"
    panel.id <- "bench-results"
    panel.setAttribute("aria-label", "Grafik-Benchmark")
    panel.setAttribute("style", "position:fixed;top:1rem;left:1rem;z-index:6;max-width:calc(100vw - 2rem);overflow:auto;box-sizing:border-box;padding:.8rem 1rem;background:#0a1721ee;border:1px solid #345567;color:#def1f3;font:14px/1.5 GameUI,system-ui,sans-serif")
    document.body.appendChild panel |> ignore
    (document.getElementById "open-saves").setAttribute("hidden", "")
    let tiers = [| GraphicsQuality.Low; GraphicsQuality.Medium; GraphicsQuality.High; GraphicsQuality.Ultra |]
    let rows = ResizeArray<ResultRow>()
    let frames = ResizeArray<float>(2048)
    let mutable index = 0
    let mutable warming = true
    let mutable elapsed = 0.0
    let mutable previous = -1.0
    let mutable running = true

    let prepare () =
        configure (GraphicsQuality.preset tiers.[index])
        panel.textContent <- "Benchmark · " + GraphicsQuality.label tiers.[index] + " · Aufwärmen"
        warming <- true
        elapsed <- 0.0
        previous <- -1.0
        frames.Clear()

    let finish () =
        let results = rows.ToArray()
        window?__bench <- results
        window?console?table(results)
        panel.innerHTML <- "<table class='market-table'><caption>Grafik-Benchmark · 10 Sekunden Kampfflug je Stufe</caption><thead><tr><th>Stufe</th><th>Ø FPS</th><th>p95 (ms)</th><th>p99 (ms)</th><th>Maximum (ms)</th></tr></thead><tbody></tbody></table>"
        let body = panel.querySelector "tbody"
        for row in results do
            let tr = document.createElement "tr"
            for value in [| row.tier; sprintf "%.2f" row.avgFps; sprintf "%.2f" row.p95ms; sprintf "%.2f" row.p99ms; sprintf "%.2f" row.maxMs |] do
                let cell = document.createElement "td"
                cell.textContent <- value
                tr.appendChild cell |> ignore
            body.appendChild tr |> ignore
        running <- false

    // Nur sichtbare Frame-Intervalle messen. Echte Ruckler bleiben im Maximum.
    document.addEventListener("visibilitychange", fun _ -> previous <- -1.0)
    let rec frame (now: float) =
        if document?visibilityState <> "hidden" then
            let dt = if previous < 0.0 then 0.0 else now - previous
            previous <- now
            elapsed <- elapsed + dt
            if warming && elapsed >= 1000.0 then
                reset ()
                warming <- false
                elapsed <- 0.0
                panel.textContent <- "Benchmark · " + GraphicsQuality.label tiers.[index] + " · Messung (10 s)"
            elif not warming && dt > 0.0 then
                frames.Add dt
                if elapsed >= 10000.0 then
                    rows.Add(summarize (GraphicsQuality.label tiers.[index]) (frames.ToArray()))
                    index <- index + 1
                    if index < tiers.Length then prepare () else finish ()
            if running then render (elapsed / 1000.0)
        if running then window.requestAnimationFrame frame |> ignore
    prepare ()
    window.requestAnimationFrame frame |> ignore
