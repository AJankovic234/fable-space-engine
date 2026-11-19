namespace SpaceEngine

type Wallet =
    { mutable Credits: int
      mutable Earned: int
      mutable SalvageEarned: int
      mutable TurretOwned: bool
      mutable Addons: int }

type AddonDef =
    { Id: int
      Name: string
      Tag: string
      Effect: string
      Price: int }

[<RequireQualifiedAccess>]
module Economy =
    [<Literal>]
    let AddonMissileLoader = 0
    [<Literal>]
    let AddonSalvageScanner = 1
    [<Literal>]
    let AddonCountermeasures = 2
    [<Literal>]
    let AddonHullNanites = 3

    let addons : AddonDef[] =
        [| { Id = AddonMissileLoader; Name = "Raketen-Nachlader"; Tag = "WAFFEN-ADDON"
             Effect = "+2 Abfangraketen-Kapazität und 35 % schnelleres Nachladen im Flug."; Price = 210 }
           { Id = AddonSalvageScanner; Name = "Erz- & Wrack-Scanner"; Tag = "BERGUNGS-ADDON"
             Effect = "+40 m Magnetreichweite und +250 m G-Scan-Radius (bis 12 Erz-Echos)."; Price = 160 }
           { Id = AddonCountermeasures; Name = "Notfall-Täuschkörper"; Tag = "SCHUTZ-ADDON"
             Effect = "Aktiviert bei Schildbruch alle 22 s automatisch +30 Notfallschild."; Price = 240 }
           { Id = AddonHullNanites; Name = "Hüllen-Naniten"; Tag = "STRUKTUR-ADDON"
             Effect = "Regeneriert außerhalb direkten Beschusses +2,5 Hülle pro Sekunde."; Price = 280 } |]

    let hasAddon (wallet: Wallet) id =
        id >= 0 && id < addons.Length && (wallet.Addons &&& (1 <<< id)) <> 0

    let addonPriceFor terms id =
        if id >= 0 && id < addons.Length then TradeTerms.servicePrice terms addons.[id].Price else 0

    let buyAddonFor terms (wallet: Wallet) id =
        let cost = addonPriceFor terms id
        if id >= 0 && id < addons.Length && not (hasAddon wallet id) && wallet.Credits >= cost then
            wallet.Credits <- wallet.Credits - cost
            wallet.Addons <- wallet.Addons ||| (1 <<< id)
            true
        else false

    let create () = { Credits = 0; Earned = 0; SalvageEarned = 0; TurretOwned = false; Addons = 0 }
    let reset (wallet: Wallet) =
        wallet.Credits <- 0
        wallet.Earned <- 0
        wallet.SalvageEarned <- 0
        wallet.TurretOwned <- false
        wallet.Addons <- 0

    let earn (wallet: Wallet) (amount: int) (mineral: bool) =
        if amount > 0 then
            wallet.Credits <- wallet.Credits + amount
            wallet.Earned <- wallet.Earned + amount
            if mineral then wallet.SalvageEarned <- wallet.SalvageEarned + amount

    /// 0 = Turret; 1..3 = Laser, Schild und Hülle. Maximal drei Ausbaustufen.
    let available (wallet: Wallet) (levels: int[]) (item: int) =
        if item = 0 then not wallet.TurretOwned
        elif item >= 1 && item <= 3 then levels.[item - 1] < 3
        else false

    let price (levels: int[]) (item: int) =
        if item = 0 then 350
        elif item >= 1 && item <= 3 then
            (if item = 1 then 120 else 140) + levels.[item - 1] * 80
        else 0

    let priceFor terms levels item =
        let basePrice = price levels item
        if item = 0 then basePrice else TradeTerms.servicePrice terms basePrice

    let buyFor terms (wallet: Wallet) (ship: Ship) (levels: int[]) (item: int) =
        let charge = priceFor terms levels item
        if available wallet levels item && wallet.Credits >= charge then
            wallet.Credits <- wallet.Credits - charge
            if item = 0 then wallet.TurretOwned <- true
            else Mission.applyUpgrade ship levels (item - 1)
            true
        else false

    let buy wallet ship levels item = buyFor TradeTerms.standard wallet ship levels item

    [<Literal>]
    let ScrapRepairAmount = 35.0

    let repairNeeded (ship: Ship) =
        max 0 (int (System.Math.Ceiling(max 0.0 (ship.MaxHull - ship.Hull))))

    let repairCostFor terms (ship: Ship) =
        let missing = repairNeeded ship
        if missing <= 0 then 0
        else max 1 (TradeTerms.servicePrice terms (missing * 2))

    let repairWithCredits terms (wallet: Wallet) (ship: Ship) =
        let cost = repairCostFor terms ship
        if cost > 0 && wallet.Credits >= cost then
            wallet.Credits <- wallet.Credits - cost
            ship.Hull <- ship.MaxHull
            ship.Shield <- ship.MaxShield
            ship.ShieldDelay <- 0.0
            true
        else false

    let repairWithScrap (cargo: CargoHold) (ship: Ship) =
        let i = int CommodityId.TitaniumScrap
        if repairNeeded ship > 0 && cargo.Quantities.[i] >= 1 then
            cargo.Quantities.[i] <- cargo.Quantities.[i] - 1
            if cargo.Quantities.[i] = 0 then cargo.AvgCost.[i] <- 0.0
            ship.Hull <- min ship.MaxHull (ship.Hull + ScrapRepairAmount)
            ship.Shield <- ship.MaxShield
            ship.ShieldDelay <- 0.0
            true
        else false
