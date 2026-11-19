namespace SpaceEngine

// =====================================================================================
//  TradeSystem.fs  -  Warenwirtschaft, Frachtraum und dynamische Stationsmaerkte
//
//  Statt nur pauschale Credits zu vergeben, landen Bergungsgüter im Frachtraum des
//  Schiffs. Jede Station besitzt eigene Lagerbestaende und Preis-Multiplikatoren,
//  sodass Handelsrouten zwischen Bergbau-, Raffinerie-, Forschungs- und Flottensektoren
//  entstehen.
// =====================================================================================

type CommodityId =
    | TitaniumScrap = 0
    | CryoOre = 1
    | PlasmaCores = 2
    | NavData = 3
    | FuelCells = 4
    | MedSupplies = 5

type CommodityDef =
    { Id: CommodityId
      Name: string
      ShortName: string
      BasePrice: int
      UnitVolume: int }

type PriceTrend =
    | Bargain
    | Normal
    | HighDemand

type MarketEntry =
    { Commodity: CommodityId
      Price: int
      Stock: int
      MaxStock: int
      Trend: PriceTrend }

type StationMarket =
    { SectorId: string
      StationKind: StationKind
      Entries: MarketEntry[] }

type FreightContract = { Origin: int; Destination: int; Tonnes: int; Fee: int }

type CargoHold =
    { mutable Capacity: int
      Quantities: int[]
      AvgCost: float[]
      mutable Freight: FreightContract option }

type TradeOutcome =
    { Ok: bool
      CreditsDelta: int
      VolumeDelta: int
      Message: string }

/// Die Dockkonditionen gelten für Vorschau und Abrechnung gleichermaßen.
type TradeTerms = { SignalLevel: int; FixerLevel: int }

[<RequireQualifiedAccess>]
module TradeTerms =
    let standard = { SignalLevel = 0; FixerLevel = 0 }

    let servicePrice terms credits =
        if terms.FixerLevel >= 1 then credits * 3 / 4 else credits

