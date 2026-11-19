namespace SpaceEngine

// =====================================================================================
//  SectorOps.fs  -  Sektorlage: wechselnde Einsätze draußen im Sektor (Handelsflug)
//
//  Nach der Einflugpatrouille war ein Sektor bisher still; alles spielte sich zwischen
//  Tor und Station ab. Die Sektorlage meldet nach kurzer Zeit einen Einsatz an einem
//  Ort weit weg von der Station (Erznest oder Sektorrand) und danach immer wieder
//  einen neuen. Wer hinfliegt, löst ihn aus: Schürfer unter Piratenbeschuss, ein
//  Piratenführer mit Kopfgeld oder Treibgut, um das sich Plünderer streiten.
//  Reine Logik ohne Grafik: Main startet den Kampf, das HUD zeigt den Ort.
// =====================================================================================

type OpKind =
    | Distress
    | Bounty
    | Flotsam
    | Lifeboat

type SectorOp =
    { Kind: OpKind
      Sector: int
      Position: Vec3
      Place: string
      Title: string
      Detail: string
      Roster: EnemyRole[]
      /// Prämie bei Erfolg (zusätzlich zu 100 CR je Abschuss).
      Reward: int
      /// Bergungsgut (Salvage-Art, Anzahl): bei Treibgut sofort am Ort, sonst als Dank.
      Loot: (int * int)[]
      TriggerRadius: float }

type OpSignal =
    | Quiet
    /// Neuer Sektor: Lagebild (Landschaft und Erznester) melden.
    | Arrived of label: string
    | Offered of SectorOp
    | Engaged of SectorOp

type SavedOp = { Ticket: int; Site: int; Remaining: float; Active: bool; Inbound: bool }

type SectorOpsState =
    { mutable Sector: int
      mutable Current: SectorOp option
      mutable Active: bool
      mutable Timer: float
      mutable Ticket: int
      mutable Inbound: bool
      mutable Offered: int
      mutable Completed: int }

