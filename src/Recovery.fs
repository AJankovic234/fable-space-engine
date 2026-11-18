namespace SpaceEngine

type ArchiveEcho =
    | NoEcho
    | KnownRecording of distance: float
    | NewRecording of distance: float

type ArchiveScan = { Echo: ArchiveEcho; mutable Elapsed: float }

module Recovery =
    let pulseDuration = 0.8
    let readoutDuration = 6.4

    let rotation kind =
        let pitch = if kind = FleetDock then 0.9 else -0.22
        Quat.fromAxisAngle Vec3.unitY 0.65 * Quat.fromAxisAngle Vec3.unitX pitch

    /// Ein Impuls tastet die Kugel um die Schiffslage beim Tastendruck ab.
    let scan pilot sector (ship: Vec3) (source: Vec3) =
        let distance = Vec3.distance ship source
        if distance > RelicArchive.range pilot || not (PilotSkills.contains pilot.Visited sector) then NoEcho
        elif PilotSkills.scan pilot sector then NewRecording distance
        else KnownRecording distance

    let transmission = function
        | MiningDepot -> "»Flieg bloß nicht mit meinem Namen nach Morgenlicht.«"
        | Refinery -> "Schacht 9 · Rücklauf an die Lohnstelle. Die Liste enthält 38 Namen."
        | FrontierBazaar -> "»Warte. Ich leg den Funk hin. Ich brauch beide Hände.«"
        | OrbitalYard -> "Trockendock B ruft die Bodenstation. Das Feld für die Startfreigabe ist leer."
        | ResearchRelay -> "RT-9 · Zwei Signale vom 14. März. Beide tragen die Kennung ORPHEUS-7."
        | FleetDock -> "Rückkanal offen. Die Wiederherstellungsakte trägt den Namen Kaelen Vance."
