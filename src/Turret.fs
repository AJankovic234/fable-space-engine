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
