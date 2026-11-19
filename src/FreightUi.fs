namespace SpaceEngine

open Browser.Dom
open Browser.Types

type FreightUi =
    { Destination: HTMLSelectElement; Offers: HTMLButtonElement[]; Deliver: HTMLButtonElement; Return: HTMLButtonElement
      mutable Origin: int; mutable Select: unit -> unit; mutable Request: FreightAction option }

[<RequireQualifiedAccess>]
module FreightUi =
    let private get id = document.getElementById id
    let private button id = get id :?> HTMLButtonElement
    let private show (node: HTMLElement) visible =
        if visible then node.removeAttribute "hidden" else node.setAttribute ("hidden","")

    let create () =
        let ui =
            { Destination = get "freight-destination" :?> HTMLSelectElement
              Offers = Freight.sizes |> Array.map (fun tonnes -> button ("freight-accept-" + string tonnes))
              Deliver = button "freight-deliver"; Return = button "freight-return"
              Origin = -1; Select = ignore; Request = None }
        ui.Destination.addEventListener ("change", fun _ -> ui.Select ())
        ui.Offers |> Array.iteri (fun i offer ->
            offer.addEventListener ("click", fun _ -> ui.Request <- Some (AcceptLoad(int ui.Destination.value, Freight.sizes.[i]))))
        ui.Deliver.addEventListener ("click", fun _ -> ui.Request <- Some DeliverLoad)
        ui.Return.addEventListener ("click", fun _ -> ui.Request <- Some ReturnLoad)
        (button "freight-route").addEventListener ("click", fun _ -> (button "tab-routes").click (); (button "tab-routes").focus ())
        ui

    let refresh ui cargo port trading terms escortPort credits =
        show (get "freight-board") trading
        if trading then
            show (get "freight-offers") cargo.Freight.IsNone
            show (get "freight-active") cargo.Freight.IsSome
            let origin = match cargo.Freight with Some job -> job.Origin | None -> port
            (get "freight-goods").textContent <- Freight.goods origin
            match cargo.Freight with
            | Some job ->
                (get "freight-manifest").textContent <-
                    sprintf "%d t versiegelt · %s → %s · %d CR bei Übergabe" job.Tonnes (Freight.portName job.Origin) (Freight.portName job.Destination) job.Fee
                (get "freight-instruction").textContent <- Freight.hint port job
                show ui.Deliver (port = job.Destination)
                show ui.Return (port = job.Origin)
                show (button "freight-route") (port <> job.Destination)
            | None ->
                if ui.Origin <> port then
                    ui.Origin <- port
                    ui.Destination.innerHTML <- ""
                    for target in TradeRoutes.neighbors port do
                        let opt = document.createElement "option" :?> HTMLOptionElement
                        opt.value <- string target
                        opt.textContent <- Freight.portName target
                        ui.Destination.appendChild opt |> ignore
                let free = TradeSystem.freeVolume cargo
                ui.Select <- fun () ->
                    let target = int ui.Destination.value
                    let fee = TradeRoutes.convoyFee terms escortPort target
                    (get "freight-passage").textContent <-
                        if fee = 0 then "Die Station stellt die Ladung. Die sichere Überfahrt ist kostenlos."
                        elif credits < fee then
                            sprintf "Die Station stellt die Ladung. Für das Geleit fehlen %d CR. Der Eigenflug ist kostenlos." (fee - credits)
                        else
                            sprintf "Die Station stellt die Ladung. Geleit kostet %d CR. Bis zur Ablieferung bleiben dir damit %d CR." fee (credits - fee)
                    ui.Offers |> Array.iteri (fun i offer ->
                        let tonnes = Freight.sizes.[i]
                        offer.disabled <- tonnes > free
                        offer.textContent <- sprintf "%d t · %d CR" tonnes (Freight.fee target tonnes)
                        let detail = document.createElement "small"
                        detail.textContent <-
                            if offer.disabled then sprintf "%d t Platz fehlen" (tonnes - free)
                            elif tonnes = free then "Annehmen · füllt den Frachtraum"
                            else sprintf "Annehmen · danach %d t frei" (free - tonnes)
                        offer.appendChild detail |> ignore)
                ui.Select ()
