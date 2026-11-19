namespace SpaceEngine

type MineralPickup =
    { mutable Position: Vec3
      mutable Velocity: Vec3
      Kind: int
      mutable Quantity: int
      mutable Life: float }

/// Freie Mineralien gehoeren zur Szene; die Physikwelt kennt weder Geld noch Shop.
[<RequireQualifiedAccess>]
module Salvage =
    [<Literal>]
    let Capacity = 96

    [<Literal>]
    let Lifetime = 90.0

    [<Literal>]
    let MagnetRadius = 55.0

    [<Literal>]
    let CryoOreKind = 1

    [<Literal>]
    let RichCryoOreKind = 2

    [<Literal>]
    let NavDataKind = 3

    [<Literal>]
    let TitaniumScrapKind = 4

    [<Literal>]
    let PlasmaCoresKind = 5

    [<Literal>]
    let FuelCellsKind = 6

    let commodityOf (kind: int) : CommodityId =
        match kind with
        | 3 -> CommodityId.NavData
        | 4 -> CommodityId.TitaniumScrap
        | 5 -> CommodityId.PlasmaCores
        | 6 -> CommodityId.FuelCells
        | _ -> CommodityId.CryoOre

    /// Rollenspezifische Wrack-Beute (0 = Drohne, 1 = Spaeher, 2 = Kanonenboot, 3 = Bomber, 4 = Kommandoschiff).
    let wreckDrops (role: int) : (int * int)[] =
        match role with
        | 0 -> [| TitaniumScrapKind, 1 |]
        | 1 -> [| TitaniumScrapKind, 1; FuelCellsKind, 1 |]
        | 2 -> [| TitaniumScrapKind, 2; PlasmaCoresKind, 1 |]
        | 3 -> [| PlasmaCoresKind, 1; FuelCellsKind, 1 |]
        | 4 -> [| PlasmaCoresKind, 2; TitaniumScrapKind, 2; NavDataKind, 1 |]
        | _ -> Array.empty

    let create () : ResizeArray<MineralPickup> = ResizeArray<MineralPickup>(Capacity)

    let spawn (pool: ResizeArray<MineralPickup>) (position: Vec3) (kind: int) (count: int) =
        if kind >= 1 && kind <= 6 then
            let first = pool.Count
            let amount = max 0 (min count (Capacity - first))
            for i in 0 .. amount - 1 do
                // Goldener Winkel verteilt Kristalle ohne Zufallsquelle oder doppelte Spawnpunkte.
                let angle = float (first + i) * 2.399963229728653
                let direction = Vec3(cos angle, 0.5 + sin (angle * 0.7) * 0.35, sin angle)
                pool.Add
                    { Position = position + direction * 1.8
                      Velocity = direction * 3.0
                      Kind = kind
                      Quantity = if kind = RichCryoOreKind then 2 else 1
                      Life = Lifetime }

    let spawnWreck (pool: ResizeArray<MineralPickup>) (position: Vec3) (role: int) =
        let before = pool.Count
        for kind, count in wreckDrops role do
            spawn pool position kind count
        pool.Count - before

    /// Nach Physics.step mit demselben dt aufrufen: PrevPosition erfasst auch einen
    /// schnellen Durchflug. Rueckgabe: aufgenommene Stueckzahl je CommodityId (0..5).
    let collectCargo (pool: ResizeArray<MineralPickup>) (player: Ship) (cargo: CargoHold) (magnetRadius: float) (dt: float) : int[] =
        let collected = Array.zeroCreate TradeSystem.commodities.Length
        if dt > 0.0 then
            let damping = exp (-0.8 * dt)
            let collectRadius = 12.0 + player.Radius
            let mutable i = 0
            while i < pool.Count do
                let pickup = pool.[i]
                pickup.Life <- pickup.Life - dt
                let mutable remove = pickup.Life <= 0.0 || pickup.Quantity <= 0
                if not remove then
                    let good = commodityOf pickup.Kind
                    let unitVolume = max 1 (TradeSystem.commodityDef good).UnitVolume
                    let oldPosition = pickup.Position
                    let toPlayer = player.Position - oldPosition
                    let distance = Vec3.length toPlayer
                    let mutable velocity = pickup.Velocity * damping
                    let canCollect = player.Alive && TradeSystem.freeVolume cargo >= unitVolume
                    if canCollect && distance < magnetRadius && distance > 1e-6 then
                        let acceleration = 28.0 + 60.0 * (1.0 - distance / magnetRadius)
                        velocity <- velocity + toPlayer * (acceleration * dt / distance)
                    pickup.Velocity <- Vec3.clampLength 90.0 velocity
                    pickup.Position <- Vec3.mulAdd oldPosition pickup.Velocity dt

                    if canCollect then
                        // Relativbewegung beider Koerper statt nur Abstand am Frame-Ende.
                        let relativeStart = oldPosition - player.PrevPosition
                        let relativeEnd = pickup.Position - player.Position
                        if Collision.segmentSphere
                               relativeStart.X relativeStart.Y relativeStart.Z
                               relativeEnd.X relativeEnd.Y relativeEnd.Z
                               0.0 0.0 0.0 collectRadius >= 0.0 then
                            let taken = TradeSystem.loadSalvage cargo good pickup.Quantity
                            pickup.Quantity <- pickup.Quantity - taken
                            collected.[int good] <- collected.[int good] + taken
                            remove <- pickup.Quantity <= 0

                if remove then
                    let last = pool.Count - 1
                    if i <> last then pool.[i] <- pool.[last]
                    pool.RemoveAt(last)
                else
                    i <- i + 1
        collected

    let collect (pool: ResizeArray<MineralPickup>) (player: Ship) (cargo: CargoHold) (magnetRadius: float) (dt: float) =
        let counts = collectCargo pool player cargo magnetRadius dt
        struct (counts.[int CommodityId.CryoOre], counts.[int CommodityId.NavData])

    let update pool player cargo dt =
        let struct (ore, _) = collect pool player cargo MagnetRadius dt
        ore
