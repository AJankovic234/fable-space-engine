module SpaceEngine.Main

// =====================================================================================
//  Main.fs  -  Spielschleife, Game-States und Wave-System
//
//  Ablauf eines Frames (requestAnimationFrame, variable Bildrate):
//    1. Eingabe-Flanken auswerten (Start, Pause, Kamera)
//    2. Simulation in festen 120-Hz-Schritten ueber einen Akkumulator:
//         Spieler-Eingabe -> AI.update -> Physics.step
//    3. Ereignisse der Simulation auswerten (Punkte, Treffer-Feedback, Spielende)
//    4. Wave-System fortschreiben
//    5. Kamera nachfuehren, Szene interpoliert zeichnen, HUD zeichnen
//
//  Game-States:   Menu --Start--> Playing <--P--> Paused
//                                    |
//                             Schiff zerstoert
//                                    v
//                                 GameOver --Neustart--> Playing
//
//  Wave-System:   Intermission --(Timer)--> Fighting --(alle Gegner zerstoert)--> Intermission
//                 Jede Welle bringt einen Gegner mehr (bis 8) und staerkere Gegner.
// =====================================================================================

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Browser.Dom

// -------------------------------------------------------------------------------------
//  Zustaende
// -------------------------------------------------------------------------------------

type GameState =
    /// Titelbildschirm; die Welt laeuft als Kulisse.
    | Menu
    | Playing
    /// Simulation angehalten, Bild bleibt stehen.
    | Paused
    /// Spieler zerstoert; die Welt laeuft weiter, bis neu gestartet wird.
    | GameOver

type WavePhase =
    /// Pause zwischen zwei Wellen.
    | Intermission
    /// Gegner der aktuellen Welle sind unterwegs.
    | Fighting

/// Einmal angelegte Instanzen der feldlosen DU-Faelle: Fable wuerde sonst bei jeder
/// Zuweisung ein neues Objekt erzeugen.
module private States =
    let menu = Menu
    let playing = Playing
    let paused = Paused
    let gameOver = GameOver
    let intermission = Intermission
    let fighting = Fighting

// -------------------------------------------------------------------------------------
//  Konstanten
// -------------------------------------------------------------------------------------

[<Literal>]
let private PhysicsStep = 0.008333333333333333 // 1/120 s

/// Obergrenze an Physikschritten pro Frame (verhindert die "Todesspirale",
/// wenn ein Frame laenger dauert als die Simulation aufholen kann).
[<Literal>]
let private MaxStepsPerFrame = 8

[<Literal>]
let private InitialAsteroids = 420

/// Plaetze fuer Gegner in der Welt = groesste Welle.
[<Literal>]
let private MaxEnemies = 8

[<Literal>]
let private DustCount = 420

/// Kantenlaenge des Wuerfels um die Kamera, in dem der Raumstaub lebt.
[<Literal>]
let private DustCell = 160.0

/// Pro Staubkorn: [x, y, z, Groesse]
[<Literal>]
let private DustStride = 4

[<Literal>]
let private StarCount = 2600

/// Aenderung des Schubhebels pro Sekunde bei gehaltener Taste.
[<Literal>]
let private ThrottleRate = 0.7

/// Sekunden zwischen zwei Wellen.
[<Literal>]
let private IntermissionTime = 5.0

/// Sekunden zwischen der Zerstoerung des Spielers und dem Game-Over-Bildschirm.
[<Literal>]
let private DeathDelay = 2.6

/// Schluessel des Bestwerts im localStorage des Browsers.
[<Literal>]
let private BestKey = "fable-space-engine.best"

// -------------------------------------------------------------------------------------
//  Szene
// -------------------------------------------------------------------------------------

type private Scene =
    { Renderer: Renderer
      Hud: Hud
      Input: InputState
      Camera: Camera
      World: World
      Brains: AiBrain[]
      // --- Batches (je ein Draw-Call) ---
      ShipBatch: InstanceBatch
      EnemyBatch: InstanceBatch
      AsteroidBatches: InstanceBatch[]
      DebrisBatch: InstanceBatch
      SkyBatch: InstanceBatch
      SunBatch: InstanceBatch
      DustBatch: InstanceBatch
      ExhaustBatch: InstanceBatch
      LaserBatch: InstanceBatch
      GlowBatch: InstanceBatch
      Dust: JS.Float32Array
      ShipScale: Vec3
      EnemyScale: Vec3
      // --- Spielzustand ---
      mutable State: GameState
      /// Sekunden seit dem letzten Zustandswechsel.
      mutable StateTime: float
      mutable Phase: WavePhase
      mutable PhaseTimer: float
      mutable Wave: int
      mutable Score: int
      mutable Kills: int
      mutable Best: int
      mutable DeathTimer: float
      mutable EnemiesAlive: int
      /// Kamerawackeln nach Treffern und nahen Explosionen (klingt ab).
      mutable Shake: float
      // --- Zeit ---
      mutable Time: float
      mutable LastTimestamp: float
      mutable Accumulator: float
      mutable FrameCounter: int
      mutable StatTimer: float }

