namespace SpaceEngine

open Browser.Dom
open Browser.Types

type ShopUi =
    { Root: HTMLElement
      Shipyard: ShipyardUi
      FreightDesk: FreightUi
      Buttons: HTMLButtonElement[]
      Prices: HTMLElement[]
      Credits: HTMLElement
      Heading: HTMLElement
      Notice: HTMLElement
      Cargo: HTMLElement
      CargoMeter: HTMLProgressElement
      MarketButtons: HTMLButtonElement[]
      SellButtons: HTMLButtonElement[]
      Stock: HTMLElement[]
      Owned: HTMLElement[]
      SellAll: HTMLButtonElement
      Expand: HTMLButtonElement
      RepairCredits: HTMLButtonElement
      RepairScrap: HTMLButtonElement
      RepairStatus: HTMLElement
      Next: HTMLButtonElement
      mutable Purchase: int
      mutable TradeAction: int
      mutable RepairAction: int
      mutable TravelAction: int
      mutable Depart: bool
      mutable Advance: bool
      mutable TabChanged: bool }

[<RequireQualifiedAccess>]
module Shop =
    let private cargoIcon (id: CommodityId) =
        "<i class='cargo-icon cargo-icon-" + string (int id) + "' aria-hidden='true'></i>"

    let create () =
        let element id = document.getElementById id
        let body = element "market-rows"
        for good in TradeSystem.commodities do
            let i = string (int good.Id)
            let row = document.createElement "tr"
            row.innerHTML <-
                "<th scope='row'><span class='good-name'>" + cargoIcon good.Id + "<span>" + good.Name + "<small>" + string good.UnitVolume + " t / Einheit</small><small class='market-intel' id='intel-" + i + "'></small></span></span></th>" +
                "<td id='stock-" + i + "'></td><td id='owned-" + i + "'></td>" +
                "<td><button type='button' id='trade-buy-" + i + "' aria-label='Eine Einheit " + good.Name + " kaufen'></button></td>" +
                "<td><button type='button' id='trade-sell-" + i + "' aria-label='Eine Einheit " + good.Name + " verkaufen'></button></td>"
            body.appendChild row |> ignore
        let ui =
            { Root = element "hangar"
              Shipyard = ShipyardUi.create ()
              FreightDesk = FreightUi.create ()
              Buttons = Array.init 4 (fun i -> unbox<HTMLButtonElement> (element ("buy-" + string i)))
              Prices = Array.init 4 (fun i -> element ("price-" + string i))
              Credits = element "shop-credits"
              Heading = element "shop-heading"
              Notice = element "shop-notice"
              Cargo = element "cargo-status"
              CargoMeter = unbox<HTMLProgressElement> (element "cargo-meter")
              MarketButtons = Array.init 6 (fun i -> unbox<HTMLButtonElement> (element ("trade-buy-" + string i)))
              SellButtons = Array.init 6 (fun i -> unbox<HTMLButtonElement> (element ("trade-sell-" + string i)))
              Stock = Array.init 6 (fun i -> element ("stock-" + string i))
              Owned = Array.init 6 (fun i -> element ("owned-" + string i))
              SellAll = unbox<HTMLButtonElement> (element "sell-all")
              Expand = unbox<HTMLButtonElement> (element "cargo-upgrade")
              RepairCredits = unbox<HTMLButtonElement> (element "repair-credits")
              RepairScrap = unbox<HTMLButtonElement> (element "repair-scrap")
              RepairStatus = element "repair-status"
              Next = unbox<HTMLButtonElement> (element "next-sector")
              Purchase = -1
              TradeAction = -1
              RepairAction = -1
              TravelAction = -1
              Depart = false
              Advance = false
              TabChanged = false }
        for i in 0..3 do
            ui.Buttons.[i].addEventListener ("click", fun _ -> ui.Purchase <- i)
        (element "depart").addEventListener ("click", fun _ -> ui.Depart <- true)
        ui.Next.addEventListener ("click", fun _ -> ui.Advance <- true)
        for i in 0..5 do
            ui.MarketButtons.[i].addEventListener ("click", fun _ -> ui.TradeAction <- i)
            ui.SellButtons.[i].addEventListener ("click", fun _ -> ui.TradeAction <- i + 6)
        ui.SellAll.addEventListener ("click", fun _ -> ui.TradeAction <- 12)
        ui.Expand.addEventListener ("click", fun _ -> ui.TradeAction <- 13)
        ui.RepairCredits.addEventListener ("click", fun _ -> ui.RepairAction <- 1)
        ui.RepairScrap.addEventListener ("click", fun _ -> ui.RepairAction <- 2)
        let tabs = [| "market"; "equipment"; "routes"; "skills"; "blockade"; "ships" |]
        let selectTab selected =
            for name in tabs do
                if name = selected then (element (name + "-panel")).removeAttribute "hidden"
                else (element (name + "-panel")).setAttribute ("hidden", "")
                (element ("tab-" + name)).setAttribute ("aria-pressed", if name = selected then "true" else "false")
        for name in tabs do
            (element ("tab-" + name)).addEventListener ("click", fun _ ->
                selectTab name
                ui.TabChanged <- true)
        // Enter/Leertaste auf einem Button soll ihn bedienen, nicht den Flug starten.
        ui.Root.addEventListener ("keydown", fun e ->
            let key = unbox<KeyboardEvent> e
            if key.code = "Enter" || key.code = "Space" then e.stopPropagation ())
        ui

    let refresh (ui: ShopUi) terms (wallet: Wallet) (levels: int[]) (cargo: CargoHold) (market: StationMarket) (outpost: Outpost) (canAdvance: bool) fleet =
        ui.Credits.textContent <- string wallet.Credits + " CR"
        let used = TradeSystem.usedVolume cargo
        let contractSpace = match cargo.Freight with Some job -> sprintf " · %d t versiegelt" job.Tonnes | None -> ""
        ui.Cargo.textContent <- string used + " / " + string cargo.Capacity + " t" + (if used = cargo.Capacity then " · VOLL" else "") + contractSpace
        ui.CargoMeter.max <- float cargo.Capacity
        ui.CargoMeter.value <- float used
        ui.Next.disabled <- not canAdvance
        let sale = TradeRoutes.quoteCargoFor terms market cargo
        ui.SellAll.disabled <- not sale.Ok
        ui.SellAll.textContent <- if sale.Ok then sprintf "FRACHT VERKAUFEN · %d CR" sale.CreditsDelta else "FRACHT VERKAUFEN"
        let expansion = TradeSystem.hullUpgradePrice terms ShipCatalog.hulls.[fleet.Active].Hold cargo
        ui.Expand.disabled <- expansion = 0 || wallet.Credits < expansion || not outpost.HasUpgrades
        ui.Expand.textContent <-
            if expansion = 0 then "FRACHTRAUM MAXIMAL"
            else
                let price = sprintf "AUF %d t AUSBAUEN · %d CR" (cargo.Capacity + 8) expansion
                if not outpost.HasUpgrades then price + " · AUSBAU AN ANDEREM HAFEN"
                elif wallet.Credits < expansion then sprintf "%s · %d CR FEHLEN" price (expansion - wallet.Credits)
                else price
        for i in 0..5 do
            let good = TradeSystem.commodities.[i]
            let entry = market.Entries.[i]
            let ask = TradeSystem.buyPrice market good.Id
            let bid = TradeSystem.pilotSellPrice terms market cargo good.Id
            ui.Stock.[i].textContent <- string entry.Stock + " / " + string entry.MaxStock
            ui.Owned.[i].textContent <- string (TradeSystem.quantityOf cargo good.Id)
            ui.Owned.[i].title <- "Durchschnittlicher Einstand: " + string (int (System.Math.Round cargo.AvgCost.[i])) + " CR / Einheit"
            ui.MarketButtons.[i].textContent <- if ask = 0 then "AUSVERKAUFT" else string ask + " CR"
            ui.SellButtons.[i].textContent <- if bid = 0 then "LAGER VOLL" else string bid + " CR"
            ui.MarketButtons.[i].disabled <- ask = 0 || wallet.Credits < ask || TradeSystem.freeVolume cargo < good.UnitVolume
            ui.SellButtons.[i].disabled <- bid = 0 || TradeSystem.quantityOf cargo good.Id = 0
        for i in 0..3 do
            let available = Economy.available wallet levels i
            let price = Economy.priceFor terms levels i
            ui.Buttons.[i].disabled <- not available || wallet.Credits < price || not outpost.HasUpgrades
            ui.Prices.[i].textContent <-
                if not available then (if i = 0 then "INSTALLIERT" else "MAXIMAL AUSGEBAUT")
                else string price + " CR" + (if i > 0 then " · STUFE " + string (levels.[i-1] + 1) else "")

    let show (ui: ShopUi) (outpost: Outpost) =
        ui.Purchase <- -1
        ui.TradeAction <- -1
        ui.RepairAction <- -1
        ui.TravelAction <- -1
        ui.Shipyard.Request <- None
        ui.FreightDesk.Request <- None
        ui.Depart <- false
        ui.Advance <- false
        ui.Heading.textContent <- outpost.Name
        ui.Root.setAttribute ("data-port", outpost.Id)
        ui.Notice.textContent <-
            match outpost.Id with
            | "station-kestrel" -> "Kestrel erteilt vorläufige Dockfreigabe. Im Kurierregister gilt deine SIN weiter als terminiert."
            | "station-helios" -> "Schichtbericht Helios-IV. Todesfälle im Toleranzbereich. Beschwerdestelle geschlossen."
            | "station-bazaar" -> "Drift-9 · Fixer-Frequenz offen. Kipper funkt aus dem Schrottdock."
            | "station-kepler" -> "Werftfunk Kepler-07. Continuity-Drohnen prüfen die Lizenzen im Anflug."
            | "station-aether" -> "Relais Theta empfängt auf RT-9. Auch gespeicherte Notrufe laufen über diesen Kanal."
            | "station-bastion" -> "Flottenregister Morgenlicht. Immunität gilt ausschließlich für Platin-SINs."
            | _ -> outpost.Name + " · Angedockt."
        ui.Root.removeAttribute "hidden"
        document.exitPointerLock ()
        (ui.Root.querySelector(".station-tabs [aria-pressed='true']") :?> HTMLButtonElement).focus ()
        ui.Root.scrollTop <- 0.0

    let refreshRoutes (ui: ShopUi) terms (wallet: Wallet) (cargo: CargoHold) (markets: StationMarket[]) origin trading (profit: float) blockade =
        let element id = document.getElementById id
        if trading then
            (element "tab-routes").removeAttribute "hidden"
            ui.Next.setAttribute ("hidden", "")
        else
            (element "tab-routes").setAttribute ("hidden", "")
            ui.Next.removeAttribute "hidden"
            if (element "tab-routes").getAttribute "aria-pressed" = "true" then
                (element "tab-market").click ()
        let used = TradeSystem.usedVolume cargo
        (element "route-summary").textContent <-
            "Handelsergebnis: " + string (int profit) + " / 1.000 CR · Handelsmarge und Frachthonorare abzüglich Geleitschutz. Fracht: " + string used + "/" + string cargo.Capacity + " t."
        let cards = element "route-cards"
        cards.innerHTML <- ""
        let forecastMarkets =
            if trading then markets |> Array.mapi (fun i m -> Blockade.forecastMarket blockade i m)
            else markets
        for i in 0 .. TradeSystem.commodities.Length - 1 do
            let intel = element ("intel-" + string i)
            if not (isNull intel) then
                if trading then
                    let good = TradeSystem.commodities.[i]
                    let ask = TradeSystem.buyPrice markets.[origin] good.Id
                    let avg = TradeRoutes.galacticAverageSell forecastMarkets good.Id
                    match TradeRoutes.bestNeighborBid terms forecastMarkets cargo origin good.Id with
                    | Some(target, bid) ->
                        let margin = if ask > 0 then bid - ask else 0
                        let port = Universe.sectors.[target].Outpost.Name
                        let suffix = if margin > 0 then sprintf " (+%d CR)" margin else ""
                        intel.textContent <- sprintf "Ø %d CR · Beste Route: %s %d CR%s" avg port bid suffix
                        intel.className <- if margin > 0 then "market-intel deal" else "market-intel"
                    | None ->
                        intel.textContent <- if avg > 0 then sprintf "Ø %d CR im Netz" avg else ""
                        intel.className <- "market-intel"
                else
                    intel.textContent <- ""
                    intel.className <- "market-intel"
        if trading then
            let manifest = Blockade.manifestLeg blockade origin
            let escortPort = blockade.EscortPort
            let transportPort = blockade.TransportPort
            for target in TradeRoutes.neighbors origin do
                let sector = Universe.sectors.[target]
                let market = forecastMarkets.[target]
                let quote, margin = TradeRoutes.quoteSaleFor terms market cargo
                let saleable = TradeSystem.goodsVolume cargo
                let remaining = saleable + quote.VolumeDelta
                let honorarium = match cargo.Freight with Some job when job.Destination = target -> job.Fee | _ -> 0
                let fee = TradeRoutes.convoyFee terms escortPort target
                let count = (TradeRoutes.patrol target).Length
                let card = document.createElement "article"
                card.className <- "route-card"
                let manifestLabel =
                    match manifest with
                    | Some leg when leg.NextPort = Some target -> "Frachtbrief nach Helios · "
                    | _ -> ""
                let freightLabel =
                    match cargo.Freight with
                    | Some job when TradeRoutes.nextHop origin job.Destination = Some target -> "Lieferroute · "
                    | _ -> ""
                let fleetBadges =
                    let tags =
                        [ if transportPort = target then yield "<span>EIGENER TRANSPORTER · +3 TREIBSTOFF</span>"
                          if escortPort = target then yield "<span>EIGENE ESKORTE STATIONIERT</span>" ]
                    if List.isEmpty tags then ""
                    else "<div class='fleet-tags'>" + String.concat "" tags + "</div>"
                let prices =
                    TradeSystem.commodities |> Array.map (fun good ->
                        let price = TradeSystem.pilotSellPrice terms market cargo good.Id
                        let trend =
                            if terms.FixerLevel < 1 then ""
                            else
                                match market.Entries.[int good.Id].Trend with
                                | Bargain -> "<small>Günstig</small>"
                                | HighDemand -> "<small>Hohe Nachfrage</small>"
                                | Normal -> "<small>Normal</small>"
                        "<span>" + cargoIcon good.Id + good.Name + " <b>" + (if price = 0 then "VOLL" else string price + " CR") + "</b>" + trend + "</span>") |> String.concat ""
                card.innerHTML <-
                    "<div class='port-portrait' data-port='" + sector.Id + "' aria-hidden='true'></div>" +
                    "<p class='eyebrow'>" + freightLabel + manifestLabel + (if count = 0 then "GESICHERTER KORRIDOR" else string count + " PIRATEN BEI EIGENFLUG") + "</p><h2>" + sector.Outpost.Name + "</h2>" + fleetBadges +
                    "<p class='route-value'>Verkaufserlös am Ziel<strong>" + string quote.CreditsDelta + " CR</strong><small>" +
                    (if used = 0 then "Frachtraum leer." elif saleable = 0 then "Nur versiegelte Auftragsfracht." elif remaining = 0 then "Eigene Ware vollständig verkäuflich." else string remaining + " t eigene Ware bleiben an Bord.") +
                    (if honorarium > 0 then sprintf " Zusätzlich %d CR bei Ablieferung." honorarium else "") + "</small></p>" +
                    "<p class='eyebrow'>ANKAUF DORT / NÄCHSTE EINHEIT</p><div class='route-prices'>" + prices + "</div>"
                let depart escorted label =
                    let button = document.createElement "button" :?> HTMLButtonElement
                    button.textContent <- label
                    let net = System.Math.Round(margin + float honorarium - (if escorted then float fee else 0.0), 1)
                    let result = document.createElement "strong"
                    result.className <- if net < 0.0 then "route-net loss" else "route-net"
                    let signed = (if net > 0.0 then "+" else "") + (string net).Replace(".", ",")
                    result.textContent <- "Handelsergebnis " + signed + " CR"
                    button.appendChild result |> ignore
                    button.setAttribute ("data-route", string target)
                    button.setAttribute ("data-escort", if escorted then "true" else "false")
                    button.disabled <- escorted && wallet.Credits < fee
                    button.addEventListener ("click", fun _ -> ui.TravelAction <- target * 2 + (if escorted then 1 else 0))
                    card.appendChild button |> ignore
                if count > 0 then
                    depart true (if escortPort = target then "EIGENE ESKORTE · KOSTENLOS" else "MIT GELEITSCHUTZ · " + string fee + " CR")
                depart false (if count = 0 then "SICHER REISEN · KOSTENLOS" else sprintf "EIGENFLUG · 0 CR · PIRATEN IN %d S" (int (TradeRoutes.patrolDelay terms)))
                cards.appendChild card |> ignore

    let refreshRepair (ui: ShopUi) terms (wallet: Wallet) (cargo: CargoHold) (ship: Ship) (outpost: Outpost) =
        let missing = Economy.repairNeeded ship
        let hullNow = int (System.Math.Floor ship.Hull)
        let hullMax = int (System.Math.Round ship.MaxHull)
        let cost = Economy.repairCostFor terms ship
        let scrap = TradeSystem.quantityOf cargo CommodityId.TitaniumScrap
        ui.RepairStatus.textContent <-
            if missing <= 0 then sprintf "%d / %d Hülle · INTAKT" hullMax hullMax
            else sprintf "%d / %d Hülle · −%d Schaden" hullNow hullMax missing
        ui.RepairCredits.disabled <- not outpost.HasRepair || missing <= 0 || wallet.Credits < cost
        ui.RepairCredits.textContent <-
            if missing <= 0 then "HÜLLE INTAKT"
            elif not outpost.HasRepair then "KEINE REPARATUR"
            elif wallet.Credits < cost then sprintf "REPARIEREN · %d CR (%d CR FEHLEN)" cost (cost - wallet.Credits)
            else sprintf "VOLL REPARIEREN · %d CR" cost
        ui.RepairScrap.disabled <- not outpost.HasRepair || missing <= 0 || scrap < 1
        ui.RepairScrap.textContent <-
            if missing <= 0 then "KEIN SCHADEN"
            elif scrap < 1 then "1 t TITANSCHROTT (+35) · 0 AN BORD"
            else sprintf "1 t TITANSCHROTT (+35) · %d AN BORD" scrap

    let hide (ui: ShopUi) = ui.Root.setAttribute ("hidden", "")
