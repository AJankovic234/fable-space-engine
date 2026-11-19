namespace SpaceEngine

// =====================================================================================
//  CombatFeedback.fs  -  reine Hilfsrechnungen fuer das Kampf-Feedback im HUD
//
//  * Trefferrichtung: Aus einer Richtung im Kamerasystem (x rechts, y oben,
//    Blick entlang -z) wird der Bildschirmwinkel des roten Hinweisbogens.
//  * Zielwahl nach Entfernung (Taste N): naechstgelegenes Ziel, jeder weitere
//    Druck das naechstfernere.
// =====================================================================================

open System

[<RequireQualifiedAccess>]
module CombatFeedback =

    /// Lebensdauer eines Trefferrichtungs-Hinweises in Sekunden.
    [<Literal>]
    let DamageIndicatorTime = 1.4

    /// Bildschirmwinkel (Canvas-Konvention: 0 = rechts, pi/2 = unten) und Gewicht 0.25..1
    /// fuer eine Schadensquelle in Richtung (x, y, z) im Kamerasystem. Quellen genau
    /// hinter der Kamera zeigen nach unten (wie die Randpfeile); Quellen nahe der
    /// Blickrichtung sind ohnehin im Bild und werden schwaecher gezeichnet.
    let indicator (x: float) (y: float) (z: float) : struct (float * float) =
        let planar = sqrt (x * x + y * y)
        let length = sqrt (x * x + y * y + z * z)
        let angle = if planar < 1e-6 then Math.PI * 0.5 else atan2 (-y) x
        // Winkel zwischen Blickrichtung (-z) und Quelle: 0 = genau voraus, pi = genau hinten
        let offAxis = if length < 1e-9 then Math.PI else acos (Math.Max(-1.0, Math.Min(1.0, -z / length)))
        let weight = Math.Max(0.25, Math.Min(1.0, offAxis / 0.6))
        struct (angle, weight)

    /// Naechstes Ziel nach Entfernung von `from`: Ohne gueltiges aktuelles Ziel
    /// (`current` < 0 oder tot) das naechstgelegene, sonst das naechstfernere; nach dem
    /// fernsten wieder das naechstgelegene. Gleiche Entfernungen ordnet der Index.
    /// -1, wenn kein Gegner lebt.
    let nextByDistance (enemies: Ship[]) (from: Vec3) (current: int) : int =
        let currentDistance =
            if current >= 0 && current < enemies.Length && enemies.[current].Alive then
                Vec3.distanceSq enemies.[current].Position from
            else
                -1.0

        let mutable nearest = -1
        let mutable nearestDistance = Double.MaxValue
        let mutable next = -1
        let mutable nextDistance = Double.MaxValue

        for i in 0 .. enemies.Length - 1 do
            if enemies.[i].Alive then
                let d = Vec3.distanceSq enemies.[i].Position from

                if d < nearestDistance then
                    nearest <- i
                    nearestDistance <- d

                if currentDistance >= 0.0 && i <> current
                   && (d > currentDistance || (d = currentDistance && i > current))
                   && d < nextDistance then
                    next <- i
                    nextDistance <- d

        if next >= 0 then next else nearest