let private createDust () : JS.Float32Array =
    let rng = Rng(4711)
    let data = JS.Constructors.Float32Array.Create(DustCount * DustStride)

    for i in 0 .. DustCount - 1 do
        let o = i * DustStride
        data.[o] <- float32 (rng.Range(0.0, DustCell))
        data.[o + 1] <- float32 (rng.Range(0.0, DustCell))
        data.[o + 2] <- float32 (rng.Range(0.0, DustCell))
        data.[o + 3] <- float32 (rng.Range(0.05, 0.13))

    data

/// Liest den gespeicherten Bestwert (0, wenn keiner vorhanden oder der Speicher gesperrt ist).
let private loadBest () : int =
    try
        match System.Int32.TryParse(window.localStorage.getItem BestKey) with
        | true, value -> value
        | _ -> 0
    with _ ->
        0

let private saveBest (value: int) =
    try
        window.localStorage.setItem (BestKey, string value)
    with _ ->
        ()

let private createScene () : Scene =
    let glCanvas = unbox<HTMLCanvasElement> (document.getElementById "gl-canvas")
    let hudCanvas = unbox<HTMLCanvasElement> (document.getElementById "hud-canvas")
    let renderer = Renderer.create glCanvas
    let camera = Camera.create ()
    camera.CockpitOffset <- ShipHardpoints.playerCockpit

    // Sonne: Richtung ZUR Sonne; dieselbe Richtung bekommt die sichtbare Sonnenscheibe
    let sunDirection = Vec3.normalize (Vec3(0.78, 0.42, 0.28))
    Lights.setSun renderer.Lights sunDirection.X sunDirection.Y sunDirection.Z 1.0 0.95 0.86
    Lights.setAmbient renderer.Lights 0.06 0.07 0.10

    // Meshes
    let shipMesh = Renderer.createMesh renderer (ProceduralMeshes.playerShip ())
    let enemyMesh = Renderer.createMesh renderer (ProceduralMeshes.interceptor ())
    let boltMesh = Renderer.createMesh renderer (ProceduralMeshes.laserBolt ())
    let starMesh = Renderer.createMesh renderer (ProceduralMeshes.glowSprite 8 3)
    let particleMesh = Renderer.createMesh renderer (ProceduralMeshes.glowSprite 12 3)
    let sunMesh = Renderer.createMesh renderer (ProceduralMeshes.glowSprite 24 6)

    let asteroidMeshes =
        [| Renderer.createMesh renderer (ProceduralMeshes.asteroid 11 2)
           Renderer.createMesh renderer (ProceduralMeshes.asteroid 23 2)
           Renderer.createMesh renderer (ProceduralMeshes.asteroid 37 1)
           Renderer.createMesh renderer (ProceduralMeshes.asteroid 51 1) |]

    let world = World.create MaxEnemies
    World.reset world InitialAsteroids

    // Batches
    let shipBatch = Renderer.createBatch renderer shipMesh 1 Opaque
    shipBatch.Shininess <- 70.0
    shipBatch.Specular <- 0.6
    let enemyBatch = Renderer.createBatch renderer enemyMesh MaxEnemies Opaque
    enemyBatch.Shininess <- 60.0
    enemyBatch.Specular <- 0.55

    let asteroidBatches =
        asteroidMeshes
        |> Array.map (fun mesh ->
            let batch = Renderer.createBatch renderer mesh world.Asteroids.Capacity Opaque
            batch.Shininess <- 12.0
            batch.Specular <- 0.06
            batch)

    // Truemmer nutzen das grobe Asteroiden-Mesh (80 Dreiecke)
    let debrisBatch = Renderer.createBatch renderer asteroidMeshes.[2] world.Particles.Capacity Opaque
    debrisBatch.Shininess <- 20.0
    debrisBatch.Specular <- 0.15

    // Himmel und Sonne werden einmal gefuellt und danach nie wieder hochgeladen.
    let skyBatch = Renderer.createBatch renderer starMesh StarCount Additive
    skyBatch.CameraRelative <- true
    ProceduralMeshes.fillStarfield skyBatch 1977 (camera.Far * 0.85)
    let sunBatch = Renderer.createBatch renderer sunMesh 3 Additive
    sunBatch.CameraRelative <- true
    ProceduralMeshes.fillSun sunBatch (camera.Far * 0.85) sunDirection

    let dustBatch = Renderer.createBatch renderer boltMesh DustCount Additive
    let exhaustBatch = Renderer.createBatch renderer boltMesh (4 + MaxEnemies * 2) Additive
    let laserBatch = Renderer.createBatch renderer boltMesh world.Lasers.Capacity Additive
    let glowBatch = Renderer.createBatch renderer particleMesh world.Particles.Capacity Additive

    Camera.snap camera world.Player.Position world.Player.Rotation

    let hud = Hud.create hudCanvas
    let best = loadBest ()
    Hud.setBest hud best
    let scale = ShipHardpoints.interceptorScale

    { Renderer = renderer
      Hud = hud
      Input = Input.attach glCanvas
      Camera = camera
      World = world
      Brains = Array.init MaxEnemies AI.createBrain
      ShipBatch = shipBatch
      EnemyBatch = enemyBatch
      AsteroidBatches = asteroidBatches
      DebrisBatch = debrisBatch
      SkyBatch = skyBatch
      SunBatch = sunBatch
      DustBatch = dustBatch
      ExhaustBatch = exhaustBatch
      LaserBatch = laserBatch
      GlowBatch = glowBatch
      Dust = createDust ()
      ShipScale = Vec3.one
      EnemyScale = Vec3(scale, scale, scale)
      State = States.menu
      StateTime = 0.0
      Phase = States.intermission
      PhaseTimer = 0.0
      Wave = 0
      Score = 0
      Kills = 0
      Best = best
      DeathTimer = 0.0
      EnemiesAlive = 0
      Shake = 0.0
      Time = 0.0
      LastTimestamp = -1.0
      Accumulator = 0.0
      FrameCounter = 0
      StatTimer = 0.0 }

