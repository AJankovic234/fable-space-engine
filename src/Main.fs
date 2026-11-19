module SpaceEngine.Main

// =====================================================================================
//  Main.fs  -  Spielschleife, Missionsabschnitte und Aufrüstung
//
//  Ablauf eines Frames (requestAnimationFrame, variable Bildrate):
//    1. Eingabe-Flanken auswerten (Start, Pause, Kamera)
//    2. Simulation in festen 120-Hz-Schritten ueber einen Akkumulator:
//         Spieler-Eingabe -> AI.update -> Physics.step
//    3. Ereignisse der Simulation auswerten (Punkte, Treffer-Feedback, Spielende)
//    4. Wave-System fortschreiben
//    5. Kamera nachfuehren, Szene interpoliert zeichnen, HUD zeichnen
//
//  Playing <-> Paused; abgeschlossener Sektor -> Refitting -> Playing.
//  Sechs gesicherte Sektoren -> Victory; Schiff zerstört -> GameOver.
//  Pro Sektor treffen feste gemischte Verbände in Verstärkungsgruppen ein.
// =====================================================================================

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Browser.Dom

// -------------------------------------------------------------------------------------
//  Zustaende
// -------------------------------------------------------------------------------------

type GameState =
    /// Titelbildschirm mit Kestrels Anflugbild.
    | Menu
    | Playing
    /// Simulation angehalten, Bild bleibt stehen.
    | Paused
    | Refitting
    | Victory
    /// Spieler zerstoert; die Welt laeuft weiter, bis neu gestartet wird.
    | GameOver

type WavePhase =
    /// Pause zwischen zwei Wellen.
    | Intermission
    /// Gegner der aktuellen Welle sind unterwegs.
    | Fighting
    | Salvaging

/// Einmal angelegte Instanzen der feldlosen DU-Faelle: Fable wuerde sonst bei jeder
/// Zuweisung ein neues Objekt erzeugen.
module private States =
    let menu = Menu
    let playing = Playing
    let paused = Paused
    let refitting = Refitting
    let victory = Victory
    let gameOver = GameOver
    let intermission = Intermission
    let fighting = Fighting
    let salvaging = Salvaging

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

/// Plätze für den größten Missionsverband einschließlich Reserve.
[<Literal>]
let private MaxEnemies = 16

[<Literal>]
let private DustCount = 420

/// Kantenlaenge des Wuerfels um die Kamera, in dem der Raumstaub lebt.
[<Literal>]
let private DustCell = 160.0

/// Pro Staubkorn: [x, y, z, Groesse]
[<Literal>]
let private DustStride = 4

/// Sekunden zwischen zwei Wellen.
[<Literal>]
let private IntermissionTime = 8.0

/// Sekunden zwischen der Zerstoerung des Spielers und dem Game-Over-Bildschirm.
[<Literal>]
let private DeathDelay = 2.6

/// Schluessel des Bestwerts im localStorage des Browsers.
[<Literal>]
let private BestKey = "fable-space-engine.best"

// -------------------------------------------------------------------------------------
//  Szene
// -------------------------------------------------------------------------------------

type private BlockadeModels =
    { Cruiser: InstanceBatch
      Shutter: InstanceBatch
      Breach: InstanceBatch
      Battery: InstanceBatch
      Bomber: InstanceBatch
      Torpedoes: InstanceBatch }

type private Scene =
    { Renderer: Renderer
      Hud: Hud
      Shop: ShopUi
      Skills: SkillUi
      Yard: BlockadeUi
      mutable Blockade: BlockadeState
      mutable Sortie: CapitalFlightState
      mutable School: FlightSchoolState
      mutable Fleet: HangarFleet
      mutable SovereignWarningArmed: bool
      CustomsBatches: Lazy<BlockadeModels>
      Contact: DockContact
      Saves: SaveUi
      mutable SaveReturnState: GameState
      mutable SaveDirty: bool
      mutable FlightChanged: bool
      mutable SaveTimer: float
      mutable SaveRetryDelay: float
      mutable ResumeJson: string option
      mutable CourseTarget: string option
      mutable CourseVia: string option
      Wallet: Wallet
      Cargo: CargoHold
      Markets: StationMarket[]
      Turret: AutoTurret
      Missiles: MissileRack
      Pickups: ResizeArray<MineralPickup>
      Sound: SoundEngine
      Input: InputState
      Camera: Camera
      World: World
      Brains: AiBrain[]
      // --- Batches (je ein Draw-Call) ---
      HullBatches: Lazy<InstanceBatch>[]
      Outfit: OutfitVisuals
      DeckCargoBatches: InstanceBatch[]
      EnemyBatches: InstanceBatch[]
      CrystalBatch: InstanceBatch
      StationBatches: Lazy<InstanceBatch>[]
      RelicBatches: (Vec3 * Lazy<InstanceBatch>)[]
      mutable ArchiveScan: ArchiveScan option
      FreighterBatch: InstanceBatch
      BuoyFrames: InstanceBatch
      BuoyLenses: InstanceBatch
      GateFrameBatch: InstanceBatch
      GateRingBatch: InstanceBatch
      TurretBaseBatch: InstanceBatch
      TurretBatch: InstanceBatch
      MissileBatch: InstanceBatch
      AsteroidBatches: Lazy<InstanceBatch[]>[]
      AsteroidFrustum: FrustumPlanes
      DebrisBatch: InstanceBatch
      Sky: SkyBackdrop
      DustBatch: InstanceBatch
      ExhaustBatch: InstanceBatch
      EngineHaloBatch: InstanceBatch
      ShieldBatch: InstanceBatch
      LaserBatch: InstanceBatch
      GlowBatch: InstanceBatch
      Dust: JS.Float32Array
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
      mutable Spawned: int
      mutable SpawnTimer: float
      mutable Trading: bool
      mutable Sector: int
      mutable TradeProfit: float
      mutable Trips: int
      mutable Patrol: EnemyRole[]
      Upgrades: int[]
      /// Kamerawackeln nach Treffern und nahen Explosionen (klingt ab).
      mutable Shake: float
      /// Boost-Vorrat 0..1. Leer gesperrt, bis er sich teilweise erholt hat.
      mutable BoostReserve: float
      /// Sekunden seit dem letzten Boost (Erholung setzt verzoegert ein).
      mutable BoostRest: float
      mutable BoostLocked: bool
      /// Heckblick war im letzten Frame aktiv (Kamera springt beim Wechsel).
      mutable LookBack: bool
      /// FPS-/Draw-Call-Zeile oben links (F3).
      mutable ShowStats: bool
      /// Schub folgt dem Tempo des erfassten Ziels (L), bis der Pilot selbst regelt.
      mutable MatchSpeed: bool
      /// Der Regler im Hauptmenue wurde bewegt: Sein Wert gilt statt des gespeicherten.
      mutable SensitivityOverride: bool
      /// Tastenuebersicht ueber dem Flugbild (F1).
      mutable ShowHelp: bool
      // --- Audio-Zustandsflanken ---
      mutable PrevBoostLocked: bool
      mutable PrevOverdrive: float
      mutable PrevShieldUp: bool
      mutable PrevShieldDelay: float
      mutable UpgradeNotified: bool
      mutable JumpNotifiedSector: int
      mutable JumpTransitUntil: float
      mutable GateContactUntil: float
      // --- Zeit ---
      mutable Time: float
      mutable LastTimestamp: float
      mutable Accumulator: float
      mutable FrameCounter: int
      mutable StatTimer: float
      Landmarks: LandmarkMeshes.Models
      Traffic: Traffic.Fleet
      /// Funken und gluehende Splitter (ParticleKind.Streak).
      VfxStreakBatch: InstanceBatch
      /// Stossringe und Schildtreffer (ParticleKind.Ring).
      VfxRingBatch: InstanceBatch
      Graphics: GraphicsSettings.Settings
      /// GPU-Partikel: Triebwerksspuren, Gefechtsfunken, Glut, Rauch, Guertelstaub.
      GpuParticles: GpuParticleSystem
      Trails: EngineTrails.Emitters
      /// Abklingende Lichter der Zerstoerungsstufen (CombatLights).
      CombatLights: CombatLightPool }
    member scene.Pilot = scene.World.Pilot

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

let private stationOrientation =
    Quat.fromAxisAngle Vec3.unitY -0.39 * Quat.fromAxisAngle Vec3.unitX 0.16

[<Literal>]
let private ApproachPairs = 5

let private createScene () : Scene =
    let glCanvas = unbox<HTMLCanvasElement> (document.getElementById "gl-canvas")
    let hudCanvas = unbox<HTMLCanvasElement> (document.getElementById "hud-canvas")
    let renderer = Renderer.create glCanvas
    let camera = Camera.create ()
    let sky = SkyBackdrop.create renderer
    camera.CockpitOffset <- ShipHardpoints.playerCockpit

    // Sonne: Richtung ZUR Sonne; dieselbe Richtung bekommt die sichtbare Sonnenscheibe
    let sunDirection = Vec3.normalize (Vec3(0.78, 0.42, 0.28))
    Lights.setSun renderer.Lights sunDirection.X sunDirection.Y sunDirection.Z 1.0 0.95 0.86
    Lights.setAmbient renderer.Lights 0.085 0.10 0.14

    // Meshes
    let crystalMesh = Renderer.createMesh renderer (ProceduralMeshes.mineralCrystal ())
    let stationBatches =
        Universe.sectors |> Array.map (fun sector ->
            lazy (
                let outpost = sector.Outpost
                let mesh = Renderer.createMesh renderer (StationMeshes.station outpost.Kind)
                let batch = Renderer.createBatch renderer mesh 1 Opaque
                batch.Shininess <- 42.0
                batch.Specular <- 0.31
                InstanceBatch.push batch outpost.Position Quat.identity Vec3.one 1.0 1.0 1.0 0.0
                batch))
    let freighterMesh = Renderer.createMesh renderer (ProceduralMeshes.freightHauler ())
    let relicBatch kind p = lazy (
        let mesh = Renderer.createMesh renderer (ProceduralMeshes.archiveRelic kind true)
        let batch = Renderer.createBatch renderer mesh 1 Opaque
        batch.Shininess <- 34.0
        batch.Specular <- 0.29
        InstanceBatch.push batch p (Recovery.rotation kind) Vec3.one 1.0 1.0 1.0 0.0
        batch)
    let relicBatches =
        Universe.sectors |> Array.mapi (fun i sector ->
            let p = RelicArchive.position sector.Outpost.Position i
            p, relicBatch sector.Outpost.Kind p)
    let buoyBody, buoyLens = ProceduralMeshes.approachBuoy ()
    let buoyBatch data = Renderer.createBatch renderer (Renderer.createMesh renderer data) (2*ApproachPairs) Opaque
    let gateFrameBatch = Renderer.createBatch renderer (Renderer.createMesh renderer (ProceduralMeshes.jumpGateFrame ())) 4 Opaque
    gateFrameBatch.Shininess <- 56.0
    gateFrameBatch.Specular <- 0.45
    let gateRingBatch = Renderer.createBatch renderer (Renderer.createMesh renderer (ProceduralMeshes.jumpGateRing ())) 4 Opaque
    gateRingBatch.Shininess <- 84.0
    gateRingBatch.Specular <- 0.65
    let freighters = Renderer.createBatch renderer freighterMesh 2 Opaque
    freighters.Shininess <- 35.0
    freighters.Specular <- 0.28
    let turretMesh = Renderer.createMesh renderer (ProceduralMeshes.autoTurret ())
    let turretBaseMesh = Renderer.createMesh renderer (ProceduralMeshes.autoTurretBase ())
    let boltMesh = Renderer.createMesh renderer (ProceduralMeshes.laserBolt ())
    let exhaustMesh = Renderer.createMesh renderer (ProceduralMeshes.exhaustPlume ())
    let shieldMesh = Renderer.createMesh renderer (ProceduralMeshes.shieldBubble ())
    let particleMesh = Renderer.createMesh renderer (ProceduralMeshes.glowSprite 12 3)

    let debrisMesh = Renderer.createMesh renderer (ProceduralMeshes.asteroid 37 2)

    let world = World.create MaxEnemies
    World.reset world InitialAsteroids

    // Batches
    let hullBatches =
        PlayerMeshes.models
        |> Array.map (fun geometry -> lazy (
            let batch = Renderer.createBatch renderer (Renderer.createMesh renderer (geometry ())) 1 Opaque
            batch.Shininess <- 70.0
            batch.Specular <- 0.6
            batch))
    let enemyBatches =
        Array.init (int EnemyRole.Blocker + 1) (fun index ->
            let mesh =
                match enum<EnemyRole> index with
                | EnemyRole.Drone -> EnemyMeshes.drone ()
                | EnemyRole.Scout -> EnemyMeshes.scout ()
                | EnemyRole.Gunship -> EnemyMeshes.gunship ()
                | EnemyRole.Bomber -> EnemyMeshes.bomber ()
                | EnemyRole.Commander -> EnemyMeshes.corvette ()
                | EnemyRole.Blocker -> EnemyMeshes.blocker ()
                | _ -> invalidArg "index" "Unbekannte Gegnerrolle"
            let batch = Renderer.createBatch renderer (Renderer.createMesh renderer mesh) MaxEnemies Opaque
            batch.Shininess <- 60.0
            batch.Specular <- 0.55
            batch)

    let asteroidBatches =
        Universe.sectors |> Array.map (AsteroidMeshes.batches renderer world.Asteroids.Capacity)

    // Truemmer teilen sich das einfachere Asteroiden-Mesh (320 Dreiecke).
    let debrisBatch = Renderer.createBatch renderer debrisMesh world.Particles.Capacity Opaque
    debrisBatch.Shininess <- 20.0
    debrisBatch.Specular <- 0.15
    debrisBatch.Material <- 1.0

    let dustBatch = Renderer.createBatch renderer boltMesh DustCount Additive
    let exhaustBatch = Renderer.createBatch renderer exhaustMesh (11 + MaxEnemies * 10 + CapitalMeshes.drives.Length + Traffic.capacity) Additive
    // Hoefe stehen gross im Bild: feinere Scheibe als die wenige Pixel grossen Partikel.
    let haloMesh = Renderer.createMesh renderer (ProceduralMeshes.glowSprite 28 6)
    let engineHaloBatch =
        Renderer.createBatch renderer haloMesh (16 + MaxEnemies * 10 + CapitalMeshes.drives.Length + CapitalMeshes.beacons.Length + SovereignMeshes.beacons.Length + 3 * Traffic.capacity) Additive
    let shieldBatch = Renderer.createBatch renderer shieldMesh (1 + MaxEnemies) Additive
    // Kampfeffekte: je Bolt Saum und Kern; der Glow-Batch traegt Partikel, je Bolt
    // Lichthof und Muendungsstern (2 Scheiben) sowie die Lichthoefe der Mineralien.
    let energyMesh = Renderer.createMesh renderer (ProceduralMeshes.energyBolt ())
    let laserBatch = Renderer.createBatch renderer energyMesh (world.Lasers.Capacity * 2) Additive
    let glowBatch = Renderer.createBatch renderer particleMesh (world.Particles.Capacity + world.Lasers.Capacity * 3 + Salvage.Capacity) Additive
    let streakBatch = Renderer.createBatch renderer energyMesh world.Particles.Capacity Additive
    let ringBatch = Renderer.createBatch renderer (Renderer.createMesh renderer (ProceduralMeshes.shockRing ())) 128 Additive
    // GPU-Partikel; die Kampf-Emitter in Effects schreiben ueber attach hinein.
    let gpuParticles = GpuParticles.create renderer GpuParticles.DefaultBudget
    GpuParticles.attach gpuParticles

    Camera.snap camera world.Player.Position world.Player.Rotation

    let hud = Hud.create hudCanvas
    let sound = Sound.create ()
    let best = loadBest ()
    Hud.setBest hud best
    let scale = ShipHardpoints.interceptorScale

    let scene =
        { Renderer = renderer
          Hud = hud
          Shop = Shop.create ()
          Skills = SkillUi.create ()
          Yard = BlockadeUi.create ()
          Blockade = Blockade.create ()
          Sortie = CapitalFlight.create ()
          School = FlightSchool.create ()
          Fleet = ShipCatalog.create ()
          SovereignWarningArmed = false
          CustomsBatches = lazy (
            let batch model count = Renderer.createBatch renderer (Renderer.createMesh renderer (model ())) count Opaque
            // The dark armour catches a hard highlight in the Helios sun.
            let armoured model count =
                let b = batch model count
                b.Shininess <- 48.0
                b.Specular <- 0.42
                b
            { Cruiser = armoured CapitalMeshes.cruiser 1
              Shutter = armoured (fun () -> CapitalMeshes.shutter false) 1
              Breach = armoured (fun () -> CapitalMeshes.shutter true) 1
              Battery = armoured CapitalMeshes.battery 2
              Bomber = batch PlayerMeshes.bomber 1
              Torpedoes = batch PlayerMeshes.torpedo 2 })
          Contact = DockContact.create sound
          Saves = SaveUi.create ()
          SaveReturnState = States.menu
          SaveDirty = false
          FlightChanged = false
          SaveTimer = 0.0
          SaveRetryDelay = 0.0
          ResumeJson = None
          CourseTarget = None
          CourseVia = None
          Wallet = Economy.create ()
          Cargo = TradeSystem.createCargo 16
          Markets = TradeSystem.createUniverseMarkets ()
          Turret = Turret.create ()
          Missiles = Missiles.create ()
          Pickups = Salvage.create ()
          Sound = sound
          Input = Input.attach glCanvas
          Camera = camera
          World = world
          Brains = Array.init MaxEnemies AI.createBrain
          HullBatches = hullBatches
          Outfit = OutfitVisuals.create renderer
          DeckCargoBatches = Array.init (DeckCargo.sealedIndex + 1) (fun i ->
              let geometry = if i = DeckCargo.sealedIndex then ProceduralMeshes.sealedFreight () else ProceduralMeshes.deckPackage TradeSystem.commodities.[i].Id
              Renderer.createBatch renderer (Renderer.createMesh renderer geometry) DeckCargo.haulerMounts.Length Opaque)
          EnemyBatches = enemyBatches
          CrystalBatch = Renderer.createBatch renderer crystalMesh Salvage.Capacity Opaque
          StationBatches = stationBatches
          RelicBatches = relicBatches
          ArchiveScan = None
          FreighterBatch = freighters
          BuoyFrames = buoyBatch buoyBody
          BuoyLenses = buoyBatch buoyLens
          GateFrameBatch = gateFrameBatch
          GateRingBatch = gateRingBatch
          TurretBaseBatch = Renderer.createBatch renderer turretBaseMesh 1 Opaque
          TurretBatch = Renderer.createBatch renderer turretMesh 1 Opaque
          MissileBatch = Renderer.createBatch renderer (Renderer.createMesh renderer (PlayerMeshes.torpedo ())) Missiles.PoolSize Opaque
          AsteroidBatches = asteroidBatches
          AsteroidFrustum = Frustum.create ()
          DebrisBatch = debrisBatch
          Sky = sky
          DustBatch = dustBatch
          ExhaustBatch = exhaustBatch
          EngineHaloBatch = engineHaloBatch
          ShieldBatch = shieldBatch
          LaserBatch = laserBatch
          GlowBatch = glowBatch
          Dust = createDust ()
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
          Spawned = 0
          SpawnTimer = 0.0
          Trading = true
          Sector = 0
          TradeProfit = 0.0
          Trips = 0
          Patrol = Array.empty
          Upgrades = Array.zeroCreate 3
          Shake = 0.0
          BoostReserve = 1.0
          BoostRest = 0.0
          BoostLocked = false
          LookBack = false
          ShowStats = false
          MatchSpeed = false
          SensitivityOverride = false
          ShowHelp = false
          PrevBoostLocked = false
          PrevOverdrive = 0.0
          PrevShieldUp = true
          PrevShieldDelay = 0.0
          UpgradeNotified = false
          JumpNotifiedSector = -1
          JumpTransitUntil = 0.0
          GateContactUntil = 0.0
          Time = 0.0
          LastTimestamp = -1.0
          Accumulator = 0.0
          FrameCounter = 0
          StatTimer = 0.0
          Landmarks = LandmarkMeshes.create renderer
          Traffic = Traffic.create renderer
          VfxStreakBatch = streakBatch
          VfxRingBatch = ringBatch
          Graphics = GraphicsSettings.Settings(renderer)
          GpuParticles = gpuParticles
          Trails = EngineTrails.create MaxEnemies Traffic.capacity
          CombatLights = CombatLights.create () }
    let initAtmo = (Universe.sectorByIndex 0).Atmosphere
    Lights.setSun renderer.Lights initAtmo.SunDir.X initAtmo.SunDir.Y initAtmo.SunDir.Z initAtmo.SunColor.X initAtmo.SunColor.Y initAtmo.SunColor.Z
    Lights.setAmbient renderer.Lights initAtmo.Ambient.X initAtmo.Ambient.Y initAtmo.Ambient.Z
    SkyLight.apply renderer.Lights 0
    renderer.ClearColor.[0] <- float32 initAtmo.ClearColor.X
    renderer.ClearColor.[1] <- float32 initAtmo.ClearColor.Y
    renderer.ClearColor.[2] <- float32 initAtmo.ClearColor.Z
    scene

// -------------------------------------------------------------------------------------
//  Spielablauf
// -------------------------------------------------------------------------------------

let private clearArchiveScan (scene: Scene) =
    scene.ArchiveScan <- None
    Hud.hideRecovery ()
    Sound.receiver scene.Sound -1.0

let private setState (scene: Scene) (state: GameState) =
    scene.State <- state
    if state <> Playing && state <> Paused then scene.Camera.OpticsActive <- false
    Hud.showSchool (if state = Playing then scene.School.Step else FlightSchool.Off)
    scene.StateTime <- 0.0
    if state = Paused || state = Menu || state = GameOver then Sound.clearRadio scene.Sound
    if state <> Playing then clearArchiveScan scene
    let menu = document.getElementById "flight-menu"
    if state = Menu then menu.removeAttribute "hidden" else menu.setAttribute ("hidden", "")

let private sectorIndex (scene: Scene) = if scene.Trading then scene.Sector else max 0 (scene.Wave - 1)
let private currentSector (scene: Scene) = Universe.sectorByIndex (sectorIndex scene)

