namespace SpaceEngine

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

[<RequireQualifiedAccess>]
module StarMap =
    // Fixed positions keep the network recognizable after every jump.
    let private layout =
        [| "outer-belt", ("Kestrel", 27, 41)
           "slag-refinery", ("Helios", 45, 24)
           "frontier-drift", ("Drift-9", 38, 65)
           "kepler-yard", ("Kepler", 62, 46)
           "aether-relay", ("Theta", 76, 13)
           "morgenlicht-bastion", ("Morgenlicht", 85, 68)
           "kaltstrom", ("Kaltstrom", 18, 11)
           "wracksaum", ("Wracksaum", 55, 86)
           "schleierpass", ("Schleierpass", 16, 75) |]
        |> Map.ofArray

    let private sectors = Universe.sectors
    let private nodes =
        sectors |> Array.mapi (fun i sector ->
            Map.tryFind sector.Id layout
            |> Option.defaultValue (sector.Name, 20 + (i % 4)*20, 25 + (i / 4)*35))

    let tryPort id = sectors |> Array.tryFindIndex (fun s -> s.Id = id)

    let nextPort origin target via =
        Option.orElse target via |> Option.bind tryPort |> Option.bind (TradeRoutes.nextHop origin)

    let route origin target =
        let ports = ResizeArray<int>()
        ports.Add origin
        let mutable next = TradeRoutes.nextHop origin target
        while next.IsSome do
            let port = next.Value
            ports.Add port
            next <- TradeRoutes.nextHop port target
        if ports.[ports.Count-1] = target then ports.ToArray() else Array.empty

    /// Concatenate two legs only when they form a route without backtracking.
    let routeThrough origin target via =
        let first, second = route origin via, route via target
        if first.Length < 2 || second.Length < 2 then None
        else
            let path = Array.append first second.[1..]
            if (Array.distinct path).Length = path.Length then Some path else None

    let validStop origin target via =
        match Option.bind tryPort target, Option.bind tryPort via with
        | Some destination, Some stop -> (routeThrough origin destination stop).IsSome
        | _ -> false

    let private edges =
        [| for origin in 0 .. sectors.Length-1 do
               for target in TradeRoutes.neighbors origin do
                   if origin < target || not (Array.contains origin (TradeRoutes.neighbors target)) then
                       yield origin,target |]

    let private get id = document.getElementById id
    let private dialog () = get "star-map"
    let visible () = (dialog ()).hasAttribute "open"

    let private draw (canvas: HTMLCanvasElement) legs =
        let ctx = canvas.getContext_2d ()
        ctx.clearRect(0.,0.,1000.,1000.)
        for origin,target in edges do
            let onPath = Array.contains (origin,target) legs || Array.contains (target,origin) legs
            let _,x0,y0 = nodes.[origin]
            let _,x1,y1 = nodes.[target]
            ctx.beginPath ()
            ctx.lineWidth <- if onPath then 5.0 else 2.4
            ctx.strokeStyle <- U3.Case1 (if onPath then "#64e6e1" else "#476a7a")
            ctx.moveTo(float x0*10., float y0*10.)
            ctx.lineTo(float x1*10., float y1*10.)
            ctx.stroke ()

    let show origin (cargo: CargoHold) (markets: StationMarket[]) terms (blockade: BlockadeState) target via onCourse onClose =
        let root = dialog ()
        let canvas = get "star-map-links" :?> HTMLCanvasElement
        let graph = get "star-map-nodes"
        graph.innerHTML <- ""
        let mutable course = target
        let mutable courseVia = via
        let mutable selectedVia = via
        let mutable selected = target |> Option.bind tryPort |> Option.defaultValue origin
        let buttons =
            nodes |> Array.mapi (fun i (label,x,y) ->
                let button = document.createElement "button" :?> HTMLButtonElement
                button.setAttribute("type", "button")
                button.className <- "star-map-node"
                button.setAttribute("data-port", sectors.[i].Id)
                button.textContent <- label
                button.setAttribute("style", sprintf "left:%d%%;top:%d%%" x y)
                graph.appendChild button |> ignore
                button)
        let set = get "star-map-set" :?> HTMLButtonElement
        let clear = get "star-map-clear" :?> HTMLButtonElement
        let stops = get "star-map-stop" :?> HTMLSelectElement
        let updateStops () =
            stops.innerHTML <- ""
            let add id label =
                let option = document.createElement "option" :?> HTMLOptionElement
                option.value <- id
                option.textContent <- label
                stops.appendChild option |> ignore
            add "" "Kein Zwischenstopp"
            for i in 0 .. sectors.Length-1 do
                if (routeThrough origin selected i).IsSome then
                    let label,_,_ = nodes.[i]
                    add sectors.[i].Id label
            stops.disabled <- stops.options.length = 1
            stops.value <- defaultArg selectedVia ""
            if stops.value = "" then
                selectedVia <- None
                stops.value <- ""
        let update () =
            let path =
                selectedVia |> Option.bind tryPort |> Option.bind (routeThrough origin selected)
                |> Option.defaultWith (fun () -> route origin selected)
            draw canvas (Array.pairwise path)
            for i in 0 .. buttons.Length-1 do
                let button = buttons.[i]
                let mark = Array.tryFindIndex ((=) i) path
                let isFreight = cargo.Freight |> Option.exists (fun freight -> freight.Destination = i)
                button.setAttribute("aria-pressed", if i = selected then "true" else "false")
                button?toggleAttribute("data-current", i = origin)
                button?toggleAttribute("data-freight", isFreight)
                let isStop = selectedVia = Some sectors.[i].Id
                button.setAttribute("aria-label", sectors.[i].Name + (if i = origin then " · eigener Sektor" else "") + (if isFreight then " · Lieferziel" else "") + (if isStop then " · Zwischenstopp" else ""))
                button.setAttribute("data-leg", if i = origin then "●" elif isFreight then "▣" else mark |> Option.map string |> Option.defaultValue "")
                button?toggleAttribute("data-waypoint", isStop)
            let sector = sectors.[selected]
            (get "star-map-title").textContent <- sector.Name
            (get "star-map-station").textContent <- sector.Outpost.Name
            (get "star-map-portrait").setAttribute("data-port", sector.Id)
            (get "star-map-summary").textContent <-
                sprintf "Gefahrenstufe %d · %s" sector.DangerLevel (if sector.Outpost.HasUpgrades then "Handel und Ausrüstung" else "Handelsmarkt")
            (get "star-map-course").textContent <-
                if selected = origin then "Eigener Sektor"
                elif path.Length = 0 then "Kein Kurs verfügbar"
                else path |> Array.map (fun i -> let label,_,_ = nodes.[i] in label) |> String.concat " → "
            let forecast = get "star-map-forecast"
            forecast.hidden <- path.Length <= 1
            if path.Length > 1 then
                let next = path.[1]
                let market = Blockade.forecastMarket blockade next markets.[next]
                let sale, _ = TradeRoutes.quoteSaleFor terms market cargo
                let fee = TradeRoutes.convoyFee terms blockade.EscortPort next
                forecast.textContent <-
                    sprintf "Nächste Etappe · %s. Verkaufserlös %d CR · Geleitschutz %d CR."
                        sectors.[next].Outpost.Name sale.CreditsDelta fee
            (get "star-map-freight").textContent <-
                match cargo.Freight with
                | Some job -> sprintf "%d t Auftragsfracht nach %s · %d CR bei Ablieferung" job.Tonnes (Freight.portName job.Destination) job.Fee
                | None -> ""
            (get "star-map-active").textContent <-
                course |> Option.bind tryPort |> Option.map (fun i ->
                    let destination,_,_ = nodes.[i]
                    match courseVia |> Option.bind tryPort with
                    | Some stop ->
                        let label,_,_ = nodes.[stop]
                        "Aktiver Kurs · " + destination + " über " + label
                    | None -> "Aktiver Kurs · " + destination)
                |> Option.defaultValue ""
            set.disabled <- path.Length <= 1 || (course = Some sector.Id && courseVia = selectedVia)
            clear.disabled <- course.IsNone
        for i in 0 .. buttons.Length-1 do
            buttons.[i].addEventListener ("click", fun _ ->
                if selected <> i then
                    selected <- i
                    selectedVia <- if course = Some sectors.[i].Id then courseVia else None
                    updateStops ()
                    update ())
        stops?onchange <- fun () ->
            selectedVia <- if stops.value = "" then None else Some stops.value
            update ()
        let change next stop =
            course <- next
            courseVia <- stop
            selectedVia <- stop
            onCourse next stop
            updateStops ()
            update ()
        set?onclick <- fun () -> change (Some sectors.[selected].Id) selectedVia
        clear?onclick <- fun () -> change None None
        root?onkeydown <- fun (event: Event) ->
            event.stopPropagation()
            if (unbox<KeyboardEvent> event).code = "F7" then
                event.preventDefault ()
                root?close()
        root?onclose <- fun () -> onClose ()
        updateStops ()
        update ()
        root?showModal()
        buttons.[selected].focus()