// -------------------------------------------------------------------------------------
//  Spielablauf
// -------------------------------------------------------------------------------------

let private setState (scene: Scene) (state: GameState) =
    scene.State <- state
    scene.StateTime <- 0.0

/// Liegt der Punkt mit `margin` Abstand ausserhalb aller Asteroiden?
let private isFreeSpace (field: AsteroidField) (x: float) (y: float) (z: float) (margin: float) : bool =
    let d = field.Data
    let mutable free = true

    for i in 0 .. field.Count - 1 do
        let o = i * AsteroidField.Stride
        let dx = float d.[o] - x
        let dy = float d.[o + 1] - y
        let dz = float d.[o + 2] - z
        let reach = float d.[o + 6] + margin

        if dx * dx + dy * dy + dz * dz < reach * reach then
            free <- false

    free

/// Setzt einen Gegner 500-700 Einheiten vom Spieler entfernt in freien Raum.
/// Sein Patrouillengebiet ist die aktuelle Position des Spielers - er fliegt
/// also heran und entdeckt ihn dabei.
let private spawnEnemy (scene: Scene) (index: int) =
    let world = scene.World
    let rng = world.Rng
    let player = world.Player
    let mutable position = player.Position
    let mutable attempts = 0
    let mutable placed = false

    while not placed && attempts < 12 do
        let direction = rng.UnitVector()
        // Flach halten: Gegner kommen eher von der Seite als von oben/unten
        let flat = Vec3.normalize (Vec3(direction.X, direction.Y * 0.35, direction.Z + 0.001))
        position <- player.Position + flat * rng.Range(500.0, 700.0)
        placed <- isFreeSpace world.Asteroids position.X position.Y position.Z 30.0
        attempts <- attempts + 1

    let enemy = world.Enemies.[index]
    Ship.reset enemy position (Quat.lookRotation (player.Position - position) Vec3.unitY)
    AI.reset scene.Brains.[index] enemy player.Position

/// Anzahl der Gegner in Welle `wave`: zwei in der ersten, dann jeweils einer mehr.
let private waveSize (wave: int) : int = System.Math.Min(MaxEnemies, wave + 1)

/// Startet die naechste Welle. Ab der zweiten Welle werden die Gegner pro Welle
/// 10 % robuster und ihre Laser 6 % staerker.
let private startWave (scene: Scene) =
    scene.Wave <- scene.Wave + 1
    let count = waveSize scene.Wave
    let level = float (scene.Wave - 1)

    for i in 0 .. count - 1 do
        let enemy = scene.World.Enemies.[i]
        enemy.MaxHull <- 45.0 * (1.0 + 0.10 * level)
        enemy.MaxShield <- 25.0 * (1.0 + 0.10 * level)
        enemy.LaserDamage <- 8.0 * (1.0 + 0.06 * level)
        spawnEnemy scene i

    scene.Phase <- States.fighting
    Hud.notify scene.Hud ("WELLE " + string scene.Wave) (string count + " Abfangjaeger im Anflug") 3.5

/// Alle Gegner der Welle sind zerstoert: Bonus, Feldreparatur, kurze Pause.
let private completeWave (scene: Scene) =
    let player = scene.World.Player
    let bonus = 250 * scene.Wave
    scene.Score <- scene.Score + bonus
    player.Hull <- System.Math.Min(player.MaxHull, player.Hull + 35.0)
    scene.Phase <- States.intermission
    scene.PhaseTimer <- IntermissionTime

    Hud.notify
        scene.Hud
        ("WELLE " + string scene.Wave + " ABGEWEHRT")
        ("+" + string bonus + " Punkte   ::   Huelle repariert")
        3.5

