namespace SpaceEngine

module SectorPoi =
    type Kind = Depot of CommodityId * int | ReceiverPair | Maintenance | CalibrationRing
    type Site =
        { Id: int; Sector: int; Kind: Kind; Name: string; Position: Vec3; Fee: int; Report: string }

    // Ids liegen als Bits im Spielstand. Neue Orte erhalten neue, feste Ids.
    let private site id sector kind name x y z fee report =
        { Id = id; Sector = sector; Kind = kind; Name = name; Fee = fee; Report = report
          Position = Universe.sectors.[sector].Outpost.Position + Vec3(x,y,z) }

    let sites =
        [| site 0 0 (Depot (CommodityId.FuelCells,3)) "Vorratslager Sieben" 1260. 260. 810. 85 "Drei Zellen aus der alten Schichtreserve sind verladen."
           site 1 0 CalibrationRing "Haldens Eichbogen" -1600. 610. 3460. 270 "Die Flugzeit zwischen den Lichtschranken stimmt wieder mit Haldens Erzkarte überein."
           site 2 1 Maintenance "Kühlpumpwerk Süd" 1150. -375. -1060. 175 "Die zweite Pumpe läuft. Der Nachtdienst kann den Kühlkreis wieder unter Druck setzen."
           site 3 1 (Depot (CommodityId.PlasmaCores,2)) "Schmelzreserve 12" 3190. 470. 1790. 160 "Zwei abgekühlte Kerne aus der letzten Ofencharge sind verladen."
           site 4 2 ReceiverPair "Schwesternfunk" -940. 320. 1390. 205 "Aus den beiden Empfangsspuren lässt sich die Anflugliste der verschollenen Lazarettfähre lesen."
           site 5 2 Maintenance "Quarantänefähre Edda" -2690. -540. -2760. 305 "Eddas Luftschleuse hält wieder Druck. Die Krankenbucht hat ihren Heimathafen gerufen."
           site 6 3 CalibrationRing "Keplers Nullmaß" -1330. 420. -730. 180 "Mit der neuen Referenzzeit kann die Werft die Schubregler ihrer Schlepper abgleichen."
           site 7 3 (Depot (CommodityId.TitaniumScrap,6)) "Materialprahm Marga" 3260. -490. 2170. 195 "Sechs Titanbündel sind vom stillgelegten Materialprahm übernommen."
           site 8 4 Maintenance "Uhrmacherplattform" 1210. -360. 980. 190 "Die Atomuhr hält ihren Takt. Das Horchgitter empfängt seine Zeitmarke wieder."
           site 9 4 ReceiverPair "Das stumme Doppelauge" 3130. 820. -2240. 365 "Die beiden fremden Spiegel zeigen denselben Puls mit genau einem Umlauf Abstand."
           site 10 5 (Depot (CommodityId.MedSupplies,3)) "Sanitätsreserve C" 1450. 310. 590. 135 "Drei versiegelte Med-Module aus dem äußeren Sanitätslager sind an Bord."
           site 11 5 CalibrationRing "Eklipsenlinie" 2210. -630. -3220. 330 "Der Durchflug bestimmt den Eintritt des nächsten Schattens für den Lotsendienst."
           site 12 6 CalibrationRing "Eislot" -1320. -300. 1020. 160 "Die Destille hat die Driftmessung und kann die nächste Eisernte ansetzen."
           site 13 6 ReceiverPair "Frostharfe" -2410. 760. -2890. 340 "Zwei Echos ergeben das Profil eines Hohlraums unter der Eisrinde."
           site 14 7 Maintenance "Druckglocke Neun" 1040. 505. -1320. 245 "Die Bergungscrew kann das geflutete Deck wieder über die Druckglocke erreichen."
           site 15 7 (Depot (CommodityId.NavData,3)) "Kurierrahmen Ulla" 3150. -550. 2430. 240 "Drei unversehrte Nav-Kassetten sind aus dem Übergabefach geborgen."
           site 16 8 ReceiverPair "Schattentelegraph" 1220. 405. -1060. 215 "In der Differenz der Signale liegt eine alte Lotsendurchsage für die Graue Wacht."
           site 17 8 CalibrationRing "Lotsenschleuse Nachtglas" -2310. 640. 3490. 370 "Die Wacht hat die Laufzeit durch den Nebel bestätigt. Das Lotsenfeuer ist geeicht." |]

    let bySector = Universe.sectors |> Array.mapi (fun i _ -> sites |> Array.filter (fun s -> s.Sector = i))
    type Log = { Done: int; WestFix: int; Pinned: int option }
    let empty = { Done = 0; WestFix = 0; Pinned = None }
    let private bit site = 1 <<< site.Id
    let doneAt log site = (log.Done &&& bit site) <> 0
    let private westRead log site = (log.WestFix &&& bit site) <> 0
    let allMask = sites |> Array.fold (fun mask s -> mask ||| bit s) 0
    let receiverMask = sites |> Array.fold (fun mask s -> if s.Kind = ReceiverPair then mask ||| bit s else mask) 0

    let kindIndex = function Depot _ -> 0 | ReceiverPair -> 1 | Maintenance -> 2 | CalibrationRing -> 3
    let category = function Depot _ -> "Frachtdepot" | ReceiverPair -> "Peilanlage" | Maintenance -> "Wartungsstelle" | CalibrationRing -> "Messring"
    let target log site =
        match site.Kind with
        | ReceiverPair when not (doneAt log site) ->
            site.Position + Vec3((if westRead log site then 85. else -85.),0.,0.)
        | _ -> site.Position

    let instruction log site =
        match site.Kind with
        | Depot (good,count) ->
            let def = TradeSystem.commodityDef good
            sprintf "G bis 90 m · %d × %s · %d t Laderaum" count def.Name (count * def.UnitVolume)
        | ReceiverPair ->
            if westRead log site then "Ostempfänger · türkis · G bis 48 m"
            else "Westempfänger · bernsteinfarben · G bis 48 m"
        | Maintenance -> "Auf höchstens 12 m/s bremsen · G bis 90 m zum Ankoppeln"
        | CalibrationRing -> "Durch die freie Ringmitte fliegen"

    type Contact = OutOfRange | Advice of string | FirstReading of Log | Completed of Log

    let private finish log site =
        Completed { Done = log.Done ||| bit site; WestFix = log.WestFix &&& ~~~(bit site)
                    Pinned = if log.Pinned = Some site.Id then None else log.Pinned }

    let scan log site position speed cargo =
        if Vec3.distanceSq position site.Position > 195. * 195. then OutOfRange
        elif doneAt log site then Advice site.Report
        else
            let distance = Vec3.distance position (target log site)
            match site.Kind with
            | ReceiverPair when distance <= 48. ->
                if westRead log site then finish log site
                else FirstReading { log with WestFix = log.WestFix ||| bit site; Pinned = Some site.Id }
            | Maintenance when distance <= 90. && speed <= 12. -> finish log site
            | Depot (good,amount) when distance <= 90. ->
                let needed = amount * (TradeSystem.commodityDef good).UnitVolume
                if TradeSystem.freeVolume cargo < needed then Advice (sprintf "Der Posten bleibt im Depot. Benötigter freier Laderaum: %d t." needed)
                else
                    TradeSystem.loadSalvage cargo good amount |> ignore
                    finish log site
            | _ -> Advice (instruction log site)

    /// Der ganze Physikschritt zählt, auch wenn der Boost die Messebene überspringt.
    let flight log site (start: Vec3) (finishPos: Vec3) radius =
        if site.Kind <> CalibrationRing || doneAt log site then OutOfRange
        else
            let a = start.Z - site.Position.Z
            let b = finishPos.Z - site.Position.Z
            if (a < 0. && b >= 0.) || (a > 0. && b <= 0.) then
                let p = Vec3.lerp start finishPos (a / (a-b)) - site.Position
                let aperture = 44. - radius
                if aperture > 0. && p.X*p.X + p.Y*p.Y < aperture*aperture then finish log site else OutOfRange
            else OutOfRange