let private storage =
    { Read = fun key -> window.localStorage.getItem key
      Write = fun key json -> window.localStorage.setItem(key, json) }

let private readSlot slot = SaveStore.read storage slot GameSave.isValid

let private canSave (scene: Scene) =
    scene.State <> Menu && scene.State <> GameOver && scene.World.Player.Alive

let private saveNotice (scene: Scene) failed text =
    scene.Hud.SaveFailed <- failed
    scene.Hud.SaveText <- text
    scene.Saves.Status.textContent <- text
    if scene.State = Menu then (document.getElementById "menu-notice").textContent <- if failed then text else ""

let private refreshSaveSlots (scene: Scene) =
    let entries = SaveStore.slots |> Array.map (fun slot -> slot, readSlot slot)
    let latest =
        entries
        |> Array.choose (fun (_, entry) ->
            entry.Json |> Option.bind (fun json ->
                match GameSave.decode json with
                | Ok data -> Some(data, json)
                | Error _ -> None))
        |> Array.sortByDescending (fun (data, _) -> data.SavedAt)
        |> Array.tryHead
    scene.ResumeJson <- latest |> Option.map snd
    scene.Hud.HasSave <- latest.IsSome
    let brief = document.getElementById "menu-brief"
    let launch = document.getElementById "menu-launch-label"
    let record = document.getElementById "menu-record"
    match latest with
    | Some(data, _) ->
        brief.setAttribute ("hidden","")
        launch.textContent <- "Flug fortsetzen"
        record.textContent <- SaveUi.summary data
    | None ->
        brief.removeAttribute "hidden"
        launch.textContent <- "Startschiff wählen"
        record.textContent <- "Vier Startschiffe am Bergbau-Depot Kestrel"
    match entries |> Array.tryFind (fun (_, entry) -> entry.Error <> "") with
    | Some(_, entry) -> saveNotice scene true entry.Error
    | None -> ()
    if scene.Saves.Visible then SaveUi.render scene.Saves entries (canSave scene)

let private snapshot (scene: Scene) =
    let run =
        { Trading = scene.Trading; SectorId = (currentSector scene).Id; Wave = scene.Wave
          CourseTarget = scene.CourseTarget; CourseVia = scene.CourseVia
          Phase = (match scene.Phase with Intermission -> 0 | Fighting -> 1 | Salvaging -> 2)
          PhaseTimer = scene.PhaseTimer; Spawned = scene.Spawned; SpawnTimer = scene.SpawnTimer
          Patrol = Array.map int scene.Patrol; Score = scene.Score; Kills = scene.Kills
          Profit = scene.TradeProfit; Trips = scene.Trips; Docked = (scene.State = Refitting)
          Completed = (scene.State = Victory); Combo = scene.Hud.Combo; ComboTimer = scene.Hud.ComboTimer
          HeardContacts = Some (DockContact.heard scene.Contact)
          School = if scene.Trading then Some scene.School else None
          Blockade = if scene.Trading then Some scene.Blockade else None
          Sortie = if scene.Trading then Some scene.Sortie else None
          Fleet = if scene.Trading then Some scene.Fleet else None
          Recovery = None }
    let settings =
        { Camera = (if scene.Camera.Mode = Cockpit then 1 else 0); Muted = scene.Sound.Muted
          InvertPitch = scene.Input.InvertPitch; Sensitivity = scene.Input.MouseSensitivity }
    GameSave.capture run settings (SaveUi.now()) scene.World scene.Brains scene.Wallet scene.Cargo scene.Markets scene.Upgrades scene.Turret scene.Pickups

let private saveFlight (scene: Scene) slot =
    if canSave scene then
        let data = snapshot scene
        let json = GameSave.encode data
        let result = SaveStore.write storage slot json GameSave.isValid
        if result.Ok then
            scene.ResumeJson <- Some json
            scene.Hud.HasSave <- true
        if slot = "auto" then
            scene.SaveDirty <- false
            scene.FlightChanged <- not result.Ok
            scene.SaveTimer <- 0.0
            scene.SaveRetryDelay <- if result.Ok then 0.0 else 20.0
        if result.Ok then
            saveNotice scene false (if slot = "auto" then "Automatisch gesichert · F8" else SaveUi.label slot + " gesichert.")
            if slot <> "auto" then Sound.tradeConfirm scene.Sound
        else
            saveNotice scene true result.Error
            if scene.Saves.Visible then Sound.uiError scene.Sound

let private applySectorAtmosphere (scene: Scene) =
    let sector = sectorIndex scene
    scene.World.Customs <- if scene.Trading && sector = 1 && not scene.Blockade.Lifted then Some scene.Sortie else None
    if scene.World.SectorIndex <> sector then
        scene.Pilot.PulseTime <- 0.0
        clearArchiveScan scene
    scene.World.SectorIndex <- sector
    if not (PilotSkills.contains scene.Pilot.Visited sector) then
        scene.Hud.RewardText <- "+1 STACK-PUNKT · SEKTOR ENTDECKT"
        scene.Hud.RewardTime <- 4.0
    PilotSkills.visit scene.Pilot sector
    let atmo = (currentSector scene).Atmosphere
    Lights.setSun scene.Renderer.Lights atmo.SunDir.X atmo.SunDir.Y atmo.SunDir.Z atmo.SunColor.X atmo.SunColor.Y atmo.SunColor.Z
    Lights.setAmbient scene.Renderer.Lights atmo.Ambient.X atmo.Ambient.Y atmo.Ambient.Z
    SkyLight.apply scene.Renderer.Lights sector
    scene.Renderer.ClearColor.[0] <- float32 atmo.ClearColor.X
    scene.Renderer.ClearColor.[1] <- float32 atmo.ClearColor.Y
    scene.Renderer.ClearColor.[2] <- float32 atmo.ClearColor.Z
let private canAdvance (scene: Scene) = not scene.Trading && scene.Phase = Salvaging && scene.Wave > 0 && scene.Wave < Mission.stages.Length

let private encounterRoles (scene: Scene) = if scene.Trading then scene.Patrol else (Mission.stage scene.Wave).Enemies

let private dockBlocked (scene: Scene) =
    scene.School.Step = FlightSchool.Off && scene.Phase = Fighting &&
    (not scene.Trading || scene.World.Enemies |> Array.exists (fun enemy -> enemy.Alive && Vec3.distance enemy.Position (currentSector scene).Outpost.Position < 230.0))

/// Stationsanflug und Sprungtore freihalten; Asteroiden außerhalb der Zonen bleiben unverändert.
let private clearDockingArea (scene: Scene) =
    let sector = sectorIndex scene
    let cruiserPresent = scene.Trading && sector = Blockade.helios && not scene.Blockade.Lifted
    AsteroidField.clearDockingArea sector cruiserPresent scene.World.Asteroids

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

/// Verstärkungen kommen mit Abstand und vorwiegend aus dem vorderen Halbraum.
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
        let angle = float index * 2.39996 + rng.Range(-0.25, 0.25)
        let local = Vec3(sin angle * 0.9, rng.Range(-0.22, 0.22), -0.7 - abs (cos angle) * 0.5)
        let direction = Quat.rotateVec3 player.Rotation (Vec3.normalize local)
        position <- player.Position + direction * rng.Range(320.0, 440.0)
        placed <- isFreeSpace world.Asteroids position.X position.Y position.Z 30.0
        attempts <- attempts + 1

    let enemy = world.Enemies.[index]
    let role = (encounterRoles scene).[index]
    EnemyProfiles.configure enemy role (if scene.Trading then (currentSector scene).DangerLevel - 1 else scene.Wave - 1)
    scene.Brains.[index].Role <- role
    Ship.reset enemy position (Quat.lookRotation (player.Position - position) Vec3.unitY)
    AI.reset scene.Brains.[index] enemy player.Position

/// Neue Abschnitte haben feste gemischte Verbände und zeitversetzte Verstärkung.
let private startWave (scene: Scene) =
    scene.SaveDirty <- true
    scene.Wave <- scene.Wave + 1
    clearDockingArea scene
    applySectorAtmosphere scene
    let stage = Mission.stage scene.Wave
    scene.Spawned <- min 3 stage.Enemies.Length
    scene.SpawnTimer <- 5.0
    for i in 0 .. scene.Spawned - 1 do
        spawnEnemy scene i
    scene.EnemiesAlive <- scene.Spawned
    scene.Phase <- States.fighting
    scene.Hud.Objective <- stage.Objective
    scene.Hud.StageName <- stage.Name
    if stage.Enemies.[0] = EnemyRole.Commander then
        scene.SovereignWarningArmed <- true
        Sound.capitalHornAt scene.Sound scene.World.Enemies.[0].Position
        Sound.capitalComm scene.Sound 2
    else Sound.waveStart scene.Sound
    Hud.notify scene.Hud ("SEKTOR " + string scene.Wave + " / 6") stage.Hint 5.0

let private finishGame (scene: Scene) (won: bool) =
    Shop.hide scene.Shop
    let isBest = scene.Score > scene.Best
    if isBest then
        scene.Best <- scene.Score
        saveBest scene.Best
        Hud.setBest scene.Hud scene.Best
    scene.Hud.ResultVictory <- won
    Hud.setResult scene.Hud scene.Score scene.Wave scene.Kills scene.Best isBest
    if scene.Trading then
        scene.Hud.ResultDetailText <- string scene.Trips + " Reisen · " + string (int scene.TradeProfit) + " CR Handelsergebnis · " + string scene.Kills + " Abschüsse"
    if won then Sound.waveCleared scene.Sound else Sound.gameOver scene.Sound
    setState scene (if won then States.victory else States.gameOver)

/// Der Sektor ist erst ohne lebende UND ohne ausstehende Gegner abgeschlossen.
let private completeWave (scene: Scene) =
    scene.SaveDirty <- true
    let bonus = 250 * scene.Wave
    scene.Score <- scene.Score + bonus
    scene.World.Lasers.Count <- 0
    scene.Hud.BannerTime <- 0.0
    scene.Hud.Combo <- 1
    scene.Hud.ComboTimer <- 0.0
    scene.Accumulator <- 0.0
    if scene.Wave = Mission.stages.Length then
        scene.Score <- scene.Score + 2000
        finishGame scene true
    else
        Sound.waveCleared scene.Sound
        scene.World.Player.Hull <- min scene.World.Player.MaxHull (scene.World.Player.Hull + 25.0)
        scene.World.Player.Shield <- scene.World.Player.MaxShield
        scene.Phase <- States.salvaging
        scene.Hud.Objective <- "Erz bergen, Station anfliegen und Fracht verkaufen."
        Hud.notify scene.Hud "SEKTOR GESICHERT" "Freie Bergung · Stationsmarker folgen · Im Andockradius [B] drücken. +25 Hülle." 5.0

let private tradeTerms (scene: Scene) =
    { SignalLevel = PilotSkills.level scene.Pilot 0; FixerLevel = PilotSkills.level scene.Pilot 1 }

let private canTrain (scene: Scene) =
    scene.Trading && scene.Sector = 0 && scene.Phase = Salvaging && scene.Sortie.Role = 0

let private clearScreen (scene: Scene) =
    for enemy in scene.World.Enemies do enemy.Alive <- false
    scene.World.Lasers.Count <- 0
    scene.Patrol <- Array.empty
    scene.Spawned <- 0
    scene.EnemiesAlive <- 0
    scene.Phase <- States.salvaging
    scene.Hud.Target <- -1
    Turret.reset scene.Turret

let private endSchool (scene: Scene) =
    if FlightSchool.onRange scene.School.Step then
        clearScreen scene
        scene.Hud.ManualLock <- false
    scene.School.Step <- FlightSchool.Off
    scene.SaveDirty <- true
    Hud.showSchool FlightSchool.Off

let private refreshShop (scene: Scene) =
    let training = document.getElementById "school-replay"
    if canTrain scene then training.removeAttribute "hidden" else training.setAttribute ("hidden","")
    (document.getElementById "open-star-map").hidden <- not scene.Trading
    SkillUi.refresh scene.Skills scene.Pilot
    DockContact.refreshArchive scene.Contact scene.Pilot
    let terms = tradeTerms scene
    Shop.refresh scene.Shop terms scene.Wallet scene.Upgrades scene.Cargo scene.Markets.[sectorIndex scene] (currentSector scene).Outpost (canAdvance scene) scene.Fleet
    Shop.refreshRepair scene.Shop terms scene.Wallet scene.Cargo scene.World.Player (currentSector scene).Outpost
    let trained = PilotSkills.level scene.Pilot 2 >= 1
    ShipyardUi.refreshEquipment scene.Fleet scene.Upgrades trained
    ShipyardUi.refresh scene.Shop.Shipyard scene.Fleet scene.Cargo scene.Wallet.Credits (currentSector scene).Outpost.HasUpgrades scene.Trading scene.Upgrades.[0] trained
    FreightUi.refresh scene.Shop.FreightDesk scene.Cargo (sectorIndex scene) scene.Trading terms scene.Blockade.EscortPort scene.Wallet.Credits
    (document.getElementById "route-hint").hidden <- scene.Trading && scene.Cargo.Freight.IsSome
    let origin = sectorIndex scene
    let nextPort = StarMap.nextPort origin scene.CourseTarget scene.CourseVia
    Shop.refreshRoutes scene.Shop terms scene.Wallet scene.Cargo scene.Markets origin scene.Trading scene.TradeProfit scene.Blockade nextPort

let private openStarMap (scene: Scene) =
    if scene.Trading && scene.World.Player.Alive && not scene.Saves.Visible && not (StarMap.visible ())
       && (scene.State = Playing || scene.State = Paused || scene.State = Refitting) then
        let docked = scene.State = Refitting
        if docked then DockContact.leave scene.Contact
        Sound.clearRadio scene.Sound
        scene.Input.UiMode <- true
        Input.reset scene.Input
        document.exitPointerLock()
        let setCourse target via =
            scene.CourseTarget <- target
            scene.CourseVia <- via
            scene.SaveDirty <- true
        let close () =
            scene.Input.UiMode <- docked
            Input.reset scene.Input
            scene.Input.FocusLost <- false
            if docked then
                refreshShop scene
                DockContact.dock scene.Contact (currentSector scene).Outpost.Id scene.Pilot
        StarMap.show scene.Sector scene.Cargo scene.Markets (tradeTerms scene) scene.Blockade scene.CourseTarget scene.CourseVia setCourse close
    BlockadeUi.refresh scene.Yard scene.Blockade scene.Trading (sectorIndex scene) scene.Wallet scene.Cargo scene.Markets

let private showDock (scene: Scene) arrived =
    Missiles.reload scene.Missiles
    let graduated = scene.School.Step = FlightSchool.Dock
    if scene.School.Step <> FlightSchool.Off then endSchool scene
    let sector = currentSector scene
    scene.Input.UiMode <- true
    Shop.show scene.Shop sector.Outpost
    DockContact.dock scene.Contact sector.Outpost.Id scene.Pilot
    refreshShop scene
    let next = Universe.sectorByIndex scene.Wave
    let route = document.getElementById "route-hint"
    route.textContent <-
        if scene.Trading then
            if scene.Trips = 0 then "Helios kauft Kryo-Erz. Für einen Transport ohne Wareneinkauf wählst du unten an der Frachtbörse einen Auftrag."
            else "Vergleiche unter Routen & Kurse die Nachbarstationen. Der Warenwert berücksichtigt deren freie Lager; Geleitschutz geht extra vom Guthaben ab."
        elif canAdvance scene then
            "Nächster Halt: " + next.Outpost.Name + " · Kryo-Erz-Ankauf dort aktuell " + string (TradeSystem.pilotSellPrice (tradeTerms scene) scene.Markets.[scene.Wave] scene.Cargo CommodityId.CryoOre) + " CR. Fracht mitnehmen oder hier verkaufen."
        else "Blaue Kristalle laden 1 t, goldene 2 t Kryo-Erz. Erst beim Verkauf erhältst du Credits."
    scene.World.Player.Velocity <- Vec3.zero
    scene.World.Player.Controls.Throttle <- 0.0
    scene.Input.StickX <- 0.0
    scene.Input.StickY <- 0.0
    scene.Input.MouseFire <- false
    scene.Accumulator <- 0.0
    setState scene States.refitting
    if graduated then scene.Shop.Notice.textContent <- "Flugschule abgeschlossen. Kestrels Handelsmarkt und Frachtbörse stehen dir offen."
    if arrived then
        Sound.clearRadio scene.Sound
        match sector.Outpost.Kind with
        | OrbitalYard ->
            Sound.shipyardEnter scene.Sound
            Sound.capitalComm scene.Sound 1
        | FleetDock -> Sound.shipyardEnter scene.Sound
        | _ -> Sound.dockOpen scene.Sound

let private learnSkill (scene: Scene) branch rank =
    if PilotSkills.learn scene.Pilot branch rank then
        if branch = 1 && rank = 2 then scene.Cargo.Capacity <- scene.Cargo.Capacity + 8
        Mission.applyLoadout scene.World.Player scene.Upgrades scene.Pilot scene.Fleet
        scene.SaveDirty <- true
        Sound.uiConfirm scene.Sound
        refreshShop scene
        scene.Shop.Notice.textContent <- "Fähigkeit gelernt. Der Vorteil ist jetzt aktiv."

let private updateArchiveScan (scene: Scene) dt =
    let sector = sectorIndex scene
    let kind = (currentSector scene).Outpost.Kind
    let ship = scene.World.Player
    if not ship.Alive then
        if scene.ArchiveScan.IsSome then clearArchiveScan scene
    elif Input.wasPressed scene.Input GameAction.ScanRelic then
        let source = fst scene.RelicBatches.[sector]
        let radius = RelicArchive.range scene.Pilot
        let echo = Recovery.scan scene.Pilot sector ship.Position source
        scene.ArchiveScan <- Some { Echo = echo; Elapsed = 0.0 }
        Sound.receiver scene.Sound -1.0
        Sound.scannerPing scene.Sound
        match echo with
        | NoEcho ->
            Hud.showRecovery "Kein Archivecho" (sprintf "Scanradius %d m" (int radius)) "Versuche es an einer anderen Stelle im Stationsanflug." 0.0
        | KnownRecording distance | NewRecording distance ->
            let status =
                match echo with
                | NewRecording _ ->
                    scene.SaveDirty <- true
                    Sound.printerTick scene.Sound
                    "+1 Stack-Punkt · Bericht am Dock"
                | _ -> "Bereits im Fundarchiv"
            Hud.showRecovery (fst RelicArchive.entries.[sector]) (sprintf "Echo auf %d m · %s" (int (round distance)) status)
                (Recovery.transmission kind) (1.0 - distance / radius)
    else
        match scene.ArchiveScan with
        | Some scan ->
            scan.Elapsed <- scan.Elapsed + dt
            if scan.Elapsed >= Recovery.readoutDuration then clearArchiveScan scene
            elif kind = FrontierBazaar && scan.Echo <> NoEcho then Sound.receiver scene.Sound 1.0
        | None -> ()

let private openShop (scene: Scene) =
    let sector = currentSector scene
    match Universe.evaluateDocking sector.Outpost scene.World.Player.Position (dockBlocked scene) with
    | ReadyToDock _ when scene.World.Player.Alive ->
        scene.SaveDirty <- true
        showDock scene true
    | BlockedByCombat _ ->
        Sound.dockJam scene.Sound
        Hud.notify scene.Hud "DOCK GESPERRT" (if scene.Trading then "Piraten bedrohen die Station. Locke sie weg oder schalte sie aus." else "Sichere zuerst diesen Sektor. Abschüsse bringen je 100 CR.") 2.5
    | _ ->
        Sound.uiError scene.Sound
        Hud.notify scene.Hud "STATION ANFLIEGEN" (sector.Outpost.Name + " · Zum Andocken näher als " + string (int sector.Outpost.DockRadius) + " m heranfliegen.") 2.5

let private buyEquipment (scene: Scene) (item: int) =
    if (currentSector scene).Outpost.HasUpgrades && Economy.buyFor (tradeTerms scene) scene.Wallet scene.World.Player scene.Upgrades item then
        Mission.applyLoadout scene.World.Player scene.Upgrades scene.Pilot scene.Fleet
        scene.SaveDirty <- true
        scene.Shop.Notice.textContent <- if item = 0 then "Auto-Turret installiert. Er erfasst selbstständig Gegner in 280 m Reichweite." else "Verbesserung eingebaut. Hülle repariert und Schild geladen."
        Sound.tradeConfirm scene.Sound
        if item = 0 then Sound.autoTurretOnline scene.Sound
    else
        Sound.uiError scene.Sound
        scene.Shop.Notice.textContent <- "Nicht genug Credits oder bereits vollständig ausgebaut."
    refreshShop scene

let private repairHull (scene: Scene) (action: int) =
    let outpost = (currentSector scene).Outpost
    let ok, message =
        if not outpost.HasRepair then false, "Diese Station bietet keine Hüllenreparatur an."
        elif action = 1 then
            if Economy.repairWithCredits (tradeTerms scene) scene.Wallet scene.World.Player then
                true, "Hülle vollständig instand gesetzt und Schild geladen."
            else false, "Reparatur nicht möglich oder nicht genug Credits."
        elif action = 2 then
            if Economy.repairWithScrap scene.Cargo scene.World.Player then
                true, "1 t Titanschrott verbaut: +35 Hülle wiederhergestellt und Schild geladen."
            else false, "Kein Schaden oder kein Titanschrott im Frachtraum."
        else false, ""
    if ok then
        scene.SaveDirty <- true
        Sound.refit scene.Sound
    else Sound.uiError scene.Sound
    if message <> "" then scene.Shop.Notice.textContent <- message
    refreshShop scene