/// Beginnt ein neues Spiel (aus dem Menue oder nach Game Over).
let private startGame (scene: Scene) =
    let world = scene.World
    World.reset world InitialAsteroids
    Camera.snap scene.Camera world.Player.Position world.Player.Rotation
    scene.Score <- 0
    scene.Kills <- 0
    scene.Wave <- 0
    scene.Phase <- States.intermission
    scene.PhaseTimer <- 2.5
    scene.DeathTimer <- 0.0
    scene.Shake <- 0.0
    scene.Accumulator <- 0.0
    scene.Hud.DamageFlash <- 0.0
    scene.Hud.Target <- -1
    Hud.notify scene.Hud "BEREIT MACHEN" "Die erste Welle ist im Anflug" 2.3
    setState scene States.playing

/// Spielende: Bestwert aktualisieren und Ergebnis fuer den Bildschirm hinterlegen.
let private endGame (scene: Scene) =
    let isBest = scene.Score > scene.Best

    if isBest then
        scene.Best <- scene.Score
        saveBest scene.Best
        Hud.setBest scene.Hud scene.Best

    Hud.setResult scene.Hud scene.Score scene.Wave scene.Kills scene.Best isBest
    setState scene States.gameOver

/// Uebertraegt die Eingabe in die Steuerbefehle des Spielerschiffs.
let private applyPlayerInput (input: InputState) (ship: Ship) (dt: float) =
    let c = ship.Controls
    c.Pitch <- Input.pitch input
    c.Yaw <- Input.yaw input
    c.Roll <- Input.roll input
    let throttle = c.Throttle + Input.throttleChange input * ThrottleRate * dt
    c.Throttle <- System.Math.Min(1.0, System.Math.Max(0.0, throttle))
    c.Boost <- Input.isDown input GameAction.Boost
    c.Brake <- Input.isDown input GameAction.Brake
    c.Fire <- Input.fire input

/// Kulisse im Menue: Das Schiff steht und dreht sich langsam - die Kamera
/// schwenkt dadurch gemaechlich ueber Asteroidenfeld und Himmel.
let private applyMenuControls (ship: Ship) =
    let c = ship.Controls
    c.Pitch <- 0.0
    c.Yaw <- 0.13
    c.Roll <- 0.0
    c.Boost <- false
    c.Brake <- true
    c.Fire <- false

/// Wertet die Ereignisse der Simulation aus: Punkte, Treffer-Feedback, Spielende.
let private processEvents (scene: Scene) =
    let events = scene.World.Events
    let player = scene.World.Player

    let playing =
        match scene.State with
        | Playing -> true
        | _ -> false

    for i in 0 .. events.Count - 1 do
        match events.[i] with
        | AsteroidDestroyed(radius, team) ->
            if playing && team = Team.Player then
                scene.Score <- scene.Score + 10 + int radius
        | ShipDestroyed(victim, position) ->
            if victim = Team.Enemy then
                if playing then
                    scene.Kills <- scene.Kills + 1
                    scene.Score <- scene.Score + 100 * System.Math.Max(1, scene.Wave)

                // Nahe Explosionen ruetteln an der Kamera
                let distance = Vec3.distance position player.Position

                if distance < 120.0 then
                    scene.Shake <- scene.Shake + 0.9 * (1.0 - distance / 120.0)
            elif playing then
                scene.DeathTimer <- DeathDelay
                scene.Shake <- scene.Shake + 1.5
        | ShipHit(victim, damage) ->
            if victim = Team.Player then
                Hud.damage scene.Hud damage
                scene.Shake <- scene.Shake + damage * 0.03
            else
                Hud.hitMarker scene.Hud
        | LaserFired _ -> ()

    events.Clear()

/// Zaehlt die lebenden Gegner und schreibt das Wave-System fort.
let private updateWaves (scene: Scene) (dt: float) =
    let enemies = scene.World.Enemies
    let mutable alive = 0

    for i in 0 .. enemies.Length - 1 do
        if enemies.[i].Alive then
            alive <- alive + 1
            // Jaeger, die den Spieler verloren haben, patrouillieren um seine aktuelle
            // Position statt um den alten Treffpunkt - so findet jede Welle ihr Ende.
            scene.Brains.[i].Home <- scene.World.Player.Position

    scene.EnemiesAlive <- alive

    match scene.State with
    | Playing ->
        if scene.World.Player.Alive then
            match scene.Phase with
            | Intermission ->
                scene.PhaseTimer <- scene.PhaseTimer - dt

                if scene.PhaseTimer <= 0.0 then
                    startWave scene
            | Fighting ->
                if alive = 0 then
                    completeWave scene
        else
            // Die Explosion des eigenen Schiffs noch zeigen, dann Game Over
            scene.DeathTimer <- scene.DeathTimer - dt

            if scene.DeathTimer <= 0.0 then
                endGame scene
    | _ -> ()

// -------------------------------------------------------------------------------------
//  Darstellung
// -------------------------------------------------------------------------------------

