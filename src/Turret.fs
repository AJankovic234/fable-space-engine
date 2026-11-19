namespace SpaceEngine

type AutoTurret =
    { mutable Cooldown: float
      mutable Rotation: Quat
      mutable Target: int
      mutable LastBarrel: int
      mutable Flash: float }

[<RequireQualifiedAccess>]
module Turret =
    let Range = 280.0
    let Mount = Vec3(0.0, 0.85, 0.65)
    let create () = { Cooldown = 0.0; Rotation = Quat.identity; Target = -1; LastBarrel = -1; Flash = 0.0 }
    let reset (turret: AutoTurret) =
        turret.Cooldown <- 0.0
        turret.Rotation <- Quat.identity
        turret.Target <- -1
        turret.LastBarrel <- -1
        turret.Flash <- 0.0

    let private clearShot (world: World) (origin: Vec3) (target: Vec3) =
        let field = world.Asteroids
        let mutable clear = true
        let mutable i = 0
        while clear && i < field.Count do
            let o = i * AsteroidField.Stride
            let d = field.Data
            clear <- Collision.segmentSphere origin.X origin.Y origin.Z target.X target.Y target.Z
                        (float d.[o]) (float d.[o+1]) (float d.[o+2]) (float d.[o+6] + 0.4) < 0.0
            i <- i + 1
        clear

    /// Ein eigener schwenkender Turm: echte Projektile, Vorhalt und Sichtprüfung.
    let update (world: World) (turret: AutoTurret) (owned: bool) (dt: float) =
        turret.Flash <- max 0.0 (turret.Flash - dt * 8.0)
        turret.Cooldown <- max 0.0 (turret.Cooldown - dt)
        turret.Target <- -1
        if owned && world.Player.Alive then
            let player = world.Player
            let origin = player.Position + Quat.rotateVec3 player.Rotation Mount
            let mutable nearest = Range * Range
            for i in 0 .. world.Enemies.Length - 1 do
                let enemy = world.Enemies.[i]
                if enemy.Alive then
                    let distance = Vec3.distanceSq origin enemy.Position
                    if distance < nearest && clearShot world origin enemy.Position then
                        nearest <- distance
                        turret.Target <- i
            if turret.Target >= 0 then
                let enemy = world.Enemies.[turret.Target]
                let relative = enemy.Position - origin
                let velocity = enemy.Velocity - player.Velocity
                let time = Ballistics.interceptTime relative.X relative.Y relative.Z velocity.X velocity.Y velocity.Z Combat.LaserSpeed
                let aim = enemy.Position + velocity * max 0.0 time
                let desired = Vec3.normalize (aim - origin)
                let rotation = Quat.lookRotation desired (Quat.up player.Rotation)
                turret.Rotation <- Quat.slerp turret.Rotation rotation (min 1.0 (dt * 9.0))
                let forward = Quat.forward turret.Rotation
                if turret.Cooldown <= 0.0 && Vec3.dot forward desired > 0.998 && clearShot world origin aim then
                    let barrel = if turret.LastBarrel = 0 then 1 else 0
                    let gun = if barrel = 0 then ShipHardpoints.turretGunLeft else ShipHardpoints.turretGunRight
                    let muzzle = origin + Quat.rotateVec3 turret.Rotation gun
                    let v = forward * Combat.LaserSpeed + player.Velocity
                    let q = turret.Rotation
                    if LaserPool.spawn world.Lasers muzzle.X muzzle.Y muzzle.Z v.X v.Y v.Z q.X q.Y q.Z q.W 10.0 Team.Player then
                        let boosted = PilotSkills.level world.Pilot 1 = 3
                        if boosted then world.Lasers.Piercing.[world.Lasers.Count-1] <- 1uy
                        turret.Cooldown <- if boosted then 0.36 / 1.4 else 0.36
                        turret.LastBarrel <- barrel
                        turret.Flash <- 1.0
                        world.Events.Add(TurretFired muzzle)
            else
                turret.Rotation <- Quat.slerp turret.Rotation player.Rotation (min 1.0 (dt * 3.0))

type HomingMissile =
    { mutable Alive: bool
      mutable Position: Vec3
      mutable PrevPosition: Vec3
      mutable Velocity: Vec3
      mutable Rotation: Quat
      mutable Target: int
      mutable Life: float }

type MissileRack =
    { mutable Ammo: int
      mutable ReloadTimer: float
      mutable FireCooldown: float
      Missiles: HomingMissile[] }

type MissileImpact = { HitEnemy: bool; Position: Vec3 }

