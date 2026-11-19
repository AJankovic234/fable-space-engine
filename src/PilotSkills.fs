namespace SpaceEngine

/// Ein Bit je Sektor. Verfügbare Punkte ergeben sich aus Funden, Besuchen und Käufen.
type PilotProgress =
    { Levels: int[]
      mutable Visited: int
      mutable Relics: int
      mutable Rescues: int
      mutable PulseCooldown: float
      mutable PulseTime: float
      mutable BypassCooldown: float }

[<RequireQualifiedAccess>]
module PilotSkills =
    let create () =
        { Levels = [|0; 0; 0|]; Visited = 1; Relics = 0; Rescues = 0
          PulseCooldown = 0.0; PulseTime = 0.0; BypassCooldown = 0.0 }

    let level (pilot: PilotProgress) branch = pilot.Levels.[branch]
    let contains mask sector = mask &&& (1 <<< sector) <> 0
    let count mask =
        let mutable bits = uint32 mask
        let mutable total = 0
        while bits <> 0u do
            bits <- bits &&& (bits - 1u)
            total <- total + 1
        total
    let cost rank = if rank = 3 then 2 else 1
    let points pilot =
        count pilot.Visited + count pilot.Relics -
        (pilot.Levels |> Array.sumBy (fun rank -> if rank = 3 then 4 else rank))

    let learn pilot branch rank =
        if branch >= 0 && branch < 3 && rank >= 1 && rank <= 3 &&
           rank = level pilot branch + 1 && points pilot >= cost rank then
            pilot.Levels.[branch] <- rank
            true
        else false

    let visit pilot sector = pilot.Visited <- pilot.Visited ||| (1 <<< sector)

    let scan pilot sector =
        if contains pilot.Visited sector && not (contains pilot.Relics sector) then
            pilot.Relics <- pilot.Relics ||| (1 <<< sector)
            true
        else false

    let pulse pilot =
        if level pilot 0 = 3 && pilot.PulseCooldown <= 0.0 then
            pilot.PulseCooldown <- 45.0
            pilot.PulseTime <- 8.0
            true
        else false

    let tick pilot dt =
        pilot.PulseCooldown <- max 0.0 (pilot.PulseCooldown - dt)
        pilot.PulseTime <- max 0.0 (pilot.PulseTime - dt)
        pilot.BypassCooldown <- max 0.0 (pilot.BypassCooldown - dt)

module RelicArchive =
    // Summaries stay sealed in the dock archive until the corresponding signal is scanned.
    let entries =
        [| "Kestrel-04", "Auf dem Flugschreiber steht J. Halden, Kennung identisch mit meiner. In seinem letzten Eintrag meldet er eine Drohne mit derselben Nummer und warnt vor dem Anflug auf Morgenlicht."
           "Schichtliste aus Schacht 9", "Hinter allen 38 Namen steht „Transfer abgeschlossen“. Im selben Umschlag liegt der Druckverlauf vom Sauerstoffventil: bei vier Prozent Mehrverbrauch wurde der Strang abgeriegelt."
           "Der Puffer-Chor", "Zwölf Notrufe in Endlosschleife auf einer Antenne. Eine der Stimmen bricht mitten im Satz ab, als jemand das Mikro weglegt. Zeitstempel liegt elf Jahre zurück."
           "Trockendock B", "Im Werkstattbuch des Kolonieschiffs fehlt genau ein Stempel: Startfreigabe Erde. Seit dem Eintrag von 03 AE wiederholt das Dock dieselbe Anfrage an die Bodenstation."
           "RT-9-Mirror", "14. März 2034. Relais RT-9 empfängt zwei getrennte Bahnen von ORPHEUS-7 unter genau derselben Kennung. Das Band reißt vor der Wiedereintritts-Meldung ab."
           "Sleeve-Satellit", "Reaktorleck 2564: Totenschein auf den Namen Kaelen Vance, direkt gefolgt vom ersten Wiederherstellungsantrag. Im aktuellen Schiffsregister der Grace Period läuft Sleeve Nummer 7."
           "Pumpenbuch Raureif", "Erster Winter: Die Mechaniker haben Kühlleistung aufs Schlafdeck umgeleitet. Als die Zellen aus Helios ausblieben, verschwand die ganze Schicht aus dem Dienstplan."
           "Schleppschein 44", "Bergungsauftrag für die Erzpresse. Im Log fragt der Schlepper sechsmal nach den Rettungskapseln; die Leitstelle schickt jedes Mal nur die alte Inventarliste zurück."
           "Kursprotokoll 17", "Die Crew von Graue Wacht schickt ihre vermessene Nebelspur kostenlos an beide Docks. Am nächsten Morgen verlangt ein anderer Sender Gebühren für genau diese Koordinaten." |]
    let position (port: Vec3) sector =
        let side = if sector % 2 = 0 then 1.0 else -1.0
        port + Vec3(side * (145.0 + 12.0 * float sector), 30.0 + 9.0 * float sector, 65.0)
    let range pilot = if PilotSkills.level pilot 0 >= 1 then 700.0 else 350.0
