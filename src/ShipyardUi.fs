namespace SpaceEngine

open Browser.Dom
open Browser.Types

type HullCard = { Hold: HTMLElement; Cruise: HTMLElement; Laser: HTMLElement; Action: HTMLButtonElement }
type ShipyardUi = { Hulls: HullCard[]; Kits: HTMLButtonElement[]; mutable Request: RefitOrder option }

[<RequireQualifiedAccess>]
module ShipyardUi =
    let private get id = document.getElementById id

    let private moduleCards =
        [| "laser", "Laserverstärker", "Schaden je Treffer"
           "shield", "Schildkondensator", "Schildkapazität"
           "armour", "Verstärkte Hülle", "Hüllenpunkte" |]

    let refreshEquipment fleet (levels: int[]) trained =
        for slot in 0 .. moduleCards.Length - 1 do
            let asset, name, stat = moduleCards.[slot]
            let level = levels.[slot]
            let preview = min 3 (level + 1)
            let value rank =
                let amount =
                    match slot with
                    | 0 -> ShipCatalog.laser ShipCatalog.hulls.[fleet.Active] fleet.Kit rank trained
                    | 1 -> ShipCatalog.shieldCapacity rank
                    | _ -> ShipCatalog.hullStrength rank
                (string (System.Math.Round(amount, 2))).Replace(".", ",")
            let number = string (slot + 1)
            let art = get ("module-art-" + number)
            let path = sprintf "/assets/upgrades/%s-%d.webp" asset preview
            if art.getAttribute "src" <> path then art.setAttribute ("src", path)
            art.setAttribute ("alt", sprintf "%s, Stufe %d" name preview)
            (get ("module-stats-" + number)).textContent <-
                sprintf "%s · %s%s" stat (value level) (if level < 3 then " → " + value preview else " · Maximum")
            let button = get ("buy-" + number)
            (button.querySelector "span").textContent <- if level < 3 then "AUFRÜSTEN" else "INSTALLIERT"
            button.setAttribute ("aria-label", name + " · " + (get ("price-" + number)).textContent)

    let create () =
        let hullGrid = get "hull-cards"
        let cards =
            ShipCatalog.hulls |> Array.mapi (fun i hull ->
                let card = document.createElement "article"
                card.className <- "hull-card"
                card.setAttribute ("data-hull", string i)
                card.innerHTML <-
                    "<div class='hull-art'><img loading='lazy' src='/assets/ships/" + hull.Art + ".webp' alt=''></div>" +
                    "<div class='hull-copy'><p class='eyebrow'>" + hull.Role + "</p><h3>" + hull.Name + "</h3>" +
                    "<dl><div><dt>Laderaum</dt><dd></dd></div><div><dt>Reisetempo</dt><dd></dd></div><div><dt>Laser</dt><dd></dd></div></dl>" +
                    "<button class='hangar-action' type='button'></button></div>"
                hullGrid.appendChild card |> ignore
                let values = card.querySelectorAll "dd"
                { Hold = unbox (values.item 0); Cruise = unbox (values.item 1); Laser = unbox (values.item 2)
                  Action = card.querySelector "button" :?> HTMLButtonElement })
        let kitGrid = get "ship-kits"
        let kits =
            ShipCatalog.kits |> Array.mapi (fun i kit ->
                let card = document.createElement "article"
                card.className <- "kit-card"
                card.setAttribute ("data-kit", string i)
                let art =
                    match i with
                    | 0 -> "<img class='relay-art' src='/assets/upgrades/kit-stock.svg' alt=''>"
                    | 1 -> "<img class='relay-art' src='/assets/upgrades/kit-courier.svg' alt=''>"
                    | 2 -> "<img class='relay-art' src='/assets/upgrades/kit-focus.svg' alt=''>"
                    | 3 -> "<img class='relay-art' src='/assets/upgrades/relay.webp' alt=''>"
                    | 4 -> "<img class='relay-art' src='/assets/upgrades/laser-2.webp' alt=''>"
                    | 5 -> "<img class='relay-art' src='/assets/upgrades/laser-3.webp' alt=''>"
                    | 6 -> "<img class='relay-art' src='/assets/upgrades/kit-focus.svg' alt=''>"
                    | 7 -> "<img class='relay-art' src='/assets/upgrades/armour-2.webp' alt=''>"
                    | 8 -> "<img class='relay-art' src='/assets/upgrades/kit-courier.svg' alt=''>"
                    | 9 -> "<img class='relay-art' src='/assets/upgrades/shield-3.webp' alt=''>"
                    | 10 -> "<img class='relay-art' src='/assets/upgrades/shield-2.webp' alt=''>"
                    | _ -> "<img class='relay-art' src='/assets/upgrades/laser-3.webp' alt=''>"
                card.innerHTML <- art + "<h4>" + kit.Name + "</h4><p>" + kit.Effect + "</p><button class='hangar-action' type='button'></button>"
                kitGrid.appendChild card |> ignore
                card.querySelector "button" :?> HTMLButtonElement)
        let ui = { Hulls = cards; Kits = kits; Request = None }
        for i in 0 .. cards.Length-1 do cards.[i].Action.addEventListener ("click", fun _ -> ui.Request <- Some (Hull i))
        for i in 0 .. kits.Length-1 do kits.[i].addEventListener ("click", fun _ -> ui.Request <- Some (Kit i))
        ui

    let refresh ui fleet cargo credits canRefit trading laserLevel trained =
        let button (b: HTMLButtonElement) order active price owned name =
            let label =
                match ShipCatalog.quote fleet cargo credits canRefit order with
                | Error reason ->
                    b.disabled <- true
                    if active then reason
                    elif owned then "Im Besitz · " + reason
                    else sprintf "%d CR · %s" price reason
                | Ok cost ->
                    b.disabled <- false
                    if owned then "Im Besitz · Ausrüsten" else sprintf "Kaufen & ausrüsten · %d CR" cost
            b.textContent <- label
            b.setAttribute ("aria-label", name + " · " + label)
            b.closest "article" |> Option.iter (fun card -> card.setAttribute ("data-active", if active then "true" else "false"))
        for i in 0 .. ui.Hulls.Length-1 do
            let hull, card = ShipCatalog.hulls.[i], ui.Hulls.[i]
            card.Hold.textContent <- sprintf "%d t" (ShipCatalog.capacityFor fleet cargo i)
            card.Cruise.textContent <- sprintf "%g m/s" (ShipCatalog.cruise hull fleet.Kit trained)
            let damage = System.Math.Round(ShipCatalog.laser hull fleet.Kit laserLevel trained, 2)
            card.Laser.textContent <- (string damage).Replace(".", ",")
            button card.Action (Hull i) (i = fleet.Active) hull.Price (ShipCatalog.owns fleet.Owned i) hull.Name
        (get "ship-kit-stock").hidden <- not trading
        if trading then
            for i in 0 .. ui.Kits.Length-1 do
                let kit = ShipCatalog.kits.[i]
                button ui.Kits.[i] (Kit i) (i = fleet.Kit) kit.Price (ShipCatalog.owns fleet.Kits i) kit.Name