/// Schreibt alle Asteroiden in ihre Batches. `backstep` verschiebt bewegte
/// Objekte auf den interpolierten Darstellungszeitpunkt zurueck.
let private pushAsteroids (scene: Scene) (backstep: float) =
    let field = scene.World.Asteroids
    let a = field.Data
    let time = scene.World.Time - backstep

    for v in 0 .. AsteroidField.Variants - 1 do
        InstanceBatch.clear scene.AsteroidBatches.[v]

    for i in 0 .. field.Count - 1 do
        let o = i * AsteroidField.Stride
        let radius = float a.[o + 6]
        let half = (float a.[o + 11] + time * float a.[o + 10]) * 0.5
        let s = sin half

        InstanceBatch.pushTRS
            scene.AsteroidBatches.[int field.Variant.[i]]
            (float a.[o] - float a.[o + 3] * backstep)
            (float a.[o + 1] - float a.[o + 4] * backstep)
            (float a.[o + 2] - float a.[o + 5] * backstep)
            (float a.[o + 7] * s)
            (float a.[o + 8] * s)
            (float a.[o + 9] * s)
            (cos half)
            radius
            radius
            radius
            (float a.[o + 12])
            (float a.[o + 13])
            (float a.[o + 14])
            0.0

/// Alle Laser-Bolts in einem Draw-Call. Jeder Bolt bekommt zusaetzlich einen
/// Lichthof im Glow-Batch - von hinten gesehen waere ein Bolt sonst nur ein Punkt.
/// Muss NACH pushParticles laufen (das leert den Glow-Batch).
let private pushLasers (scene: Scene) (backstep: float) =
    let pool = scene.World.Lasers
    let d = pool.Data
    let cameraRotation = scene.Camera.Rotation
    InstanceBatch.clear scene.LaserBatch

    for i in 0 .. pool.Count - 1 do
        let o = i * LaserPool.Stride
        let isPlayer = int pool.Team.[i] = int Team.Player
        let x = float d.[o] - float d.[o + 3] * backstep
        let y = float d.[o + 1] - float d.[o + 4] * backstep
        let z = float d.[o + 2] - float d.[o + 5] * backstep
        let r = if isPlayer then 0.25 else 1.7
        let g = if isPlayer then 1.6 else 0.22
        let b = if isPlayer then 0.75 else 0.12

        InstanceBatch.pushTRS
            scene.LaserBatch
            x
            y
            z
            (float d.[o + 6])
            (float d.[o + 7])
            (float d.[o + 8])
            (float d.[o + 9])
            0.5
            0.5
            13.0
            r
            g
            b
            1.0

        InstanceBatch.pushTRS
            scene.GlowBatch
            x
            y
            z
            cameraRotation.X
            cameraRotation.Y
            cameraRotation.Z
            cameraRotation.W
            1.7
            1.7
            1.7
            (r * 0.8)
            (g * 0.8)
            (b * 0.8)
            1.0

/// Partikel: Leuchtpunkte als Billboards (Rotation = Kamerarotation) in den
/// additiven Batch, Truemmer als taumelnde, beleuchtete Brocken in den deckenden.
let private pushParticles (scene: Scene) (backstep: float) =
    let pool = scene.World.Particles
    let d = pool.Data
    let cameraRotation = scene.Camera.Rotation
    let cx = cameraRotation.X
    let cy = cameraRotation.Y
    let cz = cameraRotation.Z
    let cw = cameraRotation.W
    InstanceBatch.clear scene.GlowBatch
    InstanceBatch.clear scene.DebrisBatch

    for i in 0 .. pool.Count - 1 do
        let o = i * ParticlePool.Stride
        let vx = float d.[o + 3]
        let vy = float d.[o + 4]
        let vz = float d.[o + 5]
        let x = float d.[o] - vx * backstep
        let y = float d.[o + 1] - vy * backstep
        let z = float d.[o + 2] - vz * backstep
        let life = float d.[o + 6]
        // t laeuft von 1 (frisch) nach 0 (erloschen)
        let t = life / float d.[o + 7]
        let size = float d.[o + 8]

        if int pool.Kind.[i] = ParticleKind.Glow then
            // Waechst leicht und verglimmt quadratisch
            let s = size * (1.3 - 0.6 * t)
            let fade = t * t

            InstanceBatch.pushTRS
                scene.GlowBatch
                x
                y
                z
                cx
                cy
                cz
                cw
                s
                s
                s
                (float d.[o + 9] * fade)
                (float d.[o + 10] * fade)
                (float d.[o + 11] * fade)
                1.0
        else
            // Taumeln um eine feste, aus der Geschwindigkeit abgeleitete Achse
            let len = System.Math.Max(1e-3, sqrt (vx * vx + vy * vy + vz * vz))
            let half = life * 2.2
            let sh = sin half / len
            // Kurz vor dem Erloeschen schrumpfen statt abrupt zu verschwinden
            let s = size * System.Math.Min(1.0, t * 5.0)

            InstanceBatch.pushTRS
                scene.DebrisBatch
                x
                y
                z
                (vy * sh)
                (vz * sh)
                (vx * sh)
                (cos half)
                s
                (s * 0.7)
                (s * 1.2)
                (float d.[o + 9])
                (float d.[o + 10])
                (float d.[o + 11])
                0.0

