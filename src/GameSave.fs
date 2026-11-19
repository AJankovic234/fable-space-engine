namespace SpaceEngine

open Fable.Core

/// Frühere Spielstände können ein begonnenes Bergemanöver enthalten.
type SavedRecovery = { Latched: bool; Progress: float }

type SavedRun =
    { Trading: bool; SectorId: string; Wave: int; Phase: int; PhaseTimer: float
      Spawned: int; SpawnTimer: float; Patrol: int[]; Score: int; Kills: int
      Profit: float; Trips: int; Docked: bool; Completed: bool; Combo: int; ComboTimer: float
      HeardContacts: int option; Recovery: SavedRecovery option; School: FlightSchoolState option
      Blockade: BlockadeState option; Sortie: CapitalFlightState option; Fleet: HangarFleet option }

type SavedShip =
    { Position: float[]; Rotation: float[]; Velocity: float[]
      /// Pitch-/Yaw-/Rollrate, Boost.
      Motion: float[]
      /// Hülle, Schild, Schildverzögerung, Waffencooldown, Overdrive.
      Status: float[]
      /// Pitch, Yaw, Roll, Schub, Boosttaste, Bremse, Feuer.
      Controls: float[] }

type SavedBrain =
    { State: int; Timing: float[]; Firing: bool
      Home: float[]; Waypoint: float[]; Evade: float[]; Aim: float[] }

type SavedEnemy = { Index: int; Role: int; Ship: SavedShip; Brain: SavedBrain }
type SavedMarket = { SectorId: string; Stocks: int[] }
type SavedField = { Data: float[]; Variants: int[]; Ore: int[] }
type SavedLasers = { Data: float[]; Teams: int[]; Piercing: int[] option }
type SavedPickup = { Position: float[]; Velocity: float[]; Kind: int; Quantity: int; Life: float }
type SavedTurret = { Rotation: float[]; Cooldown: float; Target: int; LastBarrel: int }
type SavedSettings = { Camera: int; Muted: bool; InvertPitch: bool; Sensitivity: float }
type SaveData =
    { Version: int; SavedAt: float; Run: SavedRun; Wallet: Wallet; Cargo: CargoHold
      Markets: SavedMarket[]; Upgrades: int[]; Player: SavedShip; Enemies: SavedEnemy[]
      Asteroids: SavedField; Lasers: SavedLasers; Pickups: SavedPickup[]; Turret: SavedTurret
      WorldTime: float; RandomState: float; Settings: SavedSettings; Pilot: PilotProgress option }