[<RequireQualifiedAccess>]
module Missiles =
    [<Literal>]
    let MaxAmmo = 4
    [<Literal>]
    let ReloadInterval = 16.0
    [<Literal>]
    let Cooldown = 0.45
    [<Literal>]
    let Range = 440.0
    [<Literal>]
    let Speed = 185.0
    [<Literal>]
    let TurnRate = 5.4
    [<Literal>]
    let DamageAmount = 38.0
    [<Literal>]
    let Lifetime = 4.5
    [<Literal>]
    let PoolSize = 8

    let create () : MissileRack =
        { Ammo = MaxAmmo
          ReloadTimer = 0.0
          FireCooldown = 0.0
          Missiles = Array.init PoolSize (fun _ ->
              { Alive = false; Position = Vec3.zero; PrevPosition = Vec3.zero
                Velocity = Vec3.zero; Rotation = Quat.identity; Target = -1; Life = 0.0 }) }

    let clear (rack: MissileRack) =
        for m in rack.Missiles do m.Alive <- false
        rack.FireCooldown <- 0.0

    let reload (rack: MissileRack) =
        clear rack
        rack.Ammo <- MaxAmmo
        rack.ReloadTimer <- 0.0

    let tryLaunch (world: World) (rack: MissileRack) (target: int) : Result<Vec3, string> =
        let player = world.Player
        if not player.Alive then Error ""
        elif rack.FireCooldown > 0.0 then Error ""
        elif rack.Ammo <= 0 then Error "RAKETEN LADEN NACH"
        elif target < 0 || target >= world.Enemies.Length || not world.Enemies.[target].Alive then
            Error "KEIN ZIEL ERFASST · T/N ZUM ERFASSEN"
        elif Vec3.distance player.Position world.Enemies.[target].Position > Range then
            Error "ZIEL AUSSER REICHWEITE (MAX 440 m)"
        else
            let mutable slot = -1
            for i in 0 .. rack.Missiles.Length - 1 do
                if slot < 0 && not rack.Missiles.[i].Alive then slot <- i
            if slot < 0 then Error ""
            else
                let fwd = Quat.forward player.Rotation
                let up = Quat.up player.Rotation
                let origin = player.Position + fwd * 3.0 - up * 0.9
                let m = rack.Missiles.[slot]
                m.Alive <- true
                m.Position <- origin
                m.PrevPosition <- origin
                m.Rotation <- player.Rotation
                m.Velocity <- fwd * Speed + player.Velocity * 0.35
                m.Target <- target
                m.Life <- Lifetime
                rack.Ammo <- rack.Ammo - 1
                rack.FireCooldown <- Cooldown
                Ok origin

    let update (world: World) (rack: MissileRack) (dt: float) : ResizeArray<MissileImpact> =
        let impacts = ResizeArray<MissileImpact>()
        if dt <= 0.0 then impacts
        else
            rack.FireCooldown <- max 0.0 (rack.FireCooldown - dt)
            if rack.Ammo < MaxAmmo then
                rack.ReloadTimer <- rack.ReloadTimer + dt
                if rack.ReloadTimer >= ReloadInterval then
                    rack.ReloadTimer <- rack.ReloadTimer - ReloadInterval
                    rack.Ammo <- min MaxAmmo (rack.Ammo + 1)
            else
                rack.ReloadTimer <- 0.0

            let field = world.Asteroids
            let fdata = field.Data
            for m in rack.Missiles do
                if m.Alive then
                    m.PrevPosition <- m.Position
                    m.Life <- m.Life - dt
                    if m.Life <= 0.0 then
                        m.Alive <- false
                    else
                        if m.Target >= 0 && m.Target < world.Enemies.Length && world.Enemies.[m.Target].Alive then
                            let enemy = world.Enemies.[m.Target]
                            let aim = enemy.Position + enemy.Velocity * 0.15
                            let delta = aim - m.Position
                            let distSq = Vec3.lengthSq delta
                            if distSq > 1e-4 then
                                let desired = Quat.lookRotation (delta * (1.0 / sqrt distSq)) Vec3.up
                                m.Rotation <- Quat.slerp m.Rotation desired (min 1.0 (dt * TurnRate))
                        m.Velocity <- Quat.forward m.Rotation * Speed
                        m.Position <- Vec3.mulAdd m.PrevPosition m.Velocity dt

                        let mutable hit = false
                        let mutable ei = 0
                        while not hit && ei < world.Enemies.Length do
                            let enemy = world.Enemies.[ei]
                            if enemy.Alive then
                                let t =
                                    Collision.segmentSphere
                                        m.PrevPosition.X m.PrevPosition.Y m.PrevPosition.Z
                                        m.Position.X m.Position.Y m.Position.Z
                                        enemy.Position.X enemy.Position.Y enemy.Position.Z
                                        (enemy.Radius + 1.8)
                                if t >= 0.0 then
                                    let point = Vec3.lerp m.PrevPosition m.Position t
                                    Damage.ship world enemy DamageAmount
                                    m.Alive <- false
                                    hit <- true
                                    impacts.Add { HitEnemy = true; Position = point }
                            ei <- ei + 1

                        let mutable ai = 0
                        while not hit && ai < field.Count do
                            let o = ai * AsteroidField.Stride
                            let t =
                                Collision.segmentSphere
                                    m.PrevPosition.X m.PrevPosition.Y m.PrevPosition.Z
                                    m.Position.X m.Position.Y m.Position.Z
                                    (float fdata.[o]) (float fdata.[o + 1]) (float fdata.[o + 2])
                                    (float fdata.[o + 6] + 0.5)
                            if t >= 0.0 then
                                let point = Vec3.lerp m.PrevPosition m.Position t
                                m.Alive <- false
                                hit <- true
                                impacts.Add { HitEnemy = false; Position = point }
                            ai <- ai + 1
            impacts
