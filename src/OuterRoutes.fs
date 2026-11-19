namespace SpaceEngine

/// Die Peilung folgt der örtlichen Arbeitsroute. Alle Punkte sind Weltkoordinaten.
module OuterRoutes =
    type Site =
        { Name: string; Guide: string; Hint: string; Approach: string; Report: string
          Port: Vec3; Relay: Vec3; Origin: Vec3; Archive: Vec3; Fee: int }

    type FlightLog = { Finds: int; Sprung: int; Tracking: bool; Engaged: bool }

    let empty = { Finds = 0; Sprung = 0; Tracking = false; Engaged = false }
    let depart log = { log with Tracking = false; Engaged = false }
    let has mask sector = (mask &&& (1 <<< sector)) <> 0
    let private v x y z = Vec3(x,y,z)
    let private site sector name guide hint approach report fee offset relayOffset archiveOffset =
        let port = Universe.sectors.[sector].Outpost.Position
        { Name = name; Guide = guide; Hint = hint; Approach = approach; Report = report; Fee = fee
          Port = port; Origin = port + offset; Relay = port + relayOffset; Archive = port + offset + archiveOffset }

    let sites =
        [| site 0 "Wrack der Kestrel-04" "Schlepper Vier" "Der Schlepper setzt hinter dem Haldenkamm gelbe Arbeitslichter."
               "Der Flugschreiber sitzt im abgerissenen Bug." "Der Schlepper hat die Bruchstelle mit einer Schleppöse gesichert. Haldens Kennung ist lesbar." 210
               (v -2180. 260. -840.) (v -920. 240. -130.) (v -68. 15. 4.)
           site 1 "Schachtofen 9" "Kühltanker Neun" "Der Tanker folgt der erstarrten Schlackezunge bis zum alten Kühlkreis."
               "Am kalten Ende der Leitung liegt der Arbeitsnachweis." "38 Marken hängen noch an der Schichtuhr. Die letzte Kühllieferung ging an einen längst stillgelegten Ofen." 280
               (v -2400. -260. 1150.) (v -1050. -125. 540.) (v 64. 34. 15.)
           site 2 "Die dreizehnte Boje" "Nachtkurier" "Nach dem zwölften Notruf antwortet ein Sender jenseits der Liegeplätze."
               "Der dreizehnte Sender speichert die ganze Schleife." "Der Nachtkurier tauscht die Batterien. Unter dem Träger der Hafenbojen liegt eine dreizehnte Stimme." 300
               (v 2680. 375. -780.) (v 960. 260. 100.) (v 15. -48. 32.)
           site 3 "Trockendock B" "Schleppverband B" "Zwei Schlepper bringen ein Rumpfsegment vom Werfthafen zur äußeren Helling."
               "Im offenen Dock sendet die Startfreigabe noch auf der alten Frequenz." "Kein Antrieb an Bord, aber die Helling richtet den Kolonierumpf noch immer auf den Startkorridor aus." 370
               (v 1960. 370. -2160.) (v 850. 170. -550.) (v -64. 38. 24.)
           site 4 "Gebrochener Resonanzring" "Peilgitter Band 7" "Das Horchgitter schwenkt zwischen dem Pulsar und einer zweiten Quelle."
               "Das abgelöste Segment antwortet oberhalb der Ringmitte. Dort erneut G drücken." "Fremde Keramik antwortet genau einen Puls zu früh. Die zweite Frequenz liefert eine Kreuzpeilung auf einen Eiskörper im Kaltstrom." 420
               (v -1890. 740. -2490.) (v -770. 275. -710.) (v 158. -38. 12.)
           site 5 "Vances Sleeve-Satellit" "Registerpatrouille" "Die Registerpatrouille dreht an der Grenze des Finsternisschattens."
               "Der Satellit ist von der Schattenseite erreichbar. Die Patrouille meldet ihren Vorbeiflug." "Die Wiederherstellungsakte wurde zuletzt von Sleeve Nummer sieben geöffnet." 490
               (v -2710. -360. -1730.) (v -1100. -165. -400.) (v 20. 50. 18.)
           site 6 "Wandernde Eislinse" "Vermesser Raureif" "Ein Vermesser kreuzt den Kometenschweif. Ein dunkler Körper driftet gegen das Eis."
               "Die schwarze Linse wirft den Puls zur Quelle zurück. G in ihrer Nähe drücken." "Unter dem Eis trägt die Linse dieselbe Teilung wie Thetas Ring. Ihr gebrochener Strahl weist ins Zollfeuer des Schleierpasses." 360
               (v 2580. 420. 1980.) (v 265. 145. 790.) (v -142. 45. 88.)
           site 7 "Der stumme Konvoi" "Bergungstender" "Am verstummten Konvoi brennt auf einem vermeintlichen Wrack ein Manövrierlicht."
               "Steuerdüsen am Erzleichter. Wer den Bogen auf der anderen Seite fliegt, erreicht das Frachtregister ungesehen." "Die Versicherung hat den Konvoi aufgegeben. Sein Frachtregister reicht für die Bergungsprämie." 560
               (v -3180. 325. 1810.) (v -1180. 240. 440.) (v -70. 18. 42.)
           site 8 "Prisma im Zollschatten" "Zollfeuer West" "Die amtliche Bake blinkt regelmäßig. Hinter dem Nebel antwortet ein zweites Feuer."
               "Das falsche Feuer sitzt seitlich am Prisma. Im großen Bogen zur dunklen Flanke fliegen." "Das Prisma zerlegt Funk in drei schmale Linien. Die falsche Zollbake wurde erst später an seinem Sockel befestigt." 510
               (v -3540. -510. -1120.) (v -1290. -180. -210.) (v -168. 32. 56.) |]

    let linked finds sector = (sector = 6 && has finds 4) || (sector = 8 && has finds 6)
    let chartComplete finds = has finds 4 && has finds 6 && has finds 8
    let chartFee = 650
    let chartReport = "Überlagert man die drei Peilungen, erscheint ein Stern mit zwei dunklen Begleitern. Das RT-9-Labor hat die Sternkarte übernommen."

    let workRadio =
        [| "Winde hält. Wer Haldens Bug sucht, folgt meinem gelben Arbeitslicht."
           "Rücklauf kalt. Schickt die nächste Kühlzelle nach draußen an Ofen neun."
           "Zwölf gehört dem Hafen. Hör nach dem letzten Ruf noch einen Takt länger hin."
           "Schlepper Zwei, nimm Zug vom Heck. Wir müssen mit dem Kiel unter den Portalkran."
           "Alle neun Spiegel stehen auf Empfang. Der zweite Puls kommt von oberhalb des Bogens."
           "Registerdienst an Außenwache. Neue Runde am Satelliten. Kennungen am Lichtsaum erfassen."
           "Ich lege eine zweite Messlinie. Seht ihr das? Das Eis läuft in die andere Richtung."
           "Am hinteren Leichter hat gerade eine Düse gezündet. Ich nehme den langen Weg außen herum."
           "Graue Wacht an Durchflug. Unser Feuer gibt Einzeltakt. Wer doppelt blinkt, gehört nicht zu uns." |]

    let guidePose sector time =
        let s = sites.[sector]
        let phase = time * 0.035 + float sector
        let axis = Vec3.normalize (s.Origin - s.Relay)
        if sector = 4 || sector = 8 then
            let aim = axis + Vec3.unitY * (0.12 * sin phase)
            s.Relay, Quat.lookRotation aim Vec3.up
        elif sector = 5 then
            let angle = time * 0.085
            s.Origin + v (460. * cos angle) 75. (460. * sin angle),
            Quat.lookRotation (v (-sin angle) 0. (cos angle)) Vec3.up
        else
            // Eine geschlossene langgezogene Runde hat getrennte Hin- und Rückspuren.
            let start = s.Port + Vec3.normalize (s.Relay - s.Port) * 235. + Vec3.up * 85.
            let finish = s.Origin - axis * 175.
            let side = Vec3.normalize (Vec3.cross (finish - start) Vec3.up) * 95.
            let t = (1. - cos phase) * 0.5
            let control = s.Relay * 2. - (start + finish) * 0.5
            let outbound = Vec3.lerp start control t
            let inbound = Vec3.lerp control finish t
            let pos = Vec3.lerp outbound inbound t + side * sin phase
            let direction = (inbound - outbound) * sin phase + side * cos phase
            pos, Quat.lookRotation direction Vec3.up

    let nearSite sector (position: Vec3) = Vec3.distanceSq position sites.[sector].Origin < 1150. * 1150.

    /// Drei driftende Staubbänke auf der Zollroute. Die Lücken geben die Sicht wieder frei.
    let veilDensity sector (viewer: Vec3) time =
        if sector <> 8 then 0.
        else
            let route = sites.[sector]
            let axis = Vec3.normalize (route.Origin - route.Port)
            let offset = viewer - route.Port
            let along = Vec3.dot offset axis
            let width = max 0. (1. - Vec3.length (offset - axis * along) / 1500.)
            let drift = 120. * sin (time * 0.034)
            let bank center = max 0. (1. - abs (along - center - drift) / 390.)
            width * max (bank 940.) (max (bank 1890.) (bank 3120.))

    let scanPoint sector =
        let center = sites.[sector].Origin
        if sector = 4 then center + v 0. 120. 0. else center

    let canSurvey sector (position: Vec3) =
        let range = if sector = 4 then 70. else 155.
        Vec3.distanceSq position (scanPoint sector) <= range * range

    let survey sector position log =
        if not log.Engaged && not (has log.Finds sector) && canSurvey sector position then
            let finds = log.Finds ||| (1 <<< sector)
            let bonus = if chartComplete finds && not (chartComplete log.Finds) then chartFee else 0
            { log with Finds = finds; Tracking = false }, sites.[sector].Fee + bonus
        else log, 0

    /// Keine genaue Distanz zum unbekannten Ziel verraten. Zunächst der Arbeitsroute folgen.
    let bearing sector (position: Vec3) log =
        let s = sites.[sector]
        let d = Vec3.distance position s.Origin
        if d <= 460. then scanPoint sector, s.Approach
        elif d < Vec3.distance s.Relay s.Origin + 260. || linked log.Finds sector || has log.Finds sector then
            s.Origin, (if linked log.Finds sector then "Kreuzpeilung aus dem Fundarchiv" else "Trägersignal wird klarer")
        else s.Relay, s.Hint

    let trapMask = (1 <<< 5) ||| (1 <<< 7) ||| (1 <<< 8)
    let threat sector time =
        let center = sites.[sector].Origin
        match sector with
        | 5 -> Some (fst (guidePose sector time), 235.)
        | 7 -> Some (center + v 112. 16. -62., 162.)
        | 8 -> Some (center + v 104. -14. 26., 150.)
        | _ -> None

    let triggers sector time position log =
        (trapMask &&& (1 <<< sector)) <> 0 && nearSite sector position &&
        not (has (log.Sprung ||| log.Finds) sector) &&
        match threat sector time with
        | Some (point,radius) -> Vec3.distanceSq position point < radius * radius
        | None -> false

    let defenders sector =
        match sector with
        | 5 -> [| EnemyRole.Scout; EnemyRole.Blocker |]
        | 7 -> [| EnemyRole.Gunship; EnemyRole.Scout; EnemyRole.Scout |]
        | 8 -> [| EnemyRole.Blocker; EnemyRole.Drone |]
        | _ -> [||]
