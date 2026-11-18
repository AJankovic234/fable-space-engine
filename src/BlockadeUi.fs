namespace SpaceEngine

open Browser.Dom
open Browser.Types

type YardCard =
    { Materials: HTMLElement; Destination: HTMLSelectElement; Button: HTMLButtonElement
      mutable Select: unit -> unit; mutable Action: BlockadeAction }

type BlockadeUi =
    { Cards: YardCard[]; mutable Request: BlockadeAction option }

[<RequireQualifiedAccess>]
module BlockadeUi =
    let private element id = document.getElementById id
    let private button id = element id :?> HTMLButtonElement
    let private visible (node: HTMLElement) enabled =
        if enabled then node.removeAttribute "hidden" else node.setAttribute ("hidden", "")
    let private portName port = Universe.sectors.[port].Outpost.Name

    let create () =
        let grid = element "yard-blueprints"
        let cards =
            Blockade.blueprints |> Array.map (fun spec ->
                let art =
                    match spec.Kind with
                    | 1 -> "/assets/ships/yard-transporter.webp"
                    | 2 -> "/assets/ships/yard-escort.webp"
                    | _ -> "/assets/ships/yard-bomber.webp"
                let card = document.createElement "article"
                card.className <- "yard-order"
                card.innerHTML <- "<div class='yard-art'><img alt='' loading='lazy' /></div><h3></h3><p></p><div class='yard-materials'></div><label>Einsatzhafen<select></select></label><button type='button'></button>"
                let img = card.querySelector("img") :?> HTMLImageElement
                img.src <- art
                img.alt <- spec.Name + " auf dem Baudock von Kepler-07"
                card.querySelector("h3").textContent <- spec.Name
                card.querySelector("p").textContent <- spec.Description
                let select = card.querySelector("select") :?> HTMLSelectElement
                select.setAttribute ("aria-label", "Einsatzhafen für " + spec.Name)
                for port in 0 .. Universe.sectors.Length - 1 do
                    if Blockade.validDestination spec.Kind port then
                        let option = document.createElement "option" :?> HTMLOptionElement
                        option.value <- string port
                        option.textContent <- portName port
                        select.appendChild option |> ignore
                select.value <- string Blockade.helios
                select.disabled <- spec.Kind = 3
                let submit = card.querySelector("button") :?> HTMLButtonElement
                submit.setAttribute ("data-build", string spec.Kind)
                grid.appendChild card |> ignore
                { Materials = card.querySelector(".yard-materials") :?> HTMLElement; Destination = select
                  Button = submit; Select = ignore; Action = Commission(spec.Kind, Blockade.helios) })
        let ui = { Cards = cards; Request = None }
        for card in cards do
            card.Button.addEventListener ("click", fun _ -> ui.Request <- Some card.Action)
            card.Destination.addEventListener ("change", fun _ -> card.Select ())
        for id,action in [ "convoy-clearance", BuyClearance; "convoy-filters", UnloadFilters; "convoy-bomber", FlyBomber; "convoy-escort", FlyEscort ] do
            (button id).addEventListener ("click", fun _ -> ui.Request <- Some action)
        (button "convoy-market-news").addEventListener ("click", fun _ ->
            (button "tab-blockade").click ()
            (button "tab-blockade").focus ())
        ui

    let private dispatch state sector =
        if state.Lifted then
            "Orlov · Werkstor Helios", "Der Korridor ist offen",
            "»Die Sechs hat festgemacht. Ich hab die Kisten selber gezählt. Ruf an, wenn du wieder hier bist.«"
        elif state.Permit = 2 then
            "Orlov · Schicht drei", "Die Filter sind im Werk",
            "»Sind da. Schicht drei kann morgen wieder runter. Die anderen Frachter stehen immer noch vor dem Tor.«"
        elif sector = Blockade.drift then
            "Kipper · Liegeplatz Drift-9", "Eine Kennung für Frachter Sechs",
            (if state.Permit = 0 then "»Auf der Zollliste ist noch ein Wartungsschlepper frei. Gib mir 35 Credits und ich setz die Sechs auf seinen Platz. Das geht genau einmal.«"
             else "»Die Nummer steht auf deinem Frachtbrief. Zeig ihn in Helios, dann geben sie die Filter frei.«")
        elif sector = Blockade.kepler && state.BomberReady then
            "Schichtleitung · Kepler-07", "Der Bomber ist überführt",
            "»Die Crew hat sich aus Helios gemeldet. Der Bomber steht bei ihnen im Dock. Du kannst ihn dort übernehmen.«"
        elif sector = Blockade.kepler && state.BuildKind = 3 then
            "Schichtleitung · Kepler-07", "Triebwerkstest am Baudock",
            "»Beide Kerne sitzen. Die Crew packt schon ihre Sachen für Helios.«"
        elif sector = Blockade.kepler then
            "Schichtleitung · Kepler-07", "Ein Platz im Baudock",
            "»Der Rahmen für den Bomber ist da. Bring zwei Plasmakerne und eine Einheit Titanschrott. Den Rest kriegen wir hier hin.«"
        else
            "Orlov · Werkstor Helios", "Frachter Sechs wartet am Tor",
            "»Die Kiste mit den Filtern liegt auf Frachter Sechs. Von meinem Fenster aus kann ich ihn sehen. Die lassen ihn einfach nicht rein.«"

    let private updateYard ui state sector wallet cargo =
        let inYard = sector = Blockade.kepler
        visible (element "yard-blueprints") inYard
        let buildStatus =
            if state.BuildKind > 0 then
                sprintf "%s im Bau für %s. Nach deiner nächsten Routenreise ist das Schiff am Einsatzhafen."
                    Blockade.blueprints.[state.BuildKind - 1].Name (portName state.BuildPort)
            elif inYard then "Der Bauplatz ist frei. Mit dem Auftrag geht das Material aus deinem Frachtraum an die Werft. Bau und Überführung dauern eine Routenreise."
            else "Der Bauplatz ist frei. Aufträge und Einsatzhäfen legst du am Dock von Kepler-07 fest."
        (element "yard-status").textContent <- buildStatus
        let fleet =
            [ if state.TransportPort >= 0 then yield "Transporter → " + portName state.TransportPort
              if state.EscortPort >= 0 then yield "Eskorte → " + portName state.EscortPort
              if state.BomberReady then yield "Torpedobomber → Helios-IV" ]
        let line = element "yard-fleet"
        line.textContent <- String.concat "  ·  " fleet
        visible line (not (List.isEmpty fleet))
        if inYard then
            ui.Cards |> Array.iteri (fun index card ->
                let spec = Blockade.blueprints.[index]
                let servicePort =
                    match spec.Kind with
                    | 1 -> state.TransportPort
                    | 2 -> state.EscortPort
                    | _ -> if state.BomberReady then Blockade.helios else -1
                let deployed = servicePort >= 0
                if deployed then card.Destination.value <- string servicePort
                let materials =
                    spec.Materials |> Array.map (fun (good, required) ->
                        let owned = TradeSystem.quantityOf cargo good
                        sprintf "%s %d/%d" (TradeSystem.commodityDef good).Name owned required)
                card.Materials.textContent <- "An Bord / benötigt · " + String.concat " · " materials
                visible card.Materials (not deployed)
                card.Select <- fun () ->
                    let port = int card.Destination.value
                    let action = if deployed then Reassign(spec.Kind, port) else Commission(spec.Kind, port)
                    card.Action <- action
                    card.Button.disabled <- not (Blockade.available state sector wallet cargo action)
                    card.Button.textContent <-
                        if deployed && spec.Kind = 3 then "In Helios stationiert"
                        elif deployed && card.Button.disabled then "Am Einsatzhafen stationiert"
                        elif deployed then "Einsatzhafen ändern"
                        elif state.BuildKind = spec.Kind then "Im Bau"
                        elif state.BuildKind > 0 then "Bauplatz belegt"
                        elif card.Button.disabled then "Material fehlt"
                        else spec.Name + " beauftragen"
                card.Select ())

    let refresh ui state trading sector wallet cargo (markets: StationMarket[]) =
        let tab = button "tab-blockade"
        let news = button "convoy-market-news"
        visible tab trading
        visible news trading
        if not trading then
            ui.Request <- None
            if tab.getAttribute "aria-pressed" = "true" then (button "tab-market").click ()
        else
            let sender,title,message = dispatch state sector
            (element "convoy-sender").textContent <- sender
            (element "convoy-title").textContent <- title
            (element "convoy-message").textContent <- message
            let medical = markets.[Blockade.helios].Entries.[int CommodityId.MedSupplies]
            let price = TradeSystem.sellPrice markets.[Blockade.helios] CommodityId.MedSupplies
            let bulletin = if state.Lifted then "Helios · Korridor offen" else "Helios · Zollblockade"
            news.textContent <- sprintf "%s · %d Med-Module auf Lager · %s  ›"
                bulletin medical.Stock (if price > 0 then sprintf "Grundkurs im Ankauf %d CR" price else "Lager voll")
            (element "convoy-objective").textContent <-
                match Blockade.manifestLeg state sector with
                | Some leg -> leg.Hint
                | None when state.Lifted ->
                    if state.Deliveries < 3 then sprintf "%d von 3 Versorgungsschiffen entladen. Beim nächsten Anflug auf Helios kommt die nächste Ladung an." state.Deliveries
                    else "Alle drei Versorgungsschiffe haben entladen. Das Werk fährt wieder seine regulären Schichten."
                | None when state.BomberReady -> "Der Bomber steht am Dock von Helios. Wähle dort, wer ihn beim nächsten Anflug fliegt."
                | None when state.BuildKind = 3 -> "Der Bomber wird ausgerüstet. Triff seine Crew nach deiner nächsten Routenreise am Dock von Helios."
                | None when state.Permit = 2 -> "Die Durchlasskennung ist verbraucht. Kepler kann einen Bomber für den Korridor bauen."
                | None -> "Kipper auf Drift-9 kann eine Lieferung durch den Zoll bringen. In Kepler baut die Werft einen Bomber, der den Korridor für alle öffnen kann."
            let setAction id shown action label =
                let b = button id
                visible b shown
                b.disabled <- not (Blockade.available state sector wallet cargo action)
                b.textContent <- label
            setAction "convoy-clearance" (sector = Blockade.drift && state.Permit = 0 && not state.Lifted) BuyClearance
                (sprintf "Durchlasskennung kaufen · %d CR" Blockade.clearanceCost)
            setAction "convoy-filters" (sector = Blockade.helios && state.Permit = 1) UnloadFilters
                (sprintf "Filterlieferung quittieren · %d CR Honorar" Blockade.filterFee)
            setAction "convoy-bomber" (Blockade.canSortie state sector) FlyBomber "Bomber übernehmen"
            setAction "convoy-escort" (Blockade.canSortie state sector) FlyEscort "Bomber im eigenen Kutter begleiten"
            updateYard ui state sector wallet cargo