[<RequireQualifiedAccess>]
module GameSave =
    [<Emit("JSON.stringify($0, (_, v) => ArrayBuffer.isView(v) ? Array.from(v) : v)")>]
    let encode (data: SaveData) : string = jsNative
    [<Emit("JSON.parse($0)")>]
    let private parse (json: string) : SaveData = jsNative
    [<Emit("typeof $0 === 'number' && Number.isFinite($0)")>]
    let private number (value: obj) : bool = jsNative
    [<Emit("Number.isInteger($0)")>]
    let private integer (value: obj) : bool = jsNative
    [<Emit("typeof $0 === 'boolean'")>]
    let private boolean (value: obj) : bool = jsNative
    [<Emit("typeof $0 === 'string'")>]
    let private text (value: obj) : bool = jsNative
    [<Emit("Array.isArray($0)")>]
    let private array (value: obj) : bool = jsNative

    let private range lo hi v = number (box v) && v >= lo && v <= hi
    let private intRange lo hi v = integer (box v) && v >= lo && v <= hi
    let private items maxCount predicate (values: 'T[]) =
        array (box values) && values.Length <= maxCount && Array.forall predicate values
    let private numbers count lo hi values = items count (range lo hi) values && values.Length = count
    let private vec values = numbers 3 -1e8 1e8 values
    let private quat values = numbers 4 -1.0 1.0 values && let n = Array.sumBy (fun v -> v*v) values in n > 0.98 && n < 1.02
    let private bool v = boolean (box v)
    let private ship hull shield (s: SavedShip) =
        vec s.Position && quat s.Rotation && numbers 3 -1e6 1e6 s.Velocity
        && numbers 4 -20.0 20.0 s.Motion && range 0.0 1.0 s.Motion.[3]
        && numbers 5 0.0 1e5 s.Status && range 0.000001 hull s.Status.[0]
        && range 0.0 shield s.Status.[1]
        && numbers 7 -1.0 1.0 s.Controls && range 0.0 1.0 s.Controls.[3]
        && (s.Controls.[4..6] |> Array.forall (fun v -> v = 0.0 || v = 1.0))
    let private brain (b: SavedBrain) =
        intRange 0 3 b.State && numbers 6 -10.0 1e9 b.Timing && range 0.0 1.0 b.Timing.[5]
        && bool b.Firing && vec b.Home && vec b.Waypoint && vec b.Evade && vec b.Aim

    let private validPilot p =
        items 3 (intRange 0 3) p.Levels && p.Levels.Length = 3
        && intRange 1 63 p.Visited && PilotSkills.contains p.Visited 0
        && intRange 0 63 p.Relics && (p.Relics &&& p.Visited) = p.Relics
        && intRange 0 63 p.Rescues && (p.Rescues &&& p.Visited) = p.Rescues
        && (p.Levels.[2] = 3 || p.Rescues = 0) && PilotSkills.points p >= 0
        && range 0.0 45.0 p.PulseCooldown && range 0.0 (min 8.0 p.PulseCooldown) p.PulseTime
        && (p.Levels.[0] = 3 || p.PulseCooldown = 0.0)
        && range 0.0 25.0 p.BypassCooldown && (p.Levels.[2] >= 2 || p.BypassCooldown = 0.0)

    /// Alle untrusted JSON-Felder prüfen, bevor irgendein Live-Zustand geändert wird.
    let private valid (s: SaveData) =
        let r = s.Run
        s.Version = 1 && integer (box s.Version) && range 0.0 9e15 s.SavedAt
        && bool r.Trading && text (box r.SectorId) && (Universe.tryFindSector r.SectorId |> Option.isSome)
        && intRange 0 6 r.Wave && intRange 0 2 r.Phase && range -1.0 120.0 r.PhaseTimer
        && intRange 0 14 r.Spawned && range -1.0 60.0 r.SpawnTimer
        && items 4 (intRange 0 4) r.Patrol && intRange 0 1000000000 r.Score && intRange 0 10000000 r.Kills
        && range -1e9 1e9 r.Profit && intRange 0 10000000 r.Trips && bool r.Docked && bool r.Completed
        && intRange 1 4 r.Combo && range 0.0 8.01 r.ComboTimer
        && Option.forall (intRange 0 StoryRadio.heardMask) r.HeardContacts
        && (r.School |> Option.forall (fun school ->
            intRange 0 FlightSchool.Dock school.Step && intRange 0 2 school.PaidTargets && r.Trading &&
            (school.Step = FlightSchool.Off ||
             (not r.Docked && r.SectorId = Universe.sectors.[0].Id &&
              (if FlightSchool.onRange school.Step then
                   r.Phase = 1 && r.Spawned = 1 && r.Patrol.Length = 1 && r.Patrol.[0] = int EnemyRole.Drone &&
                   s.Enemies.Length = 1 && (school.Step <> FlightSchool.MovingTarget || school.PaidTargets >= 1)
               else r.Phase = 2 && r.Spawned = 0 && r.Patrol.Length = 0 && s.Enemies.Length = 0)))))
        && (r.Blockade.IsSome = r.Sortie.IsSome)
        && (r.Blockade |> Option.forall (fun state -> r.Trading && Blockade.valid state && state.Started))
        && (r.Sortie |> Option.forall (fun f ->
            r.Trading && r.Blockade.IsSome && intRange 0 2 f.Role && intRange 0 2 f.Hits && intRange 0 3 f.Defeated
            && numbers 2 0.0 32.0 f.Guns && intRange 0 2 f.Ammo && range 0.0 CapitalFlight.lockTime f.Lock
            && range 0.0 1.0 f.Flight && range -1e8 1e8 f.Launch.X && range -1e8 1e8 f.Launch.Y && range -1e8 1e8 f.Launch.Z
            && (f.Flight = 0.0 || (f.Role <> 0 && f.Ammo < 2 && Vec3.distance f.Launch CapitalFlight.position <= CapitalFlight.range + 1.0))
            && range 0.0 1.0 f.Approach && range 0.0 100.0 f.WingHull && range 0.0 5.0 f.Volley && intRange 0 1 f.NextGun
            && range 0.0 (100.0+25.0*float s.Upgrades.[2]) f.ParkedHull && range 0.0 (100.0+30.0*float s.Upgrades.[1]) f.ParkedShield
            && range 0.0 CapitalFlight.landingTime f.Landing && (r.Blockade.Value.Lifted = (f.Hits = 2))
            && (if f.Role = 0 then f.Lock = 0.0 && f.Flight = 0.0
                else r.SectorId = Universe.sectors.[1].Id && not r.Docked && r.Blockade.Value.BomberReady && f.ParkedHull > 0.0)))
        && (r.Recovery |> Option.forall (fun state ->
            let kind = (Universe.tryFindSector r.SectorId |> Option.get).Outpost.Kind
            bool state.Latched && range 0.0 6.4 state.Progress &&
            match kind with
            | Refinery -> state.Progress <= 2.8 && (state.Latched || state.Progress = 0.0)
            | FrontierBazaar -> not state.Latched
            | OrbitalYard -> not state.Latched && (state.Progress = 0.0 || state.Progress = 1.0)
            | ResearchRelay -> not state.Latched && state.Progress <= 4.8
            | _ -> not state.Latched && state.Progress = 0.0))
        && intRange 0 1000000000 s.Wallet.Credits && intRange 0 1000000000 s.Wallet.Earned
        && intRange 0 1000000000 s.Wallet.SalvageEarned && bool s.Wallet.TurretOwned
        && Option.forall validPilot s.Pilot
        && (r.Fleet |> Option.forall (fun fleet -> r.Trading && ShipCatalog.valid fleet))
        && (let extra = if s.Pilot |> Option.exists (fun p -> p.Levels.[1] >= 2) then 8 else 0
            let baseHold = ShipCatalog.hulls.[r.Fleet |> Option.map (fun fleet -> fleet.Active) |> Option.defaultValue 0].Hold
            let expansion = s.Cargo.Capacity - baseHold - extra
            integer (box s.Cargo.Capacity) && expansion >= 0 && expansion <= 24 && expansion % 8 = 0)
        && items 6 (intRange 0 s.Cargo.Capacity) s.Cargo.Quantities && s.Cargo.Quantities.Length = 6
        && (s.Cargo.Freight |> Option.forall (fun job -> r.Trading && Freight.valid job))
        && numbers 6 0.0 1e9 s.Cargo.AvgCost && TradeSystem.usedVolume s.Cargo <= s.Cargo.Capacity
        && items 3 (intRange 0 3) s.Upgrades && s.Upgrades.Length = 3
        && ship (100.0+25.0*float s.Upgrades.[2]) (100.0+30.0*float s.Upgrades.[1]) s.Player
        && items 6 (fun (m: SavedMarket) -> text (box m.SectorId) && items 6 (intRange 0 30) m.Stocks && m.Stocks.Length = 6) s.Markets
        && s.Markets.Length = Universe.sectors.Length
        && (s.Markets |> Array.mapi (fun i m ->
            m.SectorId = Universe.sectors.[i].Id &&
            let market = TradeSystem.createMarket Universe.sectors.[i]
            m.Stocks |> Array.mapi (fun j v -> v <= market.Entries.[j].MaxStock) |> Array.forall id) |> Array.forall id)
        && items 16 (fun e -> intRange 0 15 e.Index && intRange 0 4 e.Role && brain e.Brain && ship 10000.0 10000.0 e.Ship) s.Enemies
        && (s.Enemies |> Array.map (fun e -> e.Index) |> Array.distinct |> Array.length) = s.Enemies.Length
        && items 768 (intRange 0 (AsteroidField.Variants-1)) s.Asteroids.Variants
        && items 768 (intRange 0 2) s.Asteroids.Ore && s.Asteroids.Ore.Length = s.Asteroids.Variants.Length
        && numbers (s.Asteroids.Variants.Length*16) -1e9 1e9 s.Asteroids.Data
        && (s.Asteroids.Variants |> Array.mapi (fun i _ -> range 0.01 1000.0 s.Asteroids.Data.[i*16+6] && s.Asteroids.Data.[i*16+15] > 0.0) |> Array.forall id)
        && items 512 (intRange 0 1) s.Lasers.Teams && numbers (s.Lasers.Teams.Length*12) -1e9 1e9 s.Lasers.Data
        && (s.Lasers.Teams |> Array.mapi (fun i _ -> range 0.0 5.0 s.Lasers.Data.[i*12+10] && range 0.0 10000.0 s.Lasers.Data.[i*12+11]) |> Array.forall id)
        && Option.forall (items 512 (fun i -> intRange 0 (s.Lasers.Teams.Length - 1) i && s.Lasers.Teams.[i] = 0)) s.Lasers.Piercing
        && items 96 (fun p -> vec p.Position && numbers 3 -1000.0 1000.0 p.Velocity && intRange 1 6 p.Kind && intRange 1 (if p.Kind = 2 then 2 else 1) p.Quantity && range 0.0 90.0 p.Life) s.Pickups
        && quat s.Turret.Rotation && range 0.0 10.0 s.Turret.Cooldown && intRange -1 15 s.Turret.Target && intRange -1 1 s.Turret.LastBarrel
        && range 0.0 1e9 s.WorldTime && integer (box s.RandomState) && range 1.0 4294967295.0 s.RandomState
        && intRange 0 1 s.Settings.Camera && bool s.Settings.Muted && bool s.Settings.InvertPitch && range 0.0001 0.02 s.Settings.Sensitivity
        && (let sector = Universe.tryFindSector r.SectorId |> Option.get
            let roles = if r.Trading then r.Patrol else (Mission.stage r.Wave).Enemies |> Array.map int
            (r.Trading || sector.Id = (Universe.sectorByIndex (max 0 (r.Wave-1))).Id)
            && (not r.Trading || r.Wave = 0)
            && r.Spawned <= roles.Length
            && (r.Phase <> 1 || (r.Spawned > 0 && roles.Length > 0 && (r.Trading || r.Wave > 0)))
            && (r.Phase = 1 || s.Enemies.Length = 0)
            && (s.Enemies |> Array.forall (fun e ->
                let profile = EnemyProfiles.profile (enum<EnemyRole> e.Role)
                let level = if r.Trading then sector.DangerLevel-1 else r.Wave-1
                let scale = 1.0 + float (max 0 (min 12 (level-1))) * 0.04
                e.Index < r.Spawned && e.Role = roles.[e.Index] && e.Ship.Status.[0] <= profile.Hull*scale && e.Ship.Status.[1] <= profile.Shield*scale))
            && (not r.Completed || (not r.Trading && r.Wave = 6 && s.Enemies.Length = 0))
            && (not r.Docked || Vec3.distance (Vec3(s.Player.Position.[0],s.Player.Position.[1],s.Player.Position.[2])) sector.Outpost.Position <= sector.Outpost.DockRadius + 0.01))

    let decode (json: string) : Result<SaveData,string> =
        try
            if System.String.IsNullOrWhiteSpace json || json.Length > 2000000 then Error "Die Spielstandsdatei ist leer oder zu groß."
            else
                let data = parse json
                if data.Version <> 1 then Error "Diese Spielstand-Version wird noch nicht unterstützt. Die Datei bleibt unverändert."
                elif valid data then Ok data
                else Error "Der Spielstand enthält ungültige oder unvollständige Daten."
        with _ -> Error "Die Datei ist kein gültiger Fable-Space-Spielstand."

    let isValid json = match decode json with Ok _ -> true | Error _ -> false
    let private v (p: Vec3) = [|p.X;p.Y;p.Z|]
    let private q (p: Quat) = [|p.X;p.Y;p.Z;p.W|]
    let private vector (p: float[]) = Vec3(p.[0],p.[1],p.[2])
    let private rotation (p: float[]) = Quat.normalize (Quat(p.[0],p.[1],p.[2],p.[3]))
    let private flag b = if b then 1.0 else 0.0
    let private captureShip (s: Ship) =
        { Position=v s.Position; Rotation=q s.Rotation; Velocity=v s.Velocity
          Motion=[|s.PitchRate;s.YawRate;s.RollRate;s.Boost|]
          Status=[|s.Hull;s.Shield;max 0.0 s.ShieldDelay;max 0.0 s.FireCooldown;s.Overdrive|]
          Controls=[|s.Controls.Pitch;s.Controls.Yaw;s.Controls.Roll;s.Controls.Throttle;flag s.Controls.Boost;flag s.Controls.Brake;flag s.Controls.Fire|] }
    let private captureBrain (b: AiBrain) =
        { State=(match b.State with AiState.Patrol -> 0 | AiState.Chase -> 1 | AiState.Attack -> 2 | AiState.Evade -> 3)
          Timing=[|b.StateTime;b.ThinkTimer;b.HealthAtEntry;b.AttackDuration;b.EvadeDuration;b.WeaponCharge|]
          Firing=b.FiringBurst; Home=v b.Home; Waypoint=v b.Waypoint; Evade=v b.EvadeDirection; Aim=v b.AimPoint }

    let capture run settings now (world: World) (brains: AiBrain[]) (wallet: Wallet) (cargo: CargoHold) (markets: StationMarket[]) levels (turret: AutoTurret) (pickups: ResizeArray<MineralPickup>) =
        { Version=1; SavedAt=now; Run=run; Settings=settings; Wallet=wallet; Cargo=cargo; Upgrades=levels
          Markets=markets |> Array.map (fun m -> {SectorId=m.SectorId;Stocks=m.Entries |> Array.map (fun e -> e.Stock)})
          Player=captureShip world.Player
          Enemies=world.Enemies |> Array.mapi (fun i e -> if e.Alive then Some {Index=i;Role=int brains.[i].Role;Ship=captureShip e;Brain=captureBrain brains.[i]} else None) |> Array.choose id
          Asteroids={Data=Array.init (world.Asteroids.Count*16) (fun i -> float world.Asteroids.Data.[i]);Variants=Array.init world.Asteroids.Count (fun i -> int world.Asteroids.Variant.[i]);Ore=Array.init world.Asteroids.Count (fun i -> int world.Asteroids.Ore.[i])}
          Lasers={Data=Array.init (world.Lasers.Count*12) (fun i -> float world.Lasers.Data.[i]);Teams=Array.init world.Lasers.Count (fun i -> int world.Lasers.Team.[i])
                  Piercing=Some [| for i in 0 .. world.Lasers.Count-1 do if world.Lasers.Piercing.[i] <> 0uy then yield i |]}
          Pickups=pickups |> Seq.map (fun p -> {Position=v p.Position;Velocity=v p.Velocity;Kind=p.Kind;Quantity=p.Quantity;Life=p.Life}) |> Seq.toArray
          Turret={Rotation=q turret.Rotation;Cooldown=max 0.0 turret.Cooldown;Target=turret.Target;LastBarrel=turret.LastBarrel}
          WorldTime=world.Time;RandomState=float world.Rng.State;Pilot=Some world.Pilot }

    let private restoreShip (data: SavedShip) (s: Ship) =
        Ship.reset s (vector data.Position) (rotation data.Rotation)
        s.Velocity <- vector data.Velocity
        s.PitchRate <- data.Motion.[0]; s.YawRate <- data.Motion.[1]; s.RollRate <- data.Motion.[2]; s.Boost <- data.Motion.[3]
        s.Hull <- data.Status.[0]; s.Shield <- data.Status.[1]; s.ShieldDelay <- data.Status.[2]; s.FireCooldown <- data.Status.[3]; s.Overdrive <- data.Status.[4]
        s.Controls.Throttle <- data.Controls.[3]
        if s.Team = Team.Enemy then
            s.Controls.Pitch <- data.Controls.[0]; s.Controls.Yaw <- data.Controls.[1]; s.Controls.Roll <- data.Controls.[2]
            s.Controls.Boost <- data.Controls.[4] = 1.0; s.Controls.Brake <- data.Controls.[5] = 1.0; s.Controls.Fire <- data.Controls.[6] = 1.0

    /// Nur mit erfolgreich decodierten Daten aufrufen; keine Web-/Dateizugriffe.
    let restore (data: SaveData) (world: World) (brains: AiBrain[]) (wallet: Wallet) (cargo: CargoHold) (markets: StationMarket[]) (levels: int[]) (turret: AutoTurret) (pickups: ResizeArray<MineralPickup>) =
        Array.blit data.Upgrades 0 levels 0 3
        Mission.resetPlayer world.Player
        world.Player.MaxHull <- ShipCatalog.hullStrength levels.[2]
        world.Player.MaxShield <- ShipCatalog.shieldCapacity levels.[1]
        world.Player.LaserDamage <- 12.0*(1.0+0.2*float levels.[0])
        World.reset world 0
        restoreShip data.Player world.Player
        wallet.Credits <- data.Wallet.Credits; wallet.Earned <- data.Wallet.Earned; wallet.SalvageEarned <- data.Wallet.SalvageEarned; wallet.TurretOwned <- data.Wallet.TurretOwned
        TradeSystem.resetCargo cargo data.Cargo.Capacity
        cargo.Freight <- data.Cargo.Freight
        Array.blit data.Cargo.Quantities 0 cargo.Quantities 0 6
        Array.blit data.Cargo.AvgCost 0 cargo.AvgCost 0 6
        for i in 0 .. markets.Length-1 do
            markets.[i] <- TradeSystem.createMarket Universe.sectors.[i]
            TradeSystem.restoreStocks markets.[i] data.Markets.[i].Stocks
        let sector = Universe.tryFindSector data.Run.SectorId |> Option.get
        world.SectorIndex <- Universe.sectors |> Array.findIndex (fun s -> s.Id = sector.Id)
        world.Pilot <-
            match data.Pilot with
            | Some p -> { p with Levels = Array.copy p.Levels }
            | None ->
                let p = PilotSkills.create ()
                if data.Run.Trading then PilotSkills.visit p world.SectorIndex
                else for i in 0 .. world.SectorIndex do PilotSkills.visit p i
                p
        Mission.applyLoadout world.Player levels world.Pilot (defaultArg data.Run.Fleet (ShipCatalog.create ()))
        let level = if data.Run.Trading then sector.DangerLevel-1 else data.Run.Wave-1
        for enemy in data.Enemies do
            let s, b = world.Enemies.[enemy.Index], brains.[enemy.Index]
            EnemyProfiles.configure s (enum<EnemyRole> enemy.Role) level
            restoreShip enemy.Ship s
            b.Role <- enum<EnemyRole> enemy.Role
            b.State <- [|AiState.Patrol;AiState.Chase;AiState.Attack;AiState.Evade|].[enemy.Brain.State]
            let timing = enemy.Brain.Timing
            b.StateTime <- timing.[0]; b.ThinkTimer <- timing.[1]; b.HealthAtEntry <- timing.[2]; b.AttackDuration <- timing.[3]; b.EvadeDuration <- timing.[4]; b.WeaponCharge <- timing.[5]
            b.FiringBurst <- enemy.Brain.Firing; b.Home <- vector enemy.Brain.Home; b.Waypoint <- vector enemy.Brain.Waypoint; b.EvadeDirection <- vector enemy.Brain.Evade; b.AimPoint <- vector enemy.Brain.Aim
        world.Asteroids.Count <- data.Asteroids.Variants.Length
        for i in 0 .. data.Asteroids.Data.Length-1 do world.Asteroids.Data.[i] <- float32 data.Asteroids.Data.[i]
        for i in 0 .. world.Asteroids.Count-1 do
            world.Asteroids.Variant.[i] <- byte data.Asteroids.Variants.[i]
            world.Asteroids.Ore.[i] <- byte data.Asteroids.Ore.[i]
        world.Lasers.Count <- data.Lasers.Teams.Length
        for i in 0 .. data.Lasers.Data.Length-1 do world.Lasers.Data.[i] <- float32 data.Lasers.Data.[i]
        for i in 0 .. world.Lasers.Count-1 do
            world.Lasers.Team.[i] <- byte data.Lasers.Teams.[i]
            world.Lasers.Piercing.[i] <- 0uy
        data.Lasers.Piercing |> Option.iter (Array.iter (fun i -> world.Lasers.Piercing.[i] <- 1uy))
        pickups.Clear()
        for p in data.Pickups do pickups.Add {Position=vector p.Position;Velocity=vector p.Velocity;Kind=p.Kind;Quantity=p.Quantity;Life=p.Life}
        Turret.reset turret
        turret.Rotation <- rotation data.Turret.Rotation; turret.Cooldown <- data.Turret.Cooldown; turret.Target <- data.Turret.Target; turret.LastBarrel <- data.Turret.LastBarrel
        world.Time <- data.WorldTime
        world.Rng.State <- uint32 data.RandomState
