namespace SpaceEngine

type TravelPlan =
    { Destination: int
      Fee: int
      Patrol: EnemyRole[] }

[<RequireQualifiedAccess>]
module TradeRoutes =
    let neighbors origin =
        if origin < 0 || origin >= Universe.sectors.Length then Array.empty
        else
            Universe.sectors.[origin].Gates
            |> Array.choose (fun gate -> Universe.sectors |> Array.tryFindIndex (fun sector -> sector.Id = gate.TargetSectorId))

    // Rückwärts über eingehende Sprungverbindungen, damit Umwege und Einbahnstrecken
    // dieselbe Navigation für Frachtbriefe und Lieferaufträge erhalten.
    let private hops =
        let routes = Universe.sectors |> Array.mapi (fun origin _ -> neighbors origin)
        Array.init routes.Length (fun destination ->
            let next = Array.create routes.Length None
            let ports = System.Collections.Generic.Queue<int>()
            ports.Enqueue destination
            while ports.Count > 0 do
                let via = ports.Dequeue()
                for origin in 0 .. routes.Length - 1 do
                    if origin <> destination && next.[origin].IsNone && Array.contains via routes.[origin] then
                        next.[origin] <- Some via
                        ports.Enqueue origin
            next)

    let nextHop origin destination = hops.[destination].[origin]

    let escortFee destination =
        let danger = (Universe.sectorByIndex destination).DangerLevel
        if danger <= 1 then 0 else 25 * danger

    let escortFeeFor terms destination = TradeTerms.servicePrice terms (escortFee destination)

    let patrolDelay terms = if terms.FixerLevel >= 2 then 20.0 else 8.0

    let patrol destination =
        match (Universe.sectorByIndex destination).DangerLevel with
        | danger when danger <= 1 -> Array.empty
        | 2 -> [| EnemyRole.Drone; EnemyRole.Scout |]
        | 3 -> [| EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Gunship |]
        | _ -> [| EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Gunship; EnemyRole.Bomber |]

    /// Keine Abkürzung zu nicht verbundenen Märkten, kein unbezahlter Geleitschutz.
    let convoyFee terms escortPort destination =
        if escortPort = destination then 0 else escortFeeFor terms destination

    let tryPlanWithEscort terms escortPort origin destination escorted credits =
        if not (neighbors origin |> Array.contains destination) then None
        else
            let fee = if escorted then convoyFee terms escortPort destination else 0
            if credits < fee then None
            else Some { Destination = destination; Fee = fee; Patrol = if escorted then Array.empty else patrol destination }

    let tryPlanFor terms origin destination escorted credits =
        tryPlanWithEscort terms -1 origin destination escorted credits

    let tryPlan origin destination escorted credits = tryPlanFor TradeTerms.standard origin destination escorted credits

    /// Exakter Mengenverkauf als Vorschau, ohne Originalmarkt oder Fracht zu verändern.
    let quoteSaleFor terms (market: StationMarket) (cargo: CargoHold) =
        let previewCargo = TradeSystem.copyCargo cargo
        let previewMarket = { market with Entries = Array.copy market.Entries }
        let sale = TradeSystem.sellAllFor terms previewMarket previewCargo
        sale, TradeSystem.saleProfit sale.CreditsDelta cargo previewCargo

    let quoteCargoFor terms market cargo = quoteSaleFor terms market cargo |> fst

    let quoteCargo market cargo = quoteCargoFor TradeTerms.standard market cargo
