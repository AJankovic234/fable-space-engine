namespace SpaceEngine

open Fable.Core

type FreightAction = AcceptLoad of destination: int * tonnes: int | DeliverLoad | ReturnLoad

[<RequireQualifiedAccess>]
module Freight =
    let sizes = [| 8; 24 |]
    let portName port = Universe.sectors.[port].Outpost.Name
    let private consignments = [| "Bohrkronen"; "Filterpatronen"; "Transponderteile"; "Werftwerkzeug"; "Messsonden"; "Ersatzpumpen" |]
    let goods origin = consignments.[origin]

    let private riskPremium = [| 0; 0; 28; 50; 70 |]
    let fee destination tonnes =
        let danger = Universe.sectors.[destination].DangerLevel
        tonnes * 6 + riskPremium.[danger]

    [<Emit("Number.isInteger($0)")>]
    let private integer (_value: int): bool = jsNative

    let private isPort value = integer value && value >= 0 && value < Universe.sectors.Length

    let private offered origin destination tonnes =
        isPort origin && isPort destination && Array.contains tonnes sizes &&
        Array.contains destination (TradeRoutes.neighbors origin)

    let valid job =
        not (isNull (box job)) && offered job.Origin job.Destination job.Tonnes &&
        integer job.Fee && job.Fee = fee job.Destination job.Tonnes

    // Der Frachtplatz ist im selben Objekt wie die Ladung reserviert. Eine Übergabe
    // verbraucht den Auftrag, bevor sein Honorar an die Buchhaltung zurückgeht.
    let apply (cargo: CargoHold) port action =
        match action, cargo.Freight with
        | AcceptLoad (destination, tonnes), None when offered port destination tonnes && TradeSystem.freeVolume cargo >= tonnes ->
            cargo.Freight <- Some { Origin = port; Destination = destination; Tonnes = tonnes; Fee = fee destination tonnes }
            Some 0
        | DeliverLoad, Some job when port = job.Destination ->
            cargo.Freight <- None
            Some job.Fee
        | ReturnLoad, Some job when port = job.Origin ->
            cargo.Freight <- None
            Some 0
        | _ -> None

    let private directions =
        Universe.sectors |> Array.mapi (fun destination _ ->
            Universe.sectors |> Array.mapi (fun origin _ ->
                if origin = destination then "Auftragsfracht am Handelsmarkt abliefern."
                else
                    match TradeRoutes.nextHop origin destination with
                    | Some next -> sprintf "Auftragsfracht · Am Dock „%s“ wählen." (portName next)
                    | None -> "Auftragsfracht an Bord."))

    let hint port (job: FreightContract) = directions.[job.Destination].[port]