/// Raumstaub: Die Koerner liegen in einem Wuerfel, der sich periodisch wiederholt
/// und immer um die Kamera zentriert ist. Bei Fahrt werden sie entlang der
/// Flugrichtung zu Strichen gestreckt - der wichtigste Geschwindigkeitseindruck.
let private pushDust (scene: Scene) =
    let d = scene.Dust
    let camera = scene.Camera.Position
    let velocity = scene.World.Player.Velocity
    let speed = Vec3.length velocity
    let half = DustCell * 0.5
    let mutable qx = 0.0
    let mutable qy = 0.0
    let mutable qz = 0.0
    let mutable qw = 1.0

    if speed > 1.0 then
        let q = Quat.lookRotation velocity Vec3.unitY
        qx <- q.X
        qy <- q.Y
        qz <- q.Z
        qw <- q.W

    let stretch = speed * 0.035
    InstanceBatch.clear scene.DustBatch

    for i in 0 .. DustCount - 1 do
        let o = i * DustStride
        // Position relativ zur Kamera, periodisch in [-half, half)
        let mutable dx = float d.[o] - camera.X
        let mutable dy = float d.[o + 1] - camera.Y
        let mutable dz = float d.[o + 2] - camera.Z
        dx <- dx - floor (dx / DustCell) * DustCell - half
        dy <- dy - floor (dy / DustCell) * DustCell - half
        dz <- dz - floor (dz / DustCell) * DustCell - half
        // Zum Rand hin ausblenden, damit das Umspringen unsichtbar bleibt
        let fade = 1.0 - (dx * dx + dy * dy + dz * dz) / (half * half)

        if fade > 0.0 then
            let size = float d.[o + 3]
            let brightness = 0.55 * fade

            InstanceBatch.pushTRS
                scene.DustBatch
                (camera.X + dx)
                (camera.Y + dy)
                (camera.Z + dz)
                qx
                qy
                qz
                qw
                size
                size
                (size + stretch)
                (0.62 * brightness)
                (0.74 * brightness)
                brightness
                1.0

/// Eine Abgasflamme: beginnt an der Duese (lokale Koordinaten) und reicht
/// `length` nach hinten.
let private pushFlame
    (scene: Scene)
    (position: Vec3)
    (rotation: Quat)
    (nozzle: Vec3)
    (length: float)
    (width: float)
    (r: float)
    (g: float)
    (b: float)
    =
    let center = position + Quat.rotateVec3 rotation (Vec3(nozzle.X, nozzle.Y, nozzle.Z + length * 0.5))

    InstanceBatch.pushTRS
        scene.ExhaustBatch
        center.X
        center.Y
        center.Z
        rotation.X
        rotation.Y
        rotation.Z
        rotation.W
        width
        width
        length
        r
        g
        b
        1.0

/// Schreibt Spielerschiff, Gegner, Abgasflammen und Triebwerkslichter.
let private pushShips (scene: Scene) (playerPosition: Vec3) (playerRotation: Quat) (alpha: float) =
    let world = scene.World
    let lights = scene.Renderer.Lights
    let player = world.Player
    // Leichtes Flackern aller Flammen
    let flicker = 0.92 + 0.08 * sin (scene.Time * 71.0)
    InstanceBatch.clear scene.ShipBatch
    InstanceBatch.clear scene.EnemyBatch
    InstanceBatch.clear scene.ExhaustBatch

    if player.Alive then
        let flash = player.HitFlash

        InstanceBatch.push
            scene.ShipBatch
            playerPosition
            playerRotation
            scene.ShipScale
            (1.0 + flash * 0.5)
            (1.0 + flash * 0.5)
            (1.0 + flash * 0.5)
            (flash * 0.25)

        let thrust = if player.Controls.Brake then 0.0 else player.Controls.Throttle
        let length = (0.35 + 1.5 * thrust + 3.4 * player.Boost) * flicker
        let glow = 0.35 + 0.65 * thrust + 0.9 * player.Boost

        // Pro Triebwerk ein breiter blauer Mantel und ein schmaler heisser Kern
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineLeft length 0.34 (0.10 * glow) (0.36 * glow) (0.95 * glow)
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineLeft (length * 0.6) 0.17 (0.75 * glow) (0.9 * glow) glow
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineRight length 0.34 (0.10 * glow) (0.36 * glow) (0.95 * glow)
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineRight (length * 0.6) 0.17 (0.75 * glow) (0.9 * glow) glow

        // Triebwerkslicht: beleuchtet Heck und nahe Asteroiden
        let engine = playerPosition + Quat.rotateVec3 playerRotation (Vec3(0.0, 0.1, 2.6))
        Lights.addPoint lights engine.X engine.Y engine.Z 16.0 0.35 0.65 1.0 (0.3 + 0.9 * thrust + 2.4 * player.Boost)

    for i in 0 .. world.Enemies.Length - 1 do
        let enemy = world.Enemies.[i]

        if enemy.Alive then
            let position = Vec3.lerp enemy.PrevPosition enemy.Position alpha
            let rotation = Quat.slerp enemy.PrevRotation enemy.Rotation alpha
            let flash = enemy.HitFlash

            InstanceBatch.push
                scene.EnemyBatch
                position
                rotation
                scene.EnemyScale
                (1.0 + flash * 1.5)
                (1.0 + flash * 1.5)
                (1.0 + flash * 1.5)
                (flash * 0.6)

            let thrust = if enemy.Controls.Brake then 0.0 else enemy.Controls.Throttle
            let length = (0.3 + 1.3 * thrust + 2.6 * enemy.Boost) * flicker
            let glow = 0.4 + 0.6 * thrust + 0.8 * enemy.Boost
            pushFlame scene position rotation ShipHardpoints.interceptorEngine (length * 1.5) 0.66 (0.95 * glow) (0.30 * glow) (0.06 * glow)
            pushFlame scene position rotation ShipHardpoints.interceptorEngine (length * 0.9) 0.32 glow (0.8 * glow) (0.5 * glow)
            let engine = position + Quat.rotateVec3 rotation (Vec3(0.0, 0.0, 2.9))
            // Lights.addPoint verwirft Lichter jenseits des Limits von selbst
            Lights.addPoint lights engine.X engine.Y engine.Z 12.0 1.0 0.45 0.15 (0.4 + 1.2 * thrust)

