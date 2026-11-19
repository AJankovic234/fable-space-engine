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
        [| "Kestrel-04", "Auf dem Flugschreiber steht J. Halden. Seine Kennung stimmt bis zur letzten Ziffer mit meiner überein. »Die haben sie auf eine Lockdrohne kopiert«, sagt er in seinem letzten Eintrag. »Flieg bloß nicht mit meinem Namen nach Morgenlicht.«"
           "Schichtliste aus Schacht 9", "Hinter allen 38 Namen steht »Sleeve-Transfer abgeschlossen«. Im Umschlag steckt auch der Ausdruck vom Sauerstoffventil. Als der Verbrauch vier Prozent über Plan lag, hat die Leitstelle die Zufuhr zu Schacht 9 geschlossen."
           "Der Puffer-Chor", "Auf der Antenne laufen zwölf Notrufe in einer Schleife. Ich habe die Stimme der Frau herauskopiert, bis zu der Stelle, an der sie den Funk ablegt. Die Aufnahme ist elf Jahre alt."
           "Trockendock B", "Im Werkstattbuch des Kolonieschiffs ist ein Kästchen frei geblieben. »Startfreigabe Erde« steht darüber. Seit dem Eintrag von 03 AE schickt das Dock dieselbe Anfrage an die Bodenstation."
           "RT-9-Mirror", "Am 14. März 2034 hat RT-9 beide Teile von ORPHEUS-7 empfangen. Ihre Bahnen laufen auseinander, über beiden Spuren steht dieselbe Kennung. Das Band endet vor der Bestätigung des Wiedereintritts."
           "Sleeve-Satellit", "Beim Reaktorleck von 2564 starb Kaelen Vance. Sein Totenschein liegt in der Wiederherstellungsakte, gleich vor dem ersten Antrag auf Ersatz. Laut jüngstem Eintrag ist auf der Sovereign gerade Sleeve Nummer 7 im Dienst."
           "Pumpenbuch der Kaltstrom-Schicht", "Im ersten Winter leiteten die Mechaniker einen Teil des Kühlmittels zum Schlafdeck. Die Pumpen liefen mit Zellen aus Helios. Als der Nachschub ausblieb, strich die Verwaltung zuerst die Leute von der Anwesenheitsliste."
           "Schleppschein 44", "Der Auftrag lautet, die Presse aus dem Deck zu bergen. Der Schlepper hat sechsmal nach den Rettungskapseln gefragt. Jedes Mal antwortete die Leitstelle mit derselben veralteten Inventarliste."
           "Kursprotokoll 17", "Die Relaiscrew hat den Nebel vermessen und die sichere Spur an beide Docks geschickt. Am nächsten Morgen verlangte ein unbekannter Sender Geleitschutz genau für diese Spur." |]
    let position (port: Vec3) sector =
        let side = if sector % 2 = 0 then 1.0 else -1.0
        port + Vec3(side * (145.0 + 12.0 * float sector), 30.0 + 9.0 * float sector, 65.0)
    let range pilot = if PilotSkills.level pilot 0 >= 1 then 700.0 else 350.0