let private trade (scene: Scene) (action: int) =
    let market = scene.Markets.[sectorIndex scene]
    let terms = tradeTerms scene
    let before = TradeSystem.copyCargo scene.Cargo
    let result =
        if action < 6 then TradeSystem.buyGood market scene.Cargo scene.Wallet.Credits (enum<CommodityId> action) 1
        elif action < 12 then TradeSystem.sellGoodFor terms market scene.Cargo (enum<CommodityId> (action-6)) 1
        elif action = 12 then TradeSystem.sellAllFor terms market scene.Cargo
        elif action = 13 then
            if (currentSector scene).Outpost.HasUpgrades then TradeSystem.upgradeHullCargo terms ShipCatalog.hulls.[scene.Fleet.Active].Hold scene.Cargo scene.Wallet.Credits
            else { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Diese Station bietet keinen Ausbau an." }
        elif action < 20 then TradeSystem.buyGood market scene.Cargo scene.Wallet.Credits (enum<CommodityId> (action-14)) 5
        elif action < 26 then TradeSystem.sellGoodFor terms market scene.Cargo (enum<CommodityId> (action-20)) 5
        else { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Unbekannte Handelsaktion." }
    if result.Ok then
        scene.SaveDirty <- true
        if result.CreditsDelta > 0 then
            Economy.earn scene.Wallet result.CreditsDelta true
            let profit = TradeSystem.saleProfit result.CreditsDelta before scene.Cargo
            scene.TradeProfit <- scene.TradeProfit + profit
            if profit > 0.0 then Sound.tradeProfit scene.Sound
        else scene.Wallet.Credits <- scene.Wallet.Credits + result.CreditsDelta
        Sound.tradeConfirm scene.Sound
    else Sound.uiError scene.Sound
    scene.Shop.Notice.textContent <- result.Message
    refreshShop scene

let private refitShip (scene: Scene) order =
    if scene.Trading && scene.State = Refitting then
        match ShipCatalog.equip scene.Fleet scene.Cargo scene.Wallet.Credits (currentSector scene).Outpost.HasUpgrades order with
        | Error message ->
            scene.Shop.Notice.textContent <- message
            Sound.uiError scene.Sound
        | Ok cost ->
            scene.Wallet.Credits <- scene.Wallet.Credits - cost
            Mission.applyLoadout scene.World.Player scene.Upgrades scene.Pilot scene.Fleet
            let hull = ShipCatalog.hulls.[scene.Fleet.Active]
            let kit = ShipCatalog.kits.[scene.Fleet.Kit]
            scene.Shop.Notice.textContent <- sprintf "%s flugbereit · %s · %d t Frachtraum." hull.Name kit.Name scene.Cargo.Capacity
            scene.SaveDirty <- true
            Sound.refit scene.Sound
        refreshShop scene

let private handleFreight (scene: Scene) action =
    if scene.Trading && scene.State = Refitting then
        match Freight.apply scene.Cargo scene.Sector action with
        | None -> Sound.uiError scene.Sound
        | Some payment ->
            scene.SaveDirty <- true
            scene.Shop.Notice.textContent <-
                match action with
                | AcceptLoad _ -> "Fracht verladen. Die Lieferroute ist unter Routen & Kurse markiert."
                | ReturnLoad -> "Die Station hat die versiegelte Ladung zurückgenommen."
                | DeliverLoad ->
                    Economy.earn scene.Wallet payment false
                    scene.TradeProfit <- scene.TradeProfit + float payment
                    sprintf "Lieferung quittiert. %d CR Honorar gutgeschrieben." payment
            Sound.tradeConfirm scene.Sound
            refreshShop scene

let private departShop (scene: Scene) =
    DockContact.leave scene.Contact
    Sound.clearRadio scene.Sound
    Sound.undock scene.Sound
    scene.SaveDirty <- true
    scene.Input.UiMode <- false
    Shop.hide scene.Shop
    scene.Input.StickX <- 0.0
    scene.Input.StickY <- 0.0
    scene.Hud.Target <- -1
    scene.Hud.ManualLock <- false
    Hud.notify scene.Hud "ABGEDOCKT" "Schub mit ↑ erhöhen · Weiter bergen oder zum Stationsmarkt zurückkehren." 3.0
    setState scene States.playing

let private advanceSector (scene: Scene) =
    if canAdvance scene then
        departShop scene
        Sound.jumpGate scene.Sound
        scene.Phase <- States.intermission
        scene.PhaseTimer <- IntermissionTime
        let stage = Mission.stage (scene.Wave + 1)
        scene.Hud.StageName <- stage.Name
        scene.Hud.Objective <- stage.Objective
        Hud.notify scene.Hud "ROUTE FREIGEGEBEN" "Nächster Sektor in 8 Sekunden · Fracht und Ausrüstung bleiben an Bord." 4.0

/// Ein Sprung versetzt das Schiff zum Anflug, heilt aber weder Hülle noch Schild.
let private arrive (scene: Scene) distance =
    let ship = scene.World.Player
    let hull, shield, delay = ship.Hull, ship.Shield, ship.ShieldDelay
    let point = (currentSector scene).Outpost.Position + Vec3(0.0,0.0,distance)
    Ship.reset ship point Quat.identity
    ship.Hull <- hull
    ship.Shield <- shield
    ship.ShieldDelay <- delay
    scene.Input.StickX <- 0.0
    scene.Input.StickY <- 0.0
    Camera.snap scene.Camera point Quat.identity
    clearDockingArea scene
    applySectorAtmosphere scene

let private beginSchool scene =
    clearScreen scene
    scene.School.Step <- FlightSchool.Accelerate
    arrive scene 310.0
    // The targets lie against open sky beside the dock, away from its bright approach ring.
    let ship = scene.World.Player
    let heading = Quat.lookRotation (Vec3(0.25,0.07,-1.0)) Vec3.up
    ship.Rotation <- heading
    ship.PrevRotation <- heading
    Camera.snap scene.Camera ship.Position heading
    scene.Hud.BannerTime <- 0.0
    scene.SaveDirty <- true
    Hud.showSchool scene.School.Step

let private launchTrainingDrone (scene: Scene) =
    let player = scene.World.Player
    let port = (currentSector scene).Outpost.Position
    let lateral = if scene.School.Step = FlightSchool.MovingTarget then 4.0 else 0.0
    let ahead distance = player.Position + Quat.rotateVec3 player.Rotation (Vec3(lateral,0.0,-distance))
    let position =
        [|78.0; 52.0; 105.0; 34.0|] |> Array.map ahead
        |> Array.tryFind (fun p -> Vec3.distance p port > 65.0 && isFreeSpace scene.World.Asteroids p.X p.Y p.Z 10.0)
        |> Option.defaultValue (port + Vec3(0.0,2.0,165.0))
    let drone,brain = scene.World.Enemies.[0],scene.Brains.[0]
    EnemyProfiles.configure drone EnemyRole.Drone 0
    Ship.reset drone position (Quat.lookRotation (player.Position-position) Vec3.up)
    brain.Role <- EnemyRole.Drone
    AI.reset brain drone position
    brain.State <- AiState.Patrol
    FlightSchool.guideTarget scene.School.Step scene.World.Time drone
    scene.Patrol <- [|EnemyRole.Drone|]
    scene.Spawned <- 1
    scene.EnemiesAlive <- 1
    scene.Phase <- States.fighting
    scene.Hud.Target <- 0

let private advanceSchool (scene: Scene) =
    let ship = scene.World.Player
    let next = FlightSchool.helmStep scene.School.Step ship.Controls.Throttle ship.Controls.Brake (Vec3.length ship.Velocity)
    if next <> scene.School.Step then
        scene.School.Step <- next
        if next = FlightSchool.FirstTarget then launchTrainingDrone scene
        scene.SaveDirty <- true
        Hud.showSchool next
        if next = FlightSchool.Dock then Sound.tradeConfirm scene.Sound
        else Sound.uiConfirm scene.Sound

let private configureBomber (ship: Ship) =
    ship.ShieldRechargeDelay <- Combat.ShieldRegenDelay
    ship.CruiseSpeed <- 46.0
    ship.MaxYawRate <- 0.87
    ship.MaxPitchRate <- 0.94
    ship.LaserDamage <- 11.0
    ship.MaxHull <- 100.0
    ship.MaxShield <- 100.0

let private launchSortie (scene: Scene) role =
    let ship = scene.World.Player
    let flight = scene.Sortie
    CapitalFlight.beginSortie flight role ship.Hull ship.Shield
    departShop scene
    clearScreen scene
    if role = CapitalFlight.Bomber then configureBomber ship
    let point = CapitalFlight.position + Vec3(24.0,16.0,365.0)
    let heading = Quat.lookRotation (CapitalFlight.position - point) Vec3.up
    Ship.reset ship point heading
    if role = CapitalFlight.Escort then
        ship.Hull <- flight.ParkedHull
        ship.Shield <- flight.ParkedShield
    Input.reset scene.Input
    Camera.snap scene.Camera point heading
    clearDockingArea scene
    scene.Patrol <- [| EnemyRole.Drone; EnemyRole.Scout |]
    scene.Spawned <- scene.Patrol.Length
    for i in 0..scene.Spawned-1 do
        if flight.Defeated &&& (1 <<< i) = 0 then
            let enemy, brain = scene.World.Enemies.[i], scene.Brains.[i]
            let p = CapitalFlight.position + (if i = 0 then Vec3(-61.0,39.0,202.0) else Vec3(89.0,2.0,148.0))
            let role = scene.Patrol.[i]
            EnemyProfiles.configure enemy role ((currentSector scene).DangerLevel-1)
            Ship.reset enemy p (Quat.lookRotation (point-p) Vec3.up)
            AI.reset brain enemy point
            brain.Role <- role
            scene.EnemiesAlive <- scene.EnemiesAlive + 1
    scene.Phase <- if scene.EnemiesAlive = 0 then States.salvaging else States.fighting
    scene.SaveDirty <- true
    scene.Hud.BannerTime <- 0.0
    if role = CapitalFlight.Escort then
        scene.Hud.RewardText <- "Kepler Zwei · »Halt uns die beiden Jäger vom Hals.«"
        scene.Hud.RewardTime <- 3.5

let private recoverSortie (scene: Scene) message =
    let ship, flight = scene.World.Player, scene.Sortie
    let hull, shield =
        if flight.Role = CapitalFlight.Bomber then flight.ParkedHull, flight.ParkedShield
        else max 1.0 ship.Hull, ship.Shield
    CapitalFlight.standDown flight
    clearScreen scene
    ship.MaxHull <- ShipCatalog.hullStrength scene.Upgrades.[2]
    ship.MaxShield <- ShipCatalog.shieldCapacity scene.Upgrades.[1]
    Mission.applyLoadout ship scene.Upgrades scene.Pilot scene.Fleet
    ship.Hull <- hull
    ship.Shield <- shield
    arrive scene 72.0
    scene.DeathTimer <- 0.0
    showDock scene true
    scene.Shop.Notice.textContent <- message
    scene.SaveDirty <- true

let private yardAction (scene: Scene) action =
    if scene.State = Refitting && scene.Trading then
        match action with
        | FlyBomber | FlyEscort ->
            if Blockade.canSortie scene.Blockade scene.Sector && scene.Sortie.Role = 0 then
                launchSortie scene (if action = FlyBomber then CapitalFlight.Bomber else CapitalFlight.Escort)
        | _ ->
            if Blockade.apply scene.Blockade scene.Sector scene.Wallet scene.Cargo scene.Markets action then
                scene.SaveDirty <- true
                match action with
                | Commission _ -> Sound.shipyardCrane scene.Sound
                | _ -> Sound.tradeConfirm scene.Sound
                refreshShop scene

let private updateSortie (scene: Scene) dt =
    let f,world = scene.Sortie,scene.World
    let ship = world.Player
    if scene.Trading && scene.Sector = 1 && (scene.Blockade.Lifted || scene.Blockade.Permit = 2) &&
       Vec3.distance ship.Position (currentSector scene).Outpost.Position < 235.0 then
        f.Landing <- min CapitalFlight.landingTime (f.Landing + dt)
    if f.Role <> 0 && (not ship.Alive || f.WingHull <= 0.0) then
        recoverSortie scene "Der Flugdienst hat den Einsatz abgebrochen. Die Werft stellt den Bomber wieder bereit."
    elif f.Role <> 0 && not scene.Blockade.Lifted then
        let previousLock, ammo, hits = f.Lock,f.Ammo,f.Hits
        let fire = Input.wasPressed scene.Input GameAction.LaunchTorpedo
        CapitalFlight.advance f ship.Position (Quat.forward ship.Rotation) (scene.EnemiesAlive = 0) fire dt
        if previousLock < CapitalFlight.lockTime && f.Lock >= CapitalFlight.lockTime then Sound.targetLock scene.Sound
        if f.Ammo < ammo then
            let attitude = if f.Role = CapitalFlight.Escort then Quat.lookRotation (CapitalFlight.position-f.Launch) Vec3.up else ship.Rotation
            f.Launch <- f.Launch + Quat.rotateVec3 attitude ShipHardpoints.bomberRails.[f.Ammo]
            Sound.torpedoLaunch scene.Sound
            scene.SaveDirty <- true
        if f.Hits > hits then
            let p = CapitalFlight.position
            Effects.shipExplosion world p.X p.Y (p.Z + 14.0 * CapitalFlight.hullScale) 0.0 0.0 0.0 (10.0 * CapitalFlight.hullScale)
            CombatLights.explosion scene.CombatLights world.Time (Vec3(p.X, p.Y, p.Z + 14.0 * CapitalFlight.hullScale)) (10.0 * CapitalFlight.hullScale)
            let distance = Vec3.distance ship.Position p
            scene.SaveDirty <- true
            if f.Hits = 2 && Blockade.lift scene.Blockade scene.Markets then
                Sound.capitalExplosionAt scene.Sound p
                // Grossexplosion ueber die ganze Rumpflaenge (Bug +X), in Stufen ueber rund 4 s
                let hs = CapitalFlight.hullScale
                Effects.cruiserBreakup world p Vec3.unitX (-78.0 * hs) (94.0 * hs) (17.0 * hs)
                CombatLights.blast scene.CombatLights world.Time (p + Vec3.unitX * (8.0 * hs)) Vec3.unitX (86.0 * hs) (25.5 * hs) CombatVfx.TierCruiser
                world.Customs <- None
                clearScreen scene
                f.Landing <- 0.0
                Hud.notify scene.Hud "HELIOS · DURCHFLUG FREI" "»Frachter Sechs an Hafenmeister. Wir kommen rein.«  B bringt dich zum Dock." 6.0
            else Sound.torpedoImpact scene.Sound

let private completeTravel (scene: Scene) terms (plan: TravelPlan) exitGate =
    Blockade.onTravel scene.Blockade scene.Markets scene.Sector plan.Destination |> ignore
    let heliosBlockaded = scene.Blockade.Started && not scene.Blockade.Lifted
    TradeSystem.driftMarkets scene.Markets heliosBlockaded
    Missiles.reload scene.Missiles
    scene.Wallet.Credits <- scene.Wallet.Credits - plan.Fee
    scene.TradeProfit <- scene.TradeProfit - float plan.Fee
    scene.Sector <- plan.Destination
    if scene.CourseTarget = Some (currentSector scene).Id then scene.CourseTarget <- None
    if not (StarMap.validStop scene.Sector scene.CourseTarget scene.CourseVia) then scene.CourseVia <- None
    scene.Trips <- scene.Trips + 1
    if scene.Trips % 2 = 0 then
        let rng = scene.World.Rng
        let targetSector = int (rng.NextFloat() * float scene.Markets.Length) % scene.Markets.Length
        let goodIndex = int (rng.NextFloat() * float TradeSystem.commodities.Length) % TradeSystem.commodities.Length
        let shortage = rng.NextFloat() < 0.55
        match TradeSystem.triggerMarketEvent scene.Markets heliosBlockaded targetSector goodIndex shortage with
        | Some (title, detail) -> Hud.notify scene.Hud title detail 5.5
        | None -> ()
    clearScreen scene
    scene.Patrol <- plan.Patrol
    scene.World.Particles.Count <- 0
    GpuParticles.clear scene.GpuParticles
    scene.World.Events.Clear()
    scene.Pickups.Clear()
    AsteroidField.generate scene.World.Asteroids scene.World.Rng InitialAsteroids
    match exitGate with
    | None ->
        departShop scene
        Sound.jumpGate scene.Sound
        arrive scene 650.0
    | Some gate ->
        Sound.jumpGate scene.Sound
        let ship = scene.World.Player
        let cruiserPresent = scene.Sector = Blockade.helios && not scene.Blockade.Lifted
        let point, direction = JumpFlight.arrival (currentSector scene) gate ship.Radius cruiserPresent
        let rotation = Quat.lookRotation direction Vec3.up
        ship.Position <- point
        ship.PrevPosition <- point
        ship.Rotation <- rotation
        ship.PrevRotation <- rotation
        ship.Velocity <- direction * Vec3.length ship.Velocity
        ship.PitchRate <- 0.0
        ship.YawRate <- 0.0
        ship.RollRate <- 0.0
        scene.Input.StickX <- 0.0
        scene.Input.StickY <- 0.0
        scene.Hud.ManualLock <- false
        scene.MatchSpeed <- false
        scene.JumpNotifiedSector <- scene.Sector
        scene.JumpTransitUntil <- scene.Time + JumpFlight.transitDuration
        scene.SaveDirty <- true
        Camera.snap scene.Camera point rotation
        clearDockingArea scene
        applySectorAtmosphere scene
    if scene.Sector = Blockade.kepler then Sound.capitalComm scene.Sound 0
    elif scene.World.Customs.IsSome then
        Sound.capitalHornAt scene.Sound CapitalFlight.position
    scene.Phase <- if plan.Patrol.Length = 0 then States.salvaging else States.intermission
    scene.PhaseTimer <- TradeRoutes.patrolDelay terms
    let message =
        if plan.Patrol.Length > 0 then sprintf "Piraten in %d Sekunden. Zur Station durchbrechen oder kämpfen." (int scene.PhaseTimer)
        elif exitGate.IsSome then "Sprung abgeschlossen · Station anfliegen · [X] stoppt · [B] andocken"
        else "Gesicherte Überfahrt · Station anfliegen · [X] stoppt · [B] andocken"
    Hud.notify scene.Hud (currentSector scene).Name message 5.0

let private travel (scene: Scene) action =
    if scene.Trading && scene.State = Refitting then
        let target, escorted = action / 2, action % 2 = 1
        let terms = tradeTerms scene
        match TradeRoutes.tryPlanWithEscort terms scene.Blockade.EscortPort scene.Sector target escorted scene.Wallet.Credits with
        | None ->
            Sound.uiError scene.Sound
            scene.Shop.Notice.textContent <- "Route nicht erreichbar oder Guthaben für Geleitschutz zu klein."
            refreshShop scene
        | Some plan -> completeTravel scene terms plan None

/// Beginnt ein neues Spiel (aus dem Menue oder nach Game Over).
let private startGame (scene: Scene) (trading: bool) startHull =
    scene.School <- FlightSchool.create ()
    scene.Fleet <- ShipCatalog.startFleet startHull
    let hull = ShipCatalog.hulls.[startHull]
    Sound.clearRadio scene.Sound
    scene.SovereignWarningArmed <- false
    scene.Blockade <- Blockade.create ()
    scene.Sortie <- CapitalFlight.create ()
    scene.Yard.Request <- None
    scene.World.Pilot <- PilotSkills.create ()
    clearArchiveScan scene
    scene.Skills.Request <- None
    DockContact.restore scene.Contact 0
    scene.SaveDirty <- true
    scene.SaveRetryDelay <- 0.0
    Input.reset scene.Input
    let world = scene.World
    Sound.unlock scene.Sound
    Mission.resetPlayer world.Player
    Economy.reset scene.Wallet
    scene.Trading <- trading
    scene.Sector <- 0
    scene.CourseTarget <- None
    scene.CourseVia <- None
    scene.TradeProfit <- 0.0
    scene.Trips <- 0
    scene.Patrol <- Array.empty
    scene.Wallet.Credits <- if trading then hull.StartingCredits else 0
    TradeSystem.resetCargo scene.Cargo hull.Hold
    for i in 0 .. scene.Markets.Length - 1 do
        scene.Markets.[i] <- TradeSystem.createMarket Universe.sectors.[i]
    if trading then Blockade.beginRun scene.Blockade scene.Markets |> ignore
    Turret.reset scene.Turret
    scene.Pickups.Clear()
    Shop.hide scene.Shop
    Array.fill scene.Upgrades 0 scene.Upgrades.Length 0
    scene.Input.UiMode <- false
    World.reset world InitialAsteroids
    GpuParticles.clear scene.GpuParticles
    Mission.applyLoadout world.Player scene.Upgrades world.Pilot scene.Fleet
    Camera.snap scene.Camera world.Player.Position world.Player.Rotation
    scene.Score <- 0
    scene.Kills <- 0
    scene.Wave <- 0
    clearDockingArea scene
    applySectorAtmosphere scene
    scene.Phase <- if trading then States.salvaging else States.intermission
    scene.PhaseTimer <- 12.0
    scene.Spawned <- 0
    scene.SpawnTimer <- 0.0
    scene.EnemiesAlive <- 0
    scene.DeathTimer <- 0.0
    scene.Shake <- 0.0
    scene.Accumulator <- 0.0
    scene.PrevBoostLocked <- false
    scene.PrevOverdrive <- 0.0
    scene.PrevShieldUp <- true
    scene.PrevShieldDelay <- 0.0
    scene.UpgradeNotified <- false
    scene.JumpNotifiedSector <- -1
    scene.JumpTransitUntil <- 0.0
    scene.GateContactUntil <- 0.0
    scene.Hud.DamageFlash <- 0.0
    scene.Hud.Target <- -1
    scene.Hud.ManualLock <- false
    scene.Hud.Combo <- 1
    scene.Hud.ComboTimer <- 0.0
    scene.Hud.ResultVictory <- false
    scene.Hud.RewardTime <- 0.0
    scene.Hud.StageName <- (Mission.stage 1).Name
    scene.Hud.Objective <- (Mission.stage 1).Objective
    if trading then beginSchool scene
    else Hud.notify scene.Hud "OPERATION MORGENLICHT" "Kampfmission · Erster Gegneranflug in 12 Sekunden · X stoppt dein Schiff." 6.0
    setState scene States.playing

/// Spielende: Bestwert aktualisieren und Ergebnis fuer den Bildschirm hinterlegen.
let private endGame (scene: Scene) =
    finishGame scene false

// Anzeige-Einstellungen gehoeren zum Geraet, nicht zum Spielstand.
[<Literal>]
let private ZoomKey = "space2601.camera-zoom"

[<Emit("(() => { try { const v = parseFloat(localStorage.getItem($0)); return Number.isFinite(v) ? v : $1; } catch { return $1; } })()")>]
let private loadSetting (key: string) (fallback: float) : float = jsNative

[<Emit("(() => { try { localStorage.setItem($0, String($1)); } catch { } })()")>]
let private saveSetting (key: string) (value: float) : unit = jsNative

/// Ein voller Boost-Vorrat reicht fuer so viele Sekunden Dauerboost.
[<Literal>]
let private BoostDuration = 8.0

/// Sekunden, bis sich ein leerer Vorrat vollstaendig erholt hat.
[<Literal>]
let private BoostRecovery = 6.0

/// Uebertraegt die Eingabe in die Steuerbefehle des Spielerschiffs.
let private applyPlayerInput (scene: Scene) (input: InputState) (ship: Ship) (dt: float) =
    let c = ship.Controls
    let gain = Camera.steeringGain scene.Camera
    c.Pitch <- Input.pitch input * gain
    c.Yaw <- Input.yaw input * gain
    c.Roll <- Input.roll input
    c.Throttle <- Flight.adjustThrottle c.Throttle (Input.throttleChange input) dt
    c.Brake <- Input.isDown input GameAction.Brake

    // Boost zehrt vom Vorrat. Ein leergeflogener Vorrat sperrt, bis 30 % zurueck sind,
    // damit der Antrieb an der Grenze nicht im Takt der Erholung stottert.
    if scene.BoostLocked && scene.BoostReserve >= 0.3 then
        scene.BoostLocked <- false

    let boosting = Input.isDown input GameAction.Boost && not c.Brake && not scene.BoostLocked

    if boosting then
        scene.BoostRest <- 0.0
        scene.BoostReserve <- scene.BoostReserve - dt / BoostDuration

        if scene.BoostReserve <= 0.0 then
            scene.BoostReserve <- 0.0
            scene.BoostLocked <- true
    else
        scene.BoostRest <- scene.BoostRest + dt

        if scene.BoostRest > 0.8 then
            scene.BoostReserve <- System.Math.Min(1.0, scene.BoostReserve + dt / BoostRecovery)

    c.Boost <- boosting
    c.Fire <- Input.fire input
    Input.centerStick input dt

/// Wertet die Ereignisse der Simulation aus: Punkte, Treffer-Feedback, Sound, Spielende.
let private processEvents (scene: Scene) =
    let events = scene.World.Events
    let player = scene.World.Player

    let playing =
        match scene.State with
        | Playing -> true
        | _ -> false

    for i in 0 .. events.Count - 1 do
        CombatLights.observe scene.CombatLights scene.World events.[i]
        match events.[i] with
        | AsteroidDestroyed(radius, team, position) ->
            if playing then
                Sound.explosionAt scene.Sound (System.Math.Min(1.2, radius * 0.06)) position

                if team = Team.Player then
                    scene.Score <- scene.Score + (10 + int radius) * scene.Hud.Combo
        | MineralReleased(position, kind, count) ->
            if playing then
                Salvage.spawn scene.Pickups position kind count
                Sound.mineralCrack scene.Sound position
                scene.Hud.RewardText <- "MINERALIEN FREIGELEGT · HINFLIEGEN & EINSAMMELN"
                scene.Hud.RewardTime <- 3.0
        | StackRescue ->
            scene.SaveDirty <- true
            Hud.notify scene.Hud "NOTFALL-SYNC" "Hülle auf 30 %, Schild voll. Rettung für diesen Sektor verbraucht." 5.0
            Sound.shieldDown scene.Sound
        | ShipDestroyed(Team.Enemy, position, 0) when playing && FlightSchool.onRange scene.School.Step ->
            let bounty = FlightSchool.targetDown scene.School
            if bounty > 0 then Economy.earn scene.Wallet bounty false
            scene.Hud.RewardText <- if bounty > 0 then "ZIEL BESTÄTIGT · +100 CR ÜBUNGSPRÄMIE" else "ZIEL BESTÄTIGT"
            scene.Hud.RewardTime <- 2.0
            Hud.confirmKill scene.Hud
            Sound.explosionAt scene.Sound 1.2 position
            if scene.School.Step = FlightSchool.MovingTarget then launchTrainingDrone scene
            else clearScreen scene
            Hud.showSchool scene.School.Step
            scene.SaveDirty <- true
        | ShipDestroyed(victim, position, slot) ->
            let distance = Vec3.distance position player.Position

            if playing then
                if victim = Team.Enemy && slot >= 0 && scene.Brains.[slot].Role = EnemyRole.Commander then
                    scene.SovereignWarningArmed <- false
                    Sound.clearRadio scene.Sound
                    Sound.capitalExplosionAt scene.Sound position
                else Sound.explosionAt scene.Sound 1.5 position

            if victim = Team.Enemy then
                if playing then
                    if scene.Sortie.Role <> 0 && slot >= 0 && slot < 2 then
                        scene.Sortie.Defeated <- scene.Sortie.Defeated ||| (1 <<< slot)
                        scene.SaveDirty <- true
                    scene.Kills <- scene.Kills + 1
                    Economy.earn scene.Wallet 100 false
                    let mutable salvageHint = ""
                    if slot >= 0 then
                        let role = scene.Brains.[slot].Role
                        let dropped = Salvage.spawnWreck scene.Pickups position (int role)
                        if dropped > 0 then
                            salvageHint <-
                                match role with
                                | EnemyRole.Gunship | EnemyRole.Bomber | EnemyRole.Commander -> " · PLASMAKERN & WRACKGUT FREI"
                                | EnemyRole.Blocker -> " · NAV-DATEN & WRACKGUT FREI"
                                | _ -> " · WRACKGUT FREI"
                        if PilotSkills.level scene.Pilot 0 >= 2 then
                            match role with
                            | EnemyRole.Gunship | EnemyRole.Bomber ->
                                Salvage.spawn scene.Pickups position Salvage.NavDataKind 1
                                salvageHint <- " · NAV-DATEN & WRACKGUT FREI"
                            | _ -> ()
                    scene.Hud.RewardText <- "+100 CR" + (if salvageHint <> "" then salvageHint else " · ABSCHUSS")
                    scene.Hud.RewardTime <- 2.5

                    if scene.Hud.ComboTimer > 0.0 then
                        scene.Hud.Combo <- System.Math.Min(4, scene.Hud.Combo + 1)
                    else
                        scene.Hud.Combo <- 1

                    scene.Hud.ComboTimer <- 8.0
                    scene.Score <- scene.Score + 100 * System.Math.Max(1, scene.Wave) * scene.Hud.Combo

                    // Schild-Boost bei jedem Abschuss; ab 3x Kombo aktiviert sich der Waffen-Overdrive
                    player.Shield <- System.Math.Min(player.MaxShield, player.Shield + (if PilotSkills.level scene.Pilot 2 >= 2 then 31.0 else 16.0))
                    player.ShieldFlash <- System.Math.Max(player.ShieldFlash, 0.45)

                    if scene.Hud.Combo >= 3 then
                        player.Overdrive <- 7.5

                    Hud.confirmKill scene.Hud

                // Nahe Explosionen ruetteln an der Kamera
                if distance < 120.0 then
                    scene.Shake <- scene.Shake + 0.9 * (1.0 - distance / 120.0)
            elif playing then
                scene.DeathTimer <- DeathDelay
                scene.Shake <- scene.Shake + 1.5
        | ShipHit(victim, damage, shieldAbsorbed) ->
            if playing then
                Sound.hit scene.Sound shieldAbsorbed (victim = Team.Player)
                if victim = Team.Player && not shieldAbsorbed && player.Alive && player.Hull <= 0.25 * player.MaxHull then
                    Sound.lowHullWarn scene.Sound
                    Sound.hullCritical scene.Sound

            if victim = Team.Player then
                Hud.damage scene.Hud damage
                // Jeder Treffer ist spuerbar, Schildtreffer etwas weicher als Huellentreffer.
                scene.Shake <- scene.Shake + (if shieldAbsorbed then 0.25 + damage * 0.03 else 0.4 + damage * 0.045)
            else
                Hud.confirmHit scene.Hud shieldAbsorbed
        | PlayerHitFrom direction ->
            if playing then Hud.damageFrom scene.Hud direction
        | LaserFired(team, position, slot) ->
            if playing then
                if slot >= 0 && scene.Brains.[slot].Role = EnemyRole.Commander then Sound.macroCannonAt scene.Sound position
                else
                    let role = if slot >= 0 then int scene.Brains.[slot].Role else -1
                    Sound.laserAt scene.Sound (team = Team.Player) position (player.Overdrive > 0.0) scene.Upgrades.[0] scene.Fleet.Kit role
        | TurretFired position ->
            if playing then Sound.turretFireAt scene.Sound position
        | CapitalGunFired position ->
            if playing then Sound.macroCannonAt scene.Sound position
        | CapitalHit(position, part, destroyed) ->
            if playing then Sound.capitalHit scene.Sound position (part < 0) destroyed
        | CollisionImpact(position, intensity, isPlayer) ->
            if playing then Sound.collisionImpact scene.Sound position intensity isPlayer

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
            if scene.State = Playing then scene.Brains.[i].Home <- scene.World.Player.Position

    scene.EnemiesAlive <- alive

    match scene.State with
    | Playing ->
        if scene.World.Player.Alive then
            if scene.SovereignWarningArmed && enemies.[0].Alive && scene.Brains.[0].WeaponCharge > 0.0 &&
               Sound.tryMacroWarning scene.Sound then scene.SovereignWarningArmed <- false
            for i in 0 .. enemies.Length - 1 do
                if enemies.[i].Alive && (scene.Brains.[i].Role = EnemyRole.Commander || scene.Brains.[i].Role = EnemyRole.Bomber) &&
                   scene.Brains.[i].WeaponCharge > 0.35 && scene.Brains.[i].WeaponCharge < 0.75 then
                    Sound.macroCharge scene.Sound
            match scene.Phase with
            | Intermission ->
                scene.PhaseTimer <- scene.PhaseTimer - dt

                if scene.PhaseTimer <= 0.0 then
                    if scene.Trading then
                        scene.SaveDirty <- true
                        scene.Spawned <- min 2 scene.Patrol.Length
                        for i in 0 .. scene.Spawned-1 do spawnEnemy scene i
                        scene.EnemiesAlive <- scene.Spawned
                        scene.SpawnTimer <- 8.0
                        scene.Phase <- States.fighting
                        if Array.contains EnemyRole.Blocker scene.Patrol then
                            Hud.notify scene.Hud "SPERRJÄGER IM ANFLUG" "Bei gelbem Aufglühen den Kurs ändern. Fächersalven treffen auf kurze Distanz." 4.5
                        else
                            Hud.notify scene.Hud "PIRATEN AUF ABFANGKURS" "Zur Station durchbrechen oder kämpfen · 100 CR je Abschuss · X bremst." 4.0
                        Sound.pirateAmbush scene.Sound
                    else startWave scene
            | Fighting ->
                // Im Bossabschnitt bleibt ein Teil der Eskorte in Reserve, bis die Korvette ruft.
                let count = Encounters.rosterLimit (encounterRoles scene) scene.Brains enemies
                if Encounters.takeEscortCall scene.Brains enemies then
                    scene.SpawnTimer <- min scene.SpawnTimer Encounters.EscortDelay
                    let title, hint = Encounters.phaseNotice scene.Brains.[0].BossPhase
                    Hud.notify scene.Hud title hint 4.5
                    Sound.capitalHornAt scene.Sound enemies.[0].Position
                if scene.Spawned < count then
                    scene.SpawnTimer <- scene.SpawnTimer - dt
                    if scene.SpawnTimer <= 0.0 then
                        let next = min count (scene.Spawned + 2)
                        for i in scene.Spawned .. next - 1 do
                            spawnEnemy scene i
                        scene.EnemiesAlive <- alive + next - scene.Spawned
                        scene.Spawned <- next
                        scene.SpawnTimer <- if scene.Trading then 8.0 else 5.0
                elif alive = 0 then
                    if scene.Trading then
                        scene.SaveDirty <- true
                        scene.Phase <- States.salvaging
                        if scene.Sortie.Role <> 0 then
                            scene.Hud.RewardText <- if scene.Sortie.Role = CapitalFlight.Escort then "Kepler Zwei · »Danke. Wir haben freie Sicht.«" else "Abfangjäger ausgeschaltet"
                            scene.Hud.RewardTime <- 3.0
                        else
                            Hud.notify scene.Hud "HANDELSWEG FREI" "Patrouille ausgeschaltet · Station anfliegen, handeln oder Erz bergen." 4.0
                            Sound.sectorClear scene.Sound
                    else completeWave scene
            | Salvaging -> ()
        else
            // Die Explosion des eigenen Schiffs noch zeigen, dann Game Over
            scene.DeathTimer <- scene.DeathTimer - dt

            if scene.DeathTimer <= 0.0 then
                endGame scene
    | _ -> ()

    let hud = scene.Hud
    hud.Credits <- scene.Wallet.Credits
    hud.CargoUsed <- TradeSystem.usedVolume scene.Cargo
    hud.CargoCapacity <- scene.Cargo.Capacity
    hud.TurretOwned <- scene.Wallet.TurretOwned
    hud.TurretTarget <- scene.Turret.Target
    hud.Salvaging <- scene.Phase = Salvaging
    hud.Trading <- scene.Trading
    hud.FreightGate <-
        if scene.Trading && scene.Sortie.Role = 0 && scene.School.Step = FlightSchool.Off then
            scene.Cargo.Freight |> Option.bind (Freight.nextGate scene.Sector)
        else None
    hud.CourseGate <-
        if scene.Trading && scene.Sortie.Role = 0 && scene.School.Step = FlightSchool.Off then
            StarMap.nextPort scene.Sector scene.CourseTarget scene.CourseVia |> Option.map (fun port -> Universe.sectors.[port].Id)
        else None
    hud.Trips <- scene.Trips
    hud.TradeProfit <- int scene.TradeProfit
    let total = max 1 (encounterRoles scene).Length
    if scene.Phase = Fighting then
        hud.PendingEnemies <- max 0 (total - scene.Spawned)
        hud.StageProgress <- float (total - scene.EnemiesAlive - hud.PendingEnemies) / float total
        hud.Countdown <- 0
    elif scene.Phase = Intermission then
        hud.PendingEnemies <- 0
        hud.StageProgress <- 0.0
        hud.Countdown <- max 0 (int (ceil scene.PhaseTimer))
    else
        hud.StageProgress <- 1.0
        hud.PendingEnemies <- 0
        hud.Countdown <- 0

    if scene.Trading then
        hud.StageName <- (currentSector scene).Name
        hud.StageProgress <- if scene.Sortie.Role <> 0 then float scene.Sortie.Hits / 2.0 else max 0.0 (min 1.0 (scene.TradeProfit / 1000.0))
        hud.Objective <-
            if scene.Sortie.Role <> 0 && scene.Blockade.Lifted then "Frachter Sechs hat Freigabe. B bringt dich nach Helios zurück."
            elif scene.Sortie.Role = CapitalFlight.Bomber then "Kreuzer im Visier halten · J Torpedo bei Zielton · B Rückzug"
            elif scene.Sortie.Role = CapitalFlight.Escort then "Kepler Zwei decken · Abfangjäger ausschalten · B Rückzug"
            elif scene.Phase = Fighting then "Piraten: Durchbrechen oder bekämpfen. Nur nahe Feinde sperren das Dock."
            elif scene.Cargo.Freight.IsSome then Freight.flightHint scene.Sector scene.Cargo.Freight.Value
            else
                match Blockade.manifestLeg scene.Blockade scene.Sector with
                | Some leg -> leg.Hint
                | None when hud.CourseGate.IsSome ->
                    "Kurs · " + ((currentSector scene).Gates |> Array.find (fun gate -> Some gate.TargetSectorId = hud.CourseGate)).Label
                | None when scene.Sector = Blockade.helios && not scene.Blockade.Lifted && scene.Blockade.Permit = 0 ->
                    "AETHER hält den Filterfrachter fest. Orlov wartet am Dock."
                | None when scene.TradeProfit >= 1000.0 -> "Handelsziel erreicht! Neue Routen, größere Fracht oder bessere Ausrüstung?"
                | None when scene.Trips = 0 && TradeSystem.usedVolume scene.Cargo = 0 -> "Erster Handel: Kestrel anfliegen, Kryo-Erz kaufen oder selbst bergen."
                | None -> "Fracht verkaufen, Kurse vergleichen und deine nächste Route wählen."

let private closeSaves (scene: Scene) =
    let wasOpen = scene.Saves.Visible
    SaveUi.hide scene.Saves
    scene.Saves.Imported <- ""
    scene.Input.SaveDialogOpen <- false
    Input.reset scene.Input
    if wasOpen then
        setState scene scene.SaveReturnState
        Sound.uiClick scene.Sound
        if scene.State = Refitting then
            scene.Shop.Root.removeAttribute "hidden"
            DockContact.dock scene.Contact (currentSector scene).Outpost.Id scene.Pilot
            (document.getElementById "depart" :?> HTMLButtonElement).focus()
    scene.Input.UiMode <- scene.State = Refitting || scene.State = Menu
    if wasOpen && scene.State = Menu then (document.getElementById "menu-saves").focus()

let private openSaves (scene: Scene) =
    if not scene.Saves.Visible then
        scene.SaveReturnState <- scene.State
        if scene.State = Playing then setState scene States.paused
        if scene.State = Refitting then
            DockContact.leave scene.Contact
            Sound.clearRadio scene.Sound
            Shop.hide scene.Shop
        scene.Input.SaveDialogOpen <- true
        scene.Input.UiMode <- true
        Input.reset scene.Input
        SaveUi.show scene.Saves
        Sound.uiClick scene.Sound
        scene.Saves.Status.textContent <- scene.Hud.SaveText
        refreshSaveSlots scene

let private loadFlight (scene: Scene) json =
    match GameSave.decode json with
    | Error error -> saveNotice scene true error
    | Ok data ->
        scene.SovereignWarningArmed <- false
        closeSaves scene
        scene.Skills.Request <- None
        Shop.hide scene.Shop
        scene.Input.UiMode <- false
        document.exitPointerLock()
        GameSave.restore data scene.World scene.Brains scene.Wallet scene.Cargo scene.Markets scene.Upgrades scene.Turret scene.Pickups
        AI.resync scene.World scene.Brains
        let run = data.Run
        scene.School <- defaultArg run.School (FlightSchool.create ())
        scene.Fleet <- defaultArg run.Fleet (ShipCatalog.create ())
        scene.Shop.Shipyard.Request <- None
        scene.Shop.FreightDesk.Request <- None
        scene.Blockade <- defaultArg run.Blockade (Blockade.create ())
        scene.Sortie <- defaultArg run.Sortie (CapitalFlight.create ())
        if scene.Sortie.Role = CapitalFlight.Bomber then configureBomber scene.World.Player
        scene.Yard.Request <- None
        if run.Trading then Blockade.beginRun scene.Blockade scene.Markets |> ignore
        DockContact.restore scene.Contact (defaultArg run.HeardContacts 0)
        scene.Trading <- run.Trading
        scene.Sector <- scene.World.SectorIndex
        scene.CourseTarget <- run.CourseTarget
        scene.CourseVia <- if StarMap.validStop scene.Sector run.CourseTarget run.CourseVia then run.CourseVia else None
        scene.Wave <- run.Wave
        clearDockingArea scene
        scene.Phase <- match run.Phase with 0 -> States.intermission | 1 -> States.fighting | _ -> States.salvaging
        scene.PhaseTimer <- run.PhaseTimer
        scene.Spawned <- run.Spawned
        scene.SpawnTimer <- run.SpawnTimer
        scene.Patrol <- run.Patrol |> Array.map enum<EnemyRole>
        scene.Score <- run.Score
        scene.Kills <- run.Kills
        scene.Trips <- run.Trips
        scene.TradeProfit <- run.Profit
        scene.DeathTimer <- 0.0
        scene.Accumulator <- 0.0
        scene.Shake <- 0.0
        scene.PrevBoostLocked <- false
        scene.PrevOverdrive <- scene.World.Player.Overdrive
        scene.PrevShieldUp <- scene.World.Player.Shield > 0.0
        scene.PrevShieldDelay <- scene.World.Player.ShieldDelay
        scene.UpgradeNotified <- scene.Wallet.TurretOwned || scene.Wallet.Credits >= 350
        scene.JumpNotifiedSector <- -1
        scene.JumpTransitUntil <- 0.0
        scene.GateContactUntil <- 0.0
        scene.Time <- data.WorldTime
        scene.Hud.Target <- -1
        scene.Hud.ManualLock <- false
        scene.Hud.BannerTime <- 0.0
        scene.Hud.RewardTime <- 0.0
        scene.Hud.HitMarker <- 0.0
        scene.Hud.DamageFlash <- 0.0
        scene.Hud.Combo <- run.Combo
        scene.Hud.ComboTimer <- run.ComboTimer
        scene.Hud.ResultVictory <- run.Completed
        scene.Camera.Mode <- if data.Settings.Camera = 1 then Cockpit else CameraMode.Chase
        scene.Input.InvertPitch <- data.Settings.InvertPitch
        if scene.SensitivityOverride then
            scene.SensitivityOverride <- false
            scene.SaveDirty <- true
        else
            scene.Input.MouseSensitivity <- data.Settings.Sensitivity
        if scene.Sound.Muted <> data.Settings.Muted then Sound.toggleMute scene.Sound
        Sound.unlock scene.Sound
        scene.Hud.Muted <- scene.Sound.Muted
        scene.Camera.OpticsActive <- false
        Camera.snap scene.Camera scene.World.Player.Position scene.World.Player.Rotation
        applySectorAtmosphere scene
        setState scene States.paused
        let stage = Mission.stage (if scene.Phase = Intermission then run.Wave + 1 else run.Wave)
        scene.Hud.StageName <- stage.Name
        scene.Hud.Objective <- if scene.Phase = Salvaging then "Erz bergen, Station anfliegen und Fracht verkaufen." else stage.Objective
        updateWaves scene 0.0
        if run.Completed then
            Hud.setResult scene.Hud run.Score run.Wave run.Kills scene.Best false
            setState scene States.victory
        elif run.Docked then showDock scene false
        scene.SaveRetryDelay <- 0.0
        scene.SaveDirty <- true

let private chooseTradeShip (scene: Scene) =
    StartHangar.show (fun hull ->
        closeSaves scene
        startGame scene true hull)

let private handleSaveAction (scene: Scene) action slot confirmed =
    let ui = scene.Saves
    let ask action slot message = SaveUi.ask ui action slot message
    match action with
    | "open" -> openSaves scene
    | "close" -> closeSaves scene
    | "newtrade" | "newcombat" ->
        if not confirmed && (scene.ResumeJson.IsSome || canSave scene) then
            openSaves scene
            ask action "" "Neuen Flug starten? Der automatische Spielstand wird ersetzt. Deine manuellen Speicherplätze bleiben erhalten."
        elif action = "newtrade" then chooseTradeShip scene
        else
            closeSaves scene
            startGame scene false 0
    | "save" when canSave scene && Array.contains slot SaveStore.slots ->
        let existing = readSlot slot
        if slot <> "auto" && not confirmed && (existing.Json.IsSome || existing.Error <> "") then
            ask action slot (SaveUi.label slot + " mit dem aktuellen Flug überschreiben?")
        else
            saveFlight scene slot
            refreshSaveSlots scene
    | "load" | "export" ->
        let entry = readSlot slot
        match entry.Json with
        | None -> saveNotice scene true (if entry.Error = "" then "Dieser Speicherplatz ist leer." else entry.Error)
        | Some json when action = "export" ->
            SaveUi.download json ("fable-space-" + slot + ".json")
        | Some _ when not confirmed && canSave scene ->
            ask action slot "Diesen Spielstand laden und den aktuellen Flug ersetzen?"
        | Some json -> loadFlight scene json
    | "exportcurrent" when canSave scene ->
        let json = snapshot scene |> GameSave.encode
        match GameSave.decode json with
        | Ok _ -> SaveUi.download json "fable-space-flug.json"
        | Error error -> saveNotice scene true error
    | "import" when slot = "slot1" || slot = "slot2" || slot = "slot3" ->
        match GameSave.decode ui.Imported with
        | Error error ->
            ui.Imported <- ""
            saveNotice scene true error
        | Ok _ ->
            let existing = readSlot slot
            if not confirmed && (existing.Json.IsSome || existing.Error <> "") then
                ask action slot (SaveUi.label slot + " mit der importierten Datei überschreiben?")
            else
                let result = SaveStore.write storage slot ui.Imported GameSave.isValid
                ui.Imported <- ""
                if result.Ok then
                    refreshSaveSlots scene
                    saveNotice scene false ("Datei in " + SaveUi.label slot + " importiert.")
                else saveNotice scene true result.Error
    | _ -> ()

let private processSaveUi (scene: Scene) =
    let ui = scene.Saves
    let action = ui.Action
    let slot = ui.Slot
    ui.Action <- ""
    if not ui.Visible && (action = "open" || Input.wasPressed scene.Input GameAction.OpenSaves) then
        openSaves scene
        true
    elif ui.Visible then
        match ui.Pending, action with
        | Some(pending, target), "confirm" ->
            SaveUi.cancel ui
            handleSaveAction scene pending target true
        | Some _, "cancel" ->
            ui.Imported <- ""
            SaveUi.cancel ui
        | _, "close" -> closeSaves scene
        | None, action when action <> "" -> handleSaveAction scene action slot false
        | _ -> ()
        true
    else false

// -------------------------------------------------------------------------------------
//  Darstellung
// -------------------------------------------------------------------------------------

/// Prüft die Hüllkugel an derselben interpolierten Position, die das Mesh erhält.
let private pushAsteroids (scene: Scene) (batches: InstanceBatch[]) (backstep: float) =
    let field = scene.World.Asteroids
    let a = field.Data
    let time = scene.World.Time - backstep
    let distance = scene.Graphics.Preset.SmallObjectDistance
    let eye = scene.Camera.Position

    for batch in batches do InstanceBatch.clear batch

    for i in 0 .. field.Count - 1 do
        let o = i * AsteroidField.Stride
        let radius = float a.[o + 6]
        let x = float a.[o] - float a.[o + 3] * backstep
        let y = float a.[o + 1] - float a.[o + 4] * backstep
        let z = float a.[o + 2] - float a.[o + 5] * backstep
        let inRange =
            if distance = infinity then true
            else
                let dx, dy, dz = x - eye.X, y - eye.Y, z - eye.Z
                let limit = distance + radius
                dx * dx + dy * dy + dz * dz <= limit * limit
        if inRange && Frustum.intersectsSphere scene.AsteroidFrustum x y z radius then
            let half = (float a.[o + 11] + time * float a.[o + 10]) * 0.5
            let s = sin half
            InstanceBatch.pushTRS
                batches.[AsteroidMeshes.variant (int field.Variant.[i]) (float a.[o + 7])]
                x y z
                (float a.[o + 7] * s)
                (float a.[o + 8] * s)
                (float a.[o + 9] * s)
                (cos half)
                radius radius radius
                (float a.[o + 12])
                (float a.[o + 13])
                (float a.[o + 14])
                0.0

/// Alle Laser-Bolts in einem Draw-Call: farbiger Saum plus heller Kern je Bolt
/// (Aussehen je Quelle aus CombatVfx). Jeder Bolt bekommt zusaetzlich einen Lichthof
/// im Glow-Batch - von hinten gesehen waere er sonst nur ein Punkt - und in den
/// ersten Millisekunden einen Muendungsstern am Rohr.
/// Muss NACH pushParticles laufen (das leert den Glow-Batch).
let private pushLasers (scene: Scene) (backstep: float) =
    let pool = scene.World.Lasers
    let d = pool.Data
    let cameraRotation = scene.Camera.Rotation
    let cx = cameraRotation.X
    let cy = cameraRotation.Y
    let cz = cameraRotation.Z
    let cw = cameraRotation.W
    let overdrive = scene.World.Player.Overdrive > 0.0
    let focus = scene.Fleet.Kit = 2 && scene.Sortie.Role <> CapitalFlight.Bomber
    let glow = scene.GlowBatch
    InstanceBatch.clear scene.LaserBatch

    for i in 0 .. pool.Count - 1 do
        let o = i * LaserPool.Stride
        let owner = int pool.Owner.[i]
        let slot = owner - CombatVfx.OwnerEnemySlot
        let role = if slot >= 0 && slot < scene.Brains.Length then int scene.Brains.[slot].Role else 0
        let style = CombatVfx.boltStyles.[CombatVfx.boltStyleId owner (int pool.Team.[i] = int Team.Player) overdrive focus role]
        let vx = float d.[o + 3]
        let vy = float d.[o + 4]
        let vz = float d.[o + 5]
        let x = float d.[o] - vx * backstep
        let y = float d.[o + 1] - vy * backstep
        let z = float d.[o + 2] - vz * backstep
        let qx = float d.[o + 6]
        let qy = float d.[o + 7]
        let qz = float d.[o + 8]
        let qw = float d.[o + 9]
        let thickness = style.Thickness
        let length = style.Length

        InstanceBatch.pushTRS scene.LaserBatch x y z qx qy qz qw thickness thickness length style.R style.G style.B 1.0
        InstanceBatch.pushTRS scene.LaserBatch x y z qx qy qz qw (thickness * 0.42) (thickness * 0.42) (length * 0.86) style.CoreR style.CoreG style.CoreB 1.0

        // Lichthof am Kopf des Bolts
        let head = length * 0.32 / System.Math.Max(1.0, sqrt (vx * vx + vy * vy + vz * vz))
        let halo = style.Halo
        InstanceBatch.pushTRS glow (x + vx * head) (y + vy * head) (z + vz * head) cx cy cz cw halo halo halo (style.R * 0.75) (style.G * 0.75) (style.B * 0.75) 1.0

        // Muendungsstern: steht kurz am Abschussort, waehrend der Bolt davonzieht
        let age = Combat.LaserLife - float d.[o + 10] + backstep

        if age < CombatVfx.MuzzleTime then
            let k = 1.0 - System.Math.Max(0.0, age) / CombatVfx.MuzzleTime
            let back = age + length * 0.5 / Combat.LaserSpeed
            let mx = x - vx * back
            let my = y - vy * back
            let mz = z - vz * back
            let s = style.Muzzle * (0.55 + 0.45 * k)
            InstanceBatch.pushTRS glow mx my mz cx cy cz cw s s s (style.R * k) (style.G * k) (style.B * k) 1.0
            InstanceBatch.pushTRS glow mx my mz cx cy cz cw (s * 0.45) (s * 0.45) (s * 0.45) (style.CoreR * k) (style.CoreG * k) (style.CoreB * k) 1.0

/// Partikel: Leuchtpunkte und Glutwolken als Billboards (Rotation = Kamerarotation)
/// in den additiven Batch, Funken als Leuchtstriche entlang ihrer Flugrichtung,
/// Stossringe in ihrer Ebene, Truemmer als taumelnde, beleuchtete Brocken.
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
    InstanceBatch.clear scene.VfxStreakBatch
    InstanceBatch.clear scene.VfxRingBatch

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
        let kind = int pool.Kind.[i]

        if kind = ParticleKind.Glow then
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
        elif kind = ParticleKind.Streak then
            // Strich entlang der Flugrichtung; die Laenge folgt dem Tempo (Bewegungsunschaerfe)
            let speed = sqrt (vx * vx + vy * vy + vz * vz)
            let inv = 1.0 / System.Math.Max(1e-3, speed)
            let dx = vx * inv
            let dy = vy * inv
            let dz = vz * inv
            // Kuerzeste Drehung von -Z auf die Flugrichtung
            let w = 1.0 - dz
            let norm = sqrt (dx * dx + dy * dy + w * w)
            let qx, qy, qw = if norm > 1e-5 then dy / norm, -dx / norm, w / norm else 0.0, 1.0, 0.0
            let thick = size
            let length = System.Math.Min(size * 45.0, System.Math.Max(size * 8.0, speed * 0.06))

            InstanceBatch.pushTRS
                scene.VfxStreakBatch
                x
                y
                z
                qx
                qy
                0.0
                qw
                thick
                thick
                length
                (float d.[o + 9] * t)
                (float d.[o + 10] * t)
                (float d.[o + 11] * t)
                1.0
        elif kind = ParticleKind.Ring then
            // Schnell auf, dann auslaufend (ease-out); Normale aus der Achse, sonst zur Kamera
            let e = 1.0 - t * t * t
            let s = size * (0.12 + 0.88 * e)
            let fade = t * sqrt t
            let nx = float d.[o + 12]
            let ny = float d.[o + 13]
            let nz = float d.[o + 14]
            let w = 1.0 + nz
            let norm = sqrt (nx * nx + ny * ny + w * w)
            let free = nx * nx + ny * ny + nz * nz < 0.25
            let qx = if free then cx elif norm > 1e-5 then -ny / norm else 1.0
            let qy = if free then cy elif norm > 1e-5 then nx / norm else 0.0
            let qz = if free then cz else 0.0
            let qw = if free then cw elif norm > 1e-5 then w / norm else 0.0

            InstanceBatch.pushTRS
                scene.VfxRingBatch
                x
                y
                z
                qx
                qy
                qz
                qw
                s
                s
                s
                (float d.[o + 9] * fade)
                (float d.[o + 10] * fade)
                (float d.[o + 11] * fade)
                1.0
        elif kind = ParticleKind.Smoke then
            // Glutwolke: blendet weich ein, waechst und verglimmt spaet
            let s = size * (0.7 + 1.6 * (1.0 - t))
            let fade = t * System.Math.Min(1.0, (1.0 - t) * 6.0)

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
        elif kind = ParticleKind.Debris then
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

    // Gedeckelt: ungebremst wuerden die Koerner im Boost zu bildschirmlangen Strichen.
    let stretch = System.Math.Min(5.5, speed * (0.035 + scene.World.Player.Boost * 0.085))
    let boostGlow = scene.World.Player.Boost * 0.35
    let sun = (currentSector scene).Atmosphere.SunColor
    let sunPeak = max sun.X (max sun.Y sun.Z)
    let dustTint =
        if sunPeak > 0.01 then Vec3.lerp (Vec3(0.62, 0.74, 1.0)) (sun / sunPeak) 0.38
        else Vec3(0.62, 0.74, 1.0)
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
        let distanceSq = dx * dx + dy * dy + dz * dz
        let fade = 1.0 - distanceSq / (half * half)

        if fade > 0.0 then
            let size = float d.[o + 3]
            // Koerner direkt vor der Linse blenden aus, statt quer durchs Bild zu ziehen.
            let near = System.Math.Min(1.0, distanceSq / 144.0)
            let brightness = (0.55 + boostGlow) * fade * near

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
                (dustTint.X * brightness)
                (dustTint.Y * brightness)
                (dustTint.Z * brightness)
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

/// Kameraparalleler Lichthof am Duesenaustritt; der Tiefentest verdeckt ihn
/// automatisch, wenn man das Schiff von vorn sieht.
let private pushEngineHalo (scene: Scene) (position: Vec3) (rotation: Quat) (nozzle: Vec3) (size: float) (r: float) (g: float) (b: float) =
    let p = position + Quat.rotateVec3 rotation (Vec3(nozzle.X, nozzle.Y, nozzle.Z + 0.10))
    let q = scene.Camera.Rotation
    InstanceBatch.pushTRS scene.EngineHaloBatch p.X p.Y p.Z q.X q.Y q.Z q.W size size size r g b 1.0

/// Schreibt Spielerschiff, Gegner, Energieschilde, Abgasflammen und Triebwerkslichter.
let private pushShips (scene: Scene) (playerPosition: Vec3) (playerRotation: Quat) (alpha: float) =
    let world = scene.World
    let lights = scene.Renderer.Lights
    let player = world.Player
    // Leichtes Flackern aller Flammen
    let flicker = 0.92 + 0.08 * sin (scene.Time * 71.0)
    let hullBatch = scene.HullBatches.[scene.Fleet.Active].Value
    InstanceBatch.clear hullBatch
    OutfitVisuals.clear scene.Outfit
    for batch in scene.DeckCargoBatches do InstanceBatch.clear batch
    for batch in scene.EnemyBatches do InstanceBatch.clear batch
    InstanceBatch.clear scene.ExhaustBatch
    InstanceBatch.clear scene.EngineHaloBatch
    InstanceBatch.clear scene.ShieldBatch

    if player.Alive then
        let flash = player.HitFlash

        // In der Cockpit-Perspektive wird das eigene Aussenmodell ausgeblendet,
        // damit die Kamera nicht in die Rumpfnase schneidet.
        if scene.Camera.Mode = Chase then
            if scene.Sortie.Role <> CapitalFlight.Bomber then
                let tint = 1.0 + flash * 0.5
                InstanceBatch.push hullBatch playerPosition playerRotation Vec3.one tint tint tint (flash * 0.25)
                OutfitVisuals.push scene.Outfit scene.Fleet scene.Upgrades playerPosition playerRotation flash
                if scene.Trading && scene.Fleet.Active = 0 then
                    DeckCargo.iter scene.Cargo (fun bay commodity ->
                        let p = playerPosition + Quat.rotateVec3 playerRotation bay
                        InstanceBatch.push scene.DeckCargoBatches.[commodity] p playerRotation Vec3.one tint tint tint (flash*0.25))
                elif scene.Cargo.Freight.IsSome then
                    let job = scene.Cargo.Freight.Value
                    let mounts =
                        match scene.Fleet.Active with
                        | 1 -> DeckCargo.haulerMounts
                        | 3 -> DeckCargo.courierMounts
                        | _ -> DeckCargo.escortMounts
                    let count = min mounts.Length (job.Tonnes / 4)
                    for i in 0 .. count - 1 do
                        let p = playerPosition + Quat.rotateVec3 playerRotation mounts.[i]
                        InstanceBatch.push scene.DeckCargoBatches.[DeckCargo.sealedIndex] p playerRotation Vec3.one tint tint tint (flash*0.25)

            if player.ShieldFlash > 0.01 then
                let sf = player.ShieldFlash
                let center = playerPosition + Quat.rotateVec3 playerRotation ShipHardpoints.playerObbCenter
                let shieldScale =
                    match scene.Fleet.Active with
                    | 1 -> Vec3(3.25, 1.35, 3.65)
                    | 2 -> Vec3(2.45, 0.92, 2.75)
                    | 3 -> Vec3(2.95, 1.05, 3.10)
                    | _ -> Vec3(2.85, 1.05, 2.95)
                InstanceBatch.push
                    scene.ShieldBatch
                    center
                    playerRotation
                    shieldScale
                    (0.18 * sf)
                    (0.72 * sf)
                    (1.45 * sf)
                    1.0
                Lights.addPoint lights center.X center.Y center.Z 15.0 0.24 0.76 1.0 (1.8 * sf)

        // Muendungsfeuer direkt nach einem Schuss (farblich abgestimmt auf Overdrive & Impulsfokus)
        let interval = if player.Overdrive > 0.0 then player.FireInterval * 0.62 else player.FireInterval
        let focus = scene.Fleet.Kit = 2 && scene.Sortie.Role <> CapitalFlight.Bomber

        if player.FireCooldown > interval - 0.045 then
            let mr, mg, mb =
                if player.Overdrive > 0.0 then 0.85, 1.65, 1.85
                elif focus then 0.92, 0.52, 1.75
                else 0.35, 1.4, 0.95
            let muzzleSize = if player.Overdrive > 0.0 then 1.15 else 0.95
            pushEngineHalo scene playerPosition playerRotation ShipHardpoints.playerGunLeft muzzleSize mr mg mb
            pushEngineHalo scene playerPosition playerRotation ShipHardpoints.playerGunRight muzzleSize mr mg mb
            let muzzle = playerPosition + Quat.rotateVec3 playerRotation (Vec3(0.0, -0.1, -1.6))
            Lights.addPoint lights muzzle.X muzzle.Y muzzle.Z 24.0 (mr * 0.65) (mg * 0.65) (mb * 0.65) 1.25

        let thrust = if player.Controls.Brake then 0.0 else player.Controls.Throttle
        let courier = scene.Fleet.Kit = 1 && scene.Sortie.Role <> CapitalFlight.Bomber
        let length = (0.35 + (if courier then 2.15 else 1.5) * thrust + 3.4 * player.Boost) * flicker
        let glow = 0.35 + 0.65 * thrust + 0.9 * player.Boost
        let haloSize = 0.72 + player.Boost * 0.38
        let haloR, haloG, haloB = if courier then 0.68 * glow, 0.32 * glow, 0.08 * glow else 0.06 * glow, 0.26 * glow, 0.6 * glow
        pushEngineHalo scene playerPosition playerRotation ShipHardpoints.playerEngineLeft haloSize haloR haloG haloB
        pushEngineHalo scene playerPosition playerRotation ShipHardpoints.playerEngineRight haloSize haloR haloG haloB

        // Pro Triebwerk ein breiter Mantel und ein schmaler heisser Kern
        let outerR, outerG, outerB = if courier then 1.0 * glow, 0.48 * glow, 0.1 * glow else 0.10 * glow, 0.36 * glow, 0.95 * glow
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineLeft length 0.34 outerR outerG outerB
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineLeft (length * 0.6) 0.17 (0.75 * glow) (0.9 * glow) glow
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineRight length 0.34 outerR outerG outerB
        pushFlame scene playerPosition playerRotation ShipHardpoints.playerEngineRight (length * 0.6) 0.17 (0.75 * glow) (0.9 * glow) glow

        // Triebwerkslicht: beleuchtet Heck und nahe Asteroiden
        let engine = playerPosition + Quat.rotateVec3 playerRotation (Vec3(0.0, 0.1, 2.6))
        let lightR, lightG, lightB = if courier then 1.0, 0.56, 0.18 else 0.35, 0.65, 1.0
        Lights.addPoint lights engine.X engine.Y engine.Z 16.0 lightR lightG lightB (0.3 + 0.9 * thrust + 2.4 * player.Boost)

        // Positionslichter, nachglühende Düsen und Bremsdüsen des sichtbaren Rumpfs.
        if scene.Camera.Mode = Chase then
            let hull = if scene.Sortie.Role = CapitalFlight.Bomber then PlayerMeshes.bomberHull else PlayerMeshes.hulls.[scene.Fleet.Active]
            let braking = if player.Controls.Brake then min 1.0 (Vec3.length player.Velocity / 25.0) else 0.0
            ShipLife.push scene.EngineHaloBatch scene.ExhaustBatch scene.Camera.Rotation scene.Time hull playerPosition playerRotation (thrust + player.Boost) braking
            EngineTrails.player scene.Trails scene.GpuParticles player hull playerPosition playerRotation courier

    let eye = scene.Camera.Position
    let facing = scene.Camera.Rotation
    // Lichtpunkt, der mit der Entfernung mitwächst (Kern bleibt auch auf 300+ m einige Pixel
    // groß) und zur Kamera vorgezogen wird, damit die eigene Hülle ihn nicht anschneidet.
    let pushBeacon (point: Vec3) (size: float) (r: float) (g: float) (b: float) =
        let toEye = eye - point
        let distance = Vec3.length toEye
        let s = max size (min distance 1400.0 * 0.0095)
        let p = if distance > 1e-3 then point + toEye * (min (s * 0.7) (distance * 0.5) / distance) else point
        InstanceBatch.pushTRS scene.EngineHaloBatch p.X p.Y p.Z facing.X facing.Y facing.Z facing.W s s s r g b 1.0

    for i in 0 .. world.Enemies.Length - 1 do
        let enemy = world.Enemies.[i]

        if enemy.Alive then
            let position = Vec3.lerp enemy.PrevPosition enemy.Position alpha
            let rotation = Quat.slerp enemy.PrevRotation enemy.Rotation alpha
            let flash = enemy.HitFlash
            let brain = scene.Brains.[i]
            let profile = EnemyProfiles.profile brain.Role
            let hull = EnemyMeshes.hull (int brain.Role)
            let size = scene.EnemyScale.X
            let at (point: Vec3) = position + Quat.rotateVec3 rotation (point * size)
            let training = FlightSchool.onRange scene.School.Step
            // Die Modelle tragen ihre Farben selbst; nur die Übungsdrohne bleibt ein Hologramm.
            // Piraten im Handelsflug fliegen dieselben Rümpfe verwittert und uneinheitlich getönt.
            let pirate = scene.Trading && scene.Sortie.Role = 0 && not training
            let tint = if training then Vec3(0.48,1.48,1.20) elif pirate then Encounters.pirateTint i else Vec3.one
            let light = if training then Vec3(0.3,1.3,1.0) elif pirate then Encounters.pirateLight i profile.Tint else profile.Tint

            InstanceBatch.push
                scene.EnemyBatches.[int brain.Role]
                position
                rotation
                scene.EnemyScale
                (tint.X + flash * 1.5)
                (tint.Y + flash * 1.5)
                (tint.Z + flash * 1.5)
                (flash * 0.6)

            if enemy.ShieldFlash > 0.01 then
                let sf = enemy.ShieldFlash
                let center = position + Quat.rotateVec3 rotation enemy.ObbCenter
                InstanceBatch.push
                    scene.ShieldBatch
                    center
                    rotation
                    (enemy.ObbHalf * 1.2)
                    (0.35 * sf)
                    (0.78 * sf)
                    (1.5 * sf)
                    1.0

            // Mündungsfeuer. Beim Aufladen glühen alle Rohre an; schwere Rollen werfen
            // zusätzlich Licht auf ihren Bug, damit die kommende Salve früh lesbar ist.
            let muzzle = hull.MuzzleSize * size
            let gunLeft = position + Quat.rotateVec3 rotation enemy.GunLeft
            let gunRight = position + Quat.rotateVec3 rotation enemy.GunRight
            if enemy.FireCooldown > enemy.FireInterval - 0.05 then
                pushBeacon gunLeft (muzzle * 1.3) 2.4 0.8 0.25
                pushBeacon gunRight (muzzle * 1.3) 2.4 0.8 0.25
                CombatLights.muzzle lights gunLeft gunRight muzzle (int brain.Role)
            elif brain.WeaponCharge > 0.0 then
                // Werte über 1 brennen den Kern zur festen Scheibe aus: auch auf Distanz ein greller Punkt.
                let charge = brain.WeaponCharge
                let pulse = 0.8 + 0.2 * sin (scene.Time * (14.0 + 34.0 * charge))
                let s = muzzle * (0.3 + 0.9 * charge) * pulse
                let r, g, b = 2.4 * charge, 1.05 * charge, 0.22 * charge
                pushBeacon gunLeft s r g b
                pushBeacon gunRight s r g b
                for battery in hull.Batteries do
                    pushBeacon (at battery) s r g b
                if charge > 0.2 && int brain.Role >= int EnemyRole.Gunship then
                    let bow = position + Quat.rotateVec3 rotation ((enemy.GunLeft + enemy.GunRight) * 0.5)
                    Lights.addPoint lights bow.X bow.Y bow.Z (muzzle * 14.0) 1.0 0.7 0.3 (2.2 * charge)

            // Mehrere Triebwerke je Rolle; ab mittlerer Entfernung wachsen die Höfe mit,
            // damit das Heck als Lichtpunkt lesbar bleibt.
            let thrust = if enemy.Controls.Brake then 0.0 else enemy.Controls.Throttle
            let heat = 0.45 + 0.55 * thrust + 0.8 * enemy.Boost
            let reach = (0.6 + 1.5 * thrust + 2.6 * enemy.Boost) * flicker
            let far = min (Vec3.distance position eye) 1400.0 * 0.0055
            let mutable stern = Vec3.zero
            for nozzle, radius in hull.Engines do
                let n = nozzle * size
                let r = radius * size
                pushEngineHalo scene position rotation n (max (r * 2.6) far) (light.X * heat) (light.Y * heat * 0.55) (light.Z * heat * 0.45)
                pushFlame scene position rotation n (r * 5.5 * reach) (r * 2.1) (0.95 * heat) (0.32 * heat) (0.07 * heat)
                pushFlame scene position rotation n (r * 3.2 * reach) r heat (0.82 * heat) (0.52 * heat)
                stern <- stern + n
            EngineTrails.enemy scene.Trails scene.GpuParticles i enemy hull position rotation size eye
            let engine = position + Quat.rotateVec3 rotation (stern / float hull.Engines.Length)
            // Lights waehlt die wichtigsten Lichter selbst aus (LightSelection)
            Lights.addPoint lights engine.X engine.Y engine.Z (hull.Radius * size * 3.0) 1.0 0.45 0.15 (0.4 + 1.2 * thrust)

            // Positionslicht in Rollenfarbe (ruhiges Pulsieren), karminrote Doppelblitzer.
            let pulse = if pirate then Encounters.pirateFlicker scene.Time i else 0.85 + 0.15 * sin (scene.Time * 2.6 + float i * 1.7)
            pushBeacon (at hull.Beacon) ((0.35 + 0.15 * hull.MuzzleSize) * size) (light.X * pulse * 1.6) (light.Y * pulse * 1.6) (light.Z * pulse * 1.6)
            let blink = (scene.Time * 0.8 + float i * 0.37) % 1.0
            if pirate then
                if Encounters.pirateStrobe scene.Time i then
                    for strobe in hull.Strobes do
                        pushBeacon (at strobe) (0.4 * size) (light.X * 1.2) (light.Y * 1.2) (light.Z * 1.2)
            elif blink < 0.07 || (blink > 0.14 && blink < 0.2) then
                for strobe in hull.Strobes do
                    pushBeacon (at strobe) (0.4 * size) 1.7 0.14 0.07

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

let private pushEquipmentAndMinerals (scene: Scene) (position: Vec3) (rotation: Quat) =
    InstanceBatch.clear scene.CrystalBatch
    InstanceBatch.clear scene.TurretBaseBatch
    InstanceBatch.clear scene.TurretBatch
    InstanceBatch.clear scene.MissileBatch
    for m in scene.Missiles.Missiles do
        if m.Alive then
            InstanceBatch.push scene.MissileBatch m.Position m.Rotation (Vec3(0.55, 0.55, 0.55)) 1.15 0.95 0.85 0.2
            let motor = Vec3(0.0, 0.0, 0.62)
            pushFlame scene m.Position m.Rotation motor 2.8 0.22 1.5 0.52 0.12
            pushEngineHalo scene m.Position m.Rotation motor 1.35 1.15 0.42 0.08
    let crystal (point: Vec3) (size: float) (kind: int) (angle: float) =
        let tint =
            match kind with
            | 2 -> Vec3(1.7, 0.95, 0.15)
            | 3 -> Vec3(1.5, 0.55, 1.7)
            | 4 -> Vec3(0.85, 0.94, 1.06)
            | 5 -> Vec3(1.85, 0.62, 0.12)
            | 6 -> Vec3(1.55, 0.84, 0.24)
            | _ -> Vec3(0.14, 1.2, 1.4)
        InstanceBatch.push scene.CrystalBatch point (Quat.fromAxisAngle Vec3.unitY angle) (Vec3(size,size,size)) tint.X tint.Y tint.Z 0.35
    // Nur bereits freigelegte Mineralien darstellen. Der Erzvorrat eines noch
    // ungetroffenen Asteroiden beeinflusst weder Geometrie noch Farbe oder Licht.
    for pickup in scene.Pickups do
        let size =
            match pickup.Kind with
            | 2 -> 1.3
            | 4 -> 1.1
            | 5 -> 1.35
            | 6 -> 1.05
            | _ -> 1.0
        crystal pickup.Position size pickup.Kind (scene.Time * 1.2 + pickup.Life)
        if Vec3.distanceSq position pickup.Position < 40000.0 then
            let p = pickup.Position
            let glow =
                match pickup.Kind with
                | 2 -> Vec3(0.27, 0.17, 0.04)
                | 3 -> Vec3(0.22, 0.08, 0.28)
                | 4 -> Vec3(0.14, 0.17, 0.22)
                | 5 -> Vec3(0.34, 0.12, 0.03)
                | 6 -> Vec3(0.24, 0.14, 0.04)
                | _ -> Vec3(0.04, 0.22, 0.22)
            InstanceBatch.push scene.GlowBatch p scene.Camera.Rotation (Vec3(4.0,4.0,4.0)) glow.X glow.Y glow.Z 1.0
    if scene.Wallet.TurretOwned && scene.World.Player.Alive && scene.Sortie.Role <> CapitalFlight.Bomber then
        let mount = position + Quat.rotateVec3 rotation Turret.Mount
        let head = mount - Quat.forward scene.Turret.Rotation * (scene.Turret.Flash * 0.09)
        InstanceBatch.push scene.TurretBaseBatch mount rotation Vec3.one 1.0 1.0 1.0 0.0
        InstanceBatch.push scene.TurretBatch head scene.Turret.Rotation Vec3.one 1.0 1.0 1.0 0.0
        if scene.Turret.Flash > 0.0 then
            let gun = if scene.Turret.LastBarrel = 0 then ShipHardpoints.turretGunLeft else ShipHardpoints.turretGunRight
            let flash = scene.Turret.Flash
            pushEngineHalo scene head scene.Turret.Rotation gun (0.28 + flash * 0.25) (0.15 * flash) (1.5 * flash) (1.8 * flash)

    LandmarkMeshes.pushLights scene.Landmarks (sectorIndex scene) scene.Time scene.Renderer.Lights scene.EngineHaloBatch scene.Camera.Rotation

/// Physische 3D-Sprungtore, rotierende Beschleunigerringe und Portal-Leuchtfeuer.
let private updateGateCrossing (scene: Scene) =
    let ship = scene.World.Player
    if scene.State = Playing && ship.Alive && scene.Trading && scene.School.Step = FlightSchool.Off && scene.Sortie.Role = 0 then
        let sector = currentSector scene
        let event =
            sector.Gates |> Array.tryPick (fun gate ->
                JumpFlight.trace sector gate scene.Time ship.Radius ship.PrevPosition ship.Position
                |> Option.map (fun hit -> gate, hit))
        match event with
        | Some (_, struct (FrameContact, fraction)) ->
            let point = Vec3.lerp ship.PrevPosition ship.Position fraction
            let direction = Vec3.normalize (ship.Position - ship.PrevPosition)
            ship.Position <- point - direction * 0.025
            ship.Velocity <- Vec3.zero
            if scene.Time >= scene.GateContactUntil then
                scene.GateContactUntil <- scene.Time + 0.8
                scene.Shake <- max scene.Shake 0.7
                Effects.sparks scene.World point.X point.Y point.Z 14 1.35 0.62 0.22
                Sound.collisionImpact scene.Sound point 0.55 true
                Hud.notify scene.Hud "RAHMENKONTAKT" "Abstand nehmen und die Toröffnung anfliegen." 2.2
        | Some (gate, struct (ApertureCrossed, _)) ->
            let terms = tradeTerms scene
            let destination = Universe.sectors |> Array.findIndex (fun s -> s.Id = gate.TargetSectorId)
            let reciprocal = Universe.sectors.[destination].Gates |> Array.tryFind (fun candidate -> candidate.TargetSectorId = sector.Id)
            match reciprocal, TradeRoutes.tryPlanWithEscort terms scene.Blockade.EscortPort scene.Sector destination false scene.Wallet.Credits with
            | Some exitGate, Some plan ->
                processEvents scene
                if ship.Alive && scene.State = Playing then completeTravel scene terms plan (Some exitGate)
            | _ -> ()
        | None -> ()

let private pushJumpGates (scene: Scene) (playerPosition: Vec3) =
    InstanceBatch.clear scene.GateFrameBatch
    InstanceBatch.clear scene.GateRingBatch
    let sector = currentSector scene
    for gate in sector.Gates do
        let gp = gate.Position
        let prox = Universe.gateProximity gate playerPosition
        let frameRot = Universe.gateRotation sector gate
        let ringRot = Universe.gateRingRotation sector gate scene.Time
        let ringTint = Vec3.lerp (Vec3(0.42, 0.90, 1.18)) (Vec3(1.18, 0.86, 0.44)) prox
        InstanceBatch.push scene.GateFrameBatch gp frameRot Vec3.one 1.0 1.0 1.0 0.0
        InstanceBatch.push scene.GateRingBatch gp ringRot Vec3.one ringTint.X ringTint.Y ringTint.Z (0.1 + 0.12 * prox)
        let wave = (scene.Time * 0.72 % 1.0) * 1.36 - 0.18
        let up = Quat.up frameRot
        for emitter in ProceduralMeshes.jumpGateEmitters do
            let offset = Quat.rotateVec3 ringRot emitter
            let rise = 0.5 + 0.5 * Vec3.dot (Vec3.normalize offset) up
            let strength = max 0.0 (1.0 - abs (rise - wave) * 5.5)
            let point = gp + offset
            let glow = 1.0 + strength * (1.8 + prox)
            let tint = ringTint * (0.33 + strength * 0.95)
            InstanceBatch.push scene.GlowBatch point scene.Camera.Rotation (Vec3.one * glow) tint.X tint.Y tint.Z 1.0
        Lights.addPoint scene.Renderer.Lights gp.X gp.Y gp.Z 78.0 0.28 0.78 1.0 (1.0 + 0.65 * prox)

/// Dockside traffic follows the saved simulation clock; both hulls share one draw.
let private pushFreighters (scene: Scene) =
    Traffic.push scene.Traffic (if scene.Trading then scene.Sector else -1) scene.World.Time scene.Camera.Position (pushFlame scene) (pushEngineHalo scene)
    EngineTrails.traffic scene.Trails scene.GpuParticles scene.Traffic (if scene.Trading then scene.Sector else -1) scene.World.Time scene.Camera.Position
    let batch = scene.FreighterBatch
    InstanceBatch.clear batch
    let port = (currentSector scene).Outpost.Position
    if scene.Trading && Vec3.distanceSq port scene.Camera.Position < 1000.0 * 1000.0 then
        let berth = port + Quat.rotateVec3 stationOrientation (Vec3(30.5,-12.0,14.7))
        let berthedHeading = stationOrientation * Quat.fromAxisAngle Vec3.unitY 0.60
        let helios = scene.Sector = Blockade.helios
        let holding = port + Vec3(-71.0,21.0,174.0)
        let t = scene.Sortie.Landing / CapitalFlight.landingTime
        let eased = t*t*(3.0-2.0*t)
        let freightSix = if helios then CapitalFlight.freightSix port berth scene.Sortie.Landing else berth
        let aligned = if helios then Quat.slerp (Quat.lookRotation (berth-holding) Vec3.up) berthedHeading eased else berthedHeading
        InstanceBatch.push batch freightSix aligned (Vec3(0.70,0.70,0.70)) 0.78 0.89 1.0 0.0
        // An elevated holding orbit clears the centreline of the player's approach.
        let phase = 2.35 + float scene.Sector * 0.81 + scene.World.Time * 0.042
        let offset = Vec3(76.0 * cos phase, 34.0, 41.0 * sin phase - 9.0)
        let yaw = atan2 (76.0 * sin phase) (-41.0 * cos phase)
        let ownTransport = scene.Blockade.TransportPort = scene.Sector
        let tr, tg, tb = if ownTransport then 0.56, 0.96, 1.06 else 1.0, 1.0, 1.0
        if helios && not scene.Blockade.Lifted then
            InstanceBatch.push batch (holding+Vec3(64.0,5.0,48.0)) Quat.identity Vec3.one tr tg tb 0.0
        else InstanceBatch.push batch (port + offset) (Quat.fromAxisAngle Vec3.unitY yaw) Vec3.one tr tg tb 0.0
        // Optionale eigene Eskorte (Wing Commander: Armada-Faktor): patrouilliert sichtbar ueber dem Einsatzhafen.
        if scene.Blockade.EscortPort = scene.Sector then
            let scoutBatch = scene.EnemyBatches.[int EnemyRole.Scout]
            if scoutBatch.Count < scoutBatch.Capacity then
                let ep = scene.World.Time * 0.24 + 1.1
                let escortPos = port + Vec3(96.0 * cos ep, 22.0 + 4.0 * sin (ep * 2.0), 96.0 * sin ep)
                let escortDir = Vec3(-sin ep, 0.08 * cos (ep * 2.0), cos ep)
                let escortRot = Quat.lookRotation escortDir Vec3.unitY * Quat.fromAxisAngle Vec3.unitZ 0.24
                InstanceBatch.push scoutBatch escortPos escortRot (scene.EnemyScale * 0.95) 0.34 1.18 0.96 0.08
                let escortEngine = fst (EnemyMeshes.hull (int EnemyRole.Scout)).Engines.[0]
                if scene.ExhaustBatch.Count < scene.ExhaustBatch.Capacity then
                    pushFlame scene escortPos escortRot (escortEngine * 0.95) 2.2 0.36 0.16 0.92 0.82
                if scene.EngineHaloBatch.Count < scene.EngineHaloBatch.Capacity then
                    pushEngineHalo scene escortPos escortRot (escortEngine * 0.95) 0.95 0.14 0.85 0.78

/// Drive plumes, the stern light and the slow red strobes of the customs cruiser.
/// Runs before beginFrame so the drive light reaches the hull.
let private pushCustomsGlow (scene: Scene) =
    if scene.World.Customs.IsSome then
        let p, k = CapitalFlight.position, CapitalFlight.hullScale
        let flicker = 0.93 + 0.07 * sin (scene.Time * 53.0)
        // Plumes extend along their local +Z; this turn points them aft (-X).
        let aft = Quat.fromAxisAngle Vec3.unitY (-0.5 * System.Math.PI)
        for nozzle, radius in CapitalMeshes.drives do
            let exit = p + nozzle * k
            pushFlame scene exit aft Vec3.zero (radius * k * 7.0 * flicker) (radius * k * 2.0) 0.35 0.60 1.2
            pushEngineHalo scene exit aft Vec3.zero (radius * k * 2.4) 0.35 0.55 1.0
        let q = scene.Camera.Rotation
        for i in 0 .. CapitalMeshes.beacons.Length - 1 do
            let on = if (scene.Time * 0.7 + float i * 0.137) % 1.0 < 0.09 then 1.0 else 0.12
            let size = 1.6 * k * (0.6 + 0.4 * on)
            let b = p + CapitalMeshes.beacons.[i] * k
            InstanceBatch.pushTRS scene.EngineHaloBatch b.X b.Y b.Z q.X q.Y q.Z q.W size size size (1.6 * on) (0.12 * on) (0.06 * on) 1.0
        let stern = p + Vec3(-95.0 * k, 0.0, 0.0)
        Lights.addPoint scene.Renderer.Lights stern.X stern.Y stern.Z (140.0 * k) 0.35 0.55 1.0 2.2

let private pushCustomsShips (scene: Scene) (playerPosition: Vec3) (playerRotation: Quat) =
    match scene.World.Customs with
    | None -> ()
    | Some f ->
        let models = scene.CustomsBatches.Value
        let p = CapitalFlight.position
        let scale = Vec3(CapitalFlight.hullScale, CapitalFlight.hullScale, CapitalFlight.hullScale)
        let shutter = if f.Hits > 0 then models.Breach else models.Shutter
        InstanceBatch.clear models.Cruiser
        InstanceBatch.push models.Cruiser p Quat.identity scale 1.0 1.0 1.0 0.0
        InstanceBatch.clear shutter
        InstanceBatch.push shutter p Quat.identity scale 1.0 1.0 1.0 0.0
        // The batteries train on whoever they would fire at, within their side's arc.
        let target = if f.Role = CapitalFlight.Escort then CapitalFlight.allyPosition f else playerPosition
        InstanceBatch.clear models.Battery
        for i in 0..1 do
            if f.Guns.[i] > 0.0 then
                let gun = p + CapitalFlight.gunOffsets.[i]
                let yaw =
                    if Vec3.distance gun target < 900.0 then max (-1.75) (min 1.75 (atan2 (target.X - gun.X) (target.Z - gun.Z)))
                    else 0.0
                InstanceBatch.push models.Battery gun (Quat.fromAxisAngle Vec3.unitY yaw) scale 1.0 1.0 1.0 0.0

    let f = scene.Sortie
    if f.Role <> 0 then
        let models = scene.CustomsBatches.Value
        InstanceBatch.clear models.Bomber
        InstanceBatch.clear models.Torpedoes
        let own = f.Role = CapitalFlight.Bomber
        if (own && scene.World.Player.Alive && scene.Camera.Mode = Chase) || (not own && not scene.Blockade.Lifted) then
            let p = if own then playerPosition else CapitalFlight.allyPosition f
            let q = if own then playerRotation else Quat.lookRotation (CapitalFlight.position-p) Vec3.up
            let flash = if own then scene.World.Player.HitFlash else 0.0
            let tint = 1.0 + flash*0.5
            InstanceBatch.push models.Bomber p q Vec3.one tint tint tint (flash*0.25)
            for i in 0 .. f.Ammo-1 do
                let rack = p + Quat.rotateVec3 q ShipHardpoints.bomberRails.[i]
                InstanceBatch.push models.Torpedoes rack q Vec3.one tint tint tint (flash*0.25)
            if not own then
                pushFlame scene p q ShipHardpoints.playerEngineLeft 1.65 0.30 0.17 0.58 1.0
                pushFlame scene p q ShipHardpoints.playerEngineRight 1.65 0.30 0.17 0.58 1.0
        if f.Flight > 0.0 && not scene.Blockade.Lifted then
            let q = Quat.lookRotation (CapitalFlight.position-f.Launch) Vec3.up
            let p = CapitalFlight.torpedoPosition f
            InstanceBatch.push models.Torpedoes p q Vec3.one 1.0 1.0 1.0 0.0
            let motor = Vec3(0.0,0.0,1.08)
            pushFlame scene p q motor 4.2 0.31 1.45 0.51 0.10
            pushEngineHalo scene p q motor 1.9 1.05 0.39 0.065

let private drawCustomsShadow (scene: Scene) =
    if scene.World.Customs.IsSome || scene.Sortie.Role <> 0 then
        let models = scene.CustomsBatches.Value
        match scene.World.Customs with
        | Some f ->
            let shutter = if f.Hits > 0 then models.Breach else models.Shutter
            Renderer.drawShadow scene.Renderer models.Cruiser
            Renderer.drawShadow scene.Renderer shutter
            Renderer.drawShadow scene.Renderer models.Battery
        | None -> ()
        if scene.Sortie.Role <> 0 then
            Renderer.drawShadow scene.Renderer models.Bomber
            Renderer.drawShadow scene.Renderer models.Torpedoes

let private drawCustomsShips (scene: Scene) =
    if scene.World.Customs.IsSome || scene.Sortie.Role <> 0 then
        let models = scene.CustomsBatches.Value
        match scene.World.Customs with
        | Some f ->
            let shutter = if f.Hits > 0 then models.Breach else models.Shutter
            Renderer.draw scene.Renderer models.Cruiser
            Renderer.draw scene.Renderer shutter
            Renderer.draw scene.Renderer models.Battery
        | None -> ()
        if scene.Sortie.Role <> 0 then
            Renderer.draw scene.Renderer models.Bomber
            Renderer.draw scene.Renderer models.Torpedoes

/// The clear arrival corridor runs along world +Z. Light travels from the outer
/// pair toward the dock. Closed ports show a steady amber signal instead.
let private pushApproachBuoys (scene: Scene) =
    InstanceBatch.clear scene.BuoyFrames
    InstanceBatch.clear scene.BuoyLenses
    let port = (currentSector scene).Outpost
    if Vec3.distanceSq scene.Camera.Position port.Position < 800.0 * 800.0 then
        let closed = dockBlocked scene
        let inner = port.DockRadius * 0.66
        let outer = port.ApproachRadius * 0.91
        for pair in 0 .. ApproachPairs-1 do
            let t = float pair / float (ApproachPairs-1)
            let depth = inner + t*(outer-inner)
            let width = 10.0 + 12.0*t
            let signal = 0.5 + 0.5*cos (scene.World.Time*2.55 + float pair*1.27)
            let intensity = 0.59 + 1.65*signal*signal*signal
            let r,g,b = if closed then 1.92,0.56,0.045 else 0.11*intensity,1.04*intensity,0.86*intensity
            for side in 0..1 do
                let x = if side = 0 then -width else width
                let p = port.Position + Vec3(x,-5.6,depth)
                InstanceBatch.push scene.BuoyFrames p Quat.identity Vec3.one 1.0 1.0 1.0 0.0
                InstanceBatch.push scene.BuoyLenses p Quat.identity Vec3.one r g b 1.0

/// Schiebt die Verfolgerkamera aus groesseren Asteroiden heraus. Camera.update
/// berechnet die Position jeden Frame neu, die Korrektur haeuft sich also nicht auf.
let private keepCameraOutOfRock (scene: Scene) =
    let field = scene.World.Asteroids
    let a = field.Data
    let mutable p = scene.Camera.Position
    let mutable moved = false

    for i in 0 .. field.Count - 1 do
        let o = i * AsteroidField.Stride
        let radius = float a.[o + 6]

        if radius > 3.0 then
            // Das Mesh fuellt seine Huellkugel nicht ganz aus.
            let keepOut = radius * 0.92 + 0.6
            let dx = p.X - float a.[o]
            let dy = p.Y - float a.[o + 1]
            let dz = p.Z - float a.[o + 2]
            let distanceSq = dx * dx + dy * dy + dz * dz

            if distanceSq < keepOut * keepOut && distanceSq > 1e-6 then
                let push = keepOut / sqrt distanceSq
                p <- Vec3(float a.[o] + dx * push, float a.[o + 1] + dy * push, float a.[o + 2] + dz * push)
                moved <- true

    let port = (currentSector scene).Outpost.Position
    let portGap = p - port
    let portDistSq = Vec3.lengthSq portGap
    let portKeepOut = 34.0
    if portDistSq < portKeepOut * portKeepOut && portDistSq > 1e-6 then
        p <- port + portGap * (portKeepOut / sqrt portDistSq)
        moved <- true

    if moved then
        scene.Camera.Position <- p

let private render (scene: Scene) (position: Vec3) (rotation: Quat) (alpha: float) =
    let renderer = scene.Renderer
    let camera = scene.Camera
    let hud = scene.Hud
    hud.BoostReserve <- scene.BoostReserve
    hud.BoostLocked <- scene.BoostLocked
    hud.HoldStick <- scene.Input.HoldStick
    hud.MatchSpeed <- scene.MatchSpeed

    // Rein optische Querneigung: Das Aussenmodell legt sich in Gierkurven, waehrend
    // Flugphysik, Kamera und Zielhilfe bei der echten Lage bleiben.
    let player = scene.World.Player
    let bank = if camera.Mode = Chase then player.YawRate / System.Math.Max(0.01, player.MaxYawRate) * 0.2 else 0.0
    let hull = if bank <> 0.0 then rotation * Quat.fromAxisAngle Vec3.unitZ bank else rotation
    // Die Welt steht einen Bruchteil eines Physikschritts "vor" dem Darstellungs-
    // zeitpunkt; bewegte Pool-Objekte werden um diese Zeit zurueckgerechnet.
    let backstep = (1.0 - alpha) * PhysicsStep

    if Renderer.resize renderer then
        // Schrift und Zielmarken behalten beim Skalieren der 3D-Szene ihre Schärfe.
        let ratio = min 2.0 (max 1.0 window.devicePixelRatio)
        let width = max 1 (int (hud.Canvas.clientWidth * ratio))
        let height = max 1 (int (hud.Canvas.clientHeight * ratio))
        if hud.Canvas.width <> float width || hud.Canvas.height <> float height || hud.Ratio <> ratio then
            Hud.resize hud width height ratio

    Camera.updateMatrices camera (Renderer.aspect renderer)
    hud.ViewRotation <- Camera.viewRotation camera

    // Lichter: Alle Quellen melden Kandidaten; beginFrame waehlt die wichtigsten aus.
    Lights.clearPoints renderer.Lights
    pushFlashLights scene
    CombatLights.push scene.CombatLights renderer.Lights scene.World.Time
    CombatLights.bolts renderer.Lights scene.World scene.Brains (player.Overdrive > 0.0) (scene.Fleet.Kit = 2 && scene.Sortie.Role <> CapitalFlight.Bomber) backstep

    // GPU-Partikel laufen auf der gezeichneten Weltzeit (steht in der Pause).
    GpuParticles.setClock scene.GpuParticles (scene.World.Time - backstep)
    EngineTrails.frame scene.Trails scene.GpuParticles
    EngineTrails.beltDust scene.Trails scene.GpuParticles scene.World.Asteroids camera.Position

    // Instanzen schreiben
    pushShips scene position hull alpha
    pushParticles scene backstep
    pushLasers scene backstep
    pushEquipmentAndMinerals scene position hull
    pushJumpGates scene position
    pushDust scene
    pushFreighters scene
    pushApproachBuoys scene
    pushCustomsShips scene position hull
    pushCustomsGlow scene

    // Zeichnen: erst deckend, dann additiv
    Renderer.beginFrame renderer camera.View camera.Projection camera.Position
    Frustum.update scene.AsteroidFrustum renderer.ViewProj
    let rocks = scene.AsteroidBatches.[min (scene.AsteroidBatches.Length - 1) (sectorIndex scene)].Value
    pushAsteroids scene rocks backstep

    // Sonnenschatten: alle deckenden Koerper in Spielernaehe in die Tiefenkarte.
    // Im freien Flug eng um das Schiff (scharf); nahe der Station, eines Sprungtors oder des Zollkreuzers weitet sich der Bereich.
    let curSector = currentSector scene
    let stationDistance = Vec3.distance position curSector.Outpost.Position
    let nearStation = 1.0 - System.Math.Max(0.0, System.Math.Min(1.0, (stationDistance - 260.0) / 240.0))
    let nearGate =
        curSector.Gates
        |> Array.fold (fun best gate ->
            let gd = Vec3.distance position gate.Position
            max best (1.0 - System.Math.Max(0.0, System.Math.Min(1.0, (gd - 220.0) / 220.0)))) 0.0
    let nearCruiser =
        if scene.World.Customs.IsSome then
            let cd = Vec3.distance position CapitalFlight.position
            1.0 - System.Math.Max(0.0, System.Math.Min(1.0, (cd - 320.0) / 280.0))
        else 0.0
    let shadowSpan = Layout.ShadowRadius + max (150.0 * nearStation * nearStation) (max (190.0 * nearCruiser * nearCruiser) (140.0 * nearGate * nearGate))
    Renderer.beginShadows renderer position shadowSpan
    for batch in rocks do Renderer.drawShadow renderer batch
    Renderer.drawShadow renderer scene.HullBatches.[scene.Fleet.Active].Value
    for batch in scene.Outfit.Visible do Renderer.drawShadow renderer batch
    for batch in scene.DeckCargoBatches do Renderer.drawShadow renderer batch
    for batch in scene.EnemyBatches do Renderer.drawShadow renderer batch
    Renderer.drawShadow renderer scene.StationBatches.[sectorIndex scene].Value
    drawCustomsShadow scene
    Renderer.drawShadow renderer scene.FreighterBatch
    Renderer.drawShadow renderer scene.Traffic.Hulls
    Renderer.drawShadow renderer scene.GateFrameBatch
    Renderer.drawShadow renderer scene.GateRingBatch
    Renderer.drawShadow renderer scene.TurretBaseBatch
    Renderer.drawShadow renderer scene.TurretBatch
    Renderer.drawShadow renderer scene.MissileBatch
    Renderer.endShadows renderer

    SkyBackdrop.draw renderer scene.Sky camera (sectorIndex scene)

    for batch in rocks do Renderer.draw renderer batch

    Renderer.draw renderer scene.DebrisBatch
    Renderer.draw renderer scene.HullBatches.[scene.Fleet.Active].Value
    for batch in scene.Outfit.Visible do Renderer.draw renderer batch
    for batch in scene.DeckCargoBatches do Renderer.draw renderer batch
    for batch in scene.EnemyBatches do Renderer.draw renderer batch
    let sector = sectorIndex scene
    Renderer.draw renderer scene.StationBatches.[sector].Value
    drawCustomsShips scene
    LandmarkMeshes.drawOpaque renderer scene.Landmarks sector scene.AsteroidFrustum scene.Time
    let origin, relic = scene.RelicBatches.[sector]
    if Frustum.intersectsSphere scene.AsteroidFrustum origin.X origin.Y origin.Z 9.0 then
        Renderer.draw renderer relic.Value
    Renderer.draw renderer scene.FreighterBatch
    Renderer.draw renderer scene.Traffic.Hulls
    Renderer.draw renderer scene.BuoyFrames
    Renderer.draw renderer scene.BuoyLenses
    Renderer.draw renderer scene.GateFrameBatch
    Renderer.draw renderer scene.GateRingBatch
    Renderer.draw renderer scene.CrystalBatch
    Renderer.draw renderer scene.TurretBaseBatch
    Renderer.draw renderer scene.TurretBatch
    Renderer.draw renderer scene.MissileBatch
    Renderer.draw renderer scene.DustBatch
    Renderer.draw renderer scene.Landmarks.Horizon
    Renderer.draw renderer scene.ShieldBatch
    Renderer.draw renderer scene.ExhaustBatch
    Renderer.draw renderer scene.EngineHaloBatch
    Renderer.draw renderer scene.LaserBatch
    Renderer.draw renderer scene.VfxRingBatch
    Renderer.draw renderer scene.VfxStreakBatch
    Renderer.draw renderer scene.GlowBatch
    let v = scene.World.Player.Velocity
    GpuParticles.draw renderer scene.GpuParticles camera.View camera.Fov v.X v.Y v.Z
    Renderer.setEffects renderer scene.World.Player.Boost scene.World.Player.HitFlash
    Renderer.endFrame renderer

    // HUD je nach Game-State
    Hud.clear hud
    let scanElapsed = match scene.ArchiveScan with Some scan -> scan.Elapsed | None -> -1.0

    match scene.State with
    | Menu -> ()
    | Playing ->
        if scene.JumpTransitUntil > scene.Time then
            let progress = 1.0 - (scene.JumpTransitUntil - scene.Time) / JumpFlight.transitDuration
            Hud.drawJumpTransit hud renderer.ViewProj position (Quat.forward rotation) progress
        Hud.drawFlight
            hud
            renderer.ViewProj
            (Renderer.aspect renderer)
            camera.Mode
            camera.OpticsActive
            scene.World
            scene.Brains
            sector
            scene.Pickups
            scanElapsed
            scene.Input
            alpha
            position
            rotation
            scene.Wave
            scene.Score
            scene.EnemiesAlive
            (scene.Sortie.Role <> 0)
            scene.School.Step
        if (scene.Sortie.Role = 0 || scene.Blockade.Lifted) && (scene.School.Step = FlightSchool.Off || scene.School.Step = FlightSchool.Dock) then
            Hud.drawStation hud renderer.ViewProj (Renderer.aspect renderer) (currentSector scene).Outpost position (dockBlocked scene)
            if scene.Trading && scene.School.Step = FlightSchool.Off then Hud.drawJumpGates hud renderer.ViewProj (currentSector scene) position
        if scene.Trading && scene.Sector = Blockade.helios then
            let port = (currentSector scene).Outpost.Position
            let berth = port + Quat.rotateVec3 stationOrientation (Vec3(30.5,-12.0,14.7))
            Hud.drawCustoms hud renderer.ViewProj scene.Blockade scene.Sortie position (CapitalFlight.freightSix port berth scene.Sortie.Landing)
        Hud.drawWorldMarkers hud
    | Paused ->
        Hud.drawFlight
            hud
            renderer.ViewProj
            (Renderer.aspect renderer)
            camera.Mode
            camera.OpticsActive
            scene.World
            scene.Brains
            sector
            scene.Pickups
            scanElapsed
            scene.Input
            alpha
            position
            rotation
            scene.Wave
            scene.Score
            scene.EnemiesAlive
            (scene.Sortie.Role <> 0)
            scene.School.Step

        if (scene.Sortie.Role = 0 || scene.Blockade.Lifted) && (scene.School.Step = FlightSchool.Off || scene.School.Step = FlightSchool.Dock) then
            Hud.drawStation hud renderer.ViewProj (Renderer.aspect renderer) (currentSector scene).Outpost position (dockBlocked scene)
            if scene.Trading && scene.School.Step = FlightSchool.Off then Hud.drawJumpGates hud renderer.ViewProj (currentSector scene) position
        if scene.Trading && scene.Sector = Blockade.helios then
            let port = (currentSector scene).Outpost.Position
            let berth = port + Quat.rotateVec3 stationOrientation (Vec3(30.5,-12.0,14.7))
            Hud.drawCustoms hud renderer.ViewProj scene.Blockade scene.Sortie position (CapitalFlight.freightSix port berth scene.Sortie.Landing)
        Hud.drawWorldMarkers hud
        Hud.drawPaused hud (Input.mouseSensitivityPercent scene.Input)
    | Refitting -> () // Die bebilderte, klickbare Werft liegt als HTML über der Szene.
    | Victory
    | GameOver -> Hud.drawGameOver hud

    if scene.ShowHelp && (scene.State = Playing || scene.State = Paused) then
        Hud.drawHelp hud

// -------------------------------------------------------------------------------------
//  Schleife
// -------------------------------------------------------------------------------------

let private initializeFlight () : Scene option =
    try
        let scene = createScene ()
        refreshSaveSlots scene
        scene.Camera.Zoom <- System.Math.Max(0.6, System.Math.Min(2.2, loadSetting ZoomKey 1.0))
        let menu = document.getElementById "flight-menu"
        setState scene States.menu
        scene.Input.UiMode <- true
        (document.getElementById "menu-launch").addEventListener ("click", fun _ -> scene.Input.Clicked <- true)
        (document.getElementById "school-skip").addEventListener ("click", fun _ -> scene.Input.Pressed.[int GameAction.LeaveSchool] <- 1uy)
        (document.getElementById "school-replay").addEventListener ("click", fun _ ->
            if scene.State = Refitting && canTrain scene then
                departShop scene
                Input.reset scene.Input
                beginSchool scene)
        (document.getElementById "menu-combat").addEventListener ("click", fun _ -> scene.Input.Pressed.[int GameAction.StartCombat] <- 1uy)
        (document.getElementById "open-star-map").addEventListener ("click", fun _ -> openStarMap scene)
        (document.getElementById "menu-saves").addEventListener ("click", fun _ -> scene.Saves.Action <- "open")
        // Mausempfindlichkeit schon im Hauptmenue: startet mit dem Wert des juengsten
        // Spielstands; ein bewegter Regler gilt danach auch beim Fortsetzen.
        let slider = document.getElementById "menu-sensitivity"
        let sliderValue = document.getElementById "menu-sensitivity-value"
        scene.ResumeJson |> Option.iter (fun json ->
            match GameSave.decode json with
            | Ok data -> scene.Input.MouseSensitivity <- data.Settings.Sensitivity
            | Error _ -> ())
        let showSensitivity () =
            let percent = Input.mouseSensitivityPercent scene.Input
            slider?value <- string percent
            sliderValue.textContent <- string percent + " %"
        showSensitivity ()
        slider.addEventListener ("input", fun _ ->
            Input.setMouseSensitivityPercent scene.Input (int (float (string slider?value)))
            scene.SensitivityOverride <- true
            showSensitivity ())
        let audioPrefixes = [| "audio-"; "save-audio-" |]
        let audioChannels = [| "master"; "sfx"; "radio"; "ambience" |]
        let syncAudioUi () =
            let vols = Sound.getVolumes scene.Sound
            for prefix in audioPrefixes do
                for i in 0 .. audioChannels.Length - 1 do
                    let pct = int (round (vols.[i] * 100.0))
                    let el = document.getElementById (prefix + audioChannels.[i])
                    let outEl = document.getElementById (prefix + audioChannels.[i] + "-value")
                    if not (isNull el) then el?value <- string pct
                    if not (isNull outEl) then outEl.textContent <- string pct + " %"
        syncAudioUi ()
        for prefix in audioPrefixes do
            for i in 0 .. audioChannels.Length - 1 do
                let el = document.getElementById (prefix + audioChannels.[i])
                if not (isNull el) then
                    el.addEventListener ("input", fun _ ->
                        let vols = Sound.getVolumes scene.Sound
                        vols.[i] <- System.Math.Max(0.0, System.Math.Min(1.0, float (string el?value) / 100.0))
                        Sound.setVolumes scene.Sound vols.[0] vols.[1] vols.[2] vols.[3]
                        syncAudioUi ())
            let testBtn = document.getElementById (prefix + "test")
            if not (isNull testBtn) then
                testBtn.addEventListener ("click", fun _ ->
                    Sound.unlock scene.Sound
                    Sound.tradeConfirm scene.Sound)
        menu.addEventListener ("keydown", fun e ->
            let key = unbox<KeyboardEvent> e
            if key.code = "Enter" || key.code = "Space" then e.stopPropagation())
        let flush () =
            if scene.SaveRetryDelay <= 0.0 && (scene.FlightChanged || scene.SaveDirty) then saveFlight scene "auto"
        window.addEventListener("pagehide", fun _ -> flush ())
        document.addEventListener("visibilitychange", fun _ ->
            if document?visibilityState = "hidden" then
                DockContact.leave scene.Contact
                flush ()
            elif scene.State = Refitting && not scene.Saves.Visible && not (StarMap.visible ()) then
                DockContact.dock scene.Contact (currentSector scene).Outpost.Id scene.Pilot)
        Some scene
    with ex ->
        // Klartext statt schwarzem Bildschirm, falls WebGL2 fehlt oder ein Shader bricht.
        let box = document.getElementById "error"
        box.textContent <- ex.Message
        box.removeAttribute "hidden"
        console.error ex
        None

let private scene =
    if Bench.requested () then
        try Bench.start (unbox<HTMLCanvasElement> (document.getElementById "gl-canvas"))
        with ex ->
            let box = document.getElementById "error"
            box.textContent <- ex.Message
            box.removeAttribute "hidden"
            console.error ex
        None
    else initializeFlight ()

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

    if not scene.ShowStats then
        scene.FrameCounter <- 0
        scene.StatTimer <- 0.0
    elif scene.StatTimer >= 0.25 then
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
             + " Instanzen   "
             + GraphicsQuality.label scene.Graphics.Tier
             + " " + string (int (round (renderer.RenderScale * 100.0))) + " %   "
             + (if renderer.Post.Msaa then "MSAA " + string renderer.Post.MsaaSamples + "x" else "MSAA aus")
             + (if renderer.ShadowsOn then "   Schatten an" else "   Schatten aus"))

let private frame (timestamp: float) =
    match scene with
    | Some scene ->
        let input = scene.Input
        let world = scene.World
        let ship = world.Player

        // dt deckeln, damit ein Tab-Wechsel keinen Zeitsprung erzeugt
        let elapsed =
            if scene.LastTimestamp < 0.0 then
                0.0
            else
                System.Math.Max(0.0, (timestamp - scene.LastTimestamp) / 1000.0)
        let dt = System.Math.Min(0.1, elapsed)

        scene.LastTimestamp <- timestamp
        if Input.wasPressed input GameAction.OpenStarMap then openStarMap scene
        let mapVisible = StarMap.visible ()
        if not mapVisible then scene.StateTime <- scene.StateTime + dt

        // ---- 1. Eingabe-Flanken: gehoeren zum Frame, nicht zum Physikschritt ----
        let confirm = Input.wasPressed input GameAction.Confirm || input.Clicked

        let saveUiHandled = mapVisible || processSaveUi scene
        match scene.State with
        | _ when saveUiHandled -> ()
        | Menu ->
            if Input.wasPressed input GameAction.StartCombat then handleSaveAction scene "newcombat" "" false
            elif confirm then
                match scene.ResumeJson with
                | Some json -> loadFlight scene json
                | None -> chooseTradeShip scene
        | Playing ->
            if ship.Alive then
                let wheel = Input.takeWheelThrottle input
                if wheel <> 0.0 then
                    ship.Controls.Throttle <- System.Math.Max(0.0, System.Math.Min(1.0, ship.Controls.Throttle + wheel))
                let full = Input.wasPressed input GameAction.ThrottleFull
                let zero = Input.wasPressed input GameAction.ThrottleZero
                if full then ship.Controls.Throttle <- 1.0
                if zero then ship.Controls.Throttle <- 0.0

                // Tempo angleichen: Der Schub folgt dem erfassten Ziel, bis der Pilot
                // selbst regelt oder das Ziel verschwindet.
                let target = scene.Hud.Target
                let hasTarget = target >= 0 && target < world.Enemies.Length && world.Enemies.[target].Alive
                if Input.wasPressed input GameAction.MatchSpeed then
                    if scene.MatchSpeed then
                        scene.MatchSpeed <- false
                        Sound.uiClick scene.Sound
                        Hud.notify scene.Hud "TEMPO FREI" "Der Schub bleibt auf dem letzten Wert stehen." 2.0
                    elif hasTarget then
                        scene.MatchSpeed <- true
                        Sound.uiClick scene.Sound
                        Hud.notify scene.Hud "TEMPO ANGEGLICHEN" "Der Schub folgt dem erfassten Ziel. Jede eigene Schubeingabe löst die Kopplung." 3.0
                    else
                        Sound.uiError scene.Sound
                        Hud.notify scene.Hud "KEIN ZIEL" "Tempo angleichen braucht ein erfasstes Ziel." 2.0
                elif scene.MatchSpeed then
                    let manual = wheel <> 0.0 || full || zero || Input.throttleChange input <> 0.0 || Input.isDown input GameAction.Brake
                    if manual || not hasTarget then
                        scene.MatchSpeed <- false
                    else
                        ship.Controls.Throttle <- System.Math.Min(1.0, Vec3.length world.Enemies.[target].Velocity / ship.CruiseSpeed)
            else
                scene.MatchSpeed <- false

            // Esc gibt die Maus frei, Alt+Tab nimmt den Fokus: beides haelt den Flug an.
            if Input.wasPressed input GameAction.Pause || input.FocusLost then
                scene.SaveDirty <- true
                Sound.uiClick scene.Sound
                setState scene States.paused
            elif scene.School.Step <> FlightSchool.Off && Input.wasPressed input GameAction.LeaveSchool then
                endSchool scene
                Hud.notify scene.Hud "KESTREL · FREIER FLUG" "Am Handelsdock kannst du die Flugschule erneut starten." 3.0
            elif Input.wasPressed input GameAction.OpenShop && ship.Alive then
                if scene.Sortie.Role <> 0 then
                    recoverSortie scene (if scene.Blockade.Lifted then "Orlov hat die erste Lieferung quittiert. Der Verkehr nach Helios läuft wieder." else "Zurück in Helios. Der Bomber wird neu bestückt. Bereits getroffene Ziele bleiben beschädigt.")
                else openShop scene
            elif ship.Alive && Input.wasPressed input GameAction.GhostPulse && PilotSkills.pulse scene.Pilot then
                scene.SaveDirty <- true
                Sound.ecmJam scene.Sound
                Hud.notify scene.Hud "GEISTER-PROTOKOLL" "Drohnenfeuer für 3 s gestört · Feindliche Schildregeneration für 8 s blockiert." 3.0
        | Paused ->
            Input.takeWheelThrottle input |> ignore
            if Input.wasPressed input GameAction.Pause || Input.wasPressed input GameAction.Confirm || input.Clicked then
                Sound.uiClick scene.Sound
                setState scene States.playing
        | Refitting ->
            if scene.Shop.TabChanged then
                scene.Shop.TabChanged <- false
                Sound.uiClick scene.Sound
            match scene.Shop.FreightDesk.Request with
            | Some action ->
                scene.Shop.FreightDesk.Request <- None
                handleFreight scene action
            | None -> ()
            match scene.Shop.Shipyard.Request with
            | Some order ->
                scene.Shop.Shipyard.Request <- None
                refitShip scene order
            | None -> ()
            match scene.Skills.Request with
            | Some (branch, rank) ->
                scene.Skills.Request <- None
                learnSkill scene branch rank
            | None -> ()
            let purchase =
                if Input.wasPressed input GameAction.BuyTurret then 0
                elif Input.wasPressed input GameAction.UpgradeWeapon then 1
                elif Input.wasPressed input GameAction.UpgradeShield then 2
                elif Input.wasPressed input GameAction.UpgradeHull then 3
                else scene.Shop.Purchase
            scene.Shop.Purchase <- -1
            if purchase >= 0 then buyEquipment scene purchase
            let tradeAction = scene.Shop.TradeAction
            scene.Shop.TradeAction <- -1
            if tradeAction >= 0 then trade scene tradeAction
            let repairAction = scene.Shop.RepairAction
            scene.Shop.RepairAction <- -1
            if repairAction > 0 then repairHull scene repairAction
            let travelAction = scene.Shop.TravelAction
            scene.Shop.TravelAction <- -1
            if scene.Yard.Request.IsSome then
                let request = scene.Yard.Request.Value
                scene.Yard.Request <- None
                yardAction scene request
            elif travelAction >= 0 then travel scene travelAction
            elif scene.Shop.Advance then
                scene.Shop.Advance <- false
                advanceSector scene
            elif scene.Shop.Depart || Input.wasPressed input GameAction.Confirm || Input.wasPressed input GameAction.OpenShop then
                scene.Shop.Depart <- false
                departShop scene
        | Victory ->
            if confirm && scene.StateTime > 1.0 then openSaves scene
        | GameOver ->
            // Kurze Sperre, damit ein noch gedrueckter Feuerknopf nicht sofort neu startet
            if confirm && scene.StateTime > 1.0 then
                match (readSlot "auto").Json |> Option.orElse scene.ResumeJson with
                | Some json -> loadFlight scene json
                | None when scene.Trading -> chooseTradeShip scene
                | None -> startGame scene false 0

        if not saveUiHandled && scene.State = Playing && ship.Alive && Input.wasPressed input GameAction.ToggleOptics then
            scene.Camera.OpticsActive <- not scene.Camera.OpticsActive
            Sound.uiClick scene.Sound

        if not saveUiHandled && Input.wasPressed input GameAction.ToggleCamera then
            Camera.toggleMode scene.Camera
            Sound.uiClick scene.Sound
            scene.SaveDirty <- canSave scene

        if not saveUiHandled && (scene.State = Playing || scene.State = Paused) then
            if Input.wasPressed input GameAction.ToggleMouseMode then
                Input.toggleMouseMode input
                Sound.uiClick scene.Sound
                if scene.State = Playing then
                    if input.HoldStick then
                        Hud.notify scene.Hud "MAUS · HALTEN" "Der Stick bleibt stehen. Maus zur Mitte zurückführen oder X zum Zentrieren." 3.0
                    else
                        Hud.notify scene.Hud "MAUS · WISCHEN" "Der Stick federt nach jeder Bewegung zur Mitte zurück." 3.0
            if Input.wasPressed input GameAction.ToggleStats then
                scene.ShowStats <- not scene.ShowStats
                if not scene.ShowStats then Hud.setStats scene.Hud ""
            if Input.wasPressed input GameAction.ToggleHelp then
                scene.ShowHelp <- not scene.ShowHelp
            if Input.wasPressed input GameAction.ToggleShadows then
                let on = scene.Graphics.ToggleShadows()
                if scene.State = Playing then
                    Hud.notify scene.Hud (if on then "SCHATTEN AN" else "SCHATTEN AUS") "F4 schaltet die Sonnenschatten um." 2.0
            // Kameraabstand der Verfolgerperspektive in Stufen
            if Input.wasPressed input GameAction.ZoomIn then
                scene.Camera.Zoom <- System.Math.Max(0.6, scene.Camera.Zoom - 0.2)
                saveSetting ZoomKey scene.Camera.Zoom
            if Input.wasPressed input GameAction.ZoomOut then
                scene.Camera.Zoom <- System.Math.Min(2.2, scene.Camera.Zoom + 0.2)
                saveSetting ZoomKey scene.Camera.Zoom
            let slower = Input.wasPressed input GameAction.MouseSlower
            let faster = Input.wasPressed input GameAction.MouseFaster
            if slower <> faster then
                Input.adjustMouseSensitivity input faster
                Sound.uiClick scene.Sound
                scene.SaveDirty <- canSave scene || scene.State = Paused
                if scene.State = Playing then
                    Hud.notify scene.Hud "MAUS · EMPFINDLICHKEIT" (string (Input.mouseSensitivityPercent input) + " % (Komma / Punkt)") 1.6

        if not saveUiHandled && Input.wasPressed input GameAction.InvertPitch then
            input.InvertPitch <- not input.InvertPitch
            scene.SaveDirty <- canSave scene

        if not saveUiHandled && Input.wasPressed input GameAction.ToggleMute then
            Sound.toggleMute scene.Sound
            DockContact.onMuteChanged scene.Contact
            scene.Hud.Muted <- scene.Sound.Muted
            scene.SaveDirty <- canSave scene

        if not saveUiHandled && Input.wasPressed input GameAction.CycleTarget then
            if Hud.cycleTarget scene.Hud world.Enemies then
                Sound.targetLock scene.Sound

        if not saveUiHandled && scene.State = Playing && Input.wasPressed input GameAction.TargetNearest then
            if Hud.targetNearest scene.Hud world.Enemies ship.Position then
                Sound.targetLock scene.Sound

        // Kontinuierlicher Triebwerksklang, Sektor-Ambience & 3D-Hoerer im aktiven Flug
        let engineActive =
            match scene.State with
            | Playing -> ship.Alive
            | _ -> false

        let curSector = currentSector scene
        let mutable gateProx = 0.0
        if engineActive && scene.Trading && scene.School.Step = FlightSchool.Off then
            for gate in curSector.Gates do
                gateProx <- max gateProx (Universe.gateProximity gate ship.Position)
            if gateProx <= 0.3 then scene.JumpNotifiedSector <- -1
            if gateProx > 0.45 && scene.Sortie.Role = 0 && scene.JumpNotifiedSector <> sectorIndex scene then
                scene.JumpNotifiedSector <- sectorIndex scene
                Sound.jumpReady scene.Sound

        if engineActive then
            if not scene.PrevBoostLocked && scene.BoostLocked then Sound.boostEmpty scene.Sound
            elif scene.PrevBoostLocked && not scene.BoostLocked then Sound.boostReady scene.Sound
            scene.PrevBoostLocked <- scene.BoostLocked
            if scene.PrevOverdrive <= 0.0 && ship.Overdrive > 0.0 then Sound.overdriveOn scene.Sound
            scene.PrevOverdrive <- ship.Overdrive
            let shieldUp = ship.Shield > 0.0
            if scene.PrevShieldUp && not shieldUp then Sound.shieldBreak scene.Sound
            if scene.PrevShieldDelay > 0.0 && ship.ShieldDelay <= 0.0 && ship.Shield < ship.MaxShield then
                Sound.shieldRecharge scene.Sound
            scene.PrevShieldUp <- shieldUp
            scene.PrevShieldDelay <- ship.ShieldDelay
            if scene.Trading && not scene.UpgradeNotified && not scene.Wallet.TurretOwned && scene.Wallet.Credits >= 350 && scene.School.Step = FlightSchool.Off then
                scene.UpgradeNotified <- true
                Sound.upgradeReady scene.Sound
        else
            scene.PrevBoostLocked <- scene.BoostLocked
            scene.PrevOverdrive <- ship.Overdrive
            scene.PrevShieldUp <- ship.Shield > 0.0
            scene.PrevShieldDelay <- ship.ShieldDelay

        let outpostKind =
            match curSector.Outpost.Kind with
            | MiningDepot -> 0 | Refinery -> 1 | OrbitalYard -> 2
            | ResearchRelay -> 3 | FrontierBazaar -> 4 | FleetDock -> 5
        Sound.setListener scene.Sound scene.Camera.Position scene.Camera.Rotation
        Sound.setFlightContext scene.Sound scene.Fleet.Active scene.Fleet.Kit (sectorIndex scene) (scene.Camera.Mode = Cockpit) outpostKind gateProx (scene.World.Customs.IsSome)
        let sceneMode =
            match scene.State with
            | Playing when mapVisible -> 2
            | Playing -> 1
            | Paused -> 2
            | Refitting -> 3
            | Menu | Victory | GameOver -> 0
        Sound.setSceneMode scene.Sound sceneMode
        let thrust = if ship.Controls.Brake then 0.0 else ship.Controls.Throttle
        Sound.updateEngine scene.Sound (engineActive && not mapVisible) thrust ship.Boost

        // ---- 2. Simulation mit festem Zeitschritt (steht in der Pause) ----
        let simulate =
            match scene.State with
            | _ when scene.Saves.Visible || mapVisible -> false
            | Menu | Paused | Refitting | Victory -> false
            | _ -> true

        if simulate then
            scene.Accumulator <- scene.Accumulator + dt
            let mutable steps = 0

            while scene.Accumulator >= PhysicsStep && steps < MaxStepsPerFrame do
                scene.Time <- scene.Time + PhysicsStep
                match scene.State with
                | Playing ->
                    if ship.Alive then
                        applyPlayerInput scene input ship PhysicsStep
                | _ -> ()

                if FlightSchool.onRange scene.School.Step then
                    FlightSchool.guideTarget scene.School.Step world.Time world.Enemies.[0]
                else AI.update world scene.Brains PhysicsStep
                if scene.State = Playing && not (FlightSchool.onRange scene.School.Step) then
                    Turret.update world scene.Turret (scene.Wallet.TurretOwned && scene.Sortie.Role <> CapitalFlight.Bomber) PhysicsStep
                    if scene.Sortie.Role <> CapitalFlight.Bomber && scene.School.Step = FlightSchool.Off && Input.wasPressed input GameAction.LaunchTorpedo then
                        match Missiles.tryLaunch world scene.Missiles scene.Hud.Target with
                        | Ok _ ->
                            Sound.torpedoLaunch scene.Sound
                            scene.Hud.RewardText <- "ABFANGRAKETE GESTARTET"
                            scene.Hud.RewardTime <- 1.4
                        | Error msg when msg <> "" ->
                            Sound.uiError scene.Sound
                            scene.Hud.RewardText <- msg
                            scene.Hud.RewardTime <- 1.8
                        | Error _ -> ()
                    let impacts = Missiles.update world scene.Missiles PhysicsStep
                    for impact in impacts do
                        if impact.HitEnemy then
                            Sound.torpedoImpact scene.Sound
                            Hud.confirmHit scene.Hud false
                        else
                            Sound.explosionAt scene.Sound 0.9 impact.Position
                        CombatLights.explosion scene.CombatLights world.Time impact.Position 24.0
                    scene.Hud.MissileAmmo <- scene.Missiles.Ammo
                    scene.Hud.MissileReload <- scene.Missiles.ReloadTimer / Missiles.ReloadInterval
                Physics.step world PhysicsStep
                updateGateCrossing scene
                if scene.State = Playing && scene.Sortie.Role <> CapitalFlight.Bomber then
                    let radius = if PilotSkills.level scene.Pilot 0 >= 1 then 85.0 else Salvage.MagnetRadius
                    let collected = Salvage.collectCargo scene.Pickups ship scene.Cargo radius PhysicsStep
                    let goods =
                        [ for i in 0 .. collected.Length - 1 do
                            if collected.[i] > 0 then
                                yield "+" + string collected.[i] + " " + TradeSystem.commodities.[i].Name.ToUpperInvariant() ]
                    if not (List.isEmpty goods) then
                        scene.Hud.RewardText <- String.concat " · " goods
                        scene.Hud.RewardTime <- 2.5
                        Sound.cargoPickup scene.Sound
                scene.Accumulator <- scene.Accumulator - PhysicsStep
                steps <- steps + 1

            if scene.Accumulator >= PhysicsStep then
                scene.Accumulator <- scene.Accumulator % PhysicsStep

            // ---- 3. + 4. Ereignisse und Wave-System ----
            let simulatedTime = float steps * PhysicsStep
            processEvents scene
            updateWaves scene simulatedTime
            if scene.State = Playing then updateSortie scene simulatedTime
            if scene.State = Playing && scene.School.Step <> FlightSchool.Off then
                if ship.Alive then advanceSchool scene
            elif scene.State = Playing then updateArchiveScan scene simulatedTime

            if scene.State = Playing && ship.Alive && (scene.School.Step = FlightSchool.Off || scene.School.Step = FlightSchool.Dock) then
                let outpost = (currentSector scene).Outpost
                match Universe.evaluateDocking outpost ship.Position (dockBlocked scene) with
                | Approaching _ -> Sound.stationComm scene.Sound outpost.Id 0
                | ReadyToDock _ -> Sound.stationComm scene.Sound outpost.Id 1
                | BlockedByCombat _ -> Sound.stationComm scene.Sound outpost.Id 2
                | TooFar _ -> Sound.resetStationComm scene.Sound outpost.Id
            Hud.update scene.Hud simulatedTime
            if canSave scene then scene.FlightChanged <- true

        scene.SaveRetryDelay <- max 0.0 (scene.SaveRetryDelay - elapsed)
        if scene.State = Refitting && not scene.Saves.Visible && not mapVisible then
            let before = DockContact.heard scene.Contact
            DockContact.update scene.Contact
            if before <> DockContact.heard scene.Contact then scene.SaveDirty <- true
        if canSave scene then
            scene.SaveTimer <- scene.SaveTimer + elapsed
            if scene.SaveRetryDelay <= 0.0 && (scene.SaveDirty || (scene.SaveTimer >= 20.0 && scene.FlightChanged)) then
                saveFlight scene "auto"

        // Menüs bleiben animiert; Kampfzeit und Aufrüstungswahl bleiben eingefroren.
        if not simulate then
            scene.Hud.Time <- scene.Hud.Time + dt

        // ---- 5. Darstellung zwischen letztem und aktuellem Physikzustand interpolieren ----
        let alpha = scene.Accumulator / PhysicsStep
        let position = Vec3.lerp ship.PrevPosition ship.Position alpha
        let rotation = Quat.slerp ship.PrevRotation ship.Rotation alpha

        if simulate then
            // Heckblick (H halten): Die Kamera dreht sich um die Hochachse des Schiffs.
            let lookBack = scene.State = Playing && ship.Alive && Input.isDown input GameAction.LookBack
            let behind = if lookBack then rotation * Quat.fromAxisAngle Vec3.unitY System.Math.PI else rotation
            // Freies Umsehen (mittlere Maustaste): Blick um Hoch- und Querachse des Schiffs.
            Input.relaxLook input dt
            let viewRotation =
                if input.LookX <> 0.0 || input.LookY <> 0.0 then
                    behind * Quat.fromAxisAngle Vec3.unitY (-input.LookX) * Quat.fromAxisAngle Vec3.unitX (-input.LookY)
                else
                    behind
            if lookBack <> scene.LookBack then
                scene.LookBack <- lookBack
                Camera.snap scene.Camera position viewRotation
            // Fahrtgefuehl: Abstand/FOV folgen dem Tempo verzoegert, in Gierkurven legt
            // sich das Bild leicht mit (nicht beim Heckblick oder freien Umsehen).
            let forwardSpeed = if ship.Alive then Vec3.dot ship.Velocity (Quat.forward rotation) else 0.0
            let turn =
                if lookBack || input.LookX <> 0.0 || input.LookY <> 0.0 || not ship.Alive then 0.0
                else ship.YawRate / System.Math.Max(0.01, ship.MaxYawRate)
            Camera.feel scene.Camera (forwardSpeed / System.Math.Max(1.0, ship.CruiseSpeed)) turn dt
            Camera.update scene.Camera position viewRotation ship.Boost dt
            if scene.Camera.Mode = Chase then keepCameraOutOfRock scene

            // Kamerawackeln als kleine Drehung: klingt exponentiell ab und haeuft sich
            // nicht auf, weil es nur in die Darstellung (View) eingeht.
            if scene.Shake > 0.01 then
                scene.Camera.Jolt <- Camera.shakeRotation scene.Shake scene.Time
                scene.Shake <- scene.Shake * exp (-7.0 * dt)
            else
                scene.Shake <- 0.0
                scene.Camera.Jolt <- Quat.identity

        scene.Graphics.Frame((scene.State = Playing), elapsed * 1000.0)
        // Qualitaetsstufe -> Partikel- und Lichtbudget (reine Zuweisungen).
        GpuParticles.setBudget scene.GpuParticles scene.Graphics.Preset.Particles
        Lights.setBudget scene.Renderer.Lights scene.Graphics.Preset.PointLights

        updateStats scene elapsed
        let introActive = scene.State = Menu && not (isNull (window?introCam)) && unbox<bool> (window?introCam?active)
        if introActive then
            let t = unbox<float> (window?introCam?time)
            let shot = if isNull (window?introCam?shot) then 1 else unbox<int> (window?introCam?shot)
            let target, eye =
                match shot with
                | 0 ->
                    // Weiter 3D-Anflug auf Bergbau-Depot Kestrel (Station, Bojen, Verkehr)
                    let port = (currentSector scene).Outpost.Position
                    let p = port + Vec3(cos (t * 0.16 - 0.5) * 210.0, 52.0 + 18.0 * sin (t * 0.22), sin (t * 0.16 - 0.5) * 210.0 + 90.0)
                    port, p
                | 2 ->
                    // Finale: weite Hero-Umkreisung vom Schiff in den beleuchteten Sektor
                    let angle = t * 0.18 + 0.65
                    let radius = 22.0 + 6.0 * sin (t * 0.25)
                    position, position + Vec3(cos angle * radius, 6.5 + 2.5 * cos (t * 0.28), sin angle * radius)
                | _ ->
                    // Nahaufnahme um den Bergungskutter fuer den 3D-Freeze-Frame
                    let angle = t * 0.26 - 0.95
                    let radius = 13.5 + 2.5 * sin (t * 0.45)
                    position, position + Vec3(cos angle * radius, 2.4 + 1.6 * cos (t * 0.34), sin angle * radius)
            scene.Camera.Position <- eye
            scene.Camera.Rotation <- Quat.lookRotation (target - eye) Vec3.unitY
            scene.Camera.Fov <- scene.Camera.BaseFov
            scene.Camera.Jolt <- Quat.identity
            if not (unbox<bool> (window?introCam?freeze)) then scene.Time <- scene.Time + dt
            render scene position rotation alpha
        elif scene.State <> Menu then
            render scene position rotation alpha
        Input.endFrame input
        window.requestAnimationFrame loop.Callback |> ignore
    | None -> ()

if Option.isSome scene then
    loop.Callback <- frame
    window.requestAnimationFrame loop.Callback |> ignore