[<RequireQualifiedAccess>]
module SectorOps =
    /// Erster Einsatz nach Betreten des Sektors, dann Pause nach jedem Einsatz.
    let FirstDelay = 18.0
    let NextDelay = 34.0
    /// Einsätze liegen nie direkt vor der Nase: mindestens so weit vom Spieler weg.
    let MinOfferDistance = 300.0

    let create () : SectorOpsState =
        { Sector = -1; Current = None; Active = false; Timer = 0.0; Ticket = 0; Inbound = false; Offered = 0; Completed = 0 }

    let reset (s: SectorOpsState) =
        s.Sector <- -1
        s.Current <- None
        s.Active <- false
        s.Inbound <- false
        s.Timer <- 0.0
        s.Offered <- 0
        s.Completed <- 0

    let private miners = [| "Sohle 4"; "Grauer Hans"; "Ida K."; "Bohrkopf 12"; "Mutter Sieben"; "Kleine Brise" |]
    let private captains = [| "Rostzahn Vell"; "Die Nadel"; "Witwe Karrass"; "Brennhaut"; "Kalter Oslo"; "Sechsfinger" |]

    let private placeName shape =
        match shape with
        | Belt -> "Gürtelrand"
        | SlagStream -> "Schlackefeld"
        | Pockets -> "Treibnest"
        | Swept -> "Leerraum"
        | Rampart -> "Wallschatten"
        | IceNests -> "Eiswolke"
        | DebrisStream -> "Trümmerstrom"
        | Boulders -> "Nebelwand"

    /// Ortsangabe im Dativ: „an der Kestrel-Ader“, „am Haldenkamm“.
    let at (place: string) =
        let feminine = [| "ader"; "Ader"; "zunge"; "halde"; "Sperrzone"; "wolke"; "wand" |]
        if feminine |> Array.exists (fun ending -> place.Contains ending) then "an der " + place else "am " + place

    /// Alle Einsatzorte des Sektors mit Namen: Erznester zuerst.
    let places (sector: int) =
        let plan = SectorFields.planFor sector
        // Die früheren ersten Einsatzorte behalten ihren Index in älteren Spielständen.
        let workplaces =
            match sector with
            | 3 -> [| "Werftkai Ost", Vec3(700., -40., -300.), false |]
            | 4 -> [| "Horchposten Band 7", Vec3(600., -20., -500.), false |]
            | _ -> plan.Nests |> Array.map (fun n -> n.Name, n.Center, true)
        Array.append workplaces
            (plan.Hotspots |> Array.map (fun p -> placeName plan.Shape, p, false))

    let kindFor (danger: int) (offered: int) =
        if danger <= 1 then [| Distress; Flotsam; Lifeboat |].[offered % 3]
        else [| Distress; Bounty; Flotsam; Lifeboat |].[offered % 4]

    let roster kind danger =
        let d = max 1 (min 4 danger)
        match kind with
        | Lifeboat -> Array.empty
        | Distress ->
            [| [| EnemyRole.Drone; EnemyRole.Drone |]
               [| EnemyRole.Drone; EnemyRole.Scout; EnemyRole.Drone |]
               [| EnemyRole.Scout; EnemyRole.Gunship; EnemyRole.Drone |]
               [| EnemyRole.Scout; EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Bomber |] |].[d - 1]
        | Bounty ->
            [| [| EnemyRole.Gunship |]
               [| EnemyRole.Gunship; EnemyRole.Drone |]
               [| EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Scout |]
               [| EnemyRole.Gunship; EnemyRole.Bomber; EnemyRole.Scout; EnemyRole.Scout |] |].[d - 1]
        | Flotsam ->
            [| Array.empty
               [| EnemyRole.Scout; EnemyRole.Drone |]
               [| EnemyRole.Scout; EnemyRole.Scout; EnemyRole.Drone |]
               [| EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Scout |] |].[d - 1]

    let reward kind danger =
        let d = max 1 (min 4 danger)
        match kind with
        | Distress -> 40 + 30 * d
        | Bounty -> 100 + 75 * d
        | Flotsam -> 0
        | Lifeboat -> 115 + 35 * d

    let loot kind danger =
        let d = max 1 (min 4 danger)
        match kind with
        | Lifeboat -> Array.empty
        | Distress -> [| Salvage.RichCryoOreKind, 2 + d / 2 |]
        | Bounty -> [| Salvage.NavDataKind, 1 |]
        | Flotsam -> [| Salvage.FuelCellsKind, 2 + d / 2; Salvage.PlasmaCoresKind, 1 + d / 2; Salvage.NavDataKind, 1 |]

    /// Plant den nächsten Einsatz: Notrufe bevorzugt an Erznestern, Kopfgeld und
    /// Treibgut eher am Sektorrand. Orte rotieren und liegen weit genug vom Spieler weg.
    let private build sector danger offered (name, position, _) : SectorOp =
        let kind = kindFor danger offered
        let title, detail =
            match kind with
            | Lifeboat ->
                "RETTUNGSKAPSEL · Druckverlust", sprintf "Weißes Blitzlicht %s · auf 65 m heran und für die Sanitätsfähre stabilisieren · %d CR" (at name) (reward kind danger)
            | Distress ->
                let miner = miners.[(offered + sector) % miners.Length]
                let role = if sector = 3 then "Werftschlepper" elif sector = 4 then "Messfähre" else "Schürfer"
                sprintf "NOTRUF · %s »%s«" role miner,
                sprintf "Piraten %s · hinfliegen und vertreiben · %d CR und Erz als Dank" (at name) (reward kind danger)
            | Bounty ->
                let captain = captains.[(offered * 5 + sector) % captains.Length]
                sprintf "KOPFGELD · »%s«" captain,
                sprintf "Piratenführer mit Begleitschutz %s · %d CR Prämie" (at name) (reward kind danger)
            | Flotsam ->
                "TREIBGUT · Frachtcontainer",
                if danger <= 1 then sprintf "Ungesicherte Ladung %s · bergen, solange sie treibt" (at name)
                else sprintf "Ladung %s · Plünderer haben den Funk auch gehört" (at name)
        let roster = roster kind danger
        { Kind = kind; Sector = sector; Position = position; Place = name; Title = title; Detail = detail
          Roster = roster; Reward = reward kind danger; Loot = loot kind danger
          TriggerRadius = if kind = Lifeboat then 65.0 elif roster.Length = 0 then 90.0 else 320.0 }

    let plan sector danger ticket (player: Vec3) =
        let kind = kindFor danger ticket
        let all = places sector
        let preferred = all |> Array.filter (fun (_, _, nest) -> nest = (kind = Distress))
        let pool = if preferred.Length > 0 then preferred else all
        let far = pool |> Array.filter (fun (_, p, _) -> Vec3.distance p player >= MinOfferDistance)
        let site =
            if far.Length > 0 then far.[(ticket / (if danger <= 1 then 3 else 4) + sector) % far.Length]
            else pool |> Array.maxBy (fun (_, p, _) -> Vec3.distance p player)
        build sector danger ticket site

    let private random = System.Random()
    let private offer (s: SectorOpsState) sector danger player =
        let n = random.Next(60000)
        let ticket =
            match s.Current with
            | None when s.Offered = 0 -> if danger <= 1 then n - n % 3 else n - n % 4 + n % 2
            | Some previous when kindFor danger n = previous.Kind -> n + 1
            | None when s.Offered > 0 && kindFor danger n = kindFor danger s.Ticket -> n + 1
            | _ -> n
        let op = plan sector danger ticket player
        s.Ticket <- ticket
        s.Offered <- s.Offered + 1
        s.Current <- Some op
        s.Inbound <- false
        s.Timer <- 60.0 + float (ticket % 31)
        Offered op

    let private exposed sector player =
        let def = Universe.sectorByIndex sector
        Vec3.distance player def.Outpost.Position > def.Outpost.ApproachRadius + 80.0
        && Vec3.distance player (PortWork.terminal sector) > 160.0
        && def.Gates |> Array.forall (fun gate -> Vec3.distance player gate.Position > gate.TriggerRadius + 100.0)

    /// Pro Frame im ruhigen Handelsflug. Liefert, was Main melden oder starten soll.
    let tick (s: SectorOpsState) (sector: int) (danger: int) (player: Vec3) (dt: float) =
        if s.Sector <> sector then
            s.Sector <- sector
            s.Current <- None
            s.Active <- false
            s.Inbound <- false
            s.Timer <- FirstDelay
            Arrived (SectorFields.planFor sector).Label
        elif s.Active then Quiet
        else
            match s.Current with
            | None ->
                s.Timer <- s.Timer - dt
                if s.Timer > 0.0 then Quiet
                else offer s sector danger player
            | Some op when Vec3.distance player op.Position <= op.TriggerRadius ->
                s.Active <- true
                Engaged op
            | Some op ->
                s.Timer <- s.Timer - dt
                if s.Timer > 0.0 then Quiet
                elif danger >= 2 && op.Kind <> Lifeboat && exposed sector player then
                    // A raiding wing diverts from the expired signal into the player's sector of space.
                    s.Ticket <- s.Ticket - s.Ticket % 4 + 1
                    let wing = plan sector danger s.Ticket player
                    s.Current <- Some wing
                    s.Active <- true
                    s.Inbound <- true
                    s.Timer <- 0.0
                    Engaged wing
                else offer s sector danger player

    /// Laufender Einsatz: Gegner erscheinen um diesen Punkt statt vor dem Spieler.
    let anchor (s: SectorOpsState) (sector: int) =
        if s.Active && not s.Inbound && s.Sector = sector then s.Current |> Option.map (fun op -> op.Position) else None

    let snapshot (s: SectorOpsState) sector =
        match s.Current with
        | Some op when s.Sector = sector ->
            let idx = places sector |> Array.findIndex (fun (_,p,_) -> Vec3.distanceSq p op.Position < 0.01)
            Some { Ticket = s.Ticket; Site = idx; Remaining = max 0.0 s.Timer; Active = s.Active; Inbound = s.Inbound }
        | _ -> None

    let restore (s: SectorOpsState) sector (saved: SavedOp) =
        let op = build sector Universe.sectors.[sector].DangerLevel saved.Ticket (places sector).[saved.Site]
        s.Sector <- sector
        s.Ticket <- saved.Ticket
        s.Timer <- saved.Remaining
        s.Active <- saved.Active
        s.Inbound <- saved.Inbound
        s.Offered <- 1
        s.Current <- Some op

    /// Kampf vorbei (alle Gegner weg). Nur ein im selben Sektor laufender Einsatz zählt.
    let complete (s: SectorOpsState) (sector: int) =
        match s.Current with
        | Some op when s.Active && s.Sector = sector ->
            s.Current <- None
            s.Active <- false
            s.Inbound <- false
            s.Completed <- s.Completed + 1
            s.Timer <- NextDelay
            Some op
        | _ ->
            if s.Sector <> sector then
                s.Current <- None
                s.Active <- false
            None
