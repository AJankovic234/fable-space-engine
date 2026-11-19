namespace SpaceEngine

open Fable.Core

// =====================================================================================
//  LightSelection.fs  -  welche Punktlichter der Shader in diesem Frame bekommt
//
//  Aufrufer melden beliebig viele Kandidaten (Lights.addPoint). Vor dem Zeichnen waehlt
//  selectInto die wichtigsten nach Helligkeit, Reichweite und Abstand zur Kamera aus.
//  Die Aufrufreihenfolge spielt keine Rolle mehr. Reine Funktionen ohne Allokation im
//  Frame-Pfad; `select` ist die allokierende Variante fuer Tests.
// =====================================================================================

[<RequireQualifiedAccess>]
module LightSelection =
    /// Hoechstzahl der Kandidaten pro Frame. Weitere Lichter ersetzen den schwaechsten.
    [<Literal>]
    let Capacity = 128

    /// Floats pro Licht in den Kandidatenpuffern (xyz + Reichweite bzw. rgb + frei).
    [<Literal>]
    let Stride = 4

    /// Eigenhelligkeit eines Lichts: staerkster Farbkanal (Farbe * Intensitaet).
    let inline power (r: float) (g: float) (b: float) = System.Math.Max(r, System.Math.Max(g, b))

    /// Gewicht aus Sicht der Kamera: Helligkeit mal Bildanteil der beleuchteten Kugel.
    /// Steht die Kamera in der Kugel, zaehlt die volle Helligkeit; ausserhalb faellt das
    /// Gewicht mit dem Quadrat von Reichweite / Abstand.
    let score (power: float) (range: float) (distance: float) : float =
        if power <= 0.0 || range <= 0.0 then 0.0
        else
            let reach = range / System.Math.Max(range, distance)
            power * reach * reach

    /// Gewicht des Kandidaten `i` in den Puffern.
    let scoreAt (posRange: JS.Float32Array) (color: JS.Float32Array) (i: int) (cx: float) (cy: float) (cz: float) =
        let o = i * Stride
        let dx = float posRange.[o] - cx
        let dy = float posRange.[o + 1] - cy
        let dz = float posRange.[o + 2] - cz
        score (power (float color.[o]) (float color.[o + 1]) (float color.[o + 2])) (float posRange.[o + 3]) (sqrt (dx * dx + dy * dy + dz * dz))

    /// Schreibt die Indizes der bis zu `budget` wichtigsten Kandidaten absteigend nach
    /// `order` und liefert ihre Anzahl. Lichter ohne Gewicht fallen weg; bei gleichem
    /// Gewicht gewinnt der fruehere Kandidat. `scores` und `order` brauchen `count` Plaetze.
    let selectInto
        (posRange: JS.Float32Array)
        (color: JS.Float32Array)
        (count: int)
        (cx: float)
        (cy: float)
        (cz: float)
        (budget: int)
        (scores: float[])
        (order: int[])
        : int =
        for i in 0 .. count - 1 do
            scores.[i] <- scoreAt posRange color i cx cy cz
            order.[i] <- i

        let take = System.Math.Min(System.Math.Max(0, budget), count)
        let mutable selected = 0
        let mutable k = 0

        while k < take do
            let mutable best = k

            for j in k + 1 .. count - 1 do
                let sj = scores.[order.[j]]
                let sb = scores.[order.[best]]
                if sj > sb || (sj = sb && order.[j] < order.[best]) then best <- j

            let swap = order.[k]
            order.[k] <- order.[best]
            order.[best] <- swap

            if scores.[order.[k]] > 0.0 then
                selected <- selected + 1
                k <- k + 1
            else
                // Absteigend sortiert: alle weiteren haben ebenfalls kein Gewicht.
                k <- take

        selected

    /// Reine Variante fuer Tests und Werkzeuge: Indizes der gewaehlten Lichter.
    let select (posRange: JS.Float32Array) (color: JS.Float32Array) (count: int) (cx: float) (cy: float) (cz: float) (budget: int) : int[] =
        let scores = Array.zeroCreate<float> (System.Math.Max(1, count))
        let order = Array.zeroCreate<int> (System.Math.Max(1, count))
        let n = selectInto posRange color count cx cy cz budget scores order
        Array.sub order 0 n
