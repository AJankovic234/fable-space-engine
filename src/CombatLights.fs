namespace SpaceEngine

// =====================================================================================
//  CombatLights.fs  -  Punktlichter des Gefechts
//
//  * Zerstoerungen: abklingende Feuerball-Glut je Stufe nach dem Zeitplan in CombatVfx
//    (erste Stufe, Folgeexplosionen, Hauptexplosion). Den kurzen hellen Blitz setzt
//    weiterhin Effects.flash; die Glut haelt das Licht, solange der Feuerball brennt.
//  * Muendungsfeuer der Gegner und Bolt-Koepfe in Rumpfnaehe.
//  Alles meldet nur Kandidaten an Lights; welche der Shader bekommt, entscheidet
//  LightSelection in beginFrame. Keine Allokation im Frame-Pfad.
// =====================================================================================

/// Geplante Lichter, je Eintrag: x y z, Reichweite, r g b, Spitze, Zuendzeit (Weltzeit),
/// Abklingzeit.
type CombatLightPool =
    { Data: float[]
      mutable Count: int }

[<RequireQualifiedAccess>]
module CombatLights =
    [<Literal>]
    let Capacity = 48

    [<Literal>]
    let Stride = 10

    /// Anstieg der Glut in Sekunden.
    [<Literal>]
    let Attack = 0.04

    /// Reichweite (m) und Intensitaet des Lichts am Kopf eines Bolts.
    [<Literal>]
    let BoltReach = 7.0

    [<Literal>]
    let BoltIntensity = 0.9

    let create () : CombatLightPool =
        { Data = Array.zeroCreate (Capacity * Stride); Count = 0 }

    /// Helligkeit 0..1 `age` Sekunden nach dem Zuenden: kurzer Anstieg, dann exponentiell.
    let envelope (age: float) (tau: float) : float =
        if age < 0.0 then 0.0
        elif age < Attack then age / Attack
        else exp (-(age - Attack) / tau)

    /// Unter 2 % der Spitze gilt ein Eintrag als verbraucht.
    let private spent (age: float) (tau: float) = age > Attack + 4.0 * tau

    /// Plant ein Licht, das `delay` Sekunden nach `now` zuendet. Ist der Pool voll,
    /// ersetzt es den Eintrag mit der geringsten verbleibenden Helligkeit.
    let schedule (pool: CombatLightPool) (now: float) (p: Vec3) (range: float) (r: float) (g: float) (b: float) (peak: float) (delay: float) (tau: float) =
        let d = pool.Data
        let mutable slot = -1

        if pool.Count < Capacity then
            slot <- pool.Count
            pool.Count <- pool.Count + 1
        else
            let mutable weakest = System.Double.MaxValue

            for i in 0 .. Capacity - 1 do
                let o = i * Stride
                let age = now - d.[o + 8]
                // Noch nicht gezuendete Eintraege zaehlen mit ihrer Spitze.
                let left = d.[o + 7] * (if age < 0.0 then 1.0 else envelope age d.[o + 9])
                if left < weakest then
                    weakest <- left
                    slot <- i

        let o = slot * Stride
        d.[o] <- p.X
        d.[o + 1] <- p.Y
        d.[o + 2] <- p.Z
        d.[o + 3] <- range
        d.[o + 4] <- r
        d.[o + 5] <- g
        d.[o + 6] <- b
        d.[o + 7] <- peak
        d.[o + 8] <- now + delay
        d.[o + 9] <- tau

    /// Lichtfahrplan einer Zerstoerung der Stufe `tier` (CombatVfx): Glut der ersten Stufe,
    /// je Folgeexplosion ein kurzer Puls entlang der Rumpfachse (+-`half` um `center`) und
    /// die Glut der Hauptexplosion. `size` ist die Groesse wie in Effects (Huellradius).
    let blast (pool: CombatLightPool) (now: float) (center: Vec3) (axis: Vec3) (half: float) (size: float) (tier: int) =
        let recipe = CombatVfx.recipe tier
        let reach = size * recipe.FlashRange
        let glow = 0.32 + 0.12 * float tier
        schedule pool now center (reach * 0.9) 1.0 0.52 0.18 (recipe.FlashIntensity * 0.6) 0.0 glow

        for i in 0 .. recipe.Secondaries - 1 do
            // Feste Streuung entlang der Achse statt Zufall: dieselbe Explosion leuchtet gleich.
            let spread = (float ((i * 7 + 3) % 11) / 10.0) * 2.0 - 1.0
            let p = center + axis * (half * spread)
            schedule pool now p (12.0 + size * 6.0) 1.0 0.58 0.22 2.2 (CombatVfx.secondaryTime tier i) 0.2

        if recipe.Finale > 0.0 then
            schedule pool now center (reach * 1.1) 1.0 0.6 0.26 (recipe.FlashIntensity * 0.55) recipe.Finale (glow * 1.5)

    /// Zerstoerung eines Schiffs wie Effects.shipDestruction: Stufe nach Huellradius, der
    /// Spieler immer schwer.
    let shipDestroyed (pool: CombatLightPool) (now: float) (ship: Ship) (position: Vec3) =
        let player = ship.Team = Team.Player
        let size = if player then ship.Radius * 0.8 else ship.Radius
        let tier = if player then CombatVfx.TierHeavy else CombatVfx.tier ship.Radius
        let axis = Quat.rotateVec3 ship.Rotation Vec3.unitZ
        let center = position + Quat.rotateVec3 ship.Rotation ship.ObbCenter
        blast pool now center axis (ship.ObbHalf.Z * 0.8) size tier

    /// Explosion ohne Rumpfbezug wie Effects.shipExplosion (Geschuetz, Torpedotreffer).
    let explosion (pool: CombatLightPool) (now: float) (position: Vec3) (size: float) =
        let tier = if CombatVfx.tier size = CombatVfx.TierLight then CombatVfx.TierLight else CombatVfx.TierHeavy
        blast pool now position Vec3.unitZ 0.0 size tier

    /// Wertet ein Simulationsereignis aus (in processEvents fuer jedes Ereignis aufrufen).
    let observe (pool: CombatLightPool) (world: World) (event: GameEvent) =
        match event with
        | ShipDestroyed(victim, position, slot) ->
            if victim = Team.Player then shipDestroyed pool world.Time world.Player position
            elif slot >= 0 && slot < world.Enemies.Length then shipDestroyed pool world.Time world.Enemies.[slot] position
        | _ -> ()

    /// Meldet die aktiven Glutlichter an Lights und gibt verbrauchte Plaetze frei.
    let push (pool: CombatLightPool) (lights: Lights) (now: float) =
        let d = pool.Data
        let mutable i = 0

        while i < pool.Count do
            let o = i * Stride
            let age = now - d.[o + 8]
            let tau = d.[o + 9]
            // Springt die Weltzeit zurueck (neues Spiel, Laden), verfallen alte Plaene.
            if spent age tau || age < -10.0 then
                let last = (pool.Count - 1) * Stride
                for k in 0 .. Stride - 1 do
                    d.[o + k] <- d.[last + k]
                pool.Count <- pool.Count - 1
            else
                let k = envelope age tau
                if k > 0.0 then
                    Lights.addPoint lights d.[o] d.[o + 1] d.[o + 2] d.[o + 3] d.[o + 4] d.[o + 5] d.[o + 6] (d.[o + 7] * k)
                i <- i + 1

    /// Muendungsfeuer eines Gegners: ein Licht zwischen den Rohren in der Boltfarbe der Rolle.
    let muzzle (lights: Lights) (left: Vec3) (right: Vec3) (muzzleSize: float) (role: int) =
        let style = CombatVfx.boltStyles.[CombatVfx.StyleEnemy + System.Math.Max(0, System.Math.Min(4, role))]
        let p = (left + right) * 0.5
        Lights.addPoint lights p.X p.Y p.Z (4.0 + muzzleSize * 9.0) style.R style.G style.B 0.8

    let private near (ship: Ship) (x: float) (y: float) (z: float) =
        let dx = ship.Position.X - x
        let dy = ship.Position.Y - y
        let dz = ship.Position.Z - z
        let reach = ship.Radius + BoltReach
        ship.Alive && dx * dx + dy * dy + dz * dz < reach * reach

    /// Bolt-Koepfe, die einem Rumpf (Spieler oder Gegner) naeher als BoltReach kommen,
    /// werfen ein kleines Licht in ihrer Farbe. `backstep` wie in Main.pushLasers.
    let bolts (lights: Lights) (world: World) (brains: AiBrain[]) (overdrive: bool) (focus: bool) (backstep: float) =
        let pool = world.Lasers
        let d = pool.Data
        let player = world.Player

        for i in 0 .. pool.Count - 1 do
            let o = i * LaserPool.Stride
            let x = float d.[o] - float d.[o + 3] * backstep
            let y = float d.[o + 1] - float d.[o + 4] * backstep
            let z = float d.[o + 2] - float d.[o + 5] * backstep
            let mutable lit = near player x y z
            let mutable e = 0

            while not lit && e < world.Enemies.Length do
                lit <- near world.Enemies.[e] x y z
                e <- e + 1

            if lit then
                let owner = int pool.Owner.[i]
                let slot = owner - CombatVfx.OwnerEnemySlot
                let role = if slot >= 0 && slot < brains.Length then int brains.[slot].Role else 0
                let playerTeam = int pool.Team.[i] = int Team.Player
                let style = CombatVfx.boltStyles.[CombatVfx.boltStyleId owner playerTeam overdrive focus role]
                Lights.addPoint lights x y z BoltReach style.R style.G style.B BoltIntensity