/// Uebernimmt die Explosionsblitze der Welt als Point Lights.
let private pushFlashLights (scene: Scene) =
    let f = scene.World.Flashes
    let lights = scene.Renderer.Lights

    for i in 0 .. Flashes.Slots - 1 do
        let o = i * Flashes.Stride
        let intensity = float f.[o + 7]

        if intensity > 0.0 then
            Lights.addPoint
                lights
                (float f.[o])
                (float f.[o + 1])
                (float f.[o + 2])
                (float f.[o + 3])
                (float f.[o + 4])
                (float f.[o + 5])
                (float f.[o + 6])
                intensity

let private render (scene: Scene) (position: Vec3) (rotation: Quat) (alpha: float) =
    let renderer = scene.Renderer
    let camera = scene.Camera
    let hud = scene.Hud
    // Die Welt steht einen Bruchteil eines Physikschritts "vor" dem Darstellungs-
    // zeitpunkt; bewegte Pool-Objekte werden um diese Zeit zurueckgerechnet.
    let backstep = (1.0 - alpha) * PhysicsStep

    if Renderer.resize renderer then
        // HUD-Canvas deckungsgleich mit dem WebGL-Canvas halten
        Hud.resize hud renderer.Width renderer.Height renderer.PixelRatio

    Camera.updateMatrices camera (Renderer.aspect renderer)

    // Lichter: Explosionsblitze zuerst (sie sollen nie dem Limit zum Opfer fallen),
    // dann die Triebwerke in pushShips
    Lights.clearPoints renderer.Lights
    pushFlashLights scene

    // Instanzen schreiben
    pushShips scene position rotation alpha
    pushAsteroids scene backstep
    pushParticles scene backstep
    pushLasers scene backstep
    pushDust scene

    // Zeichnen: erst deckend, dann additiv
    Renderer.beginFrame renderer camera.View camera.Projection camera.Position

    for v in 0 .. AsteroidField.Variants - 1 do
        Renderer.draw renderer scene.AsteroidBatches.[v]

    Renderer.draw renderer scene.DebrisBatch
    Renderer.draw renderer scene.ShipBatch
    Renderer.draw renderer scene.EnemyBatch
    Renderer.draw renderer scene.SkyBatch
    Renderer.draw renderer scene.SunBatch
    Renderer.draw renderer scene.DustBatch
    Renderer.draw renderer scene.ExhaustBatch
    Renderer.draw renderer scene.LaserBatch
    Renderer.draw renderer scene.GlowBatch

    // HUD je nach Game-State
    Hud.clear hud

    match scene.State with
    | Menu -> Hud.drawMenu hud
    | Playing ->
        Hud.drawFlight
            hud
            renderer.ViewProj
            (Renderer.aspect renderer)
            scene.World
            scene.Brains
            scene.Input
            alpha
            position
            rotation
            scene.Wave
            scene.Score
            scene.EnemiesAlive
    | Paused ->
        Hud.drawFlight
            hud
            renderer.ViewProj
            (Renderer.aspect renderer)
            scene.World
            scene.Brains
            scene.Input
            alpha
            position
            rotation
            scene.Wave
            scene.Score
            scene.EnemiesAlive

        Hud.drawPaused hud
    | GameOver -> Hud.drawGameOver hud

// -------------------------------------------------------------------------------------
//  Schleife
// -------------------------------------------------------------------------------------

let private scene: Scene option =
    try
        Some(createScene ())
    with ex ->
        // Klartext statt schwarzem Bildschirm, falls WebGL2 fehlt oder ein Shader bricht.
        let box = document.getElementById "error"
        box.textContent <- ex.Message
        box.removeAttribute "hidden"
        console.error ex
        None

