namespace SpaceEngine

open Fable.Core

type BlockadeState =
    { mutable Started: bool
      mutable Lifted: bool
      mutable Permit: int
      mutable BomberReady: bool
      mutable BuildKind: int
      mutable BuildPort: int
      mutable BuildLegs: int
      mutable TransportPort: int
      mutable EscortPort: int
      mutable Deliveries: int }

type BlockadeAction =
    | BuyClearance | UnloadFilters
    | Commission of kind: int * destination: int
    | Reassign of kind: int * destination: int
    | FlyBomber | FlyEscort

[<RequireQualifiedAccess>]
module Blockade =
    let helios = 1
    let drift = 2
    let kepler = 3
    let clearanceCost = 35
    let filterFee = 65

    type Blueprint =
        { Kind: int; Name: string; Materials: (CommodityId * int)[]; Description: string }

    let blueprints =
        [| { Kind = 1; Name = "Transporter"; Materials = [| CommodityId.TitaniumScrap, 2; CommodityId.FuelCells, 1 |]
             Description = "Bringt bei jedem deiner Anflüge drei Treibstoffzellen auf den Markt seines Einsatzhafens." }
           { Kind = 2; Name = "Eskorte"; Materials = [| CommodityId.TitaniumScrap, 2; CommodityId.PlasmaCores, 1 |]
             Description = "Begleitet dich auf Wunsch zum Einsatzhafen. Dort entfällt die Gebühr für Geleitschutz." }
           { Kind = 3; Name = "Torpedobomber"; Materials = [| CommodityId.PlasmaCores, 2; CommodityId.TitaniumScrap, 1 |]
             Description = "Geht nach Helios. Dort kannst du den Bomber selbst fliegen oder seiner Crew im eigenen Kutter Geleitschutz geben." } |]

    let create () =
        { Started = false; Lifted = false; Permit = 0; BomberReady = false
          BuildKind = 0; BuildPort = -1; BuildLegs = 0; TransportPort = -1; EscortPort = -1; Deliveries = 0 }

    let private isPort port = port >= 0 && port < Universe.sectors.Length

    type ManifestLeg = { NextPort: int option; Hint: string }

    let private manifestLegs =
        Universe.sectors |> Array.mapi (fun origin _ ->
            if origin = helios then Some { NextPort = None; Hint = "Frachtbrief am Helios-Dock unter „Werft & Aufträge“ quittieren." }
            else
                TradeRoutes.nextHop origin helios |> Option.map (fun next ->
                    { NextPort = Some next
                      Hint = sprintf "Frachtbrief nach Helios · Am Dock „%s“ wählen." Universe.sectors.[next].Outpost.Name }))

    let manifestLeg state port =
        if state.Permit = 1 && isPort port then manifestLegs.[port] else None

    let validDestination kind port =
        isPort port &&
        match kind with
        | 1 -> true
        | 2 -> Universe.sectors.[port].DangerLevel > 1
        | 3 -> port = helios
        | _ -> false

    let private deployed state kind =
        match kind with
        | 1 -> state.TransportPort >= 0
        | 2 -> state.EscortPort >= 0
        | 3 -> state.BomberReady
        | _ -> false

    [<Emit("Number.isInteger($0)")>]
    let private integer (_value: int) : bool = jsNative
    [<Emit("typeof $0 === 'boolean'")>]
    let private boolean (_value: bool) : bool = jsNative

    let valid state =
        not (isNull (box state)) &&
        boolean state.Started && boolean state.Lifted && boolean state.BomberReady &&
        integer state.Permit && integer state.BuildKind && integer state.BuildPort && integer state.BuildLegs &&
        integer state.TransportPort && integer state.EscortPort && integer state.Deliveries &&
        state.Permit >= 0 && state.Permit <= 2 &&
        (state.TransportPort = -1 || validDestination 1 state.TransportPort) &&
        (state.EscortPort = -1 || validDestination 2 state.EscortPort) &&
        (if state.Lifted then state.BomberReady && state.Deliveries >= 1 && state.Deliveries <= 3
         else state.Deliveries = 0) &&
        (if state.BuildKind = 0 then state.BuildPort = -1 && state.BuildLegs = 0
         else state.BuildLegs = 1 && validDestination state.BuildKind state.BuildPort && not (deployed state state.BuildKind)) &&
        (state.Started || (not state.Lifted && state.Permit = 0 && not state.BomberReady &&
                           state.BuildKind = 0 && state.TransportPort = -1 && state.EscortPort = -1))

    /// Der Rückstau betrifft das Stationslager, nie Fracht im Besitz des Piloten.
    let beginRun state (markets: StationMarket[]) =
        if not state.Started then
            let market = markets.[helios]
            let stock = market.Entries.[int CommodityId.MedSupplies].Stock
            TradeSystem.adjustStock market CommodityId.MedSupplies (min 0 (2 - stock))
            state.Started <- true

    let canSortie state sector =
        state.Started && not state.Lifted && state.BomberReady && sector = helios

    let hasEscort state destination =
        state.Started && isPort destination && state.EscortPort = destination

    let available state sector (wallet: Wallet) cargo action =
        state.Started &&
        match action with
        | BuyClearance -> sector = drift && not state.Lifted && state.Permit = 0 && wallet.Credits >= clearanceCost
        | UnloadFilters -> sector = helios && state.Permit = 1
        | FlyBomber | FlyEscort -> canSortie state sector
        | Reassign (kind, port) ->
            sector = kepler && (kind = 1 || kind = 2) && deployed state kind && validDestination kind port &&
            port <> (if kind = 1 then state.TransportPort else state.EscortPort)
        | Commission (kind, port) ->
            sector = kepler && state.BuildKind = 0 && validDestination kind port && not (deployed state kind) &&
            (blueprints.[kind-1].Materials |> Array.forall (fun (good, count) -> TradeSystem.quantityOf cargo good >= count))

    /// Vor Abbuchung werden Ort, kompletter Materialbedarf und Bauplatz gemeinsam geprüft.
    let apply state sector (wallet: Wallet) cargo (markets: StationMarket[]) action =
        if not (available state sector wallet cargo action) then false
        else
            match action with
            | BuyClearance ->
                wallet.Credits <- wallet.Credits - clearanceCost
                state.Permit <- 1
                true
            | UnloadFilters ->
                state.Permit <- 2
                Economy.earn wallet filterFee false
                if not state.Lifted then TradeSystem.adjustStock markets.[helios] CommodityId.MedSupplies 3
                true
            | Commission (kind, port) ->
                for good, count in blueprints.[kind-1].Materials do
                    let i = int good
                    cargo.Quantities.[i] <- cargo.Quantities.[i] - count
                    if cargo.Quantities.[i] = 0 then cargo.AvgCost.[i] <- 0.0
                state.BuildKind <- kind
                state.BuildPort <- port
                state.BuildLegs <- 1
                true
            | Reassign (kind, port) ->
                if kind = 1 then state.TransportPort <- port else state.EscortPort <- port
                true
            | FlyBomber | FlyEscort -> false // Launch is resolved with the live flight and combat state.

    let private unloadMedicine market = TradeSystem.adjustStock market CommodityId.MedSupplies 5

    let lift state (markets: StationMarket[]) =
        if not (canSortie state helios) then false
        else
            state.Lifted <- true
            unloadMedicine markets.[helios]
            state.Deliveries <- state.Deliveries + 1
            true

    let private medicineDue state destination =
        state.Lifted && destination = helios && state.Deliveries < 3

    let private unloadOnArrival state destination market =
        if medicineDue state destination then unloadMedicine market
        let newTransport = state.BuildKind = 1 && state.BuildLegs = 1 && state.BuildPort = destination
        if state.TransportPort = destination || newTransport then
            TradeSystem.adjustStock market CommodityId.FuelCells 3

    let forecastMarket state destination (market: StationMarket) =
        let forecast = { market with Entries = Array.copy market.Entries }
        if state.Started then unloadOnArrival state destination forecast
        forecast

    let onTravel state (markets: StationMarket[]) origin destination =
        if state.Started && (TradeRoutes.neighbors origin |> Array.contains destination) then
            unloadOnArrival state destination markets.[destination]
            if medicineDue state destination then state.Deliveries <- state.Deliveries + 1
            if state.BuildKind <> 0 then
                state.BuildLegs <- state.BuildLegs - 1
                if state.BuildLegs = 0 then
                    match state.BuildKind with
                    | 1 -> state.TransportPort <- state.BuildPort
                    | 2 -> state.EscortPort <- state.BuildPort
                    | _ -> state.BomberReady <- true
                    state.BuildKind <- 0
                    state.BuildPort <- -1
