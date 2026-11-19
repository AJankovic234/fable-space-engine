namespace SpaceEngine

// =====================================================================================
//  CombatVfx.fs  -  Rezepte fuer Kampfeffekte (reine Daten und Funktionen)
//
//  * Bolts: Aussehen je Quelle (Spieler, Overdrive, Impulsfokus, Turret,
//    Kreuzerbatterie, Gegnerrollen). Trefferlogik und Tempo liegen woanders.
//  * Treffer: Funkenzahl und Massstab nach Zielgroesse.
//  * Zerstoerung: vier Stufen nach Huellradius mit Partikelzahlen, Zeitplan der
//    Folgeexplosionen und einer Budget-Obergrenze je Abschuss.
//  * Budget: Drosselung kosmetischer Effekte, wenn der Partikelpool voll laeuft.
//
//  Effects (PhysicsAndPool.fs) setzt die Rezepte in Partikel um, Main.fs zeichnet.
// =====================================================================================

open System

[<RequireQualifiedAccess>]
module CombatVfx =

    // ---------------------------------------------------------------------------------
    //  Bolts
    // ---------------------------------------------------------------------------------

    /// Wer einen Bolt abgefeuert hat (LaserPool.Owner). Ab OwnerEnemySlot folgt der
    /// Gegner-Slot. 0 bleibt nach dem Laden eines Spielstands stehen: dann entscheidet
    /// das Team.
    [<Literal>]
    let OwnerUnknown = 0

    [<Literal>]
    let OwnerPlayer = 1

    [<Literal>]
    let OwnerBattery = 2

    [<Literal>]
    let OwnerEnemySlot = 3

    /// Saum (R, G, B), Kern, Dicke und Laenge in Metern, Lichthof am Kopf und
    /// Groesse des Muendungsblitzes.
    type BoltStyle =
        { R: float
          G: float
          B: float
          CoreR: float
          CoreG: float
          CoreB: float
          Thickness: float
          Length: float
          Halo: float
          Muzzle: float }

    [<Literal>]
    let StylePlayer = 0

    [<Literal>]
    let StyleOverdrive = 1

    [<Literal>]
    let StyleFocus = 2

    [<Literal>]
    let StyleTurret = 3

    [<Literal>]
    let StyleBattery = 4

    /// StyleEnemy + Rolle (0 Drohne .. 4 Kommandoschiff).
    [<Literal>]
    let StyleEnemy = 5

    let private style r g b thickness length halo muzzle =
        // Der Kern ist die zum Weiss gezogene Saumfarbe: hell genug fuer Bloom,
        // aber noch als Farbe der Quelle lesbar.
        let core c = 0.9 + c * 0.55

        { R = r
          G = g
          B = b
          CoreR = core r
          CoreG = core g
          CoreB = core b
          Thickness = thickness
          Length = length
          Halo = halo
          Muzzle = muzzle }

    /// Spielerwaffen bleiben kuehl (gruen, tuerkis, violett, blau), Gegner warm
    /// (orange bis magenta): Freund und Feind sind auf einen Blick getrennt.
    let boltStyles: BoltStyle[] =
        [| style 0.22 1.60 0.70 0.62 17.0 2.0 2.6 // Bordkanone
           style 0.85 1.80 1.95 0.82 19.0 2.4 3.2 // Overdrive
           style 0.95 0.45 1.95 0.74 18.0 2.3 3.0 // Impulsfokus-Kit
           style 0.25 0.85 1.95 0.40 12.0 1.4 1.7 // Auto-Turret
           style 2.00 0.70 0.16 2.60 38.0 8.0 13.0 // Kreuzerbatterie
           style 1.85 0.62 0.10 0.46 11.0 1.5 1.8 // Drohne
           style 1.90 0.30 0.16 0.42 16.0 1.5 1.8 // Spaeher
           style 1.95 0.14 0.08 0.80 15.0 2.2 2.8 // Kanonenboot
           style 1.90 0.16 0.85 1.20 11.0 3.0 3.6 // Bomber
           style 2.00 0.95 0.22 1.00 20.0 2.8 3.6 |] // Kommandoschiff

    /// Stil eines Bolts. `role` ist die Rolle des feuernden Gegners oder -1.
    let boltStyleId (owner: int) (playerTeam: bool) (overdrive: bool) (focus: bool) (role: int) : int =
        if owner = OwnerBattery then StyleBattery
        elif playerTeam then
            if owner <> OwnerPlayer then StyleTurret
            elif overdrive then StyleOverdrive
            elif focus then StyleFocus
            else StylePlayer
        else
            StyleEnemy + Math.Max(0, Math.Min(4, role))

    /// Dauer des Muendungsblitzes in Sekunden.
    [<Literal>]
    let MuzzleTime = 0.055

    // ---------------------------------------------------------------------------------
    //  Treffer
    // ---------------------------------------------------------------------------------

    /// Massstab eines Treffereffekts nach Zielradius: Drohne 1, Korvette etwa 2,4.
    let hitScale (radius: float) : float =
        Math.Max(0.85, Math.Min(3.0, 0.55 + radius * 0.125))

    /// Funken eines Huellentreffers nach Zielradius und Schaden.
    let hitSparks (radius: float) (damage: float) : int =
        Math.Max(6, Math.Min(22, 5 + int (radius * 0.5 + damage * 0.45)))

    // ---------------------------------------------------------------------------------
    //  Zerstoerung
    // ---------------------------------------------------------------------------------

    /// Drohne, Spaeher: ein schneller Feuerball mit Truemmern.
    [<Literal>]
    let TierLight = 0

    /// Kanonenboot, Bomber: Aufbrechen, Folgeexplosionen, Hauptexplosion mit Stossring.
    [<Literal>]
    let TierHeavy = 1

    /// Kommando-Korvette: Kette entlang des Rumpfs, dann Kernbruch.
    [<Literal>]
    let TierCapital = 2

    /// Zollkreuzer: wie Capital, ueber die ganze Rumpflaenge.
    [<Literal>]
    let TierCruiser = 3

    let tier (radius: float) : int =
        if radius < 6.5 then TierLight
        elif radius < 12.0 then TierHeavy
        elif radius < 40.0 then TierCapital
        else TierCruiser

    /// Partikelzahlen der ersten Stufe und Zeitplan. `Secondaries` Folgeexplosionen
    /// verteilen sich auf [FirstSecondary, LastSecondary]; bei `Finale` zuendet die
    /// Hauptexplosion (0 = keine eigene Stufe). `Duration` ist das Ende des Nachgluehens.
    type BlastRecipe =
        { Fire: int
          Core: int
          Sparks: int
          Shards: int
          Debris: int
          Chunks: int
          Smoke: int
          Secondaries: int
          FirstSecondary: float
          LastSecondary: float
          Finale: float
          Duration: float
          /// Reichweite des Lichtblitzes als Vielfaches der Groesse.
          FlashRange: float
          FlashIntensity: float }

    let recipes: BlastRecipe[] =
        [| { Fire = 22; Core = 6; Sparks = 36; Shards = 8; Debris = 14; Chunks = 0; Smoke = 0
             Secondaries = 0; FirstSecondary = 0.0; LastSecondary = 0.0; Finale = 0.0; Duration = 1.6
             FlashRange = 22.0; FlashIntensity = 7.0 }
           { Fire = 20; Core = 6; Sparks = 30; Shards = 12; Debris = 16; Chunks = 5; Smoke = 8
             Secondaries = 3; FirstSecondary = 0.10; LastSecondary = 0.36; Finale = 0.50; Duration = 3.2
             FlashRange = 22.0; FlashIntensity = 6.0 }
           { Fire = 36; Core = 9; Sparks = 50; Shards = 20; Debris = 24; Chunks = 12; Smoke = 12
             Secondaries = 10; FirstSecondary = 0.18; LastSecondary = 2.05; Finale = 2.35; Duration = 3.8
             FlashRange = 16.0; FlashIntensity = 7.0 }
           { Fire = 84; Core = 21; Sparks = 84; Shards = 42; Debris = 49; Chunks = 42; Smoke = 28
             Secondaries = 26; FirstSecondary = 0.15; LastSecondary = 2.9; Finale = 3.2; Duration = 4.0
             FlashRange = 9.0; FlashIntensity = 9.0 } |]

    let recipe (tier: int) : BlastRecipe = recipes.[Math.Max(0, Math.Min(recipes.Length - 1, tier))]

    /// Feuerbaelle der ersten Stufe, verteilt entlang der Rumpfachse.
    let blastPoints (tier: int) : int =
        if tier >= TierCruiser then 7 elif tier = TierCapital then 3 else 1

    /// Partikel einer Folgeexplosion (Feuer, Kern, Lichtpuls, Funken, Splitter).
    [<Literal>]
    let SecondaryParticles = 30

    /// Partikel der Hauptexplosion (Finale) je Stufe: Feuerball, Funken, Splitter,
    /// Truemmer, Rauch, Stossringe.
    let finaleParticles (tier: int) : int =
        match tier with
        | TierHeavy -> 26 + 7 + 1 + 44 + 14 + 10 + 10 + 2
        | TierCapital -> 44 + 10 + 1 + 90 + 30 + 20 + 22 + 3
        | TierCruiser -> 90 + 20 + 1 + 140 + 50 + 40 + 40 + 3
        | _ -> 0

    /// Zeitpunkt der Folgeexplosion `index` (0-basiert) nach dem Abschuss.
    let secondaryTime (tier: int) (index: int) : float =
        let r = recipe tier
        if r.Secondaries <= 1 then r.FirstSecondary
        else r.FirstSecondary + (r.LastSecondary - r.FirstSecondary) * float index / float (r.Secondaries - 1)

    /// Obergrenze aller Partikel einer Zerstoerung ueber alle Stufen, inklusive der
    /// unsichtbaren Zuender. Die erste Stufe zaehlt je Feuerball einen Lichtpuls und
    /// einen Stossring mit.
    let budget (tier: int) : int =
        let r = recipe tier
        let fuses = r.Secondaries + (if r.Finale > 0.0 then 1 else 0)
        r.Fire + r.Core + r.Sparks + r.Shards + r.Debris + r.Chunks + r.Smoke + blastPoints tier + 1
        + fuses + r.Secondaries * SecondaryParticles + finaleParticles tier

    // ---------------------------------------------------------------------------------
    //  Budget
    // ---------------------------------------------------------------------------------

    /// Faktor fuer kosmetische Partikelzahlen (Funken, Spuren): 1 bis 60 % Poolfuellung,
    /// dann linear herunter auf 0,25. Explosionen selbst werden nicht gedrosselt.
    let throttle (used: int) (capacity: int) : float =
        let fill = float used / float (Math.Max(1, capacity))
        if fill <= 0.6 then 1.0 else Math.Max(0.25, 1.0 - (fill - 0.6) * 1.875)

    /// Triebwerksspuren entfallen ganz, sobald der Pool zu drei Vierteln belegt ist.
    let trailsAllowed (used: int) (capacity: int) : bool = used * 4 < capacity * 3

    /// Gedrosselte Anzahl, mindestens 1.
    let scaled (count: int) (factor: float) : int = Math.Max(1, int (float count * factor + 0.5))