#if DEBUG
// Nur im Watch-Modus (npm run dev): Szene fuer Tests in der Browser-Konsole.
window?spaceEngine <- scene
#endif

/// Haelt den requestAnimationFrame-Callback als einmal erzeugtes Funktionsobjekt.
/// Wuerde `frame` direkt uebergeben, erzeugte Fable in jedem Frame eine neue Closure.
type private Loop =
    { mutable Callback: float -> unit }

let private loop: Loop = { Callback = ignore }

/// Aktualisiert die Statistikzeile des HUD viermal pro Sekunde.
let private updateStats (scene: Scene) (dt: float) =
    scene.FrameCounter <- scene.FrameCounter + 1
    scene.StatTimer <- scene.StatTimer + dt

    if scene.StatTimer >= 0.25 then
        let renderer = scene.Renderer
        let fps = int (float scene.FrameCounter / scene.StatTimer + 0.5)
        scene.FrameCounter <- 0
        scene.StatTimer <- 0.0

        Hud.setStats
            scene.Hud
            (string fps
             + " FPS   "
             + string renderer.DrawCalls
             + " Draw-Calls   "
             + string renderer.InstancesDrawn
             + " Instanzen")

let private frame (timestamp: float) =
    match scene with
    | Some scene ->
        let input = scene.Input
        let world = scene.World
        let ship = world.Player

        // dt deckeln, damit ein Tab-Wechsel keinen Zeitsprung erzeugt
        let dt =
            if scene.LastTimestamp < 0.0 then
                0.0
            else
                System.Math.Min(0.1, (timestamp - scene.LastTimestamp) / 1000.0)

        scene.LastTimestamp <- timestamp
        scene.StateTime <- scene.StateTime + dt

        // ---- 1. Eingabe-Flanken: gehoeren zum Frame, nicht zum Physikschritt ----
        let confirm = Input.wasPressed input GameAction.Confirm || input.Clicked

        match scene.State with
        | Menu ->
            if confirm then
                startGame scene
        | Playing ->
            if Input.wasPressed input GameAction.Pause then
                setState scene States.paused
        | Paused ->
            if Input.wasPressed input GameAction.Pause || Input.wasPressed input GameAction.Confirm then
                setState scene States.playing
        | GameOver ->
            // Kurze Sperre, damit ein noch gedrueckter Feuerknopf nicht sofort neu startet
            if confirm && scene.StateTime > 1.0 then
                startGame scene

        if Input.wasPressed input GameAction.ToggleCamera then
            Camera.toggleMode scene.Camera

        if Input.wasPressed input GameAction.InvertPitch then
            input.InvertPitch <- not input.InvertPitch

        // ---- 2. Simulation mit festem Zeitschritt (steht in der Pause) ----
        let simulate =
            match scene.State with
            | Paused -> false
            | _ -> true

        if simulate then
            scene.Time <- scene.Time + dt
            scene.Accumulator <- scene.Accumulator + dt
            let mutable steps = 0

            while scene.Accumulator >= PhysicsStep && steps < MaxStepsPerFrame do
                match scene.State with
                | Menu -> applyMenuControls ship
                | Playing ->
                    if ship.Alive then
                        applyPlayerInput input ship PhysicsStep
                | _ -> ()

                AI.update world scene.Brains PhysicsStep
                Physics.step world PhysicsStep
                scene.Accumulator <- scene.Accumulator - PhysicsStep
                steps <- steps + 1

            if steps = MaxStepsPerFrame then
                scene.Accumulator <- 0.0

            // ---- 3. + 4. Ereignisse und Wave-System ----
            processEvents scene
            updateWaves scene dt
            Hud.update scene.Hud dt

        // ---- 5. Darstellung zwischen letztem und aktuellem Physikzustand interpolieren ----
        let alpha = scene.Accumulator / PhysicsStep
        let position = Vec3.lerp ship.PrevPosition ship.Position alpha
        let rotation = Quat.slerp ship.PrevRotation ship.Rotation alpha

        if simulate then
            Camera.update scene.Camera position rotation ship.Boost dt

            // Kamerawackeln: klingt exponentiell ab und haeuft sich nicht auf, weil
            // Camera.update die Position jeden Frame neu aus dem Ziel berechnet.
            if scene.Shake > 0.01 then
                let amount = System.Math.Min(scene.Shake, 2.0) * 0.3
                let t = scene.Time
                let p = scene.Camera.Position

                scene.Camera.Position <-
                    Vec3(
                        p.X + sin (t * 83.0) * amount,
                        p.Y + sin (t * 71.0 + 1.3) * amount,
                        p.Z + sin (t * 97.0 + 2.1) * amount
                    )

                scene.Shake <- scene.Shake * exp (-7.0 * dt)
            else
                scene.Shake <- 0.0

        updateStats scene dt
        render scene position rotation alpha
        Input.endFrame input
        window.requestAnimationFrame loop.Callback |> ignore
    | None -> ()

loop.Callback <- frame
window.requestAnimationFrame loop.Callback |> ignore
