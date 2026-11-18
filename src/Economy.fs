namespace SpaceEngine

type Wallet =
    { mutable Credits: int
      mutable Earned: int
      mutable SalvageEarned: int
      mutable TurretOwned: bool }

[<RequireQualifiedAccess>]
module Economy =
    let create () = { Credits = 0; Earned = 0; SalvageEarned = 0; TurretOwned = false }
    let reset (wallet: Wallet) =
        wallet.Credits <- 0
        wallet.Earned <- 0
        wallet.SalvageEarned <- 0
        wallet.TurretOwned <- false

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