[<RequireQualifiedAccess>]
module TradeSystem =

    let commodities: CommodityDef[] =
        [| { Id = CommodityId.TitaniumScrap; Name = "Titanschrott"; ShortName = "TIT"; BasePrice = 18; UnitVolume = 1 }
           { Id = CommodityId.CryoOre; Name = "Kryo-Erz"; ShortName = "KRY"; BasePrice = 28; UnitVolume = 1 }
           { Id = CommodityId.PlasmaCores; Name = "Plasmakerne"; ShortName = "PLS"; BasePrice = 64; UnitVolume = 2 }
           { Id = CommodityId.NavData; Name = "Nav-Daten"; ShortName = "NAV"; BasePrice = 52; UnitVolume = 1 }
           { Id = CommodityId.FuelCells; Name = "Treibstoffzellen"; ShortName = "TRB"; BasePrice = 34; UnitVolume = 1 }
           { Id = CommodityId.MedSupplies; Name = "Med-Module"; ShortName = "MED"; BasePrice = 46; UnitVolume = 1 } |]

    let inline private idx (id: CommodityId) = int id

    let commodityDef (id: CommodityId) = commodities.[idx id]

    let createCargo (capacity: int) : CargoHold =
        { Capacity = max 4 capacity
          Quantities = Array.zeroCreate commodities.Length
          AvgCost = Array.zeroCreate commodities.Length
          Freight = None }

    let resetCargo (cargo: CargoHold) (capacity: int) =
        cargo.Capacity <- max 4 capacity
        cargo.Freight <- None
        for i in 0 .. cargo.Quantities.Length - 1 do
            cargo.Quantities.[i] <- 0
            cargo.AvgCost.[i] <- 0.0

    let copyCargo (cargo: CargoHold) =
        { cargo with Quantities = Array.copy cargo.Quantities; AvgCost = Array.copy cargo.AvgCost }

    let saleProfit revenue (before: CargoHold) (after: CargoHold) =
        let mutable cost = 0.0
        for i in 0 .. before.Quantities.Length - 1 do
            let sold = before.Quantities.[i] - after.Quantities.[i]
            cost <- cost + float sold * before.AvgCost.[i]
        float revenue - cost

    let goodsVolume (cargo: CargoHold) =
        let mutable total = 0
        for i in 0 .. commodities.Length - 1 do
            total <- total + cargo.Quantities.[i] * commodities.[i].UnitVolume
        total

    let usedVolume cargo =
        goodsVolume cargo + (match cargo.Freight with Some job -> job.Tonnes | None -> 0)

    let freeVolume (cargo: CargoHold) = max 0 (cargo.Capacity - usedVolume cargo)

    let quantityOf (cargo: CargoHold) (id: CommodityId) = cargo.Quantities.[idx id]

    /// Nimmt geborgene Fracht aus dem Raum auf, soweit Platz im Frachtraum ist.
    /// Liefert die tatsaechlich eingelagerte Stueckzahl.
    let loadSalvage (cargo: CargoHold) (id: CommodityId) (amount: int) =
        if amount <= 0 then
            0
        else
            let def = commodityDef id
            let maxFit = freeVolume cargo / max 1 def.UnitVolume
            let taken = min amount maxFit
            if taken > 0 then
                let i = idx id
                let prevQty = cargo.Quantities.[i]
                let newQty = prevQty + taken
                // Gefundene Fracht hat Einstandskosten 0, senkt also die Durchschnitts-Kostenbasis.
                cargo.AvgCost.[i] <- (cargo.AvgCost.[i] * float prevQty) / float newQty
                cargo.Quantities.[i] <- newQty
            taken

    /// Spezifische Angebots-/Nachfragefaktoren pro Stationstyp (< 1.0 = guenstig, > 1.0 = begehrt).
    let private stationFactor (kind: StationKind) (id: CommodityId) =
        match kind, id with
        | MiningDepot, CommodityId.CryoOre -> 0.65
        | MiningDepot, CommodityId.TitaniumScrap -> 0.75
        | MiningDepot, CommodityId.FuelCells -> 1.30
        | MiningDepot, CommodityId.MedSupplies -> 1.25
        | Refinery, CommodityId.CryoOre -> 1.45
        | Refinery, CommodityId.TitaniumScrap -> 1.35
        | Refinery, CommodityId.FuelCells -> 0.72
        | Refinery, CommodityId.PlasmaCores -> 0.82
        | OrbitalYard, CommodityId.TitaniumScrap -> 1.25
        | OrbitalYard, CommodityId.PlasmaCores -> 1.32
        | OrbitalYard, CommodityId.FuelCells -> 0.85
        | ResearchRelay, CommodityId.NavData -> 0.70
        | ResearchRelay, CommodityId.PlasmaCores -> 1.40
        | ResearchRelay, CommodityId.CryoOre -> 1.22
        | FrontierBazaar, CommodityId.MedSupplies -> 0.74
        | FrontierBazaar, CommodityId.NavData -> 1.38
        | FrontierBazaar, CommodityId.PlasmaCores -> 1.18
        | FleetDock, CommodityId.FuelCells -> 1.45
        | FleetDock, CommodityId.MedSupplies -> 1.50
        | FleetDock, CommodityId.PlasmaCores -> 1.48
        | FleetDock, CommodityId.NavData -> 1.35
        | _ -> 1.0

    let private marketFactor sectorId kind id =
        match sectorId, id with
        | "kaltstrom", CommodityId.CryoOre -> 0.54
        | "kaltstrom", CommodityId.FuelCells -> 1.64
        | "wracksaum", CommodityId.TitaniumScrap -> 0.65
        | "wracksaum", CommodityId.PlasmaCores -> 0.73
        | "wracksaum", CommodityId.MedSupplies -> 1.49
        | "schleierpass", CommodityId.NavData -> 0.52
        | "schleierpass", CommodityId.MedSupplies -> 1.72
        | _ -> stationFactor kind id

    let private initialStock factor id =
        let maxStock =
            match id with
            | CommodityId.PlasmaCores -> 14
            | CommodityId.NavData -> 18
            | _ -> 30
        let stock =
            if factor < 0.85 then int (float maxStock * 0.85)
            elif factor > 1.20 then int (float maxStock * 0.25)
            else int (float maxStock * 0.55)
        stock, maxStock

    let private computePrice (basePrice: int) baseFactor stock maxStock =
        let fill = if maxStock > 0 then float stock / float maxStock else 0.5
        // Knappheit treibt den Preis leicht hoch, Ueberbestand drueckt ihn leicht.
        let stockAdjust = 1.15 - 0.30 * fill
        let price = max 5 (int (System.Math.Round(float basePrice * baseFactor * stockAdjust)))
        let ratio = float price / float (max 1 basePrice)
        let trend =
            if ratio <= 0.86 then Bargain
            elif ratio >= 1.18 then HighDemand
            else Normal
        price, trend

    let createMarket (sector: SectorDef) : StationMarket =
        let entries =
            commodities
            |> Array.map (fun def ->
                let factor = marketFactor sector.Id sector.Outpost.Kind def.Id
                let stock, maxStock = initialStock factor def.Id
                let price, trend = computePrice def.BasePrice factor stock maxStock
                { Commodity = def.Id
                  Price = price
                  Stock = stock
                  MaxStock = maxStock
                  Trend = trend })
        { SectorId = sector.Id
          StationKind = sector.Outpost.Kind
          Entries = entries }

    let createUniverseMarkets () =
        Universe.sectors |> Array.map createMarket

    let private refreshEntry (market: StationMarket) (i: int) (newStock: int) =
        let def = commodities.[i]
        let prev = market.Entries.[i]
        let clamped = max 0 (min prev.MaxStock newStock)
        let price, trend = computePrice def.BasePrice (marketFactor market.SectorId market.StationKind def.Id) clamped prev.MaxStock
        market.Entries.[i] <-
            { prev with
                Stock = clamped
                Price = price
                Trend = trend }

    /// Für validierte Spielstände: Referenzkurse aus den gespeicherten Beständen ableiten.
    let restoreStocks (market: StationMarket) (stocks: int[]) =
        for i in 0 .. market.Entries.Length-1 do refreshEntry market i stocks.[i]

    /// Fracht eines Konvois verändert dieselben Bestände und Kurse wie ein Marktgeschäft.
    let adjustStock (market: StationMarket) (id: CommodityId) delta =
        let i = idx id
        refreshEntry market i (market.Entries.[i].Stock + delta)

    // Eine Bestandskante hat in beiden Richtungen dieselbe Preisbasis:
    // Kauf S -> S-1 verwendet S, Rueckverkauf S-1 -> S ebenfalls S.
    // Auf-/Abschlag und ganzzahlige Rundung sichern eine echte Handelsspanne.
    let private buyQuote (market: StationMarket) i stock =
        let def = commodities.[i]
        let reference, _ = computePrice def.BasePrice (marketFactor market.SectorId market.StationKind def.Id) stock market.Entries.[i].MaxStock
        int (System.Math.Ceiling(float reference * 1.08))

    let private sellQuote (market: StationMarket) i stock =
        let def = commodities.[i]
        let reference, _ = computePrice def.BasePrice (marketFactor market.SectorId market.StationKind def.Id) (stock + 1) market.Entries.[i].MaxStock
        max 1 (int (System.Math.Floor(float reference * 0.92)))

    /// Exakter Preis fuer das naechste einzelne Stueck; 0 bedeutet ausverkauft.
    let buyPrice (market: StationMarket) (id: CommodityId) =
        let i = idx id
        let entry = market.Entries.[i]
        if entry.Stock <= 0 then 0 else buyQuote market i entry.Stock

    /// Exakter Erloes fuer das naechste einzelne Stueck; 0 bedeutet Stationslager voll.
    let sellPrice (market: StationMarket) (id: CommodityId) =
        let i = idx id
        let entry = market.Entries.[i]
        if entry.Stock >= entry.MaxStock then 0 else sellQuote market i entry.Stock

    let private payout terms (cargo: CargoHold) (id: CommodityId) bid =
        let dataBonus = if id = CommodityId.NavData && terms.SignalLevel >= 2 then 35 else 0
        let councilBonus = if terms.FixerLevel >= 3 then 15 else 0
        let margin = max 0.0 (float bid - cargo.AvgCost.[idx id])
        bid + int (margin * float (dataBonus + councilBonus) / 100.0)

    /// Ohne Ware an Bord ist der spätere Einstand unbekannt. Dann gilt der Basiskurs.
    let pilotSellPrice terms market cargo id =
        let bid = sellPrice market id
        if bid = 0 || quantityOf cargo id = 0 then bid else payout terms cargo id bid

    /// Kauft bis zu `amount` Einheiten vom Stationsmarkt in den Frachtraum.
    let buyGood (market: StationMarket) (cargo: CargoHold) (credits: int) (id: CommodityId) (amount: int) =
        let i = idx id
        let entry = market.Entries.[i]
        let def = commodities.[i]
        if amount <= 0 then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Ungültige Menge." }
        elif entry.Stock <= 0 then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = def.Name + " ist hier ausverkauft." }
        elif freeVolume cargo < def.UnitVolume then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Frachtraum voll." }
        elif credits < buyPrice market id then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Nicht genug Credits." }
        else
            let maxBySpace = freeVolume cargo / max 1 def.UnitVolume
            let limit = min amount (min entry.Stock maxBySpace)
            let mutable qty = 0
            let mutable totalCost = 0
            let mutable affordable = true
            while qty < limit && affordable do
                let unitPrice = buyQuote market i (entry.Stock - qty)
                if unitPrice <= credits - totalCost then
                    totalCost <- totalCost + unitPrice
                    qty <- qty + 1
                else
                    affordable <- false
            if qty <= 0 then
                { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Kauf nicht möglich." }
            else
                let prevQty = cargo.Quantities.[i]
                let newQty = prevQty + qty
                cargo.AvgCost.[i] <- (cargo.AvgCost.[i] * float prevQty + float totalCost) / float newQty
                cargo.Quantities.[i] <- newQty
                refreshEntry market i (entry.Stock - qty)
                { Ok = true
                  CreditsDelta = -totalCost
                  VolumeDelta = qty * def.UnitVolume
                  Message = sprintf "%dx %s für %d CR gekauft." qty def.Name totalCost }

    /// Verkauft bis zu `amount` Einheiten aus dem Frachtraum an die Station.
    let sellGoodFor terms (market: StationMarket) (cargo: CargoHold) (id: CommodityId) (amount: int) =
        let i = idx id
        let entry = market.Entries.[i]
        let def = commodities.[i]
        let owned = cargo.Quantities.[i]
        if amount <= 0 || owned <= 0 then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Keine Fracht zum Verkaufen." }
        elif entry.Stock >= entry.MaxStock then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Stationslager für " + def.Name + " voll. Fracht bleibt an Bord." }
        else
            let requested = min amount owned
            let qty = min requested (entry.MaxStock - entry.Stock)
            let mutable revenue = 0
            for unit in 0 .. qty - 1 do
                revenue <- revenue + payout terms cargo id (sellQuote market i (entry.Stock + unit))
            cargo.Quantities.[i] <- owned - qty
            if cargo.Quantities.[i] = 0 then cargo.AvgCost.[i] <- 0.0
            refreshEntry market i (entry.Stock + qty)
            { Ok = true
              CreditsDelta = revenue
              VolumeDelta = -(qty * def.UnitVolume)
              Message =
                if qty < requested then
                    sprintf "%dx %s für %d CR verkauft. Stationslager voll; %d bleiben an Bord." qty def.Name revenue (owned - qty)
                else
                    sprintf "%dx %s für %d CR verkauft." qty def.Name revenue }

    let sellGood market cargo id amount = sellGoodFor TradeTerms.standard market cargo id amount

    /// Verkauft alle vorraetigen Waren im Frachtraum auf einen Schlag.
    let sellAllFor terms (market: StationMarket) (cargo: CargoHold) =
        let mutable totalRevenue = 0
        let mutable totalUnits = 0
        let mutable freedVolume = 0
        for i in 0 .. commodities.Length - 1 do
            let qty = cargo.Quantities.[i]
            if qty > 0 then
                let res = sellGoodFor terms market cargo commodities.[i].Id qty
                if res.Ok then
                    totalRevenue <- totalRevenue + res.CreditsDelta
                    totalUnits <- totalUnits + qty - cargo.Quantities.[i]
                    freedVolume <- freedVolume - res.VolumeDelta
        let remaining = Array.sum cargo.Quantities
        if totalUnits = 0 then
            { Ok = false
              CreditsDelta = 0
              VolumeDelta = 0
              Message = if remaining > 0 then "Stationslager voll. Fracht bleibt an Bord."
                        elif cargo.Freight.IsSome then "Nur versiegelte Auftragsfracht an Bord."
                        else "Frachtraum ist leer." }
        else
            { Ok = true
              CreditsDelta = totalRevenue
              VolumeDelta = -freedVolume
              Message =
                if remaining > 0 then
                    sprintf "%d Einheiten für %d CR verkauft. Stationslager voll; %d Einheiten bleiben an Bord." totalUnits totalRevenue remaining
                else
                    sprintf "%d Einheiten Fracht für %d CR verkauft." totalUnits totalRevenue }

    let sellAll market cargo = sellAllFor TradeTerms.standard market cargo

    /// Das abgeschirmte Fach zählt zusätzlich zu den drei kaufbaren Ausbaustufen.
    let hullUpgradePrice terms baseHold (cargo: CargoHold) =
        let shieldedCapacity = if terms.FixerLevel >= 2 then 8 else 0
        let level = max 0 ((cargo.Capacity - shieldedCapacity - baseHold) / 8)
        if level >= 3 then 0 else TradeTerms.servicePrice terms (110 + level * 90)

    let cargoUpgradePriceFor terms cargo = hullUpgradePrice terms 16 cargo
    let cargoUpgradePrice cargo = cargoUpgradePriceFor TradeTerms.standard cargo

    let upgradeHullCargo terms baseHold (cargo: CargoHold) (credits: int) =
        let price = hullUpgradePrice terms baseHold cargo
        if price <= 0 then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Frachtraum bereits maximal ausgebaut." }
        elif credits < price then
            { Ok = false; CreditsDelta = 0; VolumeDelta = 0; Message = "Nicht genug Credits für Frachtraum-Ausbau." }
        else
            cargo.Capacity <- cargo.Capacity + 8
            { Ok = true
              CreditsDelta = -price
              VolumeDelta = 0
              Message = sprintf "Frachtraum auf %d t erweitert." cargo.Capacity }

    let upgradeCargoFor terms cargo credits = upgradeHullCargo terms 16 cargo credits
    let upgradeCargo cargo credits = upgradeCargoFor TradeTerms.standard cargo credits

    /// Bewegt alle Stationslager pro Sektorwechsel sanft Richtung Grundbestand,
    /// damit profitable Handelsrouten nicht dauerhaft austrocknen.
    let driftMarkets (markets: StationMarket[]) (heliosBlockaded: bool) =
        for market in markets do
            for i in 0 .. market.Entries.Length - 1 do
                let blockedMed =
                    heliosBlockaded && market.SectorId = "slag-refinery" && i = int CommodityId.MedSupplies
                if not blockedMed then
                    let commodity = commodities.[i].Id
                    let target, _ = initialStock (marketFactor market.SectorId market.StationKind commodity) commodity
                    let current = market.Entries.[i].Stock
                    if current <> target then
                        let gap = abs (target - current)
                        let step = if gap >= 6 then 2 else 1
                        let delta = if current < target then step else -step
                        refreshEntry market i (current + delta)

    /// Erzeugt ein spontanes Sektor-Marktereignis (Engpass oder Ueberschuss) und gibt Titel + Meldung zurueck.
    let triggerMarketEvent (markets: StationMarket[]) (heliosBlockaded: bool) (sectorIndex: int) (goodIndex: int) (shortage: bool) =
        if sectorIndex < 0 || sectorIndex >= markets.Length || goodIndex < 0 || goodIndex >= commodities.Length then
            None
        else
            let market = markets.[sectorIndex]
            let blockedMed =
                heliosBlockaded && market.SectorId = "slag-refinery" && goodIndex = int CommodityId.MedSupplies
            if blockedMed then None
            else
                let good = commodities.[goodIndex]
                let entry = market.Entries.[goodIndex]
                let delta = if shortage then -5 else 6
                let nextStock = max 0 (min entry.MaxStock (entry.Stock + delta))
                if nextStock = entry.Stock then None
                else
                    refreshEntry market goodIndex nextStock
                    let port = (Universe.sectorByIndex sectorIndex).Outpost.Name
                    let title = if shortage then "MARKT-ENGPASS" else "MARKT-ÜBERSCHUSS"
                    let detail =
                        if shortage then sprintf "%s meldet Knappheit bei %s — hohe Ankaufspreise!" port good.Name
                        else sprintf "%s meldet Überschuss an %s — günstige Einkaufskurse!" port good.Name
                    Some (title, detail)
