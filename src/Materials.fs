namespace SpaceEngine

// =====================================================================================
//  Materials.fs  -  Oberflaechenklassen pro Vertex
//
//  Das Vertexformat bleibt bei 10 Floats. Die Klasse steckt im Vorzeichen des vierten
//  Farbkanals (Emissive):
//      e >= 0  ->  Lack mit Eigenleuchten e (wie bisher, auch Werte ueber 1)
//      e <  0  ->  -(Klasse + Eigenleuchten), Eigenleuchten dann 0 .. 0,95
//  Bestehende Meshes und GLB-Modelle sind damit ohne Aenderung Lack. Der Vertex-Shader
//  dekodiert den Kanal (Shaders.vertex), Tests lesen e > 0,5 weiter als "leuchtet".
// =====================================================================================

[<RequireQualifiedAccess>]
module Materials =
    /// Lackierte Platte (Standard): dielektrisch, Rauheit aus Batch-Glanz und Panellinien.
    [<Literal>]
    let Paint = 0

    /// Blankes Metall: Die Farbe ist die Reflexionsfarbe, kein Diffusanteil.
    [<Literal>]
    let Metal = 1

    /// Glas und Kanzeln: dunkles Inneres, spiegelt Himmel und Sonne.
    [<Literal>]
    let Glass = 2

    /// Gummi und Dichtungen: matt, kaum Glanz.
    [<Literal>]
    let Rubber = 3

    /// Hoechstes Eigenleuchten einer Nicht-Lack-Flaeche.
    [<Literal>]
    let MaxCodedGlow = 0.95

    /// Kodiert Klasse und Eigenleuchten in einen Kanalwert.
    let encode (surface: int) (emissive: float) : float =
        if surface <= Paint then emissive
        else -(float surface + System.Math.Max(0.0, System.Math.Min(MaxCodedGlow, emissive)))

    /// Klasse eines Kanalwerts.
    let surface (channel: float) : int =
        if channel >= 0.0 then Paint else int (floor (-channel))

    /// Eigenleuchten eines Kanalwerts.
    let emissive (channel: float) : float =
        if channel >= 0.0 then channel else -channel - floor (-channel)
