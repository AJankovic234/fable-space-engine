namespace SpaceEngine

// =====================================================================================
//  Encounters.fs  -  Gefechtsregie zwischen KI und Main
//
//  * Bossabschnitt: ein Teil der Eskorte bleibt in Reserve, bis die Korvette sie ruft.
//  * Fraktionen: Piraten (Handelsflug) fliegen dieselben Modelle wie AETHER (Mission),
//    aber verwittert, uneinheitlich getoent und mit eigenen Lichtfarben.
// =====================================================================================

[<RequireQualifiedAccess>]
module Encounters =

    /// So viele Schiffe der Abschnittsliste haelt die Korvette zurueck; je Phasenwechsel ruft sie zwei.
    [<Literal>]
    let BossReserve = 4

    /// Der gerufene Verband trifft spaetestens nach dieser Zeit ein.
    [<Literal>]
    let EscortDelay = 1.5

    let private commanderAlive (brains: AiBrain[]) (enemies: Ship[]) =
        enemies.Length > 0 && enemies.[0].Alive && brains.[0].Role = EnemyRole.Commander

    /// Wie viele Plaetze der Gegnerliste jetzt nachruecken duerfen. Ohne lebende Korvette alle.
    let rosterLimit (roles: EnemyRole[]) (brains: AiBrain[]) (enemies: Ship[]) : int =
        if roles.Length > BossReserve && roles.[0] = EnemyRole.Commander && commanderAlive brains enemies then
            min roles.Length (roles.Length - BossReserve + 2 * (brains.[0].BossPhase - 1))
        else
            roles.Length

    /// True genau einmal je Phasenwechsel der Korvette (Main meldet den Ruf und zieht den Verband vor).
    let takeEscortCall (brains: AiBrain[]) (enemies: Ship[]) : bool =
        let mutable called = false
        for i in 0 .. brains.Length - 1 do
            if brains.[i].EscortCall then
                brains.[i].EscortCall <- false
                if enemies.[i].Alive then called <- true
        called

    /// Meldung zum Phasenwechsel: Titel und Hinweis.
    let phaseNotice (phase: int) : string * string =
        if phase >= 3 then
            "KORVETTE GEHT AUF NAHDISTANZ", "Schild neu geladen · letzte Eskorte gerufen · kürzere Feuerpausen. Abstand halten."
        else
            "KORVETTE RUFT ESKORTE", "Schild neu geladen · Seitenbatterien feuern Fächer. Nach dem Aufglühen den Kurs wechseln."

    // ---------------------------------------------------------------------------------
    //  Fraktionen
    // ---------------------------------------------------------------------------------

    // Rost, Olivgruen, ausgeblichenes Tuerkis, Bleigrau: jede Maschine anders zusammengeflickt.
    let private pirateCoats =
        [| Vec3(0.88, 0.52, 0.36)
           Vec3(0.56, 0.66, 0.38)
           Vec3(0.40, 0.60, 0.58)
           Vec3(0.64, 0.52, 0.42)
           Vec3(0.74, 0.44, 0.46) |]

    // Kaltes Giftgruen, schmutziges Bernstein, fahles Magenta.
    let private pirateLamps =
        [| Vec3(0.45, 1.45, 0.35)
           Vec3(1.35, 0.85, 0.20)
           Vec3(1.20, 0.35, 0.95) |]

    /// Instanzfarbe eines Piratenschiffs (multipliziert die AETHER-Lackierung des Modells).
    let pirateTint (slot: int) : Vec3 = pirateCoats.[((slot % pirateCoats.Length) + pirateCoats.Length) % pirateCoats.Length]

    /// Positionslicht und Triebwerkshof: Fraktionsfarbe mit einem Rest der Rollenfarbe.
    let pirateLight (slot: int) (role: Vec3) : Vec3 =
        let lamp = pirateLamps.[((slot % pirateLamps.Length) + pirateLamps.Length) % pirateLamps.Length]
        lamp * 0.8 + role * 0.25

    /// Wackelkontakt: das Positionslicht flackert unregelmaessig und setzt kurz aus (0..1).
    let pirateFlicker (time: float) (slot: int) : float =
        let t = time * 7.3 + float slot * 2.1
        let noise = sin t * sin (t * 0.37 + 1.3) * sin (t * 1.91)
        if noise > 0.45 then 0.15 else 0.75 + 0.25 * sin (time * 3.1 + float slot)

    /// Ein einzelner, langer Blitz in Lampenfarbe statt des karminroten AETHER-Doppelblitzes.
    let pirateStrobe (time: float) (slot: int) : bool =
        (time * (0.55 + 0.07 * float (slot % 4)) + float slot * 0.61) % 1.0 < 0.12
