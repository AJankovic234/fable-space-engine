namespace SpaceEngine

// =====================================================================================
//  SkyLight.fs  -  Himmelslicht der Sektoren fuer die Oberflaechen
//
//  Naehert den Himmel von SkyBackdrop analytisch an (Galaxienband mit sektorabhaengiger
//  Drehung und Toenung, dazu der grosse Himmelskoerper im Vordergrundfenster) und
//  projiziert ihn auf L1-Kugelflaechenfunktionen. Der Shader liest daraus das diffuse
//  Himmelslicht und den Verlauf, den Metall und Glas spiegeln. Rechnet einmal pro
//  Sektorwechsel, nichts pro Frame.
// =====================================================================================

[<RequireQualifiedAccess>]
module SkyLight =
    // Muss zu SkyBackdrop passen: Reihenfolge der Himmelsansichten, Drehung (YAW) und
    // Toenung (TINT) des Galaxienbands.
    let private views =
        [| "outer-belt"; "slag-refinery"; "frontier-drift"; "kepler-yard"; "aether-relay"; "morgenlicht-bastion" |]

    let private yaw =
        [| 1.0, 0.0; 0.362, 0.932; -0.801, 0.598; -0.416, -0.909; 0.697, -0.717; -0.942, -0.335 |]

    let private tint =
        [| Vec3(1.0, 1.0, 1.0); Vec3(1.38, 0.47, 0.29); Vec3(0.46, 1.27, 1.12)
           Vec3(0.67, 0.85, 1.36); Vec3(0.94, 0.56, 1.57); Vec3(1.43, 0.42, 0.49) |]

    /// Grosser Himmelskoerper je Ansicht (SkyBackdrop.bodyFragment): Richtung in Welt-
    /// koordinaten, Winkelradius und mittlere Strahldichte der beleuchteten Scheibe.
    let private bodies =
        [| Vec3.normalize (Vec3(0.57, 0.24, -1.0)), 0.30, Vec3(0.10, 0.14, 0.16) // Ringplanet
           Vec3.normalize (Vec3(-0.49, 0.16, -1.0)), 0.25, Vec3(0.26, 0.07, 0.035) // Lavawelt
           Vec3.normalize (Vec3(0.54, 0.22, -1.0)), 0.19, Vec3(0.13, 0.22, 0.22) // Ozeanwelt
           Vec3.normalize (Vec3(-0.40, -0.17, -1.0)), 0.27, Vec3(0.19, 0.26, 0.30) // Eiswelt
           Vec3.normalize (Vec3(-0.42, 0.23, -1.0)), 0.21, Vec3(0.20, 0.10, 0.38) // Pulsar
           Vec3.normalize (Vec3(0.43, 0.19, -1.0)), 0.24, Vec3(0.30, 0.11, 0.025) |] // Finsternis

    let inline private smooth (e0: float) (e1: float) (x: float) =
        let t = System.Math.Max(0.0, System.Math.Min(1.0, (x - e0) / (e1 - e0)))
        t * t * (3.0 - 2.0 * t)

    /// Himmelsansicht (0..5) eines Sektors; unbekannte Sektoren zeigen den Kestrel-Himmel.
    let view (sector: int) : int =
        if sector >= 0 && sector < Universe.sectors.Length then
            Array.tryFindIndex ((=) Universe.sectors.[sector].Id) views |> Option.defaultValue 0
        else 0

    /// Mittlere Strahldichte des Himmels in Richtung `d` (Welt, normalisiert) ohne Sterne
    /// und Wolkenrauschen, in den Einheiten der Himmelsanzeige.
    let radiance (view: int) (d: Vec3) : Vec3 =
        let v = System.Math.Max(0, System.Math.Min(views.Length - 1, view))
        // Galaxienkoordinaten wie im Anzeige-Shader (Drehung um Y).
        let tx, ty = yaw.[v]
        let gx = tx * d.X - ty * d.Z
        let gy = d.Y
        // Band um die Ebene y + 0,38 x = 0,12; Wolken, Schleier und Staub im Mittel 0,45.
        let offset = (gy + gx * 0.38 - 0.12) * 3.4
        let band = exp (-offset * offset)
        let purple = Vec3(0.13, 0.035, 0.18)
        let teal = Vec3(0.025, 0.22, 0.27)
        let nebula = Vec3.lerp purple teal (smooth -0.7 0.7 gx)
        let mutable c = Vec3(0.003, 0.006, 0.017) + nebula * (band * 0.45) + Vec3(0.08, 0.13, 0.20) * (band * 0.03)

        if v <> 0 then
            let lum = c.X * 0.24 + c.Y * 0.62 + c.Z * 0.14
            let dim = 1.0 - smooth 0.14 0.65 lum
            let grey = Vec3.lerp c (Vec3(lum, lum, lum)) 0.62
            let t = tint.[v]
            c <- Vec3.lerp c (Vec3(grey.X * t.X, grey.Y * t.Y, grey.Z * t.Z)) dim

        // Der Himmelskoerper steht fest im Weltraum vor dem Band (weicher Rand).
        let dir, radius, color = bodies.[v]
        let angle = acos (System.Math.Max(-1.0, System.Math.Min(1.0, Vec3.dot d dir)))
        Vec3.lerp c color (1.0 - smooth (radius * 0.85) (radius * 1.1) angle)

    /// Rohe L1-Koeffizienten der Irradianz in Shader-Einheiten (12 Werte: Konstante, x, y, z
    /// je RGB): E(n) = c0 + c1 . n, gespiegelte Strahldichte L(r) = c0 + 1,5 c1 . r.
    let project (view: int) : float[] =
        let samples = 2048
        let sh = Array.zeroCreate<float> 12
        let golden = System.Math.PI * (3.0 - sqrt 5.0)

        for i in 0 .. samples - 1 do
            // Fibonacci-Kugel: gleichmaessig verteilte Richtungen.
            let y = 1.0 - (float i + 0.5) * 2.0 / float samples
            let r = sqrt (System.Math.Max(0.0, 1.0 - y * y))
            let phi = golden * float i
            let d = Vec3(r * cos phi, y, r * sin phi)
            let l = radiance view d
            let rgb = [| l.X; l.Y; l.Z |]
            for c in 0 .. 2 do
                sh.[c] <- sh.[c] + rgb.[c] / float samples
                sh.[3 + c] <- sh.[3 + c] + rgb.[c] * d.X * 2.0 / float samples
                sh.[6 + c] <- sh.[6 + c] + rgb.[c] * d.Y * 2.0 / float samples
                sh.[9 + c] <- sh.[9 + c] + rgb.[c] * d.Z * 2.0 / float samples

        sh

    /// Himmelslicht eines Sektors fuer den Shader. Der echte Himmel ist zu dunkel, um
    /// Rumpfschatten lesbar zu halten: Der Grundpegel folgt deshalb dem gestalteten
    /// Sektor-Ambient (`ambient`), Farbton und Richtungsverlauf kommen vom Himmel.
    let coefficients (sector: int) (ambient: Vec3) : float[] =
        let raw = project (view sector)
        let lum (i: int) = raw.[i] * 0.2126 + raw.[i + 1] * 0.7152 + raw.[i + 2] * 0.0722
        // Pegel wie das bisherige flache Ambient samt Fuelllicht (Mittel etwa 1,2 x Ambient).
        let target = (ambient.X * 0.2126 + ambient.Y * 0.7152 + ambient.Z * 0.0722) * 1.2
        let gain = target / System.Math.Max(1e-6, lum 0)
        let level = [| ambient.X; ambient.Y; ambient.Z |]
        let ambientLum = System.Math.Max(1e-6, target / 1.2)
        let out = Array.zeroCreate<float> 12
        let skyLum = System.Math.Max(1e-6, lum 0)

        for c in 0 .. 2 do
            // Farbton: zu 40 % Himmel, sonst das gestaltete Ambient (das auch den Nebel faerbt).
            let sky = raw.[c] * gain
            let authored = level.[c] / ambientLum * target
            out.[c] <- sky * 0.4 + authored * 0.6

        // Der Verlauf traegt die Stimmung des Sektors. Zu 70 % folgt er dem Farbton der
        // Konstanten (nur heller/dunkler), sonst kippen Spiegelungen ins Grelle.
        for axis in 1 .. 3 do
            let gradLum = lum (axis * 3)
            for c in 0 .. 2 do
                let hued = raw.[axis * 3 + c]
                let toned = gradLum * raw.[c] / skyLum
                out.[axis * 3 + c] <- (hued * 0.3 + toned * 0.7) * gain * 1.15

        // Kein Richtungsanteil darf die Gegenseite unter 50 % der Konstanten druecken.
        for c in 0 .. 2 do
            let g = sqrt (out.[3 + c] ** 2.0 + out.[6 + c] ** 2.0 + out.[9 + c] ** 2.0)
            let limit = out.[c] * 0.5
            if g > limit && g > 0.0 then
                for axis in 1 .. 3 do
                    out.[axis * 3 + c] <- out.[axis * 3 + c] * limit / g

        out

    /// Setzt das Himmelslicht des Sektors (nach Lights.setAmbient aufrufen, dessen Wert
    /// den Grundpegel bestimmt).
    let apply (lights: Lights) (sector: int) =
        let ambient = Vec3(float lights.Ambient.[0], float lights.Ambient.[1], float lights.Ambient.[2])
        Lights.setSky lights (coefficients sector ambient)
