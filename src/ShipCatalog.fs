namespace SpaceEngine

open Fable.Core

type HullSpec =
    { Name: string; Role: string; Art: string; Price: int; Hold: int
      Cruise: float; Laser: float; Handling: float
      Difficulty: string; StartingCredits: int; FlightBrief: string }

type ShipKit = { Name: string; Effect: string; Price: int }
type HangarFleet = { mutable Active: int; mutable Owned: int; mutable Kit: int; mutable Kits: int }
type RefitOrder = Hull of int | Kit of int

[<RequireQualifiedAccess>]
module ShipCatalog =
    [<Literal>]
    let ShieldRelay = 3

    let rechargeDelay kit = if kit = ShieldRelay then 2.0 else Combat.ShieldRegenDelay
    let shieldCapacity level = 100.0 + 30.0 * float level
    let hullStrength level = 100.0 + 25.0 * float level

    let hulls =
        [| { Name = "Bergungskutter"; Role = "Bergung & Handel"; Art = "kutter"; Price = 200
             Hold = 16; Cruise = Ship.cutterCruise; Laser = 12.0; Handling = 1.0
             Difficulty = "Standard"; StartingCredits = 200
             FlightBrief = "Nach einem 8-t-Auftrag bleibt die halbe Ladefläche für eigenes Erz frei. Der Kutter dreht gutmütig in den Stationsanflug." }
           { Name = "Lastkahn"; Role = "Schwerer Frachter"; Art = "lastkahn"; Price = 380
             Hold = 32; Cruise = 48.0; Laser = 10.0; Handling = 0.8
             Difficulty = "Anspruchsvoll"; StartingCredits = 110
             FlightBrief = "Ein Großauftrag lässt noch acht Tonnen für eigene Ware. Die trägen Kurven verlangen frühe Kurskorrekturen, besonders wenn Piraten den Anflug stören." }
           { Name = "Sperber"; Role = "Leichte Eskorte"; Art = "sperber"; Price = 320
             Hold = 8; Cruise = 82.0; Laser = 16.0; Handling = 1.25
             Difficulty = "Experte"; StartingCredits = 60
             FlightBrief = "Ein Kleinauftrag füllt den ganzen Laderaum. Mit engen Kurven und dem stärksten Serienlaser kannst du dein knappes Handelsbudget durch Abschussprämien ergänzen." }
           { Name = "Eisvogel"; Role = "Frachtkurier"; Art = "eisvogel"; Price = 540
             Hold = 24; Cruise = 74.0; Laser = 9.0; Handling = 0.97
             Difficulty = "Entspannt"; StartingCredits = 300
             FlightBrief = "Die Doppelkufen tragen einen ganzen 24-t-Auftrag. Mit hohem Reisetempo und einer Reserve für Geleitschutz kannst du Gefechten ausweichen." } |]
    let kits =
        [| { Name = "Serientrimmung"; Effect = "Grundabstimmung des Rumpfs"; Price = 0 }
           { Name = "Kurierdüsen"; Effect = "+12 m/s Reisegeschwindigkeit"; Price = 90 }
           { Name = "Impulsfokus"; Effect = "+25 % Laserschaden"; Price = 130 }
           { Name = "Schildrelais"; Effect = "Schild lädt 2 s nach dem letzten Treffer. Serie: 3,5 s."; Price = 150 } |]

    let startFleet hull = { Active = hull; Owned = 1 <<< hull; Kit = 0; Kits = 1 }
    let create () = startFleet 0
    let owns mask index = mask &&& (1 <<< index) <> 0

    [<Emit("Number.isInteger($0)")>]
    let private integer (_n: int) : bool = jsNative

    let valid fleet =
        let selection index mask count =
            integer index && index >= 0 && index < count && integer mask && mask > 0 &&
            mask < (1 <<< count) && owns mask index
        not (isNull (box fleet)) && selection fleet.Active fleet.Owned hulls.Length &&
        selection fleet.Kit fleet.Kits kits.Length && owns fleet.Kits 0

    let capacityFor fleet (cargo: CargoHold) target =
        cargo.Capacity - hulls.[fleet.Active].Hold + hulls.[target].Hold

    let cruise (hull: HullSpec) kit trained =
        hull.Cruise + (if kit = 1 then 12.0 else 0.0) + (if trained then 6.0 else 0.0)

    let laser (hull: HullSpec) kit level trained =
        hull.Laser * (1.0 + 0.2 * float level) * (if kit = 2 then 1.25 else 1.0) * (if trained then 1.2 else 1.0)

    let quote fleet cargo credits canRefit order =
        let offer index count active owned price =
            if index < 0 || index >= count then Error "Unbekannt"
            elif index = active then Error "Im Einsatz"
            elif not canRefit then Error "Am Ausrüstungshafen verfügbar"
            else
                let cost = if owns owned index then 0 else price index
                if credits < cost then Error (sprintf "%d CR fehlen" (cost - credits)) else Ok cost
        match order with
        | Hull i ->
            match offer i hulls.Length fleet.Active fleet.Owned (fun n -> hulls.[n].Price) with
            | Ok _ when TradeSystem.usedVolume cargo > capacityFor fleet cargo i ->
                Error (sprintf "Zuerst %d t entladen" (TradeSystem.usedVolume cargo - capacityFor fleet cargo i))
            | result -> result
        | Kit i -> offer i kits.Length fleet.Kit fleet.Kits (fun n -> kits.[n].Price)

    let equip fleet cargo credits canRefit order =
        match quote fleet cargo credits canRefit order with
        | Error reason -> Error reason
        | Ok cost ->
            match order with
            | Hull i ->
                cargo.Capacity <- capacityFor fleet cargo i
                fleet.Owned <- fleet.Owned ||| (1 <<< i)
                fleet.Active <- i
            | Kit i ->
                fleet.Kits <- fleet.Kits ||| (1 <<< i)
                fleet.Kit <- i
            Ok cost
